using System;

namespace MaNet
{
	// Token: 0x020001E5 RID: 485
	[Serializable]
	public class EigenvalueDecomposition
	{
		// Token: 0x06001680 RID: 5760 RVA: 0x00070814 File Offset: 0x0006EA14
		void tred2()
		{
			for (int i = 0; i < this.n; i++)
			{
				this.d[i] = this.V[this.n - 1][i];
			}
			for (int j = this.n - 1; j > 0; j--)
			{
				double num = 0.0;
				double num2 = 0.0;
				for (int k = 0; k < j; k++)
				{
					num += Math.Abs(this.d[k]);
				}
				if (num == 0.0)
				{
					this.e[j] = this.d[j - 1];
					for (int l = 0; l < j; l++)
					{
						this.d[l] = this.V[j - 1][l];
						this.V[j][l] = 0.0;
						this.V[l][j] = 0.0;
					}
				}
				else
				{
					for (int m = 0; m < j; m++)
					{
						this.d[m] /= num;
						num2 += this.d[m] * this.d[m];
					}
					double num3 = this.d[j - 1];
					double num4 = Math.Sqrt(num2);
					if (num3 > 0.0)
					{
						num4 = -num4;
					}
					this.e[j] = num * num4;
					num2 -= num3 * num4;
					this.d[j - 1] = num3 - num4;
					for (int n = 0; n < j; n++)
					{
						this.e[n] = 0.0;
					}
					for (int num5 = 0; num5 < j; num5++)
					{
						num3 = this.d[num5];
						this.V[num5][j] = num3;
						num4 = this.e[num5] + this.V[num5][num5] * num3;
						for (int num6 = num5 + 1; num6 <= j - 1; num6++)
						{
							num4 += this.V[num6][num5] * this.d[num6];
							this.e[num6] += this.V[num6][num5] * num3;
						}
						this.e[num5] = num4;
					}
					num3 = 0.0;
					for (int num7 = 0; num7 < j; num7++)
					{
						this.e[num7] /= num2;
						num3 += this.e[num7] * this.d[num7];
					}
					double num8 = num3 / (num2 + num2);
					for (int num9 = 0; num9 < j; num9++)
					{
						this.e[num9] -= num8 * this.d[num9];
					}
					for (int num10 = 0; num10 < j; num10++)
					{
						num3 = this.d[num10];
						num4 = this.e[num10];
						for (int num11 = num10; num11 <= j - 1; num11++)
						{
							this.V[num11][num10] -= num3 * this.e[num11] + num4 * this.d[num11];
						}
						this.d[num10] = this.V[j - 1][num10];
						this.V[j][num10] = 0.0;
					}
				}
				this.d[j] = num2;
			}
			for (int num12 = 0; num12 < this.n - 1; num12++)
			{
				this.V[this.n - 1][num12] = this.V[num12][num12];
				this.V[num12][num12] = 1.0;
				double num13 = this.d[num12 + 1];
				if (num13 != 0.0)
				{
					for (int num14 = 0; num14 <= num12; num14++)
					{
						this.d[num14] = this.V[num14][num12 + 1] / num13;
					}
					for (int num15 = 0; num15 <= num12; num15++)
					{
						double num16 = 0.0;
						for (int num17 = 0; num17 <= num12; num17++)
						{
							num16 += this.V[num17][num12 + 1] * this.V[num17][num15];
						}
						for (int num18 = 0; num18 <= num12; num18++)
						{
							this.V[num18][num15] -= num16 * this.d[num18];
						}
					}
				}
				for (int num19 = 0; num19 <= num12; num19++)
				{
					this.V[num19][num12 + 1] = 0.0;
				}
			}
			for (int num20 = 0; num20 < this.n; num20++)
			{
				this.d[num20] = this.V[this.n - 1][num20];
				this.V[this.n - 1][num20] = 0.0;
			}
			this.V[this.n - 1][this.n - 1] = 1.0;
			this.e[0] = 0.0;
		}

