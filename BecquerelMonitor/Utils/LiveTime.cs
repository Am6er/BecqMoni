namespace BecquerelMonitor.Utils
{
    public class LiveTime
    {
        public static double Calculate(double measuremtntTime, double totalPulseCount, double deadTime)
        {
            if (measuremtntTime == 0 || totalPulseCount == 0) return 0;
            double detected_cps = totalPulseCount / measuremtntTime;
            double estimated_true_cps = (detected_cps) / (1 - detected_cps * deadTime);
            return measuremtntTime * (detected_cps / estimated_true_cps);
        }

        public static double Calculate2(double measuremtntTime, double cps, double deadTime)
        {
            if (cps == 0) return 0;
            double detected_cps = cps;
            double estimated_true_cps = (detected_cps) / (1 - detected_cps * deadTime);
            return measuremtntTime * (detected_cps / estimated_true_cps);
        }
    }
}
