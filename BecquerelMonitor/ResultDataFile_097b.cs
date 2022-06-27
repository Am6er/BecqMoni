using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
	// Token: 0x02000141 RID: 321
	[XmlRoot("ResultDataFile")]
	public class ResultDataFile_097b
	{
		// Token: 0x17000450 RID: 1104
		// (get) Token: 0x06001038 RID: 4152 RVA: 0x00058AD0 File Offset: 0x00056CD0
		// (set) Token: 0x06001039 RID: 4153 RVA: 0x00058AD8 File Offset: 0x00056CD8
		[XmlArrayItem("ResultData")]
		public List<ResultData_097b> ResultDataList
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

		// Token: 0x04000969 RID: 2409
		List<ResultData_097b> resultDataList = new List<ResultData_097b>();
	}
}