		// Token: 0x06001681 RID: 5761 RVA: 0x00070DB8 File Offset: 0x0006EFB8
		void tql2()
		{
			for (int i = 1; i < this.n; i++)
			{
				this.e[i - 1] = this.e[i];
			}
			this.e[this.n - 1] = 0.0;
			double num = 0.0;
			double num2 = 0.0;
			double num3 = Math.Pow(2.0, -52.0);
			for (int j = 0; j < this.n; j++)
			{
				num2 = Math.Max(num2, Math.Abs(this.d[j]) + Math.Abs(this.e[j]));
				int num4 = j;
				while (num4 < this.n && Math.Abs(this.e[num4]) > num3 * num2)
				{
					num4++;
				}
				if (num4 > j)
				{
					int num5 = 0;
					do
					{
						num5++;
						double num6 = this.d[j];
						double num7 = (this.d[j + 1] - num6) / (2.0 * this.e[j]);
						double num8 = Maths.Hypot(num7, 1.0);
						if (num7 < 0.0)
						{
							num8 = -num8;
						}
						this.d[j] = this.e[j] / (num7 + num8);
						this.d[j + 1] = this.e[j] * (num7 + num8);
						double num9 = this.d[j + 1];
						double num10 = num6 - this.d[j];
						for (int k = j + 2; k < this.n; k++)
						{
							this.d[k] -= num10;
						}
						num += num10;
						num7 = this.d[num4];
						double num11 = 1.0;
						double num12 = num11;
						double num13 = num11;
						double num14 = this.e[j + 1];
						double num15 = 0.0;
						double num16 = 0.0;
						for (int l = num4 - 1; l >= j; l--)
						{
							num13 = num12;
							num12 = num11;
							num16 = num15;
							num6 = num11 * this.e[l];
							num10 = num11 * num7;
							num8 = Maths.Hypot(num7, this.e[l]);
							this.e[l + 1] = num15 * num8;
							num15 = this.e[l] / num8;
							num11 = num7 / num8;
							num7 = num11 * this.d[l] - num15 * num6;
							this.d[l + 1] = num10 + num15 * (num11 * num6 + num15 * this.d[l]);
							for (int m = 0; m < this.n; m++)
							{
								num10 = this.V[m][l + 1];
								this.V[m][l + 1] = num15 * this.V[m][l] + num11 * num10;
								this.V[m][l] = num11 * this.V[m][l] - num15 * num10;
							}
						}
						num7 = -num15 * num16 * num13 * num14 * this.e[j] / num9;
						this.e[j] = num15 * num7;
						this.d[j] = num11 * num7;
					}
					while (Math.Abs(this.e[j]) > num3 * num2);
				}
				this.d[j] = this.d[j] + num;
				this.e[j] = 0.0;
			}
			for (int n = 0; n < this.n - 1; n++)
			{
				int num17 = n;
				double num18 = this.d[n];
				for (int num19 = n + 1; num19 < this.n; num19++)
				{
					if (this.d[num19] < num18)
					{
						num17 = num19;
						num18 = this.d[num19];
					}
				}
				if (num17 != n)
				{
					this.d[num17] = this.d[n];
					this.d[n] = num18;
					for (int num20 = 0; num20 < this.n; num20++)
					{
						num18 = this.V[num20][n];
						this.V[num20][n] = this.V[num20][num17];
						this.V[num20][num17] = num18;
					}
				}
			}
		}

