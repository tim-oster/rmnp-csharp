// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System;
using System.Text;

namespace rmnp_example
{
	class ClientExample
	{
		public static void Main(string[] args)
		{
			var client = new rmnp.Client("127.0.0.1:10001");
			
			client.ServerConnect = ServerConnect;
			client.ServerDisconnect = ServerDisconnect;
			client.ServerTimeout = ServerTimeout;
			client.PacketHandler = HandleClientPacket;

			client.Connect(new byte[] { 0, 1, 2 });

			while (true) ;
		}

		static void ServerConnect(rmnp.Connection conn, byte[] data)
		{
			Console.WriteLine("connected to server");
			conn.SendReliableOrdered(Encoding.UTF8.GetBytes("ping"));
		}

		static void ServerDisconnect(rmnp.Connection conn, byte[] data)
		{
			if (data != null)
			{
				Console.WriteLine("disconnected from server: " + Encoding.UTF8.GetString(data));
			}
		}

		static void ServerTimeout(rmnp.Connection conn, byte[] data)
		{
			Console.WriteLine("server timeout");
		}

		static void HandleClientPacket(rmnp.Connection conn, byte[] data, rmnp.Connection.Channel channel)
		{
			Console.WriteLine("'" + Encoding.UTF8.GetString(data) + "' on channel " + channel.ToString());
		}
	}
}