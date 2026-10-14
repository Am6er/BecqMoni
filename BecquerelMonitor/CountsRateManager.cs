using System;
using System.Collections.Generic;

namespace BecquerelMonitor
{
    public class CountsRateManager
    {
        const int UpperWindow = 120;

        public void AppendResultData(ResultData resultData)
        {
            if (!resultData.ResultDataStatus.Recording) return;
            long new_counts = resultData.EnergySpectrum.TotalPulseCount;
            double new_time = resultData.ResultDataStatus.ElapsedTime.TotalMilliseconds;

            if (new_time == 0) return;
            if (resultData.CountRates.Count > 0)
            {
                // if old counts > new counts or old time > new time, then reset history. Because it's new spectrum.
                if (resultData.CountRates[resultData.CountRates.Count - 1].Counts > new_counts ||
                resultData.CountRates[resultData.CountRates.Count - 1].ElapsedTimeInMs > new_time) resultData.CountRates.Clear();
            }

            if (resultData.CountRates.Count > 0)
            {
                // data comes too fast, skip this counts
                if (resultData.CountRates[resultData.CountRates.Count - 1].ElapsedTimeInMs == new_time ||
                new_time - resultData.CountRates[resultData.CountRates.Count - 1].ElapsedTimeInMs < 1000.0) return;
            }

            if (resultData.CountRates.Count >= UpperWindow)
            {
                resultData.CountRates.RemoveAt(0);
            }
            resultData.CountRates.Add(new CountRate(new_counts, resultData.EnergySpectrum.TotalPulseCount - resultData.EnergySpectrum.ValidPulseCount, new_time));
        }

        /// <summary>
        /// Скорость счёта на окне <paramref name="window"/> (с), имп/с.
        ///
        /// ⛔ П250 (находка ревизии 13.10.2026): прибор со СТУПЕНЧАТЫМИ данными
        /// (<see cref="IDeviceLiveTimeSource"/>, `AmplitudaSerial`) отдаёт отсчёты
        /// раз в цикл опроса (период опроса + чтение блока: 3 + 2.5 ≈ 5.5 с), а
        /// реальное время идёт непрерывно. Окно в 3 с (умолчание панели) ловило
        /// ступеньку через раз: на модели `DeadTimeZeroProbeP250` 61 % обновлений
        /// показывали 0, остальные — до 2.75 истинной скорости (после правки
        /// 0.83…1.23, при окне 30 с 0.97…1.03). Такому прибору концы окна ставятся на моменты, когда
        /// данные пришли (<see cref="StepwiseCPS"/>); прочим — прежний расчёт,
        /// байт в байт.
        /// </summary>
        public double GetCPS(ResultData resultData, double window)
        {
            if (StepwiseData(resultData)) return StepwiseCPS(resultData.CountRates, window);
            if (resultData.CountRates.Count <= 3) return 0;
            double win_ms = resultData.CountRates[resultData.CountRates.Count - 1].ElapsedTimeInMs - (double)(window * 1000.0);
            CountRate last_countRate = resultData.CountRates[resultData.CountRates.Count - 1];
            CountRate first_countRate = null;

            for (int i = resultData.CountRates.Count - 2; i >= 0; i--)
            {
                if (resultData.CountRates[i].ElapsedTimeInMs >= win_ms) {
                    first_countRate = resultData.CountRates[i];
                }
                else
                {
                    if (first_countRate == null || last_countRate.ElapsedTimeInMs <= first_countRate.ElapsedTimeInMs)
                    {
                        first_countRate = resultData.CountRates[i];
                        continue;
                    }
                    break;
                }
            }
            if (last_countRate.ElapsedTimeInMs == first_countRate.ElapsedTimeInMs) { return 0; }
            double result = ((double)(last_countRate.Counts - first_countRate.Counts) * 1000.0) / (last_countRate.ElapsedTimeInMs - first_countRate.ElapsedTimeInMs);
            if (result < 0)
            {
                return 0;
            }
            return result;
        }

        /// <summary>
        /// Мёртвое время для панели скорости счёта, в процентах.
        ///
        /// У прибора с мёртвым временем τ &gt; 0 — на окне <paramref name="window"/>
        /// по скорости счёта и τ, как было (байт в байт).
        ///
        /// ⛔ `A319` (решение Amber 13.10.2026, «Чинить ночью так»): τ = 0 НЕ
        /// значит «мёртвого времени нет». Ноль отдаёт и прибор, который сам мерит
        /// живое время (<see cref="IDeviceLiveTimeSource"/>, `AmplitudaSerial`), —
        /// у него мёртвое время есть, и прежде панель показывала 0 % при ≈30 % по
        /// времени блока. Тогда доля берётся из живого и реального времени САМОГО
        /// СПЕКТРА (<see cref="SpectrumDeadTimePercent"/>) — по всему спектру, а
        /// не на окне: история скорости счёта живого времени не хранит, а живое
        /// время такого прибора приходит ступенькой раз в цикл опроса (период
        /// опроса 2–60 с плюс чтение блока) при непрерывном реальном — на окне в
        /// 3 с (умолчание панели) доля прыгала бы между 0 и 100 %. Число то же,
        /// что «Мёртвое время %» панели измерения (<c>DCControlPanel.ShowRecordingStatus</c>).
        /// Разбор — журнал `handover/handover-2026-10-14-p250-a319-deadtime-zero.md`.
        /// </summary>
        public double GetDeadTime(ResultData resultData, double window)
        {
            if (resultData.DeviceConfig == null ||
                resultData.DeviceConfig.InputDeviceConfig == null)
            { return 0; }

            double deadTime = resultData.DeviceConfig.InputDeviceConfig.DeadTime();
            if (deadTime == 0)
            {
                return SpectrumDeadTimePercent(resultData.EnergySpectrum);
            }

            double liveTime = Utils.LiveTime.Calculate2(window, GetCPS(resultData, window), deadTime);
            if (liveTime != 0)
            {
                return 100.0 * (window - liveTime) / window;
            }
            else
            {
                return 0;
            }
        }

