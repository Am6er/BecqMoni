namespace BecquerelMonitor
{
    public enum PeakSearchOrigin
    {
        FWHMPeakFinder,
        RJMCMC
    }

    public class Peak
    {
        public double Energy
        {
            get
            {
                return this.energy;
            }
            set
            {
                this.energy = value;
            }
        }

        public double SNR
        {
            get
            {
                return this.snr;
            }
            set
            {
                this.snr = value;
            }
        }

        public double FWHM
        {
            get
            {
                return this.fwhm;
            }
            set
            {
                this.fwhm = value;
            }
        }

        public double FWHM_DELTA
        {
            get
            {
                return this.fwhm_delta;
            }
            set
            {
                this.fwhm_delta = value;
            }
        }

        public int Channel
        {
            get
            {
                return this.channel;
            }
            set
            {
                this.channel = value;
            }
        }

        public NuclideDefinition Nuclide
        {
            get
            {
                return this.nuclide;
            }
            set
            {
                this.nuclide = value;
            }
        }

        public int Count
        {
            get
            {
                return this.count;
            }
            set
            {
                this.count = value;
            }
        }

        public int LeftChannel
        {
            get
            {
                return this.leftChannel;
            }
            set
            {
                this.leftChannel = value;
            }
        }

        public int RightChannel
        {
            get
            {
                return this.rightChannel;
            }
            set
            {
                this.rightChannel = value;
            }
        }

        public PeakSearchOrigin PeakSearchOrigin
        {
            get
            {
                return this.peakSearchOrigin;
            }
            set
            {
                this.peakSearchOrigin = value;
            }
        }

        public PeakDeconvolutionInfo DeconvolutionInfo
        {
            get
            {
                return this.deconvolutionInfo;
            }
            set
            {
                this.deconvolutionInfo = value;
            }
        }

        double energy;

        int channel;

        NuclideDefinition nuclide;

        int count;

        int leftChannel;

        int rightChannel;

        double snr;

        double fwhm;

        double fwhm_delta;

        PeakSearchOrigin peakSearchOrigin;

        PeakDeconvolutionInfo deconvolutionInfo;
    }
}
