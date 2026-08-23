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
            /// Через сколько секунд после распада вылетает этот квант. Ноль —
            /// мгновенно. Считается ходом по схеме уровней, см.
            /// <see cref="Delays"/>.
            /// </summary>
            public double EmitDelaySec;
        }

        /// <summary>K-линии дочернего атома: энергия и выход, % на распад.</summary>
        public List<double[]> KLines = new List<double[]>();

        /// <summary>Выход флуоресценции K, доля.</summary>
        public double OmegaK;

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
                command.CommandText =
                    "select type_a, type_c, energy_num, intensity_num from decay_radiations"
                    + " where parent_nucid = $n and intensity_num > 0";
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
                command.Parameters.Clear();
                command.CommandText =
                    "select daughter_nucid, perc from decay_chain"
                    + " where nucid = $n and l_seqno = 0";
                command.Parameters.AddWithValue("$n", nucid);
                double best = -1.0;
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string name = reader.IsDBNull(0) ? null : reader.GetString(0);
                        double perc;
                        if (string.IsNullOrEmpty(name)
                            || !double.TryParse(reader.IsDBNull(1) ? "" : reader.GetString(1),
                                                NumberStyles.Float, CultureInfo.InvariantCulture,
                                                out perc))
                        {
                            perc = 0.0;
                        }

                        if (name != null && perc > best)
                        {
                            best = perc;
                            daughter = name;
                        }
                    }
                }
            }

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

            // Схема уровней дочернего: коэффициенты конверсии и времена жизни.
            List<Transition> scheme;
            Dictionary<int, double> halfLife;
            LoadScheme(z, mass, out scheme, out halfLife, notes);

            double conversionVacancy = 0.0;
            foreach (double[] line in gammaIntensity)
            {
                Transition match = MatchTransition(scheme, line[0]);
                if (match == null)
                {
                    continue;
                }

                data.Gammas[line[0]] = match;
                conversionVacancy += line[1] / 100.0 * match.AlphaK;
            }

            Delays(data.Gammas, halfLife);

            if (data.OmegaK > 0.0)
            {
                double total = data.KIntensityPct / 100.0 / data.OmegaK;
                double prompt = total - conversionVacancy;
                if (prompt < -0.02)
                {
                    // Отрицательный остаток физически невозможен: вакансий от
                    // конверсии не может быть больше, чем их всего. Значит
                    // сопоставление взяло не тот переход — ровно та беда, что
                    // описана в TODO D31. Говорим об этом вслух.
                    notes.AppendFormat(CultureInfo.InvariantCulture,
                        "остаток вакансий отрицателен ({0:F4}), сопоставление под подозрением; ",
                        prompt);
                }

                data.PromptVacancy = prompt > 0.0 ? prompt : 0.0;
            }
            else
            {
                notes.Append("нет ω_K для Z=" + z + "; ");
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
            foreach (int level in levels)
            {
                double came;
                if (!arrival.TryGetValue(level, out came))
                {
                    came = 0.0;
                }

                double life;
                if (!halfLife.TryGetValue(level, out life) || !(life > 0.0))
                {
                    life = 0.0;
                }

                double emitted = came + life;
                foreach (Transition transition in gammas.Values)
                {
                    if (transition.FromSeq != level)
                    {
                        continue;
                    }

                    transition.EmitDelaySec = emitted;
                    double have;
                    if (!arrival.TryGetValue(transition.ToSeq, out have) || emitted > have)
                    {
                        arrival[transition.ToSeq] = emitted;
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
