using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace FsaInferProbeF51
{
    /// <summary>
    /// (`A205`, полоса F51) ЧТО ВЫВОД СОСТАВА ГОВОРИТ ПРО ЭТОТ СПЕКТР — тем же
    /// кодом, каким его зовёт приложение (`FsaAnalysisSession.Compute`).
    ///
    /// Вопрос строки прямой: человек ставит источник состава «Из NucBase» на
    /// точечный Th-228 и получает ПУСТОЙ разбор. Проверять это глазами в окне
    /// нельзя (окно `BecqMoni` запускать запрещено), а пересказ отчёта
    /// доказательством не является — поэтому проба печатает три вещи, и все три
    /// числами:
    ///
    ///   1. отчёт вывода состава целиком (кандидаты, доли, подцепочки);
    ///   2. получившуюся спецификацию — ряды с ограничением членов и одиночки;
    ///   3. библиотеку, которую по ней собирает `FsaSampleLibrary`, — то самое
    ///      место, где «пусто» превращается в отказ приложения
    ///      (`library.Count == 0` → `FSANoComponents`).
    ///
    ///   FsaInferProbeF51 --spectrum=X.xml [--set=Имя] [--efficiency=Имя]
    ///                    [--cut=whole|criterion|only] [--lib-dump]
    ///                    [--modal-control]
    ///
    /// `--cut=` заставляет правило обрыва ряда; БЕЗ ключа зовётся ровно та
    /// перегрузка, которой пользуется приложение, — то есть меряется умолчание,
    /// а не догадка о нём.
    ///
    /// ⛔ Сторож модальных окон поднят ПЕРВЫМ ДЕЛОМ (образец —
    /// `CultureProbeO14`): безоконный путь, упершийся в `MessageBox`, вешает
    /// прогон насмерть, и «окон не было» без сторожа неотличимо от «сторож
    /// молчит». Положительный контроль самого сторожа — `--modal-control`.
    /// </summary>
    static class Program
    {
        static int failures;
        static int modalSeen;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, setName = null, efficiencyName = null;
            string cutName = null;
            bool libDump = false, modalControl = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--efficiency=", StringComparison.Ordinal)) efficiencyName = a.Substring(13);
                else if (a.StartsWith("--cut=", StringComparison.Ordinal)) cutName = a.Substring(6);
                else if (a == "--lib-dump") libDump = true;
                else if (a == "--modal-control") modalControl = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            // ⛔ Сторож — до менеджеров-одиночек: `DocumentManager` и чтение
            //    конфигов умеют поднять окно сами.
            ModalWatchStart();

            if (modalControl)
            {
                ModalControl();
                ModalWatchStop();
                Console.WriteLine(modalSeen == 0
                    ? "⛔ КОНТРОЛЬ НЕ СРАБОТАЛ: сторож не засчитал ни одного окна"
                    : "окон засчитано: " + modalSeen.ToString(CultureInfo.InvariantCulture)
                      + " (так и надо: это контроль сторожа)");
                return modalSeen == 0 ? 3 : 1;
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                ModalWatchStop();
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            if (efficiencyName != null && !AttachEfficiency(rd, efficiencyName))
            {
                ModalWatchStop();
                return 2;
            }

            // Набор — ДО поиска пиков: им подписываются пики, а подписи задают
            // выведенный состав (`S57`).
            if (setName != null && !SelectSet(nuclides, setName))
            {
                ModalWatchStop();
                return 2;
            }

            FWHMPeakDetectionMethodConfig used = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            Console.WriteLine("SETUP\tспектр: {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("SETUP\tSNR={0}\tдопуск={1}\tдиапазон={2}…{3} кэВ",
                              used != null ? used.Min_SNR.ToString("G", CultureInfo.InvariantCulture) : "?",
                              used != null ? used.Tolerance.ToString("G", CultureInfo.InvariantCulture) : "?",
                              used != null ? used.Min_Range.ToString("G", CultureInfo.InvariantCulture) : "?",
                              used != null ? used.Max_Range.ToString("G", CultureInfo.InvariantCulture) : "?");
            Console.WriteLine("SETUP\tкривая: {0}",
                              rd.Efficiency != null ? (rd.Efficiency.Name + (rd.Efficiency.HasGeometry ? " (геометрия есть)" : " (геометрии нет)")) : "нет");

            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            Console.WriteLine("SETUP\tнайдено пиков: {0}", peaks.Count.ToString(CultureInfo.InvariantCulture));
            foreach (Peak p in peaks)
            {
                Console.WriteLine("PEAK\t{0}\t{1}",
                                  p.Energy.ToString("F1", CultureInfo.InvariantCulture),
                                  p.Nuclide != null ? p.Nuclide.Name : "—");
            }

            FsaCompositionInference.Report report;
            FsaSampleSpec spec;
            if (cutName == null)
            {
                // ⛔ ТА ЖЕ перегрузка, что у `FsaAnalysisSession.Compute`: меряем
                //    умолчание приложения, а не своё представление о нём.
                spec = FsaCompositionInference.Infer(peaks, rd, out report);
            }
            else
            {
                FsaChainCut cut;
                switch (cutName)
                {
                    case "whole": cut = FsaChainCut.Whole; break;
                    case "criterion": cut = FsaChainCut.Criterion; break;
                    case "only": cut = FsaChainCut.Only; break;
                    default:
                        Console.Error.WriteLine("--cut= принимает whole|criterion|only");
                        ModalWatchStop();
                        return 2;
                }

                spec = FsaCompositionInference.Infer(peaks, rd, FsaCompositionInference.DefaultCoverage,
                                                    true, true, cut, out report);
            }

            Console.WriteLine();
            Console.WriteLine("=== ОТЧЁТ ВЫВОДА СОСТАВА ===");
            Console.WriteLine("правило обрыва ряда: {0}{1}", report.Cut,
                              cutName == null ? " (умолчание приложения)" : " (--cut=" + cutName + ")");
            Console.WriteLine("REPORT\t{0}", report);
            Console.WriteLine();
            foreach (FsaParentEvidence candidate in report.Candidates)
            {
                Console.WriteLine("CAND\t{0}\t{1}\t{2}",
                                  candidate.Accepted ? "принят " : "отвергнут",
                                  candidate.Nucid, candidate);
                foreach (string member in candidate.ByMember)
                {
                    Console.WriteLine("MEMBER\t{0}\t{1}", candidate.Nucid, member);
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== СОСТАВ (FsaSampleSpec) ===");
            Console.WriteLine("SPEC\tрядов {0}, одиночек {1}, элементов пробы {2}, кристалла {3}",
                              spec.Chains.Count.ToString(CultureInfo.InvariantCulture),
                              spec.Nuclides.Count.ToString(CultureInfo.InvariantCulture),
                              spec.SampleElements.Count.ToString(CultureInfo.InvariantCulture),
                              spec.CrystalElements.Count.ToString(CultureInfo.InvariantCulture));
            foreach (FsaSampleChain chain in spec.Chains)
            {
                var only = new List<string>(chain.Only);
                only.Sort(StringComparer.OrdinalIgnoreCase);
                Console.WriteLine("SPEC\tряд\t{0}\t{1}", chain.Root,
                                  only.Count == 0 ? "весь ряд"
                                                  : "только " + string.Join(",", only.ToArray()));
            }

            foreach (string nuclide in spec.Nuclides)
            {
                Console.WriteLine("SPEC\tодиночка\t{0}", nuclide);
            }

            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            Console.WriteLine();
            Console.WriteLine("=== БИБЛИОТЕКА ===");
            Console.WriteLine("LIBSIZE\t{0}", library.Count.ToString(CultureInfo.InvariantCulture));
            if (libDump)
            {
                foreach (FsaComponent c in library)
                {
                    Console.WriteLine("LIB\t{0}\t{1}\t{2}", c.Name, c.Kind,
                                      c.Lines.Count.ToString(CultureInfo.InvariantCulture));
                }
            }

            // ⛔ ВЕРДИКТ — про то, что увидит человек. Пустая библиотека и есть
            //    тот самый «пустой разбор»: `FsaAnalysisSession` на ней
            //    отвечает `FSANoComponents` и слоёв не рисует вовсе.
            Console.WriteLine();
            Console.WriteLine("ВЕРДИКТ\tразбор {0} (компонентов {1}, родителей принято {2})",
                              library.Count == 0 ? "ПУСТ" : "не пуст",
                              library.Count.ToString(CultureInfo.InvariantCulture),
                              report.Accepted.ToString(CultureInfo.InvariantCulture));

            ModalWatchStop();
            Console.WriteLine("модальных окон за прогон: {0}",
                              modalSeen.ToString(CultureInfo.InvariantCulture));
            return failures > 0 ? 1 : 0;
        }

        static bool SelectSet(NuclideDefinitionManager nuclides, string name)
        {
            var have = new List<string>();
            foreach (NuclideSet set in nuclides.NuclideSets)
            {
                if (set == null)
                {
                    continue;
                }

                have.Add(set.Name);
                if (string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    nuclides.ActiveSet = set;
                    Console.WriteLine("набор: {0}", set.Name);
                    return true;
                }
            }

            Console.Error.WriteLine("набора «{0}» нет; есть: {1}", name, string.Join(", ", have.ToArray()));
            return false;
        }

        static bool AttachEfficiency(ResultData rd, string name)
        {
            foreach (DeviceConfigInfo device in DeviceConfigManager.GetInstance().DeviceConfigList)
            {
                foreach (EfficiencyConfigData curve in device.EfficiencyConfigs)
                {
                    if (string.Equals(curve.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        rd.Efficiency = curve.Copy();
                        return true;
                    }
                }
            }

            Console.Error.WriteLine("кривая «{0}» не нашлась", name);
            return false;
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            // Прибор и его настройки поиска пиков — общим правилом проб (`S82`).
            Console.WriteLine("SETUP\tприбор: {0}", ProbeDeviceConfig.Attach(rd));

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                        cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  СТОРОЖ МОДАЛЬНЫХ ОКОН — приём взят у `CultureProbeO14` (файл её не
        //  трогается). Каждые 200 мс перечисляются окна СВОЕГО процесса класса
        //  `#32770`, текст называется, окно закрывается `WM_CLOSE`.
        // ══════════════════════════════════════════════════════════════════════

        const string DialogClass = "#32770";
        const uint WM_CLOSE = 0x0010;

        delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowProc lpfn, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        static volatile bool modalWatchStop;
        static Thread modalWatchThread;

        static void ModalWatchStart()
        {
            modalWatchThread = new Thread(delegate ()
            {
                uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                var known = new Dictionary<long, bool>();
                while (!modalWatchStop)
                {
                    var found = new List<IntPtr>();
                    try
                    {
                        EnumWindows(delegate (IntPtr h, IntPtr l)
                        {
                            uint pid;
                            GetWindowThreadProcessId(h, out pid);
                            if (pid != self) return true;
                            var cls = new StringBuilder(64);
                            GetClassNameW(h, cls, cls.Capacity);
                            if (cls.ToString() == DialogClass) found.Add(h);
                            return true;
                        }, IntPtr.Zero);
                    }
                    catch (Exception) { }

                    foreach (IntPtr h in found)
                    {
                        long key = h.ToInt64();
                        if (known.ContainsKey(key)) continue;
                        known[key] = true;
                        modalSeen++;
                        failures++;
                        Console.WriteLine("⛔ БЕЗОКОННЫЙ ПУТЬ УПЁРСЯ В МОДАЛЬНОЕ ОКНО: «" + ModalText(h)
                                          + "» — сторож закрывает его сам");
                        try { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
                        catch (Exception) { }
                    }

                    Thread.Sleep(200);
                }
            });
            modalWatchThread.IsBackground = true;
            modalWatchThread.Start();
        }

        static string ModalText(IntPtr dialog)
        {
            var acc = new StringBuilder();
            try
            {
                EnumChildWindows(dialog, delegate (IntPtr ch, IntPtr l)
                {
                    var cls = new StringBuilder(64);
                    GetClassNameW(ch, cls, cls.Capacity);
                    if (cls.ToString() == "Static")
                    {
                        var txt = new StringBuilder(512);
                        GetWindowTextW(ch, txt, txt.Capacity);
                        string s = txt.ToString().Trim();
                        if (s.Length > 0)
                        {
                            if (acc.Length > 0) acc.Append(" / ");
                            acc.Append(s);
                        }
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception) { }

            return acc.Length == 0 ? "(текст не прочитан)" : acc.ToString();
        }

        static void ModalWatchStop()
        {
            modalWatchStop = true;
            if (modalWatchThread != null) modalWatchThread.Join(2000);
        }

        /// <summary>
        /// Положительный контроль сторожа: окно поднимается НАРОЧНО, на фоновом
        /// потоке. Сторож обязан назвать его текст и закрыть.
        /// </summary>
        static void ModalControl()
        {
            Console.WriteLine("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА ОКОН (--modal-control)");
            var th = new Thread(delegate ()
            {
                System.Windows.Forms.MessageBox.Show("контрольное окно полосы F51",
                                                     "контроль",
                                                     System.Windows.Forms.MessageBoxButtons.OK);
            });
            th.IsBackground = true;
            th.Start();
            th.Join(6000);
        }
    }
}
