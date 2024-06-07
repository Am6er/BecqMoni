using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

                // data comes too fast, skip this counts
                if (resultData.CountRates[resultData.CountRates.Count - 1].ElapsedTimeInMs == new_time ||
                new_time - resultData.CountRates[resultData.CountRates.Count - 1].ElapsedTimeInMs < 1000.0) return;
            }
            
            if (resultData.CountRates.Count >= UpperWindow)
            {
                resultData.CountRates.RemoveAt(0);
            }
            resultData.CountRates.Add(new CountRate(new_counts, new_time));
        }

        public decimal GetCPS(ResultData resultData, decimal window)
        {
            if (resultData.CountRates.Count <= 3) return 0;
            double win_ms = resultData.CountRates[resultData.CountRates.Count - 1].ElapsedTimeInMs - (double)(window * 1000);
            CountRate last_countRate = resultData.CountRates[resultData.CountRates.Count - 1];
            CountRate first_countRate = null;

            for (int i = resultData.CountRates.Count - 2; i >= 0; i--)
            {
                if (resultData.CountRates[i].ElapsedTimeInMs >= win_ms) {
                    first_countRate = resultData.CountRates[i];
                }
                else
                {
                    if (first_countRate == null ||
                        last_countRate.ElapsedTimeInMs <= first_countRate.ElapsedTimeInMs)
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
            return (decimal)result;
        }

    }
}
