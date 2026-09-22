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
    ///    Вторая половина строки — опорная кривая фиттера — мерилась здесь до
    ///    13.09.2026; снята вместе с самим фитом по спектрам (`AMBER25`).
    ///    ⛔ Плечи A222.1 (сличение с экспортом) и A222.2 (весь набор из восьми
    ///    экспортов) с 15.09.2026 идут ТОЛЬКО с `--lsrm=<каталог экспортов>`:
    ///    `LSRM Geometries/` снят из дерева решением Amber («Удалить вместе с
    ///    каталогом»). Без ключа они пропускаются вслух и отказом не считаются;
    ///    A222.3 (поставочная кривая с точкой 1471.85) и A222.4 (сама граница)
    ///    идут всегда — на них и держится `--break=bound`.
    /// 2. **`A183`, знак вне кодовой страницы 1251.** Положительный контроль —
    ///    ТА САМАЯ снятая строка `File.WriteAllText(..., Encoding.GetEncoding(1251))`,
    ///    выполняемая здесь дословно: она обязана дать `?` там, где новый
    ///    `GeometryWriter.Save` обязан отказать и НАЗВАТЬ знак.
    /// 3. **`A120`, умолчания семи ключей хвоста `OPTF`.** Умолчание поля
    ///    печатается из самого объявления, а поведение старого файла (без байта)
    ///    мерится кругом «записал — обрезал — прочитал». ⚠ Ожидания — ФИЗИКИ 18
    ///    (П51, 14.09.2026): из семи флагов `OPTF` включены пять (`LXrayEscape`,
    ///    `KLCascade`, `PositronTransport`, `PositronOffset`, `RayleighToCrystal`),
    ///    семь ключей физики 17 и два ключа физики 18 (`ecomp=1`, `bpath=2`) — ВКЛ
    ///    умолчанием класса; `PositronOffset` двигает клеймо ТОЛЬКО при включённом
    ///    `PositronTransport`. До того проба ждала физику 16 и с 13.09.2026 краснела
    ///    (П54 §6.2: «включённых умолчанием: 5 (ждали 3)»).
    ///
    /// ⛔ Проверка, которая проходит всегда, ничего не меряет: у каждого
    /// утверждения здесь есть заведомо плохой вход, на котором проба ОТКАЗЫВАЕТ.
    /// Ключ `--break=` подставляет порчу нарочно и обязан валить пробу:
    ///   `--break=bound`  — граница дозиметра поднята до 1e9 (точка 1471.85
    ///                      проходит насквозь);
    ///   `--break=encode` — запись идёт снятой строкой (знак становится `?`);
    ///   `--break=defaults` — ожидание «умолчаний true ровно пять» подменяется
    ///                      на три, как ждала проба до физики 17.
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

        /// <summary>
        /// Каталог с восемью экспортами ЛСРМ — только ключом `--lsrm=`; умолчания
        /// нет с 15.09.2026 (каталог снят из дерева, решение Amber).
        /// </summary>
        static string lsrm;

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
                else if (a.StartsWith("--lsrm=", StringComparison.Ordinal)) lsrm = a.Substring(7);
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
                if (lsrm != null)
                {
                    A222_WhatIsInTheColumn();
                    A222_WholeLsrmSet();
                }
                else
                {
                    Say("");
                    Say("-- A222.1 и A222.2 НЕ ГОНЯЮТСЯ: экспорты ЛСРМ сняты из дерева 15.09.2026"
                        + " (решение Amber: «Удалить вместе с каталогом»); дать --lsrm=<каталог с восемью"
                        + " экспортами>. Отказом не считается. --");
                }

                A222_ShippedCurve();
                A222_BoundItself();
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

            List<double[]> export = RawLsrm(Path.Combine(lsrm, "Obsidian - marinelli 0.5.txt"));
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

            string dir = lsrm;
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

        // A222.5 — «опорная кривая фиттера» — снята 13.09.2026 вместе с самим
        // фитом по спектрам (`AMBER25`, решение Amber):
        // мерить больше нечего.

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

            // ⛔ Ожидание — ФИЗИКА 18 (П51, 14.09.2026): из семи флагов хвоста `OPTF`
            //    включены пять — `LXrayEscape`, `KLCascade` (физика 16) и `PositronTransport`,
            //    `PositronOffset`, `RayleighToCrystal` (семь ключей физики 17, решения Amber
            //    12.09.2026 «Оба ВКЛ в единый счёт, rayl2 только с pkch=1»); выключены
            //    `XcomPairThreshold` и `AnalogConeSampling`. До 14.09.2026 проба ждала три
            //    (физика 16) и краснела «включённых умолчанием: 5 (ждали 3)» (П54 §6.2).
            //    `--break=defaults` подставляет прежние три — и обязан валить пробу.
            int trues = flags.Count(f => f.Value);
            int expected = breakage == "defaults" ? 3 : 5;
            Ok(trues == expected, string.Format(CultureInfo.InvariantCulture,
                "включённых умолчанием: {0} (ждали {1}) — `LXrayEscape`, `KLCascade`, `PositronTransport`, `PositronOffset`, `RayleighToCrystal`",
                trues, expected));
            Ok(options.KLCascade,
               "умолчание `KLCascade` — ВКЛЮЧЕНО; прежний комментарий `Load` говорил «умолчание false»");
            Ok(!options.XcomPairThreshold && !options.AnalogConeSampling,
               "`XcomPairThreshold` и `AnalogConeSampling` умолчанием ВЫКЛЮЧЕНЫ (абляции, не физика склада)");

            // Семь ключей физики 17 (П37/П38: `lbin` `pkch` `lys=2` `etr` `e+tr` `e+off` `rayl2`),
            // два ключа физики 18 (`ecomp=1` `bpath=2`, П50; решение Amber 13.09.2026
            // «ecomp=1 + bpath=2»), ключ физики 19 (`eltr=1`, П97 18.09.2026; решение Amber
            // 17.09.2026 «ВКЛ сейчас, единый счёт ночью»), ключ физики 20 (`elmix=1`, П103
            // 19.09.2026; решение Amber 18.09.2026 по приёмке П100, дословно: «ВКЛ сейчас, единый
            // счёт ночью»), ключ физики 21 (`lbrem=1`, П107 19.09.2026; решение Amber 19.09.2026
            // по приёмке П106, дословно: «ВКЛ сейчас, единый счёт ночью») и ключ физики 22
            // (`lbang=1`, П114 19–21.09.2026; решение Amber 19.09.2026 по приёмке П111,
            // дословно: «ВКЛ единым счётом ночью») — умолчания КЛАССА, одно
            // место истины (правило I `check_matrix_keys.py`):
            // путь склада, путь кривой и поля симулятора берут их отсюда.
            Say("");
            Say("-- A120: умолчания физики 17, 18, 19, 20, 21 и 22 (у физики 23 те же; склад = кривая = симулятор) --");
            Say(string.Format(CultureInfo.InvariantCulture, "   PhysicsVersion       = {0}", ResponseMatrix.PhysicsVersion));
            Say(string.Format(CultureInfo.InvariantCulture, "   LightBinUnified      = {0}", options.LightBinUnified));
            Say(string.Format(CultureInfo.InvariantCulture, "   PeakChannelByTolerance = {0}", options.PeakChannelByTolerance));
            Say(string.Format(CultureInfo.InvariantCulture, "   LYieldSupply         = {0}", options.LYieldSupply));
            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronTransport    = {0}", options.ElectronTransport));
            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronAnyMaterial  = {0}", options.ElectronAnyMaterial));
            Say(string.Format(CultureInfo.InvariantCulture, "   BremAlongPath        = {0}", options.BremAlongPath));
            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronLayerTransport = {0}", options.ElectronLayerTransport));
            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronLayerMixedScattering = {0}", options.ElectronLayerMixedScattering));
            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronLayerBremAlongPath = {0}", options.ElectronLayerBremAlongPath));
            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronLayerBremAngular2BS = {0}", options.ElectronLayerBremAngular2BS));
            // (П122 22.09.2026) Физика 23 — три безусловных исправления `AMBER50`/`AMBER52`/`AMBER57`
            // без ключей: умолчания класса те же, что у физики 22, меняется только номер.
            Ok(ResponseMatrix.PhysicsVersion == 23,
               string.Format(CultureInfo.InvariantCulture, "версия физики склада — 23 (есть {0})", ResponseMatrix.PhysicsVersion));
            Ok(options.LightBinUnified && options.PeakChannelByTolerance && options.LYieldSupply == 2
               && options.ElectronTransport && options.PositronTransport && options.PositronOffset && options.RayleighToCrystal,
               "семь ключей физики 17 умолчанием ВКЛ: lbin=1 pkch=1 lys=2 etr=1 e+tr=1 e+off=1 rayl2=1");
            Ok(options.ElectronAnyMaterial && options.BremAlongPath == 2,
               "два ключа физики 18 умолчанием ВКЛ: ecomp=1 bpath=2");
            Ok(options.ElectronLayerTransport,
               "ключ физики 19 умолчанием ВКЛ: eltr=1");
            Ok(options.ElectronLayerMixedScattering,
               "ключ физики 20 умолчанием ВКЛ: elmix=1");
            Ok(options.ElectronLayerBremAlongPath,
               "ключ физики 21 умолчанием ВКЛ: lbrem=1");
            Ok(options.ElectronLayerBremAngular2BS,
               "ключ физики 22 умолчанием ВКЛ: lbang=1");
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
            // ⛔ С физики 17 (П37, 13.09.2026) `PositronTransport` умолчанием ВКЛ, и клеймо пишет
            //    `e+tr=1;e+off=N;` — половины `S126` обязаны различаться (`ResponseMatrix.ComputeStamp`).
            //    Прежнее ожидание «`PositronOffset` сам по себе клейма НЕ двигает» было верно при
            //    `PositronTransport = false` умолчанием (физика 16) и краснело с 13.09 (П54 §6.2);
            //    сам смысл — «смещение пишется ТОЛЬКО вместе с переносом позитрона» — проверяется
            //    теперь обеими половинами: при переносе ВКЛ смещение клеймо ДВИГАЕТ, при ВЫКЛ — нет.
            string offsetOn = ResponseMatrix.ComputeStamp(model, new ResponseMatrixOptions { PositronOffset = true });
            string offsetOff = ResponseMatrix.ComputeStamp(model, new ResponseMatrixOptions { PositronOffset = false });
            string noTrOffsetOn = ResponseMatrix.ComputeStamp(model, new ResponseMatrixOptions { PositronTransport = false, PositronOffset = true });
            string noTrOffsetOff = ResponseMatrix.ComputeStamp(model, new ResponseMatrixOptions { PositronTransport = false, PositronOffset = false });

            Say("   клеймо умолчания: " + byDefault);
            Say("   клеймо ВЫКЛ:      " + off);

            Ok(string.Equals(byDefault, on, StringComparison.Ordinal),
               "клеймо умолчания РАВНО клейму с явно включённым каскадом — умолчание и есть `true`");
            Ok(!string.Equals(byDefault, off, StringComparison.Ordinal),
               "клеймо с выключенным каскадом ОТЛИЧАЕТСЯ — ключ до клейма доходит");
            Ok(new ResponseMatrixOptions().PositronTransport,
               "`PositronTransport` умолчанием ВКЛ (физика 17, `e+tr=1`)");
            Ok(!string.Equals(offsetOn, offsetOff, StringComparison.Ordinal),
               "при переносе позитрона ВКЛ `PositronOffset` клеймо ДВИГАЕТ (`e+off=1` / `e+off=0`) — половины S126 различимы");
            Ok(string.Equals(noTrOffsetOn, noTrOffsetOff, StringComparison.Ordinal),
               "при переносе позитрона ВЫКЛ `PositronOffset` клеймо НЕ двигает — его пишет только `PositronTransport`");
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
            foreach (string f in Directory.GetFiles(lsrm, "*.txt"))
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

        /// <summary>
        /// Геометрия для круга записи: та же `Nano16Pro.in`, что до 15.09.2026 была
        /// первой по имени в `LSRM Geometries\Models`, — теперь из копий с нашими
        /// ключами `tools\effmaker\models` (решение Amber 15.09.2026: копии остаются).
        /// </summary>
        static string FirstGeometry()
        {
            return Path.Combine(repo, @"tools\effmaker\models\Nano16Pro.in");
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
