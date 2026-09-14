using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace AnnihilationGateProbe
{
    /// <summary>
    /// СТОРОЖ ГЕЙТА 511 (`AMBER7`): два столбца на одну линию — не модель.
    ///
    ///     annihilationgateprobe --spectrum=&lt;спектр, где своя линия у 511&gt; --set=&lt;сет&gt;
    ///                           --control=&lt;спектр без своей линии у 511&gt; --control-set=&lt;сет&gt;
    ///
    /// ⛔ ЗАЧЕМ. Задача Amber 08.09.2026, снимком: «Откуда ANN-511?» — на
    /// чистом тории свободный образ брал 0.86 %. Аннигиляционная линия в
    /// спектре ЕСТЬ (пары рождаются в защите и обвязке от 2614.5 кэВ), спор не
    /// о ней, а о том, можно ли её ИЗМЕРИТЬ отдельной колонкой: у Tl-208 своя
    /// линия 510.77 кэВ с выходом 8.12 % отстоит от 511.00 на 0.23 кэВ, то
    /// есть на сцинтилляторе это ОДИН столбец. Свободная амплитуда 511 просто
    /// отбирает отсчёты у объявленного ряда (родня `A280`).
    ///
    /// Два плеча, и второе обязательно:
    ///
    ///   1. СТОЛКНОВЕНИЕ ЕСТЬ: гейт назвал столкнувшуюся линию
    ///      (<see cref="FsaAnalyzer.AnnihilationCollides"/>), и `Ann-511` нет
    ///      ни живым, ни отсеянным — образ не строился вовсе.
    ///   2. СТОЛКНОВЕНИЯ НЕТ (положительный контроль): гейт молчит, образ
    ///      предъявлен фиту — иначе правка снимала бы 511 везде.
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

            // (`T243`) Эталон настроек — ДО разбора ключей.
            FsaTuningReport.Snapshot();

            string path = null, setName = null, control = null, controlSet = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--control-set=", StringComparison.Ordinal)) controlSet = a.Substring(14);
                else if (a.StartsWith("--control=", StringComparison.Ordinal)) control = a.Substring(10);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (path == null || control == null)
            {
                Console.Error.WriteLine("нужны --spectrum=<столкновение есть> и --control=<столкновения нет>");
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            Section("1. СТОЛКНОВЕНИЕ ЕСТЬ: 511 не строится", path, setName, nuclides, true);
            Section("2. СТОЛКНОВЕНИЯ НЕТ: 511 предъявлена (положительный контроль)",
                    control, controlSet, nuclides, false);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static void Section(string title, string path, string setName,
                            NuclideDefinitionManager nuclides, bool expectCollision)
        {
            Console.WriteLine();
            Console.WriteLine("=== {0} ===", title);

            nuclides.ActiveSet = null;
            if (setName != null && nuclides.NuclideSets != null)
            {
                foreach (NuclideSet set in nuclides.NuclideSets)
                {
                    if (string.Equals(set.Name, setName, StringComparison.OrdinalIgnoreCase))
                    {
                        nuclides.ActiveSet = set;
                    }
                }
            }

            ResultData rd = Load(path, nuclides);
            if (rd == null) { bad++; return; }

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            var analyzer = new FsaAnalyzer();
            options.ApplyTo(analyzer);
            FsaTuningReport.Print(analyzer);

            FsaCompositionInference.Report inferred;
            FsaSampleSpec spec = FsaCompositionInference.Infer(
                new List<Peak>(rd.DetectedPeaks), rd, out inferred);
            options.ApplyTo(spec);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);

            bool inLibrary = false;
            foreach (FsaComponent component in library)
            {
                if (FsaResult.IsAnnihilationImage(component.Name)) inLibrary = true;
            }

            FsaResult result = analyzer.Analyze(rd.EnergySpectrum.Clone(),
                rd.BackgroundEnergySpectrum != null ? rd.BackgroundEnergySpectrum.Clone() : null,
                rd.FwhmCalibration != null ? rd.FwhmCalibration.Clone() : null,
                library, FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null) { Console.WriteLine("  ⛔ разбор не получился"); bad++; return; }

            bool shown = false;
            foreach (FsaComponentResult component in result.Components)
            {
                if (FsaResult.IsAnnihilationImage(component.Name)) shown = true;
            }

            foreach (FsaSuppressedImage image in result.SuppressedImages)
            {
                if (FsaResult.IsAnnihilationImage(image.Name)) shown = true;
            }

            Console.WriteLine("  511 в библиотеке {0}; столкновение: {1}; нуклидная доля {2:F2} %",
                              inLibrary, analyzer.AnnihilationCollides ?? "нет", result.NuclideSharePercent);

            Same("образ 511 библиотека строит (иначе плечо ничего не мерит)", true, inLibrary);
            if (expectCollision)
            {
                Same("гейт назвал столкнувшуюся линию", true,
                     !string.IsNullOrEmpty(analyzer.AnnihilationCollides));
                Same("511 не предъявлена фиту — ни живой, ни отсеянной", false, shown);
            }
            else
            {
                Same("гейт молчит: столкновения нет", null, analyzer.AnnihilationCollides);
                Same("511 предъявлена фиту", true, shown);
            }
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-58} {2}{3}", ok ? "ok  " : "⛔ ", what,
                              got ?? "(null)", ok ? string.Empty : "  вместо " + (expected ?? "(null)"));
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

            Console.WriteLine("  {0}: прибор {1}, сет {2}", Path.GetFileName(path),
                              ProbeDeviceConfig.Attach(rd),
                              nuclides.ActiveSet != null ? nuclides.ActiveSet.Name : "(все нуклиды)");

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
            return rd;
        }
    }
}
