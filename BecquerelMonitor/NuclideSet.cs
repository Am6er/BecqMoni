using System;

namespace BecquerelMonitor
{
    public class NuclideSet
    {
        public Guid Id
        {
            get
            {
                return this.id;
            }
            set
            {
                this.id = value;
            }
        }

        public string Name
        {
            get
            {
                return this.name;
            }
            set
            {
                this.name = value;
            }
        }

        public bool HideUnknownPeaks
        {
            get
            {
                return this.hideUnknownPeaks;
            }
            set
            {
                this.hideUnknownPeaks = value;
            }
        }

        Guid id = Guid.Empty;
        string name = "";
        bool hideUnknownPeaks = false;
    }
}
