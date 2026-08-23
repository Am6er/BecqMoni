using System;
using System.Collections.Generic;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>Компонент в готовом разложении.</summary>
    public sealed class FsaComponentResult
    {
        public string Name { get; set; }

        public FsaComponentKind Kind { get; set; }

        /// <summary>Вклад компонента по каналам, отсчёты (амплитуда x образ).</summary>
        public double[] Curve { get; set; }

        /// <summary>
        /// Та ЧАСТЬ <see cref="Curve"/>, что пришла от сумм-пиков каскада;
        /// null — их нет. Не отдельный компонент: в фите это одна колонка с
        /// нуклидом, и свободной амплитуды у сумм-пиков нет нарочно. Здесь
        /// лежит только ради отрисовки подслоем.
        /// </summary>
        public double[] SumPeakCurve { get; set; }

        /// <summary>
        /// Отсчёты компонента в его ПИКОВЫХ ОКНАХ (±2 ПШПВ у каждой линии и
        /// каждого сумм-пика), а не по всему образу — см.
        /// <c>FsaAnalyzer.PeakWindowCounts</c> и решение S24в.
        /// </summary>
        public double PeakCounts { get; set; }

        /// <summary>Скорость счёта компонента, имп/с.</summary>
        public double CountRate { get; set; }

        /// <summary>Значимость амплитуды (амплитуда / её погрешность).</summary>
        public double Z { get; set; }

        /// <summary>
        /// Доля в «пироге» — по объяснённым пиковым отсчётам, %.
        ///
        /// ⚠ Считается ТОЛЬКО по нуклидным компонентам и у служебных образов
        /// (<see cref="FsaComponentKind.Nuisance"/> — `Ann-511`, `SE`/`DE`,
        /// обратное рассеяние, рентген) равна нулю НАРОЧНО: «пирог» отвечает на
        /// вопрос «из чего проба», а вылет и аннигиляция — не вещество.
        ///
        /// Читать её как «сколько занимает компонент» НЕЛЬЗЯ — для этого есть
        /// <see cref="PeakSharePercent"/>. Ноль здесь означает «не вещество», а
        /// не «пусто», и ровно на этом `Ann-511` с 3.1 % пиковых отсчётов
        /// выглядела в сводке ОТСУТСТВУЮЩЕЙ (S49).
        /// </summary>
        public double SharePercent { get; set; }

        /// <summary>
        /// Доля пиковых отсчётов компонента среди ВСЕХ образов, включая
        /// служебные, %. В отличие от <see cref="SharePercent"/> заполняется у
        /// каждого компонента и нулём бывает только тогда, когда у компонента
        /// и правда ничего нет.
        ///
        /// Заведена по S49: `Ann-511` держала 70 542 пиковых отсчёта при z =
        /// 57.7 и печаталась нулём — то есть подмена, которую в итоге пришлось
        /// доказывать четырьмя независимыми путями, по сводке выглядела как
        /// отсутствие компонента. Континуум сюда не входит: у него нет линий и
        /// нет пикового окна.
        /// </summary>
        public double PeakSharePercent { get; set; }

        /// <summary>
        /// Порог решения a* компонента, имп/с (S9, Xu-2022/ISO 11929): величина,
        /// выше которой скорость счёта статистически отличима от нуля при
        /// α = 5 %. NaN — предел не определён (вырожденная колонка).
        /// </summary>
        public double DecisionThresholdRate { get; set; }

        /// <summary>
        /// Предел обнаружения a# (МДА-аналог в шкале скорости счёта), имп/с:
        /// наименьшая истинная скорость счёта, которую разбор ещё обнаруживает
        /// с β = 5 % при пороге <see cref="DecisionThresholdRate"/>.
        /// </summary>
        public double DetectionLimitRate { get; set; }

        /// <summary>
        /// (P6) χ²/ndf остатка в пиковых окнах компонента (±2 ПШПВ вокруг
        /// линий и сумм-пиков) ПРЕЖНИМИ весами. Сигма-множитель гипотезы (а)
        /// выводится читателем: √(max(1, ZoneChi2Ndf)/max(1, χ²/ndf общего)).
        /// NaN — не считалось (<c>FsaAnalyzer.PartialResiduals</c> выключен
        /// или у компонента нет линий в окне фита).
        /// </summary>
        public double ZoneChi2Ndf { get; set; } = double.NaN;

        /// <summary>
        /// (P6) Насколько включение компонента улучшает Σw·r² его же зоны:
        /// рефит того же состава без него при том же узле дрейфа. ≈0 — зоне
        /// компонент не нужен, кандидат на исключение гейтом (б). NaN — не
        /// считалось.
        /// </summary>
        public double ZoneDeltaD { get; set; } = double.NaN;

        /// <summary>(P6) Число каналов зоны; 0 — зона не строилась.</summary>
        public int ZoneChannels { get; set; }
    }

    /// <summary>
    /// Характеристические пределы ОДНОГО кандидата библиотеки (S9). Строка есть
    /// у каждого нуклидного компонента, поданного на разбор, — в том числе у НЕ
    /// вошедших в состав: метрологический ответ «не обнаружен» без «мог ли быть
    /// обнаружен» (МДА) — полответа. Формализм — Xu et al., ART 182 (2022)
    /// 110109 поверх ISO 11929; расчёт — <c>FsaAnalyzer.ComputeCharacteristicLimits</c>.
    /// </summary>
    public sealed class FsaCharacteristicLimit
    {
        public string Name { get; set; }

        public FsaComponentKind Kind { get; set; }

        /// <summary>Компонент вошёл в состав (амплитуда фита больше нуля).</summary>
        public bool Detected { get; set; }

        /// <summary>Оценённая скорость счёта компонента, имп/с; 0 у не вошедших.</summary>
        public double CountRate { get; set; }

        /// <summary>Порог решения a*, имп/с. NaN — предел не определён.</summary>
        public double DecisionThresholdRate { get; set; }

        /// <summary>Предел обнаружения a# (МДА-аналог), имп/с. NaN — не определён.</summary>
        public double DetectionLimitRate { get; set; }

        /// <summary>
        /// Колонка компонента коллинеарна остальной модели — информации о нём в
        /// спектре нет, и пределы не определены. Это не ошибка счёта, а свойство
        /// постановки (например, образ целиком закрыт другими компонентами).
        /// </summary>
        public bool Degenerate { get; set; }

        /// <summary>
        /// Доля образа, представимая остальной моделью, во ВЗВЕШЕННОЙ метрике
        /// фита: 0 — образ независим, →1 — почти коллинеарен (1 − denom/g_jj по
        /// дополнению Шура). Контекст для чтения пределов, а не гарантия:
        /// отказ МДА, найденный МК-поверкой на `G1S_Eu152_5cm` (Pu-238, 79 %
        /// пропусков впрыска на уровне МДА, все — нулевой оценкой), эта мера НЕ
        /// помечает — у того компонента она 0.054, наименьшая на спектре.
        /// Гипотеза «виновата коллинеарность» измерена и отпала; механизм не
        /// назван, остаток записан в строке S9.
        /// </summary>
        public double Collinearity { get; set; }

        /// <summary>
        /// Отсчёты образа в его пиковых окнах, если бы амплитуда стояла НА
        /// ПРЕДЕЛЕ ОБНАРУЖЕНИЯ a# (`S68`). NaN — предел не определён.
        ///
        /// Это ЧИСЛИТЕЛЬ доли, которую кандидат занял бы в составе; знаменатель
        /// — <see cref="FsaResult.StackTotal"/>, тот же, которым считаются доли
        /// строк состава (решение Amber 18.08.2026). Числом в имп/с колонка
        /// пределов подписана быть не может: вес линии в образе равен
        /// <c>I/100 × ε(E)</c> при профилях единичной площади, то есть амплитуда
        /// выражена в РАСПАДАХ, и <c>amplitude/liveTime</c> есть активность в
        /// шкале поданной кривой эффективности, а не скорость счёта. На
        /// `Th232_29.07.2022.xml` это было видно прямо: полная скорость счёта
        /// спектра 416.37 имп/с против напечатанного у Th-232 «&lt; 607 cps».
        ///
        /// ⚠ Сама величина в имп/с (<see cref="DetectionLimitRate"/>) остаётся
        /// и в модели, и в корпусных таблицах — из ЛЕГЕНДЫ убрана она, а не из
        /// расчёта.
        /// </summary>
        public double DetectionLimitPeakCounts { get; set; } = double.NaN;

        /// <summary>
        /// Суммарный выход всех излучений нуклида на собственный распад, %
        /// (`S69`) — копия <see cref="FsaComponent.TotalYieldPercent"/>. NaN —
        /// неизвестен.
        /// </summary>
        public double TotalYieldPercent { get; set; } = double.NaN;
    }

    /// <summary>
    /// Образ, который БЫЛ построен и предъявлен фиту, но в отчёт не вошёл
    /// (`S78`). Заведено потому, что «не строился» и «построен и признан
    /// незначимым» на экране были неразличимы: отсев по значимости
    /// (<c>FsaAnalyzer.RefitZ</c>, умолчание 3.0) убирает колонку из
    /// <c>FitResult.Columns</c> целиком, и следа не остаётся ни в легенде, ни в
    /// корпусной сводке.
    ///
    /// Молчаливый ноль уже стоил дорого однажды: `Ann-511` с 70 542 пиковыми
    /// отсчётами при z = 57.7 печаталась нулём и выглядела отсутствующей, и
    /// подмену пришлось доказывать четырьмя независимыми путями (`S49`).
    /// </summary>
    public sealed class FsaSuppressedImage
    {
        public string Name { get; set; }

        public FsaComponentKind Kind { get; set; }

        /// <summary>Значимость, с которой образ был выброшен; NaN — неизвестна.</summary>
        public double Z { get; set; } = double.NaN;
    }

    /// <summary>Слой стека для отрисовки: кривая и подпись с долей.</summary>
    public sealed class FsaStackLayer
    {
        public string Name { get; set; }

        public FsaComponentKind Kind { get; set; }

        /// <summary>Вклад слоя по каналам с разнесённой на него подложкой.</summary>
        public double[] Curve { get; set; }

        /// <summary>
        /// Часть <see cref="Curve"/> от сумм-пиков каскада; null — их нет.
        /// Рисуется ВНУТРИ ленты слоя штриховкой того же цвета: сумм-пик
        /// принадлежит своему нуклиду, отдельной строки в легенде и отдельной
        /// доли в «пироге» у него быть не должно. Подложка сюда не
        /// разносится — это чистый пиковый вклад.
        /// </summary>
        public double[] SumPeakCurve { get; set; }

        /// <summary>Доля слоя в полном счёте модели, %.</summary>
        public double SharePercent { get; set; }
    }

    /// <summary>Результат полноспектральной декомпозиции одного спектра.</summary>
    public sealed class FsaResult
    {
        public List<FsaComponentResult> Components { get; set; }

        /// <summary>
        /// Характеристические пределы ВСЕХ нуклидных кандидатов библиотеки —
        /// и вошедших в состав, и нет (S9). Порядок — порядок библиотеки.
        /// </summary>
        public List<FsaCharacteristicLimit> CharacteristicLimits { get; set; }

        /// <summary>Континуум модели (шапки сплайна), отсчёты по каналам.</summary>
        public double[] Continuum { get; set; }

        /// <summary>Вычтенный измеренный фон, отсчёты по каналам.</summary>
        public double[] Background { get; set; }

        /// <summary>Сумма модели, отсчёты по каналам.</summary>
        public double[] Model { get; set; }

        public int FirstChannel { get; set; }

        public int LastChannel { get; set; }

        public double Chi2Ndf { get; set; }

        /// <summary>
        /// (S44) Фон ВЫЧТЕН из спектра. Не путать с <see cref="Background"/>:
        /// та кривая создаётся всегда и при отсутствии фона состоит из нулей,
        /// поэтому по ней «был ли фон» не узнать — читатели, спрашивавшие её,
        /// молча получали «фон есть» на спектре без фона.
        /// </summary>
        public bool BackgroundUsed { get; set; }

        /// <summary>
        /// (S44) Фон был ПОДАН на разбор, но НЕ ВЗЯТ — с причиной словами
        /// («каналов у фона 1012, у спектра 1024»). null — фона не подавали
        /// либо он вычтен. Отказ обязан быть назван: обрезанный фон
        /// одиннадцати спектров G1S пролежал незамеченным именно потому, что
        /// ссылка обнулялась молча, а все читатели смотрели на наличие узла.
        /// </summary>
        public string BackgroundRejected { get; set; }

        /// <summary>
        /// χ²/ndf того же остатка ПРЕЖНИМИ весами (пуассон плюс шум фона) —
        /// без хуберовского перевзвешивания и без составного шума S43. Общая
        /// метрика: прогоны с разными весами решателя сравнимы только ею
        /// (ловушка fsa-hypotheses-2026-08.md §1/§4). При выключенном Хубере и
        /// γ = β = 0 совпадает с <see cref="Chi2Ndf"/>.
        /// </summary>
        public double Chi2NdfPoisson { get; set; }

        /// <summary>
        /// (S51) НЕВЯЗКА МОДЕЛИ ε — доля (не проценты), на которую модель врёт
        /// по форме спектра: ε = √(max(χ² − ndf, 0) / Σ M_i²·w_i) отчётными
        /// весами. В отличие от <see cref="Chi2NdfPoisson"/> сравнима МЕЖДУ
        /// спектрами: χ²/ndf растёт со статистикой, и «34» на германии с
        /// миллионами отсчётов и «3» на обсидиане с тысячами не говорят, какой
        /// разбор надёжнее, — а «модель врёт на 7 % формы» говорит.
        /// Ноль означает «модель согласна со статистикой» (χ² ≤ ndf), а не
        /// «не считалось».
        /// </summary>
        public double ModelResidual { get; set; }

        public double Gain { get; set; }

        public double OffsetChannels { get; set; }

        /// <summary>
        /// Оптимум дрейфа упёрся в границу сетки — шкале верить нельзя. Это ИЛИ
        /// двух признаков ниже; порознь они появились 13.08.2026, когда
        /// корпусный прогон (S1, S6) показал, что одним словом «дрейф» названы
        /// два разных отказа, и читатель предупреждения расширял не ту сетку.
        /// </summary>
        public bool DriftOnGridEdge
        {
            get { return this.GainOnGridEdge || this.OffsetOnGridEdge; }
        }

        /// <summary>Упёрлось УСИЛЕНИЕ: оптимум на краю сетки <c>GainRange</c>.</summary>
        public bool GainOnGridEdge { get; set; }

        /// <summary>Упёрся НОЛЬ шкалы: оптимум на краю сетки <c>OffsetRangeKev</c>.</summary>
        public bool OffsetOnGridEdge { get; set; }

        public double LiveTime { get; set; }

        /// <summary>Кривая эффективности была учтена.</summary>
        public bool EfficiencyUsed { get; set; }

        /// <summary>
        /// Образы построены по матрице отклика (S2). Без пометки «с матрицей»
        /// и «без матрицы» неотличимы на глаз, а браковка матрицы по
        /// отпечатку или формату файла молчалива — разложение просто тихо
        /// становится хуже.
        /// </summary>
        public bool ResponseMatrixUsed { get; set; }

        /// <summary>
        /// Каскадное суммирование СРАБОТАЛО: хоть одной линии состава поправлен
        /// пик или добавлен сумм-пик. Пометка ставится по факту, а не по
        /// включённому ключу: у состава из Cs-137 и K-40 каскадов нет вовсе, и
        /// «с суммированием» там значило бы, что поправка что-то сделала.
        /// </summary>
        public bool CascadeSummingUsed { get; set; }

        /// <summary>
        /// Знаменатель долей стека: Σ по слоям от последнего
        /// <see cref="BuildStackedLayers"/>. Заполняется им же и до первого его
        /// вызова равен нулю.
        ///
        /// Заведён ради `S68`: у НЕ вошедшего в состав кандидата ленты нет, а
        /// доля, которую он занял бы на пределе обнаружения, обязана считаться
        /// ТЕМ ЖЕ знаменателем, что и строки состава над ней (решение Amber
        /// 18.08.2026). Пересчитать этот знаменатель на стороне читателя значило
        /// бы завести второе правило для одного числа.
        /// </summary>
        public double StackTotal { get; private set; }

        /// <summary>
        /// (S78) Образы, построенные и предъявленные фиту, но выброшенные до
        /// отчёта. Пусто — выброшенных нет; null не бывает.
        /// </summary>
        public List<FsaSuppressedImage> SuppressedImages { get; set; }

        public FsaResult()
        {
            this.Components = new List<FsaComponentResult>();
            this.CharacteristicLimits = new List<FsaCharacteristicLimit>();
            this.SuppressedImages = new List<FsaSuppressedImage>();
        }

        /// <summary>
        /// Слои для послойной отрисовки. Континуум разносится по компонентам
        /// так, как это неявно делают полные измеренные образы: подложка на
        /// энергии E приписывается компонентам пропорционально их пиковому
        /// счёту ВЫШЕ E — комптоновское рассеяние сбрасывает энергию только
        /// вниз. Это правило отображения, а не фита.
        ///
        /// Исключение — хвост: выше самой верхней линии пикового счёта нет ни у
        /// кого, и разносить подложку не по чему. Там она остаётся отдельным
        /// серым слоем, иначе на пустом месте рисовался бы состав, которого
        /// модель не знает.
        ///
        /// ⛔ И ОТБОР девятки, и ПОРЯДОК строк идут по ТОЙ ЖЕ МЕРЕ, которая
        /// печатается, — по ДОЛЕ (<see cref="FsaStackLayer.SharePercent"/>), а
        /// не по высоте ленты (решение Amber 18.08.2026, `S71`). Прежде и то и
        /// другое решал <c>Max(Curve)</c>, отчего узкий высокий образ вытеснял
        /// широкий, но более весомый, а в легенде `Th232_29.07.2022.xml` Ac-228
        /// с 23.09 % стоял ТРЕТЬИМ, после Pb-212 18.89 % и Ra-224 8.22 %.
        ///
        /// ⚠ Потому же подложка разносится ДО отбора и по ВСЕМ компонентам, а
        /// не после: доля считается уже с разнесённой подложкой, и отбирать по
        /// сумме без неё значило бы снова мерить одним, а печатать другое.
        /// Свернуть «прочее» можно и после разноса — правило разноса линейно по
        /// накопленному счёту выше канала, поэтому сумма разнесённых кривых
        /// равна разносу по их сумме, и «прочее» от перестановки не меняется.
        /// </summary>
        public List<FsaStackLayer> BuildStackedLayers(int maxNamedLayers)
        {
            List<FsaStackLayer> layers = new List<FsaStackLayer>();
            this.StackTotal = 0.0;
            if (this.Components == null || this.Components.Count == 0)
            {
                return layers;
            }

            int channels = this.Model != null ? this.Model.Length : 0;
            if (channels == 0)
            {
                return layers;
            }

            List<FsaComponentResult> ordered = new List<FsaComponentResult>(this.Components);
            ordered.RemoveAll(c => c.Curve == null || Max(c.Curve) <= 0.0);
            if (ordered.Count == 0)
            {
                return layers;
            }

            // Сперва слой на КАЖДЫЙ компонент — отбор идёт после разноса
            // подложки, см. шапку метода.
            List<FsaStackLayer> full = new List<FsaStackLayer>(ordered.Count);
            foreach (FsaComponentResult component in ordered)
            {
                full.Add(new FsaStackLayer
                {
                    Name = component.Name,
                    Kind = component.Kind,
                    Curve = PositivePart(component.Curve),
                    SumPeakCurve = component.SumPeakCurve != null
                        ? (double[])component.SumPeakCurve.Clone()
                        : null
                });
            }

            double[] leftover = DistributeContinuum(full, channels);

            // Мера отбора и порядка — та же сумма, из которой потом считается
            // доля. Хранится отдельно, чтобы не пересчитывать её на каждое
            // сравнение внутри сортировки.
            double[] weight = new double[full.Count];
            double total = 0.0;
            for (int k = 0; k < full.Count; k++)
            {
                weight[k] = Sum(full[k].Curve);
                total += weight[k];
            }

            if (leftover != null)
            {
                total += Sum(leftover);
            }

            this.StackTotal = total;

            // Мешающие образы (рентген, пики вылета) показываются всегда и в
            // лимит НЕ входят: лимит отмеряет, сколько нуклидов названо
            // поимённо. Общий счётчик их смешивал — при обычном составе четыре
            // мешающих (рентген W и Pb, SE- и DE-2614, последние два в
            // библиотеке всегда) съедали четыре слота из шести, и нуклиды
            // сверх двух схлопывались в «other».
            //
            // Порядок обхода держится на исходном номере: Array.Sort
            // неустойчива, и без него компоненты равного веса перемешивались бы
            // от запуска к запуску.
            int[] byWeight = new int[full.Count];
            for (int k = 0; k < byWeight.Length; k++)
            {
                byWeight[k] = k;
            }

            Array.Sort(byWeight, (x, y) =>
            {
                int c = weight[y].CompareTo(weight[x]);
                return c != 0 ? c : x.CompareTo(y);
            });

            List<int> rest = new List<int>();
            int namedNuclides = 0;
            foreach (int k in byWeight)
            {
                if (full[k].Kind == FsaComponentKind.Nuisance)
                {
                    layers.Add(full[k]);
                }
                else if (namedNuclides < maxNamedLayers)
                {
                    layers.Add(full[k]);
                    namedNuclides++;
                }
                else
                {
                    rest.Add(k);
                }
            }

            if (rest.Count > 0)
            {
                double[] other = new double[channels];
                double[] otherSums = null;
                foreach (int k in rest)
                {
                    double[] curve = full[k].Curve;
                    for (int i = 0; i < channels && i < curve.Length; i++)
                    {
                        other[i] += curve[i];
                    }

                    double[] sums = full[k].SumPeakCurve;
                    if (sums == null)
                    {
                        continue;
                    }

                    if (otherSums == null)
                    {
                        otherSums = new double[channels];
                    }

                    for (int i = 0; i < channels && i < sums.Length; i++)
                    {
                        otherSums[i] += sums[i];
                    }
                }

                layers.Add(new FsaStackLayer
                {
                    Name = OtherLayerName,
                    Kind = FsaComponentKind.Single,
                    Curve = other,
                    SumPeakCurve = otherSums
                });
            }

            if (leftover != null)
            {
                // Неразнесённый остаток — только хвост выше последней линии.
                layers.Add(new FsaStackLayer
                {
                    Name = ContinuumLayerName,
                    Kind = FsaComponentKind.Nuisance,
                    Curve = leftover
                });
            }

            foreach (FsaStackLayer layer in layers)
            {
                layer.SharePercent = total > 0.0 ? 100.0 * Sum(layer.Curve) / total : 0.0;
            }

            // Слой, чья доля печатается НУЛЁМ, из стека убирается целиком —
            // указание Amber 19.08.2026 (`S87`): «если там 0, его не должно
            // быть отрисовано». Найдено ею на `Cs 137 в домике 24.11.2022.xml`,
            // где связка равновесия (`S70`) даёт строку каждому члену ряда, а
            // у эманаций и α-излучателей гамм нет: `Rn-222 0.00 %`,
            // `Po-214 0.00 %`, и туда же `continuum 0.00 %`.
            //
            // ⛔ Убирается И СТРОКА, И ЛЕНТА, потому что список задаёт то и
            // другое разом (см. шапку про порядок): легенда, в которой строк
            // меньше, чем лент, — тот же способ ошибиться, что и легенда не в
            // том порядке.
            //
            // ⚠ Это ОТОБРАЖЕНИЕ, а не фит: колонка остаётся в разложении со
            // своей амплитудой, `StackTotal` — знаменатель долей — считается
            // ДО отсева и не меняется, поэтому доли оставшихся строк те же, что
            // были. Верх стека проседает на сумму убранного, то есть меньше чем
            // на 0.005 % от него на слой, — тем же порядком, каким его уже
            // двигает <see cref="PositivePart"/>.
            layers.RemoveAll(l => l.SharePercent < MinShownSharePercent);

            // Порядок: сначала нуклиды, потом всё остальное — приборные образы
            // (рассеяние, вылеты, рентген), «прочее» и подложка. Список задаёт и
            // стопку, и легенду разом, поэтому раскладывать их порознь нельзя:
            // легенда, идущая не в том порядке, в каком нарисованы ленты, —
            // отдельный способ ошибиться. Внутри каждой группы порядок — по
            // убыванию ДОЛИ, той же, что напечатана.
            // Разбиение по рангам не трогается: строки состава обязаны идти
            // выше приборных образов, как бы ни легли доли.
            int[] order = new int[layers.Count];
            for (int k = 0; k < order.Length; k++)
            {
                order[k] = k;
            }

            List<FsaStackLayer> source = new List<FsaStackLayer>(layers);
            Array.Sort(order, (x, y) =>
            {
                int rx = LayerRank(source[x]), ry = LayerRank(source[y]);
                if (rx != ry)
                {
                    return rx.CompareTo(ry);
                }

                int c = source[y].SharePercent.CompareTo(source[x].SharePercent);
                return c != 0 ? c : x.CompareTo(y);
            });

            layers.Clear();
            for (int k = 0; k < order.Length; k++)
            {
                layers.Add(source[order[k]]);
            }

            return layers;
        }

        /// <summary>0 — нуклид, 1 — приборный образ и «прочее», 2 — подложка.</summary>
        static int LayerRank(FsaStackLayer layer)
        {
            if (string.Equals(layer.Name, ContinuumLayerName, StringComparison.Ordinal))
            {
                return 2;
            }

            return layer.Kind == FsaComponentKind.Nuisance
                   || string.Equals(layer.Name, OtherLayerName, StringComparison.Ordinal)
                ? 1 : 0;
        }

        /// <summary>
        /// Наименьшая доля, при которой у слоя ещё есть строка в легенде и
        /// лента в стеке, % (`S87`).
        ///
        /// ⛔ Величина НЕ подобрана: она равна половине последнего печатаемого
        /// разряда. Доля выводится как <c>"n2"</c> — два знака после запятой, —
        /// значит всё, что меньше 0.005 %, на экране и есть «0.00 %». Меняется
        /// формат — меняется и она, порознь их держать нельзя: порог выше
        /// формата прячет то, что видно, ниже — оставляет нули, ради которых
        /// правило и заведено.
        ///
        /// ⚠ Что при этом уходит с картинки, посчитано: 0.005 % от стека
        /// `Cs 137 в домике 24.11.2022.xml` (250 млн отсчётов) — 12.5 тыс.
        /// отсчётов, и даже собранные в один пик шириной с ПШПВ они дают около
        /// 0.25 % высоты подложки под ним. Лента такой толщины не рисуется
        /// вовсе, поэтому убирается не «мелкое», а невидимое.
        /// </summary>
        public const double MinShownSharePercent = 0.005;

        public const string OtherLayerName = "other";

        /// <summary>Имя слоя подложки в стеке отрисовки.</summary>
        public const string ContinuumLayerName = "continuum";

        /// <summary>
        /// Имя образа случайных наложений (pile-up). Служебное и НЕ переводится:
        /// по нему раздаётся цвет и его же ищет проба. Человеку показывается
        /// перевод — см. <see cref="FsaPalette.DisplayName"/>.
        /// </summary>
        public const string PileUpLayerName = "pile-up";

        /// <summary>
        /// Копия кривой без отрицательной части. У ленты стека не бывает
        /// отрицательной высоты, а знакопеременные образы есть: наложения
        /// ПЕРЕНОСЯТ счёт — снизу убыль, сверху приход. Рисуется приход,
        /// убыль (доли процента) остаётся в модели, но не в стеке; из-за этого
        /// верх стека расходится с суммой модели на ту же долю процента.
        /// </summary>
        static double[] PositivePart(double[] curve)
        {
            double[] copy = new double[curve.Length];
            for (int i = 0; i < curve.Length; i++)
            {
                copy[i] = curve[i] > 0.0 ? curve[i] : 0.0;
            }

            return copy;
        }

        /// <summary>
        /// Разнести континуум по компонентам. Возвращает остаток — ту часть
        /// подложки, разносить которую не по чему (выше самой верхней линии
        /// пикового счёта ни у одного компонента нет), или null, если остатка
        /// нет.
        /// </summary>
        double[] DistributeContinuum(List<FsaStackLayer> layers, int channels)
        {
            if (this.Continuum == null || layers.Count == 0)
            {
                return null;
            }

            int count = layers.Count;
            double[][] above = new double[count][];
            for (int k = 0; k < count; k++)
            {
                double[] curve = layers[k].Curve;
                double[] cumulative = new double[channels];
                double running = 0.0;
                for (int i = channels - 1; i >= 0; i--)
                {
                    running += i < curve.Length ? curve[i] : 0.0;
                    cumulative[i] = running;
                }

                above[k] = cumulative;
            }

            double[] leftover = null;
            for (int i = 0; i < channels; i++)
            {
                double continuum = i < this.Continuum.Length ? this.Continuum[i] : 0.0;
                if (continuum <= 0.0)
                {
                    continue;
                }

                double totalAbove = 0.0;
                for (int k = 0; k < count; k++)
                {
                    totalAbove += above[k][i];
                }

                if (totalAbove <= 0.0)
                {
                    // Хвост: линий выше нет, приписать подложку некому.
                    if (leftover == null)
                    {
                        leftover = new double[channels];
                    }

                    leftover[i] = continuum;
                    continue;
                }

                for (int k = 0; k < count; k++)
                {
                    // Длина проверяется, как и везде рядом: кривые слоёв
                    // приходят от анализатора и в принципе могут быть короче
                    // модели, по которой считается channels.
                    double[] curve = layers[k].Curve;
                    if (i < curve.Length)
                    {
                        curve[i] += above[k][i] / totalAbove * continuum;
                    }
                }
            }

            return leftover;
        }

        static double Max(double[] values)
        {
            double max = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max)
                {
                    max = values[i];
                }
            }

            return max;
        }

        static double Sum(double[] values)
        {
            double sum = 0.0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return sum;
        }
    }
}
