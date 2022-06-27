using System;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x0200011D RID: 285
	public class GlobalConfigInfo
	{
		// Token: 0x170003DB RID: 987
		// (get) Token: 0x06000F15 RID: 3861 RVA: 0x00056BB4 File Offset: 0x00054DB4
		// (set) Token: 0x06000F16 RID: 3862 RVA: 0x00056BBC File Offset: 0x00054DBC
		public int MainFormTop
		{
			get
			{
				return this.mainFormTop;
			}
			set
			{
				this.mainFormTop = value;
			}
		}

		// Token: 0x170003DC RID: 988
		// (get) Token: 0x06000F17 RID: 3863 RVA: 0x00056BC8 File Offset: 0x00054DC8
		// (set) Token: 0x06000F18 RID: 3864 RVA: 0x00056BD0 File Offset: 0x00054DD0
		public int MainFormLeft
		{
			get
			{
				return this.mainFormLeft;
			}
			set
			{
				this.mainFormLeft = value;
			}
		}

		// Token: 0x170003DD RID: 989
		// (get) Token: 0x06000F19 RID: 3865 RVA: 0x00056BDC File Offset: 0x00054DDC
		// (set) Token: 0x06000F1A RID: 3866 RVA: 0x00056BE4 File Offset: 0x00054DE4
		public int MainFormWidth
		{
			get
			{
				return this.mainFormWidth;
			}
			set
			{
				this.mainFormWidth = value;
			}
		}

		// Token: 0x170003DE RID: 990
		// (get) Token: 0x06000F1B RID: 3867 RVA: 0x00056BF0 File Offset: 0x00054DF0
		// (set) Token: 0x06000F1C RID: 3868 RVA: 0x00056BF8 File Offset: 0x00054DF8
		public int MainFormHeight
		{
			get
			{
				return this.mainFormHeight;
			}
			set
			{
				this.mainFormHeight = value;
			}
		}

		// Token: 0x170003DF RID: 991
		// (get) Token: 0x06000F1D RID: 3869 RVA: 0x00056C04 File Offset: 0x00054E04
		// (set) Token: 0x06000F1E RID: 3870 RVA: 0x00056C0C File Offset: 0x00054E0C
		public bool MainFormMaximized
		{
			get
			{
				return this.mainFormMaximized;
			}
			set
			{
				this.mainFormMaximized = value;
			}
		}

		// Token: 0x170003E0 RID: 992
		// (get) Token: 0x06000F1F RID: 3871 RVA: 0x00056C18 File Offset: 0x00054E18
		// (set) Token: 0x06000F20 RID: 3872 RVA: 0x00056C20 File Offset: 0x00054E20
		public int DeviceConfigFormWidth
		{
			get
			{
				return this.deviceConfigFormWidth;
			}
			set
			{
				this.deviceConfigFormWidth = value;
			}
		}

		// Token: 0x170003E1 RID: 993
		// (get) Token: 0x06000F21 RID: 3873 RVA: 0x00056C2C File Offset: 0x00054E2C
		// (set) Token: 0x06000F22 RID: 3874 RVA: 0x00056C34 File Offset: 0x00054E34
		public int DeviceConfigFormHeight
		{
			get
			{
				return this.deviceConfigFormHeight;
			}
			set
			{
				this.deviceConfigFormHeight = value;
			}
		}

		// Token: 0x170003E2 RID: 994
		// (get) Token: 0x06000F23 RID: 3875 RVA: 0x00056C40 File Offset: 0x00054E40
		// (set) Token: 0x06000F24 RID: 3876 RVA: 0x00056C48 File Offset: 0x00054E48
		public int ROIConfigFormWidth
		{
			get
			{
				return this.roiConfigFormWidth;
			}
			set
			{
				this.roiConfigFormWidth = value;
			}
		}

		// Token: 0x170003E3 RID: 995
		// (get) Token: 0x06000F25 RID: 3877 RVA: 0x00056C54 File Offset: 0x00054E54
		// (set) Token: 0x06000F26 RID: 3878 RVA: 0x00056C5C File Offset: 0x00054E5C
		public int ROIConfigFormHeight
		{
			get
			{
				return this.roiConfigFormHeight;
			}
			set
			{
				this.roiConfigFormHeight = value;
			}
		}

		// Token: 0x170003E4 RID: 996
		// (get) Token: 0x06000F27 RID: 3879 RVA: 0x00056C68 File Offset: 0x00054E68
		// (set) Token: 0x06000F28 RID: 3880 RVA: 0x00056C70 File Offset: 0x00054E70
		[XmlArrayItem("ColumnSize")]
		public int[] DeviceConfigListColumnSizes
		{
			get
			{
				return this.deviceConfigListColumnSizes;
			}
			set
			{
				this.deviceConfigListColumnSizes = value;
			}
		}

		// Token: 0x170003E5 RID: 997
		// (get) Token: 0x06000F29 RID: 3881 RVA: 0x00056C7C File Offset: 0x00054E7C
		// (set) Token: 0x06000F2A RID: 3882 RVA: 0x00056C84 File Offset: 0x00054E84
		[XmlArrayItem("ColumnSize")]
		public int[] ROIConfigListColumnSizes
		{
			get
			{
				return this.roiConfigListColumnSizes;
			}
			set
			{
				this.roiConfigListColumnSizes = value;
			}
		}

		// Token: 0x170003E6 RID: 998
		// (get) Token: 0x06000F2B RID: 3883 RVA: 0x00056C90 File Offset: 0x00054E90
		// (set) Token: 0x06000F2C RID: 3884 RVA: 0x00056C98 File Offset: 0x00054E98
		public bool ShowPulseShape
		{
			get
			{
				return this.showPulseShape;
			}
			set
			{
				this.showPulseShape = value;
			}
		}

		// Token: 0x170003E7 RID: 999
		// (get) Token: 0x06000F2D RID: 3885 RVA: 0x00056CA4 File Offset: 0x00054EA4
		// (set) Token: 0x06000F2E RID: 3886 RVA: 0x00056CAC File Offset: 0x00054EAC
		public bool AntiAliasingPulseView
		{
			get
			{
				return this.antiAliasingPulseView;
			}
			set
			{
				this.antiAliasingPulseView = value;
			}
		}

		// Token: 0x170003E8 RID: 1000
		// (get) Token: 0x06000F2F RID: 3887 RVA: 0x00056CB8 File Offset: 0x00054EB8
		// (set) Token: 0x06000F30 RID: 3888 RVA: 0x00056CC0 File Offset: 0x00054EC0
		public bool DoSaveRawPulseData
		{
			get
			{
				return this.doSaveRawPulseData;
			}
			set
			{
				this.doSaveRawPulseData = value;
			}
		}

		// Token: 0x170003E9 RID: 1001
		// (get) Token: 0x06000F31 RID: 3889 RVA: 0x00056CCC File Offset: 0x00054ECC
		// (set) Token: 0x06000F32 RID: 3890 RVA: 0x00056CD4 File Offset: 0x00054ED4
		public ResultTranslation ResultTranslation
		{
			get
			{
				return this.resultTranslation;
			}
			set
			{
				this.resultTranslation = value;
			}
		}

		// Token: 0x170003EA RID: 1002
		// (get) Token: 0x06000F33 RID: 3891 RVA: 0x00056CE0 File Offset: 0x00054EE0
		// (set) Token: 0x06000F34 RID: 3892 RVA: 0x00056CE8 File Offset: 0x00054EE8
		public LayoutMode LayoutMode
		{
			get
			{
				return this.layoutMode;
			}
			set
			{
				this.layoutMode = value;
			}
		}

		// Token: 0x170003EB RID: 1003
		// (get) Token: 0x06000F35 RID: 3893 RVA: 0x00056CF4 File Offset: 0x00054EF4
		// (set) Token: 0x06000F36 RID: 3894 RVA: 0x00056CFC File Offset: 0x00054EFC
		public string Language
		{
			get
			{
				return this.language;
			}
			set
			{
				this.language = value;
			}
		}

		// Token: 0x170003EC RID: 1004
		// (get) Token: 0x06000F37 RID: 3895 RVA: 0x00056D08 File Offset: 0x00054F08
		// (set) Token: 0x06000F38 RID: 3896 RVA: 0x00056D10 File Offset: 0x00054F10
		public string OptionString
		{
			get
			{
				return this.optionString;
			}
			set
			{
				this.optionString = value;
			}
		}

		// Token: 0x170003ED RID: 1005
		// (get) Token: 0x06000F39 RID: 3897 RVA: 0x00056D1C File Offset: 0x00054F1C
		// (set) Token: 0x06000F3A RID: 3898 RVA: 0x00056D24 File Offset: 0x00054F24
		public ChartViewConfig ChartViewConfig
		{
			get
			{
				return this.chartViewConfig;
			}
			set
			{
				this.chartViewConfig = value;
			}
		}

		// Token: 0x170003EE RID: 1006
		// (get) Token: 0x06000F3B RID: 3899 RVA: 0x00056D30 File Offset: 0x00054F30
		// (set) Token: 0x06000F3C RID: 3900 RVA: 0x00056D38 File Offset: 0x00054F38
		public ColorConfig ColorConfig
		{
			get
			{
				return this.colorConfig;
			}
			set
			{
				this.colorConfig = value;
			}
		}

		// Token: 0x170003EF RID: 1007
		// (get) Token: 0x06000F3D RID: 3901 RVA: 0x00056D44 File Offset: 0x00054F44
		// (set) Token: 0x06000F3E RID: 3902 RVA: 0x00056D4C File Offset: 0x00054F4C
		public MeasurementConfig MeasurementConfig
		{
			get
			{
				return this.measurementConfig;
			}
			set
			{
				this.measurementConfig = value;
			}
		}

		// Token: 0x170003F0 RID: 1008
		// (get) Token: 0x06000F3F RID: 3903 RVA: 0x00056D58 File Offset: 0x00054F58
		// (set) Token: 0x06000F40 RID: 3904 RVA: 0x00056D60 File Offset: 0x00054F60
		public EasyControlConfig EasyControlConfig
		{
			get
			{
				return this.easyControlConfig;
			}
			set
			{
				this.easyControlConfig = value;
			}
		}

		// Token: 0x170003F1 RID: 1009
		// (get) Token: 0x06000F41 RID: 3905 RVA: 0x00056D6C File Offset: 0x00054F6C
		// (set) Token: 0x06000F42 RID: 3906 RVA: 0x00056D74 File Offset: 0x00054F74
		public SoundConfig SoundConfig
		{
			get
			{
				return this.soundConfig;
			}
			set
			{
				this.soundConfig = value;
			}
		}

		// Token: 0x040008AF RID: 2223
		int mainFormTop = 100;

		// Token: 0x040008B0 RID: 2224
		int mainFormLeft = 100;

		// Token: 0x040008B1 RID: 2225
		int mainFormWidth = 1120;

		// Token: 0x040008B2 RID: 2226
		int mainFormHeight = 850;

		// Token: 0x040008B3 RID: 2227
		bool mainFormMaximized;

		// Token: 0x040008B4 RID: 2228
		int deviceConfigFormWidth = 926;

		// Token: 0x040008B5 RID: 2229
		int deviceConfigFormHeight = 655;

		// Token: 0x040008B6 RID: 2230
		int roiConfigFormWidth = 1080;

		// Token: 0x040008B7 RID: 2231
		int roiConfigFormHeight = 772;

		// Token: 0x040008B8 RID: 2232
		int[] deviceConfigListColumnSizes = new int[]
		{
			240,
			120
		};

		// Token: 0x040008B9 RID: 2233
		int[] roiConfigListColumnSizes = new int[]
		{
			120,
			120
		};

		// Token: 0x040008BA RID: 2234
		bool showPulseShape = true;

		// Token: 0x040008BB RID: 2235
		bool antiAliasingPulseView;

		// Token: 0x040008BC RID: 2236
		bool doSaveRawPulseData = true;

		// Token: 0x040008BD RID: 2237
		ResultTranslation resultTranslation = ResultTranslation.Becquerels;

		// Token: 0x040008BE RID: 2238
		LayoutMode layoutMode;

		// Token: 0x040008BF RID: 2239
		string language = "OS";

		// Token: 0x040008C0 RID: 2240
		string optionString = "";

		// Token: 0x040008C1 RID: 2241
		ColorConfig colorConfig = new ColorConfig();

		// Token: 0x040008C2 RID: 2242
		ChartViewConfig chartViewConfig = new ChartViewConfig();

		// Token: 0x040008C3 RID: 2243
		MeasurementConfig measurementConfig = new MeasurementConfig();

		// Token: 0x040008C4 RID: 2244
		EasyControlConfig easyControlConfig = new EasyControlConfig();

		// Token: 0x040008C5 RID: 2245
		SoundConfig soundConfig = new SoundConfig();
	}
}
