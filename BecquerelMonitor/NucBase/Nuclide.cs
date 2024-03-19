using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BecquerelMonitor.NucBase
{
    public class Nuclide
    {
        public int Z
        {
            get
            {
                return this.z;
            }
            set
            {
                this.z = value;
            }
        }

        public int N
        {
            get
            {
                return this.n;
            }
            set
            {
                this.n = value;
            }
        }

        public string HalfLife
        {
            get
            {
                return this.half_life;
            }
            set
            {
                this.half_life = value;
            }
        }

        public string HalfLifeUOM
        {
            get
            {
                return this.half_life_unit;
            }
            set
            {
                this.half_life_unit = value;
            }
        }

        public List<Decay> Parents
        {
            get
            {
                return this.parents;
            }
            set
            {
                this.parents = value;
            }
        }

        public List<Decay> Daughters
        {
            get
            {
                return this.daughters;
            }
            set
            {
                this.daughters = value;
            }
        }

        public double SpecialActivity
        {
            get
            {
                double retvalue = 0.0;
                if (this.z + this.n != 0 && this.half_life_sec != 0)
                {
                    double activity = 0.693 / ((this.z + this.n) * this.half_life_sec);
                    retvalue = ((activity * 6.02214076E+23) / 9.9999999965E-4) / 1000.0;
                }
                return retvalue;
            }
        }

        public double HalfLife_Sec
        {
            get
            {
                return this.half_life_sec;
            }
            set
            {
                this.half_life_sec = value;
            }
        }

        public double Abundance
        {
            get
            {
                return this.abundance;
            }
            set
            {
                this.abundance = value;
            }
        }

        int z;
        int n;
        string half_life;
        string half_life_unit;
        double activity;
        double half_life_sec = 0.0;
        double abundance;
        List<Decay> parents = new List<Decay>();
        List<Decay> daughters = new List<Decay>();
    }
}
