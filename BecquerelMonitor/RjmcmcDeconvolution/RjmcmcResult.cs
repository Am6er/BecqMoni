using System.Collections.Generic;

namespace BecquerelMonitor.RjmcmcDeconvolution
{
    internal sealed class RjmcmcResult
    {
        public List<RjmcmcPeakCandidate> ExtraCandidates { get; private set; }
        public double LastRunElapsedMilliseconds { get; set; }
        public double AverageElapsedMillisecondsLast10Runs { get; set; }
        public int AverageElapsedMillisecondsSampleCount { get; set; }

        public RjmcmcResult()
        {
            ExtraCandidates = new List<RjmcmcPeakCandidate>();
        }
    }
}
