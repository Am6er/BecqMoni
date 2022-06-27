using System;

namespace MaNet
{
	// Token: 0x020001E3 RID: 483
	[Serializable]
	public class QRDecomposition
	{
		// Token: 0x170006BE RID: 1726
		// (get) Token: 0x0600166F RID: 5743 RVA: 0x0006F9B0 File Offset: 0x0006DBB0
		// (set) Token: 0x06001670 RID: 5744 RVA: 0x0006F9B8 File Offset: 0x0006DBB8
		public double RankTolerance
		{
			get
			{
				return this.mRankTolerance;
			}
			set
			{
				this.mRankTolerance = value;
			}
		}

		// Token: 0x06001671 RID: 5745 RVA: 0x0006F9C4 File Offset: 0x0006DBC4
		public QRDecomposition(Matrix A)
		{
			this.QR = A.ArrayCopy();
			this.m = A.RowDimension;
			this.n = A.ColumnDimension;
			this.Rdiag = new double[this.n];
			for (int i = 0; i < this.n; i++)
			{
				double num = 0.0;
				for (int j = i; j < this.m; j++)
				{
					num = Maths.Hypot(num, this.QR[j][i]);
				}
				if (num != 0.0)
				{
					if (this.QR[i][i] < 0.0)
					{
						num = -num;
					}
					for (int k = i; k < this.m; k++)
					{
						this.QR[k][i] /= num;
					}
					this.QR[i][i] += 1.0;
					for (int l = i + 1; l < this.n; l++)
					{
						double num2 = 0.0;
						for (int m = i; m < this.m; m++)
						{
							num2 += this.QR[m][i] * this.QR[m][l];
						}
						num2 = -num2 / this.QR[i][i];
						for (int n = i; n < this.m; n++)
						{
							this.QR[n][l] += num2 * this.QR[n][i];
						}
					}
				}
				this.Rdiag[i] = -num;
			}
		}

		// Token: 0x06001672 RID: 5746 RVA: 0x0006FB98 File Offset: 0x0006DD98
		public bool IsFullRank()
		{
			for (int i = 0; i < this.n; i++)
			{
				if (Math.Abs(this.Rdiag[i]) <= this.mRankTolerance)
				{
					return false;
				}
			}
			return true;
		}

		// Token: 0x06001673 RID: 5747 RVA: 0x0006FBD8 File Offset: 0x0006DDD8
		public Matrix GetH()
		{
			Matrix matrix = new Matrix(this.m, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					if (i >= j)
					{
						array[i][j] = this.QR[i][j];
					}
					else
					{
						array[i][j] = 0.0;
					}
				}
			}
			return matrix;
		}

		// Token: 0x06001674 RID: 5748 RVA: 0x0006FC60 File Offset: 0x0006DE60
		public Matrix GetR()
		{
			Matrix matrix = new Matrix(this.n, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.n; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					if (i < j)
					{
						array[i][j] = this.QR[i][j];
					}
					else if (i == j)
					{
						array[i][j] = this.Rdiag[i];
					}
					else
					{
						array[i][j] = 0.0;
					}
				}
			}
			return matrix;
		}

		// Token: 0x06001675 RID: 5749 RVA: 0x0006FD08 File Offset: 0x0006DF08
		public Matrix GetQ()
		{
			Matrix matrix = new Matrix(this.m, this.n);
			double[][] array = matrix.Array;
			for (int i = this.n - 1; i >= 0; i--)
			{
				for (int j = 0; j < this.m; j++)
				{
					array[j][i] = 0.0;
				}
				array[i][i] = 1.0;
				for (int k = i; k < this.n; k++)
				{
					if (this.QR[i][i] != 0.0)
					{
						double num = 0.0;
						for (int l = i; l < this.m; l++)
						{
							num += this.QR[l][i] * array[l][k];
						}
						num = -num / this.QR[i][i];
						for (int m = i; m < this.m; m++)
						{
							array[m][k] += num * this.QR[m][i];
						}
					}
				}
			}
			return matrix;
		}

		// Token: 0x06001676 RID: 5750 RVA: 0x0006FE50 File Offset: 0x0006E050
		public Matrix Solve(Matrix B)
		{
			if (B.RowDimension != this.m)
			{
				throw new ArgumentException("Matrix row dimensions must agree.");
			}
			if (!this.IsFullRank())
			{
				throw new Exception("Matrix is rank deficient.");
			}
			int columnDimension = B.ColumnDimension;
			double[][] array = B.ArrayCopy();
			for (int i = 0; i < this.n; i++)
			{
				for (int j = 0; j < columnDimension; j++)
				{
					double num = 0.0;
					for (int k = i; k < this.m; k++)
					{
						num += this.QR[k][i] * array[k][j];
					}
					num = -num / this.QR[i][i];
					for (int l = i; l < this.m; l++)
					{
						array[l][j] += num * this.QR[l][i];
					}
				}
			}
			for (int m = this.n - 1; m >= 0; m--)
			{
				for (int n = 0; n < columnDimension; n++)
				{
					array[m][n] /= this.Rdiag[m];
				}
				for (int num2 = 0; num2 < m; num2++)
				{
					for (int num3 = 0; num3 < columnDimension; num3++)
					{
						array[num2][num3] -= array[m][num3] * this.QR[num2][m];
					}
				}
			}
			return new Matrix(array, this.n, columnDimension).GetMatrix(0, this.n - 1, 0, columnDimension - 1);
		}

		// Token: 0x04000D19 RID: 3353
		double[][] QR;

		// Token: 0x04000D1A RID: 3354
		int m;

		// Token: 0x04000D1B RID: 3355
		int n;

		// Token: 0x04000D1C RID: 3356
		double[] Rdiag;

		// Token: 0x04000D1D RID: 3357
		double mRankTolerance;
	}
}