		// Token: 0x06001682 RID: 5762 RVA: 0x00071250 File Offset: 0x0006F450
		void orthes()
		{
			int num = 0;
			int num2 = this.n - 1;
			for (int i = num + 1; i <= num2 - 1; i++)
			{
				double num3 = 0.0;
				for (int j = i; j <= num2; j++)
				{
					num3 += Math.Abs(this.H[j][i - 1]);
				}
				if (num3 != 0.0)
				{
					double num4 = 0.0;
					for (int k = num2; k >= i; k--)
					{
						this.ort[k] = this.H[k][i - 1] / num3;
						num4 += this.ort[k] * this.ort[k];
					}
					double num5 = Math.Sqrt(num4);
					if (this.ort[i] > 0.0)
					{
						num5 = -num5;
					}
					num4 -= this.ort[i] * num5;
					this.ort[i] = this.ort[i] - num5;
					for (int l = i; l < this.n; l++)
					{
						double num6 = 0.0;
						for (int m = num2; m >= i; m--)
						{
							num6 += this.ort[m] * this.H[m][l];
						}
						num6 /= num4;
						for (int n = i; n <= num2; n++)
						{
							this.H[n][l] -= num6 * this.ort[n];
						}
					}
					for (int num7 = 0; num7 <= num2; num7++)
					{
						double num8 = 0.0;
						for (int num9 = num2; num9 >= i; num9--)
						{
							num8 += this.ort[num9] * this.H[num7][num9];
						}
						num8 /= num4;
						for (int num10 = i; num10 <= num2; num10++)
						{
							this.H[num7][num10] -= num8 * this.ort[num10];
						}
					}
					this.ort[i] = num3 * this.ort[i];
					this.H[i][i - 1] = num3 * num5;
				}
			}
			for (int num11 = 0; num11 < this.n; num11++)
			{
				for (int num12 = 0; num12 < this.n; num12++)
				{
					this.V[num11][num12] = ((num11 == num12) ? 1.0 : 0.0);
				}
			}
			for (int num13 = num2 - 1; num13 >= num + 1; num13--)
			{
				if (this.H[num13][num13 - 1] != 0.0)
				{
					for (int num14 = num13 + 1; num14 <= num2; num14++)
					{
						this.ort[num14] = this.H[num14][num13 - 1];
					}
					for (int num15 = num13; num15 <= num2; num15++)
					{
						double num16 = 0.0;
						for (int num17 = num13; num17 <= num2; num17++)
						{
							num16 += this.ort[num17] * this.V[num17][num15];
						}
						num16 = num16 / this.ort[num13] / this.H[num13][num13 - 1];
						for (int num18 = num13; num18 <= num2; num18++)
						{
							this.V[num18][num15] += num16 * this.ort[num18];
						}
					}
				}
			}
		}

		// Token: 0x06001683 RID: 5763 RVA: 0x00071628 File Offset: 0x0006F828
		void cdiv(double xr, double xi, double yr, double yi)
		{
			double num;
			double num2;
			if (Math.Abs(yr) > Math.Abs(yi))
			{
				num = yi / yr;
				num2 = yr + num * yi;
				this.cdivr = (xr + num * xi) / num2;
				this.cdivi = (xi - num * xr) / num2;
				return;
			}
			num = yr / yi;
			num2 = yi + num * yr;
			this.cdivr = (num * xr + xi) / num2;
			this.cdivi = (num * xi - xr) / num2;
		}

