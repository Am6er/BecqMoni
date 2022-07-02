using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Data;
namespace MaNet
{
   
    /// <summary>
   ///The Java Matrix Class provides the fundamental operations of numerical
   ///linear algebra.  Various constructors create Matrices from two dimensional
   ///arrays of double precision floating point numbers.  Various "gets" and
   ///"sets" provide access to submatrices and matrix elements.  Several methods 
   ///implement basic matrix arithmetic, including matrix addition and
   ///multiplication, matrix norms, and element-by-element array operations.
   ///Methods for reading and printing matrices are also included.  All the
   ///operations in this version of the Matrix Class involve real matrices.
    /// </summary>
    [Serializable]
public class Matrix :ICloneable , IEnumerable<double[]>
    {

/** Original introduction
   Jama = Java Matrix class.
<P>
   The Java Matrix Class provides the fundamental operations of numerical
   linear algebra.  Various constructors create Matrices from two dimensional
   arrays of double precision floating point numbers.  Various "gets" and
   "sets" provide access to submatrices and matrix elements.  Several methods 
   implement basic matrix arithmetic, including matrix addition and
   multiplication, matrix norms, and element-by-element array operations.
   Methods for reading and printing matrices are also included.  All the
   operations in this version of the Matrix Class involve real matrices.
   Complex matrices may be handled in a future version.
<P>
   Five fundamental matrix decompositions, which consist of pairs or triples
   of matrices, permutation vectors, and the like, produce results in five
   decomposition classes.  These decompositions are accessed by the Matrix
   class to compute solutions of simultaneous linear equations, determinants,
   inverses and other matrix functions.  The five decompositions are:
<P><UL>
   <LI>Cholesky Decomposition of symmetric, positive definite matrices.
   <LI>LU Decomposition of rectangular matrices.
   <LI>QR Decomposition of rectangular matrices.
   <LI>Singular Value Decomposition of rectangular matrices.
   <LI>Eigenvalue Decomposition of both symmetric and nonsymmetric square matrices.
</UL>
<DL>
<DT><B>Example of use:</B></DT>
<P>
<DD>Solve a linear system A x = b and compute the residual norm, ||b - A x||.
<P><PRE>
      double[][] vals = {{1.,2.,3},{4.,5.,6.},{7.,8.,10.}};
      Matrix A = new Matrix(vals);
      Matrix b = Matrix.random(3,1);
      Matrix x = A.solve(b);
      Matrix r = A.times(x).minus(b);
      double rnorm = r.normInf();
</PRE></DD>
</DL>

@author The MathWorks, Inc. and the National Institute of Standards and Technology.
@version 5 August 1998
*/





/* ------------------------
   Class variables
 * ------------------------ */
        
   /** Array for internal storage of elements.
   @serial internal array storage.
   */
   private double[][] A;

   /** Row and column dimensions.
   @serial row dimension.
   @serial column dimension.
   */
   private int m, n;

 
   #region Constructors 
   ///<summary>Construct an m-by-n matrix of zeros</summary>
    ///<param name="m">Number of rows.</param>
    ///<param name="n">Number of colums</param>
   public Matrix (int m, int n) {
      this.m = m;
      this.n = n;
      A = new double[m][];
      for (int i = 0; i < m; i++)//Added by kj
      {
          A[i] = new double[n];
      }
   }

   /// <summary>
   /// Construct an m-by-M matrix of zeros
   /// </summary>
   /// <param name="m">Number of rows and columns</param>
   public Matrix(int m):this(m,m)
   {

   }

   ///<summary>Construct an m-by-n constant matrix.</summary>
   ///<param name="m">Number of rows.</param>
   ///<param name="n">Number of colums.</param>
   ///<param name="s">Fill the matrix with this scalar value.</param>
   public Matrix(int m, int n, double s)
   {
      this.m = m;
      this.n = n;
      A = new double[m][];
      for (int i = 0; i < m; i++) {
         A[i] = new double[n]; //Added by kj
         for (int j = 0; j < n; j++) {
            A[i][j] = s;
         }
      }
   }
 

  ///<summary>Construct a matrix from a 2-D array</summary>
  ///<param name="A">Two-dimensional array of doubles.</param>
   ///<remarks>Differs from ConstructWithCopy in that the original array is used in the Matrix, not just its values</remarks>
   public Matrix (double[][] A) {
      m = A.Length;
      n = A[0].Length;
      for (int i = 0; i < m; i++) {
         if (A[i].Length != n) {
            throw new ArgumentException("All rows must have the same length.");
         }
      }
      this.A = A;
   }


