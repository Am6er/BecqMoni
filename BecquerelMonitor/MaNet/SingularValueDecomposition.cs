﻿using System;

namespace MaNet
{



    /// <summary>
    /// Singular Value Decomposition of a matrix
    /// For an m-by-n matrix A with m >= n, the singular value decomposition is
    ///an m-by-n orthogonal matrix U, an n-by-n diagonal matrix S, and
    ///an n-by-n orthogonal matrix V so that A = U*S*V'.
    ///
    /// The singular values, sigma[k] = S[k][k], are ordered so that
    ///  sigma[0] >= sigma[1] >= ... >= sigma[n-1].
    ///  
    /// The singular value decompostion always exists, so the constructor will
    ///  never fail.  The matrix condition number and the effective numerical
    ///    rank can be computed from this decomposition.
    ///    
    /// There may be some confusion as other conventions are also in use. 
    ///            Plesase see http://mathworld.wolfram.com/SingularValueDecomposition.html 
    ///           * which is part of mathematica's documentation
    ///    
    /// 
    /// </summary>
    [Serializable]
    public class SingularValueDecomposition
    {



        /** Singular Value Decomposition.
        <P>
        For an m-by-n matrix A with m >= n, the singular value decomposition is
        an m-by-n orthogonal matrix U, an n-by-n diagonal matrix S, and
        an n-by-n orthogonal matrix V so that A = U*S*V'.
        <P>
        The singular values, sigma[k] = S[k][k], are ordered so that
        sigma[0] >= sigma[1] >= ... >= sigma[n-1].
        <P>
        The singular value decompostion always exists, so the constructor will
        never fail.  The matrix condition number and the effective numerical
        rank can be computed from this decomposition.

         There may be some confusion as other conventions are also in use. 
         Plesase see http://mathworld.wolfram.com/SingularValueDecomposition.html 
         * which is part of mathematica's documentation
  
         * Note that there are several conflicting notational conventions in use in the literature.
         * Press et al. (1992) define  to be an  matrix,  as , and  as . 
         * However, Mathematica defines  as an ,  as , and  as . In both systems,  
         * and  have orthogonal columns so that 
 
        */
        /* ------------------------
           Class variables
         * ------------------------ */

        /** Arrays for internal storage of U and V.
        @serial internal storage of U.
        @serial internal storage of V.
        */
        private double[][] U, V;

        /** Array for internal storage of singular values.
        @serial internal storage of singular values.
        */
        private double[] s;

        /** Row and column dimensions.
        @serial row dimension.
        @serial column dimension.
        */
        private int m, n;

        /* ------------------------
           Constructor
         * ------------------------ */


