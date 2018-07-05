// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace rmnp
{
	public class Connection
	{
		internal enum ConnectionState
		{
			DISCONNECTED,
			CONNECTING,
			CONNECTED
		}

		public enum Channel
		{
			UNRELIABLE,
			UNRELIABLE_ORDERED,
			RELIABLE,
			RELIABLE_ORDERED
		}

		private RMNP protocol;

		private readonly ReaderWriterLockSlim stateMutex = new ReaderWriterLockSlim();
		private ConnectionState state;

		public Socket Conn;
		public IPEndPoint Addr;
		public bool IsServer;

		// for threading
		private bool isRunning;
		private Thread thread;

		// for reliable packets
		private ushort localSequence;
		private ushort remoteSequence;
		private uint ackBits;
		private Chain orderedChain;
		private byte orderedSequence;

		// for unreliable ordered packets
		private ushort localUnreliableSequence;
		private ushort remoteUnreliableSequence;

		private long lastAckSendTime;
		private long lastResendTime;
		private long lastReceivedTime;
		private long lastChainTime;
		private byte pingPacketInterval;
		private SendBuffer sendBuffer;
		private SequenceBuffer receiveBuffer;
		private CongestionHandler congestionHandler;

		private DropChannel<Packet> sendQueue;
		// no need for recvQeue because packets are handled in place

		private readonly object valuesMutex = new object();
		private Dictionary<byte, object> values;

		internal Connection()
		{
			this.state = ConnectionState.DISCONNECTED;
			this.orderedChain = new Chain(Config.CfgMaxPacketChainLength);
			this.sendBuffer = new SendBuffer();
			this.receiveBuffer = new SequenceBuffer(Config.CfgSequenceBufferSize);
			this.congestionHandler = new CongestionHandler();
			this.sendQueue = new DropChannel<Packet>(Config.CfgMaxSendReceiveQueueSize);
			this.values = new Dictionary<byte, object>();
		}

		internal void Init(RMNP impl, IPEndPoint addr)
		{
			this.protocol = impl;
			this.Conn = impl.Socket;
			this.Addr = addr;
			this.state = ConnectionState.CONNECTING;

			long t = Util.CurrentTime();
			this.lastAckSendTime = t;
			this.lastResendTime = t;
			this.lastReceivedTime = t;
		}

		internal void Reset()
		{
			this.protocol = null;
			this.state = ConnectionState.DISCONNECTED;

			this.Conn = null;
			this.Addr = null;
			this.IsServer = false;

			this.orderedChain.Reset();
			this.sendBuffer.Reset();
			this.receiveBuffer.Reset();
			this.congestionHandler.Reset();

			this.localSequence = 0;
			this.remoteSequence = 0;
			this.ackBits = 0;
			this.orderedSequence = 0;

			this.localUnreliableSequence = 0;
			this.remoteUnreliableSequence = 0;

			this.lastAckSendTime = 0;
			this.lastResendTime = 0;
			this.lastReceivedTime = 0;
			this.lastChainTime = 0;
			this.pingPacketInterval = 0;

			this.sendQueue.Clear();
			this.values.Clear();
		}

		internal void StartRoutines()
		{
			this.thread = new Thread(() =>
			{
				Interlocked.Increment(ref Stats.StatRunningGoRoutines);

				while (this.isRunning)
				{
					try
					{
						this.SendUpdate();
						//this.ReceiveUpdate();
						this.KeepAlive();
					}
					catch
					{
						Interlocked.Increment(ref Stats.StatGoRoutinePanics);
					}
				}

				Interlocked.Decrement(ref Stats.StatRunningGoRoutines);
			});

			this.isRunning = true;
			this.thread.Start();
		}

		internal void StopRoutines()
		{
			this.isRunning = false;
			this.thread.Join();
		}

		internal void OnSuccessfulConnect()
		{
			this.sendBuffer.Reset();
			this.sendQueue.Clear();
		}

		private void SendUpdate()
		{
			Packet toSend = this.sendQueue.Pop();
			if (toSend != null) this.ProcessSend(toSend, false);
			else Thread.Sleep((int)Config.CfgUpdateLoopTimeout);

			long currentTime = Util.CurrentTime();
			if (currentTime - this.lastResendTime > this.congestionHandler.ResendTimeout)
			{
				this.lastResendTime = currentTime;

				this.sendBuffer.Iterate((i, data) =>
				{
					if (i >= this.congestionHandler.MaxPacketResend) return SendBuffer.Operation.CANCEL;

					if (currentTime - data.sendTime > Config.CfgSendRemoveTimeout) return SendBuffer.Operation.DELETE;
					else this.ProcessSend(data.packet, true);

					return SendBuffer.Operation.CONTINUE;
				});
			}

			if (this.GetState() != ConnectionState.CONNECTED) return;

			if (currentTime - this.lastChainTime > Config.CfgChainSkipTimeout)
			{
				this.orderedChain.Skip();
				this.HandleNextChainSequence();
			}

			if (currentTime - this.lastAckSendTime > this.congestionHandler.ReackTimeout)
			{
				this.SendAckPacket();

				if (this.pingPacketInterval % Config.CfgAutoPingInterval == 0)
				{
					this.SendLowLevelPacket(Packet.PacketDescriptor.RELIABLE | Packet.PacketDescriptor.ACK);
					this.pingPacketInterval = 0;
				}

				this.pingPacketInterval++;
			}
		}

		private void ReceiveUpdate()
		{
			// not used (is implemented as direct call instead of threading)
		}

		private void KeepAlive()
		{
			// case < -time.After((CfgTimeoutThreshold / 2) * time.Millisecond):

			if (this.GetState() == ConnectionState.DISCONNECTED)
			{
				return;
			}

			long currentTime = Util.CurrentTime();

			if (currentTime - this.lastReceivedTime > Config.CfgTimeoutThreshold || this.GetPing() > Config.CfgMaxPing)
			{
				// needs to be executed in goroutine; otherwise this method could not exit and therefore deadlock
				// the connection's waitGroup
				Background.Execute(() => { if (this.protocol != null) this.protocol.DisconnectClient(this, RMNP.DisconnectType.TIMEOUT, null); });
			}
		}

		internal void ProcessReceive(byte[] buffer)
		{
			this.lastReceivedTime = Util.CurrentTime();

			Packet p = new Packet();
			if (!p.Deserialize(buffer)) return;
			if (p.Flag(Packet.PacketDescriptor.RELIABLE) && !this.HandleReliablePacket(p)) return;
			if (p.Flag(Packet.PacketDescriptor.ACK) && !this.HandleAckPacket(p)) return;
			if (p.Flag(Packet.PacketDescriptor.ORDERED) && !this.HandleOrderedPacket(p)) return;

			Channel channel;

			if (p.Flag(Packet.PacketDescriptor.RELIABLE))
			{
				if (p.Flag(Packet.PacketDescriptor.ORDERED)) channel = Channel.RELIABLE_ORDERED;
				else channel = Channel.RELIABLE;
			}
			else
			{
				if (p.Flag(Packet.PacketDescriptor.ORDERED)) channel = Channel.UNRELIABLE_ORDERED;
				else channel = Channel.UNRELIABLE;
			}

			this.Process(p, channel);
		}

		private bool HandleReliablePacket(Packet packet)
		{
			if (this.receiveBuffer.Get(packet.Sequence))
			{
				return false;
			}

			this.receiveBuffer.Set(packet.Sequence, true);

			if (Util.GreaterThanSequence(packet.Sequence, this.remoteSequence) && Util.DifferenceSequence(packet.Sequence, this.remoteSequence) <= Config.CfgMaxSkippedPackets)
			{
				this.remoteSequence = packet.Sequence;
			}

			this.ackBits = 0;
			for (int i = 1; i <= 32; i++)
			{
				if (this.receiveBuffer.Get((ushort)(this.remoteSequence - i)))
				{
					this.ackBits |= (uint)(1 << (i - 1));
				}
			}

			this.SendAckPacket();

			return true;
		}

		private bool HandleOrderedPacket(Packet packet)
		{
			if (packet.Flag(Packet.PacketDescriptor.RELIABLE))
			{
				this.orderedChain.ChainPacket(packet);
				this.HandleNextChainSequence();
			}
			else
			{
				if (Util.GreaterThanSequence(packet.Sequence, this.remoteUnreliableSequence))
				{
					this.remoteUnreliableSequence = packet.Sequence;
					return true;
				}
			}

			return false;
		}

		private bool HandleAckPacket(Packet packet)
		{
			for (ushort i = 0; i <= 32; i++)
			{
				if (i == 0 || (packet.AckBits & (1 << (i - 1))) != 0)
				{
					ushort s = (ushort)(packet.Ack - i);
					SendBuffer.SendPacket sp = this.sendBuffer.Retrieve(s);
					if (sp != null && !sp.noRTT) this.congestionHandler.Check(sp.sendTime);
				}
			}

			return true;
		}

		private void Process(Packet packet, Channel channel)
		{
			if (packet.Data != null && packet.Data.Length > 0)
			{
				this.protocol.OnPacket(this, packet.Data, channel);
			}
		}

		private void HandleNextChainSequence()
		{
			this.lastChainTime = Util.CurrentTime();

			for (Chain.Link l = this.orderedChain.PopConsecutive(); l != null; l = l.next)
			{
				this.Process(l.packet, Channel.RELIABLE_ORDERED);
			}
		}

		private void ProcessSend(Packet packet, bool resend)
		{
			if (!packet.Flag(Packet.PacketDescriptor.RELIABLE) && this.congestionHandler.ShouldDropUnreliable())
			{
				return;
			}

			packet.ProtocolId = Config.CfgProtocolId;

			if (!resend)
			{
				if (packet.Flag(Packet.PacketDescriptor.RELIABLE))
				{
					packet.Sequence = this.localSequence;
					this.localSequence++;

					if (packet.Flag(Packet.PacketDescriptor.ORDERED))
					{
						packet.Order = this.orderedSequence;
						this.orderedSequence++;
					}

					this.sendBuffer.Add(packet, this.GetState() != ConnectionState.CONNECTED);
				}
				else if (packet.Flag(Packet.PacketDescriptor.ORDERED))
				{
					packet.Sequence = this.localUnreliableSequence;
					this.localUnreliableSequence++;
				}
			}

			if (packet.Flag(Packet.PacketDescriptor.ACK))
			{
				this.lastAckSendTime = Util.CurrentTime();
				packet.Ack = this.remoteSequence;
				packet.AckBits = this.ackBits;
			}

			packet.CalculateHash();
			byte[] buffer = packet.Serialize();
			this.protocol.writeFunc(this.Conn, this.Addr, ref buffer);
			Interlocked.Add(ref Stats.StatSendBytes, buffer.Length);
		}

		private void SendPacket(Packet packet)
		{
			this.sendQueue.Push(packet);
		}

		internal void SendLowLevelPacket(Packet.PacketDescriptor descriptor)
		{
			this.SendHighLevelPacket(descriptor, null);
		}

		internal void SendHighLevelPacket(Packet.PacketDescriptor descriptor, byte[] data)
		{
			Packet packet = new Packet();
			packet.Descriptor = (byte)descriptor;
			packet.Data = data;
			this.SendPacket(packet);
		}

		private void SendAckPacket()
		{
			this.SendLowLevelPacket(Packet.PacketDescriptor.ACK);
		}

		internal ConnectionState GetState()
		{
			this.stateMutex.EnterReadLock();
			ConnectionState state = this.state;
			this.stateMutex.ExitReadLock();
			return state;
		}

		internal void SetState(ConnectionState state)
		{
			this.stateMutex.EnterWriteLock();
			this.state = state;
			this.stateMutex.ExitWriteLock();
		}

		internal bool UpdateState(ConnectionState state)
		{
			this.stateMutex.EnterWriteLock();

			if (this.state != state)
			{
				this.state = state;
				this.stateMutex.ExitWriteLock();
				return true;
			}

			this.stateMutex.ExitWriteLock();
			return false;
		}

		// SendUnreliable sends the data with no guarantee whether it arrives or not.
		// Note that the packets or not guaranteed to arrive in order.
		public void SendUnreliable(byte[] data)
		{
			this.SendHighLevelPacket(0, data);
		}

		// SendUnreliableOrdered is the same as SendUnreliable but guarantees that if packets
		// do not arrive chronologically the receiver only accepts newer packets and discards older
		// ones.
		public void SendUnreliableOrdered(byte[] data)
		{
			this.SendHighLevelPacket(Packet.PacketDescriptor.ORDERED, data);
		}

		// SendReliable send the data and guarantees that the data arrives.
		// Note that packets are not guaranteed to arrive in the order they were sent.
		// This method is not 100% reliable. (Read more in README)
		public void SendReliable(byte[] data)
		{
			this.SendHighLevelPacket(Packet.PacketDescriptor.RELIABLE | Packet.PacketDescriptor.ACK, data);
		}

		// SendReliableOrdered is the same as SendReliable but guarantees that packets
		// will be processed in order.
		// This method is not 100% reliable. (Read more in README)
		public void SendReliableOrdered(byte[] data)
		{
			this.SendHighLevelPacket(Packet.PacketDescriptor.RELIABLE | Packet.PacketDescriptor.ACK | Packet.PacketDescriptor.ORDERED, data);
		}

		// SendOnChannel sends the data on the given channel using the dedicated send method
		// for each channel
		public void SendOnChannel(Channel channel, byte[] data)
		{
			switch (channel)
			{
				case Channel.UNRELIABLE:
					this.SendUnreliable(data);
					break;
				case Channel.UNRELIABLE_ORDERED:
					this.SendUnreliableOrdered(data);
					break;
				case Channel.RELIABLE:
					this.SendReliable(data);
					break;
				case Channel.RELIABLE_ORDERED:
					this.SendReliableOrdered(data);
					break;
			}
		}

		// GetPing returns the current ping to this connection's socket
		public short GetPing()
		{
			return (short)(this.congestionHandler.RTT / 2);
		}

		// Disconnect disconnects the connection
		public void Disconnect(byte[] packet)
		{
			Background.Execute(() => { this.protocol.DisconnectClient(this, RMNP.DisconnectType.DEFAULT, packet); });
		}

		// Set stores a value associated with the given key in this connection instance.
		// It is thread safe.
		public void Set(byte key, object value)
		{
			lock (this.valuesMutex)
			{
				this.values[key] = value;
			}
		}
		
		// TrySet stores a value associated with the given key in this connection instance if does not exist yet.
		// It returns whether it was able to set the value. It is thread safe.
		public bool TrySet(byte key, object value)
		{
			lock (this.valuesMutex)
			{
				if (this.values.ContainsKey(key)) return false;

				this.values[key] = value;
				return true;
			}
		}

		// Get retrieves a stored value from this connection instance and returns null if it does not exist.
		// It is thread safe.
		public object Get(byte key, object fallback = null)
		{
			lock (this.valuesMutex)
			{
				if (!this.values.ContainsKey(key)) return fallback;
				return this.values[key];
			}
		}

		// Del deletes a stored value from this connection instance.
		// It is thread safe.
		public void Del(byte key)
		{
			lock (this.valuesMutex)
			{
				if (this.values.ContainsKey(key)) this.values.Remove(key);
			}
		}
	}
}
