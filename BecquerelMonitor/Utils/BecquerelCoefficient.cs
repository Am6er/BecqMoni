using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BecquerelMonitor.EfficiencyMaker;
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

        // ==================================================================
        // Каскадное суммирование у зоны и у выделения (`AMBER133`, П167)
        // ==================================================================

        /// <summary>
        /// Ответ <see cref="Summing"/>: множитель к K и приписка к числу.
        /// </summary>
        public struct SummingResult
        {
            /// <summary>
            /// Множитель к K (A_ист = cps·K·Factor). Единица, когда поправка не
            /// применена.
            /// </summary>
            public double Factor;

            /// <summary>Поправка посчитана и применена.</summary>
            public bool Applied;

            /// <summary>CF самой линии по `FsaCascadeSummer` (вынос и влёт точных сумм).</summary>
            public double LineCf;

            /// <summary>
            /// Чужие сумм-пики в окне, к прямой площади линии в том же окне
            /// (доля, не проценты): то, чего у FSA нет, потому что у FSA нет окна.
            /// </summary>
            public double WindowSumShare;

            /// <summary>Нуклид, по чьей схеме посчитано (`nucid`); null — не распознан.</summary>
            public string Nuclide;

            /// <summary>
            /// Короткая приписка к числу (клетка таблицы зон); null — приписывать
            /// нечего: поправки нет, и у нуклида нет каскада с этой линией.
            /// </summary>
            public string Note;

            /// <summary>
            /// Полная фраза: что применено или ПОЧЕМУ не применено (подсказка
            /// клетки, строка панели выделения); null — сказать нечего.
            /// </summary>
            public string Problem;
        }

        /// <summary>
        /// ⛔ (`AMBER133`, П167 28.09.2026) ПОПРАВКА НА КАСКАДНОЕ СУММИРОВАНИЕ У
        /// ЗОНЫ ROI И У ВЫДЕЛЕНИЯ — ТЕМ ЖЕ СУММИРОВАТЕЛЕМ, ЧТО У FSA.
        ///
        /// Прежде K = 100/(ε·I) не знал о суммировании вовсе, а FSA ту же линию
        /// поправлял (`FsaCascadeSummer`, образ × 1/CF): по одной линии на
        /// одном экране два числа расходились до −32 % вплотную и +25 % у
        /// Ba-133 384 кэВ на 5 см (ревизия П163, `roi_mc.py` по схеме DDEP).
        ///
        /// ГДЕ ИСТИНА. Суммирователь строится ВЫЗОВОМ
        /// <see cref="FsaCascadeSummer.Create(ResponseMatrix, string, double, bool, bool, bool, bool)"/>
        /// на матрице отклика ТОЙ ЖЕ кривой эффективности, по которой считан K
        /// (склад `ResponseMatrixStore` по `Guid` кривой, отпечаток обязан
        /// сойтись с геометрией — ровно как у FSA и у дозы), с ключами,
        /// которые ставит FSA: настройки пользователя
        /// (<see cref="FsaCalculationOptions.Of"/>), привязка матрицы
        /// (<see cref="FsaMatrixBinding.Bind"/>: вещество кристалла, Q_k сцены),
        /// окно совпадения — мёртвое время прибора, прочие рычаги — умолчания
        /// нового <see cref="FsaAnalyzer"/>. Совпадение с суммирователем самого
        /// FSA — побитово, проверяет проба `RoiSummingProbeP167` (контроль К1).
        ///
        /// ЧТО ДОБАВЛЕНО ПОВЕРХ CF. У FSA окна нет: сумм-пики, не попавшие ни в
        /// одну линию, он ставит своими образами. Зона же собирает в своё окно
        /// ВСЁ — и чужие суммы тоже (Ba-133 384 на 5 см: 356 + Kx при
        /// 387–391 кэВ). Поэтому
        ///
        ///     Factor = w_z / ( w_z/CF + Σ_s A_s·w_s / D_z ),
        ///
        /// где D_z = I·ε_p(E) — прямая площадь линии на распад, A_s — площади
        /// сумм-пиков нуклида (`Correction.SumPeaks`, уже с обеими
        /// эффективностями), w — доля гауссова пика в окне [lo, hi] при ПШПВ
        /// спектра (калибровка ПШПВ; нет её — ПШПВ геометрии по √E).
        ///
        /// ⚠ ГРАНИЦЫ. Ручной K (галочка снята) сюда не идёт: число, снятое
        /// образцовым источником в той же геометрии, суммирование уже несёт,
        /// и поправлять его второй раз значило бы испортить; решение Amber
        /// 15.09.2026 «явно ручной режим сохранить». Сумм-континуум (частичное
        /// поглощение третьего кванта) — подложка, её снимает метод зоны.
        /// Прочие прямые линии нуклида в окне — не суммирование, их K не знал
        /// и прежде.
        ///
        /// ⛔ ОТКАЗ ВСЛУХ. Поправки нет — Factor = 1, и причина названа
        /// (<see cref="SummingResult.Problem"/>); приписка к числу
        /// (<see cref="SummingResult.Note"/>) ставится, когда нуклид узнан и
        /// линия стоит в его каскаде (таблица пар поставки), — у Cs-137 662 или
        /// K-40 приписывать нечего.
        /// </summary>
        /// <param name="label">Имя зоны или подпись линии: нуклид берётся из
        /// него (весь текст, затем слова по пробелам, скобкам, «/», «,», «+»),
        /// и годен тот, у кого эта линия есть в таблицах каскадов.</param>
        public static SummingResult Summing(string label, double energyKev, double intensityPercent,
                                            double lowerKev, double upperKev, ResultData resultData)
        {
            SummingResult result = new SummingResult { Factor = 1.0, LineCf = 1.0 };
            if (resultData == null || !(energyKev > 0.0) || !(intensityPercent > 0.0))
            {
                return result;
            }

            List<string> candidates = NuclideCandidates(label);
            string why;
            GeometryModel geometry;
            FsaCascadeSummer summer = SummerFor(resultData, out geometry, out why);
            if (summer == null)
            {
                result.Problem = string.Format(CultureInfo.InvariantCulture, Resources.SummingPanelNone, why);
                if (InCascade(candidates, energyKev))
                {
                    result.Note = Resources.SummingCellNone;
                }

                return result;
            }

            string nuclide = null;
            foreach (string candidate in candidates)
            {
                if (summer.HasLine(candidate, energyKev))
                {
                    nuclide = candidate;
                    break;
                }
            }

            if (nuclide == null)
            {
                why = string.Format(CultureInfo.InvariantCulture, Resources.SummingWhyNoLine,
                                    energyKev.ToString("0.###", CultureInfo.InvariantCulture), label ?? "");
                result.Problem = string.Format(CultureInfo.InvariantCulture, Resources.SummingPanelNone, why);
                result.Note = Resources.SummingCellNone;
                return result;
            }

            result.Nuclide = nuclide;
            FsaCascadeSummer.Correction correction = CorrectionFor(summer, nuclide, energyKev, intensityPercent);
            double cf = 1.0;
            if (correction != null && correction.Notes != null)
            {
                double best = double.MaxValue;
                foreach (FsaCascadeSummer.LineNote note in correction.Notes)
                {
                    double delta = Math.Abs(note.EnergyKev - energyKev);
                    if (note.Cf > 0.0 && delta < best)
                    {
                        best = delta;
                        cf = note.Cf;
                    }
                }
            }

            double direct = intensityPercent / 100.0 * summer.PeakEfficiency(energyKev);
            double lo = Math.Min(lowerKev, upperKev);
            double hi = Math.Max(lowerKev, upperKev);
            double wLine = WindowShare(energyKev, lo, hi, resultData, geometry);
            double foreign = 0.0;
            if (correction != null && correction.SumPeaks != null)
            {
                foreach (FsaCascadeSummer.SumPeak peak in correction.SumPeaks)
                {
                    if (peak.Area > 0.0)
                    {
                        foreign += peak.Area * WindowShare(peak.Energy, lo, hi, resultData, geometry);
                    }
                }
            }

            double share = direct > 0.0 && wLine > 0.0 ? foreign / (direct * wLine) : 0.0;
            double denominator = 1.0 / cf + share;
            if (!(direct > 0.0) || !(wLine > 0.0) || !(denominator > 0.0)
                || double.IsNaN(denominator) || double.IsInfinity(denominator))
            {
                why = string.Format(CultureInfo.InvariantCulture, Resources.SummingWhyNoLine,
                                    energyKev.ToString("0.###", CultureInfo.InvariantCulture), label ?? "");
                result.Problem = string.Format(CultureInfo.InvariantCulture, Resources.SummingPanelNone, why);
                result.Note = Resources.SummingCellNone;
                return result;
            }

            result.LineCf = cf;
            result.WindowSumShare = share;
            result.Factor = 1.0 / denominator;
            result.Applied = true;
            string factor = result.Factor.ToString("F3", CultureInfo.InvariantCulture);
            result.Note = string.Format(CultureInfo.InvariantCulture, Resources.SummingCellApplied, factor);
            result.Problem = string.Format(CultureInfo.InvariantCulture, Resources.SummingPanelApplied, factor,
                                           cf.ToString("F3", CultureInfo.InvariantCulture),
                                           (share * 100.0).ToString("F1", CultureInfo.InvariantCulture));
            return result;
        }

        /// <summary>
        /// (`AMBER133`) Суммирование для зоны: только K ПО КРИВОЙ; ручной K и
        /// запасное число — без поправки и без приписки (побитово прежнее).
        /// </summary>
        public static SummingResult SummingForZone(ROIDefinitionData roi, Result coefficient, ResultData resultData)
        {
            if (roi == null || coefficient.From != Source.Efficiency)
            {
                return new SummingResult { Factor = 1.0, LineCf = 1.0 };
            }

            return Summing(roi.Name, roi.PeakEnergy, roi.Intencity, roi.LowerLimit, roi.UpperLimit, resultData);
        }

        // ==================================================================
        // Помеха природного спутника у зоны (`S199`, П174)
        // ==================================================================

        static readonly Dictionary<string, FsaLineInterference> InterferenceCache =
            new Dictionary<string, FsaLineInterference>();

        /// <summary>
        /// ⛔ (`S199`, П174 28.09.2026; решение Amber 28.09.2026 «Строка AMBER:
        /// предупреждение об интерференции 186 кэВ» — у FSA И у зоны ROI) ЗОНА,
        /// В ОКНО КОТОРОЙ ЛОЖАТСЯ ЛИНИИ ПРИРОДНОГО СПУТНИКА ЕЁ НУКЛИДА. Типичный
        /// случай — Ra-226 по 186.2 кэВ при природном уране: U-235 даёт 185.7 кэВ,
        /// и число зоны завышено до ×1.7. Правило — у разбора
        /// (<see cref="FsaSampleLibrary.ZoneCompanionInterference"/>, спутник по
        /// базе, имён в коде нет); здесь — нуклид из имени зоны (как у
        /// суммирования: весь текст, затем слова), окно зоны с долей гауссова
        /// пика при ПШПВ спектра и эффективность кривой (нет кривой — единица:
        /// в узком окне она почти постоянна). Число зоны НЕ правится — о помехе
        /// говорится (~~`S110`~~ в силе); null — сказать нечего.
        /// </summary>
        public static FsaLineInterference InterferenceForZone(ROIDefinitionData roi, ResultData resultData)
        {
            if (roi == null || resultData == null)
            {
                return null;
            }

            double lo = Math.Min(roi.LowerLimit, roi.UpperLimit);
            double hi = Math.Max(roi.LowerLimit, roi.UpperLimit);
            if (!(hi > lo))
            {
                return null;
            }

            EfficiencyConfigData efficiency = resultData.Efficiency;
            GeometryModel geometry = efficiency != null && efficiency.HasGeometry ? efficiency.Geometry : null;
            double fwhm = FwhmKev(0.5 * (lo + hi), resultData, geometry);
            string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1:R}|{2:R}|{3}|{4:F4}",
                                       roi.Name ?? "", lo, hi,
                                       efficiency != null ? (efficiency.Guid ?? "") : "-", fwhm);
            lock (InterferenceCache)
            {
                FsaLineInterference ready;
                if (InterferenceCache.TryGetValue(key, out ready))
                {
                    return ready;
                }
            }

            Func<double, double> eff = null;
            if (efficiency != null)
            {
                string refusal;
                FsaEfficiency curve = FsaEfficiency.FromConfig(efficiency, out refusal);
                double probe, probeError;
                // Кривая, не отвечающая в середине окна, не взвешивает ничего:
                // смешать «эффективность» с единицей у соседних линий нельзя.
                if (curve != null && curve.TryEval(0.5 * (lo + hi), out probe, out probeError) && probe > 0.0)
                {
                    eff = energy =>
                    {
                        double eps, errorPercent;
                        return curve.TryEval(energy, out eps, out errorPercent) ? eps : probe;
                    };
                }
            }

            Func<double, double> share = energy => WindowShare(energy, lo, hi, resultData, geometry);
            FsaLineInterference found = null;
            foreach (string candidate in NuclideCandidates(roi.Name))
            {
                found = FsaSampleLibrary.ZoneCompanionInterference(FsaSampleLibrary.NucidOf(candidate), lo, hi,
                                                                   share, eff);
                if (found != null)
                {
                    break;
                }
            }

            lock (InterferenceCache)
            {
                if (InterferenceCache.Count > 512)
                {
                    InterferenceCache.Clear();
                }

                InterferenceCache[key] = found;
            }

            return found;
        }

        /// <summary>Доля гауссова пика энергии E в окне [lo, hi].</summary>
        static double WindowShare(double energyKev, double lo, double hi, ResultData resultData, GeometryModel geometry)
        {
            double fwhm = FwhmKev(energyKev, resultData, geometry);
            if (!(fwhm > 0.0))
            {
                return energyKev >= lo && energyKev <= hi ? 1.0 : 0.0;
            }

            double sigma = fwhm / (2.0 * Math.Sqrt(2.0 * Math.Log(2.0)));
            double a = (lo - energyKev) / (sigma * Math.Sqrt(2.0));
            double b = (hi - energyKev) / (sigma * Math.Sqrt(2.0));
            return 0.5 * (Erf(b) - Erf(a));
        }

        /// <summary>
        /// ПШПВ в кэВ: калибровка ПШПВ спектра (ширина в каналах × шаг шкалы),
        /// а без неё — ПШПВ геометрии законом <see cref="GeometryModel.PeakHalfWidthKev"/>.
        /// Ни того, ни другого — ноль (окно судится центром пика).
        /// </summary>
        static double FwhmKev(double energyKev, ResultData resultData, GeometryModel geometry)
        {
            try
            {
                EnergySpectrum spectrum = resultData.EnergySpectrum;
                FwhmCalibration fwhm = resultData.FwhmCalibration;
                if (fwhm != null && !fwhm.NotCalibrated() && spectrum != null && spectrum.EnergyCalibration != null)
                {
                    EnergyCalibration cal = spectrum.EnergyCalibration;
                    double channel = PolynomialEnergyCalibration.ChannelOf(cal, energyKev, spectrum.NumberOfChannels);
                    double widthChannels = fwhm.ChannelToFwhm(channel);
                    double step = cal.ChannelToEnergy(channel + 0.5) - cal.ChannelToEnergy(channel - 0.5);
                    double kev = widthChannels * step;
                    if (kev > 0.0 && !double.IsNaN(kev) && !double.IsInfinity(kev))
                    {
                        return kev;
                    }
                }
            }
            catch (Exception)
            {
                // калибровка ПШПВ негодна — запасной путь ниже
            }

            // (`AMBER201`, Р4, мелочь 9.1) Запасной путь — ТЕМ ЖЕ законом, что
            // допуск пика кривой: ПШПВ(E) = ПШПВ(662)·(E/662)^p, p =
            // `GeometryModel.FwhmPowerLaw` (0.6, `AMBER135`). Прежде здесь жила
            // вторая копия закона с корнем (p = ½), не поднятая вместе с первой:
            // у спектра без ПШПВ-калибровки окно зоны считало пик на 60 кэВ в
            // 1.27 раза шире, чем кривая, — и долю линии в зоне, и чужие
            // суммарные пики в ней. Один закон — одно место (`S37`).
            if (geometry != null && geometry.FwhmAt662Percent > 0.0)
            {
                return 2.0 * geometry.PeakHalfWidthKev(energyKev);
            }

            return 0.0;
        }

        /// <summary>erf по Абрамовицу–Стигану 7.1.26 (|ошибка| &lt; 1.5e-7).</summary>
        static double Erf(double x)
        {
            double sign = x < 0.0 ? -1.0 : 1.0;
            x = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.3275911 * x);
            double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t
                              + 0.254829592) * t * Math.Exp(-x * x);
            return sign * y;
        }

        /// <summary>Кандидаты в нуклид из имени зоны: весь текст, затем слова.</summary>
        static List<string> NuclideCandidates(string label)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(label))
            {
                return result;
            }

            var names = new List<string> { label.Trim() };
            names.AddRange(label.Split(new[] { ' ', '(', ')', '/', ',', '+', ';', '\t' },
                                       StringSplitOptions.RemoveEmptyEntries));
            foreach (string name in names)
            {
                string key = FsaCascadeSummer.ParentKey(name);
                if (key != null && FsaSampleLibrary.NucidOf(name).Length > 0 && !result.Contains(name))
                {
                    result.Add(name);
                }
            }

            return result;
        }

        /// <summary>
        /// Стоит ли линия в каскаде кого-то из кандидатов — по таблице пар
        /// поставки (<see cref="FsaCascadeSummer.PairTable"/>), без матрицы.
        /// Нужна для приписки «без поправки»: у Cs-137 662 её быть не должно.
        /// </summary>
        static bool InCascade(List<string> candidates, double energyKev)
        {
            foreach (string candidate in candidates)
            {
                List<double[]> pairs;
                try
                {
                    pairs = FsaCascadeSummer.PairTable(candidate);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (double[] pair in pairs)
                {
                    if (Math.Abs(pair[0] - energyKev) < CascadeLineKev || Math.Abs(pair[1] - energyKev) < CascadeLineKev)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Допуск линии зоны к линии таблицы пар, кэВ (округления справочников).</summary>
        const double CascadeLineKev = 0.6;

        static readonly object SummerGate = new object();
        static FsaCascadeSummer cachedSummer;
        static GeometryModel cachedGeometry;
        static EfficiencyConfigData cachedEfficiency;
        static string cachedStamp;
        static string cachedWhy;
        static readonly Dictionary<string, FsaCascadeSummer.Correction> CorrectionCache =
            new Dictionary<string, FsaCascadeSummer.Correction>();

        static FsaCascadeSummer.Correction CorrectionFor(FsaCascadeSummer summer, string nuclide,
                                                         double energyKev, double intensityPercent)
        {
            string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1:R}|{2:R}", nuclide, energyKev, intensityPercent);
            lock (SummerGate)
            {
                FsaCascadeSummer.Correction ready;
                if (object.ReferenceEquals(summer, cachedSummer) && CorrectionCache.TryGetValue(key, out ready))
                {
                    return ready;
                }
            }

            var component = new FsaComponent(nuclide, FsaComponentKind.Single);
            component.Lines.Add(new FsaLine(nuclide, energyKev, intensityPercent));
            FsaCascadeSummer.Correction correction = summer.For(component);
            lock (SummerGate)
            {
                if (object.ReferenceEquals(summer, cachedSummer))
                {
                    CorrectionCache[key] = correction;
                }
            }

            return correction;
        }

        /// <summary>
        /// Суммирователь для кривой спектра; null — причина в <paramref name="why"/>.
        /// Один на кривую, матрицу, настройки и окно совпадения: зоны
        /// пересчитываются дважды в секунду.
        /// </summary>
        static FsaCascadeSummer SummerFor(ResultData resultData, out GeometryModel geometry, out string why)
        {
            geometry = null;
            why = null;
            EfficiencyConfigData efficiency = resultData.Efficiency;
            if (efficiency == null)
            {
                why = Resources.SummingWhyNoCurve;
                return null;
            }

            if (!efficiency.HasGeometry)
            {
                why = Resources.SummingWhyNoGeometry;
                return null;
            }

            if (!efficiency.UseResponseMatrix)
            {
                why = Resources.SummingWhyMatrixOff;
                return null;
            }

            FsaCalculationOptions options = FsaCalculationOptions.Of(resultData);
            if (!options.CascadeSumming)
            {
                why = Resources.SummingWhyOff;
                return null;
            }

            double deadTime = DeadTimeOf(resultData);
            string stamp = StampOf(efficiency, options, deadTime);
            lock (SummerGate)
            {
                if (object.ReferenceEquals(cachedEfficiency, efficiency) && cachedStamp == stamp)
                {
                    geometry = cachedGeometry;
                    why = cachedWhy;
                    return cachedSummer;
                }
            }

            FsaCascadeSummer summer = null;
            geometry = efficiency.Geometry;
            try
            {
                // (`AMBER202`) общим путём читателей: склад, а нет на нём
                // годной — матрица, приехавшая в файле спектра
                MatrixRefusal refusal;
                int fileFormat;
                ResponseMatrixSource source;
                string spectrumRefusal;
                ResponseMatrix matrix = ResponseMatrixStore.Resolve(efficiency, out refusal, out fileFormat,
                                                                    out source, out spectrumRefusal);
                if (matrix == null)
                {
                    why = string.Format(CultureInfo.InvariantCulture, Resources.SummingWhyNoMatrix,
                                        !string.IsNullOrEmpty(spectrumRefusal)
                                            ? spectrumRefusal
                                            : refusal == MatrixRefusal.OldFormat
                                            ? string.Format(CultureInfo.InvariantCulture, "format {0} < {1}",
                                                            fileFormat, ResponseMatrix.FormatVersion)
                                            : refusal.ToString());
                }
                else if (!matrix.IsValidFor(efficiency.Geometry))
                {
                    why = Resources.SummingWhyMatrixStale;
                }
                else if (matrix.Normalization == ResponseMatrixNormalization.PerUnitFluence)
                {
                    why = Resources.SummingWhyFieldScene;
                }
                else
                {
                    summer = CreateLikeFsa(matrix, efficiency.Geometry, options, deadTime);
                    if (summer == null)
                    {
                        why = string.Format(CultureInfo.InvariantCulture, Resources.SummingWhyNoMatrix,
                                            "nucdb.sqlite / channels");
                    }
                }
            }
            catch (Exception ex)
            {
                summer = null;
                why = string.Format(CultureInfo.InvariantCulture, Resources.SummingWhyNoMatrix, ex.Message);
            }

            lock (SummerGate)
            {
                cachedEfficiency = efficiency;
                cachedStamp = stamp;
                cachedSummer = summer;
                cachedGeometry = geometry;
                cachedWhy = why;
                CorrectionCache.Clear();
            }

            return summer;
        }

        /// <summary>
        /// (`AMBER133`) Суммирователь С ТЕМИ ЖЕ КЛЮЧАМИ, ЧТО СТАВИТ FSA
        /// (`FsaAnalyzer.Analyze`, блок «Каскадные поправки»): новый
        /// <see cref="FsaAnalyzer"/> несёт умолчания рычагов, настройки
        /// пользователя кладёт <see cref="FsaCalculationOptions.ApplyTo(FsaAnalyzer)"/>,
        /// матрицу с веществом и Q_k — <see cref="FsaMatrixBinding.Bind"/>,
        /// окно — мёртвое время прибора. Сам суммирователь и его ключи
        /// собирает <see cref="FsaAnalyzer.CreateCascadeSummer"/> — то же место,
        /// что зовёт разбор (П168 28.09.2026): второй копии блока настроек
        /// здесь больше нет, и новый ключ суммирователя доезжает сюда сам.
        /// Контроль — проба `RoiSummingProbeP167` (К1: CF этого пути против
        /// суммирователя самого FSA после разбора, побитово).
        /// </summary>
        public static FsaCascadeSummer CreateLikeFsa(ResponseMatrix matrix, GeometryModel geometry,
                                                     FsaCalculationOptions options, double deadTimeSec)
        {
            var analyzer = new FsaAnalyzer();
            (options ?? new FsaCalculationOptions()).ApplyTo(analyzer);
            FsaMatrixBinding.Bind(analyzer, geometry, matrix);
            analyzer.CoincidenceWindowSec = deadTimeSec > 0.0 ? deadTimeSec : 0.0;
            return analyzer.CreateCascadeSummer();
        }

        static double DeadTimeOf(ResultData resultData)
        {
            try
            {
                if (resultData.DeviceConfig == null || resultData.DeviceConfig.InputDeviceConfig == null)
                {
                    return 0.0;
                }

                double deadTime = resultData.DeviceConfig.InputDeviceConfig.DeadTime();
                return deadTime > 0.0 ? deadTime : 0.0;
            }
            catch (Exception)
            {
                return 0.0;
            }
        }

        static string StampOf(EfficiencyConfigData efficiency, FsaCalculationOptions options, double deadTime)
        {
            // (`AMBER202`) склад и блок из файла спектра — общей отметкой
            string file = ResponseMatrixStore.SourceStamp(efficiency);

            return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3:R}|{4}",
                                 file, efficiency.Guid, options.Stamp, deadTime,
                                 efficiency.LastUpdated.Ticks);
        }
    }
}
