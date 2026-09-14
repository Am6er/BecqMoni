using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// Поверка разбора `nucid` НА ВСЕЙ БАЗЕ (`D32`).
    ///
    /// ЗАЧЕМ ПРОБА, А НЕ ГЛАЗА. Разбор имени нуклида — три строки кода, и ровно
    /// поэтому его дважды написали неверно. Правило, которое кажется очевидным
    /// («хвост M или m — это изомер»), ломает 176 нуклидов базы разом: все, чей
    /// СИМВОЛ кончается на M — Am, Cm, Fm, Tm, Sm, Pm. Америций разбирался как
    /// «A-241m», не сходился с истиной корпуса и уходил в фантомы на шести
    /// спектрах — при том что нуклид объявлен в манифесте и найден разбором.
    /// Ошибка врала в обе мерки сразу и глазами не видна.
    ///
    /// Метку состояния от символа отделяет **РЕГИСТР**: символ стоит заглавными
    /// целиком («241AM» — америций), метка — строчными («234PAm1», «108AGm»,
    /// «105PDe»). Правило живёт в одном месте —
    /// <see cref="CascadeAtomicData.SplitNucid"/>, — и здесь проверяется, что
    /// оно верно для КАЖДОГО имени базы, а не для тех шести, что пришли в голову.
    ///
    /// ЧТО ИМЕННО ПРОВЕРЯЕТСЯ:
    ///
    ///   * каждый `nucid` из `nuclides` разбирается, и его символ находится
    ///     среди элементов `matdb` (то есть <see cref="CascadeAtomicData.ChargeOf"/>
    ///     отдаёт НЕ ноль);
    ///   * обратный ход `nucid` → подпись → `nucid` возвращает исходное имя —
    ///     без этого имя компонента и ключ базы разойдутся молча;
    ///   * поимённые ловушки, каждая со своей причиной.
    ///
    /// ⚠ Единственное законное исключение — трансфермиевые (Z &gt; 100): в
    /// `matdb.xcom_elements` элементов больше ста нет вовсе, потому что нет
    /// сечений. Это предел ДАННЫХ, а не разбора, и проба считает такие имена
    /// отдельно, а не в отказы.
    ///
    /// Запуск: NucidProbe.exe [--all] — с ключом печатает каждое непрошедшее имя.
    /// </summary>
    static class NucidProbe
    {
        /// <summary>
        /// Ловушки, каждая со своей причиной. Список поимённый нарочно: сплошной
        /// обход скажет «всё сошлось», но не скажет, что именно он проверил.
        /// </summary>
        static readonly string[][] Traps =
        {
            new[] { "241AM",   "Am-241", "символ кончается на M — НЕ изомер (тот самый случай D32)" },
            new[] { "244CM",   "Cm-244", "то же: кюрий" },
            new[] { "147SM",   "Sm-147", "то же: самарий" },
            new[] { "147PM",   "Pm-147", "то же: прометий" },
            new[] { "170TM",   "Tm-170", "то же: тулий" },
            new[] { "234PAm1", "Pa-234m", "изомер с номером состояния: номер в подпись не идёт" },
            new[] { "108AGm",  "Ag-108m", "изомер без номера" },
            new[] { "105PDe",  "Pd-105e", "метка состояния не только «m»" },
            new[] { "212PB",   "Pb-212",  "обычный член ряда" },
            new[] { "40K",     "K-40",    "односимвольный элемент" },
        };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            bool verbose = Array.IndexOf(args, "--all") >= 0;

            int failed = 0;

            Console.WriteLine("ЛОВУШКИ РАЗБОРА ИМЕНИ (регистр отделяет метку состояния от символа)");
            Console.WriteLine();
            Console.WriteLine("  nucid       ждём        вышло       Z    обратно     итог   почему");
            foreach (string[] trap in Traps)
            {
                string pretty = FsaSampleLibrary.PrettyName(trap[0]);
                int z = CascadeAtomicData.ChargeOf(trap[0]);
                string back = FsaSampleLibrary.NucidOf(pretty);
                bool ok = string.Equals(pretty, trap[1], StringComparison.Ordinal) && z > 0;
                if (!ok)
                {
                    failed++;
                }

                Console.WriteLine("  {0,-10}  {1,-10}  {2,-10}  {3,3}  {4,-10}  {5}   {6}",
                                  trap[0], trap[1], pretty, z, back,
                                  ok ? "СОШЛОСЬ" : "⛔ НЕ ТО", trap[2]);
            }

            Console.WriteLine();
            Console.WriteLine("СПЛОШНОЙ ОБХОД `nuclides`");
            Console.WriteLine();

            var names = new List<string>();
            try
            {
                using (SqliteConnection connection = new SqliteConnection(
                    "Data Source=" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite")
                    + ";Mode=ReadOnly;Cache=Shared;"))
                {
                    connection.Open();
                    using (SqliteCommand command = connection.CreateCommand())
                    {
                        command.CommandText = "select nucid from nuclides";
                        using (SqliteDataReader reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                if (!reader.IsDBNull(0))
                                {
                                    names.Add(reader.GetString(0));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("не прочитать nucdb: " + error.Message);
                return 2;
            }

            int split = 0, charged = 0, isomers = 0, roundtrip = 0;
            var heavy = new List<string>();
            var broken = new List<string>();
            var mismatched = new List<string>();
            foreach (string nucid in names)
            {
                int mass;
                string symbol, state;
                if (!CascadeAtomicData.SplitNucid(nucid, out mass, out symbol, out state))
                {
                    broken.Add(nucid);
                    continue;
                }

                split++;
                if (state.Length > 0)
                {
                    isomers++;
                }

                int z = CascadeAtomicData.ChargeOf(nucid);
                if (z > 0)
                {
                    charged++;
                }
                else
                {
                    // Трансфермиевые: элемента нет в `xcom_elements`, потому что
                    // нет сечений. Предел данных, не разбора.
                    heavy.Add(nucid);
                }

                // Обратный ход. У изомера с НОМЕРОМ состояния он законно теряет
                // номер («234PAm1» -> «Pa-234m» -> «234PAm»), и это не поломка,
                // а принятое соглашение подписи: мерка корпуса зовёт его
                // «Pa-234m». Считаем сошедшимся, если совпало имя без номера.
                string pretty = FsaSampleLibrary.PrettyName(nucid);
                string back = FsaSampleLibrary.NucidOf(pretty);
                string trimmed = nucid.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                if (string.Equals(back, nucid, StringComparison.OrdinalIgnoreCase)
                    || (state.Length > 0
                        && string.Equals(back, trimmed, StringComparison.OrdinalIgnoreCase)))
                {
                    roundtrip++;
                }
                else
                {
                    mismatched.Add(nucid + " -> " + pretty + " -> " + back);
                }
            }

            Console.WriteLine("  имён в базе:                {0}", names.Count);
            Console.WriteLine("  разобрано:                  {0}{1}", split,
                              broken.Count > 0 ? "  ⛔ НЕ разобрано " + broken.Count : "");
            Console.WriteLine("  из них с меткой состояния:  {0}", isomers);
            Console.WriteLine("  Z определён:                {0}", charged);
            Console.WriteLine("  Z НЕ определён (Z > 100):   {0}  — предел `xcom_elements`, не разбора",
                              heavy.Count);
            Console.WriteLine("  обратный ход сошёлся:       {0}{1}", roundtrip,
                              mismatched.Count > 0 ? "  ⛔ разошлось " + mismatched.Count : "");

            if (broken.Count > 0 || mismatched.Count > 0)
            {
                failed++;
                Console.WriteLine();
                int show = verbose ? int.MaxValue : 12;
                foreach (string name in broken)
                {
                    if (show-- <= 0) { break; }
                    Console.WriteLine("    ⛔ не разобрано: {0}", name);
                }

                show = verbose ? int.MaxValue : 12;
                foreach (string name in mismatched)
                {
                    if (show-- <= 0) { break; }
                    Console.WriteLine("    ⛔ обратный ход: {0}", name);
                }
            }

            // Проверка, что «Z > 100» — действительно только трансфермиевые, а
            // не удобное объяснение. Символ каждого обязан отсутствовать среди
            // ста элементов базы, и это проверяется, а не утверждается.
            int impostors = 0;
            foreach (string nucid in heavy)
            {
                int mass;
                string symbol, state;
                CascadeAtomicData.SplitNucid(nucid, out mass, out symbol, out state);
                for (int z = 1; z <= 100; z++)
                {
                    if (string.Equals(MaterialDatabase.SymbolOf(z), symbol,
                                      StringComparison.OrdinalIgnoreCase))
                    {
                        impostors++;
                        Console.WriteLine("    ⛔ {0}: символ {1} ЕСТЬ в базе (Z={2}), значит дело не в пределе",
                                          nucid, symbol, z);
                        break;
                    }
                }
            }

            if (impostors > 0)
            {
                failed++;
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "ИТОГ: разбор имени сошёлся везде, где данные это позволяют."
                : "ИТОГ: ⛔ есть расхождения, см. выше.");
            return failed == 0 ? 0 : 1;
        }
    }
}