		// Token: 0x06001684 RID: 5764 RVA: 0x00071698 File Offset: 0x0006F898
		void hqr2()
		{
			int num = this.n;
			int i = num - 1;
			int num2 = 0;
			int num3 = num - 1;
			double num4 = Math.Pow(2.0, -52.0);
			double num5 = 0.0;
			double num6 = 0.0;
			double num7 = 0.0;
			double num8 = 0.0;
			double num9 = 0.0;
			double num10 = 0.0;
			double num11 = 0.0;
			for (int j = 0; j < num; j++)
			{
				if (j < num2 | j > num3)
				{
					this.d[j] = this.H[j][j];
					this.e[j] = 0.0;
				}
				for (int k = Math.Max(j - 1, 0); k < num; k++)
				{
					num11 += Math.Abs(this.H[j][k]);
				}
			}
			int num12 = 0;
			while (i >= num2)
			{
				int l;
				for (l = i; l > num2; l--)
				{
					num9 = Math.Abs(this.H[l - 1][l - 1]) + Math.Abs(this.H[l][l]);
					if (num9 == 0.0)
					{
						num9 = num11;
					}
					if (Math.Abs(this.H[l][l - 1]) < num4 * num9)
					{
						break;
					}
				}
				if (l == i)
				{
					this.H[i][i] = this.H[i][i] + num5;
					this.d[i] = this.H[i][i];
					this.e[i] = 0.0;
					i--;
					num12 = 0;
				}
				else if (l == i - 1)
				{
					double num13 = this.H[i][i - 1] * this.H[i - 1][i];
					num6 = (this.H[i - 1][i - 1] - this.H[i][i]) / 2.0;
					num7 = num6 * num6 + num13;
					num10 = Math.Sqrt(Math.Abs(num7));
					this.H[i][i] = this.H[i][i] + num5;
					this.H[i - 1][i - 1] = this.H[i - 1][i - 1] + num5;
					double num14 = this.H[i][i];
					if (num7 >= 0.0)
					{
						if (num6 >= 0.0)
						{
							num10 = num6 + num10;
						}
						else
						{
							num10 = num6 - num10;
						}
						this.d[i - 1] = num14 + num10;
						this.d[i] = this.d[i - 1];
						if (num10 != 0.0)
						{
							this.d[i] = num14 - num13 / num10;
						}
						this.e[i - 1] = 0.0;
						this.e[i] = 0.0;
						num14 = this.H[i][i - 1];
						num9 = Math.Abs(num14) + Math.Abs(num10);
						num6 = num14 / num9;
						num7 = num10 / num9;
						num8 = Math.Sqrt(num6 * num6 + num7 * num7);
						num6 /= num8;
						num7 /= num8;
						for (int m = i - 1; m < num; m++)
						{
							num10 = this.H[i - 1][m];
							this.H[i - 1][m] = num7 * num10 + num6 * this.H[i][m];
							this.H[i][m] = num7 * this.H[i][m] - num6 * num10;
						}
						for (int n = 0; n <= i; n++)
						{
							num10 = this.H[n][i - 1];
							this.H[n][i - 1] = num7 * num10 + num6 * this.H[n][i];
							this.H[n][i] = num7 * this.H[n][i] - num6 * num10;
						}
						for (int num15 = num2; num15 <= num3; num15++)
						{
							num10 = this.V[num15][i - 1];
							this.V[num15][i - 1] = num7 * num10 + num6 * this.V[num15][i];
							this.V[num15][i] = num7 * this.V[num15][i] - num6 * num10;
						}
					}
					else
					{
						this.d[i - 1] = num14 + num6;
						this.d[i] = num14 + num6;
						this.e[i - 1] = num10;
						this.e[i] = -num10;
					}
					i -= 2;
					num12 = 0;
				}
				else
				{
					double num14 = this.H[i][i];
					double num16 = 0.0;
					double num13 = 0.0;
					if (l < i)
					{
						num16 = this.H[i - 1][i - 1];
						num13 = this.H[i][i - 1] * this.H[i - 1][i];
					}
					if (num12 == 10)
					{
						num5 += num14;
						for (int num17 = num2; num17 <= i; num17++)
						{
							this.H[num17][num17] -= num14;
						}
						num9 = Math.Abs(this.H[i][i - 1]) + Math.Abs(this.H[i - 1][i - 2]);
						num16 = (num14 = 0.75 * num9);
						num13 = -0.4375 * num9 * num9;
					}
					if (num12 == 30)
					{
						num9 = (num16 - num14) / 2.0;
						num9 = num9 * num9 + num13;
						if (num9 > 0.0)
						{
							num9 = Math.Sqrt(num9);
							if (num16 < num14)
							{
								num9 = -num9;
							}
							num9 = num14 - num13 / ((num16 - num14) / 2.0 + num9);
							for (int num18 = num2; num18 <= i; num18++)
							{
								this.H[num18][num18] -= num9;
							}
							num5 += num9;
							num16 = (num14 = (num13 = 0.964));
						}
					}
					num12++;
					int num19;
					for (num19 = i - 2; num19 >= l; num19--)
					{
						num10 = this.H[num19][num19];
						num8 = num14 - num10;
						num9 = num16 - num10;
						num6 = (num8 * num9 - num13) / this.H[num19 + 1][num19] + this.H[num19][num19 + 1];
						num7 = this.H[num19 + 1][num19 + 1] - num10 - num8 - num9;
						num8 = this.H[num19 + 2][num19 + 1];
						num9 = Math.Abs(num6) + Math.Abs(num7) + Math.Abs(num8);
						num6 /= num9;
						num7 /= num9;
						num8 /= num9;
						if (num19 == l || Math.Abs(this.H[num19][num19 - 1]) * (Math.Abs(num7) + Math.Abs(num8)) < num4 * (Math.Abs(num6) * (Math.Abs(this.H[num19 - 1][num19 - 1]) + Math.Abs(num10) + Math.Abs(this.H[num19 + 1][num19 + 1]))))
						{
							break;
						}
					}
					for (int num20 = num19 + 2; num20 <= i; num20++)
					{
						this.H[num20][num20 - 2] = 0.0;
						if (num20 > num19 + 2)
						{
							this.H[num20][num20 - 3] = 0.0;
						}
					}
					for (int num21 = num19; num21 <= i - 1; num21++)
					{
						bool flag = num21 != i - 1;
						if (num21 != num19)
						{
							num6 = this.H[num21][num21 - 1];
							num7 = this.H[num21 + 1][num21 - 1];
							num8 = (flag ? this.H[num21 + 2][num21 - 1] : 0.0);
							num14 = Math.Abs(num6) + Math.Abs(num7) + Math.Abs(num8);
							if (num14 != 0.0)
							{
								num6 /= num14;
								num7 /= num14;
								num8 /= num14;
							}
						}
						if (num14 == 0.0)
						{
							break;
						}
						num9 = Math.Sqrt(num6 * num6 + num7 * num7 + num8 * num8);
						if (num6 < 0.0)
						{
							num9 = -num9;
						}
						if (num9 != 0.0)
						{
							if (num21 != num19)
							{
								this.H[num21][num21 - 1] = -num9 * num14;
							}
							else if (l != num19)
							{
								this.H[num21][num21 - 1] = -this.H[num21][num21 - 1];
							}
							num6 += num9;
							num14 = num6 / num9;
							num16 = num7 / num9;
							num10 = num8 / num9;
							num7 /= num6;
							num8 /= num6;
							for (int num22 = num21; num22 < num; num22++)
							{
								num6 = this.H[num21][num22] + num7 * this.H[num21 + 1][num22];
								if (flag)
								{
									num6 += num8 * this.H[num21 + 2][num22];
									this.H[num21 + 2][num22] = this.H[num21 + 2][num22] - num6 * num10;
								}
								this.H[num21][num22] = this.H[num21][num22] - num6 * num14;
								this.H[num21 + 1][num22] = this.H[num21 + 1][num22] - num6 * num16;
							}
							for (int num23 = 0; num23 <= Math.Min(i, num21 + 3); num23++)
							{
								num6 = num14 * this.H[num23][num21] + num16 * this.H[num23][num21 + 1];
								if (flag)
								{
									num6 += num10 * this.H[num23][num21 + 2];
									this.H[num23][num21 + 2] = this.H[num23][num21 + 2] - num6 * num8;
								}
								this.H[num23][num21] = this.H[num23][num21] - num6;
								this.H[num23][num21 + 1] = this.H[num23][num21 + 1] - num6 * num7;
							}
							for (int num24 = num2; num24 <= num3; num24++)
							{
								num6 = num14 * this.V[num24][num21] + num16 * this.V[num24][num21 + 1];
								if (flag)
								{
									num6 += num10 * this.V[num24][num21 + 2];
									this.V[num24][num21 + 2] = this.V[num24][num21 + 2] - num6 * num8;
								}
								this.V[num24][num21] = this.V[num24][num21] - num6;
								this.V[num24][num21 + 1] = this.V[num24][num21 + 1] - num6 * num7;
							}
						}
					}
				}
			}
			if (num11 == 0.0)
			{
				return;
			}
			for (i = num - 1; i >= 0; i--)
			{
				num6 = this.d[i];
				num7 = this.e[i];
				if (num7 == 0.0)
				{
					int num25 = i;
					this.H[i][i] = 1.0;
					for (int num26 = i - 1; num26 >= 0; num26--)
					{
						double num13 = this.H[num26][num26] - num6;
						num8 = 0.0;
						for (int num27 = num25; num27 <= i; num27++)
						{
							num8 += this.H[num26][num27] * this.H[num27][i];
						}
						if (this.e[num26] < 0.0)
						{
							num10 = num13;
							num9 = num8;
						}
						else
						{
							num25 = num26;
							double num28;
							if (this.e[num26] == 0.0)
							{
								if (num13 != 0.0)
								{
									this.H[num26][i] = -num8 / num13;
								}
								else
								{
									this.H[num26][i] = -num8 / (num4 * num11);
								}
							}
							else
							{
								double num14 = this.H[num26][num26 + 1];
								double num16 = this.H[num26 + 1][num26];
								num7 = (this.d[num26] - num6) * (this.d[num26] - num6) + this.e[num26] * this.e[num26];
								num28 = (num14 * num9 - num10 * num8) / num7;
								this.H[num26][i] = num28;
								if (Math.Abs(num14) > Math.Abs(num10))
								{
									this.H[num26 + 1][i] = (-num8 - num13 * num28) / num14;
								}
								else
								{
									this.H[num26 + 1][i] = (-num9 - num16 * num28) / num10;
								}
							}
							num28 = Math.Abs(this.H[num26][i]);
							if (num4 * num28 * num28 > 1.0)
							{
								for (int num29 = num26; num29 <= i; num29++)
								{
									this.H[num29][i] = this.H[num29][i] / num28;
								}
							}
						}
					}
				}
				else if (num7 < 0.0)
				{
					int num30 = i - 1;
					if (Math.Abs(this.H[i][i - 1]) > Math.Abs(this.H[i - 1][i]))
					{
						this.H[i - 1][i - 1] = num7 / this.H[i][i - 1];
						this.H[i - 1][i] = -(this.H[i][i] - num6) / this.H[i][i - 1];
					}
					else
					{
						this.cdiv(0.0, -this.H[i - 1][i], this.H[i - 1][i - 1] - num6, num7);
						this.H[i - 1][i - 1] = this.cdivr;
						this.H[i - 1][i] = this.cdivi;
					}
					this.H[i][i - 1] = 0.0;
					this.H[i][i] = 1.0;
					for (int num31 = i - 2; num31 >= 0; num31--)
					{
						double num32 = 0.0;
						double num33 = 0.0;
						for (int num34 = num30; num34 <= i; num34++)
						{
							num32 += this.H[num31][num34] * this.H[num34][i - 1];
							num33 += this.H[num31][num34] * this.H[num34][i];
						}
						double num13 = this.H[num31][num31] - num6;
						if (this.e[num31] < 0.0)
						{
							num10 = num13;
							num8 = num32;
							num9 = num33;
						}
						else
						{
							num30 = num31;
							if (this.e[num31] == 0.0)
							{
								this.cdiv(-num32, -num33, num13, num7);
								this.H[num31][i - 1] = this.cdivr;
								this.H[num31][i] = this.cdivi;
							}
							else
							{
								double num14 = this.H[num31][num31 + 1];
								double num16 = this.H[num31 + 1][num31];
								double num35 = (this.d[num31] - num6) * (this.d[num31] - num6) + this.e[num31] * this.e[num31] - num7 * num7;
								double num36 = (this.d[num31] - num6) * 2.0 * num7;
								if (num35 == 0.0 & num36 == 0.0)
								{
									num35 = num4 * num11 * (Math.Abs(num13) + Math.Abs(num7) + Math.Abs(num14) + Math.Abs(num16) + Math.Abs(num10));
								}
								this.cdiv(num14 * num8 - num10 * num32 + num7 * num33, num14 * num9 - num10 * num33 - num7 * num32, num35, num36);
								this.H[num31][i - 1] = this.cdivr;
								this.H[num31][i] = this.cdivi;
								if (Math.Abs(num14) > Math.Abs(num10) + Math.Abs(num7))
								{
									this.H[num31 + 1][i - 1] = (-num32 - num13 * this.H[num31][i - 1] + num7 * this.H[num31][i]) / num14;
									this.H[num31 + 1][i] = (-num33 - num13 * this.H[num31][i] - num7 * this.H[num31][i - 1]) / num14;
								}
								else
								{
									this.cdiv(-num8 - num16 * this.H[num31][i - 1], -num9 - num16 * this.H[num31][i], num10, num7);
									this.H[num31 + 1][i - 1] = this.cdivr;
									this.H[num31 + 1][i] = this.cdivi;
								}
							}
							double num28 = Math.Max(Math.Abs(this.H[num31][i - 1]), Math.Abs(this.H[num31][i]));
							if (num4 * num28 * num28 > 1.0)
							{
								for (int num37 = num31; num37 <= i; num37++)
								{
									this.H[num37][i - 1] = this.H[num37][i - 1] / num28;
									this.H[num37][i] = this.H[num37][i] / num28;
								}
							}
						}
					}
				}
			}
			for (int num38 = 0; num38 < num; num38++)
			{
				if (num38 < num2 | num38 > num3)
				{
					for (int num39 = num38; num39 < num; num39++)
					{
						this.V[num38][num39] = this.H[num38][num39];
					}
				}
			}
			for (int num40 = num - 1; num40 >= num2; num40--)
			{
				for (int num41 = num2; num41 <= num3; num41++)
				{
					num10 = 0.0;
					for (int num42 = num2; num42 <= Math.Min(num40, num3); num42++)
					{
						num10 += this.V[num41][num42] * this.H[num42][num40];
					}
					this.V[num41][num40] = num10;
				}
			}
		}

