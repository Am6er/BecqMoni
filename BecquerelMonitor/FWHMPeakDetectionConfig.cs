using System.Xml.Serialization;
using BecquerelMonitor.RjmcmcDeconvolution;

namespace BecquerelMonitor
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

        public decimal Min_FWHM_Tol
        {
            get
            {
                return this.min_fwhm_tol;
            }
            set
            {
                this.min_fwhm_tol = value;
            }
        }

        public decimal Max_FWHM_Tol
        {
            get
            {
                return this.max_fwhm_tol;
            }
            set
            {
                this.max_fwhm_tol = value;
            }
        }

        public bool Enabled
        {
            get
            {
                return this.enabled;
            }
            set
            {
                this.enabled = value;
            }
        }

        [XmlElement("use_deconvolution")]
        public bool UseDeconvolution
        {
            get
            {
                return this.use_deconvolution;
            }
            set
            {
                this.use_deconvolution = value;
            }
        }

        public int BurnIn
        {
            get
            {
                return this.burnIn;
            }
            set
            {
                this.burnIn = value;
            }
        }

        public int Samples
        {
            get
            {
                return this.samples;
            }
            set
            {
                this.samples = value;
            }
        }

        public int MaxRois
        {
            get
            {
                return this.maxRois;
            }
            set
            {
                this.maxRois = value;
            }
        }

        public int MaxExtraPeaksPerRoi
        {
            get
            {
                return this.maxExtraPeaksPerRoi;
            }
            set
            {
                this.maxExtraPeaksPerRoi = value;
            }
        }

        public double RoiRadiusFwhm
        {
            get
            {
                return this.roiRadiusFwhm;
            }
            set
            {
                this.roiRadiusFwhm = value;
            }
        }

        public double MinDevianceImprovement
        {
            get
            {
                return this.minDevianceImprovement;
            }
            set
            {
                this.minDevianceImprovement = value;
            }
        }

        public double MinimumCandidateAmplitude
        {
            get
            {
                return this.minimumCandidateAmplitude;
            }
            set
            {
                this.minimumCandidateAmplitude = value;
            }
        }

        public int Ch_Concat
        {
            get
            {
                return this.ch_concat;
            }
            set
            {
                this.ch_concat = value;
            }
        }

        // Максимальный коэффициент расширения окна оценки континуума (SNIP)
        // относительно модельного FWHM в сторону измеренной ширины пика, если
        // реальный пик оказывается шире модели. См. SpectrumAriphmetics.BuildSnipRadius.
        public double PeakWidthWidenFactor
        {
            get
            {
                return this.peak_width_widen_factor;
            }
            set
            {
                this.peak_width_widen_factor = value;
            }
        }

        [XmlElement(typeof(SimpleSqrtFwhmCalibration))]
        [XmlElement(typeof(SqrtFwhmCalibration))]
        public FwhmCalibration FwhmCalibration { get => fwhmCalibration; set => fwhmCalibration = value; }

        public FWHMPeakDetectionMethodConfig()
        {
            this.fwhmCalibration = FwhmCalibration.DefaultCalibration(this, new PolynomialEnergyCalibration());
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
            this.min_fwhm_tol = config.min_fwhm_tol;
            this.max_fwhm_tol = config.max_fwhm_tol;
            this.ch_concat = config.ch_concat;
            this.peak_width_widen_factor = config.peak_width_widen_factor;
            this.use_deconvolution = config.use_deconvolution;
            this.burnIn = config.burnIn;
            this.samples = config.samples;
            this.maxRois = config.maxRois;
            this.maxExtraPeaksPerRoi = config.maxExtraPeaksPerRoi;
            this.roiRadiusFwhm = config.roiRadiusFwhm;
            this.minDevianceImprovement = config.minDevianceImprovement;
            this.minimumCandidateAmplitude = config.minimumCandidateAmplitude;
            if (config.fwhmCalibration != null)
            {
                this.fwhmCalibration = config.fwhmCalibration.Clone();
            }
            else
            {
                this.fwhmCalibration = FwhmCalibration.DefaultCalibration(this, new PolynomialEnergyCalibration());
            }
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

        decimal min_fwhm_tol = 1;

        decimal max_fwhm_tol = 199;

        int ch_concat = 1024;

        double peak_width_widen_factor = 1.2;

        bool use_deconvolution = false;

        int burnIn = RjmcmcConfig.CreateDefault().BurnIn;

        int samples = RjmcmcConfig.CreateDefault().Samples;

        int maxRois = RjmcmcConfig.CreateDefault().MaxRois;

        int maxExtraPeaksPerRoi = RjmcmcConfig.CreateDefault().MaxExtraPeaksPerRoi;

        double roiRadiusFwhm = RjmcmcConfig.CreateDefault().RoiRadiusFwhm;

        double minDevianceImprovement = RjmcmcConfig.CreateDefault().MinDevianceImprovement;

        double minimumCandidateAmplitude = RjmcmcConfig.CreateDefault().MinimumCandidateAmplitude;

        bool enabled = true;

        FwhmCalibration fwhmCalibration = null;
    }
}
