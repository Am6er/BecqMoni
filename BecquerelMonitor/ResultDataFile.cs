using System.Collections.Generic;

namespace BecquerelMonitor
{
    // Token: 0x020000BF RID: 191
    public class ResultDataFile
    {
        // Token: 0x1700028E RID: 654
        // (get) Token: 0x06000938 RID: 2360 RVA: 0x00035A24 File Offset: 0x00033C24
        // (set) Token: 0x06000939 RID: 2361 RVA: 0x00035A2C File Offset: 0x00033C2C
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

        // Token: 0x1700028F RID: 655
        // (get) Token: 0x0600093A RID: 2362 RVA: 0x00035A38 File Offset: 0x00033C38
        // (set) Token: 0x0600093B RID: 2363 RVA: 0x00035A40 File Offset: 0x00033C40
        public List<ResultData> ResultDataList
        {
            get
            {
                return this.resultDataList;
            }
            set
            {
                this.resultDataList = value;
            }
        }

        // Token: 0x0600093C RID: 2364 RVA: 0x00035A4C File Offset: 0x00033C4C
        public ResultDataFile()
        {
        }

        // Token: 0x0600093D RID: 2365 RVA: 0x00035A6C File Offset: 0x00033C6C
        public void InitFormatVersion()
        {
            this.formatVersion = "120920";
        }

        // Token: 0x0600093E RID: 2366 RVA: 0x00035A7C File Offset: 0x00033C7C
        public ResultDataFile(ResultDataFile_097b oldResultDataFile)
        {
            foreach (ResultData_097b old in oldResultDataFile.ResultDataList)
            {
                this.resultDataList.Add(new ResultData(old));
            }
        }

        // Token: 0x04000516 RID: 1302
        const string formatVersionString = "120920";

        // Token: 0x04000517 RID: 1303
        string formatVersion = "";

        // Token: 0x04000518 RID: 1304
        List<ResultData> resultDataList = new List<ResultData>();
    }
}
