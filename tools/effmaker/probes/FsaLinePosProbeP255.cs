using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П255 14.10.2026) ГДЕ МОДЕЛЬ FSA СТАВИТ ЛИНИЮ — против калибровки файла и против данных.
    ///
    /// Зачем: правило покрытия пиков вне состава (`AMBER210`, <c>FindUncoveredPeaks</c>)
    /// сравнивает центр серии избытка с ИСТИННЫМИ энергиями линий; проба меряет, в каком
    /// канале стоит линия E: (1) по калибровке файла; (2) по месту в модели
    /// (<see cref="FsaAnalyzer.LinePositionChannel"/> плюс усиление и нуль результата);
    /// (3) вершина МОДЕЛИ и (4) вершина ДАННЫХ рядом — центр тяжести над полувысотой.
    /// Печатает и состояние карты света анализатора (отражением).
    ///
    /// Ключи: --spectrum=&lt;файл&gt;; --set=&lt;имя&gt;; --fromset; --equilibrium; --nucbase;
    /// --lines=238.6,583.2,… (кэВ); --no-matrix; --dump=lo,hi[,шаг] — каналы: данные минус фон, модель, pull.
    /// Коды: 0 — посчитано; 2 — ключи/файлы; 3 — разбор не завершился.
    /// </summary>
    static class FsaLinePosProbeP255
    {
        static FsaAnalyzer LastAnalyzer;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null, setName = null;
            bool fromSet = false, equilibrium = false, nucbase = false, noMatrix = false;
            var lines = new List<double>();
            int dumpLo = -1, dumpHi = -1, dumpStep = 1;
            var props = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a == "--fromset") fromSet = true;
                else if (a == "--equilibrium") equilibrium = true;
                else if (a == "--nucbase") nucbase = true;
                else if (a == "--no-matrix") noMatrix = true;
                else if (a.StartsWith("--prop=", StringComparison.Ordinal)) props.Add(a.Substring(7));
                else if (a.StartsWith("--dump=", StringComparison.Ordinal))
                {
                    string[] p = a.Substring(7).Split(',');
                    dumpLo = int.Parse(p[0], CultureInfo.InvariantCulture);
                    dumpHi = int.Parse(p[1], CultureInfo.InvariantCulture);
                    if (p.Length > 2) dumpStep = int.Parse(p[2], CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--lines=", StringComparison.Ordinal))
                {
                    foreach (string p in a.Substring(8).Split(','))
                    {
                        if (p.Trim().Length > 0) lines.Add(double.Parse(p.Trim(), CultureInfo.InvariantCulture));
                    }
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrum == null || lines.Count == 0)
            {
                Console.Error.WriteLine("нужны --spectrum= и --lines=");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            if (setName != null)
            {
                NuclideSet set = null;
                foreach (NuclideSet s in nuclides.NuclideSets)
                {
                    if (string.Equals(s.Name, setName, StringComparison.OrdinalIgnoreCase)) set = s;
                }

                if (set == null) { Console.Error.WriteLine("сета нет: " + setName); return 2; }
                nuclides.ActiveSet = set;
            }

            ResultData rd = Load(spectrum, nuclides);
            if (rd == null) return 2;
            if (noMatrix && rd.Efficiency != null) rd.Efficiency.UseResponseMatrix = false;

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            if (nucbase) options.DbLookups = true;
            if (equilibrium) options.ChainEquilibrium = true;
            if (fromSet) options.FromSet = true;
            FsaAnalysisSession.ProbeAnalyzerHook = analyzer =>
            {
                LastAnalyzer = analyzer;
                foreach (string kv in props)
                {
                    int eq = kv.IndexOf('=');
                    PropertyInfo pi = typeof(FsaAnalyzer).GetProperty(kv.Substring(0, eq));
                    string text = kv.Substring(eq + 1);
                    object value = pi.PropertyType == typeof(bool) ? (object)bool.Parse(text)
                        : pi.PropertyType == typeof(double) ? (object)double.Parse(text, CultureInfo.InvariantCulture)
                        : pi.PropertyType == typeof(int) ? (object)int.Parse(text, CultureInfo.InvariantCulture)
                        : text;
                    pi.SetValue(analyzer, value);
                    Console.WriteLine("ключ анализатора: {0} = {1}", pi.Name, value);
                }
            };
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
            if (!done.WaitOne(TimeSpan.FromSeconds(300)) || session.Result == null || LastAnalyzer == null)
            {
                Console.WriteLine("разбор не завершился");
                return 3;
            }

            FsaResult result = session.Result;
            FsaAnalyzer an = LastAnalyzer;
            EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
            int channels = rd.EnergySpectrum.NumberOfChannels;
            Console.WriteLine("матрица {0}; усиление {1}; нуль {2} кан.; опор {3}; карта света: {4}",
                              result.ResponseMatrixUsed, F(result.Gain, "R"), F(result.OffsetChannels, "F3"),
                              result.ScaleAnchorsUsed, State(an));
            foreach (FsaComponentResult row in result.Components)
            {
                if (row.Kind != FsaComponentKind.Nuisance)
                {
                    Console.WriteLine("строка {0,-12} {1,12} имп/с  z {2}", row.Name, F(row.CountRate, "F3"), F(row.Z, "F1"));
                }
            }

            Console.WriteLine("χ²/ndf Пуассона {0}", F(result.Chi2NdfPoisson, "F2"));
            Console.WriteLine("привязка: {0}", result.AnchorNote);
            Console.WriteLine("ход привязки: {0}", an.ScaleAnchorStats);
            foreach (FsaScaleAnchor anchor in result.ScaleAnchors)
            {
                Console.WriteLine("опора {0} {1}: модель {2} кэВ, измерено {3} кэВ, сдвиг {4}, свет {5}, z {6}, взята {7}{8}",
                                  anchor.Component, F(anchor.LineKev, "F1"), F(anchor.ModelKev, "F1"), F(anchor.MeasuredKev, "F1"),
                                  F(anchor.ShiftKev, "F2"), F(anchor.LightShiftKev, "F2"), F(anchor.Z, "F1"), anchor.Used,
                                  string.IsNullOrEmpty(anchor.Refusal) ? "" : " (" + anchor.Refusal + ")");
            }

            Console.WriteLine("{0,9} {1,10} {2,10} {3,10} {4,10} {5,10} {6,8}",
                              "E, кэВ", "кан.файла", "кан.модели", "вершина М", "вершина Д", "E(модель)", "ПШПВ кан");
            MethodInfo drift = typeof(FsaAnalyzer).GetMethod("DriftPosition", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (double e in lines)
            {
                double fileCh = cal.EnergyToChannel(e, channels);
                double pos = an.LinePositionChannel(cal, e, channels);
                double modelCh = (double)drift.Invoke(an, new object[] { pos, result.Gain, result.OffsetChannels });
                double fwhm = rd.FwhmCalibration != null ? rd.FwhmCalibration.ChannelToFwhm(fileCh) : 10.0;
                double topModel = Top(result.Model, modelCh, fwhm, channels);
                double[] data = new double[channels];
                for (int i = 0; i < channels; i++) data[i] = rd.EnergySpectrum.Spectrum[i];
                double topData = Top(data, modelCh, fwhm, channels);
                Console.WriteLine("{0,9} {1,10} {2,10} {3,10} {4,10} {5,10} {6,8}",
                                  F(e, "F1"), F(fileCh, "F1"), F(modelCh, "F1"), F(topModel, "F1"), F(topData, "F1"),
                                  F(cal.ChannelToEnergy(modelCh), "F1"), F(fwhm, "F1"));
            }

            if (dumpLo >= 0)
            {
                Console.WriteLine("{0,6} {1,9} {2,12} {3,12} {4,9}", "кан.", "E файла", "данные-фон", "модель", "pull");
                for (int i = dumpLo; i <= dumpHi && i < channels; i += dumpStep)
                {
                    double y = rd.EnergySpectrum.Spectrum[i] - (result.Background != null && i < result.Background.Length ? result.Background[i] : 0.0);
                    double m = result.Model != null ? result.Model[i] : double.NaN;
                    double sd = Math.Sqrt(Math.Max(1.0, rd.EnergySpectrum.Spectrum[i]));
                    Console.WriteLine("{0,6} {1,9} {2,12} {3,12} {4,9}", i, F(cal.ChannelToEnergy(i), "F1"), F(y, "F1"), F(m, "F1"), F((y - m) / sd, "F1"));
                }
            }

            return 0;
        }

        /// <summary>Центр тяжести над полувысотой локальной вершины в ±ПШПВ от <paramref name="near"/>.</summary>
        static double Top(double[] y, double near, double fwhm, int channels)
        {
            if (y == null) return double.NaN;
            int lo = Math.Max(0, (int)(near - fwhm)), hi = Math.Min(Math.Min(channels, y.Length) - 1, (int)(near + fwhm));
            int top = -1;
            for (int i = lo; i <= hi; i++) if (top < 0 || y[i] > y[top]) top = i;
            if (top < 0) return double.NaN;
            double half = 0.5 * y[top], m = 0.0, w = 0.0;
            for (int i = top; i >= lo && y[i] >= half; i--) { m += y[i] * i; w += y[i]; }
            for (int i = top + 1; i <= hi && y[i] >= half; i++) { m += y[i] * i; w += y[i]; }
            return w > 0.0 ? m / w : top;
        }

        static string State(FsaAnalyzer an)
        {
            var sb = new StringBuilder();
            foreach (string name in new[] { "lightForm", "driftLight", "lightCurveName", "adcScale", "adcLinearKev", "adcE0", "adcZeroKev", "widthScale" })
            {
                FieldInfo f = typeof(FsaAnalyzer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                object v = f != null ? f.GetValue(an) : "?";
                sb.Append(name).Append('=').Append(v is double ? F((double)v, "R") : Convert.ToString(v, CultureInfo.InvariantCulture)).Append(' ');
            }

            return sb.ToString();
        }

        static string F(double v, string f)
        {
            return v.ToString(f, CultureInfo.InvariantCulture);
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            if (!File.Exists(path)) { Console.Error.WriteLine("нет файла: " + path); return null; }
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

            ProbeDeviceConfig.Attach(rd);
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }

            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }
    }
}
