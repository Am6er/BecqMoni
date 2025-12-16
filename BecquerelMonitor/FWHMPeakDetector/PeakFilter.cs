using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace BecquerelMonitor.FWHMPeakDetector
{
    /// <summary>
    ///An energy-dependent kernel that can be convolved with a spectrum.<br/>
    ///<br/>
    ///To detect lines, a kernel should have a positive component in the center<br/>
    ///and negative wings to subtract the continuum, e.g., a Gaussian or a boxcar:<br/>
    ///<br/>
    ///+2|     ┌───┐     |<br/>
    ///  |     │   │     |<br/>
    /// 0|─┬───┼───┼───┬─|<br/>
    ///-1| └───┘   └───┘ |<br/>
    ///<br/>
    ///The positive part is meant to detect the peak, and the negative part to<br/>
    ///sample the continuum under the peak.<br/>
    ///<br/>
    ///The kernel should sum to 0.<br/>
    ///<br/>
    ///The width of the kernel scales proportionally to the square root<br/>
    ///of the x values(which could be energy, ADC channels, fC of charge<br/>
    ///collected, etc.), with a minimum value set by 'fwhm_at_0'.<br/>
    ///
    /// </summary>
    public class PeakFilter
    {
        FwhmCalibration fwhmCalibration;
        int peak_type;
        double left_skew;
        double right_skew;

        /// <summary>
        /// Empty constructor
        /// </summary>
        public PeakFilter()
        {

        }

        /// <summary>
        /// Initialize with fwhm calibration and peak form definition.
        /// </summary>
        public PeakFilter(FwhmCalibration fwhmCalibration, int peak_type = 0, double left_skew = 1.0, double right_skew = 1.0)
        {
            this.fwhmCalibration = fwhmCalibration;
            this.peak_type = peak_type;
            this.left_skew = left_skew;
            this.right_skew = right_skew;
        }

        public double fwhm(double channel)
        {
            return fwhmCalibration.ChannelToFwhm(channel);
        }

        /// <summary>
        /// Generate the kernel for the given x value.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="edges"></param>
        public double[] kernel(double x, double[] edges)
        {
            double sigma = this.fwhm(x) / 2.35482;

            // TODO: Maybe optimize cycle?
            double[] g1_x0;
            double[] g1_x1;
            double[] edges_wo1 = new double[edges.Length - 1];
            Array.Copy(edges, 1, edges_wo1, 0, edges.Length - 1);
            if (peak_type == 0)
            {
                g1_x0 = gaussian1(edges.Take(edges.Length - 1).ToArray(), x, sigma);
                g1_x1 = gaussian1(edges_wo1, x, sigma);
            } else
            {
                g1_x0 = exp_gauss_exp1(edges.Take(edges.Length - 1).ToArray(), x, sigma, left_skew, right_skew);
                g1_x1 = exp_gauss_exp1(edges_wo1, x, sigma, left_skew, right_skew);
            }

            double[] result = new double[edges.Length - 1];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = g1_x0[i] - g1_x1[i];
            }
            return result;
        }

        /// <summary>
        /// Build a matrix of the kernel evaluated at each x value.
        /// </summary>
        /// <param name="edges"></param>
        /// <returns>normalized kernel double[,] matrix</returns>
        /// <summary>
        /// Build a matrix of the kernel evaluated at each x value.
        /// Returns a jagged array where row i is the kernel for channel i.
        /// </summary>
        public double[][] kernel_matrix(double[] edges)
        {
            int n_channels = edges.Length - 1;
            double[] edges_1 = new double[n_channels];
            Array.Copy(edges, 0, edges_1, 0, n_channels);

            double[][] collection_res = new double[n_channels][];
            for (int i = 0; i < n_channels; i++) collection_res[i] = new double[n_channels];

            Parallel.For(0, n_channels, i =>
            {
                double kern_pos_sum = 0.0;
                double kern_neg_sum = 0.0;
                double[] kernel_for_edge = this.kernel(edges_1[i], edges);
                int len = kernel_for_edge.Length;

                // local buffers
                double[] _kern_pos = new double[len];
                double[] _kern_neg = new double[len];

                for (int j = 0; j < len; j++)
                {
                    double v = kernel_for_edge[j];
                    if (v >= 0)
                    {
                        _kern_pos[j] = v;
                        _kern_neg[j] = 0.0;
                    }
                    else
                    {
                        _kern_pos[j] = 0.0;
                        _kern_neg[j] = -v;
                    }
                    kern_pos_sum += _kern_pos[j];
                    kern_neg_sum += _kern_neg[j];
                }

                double scale = 1.0;
                if (kern_neg_sum > 0.0) scale = kern_pos_sum / kern_neg_sum;

                for (int j = 0; j < len; j++)
                {
                    _kern_neg[j] *= scale;
                    // store row-major: kernel for channel i
                    collection_res[i][j] = _kern_pos[j] - _kern_neg[j];
                }
            });

            return collection_res;
        }

        /// <summary>
        /// Convolve this kernel with the data.
        /// </summary>
        /// <param name="edges">kernel edges</param>
        /// <param name="data">array of data counts</param>
        /// <returns></returns>
        public (double[] peak_plus_bkg, double[] bkg, double[] signal, double[] noise, double[] snr) convolve(double[] edges, double[] data)
        {
            double[][] kernel_mat = this.kernel_matrix(edges);
            int n_channels = kernel_mat.Length;

            double[][] kern_mat_pos = new double[n_channels][];
            double[][] kern_mat_neg = new double[n_channels][];
            for (int i = 0; i < n_channels; i++)
            {
                kern_mat_pos[i] = new double[n_channels];
                kern_mat_neg[i] = new double[n_channels];
            }

            Parallel.For(0, n_channels, i =>
            {
                for (int j = 0; j < n_channels; j++)
                {
                    double v = kernel_mat[i][j];
                    if (v >= 0.0)
                    {
                        kern_mat_pos[i][j] = v;
                        kern_mat_neg[i][j] = 0.0;
                    }
                    else
                    {
                        kern_mat_pos[i][j] = 0.0;
                        kern_mat_neg[i][j] = -v;
                    }
                }
            });

            double[] peak_plus_bkg = new double[n_channels];
            double[] bkg = new double[n_channels];
            double[] signal = new double[n_channels];
            double[] noise = new double[n_channels];
            double[] snr = new double[n_channels];

            Parallel.For(0, n_channels, i =>
            {
                double peak = 0.0;
                double b = 0.0;
                double s = 0.0;
                double nsum = 0.0;
                for (int j = 0; j < n_channels; j++)
                {
                    double d = data[j];
                    double km = kernel_mat[i][j];
                    peak += d * kern_mat_pos[i][j];
                    b += d * kern_mat_neg[i][j];
                    s += d * km;
                    nsum += d * km * km;
                }
                peak_plus_bkg[i] = peak;
                bkg[i] = b;
                signal[i] = s;
                noise[i] = Math.Sqrt(nsum);
                if (noise[i] > 0)
                {
                    snr[i] = signal[i] / noise[i];
                    if (snr[i] < 0) snr[i] = 0.0;
                }
                else
                {
                    snr[i] = 0.0;
                }
            });

            return (peak_plus_bkg, bkg, signal, noise, snr);
        }

        /// <summary>
        /// Gaussian function.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="mean"></param>
        /// <param name="sigma"></param>
        /// <returns></returns>
        public double gaussian0 (double x, double mean, double sigma)
        {
            double z = (x - mean) / sigma;
            return Math.Exp(-z * z / 2.0) / (Math.Sqrt(2.0 * Math.PI) * sigma);
        }

        /// <summary>
        /// First derivative of a gaussian.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="mean"></param>
        /// <param name="sigma"></param>
        /// <returns></returns>
        public double gaussian1 (double x, double mean, double sigma)
        {
            double z = x - mean;
            return -z * gaussian0(x, mean, sigma) / (sigma * sigma);
        }

        /// <summary>
        /// Gaussian function.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="mean"></param>
        /// <param name="sigma"></param>
        /// <returns></returns>
        public double[] gaussian0(double[] x, double mean, double sigma)
        {
            double[] result = new double[x.Length];

            for (int i = 0; i < x.Length; i++)
            {
                result[i] = gaussian0(x[i], mean, sigma);
            }

            return result;
        }

        /// <summary>
        /// First derivative of a gaussian.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="mean"></param>
        /// <param name="sigma"></param>
        /// <returns></returns>
        public double[] gaussian1(double[] x, double mean, double sigma)
        {
            double[] result = new double[x.Length];

            //for (int i = 0; i < x.Length; i++)
            Parallel.For(0, x.Length, i =>
            {
                result[i] = gaussian1(x[i], mean, sigma);
            });

            return result;
        }

        public double exp_gauss_exp0(double x, double median, double sigma, double left, double right)
        {
            double t = (x - median) / sigma;
            if (t > right) return Math.Exp(0.5 * right * right - right * t);
            if (t > -left) return Math.Exp(-0.5 * t * t);
            return Math.Exp(0.5 * left * left + left * t);
        }

        public double exp_gauss_exp1(double x, double median, double sigma, double left, double right)
        {
            double t = (x - median) / sigma;
            if (t > right) return - (right / sigma) * Math.Exp(0.5 * right * right - right * t);
            if (t > -left) return (median - x) * Math.Exp(-0.5 * t * t) / (sigma * sigma);
            return left * Math.Exp(0.5 * left * left + left * t) / sigma;
        }

        public double[] exp_gauss_exp1(double[] x, double mean, double sigma, double left, double right)
        {
            double[] result = new double[x.Length];

            Parallel.For(0, x.Length, i =>
            {
                result[i] = exp_gauss_exp1(x[i], mean, sigma, left, right);
            });

            return result;
        }

    }
}
