using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaLineAuditZeroMapProbe
{
    /// <summary>
    /// ОКНО СВЕРКИ ЛИНИЙ ПРОТИВ КАРТЫ НУЛЯ «adc», КОТОРОЙ ПОЛОЖЕН ОБРАЗ
    /// (`S181`, П136 22.09.2026).
    ///
    /// ⛔ ЧТО МЕРИТСЯ. Анализатор кладёт каждый бин образа через карту
    /// E꜀(x) = E(0) + (x − z₀)·s (<c>FsaAnalyzer.LightEnergyKev</c>, `S169`), а
    /// <see cref="FsaLineAudit"/> ставил окно линии прямой калибровкой. Разность
    /// Δ(x) = (s − 1)(x − x₁) обращается в нуль ТОЛЬКО у верхней линии
    /// библиотеки, поэтому глазом расхождение не видно, а `Sigma`,
    /// `DecisionThreshold`, `Obligatory` и `Purity` считаются мимо пика.
    ///
    /// ⚠ ПЛЕЧИ БЕРУТСЯ ОДНИМ РАЗБОРОМ. Сверка читает карту из
    /// <c>FsaResult.AdcScale</c>; занулив его у того же результата, получаем
    /// РОВНО прежний ход (прямая калибровка) — то есть плечо «ДО» без второй
    /// сборки. Модель, амплитуды и континуум при этом те же до бита: сверка
    /// разбора не трогает.
    ///
    ///   fsalineauditzeromapprobe --spectrum=&lt;файл.xml&gt; [--chain=Th-232]
    ///                            [--sample=137CS] [--zero=calib] [--matrix-any] [--top=12]
    ///
    /// Печатает по линии: энергию, канал окна ДО и ПОСЛЕ, смещение в каналах и
    /// в ПШПВ, и обе тройки `Ratio` / `Z` / `Sigma`.
    ///
    /// ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ встроен: если карта не включилась (`AdcScale` = 0
    /// — спектр без матрицы, привязка выключена, карта «calib»), плечи обязаны
    /// совпасть ПОБИТОВО; расхождение при выключенной карте — отказ кодом 1.
    /// Он же проверяет, что правка ничего не двигает там, где двигать нечего.
    /// Ставится он ТЕМ ЖЕ спектром и ТОЙ ЖЕ матрицей через `--zero=calib`:
    /// одна переменная, а не другой спектр с другой геометрией.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            bool matrixAny = false;
            int top = 12;
            string zero = null;

            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal)) chains.AddRange(a.Substring(8).Split(','));
                else if (a.StartsWith("--sample=", StringComparison.Ordinal)) nuclides.AddRange(a.Substring(9).Split(','));
                else if (a.StartsWith("--top=", StringComparison.Ordinal))
                    top = int.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--zero=", StringComparison.Ordinal)) zero = a.Substring(7);
                else if (a == "--matrix-any") matrixAny = true;
                else { Console.Error.WriteLine("неизвестный ключ: {0}", a); return 2; }
            }

            if (spectrumPath == null) { Console.Error.WriteLine("нужен --spectrum=<файл.xml>"); return 2; }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            bool stampOk = matrix != null && rd.Efficiency != null && rd.Efficiency.HasGeometry
                           && matrix.IsValidFor(rd.Efficiency.Geometry);
            if (matrix != null && !stampOk && !matrixAny)
            {
                Console.Error.WriteLine("⛔ ОТПЕЧАТОК НЕ СОШЁЛСЯ ({0}); осознанно — ключ --matrix-any", refusal);
                return 1;
            }

            Console.WriteLine("матрица: {0}", matrix == null ? "НЕТ (" + refusal + ")" : "есть, формат "
                              + fileFormat.ToString(CultureInfo.InvariantCulture));

            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, NucidsOf(nuclides), true, true);
            string material = EfficiencySimulator.ScintillatorNameOf(
                rd.Efficiency != null ? rd.Efficiency.Geometry : null);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            if (library.Count == 0) { Console.Error.WriteLine("⛔ библиотека пуста"); return 1; }

            var analyzer = new FsaAnalyzer { ResponseMatrix = matrix, ScintillatorMaterial = material };
            if (zero != null)
            {
                // Плечо контроля: `--zero=calib` гасит карту нуля тем же
                // ключом разбора, которым она и заводится, — одна переменная.
                analyzer.AnchorZero = zero;
            }
            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            var peakConfig = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (peakConfig != null)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            FsaTuningReport.Print(analyzer, "сверка линий против карты нуля");
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("⛔ разбор не состоялся: {0} — {1}", analyzer.Refusal, analyzer.RefusalNote);
                return 1;
            }

            Console.WriteLine("привязка: {0}", result.AnchorNote);
            Console.WriteLine("карта нуля: растяжение {0}, E(0) {1} кэВ, z₀ {2} кэВ",
                              F(result.AdcScale, 6), F(result.AdcE0Kev, 3), F(result.AdcZeroKev, 3));

            // ПОСЛЕ — карта как есть; ДО — она же, занулённая (прежний ход).
            List<FsaLineAudit.LineCheck> after = FsaLineAudit.Run(
                rd.EnergySpectrum, result, rd.FwhmCalibration, library, rd.BackgroundEnergySpectrum);
            double scale = result.AdcScale, e0 = result.AdcE0Kev, z0 = result.AdcZeroKev;
            result.AdcScale = 0.0;
            List<FsaLineAudit.LineCheck> before = FsaLineAudit.Run(
                rd.EnergySpectrum, result, rd.FwhmCalibration, library, rd.BackgroundEnergySpectrum);
            result.AdcScale = scale;

            // ⚠ Число строк у плеч может РАЗОЙТИСЬ, и это само по себе
            // следствие правки: окно, сдвинутое картой, иначе сливает соседние
            // линии и иначе упирается в края полосы. Поэтому пары ищутся по
            // (компонент, энергия), а не по номеру, а разница в числе строк
            // печатается отдельной величиной.
            Console.WriteLine("строк сверки: ДО {0}, ПОСЛЕ {1}", before.Count, after.Count);

            Console.WriteLine();
            Console.WriteLine("=== ЛИНИИ: окно ДО (прямая калибровка) против ПОСЛЕ (карта нуля) ===");
            Console.WriteLine("{0,-10} {1,9} {2,9} {3,9} {4,9} {5,9} {6,9} {7,9}",
                              "компонент", "E, кэВ", "ΔE, кэВ", "Ratio ДО", "Ratio ПОСЛЕ", "Z ДО", "Z ПОСЛЕ", "σ ДО/ПОСЛЕ");

            int moved = 0, shown = 0, unpaired = 0;
            double maxDeltaKev = 0.0, maxDeltaZ = 0.0;
            bool identical = before.Count == after.Count;
            for (int i = 0; i < after.Count; i++)
            {
                FsaLineAudit.LineCheck a = after[i];
                FsaLineAudit.LineCheck b = Pair(before, a);
                if (b == null)
                {
                    unpaired++;
                    identical = false;
                    continue;
                }

                // Смещение окна в кэВ — та самая Δ(x) = (s − 1)(x − x₁).
                double deltaKev = scale > 0.0 ? (scale - 1.0) * (a.EnergyKev - TopKev(scale, e0, z0)) : 0.0;
                double dz = Math.Abs(ZOf(a) - ZOf(b));
                if (a.Measured != b.Measured || a.Sigma != b.Sigma || a.Expected != b.Expected)
                {
                    identical = false;
                    moved++;
                }

                if (Math.Abs(deltaKev) > maxDeltaKev) maxDeltaKev = Math.Abs(deltaKev);
                if (!double.IsNaN(dz) && dz > maxDeltaZ) maxDeltaZ = dz;

                if (shown < top)
                {
                    Console.WriteLine("{0,-10} {1,9} {2,9} {3,9} {4,9} {5,9} {6,9} {7,9}",
                                      Short(a.Component), F(a.EnergyKev, 2), F(deltaKev, 2),
                                      F(b.Ratio, 4), F(a.Ratio, 4), F(ZOf(b), 2), F(ZOf(a), 2),
                                      F(b.Sigma, 1) + "/" + F(a.Sigma, 1));
                    shown++;
                }
            }

            Console.WriteLine();
            Console.WriteLine("пар сравнено {0}; без пары {1}; сдвинулось {2}; наибольший |ΔE| {3} кэВ; наибольший |ΔZ| {4}",
                              after.Count - unpaired, unpaired, moved, F(maxDeltaKev, 2), F(maxDeltaZ, 2));

            if (!(scale > 0.0))
            {
                // Карта не включилась — плечи обязаны совпасть ПОБИТОВО.
                if (!identical)
                {
                    Console.Error.WriteLine("⛔ КОНТРОЛЬ: карта нуля выключена, а плечи разошлись — правка двигает то, чего не должна");
                    return Gate(1);
                }

                Console.WriteLine("КОНТРОЛЬ ПРОШЁЛ: карта нуля не включалась, плечи побитово равны");
                return Gate(0);
            }

            if (identical)
            {
                Console.Error.WriteLine("⛔ карта нуля включена (растяжение {0}), а сверка не шевельнулась — правка не доехала",
                                        F(scale, 6));
                return Gate(1);
            }

            Console.WriteLine("СОШЛОСЬ: карта нуля включена, окно сверки встало туда же, куда положен образ");
            return Gate(0);
        }

        static int Gate(int code)
        {
            int raised = NuclideDefinitionManager.RaiseCount;
            Console.WriteLine("NuclideDefinitionManager за прогон: обращений {0}", raised);
            if (raised > 0)
            {
                Console.Error.WriteLine("⛔ AMBER19: поставочную библиотеку поднимали {0} раз(а) — числа негодны", raised);
                return 12;
            }

            return code;
        }

        /// <summary>
        /// Верхняя линия библиотеки x₁ по самой карте: E꜀(x₁) = x₁ даёт
        /// x₁ = (E(0) − z₀·s)/(1 − s). Нужна только для печати Δ(x); при s = 1
        /// расхождения нет вовсе, и точка не определена — тогда 0.
        /// </summary>
        static double TopKev(double scale, double e0, double zero)
        {
            return Math.Abs(scale - 1.0) > 1.0E-12 ? (e0 - zero * scale) / (1.0 - scale) : 0.0;
        }

        /// <summary>
        /// Пара строки ПОСЛЕ среди строк ДО: тот же компонент и ближайшая
        /// энергия в пределах 1 %. По номеру пары не ищутся: окно, сдвинутое
        /// картой, может слить соседние линии и изменить длину списка.
        /// </summary>
        static FsaLineAudit.LineCheck Pair(List<FsaLineAudit.LineCheck> list, FsaLineAudit.LineCheck want)
        {
            FsaLineAudit.LineCheck best = null;
            double bestDelta = double.MaxValue;
            foreach (FsaLineAudit.LineCheck c in list)
            {
                if (!string.Equals(c.Component, want.Component, StringComparison.Ordinal)) continue;
                double d = Math.Abs(c.EnergyKev - want.EnergyKev);
                if (d < bestDelta) { bestDelta = d; best = c; }
            }

            return best != null && bestDelta <= 0.01 * Math.Max(want.EnergyKev, 1.0) ? best : null;
        }

        static double ZOf(FsaLineAudit.LineCheck c)
        {
            return c.Sigma > 0.0 ? (c.Measured - c.Expected) / c.Sigma : double.NaN;
        }

        static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Length <= 10 ? name : name.Substring(0, 10);
        }

        static string F(double value, int digits)
        {
            if (double.IsNaN(value)) return "—";
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture),
                                  CultureInfo.InvariantCulture);
        }

        static List<string> NucidsOf(List<string> labels)
        {
            var nucids = new List<string>();
            foreach (string label in labels)
            {
                if (label.Length == 0) continue;
                int dash = label.IndexOf('-');
                nucids.Add(dash < 0 ? label.ToUpperInvariant()
                                    : label.Substring(dash + 1) + label.Substring(0, dash).ToUpperInvariant());
            }

            return nucids;
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            using (var reader = new StreamReader(path, Encoding.UTF8))
            {
                var file = (ResultDataFile)serializer.Deserialize(reader);
                return file.ResultDataList[0];
            }
        }
    }
}
