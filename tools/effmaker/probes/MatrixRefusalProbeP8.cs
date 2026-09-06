using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Целевая платформа процесса (.NETFramework 4.8) объявлена ОБЩИМ довеском
// `_TargetFramework.cs` — он компилируется в каждую пробу (`T237`, 06.09.2026);
// свой атрибут здесь дал бы CS0579. Значение печатается в шапке.

namespace MatrixRefusalProbeP8
{
    /// <summary>
    /// Полоса П8а захода 05.09.2026: форма «Матрица отклика» — `A50` и `A244`.
    ///
    /// ── `A50`, ОТКАЗ ГОВОРИТ СЛОВАМИ (решение Amber 05.09.2026: «матрица
    ///    формата 6, нужен 7 — пересчитайте»).
    ///
    ///    Отказ чтения называет себя ЗНАЧЕНИЕМ (`MatrixRefusal`) с 03.09.2026,
    ///    но обращений к ресурсам в `ResponseMatrix.cs` ноль — слов он не
    ///    произносит. Их обязан произнести ПОТРЕБИТЕЛЬ. Разбор (`FsaAnalysisSession`)
    ///    научился этому 03.09; форма — здесь.
    ///
    ///    ⚠ ПОСЫЛКА ЗАДАНИЯ «прежний код отдаёт null / молчание» НЕВЕРНА, и
    ///    проба это МЕРИТ, а не обходит: прежняя форма на файле формата 6
    ///    говорила `ResponseMatrixStateStaleVersions` — «устарела: посчитана
    ///    другим поколением ПЕРЕНОСА». Молчания не было; была НЕ ТА ПРИЧИНА и
    ///    ни одного номера формата в предложении. Поэтому положительный
    ///    контроль устроен по существу: прежняя строка (она осталась в
    ///    ресурсах и печатается в других состояниях) обязана НЕ нести ни «6»,
    ///    ни «7», а новая — нести оба.
    ///
    /// ── `A244`, РАЗДЕЛИТЕЛЬ ДРОБНОЙ ЧАСТИ ВСЕГДА ТОЧКА и ГРУППИРОВКИ НЕТ.
    ///
    ///    Весь текст формы снимается на четырёх культурах потока (инвариант,
    ///    `ru-RU`, `de-DE`, `en-US`) и обязан совпасть посимвольно. Единственное
    ///    исключение — ДАТА расчёта: у неё нет дробной части, правило Amber её
    ///    не касается, а инвариант дал бы русскому пользователю «09/05/2026».
    ///    Она маскируется и печатается отдельно; всё остальное сравнивается
    ///    как есть, поэтому «а вдруг разошлось что-то ещё» проверено, а не
    ///    обещано.
    ///
    ///     matrixrefusalprobep8 [--geometry=X.in] [--out=файл]
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0.
    ///
    /// ⛔ СЧЁТА МАТРИЦ ЗДЕСЬ НЕТ НАРОЧНО (`A50`: «счёт матриц сегодня
    ///    запрещён»). Сцена — матрица, СОБРАННАЯ В ПАМЯТИ и записанная
    ///    `ResponseMatrix.Save`: те же байты, что у настоящей, детерминированно
    ///    и за миллисекунды. Цена решения названа честно: два места печати
    ///    внутри `async void ComputeClick` (предупреждение о шуме континуума и
    ///    «Готово за …») пробой не достаются — они разбираются в журнале.
    /// </summary>
    static class Program
    {
        static readonly StringBuilder Log = new StringBuilder();
        static int failures;

        /// <summary>Три чужие культуры потока, как в `CultureProbeO14`.</summary>
        static readonly string[] Foreign = { "ru-RU", "de-DE", "en-US" };

        /// <summary>Дата расчёта сцены — постоянная, иначе сравнивать нечего.</summary>
        static readonly DateTime Created = new DateTime(2026, 9, 5, 9, 30, 0, DateTimeKind.Utc);

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string outPath = null, geometryPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                // `A263`: неизвестное ИМЯ ключа — отказ, а не молчание.
                else
                {
                    Console.WriteLine("не знаю ключа: " + a);
                    return 2;
                }
            }

            Header();

