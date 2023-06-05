using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200003B RID: 59
    public class ROIDefinitionData
    {
        // Token: 0x1700012F RID: 303
        // (get) Token: 0x060002D5 RID: 725 RVA: 0x0000D71C File Offset: 0x0000B91C
        // (set) Token: 0x060002D6 RID: 726 RVA: 0x0000D724 File Offset: 0x0000B924
        public string Name
        {
            get
            {
                return this.name;
            }
            set
            {
                this.name = value;
            }
        }

        // Token: 0x17000130 RID: 304
        // (get) Token: 0x060002D7 RID: 727 RVA: 0x0000D730 File Offset: 0x0000B930
        // (set) Token: 0x060002D8 RID: 728 RVA: 0x0000D738 File Offset: 0x0000B938
        public bool Enabled
        {
            get
            {
                return this.enabled;
            }
            set
            {
                this.enabled = value;
            }
        }

        // Token: 0x17000131 RID: 305
        // (get) Token: 0x060002D9 RID: 729 RVA: 0x0000D744 File Offset: 0x0000B944
        // (set) Token: 0x060002DA RID: 730 RVA: 0x0000D74C File Offset: 0x0000B94C
        public double PeakEnergy
        {
            get
            {
                return this.peakEnergy;
            }
            set
            {
                this.peakEnergy = value;
            }
        }

        // Token: 0x17000132 RID: 306
        // (get) Token: 0x060002DB RID: 731 RVA: 0x0000D758 File Offset: 0x0000B958
        // (set) Token: 0x060002DC RID: 732 RVA: 0x0000D760 File Offset: 0x0000B960
        public double LowerLimit
        {
            get
            {
                return this.lowerLimit;
            }
            set
            {
                this.lowerLimit = value;
            }
        }

        // Token: 0x17000133 RID: 307
        // (get) Token: 0x060002DD RID: 733 RVA: 0x0000D76C File Offset: 0x0000B96C
        // (set) Token: 0x060002DE RID: 734 RVA: 0x0000D774 File Offset: 0x0000B974
        public double UpperLimit
        {
            get
            {
                return this.upperLimit;
            }
            set
            {
                this.upperLimit = value;
            }
        }

        // Token: 0x17000134 RID: 308
        // (get) Token: 0x060002DF RID: 735 RVA: 0x0000D780 File Offset: 0x0000B980
        // (set) Token: 0x060002E0 RID: 736 RVA: 0x0000D788 File Offset: 0x0000B988
        public SerializableColor Color
        {
            get
            {
                return this.color;
            }
            set
            {
                this.color = value;
            }
        }

        // Token: 0x17000135 RID: 309
        // (get) Token: 0x060002E1 RID: 737 RVA: 0x0000D794 File Offset: 0x0000B994
        // (set) Token: 0x060002E2 RID: 738 RVA: 0x0000D79C File Offset: 0x0000B99C
        public double BecquerelCoefficient
        {
            get
            {
                return this.becquerelCoefficient;
            }
            set
            {
                this.becquerelCoefficient = value;
            }
        }

        // Token: 0x17000136 RID: 310
        // (get) Token: 0x060002E3 RID: 739 RVA: 0x0000D7A8 File Offset: 0x0000B9A8
        // (set) Token: 0x060002E4 RID: 740 RVA: 0x0000D7B0 File Offset: 0x0000B9B0
        public double BecquerelCoefficientError
        {
            get
            {
                return this.becquerelCoefficientError;
            }
            set
            {
                this.becquerelCoefficientError = value;
            }
        }

        // Token: 0x17000137 RID: 311
        // (get) Token: 0x060002E5 RID: 741 RVA: 0x0000D7BC File Offset: 0x0000B9BC
        // (set) Token: 0x060002E6 RID: 742 RVA: 0x0000D7C4 File Offset: 0x0000B9C4
        public double HalfLife
        {
            get
            {
                return this.halfLife;
            }
            set
            {
                this.halfLife = value;
            }
        }

        public double Intencity
        {
            get
            {
                return this.intencity;
            }
            set
            {
                this.intencity = value;
            }
        }

        // Token: 0x17000138 RID: 312
        // (get) Token: 0x060002E7 RID: 743 RVA: 0x0000D7D0 File Offset: 0x0000B9D0
        // (set) Token: 0x060002E8 RID: 744 RVA: 0x0000D7D8 File Offset: 0x0000B9D8
        public CDATA Note
        {
            get
            {
                return this.note;
            }
            set
            {
                this.note = value;
            }
        }

        // Token: 0x17000139 RID: 313
        // (get) Token: 0x060002E9 RID: 745 RVA: 0x0000D7E4 File Offset: 0x0000B9E4
        // (set) Token: 0x060002EA RID: 746 RVA: 0x0000D7EC File Offset: 0x0000B9EC
        [XmlArrayItem(typeof(ROICovellMethodData))]
        [XmlArrayItem(typeof(ROISimpleDifferenceData))]
        [XmlArrayItem(typeof(ROIReferenceData))]
        public List<ROIPrimitiveData> ROIPrimitives
        {
            get
            {
                return this.roiPrimitives;
            }
            set
            {
                this.roiPrimitives = value;
            }
        }

        // Token: 0x1700013A RID: 314
        // (get) Token: 0x060002EB RID: 747 RVA: 0x0000D7F8 File Offset: 0x0000B9F8
        // (set) Token: 0x060002EC RID: 748 RVA: 0x0000D800 File Offset: 0x0000BA00
        [XmlIgnore]
        public bool Dirty
        {
            get
            {
                return this.dirty;
            }
            set
            {
                this.dirty = value;
            }
        }

        // Token: 0x1700013B RID: 315
        // (get) Token: 0x060002ED RID: 749 RVA: 0x0000D80C File Offset: 0x0000BA0C
        // (set) Token: 0x060002EE RID: 750 RVA: 0x0000D814 File Offset: 0x0000BA14
        [XmlIgnore]
        public bool IsValidResult
        {
            get
            {
                return this.isValidResult;
            }
            set
            {
                this.isValidResult = value;
            }
        }

        // Token: 0x1700013C RID: 316
        // (get) Token: 0x060002EF RID: 751 RVA: 0x0000D820 File Offset: 0x0000BA20
        // (set) Token: 0x060002F0 RID: 752 RVA: 0x0000D828 File Offset: 0x0000BA28
        [XmlIgnore]
        public double ResultCount
        {
            get
            {
                return this.resultCount;
            }
            set
            {
                this.resultCount = value;
            }
        }

        // Token: 0x1700013D RID: 317
        // (get) Token: 0x060002F1 RID: 753 RVA: 0x0000D834 File Offset: 0x0000BA34
        // (set) Token: 0x060002F2 RID: 754 RVA: 0x0000D83C File Offset: 0x0000BA3C
        [XmlIgnore]
        public double ResultError
        {
            get
            {
                return this.resultError;
            }
            set
            {
                this.resultError = value;
            }
        }

        // Token: 0x1700013E RID: 318
        // (get) Token: 0x060002F3 RID: 755 RVA: 0x0000D848 File Offset: 0x0000BA48
        // (set) Token: 0x060002F4 RID: 756 RVA: 0x0000D850 File Offset: 0x0000BA50
        [XmlIgnore]
        public double MDA
        {
            get
            {
                return this.mda;
            }
            set
            {
                this.mda = value;
            }
        }

        // Token: 0x060002F5 RID: 757 RVA: 0x0000D85C File Offset: 0x0000BA5C
        public ROIDefinitionData()
        {
        }

        // Token: 0x060002F6 RID: 758 RVA: 0x0000D8B8 File Offset: 0x0000BAB8
        public ROIDefinitionData(ROIDefinitionData roi)
        {
            this.name = string.Copy(roi.name);
            this.enabled = roi.enabled;
            this.PeakEnergy = roi.peakEnergy;
            this.lowerLimit = roi.lowerLimit;
            this.upperLimit = roi.upperLimit;
            this.color = roi.color;
            this.halfLife = roi.halfLife;
            this.intencity = roi.intencity;
            this.becquerelCoefficient = roi.becquerelCoefficient;
            this.becquerelCoefficientError = roi.becquerelCoefficientError;
            this.note = new CDATA(roi.note);
            this.roiPrimitives = new List<ROIPrimitiveData>();
            for (int i = 0; i < roi.roiPrimitives.Count; i++)
            {
                this.roiPrimitives.Add(roi.roiPrimitives[i].Clone());
            }
            this.dirty = roi.dirty;
            this.isValidResult = roi.isValidResult;
            this.resultCount = roi.resultCount;
            this.resultError = roi.resultError;
        }

        // Token: 0x060002F7 RID: 759 RVA: 0x0000DA0C File Offset: 0x0000BC0C
        public ROIDefinitionData Clone()
        {
            return new ROIDefinitionData(this);
        }

        // Token: 0x04000101 RID: 257
        string name = "";

        // Token: 0x04000102 RID: 258
        bool enabled = true;

        // Token: 0x04000103 RID: 259
        double peakEnergy;

        // Token: 0x04000104 RID: 260
        double lowerLimit;

        // Token: 0x04000105 RID: 261
        double upperLimit;

        // Token: 0x04000106 RID: 262
        SerializableColor color = System.Drawing.Color.FromArgb(255, 0, 0);

        // Token: 0x04000107 RID: 263
        double halfLife;

        double intencity;

        // Token: 0x04000108 RID: 264
        double becquerelCoefficient;

        // Token: 0x04000109 RID: 265
        double becquerelCoefficientError;

        // Token: 0x0400010A RID: 266
        CDATA note = "";

        // Token: 0x0400010B RID: 267
        List<ROIPrimitiveData> roiPrimitives = new List<ROIPrimitiveData>();

        // Token: 0x0400010C RID: 268
        bool dirty;

        // Token: 0x0400010D RID: 269
        bool isValidResult;

        // Token: 0x0400010E RID: 270
        double resultCount;

        // Token: 0x0400010F RID: 271
        double resultError;

        // Token: 0x04000110 RID: 272
        double mda;
    }
}
