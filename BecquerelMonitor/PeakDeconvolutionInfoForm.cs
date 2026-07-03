using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public partial class PeakDeconvolutionInfoForm : Form
    {
        public PeakDeconvolutionInfoForm(IEnumerable<Peak> peaks)
        {
            this.InitializeComponent();
            this.LoadPeaks(peaks ?? Enumerable.Empty<Peak>());
        }

        void LoadPeaks(IEnumerable<Peak> peaks)
        {
            this.dataGridViewDetails.Rows.Clear();

            foreach (Peak peak in peaks.OrderBy(item => item != null ? item.Energy : Double.MaxValue))
            {
                if (peak == null)
                {
                    continue;
                }

                PeakDeconvolutionInfo info = peak.DeconvolutionInfo;
                if (info == null)
                {
                    continue;
                }

                this.dataGridViewDetails.Rows.Add(
                    peak.Nuclide != null ? peak.Nuclide.Name : Resources.UnknownNuclide,
                    FormatMetric(peak.Energy, "0.00"),
                    peak.Channel.ToString(),
                    FormatMetric(peak.SNR, "0.0"),
                    FormatMetric(peak.FWHM, "0.0"),
                    FormatMetric(peak.FWHM_DELTA, "0.0"),
                    FormatMetric(info.DevianceImprovement, "0.###"),
                    FormatMetric(info.PosteriorOccupancy, "0.###"),
                    FormatMetric(info.CenterStdDev, "0.###"),
                    FormatMetric(info.ResidualSnr, "0.###"),
                    FormatMetric(info.ResidualCorrelation, "0.###"),
                    FormatMetric(info.AnchorDistanceFwhm, "0.###"),
                    info.SupportingChainCount.ToString());
            }
        }

        static string FormatMetric(double value, string format)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value)
                ? "n/a"
                : value.ToString(format);
        }
    }
}
