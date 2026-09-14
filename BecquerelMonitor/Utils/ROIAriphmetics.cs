using System;
using System.Collections.Generic;

namespace BecquerelMonitor.Utils
{
    public class ROIAriphmetics
    {
        // Кривая эффективности отсюда ушла вместе с самой возможностью
        // держать её в наборе зон: она живёт в конфигурации прибора, а
        // интерполирует её FullSpectrumAnalysis.FsaEfficiency — один на всю
        // программу. Здесь остались только формулы пределов и активности,
        // которым кривая не нужна: им дают уже готовый коэффициент.

        /// <summary>
        /// Lc/Lu/Ld formulas are taken from:
        /// "МИНИМАЛЬНАЯ ДЕТЕКТИРУЕМАЯ АКТИВНОСТЬ. ОСНОВНЫЕ ПОНЯТИЯ И ОПРЕДЕЛЕНИЯ"
        ///    "А.Г.Исаев, В.В.Бабенко, А.С.Казимиров, С.Н.Гришин, С.М.Иевлев"
        /// </summary>
        public static double CalculateLc(double bgCounts, double bgTime, double fgTime, double singleSideConfidence)
        {
            double bgCps = bgCounts / bgTime;
            double LcCps = singleSideConfidence * Math.Sqrt((bgCps / bgTime) * (1.0 + bgTime / fgTime));
            double Lc = LcCps * fgTime;

            return Lc;
        }

        public static double CalculateLd(double bgCounts, double bgTime, double fgTime, double singleSideConfidence)
        {
            double bgCps = bgCounts / bgTime;
            double LdCps = (singleSideConfidence * singleSideConfidence) / fgTime + 2.0 * singleSideConfidence * Math.Sqrt((bgCps / bgTime) * (1.0 + bgTime / fgTime));
            double Ld = LdCps * fgTime;
            
            return Ld;
        }

        public static double CalculateLu(double fgCounts, double fgTime, double bgCounts, double bgTime, double singleSideConfidence)
        {
            double fgCps = fgCounts / fgTime;
            double bgCps = bgCounts / bgTime;
            double netCps = fgCps - bgCps;
            double LuCps = netCps + singleSideConfidence * Math.Sqrt((fgCps / fgTime) + (bgCps / bgTime));
            double Lu = LuCps * fgTime;

            return Lu;
        }

        public static double CalculateNetCount(double fgCounts, double fgTime, double bgCounts, double bgTime)
        {
            if (bgTime <= 0)
            {
                return fgCounts;
            }

            double adjustedBgCounts = bgCounts * fgTime / bgTime;
            double netCount = fgCounts - adjustedBgCounts;

            return netCount;
        }

        public static double CalculateNetCountError(double fgCounts, double fgTime, double bgCounts, double bgTime, double confidence)
        {
            double fgSigma = Math.Sqrt(fgCounts);
            double adjBgSigma = 0;
            if (bgTime > 0)
            {
                double bgSigma = Math.Sqrt(bgCounts);
                adjBgSigma = bgSigma * fgTime / bgTime;
            }
            
            return confidence * Math.Sqrt(Math.Pow(fgSigma, 2.0) + Math.Pow(adjBgSigma, 2.0));
        }

        public static double CalculateActivity(double bqCoeff, double fgCounts, double fgTime, double bgCounts, double bgTime)
        {
            double netCount = CalculateNetCount(fgCounts, fgTime, bgCounts, bgTime);
            double netCps = netCount / fgTime;
            double activity = netCps * bqCoeff;

            return activity;
        }

        public static double CalculateActivityUpperLimit(double bqCoeff, double bqCoeffError, double fgCounts, double fgTime, double bgCounts, double bgTime, double singleSideConfidence)
        {
            double Lu = CalculateLu(fgCounts, fgTime, bgCounts, bgTime, singleSideConfidence);
            double upperCps = Lu / fgTime;
            double activity = upperCps * bqCoeff;
            double activityError = singleSideConfidence * (bqCoeffError * upperCps);
            double activityUpper = activity + activityError;

            return activityUpper;
        }

        public static double CalculateActivityError(double bqCoeff, double bqCoeffError, double fgCounts, double fgTime, double bgCounts, double bgTime, double confidence)
        {
            double netCount = CalculateNetCount(fgCounts, fgTime, bgCounts, bgTime);
            double netCountSigma = CalculateNetCountError(fgCounts, fgTime, bgCounts, bgTime, 1.0);
            double netCps = netCount / fgTime;
            double netCpsSigma = netCountSigma / fgTime;
            double netActivitySigma = netCpsSigma * bqCoeff;
            double coeffErrorSigma = netCps * bqCoeffError;
            double activityError = confidence * Math.Sqrt(Math.Pow(netActivitySigma, 2.0) + Math.Pow(coeffErrorSigma, 2.0));

            return activityError;
        }

        public static double CalculateLqCounts(double bgCounts, double bgTime, double fgTime, double confidence)
        {
            double mdaCps = Math.Pow(confidence, 2.0) / (2.0 * fgTime) 
                + confidence * Math.Sqrt(
                    Math.Pow(confidence, 2.0) / (4.0 * Math.Pow(fgTime, 2.0)) + (bgCounts / bgTime) * (1 / fgTime + 1 / bgTime)
                );
            double mdaCounts = mdaCps * fgTime;

            return mdaCounts;
        }

    }
}
