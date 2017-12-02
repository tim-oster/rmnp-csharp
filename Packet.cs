// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System;
using System.IO;

namespace rmnp
{
	class Packet
	{
		internal enum PacketDescriptor : byte
		{
			RELIABLE = (1 << 0),
			ACK = (1 << 1),
			ORDERED = (1 << 2),

			CONNECT = (1 << 3),
			DISCONNECT = (1 << 4)
		}

		internal byte ProtocolId;
		internal uint Crc32;
		internal byte Descriptor;

		internal ushort Sequence;
		internal byte Order;

		internal ushort Ack;
		internal uint AckBits;

		internal byte[] Data;

		internal byte[] Serialize()
		{
			using (MemoryStream stream = new MemoryStream())
			{
				using (BinaryWriter writer = new BinaryWriter(stream))
				{
					writer.Write(this.ProtocolId);
					writer.Write(this.Crc32);
					writer.Write(this.Descriptor);

					if (this.Flag(PacketDescriptor.RELIABLE) || this.Flag(PacketDescriptor.ORDERED))
					{
						writer.Write(this.Sequence);
					}

					if (this.Flag(PacketDescriptor.RELIABLE) && this.Flag(PacketDescriptor.ORDERED))
					{
						writer.Write(this.Order);
					}

					if (this.Flag(PacketDescriptor.ACK))
					{
						writer.Write(this.Ack);
						writer.Write(this.AckBits);
					}

					if (this.Data != null && this.Data.Length > 0)
					{
						writer.Write(this.Data);
					}
				}

				return stream.ToArray();
			}
		}

		internal bool Deserialize(byte[] packet)
		{
			try
			{
				using (MemoryStream stream = new MemoryStream(packet))
				using (BinaryReader reader = new BinaryReader(stream))
				{
					this.ProtocolId = reader.ReadByte();
					this.Crc32 = reader.ReadUInt32();
					this.Descriptor = reader.ReadByte();

					if (this.Flag(PacketDescriptor.RELIABLE) || this.Flag(PacketDescriptor.ORDERED))
					{
						this.Sequence = reader.ReadUInt16();
					}

					if (this.Flag(PacketDescriptor.RELIABLE) && this.Flag(PacketDescriptor.ORDERED))
					{
						this.Order = reader.ReadByte();
					}

					if (this.Flag(PacketDescriptor.ACK))
					{
						this.Ack = reader.ReadUInt16();
						this.AckBits = reader.ReadUInt32();
					}

					int remaining = (int)(stream.Length - stream.Position);
					if (remaining > 0)
					{
						this.Data = reader.ReadBytes(remaining);
					}

					return true;
				}
			}
			catch
			{
				return false;
			}
		}

		internal void CalculateHash()
		{
			this.Crc32 = 0;
			byte[] buffer = this.Serialize();
			this.Crc32 = Force.Crc32.Crc32Algorithm.Compute(buffer);
		}

		internal bool Flag(PacketDescriptor flag)
		{
			return (this.Descriptor & (byte)flag) != 0;
		}

		internal static bool ValidateHeader(byte[] packet)
		{
			// 1b protocolId + 4b crc32 + 1b descriptor
			if (packet.Length < 6)
			{
				return false;
			}

			if (packet[0] != Config.CfgProtocolId)
			{
				return false;
			}

			if (packet.Length < HeaderSize(packet))
			{
				return false;
			}

			byte[] b = new byte[4];
			Buffer.BlockCopy(packet, 1, b, 0, 4);
			uint hash1 = BitConverter.ToUInt32(b, 0);

			for (int i = 1; i < 5; i++) packet[i] = 0;
			uint hash2 = Force.Crc32.Crc32Algorithm.Compute(packet);

			return hash1 == hash2;
		}

		internal static int HeaderSize(byte[] packet)
		{
			byte desc = packet[5];
			int size = 0;

			// protocolId (1) + crc (4) + descriptor (1)
			size += 6;

			if ((desc & (byte)PacketDescriptor.RELIABLE) != 0 || (desc & (byte)PacketDescriptor.ORDERED) != 0)
			{
				// sequence (2)
				size += 2;
			}

			if ((desc & (byte)PacketDescriptor.RELIABLE) != 0 && (desc & (byte)PacketDescriptor.ORDERED) != 0)
			{
				// order (1)
				size += 1;
			}

			if ((desc & (byte)PacketDescriptor.ACK) != 0)
			{
				// ack (2) + ackBits (4)
				size += 6;
			}

			return size;
		}
	}
}
