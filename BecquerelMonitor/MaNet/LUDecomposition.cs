using System;

namespace MaNet
{
	// Token: 0x020001E4 RID: 484
	[Serializable]
	public class LUDecomposition
	{
		// Token: 0x06001677 RID: 5751 RVA: 0x00070020 File Offset: 0x0006E220
		public LUDecomposition(Matrix A)
		{
			this.LU = A.ArrayCopy();
			this.m = A.RowDimension;
			this.n = A.ColumnDimension;
			this.piv = new int[this.m];
			for (int i = 0; i < this.m; i++)
			{
				this.piv[i] = i;
			}
			this.pivsign = 1;
			double[] array = new double[this.m];
			for (int j = 0; j < this.n; j++)
			{
				for (int k = 0; k < this.m; k++)
				{
					array[k] = this.LU[k][j];
				}
				for (int l = 0; l < this.m; l++)
				{
					double[] array2 = this.LU[l];
					int num = Math.Min(l, j);
					double num2 = 0.0;
					for (int m = 0; m < num; m++)
					{
						num2 += array2[m] * array[m];
					}
					array2[j] = (array[l] -= num2);
				}
				int num3 = j;
				for (int n = j + 1; n < this.m; n++)
				{
					if (Math.Abs(array[n]) > Math.Abs(array[num3]))
					{
						num3 = n;
					}
				}
				if (num3 != j)
				{
					for (int num4 = 0; num4 < this.n; num4++)
					{
						double num5 = this.LU[num3][num4];
						this.LU[num3][num4] = this.LU[j][num4];
						this.LU[j][num4] = num5;
					}
					int num6 = this.piv[num3];
					this.piv[num3] = this.piv[j];
					this.piv[j] = num6;
					this.pivsign = -this.pivsign;
				}
				if (j < this.m && this.LU[j][j] != 0.0)
				{
					for (int num7 = j + 1; num7 < this.m; num7++)
					{
						this.LU[num7][j] /= this.LU[j][j];
					}
				}
			}
		}

		// Token: 0x06001678 RID: 5752 RVA: 0x00070284 File Offset: 0x0006E484
		public bool IsNonsingular()
		{
			if (this.m != this.n)
			{
				return false;
			}
			for (int i = 0; i < this.n; i++)
			{
				if (this.LU[i][i] == 0.0)
				{
					return false;
				}
			}
			return true;
		}

		// Token: 0x06001679 RID: 5753 RVA: 0x000702DC File Offset: 0x0006E4DC
		public Matrix GetL()
		{
			if (this.m >= this.n)
			{
				Matrix matrix = new Matrix(this.m, this.n);
				double[][] array = matrix.Array;
				for (int i = 0; i < this.m; i++)
				{
					for (int j = 0; j < this.n; j++)
					{
						if (i > j)
						{
							array[i][j] = this.LU[i][j];
						}
						else if (i == j)
						{
							array[i][j] = 1.0;
						}
						else
						{
							array[i][j] = 0.0;
						}
					}
				}
				return matrix;
			}
			Matrix matrix2 = new Matrix(this.m, this.m);
			double[][] array2 = matrix2.Array;
			for (int k = 0; k < this.m; k++)
			{
				for (int l = 0; l < this.m; l++)
				{
					if (k > l)
					{
						array2[k][l] = this.LU[k][l];
					}
					else if (k == l)
					{
						array2[k][l] = 1.0;
					}
					else
					{
						array2[k][l] = 0.0;
					}
				}
			}
			return matrix2;
		}

		// Token: 0x0600167A RID: 5754 RVA: 0x00070448 File Offset: 0x0006E648
		public Matrix GetU()
		{
			if (this.m >= this.n)
			{
				Matrix matrix = new Matrix(this.n, this.n);
				double[][] array = matrix.Array;
				for (int i = 0; i < this.n; i++)
				{
					for (int j = 0; j < this.n; j++)
					{
						if (i <= j)
						{
							array[i][j] = this.LU[i][j];
						}
						else
						{
							array[i][j] = 0.0;
						}
					}
				}
				return matrix;
			}
			Matrix matrix2 = new Matrix(this.m, this.n);
			double[][] array2 = matrix2.Array;
			for (int k = 0; k < this.m; k++)
			{
				for (int l = 0; l < this.n; l++)
				{
					if (k <= l)
					{
						array2[k][l] = this.LU[k][l];
					}
					else
					{
						array2[k][l] = 0.0;
					}
				}
			}
			return matrix2;
		}

		// Token: 0x0600167B RID: 5755 RVA: 0x00070570 File Offset: 0x0006E770
		public int[] getPivot()
		{
			int[] array = new int[this.m];
			for (int i = 0; i < this.m; i++)
			{
				array[i] = this.piv[i];
			}
			return array;
		}

		// Token: 0x0600167C RID: 5756 RVA: 0x000705B0 File Offset: 0x0006E7B0
		public Matrix GetP()
		{
			int[] pivot = this.getPivot();
			Matrix matrix = new Matrix(pivot.Length);
			for (int i = 0; i < pivot.Length; i++)
			{
				matrix.Array[i][pivot[i]] = 1.0;
			}
			return matrix;
		}

		// Token: 0x0600167D RID: 5757 RVA: 0x000705FC File Offset: 0x0006E7FC
		public double[] getDoublePivot()
		{
			double[] array = new double[this.m];
			for (int i = 0; i < this.m; i++)
			{
				array[i] = (double)this.piv[i];
			}
			return array;
		}

		// Token: 0x0600167E RID: 5758 RVA: 0x0007063C File Offset: 0x0006E83C
		public double det()
		{
			if (this.m != this.n)
			{
				throw new ArgumentException("Matrix must be square.");
			}
			double num = (double)this.pivsign;
			for (int i = 0; i < this.n; i++)
			{
				num *= this.LU[i][i];
			}
			return num;
		}

		// Token: 0x0600167F RID: 5759 RVA: 0x00070698 File Offset: 0x0006E898
		public Matrix solve(Matrix B)
		{
			if (B.RowDimension != this.m)
			{
				throw new ArgumentException("Matrix row dimensions must agree.");
			}
			if (!this.IsNonsingular())
			{
				throw new Exception("Matrix is singular.");
			}
			int columnDimension = B.ColumnDimension;
			Matrix matrix = B.GetMatrix(this.piv, 0, columnDimension - 1);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.n; i++)
			{
				for (int j = i + 1; j < this.n; j++)
				{
					for (int k = 0; k < columnDimension; k++)
					{
						array[j][k] -= array[i][k] * this.LU[j][i];
					}
				}
			}
			for (int l = this.n - 1; l >= 0; l--)
			{
				for (int m = 0; m < columnDimension; m++)
				{
					array[l][m] /= this.LU[l][l];
				}
				for (int n = 0; n < l; n++)
				{
					for (int num = 0; num < columnDimension; num++)
					{
						array[n][num] -= array[l][num] * this.LU[n][l];
					}
				}
			}
			return matrix;
		}

		// Token: 0x04000D1E RID: 3358
		double[][] LU;

		// Token: 0x04000D1F RID: 3359
		int m;

		// Token: 0x04000D20 RID: 3360
		int n;

		// Token: 0x04000D21 RID: 3361
		int pivsign;

		// Token: 0x04000D22 RID: 3362
		int[] piv;
	}
}
