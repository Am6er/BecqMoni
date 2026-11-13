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

namespace FermiFiniteSizeProbeA274
{
    /// <summary>
    /// (`AMBER274`, полоса `a274L` 13.11.2026) ПРИЁМКА КЛЮЧА `FsaBetaLibrary.FermiFiniteSize` —
    /// L₀ (конечный размер ядра, равномерно заряженная сфера) в функции Ферми всех форм беты
    /// кодом. Решение Amber 13.11.2026 вопросником, дословно: «Выключенным ключом + замер
    /// (Рекомендую)».
    ///
    ///   `--mode=control` — положительный контроль: Z → 0 даёт L₀ → 1 (и Z = 0 — ровно 1);
    ///       L₀ таблицы против формулы Уилкинсона (Hayen 2018, ур. 16, табл. I; своя копия
    ///       формулы, не кода приложения) при Z ≤ 58; мутанты «обратный знак заряда» и «L₀ = 1»
    ///       той же проверкой пойманы; таблица против прямого счёта и сходимость по дальности у
    ///       Z = 82, 90, 92; плотность ВКЛ = ВЫКЛ × L₀ поточечно, таблица `beta_shapes` ключом не
    ///       сдвигается; умолчание ключа — ВЫКЛ.
    ///   `--mode=means [--min-intensity=0.5] [--csv=файл]` — средние энергии ВСЕХ ветвей β⁻/β⁺
    ///       `nucdb` (все нуклиды с ветвями) к средним базы при четырёх сочетаниях λ ВЫКЛ/ВКЛ ×
    ///       L₀ ВЫКЛ/ВКЛ; сводка по полосам Z и видам формы; Z ≤ 58 — не хуже ли.
    ///   `--mode=store --store=каталог [--scenes=a.rmx,b.rmx] [--histories=N]
    ///       [--row-nuclides=214BI,208TL]` — сцены копии склада: раздел электрона при ВКЛ не
    ///       берётся (`l0=`), с полем `l0=1` при ВКЛ берётся, при ВЫКЛ — нет; сколько строк
    ///       ветвей меняет ВКЛ; образ беты весами узлов ВЫКЛ/ВКЛ и кэш весов; строки ветвей
    ///       заново одним зерном (рецепт зерна построителя, 9001 + j) ВЫКЛ/ВКЛ.
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
            var rowNuclides = new List<string> { "214BI", "208TL", "212PB", "212BI", "228AC" };
            int histories = 0;
            double minIntensity = 0.5;
            foreach (string a in args)
            {
                if (a.StartsWith("--mode=", StringComparison.Ordinal)) mode = a.Substring(7);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
                else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
                else if (a.StartsWith("--scenes=", StringComparison.Ordinal)) scenes.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--row-nuclides=", StringComparison.Ordinal)) rowNuclides = new List<string>(a.Substring(15).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--histories=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--min-intensity=", StringComparison.Ordinal)) minIntensity = double.Parse(a.Substring(16), CultureInfo.InvariantCulture);
                else
                {
                    Console.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            Check("умолчание ключа L₀ (FermiFiniteSize) — ВЫКЛ", !FsaBetaLibrary.FermiFiniteSize);
            Check("умолчание ключа λ — ВЫКЛ", !FsaBetaLibrary.CoulombLambda);
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

                Store(scenes, histories, rowNuclides);
            }

            FsaBetaLibrary.FermiFiniteSize = false;
            FsaBetaLibrary.CoulombLambda = false;
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

        // ------------------------------------------------------------------ control

        static void Control()
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. Z → 0: L₀ → 1 ===");
            Check("Z = 0: L₀ ровно 1", CoulombSphere.FiniteSizeL0(0, 4, false, 0.5) == 1.0);
            Console.WriteLine("{0,4} {1,4} {2,9} {3,12} {4,12}", "Z", "A", "T, кэВ", "L₀ − 1", "Уилк. − 1");
            var dev = new Dictionary<int, double>();
            foreach (int z in new[] { 1, 2, 5, 10, 20 })
            {
                int a = Math.Max(2, 2 * z);
                double worst = 0.0;
                foreach (double t in new[] { 10.0, 100.0, 1000.0, 3000.0 })
                {
                    double w = 1.0 + t / Me, p = Math.Sqrt(w * w - 1.0);
                    double l0 = CoulombSphere.FiniteSizeL0(z, a, false, p);
                    double wl = WilkinsonL0(z, w, CoulombSphere.Radius(a));
                    worst = Math.Max(worst, Math.Abs(l0 - 1.0));
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,4} {1,4} {2,9:F1} {3,12:E3} {4,12:E3}", z, a, t, l0 - 1.0, wl - 1.0));
                }

                dev[z] = worst;
            }

            Check("Z = 1: |L₀ − 1| < 1·10⁻³ на 10…3000 кэВ", dev[1] < 1e-3);
            Check("|L₀ − 1| растёт с Z монотонно (1 < 2 < 5 < 10 < 20)", dev[1] < dev[2] && dev[2] < dev[5] && dev[5] < dev[10] && dev[10] < dev[20]);

            Console.WriteLine();
            Console.WriteLine("=== 2. L₀ таблицы (то, что берёт плотность) против Уилкинсона, β⁻, Z ≤ 58 ===");
            double worstOk = 0.0, worstSign = 0.0, worstOne = 0.0;
            foreach (int[] za in new[] { new[] { 5, 10 }, new[] { 10, 22 }, new[] { 20, 40 }, new[] { 38, 89 }, new[] { 58, 138 } })
            {
                double radius = CoulombSphere.Radius(za[1]);
                foreach (double t in new[] { 5.0, 20.0, 100.0, 300.0, 1000.0, 2500.0 })
                {
                    double w = 1.0 + t / Me, p = Math.Sqrt(w * w - 1.0);
                    double wl = WilkinsonL0(za[0], w, radius);
                    double l0 = CoulombSphere.FiniteSizeL0(za[0], za[1], false, p);
                    double mutantSign = CoulombSphere.FiniteSizeL0(za[0], za[1], true, p);   // обратный знак заряда
                    worstOk = Math.Max(worstOk, Math.Abs(l0 / wl - 1.0));
                    worstSign = Math.Max(worstSign, Math.Abs(mutantSign / wl - 1.0));
                    worstOne = Math.Max(worstOne, Math.Abs(1.0 / wl - 1.0));
                    if (za[0] >= 20 && (t == 5.0 || t == 300.0 || t == 2500.0))
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Z {0,3} T {1,7:F1}: L₀ {2:F6}, Уилк. {3:F6}, мутант знака {4:F6}", za[0], t, l0, wl, mutantSign));
                    }
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "наибольшее |L₀/Уилк. − 1|: таблица {0:E2}; мутант «обратный знак заряда» {1:E2}; мутант «L₀ = 1» {2:E2}", worstOk, worstSign, worstOne));
            Check("L₀ таблицы = Уилкинсон до 2·10⁻⁴ (формула точна до 10⁻⁴, интерполяция до 5·10⁻⁵)", worstOk < 2e-4);
            Check("мутант «обратный знак заряда» той же проверкой пойман (> 2·10⁻⁴)", worstSign >= 2e-4);
            Check("мутант «L₀ = 1» (точечное F₀) той же проверкой пойман (> 2·10⁻⁴)", worstOne >= 2e-4);

            Console.WriteLine();
            Console.WriteLine("=== 3. Тяжёлые ядра: таблица против прямого счёта, сходимость по дальности ===");
            double worstTable = 0.0, worstReach = 0.0;
            Console.WriteLine("{0,4} {1,4} {2,2} {3,9} {4,10} {5,10} {6,10}", "Z", "A", "±", "T, кэВ", "L₀", "Уилк.", "прямой");
            foreach (int[] za in new[] { new[] { 58, 138 }, new[] { 82, 204 }, new[] { 83, 210 }, new[] { 90, 228 }, new[] { 92, 234 }, new[] { 12, 26 }, new[] { 18, 40 } })
            {
                foreach (bool positron in new[] { false, true })
                {
                    foreach (double t in new[] { 3.0, 17.3, 95.0, 420.0, 1700.0, 3000.0 })
                    {
                        if (positron && t < 10.0)
                        {
                            continue;   // под барьером плотность β⁺ — нуль
                        }

                        double w = 1.0 + t / Me, p = Math.Sqrt(w * w - 1.0);
                        double table = CoulombSphere.FiniteSizeL0(za[0], za[1], positron, p);
                        double direct = CoulombSphere.FiniteSizeL0Direct(za[0], za[1], positron, p, 1.0);
                        double far = CoulombSphere.FiniteSizeL0Direct(za[0], za[1], positron, p, 4.0);
                        worstTable = Math.Max(worstTable, Math.Abs(table / direct - 1.0));
                        worstReach = Math.Max(worstReach, Math.Abs(far / direct - 1.0));
                        if (t == 17.3 || t == 420.0 || t == 3000.0)
                        {
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,4} {1,4} {2,2} {3,9:F1} {4,10:F6} {5,10} {6,10:F6}", za[0], za[1], positron ? "+" : "-", t, table,
                                positron ? "—" : WilkinsonL0(za[0], w, CoulombSphere.Radius(za[1])).ToString("F6", CultureInfo.InvariantCulture), direct));
                        }
                    }

                    if (positron)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  Z {0} β⁺: узлов таблицы, взятых у соседа (под барьером): {1} из 160",
                                                        za[0], CoulombSphere.FiniteSizeL0Patched(za[0], za[1], true)));
                    }
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "таблица против прямого: {0:E2}; дальность ×4 против ×1: {1:E2}", worstTable, worstReach));
            Check("таблица L₀ (160 узлов ln p) = прямой счёт до 2·10⁻⁴ (β⁻ и β⁺ ≥ 10 кэВ)", worstTable < 2e-4);
            Check("сходимость по дальности до 1·10⁻⁴", worstReach < 1e-4);
            Check("β⁻ таблица без подмен узлов (Z = 82, 90, 92)", CoulombSphere.FiniteSizeL0Patched(82, 204, false) == 0
                                                                 && CoulombSphere.FiniteSizeL0Patched(90, 228, false) == 0
                                                                 && CoulombSphere.FiniteSizeL0Patched(92, 234, false) == 0);

            Console.WriteLine();
            Console.WriteLine("=== 4. Плотность: ВКЛ = ВЫКЛ × L₀ поточечно; таблица beta_shapes ключом не сдвигается ===");
            int branches = 0, exact = 0, tables = 0, tablesSame = 0, factor = 0;
            double worstPoint = 0.0;
            foreach (string nucid in new[] { "228AC", "214BI", "208TL", "212PB", "212BI", "40K", "138LA", "204TL", "210TL", "234PA", "22NA", "60CO", "137CS", "90SR", "90Y", "99TC", "14C", "3H" })
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
                    double worst = 0.0;
                    for (int i = 0; i < n; i++)
                    {
                        double t = b.EndpointKev * (i + 0.5) / n;
                        FsaBetaLibrary.FermiFiniteSize = false;
                        double off = FsaBetaLibrary.Density(b, t);
                        FsaBetaLibrary.FermiFiniteSize = true;
                        double on = FsaBetaLibrary.Density(b, t);
                        FsaBetaLibrary.FermiFiniteSize = false;
                        if (table)
                        {
                            same &= on == off;
                            continue;
                        }

                        double w = 1.0 + t / Me, p = Math.Sqrt(w * w - 1.0);
                        double expected = off * CoulombSphere.FiniteSizeL0(b.DaughterZ, b.MassNumber, b.Positron, p);
                        if (expected > 0.0)
                        {
                            worst = Math.Max(worst, Math.Abs(on / expected - 1.0));
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
                        if (b.ShapeFactor != null) factor++;
                        if (worst <= 1e-12) exact++;
                        worstPoint = Math.Max(worstPoint, worst);
                    }
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "ветвей кодом {0} (из них с фактором базы {1}): ВКЛ = ВЫКЛ × L₀ у {2}, наибольшее {3:E2}; таблиц {4}, побитно у {5}",
                                            branches, factor, exact, worstPoint, tables, tablesSame));
            Check("плотность ВКЛ = ВЫКЛ × L₀ (допуск 1e-12)", branches > 0 && exact == branches);
            Check("таблица beta_shapes ключом не сдвигается", tablesSame == tables);

            Console.WriteLine();
            Console.WriteLine("=== 5. L₀ по энергии (β⁻) — для журнала ===");
            Console.WriteLine("{0,4} {1,4} {2}", "Z", "A", "L₀ при T = 10 / 100 / 300 / 1000 / 2000 кэВ");
            foreach (int[] za in new[] { new[] { 20, 40 }, new[] { 56, 138 }, new[] { 82, 208 }, new[] { 83, 212 }, new[] { 90, 228 } })
            {
                var sb = new StringBuilder();
                foreach (double t in new[] { 10.0, 100.0, 300.0, 1000.0, 2000.0 })
                {
                    double w = 1.0 + t / Me, p = Math.Sqrt(w * w - 1.0);
                    sb.Append(CoulombSphere.FiniteSizeL0(za[0], za[1], false, p).ToString("F5", CultureInfo.InvariantCulture)).Append(' ');
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,4} {1,4} {2}", za[0], za[1], sb));
            }
        }

        // ------------------------------------------------------------------ means

        sealed class Row
        {
            public string Nuclide;
            public FsaBeta Beta;
            public string Kind;
            public double[] Mean = new double[4];   // λ0L0, λ0L1, λ1L0, λ1L1
        }

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
            Console.WriteLine();
            Console.WriteLine("=== Средние ветвей к средним базы (`nucdb`): λ ВЫКЛ/ВКЛ × L₀ ВЫКЛ/ВКЛ ===");
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
                FsaBetaLibrary.CoulombLambda = arm >= 2;
                FsaBetaLibrary.FermiFiniteSize = arm % 2 == 1;
                var t0 = watch.Elapsed.TotalSeconds;
                Parallel.ForEach(rows, r =>
                {
                    double mean;
                    FsaBetaLibrary.Integral(r.Beta, out mean);
                    r.Mean[arm] = mean;
                });
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "плечо λ {0} L₀ {1}: {2:F1} с", arm >= 2 ? "ВКЛ" : "ВЫКЛ", arm % 2 == 1 ? "ВКЛ" : "ВЫКЛ", watch.Elapsed.TotalSeconds - t0));
            }

            FsaBetaLibrary.CoulombLambda = false;
            FsaBetaLibrary.FermiFiniteSize = false;

            if (csv != null)
            {
                using (var wr = new StreamWriter(csv, false, new UTF8Encoding(false)))
                {
                    wr.WriteLine("nucid,sign,Z,A,E0_keV,I_pct,kind,transition,mean_base,mean_l0L0,mean_l0L1,mean_l1L0,mean_l1L1");
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

            // Табличные формы ключами не сдвигаются — проверка, и из сводки они выпадают.
            int tableRows = 0, tableSame = 0;
            foreach (Row r in rows.Where(r => r.Kind == "табл"))
            {
                tableRows++;
                if (r.Mean[0] == r.Mean[1] && r.Mean[0] == r.Mean[2] && r.Mean[0] == r.Mean[3]) tableSame++;
            }

            Check("средние табличных форм одинаковы во всех четырёх плечах", tableSame == tableRows);

            int[][] bands = { new[] { 1, 20 }, new[] { 21, 40 }, new[] { 41, 58 }, new[] { 59, 80 }, new[] { 81, 88 }, new[] { 89, 100 } };
            string[] kinds = { "разр", "неуник", "уник", "фактор" };
            foreach (bool positron in new[] { false, true })
            {
                Console.WriteLine();
                Console.WriteLine((positron ? "β⁺" : "β⁻") + string.Format(CultureInfo.InvariantCulture, ", выход ≥ {0} %: среднее (ср/база − 1), % — и среднее |…|, % — λ0L0 / λ0L1 / λ1L0 / λ1L1", minIntensity));
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

            // Z ≤ 58: не хуже ли. «Хуже» — |откл.| ВКЛ L₀ больше |откл.| ВЫКЛ на 0.05 % и больше.
            Console.WriteLine();
            foreach (bool lambda in new[] { false, true })
            {
                int a0 = lambda ? 2 : 0, a1 = a0 + 1;
                var light = rows.Where(r => r.Kind != "табл" && r.Beta.DaughterZ <= 58 && r.Beta.Intensity >= minIntensity).ToList();
                var heavy = rows.Where(r => r.Kind != "табл" && r.Beta.DaughterZ >= 81 && r.Beta.Intensity >= minIntensity).ToList();
                Func<List<Row>, int, double> mabs = (l, arm) => l.Count == 0 ? 0.0 : 100.0 * l.Average(r => Math.Abs(r.Mean[arm] / r.Beta.MeanKev - 1.0));
                int worse = light.Count(r => Math.Abs(r.Mean[a1] / r.Beta.MeanKev - 1.0) > Math.Abs(r.Mean[a0] / r.Beta.MeanKev - 1.0) + 5e-4);
                int better = light.Count(r => Math.Abs(r.Mean[a1] / r.Beta.MeanKev - 1.0) + 5e-4 < Math.Abs(r.Mean[a0] / r.Beta.MeanKev - 1.0));
                double shift = light.Count == 0 ? 0.0 : light.Max(r => Math.Abs(r.Mean[a1] / r.Mean[a0] - 1.0));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "λ {0}: Z ≤ 58 ветвей {1}: среднее |откл.| {2:F3} → {3:F3} %, хуже на ≥ 0.05 % у {4}, лучше у {5}, наибольший сдвиг средней ключом L₀ {6:F3} %; Z ≥ 81 ветвей {7}: {8:F3} → {9:F3} %",
                    lambda ? "ВКЛ" : "ВЫКЛ", light.Count, mabs(light, a0), mabs(light, a1), worse, better, 100.0 * shift, heavy.Count, mabs(heavy, a0), mabs(heavy, a1)));
                if (!lambda)
                {
                    Check("Z ≤ 58 (λ ВЫКЛ): среднее |откл.| при L₀ ВКЛ не больше, чем при ВЫКЛ", mabs(light, a1) <= mabs(light, a0) + 1e-9);
                }
                else
                {
                    Check("Z ≤ 58 (λ ВКЛ): среднее |откл.| при L₀ ВКЛ не больше, чем при ВЫКЛ", mabs(light, a1) <= mabs(light, a0) + 1e-9);
                }
            }

            // Три числа критика `crit274t` (1-й уникальный, λ сферы, F₀ → F₀L₀) и разрешённые Ac-228.
            Console.WriteLine();
            Console.WriteLine("сверка с находкой критика (ср/база: λ0L0 λ0L1 λ1L0 λ1L1; сдвиг L₀ при λ ВКЛ):");
            foreach (string[] probe in new[] { new[] { "204TL", "763" }, new[] { "210TL", "2413" }, new[] { "228AC", "1796" }, new[] { "228AC", "2069" }, new[] { "228AC", "1158" }, new[] { "228AC", "1731" }, new[] { "40K", "1311" }, new[] { "138LA", "255" } })
            {
                double e = double.Parse(probe[1], CultureInfo.InvariantCulture);
                foreach (Row r in rows.Where(r => string.Equals(r.Nuclide, probe[0], StringComparison.OrdinalIgnoreCase) && Math.Abs(r.Beta.EndpointKev - e) < 10.0))
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-6} {1,7:F1} кэВ {2,7:F3} % {3,-20} база {4,8:F2}: {5:F4} {6:F4} {7:F4} {8:F4}; L₀ при λ ВКЛ {9:+0.00;-0.00} %",
                        r.Nuclide, r.Beta.EndpointKev, r.Beta.Intensity, r.Beta.Transition, r.Beta.MeanKev,
                        r.Mean[0] / r.Beta.MeanKev, r.Mean[1] / r.Beta.MeanKev, r.Mean[2] / r.Beta.MeanKev, r.Mean[3] / r.Beta.MeanKev, 100.0 * (r.Mean[3] / r.Mean[2] - 1.0)));
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "время средних {0:F1} с", watch.Elapsed.TotalSeconds));
        }

        // ------------------------------------------------------------------ store

        static readonly double[] Bands = { 0.0, 50.0, 100.0, 150.0, 300.0, 600.0, 1000.0, 4000.0 };

        static void Store(List<string> scenes, int histories, List<string> rowNuclides)
        {
            bool first = true;
            int scenesWithSection = 0;
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
                FsaBetaLibrary.FermiFiniteSize = false;
                ResponseElectronSection section = matrix.ElectronSectionFor(geometry, out why);
                if (section == null)
                {
                    Console.WriteLine("раздела электрона нет или не годен: " + why);
                    continue;
                }

                scenesWithSection++;
                FsaBetaLibrary.FermiFiniteSize = true;
                ResponseElectronSection on = matrix.ElectronSectionFor(geometry, out why);
                Check("ВКЛ: раздел без `l0=` разбор не берёт (OtherPhysics)", on == null && why == ElectronSectionRefusal.OtherPhysics);
                FsaBetaLibrary.FermiFiniteSize = false;
                Check("ВЫКЛ: раздел годен, поля `l0` нет", matrix.ElectronSectionFor(geometry, out why) != null && section.StampField("l0") == null);

                if (first)
                {
                    // Второе плечо: раздел с `l0=1` при ВКЛ берётся, при ВЫКЛ — нет; с λ — оба поля.
                    string saved = section.Stamp;
                    int capAt = saved.IndexOf(";cap=", StringComparison.Ordinal);
                    Func<string, string> inject = f => capAt >= 0 ? saved.Substring(0, capAt) + f + saved.Substring(capAt) : saved + f;
                    section.Stamp = inject(";l0=1");
                    FsaBetaLibrary.FermiFiniteSize = true;
                    bool takenOn = matrix.ElectronSectionFor(geometry, out why) != null;
                    FsaBetaLibrary.FermiFiniteSize = false;
                    ElectronSectionRefusal whyOff;
                    bool takenOff = matrix.ElectronSectionFor(geometry, out whyOff) != null;
                    Check("раздел с `l0=1` при ВКЛ берётся", takenOn);
                    Check("раздел с `l0=1` при ВЫКЛ не берётся (OtherPhysics)", !takenOff && whyOff == ElectronSectionRefusal.OtherPhysics);
                    section.Stamp = inject(";l0=2");
                    FsaBetaLibrary.FermiFiniteSize = true;
                    Check("раздел с другой версией `l0=2` при ВКЛ не берётся", matrix.ElectronSectionFor(geometry, out why) == null && why == ElectronSectionRefusal.OtherPhysics);
                    section.Stamp = inject(";cl=2;l0=1");
                    FsaBetaLibrary.CoulombLambda = true;
                    Check("раздел с `cl=2;l0=1`: λ ВКЛ + L₀ ВКЛ — берётся", matrix.ElectronSectionFor(geometry, out why) != null);
                    FsaBetaLibrary.FermiFiniteSize = false;
                    Check("раздел с `cl=2;l0=1`: λ ВКЛ + L₀ ВЫКЛ — не берётся", matrix.ElectronSectionFor(geometry, out why) == null);
                    FsaBetaLibrary.CoulombLambda = false;
                    section.Stamp = saved;
                    Check("клеймо возвращено, раздел годен", matrix.ElectronSectionFor(geometry, out why) != null);
                    Console.WriteLine("клеймо раздела: " + section.Stamp);
                }

                // Строки ветвей: хеш ВЫКЛ = записанному; какие меняет L₀ (λ ВЫКЛ), λ (L₀ ВЫКЛ), оба.
                int rows = section.BranchNuclides != null ? section.BranchNuclides.Length : 0;
                int found = 0, sameOff = 0, byL0 = 0, byLambda = 0, byBoth = 0;
                var changed = new List<KeyValuePair<FsaBeta, int>>();
                var sbChanged = new StringBuilder();
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
                    string h00 = Hash(beta, false, false), h01 = Hash(beta, false, true), h10 = Hash(beta, true, false), h11 = Hash(beta, true, true);
                    if (h00 != null && section.BranchHashes != null && h00 == section.BranchHashes[j]) sameOff++;
                    if (h00 != h01)
                    {
                        byL0++;
                        changed.Add(new KeyValuePair<FsaBeta, int>(beta, j));
                        string key = section.BranchNuclides[j] + " " + beta.EndpointKev.ToString("F1", CultureInfo.InvariantCulture);
                        changedTotal[key] = (changedTotal.ContainsKey(key) ? changedTotal[key] : 0) + 1;
                    }
                    else
                    {
                        sbChanged.Append(section.BranchNuclides[j]).Append(' ').Append(beta.EndpointKev.ToString("F1", CultureInfo.InvariantCulture)).Append(" (").Append(beta.Transition).Append("); ");
                    }

                    if (h00 != h10) byLambda++;
                    if (h00 != h11) byBoth++;
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "строк ветвей {0}, найдено в библиотеке {1}; ВЫКЛ: хеш = записанному у {2}; меняет L₀ (λ ВЫКЛ) {3}, λ (L₀ ВЫКЛ) {4}, оба {5}",
                    rows, found, sameOff, byL0, byLambda, byBoth));
                if (sbChanged.Length > 0)
                {
                    Console.WriteLine("  L₀ НЕ меняет: " + sbChanged);
                }

                Check("ВЫКЛ: строки ветвей склада побитно по входу (хеш формы и каскада)", rows > 0 && found == rows && sameOff == rows);

                if (!first)
                {
                    continue;
                }

                first = false;

                // Образ беты весами узлов (без пересчёта склада), ВЫКЛ / ВКЛ, и кэш весов.
                int length = (int)(4000.0 / section.BinKev);
                Console.WriteLine("образ беты нуклида разбором (Σ ветвей I·Σwₖ·Rₖ; у ветви со строкой — её строка склада), полосы "
                                  + string.Join("/", Array.ConvertAll(Bands, b => b.ToString("F0", CultureInfo.InvariantCulture))) + " кэВ:");
                foreach (string nucid in new[] { "40K", "138LA", "90SR", "90Y", "137CS", "228AC", "212PB", "212BI", "208TL", "214BI", "214PB", "210BI", "234PA" })
                {
                    var image = new double[3][];
                    var area = new double[3];
                    for (int arm = 0; arm < 3; arm++)
                    {
                        FsaBetaLibrary.FermiFiniteSize = arm == 1;
                        image[arm] = new double[length];
                        foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
                        {
                            if (b.IsLine || b.Positron || !(b.Intensity > 0.0))
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

                            area[arm] += section.Accumulate(image[arm], scaled, false, 1.0);
                        }
                    }

                    FsaBetaLibrary.FermiFiniteSize = false;
                    Check("кэш весов: " + nucid + " ВЫКЛ после ВКЛ побитно", Same(image[0], image[2]));
                    double m0 = MeanBin(image[0], section.BinKev), m1 = MeanBin(image[1], section.BinKev);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-6} площадь {1:F5} → {2:F5}, средняя {3:F2} → {4:F2} кэВ ({5:+0.000;-0.000} %)",
                                                    nucid, area[0], area[1], m0, m1, m0 > 0.0 ? 100.0 * (m1 / m0 - 1.0) : 0.0));
                    Console.WriteLine("         ВЫКЛ " + BandShares(image[0], section.BinKev) + Environment.NewLine + "         ВКЛ  " + BandShares(image[1], section.BinKev));
                }

                // Ветви со строкой, которые меняет L₀: образ одной беты ВЕСАМИ УЗЛОВ (как если бы
                // строки не было) ВЫКЛ / ВКЛ — без шума Монте-Карло; это сдвиг, который строка,
                // посчитанная заново, несла бы в бете (каскад ключом не меняется).
                Console.WriteLine("ветви со строкой, которые меняет L₀ (выход ≥ 5 %): образ беты весами узлов, без шума:");
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
                        FsaBetaLibrary.FermiFiniteSize = arm == 1;
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

                    FsaBetaLibrary.FermiFiniteSize = false;
                    double a0 = MeanBin(img[0], section.BinKev), a1 = MeanBin(img[1], section.BinKev);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-6} {1,7:F1} кэВ {2,6:F2} %: площадь {3:F5} → {4:F5} ({5:+0.000;-0.000} %), средняя оставленной {6:F2} → {7:F2} кэВ ({8:+0.000;-0.000} %)",
                        section.BranchNuclides[pair.Value], beta.EndpointKev, beta.Intensity, ar[0], ar[1], 100.0 * (ar[1] / ar[0] - 1.0), a0, a1, a0 > 0.0 ? 100.0 * (a1 / a0 - 1.0) : 0.0));
                }

                // Строки ветвей заново одним зерном (рецепт зерна построителя, 9001 + j), ВЫКЛ / ВКЛ.
                if (histories <= 0)
                {
                    continue;
                }

                var make = typeof(ResponseMatrixBuilder).GetMethod("MakeSimulator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "строки ветвей заново, {0} историй, «событие − каскад» (то, что берёт разбор):", histories));
                foreach (KeyValuePair<FsaBeta, int> pair in changed)
                {
                    if (!rowNuclides.Contains(section.BranchNuclides[pair.Value], StringComparer.OrdinalIgnoreCase) || pair.Key.Intensity < 5.0)
                    {
                        continue;
                    }

                    var row = new double[2][];
                    var areaRow = new double[2];
                    for (int arm = 0; arm < 2; arm++)
                    {
                        FsaBetaLibrary.FermiFiniteSize = arm == 1;
                        string reason;
                        ElectronBranchSpec spec = FsaBetaLibrary.EventSpec(pair.Key, out reason);
                        var sim = (EfficiencySimulator)make.Invoke(null, new object[] { geometry, matrix.Options, 9001 + pair.Value, spec.EndpointKev });
                        sim.Histories = histories;
                        sim.ResetStream((ulong)sim.Seed + (ulong)(9001 + pair.Value) * 0x9E3779B97F4A7C15UL);
                        EfficiencySimulator.BranchEventStats bs;
                        double[][] r = sim.BranchEventResponse(spec, section.BinKev, 0.0, out bs);
                        row[arm] = new double[length];
                        for (int b = 0; b < r[0].Length && b < length; b++)
                        {
                            row[arm][b] = r[0][b] - (b < r[1].Length ? r[1][b] : 0.0);
                            areaRow[arm] += row[arm][b];
                        }
                    }

                    FsaBetaLibrary.FermiFiniteSize = false;
                    double mOff = MeanBin(row[0], section.BinKev), mOn = MeanBin(row[1], section.BinKev);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0} {1:F1} кэВ {2:F2} %: площадь {3:F5} → {4:F5} ({5:+0.00;-0.00} %), средняя {6:F2} → {7:F2} кэВ ({8:+0.000;-0.000} %)",
                                                    pair.Key.Nuclide, pair.Key.EndpointKev, pair.Key.Intensity, areaRow[0], areaRow[1],
                                                    areaRow[0] != 0.0 ? 100.0 * (areaRow[1] / areaRow[0] - 1.0) : 0.0, mOff, mOn, mOff > 0.0 ? 100.0 * (mOn / mOff - 1.0) : 0.0));
                    Console.WriteLine("         ВЫКЛ " + BandShares(row[0], section.BinKev) + Environment.NewLine + "         ВКЛ  " + BandShares(row[1], section.BinKev));
                }
            }

            FsaBetaLibrary.FermiFiniteSize = false;
            FsaBetaLibrary.CoulombLambda = false;
            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "сцен с разделом электрона {0} из {1}; различных строк ветвей, которые меняет L₀: {2}", scenesWithSection, scenes.Count, changedTotal.Count));
            Check("сцены с разделом есть", scenesWithSection > 0);
        }

        static string Hash(FsaBeta beta, bool lambda, bool l0)
        {
            FsaBetaLibrary.CoulombLambda = lambda;
            FsaBetaLibrary.FermiFiniteSize = l0;
            string reason;
            ElectronBranchSpec s = FsaBetaLibrary.EventSpec(beta, out reason);
            FsaBetaLibrary.CoulombLambda = false;
            FsaBetaLibrary.FermiFiniteSize = false;
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

        // ------------------------------------------------------------------ Уилкинсон (своя копия)

        /// <summary>Таблица I Уилкинсона (1990) для электронов по Hayen и др. 2018: строки a₋₁, a₀…a₅, столбцы b₁…b₆.</summary>
        static readonly double[,] WilkinsonB =
        {
            { 0.115, -1.8123, 8.2498, -11.223, -14.854, 32.086 },
            { -0.00062, 0.007165, 0.01841, -0.53736, 1.2691, -1.5467 },
            { 0.02482, -0.5975, 4.84199, -15.3374, 23.9774, -12.6534 },
            { -0.14038, 3.64953, -38.8143, 172.1368, -346.708, 288.7873 },
            { 0.008152, -1.15664, 49.9663, -273.711, 657.6292, -603.7033 },
            { 1.2145, -23.9931, 149.9718, -471.2985, 662.1909, -305.6804 },
            { -1.5632, 33.4192, -255.1333, 938.5297, -1641.2845, 1095.358 },
        };

        /// <summary>L₀ Уилкинсона для электронов (Hayen 2018, ур. 16–17); R — в единицах ħ/(mₑc).</summary>
        static double WilkinsonL0(int z, double w, double radius)
        {
            double az = z * Alpha;
            double g = Math.Sqrt(1.0 - az * az);
            var a = new double[7];
            for (int n = 0; n < 7; n++)
            {
                for (int x = 1; x <= 6; x++)
                {
                    a[n] += WilkinsonB[n, x - 1] * Math.Pow(az, x);
                }
            }

            double sum = a[0] * radius / w;
            for (int n = 0; n <= 5; n++)
            {
                sum += a[n + 1] * Math.Pow(w * radius, n);
            }

            return 1.0 + 13.0 / 60.0 * az * az - az * w * radius * (41.0 - 26.0 * g) / (15.0 * (2.0 * g - 1.0))
                   - az * radius * g * (17.0 - 2.0 * g) / (30.0 * w * (2.0 * g - 1.0)) + sum
                   + 0.41 * (radius - 0.0164) * Math.Pow(az, 4.5);
        }
    }
}
