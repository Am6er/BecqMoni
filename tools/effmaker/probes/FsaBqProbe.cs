using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// Активность по нуклидам из разбора FSA БЕЗ ОКОН, тем же путём, что и
    /// окно Measurement Result (`AMBER208`, задача Amber 06.10.2026 «ROI убрать,
    /// активность по нуклидам считает FSA и выводит в окно Measurement Result»):
    /// <see cref="FsaAnalysisSession.EnsureUpToDate(ResultData, bool)"/> →
    /// <see cref="FsaMeasurementResult.Build"/> →
    /// <see cref="MeasurementResultManager.Translate"/>.
    ///
    /// Печатает по каждому родителю сета: Бк (FSA) ± σ, предел a# в Бк,
    /// обнаружен ли; отношение к паспорту, если он задан (сравнение с зонами
    /// ROI снято 07.10.2026 вместе с путём зон). Ключ
    /// <c>--thin=</c> прореживает спектр биномиально (доля p отсчётов, время
    /// набора ×p): имитация ранней стадии набора — как ведут себя Бк и предел
    /// при малой статистике.
    ///
    /// Ключи:
    ///   --spectrum=&lt;файл&gt;        можно несколько раз
    ///   --set=&lt;имя сета&gt;        по умолчанию активный сет конфигурации; "all" — все определения
    ///   --thin=0.01,0.1           доли отсчётов для прореживания (зерно фиксировано)
    ///   --passport=Cs-137:5344,K-40:11312   паспорт, Бк, по родителям
    ///   --nucbase                 состав из NucBase по сету (иначе — как в настройках спектра)
    ///   --timeout=120             секунд ожидания разбора
    ///   --weights=data|model      веса решателя (умолчание приложения — model, Пирсон)
    ///   --huber=M                 порог Хубера в σ (0 — выкл; умолчание приложения 3)
    ///   --gamma=G                 составной шум S43 (умолчание приложения 0)
    ///   --xi=X                    доля погрешности континуума в весах (умолчание 0.03)
    ///   --no-pileup --no-backscatter --no-xray --no-summing --no-escape   снять компонент модели
    ///   --seed=N                  зерно прореживания (умолчание 20261006)
    ///   --components              печатать все компоненты разбора с долями и скоростями
    ///   --no-matrix               снять матрицу отклика у кривой спектра (путь по кривой)
    ///   --huber-inflate=on|off    надувка порога Хубера на √(χ²/ndf) (AMBER209; умолчание приложения — on)
    /// Коды: 0 — посчитано; 2 — ключи/файлы; 3 — разбор не завершился.
    /// </summary>
    static class FsaBqProbe
    {
        static bool NoPileUp, NoBackscatter, NoXray, NoSumming, NoEscape, PrintComponents, NoMatrix;
        static int Seed = 20261006;
        static FsaAnalyzer LastAnalyzer;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var spectra = new List<string>();
            string setName = null;
            var thins = new List<double>();
            var passport = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            bool nucbase = false, equilibrium = false, fromSet = false;
            int timeoutS = 120;
            string weights = null, huberInflate = null;
            double huber = double.NaN, gamma = double.NaN, xi = double.NaN;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectra.Add(a.Substring(11));
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a == "--nucbase") nucbase = true;
                else if (a == "--equilibrium") equilibrium = true;
                else if (a == "--fromset") fromSet = true;
                else if (a.StartsWith("--weights=", StringComparison.Ordinal)) weights = a.Substring(10);
                else if (a.StartsWith("--huber=", StringComparison.Ordinal)) huber = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--gamma=", StringComparison.Ordinal)) gamma = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--xi=", StringComparison.Ordinal)) xi = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) Seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a == "--no-pileup") NoPileUp = true;
                else if (a == "--no-backscatter") NoBackscatter = true;
                else if (a == "--no-xray") NoXray = true;
                else if (a == "--no-summing") NoSumming = true;
                else if (a == "--no-escape") NoEscape = true;
                else if (a == "--components") PrintComponents = true;
                else if (a == "--no-matrix") NoMatrix = true;
                else if (a.StartsWith("--huber-inflate=", StringComparison.Ordinal)) huberInflate = a.Substring(16);
                else if (a.StartsWith("--thin=", StringComparison.Ordinal))
                {
                    foreach (string part in a.Substring(7).Split(','))
                    {
                        if (part.Trim().Length > 0) thins.Add(double.Parse(part.Trim(), CultureInfo.InvariantCulture));
                    }
                }
                else if (a.StartsWith("--passport=", StringComparison.Ordinal))
                {
                    foreach (string part in a.Substring(11).Split(','))
                    {
                        int colon = part.LastIndexOf(':');
                        if (colon > 0)
                        {
                            passport[part.Substring(0, colon).Trim()] = double.Parse(part.Substring(colon + 1).Trim(), CultureInfo.InvariantCulture);
                        }
                    }
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (spectra.Count == 0)
            {
                Console.Error.WriteLine("нужен хотя бы один --spectrum=<файл>");
                return 2;
            }

            if (huberInflate != null && huberInflate != "on" && huberInflate != "off")
            {
                Console.Error.WriteLine("--huber-inflate= принимает on или off");
                return 2;
            }
            {
                if (weights != null && weights != "data" && weights != "model")
                {
                    Console.Error.WriteLine("--weights= принимает data или model");
                    return 2;
                }
                FsaAnalysisSession.ProbeAnalyzerHook = analyzer =>
                {
                    LastAnalyzer = analyzer;
                    if (huberInflate != null) analyzer.HuberInflate = huberInflate == "on";
                    if (weights != null) analyzer.ModelWeights = weights == "model";
                    if (!double.IsNaN(huber)) analyzer.HuberM = huber;
                    if (!double.IsNaN(gamma)) analyzer.NoiseGamma = gamma;
                    if (!double.IsNaN(xi)) analyzer.Xi = xi;
                };
            }
            Console.WriteLine("SETUP\tключи разбора: веса {0}; Хубер {1}; γ {2}; ξ {3}; зерно {4}; надувка порога Хубера {5}",
                              weights ?? "как в приложении",
                              double.IsNaN(huber) ? "как в приложении" : huber.ToString(CultureInfo.InvariantCulture),
                              double.IsNaN(gamma) ? "как в приложении" : gamma.ToString(CultureInfo.InvariantCulture),
                              double.IsNaN(xi) ? "как в приложении" : xi.ToString(CultureInfo.InvariantCulture), Seed,
                              huberInflate ?? "как в приложении");

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            NuclideSet set = nuclides.ActiveSet;
            if (setName != null)
            {
                if (setName == "all")
                {
                    set = null;
                }
                else
                {
                    set = nuclides.NuclideSets != null
                        ? nuclides.NuclideSets.FirstOrDefault(s => string.Equals(s.Name, setName, StringComparison.OrdinalIgnoreCase))
                        : null;
                    if (set == null)
                    {
                        Console.Error.WriteLine("сета «" + setName + "» нет; есть: "
                            + string.Join(", ", (nuclides.NuclideSets ?? new List<NuclideSet>()).Select(s => s.Name)));
                        return 2;
                    }
                }
                nuclides.ActiveSet = set;
            }
            Console.WriteLine("SETUP\tсет: {0}; родителей: {1}; матрицы: {2}",
                              set != null ? set.Name : "(все определения)",
                              FsaMeasurementResult.ParentsOf(set, nuclides.NuclideDefinitions).Count,
                              BecquerelMonitor.EfficiencyMaker.ResponseMatrixStore.Directory);

            int code = 0;
            foreach (string path in spectra)
            {
                ResultData rd = Load(path, nuclides);
                if (rd == null) { code = 2; continue; }
                var fractions = new List<double> { 1.0 };
                fractions.AddRange(thins);
                foreach (double p in fractions)
                {
                    ResultData work = p >= 1.0 ? rd : Thin(rd, p, nuclides);
                    int rc = RunOne(work, path, p, nuclides, set, passport, nucbase, equilibrium, fromSet, timeoutS);
                    if (rc != 0) code = rc;
                }
            }
            return code;
        }

        static int RunOne(ResultData rd, string path, double fraction, NuclideDefinitionManager nuclides, NuclideSet set,
                          Dictionary<string, double> passport, bool nucbase, bool equilibrium, bool fromSet, int timeoutS)
        {
            Console.WriteLine();
            Console.WriteLine("=== {0}  доля отсчётов {1}  отсчётов {2}  T {3} с  live {4} с ===",
                              Path.GetFileName(path), fraction.ToString("0.###", CultureInfo.InvariantCulture),
                              rd.EnergySpectrum.TotalPulseCount,
                              rd.EnergySpectrum.MeasurementTime.ToString("F1", CultureInfo.InvariantCulture),
                              rd.EnergySpectrum.LiveTime.ToString("F1", CultureInfo.InvariantCulture));
            EfficiencyConfigData eff = rd.Efficiency;
            if (NoMatrix && eff != null)
            {
                eff.UseResponseMatrix = false;
            }
            Console.WriteLine("  кривая: {0}", eff == null ? "нет"
                : string.Format(CultureInfo.InvariantCulture, "«{0}» {1}, геометрия {2}, матрица вкл {3}",
                                eff.Name, eff.Origin, eff.HasGeometry ? "есть" : "нет", eff.UseResponseMatrix));

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            if (nucbase)
            {
                options.DbLookups = true;
            }
            if (equilibrium)
            {
                options.ChainEquilibrium = true;
            }
            if (fromSet)
            {
                options.FromSet = true;
            }
            if (NoPileUp) options.PileUp = false;
            if (NoBackscatter) options.Backscatter = false;
            if (NoXray) options.AtomicXray = false;
            if (NoSumming) options.CascadeSumming = false;
            if (NoEscape) options.EscapeAndAnnihilation = false;
            Console.WriteLine("  состав: {0}; равновесие {1}; суммирование {2}; наложения {3}; обратное рассеяние {4}; рентген {5}; вылет/511 {6}",
                              options.FromSet ? "ИЗ СЕТА НУКЛИДОВ" : (options.DbLookups ? "из NucBase по подписям пиков" : "по подписям найденных пиков"),
                              options.ChainEquilibrium, options.CascadeSumming, options.PileUp, options.Backscatter,
                              options.AtomicXray, options.EscapeAndAnnihilation);

            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            var sw = Stopwatch.StartNew();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
            bool finished = done.WaitOne(TimeSpan.FromSeconds(timeoutS));
            sw.Stop();
            FsaResult result = session.Result;
            Console.WriteLine("  разбор: {0} мс, {1}; статус: {2}", sw.ElapsedMilliseconds,
                              finished ? "завершён" : "НЕ ЗАВЕРШЁН за " + timeoutS + " с", session.Status);
            if (result == null)
            {
                Console.WriteLine("  результата нет");
                return 3;
            }
            Console.WriteLine("  матрица {0}; кривая {1}; χ²/ndf {2}; компонентов {3}; пределов {4}",
                              result.ResponseMatrixUsed ? "применена" : "нет",
                              result.EfficiencyUsed ? "учтена" : "нет",
                              result.Chi2Ndf.ToString("F2", CultureInfo.InvariantCulture),
                              result.Components != null ? result.Components.Count : 0,
                              result.CharacteristicLimits != null ? result.CharacteristicLimits.Count : 0);
            Console.WriteLine("  надувка порога Хубера последнего прохода: {0}",
                              LastAnalyzer != null ? LastAnalyzer.LastHuberInflation.ToString("F3", CultureInfo.InvariantCulture) : "крючок не ставился");
            Console.WriteLine("  χ²/ndf Пуассон {0}; σ-надувка {1}; усиление {2}; нуль {3} кан.; опор шкалы {4}; фон {5}{6}; невязка модели {7}",
                              result.Chi2NdfPoisson.ToString("F2", CultureInfo.InvariantCulture),
                              result.SigmaInflation.ToString("F3", CultureInfo.InvariantCulture),
                              result.Gain.ToString("0.#####", CultureInfo.InvariantCulture),
                              result.OffsetChannels.ToString("+0.00;-0.00", CultureInfo.InvariantCulture),
                              result.ScaleAnchorsUsed,
                              result.BackgroundUsed ? "вычтен ×" : "нет",
                              result.BackgroundUsed ? result.BackgroundScale.ToString("0.####", CultureInfo.InvariantCulture) : "",
                              result.ModelResidual.ToString("0.###", CultureInfo.InvariantCulture));
            if (PrintComponents && result.Components != null)
            {
                foreach (FsaComponentResult c in result.Components.OrderByDescending(c => c.SharePercent))
                {
                    Console.WriteLine("    {0,-30} {1,-20} доля {2,8} %  {3,11} имп/с  пик {4,12}  хвост {5,12}  z {6,7}{7}",
                                      c.Name, c.Kind, c.SharePercent.ToString("F3", CultureInfo.InvariantCulture),
                                      c.CountRate.ToString("0.####", CultureInfo.InvariantCulture),
                                      c.PeakCounts.ToString("0.#", CultureInfo.InvariantCulture),
                                      c.TailCounts.ToString("0.#", CultureInfo.InvariantCulture),
                                      c.Z.ToString("0.#", CultureInfo.InvariantCulture),
                                      string.IsNullOrEmpty(c.TiedTo) ? "" : "  привязан к " + c.TiedTo);
                }
            }

            FsaMeasurementResult.Summary summary;
            MeasurementResultCollection built = FsaMeasurementResult.Build(rd, result, set, nuclides.NuclideDefinitions,
                                                                           FsaMeasurementResult.Texts.Russian, out summary);
            Console.WriteLine("  {0}; {1}", summary.Describe(null), summary.Details(null));
            if (built == null)
            {
                return 3;
            }
            var manager = new MeasurementResultManager();
            MeasurementResultCollection bq = manager.Translate(built, ResultTranslation.Becquerels);
            MeasurementResultCollection perKg = rd.SampleInfo != null && rd.SampleInfo.Weight > 0.0
                ? manager.Translate(built, ResultTranslation.BecquerelsPerKilogram) : null;

            Console.WriteLine("  {0,-14} {1,12} {2,12} {3,12} {4,5} | {5,9}",
                              "родитель", "FSA Бк", "±σ", "a# Бк", "обн", "FSA/пасп");
            for (int i = 0; i < bq.ResultList.Count; i++)
            {
                MeasurementResult row = bq.ResultList[i];
                string name = row.Line.Name;
                FsaCharacteristicLimit limit = result.CharacteristicLimits != null
                    ? result.CharacteristicLimits.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
                    : null;
                string detected = limit != null ? (limit.Detected ? "да" : "нет") : "?";
                double pass;
                bool hasPass = passport.TryGetValue(name, out pass);
                string fsaBq = row.IsValid ? F(row.ResultValue) : row.StatusText;
                string fsaS = row.IsValid ? F(row.ResultError) : "";
                string fsaMda = row.IsValid ? F(row.MDA) : "";
                string r2 = row.IsValid && hasPass && pass > 0.0 ? (row.ResultValue / pass).ToString("F3", CultureInfo.InvariantCulture) : "";
                Console.WriteLine("  {0,-14} {1,12} {2,12} {3,12} {4,5} | {5,9}", name, fsaBq, fsaS, fsaMda, detected, r2);
                if (perKg != null && i < perKg.ResultList.Count && perKg.ResultList[i].IsValid)
                {
                    Console.WriteLine("  {0,-14} {1,12} Бк/кг (вес {2} кг)", "", F(perKg.ResultList[i].ResultValue),
                                      rd.SampleInfo.Weight.ToString("0.####", CultureInfo.InvariantCulture));
                }
            }
            return finished ? 0 : 3;
        }

        static string F(double v)
        {
            return double.IsNaN(v) ? "NaN" : v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Биномиальное прореживание: каждый отсчёт канала остаётся с
        /// вероятностью p, время набора и живое время ×p — скорости те же,
        /// статистика как в начале набора. Зерно фиксировано: один и тот же
        /// прореженный спектр при каждом прогоне.
        /// </summary>
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
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("нет файла: " + path);
                return null;
            }
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
            Console.WriteLine("SETUP\t{0}: пиков {1}, каналов {2}", Path.GetFileName(path), rd.DetectedPeaks.Count, s.NumberOfChannels);
            return rd;
        }
    }
}
