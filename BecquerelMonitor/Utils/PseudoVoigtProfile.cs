using System;

namespace BecquerelMonitor.Utils
{
    internal struct PseudoVoigtParameters
    {
        public double GaussianSigma;
        public double LorentzGamma;
        public double Eta;
        public double ProfileSigma;
        public double ProfileGamma;
        public double CenterValue;
    }

    internal static class PseudoVoigtProfile
    {
        public const double FwhmToSigma = 2.3548200450309493;
        const double SqrtTwoPi = 2.5066282746310005;

        public static bool TryCreate(double fwhm, double sigmaScale, double gammaScale, out PseudoVoigtParameters parameters)
        {
            parameters = new PseudoVoigtParameters();
            if (!IsFinite(fwhm) || !IsFinite(sigmaScale) || !IsFinite(gammaScale) ||
                fwhm <= 0.0 || sigmaScale <= 0.0 || gammaScale <= 0.0)
            {
                return false;
            }

            // The user parameters define the relative Gaussian and Lorentzian widths.
            // Rescale both components so the resulting pseudo-Voigt has the requested FWHM.
            double unscaledFwhm = ApproximateFwhm(sigmaScale, gammaScale);
            if (!IsFinite(unscaledFwhm) || unscaledFwhm <= 0.0)
            {
                return false;
            }

            double scale = fwhm / unscaledFwhm;
            double gaussianSigma = sigmaScale * scale;
            double lorentzGamma = gammaScale * scale;
            double profileFwhm = ApproximateFwhm(gaussianSigma, lorentzGamma);
            if (!IsFinite(profileFwhm) || profileFwhm <= 0.0)
            {
                return false;
            }

            double ratio = 2.0 * lorentzGamma / profileFwhm;
            double eta = 1.36603 * ratio - 0.47719 * ratio * ratio + 0.11116 * ratio * ratio * ratio;
            eta = Math.Max(0.0, Math.Min(1.0, eta));

            parameters.GaussianSigma = gaussianSigma;
            parameters.LorentzGamma = lorentzGamma;
            parameters.Eta = eta;
            parameters.ProfileSigma = profileFwhm / FwhmToSigma;
            parameters.ProfileGamma = profileFwhm / 2.0;
            parameters.CenterValue = AreaValue(0.0, eta, parameters.ProfileSigma, parameters.ProfileGamma);
            return IsFinite(parameters.CenterValue) && parameters.CenterValue > 0.0;
        }

        public static double RelativeValue(double x, PseudoVoigtParameters parameters)
        {
            if (parameters.CenterValue <= 0.0)
            {
                return 0.0;
            }

            return Value(x, parameters) / parameters.CenterValue;
        }

        public static double Value(double x, PseudoVoigtParameters parameters)
        {
            if (parameters.ProfileSigma <= 0.0 || parameters.ProfileGamma <= 0.0)
            {
                return 0.0;
            }

            return AreaValue(x, parameters.Eta, parameters.ProfileSigma, parameters.ProfileGamma);
        }

        public static double Derivative(double x, PseudoVoigtParameters parameters)
        {
            if (parameters.ProfileSigma <= 0.0 || parameters.ProfileGamma <= 0.0)
            {
                return 0.0;
            }

            double gaussian = -x * Math.Exp(-0.5 * x * x / (parameters.ProfileSigma * parameters.ProfileSigma)) /
                (parameters.ProfileSigma * parameters.ProfileSigma * parameters.ProfileSigma * SqrtTwoPi);
            double lorentzDenominator = x * x + parameters.ProfileGamma * parameters.ProfileGamma;
            double lorentz = -2.0 * x * parameters.ProfileGamma /
                (Math.PI * lorentzDenominator * lorentzDenominator);
            return parameters.Eta * lorentz + (1.0 - parameters.Eta) * gaussian;
        }

        public static double HalfWidthAtRelativeHeight(double fwhm, double sigmaScale, double gammaScale, double relativeHeight)
        {
            PseudoVoigtParameters parameters;
            if (!TryCreate(fwhm, sigmaScale, gammaScale, out parameters) ||
                !IsFinite(relativeHeight) || relativeHeight <= 0.0 || relativeHeight >= 1.0)
            {
                return 0.0;
            }

            double low = 0.0;
            double high = Math.Max(fwhm, Math.Max(parameters.GaussianSigma, parameters.LorentzGamma));
            while (RelativeValue(high, parameters) > relativeHeight && high < Double.MaxValue / 2.0)
            {
                high *= 2.0;
            }

            for (int i = 0; i < 64; i++)
            {
                double middle = (low + high) / 2.0;
                if (RelativeValue(middle, parameters) > relativeHeight)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }

            return high;
        }

        static double ApproximateFwhm(double gaussianSigma, double lorentzGamma)
        {
            double gaussianFwhm = FwhmToSigma * gaussianSigma;
            double lorentzFwhm = 2.0 * lorentzGamma;
            return Math.Pow(
                Math.Pow(gaussianFwhm, 5) +
                2.69269 * Math.Pow(gaussianFwhm, 4) * lorentzFwhm +
                2.42843 * Math.Pow(gaussianFwhm, 3) * Math.Pow(lorentzFwhm, 2) +
                4.47163 * Math.Pow(gaussianFwhm, 2) * Math.Pow(lorentzFwhm, 3) +
                0.07842 * gaussianFwhm * Math.Pow(lorentzFwhm, 4) +
                Math.Pow(lorentzFwhm, 5),
                0.2);
        }

        static double AreaValue(double x, double eta, double profileSigma, double profileGamma)
        {
            double gaussian = Math.Exp(-0.5 * x * x / (profileSigma * profileSigma)) /
                (profileSigma * SqrtTwoPi);
            double lorentz = profileGamma / (Math.PI * (x * x + profileGamma * profileGamma));
            return eta * lorentz + (1.0 - eta) * gaussian;
        }

        static bool IsFinite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }
}
