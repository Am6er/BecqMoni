using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BecquerelMonitor.FWHMPeakDetector
{
    public class PeakFinderWarning
    {
        public PeakFinderWarning(string ex)
        {
            throw new Exception(ex);
        }
    }
}
