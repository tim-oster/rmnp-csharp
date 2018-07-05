// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System;
using System.Net;
using System.Net.Sockets;

#pragma warning disable 649

namespace rmnp
{
	public class Client : RMNP
	{
		// Server is the Connection to the server (nil if not connected).
		public Connection Server;

		// ServerConnect is called when a connection to the server was established.
		public Action<Connection, byte[]> ServerConnect;

		// ServerDisconnect is called when the server disconnected the client.
		public Action<Connection, byte[]> ServerDisconnect;

		// ServerTimeout is called when the connection to the server timed out.
		public Action<Connection, byte[]> ServerTimeout;

		// PacketHandler is called when packets arrive to handle the received data.
		public Action<Connection, byte[], Connection.Channel> PacketHandler;

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

			this.writeFunc = (Socket socket, IPEndPoint addr, ref byte[] buffer) =>
			{
				socket.SendTo(buffer, addr);
			};

			this.OnConnect = (connection, packet) =>
			{
				if (this.ServerConnect != null) this.ServerConnect(connection, packet);
			};

			this.OnDisconnect = (connection, packet) =>
			{
				if (this.ServerDisconnect != null) this.ServerDisconnect(connection, packet);
				Background.Execute(() => this.Destroy());
			};

			this.OnTimeout = (connection, packet) =>
			{
				if (this.ServerTimeout != null) this.ServerTimeout(connection, packet);
			};

			this.OnValidation = (addr, packet) =>
			{
				return false;
			};

			this.OnPacket = (connection, packet, channel) =>
			{
				if (this.PacketHandler != null) this.PacketHandler(connection, packet, channel);
			};

			this.Init(server);
		}

		// Connect tries to connect to the server specified in the NewClient call. This call is async.
		// On successful connection the Client.ServerConnect callback is invoked.
		// If no connection can be established after CfgTimeoutThreshold milliseconds
		// Client.ServerTimeout is called.
		public void Connect(byte[] data)
		{
			this.SetSocket(new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp));
			this.Listen();
			this.Server = this.ConnectClient(this.Address, data);
			this.Server.IsServer = true;
		}

		// Disconnect immediately disconnects from the server. It invokes no callbacks.
		// This call could take some time because it waits for goroutines to exit.
		public void Disconnect()
		{
			this.Destroy();
			this.Server = null;
		}
	}
}

#pragma warning restore 649
