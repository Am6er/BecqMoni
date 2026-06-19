using System;
using System.Buffers;
using System.Threading.Tasks;
using BecquerelMonitor.Utils;

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
        const double GaussianSigmaWindow = 8.0;
        const double TailAmplitudeCutoffLog = 10.0;

        FwhmCalibration fwhmCalibration;
        int peak_type;
        double left_skew;
        double right_skew;
        double voigt_sigma;
        double voigt_gamma;

        /// <summary>
        /// Empty constructor
        /// </summary>
        public PeakFilter()
        {

        }

        /// <summary>
        /// Initialize with fwhm calibration and peak form definition.
        /// </summary>
        public PeakFilter(FwhmCalibration fwhmCalibration)
        {
            this.fwhmCalibration = fwhmCalibration;
            this.peak_type = fwhmCalibration.PeakType;
            this.left_skew = fwhmCalibration.ExpGaussExpLeftTail;
            this.right_skew = fwhmCalibration.ExpGaussExpRightTail;
            this.voigt_sigma = fwhmCalibration.VoigtSigma;
            this.voigt_gamma = fwhmCalibration.VoigtGamma;
            if (!FwhmCalibration.IsSupportedPeakType(this.peak_type) ||
                (this.peak_type == FwhmCalibration.VoigtPeakType &&
                 (this.voigt_sigma <= 0.0 || this.voigt_gamma <= 0.0 ||
                  Double.IsNaN(this.voigt_sigma) || Double.IsNaN(this.voigt_gamma) ||
                  Double.IsInfinity(this.voigt_sigma) || Double.IsInfinity(this.voigt_gamma))))
            {
                this.peak_type = FwhmCalibration.GaussianPeakType;
            }
        }

        public double fwhm(double channel)
        {
            return fwhmCalibration.ChannelToFwhm(channel);
        }

        double tail_support(double skew)
        {
            if (skew <= 0.0 || Double.IsNaN(skew) || Double.IsInfinity(skew))
            {
                return GaussianSigmaWindow;
            }
            return Math.Max(GaussianSigmaWindow, (TailAmplitudeCutoffLog + 0.5 * skew * skew) / skew);
        }

        double voigt_support(double fwhm, double sigmaScale, double gammaScale)
        {
            if (sigmaScale <= 0.0 || gammaScale <= 0.0 ||
                Double.IsNaN(sigmaScale) || Double.IsNaN(gammaScale) ||
                Double.IsInfinity(sigmaScale) || Double.IsInfinity(gammaScale))
            {
                return GaussianSigmaWindow * fwhm / PseudoVoigtProfile.FwhmToSigma;
            }

            return PseudoVoigtProfile.HalfWidthAtRelativeHeight(
                fwhm,
                sigmaScale,
                gammaScale,
                Math.Exp(-TailAmplitudeCutoffLog));
        }

        int first_intersecting_bin(double[] edges, double x, int nChannels)
        {
            if (x <= edges[0])
            {
                return 0;
            }
            if (x >= edges[nChannels])
            {
                return nChannels;
            }

            int idx = Array.BinarySearch(edges, x);
            if (idx >= 0)
            {
                return Math.Min(idx, nChannels - 1);
            }
            return Math.Max(0, ~idx - 1);
        }

        int last_intersecting_bin(double[] edges, double x, int nChannels)
        {
            if (x <= edges[0])
            {
                return -1;
            }
            if (x >= edges[nChannels])
            {
                return nChannels - 1;
            }

            int idx = Array.BinarySearch(edges, x);
            if (idx >= 0)
            {
                return idx - 1;
            }
            return ~idx - 1;
        }

        void get_kernel_window(double center, double sigma, double[] edges, out int leftIndex, out int rightIndex)
        {
            int nChannels = edges.Length - 1;
            double leftHalfWidth = GaussianSigmaWindow * sigma;
            double rightHalfWidth = GaussianSigmaWindow * sigma;

            if (peak_type == FwhmCalibration.ExpGaussExpPeakType)
            {
                leftHalfWidth = tail_support(left_skew) * sigma;
                rightHalfWidth = tail_support(right_skew) * sigma;
            }
            else if (peak_type == FwhmCalibration.VoigtPeakType)
            {
                leftHalfWidth = voigt_support(PseudoVoigtProfile.FwhmToSigma * sigma, voigt_sigma, voigt_gamma);
                rightHalfWidth = leftHalfWidth;
            }

            double leftX = center - leftHalfWidth;
            double rightX = center + rightHalfWidth;
            leftIndex = first_intersecting_bin(edges, leftX, nChannels);
            rightIndex = last_intersecting_bin(edges, rightX, nChannels);
        }

        double kernel_derivative(double edge, double center, double sigma, PseudoVoigtParameters voigtParameters)
        {
            if (peak_type == FwhmCalibration.GaussianPeakType)
            {
                return gaussian1(edge, center, sigma);
            }
            if (peak_type == FwhmCalibration.ExpGaussExpPeakType)
            {
                return exp_gauss_exp1(edge, center, sigma, left_skew, right_skew);
            }
            if (peak_type == FwhmCalibration.VoigtPeakType)
            {
                return PseudoVoigtProfile.Derivative(edge - center, voigtParameters);
            }
            return gaussian1(edge, center, sigma);
        }

        bool try_build_kernel_row(double center, double[] edges, double[] kernelRow, out int leftIndex, out int rightIndex, out double negScale)
        {
            int nChannels = edges.Length - 1;
            double sigma = this.fwhm(center) / 2.35482;
            negScale = 1.0;
            leftIndex = 0;
            rightIndex = -1;

            if (sigma <= 0.0 || Double.IsNaN(sigma) || Double.IsInfinity(sigma))
            {
                return false;
            }

            PseudoVoigtParameters voigtParameters = new PseudoVoigtParameters();
            if (peak_type == FwhmCalibration.VoigtPeakType &&
                !PseudoVoigtProfile.TryCreate(PseudoVoigtProfile.FwhmToSigma * sigma, voigt_sigma, voigt_gamma, out voigtParameters))
            {
                return false;
            }

            get_kernel_window(center, sigma, edges, out leftIndex, out rightIndex);
            if (leftIndex > rightIndex || leftIndex >= nChannels || rightIndex < 0)
            {
                return false;
            }

            double kernPosSum = 0.0;
            double kernNegSum = 0.0;
            for (int i = leftIndex; i <= rightIndex; i++)
            {
                double value = kernel_derivative(edges[i], center, sigma, voigtParameters) -
                    kernel_derivative(edges[i + 1], center, sigma, voigtParameters);
                kernelRow[i] = value;
                if (value >= 0.0)
                {
                    kernPosSum += value;
                }
                else
                {
                    kernNegSum -= value;
                }
            }

            if (kernNegSum > 0.0)
            {
                negScale = kernPosSum / kernNegSum;
            }
            return true;
        }

        /// <summary>
        /// Generate the kernel for the given x value.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="edges"></param>
        public double[] kernel(double x, double[] edges)
        {
            int nChannels = edges.Length - 1;
            double[] result = new double[nChannels];
            try_build_kernel_row(x, edges, result, out _, out _, out _);
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
            int nChannels = edges.Length - 1;
            double[][] collectionRes = new double[nChannels][];

            Parallel.For<double[]>(0, nChannels,
                () => ArrayPool<double>.Shared.Rent(nChannels),
                (i, state, kernelRow) =>
                {
                    double center = (edges[i] + edges[i + 1]) / 2.0;
                    Array.Clear(kernelRow, 0, nChannels);
                    bool hasKernel = try_build_kernel_row(center, edges, kernelRow, out int leftIndex, out int rightIndex, out double negScale);

                    double[] row = new double[nChannels];
                    if (hasKernel)
                    {
                        for (int j = leftIndex; j <= rightIndex; j++)
                        {
                            double value = kernelRow[j];
                            row[j] = value >= 0.0 ? value : value * negScale;
                        }
                    }
                    collectionRes[i] = row;
                    return kernelRow;
                },
                kernelRow => ArrayPool<double>.Shared.Return(kernelRow));

            return collectionRes;
        }

        /// <summary>
        /// Convolve this kernel with the data.
        /// </summary>
        /// <param name="edges">kernel edges</param>
        /// <param name="data">array of data counts</param>
        /// <returns></returns>
        public (double[] peak_plus_bkg, double[] bkg, double[] signal, double[] noise, double[] snr) convolve(double[] edges, double[] data)
        {
            int nChannels = edges.Length - 1;
            double[] peakPlusBkg = new double[nChannels];
            double[] bkg = new double[nChannels];
            double[] signal = new double[nChannels];
            double[] noise = new double[nChannels];
            double[] snr = new double[nChannels];

            Parallel.For<double[]>(0, nChannels,
                () => ArrayPool<double>.Shared.Rent(nChannels),
                (i, state, kernelRow) =>
                {
                    double center = (edges[i] + edges[i + 1]) / 2.0;
                    bool hasKernel = try_build_kernel_row(center, edges, kernelRow, out int leftIndex, out int rightIndex, out double negScale);

                    double peak = 0.0;
                    double background = 0.0;
                    double sig = 0.0;
                    double noiseSum = 0.0;

                    if (hasKernel)
                    {
                        for (int j = leftIndex; j <= rightIndex; j++)
                        {
                            double d = data[j];
                            double value = kernelRow[j];
                            if (value >= 0.0)
                            {
                                peak += d * value;
                            }
                            else
                            {
                                value *= negScale;
                                background += d * (-value);
                            }

                            sig += d * value;
                            noiseSum += d * value * value;
                        }
                    }

                    peakPlusBkg[i] = peak;
                    bkg[i] = background;
                    signal[i] = sig;
                    noise[i] = Math.Sqrt(noiseSum);
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

                    return kernelRow;
                },
                kernelRow => ArrayPool<double>.Shared.Return(kernelRow));

            return (peakPlusBkg, bkg, signal, noise, snr);
        }

        /// <summary>
        /// Gaussian function.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="mean"></param>
        /// <param name="sigma"></param>
        /// <returns></returns>
        public double gaussian0(double x, double mean, double sigma)
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
        public double gaussian1(double x, double mean, double sigma)
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

            for (int i = 0; i < x.Length; i++)
            {
                result[i] = gaussian1(x[i], mean, sigma);
            }

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
            if (t > right) return -(right / sigma) * Math.Exp(0.5 * right * right - right * t);
            if (t > -left) return (median - x) * Math.Exp(-0.5 * t * t) / (sigma * sigma);
            return left * Math.Exp(0.5 * left * left + left * t) / sigma;
        }

        public double[] exp_gauss_exp1(double[] x, double mean, double sigma, double left, double right)
        {
            double[] result = new double[x.Length];

            for (int i = 0; i < x.Length; i++)
            {
                result[i] = exp_gauss_exp1(x[i], mean, sigma, left, right);
            }

            return result;
        }

        public double voigt0(double x, double median, double sigma, double sigmaScale, double gammaScale)
        {
            PseudoVoigtParameters parameters;
            if (!PseudoVoigtProfile.TryCreate(PseudoVoigtProfile.FwhmToSigma * sigma, sigmaScale, gammaScale, out parameters))
            {
                return 0.0;
            }

            return PseudoVoigtProfile.Value(x - median, parameters);
        }

        public double voigt1(double x, double median, double sigma, double sigmaScale, double gammaScale)
        {
            PseudoVoigtParameters parameters;
            if (!PseudoVoigtProfile.TryCreate(PseudoVoigtProfile.FwhmToSigma * sigma, sigmaScale, gammaScale, out parameters))
            {
                return 0.0;
            }

            return PseudoVoigtProfile.Derivative(x - median, parameters);
        }

    }
}
