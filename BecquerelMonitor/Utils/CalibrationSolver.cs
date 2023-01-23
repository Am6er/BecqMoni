using System;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using MathNet.Numerics.LinearRegression;
using System.Linq;

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

        public static double[] SolveWeighted (List<CalibrationPoint> points, int PolynomialOrder)
        {
            Matrix<double> matrix;
            Vector<double> vector;
            Matrix<double> weight;
            double[,] dense_matrix = new double[points.Count, PolynomialOrder + 1];
            double[] dense_vector = new double[points.Count];
            double[,] dense_weight = new double[points.Count, points.Count];

            double max_count = points.Max(point => point.Count);

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = 0; j < points.Count; j ++)
                {
                    if (i == j)
                    {
                        dense_weight[i, j] = Math.Sqrt((double)points[i].Count / max_count);
                    }
                    else
                    {
                        dense_weight[i, j] = 0;
                    }
                }
            }

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
            weight = Matrix<double>.Build.DenseOfArray(dense_weight);

            double[] retvalue = WeightedRegression.Weighted(matrix, vector, weight).ToArray();

            return retvalue;
        }

        public static double MSE(double[] coefficients, List<CalibrationPoint> points)
        {
            try
            {
                PolynomialEnergyCalibration pol = new PolynomialEnergyCalibration
                {
                    Coefficients = coefficients,
                    PolynomialOrder = coefficients.Length - 1
                };
                double retvalue = 0.0;
                foreach (CalibrationPoint point in points)
                {
                    retvalue += Math.Pow(pol.ChannelToEnergy(point.Channel) - (double)point.Energy, 2);
                }
                retvalue /= points.Count;
                return retvalue;
            }
            catch
            {
                return -1;
            }
        }

        public static double WMSE(double[] coefficients, List<CalibrationPoint> points)
        {
            try
            {
                double retvalue = 0.0;
                double max_count = points.Max(point => point.Count);
                double weight_sum = 0.0;
                PolynomialEnergyCalibration pol = new PolynomialEnergyCalibration
                {
                    Coefficients = coefficients,
                    PolynomialOrder = coefficients.Length - 1
                };
                for (int i = 0; i < points.Count; i++)
                {
                    double weight = Math.Sqrt((double)points[i].Count / max_count);
                    weight_sum += weight;
                    retvalue += weight * Math.Pow(pol.ChannelToEnergy(points[i].Channel) - (double)points[i].Energy, 2);
                }
                retvalue /= (points.Count * weight_sum);
                return retvalue;
            } catch
            {
                return -1;
            }
        }
    }
}
