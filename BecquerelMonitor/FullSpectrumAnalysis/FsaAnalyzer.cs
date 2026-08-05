using System;
using System.Collections.Generic;
using System.Linq;
using BecquerelMonitor.Utils;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Полноспектральная декомпозиция (full-spectrum analysis).
    ///
    /// В отличие от поиска пиков вопрос ставится не «есть ли пик на 583 кэВ», а
    /// «какая смесь откликов Th-232, Ra-226, K-40... и фона лучше всего
    /// объясняет весь спектр сразу». Модель:
    ///
    ///     S(i) ~ sum_j A_j * F_j(i; a, b) + B(i) + C(i)
    ///
    /// F_j — образ компонента (сумма профилей единичной площади по таблице
    /// линий: положение из энергетической калибровки, ширина и форма из
    /// ПШПВ-калибровки, вес из выхода на распад родителя и кривой
    /// эффективности);
    /// a, b — дрейф шкалы (усиление и ноль), общие нелинейные параметры,
    /// перебираются сеткой; B — измеренный фон, вычитается с коэффициентом 1;
    /// C — континуум: неотрицательный кусочно-линейный базис («шапки») с шагом
    /// узлов не меньше 4 ПШПВ, чтобы шапка не могла съесть одиночный пик.
    ///
    /// Линейная часть — взвешенный NNLS (активное множество на нормальных
    /// уравнениях) с двумя итерациями хуберовского перевзвешивания, чтобы
    /// систематические расхождения формы не утаскивали оценки. После первого
    /// решения компоненты со значимостью ниже порога выбрасываются и модель
    /// пересчитывается заново.
    /// </summary>
    public sealed class FsaAnalyzer
    {
        /// <summary>Модель континуума.</summary>
        public enum ContinuumMode
        {
            /// <summary>Континуум оценивается SNIP-ом заранее и вычитается.</summary>
            Snip,
            /// <summary>Континуум входит в общую систему уравнений («шапки»).</summary>
            Spline
        }

        /// <summary>
        /// Считать по всему спектру, не обрезая по MinEnergy/MaxEnergy: от
        /// первого канала до предпоследнего (последний — канал переполнения).
        /// Разложение должно быть нарисовано на всём спектре: обрыв стека на
        /// границе диапазона поиска пиков читается как дефект отрисовки.
        /// Цена известна и измерена: на 8192-канальном NaI с порогом у самого
        /// начала шкалы χ²/ndf 20.7 (от 40 кэВ) против 28.0 (от нулевого
        /// канала) — шум порога модели описывать нечем, и его берёт на себя
        /// континуум.
        /// </summary>
        public bool FitWholeSpectrum { get; set; }

        public ContinuumMode Mode { get; set; }

        public double MinEnergy { get; set; }

        public double MaxEnergy { get; set; }

        /// <summary>Доля погрешности континуума в весах.</summary>
        public double Xi { get; set; }

        /// <summary>Порог хуберовского перевзвешивания, в сигмах. 0 — выключено.</summary>
        public double HuberM { get; set; }

        /// <summary>Порог значимости для отсева перед вторым проходом. 0 — без отсева.</summary>
        public double RefitZ { get; set; }

        public double GainRange { get; set; }

        public int GainSteps { get; set; }

        /// <summary>Диапазон сдвига нуля шкалы, кэВ.</summary>
        public double OffsetRangeKev { get; set; }

        public int OffsetSteps { get; set; }

        /// <summary>
        /// Добавлять образы обратного рассеяния, выведенные из найденного
        /// состава. Мерено на корпусе: recall 86 % → 89 %, Σχ²/ndf 559 → 547.
        /// </summary>
        public bool Backscatter { get; set; }

        /// <summary>
        /// Матрица отклика геометрии, если она посчитана и годна. С ней образ
        /// компонента — это сумма ОТКЛИКОВ его линий, то есть пик вместе с
        /// комптоновским плато, краем и пиками вылета; без неё — только пики, а
        /// всё плато достаётся свободной подложке.
        ///
        /// Матрица уже несёт эффективность (она посчитана как доля на квант,
        /// испущенный источником), поэтому кривая эффективности к её весам
        /// ВТОРОЙ раз не применяется.
        /// </summary>
        public EfficiencyMaker.ResponseMatrix ResponseMatrix { get; set; }

        public FsaAnalyzer()
        {
            this.Mode = ContinuumMode.Spline;
            this.FitWholeSpectrum = true;
            this.MinEnergy = 40.0;
            this.MaxEnergy = 2800.0;
            this.Xi = 0.03;
            this.HuberM = 3.0;
            this.RefitZ = 3.0;
            this.GainRange = 0.008;
            this.GainSteps = 9;
            this.OffsetRangeKev = 3.0;
            this.OffsetSteps = 9;
            this.Backscatter = true;
        }

        /// <summary>
        /// Разложить спектр. Возвращает null, если разложение невозможно:
        /// нет калибровок, вырожденный диапазон или пустая библиотека.
        /// </summary>
        public FsaResult Analyze(
            EnergySpectrum spectrum,
            EnergySpectrum backgroundSpectrum,
            FwhmCalibration fwhmCalibration,
            List<FsaComponent> library,
            FsaEfficiency efficiency)
        {
            if (spectrum == null || spectrum.Spectrum == null || fwhmCalibration == null
                || spectrum.EnergyCalibration == null || library == null || library.Count == 0)
            {
                return null;
            }

            int channels = spectrum.NumberOfChannels;
            if (channels < 32)
            {
                return null;
            }

            // Кэши уширения живут ровно один разбор: калибровку могли изменить
            // на месте, и тогда ссылка та же, а числа другие.
            this.depositChannels = null;
            this.depositChannelsCalibration = null;
            this.kernelBank = null;

            EnergyCalibration calibration = spectrum.EnergyCalibration;
            int chLo = this.FitWholeSpectrum
                ? 0
                : ClampChannel(EnergyToChannelSafe(calibration, this.MinEnergy, channels), channels);
            int chHi = this.FitWholeSpectrum
                ? channels - 1
                : ClampChannel(EnergyToChannelSafe(calibration, this.MaxEnergy, channels), channels);
            if (chHi < chLo)
            {
                int swap = chLo;
                chLo = chHi;
                chHi = swap;
            }

            // Последний канал АЦП — канал переполнения: в него падает всё, что
            // выше шкалы. Образа у такой структуры нет, объяснить её фит не
            // может — верхний канал исключается, когда диапазон дошёл до края.
            if (chHi >= channels - 1)
            {
                chHi = channels - 2;
            }

            if (chHi <= chLo + 10)
            {
                return null;
            }

            double liveTime = spectrum.LiveTime > 0.0 ? spectrum.LiveTime : spectrum.MeasurementTime;
            if (liveTime <= 0.0)
            {
                liveTime = 1.0;
            }

            int[] raw = spectrum.Spectrum;
            double[] y = new double[channels];
            double[] variance = new double[channels];
            double[] backgroundCurve = new double[channels];
            int[] snipContinuum = null;

            EnergySpectrum background = backgroundSpectrum;
            if (background != null && (background.Spectrum == null || background.NumberOfChannels != channels))
            {
                background = null;
            }

            double backgroundScale = 0.0;
            if (background != null)
            {
                double backgroundLive = background.LiveTime > 0.0 ? background.LiveTime : background.MeasurementTime;
                if (backgroundLive > 0.0)
                {
                    backgroundScale = liveTime / backgroundLive;
                }
                else
                {
                    background = null;
                }
            }

            if (this.Mode == ContinuumMode.Snip)
            {
                snipContinuum = Snip(fwhmCalibration, spectrum);
                for (int i = 0; i < channels; i++)
                {
                    double continuum = snipContinuum != null ? snipContinuum[i] : 0.0;
                    y[i] = raw[i] - continuum;
                    double c = this.Xi * continuum;
                    variance[i] = Math.Max(raw[i], 1.0) + c * c;
                }
            }
            else
            {
                for (int i = 0; i < channels; i++)
                {
                    y[i] = raw[i];
                    variance[i] = Math.Max(raw[i], 1.0);
                }
            }

            // Фон измерен и нормирован по живому времени, поэтому он не
            // подбирается, а вычитается: свободный коэффициент фона вырожден с
            // компонентами пробы (комнатный K-40 против K-40 в образце), и NNLS
            // раздувает фон вместо образца.
            if (background != null)
            {
                int[] backgroundSnip = this.Mode == ContinuumMode.Snip ? Snip(fwhmCalibration, background) : null;
                for (int i = 0; i < channels; i++)
                {
                    double full = background.Spectrum[i] * backgroundScale;
                    double value = full;
                    if (backgroundSnip != null)
                    {
                        // в режиме SNIP континуум фона уже сидит внутри оценки
                        // континуума переднего спектра — вычитается только пиковая часть
                        value = Math.Max(0.0, background.Spectrum[i] - backgroundSnip[i]) * backgroundScale;
                    }

                    backgroundCurve[i] = value;
                    y[i] -= value;
                    // Дисперсия — от ПОЛНОГО фона, как в харнессе: шум отсчёта
                    // фона не уменьшается оттого, что его континуум учтён в
                    // другом слагаемом модели.
                    variance[i] += Math.Max(Math.Abs(full) * backgroundScale, backgroundScale * backgroundScale);
                }
            }

            List<double[]> fixedColumns = new List<double[]>();
            if (this.Mode == ContinuumMode.Spline)
            {
                fixedColumns.AddRange(BuildHatBasis(fwhmCalibration, chLo, chHi, channels));
            }

            double[] baseWeights = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                baseWeights[i] = 1.0 / variance[i];
            }

            // --offset задан в кэВ; в каналы переводится по фактическому наклону
            // шкалы на границах фита, иначе одна и та же величина означала бы
            // разное для 1024- и 8192-канальных приборов.
            double energyLo = calibration.ChannelToEnergy(chLo);
            double energyHi = calibration.ChannelToEnergy(chHi);
            double channelsPerKev = energyHi > energyLo
                ? (chHi - chLo) / (energyHi - energyLo)
                : (chHi - chLo) / Math.Max(1.0, this.MaxEnergy - this.MinEnergy);
            double offsetRangeChannels = this.OffsetRangeKev * channelsPerKev;

            int gainSteps = Math.Max(1, this.GainSteps);
            int offsetSteps = Math.Max(1, this.OffsetSteps);
            double bestGain = 1.0;
            double bestOffset = 0.0;
            double bestChi2 = Double.MaxValue;
            int bestGainIndex = 0;
            int bestOffsetIndex = 0;
            for (int gi = 0; gi < gainSteps; gi++)
            {
                double gain = gainSteps == 1
                    ? 1.0
                    : 1.0 - this.GainRange + 2.0 * this.GainRange * gi / (gainSteps - 1);
                for (int oi = 0; oi < offsetSteps; oi++)
                {
                    double offset = offsetSteps == 1
                        ? 0.0
                        : -offsetRangeChannels + 2.0 * offsetRangeChannels * oi / (offsetSteps - 1);
                    FitResult probe = FitOnce(library, fixedColumns, calibration, fwhmCalibration, efficiency,
                                              gain, offset, chLo, chHi, channels, y, baseWeights, null);
                    if (probe != null && probe.Chi2 < bestChi2)
                    {
                        bestChi2 = probe.Chi2;
                        bestGain = gain;
                        bestOffset = offset;
                        bestGainIndex = gi;
                        bestOffsetIndex = oi;
                    }
                }
            }

            if (bestChi2 == Double.MaxValue)
            {
                return null;
            }

            FitResult best = FitHuber(library, fixedColumns, calibration, fwhmCalibration, efficiency,
                                      bestGain, bestOffset, chLo, chHi, channels, y, variance, baseWeights, null);
            if (best == null)
            {
                return null;
            }

            // Образ обратного рассеяния по найденному составу — до отсева по z:
            // отсев решает, какие компоненты дожили, и решать это надо уже при
            // закрытой области рассеяния.
            List<FsaComponent> working = library;
            if (this.Backscatter)
            {
                List<FsaComponent> derived = BuildBackscatterComponents(best, efficiency);
                if (derived.Count > 0)
                {
                    List<FsaComponent> extended = new List<FsaComponent>(library);
                    extended.AddRange(derived);
                    FitResult refit = FitHuber(extended, fixedColumns, calibration, fwhmCalibration, efficiency,
                                               bestGain, bestOffset, chLo, chHi, channels, y, variance, baseWeights, null);
                    if (refit != null)
                    {
                        best = refit;
                        working = extended;
                    }
                }
            }

            // «Предварительный анализ состава»: второй проход без компонентов,
            // не прошедших порог значимости в первом.
            if (this.RefitZ > 0.0)
            {
                List<FsaComponent> keep = new List<FsaComponent>();
                int total = 0;
                for (int k = 0; k < best.Columns.Count; k++)
                {
                    FsaComponent component = best.Columns[k].Component;
                    if (component == null)
                    {
                        continue;
                    }

                    total++;
                    if (best.Z[k] >= this.RefitZ)
                    {
                        keep.Add(component);
                    }
                }

                if (keep.Count > 0 && keep.Count < total)
                {
                    FitResult refit = FitHuber(working, fixedColumns, calibration, fwhmCalibration, efficiency,
                                               bestGain, bestOffset, chLo, chHi, channels, y, variance, baseWeights, keep);
                    if (refit != null)
                    {
                        best = refit;
                    }
                }
            }

            // Второй круг: форма образа задаётся составом, а состав только что
            // изменился отсевом.
            if (this.Backscatter)
            {
                List<FsaComponent> survivors = new List<FsaComponent>();
                for (int k = 0; k < best.Columns.Count; k++)
                {
                    FsaComponent component = best.Columns[k].Component;
                    if (component != null && !component.Derived)
                    {
                        survivors.Add(component);
                    }
                }

                List<FsaComponent> derived = BuildBackscatterComponents(best, efficiency);
                if (derived.Count > 0)
                {
                    survivors.AddRange(derived);
                    FitResult refit = FitHuber(survivors, fixedColumns, calibration, fwhmCalibration, efficiency,
                                               bestGain, bestOffset, chLo, chHi, channels, y, variance, baseWeights, null);
                    if (refit != null)
                    {
                        best = refit;
                    }
                }
            }

            return BuildResult(best, spectrum, backgroundCurve, snipContinuum, chLo, chHi, channels,
                               bestGain, bestOffset, liveTime, efficiency != null,
                               (gainSteps > 1 && (bestGainIndex == 0 || bestGainIndex == gainSteps - 1))
                               || (offsetSteps > 1 && (bestOffsetIndex == 0 || bestOffsetIndex == offsetSteps - 1)));
        }

        FsaResult BuildResult(FitResult fit, EnergySpectrum spectrum, double[] backgroundCurve, int[] snipContinuum,
                              int chLo, int chHi, int channels, double gain, double offset, double liveTime,
                              bool efficiencyUsed, bool driftOnEdge)
        {
            FsaResult result = new FsaResult
            {
                FirstChannel = chLo,
                LastChannel = chHi,
                Chi2Ndf = fit.Chi2Ndf,
                Gain = gain,
                OffsetChannels = offset,
                LiveTime = liveTime,
                EfficiencyUsed = efficiencyUsed,
                DriftOnGridEdge = driftOnEdge,
                Background = backgroundCurve,
                Continuum = new double[channels],
                Model = new double[channels]
            };

            if (snipContinuum != null)
            {
                for (int i = chLo; i <= chHi; i++)
                {
                    result.Continuum[i] = snipContinuum[i];
                }
            }

            double totalPeakCounts = 0.0;
            for (int k = 0; k < fit.Columns.Count; k++)
            {
                FitColumn column = fit.Columns[k];
                double amplitude = fit.Amplitude[k];
                if (column.Component == null)
                {
                    // шапки континуума схлопываются в одну кривую
                    if (amplitude > 0.0)
                    {
                        for (int i = chLo; i <= chHi; i++)
                        {
                            result.Continuum[i] += amplitude * column.Values[i];
                        }
                    }

                    continue;
                }

                if (amplitude <= 0.0)
                {
                    continue;
                }

                double[] curve = new double[channels];
                for (int i = chLo; i <= chHi; i++)
                {
                    curve[i] = amplitude * column.Values[i];
                }

                FsaComponentResult component = new FsaComponentResult
                {
                    Name = column.Component.Name,
                    Kind = column.Component.Kind,
                    Curve = curve,
                    PeakCounts = fit.PeakCounts[k],
                    CountRate = amplitude / liveTime,
                    Z = fit.Z[k]
                };

                result.Components.Add(component);
                if (component.Kind != FsaComponentKind.Nuisance)
                {
                    totalPeakCounts += component.PeakCounts;
                }
            }

            foreach (FsaComponentResult component in result.Components)
            {
                component.SharePercent = component.Kind != FsaComponentKind.Nuisance && totalPeakCounts > 0.0
                    ? 100.0 * component.PeakCounts / totalPeakCounts
                    : 0.0;
            }

            for (int i = chLo; i <= chHi; i++)
            {
                double sum = result.Continuum[i];
                foreach (FsaComponentResult component in result.Components)
                {
                    sum += component.Curve[i];
                }

                result.Model[i] = sum;
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Модель и решатель
        // ------------------------------------------------------------------

        sealed class FitColumn
        {
            public FsaComponent Component;   // null у колонок континуума
            public double[] Values;
        }

        sealed class FitResult
        {
            public List<FitColumn> Columns;
            public double[] Amplitude;
            public double[] Sigma;
            public double[] Z;
            public double[] PeakCounts;
            public double Chi2;
            public double Chi2Ndf;
            public double[] Residual;
        }

        FitResult FitHuber(List<FsaComponent> library, List<double[]> fixedColumns,
                           EnergyCalibration calibration, FwhmCalibration fwhmCalibration, FsaEfficiency efficiency,
                           double gain, double offset, int chLo, int chHi, int channels,
                           double[] y, double[] variance, double[] baseWeights, List<FsaComponent> subset)
        {
            double[] weights = (double[])baseWeights.Clone();
            FitResult best = null;
            int passes = this.HuberM > 0.0 ? 3 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                best = FitOnce(library, fixedColumns, calibration, fwhmCalibration, efficiency,
                               gain, offset, chLo, chHi, channels, y, weights, subset);
                if (best == null || pass + 1 == passes)
                {
                    break;
                }

                for (int i = chLo; i <= chHi; i++)
                {
                    double sigma = Math.Sqrt(variance[i]);
                    double residual = Math.Abs(best.Residual[i]);
                    double m = this.HuberM * sigma;
                    weights[i] = residual > m ? (1.0 / variance[i]) * (m / residual) : 1.0 / variance[i];
                }
            }

            return best;
        }

        FitResult FitOnce(List<FsaComponent> library, List<double[]> fixedColumns,
                          EnergyCalibration calibration, FwhmCalibration fwhmCalibration, FsaEfficiency efficiency,
                          double gain, double offset, int chLo, int chHi, int channels,
                          double[] y, double[] weights, List<FsaComponent> subset)
        {
            List<FitColumn> columns = new List<FitColumn>();
            foreach (FsaComponent component in library)
            {
                if (subset != null && !subset.Contains(component))
                {
                    continue;
                }

                double[] template = BuildTemplate(component, calibration, fwhmCalibration, efficiency,
                                                  gain, offset, chLo, chHi, channels);
                if (template != null)
                {
                    columns.Add(new FitColumn { Component = component, Values = template });
                }
            }

            foreach (double[] column in fixedColumns)
            {
                columns.Add(new FitColumn { Component = null, Values = column });
            }

            int m = columns.Count;
            if (m == 0)
            {
                return null;
            }

            int n = chHi - chLo + 1;

            double[,] gram = new double[m, m];
            double[] c = new double[m];
            for (int a = 0; a < m; a++)
            {
                double[] ta = columns[a].Values;
                double dot = 0.0;
                for (int i = chLo; i <= chHi; i++)
                {
                    dot += ta[i] * weights[i] * y[i];
                }

                c[a] = dot;
                for (int b = a; b < m; b++)
                {
                    double[] tb = columns[b].Values;
                    double value = 0.0;
                    for (int i = chLo; i <= chHi; i++)
                    {
                        value += ta[i] * weights[i] * tb[i];
                    }

                    gram[a, b] = value;
                    gram[b, a] = value;
                }
            }

            bool[] active;
            double[] x = NnlsSolve(gram, c, m, out active);

            double[] model = new double[channels];
            for (int k = 0; k < m; k++)
            {
                if (x[k] <= 0.0)
                {
                    continue;
                }

                double[] t = columns[k].Values;
                for (int i = chLo; i <= chHi; i++)
                {
                    model[i] += x[k] * t[i];
                }
            }

            double chi2 = 0.0;
            double[] residual = new double[channels];
            for (int i = chLo; i <= chHi; i++)
            {
                double r = y[i] - model[i];
                residual[i] = r;
                chi2 += r * r * weights[i];
            }

            int activeCount = 0;
            for (int k = 0; k < m; k++)
            {
                if (active[k])
                {
                    activeCount++;
                }
            }

            double chi2ndf = chi2 / Math.Max(1, n - activeCount);

            // Погрешности — из обратной матрицы нормальных уравнений активного
            // множества, надутые на sqrt(chi2/ndf): когда модель не дотягивает
            // до статистики, «сырая» погрешность занижена.
            double[] sigma = new double[m];
            double[] z = new double[m];
            double inflate = Math.Sqrt(Math.Max(1.0, chi2ndf));
            List<int> activeIndices = new List<int>();
            for (int k = 0; k < m; k++)
            {
                if (active[k])
                {
                    activeIndices.Add(k);
                }
            }

            if (activeIndices.Count > 0)
            {
                double[,] activeGram = new double[activeIndices.Count, activeIndices.Count];
                for (int a = 0; a < activeIndices.Count; a++)
                {
                    for (int b = 0; b < activeIndices.Count; b++)
                    {
                        activeGram[a, b] = gram[activeIndices[a], activeIndices[b]];
                    }
                }

                double[,] inverse = InvertSymmetric(activeGram, activeIndices.Count);
                if (inverse != null)
                {
                    for (int a = 0; a < activeIndices.Count; a++)
                    {
                        double d = inverse[a, a];
                        sigma[activeIndices[a]] = d > 0.0 ? Math.Sqrt(d) * inflate : 0.0;
                    }
                }
            }

            for (int k = 0; k < m; k++)
            {
                if (!active[k] && gram[k, k] > 0.0)
                {
                    sigma[k] = inflate / Math.Sqrt(gram[k, k]);
                }

                z[k] = sigma[k] > 0.0 ? x[k] / sigma[k] : 0.0;
            }

            double[] peakCounts = new double[m];
            for (int k = 0; k < m; k++)
            {
                peakCounts[k] = x[k] * SumRange(columns[k].Values, chLo, chHi);
            }

            return new FitResult
            {
                Columns = columns,
                Amplitude = x,
                Sigma = sigma,
                Z = z,
                PeakCounts = peakCounts,
                Chi2 = chi2,
                Chi2Ndf = chi2ndf,
                Residual = residual
            };
        }

        /// <summary>
        /// Образ компонента: профили единичной площади в позициях линий с
        /// учётом дрейфа шкалы, весами по выходу и кривой эффективности.
        ///
        /// Форма берётся из ПШПВ-калибровки через <see cref="PeakShapeModel"/> —
        /// та же, которой рисует и меряет пики всё остальное приложение. Своя
        /// гауссиана здесь была прямой ошибкой: у приборов с PeakType = 1
        /// (ExpGaussExp 1.5/5 у ASN16) образ не имел левого хвоста, и невязка
        /// уходила в континуум.
        ///
        /// Нормировка — на площадь самого профиля, посчитанную по его полному
        /// носителю (GetLeftSupport/GetRightSupport), а не на площадь
        /// гауссианы: у профиля с хвостами интеграл другой, и гауссова норма
        /// поднимала бы вершину модели над данными при недоборе площади.
        /// Носитель считается целиком, даже если часть его вышла за границы
        /// фита, — иначе линия у края шкалы получила бы вес больше своего.
        /// </summary>
        const double ElectronMassKev = 510.99895;

        /// <summary>
        /// Образы обратного рассеяния по составу, найденному предыдущим проходом.
        ///
        /// Фотон энергии E, рассеявшийся назад в веществе ВНЕ кристалла (защита,
        /// сама проба, стены), приходит в детектор с энергией E/(1+2E/511). Без
        /// такого образа эти пики достаются чужим линиям: 662 → 184 кэВ садится
        /// на U-235 185.7, мультиплет 300-340 → ~145, ХРИ W 59 → 48. На корпусе
        /// (58 спектров сцинтилляторов, свой нуклидный сет под каждый) образ дал
        /// recall 86 % → 89 % и Σχ²/ndf 559 → 547; в частности вернул U-235 на
        /// граните и K-40 на плитке и снял U-235 с цезиевого спектра.
        ///
        /// Колонки две, и это не избыточность: прогон показал, что они чинят
        /// разные спектры. Узкая (строго назад) снимает U-235 с Cs-137, широкая
        /// (интеграл по задней полусфере с сечением Клейна — Нишины) снимает
        /// Ba-133 с европиевых. Доля однократного рассеяния строго назад против
        /// интеграла задаётся геометрией рассеивателя, которой мы не знаем, —
        /// поэтому обе свободны, а смесь выбирает NNLS.
        ///
        /// Вес линии берётся на энергии ИСХОДНОГО фотона (амплитуда × выход ×
        /// эффективность) — это поток, которому есть чем рассеиваться, — и
        /// второй раз эффективность не применяется (WeightsAreFinal).
        /// </summary>
        static List<FsaComponent> BuildBackscatterComponents(FitResult fit, FsaEfficiency efficiency)
        {
            List<FsaComponent> made = new List<FsaComponent>();
            FsaComponent broad = BuildBackscatter(fit, efficiency, "Backscatter", 110.0);
            if (broad != null)
            {
                made.Add(broad);
            }

            FsaComponent sharp = BuildBackscatter(fit, efficiency, "Backscatter180", 179.0);
            if (sharp != null)
            {
                made.Add(sharp);
            }

            return made;
        }

        static FsaComponent BuildBackscatter(FitResult fit, FsaEfficiency efficiency,
                                             string name, double thetaMinDegrees)
        {
            const int Steps = 24;
            const double BinKev = 1.0;
            double thetaMin = thetaMinDegrees * Math.PI / 180.0;
            Dictionary<int, double> histogram = new Dictionary<int, double>();

            for (int k = 0; k < fit.Columns.Count; k++)
            {
                FsaComponent source = fit.Columns[k].Component;
                if (source == null || source.Kind == FsaComponentKind.Nuisance
                    || source.Lines.Count == 0)
                {
                    continue;
                }

                double amplitude = fit.Amplitude[k];
                if (!(amplitude > 0.0))
                {
                    continue;
                }

                foreach (FsaLine line in source.Lines)
                {
                    if (!(line.Energy > 0.0) || !(line.Intensity > 0.0))
                    {
                        continue;
                    }

                    double flux = amplitude * line.Intensity;
                    if (efficiency != null)
                    {
                        double e = efficiency.Eval(line.Energy);
                        if (!(e > 0.0))
                        {
                            continue;
                        }

                        flux *= e;
                    }

                    double alpha = line.Energy / ElectronMassKev;
                    for (int s = 0; s < Steps; s++)
                    {
                        double theta = thetaMin + (Math.PI - thetaMin) * (s + 0.5) / Steps;
                        double sin = Math.Sin(theta);
                        double ratio = 1.0 / (1.0 + alpha * (1.0 - Math.Cos(theta)));
                        // Клейн — Нишина на телесный угол без общего множителя,
                        // умноженная на sin(θ) от самого телесного угла.
                        double weight = flux * ratio * ratio
                            * (ratio + 1.0 / ratio - sin * sin) * sin;
                        double scattered = line.Energy * ratio;
                        if (!(weight > 0.0) || !(scattered > 0.0))
                        {
                            continue;
                        }

                        int bin = (int)(scattered / BinKev);
                        double have;
                        histogram.TryGetValue(bin, out have);
                        histogram[bin] = have + weight;
                    }
                }
            }

            if (histogram.Count == 0)
            {
                return null;
            }

            double top = 0.0;
            foreach (double value in histogram.Values)
            {
                if (value > top)
                {
                    top = value;
                }
            }

            if (!(top > 0.0))
            {
                return null;
            }

            FsaComponent component = new FsaComponent(name, FsaComponentKind.Nuisance)
            {
                WeightsAreFinal = true,
                Derived = true,
            };
            foreach (KeyValuePair<int, double> pair in histogram)
            {
                // Нормировка на максимум: амплитуда колонки должна получиться
                // того же порядка, что у остальных, иначе NNLS работает на
                // плохо обусловленной матрице.
                component.Lines.Add(new FsaLine("bs", (pair.Key + 0.5) * BinKev,
                                                100.0 * pair.Value / top));
            }

            component.Lines.Sort((a, b) => a.Energy.CompareTo(b.Energy));
            return component;
        }


        /// <summary>
        /// Образ компонента по матрице отклика: сумма откликов его линий,
        /// уширенная разрешением спектра.
        ///
        /// Порядок важен для цены. Сначала складываются отклики ВСЕХ линий в
        /// одну гистограмму по энергии поглощения — это дёшево, отклик уже
        /// посчитан. И только потом гистограмма уширяется, один раз на
        /// компонент, а не на линию: уширение стоит на два порядка дороже
        /// сложения, и делать его полсотни раз вместо одного значило бы
        /// оплатить разложение секундами вместо миллисекунд.
        ///
        /// Бины ниже порога пропускаются: у отклика длинный хвост, вклад
        /// которого в канал меньше шума отсчёта, а уширение каждого стоит
        /// столько же, сколько уширение пика.
        ///
        /// Уширение сделано СВЁРТКОЙ, а не суммой отдельных пиков. Гистограмма
        /// поглощения переводится в шкалу каналов и раскладывается по ЦЕЛЫМ
        /// каналам (площадь делится между двумя соседними линейно, отчего и
        /// площадь, и центр тяжести сохраняются точно), а потом каждый
        /// ненулевой канал размазывается готовым ядром из банка. Ядро зависит
        /// только от ПШПВ, и при целом центре одно и то же ядро годится для
        /// любого положения — поэтому профиль считается один раз на значение
        /// ПШПВ за весь разбор, а не заново на каждую группу, каждый компонент
        /// и каждый узел сетки дрейфа. Именно на это уходило восемь секунд из
        /// девяти: двадцать миллионов вычислений профиля против ста тысяч.
        /// </summary>
        double[] BuildTemplateFromResponse(FsaComponent component, EnergyCalibration calibration,
                                           FwhmCalibration fwhmCalibration,
                                           double gain, double offset, int chLo, int chHi, int channels)
        {
            EfficiencyMaker.ResponseMatrix matrix = this.ResponseMatrix;
            double bin = matrix.BinKev;
            if (!(bin > 0.0))
            {
                return null;
            }

            double topEnergy = 0.0;
            foreach (FsaLine line in component.Lines)
            {
                if (line.Energy > topEnergy && line.Intensity > 0.0)
                {
                    topEnergy = line.Energy;
                }
            }

            if (!(topEnergy > 0.0))
            {
                return null;
            }

            double[] deposit = new double[(int)(topEnergy / bin + 0.5) + 1];
            bool anyLine = false;
            foreach (FsaLine line in component.Lines)
            {
                if (!(line.Energy > 0.0) || !(line.Intensity > 0.0))
                {
                    continue;
                }

                // Эффективность НЕ применяется: она уже внутри отклика.
                matrix.Accumulate(deposit, line.Energy, line.Intensity / 100.0);
                anyLine = true;
            }

            if (!anyLine)
            {
                return null;
            }

            double top = 0.0;
            foreach (double v in deposit)
            {
                if (v > top)
                {
                    top = v;
                }
            }

            if (!(top > 0.0))
            {
                return null;
            }

            double threshold = top * 1.0E-5;
            double[] template = new double[channels];

            // Перевод «энергия → канал» не зависит ни от компонента, ни от узла
            // сетки дрейфа (усиление и ноль накладываются ПОСЛЕ), значит его
            // достаточно посчитать один раз на разбор. У нелинейной калибровки
            // это обращение полинома, и полторы тысячи обращений на компонент,
            // повторённые восемьдесят один раз, стоили заметной доли счёта.
            double[] positions = this.DepositChannels(calibration, bin, deposit.Length + 1, channels);
            ShapeKernelBank bank = this.Kernels(fwhmCalibration);

            // Источники кладутся с запасом по обе стороны шкалы: линия выше
            // верхнего канала образа не имеет, но её левый хвост в окно фита
            // попадает, и терять его нельзя.
            int pad = SourcePad(fwhmCalibration, channels);
            int size = channels + 2 * pad;
            double[] source = this.sourceBuffer;
            int[] bands = this.sourceBands;
            if (source == null || source.Length < size)
            {
                source = this.sourceBuffer = new double[size];
                bands = this.sourceBands = new int[size];
            }

            int srcLo = Int32.MaxValue;
            int srcHi = Int32.MinValue;

            // Бины СЛИВАЮТСЯ в группы шириной около четверти ПШПВ, и площадь
            // группы кладётся в её центр тяжести.
            //
            // Это не оптимизация ради оптимизации, а приведение шага к смыслу.
            // Держать шаг 2 кэВ там, где ПШПВ 44 кэВ, незачем: результат всё
            // равно размажется, а свёртку пришлось бы вести по полутора тысячам
            // источников на компонент вместо полусотни. Площадь и центр тяжести
            // группа сохраняет, значит уширенная картина не меняется.
            int b = 1;
            while (b < deposit.Length)
            {
                if (deposit[b] <= threshold)
                {
                    b++;
                    continue;
                }

                double position = positions[b];
                double nextPosition = positions[b + 1];
                if (Double.IsNaN(position))
                {
                    b++;
                    continue;
                }

                double p = gain * position + offset;
                double fwhm = fwhmCalibration.ChannelToFwhm(p);
                if (!(fwhm > 0.0) || Double.IsNaN(fwhm))
                {
                    b++;
                    continue;
                }

                double perBin = Double.IsNaN(nextPosition) ? 0.0 : Math.Abs(nextPosition - position);
                int group = perBin > 0.0 ? (int)(0.25 * fwhm / perBin) : 1;
                if (group < 1)
                {
                    group = 1;
                }

                double area = 0.0;
                double moment = 0.0;
                int end = Math.Min(deposit.Length, b + group);
                for (int k = b; k < end; k++)
                {
                    double v = deposit[k];
                    if (v <= 0.0)
                    {
                        continue;
                    }

                    double q = positions[k];
                    if (Double.IsNaN(q))
                    {
                        continue;
                    }

                    area += v;
                    moment += v * (gain * q + offset);
                }

                b = end;
                if (!(area > 0.0))
                {
                    continue;
                }

                double center = moment / area;
                if (Double.IsNaN(center))
                {
                    continue;
                }

                int band = ShapeKernelBank.Band(fwhm);
                int channel = (int)Math.Floor(center);
                double frac = center - channel;
                Splat(source, bands, pad, channels, channel, area * (1.0 - frac), band, ref srcLo, ref srcHi);
                Splat(source, bands, pad, channels, channel + 1, area * frac, band, ref srcLo, ref srcHi);
            }

            // Свёртка. Буфер источников общий и переиспользуется между
            // компонентами — каждая ячейка гасится сразу после того, как её
            // размазали, иначе следующий компонент унаследовал бы чужой образ.
            bool any = false;
            for (int idx = srcLo; idx <= srcHi; idx++)
            {
                double weight = source[idx];
                if (weight == 0.0)
                {
                    continue;
                }

                source[idx] = 0.0;
                double[] kernel = bank.Get(bands[idx]);
                if (kernel == null)
                {
                    continue;
                }

                int full0 = idx - pad - bank.LeftSpan(bands[idx]);
                int lo = Math.Max(chLo, full0);
                int hi = Math.Min(chHi, full0 + kernel.Length - 1);
                if (hi < lo)
                {
                    continue;
                }

                for (int i = lo; i <= hi; i++)
                {
                    template[i] += weight * kernel[i - full0];
                }

                any = true;
            }

            return any ? template : null;
        }

        /// <summary>
        /// Положить площадь в целый канал источника, запомнив, каким ядром её
        /// потом размазать. Каналы за пределами буфера отбрасываются: их пик
        /// целиком лежит дальше своего носителя от окна фита.
        /// </summary>
        static void Splat(double[] source, int[] bands, int pad, int channels, int channel, double weight,
                          int band, ref int srcLo, ref int srcHi)
        {
            if (!(weight > 0.0) || channel < -pad || channel > channels - 1 + pad)
            {
                return;
            }

            int idx = channel + pad;
            if (source[idx] == 0.0)
            {
                bands[idx] = band;
            }

            source[idx] += weight;
            if (idx < srcLo)
            {
                srcLo = idx;
            }

            if (idx > srcHi)
            {
                srcHi = idx;
            }
        }

        /// <summary>
        /// Запас буфера источников по обе стороны шкалы. Берётся двойной
        /// носитель профиля у верхнего канала: ПШПВ растёт по шкале как корень,
        /// и на длине запаса прибавить успевает единицы процентов.
        /// </summary>
        static int SourcePad(FwhmCalibration fwhmCalibration, int channels)
        {
            int pad = 64;
            double fwhm = fwhmCalibration.ChannelToFwhm(channels - 1);
            if (fwhm > 0.0 && !Double.IsNaN(fwhm))
            {
                double support = Math.Max(PeakShapeModel.GetLeftSupport(fwhmCalibration, fwhm),
                                          PeakShapeModel.GetRightSupport(fwhmCalibration, fwhm));
                if (support > 0.0 && !Double.IsNaN(support))
                {
                    pad = (int)Math.Ceiling(2.0 * support) + 8;
                }
            }

            if (pad < 64)
            {
                pad = 64;
            }

            if (pad > channels)
            {
                pad = channels;
            }

            return pad;
        }

        /// <summary>
        /// Таблица «номер бина отклика → канал» для текущей калибровки.
        /// </summary>
        double[] DepositChannels(EnergyCalibration calibration, double bin, int count, int channels)
        {
            if (this.depositChannels != null && this.depositChannels.Length >= count
                && this.depositChannelsBin == bin
                && object.ReferenceEquals(this.depositChannelsCalibration, calibration)
                && this.depositChannelsCount == channels)
            {
                return this.depositChannels;
            }

            double[] table = new double[count];
            for (int b = 0; b < count; b++)
            {
                table[b] = EnergyToChannelSafe(calibration, b * bin, channels);
            }

            this.depositChannels = table;
            this.depositChannelsBin = bin;
            this.depositChannelsCalibration = calibration;
            this.depositChannelsCount = channels;
            return table;
        }

        ShapeKernelBank Kernels(FwhmCalibration fwhmCalibration)
        {
            if (this.kernelBank == null || !object.ReferenceEquals(this.kernelBank.Calibration, fwhmCalibration))
            {
                this.kernelBank = new ShapeKernelBank(fwhmCalibration);
            }

            return this.kernelBank;
        }

        double[] sourceBuffer;
        int[] sourceBands;
        double[] depositChannels;
        double depositChannelsBin;
        EnergyCalibration depositChannelsCalibration;
        int depositChannelsCount;
        ShapeKernelBank kernelBank;

        /// <summary>
        /// Банк ядер уширения: профиль единичной площади, посчитанный в ЦЕЛЫХ
        /// смещениях от центра, для лестницы значений ПШПВ с шагом 0.2 %.
        ///
        /// Ядро зависит только от ПШПВ, поэтому при целом центре одно и то же
        /// ядро годится в любом канале — а центры целые, потому что источники
        /// разложены по целым каналам с линейным делением площади. Лестница
        /// нужна, чтобы ядро попадало в кэш: без округления ПШПВ у каждой
        /// группы своя и кэш не срабатывает никогда. Шаг 0.2 % — это ошибка
        /// ширины не более 0.1 %, вдесятеро меньше того, что вообще видно в
        /// разложении.
        ///
        /// Ядро нормировано на единичную СУММУ по всему носителю, как это
        /// делалось при отдельной укладке пика, — иначе площадь образа поехала
        /// бы у краёв шкалы, где носитель обрезан окном фита.
        /// </summary>
        sealed class ShapeKernelBank
        {
            static readonly double LogRatio = Math.Log(1.002);

            readonly FwhmCalibration calibration;
            readonly Dictionary<int, double[]> values = new Dictionary<int, double[]>();
            readonly Dictionary<int, int> lefts = new Dictionary<int, int>();

            public ShapeKernelBank(FwhmCalibration calibration)
            {
                this.calibration = calibration;
            }

            public FwhmCalibration Calibration
            {
                get { return this.calibration; }
            }

            /// <summary>Номер ступени лестницы для заданной ПШПВ.</summary>
            public static int Band(double fwhm)
            {
                return (int)Math.Floor(Math.Log(fwhm) / LogRatio + 0.5);
            }

            /// <summary>Ядро ступени или null, если профиля на ней нет.</summary>
            public double[] Get(int band)
            {
                double[] kernel;
                if (this.values.TryGetValue(band, out kernel))
                {
                    return kernel;
                }

                double fwhm = Math.Exp(band * LogRatio);
                double left = PeakShapeModel.GetLeftSupport(this.calibration, fwhm);
                double right = PeakShapeModel.GetRightSupport(this.calibration, fwhm);
                int leftSpan = 0;
                if (left > 0.0 && right > 0.0 && !Double.IsNaN(left) && !Double.IsNaN(right))
                {
                    leftSpan = (int)Math.Ceiling(left);
                    int rightSpan = (int)Math.Ceiling(right);
                    int span = leftSpan + rightSpan + 1;
                    double[] shape = new double[span];
                    double area = 0.0;
                    for (int i = 0; i < span; i++)
                    {
                        double v = PeakShapeModel.RelativeValue(i - leftSpan, fwhm, this.calibration);
                        shape[i] = v;
                        area += v;
                    }

                    if (area > 0.0)
                    {
                        for (int i = 0; i < span; i++)
                        {
                            shape[i] /= area;
                        }

                        kernel = shape;
                    }
                }

                if (kernel == null)
                {
                    leftSpan = 0;
                }

                this.values[band] = kernel;
                this.lefts[band] = leftSpan;
                return kernel;
            }

            /// <summary>Смещение центра ядра от его начала, в каналах.</summary>
            public int LeftSpan(int band)
            {
                int left;
                return this.lefts.TryGetValue(band, out left) ? left : 0;
            }
        }

        double[] BuildTemplate(FsaComponent component, EnergyCalibration calibration,
                                      FwhmCalibration fwhmCalibration, FsaEfficiency efficiency,
                                      double gain, double offset, int chLo, int chHi, int channels)
        {
            if (this.ResponseMatrix != null && !component.WeightsAreFinal)
            {
                return this.BuildTemplateFromResponse(component, calibration, fwhmCalibration,
                                                      gain, offset, chLo, chHi, channels);
            }

            double[] template = new double[channels];
            // Значения профиля считаются один раз на носитель и переиспользуются
            // между линиями: образ строится заново на каждом узле сетки дрейфа
            // (9x9 = 81 раз на компонент), и второй проход по носителю ради
            // площади удваивал бы счёт профиля на канал.
            double[] shape = null;
            bool any = false;
            foreach (FsaLine line in component.Lines)
            {
                double position = EnergyToChannelSafe(calibration, line.Energy, channels);
                if (Double.IsNaN(position))
                {
                    continue;
                }

                double p = gain * position + offset;
                double fwhm = fwhmCalibration.ChannelToFwhm(p);
                if (fwhm <= 0.0 || Double.IsNaN(fwhm))
                {
                    continue;
                }

                double weight = line.Intensity / 100.0;
                if (efficiency != null && !component.WeightsAreFinal)
                {
                    double e = efficiency.Eval(line.Energy);
                    if (e <= 0.0)
                    {
                        continue;
                    }

                    weight *= e;
                }

                if (weight <= 0.0)
                {
                    continue;
                }

                double left = PeakShapeModel.GetLeftSupport(fwhmCalibration, fwhm);
                double right = PeakShapeModel.GetRightSupport(fwhmCalibration, fwhm);
                if (!(left > 0.0) || !(right > 0.0))
                {
                    continue;
                }

                int full0 = (int)Math.Floor(p - left);
                int full1 = (int)Math.Ceiling(p + right);
                int span = full1 - full0 + 1;
                if (span <= 0)
                {
                    continue;
                }

                if (shape == null || shape.Length < span)
                {
                    shape = new double[span];
                }

                double area = 0.0;
                for (int i = 0; i < span; i++)
                {
                    double v = PeakShapeModel.RelativeValue(full0 + i - p, fwhm, fwhmCalibration);
                    shape[i] = v;
                    area += v;
                }

                if (!(area > 0.0))
                {
                    continue;
                }

                int lo = Math.Max(chLo, full0);
                int hi = Math.Min(chHi, full1);
                if (hi < lo)
                {
                    continue;
                }

                double norm = weight / area;
                for (int i = lo; i <= hi; i++)
                {
                    template[i] += norm * shape[i - full0];
                }

                any = true;
            }

            return any ? template : null;
        }

        /// <summary>
        /// Кусочно-линейный базис («шапки») для континуума. Шаг узлов привязан к
        /// локальной ПШПВ, чтобы континуум не мог поглотить одиночный пик, но не
        /// реже чем 1/64 диапазона.
        /// </summary>
        static List<double[]> BuildHatBasis(FwhmCalibration fwhmCalibration, int chLo, int chHi, int channels)
        {
            List<int> knots = new List<int>();
            double minStep = (chHi - chLo) / 64.0;
            int ch = chLo;
            while (ch < chHi)
            {
                knots.Add(ch);
                double fwhm = fwhmCalibration.ChannelToFwhm(ch);
                if (Double.IsNaN(fwhm) || fwhm < 1.0)
                {
                    fwhm = 1.0;
                }

                ch += (int)Math.Max(1.0, Math.Max(4.0 * fwhm, minStep));
            }

            // Узел в одном канале от верхней границы дал бы «шапку-спицу» —
            // почти коллинеарную колонку. Сливается только этот вырожденный
            // случай: более широкий порог снимает легитимный узел и дубит
            // континуум у верхнего края.
            if (knots.Count > 1 && chHi - knots[knots.Count - 1] < 2)
            {
                knots.RemoveAt(knots.Count - 1);
            }

            knots.Add(chHi);

            List<double[]> hats = new List<double[]>();
            for (int k = 0; k < knots.Count; k++)
            {
                int left = k > 0 ? knots[k - 1] : knots[k];
                int mid = knots[k];
                int right = k + 1 < knots.Count ? knots[k + 1] : knots[k];
                double[] hat = new double[channels];
                for (int i = left; i <= right; i++)
                {
                    double v;
                    if (i == mid)
                    {
                        v = 1.0;
                    }
                    else if (i < mid)
                    {
                        v = left == mid ? 1.0 : (double)(i - left) / (mid - left);
                    }
                    else
                    {
                        v = right == mid ? 1.0 : (double)(right - i) / (right - mid);
                    }

                    if (v > 0.0)
                    {
                        hat[i] = v;
                    }
                }

                hats.Add(hat);
            }

            return hats;
        }

        static int[] Snip(FwhmCalibration fwhmCalibration, EnergySpectrum spectrum)
        {
            SpectrumAriphmetics ariphmetics = new SpectrumAriphmetics(fwhmCalibration, spectrum, SmoothingMethod.None);
            try
            {
                EnergySpectrum continuum = ariphmetics.Continuum();
                return continuum != null ? continuum.Spectrum : null;
            }
            finally
            {
                ariphmetics.Dispose();
            }
        }

        /// <summary>Быстрый NNLS (Bro &amp; de Jong) на готовых нормальных уравнениях.</summary>
        static double[] NnlsSolve(double[,] gram, double[] c, int m, out bool[] active)
        {
            double[] x = new double[m];
            active = new bool[m];
            // Колонки, добавление которых дало сингулярную активную матрицу
            // (дубликат или коллинеарность): без бана градиент не меняется, и
            // внешний цикл выбирал бы тот же индекс до исчерпания бюджета.
            bool[] banned = new bool[m];
            double tol = 1e-10 * MaxDiagonal(gram, m);
            double[] w = (double[])c.Clone();

            for (int iteration = 0; iteration < 30 * m; iteration++)
            {
                int j = -1;
                double wmax = tol;
                for (int k = 0; k < m; k++)
                {
                    if (!active[k] && !banned[k] && w[k] > wmax)
                    {
                        wmax = w[k];
                        j = k;
                    }
                }

                if (j < 0)
                {
                    break;
                }

                active[j] = true;
                while (true)
                {
                    double[] z = SolveActive(gram, c, active, m);
                    if (z == null)
                    {
                        active[j] = false;
                        banned[j] = true;
                        break;
                    }

                    bool allPositive = true;
                    double alpha = 1.0;
                    for (int k = 0; k < m; k++)
                    {
                        if (active[k] && z[k] <= 0.0)
                        {
                            allPositive = false;
                            double a = x[k] / (x[k] - z[k]);
                            if (a < alpha)
                            {
                                alpha = a;
                            }
                        }
                    }

                    if (allPositive)
                    {
                        for (int k = 0; k < m; k++)
                        {
                            x[k] = active[k] ? z[k] : 0.0;
                        }

                        break;
                    }

                    for (int k = 0; k < m; k++)
                    {
                        if (!active[k])
                        {
                            continue;
                        }

                        x[k] += alpha * (z[k] - x[k]);
                        if (x[k] <= tol)
                        {
                            x[k] = 0.0;
                            active[k] = false;
                        }
                    }
                }

                for (int a = 0; a < m; a++)
                {
                    double s = c[a];
                    for (int b = 0; b < m; b++)
                    {
                        if (x[b] != 0.0)
                        {
                            s -= gram[a, b] * x[b];
                        }
                    }

                    w[a] = s;
                }
            }

            return x;
        }

        static double MaxDiagonal(double[,] gram, int m)
        {
            double max = 0.0;
            for (int k = 0; k < m; k++)
            {
                if (gram[k, k] > max)
                {
                    max = gram[k, k];
                }
            }

            return max > 0.0 ? max : 1.0;
        }

        static double[] SolveActive(double[,] gram, double[] c, bool[] active, int m)
        {
            List<int> index = new List<int>();
            for (int k = 0; k < m; k++)
            {
                if (active[k])
                {
                    index.Add(k);
                }
            }

            int n = index.Count;
            if (n == 0)
            {
                return null;
            }

            double[,] a = new double[n, n + 1];
            for (int r = 0; r < n; r++)
            {
                for (int q = 0; q < n; q++)
                {
                    a[r, q] = gram[index[r], index[q]];
                }

                a[r, n] = c[index[r]];
            }

            if (!GaussSolve(a, n))
            {
                return null;
            }

            double[] z = new double[m];
            for (int r = 0; r < n; r++)
            {
                z[index[r]] = a[r, n];
            }

            return z;
        }

        static bool GaussSolve(double[,] a, int n)
        {
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                    {
                        pivot = r;
                    }
                }

                if (Math.Abs(a[pivot, col]) < 1e-30)
                {
                    return false;
                }

                if (pivot != col)
                {
                    for (int q = col; q <= n; q++)
                    {
                        double tmp = a[col, q];
                        a[col, q] = a[pivot, q];
                        a[pivot, q] = tmp;
                    }
                }

                for (int r = 0; r < n; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }

                    double f = a[r, col] / a[col, col];
                    if (f == 0.0)
                    {
                        continue;
                    }

                    for (int q = col; q <= n; q++)
                    {
                        a[r, q] -= f * a[col, q];
                    }
                }
            }

            for (int r = 0; r < n; r++)
            {
                a[r, n] /= a[r, r];
            }

            return true;
        }

        static double[,] InvertSymmetric(double[,] source, int n)
        {
            double[,] a = new double[n, 2 * n];
            for (int r = 0; r < n; r++)
            {
                for (int q = 0; q < n; q++)
                {
                    a[r, q] = source[r, q];
                }

                a[r, n + r] = 1.0;
            }

            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                    {
                        pivot = r;
                    }
                }

                if (Math.Abs(a[pivot, col]) < 1e-30)
                {
                    return null;
                }

                if (pivot != col)
                {
                    for (int q = 0; q < 2 * n; q++)
                    {
                        double tmp = a[col, q];
                        a[col, q] = a[pivot, q];
                        a[pivot, q] = tmp;
                    }
                }

                double d = a[col, col];
                for (int q = 0; q < 2 * n; q++)
                {
                    a[col, q] /= d;
                }

                for (int r = 0; r < n; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }

                    double f = a[r, col];
                    if (f == 0.0)
                    {
                        continue;
                    }

                    for (int q = 0; q < 2 * n; q++)
                    {
                        a[r, q] -= f * a[col, q];
                    }
                }
            }

            double[,] inverse = new double[n, n];
            for (int r = 0; r < n; r++)
            {
                for (int q = 0; q < n; q++)
                {
                    inverse[r, q] = a[r, n + q];
                }
            }

            return inverse;
        }

        static double SumRange(double[] values, int lo, int hi)
        {
            double sum = 0.0;
            for (int i = lo; i <= hi; i++)
            {
                sum += values[i];
            }

            return sum;
        }

        static double EnergyToChannelSafe(EnergyCalibration calibration, double energy, int channels)
        {
            try
            {
                return calibration.EnergyToChannel(energy, maxChannels: channels);
            }
            catch (Exception)
            {
                return Double.NaN;
            }
        }

        static int ClampChannel(double channel, int channels)
        {
            if (Double.IsNaN(channel) || channel < 0.0)
            {
                return 0;
            }

            if (channel > channels - 1)
            {
                return channels - 1;
            }

            return (int)Math.Round(channel);
        }
    }
}
