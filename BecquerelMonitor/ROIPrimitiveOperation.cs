using BecquerelMonitor.Properties;
using System.Collections.Generic;
using System.Drawing;

namespace BecquerelMonitor
{
    // Token: 0x0200011A RID: 282
    public class ROIPrimitiveOperation
    {
        // Token: 0x170003CE RID: 974
        // (get) Token: 0x06000EF5 RID: 3829 RVA: 0x000568BC File Offset: 0x00054ABC
        // (set) Token: 0x06000EF6 RID: 3830 RVA: 0x000568C4 File Offset: 0x00054AC4
        public static List<ROIPrimitiveOperation> Operations
        {
            get
            {
                return ROIPrimitiveOperation.operations;
            }
            set
            {
                ROIPrimitiveOperation.operations = value;
            }
        }

        // Token: 0x170003CF RID: 975
        // (get) Token: 0x06000EF7 RID: 3831 RVA: 0x000568CC File Offset: 0x00054ACC
        // (set) Token: 0x06000EF8 RID: 3832 RVA: 0x000568D4 File Offset: 0x00054AD4
        public static Dictionary<string, ROIPrimitiveOperation> OperationsMap
        {
            get
            {
                return ROIPrimitiveOperation.operationsMap;
            }
            set
            {
                ROIPrimitiveOperation.operationsMap = value;
            }
        }

        // Token: 0x170003D0 RID: 976
        // (get) Token: 0x06000EF9 RID: 3833 RVA: 0x000568DC File Offset: 0x00054ADC
        // (set) Token: 0x06000EFA RID: 3834 RVA: 0x000568E4 File Offset: 0x00054AE4
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

        // Token: 0x170003D1 RID: 977
        // (get) Token: 0x06000EFB RID: 3835 RVA: 0x000568F0 File Offset: 0x00054AF0
        // (set) Token: 0x06000EFC RID: 3836 RVA: 0x000568F8 File Offset: 0x00054AF8
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

        // Token: 0x170003D2 RID: 978
        // (get) Token: 0x06000EFD RID: 3837 RVA: 0x00056904 File Offset: 0x00054B04
        // (set) Token: 0x06000EFE RID: 3838 RVA: 0x0005690C File Offset: 0x00054B0C
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

        // Token: 0x170003D3 RID: 979
        // (get) Token: 0x06000EFF RID: 3839 RVA: 0x00056918 File Offset: 0x00054B18
        // (set) Token: 0x06000F00 RID: 3840 RVA: 0x00056920 File Offset: 0x00054B20
        public Bitmap Bitmap
        {
            get
            {
                return this.bitmap;
            }
            set
            {
                this.bitmap = value;
            }
        }

        // Token: 0x06000F01 RID: 3841 RVA: 0x0005692C File Offset: 0x00054B2C
        public static void InitializeROIPrimitiveOperations()
        {
            Resources.Plus.MakeTransparent();
            Resources.Minus.MakeTransparent();
            Bitmap bitmap = new Bitmap(Resources.Plus);
            bitmap.MakeTransparent();
            Bitmap bitmap2 = new Bitmap(Resources.Minus);
            bitmap2.MakeTransparent();
            ROIPrimitiveOperation.operations = new List<ROIPrimitiveOperation>();
            ROIPrimitiveOperation.operationsMap = new Dictionary<string, ROIPrimitiveOperation>();
            ROIPrimitiveOperation roiprimitiveOperation = new ROIPrimitiveOperation(0, "Addition", Resources.ROIPrimitiveOperationTypeAddition, bitmap);
            ROIPrimitiveOperation.operations.Add(roiprimitiveOperation);
            ROIPrimitiveOperation.operationsMap.Add(roiprimitiveOperation.Name, roiprimitiveOperation);
            ROIPrimitiveOperation roiprimitiveOperation2 = new ROIPrimitiveOperation(1, "Subtraction", Resources.ROIPrimitiveOperationTypeSubtraction, bitmap2);
            ROIPrimitiveOperation.operations.Add(roiprimitiveOperation2);
            ROIPrimitiveOperation.operationsMap.Add(roiprimitiveOperation2.Name, roiprimitiveOperation2);
        }

        // Token: 0x06000F02 RID: 3842 RVA: 0x000569E4 File Offset: 0x00054BE4
        public static int GetOperationIndex(string name)
        {
            int result = -1;
            for (int i = 0; i < ROIPrimitiveOperation.operations.Count; i++)
            {
                if (ROIPrimitiveOperation.operations[i].Name == name)
                {
                    result = i;
                    break;
                }
            }
            return result;
        }

        // Token: 0x06000F03 RID: 3843 RVA: 0x00056A34 File Offset: 0x00054C34
        public ROIPrimitiveOperation(int id, string name, string translation, Bitmap bitmap)
        {
            this.id = id;
            this.name = name;
            this.translation = translation;
            this.bitmap = bitmap;
        }

        // Token: 0x0400089F RID: 2207
        static List<ROIPrimitiveOperation> operations;

        // Token: 0x040008A0 RID: 2208
        static Dictionary<string, ROIPrimitiveOperation> operationsMap;

        // Token: 0x040008A1 RID: 2209
        int id;

        // Token: 0x040008A2 RID: 2210
        string name;

        // Token: 0x040008A3 RID: 2211
        string translation;

        // Token: 0x040008A4 RID: 2212
        Bitmap bitmap;
    }
}