        ///<summary>Construct the singular value decomposition</summary>
        ///<param name="Arg">Rectangular matrix</param>
        ///<returns>Structure to access U, S and V.</returns>
        public SingularValueDecomposition(Matrix Arg)
        {

            // Derived from LINPACK code.
            // Initialize.
            double[][] A = Arg.ArrayCopy();
            m = Arg.RowDimension;
            n = Arg.ColumnDimension;

            /* Apparently the failing cases are only a proper subset of (m<n), 
           so let's not throw error.  Correct fix to come later?
            if (m<n) {
            throw new System.ArgumentException("Jama SVD only works for m >= n"); }
            */
            int nu = Math.Min(m, n);
            s = new double[Math.Min(m + 1, n)];
            U = new double[m][];
            for (int i = 0; i < m; i++)//Added by KJ
            {
                U[i] = new double[m];
            }
            V = new double[n][];
            for (int i = 0; i < n; i++)//Added by KJ
            {
                V[i] = new double[n];
            }
            double[] e = new double[n];
            double[] work = new double[m];
            bool wantu = true;
            bool wantv = true;

            // Reduce A to bidiagonal form, storing the diagonal elements
            // in s and the super-diagonal elements in e.

            int nct = Math.Min(m - 1, n);
            int nrt = Math.Max(0, Math.Min(n - 2, m));
            for (int k = 0; k < Math.Max(nct, nrt); k++)
            {
                if (k < nct)
                {

                    // Compute the transformation for the k-th column and
                    // place the k-th diagonal in s[k].
                    // Compute 2-norm of k-th column without under/overflow.
                    s[k] = 0;
                    for (int i = k; i < m; i++)
                    {
                        s[k] = Maths.Hypot(s[k], A[i][k]);
                    }
                    if (s[k] != 0.0)
                    {
                        if (A[k][k] < 0.0)
                        {
                            s[k] = -s[k];
                        }
                        for (int i = k; i < m; i++)
                        {
                            A[i][k] /= s[k];
                        }
                        A[k][k] += 1.0;
                    }
                    s[k] = -s[k];
                }
                for (int j = k + 1; j < n; j++)
                {
                    if ((k < nct) & (s[k] != 0.0))
                    {

                        // Apply the transformation.

                        double t = 0;
                        for (int i = k; i < m; i++)
                        {
                            t += A[i][k] * A[i][j];
                        }
                        t = -t / A[k][k];
                        for (int i = k; i < m; i++)
                        {
                            A[i][j] += t * A[i][k];
                        }
                    }

                    // Place the k-th row of A into e for the
                    // subsequent calculation of the row transformation.

                    e[j] = A[k][j];
                }
                if (wantu & (k < nct))
                {

                    // Place the transformation in U for subsequent back
                    // multiplication.

                    for (int i = k; i < m; i++)
                    {
                        U[i][k] = A[i][k];
                    }
                }
                if (k < nrt)
                {

                    // Compute the k-th row transformation and place the
                    // k-th super-diagonal in e[k].
                    // Compute 2-norm without under/overflow.
                    e[k] = 0;
                    for (int i = k + 1; i < n; i++)
                    {
                        e[k] = Maths.Hypot(e[k], e[i]);
                    }
                    if (e[k] != 0.0)
                    {
                        if (e[k + 1] < 0.0)
                        {
                            e[k] = -e[k];
                        }
                        for (int i = k + 1; i < n; i++)
                        {
                            e[i] /= e[k];
                        }
                        e[k + 1] += 1.0;
                    }
                    e[k] = -e[k];
                    if ((k + 1 < m) & (e[k] != 0.0))
                    {

                        // Apply the transformation.

                        for (int i = k + 1; i < m; i++)
                        {
                            work[i] = 0.0;
                        }
                        for (int j = k + 1; j < n; j++)
                        {
                            for (int i = k + 1; i < m; i++)
                            {
                                work[i] += e[j] * A[i][j];
                            }
                        }
                        for (int j = k + 1; j < n; j++)
                        {
                            double t = -e[j] / e[k + 1];
                            for (int i = k + 1; i < m; i++)
                            {
                                A[i][j] += t * work[i];
                            }
                        }
                    }
                    if (wantv)
                    {

                        // Place the transformation in V for subsequent
                        // back multiplication.

                        for (int i = k + 1; i < n; i++)
                        {
                            V[i][k] = e[i];
                        }
                    }
                }
            }

            // Set up the final bidiagonal matrix or order p.

            int p = Math.Min(n, m + 1);
            if (nct < n)
            {
                s[nct] = A[nct][nct];
            }
            if (m < p)
            {
                s[p - 1] = 0.0;
            }
            if (nrt + 1 < p)
            {
                e[nrt] = A[nrt][p - 1];
            }
            e[p - 1] = 0.0;

            // If required, generate U.

            if (wantu)
            {
                for (int j = nct; j < nu; j++)
                {
                    for (int i = 0; i < m; i++)
                    {
                        U[i][j] = 0.0;
                    }
                    U[j][j] = 1.0;
                }
                for (int k = nct - 1; k >= 0; k--)
                {
                    if (s[k] != 0.0)
                    {
                        for (int j = k + 1; j < nu; j++)
                        {
                            double t = 0;
                            for (int i = k; i < m; i++)
                            {
                                t += U[i][k] * U[i][j];
                            }
                            t = -t / U[k][k];
                            for (int i = k; i < m; i++)
                            {
                                U[i][j] += t * U[i][k];
                            }
                        }
                        for (int i = k; i < m; i++)
                        {
                            U[i][k] = -U[i][k];
                        }
                        U[k][k] = 1.0 + U[k][k];
                        for (int i = 0; i < k - 1; i++)
                        {
                            U[i][k] = 0.0;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < m; i++)
                        {
                            U[i][k] = 0.0;
                        }
                        U[k][k] = 1.0;
                    }
                }
            }

            // If required, generate V.

            if (wantv)
            {
                for (int k = n - 1; k >= 0; k--)
                {
                    if ((k < nrt) & (e[k] != 0.0))
                    {
                        for (int j = k + 1; j < nu; j++)
                        {
                            double t = 0;
                            for (int i = k + 1; i < n; i++)
                            {
                                t += V[i][k] * V[i][j];
                            }
                            t = -t / V[k + 1][k];
                            for (int i = k + 1; i < n; i++)
                            {
                                V[i][j] += t * V[i][k];
                            }
                        }
                    }
                    for (int i = 0; i < n; i++)
                    {
                        V[i][k] = 0.0;
                    }
                    V[k][k] = 1.0;
                }
            }

            // Main iteration loop for the singular values.

            int pp = p - 1;
            int iter = 0;
            double eps = Math.Pow(2.0, -52.0);
            double tiny = Math.Pow(2.0, -966.0);
            while (p > 0)
            {
                int k, kase;

                // Here is where a test for too many iterations would go.

                // This section of the program inspects for
                // negligible elements in the s and e arrays.  On
                // completion the variables kase and k are set as follows.

                // kase = 1     if s(p) and e[k-1] are negligible and k<p
                // kase = 2     if s(k) is negligible and k<p
                // kase = 3     if e[k-1] is negligible, k<p, and
                //              s(k), ..., s(p) are not negligible (qr step).
                // kase = 4     if e(p-1) is negligible (convergence).

                for (k = p - 2; k >= -1; k--)
                {
                    if (k == -1)
                    {
                        break;
                    }
                    if (Math.Abs(e[k]) <=
                          tiny + eps * (Math.Abs(s[k]) + Math.Abs(s[k + 1])))
                    {
                        e[k] = 0.0;
                        break;
                    }
                }
                if (k == p - 2)
                {
                    kase = 4;
                }
                else
                {
                    int ks;
                    for (ks = p - 1; ks >= k; ks--)
                    {
                        if (ks == k)
                        {
                            break;
                        }
                        double t = (ks != p ? Math.Abs(e[ks]) : 0.0) +
                                   (ks != k + 1 ? Math.Abs(e[ks - 1]) : 0.0);
                        if (Math.Abs(s[ks]) <= tiny + eps * t)
                        {
                            s[ks] = 0.0;
                            break;
                        }
                    }
                    if (ks == k)
                    {
                        kase = 3;
                    }
                    else if (ks == p - 1)
                    {
                        kase = 1;
                    }
                    else
                    {
                        kase = 2;
                        k = ks;
                    }
                }
                k++;

                // Perform the task indicated by kase.

                switch (kase)
                {

                    // Deflate negligible s(p).

                    case 1:
                        {
                            double f = e[p - 2];
                            e[p - 2] = 0.0;
                            for (int j = p - 2; j >= k; j--)
                            {
                                double t = Maths.Hypot(s[j], f);
                                double cs = s[j] / t;
                                double sn = f / t;
                                s[j] = t;
                                if (j != k)
                                {
                                    f = -sn * e[j - 1];
                                    e[j - 1] = cs * e[j - 1];
                                }
                                if (wantv)
                                {
                                    for (int i = 0; i < n; i++)
                                    {
                                        t = cs * V[i][j] + sn * V[i][p - 1];
                                        V[i][p - 1] = -sn * V[i][j] + cs * V[i][p - 1];
                                        V[i][j] = t;
                                    }
                                }
                            }
                        }
                        break;

                    // Split at negligible s(k).

                    case 2:
                        {
                            double f = e[k - 1];
                            e[k - 1] = 0.0;
                            for (int j = k; j < p; j++)
                            {
                                double t = Maths.Hypot(s[j], f);
                                double cs = s[j] / t;
                                double sn = f / t;
                                s[j] = t;
                                f = -sn * e[j];
                                e[j] = cs * e[j];
                                if (wantu)
                                {
                                    for (int i = 0; i < m; i++)
                                    {
                                        t = cs * U[i][j] + sn * U[i][k - 1];
                                        U[i][k - 1] = -sn * U[i][j] + cs * U[i][k - 1];
                                        U[i][j] = t;
                                    }
                                }
                            }
                        }
                        break;

                    // Perform one qr step.

                    case 3:
                        {

                            // Calculate the shift.

                            double scale = Math.Max(Math.Max(Math.Max(Math.Max(
                                    Math.Abs(s[p - 1]), Math.Abs(s[p - 2])), Math.Abs(e[p - 2])),
                                    Math.Abs(s[k])), Math.Abs(e[k]));
                            double sp = s[p - 1] / scale;
                            double spm1 = s[p - 2] / scale;
                            double epm1 = e[p - 2] / scale;
                            double sk = s[k] / scale;
                            double ek = e[k] / scale;
                            double b = ((spm1 + sp) * (spm1 - sp) + epm1 * epm1) / 2.0;
                            double c = (sp * epm1) * (sp * epm1);
                            double shift = 0.0;
                            if ((b != 0.0) | (c != 0.0))
                            {
                                shift = Math.Sqrt(b * b + c);
                                if (b < 0.0)
                                {
                                    shift = -shift;
                                }
                                shift = c / (b + shift);
                            }
                            double f = (sk + sp) * (sk - sp) + shift;
                            double g = sk * ek;

                            // Chase zeros.

                            for (int j = k; j < p - 1; j++)
                            {
                                double t = Maths.Hypot(f, g);
                                double cs = f / t;
                                double sn = g / t;
                                if (j != k)
                                {
                                    e[j - 1] = t;
                                }
                                f = cs * s[j] + sn * e[j];
                                e[j] = cs * e[j] - sn * s[j];
                                g = sn * s[j + 1];
                                s[j + 1] = cs * s[j + 1];
                                if (wantv)
                                {
                                    for (int i = 0; i < n; i++)
                                    {
                                        t = cs * V[i][j] + sn * V[i][j + 1];
                                        V[i][j + 1] = -sn * V[i][j] + cs * V[i][j + 1];
                                        V[i][j] = t;
                                    }
                                }
                                t = Maths.Hypot(f, g);
                                cs = f / t;
                                sn = g / t;
                                s[j] = t;
                                f = cs * e[j] + sn * s[j + 1];
                                s[j + 1] = -sn * e[j] + cs * s[j + 1];
                                g = sn * e[j + 1];
                                e[j + 1] = cs * e[j + 1];
                                if (wantu && (j < m - 1))
                                {
                                    for (int i = 0; i < m; i++)
                                    {
                                        t = cs * U[i][j] + sn * U[i][j + 1];
                                        U[i][j + 1] = -sn * U[i][j] + cs * U[i][j + 1];
                                        U[i][j] = t;
                                    }
                                }
                            }
                            e[p - 2] = f;
                            iter = iter + 1;
                        }
                        break;

                    // Convergence.

                    case 4:
                        {

                            // Make the singular values positive.

                            if (s[k] <= 0.0)
                            {
                                s[k] = (s[k] < 0.0 ? -s[k] : 0.0);
                                if (wantv)
                                {
                                    for (int i = 0; i <= pp; i++)
                                    {
                                        V[i][k] = -V[i][k];
                                    }
                                }
                            }

                            // Order the singular values.

                            while (k < pp)
                            {
                                if (s[k] >= s[k + 1])
                                {
                                    break;
                                }
                                double t = s[k];
                                s[k] = s[k + 1];
                                s[k + 1] = t;
                                if (wantv && (k < n - 1))
                                {
                                    for (int i = 0; i < n; i++)
                                    {
                                        t = V[i][k + 1]; V[i][k + 1] = V[i][k]; V[i][k] = t;
                                    }
                                }
                                if (wantu && (k < m - 1))
                                {
                                    for (int i = 0; i < m; i++)
                                    {
                                        t = U[i][k + 1]; U[i][k + 1] = U[i][k]; U[i][k] = t;
                                    }
                                }
                                k++;
                            }
                            iter = 0;
                            p--;
                        }
                        break;
                }
            }
        }

