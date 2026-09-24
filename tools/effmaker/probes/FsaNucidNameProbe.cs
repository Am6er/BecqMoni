using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FsaNucidNameProbe
{
    /// <summary>
    /// ИМЯ НУКЛИДА → `nucid` ВО ВСЕХ НАПИСАНИЯХ, КОТОРЫЕ ПРОЕКТ САМ ПИШЕТ
    /// (П149, 24.09.2026, строка `AMBER78`).
    ///
    /// ⛔ ЗАЧЕМ. На пути FSA по умолчанию (`FsaLibrary.BuildFromPeaks`) имя
    /// компонента — подпись пика из набора нуклидов, и набор пишет её так, как
    /// её написал `NucBase` (три формата, умолчание — «Cs137, Pa234m1»), как её
    /// написал запасной набор приложения («K40», «Cs137») или человек рукой.
    /// Дальше это имя ищут в базе ДВА места: аннигиляционная линия β⁺
    /// (`FsaSampleLibrary.NucidOf` → `AnnihilationLine`, `AMBER63`) и
    /// суммирование совпадений (`FsaCascadeSummer.ParentKey`). Оба до П149
    /// понимали только «Cs-137».
    ///
    /// Что проба делает. Берёт из `nucdb` (только чтение) родителей со строками
    /// `B+` (`--all` — всех родителей `decay_radiations`), печатает каждое имя
    /// ТРЕМЯ форматами `NucBase` и подписью корпусного пути
    /// (`FsaSampleLibrary.PrettyName`), и для каждого написания спрашивает:
    ///
    ///   * `NucidOf(имя)` вернул ЭТОТ ЖЕ `nucid`? (у подписи `PrettyName`
    ///     номер изомера вырезан нарочно — «Pa-234m», — и там годится любой
    ///     `nucid` того же ядра, у которого номер изомера 1 или его нет);
    ///   * у β⁺-родителя `AnnihilationLine(NucidOf(имя))` не пуст?
    ///   * `FsaCascadeSummer.ParentKey(имя)` одинаков у всех написаний?
    ///
    /// ⚠ Формат `NucBase` повторён здесь КОПИЕЙ (`NucBase.FormatIsotopeName`
    /// закрыт в форме): то же выражение, те же три ветки. Разойдётся копия с
    /// формой — проба мерит не то; сверять глазами при правке формы.
    ///
    ///     fsanucidnameprobe [--all] [--check] [--out=&lt;tsv&gt;]
    ///
    /// Код 0 — напечатала (с `--check` — и всё сошлось); 1 — с `--check` есть
    /// несошедшиеся; 2 — ключи не разобраны или базы нет.
    /// </summary>
    static class Program
    {
        static readonly Regex NucBaseName = new Regex("^([0-9]+){1}([A-Z]+){1}(m[0-9]+)?$");

        /// <summary>Все `nucid` таблицы `nuclides` — «такой нуклид в базе есть».</summary>
        static HashSet<string> known;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            bool all = false, check = false;
            string outPath = null;
            foreach (string a in args)
            {
                if (a == "--all") all = true;
                else if (a == "--check") check = true;
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(db))
            {
                Console.Error.WriteLine("нет базы: " + db);
                return 2;
            }

            var parents = new List<string>();
            var betaPlus = new HashSet<string>(StringComparer.Ordinal);
            known = new HashSet<string>(StringComparer.Ordinal);
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = db,
                Mode = SqliteOpenMode.ReadOnly
            }.ToString()))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select nucid from nuclides";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0)) known.Add(reader.GetString(0));
                        }
                    }

                    command.CommandText = "select distinct parent_nucid from decay_radiations"
                                          + " where type_a = 'B+' order by parent_nucid";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            betaPlus.Add(reader.GetString(0));
                        }
                    }

                    command.CommandText = all
                        ? "select distinct parent_nucid from decay_radiations order by parent_nucid"
                        : "select distinct parent_nucid from decay_radiations where type_a = 'B+'"
                          + " order by parent_nucid";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            parents.Add(reader.GetString(0));
                        }
                    }
                }
            }

            string[] formats = { "NucBase 0 (137CS)", "NucBase 1 (Cs137) — умолчание", "NucBase 2 (Cs-137)", "PrettyName (Cs-137, Pa-234m)" };
            int[] back = new int[formats.Length];
            int[] annihilation = new int[formats.Length];
            int[] keyed = new int[formats.Length];
            int[] isomerBack = new int[formats.Length];
            int isomers = 0, betaPlusTotal = 0, betaPlusIsomers = 0, keyedTotal = 0;
            int[] annihilationIsomers = new int[formats.Length];
            var misses = new List<string>();
            var dump = new StringBuilder("nucid\tformat\tname\tnucid_back\tannihilation\tparent_key\n");

            foreach (string nucid in parents)
            {
                int mass;
                string symbol, state;
                if (!CascadeAtomicData.SplitNucid(nucid, out mass, out symbol, out state))
                {
                    misses.Add(nucid + ": сам nucid не разбирается SplitNucid");
                    continue;
                }

                bool isomer = state.Length > 0;
                bool positron = betaPlus.Contains(nucid);
                if (isomer) isomers++;
                if (positron) betaPlusTotal++;
                if (positron && isomer) betaPlusIsomers++;

                string[] names =
                {
                    NucBaseFormat(nucid, 0), NucBaseFormat(nucid, 1), NucBaseFormat(nucid, 2),
                    FsaSampleLibrary.PrettyName(nucid)
                };

                string reference = FsaCascadeSummer.ParentKey(NucBaseFormat(nucid, 2));
                bool anyKey = reference != null;
                if (anyKey) keyedTotal++;

                for (int f = 0; f < names.Length; f++)
                {
                    string got = FsaSampleLibrary.NucidOf(names[f]);
                    bool same = string.Equals(got, nucid, StringComparison.Ordinal)
                                || (f == 3 && SameNucleusFirstIsomer(got, nucid));
                    if (same)
                    {
                        back[f]++;
                        if (isomer) isomerBack[f]++;
                    }
                    else if (misses.Count < 60)
                    {
                        misses.Add(string.Format(CultureInfo.InvariantCulture,
                                                 "{0}: «{1}» ({2}) → «{3}»", nucid, names[f], formats[f], got));
                    }

                    bool line = false;
                    if (positron)
                    {
                        line = FsaSampleLibrary.AnnihilationLine(got) != null;
                        if (line)
                        {
                            annihilation[f]++;
                            if (isomer) annihilationIsomers[f]++;
                        }
                    }

                    string key = FsaCascadeSummer.ParentKey(names[f]);
                    if (anyKey && string.Equals(key, reference, StringComparison.Ordinal))
                    {
                        keyed[f]++;
                    }

                    dump.AppendFormat(CultureInfo.InvariantCulture, "{0}\t{1}\t{2}\t{3}\t{4}\t{5}\n",
                                      nucid, f, names[f], got, positron ? (line ? "1" : "0") : "", key ?? "");
                }
            }

            Console.WriteLine("родителей: {0} ({1}); из них изомеров {2}; β⁺ {3} (изомеров {4}); ключ сумматора у формата 2 есть у {5}",
                              parents.Count, all ? "все decay_radiations" : "со строками B+", isomers,
                              betaPlusTotal, betaPlusIsomers, keyedTotal);
            Console.WriteLine();
            Console.WriteLine("формат\tnucid вернулся\t(изомеров)\tлиния 511 из базы у β⁺\t(изомеров)\tключ сумматора = формату 2");
            int failures = 0;
            for (int f = 0; f < formats.Length; f++)
            {
                Console.WriteLine("{0}\t{1}/{2}\t{3}/{4}\t{5}/{6}\t{7}/{8}\t{9}/{10}",
                                  formats[f], back[f], parents.Count, isomerBack[f], isomers,
                                  annihilation[f], betaPlusTotal, annihilationIsomers[f], betaPlusIsomers,
                                  keyed[f], keyedTotal);
                failures += (parents.Count - back[f]) + (keyedTotal - keyed[f]);
            }

            // Имена, которые проект пишет сам вне NucBase, и имена, которые
            // нуклидом НЕ являются: второе обязано давать пусто или несуществующий
            // в базе `nucid`, иначе подпись элемента превратится в нуклид.
            Console.WriteLine();
            Console.WriteLine("отдельные имена:");
            string[][] singles =
            {
                new[] { "K40", "40K" }, new[] { "Cs137", "137CS" }, new[] { "Cs134", "134CS" },
                new[] { "Na22", "22NA" }, new[] { "22NA", "22NA" }, new[] { "Na-22", "22NA" },
                new[] { "241Am", "241AM" }, new[] { "Am241", "241AM" }, new[] { "am-241", "241AM" },
                new[] { "Pm-147", "147PM" }, new[] { "147Sm", "147SM" }, new[] { "Cm244", "244CM" },
                new[] { "K-38m", "38Km" }, new[] { "38Km", "38Km" },
                new[] { "Pa-234m", "234PAm1" }, new[] { "Pa234m1", "234PAm1" }, new[] { "Pa-234m1", "234PAm1" },
                new[] { "Ag-108m", "108AGm" }, new[] { "Ag108m1", "108AGm" },
                new[] { "Bi-214 (Ra-226)", "" },
                new[] { "W", "" }, new[] { "Pb x-ray", "" }, new[] { "X-ray", "" }, new[] { "Annihilation", "" },
                new[] { "NORM", "" }, new[] { "U+Ra", "" }, new[] { "Cs-137+K-40", "" }
            };
            foreach (string[] single in singles)
            {
                string got = FsaSampleLibrary.NucidOf(single[0]);
                bool ok = string.Equals(got, single[1], StringComparison.Ordinal);
                if (!ok) failures++;
                Console.WriteLine("  {0}\t«{1}» → «{2}»\tключ сумматора «{3}»{4}", ok ? "OK " : "НЕТ",
                                  single[0], got, FsaCascadeSummer.ParentKey(single[0]) ?? "—",
                                  ok ? "" : "\tждали «" + single[1] + "»");
            }

            // Подписи пиков, которые НЕ нуклиды, но через дефис: прежний ответ
            // «число + буквы» остаётся, и в базе такого `nucid` быть не должно.
            foreach (string label in new[] { "SE-2615", "DE-2615", "Ann-511", "Xray-Pb" })
            {
                string got = FsaSampleLibrary.NucidOf(label);
                bool line = got.Length > 0 && FsaSampleLibrary.AnnihilationLine(got) != null;
                Console.WriteLine("  {0}\t«{1}» → «{2}» (линия 511 из базы: {3})", line ? "НЕТ" : "OK ",
                                  label, got, line ? "ЕСТЬ" : "нет");
                if (line) failures++;
            }

            if (misses.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("первые несошедшиеся (до 60):");
                foreach (string miss in misses) Console.WriteLine("  " + miss);
            }

            if (outPath != null)
            {
                File.WriteAllText(outPath, dump.ToString(), new UTF8Encoding(false));
                Console.WriteLine("таблица: " + Path.GetFullPath(outPath));
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + failures.ToString(CultureInfo.InvariantCulture));
            return check && failures > 0 ? 1 : 0;
        }

        /// <summary>
        /// Копия `NucBase.FormatIsotopeName` (закрыт в форме): те же выражение
        /// и три ветки. Имя, не подошедшее под выражение (изомер без номера,
        /// «38Km»), форма отдаёт КАК ЕСТЬ — так же и здесь.
        /// </summary>
        static string NucBaseFormat(string nameFromDb, int format)
        {
            Match match = NucBaseName.Match(nameFromDb);
            if (!match.Success)
            {
                return nameFromDb;
            }

            string mass = match.Groups[1].Value;
            string isotope = match.Groups[2].Value;
            string isotopeLower = isotope.Substring(0, 1) + isotope.Substring(1).ToLower();
            string isomer = match.Groups.Count > 3 ? match.Groups[3].Value : string.Empty;
            switch (format)
            {
                case 0: return mass + isotope + isomer;
                case 2: return isotopeLower + "-" + mass + isomer;
                default: return isotopeLower + mass + isomer;
            }
        }

        /// <summary>
        /// Подпись `PrettyName` номер изомера режет («234PAm1» → «Pa-234m»),
        /// поэтому для неё годится `nucid` того же ядра с изомером без номера
        /// или с номером 1; второй и выше изомер из такой подписи не восстановим.
        /// ⚠ Годится только `nucid`, который в базе ЕСТЬ: «234PAm» при базовом
        /// «234PAm1» — не нуклид, а промах, и линий у него нет.
        /// </summary>
        static bool SameNucleusFirstIsomer(string got, string nucid)
        {
            if (got == null || !known.Contains(got))
            {
                return false;
            }

            int m1, m2;
            string s1, s2, t1, t2;
            if (!CascadeAtomicData.SplitNucid(got, out m1, out s1, out t1)
                || !CascadeAtomicData.SplitNucid(nucid, out m2, out s2, out t2))
            {
                return false;
            }

            return m1 == m2 && s1 == s2 && t2.Length > 0
                   && (t2 == "m" || t2 == "m1") && (t1 == "m" || t1 == "m1");
        }
    }
}
