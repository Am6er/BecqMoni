using System;

namespace BecquerelMonitor.Utils
{
    // Публичный, а не internal: харнесс полноспектральной декомпозиции
    // (tools/pie) собирается против BecquerelMonitor.exe и обязан строить
    // образ ровно тем же профилем, что и приложение — иначе числа из
    // журнала перестают относиться к продукту.
    public static class PeakShapeModel
    {
        const double GaussianSigmaWindow = 8.0;
        const double TailAmplitudeCutoffLog = 10.0;

        public static double Evaluate(double channel, double amplitude, double center, double fwhm, FwhmCalibration calibration)
        {
            return amplitude * RelativeValue(channel - center, fwhm, calibration);
        }

        public static double RelativeValue(double offset, double fwhm, FwhmCalibration calibration)
        {
            if (!IsFinite(fwhm) || fwhm <= 0.0)
            {
                return 0.0;
            }

            int peakType = FwhmCalibration.GaussianPeakType;
            if (calibration != null && FwhmCalibration.IsSupportedPeakType(calibration.PeakType))
            {
                peakType = calibration.PeakType;
            }

            if (peakType == FwhmCalibration.ExpGaussExpPeakType && calibration != null)
            {
                if (!IsFinite(calibration.ExpGaussExpLeftTail) ||
                    !IsFinite(calibration.ExpGaussExpRightTail) ||
                    calibration.ExpGaussExpLeftTail <= 0.0 ||
                    calibration.ExpGaussExpRightTail <= 0.0)
                {
                    return GaussianRelativeValue(offset, fwhm);
                }

                return ExpGaussExpRelativeValue(offset, fwhm, calibration.ExpGaussExpLeftTail, calibration.ExpGaussExpRightTail);
            }

            if (peakType == FwhmCalibration.VoigtPeakType && calibration != null)
            {
                PseudoVoigtParameters parameters;
                if (PseudoVoigtProfile.TryCreate(fwhm, calibration.VoigtSigma, calibration.VoigtGamma, out parameters))
                {
                    return PseudoVoigtProfile.RelativeValue(offset, parameters);
                }
            }

            return GaussianRelativeValue(offset, fwhm);
        }

        /// <summary>
        /// (`AMBER315`, полоса `a315f` 14.11.2026) <see cref="RelativeValue(double, double, FwhmCalibration)"/>
        /// при ОДНИХ И ТЕХ ЖЕ ширине и форме пика, но для многих сдвигов — банк ядер формы FSA зовёт
        /// его по девять раз на канал ядра. Ветвь (тип пика, проверки хвостов, σ) выбирается один раз
        /// в <see cref="Prepare"/>, а значение считается ТЕМИ ЖЕ выражениями в том же порядке, что у
        /// <see cref="RelativeValue(double, double, FwhmCalibration)"/>, поэтому совпадает с ним
        /// побитно (σ = ПШПВ / <see cref="PseudoVoigtProfile.FwhmToSigma"/> — та же операция над теми
        /// же числами, вынесенная из цикла). Фойгт и всё необычное идут прежним вызовом.
        /// </summary>
        public struct Prepared
        {
            internal int Kind;          // 0 — ноль, 1 — гауссиана, 2 — exp-gauss-exp, 3 — прежний вызов
            internal double Sigma;
            internal double Left;
            internal double Right;
            internal double Fwhm;
            internal FwhmCalibration Calibration;
        }

        /// <summary>(`AMBER315`, a315f) Подготовить <see cref="Prepared"/> — ветвь <see cref="RelativeValue(double, double, FwhmCalibration)"/> для этой ширины.</summary>
        public static Prepared Prepare(double fwhm, FwhmCalibration calibration)
        {
            var p = new Prepared { Kind = 3, Fwhm = fwhm, Calibration = calibration };
            if (!IsFinite(fwhm) || fwhm <= 0.0)
            {
                p.Kind = 0;
                return p;
            }

            int peakType = FwhmCalibration.GaussianPeakType;
            if (calibration != null && FwhmCalibration.IsSupportedPeakType(calibration.PeakType))
            {
                peakType = calibration.PeakType;
            }

            bool gaussian;
            if (peakType == FwhmCalibration.ExpGaussExpPeakType && calibration != null)
            {
                if (!IsFinite(calibration.ExpGaussExpLeftTail) ||
                    !IsFinite(calibration.ExpGaussExpRightTail) ||
                    calibration.ExpGaussExpLeftTail <= 0.0 ||
                    calibration.ExpGaussExpRightTail <= 0.0)
                {
                    gaussian = true;
                }
                else
                {
                    gaussian = false;
                    p.Left = calibration.ExpGaussExpLeftTail;
                    p.Right = calibration.ExpGaussExpRightTail;
                }
            }
            else if (peakType == FwhmCalibration.VoigtPeakType && calibration != null)
            {
                return p;   // Фойгт — прежним вызовом
            }
            else
            {
                gaussian = true;
            }

            double sigma = fwhm / PseudoVoigtProfile.FwhmToSigma;
            if (sigma <= 0.0)
            {
                p.Kind = 0;
                return p;
            }

            p.Sigma = sigma;
            p.Kind = gaussian ? 1 : 2;
            return p;
        }

