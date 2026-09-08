using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// (`A145`, этап 2) ЧИСТЫЙ ПОСТРОИТЕЛЬ ПРЕДСТАВЛЕНИЯ: из готового
    /// <see cref="FsaResult"/> и положения «родители/дочерние» собирает
    /// ОДИН снимок (<see cref="FsaPresentation"/>) — слои для графика, цвета
    /// и строки отчёта для таблицы. Без вида, без <c>Graphics</c>, без окна:
    /// его читают и график (<c>EnergySpectrumView.Fsa.cs</c>), и будущее окно
    /// отчёта (этап 3), и пробы — ровно то же, что видит человек.
    ///
    /// Всё, что раньше жило в отрисовке легенды (<c>DrawFsaRows</c>), —
    /// подписи строк, доли, пределы, свёртки, порядок, текст качества, —
    /// переехало сюда дословно; правила `S9`, `S44`, `S51`, `S68`, `S69`,
    /// `S72`, `S85`, `S87`, `S104`, `S111`, `A28`, `A96` не изменены.
    /// </summary>
    public static class FsaPresentationBuilder
    {
        /// <summary>
        /// Цвет невязки ПЕРЕБОРА — «модель приписала лишнее»: ЧЁРНЫЙ (решение
        /// Amber 01.09.2026, `A28`; до того был тёмно-серый 64,64,64).
        ///
        /// Чёрный нарочно: он не встречается в палитре нуклидов
        /// (<see cref="FsaPalette"/>), и лента невязки не может быть принята за
        /// ещё один компонент состава. Он же стоит образцом в строке отчёта.
        ///
        /// ⛔ Вторая половина — недобор — цвета НЕ ИМЕЕТ здесь: она берёт цвет
        /// линии спектра из настроек человека (см. отрисовку ленты).
        /// </summary>
        public static readonly Color ResidualColor = Color.FromArgb(0, 0, 0);

        /// <summary>
        /// Наименьший суммарный выход излучений нуклида (γ и X на собственный
        /// распад, %), при котором кандидат ещё показывается СВОЕЙ строкой.
        ///
        /// ⛔ Величина НАЗНАЧЕНА Amber 18.08.2026 (`S69`), а не выведена
        /// разверткой по корпусу, — в отличие от порога `S57`. Кто станет
        /// «уточнять» её замером, пусть знает это заранее.
        ///
        /// Что она делает, посчитано по `nucdb` для обоих природных рядов:
        /// выбывают Rn-220 (0.114), Po-216 (0.0019), Po-212 (0), Rn-222 (0.079),
        /// Po-218 (0), Po-214 (0.010), Bi-210 (0), Po-210 (0.001) — ровно
        /// α-излучатели и эманации; остаются Th-232 (7.42), Ra-228 (3.83),
        /// Th-228 (10.11), Ra-224 (5.01), Ra-226 (5.28), Pb-210 (26.92),
        /// K-40 (11.75).
        ///
        /// ⚠ Th-232 держится в списке K-рентгеном: по одним гаммам у него
        /// 0.284 % и он выбыл бы. Потому и считается сумма по γ И ПО X — правило
        /// априорное, свойство нуклида, а не прибора. То, что этот рентген ниже
        /// порога большинства сцинтилляторов, — вопрос ДРУГОЙ и здесь не
        /// решается.
        ///
        /// ⛔ Порог — ТОЛЬКО НА ПОКАЗ. Колонка кандидата из фита не убирается:
        /// эманацией радона видно неравновесие, когда связка `S70` выключена, —
        /// и ровно ради этого случая свободные амплитуды и оставлены.
        /// </summary>
        public const double MinTotalYieldPercent = 1.0;

        /// <summary>Снимок представления для состояния БЕЗ результата: одна строка состояния.</summary>
        public static FsaPresentation OfStatus(string status)
        {
            var presentation = new FsaPresentation();
            if (!string.IsNullOrEmpty(status))
            {
                presentation.Rows.Add(new FsaReportRow
                {
                    Kind = FsaReportRowKind.Status,
                    Name = status,
                    Value = string.Empty
                });
            }

            return presentation;
        }

        /// <summary>
        /// Собрать представление. <paramref name="matrixOldFormat"/> —
        /// <see cref="FsaAnalysisSession.ResponseMatrixOldFormat"/> на момент
        /// сборки: признак живёт у сеанса, а не у результата, потому что
        /// матрица бракуется ДО разбора, и <c>result</c> о ней не знает.
        /// </summary>
        public static FsaPresentation Build(FsaResult result, FsaGrouping grouping, bool matrixOldFormat)
        {
            if (result == null)
            {
                return OfStatus(null);
            }

            var presentation = new FsaPresentation
            {
                Source = result,
                RequestedGrouping = grouping,
                MatrixOldFormat = matrixOldFormat,
                ParentGroupingAllowed = result.ParentGroupingAllowed,
                // ⛔ КОД причины, а не служебная строка результата (`A184`):
                // текст подсказки собирает вид из своих ресурсов, в обеих
                // культурах.
                ParentGroupingRefusalReason = result.ParentGroupingRefusalReason
            };

            // Родители — только при связанном ряде (`A145`, «Семантика
            // группировки»): у свободных дочерних предела родителя нет.
            // Недопустимый запрос молча показывает дочерних, а причина лежит
            // рядом — форме есть что сказать подсказкой.
            bool parents = grouping == FsaGrouping.Parents && result.ParentGroupingAllowed;
            presentation.Grouping = parents ? FsaGrouping.Parents : FsaGrouping.Daughters;

            List<FsaStackLayer> layers = parents
                ? BuildParentLayers(result, FsaResult.DefaultMaxNamedLayers)
                : result.BuildStackedLayers(FsaResult.DefaultMaxNamedLayers);
            presentation.Layers = layers;

            // Цвет зависит от состава кадра: место в палитре берётся по
            // имени, а занятое отдаётся следующему свободному. Значит
            // раздавать цвета надо один раз на весь список, иначе отрисовка
            // и таблица разрешили бы столкновения по-разному.
            presentation.Colors = FsaPalette.Assign(layers.ConvertAll(l => l.Name));

            presentation.QualityText = QualityText(result, matrixOldFormat);
            presentation.Rows = BuildRows(result, presentation);
            return presentation;
        }

        // ------------------------------------------------------------------
        // Родительская группировка
        // ------------------------------------------------------------------

        /// <summary>
        /// Слои в РОДИТЕЛЬСКОМ режиме: члены одного
        /// <see cref="FsaStackLayer.DecayChainRoot"/> слиты в одну ленту корня
        /// ДО выбора верхних N (`A145`, «Семантика группировки»): кривые
        /// суммируются, сумм-пики суммируются отдельно, доли складываются, цвет
        /// и подпись — по корню; одиночные нуклиды и приборные образы остаются
        /// отдельными.
        ///
        /// Берутся ВСЕ дочерние слои без свёртки
        /// (<see cref="FsaResult.BuildStackedLayers"/> с бесконечным лимитом —
        /// те же кривые, тот же знаменатель, тот же отсев невидимых `S87`),
        /// поэтому сумма родительской ленты равна сумме показанных дочерних
        /// тождественно (критерий 8). Верхние N среди СЛИТЫХ строк отбираются
        /// по той же доле, что печатается (`S71`), остаток — «прочее»;
        /// порядок — тот же ранг и та же доля, что у дочерних.
        /// </summary>
        public static List<FsaStackLayer> BuildParentLayers(FsaResult result, int maxNamedLayers)
        {
            List<FsaStackLayer> full = result.BuildStackedLayers(int.MaxValue);
            int channels = result.Model != null ? result.Model.Length : 0;

            var merged = new List<FsaStackLayer>();
            var byRoot = new Dictionary<string, FsaStackLayer>(StringComparer.Ordinal);
            foreach (FsaStackLayer layer in full)
            {
                if (layer.Kind == FsaComponentKind.Nuisance || string.IsNullOrEmpty(layer.DecayChainRoot))
                {
                    merged.Add(layer);
                    continue;
                }

                FsaStackLayer parent;
                if (!byRoot.TryGetValue(layer.DecayChainRoot, out parent))
                {
                    parent = new FsaStackLayer
                    {
                        Name = layer.DecayChainRoot,
                        Kind = FsaComponentKind.Chain,
                        // Связка амплитуды у родительской строки — сам ряд:
                        // родители допустимы только при связанном ряде, и
                        // подпись «ряд X» (`S72`) читается по этому полю.
                        ChainRoot = layer.DecayChainRoot,
                        DecayChainRoot = layer.DecayChainRoot,
                        Curve = new double[channels]
                    };
                    byRoot[layer.DecayChainRoot] = parent;
                    merged.Add(parent);
                }

                double[] curve = parent.Curve;
                AddInto(ref curve, layer.Curve, channels);
                parent.Curve = curve;
                if (layer.SumPeakCurve != null)
                {
                    double[] sums = parent.SumPeakCurve;
                    AddInto(ref sums, layer.SumPeakCurve, channels);
                    parent.SumPeakCurve = sums;
                }

                parent.SharePercent += layer.SharePercent;
            }

            // Верхние N нуклидных строк по доле; остальные — в «прочее».
            // Приборные образы (ранг 1) показываются всегда и в лимит не входят,
            // подложка (ранг 2) — тоже.
            var nuclides = new List<int>();
            for (int k = 0; k < merged.Count; k++)
            {
                if (LayerRank(merged[k]) == 0)
                {
                    nuclides.Add(k);
                }
            }

            nuclides.Sort((x, y) =>
            {
                int c = merged[y].SharePercent.CompareTo(merged[x].SharePercent);
                return c != 0 ? c : x.CompareTo(y);
            });

            var keep = new List<FsaStackLayer>();
            var rest = new List<FsaStackLayer>();
            var named = new HashSet<int>();
            for (int i = 0; i < nuclides.Count; i++)
            {
                if (i < maxNamedLayers)
                {
                    named.Add(nuclides[i]);
                }
            }

            for (int k = 0; k < merged.Count; k++)
            {
                if (LayerRank(merged[k]) != 0 || named.Contains(k))
                {
                    keep.Add(merged[k]);
                }
                else
                {
                    rest.Add(merged[k]);
                }
            }

            if (rest.Count > 0)
            {
                var other = new FsaStackLayer
                {
                    Name = FsaResult.OtherLayerName,
                    Kind = FsaComponentKind.Single,
                    Curve = new double[channels]
                };
                foreach (FsaStackLayer layer in rest)
                {
                    double[] curve = other.Curve;
                    AddInto(ref curve, layer.Curve, channels);
                    other.Curve = curve;
                    if (layer.SumPeakCurve != null)
                    {
                        double[] sums = other.SumPeakCurve;
                        AddInto(ref sums, layer.SumPeakCurve, channels);
                        other.SumPeakCurve = sums;
                    }

                    other.SharePercent += layer.SharePercent;
                }

                keep.Add(other);
            }

            // Порядок: нуклиды, потом приборные образы и «прочее», потом
            // подложка; внутри — по убыванию доли, устойчиво.
            int[] order = new int[keep.Count];
            for (int k = 0; k < order.Length; k++)
            {
                order[k] = k;
            }

            Array.Sort(order, (x, y) =>
            {
                int rx = LayerRank(keep[x]), ry = LayerRank(keep[y]);
                if (rx != ry)
                {
                    return rx.CompareTo(ry);
                }

                int c = keep[y].SharePercent.CompareTo(keep[x].SharePercent);
                return c != 0 ? c : x.CompareTo(y);
            });

            var ordered = new List<FsaStackLayer>(keep.Count);
            foreach (int k in order)
            {
                ordered.Add(keep[k]);
            }

            return ordered;
        }

        static void AddInto(ref double[] target, double[] source, int channels)
        {
            if (target == null)
            {
                target = new double[channels];
            }

            if (source == null)
            {
                return;
            }

            for (int i = 0; i < channels && i < source.Length; i++)
            {
                target[i] += source[i];
            }
        }

        /// <summary>0 — нуклид, 1 — приборный образ и «прочее», 2 — подложка (то же правило, что у <see cref="FsaResult"/>).</summary>
        static int LayerRank(FsaStackLayer layer)
        {
            if (string.Equals(layer.Name, FsaResult.ContinuumLayerName, StringComparison.Ordinal))
            {
                return 2;
            }

            return layer.Kind == FsaComponentKind.Nuisance
                   || string.Equals(layer.Name, FsaResult.OtherLayerName, StringComparison.Ordinal)
                ? 1 : 0;
        }

        // ------------------------------------------------------------------
        // Строки отчёта
        // ------------------------------------------------------------------

        /// <summary>
        /// Порядок строк после успешного расчёта фиксирован (`A145`, «Таблица
        /// отчёта»): слои состава; сумм-пики; необнаруженные поимённо;
        /// свёрнутые необнаруженные; «без фона»; невязка; качество.
        /// </summary>
        static List<FsaReportRow> BuildRows(FsaResult result, FsaPresentation presentation)
        {
            var rows = new List<FsaReportRow>();
            List<FsaStackLayer> layers = presentation.Layers;

            // 1. Строки состава: квадратик цвета слоя, имя, доля.
            foreach (FsaStackLayer layer in layers)
            {
                rows.Add(new FsaReportRow
                {
                    Kind = FsaReportRowKind.Layer,
                    Name = RowName(layer.Name, layer.ChainRoot),
                    Value = ShareText(layer),
                    Swatch = FsaSwatchKind.Solid,
                    Color = presentation.ColorOf(layer.Name),
                    Layer = layer
                });
            }

            // 2. По строке на каждый нуклид со своими сумм-пиками: образец
            // узора в ЕГО цвете и его имя. Узор обязателен — читатель ищет на
            // графике узор, а не текст; цвет обязателен — иначе при двух
            // нуклидах с каскадами непонятно, чья штриховка какая. Доля не
            // печатается нарочно: сумм-пики уже посчитаны внутри доли своего
            // нуклида, и второе число рядом складывали бы с первым. Пометка
            // связки (`S72`) сюда не ставится: строка состава этого же нуклида
            // стоит выше и уже сказала её.
            foreach (FsaStackLayer layer in layers)
            {
                if (layer.SumPeakCurve == null)
                {
                    continue;
                }

                rows.Add(new FsaReportRow
                {
                    Kind = FsaReportRowKind.SumPeaks,
                    Name = string.Format(CultureInfo.InvariantCulture,
                                         Resources.FSASumPeakRow,
                                         FsaPalette.DisplayName(layer.Name)),
                    Value = string.Empty,
                    Swatch = FsaSwatchKind.SumPeakHatch,
                    Color = presentation.ColorOf(layer.Name),
                    Layer = layer
                });
            }

            // 3. «Не обнаружен» с пределом обнаружения (S9): имя серым — у
            // кандидата нет ленты и нет цвета; справа «< доля» той же колонкой
            // и той же мерой, что доли состава (S68). Формат G3 — три значащие
            // цифры, точность пределов выше трёх цифр была бы враньём.
            foreach (FsaCharacteristicLimit limit in UndetectedNamed(result))
            {
                rows.Add(new FsaReportRow
                {
                    Kind = FsaReportRowKind.Undetected,
                    Name = RowName(limit.Name,
                                   limit.Kind == FsaComponentKind.Chain ? limit.Name : null),
                    Value = LimitText(LimitSharePercent(result, limit.DetectionLimitPeakCounts)),
                    Muted = true
                });
            }

            // ⛔ (`AMBER5`) ОБРАЗ ПРИБОРА, ПРЕДЪЯВЛЕННЫЙ И НЕ ВЫДЕЛИВШИЙСЯ,
            // НАЗЫВАЕТСЯ. Вопрос Amber 08.09.2026: «Где обратное рассеивание?»
            // — на `Чароит в домике` строки не было вовсе, хотя переключатель
            // стоял.
            //
            // Физика: рассеянный назад квант возвращается широким горбом около
            // 200 кэВ, и подложка модели тоже гладкая. При живой матрице спора
            // нет — рассеяние уже в ней, отдельный образ не строится
            // (~~`A83`~~). Без матрицы форма континуума неизвестна и свободна,
            // и гладкий горб внутри свободной гладкой подложки неразличим:
            // измерено на чароите — в полосе 120…280 кэВ подложка держит
            // 796 338 отсчётов из 872 542 (91 %), образу достаётся ноль.
            //
            // ⚠ Значение — СЛОВО, а не число, ровно по тому же доводу, по
            // какому у выделившегося рассеяния печатается «есть», а не доля
            // (~~`S85`~~, решение Amber 24.08.2026): величина зависит от
            // густоты узлов сплайна сильнее, чем от самого рассеяния.
            // Молчание же неотличимо от «образ не строился вовсе».
            if (result.SuppressedImages != null)
            {
                bool shown = false;
                foreach (FsaStackLayer layer in presentation.Layers)
                {
                    if (string.Equals(layer.Name, FsaResult.BackscatterLayerName,
                                      StringComparison.Ordinal))
                    {
                        shown = true;
                    }
                }

                foreach (FsaSuppressedImage image in result.SuppressedImages)
                {
                    if (shown || !string.Equals(image.Name, FsaResult.BackscatterLayerName,
                                                StringComparison.Ordinal))
                    {
                        continue;
                    }

                    rows.Add(new FsaReportRow
                    {
                        Kind = FsaReportRowKind.Undetected,
                        Name = FsaPalette.DisplayName(image.Name),
                        Value = Resources.FSANotResolvedNoShare,
                        Hint = Resources.FSANotResolvedHint,
                        Muted = true
                    });
                }
            }

            // 4. (S69) Кандидаты, которые АПРИОРИ не могут показать себя гаммой, —
            // одной строкой с суммарным пределом. Их предел не ограничивает
            // содержание ничем, и печатать его отдельным числом на каждого
            // значило бы выдавать за измерение то, что измерением не является.
            //
            // ⛔ Это ВТОРАЯ «прочие» в таблице, и она НЕ ТА, что в составе
            // (S71): та сворачивает ОБНАРУЖЕННЫХ сверх лимита названных, эта —
            // НЕ обнаруженных ниже порога выхода. Поэтому у неё своя подпись со
            // своим числом свёрнутых имён.
            //
            // ⚠ Сумма пределов как верхняя граница законна (если каждое
            // a_i < L_i, то Σa_i < ΣL_i), но доверительный уровень у суммы уже
            // не 95 %, и подпись этого не обещает.
            List<FsaCharacteristicLimit> folded = UndetectedFolded(result);
            if (folded.Count > 0)
            {
                double sum = 0.0;
                foreach (FsaCharacteristicLimit limit in folded)
                {
                    if (!double.IsNaN(limit.DetectionLimitPeakCounts))
                    {
                        sum += limit.DetectionLimitPeakCounts;
                    }
                }

                // (`AMBER6`) Свёрнутые НАЗЫВАЮТСЯ в подсказке. Свёртка сама по
                // себе верна (у этих кандидатов гамма-выход ниже порога, и
                // отдельный предел был бы обещанием измерения, которого нет),
                // но человек, спросивший «где радон?», обязан получить ответ,
                // а не пустую строку: имена есть, места в строке нет —
                // значит место подсказки.
                var names = new List<string>();
                foreach (FsaCharacteristicLimit limit in folded)
                {
                    names.Add(limit.Name);
                }

                rows.Add(new FsaReportRow
                {
                    Kind = FsaReportRowKind.UndetectedFolded,
                    Name = string.Format(CultureInfo.InvariantCulture,
                                         Resources.FSAUndetectedFoldedRow, folded.Count),
                    Value = LimitText(LimitSharePercent(result, sum)),
                    Hint = string.Join(", ", names.ToArray()),
                    Muted = true
                });
            }

            // 5. «БЕЗ ФОНА» (S44, решение Amber 15.08.2026) — ОТДЕЛЬНОЙ строкой
            // и красным, а не хвостом служебной пометки. Условие — по факту
            // вычитания: фона не подали вовсе или подали и отбросили — для
            // читающего разницы нет, разбор в обоих случаях идёт по
            // неочищенному спектру.
            if (!result.BackgroundUsed)
            {
                rows.Add(new FsaReportRow
                {
                    Kind = FsaReportRowKind.NoBackground,
                    Name = Resources.FSANoBackgroundMark,
                    Value = string.Empty,
                    Warning = true
                });
            }

            // ⛔ Строки «подавлено: …» здесь БОЛЬШЕ НЕТ — снята по прямому
            // указанию Amber 19.08.2026 (`S86`) и в `A145` не возвращается.

            // 6. (S51) НЕВЯЗКА МОДЕЛИ — читать надо её, а не χ²/ndf. (`S111`,
            // `A28`) Образец — ТА ЖЕ прямая клетка, что на графике, названная по
            // чёрной половине. ⛔ ЧИСЛО — ПЛОЩАДЬ НАРИСОВАННОЙ ЛЕНТЫ, ОБЕ
            // ПОЛОВИНЫ, знаки по чтению человека: ПЛЮС — модель ПРИПИСАЛА
            // лишнее, МИНУС — модели НЕ ХВАТИЛО. ε не снята — ею меряется
            // корпус (`FsaResult.ModelResidual`).
            //
            // ⛔ ФОРМАТ `f`, А НЕ `n` (`A244`, решение Amber 05.09.2026):
            // `n` несёт разделитель РАЗРЯДОВ даже на инвариантной культуре
            // (`1234.5` → `1,234.50`), а группировки разрядов нет вовсе. То же
            // у χ²/ndf ниже и у доли слоя в <see cref="ShareText"/>.
            rows.Add(new FsaReportRow
            {
                Kind = FsaReportRowKind.Residual,
                Name = Resources.FSAModelResidualRow,
                Value = string.Format(CultureInfo.InvariantCulture,
                                      Resources.FSAResidualCountsValue,
                                      (100.0 * result.ResidualExcessShare).ToString("f1", CultureInfo.InvariantCulture),
                                      (100.0 * result.ResidualMissingShare).ToString("f1", CultureInfo.InvariantCulture)),
                Swatch = FsaSwatchKind.ResidualCross,
                Color = ResidualColor
            });

            // 7. Строка качества: полный текст пометок, χ²/ndf справа.
            rows.Add(new FsaReportRow
            {
                Kind = FsaReportRowKind.Quality,
                Name = presentation.QualityText,
                Value = result.Chi2Ndf.ToString("f2", CultureInfo.InvariantCulture)
            });

            return rows;
        }

        /// <summary>
        /// ТЕКСТ строки качества целиком: «χ²/ndf» и хвост пометок за ним.
        ///
        /// ⛔ Чистая сборка текста, вынесенная из отрисовки (`A96`): сторожу не
        /// нужны ни вид, ни `Graphics`, ни окно, и он зовёт РОВНО ТО, что зовёт
        /// таблица. ⚠ Порядок пометок — их СТАРШИНСТВО (временная таблица на
        /// графике урезает хвост с конца, `S104`); менять порядок — решение
        /// Amber, а не построителя.
        /// </summary>
        public static string QualityText(FsaResult result, bool matrixOldFormat)
        {
            string quality = "χ²/ndf";

            // (`S104`) ВЕРДИКТ: состав пересилен одним приборным образом.
            // Правило и его двойник в корпусной мерке описаны у
            // <see cref="FsaResult.SuppressorName"/>; здесь только читатель.
            // Первым в хвосте, потому что хвост урезается с конца, а вердикт
            // важнее всего.
            if (result.CompositionSuppressed)
            {
                quality += Resources.FSASuppressedMark;
            }

            // Пометка S2: с матрицей отклика образы или без — всегда, одна из
            // двух. (`A50`) Третий случай отделён от второго: матрица У КРИВОЙ
            // ЕСТЬ, но посчитана прежним форматом файла. «Без матрицы» и
            // «матрица стара» лечатся по-разному.
            quality += result.ResponseMatrixUsed
                ? Resources.FSAMatrixMark
                : matrixOldFormat
                    ? Resources.FSAOldMatrixMark
                    : Resources.FSANoMatrixMark;

            // Каскадное суммирование отмечается только когда оно СРАБОТАЛО:
            // у состава без каскадов (Cs-137, K-40) поправка возвращает
            // единицы, и пометка сказала бы о работе, которой не было.
            if (result.CascadeSummingUsed)
            {
                quality += Resources.FSACascadeMark;
            }

            if (!result.EfficiencyUsed)
            {
                quality += Resources.FSANoEfficiencyMark;
            }

            // (`S44`, решение Amber 01.09.2026 «ПОКАЗЫВАТЬ В ОКНЕ») ФОН ПОДАН
            // И НЕ ВЗЯТ. Пометка стоит между «без кривой» и краем сетки, как
            // указано строкой реестра: это происшествие разбора, и хвост
            // урезается с конца, а сказать о невычтенном фоне важнее, чем о
            // крае сетки дрейфа.
            //
            // ⛔ ПРИЧИНА СЛОВАМИ ЖИВЁТ НЕ ЗДЕСЬ, а строкой блока «Качество
            // разбора» в окне отчёта (`A247`): этот хвост — цепочка коротких
            // пометок, целое предложение внутри него нечитаемо, и, главное,
            // экранного читателя у самого хвоста БОЛЬШЕ НЕТ — таблица
            // подменяет подпись строки качества на «χ²/ndf», а график берёт у
            // снимка только слои и цвета. Здесь пометка нужна модели и пробам.
            if (result.BackgroundRejected != null)
            {
                quality += Resources.FSABackgroundRejectedMark;
            }

            if (result.DriftOnGridEdge)
            {
                quality += Resources.FSADriftEdgeMark;
            }

            return quality;
        }

        /// <summary>
        /// (`S72`) ПОДПИСЬ СТРОКИ СОСТАВА, с пометкой связки равновесия.
        ///
        /// Связанная амплитуда и свободная — РАЗНЫЕ утверждения о пробе
        /// (решение Amber 18.08.2026): «Pb-212 23 %» при включённой галке
        /// значит «столько вышло из ОДНОЙ амплитуды ряда по закреплённой доле
        /// ветвления», при выключенной — «столько данные дали ЕМУ». Пометка
        /// идёт ТЕКСТОМ, а не цветом или значком. У колонки ряда, чьё имя и
        /// есть корень, второе имя не печатается.
        /// </summary>
        public static string RowName(string name, string chainRoot)
        {
            string shown = FsaPalette.DisplayName(name);
            if (string.IsNullOrEmpty(chainRoot))
            {
                return shown;
            }

            string root = FsaPalette.DisplayName(chainRoot);
            return string.Equals(root, shown, StringComparison.Ordinal)
                ? string.Format(CultureInfo.InvariantCulture, Resources.FSAChainRow, shown)
                : string.Format(CultureInfo.InvariantCulture, Resources.FSAChainMemberRow, shown, root);
        }

        /// <summary>
        /// Правая колонка строки состава: доля слоя.
        ///
        /// ⛔ У обратного рассеяния доли НЕТ — печатается пометка о наличии
        /// (`S85`, решение Amber 24.08.2026): величина зависит от густоты узлов
        /// сплайна подложки сильнее, чем от самого рассеяния.
        ///
        /// ⛔ Формат `f2`, а не `n2`: группировки разрядов нет вовсе
        /// (`A244`, решение Amber 05.09.2026) — доля слоя бывает ≥ 1000 %
        /// у вырожденного разбора, и `n2` напечатал бы там `1,234.50`.
        /// </summary>
        public static string ShareText(FsaStackLayer layer)
        {
            if (string.Equals(layer.Name, FsaResult.BackscatterLayerName, StringComparison.Ordinal))
            {
                return Resources.FSAPresentNoShare;
            }

            return layer.SharePercent.ToString("f2", CultureInfo.InvariantCulture) + Resources.PercentCharacter;
        }

        /// <summary>Формат предела: три значащие цифры, как и прежде.</summary>
        public static string LimitText(double sharePercent)
        {
            return string.Format(CultureInfo.InvariantCulture,
                                 Resources.FSAMdaValue,
                                 double.IsNaN(sharePercent)
                                     ? "?"
                                     : sharePercent.ToString("G3", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Доля, которую кандидат занял бы в составе, стой его амплитуда на
        /// пределе обнаружения, % (`S68`). ⛔ Знаменатель —
        /// <see cref="FsaResult.StackTotal"/>, ТОТ ЖЕ, которым считаются доли
        /// строк состава (решение Amber 18.08.2026). NaN — считать нечем.
        /// </summary>
        public static double LimitSharePercent(FsaResult result, double peakCounts)
        {
            return result.StackTotal > 0.0 && !double.IsNaN(peakCounts)
                ? 100.0 * peakCounts / result.StackTotal
                : double.NaN;
        }

        /// <summary>
        /// НЕ вошедшие в состав кандидаты библиотеки с определённым пределом
        /// обнаружения (S9). Вырожденные и без предела не показываются: врать
        /// порогом, которого нет, хуже, чем молчать.
        /// </summary>
        public static List<FsaCharacteristicLimit> Undetected(FsaResult result)
        {
            var found = new List<FsaCharacteristicLimit>();
            if (result.CharacteristicLimits == null)
            {
                return found;
            }

            foreach (FsaCharacteristicLimit limit in result.CharacteristicLimits)
            {
                if (!limit.Detected && !limit.Degenerate
                    && !double.IsNaN(limit.DetectionLimitRate)
                    && limit.DetectionLimitRate > 0.0)
                {
                    found.Add(limit);
                }
            }

            return found;
        }

        /// <summary>
        /// Кандидат показывается СВОЕЙ строкой: выход у него либо приличный,
        /// либо НЕИЗВЕСТЕН. ⚠ Неизвестный выход — это не «мал»: априорную сумму
        /// заполняет только сборка из баз; на прежнем пути состава её нет, и
        /// там список остаётся ровно таким, каким был (`S74`, решение Amber
        /// 23.08.2026).
        /// </summary>
        public static bool NamedUndetected(FsaCharacteristicLimit limit)
        {
            return double.IsNaN(limit.TotalYieldPercent)
                   || limit.TotalYieldPercent >= MinTotalYieldPercent;
        }

        public static List<FsaCharacteristicLimit> UndetectedNamed(FsaResult result)
        {
            var found = new List<FsaCharacteristicLimit>();
            foreach (FsaCharacteristicLimit limit in Undetected(result))
            {
                if (NamedUndetected(limit))
                {
                    found.Add(limit);
                }
            }

            return found;
        }

        /// <summary>Кандидаты, свёрнутые порогом выхода в одну строку (`S69`).</summary>
        public static List<FsaCharacteristicLimit> UndetectedFolded(FsaResult result)
        {
            var found = new List<FsaCharacteristicLimit>();
            foreach (FsaCharacteristicLimit limit in Undetected(result))
            {
                if (!NamedUndetected(limit))
                {
                    found.Add(limit);
                }
            }

            return found;
        }
    }
}
