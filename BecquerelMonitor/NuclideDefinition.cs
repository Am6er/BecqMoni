using System;
using System.Xml.Serialization;
using System.Drawing;

namespace BecquerelMonitor
{
    // Token: 0x0200014F RID: 335
    public class NuclideDefinition : IComparable
    {
        // Token: 0x1700046A RID: 1130
        // (get) Token: 0x060010A4 RID: 4260 RVA: 0x0005AE84 File Offset: 0x00059084
        // (set) Token: 0x060010A5 RID: 4261 RVA: 0x0005AE8C File Offset: 0x0005908C
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

        // Token: 0x1700046B RID: 1131
        // (get) Token: 0x060010A6 RID: 4262 RVA: 0x0005AE98 File Offset: 0x00059098
        // (set) Token: 0x060010A7 RID: 4263 RVA: 0x0005AEA0 File Offset: 0x000590A0
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

        // Token: 0x1700046C RID: 1132
        // (get) Token: 0x060010A8 RID: 4264 RVA: 0x0005AEAC File Offset: 0x000590AC
        // (set) Token: 0x060010A9 RID: 4265 RVA: 0x0005AEB4 File Offset: 0x000590B4
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

        public SerializableColor NuclideColor
        {
            get
            {
                return this.nuclideColor;
            }
            set
            {
                this.nuclideColor = value;
            }
        }

        // Token: 0x1700046D RID: 1133
        // (get) Token: 0x060010AA RID: 4266 RVA: 0x0005AEC0 File Offset: 0x000590C0
        // (set) Token: 0x060010AB RID: 4267 RVA: 0x0005AEC8 File Offset: 0x000590C8
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

        // Token: 0x1700046E RID: 1134
        // (get) Token: 0x060010AC RID: 4268 RVA: 0x0005AED4 File Offset: 0x000590D4
        // (set) Token: 0x060010AD RID: 4269 RVA: 0x0005AEDC File Offset: 0x000590DC
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

        // Token: 0x060010AE RID: 4270 RVA: 0x0005AEE8 File Offset: 0x000590E8
        public int CompareTo(object obj)
        {
            NuclideDefinition nuclideDefinition = (NuclideDefinition)obj;
            return this.Energy.CompareTo(nuclideDefinition.Energy);
        }

        // Token: 0x060010AF RID: 4271 RVA: 0x0005AF14 File Offset: 0x00059114
        public override string ToString()
        {
            return this.name;
        }

        // Token: 0x040009B1 RID: 2481
        string name = "";

        // Token: 0x040009B2 RID: 2482
        double energy;

        // Token: 0x040009B3 RID: 2483
        double halfLife = 1.0;

        // Token: 0x040009B4 RID: 2484
        CDATA note = "";

        // Token: 0x040009B5 RID: 2485
        bool dirty;

        SerializableColor nuclideColor = Color.Gray;
    }
}
