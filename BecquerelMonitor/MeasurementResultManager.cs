using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    // Token: 0x02000071 RID: 113
    public class MeasurementResultManager
    {
        // Token: 0x060005C4 RID: 1476 RVA: 0x000243FC File Offset: 0x000225FC
        public MeasurementResultCollection Translate(MeasurementResultCollection resultCollection, ResultTranslation resultTranslation)
        {
            if (resultCollection == null)
            {
                return null;
            }
            MeasurementResultCollection measurementResultCollection = new MeasurementResultCollection();
            measurementResultCollection.ResultData = resultCollection.ResultData;
            measurementResultCollection.ROIConfig = resultCollection.ROIConfig;
            measurementResultCollection.MeasurementTime = resultCollection.MeasurementTime;
            this.roiConfig = resultCollection.ROIConfig;
            this.resultData = resultCollection.ResultData;
            this.energySpectrum = this.resultData.EnergySpectrum;
            this.measurementTime = resultCollection.MeasurementTime;
            if (this.roiConfig == null || this.energySpectrum == null)
            {
                return null;
            }
            foreach (MeasurementResult measurementResult in resultCollection.ResultList)
            {
                ROIDefinitionData roidefinition = measurementResult.ROIDefinition;
                double resultValue = measurementResult.ResultValue;
                double resultError = measurementResult.ResultError;
                double mda = measurementResult.MDA;
                if (this.measurementTime == 0.0)
                {
                    MeasurementResult item = new MeasurementResult(roidefinition, 0.0, 0.0);
                    measurementResultCollection.ResultList.Add(item);
                }
                else
                {
                    double resultValue2 = 0.0;
                    double resultError2 = 0.0;
                    double mda2 = 0.0;
                    double becquerelCoefficient = roidefinition.BecquerelCoefficient;
                    double becquerelCoefficientError = roidefinition.BecquerelCoefficientError;
                    double resultCps = resultValue / this.measurementTime;
                    double resultErrorCps = Math.Abs(resultError) / this.measurementTime;
                    double resultBq = resultCps * becquerelCoefficient;
                    double resultBqError = 0.0;
                    if (resultCps != 0.0 && becquerelCoefficient != 0.0)
                    {
                        resultBqError = resultCps * becquerelCoefficient * Math.Sqrt(Math.Pow(resultErrorCps / resultCps, 2.0) + Math.Pow(becquerelCoefficientError / becquerelCoefficient, 2.0));
                        resultBqError = Math.Abs(resultBqError);
                    }
                    double mdaCps = mda / this.measurementTime;
                    double mdaBq = mdaCps * becquerelCoefficient;
                    switch (resultTranslation)
                    {
                        case ResultTranslation.Nothing:
                            resultValue2 = resultValue;
                            resultError2 = resultError;
                            mda2 = mda;
                            break;
                        case ResultTranslation.CountsPerSecond:
                            resultValue2 = resultCps;
                            resultError2 = resultErrorCps;
                            mda2 = mdaCps;
                            break;
                        case ResultTranslation.Becquerels:
                            resultValue2 = resultBq;
                            resultError2 = resultBqError;
                            mda2 = mdaBq;
                            break;
                        case ResultTranslation.BecquerelsPerKilogram:
                            resultValue2 = resultBq / this.resultData.SampleInfo.Weight;
                            resultError2 = resultBqError / this.resultData.SampleInfo.Weight;
                            mda2 = mdaBq / this.resultData.SampleInfo.Weight;
                            break;
                        case ResultTranslation.BecquerelsPerLiter:
                            resultValue2 = resultBq / this.resultData.SampleInfo.Volume;
                            resultError2 = resultBqError / this.resultData.SampleInfo.Volume;
                            mda2 = mdaBq / this.resultData.SampleInfo.Volume;
                            break;
                    }
                    MeasurementResult item = new MeasurementResult(roidefinition, resultValue2, resultError2, mda2);
                    measurementResultCollection.ResultList.Add(item);
                }
            }
            return measurementResultCollection;
        }

        // Token: 0x060005C5 RID: 1477 RVA: 0x00024700 File Offset: 0x00022900
        public MeasurementResultCollection Correct(MeasurementResultCollection resultCollection)
        {
            MeasurementResultCollection measurementResultCollection = new MeasurementResultCollection();
            measurementResultCollection.ResultData = resultCollection.ResultData;
            measurementResultCollection.ROIConfig = resultCollection.ROIConfig;
            measurementResultCollection.MeasurementTime = resultCollection.MeasurementTime;
            this.roiConfig = resultCollection.ROIConfig;
            this.resultData = resultCollection.ResultData;
            this.energySpectrum = this.resultData.EnergySpectrum;
            this.measurementTime = resultCollection.MeasurementTime;
            if (this.roiConfig == null || this.energySpectrum == null)
            {
                return null;
            }
            foreach (MeasurementResult measurementResult in resultCollection.ResultList)
            {
                ROIDefinitionData roidefinition = measurementResult.ROIDefinition;
                double resultValue = measurementResult.ResultValue;
                double resultError = measurementResult.ResultError;
                double halfLife = roidefinition.HalfLife;
                DateTime time = this.resultData.SampleInfo.Time;
                DateTime endTime = this.resultData.EndTime;
                double num = (endTime - time).TotalDays / 365.0;
                double num2 = 1.0 / Math.Pow(0.5, num / halfLife);
                double resultValue2 = resultValue * num2;
                double resultError2 = resultError * num2;
                MeasurementResult item = new MeasurementResult(roidefinition, resultValue2, resultError2, measurementResult.MDA);
                measurementResultCollection.ResultList.Add(item);
            }
            return measurementResultCollection;
        }

        // Token: 0x060005C6 RID: 1478 RVA: 0x00024878 File Offset: 0x00022A78
        public MeasurementResultCollection Calculate(ResultData resultData)
        {
            this.detectionLevel = GlobalConfigManager.GetInstance().GlobalConfig.MeasurementConfig.DetectionLevel;
            this.resultData = resultData;
            this.roiConfig = resultData.ROIConfig;
            this.energySpectrum = resultData.EnergySpectrum;
            this.backgroundEnergySpectrum = resultData.BackgroundEnergySpectrum;
            if (this.roiConfig == null || this.energySpectrum == null)
            {
                return null;
            }
            this.bg = false;
            if (this.backgroundEnergySpectrum != null && this.backgroundEnergySpectrum.Spectrum != null)
            {
                this.bg = true;
                this.backgroundNumberOfChannels = this.backgroundEnergySpectrum.NumberOfChannels;
                this.backgroundEnergyCalibration = this.backgroundEnergySpectrum.EnergyCalibration;
                this.backgroundMeasurementTime = this.backgroundEnergySpectrum.MeasurementTime;
            }
            this.numberOfChannels = this.energySpectrum.NumberOfChannels;
            this.energyCalibration = this.energySpectrum.EnergyCalibration;
            this.measurementTime = this.energySpectrum.MeasurementTime;
            MeasurementResultCollection measurementResultCollection = new MeasurementResultCollection();
            measurementResultCollection.ResultData = resultData;
            measurementResultCollection.ROIConfig = this.roiConfig;
            measurementResultCollection.MeasurementTime = this.measurementTime;
            List<MeasurementResult> resultList = measurementResultCollection.ResultList;
            foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
            {
                roidefinitionData.IsValidResult = false;
            }
            foreach (ROIDefinitionData roidefinitionData2 in this.roiConfig.ROIDefinitions)
            {
                if (roidefinitionData2.Enabled)
                {
                    double resultValue = 0.0;
                    double resultError = 0.0;
                    if (this.measurementTime == 0.0)
                    {
                        MeasurementResult item = new MeasurementResult(roidefinitionData2, 0.0, 0.0);
                        resultList.Add(item);
                    }
                    else
                    {
                        if (roidefinitionData2.IsValidResult)
                        {
                            resultValue = roidefinitionData2.ResultCount;
                            resultError = roidefinitionData2.ResultError;
                        }
                        else if (!this.CalculateROI(roidefinitionData2, out resultValue, out resultError, 0))
                        {
                            resultList.Add(new MeasurementResult(roidefinitionData2, 0.0, 0.0)
                            {
                                IsValid = false
                            });
                            continue;
                        }
                        resultList.Add(new MeasurementResult(roidefinitionData2, resultValue, resultError)
                        {
                            MDA = roidefinitionData2.MDA
                        });
                    }
                }
            }
            return measurementResultCollection;
        }

        // Token: 0x060005C7 RID: 1479 RVA: 0x00024B38 File Offset: 0x00022D38
        bool CalculateROI(ROIDefinitionData roi, out double count, out double error, int recurse)
        {
            count = 0.0;
            error = 0.0;
            if (recurse > 10)
            {
                return false;
            }
            double num = 0.0;
            double fgTime = this.measurementTime;
            double bgTime = 0.0;
            if (this.bg)
            {
                bgTime = this.backgroundMeasurementTime;
            }
            bool hasBg = false;
            foreach (ROIPrimitiveData roiprimitiveData in roi.ROIPrimitives)
            {
                double netCounts = 0.0;
                double netCountsSigma = 0.0;
                double num6 = 0.0;
                if (roiprimitiveData is ROISimpleDifferenceData)
                {
                    ROISimpleDifferenceData roisimpleDifferenceData = (ROISimpleDifferenceData)roiprimitiveData;
                    double lowerLimit = roisimpleDifferenceData.LowerLimit;
                    double upperLimit = roisimpleDifferenceData.UpperLimit;
                    int lowerLimitChannel;
                    int upperLimitChannel;
                    try
                    {
                        lowerLimitChannel = (int)Math.Ceiling(this.energyCalibration.EnergyToChannel(lowerLimit, maxChannels: this.energySpectrum.NumberOfChannels));
                        upperLimitChannel = (int)Math.Floor(this.energyCalibration.EnergyToChannel(upperLimit, maxChannels: this.energySpectrum.NumberOfChannels));
                    }
                    catch (OutofChannelException)
                    {
                        return false;
                    }
                    double fgRegionCounts = 0.0;
                    double bgRegionCounts = 0.0;
                    for (int i = lowerLimitChannel; i <= upperLimitChannel; i++)
                    {
                        if (i >= 0 && i < this.numberOfChannels)
                        {
                            fgRegionCounts += (double)this.energySpectrum.Spectrum[i];
                            if (this.bg)
                            {
                                int bgChannelIndex = i;
                                if (!this.energyCalibration.Equals(this.backgroundEnergyCalibration))
                                {
                                    bgChannelIndex = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.energyCalibration.ChannelToEnergy((double)i), maxChannels: this.backgroundEnergySpectrum.NumberOfChannels);
                                }
                                if (bgChannelIndex >= 0 && bgChannelIndex < this.backgroundNumberOfChannels)
                                {
                                    bgRegionCounts += (double)this.backgroundEnergySpectrum.Spectrum[bgChannelIndex];
                                }
                            }
                        }
                    }
                    double fgSigma = Math.Sqrt(fgRegionCounts);
                    double bgSigma = Math.Sqrt(bgRegionCounts);
                    if (this.bg && bgTime != 0.0)
                    {
                        double bgCps = bgRegionCounts / bgTime;
                        num6 = bgCps * (1.0 / fgTime + 1.0 / bgTime);
                        hasBg = true;
                    }
                    if (this.bg && this.backgroundMeasurementTime != 0.0)
                    {
                        double bgNormalizeCoeff = this.measurementTime / this.backgroundMeasurementTime;
                        bgRegionCounts *= bgNormalizeCoeff;
                        bgSigma *= bgNormalizeCoeff;
                    }
                    netCounts = fgRegionCounts - bgRegionCounts;
                    netCountsSigma = Math.Sqrt(Math.Pow(fgSigma, 2.0) + Math.Pow(bgSigma, 2.0));
                }
                else if (roiprimitiveData is ROICovellMethodData)
                {
                    ROICovellMethodData roicovellMethodData = (ROICovellMethodData)roiprimitiveData;
                    int numberOfSideChannels = roicovellMethodData.NumberOfSideChannels;
                    netCounts = 0.0;
                    netCountsSigma = 0.0;
                    double lowerLimit2 = roicovellMethodData.LowerLimit;
                    double upperLimit2 = roicovellMethodData.UpperLimit;
                    int lowerLimitChannelIndex;
                    int upperLimitChannelIndex;
                    try
                    {
                        lowerLimitChannelIndex = (int)Math.Ceiling(this.energyCalibration.EnergyToChannel(lowerLimit2, maxChannels: this.energySpectrum.NumberOfChannels));
                        upperLimitChannelIndex = (int)Math.Floor(this.energyCalibration.EnergyToChannel(upperLimit2, maxChannels: this.energySpectrum.NumberOfChannels));
                    }
                    catch (OutofChannelException)
                    {
                        return false;
                    }
                    double leftRegionCenter = roicovellMethodData.LeftRegionCenter;
                    double rightRegionCenter = roicovellMethodData.RightRegionCenter;
                    double leftRegionWidth = roicovellMethodData.LeftRegionWidth;
                    double rightRegionWidth = roicovellMethodData.RightRegionWidth;
                    int num17;
                    int num18;
                    int num19;
                    int num20;
                    try
                    {
                        num17 = (int)Math.Ceiling(this.energyCalibration.EnergyToChannel(leftRegionCenter - leftRegionWidth / 2.0, maxChannels: this.energySpectrum.NumberOfChannels));
                        num18 = (int)Math.Floor(this.energyCalibration.EnergyToChannel(leftRegionCenter + leftRegionWidth / 2.0, maxChannels: this.energySpectrum.NumberOfChannels));
                        num19 = (int)Math.Ceiling(this.energyCalibration.EnergyToChannel(rightRegionCenter - rightRegionWidth / 2.0, maxChannels: this.energySpectrum.NumberOfChannels));
                        num20 = (int)Math.Floor(this.energyCalibration.EnergyToChannel(rightRegionCenter + rightRegionWidth / 2.0, maxChannels: this.energySpectrum.NumberOfChannels));
                    }
                    catch (OutofChannelException)
                    {
                        return false;
                    }
                    double num21 = 0.0;
                    for (int j = lowerLimitChannelIndex; j <= upperLimitChannelIndex; j++)
                    {
                        if (j >= 0 && j < this.numberOfChannels)
                        {
                            num21 += (double)this.energySpectrum.Spectrum[j];
                        }
                    }
                    double num22 = 0.0;
                    for (int k = num17; k <= num18; k++)
                    {
                        if (k >= 0 && k < this.numberOfChannels)
                        {
                            num22 += (double)this.energySpectrum.Spectrum[k];
                        }
                    }
                    double num23 = 0.0;
                    for (int l = num19; l <= num20; l++)
                    {
                        if (l >= 0 && l < this.numberOfChannels)
                        {
                            num23 += (double)this.energySpectrum.Spectrum[l];
                        }
                    }
                    double num24 = (double)(lowerLimitChannelIndex + upperLimitChannelIndex) / 2.0;
                    double num25 = (double)(num17 + num18) / 2.0;
                    double num26 = (double)(num19 + num20) / 2.0;
                    double num27 = (double)(num18 - num17 + 1);
                    double num28 = (double)(num20 - num19 + 1);
                    double num29 = (double)(upperLimitChannelIndex - lowerLimitChannelIndex + 1);
                    double num30 = num29 / num28 * (num24 - num25) / (num26 - num25);
                    double num31 = num29 / num27 * (num26 - num24) / (num26 - num25);
                    netCounts = num21 - num30 * num23 - num31 * num22;
                    netCountsSigma = Math.Sqrt(num21 + Math.Pow(num31, 2.0) * num22 + Math.Pow(num30, 2.0) * num23);
                    double num32 = Math.Abs(roiprimitiveData.Coefficient);
                    num6 = Math.Abs(netCounts) / fgTime / fgTime * num32 + Math.Pow(Math.Abs(netCountsSigma) / fgTime / fgTime * num32, 2.0);
                }
                else if (roiprimitiveData is ROIReferenceData)
                {
                    ROIReferenceData roireferenceData = (ROIReferenceData)roiprimitiveData;
                    foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
                    {
                        if (roidefinitionData.Name == roireferenceData.Reference)
                        {
                            if (roidefinitionData.IsValidResult)
                            {
                                netCounts = roidefinitionData.ResultCount;
                                netCountsSigma = roidefinitionData.ResultError;
                                break;
                            }
                            if (!this.CalculateROI(roidefinitionData, out netCounts, out netCountsSigma, recurse + 1))
                            {
                                return false;
                            }
                            break;
                        }
                    }
                    double coeff = Math.Abs(roiprimitiveData.Coefficient);
                    num6 = Math.Abs(netCounts) / fgTime / fgTime * coeff + Math.Pow(Math.Abs(netCountsSigma) / fgTime / fgTime * coeff, 2.0);
                }
                double coefficient = roiprimitiveData.Coefficient;
                double coefficientError = roiprimitiveData.CoefficientError;
                double countsToUse = netCounts * coefficient;
                double countsToUseError = 0.0;
                if (netCounts != 0.0 && coefficient != 0.0)
                {
                    countsToUseError = countsToUse * Math.Sqrt(Math.Pow(netCountsSigma / netCounts, 2.0) + Math.Pow(coefficientError / coefficient, 2.0));
                }
                if (roiprimitiveData.Operation == this.addOpe)
                {
                    count += countsToUse;
                    error = Math.Sqrt(Math.Pow(error, 2.0) + Math.Pow(countsToUseError, 2.0));
                }
                else
                {
                    if (roiprimitiveData.Operation != this.subOpe)
                    {
                        return false;
                    }
                    count -= countsToUse;
                    error = Math.Sqrt(Math.Pow(error, 2.0) + Math.Pow(countsToUseError, 2.0));
                }
                num += num6;
            }
            roi.IsValidResult = true;
            roi.ResultCount = count;
            roi.ResultError = error;
            double detectionLevel = (double)this.detectionLevel;
            roi.MDA = -1.0;
            if (hasBg)
            {
                double mdaCps = detectionLevel * detectionLevel / (2.0 * fgTime) + detectionLevel * Math.Sqrt(detectionLevel * detectionLevel / (4.0 * fgTime * fgTime) + num);
                roi.MDA = mdaCps * this.measurementTime;
            }
            return true;
        }

        // Token: 0x04000306 RID: 774
        ResultData resultData;

        // Token: 0x04000307 RID: 775
        ROIPrimitiveOperation addOpe = ROIPrimitiveOperation.OperationsMap["Addition"];

        // Token: 0x04000308 RID: 776
        ROIPrimitiveOperation subOpe = ROIPrimitiveOperation.OperationsMap["Subtraction"];

        // Token: 0x04000309 RID: 777
        ROIConfigData roiConfig;

        // Token: 0x0400030A RID: 778
        EnergySpectrum energySpectrum;

        // Token: 0x0400030B RID: 779
        EnergySpectrum backgroundEnergySpectrum;

        // Token: 0x0400030C RID: 780
        bool bg;

        // Token: 0x0400030D RID: 781
        int numberOfChannels;

        // Token: 0x0400030E RID: 782
        int backgroundNumberOfChannels;

        // Token: 0x0400030F RID: 783
        EnergyCalibration energyCalibration;

        // Token: 0x04000310 RID: 784
        EnergyCalibration backgroundEnergyCalibration;

        // Token: 0x04000311 RID: 785
        double measurementTime;

        // Token: 0x04000312 RID: 786
        double backgroundMeasurementTime;

        // Token: 0x04000313 RID: 787
        decimal detectionLevel;
    }
}
