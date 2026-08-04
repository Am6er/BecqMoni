using System;
using BecquerelMonitor.Properties;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using BecquerelMonitor.Utils;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Восстановление кривой эффективности регистрации из самих измерений.
    ///
    /// Поставочные монте-карловские кривые считаны для геометрии, которой у
    /// реальной пробы нет, и это видно по фиту: общий χ² с такой кривой лучше,
    /// а рисунок линий одного нуклида — хуже. Здесь кривая берётся не из
    /// расчёта, а из связи, которая держится сама:
    ///
    ///     линии одной цепочки в вековом равновесии обязаны лечь на одну кривую.
    ///
    /// Формально: площадь линии S = A · t · (I/100) · ε(E), где A — активность
    /// родителя цепочки в пробе, t — живое время, I — выход на распад родителя.
    /// Тогда
    ///
    ///     ln( S / (t · I/100) ) = ln A + ln ε(E),
    ///
    /// и ln A — общий сдвиг для всех линий одной цепочки в одном спектре. Он
    /// неизвестен, но одинаков, поэтому уходит в свободный член своей серии, а
    /// форма ln ε(E) = polynom(ln E) остаётся общей для всех серий и
    /// определяется из данных. Задача линейная: неизвестные — по одному сдвигу
    /// на пару (спектр, цепочка) плюс коэффициенты полинома.
    ///
    /// Чего этот метод дать НЕ может — абсолютного уровня кривой. Сдвиг всей
    /// кривой вверх компенсируется одинаковым сдвигом всех ln A вниз, система
    /// вырождена ровно на одну степень свободы. Уровень поэтому берётся либо с
    /// исходной кривой (режим «поправить»), либо с опорной точки, введённой
    /// руками. Форма — измеренная, уровень — привнесённый; в отчёте это
    /// сказано прямо, чтобы никто не принял одно за другое.
    ///
    /// Кривая — на прибор И геометрию. Эффективность полного поглощения зависит
    /// от телесного угла и самопоглощения в пробе, то есть от геометрии не
    /// меньше, чем от кристалла: одна пачка спектров = одна геометрия = одна
    /// кривая. Так же устроены и поставочные ROI-файлы («Nano - marinelli»,
    /// «RadiaCode - cilinder»).
    /// </summary>
    public static class EfficiencyFitter
    {
        /// <summary>Опорная энергия приведения, кэВ: полином строится по ln(E/E0).</summary>
        public const double PivotEnergy = 662.0;

        public static EfficiencyFitResult Run(EfficiencyFitInput input, Action<string> log,
                                              Func<bool> cancelled)
        {
            if (log == null)
            {
                log = delegate { };
            }

            if (cancelled == null)
            {
                cancelled = () => false;
            }

            EfficiencyFitResult result = new EfficiencyFitResult();
            if (input == null || input.SpectrumFiles == null || input.SpectrumFiles.Count == 0)
            {
                result.Error = Resources.EfficiencyMakerNoSpectra;
                return result;
            }

            Dictionary<string, List<EfficiencyLine>> chains = EfficiencyLibrary.BuildChains();
            if (chains.Count == 0)
            {
                result.Error = Resources.EfficiencyMakerNoChains;
                return result;
            }

            List<EfficiencyLine> interference = EfficiencyLibrary.AllKnownLines(chains);
            List<string> wanted = input.Chains != null && input.Chains.Count > 0
                ? input.Chains
                : chains.Keys.ToList();

            foreach (string file in input.SpectrumFiles)
            {
                if (cancelled())
                {
                    result.Error = Resources.EfficiencyMakerCancelled;
                    return result;
                }

                // Поспектральная разметка сильнее общего списка. Если она есть,
                // но для этого файла пуста, спектр пропускается: взять вместо
                // неё общий список значило бы искать в спектре чужие линии — и
                // площадь шума на месте несуществующей линии потянула бы кривую.
                List<string> forFile = wanted;
                if (input.ChainsBySpectrum.Count > 0)
                {
                    if (!input.ChainsBySpectrum.TryGetValue(file, out forFile) || forFile.Count == 0)
                    {
                        log(string.Format(Resources.EfficiencyMakerNoSetForSpectrum,
                                          Path.GetFileNameWithoutExtension(file)));
                        continue;
                    }
                }

                try
                {
                    MeasureSpectrum(file, chains, forFile, interference, input, result, log);
                }
                catch (Exception ex)
                {
                    log(string.Format("{0}: {1}", Path.GetFileNameWithoutExtension(file), ex.Message));
                }
            }

            List<EfficiencyObservation> used = result.Observations.Where(o => o.Accepted).ToList();
            if (used.Count < input.PolynomialOrder + 2)
            {
                result.Error = string.Format(Resources.EfficiencyMakerTooFewLines, used.Count);
                return result;
            }

            Solve(used, input, result, log);
            return result;
        }

        // ------------------------------------------------------------------
        // Площади линий в одном спектре
        // ------------------------------------------------------------------

        static void MeasureSpectrum(string file, Dictionary<string, List<EfficiencyLine>> chains,
                                    List<string> wanted, List<EfficiencyLine> interference,
                                    EfficiencyFitInput input, EfficiencyFitResult result,
                                    Action<string> log)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            ResultData data = LoadResultData(file, input.ResultIndex, input.FallbackDeviceGuid);
            EnergySpectrum spectrum = data.EnergySpectrum;
            FwhmCalibration fwhmCalibration = data.FwhmCalibration;
            EnergyCalibration calibration = spectrum.EnergyCalibration;
            int channels = spectrum.NumberOfChannels;

            double liveTime = spectrum.LiveTime > 0.0 ? spectrum.LiveTime : spectrum.MeasurementTime;
            if (liveTime <= 0.0)
            {
                log(string.Format(Resources.EfficiencyMakerNoLiveTime, name));
                return;
            }

            // Комнатный фон несёт те же торий, радий и калий, что и проба:
            // без вычитания его линии войдут в площади пробы и потянут кривую.
            double[] counts = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                counts[i] = spectrum.Spectrum[i];
            }

            double[] variance = (double[])counts.Clone();
            EnergySpectrum background = data.BackgroundEnergySpectrum;
            if (input.SubtractBackground && background != null && background.Spectrum != null
                && background.NumberOfChannels == channels)
            {
                double backgroundLive = background.LiveTime > 0.0
                    ? background.LiveTime
                    : background.MeasurementTime;
                if (backgroundLive > 0.0)
                {
                    double scale = liveTime / backgroundLive;
                    for (int i = 0; i < channels; i++)
                    {
                        counts[i] -= scale * background.Spectrum[i];
                        variance[i] += scale * scale * background.Spectrum[i];
                    }
                }
            }

            for (int i = 0; i < channels; i++)
            {
                variance[i] = Math.Max(variance[i], 1.0);
            }

            int measured = 0;
            foreach (string chain in wanted)
            {
                List<EfficiencyLine> lines;
                if (!chains.TryGetValue(chain, out lines))
                {
                    continue;
                }

                List<EfficiencyObservation> series = new List<EfficiencyObservation>();
                foreach (EfficiencyLine line in Cluster(lines, calibration, fwhmCalibration,
                                                        channels, input))
                {
                    if (line.Intensity < input.MinIntensity || line.Energy < input.MinEnergy
                        || line.Energy > input.MaxEnergy)
                    {
                        continue;
                    }

                    EfficiencyObservation observation = MeasureLine(
                        name, chain, line, counts, variance, calibration, fwhmCalibration,
                        channels, liveTime, interference, lines, input);
                    if (observation != null)
                    {
                        series.Add(observation);
                    }
                }

                // Серия из одной линии не несёт сведений о форме кривой: её
                // сдвиг ln A подгонится под эту линию точно, и вклад в
                // полином будет нулевой. Такие серии отбрасываются целиком.
                int accepted = series.Count(o => o.Accepted);
                if (accepted < 2)
                {
                    foreach (EfficiencyObservation o in series)
                    {
                        if (o.Accepted)
                        {
                            o.Accepted = false;
                            o.Reason = Resources.EfficiencyMakerReasonLoneLine;
                        }
                    }
                }

                result.Observations.AddRange(series);
                measured += Math.Max(accepted >= 2 ? accepted : 0, 0);
            }

            log(string.Format(Resources.EfficiencyMakerSpectrumDone, name, measured, liveTime));
        }

        /// <summary>
        /// Линии одной цепочки, которые прибор не разделяет, сливаются в одну
        /// наблюдаемую с суммарным выходом и взвешенным по выходу центроидом.
        /// Это ровно то, что детектор и меряет: на 7 % полуширины дублет
        /// 583/609 — один бугор, и «отбросить как наложение» означало бы
        /// выбросить всю среднюю часть шкалы. Отбрасывается только чужое —
        /// линия другого компонента, чью долю в бугре знать неоткуда.
        /// </summary>
        static List<EfficiencyLine> Cluster(List<EfficiencyLine> lines, EnergyCalibration calibration,
                                            FwhmCalibration fwhmCalibration, int channels,
                                            EfficiencyFitInput input)
        {
            List<EfficiencyLine> sorted = lines.OrderBy(l => l.Energy).ToList();
            List<EfficiencyLine> result = new List<EfficiencyLine>();
            List<EfficiencyLine> current = new List<EfficiencyLine>();
            double edge = 0.0;

            Action flush = () =>
            {
                if (current.Count == 0)
                {
                    return;
                }

                double weight = current.Sum(l => l.Intensity);
                result.Add(new EfficiencyLine
                {
                    Nuclide = current.Count == 1
                        ? current[0].Nuclide
                        : string.Join("+", current.Select(l => l.Nuclide).Distinct().ToArray()),
                    Energy = current.Sum(l => l.Energy * l.Intensity) / Math.Max(weight, 1e-12),
                    Intensity = weight
                });
                current.Clear();
            };

            foreach (EfficiencyLine line in sorted)
            {
                double width = FwhmKev(calibration, fwhmCalibration, channels, line.Energy);
                if (!(width > 0.0))
                {
                    continue;
                }

                if (current.Count > 0 && line.Energy - edge > input.MergeFwhm * width)
                {
                    flush();
                }

                current.Add(line);
                edge = line.Energy;
            }

            flush();
            return result;
        }

        static double FwhmKev(EnergyCalibration calibration, FwhmCalibration fwhmCalibration,
                              int channels, double energy)
        {
            double channel = EnergyToChannel(calibration, energy, channels);
            if (double.IsNaN(channel))
            {
                return 0.0;
            }

            double fwhm = fwhmCalibration.ChannelToFwhm(channel);
            return fwhm > 0.0 ? fwhm * Math.Abs(EnergyPerChannel(calibration, channel)) : 0.0;
        }

        /// <summary>
        /// Площадь одиночной линии: профиль фиксированной формы и ширины плюс
        /// линейная подложка, амплитуда и подложка — из взвешенного МНК.
        /// Положение уточняется сканированием, потому что дрейф шкалы в доли
        /// ПШПВ смещает центр и занижает площадь при фиксированном центре.
        /// </summary>
        static EfficiencyObservation MeasureLine(
            string spectrumName, string chain, EfficiencyLine line,
            double[] counts, double[] variance, EnergyCalibration calibration,
            FwhmCalibration fwhmCalibration, int channels, double liveTime,
            List<EfficiencyLine> interference, List<EfficiencyLine> own,
            EfficiencyFitInput input)
        {
            double center = EnergyToChannel(calibration, line.Energy, channels);
            if (double.IsNaN(center) || center < 2.0 || center > channels - 3.0)
            {
                return null;
            }

            double fwhm = fwhmCalibration.ChannelToFwhm(center);
            if (!(fwhm > 0.0) || double.IsNaN(fwhm))
            {
                return null;
            }

            EfficiencyObservation observation = new EfficiencyObservation
            {
                Spectrum = spectrumName,
                Chain = chain,
                Nuclide = line.Nuclide,
                Energy = line.Energy,
                Intensity = line.Intensity,
                LiveTime = liveTime,
                Channel = center,
                Fwhm = fwhm
            };

            // Чужие линии в окне. Отбрасывать их нельзя: на сцинтилляторе 583
            // тория и 609 радия всегда рядом, и отказ от наложений выбрасывал
            // почти всю шкалу (проверено: оставалось 13 линий из 169, и все
            // выше 900 кэВ). Поэтому чужая линия входит в то же окно СВОЕЙ
            // свободной колонкой, и площадь получается разделённой фитом.
            // Безнадёжны только неразрешимые пары — ближе трети ПШПВ.
            // «Своя» линия опознаётся по нуклиду, а не по совпадению энергии.
            // В AllKnownLines те же Cs-137 и Co-60 записаны с точностью до
            // третьего знака (661.657, 1173.228, 1332.492), а в базе нуклидов
            // они стоят круглыми (662, 1173, 1332). Сравнение чисел их не
            // отождествляло: линия попадала в окно как ЧУЖАЯ в двух десятых
            // кэВ от себя самой, это ближе трети ПШПВ — и вся цепочка Co-60
            // отбрасывалась как безнадёжное наложение с самой собой. Если
            // нуклид входит в нашу цепочку, все его линии наши по построению.
            HashSet<string> ownNuclides = new HashSet<string>(
                own.Select(l => l.Nuclide ?? ""), StringComparer.OrdinalIgnoreCase);
            HashSet<double> ownEnergies = new HashSet<double>(own.Select(l => l.Energy));
            double perChannel = Math.Abs(EnergyPerChannel(calibration, center));
            double blendWindow = input.BlendFwhm * fwhm * perChannel;
            double fatalWindow = input.UnresolvableFwhm * fwhm * perChannel;
            List<double> neighbours = new List<double>();
            foreach (EfficiencyLine other in interference)
            {
                if (ownEnergies.Contains(other.Energy)
                    || ownNuclides.Contains(other.Nuclide ?? "")
                    || other.Intensity < input.BlendRatio * line.Intensity)
                {
                    continue;
                }

                double gap = Math.Abs(other.Energy - line.Energy);
                if (gap <= fatalWindow)
                {
                    observation.Accepted = false;
                    observation.Reason = string.Format(Resources.EfficiencyMakerReasonBlend,
                        other.Nuclide, other.Energy);
                    return observation;
                }

                if (gap <= blendWindow)
                {
                    double neighbour = EnergyToChannel(calibration, other.Energy, channels);
                    if (!double.IsNaN(neighbour))
                    {
                        neighbours.Add(neighbour);
                    }
                }
            }

            // Соседние колонки почти коллинеарны, если их центры совпадают:
            // близкие чужие линии сливаются в одну колонку.
            neighbours.Sort();
            List<double> columns = new List<double>();
            foreach (double n in neighbours)
            {
                if (columns.Count == 0 || n - columns[columns.Count - 1] > 0.5 * fwhm)
                {
                    columns.Add(n);
                }
            }

            int half = (int)Math.Ceiling(input.WindowFwhm * fwhm);
            int lo = Math.Max(0, (int)Math.Round(center) - half);
            int hi = Math.Min(channels - 1, (int)Math.Round(center) + half);
            if (hi - lo < 5)
            {
                observation.Accepted = false;
                observation.Reason = Resources.EfficiencyMakerReasonWindow;
                return observation;
            }

            double bestChi2 = double.MaxValue;
            double bestArea = 0.0;
            double bestSigma = 0.0;
            double bestCenter = center;
            double search = input.CenterSearchFwhm * fwhm;
            double step = Math.Max(0.1, fwhm / 20.0);
            for (double shift = -search; shift <= search + 1e-9; shift += step)
            {
                double area, sigma, chi2;
                if (!FitPeak(counts, variance, lo, hi, center + shift, columns, shift,
                             fwhm, fwhmCalibration, out area, out sigma, out chi2))
                {
                    continue;
                }

                if (chi2 < bestChi2)
                {
                    bestChi2 = chi2;
                    bestArea = area;
                    bestSigma = sigma;
                    bestCenter = center + shift;
                }
            }

            observation.Channel = bestCenter;
            observation.NetCounts = bestArea;
            observation.NetSigma = bestSigma;
            if (!(bestSigma > 0.0) || !(bestArea > 0.0))
            {
                observation.Accepted = false;
                observation.Reason = Resources.EfficiencyMakerReasonNoPeak;
                return observation;
            }

            observation.Significance = bestArea / bestSigma;
            if (observation.Significance < input.MinSignificance)
            {
                observation.Accepted = false;
                observation.Reason = string.Format(Resources.EfficiencyMakerReasonWeak,
                    observation.Significance);
                return observation;
            }

            observation.Accepted = true;
            observation.LogRatio = Math.Log(bestArea / (liveTime * line.Intensity / 100.0));
            // Систематика формы и подложки не уходит с ростом статистики:
            // без пола веса одна миллионная линия перевесила бы всю кривую.
            double relative = Math.Sqrt(
                1.0 / (observation.Significance * observation.Significance)
                + input.SystematicPercent * input.SystematicPercent / 10000.0);
            observation.Weight = 1.0 / (relative * relative);
            observation.RelativeError = relative;
            return observation;
        }

        /// <summary>
        /// Взвешенный линейный МНК в окне. Колонки: наш профиль, по профилю на
        /// каждую чужую линию в окне, единица и наклон подложки. Возвращает
        /// площадь НАШЕГО профиля и её погрешность из ковариации — то есть уже
        /// с учётом того, что часть бугра принадлежит соседу.
        /// </summary>
        static bool FitPeak(double[] counts, double[] variance, int lo, int hi,
                            double center, List<double> neighbours, double shift,
                            double fwhm, FwhmCalibration fwhmCalibration,
                            out double area, out double sigma, out double chi2)
        {
            area = 0.0;
            sigma = 0.0;
            chi2 = double.MaxValue;

            int n = hi - lo + 1;
            // Подложка: константа, наклон и СТУПЕНЬКА под пиком.
            // Ступенька — не украшение: у сцинтиллятора часть событий линии
            // теряет заряд и садится слева от пика полкой, и без неё эта полка
            // приписывается либо пику (площадь завышена), либо континууму
            // (занижена) в зависимости от того, где линия стоит на комптоне.
            // Именно она давала разброс площадей одной цепочки в разы.
            // Квадратичного члена нет намеренно: со ступенькой он избыточен —
            // парабола повторяет её форму, колонки становятся почти
            // коллинеарными и площадь пика гуляет (χ²/ndf ASN16 146 -> 309).
            int m = 4 + neighbours.Count;
            double[][] basis = new double[m][];
            for (int k = 0; k < m; k++)
            {
                basis[k] = new double[n];
            }

            double shapeSum = 0.0;
            double mid = 0.5 * (lo + hi);
            for (int i = 0; i < n; i++)
            {
                double v = PeakShapeModel.RelativeValue(lo + i - center, fwhm, fwhmCalibration);
                basis[0][i] = v;
                shapeSum += v;
                for (int k = 0; k < neighbours.Count; k++)
                {
                    // Дрейф шкалы общий: соседи двигаются вместе с нашей линией.
                    basis[1 + k][i] = PeakShapeModel.RelativeValue(
                        lo + i - (neighbours[k] + shift), fwhm, fwhmCalibration);
                }

                double t = (lo + i - mid) / Math.Max(n, 1);
                basis[m - 3][i] = 1.0;
                basis[m - 2][i] = t;
            }

            // Ступенька — накопленная справа налево доля профиля: единица
            // слева от пика, ноль справа, переход шириной в сам пик.
            double tail = 0.0;
            for (int i = n - 1; i >= 0; i--)
            {
                tail += basis[0][i];
                basis[m - 1][i] = tail;
            }

            if (tail > 0.0)
            {
                for (int i = 0; i < n; i++)
                {
                    basis[m - 1][i] /= tail;
                }
            }

            if (!(shapeSum > 0.0) || n <= m)
            {
                return false;
            }

            double[,] normal = new double[m, m];
            double[] rhs = new double[m];
            for (int i = 0; i < n; i++)
            {
                double w = 1.0 / variance[lo + i];
                for (int a = 0; a < m; a++)
                {
                    if (basis[a][i] == 0.0)
                    {
                        continue;
                    }

                    for (int b = 0; b < m; b++)
                    {
                        normal[a, b] += w * basis[a][i] * basis[b][i];
                    }

                    rhs[a] += w * basis[a][i] * counts[lo + i];
                }
            }

            double[] solution;
            double[,] inverse;
            if (!SolveWithInverse(normal, rhs, out solution, out inverse) || inverse[0, 0] <= 0.0)
            {
                return false;
            }

            double residual = 0.0;
            for (int i = 0; i < n; i++)
            {
                double model = 0.0;
                for (int a = 0; a < m; a++)
                {
                    model += solution[a] * basis[a][i];
                }

                double d = counts[lo + i] - model;
                residual += d * d / variance[lo + i];
            }

            area = solution[0] * shapeSum;
            sigma = Math.Sqrt(inverse[0, 0]) * shapeSum;
            chi2 = residual / Math.Max(n - m, 1);
            return true;
        }

        /// <summary>Решение и ковариация: Гаусс — Жордан над [A | I | b].</summary>
        static bool SolveWithInverse(double[,] a, double[] b, out double[] x, out double[,] inverse)
        {
            int n = b.Length;
            double[,] work = new double[n, 2 * n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    work[i, j] = a[i, j];
                }

                work[i, n + i] = 1.0;
                work[i, 2 * n] = b[i];
            }

            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < n; row++)
                {
                    if (Math.Abs(work[row, col]) > Math.Abs(work[pivot, col]))
                    {
                        pivot = row;
                    }
                }

                if (Math.Abs(work[pivot, col]) < 1e-30)
                {
                    x = null;
                    inverse = null;
                    return false;
                }

                if (pivot != col)
                {
                    for (int j = 0; j <= 2 * n; j++)
                    {
                        double t = work[col, j];
                        work[col, j] = work[pivot, j];
                        work[pivot, j] = t;
                    }
                }

                double d = work[col, col];
                for (int j = 0; j <= 2 * n; j++)
                {
                    work[col, j] /= d;
                }

                for (int row = 0; row < n; row++)
                {
                    if (row == col || work[row, col] == 0.0)
                    {
                        continue;
                    }

                    double factor = work[row, col];
                    for (int j = 0; j <= 2 * n; j++)
                    {
                        work[row, j] -= factor * work[col, j];
                    }
                }
            }

            x = new double[n];
            inverse = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                x[i] = work[i, 2 * n];
                for (int j = 0; j < n; j++)
                {
                    inverse[i, j] = work[i, n + j];
                }
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Общий фит: сдвиг на серию + полином формы
        // ------------------------------------------------------------------

        /// <summary>
        /// Общий фит с отбраковкой. Один проход недостаточен: линия, севшая на
        /// соседний пик или на структуру континуума, тянет полином за собой, а
        /// целая серия, у которой цепочки в спектре просто нет, тянет его ещё
        /// сильнее. Поэтому — повторные проходы: выброс по робастной сигме
        /// невязок, затем серия целиком, если её собственный разброс больше
        /// допустимого (равновесия нет или нуклида нет).
        /// </summary>
        static void Solve(List<EfficiencyObservation> used, EfficiencyFitInput input,
                          EfficiencyFitResult result, Action<string> log)
        {
            for (int pass = 0; pass < 5; pass++)
            {
                if (used.Count < input.PolynomialOrder + 2)
                {
                    result.Error = string.Format(Resources.EfficiencyMakerTooFewLines, used.Count);
                    return;
                }

                if (!SolveOnce(used, input, result))
                {
                    return;
                }

                // Первые два прохода — только выбросы. Гейт по разбросу серии
                // раньше них выбрасывал почти всё: пока в фите сидит одна
                // съехавшая линия, невязки велики у всех серий сразу.
                int dropped = Reject(used, input, log, pass >= 2);
                if (dropped == 0 && pass >= 2)
                {
                    break;
                }
            }

            Finalize(used, input, result, log);
        }

        static int Reject(List<EfficiencyObservation> used, EfficiencyFitInput input,
                          Action<string> log, bool dropSeries)
        {
            double scale = RobustScale(used.Select(o => o.Residual));
            if (!(scale > 0.0))
            {
                return 0;
            }

            List<EfficiencyObservation> drop = new List<EfficiencyObservation>();
            foreach (var group in used.GroupBy(o => o.SeriesKey).ToList())
            {
                List<EfficiencyObservation> items = group.ToList();
                double spread = RobustScale(items.Select(o => o.Residual));
                bool lone = items.Count < 2;
                if (!lone && !(dropSeries && spread > Math.Log(input.MaxSeriesScatter)))
                {
                    continue;
                }

                foreach (EfficiencyObservation o in items)
                {
                    // У серии, ужавшейся до одной точки, разброса нет и
                    // RobustScale вернул ноль: причина «разброс в 1.0 раза»
                    // читалась бы как исправная серия, выброшенная непонятно
                    // за что. Причина у неё та же, что у одинокой линии.
                    o.Reason = lone
                        ? Resources.EfficiencyMakerReasonLoneLine
                        : string.Format(Resources.EfficiencyMakerReasonSeriesScatter,
                            Math.Exp(spread));
                    drop.Add(o);
                }

                if (!lone)
                {
                    log(string.Format(Resources.EfficiencyMakerSeriesDropped,
                        group.Key, Math.Exp(spread)));
                }
            }

            foreach (EfficiencyObservation o in used)
            {
                if (!drop.Contains(o) && Math.Abs(o.Residual) > input.OutlierSigma * scale)
                {
                    o.Reason = string.Format(Resources.EfficiencyMakerReasonOutlier,
                        o.Residual / scale);
                    drop.Add(o);
                }
            }

            foreach (EfficiencyObservation o in drop)
            {
                o.Accepted = false;
                used.Remove(o);
            }

            return drop.Count;
        }

        /// <summary>Медиана абсолютных отклонений от медианы, приведённая к сигме.</summary>
        static double RobustScale(IEnumerable<double> values)
        {
            List<double> list = values.ToList();
            if (list.Count < 2)
            {
                return 0.0;
            }

            list.Sort();
            double median = list[list.Count / 2];
            List<double> deviations = list.Select(v => Math.Abs(v - median)).ToList();
            deviations.Sort();
            return 1.4826 * deviations[deviations.Count / 2];
        }

        static bool SolveOnce(List<EfficiencyObservation> used, EfficiencyFitInput input,
                              EfficiencyFitResult result)
        {
            List<string> series = used.Select(o => o.SeriesKey).Distinct().ToList();
            int order = Math.Max(1, input.PolynomialOrder);
            int ns = series.Count;
            int m = ns + order;

            double[,] normal = new double[m, m];
            double[] rhs = new double[m];
            foreach (EfficiencyObservation o in used)
            {
                o.SeriesIndex = series.IndexOf(o.SeriesKey);
                double[] row = new double[m];
                row[o.SeriesIndex] = 1.0;
                double u = Math.Log(o.Energy / PivotEnergy);
                for (int k = 0; k < order; k++)
                {
                    row[ns + k] = Math.Pow(u, k + 1);
                }

                for (int a = 0; a < m; a++)
                {
                    if (row[a] == 0.0)
                    {
                        continue;
                    }

                    for (int b = 0; b < m; b++)
                    {
                        normal[a, b] += o.Weight * row[a] * row[b];
                    }

                    rhs[a] += o.Weight * row[a] * o.LogRatio;
                }
            }

            double[] solution;
            if (!SolveSymmetric(normal, rhs, out solution))
            {
                result.Error = Resources.EfficiencyMakerSingular;
                return false;
            }

            result.Coefficients = new double[order];
            Array.Copy(solution, ns, result.Coefficients, 0, order);
            result.SeriesKeys = series;
            result.SeriesOffsets = new double[ns];
            Array.Copy(solution, 0, result.SeriesOffsets, 0, ns);

            double chi2 = 0.0;
            foreach (EfficiencyObservation o in used)
            {
                double model = solution[o.SeriesIndex] + Shape(result.Coefficients, o.Energy);
                o.Residual = o.LogRatio - model;
                chi2 += o.Weight * o.Residual * o.Residual;
            }

            result.Chi2Ndf = chi2 / Math.Max(used.Count - m, 1);

            // Границы измеренного диапазона — не просто крайние линии.
            // Одиночная линия у края (у сцинтиллятора это 90-130 кэВ на
            // комптоновском завале) полином к себе притягивает, и кривая под
            // ней уходит в единицы эффективности. Край признаётся измеренным
            // только там, где рядом, в пределах полутора раз по энергии, есть
            // хотя бы ещё одна линия; всё, что ниже (выше), — экстраполяция,
            // и там кривая идёт по исходной, а не по полиному.
            List<double> energies = used.Select(o => o.Energy).OrderBy(e => e).ToList();
            result.MinEnergy = energies[0];
            for (int i = 0; i + 1 < energies.Count; i++)
            {
                if (energies[i + 1] <= energies[i] * 1.5)
                {
                    result.MinEnergy = energies[i];
                    break;
                }
            }

            result.MaxEnergy = energies[energies.Count - 1];
            for (int i = energies.Count - 1; i > 0; i--)
            {
                if (energies[i - 1] >= energies[i] / 1.5)
                {
                    result.MaxEnergy = energies[i];
                    break;
                }
            }

            if (result.MaxEnergy <= result.MinEnergy)
            {
                result.MinEnergy = energies[0];
                result.MaxEnergy = energies[energies.Count - 1];
            }

            return true;
        }

        static void Finalize(List<EfficiencyObservation> used, EfficiencyFitInput input,
                             EfficiencyFitResult result, Action<string> log)
        {
            if (!string.IsNullOrEmpty(result.Error))
            {
                return;
            }

            result.ReferenceCurve = input.Reference != null && input.Reference.Count >= 2
                ? input.Reference : null;

            // Уровень. Система вырождена на общий сдвиг: он либо снимается с
            // исходной кривой, либо задаётся опорной точкой. Третьего нет.
            //
            // Определяется заново на каждом заходе: Finalize вызывает себя
            // после перефита по «невозможным» точкам, а полином к этому
            // моменту уже другой. Без сброса ветка опорной точки (она под
            // условием LevelSource == None) второй раз не выполнялась, и в
            // файл шёл уровень, посчитанный со старыми коэффициентами.
            result.Level = 0.0;
            result.LevelSource = EfficiencyLevelSource.None;
            if (input.Reference != null && input.Reference.Count >= 2)
            {
                double num = 0.0;
                int count = 0;
                foreach (ROIEfficiencyData point in input.Reference)
                {
                    if (point.Energy < result.MinEnergy || point.Energy > result.MaxEnergy
                        || point.Efficiency <= 0.0)
                    {
                        continue;
                    }

                    num += Math.Log(point.Efficiency) - Shape(result.Coefficients, point.Energy);
                    count++;
                }

                if (count > 0)
                {
                    result.Level = num / count;
                    result.LevelSource = EfficiencyLevelSource.Reference;
                }
            }

            if (result.LevelSource == EfficiencyLevelSource.None
                && input.AnchorEnergy > 0.0 && input.AnchorEfficiency > 0.0)
            {
                result.Level = Math.Log(input.AnchorEfficiency)
                    - Shape(result.Coefficients, input.AnchorEnergy);
                result.LevelSource = EfficiencyLevelSource.Anchor;
            }

            if (result.LevelSource == EfficiencyLevelSource.None)
            {
                // Ни кривой, ни опорной точки: остаётся только форма. Кривая
                // приводится к единице в опорной энергии — это не физический
                // уровень, и так и написано в отчёте.
                result.Level = 0.0;
                result.LevelSource = EfficiencyLevelSource.ShapeOnly;
            }

            foreach (EfficiencyObservation o in used)
            {
                o.MeasuredEfficiency = Math.Exp(o.LogRatio - result.SeriesOffsets[o.SeriesIndex] + result.Level);
            }

            // Эффективность больше единицы невозможна физически: столько
            // событий, сколько испущено, детектор зарегистрировать может, а
            // больше — нет. Пока уровень не известен, проверить это нечем, но
            // как только он взят с кривой или с опорной точки — проверка
            // работает и снимает именно то, что портило низ шкалы: линии
            // 90-130 кэВ на комптоновском завале давали ε в единицы.
            if (result.LevelSource == EfficiencyLevelSource.Reference
                || result.LevelSource == EfficiencyLevelSource.Anchor)
            {
                List<EfficiencyObservation> impossible =
                    used.Where(o => o.MeasuredEfficiency > 1.0).ToList();
                if (impossible.Count > 0 && used.Count - impossible.Count >= input.PolynomialOrder + 2)
                {
                    foreach (EfficiencyObservation o in impossible)
                    {
                        o.Accepted = false;
                        o.Reason = string.Format(Resources.EfficiencyMakerReasonImpossible,
                            o.MeasuredEfficiency);
                        used.Remove(o);
                        log(string.Format(Resources.EfficiencyMakerReasonImpossibleLog,
                            o.Spectrum, o.Energy, o.MeasuredEfficiency));
                    }

                    if (SolveOnce(used, input, result))
                    {
                        Finalize(used, input, result, log);
                        return;
                    }
                }
            }

            result.Curve = BuildCurve(result, input);

            // Кривая обязана быть физически возможной. Полином высокой степени
            // на малом числе линий уходит в разнос: измерено на пачке из двух
            // спектров — степень 6 дала эффективность 1.0 на 2000 кэВ и 1e-6
            // на 2615, степень 7 — единицу и 1e-20 в соседних точках.
            //
            // Ловится именно УПОР В ПОТОЛОК, а не превышение: BuildCurve режет
            // значения по единице, и разнос выходил из фиттера в виде опрятной
            // кривой с полкой 1.0 — та же молчаливая подмена, что и в разборе
            // геометрии. Единица недостижима ни для какой настоящей геометрии:
            // это значило бы, что в пик полного поглощения попадает каждый
            // испущенный квант.
            //
            // Спрашивать это МОЖНО ТОЛЬКО ТАМ, ГДЕ УРОВЕНЬ ФИЗИЧЕСКИЙ. В режиме
            // «только форма» уровень условный: Shape не имеет свободного члена,
            // Level = 0, и кривая по построению равна единице на опорных 662
            // кэВ, а ниже её и подавно превышает — потому BuildCurve там и не
            // режет по единице. Безусловная проверка отвергала В КАЖДОМ таком
            // прогоне заведомо годную кривую, называя условный уровень
            // невозможной физикой.
            if (result.LevelSource != EfficiencyLevelSource.ShapeOnly)
            {
                double worst = 0.0;
                double worstEnergy = 0.0;
                foreach (ROIEfficiencyData point in result.Curve)
                {
                    bool broken = double.IsNaN(point.Efficiency) || double.IsInfinity(point.Efficiency);
                    if (broken || point.Efficiency > worst)
                    {
                        worst = broken ? double.PositiveInfinity : point.Efficiency;
                        worstEnergy = point.Energy;
                    }
                }

                if (!(worst < 1.0))
                {
                    result.Curve = new List<ROIEfficiencyData>();
                    result.Error = string.Format(Resources.EfficiencyMakerImpossibleCurve,
                                                 worstEnergy, worst, used.Count,
                                                 result.SeriesKeys.Count + Math.Max(1, input.PolynomialOrder));
                    return;
                }
            }

            log(string.Format(Resources.EfficiencyMakerFitDone,
                used.Count, result.SeriesKeys.Count, result.Chi2Ndf,
                result.MinEnergy, result.MaxEnergy));
        }

        public static double Shape(double[] coefficients, double energy)
        {
            double u = Math.Log(Math.Max(energy, 1e-6) / PivotEnergy);
            double sum = 0.0;
            for (int k = 0; k < coefficients.Length; k++)
            {
                sum += coefficients[k] * Math.Pow(u, k + 1);
            }

            return sum;
        }

        /// <summary>
        /// Кривая в точке. За пределами диапазона измеренных линий полином не
        /// продолжается: кубика по ln E хватает, чтобы за декаду вниз уйти на
        /// четыре порядка вверх (проверено: 3·10⁴ на 60 кэВ при 10⁻² на 662).
        /// Снаружи кривая идёт по исходной, сшитой по уровню на границе, а если
        /// исходной нет — держится константой, как это и делает FsaEfficiency.
        /// </summary>
        public static double Evaluate(EfficiencyFitResult result, double energy)
        {
            double clamped = Math.Min(Math.Max(energy, result.MinEnergy), result.MaxEnergy);
            double inside = Math.Exp(result.Level + Shape(result.Coefficients, clamped));
            if (Math.Abs(clamped - energy) < 1e-9 || result.ReferenceCurve == null)
            {
                return inside;
            }

            double atEdge = InterpolateLogLog(result.ReferenceCurve, clamped);
            double atPoint = InterpolateLogLog(result.ReferenceCurve, energy);
            if (!(atEdge > 0.0) || !(atPoint > 0.0))
            {
                return inside;
            }

            return inside * atPoint / atEdge;
        }

        static double InterpolateLogLog(List<ROIEfficiencyData> curve, double energy)
        {
            if (curve == null || curve.Count < 2)
            {
                return 0.0;
            }

            if (energy <= curve[0].Energy)
            {
                return curve[0].Efficiency;
            }

            if (energy >= curve[curve.Count - 1].Energy)
            {
                return curve[curve.Count - 1].Efficiency;
            }

            int hi = 1;
            while (hi < curve.Count - 1 && curve[hi].Energy < energy)
            {
                hi++;
            }

            double x0 = Math.Log(curve[hi - 1].Energy), x1 = Math.Log(curve[hi].Energy);
            double y0 = Math.Log(Math.Max(curve[hi - 1].Efficiency, 1e-12));
            double y1 = Math.Log(Math.Max(curve[hi].Efficiency, 1e-12));
            double f = (Math.Log(energy) - x0) / Math.Max(x1 - x0, 1e-12);
            return Math.Exp(y0 + f * (y1 - y0));
        }

        static List<ROIEfficiencyData> BuildCurve(EfficiencyFitResult result, EfficiencyFitInput input)
        {
            List<double> grid = new List<double>();
            if (input.Reference != null && input.Reference.Count >= 2)
            {
                // Сетка исходной кривой сохраняется: файл остаётся сравнимым
                // с прежним точка в точку.
                foreach (ROIEfficiencyData point in input.Reference)
                {
                    if (point.Energy > 0.0)
                    {
                        grid.Add(point.Energy);
                    }
                }
            }

            if (grid.Count < 2)
            {
                double lo = Math.Max(input.MinEnergy, 10.0);
                double hi = Math.Max(input.MaxEnergy, lo * 2.0);
                int steps = 120;
                for (int i = 0; i <= steps; i++)
                {
                    grid.Add(lo * Math.Pow(hi / lo, i / (double)steps));
                }
            }

            grid.Sort();
            List<ROIEfficiencyData> curve = new List<ROIEfficiencyData>();
            foreach (double e in grid)
            {
                double value = Evaluate(result, e);
                if (!(value > 0.0) || double.IsNaN(value) || double.IsInfinity(value))
                {
                    continue;
                }

                // Вне диапазона измеренных линий кривая — экстраполяция
                // полинома; она отмечена ростом заявленной погрешности, а не
                // спрятана.
                double outside = 0.0;
                if (e < result.MinEnergy)
                {
                    outside = Math.Log(result.MinEnergy / e);
                }
                else if (e > result.MaxEnergy)
                {
                    outside = Math.Log(e / result.MaxEnergy);
                }

                curve.Add(new ROIEfficiencyData
                {
                    Energy = e,
                    // Обрезка по единице законна только там, где уровень
                    // физический. В режиме «только форма» уровень условный
                    // (единица на опорной энергии), и обрезка схлопывала весь
                    // низ шкалы в 1.0 — при этом печать в консоль шла мимо
                    // BuildCurve и показывала правильные числа, так что
                    // расхождение было видно только при сверке файла.
                    Efficiency = result.LevelSource == EfficiencyLevelSource.ShapeOnly
                        ? value
                        : Math.Min(value, 1.0),
                    ErrorPercent = 100.0 * Math.Sqrt(result.Chi2Ndf) * (0.05 + outside)
                });
            }

            return curve;
        }

        static bool SolveSymmetric(double[,] a, double[] b, out double[] x)
        {
            int n = b.Length;
            double[,] m = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    m[i, j] = a[i, j];
                }

                m[i, n] = b[i];
            }

            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < n; row++)
                {
                    if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                    {
                        pivot = row;
                    }
                }

                if (Math.Abs(m[pivot, col]) < 1e-12)
                {
                    x = null;
                    return false;
                }

                if (pivot != col)
                {
                    for (int j = col; j <= n; j++)
                    {
                        double t = m[col, j];
                        m[col, j] = m[pivot, j];
                        m[pivot, j] = t;
                    }
                }

                for (int row = 0; row < n; row++)
                {
                    if (row == col)
                    {
                        continue;
                    }

                    double factor = m[row, col] / m[col, col];
                    for (int j = col; j <= n; j++)
                    {
                        m[row, j] -= factor * m[col, j];
                    }
                }
            }

            x = new double[n];
            for (int i = 0; i < n; i++)
            {
                x[i] = m[i, n] / m[i, i];
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Загрузка
        // ------------------------------------------------------------------

        public static ResultData LoadResultData(string path, int resultIndex)
        {
            return LoadResultData(path, resultIndex, null);
        }

        /// <summary>
        /// Загрузка спектра. <paramref name="fallbackDeviceGuid"/> — конфигурация
        /// устройства на случай, когда ссылка спектра никуда не ведёт: так бывает
        /// у старых файлов, переживших переименование прибора (пачка КОТ-103
        /// ссылается на «RC-103 (282)» с Guid, которого в конфиге больше нет).
        /// Подставлять что попало нельзя — от конфигурации зависят обе
        /// калибровки, — поэтому замена только явная, по решению пользователя.
        /// </summary>
        public static ResultData LoadResultData(string path, int resultIndex, string fallbackDeviceGuid)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData data = resultIndex < file.ResultDataList.Count
                ? file.ResultDataList[resultIndex]
                : file.ResultDataList[0];

            PolynomialEnergyCalibration polynomial =
                data.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (polynomial != null)
            {
                polynomial.CheckCalibration(data.EnergySpectrum.NumberOfChannels);
            }

            if (data.FwhmCalibration == null)
            {
                List<DeviceConfigInfo> devices = DeviceConfigManager.GetInstance().DeviceConfigList;
                DeviceConfigInfo device = devices
                    .FirstOrDefault(c => c.Guid == data.DeviceConfigReference.Guid);
                if (device == null && !string.IsNullOrEmpty(fallbackDeviceGuid))
                {
                    device = devices.FirstOrDefault(c => string.Equals(
                        c.Guid, fallbackDeviceGuid, StringComparison.OrdinalIgnoreCase));
                }

                if (device == null)
                {
                    throw new InvalidOperationException(string.Format(
                        Resources.EfficiencyMakerNoDeviceConfig, data.DeviceConfigReference.Name));
                }

                FWHMPeakDetectionMethodConfig peakConfig =
                    (FWHMPeakDetectionMethodConfig)device.PeakDetectionMethodConfig;
                data.FwhmCalibration = peakConfig.FwhmCalibration != null
                    ? peakConfig.FwhmCalibration.Clone()
                    : FwhmCalibration.DefaultCalibration(peakConfig, data.EnergySpectrum.EnergyCalibration);
            }

            if (data.FwhmCalibration == null)
            {
                throw new InvalidOperationException(Resources.EfficiencyMakerNoFwhm);
            }

            return data;
        }

        public static List<ROIEfficiencyData> LoadReferenceCurve(string path)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ROIConfigData));
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ROIConfigData config = (ROIConfigData)serializer.Deserialize(stream);
                if (config.ROIEfficiency == null)
                {
                    return new List<ROIEfficiencyData>();
                }

                return config.ROIEfficiency
                    .Where(p => p != null && p.Energy > 0.0 && p.Efficiency > 0.0)
                    .OrderBy(p => p.Energy)
                    .ToList();
            }
        }

        /// <summary>
        /// Записать кривую в ROI-конфигурацию. Исходный файл, если он был,
        /// копируется целиком — зоны, имя и заметка пользователя не теряются,
        /// меняется только таблица эффективности.
        /// </summary>
        public static void SaveCurve(string path, string referencePath, string name,
                                     List<ROIEfficiencyData> curve, string note)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ROIConfigData));
            ROIConfigData config;
            if (!string.IsNullOrEmpty(referencePath) && File.Exists(referencePath))
            {
                using (FileStream stream = new FileStream(referencePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    config = (ROIConfigData)serializer.Deserialize(stream);
                }
            }
            else
            {
                config = new ROIConfigData();
                config.Guid = System.Guid.NewGuid().ToString();
            }

            if (!string.IsNullOrEmpty(name))
            {
                config.Name = name;
            }

            config.ROIEfficiency = curve;
            config.LastUpdated = DateTime.Now;
            if (!string.IsNullOrEmpty(note))
            {
                config.Note = new CDATA(note);
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                serializer.Serialize(stream, config);
            }
        }

        public static void ExportCsv(string path, EfficiencyFitResult result)
        {
            using (StreamWriter writer = new StreamWriter(path, false, System.Text.Encoding.UTF8))
            {
                writer.WriteLine("E_keV,eps,err_pct");
                foreach (ROIEfficiencyData point in result.Curve)
                {
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:G8},{1:G8},{2:G6}", point.Energy, point.Efficiency, point.ErrorPercent));
                }

                writer.WriteLine();
                writer.WriteLine("spectrum,chain,nuclide,E_keV,I_pct,net,sigma,z,eps_measured,residual_ln,accepted,reason");
                foreach (EfficiencyObservation o in result.Observations)
                {
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3:G8},{4:G6},{5:G8},{6:G6},{7:G5},{8:G6},{9:G5},{10},{11}",
                        Csv(o.Spectrum), Csv(o.Chain), Csv(o.Nuclide), o.Energy, o.Intensity,
                        o.NetCounts, o.NetSigma, o.Significance, o.MeasuredEfficiency, o.Residual,
                        o.Accepted ? 1 : 0, Csv(o.Reason)));
                }
            }
        }

        /// <summary>
        /// Текстовое поле для CSV. Имя спектра приходит из имени файла, а
        /// причина отбраковки — из ресурса с подставленными числами: и там и
        /// там запятая встречается, и без кавычек она сдвигала все колонки
        /// правее себя.
        /// </summary>
        static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // ------------------------------------------------------------------

        static double EnergyToChannel(EnergyCalibration calibration, double energy, int channels)
        {
            try
            {
                // Число каналов обязательно передавать. Без него калибровка
                // берёт своё умолчание 8192, продолжает полином далеко за конец
                // спектра и объявляет верхом шкалы значение В ЭТОЙ точке. У
                // кубической калибровки с отрицательным старшим коэффициентом
                // (обычное дело у сцинтиллятора) продолжение уходит в минус —
                // и тогда ЛЮБАЯ энергия оказывается «выше верха шкалы», а
                // EnergyToChannel возвращает последний канал. Все линии подряд
                // выпадают за спектр, и разбор молча даёт «0 lines measured».
                double channel = calibration.EnergyToChannel(energy, channels);
                if (double.IsNaN(channel) || channel < 0.0 || channel > channels - 1)
                {
                    return double.NaN;
                }

                return channel;
            }
            catch (Exception)
            {
                return double.NaN;
            }
        }

        static double EnergyPerChannel(EnergyCalibration calibration, double channel)
        {
            double a = calibration.ChannelToEnergy(Math.Max(channel - 0.5, 0.0));
            double b = calibration.ChannelToEnergy(channel + 0.5);
            return b - a;
        }
    }
}
