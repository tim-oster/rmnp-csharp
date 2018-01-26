// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System.Collections.Generic;

namespace rmnp
{
	class DropChannel<T> where T : class
	{
		private readonly object mutex = new object();

		private Queue<T> channel;
		private int limit;

		internal DropChannel(int limit)
		{
			this.channel = new Queue<T>();
			this.limit = limit;
		}

		internal void Push(T value)
		{
			lock (this.mutex)
			{
				if (this.channel.Count == this.limit)
				{
					this.channel.Dequeue();
				}

				this.channel.Enqueue(value);
			}
		}

		internal T Pop()
		{
			lock (this.mutex)
			{
				if (this.channel.Count == 0) return null;
				return this.channel.Dequeue();
			}
		}

		internal void Clear()
		{
			lock (this.mutex)
			{
				this.channel.Clear();
			}
		}
	}
}
