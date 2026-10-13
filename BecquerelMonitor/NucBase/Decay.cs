using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BecquerelMonitor.NucBase
{
    public class Decay
    {
        public Decay()
        {

        }

        public string NucName
        {
            get
            {
                return this.nucname;
            }
            set
            {
                this.nucname = value;
            }
            
        }

        public int DecayType
        {
            get
            {
                return this.decay_type;
            }
            set
            {
                this.decay_type = value;
            }
        }

        public string DecayPercent
        {
            get
            {
                return this.decay_percent;
            }
            set
            {
                this.decay_percent = value;
            }
        }

        public string DecayTypeString
        {
            get
            {
                // (`AMBER197`) Коды — расшифровка `database/scheme.md`
                // («`decay_chain` и `l_decays`»): 1 — «ec β+», 6 — «β- n»,
                // 10 — «ec p». Прежде 1 и 10 подписывались «IT», 6 — «β-».
                switch (this.DecayType)
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
                        return Resources.NucBase_BettaMinus_Label + " n";
                    case 7:
                        return Resources.NucBase_EC;
                    case 10:
                        return Resources.NucBase_EC + " p";
                    default:
                        return this.decay_type.ToString(CultureInfo.InvariantCulture);
                }

            }
        }

        string nucname;
        int decay_type;
        string decay_percent;
    }
}
