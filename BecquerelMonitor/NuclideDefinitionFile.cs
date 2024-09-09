using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    // Token: 0x0200014E RID: 334
    public class NuclideDefinitionFile
    {
        // Token: 0x17000469 RID: 1129
        // (get) Token: 0x060010A1 RID: 4257 RVA: 0x0005AE5C File Offset: 0x0005905C
        // (set) Token: 0x060010A2 RID: 4258 RVA: 0x0005AE64 File Offset: 0x00059064
        [XmlArrayItem("Nuclide", typeof(NuclideDefinition))]
        public List<NuclideDefinition> NuclideDefinitions
        {
            get
            {
                return this.nuclideDefinitions;
            }
            set
            {
                this.nuclideDefinitions = value;
            }
        }

        [XmlArrayItem("NuclideSet", typeof(NuclideSet))]
        public List<NuclideSet> NuclideSets
        {
            get
            {
                return this.nuclideSets;
            }
            set
            {
                this.nuclideSets = value;
            }
        }

        // Token: 0x040009B0 RID: 2480
        List<NuclideDefinition> nuclideDefinitions = new List<NuclideDefinition>();
        List<NuclideSet> nuclideSets = new List<NuclideSet>();
    }
}
