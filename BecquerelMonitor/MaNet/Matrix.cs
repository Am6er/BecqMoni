using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MaNet
{
	// Token: 0x020001E6 RID: 486
	[Serializable]
	public class Matrix : ICloneable, IEnumerable<double[]>, IEnumerable
	{
		// Token: 0x0600168A RID: 5770 RVA: 0x00072EBC File Offset: 0x000710BC
		public Matrix(int m, int n)
		{
			this.m = m;
			this.n = n;
			this.A = new double[m][];
			for (int i = 0; i < m; i++)
			{
				this.A[i] = new double[n];
			}
		}

		// Token: 0x0600168B RID: 5771 RVA: 0x00072F10 File Offset: 0x00071110
		public Matrix(int m) : this(m, m)
		{
		}

		// Token: 0x0600168C RID: 5772 RVA: 0x00072F1C File Offset: 0x0007111C
		public Matrix(int m, int n, double s)
		{
			this.m = m;
			this.n = n;
			this.A = new double[m][];
			for (int i = 0; i < m; i++)
			{
				this.A[i] = new double[n];
				for (int j = 0; j < n; j++)
				{
					this.A[i][j] = s;
				}
			}
		}

		// Token: 0x0600168D RID: 5773 RVA: 0x00072F8C File Offset: 0x0007118C
		public Matrix(double[][] A)
		{
			this.m = A.Length;
			this.n = A[0].Length;
			for (int i = 0; i < this.m; i++)
			{
				if (A[i].Length != this.n)
				{
					throw new ArgumentException("All rows must have the same length.");
				}
			}
			this.A = A;
		}

		// Token: 0x0600168E RID: 5774 RVA: 0x00072FF8 File Offset: 0x000711F8
		public Matrix(double[][] A, int m, int n)
		{
			this.A = A;
			this.m = m;
			this.n = n;
		}

		// Token: 0x0600168F RID: 5775 RVA: 0x00073018 File Offset: 0x00071218
		public Matrix(double[] vals, int m)
		{
			this.m = m;
			this.n = ((m != 0) ? (vals.Length / m) : 0);
			if (m * this.n != vals.Length)
			{
				throw new ArgumentException("Array length must be a multiple of m.");
			}
			this.A = new double[m][];
			for (int i = 0; i < m; i++)
			{
				this.A[i] = new double[this.n];
				for (int j = 0; j < this.n; j++)
				{
					this.A[i][j] = vals[i + j * m];
				}
			}
		}

		// Token: 0x06001690 RID: 5776 RVA: 0x000730C4 File Offset: 0x000712C4
		public static Matrix Diagonal(double[] a)
		{
			Matrix matrix = new Matrix(a.Length);
			double[][] array = matrix.Array;
			for (int i = 0; i < a.Length; i++)
			{
				array[i][i] = a[i];
			}
			return matrix;
		}

		// Token: 0x06001691 RID: 5777 RVA: 0x00073104 File Offset: 0x00071304
		public double[] GetDiagonal()
		{
			int num = Math.Min(this.m, this.n);
			double[] array = new double[num];
			for (int i = 0; i < num; i++)
			{
				array[i] = this.A[i][i];
			}
			return array;
		}

		// Token: 0x06001692 RID: 5778 RVA: 0x00073150 File Offset: 0x00071350
		public static Matrix operator *(Matrix m1, Matrix m2)
		{
			return m1.Times(m2);
		}

		// Token: 0x06001693 RID: 5779 RVA: 0x0007315C File Offset: 0x0007135C
		public static Matrix operator +(Matrix m1, Matrix m2)
		{
			return m1.Plus(m2);
		}

		// Token: 0x06001694 RID: 5780 RVA: 0x00073168 File Offset: 0x00071368
		public static Matrix ConstructWithCopy(double[][] A)
		{
			int num = A.Length;
			int num2 = A[0].Length;
			Matrix matrix = new Matrix(num, num2);
			double[][] array = matrix.Array;
			for (int i = 0; i < num; i++)
			{
				if (A[i].Length != num2)
				{
					throw new ArgumentException("All rows must have the same length.");
				}
				for (int j = 0; j < num2; j++)
				{
					array[i][j] = A[i][j];
				}
			}
			return matrix;
		}

		// Token: 0x06001695 RID: 5781 RVA: 0x000731F0 File Offset: 0x000713F0
		public Matrix Copy()
		{
			Matrix matrix = new Matrix(this.m, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.m; i++)
			{
				array[i] = new double[this.n];
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = this.A[i][j];
				}
			}
			return matrix;
		}

		// Token: 0x06001696 RID: 5782 RVA: 0x0007326C File Offset: 0x0007146C
		public object Clone()
		{
			return this.Copy();
		}

		// Token: 0x170006BF RID: 1727
		// (get) Token: 0x06001697 RID: 5783 RVA: 0x00073274 File Offset: 0x00071474
		public double[][] Array
		{
			get
			{
				return this.A;
			}
		}

		// Token: 0x06001698 RID: 5784 RVA: 0x0007327C File Offset: 0x0007147C
		public double[][] ArrayCopy()
		{
			double[][] array = new double[this.m][];
			for (int i = 0; i < this.m; i++)
			{
				array[i] = new double[this.n];
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = this.A[i][j];
				}
			}
			return array;
		}

		// Token: 0x06001699 RID: 5785 RVA: 0x000732EC File Offset: 0x000714EC
		public double[] ColumnPackedCopy()
		{
			double[] array = new double[this.m * this.n];
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i + j * this.m] = this.A[i][j];
				}
			}
			return array;
		}

		// Token: 0x0600169A RID: 5786 RVA: 0x00073354 File Offset: 0x00071554
		public double[] RowPackedCopy()
		{
			double[] array = new double[this.m * this.n];
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i * this.n + j] = this.A[i][j];
				}
			}
			return array;
		}

		// Token: 0x170006C0 RID: 1728
		// (get) Token: 0x0600169B RID: 5787 RVA: 0x000733BC File Offset: 0x000715BC
		public int RowDimension
		{
			get
			{
				return this.m;
			}
		}

		// Token: 0x170006C1 RID: 1729
		// (get) Token: 0x0600169C RID: 5788 RVA: 0x000733C4 File Offset: 0x000715C4
		public int ColumnDimension
		{
			get
			{
				return this.n;
			}
		}

		// Token: 0x0600169D RID: 5789 RVA: 0x000733CC File Offset: 0x000715CC
		public double Get(int i, int j)
		{
			return this.A[i][j];
		}

		// Token: 0x0600169E RID: 5790 RVA: 0x000733DC File Offset: 0x000715DC
		public Matrix GetColumn(int col)
		{
			return this.GetMatrix(0, this.m - 1, col, col);
		}

		// Token: 0x0600169F RID: 5791 RVA: 0x00073400 File Offset: 0x00071600
		public Matrix GetMatrix(int i0, int i1, int j0, int j1)
		{
			Matrix matrix = new Matrix(i1 - i0 + 1, j1 - j0 + 1);
			double[][] array = matrix.Array;
			try
			{
				for (int k = i0; k <= i1; k++)
				{
					for (int l = j0; l <= j1; l++)
					{
						array[k - i0][l - j0] = this.A[k][l];
					}
				}
			}
			catch (IndexOutOfRangeException)
			{
				throw new IndexOutOfRangeException("Submatrix indices");
			}
			return matrix;
		}

		// Token: 0x060016A0 RID: 5792 RVA: 0x00073484 File Offset: 0x00071684
		public Matrix GetMatrix(int[] r, int[] c)
		{
			Matrix matrix = new Matrix(r.Length, c.Length);
			double[][] array = matrix.Array;
			try
			{
				for (int i = 0; i < r.Length; i++)
				{
					for (int j = 0; j < c.Length; j++)
					{
						array[i][j] = this.A[r[i]][c[j]];
					}
				}
			}
			catch (IndexOutOfRangeException)
			{
				throw new IndexOutOfRangeException("Submatrix indices");
			}
			return matrix;
		}

		// Token: 0x060016A1 RID: 5793 RVA: 0x00073504 File Offset: 0x00071704
		public Matrix GetMatrix(int i0, int i1, int[] c)
		{
			Matrix matrix = new Matrix(i1 - i0 + 1, c.Length);
			double[][] array = matrix.Array;
			try
			{
				for (int j = i0; j <= i1; j++)
				{
					for (int k = 0; k < c.Length; k++)
					{
						array[j - i0][k] = this.A[j][c[k]];
					}
				}
			}
			catch (IndexOutOfRangeException)
			{
				throw new IndexOutOfRangeException("Submatrix indices");
			}
			return matrix;
		}

		// Token: 0x060016A2 RID: 5794 RVA: 0x00073584 File Offset: 0x00071784
		public Matrix GetMatrix(int[] r, int j0, int j1)
		{
			Matrix matrix = new Matrix(r.Length, j1 - j0 + 1);
			double[][] array = matrix.Array;
			try
			{
				for (int i = 0; i < r.Length; i++)
				{
					for (int k = j0; k <= j1; k++)
					{
						array[i][k - j0] = this.A[r[i]][k];
					}
				}
			}
			catch (IndexOutOfRangeException)
			{
				throw new IndexOutOfRangeException("Submatrix indices");
			}
			return matrix;
		}

		// Token: 0x060016A3 RID: 5795 RVA: 0x00073604 File Offset: 0x00071804
		public void Set(int i, int j, double s)
		{
			this.A[i][j] = s;
		}

		// Token: 0x060016A4 RID: 5796 RVA: 0x00073618 File Offset: 0x00071818
		public void SetMatrix(int i0, int i1, int j0, int j1, Matrix X)
		{
			try
			{
				for (int k = i0; k <= i1; k++)
				{
					for (int l = j0; l <= j1; l++)
					{
						this.A[k][l] = X.Get(k - i0, l - j0);
					}
				}
			}
			catch (IndexOutOfRangeException)
			{
				throw new IndexOutOfRangeException("Submatrix indices");
			}
		}

		// Token: 0x060016A5 RID: 5797 RVA: 0x00073680 File Offset: 0x00071880
		public void SetMatrix(int[] r, int[] c, Matrix X)
		{
			try
			{
				for (int i = 0; i < r.Length; i++)
				{
					for (int j = 0; j < c.Length; j++)
					{
						this.A[r[i]][c[j]] = X.Get(i, j);
					}
				}
			}
			catch (IndexOutOfRangeException)
			{
				throw new IndexOutOfRangeException("Submatrix indices");
			}
		}

		// Token: 0x060016A6 RID: 5798 RVA: 0x000736EC File Offset: 0x000718EC
		public void SetMatrix(int[] r, int j0, int j1, Matrix X)
		{
			try
			{
				for (int i = 0; i < r.Length; i++)
				{
					for (int k = j0; k <= j1; k++)
					{
						this.A[r[i]][k] = X.Get(i, k - j0);
					}
				}
			}
			catch (IndexOutOfRangeException)
			{
				throw new IndexOutOfRangeException("Submatrix indices");
			}
		}

		// Token: 0x060016A7 RID: 5799 RVA: 0x00073758 File Offset: 0x00071958
		public void SetMatrix(int i0, int i1, int[] c, Matrix X)
		{
			try
			{
				for (int j = i0; j <= i1; j++)
				{
					for (int k = 0; k < c.Length; k++)
					{
						this.A[j][c[k]] = X.Get(j - i0, k);
					}
				}
			}
			catch (IndexOutOfRangeException)
			{
				throw new IndexOutOfRangeException("Submatrix indices");
			}
		}

		// Token: 0x060016A8 RID: 5800 RVA: 0x000737C4 File Offset: 0x000719C4
		public Matrix Transpose()
		{
			Matrix matrix = new Matrix(this.n, this.m);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[j][i] = this.A[i][j];
				}
			}
			return matrix;
		}

		// Token: 0x060016A9 RID: 5801 RVA: 0x00073830 File Offset: 0x00071A30
		public double Norm1()
		{
			double num = 0.0;
			for (int i = 0; i < this.n; i++)
			{
				double num2 = 0.0;
				for (int j = 0; j < this.m; j++)
				{
					num2 += Math.Abs(this.A[j][i]);
				}
				num = Math.Max(num, num2);
			}
			return num;
		}

		// Token: 0x060016AA RID: 5802 RVA: 0x0007389C File Offset: 0x00071A9C
		public double Norm2()
		{
			return new SingularValueDecomposition(this).Norm2();
		}

		// Token: 0x060016AB RID: 5803 RVA: 0x000738AC File Offset: 0x00071AAC
		public double NormInf()
		{
			double num = 0.0;
			for (int i = 0; i < this.m; i++)
			{
				double num2 = 0.0;
				for (int j = 0; j < this.n; j++)
				{
					num2 += Math.Abs(this.A[i][j]);
				}
				num = Math.Max(num, num2);
			}
			return num;
		}

		// Token: 0x060016AC RID: 5804 RVA: 0x00073918 File Offset: 0x00071B18
		public double NormF()
		{
			double num = 0.0;
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					num = Maths.Hypot(num, this.A[i][j]);
				}
			}
			return num;
		}

		// Token: 0x060016AD RID: 5805 RVA: 0x00073974 File Offset: 0x00071B74
		public Matrix Uminus()
		{
			Matrix matrix = new Matrix(this.m, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = -this.A[i][j];
				}
			}
			return matrix;
		}

		// Token: 0x060016AE RID: 5806 RVA: 0x000739E0 File Offset: 0x00071BE0
		public Matrix Plus(Matrix B)
		{
			this.CheckMatrixDimensions(B);
			Matrix matrix = new Matrix(this.m, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = this.A[i][j] + B.A[i][j];
				}
			}
			return matrix;
		}

		// Token: 0x060016AF RID: 5807 RVA: 0x00073A60 File Offset: 0x00071C60
		public Matrix PlusEquals(Matrix B)
		{
			this.CheckMatrixDimensions(B);
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					this.A[i][j] = this.A[i][j] + B.A[i][j];
				}
			}
			return this;
		}

		// Token: 0x060016B0 RID: 5808 RVA: 0x00073ACC File Offset: 0x00071CCC
		public Matrix Minus(Matrix B)
		{
			this.CheckMatrixDimensions(B);
			Matrix matrix = new Matrix(this.m, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = this.A[i][j] - B.A[i][j];
				}
			}
			return matrix;
		}

		// Token: 0x060016B1 RID: 5809 RVA: 0x00073B4C File Offset: 0x00071D4C
		public Matrix MinusEquals(Matrix B)
		{
			this.CheckMatrixDimensions(B);
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					this.A[i][j] = this.A[i][j] - B.A[i][j];
				}
			}
			return this;
		}

		// Token: 0x060016B2 RID: 5810 RVA: 0x00073BB8 File Offset: 0x00071DB8
		public Matrix ArrayTimes(Matrix B)
		{
			this.CheckMatrixDimensions(B);
			Matrix matrix = new Matrix(this.m, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = this.A[i][j] * B.A[i][j];
				}
			}
			return matrix;
		}

		// Token: 0x060016B3 RID: 5811 RVA: 0x00073C38 File Offset: 0x00071E38
		public Matrix ArrayTimesEquals(Matrix B)
		{
			this.CheckMatrixDimensions(B);
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					this.A[i][j] = this.A[i][j] * B.A[i][j];
				}
			}
			return this;
		}

		// Token: 0x060016B4 RID: 5812 RVA: 0x00073CA4 File Offset: 0x00071EA4
		public Matrix ArrayRightDivide(Matrix B)
		{
			this.CheckMatrixDimensions(B);
			Matrix matrix = new Matrix(this.m, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = this.A[i][j] / B.A[i][j];
				}
			}
			return matrix;
		}

		// Token: 0x060016B5 RID: 5813 RVA: 0x00073D24 File Offset: 0x00071F24
		public Matrix ArrayRightDivideEquals(Matrix B)
		{
			this.CheckMatrixDimensions(B);
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					this.A[i][j] = this.A[i][j] / B.A[i][j];
				}
			}
			return this;
		}

		// Token: 0x060016B6 RID: 5814 RVA: 0x00073D90 File Offset: 0x00071F90
		public Matrix ArrayLeftDivide(Matrix B)
		{
			this.CheckMatrixDimensions(B);
			Matrix matrix = new Matrix(this.m, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = B.A[i][j] / this.A[i][j];
				}
			}
			return matrix;
		}

		// Token: 0x060016B7 RID: 5815 RVA: 0x00073E10 File Offset: 0x00072010
		public Matrix ArrayLeftDivideEquals(Matrix B)
		{
			this.CheckMatrixDimensions(B);
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					this.A[i][j] = B.A[i][j] / this.A[i][j];
				}
			}
			return this;
		}

		// Token: 0x060016B8 RID: 5816 RVA: 0x00073E7C File Offset: 0x0007207C
		public Matrix Times(double s)
		{
			Matrix matrix = new Matrix(this.m, this.n);
			double[][] array = matrix.Array;
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array[i][j] = s * this.A[i][j];
				}
			}
			return matrix;
		}

		// Token: 0x060016B9 RID: 5817 RVA: 0x00073EE8 File Offset: 0x000720E8
		public Matrix TimesEquals(double s)
		{
			for (int i = 0; i < this.m; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					this.A[i][j] = s * this.A[i][j];
				}
			}
			return this;
		}

		// Token: 0x060016BA RID: 5818 RVA: 0x00073F40 File Offset: 0x00072140
		public Matrix Times(Matrix B)
		{
			if (B.m != this.n)
			{
				throw new ArgumentException("Matrix inner dimensions must agree.");
			}
			Matrix matrix = new Matrix(this.m, B.n);
			double[][] array = matrix.Array;
			double[] array2 = new double[this.n];
			for (int i = 0; i < B.n; i++)
			{
				for (int j = 0; j < this.n; j++)
				{
					array2[j] = B.A[j][i];
				}
				for (int k = 0; k < this.m; k++)
				{
					double[] array3 = this.A[k];
					double num = 0.0;
					for (int l = 0; l < this.n; l++)
					{
						num += array3[l] * array2[l];
					}
					array[k][i] = num;
				}
			}
			return matrix;
		}

		// Token: 0x060016BB RID: 5819 RVA: 0x0007403C File Offset: 0x0007223C
		public LUDecomposition Lu()
		{
			return new LUDecomposition(this);
		}

		// Token: 0x060016BC RID: 5820 RVA: 0x00074044 File Offset: 0x00072244
		public QRDecomposition Qr()
		{
			return new QRDecomposition(this);
		}

		// Token: 0x060016BD RID: 5821 RVA: 0x0007404C File Offset: 0x0007224C
		public CholeskyDecomposition Chol()
		{
			return new CholeskyDecomposition(this);
		}

		// Token: 0x060016BE RID: 5822 RVA: 0x00074054 File Offset: 0x00072254
		public SingularValueDecomposition Svd()
		{
			return new SingularValueDecomposition(this);
		}

		// Token: 0x060016BF RID: 5823 RVA: 0x0007405C File Offset: 0x0007225C
		public EigenvalueDecomposition Eig()
		{
			return new EigenvalueDecomposition(this);
		}

		// Token: 0x060016C0 RID: 5824 RVA: 0x00074064 File Offset: 0x00072264
		public Matrix Solve(Matrix B)
		{
			if (this.m != this.n)
			{
				return new QRDecomposition(this).Solve(B);
			}
			return new LUDecomposition(this).solve(B);
		}

		// Token: 0x060016C1 RID: 5825 RVA: 0x00074090 File Offset: 0x00072290
		public Matrix SolveTranspose(Matrix B)
		{
			return this.Transpose().Solve(B.Transpose());
		}

		// Token: 0x060016C2 RID: 5826 RVA: 0x000740A4 File Offset: 0x000722A4
		public Matrix Inverse()
		{
			return this.Solve(Matrix.Identity(this.m, this.m));
		}

		// Token: 0x060016C3 RID: 5827 RVA: 0x000740C0 File Offset: 0x000722C0
		public double Det()
		{
			return new LUDecomposition(this).det();
		}

		// Token: 0x060016C4 RID: 5828 RVA: 0x000740D0 File Offset: 0x000722D0
		public int Rank()
		{
			return new SingularValueDecomposition(this).Rank();
		}

		// Token: 0x060016C5 RID: 5829 RVA: 0x000740E0 File Offset: 0x000722E0
		public double Cond()
		{
			return new SingularValueDecomposition(this).Cond();
		}

		// Token: 0x060016C6 RID: 5830 RVA: 0x000740F0 File Offset: 0x000722F0
		public double Trace()
		{
			double num = 0.0;
			for (int i = 0; i < Math.Min(this.m, this.n); i++)
			{
				num += this.A[i][i];
			}
			return num;
		}

		// Token: 0x060016C7 RID: 5831 RVA: 0x0007413C File Offset: 0x0007233C
		public static Matrix Identity(int m, int n)
		{
			Matrix matrix = new Matrix(m, n);
			double[][] array = matrix.Array;
			for (int i = 0; i < m; i++)
			{
				for (int j = 0; j < n; j++)
				{
					array[i][j] = ((i == j) ? 1.0 : 0.0);
				}
			}
			return matrix;
		}

		// Token: 0x060016C8 RID: 5832 RVA: 0x000741A4 File Offset: 0x000723A4
		public override string ToString()
		{
			return this.ToMatLabString();
		}

		// Token: 0x060016C9 RID: 5833 RVA: 0x000741AC File Offset: 0x000723AC
		public static Matrix Parse(string str)
		{
			str.Trim();
			if (str.StartsWith("["))
			{
				return Matrix.ParseMatLab(str);
			}
			if (str.StartsWith("{"))
			{
				return Matrix.ParseMathematica(str);
			}
			StringReader stringReader = new StringReader(str);
			Matrix result = Matrix.Load(stringReader);
			stringReader.Close();
			stringReader.Dispose();
			return result;
		}

		// Token: 0x060016CA RID: 5834 RVA: 0x00074210 File Offset: 0x00072410
		public string ToString(string matrixFrontCap, string rowFrontCap, string rowDelimiter, string columnDelimiter, string rowEndCap, string matrixEndCap)
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append(matrixFrontCap);
			for (int i = 0; i < this.m; i++)
			{
				stringBuilder.Append(rowFrontCap);
				for (int j = 0; j < this.n; j++)
				{
					stringBuilder.Append(this.A[i][j].ToString("R"));
					stringBuilder.Append((j < this.n - 1) ? columnDelimiter : "");
				}
				stringBuilder.Append(rowEndCap);
				stringBuilder.Append((i < this.m - 1) ? rowDelimiter : "");
			}
			stringBuilder.Append(matrixEndCap);
			return stringBuilder.ToString();
		}

		// Token: 0x060016CB RID: 5835 RVA: 0x000742E0 File Offset: 0x000724E0
		public static Matrix Parse(string str, string matrixFrontCap, string rowFrontCap, string rowDelimiter, string columnDelimiter, string rowEndCap, string matrixEndCap)
		{
			if (!str.StartsWith(matrixFrontCap))
			{
				throw new Exception(string.Concat(new string[]
				{
					"string does not begin with proper value: ",
					matrixFrontCap,
					" was expected, but ",
					str.Substring(0, matrixFrontCap.Length),
					" was found."
				}));
			}
			if (!str.StartsWith(matrixFrontCap))
			{
				throw new Exception(string.Concat(new string[]
				{
					"string does not end with proper value: ",
					matrixEndCap,
					" was expected, but ",
					str.Substring(str.Length - matrixEndCap.Length, matrixEndCap.Length),
					" was found."
				}));
			}
			string text = str.Substring(matrixFrontCap.Length, str.Length - matrixFrontCap.Length - matrixEndCap.Length);
			string[] separator = new string[]
			{
				rowEndCap + rowDelimiter + rowFrontCap
			};
			string[] array = text.Split(separator, StringSplitOptions.RemoveEmptyEntries);
			if (array.Length == 0)
			{
				throw new Exception("No rows present");
			}
			array[0] = array[0].Substring(rowFrontCap.Length);
			array[array.Length - 1] = array[array.Length - 1].Substring(0, array[array.Length - 1].Length - rowEndCap.Length);
			double[][] array2 = new double[array.Length][];
			int num = 0;
			for (int i = 0; i < array.Length; i++)
			{
				string[] array3 = array[i].Split(new string[]
				{
					columnDelimiter
				}, StringSplitOptions.RemoveEmptyEntries);
				if (num != 0)
				{
					if (num != array3.Length)
					{
						throw new Exception("Rows are not of consistant lenght");
					}
				}
				else
				{
					num = array3.Length;
				}
				double[] array4 = new double[num];
				for (int j = 0; j < num; j++)
				{
					array4[j] = double.Parse(array3[j]);
				}
				array2[i] = array4;
			}
			return new Matrix(array2);
		}

		// Token: 0x060016CC RID: 5836 RVA: 0x00074520 File Offset: 0x00072720
		public string DisplayText(int decimalPlaces)
		{
			double num = double.MinValue;
			for (int i = 0; i < this.RowDimension; i++)
			{
				for (int j = 0; j < this.ColumnDimension; j++)
				{
					if (this.A[i][j] > num)
					{
						num = this.A[i][j];
					}
				}
			}
			int num2 = Math.Truncate(num).ToString().Length;
			if (decimalPlaces > 0)
			{
				num2 += decimalPlaces + 1;
			}
			StringBuilder stringBuilder = new StringBuilder();
			for (int k = 0; k < this.RowDimension; k++)
			{
				for (int l = 0; l < this.ColumnDimension; l++)
				{
					string text = this.A[k][l].ToString("N" + decimalPlaces);
					int num3 = num2 - text.Length;
					if (l > 0)
					{
						num3++;
					}
					for (int m = 0; m < num3; m++)
					{
						stringBuilder.Append(" ");
					}
					stringBuilder.Append(text);
				}
				if (k < this.RowDimension - 1)
				{
					stringBuilder.Append("\n");
				}
			}
			return stringBuilder.ToString();
		}

		// Token: 0x060016CD RID: 5837 RVA: 0x00074678 File Offset: 0x00072878
		public string ToArrayString()
		{
			string matrixFrontCap = "new double[" + this.RowDimension + "][] { ";
			string rowFrontCap = "new double[" + this.ColumnDimension + "] { ";
			return this.ToString(matrixFrontCap, rowFrontCap, ", ", ", ", " }", " }");
		}

		// Token: 0x060016CE RID: 5838 RVA: 0x000746DC File Offset: 0x000728DC
		public string ToMathematicaString()
		{
			return this.ToString("{", "{", ", ", ", ", "}", "}");
		}

		// Token: 0x060016CF RID: 5839 RVA: 0x00074704 File Offset: 0x00072904
		public static Matrix ParseMathematica(string str)
		{
			return Matrix.Parse(str, "{", "{", ", ", ", ", "}", "}");
		}

		// Token: 0x060016D0 RID: 5840 RVA: 0x0007472C File Offset: 0x0007292C
		public string ToMatLabString()
		{
			return this.ToString("[", "", ";", " ", "", "]");
		}

		// Token: 0x060016D1 RID: 5841 RVA: 0x00074754 File Offset: 0x00072954
		public static Matrix ParseMatLab(string str)
		{
			return Matrix.Parse(str, "[", "", ";", " ", "", "]");
		}

		// Token: 0x060016D2 RID: 5842 RVA: 0x0007477C File Offset: 0x0007297C
		public static Matrix Load(string path)
		{
			string str = File.ReadAllText(path);
			return Matrix.Parse(str);
		}

		// Token: 0x060016D3 RID: 5843 RVA: 0x0007479C File Offset: 0x0007299C
		public static Matrix Load(TextReader reader)
		{
			List<double[]> list = new List<double[]>();
			Regex regex = new Regex("\\s+");
			while (reader.Peek() != -1)
			{
				string text = reader.ReadLine().Trim();
				if (text != "")
				{
					string[] array = regex.Split(text);
					double[] array2 = new double[array.Length];
					for (int i = 0; i < array.Length; i++)
					{
						double num = 0.0;
						if (!double.TryParse(array[i], out num))
						{
							throw new ArgumentException("Invalid string");
						}
						array2[i] = num;
					}
					list.Add(array2);
				}
			}
			if (list.Count > 1)
			{
				int num2 = list[0].Length;
				for (int j = 1; j < list.Count; j++)
				{
					if (list[j].Length != num2)
					{
						throw new ArgumentException("Rows of inconsistant length");
					}
				}
				double[][] array3 = new double[list.Count][];
				list.CopyTo(array3);
				return new Matrix(array3);
			}
			if (list.Count == 1)
			{
				double[][] array4 = new double[list.Count][];
				list.CopyTo(array4);
				return new Matrix(array4);
			}
			return null;
		}

		// Token: 0x060016D4 RID: 5844 RVA: 0x000748EC File Offset: 0x00072AEC
		public DataTable ToDataTable()
		{
			DataTable dataTable = new DataTable();
			for (int i = 0; i < this.ColumnDimension; i++)
			{
				dataTable.Columns.Add("col" + i, typeof(double));
			}
			foreach (double[] array in this.A)
			{
				DataRow dataRow = dataTable.NewRow();
				for (int k = 0; k < this.ColumnDimension; k++)
				{
					dataRow[k] = array[k];
				}
				dataTable.Rows.Add(dataRow);
			}
			return dataTable;
		}

		// Token: 0x060016D5 RID: 5845 RVA: 0x000749A0 File Offset: 0x00072BA0
		public static Matrix FromDataTable(DataTable dt)
		{
			double[][] array = new double[dt.Rows.Count][];
			for (int i = 0; i < dt.Rows.Count; i++)
			{
				double[] array2 = new double[dt.Columns.Count];
				for (int j = 0; j < dt.Columns.Count; j++)
				{
					array2[j] = (double)dt.Rows[i][j];
				}
				array[i] = array2;
			}
			return new Matrix(array);
		}

		// Token: 0x060016D6 RID: 5846 RVA: 0x00074A30 File Offset: 0x00072C30
		void CheckMatrixDimensions(Matrix B)
		{
			if (B.m != this.m || B.n != this.n)
			{
				throw new ArgumentException("Matrix dimensions must agree.");
			}
		}

		// Token: 0x060016D7 RID: 5847 RVA: 0x00074A60 File Offset: 0x00072C60
		public IEnumerator<double[]> GetEnumerator()
		{
			throw new NotImplementedException();
		}

		// Token: 0x060016D8 RID: 5848 RVA: 0x00074A68 File Offset: 0x00072C68
		IEnumerator IEnumerable.GetEnumerator()
		{
			foreach (double[] row in this.A)
			{
				yield return row;
			}
			yield break;
		}

		// Token: 0x04000D2C RID: 3372
		double[][] A;

		// Token: 0x04000D2D RID: 3373
		int m;

		// Token: 0x04000D2E RID: 3374
		int n;
	}
}
