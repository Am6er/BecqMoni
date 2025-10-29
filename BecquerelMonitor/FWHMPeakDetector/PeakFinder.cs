using System;
using System.Collections.Generic;
using System.Linq;

namespace BecquerelMonitor.FWHMPeakDetector
{
    /// <summary>
    /// Find peaks in a spectrum after convolving it with a kernel.
    /// </summary>
    public class PeakFinder
    {
        public int min_sep;
        public double fwhm_tol_min;
        public double fwhm_tol_max;
        public Spectrum spectrum;
        public PeakFilter kernel;
        public double[] snr;
        public double[] peak_plus_bkg;
        public double[] bkg;
        public double[] signal;
        public double[] noise;
        public double[] centroids;
        public double[] snrs;
        public double[] fwhms;
        public double[] integrals;
        public double[] backgrounds;
        public bool[] peak;


        /// <summary>
        /// Initialize with a spectrum and kernel
        /// </summary>
        /// <param name="spectrum"></param>
        /// <param name="kernel"></param>
        /// <param name="min_sep"></param>
        /// <param name="fwhm_tol"></param>
        public PeakFinder(Spectrum spectrum, PeakFilter kernel, int min_sep = 5, double fwhm_tol_min = 0.5, double fwhm_tol_max = 1.5)
        {
            if (min_sep <= 0)
            {
                throw new PeakFinderError("Minimum x separation must be positive");
            }

            this.min_sep = min_sep;
            this.fwhm_tol_min = fwhm_tol_min;
            this.fwhm_tol_max = fwhm_tol_max;
            // TODO: Should be spectrum = null; kernel = null; in source code
            this.spectrum = null;
            this.kernel = null;
            this.snr = null;
            this.peak_plus_bkg = null;
            this.bkg = null;
            this.signal = null;
            this.noise = null;
            this.centroids = null;
            this.snrs = null;
            this.fwhms = null;
            this.integrals = null;
            this.backgrounds = null;
            this.calculate(spectrum, kernel);
        }

        /// <summary>
        /// Resets centroids, snrs, fwhms, integrals, backgrounds
        /// </summary>
        public void reset()
        {
            this.centroids = null;
            this.snrs = null;
            this.fwhms = null;
            this.integrals = null;
            this.backgrounds = null;
        }

        /// <summary>
        /// C# numpy.argsort implementation.
        /// return original indexes of sorted array elements
        /// </summary>
        /// <param name="arr"></param>
        /// <returns></returns>
        private int[] argsort(double[] arr)
        {
            var sorted = arr.Select((x, i) => new KeyValuePair<double, int>(x, (int)i)).OrderBy(x => x.Key).ToArray();
            return sorted.Select(x => x.Value).ToArray();
        }

        /// <summary>
        /// Sort peaks by the provided array.
        /// </summary>
        /// <param name="arr"></param>
        /// <exception cref="PeakFinderError"></exception>
        public void sort_by(double[] arr)
        {
            if (arr.Length != this.centroids.Length)
            {
                throw new PeakFinderError(String.Format("Sorting array has length " + arr.Length + "  but must have length " + this.centroids.Length));
            }
            int[] sortedarg = this.argsort(arr);
            List<double> _centroids = new List<double>();
            List<double> _snrs = new List<double>();
            List<double> _fwhms = new List<double>();
            List<double> _integrals = new List<double>();
            List<double> _backgrounds = new List<double>();
            foreach (int el in sortedarg)
            {
                _centroids.Add(this.centroids[el]);
                _snrs.Add(this.snrs[el]);
                _fwhms.Add(this.fwhms[el]);
                _integrals.Add(this.integrals[el]);
                _backgrounds.Add(this.backgrounds[el]);
            }
            this.centroids = _centroids.ToArray();
            this.snrs = _snrs.ToArray();
            this.fwhms = _fwhms.ToArray();
            this.integrals = _integrals.ToArray();
            this.backgrounds = _backgrounds.ToArray();
        }