   ///<summary>Construct a matrix quickly without checking arguments.</summary>    
   ///<param name="A">Two-dimensional array of doubles.</param>
   ///<param name="m">Number of rows.</param>
   ///<param name="n">Number of colums.</param>
   public Matrix (double[][] A, int m, int n) {
      this.A = A;
      this.m = m;
      this.n = n;
   }

 
   ///<summary>Construct a matrix from a one-dimensional packed array</summary>
   ///<param name="vals">One-dimensional array of doubles, packed by columns (ala Fortran).</param>
   ///<param name="m">Number of rows.</param>
   public Matrix (double[] vals, int m) {
      this.m = m;
      n = (m != 0 ? vals.Length/m : 0);
      if (m*n != vals.Length) {
         throw new System.ArgumentException("Array length must be a multiple of m.");
      }
      A = new double[m][];
      for (int i = 0; i < m; i++) {
          A[i] = new double[n]; //Added by KJ
         for (int j = 0; j < n; j++) {
            A[i][j] = vals[i+j*m];
         }
      }
   }


   #endregion

   /// <summary>
   /// Constructs a diagonal matrix whose diagonal values are the Array.
   /// </summary>
   /// <param name="a">The diagonal values of the matrix</param>
   /// <returns>The Diagonal matrix</returns>
   public static Matrix Diagonal(double[] a)
   {
       Matrix A = new Matrix(a.Length);
       double[][] X = A.Array;
       for (int i = 0; i < a.Length; i++)
       {
           X[i][i] = a[i];
       }
       return A;
   }

    /// <summary>
    /// Returns an Array of the diagonal values of the Matrix.
    /// in the case of a rectaangular matrix it returns the 
    /// diagonal values up until the edge is reached.
    /// </summary>
    /// <returns>Array of Diagonal values</returns>
   public double[] GetDiagonal()
   {
       int diaLength = Math.Min(m, n);
       double[] a = new double[diaLength];
       for (int i = 0; i < diaLength; i++)
       {
           a[i] = A[i][i];

       }
       return a;
   }


   #region Operators

   public static Matrix operator *(Matrix m1, Matrix m2)
   {
       return m1.Times(m2);
   }

   public static Matrix operator +(Matrix m1, Matrix m2)
   {
       return m1.Plus(m2);
   }


   #endregion

   /* ------------------------
   Public Methods
 * ------------------------ */

   ///<summary>Construct a matrix from a copy of a 2-D array.</summary>
   ///<param name="A">Two-dimensional array of doubles.</param>
   public static Matrix ConstructWithCopy(double[][] A) {
      int m = A.Length;
      int n = A[0].Length;
      Matrix X = new Matrix(m,n);
      double[][] C = X.Array;
      for (int i = 0; i < m; i++) {
         if (A[i].Length != n) {
            throw new System.ArgumentException
               ("All rows must have the same length.");
         }
         for (int j = 0; j < n; j++) {
            C[i][j] = A[i][j];
         }
      }
      return X;
   }



  ///<summary>Make a deep copy of a matrix</summary>
   public Matrix Copy () {
      Matrix X = new Matrix(m,n);
      double[][] C = X.Array;
      for (int i = 0; i < m; i++) {
          C[i] = new double[n]; //Added by KJ
         for (int j = 0; j < n; j++) {
            C[i][j] = A[i][j];
         }
      }
      return X;
   }
 
   ///<summary>Clone the Matrix object.</summary>
   public Object Clone () {
      return this.Copy();
   }


   ///<summary>Access the internal two-dimensional array.</summary>
   ///<returns>Pointer to the two-dimensional array of matrix elements.</returns>
   public double[][] Array  {
       get
       {
           return A;
       }
   }

 

  ///<summary>Copy the internal two-dimensional array.</summary>
  ///<returns>Two-dimensional array copy of matrix elements.</returns>
   public double[][] ArrayCopy () {
      double[][] C = new double[m][];
      for (int i = 0; i < m; i++) {
          C[i] = new double[n]; //Added by KJ
         for (int j = 0; j < n; j++) {
            C[i][j] = A[i][j];
         }
      }
      return C;
   }

 

   ///<summary>Make a one-dimensional column packed copy of the internal array.</summary>
   ///<returns>Matrix elements packed in a one-dimensional array by columns.</returns>
   public double[] ColumnPackedCopy () {
      double[] vals = new double[m*n];
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            vals[i+j*m] = A[i][j];
         }
      }
      return vals;
   }


   ///<summary>Make a one-dimensional row packed copy of the internal array.</summary>
   ///<returns>Matrix elements packed in a one-dimensional array by rows.</returns>
   public double[] RowPackedCopy () {
      double[] vals = new double[m*n];
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            vals[i*n+j] = A[i][j];
         }
      }
      return vals;
   }

 
   ///<summary>Get row dimension.</summary>
   ///<returns>m, the number of rows.</returns>
   public int RowDimension   {
       get{return m;}
   }

 
    ///<summary>Get column dimension.</summary>
    ///<returns>n, the number of columns.</returns>
   public int ColumnDimension   {
       get { return n; }
   }

 