        /// <summary>
        /// (`A319`) Доля мёртвого времени спектра по его собственным временам,
        /// в процентах: 100·(реальное − живое)/реальное. Ноль, если живого
        /// времени нет (LiveTime == 0 — «не задано», правило
        /// <see cref="Utils.LiveTime.Effective"/>), если реального нет, и если
        /// живое не меньше реального (живое &gt; реального — порча данных, её
        /// здесь не прячут, но и долю из неё не выводят).
        ///
        /// ⚠ ПОКА ИДЁТ НАБОР, число у прибора с собственным живым временем
        /// ЗАВЫШЕНО. У `AmplitudaSerial` живое время берётся из заголовка блока,
        /// который читается ПЕРВЫМ (статус → заголовок → страницы,
        /// <c>AmplitudaSerialLine.ReadJob</c>), а на экран приходит после всех
        /// страниц; реальное идёт по часам ПК непрерывно, а следующее чтение
        /// начинается через период опроса Δ после конца прежнего
        /// (<c>AmplitudaSerialAcquisition</c>). Реальное время обгоняет момент, к которому
        /// относится живое, на t_чт … Δ + 2·t_чт (t_чт — чтение страниц), и ещё
        /// на ±0.5 с — живое приходит целыми секундами. Отсюда завышение от
        /// (1 − d)·(t_чт − 0.5)/T до (1 − d)·(Δ + 2·t_чт + 0.5)/T (d — истинная
        /// доля, T — реальное время): при Δ = 3 с, t_чт = 2.5 с (1024 канала) и
        /// d = 0.3 — до 9.9 пункта при T = 60 с, до 1.0 при T = 600 с; t_чт
        /// растёт с числом страниц (512 каналов на страницу). Учесть это здесь
        /// честно нельзя: момент чтения заголовка знает только
        /// <c>AmplitudaSerialAcquisition</c>. То же завышение несёт «Мёртвое
        /// время %» панели измерения. У остановленного и у загруженного спектра
        /// число точное.
        /// </summary>
        public static double SpectrumDeadTimePercent(EnergySpectrum spectrum)
        {
            if (spectrum == null) return 0;
            double real = spectrum.MeasurementTime;
            double live = spectrum.LiveTime;
            if (!(real > 0) || !(live > 0) || !(live < real)) return 0;
            return 100.0 * (real - live) / real;
        }

        /// <summary>
        /// (`A319`, решение Amber 13.10.2026) Решающая часть меню «Поправка на
        /// мёртвое время» — без окон, чтобы её мерила проба
        /// (`tools/effmaker/probes/DeadTimeZeroProbeP250.cs`).
        ///
        /// Конфигурация с мёртвым временем τ &gt; 0 — живое время пересчитывается
        /// по τ, как было: <c>Calculate(T, N, τ)</c>, байт в байт; возврат true.
        ///
        /// ⛔ τ = 0 (или конфигурации нет) — живое время НЕ трогается, возврат
        /// false: пересчёт с нулём лишь приравнял бы живое реальному и стёр бы
        /// время, которое прибор намерил сам (`AmplitudaSerial`: активность
        /// занижалась на долю мёртвого времени). Что сказать человеку, решает
        /// вызывающий (<c>MainForm.ApplyDeadTimeCorrectionStripMenuItem_Click</c>).
        /// </summary>
        public static bool ApplyDeadTimeCorrection(EnergySpectrum spectrum, InputDeviceConfig inputDeviceConfig)
        {
            if (spectrum == null || inputDeviceConfig == null) return false;
            double deadTime = inputDeviceConfig.DeadTime();
            if (deadTime == 0) return false;
            spectrum.LiveTime = Utils.LiveTime.Calculate(spectrum.MeasurementTime, spectrum.TotalPulseCount, deadTime);
            return true;
        }

