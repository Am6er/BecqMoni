using System;
using System.Threading.Tasks;

namespace BecquerelMonitor.FWHMPeakDetector
{
    /*
        Represents an energy spectrum.

    Initialize a Spectrum directly, or with Spectrum.from_file(filename), or
    with Spectrum.from_listmode(listmode_data).

    Note on livetime:
      A livetime of None is the default for a spectrum, and indicates a
        missing, unknown livetime or not meaningful quantity.
      A spectrum may be initialized with a livetime value. However, any
        operation that produces a CPS-based spectrum (such as a spectrum
        subtraction) will discard the livetime and set it to None.
      Operations that produce a counts-based spectrum may or may not preserve a
        livetime value (for example, the sum of two spectra has a livetime
        equal to the sum of the two livetimes; but a scalar multiplication or
        division results in a livetime of None).

    Data Attributes:
      bin_edges_raw: np.array of raw/uncalibrated bin edges
      bin_edges_kev: np.array of energy bin edges, if calibrated
      livetime: int or float of livetime, in seconds. See note above
      realtime: int or float of realtime, in seconds. May be None
      start_time: a datetime.datetime object representing the acquisition start
      stop_time: a datetime.datetime object representing the acquisition end

    Properties (read-only):
      counts: counts in each bin, with uncertainty
      counts_vals: counts in each bin, no uncertainty
      counts_uncs: uncertainties on counts in each bin
      cps: counts per second in each bin, with uncertainty
      cps_vals: counts per second in each bin, no uncertainty
      cps_uncs: uncertainties on counts per second in each bin
      cpskev: CPS/keV in each bin, with uncertainty
      cpskev_vals: CPS/keV in each bin, no uncertainty
      cpskev_uncs: uncertainties on CPS/keV in each bin
      bins: np.array of bin index as integers
      channels: np.array of bin index as integers (deprecated, raises a
        DeprecationWarning)
      is_calibrated: bool indicating calibration status
      energies_kev: np.array of energy bin centers, if calibrated (deprecated,
        raises a DeprecationWarning)
      bin_centers_raw: np.array of raw bin centers
      bin_centers_kev: np.array of energy bin centers, if calibrated
      bin_widths: np.array of energy bin widths, if calibrated (deprecated,
        raises a DeprecationWarning)
      bin_widths_raw: np.array of raw bin widths
      bin_widths_kev: np.array of energy bin widths, if calibrated

    Methods:
      apply_calibration: use a Calibration object to calibrate this spectrum
      calibrate_like: copy the calibrated bin edges from another spectrum
      rm_calibration: remove the calibrated bin edges
      combine_bins: make a new Spectrum with counts combined into bigger bins
      downsample: make a new Spectrum, downsampled from this one by a factor
      copy: return a deep copy of this Spectrum object
    */
    public class Spectrum
    {
        public double[] counts;
        public double[] bin_edges_raw;
        public double[] bin_widths_raw;
        public double[] bin_centers_raw;

        /// <summary>
        /// Initialize the spectrum.
        /// 
        /// </summary>
        /// <param name="counts">counts per bin double[]</param>
        /// <param name="bin_edges_raw">bin edge array should have length of (len(counts) + 1) </param>
        /// <exception cref="SpectrumError"></exception>
        public Spectrum(double[] counts, double[] bin_edges_raw)
        {
            this.counts = counts;
            this.bin_edges_raw = bin_edges_raw;
            this.bin_centers_raw = new double[counts.Length + 1];
            this.bin_widths_raw = new double[counts.Length + 1];

            for (int i = 0; i < this.bin_centers_raw.Length - 1; i++)
            {
                this.bin_centers_raw[i] = (this.bin_edges_raw[i + 1] + this.bin_edges_raw[i]) / 2.0;
                this.bin_widths_raw[i] = this.bin_edges_raw[i + 1] - this.bin_edges_raw[i];
            }
        }

