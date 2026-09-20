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

        /// <summary>
        /// (`AMBER35`, решение Amber 15.09.2026 «согласовать все связанные
        /// пути по живому времени») ЗНАМЕНАТЕЛЬ СКОРОСТИ СЧЁТА — ОДНО ПРАВИЛО
        /// В ОДНОМ МЕСТЕ: живое время, если оно задано (&gt; 0), иначе полное.
        ///
        /// Это правило разбора FSA (<c>FsaAnalyzer</c>), и с 15.09.2026 оно же
        /// у активности выделения, зон, мощности дозы, общего вычитания фона
        /// (<c>SpectrumAriphmetics.Substract</c>, откуда вычтенный спектр идёт в
        /// поиск пиков и в вывоз CSV), графика в имп/с и cps в заголовке вывоза.
        /// До того три первых пути делили на ПОЛНОЕ время, и при мёртвом
        /// времени d одна линия на одном экране несла две скорости счёта: в
        /// отчёте FSA n/T_живое, на панели выделения и в зоне n/T_полное —
        /// а Бк, Бк/кг, Бк/л и мкЗв/ч были занижены на d.
        ///
        /// ⛔ Правило ОДНО, и потребители зовут его, а не переписывают:
        /// <c>EnergySpectrum.EffectiveLiveTime</c> для спектра,
        /// <c>MeasurementResultCollection.CountingTime</c> для коллекции
        /// результатов зон. Копия «live &gt; 0 ? live : full» в третьем месте
        /// разойдётся с этими двумя молча.
        ///
        /// ⚠ У спектра без живого времени (LiveTime == 0) знаменатель — ровно
        /// <paramref name="measurementTime"/>, побитово: все прежние числа на
        /// таком спектре сохраняются. Живое время, ПРЕВЫШАЮЩЕЕ полное, не
        /// правится и не зажимается: это не порча знаменателя, а порча данных,
        /// и прятать её округлением сюда нельзя.
        /// </summary>
        public static double Effective(double liveTime, double measurementTime)
        {
            return liveTime > 0.0 ? liveTime : measurementTime;
        }
    }
}
