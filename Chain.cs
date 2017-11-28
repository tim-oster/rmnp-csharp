// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

namespace rmnp
{
	class Chain
	{
		public class Link
		{
			public Link next;
			public Packet packet;

			internal Link(Link next, Packet packet)
			{
				this.next = next;
				this.packet = packet;
			}
		}

		private byte next;
		private Link start;
		private byte length;
		private byte maxLength;
		private object mutex;

		public Chain(byte maxLength)
		{
			this.maxLength = maxLength;
			this.mutex = new object();
		}

		public void Reset()
		{
			lock (this.mutex)
			{
				this.next = 0;
				this.start = null;
				this.length = 0;
				this.maxLength = 0;
			}
		}

		public void ChainPacket(Packet packet)
		{
			lock (this.mutex)
			{
				if (this.start == null)
				{
					this.start = new Link(null, packet);
				}
				else
				{
					Link link = null;
		  
					for (Link l = this.start; l != null; l = l.next)
					{
						if (Util.GreaterThanOrder(packet.order, l.packet.order)) link = l;
						else break;
					}

					if (link == null) this.start = new Link(this.start, packet);
					else link.next = new Link(link.next, packet);
				}

				if (this.length >= this.maxLength)
				{
					this.start = this.start.next;
					this.length--;
				}

				this.length++;
			}
		}

		public Link PopConsecutive()
		{
			lock (this.mutex)
			{
				Link last = null;

				for (Link l = this.start; l != null; l = l.next)
				{
					if (l.packet.order == this.next)
					{
						this.length--;
						this.next++;
						last = l;
					}
					else
					{
						break;
					}
				}

				if (last != null)
				{
					Link start = this.start;
					this.start = last.next;
					last.next = null;
					return start;
				}

				return null;
			}
		}

		public void Skip()
		{
			lock (this.mutex)
			{
				if (this.start != null)
				{
					this.next = this.start.packet.order;
				}
			}
		}
	}
}
