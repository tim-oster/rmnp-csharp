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
		internal long rtt;

		private long lastChangeTime;
		private long requiredTime;

		private byte unreliableCount;

		public long resendTimeout;
		public long maxPacketResend;
		public long reackTimeout;

		public CongestionHandler()
		{
			this.Reset();
		}

		public void Reset()
		{
			this.ChangeMode(Mode.NONE);
			this.rtt = 0;
			this.requiredTime = Config.CfgDefaultCongestionRequiredTime;
			this.unreliableCount = 0;
		}

		public void Check(long sendTime)
		{
			long time = Util.CurrentTime();
			long rtt = time - sendTime;

			if (this.rtt == 0) this.rtt = rtt;
			else this.rtt += (long)((rtt - this.rtt) * Config.CfgRTTSmoothFactor);

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
					this.resendTimeout = Config.CfgResendTimeout;
					this.maxPacketResend = Config.CfgMaxPacketResends;
					this.reackTimeout = Config.CfgReackTimeout;
					break;
				case Mode.BAD:
					this.resendTimeout = (long)(Config.CfgResendTimeout * Config.CfgBadModeMultiplier);
					this.maxPacketResend = (long)(Config.CfgMaxPacketResends * Config.CfgBadModeMultiplier);
					this.reackTimeout = (long)(Config.CfgReackTimeout * Config.CfgBadModeMultiplier);
					break;
			}

			this.mode = mode;
			this.lastChangeTime = Util.CurrentTime();
		}

		public bool ShouldDropUnreliable()
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
