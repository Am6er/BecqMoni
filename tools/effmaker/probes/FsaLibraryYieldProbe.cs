using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FsaLibraryYieldProbe
{
    /// <summary>
    /// ВЫХОДЫ ЛИНИЙ ОБРАЗА ИЗ БАЗЫ — построчно, с двумя независимыми мерками
    /// (П130, 22.09.2026, строки `AMBER68` и `AMBER69`):
    ///
    ///   * `L` — L-серия: проба сама читает `decay_radiations` и делит строки
    ///     на три вида ПО СТРУКТУРЕ имени `type_c` — сводная `L` (одна буква),
    ///     итог подоболочки (`L1`/`L2`/`L3`, буква и цифра) и линия
    ///     (`L1M2`, `L3N1`… — длиннее двух), — печатает суммы каждого вида и
    ///     сверяет их с тем, что реально легло в образ
    ///     (<see cref="FsaSampleLibrary.DecayLines"/>) в полосе L-рентгена.
    ///     `AMBER68`: итоги подоболочек и их же линии в образе складывались.
    ///
    ///   * `BP` — аннигиляционная линия: сырой `2·ΣI(β⁺)` (своё чтение базы,
    ///     тот же зажим уровня `DecayParentRule.LevelClause`), зажатый выход
    ///     сумматора совпадений (<see cref="CascadeAtomicData.AnnihilationQuanta"/>,
    ///     ~~`S153`~~) и то, что библиотека положила на
    ///     <see cref="FsaAnalyzer.AnnihilationKev"/>. `AMBER69`: библиотека
    ///     клала сырой, сумматор — зажатый, две редакции одной величины.
    ///
    ///     fsalibraryyieldprobe --sample=229TH,20NA,22NA [--band=9:20]
    ///                          [--out=lines.tsv] [--check]
    ///
    /// `--out=` пишет ВСЕ линии всех названных нуклидов одной таблицей — для
    /// побитового сравнения ДО/ПОСЛЕ правки. `--check` — код 1, если хоть у
    /// одного нуклида образ считает L-серию дважды либо выход 511 в образе
    /// разошёлся с сумматором; без ключа код 0 всегда (печать, а не сторож).
    /// ⚠ Имён нуклидов в пробе нет: кого печатать — говорят ключи, что
    /// считать — база. Рядом с exe нужны `nucdb.sqlite` и `schemedb.sqlite`
    /// (кладёт `build_all.ps1`).
    /// </summary>
    static class Program
    {
        static int bad;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            var samples = new List<string>();
            double bandLo = 9.0, bandHi = 20.0;
            string outPath = null;
            bool check = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--sample=", StringComparison.Ordinal))
                {
                    samples.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (a.StartsWith("--band=", StringComparison.Ordinal))
                {
                    string[] parts = a.Substring(7).Split(':');
                    if (parts.Length != 2
                        || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out bandLo)
                        || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out bandHi))
                    {
                        Console.Error.WriteLine("полосу давать как --band=низ:верх, кэВ");
                        return 2;
                    }
                }
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a == "--check") check = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (samples.Count == 0)
            {
                Console.Error.WriteLine("нечего печатать: дайте --sample=<нуклиды через запятую>");
                return 2;
            }

            var dump = new StringBuilder();
            dump.Append("nucid\tenergy\tintensity\tannihilation\n");

            foreach (string raw in samples)
            {
                // ⚠ Имя, уже похожее на `nucid`, берётся КАК ДАНО: буква
                // изомера в базе строчная (`128LAm`, `50MNm`), и приведение к
                // верхнему регистру теряло такой набор целиком — база ищет по
                // `parent_nucid` точным совпадением. Разбирается только запись
                // «Na-22» (через дефис), для которой и заведён `NucidOf`.
                string nucid = FsaSampleLibrary.NucidOf(raw);
                if (nucid.Length == 0) nucid = raw.Trim();

                Console.WriteLine();
                Console.WriteLine("=== {0} ===", nucid);

                var report = new FsaSampleLibrary.Report();
                List<double[]> lines = FsaSampleLibrary.DecayLines(nucid, report);
                if (report.Notes.Count > 0)
                {
                    Console.WriteLine("  примечания базы: {0}", string.Join("; ", report.Notes));
                }

                double inBand = 0.0, total = 0.0;
                foreach (double[] line in lines)
                {
                    double annihilation = line.Length > 2 ? line[2] : 0.0;
                    dump.AppendFormat(CultureInfo.InvariantCulture, "{0}\t{1:R}\t{2:R}\t{3:R}\n",
                                      nucid, line[0], line[1], annihilation);
                    total += line[1];
                    if (line[0] >= bandLo && line[0] <= bandHi) inBand += line[1];
                }

                Console.WriteLine("  образ: линий {0}, Σвыход {1} %, в полосе {2}…{3} кэВ {4} %",
                                  lines.Count.ToString(CultureInfo.InvariantCulture), F(total, "F3"),
                                  F(bandLo, "F1"), F(bandHi, "F1"), F(inBand, "F3"));

                // --- AMBER68: L-серия своим чтением базы -------------------
                double lLumped, lShell, lLines;
                int shellNames, lineNames;
                ReadL(nucid, out lLumped, out lShell, out lLines, out shellNames, out lineNames);
                Console.WriteLine("  L\tсводная L {0} %\tитоги L1/L2/L3 {1} % ({2} имён)\tлинии LxMy {3} % ({4} имён)\tитоги+линии {5} %",
                                  F(lLumped, "F3"), F(lShell, "F3"),
                                  shellNames.ToString(CultureInfo.InvariantCulture), F(lLines, "F3"),
                                  lineNames.ToString(CultureInfo.InvariantCulture), F(lShell + lLines, "F3"));

                // Что из этого реально легло в образ: сумма выходов линий
                // образа в полосе L (от самой низкой до самой высокой энергии
                // строк L этого нуклида). Полоса берётся У БАЗЫ, а не ключом.
                double lLo, lHi;
                LBand(nucid, out lLo, out lHi);
                double inL = 0.0;
                if (lHi > 0.0)
                {
                    foreach (double[] line in lines)
                    {
                        if (line[0] >= lLo - 1.0E-9 && line[0] <= lHi + 1.0E-9) inL += line[1];
                    }

                    Console.WriteLine("  L\tполоса базы {0}…{1} кэВ; в образе {2} %",
                                      F(lLo, "F3"), F(lHi, "F3"), F(inL, "F3"));
                    bool doubled = lineNames > 0 && shellNames > 0
                                   && inL > lLines + 0.5 * Math.Min(lShell, lLines);
                    Console.WriteLine("  L\tдвойной счёт подоболочек: {0}", doubled ? "ЕСТЬ" : "нет");
                    if (doubled) bad++;
                }

                // --- AMBER69: выход аннигиляционной линии ------------------
                double rawBetaPlus = SumBetaPlus(nucid);
                if (rawBetaPlus > 0.0)
                {
                    CascadeAtomicData atomic = CascadeAtomicData.Of(nucid);
                    double summer = atomic != null ? 100.0 * atomic.AnnihilationQuanta : double.NaN;
                    // ⚠ Потолок 200 % (100 % распадов × два кванта) — часть
                    // правила зажима, а не отдельное соглашение: у сумматора
                    // есть ранний выход по неопределённому дочернему, после
                    // которого его число остаётся СЫРЫМ, и сличать образ надо
                    // с правилом целиком, иначе проба краснеет на верной
                    // правке. Такие нуклиды проба называет отдельно.
                    double expect = double.IsNaN(summer) ? summer : Math.Min(summer, 200.0);
                    if (!double.IsNaN(summer) && summer > 200.0)
                    {
                        Console.WriteLine("  BP\t⚠ сумматор отдал СЫРОЕ {0} % (ранний выход по дочернему): "
                                          + "зажимает только потолок", F(summer, "F4"));
                    }

                    double library = double.NaN, libraryAnnihilation = double.NaN;
                    foreach (double[] line in lines)
                    {
                        if (Math.Abs(line[0] - FsaAnalyzer.AnnihilationKev) < 0.05)
                        {
                            library = line[1];
                            libraryAnnihilation = line.Length > 2 ? line[2] : 0.0;
                            break;
                        }
                    }

                    bool same = !double.IsNaN(library) && !double.IsNaN(expect)
                                && Math.Abs(libraryAnnihilation - expect) <= 1.0E-9 * Math.Max(1.0, expect);
                    Console.WriteLine("  BP\tΣI(β⁺) базы {0} %\tсырой 2·ΣI(β⁺) {1} %\tожидание (зажим+потолок) {2} %\tобраз: линия {3} %, из них аннигиляция {4} %\t{5}",
                                      F(rawBetaPlus, "F4"), F(2.0 * rawBetaPlus, "F4"), F(expect, "F4"),
                                      double.IsNaN(library) ? "нет" : F(library, "F4"),
                                      double.IsNaN(libraryAnnihilation) ? "нет" : F(libraryAnnihilation, "F4"),
                                      same ? "OK" : "DIFF");
                    if (!same) bad++;
                }
                else
                {
                    Console.WriteLine("  BP\tстрок β⁺ у нуклида нет — аннигиляционной линии быть не должно");
                }
            }

            if (outPath != null)
            {
                File.WriteAllText(outPath, dump.ToString(), new UTF8Encoding(false));
                Console.WriteLine();
                Console.WriteLine("таблица линий: {0}", Path.GetFullPath(outPath));
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return check && bad > 0 ? 1 : 0;
        }

        // ------------------------------------------------------------------

        static string DatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        }

        static SqliteConnection Open()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath(),
                Mode = SqliteOpenMode.ReadOnly
            }.ToString());
            connection.Open();
            return connection;
        }

        /// <summary>Суммы трёх видов строк L-серии и число РАЗНЫХ имён у двух последних.</summary>
        static void ReadL(string nucid, out double lumped, out double shell, out double lines,
                          out int shellNames, out int lineNames)
        {
            lumped = shell = lines = 0.0;
            var shellSet = new HashSet<string>(StringComparer.Ordinal);
            var lineSet = new HashSet<string>(StringComparer.Ordinal);
            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select trim(type_c), intensity_num from decay_radiations"
                    + " where parent_nucid = $n and type_a = 'X' and intensity_num > 0"
                    + " and energy_num not null and trim(type_c) like 'L%'"
                    + DecayParentRule.LevelClause;
                command.Parameters.AddWithValue("$n", nucid);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string series = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        double intensity = reader.IsDBNull(1) ? 0.0 : reader.GetDouble(1);
                        if (series.Length == 1) { lumped += intensity; }
                        else if (series.Length == 2 && series[1] >= '0' && series[1] <= '9')
                        {
                            shell += intensity;
                            shellSet.Add(series);
                        }
                        else
                        {
                            lines += intensity;
                            lineSet.Add(series);
                        }
                    }
                }
            }

            shellNames = shellSet.Count;
            lineNames = lineSet.Count;
        }

        /// <summary>Энергетическая полоса строк L-серии нуклида по базе.</summary>
        static void LBand(string nucid, out double lo, out double hi)
        {
            lo = 0.0;
            hi = 0.0;
            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select min(cast(energy_num as float)), max(cast(energy_num as float))"
                    + " from decay_radiations where parent_nucid = $n and type_a = 'X'"
                    + " and intensity_num > 0 and energy_num not null and trim(type_c) like 'L%'"
                    + DecayParentRule.LevelClause;
                command.Parameters.AddWithValue("$n", nucid);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read() && !reader.IsDBNull(0))
                    {
                        lo = reader.GetDouble(0);
                        hi = reader.GetDouble(1);
                    }
                }
            }
        }

        static double SumBetaPlus(string nucid)
        {
            double sum = 0.0;
            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select intensity_num from decay_radiations"
                    + " where parent_nucid = $n and type_a = 'B+' and intensity_num > 0"
                    + DecayParentRule.LevelClause;
                command.Parameters.AddWithValue("$n", nucid);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(0)) sum += reader.GetDouble(0);
                    }
                }
            }

            return sum;
        }

        static string F(double value, string format)
        {
            return double.IsNaN(value) ? "—" : value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
