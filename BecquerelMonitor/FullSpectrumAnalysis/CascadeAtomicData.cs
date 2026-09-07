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
    /// Неядерные участники каскада: K-рентген атома и аннигиляционные кванты
    /// (TODO S27, пункт «пары только γ-γ»).
    ///
    /// ЗАЧЕМ. Таблицы совпадений SandiaDecay держат ТОЛЬКО пары гамма-гамма.
    /// Между тем из одного распада вместе с гаммой вылетает и то, что гаммой не
    /// является, и на шкале это видно: у захватных нуклидов (Ba-133, Ce-139,
    /// Cd-109) K-рентген дочернего атома идёт с выходом до 115 % на распад, а у
    /// β⁺-излучателей (Na-22) — два кванта по 511 кэВ. Пока их нет в партнёрах,
    /// CF занижен, а сумм-пиков вида «гамма + рентген» модель не ставит вовсе.
    ///
    /// ОТКУДА K-ВАКАНСИЯ. Их ровно два источника, и различать их обязательно,
    /// потому что совпадают они по-разному:
    ///
    ///   * **захват (EC)** — дырка рождается В МОМЕНТ РАСПАДА, до всякого
    ///     гамма-каскада, и потому совпадает с ЛЮБОЙ гаммой этого распада;
    ///   * **внутренняя конверсия перехода T** — дырка рождается, когда идёт
    ///     сам переход T, и совпадает со всеми гаммами каскада, КРОМЕ гаммы
    ///     самого T: если T испустил гамму, значит он не конвертировал, и
    ///     вакансии от него в этом событии нет. Это не тонкость, а перемена
    ///     знака: у Lu-176 ВЕСЬ K-рентген гафния (33.5 % на распад) — от
    ///     конверсии, захвата там нет вовсе.
    ///
    /// ОТКУДА ЧИСЛА. Захватную долю НЕ ищем в справочнике (её у нас нет) —
    /// считаем ОСТАТКОМ, и этот остаток заодно служит поверкой всей связки:
    ///
    ///     вакансий всего      = I_K(измеренный выход) / ω_K
    ///     вакансий конверсии  = Σ_T n_γ(T) · α_K(T)
    ///     ЗАХВАТ              = всего − конверсия
    ///
    /// Три независимых источника (`decay_radiations`, `g4_gamma`,
    /// `fluorescence_yield`) обязаны сойтись, и сходятся: у β-излучателей без
    /// захвата остаток выходит НУЛЁМ — Cs-137 +0.0000, Co-60 +0.0003,
    /// Lu-176 +0.0014, — а у захватных встаёт долей K-захвата, какой ей и
    /// положено быть: Mn-54 0.891, Zn-65 0.871, Y-88 0.872, Ti-44 0.887,
    /// Co-57 0.861. **Отрицательный остаток означает не физику, а поломку
    /// сопоставления** (см. <see cref="MatchTransition"/>), поэтому он
    /// зажимается нулём и записывается в <see cref="Note"/>.
    ///
    /// ЧЕГО ЗДЕСЬ НЕТ, сознательно:
    ///   * **L-серия** (Am-241 17.1 кэВ на 36.6 %, Lu-176 9.1 на 23.1) — у неё
    ///     своя бухгалтерия вакансий: L-дырки родятся и сами, и при заполнении
    ///     K, и остатком их не выделить. Заведено отдельной строкой, чтобы
    ///     приближённая половина не смешалась с посчитанной (TODO S58);
    ///   * **переходы, целиком ушедшие в конверсию** (гаммы нет вовсе) — их
    ///     вакансии оседают в захватном остатке и потому не теряются, но и
    ///     своей гамме не приписываются: приписывать нечему;
    ///   * **оже-электроны** — вакансия, ответившая электроном, кванта не даёт;
    ///     за это отвечает множитель ω_K.
    /// </summary>
    public sealed class CascadeAtomicData
    {
        /// <summary>
        /// Задерживающий уровень на пути кванта: КТО задержал и НАСКОЛЬКО
        /// (`A290`). Пара неразделима — период без уровня не даёт сказать,
        /// одна это задержка у двух квантов или две независимые.
        /// </summary>
        public struct Phase
        {
            /// <summary>Номер уровня в схеме — тождество, а не мера.</summary>
            public int Seq;

            /// <summary>Период полураспада этого уровня, секунды.</summary>
            public double HalfLifeSec;
        }

        /// <summary>
        /// Переход, сопоставленный гамма-линии распада: чем он конвертирует и
        /// откуда идёт. Уровни нужны для гейта по времени.
        /// </summary>
        public sealed class Transition
        {
            public double EnergyKev;

            /// <summary>Коэффициент конверсии по K-оболочке, зажатый полным.</summary>
            public double AlphaK;

            /// <summary>Номер уровня, С которого идёт переход.</summary>
            public int FromSeq;

            /// <summary>Номер уровня, НА который переход идёт.</summary>
            public int ToSeq;

            /// <summary>
            /// НОМЕР ВЕТВИ в <see cref="Branches"/>, в схеме которой найден
            /// этот переход; −1 — ветвь не определена (`S145`, ключ исправлен
            /// по `S148`). У родителя со смешанным распадом ветвей несколько, и
            /// гамма принадлежит РОВНО ОДНОЙ: у Eu-152 линии 121.8 и 964.1 —
            /// схема Sm-152 (захват), а 344.3 и 778.9 — схема Gd-152 (β⁻).
            /// Без этого поля захватный рентген самария приписывался и гаммам
            /// гадолиния, то есть совпадению, которого не бывает.
            ///
            /// ⛔ ЗДЕСЬ НОМЕР ВЕТВИ, А НЕ `Z` ДОЧЕРНЕГО АТОМА, и это поправка
            /// `S148` к первой редакции. По `Z` ветви СЛИВАЮТСЯ: в поставке
            /// **144 родителя**, у которых две ветви дают один и тот же
            /// элемент, — либо разными каналами в один нуклид (8 случаев,
            /// например `131CE → 131LA` двумя `dec_type`), либо в разные
            /// изотопы одного элемента (`100RB` → `100SR`, `99SR`, `98SR`).
            /// У таких ветвей РАЗНЫЕ схемы уровней и времена, и общий ключ
            /// затирал носители, времена и вакансии предыдущей ветви.
            /// </summary>
            public int BranchIndex = -1;

            /// <summary>
            /// Через сколько секунд после распада вылетает этот квант. Ноль —
            /// мгновенно. Считается ходом по схеме уровней, см.
            /// <see cref="Delays"/>.
            ///
            /// ⚠ Это СУММА ПЕРИОДОВ ПОЛУРАСПАДА по пути, а не время события:
            /// время жизни уровня распределено экспоненциально, и потребителю,
            /// которому нужна вероятность уложиться в окно, нужны сами периоды
            /// (<see cref="EmitPhases"/>), а не их сумма (`A289`).
            /// </summary>
            public double EmitDelaySec;

            /// <summary>
            /// Уровни НА ПУТИ этого кванта, от распада вниз — по одному на
            /// уровень, что задержал каскад. Пусто — путь мгновенный.
            ///
            /// Хранятся ПОШТУЧНО нарочно (`A289`): вероятность уложиться в окно
            /// у суммы экспонент не выводится из суммы их периодов, а
            /// требует свёртки распределений. Кто складывает — тот получает
            /// ступеньку 100→0 % около окна прибора вместо плавного перехода.
            ///
            /// ⛔ И НОМЕР УРОВНЯ ЗДЕСЬ НЕ УКРАШЕНИЕ (`A290`). Потребителю нужно
            /// знать, ОДИН ЛИ ЭТО уровень у двух квантов, а по одному лишь
            /// периоду это неразличимо: у разных уровней разных ветвей периоды
            /// совпадают (в `schemedb` таких пар хватает — `Au-183` seq 4 и 8
            /// по 1 мкс). Сокращать общий путь по равенству ЧИСЕЛ значит
            /// объявлять две независимые задержки одной и той же.
            /// </summary>
            public Phase[] EmitPhases;
        }

        /// <summary>
        /// ВЕТВЬ РАСПАДА: свой дочерний атом, свой выход флуоресценции, свои
        /// K-линии и свой остаток захватных вакансий (`S145`).
        ///
        /// ⛔ Заведено потому, что прежде бралась ОДНА ветвь — сильнейшая по
        /// `perc`, — и её захватные вакансии приписывались ВСЕМ гаммам
        /// родителя. Для Eu-152 (Sm-152 72.08 % захватом и Gd-152 27.92 %
        /// бета-минусом) это давало физически невозможные совпадения между
        /// разными событиями распада, а K-линии обоих атомов лежали в одном
        /// ведре под одним ω_K сильнейшего.
        /// </summary>
        public sealed class Branch
        {
            public string Nucid;
            public int Z;
            public int A;

            /// <summary>Доля этой ветви, % (графа `perc` в `decay_chain`).</summary>
            public double Perc;

            /// <summary>
            /// Канал распада (`dec_type` в `decay_chain`). Держится потому, что
            /// В ОДИН дочерний нуклид ведут разные каналы, и это РАЗНЫЕ ветви
            /// с разными долями (`S148`).
            /// </summary>
            public string DecType;

            /// <summary>Выход флуоресценции K ЭТОГО атома.</summary>
            public double OmegaK;

            /// <summary>K-линии ЭТОГО атома: энергия и выход, % на распад.</summary>
            public List<double[]> KLines = new List<double[]>();

            /// <summary>Суммарный выход <see cref="KLines"/>, %.</summary>
            public double KIntensityPct;

            /// <summary>Захватные вакансии ЭТОЙ ветви, на распад.</summary>
            public double PromptVacancy;
        }

        /// <summary>
        /// Все ветви распада родителя. Пусто — ветвей не нашлось; тогда работают
        /// прежние сводные поля.
        /// </summary>
        public List<Branch> Branches = new List<Branch>();

        /// <summary>
        /// Ветвь, которой принадлежит гамма-линия (`S145`). Null — линии нет в
        /// схеме ни одной ветви либо ветвей не нашлось вовсе; потребитель тогда
        /// обязан вести себя как прежде, по сводным полям.
        /// </summary>
        public Branch BranchOfGamma(double energyKev)
        {
            Transition transition;
            if (!this.Gammas.TryGetValue(energyKev, out transition)
                || transition.BranchIndex < 0
                || transition.BranchIndex >= this.Branches.Count)
            {
                return null;
            }

            return this.Branches[transition.BranchIndex];
        }

        /// <summary>
        /// K-линии дочернего атома: энергия и выход, % на распад.
        ///
        /// ⚠ Это СВОДНОЕ поле сильнейшей ветви — оставлено ради прежних
        /// читателей (пробы). Поветвевые данные — в <see cref="Branches"/>.
        /// </summary>
        public List<double[]> KLines = new List<double[]>();

        /// <summary>Выход флуоресценции K, доля.</summary>
        public double OmegaK;

        /// <summary>
        /// Дочерний нуклид, которому принадлежит K-рентген: та ветвь распада,
        /// у которой `perc` наибольший, петли `daughter = nucid` сняты.
        /// Пусто — ветвей не нашлось.
        ///
        /// ⚠ Открыто наружу РАДИ ЧИТАТЕЛЯ (`A218`), как и `DecayParentRule`:
        /// без него «правило выбрало не тот атом» и «у атома нет ω_K» с виду
        /// одно и то же, а именно этим и различаются старое правило и новое.
        /// Читает `tools/effmaker/probes/ChainRuleProbeF58.cs`.
        /// </summary>
        public string Daughter;

        /// <summary>
        /// Число K-вакансий на распад, рождённых МГНОВЕННО (захват). Именно
        /// они совпадают с любой гаммой каскада.
        /// </summary>
        public double PromptVacancy;

        /// <summary>Гамма-линия распада → сопоставленный ей переход.</summary>
        public Dictionary<double, Transition> Gammas =
            new Dictionary<double, Transition>();

        /// <summary>
        /// ВСЕ гамма-линии распада с выходами, % на распад родителя, — включая
        /// те, которым перехода в схеме не нашлось.
        ///
        /// Нужны отдельно от <see cref="Gammas"/> вот зачем: у нуклида с ОДНОЙ
        /// гаммой (Ce-139, Cd-109, Mn-54, Zn-65, Na-22) пар гамма-гамма нет, и
        /// в поставке SandiaDecay его нет вовсе — ни строки. Значит выходы его
        /// линий взять оттуда нельзя, а без выходов не посчитать ни CF, ни
        /// площадь суммы с рентгеном. Здесь они есть всегда.
        /// </summary>
        public List<double[]> GammaIntensity = new List<double[]>();

        /// <summary>
        /// Квантов 511 кэВ на распад: ДВА на каждый β⁺, потому что позитрон
        /// аннигилирует в два кванта. Это не вероятность, а ожидаемое ЧИСЛО, и
        /// в линейные члены (вынос из пика, площадь суммы с гаммой) оно входит
        /// именно так — «любой из двух».
        ///
        /// ⛔ Пары 511 + 511 здесь НЕТ и не будет (решение Amber 18.08.2026):
        /// два кванта одной аннигиляции летят СТРОГО в противоположные
        /// стороны, и произведение ε_p(511)·ε_p(511) — изотропная формула —
        /// завышает вероятность их совместного попадания в разы. У одиночного
        /// детектора оба кванта попасть почти не могут вовсе. Честный счёт
        /// требует угловой части, а суммирователь геометрии не знает — та же
        /// преграда, что у S20 и N14. Отсутствие пика 1022 кэВ в модели — это
        /// решение, а не забывчивость.
        /// </summary>
        public double AnnihilationQuanta;

        /// <summary>
        /// Что получилось и что не получилось — для проб и журнала. Пусто, если
        /// сказать нечего. Без этого «поправка ничего не сделала» и «данных не
        /// нашлось» с виду одно и то же.
        /// </summary>
        public string Note = "";

        /// <summary>
        /// Полный выход K-рентгена, % на распад. ⚠ `KB` в
        /// `decay_radiations` — это ИТОГ по Kβ, а не третья линия рядом с
        /// `KpB1` и `KpB2` (`D30`); складывать всё подряд нельзя. Выбор между
        /// итогом и разложением — <see cref="KSeriesRule"/>, одно правило на
        /// весь проект.
        /// </summary>
        public double KIntensityPct;

        /// <summary>
        /// Выше этого коэффициента конверсии считаем, что гаммы у перехода нет
        /// вовсе. В `g4_gamma` встречаются значения до 9·10¹⁹ (TODO D31) — это
        /// не физика, а способ записать «переход целиком конверсионный», и без
        /// зажима любая арифметика с ним даёт бесконечность.
        /// </summary>
        const double AlphaCeiling = 1.0E4;

        /// <summary>
        /// Допуск сопоставления линии распада с переходом схемы, кэВ. Энергии
        /// приходят из разных поставок и округлены по-разному: у Hf-176 линия
        /// 306.780 против перехода 306.640, то есть 0.14.
        /// </summary>
        const double MatchKev = 0.6;

        static readonly object Gate = new object();

        static readonly Dictionary<string, CascadeAtomicData> Cache =
            new Dictionary<string, CascadeAtomicData>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Атомные участники каскада нуклида; null — сказать нечего (нет
        /// дочернего, нет рентгена и нет β⁺).
        /// </summary>
        public static CascadeAtomicData Of(string nucid)
        {
            if (string.IsNullOrEmpty(nucid))
            {
                return null;
            }

            lock (Gate)
            {
                CascadeAtomicData data;
                if (Cache.TryGetValue(nucid, out data))
                {
                    return data;
                }

                try
                {
                    data = Build(nucid);
                }
                catch (Exception error)
                {
                    // Отказ базы не должен ронять разбор — но и молчать нельзя:
                    // без записанной причины «рентгена не нашлось» неотличимо
                    // от «читатель сломан».
                    data = new CascadeAtomicData { Note = "отказ базы: " + error.Message };
                }

                Cache[nucid] = data;
                return data;
            }
        }

        static CascadeAtomicData Build(string nucid)
        {
            var data = new CascadeAtomicData();
            var notes = new StringBuilder();

            int mass = MassOf(nucid);
            if (mass <= 0)
            {
                return null;
            }

            // Излучения самого распада: гаммы, K-рентген, β⁺. Всё — на распад
            // РОДИТЕЛЯ цепочки, как и в остальной библиотеке.
            var gammaIntensity = new List<double[]>();
            double betaPlusPct = 0.0;
            string daughter = null;

            // K-серия собирается тремя вёдрами и разбирается ПОСЛЕ цикла:
            // выбор между итогом `KB` и разложением `KpB*` нельзя сделать «на
            // лету», не увидев обеих строк (`KSeriesRule`, `T50`).
            var kAlpha = new List<double[]>();
            var kBetaSplit = new List<double[]>();
            var kBetaTotal = new List<double[]>();
            var kBetaSplitSeries = new HashSet<string>(StringComparer.Ordinal);

            using (SqliteConnection connection = OpenRead(NuclideDatabasePath()))
            using (SqliteCommand command = connection.CreateCommand())
            {
                // (`S89`) Тот же зажим по уровню родителя, что и у библиотеки, и
                // из одного места: без него запрос складывал ВСЕ наборы одного
                // имени, то есть двоил распад у четырёх изомеров.
                command.CommandText =
                    "select type_a, type_c, energy_num, intensity_num from decay_radiations"
                    + " where parent_nucid = $n and intensity_num > 0"
                    + DecayParentRule.LevelClause;
                command.Parameters.AddWithValue("$n", nucid);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string kind = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        string series = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        double energy = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);
                        double intensity = reader.IsDBNull(3) ? 0.0 : reader.GetDouble(3);
                        if (!(intensity > 0.0))
                        {
                            continue;
                        }

                        if (kind == "G" && energy > 0.0)
                        {
                            gammaIntensity.Add(new[] { energy, intensity });
                        }
                        else if (kind == "X" && energy > 0.0 && KSeriesRule.IsSeries(series))
                        {
                            var line = new[] { energy, intensity };
                            if (KSeriesRule.IsBetaTotal(series))
                            {
                                kBetaTotal.Add(line);
                            }
                            else if (KSeriesRule.IsBetaSplit(series))
                            {
                                kBetaSplit.Add(line);
                                kBetaSplitSeries.Add(series);
                            }
                            else
                            {
                                kAlpha.Add(line);
                            }
                        }
                        else if (kind == "B+")
                        {
                            betaPlusPct += intensity;
                        }
                    }
                }

                // Дочерний нуклид — у основного состояния родителя. Ветвей
                // бывает несколько (у Eu-152 захват и β⁻ сразу), берём самую
                // сильную: K-рентген принадлежит атому, в который распад
                // ПРИШЁЛ, и у слабой ветви он тонет в выходе.
                //
                // (`A218`) Зажим по уровню — общий, из `DecayParentRule`. Здесь
                // стоял свой текст `l_seqno = 0`, и он был НЕ той же строгости:
                // у 30 родителей дочернего не находилось вовсе (`234PAm1`
                // лежит на уровне 2, а не на нуле), а ещё у четырёх выбирался
                // не тот — у `183HF` дочерним атомом выходил САМ ГАФНИЙ.
                //
                // ⛔ ПЕТЛИ СНИМАЕМ ЗДЕСЬ, ЯВНО. Правило их не снимает нарочно —
                // обходу ряда они нужны как изомерный переход, — а нам они
                // означали бы «атом сам себе дочерний». В выборку они попадают
                // у 511 родителей из 2535 и у 123 из них побеждают по `perc`.
                command.Parameters.Clear();
                command.CommandText =
                    "select daughter_nucid, perc, dec_type from decay_chain d"
                    + " where nucid = $n"
                    + DecayParentRule.ChainLevelClause;
                command.Parameters.AddWithValue("$n", nucid);
                double best = -1.0;
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string name = reader.IsDBNull(0) ? null : reader.GetString(0);
                        if (string.Equals(name, nucid, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        double perc;
                        if (string.IsNullOrEmpty(name)
                            || !double.TryParse(reader.IsDBNull(1) ? "" : reader.GetString(1),
                                                NumberStyles.Float, CultureInfo.InvariantCulture,
                                                out perc))
                        {
                            perc = 0.0;
                        }

                        if (name == null)
                        {
                            continue;
                        }

                        // (`S145`) Берутся ВСЕ ветви, а не одна сильнейшая.
                        // Сильнейшая по-прежнему становится сводным `Daughter`
                        // ради прежних читателей, но физика считается по каждой.
                        int branchZ = ChargeOf(name);
                        int branchA = MassOf(name);
                        if (branchZ > 0 && branchA > 0)
                        {
                            data.Branches.Add(new Branch
                            {
                                Nucid = name,
                                Z = branchZ,
                                A = branchA,
                                Perc = perc,
                                DecType = reader.IsDBNull(2) ? null : reader.GetString(2)
                            });
                        }

                        if (perc > best)
                        {
                            best = perc;
                            daughter = name;
                        }
                    }
                }
            }

            data.Daughter = daughter;

            // K-серия: Kα целиком плюс ОДНО из двух представлений Kβ.
            data.KLines.AddRange(kAlpha);
            data.KLines.AddRange(KSeriesRule.Beta(kBetaSplit, kBetaTotal, kBetaSplitSeries.Count));
            data.KLines.Sort((a, b) => a[0].CompareTo(b[0]));
            data.KIntensityPct = 0.0;
            foreach (double[] kLine in data.KLines)
            {
                data.KIntensityPct += kLine[1];
            }

            // Два кванта на позитрон. Аннигиляция идёт по месту остановки
            // позитрона, то есть практически мгновенно.
            data.AnnihilationQuanta = 2.0 * betaPlusPct / 100.0;
            data.GammaIntensity = gammaIntensity;

            if (data.KLines.Count == 0 && !(data.AnnihilationQuanta > 0.0))
            {
                return null;
            }

            int z = daughter != null ? ChargeOf(daughter) : 0;
            if (z <= 0)
            {
                notes.Append("дочерний не определён; ");
                data.Note = notes.ToString();
                return data.KLines.Count > 0 || data.AnnihilationQuanta > 0.0 ? data : null;
            }

            MaterialDatabase.Fluorescence fluorescence = MaterialDatabase.FluorescenceOf(z);
            data.OmegaK = fluorescence != null ? fluorescence.Omega(true) : 0.0;

            // Ветвей может не найтись вовсе (старые/неполные поставки) — тогда
            // работает одна, собранная из сводных полей: поведение прежнее.
            if (data.Branches.Count == 0)
            {
                data.Branches.Add(new Branch
                {
                    Nucid = daughter, Z = z, A = mass, Perc = 100.0
                });
            }

            SplitKLines(data, notes);

            // ⛔ ПО ВЕТВЯМ, А НЕ ПО ОДНОЙ (`S145`). У каждой своя схема уровней,
            // свой ω_K, свои K-линии и свой остаток захватных вакансий. Гамма
            // достаётся ТОЙ ветви, в схеме которой нашлась, и помечается её Z.
            // Ключ — НОМЕР ВЕТВИ, а не Z: у двух ветвей Z совпадает (`S148`).
            var halfLifeOf = new Dictionary<int, Dictionary<int, double>>();
            for (int index = 0; index < data.Branches.Count; index++)
            {
                Branch branch = data.Branches[index];
                MaterialDatabase.Fluorescence bf = MaterialDatabase.FluorescenceOf(branch.Z);
                branch.OmegaK = bf != null ? bf.Omega(true) : 0.0;

                List<Transition> scheme;
                Dictionary<int, double> halfLife;
                LoadScheme(branch.Z, branch.A, out scheme, out halfLife, notes);
                halfLifeOf[index] = halfLife;

                double conversionVacancy = 0.0;
                foreach (double[] line in gammaIntensity)
                {
                    if (data.Gammas.ContainsKey(line[0]))
                    {
                        // Линия уже разобрана более сильной ветвью: она может
                        // принадлежать только одной.
                        continue;
                    }

                    Transition match = MatchTransition(scheme, line[0]);
                    if (match == null)
                    {
                        continue;
                    }

                    match.BranchIndex = index;
                    data.Gammas[line[0]] = match;
                    conversionVacancy += line[1] / 100.0 * match.AlphaK;
                }

                if (branch.OmegaK > 0.0)
                {
                    double total = branch.KIntensityPct / 100.0 / branch.OmegaK;
                    double prompt = total - conversionVacancy;
                    if (prompt < -0.02)
                    {
                        // Отрицательный остаток физически невозможен: вакансий от
                        // конверсии не может быть больше, чем их всего. Значит
                        // сопоставление взяло не тот переход — ровно та беда, что
                        // описана в TODO D31. Говорим об этом вслух.
                        notes.AppendFormat(CultureInfo.InvariantCulture,
                            "{0}: остаток вакансий отрицателен ({1:F4}), сопоставление под подозрением; ",
                            branch.Nucid, prompt);
                    }

                    branch.PromptVacancy = prompt > 0.0 ? prompt : 0.0;
                }
                else
                {
                    notes.Append("нет ω_K для Z="
                                 + branch.Z.ToString(CultureInfo.InvariantCulture) + "; ");
                }
            }

            // Задержки — ПО СВОЕЙ схеме у каждой ветви: номера уровней у разных
            // ядер свои, и общий ход по ним смешал бы чужие пути (`A290`).
            for (int index = 0; index < data.Branches.Count; index++)
            {
                Dictionary<int, double> halfLife;
                if (!halfLifeOf.TryGetValue(index, out halfLife))
                {
                    continue;
                }

                var own = new Dictionary<double, Transition>();
                foreach (KeyValuePair<double, Transition> pair in data.Gammas)
                {
                    if (pair.Value.BranchIndex == index)
                    {
                        own.Add(pair.Key, pair.Value);
                    }
                }

                Delays(own, halfLife);
            }

            // Сводные поля — у сильнейшей ветви, ради прежних читателей.
            foreach (Branch branch in data.Branches)
            {
                if (branch.Z == z)
                {
                    data.PromptVacancy = branch.PromptVacancy;
                    break;
                }
            }

            data.Note = notes.ToString();
            return data;
        }

        /// <summary>
        /// Переход схемы, отвечающий линии распада.
        ///
        /// ⛔ БЛИЖАЙШИЙ ПО ЭНЕРГИИ — НЕВЕРНОЕ ПРАВИЛО, и это стоило часа.
        /// У Hf-176 на линию распада 306.780 кэВ приходится три кандидата:
        /// настоящий 3→2 (306.640, уровень 596.82 кэВ, интенсивность 100 %) и
        /// два самозванца с уровней 3467 и 3847 кэВ, которых β-распад Lu-176
        /// (Q = 1194 кэВ) населить не может в принципе. Ближе по энергии
        /// оказывается САМОЗВАНЕЦ — на 0.02 кэВ, — и с ним число вакансий от
        /// конверсии выходило БОЛЬШЕ полного, то есть отрицательный захват.
        ///
        /// Правило поэтому такое: сперва выбросить переходы, которых в природе
        /// не испускают (нулевая относительная интенсивность), затем брать
        /// переход с САМОГО НИЗКОГО уровня, и лишь при равенстве — ближайший по
        /// энергии.
        /// </summary>
        static Transition MatchTransition(List<Transition> scheme, double energyKev)
        {
            Transition best = null;
            double bestDelta = 0.0;
            foreach (Transition candidate in scheme)
            {
                double delta = Math.Abs(candidate.EnergyKev - energyKev);
                if (delta >= MatchKev)
                {
                    continue;
                }

                if (best == null
                    || candidate.FromSeq < best.FromSeq
                    || (candidate.FromSeq == best.FromSeq && delta < bestDelta))
                {
                    best = candidate;
                    bestDelta = delta;
                }
            }

            return best;
        }

        /// <summary>
        /// Через сколько секунд после распада вылетает каждый квант.
        ///
        /// Ход по схеме сверху вниз: квант перехода с уровня L вылетает через
        /// `приход(L) + T½(L)`, а приход на L — это самый поздний из вылетов
        /// тех переходов, что на L приводят. Уровни идут по убыванию номера,
        /// то есть родители обработаны раньше потомков.
        ///
        /// ⚠ Приближение названо: когда уровень населяется И напрямую распадом,
        /// И через долгоживущий уровень сверху, берётся ПОЗДНЕЕ из двух. Это
        /// сторона осторожная (совпадений получится меньше, а не больше), и на
        /// корпусе она точна — там у всех таких уровней путь один. Разбор по
        /// долям населённости — остаток, TODO S58.
        /// </summary>
        /// <summary>
        /// Развести K-линии между дочерними атомами ветвей (`S145`).
        ///
        /// ⛔ В поставке они лежат ОДНИМ ведром: у Eu-152 основная серия
        /// самария около 39.5–46.6 кэВ, а строка 42.996 кэВ принадлежит
        /// гадолинию, и прежний код применял ко всем один ω_K сильнейшего.
        ///
        /// ⛔ ПРАВИЛО «БЛИЖАЙШИЙ КРАЙ СВЕРХУ» НЕГОДНО, и это поймано своей же
        /// пробой, а не рассуждением. K-линии ТЯЖЁЛОГО атома лежат НИЖЕ края
        /// лёгкого: у Lu-176 (ветви Hf-176 и Yb-176) Kα гафния 55.8 кэВ стоит
        /// ниже края иттербия 61.3, и правило отдавало гафниевый рентген
        /// иттербию — баланс вакансий гафния уходил в минус. То есть первая
        /// редакция чинила Eu-152 ценой поломки Lu-176.
        ///
        /// Годное правило — ДОЛЯ ОТ КРАЯ, и она устойчива по всей таблице:
        /// Kα2 ≈ 0.844, Kα1 ≈ 0.855, Kβ1 ≈ 0.969 от K-края (мерено по Sm, Gd,
        /// Hf, Yb — разброс в третьем знаке). Линия достаётся атому, у которого
        /// она ближе всего к одной из этих трёх опор; линия выше края атому не
        /// принадлежит в принципе и такой атом не рассматривается.
        ///
        /// Линия, которой не нашлось хозяина, остаётся у сильнейшей ветви, и
        /// это НАЗЫВАЕТСЯ в примечании — молча приписывать её было бы тем же
        /// дефектом в мелком масштабе.
        /// </summary>
        /// <summary>
        /// Доли K-линий от K-края: Kα2, Kα1, Kβ1. Мерены по Sm, Gd, Hf, Yb —
        /// разброс в третьем знаке, чего для разведения соседних атомов хватает
        /// с запасом (соседи расходятся на единицы кэВ).
        /// </summary>
        static readonly double[] KLineShares = { 0.844, 0.855, 0.969 };

        /// <summary>
        /// Насколько близкими считать две опоры, чтобы решала доля ветви, кэВ.
        /// </summary>
        const double KLineTieKev = 0.30;

        static void SplitKLines(CascadeAtomicData data, StringBuilder notes)
        {
            int homeless = 0;
            Branch strongest = null;
            foreach (Branch branch in data.Branches)
            {
                if (strongest == null || branch.Perc > strongest.Perc)
                {
                    strongest = branch;
                }
            }

            foreach (double[] line in data.KLines)
            {
                Branch owner = null;
                double bestAway = double.MaxValue;
                foreach (Branch branch in data.Branches)
                {
                    // ⛔ ВЕТВЬ НУЛЕВОЙ ВЕРОЯТНОСТИ ХОЗЯИНОМ НЕ БЫВАЕТ, и это
                    // тоже поймано пробой: у Mn-54 в `decay_chain` числится
                    // ветвь 54FE с долей 0.00 %, и она забирала 3.05 % K-линий
                    // хрома — захват падал 0.8913 → 0.7855 на пустом месте.
                    if (!(branch.Perc > 0.0))
                    {
                        continue;
                    }

                    MaterialDatabase.Fluorescence f = MaterialDatabase.FluorescenceOf(branch.Z);
                    if (f == null || !(f.KEdgeKev > line[0]))
                    {
                        continue;
                    }

                    foreach (double share in KLineShares)
                    {
                        double away = Math.Abs(line[0] - share * f.KEdgeKev);

                        // Соседние Z дают опоры в единицах кэВ друг от друга, и
                        // при почти равной близости решает ДОЛЯ ВЕТВИ: у Cr Kβ
                        // (5.95) опоры хрома и железа расходятся на 0.1 кэВ, а
                        // ветви — на порядки.
                        bool better = away < bestAway - KLineTieKev
                                      || (away < bestAway + KLineTieKev
                                          && owner != null && branch.Perc > owner.Perc)
                                      || owner == null;
                        if (better && away < bestAway + KLineTieKev)
                        {
                            if (away < bestAway)
                            {
                                bestAway = away;
                            }

                            owner = branch;
                        }
                    }
                }

                if (owner == null)
                {
                    owner = strongest;
                    homeless++;
                }

                if (owner == null)
                {
                    continue;
                }

                owner.KLines.Add(line);
                owner.KIntensityPct += line[1];
            }

            if (homeless > 0 && data.Branches.Count > 1)
            {
                notes.AppendFormat(CultureInfo.InvariantCulture,
                    "K-линий без своего края {0}, отданы сильнейшей ветви; ", homeless);
            }
        }

        static readonly Phase[] EmptyLives = new Phase[0];

        static void Delays(Dictionary<double, Transition> gammas, Dictionary<int, double> halfLife)
        {
            var levels = new List<int>();
            foreach (Transition transition in gammas.Values)
            {
                if (!levels.Contains(transition.FromSeq))
                {
                    levels.Add(transition.FromSeq);
                }
            }

            levels.Sort();
            levels.Reverse();

            var arrival = new Dictionary<int, double>();

            // (`A289`) Тот же ход, но копится ещё и СПИСОК периодов пути:
            // выбор «позднее из двух» у обоих один, иначе список отвечал бы за
            // один путь, а сумма — за другой.
            var arrivalLives = new Dictionary<int, Phase[]>();
            foreach (int level in levels)
            {
                double came;
                if (!arrival.TryGetValue(level, out came))
                {
                    came = 0.0;
                }

                Phase[] cameLives;
                if (!arrivalLives.TryGetValue(level, out cameLives))
                {
                    cameLives = EmptyLives;
                }

                double life;
                if (!halfLife.TryGetValue(level, out life) || !(life > 0.0))
                {
                    life = 0.0;
                }

                double emitted = came + life;
                Phase[] lives = cameLives;
                if (life > 0.0)
                {
                    lives = new Phase[cameLives.Length + 1];
                    Array.Copy(cameLives, lives, cameLives.Length);
                    lives[cameLives.Length] = new Phase { Seq = level, HalfLifeSec = life };
                }

                foreach (Transition transition in gammas.Values)
                {
                    if (transition.FromSeq != level)
                    {
                        continue;
                    }

                    transition.EmitDelaySec = emitted;
                    transition.EmitPhases = lives;
                    double have;
                    if (!arrival.TryGetValue(transition.ToSeq, out have) || emitted > have)
                    {
                        arrival[transition.ToSeq] = emitted;
                        arrivalLives[transition.ToSeq] = lives;
                    }
                }
            }
        }

        static void LoadScheme(int z, int a, out List<Transition> scheme,
                               out Dictionary<int, double> halfLife, StringBuilder notes)
        {
            scheme = new List<Transition>();
            halfLife = new Dictionary<int, double>();

            string path = SchemeDatabasePath();
            if (!File.Exists(path))
            {
                notes.Append("нет schemedb.sqlite рядом с программой; ");
                return;
            }

            using (SqliteConnection connection = OpenRead(path))
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select energy_ev, icc_total, icc_k_ppm, from_seq, to_seq, intensity_ppm"
                    + " from g4_gamma where z = $z and a = $a";
                command.Parameters.AddWithValue("$z", z);
                command.Parameters.AddWithValue("$a", a);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Переход, которого не испускают, кандидатом быть не
                        // может: см. MatchTransition.
                        if (reader.IsDBNull(5) || reader.GetDouble(5) <= 0.0)
                        {
                            continue;
                        }

                        double alphaTotal = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
                        if (alphaTotal > AlphaCeiling)
                        {
                            alphaTotal = AlphaCeiling;
                        }

                        double kShare = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2) / 1.0E6;
                        double alphaK = alphaTotal * kShare;
                        if (alphaK > alphaTotal)
                        {
                            alphaK = alphaTotal;
                        }

                        scheme.Add(new Transition
                        {
                            EnergyKev = reader.GetDouble(0) / 1000.0,
                            AlphaK = alphaK > 0.0 ? alphaK : 0.0,
                            FromSeq = reader.GetInt32(3),
                            ToSeq = reader.GetInt32(4)
                        });
                    }
                }

                command.CommandText =
                    "select seq, half_life_sec from g4_level where z = $z and a = $a";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(1))
                        {
                            halfLife[reader.GetInt32(0)] = reader.GetDouble(1);
                        }
                    }
                }
            }
        }

        static SqliteConnection OpenRead(string path)
        {
            SqliteConnection connection = new SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;Cache=Shared;");
            connection.Open();
            return connection;
        }

        /// <summary>
        /// Разбор `nucid` на массу, символ элемента и метку состояния:
        /// «176HF» → (176, «HF», «»), «234PAm1» → (234, «PA», «m1»),
        /// «108AGm» → (108, «AG», «m»), «105PDe» → (105, «PD», «e»).
        ///
        /// ⛔ **Метку состояния от символа отделяет РЕГИСТР, а не буква**, и
        /// это единственное место в проекте, где правило записано. Символ в
        /// `nucid` стоит ЗАГЛАВНЫМИ целиком, метка — строчными. Правило «хвост
        /// M или m — изомер» неверно и ломает 176 нуклидов базы разом: всё, чей
        /// символ кончается на M, то есть Am, Cm, Fm, Tm, Sm, Pm. Поймано
        /// измерением 18.08.2026 (`S56`, `D32`): америций разбирался как
        /// «A-241m», не сходился с истиной и шёл в фантомы.
        ///
        /// Возвращает false, если разобрать не удалось: цифр в начале нет либо
        /// заглавных букв после них не осталось.
        /// </summary>
        public static bool SplitNucid(string nucid, out int mass, out string symbol, out string state)
        {
            mass = 0;
            symbol = "";
            state = "";
            if (string.IsNullOrEmpty(nucid))
            {
                return false;
            }

            string text = nucid.Trim();
            int digits = 0;
            while (digits < text.Length && char.IsDigit(text[digits]))
            {
                digits++;
            }

            if (digits == 0 || digits >= text.Length
                || !int.TryParse(text.Substring(0, digits), NumberStyles.None,
                                 CultureInfo.InvariantCulture, out mass))
            {
                mass = 0;
                return false;
            }

            string tail = text.Substring(digits);

            // Хвост состояния: сначала цифры номера состояния, затем строчные
            // буквы самой метки. Оба куска необязательны.
            int end = tail.Length;
            while (end > 0 && char.IsDigit(tail[end - 1]))
            {
                end--;
            }

            int letters = end;
            while (letters > 0 && char.IsLower(tail[letters - 1]))
            {
                letters--;
            }

            // Хотя бы одна заглавная обязана остаться: иначе это не символ
            // элемента, и разбирать нечего.
            if (letters == 0)
            {
                mass = 0;
                return false;
            }

            symbol = tail.Substring(0, letters);
            state = tail.Substring(letters);
            return true;
        }

        /// <summary>Массовое число из `nucid`: «176HF» → 176, «234PAm1» → 234.</summary>
        public static int MassOf(string nucid)
        {
            int mass;
            string symbol, state;
            return SplitNucid(nucid, out mass, out symbol, out state) ? mass : 0;
        }

        /// <summary>
        /// Заряд по `nucid`: «176HF» → 72, «234PAm1» → 91. Через символ элемента
        /// в <see cref="MaterialDatabase"/> — второй таблицы соответствий в
        /// проекте заводить не надо.
        ///
        /// ⚠ Изомер разбирается наравне с основным состоянием, и это не
        /// украшение: **Th-234 распадается именно в Pa-234m1**, то есть без
        /// такого разбора урановый ряд терял ω_K, схему уровней и весь
        /// K-рентген партнёром каскада у одного из двух своих сильных
        /// излучателей (`D32`).
        /// </summary>
        public static int ChargeOf(string nucid)
        {
            int mass;
            string symbol, state;
            if (!SplitNucid(nucid, out mass, out symbol, out state))
            {
                return 0;
            }

            for (int z = 1; z <= 103; z++)
            {
                if (string.Equals(MaterialDatabase.SymbolOf(z), symbol,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    return z;
                }
            }

            return 0;
        }

        static string NuclideDatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        }

        static string SchemeDatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "schemedb.sqlite");
        }
    }
}
