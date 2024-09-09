namespace BecquerelMonitor
{
    public class CountsRateManager
    {
        const int UpperWindow = 120;

        public void AppendResultData(ResultData resultData)
        {
            if (!resultData.ResultDataStatus.Recording) return;
            int new_counts = resultData.EnergySpectrum.TotalPulseCount;
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

        public double GetCPS(ResultData resultData, double window)
        {
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

        public double GetDeadTime(ResultData resultData, double window)
        {
            if (resultData.DeviceConfig == null ||
                resultData.DeviceConfig.InputDeviceConfig == null ||
                resultData.DeviceConfig.InputDeviceConfig.DeadTime() == 0)
            { return 0; }

            double deadTime = resultData.DeviceConfig.InputDeviceConfig.DeadTime();
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

        public int GetSpecEffRatio(ResultData resultData, double window)
        {
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
    }
}
