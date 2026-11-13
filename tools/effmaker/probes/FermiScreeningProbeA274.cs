using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FermiScreeningProbeA274
{
    /// <summary>
    /// (`AMBER274`, полоса `a274s` 13.11.2026) ПРИЁМКА КЛЮЧА `FsaBetaLibrary.FermiScreening` —
    /// экранирование ядра атомными электронами в функции Ферми (Бюринг, потенциал Салвата, X = 1;
    /// Hayen 2018, ур. 144). Решение Amber 13.11.2026 вопросником, дословно: «Принять, экран
    /// полосой (Рекомендую)».
    ///
    ///   `--mode=control` — положительный контроль: умолчание ВЫКЛ; Z = 0 — ровно 1, Z → 0 (дробный
    ///       заряд) — 1, |S − 1| растёт с Z; сверка с независимым портом BSG критика `crit274L`
    ///       (scipy, `D:\BqMoni_Claude\crit274L\screening.py`) на 36 точках; сверка с рис. 5 Hayen 2018
    ///       (⁴⁵Ca, кривые «This work» и «Numerical», оцифровка векторов PDF) при W ≥ 1.25; предел
    ///       больших W — первый порядок Роуза (ур. 141); мутанты «знак β» и «потенциал дочери»
    ///       той же проверкой пойманы; плотность ВКЛ = ВЫКЛ × S поточечно, `beta_shapes` не сдвигаются.
    ///   `--mode=means [--min-intensity=0.5] [--csv=файл]` — средние ВСЕХ ветвей β⁻/β⁺ `nucdb` к
    ///       средним базы при λ ВКЛ и четырёх сочетаниях L₀ × экранирование.
    ///   `--mode=store --store=каталог [--scenes=a.rmx,b.rmx]` — сцены копии склада: раздел без
    ///       `scr=` при ВКЛ не берётся, с `scr=1` — берётся (при ВЫКЛ — нет); сколько строк ветвей
    ///       меняет ключ; образ беты весами узлов ВЫКЛ/ВКЛ (без шума Монте-Карло).
    /// Ключи λ и L₀ — умолчания rev55 (ВКЛ), если не заданы `--clambda=` / `--l0=`.
    /// </summary>
    static class Program
    {
        static int bad;
        const double Me = 510.99895;
        const double Alpha = 1.0 / 137.035999084;

        static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.UTF8;
            string mode = "control", store = null, csv = null;
            var scenes = new List<string>();
            double minIntensity = 0.5;
            Check("умолчание ключа экранирования (FermiScreening) — ВЫКЛ", !FsaBetaLibrary.FermiScreening);
            Check("умолчания λ и L₀ — ВКЛ (rev55)", FsaBetaLibrary.CoulombLambda && FsaBetaLibrary.FermiFiniteSize);
            foreach (string a in args)
            {
                if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
                else if (a.StartsWith("--scenes=", StringComparison.Ordinal)) scenes.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--min-intensity=", StringComparison.Ordinal)) minIntensity = double.Parse(a.Substring(16), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--clambda=", StringComparison.Ordinal)) FsaBetaLibrary.CoulombLambda = a.Substring(10) == "1";
                else if (a.StartsWith("--l0=", StringComparison.Ordinal)) FsaBetaLibrary.FermiFiniteSize = a.Substring(5) == "1";
                else
                {
                    Console.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            var started = DateTime.UtcNow;
            if (mode == "control" || mode == "all")
            {
                Control();
            }

            if (mode == "means" || mode == "all")
            {
                Means(minIntensity, csv);
            }

            if (mode == "store" || mode == "all")
            {
                if (scenes.Count == 0 && store != null)
                {
                    scenes.AddRange(Directory.GetFiles(store, "*.rmx").OrderBy(s => s, StringComparer.Ordinal));
                }

                Store(scenes);
            }

            FsaBetaLibrary.FermiScreening = false;
            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "время {0:F1} с; ПРОВАЛОВ: {1}", (DateTime.UtcNow - started).TotalSeconds, bad));
            return bad == 0 ? 0 : 1;
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine((ok ? "  ✓ " : "  ✗ ") + what);
            if (!ok)
            {
                bad++;
            }
        }

        static double W(double tKev)
        {
            return 1.0 + tKev / Me;
        }

        // ------------------------------------------------------------------ control

        /// <summary>
        /// Опорные S(Z, W) — порт BSG критика `crit274L` (`screening.py`, функция `s_salvat`, scipy
        /// `loggamma`), посчитан 13.11.2026 командой из журнала `a274s` §3: Z дочери, знак, S при
        /// T = 20 / 100 / 500 / 1500 кэВ.
        /// </summary>
        static readonly object[][] BsgPort =
        {
            new object[] { 82, false, new[] { 0.9511068702, 0.9572219829, 0.9730681764, 0.9862839607 } },
            new object[] { 84, false, new[] { 0.9496898742, 0.9560297017, 0.9723651121, 0.9859178856 } },
            new object[] { 90, false, new[] { 0.9456058463, 0.9525580340, 0.9703605796, 0.9848816158 } },
            new object[] { 18, true, new[] { 1.1527501733, 1.0207398578, 1.0049441193, 1.0020711981 } },
            new object[] { 10, true, new[] { 1.0463054443, 1.0080020965, 1.0022202311, 1.0009597985 } },
            new object[] { 56, true, new[] { 4.5762901782, 1.2037737385, 1.0274974779, 1.0097133017 } },
            new object[] { 56, false, new[] { 0.9702081637, 0.9724127935, 0.9828212807, 0.9914138600 } },
            new object[] { 21, false, new[] { 0.9865230860, 0.9896336086, 0.9951802701, 0.9977370715 } },
        };

        static readonly double[] PortT = { 20.0, 100.0, 500.0, 1500.0 };

        /// <summary>Тот же порт, числа из `screening.txt` критика: Z = 56 при T = 10 / 50 / 200 / 1000 кэВ.</summary>
        static readonly double[] CritT = { 10.0, 50.0, 200.0, 1000.0 };
        static readonly double[] CritMinus = { 0.96959943, 0.97112645, 0.97567349, 0.98855228 };
        static readonly double[] CritPlus = { 29.52217437, 1.59908695, 1.07888485, 1.01404517 };

        /// <summary>Рис. 5 Hayen 2018, ⁴⁵Ca (β⁻, дочь Sc, Z = 21): W, «This work», «Numerical» — оцифровка векторов PDF (журнал §3).</summary>
        static readonly double[][] Figure5 =
        {
            new[] { 1.25, 0.99050, 0.99061 },
            new[] { 1.50, 0.99304, 0.99311 },
            new[] { 2.00, 0.99522, 0.99527 },
            new[] { 2.50, 0.99629, 0.99632 },
        };

        /// <summary>
        /// Проверяемая функция: «как в коде» или мутант. 0 — <see cref="AtomicScreening.Correction"/>;
        /// 1 — мутант знака (формула позитрона для электрона и обратно); 2 — мутант потенциала
        /// (λ и V₀ дочернего атома вместо родительского).
        /// </summary>
        static double Screen(int variant, int zDaughter, double w, bool positron)
        {
            int bt = positron ? -1 : 1;
            switch (variant)
            {
                case 1:
                    return AtomicScreening.CorrectionWith(zDaughter, zDaughter + bt, AtomicScreening.Lambda(zDaughter + bt), w, -bt);
                case 2:
                    return AtomicScreening.CorrectionWith(zDaughter, zDaughter, AtomicScreening.Lambda(zDaughter), w, bt);
                default:
                    return AtomicScreening.Correction(zDaughter, w, positron);
            }
        }

        static void Control()
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. Z → 0: S → 1 ===");
            Check("Z = 0: S ровно 1 (β⁻ и β⁺)", AtomicScreening.Correction(0, 1.5, false) == 1.0 && AtomicScreening.Correction(0, 1.5, true) == 1.0);
            Check("β⁻ с дочерью Z = 1 (родитель без электронов): S ровно 1", AtomicScreening.Correction(1, 1.5, false) == 1.0);
            double worstZero = 0.0;
            foreach (double t in new[] { 1.0, 10.0, 100.0, 1000.0, 3000.0 })
            {
                foreach (int bt in new[] { 1, -1 })
                {
                    double s = AtomicScreening.CorrectionWith(1e-6, 1e-6, AtomicScreening.Lambda(1), W(t), bt);
                    worstZero = Math.Max(worstZero, Math.Abs(s - 1.0));
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "заряд 10⁻⁶ (λ водорода), T = 1…3000 кэВ: наибольшее |S − 1| = {0:E2}", worstZero));
            Check("Z → 0: |S − 1| ≤ 1·10⁻⁶", worstZero <= 1e-6);
            Console.WriteLine("{0,4} {1}", "Z", "|S − 1| β⁻ при T = 100 кэВ, β⁺ при T = 100 кэВ");
            double prevM = 0.0, prevP = 0.0;
            bool monotone = true;
            foreach (int z in new[] { 2, 5, 10, 20, 40, 60, 82 })
            {
                double m = Math.Abs(AtomicScreening.Correction(z, W(100.0), false) - 1.0);
                double p = Math.Abs(AtomicScreening.Correction(z, W(100.0), true) - 1.0);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,4} {1:E3} {2:E3}", z, m, p));
                monotone &= m > prevM && p > prevP;
                prevM = m;
                prevP = p;
            }

            Check("|S − 1| растёт с Z (β⁻ и β⁺)", monotone);
            Check("знак: β⁻ S < 1, β⁺ S > 1 (Z = 20, 56, 82; 10…2000 кэВ)", new[] { 20, 56, 82 }.All(z => new[] { 10.0, 100.0, 2000.0 }.All(t =>
                AtomicScreening.Correction(z, W(t), false) < 1.0 && AtomicScreening.Correction(z, W(t), true) > 1.0)));

            Console.WriteLine();
            Console.WriteLine("=== 2. Сверка с портом BSG (критик `crit274L`, scipy) ===");
            var worst = new double[3];
            for (int variant = 0; variant < 3; variant++)
            {
                foreach (object[] row in BsgPort)
                {
                    int z = (int)row[0];
                    bool pos = (bool)row[1];
                    double[] reference = (double[])row[2];
                    for (int i = 0; i < PortT.Length; i++)
                    {
                        double s = Screen(variant, z, W(PortT[i]), pos);
                        worst[variant] = Math.Max(worst[variant], Math.Abs(s / reference[i] - 1.0));
                        if (variant == 0 && (i == 0 || i == 3))
                        {
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Z {0,3} {1} T {2,6:F0}: S {3:F10}, порт BSG {4:F10}", z, pos ? "β⁺" : "β⁻", PortT[i], s, reference[i]));
                        }
                    }
                }

                for (int i = 0; i < CritT.Length; i++)
                {
                    worst[variant] = Math.Max(worst[variant], Math.Abs(Screen(variant, 56, W(CritT[i]), false) / CritMinus[i] - 1.0));
                    worst[variant] = Math.Max(worst[variant], Math.Abs(Screen(variant, 56, W(CritT[i]), true) / CritPlus[i] - 1.0));
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "наибольшее |S/порт − 1| на 40 точках: код {0:E2}; мутант знака {1:E2}; мутант потенциала дочери {2:E2}", worst[0], worst[1], worst[2]));
            Check("код = порт BSG до 1·10⁻⁶ (40 точек, Z 10…90, β⁻ и β⁺, 10…1500 кэВ)", worst[0] <= 1e-6);
            Check("мутант знака той же проверкой пойман", worst[1] > 1e-6);
            Check("мутант «потенциал дочери» той же проверкой пойман", worst[2] > 1e-6);

            Console.WriteLine();
            Console.WriteLine("=== 3. Рис. 5 Hayen 2018 (⁴⁵Ca → Sc, β⁻), W ≥ 1.25 ===");
            var fig = new double[3];
            var num = new double[3];
            foreach (double[] row in Figure5)
            {
                for (int variant = 0; variant < 3; variant++)
                {
                    double s = Screen(variant, 21, row[0], false);
                    fig[variant] = Math.Max(fig[variant], Math.Abs(s - row[1]));
                    num[variant] = Math.Max(num[variant], Math.Abs(s - row[2]));
                    if (variant == 0)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  W {0:F2}: S {1:F5}; «This work» {2:F5}; «Numerical» {3:F5}", row[0], s, row[1], row[2]));
                    }
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "наибольшее |S − рис.|: «This work» код {0:E2} / знак {1:E2} / дочь {2:E2}; «Numerical» код {3:E2} / знак {4:E2} / дочь {5:E2}",
                                            fig[0], fig[1], fig[2], num[0], num[1], num[2]));
            Check("код = «This work» рис. 5 до 1·10⁻⁴ (оцифровка ±3·10⁻⁵)", fig[0] <= 1e-4);
            Check("код = «Numerical» рис. 5 (точный счёт с потенциалом Салвата) до 1.5·10⁻⁴", num[0] <= 1.5e-4);
            Check("мутант знака рисунком пойман (> 1·10⁻⁴)", fig[1] > 1e-4);
            Check("мутант «потенциал дочери» рисунком пойман (> 1·10⁻⁴)", fig[2] > 1e-4);
            Console.WriteLine("  ниже W = 1.1 (журнал): рис. «This work» ниже кода BSG на 2.4·10⁻⁴ (W 1.10) … 7.5·10⁻⁴ (W 1.02) — не проверка, факт:");
            foreach (double w in new[] { 1.02, 1.05, 1.10 })
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "    W {0:F2}: S {1:F5}", w, AtomicScreening.Correction(21, w, false)));
            }

            Console.WriteLine();
            Console.WriteLine("=== 4. Предел больших W: первый порядок Роуза (ур. 141), S − 1 ≈ ∓V₀(W/p² + 1/W) ===");
            double worstRose = 0.0, worstRoseSign = 0.0;
            foreach (int z in new[] { 6, 10, 20 })
            {
                foreach (bool pos in new[] { false, true })
                {
                    int bt = pos ? -1 : 1;
                    int zp = z - bt;
                    double v0 = 0.5 * Alpha * zp * AtomicScreening.Lambda(zp);
                    foreach (double w in new[] { 4.0, 8.0 })
                    {
                        double p2 = w * w - 1.0;
                        double first = -bt * v0 * (w / p2 + 1.0 / w);
                        double ratio = (AtomicScreening.Correction(z, w, pos) - 1.0) / first;
                        double ratioSign = (Screen(1, z, w, pos) - 1.0) / first;
                        worstRose = Math.Max(worstRose, Math.Abs(ratio - 1.0));
                        worstRoseSign = Math.Max(worstRoseSign, Math.Abs(ratioSign - 1.0));
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Z {0,2} {1} W {2}: (S − 1)/первый порядок = {3:F4}", z, pos ? "β⁺" : "β⁻", w, ratio));
                    }
                }
            }

            Check("(S − 1)/первый порядок Роуза = 1 ± 0.2 при Z ≤ 20, W = 4…8 (поправки O(αZ))", worstRose <= 0.2);
            Check("мутант знака этой проверкой пойман", worstRoseSign > 0.2);

            Console.WriteLine();
            Console.WriteLine("=== 5. Плотность: ВКЛ = ВЫКЛ × S поточечно; таблица beta_shapes ключом не сдвигается ===");
            int branches = 0, exact = 0, tables = 0, tablesSame = 0;
            double worstPoint = 0.0;
            foreach (string nucid in new[] { "228AC", "214BI", "208TL", "212PB", "212BI", "40K", "138LA", "204TL", "210TL", "234PA", "22NA", "60CO", "137CS", "90SR", "90Y", "99TC", "14C", "3H", "176LU", "18F" })
            {
                foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
                {
                    if (b.IsLine || !(b.EndpointKev > 0.0))
                    {
                        continue;
                    }

                    const int n = 257;
                    bool table = b.ShapeT != null && b.ShapeDensity != null && b.ShapeEndpointKev > 0.0;
                    bool same = true;
                    double wst = 0.0;
                    for (int i = 0; i < n; i++)
                    {
                        double t = b.EndpointKev * (i + 0.5) / n;
                        FsaBetaLibrary.FermiScreening = false;
                        double off = FsaBetaLibrary.Density(b, t);
                        FsaBetaLibrary.FermiScreening = true;
                        double on = FsaBetaLibrary.Density(b, t);
                        FsaBetaLibrary.FermiScreening = false;
                        if (table)
                        {
                            same &= on == off;
                            continue;
                        }

                        double expected = off * AtomicScreening.Correction(b.DaughterZ, W(t), b.Positron);
                        if (expected > 0.0)
                        {
                            wst = Math.Max(wst, Math.Abs(on / expected - 1.0));
                        }
                    }

                    if (table)
                    {
                        tables++;
                        if (same) tablesSame++;
                    }
                    else
                    {
                        branches++;
                        if (wst <= 1e-12) exact++;
                        worstPoint = Math.Max(worstPoint, wst);
                    }
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "ветвей кодом {0}: ВКЛ = ВЫКЛ × S у {1}, наибольшее {2:E2}; таблиц {3}, побитно у {4}", branches, exact, worstPoint, tables, tablesSame));
            Check("плотность ВКЛ = ВЫКЛ × S (допуск 1e-12)", branches > 0 && exact == branches);
            Check("таблица beta_shapes ключом не сдвигается", tablesSame == tables);

            Console.WriteLine();
            Console.WriteLine("=== 6. S по энергии — для журнала (T = 10 / 50 / 100 / 300 / 1000 / 2000 кэВ) ===");
            foreach (int z in new[] { 10, 18, 21, 40, 56, 72, 82, 84, 90 })
            {
                foreach (bool pos in new[] { false, true })
                {
                    var sb = new StringBuilder();
                    foreach (double t in new[] { 10.0, 50.0, 100.0, 300.0, 1000.0, 2000.0 })
                    {
                        sb.Append(AtomicScreening.Correction(z, W(t), pos).ToString("F5", CultureInfo.InvariantCulture)).Append(' ');
                    }

                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Z {0,3} {1}: {2}", z, pos ? "β⁺" : "β⁻", sb));
                }
            }

            // Цена: одна точка S.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            double sink = 0.0;
            for (int i = 0; i < 200000; i++)
            {
                sink += AtomicScreening.Correction(56 + (i % 30), 1.0 + (i % 997 + 1) * 0.004, (i & 1) == 0);
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "цена: {0:F3} мкс на точку S (200 000 точек, контроль суммы {1:F3})", watch.Elapsed.TotalMilliseconds * 1000.0 / 200000.0, sink));
        }

        // ------------------------------------------------------------------ means

        sealed class Row
        {
            public string Nuclide;
            public FsaBeta Beta;
            public string Kind;
            public double[] Mean = new double[4];   // L0S0, L0S1, L1S0, L1S1 (λ — как задано, умолчание ВКЛ)
        }

        static readonly string[] ArmNames = { "L₀0·S0", "L₀0·S1", "L₀1·S0", "L₀1·S1" };

        static string KindOf(FsaBeta b)
        {
            if (b.ShapeT != null) return "табл";
            if (b.ShapeFactor != null) return "фактор";
            if (b.UniqueOrder >= 1) return "уник";
            string t = (b.Transition ?? "").ToLowerInvariant();
            if (t.StartsWith("allowed", StringComparison.Ordinal)) return "разр";
            return "неуник";
        }

        static List<string> AllNuclides()
        {
            var list = new List<string>();
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            using (var c = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly;"))
            {
                c.Open();
                using (SqliteCommand cmd = c.CreateCommand())
                {
                    cmd.CommandText = "select distinct parent_nucid from decay_radiations where type_a in ('B-', 'B+') and intensity_num > 0 order by parent_nucid";
                    using (SqliteDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            if (!r.IsDBNull(0)) list.Add(r.GetString(0));
                        }
                    }
                }
            }

            return list;
        }

        static void Means(double minIntensity, string csv)
        {
            bool l0Saved = FsaBetaLibrary.FermiFiniteSize;
            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "=== Средние ветвей к средним базы (`nucdb`): λ {0}; L₀ ВЫКЛ/ВКЛ × экранирование ВЫКЛ/ВКЛ ===", FsaBetaLibrary.CoulombLambda ? "ВКЛ" : "ВЫКЛ"));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<string> nucids = AllNuclides();
            var rows = new List<Row>();
            int skipped = 0;
            foreach (string nucid in nucids)
            {
                foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
                {
                    if (b.IsLine || !(b.EndpointKev > 0.0))
                    {
                        continue;
                    }

                    if (!(b.MeanKev > 0.0) || b.DaughterZ <= 0)
                    {
                        skipped++;
                        continue;
                    }

                    rows.Add(new Row { Nuclide = nucid, Beta = b, Kind = KindOf(b) });
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "нуклидов с ветвями в базе {0}; ветвей с границей и средней {1} (без средней или Z — {2}); чтение {3:F1} с",
                                            nucids.Count, rows.Count, skipped, watch.Elapsed.TotalSeconds));
            for (int arm = 0; arm < 4; arm++)
            {
                FsaBetaLibrary.FermiFiniteSize = arm >= 2;
                FsaBetaLibrary.FermiScreening = arm % 2 == 1;
                var t0 = watch.Elapsed.TotalSeconds;
                Parallel.ForEach(rows, r =>
                {
                    double mean;
                    FsaBetaLibrary.Integral(r.Beta, out mean);
                    r.Mean[arm] = mean;
                });
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "плечо {0}: {1:F1} с", ArmNames[arm], watch.Elapsed.TotalSeconds - t0));
            }

            FsaBetaLibrary.FermiFiniteSize = l0Saved;
            FsaBetaLibrary.FermiScreening = false;

            if (csv != null)
            {
                using (var wr = new StreamWriter(csv, false, new UTF8Encoding(false)))
                {
                    wr.WriteLine("nucid,sign,Z,A,E0_keV,I_pct,kind,transition,mean_base,mean_L0S0,mean_L0S1,mean_L1S0,mean_L1S1");
                    foreach (Row r in rows)
                    {
                        FsaBeta b = r.Beta;
                        wr.WriteLine(string.Join(",", new[]
                        {
                            r.Nuclide, b.Positron ? "+" : "-", b.DaughterZ.ToString(CultureInfo.InvariantCulture), b.MassNumber.ToString(CultureInfo.InvariantCulture),
                            b.EndpointKev.ToString("R", CultureInfo.InvariantCulture), b.Intensity.ToString("R", CultureInfo.InvariantCulture), r.Kind,
                            "\"" + (b.Transition ?? "").Replace("\"", "'") + "\"", b.MeanKev.ToString("R", CultureInfo.InvariantCulture),
                            r.Mean[0].ToString("R", CultureInfo.InvariantCulture), r.Mean[1].ToString("R", CultureInfo.InvariantCulture),
                            r.Mean[2].ToString("R", CultureInfo.InvariantCulture), r.Mean[3].ToString("R", CultureInfo.InvariantCulture),
                        }));
                    }
                }

                Console.WriteLine("CSV: " + csv);
            }

            int tableRows = 0, tableSame = 0, nan = 0;
            foreach (Row r in rows)
            {
                if (r.Mean.Any(m => double.IsNaN(m) || double.IsInfinity(m))) nan++;
                if (r.Kind != "табл") continue;
                tableRows++;
                if (r.Mean[0] == r.Mean[1] && r.Mean[2] == r.Mean[3]) tableSame++;
            }

            Check("средние табличных форм экранированием не сдвигаются", tableSame == tableRows);
            Check("ни одной средней NaN/∞ ни в одном плече", nan == 0);

            int[][] bands = { new[] { 1, 20 }, new[] { 21, 40 }, new[] { 41, 58 }, new[] { 59, 80 }, new[] { 81, 88 }, new[] { 89, 100 } };
            string[] kinds = { "разр", "неуник", "уник", "фактор" };
            foreach (bool positron in new[] { false, true })
            {
                Console.WriteLine();
                Console.WriteLine((positron ? "β⁺" : "β⁻") + string.Format(CultureInfo.InvariantCulture, ", выход ≥ {0} %: среднее (ср/база − 1), % — и среднее |…|, % — плечи {1}", minIntensity, string.Join(" / ", ArmNames)));
                Console.WriteLine("{0,-7} {1,-7} {2,5}   {3,-31} {4}", "Z", "вид", "n", "среднее откл., %", "среднее |откл.|, %");
                foreach (int[] band in bands)
                {
                    foreach (string kind in kinds)
                    {
                        var sel = rows.Where(r => r.Kind == kind && r.Beta.Positron == positron && r.Beta.Intensity >= minIntensity
                                                  && r.Beta.DaughterZ >= band[0] && r.Beta.DaughterZ <= band[1]).ToList();
                        if (sel.Count == 0)
                        {
                            continue;
                        }

                        var sbm = new StringBuilder();
                        var sba = new StringBuilder();
                        for (int arm = 0; arm < 4; arm++)
                        {
                            sbm.Append((100.0 * sel.Average(r => r.Mean[arm] / r.Beta.MeanKev - 1.0)).ToString("+0.000;-0.000", CultureInfo.InvariantCulture)).Append(arm < 3 ? " " : "");
                            sba.Append((100.0 * sel.Average(r => Math.Abs(r.Mean[arm] / r.Beta.MeanKev - 1.0))).ToString("0.000", CultureInfo.InvariantCulture)).Append(arm < 3 ? " " : "");
                        }

                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-7} {1,-7} {2,5}   {3,-31} {4}", band[0] + "–" + band[1], kind, sel.Count, sbm, sba));
                    }
                }
            }

            // Итог по границе Z: |откл.| без и с экранированием при L₀ ВЫКЛ и ВКЛ; с весом выхода.
            Console.WriteLine();
            foreach (bool positron in new[] { false, true })
            {
                foreach (bool l0 in new[] { true, false })
                {
                    int a0 = l0 ? 2 : 0, a1 = a0 + 1;
                    foreach (int[] band in new[] { new[] { 1, 58 }, new[] { 59, 80 }, new[] { 81, 100 } })
                    {
                        var sel = rows.Where(r => r.Kind != "табл" && r.Beta.Positron == positron && r.Beta.DaughterZ >= band[0] && r.Beta.DaughterZ <= band[1] && r.Beta.Intensity >= minIntensity).ToList();
                        if (sel.Count == 0) continue;
                        Func<int, double> mabs = arm => 100.0 * sel.Average(r => Math.Abs(r.Mean[arm] / r.Beta.MeanKev - 1.0));
                        Func<int, double> wabs = arm => 100.0 * sel.Sum(r => r.Beta.Intensity * Math.Abs(r.Mean[arm] / r.Beta.MeanKev - 1.0)) / sel.Sum(r => r.Beta.Intensity);
                        int worse = sel.Count(r => Math.Abs(r.Mean[a1] / r.Beta.MeanKev - 1.0) > Math.Abs(r.Mean[a0] / r.Beta.MeanKev - 1.0) + 5e-4);
                        int better = sel.Count(r => Math.Abs(r.Mean[a1] / r.Beta.MeanKev - 1.0) + 5e-4 < Math.Abs(r.Mean[a0] / r.Beta.MeanKev - 1.0));
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "{0} L₀ {1}, Z {2}–{3}, ветвей {4}: среднее |откл.| {5:F3} → {6:F3} % (с весом выхода {7:F3} → {8:F3} %); хуже на ≥ 0.05 % у {9}, лучше у {10}; средний сдвиг средней экранированием {11:+0.000;-0.000} %",
                            positron ? "β⁺" : "β⁻", l0 ? "ВКЛ" : "ВЫКЛ", band[0], band[1], sel.Count, mabs(a0), mabs(a1), wabs(a0), wabs(a1), worse, better,
                            100.0 * sel.Average(r => r.Mean[a1] / r.Mean[a0] - 1.0)));
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("ветви корпуса и находки критика (ср/база: " + string.Join(" ", ArmNames) + "; сдвиг экранированием при L₀ ВКЛ):");
            foreach (string[] probe in new[]
            {
                new[] { "228AC", "2069" }, new[] { "228AC", "1731" }, new[] { "228AC", "1158" }, new[] { "212BI", "2252" }, new[] { "208TL", "1801" },
                new[] { "214BI", "3270" }, new[] { "214BI", "1540" }, new[] { "214PB", "723" }, new[] { "234PA", "642" }, new[] { "40K", "1311" },
                new[] { "40K", "1505" }, new[] { "138LA", "255" }, new[] { "60CO", "318" }, new[] { "176LU", "593" }, new[] { "22NA", "546" }, new[] { "204TL", "763" }, new[] { "210TL", "2413" },
            })
            {
                double e = double.Parse(probe[1], CultureInfo.InvariantCulture);
                foreach (Row r in rows.Where(r => string.Equals(r.Nuclide, probe[0], StringComparison.OrdinalIgnoreCase) && Math.Abs(r.Beta.EndpointKev - e) < 10.0))
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-6} {1} {2,7:F1} кэВ {3,7:F3} % {4,-22} база {5,8:F2}: {6:F4} {7:F4} {8:F4} {9:F4}; экран {10:+0.000;-0.000} %",
                        r.Nuclide, r.Beta.Positron ? "β⁺" : "β⁻", r.Beta.EndpointKev, r.Beta.Intensity, r.Beta.Transition, r.Beta.MeanKev,
                        r.Mean[0] / r.Beta.MeanKev, r.Mean[1] / r.Beta.MeanKev, r.Mean[2] / r.Beta.MeanKev, r.Mean[3] / r.Beta.MeanKev, 100.0 * (r.Mean[3] / r.Mean[2] - 1.0)));
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "время средних {0:F1} с", watch.Elapsed.TotalSeconds));
        }

        // ------------------------------------------------------------------ store

        static readonly double[] Bands = { 0.0, 50.0, 100.0, 150.0, 300.0, 600.0, 1000.0, 4000.0 };

        static void Store(List<string> scenes)
        {
            bool first = true;
            int scenesWithSection = 0, scenesChanged = 0;
            var changedTotal = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (string rmx in scenes)
            {
                Console.WriteLine();
                Console.WriteLine("=== Сцена склада: " + Path.GetFileName(rmx) + " ===");
                MatrixRefusal refusal;
                int format;
                ResponseMatrix matrix = ResponseMatrix.Load(rmx, out refusal, out format);
                string inPath = Path.ChangeExtension(rmx, ".in");
                if (matrix == null || !File.Exists(inPath))
                {
                    Check("сцена прочитана (" + refusal + ")", false);
                    continue;
                }

                GeometryModel geometry = GeometryModel.Load(inPath);
                ElectronSectionRefusal why;
                FsaBetaLibrary.FermiScreening = false;
                ResponseElectronSection section = matrix.ElectronSectionFor(geometry, out why);
                if (section == null)
                {
                    Console.WriteLine("раздела электрона нет или не годен: " + why);
                    continue;
                }

                scenesWithSection++;
                bool withField = section.StampField("scr") != null;
                if (!withField)
                {
                    FsaBetaLibrary.FermiScreening = true;
                    ResponseElectronSection on = matrix.ElectronSectionFor(geometry, out why);
                    Check("ВКЛ: раздел без `scr=` разбор не берёт (OtherPhysics)", on == null && why == ElectronSectionRefusal.OtherPhysics);
                    FsaBetaLibrary.FermiScreening = false;
                }
                else
                {
                    // склад, посчитанный с ключом: при ВЫКЛ раздел не годен, при ВКЛ — годен
                    Check("раздел с `scr=1`: при ВЫКЛ не годен", false);
                }

                if (first && !withField)
                {
                    string saved = section.Stamp;
                    int capAt = saved.IndexOf(";cap=", StringComparison.Ordinal);
                    Func<string, string> inject = f => capAt >= 0 ? saved.Substring(0, capAt) + f + saved.Substring(capAt) : saved + f;
                    section.Stamp = inject(";scr=1");
                    FsaBetaLibrary.FermiScreening = true;
                    bool takenOn = matrix.ElectronSectionFor(geometry, out why) != null;
                    FsaBetaLibrary.FermiScreening = false;
                    ElectronSectionRefusal whyOff;
                    bool takenOff = matrix.ElectronSectionFor(geometry, out whyOff) != null;
                    Check("раздел с `scr=1` при ВКЛ берётся", takenOn);
                    Check("раздел с `scr=1` при ВЫКЛ не берётся (OtherPhysics)", !takenOff && whyOff == ElectronSectionRefusal.OtherPhysics);
                    section.Stamp = inject(";scr=2");
                    FsaBetaLibrary.FermiScreening = true;
                    Check("раздел с другой версией `scr=2` при ВКЛ не берётся", matrix.ElectronSectionFor(geometry, out why) == null && why == ElectronSectionRefusal.OtherPhysics);
                    bool l0 = FsaBetaLibrary.FermiFiniteSize;
                    FsaBetaLibrary.FermiFiniteSize = !l0;
                    section.Stamp = inject(";scr=1");
                    Check("раздел с `scr=1`, но другим L₀ — не берётся", matrix.ElectronSectionFor(geometry, out why) == null);
                    FsaBetaLibrary.FermiFiniteSize = l0;
                    FsaBetaLibrary.FermiScreening = false;
                    section.Stamp = saved;
                    Check("клеймо возвращено, раздел годен", matrix.ElectronSectionFor(geometry, out why) != null);
                    Console.WriteLine("клеймо раздела: " + section.Stamp);
                }

                int rows = section.BranchNuclides != null ? section.BranchNuclides.Length : 0;
                int found = 0, sameOff = 0, changedRows = 0;
                var changed = new List<KeyValuePair<FsaBeta, int>>();
                var sbSame = new StringBuilder();
                for (int j = 0; j < rows; j++)
                {
                    FsaBeta beta = null;
                    foreach (FsaBeta b in FsaBetaLibrary.Branches(section.BranchNuclides[j]))
                    {
                        if (!b.IsLine && !b.Positron && Math.Abs(b.EndpointKev - section.BranchEndpoints[j]) <= 0.01)
                        {
                            beta = b;
                            break;
                        }
                    }

                    if (beta == null)
                    {
                        continue;
                    }

                    found++;
                    string hOff = Hash(beta, false), hOn = Hash(beta, true);
                    if (hOff != null && section.BranchHashes != null && hOff == section.BranchHashes[j]) sameOff++;
                    if (hOff != hOn)
                    {
                        changedRows++;
                        changed.Add(new KeyValuePair<FsaBeta, int>(beta, j));
                        string key = section.BranchNuclides[j] + " " + beta.EndpointKev.ToString("F1", CultureInfo.InvariantCulture);
                        changedTotal[key] = (changedTotal.ContainsKey(key) ? changedTotal[key] : 0) + 1;
                    }
                    else
                    {
                        sbSame.Append(section.BranchNuclides[j]).Append(' ').Append(beta.EndpointKev.ToString("F1", CultureInfo.InvariantCulture)).Append(" (").Append(beta.Transition).Append("); ");
                    }
                }

                if (changedRows > 0) scenesChanged++;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "строк ветвей {0}, найдено в библиотеке {1}; ВЫКЛ: хеш = записанному у {2}; меняет экранирование {3}",
                                                rows, found, sameOff, changedRows));
                if (sbSame.Length > 0)
                {
                    Console.WriteLine("  экранирование НЕ меняет: " + sbSame);
                }

                Check("ВЫКЛ: строки ветвей склада побитно по входу (хеш формы и каскада)", rows > 0 && found == rows && sameOff == rows);

                if (!first)
                {
                    continue;
                }

                first = false;
                int length = (int)(4000.0 / section.BinKev);
                Console.WriteLine("образ беты нуклида разбором (Σ ветвей I·Σwₖ·Rₖ; у ветви со строкой — её строка склада), полосы "
                                  + string.Join("/", Array.ConvertAll(Bands, b => b.ToString("F0", CultureInfo.InvariantCulture))) + " кэВ:");
                foreach (string nucid in new[] { "40K", "138LA", "90SR", "90Y", "137CS", "228AC", "212PB", "212BI", "208TL", "214BI", "214PB", "210BI", "234PA", "22NA" })
                {
                    var image = new double[3][];
                    var area = new double[3];
                    for (int arm = 0; arm < 3; arm++)
                    {
                        FsaBetaLibrary.FermiScreening = arm == 1;
                        image[arm] = new double[length];
                        foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
                        {
                            if (b.IsLine || !(b.Intensity > 0.0))
                            {
                                continue;
                            }

                            double[] w = FsaBetaLibrary.Weights(section, b);
                            if (w == null)
                            {
                                continue;
                            }

                            var scaled = new double[w.Length];
                            for (int k = 0; k < w.Length; k++)
                            {
                                scaled[k] = w[k] * b.Intensity / 100.0;
                            }

                            area[arm] += section.Accumulate(image[arm], scaled, b.Positron, 1.0);
                        }
                    }

                    FsaBetaLibrary.FermiScreening = false;
                    Check("кэш весов: " + nucid + " ВЫКЛ после ВКЛ побитно", Same(image[0], image[2]));
                    double m0 = MeanBin(image[0], section.BinKev), m1 = MeanBin(image[1], section.BinKev);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-6} площадь {1:F5} → {2:F5}, средняя {3:F2} → {4:F2} кэВ ({5:+0.000;-0.000} %)",
                                                    nucid, area[0], area[1], m0, m1, m0 > 0.0 ? 100.0 * (m1 / m0 - 1.0) : 0.0));
                    Console.WriteLine("         ВЫКЛ " + BandShares(image[0], section.BinKev) + Environment.NewLine + "         ВКЛ  " + BandShares(image[1], section.BinKev));
                }

                Console.WriteLine("ветви со строкой (выход ≥ 5 %): образ одной беты весами узлов ВЫКЛ/ВКЛ, без шума:");
                foreach (KeyValuePair<FsaBeta, int> pair in changed)
                {
                    FsaBeta beta = pair.Key;
                    if (beta.Intensity < 5.0)
                    {
                        continue;
                    }

                    var img = new double[2][];
                    var ar = new double[2];
                    for (int arm = 0; arm < 2; arm++)
                    {
                        FsaBetaLibrary.FermiScreening = arm == 1;
                        double mean;
                        double norm = FsaBetaLibrary.Integral(beta, out mean);
                        double[] w = section.NodeWeights(t => FsaBetaLibrary.Density(beta, t), beta.EndpointKev);
                        for (int k = 0; k < w.Length; k++)
                        {
                            w[k] /= norm;
                        }

                        img[arm] = new double[length];
                        ar[arm] = section.Accumulate(img[arm], w, false, 1.0);
                    }

                    FsaBetaLibrary.FermiScreening = false;
                    double a0 = MeanBin(img[0], section.BinKev), a1 = MeanBin(img[1], section.BinKev);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-6} {1,7:F1} кэВ {2,6:F2} %: площадь {3:F5} → {4:F5} ({5:+0.000;-0.000} %), средняя оставленной {6:F2} → {7:F2} кэВ ({8:+0.000;-0.000} %)",
                        section.BranchNuclides[pair.Value], beta.EndpointKev, beta.Intensity, ar[0], ar[1], 100.0 * (ar[1] / ar[0] - 1.0), a0, a1, a0 > 0.0 ? 100.0 * (a1 / a0 - 1.0) : 0.0));
                }
            }

            FsaBetaLibrary.FermiScreening = false;
            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "сцен с разделом электрона {0} из {1}, со сменой строк {2}; различных строк ветвей, которые меняет экранирование: {3}",
                                            scenesWithSection, scenes.Count, scenesChanged, changedTotal.Count));
            Check("сцены с разделом есть", scenesWithSection > 0);
        }

        static string Hash(FsaBeta beta, bool screening)
        {
            FsaBetaLibrary.FermiScreening = screening;
            string reason;
            ElectronBranchSpec s = FsaBetaLibrary.EventSpec(beta, out reason);
            FsaBetaLibrary.FermiScreening = false;
            return s != null ? s.Hash : null;
        }

        static bool Same(double[] a, double[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }

            return true;
        }

        static double MeanBin(double[] h, double bin)
        {
            double s = 0.0, m = 0.0;
            for (int i = 0; i < h.Length; i++)
            {
                s += h[i];
                m += h[i] * (i + 0.5) * bin;
            }

            return s != 0.0 ? m / s : 0.0;
        }

        static string BandShares(double[] h, double bin)
        {
            double total = h.Sum();
            var sb = new StringBuilder();
            for (int k = 0; k + 1 < Bands.Length; k++)
            {
                double s = 0.0;
                for (int i = 0; i < h.Length; i++)
                {
                    double e = (i + 0.5) * bin;
                    if (e >= Bands[k] && e < Bands[k + 1]) s += h[i];
                }

                sb.Append((total != 0.0 ? s / total : 0.0).ToString("F4", CultureInfo.InvariantCulture)).Append(' ');
            }

            return sb.ToString();
        }
    }
}
