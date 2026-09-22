using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaLibraryDefaultPathProbe
{
    /// <summary>
    /// ПУТЬ БИБЛИОТЕКИ ПО УМОЛЧАНИЮ ПРОТИВ ПУТИ ИЗ БАЗ на β⁺-спектре
    /// (П130, 22.09.2026, строка `AMBER63`).
    ///
    /// ⛔ ЗАЧЕМ. `DbLookupsForFsa` у прибора ВЫКЛЮЧЕНА умолчанием
    /// (`FWHMPeakDetectionConfig`), и разбор у человека идёт
    /// <see cref="FsaLibrary.BuildFromPeaks"/> — линии берутся из набора
    /// нуклидов, а не из базы. Признака «аннигиляционная часть выхода»
    /// (<see cref="FsaLine.AnnihilationIntensity"/>, П127) набор не несёт, и
    /// правка П124 этот путь не задела. Два следствия, и проба мерит оба:
    ///
    ///   2. НАБОР С 511 — линия 511 в наборе есть (так её кладёт
    ///      `FsaSampleLibrary.AsDefinitions`, так её пишут руками): для гейта
    ///      она «гамма состава» → свободный `Ann-511` снят по правилу (а);
    ///   3. НАБОР БЕЗ 511 — линии 511 в наборе нет (так отдаёт `NucBase`:
    ///      `type_a in ('G','X')`, строк `B+` не читает): в образе β⁺-нуклида
    ///      нет его сильнейшей линии, и весь пик 511 берёт `Ann-511`.
    ///
    /// Плечо 1 — путь ИЗ БАЗ (`FsaSampleLibrary.Build`), опора для сравнения:
    /// там линия 511 помечена аннигиляционной и гейт её не трогает.
    ///
    ///     fsalibrarydefaultpathprobe --spectrum=&lt;файл&gt; --sample=22NA
    ///                                [--chain=Th-232] [--no-matrix] [--out=&lt;tsv&gt;]
    ///
    /// Запускать ИЗ оснастки корпуса (`tools\CORPUS\scripts\wd_*`) — оттуда
    /// берутся приборы спектров и склад матриц, как у `FsaAnnihilationGateProbe`.
    /// Печатаются: линии образа нуклида у 511, приговор гейта, доля и скорость
    /// счёта каждой колонки, χ²/ndf. ⚠ Имён нуклидов в пробе нет: состав
    /// называют ключи, выходы — база. Код 0 — напечатала; 2 — ключи не
    /// разобраны; 1 — спектр или разложение не получились.
    /// </summary>
    static class Program
    {
        static int bad;
        static StringBuilder dump;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            FsaTuningReport.Snapshot();

            string spectrum = null, sample = null, chainLabel = null, outPath = null;
            bool needMatrix = true;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--sample=", StringComparison.Ordinal)) sample = a.Substring(9);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal)) chainLabel = a.Substring(8);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a == "--no-matrix") needMatrix = false;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrum == null || sample == null)
            {
                Console.Error.WriteLine("нужны --spectrum=<файл> и --sample=<нуклиды через запятую>");
                return 2;
            }

            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            dump = new StringBuilder();
            dump.Append("spectrum\tarm\tcomponent\tkind\tshare_pct\tcount_rate\tchi2ndf\n");

            ResultData rd = Load(spectrum);
            if (rd == null) return 1;

            var nuclides = new List<string>();
            foreach (string raw in sample.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string nucid = FsaSampleLibrary.NucidOf(raw);
                nuclides.Add(nucid.Length > 0 ? nucid : raw.Trim());
            }

            var chains = new List<FsaSampleChain>();
            if (!string.IsNullOrEmpty(chainLabel))
            {
                FsaSampleChain chain = FsaSampleChain.FromLabel(chainLabel);
                if (chain == null)
                {
                    Console.Error.WriteLine("метка ряда «" + chainLabel + "» не разобрана");
                    return 2;
                }

                chains.Add(chain);
            }

            FsaSampleLibrary.Report built;
            FsaSampleSpec spec = FsaSampleSpec.Declared(rd, chains, nuclides, true, true);
            List<FsaComponent> fromDb = FsaSampleLibrary.Build(spec, out built);
            Console.WriteLine("состав: {0}{1}; {2}", string.Join(", ", nuclides),
                              chainLabel != null ? " ряд " + chainLabel : "", built);

            IDictionary<int, double> crystal = FsaSampleLibrary.CrystalFractionsOf(spec);

            // Набор нуклидов из той же базы — ровно так его собирает корпусный
            // путь для ПОДПИСИ пиков (`CorpusFsaProbe`), и ровно так человек
            // пишет линию 511 рукой: тремя числами, без признака аннигиляции.
            List<NuclideDefinition> withAnnihilation = FsaSampleLibrary.AsDefinitions(fromDb);

            // Тот же набор без линии 511 — каким его отдаёт `NucBase`
            // (`type_a in ('G','X')`, строк `B+` не читает).
            var withoutAnnihilation = new List<NuclideDefinition>();
            int dropped = 0;
            foreach (FsaComponent component in fromDb)
            {
                // ⛔ Свободный образ `Ann-511` в НАБОР не попадает никогда: это
                // образ разбора, а не запись библиотеки нуклидов. Оставленный
                // здесь, он подписывал бы собою пик 511 — и плечо мерило бы
                // подпись пика, а не отсутствие линии у β⁺-нуклида (мерено
                // 22.09.2026: колонка `Ann-511` становилась `Single` и шла в
                // «пирог» долей).
                if (FsaResult.IsAnnihilationImage(component.Name))
                {
                    dropped += component.Lines.Count;
                    continue;
                }

                foreach (FsaLine line in component.Lines)
                {
                    if (line.AnnihilationIntensity > 0.0)
                    {
                        dropped++;
                        continue;
                    }

                    var definition = new NuclideDefinition();
                    definition.Name = component.Kind == FsaComponentKind.Chain
                                      && !string.IsNullOrEmpty(line.Nuclide) ? line.Nuclide : component.Name;
                    definition.Energy = line.Energy;
                    definition.Intencity = line.Intensity;
                    definition.Visible = true;
                    withoutAnnihilation.Add(definition);
                }
            }

            Console.WriteLine("набор из базы: {0} записей; без аннигиляционных {1} (снято {2})",
                              withAnnihilation.Count.ToString(CultureInfo.InvariantCulture),
                              withoutAnnihilation.Count.ToString(CultureInfo.InvariantCulture),
                              dropped.ToString(CultureInfo.InvariantCulture));

            Arm(rd, spectrum, "1. ИЗ БАЗ (DbLookupsForFsa=вкл)", fromDb, needMatrix);

            List<Peak> peaks2 = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, null, withAnnihilation);
            Console.WriteLine();
            Console.WriteLine("пиков найдено по набору С 511: {0}",
                              peaks2.Count.ToString(CultureInfo.InvariantCulture));
            Arm(rd, spectrum, "2. УМОЛЧАНИЕ, НАБОР С 511",
                FsaLibrary.BuildFromPeaks(peaks2, withAnnihilation, crystal, true), needMatrix);

            List<Peak> peaks3 = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, null, withoutAnnihilation);
            Console.WriteLine();
            Console.WriteLine("пиков найдено по набору БЕЗ 511: {0}",
                              peaks3.Count.ToString(CultureInfo.InvariantCulture));
            Arm(rd, spectrum, "3. УМОЛЧАНИЕ, НАБОР БЕЗ 511",
                FsaLibrary.BuildFromPeaks(peaks3, withoutAnnihilation, crystal, true), needMatrix);

            if (outPath != null)
            {
                File.WriteAllText(outPath, dump.ToString(), new UTF8Encoding(false));
                Console.WriteLine();
                Console.WriteLine("таблица колонок: {0}", Path.GetFullPath(outPath));
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "НАПЕЧАТАНО" : "ОТКАЗОВ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        static void Arm(ResultData rd, string spectrum, string title,
                        List<FsaComponent> library, bool needMatrix)
        {
            Console.WriteLine();
            Console.WriteLine("=== {0} ===", title);

            bool inLibrary = false;
            foreach (FsaComponent component in library)
            {
                if (FsaResult.IsAnnihilationImage(component.Name)) inLibrary = true;
                foreach (FsaLine line in component.Lines)
                {
                    if (Math.Abs(line.Energy - FsaAnalyzer.AnnihilationKev) <= 1.5)
                    {
                        Console.WriteLine("  LINE\t{0}\t{1}\tE {2} кэВ\tвыход {3} %\tиз них аннигиляция {4} %",
                                          component.Name, component.Kind, F(line.Energy, "F2"),
                                          F(line.Intensity, "F4"), F(line.AnnihilationIntensity, "F4"));
                    }
                }
            }

            var analyzer = new FsaAnalyzer();
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            if (matrix == null || rd.Efficiency == null || !rd.Efficiency.HasGeometry
                || !matrix.IsValidFor(rd.Efficiency.Geometry))
            {
                if (needMatrix)
                {
                    Console.WriteLine("  ⛔ матрицы нет ({0}) — числа судятся ПРИ матрице; --no-matrix, чтобы смотреть без неё",
                                      matrix == null ? refusal.ToString() : "отпечаток не сошёлся");
                    bad++;
                    return;
                }

                Console.WriteLine("  ⚠ БЕЗ МАТРИЦЫ (--no-matrix): разложение другое, числа не сравнивать");
                matrix = null;
            }

            FsaMatrixBinding.Bind(analyzer, rd.Efficiency != null ? rd.Efficiency.Geometry : null, matrix);
            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            // (`T243`) Настройки разбора — в вывод, ДО счёта: числа плеча
            // читаются только вместе с тем, чем они получены.
            FsaTuningReport.Print(analyzer);
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum.Clone(),
                rd.BackgroundEnergySpectrum != null ? rd.BackgroundEnergySpectrum.Clone() : null,
                rd.FwhmCalibration.Clone(), library, FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.WriteLine("  ⛔ разложение не получилось: {0}{1}", analyzer.Refusal,
                                  analyzer.RefusalNote != null ? " — " + analyzer.RefusalNote : "");
                bad++;
                return;
            }

            foreach (FsaComponentResult component in result.Components)
            {
                if (component.Kind != FsaComponentKind.Nuisance
                    || FsaResult.IsAnnihilationImage(component.Name))
                {
                    Console.WriteLine("  ROW\t{0}\t{1}\tдоля {2} %\tz {3}\tраспадов/с {4}",
                                      component.Name, component.Kind, F(component.SharePercent, "F3"),
                                      F(component.Z, "F2"), F(component.CountRate, "E6"));
                    dump.AppendFormat(CultureInfo.InvariantCulture, "{0}\t{1}\t{2}\t{3}\t{4:R}\t{5:R}\t{6:R}\n",
                                      Path.GetFileNameWithoutExtension(spectrum), title, component.Name,
                                      component.Kind, component.SharePercent, component.CountRate, result.Chi2Ndf);
                }
            }

            foreach (FsaSuppressedImage image in result.SuppressedImages)
            {
                Console.WriteLine("  CUT\t{0}\tz {1}", image.Name, F(image.Z, "F2"));
            }

            Console.WriteLine("  χ²/ndf {0}; матрица {1}; образ 511 в библиотеке {2}; гейт: {3}; вырожден: {4}",
                              F(result.Chi2Ndf, "F4"), result.ResponseMatrixUsed ? "учтена" : "НЕТ", inLibrary,
                              analyzer.AnnihilationCollides ?? "молчит", analyzer.AnnihilationDegenerate ?? "—");
        }

        static string F(double value, string format)
        {
            return double.IsNaN(value) ? "—" : value.ToString(format, CultureInfo.InvariantCulture);
        }

        static ResultData Load(string path)
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

            Console.WriteLine("{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));

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

            if (rd.FwhmCalibration == null || rd.EnergySpectrum == null || rd.EnergySpectrum.EnergyCalibration == null)
            {
                Console.Error.WriteLine("у спектра нет калибровки ПШПВ или энергии");
                return null;
            }

            return rd;
        }
    }
}
