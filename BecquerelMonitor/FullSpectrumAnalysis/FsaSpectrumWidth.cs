using System;
using System.Threading;
using BecquerelMonitor.Utils;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    // Resolution belongs to the measurement. The local peak fits use neither response model.
    internal struct FsaSpectrumWidth
    {
        internal double Scale, LogSigma;
        internal int Peaks;

        internal static FsaSpectrumWidth Measure(EnergySpectrum spectrum, EnergySpectrum background,
            FwhmCalibration calibration, int[] centers, FsaKernelSampling sampling, double maxRatio, double sigmaFloor, CancellationToken token)
        {
            var result = new FsaSpectrumWidth { Scale = 1.0 };
            if (centers.Length == 0 || calibration == null || spectrum?.Spectrum == null)
                return result;
            int channels = Math.Min(spectrum.NumberOfChannels, spectrum.Spectrum.Length);
            // Apply the same count, channel and live-time checks as the FSA background subtraction.
            double[] bg = null;
            double timeRatio = 0;
            if (background?.Spectrum != null && background.NumberOfChannels == channels
                && background.Spectrum.Length == channels && background.EffectiveLiveTime > 0 && spectrum.EffectiveLiveTime > 0)
            {
                var from = background.EnergyCalibration;
                var to = spectrum.EnergyCalibration;
                bg = from != null && to != null && !to.Equals(from)
                    ? SpectrumAriphmetics.RebinByEnergy(background.Spectrum, from, to, channels)
                    : Array.ConvertAll(background.Spectrum, count => (double)count);
                timeRatio = spectrum.EffectiveLiveTime / background.EffectiveLiveTime;
            }
            if (!(maxRatio > 1)) maxRatio = double.PositiveInfinity;
            if (!(sigmaFloor > 0)) sigmaFloor = 0;
            double weight = 0, sum = 0, sumSquares = 0;
            for (int i = 0; i < centers.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                int center = centers[i];
                if (center < 0 || center >= channels) continue;
                double width = calibration.ChannelToFwhm(center);
                if (!(width >= 2 && width < channels / 3.0)) continue;
                int span = (int)Math.Ceiling(1.7 * width);
                if (center < span || center + span >= channels || span < 4) continue;
                if ((i > 0 && center - centers[i - 1] <= span)
                    || (i + 1 < centers.Length && centers[i + 1] - center <= span)) continue;

                double logRatio, sigma;
                if (!FitPeak(spectrum.Spectrum, bg, timeRatio, calibration, center,
                             span, width, sampling, maxRatio, out logRatio, out sigma)) continue;
                double precision = 1.0 / (sigma * sigma);
                weight += precision;
                sum += precision * logRatio;
                sumSquares += precision * logRatio * logRatio;
                result.Peaks++;
            }
            if (result.Peaks > 0)
            {
                double mean = sum / weight;
                double scatter = result.Peaks > 1
                    ? Math.Max(1, (sumSquares - sum * mean) / (result.Peaks - 1)) : 1;
                result.Scale = Math.Exp(mean);
                result.LogSigma = Math.Sqrt(scatter / weight + sigmaFloor * sigmaFloor);
            }
            return result;
        }

        static bool FitPeak(int[] counts, double[] background, double timeRatio, FwhmCalibration calibration,
                            int center, int span, double width, FsaKernelSampling sampling, double maxRatio, out double logRatio, out double sigma)
        {
            logRatio = sigma = 0;
            int length = 2 * span + 1;
            var vectors = Vector<double>.Build;
            var x = vectors.Dense(length, i => (i - span) / width);
            var y = vectors.Dense(length);
            var weights = vectors.Dense(length);
            for (int i = 0; i < length; i++)
            {
                int ch = center - span + i;
                double bg = background != null ? background[ch] : 0;
                y[i] = counts[ch] - timeRatio * bg;
                weights[i] = 1.0 / Math.Max(1, counts[ch] + timeRatio * timeRatio * bg);
            }
            double baseline = (y[0] + y[length - 1]) * 0.5;
            double height = y[span] - baseline;
            if (!(height > 0)) return false;

            // p = local baseline, slope, peak height, centroid offset and relative width.
            Func<Vector<double>, double, double> profile = (p, t) => p[0] + p[1] * t
                + p[2] * FsaAnalyzer.ChannelShapeValue((t - p[3]) * width, p[4] * width, calibration, sampling);
            var model = ObjectiveFunction.NonlinearModel(profile, x, y, weights);
            try
            {
                var fit = new LevenbergMarquardtMinimizer(maximumIterations: 40)
                    .FindMinimum(model, new[] { baseline, 0, height, 0, 1 });
                if (fit.ReasonForExit == ExitCondition.InvalidValues || fit.ReasonForExit == ExitCondition.ExceedIterations)
                    return false;
                var p = fit.MinimizingPoint;
                if (!(p[2] > 0 && Math.Abs(p[3]) < 0.5 && p[4] > 1.0 / maxRatio && p[4] < maxRatio))
                    return false;
                double reduced = fit.ModelInfoAtMinimum.Value / (length - p.Count);
                if (!(reduced > 0 && reduced <= 3)) return false;
                var errors = fit.StandardErrors;
                if (errors == null) return false;
                double noise = Math.Max(1, 1.0 / Math.Sqrt(reduced));
                sigma = errors[4] * noise / p[4];
                if (!(sigma > 0 && sigma < 0.10 && p[2] > 5 * errors[2] * noise)) return false;
                double measuredChannel = center + p[3] * width;
                logRatio = Math.Log(p[4] * width / calibration.ChannelToFwhm(measuredChannel));
                return !double.IsNaN(logRatio) && !double.IsInfinity(logRatio)
                    && Math.Abs(logRatio) < Math.Log(maxRatio);
            }
            catch (MathNet.Numerics.NonConvergenceException) { return false; }
            catch (ArgumentException) { return false; }
            catch (ArithmeticException) { return false; }
        }
    }
}
