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

        /// <summary>
        /// ПЕРИОД ЗДЕСЬ — НЕ ИЗМЕРЕНИЕ, А НИЖНЯЯ ГРАНИЦА НЕИЗМЕРЕННОГО
        /// (<c>A304</c>): настоящий период БОЛЬШЕ записанного числа, насколько
        /// — неизвестно. У 81 строки <c>nuclides</c> из 4429 в поставке стоит
        /// круглое число (160, 200, 300 или 620 нс) с признаком
        /// <c>half_life_is_limit</c> = 1: в первоисточнике там стояло «&gt; 300
        /// ns», и знак «&gt;» при переносе потерян безвозвратно — ни одна из
        /// 4429 строк и ни одна из 35 220 строк независимой поставки
        /// <c>ensdf_levels</c> его не несёт.
        ///
        /// ⚠ Признак нужен ТОЛЬКО ПОКАЗУ (решения Amber 10.09.2026,
        /// вопросником: «Показывать „&gt; 0.3 мкс“ в NucBase» и «Ставить „&lt;“
        /// тем же признаком»). Ни одно ЧИСЛО он не меняет: физику разбора не
        /// трогает, <see cref="SpecialActivity"/> считается по прежней
        /// формуле — признак ставит перед ней ЗНАК, и только.
        ///
        /// ⛔ Знаков от признака ДВА, и они разные: «&gt;» у периода
        /// (<c>NucBaseFramework.HalfLifeCaption</c>) и «&lt;» у удельной
        /// активности (<c>NucBaseFramework.SpecificActivityCaption</c>) —
        /// период стоит в ЗНАМЕНАТЕЛЕ активности, поэтому граница снизу у
        /// одного есть граница сверху у другой.
        /// </summary>
        public bool HalfLifeIsLimit
        {
            get
            {
                return this.half_life_is_limit;
            }
            set
            {
                this.half_life_is_limit = value;
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
        double half_life_sec = 0.0;
        bool half_life_is_limit;
        double abundance;
        List<Decay> parents = new List<Decay>();
        List<Decay> daughters = new List<Decay>();
    }
}