///<summary>Get a single element</summary>
///<param name="i" >Row index. </param>
///<param name="j"> Column index.</param>
///<returns> A(i,j)</returns>
   public double Get (int i, int j) {
      return A[i][j];
   }

/// <summary>
/// Creates a matrix that is a copy of the Column specified by the index.
/// </summary>
/// <param name="col">The Column index</param>
/// <returns></returns>
   public Matrix GetColumn(int col)
   {
       Matrix X = GetMatrix(0, m - 1, col, col);
       return X;

   }

///<summary>Get a submatrix</summary>
///<param name="i0">Initial row index</param>
///<param name="i1">Final row index</param>
///<param name="j0">Initial column index</param>
///<param name="j1">Final column index</param>
///<returns>A(i0:i1,j0:j1)</returns>
   public Matrix GetMatrix (int i0, int i1, int j0, int j1) {
      Matrix X = new Matrix(i1-i0+1,j1-j0+1);
      double[][] B = X.Array;
      try {
         for (int i = i0; i <= i1; i++) {
            for (int j = j0; j <= j1; j++) {
               B[i-i0][j-j0] = A[i][j];
            }
         }
      } catch(IndexOutOfRangeException) {
         throw new IndexOutOfRangeException("Submatrix indices");
      }
      return X;
   }

 
   ///<summary>Get a submatrix.</summary>
   ///<param name="r">Array of row indices.</param>
   ///<param name="c">Array of column indices.</param>
   ///<returns>A(r(:),c(:))</returns>
   public Matrix GetMatrix (int[] r, int[] c) {
      Matrix X = new Matrix(r.Length,c.Length);
      double[][] B = X.Array;
      try {
         for (int i = 0; i < r.Length; i++) {
            for (int j = 0; j < c.Length; j++) {
               B[i][j] = A[r[i]][c[j]];
            }
         }
      } catch(IndexOutOfRangeException) {
         throw new IndexOutOfRangeException("Submatrix indices");
      }
      return X;
   }


   ///<summary>Get a submatrix</summary>
   ///<param name="i0">Initial row index</param>
   ///<param name="i1">Final row index</param>
   ///<param name="c">Array of column indices</param>
   ///<returns>A(i0:i1,c(:))</returns>
   public Matrix GetMatrix (int i0, int i1, int[] c) {
      Matrix X = new Matrix(i1-i0+1,c.Length);
      double[][] B = X.Array;
      try {
         for (int i = i0; i <= i1; i++) {
            for (int j = 0; j < c.Length; j++) {
               B[i-i0][j] = A[i][c[j]];
            }
         }
      } catch(IndexOutOfRangeException) {
         throw new IndexOutOfRangeException("Submatrix indices");
      }
      return X;
   }



   ///<summary>Get a submatrix</summary>
   ///<param name="r">Array of row indices</param>
   ///<param name="j0">Initial column index</param>
   ///<param name="j1">Final column index</param>
   ///<returns>A(r(:),j0:j1)</returns>
   public Matrix GetMatrix (int[] r, int j0, int j1) {
      Matrix X = new Matrix(r.Length,j1-j0+1);
      double[][] B = X.Array;
      try {
         for (int i = 0; i < r.Length; i++) {
            for (int j = j0; j <= j1; j++) {
               B[i][j-j0] = A[r[i]][j];
            }
         }
      } catch(System.IndexOutOfRangeException) {
         throw new System.IndexOutOfRangeException("Submatrix indices");
      }
      return X;
   }


   ///<summary>Set a single element.</summary>
   ///<param name="i">Row index.</param>
   ///<param name="j">Column index.</param>
   ///<param name="s">A(i,j).</param>
   public void Set (int i, int j, double s) {
      A[i][j] = s;
   }

   /** Set a submatrix.
   @param i0   Initial row index
   @param i1   Final row index
   @param j0   Initial column index
   @param j1   Final column index
   @param X    A(i0:i1,j0:j1)
   @exception  System.IndexOutOfRangeException Submatrix indices
   */

   public void SetMatrix (int i0, int i1, int j0, int j1, Matrix X) {
      try {
         for (int i = i0; i <= i1; i++) {
            for (int j = j0; j <= j1; j++) {
               A[i][j] = X.Get(i-i0,j-j0);
            }
         }
      } catch(IndexOutOfRangeException) {
         throw new IndexOutOfRangeException("Submatrix indices");
      }
   }

   ///<summary>Set a submatrix.</summary>
   ///<param name="r">Array of row indices.</param>
   ///<param name="c">Array of column indices.</param>
   ///<param name="X">A(r(:),c(:))</param>
   public void SetMatrix (int[] r, int[] c, Matrix X) {
      try {
         for (int i = 0; i < r.Length; i++) {
            for (int j = 0; j < c.Length; j++) {
               A[r[i]][c[j]] = X.Get(i,j);
            }
         }
      } catch(IndexOutOfRangeException) {
         throw new IndexOutOfRangeException("Submatrix indices");
      }
   }


   ///<summary>Set a submatrix</summary>
   ///<param name="r">Array of row indices.</param>
   ///<param name="j0">Initial column index</param>
   ///<param name="j1">Final column index</param>
   ///<param name="X">A(r(:),j0:j1)</param>
   public void SetMatrix (int[] r, int j0, int j1, Matrix X) {
      try {
         for (int i = 0; i < r.Length; i++) {
            for (int j = j0; j <= j1; j++) {
               A[r[i]][j] = X.Get(i,j-j0);
            }
         }
      } catch(IndexOutOfRangeException) {
         throw new IndexOutOfRangeException("Submatrix indices");
      }
   }

 
   ///<summary>Set a submatrix</summary>
   ///<param name="i0">Initial row index</param>
   ///<param name="i1">Final row index</param>
   ///<param name="c">Array of column indices</param>
   ///<param name="X">A(i0:i1,c(:))</param>
   public void SetMatrix (int i0, int i1, int[] c, Matrix X) {
      try {
         for (int i = i0; i <= i1; i++) {
            for (int j = 0; j < c.Length; j++) {
               A[i][c[j]] = X.Get(i-i0,j);
            }
         }
      } catch(IndexOutOfRangeException) {
         throw new IndexOutOfRangeException("Submatrix indices");
      }
   }



   ///<summary>Matrix transpose</summary>
   ///<returns>A'</returns>
   public Matrix Transpose () {
      Matrix X = new Matrix(n,m);
      double[][] C = X.Array;
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            C[j][i] = A[i][j];
         }
      }
      return X;
   }

 
