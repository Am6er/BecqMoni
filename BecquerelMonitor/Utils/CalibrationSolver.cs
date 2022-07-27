using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.Utils
{
    public static class CalibrationSolver
    {
        public static double[] Solve (List<CalibrationPoint> points, int PolynomialOrder)
        {
            Matrix<double> matrix;
            Vector<double> vector;
            double[,] dense_matrix = new double[points.Count, PolynomialOrder + 1];
            double[] dense_vector = new double[points.Count];

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = PolynomialOrder; j >= 0; j--)
                {
                    double val = (double)Math.Pow(points[i].Channel, j);
                    dense_matrix[i, j] = val;
                }
                dense_vector[i] = (double)points[i].Energy;
            }

            matrix = Matrix<double>.Build.DenseOfArray(dense_matrix);
            vector = Vector<double>.Build.Dense(dense_vector);

            double[] retvalue= matrix.Solve(vector).ToArray();

            return retvalue;

            /*
            Matrix<double> matrix = Matrix<double>.Build.DenseOfArray(new double[,] {
                        { (double)Math.Pow(ch1,4), (double)Math.Pow(ch1,3), (double)Math.Pow(ch1,2), (double)ch1, 1.0 },
                        { (double)Math.Pow(ch2,4), (double)Math.Pow(ch2,3), (double)Math.Pow(ch2,2), (double)ch2, 1.0 },
                        { (double)Math.Pow(ch3,4), (double)Math.Pow(ch3,3), (double)Math.Pow(ch3,2), (double)ch3, 1.0 },
                        { (double)Math.Pow(ch4,4), (double)Math.Pow(ch4,3), (double)Math.Pow(ch4,2), (double)ch4, 1.0 },
                        { (double)Math.Pow(ch5,4), (double)Math.Pow(ch5,3), (double)Math.Pow(ch5,2), (double)ch5, 1.0 }
                    });
            Vector<double> matrix2 = Vector<double>.Build.Dense(new double[] {
                        (double)this.calibrationPoints[0].Energy,
                        (double)this.calibrationPoints[1].Energy,
                        (double)this.calibrationPoints[2].Energy,
                        (double)this.calibrationPoints[3].Energy,
                        (double)this.calibrationPoints[4].Energy
                    });
            */
        }
    }
}
