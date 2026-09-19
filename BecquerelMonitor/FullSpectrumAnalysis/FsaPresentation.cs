using System;
using System.Collections.Generic;
using System.Drawing;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Группировка строк результата (`A145`, блок «Группировка результата»).
    /// Настройка ПРЕДСТАВЛЕНИЯ: не входит в отпечаток расчёта и не запускает
    /// разбор (критерий 7). Умолчание — дочерние: оно сохраняет нынешний вид.
    /// </summary>
    public enum FsaGrouping
    {
        /// <summary>Строки членов ряда как есть — нынешний вид.</summary>
        Daughters,

        /// <summary>
        /// Члены одного <see cref="FsaStackLayer.DecayChainRoot"/> слиты в
        /// одну строку корня. Допустимо только при связанном ряде
        /// (<see cref="FsaResult.ParentGroupingAllowed"/>); иначе показываются
        /// дочерние, а причина — в <see cref="FsaPresentation.ParentGroupingRefusalReason"/>.
        /// </summary>
        Parents
    }

    /// <summary>
    /// (`AMBER45`) РЕЖИМ ПОКАЗА СЛОЯ МАТРИЦЫ ОТКЛИКА — комбо «Matrix layer» в
    /// группе «Display» окна отчёта. Описание вида Amber 18.09.2026, дословно:
    /// «В FSA Report в группе Display добавить combo box: "Matrix layer".
    /// Значение по умолчанию - All (отображать как это выглядит сейчас). И
    /// доступные значения в этом комбо боксе - каждый слой из существующих.
    /// При его выборе происходит его отрисовка на спектре.»
    ///
    /// Настройка ПОКАЗА, как <see cref="FsaGrouping"/>: в отпечаток расчёта
    /// не входит, разбор не запускает, таблицу отчёта не меняет — меняется
    /// только стопка на графике (<c>EnergySpectrumView.Fsa.cs</c>).
    /// <see cref="All"/> — нынешняя картинка побитово; иное значение — НОМЕР
    /// КАНАЛА <see cref="EfficiencyMaker.EfficiencySimulator.ResponseChannel"/>,
    /// и стопка строится из <see cref="FsaStackLayer.ChannelCurves"/>[канал]
    /// тех же слоёв, тем же порядком и теми же цветами (уточнение Amber
    /// 18.09.2026 вопросником: «Стопка по компонентам, как сейчас»); слои без
    /// раскладки по каналам — фон, сплайн, рассеяние, наложения, серый
    /// «прочее», — а также разнесённая подложка слоёв в этом режиме не
    /// рисуются («Спрятать — только канал и спектр»). Отвязанный хвост слоя
    /// (`S175`) с 19.09.2026 (П112) рисуется В КАНАЛЕ <see cref="Compton"/> —
    /// решение Amber, дословно: «Класть хвост в слой Compton»; в остальных
    /// каналах его нет (<see cref="FsaMatrixLayers.CurveOf"/>).
    ///
    /// ⛔ Числа членов ПРИВЯЗАНЫ к номерам каналов симулятора, а не выбраны:
    /// `(int)` члена — индекс в `ChannelCurves`, и расхождение с
    /// `ResponseChannel` дало бы стопку НЕ ТОГО канала под верной подписью.
    /// Список членов — <see cref="FsaMatrixLayers.Channels"/>, и его длину
    /// проба `FsaChannelViewProbe` сверяет с `ResponseChannelCount`.
    /// </summary>
    public enum FsaMatrixLayer
    {
        /// <summary>Все каналы — стопка лент как есть (умолчание).</summary>
        All = -1,

        /// <summary>Полное поглощение.</summary>
        Peak = (int)EfficiencyMaker.EfficiencySimulator.ResponseChannel.Peak,

        /// <summary>Неполное поглощение: утечка рассеянного кванта, электрона, тормозного.</summary>
        Compton = (int)EfficiencyMaker.EfficiencySimulator.ResponseChannel.Compton,

        /// <summary>Одиночный вылет аннигиляции (пик на E − 511).</summary>
        EscapeAnnihilation = (int)EfficiencyMaker.EfficiencySimulator.ResponseChannel.EscapeAnnihilation,

        /// <summary>Вылет K-рентгена кристалла.</summary>
        EscapeXrayK = (int)EfficiencyMaker.EfficiencySimulator.ResponseChannel.EscapeXrayK,

        /// <summary>Двойной вылет аннигиляции (пик на E − 1022).</summary>
        EscapeAnnihilationDouble = (int)EfficiencyMaker.EfficiencySimulator.ResponseChannel.EscapeAnnihilationDouble,

        /// <summary>Вылет L-рентгена кристалла.</summary>
        EscapeXrayL = (int)EfficiencyMaker.EfficiencySimulator.ResponseChannel.EscapeXrayL
    }

    /// <summary>
    /// (`AMBER45`) Правила режима слоя матрицы — ОДНИ на график, окно отчёта и
    /// пробы: какой слой рисуется, какой кривой, и достижим ли режим вовсе.
    /// Второй копии этих правил в отрисовке быть не должно (по тому же
    /// доводу, что у <see cref="FsaPresentationBuilder"/>: две копии однажды
    /// разойдутся, и стопка разошлась бы с тем, что говорит окно).
    /// </summary>
    public static class FsaMatrixLayers
    {
        /// <summary>
        /// Каналы по номеру — порядок пунктов комбо после «All». Длина равна
        /// <see cref="EfficiencyMaker.EfficiencySimulator.ResponseChannelCount"/>
        /// (проверяет проба `FsaChannelViewProbe`).
        /// </summary>
        public static readonly FsaMatrixLayer[] Channels =
        {
            FsaMatrixLayer.Peak,
            FsaMatrixLayer.Compton,
            FsaMatrixLayer.EscapeAnnihilation,
            FsaMatrixLayer.EscapeXrayK,
            FsaMatrixLayer.EscapeAnnihilationDouble,
            FsaMatrixLayer.EscapeXrayL
        };

        /// <summary>
        /// Есть ли в стопке хоть один слой с раскладкой по каналам. Нет —
        /// матрицы у спектра нет (или образы построены не по ней), режим слоя
        /// недостижим: комбо в окне гаснет с подсказкой, график рисует
        /// <see cref="FsaMatrixLayer.All"/>, что бы ни просили.
        /// </summary>
        public static bool HasChannels(IList<FsaStackLayer> layers)
        {
            if (layers == null)
            {
                return false;
            }

            foreach (FsaStackLayer layer in layers)
            {
                if (layer != null && layer.ChannelCurves != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Рисуется ли слой в этом режиме: при «All» — всякий; при канале —
        /// только слой с раскладкой по каналам (у фона, сплайна, рассеяния,
        /// наложений, серого «прочего» её нет — они и есть то, что решение
        /// Amber велит спрятать).
        /// </summary>
        public static bool IsDrawn(FsaStackLayer layer, FsaMatrixLayer mode)
        {
            return layer != null && (mode == FsaMatrixLayer.All || layer.ChannelCurves != null);
        }

        /// <summary>
        /// Кривая, которой слой рисуется в этом режиме: при «All» — лента
        /// <see cref="FsaStackLayer.Curve"/>; при канале —
        /// <see cref="FsaStackLayer.ChannelCurves"/>[канал]. null — рисовать
        /// нечего: канала у слоя нет (старая матрица с меньшим числом каналов
        /// отдаёт пустой канал — рисуется пусто, не лента и не отказ).
        ///
        /// ⛔ (П112, 19.09.2026) КАНАЛ <see cref="FsaMatrixLayer.Compton"/>
        /// РИСУЕТСЯ ВМЕСТЕ С ОТВЯЗАННЫМ ХВОСТОМ СЛОЯ
        /// (<see cref="FsaStackLayer.TailCurve"/>, `S175`): кривая = канал +
        /// хвост, новым массивом. Вопрос Amber 19.09.2026, дословно: «Почему
        /// комптон на цезии имеет такую просадку в районе рентгена свинца?»;
        /// решение вопросником того же дня, дословно: «Класть хвост в слой
        /// Compton». Причина провала: ниже порога доверия матрицы континуум
        /// образа идёт в фит отвязанным хвостом, и нож вынимает те же бины из
        /// каналов исхода — в «All» хвост входит в ленту слоя, а в одиночном
        /// канале ему было некуда лечь, и слой Cs-137 в 60–100 кэВ проваливался
        /// на две декады (П109). По физике хвост — то же комптоновское плато
        /// образа ниже порога (описание <see cref="FsaComponentResult.TailCurve"/>),
        /// поэтому его место — в канале комптона. Сами
        /// <see cref="FsaStackLayer.ChannelCurves"/> НЕ трогаются: их
        /// тождество Σ каналов = лента − подложка − хвост стоит как было, и
        /// на нём стоят пробы; складывается только кривая ДЛЯ ОТРИСОВКИ, и
        /// только здесь — второй копии этого правила в отрисовке быть не должно.
        /// Остальные каналы и «All» — без изменений.
        /// </summary>
        public static double[] CurveOf(FsaStackLayer layer, FsaMatrixLayer mode)
        {
            if (layer == null)
            {
                return null;
            }

            if (mode == FsaMatrixLayer.All)
            {
                return layer.Curve;
            }

            int channel = (int)mode;
            double[] curve = layer.ChannelCurves != null && channel >= 0 && channel < layer.ChannelCurves.Length
                ? layer.ChannelCurves[channel]
                : null;

            if (mode != FsaMatrixLayer.Compton || curve == null || layer.TailCurve == null)
            {
                return curve;
            }

            // Канал + хвост — новым массивом: канал слоя остаётся тем, чем
            // был (тождество каналов), хвост остаётся у слоя отдельной записью.
            double[] tail = layer.TailCurve;
            double[] drawn = new double[Math.Max(curve.Length, tail.Length)];
            for (int i = 0; i < drawn.Length; i++)
            {
                drawn[i] = (i < curve.Length ? curve[i] : 0.0) + (i < tail.Length ? tail[i] : 0.0);
            }

            return drawn;
        }

        /// <summary>
        /// (П112) Входит ли отвязанный хвост слоя в его кривую в этом режиме:
        /// при «All» — да, лентой (<see cref="FsaStackLayer.Curve"/> его уже
        /// несёт); при канале — только у <see cref="FsaMatrixLayer.Compton"/>
        /// (см. <see cref="CurveOf"/>). Одно место ответа на вопрос «куда
        /// делся хвост в этом режиме» — для отрисовки, проб и витрины.
        /// </summary>
        public static bool TailDrawn(FsaMatrixLayer mode)
        {
            return mode == FsaMatrixLayer.All || mode == FsaMatrixLayer.Compton;
        }

        /// <summary>Слои, которые рисуются в этом режиме, — в порядке стопки.</summary>
        public static List<FsaStackLayer> Drawn(IList<FsaStackLayer> layers, FsaMatrixLayer mode)
        {
            var drawn = new List<FsaStackLayer>();
            if (layers == null)
            {
                return drawn;
            }

            foreach (FsaStackLayer layer in layers)
            {
                if (IsDrawn(layer, mode))
                {
                    drawn.Add(layer);
                }
            }

            return drawn;
        }
    }

    /// <summary>
    /// Семь смысловых родов строк отчёта (`A145`, «Таблица отчёта»), плюс
    /// служебная строка состояния. Смысл строки читается ОТСЮДА, а не обратным
    /// разбором локализованного текста.
    /// </summary>
    public enum FsaReportRowKind
    {
        /// <summary>Строка состояния: «считается», причина отказа. Результата нет.</summary>
        Status,

        /// <summary>Обнаруженный слой состава: цвет и доля.</summary>
        Layer,

        /// <summary>Штриховка сумм-пиков слоя, без второй доли.</summary>
        SumPeaks,

        /// <summary>Именованный необнаруженный кандидат серым, «&lt; … %».</summary>
        Undetected,

        /// <summary>Одна серая строка свёрнутых низковыходных необнаруженных (`S69`).</summary>
        UndetectedFolded,

        /// <summary>Красная строка «без фона» (`S44`).</summary>
        NoBackground,

        /// <summary>Невязка с клетчатым образцом и знаковыми числами (`S111`).</summary>
        Residual,

        /// <summary>Строка качества: полный текст пометок и χ²/ndf.</summary>
        Quality
    }

    /// <summary>Образец в узкой колонке отчёта.</summary>
    public enum FsaSwatchKind
    {
        None,

        /// <summary>Сплошной цвет слоя.</summary>
        Solid,

        /// <summary>Штрих сумм-пиков в цвете слоя.</summary>
        SumPeakHatch,

        /// <summary>Клетка невязки (чёрная половина, `A28`).</summary>
        ResidualCross
    }

    /// <summary>
    /// Типизированная строка отчёта: одна и та же для будущей XPTable и для
    /// временной таблицы на графике. Текст уже локализован и отформатирован
    /// текущей культурой; усечения здесь нет и быть не может — это модель, а
    /// не отрисовка.
    /// </summary>
    public sealed class FsaReportRow
    {
        public FsaReportRowKind Kind { get; set; }

        /// <summary>Локализованное имя строки (колонка «Компонент»).</summary>
        public string Name { get; set; }

        /// <summary>Доля, предел, невязка или χ²/ndf (колонка «Значение»); пусто — нет.</summary>
        public string Value { get; set; }

        public FsaSwatchKind Swatch { get; set; }

        /// <summary>Цвет образца; у строк без образца не читается.</summary>
        public Color Color { get; set; }

        /// <summary>Слой, из которого строка сделана (Layer, SumPeaks); иначе null.</summary>
        public FsaStackLayer Layer { get; set; }

        /// <summary>Серый текст: у кандидата нет ленты и нет цвета (`S9`).</summary>
        public bool Muted { get; set; }

        /// <summary>Красный текст: предупреждение (`S44`).</summary>
        public bool Warning { get; set; }

        /// <summary>
        /// (`AMBER6`) Подсказка строки — то, чего в самой строке не помещается.
        /// Пусто — подсказкой служит сам текст строки, как было.
        ///
        /// Заведено ради свёрнутой строки пределов: вопрос Amber 08.09.2026
        /// «Выключено равновесие у Ra-226 цепи. Где радон?» — радон судится и
        /// предел у него посчитан, но в таблице он попадает в безымянное
        /// «не определяются (3)» (свёртка по порогу выхода, `S69`/`S74`), и
        /// назвать свёрнутых было негде.
        /// </summary>
        public string Hint { get; set; }

        /// <summary>
        /// Строка уступает место первой при нехватке высоты (временная таблица
        /// на графике, `S73`); строки невязки, качества и «без фона» не
        /// сворачиваются никогда. У XPTable с полосой прокрутки понятия нет.
        /// </summary>
        public bool Collapsible
        {
            get
            {
                return this.Kind == FsaReportRowKind.Layer
                       || this.Kind == FsaReportRowKind.SumPeaks
                       || this.Kind == FsaReportRowKind.Undetected
                       || this.Kind == FsaReportRowKind.UndetectedFolded;
            }
        }
    }

    /// <summary>
    /// ОДИН СНИМОК ПРЕДСТАВЛЕНИЯ разбора (`A145`, «Единая модель
    /// представления»): один список слоёв задаёт и цвет ленты на графике, и
    /// образец в таблице, и порядок строк, и свёртку «прочих», и группировку.
    /// Собирать легенду отдельно от графика нельзя — после переключения
    /// родителей/дочерних цвет, имя или доля разошлись бы.
    /// </summary>
    public sealed class FsaPresentation
    {
        /// <summary>Результат, из которого построено; null у строки состояния.</summary>
        public FsaResult Source { get; set; }

        /// <summary>Что просили.</summary>
        public FsaGrouping RequestedGrouping { get; set; }

        /// <summary>Что применено: родители только при <see cref="ParentGroupingAllowed"/>.</summary>
        public FsaGrouping Grouping { get; set; }

        public bool ParentGroupingAllowed { get; set; }

        /// <summary>
        /// Почему родители недоступны, КОДОМ (`A184`);
        /// <see cref="FsaParentGroupingRefusal.None"/> — доступны. Текста здесь
        /// нет нарочно: подпись собирает вид из `Resources.*`, в культуре
        /// интерфейса. Служебная русская фраза для журнала и проб осталась у
        /// модели — <see cref="FsaResult.ParentGroupingRefusal"/>.
        /// </summary>
        public FsaParentGroupingRefusal ParentGroupingRefusalReason { get; set; }

        /// <summary>Признак «старая матрица» на момент сборки (`A50`).</summary>
        public bool MatrixOldFormat { get; set; }

        /// <summary>Слои стека в порядке отрисовки снизу вверх — он же порядок строк состава.</summary>
        public List<FsaStackLayer> Layers { get; set; }

        /// <summary>Цвета слоёв, розданные один раз на весь список (<see cref="FsaPalette.Assign"/>).</summary>
        public Dictionary<string, Color> Colors { get; set; }

        /// <summary>Строки отчёта в порядке показа.</summary>
        public List<FsaReportRow> Rows { get; set; }

        /// <summary>Полный текст строки качества (без усечения).</summary>
        public string QualityText { get; set; }

        public FsaPresentation()
        {
            this.Layers = new List<FsaStackLayer>();
            this.Colors = new Dictionary<string, Color>();
            this.Rows = new List<FsaReportRow>();
        }

        /// <summary>Цвет слоя из раздачи; серый — слоя нет.</summary>
        public Color ColorOf(string name)
        {
            Color color;
            return this.Colors != null && name != null && this.Colors.TryGetValue(name, out color)
                ? color
                : Color.Gray;
        }

        /// <summary>Сколько строк уступают место первыми (`S73`).</summary>
        public int CollapsibleRowCount
        {
            get
            {
                int count = 0;
                foreach (FsaReportRow row in this.Rows)
                {
                    if (row.Collapsible)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Сколько строк не сворачиваются никогда.</summary>
        public int FixedRowCount
        {
            get
            {
                return this.Rows.Count - this.CollapsibleRowCount;
            }
        }
    }
}
