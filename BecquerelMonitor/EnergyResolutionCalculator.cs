using BecquerelMonitor.Utils;
using System;
using Windows.UI.Xaml.Documents;

namespace BecquerelMonitor
{
    public class EnergyResolutionCalculator
    {
        public static EnergyResolutionResult CalculateFWHM(EnergySpectrum spectrum, int startChannel, int endChannel)
        {
            if (spectrum == null || spectrum.Spectrum == null) return null;
            int[] source = spectrum.Spectrum;
            double[] counts = new double[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                counts[i] = source[i];
            }

            return CalculateFWHM(counts, spectrum.NumberOfChannels, spectrum.EnergyCalibration,
                                 startChannel, endChannel);
        }

        /// <summary>
        /// То же по ГОТОВЫМ отсчётам (`A45`). Нужен тем видам спектра, у которых
        /// нарисованное не лежит в `EnergySpectrum`: в режиме FSA на экране спектр
        /// за вычетом фона (`FsaNetSpectrum`), и линии полуширины обязаны идти по
        /// нему, а не по сырым отсчётам — иначе они висят над кривой.
        ///
        /// ⚠ Отсчёты здесь ДРОБНЫЕ и могут быть отрицательными: фон вычтен с
        /// поправкой на время. Алгоритм от этого не меняется — он и раньше считал
        /// в double, целыми были только входные ячейки.
        /// </summary>
        public static EnergyResolutionResult CalculateFWHM(double[] counts, int numberOfChannels,
                                                           EnergyCalibration energyCalibration,
                                                           int startChannel, int endChannel)
        {
            if (counts == null || energyCalibration == null) return null;
            if (startChannel >= endChannel || numberOfChannels < endChannel) return null;
            int centroid = startChannel;
            if (counts.Length <= endChannel) return null;
            double centroid_counts = counts[centroid];
            for (int i = startChannel; i <= endChannel; i++)
            {
                if (counts[i] - SpectrumAriphmetics.getY(i, startChannel, endChannel, counts[startChannel], counts[endChannel])
                    > centroid_counts - SpectrumAriphmetics.getY(centroid, startChannel, endChannel, counts[startChannel], counts[endChannel]))
                {
                    centroid_counts = counts[i];
                    centroid = i;
                }
            }

            double start_counts = counts[startChannel];
            double end_counts = counts[endChannel];
            double maxBaseValue = start_counts + (end_counts - start_counts) * (double)(centroid - startChannel) / (double)(endChannel - startChannel);
            double halfValue = (centroid_counts - maxBaseValue) / 2.0 + maxBaseValue;

            // ⛔ (`AMBER181`, 05.10.2026) ПОЛУВЫСОТА — НАД НАКЛОННОЙ БАЗОЙ, а не на
            // постоянном уровне. Прежде отсчёты каждого канала сравнивались с ОДНИМ
            // числом `halfValue` — полувысотой над базой ПОД ВЕРШИНОЙ, — и на склоне
            // подложки крыло со стороны высокой базы пересекало этот уровень раньше,
            // а со стороны низкой — позже. Счёт ревизии (гаусс σ = 10 каналов,
            // истинная ПШПВ 23.55, подложка 5000): наклон −20 отсч./канал, амплитуда
            // 1000 → 33.70 (+43 %); −60 → 14.01 (−40 %); амплитуда 300 → 4.83 (−80 %).
            // Число шло в панель выделения и ОПОРОЙ в кривую ПШПВ.
            // Теперь сравнивается ЧИСТЫЙ отсчёт `counts[j] − base(j)` (база — та же
            // прямая между краями выделения, что и у поиска вершины выше) с половиной
            // высоты над базой; пересечение — линейной интерполяцией чистого отсчёта
            // между соседними каналами. При ровной базе это прежний расчёт.
            // `HalfValue` результата — по-прежнему уровень полувысоты У ВЕРШИНЫ; линия
            // полувысоты на графике идёт параллельно базе (`EnergySpectrumView`).
            double topNet = centroid_counts - maxBaseValue;
            if (!(topNet > 0.0)) return null;
            double baseSlope = (end_counts - start_counts) / (double)(endChannel - startChannel);
            Func<int, double> net = ch => counts[ch] - (start_counts + baseSlope * (double)(ch - startChannel));
            double leftChannel, rightChannel;
            if (!HalfCrossings(net, startChannel, endChannel, centroid, topNet / 2.0,
                               out leftChannel, out rightChannel))
            {
                return null;
            }

            // ⛔ (`AMBER231`, решение Amber 17.10.2026 «Чинить сейчас») ВЫСОТА ПИКА —
            // НЕ ОДИН КАНАЛ. Канал с наибольшим отсчётом из-за шума систематически
            // выше настоящей вершины, полувысота встаёт выше, и ширина выходит уже:
            // замер на синтетике (окно ±1.5 FWHM, 1500 выделений), σ = 8 каналов —
            // −2.2 % при 1000 отсчётов в вершине, −4.0 % при 300, −6.3 % при 100;
            // число идёт ОПОРОЙ в кривую ПШПВ (`PeakPickuped`). Теперь высота —
            // вершина параболы по ЛОГАРИФМУ чистых отсчётов в ±0.35 предварительной
            // ширины (у гаусса она точная, Caruana); тот же замер: −0.2 / +0.4 / +2.4 %,
            // а у узкого пика (σ = 1.3 канала) смещение и в обилии счёта меньше:
            // +1.2 % против +3.2 % (остаток — линейная интерполяция пересечения
            // между каналами). Не годится подгонка — остаётся вершина по каналу.
            double fittedTop;
            if (TryLogParabolaTop(net, startChannel, endChannel, centroid,
                                  rightChannel - leftChannel, out fittedTop))
            {
                double l, r;
                if (HalfCrossings(net, startChannel, endChannel, centroid, fittedTop / 2.0, out l, out r))
                {
                    topNet = fittedTop;
                    leftChannel = l;
                    rightChannel = r;
                }
            }
            halfValue = maxBaseValue + topNet / 2.0;

            double leftEnergy = energyCalibration.ChannelToEnergy(leftChannel);
            double rightEnergy = energyCalibration.ChannelToEnergy(rightChannel);
            double resolution = (rightEnergy - leftEnergy) / energyCalibration.ChannelToEnergy((double)centroid);
            double resolutioninkev = rightEnergy - leftEnergy;



            EnergyResolutionResult result = new EnergyResolutionResult();
            result.StartChannel = (double)startChannel;
            result.EndChannel = (double)endChannel;
            result.StartValue = start_counts;
            result.EndValue = end_counts;
            result.MaxBaseValue = maxBaseValue;
            result.LeftChannel = leftChannel;
            result.RightChannel = rightChannel;
            result.HalfValue = halfValue;
            result.MaxChannel = (double)centroid;
            // (`AMBER231`) Вершина на графике — та высота, от которой взята
            // полувысота, а не отсчёт шумного канала: иначе жёлтая линия
            // полувысоты не делила бы вертикаль пополам.
            result.MaxValue = maxBaseValue + topNet;
            result.Resolution = resolution;
            result.ResolutionInkeV = resolutioninkev;
            return result;
        }

