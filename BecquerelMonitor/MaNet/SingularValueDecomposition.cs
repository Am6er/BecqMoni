using System;

namespace MaNet
{
	// Token: 0x020001E2 RID: 482
	[Serializable]
	public class SingularValueDecomposition
	{
		// Token: 0x06001667 RID: 5735 RVA: 0x0006E54C File Offset: 0x0006C74C
		public SingularValueDecomposition(Matrix Arg)
		{
			double[][] array = Arg.ArrayCopy();
			this.m = Arg.RowDimension;
			this.n = Arg.ColumnDimension;
			int num = Math.Min(this.m, this.n);
			this.s = new double[Math.Min(this.m + 1, this.n)];
			this.U = new double[this.m][];
			for (int i = 0; i < this.m; i++)
			{
				this.U[i] = new double[this.m];
			}
			this.V = new double[this.n][];
			for (int j = 0; j < this.n; j++)
			{
				this.V[j] = new double[this.n];
			}
			double[] array2 = new double[this.n];
			double[] array3 = new double[this.m];
			bool flag = true;
			bool flag2 = true;
			int num2 = Math.Min(this.m - 1, this.n);
			int num3 = Math.Max(0, Math.Min(this.n - 2, this.m));
			for (int k = 0; k < Math.Max(num2, num3); k++)
			{
				if (k < num2)
				{
					this.s[k] = 0.0;
					for (int l = k; l < this.m; l++)
					{
						this.s[k] = Maths.Hypot(this.s[k], array[l][k]);
					}
					if (this.s[k] != 0.0)
					{
						if (array[k][k] < 0.0)
						{
							this.s[k] = -this.s[k];
						}
						for (int m = k; m < this.m; m++)
						{
							array[m][k] /= this.s[k];
						}
						array[k][k] += 1.0;
					}
					this.s[k] = -this.s[k];
				}
				for (int n = k + 1; n < this.n; n++)
				{
					if (k < num2 & this.s[k] != 0.0)
					{
						double num4 = 0.0;
						for (int num5 = k; num5 < this.m; num5++)
						{
							num4 += array[num5][k] * array[num5][n];
						}
						num4 = -num4 / array[k][k];
						for (int num6 = k; num6 < this.m; num6++)
						{
							array[num6][n] += num4 * array[num6][k];
						}
					}
					array2[n] = array[k][n];
				}
				if (flag & k < num2)
				{
					for (int num7 = k; num7 < this.m; num7++)
					{
						this.U[num7][k] = array[num7][k];
					}
				}
				if (k < num3)
				{
					array2[k] = 0.0;
					for (int num8 = k + 1; num8 < this.n; num8++)
					{
						array2[k] = Maths.Hypot(array2[k], array2[num8]);
					}
					if (array2[k] != 0.0)
					{
						if (array2[k + 1] < 0.0)
						{
							array2[k] = -array2[k];
						}
						for (int num9 = k + 1; num9 < this.n; num9++)
						{
							array2[num9] /= array2[k];
						}
						array2[k + 1] += 1.0;
					}
					array2[k] = -array2[k];
					if (k + 1 < this.m & array2[k] != 0.0)
					{
						for (int num10 = k + 1; num10 < this.m; num10++)
						{
							array3[num10] = 0.0;
						}
						for (int num11 = k + 1; num11 < this.n; num11++)
						{
							for (int num12 = k + 1; num12 < this.m; num12++)
							{
								array3[num12] += array2[num11] * array[num12][num11];
							}
						}
						for (int num13 = k + 1; num13 < this.n; num13++)
						{
							double num14 = -array2[num13] / array2[k + 1];
							for (int num15 = k + 1; num15 < this.m; num15++)
							{
								array[num15][num13] += num14 * array3[num15];
							}
						}
					}
					if (flag2)
					{
						for (int num16 = k + 1; num16 < this.n; num16++)
						{
							this.V[num16][k] = array2[num16];
						}
					}
				}
			}
			int num17 = Math.Min(this.n, this.m + 1);
			if (num2 < this.n)
			{
				this.s[num2] = array[num2][num2];
			}
			if (this.m < num17)
			{
				this.s[num17 - 1] = 0.0;
			}
			if (num3 + 1 < num17)
			{
				array2[num3] = array[num3][num17 - 1];
			}
			array2[num17 - 1] = 0.0;
			if (flag)
			{
				for (int num18 = num2; num18 < num; num18++)
				{
					for (int num19 = 0; num19 < this.m; num19++)
					{
						this.U[num19][num18] = 0.0;
					}
					this.U[num18][num18] = 1.0;
				}
				for (int num20 = num2 - 1; num20 >= 0; num20--)
				{
					if (this.s[num20] != 0.0)
					{
						for (int num21 = num20 + 1; num21 < num; num21++)
						{
							double num22 = 0.0;
							for (int num23 = num20; num23 < this.m; num23++)
							{
								num22 += this.U[num23][num20] * this.U[num23][num21];
							}
							num22 = -num22 / this.U[num20][num20];
							for (int num24 = num20; num24 < this.m; num24++)
							{
								this.U[num24][num21] += num22 * this.U[num24][num20];
							}
						}
						for (int num25 = num20; num25 < this.m; num25++)
						{
							this.U[num25][num20] = -this.U[num25][num20];
						}
						this.U[num20][num20] = 1.0 + this.U[num20][num20];
						for (int num26 = 0; num26 < num20 - 1; num26++)
						{
							this.U[num26][num20] = 0.0;
						}
					}
					else
					{
						for (int num27 = 0; num27 < this.m; num27++)
						{
							this.U[num27][num20] = 0.0;
						}
						this.U[num20][num20] = 1.0;
					}
				}
			}
			if (flag2)
			{
				for (int num28 = this.n - 1; num28 >= 0; num28--)
				{
					if (num28 < num3 & array2[num28] != 0.0)
					{
						for (int num29 = num28 + 1; num29 < num; num29++)
						{
							double num30 = 0.0;
							for (int num31 = num28 + 1; num31 < this.n; num31++)
							{
								num30 += this.V[num31][num28] * this.V[num31][num29];
							}
							num30 = -num30 / this.V[num28 + 1][num28];
							for (int num32 = num28 + 1; num32 < this.n; num32++)
							{
								this.V[num32][num29] += num30 * this.V[num32][num28];
							}
						}
					}
					for (int num33 = 0; num33 < this.n; num33++)
					{
						this.V[num33][num28] = 0.0;
					}
					this.V[num28][num28] = 1.0;
				}
			}
			int num34 = num17 - 1;
			int num35 = 0;
			double num36 = Math.Pow(2.0, -52.0);
			double num37 = Math.Pow(2.0, -966.0);
			while (num17 > 0)
			{
				int num38 = num17 - 2;
				while (num38 >= -1 && num38 != -1)
				{
					if (Math.Abs(array2[num38]) <= num37 + num36 * (Math.Abs(this.s[num38]) + Math.Abs(this.s[num38 + 1])))
					{
						array2[num38] = 0.0;
						break;
					}
					num38--;
				}
				int num39;
				if (num38 == num17 - 2)
				{
					num39 = 4;
				}
				else
				{
					int num40 = num17 - 1;
					while (num40 >= num38 && num40 != num38)
					{
						double num41 = ((num40 != num17) ? Math.Abs(array2[num40]) : 0.0) + ((num40 != num38 + 1) ? Math.Abs(array2[num40 - 1]) : 0.0);
						if (Math.Abs(this.s[num40]) <= num37 + num36 * num41)
						{
							this.s[num40] = 0.0;
							break;
						}
						num40--;
					}
					if (num40 == num38)
					{
						num39 = 3;
					}
					else if (num40 == num17 - 1)
					{
						num39 = 1;
					}
					else
					{
						num39 = 2;
						num38 = num40;
					}
				}
				num38++;
				switch (num39)
				{
				case 1:
				{
					double num42 = array2[num17 - 2];
					array2[num17 - 2] = 0.0;
					for (int num43 = num17 - 2; num43 >= num38; num43--)
					{
						double num44 = Maths.Hypot(this.s[num43], num42);
						double num45 = this.s[num43] / num44;
						double num46 = num42 / num44;
						this.s[num43] = num44;
						if (num43 != num38)
						{
							num42 = -num46 * array2[num43 - 1];
							array2[num43 - 1] = num45 * array2[num43 - 1];
						}
						if (flag2)
						{
							for (int num47 = 0; num47 < this.n; num47++)
							{
								num44 = num45 * this.V[num47][num43] + num46 * this.V[num47][num17 - 1];
								this.V[num47][num17 - 1] = -num46 * this.V[num47][num43] + num45 * this.V[num47][num17 - 1];
								this.V[num47][num43] = num44;
							}
						}
					}
					break;
				}
				case 2:
				{
					double num48 = array2[num38 - 1];
					array2[num38 - 1] = 0.0;
					for (int num49 = num38; num49 < num17; num49++)
					{
						double num50 = Maths.Hypot(this.s[num49], num48);
						double num51 = this.s[num49] / num50;
						double num52 = num48 / num50;
						this.s[num49] = num50;
						num48 = -num52 * array2[num49];
						array2[num49] = num51 * array2[num49];
						if (flag)
						{
							for (int num53 = 0; num53 < this.m; num53++)
							{
								num50 = num51 * this.U[num53][num49] + num52 * this.U[num53][num38 - 1];
								this.U[num53][num38 - 1] = -num52 * this.U[num53][num49] + num51 * this.U[num53][num38 - 1];
								this.U[num53][num49] = num50;
							}
						}
					}
					break;
				}
				case 3:
				{
					double num54 = Math.Max(Math.Max(Math.Max(Math.Max(Math.Abs(this.s[num17 - 1]), Math.Abs(this.s[num17 - 2])), Math.Abs(array2[num17 - 2])), Math.Abs(this.s[num38])), Math.Abs(array2[num38]));
					double num55 = this.s[num17 - 1] / num54;
					double num56 = this.s[num17 - 2] / num54;
					double num57 = array2[num17 - 2] / num54;
					double num58 = this.s[num38] / num54;
					double num59 = array2[num38] / num54;
					double num60 = ((num56 + num55) * (num56 - num55) + num57 * num57) / 2.0;
					double num61 = num55 * num57 * (num55 * num57);
					double num62 = 0.0;
					if (num60 != 0.0 | num61 != 0.0)
					{
						num62 = Math.Sqrt(num60 * num60 + num61);
						if (num60 < 0.0)
						{
							num62 = -num62;
						}
						num62 = num61 / (num60 + num62);
					}
					double num63 = (num58 + num55) * (num58 - num55) + num62;
					double num64 = num58 * num59;
					for (int num65 = num38; num65 < num17 - 1; num65++)
					{
						double num66 = Maths.Hypot(num63, num64);
						double num67 = num63 / num66;
						double num68 = num64 / num66;
						if (num65 != num38)
						{
							array2[num65 - 1] = num66;
						}
						num63 = num67 * this.s[num65] + num68 * array2[num65];
						array2[num65] = num67 * array2[num65] - num68 * this.s[num65];
						num64 = num68 * this.s[num65 + 1];
						this.s[num65 + 1] = num67 * this.s[num65 + 1];
						if (flag2)
						{
							for (int num69 = 0; num69 < this.n; num69++)
							{
								num66 = num67 * this.V[num69][num65] + num68 * this.V[num69][num65 + 1];
								this.V[num69][num65 + 1] = -num68 * this.V[num69][num65] + num67 * this.V[num69][num65 + 1];
								this.V[num69][num65] = num66;
							}
						}
						num66 = Maths.Hypot(num63, num64);
						num67 = num63 / num66;
						num68 = num64 / num66;
						this.s[num65] = num66;
						num63 = num67 * array2[num65] + num68 * this.s[num65 + 1];
						this.s[num65 + 1] = -num68 * array2[num65] + num67 * this.s[num65 + 1];
						num64 = num68 * array2[num65 + 1];
						array2[num65 + 1] = num67 * array2[num65 + 1];
						if (flag && num65 < this.m - 1)
						{
							for (int num70 = 0; num70 < this.m; num70++)
							{
								num66 = num67 * this.U[num70][num65] + num68 * this.U[num70][num65 + 1];
								this.U[num70][num65 + 1] = -num68 * this.U[num70][num65] + num67 * this.U[num70][num65 + 1];
								this.U[num70][num65] = num66;
							}
						}
					}
					array2[num17 - 2] = num63;
					num35++;
					break;
				}
				case 4:
					if (this.s[num38] <= 0.0)
					{
						this.s[num38] = ((this.s[num38] < 0.0) ? (-this.s[num38]) : 0.0);
						if (flag2)
						{
							for (int num71 = 0; num71 <= num34; num71++)
							{
								this.V[num71][num38] = -this.V[num71][num38];
							}
						}
					}
					while (num38 < num34 && this.s[num38] < this.s[num38 + 1])
					{
						double num72 = this.s[num38];
						this.s[num38] = this.s[num38 + 1];
						this.s[num38 + 1] = num72;
						if (flag2 && num38 < this.n - 1)
						{
							for (int num73 = 0; num73 < this.n; num73++)
							{
								num72 = this.V[num73][num38 + 1];
								this.V[num73][num38 + 1] = this.V[num73][num38];
								this.V[num73][num38] = num72;
							}
						}
						if (flag && num38 < this.m - 1)
						{
							for (int num74 = 0; num74 < this.m; num74++)
							{
								num72 = this.U[num74][num38 + 1];
								this.U[num74][num38 + 1] = this.U[num74][num38];
								this.U[num74][num38] = num72;
							}
						}
						num38++;
					}
					num35 = 0;
					num17--;
					break;
				}
			}
		}

