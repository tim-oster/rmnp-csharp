// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace rmnp
{
	class Connection
	{
		public enum State
		{
			DISCONNECTED,
			CONNECTING,
			CONNECTED
		}

		private RMNP protocol;
		internal State state;

		public Socket conn;
		public IPEndPoint addr;

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

		private readonly object sendQueueMutex = new object();
		internal readonly object recvQueueMutex = new object();
		private Queue<Packet> sendQueue;
		internal Queue<byte[]> recvQueue;

		// Values allow to store custom data for fast and easy packet handling.
		public Dictionary<string, object> values;

		public Connection()
		{
			this.state = State.DISCONNECTED;
			this.orderedChain = new Chain(Config.CfgMaxPacketChainLength);
			this.sendBuffer = new SendBuffer();
			this.receiveBuffer = new SequenceBuffer(Config.CfgSequenceBufferSize);
			this.congestionHandler = new CongestionHandler();
			this.sendQueue = new Queue<Packet>();
			this.recvQueue = new Queue<byte[]>();
			this.values = new Dictionary<string, object>();
		}

		public void Init(RMNP impl, IPEndPoint addr)
		{
			this.protocol = impl;
			this.conn = impl.socket;
			this.addr = addr;
			this.state = State.CONNECTING;

			long t = Util.CurrentTime();
			this.lastAckSendTime = t;
			this.lastResendTime = t;
			this.lastReceivedTime = t;
		}

		public void Reset()
		{
			this.protocol = null;
			this.state = State.DISCONNECTED;

			this.conn = null;
			this.addr = null;

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

			lock (this.sendQueueMutex) this.sendQueue.Clear();
			lock (this.recvQueueMutex) this.recvQueue.Clear();

			this.values.Clear();
		}

		public void StartRoutines()
		{
			this.thread = new Thread(new ThreadStart(() =>
			{
				Interlocked.Increment(ref Stats.StatRunningGoRoutines);

				while (this.isRunning)
				{
					try
					{
						this.SendUpdate();
						this.ReceiveUpdate();
						this.KeepAlive();
					}
					catch
					{
						Interlocked.Increment(ref Stats.StatGoRoutinePanics);
					}
				}

				Interlocked.Decrement(ref Stats.StatRunningGoRoutines);
			}));

			this.isRunning = true;
			this.thread.Start();
		}

		public void StopRoutines()
		{
			this.isRunning = false;
			this.thread.Join();
		}

		private void SendUpdate()
		{
			Thread.Sleep((int)Config.CfgUpdateLoopTimeout);

			lock (this.sendQueue)
			{
				if (this.sendQueue.Count > 0)
				{
					this.ProcessSend(this.sendQueue.Dequeue(), false);
				}
			}

			long currentTime = Util.CurrentTime();

			if (currentTime - this.lastResendTime > this.congestionHandler.resendTimeout)
			{
				this.lastResendTime = currentTime;

				this.sendBuffer.Iterate((i, data) =>
				{
					if (i >= this.congestionHandler.maxPacketResend) return SendBuffer.Operation.CANCEL;

					if (currentTime - data.sendTime > Config.CfgSendRemoveTimeout) return SendBuffer.Operation.DELETE;
					else this.ProcessSend(data.packet, true);

					return SendBuffer.Operation.CONTINUE;
				});
			}

			if (this.state != State.CONNECTED) return;

			if (currentTime - this.lastChainTime > Config.CfgChainSkipTimeout)
			{
				this.orderedChain.Skip();
				this.HandleNextChainSequence();
			}

			if (currentTime - this.lastAckSendTime > this.congestionHandler.reackTimeout)
			{
				this.SendAckPacket();

				if (this.pingPacketInterval % Config.CfgAutoPingInterval == 0)
				{
					this.SendLowLevelPacket(Packet.Descriptor.RELIABLE | Packet.Descriptor.ACK);
					this.pingPacketInterval = 0;
				}

				this.pingPacketInterval++;
			}
		}

		private void ReceiveUpdate()
		{
			lock (this.recvQueueMutex)
			{
				if (this.recvQueue.Count > 0)
				{
					this.ProcessReceive(this.recvQueue.Dequeue());
				}
			}
		}

		private void KeepAlive()
		{
			// case < -time.After(CfgTimeoutThreshold * (time.Millisecond / 2)):

			if (this.state == State.DISCONNECTED)
			{
				return;
			}

			long currentTime = Util.CurrentTime();

			if (currentTime - this.lastReceivedTime > Config.CfgTimeoutThreshold || this.GetPing() > Config.CfgMaxPing)
			{
				// needs to be executed in goroutine; otherwise this method could not exit and therefore deadlock
				// the connection's waitGroup
				Background.Execute(() => this.protocol.TimeoutClient(this));
			}
		}

		private void ProcessReceive(byte[] buffer)
		{
			this.lastReceivedTime = Util.CurrentTime();

			Packet p = new Packet();
			if (!p.Deserialize(buffer)) return;
			if (p.Flag(Packet.Descriptor.RELIABLE) && !this.HandleReliablePacket(p)) return;
			if (p.Flag(Packet.Descriptor.ACK) && !this.HandleAckPacket(p)) return;
			if (p.Flag(Packet.Descriptor.ORDERED) && !this.HandleOrderedPacket(p)) return;
			this.Process(p);
		}

		private bool HandleReliablePacket(Packet packet)
		{
			if (this.receiveBuffer.Get(packet.sequence))
			{
				return false;
			}

			this.receiveBuffer.Set(packet.sequence, true);

			if (Util.GreaterThanSequence(packet.sequence, this.remoteSequence) && Util.DifferenceSequence(packet.sequence, this.remoteSequence) <= Config.CfgMaxSkippedPackets)
			{
				this.remoteSequence = packet.sequence;
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
			if (packet.Flag(Packet.Descriptor.RELIABLE))
			{

				this.orderedChain.ChainPacket(packet);
				this.HandleNextChainSequence();
			}
			else
			{
				if (Util.GreaterThanSequence(packet.sequence, this.remoteUnreliableSequence))
				{
					this.remoteUnreliableSequence = packet.sequence;
					return true;
				}
			}

			return false;
		}

		private bool HandleAckPacket(Packet packet)
		{
			for (ushort i = 0; i <= 32; i++)
			{
				if (i == 0 || (packet.ackBits & (1 << (i - 1))) != 0)
				{
					ushort s = (ushort)(packet.ack - i);
					SendBuffer.SendPacket sp = this.sendBuffer.Retrieve(s);
					if (sp != null && !sp.noRTT) this.congestionHandler.Check(sp.sendTime);
				}
			}

			return true;
		}

		private void Process(Packet packet)
		{
			if (packet.data != null && packet.data.Length > 0)
			{
				this.protocol.onPacket(this, packet.data);
			}
		}

		private void HandleNextChainSequence()
		{
			this.lastChainTime = Util.CurrentTime();

			for (Chain.Link l = this.orderedChain.PopConsecutive(); l != null; l = l.next)
			{
				this.Process(l.packet);
			}
		}

		private void ProcessSend(Packet packet, bool resend)
		{
			if (!packet.Flag(Packet.Descriptor.RELIABLE) && this.congestionHandler.ShouldDropUnreliable())
			{
				return;
			}

			packet.protocolId = Config.CfgProtocolId;

			if (!resend)
			{
				if (packet.Flag(Packet.Descriptor.RELIABLE))
				{
					packet.sequence = this.localSequence;
					this.localSequence++;

					if (packet.Flag(Packet.Descriptor.ORDERED))
					{
						packet.order = this.orderedSequence;
						this.orderedSequence++;
					}

					this.sendBuffer.Add(packet, this.state != State.CONNECTED);
				}
				else if (packet.Flag(Packet.Descriptor.ORDERED))
				{
					packet.sequence = this.localUnreliableSequence;
					this.localUnreliableSequence++;
				}
			}

			if (packet.Flag(Packet.Descriptor.ACK))
			{
				this.lastAckSendTime = Util.CurrentTime();
				packet.ack = this.remoteSequence;
				packet.ackBits = this.ackBits;
			}

			packet.CalculateHash();
			byte[] buffer = packet.Serialize();
			this.protocol.writeFunc(this, ref buffer);
			Interlocked.Add(ref Stats.StatSendBytes, buffer.Length);
		}

		private void SendPacket(Packet packet)
		{
			lock (this.sendQueueMutex)
			{
				this.sendQueue.Enqueue(packet);
			}
		}

		internal void SendLowLevelPacket(Packet.Descriptor descriptor)
		{
			this.SendHighLevelPacket(descriptor, null);
		}

		private void SendHighLevelPacket(Packet.Descriptor descriptor, byte[] data)
		{
			Packet packet = new Packet();
			packet.descriptor = (byte)descriptor;
			packet.data = data;
			this.SendPacket(packet);
		}

		private void SendAckPacket()
		{
			this.SendLowLevelPacket(Packet.Descriptor.ACK);
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
			this.SendHighLevelPacket(Packet.Descriptor.ORDERED, data);
		}

		// SendReliable send the data and guarantees that the data arrives.
		// Note that packets are not guaranteed to arrive in the order they were sent.
		// This method is not 100% reliable. (Read more in README)
		public void SendReliable(byte[] data)
		{
			this.SendHighLevelPacket(Packet.Descriptor.RELIABLE | Packet.Descriptor.ACK, data);
		}

		// SendReliableOrdered is the same as SendReliable but guarantees that packets
		// will be processed in order.
		// This method is not 100% reliable. (Read more in README)
		public void SendReliableOrdered(byte[] data)
		{
			this.SendHighLevelPacket(Packet.Descriptor.RELIABLE | Packet.Descriptor.ACK | Packet.Descriptor.ORDERED, data);
		}

		// GetPing returns the current ping to this connection's socket
		public short GetPing()
		{
			return (short)(this.congestionHandler.rtt / 2);
		}
	}
}