		// Token: 0x06001685 RID: 5765 RVA: 0x00072BCC File Offset: 0x00070DCC
		public EigenvalueDecomposition(Matrix Arg)
		{
			double[][] array = Arg.Array;
			this.n = Arg.ColumnDimension;
			this.V = new double[this.n][];
			for (int i = 0; i < this.V.Length; i++)
			{
				this.V[i] = new double[this.n];
			}
			this.d = new double[this.n];
			this.e = new double[this.n];
			this.issymmetric = true;
			int num = 0;
			while (num < this.n & this.issymmetric)
			{
				int num2 = 0;
				while (num2 < this.n & this.issymmetric)
				{
					this.issymmetric = (array[num2][num] == array[num][num2]);
					num2++;
				}
				num++;
			}
			if (this.issymmetric)
			{
				for (int j = 0; j < this.n; j++)
				{
					for (int k = 0; k < this.n; k++)
					{
						this.V[j][k] = array[j][k];
					}
				}
				this.tred2();
				this.tql2();
				return;
			}
			this.H = new double[this.n][];
			for (int l = 0; l < this.H.Length; l++)
			{
				this.H[l] = new double[this.n];
			}
			this.ort = new double[this.n];
			for (int m = 0; m < this.n; m++)
			{
				for (int n = 0; n < this.n; n++)
				{
					this.H[n][m] = array[n][m];
				}
			}
			this.orthes();
			this.hqr2();
		}

