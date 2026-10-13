using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace ResidualFloorProbeP168
{
    /// <summary>
    /// (`AMBER126`, полоса П168 28.09.2026) ШУМОВОЙ ПОЛ СТРОКИ НЕВЯЗКИ при
    /// ИДЕАЛЬНОЙ модели. Модель = истинное среднее по каналам (гладкий спектр:
    /// спад плюс гауссов пик), измерение — пуассоновы копии; фон по желанию —
    /// пуассонова копия своего среднего, вычитаемая с множителем 1. Доли
    /// «не описано» / «лишнее» считает сам <see cref="FsaResult.ComputeResidualShares"/>
    /// — то же, что печатает окно отчёта; пол (<c>ResidualNoiseShare</c>, с П168)
    /// читается отражением, чтобы проба собиралась и на дереве без него.
    ///
    ///   ResidualFloorProbeP168 --channels=8192 --net=10000 [--bg=0] [--copies=400] [--seed=1]
    ///
    /// `--net=` — среднее число отсчётов источника в полосе, `--bg=` — фона
    /// (та же форма спада, без пика). Печать: медиана и среднее обеих долей,
    /// доля копий с определённой долей, значимость net/σ, пол (если есть) —
    /// медиана по копиям.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            int channels = 8192, copies = 400, seed = 1;
            double net = 1.0e4, bg = 0.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--channels=", StringComparison.Ordinal)) channels = int.Parse(a.Substring(11), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--net=", StringComparison.Ordinal)) net = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bg=", StringComparison.Ordinal)) bg = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--copies=", StringComparison.Ordinal)) copies = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            // Форма: спад exp(−i/(n/4)) — 70 %, пик в середине шириной n/200 — 30 %.
            double[] shape = new double[channels], bgShape = new double[channels];
            double sumShape = 0.0, sumBg = 0.0;
            for (int i = 0; i < channels; i++)
            {
                double decay = Math.Exp(-i / (channels / 4.0));
                double sigma = channels / 470.0;
                double peak = Math.Exp(-0.5 * Math.Pow((i - channels / 2.0) / sigma, 2));
                shape[i] = 0.7 * decay + 0.3 * peak * (channels / 4.0) / (sigma * Math.Sqrt(2 * Math.PI)) / 1.0;
                bgShape[i] = decay;
                sumShape += shape[i];
                sumBg += decay;
            }

            double[] model = new double[channels], bgMean = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                model[i] = net * shape[i] / sumShape;
                bgMean[i] = bg * bgShape[i] / sumBg;
            }

            PropertyInfo floorProperty = typeof(FsaResult).GetProperty("ResidualNoiseShare");
            var random = new Random(seed);
            var missing = new List<double>();
            var excess = new List<double>();
            var floors = new List<double>();
            int defined = 0;
            for (int c = 0; c < copies; c++)
            {
                int[] raw = new int[channels];
                double[] background = bg > 0.0 ? new double[channels] : null;
                for (int i = 0; i < channels; i++)
                {
                    raw[i] = Poisson(random, model[i] + bgMean[i]);
                    if (background != null)
                    {
                        background[i] = Poisson(random, bgMean[i]);
                    }
                }

                var result = new FsaResult
                {
                    Model = (double[])model.Clone(),
                    Background = background,
                    BackgroundScale = background != null ? 1.0 : 0.0,
                    FirstChannel = 0,
                    LastChannel = channels - 1,
                    ResidualFloorChannel = 0
                };
                result.ComputeResidualShares(raw);
                if (!result.ResidualSharesDefined)
                {
                    continue;
                }

                defined++;
                missing.Add(100.0 * result.ResidualMissingShare);
                excess.Add(100.0 * result.ResidualExcessShare);
                if (floorProperty != null)
                {
                    floors.Add(100.0 * (double)floorProperty.GetValue(result, null));
                }
            }

            double sigmaNet = Math.Sqrt(net + 2.0 * bg);
            Console.WriteLine("TOY\tканалов {0}\tnet {1}\tфон {2}\tзначимость net/σ {3:F1}\tкопий {4}\tопределено {5}",
                              channels, net, bg, net / sigmaNet, copies, defined);
            Console.WriteLine("SHARE\tне описано: медиана {0:F2} %, среднее {1:F2} %\tлишнее: медиана {2:F2} %, среднее {3:F2} %",
                              Median(missing), Mean(missing), Median(excess), Mean(excess));
            Console.WriteLine(floorProperty == null
                ? "FLOOR\tнет свойства ResidualNoiseShare (дерево до П168)"
                : string.Format(CultureInfo.InvariantCulture, "FLOOR\tпол: медиана {0:F2} %, среднее {1:F2} %",
                                Median(floors), Mean(floors)));
            return 0;
        }

        static int Poisson(Random random, double mean)
        {
            if (mean <= 0.0)
            {
                return 0;
            }

            if (mean > 60.0)
            {
                // Нормальное приближение с округлением — хвост на такой средней неважен.
                double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
                double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
                return Math.Max(0, (int)Math.Round(mean + Math.Sqrt(mean) * z));
            }

            double limit = Math.Exp(-mean), product = random.NextDouble();
            int k = 0;
            while (product > limit)
            {
                k++;
                product *= random.NextDouble();
            }

            return k;
        }

        static double Median(List<double> values)
        {
            if (values.Count == 0) return double.NaN;
            var sorted = new List<double>(values);
            sorted.Sort();
            int n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]);
        }

        static double Mean(List<double> values)
        {
            if (values.Count == 0) return double.NaN;
            double s = 0.0;
            foreach (double v in values) s += v;
            return s / values.Count;
        }
    }
}
