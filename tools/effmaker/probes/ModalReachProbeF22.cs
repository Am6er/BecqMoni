using BecquerelMonitor;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WinMM;

namespace ModalReachProbeF22
{
    /// <summary>
    /// ОСТАТОК `A245` — ЗАМЕР, ЧТО ЧЕТЫРЕ ПОЧИНЕННЫХ МЕСТА БОЛЬШЕ НЕ ПОДНИМАЮТ
    /// ОКНА В БЕЗОКОННОМ ПРОГОНЕ (полоса F22, 05.09.2026).
    ///
    /// ⛔ Сторож `tools/check_headless.py` судит ИСХОДНИК: он говорит, что в
    /// безоконном пути не осталось `MessageBox.Show`. Этого мало: сторож не
    /// исполняет ни строчки и не знает, ДОХОДИТ ли выполнение до места. Здесь
    /// мерится само поведение — прогон БЕЗ ОКОН заводится в оба почищенных
    /// места и обязан пройти их насквозь.
    ///
    /// Плечи (оба — положительный контроль самого замера: без следа «место
    /// сработало» отсутствие окна неотличимо от «проба туда не дошла»):
    ///
    ///   `fwhm`  — `DCFwhmCalibrationView.EnergySpectrumView_PeakPickuped`,
    ///             ветка «такая точка уже есть» (`DCFwhmCalibrationView.cs:420`
    ///             до правки). В список калибровки заранее кладётся точка,
    ///             РАВНАЯ подбираемой (`CalibrationPeak.Equals` сравнивает
    ///             ПШПВ и канал), и обработчик зовётся отражением. След —
    ///             строка `ERRPeakExist` в потоке ошибок, которую печатает
    ///             дверь `AppUi.Report` без окон.
    ///   `wave`  — `StandardPulseRecorder.StartRecording` с заведомо негодным
    ///             форматом звука (`StandardPulseRecorder.cs:95/101/112` до
    ///             правки). След — строка `ERRNotSupportedWavFormat` либо
    ///             сообщение `MMSystemException`, смотря чем ответит система.
    ///
    /// ⚠ Заодно плечо `fwhm` проверяет ТОЧКУ в числах сообщения: текст
    /// `ERRPeakExist` собирается `String.Format`-ом от ПШПВ и канала, и по
    /// правилу Amber (`A244`) он обязан печатать дробную часть через точку на
    /// любой культуре. Ключ `--culture=` подставляет культуру потока.
    ///
    /// Сторож модальных окон (по образцу полосы F20): фоновый поток каждые
    /// 200 мс перечисляет окна СВОЕГО процесса класса `#32770`, называет их
    /// текст, засчитывает расхождение и посылает `WM_CLOSE`. Прогон не зависит
    /// от того, закроет ли окно человек снаружи, а окно названо ПО ИМЕНИ.
    ///
    ///   ModalReachProbeF22.exe [--culture=ru-RU] [--modal-control]
    ///
    /// Код возврата: 0 — оба места пройдены насквозь и окон не поднято;
    /// 1 — расхождение (окно поднялось ЛИБО место не сработало); 2 — ключи.
    /// </summary>
    static class Program
    {
        static int bad;
        static string cultureName = "ru-RU";
        static bool modalControl;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            foreach (string a in args)
            {
                if (a.StartsWith("--culture=")) cultureName = a.Substring(10);
                else if (a == "--modal-control") modalControl = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(cultureName);
            Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(cultureName);

            string asm = typeof(DocumentManager).Assembly.Location;
            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + asm);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(asm).ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine("  культура потока: " + Thread.CurrentThread.CurrentCulture.Name);
            Console.WriteLine();

            ModalWatch.Start();

            if (modalControl) ArmModalControl();
            ArmFwhm();
            ArmWave();

            ModalWatch.Stop();

            Console.WriteLine();
            Console.WriteLine("модальных окон за прогон: " + ModalWatch.Count
                              + (ModalWatch.Count == 0 ? " ✅ окон не было" : " ⛔ окна поднимались"));
            Console.WriteLine("РАСХОЖДЕНИЙ: " + bad
                              + (modalControl ? " (контроль сторожа: расхождение ОБЯЗАНО быть)" : ""));
            return bad == 0 ? 0 : 1;
        }

