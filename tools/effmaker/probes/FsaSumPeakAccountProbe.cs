using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaSumPeakAccountProbe
{
    /// <summary>
    /// (`A264`) ОТРИСОВКА СУММ-ПИКОВ ПРОТИВ ИХ УЧЁТА. Вопрос Amber 06.09.2026:
    /// уменьшает ли снятие галки сумм-пиков в окне отчёта `counts` того
    /// изотопа, которому эти сумм-пики принадлежат.
    ///
    /// Проба разводит два разных вопроса, которые в постановке звучат одним:
    ///
    ///   (1) ОТРИСОВКА. Подслой сумм-пиков — это `SumPeakCurve`: копия ТОЙ
    ///       ЧАСТИ ленты нуклида, что пришла от сумм-пиков. Плечо «не рисовать»
    ///       снимается тем, что этот подслой у готового результата обнуляется,
    ///       а представление собирается заново ТЕМ ЖЕ построителем, что кормит
    ///       экран (`FsaPresentationBuilder.Build`). Доли, пиковые отсчёты и
    ///       скорость счёта обязаны совпасть ПОБИТОВО.
    ///
    ///   (2) РАСЧЁТ. Единственная галка про сумм-пики в окне
    ///       (`FSAReportView.cascadeSummingCheckBox`) — РАСЧЁТНАЯ: через
    ///       `FsaCalculationOptions.CascadeSumming` она пишет ОБЕ половины
    ///       каскадного суммирования (`CascadeSumming` + `CascadeSumPeaks`), и
    ///       модель становится другой. Это плечо здесь же и служит
    ///       ПОЛОЖИТЕЛЬНЫМ КОНТРОЛЕМ: если и оно даёт побитово те же числа,
    ///       значит проба не мерит ничего.
    ///
    /// Второй положительный контроль — `--drop=<имя образа>`: компонент
    /// вынимается из библиотеки, и числа обязаны разойтись у остальных.
    ///
    ///   fsasumpeakaccountprobe --spectrum=X.xml [--efficiency=Цилиндр]
    ///                          [--set=Имя] [--drop=Имя] [--matrix-any] [--no-matrix]
    ///
    /// `--matrix-any` берёт матрицу отклика, чей отпечаток с геометрией спектра
    /// НЕ СОШЁЛСЯ (склад корпуса прежней версии физики). Нужен затем, что без
    /// матрицы сумм-пики не строятся вовсе и опыт мерил бы пустоту; числа
    /// такого прогона годятся ТОЛЬКО для сравнения плеч между собой и
    /// корпусными не являются.
    ///
    /// Запускать из рабочего каталога корпуса (`mk_appwd.ps1`).
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

            string spectrumPath = null, efficiencyName = null, setName = null, dropName = null;
            bool needMatrix = true, matrixAny = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--efficiency=", StringComparison.Ordinal)) efficiencyName = a.Substring(13);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--drop=", StringComparison.Ordinal)) dropName = a.Substring(7);
                else if (a == "--matrix-any") matrixAny = true;
                else if (a == "--no-matrix") needMatrix = false;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            if (efficiencyName != null && !AttachEfficiency(rd, efficiencyName))
            {
                return 2;
            }

            if (setName != null && !SelectSet(nuclides, setName))
            {
                return 2;
            }

            FWHMPeakDetectionMethodConfig used = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            Console.WriteLine("SETUP\tSNR={0}\tдопуск={1}\tдиапазон={2}…{3} кэВ",
                              used != null ? used.Min_SNR.ToString("G", CultureInfo.InvariantCulture) : "?",
                              used != null ? used.Tolerance.ToString("G", CultureInfo.InvariantCulture) : "?",
                              used != null ? used.Min_Range.ToString("G", CultureInfo.InvariantCulture) : "?",
                              used != null ? used.Max_Range.ToString("G", CultureInfo.InvariantCulture) : "?");

            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
            Console.WriteLine("SETUP\tпиков {0}, компонентов {1}", peaks.Count, library.Count);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста");
                return 1;
            }

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            bool stampOk = matrix != null && rd.Efficiency != null && rd.Efficiency.HasGeometry
                           && matrix.IsValidFor(rd.Efficiency.Geometry);
            if (!stampOk)
            {
                // ⛔ ОТКАЗ НАЗЫВАЕТ СЕБЯ — три разных случая под одной фразой
                // разбирать догадками уже приходилось (`A35`).
                if (matrix == null)
                {
                    Console.WriteLine("матрицы нет ({0}), формат файла {1}", refusal, fileFormat);
                }
                else if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
                {
                    Console.WriteLine("матрица есть, а у кривой спектра НЕТ ГЕОМЕТРИИ");
                }
                else
                {
                    Console.WriteLine("ОТПЕЧАТОК НЕ СОШЁЛСЯ:");
                    Console.WriteLine("  у матрицы          : {0}", matrix.Stamp);
                    Console.WriteLine("  у геометрии спектра: {0}",
                                      ResponseMatrix.ComputeStamp(rd.Efficiency.Geometry, matrix.Options));
                }

                // ⚠ `--matrix-any` берёт УСТАРЕВШУЮ матрицу нарочно: этой пробе
                // нужен вход, на котором сумм-пики СТРОЯТСЯ, а склад корпуса
                // остался прежней версии физики. Числа такого прогона — A/B
                // одного спектра между плечами, и корпусными они НЕ ЯВЛЯЮТСЯ:
                // сравнивать их с базой нельзя.
                if (matrix != null && matrixAny)
                {
                    Console.WriteLine("⚠ --matrix-any: матрица взята НЕСМОТРЯ на отпечаток. Числа этого прогона "
                                      + "годятся ТОЛЬКО для сравнения плеч между собой.");
                }
                else if (needMatrix)
                {
                    Console.Error.WriteLine("отказ: без матрицы сумм-пики не строятся вовсе");
                    return 1;
                }
                else
                {
                    Console.WriteLine("⚠ БЕЗ МАТРИЦЫ: сумм-пиков не будет вовсе, плечи мерят пустоту");
                    matrix = null;
                }
            }

            // ---- сколько сумм-пиков ВООБЩЕ построено ----------------------
            // Без этого числа опыт не имеет смысла: «числа совпали» проходит и
            // на пустоте.
            string scintillator = EfficiencySimulator.ScintillatorNameOf(
                rd.Efficiency != null ? rd.Efficiency.Geometry : null);
            int builtSumPeaks = 0;
            if (matrix != null)
            {
                FsaCascadeSummer summer = FsaCascadeSummer.Create(matrix, scintillator);
                if (summer == null)
                {
                    Console.Error.WriteLine("суммирователь не создан: {0}", FsaCascadeSummer.Failure);
                    return 1;
                }

                Console.WriteLine();
                Console.WriteLine("=== сумм-пики, построенные на этом спектре ===");
                foreach (FsaComponent component in library)
                {
                    FsaCascadeSummer.Correction correction = summer.For(component);
                    int n = correction != null && correction.SumPeaks != null ? correction.SumPeaks.Count : 0;
                    if (n == 0)
                    {
                        continue;
                    }

                    builtSumPeaks += n;
                    Console.WriteLine("SUMPEAKS\t{0}\tлиний {1}\tсумм-пиков {2}",
                                      component.Name, component.Lines.Count, n);
                }

                Console.WriteLine("SUMPEAKS\tВСЕГО\t{0}", builtSumPeaks);
            }

            if (builtSumPeaks == 0)
            {
                Console.Error.WriteLine("⛔ сумм-пиков НЕ ПОСТРОЕНО НИ ОДНОГО — мерить нечего, опыт негоден");
                return 1;
            }

            // ---- плечо A: галка ВКЛ ---------------------------------------
            var onOptions = FsaCalculationOptions.Of(rd);
            onOptions.CascadeSumming = true;
            FsaResult resultOn = Run(rd, library, matrix, scintillator, onOptions, "A: галка ВКЛ");
            if (resultOn == null)
            {
                return 1;
            }

            Snapshot armOn = Snapshot.Of(resultOn, "A: галка ВКЛ, сумм-пики нарисованы");
            armOn.Print();

            double sumPeakCounts = 0.0;
            int layersWithSums = 0;
            foreach (FsaComponentResult c in resultOn.Components)
            {
                if (c.SumPeakCurve == null)
                {
                    continue;
                }

                layersWithSums++;
                double s = 0.0;
                for (int i = 0; i < c.SumPeakCurve.Length; i++)
                {
                    s += c.SumPeakCurve[i];
                }

                sumPeakCounts += s;
                Console.WriteLine("SUBLAYER\t{0}\tотсчётов в подслое сумм-пиков {1}",
                                  c.Name, s.ToString("F3", CultureInfo.InvariantCulture));
            }

            Console.WriteLine("SUBLAYER\tВСЕГО\tслоёв с подслоем {0}, отсчётов {1}",
                              layersWithSums, sumPeakCounts.ToString("F3", CultureInfo.InvariantCulture));
            if (layersWithSums == 0)
            {
                Console.Error.WriteLine("⛔ подслой сумм-пиков не построен ни у одного слоя — опыт негоден");
                return 1;
            }

            // ---- плечо B: те же числа при СНЯТОЙ отрисовке подслоя ---------
            // Обнуляется ровно то, что читает отрисовка и строка «сумм-пики X»
            // в таблице; больше `SumPeakCurve` не читает никто.
            foreach (FsaComponentResult c in resultOn.Components)
            {
                c.SumPeakCurve = null;
            }

            Snapshot armOff = Snapshot.Of(resultOn, "B: подслой сумм-пиков НЕ рисуется");
            armOff.Print();
            Compare("(1) отрисовка подслоя", armOn, armOff, true);

            // ---- положительный контроль 1: РАСЧЁТНАЯ галка ВЫКЛ ------------
            var offOptions = FsaCalculationOptions.Of(rd);
            offOptions.CascadeSumming = false;
            FsaResult resultOff = Run(rd, library, matrix, scintillator, offOptions,
                                      "C: каскадное суммирование ВЫКЛ");
            if (resultOff == null)
            {
                return 1;
            }

            Snapshot armNoSumming = Snapshot.Of(resultOff, "C: каскадное суммирование ВЫКЛ");
            armNoSumming.Print();
            Compare("(2) положительный контроль: расчётная галка", armOn, armNoSumming, false);

            // ---- положительный контроль 2: образ вынут из библиотеки -------
            if (dropName != null)
            {
                var trimmed = new List<FsaComponent>();
                bool dropped = false;
                foreach (FsaComponent c in library)
                {
                    if (string.Equals(c.Name, dropName, StringComparison.OrdinalIgnoreCase))
                    {
                        dropped = true;
                        continue;
                    }

                    trimmed.Add(c);
                }

                if (!dropped)
                {
                    Console.Error.WriteLine("⛔ образа «{0}» в библиотеке нет — контроль не поставлен", dropName);
                    bad++;
                }
                else
                {
                    FsaResult resultDrop = Run(rd, trimmed, matrix, scintillator, onOptions,
                                               "D: образ вынут из библиотеки");
                    if (resultDrop != null)
                    {
                        Snapshot armDrop = Snapshot.Of(resultDrop, "D: образ «" + dropName + "» вынут");
                        armDrop.Print();
                        Compare("(3) положительный контроль: образ вынут", armOn, armDrop, false);
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ИТОГ: всё сошлось" : "ИТОГ: расхождений " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // Снимок величин, которые читает человек
        // ------------------------------------------------------------------

        sealed class Snapshot
        {
            public string Title;

            /// <summary>Строки таблицы отчёта: «имя → напечатанное значение».</summary>
            public readonly List<string> Rows = new List<string>();

            /// <summary>Величины по слою: доля, печатаемая в таблице.</summary>
            public readonly Dictionary<string, double> LayerShare =
                new Dictionary<string, double>(StringComparer.Ordinal);

            /// <summary>Величины по компоненту: пиковые отсчёты, доля, доля пиков, скорость счёта.</summary>
            public readonly Dictionary<string, double[]> Component =
                new Dictionary<string, double[]>(StringComparer.Ordinal);

            public double StackTotal;

            public static Snapshot Of(FsaResult result, string title)
            {
                var s = new Snapshot { Title = title };
                FsaPresentation presentation =
                    FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false);
                foreach (FsaReportRow row in presentation.Rows)
                {
                    s.Rows.Add(row.Kind + "\t" + row.Name + "\t" + row.Value);
                }

                foreach (FsaStackLayer layer in presentation.Layers)
                {
                    s.LayerShare[layer.Name] = layer.SharePercent;
                }

                s.StackTotal = result.StackTotal;
                foreach (FsaComponentResult c in result.Components)
                {
                    s.Component[c.Name] = new[]
                    {
                        c.PeakCounts, c.SharePercent, c.PeakSharePercent, c.CountRate
                    };
                }

                return s;
            }

            public void Print()
            {
                Console.WriteLine();
                Console.WriteLine("=== {0} ===", this.Title);
                Console.WriteLine("StackTotal\t{0}", Bits(this.StackTotal));
                foreach (KeyValuePair<string, double[]> pair in this.Component)
                {
                    Console.WriteLine("COMP\t{0}\tcounts={1}\tдоля={2}\tдоля_пик={3}\tимп/с={4}",
                                      pair.Key, Bits(pair.Value[0]), Bits(pair.Value[1]),
                                      Bits(pair.Value[2]), Bits(pair.Value[3]));
                }

                foreach (string row in this.Rows)
                {
                    Console.WriteLine("ROW\t{0}", row);
                }
            }
        }

        /// <summary>Число и его биты: «побитово совпало» проверяется битами, а не глазами.</summary>
        static string Bits(double v)
        {
            return v.ToString("G17", CultureInfo.InvariantCulture)
                   + " [" + BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture) + "]";
        }

        /// <summary>
        /// Сравнение двух плеч. <paramref name="mustMatch"/> = true — числа
        /// обязаны совпасть побитово; false — плечо положительного контроля, и
        /// расхождение обязано БЫТЬ.
        /// </summary>
        static void Compare(string what, Snapshot a, Snapshot b, bool mustMatch)
        {
            int differ = 0, same = 0;
            var lines = new List<string>();
            foreach (KeyValuePair<string, double[]> pair in a.Component)
            {
                double[] other;
                if (!b.Component.TryGetValue(pair.Key, out other))
                {
                    differ++;
                    lines.Add("    " + pair.Key + ": во втором плече компонента НЕТ");
                    continue;
                }

                for (int k = 0; k < pair.Value.Length; k++)
                {
                    if (BitConverter.DoubleToInt64Bits(pair.Value[k])
                        == BitConverter.DoubleToInt64Bits(other[k]))
                    {
                        same++;
                        continue;
                    }

                    differ++;
                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                                            "    {0}[{1}]: {2} → {3}",
                                            pair.Key, k, Bits(pair.Value[k]), Bits(other[k])));
                }
            }

            foreach (KeyValuePair<string, double> pair in a.LayerShare)
            {
                double other;
                if (!b.LayerShare.TryGetValue(pair.Key, out other))
                {
                    differ++;
                    lines.Add("    слой " + pair.Key + ": во втором плече слоя НЕТ");
                    continue;
                }

                if (BitConverter.DoubleToInt64Bits(pair.Value) == BitConverter.DoubleToInt64Bits(other))
                {
                    same++;
                }
                else
                {
                    differ++;
                    lines.Add("    слой " + pair.Key + ": " + Bits(pair.Value) + " → " + Bits(other));
                }
            }

            if (BitConverter.DoubleToInt64Bits(a.StackTotal) == BitConverter.DoubleToInt64Bits(b.StackTotal))
            {
                same++;
            }
            else
            {
                differ++;
                lines.Add("    StackTotal: " + Bits(a.StackTotal) + " → " + Bits(b.StackTotal));
            }

            Console.WriteLine();
            Console.WriteLine("--- {0}: совпало {1}, разошлось {2} ---", what, same, differ);
            foreach (string line in lines)
            {
                Console.WriteLine(line);
            }

            // Строки таблицы — отдельно: у плеча отрисовки меняться обязан
            // ТОЛЬКО перечень строк «сумм-пики X», и это надо видеть.
            int rowsOnlyInA = 0, rowsOnlyInB = 0;
            var setB = new HashSet<string>(b.Rows, StringComparer.Ordinal);
            var setA = new HashSet<string>(a.Rows, StringComparer.Ordinal);
            foreach (string row in a.Rows)
            {
                if (!setB.Contains(row)) { rowsOnlyInA++; Console.WriteLine("    только в первом плече: " + row); }
            }

            foreach (string row in b.Rows)
            {
                if (!setA.Contains(row)) { rowsOnlyInB++; Console.WriteLine("    только во втором плече: " + row); }
            }

            Console.WriteLine("    строк таблицы: только в первом {0}, только во втором {1}",
                              rowsOnlyInA, rowsOnlyInB);

            if (mustMatch && differ > 0)
            {
                Console.WriteLine("⛔ ОЖИДАЛОСЬ ПОБИТОВОЕ СОВПАДЕНИЕ, а числа разошлись");
                bad++;
            }

            if (!mustMatch && differ == 0)
            {
                Console.WriteLine("⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ НЕ СРАБОТАЛ: числа не изменились — проба мерит пустоту");
                bad++;
            }
        }

        // ------------------------------------------------------------------
        // Разбор
        // ------------------------------------------------------------------

        /// <summary>
        /// Анализатор собирается ТОЧНО так же, как его собирает
        /// `FsaAnalysisSession`, а пользовательские настройки доезжают ЕДИНСТВЕННОЙ
        /// дверью — `FsaCalculationOptions.ApplyTo`.
        /// </summary>
        static FsaResult Run(ResultData rd, List<FsaComponent> library, ResponseMatrix matrix,
                             string scintillator, FsaCalculationOptions options, string title)
        {
            var analyzer = new FsaAnalyzer();
            if (matrix != null)
            {
                analyzer.ResponseMatrix = matrix;
                analyzer.ScintillatorMaterial = scintillator;
            }

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

            options.ApplyTo(analyzer);
            Console.WriteLine();
            Console.WriteLine("### {0}: отпечаток настроек {1}", title, options.Stamp);

            // (`T243`) ЧЕМ СЧИТАЛИ ЭТО ПЛЕЧО — ДО СЧЁТА И ВСЛУХ. Отпечаток
            // строкой выше называет ПОЛЬЗОВАТЕЛЬСКИЕ настройки; здесь — всё
            // расхождение с поставочным разбором, включая взятое у
            // конфигурации прибора и у матрицы. Анализатор у каждого плеча
            // СВОЙ, поэтому состояние прошлого разбора в отчёт не попадает.
            FsaTuningReport.Print(analyzer, title);

            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("разложение не получилось: {0}", title);
                bad++;
                return null;
            }

            Console.WriteLine("chi2/ndf {0}, невязка {1} %, суммирование применено: {2}",
                              result.Chi2Ndf.ToString("F4", CultureInfo.InvariantCulture),
                              (result.ModelResidual * 100.0).ToString("F2", CultureInfo.InvariantCulture),
                              result.CascadeSummingUsed ? "да" : "нет");
            return result;
        }

        // ------------------------------------------------------------------
        // Загрузка — те же правила, что у соседних проб экрана
        // ------------------------------------------------------------------

        static bool SelectSet(NuclideDefinitionManager nuclides, string name)
        {
            var have = new List<string>();
            foreach (NuclideSet set in nuclides.NuclideSets)
            {
                if (set == null)
                {
                    continue;
                }

                have.Add(set.Name);
                if (string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    nuclides.ActiveSet = set;
                    Console.WriteLine("SETUP\tнабор: {0}", set.Name);
                    return true;
                }
            }

            Console.Error.WriteLine("набора «{0}» нет; есть: {1}", name, string.Join(", ", have.ToArray()));
            return false;
        }

        static bool AttachEfficiency(ResultData rd, string name)
        {
            foreach (DeviceConfigInfo device in DeviceConfigManager.GetInstance().DeviceConfigList)
            {
                foreach (EfficiencyConfigData curve in device.EfficiencyConfigs)
                {
                    if (string.Equals(curve.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        rd.Efficiency = curve.Copy();
                        return true;
                    }
                }
            }

            Console.Error.WriteLine("кривая «{0}» не нашлась", name);
            return false;
        }

        static ResultData Load(string path)
        {
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
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            Console.WriteLine("SETUP\tприбор: {0}", ProbeDeviceConfig.Attach(rd));

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                        cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }
    }
}
