using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`fixFsa2`, 07.11.2026) ЗАМЕР ОБРАЗОВ ВЫЛЕТА И ШКАЛЫ В РАЗБОРЕ FSA ПУТЁМ ПРИЛОЖЕНИЯ —
    /// сеансом документа (`FsaAnalysisSession`, настройки `FsaCalculationOptions.Of`), на
    /// конфиге рабочего каталога. Поводы — проверка экраном 07.11.2026: на Y-88 без своего
    /// прибора «SE-1461 41.90 %» при K-40 42.38 %, на Cs-137 «DE-1461 1.83 %» при K-40 0.48 %,
    /// растяжение шкалы ровно «+2.00 %», порог «z ≥ 0.69».
    ///
    /// Печатает: найденные пики; по компоненту — вид, доля, доля в пиках, скорость, z, отсчёты
    /// образа всего и в окнах ±1 FWHM вокруг заданных энергий (`--at=`), центр масс образа в
    /// кэВ; шкалу (усиление, нуль, опоры), порог k_α. Ключи: --spectrum=&lt;файл&gt;
    /// [--at=898,949.8,1461] [--no-escape] [--labels] [--set=<имя набора>] [--efficiency=<кривая прибора>] [--timeout=600].
    /// Сравнивать «без образов» (`--no-escape`) — положительный контроль того, что образ
    /// действительно забирает пик. Кодов приёмки нет: это замер (0 — разбор дошёл, 3 — нет).
    /// </summary>
    static class EscapeShareProbe
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string path = null;
            var at = new List<double>();
            bool noEscape = false;
            bool labels = false;
            string setName = null;
            string efficiencyName = null;
            int timeoutS = 600;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--spectrum=", StringComparison.Ordinal)) path = arg.Substring(11);
                else if (arg.StartsWith("--at=", StringComparison.Ordinal))
                    at.AddRange(arg.Substring(5).Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)));
                else if (arg == "--no-escape") noEscape = true;
                else if (arg == "--labels") labels = true;
                else if (arg.StartsWith("--set=", StringComparison.Ordinal)) setName = arg.Substring(6);
                else if (arg.StartsWith("--efficiency=", StringComparison.Ordinal)) efficiencyName = arg.Substring(13);
                else if (arg.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(arg.Substring(10), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + arg); return 2; }
            }
            if (path == null || !File.Exists(path)) { Console.Error.WriteLine("нужен --spectrum=<файл>"); return 2; }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            if (setName != null)
            {
                // набор — ДО поиска пиков: им подписываются пики, а подписи задают состав «по меткам»;
                // сеанс пробы без документа читает тот же общий выбор (`NuclideSetSource` не задан)
                NuclideSet picked = nuclides.NuclideSets.FirstOrDefault(x => string.Equals(x.Name, setName, StringComparison.Ordinal));
                if (picked == null) { Console.Error.WriteLine("набора «{0}» нет", setName); return 2; }
                nuclides.ActiveSet = picked;
            }

            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(stream);
            }
            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s.TotalPulseCount == 0)
            {
                long total = 0;
                foreach (int c in s.Spectrum) total += c;
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }
            Console.WriteLine("SETUP\t{0}: прибор {1}; кривая {2}; набор {3}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd),
                              rd.Efficiency != null ? rd.Efficiency.Name : "нет",
                              nuclides.ActiveSet != null ? nuclides.ActiveSet.Name : "все");
            if (efficiencyName != null)
            {
                EfficiencyConfigData picked = DeviceConfigManager.GetInstance().DeviceConfigList
                    .SelectMany(d => d.EfficiencyConfigs).FirstOrDefault(c => string.Equals(c.Name, efficiencyName, StringComparison.OrdinalIgnoreCase));
                if (picked == null) { Console.Error.WriteLine("кривая «{0}» не нашлась", efficiencyName); return 2; }
                rd.Efficiency = picked.Copy();
                Console.WriteLine("SETUP	кривая подменена: {0}", picked.Name);
            }
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            foreach (Peak p in rd.DetectedPeaks)
            {
                Console.WriteLine("PEAK\t{0:F2}\t{1}\tSNR {2:F1}", p.Energy, p.Nuclide != null ? p.Nuclide.Name : "(неизвестный)", p.SNR);
            }

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            if (noEscape) options.EscapeAndAnnihilation = false;
            if (labels) options.DbLookups = false;
            Console.WriteLine("OPTIONS\t{0}", options.Stamp);
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (o, e) => done.Set();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
            if (!done.WaitOne(TimeSpan.FromSeconds(timeoutS)) || session.Result == null)
            {
                Console.WriteLine("⛔ разбора нет: {0}", session.Status ?? "причина не названа");
                return 3;
            }
            FsaResult r = session.Result;
            EnergyCalibration cal = s.EnergyCalibration;
            FwhmCalibration fw = rd.FwhmCalibration;
            Console.WriteLine("SCALE\tgain {0:F5} ({1:+0.00;-0.00} %)\toffset {2:F3} кан.\tопор {3}\tk_alpha {4}\tk_beta {5}",
                              r.Gain, 100.0 * (r.Gain - 1.0), r.OffsetChannels, r.ScaleAnchorsUsed,
                              F(r.LimitDecisionQuantile), F(r.LimitDetectionQuantile));
            if (!string.IsNullOrEmpty(r.AnchorNote)) Console.WriteLine("ANCHOR_NOTE\t{0}", r.AnchorNote);
            if (r.ScaleAnchors != null)
            {
                foreach (FsaScaleAnchor a in r.ScaleAnchors)
                {
                    Console.WriteLine("ANCHOR\t{0}\tline {1}\tmodel {2:F2}\tmeas {3:F2}\tshift {4:F2}\tz {5:F1}\tused {6}\t{7}",
                                      a.Component, F(a.LineKev), a.ModelKev, a.MeasuredKev, a.ShiftKev, a.Z, a.Used, a.Refusal ?? "");
                }
            }
            double topZ = double.NaN;
            Console.WriteLine("COMP\tимя\tвид\tдоля%\tв пиках%\tимп/с\tz\tотсч.образа\tц.м. кэВ\t" + string.Join("\t", at.Select(e => "±1FWHM@" + e.ToString("0.#", CultureInfo.InvariantCulture))));
            foreach (FsaComponentResult c in r.Components)
            {
                double sum = 0, moment = 0;
                var windows = new double[at.Count];
                if (c.Curve != null)
                {
                    for (int ch = 0; ch < c.Curve.Length; ch++)
                    {
                        double v = c.Curve[ch];
                        if (!(v > 0)) continue;
                        double e = cal.ChannelToEnergy(ch);
                        sum += v;
                        moment += v * e;
                        for (int k = 0; k < at.Count; k++)
                        {
                            double chAt = cal.EnergyToChannel(at[k], c.Curve.Length);
                            double half = fw != null ? fw.ChannelToFwhm(chAt) : 0.07 * chAt;
                            if (Math.Abs(ch - chAt) <= half) windows[k] += v;
                        }
                    }
                }
                if (c.Kind != FsaComponentKind.Nuisance && !double.IsNaN(c.Z) && (double.IsNaN(topZ) || c.Z > topZ)) topZ = c.Z;
                Console.WriteLine("COMP\t{0}\t{1}\t{2:F2}\t{3:F2}\t{4:G5}\t{5:F2}\t{6:F0}\t{7:F1}\t{8}", c.Name, c.Kind, c.SharePercent, c.PeakSharePercent,
                                  c.CountRate, c.Z, sum, sum > 0 ? moment / sum : double.NaN,
                                  string.Join("\t", windows.Select(w => w.ToString("F0", CultureInfo.InvariantCulture))));
            }
            Console.WriteLine("TOPZ\tнаибольшая z не-мешающих {0:F2}; 0.3×{0:F2} = {1:F2}", topZ, 0.3 * topZ);
            if (r.UncoveredPeaks != null)
            {
                foreach (var u in r.UncoveredPeaks) Console.WriteLine("UNCOVERED\t{0}", Describe(u));
            }
            return 0;
        }

        static string Describe(object o)
        {
            var sb = new StringBuilder();
            foreach (var p in o.GetType().GetProperties())
            {
                object v = p.GetValue(o, null);
                if (v is double d) sb.Append(p.Name).Append('=').Append(d.ToString("G5", CultureInfo.InvariantCulture)).Append(' ');
                else if (v is string || v is int || v is bool) sb.Append(p.Name).Append('=').Append(v).Append(' ');
            }
            return sb.ToString();
        }

        static string F(double v)
        {
            return double.IsNaN(v) ? "NaN" : v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