        public Spectrum(EnergySpectrum energySpectrum)
        {
            this.counts = new double[energySpectrum.NumberOfChannels];
            this.bin_edges_raw = new double[counts.Length + 1];
            this.bin_centers_raw = new double[counts.Length + 1];
            this.bin_widths_raw = new double[counts.Length + 1];

            Parallel.For(0, counts.Length, i => {
                this.counts[i] = Convert.ToDouble(energySpectrum.Spectrum[i]);
                this.bin_edges_raw[i] = Convert.ToDouble(i);
            });
            this.bin_edges_raw[counts.Length] = Convert.ToDouble(counts.Length);


            for (int i = 0; i < this.bin_centers_raw.Length - 1; i++)
            {
                this.bin_centers_raw[i] = (this.bin_edges_raw[i + 1] + this.bin_edges_raw[i]) / 2.0;
                this.bin_widths_raw[i] = this.bin_edges_raw[i + 1] - this.bin_edges_raw[i];
            }
        }

        /// <summary>
        /// Return len number of channels in spectrum
        /// </summary>
        /// <returns></returns>
        public int len()
        {
            return this.counts.Length;
        }

        /*
            Find the Spectrum bin index or indices containing x-axis value(s) x.

            One might think that if the Spectrum has uniform binning, we could just
            solve the linear equation between bin indices and x-axis values.
            However, this can introduce enough precision loss to change the index.
            As such, we use searchsorted to bisect for the insertion point where x
            would fit in a list of bin edges, then subtract 1 to get the index of
            the low edge. The numpy searchsorted method is only ~1.4 us per loop.

            Args:
              x: value(s) whose bin to find
              use_kev: check bin_edges_kev if True, bin_edges_raw if False, or
                decide based on self.is_calibrated if None

            Raises:
              UncalibratedError: if use_kev=True but Spectrum is not calibrated
              SpectrumError: if x is outside the bin edges or equal to up edge

            Returns:
              The integer bin index or indices containing x
        */
        public int find_bin_index(double x)
        {
            (double[] bin_edges, double[] bin_widths, _) = this.get_bin_properties();
            if (x < bin_edges[0])
            {
                throw new SpectrumError("requested x is < lowest bin edge");
            }
            if (x > bin_edges[bin_edges.Length - 1])
            {
                throw new SpectrumError("requested x is >= highest bin edge");
            }
            int retval = 0;
            for (int i = 1; i <= bin_edges.Length; i++)
            {
                if (x >= bin_edges[i - 1] && x < bin_edges[i])
                {
                    retval = i - 1;
                    break;
                }
            }
            return retval;
        }

        public (double[] bin_edges_raw, double[] bin_widths_raw, double[] bin_centers_raw) get_bin_properties()
        {
            return (this.bin_edges_raw, this.bin_widths_raw, this.bin_centers_raw);
        }

        public void combine_bins(int mul)
        {
            int new_size = this.counts.Length / mul;

            if (new_size == 0)
            {
                return;
            }

            double[] _counts = new double[new_size];
            double[] _bin_edges_raw = new double[new_size + 1]; //*mul
            double[] _bin_centers_raw = new double[new_size + 1];
            double[] _bin_widths_raw = new double[new_size + 1];

            for (int i = 0; i < new_size; i++)
            {
                for (int j = 0; j < mul; j++)
                {
                    _counts[i] += this.counts[mul * i + j];
                }
            }
            this.counts = _counts;

            for (int i = 0; i < new_size + 1; i++)
            {
                _bin_edges_raw[i] = mul * this.bin_edges_raw[i];
            }
            this.bin_edges_raw = _bin_edges_raw;

            for (int i = 0; i < new_size; i++)
            {
                _bin_centers_raw[i] = (_bin_edges_raw[i + 1] + _bin_edges_raw[i]) / 2.0;
                _bin_widths_raw[i] = _bin_edges_raw[i + 1] - _bin_edges_raw[i];
            }
            this.bin_centers_raw = _bin_centers_raw;
            this.bin_widths_raw = _bin_widths_raw;
        }
    }
}