///<summary>One norm</summary>
///<returns>maximum column sum</returns>
   public double Norm1 () {
      double f = 0;
      for (int j = 0; j < n; j++) {
         double s = 0;
         for (int i = 0; i < m; i++) {
            s += Math.Abs(A[i][j]);
         }
         f = Math.Max(f,s);
      }
      return f;
   }

 

  ///<summary>Two norm</summary>
  ///<returns>maximum singular value.</returns>
   public double Norm2 () {
      return (new SingularValueDecomposition(this).Norm2());
   }


 ///<summary>Infinity norm</summary>
  ///<returns> maximum row sum.</returns>
   public double NormInf () {
      double f = 0;
      for (int i = 0; i < m; i++) {
         double s = 0;
         for (int j = 0; j < n; j++) {
            s += Math.Abs(A[i][j]);
         }
         f = Math.Max(f,s);
      }
      return f;
   }


///<summary>Frobenius norm</summary>
///<returns>sqrt of sum of squares of all elements.</returns>
   public double NormF () {
      double f = 0;
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            f = Maths.Hypot(f,A[i][j]);
         }
      }
      return f;
   }

 
///<sumary>Unary minus</sumary>
///<returns>-A</returns>
   public Matrix Uminus () {
      Matrix X = new Matrix(m,n);
      double[][] C = X.Array;
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            C[i][j] = -A[i][j];
         }
      }
      return X;
   }
 
    ///<summary>C = A + B</summary>  
    ///<param name="B">another matrix</param>
    ///<returns>A + B</returns>
   public Matrix Plus (Matrix B) {
      CheckMatrixDimensions(B);
      Matrix X = new Matrix(m,n);
      double[][] C = X.Array;
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            C[i][j] = A[i][j] + B.A[i][j];
         }
      }
      return X;
   }

 
///<summary>A = A + B</summary>
///<param name="B">another matrix</param>
///<returns> A + B</returns>
   public Matrix PlusEquals (Matrix B) {
      CheckMatrixDimensions(B);
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            A[i][j] = A[i][j] + B.A[i][j];
         }
      }
      return this;
   }

 

