using System;
using MaNet;

namespace BecquerelMonitor
{
	// Token: 0x0200003A RID: 58
	public class SavitzkyGolayMethod
	{
		// Token: 0x060002D2 RID: 722 RVA: 0x0000D608 File Offset: 0x0000B808
		public static double[] CalcSavitzkyGolayWeight(int k, int d, int m)
		{
			Matrix matrix = SavitzkyGolayMethod.SavitzkyGolayMatrix(d, m);
			double[] array = new double[2 * m + 1];
			for (int i = 0; i < 2 * m + 1; i++)
			{
				array[i] = matrix.Array[k][i];
			}
			return array;
		}

		// Token: 0x060002D3 RID: 723 RVA: 0x0000D654 File Offset: 0x0000B854
		public static Matrix SavitzkyGolayMatrix(int d, int m)
		{
			Matrix matrix = new Matrix(d + 1, 2 * m + 1);
			for (int i = 0; i < 2 * m + 1; i++)
			{
				matrix.Array[0][i] = 1.0;
				matrix.Array[1][i] = (double)(i - m);
			}
			for (int j = 2; j <= d; j++)
			{
				for (int k = 0; k < 2 * m + 1; k++)
				{
					matrix.Array[j][k] = matrix.Array[j - 1][k] * (double)(k - m);
				}
			}
			Matrix b = matrix.Transpose();
			return matrix.Times(b).Inverse().Times(matrix);
		}
	}
}
