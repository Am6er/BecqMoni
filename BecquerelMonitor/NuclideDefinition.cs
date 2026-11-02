using System;
using System.Globalization;
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
            return this.name + " - " + this.energy.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Линию ПОКАЗЫВАТЬ ЧЕЛОВЕКУ: рисовать на графике и подписывать ею пик.
        /// Поле одно на оба действия сознательно (решение Amber 08.08.2026,
        /// S31) — они и означают одно и то же: «эту линию я хочу видеть».
        ///
        /// Правило, чтобы читатели не разошлись снова:
        ///
        ///  * <b>показ и подпись</b> — по этому полю. Его читают
        ///    `EnergySpectrumView` (рисование) и `PeakDetector.MatchNuclide`
        ///    (подпись найденного пика);
        ///  * <b>кривая эффективности</b> — тоже по нему, и это не отдельное
        ///    решение, а следствие: точка кривой берётся из площади
        ///    ПОДПИСАННОГО пика, а неподписанного пика не бывает;
        ///  * <b>состав образа в полноспектральном разборе</b> — НЕ по нему.
        ///    Линия, снятая с показа, из спектра не исчезла, и образ без неё
        ///    занижен. `FsaLibrary` собирает линии компонента, не глядя на
        ///    видимость, и поставочный конфиг на это опирается: недостающие
        ///    линии нуклида дописаны туда скрытыми записями нарочно — чтобы
        ///    образ был полон, а график чист.
        ///
        /// ⚠ Отсюда ловушка, и она настоящая: компонент попадает в разбор,
        /// только если поиск пиков ХОТЬ ОДИН пик подписал этим нуклидом.
        /// Спрятав ЕДИНСТВЕННУЮ линию, которой он подписывался, нуклид убирают
        /// из разложения целиком — вместе со всеми его скрытыми линиями. Это
        /// поведение задумано (спрятал — значит не хочу), но заметить его
        /// глазами нельзя, поэтому оно измеряется пробой, а не описывается.
        /// </summary>
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
        /// ⛔ (`AMBER195`, решение Amber 05.10.2026 вопросником: «Ряд задан →
        /// период корня») Период, по которому линию ПОПРАВЛЯЮТ НА РАСПАД — к
        /// дате отбора (зона, панель выделения), в годах поля
        /// <see cref="HalfLife"/>.
        ///
        /// У линии с заполненным рядом (<see cref="Chain"/>) выход дан на
        /// распад КОРНЯ, и в равновесии она убывает с его периодом: Bi-214 в
        /// ряду Ra-226 — 1600 лет, а не свои 19.9 мин. Прежде бралось
        /// собственное <see cref="HalfLife"/> — импорт из NucBase с рядом пишет
        /// туда период самого излучателя, — и множитель `2^(Δt/T½)` за 2 ч
        /// между отбором и набором был ×65, за сутки 4.7·10²¹, дальше
        /// <c>Infinity</c>. Собственный период линии при этом НЕ меняется
        /// (решение то же): он остаётся периодом излучателя.
        ///
        /// Откуда период корня, по порядку: база нуклидов (`nucdb`,
        /// <see cref="NucBase.NucBaseFramework.HalfLifeYearsOf"/>) — она
        /// поставляется с приложением и знает всякий корень; затем линия
        /// САМОГО корня в библиотеке <paramref name="library"/> (если базы нет).
        /// Не нашёлся корень нигде, ряд не разбирается в нуклид («Cs-137
        /// (фильтр)»), ряд — сам нуклид линии, ряда нет — собственный период,
        /// как прежде. Линии без ряда (поставочные Bi-214/Pb-214) — как прежде.
        /// </summary>
        public static double DecayHalfLifeYears(NuclideDefinition line, IEnumerable<NuclideDefinition> library)
        {
            if (line == null)
            {
                return 0.0;
            }

            string chain = (line.Chain ?? "").Trim();
            if (chain.Length == 0)
            {
                return line.HalfLife;
            }

            string root = FullSpectrumAnalysis.FsaSampleLibrary.NucidOf(chain);
            if (root.Length == 0
                || root == FullSpectrumAnalysis.FsaSampleLibrary.NucidOf(line.NuclideName))
            {
                return line.HalfLife;
            }

            double years = NucBase.NucBaseFramework.HalfLifeYearsOf(root);
            if (years > 0.0)
            {
                return years;
            }

            if (library != null)
            {
                foreach (NuclideDefinition other in library)
                {
                    if (other != null && other.HalfLife > 0.0
                        && FullSpectrumAnalysis.FsaSampleLibrary.NucidOf(other.NuclideName) == root)
                    {
                        return other.HalfLife;
                    }
                }
            }

            return line.HalfLife;
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

        /// <summary>
        /// ⛔ (`AMBER281`, 02.11.2026) НОМЕР СТРОКИ БАЗЫ, ИЗ КОТОРОЙ ВЗЯТА ЛИНИЯ, —
        /// `decay_radiations.dr_pk` базы нуклидов. Amber, дословно: «в изотоп добавляй
        /// строку из базы (индекс) чтобы быть точно уверенным в будущем». Пишет его импорт
        /// «Базы изотопов», а у линий без номера — наполнение при открытии окна библиотеки
        /// (<see cref="NuclideDbLink.Refresh"/>). 0 — номера нет. В файл пишется только при
        /// наличии (<see cref="ShouldSerializeDbRow"/>): старые файлы читаются как прежде.
        /// </summary>
        public long DbRow { get; set; }

        /// <summary>(`AMBER281`) Писать ли <see cref="DbRow"/> в файл: только при наличии.</summary>
        public bool ShouldSerializeDbRow()
        {
            return this.DbRow > 0;
        }

        /// <summary>
        /// (`AMBER281`) Ссылка линии рентгена ЭЛЕМЕНТА на таблицу рентгена `matdb`
        /// («W:KA1»): строки распада у неё нет (<see cref="NuclideDbLink.XrayReference"/>).
        /// Пусто — ссылки нет; в файл пишется только при наличии.
        /// </summary>
        public string DbXray
        {
            get { return this.dbXray; }
            set { this.dbXray = value ?? ""; }
        }

        /// <summary>(`AMBER281`) Писать ли <see cref="DbXray"/> в файл: только при наличии.</summary>
        public bool ShouldSerializeDbXray()
        {
            return this.dbXray.Length > 0;
        }

        /// <summary>(`AMBER281`) Линия взята из базы: поля из базы правке не подлежат.</summary>
        [XmlIgnore]
        public bool IsDbLinked
        {
            get { return this.DbRow > 0 || this.dbXray.Length > 0; }
        }

        /// <summary>(`AMBER281`) Метка сверки с базой (<see cref="NuclideDbLink.Refresh"/>); в файл не пишется.</summary>
        [XmlIgnore]
        public NuclideDbMark DbMark { get; set; }

        /// <summary>(`AMBER281`) Причина метки словами — подсказка в окне библиотеки.</summary>
        [XmlIgnore]
        public string DbMarkText { get; set; }

        string dbXray = "";
    }

    /// <summary>(`AMBER281`) Метка линии библиотеки по сверке с базой.</summary>
    public enum NuclideDbMark
    {
        /// <summary>Линия сверена со своей строкой базы (или ещё не сверялась).</summary>
        None = 0,

        /// <summary>Строки базы у линии нет: вбита руками, строка снята, найти нельзя — красный «!».</summary>
        Missing = 1,

        /// <summary>Строка базы по номеру есть, но её энергия, выход или период другие — «Обновить из базы».</summary>
        Changed = 2,
    }

    /// <summary>
    /// ⛔ (`AMBER281`, 02.11.2026) СВЕРКА ЛИНИЙ БИБЛИОТЕКИ С БАЗОЙ НУКЛИДОВ. Постановка Amber,
    /// дословно: «подсвечивать красным знаком восклицательным те нуклиды из библиотеки
    /// нуклидов, которые были вбиты руками, а не взяты из базы»; вопросником того же дня —
    /// наполнение: «Если нет номера строки прописанного: запустить проверку. Проверка по
    /// энергии периоду полураспада и родителю (некоторых полей может и не быть в самой
    /// базе - родителя например быть не может)»; правка: «Запретить правку импортов из
    /// базы»; строка базы изменилась или исчезла: «Отдельная метка и «Обновить»».
    ///
    /// НАПОЛНЕНИЕ линии без номера (допуски — по разбору копии рабочей библиотеки,
    /// журнал `AMBER281`):
    /// <list type="bullet">
    /// <item>родитель — излучатель, имя до первого пробела («Bi-214 (Ra-226)» → 214BI,
    /// изомер «Pa-234m1» → 234PAm1); ряд в сверке не участвует; у рентгена элемента
    /// (имя без массового числа) родителя нет — он ищется в таблице рентгена matdb;</item>
    /// <item>энергия — сначала строка той же энергии (± <see cref="ExactKev"/>); нет — ровно
    /// одна строка родителя в ± <see cref="WindowKev"/> (наибольший сдвиг правки LNHB
    /// 02.11.2026 на линиях библиотеки — 0.059 кэВ, у Pa-234m 766.42 → 766.361);</item>
    /// <item>период — в пределах <see cref="HalfLifeTolerance"/> (отделяет уровни: Pa-234
    /// 6.70 ч и Pa-234m 1.159 мин); пересчёт в годы — тот же, что у импорта
    /// (<c>NucBase.HalfLifeYearsFromCell</c>); у строки без периода (рентген) — не сверяется.</item>
    /// </list>
    /// Одна строка — номер записывается; ноль или несколько — номера нет, метка
    /// <see cref="NuclideDbMark.Missing"/> с причиной.
    ///
    /// СВЕРКА линии с номером: строки нет — <see cref="NuclideDbMark.Missing"/>; энергия,
    /// период или выход другие — <see cref="NuclideDbMark.Changed"/> «было → стало». Выход
    /// сравнивается с выходом строки × доля ветвления ряда — так его пишет импорт с рядом
    /// (Tl-208 в ряду Th-232 — × 0.3594), с допуском <see cref="IntensityTolerance"/>.
    /// </summary>
    public static class NuclideDbLink
    {
        /// <summary>Энергия «та же», кэВ: импорт пишет энергию строки как есть.</summary>
        public const double ExactKev = 0.0005;

        /// <summary>Окно поиска строки родителя при наполнении, кэВ.</summary>
        public const double WindowKev = 0.1;

        /// <summary>Допуск периода при наполнении, доля.</summary>
        public const double HalfLifeTolerance = 0.01;

        /// <summary>Допуск выхода в метке «строка изменилась», доля.</summary>
        public const double IntensityTolerance = 0.01;

        /// <summary>Ссылка рентгена элемента: «символ:серия» («W:KA1»).</summary>
        public static string XrayReference(string symbol, string series)
        {
            return (symbol ?? "").Trim() + ":" + (series ?? "").Trim();
        }

        /// <summary>Итог сверки: сколько линий получили номер и сколько с какой меткой.</summary>
        public sealed class Outcome
        {
            public int Lines;
            public int Filled;
            public int Linked;
            public int Missing;
            public int Changed;
            public readonly Dictionary<string, int> Reasons = new Dictionary<string, int>(StringComparer.Ordinal);

            internal void Note(string reasonKey)
            {
                int n;
                this.Reasons.TryGetValue(reasonKey, out n);
                this.Reasons[reasonKey] = n + 1;
            }
        }

        /// <summary>
        /// Наполнить номера линий без номера и сверить все линии с базой; метки — в
        /// <see cref="NuclideDefinition.DbMark"/> и <see cref="NuclideDefinition.DbMarkText"/>.
        /// Номер, найденный наполнением, записывается в линию (в файл — со следующим
        /// сохранением библиотеки). Зовёт окно библиотеки при открытии.
        /// </summary>
        public static Outcome Refresh(IEnumerable<NuclideDefinition> lines)
        {
            Outcome outcome = new Outcome();
            NucBase.NucBaseFramework fw = new NucBase.NucBaseFramework();
            Dictionary<string, Dictionary<string, double>> chains = new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
            foreach (NuclideDefinition line in lines)
            {
                if (line == null)
                {
                    continue;
                }

                outcome.Lines++;
                CheckLine(line, fw, chains, outcome);
                if (line.DbMark == NuclideDbMark.Missing)
                {
                    outcome.Missing++;
                }
                else if (line.DbMark == NuclideDbMark.Changed)
                {
                    outcome.Changed++;
                }

                if (line.IsDbLinked)
                {
                    outcome.Linked++;
                }
            }

            return outcome;
        }

        /// <summary>
        /// «Обновить из базы»: энергия, выход (× доля ветвления ряда) и период — из строки
        /// по номеру. false — строки нет или база не прочиталась (<paramref name="error"/>).
        /// </summary>
        public static bool UpdateFromDb(NuclideDefinition line, out string error)
        {
            error = null;
            if (line == null || !line.IsDbLinked)
            {
                error = Text("NucLib_DbNotLinked", "the line has no database row.");
                return false;
            }

            NucBase.NucBaseFramework fw = new NucBase.NucBaseFramework();
            if (line.DbRow > 0)
            {
                NucBase.DecayRad row = fw.GetDecayRadByRow(line.DbRow);
                if (row == null)
                {
                    error = fw.LastError ?? string.Format(CultureInfo.InvariantCulture,
                        Text("NucLib_DbGone", "database row #{0} is no longer in the nuclide database."), line.DbRow);
                    return false;
                }

                double factor = ChainFactor(line, row.Name, fw, new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase));
                line.Energy = row.Energy;
                line.HalfLife = Years(row);
                if (factor > 0.0)
                {
                    line.Intencity = row.Intensity * factor;
                }
            }
            else
            {
                NucBase.DecayRad x = XrayRow(line, fw);
                if (x == null)
                {
                    error = string.Format(CultureInfo.InvariantCulture,
                        Text("NucLib_DbXrayGone", "X-ray line {0} is no longer in the material database."), line.DbXray);
                    return false;
                }

                line.Energy = x.Energy;
                line.Intencity = x.Intensity;
            }

            line.DbMark = NuclideDbMark.None;
            line.DbMarkText = "";
            return true;
        }

        static void CheckLine(NuclideDefinition line, NucBase.NucBaseFramework fw,
                              Dictionary<string, Dictionary<string, double>> chains, Outcome outcome)
        {
            line.DbMark = NuclideDbMark.None;
            line.DbMarkText = "";
            if (!line.IsDbLinked)
            {
                string reasonKey, reason;
                if (!Fill(line, fw, out reasonKey, out reason))
                {
                    line.DbMark = NuclideDbMark.Missing;
                    line.DbMarkText = reason;
                    outcome.Note(reasonKey);
                    return;
                }

                outcome.Filled++;
            }

            List<string> changes = new List<string>();
            if (line.DbRow > 0)
            {
                NucBase.DecayRad row = fw.GetDecayRadByRow(line.DbRow);
                if (row == null)
                {
                    line.DbMark = NuclideDbMark.Missing;
                    line.DbMarkText = fw.LastError != null
                        ? Text("NucLib_DbUnreadable", "the nuclide database could not be read: ") + fw.LastError
                        : string.Format(CultureInfo.InvariantCulture,
                            Text("NucLib_DbGone", "database row #{0} is no longer in the nuclide database."), line.DbRow);
                    outcome.Note(fw.LastError != null ? "NucLib_DbUnreadable" : "NucLib_DbGone");
                    return;
                }

                if (Math.Abs(row.Energy - line.Energy) > ExactKev)
                {
                    changes.Add(string.Format(CultureInfo.InvariantCulture, Text("NucLib_DbChangedEnergy", "energy {0} → {1} keV"),
                        line.Energy.ToString("R", CultureInfo.InvariantCulture), row.Energy.ToString("R", CultureInfo.InvariantCulture)));
                }

                double years = Years(row);
                if (years > 0.0 && Math.Abs(years - line.HalfLife) > 1e-6 * years)
                {
                    changes.Add(string.Format(CultureInfo.InvariantCulture, Text("NucLib_DbChangedHalfLife", "half-life {0} → {1} y"),
                        line.HalfLife.ToString("G6", CultureInfo.InvariantCulture), years.ToString("G6", CultureInfo.InvariantCulture)));
                }

                double factor = ChainFactor(line, row.Name, fw, chains);
                double expected = row.Intensity * factor;
                if (factor > 0.0 && expected > 0.0 && Math.Abs(line.Intencity / expected - 1.0) > IntensityTolerance)
                {
                    changes.Add(string.Format(CultureInfo.InvariantCulture, Text("NucLib_DbChangedIntensity", "yield {0} → {1} %"),
                        line.Intencity.ToString("G6", CultureInfo.InvariantCulture), expected.ToString("G6", CultureInfo.InvariantCulture)));
                }

                if (changes.Count > 0)
                {
                    line.DbMark = NuclideDbMark.Changed;
                    line.DbMarkText = string.Format(CultureInfo.InvariantCulture,
                        Text("NucLib_DbChanged", "Database row {0} has changed: {1}. Press «Update from database»."),
                        "#" + line.DbRow.ToString(CultureInfo.InvariantCulture), string.Join("; ", changes.ToArray()));
                    outcome.Note("NucLib_DbChanged");
                }

                return;
            }

            NucBase.DecayRad x = XrayRow(line, fw);
            if (x == null)
            {
                line.DbMark = NuclideDbMark.Missing;
                line.DbMarkText = string.Format(CultureInfo.InvariantCulture,
                    Text("NucLib_DbXrayGone", "X-ray line {0} is no longer in the material database."), line.DbXray);
                outcome.Note("NucLib_DbXrayGone");
                return;
            }

            if (Math.Abs(x.Energy - line.Energy) > ExactKev)
            {
                changes.Add(string.Format(CultureInfo.InvariantCulture, Text("NucLib_DbChangedEnergy", "energy {0} → {1} keV"),
                    line.Energy.ToString("R", CultureInfo.InvariantCulture), x.Energy.ToString("R", CultureInfo.InvariantCulture)));
            }

            if (x.Intensity > 0.0 && Math.Abs(line.Intencity / x.Intensity - 1.0) > IntensityTolerance)
            {
                changes.Add(string.Format(CultureInfo.InvariantCulture, Text("NucLib_DbChangedIntensity", "yield {0} → {1} %"),
                    line.Intencity.ToString("G6", CultureInfo.InvariantCulture), x.Intensity.ToString("G6", CultureInfo.InvariantCulture)));
            }

            if (changes.Count > 0)
            {
                line.DbMark = NuclideDbMark.Changed;
                line.DbMarkText = string.Format(CultureInfo.InvariantCulture,
                    Text("NucLib_DbChanged", "Database row {0} has changed: {1}. Press «Update from database»."),
                    line.DbXray, string.Join("; ", changes.ToArray()));
                outcome.Note("NucLib_DbChanged");
            }
        }

        /// <summary>Наполнение одной линии (правило — в шапке класса). false — номера нет, причина словами.</summary>
        static bool Fill(NuclideDefinition line, NucBase.NucBaseFramework fw, out string reasonKey, out string reason)
        {
            reasonKey = null;
            reason = null;
            if (line.IsElementXray)
            {
                string symbol = line.NuclideName;
                List<NucBase.DecayRad> xrays = fw.GetFluorescence(symbol) ?? new List<NucBase.DecayRad>();
                NucBase.DecayRad pick = Pick(xrays, line.Energy, out reasonKey);
                if (pick == null)
                {
                    reasonKey = reasonKey == "ambiguous" ? "NucLib_DbAmbiguous" : "NucLib_DbNoXray";
                    reason = string.Format(CultureInfo.InvariantCulture,
                        Text(reasonKey, reasonKey == "NucLib_DbAmbiguous"
                            ? "{0}: more than one database line within ±{2} keV of {1} keV — cannot tell which one."
                            : "{0}: no X-ray line of the element within ±{2} keV of {1} keV in the material database — entered by hand?"),
                        symbol, line.Energy.ToString("R", CultureInfo.InvariantCulture), WindowKev.ToString("R", CultureInfo.InvariantCulture));
                    return false;
                }

                line.DbXray = XrayReference(symbol, pick.XrayType);
                return true;
            }

            string nucid = FullSpectrumAnalysis.FsaSampleLibrary.NucidOf(line.NuclideName);
            List<NucBase.DecayRad> rows = nucid.Length > 0 ? fw.getDecayRad(nucid) : new List<NucBase.DecayRad>();
            if (rows == null)
            {
                reasonKey = "NucLib_DbUnreadable";
                reason = Text(reasonKey, "the nuclide database could not be read: ") + fw.LastError;
                return false;
            }

            if (rows.Count == 0)
            {
                reasonKey = "NucLib_DbNoParent";
                reason = string.Format(CultureInfo.InvariantCulture,
                    Text(reasonKey, "{0}: no such nuclide (or none of its lines) in the nuclide database — entered by hand?"),
                    line.NuclideName);
                return false;
            }

            string pickWhy;
            NucBase.DecayRad cand = Pick(rows, line.Energy, out pickWhy);
            if (cand == null)
            {
                reasonKey = pickWhy == "ambiguous" ? "NucLib_DbAmbiguous" : "NucLib_DbNoLine";
                reason = string.Format(CultureInfo.InvariantCulture,
                    Text(reasonKey, reasonKey == "NucLib_DbAmbiguous"
                        ? "{0}: more than one database line within ±{2} keV of {1} keV — cannot tell which one."
                        : "{0}: no database line within ±{2} keV of {1} keV — entered by hand?"),
                    line.NuclideName, line.Energy.ToString("R", CultureInfo.InvariantCulture), WindowKev.ToString("R", CultureInfo.InvariantCulture));
                return false;
            }

            double years = Years(cand);
            if (years > 0.0 && !(Math.Abs(years - line.HalfLife) <= HalfLifeTolerance * years))
            {
                reasonKey = "NucLib_DbHalfLife";
                reason = string.Format(CultureInfo.InvariantCulture,
                    Text(reasonKey, "{0} {1} keV: half-life {2} y differs from the database {3} y."),
                    line.NuclideName, line.Energy.ToString("R", CultureInfo.InvariantCulture),
                    line.HalfLife.ToString("G6", CultureInfo.InvariantCulture), years.ToString("G6", CultureInfo.InvariantCulture));
                return false;
            }

            line.DbRow = cand.DrPk;
            return line.DbRow > 0;
        }

        /// <summary>Строка той же энергии, иначе единственная в окне; why: «ambiguous» или «none».</summary>
        static NucBase.DecayRad Pick(List<NucBase.DecayRad> rows, double energy, out string why)
        {
            why = "none";
            List<NucBase.DecayRad> exact = rows.FindAll(r => Math.Abs(r.Energy - energy) <= ExactKev);
            if (exact.Count == 1)
            {
                return exact[0];
            }

            if (exact.Count > 1)
            {
                why = "ambiguous";
                return null;
            }

            List<NucBase.DecayRad> near = rows.FindAll(r => Math.Abs(r.Energy - energy) <= WindowKev);
            if (near.Count == 1)
            {
                return near[0];
            }

            if (near.Count > 1)
            {
                why = "ambiguous";
            }

            return null;
        }

        static NucBase.DecayRad XrayRow(NuclideDefinition line, NucBase.NucBaseFramework fw)
        {
            int colon = line.DbXray.IndexOf(':');
            if (colon <= 0)
            {
                return null;
            }

            string symbol = line.DbXray.Substring(0, colon);
            string series = line.DbXray.Substring(colon + 1);
            List<NucBase.DecayRad> xrays = fw.GetFluorescence(symbol) ?? new List<NucBase.DecayRad>();
            return xrays.Find(x => string.Equals(x.XrayType, series, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Период строки в годах — тем же пересчётом, что у импорта.</summary>
        static double Years(NucBase.DecayRad row)
        {
            return NucBase.NucBase.HalfLifeYearsFromCell(
                row.HalfLife.ToString(CultureInfo.InvariantCulture) + "(" + row.HalfLifeUnit + ")");
        }

        /// <summary>
        /// Доля ветвления ряда «корень → излучатель», как у импорта с рядом
        /// (<c>NucBaseFramework.GetChainBranches</c>); без ряда или ряд — сам излучатель — 1;
        /// излучателя в ряду нет — 0 (выход тогда не сверяется).
        /// </summary>
        static double ChainFactor(NuclideDefinition line, string emitter, NucBase.NucBaseFramework fw,
                                  Dictionary<string, Dictionary<string, double>> chains)
        {
            string root = FullSpectrumAnalysis.FsaSampleLibrary.NucidOf((line.Chain ?? "").Trim());
            if (root.Length == 0 || string.Equals(root, emitter, StringComparison.OrdinalIgnoreCase))
            {
                return 1.0;
            }

            Dictionary<string, double> branches;
            if (!chains.TryGetValue(root, out branches))
            {
                branches = fw.GetChainBranches(root) ?? new Dictionary<string, double>();
                chains[root] = branches;
            }

            double factor;
            return branches.TryGetValue(emitter, out factor) ? factor : 0.0;
        }

        /// <summary>Текст из ресурсов окна библиотеки (en первичен, ru вторым), без ключа — английский.</summary>
        static string Text(string key, string fallback)
        {
            try
            {
                string text = new System.ComponentModel.ComponentResourceManager(typeof(NuclideDefinitionForm)).GetString(key);
                return string.IsNullOrEmpty(text) ? fallback : text;
            }
            catch (System.Resources.MissingManifestResourceException)
            {
                return fallback;
            }
        }
    }
}
