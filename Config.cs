// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

namespace rmnp
{
	class Config
	{
		// CfgMTU is the maximum byte size of a packet (header included).
		public static int CfgMTU = 1024;

		// CfgProtocolId is the identification number send with every rmnp packet to filter out unwanted traffic.
		public static byte CfgProtocolId = 231;

		// CfgParallelListenerCount is the amount of goroutines that will be spawned to listen on incoming requests.
		public static int CfgParallelListenerCount = 1;

		// CfgMaxSendReceiveQueueSize is the max size of packets that can be queued up before they are processed.
		public static int CfgMaxSendReceiveQueueSize = 100;

		// CfgMaxPacketChainLength is the max length of packets that are chained when waiting for missing sequence.
		public static byte CfgMaxPacketChainLength = 255;

		// CfgSequenceBufferSize is the size of the buffer that store the last received packets in order to ack them.
		// Size should be big enough that packets are at least overridden twice (max_sequence % size > 32 && max_sequence / size >= 2).
		// (max_sequence = highest possible sequence number = max value of sequenceNumber)
		public static ushort CfgSequenceBufferSize = 200;

		// CfgMaxSkippedPackets is the max amount of that are allowed to be skipped during packet loss (should be less than 32).
		public static ushort CfgMaxSkippedPackets = 25;

		// CfgUpdateLoopTimeout is the max wait duration for the connection update loop (should be less than other timeout variables).
		public static long CfgUpdateLoopTimeout = 10;

		// CfgSendRemoveTimeout is the time after which packets that have not being acked yet stop to be resend.
		public static long CfgSendRemoveTimeout = 1600;

		// CfgChainSkipTimeout is the time after which a missing packet for a complete sequence is ignored and skipped.
		public static long CfgChainSkipTimeout = 3000;

		// CfgAutoPingInterval defines the interval for sending a ping packet.
		public static byte CfgAutoPingInterval = 15;

		// CfgTimeoutThreshold is the time after which a connection times out if no packets have being send for the specified amount of time.
		public static long CfgTimeoutThreshold = 4000;

		// CfgMaxPing is the max ping before a connection times out.
		public static short CfgMaxPing = 150;

		// CfgRTTSmoothFactor is the factor used to slowly adjust the RTT.
		public static float CfgRTTSmoothFactor = 0.1f;

		// CfgCongestionThreshold is the max RTT before the connection enters bad mode.
		public static long CfgCongestionThreshold = 250;

		// CfgGoodRTTRewardInterval the time after how many seconds a good connection is rewarded.
		public static long CfgGoodRTTRewardInterval = 10 * 1000;

		// CfgBadRTTPunishTimeout the time after how many seconds a bad connection is punished.
		public static long CfgBadRTTPunishTimeout = 10 * 1000;

		// CfgMaxCongestionRequiredTime the max time it should take to switch between modes.
		public static long CfgMaxCongestionRequiredTime = 60 * 1000;

		// CfgDefaultCongestionRequiredTime the initial time it should take to switch back from bad to good mode.
		public static long CfgDefaultCongestionRequiredTime = 4 * 1000;

		// CfgCongestionPacketReduction is the amount of unreliable packets that get dropped in bad mode.
		public static byte CfgCongestionPacketReduction = 4;

		// CfgBadModeMultiplier is the multiplier for variables in bad mode
		public static float CfgBadModeMultiplier = 2.5f;

		// CfgResendTimeout is the default timeout for packets before resend
		public static long CfgResendTimeout = 50;

		// CfgMaxPacketResends is the default max amount of packets to resend during one update
		public static long CfgMaxPacketResends = 15;

		// CfgReackTimeout is the default timeout before a manual ack packet gets send
		public static long CfgReackTimeout = 50;
	}
}
