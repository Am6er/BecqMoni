using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    /// <summary>
    /// Перевод строк результата (амплитуда разбора FSA за время счёта) в имп/с,
    /// Бк, Бк/кг, Бк/л и приведение к дате отбора. (`AMBER208`, 07.10.2026)
    /// Счёт площадей зон ROI (<c>Calculate</c>/<c>CalculateROI</c>, Ковелл,
    /// ссылки на зоны, МДА Карри) снят вместе с формой ROI по решению Amber
    /// «максимально почистить остатки»; строки несёт <see cref="MeasurementLine"/>.
    /// </summary>
    public class MeasurementResultManager
    {
        public MeasurementResultCollection Translate(MeasurementResultCollection resultCollection, ResultTranslation resultTranslation)
        {
            if (resultCollection == null)
            {
                return null;
            }
            MeasurementResultCollection output = Shell(resultCollection);
            ResultData resultData = resultCollection.ResultData;
            if (resultData == null || resultData.EnergySpectrum == null)
            {
                return null;
            }
            // (`AMBER35`, 15.09.2026) Знаменатель — `CountingTime` коллекции:
            // живое, если задано, иначе полное.
            double countingTime = resultCollection.CountingTime;
            bool needsCoefficient =
                resultTranslation == ResultTranslation.Becquerels
                || resultTranslation == ResultTranslation.BecquerelsPerKilogram
                || resultTranslation == ResultTranslation.BecquerelsPerLiter;
            foreach (MeasurementResult measurementResult in resultCollection.ResultList)
            {
                MeasurementLine line = measurementResult.Line;
                // Невалидный результат обязан остаться невалидным: перевод
                // единиц создаёт НОВЫЕ объекты, и без переноса флага строка
                // «Ошибка» превращалась в молчаливый честный ноль.
                if (!measurementResult.IsValid)
                {
                    output.ResultList.Add(Invalid(line, measurementResult.StatusText));
                    continue;
                }
                if (countingTime == 0.0)
                {
                    output.ResultList.Add(new MeasurementResult(line, 0.0, 0.0));
                    continue;
                }

                // K — одна точка: у строки разбора 1 при абсолютной шкале
                // (амплитуда уже в распадах в секунду) и 0, когда беккерели
                // скрыты. Беккерели без K не считаются: нулевой коэффициент
                // молча давал 0 Бк — неотличимо от настоящего нуля (TODO G7).
                double k = line != null ? line.Coefficient : 0.0;
                double kError = line != null ? line.CoefficientError : 0.0;
                string cannot = null;
                if (needsCoefficient && !(k > 0.0))
                {
                    cannot = Properties.Resources.ResultNoCoefficient;
                }
                else if (resultTranslation == ResultTranslation.BecquerelsPerKilogram
                         && !(resultData.SampleInfo.Weight > 0.0))
                {
                    cannot = Properties.Resources.ResultNoWeight;
                }
                else if (resultTranslation == ResultTranslation.BecquerelsPerLiter
                         && !(resultData.SampleInfo.Volume > 0.0))
                {
                    cannot = Properties.Resources.ResultNoVolume;
                }
                if (cannot != null)
                {
                    output.ResultList.Add(Invalid(line, cannot));
                    continue;
                }

                double resultValue = measurementResult.ResultValue;
                double resultError = measurementResult.ResultError;
                double mda = measurementResult.MDA;
                double resultCps = resultValue / countingTime;
                double resultErrorCps = Math.Abs(resultError) / countingTime;
                double resultBq = resultCps * k;
                double resultBqError = 0.0;
                if (resultCps != 0.0 && k != 0.0)
                {
                    resultBqError = Math.Abs(resultBq * Math.Sqrt(Math.Pow(resultErrorCps / resultCps, 2.0) + Math.Pow(kError / k, 2.0)));
                }
                double mdaCps = mda / countingTime;
                double mdaBq = mdaCps * k;
                double value2 = 0.0, error2 = 0.0, mda2 = 0.0;
                switch (resultTranslation)
                {
                    case ResultTranslation.Nothing:
                        value2 = resultValue; error2 = resultError; mda2 = mda;
                        break;
                    case ResultTranslation.CountsPerSecond:
                        value2 = resultCps; error2 = resultErrorCps; mda2 = mdaCps;
                        break;
                    case ResultTranslation.Becquerels:
                        value2 = resultBq; error2 = resultBqError; mda2 = mdaBq;
                        break;
                    case ResultTranslation.BecquerelsPerKilogram:
                        // вес проверен выше — знаменатель не ноль
                        value2 = resultBq / resultData.SampleInfo.Weight;
                        error2 = resultBqError / resultData.SampleInfo.Weight;
                        mda2 = mdaBq / resultData.SampleInfo.Weight;
                        break;
                    case ResultTranslation.BecquerelsPerLiter:
                        value2 = resultBq / resultData.SampleInfo.Volume;
                        error2 = resultBqError / resultData.SampleInfo.Volume;
                        mda2 = mdaBq / resultData.SampleInfo.Volume;
                        break;
                }
                output.ResultList.Add(new MeasurementResult(line, value2, error2, mda2));
            }
            return output;
        }

        public MeasurementResultCollection Correct(MeasurementResultCollection resultCollection)
        {
            if (resultCollection == null)
            {
                return null;
            }
            MeasurementResultCollection output = Shell(resultCollection);
            ResultData resultData = resultCollection.ResultData;
            if (resultData == null || resultData.EnergySpectrum == null)
            {
                return null;
            }
            foreach (MeasurementResult measurementResult in resultCollection.ResultList)
            {
                MeasurementLine line = measurementResult.Line;
                // Тот же перенос невалидности, что в Translate: поправка на
                // распад создаёт новые объекты и теряла флаг.
                if (!measurementResult.IsValid)
                {
                    output.ResultList.Add(Invalid(line, measurementResult.StatusText));
                    continue;
                }
                // ⛔ (`AMBER148`, П192 01.10.2026) Множитель — от СРЕДНЕЙ за
                //    набор к активности на дату отбора (`DecayToSamplingFactor`).
                //    Прежде стояло 2^((EndTime − SampleInfo.Time)/T½) — от КОНЦА
                //    набора, и Бк короткоживущих выходили завышены на ≈ λT/2
                //    (I-131 за сутки +4.4 %, при T = T½ +44 %). MDA умножается тем
                //    же множителем: порог стоял на дату измерения, значение — на
                //    дату отбора, и при условии обнаружения «≥ MDA» поправка
                //    делала необнаруженное обнаруженным. Период ≤ 0 — множитель 1.
                double factor = DecayToSamplingFactor(line != null ? line.HalfLifeYears : 0.0, resultData.SampleInfo.Time,
                                                      resultData.StartTime, resultData.EndTime);
                output.ResultList.Add(new MeasurementResult(line, measurementResult.ResultValue * factor,
                                                            measurementResult.ResultError * factor,
                                                            measurementResult.MDA * factor));
            }
            return output;
        }

        static MeasurementResultCollection Shell(MeasurementResultCollection source)
        {
            return new MeasurementResultCollection
            {
                ResultData = source.ResultData,
                SourceKey = source.SourceKey,
                MeasurementTime = source.MeasurementTime,
                LiveTime = source.LiveTime
            };
        }

        static MeasurementResult Invalid(MeasurementLine line, string statusText)
        {
            return new MeasurementResult(line, 0.0, 0.0)
            {
                IsValid = false,
                StatusText = statusText
            };
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
    }
}
