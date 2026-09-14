// `A119`: ПОКОЛЕНИЕ КРИВОЙ ЭФФЕКТИВНОСТИ НАЗЫВАЕТСЯ СЛОВАМИ.
//
// Что меряется. Решение «что сказать про поколения расчёта» живёт в статическом
// и чистом `DeviceConfigForm.GenerationNotes(клеймо кривой, поколение матрицы,
// поколение сборки)` — нарочно без окна, чтобы его можно было измерить, а не
// снимать подпись с формы. Проба ходит отражением: своей копии правила у неё
// нет НАРОЧНО, копия проверяла бы себя.
//
//     curvegenerationprobe
//
// Девять случаев решения + положительный контроль на двух культурах + высота
// подписи, в которую сообщение обязано поместиться, + заголовок матрицы,
// прочитанный `ResponseMatrix.PeekVersions` с диска.
//
// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ЗДЕСЬ — «У СОШЕДШИХСЯ ПОКОЛЕНИЙ РАЗБОР МОЛЧИТ»
// (случай 1) и «тексты двух языков РАЗЛИЧАЮТСЯ» (случай 9). Без первого проба
// прошла бы и на правиле «говорить всегда», без второго — на правиле,
// вернувшем оба плеча английскими (ловушка F18, 05.09.2026).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor.Probes
{
    static class CurveGenerationProbe
    {
        static int failures;

        static readonly MethodInfo Notes = typeof(DeviceConfigForm).GetMethod(
            "GenerationNotes", BindingFlags.NonPublic | BindingFlags.Static);

        static void Main()
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");

            if (Notes == null)
            {
                Console.WriteLine("НЕТ МЕТОДА DeviceConfigForm.GenerationNotes — мерить нечего");
                Environment.Exit(2);
            }

            Console.WriteLine("== поколение сборки ==");
            Console.WriteLine("  ResponseMatrix.PhysicsVersion = {0}, FormatVersion = {1}",
                              ResponseMatrix.PhysicsVersion.ToString(CultureInfo.InvariantCulture),
                              ResponseMatrix.FormatVersion.ToString(CultureInfo.InvariantCulture));

            Decisions();
            Cultures();
            LabelHeight();
            Header();

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + failures);
            Environment.Exit(failures == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------
        // Решение: сколько сказать и про какие числа
        // ------------------------------------------------------------------

        static void Decisions()
        {
            Console.WriteLine();
            Console.WriteLine("== решение ==");

            // 1. ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: всё сошлось — молчим.
            Case("всё поколения 16", Stamp(16), 16, 16, 0);

            // 2. Кривая отстала от сборки И от матрицы — ДВА разных сообщения.
            //    Ровно случай склада 11.09.2026: «Точка» и «Маринелли» прибора
            //    RadiaScan 701A, клеймо `phys=11`, матрица физики 16.
            Case("кривая 11, матрица 16, сборка 16", Stamp(11), 16, 16, 2, 11, 16, 11, 16);

            // 3. Кривая и матрица одного СТАРОГО поколения — сообщение одно:
            //    рядом они согласны, устарели обе.
            Case("кривая 11, матрица 11, сборка 16", Stamp(11), 11, 16, 1, 11, 16);

            // 4. Кривая нынешняя, матрица отстала — сообщение одно, про соседа.
            Case("кривая 16, матрица 12, сборка 16", Stamp(16), 12, 16, 1, 16, 12);

            // 5. Матрицы нет (склад молчит нулём) — про соседа сказать нечего.
            Case("кривая 11, матрицы нет", Stamp(11), 0, 16, 1, 11, 16);

            // 6. Кривая по измерениям: клейма нет вовсе.
            Case("клейма нет", "", 16, 16, 0);

            // 7. Клеймо без `phys=` (мусор или клеймо старше самих клейм):
            //    «поколение 0» было бы неправдой, поэтому молчим.
            Case("клеймо без phys=", "hist=200000; grid=40-3000 keV/34 std", 16, 16, 0);
            Case("клеймо-мусор", "*** не клеймо ***", 12, 16, 0);

            // 8. Кривая ВПЕРЕДИ сборки (сборку откатили) — тоже расхождение,
            //    и молчать про него нельзя: числа кривой считались физикой,
            //    которой в этой сборке нет.
            Case("кривая 17, сборка 16", Stamp(17), 17, 16, 1, 17, 16);
        }

        /// <summary>Клеймо кривой того же вида, что печатает `EfficiencyCalculation`.</summary>
        static string Stamp(int physics)
        {
            return string.Format(CultureInfo.InvariantCulture,
                                 "phys={0}; hist=200000; grid=40-3000 keV/34 std", physics);
        }

        /// <summary>
        /// Один случай. `want` — сколько сообщений ждём, `numbers` — какие числа
        /// обязаны прозвучать, попарно и по порядку сообщений.
        /// </summary>
        static void Case(string title, string stamp, int matrixPhysics, int buildPhysics,
                         int want, params int[] numbers)
        {
            List<string> got = Ask(stamp, matrixPhysics, buildPhysics);
            Console.WriteLine("  {0,-34} -> сообщений {1}", title,
                              got.Count.ToString(CultureInfo.InvariantCulture));
            foreach (string line in got)
            {
                Console.WriteLine("        {0}", line);
            }

            Check(got.Count == want, title + ": ждали сообщений "
                  + want.ToString(CultureInfo.InvariantCulture));
            if (got.Count != want)
            {
                return;
            }

            // ⛔ Числа обязаны быть В ТЕКСТЕ. Правило, вернувшее верное ЧИСЛО
            // сообщений с пустыми местами подстановки, прошло бы счёт строк.
            for (int i = 0; i < want && 2 * i + 1 < numbers.Length; i++)
            {
                string line = got[i];
                bool ok = HasNumber(line, numbers[2 * i]) && HasNumber(line, numbers[2 * i + 1]);
                Check(ok, string.Format(CultureInfo.InvariantCulture,
                                        "{0}: сообщение {1} обязано назвать {2} и {3}",
                                        title, i + 1, numbers[2 * i], numbers[2 * i + 1]));
            }
        }

        /// <summary>Число целым словом, а не куском другого числа.</summary>
        static bool HasNumber(string line, int value)
        {
            string want = value.ToString(CultureInfo.InvariantCulture);
            int from = 0;
            while (true)
            {
                int at = line.IndexOf(want, from, StringComparison.Ordinal);
                if (at < 0)
                {
                    return false;
                }

                bool leftOk = at == 0 || !char.IsDigit(line[at - 1]);
                int end = at + want.Length;
                bool rightOk = end >= line.Length || !char.IsDigit(line[end]);
                if (leftOk && rightOk)
                {
                    return true;
                }

                from = at + 1;
            }
        }

        static List<string> Ask(string stamp, int matrixPhysics, int buildPhysics)
        {
            object result = Notes.Invoke(null, new object[] { stamp, matrixPhysics, buildPhysics });
            List<string> lines = new List<string>();
            foreach (object item in (IEnumerable)result)
            {
                lines.Add((string)item);
            }

            return lines;
        }

        // ------------------------------------------------------------------
        // Две культуры
        // ------------------------------------------------------------------

        static void Cultures()
        {
            Console.WriteLine();
            Console.WriteLine("== две культуры ==");

            CultureInfo saved = Thread.CurrentThread.CurrentUICulture;
            List<string> en, ru;
            try
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                en = Ask(Stamp(11), 16, 16);
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
                ru = Ask(Stamp(11), 16, 16);
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = saved;
            }

            Check(en.Count == 2 && ru.Count == 2, "оба языка дают по два сообщения");
            if (en.Count != 2 || ru.Count != 2)
            {
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                Console.WriteLine("  en: {0}", en[i]);
                Console.WriteLine("  ru: {0}", ru[i]);
                // ⛔ Тексты обязаны РАЗЛИЧАТЬСЯ: непереведённая строка вернула бы
                // оба плеча английскими, и опыт «совпал» бы молча (ловушка F18).
                Check(!string.Equals(en[i], ru[i], StringComparison.Ordinal),
                      "сообщение " + (i + 1).ToString(CultureInfo.InvariantCulture)
                      + ": языки обязаны различаться");
                Check(HasCyrillic(ru[i]) && !HasCyrillic(en[i]),
                      "сообщение " + (i + 1).ToString(CultureInfo.InvariantCulture)
                      + ": кириллица только в русском");
                // Числа — инвариантом в обеих культурах: ни запятой, ни пробела
                // внутри (решение Amber 05.09.2026 про разделитель и группировку).
                Check(HasNumber(ru[i], 11) && HasNumber(ru[i], 16),
                      "сообщение " + (i + 1).ToString(CultureInfo.InvariantCulture)
                      + ": числа названы и по-русски");
            }
        }

        static bool HasCyrillic(string text)
        {
            foreach (char c in text)
            {
                if (c >= 'Ѐ' && c <= 'ӿ')
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Высота подписи
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ ПОДПИСЬ, НЕ ПОМЕСТИВШАЯСЯ В СВОЮ ВЫСОТУ, ОБРЕЗАЕТСЯ МОЛЧА — и
        /// починка `A119` выглядела бы сделанной, оставаясь невидимой. Высота
        /// считается `DeviceConfigForm.GenerationLabelHeight` от НАСТОЯЩЕГО
        /// текста при настоящей ширине вкладки (466 точек), и проба меряет
        /// именно её.
        ///
        /// Три обязательных наблюдения: пустой текст даёт РОВНО НОЛЬ (иначе
        /// пустая подпись отнимала бы место у чертежа); русская пара —
        /// БОЛЬШЕ английской или столько же (перевод длиннее на треть, и
        /// высота, застрявшая на английской, обрезала бы русскую); двойное
        /// сообщение — БОЛЬШЕ одиночного (иначе мерка стоит на месте и не
        /// меряет ничего).
        /// </summary>
        static void LabelHeight()
        {
            Console.WriteLine();
            Console.WriteLine("== высота подписи ==");

            MethodInfo measure = typeof(DeviceConfigForm).GetMethod(
                "GenerationLabelHeight", BindingFlags.NonPublic | BindingFlags.Static);
            if (measure == null)
            {
                Check(false, "нет DeviceConfigForm.GenerationLabelHeight");
                return;
            }

            const int Width = 490 - 2 * 12;      // ширина вкладки, как в построителе
            using (Font font = new Font("Microsoft Sans Serif", 8.25f))
            {
                CultureInfo saved = Thread.CurrentThread.CurrentUICulture;
                string en, ru;
                try
                {
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                    en = string.Join(Environment.NewLine, Ask(Stamp(11), 16, 16).ToArray());
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
                    ru = string.Join(Environment.NewLine, Ask(Stamp(11), 16, 16).ToArray());
                }
                finally
                {
                    Thread.CurrentThread.CurrentUICulture = saved;
                }

                int empty = (int)measure.Invoke(null, new object[] { "", font, Width });
                int hEn = (int)measure.Invoke(null, new object[] { en, font, Width });
                int hRu = (int)measure.Invoke(null, new object[] { ru, font, Width });
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
                string one = string.Join(Environment.NewLine, Ask(Stamp(11), 11, 16).ToArray());
                Thread.CurrentThread.CurrentUICulture = saved;
                int hOne = (int)measure.Invoke(null, new object[] { one, font, Width });

                Console.WriteLine("  пусто          -> {0} точек",
                                  empty.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("  одно (ru)      -> {0} точек",
                                  hOne.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("  два (en)       -> {0} точек",
                                  hEn.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("  два (ru)       -> {0} точек",
                                  hRu.ToString(CultureInfo.InvariantCulture));

                Check(empty == 0, "пустой текст — ровно ноль точек");
                Check(hEn > 0 && hRu > 0, "непустой текст — высота больше нуля");
                Check(hRu >= hEn, "русская пара не ниже английской");
                Check(hRu > hOne, "два сообщения выше одного — мерка не стоит на месте");
            }
        }

        // ------------------------------------------------------------------
        // Заголовок матрицы с диска
        // ------------------------------------------------------------------

        /// <summary>
        /// Поколение матрицы приходит из ЗАГОЛОВКА файла, и читает его
        /// `ResponseMatrix.PeekVersions`. Проба пишет заголовок сама — теми же
        /// тремя полями, какими его пишет `ResponseMatrix.Save`, — и проверяет,
        /// что прочитанное сходится; плюс два отказа: не наш файл и пустой guid.
        /// </summary>
        static void Header()
        {
            Console.WriteLine();
            Console.WriteLine("== заголовок матрицы ==");

            string dir = Path.Combine(Path.GetTempPath(),
                                      "bq-a119-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string good = Path.Combine(dir, "good.rmx");
                Write(good, "BQRM", 8, Stamp(13) + ";deadbeef");
                int format, physics;
                bool ok = ResponseMatrix.PeekVersions(good, out format, out physics);
                Console.WriteLine("  наш файл       -> {0}, формат {1}, физика {2}",
                                  ok ? "прочитан" : "отказ",
                                  format.ToString(CultureInfo.InvariantCulture),
                                  physics.ToString(CultureInfo.InvariantCulture));
                Check(ok && format == 8 && physics == 13, "заголовок нашего файла разобран");

                string alien = Path.Combine(dir, "alien.rmx");
                Write(alien, "ZZZZ", 8, Stamp(13));
                ok = ResponseMatrix.PeekVersions(alien, out format, out physics);
                Console.WriteLine("  чужая метка    -> {0}", ok ? "прочитан" : "отказ");
                Check(!ok, "файл с чужой меткой — отказ");

                ok = ResponseMatrix.PeekVersions(Path.Combine(dir, "нет.rmx"),
                                                 out format, out physics);
                Check(!ok, "файла нет — отказ");

                // Обёртка склада на пустом guid обязана отказать НЕ ТРОГАЯ путь.
                ok = ResponseMatrixStore.PeekVersions("", out format, out physics);
                Console.WriteLine("  пустой guid    -> {0}", ok ? "прочитан" : "отказ");
                Check(!ok && format == 0 && physics == 0, "пустой guid — отказ и нули");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { }
            }
        }

        static void Write(string path, string magic, int format, string stamp)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(Encoding.ASCII.GetBytes(magic));
                writer.Write(format);
                writer.Write(stamp);
            }
        }

        // ------------------------------------------------------------------

        static void Check(bool ok, string what)
        {
            if (!ok)
            {
                failures++;
                Console.WriteLine("    ⛔ НЕ СОШЛОСЬ: {0}", what);
            }
        }
    }
}
