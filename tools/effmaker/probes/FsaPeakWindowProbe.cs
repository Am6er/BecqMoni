using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaPeakWindowProbe
{
    /// <summary>
    /// СТОРОЖ МЕРКИ ПИКОВОГО ОКНА (`AMBER62`, П128 22.09.2026).
    ///
    ///     fsapeakwindowprobe --spectrum=&lt;спектр&gt; [--sample=137CS,40K] [--chain=Th-232]
    ///                        [--no-matrix] [--min-share=&lt;%&gt;]
    ///                        [--keep=&lt;[Имя@]кэВ,…&gt;] [--drop=&lt;[Имя@]кэВ,…&gt;] [--lines]
    ///
    /// Запускать ИЗ оснастки (`tools\CORPUS\scripts\wd_*` либо
    /// `tools\fsa_showcase\wd`): оттуда берутся приборы спектра и склад матриц,
    /// как у `FsaStackShot`.
    ///
    /// ⛔ ЗАЧЕМ. До П128 окно ±2 ПШПВ открывалось вокруг КАЖДОЙ линии образа с
    /// ненулевым выходом, как бы мала она ни была, и в «пиковые отсчёты»
    /// компонента входил чужой континуум: у K-40 следовая линия 511 открыла
    /// окно, куда через матрицу вошёл комптон собственной 1460, и предел
    /// обнаружения на экране поехал в полтора раза. Мерка теперь —
    /// <see cref="FsaAnalyzer.PeakWindowMinPeakSharePercent"/> от ожидаемых
    /// пиковых отсчётов САМОЙ ЯРКОЙ линии компонента.
    ///
    /// Три плеча:
    ///   А. МЕРКА — проба считает ожидаемые пиковые отсчёты линий СВОИМ счётом
    ///      (выход линии × выход пикового канала матрицы, который проба берёт
    ///      сама; без матрицы — × эффективность кривой) и сверяет с тем, что
    ///      отдаёт <see cref="FsaAnalyzer.PeakWindowLinePeakCounts"/>. Отношение
    ///      обязано быть единицей с точностью до КАСКАДНОЙ ПОПРАВКИ линии,
    ///      которой у пробы нет, — допуск `--cf-tol` (доля). ⚠ Допуск широк
    ///      нарочно и ТОЧНОСТЬЮ НЕ ЯВЛЯЕТСЯ: плечо доказывает, что мерка есть
    ///      выход × ПИКОВАЯ эффективность, а не выход в одиночку и не выход ×
    ///      полный отклик: те расходятся в разы и порядки, каскадная поправка —
    ///      на десятки процентов (мерено на ториевом ряду).
    ///   Б. МАСКА — проба заново строит маску окон по мерке (±2 ПШПВ вокруг
    ///      каждой заметной линии, дрейф из `result.Gain`/`OffsetChannels`),
    ///      суммирует по ней ОБРАЗ компонента и сверяет с `PeakCounts`
    ///      анализатора. Совпало — анализатор раздаёт окна ровно по мерке;
    ///      ключ `--min-share=` двигает мерку ПРОБЫ, не приложения, и на
    ///      сдвинутой мерке плечо обязано отказать (положительный контроль).
    ///   В. ИМЕНА — `--keep=` линии обязаны окно получить, `--drop=` обязаны не
    ///      получить; судится мерой АНАЛИЗАТОРА, с его же порогом.
    ///
    /// ⚠ Карта нуля по свету (`--anchor-light`) маску сдвигает своей таблицей,
    /// которой у пробы нет: включена — плечо Б не судится, и проба говорит об
    /// этом вслух отказом, а не молчанием.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0; разошлось — 1; ключи не разобраны — 2.
    /// </summary>
    static class Program
    {
        static int bad;
        static bool showAll;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей.
            FsaTuningReport.Snapshot();

            string spectrum = null, sample = null, chainLabels = null, keep = null, drop = null;
            bool needMatrix = true;
            double minShare = FsaAnalyzer.PeakWindowMinPeakSharePercent;
            double cfTol = 1.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrum = a.Substring(11);
                else if (a.StartsWith("--sample=", StringComparison.Ordinal)) sample = a.Substring(9);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal)) chainLabels = a.Substring(8);
                else if (a.StartsWith("--keep=", StringComparison.Ordinal)) keep = a.Substring(7);
                else if (a.StartsWith("--drop=", StringComparison.Ordinal)) drop = a.Substring(7);
                else if (a.StartsWith("--min-share=", StringComparison.Ordinal))
                {
                    if (!double.TryParse(a.Substring(12), NumberStyles.Float, CultureInfo.InvariantCulture, out minShare))
                    {
                        Console.Error.WriteLine("не разобран --min-share=");
                        return 2;
                    }
                }
                else if (a.StartsWith("--cf-tol=", StringComparison.Ordinal))
                {
                    if (!double.TryParse(a.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out cfTol))
                    {
                        Console.Error.WriteLine("не разобран --cf-tol=");
                        return 2;
                    }
                }
                else if (a == "--no-matrix") needMatrix = false;
                else if (a == "--lines") showAll = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrum == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            Run(spectrum, sample, chainLabels, needMatrix, minShare, cfTol,
                Targets(keep), Targets(drop));

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Названные линии ключа: `кэВ` либо `Имя@кэВ`. Имя нужно там, где
        /// линию той же энергии несёт не один образ, — например свободный
        /// мешающий образ и след нуклида на одной и той же энергии: без имени
        /// ключ судил бы не тот образ и молча проходил бы.
        /// </summary>
        static List<KeyValuePair<string, double>> Targets(string raw)
        {
            var list = new List<KeyValuePair<string, double>>();
            if (string.IsNullOrEmpty(raw))
            {
                return list;
            }

            foreach (string part in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string item = part.Trim();
                string who = null;
                int at = item.IndexOf('@');
                if (at >= 0)
                {
                    who = item.Substring(0, at).Trim();
                    item = item.Substring(at + 1).Trim();
                }

                double value;
                if (double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    list.Add(new KeyValuePair<string, double>(who, value));
                }
                else
                {
                    Console.WriteLine("  ⛔ не разобрана энергия «{0}»", part);
                    bad++;
                }
            }

            return list;
        }

        static List<double> Plain(List<KeyValuePair<string, double>> targets)
        {
            var list = new List<double>();
            foreach (KeyValuePair<string, double> pair in targets) list.Add(pair.Value);
            return list;
        }

        static void Run(string path, string sample, string chainLabels, bool needMatrix,
                        double minShare, double cfTol,
                        List<KeyValuePair<string, double>> keep, List<KeyValuePair<string, double>> drop)
        {
            List<double> keepKev = Plain(keep), dropKev = Plain(drop);
            Console.WriteLine("=== {0} ===", Path.GetFileName(path));
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
            if (!string.IsNullOrEmpty(chainLabels))
            {
                foreach (string label in chainLabels.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    FsaSampleChain chain = FsaSampleChain.FromLabel(label.Trim());
                    if (chain == null)
                    {
                        Console.WriteLine("  ⛔ метка ряда «{0}» не разобрана", label);
                        bad++;
                        return;
                    }

                    chains.Add(chain);
                }
            }

            FsaSampleLibrary.Report built;
            FsaSampleSpec spec = FsaSampleSpec.Declared(rd, chains, nuclides, true, true);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec, out built);
            Console.WriteLine("  состав: {0}{1}; {2}", string.Join(", ", nuclides),
                              chainLabels != null ? " ряды " + chainLabels : "", built);

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
                    Console.WriteLine("  ⛔ матрицы нет ({0}) — мерка судится ПРИ матрице; --no-matrix, чтобы смотреть без неё",
                                      matrix == null ? refusal.ToString() : "отпечаток не сошёлся");
                    bad++;
                    return;
                }

                Console.WriteLine("  ⚠ БЕЗ МАТРИЦЫ (--no-matrix): пиковый вес берётся кривой");
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

            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            FsaTuningReport.Print(analyzer);
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum.Clone(),
                rd.BackgroundEnergySpectrum != null ? rd.BackgroundEnergySpectrum.Clone() : null,
                rd.FwhmCalibration.Clone(), library, efficiency);
            if (result == null)
            {
                Console.WriteLine("  ⛔ разложение не получилось: {0}{1}", analyzer.Refusal,
                                  analyzer.RefusalNote != null ? " — " + analyzer.RefusalNote : "");
                bad++;
                return;
            }

            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            FwhmCalibration fwhm = rd.FwhmCalibration;
            int channels = rd.EnergySpectrum.Spectrum.Length;
            Console.WriteLine("  χ²/ndf {0}; матрица {1}; усиление {2}, сдвиг {3} канала; полоса {4}…{5}",
                              F(result.Chi2Ndf, "F4"), result.ResponseMatrixUsed ? "учтена" : "НЕТ",
                              F(result.Gain, "F6"), F(result.OffsetChannels, "F3"),
                              result.FirstChannel.ToString(CultureInfo.InvariantCulture),
                              result.LastChannel.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  мерка приложения {0} %, мерка пробы {1} %",
                              F(FsaAnalyzer.PeakWindowMinPeakSharePercent, "0.###"), F(minShare, "0.###"));

            bool lightMap = !string.IsNullOrEmpty(analyzer.AnchorLightCurve);
            if (lightMap)
            {
                Console.WriteLine("  ⛔ карта нуля по свету включена ({0}) — маску окон проба не восстанавливает, плечо Б не судится",
                                  analyzer.AnchorLightCurve);
                bad++;
            }

            var kept = new Dictionary<string, List<double>>(StringComparer.Ordinal);
            var dropped = new Dictionary<string, List<double>>(StringComparer.Ordinal);

            foreach (FsaComponent component in library)
            {
                double[] mine = MyPeakCounts(component, matrix, efficiency);
                double[] theirs = analyzer.PeakWindowLinePeakCounts(component, efficiency);
                if (theirs == null || mine == null || theirs.Length != mine.Length)
                {
                    Console.WriteLine("  ⛔ {0}: мерка не отдана либо длины разошлись", component.Name);
                    bad++;
                    continue;
                }

                double top = 0.0;
                for (int i = 0; i < theirs.Length; i++)
                {
                    if (theirs[i] > top) top = theirs[i];
                }

                var keepHere = new List<double>();
                var dropHere = new List<double>();
                double worst = 0.0;
                int worstAt = -1;
                Console.WriteLine("  --- {0} ({1}), линий {2}, ярчайшая {3} пиковых отсч./распад",
                                  component.Name, component.Kind,
                                  theirs.Length.ToString(CultureInfo.InvariantCulture), F(top, "E4"));
                for (int i = 0; i < theirs.Length; i++)
                {
                    FsaLine line = component.Lines[i];
                    double share = top > 0.0 ? 100.0 * theirs[i] / top : 0.0;
                    bool kepti = theirs[i] > 0.0
                                 && share >= FsaAnalyzer.PeakWindowMinPeakSharePercent;
                    (kepti ? keepHere : dropHere).Add(line.Energy);

                    double ratio = theirs[i] > 0.0 && mine[i] > 0.0 ? mine[i] / theirs[i]
                                   : (theirs[i] == 0.0 && mine[i] == 0.0 ? 1.0 : double.NaN);
                    double dev = double.IsNaN(ratio) ? double.PositiveInfinity : Math.Abs(ratio - 1.0);
                    if (!(dev <= worst) && !double.IsNaN(dev))
                    {
                        worst = dev;
                        worstAt = i;
                    }

                    if (showAll || !kepti || Named(keepKev, line.Energy) || Named(dropKev, line.Energy))
                    {
                        Console.WriteLine("    LINE\t{0}\t{1} кэВ\tвыход {2} %\tпиковых {3}\tдоля {4} %\t{5}\tмерка пробы/приложения {6}",
                                          line.Nuclide, F(line.Energy, "F3"), F(line.Intensity, "0.####"),
                                          F(theirs[i], "E4"), F(share, "0.####"),
                                          kepti ? "ОКНО" : "снято",
                                          double.IsNaN(ratio) ? "—" : F(ratio, "F6"));
                    }
                }

                if (worstAt >= 0 && !(worst <= cfTol))
                {
                    Console.WriteLine("  ⛔ {0}: мерка пробы разошлась с меркой приложения на {1} % (линия {2} кэВ), допуск {3} %",
                                      component.Name, F(100.0 * worst, "F3"),
                                      F(component.Lines[worstAt].Energy, "F3"), F(100.0 * cfTol, "F1"));
                    bad++;
                }
                else
                {
                    Console.WriteLine("  ok   А. мерка пробы сошлась с мерой приложения, наибольшее расхождение {0} %",
                                      F(100.0 * worst, "F4"));
                }

                kept[component.Name] = keepHere;
                dropped[component.Name] = dropHere;
            }

            // Плечо Б — маска по мерке ПРОБЫ, сумма образа по ней против PeakCounts.
            if (!lightMap)
            {
                foreach (FsaComponentResult row in result.Components)
                {
                    FsaComponent component = ByName(library, row.Name);
                    if (component == null || row.Curve == null)
                    {
                        continue;
                    }

                    double[] weights = analyzer.PeakWindowLinePeakCounts(component, efficiency);
                    if (weights == null)
                    {
                        continue;
                    }

                    double top = 0.0;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        if (weights[i] > top) top = weights[i];
                    }

                    bool[] mask = new bool[channels];
                    bool any = false;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        if (!(weights[i] > 0.0) || !(100.0 * weights[i] >= minShare * top))
                        {
                            continue;
                        }

                        if (Mark(mask, analyzer, component.Lines[i].Energy, calibration, fwhm,
                                 result.Gain, result.OffsetChannels,
                                 result.FirstChannel, result.LastChannel, channels))
                        {
                            any = true;
                        }
                    }

                    if (!any)
                    {
                        Console.WriteLine("  ⚠ {0}: маска пробы пуста — сравнивать нечего", row.Name);
                        continue;
                    }

                    // ⚠ Сумм-пики (`S19`) разбор тоже метит окном, а каскадной
                    // поправки у пробы нет: маски несравнимы по построению. Приговор
                    // плеча поэтому СУЖЕН нарочно (`T226`: судить ровно то,
                    // что сторож исполняет) до одиночных образов без сумм-подслоя;
                    // остальные НАЗЫВАЮТСЯ, а не пропускаются молча.
                    if (row.Kind == FsaComponentKind.Chain || row.SumPeakCurve != null
                        || !string.IsNullOrEmpty(row.ChainRoot))
                    {
                        Console.WriteLine("  ⚠ Б. {0}: ряд либо сумм-пики — окна каскада маска пробы не несёт, плечо не судится (у пробы {1}, у приложения {2})",
                                          row.Name, F(SumMask(mask, row.Curve, result.FirstChannel, result.LastChannel), "F4"),
                                          F(row.PeakCounts, "F4"));
                        continue;
                    }

                    double sum = SumMask(mask, row.Curve, result.FirstChannel, result.LastChannel);

                    double scale = Math.Max(Math.Abs(sum), Math.Abs(row.PeakCounts));
                    bool same = scale <= 0.0 || Math.Abs(sum - row.PeakCounts) <= 1.0E-9 * scale;
                    Console.WriteLine("  {0} Б. {1}: пиковых отсчётов по маске пробы {2}, у приложения {3}",
                                      same ? "ok  " : "⛔ ", row.Name, F(sum, "F4"), F(row.PeakCounts, "F4"));
                    if (!same) bad++;
                }
            }

            // Плечо В — названные линии.
            foreach (KeyValuePair<string, double> target in keep)
            {
                Check(kept, dropped, target, true);
            }

            foreach (KeyValuePair<string, double> target in drop)
            {
                Check(kept, dropped, target, false);
            }

            foreach (FsaCharacteristicLimit limit in result.CharacteristicLimits)
            {
                Console.WriteLine("  LIMIT\t{0}\tобнаружен {1}\tпредел {2} расп./с\tпиковых отсчётов предела {3}",
                                  limit.Name, limit.Detected ? "да" : "нет",
                                  F(limit.DetectionLimitRate, "E5"), F(limit.DetectionLimitPeakCounts, "F3"));
            }

            foreach (FsaComponentResult row in result.Components)
            {
                Console.WriteLine("  ROW\t{0}\t{1}\tдоля {2} %\tпиковых отсчётов {3}\tz {4}",
                                  row.Name, row.Kind, F(row.SharePercent, "F3"),
                                  F(row.PeakCounts, "F2"), F(row.Z, "F2"));
            }
        }

        static double SumMask(bool[] mask, double[] curve, int chLo, int chHi)
        {
            double sum = 0.0;
            for (int i = chLo; i <= chHi; i++)
            {
                if (mask[i]) sum += curve[i];
            }

            return sum;
        }

        /// <summary>Названа ли энергия в списке ключа (допуск полкэВ).</summary>
        static bool Named(List<double> list, double energy)
        {
            foreach (double value in list)
            {
                if (Math.Abs(value - energy) <= 0.5) return true;
            }

            return false;
        }

        static void Check(Dictionary<string, List<double>> kept, Dictionary<string, List<double>> dropped,
                          KeyValuePair<string, double> target, bool mustKeep)
        {
            string who = target.Key;
            double energy = target.Value;
            var found = new List<KeyValuePair<string, bool>>();
            foreach (KeyValuePair<string, List<double>> pair in kept)
            {
                if ((who == null || string.Equals(who, pair.Key, StringComparison.OrdinalIgnoreCase))
                    && Named(pair.Value, energy))
                {
                    found.Add(new KeyValuePair<string, bool>(pair.Key, true));
                }
            }

            foreach (KeyValuePair<string, List<double>> pair in dropped)
            {
                if ((who == null || string.Equals(who, pair.Key, StringComparison.OrdinalIgnoreCase))
                    && Named(pair.Value, energy))
                {
                    found.Add(new KeyValuePair<string, bool>(pair.Key, false));
                }
            }

            if (found.Count == 0)
            {
                Console.WriteLine("  ⛔ В. линии {0} кэВ{1} в образах состава нет вовсе — ключ судить нечем",
                                  F(energy, "F2"), who != null ? " у «" + who + "»" : string.Empty);
                bad++;
                return;
            }

            foreach (KeyValuePair<string, bool> pair in found)
            {
                bool ok = pair.Value == mustKeep;
                Console.WriteLine("  {0} В. {1} кэВ ({2}): {3}, ждали {4}",
                                  ok ? "ok  " : "⛔ ", F(energy, "F2"), pair.Key,
                                  pair.Value ? "ОКНО" : "снято",
                                  mustKeep ? "ОКНО" : "снято");
                if (!ok) bad++;
            }
        }

        static FsaComponent ByName(List<FsaComponent> library, string name)
        {
            foreach (FsaComponent component in library)
            {
                if (string.Equals(component.Name, name, StringComparison.Ordinal)) return component;
            }

            return null;
        }

        /// <summary>
        /// СВОЙ счёт ожидаемых пиковых отсчётов линии: выход на один распад,
        /// умноженный на выход пикового канала матрицы (проба спрашивает
        /// матрицу сама) либо на эффективность кривой. Каскадной поправки у
        /// пробы нет нарочно — на ней и меряется расхождение плеча А.
        /// </summary>
        static double[] MyPeakCounts(FsaComponent component, ResponseMatrix matrix, FsaEfficiency efficiency)
        {
            if (component == null || component.Lines == null) return null;

            double[] counts = new double[component.Lines.Count];
            bool byMatrix = matrix != null && !component.WeightsAreFinal;
            double bin = matrix != null ? matrix.BinKev : 0.0;
            for (int i = 0; i < component.Lines.Count; i++)
            {
                FsaLine line = component.Lines[i];
                if (!(line.Energy > 0.0) || !(line.Intensity > 0.0)) continue;

                double weight = line.Intensity / 100.0;
                if (byMatrix && bin > 0.0)
                {
                    double[] row = new double[(int)(line.Energy / bin + 0.5) + 2];
                    if (matrix.HasChannels)
                    {
                        matrix.AccumulateChannel(row, line.Energy, 1.0,
                                                 (int)EfficiencySimulator.ResponseChannel.Peak);
                    }
                    else
                    {
                        matrix.Accumulate(row, line.Energy, 1.0);
                    }

                    double yield = 0.0;
                    for (int b = 0; b < row.Length; b++) yield += row[b];
                    weight *= yield;
                }
                else if (efficiency != null && !component.WeightsAreFinal)
                {
                    double e = efficiency.Eval(line.Energy);
                    weight = e > 0.0 ? weight * e : 0.0;
                }

                counts[i] = weight > 0.0 ? weight : 0.0;
            }

            return counts;
        }

        /// <summary>
        /// Окно ±2 ПШПВ вокруг линии — тем же правилом, что у разбора. Канал
        /// линии берётся у анализатора (`LinePositionChannel`): карта нуля по
        /// свету (`S169`) своя у каждого разбора, и построй её проба сама —
        /// отказывала бы на карте, а не на мерке окна.
        /// </summary>
        static bool Mark(bool[] mask, FsaAnalyzer analyzer, double energy,
                         EnergyCalibration calibration, FwhmCalibration fwhm,
                         double gain, double offset, int chLo, int chHi, int channels)
        {
            if (!(energy > 0.0)) return false;

            double position = analyzer.LinePositionChannel(calibration, energy, channels);
            if (double.IsNaN(position) || double.IsInfinity(position)) return false;

            double p = gain * position + offset;
            double width = fwhm.ChannelToFwhm(p);
            if (!(width > 0.0) || double.IsNaN(width) || double.IsInfinity(width) || width >= channels)
            {
                return false;
            }

            int from = (int)Math.Floor(p - 2.0 * width);
            int to = (int)Math.Ceiling(p + 2.0 * width);
            if (from < chLo) from = chLo;
            if (to > chHi) to = chHi;
            if (from > to) return false;

            for (int i = from; i <= to; i++) mask[i] = true;
            return true;
        }

        static string F(double value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
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
