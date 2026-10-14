using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.NucBase;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П266, 14.10.2026) Сверка ДАННЫХ РАСПАДА, как их видит приложение, с самой базой
    /// (`nucdb.sqlite`, только чтение) и с известными значениями — без окон.
    ///
    /// Разделы (каждый печатает «ок»/«!!» по строке и счёт расхождений):
    ///   1. период: <see cref="NucBaseFramework.HalfLifeYearsOf"/> против базы (все `nucid`) и
    ///      против известных значений (три десятка нуклидов);
    ///   2. имена: формат имени импорта NucBase (три формата, отражением) → `NucidOf`, `QueryNucid`,
    ///      `LineIdentity`, `PrettyName` — круг для КАЖДОГО `nucid` базы;
    ///   3. период корня у линии ряда (`NuclideDefinition.DecayHalfLifeYears`) — ряд в трёх
    ///      форматах имени;
    ///   4. ветвление рядов: два обхода (NucBase, FSA) против известных долей;
    ///   5. библиотека FSA по ряду (равновесие ВКЛ/ВЫКЛ) — сильные линии против выход × доля;
    ///   6. сильнейшие линии одиночных нуклидов (`DecayLines`) против известных выходов;
    ///   7. подписи кодов распада (`Decay`, `DecayRad`) — какие коды базы печатаются числом;
    ///   8. потоки: первые обращения к кэшам библиотеки из 8 потоков разом.
    /// Ключи: --all — печатать каждую строку раздела 2 с расхождением (иначе первые 20).
    /// Коды: 0 — всё сошлось; 1 — есть расхождения; 2 — базы нет.
    /// </summary>
    static class DecayDataProbeP266
    {
        static int bad;
        static bool printAll;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");   // разбор обязан быть безразличен к культуре
            printAll = args.Contains("--all");
            string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(db))
            {
                Console.WriteLine("нет базы: " + db);
                return 2;
            }

            Section8Threads();          // ПЕРВЫМ — пока кэши пусты
            Section1HalfLife(db);
            Section2Names(db);
            Section3RootHalfLife();
            Section4Branches();
            Section5ChainLibrary();
            Section6Lines();
            Section7DecayLabels(db);
            Section9Card(db);
            Section10Starter();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- 1
        static readonly object[][] KnownHalfLives =
        {
            // nucid, значение, единица (как в ENSDF/NuDat), допуск относительный
            new object[] { "137CS", 30.08, "Y" }, new object[] { "60CO", 5.2714, "Y" },
            new object[] { "241AM", 432.6, "Y" }, new object[] { "40K", 1.248e9, "Y" },
            new object[] { "238U", 4.468e9, "Y" }, new object[] { "232TH", 1.40e10, "Y" },
            new object[] { "226RA", 1600.0, "Y" }, new object[] { "222RN", 3.8235, "d" },
            new object[] { "214PB", 27.06, "m" }, new object[] { "214BI", 19.9, "m" },
            new object[] { "210PB", 22.20, "Y" }, new object[] { "228AC", 6.15, "h" },
            new object[] { "212PB", 10.64, "h" }, new object[] { "212BI", 60.55, "m" },
            new object[] { "208TL", 3.053, "m" }, new object[] { "228TH", 1.9116, "Y" },
            new object[] { "224RA", 3.6319, "d" }, new object[] { "176LU", 3.76e10, "Y" },
            new object[] { "152EU", 13.517, "Y" }, new object[] { "133BA", 10.551, "Y" },
            new object[] { "131I", 8.0252, "d" }, new object[] { "234TH", 24.10, "d" },
            new object[] { "234PAm1", 1.159, "m" }, new object[] { "234U", 2.455e5, "Y" },
            new object[] { "230TH", 7.54e4, "Y" }, new object[] { "235U", 7.04e8, "Y" },
            new object[] { "22NA", 2.6018, "Y" }, new object[] { "57CO", 271.74, "d" },
            new object[] { "109CD", 461.9, "d" }, new object[] { "54MN", 312.20, "d" },
            new object[] { "134CS", 2.0652, "Y" }, new object[] { "88Y", 106.626, "d" },
            new object[] { "207BI", 31.55, "Y" }, new object[] { "99TCm", 6.007, "h" },
            new object[] { "18F", 109.77, "m" }, new object[] { "65ZN", 243.93, "d" },
            new object[] { "214PO", 164.3, "us" }, new object[] { "212PO", 294.3, "ns" },
            new object[] { "216PO", 145.0, "ms" }, new object[] { "220RN", 55.6, "s" },
            new object[] { "3H", 12.32, "Y" }, new object[] { "239PU", 24110.0, "Y" },
        };

        static double UnitSeconds(string unit)
        {
            switch (unit)
            {
                case "s": return 1.0;
                case "m": return 60.0;
                case "h": return 3600.0;
                case "d": return 86400.0;
                case "Y": return 31536000.0;      // год поля HalfLife = 365 сут (AMBER148/AMBER195)
                case "ms": return 1e-3;
                case "us": return 1e-6;
                case "ns": return 1e-9;
                default: return double.NaN;
            }
        }

        static void Section1HalfLife(string db)
        {
            Console.WriteLine("=== 1. период полураспада: HalfLifeYearsOf против базы и известного ===");
            int checkedRows = 0, mism = 0, stable = 0, unknown = 0;
            var rows = new List<string[]>();
            using (var c = Open(db))
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "select nucid, half_life, half_life_unit, l_seqno from nuclides where half_life is not null order by nucid, l_seqno";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        rows.Add(new[] { r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2) });
                    }
                }
            }

            var firstOf = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (string[] row in rows)
            {
                if (!firstOf.ContainsKey(row[0])) firstOf[row[0]] = row;
            }

            int shown = 0;
            foreach (string[] row in firstOf.Values)
            {
                double value;
                double expected = 0.0;
                if (double.TryParse(row[1], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    expected = value * UnitSeconds(row[2]) / 31536000.0;
                }
                else if (row[1] == "STABLE") stable++;
                else unknown++;

                double app = NucBaseFramework.HalfLifeYearsOf(row[0]);
                checkedRows++;
                bool ok = expected > 0.0 ? Math.Abs(app / expected - 1.0) < 1e-12 : app == 0.0;
                if (!ok)
                {
                    mism++;
                    if (printAll || shown++ < 20)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  !! {0,-10} база «{1} {2}» → ждали {3:G8} лет, приложение {4:G8}", row[0], row[1], row[2], expected, app));
                    }
                }
            }

            Console.WriteLine("  нуклидов {0}: стабильных {1}, «?» {2}; расхождений {3}", checkedRows, stable, unknown, mism);
            bad += mism;

            foreach (object[] k in KnownHalfLives)
            {
                string nucid = (string)k[0];
                double known = (double)k[1] * UnitSeconds((string)k[2]) / 31536000.0;
                double app = NucBaseFramework.HalfLifeYearsOf(nucid);
                Near("  T½ " + nucid + " (известно " + ((double)k[1]).ToString("G6", CultureInfo.InvariantCulture) + " " + k[2] + ")", known, app, 0.012);
            }
        }

        // ---------------------------------------------------------------- 2
        static void Section2Names(string db)
        {
            Console.WriteLine("=== 2. имена: формат импорта NucBase → nucid, запрос поиска, ключ импорта ===");
            var nucids = new List<string>();
            using (var c = Open(db))
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "select distinct nucid from nuclides where nucid is not null";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) nucids.Add(r.GetString(0));
                }
            }

            var form = new NucBase.NucBase();
            MethodInfo format = typeof(NucBase.NucBase).GetMethod("FormatIsotopeName", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo comboField = typeof(NucBase.NucBase).GetField("comboBoxNameFormat", BindingFlags.NonPublic | BindingFlags.Instance);
            var combo = (System.Windows.Forms.ComboBox)comboField.GetValue(form);
            int mism = 0, shown = 0, notFormatted = 0;
            var queryBad = new List<string>();
            foreach (string nucid in nucids)
            {
                string identity = NucBase.NucBase.LineIdentity(nucid, false);
                for (int f = 0; f < 3; f++)
                {
                    combo.SelectedIndex = f;
                    string name = (string)format.Invoke(form, new object[] { nucid });
                    if (f > 0 && name == nucid) notFormatted++;
                    string back = FsaSampleLibrary.NucidOf(name);
                    string query = NucBaseFramework.QueryNucid(name);
                    string id = NucBase.NucBase.LineIdentity(name, false);
                    if (back != nucid || query != nucid || id != identity)
                    {
                        mism++;
                        if (printAll || shown++ < 20)
                        {
                            Console.WriteLine("  !! {0,-9} формат {1}: «{2}» → NucidOf «{3}», запрос «{4}», ключ «{5}» (ждали «{6}»)",
                                              nucid, f, name, back, query, id, identity);
                        }
                    }
                }

                string pretty = FsaSampleLibrary.PrettyName(nucid);
                string prettyBack = FsaSampleLibrary.NucidOf(pretty);
                if (prettyBack != nucid)
                {
                    queryBad.Add(nucid + " → «" + pretty + "» → «" + prettyBack + "»");
                }
            }

            Console.WriteLine("  nucid {0}, имён проверено {1}; расхождений {2}; формат не применился (имя осталось nucid) {3}",
                              nucids.Count, nucids.Count * 3, mism, notFormatted);
            bad += mism;
            // PrettyName режет номер ПЕРВОГО изомера нарочно (AMBER78); круг через подпись корпуса —
            // только сведение, не расхождение, если обратный разбор даёт то же состояние.
            Console.WriteLine("  круг через подпись разбора (PrettyName → NucidOf) не вернулся: {0}{1}", queryBad.Count,
                              queryBad.Count > 0 ? " — напр. " + string.Join("; ", queryBad.Take(6)) : "");

            // Написания, которые человек набирает в поиске
            string[][] typed =
            {
                new[] { "Cs-137", "137CS" }, new[] { "cs137", "137CS" }, new[] { "137Cs", "137CS" }, new[] { "137 Cs", "137CS" },
                new[] { "Am-241", "241AM" }, new[] { "241Am", "241AM" }, new[] { "am241", "241AM" },
                new[] { "Tc-99m", "99TCm" }, new[] { "99mTc", "99TCm" }, new[] { "Tc99m", "99TCm" },
                new[] { "Pa-234m", "234PAm1" }, new[] { "Pa234m1", "234PAm1" }, new[] { "234mPa", "234PAm1" },
                new[] { "Ba-137m", "137BAm" }, new[] { "137mBa", "137BAm" }, new[] { "Ag-108m", "108AGm" },
                new[] { "Ag-110m", "110AGm1" }, new[] { "U-238", "238U" }, new[] { "U238", "238U" },
                new[] { "K-40", "40K" }, new[] { "K40", "40K" }, new[] { "40K", "40K" },
                new[] { "Th-232", "232TH" }, new[] { "Ra-226", "226RA" }, new[] { "Rn-222", "222RN" },
                new[] { "Lu-176", "176LU" }, new[] { "Eu-152", "152EU" }, new[] { "Ba-133", "133BA" },
                new[] { "I-131", "131I" }, new[] { "H-3", "3H" }, new[] { "Co-60", "60CO" },
            };
            foreach (string[] t in typed)
            {
                string q = NucBaseFramework.QueryNucid(t[0]);
                Same("  запрос «" + t[0] + "»", t[1], q);
            }
        }

        // ---------------------------------------------------------------- 3
        static void Section3RootHalfLife()
        {
            Console.WriteLine("=== 3. период корня у линии ряда (AMBER195) ===");
            double th = NucBaseFramework.HalfLifeYearsOf("232TH");
            double ra = NucBaseFramework.HalfLifeYearsOf("226RA");
            double own = NucBaseFramework.HalfLifeYearsOf("208TL");
            foreach (string chain in new[] { "Th-232", "Th232", "232TH", "th-232" })
            {
                var line = new NuclideDefinition { Name = "Tl208 (" + chain + ")", Chain = chain, Energy = 2614.5, HalfLife = own };
                Near("  Tl-208, ряд «" + chain + "»", th, NuclideDefinition.DecayHalfLifeYears(line, null), 1e-12);
            }

            foreach (string chain in new[] { "Ra-226", "Ra226", "226RA" })
            {
                var line = new NuclideDefinition { Name = "Bi-214", Chain = chain, Energy = 609.3, HalfLife = 3.75e-5 };
                Near("  Bi-214, ряд «" + chain + "»", ra, NuclideDefinition.DecayHalfLifeYears(line, null), 1e-12);
            }

            var self = new NuclideDefinition { Name = "Cs-137", Chain = "Cs137", Energy = 661.657, HalfLife = 30.08 };
            Near("  Cs-137, ряд — он сам", 30.08, NuclideDefinition.DecayHalfLifeYears(self, null), 1e-12);
            var none = new NuclideDefinition { Name = "Pb-214", Chain = "", Energy = 351.9, HalfLife = 5.1e-5 };
            Near("  Pb-214 без ряда — свой", 5.1e-5, NuclideDefinition.DecayHalfLifeYears(none, null), 1e-12);
            var filter = new NuclideDefinition { Name = "Cs-137", Chain = "Cs-137 (фильтр)", Energy = 661.657, HalfLife = 30.08 };
            Near("  ряд «Cs-137 (фильтр)» — свой", 30.08, NuclideDefinition.DecayHalfLifeYears(filter, null), 1e-12);
            var isomer = new NuclideDefinition { Name = "Ba-137m (Cs-137)", Chain = "Cs-137", Energy = 661.657, HalfLife = 4.86e-6 };
            Near("  Ba-137m, ряд «Cs-137»", NucBaseFramework.HalfLifeYearsOf("137CS"), NuclideDefinition.DecayHalfLifeYears(isomer, null), 1e-12);
        }

        // ---------------------------------------------------------------- 4
        static void Section4Branches()
        {
            Console.WriteLine("=== 4. ветвление рядов: два обхода против известных долей ===");
            var known = new Dictionary<string, Dictionary<string, double>>
            {
                { "232TH", new Dictionary<string, double> {
                    { "228RA", 1 }, { "228AC", 1 }, { "228TH", 1 }, { "224RA", 1 }, { "220RN", 1 }, { "216PO", 1 },
                    { "212PB", 1 }, { "212BI", 1 }, { "212PO", 0.6406 }, { "208TL", 0.3594 } } },
                { "238U", new Dictionary<string, double> {
                    { "234TH", 1 }, { "234PAm1", 1 }, { "234PA", 0.0016 }, { "234U", 1 }, { "230TH", 1 }, { "226RA", 1 },
                    { "222RN", 1 }, { "218PO", 1 }, { "214PB", 0.9998 }, { "214BI", 1 }, { "214PO", 0.99979 },
                    { "210TL", 0.00021 }, { "210PB", 1 }, { "210BI", 1 }, { "210PO", 1 } } },
                { "235U", new Dictionary<string, double> {
                    { "231TH", 1 }, { "231PA", 1 }, { "227AC", 1 }, { "227TH", 0.9862 }, { "223FR", 0.0138 }, { "223RA", 1 },
                    { "219RN", 1 }, { "215PO", 1 }, { "211PB", 1 }, { "211BI", 1 }, { "207TL", 0.99724 }, { "211PO", 0.00276 } } },
                { "226RA", new Dictionary<string, double> {
                    { "222RN", 1 }, { "218PO", 1 }, { "214PB", 0.9998 }, { "214BI", 1 }, { "214PO", 0.99979 }, { "210PB", 1 }, { "210PO", 1 } } },
                { "228TH", new Dictionary<string, double> {
                    { "224RA", 1 }, { "212PB", 1 }, { "212BI", 1 }, { "212PO", 0.6406 }, { "208TL", 0.3594 } } },
            };
            foreach (KeyValuePair<string, Dictionary<string, double>> root in known)
            {
                var fw = new NucBaseFramework();
                Dictionary<string, double> nb = fw.GetChainBranches(root.Key);
                Dictionary<string, double> fsa = FsaSampleLibrary.ChainBranches(root.Key, new FsaSampleLibrary.Report());
                foreach (KeyValuePair<string, double> m in root.Value)
                {
                    double a, b;
                    nb.TryGetValue(m.Key, out a);
                    fsa.TryGetValue(m.Key, out b);
                    double tol = m.Value < 0.01 ? 0.08 : 0.002;
                    Near("  " + root.Key + " → " + m.Key + " NucBase", m.Value, a, tol);
                    Near("  " + root.Key + " → " + m.Key + " FSA    ", m.Value, b, tol);
                }
            }

            // Множитель переходного равновесия (AMBER55): Ra-226 → Pb-210 = 1/(1 − 22.2/1600)
            MethodInfo factors = typeof(FsaSampleLibrary).GetMethod("EquilibriumFactors", BindingFlags.NonPublic | BindingFlags.Static);
            var f = (Dictionary<string, double>)factors.Invoke(null, new object[] { "226RA", new FsaSampleLibrary.Report() });
            double tPb = NucBaseFramework.HalfLifeYearsOf("210PB"), tRa = NucBaseFramework.HalfLifeYearsOf("226RA");
            double expect = 1.0 / (1.0 - tPb / tRa);
            double got;
            f.TryGetValue("210PB", out got);
            Near("  множитель Ra-226 → Pb-210", expect, got, 2e-4);
            f.TryGetValue("210PO", out got);
            Near("  множитель Ra-226 → Po-210 (через Pb-210)", expect, got, 2e-3);
        }

        // ---------------------------------------------------------------- 5
        static void Section5ChainLibrary()
        {
            Console.WriteLine("=== 5. библиотека FSA по ряду: сильные линии = выход × доля ===");
            // {метка ряда, нуклид линии, энергия, выход базы на свой распад, доля от корня}
            object[][] expect =
            {
                new object[] { "Th-232", "Ac-228", 911.204, 25.8, 1.0 },
                new object[] { "Th-232", "Pb-212", 238.632, 43.6, 1.0 },
                new object[] { "Th-232", "Tl-208", 2614.511, 99.754, 0.3594 },
                new object[] { "Th-232", "Tl-208", 583.187, 85.0, 0.3594 },
                new object[] { "Th-232", "Bi-212", 727.33, 6.67, 1.0 },
                new object[] { "Th-232", "Ra-224", 240.986, 4.1, 1.0 },
                new object[] { "U-238", "Pa-234m", 1001.03, 0.842, 1.0 },
                new object[] { "U-238", "Bi-214", 609.321, 45.44, 1.0 },
                new object[] { "U-238", "Pb-214", 351.932, 35.6, 0.9998 },
                new object[] { "U-238", "Ra-226", 186.211, 3.64, 1.0 },
                new object[] { "U-235", "U-235", 185.715, 57.0, 1.0 },
                new object[] { "U-235", "Ra-223", 269.463, 14.23, 1.0 },
                new object[] { "Ra-226", "Pb-210", 46.539, 4.25, 1.0 / (1.0 - 22.20 / 1600.0) },
                new object[] { "Ra-226", "Bi-214", 609.321, 45.44, 1.0 },
            };
            foreach (bool eq in new[] { true, false })
            {
                var cache = new Dictionary<string, List<FsaComponent>>();
                foreach (object[] e in expect)
                {
                    string label = (string)e[0];
                    List<FsaComponent> lib;
                    if (!cache.TryGetValue(label, out lib))
                    {
                        var spec = new FsaSampleSpec { Equilibrium = eq, AtomicXray = false };
                        spec.Chains.Add(FsaSampleChain.FromLabel(label));
                        lib = FsaSampleLibrary.Build(spec);
                        cache[label] = lib;
                    }

                    double sum = 0.0;
                    int hits = 0;
                    foreach (FsaComponent comp in lib)
                    {
                        foreach (FsaLine line in comp.Lines)
                        {
                            if (Math.Abs(line.Energy - (double)e[2]) < 0.05
                                && string.Equals(line.Nuclide, (string)e[1], StringComparison.OrdinalIgnoreCase))
                            {
                                sum += line.Intensity;
                                hits++;
                            }
                        }
                    }

                    string what = string.Format(CultureInfo.InvariantCulture, "  {0} {1} {2} {3:F2} кэВ (линий {4})",
                                                eq ? "ВКЛ " : "ВЫКЛ", label, e[1], e[2], hits);
                    Near(what, (double)e[3] * (double)e[4], sum, 0.003);
                    if (hits > 1)
                    {
                        bad++;
                        Console.WriteLine("  !! линия задвоена: {0}", what);
                    }
                }

                if (eq)
                {
                    // При связке ряд — ОДНА колонка с корнем во имени
                    foreach (string label in new[] { "Th-232", "U-238" })
                    {
                        List<FsaComponent> lib = cache[label];
                        int chains = lib.Count(c => c.Kind == FsaComponentKind.Chain);
                        Same("  " + label + " ВКЛ: колонок ряда", "1", chains.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        // ---------------------------------------------------------------- 6
        static void Section6Lines()
        {
            Console.WriteLine("=== 6. сильнейшие линии одиночных нуклидов (DecayLines) ===");
            object[][] known =
            {
                new object[] { "137CS", 661.657, 85.1 }, new object[] { "60CO", 1173.228, 99.85 },
                new object[] { "60CO", 1332.492, 99.98 }, new object[] { "241AM", 59.5409, 35.9 },
                new object[] { "40K", 1460.82, 10.34 },   // DDEP 2025 (AMBER132, выбор Amber)
                new object[] { "176LU", 306.78, 93.6 }, new object[] { "176LU", 201.83, 78.0 },
                new object[] { "152EU", 121.7817, 28.41 }, new object[] { "152EU", 344.2785, 26.59 },
                new object[] { "133BA", 356.0129, 62.05 }, new object[] { "131I", 364.489, 81.5 },
                new object[] { "22NA", 1274.537, 99.94 }, new object[] { "57CO", 122.06065, 85.6 },
                new object[] { "109CD", 88.0336, 3.644 }, new object[] { "54MN", 834.848, 99.976 },
                new object[] { "88Y", 898.042, 93.7 }, new object[] { "88Y", 1836.063, 99.2 },
                new object[] { "134CS", 604.721, 97.62 }, new object[] { "134CS", 795.864, 85.46 },
                new object[] { "65ZN", 1115.539, 50.04 }, new object[] { "99TCm", 140.511, 89.0 },
                new object[] { "226RA", 186.211, 3.64 }, new object[] { "214BI", 609.321, 45.44 },
                new object[] { "208TL", 2614.511, 99.754 }, new object[] { "235U", 185.715, 57.0 },
            };
            foreach (object[] k in known)
            {
                double got = 0.0;
                foreach (double[] line in FsaSampleLibrary.DecayLines((string)k[0], new FsaSampleLibrary.Report()))
                {
                    if (Math.Abs(line[0] - (double)k[1]) < 0.05) got += line[1];
                }

                Near(string.Format(CultureInfo.InvariantCulture, "  {0} {1:F3} кэВ", k[0], k[1]), (double)k[2], got, 0.005);
            }

            // Аннигиляция β⁺: 2·ΣI(β⁺) зажатая (AMBER69)
            double[] na = FsaSampleLibrary.AnnihilationLine("22NA");
            Near("  22NA аннигиляция 511 (2 × 89.9)", 179.8, na == null ? 0.0 : na[1], 0.002);
            double[] f18 = FsaSampleLibrary.AnnihilationLine("18F");
            Near("  18F аннигиляция 511 (2 × 96.73)", 193.46, f18 == null ? 0.0 : f18[1], 0.002);
            Same("  137CS аннигиляции нет", "null", FsaSampleLibrary.AnnihilationLine("137CS") == null ? "null" : "есть");

            // Изомер, которого decay_chain не знает: ряд Mo-99 без 140.5 (сведение для Amber, не счёт)
            Dictionary<string, double> mo = FsaSampleLibrary.ChainBranches("99MO", new FsaSampleLibrary.Report());
            double mo140 = 0.0;
            foreach (KeyValuePair<string, double> m in mo)
            {
                foreach (double[] line in FsaSampleLibrary.DecayLines(m.Key, new FsaSampleLibrary.Report()))
                {
                    if (Math.Abs(line[0] - 140.511) < 0.05) mo140 += line[1] * m.Value;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  сведение: ряд от 99MO — членов {0} ({1}), выход 140.511 кэВ на распад Mo-99 = {2:G4} % (в природе ≈ 88 % через Tc-99m)",
                              mo.Count, string.Join(", ", mo.Keys), mo140));
        }

        // ---------------------------------------------------------------- 7
        static void Section7DecayLabels(string db)
        {
            Console.WriteLine("=== 7. подписи кодов распада ===");
            foreach (string table in new[] { "decay_chain", "decay_radiations" })
            {
                using (var c = Open(db))
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = table == "decay_chain"
                        ? "select dec_type, count(*), count(distinct nucid) from decay_chain group by dec_type"
                        : "select dec_type, count(*), count(distinct parent_nucid) from decay_radiations where type_a in ('G','X') group by dec_type";
                    using (var r = cmd.ExecuteReader())
                    {
                        var numeric = new List<string>();
                        while (r.Read())
                        {
                            int code;
                            int.TryParse(r.GetString(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
                            string label = table == "decay_chain"
                                ? new Decay { DecayType = code }.DecayTypeString
                                : new DecayRad { DecayType = code }.DecayTypeString;
                            if (label == code.ToString(CultureInfo.InvariantCulture))
                            {
                                numeric.Add(code + " (" + r.GetInt64(1) + " строк, " + r.GetInt64(2) + " родит.)");
                            }
                        }

                        Console.WriteLine("  {0}: числом печатаются коды {1}", table, numeric.Count == 0 ? "— нет" : string.Join(", ", numeric));
                    }
                }
            }
        }

        // ---------------------------------------------------------------- 8
        static void Section8Threads()
        {
            Console.WriteLine("=== 8. потоки: первые обращения к кэшам из 8 потоков ===");
            string[] names = { "137CS", "232TH", "226RA", "238U", "176LU", "40K", "152EU", "60CO" };
            var errors = new List<string>();
            var answers = new Dictionary<string, string>[8];
            Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
            {
                var mine = new Dictionary<string, string>();
                try
                {
                    foreach (string n in names)
                    {
                        string nucid = FsaSampleLibrary.NucidOf(FsaSampleLibrary.PrettyName(n));
                        double t = NucBaseFramework.HalfLifeYearsOf(n);
                        int lines = FsaSampleLibrary.DecayLines(n, new FsaSampleLibrary.Report()).Count;
                        int members = FsaSampleLibrary.ChainBranches(n, new FsaSampleLibrary.Report()).Count;
                        mine[n] = nucid + "|" + t.ToString("R", CultureInfo.InvariantCulture) + "|" + lines + "|" + members;
                    }
                }
                catch (Exception ex)
                {
                    lock (errors) errors.Add(ex.GetType().Name + ": " + ex.Message);
                }

                answers[i] = mine;
            });
            Same("  исключений", "0", errors.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string e in errors.Distinct().Take(5)) Console.WriteLine("    " + e);
            int differ = 0;
            for (int i = 1; i < 8; i++)
            {
                foreach (string n in names)
                {
                    string a, b;
                    answers[0].TryGetValue(n, out a);
                    answers[i].TryGetValue(n, out b);
                    if (a != b) differ++;
                }
            }

            Same("  ответов, разошедшихся между потоками", "0", differ.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  образец: 232TH → {0}", answers[0].ContainsKey("232TH") ? answers[0]["232TH"] : "—");
        }

        // ---------------------------------------------------------------- 9
        static string Edges(List<Decay> list)
        {
            return string.Join(", ", list.Select(d => d.NucName + " " + d.DecayPercent + " [" + d.DecayTypeString + "]"));
        }

        static void Section9Card(string db)
        {
            Console.WriteLine("=== 9. карточка NucBase: дочерние и родители (getNuclude) ===");
            // nucid → ожидаемое множество «дочь:доля» (порядок не важен)
            string[][] daughters =
            {
                new[] { "212BI", "208TL:35.94", "212PO:64.06" },
                new[] { "152EU", "152GD:27.92", "152SM:72.08" },
                new[] { "60CO", "60NI:100" },
                new[] { "238U", "234TH:100" },
                new[] { "58CO", "58FE:100" },
                new[] { "133BA", "133CS:100" },
                new[] { "176LU", "176HF:100" },
                new[] { "234PA", "234U:100" },
                new[] { "234PAm1", "234U:99.84", "234PA:0.16" },
                new[] { "137CS", "137BA:100" },
                new[] { "40K", "40AR:10.72", "40CA:89.28" },
                new[] { "232TH", "228RA:100" },
                new[] { "226RA", "222RN:100" },
                new[] { "214BI", "214PO:99.9790", "210TL:0.0210", "210PB:0.003" },
                new[] { "164TM", "164ER:100" },
            };
            string[][] parents =
            {
                new[] { "208TL", "212BI:35.94", "208HG:100" },
                new[] { "212PO", "212BI:64.06", "216RN:100" },
                new[] { "234PA", "234PAm1:0.16" },
                new[] { "234PAm1", "234TH:100" },
                new[] { "228AC", "228RA:100" },
            };
            foreach (bool parentSide in new[] { false, true })
            {
                foreach (string[] e in parentSide ? parents : daughters)
                {
                    Nuclide nuc = new NucBaseFramework().getNuclude(e[0]);
                    List<Decay> got = nuc == null ? new List<Decay>() : (parentSide ? nuc.Parents : nuc.Daughters);
                    var want = new HashSet<string>(e.Skip(1));
                    var have = new HashSet<string>(got.Select(d =>
                    {
                        double v;
                        return d.NucName + ":" + (double.TryParse(d.DecayPercent, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                            ? v.ToString("G6", CultureInfo.InvariantCulture) : d.DecayPercent);
                    }));
                    var wantNorm = new HashSet<string>(want.Select(w =>
                    {
                        string[] p = w.Split(':');
                        return p[0] + ":" + double.Parse(p[1], CultureInfo.InvariantCulture).ToString("G6", CultureInfo.InvariantCulture);
                    }));
                    bool ok = have.SetEquals(wantNorm);
                    if (!ok) bad++;
                    Console.WriteLine("  {0} {1,-8} {2} {3}{4}", parentSide ? "родители" : "дочерние", e[0], ok ? "ок" : "!!",
                                      Edges(got), ok ? "" : "   (ждали " + string.Join(", ", want) + ")");
                }
            }

            // Перепись: у скольких nucid карточка показывает НЕ то, что голая decay_chain
            int differ = 0, total = 0;
            var examples = new List<string>();
            var raw = new Dictionary<string, int>(StringComparer.Ordinal);
            using (var c = Open(db))
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "select nucid, count(*) from decay_chain group by nucid";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) raw[r.GetString(0)] = (int)r.GetInt64(1);
                }
            }

            foreach (KeyValuePair<string, int> p in raw)
            {
                Nuclide nuc = new NucBaseFramework().getNuclude(p.Key);
                if (nuc == null) continue;
                total++;
                if (nuc.Daughters.Count != p.Value)
                {
                    differ++;
                    if (examples.Count < 8) examples.Add(p.Key + " " + p.Value + "→" + nuc.Daughters.Count);
                }
            }

            Console.WriteLine("  перепись: карточек с дочерними {0}, строк карточки ≠ строк decay_chain у {1} (напр. {2})",
                              total, differ, string.Join("; ", examples));

            // Цена карточки: getNuclude не кэшируется и зовётся на КАЖДЫЙ переход по строке таблицы
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string[] timed = { "212BI", "152EU", "60CO", "208TL", "238U", "137CS", "40K", "234PA", "214BI", "226RA" };
            for (int rep = 0; rep < 5; rep++)
            {
                foreach (string n in timed) new NucBaseFramework().getNuclude(n);
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  цена карточки: {0:F1} мс на getNuclude (среднее по {1})",
                              sw.Elapsed.TotalMilliseconds / (5 * timed.Length), 5 * timed.Length));
            // Прежняя карточка — три голых запроса (для сравнения цены)
            sw.Restart();
            for (int rep = 0; rep < 5; rep++)
            {
                foreach (string n in timed)
                {
                    var b = new SqliteConnectionStringBuilder { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared };
                    using (var c = new SqliteConnection(b.ToString()))
                    {
                        c.Open();
                        foreach (string sql in new[] {
                            "select z, n, ifnull(half_life, '?') from nuclides where nucid = $n and half_life not null",
                            "select daughter_nucid, ifnull(perc, '?'), dec_type from decay_chain where nucid = $n",
                            "select nucid, ifnull(perc, '?'), dec_type from decay_chain where daughter_nucid = $n" })
                        {
                            using (var cmd = c.CreateCommand())
                            {
                                cmd.CommandText = sql;
                                cmd.Parameters.AddWithValue("$n", n);
                                using (var r = cmd.ExecuteReader()) { while (r.Read()) { } }
                            }
                        }
                    }
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  цена прежних трёх запросов: {0:F1} мс", sw.Elapsed.TotalMilliseconds / (5 * timed.Length)));

            // То же В ОКНЕ: настоящая форма NucBase без показа, поиск — её DoSearch (отражением)
            var form = new NucBase.NucBase();
            Type t = typeof(NucBase.NucBase);
            var box = (System.Windows.Forms.TextBox)t.GetField("IsotopeTextBox", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            var grid = (System.Windows.Forms.DataGridView)t.GetField("DaughtersDataGridView", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            var pgrid = (System.Windows.Forms.DataGridView)t.GetField("ParentsDataGridView", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            var hl = (System.Windows.Forms.Label)t.GetField("IsotopeHLLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
            MethodInfo search = t.GetMethod("DoSearch", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (string q in new[] { "Bi-212", "Eu-152", "Co-60", "Tl-208", "Tb-144m" })
            {
                box.Text = q;
                search.Invoke(form, null);
                var rows = new List<string>();
                foreach (System.Windows.Forms.DataGridViewRow r in grid.Rows)
                {
                    if (!r.IsNewRow) rows.Add(r.Cells[0].Value + " " + r.Cells[2].Value + " [" + r.Cells[1].Value + "]");
                }

                var prow = new List<string>();
                foreach (System.Windows.Forms.DataGridViewRow r in pgrid.Rows)
                {
                    if (!r.IsNewRow) prow.Add(r.Cells[0].Value + " " + r.Cells[2].Value + " [" + r.Cells[1].Value + "]");
                }

                Console.WriteLine("  окно «{0}»: T½ «{1}»; дочерние: {2}; родители: {3}", q, hl.Text,
                                  string.Join(", ", rows), string.Join(", ", prow));
            }
        }

        // ---------------------------------------------------------------- 10
        static void Section10Starter()
        {
            Console.WriteLine("=== 10. заготовка библиотеки первого запуска (NuclideDefinitionManager) ===");
            // Менеджер новым объектом (одиночку не трогаем), заготовка — его закрытым методом.
            var manager = (NuclideDefinitionManager)Activator.CreateInstance(typeof(NuclideDefinitionManager), true);
            manager.NuclideDefinitionFile = new NuclideDefinitionFile();
            typeof(NuclideDefinitionManager).GetMethod("InitializeNuclideDefinitionFile", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(manager, null);
            foreach (NuclideDefinition def in manager.NuclideDefinitions)
            {
                string nucid = FsaSampleLibrary.NucidOf(def.NuclideName);
                double nearest = double.NaN;
                foreach (double[] line in FsaSampleLibrary.DecayLines(nucid, new FsaSampleLibrary.Report()))
                {
                    if (line[1] >= 5.0 && (double.IsNaN(nearest) || Math.Abs(line[0] - def.Energy) < Math.Abs(nearest - def.Energy)))
                    {
                        nearest = line[0];
                    }
                }

                Near("  " + def.Name + " " + def.Energy.ToString("R", CultureInfo.InvariantCulture) + " кэВ: ближайшая линия базы ≥ 5 %",
                     nearest, def.Energy, 1e-5);
                Near("  " + def.Name + " период, лет", NucBaseFramework.HalfLifeYearsOf(nucid), def.HalfLife, 1e-4);
            }
        }

        // ---------------------------------------------------------------- общее
        static SqliteConnection Open(string path)
        {
            var b = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly };
            var c = new SqliteConnection(b.ToString());
            c.Open();
            return c;
        }

        static void Near(string what, double expected, double got, double rel)
        {
            bool ok = expected == 0.0 ? Math.Abs(got) < 1e-15 : Math.Abs(got / expected - 1.0) <= rel;
            if (!ok) bad++;
            Console.WriteLine("{0,-58} {1} {2} (ждали {3})", what, ok ? "ок" : "!!",
                              got.ToString("G6", CultureInfo.InvariantCulture), expected.ToString("G6", CultureInfo.InvariantCulture));
        }

        static void Same(string what, string expected, string got)
        {
            bool ok = string.Equals(expected, got, StringComparison.Ordinal);
            if (!ok) bad++;
            Console.WriteLine("{0,-58} {1} «{2}»{3}", what, ok ? "ок" : "!!", got, ok ? "" : " (ждали «" + expected + "»)");
        }
    }
}
