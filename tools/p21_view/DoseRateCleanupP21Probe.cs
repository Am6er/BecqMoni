// Читатель полосы П21, 10.09.2026: ИСПОЛНЕНЫ ЛИ ЧЕТЫРЕ РЕШЕНИЯ AMBER по вкладке
// `DoseRate` (`AMBER13`) — числом, из СОБРАННОЙ сборки.
//
// ⛔ ПЕРЕВЕДЁН 12.09.2026 (полоса П1, `AMBER18`) на «вкладки нет»: решением
// Amber 11.09.2026 вкладка `Dose Rate` снята ЦЕЛИКОМ вместе с
// `comboDoseRateEfficiency` и `DoseRateConfig` прибора; §1 теперь ждёт, что
// `tabPage7` в форме НЕТ, §4 — что `DeviceConfigInfo.DoseRateConfig` снят, а
// старый XML с точками читается. §2 и §3 (ввоз ЛСРМ на Efficiency) — как были.
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
//  §1 ВКЛАДКИ `DoseRate` В ФОРМЕ НЕТ (`AMBER18`, 12.09.2026): ни поля
//     `tabPage7`, ни `comboDoseRateEfficiency`, ни страницы с текстом «Dose
//     Rate»/«МЭД» у `tabControl1`. До 12.09 здесь ждался ровно один контрол.
//
//  §2 ВВОЗ ЛСРМ НА ВКЛАДКЕ Efficiency (решение (4)). Кнопка есть, стоит
//     ВНУТРИ шапки (панель обрезает детей молча — высота числом устаревает при
//     первой же добавленной строке), зовёт разбор экспорта.
//
//  §3 ВВЕЗЁННАЯ КРИВАЯ СОХРАНЯЕТСЯ. Ввоз кладёт кривую в конфигурацию прибора,
//     та переживает сериализацию и чтение обратно — то есть «живёт в поле
//     формы до закрытия окна» больше не про неё.
//
//  §4 `DoseRateConfig` СНЯТ ИЗ КОНФИГУРАЦИИ ПРИБОРА (`AMBER18`, решение (4)):
//     свойства у `DeviceConfigInfo` нет, типов `DoseRateConfig` и
//     `DoseRateCalibrationPoint` в сборке нет; каталоги конфигураций читаются
//     ТЕМ ЖЕ разбором, что и приложение, без единого отказа — старый элемент
//     пропускается молча, — и при пересохранении элемента нет.
//
//   doserstecleanupp21probe [--devices=<каталог>]...
//   doserstecleanupp21probe --sabotage=absent|noheader|nosave   (ждёт ОТКАЗ)
//
// ⛔ Приёмка, которая проходит всегда, не мерит ничего. `--sabotage` портит
// РОВНО ОДНУ вещь и требует, чтобы свой раздел ОТКАЗАЛ; коды у него
// перевёрнуты: 0 — отказ получен (читатель смотрит), 1 — не получен (слеп).
//
//   absent   — читателю §1 подсунуто имя поля, которое в форме ЕСТЬ
//              (`efficiencyTabPage`): «вкладки нет» обязано отказать;
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
        /// Чего в форме быть НЕ ДОЛЖНО (`AMBER18`, 12.09.2026): вкладка снята
        /// целиком вместе с единственным оставшимся на ней списком.
        /// </summary>
        static readonly string[] Gone = { "tabPage7", "comboDoseRateEfficiency", "efficiencyCurve" };

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

            Console.WriteLine("ЧИТАТЕЛЬ П21 (переведён П1 12.09.2026): вкладки DoseRate и DoseRateConfig нет");
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
            Head("§1. ВКЛАДКИ tabPage7 В ФОРМЕ НЕТ — решение Amber 11.09.2026 (AMBER18)");

            var gone = new List<string>(Gone);
            if (sabotage == "absent")
            {
                // Порча: в список «чего нет» подсунуто имя, которое в форме ЕСТЬ.
                gone.Add("efficiencyTabPage");
            }

            using (DeviceConfigForm form = BuildForm())
            {
                foreach (string name in gone)
                {
                    object value = Field(form, name);
                    Ok(value == null && FieldInfoOf(form, name) == null,
                       value == null && FieldInfoOf(form, name) == null
                           ? "поля " + name + " в форме нет"
                           : "поле " + name + " всё ещё в форме");
                }

                var tabs = (TabControl)Field(form, "tabControl1");
                Ok(tabs != null, "tabControl1 на месте");
                if (tabs != null)
                {
                    var titles = new List<string>();
                    foreach (TabPage page in tabs.TabPages)
                    {
                        titles.Add(page.Name + " «" + page.Text + "»");
                    }

                    Console.WriteLine("  вкладок: {0}: {1}", tabs.TabPages.Count, string.Join(", ", titles.ToArray()));
                    bool doseTab = false;
                    foreach (TabPage page in tabs.TabPages)
                    {
                        if (page.Name == "tabPage7" || page.Text == "Dose Rate" || page.Text == "МЭД")
                        {
                            doseTab = true;
                        }
                    }

                    Ok(!doseTab, doseTab ? "страница мощности дозы всё ещё в tabControl1" : "страницы «Dose Rate»/«МЭД» в tabControl1 нет");
                }
            }
        }

        static FieldInfo FieldInfoOf(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic
                                               | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }

            return null;
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
            Head("§4. DoseRateConfig СНЯТ ИЗ КОНФИГУРАЦИИ ПРИБОРА (AMBER18, решение (4)); старые файлы читаются");

            Assembly app = typeof(DeviceConfigInfo).Assembly;
            Ok(typeof(DeviceConfigInfo).GetProperty("DoseRateConfig") == null,
               "у DeviceConfigInfo нет свойства DoseRateConfig");
            Ok(app.GetType("BecquerelMonitor.DoseRateConfig") == null, "типа DoseRateConfig в сборке нет");
            Ok(app.GetType("BecquerelMonitor.DoseRateCalibrationPoint") == null,
               "типа DoseRateCalibrationPoint в сборке нет");

            var serializer = new XmlSerializer(typeof(DeviceConfigInfo));
            int totalFiles = 0, totalRead = 0, totalWithElement = 0;

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
                int read = 0, withElement = 0;
                foreach (string f in files)
                {
                    string text = File.ReadAllText(f, Encoding.UTF8);
                    bool hasElement = text.IndexOf("<DoseRateConfig>", StringComparison.Ordinal) >= 0;
                    int points = Count(text, "<DoseRateCalibrationPoint>");
                    DeviceConfigInfo cfg = Load(serializer, f);
                    if (cfg == null) continue;
                    read++;
                    if (hasElement) withElement++;
                    if (points > 0)
                    {
                        Console.WriteLine("    прочитан, в тексте точек {0,3}  {1}", points, Path.GetFileName(f));
                    }
                }

                Console.WriteLine("    ИТОГО: прочитано {0} из {1}, с элементом <DoseRateConfig> в тексте {2}",
                                  read, files.Length, withElement);
                totalFiles += files.Length;
                totalRead += read;
                totalWithElement += withElement;
            }

            Console.WriteLine();
            Ok(totalRead == totalFiles,
               string.Format(CultureInfo.InvariantCulture,
                   "все конфигурации читаются без отказа: {0} из {1} (с рудиментом в тексте {2})",
                   totalRead, totalFiles, totalWithElement));

            // «Рудимент исчезает при пересохранении» — прямая проверка на
            // подставном XML с точками посреди полей.
            string synthetic = "<?xml version=\"1.0\"?>\r\n<DeviceConfigInfo>\r\n  <Guid>p21</Guid>\r\n  <Name>old</Name>\r\n"
                + "  <NumberOfChannels>2048</NumberOfChannels>\r\n  <DoseRateConfig>\r\n    <DoseRateCalibrationPoints>\r\n"
                + "      <DoseRateCalibrationPoint><LowerBound>0</LowerBound><UpperBound>3000</UpperBound><CPS>100</CPS>"
                + "<EtalonDoseRateValue>1</EtalonDoseRateValue></DoseRateCalibrationPoint>\r\n"
                + "    </DoseRateCalibrationPoints>\r\n  </DoseRateConfig>\r\n  <BackgroundSpectrumPathname>bg</BackgroundSpectrumPathname>\r\n"
                + "</DeviceConfigInfo>";
            DeviceConfigInfo device;
            using (var reader = new StringReader(synthetic))
            {
                device = (DeviceConfigInfo)serializer.Deserialize(reader);
            }

            Ok(device.Name == "old" && device.NumberOfChannels == 2048 && device.BackgroundSpectrumPathname == "bg",
               "подставной XML с точками читается целиком: поля до и после элемента на месте");

            string xml;
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, device);
                xml = writer.ToString();
            }

            bool gone = xml.IndexOf("DoseRateC", StringComparison.Ordinal) < 0;
            Ok(gone, gone
                ? "при пересохранении конфигурации ни DoseRateConfig, ни точек нет — рудимент исчезает"
                : "элемент всё ещё уходит в XML при пересохранении");
        }

        static int Count(string text, string needle)
        {
            int n = 0;
            for (int i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf(needle, i + 1, StringComparison.Ordinal))
            {
                n++;
            }

            return n;
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
