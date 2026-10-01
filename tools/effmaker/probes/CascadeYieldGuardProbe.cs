using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;

// П193 (`AMBER144`, 01.10.2026): СТОРОЖ ВЫХОДОВ ЛИНИЙ В ПОСТАВКЕ ПАР ПРОТИВ БИБЛИОТЕКИ.
//
// Зачем. Перечень пар и выходы линий счёт совпадений берёт из поставки SandiaDecay
// (`v_gamma_coincidence`), а образы — из библиотеки (`decay_radiations`). Поставка
// делит распад на долгоживущем уровне дочки: у Bi-207 выход 569.70 в ней 15.57 %
// (прямая часть) против 97.75 % библиотеки, пара 1063.66 + 569.70 лежит под `Pb207m`.
// Расхождение выхода линии ВДВОЕ и больше — признак такого раскола (или иной дыры
// поставки), и сторож называет нуклид.
//
// Что сверяется. По каждому родителю поставки пар (isomer = 0; `--all`) или только по
// нуклидам корпуса (умолчание — `manifest.csv`, ряды развёрнуты по `decay_chain`):
//   сырое   — выход линии в поставке (`v_gamma_coincidence_line`, isomer = 0);
//   счёт    — выход, каким его видит счёт пар (`FsaCascadeSummer.LineYields`: поставка +
//             присоединённые изомеры дочек, `AttachDaughterIsomers`);
// против выхода линии библиотеки (то же правило уровня, `DecayParentRule.LevelClause`).
// Линия берётся, если в библиотеке у неё ≥ 1 % (`--min=`). Флаг — отношение ≥ 2 в любую
// сторону.
//
// Положительный контроль: сырой выход Bi-207 569.70 ОБЯЗАН попасть под флаг (иначе
// сторож слеп); код 3, если не попал.
//
// Известная перепись (`Known`, решение Amber 01.10.2026) — расхождения не изомерного класса у
// Th-227, Ac-228, Bi-215: печатаются списком и кода не поднимают.
//
// Плечо контроля `--control`: «счёт» = сырая поставка без присоединения изомеров дочек — обязан
// дать код 1 (Bi-207 569.70 в перепись не входит).
//
//   CascadeYieldGuardProbe.exe [--all] [--control] [--min=1] [--manifest=<путь>]
// Коды: 0 — у нуклидов корпуса счёт без флагов сверх переписи и контроль сработал; 1 — НОВЫЙ
// флаг у нуклида корпуса (назван); 3 — положительный контроль не сработал; 2 — нечем сверять.
public static class CascadeYieldGuardProbe
{
    const double Factor = 2.0;
    const double MatchKev = 0.6;

