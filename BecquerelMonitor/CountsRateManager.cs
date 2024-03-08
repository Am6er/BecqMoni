using System;
using System.Collections.Generic;
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
            int new_counts = resultData.EnergySpectrum.ValidPulseCount;
            double new_time = resultData.ResultDataStatus.ElapsedTime.TotalMilliseconds;

            if (new_time == 0) return; 
            
            if (resultData.CountRates.Count >= UpperWindow)
            {
                resultData.CountRates.RemoveAt(0);
            }
            resultData.CountRates.Add(new CountRate(new_counts, new_time));
        }

        public decimal GetCPS(ResultData resultData, decimal window)
        {
            if (resultData.CountRates.Count <= 1) return 0;
            double win_ms = resultData.CountRates[resultData.CountRates.Count - 1].ElapsedTimeInMs - (double)(window * 1000);
            CountRate last_countRate = resultData.CountRates[resultData.CountRates.Count - 1];
            CountRate first_countRate = null;

            int min = Math.Min(UpperWindow, resultData.CountRates.Count - 1);
            for (int i = min; i >= 0; i--)
            {
                if (resultData.CountRates[i].ElapsedTimeInMs >= win_ms) {
                    first_countRate = resultData.CountRates[i];
                } else
                {
                    break;
                }
            }
            if (first_countRate == null ||
                last_countRate.Counts < first_countRate.Counts ||
                last_countRate.ElapsedTimeInMs <= first_countRate.ElapsedTimeInMs) { return 0; }
            double result = ((double)(last_countRate.Counts - first_countRate.Counts) * 1000.0) / (last_countRate.ElapsedTimeInMs - first_countRate.ElapsedTimeInMs);
            return (decimal)result;
        }

    }
}
