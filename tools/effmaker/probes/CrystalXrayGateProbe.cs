using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace CrystalXrayGateProbe
{
    /// <summary>
    /// СТОРОЖ ГЕЙТА СОБСТВЕННОГО РЕНТГЕНА КРИСТАЛЛА (`AMBER4`).
    ///
    ///     crystalxraygateprobe --spectrum=&lt;файл с матрицей отклика&gt;
    ///                          [--set=&lt;имя нуклидного сета&gt;] [--kalpha=28.61]
    ///
    /// ⛔ ЗАЧЕМ ГЕЙТ. Довод Amber 08.09.2026: «физику не обманешь: если детектор
    /// NaI — должен быть вылет, его не может не быть». В сцинтилляторе
    /// собственный K-рентген кристалла — это ПРОЦЕСС ВЫЛЕТА: линия стоит не на
    /// Kα, а на `E − Kα` у каждого фотопика, и отношение «вылет/фотопик» есть
    /// считаемая функция энергии, а не свободная амплитуда. Значит при живой
    /// матрице отклика свободная колонка `Xray-&lt;вещество&gt;` — второй счёт того
    /// же рода, что сняли ~~`S47`~~ у SE/DE и ~~`A83`~~ у обратного рассеяния.
    ///
    /// Три раздела, и первый — ФИЗИЧЕСКИЙ, без него правило голословно:
    ///
    ///   1. МАТРИЦА НЕСЁТ ВЫЛЕТ. Для линий 88…911 кэВ печатается отношение
    ///      «площадь у `E − Kα` / фотопик»; оно обязано быть заметным внизу
    ///      шкалы и УБЫВАТЬ с энергией. Это ровно то, что говорит опыт
    ///      (вылет — проценты фотопика ниже 160 кэВ и падает выше).
    ///   2. С МАТРИЦЕЙ образ кристалла снят: счётчик
    ///      <see cref="FsaAnalyzer.CrystalXrayDropped"/> положителен, и в
    ///      составе его нет ни среди компонентов, ни среди отсеянных.
    ///   3. БЕЗ МАТРИЦЫ он на месте: счётчик ноль, образ в составе — иначе
    ///      правка сняла бы физику там, где выразить её больше нечем.
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

            string path = null, setName = null;
            double kalpha = 28.61;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--kalpha=", StringComparison.Ordinal))
                    kalpha = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
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

            // ⛔ Сет выбирается ЯВНО: `ActiveSet` — выбор на сеанс, у пробы он
            // пуст, и без него проба меряет ДРУГУЮ библиотеку, чем окно.
            // Цена ошибки измерена 08.09.2026: без сета вывод состава привёл на
            // ториевый спектр `Ba-131`, и разбор той пробы был неверен.
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

            Console.WriteLine("активный сет: {0}",
                              nuclides.ActiveSet != null ? nuclides.ActiveSet.Name : "(нет, все нуклиды)");

            ResultData rd = Load(path, nuclides);
            if (rd == null) return 2;

            EfficiencyConfigData efficiency = rd.Efficiency;
            if (efficiency == null || !efficiency.HasGeometry)
            {
                Console.Error.WriteLine("у спектра нет геометрии — гейт мерить не на чем");
                return 2;
            }

            MatrixSection(efficiency, kalpha);

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            GateSection("2. С МАТРИЦЕЙ: образ кристалла снят", rd, options, efficiency, true);
            GateSection("3. БЕЗ МАТРИЦЫ: образ кристалла на месте", rd, options, efficiency, false);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Раздел 1: вылет K-рентгена в самой матрице. Считается по её строкам,
        /// а не по фиту, — это утверждение о ПРИБОРЕ, и оно не должно зависеть
        /// от того, что лежит на детекторе.
        /// </summary>
        static void MatrixSection(EfficiencyConfigData efficiency, double kalpha)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. МАТРИЦА НЕСЁТ ВЫЛЕТ K-РЕНТГЕНА (физика правила) ===");

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(efficiency.Guid, out refusal, out fileFormat);
            if (matrix == null || !matrix.IsValidFor(efficiency.Geometry))
            {
                Console.WriteLine("  ⛔ матрица не взята (отказ {0}) — раздел мерить нечем", refusal);
                bad++;
                return;
            }

            double bin = matrix.BinKev > 0.0 ? matrix.BinKev : 2.0;
            int bins = (int)(3200.0 / bin);
            double[] energies = { 88.0, 122.0, 238.63, 338.32, 583.19, 911.20 };
            var ratios = new List<double>();

            foreach (double energy in energies)
            {
                double[] response = matrix.Evaluate(energy, bins);
                int peakBin = (int)Math.Round(energy / bin);
                int escapeBin = (int)Math.Round((energy - kalpha) / bin);
                if (escapeBin <= 1 || peakBin >= bins - 2)
                {
                    continue;
                }

                double peak = Sum(response, peakBin - 1, peakBin + 1);
                double escape = Sum(response, escapeBin - 1, escapeBin + 1);
                double ratio = peak > 0.0 ? escape / peak : 0.0;
                ratios.Add(ratio);
                Console.WriteLine("  линия {0,7:F2} кэВ: вылет/фотопик {1,6:F2} %", energy, 100.0 * ratio);
            }

            Same("измерено на всех шести линиях", energies.Length, ratios.Count);
            if (ratios.Count == energies.Length)
            {
                Same("внизу шкалы вылет заметен (88 кэВ: больше 1 % фотопика)", true, ratios[0] > 0.01);
                bool falls = true;
                for (int i = 2; i < ratios.Count; i++)
                {
                    if (ratios[i] > ratios[i - 1]) falls = false;
                }

                Same("выше 122 кэВ отношение убывает с энергией", true, falls);
                Same("вверху шкалы вылет мал (911 кэВ: меньше 1 %)", true, ratios[ratios.Count - 1] < 0.01);
            }
        }

        /// <summary>
        /// Разделы 2 и 3: тот же путь, каким считает сеанс разбора, с рычагом
        /// «матрица отклика».
        /// </summary>
        static void GateSection(string title, ResultData rd, FsaCalculationOptions options,
                                EfficiencyConfigData efficiency, bool useMatrix)
        {
            Console.WriteLine();
            Console.WriteLine("=== {0} ===", title);

            EnergySpectrum spectrum = rd.EnergySpectrum.Clone();
            FwhmCalibration fwhm = rd.FwhmCalibration != null ? rd.FwhmCalibration.Clone() : null;
            EfficiencyConfigData config = efficiency.Copy();
            FsaEfficiency curve = FsaEfficiency.FromConfig(config);

            var analyzer = new FsaAnalyzer();
            options.ApplyTo(analyzer);
            FsaTuningReport.Print(analyzer);

            string crystal = EfficiencySimulator.ScintillatorNameOf(config.Geometry);
            if (useMatrix)
            {
                MatrixRefusal refusal;
                int fileFormat;
                ResponseMatrix matrix = ResponseMatrixStore.Load(config.Guid, out refusal, out fileFormat);
                if (matrix != null && matrix.IsValidFor(config.Geometry))
                {
                    analyzer.ResponseMatrix = matrix;
                    analyzer.ScintillatorMaterial = crystal;
                }
            }

            FsaCompositionInference.Report inferred;
            FsaSampleSpec spec = FsaCompositionInference.Infer(
                new List<Peak>(rd.DetectedPeaks), rd, out inferred);
            options.ApplyTo(spec);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);

            int inLibrary = 0;
            foreach (FsaComponent component in library)
            {
                if (component.FromCrystal) inLibrary++;
            }

            FsaResult result = analyzer.Analyze(spectrum, null, fwhm, library, curve);
            if (result == null)
            {
                Console.WriteLine("  ⛔ разбор не получился");
                bad++;
                return;
            }

            bool present = false;
            foreach (FsaComponentResult component in result.Components)
            {
                if (IsCrystalImage(component.Name, crystal)) present = true;
            }

            foreach (FsaSuppressedImage image in result.SuppressedImages)
            {
                if (IsCrystalImage(image.Name, crystal)) present = true;
            }

            Console.WriteLine("  вещество кристалла {0}; образов кристалла в библиотеке {1}; снято гейтом {2}",
                              crystal ?? "(неизвестно)", inLibrary, analyzer.CrystalXrayDropped);
            Same("матрица " + (useMatrix ? "взята" : "не бралась"), useMatrix, result.ResponseMatrixUsed);
            Same("образ кристалла в библиотеке построен (иначе плечо ничего не мерит)", true, inLibrary > 0);
            if (useMatrix)
            {
                Same("гейт снял образ кристалла", true, analyzer.CrystalXrayDropped > 0);
                Same("в разборе его нет ни живым, ни отсеянным", false, present);
            }
            else
            {
                Same("гейт молчит", 0, analyzer.CrystalXrayDropped);
                Same("образ кристалла остался в разборе", true, present);
            }
        }

        /// <summary>
        /// Имя образа кристалла: сведённый зовётся по веществу (`Xray-NaI`),
        /// запасной поэлементный — по символу (`Xray-I`). Здесь по имени судит
        /// только ПЕЧАТЬ пробы; сам гейт судит по флагу, и это разные вещи.
        /// </summary>
        static bool IsCrystalImage(string name, string crystal)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("Xray-", StringComparison.Ordinal))
            {
                return false;
            }

            string tail = name.Substring(5);
            if (!string.IsNullOrEmpty(crystal)
                && string.Equals(tail, crystal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Поэлементный запасной путь: символ элемента входит в имя вещества.
            return !string.IsNullOrEmpty(crystal)
                   && crystal.IndexOf(tail, StringComparison.Ordinal) >= 0;
        }

        static double Sum(double[] values, int from, int to)
        {
            double sum = 0.0;
            for (int i = Math.Max(0, from); i <= Math.Min(values.Length - 1, to); i++) sum += values[i];
            return sum;
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
            Console.WriteLine("SETUP\t{0}: пиков {1}, кривая {2}", Path.GetFileName(path),
                              rd.DetectedPeaks.Count,
                              rd.Efficiency != null ? rd.Efficiency.Name : "(нет)");
            return rd;
        }
    }
}
