// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace rmnp
{
	public class RMNP
	{
		internal enum DisconnectType
		{
			DEFAULT,
			SHUTDOWN,
			TIMEOUT
		}

		internal delegate void ReadFunc(Socket socket, ref byte[] buffer, out int length, out IPEndPoint addr, out bool next);
		internal delegate void WriteFunc(Socket socket, IPEndPoint addr, ref byte[] buffer);

		internal IPEndPoint Address;
		internal Socket Socket;

		private bool isRunning = false;
		private WaitGroup waitGroup = new WaitGroup();
		private List<Thread> listeners;

		private ExecGuard connectGuard;
		private readonly ReaderWriterLockSlim connectionsMutex = new ReaderWriterLockSlim();
		private Dictionary<uint, Connection> connections;
		internal ReadFunc readFunc;
		internal WriteFunc writeFunc;

		private Pool<byte[]> bufferPool;
		private Pool<Connection> connectionPool;

		// callbacks
		// for clients: only executed if client is still connected. if client disconnects callback will not be executed.
		internal Action<Connection, byte[]> OnConnect;
		internal Action<Connection, byte[]> OnDisconnect;
		internal Action<Connection, byte[]> OnTimeout;
		internal Func<IPEndPoint, byte[], bool> OnValidation;
		internal Action<Connection, byte[], Connection.Channel> OnPacket;

		protected RMNP() { }

		protected void Init(string address)
		{
			if (!address.Contains(":")) throw new Exception("Port has to be specified");

			string[] data = address.Split(':');
			this.Address = new IPEndPoint(IPAddress.Parse(data[0]), int.Parse(data[1]));

			this.listeners = new List<Thread>();
			this.connectGuard = new ExecGuard();
			this.connections = new Dictionary<uint, Connection>();

			this.bufferPool = new Pool<byte[]>();
			this.bufferPool.Allocator = () => { return new byte[Config.CfgMTU]; };

			this.connectionPool = new Pool<Connection>();
			this.connectionPool.Allocator = () => { return new Connection(); };

			Background.Start();
		}

		// is blocking call!
		protected void Destroy()
		{
			if (this.Address == null) return;

			this.isRunning = false;
			this.waitGroup.Wait();

			this.connectionsMutex.EnterWriteLock();
			foreach (Connection conn in this.connections.Values) this.DisconnectClient(conn, DisconnectType.SHUTDOWN, null);
			this.connections.Clear();
			this.connectionsMutex.ExitWriteLock();

			this.Socket.Close();
			Background.Stop();

			this.Address = null;
			this.Socket = null;
			this.listeners = null;

			this.connectGuard = null;
			this.connections = null;
			this.listeners = null;
		}

		protected void SetSocket(Socket socket)
		{
			this.Socket = socket;
			this.Socket.SendBufferSize = Config.CfgMTU;
			this.Socket.ReceiveBufferSize = Config.CfgMTU;
			this.Socket.Blocking = false;
		}

		protected void Listen()
		{
			this.isRunning = true;

			for (int i = 0; i < Config.CfgParallelListenerCount; i++)
			{
				Thread thread = new Thread(this.ListeningWorker);
				this.listeners.Add(thread);
				thread.Start();
			}
		}

		private void ListeningWorker()
		{
			this.waitGroup.Start();
			Interlocked.Increment(ref Stats.StatRunningGoRoutines);

			while (this.isRunning)
			{
				try
				{
					int length;
					IPEndPoint addr;
					bool next;

					byte[] buffer = this.bufferPool.Get();

					this.Socket.ReceiveTimeout = 1000;
					this.readFunc(this.Socket, ref buffer, out length, out addr, out next);

					if (!next)
					{
						this.bufferPool.Put(buffer);
						continue;
					}

					byte[] packet = new byte[length];
					Buffer.BlockCopy(buffer, 0, packet, 0, length);
					this.bufferPool.Put(buffer);

					if (!Packet.ValidateHeader(packet)) continue;

					Interlocked.Add(ref Stats.StatReceivedBytes, length);

					this.HandlePacket(addr, packet);
				}
				catch
				{
					Interlocked.Increment(ref Stats.StatGoRoutinePanics);
				}
			}

			Interlocked.Decrement(ref Stats.StatRunningGoRoutines);
			this.waitGroup.End();
		}

		private void HandlePacket(IPEndPoint addr, byte[] packet)
		{
			uint hash = Util.AddrHash(addr);

			this.connectionsMutex.EnterReadLock();
			Connection connection = this.connections.ContainsKey(hash) ? this.connections[hash] : null;
			this.connectionsMutex.ExitReadLock();

			if (connection == null)
			{
				if ((packet[5] & (byte)Packet.PacketDescriptor.CONNECT) == 0) return;
				if (!this.connectGuard.TryExecute(hash)) return;

				int header = Packet.HeaderSize(packet);
				if (this.OnValidation != null && !this.OnValidation(addr, packet.Skip(header).ToArray()))
				{
					Interlocked.Increment(ref Stats.StatDeniedConnects);
					return;
				}

				connection = this.ConnectClient(addr, null);
			}

			// done this way to ensure that connect callback is executed on client-side
			if ((packet[5] & (byte)Packet.PacketDescriptor.CONNECT) != 0)
			{
				if (connection.UpdateState(Connection.ConnectionState.CONNECTED))
				{
					int header = Packet.HeaderSize(packet);

					// clear buffers so all remaining connection packets are deleted
					if (connection.IsServer) connection.OnSuccessfulConnect();
					
					if (this.OnConnect != null) this.OnConnect(connection, packet.Skip(header).ToArray());
					this.connectGuard.Finish(hash);
				}

				return;
			}

			if ((packet[5] & (byte)Packet.PacketDescriptor.DISCONNECT) != 0)
			{
				int header = Packet.HeaderSize(packet);
				this.DisconnectClient(connection, DisconnectType.DEFAULT, packet.Skip(header).ToArray());
				return;
			}

			Interlocked.Add(ref Stats.StatProcessedBytes, packet.Length);
			connection.ProcessReceive(packet);
		}

		internal Connection ConnectClient(IPEndPoint addr, byte[] data)
		{
			Interlocked.Increment(ref Stats.StatConnects);

			uint hash = Util.AddrHash(addr);

			Connection connection = this.connectionPool.Get();
			connection.Init(this, addr);

			this.connectionsMutex.EnterWriteLock();
			this.connections.Add(hash, connection);
			this.connectionsMutex.ExitWriteLock();

			connection.SendHighLevelPacket(Packet.PacketDescriptor.RELIABLE | Packet.PacketDescriptor.CONNECT, data);
			connection.StartRoutines();

			return connection;
		}

		internal void DisconnectClient(Connection connection, DisconnectType disconnectType, byte[] packet)
		{
			if (!connection.UpdateState(Connection.ConnectionState.DISCONNECTED)) return;

			if (disconnectType == DisconnectType.TIMEOUT)
			{
				Interlocked.Increment(ref Stats.StatTimeouts);
				if (this.OnTimeout != null) this.OnTimeout(connection, null);
			}

			Interlocked.Increment(ref Stats.StatDisconnects);

			// send more than necessary so that the packet hopefully arrives
			for (int i = 0; i < 10; i++) connection.SendHighLevelPacket(Packet.PacketDescriptor.DISCONNECT, packet);

			// give the channel some time to process the packets
			Thread.Sleep(20);

			connection.StopRoutines();

			if (disconnectType != DisconnectType.SHUTDOWN)
			{
				uint hash = Util.AddrHash(connection.Addr);

				this.connectionsMutex.EnterWriteLock();
				this.connections.Remove(hash);
				this.connectionsMutex.ExitWriteLock();

				if (this.OnDisconnect != null) this.OnDisconnect(connection, packet);
			}

			connection.Reset();
			this.connectionPool.Put(connection);
		}
	}
}