/// <summary>C = A - B</summary>
/// <param name="B">another matrix</param>
/// <returns>A - B</returns>
   public Matrix Minus (Matrix B) {
      CheckMatrixDimensions(B);
      Matrix X = new Matrix(m,n);
      double[][] C = X.Array;
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            C[i][j] = A[i][j] - B.A[i][j];
         }
      }
      return X;
   }



   ///<summary>A = A - B</summary>
   ///<param name="B">another matrix</param>
   ///<returns> A - B</returns>
   public Matrix MinusEquals (Matrix B) {
      CheckMatrixDimensions(B);
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            A[i][j] = A[i][j] - B.A[i][j];
         }
      }
      return this;
   }

  

 ///<summary>Element-by-element multiplication, C = A.*B</summary>
///<param name="B">another matrix</param>
///<returns>A.*B</returns>
   public Matrix ArrayTimes (Matrix B) {
      CheckMatrixDimensions(B);
      Matrix X = new Matrix(m,n);
      double[][] C = X.Array;
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            C[i][j] = A[i][j] * B.A[i][j];
         }
      }
      return X;
   }

 

   ///<summary>Element-by-element multiplication in place, A = A.*B</summary>
   ///<param name="B">another matrix</param>
   ///<returns>A.*B</returns>
   public Matrix ArrayTimesEquals (Matrix B) {
      CheckMatrixDimensions(B);
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            A[i][j] = A[i][j] * B.A[i][j];
         }
      }
      return this;
   }


   ///<summary>Element-by-element right division, C = A./B</summary>
   ///<param name="B">another matrix</param>
   ///<returns>A./B</returns>
   public Matrix ArrayRightDivide (Matrix B) {
      CheckMatrixDimensions(B);
      Matrix X = new Matrix(m,n);
      double[][] C = X.Array;
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            C[i][j] = A[i][j] / B.A[i][j];
         }
      }
      return X;
   }

  

   ///<summary>Element-by-element right division in place, A = A./B</summary>
   ///<param name="B">another matrix</param>
   ///<returns>A./B</returns>
   public Matrix ArrayRightDivideEquals (Matrix B) {
      CheckMatrixDimensions(B);
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            A[i][j] = A[i][j] / B.A[i][j];
         }
      }
      return this;
   }


   ///<summary>Element-by-element left division, C = A.\B</summary>
   ///<param name="B">another matrix</param>
   ///<returns>A.\B</returns>
   public Matrix ArrayLeftDivide (Matrix B) {
      CheckMatrixDimensions(B);
      Matrix X = new Matrix(m,n);
      double[][] C = X.Array;
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            C[i][j] = B.A[i][j] / A[i][j];
         }
      }
      return X;
   }

 
   ///<summary>Element-by-element left division in place, A = A.\B</summary>
   ///<param name="B">another matrix</param>
   ///<returns>A.\B</returns>
   public Matrix ArrayLeftDivideEquals (Matrix B) {
      CheckMatrixDimensions(B);
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            A[i][j] = B.A[i][j] / A[i][j];
         }
      }
      return this;
   }


   ///<summary>Multiply a matrix by a scalar, C = s*A</summary>
   ///<param name="s">scalar</param>
   ///<returns>s*A</returns>
   public Matrix Times (double s) {
      Matrix X = new Matrix(m,n);
      double[][] C = X.Array;
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            C[i][j] = s*A[i][j];
         }
      }
      return X;
   }

 
   ///<summary>Multiply a matrix by a scalar in place, A = s*A</summary>
   ///<param name="s">scalar</param>
   ///<returns>replace A by s*A</returns>
   public Matrix TimesEquals (double s) {
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            A[i][j] = s*A[i][j];
         }
      }
      return this;
   }
 
        ///<summary> Linear algebraic matrix multiplication, A * B</summary>
        ///<param name="B">another matrix</param>
        ///<returns>Matrix product, A * B</returns>
        ///<remarks>Matrix inner dimensions must agree.</remarks>
   public Matrix Times (Matrix B) {
      if (B.m != n) {
         throw new System.ArgumentException("Matrix inner dimensions must agree.");
      }
      Matrix X = new Matrix(m,B.n);
      double[][] C = X.Array;
      double[] Bcolj = new double[n];
      for (int j = 0; j < B.n; j++) {
         for (int k = 0; k < n; k++) {
            Bcolj[k] = B.A[k][j];
         }
         for (int i = 0; i < m; i++) {
            double[] Arowi = A[i];
            double s = 0;
            for (int k = 0; k < n; k++) {
               s += Arowi[k]*Bcolj[k];
            }
            C[i][j] = s;
         }
      }
      return X;
   }

  
   ///<summary>LU Decomposition</summary>
   ///<returns>LUDecomposition</returns>
   public LUDecomposition Lu () {
      return new LUDecomposition(this);
   }


   ///<summary>QR Decomposition</summary>
   ///<returns>QRDecomposition</returns>
   public QRDecomposition Qr () {
      return new QRDecomposition(this);
   }

 
   ///<summary>Cholesky Decomposition</summary>
   ///<returns>CholeskyDecomposition</returns>
   public CholeskyDecomposition Chol () {
      return new CholeskyDecomposition(this);
   }

 
   ///<summary>Singular Value Decomposition</summary>
   ///<returns>SingularValueDecomposition</returns>
   public SingularValueDecomposition Svd () {
      return new SingularValueDecomposition(this);
   }


   ///<summary>Eigenvalue Decomposition</summary>
   ///<returns>EigenvalueDecomposition</returns>
   public EigenvalueDecomposition Eig () {
      return new EigenvalueDecomposition(this);
   }
 

