/*
 * Copyright (C) Tim Oster - All Rights Reserved
 * Unauthorized copying of this file, via any medium is strictly prohibited
 * Proprietary and confidential
 * Written by Tim Oster <tim.oster@teamdna.de>
 */

using System.Threading;

public class WaitGroup
{
	private int count = 0;

	public void Start()
	{
		Interlocked.Increment(ref this.count);
	}

	public void End()
	{
		Interlocked.Decrement(ref this.count);
	}

	public void Wait()
	{
		while (Interlocked.CompareExchange(ref this.count, 0, 0) > 0)
		{
			Thread.Sleep(10);
		}
	}
}
