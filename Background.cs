// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.Threading;

namespace rmnp
{
	class Background
	{
		private static readonly object mutex = new object();
		private static Queue<Action> actions = new Queue<Action>();

		private static bool isRunning;
		private static Thread thread;

		public static void Execute(Action action)
		{
			lock (mutex)
			{
				actions.Enqueue(action);
			}
		}

		private static void Update()
		{
			while (isRunning)
			{
				Action action = null;

				lock (mutex)
				{
					if (actions.Count > 0)
					{
						action = actions.Dequeue();
					}
				}

				action();

				Thread.Sleep(50);
			}
		}

		public static void Start()
		{
			if (thread != null) return;

			isRunning = true;
			thread = new Thread(new ThreadStart(Update));
			thread.Start();
		}

		public static void Stop()
		{
			isRunning = false;
			thread.Join();
			thread = null;
		}
	}
}
