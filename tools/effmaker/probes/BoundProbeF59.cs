using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;

namespace BoundProbeF59
{
    /// <summary>
    /// Границы величин и кодировка записи — три строки одной пробой (`A222`,
    /// `A183`, `A120`, полоса F59, 06.09.2026).
    ///
    /// Проба судит ровно то, что нельзя увидеть чтением кода:
    ///
    /// 1. **`A222`, верхняя граница эффективности.** Что лежит в колонке
    ///    `Efficiency` экспорта ЛСРМ, устанавливается СЛИЧЕНИЕМ с поставочной
    ///    кривой, а не рассуждением; затем считается, сколько точек и в скольких
    ///    файлах отвергает новая граница — отдельно на ВВОЗЕ (где точку и так
    ///    снимает правило «погрешность выше 100 % не брать») и на СЫРЫХ данных.
    ///    Отдельно мерится вторая половина строки — опорная кривая фиттера.
    /// 2. **`A183`, знак вне кодовой страницы 1251.** Положительный контроль —
    ///    ТА САМАЯ снятая строка `File.WriteAllText(..., Encoding.GetEncoding(1251))`,
    ///    выполняемая здесь дословно: она обязана дать `?` там, где новый
    ///    `GeometryWriter.Save` обязан отказать и НАЗВАТЬ знак.
    /// 3. **`A120`, умолчания семи ключей хвоста `OPTF`.** Умолчание поля
    ///    печатается из самого объявления, а поведение старого файла (без байта)
    ///    мерится кругом «записал — обрезал — прочитал».
    ///
    /// ⛔ Проверка, которая проходит всегда, ничего не меряет: у каждого
    /// утверждения здесь есть заведомо плохой вход, на котором проба ОТКАЗЫВАЕТ.
    /// Ключ `--break=` подставляет порчу нарочно и обязан валить пробу:
    ///   `--break=bound`  — граница дозиметра поднята до 1e9 (точка 1471.85
    ///                      проходит насквозь);
    ///   `--break=encode` — запись идёт снятой строкой (знак становится `?`);
    ///   `--break=defaults` — ожидание «умолчаний true ровно три» подменяется
    ///                      на два, как гласил старый комментарий.
    ///
    /// ⛔ Ни один файл дерева не переписывается: цель записи всегда во
    /// временном каталоге, геометрии дерева только читаются.
    /// </summary>
    static class Program
    {
        static int checks;
        static int failed;
        static string breakage = "";
        static string repo = ".";
        static readonly List<string> report = new List<string>();

        static int Main(string[] args)
        {
            // ⛔ (`T247`) Культура ЦЕЛИКОМ инвариантная, приказ Amber 05.09.2026. Проба
            //    не ставила её ВОВСЕ, и на русской машине часть её чисел шла с ЗАПЯТОЙ
            //    (замер 10.09.2026, полоса П8: мест без поставщика культуры — 1).
            //    Инвариант ЦЕЛИКОМ, а не клон с подменённым разделителем: клон
            //    чинит печать и оставляет РАЗБОР системным (`T245`).
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception)
            {
                // Консоль без UTF-8 — не повод не работать: отчёт пишется файлом.
            }

            string reportPath = "";
            foreach (string a in args)
            {
                if (a.StartsWith("--repo=", StringComparison.Ordinal)) repo = a.Substring(7);
                else if (a.StartsWith("--break=", StringComparison.Ordinal)) breakage = a.Substring(8);
                else if (a.StartsWith("--report=", StringComparison.Ordinal)) reportPath = a.Substring(9);
                // `A263`: неизвестное ИМЯ ключа — отказ, а не молчание. Опечатка
                // молча меняла прогон, ничем этого не показывая.
                else
                {
                    Console.WriteLine("не знаю ключа: " + a);
                    return 2;
                }
            }

            Say("=== BoundProbeF59: A222 (граница эффективности), A183 (кодировка записи), A120 (умолчания OPTF) ===");
            Say("дерево: " + Path.GetFullPath(repo));
            if (breakage.Length > 0)
            {
                Say("⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: --break=" + breakage + " — проба ОБЯЗАНА отказать");
            }

            try
            {
                A222_WhatIsInTheColumn();
                A222_WholeLsrmSet();
                A222_ShippedCurve();
                A222_BoundItself();
                A222_FitterReference();
                A183_Encoding();
                A183_HealthyGeometries();
                A120_Defaults();
                A120_OldFileWithoutTail();
                A120_Stamp();
            }
            catch (Exception ex)
            {
                Say("⛔ ПРОБА УПАЛА: " + ex);
                failed++;
                checks++;
            }

