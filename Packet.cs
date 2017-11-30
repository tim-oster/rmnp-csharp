// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System;
using System.IO;

namespace rmnp
{
	class Packet
	{
		public enum Descriptor : byte
		{
			RELIABLE = (1 << 0),
			ACK = (1 << 1),
			ORDERED = (1 << 2),

			CONNECT = (1 << 3),
			DISCONNECT = (1 << 4)
		}

		public byte protocolId;
		public uint crc32;
		public byte descriptor;

		public ushort sequence;
		public byte order;

		public ushort ack;
		public uint ackBits;

		public byte[] data;

		public byte[] Serialize()
		{
			using (MemoryStream stream = new MemoryStream())
			{
				using (BinaryWriter writer = new BinaryWriter(stream))
				{
					writer.Write(this.protocolId);
					writer.Write(this.crc32);
					writer.Write(this.descriptor);

					if (this.Flag(Descriptor.RELIABLE) || this.Flag(Descriptor.ORDERED))
					{
						writer.Write(this.sequence);
					}

					if (this.Flag(Descriptor.RELIABLE) && this.Flag(Descriptor.ORDERED))
					{
						writer.Write(this.order);
					}

					if (this.Flag(Descriptor.ACK))
					{
						writer.Write(this.ack);
						writer.Write(this.ackBits);
					}

					if (this.data != null && this.data.Length > 0)
					{
						writer.Write(this.data);
					}
				}

				return stream.ToArray();
			}
		}

		public bool Deserialize(byte[] packet)
		{
			try
			{
				using (MemoryStream stream = new MemoryStream(packet))
				using (BinaryReader reader = new BinaryReader(stream))
				{
					this.protocolId = reader.ReadByte();
					this.crc32 = reader.ReadUInt32();
					this.descriptor = reader.ReadByte();

					if (this.Flag(Descriptor.RELIABLE) || this.Flag(Descriptor.ORDERED))
					{
						this.sequence = reader.ReadUInt16();
					}

					if (this.Flag(Descriptor.RELIABLE) && this.Flag(Descriptor.ORDERED))
					{
						this.order = reader.ReadByte();
					}

					if (this.Flag(Descriptor.ACK))
					{
						this.ack = reader.ReadUInt16();
						this.ackBits = reader.ReadUInt32();
					}

					int remaining = (int)(stream.Length - stream.Position);
					if (remaining > 0)
					{
						this.data = reader.ReadBytes(remaining);
					}

					return true;
				}
			}
			catch
			{
				return false;
			}
		}

		public void CalculateHash()
		{
			this.crc32 = 0;
			byte[] buffer = this.Serialize();
			this.crc32 = Force.Crc32.Crc32Algorithm.Compute(buffer);
		}

		public bool Flag(Descriptor flag)
		{
			return (this.descriptor & (byte)flag) != 0;
		}

		public static bool ValidateHeader(byte[] packet)
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

		public static int HeaderSize(byte[] packet)
		{
			byte desc = packet[5];
			int size = 0;

			// protocolId (1) + crc (4) + descriptor (1)
			size += 6;

			if ((desc & (byte)Descriptor.RELIABLE) != 0 || (desc & (byte)Descriptor.ORDERED) != 0)
			{
				// sequence (2)
				size += 2;
			}

			if ((desc & (byte)Descriptor.RELIABLE) != 0 && (desc & (byte)Descriptor.ORDERED) != 0)
			{
				// order (1)
				size += 1;
			}

			if ((desc & (byte)Descriptor.ACK) != 0)
			{
				// ack (2) + ackBits (4)
				size += 6;
			}

			return size;
		}
	}
}
