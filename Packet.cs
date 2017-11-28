// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

namespace rmnp
{
	class Packet
	{
		public byte protocolId;
		public uint crc32;
		public byte descriptor;

		public ushort sequence;
		public byte order;

		public ushort ack;
		public uint ackBits;

		public byte[] data;
	}
}
