using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER211`, П238 07.10.2026; решения Amber вопросником, дословно: «Не чаще раза в 30 с и
    /// только для видимых», консоль: «30 секунд - вынеси в настройки») ПАУЗА СЧЁТА FSA ПРИ ЗАПИСИ
    /// СПЕКТРА — <see cref="FsaAnalysisSession.EnsureUpToDate(ResultData, bool, FsaCalculationOptions)"/>.
    ///
    /// Детерминированно, без зависимости от скорости счёта: часы сеанса подменены
    /// (<see cref="FsaAnalysisSession.Clock"/>), интервал — переопределением
    /// (<see cref="FsaAnalysisSession.AcquisitionIntervalOverride"/>). Десять изменений спектра с
    /// шагом поддельных часов в одну секунду, каждое — заказ счёта и ожидание покоя сеанса:
    ///   * запись идёт, интервал 3 с → стартов 4 (t = 0, 3, 6, 9);
    ///   * запись идёт, интервал 0 → стартов 10 (положительный контроль: пауза выключена);
    ///   * записи нет, интервал 3 с → стартов 10 (положительный контроль: статичный спектр пауза не трогает);
    ///   * запись идёт, интервал 3 с, спектр НЕ меняется → стартов 1 (тот же отпечаток не считается заново).
    /// Старты считаются крючком <see cref="FsaAnalysisSession.ProbeAnalyzerHook"/> — он зовётся на
    /// каждый поставленный счёт.
    ///
    /// Ключи: --spectrum=&lt;файл&gt; (обязателен; подойдёт любой спектр корпуса), --timeout=120.
    /// Коды: 0 — сошлось; 1 — расхождения; 2 — ключи/файл; 3 — сеанс не успокоился за timeout.
    /// </summary>
    static class FsaThrottleProbeP238
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null;
            int timeoutS = 120;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (path == null || !File.Exists(path))
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            ResultData rd = Load(path, nuclides);
            if (rd == null || rd.EnergySpectrum == null || rd.EnergySpectrum.Spectrum == null)
            {
                Console.Error.WriteLine("спектр не прочитан");
                return 2;
            }
            if (rd.ResultDataStatus == null)
            {
                rd.ResultDataStatus = new ResultDataStatus();
            }

            int starts = 0;
            FsaAnalysisSession.ProbeAnalyzerHook = analyzer => Interlocked.Increment(ref starts);
            DateTime epoch = new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
            double fake = 0.0;
            FsaAnalysisSession.Clock = () => epoch.AddSeconds(fake);

            Console.WriteLine("SETUP\t{0}: каналов {1}, отсчётов {2}; поддельные часы, шаг 1 с, десять изменений спектра",
                              Path.GetFileName(path), rd.EnergySpectrum.NumberOfChannels, rd.EnergySpectrum.TotalPulseCount);

            // Прогрев: первый счёт — вне сравнения (загрузка базы, образы).
            FsaAnalysisSession.AcquisitionIntervalOverride = 0.0;
            rd.ResultDataStatus.Recording = false;
            int rc = Scenario("прогрев", rd, ref fake, ref starts, 1, true, timeoutS, -1);
            if (rc != 0) return rc;

            rc = Scenario("запись идёт, интервал 3 с", rd, ref fake, ref starts, 10, true, timeoutS, 4, recording: true, interval: 3.0);
            if (rc != 0) return rc;
            rc = Scenario("запись идёт, интервал 0 (контроль: пауза выключена)", rd, ref fake, ref starts, 10, true, timeoutS, 10, recording: true, interval: 0.0);
            if (rc != 0) return rc;
            rc = Scenario("записи нет, интервал 3 с (контроль: статичный спектр без паузы)", rd, ref fake, ref starts, 10, true, timeoutS, 10, recording: false, interval: 3.0);
            if (rc != 0) return rc;
            rc = Scenario("запись идёт, интервал 3 с, спектр не меняется", rd, ref fake, ref starts, 10, false, timeoutS, 1, recording: true, interval: 3.0);
            if (rc != 0) return rc;

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Серия из `steps` заказов счёта с шагом поддельных часов в секунду; перед каждым — при
        /// `change` спектр меняется (одним отсчётом и секундой набора, отпечаток новый). После
        /// каждого заказа ждёт покоя сеанса. `expected` — ожидаемое число стартов (−1 — не судить).
        /// </summary>
        static int Scenario(string title, ResultData rd, ref double fake, ref int starts, int steps, bool change,
                            int timeoutS, int expected, bool recording = false, double interval = 0.0)
        {
            var session = new FsaAnalysisSession();
            FsaAnalysisSession.AcquisitionIntervalOverride = interval;
            rd.ResultDataStatus.Recording = recording;
            // первый заказ серии всегда идёт: сеанс новый, последнего старта у него нет
            fake += 100.0;
            int before = starts;
            for (int i = 0; i < steps; i++)
            {
                if (change || i == 0)
                {
                    if (change && i > 0)
                    {
                        int k = Math.Min(100 + i, rd.EnergySpectrum.Spectrum.Length - 1);
                        rd.EnergySpectrum.Spectrum[k] += 1;
                        rd.EnergySpectrum.TotalPulseCount += 1;
                        rd.EnergySpectrum.MeasurementTime += 1.0;
                        rd.EnergySpectrum.LiveTime += 1.0;
                    }
                }
                session.EnsureUpToDate(rd, false, FsaCalculationOptions.Of(rd));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (session.IsRunning)
                {
                    if (sw.Elapsed.TotalSeconds > timeoutS)
                    {
                        Console.WriteLine("⛔ {0}: сеанс не успокоился за {1} с", title, timeoutS);
                        return 3;
                    }
                    Thread.Sleep(10);
                }
                fake += 1.0;
            }
            int made = starts - before;
            if (expected >= 0)
            {
                bool ok = made == expected;
                Console.WriteLine("  {0} {1,-70} стартов {2} (ожидалось {3})", ok ? "ok  " : "⛔ ", title, made, expected);
                if (!ok) bad++;
            }
            else
            {
                Console.WriteLine("  {0}: стартов {1}", title, made);
            }
            session.Reset();
            return 0;
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
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
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }
            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }
                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }
    }
}