        /// <summary>
        /// Точки полувысоты чистого отсчёта: слева — от края выделения к вершине,
        /// справа — от края к вершине; пересечение — линейной интерполяцией между
        /// соседними каналами. false — пересечения нет или крыло не монотонно у
        /// уровня (прежний отказ).
        /// </summary>
        static bool HalfCrossings(Func<int, double> net, int startChannel, int endChannel, int centroid,
                                  double halfNet, out double leftChannel, out double rightChannel)
        {
            leftChannel = -1.0;
            rightChannel = -1.0;
            // До вершины ВКЛЮЧИТЕЛЬНО: пересечение между centroid − 1 и centroid
            // (узкий пик, полуширина меньше канала) прежде давало отказ.
            for (int j = startChannel + 1; j <= centroid; j++)
            {
                double nj = net(j);
                if (nj > halfNet)
                {
                    double nPrev = net(j - 1);
                    if (!(nj > nPrev)) return false;
                    leftChannel = (double)(j - 1) + (halfNet - nPrev) / (nj - nPrev);
                    break;
                }
            }
            if (leftChannel < 0.0) return false;
            for (int k = endChannel - 1; k >= centroid; k--)
            {
                double nk = net(k);
                if (nk > halfNet)
                {
                    double nNext = net(k + 1);
                    if (!(nk > nNext)) return false;
                    rightChannel = (double)(k + 1) - (halfNet - nNext) / (nk - nNext);
                    break;
                }
            }
            return rightChannel >= 0.0;
        }

        /// <summary>
        /// (`AMBER231`) Высота пика над базой — вершиной параболы, подогнанной МНК к
        /// ln(чистого отсчёта) в каналах centroid ± h, h = max(1, round(0.35 · ширины
        /// первого прохода)). false — подгонка не годится: окно выходит за выделение,
        /// чистый отсчёт в окне ≤ 0 (у FSA за вычетом фона бывает), у параболы нет
        /// максимума или её вершина вне окна.
        /// </summary>
        static bool TryLogParabolaTop(Func<int, double> net, int startChannel, int endChannel, int centroid,
                                      double roughWidth, out double top)
        {
            top = 0.0;
            if (!(roughWidth > 0.0)) return false;
            int h = Math.Max(1, (int)Math.Round(0.35 * roughWidth));
            if (centroid - h < startChannel || centroid + h > endChannel) return false;

            // Нормальные уравнения для y = p0 + p1·t + p2·t², t = канал − centroid.
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, v0 = 0, v1 = 0, v2 = 0;
            for (int t = -h; t <= h; t++)
            {
                double value = net(centroid + t);
                if (!(value > 0.0)) return false;
                double y = Math.Log(value);
                double t2 = (double)t * t;
                s0 += 1.0; s1 += t; s2 += t2; s3 += t2 * t; s4 += t2 * t2;
                v0 += y; v1 += y * t; v2 += y * t2;
            }

            // Окно симметрично, поэтому s1 = s3 = 0: p1 отделяется, p0 и p2 — система 2×2.
            double det = s0 * s4 - s2 * s2;
            if (!(Math.Abs(det) > 0.0) || !(s2 > 0.0)) return false;
            double p1 = v1 / s2;
            double p0 = (v0 * s4 - v2 * s2) / det;
            double p2 = (s0 * v2 - s2 * v0) / det;
            if (!(p2 < 0.0)) return false;
            double vertex = -p1 / (2.0 * p2);
            if (Math.Abs(vertex) > h) return false;
            double logTop = p0 - p1 * p1 / (4.0 * p2);
            if (double.IsNaN(logTop) || double.IsInfinity(logTop)) return false;
            top = Math.Exp(logTop);
            return top > 0.0 && !double.IsInfinity(top);
        }
    }
}
