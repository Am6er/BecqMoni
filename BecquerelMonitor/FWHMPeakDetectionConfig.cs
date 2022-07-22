﻿namespace BecquerelMonitor
{
    public class FWHMPeakDetectionMethodConfig : PeakDetectionMethodConfig
    {
        
        public double Min_SNR
        {
            get
            {
                return this.min_snr;
            }
            set
            {
                this.min_snr = value;
            }
        }

        public double FWHM_AT_0
        {
            get
            {
                return this.fwhm_at_0;
            }
            set
            {
                this.fwhm_at_0 = value;
            }
        }

        public double Ch_Fwhm
        {
            get
            {
                return this.ch_fwhm;
            }
            set
            {
                this.ch_fwhm = value;
            }
        }

        public double Width_Fwhm
        {
            get
            {
                return this.width_fwhm;
            }
            set
            {
                this.width_fwhm = value;
            }
        }

        public int Max_Items
        {
            get
            {
                return this.max_items;
            }
            set
            {
                this.max_items = value;
            }
        }

        public double Tolerance
        {
            get
            {
                return this.tolerance;
            }
            set
            {
                this.tolerance = value;
            }
        }

        public double Min_Range
        {
            get
            {
                return this.min_range_en;
            }
            set
            {
                this.min_range_en = value;
            }
        }

        public double Max_Range
        {
            get
            {
                return this.max_range_en;
            }
            set
            {
                this.max_range_en = value;
            }
        }

        public FWHMPeakDetectionMethodConfig()
        {
        }

        public FWHMPeakDetectionMethodConfig(FWHMPeakDetectionMethodConfig config)
        {
            this.tolerance = config.tolerance;
            this.fwhm_at_0 = config.fwhm_at_0;
            this.ch_fwhm = config.ch_fwhm;
            this.width_fwhm = config.width_fwhm;
            this.min_snr = config.min_snr;
            this.max_items = config.max_items;
            this.min_range_en = config.min_range_en;
            this.max_range_en = config.max_range_en;
        }

        public override PeakDetectionMethodConfig Clone()
        {
            return new FWHMPeakDetectionMethodConfig(this);
        }

        double tolerance = 10.0;

        double fwhm_at_0 = 15.0;

        double ch_fwhm = 3756.0;

        double width_fwhm = 103;

        double min_snr = 10;

        int max_items = 40;

        double min_range_en = 30; //keV

        double max_range_en = 2800; //keV
    }
}