            Say("");
            Say(string.Format(CultureInfo.InvariantCulture,
                              "=== проверок {0}, отказов {1} ===", checks, failed));

            if (reportPath.Length > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
                File.WriteAllText(reportPath, string.Join(Environment.NewLine, report) + Environment.NewLine,
                                  new UTF8Encoding(false));
                Console.WriteLine("отчёт: " + Path.GetFullPath(reportPath));
            }

            return failed == 0 ? 0 : 1;
        }

        // ==================================================================
        // A222 — что лежит в колонке `Efficiency`
        // ==================================================================

        /// <summary>
        /// Вопрос самой строки `A222`: доля, проценты или отсчёты на распад.
        /// Ответ берётся НЕ из описания поля, а из совпадения двух независимых
        /// поставок: экспорт ЛСРМ и поставочная кривая `config/ROI` той же
        /// сцены. Если это одно число в одной шкале — граница у них общая.
        /// </summary>
        static void A222_WhatIsInTheColumn()
        {
            Say("");
            Say("-- A222.1: колонка `Efficiency` экспорта против поставочной кривой той же сцены --");

            List<double[]> export = RawLsrm(Path.Combine(repo,
                @"LSRM Geometries\Exported Curves\Obsidian - marinelli 0.5.txt"));
            List<double[]> roi = RoiCurve(Path.Combine(repo,
                @"BecquerelMonitor\config\ROI\Obsidian Marinelli 0.5.xml"));

            Say(string.Format(CultureInfo.InvariantCulture,
                              "   точек: экспорт {0}, поставочная кривая {1}", export.Count, roi.Count));

            int same = 0;
            string firstDiff = "";
            for (int i = 0; i < Math.Min(export.Count, roi.Count); i++)
            {
                if (Close(export[i][0], roi[i][0]) && Close(export[i][1], roi[i][1])
                    && Close(export[i][2], roi[i][2]))
                {
                    same++;
                }
                else if (firstDiff.Length == 0)
                {
                    firstDiff = string.Format(CultureInfo.InvariantCulture,
                        "i={0}: экспорт ({1},{2},{3}) против кривой ({4},{5},{6})",
                        i, export[i][0], export[i][1], export[i][2], roi[i][0], roi[i][1], roi[i][2]);
                }
            }

            Ok(export.Count == roi.Count && same == export.Count,
               string.Format(CultureInfo.InvariantCulture,
                   "поставочная кривая ЕСТЬ тот же экспорт: сошлось {0} точек из {1}{2}",
                   same, export.Count, firstDiff.Length == 0 ? "" : "; первое расхождение " + firstDiff));

            Say("   ⇒ колонка `Efficiency` и поле `ROIEfficiencyData.Efficiency` — одно число в одной шкале.");
            Say(string.Format(CultureInfo.InvariantCulture,
                "   ⇒ наибольшее значение среди ЗДОРОВЫХ точек всех восьми экспортов: {0:G4} — доля, не проценты.",
                HealthyMax()));
        }

