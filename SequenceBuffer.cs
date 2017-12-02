// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

namespace rmnp
{
	class SequenceBuffer
	{
		private ushort size;
		private ushort[] sequences;
		private bool[] states;
		private readonly object mutex = new object();

		internal SequenceBuffer(ushort size)
		{
			this.size = size;
			this.sequences = new ushort[size];
			this.states = new bool[size];
		}

		internal void Reset()
		{
			lock (this.mutex)
			{
				for (int i = 0; i < this.size; i++)
				{
					this.sequences[i] = 0;
					this.states[i] = false;
				}
			}
		}

		internal bool Get(ushort sequence)
		{
			lock (this.mutex)
			{
				if (this.sequences[sequence % this.size] != sequence)
				{
					return false;
				}

				return this.states[sequence % this.size];
			}
		}

		internal void Set(ushort sequence, bool value)
		{
			lock (this.mutex)
			{
				this.sequences[sequence % this.size] = sequence;
				this.states[sequence % this.size] = value;
			}
		}
	}
}
