using System;

namespace BecquerelMonitor.Utils
{
    internal static class PeakShapeModel
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
