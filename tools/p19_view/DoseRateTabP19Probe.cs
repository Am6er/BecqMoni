// Читатель полосы П19, 10.09.2026: ЧТО НА ВКЛАДКЕ `DoseRate` ЛИШНЕЕ И ЧТО
// СТОИТ ЕЁ ЧИСТКА (`AMBER13`), плюс обе находки `AMBER14` — числом.
//
// Окно `BecqMoni` не поднимают: вкладка обмеряется ОТРАЖЕНИЕМ из собранной
// сборки — форма строится без показа, тем же приёмом, что у
// `tools/effmaker/probes/DoseRateProbe.cs` (дескриптор берётся у формы,
// `CreateControl(true)` у страницы).
//
// Пять разделов, каждый отвечает на свой вопрос:
//
//  §1 СОСТАВ ВКЛАДКИ. Сколько на ней контролов на самом деле и покрывает ли их
//     перечень решения Amber 10.09.2026 («снять таблицу и эталон, оставить
//     геометрию облучения»). Контрол, не названный НИ в снимаемых, НИ в
//     остающихся, — находка: его судьбу никто не решал.
//
//  §2 КТО ЗАДАЁТ ТОЧКИ. `DoseRateCalibrationPoints` по каталогам конфигураций:
//     поставочные, корпусные, живые. Это и есть «поле задают N устройств из M».
//
//  §3 ЦЕНА СНЯТИЯ ТОЧЕК. `MainForm.ShowDoseRate` показывает дозу ТОЛЬКО при
//     `DoseRateCalibrationPoints.Count > 0`. Раздел считает `DoseRateManager`
//     на живом спектре с точками и без них — то есть цену чистки, сделанной
//     ДО замены расчёта.
//
//  §4 `AMBER14`, находка 1. Поле `DoseRateSpectrumChoice.Efficiency`
//     заполняется (`DoseRate.cs`), но читает ли его кто-нибудь — судится не
//     грепом, а РАЗБОРОМ СОБРАННОГО КОДА: по всем телам методов сборки
//     считаются вызовы `get_Efficiency` этого типа. Положительный контроль —
//     соседнее свойство `Spectrum` того же типа: у него ссылки ОБЯЗАНЫ быть,
//     иначе скан не мерит ничего.
//
//  §5 `AMBER14`, находка 2. В типах `BecquerelMonitor.N42` нет ни одного члена
//     со словом `Efficiency`. Положительный контроль — `EnergyCalibration`:
//     он там ОБЯЗАН быть, иначе «не нашлось» ничего не значит.
//
//   doseratetabp19probe [--devices=<каталог>]... [--spectra=<каталог>]
//                       [--device=<файл .xml>] [--spectrum=<файл .xml>]
//   doseratetabp19probe --sabotage=absent|dead|n42     (ждёт ОТКАЗ)
//
// ⛔ Приёмка, которая проходит всегда, не мерит ничего. `--sabotage` портит
// РОВНО ОДНУ вещь и требует, чтобы свой раздел ОТКАЗАЛ; коды у него
// перевёрнуты: 0 — отказ получен (читатель смотрит), 1 — не получен (слеп).
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

namespace DoseRateTabP19Probe
{
    static class Program
    {
        static int failed;
        static int checks;
        static string sabotage;

        // ------------------------------------------------------------------
        // Решение Amber 10.09.2026 (строка `AMBER13`), перенесённое сюда
        // ИМЕНАМИ КОНТРОЛОВ. Читатель не решает, что снимать, — он сверяет
        // решение с деревом.
        // ------------------------------------------------------------------

        /// <summary>Снять: таблица точек и всё, что её наполняет.</summary>
        static readonly string[] ToRemove =
        {
            "groupBox3", "table4", "button15", "button16", "buttonClearDoseRate",
            "comboDoseRateSpectrum", "buttonLoadDoseRateSpectrum", "labelSpectrumNote",
            "upDownDoseRateValue", "labelDoseRateValue",
            "buttonEstimateDRConf", "labelDREstimateTitle",
        };

