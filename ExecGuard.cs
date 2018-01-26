// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System.Collections.Generic;

namespace rmnp
{
	class ExecGuard
	{
		private readonly object mutex = new object();
		private Dictionary<uint, bool> executions = new Dictionary<uint, bool>();

		internal ExecGuard()
		{
		}

		internal bool TryExecute(uint id)
		{
			lock (this.mutex)
			{
				if (!this.executions.ContainsKey(id))
				{
					this.executions.Add(id, true);
					return true;
				}
			}

			return false;
		}

		internal void Finish(uint id)
		{
			lock (this.mutex)
			{
				this.executions.Remove(id);
			}
		}
	}
}
