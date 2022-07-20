using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BecquerelMonitor.FWHMPeakDetector
{
    public class SpectrumError : Exception
    {
        public SpectrumError(string ex)
        {
            throw new Exception(ex);
        }
    }
}
