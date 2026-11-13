using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER328`, полоса a328 12.11.2026, доделка a328b 13.11.2026) КНОПКА-ФЛАЖОК FSA
    /// РЯДОМ С КНОПКОЙ ПИКОВ — автомат связи «галочка FSA × пики» без окна приложения:
    /// документы собираются из файлов корпуса, кнопки нажимаются их обработчиками
    /// (отражением), окна на экран не выводятся (`MainForm` не создаётся, `Show` не зовётся).
    ///
    /// Правило (`DocEnergySpectrum.IsFsaShownBy`): FSA показан на вкладке ⇔ галочка
    /// FSA (`ChartViewConfig.FsaModeEnabled`, одна на приложение) ВКЛ И пики ВКЛ. Пики
    /// ОБЩИЕ (решение Amber 13.11.2026 «Пики станут общими»): кнопка пиков и её пункты
    /// меняют все вкладки и `ChartViewConfig.DefaultPeakMode`. ВКЛЮЧИТЬ галочку можно только
    /// при пиках ВКЛ («Кнопка FSA доступна к включению только после включения поиска
    /// пиков.»), выключить — всегда. Критерии:
    ///   К0 гигиена: кнопка FSA стоит СРАЗУ справа от кнопки пиков; пункт «Показать
    ///      FSA» списка фона виден, но недоступен; при пиках ВКЛ кнопка и «Включить» доступны;
    ///   К1 таблица истинности правила (4 сочетания);
    ///   К2 П4 — кнопка FSA ВКЛ: ВСЕ вкладки в ShowFSA (три вкладки, у третьей нет ни
    ///      кривой, ни FWHM — вопросов нет, исключения нет), событие входа у каждой одно
    ///      и с Explicit = false; кнопка цветная у всех;
    ///   К3 П1 — пики ВЫКЛ кнопкой ОДНОЙ вкладки: пики ВЫКЛ на ВСЕХ, каждая — в бывший до
    ///      FSA режим (три разных), галочка цела, `DefaultPeakMode` = ВЫКЛ, картинка кнопки
    ///      пиков у всех «нет пиков», `Enabled` метода у всех спектров ВЫКЛ, а конфиг
    ///      ПРИБОРА не тронут (Н4); кнопка FSA доступна (выключить), «Включить» — нет;
    ///   К4 П2 — пики ВКЛ кнопкой ДРУГОЙ вкладки: FSA вернулся на всех, событие у каждой
    ///      одно; то же пунктами списка пиков (с третьей и с первой вкладки);
    ///   К5 П3 — кнопка FSA ВЫКЛ (пунктом списка): галочка сброшена, все вкладки — в
    ///      свои бывшие режимы (три разных), кнопка серая у всех;
    ///   К6 сценарий 2: галочка ВЫКЛ — 4 нажатия пиков: пики у всех вкладок одни и те же,
    ///      `DefaultPeakMode` за ними, на FSA не влияют (ни режима, ни событий);
    ///   К7 доступность: пики ВЫКЛ и галочка ВЫКЛ — кнопка FSA и «Включить» недоступны у
    ///      всех вкладок, «Выключить» доступен; обработчики кнопки и «Включить», позванные
    ///      мимо недоступности, галочку НЕ включают; пики ВКЛ — доступно; галочка ВКЛ и
    ///      пики ВЫКЛ — кнопка доступна, её щелчок ВЫКЛЮЧАЕТ галочку, после чего кнопка
    ///      недоступна; пики ВКЛ при галочке ВЫКЛ — FSA нет;
    ///   К8 возврат в «Нормировку по эффективности» и её подмена на «Показать», когда
    ///      нормировать нечем;
    ///   К9 цикл кнопки фона FSA пропускает — с любого режима, 12 нажатий, ВСЕ ТРИ
    ///      вкладки (у C нет FWHM — «континуум» не падает, Н2);
    ///   К10 новая вкладка следует галочке и общим пикам (пики, выключенные кнопкой, —
    ///      и у новой вкладки ВЫКЛ; кнопка FSA у неё доступна только к выключению);
    ///   К11 конфиг галочки: круг через XML, элемент стоит ПОСЛЕ `DefaultBackgroundMode`;
    ///      старый файл с `DefaultBackgroundMode=ShowFSA` даёт галочку ВКЛ и фон Visible;
    ///      явная запись галочки перекрывает перенос; «перезапуск» — новая вкладка на
    ///      прочитанном конфиге входит в FSA;
    ///   К12 запоминание пиков: выключенные кнопкой пики пишутся в `DefaultPeakMode`,
    ///      ходят кругом через XML; «перезапуск» на прочитанном конфиге — новая вкладка
    ///      без пиков и без FSA (галочка ВКЛ), её кнопка пиков возвращает и пики, и FSA;
    ///   К13 окно настроек (Н3): `DefaultBackgroundMode` вне двух пунктов списка окно не
    ///      роняет и при сохранении не перезаписывается; пики, сменённые кнопкой при
    ///      открытом окне, сохранение окна не возвращает назад, а сменённые в окне — пишет;
    ///      пики, сменённые В ОКНЕ («Применить» — `AcceptFormContents`), приводят ВСЕ вкладки:
    ///      ВЫКЛ — FSA гаснет везде, каждая вкладка в свой бывший режим; ВКЛ — FSA возвращается;
    ///      «Применить» без смены вкладки не трогает (З1 критика crit328b, мутант V);
    ///   К14 (Н2) «континуум» на спектре без FWHM: исключения нет, континуума нет, есть
    ///      отказ с причиной (`EnergySpectrumView.ContinuumRefusal`) и он рисуется панелью;
    ///      контроль — тот же спектр С FWHM: континуум есть, отказа нет;
    ///   К15 (дефект 13.11.2026, полоса a328g) иконка и подсказка кнопки: при галочке ВКЛ на
    ///      кнопке `Resources.FSAToggle` попиксельно и НЕ `Resources.FSA`; два обновления
    ///      кнопки подряд и открытие её списка ссылку `Image` не меняют (ВКЛ и ВЫКЛ), картинка
    ///      одна на все вкладки; подсказка — одна строка не длиннее `AppUi.TipWidth` в en и ru;
    ///   конец: проба конфиг на диске НЕ пишет (mtime файла рядом с exe не сдвинулся).
    ///
    ///     fsatoggleprobea328 --spectrum=&lt;xml&gt; --second=&lt;xml&gt; [--spoil=peaks] [--only=n2]
    ///
    /// `--spoil=peaks` — положительный контроль судьи: после КАЖДОГО выключения пиков
    /// проба сама возвращает вкладки в ShowFSA (так вела бы себя сборка, где пики не
    /// гасят FSA) — К3/К6 обязаны отказать, код 1.
    /// `--only=n2` — только К14, и без единого члена, которого нет в сборке ДО правки
    /// (новые — отражением): так проба гоняется и против старого exe — контроль, что
    /// без правки исключение ловится (код 1).
    /// Вывод — «  ok   …» / «  ⛔  …», последняя строка «ВСЕ СОШЛИСЬ» / «НЕ СОШЛОСЬ: N».
    /// Коды: 0 — все критерии; 1 — есть отказы; 2 — мерить нечем (ключи, файлы).
    /// </summary>
    static class FsaToggleProbeA328
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int bad;
        static int good;
        static bool spoilPeaks;
        static readonly Dictionary<DocEnergySpectrum, List<bool>> entered = new Dictionary<DocEnergySpectrum, List<bool>>();

        // ⚠ Main не трогает ни одного члена, которого нет в сборке до правки: JIT
        // компилирует его целиком, и `--only=n2` против старого exe иначе не дошёл бы до К14.
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null, second = null;
            bool onlyN2 = false;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = arg.Substring(11);
                else if (arg.StartsWith("--second=", StringComparison.Ordinal)) second = arg.Substring(9);
                else if (arg == "--spoil=peaks") spoilPeaks = true;
                else if (arg == "--only=n2") onlyN2 = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + arg); return 2; }
            }
            if (spectrum == null || second == null)
            {
                Console.Error.WriteLine("нужны --spectrum=<xml> --second=<xml> [--spoil=peaks] [--only=n2]");
                return 2;
            }
            if (!File.Exists(spectrum) || !File.Exists(second))
            {
                Console.WriteLine("  ⛔ мерить нечем: нет файла спектра ({0} / {1})", spectrum, second);
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            }
            string configPath = Path.Combine(Path.GetDirectoryName(typeof(DocEnergySpectrum).Assembly.Location),
                                             "config", "BecquerelMonitor.xml");
            DateTime configStamp = File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;
            Console.WriteLine("конфиг рядом с exe: {0} ({1})", configPath, File.Exists(configPath) ? "есть" : "НЕТ");

            int code = onlyN2 ? RunN2Only(spectrum, nuclides) : RunAll(spectrum, second, nuclides);
            if (code == 2) return 2;

            Console.WriteLine();
            Same("проба конфиг на диске не писала (mtime рядом с exe)", configStamp,
                 File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue);
            Console.WriteLine();
            Console.WriteLine("сошлось {0}, отказов {1}", good, bad);
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            Console.Out.Flush();
            // фоновые счёты FSA вкладок процесс держать не должны
            Environment.Exit(bad == 0 ? 0 : 1);
            return bad == 0 ? 0 : 1;
        }

        static int RunN2Only(string spectrum, NuclideDefinitionManager nuclides)
        {
            Console.WriteLine("⚠ --only=n2: только К14 (против любой сборки, новые члены — отражением)");
            DocEnergySpectrum a = Open(spectrum, nuclides, false);
            DocEnergySpectrum c = Open(spectrum, nuclides, true);
            if (a == null || c == null) return 2;
            ContinuumSection(a, c);
            return 0;
        }

        static int RunAll(string spectrum, string second, NuclideDefinitionManager nuclides)
        {
            GlobalConfigManager manager = GlobalConfigManager.GetInstance();
            if (spoilPeaks)
            {
                Console.WriteLine("⚠ --spoil=peaks: после выключения пиков проба сама возвращает ShowFSA (контроль судьи)");
            }

            ChartViewConfig chart = manager.GlobalConfig.ChartViewConfig;
            chart.FsaModeEnabled = false;
            chart.DefaultPeakMode = PeakMode.Visible;
            chart.DefaultBackgroundMode = BackgroundMode.Visible;

            // К1 — правило одной строкой
            Console.WriteLine();
            Console.WriteLine("=== К1. правило показа: FSA ⇔ галочка И пики ===");
            MethodInfo rule = typeof(DocEnergySpectrum).GetMethod("IsFsaShownBy", Any);
            if (rule == null) { Console.WriteLine("  ⛔ обвязка: нет DocEnergySpectrum.IsFsaShownBy"); bad++; return 1; }
            foreach (bool flag in new[] { false, true })
            {
                foreach (PeakMode peaks in new[] { PeakMode.Invisible, PeakMode.Visible })
                {
                    bool got = (bool)rule.Invoke(null, new object[] { flag, peaks });
                    Same(string.Format("галочка {0}, пики {1}", On(flag), peaks == PeakMode.Visible ? "ВКЛ" : "ВЫКЛ"),
                         flag && peaks == PeakMode.Visible, got);
                }
            }

            DocEnergySpectrum a = Open(spectrum, nuclides, false);
            DocEnergySpectrum b = Open(second, nuclides, false);
            DocEnergySpectrum c = Open(spectrum, nuclides, true);
            if (a == null || b == null || c == null) return 2;
            var docs = new[] { a, b, c };
            List<DocEnergySpectrum> managed = DocumentManager.GetInstance().DocumentList;
            foreach (DocEnergySpectrum d in docs)
            {
                managed.Add(d);
                var list = new List<bool>();
                entered[d] = list;
                d.FsaModeEntered += (s, e) => list.Add(e.Explicit);
            }
            // Н4: кнопка пиков прежде писала `Enabled` и в конфигурацию ПРИБОРА — теперь нет
            string deviceEnabledBefore = DeviceEnabled(docs);
            Console.WriteLine("SETUP\tEnabled метода в конфигурации прибора у вкладок: {0}", deviceEnabledBefore);

            // К0 — гигиена
            Console.WriteLine();
            Console.WriteLine("=== К0. кнопка справа от кнопки пиков; «Показать FSA» виден, но недоступен ===");
            ToolStrip strip = (ToolStrip)Field(a, "toolStrip2");
            ToolStripItem peaksButton = (ToolStripItem)Field(a, "toolStripSplitButton9");
            ToolStripItem fsaButton = (ToolStripItem)Field(a, "toolStripSplitButtonFsa");
            ToolStripMenuItem showFsaItem = (ToolStripMenuItem)Field(a, "ShowFsaToolStripMenuItem");
            Same("кнопка FSA есть", true, fsaButton != null);
            int iPeaks = strip.Items.IndexOf(peaksButton), iFsa = strip.Items.IndexOf(fsaButton);
            Same(string.Format("кнопка FSA сразу справа от кнопки пиков (#{0} → #{1})", iPeaks, iFsa), iPeaks + 1, iFsa);
            Same("кнопка FSA — флажок с выпадающим списком (как кнопка пиков)", peaksButton.GetType(), fsaButton.GetType());
            Same("прозрачный цвет кнопки FSA — как у кнопки пиков", peaksButton.ImageTransparentColor, fsaButton.ImageTransparentColor);
            Same("подсказка кнопки FSA не пуста", true, !string.IsNullOrEmpty(fsaButton.ToolTipText));
            Same("«Показать FSA» после конструктора: недоступен", false, showFsaItem.Enabled);
            Same("«Показать FSA» в списке есть (Available)", true, showFsaItem.Available);
            Invoke(a, "toolStripSplitButton7_DropDownOpening", null, EventArgs.Empty);
            Same("«Показать FSA» после открытия списка: недоступен", false, showFsaItem.Enabled);
            Same("пики ВКЛ, галочка ВЫКЛ: кнопка FSA/«Включить»/«Выключить» доступны", "True/True/True", Availability(a));

            // исходное: галочка ВЫКЛ, пики ВКЛ у всех, у вкладок РАЗНЫЕ режимы фона
            foreach (DocEnergySpectrum d in docs) d.EnergySpectrumView.PeakMode = PeakMode.Visible;
            a.EnergySpectrumView.BackgroundMode = BackgroundMode.Invisible;
            b.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowContinuum;
            c.EnergySpectrumView.BackgroundMode = BackgroundMode.Substract;
            Same("кнопка FSA при галочке ВЫКЛ — серая", false, Colored(Button(a).Image));

            // К2 — П4
            Console.WriteLine();
            Console.WriteLine("=== К2. П4: кнопка FSA ВКЛ — все вкладки в FSA ===");
            ClearEvents();
            Exception thrown = null;
            try { Invoke(a, "toolStripSplitButtonFsa_ButtonClick", null, EventArgs.Empty); }
            catch (Exception ex) { thrown = ex; }
            Same("нажатие без исключения (у вкладки C нет кривой и FWHM — вопросов нет)", null,
                 thrown == null ? null : thrown.GetBaseException().GetType().Name + ": " + thrown.GetBaseException().Message);
            Same("галочка ВКЛ", true, chart.FsaModeEnabled);
            foreach (DocEnergySpectrum d in docs)
            {
                Same(Name(d) + ": ShowFSA", BackgroundMode.ShowFSA, d.EnergySpectrumView.BackgroundMode);
                Same(Name(d) + ": событие входа одно, Explicit = false", "1:False", Events(d));
                Same(Name(d) + ": кнопка FSA цветная", true, Colored(Button(d).Image));
            }
            Invoke(b, "toolStripSplitButtonFsa_DropDownOpening", null, EventArgs.Empty);
            Same("список кнопки FSA: «Включить» отмечен, «Выключить» нет", "True/False",
                 Item(b, "fsaOnToolStripMenuItem").Checked + "/" + Item(b, "fsaOffToolStripMenuItem").Checked);

            // К3 — П1, пики общие
            Console.WriteLine();
            Console.WriteLine("=== К3. П1: пики ВЫКЛ кнопкой A при галочке ВКЛ — гаснут на ВСЕХ, FSA гаснет везде ===");
            ClearEvents();
            PeaksButton(a);
            Same("пики ВЫКЛ у всех вкладок", "Invisible/Invisible/Invisible", Peaks(docs));
            Same("A: режим бывший до FSA — Invisible", BackgroundMode.Invisible, a.EnergySpectrumView.BackgroundMode);
            Same("B: режим бывший до FSA — ShowContinuum", BackgroundMode.ShowContinuum, b.EnergySpectrumView.BackgroundMode);
            Same("C: режим бывший до FSA — Substract", BackgroundMode.Substract, c.EnergySpectrumView.BackgroundMode);
            Same("галочка FSA не сброшена", true, chart.FsaModeEnabled);
            Same("DefaultPeakMode = ВЫКЛ (запоминается)", PeakMode.Invisible, chart.DefaultPeakMode);
            Same("событий входа нет", "0/0/0", Events(a) + "/" + Events(b) + "/" + Events(c));
            foreach (DocEnergySpectrum d in docs)
            {
                Same(Name(d) + ": картинка кнопки пиков — «нет пиков»", true,
                     SameImage(((ToolStripItem)Field(d, "toolStripSplitButton9")).Image, BecquerelMonitor.Properties.Resources.nopeak));
                Same(Name(d) + ": доступность кнопка/«Включить»/«Выключить» (галочка ВКЛ, пики ВЫКЛ)", "True/False/True", Availability(d));
            }
            Same("Enabled метода у всех спектров всех вкладок — ВЫКЛ", "False", MethodEnabled(docs));
            Same("Н4: конфигурация прибора не тронута кнопкой пиков", deviceEnabledBefore, DeviceEnabled(docs));

            // К4 — П2
            Console.WriteLine();
            Console.WriteLine("=== К4. П2: пики ВКЛ кнопкой B при галочке ВКЛ — FSA возвращается везде ===");
            ClearEvents();
            PeaksButton(b);
            Same("пики ВКЛ у всех вкладок", "Visible/Visible/Visible", Peaks(docs));
            Same("DefaultPeakMode = ВКЛ", PeakMode.Visible, chart.DefaultPeakMode);
            foreach (DocEnergySpectrum d in docs)
            {
                Same(Name(d) + ": снова ShowFSA", BackgroundMode.ShowFSA, d.EnergySpectrumView.BackgroundMode);
                Same(Name(d) + ": событие входа одно, Explicit = false", "1:False", Events(d));
                Same(Name(d) + ": картинка кнопки пиков — «пики»", true,
                     SameImage(((ToolStripItem)Field(d, "toolStripSplitButton9")).Image, BecquerelMonitor.Properties.Resources.peak));
            }
            Same("Enabled метода у всех спектров всех вкладок — ВКЛ", "True", MethodEnabled(docs));
            // то же пунктами списка пиков: «Скрыть» с C, «Показать» с A
            ClearEvents();
            Invoke(c, "hidePeaksToolStripMenuItem_Click", null, EventArgs.Empty);
            SpoilAll(docs);
            Same("пунктом «Скрыть пики» с C: пики ВЫКЛ у всех", "Invisible/Invisible/Invisible", Peaks(docs));
            Same("пунктом «Скрыть пики» с C: режимы бывшие", "Invisible/ShowContinuum/Substract", Modes(docs));
            Same("пунктом «Скрыть пики»: Enabled метода ВЫКЛ у всех", "False", MethodEnabled(docs));
            Same("пунктом «Скрыть пики»: конфигурация прибора не тронута", deviceEnabledBefore, DeviceEnabled(docs));
            Invoke(a, "showPeaksToolStripMenuItem_Click", null, EventArgs.Empty);
            Same("пунктом «Показать пики» с A: ShowFSA у всех", "ShowFSA/ShowFSA/ShowFSA", Modes(docs));
            Same("пунктом «Показать пики»: событие входа у каждой одно", "1:False/1:False/1:False", Events(a) + "/" + Events(b) + "/" + Events(c));

            // К5 — П3
            Console.WriteLine();
            Console.WriteLine("=== К5. П3: кнопка FSA ВЫКЛ — все вкладки в свои бывшие режимы ===");
            ClearEvents();
            Invoke(b, "fsaOffToolStripMenuItem_Click", null, EventArgs.Empty);
            Same("галочка ВЫКЛ", false, chart.FsaModeEnabled);
            Same("A: Invisible", BackgroundMode.Invisible, a.EnergySpectrumView.BackgroundMode);
            Same("B: ShowContinuum", BackgroundMode.ShowContinuum, b.EnergySpectrumView.BackgroundMode);
            Same("C: Substract", BackgroundMode.Substract, c.EnergySpectrumView.BackgroundMode);
            Same("событий входа нет", "0/0/0", Events(a) + "/" + Events(b) + "/" + Events(c));
            foreach (DocEnergySpectrum d in docs) Same(Name(d) + ": кнопка FSA серая", false, Colored(Button(d).Image));
            Invoke(b, "toolStripSplitButtonFsa_DropDownOpening", null, EventArgs.Empty);
            Same("список кнопки FSA: «Включить» не отмечен, «Выключить» отмечен", "False/True",
                 Item(b, "fsaOnToolStripMenuItem").Checked + "/" + Item(b, "fsaOffToolStripMenuItem").Checked);

            // К6 — сценарий 2
            Console.WriteLine();
            Console.WriteLine("=== К6. галочка ВЫКЛ: пики ВЫКЛ/ВКЛ (общие) на FSA не влияют ===");
            ClearEvents();
            for (int i = 0; i < 4; i++)
            {
                PeaksButton(a);
                string want = a.EnergySpectrumView.PeakMode.ToString();
                Same(string.Format("нажатие пиков {0}: пики у всех одни ({1}), DefaultPeakMode за ними", i + 1, want),
                     want + "/" + want + "/" + want + "|" + want, Peaks(docs) + "|" + chart.DefaultPeakMode);
                Same(string.Format("нажатие пиков {0}: режимы прежние, не ShowFSA", i + 1),
                     "Invisible/ShowContinuum/Substract", Modes(docs));
            }
            Same("галочка так и ВЫКЛ", false, chart.FsaModeEnabled);
            Same("событий входа нет", "0/0/0", Events(a) + "/" + Events(b) + "/" + Events(c));

            // К7 — доступность включения FSA
            Console.WriteLine();
            Console.WriteLine("=== К7. FSA включается только при пиках ВКЛ; выключается всегда ===");
            PeaksButton(c);
            Same("пики ВЫКЛ у всех (контроль сцены)", "Invisible/Invisible/Invisible", Peaks(docs));
            foreach (DocEnergySpectrum d in docs)
            {
                Same(Name(d) + ": пики ВЫКЛ, галочка ВЫКЛ — кнопка/«Включить»/«Выключить»", "False/False/True", Availability(d));
            }
            Invoke(b, "toolStripSplitButtonFsa_DropDownOpening", null, EventArgs.Empty);
            Same("после открытия списка B — то же", "False/False/True", Availability(b));
            ClearEvents();
            Invoke(a, "toolStripSplitButtonFsa_ButtonClick", null, EventArgs.Empty);
            Same("обработчик кнопки мимо недоступности: галочка НЕ включилась", false, chart.FsaModeEnabled);
            Invoke(b, "fsaOnToolStripMenuItem_Click", null, EventArgs.Empty);
            Same("обработчик «Включить» мимо недоступности: галочка НЕ включилась", false, chart.FsaModeEnabled);
            Same("и ни одна вкладка не в FSA, событий нет", "Invisible/ShowContinuum/Substract|0/0/0",
                 Modes(docs) + "|" + Events(a) + "/" + Events(b) + "/" + Events(c));
            Invoke(c, "showPeaksToolStripMenuItem_Click", null, EventArgs.Empty);
            foreach (DocEnergySpectrum d in docs)
            {
                Same(Name(d) + ": пики ВКЛ — кнопка/«Включить»/«Выключить» доступны", "True/True/True", Availability(d));
            }
            Same("пики ВКЛ при галочке ВЫКЛ: FSA нет", "Invisible/ShowContinuum/Substract", Modes(docs));
            Invoke(c, "toolStripSplitButtonFsa_ButtonClick", null, EventArgs.Empty);
            Same("кнопка FSA (пики ВКЛ): галочка ВКЛ, ShowFSA у всех", "True|ShowFSA/ShowFSA/ShowFSA", chart.FsaModeEnabled + "|" + Modes(docs));
            PeaksButton(b);
            Same("пики ВЫКЛ: FSA погас у всех", "Invisible/ShowContinuum/Substract", Modes(docs));
            foreach (DocEnergySpectrum d in docs)
            {
                Same(Name(d) + ": галочка ВКЛ, пики ВЫКЛ — кнопка доступна (выключить), «Включить» нет", "True/False/True", Availability(d));
            }
            ClearEvents();
            Invoke(a, "toolStripSplitButtonFsa_ButtonClick", null, EventArgs.Empty);
            Same("щелчок кнопки при пиках ВЫКЛ ВЫКЛЮЧАЕТ галочку", false, chart.FsaModeEnabled);
            foreach (DocEnergySpectrum d in docs)
            {
                Same(Name(d) + ": после выключения — кнопка и «Включить» недоступны", "False/False/True", Availability(d));
                Same(Name(d) + ": кнопка FSA серая", false, Colored(Button(d).Image));
            }
            Same("режимы не тронуты, событий нет", "Invisible/ShowContinuum/Substract|0/0/0",
                 Modes(docs) + "|" + Events(a) + "/" + Events(b) + "/" + Events(c));
            PeaksButton(a);
            Same("пики ВКЛ при галочке ВЫКЛ: FSA не появился", "Visible/Visible/Visible|Invisible/ShowContinuum/Substract",
                 Peaks(docs) + "|" + Modes(docs));

            // К8 — возврат в нормировку и подмена при недоступной
            Console.WriteLine();
            Console.WriteLine("=== К8. возврат в «Нормировку по эффективности»; нечем нормировать — «Показать» ===");
            DocEnergySpectrum norm = null;
            foreach (DocEnergySpectrum d in docs)
            {
                if ((bool)Invoke(d, "IsNormalizeByEfficiencyAvailable")) { norm = d; break; }
            }
            if (norm == null)
            {
                Console.WriteLine("  ⛔ обвязка: ни у одной вкладки нормировка недоступна — плечо (а) не измерить");
                bad++;
            }
            else
            {
                norm.EnergySpectrumView.BackgroundMode = BackgroundMode.NormalizeByEfficiency;
                Invoke(norm, "fsaOnToolStripMenuItem_Click", null, EventArgs.Empty);
                Same("(а) " + Name(norm) + ": вход в ShowFSA", BackgroundMode.ShowFSA, norm.EnergySpectrumView.BackgroundMode);
                Invoke(norm, "fsaOffToolStripMenuItem_Click", null, EventArgs.Empty);
                Same("(а) " + Name(norm) + ": возврат в NormalizeByEfficiency", BackgroundMode.NormalizeByEfficiency,
                     norm.EnergySpectrumView.BackgroundMode);
            }
            // (б) у C кривой нет — бывший режим «нормировка» подменяется на «Показать»
            Same("(б) C: нормировка недоступна (контроль сцены)", false, (bool)Invoke(c, "IsNormalizeByEfficiencyAvailable"));
            c.EnergySpectrumView.BackgroundMode = BackgroundMode.NormalizeByEfficiency;
            Invoke(c, "fsaOnToolStripMenuItem_Click", null, EventArgs.Empty);
            Same("(б) C: вход в ShowFSA", BackgroundMode.ShowFSA, c.EnergySpectrumView.BackgroundMode);
            Invoke(c, "fsaOffToolStripMenuItem_Click", null, EventArgs.Empty);
            Same("(б) C: возврат — Visible, а не нормировка без кривой", BackgroundMode.Visible, c.EnergySpectrumView.BackgroundMode);

            // К9 — цикл кнопки фона, все три вкладки (у C нет FWHM — Н2)
            Console.WriteLine();
            Console.WriteLine("=== К9. цикл кнопки фона FSA пропускает (и «континуум» без FWHM не падает) ===");
            foreach (DocEnergySpectrum d in docs)
            {
                foreach (BackgroundMode start in new[] { BackgroundMode.Visible, BackgroundMode.Invisible, BackgroundMode.Substract,
                                                          BackgroundMode.ShowContinuum, BackgroundMode.NormalizeByEfficiency })
                {
                    d.EnergySpectrumView.BackgroundMode = start;
                    int hits = 0;
                    var seen = new List<string>();
                    string failure = null;
                    for (int i = 0; i < 12 && failure == null; i++)
                    {
                        try { Invoke(d, "toolStripSplitButton7_ButtonClick", null, EventArgs.Empty); }
                        catch (Exception ex) { failure = ex.GetBaseException().GetType().Name; }
                        if (d.EnergySpectrumView.BackgroundMode == BackgroundMode.ShowFSA) hits++;
                        if (i < 6) seen.Add(Short(d.EnergySpectrumView.BackgroundMode));
                    }
                    Same(string.Format("{0} с {1}: 12 нажатий без исключения, ShowFSA ни разу ({2}…)", Name(d), Short(start), string.Join("→", seen)),
                         "0", failure ?? hits.ToString(CultureInfo.InvariantCulture));
                }
            }

            // К10 — новая вкладка следует галочке и общим пикам
            Console.WriteLine();
            Console.WriteLine("=== К10. новая вкладка следует галочке и общим пикам ===");
            chart.FsaModeEnabled = true;
            chart.DefaultPeakMode = PeakMode.Visible;
            DocEnergySpectrum fresh = new DocEnergySpectrum();
            Same("галочка ВКЛ, пики ВКЛ: новая вкладка в ShowFSA", BackgroundMode.ShowFSA, fresh.EnergySpectrumView.BackgroundMode);
            Same("и кнопка FSA у неё цветная", true, Colored(Button(fresh).Image));
            fresh.Dispose();
            PeaksButton(a);
            Same("пики ВЫКЛ кнопкой: DefaultPeakMode = ВЫКЛ", PeakMode.Invisible, chart.DefaultPeakMode);
            fresh = new DocEnergySpectrum();
            Same("новая вкладка после выключения пиков кнопкой: пики ВЫКЛ", PeakMode.Invisible, fresh.EnergySpectrumView.PeakMode);
            Same("и в фоне по умолчанию «Показать», не в ShowFSA (галочка ВКЛ)", BackgroundMode.Visible, fresh.EnergySpectrumView.BackgroundMode);
            Same("и кнопка FSA у неё доступна только к выключению", "True/False/True", Availability(fresh));
            fresh.Dispose();
            PeaksButton(a);
            Invoke(a, "fsaOffToolStripMenuItem_Click", null, EventArgs.Empty);
            fresh = new DocEnergySpectrum();
            Same("галочка ВЫКЛ: новая вкладка в фоне по умолчанию «Показать», не в ShowFSA", BackgroundMode.Visible, fresh.EnergySpectrumView.BackgroundMode);
            Same("и кнопка FSA у неё серая", false, Colored(Button(fresh).Image));
            fresh.Dispose();

            // К11 — конфиг галочки
            Console.WriteLine();
            Console.WriteLine("=== К11. конфиг галочки: круг через XML, старый ShowFSA, «перезапуск» ===");
            ConfigSection(manager);

            // К12 — запоминание пиков
            Console.WriteLine();
            Console.WriteLine("=== К12. пики запоминаются: DefaultPeakMode через XML и «перезапуск» ===");
            PeaksMemorySection(manager, a, docs);

            // К13 — окно настроек
            Console.WriteLine();
            Console.WriteLine("=== К13. окно настроек: фон вне списка (Н3), пики кнопкой при открытом окне ===");
            SettingsFormSection(manager);
            WindowPeaksSection(manager, a, docs);

            // К15 — иконка и подсказка кнопки (дефект 13.11.2026)
            Console.WriteLine();
            Console.WriteLine("=== К15. иконка кнопки FSA — своя (FSAToggle), картинка не перекладывается, подсказка в одну строку ===");
            IconAndTipSection(chart, a, b, docs);

            // К14 — Н2
            ContinuumSection(a, c);

            foreach (DocEnergySpectrum d in docs)
            {
                managed.Remove(d);
                d.FsaSession.Reset();
            }
            return 0;
        }

        static void ConfigSection(GlobalConfigManager manager)
        {
            var serializer = new XmlSerializer(typeof(GlobalConfigInfo));
            GlobalConfigInfo live = manager.GlobalConfig;
            bool keep = live.ChartViewConfig.FsaModeEnabled;
            live.ChartViewConfig.FsaModeEnabled = true;
            string xml = Write(serializer, live);
            live.ChartViewConfig.FsaModeEnabled = keep;
            int iBg = xml.IndexOf("<DefaultBackgroundMode>", StringComparison.Ordinal);
            int iFlag = xml.IndexOf("<FsaModeEnabled>true</FsaModeEnabled>", StringComparison.Ordinal);
            Same("в XML галочка записана", true, iFlag >= 0);
            Same("элемент галочки ПОСЛЕ DefaultBackgroundMode", true, iBg >= 0 && iFlag > iBg);
            GlobalConfigInfo back = Read(serializer, xml);
            Same("круг: галочка ВКЛ прочитана", true, back.ChartViewConfig.FsaModeEnabled);
            Same("круг: фон по умолчанию прежний", live.ChartViewConfig.DefaultBackgroundMode, back.ChartViewConfig.DefaultBackgroundMode);

            string noFlag = xml.Replace("<FsaModeEnabled>true</FsaModeEnabled>", "");
            Same("старый файл без элемента: галочка ВЫКЛ", false, Read(serializer, noFlag).ChartViewConfig.FsaModeEnabled);
            string legacy = SetElement(noFlag, "DefaultBackgroundMode", "ShowFSA");
            GlobalConfigInfo old = Read(serializer, legacy);
            Same("старый файл с DefaultBackgroundMode=ShowFSA: галочка ВКЛ", true, old.ChartViewConfig.FsaModeEnabled);
            Same("старый файл с DefaultBackgroundMode=ShowFSA: фон Visible", BackgroundMode.Visible, old.ChartViewConfig.DefaultBackgroundMode);
            string legacyOff = SetElement(xml.Replace("<FsaModeEnabled>true</FsaModeEnabled>", "<FsaModeEnabled>false</FsaModeEnabled>"),
                                          "DefaultBackgroundMode", "ShowFSA");
            Same("ShowFSA и явная галочка ВЫКЛ после него: ВЫКЛ", false, Read(serializer, legacyOff).ChartViewConfig.FsaModeEnabled);
            Same("в записанном файле ShowFSA фоном по умолчанию не бывает", false,
                 Write(serializer, old).Contains("<DefaultBackgroundMode>ShowFSA</DefaultBackgroundMode>"));

            // «перезапуск»: приложение живёт на прочитанном конфиге — новая вкладка следует ему
            back.ChartViewConfig.DefaultPeakMode = PeakMode.Visible;
            manager.GlobalConfig = back;
            try
            {
                DocEnergySpectrum restarted = new DocEnergySpectrum();
                Same("«перезапуск» с галочкой ВКЛ из файла: новая вкладка в ShowFSA", BackgroundMode.ShowFSA,
                     restarted.EnergySpectrumView.BackgroundMode);
                restarted.Dispose();
            }
            finally
            {
                manager.GlobalConfig = live;
            }
        }

        /// <summary>
        /// К12: пики, выключенные кнопкой, — в `DefaultPeakMode`, кругом через XML; «перезапуск»
        /// на прочитанном конфиге — новая вкладка без пиков и без FSA, её кнопка пиков
        /// возвращает и пики, и FSA (галочка ВКЛ из того же файла).
        /// </summary>
        static void PeaksMemorySection(GlobalConfigManager manager, DocEnergySpectrum a, DocEnergySpectrum[] docs)
        {
            var serializer = new XmlSerializer(typeof(GlobalConfigInfo));
            GlobalConfigInfo live = manager.GlobalConfig;
            ChartViewConfig chart = live.ChartViewConfig;
            Same("исходное: пики ВКЛ (контроль сцены)", PeakMode.Visible, a.EnergySpectrumView.PeakMode);
            Invoke(a, "fsaOnToolStripMenuItem_Click", null, EventArgs.Empty);
            PeaksButton(a);
            Same("кнопкой пики ВЫКЛ: DefaultPeakMode = ВЫКЛ", PeakMode.Invisible, chart.DefaultPeakMode);
            string xml = Write(serializer, live);
            Same("в XML: <DefaultPeakMode>Invisible</DefaultPeakMode>", true,
                 xml.Contains("<DefaultPeakMode>Invisible</DefaultPeakMode>"));
            Same("в XML: <FsaModeEnabled>true</FsaModeEnabled>", true, xml.Contains("<FsaModeEnabled>true</FsaModeEnabled>"));
            GlobalConfigInfo back = Read(serializer, xml);
            Same("круг: пики ВЫКЛ и галочка ВКЛ прочитаны", "Invisible/True",
                 back.ChartViewConfig.DefaultPeakMode + "/" + back.ChartViewConfig.FsaModeEnabled);
            Same("круг через Visible: пики ВКЛ прочитаны", PeakMode.Visible,
                 Read(serializer, SetElement(xml, "DefaultPeakMode", "Visible")).ChartViewConfig.DefaultPeakMode);

            // «перезапуск»: открытых вкладок прошлого запуска нет — только новая
            List<DocEnergySpectrum> managed = DocumentManager.GetInstance().DocumentList;
            foreach (DocEnergySpectrum d in docs) managed.Remove(d);
            manager.GlobalConfig = back;
            DocEnergySpectrum restarted = null;
            try
            {
                restarted = new DocEnergySpectrum();
                Same("«перезапуск»: новая вкладка — пики ВЫКЛ", PeakMode.Invisible, restarted.EnergySpectrumView.PeakMode);
                Same("«перезапуск»: в фоне по умолчанию из файла, не в ShowFSA", back.ChartViewConfig.DefaultBackgroundMode, restarted.EnergySpectrumView.BackgroundMode);
                Same("«перезапуск»: фон по умолчанию из файла — «Показать» (контроль: он не ShowFSA)", BackgroundMode.Visible, back.ChartViewConfig.DefaultBackgroundMode);
                Same("«перезапуск»: картинка кнопки пиков — «нет пиков»", true,
                     SameImage(((ToolStripItem)Field(restarted, "toolStripSplitButton9")).Image, BecquerelMonitor.Properties.Resources.nopeak));
                Same("«перезапуск»: кнопка FSA цветная и доступна только к выключению", "True|True/False/True",
                     Colored(Button(restarted).Image) + "|" + Availability(restarted));
                Invoke(restarted, "toolStripSplitButton9_ButtonClick", null, EventArgs.Empty);
                Same("«перезапуск», пики ВКЛ её кнопкой: ShowFSA", BackgroundMode.ShowFSA, restarted.EnergySpectrumView.BackgroundMode);
                Same("и прочитанный конфиг помнит пики ВКЛ", PeakMode.Visible, back.ChartViewConfig.DefaultPeakMode);
            }
            finally
            {
                if (restarted != null) restarted.Dispose();
                manager.GlobalConfig = live;
                foreach (DocEnergySpectrum d in docs) managed.Add(d);
            }
            PeaksButton(a);
            Invoke(a, "fsaOffToolStripMenuItem_Click", null, EventArgs.Empty);
            Same("сцена возвращена: пики ВКЛ, галочка ВЫКЛ", "Visible/False", chart.DefaultPeakMode + "/" + chart.FsaModeEnabled);
        }

        /// <summary>
        /// К13: окно настроек без показа — `LoadFormContents`/`SaveFormContents` на копии
        /// конфига (Н3 и пики, сменённые кнопкой при открытом немодальном окне).
        /// </summary>
        static void SettingsFormSection(GlobalConfigManager manager)
        {
            var serializer = new XmlSerializer(typeof(GlobalConfigInfo));
            GlobalConfigInfo copy = Read(serializer, Write(serializer, manager.GlobalConfig));
            using (GlobalConfigForm form = new GlobalConfigForm())
            {
                ComboBox background = (ComboBox)Field(form, "comboBox7");
                ComboBox peaks = (ComboBox)Field(form, "comboBox9");
                foreach (BackgroundMode mode in new[] { BackgroundMode.Substract, BackgroundMode.ShowContinuum, BackgroundMode.NormalizeByEfficiency })
                {
                    copy.ChartViewConfig.DefaultBackgroundMode = mode;
                    string failure = null;
                    try
                    {
                        form.LoadFormContents(copy);
                        form.SaveFormContents(copy);
                    }
                    catch (Exception ex) { failure = ex.GetBaseException().GetType().Name; }
                    Same(string.Format("фон по умолчанию {0}: окно открывается и сохраняет без исключения", mode), null, failure);
                    Same(string.Format("фон {0}: список пуст, сохранённое значение то же", mode), "-1/" + mode,
                         background.SelectedIndex + "/" + copy.ChartViewConfig.DefaultBackgroundMode);
                }
                copy.ChartViewConfig.DefaultBackgroundMode = BackgroundMode.Invisible;
                form.LoadFormContents(copy);
                Same("фон Invisible: в списке пункт 1", 1, background.SelectedIndex);
                background.SelectedIndex = 0;
                form.SaveFormContents(copy);
                Same("сменили в окне на «Показать» — записано", BackgroundMode.Visible, copy.ChartViewConfig.DefaultBackgroundMode);

                // пики: окно открыто с ВКЛ, кнопка выключила их — сохранение окна не возвращает ВКЛ
                copy.ChartViewConfig.DefaultPeakMode = PeakMode.Visible;
                form.LoadFormContents(copy);
                copy.ChartViewConfig.DefaultPeakMode = PeakMode.Invisible;
                form.SaveFormContents(copy);
                Same("пики выключены кнопкой при открытом окне: сохранение окна их не включает", PeakMode.Invisible,
                     copy.ChartViewConfig.DefaultPeakMode);
                // кнопка снова включила пики, а человек в окне выбрал «скрыть» — окно пишет своё
                copy.ChartViewConfig.DefaultPeakMode = PeakMode.Visible;
                peaks.SelectedIndex = (int)PeakMode.Invisible;
                form.SaveFormContents(copy);
                Same("пики сменили в самом окне — записано", PeakMode.Invisible, copy.ChartViewConfig.DefaultPeakMode);

                // возврат в окно: нетронутый список подтягивает пики, сменённые кнопкой
                ChartViewConfig live = manager.GlobalConfig.ChartViewConfig;
                PeakMode keep = live.DefaultPeakMode;
                try
                {
                    live.DefaultPeakMode = PeakMode.Visible;
                    copy.ChartViewConfig.DefaultPeakMode = PeakMode.Visible;
                    form.LoadFormContents(copy);
                    live.DefaultPeakMode = PeakMode.Invisible;
                    Invoke(form, "OnActivated", EventArgs.Empty);
                    Same("возврат в окно после кнопки: список пиков показывает ВЫКЛ", (int)PeakMode.Invisible, peaks.SelectedIndex);
                    peaks.SelectedIndex = (int)PeakMode.Visible;
                    live.DefaultPeakMode = PeakMode.Invisible;
                    Invoke(form, "OnActivated", EventArgs.Empty);
                    Same("список тронут в окне — возврат в окно его не перебивает", (int)PeakMode.Visible, peaks.SelectedIndex);
                }
                finally
                {
                    live.DefaultPeakMode = keep;
                }
            }
        }

        /// <summary>
        /// К13, окно → вкладки (замечание З1 критика crit328b): пики, сменённые В ОКНЕ настроек
        /// («OK»/«Применить» — `AcceptFormContents`), приводят ВСЕ открытые вкладки тем же путём,
        /// что кнопка пиков, — вместе с FSA. Образец — мини-проба критика `WindowPeaksCrit`.
        /// Окно без главной формы: `AcceptFormContents` после приведения вкладок падает на
        /// `mainForm.RefreshAllView()` (NRE в самом `AcceptFormContents`) — это обвязка, и
        /// принимается ТОЛЬКО такое исключение. Конфиг окно пишет на диск безусловно
        /// (`SaveConfigFile`), поэтому путь файла менеджера на время раздела отведён во
        /// временный файл: конфиг рядом с exe не трогается (mtime проверяется в конце).
        /// Положительный контроль — мутант V (снят вызов `SetPeakModeEverywhere` в
        /// `AcceptFormContents`): вкладки не меняются, раздел отказывает.
        /// </summary>
        static void WindowPeaksSection(GlobalConfigManager manager, DocEnergySpectrum a, DocEnergySpectrum[] docs)
        {
            ChartViewConfig chart = manager.GlobalConfig.ChartViewConfig;
            FieldInfo pathField = typeof(GlobalConfigManager).GetField("becqMoniMainConfig", Any);
            if (pathField == null) { Console.WriteLine("  ⛔ обвязка: нет GlobalConfigManager.becqMoniMainConfig"); bad++; return; }
            string keepPath = (string)pathField.GetValue(manager);
            string tempConfig = Path.Combine(Path.GetTempPath(), "FsaToggleProbeA328_" + Guid.NewGuid().ToString("N") + ".xml");
            pathField.SetValue(manager, tempConfig);
            MethodInfo accept = typeof(GlobalConfigForm).GetMethod("AcceptFormContents", Any);
            if (accept == null) { Console.WriteLine("  ⛔ обвязка: нет GlobalConfigForm.AcceptFormContents"); bad++; pathField.SetValue(manager, keepPath); return; }
            try
            {
                Same("окно: исходное — пики ВКЛ, галочка ВЫКЛ (контроль сцены)", "Visible/False", chart.DefaultPeakMode + "/" + chart.FsaModeEnabled);
                var before = new Dictionary<DocEnergySpectrum, BackgroundMode>();
                foreach (DocEnergySpectrum d in docs) before[d] = d.EnergySpectrumView.BackgroundMode;
                Invoke(a, "fsaOnToolStripMenuItem_Click", null, EventArgs.Empty);
                Same("окно: сцена — все вкладки пики ВКЛ и ShowFSA", States(docs, PeakMode.Visible, null), States(docs));

                using (GlobalConfigForm form = new GlobalConfigForm())
                {
                    ComboBox peaks = (ComboBox)Field(form, "comboBox9");
                    form.LoadFormContents(manager.GlobalConfig);
                    Same("окно открыто: список пиков показывает ВКЛ", (int)PeakMode.Visible, peaks.SelectedIndex);

                    // 1. в окне пики ВЫКЛ → «Применить»: все вкладки без пиков и вне FSA, каждая — в свой бывший режим
                    peaks.SelectedIndex = (int)PeakMode.Invisible;
                    Same("«Применить» с пиками ВЫКЛ в окне: только обвязочное NRE в AcceptFormContents", "NullReferenceException@AcceptFormContents", Accept(accept, form));
                    SpoilAll(docs);
                    Same("окно: DefaultPeakMode = ВЫКЛ", PeakMode.Invisible, chart.DefaultPeakMode);
                    Same("окно: галочка FSA цела", true, chart.FsaModeEnabled);
                    Same("окно: ВСЕ вкладки — пики ВЫКЛ, FSA погас, каждая в свой бывший режим", States(docs, PeakMode.Invisible, before), States(docs));
                    Same("окно: временный конфиг записан с пиками ВЫКЛ", true,
                         File.Exists(tempConfig) && File.ReadAllText(tempConfig).Contains("<DefaultPeakMode>Invisible</DefaultPeakMode>"));

                    // 2. в окне пики ВКЛ → «Применить»: FSA возвращается на всех (галочка ВКЛ)
                    peaks.SelectedIndex = (int)PeakMode.Visible;
                    Same("«Применить» с пиками ВКЛ в окне: только обвязочное NRE", "NullReferenceException@AcceptFormContents", Accept(accept, form));
                    Same("окно: DefaultPeakMode = ВКЛ", PeakMode.Visible, chart.DefaultPeakMode);
                    Same("окно: ВСЕ вкладки — пики ВКЛ и снова ShowFSA", States(docs, PeakMode.Visible, null), States(docs));

                    // 3. «Применить» без смены в окне вкладки не трогает
                    Same("«Применить» без смены: только обвязочное NRE", "NullReferenceException@AcceptFormContents", Accept(accept, form));
                    Same("«Применить» без смены в окне: вкладки как были", States(docs, PeakMode.Visible, null), States(docs));
                }

                Invoke(a, "fsaOffToolStripMenuItem_Click", null, EventArgs.Empty);
                Same("окно: сцена возвращена — пики ВКЛ, галочка ВЫКЛ, вкладки в бывших режимах", "Visible/False|" + States(docs, PeakMode.Visible, before),
                     chart.DefaultPeakMode + "/" + chart.FsaModeEnabled + "|" + States(docs));
            }
            finally
            {
                pathField.SetValue(manager, keepPath);
                try { if (File.Exists(tempConfig)) File.Delete(tempConfig); } catch (IOException) { }
            }
        }

        /// <summary>«Применить» окна: имя исключения и метод, где оно брошено (или «без исключения»).</summary>
        static string Accept(MethodInfo accept, GlobalConfigForm form)
        {
            try
            {
                accept.Invoke(form, null);
                return "без исключения";
            }
            catch (TargetInvocationException ex)
            {
                Exception inner = ex.InnerException ?? ex;
                return inner.GetType().Name + "@" + (inner.TargetSite == null ? "?" : inner.TargetSite.Name);
            }
        }

        /// <summary>Пики и режим фона вкладок строкой; ожидаемое — при <paramref name="modes"/> = null ShowFSA.</summary>
        static string States(DocEnergySpectrum[] docs, PeakMode peaks, Dictionary<DocEnergySpectrum, BackgroundMode> modes)
        {
            var parts = new List<string>();
            foreach (DocEnergySpectrum d in docs) parts.Add(Name(d) + ":" + peaks + ":" + (modes == null ? BackgroundMode.ShowFSA : modes[d]));
            return string.Join("/", parts);
        }

        static string States(DocEnergySpectrum[] docs)
        {
            var parts = new List<string>();
            foreach (DocEnergySpectrum d in docs) parts.Add(Name(d) + ":" + d.EnergySpectrumView.PeakMode + ":" + d.EnergySpectrumView.BackgroundMode);
            return string.Join("/", parts);
        }

        /// <summary>
        /// К15 — дефект 13.11.2026 (подсказка в три строки у нижнего края экрана вставала на
        /// саму кнопку и глотала нажатие стрелки; иконка дублировала `FSA.bmp` кнопки фона):
        /// (а) при галочке ВКЛ на кнопке — `Resources.FSAToggle` попиксельно и НЕ `Resources.FSA`;
        /// (б) два обновления кнопки подряд (и открытие её списка) ссылку `Image` не меняют —
        ///     ни при ВКЛ, ни при ВЫКЛ; картинка одна на все вкладки;
        /// (в) подсказка кнопки — одна строка не длиннее `AppUi.TipWidth` в en и ru; язык
        ///     вкладки проверен буквами (контроль, что культура и впрямь сменилась), текст
        ///     кнопки равен строке `*.resx` той же культуры (перенос ничего не добавил).
        /// Сцена восстанавливается: пики ВКЛ, галочка ВЫКЛ.
        /// </summary>
        static void IconAndTipSection(ChartViewConfig chart, DocEnergySpectrum a, DocEnergySpectrum b, DocEnergySpectrum[] docs)
        {
            Invoke(a, "showPeaksToolStripMenuItem_Click", null, EventArgs.Empty);
            Invoke(a, "fsaOnToolStripMenuItem_Click", null, EventArgs.Empty);
            Same("сцена: галочка ВКЛ, пики ВКЛ", "True|Visible", chart.FsaModeEnabled + "|" + a.EnergySpectrumView.PeakMode);
            Image on = Button(a).Image;
            Same("(а) ВКЛ: картинка кнопки — Resources.FSAToggle попиксельно", true,
                 SameImage(on, BecquerelMonitor.Properties.Resources.FSAToggle));
            Same("(а) ВКЛ: картинка кнопки — НЕ Resources.FSA (иконка кнопки фона в FSA)", false,
                 SameImage(on, BecquerelMonitor.Properties.Resources.FSA));
            Same("(а) контроль судьи: FSAToggle и FSA — разные рисунки", false,
                 SameImage(BecquerelMonitor.Properties.Resources.FSAToggle, BecquerelMonitor.Properties.Resources.FSA));
            Invoke(a, "UpdateFsaButton");
            Invoke(a, "UpdateFsaButton");
            Same("(б) ВКЛ: два обновления подряд — ссылка Image та же", true, ReferenceEquals(on, Button(a).Image));
            Invoke(a, "toolStripSplitButtonFsa_DropDownOpening", null, EventArgs.Empty);
            Same("(б) ВКЛ: открытие списка кнопки — ссылка Image та же", true, ReferenceEquals(on, Button(a).Image));
            Same("(б) ВКЛ: у вкладки B та же картинка (одна на приложение)", true, ReferenceEquals(on, Button(b).Image));

            Invoke(a, "fsaOffToolStripMenuItem_Click", null, EventArgs.Empty);
            Image off = Button(a).Image;
            Same("(б) ВЫКЛ: картинка сменилась на серую", "False|False", ReferenceEquals(on, off) + "|" + Colored(off));
            Invoke(a, "UpdateFsaButton");
            Invoke(a, "UpdateFsaButton");
            Same("(б) ВЫКЛ: два обновления подряд — ссылка Image та же", true, ReferenceEquals(off, Button(a).Image));
            Invoke(a, "toolStripSplitButtonFsa_DropDownOpening", null, EventArgs.Empty);
            Same("(б) ВЫКЛ: открытие списка кнопки — ссылка Image та же", true, ReferenceEquals(off, Button(a).Image));

            var resources = new System.ComponentModel.ComponentResourceManager(typeof(DocEnergySpectrum));
            CultureInfo keep = Thread.CurrentThread.CurrentUICulture;
            int width = (int)typeof(AppUi).GetField("TipWidth", Any).GetValue(null);
            foreach (string lang in new[] { "en-US", "ru-RU" })
            {
                CultureInfo culture = new CultureInfo(lang);
                Thread.CurrentThread.CurrentUICulture = culture;
                DocEnergySpectrum fresh = null;
                try
                {
                    fresh = new DocEnergySpectrum();
                    string tip = Button(fresh).ToolTipText ?? "";
                    Console.WriteLine("        {0}: «{1}» ({2} знаков)", lang, tip.Replace("\r", "\\r").Replace("\n", "\\n"),
                                      tip.Length.ToString(CultureInfo.InvariantCulture));
                    bool cyrillic = false;
                    foreach (char ch in tip) if (ch >= '\u0400' && ch <= '\u04FF') cyrillic = true;
                    Same("(в) " + lang + ": язык подсказки тот (кириллица " + (lang == "ru-RU" ? "есть" : "нет") + ")",
                         lang == "ru-RU", cyrillic);
                    Same("(в) " + lang + ": подсказка — одна строка (нет \\r и \\n)", true,
                         tip.Length > 0 && tip.IndexOf('\n') < 0 && tip.IndexOf('\r') < 0);
                    Same("(в) " + lang + ": подсказка не длиннее AppUi.TipWidth = " + width.ToString(CultureInfo.InvariantCulture), true,
                         tip.Length <= width);
                    Same("(в) " + lang + ": подсказка кнопки = строка resx этой культуры", true,
                         tip == resources.GetString("toolStripSplitButtonFsa.ToolTipText", culture));
                }
                finally
                {
                    Thread.CurrentThread.CurrentUICulture = keep;
                    if (fresh != null) fresh.Dispose();
                }
            }
        }

        /// <summary>
        /// К14 (`AMBER328`, находка Н2): «континуум» на спектре без FWHM. Только члены сборки
        /// ДО правки, новые — отражением (`--only=n2` гоняет это против старого exe).
        /// </summary>
        static void ContinuumSection(DocEnergySpectrum withFwhm, DocEnergySpectrum bare)
        {
            Console.WriteLine();
            Console.WriteLine("=== К14. «континуум» без FWHM: отказ с причиной, а не падение (Н2) ===");
            Same("C без FWHM (контроль сцены)", true, bare.ActiveResultData.FwhmCalibration == null);
            Same("A с FWHM (контроль сцены)", true, withFwhm.ActiveResultData.FwhmCalibration != null);
            PropertyInfo refusalProperty = typeof(EnergySpectrumView).GetProperty("ContinuumRefusal", Any);
            FieldInfo continuumField = typeof(EnergySpectrumView).GetField("continuumEnergySpectrum", Any);

            foreach (DocEnergySpectrum d in new[] { bare, withFwhm })
            {
                bool hasFwhm = d.ActiveResultData.FwhmCalibration != null;
                string who = hasFwhm ? "A (с FWHM)" : "C (без FWHM)";
                d.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowContinuum;
                string failure = null;
                try
                {
                    d.RefreshView();
                    d.EnergySpectrumView.PrepareViewData();
                }
                catch (Exception ex)
                {
                    Exception root = ex.GetBaseException();
                    failure = root.GetType().Name + " " + FirstFrame(root);
                }
                Same(who + ": «континуум» без исключения", null, failure);
                object continuum = continuumField != null ? continuumField.GetValue(d.EnergySpectrumView) : "нет поля";
                Same(who + ": континуум построен", hasFwhm, continuum != null);
                if (refusalProperty == null)
                {
                    Same(who + ": у вида есть ContinuumRefusal", true, false);
                    continue;
                }
                string refusal = (string)refusalProperty.GetValue(d.EnergySpectrumView, null);
                if (hasFwhm)
                {
                    Same(who + ": отказа нет", null, refusal);
                    continue;
                }
                Same(who + ": отказ с причиной (упоминает FWHM)", true, refusal != null && refusal.Contains("FWHM"));
                Console.WriteLine("        текст: {0}", refusal);
                Same(who + ": отказ — строка ресурса ContinuumNoFwhmRefusal", ResourceText("ContinuumNoFwhmRefusal"), refusal);
                MethodInfo draw = typeof(EnergySpectrumView).GetMethod("ShowContinuumRefusal", Any);
                if (draw == null)
                {
                    Same(who + ": у вида есть ShowContinuumRefusal", true, false);
                    continue;
                }
                using (var bmp = new Bitmap(400, 200))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(255, 1, 2, 3));
                    draw.Invoke(d.EnergySpectrumView, new object[] { g, 230 });
                    Same(who + ": панель отказа нарисована (белая заливка, тёмный текст)", "True/True",
                         Has(bmp, p => p.R == 255 && p.G == 255 && p.B == 255) + "/" + Has(bmp, p => p.R == 0 && p.G == 0 && p.B == 0));
                }
            }
            // режим сменили — отказа нет
            bare.EnergySpectrumView.BackgroundMode = BackgroundMode.Visible;
            bare.RefreshView();
            if (refusalProperty != null)
            {
                Same("C: из «континуума» в «Показать» — отказа нет", null, (string)refusalProperty.GetValue(bare.EnergySpectrumView, null));
            }
        }

        static string FirstFrame(Exception ex)
        {
            string trace = ex.StackTrace ?? "";
            int i = trace.IndexOf('\n');
            return (i > 0 ? trace.Substring(0, i) : trace).Trim();
        }

        static string ResourceText(string key)
        {
            return BecquerelMonitor.Properties.Resources.ResourceManager.GetString(key, BecquerelMonitor.Properties.Resources.Culture);
        }

        static bool Has(Bitmap bmp, Func<Color, bool> test)
        {
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (test(bmp.GetPixel(x, y))) return true;
                }
            }
            return false;
        }

        static string SetElement(string xml, string element, string value)
        {
            int i = xml.IndexOf("<" + element + ">", StringComparison.Ordinal);
            int j = xml.IndexOf("</" + element + ">", StringComparison.Ordinal);
            return xml.Substring(0, i) + "<" + element + ">" + value + xml.Substring(j);
        }

        static string Write(XmlSerializer serializer, GlobalConfigInfo config)
        {
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, config);
                return writer.ToString();
            }
        }

        static GlobalConfigInfo Read(XmlSerializer serializer, string xml)
        {
            using (var reader = new StringReader(xml))
            {
                return (GlobalConfigInfo)serializer.Deserialize(reader);
            }
        }

        /// <summary>Кнопка пиков; при `--spoil=peaks` выключение пиков FSA не гасит.</summary>
        static void PeaksButton(DocEnergySpectrum d)
        {
            Invoke(d, "toolStripSplitButton9_ButtonClick", null, EventArgs.Empty);
            SpoilAll(entered.Keys);
        }

        static void SpoilAll(IEnumerable<DocEnergySpectrum> docs)
        {
            if (!spoilPeaks) return;
            foreach (DocEnergySpectrum d in new List<DocEnergySpectrum>(docs))
            {
                if (d.EnergySpectrumView.PeakMode == PeakMode.Invisible)
                {
                    d.EnergySpectrumView.BackgroundMode = BackgroundMode.ShowFSA;
                }
            }
        }

        static ToolStripItem Button(DocEnergySpectrum d)
        {
            return (ToolStripItem)Field(d, "toolStripSplitButtonFsa");
        }

        static ToolStripMenuItem Item(DocEnergySpectrum d, string name)
        {
            return (ToolStripMenuItem)Field(d, name);
        }

        /// <summary>Доступность «кнопка FSA / пункт «Включить» / пункт «Выключить»».</summary>
        static string Availability(DocEnergySpectrum d)
        {
            return Button(d).Enabled + "/" + Item(d, "fsaOnToolStripMenuItem").Enabled + "/" + Item(d, "fsaOffToolStripMenuItem").Enabled;
        }

        static string Peaks(DocEnergySpectrum[] docs)
        {
            var parts = new List<string>();
            foreach (DocEnergySpectrum d in docs) parts.Add(d.EnergySpectrumView.PeakMode.ToString());
            return string.Join("/", parts);
        }

        static string Modes(DocEnergySpectrum[] docs)
        {
            var parts = new List<string>();
            foreach (DocEnergySpectrum d in docs) parts.Add(d.EnergySpectrumView.BackgroundMode.ToString());
            return string.Join("/", parts);
        }

        /// <summary>`Enabled` настроек поиска пиков у всех спектров всех вкладок: одно значение или «разное».</summary>
        static string MethodEnabled(DocEnergySpectrum[] docs)
        {
            var values = new HashSet<string>();
            foreach (DocEnergySpectrum d in docs)
            {
                foreach (ResultData rd in d.ResultDataFile.ResultDataList)
                {
                    FWHMPeakDetectionMethodConfig cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
                    values.Add(cfg == null ? "нет" : cfg.Enabled.ToString());
                }
            }
            return values.Count == 1 ? new List<string>(values)[0] : "разное: " + string.Join(",", values);
        }

        /// <summary>`Enabled` в конфигурации ПРИБОРА каждой вкладки (Н4: кнопка пиков его больше не пишет).</summary>
        static string DeviceEnabled(DocEnergySpectrum[] docs)
        {
            var parts = new List<string>();
            foreach (DocEnergySpectrum d in docs)
            {
                DeviceConfigInfo device = d.ActiveResultData.DeviceConfig;
                FWHMPeakDetectionMethodConfig cfg = device != null ? device.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig : null;
                parts.Add(cfg == null ? "нет" : cfg.Enabled.ToString());
            }
            return string.Join("/", parts);
        }

        /// <summary>
        /// Картинка кнопки — та же иконка: кнопка хранит копию, где прозрачный цвет снят
        /// (`ImageTransparentColor`), поэтому прозрачные пиксели кнопки не сравниваются.
        /// </summary>
        static bool SameImage(Image button, Image icon)
        {
            Bitmap l = button as Bitmap, r = icon as Bitmap;
            if (l == null || r == null || l.Width != r.Width || l.Height != r.Height) return false;
            int compared = 0;
            for (int y = 0; y < l.Height; y++)
            {
                for (int x = 0; x < l.Width; x++)
                {
                    Color p = l.GetPixel(x, y);
                    if (p.A == 0) continue;
                    if (p.ToArgb() != r.GetPixel(x, y).ToArgb()) return false;
                    compared++;
                }
            }
            return compared > 0;
        }

        /// <summary>Есть ли в картинке цветной (не серый) пиксель: «вкл» — цветная иконка FSA, «выкл» — серая.</summary>
        static bool Colored(Image image)
        {
            Bitmap bmp = image as Bitmap;
            if (bmp == null) return false;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    Color p = bmp.GetPixel(x, y);
                    if (Math.Abs(p.R - p.G) > 24 || Math.Abs(p.G - p.B) > 24 || Math.Abs(p.R - p.B) > 24) return true;
                }
            }
            return false;
        }

        static void ClearEvents()
        {
            foreach (List<bool> list in entered.Values) list.Clear();
        }

        static string Events(DocEnergySpectrum d)
        {
            List<bool> list = entered[d];
            return list.Count == 0 ? "0" : list.Count + ":" + string.Join(",", list);
        }

        static string Name(DocEnergySpectrum d)
        {
            return d.Tag as string ?? "?";
        }

        static string Short(BackgroundMode mode)
        {
            switch (mode)
            {
                case BackgroundMode.Visible: return "BG";
                case BackgroundMode.Invisible: return "NOBG";
                case BackgroundMode.Substract: return "SUB";
                case BackgroundMode.ShowContinuum: return "CONT";
                case BackgroundMode.NormalizeByEfficiency: return "NORM";
                case BackgroundMode.ShowFSA: return "FSA";
                default: return mode.ToString();
            }
        }

        static string On(bool flag)
        {
            return flag ? "ВКЛ" : "ВЫКЛ";
        }

        static void Same<T>(string what, T expected, T actual)
        {
            if (Equals(expected, actual))
            {
                good++;
                Console.WriteLine("  ok   {0}: {1}", what, actual);
            }
            else
            {
                bad++;
                Console.WriteLine("  ⛔  {0}: ждали {1}, вышло {2}", what, expected, actual);
            }
        }

        static object Field(object target, string name)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, Any);
                if (f != null) return f.GetValue(target);
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }

        static object Invoke(object target, string name, params object[] args)
        {
            MethodInfo m = target.GetType().GetMethod(name, Any);
            if (m == null) throw new MissingMethodException(target.GetType().Name, name);
            try
            {
                return m.Invoke(target, args);
            }
            catch (TargetInvocationException ex)
            {
                // стек — от места беды, а не от отражения
                throw new InvalidOperationException(name + ": " + (ex.InnerException ?? ex), ex.InnerException ?? ex);
            }
        }

        /// <summary>
        /// Документ из файла корпуса, как в `DocSwitchFsaProbe`; <paramref name="bare"/> — без
        /// кривой эффективности и без FWHM (спектр, на котором FSA невозможен).
        /// </summary>
        static DocEnergySpectrum Open(string path, NuclideDefinitionManager nuclides, bool bare)
        {
            ResultDataFile file;
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }
            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }
            Console.WriteLine("SETUP\t{0}{1}: прибор {2}", Path.GetFileName(path), bare ? " (без кривой и FWHM)" : "",
                              ProbeDeviceConfig.Attach(rd));
            if (bare)
            {
                rd.Efficiency = null;
                rd.FwhmCalibration = null;
            }
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            doc.Tag = bare ? "C" : (docTags == 0 ? "A" : "B");
            if (!bare) docTags++;
            return doc;
        }

        static int docTags;
    }
}
