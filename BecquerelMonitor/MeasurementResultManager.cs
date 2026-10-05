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
            measurementResultCollection.LiveTime = resultCollection.LiveTime;
            this.roiConfig = resultCollection.ROIConfig;
            this.resultData = resultCollection.ResultData;
            this.energySpectrum = this.resultData.EnergySpectrum;
            // (`AMBER35`, 15.09.2026) Знаменатель — `CountingTime` коллекции:
            // живое, если задано, иначе полное. Прежде делили на полное.
            this.countingTime = resultCollection.CountingTime;
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
                // Невалидный результат обязан остаться невалидным: перевод
                // единиц создаёт НОВЫЕ объекты, и без переноса флага строка
                // «Ошибка» превращалась в молчаливый честный ноль.
                if (!measurementResult.IsValid)
                {
                    measurementResultCollection.ResultList.Add(
                        new MeasurementResult(roidefinition, 0.0, 0.0)
                        {
                            IsValid = false,
                            StatusText = measurementResult.StatusText,
                        });
                    continue;
                }

                if (this.countingTime == 0.0)
                {
                    MeasurementResult item = new MeasurementResult(roidefinition, 0.0, 0.0);
                    measurementResultCollection.ResultList.Add(item);
                }
                else
                {
                    double resultValue2 = 0.0;
                    double resultError2 = 0.0;
                    double mda2 = 0.0;
                    // Точка счёта K одна, и она здесь. Раньше это было просто
                    // поле зоны; теперь оно может быть функцией активной кривой
                    // эффективности, и разводить эту развилку по вызывающим
                    // значило бы иметь в программе две разные активности.
                    Utils.BecquerelCoefficient.Result coefficient =
                        Utils.BecquerelCoefficient.Resolve(roidefinition, this.resultData.Efficiency);
                    double becquerelCoefficient = coefficient.Value;
                    double becquerelCoefficientError = coefficient.Error;

                    // ⛔ (`AMBER133`, П167 28.09.2026) Каскадное суммирование —
                    // тем же суммирователем, что у FSA, на матрице той же
                    // кривой, плюс чужие суммы в окне зоны. Только K ПО
                    // КРИВОЙ и только в беккерелях: ручной K суммирование уже
                    // несёт, а счёт и имп/с — не активность. Поправки нет —
                    // множитель 1 и причина в приписке строки.
                    Utils.BecquerelCoefficient.SummingResult summing = default(Utils.BecquerelCoefficient.SummingResult);
                    summing.Factor = 1.0;
                    bool translatesToBq =
                        resultTranslation == ResultTranslation.Becquerels
                        || resultTranslation == ResultTranslation.BecquerelsPerKilogram
                        || resultTranslation == ResultTranslation.BecquerelsPerLiter;
                    if (translatesToBq && !coefficient.Refused)
                    {
                        summing = Utils.BecquerelCoefficient.SummingForZone(roidefinition, coefficient, this.resultData);
                        if (summing.Applied)
                        {
                            becquerelCoefficient *= summing.Factor;
                            becquerelCoefficientError *= summing.Factor;
                        }
                    }

                    // Беккерели без K не считаются. Раньше нулевой коэффициент
                    // молча давал 0 Бк — неотличимо от настоящего нуля
                    // активности (TODO G7). Теперь строка получает статус.
                    bool needsCoefficient =
                        resultTranslation == ResultTranslation.Becquerels
                        || resultTranslation == ResultTranslation.BecquerelsPerKilogram
                        || resultTranslation == ResultTranslation.BecquerelsPerLiter;
                    string cannot = null;
                    if (needsCoefficient && coefficient.Refused)
                    {
                        // (`AMBER34`, решение Amber 15.09.2026) Кривая сцены поля
                        // (см²): K не получен И не подменён сохранённым числом —
                        // строка невалидна со СВОЕЙ причиной, не с общим «no K».
                        // Ручной режим сюда не попадает: `Resolve` кривую там не
                        // спрашивает.
                        cannot = coefficient.StatusText ?? Properties.Resources.ResultNoCoefficient;
                    }
                    else if (needsCoefficient && !(becquerelCoefficient > 0.0))
                    {
                        cannot = Properties.Resources.ResultNoCoefficient;
                    }
                    else if (resultTranslation == ResultTranslation.BecquerelsPerKilogram
                             && !(this.resultData.SampleInfo.Weight > 0.0))
                    {
                        cannot = Properties.Resources.ResultNoWeight;
                    }
                    else if (resultTranslation == ResultTranslation.BecquerelsPerLiter
                             && !(this.resultData.SampleInfo.Volume > 0.0))
                    {
                        cannot = Properties.Resources.ResultNoVolume;
                    }

                    if (cannot != null)
                    {
                        measurementResultCollection.ResultList.Add(
                            new MeasurementResult(roidefinition, 0.0, 0.0)
                            {
                                IsValid = false,
                                StatusText = cannot,
                            });
                        continue;
                    }
                    double resultCps = resultValue / this.countingTime;
                    double resultErrorCps = Math.Abs(resultError) / this.countingTime;
                    double resultBq = resultCps * becquerelCoefficient;
                    double resultBqError = 0.0;
                    if (resultCps != 0.0 && becquerelCoefficient != 0.0)
                    {
                        resultBqError = resultCps * becquerelCoefficient * Math.Sqrt(Math.Pow(resultErrorCps / resultCps, 2.0) + Math.Pow(becquerelCoefficientError / becquerelCoefficient, 2.0));
                        resultBqError = Math.Abs(resultBqError);
                    }
                    double mdaCps = mda / this.countingTime;
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
                            // Weight defaults to 0 - guard the denominator, otherwise the
                            // tables (and saved results) get NaN/Infinity every 500 ms.
                            if (this.resultData.SampleInfo.Weight > 0.0)
                            {
                                resultValue2 = resultBq / this.resultData.SampleInfo.Weight;
                                resultError2 = resultBqError / this.resultData.SampleInfo.Weight;
                                mda2 = mdaBq / this.resultData.SampleInfo.Weight;
                            }
                            break;
                        case ResultTranslation.BecquerelsPerLiter:
                            if (this.resultData.SampleInfo.Volume > 0.0)
                            {
                                resultValue2 = resultBq / this.resultData.SampleInfo.Volume;
                                resultError2 = resultBqError / this.resultData.SampleInfo.Volume;
                                mda2 = mdaBq / this.resultData.SampleInfo.Volume;
                            }
                            break;
                    }
                    MeasurementResult item = new MeasurementResult(roidefinition, resultValue2, resultError2, mda2)
                    {
                        SummingFactor = summing.Applied ? summing.Factor : 1.0,
                        SummingNote = summing.Note,
                        SummingProblem = summing.Problem,
                        // (`S199`, П174) помеха природного спутника в окне зоны —
                        // во всех единицах: окно собирает чужие отсчёты и в счёте.
                        Interference = Utils.BecquerelCoefficient.InterferenceForZone(roidefinition, this.resultData),
                    };
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
            measurementResultCollection.LiveTime = resultCollection.LiveTime;
            this.roiConfig = resultCollection.ROIConfig;
            this.resultData = resultCollection.ResultData;
            this.energySpectrum = this.resultData.EnergySpectrum;
            this.countingTime = resultCollection.CountingTime;
            if (this.roiConfig == null || this.energySpectrum == null)
            {
                return null;
            }
            foreach (MeasurementResult measurementResult in resultCollection.ResultList)
            {
                // Тот же перенос невалидности, что в Translate: поправка на
                // распад создаёт новые объекты и теряла флаг.
                if (!measurementResult.IsValid)
                {
                    measurementResultCollection.ResultList.Add(
                        new MeasurementResult(measurementResult.ROIDefinition, 0.0, 0.0)
                        {
                            IsValid = false,
                            StatusText = measurementResult.StatusText,
                        });
                    continue;
                }

                ROIDefinitionData roidefinition = measurementResult.ROIDefinition;
                double resultValue = measurementResult.ResultValue;
                double resultError = measurementResult.ResultError;
                // ⛔ (`AMBER148`, П192 01.10.2026) Множитель — от СРЕДНЕЙ за
                //    набор к активности на дату отбора (`DecayToSamplingFactor`).
                //    Прежде стояло 2^((EndTime − SampleInfo.Time)/T½) — от КОНЦА
                //    набора, и Бк короткоживущих выходили завышены на ≈ λT/2
                //    (I-131 за сутки +4.4 %, при T = T½ +44 %). MDA теперь
                //    умножается тем же множителем: порог стоял на дату
                //    измерения, значение — на дату отбора, и при условии
                //    обнаружения «≥ MDA» поправка делала необнаруженное
                //    обнаруженным. Период ≤ 0 (у поставочных зон бывает) —
                //    множитель 1, как и прежде.
                double num2 = DecayToSamplingFactor(roidefinition.HalfLife, this.resultData.SampleInfo.Time,
                                                    this.resultData.StartTime, this.resultData.EndTime);
                double resultValue2 = resultValue * num2;
                double resultError2 = resultError * num2;
                MeasurementResult item = new MeasurementResult(roidefinition, resultValue2, resultError2, measurementResult.MDA * num2)
                {
                    // (`AMBER133`) поправка на распад приписку суммирования не теряет
                    SummingFactor = measurementResult.SummingFactor,
                    SummingNote = measurementResult.SummingNote,
                    SummingProblem = measurementResult.SummingProblem,
                    // (`S199`) и помеху спутника тоже
                    Interference = measurementResult.Interference,
                };
                measurementResultCollection.ResultList.Add(item);
            }
            return measurementResultCollection;
        }

        /// <summary>
        /// (`AMBER148`) Дней в году периода полураспада. ⚠ 365, а не 365.2422,
        /// и это не небрежность: годы в поле <c>HalfLife</c> пишет импорт из базы
        /// нуклидов (<c>NucBase.HalfLifeYearsFromCell</c>: секунды / 31536000,
        /// то есть год = 365 сут), и период «8.0252 d» лежит в конфиге как
        /// 8.0252/365. Делить интервал на 365.2422 значило бы сдвинуть показатель
        /// на 0.066 % у всех таких нуклидов — через 10 периодов −0.46 %
        /// (`DecayCorrP192`, плечо «год»).
        /// </summary>
        public const double DaysPerHalfLifeYear = 365.0;

        /// <summary>
        /// (`AMBER148`, П192 01.10.2026) Множитель, приводящий СРЕДНЮЮ за набор
        /// активность к активности на дату отбора.
        ///
        /// Счёт за набор [t₀, t₀+T] от источника с активностью A(tₛ) даёт среднюю
        /// A(tₛ)·e^{−λ(t₀−tₛ)}·(1−e^{−λT})/(λT); обратный множитель —
        /// 2^{(t₀−tₛ)/T½}·λT/(1−e^{−λT}), где t₀ = <paramref name="start"/>,
        /// T = <paramref name="end"/> − <paramref name="start"/> (полное время:
        /// распад идёт и в мёртвое время), tₛ = <paramref name="sampling"/>.
        ///
        /// При T = 0 (и при T ≤ 0 у испорченного файла) — ровно прежняя форма
        /// 1/0.5^{Δt/T½}, побитово. Период ≤ 0 — множитель 1.
        /// </summary>
        public static double DecayToSamplingFactor(double halfLifeYears, DateTime sampling, DateTime start, DateTime end)
        {
            if (!(halfLifeYears > 0.0))
            {
                return 1.0;
            }

            double delayYears = (start - sampling).TotalDays / DaysPerHalfLifeYear;
            double toStart = 1.0 / Math.Pow(0.5, delayYears / halfLifeYears);
            double countYears = (end - start).TotalDays / DaysPerHalfLifeYear;
            if (!(countYears > 0.0))
            {
                return toStart;
            }

            return toStart * MeanToStartFactor(Math.Log(2.0) * countYears / halfLifeYears);
        }

        /// <summary>
        /// x/(1−e^{−x}) при x = λT ≥ 0 — от средней за набор к активности в начале
        /// набора. Малые x — рядом 1 + x/2 + x²/12 (отброшенный член x⁴/720 при
        /// x &lt; 1e-4 меньше 2e-19): прямая формула теряет там знаки на вычитании.
        /// </summary>
        public static double MeanToStartFactor(double x)
        {
            if (!(x > 0.0))
            {
                return 1.0;
            }

            if (x < 1e-4)
            {
                return 1.0 + x / 2.0 + x * x / 12.0;
            }

            return x / (1.0 - Math.Exp(-x));
        }

        // Token: 0x060005C6 RID: 1478 RVA: 0x00024878 File Offset: 0x00022A78
        public MeasurementResultCollection Calculate(ResultData resultData)
        {
            this.detectionLevel = GlobalConfigManager.GetInstance().GlobalConfig.MeasurementConfig.DetectionLevel;
            this.resultData = resultData;
            this.roiConfig = resultData.ROIConfig;
            this.energySpectrum = resultData.EnergySpectrum;
            this.backgroundEnergySpectrum = resultData.BackgroundEnergySpectrum;
            this.bgInSpectrumScale = null;
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
                // (`AMBER35`, решение Amber 15.09.2026) Оба знаменателя — по
                // правилу разбора FSA: живое, если задано (> 0), иначе полное
                // (`EnergySpectrum.EffectiveLiveTime`); фон нормируется
                // ОТНОШЕНИЕМ ЖИВЫХ. Прежде оба были полным временем, и Бк,
                // Бк/кг, Бк/л зон занижались на мёртвое время прибора, а фон
                // при разном мёртвом времени спектра и фона вычитался не в
                // той доле. Спектр без живого — побитово прежние числа.
                this.backgroundCountingTime = this.backgroundEnergySpectrum.EffectiveLiveTime;
            }
            this.numberOfChannels = this.energySpectrum.NumberOfChannels;
            this.energyCalibration = this.energySpectrum.EnergyCalibration;
            this.countingTime = this.energySpectrum.EffectiveLiveTime;
            MeasurementResultCollection measurementResultCollection = new MeasurementResultCollection();
            measurementResultCollection.ResultData = resultData;
            measurementResultCollection.ROIConfig = this.roiConfig;
            // Подпись — полное время, знаменатель (`CountingTime`) — по живому.
            measurementResultCollection.MeasurementTime = this.energySpectrum.MeasurementTime;
            measurementResultCollection.LiveTime = this.energySpectrum.LiveTime;
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
                    if (this.countingTime == 0.0)
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
                        else if (!this.CalculateROIWithReason(roidefinitionData2, out resultValue, out resultError))
                        {
                            resultList.Add(new MeasurementResult(roidefinitionData2, 0.0, 0.0)
                            {
                                IsValid = false,
                                // (`AMBER191`, `AMBER192`) причина словами, а не общее «Ошибка»
                                StatusText = this.roiFailure,
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

        /// <summary>
        /// (`AMBER191`, `AMBER192`) Счёт зоны с причиной отказа: при <c>false</c>
        /// в <see cref="roiFailure"/> — слова для таблицы (или null — общее «Ошибка»).
        /// </summary>
        bool CalculateROIWithReason(ROIDefinitionData roi, out double count, out double error)
        {
            this.roiFailure = null;
            return this.CalculateROI(roi, out count, out error, 0);
        }

        /// <summary>Причина последнего отказа <see cref="CalculateROI"/>; null — без слов.</summary>
        string roiFailure;

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
            double fgTime = this.countingTime;
            double bgTime = 0.0;
            if (this.bg)
            {
                bgTime = this.backgroundCountingTime;
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
                    lowerLimitChannel = (int)Math.Ceiling(PolynomialEnergyCalibration.ChannelOf(this.energyCalibration, lowerLimit, this.energySpectrum.NumberOfChannels));
                    upperLimitChannel = (int)Math.Floor(PolynomialEnergyCalibration.ChannelOf(this.energyCalibration, upperLimit, this.energySpectrum.NumberOfChannels));
                    double fgRegionCounts = 0.0;
                    double bgRegionCounts = 0.0;
                    for (int i = lowerLimitChannel; i <= upperLimitChannel; i++)
                    {
                        if (i >= 0 && i < this.numberOfChannels)
                        {
                            fgRegionCounts += (double)this.energySpectrum.Spectrum[i];
                            if (this.bg)
                            {
                                // (`AMBER109`, П167) Фон в чужой калибровке —
                                // раскладкой по перекрытию энергий в шкалу
                                // спектра (`SpectrumAriphmetics.RebinByEnergy`),
                                // а не каналом `(int)ChannelOf(E)`: тот брал
                                // отсчёты канала фона без множителя
                                // h_спектр/h_фон (и ещё усечением), и фон
                                // зоны на `RC103_Th232WT20` уезжал на
                                // −22…+5 %. Равные калибровки — прежняя
                                // ветка, побитово.
                                if (this.energyCalibration.Equals(this.backgroundEnergyCalibration))
                                {
                                    if (i < this.backgroundNumberOfChannels)
                                    {
                                        bgRegionCounts += (double)this.backgroundEnergySpectrum.Spectrum[i];
                                    }
                                }
                                else
                                {
                                    double[] bgInScale = this.BackgroundInSpectrumScale();
                                    bgRegionCounts += bgInScale[i];
                                }
                            }
                        }
                    }
                    double fgSigma = Math.Sqrt(fgRegionCounts);
                    double bgSigma = Math.Sqrt(bgRegionCounts);
                    if (this.bg && bgTime != 0.0)
                    {
                        double bgCps = bgRegionCounts / bgTime;
                        // (`AMBER192`, попутно) В зону идёт k·net, и дисперсия
                        // масштабированного счёта — k²·σ²: коэффициент примитива
                        // в квадрате, как у ветвей Ковелла и ссылки ниже. Прежде
                        // его не было вовсе — МДА зоны с k ≠ 1 не масштабировался.
                        double k2 = roiprimitiveData.Coefficient * roiprimitiveData.Coefficient;
                        num6 = k2 * bgCps * (1.0 / fgTime + 1.0 / bgTime);
                        hasBg = true;
                    }
                    if (this.bg && this.backgroundCountingTime != 0.0)
                    {
                        double bgNormalizeCoeff = this.countingTime / this.backgroundCountingTime;
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
                    lowerLimitChannelIndex = (int)Math.Ceiling(PolynomialEnergyCalibration.ChannelOf(this.energyCalibration, lowerLimit2, this.energySpectrum.NumberOfChannels));
                    upperLimitChannelIndex = (int)Math.Floor(PolynomialEnergyCalibration.ChannelOf(this.energyCalibration, upperLimit2, this.energySpectrum.NumberOfChannels));
                    double leftRegionCenter = roicovellMethodData.LeftRegionCenter;
                    double rightRegionCenter = roicovellMethodData.RightRegionCenter;
                    double leftRegionWidth = roicovellMethodData.LeftRegionWidth;
                    double rightRegionWidth = roicovellMethodData.RightRegionWidth;
                    int num17;
                    int num18;
                    int num19;
                    int num20;
                    num17 = (int)Math.Ceiling(PolynomialEnergyCalibration.ChannelOf(this.energyCalibration, leftRegionCenter - leftRegionWidth / 2.0, this.energySpectrum.NumberOfChannels));
                    num18 = (int)Math.Floor(PolynomialEnergyCalibration.ChannelOf(this.energyCalibration, leftRegionCenter + leftRegionWidth / 2.0, this.energySpectrum.NumberOfChannels));
                    num19 = (int)Math.Ceiling(PolynomialEnergyCalibration.ChannelOf(this.energyCalibration, rightRegionCenter - rightRegionWidth / 2.0, this.energySpectrum.NumberOfChannels));
                    num20 = (int)Math.Floor(PolynomialEnergyCalibration.ChannelOf(this.energyCalibration, rightRegionCenter + rightRegionWidth / 2.0, this.energySpectrum.NumberOfChannels));
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
                    // ⛔ (`AMBER192`) Ширина и центр каждого окна — по каналам
                    //    ВНУТРИ спектра, ровно тем, что вошли в суммы выше.
                    //    Прежде ширина считалась по номерам каналов до зажима:
                    //    окно у края спектра (`ChannelOf` зажимает к N) несло в
                    //    ширине каналы, которых нет в сумме, и ровный спектр
                    //    100 отсч./канал у верхнего края давал net = +442.9
                    //    вместо 0. Окно уже канала (ширина 0) делило на ноль —
                    //    net = NaN при IsValid = true; теперь это отказ зоны
                    //    словами.
                    int lastChannel = this.numberOfChannels - 1;
                    int peakLo = Math.Max(lowerLimitChannelIndex, 0);
                    int peakHi = Math.Min(upperLimitChannelIndex, lastChannel);
                    int leftLo = Math.Max(num17, 0);
                    int leftHi = Math.Min(num18, lastChannel);
                    int rightLo = Math.Max(num19, 0);
                    int rightHi = Math.Min(num20, lastChannel);
                    double num27 = (double)(leftHi - leftLo + 1);
                    double num28 = (double)(rightHi - rightLo + 1);
                    double num29 = (double)(peakHi - peakLo + 1);
                    if (!(num27 >= 1.0) || !(num28 >= 1.0) || !(num29 >= 1.0))
                    {
                        this.roiFailure = Properties.Resources.ROICovellWindowNarrow;
                        return false;
                    }
                    double num24 = (double)(peakLo + peakHi) / 2.0;
                    double num25 = (double)(leftLo + leftHi) / 2.0;
                    double num26 = (double)(rightLo + rightHi) / 2.0;
                    double num30;
                    double num31;
                    if (num26 != num25)
                    {
                        num30 = num29 / num28 * (num24 - num25) / (num26 - num25);
                        num31 = num29 / num27 * (num26 - num24) / (num26 - num25);
                    }
                    else
                    {
                        // Degenerate side windows (same centers) - the trapezoid weights
                        // used to divide by zero here; fall back to an even 50/50 split.
                        num30 = num29 / num28 * 0.5;
                        num31 = num29 / num27 * 0.5;
                    }
                    netCounts = num21 - num30 * num23 - num31 * num22;
                    netCountsSigma = Math.Sqrt(num21 + Math.Pow(num31, 2.0) * num22 + Math.Pow(num30, 2.0) * num23);
                    double num32 = Math.Abs(roiprimitiveData.Coefficient);
                    // Contribution to the H0 variance in cps^2: (coeff * sigma / t)^2.
                    // The old expression mixed dimensions (counts/t^2*k + (counts/t^2*k)^2)
                    // and used the coefficient linearly instead of squared.
                    num6 = Math.Pow(num32 * netCountsSigma / fgTime, 2.0);
                }
                else if (roiprimitiveData is ROIReferenceData)
                {
                    ROIReferenceData roireferenceData = (ROIReferenceData)roiprimitiveData;
                    bool referenceFound = false;
                    foreach (ROIDefinitionData roidefinitionData in this.roiConfig.ROIDefinitions)
                    {
                        if (roidefinitionData.Name == roireferenceData.Reference)
                        {
                            referenceFound = true;
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
                    // ⛔ (`AMBER191`) Зоны-цели нет (переименована в обход окна,
                    //    удалена, ссылка не выбрана) — ОШИБКА ЗОНЫ, а не ноль.
                    //    Прежде netCounts оставался 0, строка была валидна, и
                    //    вычитаемое молча пропадало: счёт и Бк зоны завышены.
                    if (!referenceFound)
                    {
                        this.roiFailure = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            Properties.Resources.ROIReferenceMissing, roireferenceData.Reference ?? "");
                        return false;
                    }
                    double coeff = Math.Abs(roiprimitiveData.Coefficient);
                    // Same dimensional fix as in the Covell branch above.
                    num6 = Math.Pow(coeff * netCountsSigma / fgTime, 2.0);
                }
                double coefficient = roiprimitiveData.Coefficient;
                double coefficientError = roiprimitiveData.CoefficientError;
                double countsToUse = netCounts * coefficient;
                double countsToUseError = 0.0;
                if (netCounts != 0.0 && coefficient != 0.0)
                {
                    countsToUseError = countsToUse * Math.Sqrt(Math.Pow(netCountsSigma / netCounts, 2.0) + Math.Pow(coefficientError / coefficient, 2.0));
                }
                else if (coefficient != 0.0)
                {
                    // netCounts == 0 still has a counting uncertainty: report 0 +- sigma,
                    // not 0 +- 0.
                    countsToUseError = Math.Abs(coefficient) * netCountsSigma;
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
                // Currie form (Ld = k^2/t + 2*k*sigma0), consistent with
                // ROIAriphmetics.CalculateLd; the old expression solved x = k*sqrt(x/t + S)
                // and understated the MDA up to ~2x for dominating background.
                double mdaCps = detectionLevel * detectionLevel / fgTime + 2.0 * detectionLevel * Math.Sqrt(num);
                roi.MDA = mdaCps * this.countingTime;
            }
            return true;
        }

        /// <summary>
        /// (`AMBER109`) Фон, переложенный в шкалу спектра по перекрытию энергий,
        /// — один раз на расчёт (<see cref="Calculate"/> сбрасывает): зон
        /// много, а фон и калибровки на расчёт одни.
        /// </summary>
        double[] BackgroundInSpectrumScale()
        {
            if (this.bgInSpectrumScale == null)
            {
                this.bgInSpectrumScale = Utils.SpectrumAriphmetics.RebinByEnergy(this.backgroundEnergySpectrum.Spectrum,
                    this.backgroundEnergyCalibration, this.energyCalibration, this.numberOfChannels);
            }
            return this.bgInSpectrumScale;
        }

        double[] bgInSpectrumScale;

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
        /// <summary>(`AMBER35`) Знаменатель скорости спектра: живое, если задано, иначе полное.</summary>
        double countingTime;

        // Token: 0x04000312 RID: 786
        /// <summary>(`AMBER35`) То же у фона.</summary>
        double backgroundCountingTime;

        // Token: 0x04000313 RID: 787
        decimal detectionLevel;
    }
}
