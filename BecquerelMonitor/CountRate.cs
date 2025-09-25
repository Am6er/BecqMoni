namespace BecquerelMonitor
{
    public class CountRate
    {
        public CountRate(long counts_, long invalidcounts_, double elapsedtime_)
        {
            this.counts = counts_;
            this.elapsedtime = elapsedtime_;
            this.invalidcounts = invalidcounts_;
        }

        public long Counts
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

        public long InvalidCounts
        {
            get
            {
                return this.invalidcounts;
            }
            set
            {
                this.invalidcounts = value;
            }
        }

        long counts;
        double elapsedtime;
        long invalidcounts;
    }
}
