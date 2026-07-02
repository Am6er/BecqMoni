using System.Collections.Generic;

namespace BecquerelMonitor.RjmcmcDeconvolution
{
    internal sealed class RjmcmcResult
    {
        public List<RjmcmcPeakCandidate> ExtraCandidates { get; private set; }
        public List<RjmcmcRoi> ProcessedRois { get; private set; }

        public RjmcmcResult()
        {
            ExtraCandidates = new List<RjmcmcPeakCandidate>();
            ProcessedRois = new List<RjmcmcRoi>();
        }
    }
}
