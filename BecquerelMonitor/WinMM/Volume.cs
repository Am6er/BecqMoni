using System;

namespace WinMM
{
	// Token: 0x020001A6 RID: 422
	public struct Volume
	{
		// Token: 0x0600152A RID: 5418 RVA: 0x0006B8A8 File Offset: 0x00069AA8
		public Volume(float leftVolume, float rightVolume)
		{
			this.left = leftVolume;
			this.right = rightVolume;
		}

		// Token: 0x0600152B RID: 5419 RVA: 0x0006B8B8 File Offset: 0x00069AB8
		public Volume(float volume)
		{
			this.left = volume;
			this.right = volume;
		}

		// Token: 0x17000643 RID: 1603
		// (get) Token: 0x0600152C RID: 5420 RVA: 0x0006B8C8 File Offset: 0x00069AC8
		// (set) Token: 0x0600152D RID: 5421 RVA: 0x0006B8D0 File Offset: 0x00069AD0
		public float Left
		{
			get
			{
				return this.left;
			}
			set
			{
				this.left = value;
			}
		}

		// Token: 0x17000644 RID: 1604
		// (get) Token: 0x0600152E RID: 5422 RVA: 0x0006B8DC File Offset: 0x00069ADC
		// (set) Token: 0x0600152F RID: 5423 RVA: 0x0006B8E4 File Offset: 0x00069AE4
		public float Right
		{
			get
			{
				return this.right;
			}
			set
			{
				this.right = value;
			}
		}

		// Token: 0x06001530 RID: 5424 RVA: 0x0006B8F0 File Offset: 0x00069AF0
		public static bool operator ==(Volume volume1, Volume volume2)
		{
			return volume1.Equals(volume2);
		}

		// Token: 0x06001531 RID: 5425 RVA: 0x0006B908 File Offset: 0x00069B08
		public static bool operator !=(Volume volume1, Volume volume2)
		{
			return !volume1.Equals(volume2);
		}

		// Token: 0x06001532 RID: 5426 RVA: 0x0006B920 File Offset: 0x00069B20
		public override bool Equals(object obj)
		{
			if (obj == null || base.GetType() != obj.GetType())
			{
				return false;
			}
			Volume volume = (Volume)obj;
			return this.Left == volume.Left && this.Right == volume.Right;
		}

		// Token: 0x06001533 RID: 5427 RVA: 0x0006B980 File Offset: 0x00069B80
		public override int GetHashCode()
		{
			return 0;
		}

		// Token: 0x04000C39 RID: 3129
		float left;

		// Token: 0x04000C3A RID: 3130
		float right;
	}
}
