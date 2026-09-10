// Читатель полосы П21, 10.09.2026: ИСПОЛНЕНЫ ЛИ ЧЕТЫРЕ РЕШЕНИЯ AMBER по вкладке
// `DoseRate` (`AMBER13`) — числом, из СОБРАННОЙ сборки.
//
// Окно `BecqMoni` не поднимают: и вкладка прибора, и вкладка Efficiency
// обмеряются ОТРАЖЕНИЕМ — форма строится без показа тем же приёмом, что у
// `tools/p19_view/DoseRateTabP19Probe.cs` (дескриптор берётся у формы,
// `CreateControl(true)` у страницы).
//
// ⛔ Тот же исходник собирается ДВАЖДЫ — против сборки ДО правок и против
// сборки ПОСЛЕ, — поэтому всё новое зовётся ОТРАЖЕНИЕМ по имени, а не
// напрямую: иначе «до» просто не скомпилировалось бы, и разности не вышло бы
// вовсе. Разность до/после и есть мера полосы.
//
// Четыре раздела, по разделу на решение:
//
//  §1 СОСТАВ ВКЛАДКИ `DoseRate` (решения (1) «Чистить сразу», (2) «Снять
//     целиком» и (4) в части снятия). Сколько на вкладке контролов и какие
//     именно. Ожидание после чистки — РОВНО ОДИН, `comboDoseRateEfficiency`:
//     решение (в) велит не снять его, а ЗАМЕСТИТЬ выбором вида облучения, и
//     замещение приходит с пунктом (б), лежащим в запретном для полосы
//     `EfficiencyMaker/**`.
//
//  §2 ВВОЗ ЛСРМ НА ВКЛАДКЕ Efficiency (решение (4)). Кнопка есть, стоит
//     ВНУТРИ шапки (панель обрезает детей молча — высота числом устаревает при
//     первой же добавленной строке), зовёт разбор экспорта.
//
//  §3 ВВЕЗЁННАЯ КРИВАЯ СОХРАНЯЕТСЯ. Ввоз кладёт кривую в конфигурацию прибора,
//     та переживает сериализацию и чтение обратно — то есть «живёт в поле
//     формы до закрытия окна» больше не про неё.
//
//  §4 ЦЕНА РЕШЕНИЯ (1), названная Amber заранее и принятая ею: у скольких
//     конфигураций пропадает показание мощности дозы. Гейт —
//     `MainForm.ShowDoseRate`: доза показывается ровно при
//     `DoseRateConfig.DoseRateCalibrationPoints.Count > 0`. Раздел читает
//     каталоги конфигураций ТЕМ ЖЕ разбором, что и приложение, и считает, у
//     скольких гейт открыт. Плюс проверка «рудимент исчезает при
//     пересохранении»: конфигурация, прочитанная и записанная обратно, больше
//     не несёт элемента `DoseRateCalibrationPoints`.
//
//   doserstecleanupp21probe [--devices=<каталог>]...
//   doserstecleanupp21probe --sabotage=absent|noheader|nosave   (ждёт ОТКАЗ)
//
// ⛔ Приёмка, которая проходит всегда, не мерит ничего. `--sabotage` портит
// РОВНО ОДНУ вещь и требует, чтобы свой раздел ОТКАЗАЛ; коды у него
// перевёрнуты: 0 — отказ получен (читатель смотрит), 1 — не получен (слеп).
//
//   absent   — в ожидаемый состав вкладки добавлено имя, которого там нет;
//   noheader — экспорту ЛСРМ отрезана шапка: ввоз обязан отказать;
//   nosave   — на проверку сохранности подсовывается кривая, НЕ положенная в
//              конфигурацию: чтение после записи обязано её не найти.
//
// Коды возврата обычного прогона: 0 — все проверки прошли, 1 — есть
// непрошедшие, 2 — отказ оснастки.

