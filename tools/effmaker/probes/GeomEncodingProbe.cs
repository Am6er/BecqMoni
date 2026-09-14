using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GeomEncodingProbe
{
    /// <summary>
    /// Кодировка файлов геометрии и её цена в ОТПЕЧАТКЕ (`A161`).
    ///
    /// Проба отвечает на три вопроса разом, и все три — числом:
    ///
    /// 1. **Имя вещества доезжает до модели целиком.** Ожидаемое значение
    ///    берётся НЕ у приложения, а СВОИМ разбором файла: проба сама читает
    ///    байты, сама распознаёт кодировку и сама вынимает все `*.MName`.
    ///    Совпадение с тем, что вернул `GeometryModel.Load`, — единственное
    ///    доказательство, которое не зависит от проверяемого кода. Отдельно
    ///    ловятся `U+FFFD` (порча при чтении) и `?` (порча при записи).
    /// 2. **Круг «открыл — сохранил» не двигает отпечаток.** Модель читается,
    ///    пишется во ВРЕМЕННЫЙ каталог и читается снова; сравниваются два
    ///    `ResponseMatrix.ComputeStamp`. ⛔ Корпусные файлы не переписываются
    ///    НИКОГДА — цель записи всегда во временном каталоге.
    /// 3. **Сам отпечаток печатается**, чтобы две сборки (до правки и после)
    ///    можно было свести поимённо: цена правки — это список геометрий, у
    ///    которых отпечаток сменился ОДНОКРАТНО.
    ///
    /// ⛔ Положительный контроль обязателен, иначе «сошлось» ничего не стоит:
    /// `--break=mojibake` подставляет ровно ту порчу, против которой проба
    /// стоит (кириллица имени → `U+FFFD`), `--break=stamp` двигает отпечаток
    /// после круга. Проба ОБЯЗАНА отказать на обоих.
    /// </summary>
    static class Program
    {
        static string breakage = "";
        static int broken;

        static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception)
            {
                // Консоль без UTF-8 — не повод не работать: отчёт всё равно
                // пишется файлом, и он первичен.
            }

            var dirs = new List<string>();
            string tmp = Path.Combine(Path.GetTempPath(),
                                      "bq_geomenc_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string report = "";
            string label = "";

            foreach (string a in args)
            {
                if (a.StartsWith("--dirs=", StringComparison.Ordinal))
                {
                    foreach (string d in a.Substring(7).Split(';'))
                    {
                        if (d.Trim().Length > 0)
                        {
                            dirs.Add(d.Trim());
                        }
                    }
                }
                else if (a.StartsWith("--tmp=", StringComparison.Ordinal))
                {
                    tmp = a.Substring(6);
                }
                else if (a.StartsWith("--report=", StringComparison.Ordinal))
                {
                    report = a.Substring(9);
                }
                else if (a.StartsWith("--label=", StringComparison.Ordinal))
                {
                    label = a.Substring(8);
                }
                else if (a.StartsWith("--break=", StringComparison.Ordinal))
                {
                    breakage = a.Substring(8);
                }
                else
                {
                    Console.Error.WriteLine("не знаю ключа: " + a);
                    return 1;
                }
            }

            if (dirs.Count == 0)
            {
                Console.Error.WriteLine(
                    "GeomEncodingProbe --dirs=<кат1>;<кат2> [--tmp=<кат>] [--report=<файл>]");
                Console.Error.WriteLine("                  [--label=<метка>] [--break=mojibake|stamp]");
                return 1;
            }

            if (breakage.Length > 0)
            {
                Console.WriteLine("### ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: подставлена порча «{0}» — проба ОБЯЗАНА отказать",
                                  breakage);
            }

            Directory.CreateDirectory(tmp);

            var lines = new List<string>();
            var files = new List<string>();
            foreach (string d in dirs)
            {
                if (!Directory.Exists(d))
                {
                    Console.Error.WriteLine("НЕТ КАТАЛОГА: " + d);
                    return 1;
                }

                string[] found = Directory.GetFiles(d, "*.in");
                Array.Sort(found, StringComparer.Ordinal);
                files.AddRange(found);
            }

            int badName = 0, badStamp = 0, withCyr = 0, notUtf8 = 0, isUtf8 = 0, ascii = 0;
            foreach (string path in files)
            {
                byte[] data = File.ReadAllBytes(path);
                string ownEncoding = OwnGuess(data);
                if (ownEncoding == "ASCII")
                {
                    ascii++;
                }
                else if (ownEncoding == "UTF8")
                {
                    isUtf8++;
                }
                else
                {
                    notUtf8++;
                }

                HashSet<string> expected = OwnNames(data, ownEncoding);
                bool cyr = false;
                foreach (string n in expected)
                {
                    foreach (char c in n)
                    {
                        if (c >= '\u0400' && c <= '\u04FF')
                        {
                            cyr = true;
                        }
                    }
                }

                if (cyr)
                {
                    withCyr++;
                }

                GeometryModel a = GeometryModel.Load(path);
                Mojibake(a);
                List<string> got = Names(a);

                // 1. имя доехало целиком — сверка со СВОИМ разбором файла
                var lost = new List<string>();
                foreach (string n in got)
                {
                    if (n.Length == 0)
                    {
                        continue;
                    }

                    if (n.IndexOf('\uFFFD') >= 0 || n.IndexOf('?') >= 0 || !expected.Contains(n))
                    {
                        lost.Add(n);
                    }
                }

                // 2. круг «открыл — сохранил»
                //
                // ⛔ Цель нумеруется, а не зовётся именем исходника: у каталогов
                // есть ТЁЗКИ — `Nano16Pro.in` лежит и в `tools/effmaker/models`,
                // и в `LSRM Geometries/Models`, всего таких пар семь. Общее имя
                // в одном временном каталоге затирало бы одну сцену другой.
                string target = Path.Combine(tmp, lines.Count.ToString(CultureInfo.InvariantCulture)
                                                  + "_" + Path.GetFileName(path));
                GeometryWriter.Save(a, target);
                GeometryModel b = GeometryModel.Load(target);
                BreakStamp(b);

                var options = new ResponseMatrixOptions();
                string sa = ResponseMatrix.ComputeStamp(a, options);
                string sb = ResponseMatrix.ComputeStamp(b, options);

                if (lost.Count > 0)
                {
                    badName++;
                }

                if (!string.Equals(sa, sb, StringComparison.Ordinal))
                {
                    badStamp++;
                }

                // ⛔ В отчёт идёт ПОЛНЫЙ путь, а не имя файла. Тёзки (см. выше)
                // при сведении двух отчётов по имени слипаются, и 66 строк
                // становятся 59 молча — поймано 05.09.2026 на первом же сведении.
                lines.Add(string.Join("\t",
                    path,
                    ownEncoding,
                    sa,
                    sb,
                    string.Equals(sa, sb, StringComparison.Ordinal) ? "круг-ок" : "КРУГ-СДВИНУЛ",
                    lost.Count == 0 ? "имена-ок" : "ИМЕНА-ПОТЕРЯНЫ:" + string.Join(",", lost.ToArray()),
                    string.Join("|", got.ToArray()),
                    string.Join("|", new List<string>(expected).ToArray())));
            }

            var text = new StringBuilder();
            text.Append("# GeomEncodingProbe").Append(label.Length > 0 ? " [" + label + "]" : "")
                .Append("\r\n");
            text.Append("# файл\tкодировка\tотпечаток\tотпечаток-после-круга\tкруг\tимена\tчитано\tожидано\r\n");
            foreach (string l in lines)
            {
                text.Append(l).Append("\r\n");
            }

            if (report.Length > 0)
            {
                string dir = Path.GetDirectoryName(report);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(report, text.ToString(), new UTF8Encoding(false));
                Console.WriteLine("отчёт: {0}", report);
            }

            Console.WriteLine("файлов: {0}   (не-UTF8/однобайтных: {1}, UTF-8: {2}, чистый ASCII: {3})",
                              files.Count, notUtf8, isUtf8, ascii);
            Console.WriteLine("с кириллицей в имени вещества: {0}", withCyr);
            Console.WriteLine("имя потеряно у: {0}", badName);
            Console.WriteLine("круг сдвинул отпечаток у: {0}", badStamp);

            if (breakage.Length > 0)
            {
                Console.WriteLine("### контроль «{0}»: испорчено сцен {1}, отказов имени {2}, отказов круга {3}",
                                  breakage, broken, badName, badStamp);
                if (broken == 0)
                {
                    Console.WriteLine("### ⛔ ПОРЧА НИ К ЧЕМУ НЕ ПРИЛОЖИЛАСЬ — контроль НЕ СОСТОЯЛСЯ");
                    return 3;
                }
            }

            bool ok = badName == 0 && badStamp == 0;
            Console.WriteLine(ok ? "ВСЁ СОШЛОСЬ" : "ОТКАЗ");
            return ok ? 0 : 2;
        }

        static List<string> Names(GeometryModel g)
        {
            var list = new List<string>();
            list.Add(g.Crystal.Name);
            list.Add(g.Reflector.Name);
            list.Add(g.Cladding.Name);
            list.Add(g.BeakerWall.Name);
            list.Add(g.Source.Name);
            return list;
        }

        /// <summary>
        /// СВОЙ разбор кодировки — независимо от приложения.
        ///
        /// ⚠ Порядок именно такой: BOM сильнее всего, чистый ASCII нейтрален
        /// (обе кодировки дают одно и то же), и только строгий разбор UTF-8
        /// отличает настоящий UTF-8 от однобайтной кириллицы. Обратное правило
        /// («сперва cp1251») было бы неотличимо: cp1251 принимает ЛЮБЫЕ байты.
        /// </summary>
        static string OwnGuess(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                return "UTF8";
            }

            bool high = false;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] >= 0x80)
                {
                    high = true;
                    break;
                }
            }

            if (!high)
            {
                return "ASCII";
            }

            try
            {
                new UTF8Encoding(false, true).GetString(data);
                return "UTF8";
            }
            catch (ArgumentException)
            {
                return "CP1251";
            }
        }

        static readonly Regex MName = new Regex(@"^\s*[A-Za-z_][A-Za-z0-9_\[\]\.]*\.MName\s*=\s*(.+?)\s*$",
                                                RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>Все имена веществ, вынутые из файла СВОИМ разбором.</summary>
        static HashSet<string> OwnNames(byte[] data, string guess)
        {
            Encoding enc = guess == "CP1251" ? Encoding.GetEncoding(1251) : new UTF8Encoding(false);
            string text;
            using (var reader = new StreamReader(new MemoryStream(data), enc, true))
            {
                text = reader.ReadToEnd();
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in MName.Matches(text))
            {
                string v = m.Groups[1].Value;
                int comment = v.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0)
                {
                    v = v.Substring(0, comment);
                }

                set.Add(v.Trim());
            }

            set.Add("");
            return set;
        }

        /// <summary>
        /// Порча «mojibake» — ровно та, которую даёт чтение однобайтного файла
        /// как UTF-8: кириллица имени превращается в `U+FFFD`. Вносится ПОСЛЕ
        /// чтения, то есть моделирует дефект, не трогая ни одного файла на
        /// диске.
        /// </summary>
        static void Mojibake(GeometryModel g)
        {
            if (breakage != "mojibake")
            {
                return;
            }

            foreach (GeometryMaterial m in new GeometryMaterial[]
                     { g.Crystal, g.Reflector, g.Cladding, g.BeakerWall, g.Source })
            {
                if (m == null || m.Name.Length == 0)
                {
                    continue;
                }

                byte[] raw = Encoding.GetEncoding(1251).GetBytes(m.Name);
                string spoiled = new UTF8Encoding(false).GetString(raw);
                if (!string.Equals(spoiled, m.Name, StringComparison.Ordinal))
                {
                    m.Name = spoiled;
                    broken++;
                }
            }
        }

        /// <summary>
        /// Порча «stamp» — сдвиг ПОСЛЕ круга. ⛔ Вносить её до записи нельзя:
        /// файл честно перенесёт порчу, обе стороны придут одинаковыми и
        /// контроль покажет пустоту (грабля `A131`, § «сторона порчи»).
        /// </summary>
        static void BreakStamp(GeometryModel g)
        {
            if (breakage != "stamp")
            {
                return;
            }

            g.Source.Name = g.Source.Name + "x";
            broken++;
        }
    }
}