        /// <summary>
        /// Calculate the convolution of the spectrum with the kernel.
        /// </summary>
        /// <param name="spectrum"></param>
        /// <param name="kernel"></param>
        public void calculate(Spectrum spectrum, PeakFilter kernel)
        {
            this.spectrum = spectrum;
            this.kernel = kernel;
            this.snr = new double[this.spectrum.counts.Length];

            // calculate the convolution
            //Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.ff") + " -> Start convolve.");
            (this.peak_plus_bkg, this.bkg, this.signal, this.noise, this.snr) = this.kernel.convolve(this.spectrum.bin_edges_raw, this.spectrum.counts);
            this.reset();
        }

        /// <summary>
        /// Add founded peak
        /// </summary>
        /// <param name="xpeak"></param>
        /// <exception cref="PeakFinderError"></exception>
        public void add_peak(double xpeak)
        {
            double[] bin_edges = this.spectrum.bin_edges_raw;
            double xmin = bin_edges.Min();
            double xmax = bin_edges.Max();

            if (xpeak < xmin || xpeak > xmax)
            {
                throw new PeakFinderError(String.Format("Peak x " + xpeak + " is outside of range " + xmin + " - " + xmax));
            }
            bool is_New_x = true;
            if (this.centroids != null)
            {
                foreach (double cent in this.centroids)
                {
                    if (Math.Abs(xpeak - cent) <= this.min_sep)
                    {
                        is_New_x = false;
                    }
                }
            }
            
            if (is_New_x)
            {
                /* estimate FWHM using the second derivative
                snr(x) = snr(x0) - 0.5 d2snr/dx2(x0) (x-x0)^2
                0.5 = 1 - 0.5 d2snr/dx2 (fwhm/2)^2 / snr0
                1 = d2snr/dx2 (fwhm/2)^2 / snr0
                fwhm = 2 sqrt(2 * snr0 / d2snr/dx2)
                */

                int xbin = this.spectrum.find_bin_index(xpeak);
                double fwhm0 = this.kernel.fwhm(xpeak);
                double bw = this.spectrum.bin_widths_raw[0];
                int h = (int)Math.Max(1, 0.2 * fwhm0 / bw);

                // skip peaks that are too close to the edge
                if (xbin - h < 0 || xbin + h > this.snr.Length - 1)
                {
                    return;
                }

                double d2 = (this.snr[xbin - h] - 2 * this.snr[xbin] + this.snr[xbin + h]) / (Math.Pow(h, 2) * Math.Pow(bw, 2));

                if (d2 >= 0)
                {
                    return;
                    //throw new PeakFinderError("Second derivative must be negative at peak");
                }

                d2 *= -1;
                double fwhm = 2 * Math.Sqrt(2 * this.snr[xbin] / d2);
                // add the peak if it has a similar FWHM to the kernel's FWHM
                if (this.fwhm_tol_min * fwhm0 <= fwhm && fwhm <= this.fwhm_tol_max * fwhm0)
                {
                    if (this.centroids == null)
                    {
                        this.centroids = new[] { xpeak };

                    } else
                    {
                        this.centroids = this.centroids.Append(xpeak).ToArray();
                    }

                    if (this.snrs == null)
                    {
                        this.snrs = new[] { this.snr[xbin] };
                    } else
                    {
                        this.snrs = this.snrs.Append(this.snr[xbin]).ToArray();
                    }
                    
                    if (this.fwhms == null)
                    {
                        this.fwhms = new[] { fwhm };
                    } else
                    {
                        this.fwhms = this.fwhms.Append(fwhm).ToArray();
                    }
                    
                    if (this.integrals == null)
                    {
                        this.integrals = new[] { this.signal[xbin] };
                    } else
                    {
                        this.integrals = this.integrals.Append(this.signal[xbin]).ToArray();
                    }
                    
                    if (this.backgrounds == null)
                    {
                        this.backgrounds = new[] { this.bkg[xbin] };
                    } else
                    {
                        this.backgrounds = this.backgrounds.Append(this.bkg[xbin]).ToArray();
                    }
                    // sort the peaks by centroid
                    this.sort_by(this.centroids);
                }
            }
        }