///<summary> Solve A*X = B</summary>
///<param name="B"> right hand side</param>
///<returns> solution if A is square, least squares solution otherwise</returns>
   public Matrix Solve (Matrix B) {
      return (m == n ? (new LUDecomposition(this)).solve(B) :
                       (new QRDecomposition(this)).Solve(B));
   }

 
   ///<summary>Solve X*A = B, which is also A'*X' = B'</summary>
   ///<param name="B"> right hand side</param>
   ///<returns>solution if A is square, least squares solution otherwise.</returns>
   public Matrix SolveTranspose (Matrix B) {
      return Transpose().Solve(B.Transpose());
   }
 

   ///<summary>Matrix inverse or pseudoinverse</summary>
   ///<returns>inverse(A) if A is square, pseudoinverse otherwise.</returns>
   public Matrix Inverse () {
      return Solve(Identity(m,m));
   }

///<summary>Matrix determinant</summary>
///<returns>determinant</returns>
   public double Det () {
      return new LUDecomposition(this).det();
   }

  
   ///<summary>Matrix rank</summary>
   ///<returns>effective numerical rank, obtained from SVD.</returns>
   public int Rank () {
      return new SingularValueDecomposition(this).Rank();
   }

  

   ///<summary>Matrix condition (2 norm)</summary>
   ///<returns>ratio of largest to smallest singular value.</returns>
   public double Cond () {
      return new SingularValueDecomposition(this).Cond();
   }

 
   ///<summary>Matrix trace</summary>
   ///<returns>sum of the diagonal elements.</returns>
   public double Trace () {
      double t = 0;
      for (int i = 0; i < Math.Min(m,n); i++) {
         t += A[i][i];
      }
      return t;
   }
      
    
 


 
   ///<summary>Generate identity matrix</summary>
   ///<param name="m">Number of rows.</param>
   ///<param name="n">Number of colums.</param>
   ///<returns>An m-by-n matrix with ones on the diagonal and zeros elsewhere.</returns>
   public static Matrix Identity (int m, int n) {
      Matrix A = new Matrix(m,n);
      double[][] X = A.Array;
      for (int i = 0; i < m; i++) {
         for (int j = 0; j < n; j++) {
            X[i][j] = (i == j ? 1.0 : 0.0);
         }
      }
      return A;
   }


/// <summary>
/// Returns matrix as string in MatLab format
/// </summary>
/// <returns>string representation of Matrix</returns>
   public override string ToString()
   {
       return ToMatLabString();
   }

/// <summary>
/// Parses strig to 
/// </summary>
/// <param name="str"></param>
/// <returns></returns>
public static Matrix Parse(string str)
   {
       string working = str.Trim();

       if (str.StartsWith("["))
       {
           return ParseMatLab(str);
       }
       else if (str.StartsWith("{"))
       {
           return ParseMathematica(str);
       }
       else
       {
              System.IO.StringReader reader = new System.IO.StringReader(str);
               Matrix mat = Load(reader);
               reader.Close();
               reader.Dispose();
               return mat;
       }
   }

/// <summary>
/// Converts matrix to a string
/// </summary>
/// <param name="matrixFrontCap">placed at the beginning of the string</param>
/// <param name="rowFrontCap">placed at the beginning of each row</param>
/// <param name="rowDelimiter">delimiter for rows</param>
/// <param name="columnDelimiter">delimiter for Columns</param>
/// <param name="rowEndCap">placed at the end of each row</param>
/// <param name="matrixEndCap">placed at the beginning of the string</param>
/// <returns></returns>
public string ToString(string matrixFrontCap, string rowFrontCap, string rowDelimiter, string columnDelimiter, string rowEndCap, string matrixEndCap)
{
    StringBuilder sb = new StringBuilder();  // start on new line.
    sb.Append(matrixFrontCap);
    for (int i = 0; i < m; i++)
    {
        sb.Append(rowFrontCap);
        for (int j = 0; j < n; j++)
        {
             
            sb.Append(A[i][j].ToString("R")); //Round-trip is necessary
            sb.Append((j < n - 1) ? columnDelimiter : "");
        }
        sb.Append(rowEndCap);
        sb.Append((i < m - 1) ? rowDelimiter : "");
    }
    sb.Append(matrixEndCap);
    return sb.ToString();
}

