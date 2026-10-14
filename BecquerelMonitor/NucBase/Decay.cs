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
                return ModeLabel(this.DecayType);
            }
        }

        /// <summary>
        /// Подпись кода распада — ОДНА на оба читателя (`Decay` — карточка, `DecayRad` —
        /// таблица линий).
        ///
        /// (`AMBER197`) Коды — расшифровка `database/scheme.md` («`decay_chain` и
        /// `l_decays`»): 1 — «ec β+», 6 — «β- n», 10 — «ec p». Прежде 1 и 10
        /// подписывались «IT», 6 — «β-».
        ///
        /// (П266, 14.10.2026) Таблица доведена до ВСЕХ 42 кодов той же расшифровки:
        /// прежде остальные печатались числом — у карточки «164TM → 164ER [15]» (β⁺),
        /// «214BI → 210PB [22]» (β⁻α), в таблице линий «10» у 12 родителей (`DecayRad`
        /// своего «ec p» не знал), «15» у 7, «12» у 8 (замер `DecayDataProbeP266`, раздел 7).
        /// Слова α, β±, EC, IT — прежние ресурсы, частицы и ядра — символами, как в
        /// расшифровке; код, которого в ней нет (58…69 — подпись без режима), — числом,
        /// как прежде.
        /// </summary>
        public static string ModeLabel(int code)
        {
            string a = Resources.NucBase_Alpha_Label;
            string bm = Resources.NucBase_BettaMinus_Label;
            string bp = Resources.NucBase_BettaPlus_Label;
            string ec = Resources.NucBase_EC;
            switch (code)
            {
                case 0: return a;
                case 1: return ec + " " + bp;
                case 2: return bm;
                case 3: return Resources.NucBase_IT;
                case 4: return "p";
                case 5: return "n";
                case 6: return bm + " n";
                case 7: return ec;
                case 8: return "SF";
                case 10: return ec + " p";
                case 12: return "βp";
                case 15: return bp;
                case 16: return ec + " " + a;
                case 17: return bm + " 2n";
                case 19: return "β + " + a;
                case 20: return "2" + bm;
                case 21: return "2p";
                case 22: return bm + " " + a;
                case 23: return "12C";
                case 24: return ec + " 2p";
                case 25: return bp + " 2p";
                case 26: return "2" + bp;
                case 27: return "28Mg";
                case 28: return ec + " SF";
                case 29: return bm + " 3n";
                case 30: return "2" + ec;
                case 31: return "24Ne";
                case 32: return ec + " F";
                case 33: return "Ne";
                case 34: return ec + " p " + ec + " 2p";
                case 36: return "34Si";
                case 38: return "24Ne";
                case 39: return "β fission";
                case 42: return bm + " 4n";
                case 44: return "SF " + ec + " " + bm;
                case 46: return ec + " 3p";
                case 47: return "20Ne";
                case 49: return bm + " F";
                case 50: return bp + " " + ec;
                case 52: return "Mg";
                case 55: return bm + " p";
                case 57: return "20Ne";
                default:
                    return code.ToString(CultureInfo.InvariantCulture);
            }
        }

        string nucname;
        int decay_type;
        string decay_percent;
    }
}