using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace DoseRateCleanupP21Probe
{
    static class Program
    {
        static int failed;
        static int checks;
        static string sabotage;

        /// <summary>
        /// Что обязано остаться на вкладке `DoseRate` после чистки. ⚠ Список
        /// не «мой вкус», а прямое следствие решений: (1) и (2) снимают
        /// таблицу с эталоном, (4) уносит ввоз ЛСРМ на Efficiency, а
        /// `comboDoseRateEfficiency` решением (в) НЕ снимается — он замещается
        /// выбором вида облучения ICRP, и замещение делает пункт (б).
        /// </summary>
        static readonly string[] Expected = { "comboDoseRateEfficiency" };

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch (IOException) { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var deviceDirs = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--devices=", StringComparison.Ordinal)) deviceDirs.Add(a.Substring(10));
                else if (a.StartsWith("--sabotage=", StringComparison.Ordinal)) sabotage = a.Substring(11);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (sabotage != null && sabotage != "absent" && sabotage != "noheader" && sabotage != "nosave")
            {
                Console.Error.WriteLine("--sabotage= принимает absent, noheader или nosave");
                return 2;
            }

            Console.WriteLine("ЧИТАТЕЛЬ П21: четыре решения Amber 10.09.2026 по вкладке DoseRate");
            Console.WriteLine("сборка под рукой: " + typeof(DeviceConfigForm).Assembly.Location);
            if (sabotage != null)
            {
                Console.WriteLine("⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: испорчено «" + sabotage + "», ждём ОТКАЗ");
            }

            try
            {
                TabComposition();
                ImportButton();
                ImportedCurveSurvivesSave();
                PriceOfCleanup(deviceDirs);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: " + ex);
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("проверок {0}, непрошедших {1}", checks, failed);

            if (sabotage != null)
            {
                if (failed > 0)
                {
                    Console.WriteLine("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПРОЙДЕН: порча замечена");
                    return 0;
                }

                Console.WriteLine("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПРОВАЛЕН: порча НЕ замечена — читатель слеп");
                return 1;
            }

            return failed > 0 ? 1 : 0;
        }

        static void Ok(bool condition, string what)
        {
            checks++;
            if (!condition) failed++;
            Console.WriteLine((condition ? "  [ок]   " : "  [НЕТ]  ") + what);
        }

        static void Head(string text)
        {
            Console.WriteLine();
            Console.WriteLine("──────────────────────────────────────────────────────────────");
            Console.WriteLine(text);
            Console.WriteLine("──────────────────────────────────────────────────────────────");
        }

        // ==================================================================
        // §1. Состав вкладки DoseRate
        // ==================================================================

        static void TabComposition()
        {
            Head("§1. СОСТАВ ВКЛАДКИ tabPage7 — решения (1), (2), (4) в части снятия");

            var expected = new List<string>(Expected);
            if (sabotage == "absent")
            {
                // Порча: ждём на вкладке имя, которого там нет.
                expected.Add("buttonThatNeverWas");
            }

            using (DeviceConfigForm form = BuildForm())
            {
                var page = (TabPage)Field(form, "tabPage7");
                if (page == null)
                {
                    Ok(false, "вкладки tabPage7 в форме нет");
                    return;
                }

                Realize(form, page);

                var all = new List<Control>();
                Walk(page, all);

                Console.WriteLine("  на вкладке контролов: {0}", all.Count);
                foreach (Control c in all)
                {
                    Console.WriteLine("  {0,-28} {1}", c.Name, c.GetType().Name);
                }

                Console.WriteLine();
                var extra = all.Select(c => c.Name).Where(n => !expected.Contains(n)).ToList();
                var missing = expected.Where(n => !all.Any(c => c.Name == n)).ToList();

                Ok(extra.Count == 0,
                   extra.Count == 0
                       ? "лишнего на вкладке нет"
                       : "на вкладке ОСТАЛОСЬ снимаемое: " + string.Join(", ", extra.ToArray()));
                Ok(missing.Count == 0,
                   missing.Count == 0
                       ? "всё, что должно остаться, на месте: " + string.Join(", ", expected.ToArray())
                       : "на вкладке НЕТ ожидаемого: " + string.Join(", ", missing.ToArray()));
            }
        }

        // ==================================================================
        // §2. Ввоз ЛСРМ на вкладке Efficiency (решение (4))
        // ==================================================================

        static void ImportButton()
        {
            Head("§2. ВВОЗ ЛСРМ ПЕРЕЕХАЛ НА ВКЛАДКУ Efficiency — решение (4)");

            using (DeviceConfigForm form = BuildForm())
            {
                var page = (TabPage)Field(form, "efficiencyTabPage");
                var button = (Button)Field(form, "efficiencyImportButton");
                if (page == null)
                {
                    Ok(false, "вкладки Efficiency в форме нет");
                    return;
                }

                Realize(form, page);

                Ok(button != null, button == null
                    ? "кнопки ввоза ЛСРМ на вкладке Efficiency НЕТ"
                    : "кнопка ввоза ЛСРМ есть: «" + button.Text + "»");

                if (button == null)
                {
                    return;
                }

                var owner = (Panel)Field(form, "efficiencyHeader");
                bool inTab = button.Parent != null && IsUnder(button, page);
                Ok(inTab, inTab
                    ? "кнопка стоит на вкладке Efficiency, а не на DoseRate"
                    : "кнопка НЕ на вкладке Efficiency");

                // ⛔ Панель обрезает детей МОЛЧА: высота шапки — число, и оно
                // устаревает при первой же добавленной строке. Проверяется не
                // «кнопка создана», а «кнопка ЦЕЛИКОМ внутри шапки».
                if (owner != null)
                {
                    int bottom = button.Bottom;
                    Ok(bottom <= owner.Height,
                       string.Format(CultureInfo.InvariantCulture,
                           "кнопка не обрезана шапкой: низ {0} при высоте шапки {1}",
                           bottom, owner.Height));
                }

                // Ввоз с вкладки DoseRate обязан ИСЧЕЗНУТЬ, а не задвоиться.
                Ok(Field(form, "buttonLoadEff") == null,
                   Field(form, "buttonLoadEff") == null
                       ? "buttonLoadEff с вкладки DoseRate снят"
                       : "buttonLoadEff всё ещё в форме — ввоз задвоился");
            }
        }

        // ==================================================================
        // §3. Ввезённая кривая СОХРАНЯЕТСЯ
        // ==================================================================

        static void ImportedCurveSurvivesSave()
        {
            Head("§3. ВВЕЗЁННАЯ КРИВАЯ ПЕРЕЖИВАЕТ СОХРАНЕНИЕ КОНФИГУРАЦИИ");

            MethodInfo import = typeof(DeviceConfigForm).GetMethod(
                "ImportLsrmEfficiency", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (import == null)
            {
                Ok(false, "метода DeviceConfigForm.ImportLsrmEfficiency нет — ввоз не заведён");
                return;
            }

            string path = Path.Combine(Path.GetTempPath(),
                                       "p21_lsrm_" + Guid.NewGuid().ToString("N") + ".txt");
            WriteLsrm(path, sabotage == "noheader");
            try
            {
                var device = new DeviceConfigInfo();
                device.Name = "P21 probe";
                object[] call = { device, path, null };
                object config = import.Invoke(null, call);
                string problem = (string)call[2];

                Ok(config != null,
                   config != null
                       ? "ввоз принял экспорт ЛСРМ"
                       : "ввоз ОТКАЗАЛ: " + (problem ?? "причина не названа"));
                if (config == null)
                {
                    return;
                }

                var data = (EfficiencyConfigData)config;
                Console.WriteLine("  ввезено точек: {0}; происхождение: {1}; имя: {2}",
                                  data.Curve.Count, data.Origin, data.Name);
                Ok(data.Curve.Count == 4,
                   string.Format(CultureInfo.InvariantCulture,
                       "в кривой {0} точек (в файле 5, одна отсечена по погрешности > 100 %)",
                       data.Curve.Count));
                Ok(data.Origin == EfficiencyOrigin.Lsrm,
                   "происхождение кривой помечено как Lsrm: " + data.Origin);

                bool inList = device.EfficiencyConfigs.Contains(data);
                if (sabotage == "nosave")
                {
                    // Порча: кривую из конфигурации вынимают ДО записи. Чтение
                    // после записи обязано её не найти.
                    device.EfficiencyConfigs.Remove(data);
                }

                Ok(inList, inList
                    ? "кривая положена в список конфигурации прибора"
                    : "кривой НЕТ в EfficiencyConfigs");

                // ⛔ Доказательство «сохраняется» — не «поле заполнено», а
                // ЧТЕНИЕ ПОСЛЕ ЗАПИСИ: конфигурация уходит в XML тем же
                // сериализатором, что и у приложения, и читается обратно.
                var serializer = new XmlSerializer(typeof(DeviceConfigInfo));
                string xml;
                using (var writer = new StringWriter(CultureInfo.InvariantCulture))
                {
                    serializer.Serialize(writer, device);
                    xml = writer.ToString();
                }

                DeviceConfigInfo back;
                using (var reader = new StringReader(xml))
                {
                    back = (DeviceConfigInfo)serializer.Deserialize(reader);
                }

                EfficiencyConfigData restored = back.EfficiencyConfigs
                    .FirstOrDefault(c => c.Guid == data.Guid);
                Ok(restored != null,
                   restored != null
                       ? "кривая НАЙДЕНА после пересохранения, точек " + restored.Curve.Count
                       : "кривой после пересохранения НЕТ — ввоз не сохраняется");

                if (restored != null)
                {
                    bool same = restored.Curve.Count == data.Curve.Count
                                && Math.Abs(restored.Curve[0].Energy - data.Curve[0].Energy) < 1e-9
                                && Math.Abs(restored.Curve[restored.Curve.Count - 1].Efficiency
                                            - data.Curve[data.Curve.Count - 1].Efficiency) < 1e-12;
                    Ok(same, string.Format(CultureInfo.InvariantCulture,
                        "числа кривой те же: {0} точек, первая {1:f2} кэВ, последняя ε {2}",
                        restored.Curve.Count, restored.Curve[0].Energy,
                        restored.Curve[restored.Curve.Count - 1].Efficiency
                            .ToString("f6", CultureInfo.InvariantCulture)));
                }
            }
            finally
            {
                try { File.Delete(path); } catch (IOException) { }
            }
        }

        /// <summary>
        /// Подставной экспорт ЛСРМ: пять точек, у первой погрешность заявлена
        /// выше 100 % — её разбор обязан отсечь (правило `T174`).
        /// </summary>
        static void WriteLsrm(string path, bool withoutHeader)
        {
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                if (!withoutHeader)
                {
                    w.WriteLine("Energy, keV\tEfficiency\tUncertainty, %");
                }

                w.WriteLine("20.0\t0.0100000\t554.0");
                w.WriteLine("40.0\t0.0386379\t30.5");
                w.WriteLine("100.0\t0.0301122\t12.4");
                w.WriteLine("662.0\t0.0092210\t8.1");
                w.WriteLine("1332.0\t0.0051770\t9.9");
            }
        }

        // ==================================================================
        // §4. Цена решения (1) — числом
        // ==================================================================

        static void PriceOfCleanup(List<string> dirs)
        {
            Head("§4. ЦЕНА РЕШЕНИЯ (1): у скольких конфигураций пропадает показание дозы");
            Console.WriteLine("  гейт MainForm.ShowDoseRate: доза показывается при");
            Console.WriteLine("  DoseRateConfig.DoseRateCalibrationPoints.Count > 0");

            var serializer = new XmlSerializer(typeof(DeviceConfigInfo));
            int totalOpen = 0, totalFiles = 0;

            foreach (string dir in dirs)
            {
                Console.WriteLine();
                Console.WriteLine("  " + dir);
                if (!Directory.Exists(dir))
                {
                    Console.WriteLine("    каталога нет");
                    continue;
                }

                string[] files = Directory.GetFiles(dir, "*.xml");
                int open = 0;
                foreach (string f in files)
                {
                    DeviceConfigInfo cfg = Load(serializer, f);
                    if (cfg == null || cfg.DoseRateConfig == null) continue;
                    int n = cfg.DoseRateConfig.DoseRateCalibrationPoints == null
                        ? 0 : cfg.DoseRateConfig.DoseRateCalibrationPoints.Count;
                    if (n > 0)
                    {
                        open++;
                        Console.WriteLine("    гейт ОТКРЫТ, точек {0,3}  {1}", n, Path.GetFileName(f));
                    }
                }

                Console.WriteLine("    ИТОГО: гейт открыт у {0} конфигураций из {1}", open, files.Length);
                totalOpen += open;
                totalFiles += files.Length;
            }

            Console.WriteLine();
            Console.WriteLine("  ВСЕГО по прочитанным каталогам: гейт открыт у {0} из {1}",
                              totalOpen, totalFiles);
            Ok(totalOpen == 0,
               totalOpen == 0
                   ? "показание дозы не показывается НИ ОДНОЙ конфигурацией — цена решения (1) уплачена"
                   : "гейт всё ещё открыт у " + totalOpen + " конфигураций");

            // «Рудимент исчезает при пересохранении» — прямая проверка.
            var device = new DeviceConfigInfo();
            device.DoseRateConfig.DoseRateCalibrationPoints = new List<DoseRateCalibrationPoint>
            {
                new DoseRateCalibrationPoint { LowerBound = 0, UpperBound = 3000, CPS = 100, EtalonDoseRateValue = 1 },
                new DoseRateCalibrationPoint { LowerBound = 3000, UpperBound = 5000, CPS = 100, EtalonDoseRateValue = 1 },
            };

            string xml;
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, device);
                xml = writer.ToString();
            }

            bool gone = xml.IndexOf("DoseRateCalibrationPoint", StringComparison.Ordinal) < 0;
            Ok(gone, gone
                ? "при пересохранении конфигурации точки НЕ пишутся — рудимент исчезает"
                : "точки всё ещё уходят в XML при пересохранении");
        }

        static DeviceConfigInfo Load(XmlSerializer serializer, string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return (DeviceConfigInfo)serializer.Deserialize(stream);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("    не прочиталось: {0} ({1})", Path.GetFileName(path), ex.Message);
                return null;
            }
        }

        // ==================================================================
        // Оснастка
        // ==================================================================

        /// <summary>
        /// ⛔ Без этих двух вызовов конструктор формы падает
        /// `NullReferenceException`: оба статических списка наполняет только
        /// `MainForm`, которого у безоконного прогона нет (найдено полосой П19).
        /// </summary>
        static DeviceConfigForm BuildForm()
        {
            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();
            return new DeviceConfigForm();
        }

        /// <summary>
        /// Заставить страницу построить детей: до этого `Controls` у неё
        /// заполнены, а раскладка не посчитана и `Bottom` ничего не значит.
        /// </summary>
        static void Realize(Form form, TabPage page)
        {
            var tabs = (TabControl)Field(form, "tabControl1");
            if (tabs != null && tabs.TabPages.Contains(page)) tabs.SelectedTab = page;
            IntPtr h = form.Handle;
            GC.KeepAlive(h);
            MethodInfo create = typeof(Control).GetMethod(
                "CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(bool) }, null);
            if (create != null) create.Invoke(page, new object[] { true });
        }

        static bool IsUnder(Control child, Control root)
        {
            for (Control c = child; c != null; c = c.Parent)
            {
                if (ReferenceEquals(c, root)) return true;
            }

            return false;
        }

        /// <summary>Именованные дети, рекурсивно (безымянные служебные — не в счёт).</summary>
        static void Walk(Control parent, List<Control> into)
        {
            foreach (Control c in parent.Controls)
            {
                if (!string.IsNullOrEmpty(c.Name)) into.Add(c);
                Walk(c, into);
            }
        }

        static object Field(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic
                                               | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(target);
            }

            return null;
        }
    }
}