/// <summary>
/// parses a string to produce a matrix
/// </summary>
/// <param name="str">The string to parse</param>
/// <param name="matrixFrontCap">placed at the beginning of the string</param>
/// <param name="rowFrontCap">placed at the beginning of each row</param>
/// <param name="rowDelimiter">delimiter for rows</param>
/// <param name="columnDelimiter">delimiter for Columns</param>
/// <param name="rowEndCap">placed at the end of each row</param>
/// <param name="matrixEndCap">placed at the beginning of the string</param>
/// <returns></returns>
public static Matrix Parse(string str, string matrixFrontCap, string rowFrontCap, string rowDelimiter, string columnDelimiter, string rowEndCap, string matrixEndCap)
{
    if (! str.StartsWith(matrixFrontCap )){
        throw new Exception("string does not begin with proper value: " + matrixFrontCap + " was expected, but " + str.Substring(0, matrixFrontCap.Length) + " was found.");
    }

    if (!str.StartsWith(matrixFrontCap))
    {
        throw new Exception("string does not end with proper value: " + matrixEndCap + " was expected, but " + str.Substring(str.Length - matrixEndCap.Length, matrixEndCap.Length) + " was found.");
    }

    string working = str.Substring(matrixFrontCap.Length, str.Length - matrixFrontCap.Length - matrixEndCap.Length);
    string[] rDelim = { rowEndCap + rowDelimiter + rowFrontCap};
    string[] rows =    working.Split(rDelim ,  StringSplitOptions.RemoveEmptyEntries);
    if (rows.Length == 0){throw new Exception("No rows present");}

    rows[0] = rows[0].Substring(rowFrontCap.Length);
    rows[rows.Length -1] = rows[rows.Length -1].Substring(0,rows[rows.Length -1].Length  - rowEndCap.Length);

    double[][] matrixArray = new double[rows.Length][];
    int columnCount = 0;
    for (int iRow = 0; iRow < rows.Length; iRow++)
    {
        string[] cols = rows[iRow].Split(new string[] { columnDelimiter }, StringSplitOptions.RemoveEmptyEntries);
        if (columnCount != 0)
        {
            if (columnCount != cols.Length)
            {
                throw new Exception("Rows are not of consistant lenght");
            }
        }
        else { columnCount = cols.Length; }

        double[] row = new double[columnCount];
        for (int iCol = 0; iCol < columnCount; iCol++)
        {
            row[iCol] = Double.Parse(cols[iCol]);
        }
        matrixArray[iRow] = row;
    }
    return new Matrix(matrixArray);

}


/// <summary>
/// Produces a visually pleasing representation of the matrix to a specified number of decimal places so that the decimal points are lined up.
/// </summary>
/// <param name="decimalPlaces">Number of decimal places</param>
/// <returns>Text representation of the matrix</returns>
public string DisplayText(int decimalPlaces)
{
    double maxVal = double.MinValue;
    for (int iRow = 0; iRow < RowDimension; iRow++)
    {
        for (int iCol = 0; iCol <   ColumnDimension ; iCol++)
        {
            if (A[iRow][iCol] > maxVal)
            {
                maxVal = A[iRow][iCol];
            }
        }
    }
   int width =  Math.Truncate(maxVal).ToString().Length   ;
   if (decimalPlaces > 0)
   {
       width += decimalPlaces + 1;
   }

   StringBuilder sb = new StringBuilder();
   for (int iRow = 0; iRow < RowDimension; iRow++)
   {
       for (int iCol = 0; iCol < ColumnDimension; iCol++)
       {
           string s = A[iRow][iCol].ToString("N" + decimalPlaces);
           int padding =  width - s.Length ;
           if (iCol > 0)
           {
               padding++;
           }
           for (int k = 0; k < padding; k++)
           {
               sb.Append(" ");
           }
           sb.Append(s);
       }
       if (iRow < RowDimension - 1)
       {
           sb.Append("\n");
       }
   }
   return sb.ToString();

}



/// <summary>
/// Produces the string constructor of the underlying jagged array for the matrix.
/// Very useful for putting into source code
/// </summary>
/// <returns>String constructor of the underlying jagged array in C#</returns>
public string ToArrayString()
{
   // Output should look something like this.
   //  double[][] avals = new double[3][] { new double[4] { 1.0, 4.0, 7.0, 10.0 }, new double[4] { 2.0, 5.0, 8.0, 11.0 }, new double[4] { 3.0, 6.0, 9.0, 12.0 } };

    string matrixFrontCap = "new double[" + RowDimension + "][] { ";
    string rowFrontCap = "new double[" + ColumnDimension + "] { ";
    return ToString(matrixFrontCap, rowFrontCap, ", ", ", ", " }", " }");
}


