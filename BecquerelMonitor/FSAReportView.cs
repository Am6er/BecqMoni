using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
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
    /// ДВЕ ОСИ ПЕРЕКЛЮЧАТЕЛЕЙ, и смешивать их нельзя:
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

        public FSAReportView(MainForm mainForm)
        {
            this.mainForm = mainForm;
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

            this.SetToolTips();
            this.reportTable.Resize += this.ReportTable_Resize;
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
                }
                else if (this.ProbeConsumer && !this.IsDisposed)
                {
                    // Пробы: окно без ручки, очереди сообщений нет — читаем
                    // прямо здесь. В приложении ручка есть всегда.
                    this.RefreshReport();
                }
            }
            catch (Exception)
            {
                // окно успело закрыться — читать уже некому
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
        /// одна строка состояния (считается / причина невозможности /
        /// ошибка с полной цепочкой причины, слова сеанса `A95`); результат
        /// есть — полный отчёт, а при идущем пересчёте перед ним заметная
        /// строка «Пересчёт…» (`A32`: старые строки нельзя выдавать за
        /// актуальные).
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
                string text = running ? Resources.FSACalculating : status;
                if (!string.IsNullOrEmpty(text))
                {
                    rows.Add(StatusRow(text));
                }

                return rows;
            }

            this.presentation = FsaPresentationBuilder.Build(result, this.EffectiveGrouping, oldFormat);
            if (running)
            {
                FsaReportRow recalculating = StatusRow(Resources.FSAReportRecalculating);
                recalculating.Warning = true;
                rows.Add(recalculating);
            }

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

        /// <summary>Перечитать сеанс: таблица и доступность элементов управления.</summary>
        public void RefreshReport()
        {
            List<FsaReportRow> rows = this.BuildRows();
            this.reportTable.BeginUpdate();
            try
            {
                this.tableModel.Rows.Clear();
                foreach (FsaReportRow row in rows)
                {
                    this.tableModel.Rows.Add(this.MakeRow(row));
                }
            }
            finally
            {
                this.reportTable.EndUpdate();
            }

            this.FitColumns();
            this.UpdateAvailability();
        }

        /// <summary>
        /// Строка XPTable из строки модели: образец, имя, значение. Смысл
        /// читается из <see cref="FsaReportRow.Kind"/> и признаков, никогда —
        /// из текста. Цвет текста никогда не остаётся единственным носителем
        /// смысла: у серых строк есть «&lt; … %», у красных — свой текст.
        /// </summary>
        Row MakeRow(FsaReportRow row)
        {
            Color fore = row.Kind == FsaReportRowKind.Status && row.Warning
                ? Color.DarkOrange
                : row.Warning ? Color.Firebrick : row.Muted ? Color.Gray : Color.Black;

            var swatch = new Cell(string.Empty, this.SwatchOf(row));
            var name = new Cell(row.Name ?? string.Empty);
            var value = new Cell(row.Value ?? string.Empty);
            name.ForeColor = fore;
            value.ForeColor = fore;
            name.ToolTipText = row.Name;
            value.ToolTipText = row.Value;

            // Полный текст без усечения: строка качества и строка состояния
            // (причина отказа бывает длинной) переносятся по ширине колонки.
            if (row.Kind == FsaReportRowKind.Quality || row.Kind == FsaReportRowKind.Status)
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
                    : this.presentation != null && this.presentation.ParentGroupingRefusal != null
                        ? string.Format(CultureInfo.CurrentCulture, Resources.FSAReportTipParentsRefused,
                                        this.presentation.ParentGroupingRefusal)
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
                if (this.session != null)
                {
                    this.session.Completed -= this.SessionCompleted;
                    this.session = null;
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