            geometryPath = geometryPath ?? FindGeometry();
            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Say("⛔ геометрии нет: нужен --geometry=<файл .in>; замер невозможен");
                Finish(outPath);
                return 2;
            }

            Say("геометрия сцены:   " + geometryPath);
            Application.EnableVisualStyles();
            GlobalConfigManager.GetInstance();

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            var options = new ResponseMatrixOptions
            {
                MinEnergyKev = 5.0,
                MaxEnergyKev = 3000.0,
                NodeCount = 8,
                BinKev = 2.0,
                // РЕЖЕ ШТАТНОГО (3 млн, `A39`) НАРОЧНО: так в подробностях
                // появляется строка `DescribeInheritedHistories`, а в ней и
                // стоял формат `N0` с разделителем разрядов (`A244`).
                Histories = 300000
            };

            var config = new EfficiencyConfigData("проба П8а")
            {
                Guid = "p8a-" + Guid.NewGuid().ToString("N"),
                Geometry = geometry
            };
            string path = ResponseMatrixStore.PathOf(config.Guid);
            Say("файл сцены:        " + path);
            Say("");

            try
            {
                A50(config, geometry, options, path);
                A244(config, path);
            }
            finally
            {
                try { ResponseMatrixStore.Delete(config.Guid); } catch (Exception) { }
            }

            Say("");
            Say(failures == 0 ? "ВСЕ СОШЛИСЬ" : "⛔ РАСХОЖДЕНИЙ: " + Num(failures));
            Finish(outPath);
            return failures == 0 ? 0 : 1;
        }

        // ══════════════════════════════════════════════════════════════════
        //  `A50` — ОТКАЗ ГОВОРИТ СЛОВАМИ
        // ══════════════════════════════════════════════════════════════════

        static void A50(EfficiencyConfigData config, GeometryModel geometry,
                        ResponseMatrixOptions options, string path)
        {
            Say("══════════════════════════════════════════════════════════════");
            Say("`A50`: ФОРМА НАЗЫВАЕТ ПРИЧИНУ ОТКАЗА СЛОВАМИ");
            Say("══════════════════════════════════════════════════════════════");

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            SetUiCulture("en-US");

            // ── Сцена: годная матрица. Она же ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ раздела:
            //    форма, которая жалуется ВСЕГДА, не измеряет ничего.
            Synthetic(geometry, options).Save(path);
            int format = ReadFormat(path);
            Check(format == ResponseMatrix.FormatVersion,
                  "сцена записана нынешним форматом: в файле " + Num(format)
                  + ", код читает " + Num(ResponseMatrix.FormatVersion));

            using (var form = new ResponseMatrixForm(config))
            {
                string state = TextOf(form, "stateLabel");
                Check(state == Resources.ResponseMatrixStateValid,
                      "[полож. контроль] годный файл: «" + state + "»");
            }

            // ── Формат 6: ровно то, что лежит у людей после подъёма формата —
            //    наш файл, наша матрица, читать нельзя.
            int old = ResponseMatrix.FormatVersion - 1;
            WriteFormat(path, old);

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix refused = ResponseMatrix.Load(path, out refusal, out fileFormat);
            Check(refused == null && refusal == MatrixRefusal.OldFormat && fileFormat == old,
                  "чтение назвало отказ ЗНАЧЕНИЕМ: «" + refusal + "», формат файла " + Num(fileFormat));

            string oldWords;
            using (var form = new ResponseMatrixForm(config))
            {
                oldWords = TextOf(form, "stateLabel");
                string want = string.Format(CultureInfo.InvariantCulture,
                                            Resources.ResponseMatrixStateOldFormat,
                                            old, ResponseMatrix.FormatVersion);
                Check(oldWords == want, "форма СКАЗАЛА СЛОВАМИ: «" + oldWords + "»");
                Check(oldWords.Contains(Num(old)) && oldWords.Contains(Num(ResponseMatrix.FormatVersion)),
                      "в словах стоят ОБА номера формата: " + Num(old) + " и "
                      + Num(ResponseMatrix.FormatVersion));
                Check(!string.IsNullOrEmpty(oldWords), "слова не пусты (не молчание)");
                Check(ButtonText(form, "computeButton") == Resources.ResponseMatrixRecompute,
                      "кнопка зовёт ПЕРЕСЧИТАТЬ: «" + ButtonText(form, "computeButton") + "»");
            }

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПО ПРЕЖНЕМУ КОДУ. Прежняя форма на этом
            //    же файле говорила `ResponseMatrixStateStaleVersions`. Молчания
            //    не было — была не та причина (ПЕРЕНОС вместо ФОРМАТА) и ни
            //    одного номера формата. Обе половины проверяются числом.
            string was = Resources.ResponseMatrixStateStaleVersions;
            Check(!was.Contains(Num(old)) && !was.Contains(Num(ResponseMatrix.FormatVersion)),
                  "[полож. контроль] прежняя строка номеров формата НЕ несла: «" + was + "»");
            Check(oldWords != was, "новая строка отличается от прежней");

            // ── Обрубок нынешнего формата. Прежний код и на нём говорил
            //    «устарела: другое поколение», хотя оба поколения СОШЛИСЬ.
            WriteFormat(path, ResponseMatrix.FormatVersion);
            long full = new FileInfo(path).Length;
            Truncate(path, full / 2);
            ResponseMatrix.Load(path, out refusal, out fileFormat);
            Check(refusal == MatrixRefusal.Unreadable,
                  "обрубок " + Num(full / 2) + " из " + Num(full) + " байт: отказ «" + refusal + "»");
            using (var form = new ResponseMatrixForm(config))
            {
                string state = TextOf(form, "stateLabel");
                Check(state == Resources.ResponseMatrixStateUnreadable,
                      "форма про обрубок: «" + state + "»");
                Check(state != Resources.ResponseMatrixStateStaleVersions,
                      "[полож. контроль] это НЕ прежняя строка про поколение переноса");
            }

            // ── Чужой файл на месте матрицы.
            Synthetic(geometry, options).Save(path);
            WriteMagic(path, "XXXX");
            ResponseMatrix.Load(path, out refusal, out fileFormat);
            Check(refusal == MatrixRefusal.NotOurs, "чужой файл: отказ «" + refusal + "»");
            using (var form = new ResponseMatrixForm(config))
            {
                string state = TextOf(form, "stateLabel");
                Check(state == Resources.ResponseMatrixStateNotOurs, "форма про чужой файл: «" + state + "»");
                Check(ButtonText(form, "computeButton") == Resources.ResponseMatrixCompute,
                      "кнопка зовёт ПОСЧИТАТЬ: «" + ButtonText(form, "computeButton") + "»");
            }

            // ── Файла нет вовсе.
            ResponseMatrixStore.Delete(config.Guid);
            using (var form = new ResponseMatrixForm(config))
            {
                string state = TextOf(form, "stateLabel");
                Check(state == Resources.ResponseMatrixStateMissing, "файла нет: «" + state + "»");
            }

            // ── ОБА ЯЗЫКА. Слова обязаны быть на языке интерфейса, а не одни
            //    английские: полдела — сказать, вторая половина — сказать так,
            //    чтобы прочли.
            Synthetic(geometry, options).Save(path);
            WriteFormat(path, old);
            string ru = null, en = null;
            foreach (string ui in new[] { "ru-RU", "en-US" })
            {
                SetUiCulture(ui);
                using (var form = new ResponseMatrixForm(config))
                {
                    string state = TextOf(form, "stateLabel");
                    if (ui == "ru-RU") ru = state; else en = state;
                    Check(!string.IsNullOrEmpty(state)
                          && state.Contains(Num(old))
                          && state.Contains(Num(ResponseMatrix.FormatVersion)),
                          "язык " + ui + ": «" + state + "»");
                }
            }

            Check(ru != en, "[полож. контроль] языки РАЗНЫЕ — сателлит `ru` на месте и читается");
            SetUiCulture("en-US");
            Say("");
        }

        // ══════════════════════════════════════════════════════════════════
        //  `A244` — ТОЧКА НА ЛЮБОЙ КУЛЬТУРЕ, ГРУППИРОВКИ НЕТ
        // ══════════════════════════════════════════════════════════════════

        static void A244(EfficiencyConfigData config, string path)
        {
            Say("══════════════════════════════════════════════════════════════");
            Say("`A244` П8а: ЧИСЛА ФОРМЫ МАТРИЦЫ — ТОЧКА НА ЛЮБОЙ КУЛЬТУРЕ");
            Say("══════════════════════════════════════════════════════════════");

            // Годная матрица: у неё печатается ВСЁ — состояние, поколения,
            // подробности (узлы, края сетки, бин, истории, размеры, дата,
            // секунды, отпечаток тела) и строка про унаследованные истории.
            WriteFormat(path, ResponseMatrix.FormatVersion);
            SetUiCulture("en-US");

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string reference = Screen(config);
            Say("эталон (инвариант):");
            foreach (string line in reference.Split('\n')) Say("    " + line);

            foreach (string name in Foreign)
            {
                Say("");
                Say("── ПЛЕЧО " + name + " (подмены разделителя НЕТ) ──");
                CultureInfo os = CultureInfo.GetCultureInfo(name);
                Thread.CurrentThread.CurrentCulture = os;
                bool comma = os.NumberFormat.NumberDecimalSeparator == ",";

                // Положительный контроль плеча: без него «совпало» значило бы
                // только «культура не поменялась».
                string bare = (12.5).ToString("F1");
                Check(bare == (comma ? "12,5" : "12.5"),
                      "[полож. контроль] `(12.5).ToString(\"F1\")` без культуры = «" + bare + "»");
                string bareGroup = (3000000).ToString("N0");
                Check(bareGroup != "3000000",
                      "[полож. контроль] `(3000000).ToString(\"N0\")` без культуры = «"
                      + bareGroup + "» — группировка на плече ЖИВА");

                string got = Screen(config);
                if (got == reference)
                {
                    Say("  ЭКРАН формы: совпал с эталоном инварианта посимвольно");
                }
                else
                {
                    Say("  ⛔ ЭКРАН формы РАЗОШЁЛСЯ");
                    Say("      здесь : " + got.Replace("\n", " ⏎ "));
                    Say("      эталон: " + reference.Replace("\n", " ⏎ "));
                    failures++;
                }

                // ГРУППИРОВКИ НЕТ (решение Amber 05.09.2026). Проверяется не
                // «нет запятой» (запятая бывает в тексте), а «нет РАЗДЕЛИТЕЛЯ
                // ГРУПП внутри числа»: цифра, разделитель, три цифры.
                string grouped = FirstGrouping(got);
                Check(grouped == null, grouped == null
                      ? "разделителя групп в тексте нет"
                      : "⛔ разделитель групп: «" + grouped + "»");

                // ОБРАТНОЕ ПЛЕЧО. Печать и разбор одного файла — в одну правку;
                // разбора чисел в самой форме нет вовсе (все шесть полей ввода
                // целые, `decimals = 0`), поэтому обратная сторона проверяется
                // так: КАЖДОЕ число, напечатанное этим плечом, разбирается
                // инвариантом обратно и даёт своё значение.
                int parsed, bad;
                Backward(got, out parsed, out bad);
                Check(bad == 0, "ОБРАТНОЕ: " + Num(parsed) + " чисел текста разобраны инвариантом, "
                                + (bad == 0 ? "отказов нет" : "ОТКАЗОВ " + Num(bad)));

                // ДАТА — единственное, что культуре потока оставлено нарочно.
                Say("  дата расчёта на этом плече: «" + LocalDate() + "» (по культуре ОС, нарочно)");

                // ГРУППИРОВКА В ПОЛЕ ВВОДА (`ThousandsSeparator`) — снята.
                using (var form = new ResponseMatrixForm(config))
                {
                    var box = (NumericUpDown)FieldOf(form, "historiesBox");
                    // ⛔ СПРАШИВАЕТСЯ ТЕКСТ САМОГО ПОЛЯ (`box.Text`), а не число,
                    //    отформатированное пробой. Прежде здесь стояло
                    //    `box.Value.ToString("F0", инвариант)`: проба печатала
                    //    сама и проверяла свою же печать, а строка отчёта при
                    //    этом говорила «на экране» — на обратном плече
                    //    (`ThousandsSeparator = true`) она честно печатала
                    //    «3000000», хотя человек видел бы «3 000 000».
                    string shown = box.Text;
                    string groupedField = FirstGrouping(shown);
                    Check(!box.ThousandsSeparator && groupedField == null,
                          "поле «Историй на узел»: разделитель групп "
                          + (box.ThousandsSeparator ? "ЕСТЬ" : "снят") + ", на экране «"
                          + shown + "»");
                }
            }

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Say("");
        }

        /// <summary>
        /// Весь печатаемый текст формы одной строкой, ДАТА заменена меткой.
        ///
        /// Берётся у ЖИВОЙ формы (три надписи плюс `Duration`, вызванный
        /// отражением), а не собирается пробой из ресурсов: проба, печатающая
        /// сама, не мерит ничего.
        /// </summary>
        static string Screen(EfficiencyConfigData config)
        {
            var text = new StringBuilder();
            using (var form = new ResponseMatrixForm(config))
            {
                text.Append("состояние: ").Append(TextOf(form, "stateLabel")).Append('\n');
                text.Append("поколения: ").Append(TextOf(form, "versionsLabel")).Append('\n');
                text.Append("подробности: ")
                    .Append(((string)FieldOf(form, "detailsText") ?? "").Replace(Environment.NewLine, " ⏎ "))
                    .Append('\n');

                // Строка хода: `ShowProgress` печатает узлы и энергию.
                var progress = new ResponseMatrixProgress
                {
                    StartedNodes = 12,
                    SettledNodes = 8,
                    TotalNodes = 140,
                    LastEnergyKev = 1234.5,
                    Done = 12,
                    Total = 140
                };
                typeof(ResponseMatrixForm)
                    .GetMethod("ShowProgress", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { progress });
                text.Append("ход: ").Append(TextOf(form, "progressLabel")).Append('\n');
            }

            MethodInfo duration = typeof(ResponseMatrixForm)
                .GetMethod("Duration", BindingFlags.Static | BindingFlags.NonPublic);
            text.Append("длительность: ")
                .Append(duration.Invoke(null, new object[] { 3906.5 }))
                .Append(' ')
                .Append(duration.Invoke(null, new object[] { 66.25 }));

            return text.ToString().Replace(LocalDate(), "<ДАТА>");
        }

        /// <summary>Дата сцены так, как её печатает форма на нынешней культуре.</summary>
        static string LocalDate()
        {
            return Created.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Первый разделитель ГРУПП в тексте: цифра, затем `,`, `.`, пробел или
        /// неразрывный пробел, затем ровно три цифры и не цифра. Точка с одной
        /// или двумя цифрами после — дробная часть, и она законна.
        /// </summary>
        static string FirstGrouping(string text)
        {
            for (int i = 1; i + 3 < text.Length; i++)
            {
                char c = text[i];
                // Пробел обычный, неразрывный и узкий неразрывный — все три
                // ходят разделителем групп у живых культур.
                if (c != ',' && c != '.' && c != ' '
                    && c != ' ' && c != ' ') continue;
                if (!char.IsDigit(text[i - 1])) continue;
                if (!char.IsDigit(text[i + 1]) || !char.IsDigit(text[i + 2]) || !char.IsDigit(text[i + 3])) continue;
                if (i + 4 < text.Length && char.IsDigit(text[i + 4])) continue;
                int from = Math.Max(0, i - 4);
                return text.Substring(from, Math.Min(text.Length - from, 12));
            }

            return null;
        }

        /// <summary>
        /// Обратная сторона: каждое число текста обязано разобраться
        /// ИНВАРИАНТОМ. Число — непрерывный ряд цифр, возможно с одной точкой.
        /// </summary>
        static void Backward(string text, out int parsed, out int bad)
        {
            parsed = 0;
            bad = 0;
            int i = 0;
            while (i < text.Length)
            {
                if (!char.IsDigit(text[i])) { i++; continue; }
                int start = i;
                bool dot = false;
                while (i < text.Length
                       && (char.IsDigit(text[i])
                           || (text[i] == '.' && !dot && i + 1 < text.Length && char.IsDigit(text[i + 1]))))
                {
                    if (text[i] == '.') dot = true;
                    i++;
                }

                string token = text.Substring(start, i - start);
                double value;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    parsed++;
                }
                else
                {
                    bad++;
                    Say("  ⛔ инвариантом не разобрано: «" + token + "»");
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Сцена
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Матрица, СОБРАННАЯ В ПАМЯТИ: те же байты файла, что у настоящей, но
        /// без счёта (`A50`: «счёт матриц сегодня запрещён»). Клеймо берётся у
        /// самого приложения (`ComputeStamp`), поэтому файл выходит ГОДНЫМ для
        /// этой геометрии — иначе положительный контроль раздела был бы пуст.
        /// </summary>
        static ResponseMatrix Synthetic(GeometryModel geometry, ResponseMatrixOptions options)
        {
            double[] grid = options.BuildGrid();
            var rows = new float[grid.Length][];
            for (int i = 0; i < grid.Length; i++)
            {
                var row = new float[64];
                for (int b = 0; b < row.Length; b++)
                {
                    row[b] = (float)((i + 1) * 1e-4 + b * 1e-6);
                }

                rows[i] = row;
            }

            return new ResponseMatrix
            {
                Stamp = ResponseMatrix.ComputeStamp(geometry, options),
                Options = options,
                Energies = grid,
                ChannelRows = new[] { rows },
                BinKev = options.BinKev,
                Histories = options.Histories,
                CreatedUtc = Created,
                BuildSeconds = 306.0,
                ContinuumRelativeError = 11.25,
                ContinuumWeightedError = 4.57
            };
        }

        static int ReadFormat(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                reader.ReadBytes(4);
                return reader.ReadInt32();
            }
        }

        static void WriteFormat(string path, int format)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                stream.Position = 4;
                writer.Write(format);
            }
        }

        static void WriteMagic(string path, string magic)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.Position = 0;
                byte[] bytes = Encoding.ASCII.GetBytes(magic);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        static void Truncate(string path, long length)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(length);
            }
        }

        /// <summary>Геометрия по умолчанию: первый `.in` из `LSRM Geometries\Models` над пробой.</summary>
        static string FindGeometry()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int up = 0; up < 8 && dir != null; up++)
            {
                string models = Path.Combine(dir, "LSRM Geometries", "Models");
                if (Directory.Exists(models))
                {
                    string[] files = Directory.GetFiles(models, "*.in");
                    Array.Sort(files, StringComparer.Ordinal);
                    if (files.Length > 0)
                    {
                        return files[0];
                    }
                }

                DirectoryInfo parent = Directory.GetParent(dir.TrimEnd(Path.DirectorySeparatorChar));
                dir = parent == null ? null : parent.FullName;
            }

            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Оснастка
        // ══════════════════════════════════════════════════════════════════

        static void Header()
        {
            Say("== П8а: `A50` отказ словами + `A244` точка в форме матрицы ==");
            Say("");
            Assembly app = typeof(GlobalConfigManager).Assembly;
            Say("сборка приложения: " + app.Location);
            try
            {
                Say("собрана:           " + File.GetLastWriteTime(app.Location)
                                                .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Say("собрана:           не прочитана: " + ex.Message); }

            bool noFlow;
            bool known = AppContext.TryGetSwitch("Switch.System.Globalization.NoAsyncCurrentCulture", out noFlow);
            Say("процесс пробы:     целевая платформа входной сборки="
                + (AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName ?? "НЕ ОБЪЯВЛЕНА")
                + ", NoAsyncCurrentCulture=" + (known ? noFlow.ToString() : "по умолчанию платформы"));
            Say("культура ОС:       " + CultureInfo.InstalledUICulture.Name
                + ", формат матрицы кода=" + Num(ResponseMatrix.FormatVersion)
                + ", физика=" + Num(ResponseMatrix.PhysicsVersion));
        }

        static void SetUiCulture(string name)
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(name);
        }

        static string TextOf(object form, string field)
        {
            var control = (Control)FieldOf(form, field);
            return control == null ? null : control.Text;
        }

        static string ButtonText(object form, string field)
        {
            return TextOf(form, field);
        }

        static object FieldOf(object target, string name)
        {
            FieldInfo info = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return info == null ? null : info.GetValue(target);
        }

        static string Num(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        static void Check(bool ok, string message)
        {
            Say((ok ? "  ✔ " : "  ⛔ ") + message);
            if (!ok) failures++;
        }

        static void Say(string line)
        {
            Console.WriteLine(line);
            Log.AppendLine(line);
        }

        static void Finish(string outPath)
        {
            if (string.IsNullOrEmpty(outPath))
            {
                return;
            }

            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(outPath, Log.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("вывод не записан: " + ex.Message);
            }
        }
    }
}
