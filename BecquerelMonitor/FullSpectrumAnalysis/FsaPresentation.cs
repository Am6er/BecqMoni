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
