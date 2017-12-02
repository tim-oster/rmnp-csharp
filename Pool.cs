// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System;
using System.Collections.Generic;

namespace rmnp
{
	class Pool<T>
	{
		private readonly object mutex = new object();
		private Stack<T> objects = new Stack<T>();

		internal Func<T> Allocator;

		internal Pool()
		{
		}

		internal T Get()
		{
			lock (this.mutex)
			{
				if (this.objects.Count == 0)
				{
					return this.Allocator();
				}
				else
				{
					return this.objects.Pop();
				}
			}
		}

		internal void Put(T obj)
		{
			lock (this.mutex)
			{
				this.objects.Push(obj);
			}
		}
	}
}