        /// <summary>
        /// Весь набор экспортов: сколько точек отвергает новая граница на ВВОЗЕ
        /// (после правила «погрешность выше 100 %») и сколько — на СЫРЫХ данных.
        /// Разница между двумя числами и есть ответ «побочно или по существу».
        /// </summary>
        static void A222_WholeLsrmSet()
        {
            Say("");
            Say("-- A222.2: весь набор экспортов ЛСРМ — что отвергает граница ε ≤ 1 --");

            string dir = Path.Combine(repo, @"LSRM Geometries\Exported Curves");
            string[] files = Directory.GetFiles(dir, "*.txt").OrderBy(f => f, StringComparer.Ordinal).ToArray();
            Ok(files.Length == 8, string.Format(CultureInfo.InvariantCulture,
                                                "экспортов в наборе: {0} (ждали 8)", files.Length));

            int rawRows = 0, rawAbove = 0, rawFilesAbove = 0;
            int importedRows = 0, importedAbove = 0, importedFilesRefused = 0, rawFilesRefused = 0;

            foreach (string f in files)
            {
                List<double[]> raw = RawLsrm(f);
                int above = raw.Count(p => p[1] > 1.0);
                rawRows += raw.Count;
                rawAbove += above;
                if (above > 0) rawFilesAbove++;

                // ВВОЗ приложением — тем самым методом, что зовёт кнопка.
                List<ROIEfficiencyData> imported = Import(f);
                importedRows += imported.Count;
                importedAbove += imported.Count(p => p.Efficiency > 1.0);
                if (Refuses(imported)) importedFilesRefused++;

                // Те же данные БЕЗ правила по погрешности — то, что дошло бы до
                // кривой, если бы точке верили по погрешности.
                List<ROIEfficiencyData> asis = raw
                    .Select(p => new ROIEfficiencyData { Energy = p[0], Efficiency = p[1], ErrorPercent = p[2] })
                    .ToList();
                if (Refuses(asis)) rawFilesRefused++;

                Say(string.Format(CultureInfo.InvariantCulture,
                    "   {0,-38} сырых {1,3}, ε>1 {2}, ввезено {3,3}, ε>1 после ввоза {4}",
                    Path.GetFileName(f), raw.Count, above, imported.Count,
                    imported.Count(p => p.Efficiency > 1.0)));
            }

            Say(string.Format(CultureInfo.InvariantCulture,
                "   ИТОГО сырых точек {0}, из них ε>1: {1} в {2} файле(ах)", rawRows, rawAbove, rawFilesAbove));
            Say(string.Format(CultureInfo.InvariantCulture,
                "   ИТОГО ввезено точек {0}, из них ε>1: {1}", importedRows, importedAbove));

            Ok(rawAbove == 1 && rawFilesAbove == 1,
               "на сырых данных граница отвергает РОВНО одну точку в одном файле из восьми");
            Ok(importedAbove == 0 && importedFilesRefused == 0,
               "после ввоза граница не отвергает НИ ОДНОЙ точки — здоровые кривые не задеты");
            Ok(rawFilesRefused == 1,
               "без правила по погрешности отказывает ровно один экспорт из восьми");
        }

        /// <summary>
        /// ⛔ Поставочная кривая — по приказу Amber 05.09.2026 не правится и
        /// дефектом не числится. Здесь она только ЧИТАЕТСЯ: это единственный
        /// путь, по которому невозможная точка попадает в дозиметр СЕГОДНЯ,
        /// мимо всякого ввоза, — и потому единственный настоящий замер строки.
        /// </summary>
        static void A222_ShippedCurve()
        {
            Say("");
            Say("-- A222.3: поставочная кривая, попадающая в дозиметр напрямую --");

            List<double[]> roi = RoiCurve(Path.Combine(repo,
                @"BecquerelMonitor\config\ROI\Obsidian Marinelli 0.5.xml"));
            List<ROIEfficiencyData> whole = roi
                .Select(p => new ROIEfficiencyData { Energy = p[0], Efficiency = p[1], ErrorPercent = p[2] })
                .ToList();

            string message;
            bool refused = Refuses(whole, out message);
            Ok(refused && message.IndexOf("1471.85", StringComparison.Ordinal) >= 0
                       && message.IndexOf("20", StringComparison.Ordinal) >= 0,
               "кривая с точкой 1471.85 на 20 кэВ ОТВЕРГНУТА, и отказ называет точку: " + Short(message));

            // Положительный контроль наоборот: та же кривая без одной точки
            // обязана строиться, иначе проверка судит не то.
            List<ROIEfficiencyData> without = whole.Where(p => p.Efficiency <= 1.0).ToList();
            DoseRateCurve curve = null;
            string problem = "";
            try { curve = DoseRateEstimator.CurveOf(without); }
            catch (Exception ex) { problem = ex.Message; }

            Ok(curve != null && curve.Count == whole.Count - 1,
               string.Format(CultureInfo.InvariantCulture,
                   "та же кривая без единственной невозможной точки строится: узлов {0} из {1}{2}",
                   curve == null ? 0 : curve.Count, whole.Count - 1,
                   problem.Length == 0 ? "" : "; " + Short(problem)));
        }

        /// <summary>Сама граница: где именно она проходит и что пропускает.</summary>
        static void A222_BoundItself()
        {
            Say("");
            Say("-- A222.4: где проходит граница --");

            Ok(!Refuses(Pair(0.5, 0.9)), "ε = 0.9 проходит");
            Ok(!Refuses(Pair(0.5, 1.0)), "ε = 1.0 проходит (граница ВКЛЮЧИТЕЛЬНАЯ)");
            Ok(Refuses(Pair(0.5, 1.0000001)), "ε = 1.0000001 отвергается");
            Ok(Refuses(Pair(0.5, 1471.85)), "ε = 1471.85 отвергается");

            // ⚠ Попутная находка полосы F59: обещание отказа «хватит двух точек»
            // до 06.09.2026 не выполнялось — монотонный сплайн на двух узлах
            // бросал `ArgumentException` МИМО `DoseRateRefusalException`.
            DoseRateCurve two = DoseRateEstimator.CurveOf(Pair(0.5, 0.9));
            Ok(two != null && two.Count == 2 && Math.Abs(two.At(150.0) - 0.7) < 1e-9,
               string.Format(CultureInfo.InvariantCulture,
                   "кривая из ДВУХ точек строится и считается: узлов {0}, на 150 кэВ {1:G6}",
                   two == null ? 0 : two.Count, two == null ? double.NaN : two.At(150.0)));
        }

