using System;

namespace BecquerelMonitor
{
    public class CalibrationPoint : IComparable
    {
        public int Channel
        {
            get
            {
                return this.channel;
            }
            set
            {
                this.channel = value;
            }
        }

        public decimal Energy
        {
            get
            {
                return this.energy;
            }
            set
            {
                this.energy = value;
            }
        }

        public int Count
        {
            get
            {
                return this.count;
            }
            set
            {
                this.count = value;
            }
        }

        public CalibrationPoint(int channel, decimal energy, int count)
        {
            this.channel = channel;
            this.energy = energy;
            this.count = count;
        }

        public int CompareTo(object obj)
        {
            return this.channel.CompareTo(((CalibrationPoint)obj).Channel);
        }

        int channel;

        decimal energy;

        int count;
    }
}