/// <summary>
/// Converts matrix to a string representation that can be cut and pasted into mathematica;
/// </summary>
/// <returns>Mathematic formatted array</returns>
public string ToMathematicaString()
{
    // Output should look something like this.
    // {{0.894427190999916, -0.447213595499958}, {0.447213595499958,   0.894427190999916}}
    return ToString("{", "{", ", ", ", ", "}", "}");
}

public static Matrix ParseMathematica(string str)
{
    return Parse(str, "{", "{", ", ", ", ", "}", "}");

}

/// <summary>
/// Converts matrix to a string representation that can be cut and pasted into matlab;
/// </summary>
/// <returns>Mathematic formatted array</returns>
public string ToMatLabString()
{   
    return ToString("[", "", ";", " ", "", "]");
     
}


        ///<summary>
        ///</summary>
        ///<param name="str">String in matlab format</param>
        ///<returns></returns>
        public static Matrix ParseMatLab(string str)
        {

            return Parse(str, "[", "", ";", " ", "", "]");
        }

    
        public static  Matrix Load(string path){
            String file = File.ReadAllText(path);
            return Parse(file);
        }

        /// <summary>
        /// Parses multiline text input  from a textreader into a Matrix where each line 
        /// corresponsd to a row and the items on the line are space deliminted
        /// </summary>
        /// <param name="reader">The TextReader</param>
        /// <returns>The Matrix</returns>
       public static Matrix Load( TextReader reader ){

             List<double[]> rows = new List<double[]>();
       Regex rx = new Regex(@"\s+");
       while (reader.Peek() != -1)
       {
           string line = reader.ReadLine().Trim();

           if (line != ""){
               string[] rowStrs = rx.Split(line);
               double[] matrixRow = new double[rowStrs.Length];
               for  (int i=0; i < rowStrs.Length;i++) 
               {
                   double val =0;
                   if (double.TryParse(rowStrs[i],out val)){

                       matrixRow[i] = val;
                   }else{

                       throw new ArgumentException("Invalid string");
                   }
               }
               rows.Add(matrixRow);
           }
       }

       if (rows.Count > 1)
       {
           //Check that all rows have the same length
           int rowSize = rows[0].Length; 
           for (int i = 1; i < rows.Count;i++ )
           {
               if (rows[i].Length != rowSize)
               {
                   throw new ArgumentException("Rows of inconsistant length");
               }
           }
           double[][] matrixArray = new double[rows.Count][];
           rows.CopyTo(matrixArray);
           Matrix mat = new Matrix(matrixArray);
           return mat;
       }
       else if (rows.Count == 1)
       {
           double[][] matrixArray = new double[rows.Count][];
           rows.CopyTo(matrixArray);
           Matrix mat = new Matrix(matrixArray);
           return mat;
       }
       else
       {

           return null;
       }
        }



       public DataTable ToDataTable()
       {
           DataTable dt = new DataTable();
           for (int i = 0; i < ColumnDimension; i++)
           {
               dt.Columns.Add("col" + i, typeof(double));
           }
           foreach (double[] row in A)
           {
               DataRow dr = dt.NewRow();

               for (int iCol = 0;   iCol < ColumnDimension ;iCol++ )
               {
                   dr[iCol] = (double)row[iCol];
               }
               dt.Rows.Add(dr);
           }
           return dt;
       }

       public static Matrix FromDataTable(DataTable dt)
       {
           double[][] X = new double[dt.Rows.Count][];
           for (int iRow = 0; iRow < dt.Rows.Count; iRow++)
           {
               double[] row = new double[dt.Columns.Count];
               for (int iCol = 0; iCol < dt.Columns.Count; iCol++)
               {
                   row[iCol] = (double)dt.Rows[iRow][iCol];

               }

               X[iRow] = row;
           }

           return new Matrix(X);

       }


/* ------------------------
   Private Methods
 * ------------------------ */

   /** Check if size(A) == size(B) **/

   private void CheckMatrixDimensions (Matrix B) {
      if (B.m != m || B.n != n) {
         throw new System.ArgumentException("Matrix dimensions must agree.");
      }
   }




   #region IEnumerable<double[]> Members

   public IEnumerator<double[]> GetEnumerator()
   {
       throw new NotImplementedException();
   }

   #endregion

   #region IEnumerable Members

   System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
   {
       foreach (double[] row in A)
       {
           yield return row;

       }
   }

   #endregion
    }
}