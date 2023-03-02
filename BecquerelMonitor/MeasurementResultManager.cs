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
                    double num = resultValue / this.measurementTime;
                    double num2 = Math.Abs(resultError) / this.measurementTime;
                    double num3 = num * becquerelCoefficient;
                    double num4 = 0.0;
                    if (num != 0.0 && becquerelCoefficient != 0.0)
                    {
                        num4 = num * becquerelCoefficient * Math.Sqrt(Math.Pow(num2 / num, 2.0) + Math.Pow(becquerelCoefficientError / becquerelCoefficient, 2.0));
                        num4 = Math.Abs(num4);
                    }
                    double num5 = mda / this.measurementTime;
                    double num6 = num5 * becquerelCoefficient;
                    switch (resultTranslation)
                    {
                        case ResultTranslation.Nothing:
                            resultValue2 = resultValue;
                            resultError2 = resultError;
                            mda2 = mda;
                            break;
                        case ResultTranslation.CountsPerSecond:
                            resultValue2 = num;
                            resultError2 = num2;
                            mda2 = num5;
                            break;
                        case ResultTranslation.Becquerels:
                            resultValue2 = num3;
                            resultError2 = num4;
                            mda2 = num6;
                            break;
                        case ResultTranslation.BecquerelsPerKilogram:
                            resultValue2 = num3 / this.resultData.SampleInfo.Weight;
                            resultError2 = num4 / this.resultData.SampleInfo.Weight;
                            mda2 = num6 / this.resultData.SampleInfo.Weight;
                            break;
                        case ResultTranslation.BecquerelsPerLiter:
                            resultValue2 = num3 / this.resultData.SampleInfo.Volume;
                            resultError2 = num4 / this.resultData.SampleInfo.Volume;
                            mda2 = num6 / this.resultData.SampleInfo.Volume;
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
            double num2 = this.measurementTime;
            double num3 = 0.0;
            if (this.bg)
            {
                num3 = this.backgroundMeasurementTime;
            }
            bool flag = false;
            foreach (ROIPrimitiveData roiprimitiveData in roi.ROIPrimitives)
            {
                double num4 = 0.0;
                double num5 = 0.0;
                double num6 = 0.0;
                if (roiprimitiveData is ROISimpleDifferenceData)
                {
                    ROISimpleDifferenceData roisimpleDifferenceData = (ROISimpleDifferenceData)roiprimitiveData;
                    double lowerLimit = roisimpleDifferenceData.LowerLimit;
                    double upperLimit = roisimpleDifferenceData.UpperLimit;
                    int num7;
                    int num8;
                    try
                    {
                        num7 = (int)Math.Ceiling(this.energyCalibration.EnergyToChannel(lowerLimit, maxChannels: this.energySpectrum.NumberOfChannels));
                        num8 = (int)Math.Floor(this.energyCalibration.EnergyToChannel(upperLimit, maxChannels: this.energySpectrum.NumberOfChannels));
                    }
                    catch (OutofChannelException)
                    {
                        return false;
                    }
                    double num9 = 0.0;
                    double num10 = 0.0;
                    for (int i = num7; i <= num8; i++)
                    {
                        if (i >= 0 && i < this.numberOfChannels)
                        {
                            num9 += (double)this.energySpectrum.Spectrum[i];
                            if (this.bg)
                            {
                                int num11 = i;
                                if (!this.energyCalibration.Equals(this.backgroundEnergyCalibration))
                                {
                                    num11 = (int)this.backgroundEnergyCalibration.EnergyToChannel(this.energyCalibration.ChannelToEnergy((double)i), maxChannels: this.backgroundEnergySpectrum.NumberOfChannels);
                                }
                                if (num11 >= 0 && num11 < this.backgroundNumberOfChannels)
                                {
                                    num10 += (double)this.backgroundEnergySpectrum.Spectrum[num11];
                                }
                            }
                        }
                    }
                    double x = Math.Sqrt(num9);
                    double num12 = Math.Sqrt(num10);
                    if (this.bg && num3 != 0.0)
                    {
                        double num13 = num10 / num3;
                        num6 = num13 * (1.0 / num2 + 1.0 / num3);
                        flag = true;
                    }
                    if (this.bg && this.backgroundMeasurementTime != 0.0)
                    {
                        double num14 = this.measurementTime / this.backgroundMeasurementTime;
                        num10 *= num14;
                        num12 *= num14;
                    }
                    num4 = num9 - num10;
                    num5 = Math.Sqrt(Math.Pow(x, 2.0) + Math.Pow(num12, 2.0));
                }
                else if (roiprimitiveData is ROICovellMethodData)
                {
                    ROICovellMethodData roicovellMethodData = (ROICovellMethodData)roiprimitiveData;
                    int numberOfSideChannels = roicovellMethodData.NumberOfSideChannels;
                    num4 = 0.0;
                    num5 = 0.0;
                    double lowerLimit2 = roicovellMethodData.LowerLimit;
                    double upperLimit2 = roicovellMethodData.UpperLimit;
                    int num15;
                    int num16;
                    try
                    {
                        num15 = (int)Math.Ceiling(this.energyCalibration.EnergyToChannel(lowerLimit2, maxChannels: this.energySpectrum.NumberOfChannels));
                        num16 = (int)Math.Floor(this.energyCalibration.EnergyToChannel(upperLimit2, maxChannels: this.energySpectrum.NumberOfChannels));
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
                    for (int j = num15; j <= num16; j++)
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
                    double num24 = (double)(num15 + num16) / 2.0;
                    double num25 = (double)(num17 + num18) / 2.0;
                    double num26 = (double)(num19 + num20) / 2.0;
                    double num27 = (double)(num18 - num17 + 1);
                    double num28 = (double)(num20 - num19 + 1);
                    double num29 = (double)(num16 - num15 + 1);
                    double num30 = num29 / num28 * (num24 - num25) / (num26 - num25);
                    double num31 = num29 / num27 * (num26 - num24) / (num26 - num25);
                    num4 = num21 - num30 * num23 - num31 * num22;
                    num5 = Math.Sqrt(num21 + Math.Pow(num31, 2.0) * num22 + Math.Pow(num30, 2.0) * num23);
                    double num32 = Math.Abs(roiprimitiveData.Coefficient);
                    num6 = Math.Abs(num4) / num2 / num2 * num32 + Math.Pow(Math.Abs(num5) / num2 / num2 * num32, 2.0);
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
                                num4 = roidefinitionData.ResultCount;
                                num5 = roidefinitionData.ResultError;
                                break;
                            }
                            if (!this.CalculateROI(roidefinitionData, out num4, out num5, recurse + 1))
                            {
                                return false;
                            }
                            break;
                        }
                    }
                    double num33 = Math.Abs(roiprimitiveData.Coefficient);
                    num6 = Math.Abs(num4) / num2 / num2 * num33 + Math.Pow(Math.Abs(num5) / num2 / num2 * num33, 2.0);
                }
                double coefficient = roiprimitiveData.Coefficient;
                double coefficientError = roiprimitiveData.CoefficientError;
                double num34 = num4 * coefficient;
                double x2 = 0.0;
                if (num4 != 0.0 && coefficient != 0.0)
                {
                    x2 = num4 * coefficient * Math.Sqrt(Math.Pow(num5 / num4, 2.0) + Math.Pow(coefficientError / coefficient, 2.0));
                }
                if (roiprimitiveData.Operation == this.addOpe)
                {
                    count += num34;
                    error = Math.Sqrt(Math.Pow(error, 2.0) + Math.Pow(x2, 2.0));
                }
                else
                {
                    if (roiprimitiveData.Operation != this.subOpe)
                    {
                        return false;
                    }
                    count -= num34;
                    error = Math.Sqrt(Math.Pow(error, 2.0) + Math.Pow(x2, 2.0));
                }
                num += num6;
            }
            roi.IsValidResult = true;
            roi.ResultCount = count;
            roi.ResultError = error;
            double num35 = (double)this.detectionLevel;
            roi.MDA = -1.0;
            if (flag)
            {
                double num36 = num35 * num35 / (2.0 * num2) + num35 * Math.Sqrt(num35 * num35 / (4.0 * num2 * num2) + num);
                roi.MDA = num36 * this.measurementTime;
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
