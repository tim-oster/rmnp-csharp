// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System;
using System.Net;
using System.Net.Sockets;

namespace rmnp
{
	class Server : RMNP
	{
		// ClientConnect is invoked when a new client connects.
		public Action<Connection, byte[]> ClientConnect;

		// ClientDisconnect is invoked when a client disconnects.
		public Action<Connection, byte[]> ClientDisconnect;

		// ClientTimeout is called when a client timed out. After that ClientDisconnect will be called.
		public Action<Connection, byte[]> ClientTimeout;

		// ClientValidation is called when a new client connects to either accept or deny the connection attempt.
		public Func<IPEndPoint, byte[], bool> ClientValidation;

		// PacketHandler is called when packets arrive to handle the received data.
		public Action<Connection, byte[], Connection.Channel> PacketHandler;

		// NewServer creates and returns a new Server instance that will listen on the
		// specified address and port. It does not start automatically.
		public Server(string address)
		{
			this.readFunc = (Socket socket, ref byte[] buffer, out int length, out IPEndPoint addr, out bool next) =>
			{
				try
				{
					EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
					length = socket.ReceiveFrom(buffer, ref ep);
					addr = (IPEndPoint)ep;
					next = true;
				}
				catch
				{
					length = 0;
					addr = null;
					next = false;
				}
			};

			this.writeFunc = (Connection connection, ref byte[] buffer) =>
			{
				connection.Conn.SendTo(buffer, connection.Addr);
			};

			this.OnConnect = (connection, packet) =>
			{
				if (this.ClientConnect != null) this.ClientConnect(connection, packet);
			};

			this.OnDisconnect = (connection, packet) =>
			{
				if (this.ClientDisconnect != null) this.ClientDisconnect(connection, packet);
			};

			this.OnTimeout = (connection, packet) =>
			{
				if (this.ClientTimeout != null) this.ClientTimeout(connection, packet);
			};

			this.OnValidation = (addr, packet) =>
			{
				return this.ClientValidation != null ? this.ClientValidation(addr, packet) : true;
			};

			this.OnPacket = (connection, packet, channel) =>
			{
				if (this.PacketHandler != null) this.PacketHandler(connection, packet, channel);
			};

			this.Init(address);
		}

		// Start starts the server asynchronously. It invokes no callbacks but
		// the server is guaranteed to be running after this call.
		public void Start()
		{
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(this.Address);
			this.Listen();
		}

		// Stop stops the server and disconnects all clients. It invokes no callbacks.
		// This call could take some time because it waits for goroutines to exit.
		public void Stop()
		{
			this.Destroy();
		}
	}
}
