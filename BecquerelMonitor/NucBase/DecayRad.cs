using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BecquerelMonitor.NucBase
{
    public class DecayRad
    {
        public double Energy
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

        public double Intensity
        {
            get
            {
                return this.intensity;
            }
            set
            {
                this.intensity = value;
            }
        }

        public string DecayLine
        {
            get
            {
                return this.decay_line;
            }
            set
            {
                this.decay_line = value;
            }
        }

        public string XrayType
        {
            get
            {
                return this.xray_type;
            }
            set
            {
                this.xray_type = value;
            }
        }

        public int DecayType
        {
            get
            {
                return this.dectype;
            }
            set
            {
                this.dectype = value;
            }
        }

        /// <summary>
        /// Что писать в колонке типа распада, когда распада нет вовсе:
        /// характеристический рентген элемента (см. NucBaseFramework).
        /// Пусто — тип берётся из кода <see cref="DecayType"/>, как и был.
        /// </summary>
        public string DecayTypeText
        {
            get
            {
                return this.decayTypeText;
            }
            set
            {
                this.decayTypeText = value ?? "";
            }
        }

        public string DecayTypeString
        {
            get
            {
                if (this.decayTypeText.Length > 0)
                {
                    return this.decayTypeText;
                }

                switch (this.dectype)
                {
                    case 0:
                        return Resources.NucBase_Alpha_Label;
                    case 1:
                        return Resources.NucBase_EC + " " + Resources.NucBase_BettaPlus_Label;
                    case 2:
                        return Resources.NucBase_BettaMinus_Label;
                    case 3:
                        return Resources.NucBase_IT;
                    case 6:
                        return Resources.NucBase_BettaMinus_Label;
                    case 7:
                        return Resources.NucBase_IT;
                    default:
                        return this.dectype.ToString();
                }

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

        public double HalfLife
        {
            get
            {
                return this.halfLife;
            }
            set
            {
                this.halfLife = value;
            }
        }

        public string HalfLifeUnit
        {
            get
            {
                return this.halfLifeUnit;
            }
            set
            {
                this.halfLifeUnit = value;
            }
        }

        string name;
        double energy;
        double intensity;
        string decay_line;
        string xray_type;
        int dectype;
        double halfLife;
        string halfLifeUnit;
        string decayTypeText = "";
    }
}
