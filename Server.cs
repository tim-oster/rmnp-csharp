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
		public Action<Connection> ClientConnect;

		// ClientDisconnect is invoked when a client disconnects.
		public Action<Connection> ClientDisconnect;

		// ClientTimeout is called when a client timed out. After that ClientDisconnect will be called.
		public Action<Connection> ClientTimeout;

		// ClientValidation is called when a new client connects to either accept or deny the connection attempt.
		public Func<Connection, IPEndPoint, byte[], bool> ClientValidation;

		// PacketHandler is called when packets arrive to handle the received data.
		public Action<Connection, byte[]> PacketHandler;

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
				connection.conn.SendTo(buffer, connection.addr);
			};

			this.onConnect = (connection) =>
			{
				if (this.ClientConnect != null) this.ClientConnect(connection);
			};

			this.onDisconnect = (connection) =>
			{
				if (this.ClientDisconnect != null) this.ClientDisconnect(connection);
			};

			this.onTimeout = (connection) =>
			{
				if (this.ClientTimeout != null) this.ClientTimeout(connection);
			};

			this.onValidation = (connection, addr, packet) =>
			{
				return this.ClientValidation != null ? this.ClientValidation(connection, addr, packet) : true;
			};

			this.onPacket = (connection, packet) =>
			{
				if (this.PacketHandler != null) this.PacketHandler(connection, packet);
			};

			this.Init(address);
		}

		// Start starts the server asynchronously. It invokes no callbacks but
		// the server is guaranteed to be running after this call.
		public void Start()
		{
			this.SetSocket();
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
