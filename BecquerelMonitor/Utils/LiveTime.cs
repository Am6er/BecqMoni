namespace BecquerelMonitor.Utils
{
    public class LiveTime
    {
        // Non-paralyzable dead-time model. Algebraically the result is
        // measurementTime * (1 - cps * deadTime), which is only meaningful for
        // 0 <= cps * deadTime < 1. Beyond that the detector is saturated: the old
        // code returned a NEGATIVE live time (and dead time > 100% in the UI).
        public static double Calculate(double measuremtntTime, double totalPulseCount, double deadTime)
        {
            if (measuremtntTime == 0 || totalPulseCount == 0) return 0;
            double detected_cps = totalPulseCount / measuremtntTime;
            double liveTime = measuremtntTime * (1.0 - detected_cps * deadTime);
            return liveTime > 0.0 ? liveTime : 0.0;
        }

        public static double Calculate2(double measuremtntTime, double cps, double deadTime)
        {
            if (cps == 0) return 0;
            double liveTime = measuremtntTime * (1.0 - cps * deadTime);
            return liveTime > 0.0 ? liveTime : 0.0;
        }
    }
}