    static string Db()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
    }

    static SqliteConnection Open()
    {
        var c = new SqliteConnection("Data Source=" + Db() + ";Mode=ReadOnly;");
        c.Open();
        return c;
    }

    static List<double[]> Library(SqliteConnection c, string nucid)
    {
        var rows = new List<double[]>();
        using (SqliteCommand cmd = c.CreateCommand())
        {
            cmd.CommandText = "select energy_num, intensity_num from decay_radiations where parent_nucid = $n"
                              + " and type_a = 'G' and intensity_num > 0" + DecayParentRule.LevelClause;
            cmd.Parameters.AddWithValue("$n", nucid);
            using (SqliteDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    if (!r.IsDBNull(0) && !r.IsDBNull(1))
                    {
                        rows.Add(new[] { r.GetDouble(0), r.GetDouble(1) });
                    }
                }
            }
        }

        return rows;
    }

    static Dictionary<double, double> Raw(SqliteConnection c, string nucid)
    {
        var t = new Dictionary<double, double>();
        using (SqliteCommand cmd = c.CreateCommand())
        {
            cmd.CommandText = "select energy_kev, intensity_pct from v_gamma_coincidence_line where nucid = $n and isomer = 0";
            cmd.Parameters.AddWithValue("$n", nucid);
            using (SqliteDataReader r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    t[r.GetDouble(0)] = r.GetDouble(1);
                }
            }
        }

        return t;
    }

    /// <summary>Флаги нуклида: строки «E: поставка/счёт X % против библиотеки Y %».</summary>
    static List<string> Flags(Dictionary<double, double> yields, List<double[]> library, double minPct)
    {
        // ⚠ Сверяются СУММЫ в окне ±MatchKev с обеих сторон, а не ближайшая линия:
        // у Eu-152 443.96 (0.33 %) и 443.965 (2.83 %) переставлены двумя оценками на
        // 0.01 кэВ, у Pa-234 у 880.5 две линии библиотеки — ближайшая давала ложный
        // флаг ×8.65 и ×0.28 (первая редакция сторожа, П193).
        var flags = new List<string>();
        var seen = new HashSet<double>();
        foreach (double[] lib in library)
        {
            if (lib[1] < minPct || seen.Contains(lib[0]))
            {
                continue;
            }

            double libSum = 0.0;
            foreach (double[] other in library)
            {
                if (Math.Abs(other[0] - lib[0]) < MatchKev)
                {
                    libSum += other[1];
                    seen.Add(other[0]);
                }
            }

            double got = 0.0;
            bool any = false;
            foreach (KeyValuePair<double, double> y in yields)
            {
                if (Math.Abs(y.Key - lib[0]) < MatchKev)
                {
                    got += y.Value;
                    any = true;
                }
            }

            // Линии нет в поставке пар вовсе — не дело этого сторожа (нуклид без
            // пар у этой линии законен: одиночная гамма).
            if (!any || !(got > 0.0))
            {
                continue;
            }

            double ratio = libSum / got;
            if (ratio >= Factor || ratio <= 1.0 / Factor)
            {
                flags.Add(string.Format(CultureInfo.InvariantCulture, "{0:F2} кэВ: {1:F3} % против библиотеки {2:F3} % (×{3:F2})",
                                        lib[0], got, libSum, ratio));
            }
        }

        return flags;
    }

    static bool controlArm;

    /// <summary>
    /// ИЗВЕСТНАЯ ПЕРЕПИСЬ — расхождения выходов поставки пар и библиотеки у нуклидов корпуса,
    /// НЕ изомерного класса (решения Amber 01.10.2026 вопросником, дословно: по AMBER144 —
    /// «Закрыть, остаток в AMBER155 (Рекомендую)»; по расхождению выходов — «Факт в журнал,
    /// сторож — перепись (Рекомендую)»). Печатаются списком и кода не поднимают; НОВОЕ
    /// расхождение сверх списка — код 1. Нуклид, линия кэВ, поставка %, библиотека % (01.10.2026).
    /// </summary>
    static readonly object[][] Known =
    {
        new object[] { "215BI", 271.10, 55.000, 4.031 },
        new object[] { "215BI", 564.40, 13.000, 1.390 },
        new object[] { "227TH", 235.96, 35.600, 12.900 },
        new object[] { "228AC", 674.75, 0.218, 2.100 },
    };

    static bool IsKnown(string nucid, string flag)
    {
        int space = flag.IndexOf(' ');
        double e;
        if (space <= 0 || !double.TryParse(flag.Substring(0, space), NumberStyles.Float, CultureInfo.InvariantCulture, out e))
        {
            return false;
        }

        foreach (object[] k in Known)
        {
            if (string.Equals((string)k[0], nucid, StringComparison.OrdinalIgnoreCase)
                && Math.Abs((double)k[1] - e) < MatchKev)
            {
                return true;
            }
        }

        return false;
    }

    static readonly Dictionary<string, string> Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Метки рядов манифеста → nucid корня. Данные корпуса, не FSA.
        { "Th-232", "232TH" }, { "Th-228", "228TH" }, { "Ra-226", "226RA" }, { "Rn-222", "222RN" },
        { "U-238", "238U" }, { "U-238u", "238U" }, { "U-235", "235U" },
    };

    static HashSet<string> CorpusNuclides(SqliteConnection c, string manifest)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        string[] lines = File.ReadAllLines(manifest);
        string[] head = lines[0].Split(',');
        int ci = Array.IndexOf(head, "chains"), ni = Array.IndexOf(head, "nuclides");
        foreach (string line in lines.Skip(1))
        {
            string[] cells = line.Split(',');
            if (cells.Length <= Math.Max(ci, ni))
            {
                continue;
            }

            foreach (string n in cells[ni].Split(';'))
            {
                if (n.Trim().Length > 0)
                {
                    set.Add(n.Trim());
                }
            }

            foreach (string ch in cells[ci].Split(';'))
            {
                string root;
                if (Roots.TryGetValue(ch.Trim(), out root) && set.Add(root))
                {
                    queue.Enqueue(root);
                }
            }
        }

        while (queue.Count > 0)
        {
            string p = queue.Dequeue();
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select distinct daughter_nucid from" + DecayParentRule.ChainTable + " d where nucid = $n"
                                  + DecayParentRule.ChainLevelClause;
                cmd.Parameters.AddWithValue("$n", p);
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string d = r.IsDBNull(0) ? null : r.GetString(0);
                        if (!string.IsNullOrEmpty(d) && set.Add(d))
                        {
                            queue.Enqueue(d);
                        }
                    }
                }
            }
        }

        return set;
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();
        bool all = args.Contains("--all");
        controlArm = args.Contains("--control");
        double minPct = 1.0;
        string manifest = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                                        @"..\..\..\CORPUS\corpus\manifest.csv"));
        foreach (string a in args)
        {
            if (a.StartsWith("--min=", StringComparison.Ordinal))
                minPct = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--manifest=", StringComparison.Ordinal))
                manifest = a.Substring(11);
        }

        if (!File.Exists(Db()) || !File.Exists(manifest))
        {
            Console.WriteLine("⛔ нечем сверять: нет {0}", File.Exists(Db()) ? manifest : Db());
            return 2;
        }

        var sw = Stopwatch.StartNew();
        using (SqliteConnection c = Open())
        {
            HashSet<string> corpus = CorpusNuclides(c, manifest);
            var parents = new List<string>();
            using (SqliteCommand cmd = c.CreateCommand())
            {
                cmd.CommandText = "select distinct nucid from v_gamma_coincidence where isomer = 0 order by nucid";
                using (SqliteDataReader r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        string n = r.GetString(0);
                        if (all || corpus.Contains(n))
                        {
                            parents.Add(n);
                        }
                    }
                }
            }

            Console.WriteLine("нуклидов корпуса (с рядами): {0}; к сверке родителей поставки пар: {1}{2}; порог библиотеки {3} %, флаг ×{4}",
                              corpus.Count, parents.Count, all ? " (вся поставка)" : "", minPct, Factor);

            if (controlArm)
            {
                Console.WriteLine("⚠ ПЛЕЧО КОНТРОЛЯ (--control): «счёт» = сырая поставка без присоединения изомеров дочек — обязан дать код 1 (Bi-207 569.70)");
            }

            int rawFlagged = 0, effFlagged = 0, corpusEff = 0, corpusKnown = 0, attachedCount = 0;
            bool control = false;
            var census = new List<string>();
            foreach (string p in parents)
            {
                List<double[]> lib = Library(c, p);
                List<string> rawFlags = Flags(Raw(c, p), lib, minPct);
                List<string> effFlags = controlArm ? new List<string>(rawFlags)
                                                   : Flags(FsaCascadeSummer.LineYields(p), lib, minPct);
                // (решение Amber 01.10.2026) известная перепись — отдельно, кодом 0
                if (corpus.Contains(p))
                {
                    foreach (string f in effFlags.ToList())
                    {
                        if (IsKnown(p, f))
                        {
                            census.Add(p + "  " + f);
                            effFlags.Remove(f);
                            corpusKnown++;
                        }
                    }
                }
                List<string> attached = FsaCascadeSummer.IsomerAttachments(p);
                bool inCorpus = corpus.Contains(p);
                if (rawFlags.Count > 0) rawFlagged++;
                if (effFlags.Count > 0) effFlagged++;
                if (attached.Count > 0) attachedCount++;
                if (string.Equals(p, "207BI", StringComparison.OrdinalIgnoreCase)
                    && rawFlags.Any(f => f.StartsWith("569.70", StringComparison.Ordinal)))
                {
                    control = true;
                }

                if (rawFlags.Count == 0 && effFlags.Count == 0 && attached.Count == 0)
                {
                    continue;
                }

                if (inCorpus && effFlags.Count > 0)
                {
                    corpusEff++;
                }

                Console.WriteLine("{0}{1}", p, inCorpus ? "  [корпус]" : "");
                foreach (string f in rawFlags) Console.WriteLine("   поставка: " + f);
                foreach (string f in effFlags) Console.WriteLine("   СЧЁТ:     " + f);
                foreach (string f in attached) Console.WriteLine("   присоединён изомер дочки " + f);
            }

            Console.WriteLine();
            Console.WriteLine("ИЗВЕСТНАЯ ПЕРЕПИСЬ (решение Amber 01.10.2026: «Факт в журнал, сторож — перепись»): не изомерный класс, код не поднимает — {0} из {1} записей:",
                              corpusKnown, Known.Length);
            foreach (string s in census) Console.WriteLine("   " + s);
            Console.WriteLine();
            Console.WriteLine("итог за {0:F1} с: с флагом в поставке {1}, в счёте сверх переписи {2} (из них корпуса — НОВЫХ {3}); присоединены изомеры дочек у {4}",
                              sw.Elapsed.TotalSeconds, rawFlagged, effFlagged, corpusEff, attachedCount);
            Console.WriteLine("положительный контроль (сырой Bi-207 569.70 под флагом): {0}", control ? "СРАБОТАЛ" : "НЕ СРАБОТАЛ");
            if (!control)
            {
                return 3;
            }

            return corpusEff == 0 ? 0 : 1;
        }
    }
}
