using System;
using System.Xml.Serialization;
using System.Drawing;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    // Token: 0x0200014F RID: 335
    public class NuclideDefinition : IComparable
    {
        // Token: 0x1700046A RID: 1130
        // (get) Token: 0x060010A4 RID: 4260 RVA: 0x0005AE84 File Offset: 0x00059084
        // (set) Token: 0x060010A5 RID: 4261 RVA: 0x0005AE8C File Offset: 0x0005908C
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

        // Token: 0x1700046B RID: 1131
        // (get) Token: 0x060010A6 RID: 4262 RVA: 0x0005AE98 File Offset: 0x00059098
        // (set) Token: 0x060010A7 RID: 4263 RVA: 0x0005AEA0 File Offset: 0x000590A0
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

        // Token: 0x1700046C RID: 1132
        // (get) Token: 0x060010A8 RID: 4264 RVA: 0x0005AEAC File Offset: 0x000590AC
        // (set) Token: 0x060010A9 RID: 4265 RVA: 0x0005AEB4 File Offset: 0x000590B4
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

        public SerializableColor NuclideColor
        {
            get
            {
                return this.nuclideColor;
            }
            set
            {
                this.nuclideColor = value;
            }
        }

        // Token: 0x1700046D RID: 1133
        // (get) Token: 0x060010AA RID: 4266 RVA: 0x0005AEC0 File Offset: 0x000590C0
        // (set) Token: 0x060010AB RID: 4267 RVA: 0x0005AEC8 File Offset: 0x000590C8
        public CDATA Note
        {
            get
            {
                return this.note;
            }
            set
            {
                this.note = value;
            }
        }

        // Token: 0x1700046E RID: 1134
        // (get) Token: 0x060010AC RID: 4268 RVA: 0x0005AED4 File Offset: 0x000590D4
        // (set) Token: 0x060010AD RID: 4269 RVA: 0x0005AEDC File Offset: 0x000590DC
        [XmlIgnore]
        public bool Dirty
        {
            get
            {
                return this.dirty;
            }
            set
            {
                this.dirty = value;
            }
        }

        // Token: 0x060010AE RID: 4270 RVA: 0x0005AEE8 File Offset: 0x000590E8
        public int CompareTo(object obj)
        {
            NuclideDefinition nuclideDefinition = (NuclideDefinition)obj;
            return this.Energy.CompareTo(nuclideDefinition.Energy);
        }

        // Token: 0x060010AF RID: 4271 RVA: 0x0005AF14 File Offset: 0x00059114
        public override string ToString()
        {
            return $"{this.name} - {this.energy}";
        }

        public bool Visible
        {
            get
            {
                return this.visible;
            }
            set
            {
                this.visible = value;
            }
        }

        public double Intencity
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

        public HashSet<Guid> Sets
        {
            get
            {
                return this.sets;
            }
            set
            {
                this.sets = value;
            }
        }

        /// <summary>
        /// Родитель ряда, на распад которого дан выход <see cref="Intencity"/>:
        /// «Ra-226» у линии Bi-214 из радиевого равновесия. Пусто — линия сама
        /// по себе, и выход дан на распад её собственного нуклида.
        ///
        /// До этого поля принадлежность линии к ряду жила ТОЛЬКО в хвосте имени
        /// («Bi-214 (Ra-226)»), и разбирали этот хвост порознь конструктор
        /// кривой и сборка библиотеки образов. Хвост никуда не делся — он
        /// подпись на графике, — но решает теперь поле.
        /// </summary>
        public string Chain
        {
            get
            {
                return this.chain;
            }
            set
            {
                this.chain = value ?? "";
            }
        }

        /// <summary>Имя без хвоста с рядом: «Bi-214 (Ra-226)» -&gt; «Bi-214».</summary>
        [XmlIgnore]
        public string NuclideName
        {
            get
            {
                return NuclideNameOf(this.name);
            }
        }

        /// <summary>
        /// Имя нуклида из подписи: всё до первого пробела. Разделение по
        /// пробелу, а не по скобке, — так это делалось и раньше в обоих местах
        /// разбора, и подписи без скобок («Ac-228 серия») читаются так же.
        /// </summary>
        public static string NuclideNameOf(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            int space = name.IndexOf(' ');
            return (space > 0 ? name.Substring(0, space) : name).Trim();
        }

        /// <summary>Подпись называет элемент, а не нуклид: «W», «Pb x-ray».</summary>
        [XmlIgnore]
        public bool IsElementXray
        {
            get
            {
                return IsElementXrayName(this.name);
            }
        }

        /// <summary>
        /// Подпись называет ЭЛЕМЕНТ, а не нуклид: «W», «Pb x-ray», «X-ray».
        ///
        /// Признак — отсутствие массового числа: у нуклида оно есть во всех
        /// принятых написаниях («137CS», «Cs137», «Cs-137»), у символа элемента
        /// его нет и быть не может. Второго источника правды заводить не стали:
        /// поле в файле пришлось бы проставлять задним числом всем, кто уже
        /// завёл рентген руками, а имя у таких записей и так одно и то же.
        ///
        /// Разница не косметическая. У линии нуклида <see cref="Intencity"/> —
        /// выход НА РАСПАД, по нему считается активность и строится кривая
        /// эффективности. У характеристического рентгена выход на распад не
        /// определён вовсе: атом светит, когда в K-оболочке появилась дырка, а
        /// сколько их — дело геометрии и спектра возбуждения. Число в этом поле
        /// у него значит лишь долю внутри K-серии. Поэтому такие линии идут
        /// мешающим образом со свободной амплитудой (FsaLibrary) и не участвуют
        /// в построении кривой эффективности (EfficiencyModel).
        /// </summary>
        public static bool IsElementXrayName(string name)
        {
            string token = NuclideNameOf(name);
            if (token.Length == 0)
            {
                return false;
            }

            foreach (char c in token)
            {
                if (c >= '0' && c <= '9')
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Родитель ряда из хвоста подписи: «Bi-214 (Ra-226)» -&gt; «Ra-226».
        /// Запасной источник — им заполняется <see cref="Chain"/> у файлов,
        /// заведённых до появления поля.
        /// </summary>
        public static string ChainOf(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            int open = name.IndexOf('(');
            int close = name.LastIndexOf(')');
            if (open < 0 || close <= open + 1)
            {
                return "";
            }

            return name.Substring(open + 1, close - open - 1).Trim();
        }

        // Token: 0x040009B1 RID: 2481
        string name = "";

        // Token: 0x040009B2 RID: 2482
        double energy;

        // Token: 0x040009B3 RID: 2483
        double halfLife = 1.0;

        // Token: 0x040009B4 RID: 2484
        CDATA note = "";

        // Token: 0x040009B5 RID: 2485
        bool dirty;

        bool visible = true;

        double intensity = 0;

        SerializableColor nuclideColor = Color.Gray;

        HashSet<Guid> sets = new HashSet<Guid>();

        string chain = "";
    }
}
