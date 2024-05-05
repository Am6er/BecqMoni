using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
        double ref_x;
        double ref_fwhm;
        double fwhm_at_0;

        /// <summary>
        /// Empty constructor
        /// </summary>
        public PeakFilter()
        {

        }

        /// <summary>
        /// Initialize with a reference line position and FWHM in x-values.
        /// </summary>
        /// <param name="ref_x">Initial reference channel position with given fwhm</param>
        /// <param name="ref_fwhm">Initial reference fwhm for given ref_x</param>
        /// <param name="fwhm_at_0">FWHM at Energy = 0keV</param>
        public PeakFilter(double ref_x, double ref_fwhm, double fwhm_at_0 = 1.0)
        {
            if (ref_x <= 0.0)
            {
                throw new PeakFilterError("Reference x must be positive");
            }
            if (ref_fwhm <= 0.0)
            {
                throw new PeakFilterError("Reference FWHM must be positive");
            }
            if (fwhm_at_0 <= 0.0)
            {
                throw new PeakFilterError("FWHM at 0 must be non-negative");
            }
            this.ref_x = ref_x;
            this.ref_fwhm = ref_fwhm;
            this.fwhm_at_0 = fwhm_at_0;
        }

        /// <summary>
        /// Calculate the expected FWHM at the given x value.<br/>
        /// f(x)^2 = f0^2 + k x^2<br/>
        /// f1^2 = f0^2 + k x1^2<br/>
        /// k = (f1^2 - f0^2) / x1^2<br/>
        /// f(x)^2 = f0^2 + (f1^2 - f0^2) (x/x1)^2<br/>
        /// </summary>
        /// <param name="x">channel</param>
        /// <returns></returns>
        public double fwhm(double x)
        {
            double f0 = this.fwhm_at_0;
            double f1 = this.ref_fwhm;
            double x1 = this.ref_x;
            double fwhm_sqr = Math.Pow(f0, 2) + (Math.Pow(f1, 2) - Math.Pow(f0, 2)) * (x / x1);
            return Math.Sqrt(fwhm_sqr);
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
            double[] g1_x0 = gaussian1(edges.Take(edges.Length - 1).ToArray(), x, sigma); //-0.00000,-0.99309,-1.94530,-2.81854,-3.58004
            double[] edges_wo1 = new double[edges.Length - 1];
            Array.Copy(edges, 1, edges_wo1, 0, edges.Length - 1);
            double[] g1_x1 = gaussian1(edges_wo1, x, sigma); //-0.99309,-1.94530,-2.81854,-3.58004

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
        public ConcurrentDictionary<int, double[]> kernel_matrix(double[] edges)
        {
            int n_channels = edges.Length - 1;
            double[,] kern_pos = new double[n_channels, n_channels];
            double[,] kern_neg = new double[n_channels, n_channels];
            double[] edges_1 = edges.Take(edges.Length - 1).ToArray();
            //Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ff") + " -> -> -> Get empty pos and neg. (Parallel)");
            ConcurrentDictionary<int, double[]> collection_res = new ConcurrentDictionary<int, double[]>();
            for (int i = 0; i < n_channels; i++)
            {
                collection_res[i] = new double[n_channels];
            }
            Parallel.For(0, edges_1.Length, i =>
            {
                double kern_pos_sum = 0.0;
                double kern_neg_sum = 0.0;
                double[] kernel_for_edge = this.kernel(edges_1[i], edges);
                double[] _kern_pos = new double[kernel_for_edge.Length];
                double[] _kern_neg = new double[kernel_for_edge.Length];
                double[] _kern_res = new double[kernel_for_edge.Length];
                for (int j = 0; j < kernel_for_edge.Length; j++)
                {
                    if (kernel_for_edge[j] >= 0)
                    {
                        _kern_pos[j] = kernel_for_edge[j];
                        _kern_neg[j] = 0.0;
                    }
                    else
                    {
                        _kern_pos[j] = 0.0;
                        _kern_neg[j] = -1.0 * kernel_for_edge[j];
                    }
                    kern_pos_sum += _kern_pos[j];
                    kern_neg_sum += _kern_neg[j];
                }
                // normalize negative part to be equal to the positive part
                for (int j = 0; j < kernel_for_edge.Length; j++)
                {
                    _kern_neg[j] *= kern_pos_sum / kern_neg_sum;
                    collection_res[j][i] = _kern_pos[j] - _kern_neg[j];
                }
                kern_pos_sum = 0.0;
                kern_neg_sum = 0.0;
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
            //Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ff") + " -> -> Get empty matrix.");
            ConcurrentDictionary<int, double[]> kernel_mat = this.kernel_matrix(edges);
            int n_channels = kernel_mat[0].Length;
            double[,] kern_mat_pos = new double[n_channels, n_channels];
            double[,] kern_mat_neg = new double[n_channels, n_channels];

            ConcurrentDictionary<int, double[]> _kern_mat_pos = new ConcurrentDictionary<int, double[]>();
            ConcurrentDictionary<int, double[]> _kern_mat_neg = new ConcurrentDictionary<int, double[]>();
            for (int i = 0; i < n_channels; i++)
            {
                _kern_mat_pos[i] = new double[n_channels];
                _kern_mat_neg[i] = new double[n_channels];
            }

            //Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ff") + " -> -> Fill matrix pos and neg. (Parallel)");
            Parallel.For(0, n_channels, i =>
            //for (int i = 0; i < n_channels; i++)
            {
                for (int j = 0; j < n_channels; j++)
                {
                    if (kernel_mat[j][i] >= 0.0)
                    {
                        _kern_mat_pos[j][i] = kernel_mat[j][i];
                        _kern_mat_neg[j][i] = 0.0;
                    }
                    else
                    {
                        _kern_mat_pos[j][i] = 0.0;
                        _kern_mat_neg[j][i] = -1.0 * kernel_mat[j][i];
                    }
                }
            });

            //Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ff") + " -> -> Calculate peak_plus_bg, bkg, signal, noise, snr. (Parallel)");
            double[] peak_plus_bkg = new double[n_channels];
            double[] bkg = new double[n_channels];
            double[] signal = new double[n_channels];
            double[] noise = new double[n_channels];
            double[] snr = new double[n_channels];


            //TODO Possible thread unsafe. Need to make it safe.
            Parallel.For(0, n_channels, i =>
            //for (int i = 0; i < n_channels; i++)
            {
                for (int j = 0; j < n_channels; j++)
                {
                    peak_plus_bkg[i] += data[j] * _kern_mat_pos[i][j];
                    bkg[i] += data[j] * _kern_mat_neg[i][j];
                    signal[i] += data[j] * kernel_mat[i][j];
                    noise[i] += data[j] * Math.Pow(kernel_mat[i][j], 2);
                }
                noise[i] = Math.Sqrt(noise[i]);
                if (noise[i] > 0)
                {
                    snr[i] = signal[i] / noise[i];
                    if (snr[i] < 0)
                    {
                        snr[i] = 0.0;
                    }
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

    }
}