        /// <summary>
        /// Остаётся: выбор кривой и путь к ней из файла. ⚠ Решение (в) велит
        /// поставить на место `comboDoseRateEfficiency` выбор вида облучения —
        /// то есть контрол остаётся на вкладке, но меняет смысл. Здесь он
        /// числится остающимся: снятия его перечень решения не требует.
        /// </summary>
        static readonly string[] ToKeep =
        {
            "comboDoseRateEfficiency", "buttonLoadEff", "labelEffNote",
        };

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var deviceDirs = new List<string>();
            string spectraDir = null;
            string devicePath = null;
            string spectrumPath = null;

            foreach (string a in args)
            {
                if (a.StartsWith("--devices=", StringComparison.Ordinal)) deviceDirs.Add(a.Substring(10));
                else if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
                else if (a.StartsWith("--device=", StringComparison.Ordinal)) devicePath = a.Substring(9);
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--sabotage=", StringComparison.Ordinal)) sabotage = a.Substring(11);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (sabotage != null && sabotage != "absent" && sabotage != "dead" && sabotage != "n42")
            {
                Console.Error.WriteLine("--sabotage= принимает absent, dead или n42");
                return 2;
            }

            Console.WriteLine("ЧИТАТЕЛЬ П19: вкладка DoseRate и обе находки AMBER14");
            if (sabotage != null)
            {
                Console.WriteLine("⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: испорчено «" + sabotage + "», ждём ОТКАЗ");
            }

            Console.WriteLine();

            try
            {
                TabComposition();
                WhoSetsPoints(deviceDirs);
                PriceOfRemoval(devicePath, spectrumPath, spectraDir);
                DeadEfficiencyField();
                N42CarriesNoCurve();
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
                // Коды перевёрнуты: порча ОБЯЗАНА быть замечена.
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
        // §1. Состав вкладки
        // ==================================================================

        static void TabComposition()
        {
            Head("§1. СОСТАВ ВКЛАДКИ tabPage7 — что решение Amber покрывает");

            // ⛔ Без этого конструктор формы падает `NullReferenceException` на
            // `foreach (DeviceType … DeviceTypeList)`: список статический и
            // наполняется только в `MainForm` (строка 180), которого у
            // безоконного прогона нет.
            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();

            var remove = new List<string>(ToRemove);
            if (sabotage == "absent")
            {
                // Порча: в перечень снимаемых добавлено имя, которого на
                // вкладке нет. Раздел обязан это назвать.
                remove.Add("buttonThatNeverWas");
            }

            using (var form = new DeviceConfigForm())
            {
                var page = (TabPage)Field(form, "tabPage7");
                if (page == null)
                {
                    Ok(false, "вкладки tabPage7 в форме нет");
                    return;
                }

                var tabs = (TabControl)Field(form, "tabControl1");
                if (tabs != null && tabs.TabPages.Contains(page)) tabs.SelectedTab = page;
                IntPtr h = form.Handle;
                GC.KeepAlive(h);
                MethodInfo create = typeof(Control).GetMethod(
                    "CreateControl", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(bool) }, null);
                if (create != null) create.Invoke(page, new object[] { true });

                var all = new List<Control>();
                Walk(page, all);

                Console.WriteLine("  на вкладке контролов: {0}", all.Count);
                Console.WriteLine();
                Console.WriteLine("  {0,-28} {1,-22} {2}", "имя", "тип", "решение Amber 10.09");
                Console.WriteLine("  " + new string('-', 76));

                int removeSeen = 0, keepSeen = 0;
                var unnamed = new List<string>();
                foreach (Control c in all)
                {
                    string verdict;
                    if (remove.Contains(c.Name)) { verdict = "СНЯТЬ"; removeSeen++; }
                    else if (ToKeep.Contains(c.Name)) { verdict = "оставить"; keepSeen++; }
                    else { verdict = "⚠ НЕ НАЗВАН"; unnamed.Add(c.Name); }

                    Console.WriteLine("  {0,-28} {1,-22} {2}", c.Name, c.GetType().Name, verdict);
                }

                Console.WriteLine();
                Ok(unnamed.Count == 0,
                   unnamed.Count == 0
                       ? "перечень решения покрывает вкладку целиком"
                       : "судьба НЕ названа у " + unnamed.Count + ": " + string.Join(", ", unnamed.ToArray()));

                // Обратная сторона: имя из перечня, которого на вкладке нет.
                var missing = remove.Where(n => !all.Any(c => c.Name == n)).ToList();
                missing.AddRange(ToKeep.Where(n => !all.Any(c => c.Name == n)));
                Ok(missing.Count == 0,
                   missing.Count == 0
                       ? "все имена перечня найдены на вкладке"
                       : "имена перечня, которых на вкладке НЕТ: " + string.Join(", ", missing.ToArray()));

                Console.WriteLine();
                Console.WriteLine("  снимается {0} из {1}, остаётся {2}",
                                  removeSeen, all.Count, keepSeen);
            }
        }

        /// <summary>
        /// Контролы вкладки. ⚠ Собираются только ИМЕНОВАННЫЕ: у таблицы
        /// `XPTable` внутри живут два безымянных `ScrollBar`, у
        /// `NumericUpDown` — `UpDownEdit` и `UpDownButtons`. Они не поля формы,
        /// решать их судьбу отдельно не о чем: они уходят и приходят вместе с
        /// хозяином.
        /// </summary>
        static void Walk(Control parent, List<Control> into)
        {
            foreach (Control c in parent.Controls)
            {
                if (!string.IsNullOrEmpty(c.Name)) into.Add(c);
                Walk(c, into);
            }
        }

        // ==================================================================
        // §2. Кто задаёт точки калибровки дозы
        // ==================================================================

        static void WhoSetsPoints(List<string> dirs)
        {
            Head("§2. КТО ЗАДАЁТ DoseRateCalibrationPoints — по каталогам конфигураций");

            if (dirs.Count == 0)
            {
                Console.WriteLine("  ключей --devices= не дано, каталоги не читались");
                return;
            }

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
                int withPoints = 0;
                int byGenerator = 0;
                int withCurves = 0;
                foreach (string f in files)
                {
                    DeviceConfigInfo cfg = LoadDevice(f);
                    if (cfg != null && DoseRateEstimator.OfferedEfficiencies(cfg).Count > 0) withCurves++;
                    List<DoseRateCalibrationPoint> pts =
                        cfg == null || cfg.DoseRateConfig == null
                            ? null
                            : cfg.DoseRateConfig.DoseRateCalibrationPoints;
                    int n = pts == null ? -1 : pts.Count;
                    if (n > 0)
                    {
                        withPoints++;
                        string why;
                        bool gen = LooksGenerated(pts, out why);
                        if (gen) byGenerator++;
                        Console.WriteLine("    {0,3} точек  {1,-46} {2}",
                                          n, Path.GetFileName(f),
                                          gen ? "сетка ГЕНЕРАТОРА" : "внесено РУКАМИ: " + why);
                    }
                }

                Console.WriteLine("    ИТОГО: точки задают {0} конфигураций из {1}; из них сеткой генератора — {2}",
                                  withPoints, files.Length, byGenerator);
                Console.WriteLine("           кривую эффективности несут {0} конфигураций из {1}"
                                  + " (столько же вкладка может предложить в comboDoseRateEfficiency)",
                                  withCurves, files.Length);
            }

            GeneratorControl();
        }

