using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Строки окна «Measurement Result» из разбора FSA (`AMBER208`, задача
    /// Amber 06.10.2026: «форму ROI убрать, FSA считает активность и выводит её
    /// в окно Measurement Result»). Решения Amber вопросником 06.10.2026:
    /// строки — «Из сета нуклидов»; беккерели — «Матрица или абсолютная
    /// кривая», иначе скрыть с причиной; ряд — «Считать строку родителя
    /// связанным рядом»; причина отсутствия матрицы — только в отчёте
    /// («Убрать причину из окна»).
    ///
    /// Коллекция строится в форме, которую читают
    /// <see cref="MeasurementResultManager.Translate"/> (Бк, Бк/кг, Бк/л) и
    /// <see cref="MeasurementResultManager.Correct"/> (приведение к дате отбора):
    ///   * <c>ResultValue</c> — амплитуда компонента за время счёта
    ///     (<c>CountRate · T</c>): <c>Translate</c> делит на то же <c>T</c>;
    ///   * K строки = 1 — амплитуда разбора уже в распадах в секунду в шкале
    ///     кривой/матрицы, то есть Бк при абсолютной шкале;
    ///   * K = 0, когда шкала не абсолютная — <c>Translate</c> отвечает
    ///     «нет коэффициента» на все три беккерельных перевода.
    ///
    /// Тексты для человека сюда не зашиты: окно подаёт их из своих ресурсов
    /// (<see cref="Texts"/>), проба — свои. Погрешность — амплитуда / Z
    /// компонента (NNLS), у необнаруженных — порог решения / k; предел — a#
    /// ISO 11929 (Xu-2022), а не Карри по боковым каналам.
    /// </summary>
    public static class FsaMeasurementResult
    {
        /// <summary>Ключ источника коллекции — окно сравнивает по нему «та же ли коллекция».</summary>
        public const string ConfigGuid = "fsa-measurement-result";

        /// <summary>Квантиль ISO 11929 при α = β = 5 %, та же, что у разбора.</summary>
        const double LimitQuantileK = 1.6449;

        /// <summary>Почему беккерели скрыты.</summary>
        public enum HiddenReason
        {
            None,
            NoResult,
            FieldCurve,
            NoCurve,
            CurveUnused,
            NonAbsoluteOrigin
        }

        /// <summary>Что надо знать читающему числа — окно складывает из этого заголовок.</summary>
        public sealed class Summary
        {
            public bool Absolute;
            public bool MatrixUsed;
            public EfficiencyOrigin Origin;
            public HiddenReason Reason;
            /// <summary>Строк всего — обнаруженные и «не обнаружен» с пределом.</summary>
            public int Rows;
            /// <summary>(`AMBER211`) Строк «не обнаружен» — кандидаты отчёта с пределом обнаружения.</summary>
            public int Undetected;
            /// <summary>(`AMBER211`) Строк связанного ряда — одна амплитуда на весь ряд, строка родителя.</summary>
            public int Chains;

            public string Describe(Texts texts)
            {
                texts = texts ?? Texts.Russian;
                string head;
                if (this.Absolute)
                {
                    // (`AMBER208` (в), решение Amber 06.10.2026 «Убрать причину из окна»)
                    // почему матрицы нет — только в отчёте FSA (строка «Response matrix»
                    // и причина с лечением); заголовок колонки для этого мал
                    head = this.MatrixUsed
                        ? texts.ScaleMatrix
                        : string.Format(CultureInfo.InvariantCulture, texts.ScaleCurve, texts.NameOf(this.Origin));
                }
                else
                {
                    string reason;
                    switch (this.Reason)
                    {
                        case HiddenReason.FieldCurve: reason = texts.ReasonFieldCurve; break;
                        case HiddenReason.NoCurve: reason = texts.ReasonNoCurve; break;
                        case HiddenReason.CurveUnused: reason = texts.ReasonCurveUnused; break;
                        case HiddenReason.NonAbsoluteOrigin:
                            reason = string.Format(CultureInfo.InvariantCulture, texts.ReasonOrigin, texts.NameOf(this.Origin)); break;
                        default: reason = texts.ReasonNoResult; break;
                    }
                    head = string.Format(CultureInfo.InvariantCulture, texts.Hidden, reason);
                }
                return head;
            }

            public string Details(Texts texts)
            {
                texts = texts ?? Texts.Russian;
                return string.Format(CultureInfo.InvariantCulture, texts.Details, this.Rows, this.Undetected, this.Chains);
            }
        }

        /// <summary>Тексты строк и заголовка — окно берёт их из своих ресурсов, проба — отсюда.</summary>
        public sealed class Texts
        {
            public string ScaleMatrix = "FSA: Бк по матрице отклика";
            public string ScaleCurve = "FSA: Бк по кривой {0}";
            public string Hidden = "FSA: Бк скрыты — {0}";
            public string ReasonFieldCurve = "кривая сцены поля (см² на единичный флюенс)";
            public string ReasonNoCurve = "кривой эффективности нет";
            public string ReasonCurveUnused = "кривая разбором не учтена";
            public string ReasonOrigin = "кривая без абсолютного уровня ({0})";
            public string ReasonNoResult = "разбора нет";
            public string Details = "строк {0}, не обнаружено {1}, связанных рядов {2}";

            /// <summary>
            /// (П254) Название происхождения кривой для заголовка и причины — из ресурсов
            /// окна; null или пустой ответ — имя члена перечисления, как до П254 (тогда в
            /// русском окне стояло «Бк по кривой Simulation»).
            /// </summary>
            public Func<EfficiencyOrigin, string> OriginName;

            public string NameOf(EfficiencyOrigin origin)
            {
                string name = this.OriginName != null ? this.OriginName(origin) : null;
                return string.IsNullOrEmpty(name) ? origin.ToString() : name;
            }

            public static readonly Texts Russian = new Texts();
        }

        /// <summary>
        /// Абсолютна ли шкала разбора — можно ли печатать беккерели.
        /// Матрица отклика — всегда (считана из геометрии); кривая — только
        /// из геометрии или ЛСРМ; кривая сцены поля — никогда (`AMBER34`).
        /// </summary>
        public static bool AbsoluteScale(ResultData rd, FsaResult result, out HiddenReason reason)
        {
            reason = HiddenReason.None;
            if (result == null)
            {
                reason = HiddenReason.NoResult;
                return false;
            }
            if (result.EfficiencyPerUnitFluence)
            {
                reason = HiddenReason.FieldCurve;
                return false;
            }
            if (result.ResponseMatrixUsed)
            {
                return true;
            }
            EfficiencyConfigData eff = rd != null ? rd.Efficiency : null;
            if (eff == null || !eff.HasCurve)
            {
                reason = HiddenReason.NoCurve;
                return false;
            }
            if (!result.EfficiencyUsed)
            {
                reason = HiddenReason.CurveUnused;
                return false;
            }
            if (eff.Origin == EfficiencyOrigin.Simulation || eff.Origin == EfficiencyOrigin.Lsrm)
            {
                return true;
            }
            reason = HiddenReason.NonAbsoluteOrigin;
            return false;
        }

        /// <summary>
        /// Метка ряда у определения: поле <see cref="NuclideDefinition.Chain"/>, а
        /// без него — хвост имени в скобках («Tl-208 (Th-232)» → «Th-232»): у
        /// части библиотек поле не заполнено, и без этого член ряда становился
        /// отдельным «родителем» с активностью ряда и без предела.
        /// </summary>
        static string ChainLabelOf(NuclideDefinition def)
        {
            string chain = (def.Chain ?? "").Trim();
            if (chain.Length > 0)
            {
                return chain;
            }
            string name = def.Name ?? "";
            int open = name.IndexOf('(');
            int close = name.IndexOf(')', open + 1);
            if (open >= 0 && close > open + 1)
            {
                string inner = name.Substring(open + 1, close - open - 1).Trim();
                if (FsaSampleLibrary.NucidOf(inner).Length > 0)
                {
                    return inner;
                }
            }
            return "";
        }

        /// <summary>
        /// Родители сета в порядке первого появления: корень ряда (метка
        /// <see cref="NuclideDefinition.Chain"/> или хвост имени), а без ряда —
        /// сам нуклид линии. Элементный рентген («Pb x-ray») не нуклид и
        /// строкой не становится. <paramref name="set"/> == null — все определения.
        /// </summary>
        public static List<KeyValuePair<string, NuclideDefinition>> ParentsOf(NuclideSet set, IList<NuclideDefinition> definitions)
        {
            var seen = new Dictionary<string, NuclideDefinition>(StringComparer.OrdinalIgnoreCase);
            var order = new List<KeyValuePair<string, NuclideDefinition>>();
            if (definitions == null)
            {
                return order;
            }
            foreach (NuclideDefinition def in definitions)
            {
                if (def == null || def.IsElementXray)
                {
                    continue;
                }
                if (set != null && (def.Sets == null || !def.Sets.Contains(set.Id)))
                {
                    continue;
                }
                string chain = ChainLabelOf(def);
                string key = chain.Length > 0 ? chain : (def.NuclideName ?? "").Trim();
                if (key.Length == 0)
                {
                    continue;
                }
                if (!seen.ContainsKey(key))
                {
                    seen.Add(key, def);
                    order.Add(new KeyValuePair<string, NuclideDefinition>(key, def));
                }
            }
            return order;
        }

        /// <summary>
        /// Объявленный состав из сета для <see cref="FsaSampleLibrary.Declared"/>:
        /// родитель с меткой ряда — ряд (<see cref="FsaSampleChain.FromLabel"/>),
        /// без ряда — одиночный `nucid`. Метка, которой ряд не строится, падает
        /// в одиночки по имени; имя, которого нет в базе, пропускается.
        /// </summary>
        public static void DeclaredOf(NuclideSet set, IList<NuclideDefinition> definitions,
                                      out List<FsaSampleChain> chains, out List<string> nuclides)
        {
            chains = new List<FsaSampleChain>();
            nuclides = new List<string>();
            var seenNucids = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, NuclideDefinition> parent in ParentsOf(set, definitions))
            {
                bool isChain = ChainLabelOf(parent.Value).Length > 0;
                string nucid = FsaSampleLibrary.NucidOf(parent.Key);
                if (nucid.Length == 0 || !seenNucids.Add(nucid))
                {
                    continue;
                }
                FsaSampleChain chain = isChain ? FsaSampleChain.FromLabel(parent.Key) : null;
                if (chain != null)
                {
                    chains.Add(chain);
                }
                else
                {
                    nuclides.Add(nucid);
                }
            }
        }

        /// <summary>
        /// (`AMBER208` → `AMBER211`, П238 07.10.2026) СТРОКИ ОКНА РЕЗУЛЬТАТА — ПО СПИСКУ ИЗОТОПОВ
        /// ОТЧЁТА FSA, а не по сету. Ответ Amber 07.10.2026 вопросником, дословно: «…все изотопы,
        /// которые были обнаружены FSA с настройками, которые сейчас работают в FSA отчёте берутся
        /// в работу для расчёта активности для окна Measurement Result. Если это изотопы имеющие
        /// родителя - то измеряется активность родителя. Если это изотопы не имеющие родителя
        /// (неравновесная история) - то считается активность каждого изотопа в отдельности.
        /// Другими словами - что мы видим в окне отчёта в списке изотопов - ту активность мы и
        /// считаем.» Прежние «Из сета нуклидов» и «Считать строку родителя связанным рядом»
        /// (06.10.2026) этим ответом для окна результата СНЯТЫ: сеанс один с отчётом
        /// («Окно результата читает сеанс документа»), и равновесие — его галочка.
        ///
        /// Правило. Обнаруженные компоненты состава (не приборные образы, амплитуда больше нуля):
        /// * член СВЯЗАННОГО ряда (<see cref="FsaComponentResult.ChainRoot"/> задан — одна амплитуда
        ///   на весь ряд) — строка РОДИТЕЛЯ, одна на ряд, скорость и значимость — ряда;
        /// * свободный нуклид (ряд рассыпан при выключенном равновесии, одиночка, или член,
        ///   привязанный к партнёру по <see cref="FsaComponentResult.TiedTo"/>) — своя строка,
        ///   своя скорость; имя — нуклида, как в отчёте.
        /// Не обнаруженные кандидаты отчёта (<see cref="FsaPresentationBuilder.UndetectedNamed"/>,
        /// те же, что отчёт печатает «&lt; доля») — строки «не обнаружен» с порогом и пределом
        /// (ISO 11929, `S9`), одна на имя (у ряда — имя корня). Период полураспада строки — по
        /// определению библиотеки с тем же именем (поправка на распад к отбору пробы,
        /// <c>MeasurementResultManager.Correct</c>), без него — из базы по `nucid`; у строки
        /// свободного члена ряда — период КОРНЯ (П278, <see cref="Line"/>).
        /// Коэффициент строки — единица при абсолютной шкале (матрица или кривая из геометрии /
        /// ЛСРМ, <see cref="AbsoluteScale"/>), иначе нуль — беккерели скрыты, причина в
        /// <see cref="Summary"/>.
        /// </summary>
        public static MeasurementResultCollection Build(ResultData rd, FsaResult result,
                                                        IList<NuclideDefinition> definitions, Texts texts,
                                                        out Summary summary)
        {
            texts = texts ?? Texts.Russian;
            summary = new Summary();
            if (rd == null || rd.EnergySpectrum == null || result == null)
            {
                summary.Reason = HiddenReason.NoResult;
                return null;
            }

            HiddenReason reason;
            bool absolute = AbsoluteScale(rd, result, out reason);
            summary.Absolute = absolute;
            summary.Reason = reason;
            summary.MatrixUsed = result.ResponseMatrixUsed;
            summary.Origin = rd.Efficiency != null ? rd.Efficiency.Origin : EfficiencyOrigin.Manual;

            var collection = new MeasurementResultCollection
            {
                ResultData = rd,
                SourceKey = ConfigGuid,
                MeasurementTime = rd.EnergySpectrum.MeasurementTime,
                LiveTime = rd.EnergySpectrum.LiveTime
            };
            // Время счёта — то же действующее живое время, которым Translate
            // делит отсчёты (Utils.LiveTime.Effective): умножение здесь и
            // деление там сокращаются, и в строке остаётся скорость разбора.
            double countingTime = collection.CountingTime;
            if (!(countingTime > 0.0))
            {
                countingTime = 1.0;
            }

            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Обнаруженные — в порядке состава; связанный ряд — одной строкой корня.
            if (result.Components != null)
            {
                foreach (FsaComponentResult component in result.Components)
                {
                    if (component == null || component.Kind == FsaComponentKind.Nuisance
                        || string.IsNullOrEmpty(component.Name) || !(component.CountRate > 0.0))
                    {
                        continue;
                    }

                    bool chain = !string.IsNullOrEmpty(component.ChainRoot);
                    string name = chain ? component.ChainRoot : component.Name;
                    if (!named.Add(name))
                    {
                        continue;
                    }

                    // ⛔ (П254, 14.10.2026) Амплитуда колонки — в распадах того, на что даны
                    // выходы её линий: у члена ряда — на распад КОРНЯ (доля ветвления уже в
                    // образе, и при выключенном равновесии тоже). Строка ряда — сам корень,
                    // ей это и нужно. Строка СВОБОДНОГО члена — «активность каждого изотопа
                    // в отдельности» (Amber 07.10.2026), то есть его собственные распады:
                    // амплитуда × доля (`FsaAnalysisSession.MemberActivityRatio`). Без неё
                    // Tl-208 на Th232_29.07.2022 при равновесии ВЫКЛ стоял 648.5 Бк — в
                    // беккерелях Bi-212, — а своих у него 233.1 (× 0.3594).
                    double own = chain ? 1.0 : FsaAnalysisSession.MemberActivityRatio(result, component.Name);
                    double rate = component.CountRate * own;
                    double sigma = component.Z > 0.0 ? rate / component.Z : rate;
                    // ⛔ (П254) Предел строки ряда — строки предела САМОЙ колонки ряда (имя
                    // ряда): у члена её нет (NaN), и прежде строка брала предел первого члена
                    // состава — верно, только если первым стоял корень со своими линиями в
                    // полосе. Ряд без линий корня (Ra-228: 13.5/16.2 кэВ) печатал MDA 0.
                    double limitRate = chain ? ChainLimitRate(result, component) : component.DetectionLimitRate;
                    double mda = limitRate > 0.0 && !double.IsNaN(limitRate) ? limitRate * own : 0.0;
                    // (П278) строка ряда — сам корень, её период свой; свободный член — период корня
                    string root = chain ? null : MemberRootOf(component.DecayChainRoot, component.Name, definitions);
                    collection.ResultList.Add(new MeasurementResult(Line(name, root, absolute, definitions),
                                                                    rate * countingTime, sigma * countingTime, mda * countingTime));
                    summary.Rows++;
                    if (chain)
                    {
                        summary.Chains++;
                    }
                }
            }

            // 2. Не обнаруженные кандидаты отчёта — с порогом и пределом (S9).
            foreach (FsaCharacteristicLimit limit in FsaPresentationBuilder.UndetectedNamed(result))
            {
                if (limit == null || string.IsNullOrEmpty(limit.Name) || !named.Add(limit.Name))
                {
                    continue;
                }

                // (П254) у не обнаруженного члена ряда — те же свои распады, что у обнаруженного
                double own = limit.Kind == FsaComponentKind.Chain ? 1.0 : FsaAnalysisSession.MemberActivityRatio(result, limit.Name);
                double rate = double.IsNaN(limit.CountRate) || limit.CountRate < 0.0 ? 0.0 : limit.CountRate * own;
                double sigma = !double.IsNaN(limit.DecisionThresholdRate) && limit.DecisionThresholdRate > 0.0
                    ? limit.DecisionThresholdRate * own / LimitQuantileK
                    : rate;
                double mda = !double.IsNaN(limit.DetectionLimitRate) && limit.DetectionLimitRate > 0.0
                    ? limit.DetectionLimitRate * own : 0.0;
                string root = limit.Kind == FsaComponentKind.Chain ? null : MemberRootOf(limit.DecayChainRoot, limit.Name, definitions);
                collection.ResultList.Add(new MeasurementResult(Line(limit.Name, root, absolute, definitions),
                                                                rate * countingTime, sigma * countingTime, mda * countingTime));
                summary.Rows++;
                summary.Undetected++;
            }

            return collection;
        }

        /// <summary>
        /// (П254) Предел обнаружения строки связанного ряда, имп/с: строка предела колонки ряда
        /// по имени ряда (<see cref="FsaComponentResult.ChainRoot"/>), а нет её — предел самого
        /// члена (колонка, которую почленно не разложили, названа именем ряда и так).
        /// </summary>
        static double ChainLimitRate(FsaResult result, FsaComponentResult member)
        {
            if (result.CharacteristicLimits != null)
            {
                foreach (FsaCharacteristicLimit limit in result.CharacteristicLimits)
                {
                    if (limit != null && string.Equals(limit.Name, member.ChainRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return limit.DetectionLimitRate;
                    }
                }
            }

            return member.DetectionLimitRate;
        }

        /// <summary>
        /// ⛔ (П278, 14.10.2026) Корень ряда СВОБОДНОГО члена — для периода поправки на распад
        /// (<see cref="Line"/>). Берётся из данных ряда, как признак члена у доли своих распадов
        /// (<c>FsaAnalysisSession.IsChainMember</c>): происхождение колонки или строки предела
        /// (<see cref="FsaComponentResult.DecayChainRoot"/>, путь из баз), а без него — ряд
        /// (<see cref="NuclideDefinition.Chain"/>) строки библиотеки того же нуклида с ДРУГИМ
        /// корнем (путь по подписям; у библиотеки Amber «Tl-208 (Th-232)»). null — корень сам,
        /// одиночка, не нуклид: период строки свой, как до П278.
        /// </summary>
        static string MemberRootOf(string origin, string name, IList<NuclideDefinition> definitions)
        {
            string nucid = FsaSampleLibrary.NucidOf(name ?? "");
            if (nucid.Length == 0)
            {
                return null;
            }

            string root = (origin ?? "").Trim();
            string rootNucid = FsaSampleLibrary.NucidOf(root);
            if (rootNucid.Length > 0)
            {
                return rootNucid != nucid ? root : null;
            }

            if (definitions != null)
            {
                foreach (NuclideDefinition d in definitions)
                {
                    if (d == null || FsaSampleLibrary.NucidOf(d.NuclideName ?? "") != nucid)
                    {
                        continue;
                    }

                    string chain = (d.Chain ?? "").Trim();
                    string chainNucid = FsaSampleLibrary.NucidOf(chain);
                    if (chainNucid.Length > 0 && chainNucid != nucid)
                    {
                        return chain;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Строка измерения по имени нуклида: коэффициент — единица при абсолютной шкале, период
        /// полураспада — по первому определению библиотеки с этим именем, а без него — из базы
        /// по `nucid` имени (нет и там — нуль, без поправки).
        ///
        /// ⛔ (П278) У строки СВОБОДНОГО члена ряда (<paramref name="root"/> задан — равновесие
        /// ВЫКЛ, у колонки есть корень) период — КОРНЯ. Решение Amber 14.10.2026 вопросником,
        /// дословно: «Периодом корня (Рекомендую)» (находка П254 №10). Прежде строка шла своим
        /// периодом, и на Th232_29.07.2022 (отбор за 51 мин до начала, набор 9.7 ч) при
        /// включённой поправке Tl-208 уезжал ×1.49·10⁷, Bi-212 ×12, Ac-228 ×1.81, а на Cs-137
        /// «в домике» (отбор за 4 сут) предел Bi-214 — ×2.3·10⁹⁰, Tl-208 — бесконечность. Приём
        /// тот же, что `AMBER195` «Ряд задан → период корня»: линия с рядом идёт через
        /// <see cref="NuclideDefinition.DecayHalfLifeYears"/> (база, затем линия корня в
        /// библиотеке, затем свой период). Следствие, названное в вопросе: торий и радий
        /// приводятся верно, радон на фильтре (неподпитанные дочерние) остаётся средним за набор.
        /// Одиночки и строка самого ряда (<paramref name="root"/> == null) — как прежде.
        /// </summary>
        static MeasurementLine Line(string name, string root, bool absolute, IList<NuclideDefinition> definitions)
        {
            NuclideDefinition def = null;
            if (definitions != null)
            {
                foreach (NuclideDefinition d in definitions)
                {
                    if (d != null && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        def = d;
                        break;
                    }
                }
            }

            double halfLifeYears = def != null ? NuclideDefinition.DecayHalfLifeYears(def, definitions) : 0.0;
            if (!(halfLifeYears > 0.0))
            {
                // (П239, 07.10.2026) имя строки — имя РАЗБОРА («Th-232», «Tl-208»), а библиотека
                // человека пишет как угодно («Th232», «Tl208 (Th232)»): без этой ветки точное
                // сравнение не находило определения, и поправка на распад к дате отбора тихо
                // становилась нулевой. Период — из базы по `nucid`, как у корня ряда в
                // NuclideDefinition.DecayHalfLifeYears.
                string nucid = FsaSampleLibrary.NucidOf(name);
                if (nucid.Length > 0)
                {
                    halfLifeYears = NucBase.NucBaseFramework.HalfLifeYearsOf(nucid);
                }
            }

            if (!string.IsNullOrEmpty(root))
            {
                // (П278) свободный член ряда — линией с рядом через правило `AMBER195`; не нашёлся
                // корень ни в базе, ни в библиотеке — правило вернёт свой период, найденный выше
                var member = new NuclideDefinition
                {
                    Name = name,
                    Chain = root,
                    HalfLife = halfLifeYears > 0.0 ? halfLifeYears : 0.0
                };
                halfLifeYears = NuclideDefinition.DecayHalfLifeYears(member, definitions);
            }

            return new MeasurementLine
            {
                Name = name,
                Coefficient = absolute ? 1.0 : 0.0,
                CoefficientError = 0.0,
                HalfLifeYears = halfLifeYears > 0.0 ? halfLifeYears : 0.0
            };
        }

    }
}
