// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

namespace rmnp
{
	class Stats
	{
		// StatSendBytes (atomic) counts the total amount of bytes send.
		public static long StatSendBytes = 0;

		// StatReceivedBytes (atomic) counts the total amount of bytes received.
		// Not the same as StatProcessedBytes because received packets may be discarded.
		public static long StatReceivedBytes = 0;

		// StatProcessedBytes (atomic) counts the total size of all processed packets.
		public static long StatProcessedBytes = 0;

		// StatRunningGoRoutines (atomic) counts all currently active goroutines spawned by rmnp
		public static long StatRunningGoRoutines = 0;

		// StatGoRoutinePanics (atomic) counts the amount of caught goroutine panics
		public static long StatGoRoutinePanics = 0;

		// StatConnects (atomic) counts all successful connects
		public static long StatConnects = 0;

		// StatDeniedConnects (atomic) counts all denied connection attempts
		public static long StatDeniedConnects = 0;

		// StatDisconnects (atomic) counts all disconnects
		public static long StatDisconnects = 0;

		// StatTimeouts (atomic) counts all timeouts
		public static long StatTimeouts = 0;
	}
}
