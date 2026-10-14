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
    /// (П258 14.10.2026, `A320`) НУЛЬ СВЕТА ПО СЪЁМКЕ НА ОДНОМ СПЕКТРЕ — трасса кандидатов нуля
    /// (<see cref="FsaAnalyzer.ZeroTraceSink"/>: линия, отказ, z, доля, промах, вес, взят ли и с каким
    /// светом/энергией), ход привязки целиком, опоры итоговой шкалы, строки разбора и χ²/ndf.
    ///
    /// Зачем: строка `A320` — на длинном наборе Th232 Amber «по файлу» привязка ходит по кругу и нуль
    /// света по съёмке уходит на −17.4 кэВ. Проба называет, КАКИЕ кандидаты нуль тянут (их x/e/w
    /// печатаются, выброс по одному считается снаружи), и что меняется на прореженной копии
    /// (<c>--thin=</c>, биномиально, зерно как у <c>FsaBqProbe</c>).
    ///
    /// Ключи: --spectrum=&lt;файл&gt;; --set=&lt;имя&gt;; --fromset; --equilibrium; --nucbase; --no-matrix;
    /// --prop=Имя=значение (свойство анализатора отражением, можно несколько); --thin=p; --seed=n;
    /// --trace (трасса нуля); --lines=E1,E2 (где модель ставит линию — как у FsaLinePosProbeP255).
    /// Коды: 0 — посчитано; 2 — ключи/файлы; 3 — разбор не завершился.
    /// </summary>
    static class FsaAnchorZeroProbeP258
    {
        static FsaAnalyzer LastAnalyzer;
        static int Seed = 20261006;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null, setName = null;
            bool fromSet = false, equilibrium = false, nucbase = false, noMatrix = false, trace = false;
            double thin = double.NaN;
            var lines = new List<double>();
            var props = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a == "--fromset") fromSet = true;
                else if (a == "--equilibrium") equilibrium = true;
                else if (a == "--nucbase") nucbase = true;
                else if (a == "--no-matrix") noMatrix = true;
                else if (a == "--trace") trace = true;
                else if (a.StartsWith("--thin=", StringComparison.Ordinal)) thin = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) Seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--prop=", StringComparison.Ordinal)) props.Add(a.Substring(7));
                else if (a.StartsWith("--lines=", StringComparison.Ordinal))
                {
                    foreach (string p in a.Substring(8).Split(','))
                    {
                        if (p.Trim().Length > 0) lines.Add(double.Parse(p.Trim(), CultureInfo.InvariantCulture));
                    }
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrum == null)
            {
                Console.Error.WriteLine("нужен --spectrum=");
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
            if (!double.IsNaN(thin)) rd = Thin(rd, thin, nuclides);
            if (noMatrix && rd.Efficiency != null) rd.Efficiency.UseResponseMatrix = false;

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            if (nucbase) options.DbLookups = true;
            if (equilibrium) options.ChainEquilibrium = true;
            if (fromSet) options.FromSet = true;
            if (trace)
            {
                FsaAnalyzer.ZeroTraceSink = line => Console.WriteLine("[нуль] " + line);
            }

            FsaAnalysisSession.ProbeAnalyzerHook = analyzer =>
            {
                LastAnalyzer = analyzer;
                foreach (string kv in props)
                {
                    int eq = kv.IndexOf('=');
                    PropertyInfo pi = typeof(FsaAnalyzer).GetProperty(kv.Substring(0, eq));
                    if (pi == null) throw new ArgumentException("свойства нет: " + kv);
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
            if (!done.WaitOne(TimeSpan.FromSeconds(600)) || session.Result == null || LastAnalyzer == null)
            {
                Console.WriteLine("разбор не завершился");
                return 3;
            }

            FsaResult result = session.Result;
            FsaAnalyzer an = LastAnalyzer;
            Console.WriteLine("матрица {0}; усиление {1}; нуль {2} кан.; опор {3}; adcZeroKev {4}; adcScale {5}",
                              result.ResponseMatrixUsed, F(result.Gain, "F6"), F(result.OffsetChannels, "F3"),
                              result.ScaleAnchorsUsed, F(Field(an, "adcZeroKev"), "F3"), F(Field(an, "adcScale"), "F5"));
            foreach (FsaComponentResult row in result.Components)
            {
                if (row.Kind != FsaComponentKind.Nuisance)
                {
                    Console.WriteLine("строка {0,-12} {1,12} имп/с  z {2}", row.Name, F(row.CountRate, "F3"), F(row.Z, "F1"));
                }
            }

            Console.WriteLine("χ²/ndf {0}; χ²/ndf Пуассона {1}", F(result.Chi2Ndf, "F4"), F(result.Chi2NdfPoisson, "F4"));
            Console.WriteLine("привязка: {0}", result.AnchorNote);
            Console.WriteLine("ход привязки: {0}", an.ScaleAnchorStats);
            foreach (FsaScaleAnchor anchor in result.ScaleAnchors)
            {
                Console.WriteLine("опора {0} {1}: модель {2} кэВ, измерено {3} кэВ, сдвиг {4}, свет {5}, доля {6}, z {7}, взята {8}{9}",
                                  anchor.Component, F(anchor.LineKev, "F1"), F(anchor.ModelKev, "F1"), F(anchor.MeasuredKev, "F1"),
                                  F(anchor.ShiftKev, "F2"), F(anchor.LightShiftKev, "F2"), F(anchor.PeakShare, "F3"), F(anchor.Z, "F1"), anchor.Used,
                                  string.IsNullOrEmpty(anchor.Refusal) ? "" : " (" + anchor.Refusal + ")");
            }

            if (lines.Count > 0)
            {
                EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
                int channels = rd.EnergySpectrum.NumberOfChannels;
                MethodInfo drift = typeof(FsaAnalyzer).GetMethod("DriftPosition", BindingFlags.Instance | BindingFlags.NonPublic);
                Console.WriteLine("{0,9} {1,10} {2,10}", "E, кэВ", "кан.файла", "кан.модели");
                foreach (double e in lines)
                {
                    double fileCh = cal.EnergyToChannel(e, channels);
                    double pos = an.LinePositionChannel(cal, e, channels);
                    double modelCh = (double)drift.Invoke(an, new object[] { pos, result.Gain, result.OffsetChannels });
                    Console.WriteLine("{0,9} {1,10} {2,10}", F(e, "F1"), F(fileCh, "F1"), F(modelCh, "F1"));
                }
            }

            return 0;
        }

        static double Field(FsaAnalyzer an, string name)
        {
            FieldInfo f = typeof(FsaAnalyzer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return f != null && f.GetValue(an) is double d ? d : double.NaN;
        }

        static string F(double v, string f)
        {
            return v.ToString(f, CultureInfo.InvariantCulture);
        }

        /// <summary>Биномиальное прореживание — тем же кодом и зерном, что <c>FsaBqProbe.Thin</c>.</summary>
        static ResultData Thin(ResultData rd, double p, NuclideDefinitionManager nuclides)
        {
            ResultData copy = rd.Clone();
            EnergySpectrum s = rd.EnergySpectrum.Clone();
            var rng = new Random(Seed);
            long total = 0;
            for (int i = 0; i < s.Spectrum.Length; i++)
            {
                int n = s.Spectrum[i];
                int k;
                if (n <= 0) k = 0;
                else if (n * p > 30.0 && n * (1.0 - p) > 30.0)
                {
                    double mean = n * p, sd = Math.Sqrt(n * p * (1.0 - p));
                    double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
                    double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                    k = (int)Math.Round(mean + sd * z);
                    if (k < 0) k = 0; if (k > n) k = n;
                }
                else
                {
                    k = 0;
                    for (int j = 0; j < n; j++) if (rng.NextDouble() < p) k++;
                }
                s.Spectrum[i] = k;
                total += k;
            }
            s.TotalPulseCount = total;
            s.ValidPulseCount = total;
            s.MeasurementTime = rd.EnergySpectrum.MeasurementTime * p;
            s.LiveTime = rd.EnergySpectrum.LiveTime * p;
            copy.EnergySpectrum = s;
            copy.DetectedPeaks = new PeakDetector().DetectPeak(copy, BackgroundMode.Invisible, SmoothingMethod.None,
                                                               nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return copy;
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
