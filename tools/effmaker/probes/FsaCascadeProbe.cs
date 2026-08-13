using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaCascadeProbe
{
    /// <summary>
    /// Каскадное суммирование в полноспектральном разборе (TODO F1, п. «г»):
    /// показывает САМИ поправки и меряет, что они делают с разложением.
    ///
    /// Мерит тремя прогонами одного спектра на одной библиотеке:
    ///
    ///   без матрицы            — образ из одних пиков, контрольная точка
    ///   матрица                — образ с континуумом, суммирования нет
    ///   матрица + суммирование — то же плюс CF на пиках и сумм-пики
    ///
    /// Ключ `--no-cascade` у третьего прогона снимается тем же выключателем,
    /// что и в приложении (`FsaAnalyzer.CascadeSumming`), — A/B снимается на
    /// одном и том же коде, а не на двух сборках.
    ///
    ///   fsacascadeprobe --spectrum=X.xml [--efficiency=Цилиндр] [--background=B.xml]
    ///                   [--rebuild] [--lines=12] [--sum-layer-continuum]
    ///
    /// Запускать из каталога с конфигурацией прибора и определениями нуклидов
    /// (`BecquerelMonitor\bin\Debug` или рабочий каталог корпуса).
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, backgroundPath = null, efficiencyName = null, dumpGeometry = null;
            bool rebuild = false, force = false, describe = false, sumLayerContinuum = false;
            int maxLines = 12;
            double scanFrom = 0.0, scanTo = 0.0;
            foreach (string a in args)
            {
                if (a == "--rebuild") { rebuild = true; continue; }
                if (a == "--force") { force = true; continue; }
                if (a == "--describe") { describe = true; continue; }
                if (a == "--sum-layer-continuum") { sumLayerContinuum = true; continue; }
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--background=", StringComparison.Ordinal)) backgroundPath = a.Substring(13);
                else if (a.StartsWith("--efficiency=", StringComparison.Ordinal)) efficiencyName = a.Substring(13);
                else if (a.StartsWith("--dump-geometry=", StringComparison.Ordinal)) dumpGeometry = a.Substring(16);
                else if (a.StartsWith("--scan=", StringComparison.Ordinal))
                {
                    string[] parts = a.Substring(7).Split(':');
                    scanFrom = double.Parse(parts[0], CultureInfo.InvariantCulture);
                    scanTo = double.Parse(parts[1], CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--lines=", StringComparison.Ordinal))
                {
                    maxLines = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
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

            // Геометрия кривой на диск: тем же писателем, каким её пишет
            // приложение. Нужно, чтобы независимая проба `CoincCfProbe` считала
            // CF по ТОЙ ЖЕ геометрии — иначе сверка чисел ничего не значит.
            if (dumpGeometry != null && rd.Efficiency != null && rd.Efficiency.HasGeometry)
            {
                GeometryWriter.Save(rd.Efficiency.Geometry, dumpGeometry);
                Console.WriteLine("геометрия выгружена: {0}", dumpGeometry);
            }

            EnergySpectrum background = backgroundPath != null ? Load(backgroundPath).EnergySpectrum : null;

            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
            Console.WriteLine("пиков {0}, компонентов {1}", peaks.Count, library.Count);
            foreach (Peak peak in peaks)
            {
                // Где стоят ИЗМЕРЕННЫЕ пики — единственный способ отличить
                // «модель съехала» от «в спектре там и правда другое».
                Console.WriteLine("    пик {0,8:F1} кэВ  {1}", peak.Energy,
                                  peak.Nuclide != null ? peak.Nuclide.Name : "(без подписи)");
            }
            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста");
                return 1;
            }

            ResponseMatrix matrix = Matrix(rd, rebuild, force);
            if (matrix == null)
            {
                return 1;
            }

            // ---- сами поправки -------------------------------------------
            string scintillator = EfficiencySimulator.ScintillatorNameOf(
                rd.Efficiency != null ? rd.Efficiency.Geometry : null);
            FsaCascadeSummer summer = FsaCascadeSummer.Create(matrix, scintillator);
            Console.WriteLine("кривая света: {0}",
                              summer != null && summer.LightYieldName.Length > 0
                              ? summer.LightYieldName : "НЕТ — суммы по энергии");
            if (summer == null)
            {
                Console.Error.WriteLine("суммирователь не создан: нет каналов у матрицы или нет nucdb.sqlite");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("=== поправки по компонентам ===");
            int corrected = 0, sumPeaks = 0;
            foreach (FsaComponent component in library)
            {
                FsaCascadeSummer.Correction correction = summer.For(component);
                if (correction == null || !correction.Any)
                {
                    // «Ничего не поправлено» — это ТРИ разных случая, и молча
                    // печатать один прочерк на все три значит однажды принять
                    // поломку за отсутствие каскадов.
                    Console.WriteLine("{0,-14} — {1}", component.Name, Why(summer, component));
                    continue;
                }

                corrected++;
                sumPeaks += correction.SumPeaks.Count;
                Console.WriteLine("{0,-14} линий {1}, сумм-пиков {2}",
                                  component.Name, component.Lines.Count, correction.SumPeaks.Count);

                // Печатаем те линии, что реально поправлены, от сильнейшей.
                var shown = new List<int>();
                for (int i = 0; i < component.Lines.Count; i++)
                {
                    if (Math.Abs(correction.LineFactors[i] - 1.0) > 1.0E-6)
                    {
                        shown.Add(i);
                    }
                }

                shown.Sort((x, y) => component.Lines[y].Intensity.CompareTo(component.Lines[x].Intensity));
                for (int k = 0; k < shown.Count && k < maxLines; k++)
                {
                    FsaLine line = component.Lines[shown[k]];
                    double applied = correction.LineFactors[shown[k]];

                    // Печатаем CF в принятом смысле (A_ист = A_набл · CF), а
                    // рядом — что реально сделано с образом: перепутать эти два
                    // числа местами легко, и один раз это уже случилось.
                    Console.WriteLine("    {0,9:F2} кэВ  I {1,7:F3} %   CF {2:F4}   пик в образе ×{3:F4}",
                                      line.Energy, line.Intensity,
                                      applied > 0.0 ? 1.0 / applied : 0.0, applied);
                }

                if (shown.Count > maxLines)
                {
                    Console.WriteLine("    ... ещё {0}", shown.Count - maxLines);
                }

                foreach (FsaCascadeSummer.SumPeak peak in correction.SumPeaks)
                {
                    Console.WriteLine("    сумм-пик {0,9:F2} кэВ  площадь {1:E3} на распад",
                                      peak.Energy, peak.Area);
                }
            }

            Console.WriteLine();
            Console.WriteLine("компонентов поправлено {0} из {1}, сумм-пиков всего {2}",
                              corrected, library.Count, sumPeaks);

            if (describe)
            {
                // Перечень «что именно посчитано суммой» — то, что у ЛСРМ
                // печатается разделом «Coincidence sum peaks» (F25). Нужен при
                // сверке CF: без пары, породившей сумму, и без раскладки CF на
                // вынос и влёт два числа сравнить нельзя.
                Console.WriteLine();
                Console.WriteLine("=== перечень сумм-пиков и раскладка CF (F25) ===");
                foreach (FsaComponent component in library)
                {
                    Console.WriteLine();
                    Console.Write(summer.Describe(component));
                }
            }

            if (!string.IsNullOrEmpty(FsaCascadeSummer.Failure))
            {
                Console.WriteLine("ОТКАЗ БАЗЫ: {0}", FsaCascadeSummer.Failure);
            }

            // ---- три прогона ---------------------------------------------
            FsaAnalyzer analyzer = new FsaAnalyzer();
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);

            // Наложения меряются отдельно от всего остального: они не каскад.
            analyzer.PileUp = false;

            // Сумм-континуум в подслое отрисовки (S19 «а»): в МОДЕЛИ он есть
            // всегда, ключ только про штриховку. Держится читателем проверки
            // «подслой выше своей ленты» ниже — на этой ветке дефект S37 и
            // проявлялся крупнее всего.
            analyzer.SumLayerIncludesContinuum = sumLayerContinuum;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            FsaResult plain = analyzer.Analyze(rd.EnergySpectrum, background, rd.FwhmCalibration,
                                               library, efficiency);
            double plainMs = clock.Elapsed.TotalMilliseconds;

            analyzer.ResponseMatrix = matrix;
            analyzer.ScintillatorMaterial = scintillator;
            analyzer.CascadeSumming = false;
            clock.Restart();
            FsaResult withMatrix = analyzer.Analyze(rd.EnergySpectrum, background, rd.FwhmCalibration,
                                                    library, efficiency);
            double matrixMs = clock.Elapsed.TotalMilliseconds;

            // Только множитель на пик, без сумм-пиков: половины суммирования
            // делают разное, и мерить их надо порознь.
            analyzer.CascadeSumming = true;
            analyzer.CascadeSumPeaks = false;
            clock.Restart();
            FsaResult cfOnly = analyzer.Analyze(rd.EnergySpectrum, background, rd.FwhmCalibration,
                                                library, efficiency);
            double cfMs = clock.Elapsed.TotalMilliseconds;

            analyzer.CascadeSumPeaks = true;
            clock.Restart();
            FsaResult withCascade = analyzer.Analyze(rd.EnergySpectrum, background, rd.FwhmCalibration,
                                                     library, efficiency);
            double cascadeMs = clock.Elapsed.TotalMilliseconds;

            // И последним — образ случайных наложений поверх всего.
            analyzer.PileUp = true;
            clock.Restart();
            FsaResult withPileUp = analyzer.Analyze(rd.EnergySpectrum, background, rd.FwhmCalibration,
                                                    library, efficiency);
            double pileMs = clock.Elapsed.TotalMilliseconds;

            if (plain == null || withMatrix == null || cfOnly == null || withCascade == null
                || withPileUp == null)
            {
                Console.Error.WriteLine("разложение не получилось");
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("=== случайные наложения ===");
            {
                double liveTime = rd.EnergySpectrum.LiveTime > 0.0
                    ? rd.EnergySpectrum.LiveTime : rd.EnergySpectrum.MeasurementTime;
                Console.WriteLine("загрузка: {0:F0} имп/с ({1:E3} отсчётов за {2:F0} с)",
                                  liveTime > 0.0 ? rd.EnergySpectrum.ValidPulseCount / liveTime : 0.0,
                                  (double)rd.EnergySpectrum.ValidPulseCount, liveTime);
            }

            Console.WriteLine("chi2/ndf без наложений {0:F3}, с наложениями {1:F3}  ({2:F0} мс)",
                              withCascade.Chi2Ndf, withPileUp.Chi2Ndf, pileMs);
            foreach (FsaComponentResult component in withPileUp.Components)
            {
                if (!string.Equals(component.Name, FsaResult.PileUpLayerName, StringComparison.Ordinal))
                {
                    continue;
                }

                // Образ нормирован на полный счёт спектра и знакопеременен
                // (приход минус убыль), поэтому сумма его кривой около нуля —
                // мерить надо АМПЛИТУДУ. Она равна ровно 2*tau*R.
                double live = rd.EnergySpectrum.LiveTime > 0.0
                    ? rd.EnergySpectrum.LiveTime : rd.EnergySpectrum.MeasurementTime;
                double totalCounts = rd.EnergySpectrum.ValidPulseCount;
                double rate = live > 0.0 ? totalCounts / live : 0.0;
                double amplitude = component.CountRate * live;

                double moved = 0.0, net = 0.0;
                foreach (double v in component.Curve)
                {
                    net += v;
                    if (v > 0.0)
                    {
                        moved += v;
                    }
                }

                // Образ поделён на полный счёт, поэтому амплитуда = 2*tau*R*N.
                double tau = rate > 0.0 && totalCounts > 0.0
                    ? amplitude / (2.0 * rate * totalCounts) * 1e6 : 0.0;
                Console.WriteLine("    амплитуда {0:E3} = 2*tau*R*N, z = {1:F1}", amplitude, component.Z);
                Console.WriteLine("    перенесено {0:F0} отсчётов вверх, баланс {1:F0} (должен быть ~0)",
                                  moved, net);
                Console.WriteLine("    скорость {0:F0} имп/с, всего {1:E3}  =>  tau = {2:F3} мкс",
                                  rate, totalCounts, tau);
            }

            Console.WriteLine();
            Console.WriteLine("{0,-16} {1,12} {2,12} {3,12} {4,12}", "",
                              "без матрицы", "матрица", "+только CF", "+сумм-пики");
            Console.WriteLine("{0,-16} {1,12:F3} {2,12:F3} {3,12:F3} {4,12:F3}", "chi2/ndf",
                              plain.Chi2Ndf, withMatrix.Chi2Ndf, cfOnly.Chi2Ndf, withCascade.Chi2Ndf);
            Console.WriteLine("{0,-16} {1,12:F0} {2,12:F0} {3,12:F0} {4,12:F0}", "мс",
                              plainMs, matrixMs, cfMs, cascadeMs);
            Console.WriteLine("{0,-16} {1,12:F5} {2,12:F5} {3,12:F5} {4,12:F5}", "усиление",
                              plain.Gain, withMatrix.Gain, cfOnly.Gain, withCascade.Gain);
            Console.WriteLine("{0,-16} {1,12:F2} {2,12:F2} {3,12:F2} {4,12:F2}", "ноль, каналов",
                              plain.OffsetChannels, withMatrix.OffsetChannels,
                              cfOnly.OffsetChannels, withCascade.OffsetChannels);
            Console.WriteLine("{0,-16} {1,12} {2,12} {3,12} {4,12}", "дрейф на краю",
                              plain.DriftOnGridEdge, withMatrix.DriftOnGridEdge,
                              cfOnly.DriftOnGridEdge, withCascade.DriftOnGridEdge);
            Console.WriteLine("{0,-16} {1,12} {2,12} {3,12} {4,12}", "пометка", "-",
                              withMatrix.CascadeSummingUsed ? "ДА (?!)" : "нет",
                              cfOnly.CascadeSummingUsed ? "да" : "НЕТ (?!)",
                              withCascade.CascadeSummingUsed ? "да" : "НЕТ (?!)");

            Console.WriteLine();
            Console.WriteLine("{0,-16} {1,12} {2,12} {3,12} {4,12}", "слой, %",
                              "без матрицы", "матрица", "+только CF", "+сумм-пики");
            var a1 = plain.BuildStackedLayers(8);
            var a2 = withMatrix.BuildStackedLayers(8);
            var a4 = cfOnly.BuildStackedLayers(8);
            var a3 = withCascade.BuildStackedLayers(8);
            var names = new List<string>();
            foreach (var set in new[] { a1, a2, a4, a3 })
            {
                foreach (var layer in set)
                {
                    if (!names.Contains(layer.Name))
                    {
                        names.Add(layer.Name);
                    }
                }
            }

            foreach (string name in names)
            {
                Console.WriteLine("{0,-16} {1,12} {2,12} {3,12} {4,12}", name,
                                  Share(a1, name), Share(a2, name), Share(a4, name), Share(a3, name));
            }

            // Кривая подслоя сумм-пиков: она рисуется ВНУТРИ ленты нуклида, и
            // выйти за неё не может ни при каких обстоятельствах.
            Console.WriteLine();
            Console.WriteLine("=== подслой сумм-пиков (отрисовка) ===");
            foreach (FsaComponentResult component in withCascade.Components)
            {
                if (component.SumPeakCurve == null)
                {
                    continue;
                }

                double whole = 0.0, sums = 0.0, over = 0.0;
                for (int i = 0; i < component.Curve.Length; i++)
                {
                    whole += component.Curve[i];
                    sums += component.SumPeakCurve[i];
                    if (component.SumPeakCurve[i] > component.Curve[i])
                    {
                        over += component.SumPeakCurve[i] - component.Curve[i];
                    }
                }

                Console.WriteLine("    {0,-14} суммы {1,10:F0} из {2,10:F0} отсчётов = {3,5:F1} %{4}",
                                  component.Name, sums, whole, whole > 0.0 ? 100.0 * sums / whole : 0.0,
                                  over > 0.0 ? "   ВЫШЕ СВОЕЙ ЛЕНТЫ на " + over.ToString("F0") : "");

                // ГДЕ именно подслой вылез за ленту: без места «на 11 отсчётов»
                // не диагноз, а повод гадать. Печатается по убыванию превышения.
                if (over > 0.0)
                {
                    var spots = new List<double[]>();
                    for (int i = 0; i < component.Curve.Length; i++)
                    {
                        double diff = component.SumPeakCurve[i] - component.Curve[i];
                        if (diff > 0.0)
                        {
                            spots.Add(new[] { rd.EnergySpectrum.EnergyCalibration.ChannelToEnergy(i),
                                              diff, component.Curve[i], component.SumPeakCurve[i] });
                        }
                    }

                    spots.Sort((x, y) => y[1].CompareTo(x[1]));
                    for (int k = 0; k < spots.Count && k < 5; k++)
                    {
                        Console.WriteLine("        {0,8:F1} кэВ  лента {1,10:F2}  подслой {2,10:F2}"
                                          + "  превышение {3,8:F2}",
                                          spots[k][0], spots[k][2], spots[k][3], spots[k][1]);
                    }
                }
            }

            // Где модель НЕДОБИРАЕТ — там и надо искать структуру, которой в
            // ней нет. Для каскадов это первым делом тройные суммы: их в
            // модели нет вовсе, а у трёхгаммового каскада (Lu-176) сумма всех
            // трёх стоит отдельным пиком на пустом месте.
            Console.WriteLine();
            Console.WriteLine("=== остаточные превышения (модель со всем) ===");
            TopExcess(rd.EnergySpectrum, withCascade, 12);

            if (scanFrom < scanTo)
            {
                // Прицельно по участку: видно, добрала модель сумм-пик или нет.
                // Три колонки — без сумм-пиков, с ними, и измерение: если пик
                // модели взялся не из сумм, разница между колонками будет нулём.
                Console.WriteLine();
                Console.WriteLine("=== участок {0:F0}–{1:F0} кэВ ===", scanFrom, scanTo);
                Console.WriteLine("    {0,8} {1,12} {2,12} {3,12}",
                                  "кэВ", "измерено", "без налож.", "с налож.");
                Scan(rd.EnergySpectrum, withCascade, withPileUp, scanFrom, scanTo);
            }

            return 0;
        }

        /// <summary>Измерение против двух моделей по окнам участка шкалы.</summary>
        static void Scan(EnergySpectrum spectrum, FsaResult without, FsaResult with,
                         double fromKev, double toKev)
        {
            EnergyCalibration calibration = spectrum.EnergyCalibration;
            int[] raw = spectrum.Spectrum;
            const int width = 8;
            for (int lo = without.FirstChannel; lo + width <= without.LastChannel; lo += width)
            {
                double energy = calibration.ChannelToEnergy(lo + width / 2.0);
                if (energy < fromKev || energy > toKev)
                {
                    continue;
                }

                double measured = 0.0, a = 0.0, b = 0.0;
                for (int i = lo; i < lo + width; i++)
                {
                    measured += raw[i];
                    a += without.Model[i];
                    b += with.Model[i];
                }

                Console.WriteLine("    {0,8:F1} {1,12:F0} {2,12:F0} {3,12:F0}", energy, measured, a, b);
            }
        }

        /// <summary>
        /// Самые крупные превышения измерения над моделью. Само правило живёт
        /// в `ResidualScan` — общем файле на эту пробу и корпусную: две копии
        /// одного счёта однажды разъедутся (S37).
        /// </summary>
        static void TopExcess(EnergySpectrum spectrum, FsaResult result, int top)
        {
            ResidualScan.Print(spectrum, result, top, "    ");
        }

        /// <summary>Почему у компонента нет поправок — по каждому его нуклиду.</summary>
        static string Why(FsaCascadeSummer summer, FsaComponent component)
        {
            var parts = new List<string>();
            var seen = new List<string>();
            foreach (FsaLine line in component.Lines)
            {
                string nuclide = line.Nuclide ?? "";
                if (seen.Contains(nuclide))
                {
                    continue;
                }

                seen.Add(nuclide);
                string nucid = FsaCascadeSummer.Nucid(nuclide);
                if (nucid == null)
                {
                    parts.Add(nuclide + ": имя не разбирается");
                    continue;
                }

                int pairs = summer.PairCount(nuclide);
                if (pairs == 0)
                {
                    parts.Add(nucid + ": каскадов нет");
                    continue;
                }

                int matched = 0, total = 0;
                foreach (FsaLine l in component.Lines)
                {
                    if (!string.Equals(l.Nuclide ?? "", nuclide, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    total++;
                    if (summer.HasLine(nuclide, l.Energy))
                    {
                        matched++;
                    }
                }

                parts.Add(string.Format(CultureInfo.InvariantCulture,
                                        "{0}: пар {1}, линий сошлось {2} из {3}",
                                        nucid, pairs, matched, total));
            }

            return string.Join("; ", parts);
        }

        static string Share(List<FsaStackLayer> layers, string name)
        {
            foreach (var layer in layers)
            {
                if (layer.Name == name)
                {
                    return layer.SharePercent.ToString("F2", CultureInfo.InvariantCulture);
                }
            }

            return "-";
        }

        /// <summary>Кривая по имени из ЖИВОЙ конфигурации прибора, копией.</summary>
        static bool AttachEfficiency(ResultData rd, string name)
        {
            foreach (DeviceConfigInfo device in DeviceConfigManager.GetInstance().DeviceConfigList)
            {
                foreach (EfficiencyConfigData curve in device.EfficiencyConfigs)
                {
                    if (string.Equals(curve.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        rd.Efficiency = curve.Copy();
                        Console.WriteLine("кривая «{0}» из прибора «{1}», геометрия {2}",
                                          curve.Name, device.Name,
                                          rd.Efficiency.HasGeometry ? "есть" : "НЕТ");
                        return true;
                    }
                }
            }

            Console.Error.WriteLine("кривая «{0}» не нашлась ни в одном приборе", name);
            return false;
        }

        /// <summary>Матрица из хранилища по Guid кривой — как её берёт приложение.</summary>
        static ResponseMatrix Matrix(ResultData rd, bool rebuild, bool force)
        {
            string guid = rd.Efficiency != null ? rd.Efficiency.Guid : null;
            ResponseMatrix matrix = ResponseMatrixStore.Load(guid);
            bool valid = matrix != null && rd.Efficiency != null && rd.Efficiency.HasGeometry
                         && matrix.IsValidFor(rd.Efficiency.Geometry);
            Console.WriteLine("матрица: {0}, отпечаток {1}",
                              matrix == null ? "нет" : "есть", valid ? "сошёлся" : "НЕ сошёлся");
            if (!valid && rebuild && rd.Efficiency != null && rd.Efficiency.HasGeometry)
            {
                Console.WriteLine("пересчитываю по геометрии кривой...");
                matrix = ResponseMatrixBuilder.Build(rd.Efficiency.Geometry, new ResponseMatrixOptions(),
                                                     null, System.Threading.CancellationToken.None);
                ResponseMatrixStore.Save(rd.Efficiency.Guid, matrix);
                valid = matrix.IsValidFor(rd.Efficiency.Geometry);
                Console.WriteLine("  за {0:F0} с, отпечаток {1}",
                                  matrix.BuildSeconds, valid ? "сошёлся" : "НЕ сошёлся");
            }

            if (!valid && force && matrix != null)
            {
                // Отпечаток не сошёлся — берём матрицу как есть. Нужно, когда
                // изменилась ФОРМА отпечатка, а не физика: числа в файле те же,
                // а пересчёт стоил бы минут и записи в чужой конфиг. Печатается
                // громко: молчаливое «мимо отпечатка» — способ намерить на
                // матрице от другой геометрии и не заметить.
                Console.WriteLine("  ВНИМАНИЕ: беру матрицу МИМО отпечатка (--force)");
                Console.WriteLine("  физика в файле: {0}, в коде: {1}",
                                  ResponseMatrix.PhysicsFromStamp(matrix.Stamp),
                                  ResponseMatrix.PhysicsVersion);
                return matrix;
            }

            return valid ? matrix : null;
        }

        /// <summary>
        /// Спектр читается ровно так же, как его читает `FsaPaletteProbe`:
        /// вместе с достройкой счёта и ПШПВ-калибровки умолчанием, как это
        /// делает `DocEnergySpectrum` в приложении. Иначе числа двух проб на
        /// одном файле не сойдутся, а разница будет не в том, что мерили.
        /// </summary>
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

            if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig)
                && rd.DeviceConfig != null
                && rd.DeviceConfig.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fromDevice)
            {
                rd.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)fromDevice.Clone();
            }

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