        // ==================================================================
        // ПЛЕЧО `fwhm`: ветка «такая точка уже есть»
        // ==================================================================
        static void ArmFwhm()
        {
            Console.WriteLine("=== ПЛЕЧО `fwhm` — DCFwhmCalibrationView.EnergySpectrumView_PeakPickuped ===");

            DocEnergySpectrum doc = DocumentManager.GetInstance().CreateDocument("f22-fwhm.xml");
            if (doc == null) { Fail("документ не создан"); return; }

            ResultData rd = doc.ActiveResultData;
            EnergySpectrum es = rd.EnergySpectrum;
            for (int i = 0; i < es.NumberOfChannels; i++)
            {
                double d = (i - 500.0) / 8.0;
                es.Spectrum[i] = 20 + (int)(4000.0 * Math.Exp(-0.5 * d * d));
            }
            es.MeasurementTime = 300.0;
            es.TotalPulseCount = es.Spectrum.Sum(x => (long)x);

            FWHMPeakDetectionMethodConfig cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (rd.FwhmCalibration == null && cfg != null)
            {
                rd.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, es.EnergyCalibration);
            }
            if (rd.FwhmCalibration == null) { Fail("кривая ПШПВ не построилась — сцена не та"); return; }

            // Точка, РАВНАЯ подбираемой: `CalibrationPeak.Equals` сравнивает
            // ПШПВ и канал, значит ветка «уже есть» сработает на первом же
            // проходе цикла — то самое место, где стояло голое окно.
            rd.FwhmCalibration.CalibrationPeaks.Clear();
            rd.FwhmCalibration.CalibrationPeaks.Add(new CalibrationPeak { Channel = 510, Energy = 1020.0, FWHM = 9.5 });

            MainForm form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            SetField(form, "activeDocument", doc);
            SetField(form, "documentManager", DocumentManager.GetInstance());

            DCFwhmCalibrationView view = new DCFwhmCalibrationView(form);
            SetField(view, "peakPickupProcessing", true);

            int before = rd.FwhmCalibration.CalibrationPeaks.Count;
            string err = CaptureErr(delegate
            {
                MethodInfo mi = typeof(DCFwhmCalibrationView).GetMethod("EnergySpectrumView_PeakPickuped",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (mi == null) throw new InvalidOperationException("метода EnergySpectrumView_PeakPickuped НЕТ в сборке");
                mi.Invoke(view, new object[] { null, new PeakPickupedEventArgs(510, 1020.0, 9.5, 480, 540) });
            });
            int after = rd.FwhmCalibration.CalibrationPeaks.Count;

            Console.WriteLine("  точек было " + before + ", стало " + after + " (ветка отказа: расти не должно)");
            Console.WriteLine("  поток ошибок: " + OneLine(err));

            // Положительный контроль ПЛЕЧА: без следа «место сработало»
            // отсутствие окна ничего не значит — проба могла до него не дойти.
            Check("ветка «точка уже есть» ИСПОЛНИЛАСЬ (дверь сообщила строкой)",
                  err.Contains("FWHM") || err.Contains("ПШПВ"),
                  "в потоке ошибок нет сообщения `ERRPeakExist`: [" + OneLine(err) + "]");
            Check("точка НЕ добавлена (ветка вернула управление)", after == before,
                  "точек " + before + " -> " + after);

            // ⚠ `A244`: числа сообщения печатаются `String.Format`-ом, и по
            //    правилу Amber дробная часть — ТОЧКА на любой культуре.
            Check("в сообщении ПШПВ напечатана ТОЧКОЙ (`A244`)",
                  err.Contains("9.5"),
                  "ждали «9.5», в строке [" + OneLine(err) + "]");
            Console.WriteLine();
        }

        // ==================================================================
        // ПЛЕЧО `wave`: StandardPulseRecorder.StartRecording, негодный формат
        // ==================================================================
        static void ArmWave()
        {
            Console.WriteLine("=== ПЛЕЧО `wave` — StandardPulseRecorder.StartRecording ===");

            int devices = WaveIn.Devices.Count;
            Console.WriteLine("  звуковых входов на машине: " + devices);

            WaveInDeviceCaps caps = new WaveInDeviceCaps();
            caps.DeviceId = devices > 0 ? 0 : -1;

            // Формат, которого не бывает: 7 бит на отсчёт. `SupportsFormat`
            // отвечает `WAVERR_BADFORMAT` — ветка 95; если система ответит
            // иначе, полетит `MMSystemException` и сработает ветка 101/112.
            // Обе теперь ходят одной дверью, и обе годятся замеру.
            WaveFormat fmt = new WaveFormat();
            fmt.FormatTag = WaveFormatTag.Pcm;
            fmt.Channels = 1;
            fmt.SamplesPerSecond = 8000;
            fmt.BitsPerSample = 7;

            StandardPulseRecorder rec = new StandardPulseRecorder();
            bool started = true;
            string thrown = null;
            string err = CaptureErr(delegate
            {
                try { started = rec.StartRecording(caps, fmt, false); }
                catch (Exception ex) { thrown = ex.GetType().Name + ": " + ex.Message; }
            });

            Console.WriteLine("  StartRecording вернул: " + started
                              + (thrown == null ? "" : "; бросил " + thrown));
            Console.WriteLine("  поток ошибок: " + OneLine(err));

            Check("место отказа звукового входа ИСПОЛНИЛОСЬ (дверь сообщила строкой)",
                  err.Length > 0,
                  "поток ошибок пуст — проба до места НЕ ДОШЛА, и замер ничего не значит");
            Check("StartRecording вернул false (ветка отказа прошла насквозь)",
                  !started || thrown != null,
                  "запись якобы началась на негодном формате");
            Console.WriteLine();
        }

