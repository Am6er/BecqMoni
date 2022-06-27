using System;

namespace MaNet
{
	// Token: 0x020001E7 RID: 487
	static class Maths
	{
		// Token: 0x060016D9 RID: 5849 RVA: 0x00074A88 File Offset: 0x00072C88
		public static double Hypot(double a, double b)
		{
			double num;
			if (Math.Abs(a) > Math.Abs(b))
			{
				num = b / a;
				num = Math.Abs(a) * Math.Sqrt(1.0 + num * num);
			}
			else if (b != 0.0)
			{
				num = a / b;
				num = Math.Abs(b) * Math.Sqrt(1.0 + num * num);
			}
			else
			{
				num = 0.0;
			}
			return num;
		}
	}
}
