using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FwhmReaderProbeF62
{
    /// <summary>
    /// ЧИТАТЕЛИ ПРИЧИНЫ, ПО КОТОРОЙ НЕ СТРОИТСЯ КРИВАЯ РАЗРЕШЕНИЯ (`A240`,
    /// полоса F62, 06.09.2026).
    ///
    /// ⛔ ЧТО МЕРИТСЯ И ЗАЧЕМ. `A235` завела причину отказа
    /// <c>FwhmCalibration.DefaultCalibration</c> словами и с тремя числами, а
    /// `A240` — о том, что читателя у неё нет нигде, кроме дверей ввоза и (с
    /// полосы F54) открытия. Полоса F62 завела читателей ещё в четырёх местах,
    /// и эта проба меряет ИХ, а не наличие правки в исходнике:
    ///
    ///   --mode=view        вкладка ПШПВ ПОКАЗЫВАЕТ причину на месте пустой
    ///                      таблицы (<c>DCFwhmCalibrationView</c>) и НЕ ГОВОРИТ
    ///                      ни слова. Три плеча: отказ → причина видна и голоса
    ///                      нет; починка → подпись пустой таблицы вернулась
    ///                      СВОЯ, из resx; и та же проверка, что причина не
    ///                      осталась от прошлого спектра.
    ///   --mode=apply       панель управления при СМЕНЕ ПРИБОРА
    ///                      (<c>DCControlPanel.ApplyDeviceConfigChange</c>)
    ///                      говорит РОВНО ОДИН раз и называет три числа;
    ///                      на годной настройке — молчит.
    ///   --mode=door        ⛔ ДВОЙНОГО ГОЛОСА НЕТ: одно событие (создание
    ///                      документа, запись, открытие его же дверью
    ///                      <c>OpenDocument</c>) — РОВНО ОДНА строка «BecqMoni:».
    ///                      Молчаливое плечо: та же запись с годными числами.
    ///   --mode=effmaker    <c>EfficiencyFitter.LoadResultData</c> кладёт
    ///                      причину в текст броска, который показывает
    ///                      вызывающий; на годной настройке броска нет вовсе.
    ///   --mode=unreachable конструктор <c>FWHMPeakDetectionMethodConfig()</c>
    ///                      отказать НЕ МОЖЕТ (три числа — постоянные поля
    ///                      класса), а конструктор КОПИИ — может; и причина
    ///                      ОДНА И ТА ЖЕ при повторном спросе, на чём и стоит
    ///                      довод «в служебных местах причину не носят, её
    ///                      спрашивают заново».
    ///   --mode=all         все пять подряд.
    ///
    /// ⛔ У КАЖДОГО ПЛЕЧА ЕСТЬ ПРОТИВОПОЛОЖНОЕ. Плечо «сказал» без плеча
    /// «молчит» мерит наличие строки в коде, а не читателя: сторож, который
    /// срабатывает всегда, неотличим от сторожа, который не смотрит.
    ///
    /// ⚠ Правка конфигураций приборов живёт ТОЛЬКО в памяти прогона — на диск
    /// проба их не пишет (поставочные `config\device\*.xml` трогать нельзя,
    /// приказ Amber 05.09.2026).
    ///
    /// ⚠ Свои файлы (записанные документы) проба кладёт в СВОЙ каталог —
    /// рядом с exe либо в <c>--out=</c>, а не в текущий.
    ///
    ///   FwhmReaderProbeF62.exe [--mode=all|view|apply|door|effmaker|unreachable]
    ///                          [--out=&lt;каталог&gt;]
    ///
    /// Код возврата: 0 — все плечи сошлись; 1 — не сошлось хоть одно;
    /// 2 — ключи не разобраны.
    /// </summary>
    static class Program
    {
        // Заведомо негодная настройка: ширина при нуле БОЛЬШЕ ширины на опорном
        // канале, прямая убывает, PerformCalibration её не принимает.
        const double BadAt0 = 40.5;
        const double BadCh = 1000.0;
        const double BadWidth = 1.25;

        static string mode = "all";
        static string outDir;
        static int failed;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            foreach (string a in args)
            {
                if (a.StartsWith("--mode=")) mode = a.Substring(7);
                else if (a.StartsWith("--out=")) outDir = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (string.IsNullOrEmpty(outDir))
            {
                outDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            }
            Directory.CreateDirectory(outDir);

            // ⛔ КУЛЬТУРА ПОТОКА — ru-RU НАРОЧНО (`A242`). На культуре, где
            //    разделитель дробной части запятая, видно, печатает ли причина
            //    свои три числа ТОЧКОЙ сама по себе. На культуре по умолчанию
            //    этот замер ничего бы не значил.
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("ru-RU");
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru-RU");
            }
            catch (Exception) { }

            // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            string asm = typeof(DocumentManager).Assembly.Location;
            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + asm);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(asm)
                                                 .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine("  культура потока: " + Thread.CurrentThread.CurrentCulture.Name);
            Console.WriteLine("  свои файлы: " + outDir);
            Console.WriteLine();

            // ⛔ МЕНЕДЖЕРЫ-ОДИНОЧКИ ПРОГРЕВАЮТСЯ ЗАРАНЕЕ. Они пишут в поток
            //    ошибок при ПЕРВОМ обращении, а первое обращение случается
            //    внутри первой же двери — то есть их болтовня попала бы в
            //    перехват и посчиталась голосом (поймано полосой F54).
            {
                TextWriter warmErr = Console.Error;
                Console.SetError(new StringWriter());
                try
                {
                    DeviceConfigManager.GetInstance();
                    NuclideDefinitionManager.GetInstance();
                    GlobalConfigManager.GetInstance();
                    ROIConfigManager.GetInstance();
                }
                finally { Console.SetError(warmErr); }
            }

            bool all = mode == "all";
            if (all || mode == "unreachable") Unreachable();
            if (all || mode == "view") View();
            if (all || mode == "apply") Apply();
            if (all || mode == "door") Door();
            if (all || mode == "effmaker") EffMaker();
            if (!all && mode != "unreachable" && mode != "view" && mode != "apply"
                && mode != "door" && mode != "effmaker")
            {
                Console.Error.WriteLine("неизвестный --mode: " + mode);
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "СТОРОЖ ПРОШЁЛ: все плечи сошлись."
                : "ОТКАЗ СТОРОЖА: не сошлось плеч — " + failed.ToString(CultureInfo.InvariantCulture));
            return failed == 0 ? 0 : 1;
        }

        // ==================================================================
        // ПЛЕЧО 1. Достижимость отказа у двух конструкторов настроек.
        // ==================================================================
        static void Unreachable()
        {
            Console.WriteLine("=== КОНСТРУКТОРЫ НАСТРОЕК ПОИСКА ПИКОВ ===");

            FWHMPeakDetectionMethodConfig fresh = new FWHMPeakDetectionMethodConfig();
            Console.WriteLine("  свежая настройка: FWHM_AT_0=" + Num(fresh.FWHM_AT_0)
                              + "  Ch_Fwhm=" + Num(fresh.Ch_Fwhm)
                              + "  Width_Fwhm=" + Num(fresh.Width_Fwhm));
            Check("умолчание у СВЕЖЕЙ настройки строится (отказ недостижим)",
                  fresh.FwhmCalibration != null);

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ той же половины: те же три числа,
            //    испорченные, — и конструктор КОПИИ обязан отказать. Без него
            //    «строится всегда» неотличимо от «проверка слепа».
            FWHMPeakDetectionMethodConfig bad = new FWHMPeakDetectionMethodConfig();
            Spoil(bad);
            // ⚠ Кривую у ИСТОЧНИКА снимаем нарочно: конструктор копии зовёт
            //    умолчание ТОЛЬКО когда её нет, иначе он её просто клонирует, и
            //    замер мерил бы клонирование, а не отказ.
            bad.FwhmCalibration = null;
            FWHMPeakDetectionMethodConfig copy = (FWHMPeakDetectionMethodConfig)bad.Clone();
            Check("умолчание у КОПИИ негодной настройки НЕ строится (отказ достижим)",
                  copy.FwhmCalibration == null);

            // ⛔ ПРИЧИНА ОДНА И ТА ЖЕ ПРИ ПОВТОРНОМ СПРОСЕ. На этом стоит довод,
            //    которым четыре служебных места оставлены на старой подписи:
            //    причину не носят с собой, её спрашивают заново там, где есть
            //    человек. Если бы метод был с памятью или со случайностью,
            //    довод был бы неверен.
            string first, second;
            FwhmCalibration.DefaultCalibration(bad, new PolynomialEnergyCalibration(), out first);
            FwhmCalibration.DefaultCalibration(bad, new PolynomialEnergyCalibration(), out second);
            Console.WriteLine("  причина: " + Flat(first));
            Check("причина не пуста", !string.IsNullOrEmpty(first));
            Check("причина ПОВТОРИМА (спрошена дважды — та же)", first == second);
            Check("причина называет три числа ТОЧКОЙ на культуре ru-RU (`A242`)",
                  first != null && first.Contains("40.5") && first.Contains("1.25")
                  && !first.Contains("40,5") && !first.Contains("1,25"));
            Console.WriteLine();
        }

        // ==================================================================
        // ПЛЕЧО 2. Вкладка ПШПВ: ПОКАЗЫВАЕТ причину и НЕ говорит.
        // ==================================================================
        static void View()
        {
            Console.WriteLine("=== ВКЛАДКА ПШПВ: ЧИТАТЕЛЬ НА МЕСТЕ ПУСТОЙ ТАБЛИЦЫ ===");

            DeviceConfigInfo device = DeviceConfigManager.GetInstance().DeviceConfigList[0];
            FWHMPeakDetectionMethodConfig deviceCfg =
                (FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig;
            double[] saved = Save(deviceCfg);
            FwhmCalibration savedCurve = deviceCfg.FwhmCalibration;
            try
            {
                Spoil(deviceCfg);
                deviceCfg.FwhmCalibration = null;
                DocEnergySpectrum doc = Silently(() =>
                    DocumentManager.GetInstance().CreateDocument("f62-view.xml"));
                if (doc == null) { Check("документ построен", false); return; }

                ResultData rd = doc.ActiveResultData;
                rd.FwhmCalibration = null;
                FWHMPeakDetectionMethodConfig cfg =
                    (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
                Spoil(cfg);
                // ⚠ Без этого вид до `DefaultCalibration` не доходит вовсе:
                //    `EnsureFwhmCalibration` спрашивает умолчание ТОЛЬКО когда
                //    у настроек кривой нет. Поймано первым прогоном.
                cfg.FwhmCalibration = null;

                MainForm form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
                SetField(form, "activeDocument", doc);
                SetField(form, "documentManager", DocumentManager.GetInstance());
                DCFwhmCalibrationView view = new DCFwhmCalibrationView(form);

                string designer = NoItemsText(view);
                Console.WriteLine("  подпись пустой таблицы из resx: «" + Flat(designer) + "»");
                Check("подпись пустой таблицы не пуста (её есть куда возвращать)",
                      !string.IsNullOrEmpty(designer));

                // ПЛЕЧО «ОТКАЗ»: причина обязана появиться, голоса быть не должно.
                string said = Voices(() => view.UpdateFwhmCalibration());
                string shown = NoItemsText(view);
                Console.WriteLine("  ОТКАЗ  → в таблице: «" + Flat(shown) + "»");
                Console.WriteLine("         → сказано: " + (said.Length == 0 ? "(молча)" : said));
                Check("причина ВИДНА на месте пустой таблицы",
                      shown != null && shown.Contains("40.5") && shown.Contains("1.25"));
                Check("вид НЕ ГОВОРИТ (второго голоса об одной беде нет)", said.Length == 0);
                Check("подсказка на таблице несёт ту же причину",
                      ToolTipOf(view, "CollectedPeaksTable") == shown);

                // ПЛЕЧО «ПОЧИНКА»: настройка годная — подпись обязана вернуться
                // СВОЯ, из resx, а кривая — построиться.
                Restore(cfg, saved);
                string said2 = Voices(() => view.UpdateFwhmCalibration());
                string back = NoItemsText(view);
                Console.WriteLine("  ГОДНАЯ → в таблице: «" + Flat(back) + "»");
                Check("кривая построилась", rd.FwhmCalibration != null);
                Check("подпись пустой таблицы ВЕРНУЛАСЬ своя (причина не залипла)",
                      back == designer);
                Check("подсказка снята", string.IsNullOrEmpty(ToolTipOf(view, "CollectedPeaksTable")));
                Check("и на годной настройке вид молчит", said2.Length == 0);
            }
            finally
            {
                Restore(deviceCfg, saved);
                deviceCfg.FwhmCalibration = savedCurve;
            }
            Console.WriteLine();
        }

        // ==================================================================
        // ПЛЕЧО 3. Панель управления: смена прибора говорит ОДИН раз.
        // ==================================================================
        static void Apply()
        {
            Console.WriteLine("=== ПАНЕЛЬ УПРАВЛЕНИЯ: СМЕНА ПРИБОРА ===");

            DeviceConfigInfo device = DeviceConfigManager.GetInstance().DeviceConfigList[0];
            FWHMPeakDetectionMethodConfig deviceCfg =
                (FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig;
            double[] saved = Save(deviceCfg);
            FwhmCalibration savedCurve = deviceCfg.FwhmCalibration;
            try
            {
                DocEnergySpectrum doc = Silently(() =>
                    DocumentManager.GetInstance().CreateDocument("f62-apply.xml"));
                if (doc == null) { Check("документ построен", false); return; }

                MainForm form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
                SetField(form, "activeDocument", doc);
                SetField(form, "documentManager", DocumentManager.GetInstance());
                DCControlPanel panel = new DCControlPanel(form);

                // ⛔ ЗОВЁТСЯ ПОМОЩНИК, А НЕ ВЕСЬ `ApplyDeviceConfigChange`, и это
                //    не для удобства. В большом методе стоит модальное окно
                //    («спектр будет стёрт», OK/Отмена), и проба, назвавшая тот
                //    метод, делает окно достижимым БЕЗ ОКОН: сторож
                //    `tools\check_headless.py` краснеет по правилу
                //    REFLECT_OVERRIDE, а безоконный прогон рискует повиснуть
                //    (`S100`). Голос вынесен в помощника ровно затем, чтобы
                //    мериться, не поднимая окна; ветвление же вокруг него —
                //    «у настроек прибора кривой нет» — одна строка, и её видно
                //    в исходнике.
                ResultData rd = doc.ActiveResultData;

                // ПЛЕЧО «ОТКАЗ»: умолчание по трём числам не строится.
                Spoil(deviceCfg);
                rd.FwhmCalibration = null;
                string said = Voices(() =>
                    rd.FwhmCalibration = (FwhmCalibration)Invoke(panel, "DefaultFwhmOrSay",
                                                                 deviceCfg, rd));
                Console.WriteLine("  ОТКАЗ  → сказано: " + (said.Length == 0 ? "(МОЛЧА)" : said));
                Check("панель СКАЗАЛА ровно один раз", Lines(said) == 1);
                Check("кривой при этом НЕТ", rd.FwhmCalibration == null);
                Check("причина названа тремя числами",
                      said.Contains("40.5") && said.Contains("1.25"));
                Check("названа и конфигурация прибора", said.Contains(device.Name));
                Check("числа напечатаны ТОЧКОЙ на культуре ru-RU (`A242`)",
                      !said.Contains("40,5") && !said.Contains("1,25"));

                // ПЛЕЧО «МОЛЧАНИЕ»: то же движение, настройка годная.
                Restore(deviceCfg, saved);
                rd.FwhmCalibration = null;
                string quiet = Voices(() =>
                    rd.FwhmCalibration = (FwhmCalibration)Invoke(panel, "DefaultFwhmOrSay",
                                                                 deviceCfg, rd));
                Console.WriteLine("  ГОДНАЯ → сказано: " + (quiet.Length == 0 ? "(молча)" : quiet));
                Check("на годной настройке панель МОЛЧИТ", Lines(quiet) == 0);
                Check("и кривая у спектра появилась", rd.FwhmCalibration != null);
            }
            finally
            {
                Restore(deviceCfg, saved);
                deviceCfg.FwhmCalibration = savedCurve;
            }
            Console.WriteLine();
        }

        // ==================================================================
        // ПЛЕЧО 4. Одно событие — один голос (создание, запись, открытие).
        // ==================================================================
        static void Door()
        {
            Console.WriteLine("=== ОДНО СОБЫТИЕ — ОДИН ГОЛОС ===");

            DeviceConfigInfo device = DeviceConfigManager.GetInstance().DeviceConfigList[0];
            FWHMPeakDetectionMethodConfig deviceCfg =
                (FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig;
            double[] saved = Save(deviceCfg);
            FwhmCalibration savedCurve = deviceCfg.FwhmCalibration;
            try
            {
                Spoil(deviceCfg);
                deviceCfg.FwhmCalibration = null;

                // Создание документа — СОБЫТИЕ ПЕРВОЕ. Через него проходят
                // CreateResultData, конструктор копии настроек, конструктор
                // копии конфигурации прибора и CheckDocument: заговори любой из
                // них — строк стало бы больше одной.
                DocEnergySpectrum doc = null;
                string onCreate = Voices(() =>
                    doc = DocumentManager.GetInstance().CreateDocument("f62-door.xml"));
                Console.WriteLine("  СОЗДАНИЕ → " + (onCreate.Length == 0 ? "(МОЛЧА)" : onCreate));
                Check("создание документа сказало РОВНО ОДИН раз", Lines(onCreate) == 1);
                if (doc == null) { Check("документ построен", false); return; }
                Check("причина названа тремя числами",
                      onCreate.Contains("40.5") && onCreate.Contains("1.25"));

                string bad = Write(doc, "f62-door-bad.xml");

                // ⛔ НАХОДКА F62, ИЗМЕРЕННАЯ ЗДЕСЬ, А НЕ ВЫВЕДЕННАЯ. Открытие
                //    сохранённого документа БЕЗ кривой строит её не по
                //    настройкам прибора, а по ВСТРОЕННЫМ УМОЛЧАНИЯМ класса
                //    (15 / 3756 / 103) — то есть по модели разрешения
                //    ВЫДУМАННОГО прибора, и молча. Причин две, обе в
                //    `DocumentManager` (файл чужой полосы, чинить отсюда
                //    нельзя): `ResultData.PeakDetectionMethodConfig` помечен
                //    `[XmlIgnore]` (~~`S82`~~) — в файле его нет никогда, а
                //    `new ResultData()` заводит свежий с постоянными
                //    умолчаниями; и `PrepareDeviceConfig`, который подставляет
                //    настройки ПРИБОРА, зовётся ПОСЛЕ `CheckDocument`, а не до.
                //    Следствие для самой `A240`: голос, поставленный полосой
                //    F54 в `OpenDocument`, на этом пути прозвучать НЕ МОЖЕТ —
                //    встроенные три числа растут всегда.
                //    Ниже это ПЕЧАТАЕТСЯ как замер, а не судится как плечо:
                //    судить находку значило бы завести сторожа, который
                //    покраснеет от ПОЧИНКИ.
                DocEnergySpectrum opened = null;
                string onOpen = Voices(() =>
                    opened = DocumentManager.GetInstance().OpenDocument(bad));
                Console.WriteLine("  ОТКРЫТИЕ → " + (onOpen.Length == 0 ? "(МОЛЧА)" : onOpen));
                Check("документ открылся", opened != null);
                Console.WriteLine("    замер: голосов при открытии — "
                                  + Lines(onOpen).ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("    замер: кривая после открытия — "
                                  + Points(opened));
                Console.WriteLine("    замер: три числа у КОНФИГУРАЦИИ ПРИБОРА в этот миг — "
                                  + Num(deviceCfg.FWHM_AT_0) + " / " + Num(deviceCfg.Ch_Fwhm)
                                  + " / " + Num(deviceCfg.Width_Fwhm));

                // ⛔ КОНТРОЛЬ, БЕЗ КОТОРОГО ЗАМЕР ВЫШЕ НИЧЕГО НЕ ЗНАЧИТ: проба
                //    обязана УМЕТЬ отличить кривую из файла от встроенной.
                //    Запись с приметными опорными точками (111 и 222) должна
                //    открыться СО СВОИМИ.
                DocEnergySpectrum marked = Silently(() =>
                    DocumentManager.GetInstance().CreateDocument("f62-door-mark.xml"));
                foreach (ResultData rd in marked.ResultDataFile.ResultDataList)
                {
                    SimpleSqrtFwhmCalibration own = new SimpleSqrtFwhmCalibration();
                    own.CalibrationPeaks.Add(new CalibrationPeak { Channel = 111, Energy = 111.0, FWHM = 5.0 });
                    own.CalibrationPeaks.Add(new CalibrationPeak { Channel = 222, Energy = 222.0, FWHM = 9.0 });
                    own.PerformCalibration(rd.EnergySpectrum.NumberOfChannels);
                    rd.FwhmCalibration = own;
                }
                string markPath = Write(marked, "f62-door-mark.xml");
                DocEnergySpectrum openedMark = Silently(() =>
                    DocumentManager.GetInstance().OpenDocument(markPath));
                Console.WriteLine("    контроль: запись СО СВОЕЙ кривой открылась как " + Points(openedMark));
                Check("проба РАЗЛИЧАЕТ кривую из файла и встроенную (контроль замера)",
                      Points(openedMark).Contains("111") && !Points(openedMark).Contains("3756"));

                // ПЛЕЧО МОЛЧАНИЯ: три числа годные. Кривой в записи
                // по-прежнему нет — CheckDocument её достроит и промолчит.
                Restore(deviceCfg, saved);
                DocEnergySpectrum good = Silently(() =>
                    DocumentManager.GetInstance().CreateDocument("f62-door-ok.xml"));
                foreach (ResultData rd in good.ResultDataFile.ResultDataList)
                {
                    rd.FwhmCalibration = null;
                    FWHMPeakDetectionMethodConfig c =
                        rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
                    if (c != null) c.FwhmCalibration = null;
                }
                string okFile = Write(good, "f62-door-good.xml");
                DocEnergySpectrum openedOk = null;
                string quiet = Voices(() =>
                    openedOk = DocumentManager.GetInstance().OpenDocument(okFile));
                Console.WriteLine("  ГОДНАЯ ЗАПИСЬ → " + (quiet.Length == 0 ? "(молча)" : quiet));
                Check("годная запись открывается МОЛЧА", Lines(quiet) == 0);
                Check("и кривая у неё построена",
                      openedOk != null && openedOk.ActiveResultData.FwhmCalibration != null);
            }
            finally
            {
                Restore(deviceCfg, saved);
                deviceCfg.FwhmCalibration = savedCurve;
            }
            Console.WriteLine();
        }

        // ==================================================================
        // ПЛЕЧО 5. Мастер эффективности: причина едет в тексте броска.
        // ==================================================================
        static void EffMaker()
        {
            Console.WriteLine("=== МАСТЕР ЭФФЕКТИВНОСТИ: ПРИЧИНА В ТЕКСТЕ ОТКАЗА ===");

            DeviceConfigInfo device = DeviceConfigManager.GetInstance().DeviceConfigList[0];
            FWHMPeakDetectionMethodConfig deviceCfg =
                (FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig;
            double[] saved = Save(deviceCfg);
            FwhmCalibration savedCurve = deviceCfg.FwhmCalibration;
            try
            {
                DocEnergySpectrum doc = Silently(() =>
                    DocumentManager.GetInstance().CreateDocument("f62-eff.xml"));
                if (doc == null) { Check("документ построен", false); return; }
                foreach (ResultData rd in doc.ResultDataFile.ResultDataList)
                {
                    rd.FwhmCalibration = null;
                }
                string path = Write(doc, "f62-eff.xml");

                // ПЛЕЧО «ОТКАЗ»: у конфигурации прибора кривой нет и умолчание
                // по трём её числам не строится.
                deviceCfg.FwhmCalibration = null;
                Spoil(deviceCfg);
                string message = null;
                try
                {
                    EfficiencyFitter.LoadResultData(path, 0, null);
                }
                catch (Exception ex) { message = ex.Message; }
                Console.WriteLine("  ОТКАЗ  → " + Flat(message));
                Check("бросок случился", message != null);
                Check("в тексте броска названы три числа",
                      message != null && message.Contains("40.5") && message.Contains("1.25"));
                Check("числа напечатаны ТОЧКОЙ на культуре ru-RU (`A242`)",
                      message != null && !message.Contains("40,5"));

                // ПЛЕЧО «МОЛЧАНИЕ»: настройка годная — броска нет, кривая есть.
                Restore(deviceCfg, saved);
                deviceCfg.FwhmCalibration = null;
                ResultData loaded = null;
                string failure = null;
                try { loaded = EfficiencyFitter.LoadResultData(path, 0, null); }
                catch (Exception ex) { failure = ex.Message; }
                Console.WriteLine("  ГОДНАЯ → " + (failure == null ? "броска нет" : Flat(failure)));
                Check("на годной настройке броска НЕТ", failure == null);
                Check("и кривая у спектра есть", loaded != null && loaded.FwhmCalibration != null);
            }
            finally
            {
                Restore(deviceCfg, saved);
                deviceCfg.FwhmCalibration = savedCurve;
            }
            Console.WriteLine();
        }

        // ==================================================================
        // ОСНАСТКА
        // ==================================================================

        static void Spoil(FWHMPeakDetectionMethodConfig cfg)
        {
            cfg.FWHM_AT_0 = BadAt0;
            cfg.Ch_Fwhm = BadCh;
            cfg.Width_Fwhm = BadWidth;
        }

        static double[] Save(FWHMPeakDetectionMethodConfig cfg)
        {
            return new double[] { cfg.FWHM_AT_0, cfg.Ch_Fwhm, cfg.Width_Fwhm };
        }

        static void Restore(FWHMPeakDetectionMethodConfig cfg, double[] v)
        {
            cfg.FWHM_AT_0 = v[0];
            cfg.Ch_Fwhm = v[1];
            cfg.Width_Fwhm = v[2];
        }

        /// <summary>
        /// Записать документ в СВОЙ каталог и вернуть путь. Имя файла меняется
        /// у самого документа, поэтому открывается потом КОПИЯ: `OpenDocument`
        /// отказывает, если документ с этим именем уже в списке открытых.
        /// </summary>
        static string Write(DocEnergySpectrum doc, string name)
        {
            string path = Path.Combine(outDir, name);
            doc.Filename = path;
            Silently<bool>(() => DocumentManager.GetInstance().SaveDocument(doc));
            string copy = Path.Combine(outDir, "open-" + name);
            File.Copy(path, copy, true);
            return copy;
        }

        /// <summary>
        /// Голоса двери: строки «BecqMoni: …» в потоке ошибок.
        ///
        /// ⚠ Бросок, случившийся ПОСЛЕ голоса, голосом не считается, но и не
        /// прячется: он печатается отдельной строкой. Молчащий перехват, в
        /// котором на самом деле упало, — это «отказ без отказа».
        /// </summary>
        static string Voices(Action body)
        {
            TextWriter real = Console.Error;
            StringWriter caught = new StringWriter();
            Console.SetError(caught);
            lastThrow = null;
            try { body(); }
            catch (Exception ex)
            {
                Exception e = ex is TargetInvocationException && ex.InnerException != null
                    ? ex.InnerException : ex;
                lastThrow = e.GetType().Name + ": " + Flat(e.Message);
            }
            finally { Console.SetError(real); }

            if (lastThrow != null) Console.WriteLine("    (после голоса упало) " + lastThrow);

            List<string> voices = new List<string>();
            foreach (string line in caught.ToString().Split('\n'))
            {
                string s = line.Trim();
                if (s.StartsWith("BecqMoni:")) voices.Add(s);
            }
            return string.Join(" | ", voices.ToArray());
        }

        static string lastThrow;

        static int Lines(string voices)
        {
            if (string.IsNullOrEmpty(voices)) return 0;
            return voices.Split(new string[] { " | " }, StringSplitOptions.None).Length;
        }

        static T Silently<T>(Func<T> body)
        {
            TextWriter real = Console.Error;
            Console.SetError(new StringWriter());
            try { return body(); }
            finally { Console.SetError(real); }
        }

        static object Invoke(object target, string method, params object[] args)
        {
            MethodInfo mi = target.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (mi == null) throw new InvalidOperationException("нет метода " + method);
            return mi.Invoke(target, args);
        }

        static void SetField(object target, string name, object value)
        {
            FieldInfo fi = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi == null) throw new InvalidOperationException("нет поля " + name);
            fi.SetValue(target, value);
        }

        static object Field(object target, string name)
        {
            FieldInfo fi = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return fi == null ? null : fi.GetValue(target);
        }

        static string NoItemsText(DCFwhmCalibrationView view)
        {
            XPTable.Models.Table table = (XPTable.Models.Table)Field(view, "CollectedPeaksTable");
            return table == null ? null : table.NoItemsText;
        }

        static string ToolTipOf(DCFwhmCalibrationView view, string control)
        {
            ToolTip tip = (ToolTip)Field(view, "toolTip1");
            Control c = (Control)Field(view, control);
            if (tip == null || c == null) return null;
            return tip.GetToolTip(c);
        }

        /// <summary>Опорные точки кривой разрешения активного спектра — словами.</summary>
        static string Points(DocEnergySpectrum doc)
        {
            if (doc == null) return "(документа нет)";
            FwhmCalibration c = doc.ActiveResultData == null ? null : doc.ActiveResultData.FwhmCalibration;
            if (c == null) return "КРИВОЙ НЕТ";
            if (c.CalibrationPeaks == null || c.CalibrationPeaks.Count == 0) return c.GetType().Name + " без опорных точек";
            StringBuilder sb = new StringBuilder(c.GetType().Name + " [");
            for (int i = 0; i < c.CalibrationPeaks.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(Num(c.CalibrationPeaks[i].Channel)).Append(':').Append(Num(c.CalibrationPeaks[i].FWHM));
            }
            return sb.Append(']').ToString();
        }

        static string Num(double v)
        {
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Flat(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ")
                    .Replace("   ", " ").Replace("  ", " ").Trim();
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine((ok ? "    [+] " : "    [ОТКАЗ] ") + what);
            if (!ok) failed++;
        }
    }
}