		// Token: 0x06001668 RID: 5736 RVA: 0x0006F844 File Offset: 0x0006DA44
		public Matrix getU()
		{
			return new Matrix(this.U, this.m, Math.Min(this.m + 1, this.n));
		}

		// Token: 0x06001669 RID: 5737 RVA: 0x0006F86C File Offset: 0x0006DA6C
		public Matrix getV()
		{
			return new Matrix(this.V, this.n, this.n);
		}

		// Token: 0x0600166A RID: 5738 RVA: 0x0006F888 File Offset: 0x0006DA88
		public double[] getSingularValues()
		{
			return this.s;
		}

		// Token: 0x0600166B RID: 5739 RVA: 0x0006F890 File Offset: 0x0006DA90
		public Matrix getS()
		{
			Matrix matrix = new Matrix(this.n, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.n; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = 0.0;
				}
				array[i][i] = this.s[i];
			}
			return matrix;
		}

		// Token: 0x0600166C RID: 5740 RVA: 0x0006F908 File Offset: 0x0006DB08
		public double Norm2()
		{
			return this.s[0];
		}

		// Token: 0x0600166D RID: 5741 RVA: 0x0006F914 File Offset: 0x0006DB14
		public double Cond()
		{
			return this.s[0] / this.s[Math.Min(this.m, this.n) - 1];
		}

		// Token: 0x0600166E RID: 5742 RVA: 0x0006F93C File Offset: 0x0006DB3C
		public int Rank()
		{
			double num = Math.Pow(2.0, -52.0);
			double num2 = (double)Math.Max(this.m, this.n) * this.s[0] * num;
			int num3 = 0;
			for (int i = 0; i < this.s.Length; i++)
			{
				if (this.s[i] > num2)
				{
					num3++;
				}
			}
			return num3;
		}

		// Token: 0x04000D14 RID: 3348
		double[][] U;

		// Token: 0x04000D15 RID: 3349
		double[][] V;

		// Token: 0x04000D16 RID: 3350
		double[] s;

		// Token: 0x04000D17 RID: 3351
		int m;

		// Token: 0x04000D18 RID: 3352
		int n;
	}
}
