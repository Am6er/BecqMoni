using BecquerelMonitor.EfficiencyMaker;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Одна цепочка в составе пробы: корень ряда и, при надобности, ограничение
    /// на его членов.
    ///
    /// <see cref="Only"/> нужен ровно для одного случая, и случай этот в корпусе
    /// есть: урановое стекло (`U-238u`). Ряд в нём ОБОРВАН на радии — уран в
    /// стекло попал химически очищенным, и равновесия ниже Ra-226 нет. Взять
    /// такому образцу весь ряд значит предъявить ему Bi-214 и Pb-214, которых в
    /// нём нет, и отдать им структуру. Список членов повторяет
    /// `build_corpus.sample_lines`, где то же самое сделано для калибровки.
    /// Пусто — брать весь ряд.
    /// </summary>
    public sealed class FsaSampleChain
    {
        public FsaSampleChain(string root)
        {
            this.Root = root;
        }

        public FsaSampleChain(string root, params string[] only)
        {
            this.Root = root;
            if (only != null)
            {
                foreach (string member in only)
                {
                    this.Only.Add(member);
                }
            }
        }

        /// <summary>`nucid` корня ряда: «232TH», «238U».</summary>
        public string Root;

        /// <summary>`nucid` членов, которыми ряд ограничен; пусто — весь ряд.</summary>
        public readonly HashSet<string> Only =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Что снято этим спектром и чем оно снято — вход
    /// <see cref="FsaSampleLibrary"/>.
    ///
    /// Заполняется тем, кто про спектр это знает: для корпуса — `manifest.csv`
    /// и `materials.csv` (проба `CorpusFsaProbe`), в приложении — объявленный
    /// состав документа. Сам сборщик ничего не угадывает.
    /// </summary>
    public sealed class FsaSampleSpec
    {
        /// <summary>Ряды в пробе.</summary>
        public readonly List<FsaSampleChain> Chains = new List<FsaSampleChain>();

        /// <summary>Одиночные нуклиды поверх рядов: `nucid`, «40K», «176LU».</summary>
        public readonly List<string> Nuclides = new List<string>();

        /// <summary>Z элементов ПРОБЫ — вольфрам электрода WT-20, лютеций Lu₂O₃.</summary>
        public readonly List<int> SampleElements = new List<int>();

        /// <summary>Z элементов КРИСТАЛЛА — иод и цезий у CsI, иод у NaI.</summary>
        public readonly List<int> CrystalElements = new List<int>();

        /// <summary>Z элементов ЗАЩИТЫ И ОБВЯЗКИ — свинец домика, железо корпуса.</summary>
        public readonly List<int> ShieldElements = new List<int>();

        /// <summary>Нижняя граница рабочего диапазона, кэВ.</summary>
        public double MinEnergyKev = 10.0;

        /// <summary>Верхняя граница рабочего диапазона, кэВ.</summary>
        public double MaxEnergyKev = 3200.0;

        /// <summary>
        /// Добавлять вездесущие K-40 / Th-232 / Ra-226 — NORM.
        ///
        /// ⛔ **Правило Amber 18.08.2026: «везде, где не знаешь, суй NORM — не из
        /// `NuclideDefinition`, а из базы».** То есть это не оговорка и не
        /// умолчание на всякий случай: незнание состава закрывается природными
        /// рядами, и берутся они оттуда же, откуда всё остальное, — из
        /// `nucdb.decay_chain` обходом от корней 232TH и 226RA плюс 40K. Ровно
        /// поэтому <see cref="RoomChains"/> — список `nucid`, а не имён
        /// компонентов конфига.
        ///
        /// «Снятым нуклидом» NORM не является, но физически в спектре есть, и
        /// без него этой структуре не найдётся имени. Второе следствие того же
        /// правила измерено: найденный ПОСЛЕ вычитания фона природный ряд — не
        /// выдумка разбора («по сути да, там NORM», Amber), и мерка корпуса
        /// считает такие компоненты отдельной колонкой «комнатных», а не
        /// фантомами (`S59` «б»).
        ///
        /// Ключ оставлен A/B-стороной замера (`--no-room`), а не выключателем на
        /// каждый день: без NORM Σχ² по корпусу хуже на 8…9 %.
        /// </summary>
        public bool Room = true;

        /// <summary>
        /// Добавлять атомный рентген — пробы, кристалла, защиты — и пики вылета
        /// кристалла (решение Amber 18.08.2026, `S56`). A/B-сторона замера.
        /// </summary>
        public bool AtomicXray = true;

        /// <summary>
        /// Наименьшая накопленная доля ветвления, при которой член ряда идёт в
        /// состав отдельным образом.
        ///
        /// ⛔ Порог здесь ОБЯЗАТЕЛЕН, и вот почему. Члены ряда входят в разбор
        /// РАЗНЫМИ образами со свободными амплитудами (так принято: «разрез
        /// цепочки получается сам», <see cref="FsaLibrary"/>). Свободная
        /// амплитуда снимает множитель ветвления начисто: у Tl-210 в радиевом
        /// ряду доля 2·10⁻⁴, а линий двадцать четыре, и предъявленный фиту
        /// такой образ ведёт себя ровно как фантом Pu-238 из `N18` — двадцать
        /// четыре свободные линии там, где физически нет ничего. Порог 10⁻³
        /// оставляет ряд целым (у Th-232 и Ra-226 все настоящие члены стоят на
        /// 1.0000 либо 0.3594) и снимает хвосты редких ветвей.
        /// </summary>
        public double MinChainBranch = 1.0e-3;

        /// <summary>
        /// Верхняя энергия родительской линии, которой ещё строится пик вылета,
        /// кэВ. Выше неё фотоэффект в кристалле пренебрежимо мал против
        /// комптона, и вылет тонет; счёт от этого не портится (вес выйдет почти
        /// нулевым), но образ разбухает линиями ни о чём.
        /// </summary>
        public double EscapeParentMaxKev = 600.0;

        /// <summary>
        /// Наименьший вес линии вылета в долях сильнейшей линии ТОГО ЖЕ образа.
        ///
        /// ⛔ Порог здесь не украшение, и цена его отсутствия измерена
        /// 18.08.2026 на `ASN16_Lu176`: без порога образ вылета иода вышел
        /// гребёнкой в 66 линий по всей шкале — по линии на каждую линию
        /// состава, включая тысячные доли процента вездесущих рядов. Гребёнка
        /// делает сразу две скверные вещи: в NNLS она свободный образ, готовый
        /// забрать любую необъяснённую структуру (механизм фантома `N18`), а в
        /// поиске пиков она забирает ПОДПИСИ — на том прогоне «Esc-I» и «Esc-Cs»
        /// подписали четыре пика из девяти, включая 304.32, который есть линия
        /// 306.78 самого лютеция.
        /// </summary>
        public double EscapeMinRelativeWeight = 0.02;
    }

    /// <summary>
    /// Сборка библиотеки образов ПО ОБЪЯВЛЕННОМУ СОСТАВУ ПРОБЫ — исполнение
    /// первого постулата (`S56`, формулировка Amber 17.08.2026): «у каждого
    /// спектра — СВОЯ база пиков, привязанная к снятому нуклиду».
    ///
    /// ЧЕМ ЭТО ОТЛИЧАЕТСЯ ОТ <see cref="FsaLibrary.BuildFromPeaks"/>. Там состав
    /// задаёт поиск пиков: какими нуклидами он подписал спектр, те и
    /// раскладываются, — а подписывает он из ОБЩЕГО списка, предъявленного
    /// прибору. Здесь наоборот: состав известен заранее (корпус знает, что
    /// снято), и общая библиотека приложения перестаёт быть тем, что
    /// предъявляется спектру. Цена прежнего порядка измерена в тот же день
    /// (`N18`): на `ASN16_Lu176` завёлся Pu-238 долей 1.7 % при z = 31.77 —
    /// единственной линией 152 кэВ в интенсивности 0.0009 %, попавшей в полосу
    /// обратного рассеяния линии 306.78 самого лютеция. Своим списком такой
    /// кандидат не появился бы вовсе.
    ///
    /// ⛔ ИСТОЧНИК — БАЗЫ, А НЕ КОНФИГ (указание Amber 18.08.2026). Ни
    /// `NuclideDefinition.xml`, ни `NuclideSet` здесь не читаются и читаться не
    /// должны: линии берутся из `nucdb.decay_radiations`, ряды — из
    /// `nucdb.decay_chain`, атомный рентген — из `matdb.xray_fluorescence`.
    /// Конфиг — дело человека и графика; состав образа — дело базы.
    ///
    /// ЛИНИИ БЕРУТСЯ ИЗ ВСЕХ ИЗЛУЧЕНИЙ РАСПАДА, А НЕ ИЗ ГАММ. Измерено
    /// 17.08.2026: у Lu-176 единственная гамма ниже 200 кэВ — 88.34, а реальный
    /// спектр внизу держится на K-рентгене гафния 54…65 кэВ (тип `X`); база из
    /// одних гамм дала невязку 405σ на 60.8 кэВ, с рентгеном — 251σ,
    /// χ²/ndf 5.342 → 4.696. Поэтому сюда идут типы `G` и `X`, K- и L-серия.
    ///
    /// ЧТО ЗДЕСЬ ЕСТЬ СВЕРХ РАСПАДА — и почему это не противоречит правилу
    /// «перенос живёт в матрице». Правило остаётся верным для СЧЁТА: матрица
    /// считает флуоресценцию пробы и вылет рентгена кристалла честно, из
    /// геометрии (`F27`, физика 12). Но матрицы нет у сорока спектров корпуса
    /// вовсе, а там, где она есть, её никто не проверяет — `E31` и `B14`
    /// прожили именно на этом. Решение Amber 18.08.2026: атомные образы
    /// кладутся ВСЕМ спектрам, свободной амплитудой, как опора будущей
    /// кросс-проверки (`S60`). ⚠ Отсюда следствие, которое надо помнить при
    /// чтении чисел: на понятной части эти образы конкурируют с матрицей за
    /// одни и те же отсчёты, и разница прогонов «было/стало» принадлежит не
    /// только сужению библиотеки. Разводится ключом `AtomicXray`.
    /// </summary>
    public static class FsaSampleLibrary
    {
        /// <summary>
        /// Что собралось и что не собралось — для проб и журнала. Пусто, если
        /// сказать нечего. Заводится на КАЖДЫЙ вызов <see cref="Build"/>:
        /// «нуклида нет в базе» и «нуклид есть, но линий в окне нет» с виду
        /// одно и то же, а чинятся по-разному.
        /// </summary>
        public sealed class Report
        {
            public readonly List<string> Notes = new List<string>();

            /// <summary>Членов рядов, отброшенных порогом ветвления.</summary>
            public int ChainMembersDropped;

            /// <summary>Образов распада (ряды и одиночные нуклиды).</summary>
            public int DecayComponents;

            /// <summary>Образов атомного рентгена и вылета.</summary>
            public int AtomicComponents;

            /// <summary>Линий во всех образах вместе.</summary>
            public int Lines;

            public override string ToString()
            {
                var text = new StringBuilder();
                text.AppendFormat(CultureInfo.InvariantCulture,
                                  "распад {0}, атомных {1}, линий {2}",
                                  this.DecayComponents, this.AtomicComponents, this.Lines);
                if (this.ChainMembersDropped > 0)
                {
                    text.AppendFormat(CultureInfo.InvariantCulture,
                                      "; членов ряда ниже порога ветвления {0}",
                                      this.ChainMembersDropped);
                }

                foreach (string note in this.Notes)
                {
                    text.Append("; ").Append(note);
                }

                return text.ToString();
            }
        }

        /// <summary>
        /// Строки `type_c`, которые при сборе K-серии дублируют соседей.
        ///
        /// `KB` — ИТОГ по Kβ, уже разложенный на `KpB1` и `KpB2` (`D30`):
        /// наивная сумма всех трёх удваивает Kβ на 21 %. Правило то же, что у
        /// <see cref="CascadeAtomicData"/>, и держится оно одно на двоих
        /// нарочно — двух соглашений о K-серии в проекте быть не должно.
        ///
        /// ⚠ Но правило это неполно, и здесь оно дополнено: у части нуклидов
        /// `KpB2` в базе НЕТ (лёгкие элементы, где линия не разрешена), и тогда
        /// `KpB1` меньше `KB` — у 232PA на 26 %, у 235NP на 25 %. Выбросив `KB`
        /// вслепую, мы теряем эту разницу. Поэтому `KB` выбрасывается, только
        /// если разложение на месте; иначе берётся он сам.
        /// </summary>
        const string KBetaTotal = "KB";

        static readonly object Gate = new object();

        /// <summary>Кэш линий распада по `nucid`: база одна, спектров сто.</summary>
        static readonly Dictionary<string, List<double[]>> LineCache =
            new Dictionary<string, List<double[]>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Кэш обходов ряда по корню.</summary>
        static readonly Dictionary<string, Dictionary<string, double>> ChainCache =
            new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Библиотека образов по объявленному составу. Никогда не null; пустой
        /// список означает «состав не дал ни одной линии в рабочем диапазоне» —
        /// это результат, а не отказ.
        /// </summary>
        public static List<FsaComponent> Build(FsaSampleSpec spec)
        {
            Report report;
            return Build(spec, out report);
        }

        /// <summary>То же, с отчётом о сборке.</summary>
        public static List<FsaComponent> Build(FsaSampleSpec spec, out Report report)
        {
            report = new Report();
            var result = new List<FsaComponent>();
            if (spec == null)
            {
                return result;
            }

            // Порядок сохраняется по первому появлению: сначала объявленный
            // состав, потом вездесущие, потом атомные образы. Так состав
            // читается в том же порядке, в каком о нём думают.
            var byName = new Dictionary<string, FsaComponent>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            // Накопленная доля ветвления на нуклид. Один и тот же нуклид может
            // прийти из двух рядов сразу (Ra-226 объявлен сам по себе и лежит
            // внутри U-238; Th-228 — сам по себе и внутри Th-232), и доли у него
            // тогда разные. Берём БОЛЬШУЮ: она отвечает тому ряду, в котором
            // нуклида больше, а свободная амплитуда всё равно перекроет разницу.
            var branch = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (FsaSampleChain chain in spec.Chains)
            {
                if (chain == null || string.IsNullOrEmpty(chain.Root))
                {
                    continue;
                }

                // ⛔ Изомеры в состав НЕ идут — кроме ряда урана-238 (указание
                // Amber 18.08.2026). Исключение здесь не поблажка, а физика:
                // Pa-234m1 несёт линию 1001.03 кэВ, классический «урановый
                // монопик», по которому уран и опознают; выбросив изомер, ряд
                // U-238 остался бы вовсе без сильной линии. В остальных рядах
                // изомер — отдельное состояние с собственным временем жизни, и
                // отдельным свободным образом он делает то же, что делают
                // хвосты редких ветвей: даёт фиту свободные линии там, где
                // равновесия нет.
                //
                // Основание у правила не только формальное. Слова Amber в тот
                // же день: на приборах AtomSpectra (ASN16, AS80x80 и прочие «на
                // A») изомеры в спектрах встречаются ТОЛЬКО в урановом стекле;
                // у америция там линии стабильные, при активности порядка
                // 65 кБк и ниже (оценка для понимания порядка). То есть
                // исключение ровно одно и оно названо, а не выведено.
                bool keepIsomers = string.Equals(chain.Root, "238U",
                                                 StringComparison.OrdinalIgnoreCase);
                Dictionary<string, double> members = ChainBranches(chain.Root, report);
                foreach (KeyValuePair<string, double> member in members)
                {
                    if (chain.Only.Count > 0 && !chain.Only.Contains(member.Key))
                    {
                        continue;
                    }

                    if (!keepIsomers && IsIsomer(member.Key))
                    {
                        continue;
                    }

                    if (member.Value < spec.MinChainBranch)
                    {
                        report.ChainMembersDropped++;
                        continue;
                    }

                    Remember(branch, member.Key, member.Value);
                }
            }

            foreach (string nucid in spec.Nuclides)
            {
                if (string.IsNullOrEmpty(nucid))
                {
                    continue;
                }

                if (IsIsomer(nucid))
                {
                    // Объявить изомер напрямую можно — это осознанный выбор
                    // того, кто заполняет состав, — но сказать об этом надо:
                    // общее правило (Amber 18.08.2026) изомеров не берёт.
                    report.Notes.Add("объявлен изомер " + nucid + " — взят по объявлению");
                }

                // Одиночный нуклид объявлен САМ, значит его выход дан на его
                // собственный распад — доля ветвления единица.
                Remember(branch, nucid, 1.0);
            }

            // Кто пришёл из ОБЪЯВЛЕННОГО состава, а кто добавлен комнатой.
            // Разница нужна пикам вылета: их образ строится по линиям пробы, и
            // подмешивать туда вездесущие ряды нельзя — в спектре лютеция
            // комната стоит на три порядка ниже, а в образе её линии оказались
            // бы вровень с лютециевыми. Измерено 18.08.2026: без этого деления
            // гребёнка вылета иода выходила в 66 линий на весь спектр.
            var declared = new HashSet<string>(branch.Keys, StringComparer.OrdinalIgnoreCase);

            if (spec.Room)
            {
                // Вездесущие. Ряды берутся целиком, тем же порогом: комната даёт
                // не «немножко тория», а торий в равновесии, просто слабый.
                foreach (string root in RoomChains)
                {
                    foreach (KeyValuePair<string, double> member in ChainBranches(root, report))
                    {
                        // Ряды комнаты — торий и радий, урана среди них нет,
                        // значит изомеров здесь не держим вовсе.
                        if (member.Value >= spec.MinChainBranch && !IsIsomer(member.Key))
                        {
                            Remember(branch, member.Key, member.Value);
                        }
                    }
                }

                Remember(branch, "40K", 1.0);
            }

            foreach (KeyValuePair<string, double> member in branch)
            {
                List<double[]> lines = DecayLines(member.Key, report);
                if (lines.Count == 0)
                {
                    continue;
                }

                string name = PrettyName(member.Key);
                FsaComponent component = Take(byName, order, name, FsaComponentKind.Single);
                foreach (double[] line in lines)
                {
                    if (line[0] < spec.MinEnergyKev || line[0] > spec.MaxEnergyKev)
                    {
                        continue;
                    }

                    AddLine(component, name, line[0], line[1] * member.Value);
                }
            }

            // Образы без единой линии в окне выбрасываются здесь, а не при
            // сборке: у члена ряда линии могут быть все выше потолка прибора, и
            // пустой образ в NNLS — вырожденный столбец.
            foreach (string name in order)
            {
                FsaComponent component = byName[name];
                if (component.Lines.Count > 0)
                {
                    result.Add(component);
                    report.DecayComponents++;
                    report.Lines += component.Lines.Count;
                }
            }

            if (spec.AtomicXray)
            {
                int before = result.Count;
                var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string nucid in declared)
                {
                    declaredNames.Add(PrettyName(nucid));
                }

                AddAtomic(spec, result, declaredNames, report);
                report.AtomicComponents = result.Count - before;
            }

            AddAnnihilation(result, report);
            return result;
        }

        // ------------------------------------------------------------------
        // Распад
        // ------------------------------------------------------------------

        /// <summary>
        /// Ряды, которые кладутся ЛЮБОМУ спектру: торий и радий комнаты. Калий
        /// стоит отдельной строкой — он не ряд.
        /// </summary>
        static readonly string[] RoomChains = { "232TH", "226RA" };

        /// <summary>
        /// {nucid → накопленная доля ветвления от корня}, только основные
        /// состояния родителя.
        ///
        /// Правило `l_seqno` — то же, что в `tools/CORPUS/scripts/chains.py`, и
        /// оно не косметическое: строки с `l_seqno` больше минимального
        /// описывают распад ВОЗБУЖДЁННОГО уровня и дублируют переход с другим
        /// ветвлением (у 212BI это 35.94 % при нуле и 67 % при пяти). Изомер
        /// при этом имеет собственный `nucid` (234PAm1), поэтому наименьший
        /// присутствующий уровень и есть физический распад.
        /// </summary>
        static Dictionary<string, double> ChainBranches(string root, Report report)
        {
            lock (Gate)
            {
                Dictionary<string, double> cached;
                if (ChainCache.TryGetValue(root, out cached))
                {
                    return cached;
                }
            }

            var branch = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            try
            {
                branch[root] = 1.0;
                var order = new List<string> { root };
                using (SqliteConnection connection = OpenRead(NuclideDatabasePath()))
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select daughter_nucid, perc from decay_chain d"
                        + " where nucid = $n and perc not null"
                        + " and l_seqno = (select min(l_seqno) from decay_chain x"
                        + "                where x.nucid = d.nucid"
                        + "                  and x.daughter_nucid = d.daughter_nucid"
                        + "                  and x.dec_type = d.dec_type)";
                    command.Parameters.AddWithValue("$n", root);
                    for (int i = 0; i < order.Count && order.Count <= 128; i++)
                    {
                        string current = order[i];
                        command.Parameters["$n"].Value = current;
                        var step = new List<KeyValuePair<string, double>>();
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                string daughter = reader.IsDBNull(0) ? null : reader.GetString(0);
                                if (string.IsNullOrEmpty(daughter)
                                    || string.Equals(daughter, current, StringComparison.OrdinalIgnoreCase))
                                {
                                    // 238U несёт самопетлю на l_seqno-119.
                                    continue;
                                }

                                double percent;
                                if (!TryNumber(reader, 1, out percent))
                                {
                                    continue;
                                }

                                step.Add(new KeyValuePair<string, double>(daughter, percent));
                            }
                        }

                        foreach (KeyValuePair<string, double> row in step)
                        {
                            double add = branch[current] * row.Value / 100.0;
                            if (add < 1.0e-6)
                            {
                                continue;
                            }

                            double have;
                            if (branch.TryGetValue(row.Key, out have))
                            {
                                branch[row.Key] = have + add;
                            }
                            else
                            {
                                branch[row.Key] = add;
                                order.Add(row.Key);
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                // Отказ базы не должен ронять разбор — но и молчать нельзя:
                // «ряд пуст» и «читатель сломан» с виду одно и то же.
                report.Notes.Add("ряд " + root + ": отказ базы — " + error.Message);
            }

            lock (Gate)
            {
                ChainCache[root] = branch;
            }

            return branch;
        }

        /// <summary>
        /// Линии распада нуклида: {энергия, выход % на распад ЭТОГО нуклида}.
        /// Типы `G` и `X`; K-серия по правилу <see cref="KBetaTotal"/>,
        /// L-серия — подробными строками, если они есть, иначе обобщённой.
        /// </summary>
        static List<double[]> DecayLines(string nucid, Report report)
        {
            lock (Gate)
            {
                List<double[]> cached;
                if (LineCache.TryGetValue(nucid, out cached))
                {
                    return cached;
                }
            }

            var gamma = new List<double[]>();
            var kAlpha = new List<double[]>();
            var kBetaSplit = new List<double[]>();
            var kBetaTotal = new List<double[]>();
            var lLumped = new List<double[]>();
            var lDetailed = new List<double[]>();

            try
            {
                using (SqliteConnection connection = OpenRead(NuclideDatabasePath()))
                using (SqliteCommand command = connection.CreateCommand())
                {
                    // ⚠ Тот же зажим по `parent_l_seqno`, что и у ряда, и по той
                    // же причине: Pa-234m1 несёт линию 1001.03 кэВ на уровне 2, а
                    // не на нуле, поэтому «= 0» здесь потеряло бы её целиком.
                    command.CommandText =
                        "select type_a, type_c, energy_num, intensity_num from decay_radiations"
                        + " where parent_nucid = $n and type_a in ('G', 'X')"
                        + " and energy_num not null and intensity_num > 0"
                        + " and parent_l_seqno = (select min(parent_l_seqno) from decay_radiations y"
                        + "                       where y.parent_nucid = $n)";
                    command.Parameters.AddWithValue("$n", nucid);
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string kind = reader.IsDBNull(0) ? "" : reader.GetString(0);
                            string series = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                            double energy, intensity;
                            if (!TryNumber(reader, 2, out energy) || !TryNumber(reader, 3, out intensity)
                                || !(energy > 0.0) || !(intensity > 0.0))
                            {
                                continue;
                            }

                            var line = new[] { energy, intensity };
                            if (kind == "G")
                            {
                                gamma.Add(line);
                            }
                            else if (series.Length == 0 || series[0] != 'K')
                            {
                                if (series.Length == 1 && series[0] == 'L')
                                {
                                    lLumped.Add(line);
                                }
                                else if (series.Length > 1 && series[0] == 'L')
                                {
                                    lDetailed.Add(line);
                                }
                            }
                            else if (string.Equals(series, KBetaTotal, StringComparison.Ordinal))
                            {
                                kBetaTotal.Add(line);
                            }
                            else if (series.StartsWith("Kp", StringComparison.Ordinal))
                            {
                                kBetaSplit.Add(line);
                            }
                            else
                            {
                                kAlpha.Add(line);
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                report.Notes.Add("линии " + nucid + ": отказ базы — " + error.Message);
            }

            var lines = new List<double[]>();
            lines.AddRange(gamma);
            lines.AddRange(kAlpha);

            // Kβ: разложение, если оно есть, иначе итог. Сложить и то и другое
            // значит удвоить Kβ (`D30`); выбросить итог там, где разложения нет,
            // значит потерять Kβ целиком.
            lines.AddRange(kBetaSplit.Count > 0 ? kBetaSplit : kBetaTotal);

            // L-серия: та же развилка. Подробных строк в базе всего у трёх
            // нуклидов (225RA, 225RN, 229TH), и ни один в корпусе не снят, —
            // но правило дешевле проверки «а вдруг».
            lines.AddRange(lDetailed.Count > 0 ? lDetailed : lLumped);

            lines.Sort((a, b) => a[0].CompareTo(b[0]));
            lock (Gate)
            {
                LineCache[nucid] = lines;
            }

            return lines;
        }

        // ------------------------------------------------------------------
        // Атомные образы: рентген пробы, кристалла, защиты и пики вылета
        // ------------------------------------------------------------------

        /// <summary>
        /// Флуоресценция пробы, кристалла и защиты плюс вылет K-рентгена
        /// кристалла. Всё — мешающими образами со свободной амплитудой: за
        /// этими линиями нет активности, и в «пирог» долей они не входят.
        /// </summary>
        static void AddAtomic(FsaSampleSpec spec, List<FsaComponent> result,
                              HashSet<string> declared, Report report)
        {
            var seen = new HashSet<int>();
            AddFluorescence(spec, spec.SampleElements, "проба", result, report, seen);
            AddFluorescence(spec, spec.ShieldElements, "защита", result, report, seen);
            AddFluorescence(spec, spec.CrystalElements, "кристалл", result, report, seen);
            AddEscape(spec, result, declared, report);
        }

        static void AddFluorescence(FsaSampleSpec spec, List<int> elements, string what,
                                    List<FsaComponent> result, Report report, HashSet<int> seen)
        {
            foreach (int z in elements)
            {
                if (z <= 0 || !seen.Add(z))
                {
                    continue;
                }

                MaterialDatabase.Fluorescence fluorescence = MaterialDatabase.FluorescenceOf(z);
                if (fluorescence == null || fluorescence.LineKev == null)
                {
                    report.Notes.Add("нет K-серии для Z=" + z.ToString(CultureInfo.InvariantCulture)
                                     + " (" + what + ")");
                    continue;
                }

                // ⛔ L-серия сюда НЕ идёт, и это не забывчивость: в
                // `matdb.xray_fluorescence` её нет вовсе — таблица держит только
                // K-край, Kα1, Kα2 и Kβ. Собрать L пришлось бы из
                // `eadl_radiative`, считая веса внутри серии самому; решением
                // Amber 18.08.2026 в `S56` это не входит.
                var component = new FsaComponent("Xray-" + MaterialDatabase.SymbolOf(z),
                                                 FsaComponentKind.Nuisance);
                for (int i = 0; i < fluorescence.LineKev.Length; i++)
                {
                    double energy = fluorescence.LineKev[i];
                    double weight = fluorescence.LineWeight[i];
                    if (!(energy > 0.0) || !(weight > 0.0)
                        || energy < spec.MinEnergyKev || energy > spec.MaxEnergyKev)
                    {
                        continue;
                    }

                    // Вес — доля ВНУТРИ K-серии, а не выход на распад: у
                    // характеристического рентгена выхода на распад не
                    // существует (атом светит, когда в K появилась дырка, а
                    // сколько их — дело геометрии и спектра возбуждения). Ровно
                    // поэтому образ и мешающий.
                    AddLine(component, component.Name, energy, 100.0 * weight);
                }

                if (component.Lines.Count > 0)
                {
                    result.Add(component);
                    report.Lines += component.Lines.Count;
                }
            }
        }

        /// <summary>
        /// Пики вылета K-рентгена кристалла: линия E порождает вылет на
        /// E − E(Kα) того элемента кристалла, чей K-край она перешла.
        ///
        /// ⚠ ЭТО ПЕРВОЕ ПРИБЛИЖЕНИЕ, и называть его надо приближением. Честно
        /// вылет считает матрица отклика — розыгрышем переноса, с геометрией
        /// кристалла и глубиной поглощения. Здесь же вес линии берётся как
        ///
        ///     I(родителя) · f_фото(E) · доля K · ω_K,
        ///
        /// то есть без геометрической части («какая доля рождённых квантов
        /// вышла наружу»), которая от энергии почти не зависит и потому уходит
        /// в общую свободную амплитуду образа. Все три множителя — из `matdb`
        /// (`xcom_cross_sections`, `epics_photo_subshell`, `xray_fluorescence`),
        /// выдуманного в них нет.
        ///
        /// ⛔ На спектрах С МАТРИЦЕЙ этот образ спорит с ней за одни и те же
        /// отсчёты — то самое «второе счётоведение», ради прекращения которого
        /// заведён гейт `S47` (<see cref="FsaAnalyzer.EscapeGate"/>). Гейт судит
        /// по имени и снимает только `SE-2614`/`DE-2614`, поэтому здешние
        /// `Esc-*` он не тронет. Так решено 18.08.2026 (Amber, «клади везде»):
        /// образ нужен как опора кросс-проверки матрицы (`S60`). Цена снимается
        /// ключом `AtomicXray`, а не догадкой.
        /// </summary>
        static void AddEscape(FsaSampleSpec spec, List<FsaComponent> result,
                              HashSet<string> declared, Report report)
        {
            if (spec.CrystalElements.Count == 0)
            {
                return;
            }

            // Родительские линии берутся из уже собранного состава распада: это
            // ровно те кванты, которые в кристалл прилетят.
            var parents = new List<double[]>();
            foreach (FsaComponent component in result)
            {
                if (component.Kind != FsaComponentKind.Single
                    || !declared.Contains(component.Name))
                {
                    continue;
                }

                foreach (FsaLine line in component.Lines)
                {
                    if (line.Energy <= spec.EscapeParentMaxKev && line.Intensity > 0.0)
                    {
                        parents.Add(new[] { line.Energy, line.Intensity });
                    }
                }
            }

            if (parents.Count == 0)
            {
                return;
            }

            var done = new HashSet<int>();
            foreach (int z in spec.CrystalElements)
            {
                if (z <= 0 || !done.Add(z))
                {
                    continue;
                }

                MaterialDatabase.Fluorescence fluorescence = MaterialDatabase.FluorescenceOf(z);
                MaterialDatabase.PhotoShellModel shells = MaterialDatabase.PhotoShellOf(z);
                MaterialDatabase.Element element;
                if (fluorescence == null || !MaterialDatabase.TryGet(z, out element))
                {
                    report.Notes.Add("вылет: нет данных для Z="
                                     + z.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                double omega = fluorescence.Omega(true);
                double kAlpha = fluorescence.LineKev != null && fluorescence.LineKev.Length > 0
                    ? fluorescence.LineKev[0] : 0.0;
                if (!(omega > 0.0) || !(kAlpha > 0.0))
                {
                    continue;
                }

                var component = new FsaComponent("Esc-" + MaterialDatabase.SymbolOf(z),
                                                 FsaComponentKind.Nuisance);
                foreach (double[] parent in parents)
                {
                    // Ниже K-края дырки в K-оболочке не бывает, и вылета нет.
                    if (parent[0] <= fluorescence.KEdgeKev)
                    {
                        continue;
                    }

                    double energy = parent[0] - kAlpha;
                    if (energy < spec.MinEnergyKev || energy > spec.MaxEnergyKev)
                    {
                        continue;
                    }

                    double photo = MaterialDatabase.Interpolate(element.EnergyKev,
                                                                element.Channels[2], parent[0]);
                    double total = MaterialDatabase.Interpolate(element.EnergyKev,
                                                                element.Total, parent[0]);
                    if (!(photo > 0.0) || !(total > 0.0))
                    {
                        continue;
                    }

                    double kShare = shells != null ? shells.KFraction(parent[0])
                                                   : fluorescence.KFraction;
                    double weight = parent[1] * (photo / total) * kShare * omega;
                    if (weight > 0.0)
                    {
                        AddLine(component, component.Name, energy, weight);
                    }
                }

                Prune(component, spec.EscapeMinRelativeWeight);
                if (component.Lines.Count > 0)
                {
                    result.Add(component);
                    report.Lines += component.Lines.Count;
                }
            }
        }

        /// <summary>
        /// Выбросить из образа линии слабее доли <paramref name="relative"/> от
        /// его же сильнейшей. Отношение берётся ВНУТРИ образа, а не по всей
        /// библиотеке: амплитуда у образа своя и свободная, так что абсолютный
        /// порог для него не определён вовсе.
        /// </summary>
        static void Prune(FsaComponent component, double relative)
        {
            if (!(relative > 0.0) || component.Lines.Count == 0)
            {
                return;
            }

            double top = 0.0;
            foreach (FsaLine line in component.Lines)
            {
                if (line.Intensity > top)
                {
                    top = line.Intensity;
                }
            }

            double floor = top * relative;
            var kept = new List<FsaLine>(component.Lines.Count);
            foreach (FsaLine line in component.Lines)
            {
                if (line.Intensity >= floor)
                {
                    kept.Add(line);
                }
            }

            component.Lines.Clear();
            component.Lines.AddRange(kept);
        }

        /// <summary>
        /// Аннигиляционная линия 511 кэВ: рождение пар жёсткими квантами в
        /// защите и обвязке плюс β⁺-примеси. Нуклиду она не принадлежит, доля
        /// зависит от домика и геометрии — поэтому свободный мешающий образ.
        /// Правило то же, что у <see cref="FsaLibrary"/>: без образа NNLS вешает
        /// 511 на ближайшую линию соседа.
        /// </summary>
        static void AddAnnihilation(List<FsaComponent> result, Report report)
        {
            if (result.Count == 0)
            {
                return;
            }

            var component = new FsaComponent("Ann-511", FsaComponentKind.Nuisance);
            component.Lines.Add(new FsaLine("Ann-511", 511.0, 100.0));
            result.Add(component);
            report.Lines++;
        }

        // ------------------------------------------------------------------
        // Мелочи
        // ------------------------------------------------------------------

        /// <summary>
        /// Та же база, но в виде, который понимает ПОИСК ПИКОВ
        /// (<see cref="PeakDetector.DetectPeak"/>).
        ///
        /// Постулат `S56` говорит «своя база ПИКОВ», и подпись — вторая его
        /// половина. Пока поиск подписывает из общего поставочного списка, на
        /// спектре лютеция получаются подписи «Pu-238», «Eu-152», «I-131» —
        /// измерено 18.08.2026 на `ASN16_Lu176`, где так подписаны четыре пика
        /// из девяти. В разложение они теперь не попадают (состав задаёт проба),
        /// но человек читает именно их, и кривая эффективности строится по
        /// площади ПОДПИСАННОГО пика.
        ///
        /// ⚠ Подавать вместе с <c>nuclideSet = null</c>: набор здесь ни при чём,
        /// список уже свой, а <see cref="NuclideSet.HideUnknownPeaks"/> у чужого
        /// набора вычеркнул бы неподписанные пики и увёл счёт.
        /// </summary>
        public static List<NuclideDefinition> AsDefinitions(List<FsaComponent> library)
        {
            var definitions = new List<NuclideDefinition>();
            if (library == null)
            {
                return definitions;
            }

            foreach (FsaComponent component in library)
            {
                foreach (FsaLine line in component.Lines)
                {
                    var definition = new NuclideDefinition();
                    definition.Name = component.Name;
                    definition.Energy = line.Energy;
                    definition.Intencity = line.Intensity;
                    definition.Visible = true;
                    definitions.Add(definition);
                }
            }

            return definitions;
        }

        /// <summary>
        /// Элементы вещества, чей K-рентген вообще способен дать линию в этом
        /// спектре: массовая доля не ниже <paramref name="minFraction"/>, а
        /// Kα1 — внутри рабочего диапазона.
        ///
        /// Оба отбора нужны, и оба дешёвые. Без порога по доле в состав пробы
        /// попадает всё, включая примеси; без порога по энергии — кальций
        /// оникса (Kα 3.69 кэВ) и калий KCl (3.31), которых ни один прибор
        /// корпуса не видит: нижняя граница у них 20…40 кэВ. Образ из линий,
        /// лежащих вне окна фита, — вырожденный столбец в NNLS.
        ///
        /// ⚠ Вещество из файла спектра приходит ОДНИМ ИМЕНЕМ, без состава:
        /// `GeometryMaterial.Fractions` в XML пуст, а «Cesium iodide» лежит
        /// строкой. Поэтому состав добирается из библиотеки веществ по имени, и
        /// молчаливого отказа здесь быть не должно — вещество, которого в
        /// библиотеке нет, возвращает пустой список, а не «ничего страшного».
        /// </summary>
        public static List<int> HeavyElementsOf(GeometryMaterial material,
                                                double minFraction,
                                                double minKAlphaKev,
                                                double maxKAlphaKev)
        {
            var elements = new List<int>();
            if (material == null)
            {
                return elements;
            }

            Dictionary<int, double> fractions = material.Fractions;
            if (fractions.Count == 0 && !string.IsNullOrEmpty(material.Name))
            {
                GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(material.Name);
                if (entry != null)
                {
                    fractions = GeometryMaterialLibrary.Make(entry, material.Density).Fractions;
                }
            }

            foreach (KeyValuePair<int, double> pair in fractions)
            {
                if (pair.Value < minFraction)
                {
                    continue;
                }

                MaterialDatabase.Fluorescence fluorescence = MaterialDatabase.FluorescenceOf(pair.Key);
                if (fluorescence == null || fluorescence.LineKev == null
                    || fluorescence.LineKev.Length == 0)
                {
                    continue;
                }

                double kAlpha = fluorescence.LineKev[0];
                if (kAlpha >= minKAlphaKev && kAlpha <= maxKAlphaKev)
                {
                    elements.Add(pair.Key);
                }
            }

            elements.Sort();
            return elements;
        }

        /// <summary>
        /// Возбуждённое состояние: у `nucid` есть метка состояния строчными
        /// буквами («234PAm1», «108AGm», «105PDe»).
        ///
        /// ⚠ Признак — РЕГИСТР, а не буква. Символ элемента записан заглавными
        /// целиком, поэтому «241AM» (америций) изомером не является, а «108AGm»
        /// является. На этом уже поскользнулись 18.08.2026 — см.
        /// <see cref="PrettyName"/>.
        /// </summary>
        public static bool IsIsomer(string nucid)
        {
            int mass;
            string symbol, state;
            return CascadeAtomicData.SplitNucid(nucid, out mass, out symbol, out state)
                   && state.Length > 0;
        }

        static void Remember(Dictionary<string, double> branch, string nucid, double value)
        {
            double have;
            branch[nucid] = branch.TryGetValue(nucid, out have) && have > value ? have : value;
        }

        static FsaComponent Take(Dictionary<string, FsaComponent> byName, List<string> order,
                                 string name, FsaComponentKind kind)
        {
            FsaComponent component;
            if (!byName.TryGetValue(name, out component))
            {
                component = new FsaComponent(name, kind);
                byName[name] = component;
                order.Add(name);
            }

            return component;
        }

        /// <summary>
        /// Линия в образ с защитой от дубля. Порог 0.05 кэВ — тот же, что в
        /// <see cref="FsaLibrary"/>: реальных раздельных линий ближе не бывает,
        /// а запись той же линии с иной точностью удвоила бы её вес и уронила
        /// амплитуду образа вдвое.
        /// </summary>
        static void AddLine(FsaComponent component, string nuclide, double energy, double intensity)
        {
            foreach (FsaLine line in component.Lines)
            {
                if (Math.Abs(line.Energy - energy) < 0.05)
                {
                    return;
                }
            }

            component.Lines.Add(new FsaLine(nuclide, energy, intensity));
        }

        /// <summary>
        /// `nucid` → подпись: «212PB» → «Pb-212», «241AM» → «Am-241»,
        /// «234PAm1» → «Pa-234m», «108AGm» → «Ag-108m».
        ///
        /// ⚠ Хвост изомера пишется БЕЗ номера состояния, и это не небрежность:
        /// именно так изомер назван и в мерке корпуса (`score.py`,
        /// `CHAIN_MEMBERS`: «Pa-234m»), а имя компонента — ключ, по которому
        /// мерка сводит найденное с истиной. `chains.pretty` в питоне на этом
        /// имени спотыкается вовсе и отдаёт «234PAm1» как есть (регистр `m` не
        /// подходит под её шаблон) — здесь это учтено.
        /// </summary>
        public static string PrettyName(string nucid)
        {
            int mass;
            string symbol, state;
            if (!CascadeAtomicData.SplitNucid(nucid, out mass, out symbol, out state))
            {
                return nucid ?? "";
            }

            // Номер состояния в подпись НЕ идёт: «234PAm1» -> «Pa-234m». Так
            // изомер назван и в мерке корпуса (`score.py`, `CHAIN_MEMBERS`), а
            // имя компонента — ключ, по которому мерка сводит найденное с
            // истиной.
            string letters = "";
            foreach (char c in state)
            {
                if (!char.IsDigit(c))
                {
                    letters += char.ToLowerInvariant(c);
                }
            }

            return char.ToUpperInvariant(symbol[0]) + symbol.Substring(1).ToLowerInvariant()
                   + "-" + mass.ToString(CultureInfo.InvariantCulture) + letters;
        }
        /// <summary>Обратно: «Cs-137» → «137CS»; пусто — разобрать не вышло.</summary>
        public static string NucidOf(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            string text = name.Trim();
            int dash = text.IndexOf('-');
            if (dash <= 0 || dash + 1 >= text.Length)
            {
                return "";
            }

            string symbol = text.Substring(0, dash);
            string mass = text.Substring(dash + 1);
            string isomer = "";
            while (mass.Length > 0 && char.IsLetter(mass[mass.Length - 1]))
            {
                isomer = char.ToLowerInvariant(mass[mass.Length - 1]) + isomer;
                mass = mass.Substring(0, mass.Length - 1);
            }

            foreach (char c in mass)
            {
                if (c < '0' || c > '9')
                {
                    return "";
                }
            }

            return mass.Length == 0 ? "" : mass + symbol.ToUpperInvariant() + isomer;
        }

        static bool TryNumber(SqliteDataReader reader, int column, out double value)
        {
            value = 0.0;
            if (reader.IsDBNull(column))
            {
                return false;
            }

            // Колонки базы разнородны по типу: `perc` лежит текстом, `energy_num`
            // — числом. Читать текстовую как double бросает, числовую как
            // строку — тоже, поэтому пробуются оба.
            try
            {
                value = reader.GetDouble(column);
                return true;
            }
            catch (InvalidCastException)
            {
            }

            string text = reader.GetValue(column) as string;
            return text != null
                   && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        static SqliteConnection OpenRead(string path)
        {
            SqliteConnection connection = new SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;");
            connection.Open();
            return connection;
        }

        static string NuclideDatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        }
    }
}
