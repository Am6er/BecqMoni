using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using XPTable.Events;
using XPTable.Models;
using XPTable.Renderers;

namespace BecquerelMonitor
{
    /// <summary>
    /// ОКНО ОТЧЁТА ПОЛНОСПЕКТРАЛЬНОГО РАЗЛОЖЕНИЯ (`A145`, этап 3) — DockPanel-вид
    /// рядом с остальными <c>DC*View</c>. Имя класса — с прописным <c>FSA</c>,
    /// по документу `handover/a145-fsa-display-groups.md`.
    ///
    /// Сверху вниз: четыре группы параметров — источник состава (две
    /// радиокнопки), группировка результата (родители/дочерние), модель
    /// цепочек (равновесие) и пять дополнительных компонентов модели, — и
    /// XPTable на весь остаток: перечень слоёв с долей, строки
    /// сумм-пиков, необнаруженные кандидаты, «без фона», невязка и строка
    /// качества (семь родов строк <see cref="FsaReportRowKind"/> плюс
    /// строка состояния).
    ///
    /// ⛔ ОКНО НЕ ВЛАДЕЕТ НИ РАСЧЁТОМ, НИ РЕЗУЛЬТАТОМ. Сеанс разбора
    /// (<see cref="FsaAnalysisSession"/>) принадлежит документу
    /// (<see cref="DocEnergySpectrum.FsaSession"/>); окно на него ПОДПИСАНО,
    /// как и график, и строит строки тем же вызовом
    /// <see cref="FsaPresentationBuilder.Build"/> из того же снимка результата —
    /// второго расчёта и второго кэша нет (критерий 4). Скрытие окна расчёта
    /// не обрывает, результата не сбрасывает и режима графика не меняет.
    ///
    /// Окно — САМО ПОТРЕБИТЕЛЬ разложения: пока оно видимо, оно заказывает
    /// расчёт для активного спектра и тогда, когда график не в режиме
    /// <c>ShowFSA</c>. Когда потребителя нет (окно скрыто и график не в FSA),
    /// смена параметра только обесценивает кэш сеанса — тяжёлый счёт
    /// откладывается до появления потребителя.
    ///
    /// ДВЕ ОСИ ПЕРЕКЛЮЧАТЕЛЕЙ, и смешивать их нельзя. С 06.09.2026
    /// (`A265`, решение Amber) они РАЗВЕДЕНЫ ПО ГРУППАМ окна, а не
    /// только по подсказкам: расчётные — «Источник состава», «Модель
    /// цепочек» и «Дополнительные компоненты модели»; отрисовочные —
    /// «Отрисовка» внизу. Прежде лента невязки стояла шестой в ряду
    /// пяти расчётных, и различала их одна подсказка при наведении.
    ///   * расчётные (источник, равновесие, пять компонентов) — с `AMBER212`
    ///     (07.10.2026) ОДНА настройка приложения
    ///     (<see cref="GlobalConfigInfo.FsaCalculation"/>, см.
    ///     <see cref="ApplyCalculationChange"/>), входят в отпечаток сеанса и
    ///     ведут к ОДНОМУ актуальному пересчёту (критерий 7); прежнее правило
    ///     «умолчание прибора, соседи не переписываются» (`S70`, критерий 11)
    ///     снято решением Amber;
    ///   * группировка родители/дочерние — настройка ПРЕДСТАВЛЕНИЯ этого окна
    ///     на время работы приложения: в файл, конфигурацию и отпечаток не
    ///     входит, расчёт не запускает; одно значение на документ
    ///     (<see cref="DocEnergySpectrum.FsaGrouping"/>) читают и график, и
    ///     таблица;
    ///   * (`AMBER45`, 18.09.2026) слой матрицы отклика — комбо «Matrix layer»
    ///     в той же группе «Display»: «All» (стопка как есть) или один канал;
    ///     настройка ПОКАЗА графика на время работы приложения, таблицу не
    ///     меняет, счёт не заказывает (<see cref="requestedMatrixLayer"/>).
    ///
    /// Родительский режим доступен только при источнике «Из NucBase» с
    /// включённым равновесием (условие настроек,
    /// <see cref="FsaCalculationOptions.ParentGroupingPossible"/>) И когда в
    /// результате есть связанный ряд (<see cref="FsaResult.ParentGroupingAllowed"/>).
    /// Когда сочетание недопустимо, радиокнопка гаснет и показываются дочерние,
    /// а просьба человека помнится (<see cref="requestedGrouping"/>) и
    /// восстанавливается, когда сочетание снова допустимо.
    /// </summary>
    public partial class FSAReportView : ToolWindow
    {
        readonly MainForm mainForm;

        /// <summary>Документ, чей сеанс показывается; null — активного спектра нет.</summary>
        DocEnergySpectrum document;

        /// <summary>Сеанс, на <see cref="FsaAnalysisSession.Completed"/> которого окно подписано.</summary>
        FsaAnalysisSession session;

        /// <summary>Что просил человек: помнится и тогда, когда родители недоступны.</summary>
        FsaGrouping requestedGrouping = FsaGrouping.Daughters;

        /// <summary>Элементы управления выставляются кодом — обработчики молчат.</summary>
        bool loading;

        /// <summary>Снимок представления, из которого заполнена таблица; null — результата нет.</summary>
        FsaPresentation presentation;

        /// <summary>
        /// (`A250`) ПОТОК ОКОН, на котором окно живёт. Событие
        /// <see cref="FsaAnalysisSession.Completed"/> приходит из ФОНОВОГО
        /// потока, а строки таблицы — состояние окна; читать результат надо
        /// ЗДЕСЬ, и только здесь. Пока у окна есть ручка, за это отвечает
        /// <c>BeginInvoke</c>; у окна без ручки (пробы) ручки нет, и
        /// единственная оставшаяся дверь на этот поток — его контекст
        /// синхронизации, снятый при создании.
        /// </summary>
        SynchronizationContext uiContext;

        /// <summary>
        /// Пробы: окно никогда не показывается, а потребителем быть обязано —
        /// иначе критерии «окно заказывает расчёт» не измерить без экрана.
        /// В приложении всегда false: потребитель определяется видимостью.
        /// </summary>
        public bool ProbeConsumer { get; set; }

        /// <summary>Образцы перед подписью строки, по одному на (род, цвет).</summary>
        readonly Dictionary<string, Image> swatches = new Dictionary<string, Image>(StringComparer.Ordinal);

        /// <summary>Образец перед подписью: ширина, высота и зазор до текста, пикселей.</summary>
        const int SwatchWidth = 14;
        const int SwatchHeight = 10;
        const int SwatchGap = 4;

        /// <summary>
        /// (`AMBER286`) Шаг отступа подписи (<see cref="FsaReportRow.Indent"/>), пикселей: на
        /// один образец с зазором — строка пика встаёт текстом под текст строки нуклида и ещё
        /// на шаг правее.
        /// </summary>
        const int IndentStep = SwatchWidth + SwatchGap;

        // ------------------------------------------------------------------
        // (`AMBER320`) СВЁРТКА СТРОК-ПОТОМКОВ
        // ------------------------------------------------------------------

        /// <summary>
        /// (`AMBER320`) Место стрелки свёртки перед подписью родителя, пикселей: треугольник
        /// <see cref="ArrowGlyph"/> и зазор до текста. Образец родителя остаётся на своём месте
        /// (под образцами строк состава), стрелка встаёт между ним и текстом.
        /// </summary>
        const int ArrowWidth = 12;

        /// <summary>(`AMBER320`) Сторона треугольника стрелки, пикселей.</summary>
        const int ArrowGlyph = 8;

        /// <summary>
        /// ⛔ (`AMBER320`, решение Amber 07.11.2026 вопросником, дословно: «Помнить до конца
        /// сеанса (Рекомендую)») РАСКРЫТЫЕ ГРУППЫ — на процесс, а не на окно и не на документ:
        /// раскрытая группа остаётся раскрытой при пересчёте и смене документа, пока программа
        /// открыта (окно отчёта бывает пересоздано сбросом раскладки — поле статическое); при
        /// запуске пусто, то есть всё свёрнуто. Ключ — <see cref="GroupKey"/>. Только поток окон.
        /// </summary>
        static readonly HashSet<string> ExpandedGroups = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// (`AMBER320`) Группа «родитель и его потомки» одной перестройки таблицы. Висит на
        /// <see cref="Cell.Tag"/> ячейки имени родителя: оба вида строки — свёрнутый и
        /// раскрытый — собраны один раз, переключение только меняет текст ячеек.
        /// </summary>
        sealed class RowGroup
        {
            public string Key;
            public int Hidden;
            public string ExpandedCaption, ExpandedValue, ExpandedHint;
            public string CollapsedCaption, CollapsedValue, CollapsedHint;
        }

        /// <summary>
        /// (`AMBER320`) Ключ памяти раскрытия: род строки родителя и имя её слоя (у строки без
        /// слоя — её подпись). Род различает «сумм-пик», «пики вылета» и «обратное рассеяние»
        /// одного нуклида.
        /// </summary>
        static string GroupKey(FsaReportRow parent)
        {
            return parent.Kind + "|" + (parent.Layer != null ? parent.Layer.Name : parent.Name ?? string.Empty);
        }

        /// <summary>
        /// (`AMBER320`, второй заход) Род строки-родителя под образцом — «— сумм-пик», «— пики вылета»,
        /// «— обратное рассеяние»: место стрелки у неё есть всегда, со стрелкой или без, чтобы
        /// тексты этих строк стояли в одну линию.
        /// </summary>
        static bool ReservesArrow(FsaReportRow row)
        {
            return row != null && (row.Kind == FsaReportRowKind.SumPeaks || row.Kind == FsaReportRowKind.EscapePeaks
                                   || row.Kind == FsaReportRowKind.Backscatter);
        }

        /// <summary>(`AMBER320`) Пробы: забыть раскрытые группы — как при запуске программы.</summary>
        public static void ForgetExpandedGroups()
        {
            ExpandedGroups.Clear();
        }

        /// <summary>Ширина колонки значения, пикселей: «+12,3 / −45,6 %» помещается с запасом.</summary>
        const int ValueColumnWidth = 92;

        // ------------------------------------------------------------------
        // (`A246`) ВЫДЕЛЕНИЕ КОМПОНЕНТА
        // ------------------------------------------------------------------

        /// <summary>
        /// Имя слоя (<see cref="FsaStackLayer.Name"/>) выбранной строки состава;
        /// null — выбора нет либо выбрана строка без ленты. Это ЕДИНСТВЕННОЕ,
        /// что окно говорит графику о выборе.
        /// </summary>
        string selectedLayer;

        /// <summary>
        /// (`AMBER327`) Выбранная строка — строка ЧАСТИ ленты своего слоя («— сумм-пик», «— пики
        /// вылета», «— обратное рассеяние» или их потомок): какая часть и окно потомка в каналах
        /// (−1 — часть целиком). <see cref="FsaLayerPart.None"/> — строка выделяет слой целиком.
        /// Род и подпись строки — чтобы перестройка таблицы вернула выбор на ТУ ЖЕ строку, а не на
        /// строку слоя (<see cref="RestoreSelection"/>).
        /// </summary>
        FsaLayerPart selectedPart;
        int selectedFirst = -1, selectedLast = -1;
        FsaReportRowKind selectedKind;
        string selectedName;

        /// <summary>
        /// Перестройка таблицы сама двигает выбор (строки чистятся и создаются
        /// заново), и XPTable честно поднимает на это событие. Пока флаг
        /// поднят, событие не читается: иначе каждый фоновый пересчёт снимал бы
        /// выбор человека, а по дороге ещё и слал бы графику null.
        /// </summary>
        bool suspendSelection;

        /// <summary>
        /// (П290) Что окно САМО сказало графику в последний раз: вид и имя слоя
        /// (признак ряда у окна отчёта всегда «нет»). По ним перестройка таблицы
        /// узнаёт, чья подсветка сейчас на графике (<see cref="HighlightIsOwn"/>).
        /// (П315, 15.10.2026) Вид — СЛАБОЙ ссылкой: нужен он только для сравнения «тот
        /// же ли», а сильная держала ЗАКРЫТЫЙ документ (вид → его событие → документ со
        /// спектром и разбором) до следующего щелчка в отчёте — мерено
        /// `P315GdiLeakProbe --mode=docs`: после закрытия жив последний документ, путь
        /// `dcFsaReportView.pushedView → EnergySpectrumView.ActionEvent → документ`.
        /// </summary>
        WeakReference<EnergySpectrumView> pushedView;
        string pushedLayer;

        /// <summary>(`AMBER327`) Часть ленты и её окно, сказанные графику вместе с <see cref="pushedLayer"/>.</summary>
        FsaLayerPart pushedPart;
        int pushedFirst = -1, pushedLast = -1;

        // ------------------------------------------------------------------
        // (`A248`) ПОКАЗ ЛЕНТЫ НЕВЯЗКИ
        // ------------------------------------------------------------------

        /// <summary>
        /// (`A248`, задача Amber 05.09.2026) Показывать ли ленту НЕВЯЗКИ на
        /// графике. Настройка ПОКАЗА, а не расчёта: она не пишется ни в копию
        /// спектра, ни в умолчание прибора, не входит в отпечаток разбора
        /// (<see cref="FsaCalculationOptions"/>) и пересчёта не заказывает —
        /// одна перерисовка графика, как у выделения `A246`.
        ///
        /// ⛔ Строка невязки и χ²/ndf в ТАБЛИЦЕ от неё не зависят вовсе
        /// (решение Amber 05.09.2026): мера качества разбора видна при любом
        /// положении галочки, гасится только лента на графике. Поэтому здесь
        /// нет ни <see cref="RefreshReport"/>, ни чего-либо ещё, что трогало бы
        /// строки.
        ///
        /// Живёт в ОКНЕ, а не в документе: окно одно на приложение
        /// (<see cref="MainForm"/>), и положение галочки держится при переходе
        /// от спектра к спектру — так же, как просьба о группировке
        /// (<see cref="requestedGrouping"/>).
        /// </summary>
        bool showResidualBand = true;

        /// <summary>Ключ подсказки шестой галочки: «меняет только показ».</summary>
        const string KeyResidualBandTip = "FSAReport_ResidualBandTip";

        // ------------------------------------------------------------------
        // (`AMBER222`) ПОКАЗ ДАННЫХ СПЕКТРА — галочка «Spectral data»
        // ------------------------------------------------------------------

        /// <summary>
        /// (`AMBER222`, задача Amber 12.10.2026) Показывать ли на графике FSA
        /// линию измеренного спектра (<see cref="EnergySpectrumView.FsaShowSpectralData"/>).
        /// Настройка ПОКАЗА той же природы, что <see cref="showResidualBand"/>: в
        /// отпечаток разбора не входит, пересчёта не заказывает, таблицу не
        /// меняет. Живёт в окне и едет на график показываемого документа.
        ///
        /// ⛔ Обе галочки показа ПИШУТСЯ НА ДИСК (решение Amber 12.10.2026
        /// вопросником, дословно: «На диск: Spectral data + Model residual
        /// (Рекомендую)») — в общие настройки (<see cref="GlobalConfigInfo.FsaShowSpectralData"/>,
        /// <see cref="GlobalConfigInfo.FsaShowResidual"/>), как семь расчётных
        /// (`AMBER212`), и переживают перезапуск. Группировка и слой матрицы —
        /// по-прежнему только в окне.
        /// </summary>
        bool showSpectralData = true;

        /// <summary>Ключ подсказки галочки «Spectral data»: «меняет только показ».</summary>
        const string KeySpectralDataTip = "FSAReport_SpectralDataTip";

        // ------------------------------------------------------------------
        // (`AMBER45`) СЛОЙ МАТРИЦЫ ОТКЛИКА — комбо «Matrix layer»
        // ------------------------------------------------------------------

        /// <summary>
        /// (`AMBER45`, задача Amber 15.09.2026, вид описан ею 18.09.2026:
        /// «В FSA Report в группе Display добавить combo box: "Matrix layer".
        /// Значение по умолчанию - All (отображать как это выглядит сейчас). И
        /// доступные значения в этом комбо боксе - каждый слой из
        /// существующих. При его выборе происходит его отрисовка на спектре.»)
        /// ЧТО ПРОСИЛ ЧЕЛОВЕК: все каналы или один. Помнится и тогда, когда у
        /// спектра нет матрицы и режим недостижим (комбо погашено, показывает
        /// «All») — на следующем спектре с матрицей просьба вступает в силу,
        /// как просьба о родителях (<see cref="requestedGrouping"/>).
        ///
        /// Настройка ПОКАЗА, а не расчёта, — той же природы, что
        /// <see cref="showResidualBand"/>: не пишется ни в копию спектра, ни в
        /// умолчание прибора, в отпечаток разбора не входит, пересчёта не
        /// заказывает и ТАБЛИЦУ НЕ МЕНЯЕТ (фит не пересчитывается — доли,
        /// невязка и χ²/ndf те же при любом слое). Меняется одна стопка на
        /// графике: у каждого компонента рисуется только выбранный канал
        /// (<see cref="EnergySpectrumView.FsaMatrixLayer"/>; канал комптона —
        /// вместе с отвязанным хвостом компонента, П112 19.09.2026, правило —
        /// <see cref="FsaMatrixLayers.CurveOf"/>). Живёт в ОКНЕ и
        /// переезжает на график того документа, который показывается сейчас.
        /// </summary>
        FsaMatrixLayer requestedMatrixLayer = FsaMatrixLayer.All;

        /// <summary>Подсказка комбо при живой матрице: «меняет только график».</summary>
        const string KeyMatrixLayerTip = "FSAReport_MatrixLayerTip";

        /// <summary>Подсказка погашенного комбо: матрицы нет — каналов у модели нет.</summary>
        const string KeyMatrixLayerNoMatrixTip = "FSAReport_MatrixLayerNoMatrixTip";

        /// <summary>
        /// Ключи ПОДПИСЕЙ пунктов комбо — по одному на член
        /// <see cref="FsaMatrixLayer"/>, литералами (их ищут сторожа `resx`).
        /// Имя канала человеку даётся здесь, а не именем члена перечисления:
        /// `Compton` в коде — служебное имя канала неполного поглощения
        /// (утечка рассеянного кванта, электрона, тормозного), и подпись
        /// обязана сказать это по-человечески на обоих языках.
        /// </summary>
        static string MatrixLayerKey(FsaMatrixLayer layer)
        {
            switch (layer)
            {
                case FsaMatrixLayer.Peak: return "FSAReport_MatrixLayerPeak";
                case FsaMatrixLayer.Compton: return "FSAReport_MatrixLayerCompton";
                case FsaMatrixLayer.EscapeAnnihilation: return "FSAReport_MatrixLayerEscapeAnnihilation";
                case FsaMatrixLayer.EscapeXrayK: return "FSAReport_MatrixLayerEscapeXrayK";
                case FsaMatrixLayer.EscapeAnnihilationDouble: return "FSAReport_MatrixLayerEscapeAnnihilationDouble";
                case FsaMatrixLayer.EscapeXrayL: return "FSAReport_MatrixLayerEscapeXrayL";
                case FsaMatrixLayer.AnnihilationOutside: return "FSAReport_MatrixLayerAnnihilationOutside";
                case FsaMatrixLayer.SampleBeta: return "FSAReport_MatrixLayerSampleBeta";      // (`AMBER274`)
                default: return "FSAReport_MatrixLayerAll";
            }
        }

        /// <summary>
        /// Подпись пункта комбо для слоя — на языке интерфейса (пробы и снимок
        /// витрины читают её отсюда же, а не собирают свою).
        /// </summary>
        public static string MatrixLayerText(FsaMatrixLayer layer)
        {
            return OwnText(MatrixLayerKey(layer));
        }

        // ------------------------------------------------------------------
        // (`A247`) БЛОК «КАЧЕСТВО РАЗБОРА»: ключи собственных строк окна
        // ------------------------------------------------------------------
        //
        // ⛔ Строки живут в паре `FSAReportView.resx` / `FSAReportView.ru.resx`,
        //    а не в общем `Properties/Resources` — так же, как `NucBase`
        //    держит свою `NucBase_NoCriteria`: они принадлежат только этому
        //    окну и читаются тем же `ComponentResourceManager`, каким
        //    конструктор берёт подписи его же контролов. Пара проверяется
        //    `tools/check_resx.py` наравне с подписями.
        //
        // ⛔ Ключи НЕ ИМЕЮТ вида «контрол.свойство» и конструктору форм
        //    неизвестны: при перезаписи `resx` конструктором WinForms их легко
        //    потерять. Потеря видна сразу — в таблице встанет само имя ключа
        //    (<see cref="OwnText"/>), а не пустота.

        const string KeyQualityHeader = "FSAReport_QualityHeader";

        /// <summary>(`AMBER320`) Подпись свёрнутого родителя: «{0} ({1})» — подпись и число скрытых строк.</summary>
        const string KeyGroupCaption = "FSAReport_GroupCaption";

        /// <summary>(`AMBER320`) Подсказка свёрнутого родителя со суммой отсчётов; {0} — число скрытых строк.</summary>
        const string KeyGroupTipSum = "FSAReport_GroupTipSum";

        /// <summary>(`AMBER320`) Подсказка свёрнутого родителя без суммы (у потомков нет отсчётов числом).</summary>
        const string KeyGroupTipCount = "FSAReport_GroupTipCount";

        /// <summary>
        /// (`AMBER320`, третий заход) Формы единственного числа обеих подсказок — по правилу
        /// <see cref="OnePeakForm"/> (en: «1 row is hidden»; ru-текст согласования не требует и
        /// совпадает с общим).
        /// </summary>
        const string KeyGroupTipSumOne = "FSAReport_GroupTipSumOne";

        const string KeyGroupTipCountOne = "FSAReport_GroupTipCountOne";
        const string KeyResidualRow = "FSAReport_ResidualRow";

        /// <summary>
        /// (`A300`, решение Amber 10.09.2026 — «Число оставить, ленту
        /// подписать») Признак у строки невязки: НА СКОЛЬКИХ КАНАЛАХ ПОКАЗ
        /// ПОДРЕЗАН.
        /// </summary>
        const string KeyResidualClamped = "FSAReport_ResidualClamped";

        /// <summary>(`A300`) Подсказка к тому же признаку: почему число и лента расходятся.</summary>
        const string KeyResidualClampedTip = "FSAReport_ResidualClampedTip";

        /// <summary>
        /// (`S174`, решение Amber 14.09.2026 «Только в диапазоне прибора»)
        /// Признак у строки невязки: ОТ КАКОЙ ЭНЕРГИИ СЧИТАНО ЧИСЛО —
        /// `Min_Range` прибора (<see cref="FsaResult.ResidualFloorKev"/>).
        /// Лента на графике идёт по всей шкале, а число — от этого пола;
        /// ставится только когда пол и вправду режет полосу фита.
        /// </summary>
        const string KeyResidualFloor = "FSAReport_ResidualFloor";

        /// <summary>(`S174`) Подсказка к тому же признаку: ниже пола лента есть, в проценте её нет.</summary>
        const string KeyResidualFloorTip = "FSAReport_ResidualFloorTip";

        const string KeyChi2Row = "FSAReport_Chi2Row";

        /// <summary>
        /// (`A281`, решение Amber 10.09.2026 — «В блок „Качество разбора“ окна
        /// отчёта») МНОЖИТЕЛЬ ПОГРЕШНОСТЕЙ `σ×`.
        ///
        /// ⛔ Зачем он на экране. Значимости состава уже ПОДЕЛЕНЫ на этот
        /// множитель (<c>FsaAnalyzer.Fit</c>: `sigma[k] = √(inv(Gram)[k,k])·inflate`,
        /// `inflate = √(max(1, χ²/ndf))`), поэтому один и тот же порог значит
        /// на разных сценах разное: по малой базе (42 спектра понятной части,
        /// замеры 10.09.2026) медиана множителя 2.444, максимум 8.163
        /// (`G1S24_Eu152_P5`), единицей зажат 1 спектр из 42 — то есть порог 3
        /// означает от 3.0 до 24.5 СЫРЫХ сигм. Множитель решено НЕ ТРОГАТЬ
        /// (решение Amber того же дня), а показывать.
        ///
        /// ⚠ Ставится сразу под χ²/ndf, а не рядом со значимостью: значимости
        /// в этом окне нет вовсе — обстоятельство названо Amber прямо, и
        /// решение принято уже с ним.
        /// </summary>
        const string KeyInflationRow = "FSAReport_InflationRow";

        /// <summary>(`A281`) Подсказка к множителю: что именно он делает с порогом.</summary>
        const string KeyInflationTip = "FSAReport_InflationTip";

        const string KeyMatrixRow = "FSAReport_MatrixRow";
        const string KeyMatrixUsed = "FSAReport_MatrixUsed";
        const string KeyMatrixNotUsed = "FSAReport_MatrixNotUsed";

        /// <summary>
        /// (`AMBER284`, решение Amber 02.11.2026 вопросником, дословно: «Объединить со строкой
        /// выше, где уже написано - нет Матрицы отклика. Остальное убрать в подсказку.»)
        /// Подсказка строки «Матрица отклика: не учтена» — то, что прежде было отдельной
        /// строкой-предупреждением `FSAReport_NoMatrixWarnRow` (`AMBER156` (в), П195): цена
        /// разбора без матрицы словами, без жаргона.
        /// </summary>
        const string KeyMatrixNotUsedTip = "FSAReport_MatrixNotUsedTip";
        const string KeyMatrixOldFormat = "FSAReport_MatrixOldFormat";
        const string KeyMatrixStale = "FSAReport_MatrixStale";
        const string KeyMatrixStaleRow = "FSAReport_MatrixStaleRow";
        const string KeyMatrixStaleValue = "FSAReport_MatrixStaleValue";
        const string KeyEfficiencyRow = "FSAReport_EfficiencyRow";
        const string KeyEfficiencyUsed = "FSAReport_EfficiencyUsed";
        const string KeyEfficiencyNotUsed = "FSAReport_EfficiencyNotUsed";
        const string KeySummingRow = "FSAReport_SummingRow";
        const string KeySummingUsed = "FSAReport_SummingUsed";
        const string KeySummingNotUsed = "FSAReport_SummingNotUsed";

