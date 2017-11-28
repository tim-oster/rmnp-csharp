// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System;
using System.Net;

namespace rmnp
{
	class Util
	{
		public static void CheckError(string msg, object error)
		{
			// not implemented
		}

		public static void AntiPanic(object callback)
		{
			// not implemented
		}

		public static uint AddrHash(IPEndPoint endpoint)
		{
			byte[] a = endpoint.Address.GetAddressBytes();
			byte[] p = BitConverter.GetBytes(endpoint.Port);

			byte[] b = new byte[a.Length + p.Length];
			Buffer.BlockCopy(a, 0, b, 0, a.Length);
			Buffer.BlockCopy(p, 0, b, a.Length, p.Length);

			return Force.Crc32.Crc32Algorithm.Compute(b);
		}

		public static byte[] CnvUint32(uint i)
		{
			// not implemented
			return null;
		}

		private static readonly DateTime Jan1st1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		public static long CurrentTime()
		{
			return (long)(DateTime.UtcNow - Jan1st1970).TotalMilliseconds;
		}

		public static bool GreaterThanSequence(ushort s1, ushort s2)
		{
			return (s1 > s2 && s1 - s2 <= 32768) || (s1 < s2 && s2 - s1 > 32768);
		}

		public static bool GreaterThanOrder(byte s1, byte s2)
		{
			return (s1 > s2 && s1 - s2 <= 127) || (s1 < s2 && s2 - s1 > 127);
		}

		public static ushort DifferenceSequence(ushort s1, ushort s2)
		{
			if (s1 >= s2)
			{
				return (ushort)(s1 - s2 <= 32768 ? s1 - s2 : (65535 - s1) + s2);
			}
			else
			{
				return DifferenceSequence(s2, s1);
			}
		}

		public static long Min(long x, long y)
		{
			return x < y ? x : y;
		}

		public static long Max(long x, long y)
		{
			return x > y ? x : y;
		}
	}
}
