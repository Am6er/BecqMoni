using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CascadeClampProbe
{
    /// <summary>
    /// ЦЕНА ЗАЖИМА ДОЛИ СОВПАДЕНИЯ БОЛЬШЕ ЕДИНИЦЫ (`D49`).
    ///
    ///     cascadeclampprobe --matrix=&lt;файл .rmx&gt; [--nuclides=Bi-207,Ir-192]
    ///                       [--scint=NaI:Tl] [--all-affected] [--csv=&lt;файл&gt;]
    ///
    /// ## Что меряется
    ///
    /// В поставке SandiaDecay доля совпадения `fraction` у 1820 пар (1.42 %) у
    /// 99 родителей больше единицы — для условной вероятности это невозможно.
    /// Потребителей у испорченной строки ДВА, и ведут они себя по-разному:
    ///
    ///   * `FsaCascadeSummer.SurviveAll` — ВЫНОС из пика. Долю зажимает в
    ///     единицу, то есть считает «партнёр вылетел наверняка»: вынос
    ///     максимальный из возможных, но конечный.
    ///   * `FsaCascadeSummer.PairBase` — ПЛОЩАДЬ сумм-события и влёт `inShare`
    ///     в CF линии, куда эта сумма попала. Зажима нет вовсе: доля 129
    ///     множит площадь на 129.
    ///
    /// Проба гоняет ОДИН и тот же нуклид на ОДНОЙ и той же матрице четырьмя
    /// правилами (<see cref="FsaCascadeSummer.SuperUnitRule"/>) и печатает,
    /// насколько разошлись образы: множители линий, суммарная пиковая площадь
    /// компонента и площадь сумм-пиков. Разность между правилами и есть цена
    /// решения, которое надо принять.
    ///
    /// ⚠ Никакого «правильного» правила проба не выбирает: она даёт числа, по
    /// которым правило выбрано. ✅ С 10.09.2026 умолчание приложения — `Clamp`
    /// (решение Amber вопросником: «Зажимать в ОБОИХ потребителях + счётчик»),
    /// и проба его ПЕЧАТАЕТ: правило, стоящее в дереве, судится наравне с
    /// числами. Плечи ставят своё правило и возвращают умолчание назад.
    ///
    /// ⛔ Счётчик зажима РАЗДЕЛЁН на два случая и печатается разделённым:
    /// «поставка» — доля списана из `sandia.decay.xml` дословно и больше
    /// единицы, то есть дефект поставки; «счёт» — величину посчитало
    /// приложение (обратная условная `P(A|B) = P(B|A)·I(A)/I(B)`, атомные
    /// партнёры), и там больше единицы бывает законно. У `Co-60` поставка
    /// безупречна (`fraction` = 0.999872), а зажим срабатывает четыре раза —
    /// сложенный счётчик послал бы чинить поставку там, где чинить нечего.
    ///
    /// ## Положительный контроль — в самой печати
    ///
    /// Нуклид БЕЗ единой доли больше единицы обязан дать ЧЕТЫРЕ одинаковых
    /// плеча (счётчики нулевые, расхождение 0.000000 %). Если разошлись —
    /// рычаг задевает не то, что должен. Штатный такой вход — `Cs-137`,
    /// и он берётся сам собой при `--all-affected`.
    ///
    /// Коды возврата: 0 — посчитано; 2 — мерить нечем (нет матрицы, нет базы,
    /// нуклид не разбирается); 1 — положительный контроль нарушен.
    /// </summary>
    static class Program
    {
        /// <summary>
        /// УМОЛЧАНИЕ ПРИЛОЖЕНИЯ, снятое ДО первого плеча (`D49`). Плечи ставят
        /// правило сами, а по окончании возвращают сюда — иначе проба судила бы
        /// приложение по правилу, которое сама же и оставила.
        /// </summary>
        static readonly FsaCascadeSummer.SuperUnitRule Default =
            FsaCascadeSummer.SuperUnitPolicy;

        /// <summary>Строка отчёта об одном плече.</summary>
        sealed class Arm
        {
            public FsaCascadeSummer.SuperUnitRule Rule;
            public double[] Factors;
            public double PeakArea;      // Σ I/100 · ε_p(E) · множитель
            public double SumArea;       // Σ площадей сумм-пиков
            public int Shares;           // сколько раз доля партнёра > 1
            public int SharesSupply;     // из них на СПИСАННОЙ из поставки доле
            public int SharesDerived;    // из них на ПОСЧИТАННОЙ нами доле
            public int Pairs;            // сколько раз доля пары > 1
            public int PairsSupply;      // из них на списанной из поставки паре
            public int PairsDerived;     // из них на посчитанной нами паре
            public double WorstShare;
            public double WorstPair;
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string matrixFile = null;
            string scint = "NaI:Tl";
            string csv = null;
            bool allAffected = false;
            List<string> names = new List<string>();
            // (`S58`) Энергии, на которых надо назвать эффективности: ими
            // меряется, какой вынос мог бы добавить партнёр низкой энергии.
            List<double> effAt = new List<double>();

            foreach (string a in args)
            {
                if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixFile = a.Substring(9);
                else if (a.StartsWith("--scint=", StringComparison.Ordinal)) scint = a.Substring(8);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
                else if (a == "--all-affected") allAffected = true;
                else if (a.StartsWith("--eff=", StringComparison.Ordinal))
                {
                    foreach (string e in a.Substring(6).Split(','))
                    {
                        double kev;
                        if (double.TryParse(e.Trim(), NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out kev))
                        {
                            effAt.Add(kev);
                        }
                    }
                }
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal))
                {
                    foreach (string n in a.Substring(11).Split(','))
                    {
                        if (n.Trim().Length > 0) names.Add(n.Trim());
                    }
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (matrixFile == null || !File.Exists(matrixFile))
            {
                Console.Error.WriteLine("нужен --matrix=<файл .rmx>; корпус: tools/CORPUS/corpus/geometries/");
                return 2;
            }

            string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(db))
            {
                Console.Error.WriteLine("рядом с пробой нет nucdb.sqlite: " + db);
                return 2;
            }

            if (names.Count == 0 && !allAffected)
            {
                names.Add("Bi-207");
            }

            if (allAffected)
            {
                // Все родители корпуса и приложения, у которых есть испорченные
                // строки, плюс заведомо чистый Cs-137 положительным контролем.
                foreach (string n in new[] { "Bi-207", "Ir-192", "I-132", "Cs-132",
                                             "Pm-146", "Sn-115m", "In-114m",
                                             // ⛔ Контроль — нуклид С КАСКАДОМ, но без
                                             // единой испорченной доли. `Cs-137` на эту
                                             // роль НЕ ГОДИТСЯ: пар у него нет вовсе, и
                                             // плечи совпали бы, даже будь рычаг мёртв.
                                             "Co-60", "Eu-152" })
                {
                    if (!names.Contains(n)) names.Add(n);
                }
            }

            MatrixRefusal refusal;
            int format;
            ResponseMatrix matrix = ResponseMatrix.Load(matrixFile, out refusal, out format);
            if (matrix == null)
            {
                Console.Error.WriteLine("матрица не прочитана: " + refusal);
                return 2;
            }

            // ⚠ `format` из `Load` — НЕ версия прочитанного файла: он ставится
            // только при отказе «старый формат», а при удаче остаётся нулём.
            // Печатать его как «формат 0» значило бы врать читателю отчёта.
            Console.WriteLine("матрица: " + Path.GetFileName(matrixFile)
                              + string.Format(CultureInfo.InvariantCulture,
                                              " (узлов {0}, историй {1})",
                                              matrix.Energies.Length, matrix.Histories));
            Console.WriteLine("клеймо: " + matrix.Stamp);

            // (`S58`) НИЖНИЙ УЗЕЛ СЕТКИ — им решается, доедет ли партнёр
            // низкой энергии (L-рентген Am-241 17.14 кэВ, Hf 9.11 кэВ) до
            // счёта вообще. Печатается всегда: это тот вход, по которому
            // судят, есть ли у L-серии работа.
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "сетка узлов: {0:F3} … {1:F1} кэВ",
                matrix.Energies[0], matrix.Energies[matrix.Energies.Length - 1]));
            Console.WriteLine("кристалл: " + scint);
            Console.WriteLine("умолчание приложения: " + Default
                              + (Default == FsaCascadeSummer.SuperUnitRule.Clamp
                                 ? "  ✔ правило Amber 10.09.2026 (`D49`)"
                                 : "  ⚠ НЕ правило Amber: ждали Clamp"));
            Console.WriteLine();

            if (effAt.Count > 0)
            {
                FsaCascadeSummer eff = FsaCascadeSummer.Create(matrix, scint);
                if (eff == null)
                {
                    Console.Error.WriteLine("суммирователь не построился: нет nucdb или матрица без каналов");
                    return 2;
                }

                Console.WriteLine();
                Console.WriteLine("     E, кэВ     ε_пик      ε_полн");
                foreach (double kev in effAt)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,9:F2}  {1,9:F6}  {2,9:F6}",
                        kev, eff.PeakEfficiency(kev), eff.TotalEfficiency(kev)));
                }

                Console.WriteLine();
            }

            TextWriter sheet = null;
            if (csv != null)
            {
                sheet = new StreamWriter(csv, false, new UTF8Encoding(false));
                sheet.WriteLine("nuclide,rule,lines,shares_over_1,shares_supply,shares_derived,"
                                + "pairs_over_1,pairs_supply,pairs_derived,worst_share,"
                                + "worst_pair,peak_area,sum_area,peak_vs_asis_pct,"
                                + "sum_vs_asis_pct,worst_line_kev,worst_line_pct");
            }

            int bad = 0;
            foreach (string name in names)
            {
                if (!Report(matrix, scint, db, name, sheet, ref bad))
                {
                    Console.WriteLine("  " + name + ": нуклид не разбирается — пропущен");
                    Console.WriteLine();
                }
            }

            if (sheet != null) sheet.Close();
            return bad > 0 ? 1 : 0;
        }

        /// <summary>Один нуклид: четыре плеча и их расхождение.</summary>
        static bool Report(ResponseMatrix matrix, string scint, string db, string name,
                           TextWriter sheet, ref int bad)
        {
            FsaComponent proto = Build(db, name);
            if (proto == null || proto.Lines.Count == 0)
            {
                return false;
            }

            var rules = new[]
            {
                FsaCascadeSummer.SuperUnitRule.AsIs,
                FsaCascadeSummer.SuperUnitRule.Clamp,
                FsaCascadeSummer.SuperUnitRule.Drop,
                FsaCascadeSummer.SuperUnitRule.Raw
            };

            List<Arm> arms = new List<Arm>();
            foreach (FsaCascadeSummer.SuperUnitRule rule in rules)
            {
                arms.Add(Measure(matrix, scint, proto, rule));
            }

            Arm baseline = arms[0];
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0}: линий {1}, долей > 1 в выносе {2} (поставка {3} / счёт {4}),"
                + " в парах {5} (поставка {6} / счёт {7})",
                name, proto.Lines.Count, baseline.Shares, baseline.SharesSupply,
                baseline.SharesDerived, baseline.Pairs, baseline.PairsSupply,
                baseline.PairsDerived));
            if (baseline.Shares > 0 || baseline.Pairs > 0)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  худшая доля партнёра {0:F3} @ {1:F2} кэВ; худшая доля пары {2:F3}",
                    baseline.WorstShare, FsaCascadeSummer.WorstSuperUnitShareKev,
                    baseline.WorstPair));
            }

            Console.WriteLine("  правило      пиковая площадь   сумм-пики    пик к AsIs   сумм к AsIs");
            foreach (Arm arm in arms)
            {
                double dp = Delta(arm.PeakArea, baseline.PeakArea);
                double ds = Delta(arm.SumArea, baseline.SumArea);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10} {1,16:E5} {2,12:E4} {3,12:F6} % {4,12:F6} %",
                    arm.Rule, arm.PeakArea, arm.SumArea, dp, ds));
            }

            // Худшая ЛИНИЯ: где множитель образа разошёлся сильнее всего.
            for (int a = 1; a < arms.Count; a++)
            {
                double worst = 0.0;
                double worstKev = 0.0;
                for (int i = 0; i < proto.Lines.Count; i++)
                {
                    double d = Delta(arms[a].Factors[i], baseline.Factors[i]);
                    if (Math.Abs(d) > Math.Abs(worst))
                    {
                        worst = d;
                        worstKev = proto.Lines[i].Energy;
                    }
                }

                if (Math.Abs(worst) > 0.0)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  худшая линия {0} против AsIs: {1:F2} кэВ, множитель образа {2:F4} %",
                        arms[a].Rule, worstKev, worst));
                }

                if (sheet != null)
                {
                    sheet.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9:F4},{10:F4},{11:E6},{12:E6},"
                        + "{13:F6},{14:F6},{15:F2},{16:F6}",
                        name, arms[a].Rule, proto.Lines.Count,
                        arms[a].Shares, arms[a].SharesSupply, arms[a].SharesDerived,
                        arms[a].Pairs, arms[a].PairsSupply, arms[a].PairsDerived,
                        arms[a].WorstShare, arms[a].WorstPair, arms[a].PeakArea, arms[a].SumArea,
                        Delta(arms[a].PeakArea, baseline.PeakArea),
                        Delta(arms[a].SumArea, baseline.SumArea), worstKev, worst));
                }
            }

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. У нуклида без единой доли больше
            // единицы четыре плеча обязаны совпасть: рычаг режет ИМЕННО
            // испорченную строку, а не что попало.
            if (baseline.Shares == 0 && baseline.Pairs == 0)
            {
                bool same = true;
                for (int a = 1; a < arms.Count; a++)
                {
                    if (Math.Abs(Delta(arms[a].PeakArea, baseline.PeakArea)) > 1.0E-9
                        || Math.Abs(Delta(arms[a].SumArea, baseline.SumArea)) > 1.0E-9)
                    {
                        same = false;
                    }
                }

                Console.WriteLine(same
                    ? "  ✔ контроль: испорченных строк нет — все четыре плеча совпали"
                    : "  ⛔ КОНТРОЛЬ НАРУШЕН: испорченных строк нет, а плечи разошлись");
                if (!same) bad++;
            }

            Console.WriteLine();
            return true;
        }

        /// <summary>Одно плечо: свой суммирователь, своё правило, свои числа.</summary>
        static Arm Measure(ResponseMatrix matrix, string scint, FsaComponent proto,
                           FsaCascadeSummer.SuperUnitRule rule)
        {
            FsaCascadeSummer.SuperUnitPolicy = rule;
            FsaCascadeSummer.ResetSuperUnitCounters();

            // Экземпляр СВОЙ на плечо: `Correction` кэшируется в экземпляре, и
            // общий суммирователь вернул бы второму плечу числа первого.
            FsaCascadeSummer summer = FsaCascadeSummer.Create(matrix, scint);
            FsaComponent component = Clone(proto);

            Arm arm = new Arm();
            arm.Rule = rule;
            arm.Factors = new double[component.Lines.Count];
            for (int i = 0; i < arm.Factors.Length; i++) arm.Factors[i] = 1.0;

            if (summer != null)
            {
                FsaCascadeSummer.Correction correction = summer.For(component);
                if (correction != null && correction.LineFactors != null)
                {
                    for (int i = 0; i < arm.Factors.Length && i < correction.LineFactors.Length; i++)
                    {
                        arm.Factors[i] = correction.LineFactors[i];
                    }
                }

                if (correction != null && correction.SumPeaks != null)
                {
                    foreach (FsaCascadeSummer.SumPeak peak in correction.SumPeaks)
                    {
                        arm.SumArea += peak.Area;
                    }
                }

                for (int i = 0; i < component.Lines.Count; i++)
                {
                    FsaLine line = component.Lines[i];
                    arm.PeakArea += line.Intensity / 100.0
                                    * summer.PeakEfficiency(line.Energy) * arm.Factors[i];
                }
            }

            arm.Shares = FsaCascadeSummer.SuperUnitShares;
            arm.SharesSupply = FsaCascadeSummer.SuperUnitSharesSupply;
            arm.SharesDerived = FsaCascadeSummer.SuperUnitSharesDerived;
            arm.Pairs = FsaCascadeSummer.SuperUnitPairs;
            arm.PairsSupply = FsaCascadeSummer.SuperUnitPairsSupply;
            arm.PairsDerived = FsaCascadeSummer.SuperUnitPairsDerived;
            arm.WorstShare = FsaCascadeSummer.WorstSuperUnitShare;
            arm.WorstPair = FsaCascadeSummer.WorstSuperUnitPair;

            // ⛔ Возвращается УМОЛЧАНИЕ ПРИЛОЖЕНИЯ, снятое до первого плеча, а
            // не жёстко вписанное `AsIs`: 10.09.2026 умолчание стало `Clamp`
            // (решение Amber по `D49`), и зашитая константа тихо подменяла бы
            // правило приложения на снятое.
            FsaCascadeSummer.SuperUnitPolicy = Default;
            return arm;
        }

        static double Delta(double value, double reference)
        {
            if (!(Math.Abs(reference) > 0.0))
            {
                return Math.Abs(value) > 0.0 ? 100.0 : 0.0;
            }

            return 100.0 * (value - reference) / reference;
        }

        static FsaComponent Clone(FsaComponent proto)
        {
            FsaComponent copy = new FsaComponent(proto.Name, proto.Kind);
            foreach (FsaLine line in proto.Lines)
            {
                copy.Lines.Add(new FsaLine(line.Nuclide, line.Energy, line.Intensity));
            }

            return copy;
        }

        /// <summary>
        /// Образ нуклида по ТЕМ ЖЕ линиям, что видит суммирователь
        /// (`v_gamma_coincidence_line`): иначе часть линий не сошлась бы с
        /// базой совпадений и цена вышла бы заниженной по построению.
        /// </summary>
        static FsaComponent Build(string db, string name)
        {
            string key = FsaCascadeSummer.ParentKey(name);
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            bool isomer = key.StartsWith("sandia:", StringComparison.Ordinal);
            string parameter = isomer ? key.Substring(7) : key;
            FsaComponent component = new FsaComponent(name, FsaComponentKind.Single);

            using (SqliteConnection connection = new SqliteConnection(
                "Data Source=" + db + ";Mode=ReadOnly;Cache=Shared;"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select energy_kev, intensity_pct from v_gamma_coincidence_line"
                        + (isomer ? " where sandia_symbol = $n" : " where nucid = $n and isomer = 0")
                        + " order by energy_kev";
                    command.Parameters.AddWithValue("$n", parameter);
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            component.Lines.Add(new FsaLine(name, reader.GetDouble(0),
                                                            reader.GetDouble(1)));
                        }
                    }
                }
            }

            return component;
        }
    }
}
