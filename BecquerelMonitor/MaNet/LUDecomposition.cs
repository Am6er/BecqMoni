﻿using System;


namespace MaNet
{
    

  
/// <summary>
/// LU Decomposition of a Matrix
/// 
///For an m-by-n matrix A with m >= n, the LU decomposition is an m-by-n
///unit lower triangular matrix L, an n-by-n upper triangular matrix U,
///and a permutation vector piv of length m so that A(piv,:) = L*U.
///If m  &lt; n, then L is m-by-m and U is m-by-n.
///
/// The LU decompostion with pivoting always exists, even if the matrix is
/// singular, so the constructor will never fail.  The primary use of the
///LU decomposition is in the solution of square systems of simultaneous
///linear equations.  This will fail if isNonsingular() returns false.
/// </summary>
    [Serializable]
public class LUDecomposition 
        {
 /** 
   <P>
   For an m-by-n matrix A with m >= n, the LU decomposition is an m-by-n
   unit lower triangular matrix L, an n-by-n upper triangular matrix U,
   and a permutation vector piv of length m so that A(piv,:) = L*U.
   If m < n, then L is m-by-m and U is m-by-n.
   <P>
   The LU decompostion with pivoting always exists, even if the matrix is
   singular, so the constructor will never fail.  The primary use of the
   LU decomposition is in the solution of square systems of simultaneous
   linear equations.  This will fail if isNonsingular() returns false.
   */

    /** IMPORTANT WARNING
   
     */
/* ------------------------
   Class variables
 * ------------------------ */

   /** Array for internal storage of decomposition.
   @serial internal array storage.
   */
   private double[][] LU;

   /** Row and column dimensions, and pivot sign.
   @serial column dimension.
   @serial row dimension.
   @serial pivot sign.
   */
   private int m, n, pivsign; 

   /** Internal storage of pivot vector.
   @serial pivot vector.
   */
   private int[] piv;

/* ------------------------
   Constructor
 * ------------------------ */

   /** LU Decomposition
   @param  A   Rectangular matrix
   @return     Structure to access L, U and piv.
   */

   public LUDecomposition (Matrix A) {

   // Use a "left-looking", dot-product, Crout/Doolittle algorithm.

      LU = A.ArrayCopy();
      m = A.RowDimension;
      n = A.ColumnDimension;
      piv = new int[m];
      for (int i = 0; i < m; i++) {
         piv[i] = i;
      }
      pivsign = 1;
      double[] LUrowi;
      double[] LUcolj = new double[m];

      // Outer loop.

      for (int j = 0; j < n; j++) {

         // Make a copy of the j-th column to localize references.

         for (int i = 0; i < m; i++) {
            LUcolj[i] = LU[i][j];
         }

         // Apply previous transformations.

         for (int i = 0; i < m; i++) {
            LUrowi = LU[i];

            // Most of the time is spent in the following dot product.

            int kmax = Math.Min(i,j);
            double s = 0.0;
            for (int k = 0; k < kmax; k++) {
               s += LUrowi[k]*LUcolj[k];
            }

            LUrowi[j] = LUcolj[i] -= s;
         }
   
         // Find pivot and exchange if necessary.

         int p = j;
         for (int i = j+1; i < m; i++) {
            if (Math.Abs(LUcolj[i]) > Math.Abs(LUcolj[p])) {
               p = i;
            }
         }
         if (p != j) {
            for (int k = 0; k < n; k++) {
               double t = LU[p][k]; LU[p][k] = LU[j][k]; LU[j][k] = t;
            }
            int k2 = piv[p]; piv[p] = piv[j]; piv[j] = k2;
            pivsign = -pivsign;
         }

         // Compute multipliers.
         
         if (j < m && LU[j][j] != 0.0) {
            for (int i = j+1; i < m; i++) {
               LU[i][j] /= LU[j][j];
            }
         }
      }
   }

/* ------------------------
   Temporary, experimental code.
   ------------------------ *\

   \** LU Decomposition, computed by Gaussian elimination.
   <P>
   This constructor computes L and U with the "daxpy"-based elimination
   algorithm used in LINPACK and MATLAB.  In Java, we suspect the dot-product,
   Crout algorithm will be faster.  We have temporarily included this
   constructor until timing experiments confirm this suspicion.
   <P>
   @param  A             Rectangular matrix
   @param  linpackflag   Use Gaussian elimination.  Actual value ignored.
   @return               Structure to access L, U and piv.
   *\

   public LUDecomposition (Matrix A, int linpackflag) {
      // Initialize.
      LU = A.getArrayCopy();
      m = A.RowDimension;
      n = A.ColumnDimension;
      piv = new int[m];
      for (int i = 0; i < m; i++) {
         piv[i] = i;
      }
      pivsign = 1;
      // Main loop.
      for (int k = 0; k < n; k++) {
         // Find pivot.
         int p = k;
         for (int i = k+1; i < m; i++) {
            if (Math.Abs(LU[i][k]) > Math.Abs(LU[p][k])) {
               p = i;
            }
         }
         // Exchange if necessary.
         if (p != k) {
            for (int j = 0; j < n; j++) {
               double t = LU[p][j]; LU[p][j] = LU[k][j]; LU[k][j] = t;
            }
            int t = piv[p]; piv[p] = piv[k]; piv[k] = t;
            pivsign = -pivsign;
         }
         // Compute multipliers and eliminate k-th column.
         if (LU[k][k] != 0.0) {
            for (int i = k+1; i < m; i++) {
               LU[i][k] /= LU[k][k];
               for (int j = k+1; j < n; j++) {
                  LU[i][j] -= LU[i][k]*LU[k][j];
               }
            }
         }
      }
   }

\* ------------------------
   End of temporary code.
 * ------------------------ */

/* ------------------------
   Public Methods
 * ------------------------ */

