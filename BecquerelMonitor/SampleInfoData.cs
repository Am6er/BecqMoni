using System;

namespace BecquerelMonitor
{
    // Token: 0x02000011 RID: 17
    public class SampleInfoData
    {
        // Token: 0x17000027 RID: 39
        // (get) Token: 0x0600006B RID: 107 RVA: 0x00002ACC File Offset: 0x00000CCC
        // (set) Token: 0x0600006C RID: 108 RVA: 0x00002AD4 File Offset: 0x00000CD4
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

        // Token: 0x17000028 RID: 40
        // (get) Token: 0x0600006D RID: 109 RVA: 0x00002AE0 File Offset: 0x00000CE0
        // (set) Token: 0x0600006E RID: 110 RVA: 0x00002AE8 File Offset: 0x00000CE8
        public string Location
        {
            get
            {
                return this.location;
            }
            set
            {
                this.location = value;
            }
        }

        // Token: 0x17000029 RID: 41
        // (get) Token: 0x0600006F RID: 111 RVA: 0x00002AF4 File Offset: 0x00000CF4
        // (set) Token: 0x06000070 RID: 112 RVA: 0x00002AFC File Offset: 0x00000CFC
        public DateTime Time
        {
            get
            {
                return this.time;
            }
            set
            {
                this.time = value;
            }
        }

        // Token: 0x1700002A RID: 42
        // (get) Token: 0x06000071 RID: 113 RVA: 0x00002B08 File Offset: 0x00000D08
        // (set) Token: 0x06000072 RID: 114 RVA: 0x00002B10 File Offset: 0x00000D10
        public double Weight
        {
            get
            {
                return this.weight;
            }
            set
            {
                this.weight = value;
            }
        }

        // Token: 0x1700002B RID: 43
        // (get) Token: 0x06000073 RID: 115 RVA: 0x00002B1C File Offset: 0x00000D1C
        // (set) Token: 0x06000074 RID: 116 RVA: 0x00002B24 File Offset: 0x00000D24
        public double Volume
        {
            get
            {
                return this.volume;
            }
            set
            {
                this.volume = value;
            }
        }

        // Token: 0x1700002C RID: 44
        // (get) Token: 0x06000075 RID: 117 RVA: 0x00002B30 File Offset: 0x00000D30
        // (set) Token: 0x06000076 RID: 118 RVA: 0x00002B38 File Offset: 0x00000D38
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

        public SampleInfoData Clone()
        {
            SampleInfoData sampleInfoData = new SampleInfoData();
            sampleInfoData.Name = this.Name;
            sampleInfoData.Volume = this.Volume;
            sampleInfoData.Note = this.Note;
            sampleInfoData.Weight = this.Weight;
            sampleInfoData.Time = this.Time;

            return sampleInfoData;
        }

        // Token: 0x04000026 RID: 38
        string name = "";

        // Token: 0x04000027 RID: 39
        string location = "";

        // Token: 0x04000028 RID: 40
        DateTime time = DateTime.Now;

        // Token: 0x04000029 RID: 41
        double weight = 1.0;

        // Token: 0x0400002A RID: 42
        double volume = 1.0;

        // Token: 0x0400002B RID: 43
        CDATA note = "";
    }
}
