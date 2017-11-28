// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

namespace rmnp
{
	class Stats
	{
		// StatSendBytes (atomic) counts the total amount of bytes send.
		public ulong StatSendBytes = 0;

		// StatReceivedBytes (atomic) counts the total amount of bytes received.
		// Not the same as StatProcessedBytes because received packets may be discarded.
		public ulong StatReceivedBytes = 0;

		// StatProcessedBytes (atomic) counts the total size of all processed packets.
		public ulong StatProcessedBytes = 0;

		// StatRunningGoRoutines (atomic) counts all currently active goroutines spawned by rmnp
		public ulong StatRunningGoRoutines = 0;

		// StatGoRoutinePanics (atomic) counts the amount of caught goroutine panics
		public ulong StatGoRoutinePanics = 0;

		// StatConnects (atomic) counts all successful connects
		public ulong StatConnects = 0;

		// StatDeniedConnects (atomic) counts all denied connection attempts
		public ulong StatDeniedConnects = 0;

		// StatDisconnects (atomic) counts all disconnects
		public ulong StatDisconnects = 0;

		// StatTimeouts (atomic) counts all timeouts
		public ulong StatTimeouts = 0;
	}
}