        /// <summary>
        /// Доля неучтённых импульсов на окне, %. Прибору со ступенчатыми данными —
        /// на тех же концах, что и скорость счёта (<see cref="StepwiseEnds"/>);
        /// прочим — как было.
        /// </summary>
        public int GetSpecEffRatio(ResultData resultData, double window)
        {
            if (StepwiseData(resultData))
            {
                CountRate first, last;
                if (!StepwiseEnds(resultData.CountRates, window, out first, out last)) return 0;
                double total = last.Counts - first.Counts;
                if (total == 0.0) return 0;
                return (int)(100.0 * ((last.InvalidCounts - first.InvalidCounts) / total));
            }
            if (resultData.CountRates.Count <= 3) return 0;
            double win_ms = resultData.CountRates[resultData.CountRates.Count - 1].ElapsedTimeInMs - window * 1000.0;
            CountRate last_countRate = resultData.CountRates[resultData.CountRates.Count - 1];
            CountRate first_countRate = null;

            for (int i = resultData.CountRates.Count - 2; i >= 0; i--)
            {
                if (resultData.CountRates[i].ElapsedTimeInMs >= win_ms)
                {
                    first_countRate = resultData.CountRates[i];
                }
                else
                {
                    if (first_countRate == null || last_countRate.ElapsedTimeInMs <= first_countRate.ElapsedTimeInMs)
                    {
                        first_countRate = resultData.CountRates[i];
                        continue;
                    }
                    break;
                }
            }

            double totaldelta = last_countRate.Counts - first_countRate.Counts;
            double invaliddelta = last_countRate.InvalidCounts - first_countRate.InvalidCounts;

            if (totaldelta == 0.0) return 0;

            int result = (int)(100.0 * (invaliddelta / totaldelta));
            return result;
        }

        /// <summary>
        /// Прибор, данные которого приходят ступенькой: его контроллер сам ведёт
        /// живое время (<see cref="IDeviceLiveTimeSource"/>) — у `AmplitudaSerial`
        /// спектр, число отсчётов и живое время приходят одним чтением блока раз в
        /// цикл опроса. Признак — по контроллеру, а не по данным: поток с редкими
        /// отсчётами тоже стоит на месте между ними, а у него расчёт обязан
        /// остаться прежним.
        /// </summary>
        static bool StepwiseData(ResultData resultData)
        {
            return resultData.MeasurementController != null &&
                   resultData.MeasurementController.DeviceController is IDeviceLiveTimeSource;
        }

        /// <summary>
        /// Скорость счёта прибора со ступенчатыми данными — между двумя моментами,
        /// когда данные ПРИШЛИ (<see cref="StepwiseEnds"/>). Охват — целое число
        /// циклов опроса, не короче окна, поэтому ступенька не делит его через раз.
        /// Остаётся только шаг истории (отсчёт раз в ≥ 1 с, `MainForm`, такт 1000):
        /// каждый конец опаздывает на долю шага, и на охвате S число гуляет не больше
        /// чем на ±шаг/S — при окне 3 с и цикле 5.5 с до ≈±20 %, при окне 30 с —
        /// до ≈±3 % (мерено `DeadTimeZeroProbeP250`).
        /// </summary>
        static double StepwiseCPS(List<CountRate> rates, double window)
        {
            CountRate first, last;
            if (!StepwiseEnds(rates, window, out first, out last)) return 0;
            double result = (last.Counts - first.Counts) * 1000.0 / (last.ElapsedTimeInMs - first.ElapsedTimeInMs);
            return result < 0 ? 0 : result;
        }

        /// <summary>
        /// Концы окна на моментах прихода данных: отсчёты истории, на которых число
        /// отсчётов сменилось. Конец — последняя смена; начало — последняя смена не
        /// позже «конец − окно», а пока такой нет (история короче окна) — самая
        /// ранняя. false — смен меньше двух, либо данные перестали приходить: после
        /// последней смены прошло больше двух окон и двух самых длинных промежутков
        /// между приходами на охвате. Прежний расчёт в этом случае показывал 0 —
        /// признак «блок молчит» сохраняется.
        /// </summary>
        static bool StepwiseEnds(List<CountRate> rates, double window, out CountRate first, out CountRate last)
        {
            first = null;
            last = null;
            if (rates == null || rates.Count < 3) return false;

            int lastIdx = -1;
            for (int i = rates.Count - 1; i >= 1; i--)
            {
                if (rates[i].Counts != rates[i - 1].Counts)
                {
                    lastIdx = i;
                    break;
                }
            }
            if (lastIdx < 0) return false;

            double limit = rates[lastIdx].ElapsedTimeInMs - window * 1000.0;
            int firstIdx = -1;
            int previous = lastIdx;
            double maxGap = 0;
            for (int i = lastIdx - 1; i >= 1; i--)
            {
                if (rates[i].Counts == rates[i - 1].Counts) continue;
                maxGap = Math.Max(maxGap, rates[previous].ElapsedTimeInMs - rates[i].ElapsedTimeInMs);
                previous = i;
                firstIdx = i;
                if (rates[i].ElapsedTimeInMs <= limit) break;
            }
            if (firstIdx < 0) return false;

            double age = rates[rates.Count - 1].ElapsedTimeInMs - rates[lastIdx].ElapsedTimeInMs;
            if (age > 2.0 * Math.Max(window * 1000.0, maxGap)) return false;

            first = rates[firstIdx];
            last = rates[lastIdx];
            return last.ElapsedTimeInMs > first.ElapsedTimeInMs;
        }
    }
}
