// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System;
using System.Net;
using System.Net.Sockets;

namespace rmnp
{
	class Client : RMNP
	{
		// Server is the Connection to the server (nil if not connected).
		public Connection Server;

		// ServerConnect is called when a connection to the server was established.
		public Action<Connection> ServerConnect;

		// ServerDisconnect is called when the server disconnected the client.
		public Action<Connection> ServerDisconnect;

		// ServerTimeout is called when the connection to the server timed out.
		public Action<Connection> ServerTimeout;

		// PacketHandler is called when packets arrive to handle the received data.
		public Action<Connection, byte[]> PacketHandler;

		// NewClient creates and returns a new Client instance that will try to connect
		// to the given server address. It does not connect automatically.
		public Client(string server)
		{
			this.readFunc = (Socket socket, ref byte[] buffer, out int length, out IPEndPoint addr, out bool next) =>
			{
				try
				{
					length = socket.Receive(buffer);
					addr = this.Address;
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

			this.OnConnect = (connection) =>
			{
				if (this.ServerConnect != null) this.ServerConnect(connection);
			};

			this.OnDisconnect = (connection) =>
			{
				if (this.ServerDisconnect != null) this.ServerDisconnect(connection);
				this.Destroy();
			};

			this.OnTimeout = (connection) =>
			{
				if (this.ServerTimeout != null) this.ServerTimeout(connection);
			};

			this.OnValidation = (connection, addr, packet) =>
			{
				return false;
			};

			this.OnPacket = (connection, packet) =>
			{
				if (this.PacketHandler != null) this.PacketHandler(connection, packet);
			};

			this.Init(server);
		}

		// Connect tries to connect to the server specified in the NewClient call. This call is async.
		// On successful connection the Client.ServerConnect callback is invoked.
		// If no connection can be established after CfgTimeoutThreshold milliseconds
		// Client.ServerTimeout is called.
		public void Start()
		{
			this.SetSocket(new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp));
			this.Listen();
			this.Server = this.ConnectClient(this.Address);
		}

		// Disconnect immediately disconnects from the server. It invokes no callbacks.
		// This call could take some time because it waits for goroutines to exit.
		public void Stop()
		{
			this.Destroy();
			this.Server = null;
		}
	}
}
