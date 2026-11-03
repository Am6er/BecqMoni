using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER282`, полоса `a282b-A`, 03.11.2026) ПРИЁМКА СЦЕНЫ NANO 3 НА СПЕКТРЕ KNO₃: активность K-40
    /// по FSA тем же путём, что окно Measurement Result (<see cref="FsaAnalysisSession"/> →
    /// <see cref="FsaMeasurementResult.Build"/> → <see cref="MeasurementResultManager.Translate"/>, как
    /// `Rc110MarinelliFsaA269`), против ожидания Amber (масса × удельная активность).
    ///
    /// Проба кладётся рядом с портативной сборкой и КОПИЕЙ конфигурации человека (`config\` у exe):
    /// библиотека и сеты — её. Конфига прибора Nano 3 в ней нет, поэтому спектру ставится:
    ///   * спектр и фон — дверью «Импорт Atom Spectra» (`DocumentManager.ImportDocumentAtomSpectra`),
    ///     фон — `BackgroundEnergySpectrum` спектра (вычитается временем, как в окне);
    ///   * кривая — `EfficiencyCalculation.Run` по геометрии `--geom=` (как кнопка «Посчитать»), guid
    ///     постоянный от имени, матрица `--matrix=` копируется в склад копии конфига под этим guid;
    ///   * FWHM — степенная модель `a·ch^p` с показателем `--fwhm-p=` (умолчание 0.569 — группа ASN16
    ///     корпуса: «Нано3 - как ASN16, только корпус пластиковый, внутренности те же»), посаженная на
    ///     ширину K-40 1460.8 кэВ `--fwhm1461-kev=` (мерится на самом спектре; ширину разбор уточняет
    ///     по опорам сам);
    ///   * κ тракта — 0 (своего прибора нет); масса — `--mass-kg=`.
    ///
    ///   Nano3KnoFsaA282 --spectrum=&lt;txt&gt; --bg=&lt;txt&gt; --geom=&lt;.in&gt; --matrix=&lt;.rmx&gt; --mass-kg=0.3
    ///        [--fwhm1461-kev=70.9] [--fwhm-p=0.569] | [--fwhm-sqrt0=5.01@1472.1]
    ///        [--expect-bq-kg=11000:11500] [--curve-n=400000]
    ///        [--nucbase] [--no-matrix] [--timeout=600]
    /// Коды: 0 — посчитано; 2 — ключи/файлы; 3 — разбор не завершился.
    /// </summary>
    static class Nano3KnoFsaA282
    {
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        static FsaAnalyzer LastAnalyzer;

        [STAThread]
        static int Main(string[] args)
        {
            FsaTuningReport.Snapshot();
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null, bg = null, geomPath = null, matrixPath = null;
            double massKg = double.NaN, fwhm1461 = 70.9, fwhmP = 0.569, expectLo = double.NaN, expectHi = double.NaN;
            double sqrt0Pct = double.NaN, sqrt0FileKev = 1472.1;
            int curveN = 400000, timeoutS = 600;
            bool nucbase = false, noMatrix = false;
            var props = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--bg=", StringComparison.Ordinal)) bg = a.Substring(5);
                else if (a.StartsWith("--geom=", StringComparison.Ordinal)) geomPath = a.Substring(7);
                else if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
                else if (a.StartsWith("--mass-kg=", StringComparison.Ordinal)) massKg = double.Parse(a.Substring(10), Ci);
                else if (a.StartsWith("--fwhm1461-kev=", StringComparison.Ordinal)) fwhm1461 = double.Parse(a.Substring(15), Ci);
                else if (a.StartsWith("--fwhm-p=", StringComparison.Ordinal)) fwhmP = double.Parse(a.Substring(9), Ci);
                else if (a.StartsWith("--fwhm-sqrt0=", StringComparison.Ordinal))
                {
                    // <процент>@<кэВ шкалы файла>: SimpleSqrt через ноль по одной опоре
                    string[] p = a.Substring(13).Split('@');
                    sqrt0Pct = double.Parse(p[0], Ci);
                    sqrt0FileKev = double.Parse(p[1], Ci);
                }
                else if (a.StartsWith("--curve-n=", StringComparison.Ordinal)) curveN = int.Parse(a.Substring(10), Ci);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), Ci);
                else if (a.StartsWith("--expect-bq-kg=", StringComparison.Ordinal))
                {
                    string[] p = a.Substring(15).Split(':');
                    expectLo = double.Parse(p[0], Ci);
                    expectHi = double.Parse(p[p.Length - 1], Ci);
                }
                else if (a.StartsWith("--prop=", StringComparison.Ordinal)) props.Add(a.Substring(7));
                else if (a.StartsWith("--fit-floor-kev=", StringComparison.Ordinal))
                {
                    // пол полосы фита числом (`A302`, ветвь Fixed) — опыт, не умолчание (как у `Rc110MarinelliFsaA269`)
                    FsaBand.DefaultFitFloor = FsaFitFloor.Fixed;
                    FsaBand.DefaultFitFloorKev = double.Parse(a.Substring(16), Ci);
                }
                else if (a == "--nucbase") nucbase = true;
                else if (a == "--no-matrix") noMatrix = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrum == null || bg == null || geomPath == null || (matrixPath == null && !noMatrix) || double.IsNaN(massKg))
            {
                Console.Error.WriteLine("нужны --spectrum=, --bg=, --geom=, --matrix= (или --no-matrix) и --mass-kg=");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Console.WriteLine("SETUP\tсет {0}; склад матриц {1}", nuclides.ActiveSet != null ? nuclides.ActiveSet.Name : "(нет)",
                              ResponseMatrixStore.Directory);

            // --- спектр и фон: дверь «Импорт Atom Spectra» ---
            DocumentManager dm = DocumentManager.GetInstance();
            var doc = new DocEnergySpectrum();
            dm.ImportDocumentAtomSpectra(doc, spectrum);
            ResultData rd = doc.ActiveResultData;
            var bgDoc = new DocEnergySpectrum();
            dm.ImportDocumentAtomSpectra(bgDoc, bg);
            EnergySpectrum bgs = bgDoc.ActiveResultData.EnergySpectrum;
            rd.BackgroundEnergySpectrum = bgs;
            EnergySpectrum s = rd.EnergySpectrum;
            Console.WriteLine("SETUP\tспектр: каналов {0}, T {1} с, отсчётов {2}; фон: T {3} с, отсчётов {4}; прибор документа «{5}»",
                              s.NumberOfChannels, F(s.MeasurementTime, "F1"), s.TotalPulseCount, F(bgs.MeasurementTime, "F1"),
                              bgs.TotalPulseCount, rd.DeviceConfig != null ? rd.DeviceConfig.Name : "нет");

            // --- κ тракта, масса, FWHM ---
            rd.TractCurvature = 0.0;
            rd.SampleInfo.Weight = massKg;
            EnergyCalibration cal = s.EnergyCalibration;
            double ch1461 = cal.EnergyToChannel(1460.82, s.NumberOfChannels);
            double kevPerCh = cal.ChannelToEnergy(ch1461 + 0.5) - cal.ChannelToEnergy(ch1461 - 0.5);
            FwhmCalibration fw;
            string fwNote;
            if (!double.IsNaN(sqrt0Pct))
            {
                // (решение Amber 03.11.2026, дословно «Из K-40 по статистике (Рекомендую)») SimpleSqrt через
                // ноль — FWHM(ch) = √(k·ch) — по одной опоре: доля ширины от положения пика K-40 в шкале файла.
                double chA = cal.EnergyToChannel(sqrt0FileKev, s.NumberOfChannels);
                double perCh = cal.ChannelToEnergy(chA + 0.5) - cal.ChannelToEnergy(chA - 0.5);
                double fwhmCh = sqrt0Pct / 100.0 * sqrt0FileKev / perCh;
                var simple = new SimpleSqrtFwhmCalibration();
                simple.Coefficients = new[] { 0.0, fwhmCh * fwhmCh / chA };
                fw = simple;
                fwNote = string.Format(Ci, "√(k·ch) через ноль, {0} % у пика K-40 (шкала файла {1} кэВ, кан. {2})",
                                       F(sqrt0Pct, "F2"), F(sqrt0FileKev, "F1"), F(chA, "F1"));
            }
            else
            {
                var power = new PowerFwhmCalibration();
                power.Coefficients = new[] { (fwhm1461 / kevPerCh) / Math.Pow(ch1461, fwhmP), fwhmP };
                fw = power;
                fwNote = string.Format(Ci, "a·ch^p, p {0}, на 1460.8 кэВ {1} кэВ (кан. {2})", F(fwhmP, "F3"), F(fwhm1461, "F1"), F(ch1461, "F1"));
            }

            rd.FwhmCalibration = fw;
            double ch662 = cal.EnergyToChannel(661.66, s.NumberOfChannels);
            double kev662 = fw.ChannelToFwhm(ch662) * (cal.ChannelToEnergy(ch662 + 0.5) - cal.ChannelToEnergy(ch662 - 0.5));
            double kev1461 = fw.ChannelToFwhm(ch1461) * kevPerCh;
            Console.WriteLine("SETUP\tFWHM: {0}; на 661.7 кэВ шкалы файла {1} кэВ = {2} %; на 1460.8 — {3} кэВ = {4} %",
                              fwNote, F(kev662, "F1"), F(100.0 * kev662 / 661.66, "F2"), F(kev1461, "F1"), F(100.0 * kev1461 / 1460.82, "F2"));

            // --- кривая и матрица ---
            GeometryModel geometry = GeometryModel.Load(geomPath);
            string guid = StableGuid("AMBER282:" + Path.GetFileNameWithoutExtension(geomPath));
            var options = new EfficiencyCalculationOptions { Histories = curveN };
            EfficiencyFitResult fit = EfficiencyCalculation.Run(geometry, options, null, null, new ResponseMatrixOptions(), null);
            if (!string.IsNullOrEmpty(fit.Error))
            {
                Console.WriteLine("РАСЧЁТ КРИВОЙ НЕ ПОШЁЛ: " + fit.Error);
                return 2;
            }

            var eff = new EfficiencyConfigData(Path.GetFileNameWithoutExtension(geomPath))
            {
                Guid = guid,
                Origin = EfficiencyOrigin.Simulation,
                Geometry = geometry,
                Curve = fit.Curve,
                ComputeStamp = fit.ComputeStamp ?? "",
                GeometryFingerprint = fit.GeometryFingerprint ?? "",
                UseResponseMatrix = !noMatrix,
            };
            if (!noMatrix)
            {
                Directory.CreateDirectory(ResponseMatrixStore.Directory);
                File.Copy(matrixPath, ResponseMatrixStore.PathOf(guid), true);
            }

            rd.Efficiency = eff;
            Console.WriteLine("SETUP\tкривая: {0} точек, клеймо «{1}»; ε(1460.8) по кривой {2}; матрица {3}", fit.Curve.Count,
                              eff.ComputeStamp, F(CurveAt(fit.Curve, 1460.82), "E4"),
                              noMatrix ? "СНЯТА (--no-matrix)" : guid + ".rmx");

            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            foreach (Peak p in rd.DetectedPeaks)
            {
                Console.WriteLine("пик\t{0,8} кэВ кан. {1,5} SNR {2,6} {3}", F(p.Energy, "F2"), p.Channel, F(p.SNR, "F1"),
                                  p.Nuclide != null ? p.Nuclide.Name : "(неизвестный)");
            }

            FsaCalculationOptions fo = FsaCalculationOptions.Of(rd);
            if (nucbase) fo.DbLookups = true;
            Console.WriteLine("SETUP\tсостав: {0}; равновесие {1}; обратное рассеяние {2}",
                              fo.FromSet ? "из сета" : (fo.DbLookups ? "Из NucBase" : "по подписям пиков"), fo.ChainEquilibrium, fo.Backscatter);
            FsaAnalysisSession.ProbeAnalyzerHook = analyzer =>
            {
                LastAnalyzer = analyzer;
                foreach (string kv in props)
                {
                    int eq = kv.IndexOf('=');
                    System.Reflection.PropertyInfo pi = typeof(FsaAnalyzer).GetProperty(kv.Substring(0, eq));
                    if (pi == null) throw new ArgumentException("нет свойства анализатора: " + kv);
                    string text = kv.Substring(eq + 1);
                    object value = pi.PropertyType == typeof(bool) ? (object)bool.Parse(text)
                        : pi.PropertyType == typeof(double) ? (object)double.Parse(text, Ci)
                        : pi.PropertyType == typeof(int) ? (object)int.Parse(text, Ci)
                        : text;
                    pi.SetValue(analyzer, value);
                    Console.WriteLine("SETUP\tключ анализатора: {0} = {1}", pi.Name, value);
                }

                FsaTuningReport.Print(analyzer, "a282");
            };
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (o, e) => done.Set();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            session.EnsureUpToDate(rd, true, fo);
            bool finished = done.WaitOne(TimeSpan.FromSeconds(timeoutS));
            FsaResult result = session.Result;
            if (!finished || result == null || LastAnalyzer == null)
            {
                Console.WriteLine("разбор не завершился (статус {0})", session.Status);
                return 3;
            }

            Console.WriteLine("ИТОГ\tразбор {0} мс; матрица {1}; кривая {2}; χ²/ndf {3}; χ²/ndf Пуассона {4}; обратное рассеяние формулой {5}",
                              sw.ElapsedMilliseconds, result.ResponseMatrixUsed ? "применена" : "нет",
                              result.EfficiencyUsed ? "учтена" : "нет", F(result.Chi2Ndf, "F3"), F(result.Chi2NdfPoisson, "F3"),
                              LastAnalyzer.BackscatterWithMatrix ? "ДА" : "нет (защита в матрице)");
            Console.WriteLine("ИТОГ\tшкала: усиление {0}; нуль {1} кэВ; опор {2}; ширина ×{3} по {4} опорам; фон {5}; рамка {6}…{7} кан.",
                              F(result.Gain, "0.######"), F(result.AnchorOffsetKev, "+0.00;-0.00"), result.ScaleAnchorsUsed,
                              F(result.WidthScale, "0.####"), result.WidthAnchorsUsed,
                              result.BackgroundUsed ? "вычтен ×" + F(result.BackgroundScale, "0.####") : "НЕТ",
                              result.FirstChannel, result.LastChannel);
            Console.WriteLine("ИТОГ\tпривязка: {0}; откат {1}", result.AnchorNote, result.AnchorRolledBack ? "ДА" : "нет");
            Console.WriteLine("ИТОГ\tбета пробы: ключ {0}, взята {1}, раздел {2}, отказ раздела {3}", result.SampleBetaKeyOn,
                              result.SampleBetaUsed, LastAnalyzer.SampleBetaSection != null ? "есть" : "нет", LastAnalyzer.SampleBetaRefusal);
            Console.WriteLine("ИТОГ\tграница разбора: настройка FSA {0} кэВ; первый канал фита {1} кэВ",
                              F(result.FitLowerBoundKev, "F1"), F(result.FitStartKev, "F2"));
            Console.WriteLine("ИТОГ\tход привязки: {0}", LastAnalyzer.ScaleAnchorStats);
            foreach (FsaScaleAnchor anchor in result.ScaleAnchors)
            {
                Console.WriteLine("опора\t{0} {1} кэВ: модель {2}, измерено {3}, сдвиг {4} кэВ, z {5}, взята {6}{7}",
                                  anchor.Component, F(anchor.LineKev, "F1"), F(anchor.ModelKev, "F1"), F(anchor.MeasuredKev, "F1"),
                                  F(anchor.ShiftKev, "+0.00;-0.00"), F(anchor.Z, "F1"), anchor.Used,
                                  string.IsNullOrEmpty(anchor.Refusal) ? "" : " (" + anchor.Refusal + ")");
            }

            if (LastAnalyzer.LastBandImage != null)
            {
                // образы при найденных амплитудах против данных минус фон по полосам — где не сходится модель
                double[] edges = { 20, 44, 64, 100, 150, 200, 300, 450, 700, 1000, 1200, 1350, 1600, 2000, 3000 };
                for (int r = 0; r + 1 < edges.Length; r++)
                {
                    double si = 0.0, sr = 0.0, sb = 0.0, model = 0.0;
                    for (int i = 0; i < LastAnalyzer.LastBandImage.Length; i++)
                    {
                        double e = cal.ChannelToEnergy(i);
                        if (e < edges[r] || e >= edges[r + 1]) continue;
                        si += LastAnalyzer.LastBandImage[i];
                        sr += s.Spectrum[i];
                        if (result.Background != null && i < result.Background.Length) sb += result.Background[i];
                        if (result.Model != null && i < result.Model.Length) model += result.Model[i];
                    }

                    double net = sr - sb;
                    Console.WriteLine("полоса\t{0,5}…{1,5} кэВ: спектр {2,9} фон {3,9} спектр−фон {4,9} модель {5,9} модель/(спектр−фон) {6}  (z {7})",
                                      F(edges[r], "F0"), F(edges[r + 1], "F0"), F(sr, "F0"), F(sb, "F0"), F(net, "F0"), F(model, "F0"),
                                      net > 0.0 ? F(model / net, "F3") : "—",
                                      F((net - model) / Math.Sqrt(Math.Max(1.0, sr + sb)), "F1"));
                }
            }

            foreach (FsaComponentResult c in result.Components.OrderByDescending(c => c.SharePercent))
            {
                Console.WriteLine("строка\t{0,-24} {1,-12} доля {2,8} %  пик-доля {3,8} %  {4,12} 1/с  z {5,8}",
                                  c.Name, c.Kind, F(c.SharePercent, "F3"), F(c.PeakSharePercent, "F3"), F(c.CountRate, "0.#####"),
                                  F(c.Z, "F1"));
            }

            FsaMeasurementResult.Summary summary;
            MeasurementResultCollection built = FsaMeasurementResult.Build(rd, result, nuclides.NuclideDefinitions,
                                                                           FsaMeasurementResult.Texts.Russian, out summary);
            Console.WriteLine("ИТОГ\t{0}; {1}", summary.Describe(null), summary.Details(null));
            if (built != null)
            {
                MeasurementResultCollection bq = new MeasurementResultManager().Translate(built, ResultTranslation.Becquerels);
                foreach (MeasurementResult row in bq.ResultList)
                {
                    string name = row.Line.Name;
                    string extra = "";
                    if (row.IsValid && name.IndexOf("K-40", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        double perKg = row.ResultValue / massKg, perKgErr = row.ResultError / massKg;
                        extra = string.Format(Ci, "  = {0} ± {1} Бк/кг", F(perKg, "F0"), F(perKgErr, "F0"));
                        if (!double.IsNaN(expectLo))
                        {
                            double mid = 0.5 * (expectLo + expectHi);
                            extra += string.Format(Ci, "; ожидание {0}–{1} Бк/кг: FSA/ожидание {2} ({3}…{4})", F(expectLo, "F0"),
                                                   F(expectHi, "F0"), F(perKg / mid, "F4"), F(perKg / expectHi, "F4"), F(perKg / expectLo, "F4"));
                        }
                    }

                    Console.WriteLine("Бк\t{0,-14} {1} ± {2} Бк (a# {3}){4}", name,
                                      row.IsValid ? F(row.ResultValue, "F2") : row.StatusText,
                                      row.IsValid ? F(row.ResultError, "F2") : "", row.IsValid ? F(row.MDA, "F2") : "", extra);
                }
            }

            return 0;
        }

        static double CurveAt(List<ROIEfficiencyData> curve, double e)
        {
            for (int i = 0; i + 1 < curve.Count; i++)
            {
                if (curve[i].Energy <= e && curve[i + 1].Energy >= e)
                {
                    double t = (Math.Log(e) - Math.Log(curve[i].Energy)) / (Math.Log(curve[i + 1].Energy) - Math.Log(curve[i].Energy));
                    return Math.Exp(Math.Log(curve[i].Efficiency) + t * (Math.Log(curve[i + 1].Efficiency) - Math.Log(curve[i].Efficiency)));
                }
            }

            return double.NaN;
        }

        static string StableGuid(string key)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(key))).ToString();
            }
        }

        static string F(double v, string f)
        {
            return double.IsNaN(v) ? "NaN" : v.ToString(f, Ci);
        }
    }
}
