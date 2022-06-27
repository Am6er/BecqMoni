using System;
using System.Runtime.InteropServices;
using CoreAudioApi.Interfaces;

namespace CoreAudioApi
{
	// Token: 0x020001C3 RID: 451
	public class PropertyStore
	{
		// Token: 0x17000698 RID: 1688
		// (get) Token: 0x060015F2 RID: 5618 RVA: 0x0006D45C File Offset: 0x0006B65C
		public int Count
		{
			get
			{
				int result;
				Marshal.ThrowExceptionForHR(this._Store.GetCount(out result));
				return result;
			}
		}

		// Token: 0x17000699 RID: 1689
		public PropertyStoreProperty this[int index]
		{
			get
			{
				PropertyKey key = this.Get(index);
				PropVariant value;
				Marshal.ThrowExceptionForHR(this._Store.GetValue(ref key, out value));
				return new PropertyStoreProperty(key, value);
			}
		}

		// Token: 0x060015F4 RID: 5620 RVA: 0x0006D4B4 File Offset: 0x0006B6B4
		public bool Contains(Guid guid)
		{
			for (int i = 0; i < this.Count; i++)
			{
				if (this.Get(i).fmtid == guid)
				{
					return true;
				}
			}
			return false;
		}

		// Token: 0x1700069A RID: 1690
		public PropertyStoreProperty this[Guid guid]
		{
			get
			{
				for (int i = 0; i < this.Count; i++)
				{
					PropertyKey key = this.Get(i);
					if (key.fmtid == guid)
					{
						PropVariant value;
						Marshal.ThrowExceptionForHR(this._Store.GetValue(ref key, out value));
						return new PropertyStoreProperty(key, value);
					}
				}
				return null;
			}
		}

		// Token: 0x060015F6 RID: 5622 RVA: 0x0006D554 File Offset: 0x0006B754
		public PropertyKey Get(int index)
		{
			PropertyKey result;
			Marshal.ThrowExceptionForHR(this._Store.GetAt(index, out result));
			return result;
		}

		// Token: 0x060015F7 RID: 5623 RVA: 0x0006D57C File Offset: 0x0006B77C
		public PropVariant GetValue(int index)
		{
			PropertyKey propertyKey = this.Get(index);
			PropVariant result;
			Marshal.ThrowExceptionForHR(this._Store.GetValue(ref propertyKey, out result));
			return result;
		}

		// Token: 0x060015F8 RID: 5624 RVA: 0x0006D5AC File Offset: 0x0006B7AC
		internal PropertyStore(IPropertyStore store)
		{
			this._Store = store;
		}

		// Token: 0x04000CA0 RID: 3232
		IPropertyStore _Store;
	}
}
