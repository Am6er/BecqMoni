using System;

namespace CoreAudioApi
{
	// Token: 0x020001CE RID: 462
	public class PropertyStoreProperty
	{
		// Token: 0x06001638 RID: 5688 RVA: 0x0006DBD0 File Offset: 0x0006BDD0
		internal PropertyStoreProperty(PropertyKey key, PropVariant value)
		{
			this._PropertyKey = key;
			this._PropValue = value;
		}

		// Token: 0x170006B4 RID: 1716
		// (get) Token: 0x06001639 RID: 5689 RVA: 0x0006DBE8 File Offset: 0x0006BDE8
		public PropertyKey Key
		{
			get
			{
				return this._PropertyKey;
			}
		}

		// Token: 0x170006B5 RID: 1717
		// (get) Token: 0x0600163A RID: 5690 RVA: 0x0006DBF0 File Offset: 0x0006BDF0
		public object Value
		{
			get
			{
				return this._PropValue.Value;
			}
		}

		// Token: 0x04000CBC RID: 3260
		PropertyKey _PropertyKey;

		// Token: 0x04000CBD RID: 3261
		PropVariant _PropValue;
	}
}
