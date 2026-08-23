using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DecayReadersProbe
{
    /// <summary>
    /// ДВА ЧИТАТЕЛЯ `decay_radiations` обязаны давать один I_K (`S89`).
    ///
    /// Таблицу читают два места, и зажимы у них РАЗНЫЕ:
    ///
    ///   * <see cref="CascadeAtomicData"/> — `where parent_nucid = $n`, и всё:
    ///     ни уровня, ни типа распада;
    ///   * <see cref="FsaSampleLibrary.DecayLines"/> — плюс
    ///     `parent_l_seqno = (select min(parent_l_seqno) …)`.
    ///
    /// Разойдясь, библиотека и суммирователь совпадений дают разный состав
    /// пробы при одинаковых с виду числах — ровно то, что уже стоило разбора в
    /// `T50`. Признак отказа без читателя не живёт, поэтому читатель здесь.
    ///
    ///     decayreadersprobe [--all] [--limit=N]
    ///
    /// Без ключей проверяются родители, у которых расхождение ВОЗМОЖНО (строки
    /// больше чем на одном `parent_l_seqno`), плюс контрольная горстка обычных
    /// нуклидов — на них читатели обязаны совпасть. `--all` гонит по ВСЕМ
    /// родителям с K-рентгеном (около 1800, минуты).
    ///
    /// Ожидание: «СОШЛИСЬ» либо перечень расхождений поимённо. ⛔ Ненулевой код
    /// возврата здесь означает не поломку пробы, а незакрытую `S89`: пока
    /// решение не принято, четыре изомера расходятся ЗАКОННО и печатаются
    /// списком. Рядом с exe нужна `nucdb.sqlite`.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            bool all = false;
            int limit = int.MaxValue;
            foreach (string a in args)
            {
                if (a == "--all") all = true;
                else if (a.StartsWith("--limit=", StringComparison.Ordinal))
                    limit = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            }

            string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(db))
            {
                Console.Error.WriteLine("нет nucdb.sqlite рядом с пробой: " + db);
                return 2;
            }

            List<string> parents = all ? WithKXray(db) : Suspects(db);
            if (parents.Count > limit)
            {
                parents.RemoveRange(limit, parents.Count - limit);
            }

            Console.WriteLine("=== два читателя decay_radiations (S89) ===");
            Console.WriteLine("родителей на проверке: {0}{1}", parents.Count,
                              all ? " (все с K-рентгеном)" : " (подозрительные + контроль)");

            var bad = new List<string>();
            foreach (string nucid in parents)
            {
                CascadeAtomicData cascade = CascadeAtomicData.Of(nucid);
                if (cascade == null || cascade.KLines == null)
                {
                    // Суммирователь не строит данных вовсе — сравнивать нечего.
                    // Это не отказ: у нуклида может не быть ни захвата, ни
                    // K-рентгена. Молчать всё же нельзя, иначе «сошлись»
                    // означало бы «не смотрели».
                    Console.WriteLine("  {0}: суммирователь данных не дал — пропущен", nucid);
                    continue;
                }

                var report = new FsaSampleLibrary.Report();
                List<double[]> lines = FsaSampleLibrary.DecayLines(nucid, report);

                // Сравниваются НЕ суммы, а линия к линии: одинаковый итог при
                // разных линиях — тоже расхождение, и оно опаснее, потому что
                // по итогу невидимо.
                double missing = 0.0;
                var lost = new List<string>();
                foreach (double[] k in cascade.KLines)
                {
                    if (!(k[1] > 0.0))
                    {
                        continue;
                    }

                    if (!HasLine(lines, k[0], k[1]))
                    {
                        missing += k[1];
                        lost.Add(k[0].ToString("F3", CultureInfo.InvariantCulture)
                                 + " (I=" + k[1].ToString("F4", CultureInfo.InvariantCulture) + ")");
                    }
                }

                if (lost.Count == 0)
                {
                    continue;
                }

                bad.Add(nucid);
                Console.WriteLine("  РАСХОЖДЕНИЕ {0}: у суммирователя есть, у библиотеки нет — {1}"
                                  + "; потеряно I_K = {2:F4} из {3:F4}",
                                  nucid, string.Join(", ", lost.ToArray()),
                                  missing, cascade.KIntensityPct);
            }

            if (bad.Count == 0)
            {
                Console.WriteLine("СОШЛИСЬ: у всех проверенных родителей K-линии суммирователя");
                Console.WriteLine("         присутствуют в линиях библиотеки с тем же выходом.");
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("РАСХОЖДЕНИЙ: {0}. Это `S89`, и решения у неё пока нет:", bad.Count);
            Console.WriteLine("  зажим по min(parent_l_seqno) берёт линии ОДНОГО уровня родителя,");
            Console.WriteLine("  а суммирователь складывает все. Пока не решено, какой набор верен,");
            Console.WriteLine("  список обязан быть виден — молчащее расхождение и есть беда строки.");
            return 1;
        }

        /// <summary>
        /// Совпала ли K-линия суммирователя со строкой библиотеки. Допуск по
        /// энергии — 1 эВ: обе стороны читают ОДНО поле базы, и расходиться там
        /// нечему, кроме округления при переводе.
        /// </summary>
        static bool HasLine(List<double[]> lines, double energy, double intensity)
        {
            foreach (double[] line in lines)
            {
                if (Math.Abs(line[0] - energy) < 0.001
                    && Math.Abs(line[1] - intensity) <= 1.0e-6 * Math.Max(1.0, Math.Abs(intensity)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Родители, у которых расхождение ВОЗМОЖНО: строки больше чем на одном
        /// `parent_l_seqno`. Только там зажим библиотеки что-то отсекает —
        /// разные `dec_type` не зажимает ни один из читателей.
        /// </summary>
        static List<string> Suspects(string db)
        {
            var found = Query(db,
                "select parent_nucid from decay_radiations"
                + " group by parent_nucid having count(distinct parent_l_seqno) > 1");

            // Контроль: на обычных нуклидах читатели обязаны совпадать, и без
            // этой горстки «расхождений нет» означало бы лишь «не искали».
            foreach (string nucid in new[] { "137CS", "60CO", "133BA", "109CD",
                                             "54MN", "241AM", "152EU", "176LU" })
            {
                if (!found.Contains(nucid))
                {
                    found.Add(nucid);
                }
            }

            return found;
        }

        static List<string> WithKXray(string db)
        {
            return Query(db,
                "select distinct parent_nucid from decay_radiations"
                + " where type_a = 'X' and type_c like 'K%' and intensity_num > 0"
                + " order by parent_nucid");
        }

        static List<string> Query(string db, string sql)
        {
            var found = new List<string>();
            using (var connection = new SqliteConnection("Data Source=" + db + ";Mode=ReadOnly;"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                found.Add(reader.GetString(0));
                            }
                        }
                    }
                }
            }

            return found;
        }
    }
}
