using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaGateRescueProbe
{
    /// <summary>
    /// СТОРОЖ САМООТКЛЮЧЕНИЯ ГЕЙТА ΔD&lt;0 (`AMBER3`).
    ///
    ///     fsagaterescueprobe --spectrum=&lt;файл спектра с матрицей отклика&gt;
    ///
    /// Гейт по парциальной невязке (P6 «б») снимает нуклидную колонку, чьё
    /// присутствие ухудшает невязку её же пиковых окон. На спектре, где
    /// смещена ВСЯ модель (чужая матрица, не то вещество пробы), это верно для
    /// каждого объявленного нуклида разом — и до `AMBER3` гейт выносил состав
    /// ЦЕЛИКОМ, оставляя человеку вердикт «состав пересилен приборным
    /// образом» у спектра, где ряд виден глазом.
    ///
    /// Два плеча, оба на ОДНОМ спектре — иначе «правка сработала» неотличимо
    /// от «правка не понадобилась»:
    ///
    ///   1. С МАТРИЦЕЙ — гейт судит и выносит всех, самоотключение обязано
    ///      сработать: <see cref="FsaAnalyzer.GateNuclidesRescued"/> &gt; 0 и
    ///      нуклидная доля больше половины стека.
    ///   2. БЕЗ МАТРИЦЫ (тот же спектр, та же кривая) — гейт не выносит
    ///      никого, самоотключение обязано МОЛЧАТЬ: возвращённых 0. Это
    ///      положительный контроль на то, что правка не выключила гейт вовсе.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей: полоса это статика,
            // отражение её не видит, и снятая позже она уже могла быть уведена.
            FsaTuningReport.Snapshot();

            string path = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (path == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл спектра, у которого есть матрица отклика>");
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(path, nuclides);
            if (rd == null) return 2;

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);

            int judged, rescued;
            FsaResult withMatrix = Run(rd, options, true, out judged, out rescued);
            Console.WriteLine();
            Console.WriteLine("=== 1. С МАТРИЦЕЙ: гейт выносил бы состав целиком ===");
            if (withMatrix == null)
            {
                Console.WriteLine("  ⛔ разбор не получился");
                bad++;
            }
            else
            {
                Console.WriteLine("  судимых нуклидных колонок {0}, возвращено {1}; нуклидная доля {2:F2} %, χ²/ndf {3:F3}",
                                  judged, rescued, withMatrix.NuclideSharePercent, withMatrix.Chi2Ndf);
                Same("матрица взята (иначе плечо ничего не мерит)", true, withMatrix.ResponseMatrixUsed);
                Same("гейт судил хотя бы одну нуклидную колонку", true, judged > 0);
                Same("самоотключение сработало", true, rescued > 0);
                Same("состав не пуст", true, withMatrix.NuclideSharePercent > 50.0);
                Same("вердикта «пересилен приборным образом» нет", false, withMatrix.CompositionSuppressed);
            }

            FsaResult noMatrix = Run(rd, options, false, out judged, out rescued);
            Console.WriteLine();
            Console.WriteLine("=== 2. БЕЗ МАТРИЦЫ (положительный контроль: правка молчит) ===");
            if (noMatrix == null)
            {
                Console.WriteLine("  ⛔ разбор не получился");
                bad++;
            }
            else
            {
                Console.WriteLine("  судимых нуклидных колонок {0}, возвращено {1}; нуклидная доля {2:F2} %, χ²/ndf {3:F3}",
                                  judged, rescued, noMatrix.NuclideSharePercent, noMatrix.Chi2Ndf);
                Same("матрицы нет", false, noMatrix.ResponseMatrixUsed);
                Same("гейт судил хотя бы одну нуклидную колонку", true, judged > 0);
                Same("самоотключение НЕ срабатывало", 0, rescued);
                Same("состав не пуст", true, noMatrix.NuclideSharePercent > 50.0);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Тот же путь, каким считает сеанс разбора (`FsaAnalysisSession`), но
        /// с рычагом «матрица отклика» и с чтением счётчиков анализатора.
        /// </summary>
        static FsaResult Run(ResultData rd, FsaCalculationOptions options, bool useMatrix,
                             out int judged, out int rescued)
        {
            judged = 0;
            rescued = 0;

            EnergySpectrum spectrum = rd.EnergySpectrum.Clone();
            FwhmCalibration fwhm = rd.FwhmCalibration != null ? rd.FwhmCalibration.Clone() : null;
            EfficiencyConfigData efficiencyConfig = rd.Efficiency != null ? rd.Efficiency.Copy() : null;
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(efficiencyConfig);

            var analyzer = new FsaAnalyzer();
            options.ApplyTo(analyzer);
            FsaTuningReport.Print(analyzer);

            if (useMatrix && efficiencyConfig != null && efficiencyConfig.HasGeometry
                && efficiencyConfig.UseResponseMatrix)
            {
                MatrixRefusal refusal;
                int fileFormat;
                ResponseMatrix matrix = ResponseMatrixStore.Load(efficiencyConfig.Guid, out refusal, out fileFormat);
                if (matrix != null && matrix.IsValidFor(efficiencyConfig.Geometry))
                {
                    analyzer.ResponseMatrix = matrix;
                    analyzer.ScintillatorMaterial =
                        EfficiencySimulator.ScintillatorNameOf(efficiencyConfig.Geometry);
                }
                else
                {
                    Console.WriteLine("  ⚠ матрица не взята: файл {0}, отказ {1}",
                                      matrix != null ? "прочитан" : "не прочитан", refusal);
                }
            }

            List<FsaComponent> library;
            if (options.DbLookups)
            {
                FsaCompositionInference.Report inferred;
                FsaSampleSpec spec = FsaCompositionInference.Infer(
                    new List<Peak>(rd.DetectedPeaks), rd, out inferred);
                options.ApplyTo(spec);
                library = FsaSampleLibrary.Build(spec);
            }
            else
            {
                library = FsaLibrary.BuildFromPeaks(new List<Peak>(rd.DetectedPeaks),
                    NuclideDefinitionManager.GetInstance().NuclideDefinitions, null, options.AtomicXray);
            }

            FsaResult result = analyzer.Analyze(spectrum, null, fwhm, library, efficiency);
            judged = analyzer.GateNuclidesJudged;
            rescued = analyzer.GateNuclidesRescued;
            return result;
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-62} {2}{3}", ok ? "ok  " : "⛔ ", what, got,
                              ok ? string.Empty : "  вместо " + expected);
            if (!ok) bad++;
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

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
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

            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            Console.WriteLine("SETUP\t{0}: пиков {1}, каналов {2}, кривая {3}",
                              Path.GetFileName(path), rd.DetectedPeaks.Count,
                              rd.EnergySpectrum.NumberOfChannels,
                              rd.Efficiency != null ? rd.Efficiency.Name : "(нет)");
            return rd;
        }
    }
}
