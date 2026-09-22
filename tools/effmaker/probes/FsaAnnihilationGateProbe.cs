using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaAnnihilationGateProbe
{
    /// <summary>
    /// СТОРОЖ ГЕЙТА СВОБОДНОГО ОБРАЗА `Ann-511` ПОСЛЕ П127 (`AMBER54`, 22.09.2026).
    ///
    ///     fsaannihilationgateprobe --spectrum=&lt;спектр с пиком 511 и матрицей&gt;
    ///                              --gamma=&lt;ториевый спектр&gt; [--gamma-chain=Th-232]
    ///                              [--anchored=22NA] [--degenerate=18F,40K]
    ///                              [--trace=40K] [--none=137CS] [--no-matrix]
    ///
    /// Запускать ИЗ оснастки корпуса (`tools\CORPUS\scripts\wd_*`): оттуда
    /// берутся приборы спектров и склад матриц, как у `FsaStackShot`.
    ///
    /// ⛔ ЗАЧЕМ. Решение Amber 22.09.2026, вопросником, дословно: «Линию оставить
    /// + починить гейт Ann-511». С П124 в образе β⁺-излучателя лежит своя линия
    /// 511 (`2·ΣI(β⁺)`), и прежний гейт `AMBER7` («любая линия состава у 511 —
    /// снять свободный образ») снимал `Ann-511` у Na-22, Zn-65 и даже у K-40
    /// (β⁺ 0.001 %); избытку 511 от позитронов вне источника стало некуда
    /// идти, активность Na-22 +9…+18 %. Правило теперь из двух частей:
    ///   (а) ГАММА состава в окне `0.7·ПШПВ` у 511 — снять, назвать линию
    ///       (прежний случай Tl-208 510.77);
    ///   (б) β⁺-нуклид без иных линий в полосе фита с выходом не ниже
    ///       `FsaPresentationBuilder.MinTotalYieldPercent` (1 %, `S69`) — снять,
    ///       назвать нуклид словами (`FsaAnalyzer.AnnihilationDegenerate`);
    ///   аннигиляционную линию нуклида с якорем гейт не трогает.
    ///
    /// Пять плеч, каждое — объявленный состав на настоящем спектре:
    ///   1. ЯКОРЬ ЕСТЬ (`--anchored`, по умолчанию 22NA на `--spectrum`): гейт
    ///      молчит, `Ann-511` предъявлен фиту — избыток 511 берёт он;
    ///   2. ГАММА У 511 (`--gamma` + `--gamma-chain`): снят по (а), линия названа,
    ///      `Ann-511` не предъявлен — как до П124, побитово;
    ///   3. ВЫРОЖДЕН (`--degenerate`, по умолчанию 18F,40K): у первого нуклида
    ///      одна аннигиляция, второй даёт линию выше 1022 кэВ (иначе библиотека
    ///      образа 511 не строит) — снят по (б), назван нуклид;
    ///   4. СЛЕДОВОЙ β⁺ (`--trace`, по умолчанию 40K): гейт молчит, образ
    ///      предъявлен — прежде снимался по линии 0.002 %;
    ///   5. БЕЗ 511 ВОВСЕ (`--none`, по умолчанию 137CS): образа нет в
    ///      библиотеке, гейт молчит — отрицательный контроль.
    ///
    /// Независимая мерка к приговору: по каждому нуклиду состава проба сама
    /// читает `ΣI(β⁺)` из `nucdb` и складывает выход линий образа вне окна 511
    /// (гамма-часть, `Intensity − AnnihilationIntensity`) — и предсказывает
    /// (а)/(б)/«молчит» СВОИМ счётом, а потом сверяет с анализатором. Имён
    /// нуклидов в самой пробе нет: кто β⁺ — говорит база, что объявлять —
    /// ключи.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0; иначе код 1; ключи не разобраны — 2.
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей.
            FsaTuningReport.Snapshot();

            string spectrum = null, gamma = null, gammaChain = "Th-232";
            string anchored = "22NA", degenerate = "18F,40K", trace = "40K", none = "137CS";
            bool needMatrix = true;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--gamma-chain=", StringComparison.Ordinal)) gammaChain = a.Substring(14);
                else if (a.StartsWith("--gamma=", StringComparison.Ordinal)) gamma = a.Substring(8);
                else if (a.StartsWith("--anchored=", StringComparison.Ordinal)) anchored = a.Substring(11);
                else if (a.StartsWith("--degenerate=", StringComparison.Ordinal)) degenerate = a.Substring(13);
                else if (a.StartsWith("--trace=", StringComparison.Ordinal)) trace = a.Substring(8);
                else if (a.StartsWith("--none=", StringComparison.Ordinal)) none = a.Substring(7);
                else if (a == "--no-matrix") needMatrix = false;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrum == null || gamma == null)
            {
                Console.Error.WriteLine("нужны --spectrum=<спектр с пиком 511> и --gamma=<ториевый спектр>");
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            Arm("1. ЯКОРЬ ЕСТЬ: аннигиляционная линия нуклида образ не снимает",
                spectrum, anchored, null, needMatrix, Verdict.Silent, true);
            Arm("2. ГАММА У 511: снят по (а), линия названа (как до П124)",
                gamma, null, gammaChain, needMatrix, Verdict.Gamma, false);
            Arm("3. ВЫРОЖДЕН: β⁺ без якоря — снят по (б), нуклид назван",
                spectrum, degenerate, null, needMatrix, Verdict.Degenerate, false);
            Arm("4. СЛЕДОВОЙ β⁺: гейт молчит, образ предъявлен",
                spectrum, trace, null, needMatrix, Verdict.Silent, true);
            Arm("5. БЕЗ 511 ВОВСЕ: образа нет, гейт молчит (отрицательный контроль)",
                spectrum, none, null, needMatrix, Verdict.Silent, null);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        enum Verdict { Silent, Gamma, Degenerate }

        /// <param name="expectOffered">true — `Ann-511` обязан быть предъявлен фиту
        /// (живым либо отсеянным); false — не предъявлен; null — образа в
        /// библиотеке быть не должно вовсе.</param>
        static void Arm(string title, string path, string sample, string chainLabel, bool needMatrix,
                        Verdict expect, bool? expectOffered)
        {
            Console.WriteLine();
            Console.WriteLine("=== {0} ===", title);

            ResultData rd = Load(path);
            if (rd == null) { bad++; return; }

            var nuclides = new List<string>();
            if (!string.IsNullOrEmpty(sample))
            {
                foreach (string raw in sample.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string nucid = FsaSampleLibrary.NucidOf(raw);
                    nuclides.Add(nucid.Length > 0 ? nucid : raw.Trim().ToUpperInvariant());
                }
            }

            var chains = new List<FsaSampleChain>();
            if (!string.IsNullOrEmpty(chainLabel))
            {
                FsaSampleChain chain = FsaSampleChain.FromLabel(chainLabel);
                if (chain == null)
                {
                    Console.WriteLine("  ⛔ метка ряда «{0}» не разобрана", chainLabel);
                    bad++;
                    return;
                }

                chains.Add(chain);
            }

            FsaSampleLibrary.Report built;
            FsaSampleSpec spec = FsaSampleSpec.Declared(rd, chains, nuclides, true, true);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec, out built);
            Console.WriteLine("  состав: {0}{1}; {2}", string.Join(", ", nuclides),
                              chainLabel != null ? " ряд " + chainLabel : "", built);

            bool inLibrary = false;
            foreach (FsaComponent component in library)
            {
                Console.WriteLine("  LIB\t{0}\t{1}\t{2}", component.Name, component.Kind,
                                  component.Lines.Count.ToString(CultureInfo.InvariantCulture));
                if (FsaResult.IsAnnihilationImage(component.Name)) inLibrary = true;
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
                    Console.WriteLine("  ⛔ матрицы нет ({0}) — гейт судится ПРИ матрице; --no-matrix, чтобы смотреть без неё",
                                      matrix == null ? refusal.ToString() : "отпечаток не сошёлся");
                    bad++;
                    return;
                }

                Console.WriteLine("  ⚠ БЕЗ МАТРИЦЫ (--no-matrix): разложение другое, числа не сравнивать");
                matrix = null;
            }

            // Матрица — ТЕМ ЖЕ кодом, что в приложении (`FsaMatrixBinding`, `AMBER12`).
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

            // Окно гейта — та же мерка, что у анализатора: 0.7·ПШПВ на 511.
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            int channels = rd.EnergySpectrum.Spectrum.Length;
            double channel511 = calibration.EnergyToChannel(FsaAnalyzer.AnnihilationKev, channels);
            double fwhmChannels = rd.FwhmCalibration.ChannelToFwhm(channel511);
            double windowKev = 0.7 * Math.Abs(calibration.ChannelToEnergy(channel511 + fwhmChannels)
                                              - calibration.ChannelToEnergy(channel511));
            Console.WriteLine("  окно гейта: ±{0} кэВ у 511", F(windowKev, "F2"));

            // Независимая мерка: своим счётом по библиотеке и базе.
            Verdict predicted = Verdict.Silent;
            string predictedName = null;
            foreach (FsaComponent component in library)
            {
                if (component.Kind == FsaComponentKind.Nuisance) continue;
                double gammaAt511 = 0.0, annAt511 = 0.0, anchor = 0.0;
                foreach (FsaLine line in component.Lines)
                {
                    if (Math.Abs(line.Energy - FsaAnalyzer.AnnihilationKev) <= windowKev)
                    {
                        gammaAt511 += line.Intensity - line.AnnihilationIntensity;
                        annAt511 += line.AnnihilationIntensity;
                    }
                    else
                    {
                        anchor += line.Intensity - line.AnnihilationIntensity;
                    }
                }

                // ΣI(β⁺) — своим чтением базы, по каждому нуклиду колонки.
                double betaPlus = 0.0;
                foreach (string nucid in NucidsOf(component))
                {
                    betaPlus += SumBetaPlus(nucid);
                }

                Console.WriteLine("  MEAS\t{0}\tβ⁺ базы {1} %\t511 в образе: аннигиляция {2} %, гамма {3} %\tякорь вне окна {4} %",
                                  component.Name, F(betaPlus, "0.####"), F(annAt511, "0.####"),
                                  F(gammaAt511, "0.####"), F(anchor, "0.###"));
                // У одиночного нуклида линии лежат весом 1: аннигиляция в образе
                // обязана равняться 2·ΣI(β⁺) базы. У ряда члены идут своими
                // долями, и одним числом это не сверить — ряды в этой пробе
                // судятся только по гамме (а).
                if (component.Kind == FsaComponentKind.Single)
                {
                    Same("аннигиляция в образе = 2·ΣI(β⁺) базы (" + component.Name + ")",
                         true, Math.Abs(annAt511 - 2.0 * betaPlus) <= 1e-9 * Math.Max(1.0, annAt511));
                }

                if (gammaAt511 > 0.0 && predicted != Verdict.Gamma)
                {
                    predicted = Verdict.Gamma;
                    predictedName = component.Name;
                }
                else if (annAt511 > 0.0 && anchor < FsaPresentationBuilder.MinTotalYieldPercent
                         && predicted == Verdict.Silent)
                {
                    predicted = Verdict.Degenerate;
                    predictedName = component.Name;
                }
            }

            if (!inLibrary)
            {
                predicted = Verdict.Silent;
                predictedName = null;
            }

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

            bool live = false, suppressed = false;
            foreach (FsaComponentResult component in result.Components)
            {
                if (FsaResult.IsAnnihilationImage(component.Name)) live = true;
                if (component.Kind != FsaComponentKind.Nuisance || FsaResult.IsAnnihilationImage(component.Name))
                {
                    Console.WriteLine("  ROW\t{0}\t{1}\tдоля {2} %\tz {3}\tраспадов/с {4}", component.Name, component.Kind,
                                      F(component.SharePercent, "F3"), F(component.Z, "F2"), F(component.CountRate, "E4"));
                }
            }

            foreach (FsaSuppressedImage image in result.SuppressedImages)
            {
                if (FsaResult.IsAnnihilationImage(image.Name))
                {
                    suppressed = true;
                    Console.WriteLine("  CUT\t{0}\tz {1}", image.Name, F(image.Z, "F2"));
                }
            }

            Console.WriteLine("  χ²/ndf {0}; матрица {1}; 511 в библиотеке {2}; гейт: {3}; вырожден: {4}",
                              F(result.Chi2Ndf, "F4"), result.ResponseMatrixUsed ? "учтена" : "НЕТ", inLibrary,
                              analyzer.AnnihilationCollides ?? "молчит", analyzer.AnnihilationDegenerate ?? "—");

            Verdict got = analyzer.AnnihilationDegenerate != null ? Verdict.Degenerate
                          : analyzer.AnnihilationCollides != null ? Verdict.Gamma : Verdict.Silent;
            Same("приговор гейта — ожидание плеча", expect.ToString(), got.ToString());
            Same("приговор гейта — независимая мерка пробы", predicted.ToString(), got.ToString());
            if (expect == Verdict.Degenerate)
            {
                Same("назван тот нуклид, что предсказан меркой", predictedName, analyzer.AnnihilationDegenerate);
                Same("текст гейта содержит имя нуклида", true,
                     analyzer.AnnihilationCollides != null && analyzer.AnnihilationDegenerate != null
                     && analyzer.AnnihilationCollides.Contains(analyzer.AnnihilationDegenerate));
            }

            if (expect == Verdict.Gamma)
            {
                Same("текст гейта называет линию компонента", true,
                     analyzer.AnnihilationCollides != null && predictedName != null
                     && analyzer.AnnihilationCollides.StartsWith(predictedName + " ", StringComparison.Ordinal));
            }

            if (expectOffered == null)
            {
                Same("образа 511 в библиотеке нет (линии выше 1022 кэВ нет)", false, inLibrary);
            }
            else
            {
                Same("образ 511 библиотека строит (иначе плечо ничего не мерит)", true, inLibrary);
                Same(expectOffered.Value ? "511 предъявлена фиту (живой либо отсеянной)"
                                         : "511 не предъявлена фиту — ни живой, ни отсеянной",
                     expectOffered.Value, live || suppressed);
            }
        }

        /// <summary>
        /// Нуклиды `nucid`, чьи линии лежат в колонке компонента — по подписи
        /// линий (`FsaLine.Nuclide`, «Na-22» → «22NA»).
        /// </summary>
        static IEnumerable<string> NucidsOf(FsaComponent component)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FsaLine line in component.Lines)
            {
                string nucid = FsaSampleLibrary.NucidOf(line.Nuclide);
                if (nucid.Length > 0 && seen.Add(nucid))
                {
                    yield return nucid;
                }
            }
        }

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-64} {2}{3}", ok ? "ok  " : "⛔ ", what,
                              got ?? "(null)", ok ? string.Empty : "  вместо " + (expected ?? "(null)"));
            if (!ok) bad++;
        }

        static string F(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        static string DatabasePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        }

        static double SumBetaPlus(string nucid)
        {
            double sum = 0.0;
            using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath(),
                Mode = SqliteOpenMode.ReadOnly
            }.ToString()))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select intensity_num from decay_radiations"
                        + " where parent_nucid = $n and type_a = 'B+' and intensity_num > 0"
                        + DecayParentRule.LevelClause;
                    command.Parameters.AddWithValue("$n", nucid);
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (!reader.IsDBNull(0))
                            {
                                sum += reader.GetDouble(0);
                            }
                        }
                    }
                }
            }

            return sum;
        }

        static ResultData Load(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  ⛔ нет файла: " + path);
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

            Console.WriteLine("  {0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));

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
                Console.WriteLine("  ⛔ у спектра нет калибровки ПШПВ или энергии");
                return null;
            }

            return rd;
        }
    }
}