        /// <summary>(`AMBER315`, a315f) То же, что <see cref="RelativeValue(double, double, FwhmCalibration)"/> с подготовленной ветвью, побитно.</summary>
        public static double RelativeValue(double offset, ref Prepared p)
        {
            switch (p.Kind)
            {
                case 0:
                    return 0.0;
                case 1:
                {
                    double t = offset / p.Sigma;
                    return Math.Exp(-0.5 * t * t);
                }
                case 2:
                {
                    double t = offset / p.Sigma;
                    double right = p.Right;
                    double left = p.Left;
                    if (t > right)
                    {
                        return Math.Exp(0.5 * right * right - right * t);
                    }
                    if (t > -left)
                    {
                        return Math.Exp(-0.5 * t * t);
                    }
                    return Math.Exp(0.5 * left * left + left * t);
                }
                default:
                    return RelativeValue(offset, p.Fwhm, p.Calibration);
            }
        }

        public static double GetLeftSupport(FwhmCalibration calibration, double fwhm)
        {
            if (!IsFinite(fwhm) || fwhm <= 0.0)
            {
                return 0.0;
            }

            double sigma = fwhm / PseudoVoigtProfile.FwhmToSigma;
            if (sigma <= 0.0)
            {
                return 0.0;
            }

            if (calibration != null && calibration.PeakType == FwhmCalibration.ExpGaussExpPeakType)
            {
                return TailSupport(calibration.ExpGaussExpLeftTail) * sigma;
            }

            if (calibration != null && calibration.PeakType == FwhmCalibration.VoigtPeakType)
            {
                double halfWidth = PseudoVoigtProfile.HalfWidthAtRelativeHeight(
                    fwhm,
                    calibration.VoigtSigma,
                    calibration.VoigtGamma,
                    Math.Exp(-TailAmplitudeCutoffLog));
                return halfWidth > 0.0 ? halfWidth : GaussianSigmaWindow * sigma;
            }

            return GaussianSigmaWindow * sigma;
        }

        public static double GetRightSupport(FwhmCalibration calibration, double fwhm)
        {
            if (!IsFinite(fwhm) || fwhm <= 0.0)
            {
                return 0.0;
            }

            double sigma = fwhm / PseudoVoigtProfile.FwhmToSigma;
            if (sigma <= 0.0)
            {
                return 0.0;
            }

            if (calibration != null && calibration.PeakType == FwhmCalibration.ExpGaussExpPeakType)
            {
                return TailSupport(calibration.ExpGaussExpRightTail) * sigma;
            }

            if (calibration != null && calibration.PeakType == FwhmCalibration.VoigtPeakType)
            {
                double halfWidth = PseudoVoigtProfile.HalfWidthAtRelativeHeight(
                    fwhm,
                    calibration.VoigtSigma,
                    calibration.VoigtGamma,
                    Math.Exp(-TailAmplitudeCutoffLog));
                return halfWidth > 0.0 ? halfWidth : GaussianSigmaWindow * sigma;
            }

            return GaussianSigmaWindow * sigma;
        }

        public static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        static double TailSupport(double skew)
        {
            if (!IsFinite(skew) || skew <= 0.0)
            {
                return GaussianSigmaWindow;
            }

            return Math.Max(GaussianSigmaWindow, (TailAmplitudeCutoffLog + 0.5 * skew * skew) / skew);
        }

        static double GaussianRelativeValue(double offset, double fwhm)
        {
            double sigma = fwhm / PseudoVoigtProfile.FwhmToSigma;
            if (sigma <= 0.0)
            {
                return 0.0;
            }

            double t = offset / sigma;
            return Math.Exp(-0.5 * t * t);
        }

        static double ExpGaussExpRelativeValue(double offset, double fwhm, double left, double right)
        {
            double sigma = fwhm / PseudoVoigtProfile.FwhmToSigma;
            if (sigma <= 0.0)
            {
                return 0.0;
            }

            double t = offset / sigma;
            if (t > right)
            {
                return Math.Exp(0.5 * right * right - right * t);
            }
            if (t > -left)
            {
                return Math.Exp(-0.5 * t * t);
            }
            return Math.Exp(0.5 * left * left + left * t);
        }
    }
}