        /// <summary>
        /// Вторая половина строки `A222`, которой прежняя правка не касалась:
        /// ОПОРНАЯ кривая фиттера. По ней снимается уровень и по ней же
        /// `Evaluate` продолжает кривую ниже измеренных линий — там невозможная
        /// точка и работает.
        /// </summary>
        static void A222_FitterReference()
        {
            Say("");
            Say("-- A222.5: опорная кривая фиттера --");

            List<double[]> roi = RoiCurve(Path.Combine(repo,
                @"BecquerelMonitor\config\ROI\Obsidian Marinelli 0.5.xml"));
            List<ROIEfficiencyData> whole = roi
                .Select(p => new ROIEfficiencyData { Energy = p[0], Efficiency = p[1], ErrorPercent = p[2] })
                .ToList();

            MethodInfo believable = typeof(EfficiencyFitter).GetMethod(
                "Believable", BindingFlags.NonPublic | BindingFlags.Static);
            if (believable == null)
            {
                Ok(false, "в `EfficiencyFitter` нет `Believable` — просеивания опорной кривой не существует");
                return;
            }

            var log = new List<string>();
            Action<string> sink = log.Add;
            var kept = (List<ROIEfficiencyData>)believable.Invoke(null, new object[] { whole, sink });

            Ok(kept.Count == whole.Count - 1 && log.Count == 1,
               string.Format(CultureInfo.InvariantCulture,
                   "снята ровно одна точка из {0}, и она названа в журнале: {1}",
                   whole.Count, log.Count == 1 ? Short(log[0]) : "(строк " + log.Count + ")"));

            // ЦЕНА, которую снимает просеивание: продолжение кривой ВНИЗ.
            double withBad = Extrapolated(whole, 20.0);
            double withGood = Extrapolated(kept, 20.0);
            double ratio = withGood > 0.0 ? withBad / withGood : double.NaN;
            Say(string.Format(CultureInfo.InvariantCulture,
                "   продолжение кривой на 20 кэВ: с невозможной точкой {0:G4}, без неё {1:G4} — в {2:G4} раза",
                withBad, withGood, ratio));
            Ok(ratio > 1000.0,
               string.Format(CultureInfo.InvariantCulture,
                   "невозможная точка задирала экстраполяцию вниз более чем в тысячу раз ({0:G4})", ratio));
        }

        /// <summary>`Evaluate` за нижним краем измеренных линий.</summary>
        static double Extrapolated(List<ROIEfficiencyData> reference, double energy)
        {
            var result = new EfficiencyFitResult
            {
                Coefficients = new double[0],
                Level = Math.Log(0.003),
                MinEnergy = 40.0,
                MaxEnergy = 1500.0,
                ReferenceCurve = reference,
                LevelSource = EfficiencyLevelSource.Reference,
            };

            return EfficiencyFitter.Evaluate(result, energy);
        }

        // ==================================================================
        // A183 — знак, которого в 1251 нет
        // ==================================================================

