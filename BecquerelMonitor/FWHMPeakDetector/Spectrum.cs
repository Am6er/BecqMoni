using System;
using System.Threading.Tasks;

namespace BecquerelMonitor.FWHMPeakDetector
{
    public class Spectrum
    {
        public double[] counts;
        public double[] bin_edges_raw;
        public double[] bin_widths_raw;
        public double[] bin_centers_raw;

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

            // Store spectrum length in last channel
            this.bin_edges_raw[counts.Length] = Convert.ToDouble(counts.Length);

            Parallel.For(0, this.bin_centers_raw.Length - 1, i =>
            {
                this.bin_centers_raw[i] = (this.bin_edges_raw[i + 1] + this.bin_edges_raw[i]) / 2.0;
                this.bin_widths_raw[i] = this.bin_edges_raw[i + 1] - this.bin_edges_raw[i];
            });
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

            Raises:
              SpectrumError: if x is outside the bin edges or equal to up edge

            Returns:
              The integer bin index or indices containing x
        */
        public int find_bin_index(double x)
        {
            if (x < bin_edges_raw[0])
            {
                throw new SpectrumError("requested x is < lowest bin edge");
            }
            if (x > bin_edges_raw[bin_edges_raw.Length - 1])
            {
                throw new SpectrumError("requested x is >= highest bin edge");
            }
            int retval = 0;
            for (int i = 1; i <= bin_edges_raw.Length; i++)
            {
                if (x >= bin_edges_raw[i - 1] && x < bin_edges_raw[i])
                {
                    retval = i - 1;
                    break;
                }
            }
            return retval;
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
