using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FsaLibraryBetaChainProbe
{
    /// <summary>
    /// Печать библиотеки FSA из баз (<see cref="FsaSampleLibrary.Build"/>) по
    /// объявленному составу — линия за линией, с выходом, — и две независимые
    /// мерки к ней (П124, 22.09.2026, строки `AMBER54` и `AMBER55`):
    ///
    ///   * `BP` — аннигиляционная линия β⁺-излучателя: сумма строк `B+` базы
    ///     (`decay_radiations`, тот же зажим уровня `DecayParentRule.LevelClause`),
    ///     ожидаемый выход `2·ΣI(β⁺)` и что библиотека реально положила на
    ///     511.00 кэВ в образ нуклида (`AMBER54`);
    ///   * `EQ` — связка равновесия ряда: для каждого члена подряда от корня
    ///     доля ветвления (`FsaSampleLibrary.ChainBranches`), периоды корня и
    ///     члена из `nuclides.half_life_sec`, множитель переходного равновесия
    ///     по Бейтману — произведение `λ_j/(λ_j − λ_корня)` по пути от корня к
    ///     члену, посчитанное ЗДЕСЬ своим обходом `decay_chain`, — и вес, с
    ///     которым линии члена легли в образ корня (`I_lib / I_distr`). Столбец
    ///     `verdict`: `OK` — вес библиотеки равен `BR·f` (1e-9 отн.), `DIFF` —
    ///     нет (`AMBER55`).
    ///
    ///     fsalibrarybetachainprobe [--sample=22NA,68GA,18F,137CS] [--chain=Ra-226,Pb-214,Th-232]
    ///                              [--floor=20] [--max=3200] [--no-equilibrium]
    ///                              [--out=lib.tsv] [--check]
    ///
    /// Каждый нуклид и каждый ряд собирается ОТДЕЛЬНЫМ составом — так печать
    /// одного не зависит от соседей. `--out=` пишет строки `LIB` в файл — для
    /// побитового сравнения ДО/ПОСЛЕ правки. `--check` — код возврата 1, если
    /// хоть одна строка `EQ` дала `DIFF` или хоть у одного β⁺-излучателя
    /// линии 511 в образе нет при `ΣI(β⁺) > 0`; без ключа код 0 всегда —
    /// печать, а не сторож. ⚠ Имён нуклидов в самой пробе нет: что печатать —
    /// говорят ключи, что считать — база.
    ///
    /// Рядом с exe нужна `nucdb.sqlite` (кладёт `build_all.ps1`).
    /// </summary>
    static class Program
    {
        const double AnnihilationKev = 511.0;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            var samples = new List<string>();
            var chains = new List<string>();
            double floor = 20.0, max = 3200.0;
            bool equilibrium = true, check = false;
            string outPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--sample=", StringComparison.Ordinal))
                {
                    samples.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (a.StartsWith("--chain=", StringComparison.Ordinal))
                {
                    chains.AddRange(a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (a.StartsWith("--floor=", StringComparison.Ordinal))
                {
                    floor = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--max=", StringComparison.Ordinal))
                {
                    max = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                }
                else if (a == "--no-equilibrium") equilibrium = false;
                else if (a == "--check") check = true;
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (samples.Count == 0 && chains.Count == 0)
            {
                Console.Error.WriteLine("нечего печатать: дайте --sample= и/или --chain=");
                return 2;
            }

            var lib = new StringBuilder();
            int bad = 0;

            foreach (string raw in samples)
            {
                string nucid = FsaSampleLibrary.NucidOf(raw);
                if (nucid.Length == 0)
                {
                    nucid = raw.Trim().ToUpperInvariant();
                }

                var spec = new FsaSampleSpec();
                spec.Nuclides.Add(nucid);
                spec.MinEnergyKev = floor;
                spec.MaxEnergyKev = max;
                spec.Equilibrium = equilibrium;
                spec.AtomicXray = false;
                FsaSampleLibrary.Report report;
                List<FsaComponent> library = FsaSampleLibrary.Build(spec, out report);
                Dump(raw, library, report, lib);

                double betaPlus = SumBetaPlus(nucid);
                double at511 = LineAt(library, FsaSampleLibrary.PrettyName(nucid), AnnihilationKev);
                Console.WriteLine("BP\t{0}\t{1}\tsumB+={2}\texpect511={3}\tlib511={4}\t{5}",
                                  raw, nucid, F(betaPlus), F(2.0 * betaPlus),
                                  double.IsNaN(at511) ? "none" : F(at511),
                                  betaPlus > 0.0 && (double.IsNaN(at511) || Math.Abs(at511 - 2.0 * betaPlus) > 1e-9 * Math.Max(1.0, 2.0 * betaPlus))
                                      ? "DIFF" : "OK");
                if (betaPlus > 0.0 && (double.IsNaN(at511) || Math.Abs(at511 - 2.0 * betaPlus) > 1e-9 * Math.Max(1.0, 2.0 * betaPlus)))
                {
                    bad++;
                }
            }

            foreach (string label in chains)
            {
                FsaSampleChain chain = FsaSampleChain.FromLabel(label);
                if (chain == null)
                {
                    Console.WriteLine("NOTE\t{0}\tметка ряда не разобрана (правило T259)", label);
                    bad++;
                    continue;
                }

                var spec = new FsaSampleSpec();
                spec.Chains.Add(chain);
                spec.MinEnergyKev = floor;
                spec.MaxEnergyKev = max;
                spec.Equilibrium = equilibrium;
                spec.AtomicXray = false;
                FsaSampleLibrary.Report report;
                List<FsaComponent> library = FsaSampleLibrary.Build(spec, out report);
                Dump(label, library, report, lib);

                // Своя мерка: доли ветвления библиотеки, периоды и множитель Бейтмана.
                var branchReport = new FsaSampleLibrary.Report();
                Dictionary<string, double> branches = FsaSampleLibrary.ChainBranches(chain.Root, branchReport);
                Dictionary<string, double> factors = BatemanFactors(chain.Root, out double rootSeconds);
                var members = new List<string>(branches.Keys);
                members.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string member in members)
                {
                    if (chain.Only.Count > 0 && !chain.Only.Contains(member))
                    {
                        continue;
                    }

                    double seconds;
                    bool decays = TryHalfLife(member, out seconds);
                    List<double[]> decay = FsaSampleLibrary.DecayLines(member, branchReport);
                    if (decay.Count == 0)
                    {
                        continue;
                    }

                    double factor;
                    bool reachable = factors.TryGetValue(member, out factor);
                    double weight = LibraryWeight(library, FsaSampleLibrary.PrettyName(member), decay, floor, max);
                    bool inWindow = false;
                    foreach (double[] line in decay)
                    {
                        if (line[0] >= floor && line[0] <= max)
                        {
                            inWindow = true;
                            break;
                        }
                    }

                    string verdict;
                    if (!reachable)
                    {
                        verdict = double.IsNaN(weight) ? "CUT" : "DIFF";
                    }
                    else if (double.IsNaN(weight))
                    {
                        // Ожидаемое отсутствие: ветвление ниже порога состава
                        // (`FsaSampleSpec.MinChainBranch`, умолчание 1e-3) или все
                        // линии члена вне окна — это не расхождение.
                        verdict = branches[member] < spec.MinChainBranch || !inWindow ? "SKIP" : "ABSENT";
                    }
                    else
                    {
                        double expect = branches[member] * factor;
                        verdict = Math.Abs(weight - expect) <= 1e-9 * Math.Max(1.0, expect) ? "OK" : "DIFF";
                    }

                    if (verdict == "DIFF" || verdict == "ABSENT")
                    {
                        bad++;
                    }

                    Console.WriteLine("EQ\t{0}\t{1}\tT_root={2}\tT={3}\tBR={4}\tf={5}\tBR*f={6}\tlib={7}\t{8}",
                                      label, member, F(rootSeconds), decays ? F(seconds) : "stable",
                                      F(branches[member]), reachable ? F(factor) : "-",
                                      reachable ? F(branches[member] * factor) : "-",
                                      double.IsNaN(weight) ? "-" : F(weight), verdict);
                }
            }

            if (outPath != null)
            {
                File.WriteAllText(outPath, lib.ToString(), new UTF8Encoding(false));
                Console.WriteLine("OUT\t{0}", outPath);
            }

            Console.WriteLine("DONE\tbad={0}", bad);
            return check && bad > 0 ? 1 : 0;
        }

        static void Dump(string item, List<FsaComponent> library, FsaSampleLibrary.Report report, StringBuilder lib)
        {
            foreach (FsaComponent component in library)
            {
                foreach (FsaLine line in component.Lines)
                {
                    string row = string.Format(CultureInfo.InvariantCulture,
                                               "LIB\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}",
                                               item, component.Name, component.Kind, line.Nuclide,
                                               line.Energy.ToString("F3", CultureInfo.InvariantCulture),
                                               F(line.Intensity));
                    Console.WriteLine(row);
                    lib.AppendLine(row);
                }

                Console.WriteLine("COMP\t{0}\t{1}\t{2}\tlines={3}\tyield={4}\troot={5}",
                                  item, component.Name, component.Kind, component.Lines.Count,
                                  F(component.TotalYieldPercent), component.DecayChainRoot ?? "-");
            }

            Console.WriteLine("SUMMARY\t{0}\t{1}", item, report);
        }

        static string F(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Выход линии образа на этой энергии у этого нуклида; NaN — нет.</summary>
        static double LineAt(List<FsaComponent> library, string nuclide, double energy)
        {
            foreach (FsaComponent component in library)
            {
                if (component.Kind == FsaComponentKind.Nuisance)
                {
                    continue;
                }

                foreach (FsaLine line in component.Lines)
                {
                    if (Math.Abs(line.Energy - energy) < 0.05
                        && string.Equals(line.Nuclide, nuclide, StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Intensity;
                    }
                }
            }

            return double.NaN;
        }

        /// <summary>
        /// Вес, с которым линии члена легли в библиотеку: медиана отношения
        /// `I_lib / I_distr` по линиям члена в окне. NaN — линий члена в
        /// библиотеке нет.
        /// </summary>
        static double LibraryWeight(List<FsaComponent> library, string nuclide, List<double[]> decay,
                                    double floor, double max)
        {
            var ratios = new List<double>();
            foreach (double[] line in decay)
            {
                if (line[0] < floor || line[0] > max)
                {
                    continue;
                }

                double have = LineAt(library, nuclide, line[0]);
                if (!double.IsNaN(have))
                {
                    ratios.Add(have / line[1]);
                }
            }

            if (ratios.Count == 0)
            {
                return double.NaN;
            }

            ratios.Sort();
            return ratios[ratios.Count / 2];
        }

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
                        if (!reader.IsDBNull(0))
                        {
                            sum += reader.GetDouble(0);
                        }
                    }
                }
            }

            return sum;
        }

        static bool TryHalfLife(string nucid, out double seconds)
        {
            seconds = 0.0;
            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select half_life_sec from nuclides where nucid = $n"
                    + " and half_life_sec is not null order by l_seqno limit 1";
                command.Parameters.AddWithValue("$n", nucid);
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read() || reader.IsDBNull(0))
                    {
                        return false;
                    }

                    seconds = Convert.ToDouble(reader.GetValue(0), CultureInfo.InvariantCulture);
                    return seconds > 0.0;
                }
            }
        }

        /// <summary>
        /// Множитель переходного равновесия каждого члена подряда от корня —
        /// СВОИМ обходом: x_root = 1; x_k = f_k · Σ_p x_p · BR(p→k) / BR_k,
        /// где f_k = λ_k/(λ_k − λ_root) (Бейтман при t ≫ 1/λ_k). Возвращается
        /// именно f-произведение (без ветвления): Π f_j по путям, взвешенное
        /// ветвлением, — для ряда без ветвлений это просто произведение по
        /// пути. Член с периодом не короче корня не входит и закрывает всё под
        /// собой (как `EquilibriumMembers`, T259). Стабильные — не входят
        /// (линий нет).
        /// </summary>
        static Dictionary<string, double> BatemanFactors(string root, out double rootSeconds)
        {
            var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (!TryHalfLife(root, out rootSeconds))
            {
                return result;
            }

            double lambdaRoot = Math.Log(2.0) / rootSeconds;
            var edges = new Dictionary<string, List<KeyValuePair<string, double>>>(StringComparer.OrdinalIgnoreCase);
            var life = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string> { root };
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            life[root] = rootSeconds;
            using (SqliteConnection connection = Open())
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "select daughter_nucid, perc from decay_chain d"
                    + " where nucid = $n and perc not null"
                    + DecayParentRule.ChainLevelClause;
                command.Parameters.AddWithValue("$n", root);
                for (int i = 0; i < order.Count && order.Count <= 128; i++)
                {
                    string current = order[i];
                    command.Parameters["$n"].Value = current;
                    var step = new List<KeyValuePair<string, double>>();
                    var found = new List<KeyValuePair<string, double>>();
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string daughter = reader.IsDBNull(0) ? null : reader.GetString(0);
                            if (string.IsNullOrEmpty(daughter)
                                || string.Equals(daughter, current, StringComparison.OrdinalIgnoreCase)
                                || reader.IsDBNull(1))
                            {
                                continue;
                            }

                            double percent = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
                            if (!(percent > 0.0))
                            {
                                continue;
                            }

                            found.Add(new KeyValuePair<string, double>(daughter, percent / 100.0));
                        }
                    }

                    foreach (KeyValuePair<string, double> edge in found)
                    {
                        double seconds;
                        bool decays = TryHalfLife(edge.Key, out seconds);
                        if (decays && !(seconds < rootSeconds))
                        {
                            // Длиннее корня — вне равновесия, и всё под ним закрыто.
                            continue;
                        }

                        step.Add(edge);
                        if (known.Add(edge.Key))
                        {
                            if (decays)
                            {
                                life[edge.Key] = seconds;
                                order.Add(edge.Key);
                            }
                        }
                    }

                    edges[current] = step;
                }
            }

            // Релаксация: x_k (с ветвлением и множителями) и b_k (одно ветвление);
            // f-произведение члена = x_k / b_k.
            var x = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { root, 1.0 } };
            var b = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { root, 1.0 } };
            for (int pass = 0; pass < 256; pass++)
            {
                var nx = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { root, 1.0 } };
                var nb = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { root, 1.0 } };
                foreach (string parent in order)
                {
                    double hx, hb;
                    List<KeyValuePair<string, double>> outgoing;
                    if (!x.TryGetValue(parent, out hx) || !edges.TryGetValue(parent, out outgoing))
                    {
                        continue;
                    }

                    b.TryGetValue(parent, out hb);
                    foreach (KeyValuePair<string, double> edge in outgoing)
                    {
                        double seconds;
                        double f = 1.0;
                        if (life.TryGetValue(edge.Key, out seconds))
                        {
                            double lambda = Math.Log(2.0) / seconds;
                            f = lambda / (lambda - lambdaRoot);
                        }

                        double ax, ab;
                        nx.TryGetValue(edge.Key, out ax);
                        nb.TryGetValue(edge.Key, out ab);
                        nx[edge.Key] = ax + hx * edge.Value * f;
                        nb[edge.Key] = ab + hb * edge.Value;
                    }
                }

                double drift = 0.0;
                foreach (KeyValuePair<string, double> row in nx)
                {
                    double was;
                    x.TryGetValue(row.Key, out was);
                    drift = Math.Max(drift, Math.Abs(row.Value - was));
                }

                x = nx;
                b = nb;
                if (drift <= 1e-14)
                {
                    break;
                }
            }

            foreach (KeyValuePair<string, double> row in x)
            {
                double branch;
                if (b.TryGetValue(row.Key, out branch) && branch > 0.0)
                {
                    result[row.Key] = row.Value / branch;
                }
            }

            return result;
        }
    }
}
