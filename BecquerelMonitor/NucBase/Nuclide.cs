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

        int z;
        int n;
        string half_life;
        string half_life_unit;
        List<Decay> parents = new List<Decay>();
        List<Decay> daughters = new List<Decay>();
    }
}
