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
using System.Xml.Serialization;

namespace FsaUncovered511ProbeA306
{
    /// <summary>
    /// (`AMBER306`, находка `crit306`, 06.11.2026) ИЗБЫТОК У 511 КЭВ НЕ ПРЯЧЕТСЯ ЗА «ПРОИЗВОДНОЙ» ЛИНИЕЙ.
    ///
    ///     FsaUncovered511ProbeA306 [--spectra=&lt;каталог спектров&gt;] [--store=&lt;склад матриц&gt;]
    ///
    /// Прежде 511 шла производной от любой жёсткой линии состава и покрывала серию при снятом образе
    /// `Ann-511`. На неверном сете это обманывало: `G1S16_Mix_Mar` на сете Eu-152, избыток 255718 отсчётов у
    /// 511, χ²/ndf Пуассона 1472, а строки отчёта нет.
    ///
    /// Разделы: 1 — смесь на чужом сете: строка у 511 есть; 2 — та же смесь, в библиотеке нет линий у 511:
    /// строка есть и без имени; 3–5 — контроль, строки нет: образ жив (Na-22), линия состава у 511 и малый
    /// избыток (Th-228), жёсткая линия без избытка (K-40). Положительный контроль: сборка до правки — код 1.
    /// Код: 0 — сошлось; 1 — расхождение; 2 — нечем мерить.
    /// </summary>
    static class Program
    {
        const double Kev = 511.0;
        static int bad;
        static string store;

        [STAThread]
        static int Main(string[] args)
        {
            FsaTuningReport.Snapshot();
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string corpus = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "CORPUS", "corpus"));
            string dir = Path.Combine(corpus, "spectra");
            store = Path.Combine(corpus, "geometries", "response");
            foreach (string a in args)
            {
                if (a.StartsWith("--spectra=", StringComparison.Ordinal)) dir = a.Substring(10);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (!Directory.Exists(dir) || !Directory.Exists(store))
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: нет каталога " + dir + " или " + store);
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            FsaUncoveredPeak row = Scene(dir, "G1S16_Mix_Mar", "Eu-152", nuclides, false);
            Say(row != null && row.ExcessCounts > 100000.0 && row.ExcessShare >= 0.5, "1. смесь на сете Eu-152: строка у 511 есть");
            row = Scene(dir, "G1S16_Mix_Mar", "Eu-152", nuclides, true);
            Say(row != null && row.Candidates.Count == 0, "2. в библиотеке нет линий у 511: строка есть, без имени");
            Say(Scene(dir, "G1S24_Na22_P25", "Na-22", nuclides, false) == null, "3. Na-22: образ аннигиляции жив, строки нет");
            Say(Scene(dir, "G1S24_Th228_P5", "Th-232 + K-40", nuclides, false) == null, "4. Th-228: линия состава у 511, избыток мал, строки нет");
            Say(Scene(dir, "G1S24_K40_Mar", "Cs-137 + K-40", nuclides, false) == null, "5. K-40: жёсткая линия, избытка нет, строки нет");

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            Console.Out.Flush();
            return bad == 0 ? 0 : 1;
        }

        static void Say(bool ok, string text)
        {
            Console.WriteLine((ok ? "  ok  " : "  ⛔  ") + text);
            if (!ok) bad++;
        }

        static FsaUncoveredPeak Scene(string dir, string name, string setName, NuclideDefinitionManager nuclides, bool dropNear)
        {
            nuclides.ActiveSet = (nuclides.NuclideSets ?? new List<NuclideSet>())
                .FirstOrDefault(s => string.Equals(s.Name, setName, StringComparison.OrdinalIgnoreCase));
            if (nuclides.ActiveSet == null)
            {
                Say(false, "сета нет: " + setName);
                return null;
            }

            ResultData rd = Load(Path.Combine(dir, name + ".xml"), nuclides);
            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            options.DbLookups = false;
            FsaAnalysisSession.ProbeAnalyzerHook = analyzer =>
            {
                FsaTuningReport.Print(analyzer, name);
                if (dropNear)
                {
                    analyzer.LibraryLines = analyzer.LibraryLines.Where(l => Math.Abs(l.Energy - Kev) > 100.0).ToList();
                }
            };
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
            FsaResult result = done.WaitOne(TimeSpan.FromSeconds(280)) ? session.Result : null;
            if (result == null)
            {
                Say(false, name + ": разбора нет: " + session.Status);
                return null;
            }

            Console.WriteLine("{0}, сет {1}: матрица {2}, χ²/ndf Пуассона {3:F2}, колонки: {4}", name, setName, result.ResponseMatrixUsed, result.Chi2NdfPoisson,
                              string.Join(" ", result.Components.Select(c => c.Name)));
            FsaUncoveredPeak near = result.UncoveredPeaks.FirstOrDefault(p => Math.Abs(p.EnergyKev - Kev) <= 0.5 * p.WidthKev);
            foreach (FsaUncoveredPeak p in result.UncoveredPeaks)
            {
                Console.WriteLine("  вне состава {0:F1} кэВ, избыток {1:F0}, доля {2:F3}, z {3:F1}: {4}", p.EnergyKev, p.ExcessCounts, p.ExcessShare, p.Z,
                                  string.Join(", ", p.Candidates.Select(c => c.Nuclide + " " + c.EnergyKev.ToString("F1", CultureInfo.InvariantCulture))));
            }

            return near;
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
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

            ProbeDeviceConfig.Attach(rd);
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null) cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }

            // Матрица кривой — из склада корпуса, в файл спектра: разбор читает её оттуда.
            if (rd.Efficiency != null && rd.Efficiency.HasGeometry)
            {
                string rmx = Path.Combine(store, rd.Efficiency.Guid + ".rmx");
                ResponseMatrix matrix = File.Exists(rmx) ? ResponseMatrix.Load(rmx) : null;
                if (matrix != null && matrix.IsValidFor(rd.Efficiency.Geometry))
                {
                    rd.Efficiency.EmbeddedMatrix = EmbeddedResponseMatrix.FromFile(rd.Efficiency.Guid, rmx, matrix);
                }
            }

            rd.DetectedPeaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None, nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }
    }
}