        /// <summary>
        /// (`AMBER9`) Суммирование не сделано ПОТОМУ ЧТО НЕТ МАТРИЦЫ — причина,
        /// а не только факт. Вопрос Amber 08.09.2026: «Куда делся суммарный пик
        /// на 519 keV?» У лютеция три кванта каскада (88, 202, 307 кэВ) дают
        /// сумму 202 + 307 = 509 кэВ, и на спектре она видна; модель её не
        /// строит. Физика: площадь сумм-пика — это две ПИКОВЫЕ эффективности,
        /// а убыль из одиночных фотопиков — ПОЛНАЯ, и полную вне матрицы взять
        /// негде. Считать приход без убыли нельзя: это лишние отсчёты из
        /// ничего. Поэтому суммирование целиком ждёт геометрии — и окно
        /// обязано это СКАЗАТЬ, а не молча опустить пик.
        /// </summary>
        const string KeySummingNoMatrix = "FSAReport_SummingNoMatrix";
        const string KeyDriftRow = "FSAReport_DriftRow";
        const string KeyDriftEdge = "FSAReport_DriftEdge";

        // (`AMBER17`) Привязка шкалы по пикам полного поглощения: подпись,
        // значение с числом опор и сдвигом, слово «нет опор», подсказка с
        // перечнем опор.
        // (`AMBER295`) автомаска фона: участки не в подгонке
        const string KeyBackgroundMaskRow = "FSAReport_BackgroundMaskRow";
        const string KeyBackgroundMaskRowOne = "FSAReport_BackgroundMaskRowOne";
        const string KeyBackgroundKnifeRow = "FSAReport_BackgroundKnifeRow";
        const string KeyBackgroundKnifeValue = "FSAReport_BackgroundKnifeValue";
        const string KeyBackgroundKnifeTip = "FSAReport_BackgroundKnifeTip";
        const string KeyBackgroundMaskValue = "FSAReport_BackgroundMaskValue";
        const string KeyBackgroundMaskValueOne = "FSAReport_BackgroundMaskValueOne";
        const string KeyBackgroundMaskTip = "FSAReport_BackgroundMaskTip";
        const string KeyAnchorRow = "FSAReport_AnchorRow";
        const string KeyAnchorNone = "FSAReport_AnchorNone";
        const string KeyAnchorGainRow = "FSAReport_AnchorGainRow";
        const string KeyAnchorZeroRow = "FSAReport_AnchorZeroRow";
        const string KeyAnchorTip = "FSAReport_AnchorTip";
        const string KeyAnchorTipLine = "FSAReport_AnchorTipLine";
        const string KeyAnchorTipLight = "FSAReport_AnchorTipLight";
        // (`AMBER269`, дефект 4) откат привязки (А2): слово в строке опор, строка с числами
        // отменённого разбора, её значение, шапка опор отменённого разбора в подсказке
        const string KeyAnchorRolledBack = "FSAReport_AnchorRolledBack";
        const string KeyAnchorRollbackRow = "FSAReport_AnchorRollbackRow";
        // (`AMBER284`) та же подпись с «по {0} пику» — для 1, 21, 31… (<see cref="OnePeakForm"/>)
        const string KeyAnchorRollbackRowOne = "FSAReport_AnchorRollbackRowOne";
        const string KeyAnchorRollbackValue = "FSAReport_AnchorRollbackValue";
        const string KeyAnchorRollbackTip = "FSAReport_AnchorRollbackTip";
        // (`AMBER294`) тормоз усиления: почему шкала не подстроена, значение строки, подсказка и её
        // шапка, причина в строке опоры, строка отката при тормозе («по 0 пикам» читать нечего)
        const string KeyAnchorBrakeRow = "FSAReport_AnchorBrakeRow";
        const string KeyAnchorBrakeRowOne = "FSAReport_AnchorBrakeRowOne";
        const string KeyAnchorBrakeValue = "FSAReport_AnchorBrakeValue";
        // (`AMBER294`, решение Amber 05.11.2026 вопросником, дословно: «Своя строка для карты (Рекомендую)»)
        // ветка «усиление стоит, а карта света движется»: без утверждения про неподвижную шкалу
        const string KeyAnchorBrakeMapRow = "FSAReport_AnchorBrakeMapRow";
        const string KeyAnchorBrakeMapRowOne = "FSAReport_AnchorBrakeMapRowOne";
        const string KeyAnchorBrakeMapValue = "FSAReport_AnchorBrakeMapValue";
        const string KeyAnchorBrakeMapTip = "FSAReport_AnchorBrakeMapTip";
        const string KeyAnchorBrakeMapTipHead = "FSAReport_AnchorBrakeMapTipHead";
        // (`AMBER294`) опора прошла замер, но не была в последнем шаге шкалы
        const string KeyAnchorNotInStepRefusal = "FSAReport_AnchorNotInStepRefusal";
        const string KeyAnchorBrakeTip = "FSAReport_AnchorBrakeTip";
        const string KeyAnchorBrakeTipHead = "FSAReport_AnchorBrakeTipHead";
        const string KeyAnchorBrakeRefusal = "FSAReport_AnchorBrakeRefusal";
        // (`AMBER294`, проверка экраном 07.11.2026) строка опоры без остатка (центр не измерен),
        // причина отдельным хвостом и слова прочих кодов отбора опор
        const string KeyAnchorTipLineNoShift = "FSAReport_AnchorTipLineNoShift";
        const string KeyAnchorTipRefusal = "FSAReport_AnchorTipRefusal";
        const string KeyAnchorEdgeRefusal = "FSAReport_AnchorEdgeRefusal";
        const string KeyAnchorSkipRefusal = "FSAReport_AnchorSkipRefusal";
        const string KeyAnchorNarrowRefusal = "FSAReport_AnchorNarrowRefusal";
        const string KeyAnchorZRefusal = "FSAReport_AnchorZRefusal";
        const string KeyAnchorShareRefusal = "FSAReport_AnchorShareRefusal";
        const string KeyAnchorShiftRefusal = "FSAReport_AnchorShiftRefusal";
        const string KeyAnchorOutlierRefusal = "FSAReport_AnchorOutlierRefusal";
        const string KeyAnchorBrakeRollbackRow = "FSAReport_AnchorBrakeRollbackRow";
        const string KeySuppressedRow = "FSAReport_SuppressedRow";
        const string KeyBackgroundRejectedRow = "FSAReport_BackgroundRejectedRow";

        // (`S187`) Расхождения поставок данных распада: подпись, шапка
        // подсказки и по строке на вид расхождения.
        const string KeySupplyRow = "FSAReport_SupplyRow";
        const string KeySupplyTip = "FSAReport_SupplyTip";
        const string KeySupplyBetaPlusMode = "FSAReport_SupplyBetaPlusMode";
        const string KeySupplyOtherMode = "FSAReport_SupplyOtherMode";
        const string KeySupplyExcess = "FSAReport_SupplyExcess";
        const string KeySupplyClamped = "FSAReport_SupplyClamped";
        const string KeySupplyShareScaled = "FSAReport_SupplyShareScaled";
        const string KeySupplyShareKept = "FSAReport_SupplyShareKept";

        // (`AMBER123`, `AMBER118`, `AMBER126`; П168 28.09.2026) Колонка наложений
        // на границе R·τ; помеха природного спутника линии состава; шумовой пол
        // строки невязки.
        const string KeyPileUpCapRow = "FSAReport_PileUpCapRow";
        const string KeyPileUpCapValue = "FSAReport_PileUpCapValue";
        const string KeyInterferenceRow = "FSAReport_InterferenceRow";
        const string KeyInterferenceValue = "FSAReport_InterferenceValue";
        // (`AMBER236`) изотопный состав элемента: строка, разброс членов / только статистика, приговор
        const string KeyIsotopeRow = "FSAReport_IsotopeRow";
        const string KeyIsotopeSpread = "FSAReport_IsotopeSpread";
        const string KeyIsotopeStatOnly = "FSAReport_IsotopeStatOnly";
        const string KeyIsotopeNatural = "FSAReport_IsotopeNatural";
        const string KeyIsotopeDepleted = "FSAReport_IsotopeDepleted";
        const string KeyIsotopeEnriched = "FSAReport_IsotopeEnriched";
        // (`AMBER210`) пик в полосе, не покрытый составом: строка, слово состояния, «рядом ничего»
        const string KeyUncoveredRow = "FSAReport_UncoveredRow";
        // (проверка экраном 07.11.2026) значение — «нет в составе», а не «нет в сете»: состав берётся
        // из меток пиков, NucBase или сета, и при наборе «Все изотопы» слово «сет» противоречило выбору;
        // строка и сама говорит «не объяснён нуклидами разбора»
        const string KeyUncoveredValue = "FSAReport_UncoveredValue";
        const string KeyUncoveredNone = "FSAReport_UncoveredNone";
        // (`AMBER269`) предупреждения по полосе: фон больше спектра (z — в подсказке); образы выше
        // всего спектра; нижняя граница разбора (строка блока и подсказки двух её родов)
        const string KeyBackgroundAboveRow = "FSAReport_BackgroundAboveRow";
        const string KeyBackgroundAboveValue = "FSAReport_BackgroundAboveValue";
        const string KeyBackgroundAboveTip = "FSAReport_BackgroundAboveTip";
        const string KeyImagesAboveRow = "FSAReport_ImagesAboveRow";
        const string KeyImagesAboveValue = "FSAReport_ImagesAboveValue";
        // (`AMBER284`) z «модель выше спектра» — в подсказку, не в подпись
        const string KeyImagesAboveTip = "FSAReport_ImagesAboveTip";
        const string KeyFitBoundRow = "FSAReport_FitBoundRow";
        const string KeyFitBoundTipSetting = "FSAReport_FitBoundTipSetting";
        const string KeyFitBoundTipRule = "FSAReport_FitBoundTipRule";
        // (`AMBER221`) родитель сета вне состава: строка, слово состояния, причины
        const string KeySetOmittedRow = "FSAReport_SetOmittedRow";
        const string KeySetOmittedValue = "FSAReport_SetOmittedValue";
        const string KeySetOmittedNoPeak = "FSAReport_SetOmittedNoPeak";
        const string KeySetOmittedNoLines = "FSAReport_SetOmittedNoLines";
        const string KeySetOmittedNotConfirmed = "FSAReport_SetOmittedNotConfirmed";
        const string KeySetOmittedBelowCoverage = "FSAReport_SetOmittedBelowCoverage";
        const string KeySetOmittedNotNovel = "FSAReport_SetOmittedNotNovel";
        const string KeyResidualNoise = "FSAReport_ResidualNoise";
        const string KeyResidualNoiseTip = "FSAReport_ResidualNoiseTip";
        // (`AMBER157`, `AMBER159`; П195 01.10.2026) Линии вне кривой эффективности;
        // плотность пробы против сцены. (Разбор без матрицы `AMBER156` (в) — с `AMBER284`
        // подсказкой строки «Матрица отклика», <see cref="KeyMatrixNotUsedTip"/>.)
        const string KeyCurveOutsideRow = "FSAReport_CurveOutsideRow";
        const string KeyCurveOutsideValue = "FSAReport_CurveOutsideValue";
        const string KeyDensityRow = "FSAReport_DensityRow";
        // (`AMBER274`, `Р5`/`Д2`) Бета пробы: строка, «легла», выключена, не взята (причины).
        const string KeySampleBetaRow = "FSAReport_SampleBetaRow";
        const string KeySampleBetaUsed = "FSAReport_SampleBetaUsed";
        // (`AMBER284`, решение Amber 02.11.2026: «"Бета-излучение пробы: 1234 отсч." Остальное в
        // подсказку»; капсула — «Убрать из строки и подсказки, оставить в отказе») число узлов
        // раздела — в подсказке; капсула — только в причине отказа `KeySampleBetaOtherCapsule`
        const string KeySampleBetaTip = "FSAReport_SampleBetaTip";
        const string KeySampleBetaKeyOff = "FSAReport_SampleBetaKeyOff";
        const string KeySampleBetaRefused = "FSAReport_SampleBetaRefused";
        const string KeySampleBetaDamaged = "FSAReport_SampleBetaDamaged";
        const string KeySampleBetaOtherMatrix = "FSAReport_SampleBetaOtherMatrix";
        const string KeySampleBetaOtherPhysics = "FSAReport_SampleBetaOtherPhysics";
        const string KeySampleBetaOtherCapsule = "FSAReport_SampleBetaOtherCapsule";
        const string KeySampleBetaIsoScene = "FSAReport_SampleBetaIsoScene";
        const string KeySampleBetaNoScene = "FSAReport_SampleBetaNoScene";

        /// <summary>
        /// (`AMBER274`, `Р5`) Значение строки «бета пробы» словами окна; null — строки нет
        /// (разбор без матрицы или у матрицы нет раздела электронов). Открыта пробе: приёмка
        /// читает ТУ ЖЕ строку, что окно. Числа — инвариантной культурой (`A242`).
        /// </summary>
        public static string SampleBetaText(FsaResult result)
        {
            if (result == null || !result.ResponseMatrixUsed
                || result.SampleBetaRefusal == EfficiencyMaker.ElectronSectionRefusal.Missing)
            {
                return null;
            }

            string capsule = string.IsNullOrEmpty(result.SampleBetaCapsule) ? "none" : result.SampleBetaCapsule;
            if (!result.SampleBetaKeyOn)
            {
                return OwnText(KeySampleBetaKeyOff);
            }

            string reason;
            switch (result.SampleBetaRefusal)
            {
                case EfficiencyMaker.ElectronSectionRefusal.None:
                    // (`AMBER284`) в строке — только отсчёты; узлы — в подсказке, капсула — нигде
                    return string.Format(CultureInfo.InvariantCulture, OwnText(KeySampleBetaUsed),
                                         result.SampleBetaCounts.ToString("F0", CultureInfo.InvariantCulture));
                case EfficiencyMaker.ElectronSectionRefusal.Damaged:
                    reason = OwnText(KeySampleBetaDamaged);
                    break;
                case EfficiencyMaker.ElectronSectionRefusal.OtherPhysics:
                    reason = OwnText(KeySampleBetaOtherPhysics);
                    break;
                case EfficiencyMaker.ElectronSectionRefusal.OtherCapsule:
                    reason = string.Format(CultureInfo.InvariantCulture, OwnText(KeySampleBetaOtherCapsule), capsule);
                    break;
                case EfficiencyMaker.ElectronSectionRefusal.IsoScene:
                    reason = OwnText(KeySampleBetaIsoScene);
                    break;
                case EfficiencyMaker.ElectronSectionRefusal.NoScene:
                    reason = OwnText(KeySampleBetaNoScene);
                    break;
                default:
                    reason = OwnText(KeySampleBetaOtherMatrix);
                    break;
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeySampleBetaRefused), reason);
        }

