using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaChainCutEmptySegmentProbeA309
{
    /// <summary>
    /// (`AMBER309`, 05.11.2026) ПУСТОЙ ОТРЕЗОК РЯДА ОБРЫВАЕТ РЯД, НАБРАВШИЙ ПОРОГ ЦЕЛИКОМ.
    ///
    ///     FsaChainCutEmptySegmentProbeA309 [--spectra=&lt;каталог спектров корпуса&gt;] [--spoil=expect]
    ///
    /// Тем же кодом, что приложение (`PeakDetector.DetectPeak` и `FsaCompositionInference.Infer`, источник
    /// «Из NucBase», активного сета нет), на поставочной библиотеке каталога проб.
    ///
    /// Плечи, оба обязательны — иначе «правило работает» неотличимо от «правило режет всё подряд»:
    ///
    ///   1. ИСТОЧНИКИ Th-228: Th-232 принят и ОБОРВАН сверху на Ra-228 (Ac-228 в составе нет).
    ///   2. РАВНОВЕСНЫЙ Th-232: Th-232 принят ЦЕЛИКОМ, в том числе спектр с самой низкой долей Ac-228
    ///      (`RC103_Th232WT20`, 3/12) — правка не имеет права его тронуть.
    ///
    /// Положительный контроль: мутант «снова <c>continue</c> при доле целого выше порога» обязан давать код 1;
    /// `--spoil=expect` переворачивает ожидание первой строки — код 1 без пересборки (`tools/check_chain_cut.py`).
    /// Код: 0 — сошлось, 1 — расхождение, 2 — нечем мерить.
    /// </summary>
    static class Program
    {
        static int bad;
        static bool spoilFirst;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string dir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "CORPUS", "corpus", "spectra"));
            foreach (string a in args)
            {
                if (a.StartsWith("--spectra=", StringComparison.Ordinal)) dir = a.Substring(10);
                else if (a == "--spoil=expect") spoilFirst = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine("МЕРИТЬ НЕЧЕМ: нет каталога спектров " + dir);
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            Console.WriteLine("=== 1. источники Th-228: ряд оборван сверху ===");
            foreach (string name in new[] { "G1S16_Th228_P25", "G1S16_Th228_P5", "G1S24_Th228_P25", "G1S24_Th228_P5", "LaBrBril_Th228" })
            {
                Check(dir, name, nuclides, true);
            }

            Console.WriteLine();
            Console.WriteLine("=== 2. равновесный Th-232: ряд целиком ===");
            foreach (string name in new[] { "RC103_Th232WT20", "AS80_Th232Medal", "ASN16_Th232", "G1S24_Th232_Mar", "G1S16_Th232_Mar", "ASN8_Th232_8192" })
            {
                Check(dir, name, nuclides, false);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            Console.Out.Flush();
            // Форм проба не поднимает, выход обычный
            return bad == 0 ? 0 : 1;
        }

        static void Check(string dir, string name, NuclideDefinitionManager nuclides, bool wantCut)
        {
            string path = Path.Combine(dir, name + ".xml");
            if (!File.Exists(path))
            {
                Console.WriteLine("  ⛔  " + name + ": нет спектра");
                bad++;
                return;
            }

            ResultData rd = Load(path);
            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, null, nuclides.NuclideDefinitions);
            FsaCompositionInference.Report report;
            FsaSampleSpec spec = FsaCompositionInference.Infer(peaks, rd, out report);
            FsaSampleChain chain = null;
            foreach (FsaSampleChain c in spec.Chains)
            {
                if (string.Equals(c.Root, "232TH", StringComparison.OrdinalIgnoreCase)) chain = c;
            }

            if (chain == null)
            {
                Console.WriteLine("  ⛔  " + name + ": Th-232 не принят");
                bad++;
                return;
            }

            var only = new HashSet<string>(chain.Only, StringComparer.OrdinalIgnoreCase);
            bool cut = only.Count > 0 && !only.Contains("228AC");
            string got = cut ? "оборван, без Ac-228" : "целиком";
            if (spoilFirst)
            {
                wantCut = !wantCut;
                spoilFirst = false;
            }

            bool ok = cut == wantCut;
            Console.WriteLine((ok ? "  ok  " : "  ⛔  ") + name + ": " + got);
            if (!ok) bad++;
        }

        static ResultData Load(string path)
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
            var cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (rd.FwhmCalibration == null && cfg != null)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }

            return rd;
        }
    }
}
