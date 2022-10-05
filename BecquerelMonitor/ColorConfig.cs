using System.Collections.Generic;
using System.Drawing;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000120 RID: 288
    public class ColorConfig
    {
        // Token: 0x17000409 RID: 1033
        // (get) Token: 0x06000F74 RID: 3956 RVA: 0x00057100 File Offset: 0x00055300
        // (set) Token: 0x06000F75 RID: 3957 RVA: 0x00057108 File Offset: 0x00055308
        public SerializableColor ActiveSpectrumColor
        {
            get
            {
                return this.activeSpectrumColor;
            }
            set
            {
                this.activeSpectrumColor = value;
            }
        }

        // Token: 0x1700040A RID: 1034
        // (get) Token: 0x06000F76 RID: 3958 RVA: 0x00057114 File Offset: 0x00055314
        // (set) Token: 0x06000F77 RID: 3959 RVA: 0x0005711C File Offset: 0x0005531C
        public SerializableColor BackgroundSpectrumColor
        {
            get
            {
                return this.backgroundSpectrumColor;
            }
            set
            {
                this.backgroundSpectrumColor = value;
            }
        }

        // Token: 0x1700040B RID: 1035
        // (get) Token: 0x06000F78 RID: 3960 RVA: 0x00057128 File Offset: 0x00055328
        // (set) Token: 0x06000F79 RID: 3961 RVA: 0x00057130 File Offset: 0x00055330
        public decimal ActiveSpectrumColorTransparency
        {
            get
            {
                return this.activeSpectrumColorTransparency;
            }
            set
            {
                this.activeSpectrumColorTransparency = value;
            }
        }

        // Token: 0x1700040C RID: 1036
        // (get) Token: 0x06000F7A RID: 3962 RVA: 0x0005713C File Offset: 0x0005533C
        // (set) Token: 0x06000F7B RID: 3963 RVA: 0x00057144 File Offset: 0x00055344
        public decimal BackgroundSpectrumColorTransparency
        {
            get
            {
                return this.backgroundSpectrumColorTransparency;
            }
            set
            {
                this.backgroundSpectrumColorTransparency = value;
            }
        }

        // Token: 0x1700040D RID: 1037
        // (get) Token: 0x06000F7C RID: 3964 RVA: 0x00057150 File Offset: 0x00055350
        // (set) Token: 0x06000F7D RID: 3965 RVA: 0x00057158 File Offset: 0x00055358
        public int SpectrumDrawingOrder
        {
            get
            {
                return this.spectrumDrawingOrder;
            }
            set
            {
                this.spectrumDrawingOrder = value;
            }
        }

        // Token: 0x1700040E RID: 1038
        // (get) Token: 0x06000F7E RID: 3966 RVA: 0x00057164 File Offset: 0x00055364
        // (set) Token: 0x06000F7F RID: 3967 RVA: 0x0005716C File Offset: 0x0005536C
        [XmlArrayItem("SpectrumColor")]
        public List<SerializableColor> SpectrumColorList
        {
            get
            {
                return this.spectrumColorList;
            }
            set
            {
                this.spectrumColorList = value;
            }
        }

        // Token: 0x1700040F RID: 1039
        // (get) Token: 0x06000F80 RID: 3968 RVA: 0x00057178 File Offset: 0x00055378
        // (set) Token: 0x06000F81 RID: 3969 RVA: 0x00057180 File Offset: 0x00055380
        public SerializableColor BackgroundColor
        {
            get
            {
                return this.backgroundColor;
            }
            set
            {
                this.backgroundColor = value;
            }
        }

        // Token: 0x17000410 RID: 1040
        // (get) Token: 0x06000F82 RID: 3970 RVA: 0x0005718C File Offset: 0x0005538C
        // (set) Token: 0x06000F83 RID: 3971 RVA: 0x00057194 File Offset: 0x00055394
        public SerializableColor GridColor1
        {
            get
            {
                return this.gridColor1;
            }
            set
            {
                this.gridColor1 = value;
            }
        }

        // Token: 0x17000411 RID: 1041
        // (get) Token: 0x06000F84 RID: 3972 RVA: 0x000571A0 File Offset: 0x000553A0
        // (set) Token: 0x06000F85 RID: 3973 RVA: 0x000571A8 File Offset: 0x000553A8
        public SerializableColor GridColor2
        {
            get
            {
                return this.gridColor2;
            }
            set
            {
                this.gridColor2 = value;
            }
        }

        // Token: 0x17000412 RID: 1042
        // (get) Token: 0x06000F86 RID: 3974 RVA: 0x000571B4 File Offset: 0x000553B4
        // (set) Token: 0x06000F87 RID: 3975 RVA: 0x000571BC File Offset: 0x000553BC
        public SerializableColor ROIBorderColor
        {
            get
            {
                return this.roiBorderColor;
            }
            set
            {
                this.roiBorderColor = value;
            }
        }

        // Token: 0x17000413 RID: 1043
        // (get) Token: 0x06000F88 RID: 3976 RVA: 0x000571C8 File Offset: 0x000553C8
        // (set) Token: 0x06000F89 RID: 3977 RVA: 0x000571D0 File Offset: 0x000553D0
        public SerializableColor ROIBackgroundColor
        {
            get
            {
                return this.roiBackgroundColor;
            }
            set
            {
                this.roiBackgroundColor = value;
            }
        }

        // Token: 0x17000414 RID: 1044
        // (get) Token: 0x06000F8A RID: 3978 RVA: 0x000571DC File Offset: 0x000553DC
        // (set) Token: 0x06000F8B RID: 3979 RVA: 0x000571E4 File Offset: 0x000553E4
        public SerializableColor ROINetColor
        {
            get
            {
                return this.roiNetColor;
            }
            set
            {
                this.roiNetColor = value;
            }
        }

        // Token: 0x17000415 RID: 1045
        // (get) Token: 0x06000F8C RID: 3980 RVA: 0x000571F0 File Offset: 0x000553F0
        // (set) Token: 0x06000F8D RID: 3981 RVA: 0x000571F8 File Offset: 0x000553F8
        public SerializableColor SelectionBorderColor
        {
            get
            {
                return this.selectionBorderColor;
            }
            set
            {
                this.selectionBorderColor = value;
            }
        }

        // Token: 0x17000416 RID: 1046
        // (get) Token: 0x06000F8E RID: 3982 RVA: 0x00057204 File Offset: 0x00055404
        // (set) Token: 0x06000F8F RID: 3983 RVA: 0x0005720C File Offset: 0x0005540C
        public SerializableColor SelectionBackgroundColor
        {
            get
            {
                return this.selectionBackgroundColor;
            }
            set
            {
                this.selectionBackgroundColor = value;
            }
        }

        // Token: 0x17000417 RID: 1047
        // (get) Token: 0x06000F90 RID: 3984 RVA: 0x00057218 File Offset: 0x00055418
        // (set) Token: 0x06000F91 RID: 3985 RVA: 0x00057220 File Offset: 0x00055420
        public SerializableColor SelectionNetColor
        {
            get
            {
                return this.selectionNetColor;
            }
            set
            {
                this.selectionNetColor = value;
            }
        }

        // Token: 0x17000418 RID: 1048
        // (get) Token: 0x06000F92 RID: 3986 RVA: 0x0005722C File Offset: 0x0005542C
        // (set) Token: 0x06000F93 RID: 3987 RVA: 0x00057234 File Offset: 0x00055434
        public SerializableColor AxisBackgroundColor
        {
            get
            {
                return this.axisBackgroundColor;
            }
            set
            {
                this.axisBackgroundColor = value;
            }
        }

        // Token: 0x17000419 RID: 1049
        // (get) Token: 0x06000F94 RID: 3988 RVA: 0x00057240 File Offset: 0x00055440
        // (set) Token: 0x06000F95 RID: 3989 RVA: 0x00057248 File Offset: 0x00055448
        public SerializableColor AxisFigureColor
        {
            get
            {
                return this.axisFigureColor;
            }
            set
            {
                this.axisFigureColor = value;
            }
        }

        // Token: 0x1700041A RID: 1050
        // (get) Token: 0x06000F96 RID: 3990 RVA: 0x00057254 File Offset: 0x00055454
        // (set) Token: 0x06000F97 RID: 3991 RVA: 0x0005725C File Offset: 0x0005545C
        public SerializableColor AxisDivisionColor
        {
            get
            {
                return this.axisDivisionColor;
            }
            set
            {
                this.axisDivisionColor = value;
            }
        }

        // Token: 0x1700041B RID: 1051
        // (get) Token: 0x06000F98 RID: 3992 RVA: 0x00057268 File Offset: 0x00055468
        // (set) Token: 0x06000F99 RID: 3993 RVA: 0x00057270 File Offset: 0x00055470
        public SerializableColor PeakBackgroundColor
        {
            get
            {
                return this.peakBackgroundColor;
            }
            set
            {
                this.peakBackgroundColor = value;
            }
        }

        // Token: 0x1700041C RID: 1052
        // (get) Token: 0x06000F9A RID: 3994 RVA: 0x0005727C File Offset: 0x0005547C
        // (set) Token: 0x06000F9B RID: 3995 RVA: 0x00057284 File Offset: 0x00055484
        public SerializableColor PeakFigureColor
        {
            get
            {
                return this.peakFigureColor;
            }
            set
            {
                this.peakFigureColor = value;
            }
        }

        // Token: 0x1700041D RID: 1053
        // (get) Token: 0x06000F9C RID: 3996 RVA: 0x00057290 File Offset: 0x00055490
        // (set) Token: 0x06000F9D RID: 3997 RVA: 0x00057298 File Offset: 0x00055498
        public SerializableColor PeakLineColor
        {
            get
            {
                return this.peakLineColor;
            }
            set
            {
                this.peakLineColor = value;
            }
        }

        // Token: 0x1700041E RID: 1054
        // (get) Token: 0x06000F9E RID: 3998 RVA: 0x000572A4 File Offset: 0x000554A4
        // (set) Token: 0x06000F9F RID: 3999 RVA: 0x000572AC File Offset: 0x000554AC
        public SerializableColor CursorColor
        {
            get
            {
                return this.cursorColor;
            }
            set
            {
                this.cursorColor = value;
            }
        }

        // Token: 0x1700041F RID: 1055
        // (get) Token: 0x06000FA0 RID: 4000 RVA: 0x000572B8 File Offset: 0x000554B8
        // (set) Token: 0x06000FA1 RID: 4001 RVA: 0x000572C0 File Offset: 0x000554C0
        public SerializableColor BlankAreaColor
        {
            get
            {
                return this.blankAreaColor;
            }
            set
            {
                this.blankAreaColor = value;
            }
        }

        public SerializableColor UnknownPeakColor
        {
            get
            {
                return this.unknownPeakColor;
            }
            set
            {
                this.unknownPeakColor = value;
            }
        }

        public SerializableColor BgDiffColor
        {
            get
            {
                return this.bgdiffColor;
            }
            set
            {
                this.bgdiffColor = value;
            }
        }

        // Token: 0x06000FA2 RID: 4002 RVA: 0x000572CC File Offset: 0x000554CC
        public void InitializeSpectrumColor()
        {
            this.spectrumColorList = this.initialSpectrumColorList;
        }

        // Token: 0x040008DD RID: 2269
        List<SerializableColor> initialSpectrumColorList = new List<SerializableColor>
        {
            Color.DarkBlue,
            Color.FromArgb(215, 13, 18),
            Color.FromArgb(0, 176, 0),
            Color.Goldenrod,
            Color.Purple,
            Color.FromArgb(95, 169, 175),
            Color.YellowGreen,
            Color.Gray,
            Color.DarkBlue,
            Color.FromArgb(215, 13, 18),
            Color.FromArgb(0, 176, 0),
            Color.Goldenrod,
            Color.Purple,
            Color.FromArgb(95, 169, 175),
            Color.YellowGreen,
            Color.Gray
        };

        // Token: 0x040008DE RID: 2270
        SerializableColor activeSpectrumColor = Color.Black;

        // Token: 0x040008DF RID: 2271
        SerializableColor backgroundSpectrumColor = Color.Blue;

        SerializableColor bgdiffColor = Color.DarkViolet;

        // Token: 0x040008E0 RID: 2272
        List<SerializableColor> spectrumColorList = new List<SerializableColor>();

        // Token: 0x040008E1 RID: 2273
        SerializableColor backgroundColor = Color.White;

        // Token: 0x040008E2 RID: 2274
        SerializableColor gridColor1 = Color.FromArgb(220, 220, 220);

        // Token: 0x040008E3 RID: 2275
        SerializableColor gridColor2 = Color.FromArgb(240, 240, 240);

        // Token: 0x040008E4 RID: 2276
        SerializableColor roiBorderColor = Color.LightGreen;

        // Token: 0x040008E5 RID: 2277
        SerializableColor roiBackgroundColor = Color.FromArgb(236, 255, 236);

        // Token: 0x040008E6 RID: 2278
        SerializableColor roiNetColor = Color.LightGreen;

        // Token: 0x040008E7 RID: 2279
        SerializableColor selectionBorderColor = Color.PaleVioletRed;

        // Token: 0x040008E8 RID: 2280
        SerializableColor selectionBackgroundColor = Color.FromArgb(255, 230, 230);

        // Token: 0x040008E9 RID: 2281
        SerializableColor selectionNetColor = Color.OrangeRed;

        // Token: 0x040008EA RID: 2282
        SerializableColor axisBackgroundColor = Color.LightGray;

        // Token: 0x040008EB RID: 2283
        SerializableColor axisFigureColor = Color.Black;

        // Token: 0x040008EC RID: 2284
        SerializableColor axisDivisionColor = Color.Brown;

        // Token: 0x040008ED RID: 2285
        SerializableColor peakBackgroundColor = Color.LightYellow;

        // Token: 0x040008EE RID: 2286
        SerializableColor peakFigureColor = Color.Black;

        // Token: 0x040008EF RID: 2287
        SerializableColor peakLineColor = Color.Red;

        // Token: 0x040008F0 RID: 2288
        SerializableColor cursorColor = Color.Red;

        // Token: 0x040008F1 RID: 2289
        SerializableColor blankAreaColor = Color.LightGray;

        SerializableColor unknownPeakColor = Color.Gray;

        // Token: 0x040008F2 RID: 2290
        decimal activeSpectrumColorTransparency = 50m;

        // Token: 0x040008F3 RID: 2291
        decimal backgroundSpectrumColorTransparency = 50m;

        // Token: 0x040008F4 RID: 2292
        int spectrumDrawingOrder;
    }
}
