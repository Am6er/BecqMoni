using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (`AMBER269`, полоса rc110mar 02.11.2026) АКТИВНОСТЬ РЯДА ПО FSA НА СПЕКТРЕ С ИЗВЕСТНОЙ АКТИВНОСТЬЮ —
    /// в плечах привязки шкалы, тем же путём, что окно Measurement Result: <see cref="FsaAnalysisSession"/> →
    /// <see cref="FsaMeasurementResult.Build"/> → <see cref="MeasurementResultManager.Translate"/> (как
    /// <c>FsaBqProbe</c>), плюс ключи анализатора отражением (<c>--prop=</c>, как <c>FsaLinePosProbeP255</c>):
    /// плечо (А2) — умолчание сборки, (А2)+(Д-амп) — <c>--prop=AnchorShiftFreeAmplitude=true</c>.
    ///
    /// Кривую эффективности спектру ставит, как выпадающий список панели измерения: копия кривой прибора по
    /// имени (<c>--curve=</c>; <see cref="DCControlPanel.EfficiencyFromItem"/> берёт <c>Copy()</c>).
    /// Проба кладётся РЯДОМ с портативной сборкой и КОПИЕЙ конфигурации человека (`config\` у exe):
    /// прибор, κ тракта, библиотека, сеты — её.
    ///
    /// Печатает: прибор, кривую, матрицу; пики поиска; χ²/ndf и χ²/ndf Пуассона; привязку (опоры, усиление,
    /// нуль, откат, ход проходов, таблицу опор); ширину и фон; строки разбора (доли, скорости, z, связка);
    /// беккерели окна и отношение к паспорту с σ паспорта; положения линий <c>--lines=</c> (канал по калибровке
    /// файла, канал модели, вершина модели, вершина данных).
    ///
    /// Ключи: --spectrum=&lt;файл&gt;; --curve=&lt;имя кривой прибора&gt;; --set=&lt;сет&gt;; --nucbase; --labels
    /// (состав по подписям пиков, «Из NucBase» выкл); --equilibrium | --no-equilibrium; --no-matrix;
    /// --prop=&lt;свойство FsaAnalyzer&gt;=&lt;значение&gt; (несколько); --passport=&lt;имя&gt;:&lt;Бк&gt;:&lt;σ Бк&gt;;
    /// --lines=238.6,583.2,…; --arm=&lt;метка плеча&gt;; --anchor-passes; --timeout=300;
    /// --fit-floor-kev=&lt;кэВ&gt; (пол полосы фита числом, <see cref="FsaFitFloor.Fixed"/>; опыт, не умолчание);
    /// --dump=lo,hi[,шаг] — каналы: данные минус фон, модель, pull.
    /// Коды: 0 — посчитано; 2 — ключи/файлы; 3 — разбор не завершился.
    /// </summary>
    static class Rc110MarinelliFsaA269
    {
        static FsaAnalyzer LastAnalyzer;

        [STAThread]
        static int Main(string[] args)
        {
            FsaTuningReport.Snapshot();
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrum = null, setName = null, curveName = null, arm = null;
            bool nucbase = false, labels = false, noMatrix = false;
            bool? equilibrium = null;
            int timeoutS = 300;
            var props = new List<string>();
            var lines = new List<double>();
            string passName = null;
            double passBq = double.NaN, passSigma = double.NaN;
            int dumpLo = -1, dumpHi = -1, dumpStep = 1;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--curve=", StringComparison.Ordinal)) curveName = a.Substring(8);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--arm=", StringComparison.Ordinal)) arm = a.Substring(6);
                else if (a == "--nucbase") nucbase = true;
                else if (a == "--labels") labels = true;
                else if (a == "--equilibrium") equilibrium = true;
                else if (a == "--no-equilibrium") equilibrium = false;
                else if (a == "--no-matrix") noMatrix = true;
                else if (a == "--anchor-passes") FsaAnalyzer.AnchorPassSink = line => Console.WriteLine("[опоры] " + line);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--prop=", StringComparison.Ordinal)) props.Add(a.Substring(7));
                else if (a.StartsWith("--dump=", StringComparison.Ordinal))
                {
                    string[] p = a.Substring(7).Split(',');
                    dumpLo = int.Parse(p[0], CultureInfo.InvariantCulture);
                    dumpHi = int.Parse(p[1], CultureInfo.InvariantCulture);
                    if (p.Length > 2) dumpStep = int.Parse(p[2], CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--fit-floor-kev=", StringComparison.Ordinal))
                {
                    // пол полосы фита числом (`A302`, ветвь Fixed) — как ключ `--fit-floor=` корпусной пробы
                    FsaBand.DefaultFitFloor = FsaFitFloor.Fixed;
                    FsaBand.DefaultFitFloorKev = double.Parse(a.Substring(16), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--passport=", StringComparison.Ordinal))
                {
                    string[] p = a.Substring(11).Split(':');
                    passName = p[0];
                    passBq = double.Parse(p[1], CultureInfo.InvariantCulture);
                    if (p.Length > 2) passSigma = double.Parse(p[2], CultureInfo.InvariantCulture);
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
            if (spectrum == null || (nucbase && labels))
            {
                Console.Error.WriteLine("нужен --spectrum=; --nucbase и --labels вместе нельзя");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            if (setName != null)
            {
                NuclideSet set = nuclides.NuclideSets != null
                    ? nuclides.NuclideSets.FirstOrDefault(s => string.Equals(s.Name, setName, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (set == null) { Console.Error.WriteLine("сета нет: " + setName); return 2; }
                nuclides.ActiveSet = set;
            }
            Console.WriteLine("SETUP\tплечо {0}; сет {1}; конфигурация {2}", arm ?? "—",
                              nuclides.ActiveSet != null ? nuclides.ActiveSet.Name : "(нет)",
                              BecquerelMonitor.EfficiencyMaker.ResponseMatrixStore.Directory);

            ResultData rd = Load(spectrum, nuclides, curveName);
            if (rd == null) return 2;
            if (noMatrix && rd.Efficiency != null) rd.Efficiency.UseResponseMatrix = false;

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            if (nucbase) options.DbLookups = true;
            if (labels) options.DbLookups = false;
            if (equilibrium.HasValue) options.ChainEquilibrium = equilibrium.Value;
            Console.WriteLine("SETUP\tсостав: {0}; равновесие {1}; суммирование {2}; наложения {3}; обратное рассеяние {4}; рентген {5}; вылет/511 {6}; матрица {7}",
                              options.FromSet ? "из сета" : (options.DbLookups ? "Из NucBase" : "по подписям пиков"),
                              options.ChainEquilibrium, options.CascadeSumming, options.PileUp, options.Backscatter,
                              options.AtomicXray, options.EscapeAndAnnihilation, noMatrix ? "СНЯТА (--no-matrix)" : "как у кривой");

            FsaAnalysisSession.ProbeAnalyzerHook = analyzer =>
            {
                LastAnalyzer = analyzer;
                foreach (string kv in props)
                {
                    int eq = kv.IndexOf('=');
                    PropertyInfo pi = typeof(FsaAnalyzer).GetProperty(kv.Substring(0, eq));
                    if (pi == null) throw new ArgumentException("нет свойства анализатора: " + kv);
                    string text = kv.Substring(eq + 1);
                    object value = pi.PropertyType == typeof(bool) ? (object)bool.Parse(text)
                        : pi.PropertyType == typeof(double) ? (object)double.Parse(text, CultureInfo.InvariantCulture)
                        : pi.PropertyType == typeof(int) ? (object)int.Parse(text, CultureInfo.InvariantCulture)
                        : text;
                    pi.SetValue(analyzer, value);
                    Console.WriteLine("SETUP\tключ анализатора: {0} = {1}", pi.Name, value);
                }
                FsaTuningReport.Print(analyzer, arm);
            };
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
            bool finished = done.WaitOne(TimeSpan.FromSeconds(timeoutS));
            FsaResult result = session.Result;
            if (!finished || result == null || LastAnalyzer == null)
            {
                Console.WriteLine("разбор не завершился (статус {0})", session.Status);
                return 3;
            }
            FsaAnalyzer an = LastAnalyzer;
            Console.WriteLine("ИТОГ\tразбор {0} мс; матрица {1}; кривая {2}; χ²/ndf {3}; χ²/ndf Пуассона {4}; σ-надувка {5}; невязка модели {6}",
                              sw.ElapsedMilliseconds, result.ResponseMatrixUsed ? "применена" : "нет",
                              result.EfficiencyUsed ? "учтена" : "нет", F(result.Chi2Ndf, "F3"), F(result.Chi2NdfPoisson, "F3"),
                              F(result.SigmaInflation, "F3"), F(result.ModelResidual, "0.###"));
            Console.WriteLine("ИТОГ\tшкала: усиление {0} ({1} %); нуль {2} кан. ({3} кэВ); опор {4}; ширина ×{5} по {6} опорам; фон {7}; рамка {8}…{9} кан.",
                              F(result.Gain, "0.######"), F(100.0 * (result.Gain - 1.0), "+0.00;-0.00"),
                              F(result.OffsetChannels, "+0.000;-0.000"), F(result.AnchorOffsetKev, "+0.00;-0.00"),
                              result.ScaleAnchorsUsed, F(result.WidthScale, "0.####"), result.WidthAnchorsUsed,
                              result.BackgroundUsed ? "вычтен ×" + F(result.BackgroundScale, "0.####") : "НЕТ",
                              result.FirstChannel, result.LastChannel);
            Console.WriteLine("ИТОГ\tпривязка: {0}", result.AnchorNote);
            Console.WriteLine("ИТОГ\tоткат: {0}; отменённый разбор — опор {1}, усиление {2}, χ²/ndf Пуассона {3}",
                              result.AnchorRolledBack ? "ДА" : "нет", result.AnchorRollbackAnchors,
                              F(result.AnchorRollbackGain, "0.######"), F(result.AnchorRollbackChi2NdfPoisson, "F3"));
            // (`AMBER269`, 02.11.2026) два предупреждения по полосе — то, что окно отчёта
            // покажет красными строками (числа — для сверки с ним)
            Console.WriteLine("ИТОГ\tпредупреждения: фон больше спектра — {0}; модель не описывает низ — {1}",
                              Warn(result.BackgroundOversubtracted, result.BackgroundOversubtractedWorstZ),
                              Warn(result.ImagesAboveSpectrum, result.ImagesAboveSpectrumWorstZ));
            // (`AMBER269`) нижняя граница разбора — с какой энергии шёл разбор и откуда она
            Console.WriteLine("ИТОГ\tграница разбора: настройка FSA {0} кэВ; первый канал фита {1} кэВ",
                              F(result.FitLowerBoundKev, "F1"), F(result.FitStartKev, "F2"));
            Console.WriteLine("ИТОГ\tнож: отвязано {0} из {1} бинов (образов {2}, целиком {3})",
                              result.KnifeUntiedBins, result.KnifeBinsBelow, result.KnifeImages, result.KnifeImagesWhole);
            {
                // (критик critmar_fsa, перенесено полосой fsalow) низ образа по строкам:
                // привязанная лента и отвязанный хвост 5…100 кэВ
                EnergyCalibration calLow = rd.EnergySpectrum.EnergyCalibration;
                foreach (FsaComponentResult c in result.Components)
                {
                    double tied = 0.0, tail = 0.0;
                    for (int i = 0; c.Curve != null && i < c.Curve.Length; i++)
                    {
                        double e = calLow.ChannelToEnergy(i);
                        if (e < 5.0 || e >= 100.0) continue;
                        tied += c.Curve[i];
                        if (c.TailCurve != null && i < c.TailCurve.Length) tail += c.TailCurve[i];
                    }
                    Console.WriteLine("низ\t{0,-12} лента 5…100 кэВ {1,10} хвост {2,10}", c.Name, F(tied, "F0"), F(tail, "F0"));
                }
            }
            if (an.LastBandImage != null)
            {
                // (`AMBER269`) образы при найденных амплитудах против ВСЕГО измеренного и фона по полосам
                EnergyCalibration calBand = rd.EnergySpectrum.EnergyCalibration;
                double[] edges = { 5, 13, 20, 30, 44, 64, 100, 150, 300, 1000, 3000 };
                for (int r = 0; r + 1 < edges.Length; r++)
                {
                    double si = 0.0, sr = 0.0, sb = 0.0;
                    for (int i = 0; i < an.LastBandImage.Length; i++)
                    {
                        double e = calBand.ChannelToEnergy(i);
                        if (e < edges[r] || e >= edges[r + 1]) continue;
                        si += an.LastBandImage[i];
                        sr += rd.EnergySpectrum.Spectrum[i];
                        if (result.Background != null && i < result.Background.Length) sb += result.Background[i];
                    }
                    Console.WriteLine("полоса\t{0,5}…{1,5} кэВ: образы {2,10} весь спектр {3,10} фон {4,10} образы/спектр {5}",
                                      F(edges[r], "F0"), F(edges[r + 1], "F0"), F(si, "F0"), F(sr, "F0"), F(sb, "F0"),
                                      sr > 0.0 ? F(si / sr, "F3") : "—");
                }
                for (int i = 0; i < 40 && i < an.LastBandImage.Length; i++)
                {
                    Console.WriteLine("канал-низ\t{0,4} {1,7} кэВ образы {2,9} спектр {3,7} фон {4,9} ПШПВ {5} кан.", i,
                                      F(calBand.ChannelToEnergy(i), "F1"), F(an.LastBandImage[i], "F0"), rd.EnergySpectrum.Spectrum[i],
                                      result.Background != null ? F(result.Background[i], "F0") : "—",
                                      rd.FwhmCalibration != null ? F(result.WidthScale * rd.FwhmCalibration.ChannelToFwhm(i), "F2") : "—");
                }
            }
            Console.WriteLine("ИТОГ\tход привязки: {0}", an.ScaleAnchorStats);
            Console.WriteLine("ИТОГ\tкарта света: {0}; κ спектра {1}", State(an),
                              rd.TractCurvature.ToString("R", CultureInfo.InvariantCulture));
            foreach (FsaScaleAnchor anchor in result.ScaleAnchors)
            {
                Console.WriteLine("опора\t{0} {1} кэВ: модель {2}, измерено {3}, сдвиг {4} кэВ, z {5}, взята {6}{7}",
                                  anchor.Component, F(anchor.LineKev, "F1"), F(anchor.ModelKev, "F1"), F(anchor.MeasuredKev, "F1"),
                                  F(anchor.ShiftKev, "+0.00;-0.00"), F(anchor.Z, "F1"), anchor.Used,
                                  string.IsNullOrEmpty(anchor.Refusal) ? "" : " (" + anchor.Refusal + ")");
            }
            if (result.AnchorRolledBack && result.AnchorRollbackScaleAnchors != null)
            {
                foreach (FsaScaleAnchor anchor in result.AnchorRollbackScaleAnchors)
                {
                    Console.WriteLine("опора-отменённая\t{0} {1} кэВ: модель {2}, измерено {3}, сдвиг {4} кэВ, z {5}, взята {6}{7}",
                                      anchor.Component, F(anchor.LineKev, "F1"), F(anchor.ModelKev, "F1"), F(anchor.MeasuredKev, "F1"),
                                      F(anchor.ShiftKev, "+0.00;-0.00"), F(anchor.Z, "F1"), anchor.Used,
                                      string.IsNullOrEmpty(anchor.Refusal) ? "" : " (" + anchor.Refusal + ")");
                }
            }

            foreach (FsaComponentResult c in result.Components.OrderByDescending(c => c.SharePercent))
            {
                Console.WriteLine("строка\t{0,-24} {1,-12} доля {2,8} %  пик-доля {3,8} %  {4,12} 1/с  z {5,8}  ряд {6}  происхождение {7}{8}",
                                  c.Name, c.Kind, F(c.SharePercent, "F3"), F(c.PeakSharePercent, "F3"), F(c.CountRate, "0.#####"),
                                  F(c.Z, "F1"), c.ChainRoot ?? "—", c.DecayChainRoot ?? "—",
                                  string.IsNullOrEmpty(c.TiedTo) ? "" : "  привязан к " + c.TiedTo);
            }

            FsaMeasurementResult.Summary summary;
            MeasurementResultCollection built = FsaMeasurementResult.Build(rd, result, nuclides.NuclideDefinitions,
                                                                           FsaMeasurementResult.Texts.Russian, out summary);
            Console.WriteLine("ИТОГ\t{0}; {1}", summary.Describe(null), summary.Details(null));
            if (built != null)
            {
                var manager = new MeasurementResultManager();
                MeasurementResultCollection bq = manager.Translate(built, ResultTranslation.Becquerels);
                foreach (MeasurementResult row in bq.ResultList)
                {
                    string name = row.Line.Name;
                    string ratio = "";
                    if (row.IsValid && passName != null && string.Equals(name, passName, StringComparison.OrdinalIgnoreCase))
                    {
                        double r = row.ResultValue / passBq;
                        double sig = Math.Sqrt(Math.Pow(row.ResultError, 2) + (double.IsNaN(passSigma) ? 0.0 : passSigma * passSigma));
                        ratio = string.Format(CultureInfo.InvariantCulture, "  FSA/паспорт {0}; разность {1} Бк = {2} σ (σ = √(σ_FSA² + σ_пасп²) = {3} Бк)",
                                              F(r, "F4"), F(row.ResultValue - passBq, "+0.0;-0.0"),
                                              F((row.ResultValue - passBq) / sig, "+0.00;-0.00"), F(sig, "F1"));
                    }
                    Console.WriteLine("Бк\t{0,-14} {1} ± {2} Бк (a# {3}){4}", name,
                                      row.IsValid ? F(row.ResultValue, "F2") : row.StatusText,
                                      row.IsValid ? F(row.ResultError, "F2") : "",
                                      row.IsValid ? F(row.MDA, "F2") : "", ratio);
                }
            }

            if (lines.Count > 0)
            {
                EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
                int channels = rd.EnergySpectrum.NumberOfChannels;
                MethodInfo drift = typeof(FsaAnalyzer).GetMethod("DriftPosition", BindingFlags.Instance | BindingFlags.NonPublic);
                double[] data = new double[channels];
                for (int i = 0; i < channels; i++)
                {
                    data[i] = rd.EnergySpectrum.Spectrum[i]
                              - (result.Background != null && i < result.Background.Length ? result.Background[i] : 0.0);
                }
                Console.WriteLine("линия\t{0,9} {1,10} {2,10} {3,10} {4,10} {5,10}", "E, кэВ", "кан.файла", "кан.модели",
                                  "вершина М", "вершина Д", "E(Д), кэВ");
                foreach (double e in lines)
                {
                    double fileCh = cal.EnergyToChannel(e, channels);
                    double pos = an.LinePositionChannel(cal, e, channels);
                    double modelCh = (double)drift.Invoke(an, new object[] { pos, result.Gain, result.OffsetChannels });
                    double fwhm = rd.FwhmCalibration != null ? result.WidthScale * rd.FwhmCalibration.ChannelToFwhm(fileCh) : 10.0;
                    double topModel = Top(result.Model, modelCh, fwhm, channels);
                    double topData = Top(data, fileCh, fwhm, channels);
                    Console.WriteLine("линия\t{0,9} {1,10} {2,10} {3,10} {4,10} {5,10}", F(e, "F1"), F(fileCh, "F1"), F(modelCh, "F1"),
                                      F(topModel, "F1"), F(topData, "F1"), F(cal.ChannelToEnergy(topData), "F1"));
                }
            }

            if (dumpLo >= 0)
            {
                // данные минус фон, модель (вне рамки фита — продолжение модели), pull по сырому отсчёту
                EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
                int channels = rd.EnergySpectrum.NumberOfChannels;
                Console.WriteLine("канал\t{0,6} {1,9} {2,12} {3,12} {4,9}", "кан.", "E файла", "данные-фон", "модель", "pull");
                for (int i = dumpLo; i <= dumpHi && i < channels; i += dumpStep)
                {
                    double y = rd.EnergySpectrum.Spectrum[i] - (result.Background != null && i < result.Background.Length ? result.Background[i] : 0.0);
                    double m = result.Model != null && i < result.Model.Length ? result.Model[i] : double.NaN;
                    double sd = Math.Sqrt(Math.Max(1.0, rd.EnergySpectrum.Spectrum[i]));
                    Console.WriteLine("канал\t{0,6} {1,9} {2,12} {3,12} {4,9}", i, F(cal.ChannelToEnergy(i), "F1"), F(y, "F1"), F(m, "F1"), F((y - m) / sd, "F1"));
                }
            }
            return 0;
        }

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

        /// <summary>Встроенная заглушка кривой FWHM — внутренним правилом приложения (отражением).</summary>
        static bool IsStandIn(FwhmCalibration curve)
        {
            if (curve == null) return false;
            MethodInfo m = typeof(FWHMPeakDetectionMethodConfig).GetMethod(
                "IsBuiltInStandIn", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return m != null && (bool)m.Invoke(null, new object[] { curve });
        }

        static string F(double v, string f)
        {
            return double.IsNaN(v) ? "NaN" : v.ToString(f, CultureInfo.InvariantCulture);
        }

        /// <summary>(`AMBER269`) Признак по полосе словами пробы: полоса, z, отношение; без признака — наибольшая z окна.</summary>
        static string Warn(FsaBandWarning flag, double worstZ)
        {
            return flag == null
                ? "нет (наибольшая z окна " + F(worstZ, "F2") + ")"
                : string.Format(CultureInfo.InvariantCulture, "ДА: {0}–{1} кэВ, z {2}, ×{3}, полос {4} (наибольшая z окна {5})",
                                F(flag.LoKev, "F1"), F(flag.HiKev, "F1"), F(flag.Z, "F1"), F(flag.Ratio, "F3"),
                                flag.Bands, F(worstZ, "F2"));
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides, string curveName)
        {
            if (!File.Exists(path)) { Console.Error.WriteLine("нет файла: " + path); return null; }
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(stream);
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
            if (curveName != null)
            {
                EfficiencyConfigData chosen = null;
                if (rd.DeviceConfig != null && rd.DeviceConfig.EfficiencyConfigs != null)
                {
                    chosen = rd.DeviceConfig.EfficiencyConfigs.FirstOrDefault(e => string.Equals(e.Name, curveName, StringComparison.Ordinal));
                }
                if (chosen == null) { Console.Error.WriteLine("у прибора нет кривой «" + curveName + "»"); return null; }
                rd.Efficiency = DCControlPanel.EfficiencyFromItem(chosen);
            }
            EfficiencyConfigData eff = rd.Efficiency;
            Console.WriteLine("SETUP\tкривая: {0}", eff == null ? "нет"
                : string.Format(CultureInfo.InvariantCulture, "«{0}» {1}, {2}, точек {3}, геометрия {4}, клеймо «{5}»",
                                eff.Name, eff.Guid, eff.Origin, eff.Curve != null ? eff.Curve.Count : 0,
                                eff.HasGeometry ? "есть" : "нет", eff.ComputeStamp));
            FwhmCalibration fw = rd.FwhmCalibration;
            EnergyCalibration cal = s.EnergyCalibration;
            Console.WriteLine("SETUP\tканалов {0}; T {1} с, живое {2} с; отсчётов {3}; фон {4}; вес {5} кг; κ {6}; FWHM на 662 кэВ {7} кэВ ({8}){9}",
                              s.NumberOfChannels, F(s.MeasurementTime, "F1"), F(s.LiveTime, "F1"), s.TotalPulseCount,
                              rd.BackgroundEnergySpectrum != null
                                  ? string.Format(CultureInfo.InvariantCulture, "{0} отсч. за {1} с (живое {2})",
                                                  rd.BackgroundEnergySpectrum.TotalPulseCount,
                                                  F(rd.BackgroundEnergySpectrum.MeasurementTime, "F1"),
                                                  F(rd.BackgroundEnergySpectrum.LiveTime, "F1"))
                                  : "НЕТ",
                              rd.SampleInfo != null ? F(rd.SampleInfo.Weight, "0.####") : "?",
                              rd.TractCurvature.ToString("R", CultureInfo.InvariantCulture),
                              fw != null ? F(fw.ChannelToFwhm(cal.EnergyToChannel(662.0, s.NumberOfChannels))
                                             * (cal.ChannelToEnergy(cal.EnergyToChannel(662.0, s.NumberOfChannels) + 0.5)
                                                - cal.ChannelToEnergy(cal.EnergyToChannel(662.0, s.NumberOfChannels) - 0.5)), "F1") : "—",
                              fw != null ? fw.GetType().Name : "кривой нет",
                              IsStandIn(fw) ? ", ВСТРОЕННАЯ ЗАГЛУШКА 15/3756/103" : "");
            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            foreach (Peak p in rd.DetectedPeaks)
            {
                Console.WriteLine("пик\t{0,8} кэВ кан. {1,5} SNR {2,6} {3}", F(p.Energy, "F2"), p.Channel, F(p.SNR, "F1"),
                                  p.Nuclide != null ? p.Nuclide.Name : "(неизвестный)");
            }
            return rd;
        }
    }
}
