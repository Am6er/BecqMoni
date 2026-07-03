using System.Collections.Generic;

namespace BecquerelMonitor.RjmcmcDeconvolution
{
    internal sealed class RjmcmcRoi
    {
        public int StartChannel { get; set; }
        public int EndChannel { get; set; }
        public List<int> AnchorChannels { get; private set; }
        public List<int> ReferenceAnchorChannels { get; private set; }

        public int Width
        {
            get
            {
                return EndChannel - StartChannel + 1;
            }
        }

        public RjmcmcRoi()
        {
            AnchorChannels = new List<int>();
            ReferenceAnchorChannels = new List<int>();
        }
    }
}
