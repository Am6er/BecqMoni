using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CascadePairProbe
{
    /// <summary>
    /// ЧИТАТЕЛЬ ДОЛЕЙ ПАР КАСКАДА (`S176`, полоса П90 17.09.2026).
    ///
    /// Что печатает. Для каждого названного нуклида — таблицу пар совпадений,
    /// какой её видит счёт приложения (<see cref="FsaCascadeSummer.PairTable"/>):
    /// носитель, партнёр, P(B|A) ПОСЛЕ замены доли поставки вероятностью из
    /// схемы уровней, — и счётчики замены (пар по схеме / оставшихся с долей
    /// поставки / носителей без перехода). Зачем: с 17.09.2026 доля пары
    /// считается ходом по `g4_gamma`, а не берётся из `gamma_coincidence.fraction`,
    /// и без читателя «замена сработала» и «замена не сработала, доли прежние»
    /// с виду одно и то же — обе печатают сумм-пик, только в 2.7 раза разный.
    ///
    /// Сверка с эталоном. `--ref=&lt;csv&gt;` — файл от `handover/p90-s176/ref_pairs.py`
    /// (независимая реализация того же хода по схеме на питоне,
    /// `nucid;E_A;E_B;P;from_seq;z;a`): каждая пара таблицы обязана сойтись с
    /// эталоном до `--tol=` (умолчание 1e-9 относительных); пара, у которой
    /// эталон говорит «поставочная» (P = −1), обязана и у приложения нести
    /// долю поставки — сверяется с `v_gamma_coincidence` прямо из базы.
    ///
    /// Положительный контроль. `--plant=&lt;nucid&gt;:&lt;E_A&gt;:&lt;E_B&gt;` умножает
    /// значение эталона у этой пары на 2 перед сверкой — проба ОБЯЗАНА
    /// покраснеть (код 1); не покраснела — слепа.
    ///
    /// Ключи:
    ///   --nuclide=Eu-152,Lu-176     имена, как в библиотеке (или nucid 152EU)
    ///   --ref=&lt;csv&gt;                эталон; без него — только печать
    ///   --tol=1e-9                  относительный допуск сверки
    ///   --plant=152EU:1408.006:121.781   положительный контроль
    ///   --quiet                     не печатать таблицы, только итоги
    /// Код возврата: 0 — сошлось (или сверки не просили); 1 — расхождение с
    /// эталоном (пары названы); 2 — ключи/файлы/база.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var nuclides = new List<string>();
            string reference = null, plant = null;
            double tol = 1.0E-9;
            bool quiet = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--nuclide=", StringComparison.Ordinal))
                {
                    nuclides.AddRange(a.Substring(10).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (a.StartsWith("--ref=", StringComparison.Ordinal)) reference = a.Substring(6);
                else if (a.StartsWith("--plant=", StringComparison.Ordinal)) plant = a.Substring(8);
                else if (a.StartsWith("--tol=", StringComparison.Ordinal))
                {
                    tol = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                }
                else if (a == "--quiet") quiet = true;
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (nuclides.Count == 0)
            {
                Console.Error.WriteLine("нужен --nuclide=Eu-152[,Lu-176]");
                return 2;
            }

            // Эталон: (nucid, E_A, E_B) → P; −1 — поставочная.
            var expected = new Dictionary<string, double>(StringComparer.Ordinal);
            if (reference != null)
            {
                if (!File.Exists(reference))
                {
                    Console.Error.WriteLine("нет эталона: " + reference);
                    return 2;
                }

                foreach (string line in File.ReadAllLines(reference))
                {
                    string[] cells = line.Split(';');
                    if (cells.Length < 4 || cells[0] == "nucid")
                    {
                        continue;
                    }

                    double ea = double.Parse(cells[1], CultureInfo.InvariantCulture);
                    double eb = double.Parse(cells[2], CultureInfo.InvariantCulture);
                    double p = double.Parse(cells[3], CultureInfo.InvariantCulture);
                    expected[Key(cells[0], ea, eb)] = p;
                }

                if (plant != null)
                {
                    string[] parts = plant.Split(':');
                    if (parts.Length != 3)
                    {
                        Console.Error.WriteLine("--plant= ждёт nucid:E_A:E_B");
                        return 2;
                    }

                    string key = Key(parts[0],
                                     double.Parse(parts[1], CultureInfo.InvariantCulture),
                                     double.Parse(parts[2], CultureInfo.InvariantCulture));
                    double had;
                    if (!expected.TryGetValue(key, out had))
                    {
                        Console.Error.WriteLine("--plant: пары нет в эталоне: " + key);
                        return 2;
                    }

                    expected[key] = had > 0.0 ? had * 2.0 : 0.5;
                    Console.WriteLine("ПОДСАДКА: эталон пары {0} испорчен ({1:G6} → {2:G6}); проба обязана покраснеть",
                                      key, had, expected[key]);
                }
            }

            FsaCascadeSummer.ResetSchemeCounters();
            int bad = 0, checkedPairs = 0, total = 0;
            double worst = 0.0;
            string worstKey = "";
            foreach (string name in nuclides)
            {
                string key = FsaCascadeSummer.ParentKey(name);
                List<double[]> table = FsaCascadeSummer.PairTable(name);
                Console.WriteLine();
                Console.WriteLine("=== {0} (ключ {1}): пар {2} ===", name, key ?? "?", table.Count);
                if (!quiet)
                {
                    Console.WriteLine("   носитель, кэВ   партнёр, кэВ      P(B|A)");
                }

                foreach (double[] pair in table)
                {
                    total++;
                    string k = Key(key, pair[0], pair[1]);
                    double want;
                    bool have = expected.TryGetValue(k, out want);
                    string verdict = "";
                    if (reference != null)
                    {
                        if (!have)
                        {
                            verdict = "  НЕТ В ЭТАЛОНЕ";
                            bad++;
                        }
                        else if (want < 0.0)
                        {
                            // Эталон говорит: поставочная. У приложения тогда
                            // обязана стоять доля поставки — её берём из базы.
                            double supply = SupplyFraction(key, pair[0], pair[1]);
                            checkedPairs++;
                            if (!(Math.Abs(pair[2] - supply) <= tol * Math.Max(Math.Abs(supply), 1.0E-300)))
                            {
                                verdict = string.Format(CultureInfo.InvariantCulture,
                                    "  РАСХОЖДЕНИЕ: эталон — поставочная {0:G9}", supply);
                                bad++;
                            }
                            else
                            {
                                verdict = "  (поставочная, как и в эталоне)";
                            }
                        }
                        else
                        {
                            checkedPairs++;
                            double rel = Math.Abs(pair[2] - want) / Math.Max(Math.Abs(want), 1.0E-300);
                            if (rel > worst)
                            {
                                worst = rel;
                                worstKey = k;
                            }

                            if (rel > tol)
                            {
                                verdict = string.Format(CultureInfo.InvariantCulture,
                                    "  РАСХОЖДЕНИЕ: эталон {0:G12} (Δ {1:E2})", want, rel);
                                bad++;
                            }
                        }
                    }

                    if (!quiet || verdict.StartsWith("  РАСХОЖДЕНИЕ", StringComparison.Ordinal)
                        || verdict.StartsWith("  НЕТ", StringComparison.Ordinal))
                    {
                        Console.WriteLine("   {0,12:F3}   {1,12:F3}   {2,12:G9}{3}",
                                          pair[0], pair[1], pair[2], verdict);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("замена долей по схеме (S176): пар по схеме {0}, осталось с долей поставки {1}, носителей без перехода {2}; худшее отношение схема/поставка {3:G4} на паре {4:F3}+{5:F3}",
                              FsaCascadeSummer.SchemePairs, FsaCascadeSummer.SchemePairsKept,
                              FsaCascadeSummer.SchemeCarriersUnmatched, FsaCascadeSummer.WorstSchemeRatio,
                              FsaCascadeSummer.WorstSchemeRatioKev, FsaCascadeSummer.WorstSchemeRatioWithKev);
            if (!string.IsNullOrEmpty(FsaCascadeSummer.Notes))
            {
                Console.WriteLine("ПРИМЕЧАНИЕ БАЗЫ: {0}", FsaCascadeSummer.Notes);
            }

            if (!string.IsNullOrEmpty(FsaCascadeSummer.Failure))
            {
                Console.WriteLine("ОТКАЗ БАЗЫ: {0}", FsaCascadeSummer.Failure);
            }

            if (reference == null)
            {
                Console.WriteLine("пар напечатано {0}; сверки не просили", total);
                return 0;
            }

            Console.WriteLine("сверка с эталоном: пар {0}, проверено {1}, расхождений {2}, худшее Δ {3:E2} ({4})",
                              total, checkedPairs, bad, worst, worstKey);
            if (bad > 0)
            {
                Console.WriteLine(plant != null ? "ПОДСАДКА ПОЙМАНА: проба покраснела, как обязана" : "⛔ РАСХОЖДЕНИЕ С ЭТАЛОНОМ");
                return 1;
            }

            if (plant != null)
            {
                Console.WriteLine("⛔ ПОДСАДКА НЕ ПОЙМАНА: проба слепа");
                return 1;
            }

            Console.WriteLine("СОШЛОСЬ");
            return 0;
        }

        static string Key(string nucid, double ea, double eb)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:F3}:{2:F3}",
                                 nucid == null ? "" : nucid.ToUpperInvariant(), ea, eb);
        }

        /// <summary>Доля поставки для пары прямо из `nucdb` — чтобы «поставочная» проверялась числом.</summary>
        static double SupplyFraction(string nucid, double ea, double eb)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select fraction from v_gamma_coincidence where nucid = $n and isomer = 0"
                        + " and abs(energy_kev - $a) < 0.0005 and abs(coinc_energy_kev - $b) < 0.0005";
                    command.Parameters.AddWithValue("$n", nucid);
                    command.Parameters.AddWithValue("$a", ea);
                    command.Parameters.AddWithValue("$b", eb);
                    object value = command.ExecuteScalar();
                    return value == null || value is DBNull ? double.NaN : Convert.ToDouble(value, CultureInfo.InvariantCulture);
                }
            }
        }
    }
}