        /// <summary>
        /// Find the highest SNR peaks in the data.
        /// </summary>
        /// <param name="xmin"></param>
        /// <param name="xmax"></param>
        /// <param name="min_snr"></param>
        /// <param name="max_num"></param>
        /// <returns></returns>
        public void find_peaks(double xmin, double xmax, double min_snr = 2, int max_num = 40)
        {
            double[] bin_edges = this.spectrum.bin_edges_raw;
            double[] bin_centers = this.spectrum.bin_centers_raw;
            double bin_edges_min = bin_edges.Min();
            double bin_edges_max = bin_edges.Max();

            if (xmin == -1)
            {
                xmin = bin_edges_min;
            }

            if (xmax == -1)
            {
                xmax = bin_edges_max;
            }

            if (xmin < bin_edges_min)
            {
                xmin = bin_edges_min;
            }

            if (xmax > bin_edges_max)
            {
                xmax = bin_edges_max;
            }

            //if ( xmin > bin_edges.Max() || xmax < bin_edges.Min() || xmin > xmax)
            //{
            //    throw new PeakFinderError("x-axis range " + xmin + "-" + xmax + " is invalid");
            //}
            double snr_max = this.snr.Max();
            if (min_snr < 0 || min_snr > snr_max)
            {
                if (snr_max > 1)
                {
                    return;
                    //BecquerelMonitor.DCPeakDetectionView.Correct_min_snr = true;
                    //BecquerelMonitor.DCPeakDetectionView.Min_snr_value = Math.Truncate(this.snr.Max());
                }
                if (snr_max <= 1)
                {
                    return;
                    //MessageBox.Show("Could not find any peak with snr > 1.0");
                }
                return;
            }

            if (max_num < 1)
            {
                throw new PeakFinderError("Must keep at least 1 peak, not " +  max_num);
            }

            //find maxima
            bool[] peak = new bool[this.snr.Length - 2];
            for (int i = 0; i < peak.Length; i++)
            {
                double snr_wo1 = this.snr[i];
                double snr_wo2 = this.snr[i + 1];
                double snr_wo3 = this.snr[i + 2];
                peak[i] = (snr_wo1 < snr_wo2) & (snr_wo2 >= snr_wo3);
            }
            bool[] new_peak = new bool[peak.Length + 2];
            new_peak[0] = false;
            new_peak[new_peak.Length - 1] = false;
            for (int i = 0; i < peak.Length - 2; i++)
            {
                new_peak[i + 1] = peak[i];
            }
            peak = new_peak;
            //select peaks using SNR and centroid criteria
            double[] bin_edges1 = bin_edges.Take(bin_edges.Length - 1).ToArray();
            for (int i = 0; i < peak.Length; i++)
            {
                peak[i] &= min_snr <= this.snr[i];
                peak[i] &= xmin <= bin_edges1[i];
                peak[i] &= bin_edges1[i] <= xmax;
            }
            for (int i = 0; i < peak.Length; i++)
            {
                if (peak[i])
                {
                    this.add_peak(bin_centers[i]);
                }
            }

            //reduce number of centroids to a maximum number max_n of highest SNR
            if (this.snrs == null)
            {
                return;
            }
            this.sort_by(this.snrs);
            this.centroids = this.centroids.Take(max_num).ToArray();
            this.snrs = this.snrs.Take(max_num).ToArray();
            this.fwhms = this.fwhms.Take(max_num).ToArray();
            this.integrals = this.integrals.Take(max_num).ToArray();
            this.backgrounds = this.backgrounds.Take(max_num).ToArray();
            //sort by centroid
            this.sort_by(this.centroids);
            this.peak = peak;

        }
    }
}
