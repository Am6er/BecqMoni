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
    /// F_j — образ компонента (сумма гауссиан единичной площади по таблице
    /// линий: положение из энергетической калибровки, ширина из ПШПВ-калибровки,
    /// вес из выхода на распад родителя и кривой эффективности);
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
                    double value = background.Spectrum[i] * backgroundScale;
                    if (backgroundSnip != null)
                    {
                        // в режиме SNIP континуум фона уже сидит внутри оценки
                        // континуума переднего спектра — вычитается только пиковая часть
                        value = Math.Max(0.0, background.Spectrum[i] - backgroundSnip[i]) * backgroundScale;
                    }

                    backgroundCurve[i] = value;
                    y[i] -= value;
                    variance[i] += Math.Max(Math.Abs(value) * backgroundScale, backgroundScale * backgroundScale);
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
                    FitResult refit = FitHuber(library, fixedColumns, calibration, fwhmCalibration, efficiency,
                                               bestGain, bestOffset, chLo, chHi, channels, y, variance, baseWeights, keep);
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
        /// Образ компонента: гауссианы единичной площади в позициях линий с
        /// учётом дрейфа шкалы, весами по выходу и кривой эффективности.
        /// </summary>
        static double[] BuildTemplate(FsaComponent component, EnergyCalibration calibration,
                                      FwhmCalibration fwhmCalibration, FsaEfficiency efficiency,
                                      double gain, double offset, int chLo, int chHi, int channels)
        {
            double[] template = new double[channels];
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

                double sigma = fwhm / 2.35482;
                double weight = line.Intensity / 100.0;
                if (efficiency != null)
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

                int lo = Math.Max(chLo, (int)Math.Floor(p - 5.0 * sigma));
                int hi = Math.Min(chHi, (int)Math.Ceiling(p + 5.0 * sigma));
                if (hi < lo)
                {
                    continue;
                }

                double norm = weight / (sigma * Math.Sqrt(2.0 * Math.PI));
                for (int i = lo; i <= hi; i++)
                {
                    double d = (i - p) / sigma;
                    template[i] += norm * Math.Exp(-0.5 * d * d);
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
