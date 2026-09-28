using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FsaBetaPlusShareProbe
{
    /// <summary>
    /// ДОЛЯ β⁺ ВЕТВИ И ПАРТНЁР 511 У СУММАТОРА СОВПАДЕНИЙ — прямой и обратной
    /// условной (П149, 24.09.2026, строки `AMBER100` и `AMBER81`).
    ///
    /// Для каждого родителя (`--sample=` — названных, `--all` — всех со
    /// строками `B+` в `nucdb`) печатает то, что держит
    /// <see cref="CascadeAtomicData"/>: выход линии 511 (`AnnihilationQuanta`,
    /// два кванта на позитрон), по ветвям — `Perc` и условную долю β⁺
    /// (`BetaPlusShare`), по гаммам — P(511 | γ) = `AnnihilationQuantaOfLine`,
    /// выход линии I(γ) и ОБРАТНУЮ условную
    ///
    ///     P(γ | 511) = P(511 | γ) · I(γ) / I(511),
    ///
    /// ту самую, которую сумматор строит для пары «511 → γ»
    /// (`FsaCascadeSummer`, `AMBER100`). Больше единицы она быть не может: у
    /// исправного нуклида это признак того, что две стороны взяты из разных
    /// поставок. Печатается и примечание базы (`Note`).
    ///
    ///     fsabetaplusshareprobe (--sample=22NA,88Y | --all) [--check] [--out=&lt;tsv&gt;]
    ///                           [--formula=ensdf|supply]
    ///
    /// `--check` — код 1, если у кого-то обратная условная больше 1 + 1e-9
    /// либо (`S188`) прямая есть, а обратной нет — пара потеряна.
    ///
    /// (`S188`, П152 24.09.2026) Обратная — ТА, ЧТО СТРОИТ СУММАТОР:
    /// <see cref="CascadeAtomicData.AnnihilationReverseOfLine"/> — прежнее правило,
    /// а где оно за единицей — поток ENSDF (`--formula=ensdf`, умолчание).
    /// `--formula=supply` судит прежнее
    /// правило P(511 | γ)·I(γ)/I(511) из двух поставок — положительный
    /// контроль: на нынешней базе оно обязано дать `--check` код 1 (16 линий у
    /// 13 родителей, замер П149; 15 у 12 после перезаливки `ensdf_feedings`
    /// с TI, `AMBER121` П169 28.09.2026). Печатаются обе.
    /// Имён нуклидов в пробе нет: кого печатать — ключи или база.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            var samples = new List<string>();
            bool all = false, check = false, supplyFormula = false;
            string outPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--sample=", StringComparison.Ordinal))
                    samples.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a == "--all") all = true;
                else if (a == "--check") check = true;
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a == "--formula=supply") supplyFormula = true;
                else if (a == "--formula=ensdf") supplyFormula = false;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (all)
            {
                string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = db,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString()))
                {
                    connection.Open();
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "select distinct parent_nucid from decay_radiations"
                                              + " where type_a = 'B+' order by parent_nucid";
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read()) samples.Add(reader.GetString(0));
                        }
                    }
                }
            }

            if (samples.Count == 0)
            {
                Console.Error.WriteLine("нечего печатать: --sample=<nucid,...> или --all");
                return 2;
            }

            bool verbose = !all;
            int over = 0, parentsOver = 0, missing = 0, lost = 0, discrepancies = 0;
            var dump = new StringBuilder("nucid\tannihilation_quanta\tbranch\tdec_type\tperc\tbeta_plus_share\tgamma_kev\tintensity_pct\tp511_given_g\tpg_given_511\tpg_given_511_supply\n");
            foreach (string raw in samples)
            {
                string nucid = FsaSampleLibrary.NucidOf(raw);
                if (nucid.Length == 0) nucid = raw.Trim();
                CascadeAtomicData data = CascadeAtomicData.Of(nucid);
                if (data == null)
                {
                    missing++;
                    if (verbose) Console.WriteLine("=== {0}: сумматору сказать нечего (null) ===", nucid);
                    continue;
                }

                double i511 = data.AnnihilationQuanta;
                if (verbose)
                {
                    Console.WriteLine();
                    Console.WriteLine("=== {0}: I(511) = {1} квантов на распад ({2} %) ===", nucid,
                                      F(i511, "F6"), F(100.0 * i511, "F3"));
                    foreach (CascadeAtomicData.Branch branch in data.Branches)
                    {
                        Console.WriteLine("  ветвь → {0}\tканал {1}\tperc {2} %\tP(β⁺ | ветвь) {3}\tдоля β⁺ родителя {4}",
                                          branch.Nucid, branch.DecType ?? "—", F(branch.Perc, "F4"),
                                          F(branch.BetaPlusShare, "F6"), F(branch.BetaPlusOfParent, "F6"));
                    }

                    foreach (CascadeAtomicData.SupplyDiscrepancy item in data.Discrepancies)
                    {
                        Console.WriteLine("  расхождение поставок (окну): {0} {1}→{2} канал {3}: {4} / {5} → {6}{7}",
                                          item.Kind, item.Parent, item.Daughter, item.Channel,
                                          F(item.Supply, "F5"), F(item.Other, "F5"), F(item.Taken, "F3"),
                                          item.LevelsKept ? " (уровни по ENSDF)" : "");
                    }
                }

                bool parentOver = false;
                foreach (CascadeAtomicData.GammaLine row in data.GammaIntensity)
                {
                    double p511 = data.AnnihilationQuantaOfLine(row);
                    if (!(p511 > 0.0)) continue;
                    double fromSupply = i511 > 0.0 ? p511 * (row.IntensityPct / 100.0) / i511 : double.NaN;
                    double ensdf = data.AnnihilationReverseOfLine(row);
                    double inverse = supplyFormula ? fromSupply : ensdf;
                    bool bad = inverse > 1.0 + 1e-9;
                    bool gone = !supplyFormula && !(ensdf > 0.0);
                    if (bad) { over++; parentOver = true; }
                    if (gone) { lost++; parentOver = true; }
                    CascadeAtomicData.Branch owner = data.BranchOfLine(row);
                    if (verbose || bad || gone)
                    {
                        Console.WriteLine("  {0}γ {1} кэВ\tI(γ) {2} %\tP(511 | γ) {3}\tP(γ | 511) {4}\t(сумматор {5}, две поставки {6}){7}",
                                          verbose ? "" : nucid + " ", F(row.EnergyKev, "F3"), F(row.IntensityPct, "F4"),
                                          F(p511, "F6"), F(inverse, "F6"), F(ensdf, "F6"), F(fromSupply, "F6"),
                                          bad ? "\t⛔ > 1" : gone ? "\t⛔ обратной нет" : "");
                    }

                    dump.AppendFormat(CultureInfo.InvariantCulture, "{0}\t{1:R}\t{2}\t{3}\t{4:R}\t{5:R}\t{6:R}\t{7:R}\t{8:R}\t{9:R}\t{10:R}\n",
                                      nucid, i511, owner != null ? owner.Nucid : "", owner != null ? owner.DecType : "",
                                      owner != null ? owner.Perc : double.NaN, owner != null ? owner.BetaPlusShare : double.NaN,
                                      row.EnergyKev, row.IntensityPct, p511, ensdf, fromSupply);
                }

                if (parentOver) parentsOver++;
                discrepancies += data.Discrepancies.Count;
                foreach (CascadeAtomicData.Branch branch in data.Branches)
                {
                    dump.AppendFormat(CultureInfo.InvariantCulture, "{0}\t{1:R}\t{2}\t{3}\t{4:R}\t{5:R}\t\t\t\t\t\n",
                                      nucid, i511, branch.Nucid, branch.DecType, branch.Perc, branch.BetaPlusShare);
                }

                if (verbose && !string.IsNullOrEmpty(data.Note))
                {
                    Console.WriteLine("  примечание: {0}", data.Note);
                }
            }

            if (outPath != null)
            {
                File.WriteAllText(outPath, dump.ToString(), new UTF8Encoding(false));
                Console.WriteLine("таблица: " + Path.GetFullPath(outPath));
            }

            Console.WriteLine();
            Console.WriteLine("обратная: {0}", supplyFormula
                ? "прежнее правило P(511 | γ)·I(γ)/I(511) из двух поставок (--formula=supply)"
                : "сумматора: прежнее правило, за единицей — поток ENSDF (AnnihilationReverseOfLine)");
            Console.WriteLine("родителей {0}, без данных сумматора {1}; линий с P(γ | 511) > 1: {2}, без обратной: {3}; у {4} родителей",
                              samples.Count, missing, over, lost, parentsOver);
            Console.WriteLine("расхождений поставок для окна отчёта (S187): {0}", discrepancies);
            Console.WriteLine("обратных за единицей: заменено потоком ENSDF {0}, ENSDF интенсивности не дала {1}",
                              CascadeAtomicData.ReverseReplaced, CascadeAtomicData.ReverseFallbacks);
            return check && (over > 0 || lost > 0) ? 1 : 0;
        }

        static string F(double value, string format)
        {
            return double.IsNaN(value) ? "—" : value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
