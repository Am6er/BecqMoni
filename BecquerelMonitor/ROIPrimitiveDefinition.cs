using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    // Token: 0x0200006E RID: 110
    public class ROIPrimitiveDefinition
    {
        // Token: 0x170001B6 RID: 438
        // (get) Token: 0x060005B2 RID: 1458 RVA: 0x000241DC File Offset: 0x000223DC
        // (set) Token: 0x060005B3 RID: 1459 RVA: 0x000241E4 File Offset: 0x000223E4
        public static List<ROIPrimitiveDefinition> Definitions
        {
            get
            {
                return ROIPrimitiveDefinition.definitions;
            }
            set
            {
                ROIPrimitiveDefinition.definitions = value;
            }
        }

        // Token: 0x170001B7 RID: 439
        // (get) Token: 0x060005B4 RID: 1460 RVA: 0x000241EC File Offset: 0x000223EC
        // (set) Token: 0x060005B5 RID: 1461 RVA: 0x000241F4 File Offset: 0x000223F4
        public static Dictionary<string, ROIPrimitiveDefinition> DefinitionsMap
        {
            get
            {
                return ROIPrimitiveDefinition.definitionsMap;
            }
            set
            {
                ROIPrimitiveDefinition.definitionsMap = value;
            }
        }

        // Token: 0x170001B8 RID: 440
        // (get) Token: 0x060005B6 RID: 1462 RVA: 0x000241FC File Offset: 0x000223FC
        // (set) Token: 0x060005B7 RID: 1463 RVA: 0x00024204 File Offset: 0x00022404
        public int Id
        {
            get
            {
                return this.id;
            }
            set
            {
                this.id = value;
            }
        }

        // Token: 0x170001B9 RID: 441
        // (get) Token: 0x060005B8 RID: 1464 RVA: 0x00024210 File Offset: 0x00022410
        // (set) Token: 0x060005B9 RID: 1465 RVA: 0x00024218 File Offset: 0x00022418
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

        // Token: 0x170001BA RID: 442
        // (get) Token: 0x060005BA RID: 1466 RVA: 0x00024224 File Offset: 0x00022424
        // (set) Token: 0x060005BB RID: 1467 RVA: 0x0002422C File Offset: 0x0002242C
        public string Translation
        {
            get
            {
                return this.translation;
            }
            set
            {
                this.translation = value;
            }
        }

        // Token: 0x170001BB RID: 443
        // (get) Token: 0x060005BC RID: 1468 RVA: 0x00024238 File Offset: 0x00022438
        // (set) Token: 0x060005BD RID: 1469 RVA: 0x00024240 File Offset: 0x00022440
        public Type TypeOfData
        {
            get
            {
                return this.typeOfData;
            }
            set
            {
                this.typeOfData = value;
            }
        }

        // Token: 0x170001BC RID: 444
        // (get) Token: 0x060005BE RID: 1470 RVA: 0x0002424C File Offset: 0x0002244C
        // (set) Token: 0x060005BF RID: 1471 RVA: 0x00024254 File Offset: 0x00022454
        public Type TypeOfControl
        {
            get
            {
                return this.typeOfControl;
            }
            set
            {
                this.typeOfControl = value;
            }
        }

        // Token: 0x060005C0 RID: 1472 RVA: 0x00024260 File Offset: 0x00022460
        public static void InitializeROIPrimitiveDefinitions()
        {
            ROIPrimitiveDefinition.definitionsMap = new Dictionary<string, ROIPrimitiveDefinition>();
            ROIPrimitiveDefinition.definitions = new List<ROIPrimitiveDefinition>();
            ROIPrimitiveDefinition roiprimitiveDefinition = new ROIPrimitiveDefinition(0, "BG difference", Resources.ROIPrimitiveTypeBGDifference, typeof(ROISimpleDifferenceData), typeof(ROISimpleDifferenceControl));
            ROIPrimitiveDefinition.definitions.Add(roiprimitiveDefinition);
            ROIPrimitiveDefinition.definitionsMap.Add(roiprimitiveDefinition.Name, roiprimitiveDefinition);
            roiprimitiveDefinition = new ROIPrimitiveDefinition(0, "Covell Method", Resources.ROIPrimitiveTypeCovellMethod, typeof(ROICovellMethodData), typeof(ROICovellMethodControl));
            ROIPrimitiveDefinition.definitions.Add(roiprimitiveDefinition);
            ROIPrimitiveDefinition.definitionsMap.Add(roiprimitiveDefinition.Name, roiprimitiveDefinition);
            roiprimitiveDefinition = new ROIPrimitiveDefinition(0, "Reference", Resources.ROIPrimitiveTypeROIReference, typeof(ROIReferenceData), typeof(ROIReferenceControl));
            ROIPrimitiveDefinition.definitions.Add(roiprimitiveDefinition);
            ROIPrimitiveDefinition.definitionsMap.Add(roiprimitiveDefinition.Name, roiprimitiveDefinition);
        }

        // Token: 0x060005C1 RID: 1473 RVA: 0x00024348 File Offset: 0x00022548
        public static int GetPrimitiveIndex(string name)
        {
            int result = -1;
            for (int i = 0; i < ROIPrimitiveDefinition.definitions.Count; i++)
            {
                if (ROIPrimitiveDefinition.definitions[i].Name == name)
                {
                    result = i;
                    break;
                }
            }
            return result;
        }

        // Token: 0x060005C2 RID: 1474 RVA: 0x00024398 File Offset: 0x00022598
        public ROIPrimitiveDefinition(int id, string name, string translation, Type typeOfData, Type typeOfControl)
        {
            this.id = id;
            this.Name = name;
            this.translation = translation;
            this.typeOfData = typeOfData;
            this.typeOfControl = typeOfControl;
        }

        // Token: 0x040002F6 RID: 758
        static List<ROIPrimitiveDefinition> definitions;

        // Token: 0x040002F7 RID: 759
        static Dictionary<string, ROIPrimitiveDefinition> definitionsMap;

        // Token: 0x040002F8 RID: 760
        int id;

        // Token: 0x040002F9 RID: 761
        string name;

        // Token: 0x040002FA RID: 762
        string translation;

        // Token: 0x040002FB RID: 763
        Type typeOfControl;

        // Token: 0x040002FC RID: 764
        Type typeOfData;
    }
}
