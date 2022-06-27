using System;

namespace WinMM
{
	// Token: 0x020001A8 RID: 424
	public class WaveFormat
	{
		// Token: 0x17000645 RID: 1605
		// (get) Token: 0x06001538 RID: 5432 RVA: 0x0006B9B0 File Offset: 0x00069BB0
		public static WaveFormat Pcm44Khz16BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 44100,
					BitsPerSample = 16,
					Channels = 2
				};
			}
		}

		// Token: 0x17000646 RID: 1606
		// (get) Token: 0x06001539 RID: 5433 RVA: 0x0006B9EC File Offset: 0x00069BEC
		public static WaveFormat Pcm44Khz16BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 44100,
					BitsPerSample = 16,
					Channels = 1
				};
			}
		}

		// Token: 0x17000647 RID: 1607
		// (get) Token: 0x0600153A RID: 5434 RVA: 0x0006BA28 File Offset: 0x00069C28
		public static WaveFormat Pcm44Khz8BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 44100,
					BitsPerSample = 8,
					Channels = 2
				};
			}
		}

		// Token: 0x17000648 RID: 1608
		// (get) Token: 0x0600153B RID: 5435 RVA: 0x0006BA60 File Offset: 0x00069C60
		public static WaveFormat Pcm44Khz8BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 44100,
					BitsPerSample = 8,
					Channels = 1
				};
			}
		}

		// Token: 0x17000649 RID: 1609
		// (get) Token: 0x0600153C RID: 5436 RVA: 0x0006BA98 File Offset: 0x00069C98
		public static WaveFormat Pcm32Khz16BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 32000,
					BitsPerSample = 16,
					Channels = 2
				};
			}
		}

		// Token: 0x1700064A RID: 1610
		// (get) Token: 0x0600153D RID: 5437 RVA: 0x0006BAD4 File Offset: 0x00069CD4
		public static WaveFormat Pcm32Khz16BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 32000,
					BitsPerSample = 16,
					Channels = 1
				};
			}
		}

		// Token: 0x1700064B RID: 1611
		// (get) Token: 0x0600153E RID: 5438 RVA: 0x0006BB10 File Offset: 0x00069D10
		public static WaveFormat Pcm32Khz8BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 32000,
					BitsPerSample = 8,
					Channels = 2
				};
			}
		}

		// Token: 0x1700064C RID: 1612
		// (get) Token: 0x0600153F RID: 5439 RVA: 0x0006BB48 File Offset: 0x00069D48
		public static WaveFormat Pcm32Khz8BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 32000,
					BitsPerSample = 8,
					Channels = 1
				};
			}
		}

		// Token: 0x1700064D RID: 1613
		// (get) Token: 0x06001540 RID: 5440 RVA: 0x0006BB80 File Offset: 0x00069D80
		public static WaveFormat Pcm24Khz16BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 24000,
					BitsPerSample = 16,
					Channels = 2
				};
			}
		}

		// Token: 0x1700064E RID: 1614
		// (get) Token: 0x06001541 RID: 5441 RVA: 0x0006BBBC File Offset: 0x00069DBC
		public static WaveFormat Pcm24Khz16BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 24000,
					BitsPerSample = 16,
					Channels = 1
				};
			}
		}

		// Token: 0x1700064F RID: 1615
		// (get) Token: 0x06001542 RID: 5442 RVA: 0x0006BBF8 File Offset: 0x00069DF8
		public static WaveFormat Pcm24Khz8BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 24000,
					BitsPerSample = 8,
					Channels = 2
				};
			}
		}

		// Token: 0x17000650 RID: 1616
		// (get) Token: 0x06001543 RID: 5443 RVA: 0x0006BC30 File Offset: 0x00069E30
		public static WaveFormat Pcm24Khz8BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 24000,
					BitsPerSample = 8,
					Channels = 1
				};
			}
		}

		// Token: 0x17000651 RID: 1617
		// (get) Token: 0x06001544 RID: 5444 RVA: 0x0006BC68 File Offset: 0x00069E68
		public static WaveFormat Pcm22Khz16BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 22050,
					BitsPerSample = 16,
					Channels = 2
				};
			}
		}

		// Token: 0x17000652 RID: 1618
		// (get) Token: 0x06001545 RID: 5445 RVA: 0x0006BCA4 File Offset: 0x00069EA4
		public static WaveFormat Pcm22Khz16BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 22050,
					BitsPerSample = 16,
					Channels = 1
				};
			}
		}

		// Token: 0x17000653 RID: 1619
		// (get) Token: 0x06001546 RID: 5446 RVA: 0x0006BCE0 File Offset: 0x00069EE0
		public static WaveFormat Pcm22Khz8BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 22050,
					BitsPerSample = 8,
					Channels = 2
				};
			}
		}

		// Token: 0x17000654 RID: 1620
		// (get) Token: 0x06001547 RID: 5447 RVA: 0x0006BD18 File Offset: 0x00069F18
		public static WaveFormat Pcm22Khz8BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 22050,
					BitsPerSample = 8,
					Channels = 1
				};
			}
		}

		// Token: 0x17000655 RID: 1621
		// (get) Token: 0x06001548 RID: 5448 RVA: 0x0006BD50 File Offset: 0x00069F50
		public static WaveFormat Pcm16Khz16BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 16000,
					BitsPerSample = 16,
					Channels = 2
				};
			}
		}

		// Token: 0x17000656 RID: 1622
		// (get) Token: 0x06001549 RID: 5449 RVA: 0x0006BD8C File Offset: 0x00069F8C
		public static WaveFormat Pcm16Khz16BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 16000,
					BitsPerSample = 16,
					Channels = 1
				};
			}
		}

		// Token: 0x17000657 RID: 1623
		// (get) Token: 0x0600154A RID: 5450 RVA: 0x0006BDC8 File Offset: 0x00069FC8
		public static WaveFormat Pcm16Khz8BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 16000,
					BitsPerSample = 8,
					Channels = 2
				};
			}
		}

		// Token: 0x17000658 RID: 1624
		// (get) Token: 0x0600154B RID: 5451 RVA: 0x0006BE00 File Offset: 0x0006A000
		public static WaveFormat Pcm16Khz8BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 16000,
					BitsPerSample = 8,
					Channels = 1
				};
			}
		}

		// Token: 0x17000659 RID: 1625
		// (get) Token: 0x0600154C RID: 5452 RVA: 0x0006BE38 File Offset: 0x0006A038
		public static WaveFormat Pcm12Khz16BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 12000,
					BitsPerSample = 16,
					Channels = 2
				};
			}
		}

		// Token: 0x1700065A RID: 1626
		// (get) Token: 0x0600154D RID: 5453 RVA: 0x0006BE74 File Offset: 0x0006A074
		public static WaveFormat Pcm12Khz16BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 12000,
					BitsPerSample = 16,
					Channels = 1
				};
			}
		}

		// Token: 0x1700065B RID: 1627
		// (get) Token: 0x0600154E RID: 5454 RVA: 0x0006BEB0 File Offset: 0x0006A0B0
		public static WaveFormat Pcm12Khz8BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 12000,
					BitsPerSample = 8,
					Channels = 2
				};
			}
		}

		// Token: 0x1700065C RID: 1628
		// (get) Token: 0x0600154F RID: 5455 RVA: 0x0006BEE8 File Offset: 0x0006A0E8
		public static WaveFormat Pcm12Khz8BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 12000,
					BitsPerSample = 8,
					Channels = 1
				};
			}
		}

		// Token: 0x1700065D RID: 1629
		// (get) Token: 0x06001550 RID: 5456 RVA: 0x0006BF20 File Offset: 0x0006A120
		public static WaveFormat Pcm11Khz16BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 11025,
					BitsPerSample = 16,
					Channels = 2
				};
			}
		}

		// Token: 0x1700065E RID: 1630
		// (get) Token: 0x06001551 RID: 5457 RVA: 0x0006BF5C File Offset: 0x0006A15C
		public static WaveFormat Pcm11Khz16BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 11025,
					BitsPerSample = 16,
					Channels = 1
				};
			}
		}

		// Token: 0x1700065F RID: 1631
		// (get) Token: 0x06001552 RID: 5458 RVA: 0x0006BF98 File Offset: 0x0006A198
		public static WaveFormat Pcm11Khz8BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 11025,
					BitsPerSample = 8,
					Channels = 2
				};
			}
		}

		// Token: 0x17000660 RID: 1632
		// (get) Token: 0x06001553 RID: 5459 RVA: 0x0006BFD0 File Offset: 0x0006A1D0
		public static WaveFormat Pcm11Khz8BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 11025,
					BitsPerSample = 8,
					Channels = 1
				};
			}
		}

		// Token: 0x17000661 RID: 1633
		// (get) Token: 0x06001554 RID: 5460 RVA: 0x0006C008 File Offset: 0x0006A208
		public static WaveFormat Pcm8Khz16BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 8000,
					BitsPerSample = 16,
					Channels = 2
				};
			}
		}

		// Token: 0x17000662 RID: 1634
		// (get) Token: 0x06001555 RID: 5461 RVA: 0x0006C044 File Offset: 0x0006A244
		public static WaveFormat Pcm8Khz16BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 8000,
					BitsPerSample = 16,
					Channels = 1
				};
			}
		}

		// Token: 0x17000663 RID: 1635
		// (get) Token: 0x06001556 RID: 5462 RVA: 0x0006C080 File Offset: 0x0006A280
		public static WaveFormat Pcm8Khz8BitStereo
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 8000,
					BitsPerSample = 8,
					Channels = 2
				};
			}
		}

		// Token: 0x17000664 RID: 1636
		// (get) Token: 0x06001557 RID: 5463 RVA: 0x0006C0B8 File Offset: 0x0006A2B8
		public static WaveFormat Pcm8Khz8BitMono
		{
			get
			{
				return new WaveFormat
				{
					FormatTag = WaveFormatTag.Pcm,
					SamplesPerSecond = 8000,
					BitsPerSample = 8,
					Channels = 1
				};
			}
		}

		// Token: 0x17000665 RID: 1637
		// (get) Token: 0x06001558 RID: 5464 RVA: 0x0006C0F0 File Offset: 0x0006A2F0
		// (set) Token: 0x06001559 RID: 5465 RVA: 0x0006C0F8 File Offset: 0x0006A2F8
		public WaveFormatTag FormatTag
		{
			get
			{
				return this.formatTag;
			}
			set
			{
				this.formatTag = value;
			}
		}

		// Token: 0x17000666 RID: 1638
		// (get) Token: 0x0600155A RID: 5466 RVA: 0x0006C104 File Offset: 0x0006A304
		// (set) Token: 0x0600155B RID: 5467 RVA: 0x0006C10C File Offset: 0x0006A30C
		public short Channels
		{
			get
			{
				return this.channels;
			}
			set
			{
				this.channels = value;
			}
		}

		// Token: 0x17000667 RID: 1639
		// (get) Token: 0x0600155C RID: 5468 RVA: 0x0006C118 File Offset: 0x0006A318
		// (set) Token: 0x0600155D RID: 5469 RVA: 0x0006C120 File Offset: 0x0006A320
		public int SamplesPerSecond
		{
			get
			{
				return this.samplesPerSecond;
			}
			set
			{
				this.samplesPerSecond = value;
			}
		}

		// Token: 0x17000668 RID: 1640
		// (get) Token: 0x0600155E RID: 5470 RVA: 0x0006C12C File Offset: 0x0006A32C
		// (set) Token: 0x0600155F RID: 5471 RVA: 0x0006C134 File Offset: 0x0006A334
		public short BitsPerSample
		{
			get
			{
				return this.bitsPerSample;
			}
			set
			{
				this.bitsPerSample = value;
			}
		}

		// Token: 0x17000669 RID: 1641
		// (get) Token: 0x06001560 RID: 5472 RVA: 0x0006C140 File Offset: 0x0006A340
		public short BlockAlign
		{
			get
			{
                return ((short)((this.Channels * this.BitsPerSample) / 8));
			}
		}

		// Token: 0x1700066A RID: 1642
		// (get) Token: 0x06001561 RID: 5473 RVA: 0x0006C154 File Offset: 0x0006A354
		public int AverageBytesPerSecond
		{
			get
			{
                return (this.SamplesPerSecond * this.BlockAlign);
			}
		}

		// Token: 0x06001562 RID: 5474 RVA: 0x0006C164 File Offset: 0x0006A364
		internal WaveFormat Clone()
		{
			return new WaveFormat
			{
				bitsPerSample = this.bitsPerSample,
				channels = this.channels,
				formatTag = this.formatTag,
				samplesPerSecond = this.samplesPerSecond
			};
		}

		// Token: 0x04000C3B RID: 3131
		WaveFormatTag formatTag;

		// Token: 0x04000C3C RID: 3132
		short channels;

		// Token: 0x04000C3D RID: 3133
		int samplesPerSecond;

		// Token: 0x04000C3E RID: 3134
		short bitsPerSample;
	}
}