        static void A183_Encoding()
        {
            Say("");
            Say("-- A183: знак вне кодовой страницы 1251 --");

            string source = FirstGeometry();
            GeometryModel model = GeometryModel.Load(source);
            if (model == null)
            {
                Ok(false, "не прочиталась геометрия " + source);
                return;
            }

            string dir = Path.Combine(Path.GetTempPath(), "bq_f59_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);

            // ⚠ ПОСЫЛКА СТРОКИ ОКАЗАЛАСЬ УЖЕ РЕАЛЬНОСТИ: `?` — только ПОЛОВИНА
            // случаев. `Encoding.GetEncoding(1251)` берёт замену «по лучшему
            // соответствию», и знак, у которого латинский двойник есть,
            // становится ЭТИМ ДВОЙНИКОМ, а не вопросительным знаком: `Å` (U+00C5)
            // писался как `A`. Порча от этого не мягче — она НЕЗАМЕТНЕЕ:
            // `NaIATl` выглядит как имя, а `NaI?Tl` хотя бы кричит.
            // Меряются оба вида.
            Corrupted(model, dir, 'Å', "A", "знак с латинским двойником");
            Corrupted(model, dir, '中', "?", "знак без соответствия");

            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        /// <summary>
        /// Один знак: чем он становился ДО правки и что делает запись ПОСЛЕ.
        /// </summary>
        static void Corrupted(GeometryModel model, string dir, char sign, string wasBecoming, string what)
        {
            string spoiled = "NaI" + sign + "Tl";
            model.Crystal.Name = spoiled;
            string oldWay = Path.Combine(dir, "old.in");
            string newWay = Path.Combine(dir, "new" + ((int)sign).ToString("X4", CultureInfo.InvariantCulture) + ".in");

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — снятая строка, дословно.
            File.WriteAllText(oldWay, GeometryWriter.Render(model), Encoding.GetEncoding(1251));
            string oldText = Encoding.GetEncoding(1251).GetString(File.ReadAllBytes(oldWay));

            string line = oldText.Split('\n').FirstOrDefault(l => l.IndexOf("NaI", StringComparison.Ordinal) >= 0);
            Say(string.Format(CultureInfo.InvariantCulture,
                "   U+{0:X4} ({1}): снятая запись дала «{2}»", (int)sign, what,
                (line ?? "").Trim()));

            Ok(oldText.IndexOf("NaI" + wasBecoming + "Tl", StringComparison.Ordinal) >= 0
               && oldText.IndexOf(spoiled, StringComparison.Ordinal) < 0,
               string.Format(CultureInfo.InvariantCulture,
                   "ДО правки U+{0:X4} молча становился «{1}»", (int)sign, wasBecoming));

            string thrown = "";
            try
            {
                if (breakage == "encode")
                {
                    File.WriteAllText(newWay, GeometryWriter.Render(model), Encoding.GetEncoding(1251));
                }
                else
                {
                    GeometryWriter.Save(model, newWay);
                }
            }
            catch (Exception ex)
            {
                thrown = ex.Message;
            }

            string code = "U+" + ((int)sign).ToString("X4", CultureInfo.InvariantCulture);
            Ok(thrown.Length > 0 && thrown.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0,
               "ПОСЛЕ правки запись ОТКАЗЫВАЕТ и называет знак: " + Short(thrown));
            Ok(thrown.Length > 0 && !File.Exists(newWay),
               "испорченного файла на диске не осталось");
        }

        /// <summary>
        /// Здоровые геометрии дерева не задеты: байты новой записи обязаны
        /// совпасть с байтами снятой строки. Считается по ВСЕМ `.in` дерева,
        /// кроме каталогов сборки проб.
        /// </summary>
        static void A183_HealthyGeometries()
        {
            Say("");
            Say("-- A183: здоровые геометрии дерева — байт в байт --");

            string[] files = Directory.GetFiles(Path.GetFullPath(repo), "*.in", SearchOption.AllDirectories)
                .Where(f => f.IndexOf(@"\probes\build", StringComparison.OrdinalIgnoreCase) < 0)
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();

            string dir = Path.Combine(Path.GetTempPath(), "bq_f59h_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            string oldWay = Path.Combine(dir, "old.in");
            string newWay = Path.Combine(dir, "new.in");

            int seen = 0, identical = 0, refused = 0, unreadable = 0;
            var refusedNames = new List<string>();

            foreach (string f in files)
            {
                GeometryModel model;
                try { model = GeometryModel.Load(f); }
                catch (Exception) { unreadable++; continue; }
                if (model == null) { unreadable++; continue; }

                seen++;
                File.WriteAllText(oldWay, GeometryWriter.Render(model), Encoding.GetEncoding(1251));
                try
                {
                    if (File.Exists(newWay)) File.Delete(newWay);
                    GeometryWriter.Save(model, newWay);
                }
                catch (Exception)
                {
                    refused++;
                    if (refusedNames.Count < 5) refusedNames.Add(Rel(f));
                    continue;
                }

                if (File.ReadAllBytes(oldWay).SequenceEqual(File.ReadAllBytes(newWay))) identical++;
            }

            Say(string.Format(CultureInfo.InvariantCulture,
                "   геометрий {0}, прочиталось {1}, не прочиталось {2}; байт в байт {3}, отказов {4}",
                files.Length, seen, unreadable, identical, refused));
            foreach (string n in refusedNames) Say("     отказ: " + n);

            Ok(seen > 0 && identical == seen,
               string.Format(CultureInfo.InvariantCulture,
                   "новая запись даёт ТЕ ЖЕ байты у всех здоровых геометрий: {0} из {1}", identical, seen));
            Ok(refused == 0,
               string.Format(CultureInfo.InvariantCulture,
                   "ни одна геометрия дерева новой записью не отвергнута (отказов {0})", refused));

            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        // ==================================================================
        // A120 — умолчания ключей хвоста OPTF
        // ==================================================================

        static void A120_Defaults()
        {
            Say("");
            Say("-- A120: умолчания семи ключей хвоста `OPTF` --");

            var options = new ResponseMatrixOptions();
            var flags = new[]
            {
                new KeyValuePair<string, bool>("XcomPairThreshold", options.XcomPairThreshold),
                new KeyValuePair<string, bool>("PositronTransport", options.PositronTransport),
                new KeyValuePair<string, bool>("PositronOffset", options.PositronOffset),
                new KeyValuePair<string, bool>("RayleighToCrystal", options.RayleighToCrystal),
                new KeyValuePair<string, bool>("AnalogConeSampling", options.AnalogConeSampling),
                new KeyValuePair<string, bool>("LXrayEscape", options.LXrayEscape),
                new KeyValuePair<string, bool>("KLCascade", options.KLCascade),
            };

            foreach (var f in flags)
            {
                Say(string.Format(CultureInfo.InvariantCulture, "   {0,-20} = {1}", f.Key, f.Value ? "true" : "false"));
            }

            int trues = flags.Count(f => f.Value);
            int expected = breakage == "defaults" ? 2 : 3;
            Ok(trues == expected, string.Format(CultureInfo.InvariantCulture,
                "включённых умолчанием: {0} (ждали {1}) — `LXrayEscape`, `KLCascade`, `PositronOffset`",
                trues, expected));
            Ok(options.KLCascade,
               "умолчание `KLCascade` — ВКЛЮЧЕНО; прежний комментарий `Load` говорил «умолчание false»");
        }

        /// <summary>
        /// Старый файл — тот, у которого байта `KLCascade` в хвосте нет. Круг
        /// «записал — обрезал — прочитал» отвечает, чем становится флаг: тем,
        /// что говорил комментарий (`false`), или тем, что говорит объявление
        /// (`true`).
        /// </summary>
        static void A120_OldFileWithoutTail()
        {
            Say("");
            Say("-- A120: старый файл без байта каскада --");

            string dir = Path.Combine(Path.GetTempPath(), "bq_f59m_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "tiny.rsp");

            GeometryModel model = GeometryModel.Load(FirstGeometry());
            if (model == null)
            {
                Ok(false, "не прочиталась геометрия для круга");
                return;
            }

            ResponseMatrix saved = Tiny();
            saved.Options.KLCascade = false;
            saved.Options.LXrayEscape = false;
            saved.Stamp = ResponseMatrix.ComputeStamp(model, saved.Options);
            saved.Save(path);

            ResponseMatrix back = ResponseMatrix.Load(path);
            Ok(back != null && !back.Options.KLCascade && !back.Options.LXrayEscape,
               "выключенный каскад доезжает через файл выключенным");
            Ok(back != null && back.IsValidFor(model),
               "целый файл сходится сам с собой по клейму");

            // Обрезаем файл ДО байта каскада: у старых файлов его нет вовсе.
            // 4 байта метки + пять флагов + байт `LXrayEscape` = 10; каскад — 11-й.
            byte[] bytes = File.ReadAllBytes(path);
            int cut = LastIndexOf(bytes, Encoding.ASCII.GetBytes("OPTF"));
            Ok(cut > 0, "метка `OPTF` в файле найдена");
            string chopped = Path.Combine(dir, "chopped.rsp");
            File.WriteAllBytes(chopped, bytes.Take(cut + 4 + 6).ToArray());

            ResponseMatrix old = ResponseMatrix.Load(chopped);
            Ok(old != null, "обрезанный файл всё равно читается — хвост на данные не влияет");
            Ok(old != null && old.Options.KLCascade,
               "у файла БЕЗ байта каскад остаётся ВКЛЮЧЁННЫМ (умолчание поля), а не выключенным");
            Ok(old != null && !old.Options.LXrayEscape,
               "байт, который в обрезке ЕСТЬ, читается своим значением (контроль обрезки)");

            // ⛔ КОНТРОЛЬ «ОБРЕЗКА НЕ ПУСТАЯ»: этот файл считался БЕЗ каскада, а
            // после чтения объявляет себя посчитанным С НИМ — и клеймо честно
            // расходится. Без этой проверки следующая ничего не стоила бы.
            Ok(old != null && !old.IsValidFor(model),
               "обрезанный файл ВЫКЛЮЧЕННОГО каскада перестал сходиться с клеймом — обрезка действует");

            // А вот НАСТОЯЩИЙ старый файл — тот, что считался умолчаниями своего
            // времени и клеймо себе снял ими же, — после обрезки сходится.
            ResponseMatrix byDefaults = Tiny();
            byDefaults.Stamp = ResponseMatrix.ComputeStamp(model, byDefaults.Options);
            string defaultsPath = Path.Combine(dir, "defaults.rsp");
            byDefaults.Save(defaultsPath);
            byte[] db = File.ReadAllBytes(defaultsPath);
            int dcut = LastIndexOf(db, Encoding.ASCII.GetBytes("OPTF"));
            string defaultsChopped = Path.Combine(dir, "defaults_old.rsp");
            File.WriteAllBytes(defaultsChopped, db.Take(dcut + 4 + 6).ToArray());

            ResponseMatrix defaultsOld = ResponseMatrix.Load(defaultsChopped);
            Ok(defaultsOld != null && defaultsOld.Options.KLCascade && defaultsOld.IsValidFor(model),
               "файл, посчитанный умолчаниями и без байта каскада, СХОДИТСЯ со своим клеймом");

            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        /// <summary>
        /// ⚠ Клеймо — это `phys=N;<sha256>`, а не список ключей: искать в нём
        /// подстроку `noklcasc` бессмысленно, ключ лежит ВНУТРИ хеша. Поэтому
        /// «клеймо пишет то же значение» проверяется РАВЕНСТВОМ клейм, а не
        /// чтением их текста.
        /// </summary>
        static void A120_Stamp()
        {
            Say("");
            Say("-- A120: клеймо и умолчание --");

            GeometryModel model = GeometryModel.Load(FirstGeometry());
            if (model == null)
            {
                Ok(false, "не прочиталась геометрия для клейма");
                return;
            }

            string byDefault = ResponseMatrix.ComputeStamp(model, new ResponseMatrixOptions());
            string on = ResponseMatrix.ComputeStamp(model, new ResponseMatrixOptions { KLCascade = true });
            string off = ResponseMatrix.ComputeStamp(model, new ResponseMatrixOptions { KLCascade = false });
            string offsetOn = ResponseMatrix.ComputeStamp(model, new ResponseMatrixOptions { PositronOffset = true });
            string offsetOff = ResponseMatrix.ComputeStamp(model, new ResponseMatrixOptions { PositronOffset = false });

            Say("   клеймо умолчания: " + byDefault);
            Say("   клеймо ВЫКЛ:      " + off);

            Ok(string.Equals(byDefault, on, StringComparison.Ordinal),
               "клеймо умолчания РАВНО клейму с явно включённым каскадом — умолчание и есть `true`");
            Ok(!string.Equals(byDefault, off, StringComparison.Ordinal),
               "клеймо с выключенным каскадом ОТЛИЧАЕТСЯ — ключ до клейма доходит");
            Ok(string.Equals(offsetOn, offsetOff, StringComparison.Ordinal),
               "`PositronOffset` сам по себе клейма НЕ двигает — его пишет только `PositronTransport`");
        }

        static ResponseMatrix Tiny()
        {
            var matrix = new ResponseMatrix
            {
                Energies = new[] { 100.0, 200.0 },
                ChannelRows = new float[1][][],
                BinKev = 2.0,
                Histories = 1,
                CreatedUtc = DateTime.UtcNow,
                BuildSeconds = 0.0,
                Stamp = "phys=16;",
                Options = new ResponseMatrixOptions(),
            };

            matrix.ChannelRows[0] = new float[2][];
            matrix.ChannelRows[0][0] = new float[] { 1.0f, 0.0f };
            matrix.ChannelRows[0][1] = new float[] { 0.0f, 1.0f };
            return matrix;
        }

        // ==================================================================
        // Мелочи
        // ==================================================================

        /// <summary>Свой разбор экспорта ЛСРМ — независимый от проверяемого кода.</summary>
        static List<double[]> RawLsrm(string path)
        {
            var points = new List<double[]>();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] cells = lines[i].Split('\t')
                                         .Select(c => c.Trim())
                                         .Where(c => c.Length > 0)
                                         .ToArray();
                if (cells.Length < 3) continue;
                points.Add(new[]
                {
                    double.Parse(cells[0], CultureInfo.InvariantCulture),
                    double.Parse(cells[1], CultureInfo.InvariantCulture),
                    double.Parse(cells[2], CultureInfo.InvariantCulture),
                });
            }

            return points;
        }

        /// <summary>Свой разбор кривой из конфигурации ROI — тоже без приложения.</summary>
        static List<double[]> RoiCurve(string path)
        {
            var points = new List<double[]>();
            var document = new XmlDocument();
            document.Load(path);
            XmlNodeList nodes = document.SelectNodes("//ROIEfficiency/ROIEfficiencyData");
            foreach (XmlNode node in nodes)
            {
                points.Add(new[]
                {
                    double.Parse(node["Energy"].InnerText, CultureInfo.InvariantCulture),
                    double.Parse(node["Efficiency"].InnerText, CultureInfo.InvariantCulture),
                    double.Parse(node["ErrorPercent"].InnerText, CultureInfo.InvariantCulture),
                });
            }

            return points;
        }

        static double HealthyMax()
        {
            double max = 0.0;
            foreach (string f in Directory.GetFiles(Path.Combine(repo, @"LSRM Geometries\Exported Curves"), "*.txt"))
            {
                foreach (double[] p in RawLsrm(f))
                {
                    if (p[1] <= 1.0 && p[1] > max) max = p[1];
                }
            }

            return max;
        }

        /// <summary>Ввоз тем же методом, что зовёт кнопка формы.</summary>
        static List<ROIEfficiencyData> Import(string path)
        {
            MethodInfo method = typeof(DeviceConfigForm).GetMethod(
                "ReadLsrmEfficiencyExport", BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException("нет DeviceConfigForm.ReadLsrmEfficiencyExport");
            }

            object[] args = { path, null };
            var points = (List<ROIEfficiencyData>)method.Invoke(null, args);
            string problem = args[1] as string;
            if (problem != null)
            {
                throw new InvalidOperationException(path + ": " + problem);
            }

            return points;
        }

        static List<ROIEfficiencyData> Pair(double first, double second)
        {
            return new List<ROIEfficiencyData>
            {
                new ROIEfficiencyData { Energy = 100.0, Efficiency = first, ErrorPercent = 1.0 },
                new ROIEfficiencyData { Energy = 200.0, Efficiency = second, ErrorPercent = 1.0 },
            };
        }

        static bool Refuses(List<ROIEfficiencyData> points)
        {
            string message;
            return Refuses(points, out message);
        }

        static bool Refuses(List<ROIEfficiencyData> points, out string message)
        {
            message = "";
            // ⛔ Положительный контроль `--break=bound`: граница поднимается так,
            // что невозможная точка проходит, — проба обязана отказать.
            if (breakage == "bound")
            {
                return points.Any(p => p.Efficiency > 1e9);
            }

            try
            {
                DoseRateEstimator.CurveOf(points);
                return false;
            }
            catch (DoseRateRefusalException ex)
            {
                message = ex.Message;
                return true;
            }
        }

        static string FirstGeometry()
        {
            string dir = Path.Combine(repo, @"LSRM Geometries\Models");
            return Directory.GetFiles(dir, "*.in").OrderBy(f => f, StringComparer.Ordinal).First();
        }

        static int CountQuestionMarksIn(string text, string before, string after)
        {
            int count = 0;
            int from = 0;
            while (true)
            {
                int start = text.IndexOf(before + "?", from, StringComparison.Ordinal);
                if (start < 0) break;
                if (text.IndexOf(after, start, StringComparison.Ordinal) >= 0) count++;
                from = start + 1;
            }

            return count;
        }

        static int LastIndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = haystack.Length - needle.Length; i >= 0; i--)
            {
                bool hit = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { hit = false; break; }
                }

                if (hit) return i;
            }

            return -1;
        }

        static bool Close(double a, double b)
        {
            return Math.Abs(a - b) <= Math.Max(1e-9, Math.Abs(b) * 1e-9);
        }

        static string Rel(string path)
        {
            string root = Path.GetFullPath(repo);
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(root.Length).TrimStart('\\', '/')
                : path;
        }

        static string Short(string text)
        {
            text = (text ?? "").Replace("\r", " ").Replace("\n", " ");
            return text.Length <= 160 ? text : text.Substring(0, 157) + "...";
        }

        static void Ok(bool good, string what)
        {
            checks++;
            if (!good) failed++;
            Say("   " + (good ? "✅ " : "⛔ ") + what);
        }

        static void Say(string line)
        {
            Console.WriteLine(line);
            report.Add(line);
        }
    }
}
