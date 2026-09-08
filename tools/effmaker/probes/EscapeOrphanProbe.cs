using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace EscapeOrphanProbe
{
    /// <summary>
    /// СТОРОЖ ПРАВИЛА «ВЫЛЕТА БЕЗ РОДИТЕЛЯ НЕ БЫВАЕТ» (`AMBER8`).
    ///
    ///     escapeorphanprobe --spectrum=&lt;спектр, где родитель отсеян&gt; --set=&lt;сет&gt;
    ///                       --control=&lt;спектр, где родитель жив&gt; --control-set=&lt;сет&gt;
    ///
    /// ⛔ ЗАЧЕМ. Пик вылета — не самостоятельная линия, а ДОЛЯ фотопика
    /// родителя: рождать аннигиляционную пару нечему, если родительского
    /// кванта в спектре нет. Свободная амплитуда такого образа держится лишь
    /// на том, что фиту дали гауссиану в удобном месте. Задача Amber
    /// 08.09.2026, её слова: «Это явная ошибка — DE-1461. Это просто
    /// катастрофа»: на `Cs 137 в домике` `K-40` снят отсевом (z = 0.00), а его
    /// `DE-1461` выжил с z = 64.34 и забрал 5.156 % спектра, сев на 439 кэВ —
    /// в область комптоновского края цезия.
    ///
    /// Два плеча, и второе обязательно — иначе «правило работает» неотличимо
    /// от «правило снесло вылеты вообще»:
    ///
    ///   1. РОДИТЕЛЬ ОТСЕЯН: счётчик
    ///      <see cref="FsaAnalyzer.EscapeOrphansDropped"/> положителен, и ни
    ///      одного образа `SE-*`/`DE-*` осиротевшего родителя в разборе нет.
    ///   2. РОДИТЕЛЬ ЖИВ (положительный контроль): счётчик ноль, образы вылета
    ///      на месте.
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
                Console.Error.WriteLine("нужны --spectrum=<родитель отсеян> и --control=<родитель жив>");
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            Section("1. РОДИТЕЛЬ ОТСЕЯН: вылет уходит вместе с ним", path, setName, nuclides, true);
            Section("2. РОДИТЕЛЬ ЖИВ: вылет на месте (положительный контроль)",
                    control, controlSet, nuclides, false);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static void Section(string title, string path, string setName,
                            NuclideDefinitionManager nuclides, bool expectOrphans)
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
            EnergySpectrum spectrum = rd.EnergySpectrum.Clone();
            EnergySpectrum background = rd.BackgroundEnergySpectrum != null
                ? rd.BackgroundEnergySpectrum.Clone() : null;
            FwhmCalibration fwhm = rd.FwhmCalibration != null ? rd.FwhmCalibration.Clone() : null;
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);

            var analyzer = new FsaAnalyzer();
            options.ApplyTo(analyzer);
            FsaTuningReport.Print(analyzer);

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
                    nuclides.NuclideDefinitions, null, options.AtomicXray);
            }

            var offered = new List<string>();
            var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (FsaComponent component in library)
            {
                if (!string.IsNullOrEmpty(component.EscapeParent))
                {
                    offered.Add(component.Name);
                    parentOf[component.Name] = component.EscapeParent;
                }
            }

            FsaResult result = analyzer.Analyze(spectrum, background, fwhm, library, efficiency);
            if (result == null) { Console.WriteLine("  ⛔ разбор не получился"); bad++; return; }

            var alive = new HashSet<string>(StringComparer.Ordinal);
            foreach (FsaComponentResult component in result.Components)
            {
                alive.Add(component.Name);
            }

            var survivedEscapes = new List<string>();
            var orphanSurvivors = new List<string>();
            foreach (string name in offered)
            {
                if (!alive.Contains(name))
                {
                    continue;
                }

                survivedEscapes.Add(name);
                if (!alive.Contains(parentOf[name]))
                {
                    orphanSurvivors.Add(name + " (родитель " + parentOf[name] + " не дожил)");
                }
            }

            Console.WriteLine("  образов вылета предъявлено {0}, снято сиротами {1}, дожило {2}",
                              offered.Count, analyzer.EscapeOrphansDropped, survivedEscapes.Count);
            Console.WriteLine("  нуклидная доля {0:F2} %, χ²/ndf {1:F3}",
                              result.NuclideSharePercent, result.Chi2Ndf);

            Same("образы вылета вообще строились (иначе плечо ничего не мерит)", true, offered.Count > 0);
            Same("ни один вылет не пережил своего родителя", 0, orphanSurvivors.Count);
            if (orphanSurvivors.Count > 0)
            {
                foreach (string name in orphanSurvivors) Console.WriteLine("      ⛔ {0}", name);
            }

            if (expectOrphans)
            {
                Same("правило сработало: сироты сняты", true, analyzer.EscapeOrphansDropped > 0);
            }
            else
            {
                Same("правило молчит: снимать было некого", 0, analyzer.EscapeOrphansDropped);
                Same("вылеты живого родителя остались в разборе", true, survivedEscapes.Count > 0);
            }
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-58} {2}{3}", ok ? "ok  " : "⛔ ", what, got,
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
