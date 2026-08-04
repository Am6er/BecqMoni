using System;
using System.Globalization;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.Utils
{
    /// <summary>
    /// Коэффициент перевода счёта в беккерели: Bq = cps · K.
    ///
    /// Раньше K был числом, которое пользователь вписывал руками, получив его
    /// из измерения образцового источника. Теперь это ФУНКЦИЯ параметров зоны и
    /// активной кривой эффективности:
    ///
    ///     K = 100 / (ε(E) · I),   dK = K · δ(E) / 100
    ///
    /// где E — энергия линии зоны, I — её выход в процентах, ε — эффективность
    /// полного поглощения долей, δ — погрешность кривой в процентах. Обе
    /// величины берутся у одного интерполятора <see cref="FsaEfficiency"/>.
    ///
    /// Формула dK = K·δ/100 — решение пользователя от 04.08.2026. Вариант
    /// 100/(δ·I) из «Efficiency Calibration.ods» отвергнут числами: он давал
    /// относительную погрешность в 4–13 раз завышенную у Cs-137 и Ra-226 и в
    /// 70–170 раз заниженную у K-40 и Th-232 2615 — знак расхождения менялся,
    /// физики за этим нет.
    ///
    /// Сохранённое число остаётся ЗАПАСНЫМ. Оно и подставляется, когда кривой
    /// нет: без этого включение расчёта по кривой обнулило бы активность всем,
    /// у кого кривая ещё не заведена.
    /// </summary>
    public static class BecquerelCoefficient
    {
        public enum Source
        {
            /// <summary>Взят из поля зоны, как раньше.</summary>
            Stored,

            /// <summary>Посчитан по кривой эффективности.</summary>
            Efficiency,
        }

        public struct Result
        {
            public double Value;
            public double Error;
            public Source From;

            /// <summary>
            /// Почему не получилось посчитать по кривой, если не получилось.
            /// null — считалось по кривой либо расчёт по ней не запрашивали.
            /// Строка нужна ФОРМЕ: в таблице результатов места для неё нет, а
            /// молчащий откат на старое число — ровно тот случай, когда числа
            /// меняются, а сказать об этом некому.
            /// </summary>
            public string Problem;
        }

        /// <summary>
        /// Коэффициент для ОТДЕЛЬНОЙ линии, не связанной с зоной: выделили
        /// область на спектре, в ней ровно один распознанный пик — активность
        /// считается по нему.
        ///
        /// Отдельный вход нужен потому, что зоны здесь нет вовсе, а формула
        /// обязана быть одна: до этого в отрисовке лежала её третья по счёту
        /// копия, и разойтись им было нечем помешать.
        /// </summary>
        public static bool TryForLine(double energyKev, double intensityPercent,
                                      EfficiencyConfigData efficiency,
                                      out double value, out double error)
        {
            value = 0.0;
            error = 0.0;
            if (!(energyKev > 0.0) || !(intensityPercent > 0.0))
            {
                return false;
            }

            FsaEfficiency curve = FsaEfficiency.FromConfig(efficiency);
            double eps, errorPercent;
            if (curve == null || !curve.TryEval(energyKev, out eps, out errorPercent) || !(eps > 0.0))
            {
                return false;
            }

            value = 100.0 / (eps * intensityPercent);
            error = value * errorPercent / 100.0;
            return true;
        }

        /// <summary>
        /// Какой коэффициент действует для этой зоны при этой кривой.
        /// </summary>
        public static Result Resolve(ROIDefinitionData roi, EfficiencyConfigData efficiency)
        {
            Result result = new Result
            {
                Value = roi == null ? 0.0 : roi.BecquerelCoefficient,
                Error = roi == null ? 0.0 : roi.BecquerelCoefficientError,
                From = Source.Stored,
                Problem = null,
            };

            if (roi == null || !roi.AutoBecquerelCoefficient)
            {
                return result;
            }

            if (roi.Intencity <= 0.0)
            {
                result.Problem = Resources.BqCoeffNoIntensity;
                return result;
            }

            if (!(roi.PeakEnergy > 0.0))
            {
                result.Problem = Resources.BqCoeffNoEnergy;
                return result;
            }

            FsaEfficiency curve = FsaEfficiency.FromConfig(efficiency);
            if (curve == null)
            {
                result.Problem = Resources.BqCoeffNoCurve;
                return result;
            }

            double eps, errorPercent;
            if (!curve.TryEval(roi.PeakEnergy, out eps, out errorPercent))
            {
                result.Problem = string.Format(CultureInfo.CurrentCulture, Resources.BqCoeffOutOfRange,
                                               roi.PeakEnergy, curve.MinEnergy, curve.MaxEnergy);
                return result;
            }

            if (!(eps > 0.0))
            {
                result.Problem = Resources.BqCoeffNoCurve;
                return result;
            }

            result.Value = 100.0 / (eps * roi.Intencity);
            result.Error = result.Value * errorPercent / 100.0;
            result.From = Source.Efficiency;
            result.Problem = null;
            return result;
        }
    }
}
