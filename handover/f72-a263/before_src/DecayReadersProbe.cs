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
    /// Таблицу читают два места, и до `S89` зажимы у них были РАЗНЫЕ:
    ///
    ///   * <see cref="CascadeAtomicData"/> — `where parent_nucid = $n`, и всё:
    ///     ни уровня, ни типа распада;
    ///   * <see cref="FsaSampleLibrary.DecayLines"/> — плюс зажим по уровню.
    ///
    /// Разойдясь, библиотека и суммирователь совпадений дают разный состав
    /// пробы при одинаковых с виду числах — ровно то, что уже стоило разбора в
    /// `T50`. Признак отказа без читателя не живёт, поэтому читатель здесь.
    /// Теперь оба зажимают одинаково, через <see cref="DecayParentRule"/>.
    ///
    ///     decayreadersprobe [--all] [--limit=N] [нуклид ...]
    ///
    /// Названные поимённо нуклиды не проверяются, а ПОКАЗЫВАЮТСЯ: какой уровень
    /// выбрало правило, какие уровни есть в базе, какие K-линии вышли у каждого
    /// читателя. Ради `S94`, где спор идёт именно о выборе уровня.
    ///
    /// Без ключей проверяются родители, у которых расхождение ВОЗМОЖНО (строки
    /// больше чем на одном `parent_l_seqno`), плюс контрольная горстка обычных
    /// нуклидов — на них читатели обязаны совпасть. `--all` гонит по ВСЕМ
    /// родителям с K-рентгеном (1777, около минуты).
    ///
    /// ⛔ ВТОРАЯ ПРОВЕРКА, И ЗАВЕДЕНА ОНА ПО `S94`: согласие в НУЛЕ — не
    /// согласие. Сравнение линия-к-линии молчит, когда у обоих читателей линий
    /// НЕТ вовсе, и ровно так проба пропускала `118INm2`, у которого зажим по
    /// минимальному уровню съедал весь K-рентген («суммирователь данных не дал
    /// — пропущен», код возврата 0). Поэтому список родителей с K-рентгеном
    /// берётся из БАЗЫ и служит обязательством: если база говорит, что
    /// K-рентген у родителя есть, а читатель не дал ни одной K-линии — это
    /// ОТКАЗ, а не пропуск.
    ///
    /// Ожидание: «СОШЛИСЬ» и код 0. Рядом с exe нужна `nucdb.sqlite`.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            bool all = false;
            int limit = int.MaxValue;
            var show = new List<string>();
            foreach (string a in args)
            {
                if (a == "--all") all = true;
                else if (a.StartsWith("--limit=", StringComparison.Ordinal))
                    limit = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (!a.StartsWith("--", StringComparison.Ordinal))
                    show.Add(a);
            }

            string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(db))
            {
                Console.Error.WriteLine("нет nucdb.sqlite рядом с пробой: " + db);
                return 2;
            }

            if (show.Count > 0)
            {
                return Show(db, show);
            }

            // Энергии K-рентгена ПО БАЗЕ, без всякого зажима: это обязательство,
            // против которого проверяется каждый читатель (`S94`). Родитель,
            // стоящий в этом словаре, обязан дать хотя бы одну K-линию — иначе
            // зажим съел рентген целиком, и «расхождений нет» означает лишь,
            // что оба читателя молчат об одном и том же.
            Dictionary<string, List<double>> kInDb = KEnergies(db);

            List<string> parents = all ? new List<string>(SortedKeys(kInDb)) : Suspects(db);
            if (parents.Count > limit)
            {
                parents.RemoveRange(limit, parents.Count - limit);
            }

            Console.WriteLine("=== два читателя decay_radiations (S89, S94) ===");
            Console.WriteLine("родителей на проверке: {0}{1}", parents.Count,
                              all ? " (все с K-рентгеном)" : " (подозрительные + контроль)");

            // Запасная ветвь правила — не отказ, но и не мелочь: она означает,
            // что своих строк у родителя в поставке НЕТ и ему достаются чужие,
            // с уровня ниже. Молчать об этом нельзя, иначе подмена невидима.
            foreach (string nucid in Fallbacks(db))
            {
                Console.WriteLine("  ⚠ ЗАПАСНАЯ ВЕТВЬ {0}: nuclides.l_seqno в decay_radiations не встречается,"
                                  + " взят самый нижний уровень — строки СОСЕДНЕГО состояния", nucid);
            }

            var bad = new List<string>();
            foreach (string nucid in parents)
            {
                List<double> mustHaveK;
                if (!kInDb.TryGetValue(nucid, out mustHaveK))
                {
                    mustHaveK = null;
                }

                CascadeAtomicData cascade = CascadeAtomicData.Of(nucid);
                if (cascade == null || cascade.KLines == null || cascade.KLines.Count == 0)
                {
                    if (mustHaveK != null)
                    {
                        // ⛔ Это и есть `S94`: в базе K-рентген есть, а на выходе
                        // читателя ноль. Раньше здесь стоял «пропущен».
                        bad.Add(nucid);
                        Console.WriteLine("  ПУСТО {0}: в базе {1} K-линий, а суммирователь не дал НИ ОДНОЙ"
                                          + " — зажим по уровню съел K-рентген целиком",
                                          nucid, mustHaveK.Count);
                        continue;
                    }

                    // Суммирователь не строит данных вовсе — сравнивать нечего.
                    // Это не отказ: у нуклида может не быть ни захвата, ни
                    // K-рентгена. Молчать всё же нельзя, иначе «сошлись»
                    // означало бы «не смотрели».
                    Console.WriteLine("  {0}: суммирователь данных не дал — пропущен", nucid);
                    continue;
                }

                var report = new FsaSampleLibrary.Report();
                List<double[]> lines = FsaSampleLibrary.DecayLines(nucid, report);

                if (mustHaveK != null && !AnyOf(lines, mustHaveK))
                {
                    // Та же проверка со стороны библиотеки. Симметрия здесь не
                    // украшение: читатели зажимают из одного места, но обязаны
                    // проверяться порознь — иначе общая ошибка правила станет
                    // невидимой для обоих сразу.
                    bad.Add(nucid);
                    Console.WriteLine("  ПУСТО {0}: в базе {1} K-линий, а библиотека не дала НИ ОДНОЙ",
                                      nucid, mustHaveK.Count);
                    continue;
                }

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
            Console.WriteLine("РАСХОЖДЕНИЙ: {0}. Читатели `decay_radiations` обязаны давать один I_K,", bad.Count);
            Console.WriteLine("  и «ПУСТО» здесь такое же расхождение, как разные линии: зажим по");
            Console.WriteLine("  уровню родителя (`DecayParentRule`) взял набор, в котором K-рентгена");
            Console.WriteLine("  нет вовсе. Смотреть `DecayParentRule` и `S94`, а не подгонять пробу.");
            return 1;
        }

        /// <summary>
        /// Показать поимённо, ЧТО именно берут читатели у названных родителей:
        /// какой уровень выбрало правило <see cref="DecayParentRule"/>, какие
        /// уровни есть в базе и какие K-линии вышли. Ради `S94`: спор о том,
        /// какой набор верен, разбирается числами, а не «должно быть так».
        /// </summary>
        static int Show(string db, List<string> parents)
        {
            Dictionary<string, List<double>> kInDb = KEnergies(db);
            foreach (string nucid in parents)
            {
                Console.WriteLine("=== {0} ===", nucid);
                Console.WriteLine("  уровни в базе: {0};  выбран правилом: {1};  nuclides.l_seqno = {2}",
                                  Join(Levels(db, nucid)), Chosen(db, nucid), OwnLevel(db, nucid));

                CascadeAtomicData cascade = CascadeAtomicData.Of(nucid);
                if (cascade == null || cascade.KLines == null || cascade.KLines.Count == 0)
                {
                    Console.WriteLine("  суммирователь: K-линий НЕТ{0}",
                                      kInDb.ContainsKey(nucid) ? " ⛔ (а в базе они есть)" : "");
                }
                else
                {
                    Console.WriteLine("  суммирователь: I_K = {0:F4} %, линий {1}: {2}",
                                      cascade.KIntensityPct, cascade.KLines.Count, Lines(cascade.KLines));
                }

                var report = new FsaSampleLibrary.Report();
                List<double[]> lines = FsaSampleLibrary.DecayLines(nucid, report);
                var k = new List<double[]>();
                List<double> energies;
                if (kInDb.TryGetValue(nucid, out energies))
                {
                    foreach (double[] line in lines)
                    {
                        foreach (double energy in energies)
                        {
                            if (Math.Abs(line[0] - energy) < 0.001)
                            {
                                k.Add(line);
                                break;
                            }
                        }
                    }
                }

                double sum = 0.0;
                foreach (double[] line in k) sum += line[1];
                Console.WriteLine("  библиотека:    I_K = {0:F4} %, линий {1} (всего линий {2}): {3}",
                                  sum, k.Count, lines.Count, Lines(k));
            }

            return 0;
        }

        static string Lines(List<double[]> lines)
        {
            var text = new List<string>();
            foreach (double[] line in lines)
            {
                text.Add(line[0].ToString("F3", CultureInfo.InvariantCulture)
                         + " (" + line[1].ToString("F4", CultureInfo.InvariantCulture) + ")");
            }

            return string.Join(", ", text.ToArray());
        }

        static string Join(List<int> values)
        {
            var text = new List<string>();
            foreach (int value in values) text.Add(value.ToString(CultureInfo.InvariantCulture));
            return string.Join(", ", text.ToArray());
        }

        static List<int> Levels(string db, string nucid)
        {
            var found = new List<int>();
            foreach (string s in Scalars(db,
                "select distinct parent_l_seqno from decay_radiations where parent_nucid = $n"
                + " order by parent_l_seqno", nucid))
            {
                found.Add(int.Parse(s, CultureInfo.InvariantCulture));
            }

            return found;
        }

        /// <summary>
        /// Уровень родителя по `nuclides` — ВСЕ строки, а не первая: имя там не
        /// уникально (`144TBm` — три строки), и показывать одну значит соврать.
        /// </summary>
        static string OwnLevel(string db, string nucid)
        {
            List<string> found = Scalars(db, "select l_seqno from nuclides where nucid = $n"
                                             + " order by l_seqno", nucid);
            return found.Count > 0 ? string.Join(", ", found.ToArray()) : "нет в nuclides";
        }

        /// <summary>Уровень, который выберет само правило — тем же выражением.</summary>
        static string Chosen(string db, string nucid)
        {
            List<string> found = Scalars(db,
                "select distinct parent_l_seqno from decay_radiations where parent_nucid = $n"
                + DecayParentRule.LevelClause, nucid);
            return found.Count > 0 ? found[0] : "ничего";
        }

        static List<string> Scalars(string db, string sql, string nucid)
        {
            var found = new List<string>();
            using (var connection = new SqliteConnection("Data Source=" + db + ";Mode=ReadOnly;"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = sql;
                    command.Parameters.AddWithValue("$n", nucid);
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            found.Add(reader.IsDBNull(0) ? "" : Convert.ToString(reader.GetValue(0),
                                                                                CultureInfo.InvariantCulture));
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Есть ли среди линий читателя хоть одна на энергии K-рентгена из
        /// базы. Допуск тот же, что и у построчной сверки, — 1 эВ.
        /// </summary>
        static bool AnyOf(List<double[]> lines, List<double> energies)
        {
            if (lines == null)
            {
                return false;
            }

            foreach (double[] line in lines)
            {
                foreach (double energy in energies)
                {
                    if (Math.Abs(line[0] - energy) < 0.001)
                    {
                        return true;
                    }
                }
            }

            return false;
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

        /// <summary>
        /// Энергии K-рентгена по базе, БЕЗ зажима по уровню: родитель → список
        /// энергий. Именно отсюда берётся и список `--all`, и обязательство
        /// «K-рентген у этого родителя есть» (`S94`).
        /// </summary>
        static Dictionary<string, List<double>> KEnergies(string db)
        {
            var found = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            using (var connection = new SqliteConnection("Data Source=" + db + ";Mode=ReadOnly;"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select parent_nucid, energy_num from decay_radiations"
                        + " where type_a = 'X' and type_c like 'K%' and intensity_num > 0"
                        + " and energy_num not null";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                            {
                                continue;
                            }

                            string nucid = reader.GetString(0);
                            List<double> energies;
                            if (!found.TryGetValue(nucid, out energies))
                            {
                                energies = new List<double>();
                                found[nucid] = energies;
                            }

                            energies.Add(reader.GetDouble(1));
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Родители, у которых сработает ЗАПАСНАЯ ветвь <see cref="DecayParentRule"/>:
        /// уровень из `nuclides` в `decay_radiations` не встречается. Одним
        /// запросом на всю таблицу, а не по родителю.
        ///
        /// ⚠ Проверка идёт по ИМЕНИ целиком, а не по строке `nuclides`: имя там
        /// не уникально — у `144TBm` три строки (уровни 7, 6, 4), и по строке
        /// проба ругалась бы на него дважды впустую. Правилу это безразлично,
        /// у него зажим стоит через `exists`, но проба обязана считать так же.
        /// </summary>
        static List<string> Fallbacks(string db)
        {
            return Query(db,
                "select distinct n.nucid from nuclides n"
                + " where exists (select 1 from decay_radiations d where d.parent_nucid = n.nucid)"
                + "   and not exists (select 1 from nuclides w, decay_radiations d"
                + "                   where w.nucid = n.nucid and d.parent_nucid = w.nucid"
                + "                     and d.parent_l_seqno = w.l_seqno)"
                + " order by n.nucid");
        }

        static List<string> SortedKeys(Dictionary<string, List<double>> map)
        {
            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
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