		// Token: 0x06001686 RID: 5766 RVA: 0x00072DC0 File Offset: 0x00070FC0
		public Matrix getV()
		{
			return new Matrix(this.V, this.n, this.n);
		}

		// Token: 0x06001687 RID: 5767 RVA: 0x00072DDC File Offset: 0x00070FDC
		public double[] getRealEigenvalues()
		{
			return this.d;
		}

		// Token: 0x06001688 RID: 5768 RVA: 0x00072DE4 File Offset: 0x00070FE4
		public double[] getImagEigenvalues()
		{
			return this.e;
		}

		// Token: 0x06001689 RID: 5769 RVA: 0x00072DEC File Offset: 0x00070FEC
		public Matrix getD()
		{
			Matrix matrix = new Matrix(this.n, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.n; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = 0.0;
				}
				array[i][i] = this.d[i];
				if (this.e[i] > 0.0)
				{
					array[i][i + 1] = this.e[i];
				}
				else if (this.e[i] < 0.0)
				{
					array[i][i - 1] = this.e[i];
				}
			}
			return matrix;
		}

		// Token: 0x04000D23 RID: 3363
		int n;

		// Token: 0x04000D24 RID: 3364
		bool issymmetric;

		// Token: 0x04000D25 RID: 3365
		double[] d;

		// Token: 0x04000D26 RID: 3366
		double[] e;

		// Token: 0x04000D27 RID: 3367
		double[][] V;

		// Token: 0x04000D28 RID: 3368
		double[][] H;

		// Token: 0x04000D29 RID: 3369
		double[] ort;

		// Token: 0x04000D2A RID: 3370
		[NonSerialized]
		double cdivr;

		// Token: 0x04000D2B RID: 3371
		[NonSerialized]
		double cdivi;
	}
}
