using System;
using System.Collections.Generic;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
	// Token: 0x020000AA RID: 170
	public class ThermometerType
	{
		// Token: 0x1700025B RID: 603
		// (get) Token: 0x06000861 RID: 2145 RVA: 0x00030444 File Offset: 0x0002E644
		public static List<ThermometerType> ThermometerTypeList
		{
			get
			{
				return ThermometerType.thermometerTypeList;
			}
		}

		// Token: 0x1700025C RID: 604
		// (get) Token: 0x06000862 RID: 2146 RVA: 0x0003044C File Offset: 0x0002E64C
		public static Dictionary<string, ThermometerType> ThermometerTypeMap
		{
			get
			{
				return ThermometerType.thermometerTypeMap;
			}
		}

		// Token: 0x06000863 RID: 2147 RVA: 0x00030454 File Offset: 0x0002E654
		public static void InitializeThermometerTypes()
		{
			ThermometerType.thermometerTypeList = new List<ThermometerType>();
			ThermometerType.thermometerTypeMap = new Dictionary<string, ThermometerType>();
			ThermometerType thermometerType = new ThermometerType();
			thermometerType.Id = "None";
			thermometerType.Name = Resources.ThermometerTypeNone;
			thermometerType.ThermometerFormType = typeof(ThermometerForm);
			thermometerType.ThermometerControllerType = typeof(ThermometerController);
			thermometerType.ThermometerConfigType = typeof(ThermometerConfig);
			ThermometerType.thermometerTypeList.Add(thermometerType);
			ThermometerType.thermometerTypeMap.Add(thermometerType.Id, thermometerType);
		}

		// Token: 0x1700025D RID: 605
		// (get) Token: 0x06000864 RID: 2148 RVA: 0x000304E4 File Offset: 0x0002E6E4
		// (set) Token: 0x06000865 RID: 2149 RVA: 0x000304EC File Offset: 0x0002E6EC
		public string Id
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

		// Token: 0x1700025E RID: 606
		// (get) Token: 0x06000866 RID: 2150 RVA: 0x000304F8 File Offset: 0x0002E6F8
		// (set) Token: 0x06000867 RID: 2151 RVA: 0x00030500 File Offset: 0x0002E700
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

		// Token: 0x1700025F RID: 607
		// (get) Token: 0x06000868 RID: 2152 RVA: 0x0003050C File Offset: 0x0002E70C
		// (set) Token: 0x06000869 RID: 2153 RVA: 0x00030514 File Offset: 0x0002E714
		public Type ThermometerFormType
		{
			get
			{
				return this.thermometerFormType;
			}
			set
			{
				this.thermometerFormType = value;
			}
		}

		// Token: 0x17000260 RID: 608
		// (get) Token: 0x0600086A RID: 2154 RVA: 0x00030520 File Offset: 0x0002E720
		// (set) Token: 0x0600086B RID: 2155 RVA: 0x00030528 File Offset: 0x0002E728
		public Type ThermometerControllerType
		{
			get
			{
				return this.thermometerContollerType;
			}
			set
			{
				this.thermometerContollerType = value;
			}
		}

		// Token: 0x17000261 RID: 609
		// (get) Token: 0x0600086C RID: 2156 RVA: 0x00030534 File Offset: 0x0002E734
		// (set) Token: 0x0600086D RID: 2157 RVA: 0x0003053C File Offset: 0x0002E73C
		public Type ThermometerConfigType
		{
			get
			{
				return this.thermometerConfigType;
			}
			set
			{
				this.thermometerConfigType = value;
			}
		}

		// Token: 0x0600086F RID: 2159 RVA: 0x00030550 File Offset: 0x0002E750
		public override string ToString()
		{
			return this.name;
		}

		// Token: 0x04000457 RID: 1111
		static List<ThermometerType> thermometerTypeList;

		// Token: 0x04000458 RID: 1112
		static Dictionary<string, ThermometerType> thermometerTypeMap;

		// Token: 0x04000459 RID: 1113
		string id;

		// Token: 0x0400045A RID: 1114
		string name;

		// Token: 0x0400045B RID: 1115
		Type thermometerFormType;

		// Token: 0x0400045C RID: 1116
		Type thermometerContollerType;

		// Token: 0x0400045D RID: 1117
		Type thermometerConfigType;
	}
}