        /// <summary>
        /// Мог ли этот набор выйти из <c>DoseRateEstimator.Estimate</c>.
        /// Признаки жёсткие и оба из кода генератора: сетку строит
        /// <c>BuildGrid</c>, значит низ не бывает ниже
        /// <c>DoseRateCoefficients.MinEnergyKev</c>, а отношение границ у ВСЕХ
        /// диапазонов ОДНО И ТО ЖЕ (сетка геометрическая).
        /// </summary>
        static bool LooksGenerated(IList<DoseRateCalibrationPoint> pts, out string why)
        {
            why = "";
            if (pts.Count < 2) { why = "точек меньше двух"; return false; }

            if (pts[0].LowerBound < DoseRateCoefficients.MinEnergyKev)
            {
                why = string.Format(CultureInfo.InvariantCulture,
                    "низ {0:f2} кэВ ниже {1:f0}, BuildGrid так не строит",
                    pts[0].LowerBound, DoseRateCoefficients.MinEnergyKev);
                return false;
            }

            double first = pts[0].UpperBound / pts[0].LowerBound;
            for (int i = 1; i < pts.Count; i++)
            {
                if (!(pts[i].LowerBound > 0.0)) { why = "нулевая нижняя граница"; return false; }
                double r = pts[i].UpperBound / pts[i].LowerBound;
                if (Math.Abs(r - first) > 1e-6 * first)
                {
                    why = string.Format(CultureInfo.InvariantCulture,
                        "шаг сетки плавает ({0:f4} против {1:f4}), геометрической она не является", r, first);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// ⛔ Признак, который не умеет сказать «да», ничего не мерит. Набор,
        /// построенный САМИМ генератором на синтетическом входе, обязан быть
        /// опознан как сетка генератора.
        /// </summary>
        static void GeneratorControl()
        {
            Console.WriteLine();
            var calibration = new PolynomialEnergyCalibration();
            calibration.PolynomialOrder = 1;
            calibration.Coefficients = new double[] { 0.0, 1.0 };
            var spectrum = new EnergySpectrum(0.006, 3000);
            spectrum.EnergyCalibration = calibration;
            spectrum.MeasurementTime = 100.0;
            for (int i = 0; i < 3000; i++) spectrum.Spectrum[i] = 100;

            var curvePoints = new List<ROIEfficiencyData>();
            for (double e = 10.0; e <= 10000.0; e *= 1.2)
                curvePoints.Add(new ROIEfficiencyData { Energy = e, Efficiency = 0.01 });

            double minKev, maxKev;
            DoseRateEstimator.DeviceRange(null, spectrum, out minKev, out maxKev);
            double[] grid = DoseRateEstimator.BuildGrid(minKev, maxKev);
            List<DoseRateCalibrationPoint> made = DoseRateEstimator.Estimate(
                spectrum, DoseRateEstimator.CurveOf(curvePoints), 1.0, grid, null);

            string why;
            bool gen = LooksGenerated(made, out why);
            Ok(gen, gen
                ? "положительный контроль признака: набор, сделанный самим генератором ("
                  + made.Count + " точек), опознан как сетка генератора"
                : "признак СЛЕП: набор генератора назван ручным — " + why);
        }

        static DeviceConfigInfo LoadDevice(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    return (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(fs);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ==================================================================
        // §3. Цена снятия точек
        // ==================================================================

        static void PriceOfRemoval(string devicePath, string spectrumPath, string spectraDir)
        {
            Head("§3. ЦЕНА СНЯТИЯ ТОЧЕК — что станет с показанием в строке состояния");

            if (devicePath == null || spectrumPath == null)
            {
                if (spectraDir != null && devicePath == null)
                {
                    Console.WriteLine("  --device= не дан");
                }

                Console.WriteLine("  нужны --device=<конфиг с точками> и --spectrum=<спектр>; раздел пропущен");
                return;
            }

            DeviceConfigInfo device = LoadDevice(devicePath);
            if (device == null || device.DoseRateConfig == null)
            {
                Ok(false, "конфигурацию " + devicePath + " прочитать не удалось");
                return;
            }

            ResultData data = LoadSpectrum(spectrumPath);
            if (data == null)
            {
                Ok(false, "спектр " + spectrumPath + " прочитать не удалось");
                return;
            }

            int n = device.DoseRateConfig.DoseRateCalibrationPoints.Count;
            Console.WriteLine("  конфигурация: {0} ({1} точек)", Path.GetFileName(devicePath), n);
            Console.WriteLine("  спектр:       {0}", Path.GetFileName(spectrumPath));

            var manager = new DoseRateManager(Config());

            // Как сейчас.
            DoseRate before = manager.Calculate(data, device.DoseRateConfig);
            bool shownBefore = n > 0;   // гейт MainForm.ShowDoseRate
            Console.WriteLine();
            Console.WriteLine("  СЕЙЧАС: гейт MainForm ({0} > 0) — {1}", n, shownBefore ? "показывает" : "молчит");
            // ⚠ Это не печать числа, а ГОТОВАЯ строка показания: `DoseRate.ToString`
            // складывает её сам и целиком инвариантом (`DoseRate.cs`), перегрузки
            // с провайдером у него нет. Печатается ровно то, что увидит человек.
            Console.WriteLine("          {0}", before.ToString());

            // После чистки (г): точки не читаются, список пуст.
            var empty = new DoseRateConfig();
            DoseRate after = manager.Calculate(data, empty);
            bool shownAfter = empty.DoseRateCalibrationPoints.Count > 0;
            Console.WriteLine();
            Console.WriteLine("  ПОСЛЕ (г) без замены: гейт MainForm (0 > 0) — {0}",
                              shownAfter ? "показывает" : "МОЛЧИТ, строка состояния пуста");
            Console.WriteLine("          расчёт вернул бы: {0} (rate={1})",
                              after.ToString(), after.Rate.ToString("f6", CultureInfo.InvariantCulture));

            Ok(shownBefore && !shownAfter,
               "измерено: снятие точек ДО замены расчёта убирает показание совсем "
               + "(было " + before.Rate.ToString("f4", CultureInfo.InvariantCulture) + " мкЗв/ч, станет пусто)");
        }

        static ResultData LoadSpectrum(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                    return file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        static GlobalConfigManager Config()
        {
            var manager = new GlobalConfigManager();
            var info = new GlobalConfigInfo();
            if (info.ColorConfig != null
                && (info.ColorConfig.SpectrumColorList == null || info.ColorConfig.SpectrumColorList.Count == 0))
            {
                info.ColorConfig.InitializeSpectrumColor();
            }

            manager.GlobalConfig = info;
            return manager;
        }

        // ==================================================================
        // §4. AMBER14, находка 1: мёртвое поле
        // ==================================================================

        static void DeadEfficiencyField()
        {
            Head("§4. AMBER14(1). Кто читает DoseRateSpectrumChoice.Efficiency — разбор собранного кода");

            Type choice = typeof(DoseRateSpectrumChoice);

            // Порча `dead`: спрашиваем про заведомо ЖИВОЕ свойство под видом
            // мёртвого. Раздел обязан отказать — иначе он не мерит ничего.
            string deadName = sabotage == "dead" ? "Spectrum" : "Efficiency";
            string liveName = "Spectrum";

            int deadReads = CountCalls(choice, "get_" + deadName);
            int deadWrites = CountCalls(choice, "set_" + deadName);
            int liveReads = CountCalls(choice, "get_" + liveName);

            Console.WriteLine("  {0}.{1}: чтений {2}, записей {3}", choice.Name, deadName, deadReads, deadWrites);
            Console.WriteLine("  {0}.{1}: чтений {2}   ← положительный контроль скана",
                              choice.Name, liveName, liveReads);

            Ok(liveReads > 0,
               "скан вообще работает: у соседнего свойства " + liveName + " найдено " + liveReads + " чтений");
            Ok(deadReads == 0,
               deadReads == 0
                   ? "подтверждено: " + deadName + " не читает НИКТО (0 чтений при " + deadWrites + " записи)"
                   : "посылка НЕВЕРНА: " + deadName + " читают " + deadReads + " раз");
        }

        /// <summary>
        /// Сколько тел методов сборки зовут указанный метод типа. Байтовый
        /// проход по `call`/`callvirt`, как у `RestCultureProbeF47`: случайное
        /// совпадение байтов не резолвится в метод и отсеивается само.
        /// </summary>
        static int CountCalls(Type owner, string methodName)
        {
            MethodInfo target = owner.GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic);
            if (target == null) return -1;

            int found = 0;
            Assembly asm = owner.Assembly;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            foreach (Type t in types)
            {
                MethodBase[] members;
                try
                {
                    members = t.GetMethods(BindingFlags.Instance | BindingFlags.Static
                                           | BindingFlags.Public | BindingFlags.NonPublic
                                           | BindingFlags.DeclaredOnly)
                               .Cast<MethodBase>()
                               .Concat(t.GetConstructors(BindingFlags.Instance | BindingFlags.Static
                                                         | BindingFlags.Public | BindingFlags.NonPublic
                                                         | BindingFlags.DeclaredOnly)
                                        .Cast<MethodBase>())
                               .ToArray();
                }
                catch (Exception) { continue; }

                foreach (MethodBase mb in members)
                {
                    byte[] il;
                    try
                    {
                        MethodBody body = mb.GetMethodBody();
                        if (body == null) continue;
                        il = body.GetILAsByteArray();
                    }
                    catch (Exception) { continue; }

                    if (il == null) continue;
                    Module mod = mb.Module;
                    Type[] gt = null, gm = null;
                    try { if (t.IsGenericType) gt = t.GetGenericArguments(); }
                    catch (Exception) { }
                    try { if (mb.IsGenericMethodDefinition) gm = mb.GetGenericArguments(); }
                    catch (Exception) { }

                    for (int i = 0; i + 4 < il.Length; i++)
                    {
                        byte op = il[i];
                        if (op != 0x28 && op != 0x6F) continue;
                        MethodBase callee;
                        try { callee = mod.ResolveMethod(BitConverter.ToInt32(il, i + 1), gt, gm); }
                        catch (Exception) { continue; }
                        if (callee == null || callee.DeclaringType != owner) continue;
                        if (callee.Name != methodName) continue;
                        found++;
                        Console.WriteLine("      ← {0}.{1}", t.FullName, mb.Name);
                    }
                }
            }

            return found;
        }

        // ==================================================================
        // §5. AMBER14, находка 2: N42 не несёт кривую
        // ==================================================================

        static void N42CarriesNoCurve()
        {
            Head("§5. AMBER14(2). Несёт ли схема N42 кривую эффективности");

            // Порча `n42`: ищем как «запрещённое» слово `EnergyCalibration`,
            // которое там ЕСТЬ. Раздел обязан отказать.
            string forbidden = sabotage == "n42" ? "EnergyCalibration" : "Efficiency";
            string control = "EnergyCalibration";

            Assembly asm = typeof(DoseRateSpectrumChoice).Assembly;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

            Type[] n42 = types.Where(t => t.Namespace != null
                                          && t.Namespace.StartsWith("BecquerelMonitor.N42", StringComparison.Ordinal))
                              .ToArray();
            Console.WriteLine("  типов в BecquerelMonitor.N42: {0}", n42.Length);

            var hitsForbidden = new List<string>();
            var hitsControl = new List<string>();
            foreach (Type t in n42)
            {
                if (t.Name.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0)
                    hitsForbidden.Add(t.FullName + " (тип)");
                if (t.Name.IndexOf(control, StringComparison.OrdinalIgnoreCase) >= 0)
                    hitsControl.Add(t.FullName + " (тип)");

                MemberInfo[] members;
                try
                {
                    members = t.GetMembers(BindingFlags.Instance | BindingFlags.Static
                                           | BindingFlags.Public | BindingFlags.NonPublic
                                           | BindingFlags.DeclaredOnly);
                }
                catch (Exception) { continue; }

                foreach (MemberInfo m in members)
                {
                    if (m.Name.IndexOf(forbidden, StringComparison.OrdinalIgnoreCase) >= 0)
                        hitsForbidden.Add(t.Name + "." + m.Name);
                    if (m.Name.IndexOf(control, StringComparison.OrdinalIgnoreCase) >= 0)
                        hitsControl.Add(t.Name + "." + m.Name);
                }
            }

            Console.WriteLine("  членов со словом «{0}»: {1}", control, hitsControl.Count);
            foreach (string s in hitsControl.Take(6)) Console.WriteLine("      " + s);
            Console.WriteLine("  членов со словом «{0}»: {1}", forbidden, hitsForbidden.Count);
            foreach (string s in hitsForbidden.Take(6)) Console.WriteLine("      " + s);

            Ok(hitsControl.Count > 0,
               "поиск вообще работает: «" + control + "» в схеме найден " + hitsControl.Count + " раз");
            Ok(hitsForbidden.Count == 0,
               hitsForbidden.Count == 0
                   ? "подтверждено: слова «" + forbidden + "» в схеме N42 нет вовсе"
                   : "посылка НЕВЕРНА: «" + forbidden + "» встречается " + hitsForbidden.Count + " раз");

            // Вывоз есть — и он пишет калибровку, но не кривую.
            MethodInfo export = null;
            Type util = types.FirstOrDefault(t => t.FullName == "BecquerelMonitor.N42.Util");
            if (util != null)
            {
                export = util.GetMethod("ExportToN42", BindingFlags.Public | BindingFlags.NonPublic
                                                       | BindingFlags.Static | BindingFlags.Instance);
            }

            Ok(export != null, export != null
                ? "вывоз в N42 существует: BecquerelMonitor.N42.Util.ExportToN42"
                : "BecquerelMonitor.N42.Util.ExportToN42 НЕ найден");
        }

        // ==================================================================

        static object Field(object instance, string name)
        {
            FieldInfo f = instance.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return f == null ? null : f.GetValue(instance);
        }
    }
}