        /* ------------------------
           Public Methods
         * ------------------------ */

        ///<summary>Return the left singular vectors</summary>
        ///<returns>U</returns>
        public Matrix getU()
        {
            return new Matrix(U, m, Math.Min(m + 1, n));
        }


        ///<summary>Return the right singular vectors</summary>
        ///<returns>V</returns>
        public Matrix getV()
        {
            return new Matrix(V, n, n);
        }


        ///<Sumary>Return the one-dimensional array of singular values</Sumary>
        ///<returns>diagonal of S</returns>
        public double[] getSingularValues()
        {
            return s;
        }



        ///<summary>Return the diagonal matrix of singular values</summary>
        ///<returns >S</returns>
        public Matrix getS()
        {
            Matrix X = new Matrix(n, n);
            double[][] S = X.Array;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    S[i][j] = 0.0;
                }
                S[i][i] = s[i];
            }
            return X;
        }


        ///<summary>Two norm</summary>
        ///<returns>max(S)</returns>
        public double Norm2()
        {
            return s[0];
        }


        ///<summary>Two norm condition number</summary>
        ///<returns>max(S)/min(S)</returns>
        public double Cond()
        {
            return s[0] / s[Math.Min(m, n) - 1];
        }



        ///<summary>Effective numerical matrix rank</summary>
        ///<returns>Number of nonnegligible singular values.</returns>
        public int Rank()
        {
            double eps = Math.Pow(2.0, -52.0);
            double tol = Math.Max(m, n) * s[0] * eps;
            int r = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] > tol)
                {
                    r++;
                }
            }
            return r;
        }
    }

}