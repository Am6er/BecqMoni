using System;

namespace MaNet
{
	// Token: 0x020001E1 RID: 481
	[Serializable]
	public class CholeskyDecomposition
	{
		// Token: 0x06001663 RID: 5731 RVA: 0x0006E1E0 File Offset: 0x0006C3E0
		public CholeskyDecomposition(Matrix Arg)
		{
			double[][] array = Arg.Array;
			this.n = Arg.RowDimension;
			this.L = new double[this.n][];
			for (int i = 0; i < this.n; i++)
			{
				this.L[i] = new double[Arg.ColumnDimension];
			}
			this.isspd = (Arg.ColumnDimension == this.n);
			for (int j = 0; j < this.n; j++)
			{
				double[] array2 = this.L[j];
				double num = 0.0;
				for (int k = 0; k < j; k++)
				{
					double[] array3 = this.L[k];
					double num2 = 0.0;
					for (int l = 0; l < k; l++)
					{
						num2 += array3[l] * array2[l];
					}
					num2 = (array2[k] = (array[j][k] - num2) / this.L[k][k]);
					num += num2 * num2;
					this.isspd &= (array[k][j] == array[j][k]);
				}
				num = array[j][j] - num;
				this.isspd &= (num > 0.0);
				this.L[j][j] = Math.Sqrt(Math.Max(num, 0.0));
				for (int m = j + 1; m < this.n; m++)
				{
					this.L[j][m] = 0.0;
				}
			}
		}

		// Token: 0x06001664 RID: 5732 RVA: 0x0006E3AC File Offset: 0x0006C5AC
		public bool IsSPD()
		{
			return this.isspd;
		}

		// Token: 0x06001665 RID: 5733 RVA: 0x0006E3B4 File Offset: 0x0006C5B4
		public Matrix getL()
		{
			return new Matrix(this.L, this.n, this.n);
		}

		// Token: 0x06001666 RID: 5734 RVA: 0x0006E3D0 File Offset: 0x0006C5D0
		public Matrix Solve(Matrix B)
		{
			if (B.RowDimension != this.n)
			{
				throw new ArgumentException("Matrix row dimensions must agree.");
			}
			if (!this.isspd)
			{
				throw new Exception("Matrix is not symmetric positive definite.");
			}
			double[][] array = B.ArrayCopy();
			int columnDimension = B.ColumnDimension;
			for (int i = 0; i < this.n; i++)
			{
				for (int j = 0; j < columnDimension; j++)
				{
					for (int k = 0; k < i; k++)
					{
						array[i][j] -= array[k][j] * this.L[i][k];
					}
					array[i][j] /= this.L[i][i];
				}
			}
			for (int l = this.n - 1; l >= 0; l--)
			{
				for (int m = 0; m < columnDimension; m++)
				{
					for (int n = l + 1; n < this.n; n++)
					{
						array[l][m] -= array[n][m] * this.L[n][l];
					}
					array[l][m] /= this.L[l][l];
				}
			}
			return new Matrix(array, this.n, columnDimension);
		}

		// Token: 0x04000D11 RID: 3345
		readonly double[][] L;

		// Token: 0x04000D12 RID: 3346
		readonly int n;

		// Token: 0x04000D13 RID: 3347
		readonly bool isspd;
	}
}
