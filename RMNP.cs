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
	class RMNP
	{
		public delegate void ReadFunc(Socket socket, ref byte[] buffer, out int length, out IPEndPoint addr, out bool next);
		public delegate void WriteFunc(Connection connection, ref byte[] buffer);

		public IPEndPoint address;
		public Socket socket;

		private bool isRunning = false;
		private List<Thread> listeners;

		private readonly ReaderWriterLockSlim connectionsMutex = new ReaderWriterLockSlim();
		private Dictionary<uint, Connection> connections;
		public ReadFunc readFunc;
		public WriteFunc writeFunc;

		private Pool<byte[]> bufferPool;
		private Pool<Connection> connectionPool;

		// callbacks
		// for clients: only executed if client is still connected. if client disconnects callback will not be executed.
		public Action<Connection> onConnect;
		public Action<Connection> onDisconnect;
		public Action<Connection> onTimeout;
		public Func<Connection, IPEndPoint, byte[], bool> onValidation;
		public Action<Connection, byte[]> onPacket;

		protected void Init(string address)
		{
			if (!address.Contains(":")) throw new Exception("Port has to be specified");

			string[] data = address.Split(':');
			this.address = new IPEndPoint(IPAddress.Parse(data[0]), int.Parse(data[1]));

			this.listeners = new List<Thread>();
			this.connections = new Dictionary<uint, Connection>();

			this.bufferPool = new Pool<byte[]>();
			this.bufferPool.allocator = () => { return new byte[Config.CfgMTU]; };

			this.connectionPool = new Pool<Connection>();
			this.connectionPool.allocator = () => { return new Connection(); };
		}

		// is blocking call!
		internal void Destroy()
		{
			foreach (Connection conn in this.connections.Values) this.DisconnectClient(conn, true);

			this.isRunning = false;
			foreach (Thread thread in this.listeners) thread.Join();

			this.socket.Close();

			this.address = null;
			this.socket = null;
			this.listeners = null;

			this.connections = null;
			this.listeners = null;
		}

		internal void SetSocket()
		{
			this.socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			this.socket.Bind(this.address);
			this.socket.SendBufferSize = Config.CfgMTU;
			this.socket.ReceiveBufferSize = Config.CfgMTU;
		}

		internal void Listen()
		{
			for (int i = 0; i < Config.CfgParallelListenerCount; i++)
			{
				Thread thread = new Thread(new ThreadStart(this.ListeningWorker));
				this.listeners.Add(thread);
				thread.Start();
			}
		}

		private void ListeningWorker()
		{
			Interlocked.Increment(ref Stats.StatRunningGoRoutines);

			while (this.isRunning)
			{
				try
				{
					int length;
					IPEndPoint addr;
					bool next;

					byte[] buffer = this.bufferPool.Get();

					this.socket.ReceiveTimeout = 1000;
					this.readFunc(this.socket, ref buffer, out length, out addr, out next);

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
		}

		public void HandlePacket(IPEndPoint addr, byte[] packet)
		{
			uint hash = Util.AddrHash(addr);

			this.connectionsMutex.EnterReadLock();
			Connection connection = this.connections.ContainsKey(hash) ? this.connections[hash] : null;
			this.connectionsMutex.ExitReadLock();

			if (connection == null)
			{
				if ((packet[5] & (byte)Packet.Descriptor.CONNECT) == 0) return;

				int header = Packet.HeaderSize(packet);

				if (this.onValidation != null && !this.onValidation(null, addr, packet.Skip(header).ToArray()))
				{
					Interlocked.Increment(ref Stats.StatDeniedConnects);
					return;
				}

				connection = this.ConnectClient(addr);
			}

			// done this way to ensure that connect callback is executed on client-side
			if (connection.state != Connection.State.CONNECTED && (packet[5] & (byte)Packet.Descriptor.CONNECT) != 0)
			{
				if (this.onConnect != null) this.onConnect(connection);
				connection.state = Connection.State.CONNECTED;
			}

			if ((packet[5] & (byte)Packet.Descriptor.DISCONNECT) != 0)
			{
				this.DisconnectClient(connection, false);
				return;
			}

			Interlocked.Add(ref Stats.StatProcessedBytes, packet.Length);
			lock (connection.recvQueueMutex) connection.recvQueue.Enqueue(packet);
		}

		public Connection ConnectClient(IPEndPoint addr)
		{
			Interlocked.Increment(ref Stats.StatConnects);

			uint hash = Util.AddrHash(addr);

			Connection connection = this.connectionPool.Get();
			connection.Init(this, addr);

			this.connectionsMutex.EnterWriteLock();
			this.connections.Add(hash, connection);
			this.connectionsMutex.ExitWriteLock();

			connection.SendLowLevelPacket(Packet.Descriptor.RELIABLE | Packet.Descriptor.CONNECT);
			connection.StartRoutines();

			return connection;
		}

		public void DisconnectClient(Connection connection, bool shutdown)
		{
			if (connection.state == Connection.State.DISCONNECTED) return;

			Interlocked.Increment(ref Stats.StatDisconnects);

			connection.state = Connection.State.DISCONNECTED;

			// send more than necessary so that the packet hopefully arrives
			for (int i = 0; i < 10; i++) connection.SendLowLevelPacket(Packet.Descriptor.DISCONNECT);

			// give the channel some time to process the packets
			Thread.Sleep(20);

			connection.StopRoutines();

			uint hash = Util.AddrHash(connection.addr);

			this.connectionsMutex.EnterWriteLock();
			this.connections.Remove(hash);
			this.connectionsMutex.ExitWriteLock();

			if (!shutdown && this.onDisconnect != null) this.onDisconnect(connection);

			connection.Reset();
			this.connectionPool.Put(connection);
		}

		public void TimeoutClient(Connection connection)
		{
			Interlocked.Increment(ref Stats.StatTimeouts);
			if (this.onTimeout != null) this.onTimeout(connection);
			this.DisconnectClient(connection, false);
		}
	}
}