        // ==================================================================
        // Контроль СТОРОЖА окон: окно поднимается нарочно
        // ==================================================================
        static void ArmModalControl()
        {
            Console.WriteLine("=== КОНТРОЛЬ СТОРОЖА: окно поднимается НАРОЧНО ===");
            Thread t = new Thread(delegate ()
            {
                MessageBox.Show("контрольное окно полосы F22", "", MessageBoxButtons.OK, MessageBoxIcon.None);
            });
            t.IsBackground = true;
            t.Start();
            t.Join(10000);
            Console.WriteLine("  окно поднято нарочно, поток вернулся: " + !t.IsAlive);
            Console.WriteLine();
        }

        // ==================================================================
        // Сторож модальных окон СВОЕГО процесса (образец — полоса F20)
        // ==================================================================
        static class ModalWatch
        {
            [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
            [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc f, IntPtr l);
            [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
            [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
            delegate bool EnumProc(IntPtr h, IntPtr l);

            static Thread worker;
            static volatile bool run;
            static readonly HashSet<long> seen = new HashSet<long>();
            static int count;
            static int self;

            public static int Count { get { return count; } }

            public static void Start()
            {
                self = System.Diagnostics.Process.GetCurrentProcess().Id;
                run = true;
                worker = new Thread(Loop);
                worker.IsBackground = true;
                worker.Start();
            }

            public static void Stop()
            {
                run = false;
                if (worker != null) worker.Join(2000);
                Sweep();
            }

            static void Loop()
            {
                while (run) { Sweep(); Thread.Sleep(200); }
            }

            static void Sweep()
            {
                try
                {
                    EnumWindows(delegate (IntPtr h, IntPtr l)
                    {
                        int pid;
                        GetWindowThreadProcessId(h, out pid);
                        if (pid != self) return true;
                        StringBuilder c = new StringBuilder(256);
                        GetClassNameW(h, c, 256);
                        if (c.ToString() != "#32770") return true;
                        long id = h.ToInt64();
                        lock (seen)
                        {
                            if (seen.Contains(id)) return true;
                            seen.Add(id);
                        }
                        StringBuilder acc = new StringBuilder();
                        EnumChildWindows(h, delegate (IntPtr ch, IntPtr cl)
                        {
                            StringBuilder cc = new StringBuilder(256);
                            GetClassNameW(ch, cc, 256);
                            if (cc.ToString() == "Static")
                            {
                                StringBuilder ct = new StringBuilder(1024);
                                GetWindowTextW(ch, ct, 1024);
                                acc.Append(ct.ToString()).Append(" ");
                            }
                            return true;
                        }, IntPtr.Zero);
                        Interlocked.Increment(ref count);
                        Console.WriteLine("⛔ БЕЗОКОННЫЙ ПУТЬ УПЁРСЯ В МОДАЛЬНОЕ ОКНО: «"
                                          + acc.ToString().Trim() + "» — закрываю");
                        bad++;
                        PostMessageW(h, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                        return true;
                    }, IntPtr.Zero);
                }
                catch (Exception) { }
            }
        }

        // ==================================================================
        static string CaptureErr(Action body)
        {
            TextWriter old = Console.Error;
            StringWriter sw = new StringWriter();
            Console.SetError(sw);
            try { body(); }
            catch (TargetInvocationException ex)
            {
                Console.SetError(old);
                Console.WriteLine("  ⚠ вызов бросил: " + (ex.InnerException == null ? ex.Message : ex.InnerException.GetType().Name + ": " + ex.InnerException.Message));
                return sw.ToString();
            }
            catch (Exception ex)
            {
                Console.SetError(old);
                Console.WriteLine("  ⚠ вызов бросил: " + ex.GetType().Name + ": " + ex.Message);
                return sw.ToString();
            }
            finally { Console.SetError(old); }
            return sw.ToString();
        }

        static void Check(string what, bool ok, string why)
        {
            Console.WriteLine((ok ? "  ✅ " : "  ⛔ ") + what + (ok ? "" : " — " + why));
            if (!ok) bad++;
        }

        static void Fail(string why)
        {
            Console.WriteLine("  ⛔ " + why);
            bad++;
        }

        static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(пусто)";
            return s.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        static void SetField(object target, string name, object value)
        {
            Type t = target.GetType();
            while (t != null)
            {
                FieldInfo fi = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null) { fi.SetValue(target, value); return; }
                t = t.BaseType;
            }
            throw new InvalidOperationException("поля " + name + " нет: " + target.GetType().Name);
        }
    }
}
