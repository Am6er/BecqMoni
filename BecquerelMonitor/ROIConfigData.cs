using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x02000084 RID: 132
    public class ROIConfigData : IComparable
    {
        // Token: 0x170001FE RID: 510
        // (get) Token: 0x060006B0 RID: 1712 RVA: 0x00028278 File Offset: 0x00026478
        // (set) Token: 0x060006B1 RID: 1713 RVA: 0x00028280 File Offset: 0x00026480
        public string FormatVersion
        {
            get
            {
                return this.formatVersion;
            }
            set
            {
                this.formatVersion = value;
            }
        }

        // Token: 0x170001FF RID: 511
        // (get) Token: 0x060006B2 RID: 1714 RVA: 0x0002828C File Offset: 0x0002648C
        // (set) Token: 0x060006B3 RID: 1715 RVA: 0x00028294 File Offset: 0x00026494
        public string Guid
        {
            get
            {
                return this.guid;
            }
            set
            {
                this.guid = value;
            }
        }

        // Token: 0x17000200 RID: 512
        // (get) Token: 0x060006B4 RID: 1716 RVA: 0x000282A0 File Offset: 0x000264A0
        // (set) Token: 0x060006B5 RID: 1717 RVA: 0x000282A8 File Offset: 0x000264A8
        [XmlIgnore]
        public string Filename
        {
            get
            {
                return this.filename;
            }
            set
            {
                this.filename = value;
            }
        }

        // Token: 0x17000201 RID: 513
        // (get) Token: 0x060006B6 RID: 1718 RVA: 0x000282B4 File Offset: 0x000264B4
        // (set) Token: 0x060006B7 RID: 1719 RVA: 0x000282BC File Offset: 0x000264BC
        [XmlIgnore]
        public string OriginalFilename
        {
            get
            {
                return this.originalFilename;
            }
            set
            {
                this.originalFilename = value;
            }
        }

        // Token: 0x17000202 RID: 514
        // (get) Token: 0x060006B8 RID: 1720 RVA: 0x000282C8 File Offset: 0x000264C8
        // (set) Token: 0x060006B9 RID: 1721 RVA: 0x000282D0 File Offset: 0x000264D0
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

        // Token: 0x17000203 RID: 515
        // (get) Token: 0x060006BA RID: 1722 RVA: 0x000282DC File Offset: 0x000264DC
        // (set) Token: 0x060006BB RID: 1723 RVA: 0x000282E4 File Offset: 0x000264E4
        public DateTime LastUpdated
        {
            get
            {
                return this.lastUpdated;
            }
            set
            {
                this.lastUpdated = value;
            }
        }

        // Token: 0x17000204 RID: 516
        // (get) Token: 0x060006BC RID: 1724 RVA: 0x000282F0 File Offset: 0x000264F0
        // (set) Token: 0x060006BD RID: 1725 RVA: 0x000282F8 File Offset: 0x000264F8
        public List<ROIDefinitionData> ROIDefinitions
        {
            get
            {
                return this.roiDefinitions;
            }
            set
            {
                this.roiDefinitions = value;
            }
        }

        // Token: 0x17000205 RID: 517
        // (get) Token: 0x060006BE RID: 1726 RVA: 0x00028304 File Offset: 0x00026504
        // (set) Token: 0x060006BF RID: 1727 RVA: 0x0002830C File Offset: 0x0002650C
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

        // Token: 0x17000206 RID: 518
        // (get) Token: 0x060006C0 RID: 1728 RVA: 0x00028318 File Offset: 0x00026518
        // (set) Token: 0x060006C1 RID: 1729 RVA: 0x00028320 File Offset: 0x00026520
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

        // Token: 0x060006C2 RID: 1730 RVA: 0x0002832C File Offset: 0x0002652C
        public ROIConfigData()
        {
        }

        // Token: 0x060006C3 RID: 1731 RVA: 0x00028380 File Offset: 0x00026580
        public void InitFormatVersion()
        {
            this.formatVersion = "120920";
        }

        // Token: 0x060006C4 RID: 1732 RVA: 0x00028390 File Offset: 0x00026590
        public ROIConfigData(ROIConfigData config)
        {
            this.formatVersion = string.Copy(config.formatVersion);
            this.guid = string.Copy(config.guid);
            this.filename = string.Copy(config.filename);
            this.name = string.Copy(config.name);
            this.lastUpdated = config.lastUpdated;
            this.originalFilename = string.Copy(config.originalFilename);
            this.roiDefinitions = new List<ROIDefinitionData>();
            for (int i = 0; i < config.roiDefinitions.Count; i++)
            {
                this.roiDefinitions.Add(config.roiDefinitions[i].Clone());
            }
            this.note = new CDATA(config.note);
            this.dirty = config.dirty;
        }

        // Token: 0x060006C5 RID: 1733 RVA: 0x000284A8 File Offset: 0x000266A8
        public ROIConfigReference CreateReference()
        {
            return new ROIConfigReference
            {
                Name = this.name,
                Guid = this.guid
            };
        }

        // Token: 0x060006C6 RID: 1734 RVA: 0x000284D8 File Offset: 0x000266D8
        int IComparable.CompareTo(object obj)
        {
            ROIConfigData roiconfigData = (ROIConfigData)obj;
            return DateTime.Compare(roiconfigData.LastUpdated, this.LastUpdated);
        }

        // Token: 0x060006C7 RID: 1735 RVA: 0x00028504 File Offset: 0x00026704
        public ROIConfigData Clone()
        {
            return new ROIConfigData(this);
        }

        // Token: 0x0400037F RID: 895
        const string formatVersionString = "120920";

        // Token: 0x04000380 RID: 896
        string formatVersion;

        // Token: 0x04000381 RID: 897
        string guid;

        // Token: 0x04000382 RID: 898
        string filename;

        // Token: 0x04000383 RID: 899
        string name = "";

        // Token: 0x04000384 RID: 900
        DateTime lastUpdated = DateTime.Now;

        // Token: 0x04000385 RID: 901
        string originalFilename = "";

        // Token: 0x04000386 RID: 902
        List<ROIDefinitionData> roiDefinitions = new List<ROIDefinitionData>();

        // Token: 0x04000387 RID: 903
        CDATA note = "";

        // Token: 0x04000388 RID: 904
        bool dirty;
    }
}