        /// <summary>
        /// (`AMBER284`) Подсказка строки «бета-излучение пробы», когда бета легла: отсчёты и
        /// число узлов раздела электронов матрицы; null — бета не легла (строки нет, выключена
        /// или не взята — тогда подсказка строки — её же текст). Открыта пробе. Числа —
        /// инвариантной культурой (`A242`).
        /// </summary>
        public static string SampleBetaTip(FsaResult result)
        {
            if (result == null || !result.ResponseMatrixUsed || !result.SampleBetaKeyOn
                || result.SampleBetaRefusal != EfficiencyMaker.ElectronSectionRefusal.None)
            {
                return null;
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeySampleBetaTip),
                                 result.SampleBetaCounts.ToString("F0", CultureInfo.InvariantCulture),
                                 result.SampleBetaNodes.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER284`, решение Amber 02.11.2026 «Объединить со строкой выше, где уже написано -
        /// нет Матрицы отклика. Остальное убрать в подсказку.») Подсказка строки «Матрица
        /// отклика» у разбора с кривой и без матрицы — прежняя отдельная строка `AMBER156` (в)
        /// словами без жаргона; null — матрица учтена или кривой нет (без кривой разбор не
        /// судит о цене в процентах). Условие — то же, что было у снятой строки. Открыта пробе.
        /// </summary>
        public static string MatrixNotUsedTip(FsaResult result)
        {
            return result != null && result.EfficiencyUsed && !result.ResponseMatrixUsed
                ? OwnText(KeyMatrixNotUsedTip)
                : null;
        }

        // (`AMBER274`, Ф14, К3) Строка по сплошному спектру: пометка при имени, снятие без
        // раздела электрона (причины), отказ разбора, когда снятые были всем составом; ветви β
        // с суммой выше 100 %.
        const string KeyBetaOnlyMark = "FSAReport_BetaOnlyMark";
        const string KeyBetaOnlyRemovedRow = "FSAReport_BetaOnlyRemovedRow";
        const string KeyBetaOnlyRemovedValue = "FSAReport_BetaOnlyRemovedValue";
        const string KeyBetaOnlyNoMatrix = "FSAReport_BetaOnlyNoMatrix";
        const string KeyBetaOnlyKeyOff = "FSAReport_BetaOnlyKeyOff";
        const string KeyBetaOnlyNoSection = "FSAReport_BetaOnlyNoSection";
        const string KeyBetaOnlyMasked = "FSAReport_BetaOnlyMasked";
        const string KeyBetaOnlyRefusal = "FSAReport_BetaOnlyRefusal";
        const string KeyBetaBranchExcessRow = "FSAReport_BetaBranchExcessRow";
        const string KeyBetaBranchExcessValue = "FSAReport_BetaBranchExcessValue";
        // (`AMBER274`, два захода) шаг узлов под строкой беты: подпись, хвосты подписи, значения
        const string KeyBetaKnotRow = "FSAReport_BetaKnotRow";
        // (`AMBER274`, «MDA под порог z ≥ 3») правило пределов: подпись, значение, подсказка
        const string KeyLimitRuleRow = "FSAReport_LimitRuleRow";
        const string KeyLimitRuleValue = "FSAReport_LimitRuleValue";
        const string KeyLimitRuleTip = "FSAReport_LimitRuleTip";

        /// <summary>
        /// (`AMBER274`, «MDA под порог z ≥ 3») Значение строки «правило MDA»: порог z, под который
        /// посчитаны пределы (k_α); null — пределов не считали. Открыта пробе.
        /// </summary>
        public static string LimitRuleText(FsaResult result)
        {
            if (result == null || !(result.LimitDecisionQuantile > 0.0) || !(result.LimitDetectionQuantile > 0.0))
            {
                return null;
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyLimitRuleValue),
                                 result.LimitDecisionQuantile.ToString("0.##", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER274`) Подсказка строки «правило MDA»: k_α и α, k_β и β словами окна; null —
        /// пределов не считали. Открыта пробе. Числа — инвариантной культурой (`A242`).
        /// </summary>
        public static string LimitRuleTip(FsaResult result)
        {
            if (result == null || !(result.LimitDecisionQuantile > 0.0) || !(result.LimitDetectionQuantile > 0.0))
            {
                return null;
            }

            double alpha = 50.0 * MathNet.Numerics.SpecialFunctions.Erfc(result.LimitDecisionQuantile / Math.Sqrt(2.0));
            double beta = 50.0 * MathNet.Numerics.SpecialFunctions.Erfc(result.LimitDetectionQuantile / Math.Sqrt(2.0));
            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyLimitRuleTip),
                                 result.LimitDecisionQuantile.ToString("0.##", CultureInfo.InvariantCulture),
                                 alpha.ToString("0.###", CultureInfo.InvariantCulture),
                                 result.LimitDetectionQuantile.ToString("0.###", CultureInfo.InvariantCulture),
                                 beta.ToString("0.#", CultureInfo.InvariantCulture));
        }
        const string KeyBetaKnotVifOver = "FSAReport_BetaKnotVifOver";
        const string KeyBetaKnotSecondPass = "FSAReport_BetaKnotSecondPass";
        const string KeyBetaKnotOnePass = "FSAReport_BetaKnotOnePass";
        const string KeyBetaKnotSecondRefused = "FSAReport_BetaKnotSecondRefused";
        const string KeyBetaKnotNoGamma = "FSAReport_BetaKnotNoGamma";
        // (`AMBER284`, решение Amber 02.11.2026 «Убрать строку в подсказку строки бета-нуклидов
        // (Рекомендую)»: на виду — только предупреждение, если VIF выше предела) подпись и
        // значение этого предупреждения; шаг узлов, VIF и заходы — подсказкой (`BetaKnotCaption`)
        const string KeyBetaKnotVifOverRow = "FSAReport_BetaKnotVifOverRow";
        const string KeyBetaKnotVifOverValue = "FSAReport_BetaKnotVifOverValue";
        // (`AMBER284`, решение Amber 02.11.2026 вопросником «Вернуть красной строкой (Рекомендую)»)
        // отказ второго захода гамма-строк — снова красной строкой на виду: подпись и значение
        const string KeyBetaKnotSecondRefusedRow = "FSAReport_BetaKnotSecondRefusedRow";
        const string KeyBetaKnotSecondRefusedValue = "FSAReport_BetaKnotSecondRefusedValue";

        /// <summary>
        /// (`AMBER284`, решение Amber 02.11.2026 «Вернуть красной строкой (Рекомендую)») Причина
        /// отказа второго захода гамма-строк СЛОВАМИ ОКНА: <see cref="FsaResult.BetaGammaPassRefusal"/>
        /// хранит имя члена <see cref="FsaRefusal"/> («NoFit»), а человеку нужен тот же текст, каким
        /// сеанс называет такой отказ всего разбора (<see cref="FsaAnalysisSession.RefusalText(FsaRefusal, string)"/>).
        /// Имя, которого перечисление не знает, печатается как есть. null — отказа не было.
        /// Открыта пробе.
        /// </summary>
        public static string SecondPassRefusalText(FsaResult result)
        {
            if (result == null || string.IsNullOrEmpty(result.BetaGammaPassRefusal))
            {
                return null;
            }

            FsaRefusal refusal;
            return Enum.TryParse(result.BetaGammaPassRefusal, false, out refusal)
                   && Enum.IsDefined(typeof(FsaRefusal), refusal) && refusal != FsaRefusal.None
                ? FsaAnalysisSession.RefusalText(refusal, null)
                : result.BetaGammaPassRefusal;
        }

        /// <summary>
        /// (`AMBER284`) Подсказка строки состава «только по бета»: своя подсказка строки (или её
        /// подпись), а под ней — <see cref="BetaKnotCaption"/> (шаг узлов подложки, VIF, заходы).
        /// null — строка не по бета или подложку не подбирали. Открыта пробе: приёмка читает ТУ
        /// ЖЕ подсказку, что таблица.
        /// </summary>
        public static string BetaOnlyRowTip(FsaReportRow row, FsaResult result)
        {
            if (row == null || !row.BetaOnly)
            {
                return null;
            }

            string knots = BetaKnotCaption(result);
            if (knots == null)
            {
                return null;
            }

            string own = string.IsNullOrEmpty(row.Hint) ? RowCaption(row) : row.Hint;
            return own + Environment.NewLine + knots;
        }

        /// <summary>
        /// (`AMBER274`, решение Amber 02.11.2026 «Редкие узлы + два захода (Рекомендую)»; с
        /// `AMBER284` — подсказка строк «только по бета» и красного предупреждения о VIF, а не
        /// своя строка блока) Текст «подложка под нуклидами только по бета»: шаг узлов и VIF
        /// строки беты против шапок, предел VIF, и чем посчитаны гамма-строки — вторым заходом с
        /// обычным шагом, одним заходом (шаг подбора и есть обычный) или первым заходом при
        /// отказе второго. null — строки беты в фите не было. Открыта пробе. Числа —
        /// инвариантной культурой (`A242`).
        /// </summary>
        public static string BetaKnotCaption(FsaResult result)
        {
            if (result == null || double.IsNaN(result.BetaKnotFwhm))
            {
                return null;
            }

            string text = string.Format(CultureInfo.InvariantCulture, OwnText(KeyBetaKnotRow),
                                        result.BetaKnotFwhm.ToString("0.##", CultureInfo.InvariantCulture),
                                        result.BetaKnotVif.ToString("F1", CultureInfo.InvariantCulture),
                                        result.BetaKnotVifMax.ToString("0.##", CultureInfo.InvariantCulture));
            if (!result.BetaKnotVifMet)
            {
                text += OwnText(KeyBetaKnotVifOver);
            }

            if (result.BetaTwoPass)
            {
                text += string.Format(CultureInfo.InvariantCulture, OwnText(KeyBetaKnotSecondPass),
                                      result.GammaKnotFwhm.ToString("0.##", CultureInfo.InvariantCulture));
            }
            else if (result.BetaGammaPassRefusal != null)
            {
                // (`AMBER284`) причина — своей строкой и словами окна, а не именем члена
                // перечисления («NoFit») в скобках
                text += OwnText(KeyBetaKnotSecondRefused) + Environment.NewLine + SecondPassRefusalText(result);
            }
            else if (result.BetaNoGammaRows)
            {
                text += OwnText(KeyBetaKnotNoGamma);
            }
            else
            {
                text += OwnText(KeyBetaKnotOnePass);
            }

            return text;
        }

        /// <summary>
        /// (`AMBER274`, Ф14) Подпись строки отчёта: имя, а у строки по сплошному спектру — с
        /// пометкой словами окна («только по бета» с `AMBER284`, прежде «по сплошному спектру»).
        /// Открыта пробе: приёмка читает ТУ ЖЕ подпись, что таблица.
        /// </summary>
        public static string RowCaption(FsaReportRow row)
        {
            if (row == null)
            {
                return string.Empty;
            }

            string name = row.Name ?? string.Empty;
            return row.BetaOnly ? name + " — " + OwnText(KeyBetaOnlyMark) : name;
        }

        /// <summary>
        /// (`AMBER274`, Ф14) Почему строки по сплошному спектру сняты — словами окна; причина
        /// «раздел» — словами строки «Бета пробы» (те же ключи), у отсутствующего раздела —
        /// своим советом пересчитать матрицу.
        /// </summary>
        public static string BetaOnlyReasonText(FsaBetaOnlyRemoval reason,
                                                EfficiencyMaker.ElectronSectionRefusal refusal, string capsule)
        {
            switch (reason)
            {
                case FsaBetaOnlyRemoval.NoMatrix:
                    return OwnText(KeyBetaOnlyNoMatrix);
                case FsaBetaOnlyRemoval.KeyOff:
                    return OwnText(KeyBetaOnlyKeyOff);
                case FsaBetaOnlyRemoval.ChannelMasked:
                    return OwnText(KeyBetaOnlyMasked);
            }

            switch (refusal)
            {
                case EfficiencyMaker.ElectronSectionRefusal.Damaged:
                    return OwnText(KeySampleBetaDamaged);
                case EfficiencyMaker.ElectronSectionRefusal.OtherPhysics:
                    return OwnText(KeySampleBetaOtherPhysics);
                case EfficiencyMaker.ElectronSectionRefusal.OtherCapsule:
                    return string.Format(CultureInfo.InvariantCulture, OwnText(KeySampleBetaOtherCapsule),
                                         string.IsNullOrEmpty(capsule) ? "none" : capsule);
                case EfficiencyMaker.ElectronSectionRefusal.IsoScene:
                    return OwnText(KeySampleBetaIsoScene);
                case EfficiencyMaker.ElectronSectionRefusal.NoScene:
                    return OwnText(KeySampleBetaNoScene);
                case EfficiencyMaker.ElectronSectionRefusal.OtherMatrix:
                    return OwnText(KeySampleBetaOtherMatrix);
                default:
                    return OwnText(KeyBetaOnlyNoSection);
            }
        }

        /// <summary>
        /// (`AMBER274`, Ф14) Подпись строки «сняты» блока качества; null — не снимали. Открыта
        /// пробе.
        /// </summary>
        public static string BetaOnlyRemovedCaption(FsaResult result)
        {
            if (result == null || result.BetaOnlyRemoved == null || result.BetaOnlyRemoved.Count == 0)
            {
                return null;
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyBetaOnlyRemovedRow),
                                 string.Join(", ", result.BetaOnlyRemoved.ToArray()))
                   + " — " + BetaOnlyReasonText(result.BetaOnlyRemovedReason, result.BetaOnlySectionRefusal,
                                                result.SampleBetaCapsule);
        }

        /// <summary>
        /// (`AMBER274`, Ф14) Приписка к отказу разбора, когда снятые строки по сплошному спектру
        /// были ВСЕМ составом (<c>FsaRefusal.LibraryEmptied</c>): иначе человек читает «состав
        /// пуст» при объявленном Sr-90. null — снятых нет.
        /// </summary>
        public static string BetaOnlyRefusalText(List<string> removed, FsaBetaOnlyRemoval reason,
                                                 EfficiencyMaker.ElectronSectionRefusal refusal)
        {
            if (removed == null || removed.Count == 0)
            {
                return null;
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyBetaOnlyRefusal),
                                 string.Join(", ", removed.ToArray()), BetaOnlyReasonText(reason, refusal, null));
        }

        /// <summary>
        /// (`AMBER274`, К3) Подпись строки «ветви β с суммой выше 100 %»: «Pa-234 104.87 %, …»;
        /// null — таких нет. Числа — инвариантной культурой (`A242`).
        /// </summary>
        public static string BetaBranchExcessCaption(FsaResult result)
        {
            if (result == null || result.BetaBranchExcess == null || result.BetaBranchExcess.Count == 0)
            {
                return null;
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, double> item in result.BetaBranchExcess)
            {
                parts.Add(item.Key + " " + item.Value.ToString("F2", CultureInfo.InvariantCulture) + " %");
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyBetaBranchExcessRow),
                                 string.Join(", ", parts.ToArray()));
        }

        /// <summary>(`AMBER157`) Сколько исключённых линий называется в подписи поимённо.</summary>
        const int CurveOutsideShown = 6;

        /// <summary>
        /// (`AMBER157`, П195) Исключённые линии словами: «K-40 1460.8, …» —
        /// первые <see cref="CurveOutsideShown"/>, остаток числом.
        /// </summary>
        static string OutOfCurveList(List<FsaOutOfCurveLine> lines)
        {
            var parts = new List<string>();
            for (int i = 0; i < lines.Count && i < CurveOutsideShown; i++)
            {
                parts.Add(FsaSampleLibrary.PrettyName(lines[i].Component ?? string.Empty) + " "
                          + lines[i].EnergyKev.ToString("0.0", CultureInfo.InvariantCulture));
            }

            if (lines.Count > CurveOutsideShown)
            {
                parts.Add("+" + (lines.Count - CurveOutsideShown).ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts.ToArray());
        }

        /// <summary>
        /// (`AMBER159`, П195) Сверка плотности для показанного спектра: карточка
        /// пробы и геометрия его кривой; null — сверять нечего.
        /// </summary>
        public static FsaSampleDensityCheck DensityOf(ResultData data)
        {
            return data != null && data.Efficiency != null && data.Efficiency.HasGeometry
                ? FsaAnalysisSession.CheckSampleDensity(data.SampleInfo, data.Efficiency.Geometry)
                : null;
        }

        /// <summary>
        /// (`AMBER159`, П195) Подпись строки плотности: «плотность пробы X,
        /// сцены Y, расхождение Z % — ε при 60 кэВ N %, при 662 кэВ M %
        /// (оценка)». Открыта пробе: приёмка читает ТУ ЖЕ строку, что окно.
        /// </summary>
        public static string DensityCaption(FsaSampleDensityCheck density)
        {
            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyDensityRow),
                                 density.SampleDensity.ToString("0.00", CultureInfo.InvariantCulture),
                                 density.SceneDensity.ToString("0.00", CultureInfo.InvariantCulture),
                                 density.DeviationPercent.ToString("+0;-0;0", CultureInfo.InvariantCulture),
                                 density.Efficiency60Percent.ToString("+0;-0;0", CultureInfo.InvariantCulture),
                                 density.Efficiency662Percent.ToString("+0;-0;0", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER126`) Ниже этого пол шума невязки в подписи не печатается: в
        /// формате `F1` он читался бы «0.0 %», а у сильного источника (Cs-137 в
        /// домике, 250 М отсч.) пол и есть сотые доли процента.
        /// </summary>
        const double ResidualNoiseShownFloor = 0.0005;

        static readonly ComponentResourceManager OwnResources =
            new ComponentResourceManager(typeof(FSAReportView));

        /// <summary>
        /// Строка из собственного <c>resx</c> окна. Ключ вместо пропажи: пустая
        /// подпись неотличима от «сказать нечего», а признак без читателя —
        /// не работа.
        /// </summary>
        static string OwnText(string key)
        {
            return OwnResources.GetString(key) ?? key;
        }

        /// <summary>Толщина черты над блоком качества, пикселей.</summary>
        const int QualityRuleHeight = 3;

        /// <summary>Цвет черты над блоком качества.</summary>
        static readonly Color QualityRuleColor = Color.FromArgb(128, 128, 128);

        /// <summary>
        /// (`AMBER11`) Цвет ПОЛОЖИТЕЛЬНОГО статуса блока качества — тот же
        /// зелёный, каким строка состояния говорит «FSA completed»
        /// (<see cref="StatusDoneColor"/>): один смысл — один цвет.
        ///
        /// ⛔ Значение записано числом, а не ссылкой на <c>StatusDoneColor</c>:
        /// статические поля инициализируются в ТЕКСТОВОМ порядке, а та строка
        /// объявлена ниже по файлу — ссылка дала бы пустой цвет молча.
        /// </summary>
        static readonly Color MarkGoodColor = Color.FromArgb(0, 128, 0);

        /// <summary>
        /// (`AMBER11`) Цвет ОТРИЦАТЕЛЬНОГО статуса — тот же кирпичный, каким
        /// в этой же таблице уже светятся происшествия и строка «фон не
        /// вычтен» (<see cref="MakeRow"/>): двух красных в одной панели быть
        /// не должно.
        /// </summary>
        static readonly Color MarkBadColor = Color.Firebrick;

        /// <summary>Заголовок блока — тем же шрифтом, но полужирным; заводится один раз.</summary>
        Font headerFont;

        public FSAReportView(MainForm mainForm)
        {
            this.mainForm = mainForm;

            // (`A250`) Контекст снимается ЗДЕСЬ: конструктор идёт на потоке
            // окон, а WinForms ставит свой контекст первому же созданному на
            // потоке элементу управления — то есть он уже есть.
            this.CaptureUiContext();
            this.InitializeComponent();

            // Свойства XPTable с enum — кодом, шрифт заголовка — безопасно
            // от шрифта формы: сериализованный в `.resx` тип XPTable ломает
            // конструктор форм (правило здоровых видов репозитория).
            this.valueColumn.Alignment = ColumnAlignment.Right;
            this.componentColumn.Alignment = ColumnAlignment.Left;
            this.componentColumn.Renderer = new SwatchTextCellRenderer();
            this.reportTable.HeaderFont = this.Font;
            this.reportTable.NoItemsText = string.Empty;
            this.reportTable.GridLines = GridLines.None;
            this.reportTable.SelectionStyle = SelectionStyle.Grid;
            this.reportTable.EnableWordWrap = true;
            this.reportTable.EnableToolTips = true;

            this.headerFont = new Font(this.Font, FontStyle.Bold);

            // (`AMBER45`) Пункты комбо «Matrix layer» — кодом, из собственных
            // ресурсов окна: «All» первым, за ним восемь каналов по номеру (`AMBER274`: бета пробы)
            // (<see cref="FsaMatrixLayers.Channels"/>). Список каналов один на
            // проект — здесь он только подписывается.
            this.loading = true;
            try
            {
                this.matrixLayerComboBox.Items.Clear();
                this.matrixLayerComboBox.Items.Add(MatrixLayerText(FsaMatrixLayer.All));
                foreach (FsaMatrixLayer layer in FsaMatrixLayers.Channels)
                {
                    this.matrixLayerComboBox.Items.Add(MatrixLayerText(layer));
                }

                this.matrixLayerComboBox.SelectedIndex = 0;
            }
            finally
            {
                this.loading = false;
            }

            // (`AMBER222`) Обе галочки показа — из общих настроек приложения.
            this.LoadDisplaySettings();

            this.SetToolTips();
            this.reportTable.Resize += this.ReportTable_Resize;
            this.reportTable.SelectionChanged += this.ReportTable_SelectionChanged;
            this.reportTable.CellMouseDown += this.ReportTable_CellMouseDown;
            this.reportTable.KeyDown += this.ReportTable_KeyDown;       // (`AMBER320`) свёртка клавишами
            this.VisibleChanged += this.ConsumerStateChanged;
            this.DockStateChanged += this.ConsumerStateChanged;
            this.SetDocument(null);
        }

        // ------------------------------------------------------------------
        // Связь с документом
        // ------------------------------------------------------------------

        /// <summary>
        /// Сменился активный документ (или его не стало). Подписка переезжает на
        /// сеанс нового документа; группировка окна переносится на его график.
        /// </summary>
        public void SetDocument(DocEnergySpectrum doc)
        {
            this.CaptureUiContext();

            // (`A246`) Приглушение снимается У ПРЕЖНЕГО документа, пока ссылка
            // на него ещё здесь: иначе его график остался бы с поблекшими
            // лентами навсегда — окно на него больше не смотрит и снять их
            // будет некому. (П290) Снимается БЕЗУСЛОВНО, чья бы ни была подсветка:
            // прежний документ покидает и окно результата (оно читает активный
            // документ), и снять поставленное им тоже будет некому.
            if (!ReferenceEquals(this.document, doc))
            {
                this.ForgetSelectedRow();
                this.PushHighlight();
            }

            FsaAnalysisSession next = doc != null ? doc.FsaSession : null;
            if (!ReferenceEquals(this.session, next))
            {
                if (this.session != null)
                {
                    this.session.Completed -= this.SessionCompleted; this.session.Started -= this.SessionStarted;
                }

                this.session = next;
                if (this.session != null)
                {
                    this.session.Completed += this.SessionCompleted; this.session.Started += this.SessionStarted;
                }
            }

            // (`A295`) Такт обновления вида — второй источник заказа, наравне
            // со сменой документа и видимости: без него окно, открытое при
            // выключенном `ShowFSA`, не узнаёт о новых отсчётах вовсе.
            if (!ReferenceEquals(this.document, doc))
            {
                if (this.document != null)
                {
                    this.document.ViewRefreshed -= this.DocumentViewRefreshed;
                }

                if (doc != null)
                {
                    doc.ViewRefreshed += this.DocumentViewRefreshed;
                }
            }

            this.document = doc;
            this.probeResultData = null;
            this.ReadDocument();
        }

        /// <summary>В том же документе выбран другой спектр — перечитать всё.</summary>
        public void ActiveResultDataChanged()
        {
            this.ReadDocument();
        }

        /// <summary>
        /// ПРОБЫ: сеанс и спектр без документа (`FsaStackShot`,
        /// `FsaReportViewProbe`). В приложении не зовётся — там источник один,
        /// документ. Группировку пробе некуда толкать: графика у неё нет.
        /// </summary>
        public void SetProbeSource(FsaAnalysisSession probeSession, ResultData resultData)
        {
            this.CaptureUiContext();
            this.ForgetSelectedRow();
            this.PushHighlight();
            if (!ReferenceEquals(this.session, probeSession))
            {
                if (this.session != null)
                {
                    this.session.Completed -= this.SessionCompleted; this.session.Started -= this.SessionStarted;
                }

                this.session = probeSession;
                if (this.session != null)
                {
                    this.session.Completed += this.SessionCompleted; this.session.Started += this.SessionStarted;
                }
            }

            this.document = null;
            this.probeResultData = resultData;
            this.ReadDocument();
        }

        ResultData probeResultData;

        /// <summary>Документ, который показывается сейчас (пробы).</summary>
        public DocEnergySpectrum Document
        {
            get { return this.document; }
        }

        ResultData ActiveResultData
        {
            get
            {
                if (this.probeResultData != null)
                {
                    return this.probeResultData;
                }

                if (this.document == null || this.document.IsDisposed || this.document.ResultDataFile == null
                    || this.document.ResultDataFile.ResultDataList.Count == 0)
                {
                    return null;
                }

                ResultData rd = this.document.ActiveResultData;
                return rd != null && rd.EnergySpectrum != null && rd.EnergySpectrum.Spectrum != null ? rd : null;
            }
        }

        /// <summary>
        /// Элементы управления — из активной копии конфигурации спектра, как
        /// прежние две галки панели поиска пиков; затем группировка, заказ
        /// расчёта (если окно — потребитель) и таблица.
        /// </summary>
        void ReadDocument()
        {
            ResultData rd = this.ActiveResultData;
            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            this.loading = true;
            try
            {
                this.sourceNucBaseRadio.Checked = options.DbLookups;
                this.sourcePeaksRadio.Checked = !options.DbLookups;
                this.equilibriumCheckBox.Checked = options.ChainEquilibrium;
                this.atomicXrayCheckBox.Checked = options.AtomicXray;
                this.cascadeSummingCheckBox.Checked = options.CascadeSumming;
                this.backscatterCheckBox.Checked = options.Backscatter;
                this.escapeCheckBox.Checked = options.EscapeAndAnnihilation;
                this.pileUpCheckBox.Checked = options.PileUp;
                // (`AMBER269`) нижняя граница разбора — 0 значит правило порога
                decimal bound = (decimal)Math.Max(0.0, Math.Min((double)this.fitLowerBoundUpDown.Maximum,
                                                                options.FitLowerBoundKev));
                this.fitLowerBoundUpDown.Value = bound;
            }
            finally
            {
                this.loading = false;
            }

            this.PushGrouping();

            // (`A248`) Положение галочки принадлежит ОКНУ и переезжает на
            // график того документа, который показывается сейчас: иначе
            // снятая лента возвращалась бы при каждой смене спектра.
            this.PushResidualBand();
            this.PushSpectralData();

            // (`AMBER45`) Тот же довод для слоя матрицы: просьба живёт в окне
            // и едет на график показываемого документа.
            this.PushMatrixLayer();
            this.Consume();
            this.RefreshReport();
        }

        // ------------------------------------------------------------------
        // Потребитель расчёта
        // ------------------------------------------------------------------

        /// <summary>
        /// Окно — потребитель, пока его видно: скрытое (`HideOnClose`),
        /// свёрнутое в автоскрытие или лежащее неактивной вкладкой окно
        /// расчёта не заказывает — его строки никто не читает.
        /// </summary>
        bool IsConsumer
        {
            get
            {
                if (this.ProbeConsumer)
                {
                    return true;
                }

                return !this.IsDisposed && this.Visible && !this.IsHidden && this.DockState != DockState.Hidden;
            }
        }

        /// <summary>Заказать расчёт активного спектра, если окно — потребитель.</summary>
        void Consume()
        {
            ResultData rd = this.ActiveResultData;
            if (this.session == null || rd == null || !this.IsConsumer)
            {
                return;
            }

            // Тот же довод «фон вычитается», что у графика
            // (`EnergySpectrumView.UpdateFsaOverlay`): у обоих потребителей
            // один отпечаток, иначе они заказывали бы два разных счёта по
            // очереди.
            this.session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null);
        }

        /// <summary>
        /// Вид документа обновлён — данные могли смениться (`A295`). Заказ
        /// идёт БЕЗ перестроения таблицы: строки перечитает
        /// <see cref="SessionCompleted"/>, когда счёт кончится, а дёргать
        /// таблицу на каждом такте отрисовки незачем.
        /// </summary>
        void DocumentViewRefreshed(object sender, EventArgs e)
        {
            this.Consume();
        }

        void ConsumerStateChanged(object sender, EventArgs e)
        {
            if (this.IsConsumer)
            {
                this.Consume();
                this.RefreshReport();
            }
        }

        /// <summary>
        /// (`AMBER201`, мелочь 6.9, 05.10.2026) Пересчёт начался — строка состояния
        /// переходит в «идёт расчёт». Только строка: таблица до прихода результата
        /// остаётся прежней (её перечитает <see cref="SessionCompleted"/>), а цвет
        /// и текст строки и есть то, что говорит человеку «числа ниже — прежние».
        /// Событие приходит с потока окон, но окно без ручки (пробы) не трогается.
        /// </summary>
        void SessionStarted(object sender, EventArgs e)
        {
            if (!this.IsHandleCreated || this.IsDisposed)
            {
                return;
            }

            if (this.InvokeRequired)
            {
                this.BeginInvoke((MethodInvoker)this.RefreshOnStart);
                return;
            }

            this.RefreshOnStart();
        }

        /// <summary>
        /// ⛔ (П261, 14.10.2026) ПРИЧИНА ПРЕЖНЕГО ОТКАЗА НЕ СТОИТ ПОД «ИДЁТ РАСЧЁТ». Таблица до
        /// прихода результата остаётся прежней ради ЧИСЕЛ — жёлтая строка говорит «числа ниже —
        /// прежние». Строка отказа числом не является: под жёлтым «FSA in progress» она читается
        /// как отказ ИДУЩЕГО счёта, которого нет (П259, находка 6: `FSALibraryEmptied` 20–40 с
        /// поверх нормального разбора тория). Поэтому без результата таблица перестраивается —
        /// при идущем счёте <see cref="BuildRows"/> строки причины не даёт (решение Amber
        /// 07.09.2026 «идёт расчёт» в таблицу не возвращается: признак — строка состояния).
        /// ⚠ Кроме записи спектра: там пересчёт идёт кругами, и та же причина мигала бы на каждом
        /// круге — именно от мигания таблицы строку состояния и заводили (то же решение).
        /// Перестройка — отложенно: пуск счёта приходит и изнутри отрисовки графика.
        /// </summary>
        void RefreshOnStart()
        {
            this.RefreshStatusLine();
            FsaAnalysisSession current = this.session;
            ResultData rd = this.ActiveResultData;
            if (current == null || rd == null || current.Result != null
                || (rd.ResultDataStatus != null && rd.ResultDataStatus.Recording))
            {
                return;
            }

            this.BeginInvoke((MethodInvoker)delegate
            {
                if (!this.IsDisposed && ReferenceEquals(this.session, current) && current.Result == null)
                {
                    this.RefreshReport();
                }
            });
        }

        /// <summary>
        /// Счёт закончился — из ФОНОВОГО потока: перечитать на UI. Заодно
        /// заказать счёт заново, если входные данные сменились, пока никто
        /// не звал <c>EnsureUpToDate</c> (тот же довод, что у графика).
        /// </summary>
        void SessionCompleted(object sender, EventArgs e)
        {
            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        this.Consume();
                        this.RefreshReport();
                    });
                    return;
                }

                if (!this.ProbeConsumer || this.IsDisposed)
                {
                    return;
                }

                // ⛔ (`A250`) ЧИТАТЬ ПРЯМО ЗДЕСЬ НЕЛЬЗЯ. Прежде окно без ручки
                // звало `RefreshReport` в этой самой точке — то есть на
                // ФОНОВОМ потоке, из которого сеанс поднимает событие, — и
                // делало это ОДНОВРЕМЕННО с `RefreshReport` на потоке окон.
                // `TableModel.Rows` чистились и наполнялись двумя потоками
                // сразу, и состав отчёта у ОДНОГО спектра плыл от прогона к
                // прогону: 16 / 20 / 22 / 27 / 29 / 31 / 32 строки на
                // `G1S16_Co60_P5` при одной сборке и одном файле; на двенадцати
                // пересчётах подряд — до шести разных таблиц. Сам разбор при
                // этом ПОБИТОВО детерминирован (`FsaDeterminismProbeF29`):
                // плыла публикация, а не счёт. Тем же гонкам обязано и падение
                // `RowCollection.Clear()` примерно каждый третий прогон
                // (`A251`): вторая рука опустошала список между `this[0]` и
                // его чтением.
                //
                // Ручки нет — но поток окон есть, и дверь на него одна:
                // контекст синхронизации, снятый в конструкторе. Очередь
                // сообщений у безоконной пробы прокачивается `DoEvents`, и
                // отложенное чтение доезжает там же, где и в приложении.
                SynchronizationContext ui = this.uiContext;
                if (ui == null || ui.GetType() == typeof(SynchronizationContext))
                {
                    // Голый базовый контекст исполняет `Post` на пуле — это
                    // ровно тот дефект, от которого мы уходим. Такого
                    // потребителя обслужит следующий явный `RefreshReport`
                    // с потока окон.
                    return;
                }

                ui.Post(delegate
                {
                    if (!this.IsDisposed)
                    {
                        this.Consume();
                        this.RefreshReport();
                    }
                }, null);
            }
            catch (Exception)
            {
                // окно успело закрыться — читать уже некому
            }
        }

        /// <summary>
        /// (`A250`) Снять контекст потока окон, если он ещё не снят. Зовётся с
        /// потока окон — из конструктора и из обеих дверей источника.
        /// </summary>
        void CaptureUiContext()
        {
            if (this.uiContext == null)
            {
                this.uiContext = SynchronizationContext.Current;
            }
        }

        // ------------------------------------------------------------------
        // Расчётные переключатели
        // ------------------------------------------------------------------

        void sourceRadio_CheckedChanged(object sender, EventArgs e)
        {
            // Пара радиокнопок поднимает событие дважды; пишем по нажатой.
            RadioButton radio = sender as RadioButton;
            if (radio == null || !radio.Checked)
            {
                return;
            }

            this.ApplyCalculationChange(cfg => cfg.DbLookupsForFsa = this.sourceNucBaseRadio.Checked);
        }

        void equilibriumCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            this.ApplyCalculationChange(cfg => cfg.ChainEquilibrium = this.equilibriumCheckBox.Checked);
        }

        /// <summary>
        /// (`AMBER269`; решение Amber 02.11.2026 «Да, настройкой FSA с умолчанием 40 (Рекомендую)»)
        /// Поле «Нижняя граница разбора, кэВ» — расчётное, той же дверью, что семь переключателей:
        /// пишется в общую настройку приложения; у копии конфигурации спектра поля нет.
        /// </summary>
        void fitLowerBoundUpDown_ValueChanged(object sender, EventArgs e)
        {
            double kev = (double)this.fitLowerBoundUpDown.Value;
            this.ApplyCalculationChange(cfg => { }, settings => settings.FitLowerBoundKev = kev);
        }

        void atomicXrayCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            this.ApplyCalculationChange(cfg => cfg.AtomicXrayForFsa = this.atomicXrayCheckBox.Checked);
        }

        void cascadeSummingCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            this.ApplyCalculationChange(cfg => cfg.CascadeSummingForFsa = this.cascadeSummingCheckBox.Checked);
        }

        void backscatterCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            this.ApplyCalculationChange(cfg => cfg.BackscatterForFsa = this.backscatterCheckBox.Checked);
        }

        void escapeCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            this.ApplyCalculationChange(cfg => cfg.EscapeAndAnnihilationForFsa = this.escapeCheckBox.Checked);
        }

        void pileUpCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            this.ApplyCalculationChange(cfg => cfg.PileUpForFsa = this.pileUpCheckBox.Checked);
        }

        // ------------------------------------------------------------------
        // (`A248`) Показ ленты невязки — переключатель ПОКАЗА, не расчёта
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ Галка ленты невязки идёт ДРУГОЙ дорогой, нежели пять галок
        /// «Дополнительных компонентов модели»: не
        /// <see cref="ApplyCalculationChange"/>, а одна
        /// перерисовка графика. Никакой записи в конфигурацию СПЕКТРА, никакого
        /// <c>session.Invalidate()</c>, никакого <c>Consume()</c> — иначе снятая
        /// лента стоила бы человеку полного пересчёта разбора и, что хуже,
        /// поменяла бы отпечаток: те же числа считались бы заново.
        /// (`AMBER222`) Положение галочки пишется в общие настройки приложения
        /// (<see cref="SaveDisplaySettings"/>) — это не отпечаток разбора.
        /// </summary>
        void residualBandCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (this.loading)
            {
                return;
            }

            this.showResidualBand = this.residualBandCheckBox.Checked;
            this.PushResidualBand();
            this.SaveDisplaySettings();
        }

        /// <summary>
        /// (`AMBER222`) Галка «Spectral data» — той же дорогой, что галка ленты
        /// невязки: перерисовка графика и запись положения в общие настройки,
        /// без пересчёта и без правки таблицы.
        /// </summary>
        void spectralDataCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (this.loading)
            {
                return;
            }

            this.showSpectralData = this.spectralDataCheckBox.Checked;
            this.PushSpectralData();
            this.SaveDisplaySettings();
        }

        /// <summary>Показывать ли на графике FSA линию измеренного спектра (пробы).</summary>
        public bool ShowSpectralData
        {
            get
            {
                return this.showSpectralData;
            }

            set
            {
                this.loading = true;
                try
                {
                    this.spectralDataCheckBox.Checked = value;
                }
                finally
                {
                    this.loading = false;
                }

                this.showSpectralData = value;
                this.PushSpectralData();
            }
        }

        /// <summary>
        /// Сказать графику, показывать ли линию спектра. ⛔ Ничего, кроме
        /// перерисовки, это не меняет — по образцу <see cref="PushResidualBand"/>.
        /// </summary>
        void PushSpectralData()
        {
            if (this.document != null && !this.document.IsDisposed
                && this.document.EnergySpectrumView != null)
            {
                this.document.EnergySpectrumView.FsaShowSpectralData = this.showSpectralData;
            }
        }

        /// <summary>
        /// (`AMBER222`) Обе галочки показа — из общих настроек приложения. Без
        /// источника приложения (<see cref="FsaCalculationOptions.ApplicationSource"/>
        /// не поставлен — пробы) общей настройки нет: остаются умолчания окна,
        /// как у семи расчётных в <see cref="ApplyCalculationChange"/>.
        /// </summary>
        void LoadDisplaySettings()
        {
            if (FsaCalculationOptions.ApplicationSource == null)
            {
                return;
            }

            GlobalConfigInfo config = GlobalConfigManager.GetInstance().GlobalConfig;
            if (config == null)
            {
                return;
            }

            this.loading = true;
            try
            {
                this.residualBandCheckBox.Checked = config.FsaShowResidual;
                this.spectralDataCheckBox.Checked = config.FsaShowSpectralData;
            }
            finally
            {
                this.loading = false;
            }

            this.showResidualBand = config.FsaShowResidual;
            this.showSpectralData = config.FsaShowSpectralData;
        }

        /// <summary>
        /// (`AMBER222`) Записать обе галочки показа в общие настройки и сразу на
        /// диск — тем же путём, что семь расчётных (<see cref="ApplyCalculationChange"/>).
        /// Без источника приложения (пробы) — ничего: общей настройки у пробы нет.
        /// </summary>
        void SaveDisplaySettings()
        {
            if (FsaCalculationOptions.ApplicationSource == null)
            {
                return;
            }

            GlobalConfigManager manager = GlobalConfigManager.GetInstance();
            GlobalConfigInfo config = manager.GlobalConfig;
            if (config == null)
            {
                return;
            }

            config.FsaShowResidual = this.showResidualBand;
            config.FsaShowSpectralData = this.showSpectralData;
            manager.SaveConfigFile();
        }

        /// <summary>Показывать ли ленту невязки (пробы).</summary>
        public bool ShowResidualBand
        {
            get
            {
                return this.showResidualBand;
            }

            set
            {
                this.loading = true;
                try
                {
                    this.residualBandCheckBox.Checked = value;
                }
                finally
                {
                    this.loading = false;
                }

                this.showResidualBand = value;
                this.PushResidualBand();
            }
        }

        /// <summary>
        /// Сказать графику, показывать ли ленту невязки. ⛔ Ничего, кроме
        /// перерисовки, это не меняет: ни расчёта, ни представления, ни строк
        /// таблицы — по образцу <see cref="PushHighlight"/>.
        /// </summary>
        void PushResidualBand()
        {
            if (this.document != null && !this.document.IsDisposed
                && this.document.EnergySpectrumView != null)
            {
                this.document.EnergySpectrumView.FsaShowResidual = this.showResidualBand;
            }
        }

        // ------------------------------------------------------------------
        // (`AMBER45`) Слой матрицы отклика — переключатель ПОКАЗА, не расчёта
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ Комбо «Matrix layer» идёт ТОЙ ЖЕ дорогой, что галка ленты
        /// невязки (<see cref="residualBandCheckBox_CheckedChanged"/>): не
        /// <see cref="ApplyCalculationChange"/>, а одна перерисовка графика.
        /// Ни записи в конфигурацию, ни <c>session.Invalidate()</c>, ни
        /// <c>Consume()</c>, ни <see cref="RefreshReport"/>: строки таблицы от
        /// слоя не зависят (уточнение Amber 18.09.2026 — фит не
        /// пересчитывается, таблица прежняя), и дёргать их незачем.
        /// </summary>
        void matrixLayerComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.loading)
            {
                return;
            }

            this.requestedMatrixLayer = MatrixLayerAt(this.matrixLayerComboBox.SelectedIndex);
            this.PushMatrixLayer();
        }

        /// <summary>Слой по номеру пункта комбо: 0 — «All», далее каналы по порядку.</summary>
        static FsaMatrixLayer MatrixLayerAt(int index)
        {
            int channel = index - 1;
            return channel >= 0 && channel < FsaMatrixLayers.Channels.Length
                ? FsaMatrixLayers.Channels[channel]
                : FsaMatrixLayer.All;
        }

        /// <summary>Номер пункта комбо для слоя: 0 — «All».</summary>
        static int MatrixLayerIndex(FsaMatrixLayer layer)
        {
            return Array.IndexOf(FsaMatrixLayers.Channels, layer) + 1;
        }

        /// <summary>Что просил человек — и когда матрицы нет тоже (пробы).</summary>
        public FsaMatrixLayer RequestedMatrixLayer
        {
            get
            {
                return this.requestedMatrixLayer;
            }

            set
            {
                this.requestedMatrixLayer = value;
                this.PushMatrixLayer();
                this.UpdateAvailability();
            }
        }

        /// <summary>
        /// Достижим ли режим слоя на показываемом спектре: у стопки есть слой
        /// с раскладкой по каналам, то есть матрица отклика жива (пробы).
        /// Правило одно на график и окно — <see cref="FsaMatrixLayers.HasChannels"/>.
        /// </summary>
        public bool MatrixLayerAvailable
        {
            get
            {
                return this.presentation != null && FsaMatrixLayers.HasChannels(this.presentation.Layers);
            }
        }

        /// <summary>
        /// Сказать графику, какой слой показывать. Уходит ПРОСЬБА, а не
        /// «эффективное» значение: достижим ли режим, график решает сам по
        /// результату (<see cref="FsaMatrixLayers.HasChannels"/>) — тем же
        /// правилом, каким это окно гасит комбо, и потому оба всегда об одном.
        /// ⛔ Ничего, кроме перерисовки стопки, это не меняет.
        /// </summary>
        void PushMatrixLayer()
        {
            if (this.document != null && !this.document.IsDisposed)
            {
                this.document.FsaMatrixLayer = this.requestedMatrixLayer;
            }
        }

        /// <summary>
        /// Расчётный переключатель. Путь ОДИН на все семь — «записать настройку,
        /// обесценить кэш, заказать счёт потребителям» — написанный семь раз он
        /// однажды разошёлся бы.
        ///
        /// ⛔ (`AMBER212`, П240 07.10.2026; решения Amber вопросником, дословно:
        /// «Одно на приложение, плюс по умолчанию "From Nucbase"», «Все семь
        /// одинаково») В ПРИЛОЖЕНИИ настройка ОДНА — общая
        /// (<see cref="GlobalConfigInfo.FsaCalculation"/>), и пишется она сразу на
        /// диск, чтобы пережить перезапуск. Прежняя запись в умолчание прибора
        /// (`S70`, 18.08.2026) снята этим решением: у спектра, чей прибор не заведён
        /// в конфигурации, выбор сохранить было некуда, и при следующем открытии он
        /// молча возвращался к «From labels». Копия спектра держится в согласии с
        /// общей настройкой — её читают пробы и тот, кто смотрит поле напрямую.
        /// Соседние документы при этом меняются тоже: отпечаток их разбора
        /// сменился, и видимый потребитель закажет пересчёт сам.
        ///
        /// Без источника приложения (<see cref="FsaCalculationOptions.ApplicationSource"/>
        /// не поставлен — пробы) пишется только копия спектра, как прежде.
        ///
        /// Поиска пиков переключатели НЕ касаются — ни одного пика от них не
        /// появится и не исчезнет, — поэтому детекция не перезапускается;
        /// инвалидируется только разложение. Пересчёт идёт немедленно, только
        /// если есть потребитель: это окно (видимое) или график в `ShowFSA`.
        /// Оба заказывают ОДИН отпечаток, и сеанс считает его один раз.
        /// </summary>
        void ApplyCalculationChange(Action<FWHMPeakDetectionMethodConfig> set)
        {
            this.ApplyCalculationChange(set, null);
        }

        /// <summary>
        /// (`AMBER269`) То же, с настройкой, которой у копии спектра нет (нижняя граница разбора):
        /// <paramref name="setGlobal"/> пишет её в общую настройку приложения; без источника
        /// приложения (пробы) писать её некуда, и пересчёт идёт с прежним значением.
        /// </summary>
        void ApplyCalculationChange(Action<FWHMPeakDetectionMethodConfig> set, Action<FsaCalculationSettings> setGlobal)
        {
            if (this.loading)
            {
                return;
            }

            ResultData rd = this.ActiveResultData;
            FWHMPeakDetectionMethodConfig config = rd != null
                ? rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig
                : null;
            if (FsaCalculationOptions.ApplicationSource != null)
            {
                // Нажатие ложится поверх ОБЩЕЙ настройки, а не поверх копии
                // спектра: копия могла отстать (снята с прибора при открытии), и
                // снять семь значений с неё значило бы вернуть в общую настройку
                // устаревшие шесть.
                GlobalConfigManager manager = GlobalConfigManager.GetInstance();
                FsaCalculationSettings settings = manager.GlobalConfig.FsaCalculation;
                FWHMPeakDetectionMethodConfig scratch = config ?? new FWHMPeakDetectionMethodConfig();
                settings.CopyTo(scratch);
                set(scratch);
                settings.CopyFrom(scratch);
                if (setGlobal != null)
                {
                    setGlobal(settings);
                }

                manager.SaveConfigFile();
            }
            else
            {
                if (config == null)
                {
                    return;
                }

                set(config);
            }

            if (this.session != null)
            {
                this.session.Invalidate();
            }

            this.PushGrouping();
            this.Consume();
            if (this.document != null && !this.document.IsDisposed
                && this.document.EnergySpectrumView != null
                && this.document.EnergySpectrumView.BackgroundMode == BackgroundMode.ShowFSA)
            {
                // Второй потребитель — график: подготовка данных вида закажет
                // тот же отпечаток, что уже считается, и вернётся молча.
                this.document.RefreshView();
            }

            this.RefreshReport();
        }

        // ------------------------------------------------------------------
        // Группировка
        // ------------------------------------------------------------------

        void groupingRadio_CheckedChanged(object sender, EventArgs e)
        {
            RadioButton radio = sender as RadioButton;
            if (this.loading || radio == null || !radio.Checked)
            {
                return;
            }

            this.requestedGrouping = this.parentsRadio.Checked ? FsaGrouping.Parents : FsaGrouping.Daughters;
            this.PushGrouping();
            this.RefreshReport();
        }

        /// <summary>Что просил человек — и когда родители недоступны тоже (пробы).</summary>
        public FsaGrouping RequestedGrouping
        {
            get
            {
                return this.requestedGrouping;
            }

            set
            {
                this.requestedGrouping = value;
                this.PushGrouping();
                this.RefreshReport();
            }
        }

        /// <summary>
        /// Группировка, которая применяется: родители — только когда настройки
        /// это допускают (NucBase + равновесие); при недопустимом сочетании
        /// показываются дочерние, а просьба помнится. Допустим ли режим по
        /// РЕЗУЛЬТАТУ (есть ли связанный ряд), решает построитель — одинаково
        /// для графика и таблицы.
        /// </summary>
        public FsaGrouping EffectiveGrouping
        {
            get
            {
                return this.requestedGrouping == FsaGrouping.Parents
                       && FsaCalculationOptions.Of(this.ActiveResultData).ParentGroupingPossible
                    ? FsaGrouping.Parents
                    : FsaGrouping.Daughters;
            }
        }

        /// <summary>Одно значение на документ: график читает его оттуда же.</summary>
        void PushGrouping()
        {
            if (this.document != null && !this.document.IsDisposed)
            {
                this.document.FsaGrouping = this.EffectiveGrouping;
            }
        }

        // ------------------------------------------------------------------
        // Таблица и доступность
        // ------------------------------------------------------------------

        /// <summary>Снимок, из которого заполнена таблица (пробы); null — результата нет.</summary>
        public FsaPresentation Presentation
        {
            get { return this.presentation; }
        }

        /// <summary>Таблица отчёта (пробы: строки читаются из неё, а не из модели).</summary>
        public Table ReportTable
        {
            get { return this.reportTable; }
        }

        /// <summary>
        /// Строки таблицы по состоянию сеанса (`A145`, «Состояния таблицы»):
        /// нет спектра — одна строка «Спектр не выбран»; результата нет —
        /// одна строка ПРИЧИНЫ (невозможность или ошибка с полной цепочкой,
        /// слова сеанса `A95`); результат есть — полный отчёт.
        ///
        /// ⛔ «ИДЁТ РАСЧЁТ» ЗДЕСЬ НЕ ПОЯВЛЯЕТСЯ НИ В КАКОМ ВИДЕ (решение
        /// Amber 07.09.2026) — ни строкой «считается» при пустом результате,
        /// ни строкой «Пересчёт…» перед готовым. Обе мигали при записи
        /// спектра, где разбор пересчитывается непрерывно. Признак живёт
        /// ПОСТОЯННОЙ строкой над таблицей (<see cref="RefreshStatusLine"/>),
        /// и замысел `A32` — «старые строки нельзя выдавать за актуальные» —
        /// исполняет она: жёлтый цвет держится всё время пересчёта.
        ///
        /// ⚠ Состояние «пересчёт завершился ошибкой при старом результате»
        /// здесь не бывает по устройству сеанса: отказ счёта публикуется с
        /// пустым результатом (<see cref="FsaAnalysisSession"/>, «на каждом
        /// пути отказа оно становится null»), и старых строк к тому моменту
        /// уже нет — показывается одна строка ошибки.
        /// </summary>
        public List<FsaReportRow> BuildRows()
        {
            var rows = new List<FsaReportRow>();
            ResultData rd = this.ActiveResultData;
            if (this.session == null || rd == null)
            {
                this.presentation = null;
                rows.Add(StatusRow(Resources.FSAReportNoSpectrum));
                return rows;
            }

            // Один снимок на заполнение: фон публикует результат в любой момент.
            FsaResult result = this.session.Result;
            string status = this.session.Status;
            bool running = this.session.IsRunning;
            bool oldFormat = this.session.ResponseMatrixOldFormat;

            if (result == null)
            {
                this.presentation = null;

                // ⛔ «ИДЁТ РАСЧЁТ» В ТАБЛИЦЕ БОЛЬШЕ НЕ ПОЯВЛЯЕТСЯ (решение
                // Amber 07.09.2026). При записи спектра разбор пересчитывается
                // непрерывно, и строка мигала в таблице на каждом круге.
                // Признак переехал в <see cref="statusLabel"/> НАД таблицей —
                // он там постоянный и потому не дёргается. В таблице остаётся
                // только ПРИЧИНА, по которой разбора нет: её надо читать, и она
                // не мигает.
                if (!running && !string.IsNullOrEmpty(status))
                {
                    rows.Add(StatusRow(status));
                }

                return rows;
            }

            this.presentation = FsaPresentationBuilder.Build(result, this.EffectiveGrouping, oldFormat);

            // ⛔ Строки «Пересчёт…» здесь БОЛЬШЕ НЕТ (решение Amber
            // 07.09.2026): она вставлялась первой на каждом круге пересчёта и
            // при живой записи спектра мигала. То, ради чего её заводила
            // ~~`A32`~~ — «старые строки нельзя выдавать за актуальные», —
            // теперь говорит ПОСТОЯННАЯ жёлтая строка состояния над таблицей.
            rows.AddRange(this.presentation.Rows);
            return rows;
        }

        static FsaReportRow StatusRow(string text)
        {
            return new FsaReportRow
            {
                Kind = FsaReportRowKind.Status,
                Name = text,
                Value = string.Empty
            };
        }

        /// <summary>
        /// (`A300`, решение Amber 10.09.2026 — «Число оставить, ленту
        /// подписать») НА СКОЛЬКИХ КАНАЛАХ ПОКАЗ ПОДРЕЗАН; 0 — ни на одном.
        ///
        /// ⛔ ЧТО ЭТО ЧИНИТ. Лента невязки на графике и число невязки в этой
        /// строке считаются по РАЗНЫМ кривым, и разошлись они молча:
        /// <see cref="EnergySpectrumView"/> рисует ленту против ПОКАЗНОЙ
        /// кривой (<see cref="FsaResult.NetSpectrum"/>, отрицательное подрезано
        /// нулём — так решено в ~~`A26`~~, «линия измерения не рвётся»), а
        /// число берёт <see cref="FsaResult.ResidualExcessShare"/>, считанное
        /// на КРИВОЙ ФИТА (<see cref="FsaResult.FitSpectrum"/>, без подрезки).
        /// Измерено 10.09.2026 полосой П2 на `AS80_Onyx`: строка говорит
        /// «лишнего 62.79 %», площадь ленты отвечает 18.55 % — впятеро меньше.
        ///
        /// ⛔ ЧИСЛО НЕ ТРОГАЕТСЯ, И ЛЕНТА ТОЖЕ (решение Amber дословно). Число
        /// честно: модель подгонялась под кривую фита, ею же и меряется, и
        /// корпусные числа от этой правки не двигаются ни на знак. Лента
        /// подрезана нарочно — `A26` не отменяется. Меняется ровно одно: у
        /// строки появляется ПРИЗНАК, что показ и число смотрят на разные
        /// кривые, и на скольких каналах.
        ///
        /// ⚠ Считается по ВСЕМУ спектру, а не по полосе фита, и это верная
        /// мера ИМЕННО ДЛЯ ПОКАЗА: лента рисуется по всем каналам, какие
        /// попали в поле, а не по `[chLo, chHi]`. Отсюда расхождение с числом
        /// строки `A300` (2631 из полосы 8075) — там считана полоса.
        /// </summary>
        int ResidualClampedChannels()
        {
            FsaResult result = this.session != null ? this.session.Result : null;
            ResultData rd = this.ActiveResultData;
            if (result == null || rd == null || rd.EnergySpectrum == null
                || rd.EnergySpectrum.Spectrum == null)
            {
                return 0;
            }

            return result.ClampedChannels(rd.EnergySpectrum.Spectrum);
        }

        /// <summary>
        /// Перечитать сеанс: таблица и доступность элементов управления.
        ///
        /// (`A247`) Строки модели ложатся в таблицу НЕ ОДНА В ОДНУ. Всё, что не
        /// компонент состава — «фон не вычтен», невязка и качество, — идёт
        /// отдельным блоком: перед первой такой строкой встают ЧЕРТА и
        /// ЗАГОЛОВОК «Качество разбора» (решение Amber 05.09.2026), а строка
        /// качества разворачивается в несколько строк с подписями, чтобы
        /// всплывающая подсказка была не нужна.
        ///
        /// ⛔ ЧИСЛА ПРИ ЭТОМ НЕ ТРОГАЮТСЯ: и невязка, и χ²/ndf берут
        /// <see cref="FsaReportRow.Value"/> модели ДОСЛОВНО. Ни одного второго
        /// форматирования числа в окне нет и быть не должно (`A242`/`A244`).
        /// </summary>
        /// <summary>
        /// Цвета строки состояния. Не из палитры отчёта: это светофор, и он
        /// обязан читаться independently от того, чем раскрашены ленты.
        /// </summary>
        static readonly Color StatusDoneColor = Color.FromArgb(0, 128, 0);
        static readonly Color StatusRunningColor = Color.FromArgb(176, 124, 0);
        static readonly Color StatusErrorColor = Color.FromArgb(192, 0, 0);
        static readonly Color StatusIdleColor = Color.Gray;

        /// <summary>
        /// Строка состояния расчёта НАД таблицей (решение Amber 07.09.2026):
        /// зелёный — расчёт завершён, жёлтый — идёт, красный — ошибка, серый —
        /// расчёта не было.
        ///
        /// ⛔ ЗАЧЕМ ОНА ЗАВЕДЕНА, и почему признак не вернуть в таблицу.
        /// Признак «идёт расчёт» жил СТРОКОЙ ТАБЛИЦЫ (~~`A32`~~), то есть
        /// появлялся и исчезал вместе с ней. При записи спектра разбор
        /// пересчитывается непрерывно, и строка мигала, сдвигая таблицу.
        /// Постоянная строка снимает мигание, СОХРАНЯЯ признак: она всегда на
        /// месте и меняет только цвет и слово.
        ///
        /// ⚠ Ошибка показывается ТЕКСТОМ сеанса, а не одним словом «ошибка»:
        /// причина («нет опознанных пиков», «разложение невозможно») — это то,
        /// что человеку и нужно, а красного цвета без причины мало.
        /// </summary>
        void RefreshStatusLine()
        {
            if (this.statusLabel == null)
            {
                return;
            }

            string text;
            Color color;
            if (this.session == null || this.ActiveResultData == null)
            {
                text = Resources.FSAStatusIdle;
                color = StatusIdleColor;
            }
            else
            {
                // Один снимок на обновление: фон публикует результат в любой
                // момент, и три чтения подряд дали бы три разных состояния.
                FsaResult result = this.session.Result;
                string status = this.session.Status;
                bool running = this.session.IsRunning;

                if (running)
                {
                    text = Resources.FSAStatusRunning;
                    color = StatusRunningColor;
                }
                else if (result != null)
                {
                    text = Resources.FSAStatusCompleted;
                    color = StatusDoneColor;
                }
                else if (!string.IsNullOrEmpty(status))
                {
                    text = Resources.FSAStatusError + ": " + status;
                    color = StatusErrorColor;
                }
                else
                {
                    text = Resources.FSAStatusIdle;
                    color = StatusIdleColor;
                }
            }

            // Перекрашивать и переписывать только на СМЕНЕ состояния: иначе
            // каждое обновление отчёта дёргало бы метку перерисовкой, а это та
            // же болезнь, от которой строку и завели.
            if (this.statusLabel.Text != text)
            {
                this.statusLabel.Text = text;
                // (проверка экраном 07.11.2026) Строка в одну строку высотой, и длинная причина
                // отказа («…прибор спектра не найден в конфигурации…») обрывалась на слове без
                // знака обрыва. Обрыв — многоточием (`AutoEllipsis`), полный текст — подсказкой
                // блоком; своя подсказка заменяет встроенную однострочную подсказку метки.
                this.statusLabel.AutoEllipsis = true;
                this.toolTip.SetToolTip(this.statusLabel, AppUi.WrapTip(text));
            }

            if (this.statusLabel.ForeColor != color)
            {
                this.statusLabel.ForeColor = color;
            }
        }

        public void RefreshReport()
        {
            this.RefreshStatusLine();
            List<FsaReportRow> rows = this.BuildRows();

            // (`A300`) Признак подрезки считается ОДИН раз на заполнение: он
            // общий для всей таблицы, а `ClampedChannels` проходит спектр
            // целиком.
            int clamped = this.ResidualClampedChannels();
            string residualCaption = OwnText(KeyResidualRow);
            var residualMarks = new List<string>();
            var residualTips = new List<string>();
            if (clamped > 0)
            {
                residualMarks.Add(string.Format(CultureInfo.InvariantCulture,
                                                OwnText(KeyResidualClamped), clamped));
                residualTips.Add(string.Format(CultureInfo.InvariantCulture,
                                               OwnText(KeyResidualClampedTip), clamped));
            }

            // (`S174`, решение Amber 14.09.2026 «Только в диапазоне прибора»)
            // Число считано от `Min_Range` прибора, лента идёт по всей шкале —
            // и это ГОВОРИТСЯ, тем же движением, что подрезка показа (`A300`):
            // порог АЦП 0–30 кэВ виден лентой, а в процент не входит. Пометка
            // только когда пол режет полосу фита: при фите от `Min_Range` число
            // и лента и так об одном.
            FsaResult floorResult = this.session != null ? this.session.Result : null;
            if (floorResult != null && floorResult.ResidualFloorKev > 0.0
                && floorResult.ResidualFloorChannel > floorResult.FirstChannel)
            {
                string kev = floorResult.ResidualFloorKev.ToString("G", CultureInfo.InvariantCulture);
                residualMarks.Add(string.Format(CultureInfo.InvariantCulture, OwnText(KeyResidualFloor), kev));
                residualTips.Add(string.Format(CultureInfo.InvariantCulture, OwnText(KeyResidualFloorTip), kev));
            }

            // (`AMBER126`, П168 28.09.2026) ШУМОВОЙ ПОЛ — рядом с числом, а не
            // вычетом из него: число — площадь ленты (решение Amber по ~~`S111`~~),
            // но без пола «не описано 37 %» у модели, описавшей всё (проба в 30σ
            // над фоном), читалось бы как недоработка модели. Первой пометкой:
            // она о самом числе, остальные — о показе.
            if (floorResult != null && floorResult.ResidualSharesDefined
                && floorResult.ResidualNoiseShare >= ResidualNoiseShownFloor)
            {
                string noise = (100.0 * floorResult.ResidualNoiseShare).ToString("F1", CultureInfo.InvariantCulture);
                residualMarks.Insert(0, string.Format(CultureInfo.InvariantCulture, OwnText(KeyResidualNoise), noise));
                residualTips.Insert(0, string.Format(CultureInfo.InvariantCulture, OwnText(KeyResidualNoiseTip), noise));
            }

            if (residualMarks.Count > 0)
            {
                residualCaption += " — " + string.Join(", ", residualMarks.ToArray());
            }

            string residualHint = residualTips.Count > 0
                ? string.Join(Environment.NewLine, residualTips.ToArray()) : null;

            FsaResult shownSource = this.presentation != null ? this.presentation.Source : null;
            string keep = this.selectedLayer;
            this.suspendSelection = true;
            this.reportTable.BeginUpdate();
            try
            {
                this.tableModel.Rows.Clear();
                bool blockOpened = false;
                for (int index = 0; index < rows.Count; index++)
                {
                    FsaReportRow row = rows[index];

                    // ⛔ (`AMBER320`, решение Amber 07.11.2026: «Все потомки») Строка с отступом
                    // (`Indent > 0`) — потомок ближайшей строки выше без отступа: подстрокой
                    // XPTable (`Row.SubRows`), свёрнутой, пока группу не раскрыли стрелкой.
                    // Строки остаются в `TableModel.Rows` — их читают пробы и сторож витрины.
                    int children = 0;
                    while (row.Indent == 0 && index + 1 + children < rows.Count
                           && rows[index + 1 + children].Indent > 0)
                    {
                        children++;
                    }

                    if (children > 0)
                    {
                        this.AddGroup(row, rows.GetRange(index + 1, children), shownSource);
                        index += children;
                        continue;
                    }

                    if (!blockOpened && IsQualityBlockRow(row))
                    {
                        blockOpened = true;
                        this.tableModel.Rows.Add(this.MakeRuleRow());
                        this.tableModel.Rows.Add(this.MakeHeaderRow());
                    }

                    if (row.Kind == FsaReportRowKind.Quality)
                    {
                        foreach (Row made in this.MakeQualityRows(row))
                        {
                            this.tableModel.Rows.Add(made);
                        }

                        continue;
                    }

                    bool residual = row.Kind == FsaReportRowKind.Residual;
                    // (`AMBER284`) у строки «только по бета» — шаг узлов подложки в подсказке
                    this.tableModel.Rows.Add(this.MakeRow(
                        row, residual ? residualCaption : null,
                        residual ? residualHint : BetaOnlyRowTip(row, shownSource)));
                }
            }
            finally
            {
                this.reportTable.EndUpdate();
            }

            this.RestoreSelection(keep);
            this.FitColumns();
            this.UpdateAvailability();
        }

        /// <summary>
        /// (`AMBER320`) Родитель и его потомки — одной группой XPTable: родитель строкой
        /// таблицы, потомки — его подстроками (<see cref="Row.SubRows"/>, XPTable ставит их
        /// в <see cref="TableModel.Rows"/> сразу за родителем), раскрытие — из памяти сеанса
        /// (<see cref="ExpandedGroups"/>), по умолчанию свёрнуто.
        ///
        /// Свёрнутый родитель — решение Amber 07.11.2026 вопросником, дословно: «Сумму и число
        /// строк (Рекомендую)»: значение — сумма отсчётов потомков ТЕМ ЖЕ форматом, что у
        /// потомка (<c>FSASumPeakLineCounts</c>, число инвариантной культурой), подпись — с
        /// числом скрытых строк. Сумма — из <see cref="FsaReportRow.Counts"/>, не из текста;
        /// правило одно на все группы, заголовок собственной активности кристалла тоже (решение
        /// Amber 08.11.2026 вопросником: «Сумма отсчётов (Рекомендую)»). Если хоть у одного
        /// потомка отсчётов числом нет, суммы нет, только число строк.
        /// Раскрытый родитель — ровно прежний вид строки.
        /// </summary>
        void AddGroup(FsaReportRow parent, List<FsaReportRow> children, FsaResult shownSource)
        {
            Row parentRow = this.MakeRow(parent, null, BetaOnlyRowTip(parent, shownSource), true);
            this.tableModel.Rows.Add(parentRow);

            double sum = 0.0;
            bool summable = true;
            foreach (FsaReportRow child in children)
            {
                parentRow.SubRows.Add(this.MakeRow(child, null, BetaOnlyRowTip(child, shownSource), false, true));
                if (double.IsNaN(child.Counts) || double.IsInfinity(child.Counts))
                {
                    summable = false;
                }
                else
                {
                    sum += child.Counts;
                }
            }

            Cell name = parentRow.Cells[0];
            Cell value = parentRow.Cells[1];
            string count = children.Count.ToString(CultureInfo.InvariantCulture);
            var group = new RowGroup
            {
                Key = GroupKey(parent),
                Hidden = children.Count,
                ExpandedCaption = name.Text,
                ExpandedValue = value.Text,
                ExpandedHint = name.ToolTipText,
                CollapsedCaption = string.Format(CultureInfo.InvariantCulture, OwnText(KeyGroupCaption), name.Text, count),
                CollapsedValue = summable
                    ? string.Format(CultureInfo.InvariantCulture, Resources.FSASumPeakLineCounts,
                                    sum.ToString("f0", CultureInfo.InvariantCulture))
                    : value.Text
            };
            bool one = OnePeakForm(children.Count);
            string tip = string.Format(CultureInfo.InvariantCulture,
                                       OwnText(summable ? (one ? KeyGroupTipSumOne : KeyGroupTipSum)
                                                        : (one ? KeyGroupTipCountOne : KeyGroupTipCount)), count);
            // (третий заход) подсказка, которая лишь повторяет подпись (у строки нет своей подсказки
            // модели), не ставится перед строкой о свёртке: подпись и так видна
            group.CollapsedHint = string.IsNullOrEmpty(group.ExpandedHint)
                                  || string.Equals(group.ExpandedHint, group.ExpandedCaption, StringComparison.Ordinal)
                ? tip : group.ExpandedHint + Environment.NewLine + tip;
            name.Tag = group;

            ApplyGroupState(parentRow, ExpandedGroups.Contains(group.Key));
        }

        /// <summary>(`AMBER320`) Группа строки таблицы; null — строка не родитель.</summary>
        static RowGroup GroupOf(Row row)
        {
            return row != null && row.Cells.Count > 0 && row.Cells[0] != null ? row.Cells[0].Tag as RowGroup : null;
        }

        /// <summary>(`AMBER320`) Показать родителя раскрытым или свёрнутым: подстроки и текст ячеек.</summary>
        static void ApplyGroupState(Row parentRow, bool expanded)
        {
            RowGroup group = GroupOf(parentRow);
            if (group == null)
            {
                return;
            }

            parentRow.ExpandSubRows = expanded;
            Cell name = parentRow.Cells[0];
            Cell value = parentRow.Cells[1];
            name.Text = expanded ? group.ExpandedCaption : group.CollapsedCaption;
            name.ToolTipText = expanded ? group.ExpandedHint : group.CollapsedHint;
            value.Text = expanded ? group.ExpandedValue : group.CollapsedValue;
            value.ToolTipText = value.Text;
        }

        /// <summary>
        /// (`AMBER320`) Раскрыть или свернуть группу строки <paramref name="rowIndex"/> — ТЕМ ЖЕ
        /// путём, каким это делает щелчок по стрелке (<see cref="ReportTable_CellMouseDown"/>).
        /// false — строка не родитель.
        /// </summary>
        public bool SetGroupExpanded(int rowIndex, bool expanded)
        {
            if (rowIndex < 0 || rowIndex >= this.tableModel.Rows.Count)
            {
                return false;
            }

            Row row = this.tableModel.Rows[rowIndex];
            if (GroupOf(row) == null)
            {
                return false;
            }

            if (row.ExpandSubRows != expanded)
            {
                this.ToggleGroup(row);
            }

            return true;
        }

        /// <summary>
        /// (`AMBER320`, второй заход 08.11.2026) Свёртка с клавиатуры — по обычаю дерева Windows,
        /// у ВЫБРАННОЙ строки и без модификаторов: «→» у свёрнутого родителя — раскрыть, у
        /// раскрытого — перейти на первого потомка; «←» у раскрытого родителя — свернуть, у
        /// потомка — перейти на родителя; Enter и Пробел у родителя — раскрыть/свернуть. Прочее
        /// (стрелки на строке без группы, вверх/вниз, страницы) — штатная обработка таблицы: ключ
        /// забирается (<c>Handled</c>) только когда он что-то сделал с группой.
        /// </summary>
        void ReportTable_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers != Keys.None || this.suspendSelection)
            {
                return;
            }

            int[] selected = this.tableModel.Selections.SelectedIndicies;
            if (selected == null || selected.Length == 0 || selected[0] < 0 || selected[0] >= this.tableModel.Rows.Count)
            {
                return;
            }

            Row row = this.tableModel.Rows[selected[0]];
            bool parent = GroupOf(row) != null;
            bool child = row.Parent != null && GroupOf(row.Parent) != null;
            bool handled = false;
            switch (e.KeyCode)
            {
                case Keys.Right:
                    if (parent && !row.ExpandSubRows)
                    {
                        this.ToggleGroup(row);
                        handled = true;
                    }
                    else if (parent && row.SubRows.Count > 0)
                    {
                        this.SelectRowByKey(row.SubRows[0]);
                        handled = true;
                    }

                    break;

                case Keys.Left:
                    if (parent && row.ExpandSubRows)
                    {
                        this.ToggleGroup(row);
                        handled = true;
                    }
                    else if (child)
                    {
                        this.SelectRowByKey(row.Parent);
                        handled = true;
                    }

                    break;

                case Keys.Enter:
                case Keys.Space:
                    if (parent)
                    {
                        this.ToggleGroup(row);
                        handled = true;
                    }

                    break;
            }

            if (handled)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        /// <summary>(`AMBER320`) Выбор строки клавишей: выбор и фокус ячейки — туда же, как у стрелок таблицы.</summary>
        void SelectRowByKey(Row target)
        {
            int index = target.Index;
            this.tableModel.Selections.SelectCell(index, 0);
            this.reportTable.FocusedCell = new CellPos(index, 0);
            this.reportTable.EnsureVisible(index, 0);
        }

        /// <summary>
        /// (`AMBER320`) Щелчок по стрелке: группа меняет вид, память сеанса — запись. Выбор,
        /// стоявший на скрываемом потомке, переезжает на родителя: на скрытой строке выбора быть
        /// не должно, а слой у родителя тот же (строки сумм-пиков, вылета, рассеяния несут слой
        /// своего нуклида). (`AMBER327`) Подсветка графика при этом переходит с пика-потомка на
        /// подслой родителя целиком — строка группы выделяет всех своих потомков.
        /// </summary>
        void ToggleGroup(Row parentRow)
        {
            RowGroup group = GroupOf(parentRow);
            if (group == null)
            {
                return;
            }

            bool expanded = !parentRow.ExpandSubRows;
            if (expanded)
            {
                ExpandedGroups.Add(group.Key);
            }
            else
            {
                ExpandedGroups.Remove(group.Key);
            }

            bool selectionHidden = false;
            if (!expanded)
            {
                foreach (Row child in parentRow.SubRows)
                {
                    if (child.AnyCellsSelected)
                    {
                        selectionHidden = true;
                    }
                }
            }

            ApplyGroupState(parentRow, expanded);
            if (selectionHidden)
            {
                this.tableModel.Selections.SelectCell(parentRow.Index, 0);
            }

            this.FitColumns();
            this.reportTable.Invalidate();
        }

        /// <summary>
        /// (`A247`) Строка НЕ О СОСТАВЕ — с неё начинается блок качества. Род
        /// читается из <see cref="FsaReportRow.Kind"/>, а не из текста: «фон не
        /// вычтен», невязка и качество не компоненты, а мера разбора, и стоять
        /// в одном списке с `Pb-214 – Ra-226 chain` им нечего.
        /// </summary>
        static bool IsQualityBlockRow(FsaReportRow row)
        {
            return row.Kind == FsaReportRowKind.NoBackground
                   || row.Kind == FsaReportRowKind.Residual
                   || row.Kind == FsaReportRowKind.Quality;
        }

        /// <summary>(`A247`) Черта над блоком: строка в три пикселя, залитая целиком.</summary>
        Row MakeRuleRow()
        {
            var cells = new[] { new Cell(string.Empty), new Cell(string.Empty) };
            foreach (Cell cell in cells)
            {
                cell.BackColor = QualityRuleColor;
            }

            var row = new Row(cells);
            row.Height = QualityRuleHeight;
            row.Tag = ServiceRow(string.Empty, string.Empty);
            return row;
        }

        /// <summary>(`A247`) Заголовок блока — полужирным, без значения.</summary>
        Row MakeHeaderRow()
        {
            string text = OwnText(KeyQualityHeader);
            var name = new Cell(text);
            name.Font = this.headerFont;
            name.ForeColor = Color.Black;
            name.ToolTipText = text;
            var row = new Row(new[] { name, new Cell(string.Empty) });
            row.Tag = ServiceRow(text, string.Empty);
            return row;
        }

        /// <summary>
        /// (`A247`) Строка качества, развёрнутая в перечень с подписями.
        ///
        /// Первой идёт χ²/ndf с числом модели ДОСЛОВНО, за ней — по строке на
        /// каждую пометку прежнего хвоста `· matrix · summing (no eff) !`,
        /// каждая с полной подписью и своим словом состояния. Пометки читаются
        /// из признаков РЕЗУЛЬТАТА, а не разбором локализованного текста:
        /// разбирать собранную строку обратно — тот же грех, за который род
        /// строки вынесен в <see cref="FsaReportRowKind"/>.
        ///
        /// Три первые пометки печатаются ВСЕГДА (матрица, кривая,
        /// суммирование): человек обязан видеть, что учтено, а не гадать по
        /// отсутствию слова. Остальные — только когда есть о чём сказать:
        /// отвергнутый фон (`S44`), край сетки дрейфа и подавленный состав это
        /// происшествия, и строка, стоящая всегда, их обесценила бы.
        /// </summary>
        List<Row> MakeQualityRows(FsaReportRow quality)
        {
            var made = new List<Row>();
            made.Add(this.MakeRow(quality, OwnText(KeyChi2Row)));

            FsaResult result = this.presentation != null ? this.presentation.Source : null;
            if (result == null)
            {
                return made;
            }

            // (`A281`) МНОЖИТЕЛЬ ПОГРЕШНОСТЕЙ — сразу под χ²/ndf: он из него и
            // считан, и читать их порознь нельзя.
            //
            // ⛔ Печатается ВСЕГДА, в том числе ровно `1.000`. Молчание на
            // зажатом множителе неотличимо от «признак не завёлся», а именно
            // единица и есть тот случай, ради которого число на экране нужнее
            // всего: она означает, что порог тут — сырые сигмы, а на соседнем
            // спектре тот же порог означает восемь.
            //
            // ⛔ Число печатается ОДИН раз и явной инвариантной культурой
            // (`A242`): разделитель дробной части — всегда точка, группировки
            // разрядов нет.
            made.Add(this.MakeNumberRow(
                KeyInflationRow,
                result.SigmaInflation.ToString("F3", CultureInfo.InvariantCulture),
                OwnText(KeyInflationTip)));

            // ⛔ (`AMBER274`, решение Amber 02.11.2026 вопросником, дословно: «MDA под порог z ≥ 3
            // (Рекомендую)») ПРАВИЛО ПРЕДЕЛОВ НАЗВАНО — сразу под множителем, которым пределы
            // надуты: порог решения — порог отсева значимостью (тот, которым строка состава
            // признаётся найденной), β — квантилем ISO. Значение — сам порог, подсказка — уровни.
            string limitRule = LimitRuleText(result);
            if (limitRule != null)
            {
                made.Add(this.MakeNumberRow(KeyLimitRuleRow, limitRule, LimitRuleTip(result)));
            }

            // (`AMBER269`; решение Amber 02.11.2026 «Да, настройкой FSA с умолчанием 40 (Рекомендую)»)
            // С КАКОЙ ГРАНИЦЫ ШЁЛ РАЗБОР — числом; подсказка называет источник: поле окна или
            // правило порога (поле равно нулю).
            string fitBound = FitBoundText(result);
            if (fitBound != null)
            {
                made.Add(this.MakeNumberRow(KeyFitBoundRow, fitBound, FitBoundTip(result)));
            }

            bool oldFormat = this.presentation.MatrixOldFormat;
            // (`AMBER208` (в), П236) матрица склада ЕСТЬ, но посчитана по другой
            // геометрии, чем кривая разбора (KCl: кривая из файла спектра старше
            // кривой прибора) — «устарела», а не безликое «не учтена»
            bool matrixStale = !result.ResponseMatrixUsed && this.session != null
                               && this.session.ResponseMatrixSkip == FsaMatrixSkip.StaleGeometry;
            // (`AMBER202`) матрица, приехавшая В ФАЙЛЕ СПЕКТРА, называет источник
            bool matrixFromSpectrum = result.ResponseMatrixUsed && this.session != null
                                      && this.session.ResponseMatrixFromSpectrum;
            // ⛔ (`AMBER284`, решение Amber 02.11.2026 вопросником, дословно: «Объединить со строкой
            // выше, где уже написано - нет Матрицы отклика. Остальное убрать в подсказку.») Отдельной
            // строки-предупреждения «Нет матрицы отклика: …» (`AMBER156` (в), П195) больше нет: её
            // цена словами — подсказкой этой строки, а признак происшествия (красное значение и
            // `Warning` у строки) — здесь же, при том же условии: разбор с кривой и без матрицы.
            string matrixTip = MatrixNotUsedTip(result);
            Row matrixRow = this.MakeMarkRow(KeyMatrixRow,
                                             matrixFromSpectrum
                                                 ? Resources.FSAReportMatrixUsedFromSpectrum
                                                 : OwnText(result.ResponseMatrixUsed
                                                               ? KeyMatrixUsed
                                                               : matrixStale ? KeyMatrixStale
                                                               : oldFormat ? KeyMatrixOldFormat : KeyMatrixNotUsed),
                                             result.ResponseMatrixUsed,
                                             matrixTip != null);
            if (matrixTip != null)
            {
                matrixRow.Cells[0].ToolTipText = matrixTip;
                matrixRow.Cells[1].ToolTipText = matrixTip;
            }

            made.Add(matrixRow);
            // (`AMBER202`) матрица из файла спектра ОТВЕРГНУТА — причина словами
            string spectrumMatrixRefusal = this.session != null ? this.session.SpectrumMatrixRefusal : "";
            if (!result.ResponseMatrixUsed && !string.IsNullOrEmpty(spectrumMatrixRefusal))
            {
                made.Add(this.MakeMarkRowText(
                    string.Format(CultureInfo.InvariantCulture, Resources.FSAReportSpectrumMatrixRefusedRow,
                                  spectrumMatrixRefusal),
                    Resources.FSAReportSpectrumMatrixRefusedValue, false, true));
            }
            // (`AMBER208` (в)) причина словами и лечение: пересчитать матрицу или
            // выбрать кривую прибора вместо кривой из файла спектра
            if (matrixStale)
            {
                made.Add(this.MakeMarkRowText(OwnText(KeyMatrixStaleRow), OwnText(KeyMatrixStaleValue), false, true));
            }
            // (`AMBER34`, решение Amber 15.09.2026 «Разбор идёт, Бк скрыты с
            // причиной») Кривая СЦЕНЫ ПОЛЯ учтена формой, а беккерели из
            // разбора не выводятся — причина стоит В ТОЙ ЖЕ строке, где
            // «учтена/не учтена», и красным с вниманием: это не поломка, а
            // предупреждение читающему числа. Строка — из общих ресурсов
            // приложения (в двух языках), как и прочие слова разбора.
            bool fieldCurve = result.EfficiencyUsed && result.EfficiencyPerUnitFluence;
            made.Add(this.MakeMarkRow(KeyEfficiencyRow,
                                      fieldCurve
                                          ? Resources.FSAReportEfficiencyFieldCurve
                                          : OwnText(result.EfficiencyUsed ? KeyEfficiencyUsed : KeyEfficiencyNotUsed),
                                      result.EfficiencyUsed && !fieldCurve,
                                      fieldCurve));
            // (`AMBER34`) Матрица сцены поля: суммирование не применялось —
            // причина ПОЧЕМУ, а не только факт, как у `KeySummingNoMatrix`.
            made.Add(this.MakeMarkRow(KeySummingRow,
                                      result.CascadeSummingUsed
                                          ? OwnText(KeySummingUsed)
                                          : result.CascadeSummingRefusedFieldMatrix
                                              ? Resources.FSAReportSummingFieldMatrix
                                              : OwnText(result.ResponseMatrixUsed
                                                            ? KeySummingNotUsed
                                                            : KeySummingNoMatrix),
                                      result.CascadeSummingUsed,
                                      result.CascadeSummingRefusedFieldMatrix));

            // (`AMBER274`, `Р5`/`Д2`) БЕТА ПРОБЫ — когда у матрицы ЕСТЬ раздел электронов
            // (или он был и испорчен): легла ли бета, сколько отсчётов в образах, какая
            // капсула; не взята — почему. У матрицы без раздела строки нет: сказать нечего,
            // разбор идёт прежним путём побитово.
            string sampleBeta = SampleBetaText(result);
            if (sampleBeta != null)
            {
                bool betaUsed = result.SampleBetaKeyOn
                                && result.SampleBetaRefusal == EfficiencyMaker.ElectronSectionRefusal.None;
                Row betaRow = this.MakeMarkRow(KeySampleBetaRow, sampleBeta, betaUsed,
                                               result.SampleBetaKeyOn && !betaUsed);
                // (`AMBER284`) число узлов раздела — в подсказке легшей беты
                string betaTip = SampleBetaTip(result);
                if (betaTip != null)
                {
                    betaRow.Cells[0].ToolTipText = betaTip;
                    betaRow.Cells[1].ToolTipText = betaTip;
                }

                made.Add(betaRow);
            }

            // (`AMBER274`, Ф14) СТРОКИ ПО СПЛОШНОМУ СПЕКТРУ, СНЯТЫЕ ДО ФИТА — происшествие, только
            // когда было: имена и причина (матрицы нет, бета выключена, раздела нет или он не
            // годен), красным: объявленного в составе нуклида в таблице нет.
            string betaOnlyRemoved = BetaOnlyRemovedCaption(result);
            if (betaOnlyRemoved != null)
            {
                made.Add(this.MakeMarkRowText(betaOnlyRemoved, OwnText(KeyBetaOnlyRemovedValue), false, true));
            }

            // (`AMBER274`, решение Amber 02.11.2026 «Редкие узлы + два захода (Рекомендую)») ШАГ
            // УЗЛОВ ПОДЛОЖКИ ПОД СТРОКОЙ БЕТЫ и второй заход гамма-строк — только когда строка
            // беты дошла до фита.
            // ⛔ (`AMBER284`, решение Amber 02.11.2026 «Убрать строку в подсказку строки
            // бета-нуклидов (Рекомендую)»: на виду — только предупреждение, если VIF выше предела)
            // Своей строки блока у шага узлов больше нет: он — подсказкой строк «только по бета»
            // (<see cref="BetaOnlyRowTip"/>). Красная строка — только когда VIF не достиг предела и
            // на самом редком шаге; её подсказка — тот же текст шага узлов.
            string betaKnots = BetaKnotCaption(result);
            if (betaKnots != null && !result.BetaKnotVifMet)
            {
                Row vif = this.MakeMarkRowText(OwnText(KeyBetaKnotVifOverRow), OwnText(KeyBetaKnotVifOverValue),
                                               false, true);
                vif.Cells[0].ToolTipText = betaKnots;
                vif.Cells[1].ToolTipText = betaKnots;
                made.Add(vif);
            }

            // ⛔ (`AMBER284`, решение Amber 02.11.2026 вопросником «Вернуть красной строкой
            // (Рекомендую)») ОТКАЗ ВТОРОГО ЗАХОДА ГАММА-СТРОК — на виду, красной строкой: активности
            // нуклидов с гамма-линиями взяты из первого, более грубого захода (подложка с редкими
            // узлами строки беты). До AMBER284 этот отказ красил строку шага узлов; с её уходом в
            // подсказку он остался только в подсказке. Строка — независимо от VIF: при VIF выше
            // предела рядом стоит и его предупреждение — это два разных факта (бета ненадёжна;
            // гамма грубее). Подсказка — тот же текст шага узлов с причиной словами окна.
            if (betaKnots != null && result.BetaGammaPassRefusal != null)
            {
                Row refused = this.MakeMarkRowText(OwnText(KeyBetaKnotSecondRefusedRow), OwnText(KeyBetaKnotSecondRefusedValue),
                                                   false, true);
                refused.Cells[0].ToolTipText = betaKnots;
                refused.Cells[1].ToolTipText = betaKnots;
                made.Add(refused);
            }

            // (`AMBER274`, К3) ВЕТВИ β С СУММОЙ ВЫШЕ 100 % у нуклидов, чья бета легла в образ.
            string branchExcess = BetaBranchExcessCaption(result);
            if (branchExcess != null)
            {
                made.Add(this.MakeMarkRowText(branchExcess, OwnText(KeyBetaBranchExcessValue), false, true));
            }

            // (`AMBER157`, П195; решение Amber 01.10.2026 «Исключать с
            // заверением») ЛИНИИ ВНЕ КРИВОЙ ИСКЛЮЧЕНЫ — происшествие, только
            // когда было: диапазон кривой и линии словами в подписи, короткое
            // слово справа. Числа — один раз и инвариантной культурой (`A242`).
            if (result.EfficiencyOutOfRangeLines != null && result.EfficiencyOutOfRangeLines.Count > 0)
            {
                made.Add(this.MakeMarkRowText(
                    string.Format(CultureInfo.InvariantCulture, OwnText(KeyCurveOutsideRow),
                                  result.EfficiencyMinKev.ToString("0.#", CultureInfo.InvariantCulture),
                                  result.EfficiencyMaxKev.ToString("0.#", CultureInfo.InvariantCulture),
                                  OutOfCurveList(result.EfficiencyOutOfRangeLines)),
                    OwnText(KeyCurveOutsideValue), false, true));
            }

            // (`AMBER159`, П195; решение Amber 01.10.2026 «Предупреждение с
            // величиной») ПЛОТНОСТЬ ПРОБЫ ПРОТИВ СЦЕНЫ — когда в карточке есть
            // и вес, и объём, а у сцены есть проба. Сверяется здесь, при каждом
            // заполнении: вес и объём в отпечаток разбора не входят. Красным —
            // при |расхождении| выше порога; поправки активности нет.
            FsaSampleDensityCheck density = DensityOf(this.ActiveResultData);
            if (density != null)
            {
                made.Add(this.MakeMarkRowText(DensityCaption(density),
                                              density.DeviationPercent.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " %",
                                              !density.Warning, density.Warning));
            }

            // (`AMBER185`, решение Amber 05.10.2026 «Отпечаток + предупреждение»)
            // КРИВАЯ ПОСЧИТАНА ДЛЯ ДРУГОЙ ГЕОМЕТРИИ — отпечаток при кривой не
            // сходится с геометрией конфигурации спектра. Происшествие, только
            // когда было; у кривой без отпечатка (до 05.10.2026) — молчим.
            // ⛔ (`AMBER269` дефект 5, 02.11.2026) ПРИЧИНА — ТА ЖЕ, ЧТО НА ВКЛАДКЕ
            // ПРИБОРА (`DeviceConfigForm.GeometryNotes`): кривая, посчитанная до
            // сдвига точки в капсуле, помечается этой причиной, а не «геометрию
            // правили после расчёта» — отпечаток расходится только строкой сдвига.
            ResultData shown = this.ActiveResultData;
            if (shown != null && shown.Efficiency != null && shown.Efficiency.CurveGeometryMismatch)
            {
                made.Add(this.MakeMarkRowText(shown.Efficiency.CurveBeforeSourceOffset
                                                  ? DeviceConfigForm.CurveBeforeSourceOffsetNote(shown.Efficiency)
                                                  : Resources.EfficiencyCurveOtherGeometry,
                                              Resources.FSAReportCurveOtherGeometryValue, false, true));
            }

            // (`S44`, решение Amber 01.09.2026) ФОН ПОДАН И НЕ ВЗЯТ — причина
            // словами. Стоит первой среди происшествий, как и в хвосте
            // <see cref="FsaPresentationBuilder.QualityText"/>: строка «фон не
            // вычтен» выше по таблице говорит ЧТО, а эта — ПОЧЕМУ, и без неё
            // отказ был виден только пробам (`bg_rejected` у `CorpusFsaProbe`),
            // а человеку приложение молчало.
            //
            // ⚠ Значение — текст ОТКАЗА как он есть у результата, ровно так же,
            // как имя пересилившего образа ниже: переводится он ресурсами
            // самого разбора (`FSABackgroundNoCounts` и соседи), а не здесь.
            if (result.BackgroundRejected != null)
            {
                made.Add(this.MakeMarkRow(KeyBackgroundRejectedRow, result.BackgroundRejected, false, true));
            }

            if (result.DriftOnGridEdge)
            {
                made.Add(this.MakeMarkRow(KeyDriftRow, OwnText(KeyDriftEdge), false, true));
            }

            // (`AMBER17`) ПРИВЯЗКА ШКАЛЫ — печатается ВСЕГДА при живой матрице:
            // «опор нет, шкала как в калибровке» и «шкала привязана по N
            // опорам, усиление и ноль такие-то» — разные состояния, и молчание
            // в одном из них читалось бы как второе. Число — инвариантной
            // культурой (`A242`); перечень опор — в подсказке строки, потому
            // что в самой строке не поместится: имя, энергия, остаток.
            // (`AMBER156` (а), П195) Привязка идёт и без матрицы — по голым
            // пикам кривой, — и строка печатается при любом разборе с кривой.
            if (result.ResponseMatrixUsed || result.EfficiencyUsed)
            {
                this.AddAnchorRow(made, result);
            }

            if (result.CompositionSuppressed)
            {
                // Имя пересилившего образа — данные результата (`Backscatter`,
                // `Esc-I`, нуклид), но человеку — подписью слоя, той же, что у
                // строки состава (П274 по слову распорядителя, вслед за П275:
                // по-русски здесь стояло служебное «Backscatter» рядом с
                // «Обратное рассеяние» в составе). Нуклид подпись не меняет.
                made.Add(this.MakeMarkRow(KeySuppressedRow, FsaPalette.DisplayName(result.SuppressorName ?? string.Empty), false, true));
            }

            // (`S187`, П152 24.09.2026) РАСХОЖДЕНИЯ ПОСТАВОК ДАННЫХ РАСПАДА —
            // только когда есть о чём сказать (происшествие, а не состояние).
            // Справа — нуклиды, в подсказке — по строке на расхождение: ЧТО
            // разбор взял. Прежде эти слова жили только в примечаниях базы и
            // до человека не доходили.
            if (result.SupplyDiscrepancies != null && result.SupplyDiscrepancies.Count > 0)
            {
                this.AddSupplyRow(made, result.SupplyDiscrepancies);
            }

            // ⛔ (`AMBER199`, 05.10.2026) БАЗА НЕ ПРОЧИТАНА — происшествие,
            // только когда было. Разбор при отказе чтения `nucdb`/`schemedb`/
            // `matdb` идёт без части данных (нуклид без линий, линия без
            // поправки на совпадения), и до этого дня на экране не было ни
            // слова. Справа — первая строка отказа (файл, что читалось,
            // сообщение платформы — переводу не подлежит), в подсказке — все.
            if (result.DatabaseFailures != null && result.DatabaseFailures.Count > 0)
            {
                string first = result.DatabaseFailures[0]
                               + (result.DatabaseFailures.Count > 1
                                      ? string.Format(CultureInfo.InvariantCulture, " (+{0})",
                                                      result.DatabaseFailures.Count - 1)
                                      : string.Empty);
                Row failed = this.MakeMarkRowText(Resources.FSAReportDatabaseFailedRow, first, false, true);
                failed.Cells[1].ToolTipText = string.Join(Environment.NewLine, result.DatabaseFailures.ToArray());
                made.Add(failed);
            }

            // (`AMBER123`, П166 → строка окна П168 28.09.2026) КОЛОНКА НАЛОЖЕНИЙ
            // ЗАКРЕПЛЕНА НА ГРАНИЦЕ R·τ: фит просил больше пар, чем допускает
            // мёртвое время, — избыток брал не наложения, а недостающую модель.
            // Признак был только в результате (`PileUpNotPairs`); происшествие,
            // поэтому кирпичным и только когда было.
            if (result.PileUpNotPairs)
            {
                made.Add(this.MakeMarkRowText(
                    string.Format(CultureInfo.InvariantCulture, OwnText(KeyPileUpCapRow),
                                  result.PileUpUncappedPairs, result.PileUpCapPairs),
                    OwnText(KeyPileUpCapValue), false, true));
            }

            // (`AMBER118`, П168; решение Amber 28.09.2026 «Строка AMBER:
            // предупреждение об интерференции 186 кэВ») ЛИНИЯ СОСТАВА, КОТОРУЮ
            // ДЕЛИТ ПРИРОДНЫЙ СПУТНИК, ОТСУТСТВУЮЩИЙ В СОСТАВЕ. Имена — данные
            // результата (подписи нуклидов переводу не подлежат), числа — один
            // раз и инвариантной культурой (`A242`).
            if (result.LineInterferences != null)
            {
                foreach (FsaLineInterference item in result.LineInterferences)
                {
                    made.Add(this.MakeMarkRowText(
                        string.Format(CultureInfo.InvariantCulture, OwnText(KeyInterferenceRow),
                                      item.LineKev.ToString("F1", CultureInfo.InvariantCulture),
                                      item.Companion ?? string.Empty, item.Reference ?? string.Empty,
                                      item.Component ?? string.Empty,
                                      item.Factor.ToString("F2", CultureInfo.InvariantCulture)),
                        OwnText(KeyInterferenceValue), false, true));
                }
            }

            // (`AMBER236`; решение Amber 19.10.2026 вопросником, дословно: «Всегда, со
            // словом-вердиктом», «Массовая доля, %», «σ разбора + разброс членов»)
            // ИЗОТОПНЫЙ СОСТАВ ЭЛЕМЕНТА: доля, σ, разброс по членам (или «только
            // статистика»), природная доля и приговор словом. Имена — данные результата,
            // числа — инвариантной культурой (`A242`).
            if (result.IsotopeRatios != null)
            {
                foreach (FsaIsotopeRatio item in result.IsotopeRatios)
                {
                    string spread = double.IsNaN(item.LowPercent) || double.IsNaN(item.HighPercent)
                        ? OwnText(KeyIsotopeStatOnly)
                        : string.Format(CultureInfo.InvariantCulture, OwnText(KeyIsotopeSpread),
                                        Math.Min(item.LowPercent, item.HighPercent).ToString("F3", CultureInfo.InvariantCulture),
                                        Math.Max(item.LowPercent, item.HighPercent).ToString("F3", CultureInfo.InvariantCulture));
                    string verdict = OwnText(item.Verdict == FsaIsotopeVerdict.Depleted ? KeyIsotopeDepleted
                                             : item.Verdict == FsaIsotopeVerdict.Enriched ? KeyIsotopeEnriched
                                             : KeyIsotopeNatural);
                    made.Add(this.MakeMarkRowText(
                        string.Format(CultureInfo.InvariantCulture, OwnText(KeyIsotopeRow),
                                      item.Isotope ?? string.Empty, item.Others ?? string.Empty,
                                      item.MassPercent.ToString("F3", CultureInfo.InvariantCulture),
                                      item.SigmaPercent.ToString("F3", CultureInfo.InvariantCulture),
                                      spread,
                                      item.NaturalPercent.ToString("F3", CultureInfo.InvariantCulture)),
                        verdict, item.Verdict == FsaIsotopeVerdict.Natural, item.Verdict != FsaIsotopeVerdict.Natural));
                }
            }

            // ⛔ (`AMBER269`; решения Amber 02.11.2026 вопросником, дословно: «Да, по признакам
            // (Рекомендую)», «Вариант 1. Только заменить фразу фон перевычтен на более понятную
            // человеку», «Фон больше спектра» (Рекомендую)) ДВА ПРЕДУПРЕЖДЕНИЯ ПО ПОЛОСЕ —
            // красным, без отказа: «фон больше спектра» (после вычета фона счёт ниже нуля; z — в
            // подсказке) и «модель не описывает низ спектра» (образы выше всего измеренного
            // спектра). Правило — у `FsaAnalyzer.JudgeBandWarnings`; числа — инвариантной
            // культурой (`A242`).
            string backgroundAbove = BackgroundOversubtractedText(result);
            // (`AMBER295`) снятые участки шире окна признака: одна строка о снятых, окно признака — в её подсказке
            string masked = BackgroundMaskText(result);
            if (masked != null)
            {
                Row maskRow = this.MakeMarkRowText(masked, OwnText(result.BackgroundMaskBands.Count == 1 ? KeyBackgroundMaskValueOne : KeyBackgroundMaskValue), false, true);
                string tip = BackgroundMaskTip(result);
                maskRow.Cells[0].ToolTipText = tip;
                maskRow.Cells[1].ToolTipText = tip;
                made.Add(maskRow);
            }
            else if (backgroundAbove != null)
            {
                Row above = this.MakeMarkRowText(backgroundAbove, OwnText(KeyBackgroundAboveValue), false, true);
                string tip = BackgroundOversubtractedTip(result);
                above.Cells[0].ToolTipText = tip;
                above.Cells[1].ToolTipText = tip;
                made.Add(above);
            }

            // (`AMBER295`) нож при красной строке: откуда активность
            string knife = BackgroundKnifeText(result);
            if (knife != null)
            {
                Row knifeRow = this.MakeMarkRowText(knife, BackgroundKnifeValue(result), false, true);
                string tip = BackgroundKnifeTip(result);
                knifeRow.Cells[0].ToolTipText = tip;
                knifeRow.Cells[1].ToolTipText = tip;
                made.Add(knifeRow);
            }

            string imagesAbove = ImagesAboveSpectrumText(result);
            if (imagesAbove != null)
            {
                // (`AMBER284`) z — в подсказке, как у «фон больше спектра»
                Row images = this.MakeMarkRowText(imagesAbove, OwnText(KeyImagesAboveValue), false, true);
                string tip = ImagesAboveSpectrumTip(result);
                images.Cells[0].ToolTipText = tip;
                images.Cells[1].ToolTipText = tip;
                made.Add(images);
            }

            // (`AMBER210`, П238; решение Amber 07.10.2026 вопросником, дословно: «Только
            // отчёт FSA, строка на пик») ПИК В ПОЛОСЕ, НЕ ПОКРЫТЫЙ СОСТАВОМ: энергия,
            // избыток над моделью в процентах, значимость и линии библиотеки приложения
            // рядом («Библиотека приложения целиком»). Имена — данные результата,
            // числа — один раз и инвариантной культурой (`A242`).
            if (result.UncoveredPeaks != null)
            {
                foreach (FsaUncoveredPeak item in result.UncoveredPeaks)
                {
                    var names = new List<string>();
                    foreach (FsaUncoveredCandidate candidate in item.Candidates)
                    {
                        names.Add(candidate.Nuclide + " " + candidate.EnergyKev.ToString("F1", CultureInfo.InvariantCulture));
                    }

                    made.Add(this.MakeMarkRowText(
                        string.Format(CultureInfo.InvariantCulture, OwnText(KeyUncoveredRow),
                                      item.EnergyKev.ToString("F1", CultureInfo.InvariantCulture),
                                      (100.0 * item.ExcessShare).ToString("F0", CultureInfo.InvariantCulture),
                                      item.Z.ToString("F1", CultureInfo.InvariantCulture),
                                      names.Count > 0 ? string.Join(", ", names) : OwnText(KeyUncoveredNone)),
                        OwnText(KeyUncoveredValue), false, true));
                }
            }

            // (`AMBER221`, решение Amber 12.10.2026 вопросником, дословно: «Чинить
            // сейчас») РОДИТЕЛЬ АКТИВНОГО СЕТА, НЕ ВОШЕДШИЙ В СОСТАВ, — строкой на
            // каждого, с причиной. Прежде он выпадал молча: K-40 в сете, подпись
            // «K-40» над пиком — и ни компонента, ни слова.
            if (result.SetOmissions != null)
            {
                foreach (FsaSetOmission item in result.SetOmissions)
                {
                    made.Add(this.MakeMarkRowText(
                        string.Format(CultureInfo.InvariantCulture, OwnText(KeySetOmittedRow),
                                      item.Nuclide ?? string.Empty, SetOmissionReason(item)),
                        OwnText(KeySetOmittedValue), false, true));
                }
            }

            return made;
        }

        /// <summary>
        /// (`AMBER269`; решение Amber 02.11.2026 «Фон больше спектра» (Рекомендую)) Подпись строки
        /// «фон больше спектра» словами окна — полоса и вероятная причина, без z (z — в подсказке,
        /// <see cref="BackgroundOversubtractedTip"/>); null — признака нет. Открыта пробе: приёмка
        /// читает ТУ ЖЕ подпись, что таблица. Числа — инвариантной культурой (`A242`).
        /// </summary>
        public static string BackgroundOversubtractedText(FsaResult result)
        {
            FsaBandWarning flag = result != null ? result.BackgroundOversubtracted : null;
            if (flag == null)
            {
                return null;
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyBackgroundAboveRow),
                                 flag.LoKev.ToString("F0", CultureInfo.InvariantCulture),
                                 flag.HiKev.ToString("F0", CultureInfo.InvariantCulture));
        }

        /// <summary>(`AMBER295`) Строка автомаски фона: энергии участков; null — снято нечего. Открыта пробе.</summary>
        public static string BackgroundMaskText(FsaResult result)
        {
            if (result == null || result.BackgroundMaskBands == null || result.BackgroundMaskBands.Count == 0)
            {
                return null;
            }

            var parts = new List<string>();
            foreach (FsaBandWarning band in result.BackgroundMaskBands)
            {
                parts.Add(band.LoKev.ToString("F0", CultureInfo.InvariantCulture) + "–" + band.HiKev.ToString("F0", CultureInfo.InvariantCulture));
            }

            return string.Format(CultureInfo.InvariantCulture,
                                 OwnText(parts.Count == 1 ? KeyBackgroundMaskRowOne : KeyBackgroundMaskRow), string.Join(", ", parts));
        }

        /// <summary>(`AMBER295`) Подсказка строки автомаски: число каналов и наиболее значимое окно признака. Открыта пробе.</summary>
        public static string BackgroundMaskTip(FsaResult result)
        {
            FsaBandWarning flag = result != null ? result.BackgroundOversubtracted : null;
            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyBackgroundMaskTip),
                                 result != null ? result.BackgroundMaskChannels.ToString(CultureInfo.InvariantCulture) : "0",
                                 flag != null ? flag.LoKev.ToString("F0", CultureInfo.InvariantCulture) : "—",
                                 flag != null ? flag.HiKev.ToString("F0", CultureInfo.InvariantCulture) : "—",
                                 flag != null ? flag.Z.ToString("F1", CultureInfo.InvariantCulture) : "—",
                                 flag != null ? flag.Ratio.ToString("F3", CultureInfo.InvariantCulture) : "—");
        }

        /// <summary>(`AMBER295`) Строка ножа при красной строке; null — ножа не было. Открыта пробе.</summary>
        public static string BackgroundKnifeText(FsaResult result)
        {
            return result != null && result.BackgroundBandKnifeApplied
                ? string.Format(CultureInfo.InvariantCulture, OwnText(KeyBackgroundKnifeRow), result.BackgroundKnifeFloorKev.ToString("F0", CultureInfo.InvariantCulture))
                : null;
        }

        static string BackgroundKnifeValue(FsaResult result)
        {
            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyBackgroundKnifeValue), result.BackgroundKnifeFloorKev.ToString("F0", CultureInfo.InvariantCulture));
        }

        /// <summary>(`AMBER295`) Подсказка строки ножа. Открыта пробе.</summary>
        public static string BackgroundKnifeTip(FsaResult result)
        {
            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyBackgroundKnifeTip),
                                 result != null ? result.BackgroundKnifeFloorKev.ToString("F0", CultureInfo.InvariantCulture) : "—",
                                 result != null ? result.KnifeUntiedBins.ToString(CultureInfo.InvariantCulture) : "0",
                                 result != null ? result.KnifeBinsBelow.ToString(CultureInfo.InvariantCulture) : "0");
        }

        /// <summary>
        /// (`AMBER269`) Подсказка строки «фон больше спектра»: значимость окна и во сколько раз фон в
        /// нём больше измеренного; null — признака нет. Открыта пробе.
        /// </summary>
        public static string BackgroundOversubtractedTip(FsaResult result)
        {
            FsaBandWarning flag = result != null ? result.BackgroundOversubtracted : null;
            if (flag == null)
            {
                return null;
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyBackgroundAboveTip),
                                 flag.Z.ToString("F1", CultureInfo.InvariantCulture),
                                 flag.Ratio.ToString("F3", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER269`; решение Amber 02.11.2026 «Да, настройкой FSA с умолчанием 40 (Рекомендую)»)
        /// Значение строки «нижняя граница разбора»: энергия, с которой шёл разбор, кэВ (первый канал
        /// фита); null — у результата её нет. Открыта пробе. Числа — инвариантной культурой (`A242`).
        /// </summary>
        public static string FitBoundText(FsaResult result)
        {
            if (result == null)
            {
                return null;
            }

            double kev = result.FitLowerBoundKev > 0.0 ? result.FitLowerBoundKev : result.FitStartKev;
            return double.IsNaN(kev) ? null : kev.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>(`AMBER269`) Подсказка строки «нижняя граница разбора»: настройка окна или правило порога.</summary>
        public static string FitBoundTip(FsaResult result)
        {
            if (result == null)
            {
                return null;
            }

            return result.FitLowerBoundKev > 0.0
                ? string.Format(CultureInfo.InvariantCulture, OwnText(KeyFitBoundTipSetting),
                                result.FitLowerBoundKev.ToString("F1", CultureInfo.InvariantCulture))
                : string.Format(CultureInfo.InvariantCulture, OwnText(KeyFitBoundTipRule),
                                double.IsNaN(result.FitStartKev) ? "—"
                                    : result.FitStartKev.ToString("F1", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER269`; `AMBER284`, решение Amber 02.11.2026 «Модель выше спектра в 30.6–42.7 кэВ
        /// (в 2.06 раза) (Рекомендую)», продолжение «: низ спектра не описан», z — в подсказку)
        /// Подпись строки «модель выше спектра» словами окна: полоса и во сколько раз, без z
        /// (<see cref="ImagesAboveSpectrumTip"/>); null — признака нет. Открыта пробе. Числа —
        /// инвариантной культурой (`A242`).
        /// </summary>
        public static string ImagesAboveSpectrumText(FsaResult result)
        {
            FsaBandWarning flag = result != null ? result.ImagesAboveSpectrum : null;
            if (flag == null)
            {
                return null;
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyImagesAboveRow),
                                 flag.LoKev.ToString("F0", CultureInfo.InvariantCulture),
                                 flag.HiKev.ToString("F0", CultureInfo.InvariantCulture),
                                 flag.Ratio.ToString("F2", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER284`) Подсказка строки «модель выше спектра»: значимость окна; null — признака
        /// нет. Открыта пробе.
        /// </summary>
        public static string ImagesAboveSpectrumTip(FsaResult result)
        {
            FsaBandWarning flag = result != null ? result.ImagesAboveSpectrum : null;
            if (flag == null)
            {
                return null;
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyImagesAboveTip),
                                 flag.Z.ToString("F1", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER221`) Причина словами окна; числа — один раз и инвариантной
        /// культурой (`A242`). <see cref="FsaParentVerdict.None"/> — родитель
        /// кандидатом не был: ни один пик им не подписан.
        /// </summary>
        static string SetOmissionReason(FsaSetOmission item)
        {
            switch (item.Reason)
            {
                case FsaParentVerdict.NoLines:
                    return OwnText(KeySetOmittedNoLines);
                case FsaParentVerdict.NotConfirmed:
                    return OwnText(KeySetOmittedNotConfirmed);
                case FsaParentVerdict.BelowCoverage:
                    return string.Format(CultureInfo.InvariantCulture, OwnText(KeySetOmittedBelowCoverage),
                                         item.Matched.ToString(CultureInfo.InvariantCulture),
                                         item.Expected.ToString(CultureInfo.InvariantCulture),
                                         (100.0 * item.Coverage).ToString("F0", CultureInfo.InvariantCulture),
                                         (100.0 * item.Threshold).ToString("F0", CultureInfo.InvariantCulture));
                case FsaParentVerdict.NotNovel:
                    return OwnText(KeySetOmittedNotNovel);
                default:
                    return OwnText(KeySetOmittedNoPeak);
            }
        }

        /// <summary>
        /// (`S187`) Строка «поставки данных распада расходятся»: справа —
        /// родители через запятую (подпись нуклида та же, что в составе), в
        /// подсказке — по строке на расхождение. Числа — один раз и
        /// инвариантной культурой (`A242`); цвета нет, как у строк с числом:
        /// это не суждение о разборе, а сказанное о данных.
        /// </summary>
        void AddSupplyRow(List<Row> made, List<CascadeAtomicData.SupplyDiscrepancy> found)
        {
            var parents = new List<string>();
            var tip = new System.Text.StringBuilder(OwnText(KeySupplyTip));
            foreach (CascadeAtomicData.SupplyDiscrepancy item in found)
            {
                if (item == null)
                {
                    continue;
                }

                string parent = FsaSampleLibrary.PrettyName(item.Parent ?? string.Empty);
                if (!parents.Contains(parent))
                {
                    parents.Add(parent);
                }

                tip.AppendLine();
                tip.Append(SupplyLine(item, parent));
            }

            made.Add(this.MakeNumberRow(KeySupplyRow, string.Join(", ", parents.ToArray()), tip.ToString()));
        }

        /// <summary>(`S187`) Одна строка подсказки: вид расхождения и числа.</summary>
        static string SupplyLine(CascadeAtomicData.SupplyDiscrepancy item, string parent)
        {
            string daughter = FsaSampleLibrary.PrettyName(item.Daughter ?? string.Empty);
            bool share = item.Kind == CascadeAtomicData.SupplyDiscrepancyKind.BetaPlusShareMismatch;
            string format = share ? "F5" : "F3";
            string supply = item.Supply.ToString(format, CultureInfo.InvariantCulture);
            string other = item.Other.ToString(format, CultureInfo.InvariantCulture);
            string taken = share
                ? item.Taken.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)
                : item.Taken.ToString("F3", CultureInfo.InvariantCulture);
            string key;
            switch (item.Kind)
            {
                case CascadeAtomicData.SupplyDiscrepancyKind.BetaPlusModeWidened:
                    key = KeySupplyBetaPlusMode;
                    break;
                case CascadeAtomicData.SupplyDiscrepancyKind.OtherModeWidened:
                    key = KeySupplyOtherMode;
                    break;
                case CascadeAtomicData.SupplyDiscrepancyKind.BranchExcess:
                    key = KeySupplyExcess;
                    break;
                case CascadeAtomicData.SupplyDiscrepancyKind.BetaPlusClamped:
                    key = KeySupplyClamped;
                    break;
                default:
                    key = item.LevelsKept ? KeySupplyShareKept : KeySupplyShareScaled;
                    break;
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(key), parent, daughter, supply, other, taken);
        }

        /// <summary>
        /// (`A247`) Строка пометки: подпись слева, слово состояния справа.
        /// Числа здесь не бывает никогда — только у невязки и χ²/ndf, и оба
        /// берут его у модели.
        ///
        /// (`AMBER11`, задача Amber 09.09.2026: «нужно раскрасить все статусы:
        /// положительные зелёным, отрицательные красным») ЦВЕТ НЕСЁТ СЛОВО
        /// СОСТОЯНИЯ, А НЕ ПОДПИСЬ: серый одинаково окрашивал «used» и «not
        /// used», и человек читал блок словами вместо того, чтобы видеть его
        /// целиком. Подпись остаётся чёрной (решение Amber того же дня,
        /// вопросником: «только значение справа»).
        ///
        /// ⚠ Судится ФАКТ, а не причина (её же решение): «not applied», когда
        /// суммирование выключил сам человек галочкой, — такой же красный, как
        /// «not applied: no response matrix». Окно говорит, ЧТО учтено в
        /// числах, а не одобряет выбор.
        ///
        /// <paramref name="good"/> — статус положительный (учтено); у
        /// происшествий (<paramref name="attention"/>) его не бывает.
        /// </summary>
        Row MakeMarkRow(string captionKey, string value, bool good, bool attention)
        {
            return this.MakeMarkRowText(OwnText(captionKey), value, good, attention);
        }

        /// <summary>
        /// То же, что <see cref="MakeMarkRow"/>, но подпись — готовый текст, а не
        /// ключ: у происшествий с числами (`AMBER123`, `AMBER118`) подпись
        /// собирается форматом и переносится по ширине колонки целиком.
        /// </summary>
        Row MakeMarkRowText(string caption, string value, bool good, bool attention)
        {
            var name = new Cell(caption);
            var cell = new Cell(value ?? string.Empty);
            name.ForeColor = Color.Black;
            cell.ForeColor = good ? MarkGoodColor : MarkBadColor;

            // Подпись переносится по ширине колонки: усечение многоточием и
            // есть та беда, ради которой заведена `A247`.
            name.WordWrap = true;
            // (`AMBER201`, мелочь 6.13, 05.10.2026) Значение переносится тоже:
            // колонка значений 92 px, и длинная причина (отказ базы, строка
            // предупреждения) резалась многоточием — целиком её было видно только
            // в подсказке.
            cell.WordWrap = true;
            name.ToolTipText = caption;
            cell.ToolTipText = cell.Text;
            var row = new Row(new[] { name, cell });
            row.Tag = ServiceRow(caption, cell.Text, attention);
            return row;
        }

        /// <summary>
        /// (`AMBER17`) Строки привязки шкалы — до трёх, по образцу блока
        /// (подпись слева, короткое значение справа; длинное значение колонка
        /// усекает многоточием, и это уже ловили — `A247`): число опор («none»
        /// при нуле — шкала как в калибровке), усиление в процентах и ноль в
        /// кэВ — два последних только при опорах, чтобы «+0.00» на
        /// непривязанной шкале не читалось как результат привязки. Знак у
        /// чисел печатается всегда. Подсказка у всех трёх одна: перечень
        /// опор — компонент, энергия модельного центра, остаток после
        /// привязки, — а следом отвергнутые кандидаты с причиной: без них
        /// «опор нет» неотличимо от «опор не искали».
        /// </summary>
        void AddAnchorRow(List<Row> made, FsaResult result)
        {
            // (`AMBER269`, дефект 4) при откате (А2) опор у отданного разбора нет, но это не
            // «опор нет»: привязка нашлась и отменена как ухудшившая разбор — своё слово.
            // (`AMBER294`) При тормозе отменять было нечего: на экране итог — опор нет, причина строкой ниже
            string brake = AnchorBrakeCaption(result);
            string value = result.ScaleAnchorsUsed > 0
                ? result.ScaleAnchorsUsed.ToString(CultureInfo.InvariantCulture)
                : OwnText(result.AnchorRolledBack && brake == null ? KeyAnchorRolledBack : KeyAnchorNone);

            string rollback = AnchorRollbackCaption(result);
            var tip = new System.Text.StringBuilder();
            if (brake != null)
            {
                // (`AMBER294`) шапка «по которым подстроена шкала» и «отменённой подстройки» спорили бы
                // со строкой причины: подстройки не было, опоры идут под своей шапкой
                tip.Append(AnchorBrakeHint(result));
                tip.AppendLine();
                tip.AppendLine();
                tip.Append(OwnText(AnchorBrakeOnMap(result) ? KeyAnchorBrakeMapTipHead : KeyAnchorBrakeTipHead));
                AppendAnchorLines(tip, result.AnchorRolledBack ? result.AnchorRollbackScaleAnchors : result.ScaleAnchors);
                if (rollback != null)
                {
                    tip.AppendLine();
                    tip.AppendLine();
                    tip.Append(rollback);
                }
            }
            else
            {
                tip.Append(OwnText(KeyAnchorTip));
                AppendAnchorLines(tip, result.ScaleAnchors);
                if (rollback != null)
                {
                    tip.AppendLine();
                    tip.AppendLine();
                    tip.Append(rollback);
                    tip.AppendLine();
                    tip.Append(OwnText(KeyAnchorRollbackTip));
                    AppendAnchorLines(tip, result.AnchorRollbackScaleAnchors);
                }
            }

            made.Add(this.MakeNumberRow(KeyAnchorRow, value, tip.ToString()));
            if (brake != null)
            {
                Row braked = this.MakeMarkRowText(brake, OwnText(AnchorBrakeOnMap(result) ? KeyAnchorBrakeMapValue : KeyAnchorBrakeValue), false, true);
                braked.Cells[0].ToolTipText = tip.ToString();
                made.Add(braked);
            }

            if (rollback != null)
            {
                // (`AMBER294`) при тормозе не «шкала файла»: подстройки не было, значение то же, что у строки причины
                Row rolled = this.MakeMarkRowText(rollback, OwnText(brake != null ? KeyAnchorBrakeValue : KeyAnchorRollbackValue), false, true);
                rolled.Cells[0].ToolTipText = tip.ToString();
                made.Add(rolled);
            }

            if (result.ScaleAnchorsUsed > 0)
            {
                made.Add(this.MakeNumberRow(
                    KeyAnchorGainRow,
                    (100.0 * (result.Gain - 1.0)).ToString("+0.00;-0.00", CultureInfo.InvariantCulture),
                    tip.ToString()));
                made.Add(this.MakeNumberRow(
                    KeyAnchorZeroRow,
                    result.AnchorOffsetKev.ToString("+0.00;-0.00", CultureInfo.InvariantCulture),
                    tip.ToString()));
            }
        }

        /// <summary>(`AMBER17`) Строки опор в подсказку: знак, компонент, центр, остаток, доля, причина.</summary>
        static void AppendAnchorLines(System.Text.StringBuilder tip, List<FsaScaleAnchor> anchors)
        {
            if (anchors == null)
            {
                return;
            }

            foreach (FsaScaleAnchor anchor in anchors)
            {
                tip.AppendLine();
                // (`AMBER294`, проверка экраном 07.11.2026) Опора, чей центр не измерен (в окне нет
                // счёта), несёт остаток NaN — строка печатала «остаток NaN кэВ». Нет числа — нет и
                // слова «остаток»: строка без него.
                if (double.IsNaN(anchor.ShiftKev) || double.IsInfinity(anchor.ShiftKev))
                {
                    tip.Append(string.Format(CultureInfo.InvariantCulture, OwnText(KeyAnchorTipLineNoShift),
                                             anchor.Used ? "+" : "-",
                                             anchor.Component ?? string.Empty,
                                             anchor.ModelKev.ToString("F1", CultureInfo.InvariantCulture),
                                             anchor.PeakShare.ToString("F2", CultureInfo.InvariantCulture)));
                }
                else
                {
                    tip.Append(string.Format(CultureInfo.InvariantCulture, OwnText(KeyAnchorTipLine),
                                             anchor.Used ? "+" : "-",
                                             anchor.Component ?? string.Empty,
                                             anchor.ModelKev.ToString("F1", CultureInfo.InvariantCulture),
                                             anchor.ShiftKev.ToString("+0.00;-0.00", CultureInfo.InvariantCulture),
                                             anchor.PeakShare.ToString("F2", CultureInfo.InvariantCulture)));
                }

                string refusal = AnchorRefusalText(anchor);
                if (!string.IsNullOrEmpty(refusal))
                {
                    tip.Append(string.Format(CultureInfo.InvariantCulture, OwnText(KeyAnchorTipRefusal), refusal));
                }
                // (`F11` (в), П18) у ПРИНЯТОЙ опоры со сдвигом по свету —
                // сам сдвиг: человек видит, что положение линии модели
                // здесь не табличное, а световое. Нуль (координата
                // выключена) не печатается — строка остаётся прежней.
                if (anchor.Used && anchor.LightShiftKev != 0.0)
                {
                    tip.Append(string.Format(CultureInfo.InvariantCulture, OwnText(KeyAnchorTipLight),
                                             anchor.LightShiftKev.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)));
                }
            }
        }

        /// <summary>
        /// (`AMBER294`) Причина в строке опоры — словами окна. Коды ставит отбор опор
        /// <c>FsaAnalyzer</c> (`MeasureAnchors`, выбросы МНК и совместного шага, тормоз, шаг шкалы):
        /// `edge`, `skip`, `narrow`, `z`, `share`, `shift`, `outlier`, `brake`, `not_in_step`.
        /// Прежде словами шли только два последних, и подсказка печатала голое «z» (проверка
        /// экраном 07.11.2026). Код, которого здесь нет, печатается как есть — лучше служебное
        /// слово, чем пустая причина; пробу `ScreenFixesProbe1107` (плечо А) такой код краснит.
        /// </summary>
        static string AnchorRefusalText(FsaScaleAnchor anchor)
        {
            switch (anchor.Refusal)
            {
                case null:
                    return string.Empty;
                case FsaScaleAnchor.RefusalGainBrake:
                    return OwnText(KeyAnchorBrakeRefusal);
                case FsaScaleAnchor.RefusalNotInStep:
                    return OwnText(KeyAnchorNotInStepRefusal);
                case "edge":
                    return OwnText(KeyAnchorEdgeRefusal);
                case "skip":
                    return OwnText(KeyAnchorSkipRefusal);
                case "narrow":
                    return OwnText(KeyAnchorNarrowRefusal);
                case "z":
                    return OwnText(KeyAnchorZRefusal);
                case "share":
                    return OwnText(KeyAnchorShareRefusal);
                case "shift":
                    return OwnText(KeyAnchorShiftRefusal);
                case "outlier":
                    return OwnText(KeyAnchorOutlierRefusal);
                default:
                    return anchor.Refusal;
            }
        }

        /// <summary>
        /// (`AMBER269`, дефект 4) Подпись строки «откат привязки» словами окна: сколько опор и
        /// какое усиление было у отменённого разбора и чем он хуже — χ²/ndf Пуассона с привязкой
        /// и без; null — отката не было. Открыта пробе: приёмка читает ТУ ЖЕ подпись, что
        /// таблица. Числа — инвариантной культурой (`A242`).
        /// </summary>
        public static string AnchorRollbackCaption(FsaResult result)
        {
            if (result == null || !result.AnchorRolledBack)
            {
                return null;
            }

            // (`AMBER294`) опор у отменённого разбора нет: «по 0 пикам (+0.00 %)» — не про то. Усиление у него
            // стоит, а карта шкалы двигалась, она и ухудшила подгонку: `AS80_Cs137_0cm`, порог 3000: усиление
            // 1, опор 0, `AdcScale` 1.0415, `AdcZeroKev` 0.21, χ²/ndf 89.13; с `--zero=calib` — ровно 83.986
            if (result.AnchorBrakedPeaks > 0 || result.AnchorRollbackAnchors == 0)
            {
                return string.Format(CultureInfo.InvariantCulture, OwnText(KeyAnchorBrakeRollbackRow),
                                     result.AnchorRollbackChi2NdfPoisson.ToString("F2", CultureInfo.InvariantCulture),
                                     result.Chi2NdfPoisson.ToString("F2", CultureInfo.InvariantCulture));
            }

            // (`AMBER284`, решение Amber 02.11.2026: «по 1 пикам» склонять по числу) форма «пику» —
            // своим ключом, правило выбора — языка окна (<see cref="OnePeakForm"/>)
            return string.Format(CultureInfo.InvariantCulture,
                                 OwnText(OnePeakForm(result.AnchorRollbackAnchors) ? KeyAnchorRollbackRowOne : KeyAnchorRollbackRow),
                                 result.AnchorRollbackAnchors.ToString(CultureInfo.InvariantCulture),
                                 (100.0 * (result.AnchorRollbackGain - 1.0)).ToString("+0.00;-0.00", CultureInfo.InvariantCulture),
                                 result.AnchorRollbackChi2NdfPoisson.ToString("F2", CultureInfo.InvariantCulture),
                                 result.Chi2NdfPoisson.ToString("F2", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER294`, решения Amber 05.11.2026 вопросником, дословно: «Показать причину, снять
        /// «+» (Рекомендую)», «Шкала не подстроена (Рекомендую)») Подпись строки «почему шкала не
        /// подстроена», когда опоры снял тормоз усиления (`AMBER269`, (А1)); null — тормоз не снимал.
        /// На экран идёт ИТОГ: опор нет, усиления нет. Не «шкала файла»: при матрице без отката карта
        /// света двигает шкалу и при тормозе (`G1S16_K40_Mar`, порог 2000: `AdcScale` 1.0405,
        /// `AdcE0Kev` −19.34). Служебная `AnchorNote` с «опор 1 … усиление a 1.07332» — проход до
        /// тормоза, сюда она не попадает. Число — то же, что в строке опоры в подсказке (центр модели).
        /// Открыта пробе: приёмка читает ТУ ЖЕ подпись, что таблица.
        /// </summary>
        public static string AnchorBrakeCaption(FsaResult result)
        {
            if (result == null || result.AnchorBrakedPeaks <= 0)
            {
                return null;
            }

            string top = result.AnchorBrakedTopKev.ToString("F1", CultureInfo.InvariantCulture);
            bool map = AnchorBrakeOnMap(result);
            return result.AnchorBrakedPeaks == 1
                ? string.Format(CultureInfo.InvariantCulture, OwnText(map ? KeyAnchorBrakeMapRowOne : KeyAnchorBrakeRowOne), top)
                : string.Format(CultureInfo.InvariantCulture, OwnText(map ? KeyAnchorBrakeMapRow : KeyAnchorBrakeRow),
                                result.AnchorBrakedPeaks.ToString(CultureInfo.InvariantCulture), top);
        }

        /// <summary>
        /// (`AMBER294`) Ветка строки при тормозе: усиление стоит, а карта света в отданном разборе есть и
        /// движется (`G1S16_K40_Mar`, порог 2000: `AdcScale` 1.0405, `AdcE0Kev` −19.34). Без карты и при откате
        /// (усиление 1, `AdcScale` 0) — прежняя «Шкала не подстроена». Открыта пробе.
        /// </summary>
        public static bool AnchorBrakeOnMap(FsaResult result)
        {
            return result != null && result.AnchorBrakedPeaks > 0 && !result.AnchorRolledBack && result.AdcScale > 0.0;
        }

        /// <summary>(`AMBER294`) Подсказка к строке тормоза: почему пик у нуля не держит шкалу. Открыта пробе.</summary>
        public static string AnchorBrakeHint(FsaResult result)
        {
            return string.Format(CultureInfo.InvariantCulture, OwnText(AnchorBrakeOnMap(result) ? KeyAnchorBrakeMapTip : KeyAnchorBrakeTip),
                                 result.AnchorBrakeZeroMaxKev.ToString("F0", CultureInfo.InvariantCulture),
                                 result.AnchorBrakedTopLineKev.ToString("F1", CultureInfo.InvariantCulture),
                                 result.AnchorBrakedTopKev.ToString("F1", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER284`, решение Amber 02.11.2026: «Склонять по числу: 1, 21, 31… — «пику»; прочие —
        /// «пикам». EN — «peak»/«peaks».») Нужна ли форма единственного числа
        /// (`FSAReport_AnchorRollbackRowOne`) при <paramref name="count"/> пиках. Правило — языка
        /// окна (`CurrentUICulture`, им же <c>OwnText</c> выбирает `resx`): по-русски 1, 21, 31…, но
        /// не 11, 111 («по 11 пикам»); иначе — только 1. Открыта пробе.
        /// </summary>
        public static bool OnePeakForm(int count)
        {
            if (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru")
            {
                int n = Math.Abs(count);
                return n % 10 == 1 && n % 100 != 11;
            }

            return count == 1;
        }

        /// <summary>
        /// (`A281`) Строка блока с ЧИСЛОМ, а не со словом состояния: подпись
        /// слева, число справа, обе чёрные.
        ///
        /// ⛔ Цвета здесь нет НАРОЧНО, и это не небрежность. `MakeMarkRow`
        /// красит значение зелёным или кирпичным, потому что судит ФАКТ
        /// («учтено» / «не учтено»); у множителя погрешностей такого факта нет
        /// — 8.163 не «плохо», а «порог здесь в восемь раз жёстче сырого».
        /// Покрасить его значило бы вынести суждение, которого решение Amber
        /// не содержит: сказано печатать число.
        /// </summary>
        Row MakeNumberRow(string captionKey, string value, string hint)
        {
            string caption = OwnText(captionKey);
            var name = new Cell(caption);
            var cell = new Cell(value ?? string.Empty);
            name.ForeColor = Color.Black;
            cell.ForeColor = Color.Black;
            name.WordWrap = true;
            name.ToolTipText = string.IsNullOrEmpty(hint) ? caption : hint;
            cell.ToolTipText = name.ToolTipText;
            var row = new Row(new[] { name, cell });
            row.Tag = ServiceRow(caption, cell.Text);
            return row;
        }

        /// <summary>
        /// Модельная строка для служебных строк блока (черта, заголовок,
        /// пометка): у таблицы обязан быть <see cref="Row.Tag"/> известного
        /// рода — по нему читают строки и пробы, и обработчик выбора.
        /// Ленты у таких строк нет, поэтому выбор их ничего не подсвечивает.
        /// </summary>
        static FsaReportRow ServiceRow(string name, string value, bool warning = false)
        {
            return new FsaReportRow
            {
                Kind = FsaReportRowKind.Quality,
                Name = name,
                Value = value,
                Warning = warning
            };
        }

        // ------------------------------------------------------------------
        // (`A246`) Выбор строки состава -> приглушение остальных лент
        // ------------------------------------------------------------------

        /// <summary>Имя слоя выбранного компонента; null — выбора нет (пробы).</summary>
        public string SelectedComponent
        {
            get { return this.selectedLayer; }
        }

        /// <summary>
        /// Выбрать строку состава по имени слоя — ТЕМ ЖЕ путём, каким её
        /// выбирает мышь (пробы). false — такой строки в таблице нет.
        /// Пустое имя снимает выбор.
        /// </summary>
        public bool SelectComponent(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                this.reportTable.TableModel.Selections.Clear();
                this.ForgetSelectedRow();
                this.PushHighlight();
                return true;
            }

            int index = this.RowOfLayer(layerName);
            if (index < 0)
            {
                return false;
            }

            this.tableModel.Selections.SelectCell(index, 0);
            return true;
        }

        /// <summary>Номер строки таблицы, показывающей слой с этим именем; −1 — нет такой.</summary>
        int RowOfLayer(string layerName)
        {
            for (int i = 0; i < this.tableModel.Rows.Count; i++)
            {
                FsaReportRow model = this.tableModel.Rows[i].Tag as FsaReportRow;
                if (model != null && model.Layer != null
                    && string.Equals(model.Layer.Name, layerName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        void ReportTable_SelectionChanged(object sender, XPTable.Events.SelectionEventArgs e)
        {
            if (this.suspendSelection)
            {
                return;
            }

            this.RememberSelectedRow(this.ModelOfSelection());
            this.PushHighlight();
        }

        /// <summary>(`AMBER327`) Выбора нет: ни слоя, ни части.</summary>
        void ForgetSelectedRow()
        {
            this.RememberSelectedRow(null);
        }

        /// <summary>
        /// (`AMBER327`) Запомнить выбранную строку: слой и — у строк «— сумм-пик», «— пики вылета»,
        /// «— обратное рассеяние» и их потомков — часть ленты и окно потомка. Строка без ленты
        /// (необнаруженный, невязка, качество) — как «выбора нет», правило `A246`.
        /// </summary>
        void RememberSelectedRow(FsaReportRow model)
        {
            bool layered = model != null && model.Layer != null;
            this.selectedLayer = layered ? model.Layer.Name : null;
            this.selectedPart = layered ? model.Part : FsaLayerPart.None;
            this.selectedFirst = this.selectedPart != FsaLayerPart.None ? model.FirstChannel : -1;
            this.selectedLast = this.selectedPart != FsaLayerPart.None ? model.LastChannel : -1;
            this.selectedKind = layered ? model.Kind : FsaReportRowKind.Status;
            this.selectedName = layered ? model.Name : null;
            if (this.selectedFirst < 0 || this.selectedLast < this.selectedFirst)
            {
                this.selectedFirst = -1;
                this.selectedLast = -1;
            }
        }

        /// <summary>
        /// (П290) Щелчок по УЖЕ выбранной строке. XPTable выбор при этом не меняет и
        /// события выбора не поднимает, а щелчок человека обязан вернуть графику
        /// подсветку этого окна, если её с тех пор сменило окно результата: главнее
        /// последний щелчок. Щелчок по невыбранной строке сюда тоже приходит — раньше
        /// смены выбора, — и его обслужит <see cref="ReportTable_SelectionChanged"/>.
        /// </summary>
        void ReportTable_CellMouseDown(object sender, XPTable.Events.CellMouseEventArgs e)
        {
            // (`AMBER320`) Щелчок левой кнопкой по стрелке родителя — левее его текста (стрелка
            // и образец) — раскрывает или сворачивает группу. Выбор строки XPTable делает сам,
            // как при любом щелчке: строка родителя выбирается, подсветка — её слоя.
            if (e.Button == MouseButtons.Left && e.Column == 0
                && e.Row >= 0 && e.Row < this.tableModel.Rows.Count)
            {
                Row row = this.tableModel.Rows[e.Row];
                if (GroupOf(row) != null && e.X - e.CellRect.X <= row.Cells[0].Padding.Left + 1)
                {
                    this.ToggleGroup(row);
                }
            }

            if (this.suspendSelection || e.Row < 0 || e.Row >= this.tableModel.Rows.Count
                || !this.tableModel.Rows[e.Row].AnyCellsSelected)
            {
                return;
            }

            this.PushHighlight();
        }

        /// <summary>
        /// Строка модели выбранной строки; null — выбора нет. Строка без
        /// ленты (необнаруженный кандидат, невязка, качество, состояние)
        /// выбирается, и приглушать по ней НЕЧЕГО: на графике её нет.
        /// (`AMBER327`) Слой и часть ленты из неё берёт <see cref="RememberSelectedRow"/>.
        /// </summary>
        FsaReportRow ModelOfSelection()
        {
            int[] indices = this.tableModel.Selections.SelectedIndicies;
            if (indices == null || indices.Length == 0)
            {
                return null;
            }

            int index = indices[0];
            if (index < 0 || index >= this.tableModel.Rows.Count)
            {
                return null;
            }

            return this.tableModel.Rows[index].Tag as FsaReportRow;
        }

        /// <summary>
        /// Сказать графику, что выделено. ⛔ Ничего, кроме краски, это не
        /// меняет: ни расчёта, ни представления, ни группировки — поэтому
        /// заказа пересчёта здесь нет и не будет.
        ///
        /// (П287) Строка отчёта — один слой стека, и горит ровно его лента: решение
        /// Amber 14.10.2026 «В отчёте — только лента корня» — строка «Th-232 — ряд»
        /// зажигает ленту самого Th-232, остальной ряд гаснет. Весь ряд зажигает
        /// окно результата (<see cref="DCResultView"/>), не отчёт.
        /// </summary>
        void PushHighlight()
        {
            if (this.document != null && !this.document.IsDisposed
                && this.document.EnergySpectrumView != null)
            {
                EnergySpectrumView view = this.document.EnergySpectrumView;

                // (`AMBER327`) Строка части ленты («— сумм-пик» и прочие под стрелкой, их потомки)
                // выделяет свою часть; прочие строки — слой целиком, как прежде.
                if (this.selectedPart != FsaLayerPart.None)
                {
                    view.SetFsaHighlightPart(this.selectedLayer, this.selectedPart, this.selectedFirst, this.selectedLast);
                }
                else
                {
                    view.SetFsaHighlight(this.selectedLayer, false);
                }

                this.pushedView = new WeakReference<EnergySpectrumView>(view);
                this.pushedLayer = this.selectedLayer;
                this.pushedPart = this.selectedLayer != null ? this.selectedPart : FsaLayerPart.None;
                this.pushedFirst = this.pushedPart != FsaLayerPart.None ? this.selectedFirst : -1;
                this.pushedLast = this.pushedPart != FsaLayerPart.None ? this.selectedLast : -1;
            }
        }

        /// <summary>
        /// ⛔ (П290, 14.10.2026) ГЛАВНЕЕ ПОСЛЕДНИЙ ЩЕЛЧОК ЧЕЛОВЕКА. Подсветку графика
        /// пишут два окна — это и окно результата (<see cref="DCResultView"/>, с
        /// 06.10.2026). Перестройка таблицы (конец каждого счёта — при наборе раз в
        /// 30 с, смена группировки, настройки, спектра) — не действие человека, и
        /// прежде она заново слала графику выбор отчёта, чаще всего null: щелчок в окне
        /// результата жил до первого пересчёта, а после П287 строка ряда «Th-232»
        /// отчёта ещё и меняла «весь ряд» на «только корень» (находка П287, замер
        /// `HighlightLastClickProbeP290`). Теперь перестройка возвращает подсветку,
        /// только если последней её ставило само это окно; щелчок человека в любом
        /// окне ставит своё, как прежде. Правило — умолчание распорядителя, не решение
        /// Amber (журнал `handover-2026-10-14-p290-highlight-last-click.md`).
        /// </summary>
        void PushHighlightIfOwn()
        {
            if (this.document != null && !this.document.IsDisposed
                && this.document.EnergySpectrumView != null
                && this.HighlightIsOwn(this.document.EnergySpectrumView))
            {
                this.PushHighlight();
            }
        }

        /// <summary>
        /// (П290) На графике всё ещё то, что последним поставило это окно? Окно
        /// результата пишет либо слой С признаком ряда, либо null — поэтому любой его
        /// щелчок после щелчка отчёта виден здесь как расхождение. Совпасть с записью
        /// отчёта может только его null при null отчёта — тогда и возвращать нечего.
        /// Вид другого документа — не свой: этому окну он ничего не говорил.
        /// </summary>
        bool HighlightIsOwn(EnergySpectrumView view)
        {
            EnergySpectrumView pushed;
            return this.pushedView != null && this.pushedView.TryGetTarget(out pushed)
                   && ReferenceEquals(view, pushed)
                   && string.Equals(view.FsaHighlight, this.pushedLayer, StringComparison.Ordinal)
                   && !view.FsaHighlightWholeChain
                   && view.FsaHighlightPart == this.pushedPart
                   && view.FsaHighlightFirstChannel == this.pushedFirst
                   && view.FsaHighlightLastChannel == this.pushedLast;
        }

        /// <summary>
        /// Вернуть выбор на ту же строку состава после перестройки таблицы.
        /// Слоя может уже не быть (сменился спектр, группировка, состав) —
        /// тогда выбор снимается, и приглушение снимается вместе с ним.
        /// (П290) Графику выбор уходит, только если подсветка на нём — этого окна
        /// (<see cref="PushHighlightIfOwn"/>).
        /// </summary>
        void RestoreSelection(string layerName)
        {
            this.suspendSelection = true;
            try
            {
                this.tableModel.Selections.Clear();
                int index = layerName != null ? this.RowOfSelected(layerName) : -1;
                if (index >= 0)
                {
                    this.tableModel.Selections.SelectCell(index, 0);
                    this.RememberSelectedRow(this.tableModel.Rows[index].Tag as FsaReportRow);
                }
                else
                {
                    this.ForgetSelectedRow();
                }
            }
            finally
            {
                this.suspendSelection = false;
            }

            this.PushHighlightIfOwn();
        }

        /// <summary>
        /// (`AMBER327`) Строка, на которую вернуть выбор после перестройки: у строки части ленты —
        /// та же строка (род, слой, подпись); пропала (пик стал незаметен) — её строка группы того же
        /// слоя; скрыта свёрнутой группой — родитель, на скрытой строке выбора не бывает
        /// (`AMBER320`). Иначе и у строки слоя — строка слоя, как прежде (<see cref="RowOfLayer"/>).
        /// </summary>
        int RowOfSelected(string layerName)
        {
            if (this.selectedPart != FsaLayerPart.None)
            {
                int same = -1, group = -1;
                FsaReportRowKind groupKind = GroupKindOf(this.selectedPart);
                for (int i = 0; i < this.tableModel.Rows.Count; i++)
                {
                    FsaReportRow model = this.tableModel.Rows[i].Tag as FsaReportRow;
                    if (model == null || model.Layer == null
                        || !string.Equals(model.Layer.Name, layerName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (same < 0 && model.Kind == this.selectedKind
                        && string.Equals(model.Name, this.selectedName, StringComparison.Ordinal))
                    {
                        same = i;
                    }

                    if (group < 0 && model.Kind == groupKind)
                    {
                        group = i;
                    }
                }

                int index = same >= 0 ? same : group;
                if (index >= 0)
                {
                    Row row = this.tableModel.Rows[index];
                    return row.Parent != null && !row.Parent.ExpandSubRows ? row.Parent.Index : index;
                }
            }

            return this.RowOfLayer(layerName);
        }

        /// <summary>(`AMBER327`) Род строки-группы части ленты: «— сумм-пик», «— пики вылета», «— обратное рассеяние».</summary>
        static FsaReportRowKind GroupKindOf(FsaLayerPart part)
        {
            switch (part)
            {
                case FsaLayerPart.EscapePeaks:
                    return FsaReportRowKind.EscapePeaks;
                case FsaLayerPart.Backscatter:
                    return FsaReportRowKind.Backscatter;
                default:
                    return FsaReportRowKind.SumPeaks;
            }
        }

        /// <summary>
        /// Строка XPTable из строки модели: образец, имя, значение. Смысл
        /// читается из <see cref="FsaReportRow.Kind"/> и признаков, никогда —
        /// из текста. Цвет текста никогда не остаётся единственным носителем
        /// смысла: у серых строк есть «&lt; … %», у красных — свой текст.
        /// </summary>
        /// <param name="caption">
        /// (`A247`) Подпись ВМЕСТО <see cref="FsaReportRow.Name"/>; null —
        /// подпись модели. Подменяются ровно две строки блока качества —
        /// невязка и χ²/ndf, — и подменяется у них ТОЛЬКО ПОДПИСЬ: значение
        /// берётся у модели дословно, второго форматирования числа нет.
        /// </param>
        /// <param name="hint">
        /// (`A300`) Подсказка ВМЕСТО <see cref="FsaReportRow.Hint"/>; null —
        /// подсказка модели. Нужна строке невязки: подрезка показа — свойство
        /// ОКНА, модель о ней не знает и знать не должна.
        /// </param>
        /// <param name="expander">
        /// (`AMBER320`) Строка — родитель группы: перед текстом место под стрелку свёртки
        /// (<see cref="ArrowWidth"/>), образец остаётся на прежнем месте.
        /// </param>
        /// <param name="underExpander">
        /// (`AMBER320`) Строка — потомок родителя со стрелкой: отступ сдвинут на ту же стрелку,
        /// чтобы текст потомка стоял на прежний шаг (<see cref="IndentStep"/>) правее текста родителя.
        /// </param>
        Row MakeRow(FsaReportRow row, string caption = null, string hint = null,
                    bool expander = false, bool underExpander = false)
        {
            Color fore = row.Kind == FsaReportRowKind.Status && row.Warning
                ? Color.DarkOrange
                : row.Warning ? Color.Firebrick : row.Muted ? Color.Gray : Color.Black;

            // (`AMBER217`) Образец — картинкой ПЕРЕД подписью, в той же ячейке:
            // отступ слева освобождает ему место, текст и его перенос остаются
            // штатными (<see cref="SwatchTextCellRenderer"/>).
            // (`AMBER274`, Ф14) подпись — с пометкой строки по сплошному спектру
            var name = new Cell(caption ?? RowCaption(row), this.SwatchOf(row));
            // (`AMBER320`, второй заход) Строка рода «родитель» без потомков (у «— сумм-пик» нет
            // заметных пиков) держит место стрелки пустым: текст встаёт вровень с соседями со стрелкой.
            int arrow = expander || underExpander || ReservesArrow(row) ? ArrowWidth : 0;
            if (name.Image != null)
            {
                name.Padding = new CellPadding(SwatchWidth + SwatchGap + arrow, 0, 0, 0);
            }
            else if (row.Indent > 0)
            {
                // (`AMBER286`) строка под строкой нуклида: текст — под его текстом и на шаг правее
                name.Padding = new CellPadding(SwatchWidth + SwatchGap + IndentStep * row.Indent + arrow, 0, 0, 0);
            }
            else if (arrow > 0)
            {
                // (`AMBER320`) родитель без образца (заголовок собственной активности кристалла)
                name.Padding = new CellPadding(arrow, 0, 0, 0);
            }

            var value = new Cell(row.Value ?? string.Empty);
            name.ForeColor = fore;
            value.ForeColor = fore;
            // (`AMBER6`) Подсказка строки: своя, если она есть, иначе сам
            // текст — как было до 08.09.2026. (`A300`) Подсказка ОКНА, если
            // она задана, бьёт обе: модель о подрезке показа не знает.
            name.ToolTipText = !string.IsNullOrEmpty(hint) ? hint
                : string.IsNullOrEmpty(row.Hint) ? name.Text : row.Hint;
            value.ToolTipText = row.Value;

            // Полный текст без усечения: строки блока качества и строка
            // состояния (причина отказа бывает длинной) переносятся по ширине
            // колонки.
            if (row.Kind == FsaReportRowKind.Quality || row.Kind == FsaReportRowKind.Status
                || row.Kind == FsaReportRowKind.Residual || row.Kind == FsaReportRowKind.NoBackground)
            {
                name.WordWrap = true;
            }

            var tableRow = new Row(new[] { name, value });
            tableRow.Tag = row;
            return tableRow;
        }

        /// <summary>
        /// Образец строки — тем же правилом, что лента на графике: сплошной
        /// цвет слоя или штрих сумм-пиков (<see cref="FsaPalette.SumPeakHatchColor"/>).
        /// Пусто — образца нет (у невязки его нет с `AMBER217`).
        /// </summary>
        Image SwatchOf(FsaReportRow row)
        {
            if (row.Swatch == FsaSwatchKind.None)
            {
                return null;
            }

            string key = row.Swatch + "|" + row.Color.ToArgb().ToString(CultureInfo.InvariantCulture);
            Image image;
            if (this.swatches.TryGetValue(key, out image))
            {
                return image;
            }

            var bitmap = new Bitmap(SwatchWidth, SwatchHeight);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                Rectangle r = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                switch (row.Swatch)
                {
                    case FsaSwatchKind.Solid:
                        using (Brush brush = new SolidBrush(row.Color))
                        {
                            g.FillRectangle(brush, r);
                        }

                        break;

                    case FsaSwatchKind.SumPeakHatch:
                        using (Brush brush = new HatchBrush(HatchStyle.DarkUpwardDiagonal,
                                                            FsaPalette.SumPeakHatchColor(row.Color), row.Color))
                        {
                            g.FillRectangle(brush, r);
                        }

                        break;

                    case FsaSwatchKind.BackscatterHatch:  // (`AMBER311`) тот же штрих, что на графике
                        using (Brush brush = new HatchBrush(HatchStyle.DarkVertical,
                                                            FsaPalette.SumPeakHatchColor(row.Color), row.Color))
                        {
                            g.FillRectangle(brush, r);
                        }

                        break;

                    case FsaSwatchKind.EscapeHatch:      // (`AMBER311`) тот же штрих, что на графике
                        using (Brush brush = new HatchBrush(HatchStyle.DarkDownwardDiagonal,
                                                            FsaPalette.SumPeakHatchColor(row.Color), row.Color))
                        {
                            g.FillRectangle(brush, r);
                        }

                        break;
                }

                g.DrawRectangle(Pens.DimGray, 0, 0, bitmap.Width - 1, bitmap.Height - 1);
            }

            this.swatches[key] = bitmap;
            return bitmap;
        }

        /// <summary>
        /// (`AMBER217`) Подпись с образцом перед ней. Отдельной колонки образцов
        /// больше нет (решение Amber 07.10.2026), а штатный <see cref="ImageCellRenderer"/>
        /// не переносит текст, без чего блок качества уезжает (`AMBER214`). Поэтому
        /// текст рисует и меряет обычный <see cref="TextCellRenderer"/> — ячейка с
        /// образцом несёт отступ слева, — а образец встаёт в этот отступ на уровне
        /// ПЕРВОЙ строки текста.
        /// </summary>
        sealed class SwatchTextCellRenderer : TextCellRenderer
        {
            protected override void OnPaint(PaintCellEventArgs e)
            {
                base.OnPaint(e);
                Image image = e.Cell != null ? e.Cell.Image : null;

                // (`AMBER320`) Родитель группы: стрелка между образцом и текстом — «▶» свёрнуто,
                // «▼» раскрыто; образец сдвигается влево на её место и стоит, где стоял.
                bool expander = e.Cell != null && GroupOf(e.Cell.Row) != null;
                Rectangle client = this.ClientRectangle;
                int line = this.Font != null ? this.Font.Height : SwatchHeight;
                int top = client.Y;
                int lineHeight = Math.Min(line, client.Height);
                if (expander)
                {
                    int ax = client.X - ArrowWidth;
                    int ay = top + Math.Max(0, (lineHeight - ArrowGlyph) / 2);
                    Point[] glyph = e.Cell.Row.ExpandSubRows
                        ? new[] { new Point(ax, ay + 1), new Point(ax + ArrowGlyph, ay + 1),
                                  new Point(ax + ArrowGlyph / 2, ay + ArrowGlyph - 1) }
                        : new[] { new Point(ax + 1, ay), new Point(ax + ArrowGlyph - 1, ay + ArrowGlyph / 2),
                                  new Point(ax + 1, ay + ArrowGlyph) };
                    System.Drawing.Drawing2D.SmoothingMode mode = e.Graphics.SmoothingMode;
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.FillPolygon(Brushes.DimGray, glyph);
                    e.Graphics.SmoothingMode = mode;
                }

                if (image == null)
                {
                    return;
                }

                bool arrowSpace = expander || ReservesArrow(e.Cell.Row.Tag as FsaReportRow);
                int x = client.X - SwatchGap - image.Width - (arrowSpace ? ArrowWidth : 0);
                int y = top + Math.Max(0, (Math.Min(line, client.Height) - image.Height) / 2);
                e.Graphics.DrawImage(image, x, y, image.Width, image.Height);
            }
        }

        void ReportTable_Resize(object sender, EventArgs e)
        {
            this.FitColumns();
        }

        /// <summary>
        /// Колонка «Компонент» получает всю ширину, оставшуюся от колонки
        /// значения (`AMBER217`: отдельной колонки образцов нет — образец стоит
        /// картинкой перед подписью в этой же колонке): у XPTable нет колонки-заполнителя, а без этого длинный
        /// текст обрезался бы по фиксированной ширине.
        /// </summary>
        void FitColumns()
        {
            int spare = this.reportTable.ClientSize.Width - ValueColumnWidth
                        - SystemInformation.VerticalScrollBarWidth - 4;
            this.componentColumn.Width = Math.Max(80, spare);

            // ⛔ (`AMBER214`, П240 07.10.2026; Amber со снимком, дословно: «Тут
            // требуется скроллбар. Текст уехал вниз.») Высоты строк с переносом
            // XPTable узнаёт только при ОТРИСОВКЕ, а нужна ли вертикальная полоса,
            // решает раньше — в `EndUpdate` и при первой отрисовке — по высотам
            // строк в одну строку. Длинные строки блока качества (разбор без
            // матрицы, сумм-пики) переносятся в пять-шесть строк, сумма выходила
            // больше окна, а полосы не было: низ таблицы уезжал за край. Ширина
            // колонки к тому же меняется здесь, ПОСЛЕ `EndUpdate`. Поэтому
            // высоты считаются заново при каждой подгонке ширины и полоса
            // переоценивается по ним.
            // ⚠ Только при живой ручке: `CalculateAllRowHeights` зовёт
            // `CreateGraphics`, а он создал бы ручку окну пробы, у которой её
            // нет нарочно (`SessionCompleted` судит по `IsHandleCreated`).
            if (this.reportTable.IsHandleCreated && this.reportTable.TableModel != null)
            {
                this.reportTable.CalculateAllRowHeights();
                this.reportTable.UpdateScrollBars();
            }
        }

        /// <summary>
        /// Доступность по таблице документа («Доступность элементов
        /// управления»): нет спектра — всё выключено; источник «по пикам» —
        /// равновесие и родители недоступны; NucBase без равновесия —
        /// родители недоступны; иначе всё доступно, родители — если в
        /// результате есть связанный ряд. Недоступный элемент ХРАНИТ выбор и
        /// объясняет себя подсказкой.
        /// </summary>
        void UpdateAvailability()
        {
            bool has = this.session != null && this.ActiveResultData != null;
            FsaResult result = this.presentation != null ? this.presentation.Source : null;

            this.sourcePeaksRadio.Enabled = has;
            this.sourceNucBaseRadio.Enabled = has;
            this.daughtersRadio.Enabled = has;
            this.atomicXrayCheckBox.Enabled = has;
            this.cascadeSummingCheckBox.Enabled = has;
            this.backscatterCheckBox.Enabled = has;
            this.escapeCheckBox.Enabled = has;
            this.pileUpCheckBox.Enabled = has;
            this.fitLowerBoundUpDown.Enabled = has;
            this.residualBandCheckBox.Enabled = has;
            this.reportTable.Enabled = has;

            bool nucBase = has && this.sourceNucBaseRadio.Checked;
            this.equilibriumCheckBox.Enabled = nucBase;
            this.SetTip(this.equilibriumCheckBox,
                                    nucBase || !has ? Resources.FSAReportTipCalculation
                                                    : Resources.FSAReportTipEquilibriumNeedsNucBase);

            bool parentsPossible = nucBase && this.equilibriumCheckBox.Checked;
            bool parentsAllowed = parentsPossible && this.presentation != null && this.presentation.ParentGroupingAllowed;
            this.parentsRadio.Enabled = parentsAllowed;
            string parentsTip = !has || parentsAllowed
                ? Resources.FSAReportTipGrouping
                : !parentsPossible
                    ? Resources.FSAReportTipParentsNeedNucBase
                    : this.presentation != null
                      && this.presentation.ParentGroupingRefusalReason != FsaParentGroupingRefusal.None
                        ? string.Format(CultureInfo.CurrentCulture, Resources.FSAReportTipParentsRefused,
                                        RefusalText(this.presentation.ParentGroupingRefusalReason))
                        : Resources.FSAReportTipGrouping;
            this.SetTip(this.parentsRadio, parentsTip);

            // Матрица уже несёт рассеяние и вылеты: переключатель управляет
            // только ОТДЕЛЬНОЙ дополнительной компонентой, и подсказка обязана
            // это сказать (флажок не вправе обещать отключение физики матрицы).
            bool matrix = result != null && result.ResponseMatrixUsed;
            string extra = matrix
                ? Resources.FSAReportTipCalculation + " " + Resources.FSAReportTipMatrixExtra
                : Resources.FSAReportTipCalculation;
            // (`AMBER311`) При матрице галка управляет показом обратного рассеяния: «Да, тем же правилом (Рекомендую)».
            this.SetTip(this.backscatterCheckBox, matrix
                ? Resources.ResourceManager.GetString("FSAReportTipBackscatterMatrix", Resources.Culture) ?? extra
                : extra);
            // (`AMBER311`) Решение Amber 06.11.2026, дословно: «Если там включена галка, то нужно
            // отображать, рассчитывать эти особенности спектра в FSA. Если галки нет - то
            // отрисовывать только нуклиды.»; при матрице и снятой галке вопросником: «Снять показ,
            // расчёт тот же (Рекомендую)». Подсказка обязана сказать, что при матрице галка
            // управляет показом вылетов.
            this.SetTip(this.escapeCheckBox, matrix
                ? Resources.ResourceManager.GetString("FSAReportTipEscapeMatrix", Resources.Culture) ?? extra
                : extra);

            // (`AMBER45`) Комбо «Matrix layer» живо только при живой матрице:
            // без раскладки по каналам у слоёв (матрицы нет, старый формат,
            // образы не по матрице) режим слоя недостижим, и комбо гаснет с
            // подсказкой-причиной; просьба человека при этом ПОМНИТСЯ, а
            // показывается «All» — то, что график и рисует. Судится тем же
            // правилом, что у графика (`FsaMatrixLayers.HasChannels`), — иначе
            // окно могло бы обещать слой, которого стопка не покажет.
            bool matrixLayers = has && this.MatrixLayerAvailable;
            this.matrixLayerComboBox.Enabled = matrixLayers;
            this.SetTip(this.matrixLayerComboBox,
                                    !has || matrixLayers ? OwnText(KeyMatrixLayerTip) : OwnText(KeyMatrixLayerNoMatrixTip));
            this.SetTip(this.matrixLayerLabel,
                                    !has || matrixLayers ? OwnText(KeyMatrixLayerTip) : OwnText(KeyMatrixLayerNoMatrixTip));

            this.loading = true;
            try
            {
                bool parents = parentsAllowed && this.requestedGrouping == FsaGrouping.Parents;
                this.parentsRadio.Checked = parents;
                this.daughtersRadio.Checked = !parents;
                this.matrixLayerComboBox.SelectedIndex = matrixLayers ? MatrixLayerIndex(this.requestedMatrixLayer) : 0;
            }
            finally
            {
                this.loading = false;
            }
        }

        /// <summary>
        /// (`A184`) ЧИТАТЕЛЬ КОДА ПРИЧИНЫ: превращает
        /// <see cref="FsaParentGroupingRefusal"/> в подпись НА ЯЗЫКЕ
        /// ИНТЕРФЕЙСА. Единственное место, где у причины появляется текст для
        /// человека; модель отдаёт только код (её служебная строка
        /// <c>FsaResult.ParentGroupingRefusal</c> всегда русская и предназначена
        /// журналу и пробам).
        ///
        /// <see cref="FsaParentGroupingRefusal.None"/> сюда не приходит — вызов
        /// стоит под проверкой «причина есть», — но пустая строка на нём лучше
        /// отказа: подсказка не то место, где стоит падать.
        /// </summary>
        static string RefusalText(FsaParentGroupingRefusal reason)
        {
            switch (reason)
            {
                case FsaParentGroupingRefusal.FreeChainMembers:
                    return Resources.FSAReportRefusalFreeChainMembers;

                case FsaParentGroupingRefusal.NoDecayChain:
                    return Resources.FSAReportRefusalNoDecayChain;

                default:
                    return "";
            }
        }

        /// <summary>
        /// (`AMBER264`) Подсказка переключателя окна — блоком по <see cref="AppUi.TipWidth"/>
        /// знаков (правило Amber 23.10.2026: «все подсказки в popup делать вот такими блоками»).
        /// Подсказки строк таблицы переносит сама таблица (XPTable, <c>CellRenderer</c>).
        /// </summary>
        void SetTip(Control control, string text)
        {
            this.toolTip.SetToolTip(control, AppUi.WrapTip(text));
        }

        /// <summary>Подсказки, не зависящие от состояния: меняет расчёт или только показ.</summary>
        void SetToolTips()
        {
            this.SetTip(this.sourcePeaksRadio, Resources.FSAReportTipCalculation);
            this.SetTip(this.sourceNucBaseRadio, Resources.FSAReportTipCalculation);
            this.SetTip(this.daughtersRadio, Resources.FSAReportTipGrouping);
            this.SetTip(this.parentsRadio, Resources.FSAReportTipGrouping);
            this.SetTip(this.equilibriumCheckBox, Resources.FSAReportTipCalculation);
            this.SetTip(this.atomicXrayCheckBox, Resources.FSAReportTipCalculation);
            this.SetTip(this.cascadeSummingCheckBox, Resources.FSAReportTipCalculation);
            this.SetTip(this.backscatterCheckBox, Resources.FSAReportTipCalculation);
            this.SetTip(this.escapeCheckBox, Resources.FSAReportTipCalculation);
            this.SetTip(this.pileUpCheckBox, Resources.FSAReportTipCalculation);
            this.SetTip(this.fitLowerBoundUpDown, Resources.FSAReportTipCalculation);
            this.SetTip(this.fitLowerBoundLabel, Resources.FSAReportTipCalculation);

            // (`A248`) Лента невязки расчёта НЕ меняет, и подсказка обязана
            // сказать это прямо: подпись, по решению Amber, ничего не
            // поясняет, а окно делит подсказки ровно на два рода — «меняет
            // расчёт» и «меняет только показ». ⚠ Подсказка тут ВТОРОЙ
            // признак, а не единственный: с `A265` род переключателя виден
            // и без наведения — по группе, в которой он лежит.
            this.SetTip(this.residualBandCheckBox, OwnText(KeyResidualBandTip));
            this.SetTip(this.spectralDataCheckBox, OwnText(KeySpectralDataTip));

            // (`AMBER45`) Комбо слоя матрицы — того же рода «меняет только
            // показ»; при мёртвой матрице подсказку подменяет причиной
            // <see cref="UpdateAvailability"/>.
            this.SetTip(this.matrixLayerComboBox, OwnText(KeyMatrixLayerTip));
            this.SetTip(this.matrixLayerLabel, OwnText(KeyMatrixLayerTip));
        }

        /// <summary>Подсказка элемента сейчас (пробы).</summary>
        public string ToolTipOf(Control control)
        {
            return this.toolTip.GetToolTip(control);
        }

        /// <summary>
        /// Закрытие окна — скрытие (`HideOnClose`): сеанс документа живёт
        /// дальше, режим графика не меняется, подписка остаётся, чтобы при
        /// повторном показе строки были свежими без второго расчёта.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // (`A246`) Окна не станет — приглушение снимается: график живёт
                // дальше и остался бы поблекшим навсегда. (П290) Только СВОЁ:
                // подсветку, поставленную окном результата, снимет оно само.
                this.ForgetSelectedRow();
                this.PushHighlightIfOwn();

                // (`A248`) Тот же довод: окна не станет, а с ним и галочки, —
                // спрятанная лента невязки осталась бы спрятанной навсегда, и
                // вернуть её было бы нечем.
                this.showResidualBand = true;
                this.PushResidualBand();

                // (`AMBER45`) И тот же довод для слоя матрицы: без окна вернуть
                // графику все каналы было бы нечем — он остался бы стопкой
                // одного канала навсегда.
                this.requestedMatrixLayer = FsaMatrixLayer.All;
                this.PushMatrixLayer();

                // (`A295`) Подписку на такт вида снимаем здесь же: иначе
                // документ держал бы ссылку на закрытое окно и звал бы
                // его на каждом обновлении.
                if (this.document != null)
                {
                    this.document.ViewRefreshed -= this.DocumentViewRefreshed;
                }

                if (this.session != null)
                {
                    this.session.Completed -= this.SessionCompleted; this.session.Started -= this.SessionStarted;
                    this.session = null;
                }

                if (this.headerFont != null)
                {
                    this.headerFont.Dispose();
                    this.headerFont = null;
                }

                foreach (Image image in this.swatches.Values)
                {
                    image.Dispose();
                }

                this.swatches.Clear();
                if (this.components != null)
                {
                    this.components.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
