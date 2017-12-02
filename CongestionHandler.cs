// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

namespace rmnp
{
	class CongestionHandler
	{
		private enum Mode
		{
			NONE,
			GOOD,
			BAD
		}

		private Mode mode;
		internal long RTT;

		private long lastChangeTime;
		private long requiredTime;

		private byte unreliableCount;

		internal long ResendTimeout;
		internal long MaxPacketResend;
		internal long ReackTimeout;

		internal CongestionHandler()
		{
			this.Reset();
		}

		internal void Reset()
		{
			this.ChangeMode(Mode.NONE);
			this.RTT = 0;
			this.requiredTime = Config.CfgDefaultCongestionRequiredTime;
			this.unreliableCount = 0;
		}

		internal void Check(long sendTime)
		{
			long time = Util.CurrentTime();
			long rtt = time - sendTime;

			if (this.RTT == 0) this.RTT = rtt;
			else this.RTT += (long)((rtt - this.RTT) * Config.CfgRTTSmoothFactor);

			switch (this.mode)
			{
				case Mode.NONE:
					this.ChangeMode(Mode.GOOD);
					break;
				case Mode.GOOD:
					if (rtt > Config.CfgCongestionThreshold)
					{
						if (time - this.lastChangeTime <= Config.CfgBadRTTPunishTimeout)
						{
							this.requiredTime = Util.Min(Config.CfgMaxCongestionRequiredTime, this.requiredTime * 2);
						}

						this.ChangeMode(Mode.BAD);
					}
					else if (time - this.lastChangeTime >= Config.CfgGoodRTTRewardInterval)
					{
						this.requiredTime = Util.Max(1, this.requiredTime / 2);
						this.lastChangeTime = time;
					}

					break;
				case Mode.BAD:
					if (rtt > Config.CfgCongestionThreshold)
					{
						this.lastChangeTime = time;
					}

					if (time - this.lastChangeTime >= this.requiredTime)
					{
						this.ChangeMode(Mode.GOOD);
					}

					break;
			}
		}

		private void ChangeMode(Mode mode)
		{
			switch (mode)
			{
				case Mode.NONE:
				case Mode.GOOD:
					this.ResendTimeout = Config.CfgResendTimeout;
					this.MaxPacketResend = Config.CfgMaxPacketResends;
					this.ReackTimeout = Config.CfgReackTimeout;
					break;
				case Mode.BAD:
					this.ResendTimeout = (long)(Config.CfgResendTimeout * Config.CfgBadModeMultiplier);
					this.MaxPacketResend = (long)(Config.CfgMaxPacketResends * Config.CfgBadModeMultiplier);
					this.ReackTimeout = (long)(Config.CfgReackTimeout * Config.CfgBadModeMultiplier);
					break;
			}

			this.mode = mode;
			this.lastChangeTime = Util.CurrentTime();
		}

		internal bool ShouldDropUnreliable()
		{
			switch (this.mode)
			{
				case Mode.GOOD:
					return false;
				case Mode.BAD:
					this.unreliableCount++;
					return this.unreliableCount % Config.CfgCongestionPacketReduction == 0;
			}

			return false;
		}
	}
}
