using System;
using WinMM;

namespace BecquerelMonitor
{
	// Token: 0x020000E5 RID: 229
	public class ResultDataStatus
	{
		// Token: 0x170002FA RID: 762
		// (get) Token: 0x06000B3B RID: 2875 RVA: 0x00046164 File Offset: 0x00044364
		// (set) Token: 0x06000B3C RID: 2876 RVA: 0x0004616C File Offset: 0x0004436C
		public int PresetTime
		{
			get
			{
				return this.presetTime;
			}
			set
			{
				this.presetTime = value;
			}
		}

		// Token: 0x170002FB RID: 763
		// (get) Token: 0x06000B3D RID: 2877 RVA: 0x00046178 File Offset: 0x00044378
		// (set) Token: 0x06000B3E RID: 2878 RVA: 0x00046180 File Offset: 0x00044380
		public TimeSpan TotalTime
		{
			get
			{
				return this.totalTime;
			}
			set
			{
				this.totalTime = value;
			}
		}

		// Token: 0x170002FC RID: 764
		// (get) Token: 0x06000B3F RID: 2879 RVA: 0x0004618C File Offset: 0x0004438C
		// (set) Token: 0x06000B40 RID: 2880 RVA: 0x00046194 File Offset: 0x00044394
		public TimeSpan ElapsedTime
		{
			get
			{
				return this.elapsedTime;
			}
			set
			{
				this.elapsedTime = value;
			}
		}

		// Token: 0x170002FD RID: 765
		// (get) Token: 0x06000B41 RID: 2881 RVA: 0x000461A0 File Offset: 0x000443A0
		// (set) Token: 0x06000B42 RID: 2882 RVA: 0x000461A8 File Offset: 0x000443A8
		public long TimeInSamples
		{
			get
			{
				return this.timeInSamples;
			}
			set
			{
				this.timeInSamples = value;
			}
		}

		// Token: 0x170002FE RID: 766
		// (get) Token: 0x06000B43 RID: 2883 RVA: 0x000461B4 File Offset: 0x000443B4
		// (set) Token: 0x06000B44 RID: 2884 RVA: 0x000461BC File Offset: 0x000443BC
		public bool Recording
		{
			get
			{
				return this.recording;
			}
			set
			{
				this.recording = value;
			}
		}

		// Token: 0x170002FF RID: 767
		// (get) Token: 0x06000B45 RID: 2885 RVA: 0x000461C8 File Offset: 0x000443C8
		// (set) Token: 0x06000B46 RID: 2886 RVA: 0x000461D0 File Offset: 0x000443D0
		public bool Testing
		{
			get
			{
				return this.testing;
			}
			set
			{
				this.testing = value;
			}
		}

		// Token: 0x17000300 RID: 768
		// (get) Token: 0x06000B47 RID: 2887 RVA: 0x000461DC File Offset: 0x000443DC
		// (set) Token: 0x06000B48 RID: 2888 RVA: 0x000461E4 File Offset: 0x000443E4
		public bool StopTest
		{
			get
			{
				return this.stopTest;
			}
			set
			{
				this.stopTest = value;
			}
		}

		// Token: 0x17000301 RID: 769
		// (get) Token: 0x06000B49 RID: 2889 RVA: 0x000461F0 File Offset: 0x000443F0
		// (set) Token: 0x06000B4A RID: 2890 RVA: 0x000461F8 File Offset: 0x000443F8
		public bool Stabilization
		{
			get
			{
				return this.stabilization;
			}
			set
			{
				this.stabilization = value;
			}
		}

		// Token: 0x17000302 RID: 770
		// (get) Token: 0x06000B4B RID: 2891 RVA: 0x00046204 File Offset: 0x00044404
		// (set) Token: 0x06000B4C RID: 2892 RVA: 0x0004620C File Offset: 0x0004440C
		public WaveIn WaveIn
		{
			get
			{
				return this.waveIn;
			}
			set
			{
				this.waveIn = value;
			}
		}

		// Token: 0x17000303 RID: 771
		// (get) Token: 0x06000B4D RID: 2893 RVA: 0x00046218 File Offset: 0x00044418
		// (set) Token: 0x06000B4E RID: 2894 RVA: 0x00046220 File Offset: 0x00044420
		public bool AudioVolumeChanged
		{
			get
			{
				return this.audioVolumeChanged;
			}
			set
			{
				this.audioVolumeChanged = value;
			}
		}

		// Token: 0x17000304 RID: 772
		// (get) Token: 0x06000B4F RID: 2895 RVA: 0x0004622C File Offset: 0x0004442C
		// (set) Token: 0x06000B50 RID: 2896 RVA: 0x00046234 File Offset: 0x00044434
		public float PreviousVolume
		{
			get
			{
				return this.previousVolume;
			}
			set
			{
				this.previousVolume = value;
			}
		}

		// Token: 0x04000759 RID: 1881
		int presetTime = 3600;

		// Token: 0x0400075A RID: 1882
		TimeSpan totalTime = TimeSpan.FromSeconds(0.0);

		// Token: 0x0400075B RID: 1883
		TimeSpan elapsedTime = TimeSpan.FromSeconds(0.0);

		// Token: 0x0400075C RID: 1884
		long timeInSamples;

		// Token: 0x0400075D RID: 1885
		bool recording;

		// Token: 0x0400075E RID: 1886
		bool testing;

		// Token: 0x0400075F RID: 1887
		bool stopTest;

		// Token: 0x04000760 RID: 1888
		bool stabilization;

		// Token: 0x04000761 RID: 1889
		WaveIn waveIn;

		// Token: 0x04000762 RID: 1890
		bool audioVolumeChanged;

		// Token: 0x04000763 RID: 1891
		float previousVolume;
	}
}
