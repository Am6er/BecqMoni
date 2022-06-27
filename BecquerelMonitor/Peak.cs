using System;

namespace BecquerelMonitor
{
	// Token: 0x020000D0 RID: 208
	public class Peak
	{
		// Token: 0x170002D8 RID: 728
		// (get) Token: 0x06000AB2 RID: 2738 RVA: 0x0003FDC0 File Offset: 0x0003DFC0
		// (set) Token: 0x06000AB3 RID: 2739 RVA: 0x0003FDC8 File Offset: 0x0003DFC8
		public double Energy
		{
			get
			{
				return this.energy;
			}
			set
			{
				this.energy = value;
			}
		}

		// Token: 0x170002D9 RID: 729
		// (get) Token: 0x06000AB4 RID: 2740 RVA: 0x0003FDD4 File Offset: 0x0003DFD4
		// (set) Token: 0x06000AB5 RID: 2741 RVA: 0x0003FDDC File Offset: 0x0003DFDC
		public int Channel
		{
			get
			{
				return this.channel;
			}
			set
			{
				this.channel = value;
			}
		}

		// Token: 0x170002DA RID: 730
		// (get) Token: 0x06000AB6 RID: 2742 RVA: 0x0003FDE8 File Offset: 0x0003DFE8
		// (set) Token: 0x06000AB7 RID: 2743 RVA: 0x0003FDF0 File Offset: 0x0003DFF0
		public NuclideDefinition Nuclide
		{
			get
			{
				return this.nuclide;
			}
			set
			{
				this.nuclide = value;
			}
		}

		// Token: 0x170002DB RID: 731
		// (get) Token: 0x06000AB8 RID: 2744 RVA: 0x0003FDFC File Offset: 0x0003DFFC
		// (set) Token: 0x06000AB9 RID: 2745 RVA: 0x0003FE04 File Offset: 0x0003E004
		public int Count
		{
			get
			{
				return this.count;
			}
			set
			{
				this.count = value;
			}
		}

		// Token: 0x170002DC RID: 732
		// (get) Token: 0x06000ABA RID: 2746 RVA: 0x0003FE10 File Offset: 0x0003E010
		// (set) Token: 0x06000ABB RID: 2747 RVA: 0x0003FE18 File Offset: 0x0003E018
		public int LeftChannel
		{
			get
			{
				return this.leftChannel;
			}
			set
			{
				this.leftChannel = value;
			}
		}

		// Token: 0x170002DD RID: 733
		// (get) Token: 0x06000ABC RID: 2748 RVA: 0x0003FE24 File Offset: 0x0003E024
		// (set) Token: 0x06000ABD RID: 2749 RVA: 0x0003FE2C File Offset: 0x0003E02C
		public int RightChannel
		{
			get
			{
				return this.rightChannel;
			}
			set
			{
				this.rightChannel = value;
			}
		}

		// Token: 0x040005EB RID: 1515
		double energy;

		// Token: 0x040005EC RID: 1516
		int channel;

		// Token: 0x040005ED RID: 1517
		NuclideDefinition nuclide;

		// Token: 0x040005EE RID: 1518
		int count;

		// Token: 0x040005EF RID: 1519
		int leftChannel;

		// Token: 0x040005F0 RID: 1520
		int rightChannel;
	}
}
