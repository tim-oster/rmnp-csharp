// Copyright 2017 Tim Oster. All rights reserved.
// Use of this source code is governed by the MIT license.
// More information can be found in the LICENSE file.

namespace rmnp
{
	class CongestionHandler
	{
		private enum Mode
		{
			None,
			Good,
			Bad
		}

		private Mode mode;
		private long rtt;

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
			this.ChangeMode(Mode.None);
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
				case Mode.None:
					this.ChangeMode(Mode.Good);
					break;
				case Mode.Good:
					if (rtt > Config.CfgCongestionThreshold)
					{
						if (time - this.lastChangeTime <= Config.CfgBadRTTPunishTimeout)
						{
							this.requiredTime = Util.Min(Config.CfgMaxCongestionRequiredTime, this.requiredTime * 2);
						}

						this.ChangeMode(Mode.Bad);
					}
					else if (time - this.lastChangeTime >= Config.CfgGoodRTTRewardInterval)
					{
						this.requiredTime = Util.Max(1, this.requiredTime / 2);
						this.lastChangeTime = time;
					}

					break;
				case Mode.Bad:
					if (rtt > Config.CfgCongestionThreshold)
					{
						this.lastChangeTime = time;
					}

					if (time - this.lastChangeTime >= this.requiredTime)
					{
						this.ChangeMode(Mode.Good);
					}

					break;
			}
		}

		private void ChangeMode(Mode mode)
		{
			switch (mode)
			{
				case Mode.None:
				case Mode.Good:
					this.resendTimeout = Config.CfgResendTimeout;
					this.maxPacketResend = Config.CfgMaxPacketResends;
					this.reackTimeout = Config.CfgReackTimeout;
					break;
				case Mode.Bad:
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
				case Mode.Good:
					return false;
				case Mode.Bad:
					this.unreliableCount++;
					return this.unreliableCount % Config.CfgCongestionPacketReduction == 0;
			}

			return false;
		}
	}
}
