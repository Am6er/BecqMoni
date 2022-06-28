using System;

namespace MaNet
{
   /// <summary>
   ///Cholesky Decomposition of A Matrix
   /// For a symmetric, positive definite matrix A, the Cholesky decomposition
   ///is an lower triangular matrix L so that A = L*L'.
   /// 
   ///If the matrix is not symmetric or positive definite, the constructor
   ///returns a partial decomposition and sets an internal flag that may
   ///be queried by the isSPD() method.
   /// </summary>
    [Serializable]
public class CholeskyDecomposition 
    {

 /** Cholesky Decomposition  Original Documentation.
   <P>
   For a symmetric, positive definite matrix A, the Cholesky decomposition
   is an lower triangular matrix L so that A = L*L'.
   <P>
   If the matrix is not symmetric or positive definite, the constructor
   returns a partial decomposition and sets an internal flag that may
   be queried by the isSPD() method.
   */



/* ------------------------
   Class variables
 * ------------------------ */

   /** Array for internal storage of decomposition.
   @serial internal array storage.
   */
   private readonly double[][] L;

   /** Row and column dimension (square matrix).
   @serial matrix dimension.
   */
   private readonly int n;

   /** Symmetric and positive definite flag.
   @serial is symmetric and positive definite flag.
   */
   private readonly bool isspd;



  /// <summary>
  /// Cholesky algorithm for symmetric and positive definite matrix
  /// </summary>
  /// <param name="Arg">A Square, symmetric matrix.</param>
   public CholeskyDecomposition (Matrix Arg) {


     // Initialize.
      double[][] A = Arg.Array;
      n = Arg.RowDimension;
      L = new double[n][];
      for (int i = 0; i < n; i++)//Added by kj
      {
          L[i] = new double[Arg.ColumnDimension];
      }

      isspd = (Arg.ColumnDimension == n);
      // Main loop.
      for (int j = 0; j < n; j++) {
         double[] Lrowj = L[j];
         double d = 0.0;
         for (int k = 0; k < j; k++) {
            double[] Lrowk = L[k];
            double s = 0.0;
            for (int i = 0; i < k; i++) {
               s += Lrowk[i]*Lrowj[i];
            }
            Lrowj[k] = s = (A[j][k] - s)/L[k][k];
            d = d + s*s;
            isspd = isspd & (A[k][j] == A[j][k]); 
         }
         d = A[j][j] - d;
         isspd = isspd & (d > 0.0);
         L[j][j] = Math.Sqrt(Math.Max(d,0.0));
         for (int k = j+1; k < n; k++) {
            L[j][k] = 0.0;
         }
      }
   }

/* ------------------------
   Temporary, experimental code.
 * ------------------------ *\

   \** Right Triangular Cholesky Decomposition.
   <P>
   For a symmetric, positive definite matrix A, the Right Cholesky
   decomposition is an upper triangular matrix R so that A = R'*R.
   This constructor computes R with the Fortran inspired column oriented
   algorithm used in LINPACK and MATLAB.  In Java, we suspect a row oriented,
   lower triangular decomposition is faster.  We have temporarily included
   this constructor here until timing experiments confirm this suspicion.
   *\

   \** Array for internal storage of right triangular decomposition. **\
   private transient double[][] R;

   \** Cholesky algorithm for symmetric and positive definite matrix.
   @param  A           Square, symmetric matrix.
   @param  rightflag   Actual value ignored.
   @return             Structure to access R and isspd flag.
   *\

   public CholeskyDecomposition (Matrix Arg, int rightflag) {
      // Initialize.
      double[][] A = Arg.getArray();
      n = Arg.ColumnDimension;
      R = new double[n][n];
      isspd = (Arg.ColumnDimension == n);
      // Main loop.
      for (int j = 0; j < n; j++) {
         double d = 0.0;
         for (int k = 0; k < j; k++) {
            double s = A[k][j];
            for (int i = 0; i < k; i++) {
               s = s - R[i][k]*R[i][j];
            }
            R[k][j] = s = s/R[k][k];
            d = d + s*s;
            isspd = isspd & (A[k][j] == A[j][k]); 
         }
         d = A[j][j] - d;
         isspd = isspd & (d > 0.0);
         R[j][j] = Math.Sqrt(Math.Max(d,0.0));
         for (int k = j+1; k < n; k++) {
            R[k][j] = 0.0;
         }
      }
   }

   \** Return upper triangular factor.
   @return     R
   *\

   public Matrix getR () {
      return new Matrix(R,n,n);
   }

\* ------------------------
   End of temporary code.
 * ------------------------ */

/* ------------------------
   Public Methods
 * ------------------------ */

   ///<summary>Is the matrix symmetric and positive definite?</summary>
   ///<returns> true if A is symmetric and positive definite.</returns>
   public bool IsSPD () {
      return isspd;
   }


///<summary> Return triangular factor</summary>
 ///<returns>L</returns>
   public Matrix getL () {
      return new Matrix(L,n,n);
   }


   ///<summary>Solve A*X = B</summary>
   ///<param name="B">A Matrix with as many rows as A and any number of columns.</param>
   ///<returns>X so that L*L'*X = B</returns>
   ///<exception cref="ArgumentException">System.ArgumentException  Matrix row dimensions must agree.</exception>
   ///<exception cref="Exception">System.Exception  Matrix is not symmetric positive definite.</exception>
   public Matrix Solve (Matrix B) {
      if (B.RowDimension != n) {
         throw new ArgumentException("Matrix row dimensions must agree.");
      }
      if (!isspd) {
         throw new Exception("Matrix is not symmetric positive definite.");
      }

      // Copy right hand side.
      double[][] X = B.ArrayCopy();
      int nx = B.ColumnDimension;

	      // Solve L*Y = B;
	      for (int k = 0; k < n; k++) {
	        for (int j = 0; j < nx; j++) {
	           for (int i = 0; i < k ; i++) {
	               X[k][j] -= X[i][j]*L[k][i];
	           }
	           X[k][j] /= L[k][k];
	        }
	      }
	
	      // Solve L'*X = Y;
	      for (int k = n-1; k >= 0; k--) {
	        for (int j = 0; j < nx; j++) {
	           for (int i = k+1; i < n ; i++) {
	               X[k][j] -= X[i][j]*L[i][k];
	           }
	           X[k][j] /= L[k][k];
	        }
	      }
      
      
      return new Matrix(X,n,nx);
   }
}




}