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
using XPTable.Models;

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
    /// XPTable на весь остаток: перечень слоёв с образцом и долей, строки
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
    ///   * расчётные (источник, равновесие, пять компонентов) пишутся в
    ///     активную копию конфигурации спектра и в умолчание прибора
    ///     (<see cref="SaveOptionsToDevice"/>), входят в отпечаток сеанса и
    ///     ведут к ОДНОМУ актуальному пересчёту (критерий 7); соседние
    ///     открытые документы не переписываются (критерий 11);
    ///   * группировка родители/дочерние — настройка ПРЕДСТАВЛЕНИЯ этого окна
    ///     на время работы приложения: в файл, конфигурацию и отпечаток не
    ///     входит, расчёт не запускает; одно значение на документ
    ///     (<see cref="DocEnergySpectrum.FsaGrouping"/>) читают и график, и
    ///     таблица.
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

        /// <summary>Образцы колонки-образца, по одному на (род, цвет).</summary>
        readonly Dictionary<string, Image> swatches = new Dictionary<string, Image>(StringComparer.Ordinal);

        /// <summary>
        /// Пробы: окно никогда не показывается, а потребителем быть обязано —
        /// иначе критерии «окно заказывает расчёт» не измерить без экрана.
        /// В приложении всегда false: потребитель определяется видимостью.
        /// </summary>
        public bool ProbeConsumer { get; set; }

        /// <summary>Ширина колонки-образца, пикселей.</summary>
        const int SwatchColumnWidth = 24;

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
        /// Перестройка таблицы сама двигает выбор (строки чистятся и создаются
        /// заново), и XPTable честно поднимает на это событие. Пока флаг
        /// поднят, событие не читается: иначе каждый фоновый пересчёт снимал бы
        /// выбор человека, а по дороге ещё и слал бы графику null.
        /// </summary>
        bool suspendSelection;

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
        const string KeyResidualRow = "FSAReport_ResidualRow";
        const string KeyChi2Row = "FSAReport_Chi2Row";
        const string KeyMatrixRow = "FSAReport_MatrixRow";
        const string KeyMatrixUsed = "FSAReport_MatrixUsed";
        const string KeyMatrixNotUsed = "FSAReport_MatrixNotUsed";
        const string KeyMatrixOldFormat = "FSAReport_MatrixOldFormat";
        const string KeyEfficiencyRow = "FSAReport_EfficiencyRow";
        const string KeyEfficiencyUsed = "FSAReport_EfficiencyUsed";
        const string KeyEfficiencyNotUsed = "FSAReport_EfficiencyNotUsed";
        const string KeySummingRow = "FSAReport_SummingRow";
        const string KeySummingUsed = "FSAReport_SummingUsed";
        const string KeySummingNotUsed = "FSAReport_SummingNotUsed";
        const string KeyDriftRow = "FSAReport_DriftRow";
        const string KeyDriftEdge = "FSAReport_DriftEdge";
        const string KeySuppressedRow = "FSAReport_SuppressedRow";
        const string KeyBackgroundRejectedRow = "FSAReport_BackgroundRejectedRow";

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
            this.swatchColumn.Alignment = ColumnAlignment.Center;
            this.reportTable.HeaderFont = this.Font;
            this.reportTable.NoItemsText = string.Empty;
            this.reportTable.GridLines = GridLines.None;
            this.reportTable.SelectionStyle = SelectionStyle.Grid;
            this.reportTable.EnableWordWrap = true;
            this.reportTable.EnableToolTips = true;

            this.headerFont = new Font(this.Font, FontStyle.Bold);

            this.SetToolTips();
            this.reportTable.Resize += this.ReportTable_Resize;
            this.reportTable.SelectionChanged += this.ReportTable_SelectionChanged;
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
            // будет некому.
            if (!ReferenceEquals(this.document, doc))
            {
                this.selectedLayer = null;
                this.PushHighlight();
            }

            FsaAnalysisSession next = doc != null ? doc.FsaSession : null;
            if (!ReferenceEquals(this.session, next))
            {
                if (this.session != null)
                {
                    this.session.Completed -= this.SessionCompleted;
                }

                this.session = next;
                if (this.session != null)
                {
                    this.session.Completed += this.SessionCompleted;
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
            this.selectedLayer = null;
            this.PushHighlight();
            if (!ReferenceEquals(this.session, probeSession))
            {
                if (this.session != null)
                {
                    this.session.Completed -= this.SessionCompleted;
                }

                this.session = probeSession;
                if (this.session != null)
                {
                    this.session.Completed += this.SessionCompleted;
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
        /// перерисовка графика. Никакой записи в конфигурацию, никакого
        /// <c>session.Invalidate()</c>, никакого <c>Consume()</c> — иначе снятая
        /// лента стоила бы человеку полного пересчёта разбора и, что хуже,
        /// поменяла бы отпечаток: те же числа считались бы заново.
        /// </summary>
        void residualBandCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (this.loading)
            {
                return;
            }

            this.showResidualBand = this.residualBandCheckBox.Checked;
            this.PushResidualBand();
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

        /// <summary>
        /// Расчётный переключатель. Путь ОДИН на все семь — «в копию спектра,
        /// в умолчание прибора, обесценить кэш, заказать счёт потребителям» —
        /// написанный семь раз он однажды разошёлся бы.
        ///
        /// ⛔ Пишется В ДВА МЕСТА (решение Amber 18.08.2026, `S70`). В копию
        /// СПЕКТРА — иначе нажатие не влияет на то, что человек сейчас видит.
        /// В умолчание ПРИБОРА и на диск — иначе положение не переживает ни
        /// следующий спектр, ни перезапуск. Соседние документы при этом не
        /// трогаются (см. <see cref="SaveOptionsToDevice"/>).
        ///
        /// Поиска пиков переключатели НЕ касаются — ни одного пика от них не
        /// появится и не исчезнет, — поэтому детекция не перезапускается;
        /// инвалидируется только разложение. Пересчёт идёт немедленно, только
        /// если есть потребитель: это окно (видимое) или график в `ShowFSA`.
        /// Оба заказывают ОДИН отпечаток, и сеанс считает его один раз.
        /// </summary>
        void ApplyCalculationChange(Action<FWHMPeakDetectionMethodConfig> set)
        {
            if (this.loading)
            {
                return;
            }

            ResultData rd = this.ActiveResultData;
            FWHMPeakDetectionMethodConfig config = rd != null
                ? rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig
                : null;
            if (config == null)
            {
                return;
            }

            set(config);
            SaveOptionsToDevice(rd, config);

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

        /// <summary>
        /// Семь настроек разложения — в умолчание прибора и на диск. Прибор
        /// берётся из менеджера по Guid: именно ту запись читает
        /// <see cref="FWHMPeakDetectionMethodConfig.AdoptFrom"/> при открытии
        /// следующего спектра, и правка её копии никуда бы не дошла.
        ///
        /// ⛔ Прибор сохраняется ТИХО
        /// (<see cref="DeviceConfigManager.SaveConfigQuiet"/>): обычное
        /// сохранение рассылает событие, а по нему настройки прибора
        /// переносятся во ВСЕ открытые спектры этого прибора. Решение то же,
        /// что у прежних двух галок (`S70`): умолчание прибора меняем, уже
        /// открытые копии соседних документов не трогаем (критерий 11).
        /// </summary>
        internal static void SaveOptionsToDevice(ResultData resultData, FWHMPeakDetectionMethodConfig source)
        {
            if (resultData == null || source == null
                || resultData.DeviceConfigReference == null
                || string.IsNullOrEmpty(resultData.DeviceConfigReference.Guid))
            {
                return;
            }

            DeviceConfigManager manager = DeviceConfigManager.GetInstance();
            DeviceConfigInfo device;
            if (!manager.DeviceConfigMap.TryGetValue(resultData.DeviceConfigReference.Guid, out device)
                || device == null
                || !(device.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig devicePeak))
            {
                return;
            }

            if (FsaCalculationOptions.FromConfig(devicePeak).Stamp
                == FsaCalculationOptions.FromConfig(source).Stamp)
            {
                return;
            }

            devicePeak.DbLookupsForFsa = source.DbLookupsForFsa;
            devicePeak.ChainEquilibrium = source.ChainEquilibrium;
            devicePeak.AtomicXrayForFsa = source.AtomicXrayForFsa;
            devicePeak.CascadeSummingForFsa = source.CascadeSummingForFsa;
            devicePeak.BackscatterForFsa = source.BackscatterForFsa;
            devicePeak.EscapeAndAnnihilationForFsa = source.EscapeAndAnnihilationForFsa;
            devicePeak.PileUpForFsa = source.PileUpForFsa;
            manager.SaveConfigQuiet(device);
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
            string keep = this.selectedLayer;
            this.suspendSelection = true;
            this.reportTable.BeginUpdate();
            try
            {
                this.tableModel.Rows.Clear();
                bool blockOpened = false;
                foreach (FsaReportRow row in rows)
                {
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

                    this.tableModel.Rows.Add(this.MakeRow(
                        row, row.Kind == FsaReportRowKind.Residual ? OwnText(KeyResidualRow) : null));
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
            var cells = new[] { new Cell(string.Empty, (Image)null), new Cell(string.Empty), new Cell(string.Empty) };
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
            var row = new Row(new[] { new Cell(string.Empty, (Image)null), name, new Cell(string.Empty) });
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

            bool oldFormat = this.presentation.MatrixOldFormat;
            made.Add(this.MakeMarkRow(KeyMatrixRow,
                                      OwnText(result.ResponseMatrixUsed
                                                  ? KeyMatrixUsed
                                                  : oldFormat ? KeyMatrixOldFormat : KeyMatrixNotUsed),
                                      false));
            made.Add(this.MakeMarkRow(KeyEfficiencyRow,
                                      OwnText(result.EfficiencyUsed ? KeyEfficiencyUsed : KeyEfficiencyNotUsed),
                                      false));
            made.Add(this.MakeMarkRow(KeySummingRow,
                                      OwnText(result.CascadeSummingUsed ? KeySummingUsed : KeySummingNotUsed),
                                      false));

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
                made.Add(this.MakeMarkRow(KeyBackgroundRejectedRow, result.BackgroundRejected, true));
            }

            if (result.DriftOnGridEdge)
            {
                made.Add(this.MakeMarkRow(KeyDriftRow, OwnText(KeyDriftEdge), true));
            }

            if (result.CompositionSuppressed)
            {
                // Имя пересилившего образа — не надпись, а данные результата,
                // и переводу не подлежит (`Backscatter`, `Esc-I`, нуклид).
                made.Add(this.MakeMarkRow(KeySuppressedRow, result.SuppressorName ?? string.Empty, true));
            }

            return made;
        }

        /// <summary>
        /// (`A247`) Строка пометки: подпись слева, слово состояния справа.
        /// Числа здесь не бывает никогда — только у невязки и χ²/ndf, и оба
        /// берут его у модели.
        /// </summary>
        Row MakeMarkRow(string captionKey, string value, bool attention)
        {
            string caption = OwnText(captionKey);
            var name = new Cell(caption);
            var cell = new Cell(value ?? string.Empty);
            name.ForeColor = Color.Black;
            cell.ForeColor = attention ? Color.Firebrick : Color.Gray;

            // Подпись переносится по ширине колонки: усечение многоточием и
            // есть та беда, ради которой заведена `A247`.
            name.WordWrap = true;
            name.ToolTipText = caption;
            cell.ToolTipText = cell.Text;
            var row = new Row(new[] { new Cell(string.Empty, (Image)null), name, cell });
            row.Tag = ServiceRow(caption, cell.Text, attention);
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
                this.selectedLayer = null;
                this.PushHighlight();
                return true;
            }

            int index = this.RowOfLayer(layerName);
            if (index < 0)
            {
                return false;
            }

            this.tableModel.Selections.SelectCell(index, 1);
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

            this.selectedLayer = this.LayerOfSelection();
            this.PushHighlight();
        }

        /// <summary>
        /// Слой выбранной строки; null — выбора нет или выбрана строка без
        /// ленты (необнаруженный кандидат, невязка, качество, состояние).
        /// Такую выбрать можно, и приглушать по ней НЕЧЕГО: на графике её нет.
        /// </summary>
        string LayerOfSelection()
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

            FsaReportRow model = this.tableModel.Rows[index].Tag as FsaReportRow;
            return model != null && model.Layer != null ? model.Layer.Name : null;
        }

        /// <summary>
        /// Сказать графику, что выделено. ⛔ Ничего, кроме краски, это не
        /// меняет: ни расчёта, ни представления, ни группировки — поэтому
        /// заказа пересчёта здесь нет и не будет.
        /// </summary>
        void PushHighlight()
        {
            if (this.document != null && !this.document.IsDisposed
                && this.document.EnergySpectrumView != null)
            {
                this.document.EnergySpectrumView.FsaHighlight = this.selectedLayer;
            }
        }

        /// <summary>
        /// Вернуть выбор на ту же строку состава после перестройки таблицы.
        /// Слоя может уже не быть (сменился спектр, группировка, состав) —
        /// тогда выбор снимается, и приглушение снимается вместе с ним.
        /// </summary>
        void RestoreSelection(string layerName)
        {
            this.suspendSelection = true;
            try
            {
                this.tableModel.Selections.Clear();
                int index = layerName != null ? this.RowOfLayer(layerName) : -1;
                if (index >= 0)
                {
                    this.tableModel.Selections.SelectCell(index, 1);
                    this.selectedLayer = layerName;
                }
                else
                {
                    this.selectedLayer = null;
                }
            }
            finally
            {
                this.suspendSelection = false;
            }

            this.PushHighlight();
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
        Row MakeRow(FsaReportRow row, string caption = null)
        {
            Color fore = row.Kind == FsaReportRowKind.Status && row.Warning
                ? Color.DarkOrange
                : row.Warning ? Color.Firebrick : row.Muted ? Color.Gray : Color.Black;

            var swatch = new Cell(string.Empty, this.SwatchOf(row));
            var name = new Cell(caption ?? row.Name ?? string.Empty);
            var value = new Cell(row.Value ?? string.Empty);
            name.ForeColor = fore;
            value.ForeColor = fore;
            // (`AMBER6`) Подсказка строки: своя, если она есть, иначе сам
            // текст — как было до 08.09.2026.
            name.ToolTipText = string.IsNullOrEmpty(row.Hint) ? name.Text : row.Hint;
            value.ToolTipText = row.Value;

            // Полный текст без усечения: строки блока качества и строка
            // состояния (причина отказа бывает длинной) переносятся по ширине
            // колонки.
            if (row.Kind == FsaReportRowKind.Quality || row.Kind == FsaReportRowKind.Status
                || row.Kind == FsaReportRowKind.Residual || row.Kind == FsaReportRowKind.NoBackground)
            {
                name.WordWrap = true;
            }

            var tableRow = new Row(new[] { swatch, name, value });
            tableRow.Tag = row;
            return tableRow;
        }

        /// <summary>
        /// Образец строки — тем же правилом, что лента на графике: сплошной
        /// цвет слоя, штрих сумм-пиков (<see cref="FsaPalette.SumPeakHatchColor"/>)
        /// или клетка невязки по чёрной половине (`A28`). Пусто — образца нет.
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

            var bitmap = new Bitmap(14, 10);
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

                    case FsaSwatchKind.ResidualCross:
                        using (Brush brush = new HatchBrush(HatchStyle.Cross, row.Color, Color.White))
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

        void ReportTable_Resize(object sender, EventArgs e)
        {
            this.FitColumns();
        }

        /// <summary>
        /// Колонка «Компонент» получает всю ширину, оставшуюся от образца и
        /// значения: у XPTable нет колонки-заполнителя, а без этого длинный
        /// текст обрезался бы по фиксированной ширине.
        /// </summary>
        void FitColumns()
        {
            int spare = this.reportTable.ClientSize.Width - SwatchColumnWidth - ValueColumnWidth
                        - SystemInformation.VerticalScrollBarWidth - 4;
            this.componentColumn.Width = Math.Max(80, spare);
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
            this.residualBandCheckBox.Enabled = has;
            this.reportTable.Enabled = has;

            bool nucBase = has && this.sourceNucBaseRadio.Checked;
            this.equilibriumCheckBox.Enabled = nucBase;
            this.toolTip.SetToolTip(this.equilibriumCheckBox,
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
            this.toolTip.SetToolTip(this.parentsRadio, parentsTip);

            // Матрица уже несёт рассеяние и вылеты: переключатель управляет
            // только ОТДЕЛЬНОЙ дополнительной компонентой, и подсказка обязана
            // это сказать (флажок не вправе обещать отключение физики матрицы).
            bool matrix = result != null && result.ResponseMatrixUsed;
            string extra = matrix
                ? Resources.FSAReportTipCalculation + " " + Resources.FSAReportTipMatrixExtra
                : Resources.FSAReportTipCalculation;
            this.toolTip.SetToolTip(this.backscatterCheckBox, extra);
            this.toolTip.SetToolTip(this.escapeCheckBox, extra);

            this.loading = true;
            try
            {
                bool parents = parentsAllowed && this.requestedGrouping == FsaGrouping.Parents;
                this.parentsRadio.Checked = parents;
                this.daughtersRadio.Checked = !parents;
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

        /// <summary>Подсказки, не зависящие от состояния: меняет расчёт или только показ.</summary>
        void SetToolTips()
        {
            this.toolTip.SetToolTip(this.sourcePeaksRadio, Resources.FSAReportTipCalculation);
            this.toolTip.SetToolTip(this.sourceNucBaseRadio, Resources.FSAReportTipCalculation);
            this.toolTip.SetToolTip(this.daughtersRadio, Resources.FSAReportTipGrouping);
            this.toolTip.SetToolTip(this.parentsRadio, Resources.FSAReportTipGrouping);
            this.toolTip.SetToolTip(this.equilibriumCheckBox, Resources.FSAReportTipCalculation);
            this.toolTip.SetToolTip(this.atomicXrayCheckBox, Resources.FSAReportTipCalculation);
            this.toolTip.SetToolTip(this.cascadeSummingCheckBox, Resources.FSAReportTipCalculation);
            this.toolTip.SetToolTip(this.backscatterCheckBox, Resources.FSAReportTipCalculation);
            this.toolTip.SetToolTip(this.escapeCheckBox, Resources.FSAReportTipCalculation);
            this.toolTip.SetToolTip(this.pileUpCheckBox, Resources.FSAReportTipCalculation);

            // (`A248`) Лента невязки расчёта НЕ меняет, и подсказка обязана
            // сказать это прямо: подпись, по решению Amber, ничего не
            // поясняет, а окно делит подсказки ровно на два рода — «меняет
            // расчёт» и «меняет только показ». ⚠ Подсказка тут ВТОРОЙ
            // признак, а не единственный: с `A265` род переключателя виден
            // и без наведения — по группе, в которой он лежит.
            this.toolTip.SetToolTip(this.residualBandCheckBox, OwnText(KeyResidualBandTip));
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
                // дальше и остался бы поблекшим навсегда.
                this.selectedLayer = null;
                this.PushHighlight();

                // (`A248`) Тот же довод: окна не станет, а с ним и галочки, —
                // спрятанная лента невязки осталась бы спрятанной навсегда, и
                // вернуть её было бы нечем.
                this.showResidualBand = true;
                this.PushResidualBand();

                // (`A295`) Подписку на такт вида снимаем здесь же: иначе
                // документ держал бы ссылку на закрытое окно и звал бы
                // его на каждом обновлении.
                if (this.document != null)
                {
                    this.document.ViewRefreshed -= this.DocumentViewRefreshed;
                }

                if (this.session != null)
                {
                    this.session.Completed -= this.SessionCompleted;
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