   /** Is the matrix nonsingular?
   @return     true if U, and hence A, is nonsingular.
   */

   public bool IsNonsingular () {
       if (m != n) return false;// Added to deal with fat and skinny matrices
      for (int j = 0; j < n; j++) {
         if (LU[j][j] == 0)
            return false;
      }
      return true;
   }

   /** Return lower triangular factor
   @return     L
   */

   public Matrix GetL () {
       if (m >= n) //For correct Dimensions with square or skinny matrices
       {
           Matrix X = new Matrix(m, n);
           double[][] L = X.Array;
           for (int i = 0; i < m; i++)
           {
               for (int j = 0; j < n; j++)
               {
                   if (i > j)
                   {
                       L[i][j] = LU[i][j];
                   }
                   else if (i == j)
                   {
                       L[i][j] = 1.0;
                   }
                   else
                   {
                       L[i][j] = 0.0;
                   }
               }
           }
           return X;
       }
       else// For when n > m, Fat matrix
       {
           Matrix X = new Matrix(m, m);
           double[][] L = X.Array;
           for (int i = 0; i < m; i++)
           {
               for (int j = 0; j < m; j++)
               {
                   if (i > j)
                   {
                       L[i][j] = LU[i][j];
                   }
                   else if (i == j)
                   {
                       L[i][j] = 1.0;
                   }
                   else
                   {
                       L[i][j] = 0.0;
                   }
               }
           }
           return X;

       }
   }

   /** Return upper triangular factor
   @return     U
   */

   public Matrix GetU () {
       //For correct Dimensions with Fat matrices
       if (m >= n)
       {
           Matrix X = new Matrix(n, n);
           double[][] U = X.Array;
           for (int i = 0; i < n; i++)
           {
               for (int j = 0; j < n; j++)
               {
                   if (i <= j)
                   {
                       U[i][j] = LU[i][j];
                   }
                   else
                   {
                       U[i][j] = 0.0;
                   }
               }
           }
           return X;
       }
       else // this case added for when n > m
       {
           Matrix X = new Matrix(m,n );
           double[][] U = X.Array;
           for (int i = 0; i < m; i++)
           {
               for (int j = 0; j <n ; j++)
               {
                   if (i <= j)
                   {
                       U[i][j] = LU[i][j];
                   }
                   else
                   {
                       U[i][j] = 0.0;
                   }
               }
           }
           return X;

       }
   }

   /** Return pivot permutation vector
   @return     piv
   */

   public int[] getPivot () {
      int[] p = new int[m];
      for (int i = 0; i < m; i++) {
         p[i] = piv[i];
      }
      return p;
   }

    /// <summary>
    /// Returns the Pivot Permutation matrix such that L*U = P*A
    /// </summary>
   /// <returns> Pivot Permutation matrix</returns>
   public Matrix GetP()
   {

       int[] pivots = getPivot();
       Matrix X = new Matrix(pivots.Length);
       for (int i = 0; i < pivots.Length; i++)
       {
           X.Array[i][pivots[i]] = 1;
       }
       return X;
   }

   /** Return pivot permutation vector as a one-dimensional double array
   @return     (double) piv
   */

   public double[] getDoublePivot () {
      double[] vals = new double[m];
      for (int i = 0; i < m; i++) {
         vals[i] = (double) piv[i];
      }
      return vals;
   }

   /** Determinant
   @return     det(A)
   @exception  System.ArgumentException  Matrix must be square
   */

   public double det () {
      if (m != n) {
         throw new System.ArgumentException("Matrix must be square.");
      }
      double d = (double) pivsign;
      for (int j = 0; j < n; j++) {
         d *= LU[j][j];
      }
      return d;
   }

   /** Solve A*X = B
   @param  B   A Matrix with as many rows as A and any number of columns.
   @return     X so that L*U*X = B(piv,:)
   @exception  System.ArgumentException Matrix row dimensions must agree.
   @exception  System.Exception  Matrix is singular.
   */

   public Matrix solve (Matrix B) {
      if (B.RowDimension != m) {
         throw new ArgumentException("Matrix row dimensions must agree.");
      }
      if (!this.IsNonsingular()) {
         throw new Exception("Matrix is singular.");
      }

      // Copy right hand side with pivoting
      int nx = B.ColumnDimension;
      Matrix Xmat = B.GetMatrix(piv,0,nx-1);
      double[][] X = Xmat.Array;

      // Solve L*Y = B(piv,:)
      for (int k = 0; k < n; k++) {
         for (int i = k+1; i < n; i++) {
            for (int j = 0; j < nx; j++) {
               X[i][j] -= X[k][j]*LU[i][k];
            }
         }
      }
      // Solve U*X = Y;
      for (int k = n-1; k >= 0; k--) {
         for (int j = 0; j < nx; j++) {
            X[k][j] /= LU[k][k];
         }
         for (int i = 0; i < k; i++) {
            for (int j = 0; j < nx; j++) {
               X[i][j] -= X[k][j]*LU[i][k];
            }
         }
      }
      return Xmat;
   }
}

}