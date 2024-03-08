using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BecquerelMonitor
{
    public class CountRate
    {
        public CountRate(int counts_, double elapsedtime_)
        {
            this.counts = counts_;
            this.elapsedtime = elapsedtime_;
        }

        public int Counts
        {
            get
            {
                return this.counts;
            }
            set
            {
                this.counts = value;
            }
        }

        public double ElapsedTimeInMs
        {
            get
            {
                return this.elapsedtime;
            }
            set
            {
                this.elapsedtime = value;
            }
        }

        int counts;
        double elapsedtime;
    }
}
