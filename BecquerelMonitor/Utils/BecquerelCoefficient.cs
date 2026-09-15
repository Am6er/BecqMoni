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
    ///
    /// ⛔ (`AMBER34`, решение Amber 15.09.2026, дословно: «В автоматическом
    /// режиме скрыть активность и показать причину; явно ручной режим
    /// сохранить») Кривая СЦЕНЫ ПОЛЯ (`norm=fluence`, значения — см² на
    /// единичный флюенс) — не «кривой нет» и не откат на запасное число:
    /// в автоматическом режиме активность СКРЫВАЕТСЯ (<see cref="Result.Refused"/>),
    /// причина называется; ручной режим (галочка снята) кривую не спрашивает
    /// и остаётся как был.
    /// </summary>
    public static class BecquerelCoefficient
    {
        public enum Source
        {
            /// <summary>Взят из поля зоны, как раньше.</summary>
            Stored,

            /// <summary>Посчитан по кривой эффективности.</summary>
            Efficiency,

            /// <summary>
            /// (`AMBER34`) НЕ ПОЛУЧЕН И НЕ ПОДМЕНЁН: кривая сцены поля — по ней
            /// K не считается, а запасное число подставлять нельзя, иначе
            /// причина потонет в «каком-то» числе.
            /// </summary>
            Refused,
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

            /// <summary>
            /// (`AMBER34`) ОТКАЗ, А НЕ ОТКАТ: коэффициента нет, и запасное
            /// число НЕ подставлено — строка результата обязана стать
            /// невалидной со статусом <see cref="StatusText"/>. Прежние мягкие
            /// беды (кривой нет, энергия за краем, выхода нет) этого признака
            /// не несут и ведут себя как раньше — сохранённым K.
            /// </summary>
            public bool Refused;

            /// <summary>
            /// Короткая причина для клетки таблицы результатов (там места на
            /// фразу нет — «no K», «no weight»); полная — в <see cref="Problem"/>,
            /// её показывает подсказка формы зон.
            /// </summary>
            public string StatusText;
        }

        /// <summary>
        /// Почему коэффициент для отдельной линии НЕ получен. До 05.09.2026
        /// <see cref="TryForLine"/> отвечал голым <c>false</c>, и на панели
        /// выделения энергия за краем кривой была неотличима от «кривой нет
        /// вовсе» (измерено `BqActivityProbe`: обе границы кривой — «молча
        /// ничего»). На пути ЗОН ту же беду <see cref="Resolve"/> называла
        /// причиной с самого начала; теперь причина есть у обоих путей.
        /// </summary>
        public enum LineProblem
        {
            /// <summary>Получен.</summary>
            None,

            /// <summary>Энергия или выход не положительны — считать не из чего.</summary>
            NoInput,

            /// <summary>
            /// Кривой нет: конфигурации нет, либо в ней меньше двух годных точек
            /// (<see cref="FsaEfficiency.FromConfig"/> отвечает null).
            /// </summary>
            NoCurve,

            /// <summary>
            /// Энергия за краем таблицы кривой; края лежат в
            /// <see cref="LineResult.CurveMin"/> и <see cref="LineResult.CurveMax"/>.
            /// </summary>
            OutOfRange,

            /// <summary>Кривая ответила, но неположительным числом.</summary>
            NoEpsilon,

            /// <summary>
            /// (`AMBER34`) Кривая СЦЕНЫ ПОЛЯ: значения — см² на единичный
            /// флюенс, не доли на квант; активность по ней не считается.
            /// Имя кривой — в <see cref="LineResult.Refusal"/>.
            /// </summary>
            FieldCurve,

            /// <summary>
            /// (`AMBER34`) Кривая ОТВЕРГНУТА с названной причиной
            /// (<see cref="LineResult.Refusal"/>): у кривой долей есть точка
            /// выше единицы. Не <see cref="NoCurve"/>: та — «не выбрана», а
            /// здесь кривая выбрана и негодна.
            /// </summary>
            CurveRefused,
        }

        /// <summary>Ответ <see cref="ForLine"/>: коэффициент либо причина, почему его нет.</summary>
        public struct LineResult
        {
            public double Value;
            public double Error;
            public LineProblem Problem;

            /// <summary>Края таблицы кривой, кэВ; нули, когда кривой нет.</summary>
            public double CurveMin;
            public double CurveMax;

            /// <summary>
            /// (`AMBER34`) Слова к <see cref="LineProblem.CurveRefused"/> (причина
            /// отказа кривой) и к <see cref="LineProblem.FieldCurve"/> (имя
            /// кривой); null у прочих.
            /// </summary>
            public string Refusal;

            public bool Ok
            {
                get { return this.Problem == LineProblem.None; }
            }
        }

        /// <summary>
        /// Коэффициент для ОТДЕЛЬНОЙ линии, не связанной с зоной: выделили
        /// область на спектре, в ней ровно один распознанный пик — активность
        /// считается по нему.
        ///
        /// Отдельный вход нужен потому, что зоны здесь нет вовсе, а формула
        /// обязана быть одна: до этого в отрисовке лежала её третья по счёту
        /// копия, и разойтись им было нечем помешать.
        ///
        /// Отвечает ПРИЧИНОЙ, а не только отказом: у каждого <c>false</c>
        /// прежнего <see cref="TryForLine"/> должен быть читатель, иначе панель
        /// молчит (05.09.2026).
        /// </summary>
        public static LineResult ForLine(double energyKev, double intensityPercent,
                                         EfficiencyConfigData efficiency)
        {
            LineResult result = new LineResult
            {
                Value = 0.0,
                Error = 0.0,
                Problem = LineProblem.None,
                CurveMin = 0.0,
                CurveMax = 0.0,
                Refusal = null,
            };

            if (!(energyKev > 0.0) || !(intensityPercent > 0.0))
            {
                result.Problem = LineProblem.NoInput;
                return result;
            }

            string refusal;
            FsaEfficiency curve = FsaEfficiency.FromConfig(efficiency, out refusal);
            if (curve == null)
            {
                // (`AMBER34`) Кривая выбрана, но отвергнута с причиной — не то
                // же, что «не выбрана»: человеку называется точка.
                result.Problem = refusal != null ? LineProblem.CurveRefused : LineProblem.NoCurve;
                result.Refusal = refusal;
                return result;
            }

            result.CurveMin = curve.MinEnergy;
            result.CurveMax = curve.MaxEnergy;
            if (curve.IsPerUnitFluence)
            {
                // (`AMBER34`) см² на единичный флюенс: K = 100/(A·I) дал бы
                // число в 1/(с·см²) под именем беккерелей — отказ словами.
                result.Problem = LineProblem.FieldCurve;
                result.Refusal = curve.Name ?? "";
                return result;
            }

            double eps, errorPercent;
            if (!curve.TryEval(energyKev, out eps, out errorPercent))
            {
                result.Problem = LineProblem.OutOfRange;
                return result;
            }

            if (!(eps > 0.0))
            {
                result.Problem = LineProblem.NoEpsilon;
                return result;
            }

            result.Value = 100.0 / (eps * intensityPercent);
            result.Error = result.Value * errorPercent / 100.0;
            return result;
        }

        /// <summary>
        /// Прежний вход: то же, что <see cref="ForLine"/>, но причина отказа
        /// теряется. Оставлен для проб; в приложении читатель у него один —
        /// панель выделения — и она переведена на <see cref="ForLine"/>.
        /// </summary>
        public static bool TryForLine(double energyKev, double intensityPercent,
                                      EfficiencyConfigData efficiency,
                                      out double value, out double error)
        {
            LineResult result = ForLine(energyKev, intensityPercent, efficiency);
            value = result.Value;
            error = result.Error;
            return result.Ok;
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
                Refused = false,
                StatusText = null,
            };

            // ⛔ Ручной режим (галочка снята) — кривую не спрашиваем ВОВСЕ:
            // решение Amber 15.09.2026 «явно ручной режим сохранить», и это
            // касается и кривой сцены поля — сохранённый K действует побитово.
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

            string refusal;
            FsaEfficiency curve = FsaEfficiency.FromConfig(efficiency, out refusal);
            if (curve == null)
            {
                // (`AMBER34`) Отвергнутая кривая ведёт себя как «кривой нет»
                // (сохранённый K), но причина названа — с точкой и энергией.
                result.Problem = refusal != null
                    ? string.Format(CultureInfo.InvariantCulture, Resources.BqCoeffCurveRefused, refusal)
                    : Resources.BqCoeffNoCurve;
                return result;
            }

            if (curve.IsPerUnitFluence)
            {
                // (`AMBER34`) Кривая сцены поля: см² на единичный флюенс. Не
                // откат на сохранённое число, а ОТКАЗ — активность скрыта.
                result.Value = 0.0;
                result.Error = 0.0;
                result.From = Source.Refused;
                result.Refused = true;
                result.Problem = string.Format(CultureInfo.InvariantCulture, Resources.BqCoeffFieldCurve,
                                               curve.Name ?? "");
                result.StatusText = Resources.ResultFieldCurve;
                return result;
            }

            double eps, errorPercent;
            if (!curve.TryEval(roi.PeakEnergy, out eps, out errorPercent))
            {
                result.Problem = string.Format(CultureInfo.InvariantCulture, Resources.BqCoeffOutOfRange,
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
