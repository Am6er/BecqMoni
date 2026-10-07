using BecquerelMonitor.Properties;
using System;
using System.Globalization;
using System.Threading;
using XPTable.Models;

namespace BecquerelMonitor
{
    // Token: 0x0200004E RID: 78
    public partial class DCResultView : ToolWindow
    {
        // Token: 0x17000177 RID: 375
        // (get) Token: 0x0600042F RID: 1071 RVA: 0x000135AC File Offset: 0x000117AC
        // (set) Token: 0x06000430 RID: 1072 RVA: 0x000135B4 File Offset: 0x000117B4
        public ResultTranslation ResultTranslation
        {
            get
            {
                return this.resultTranslation;
            }
            set
            {
                // (`AMBER208`) окно — только беккерели: «отсчёты» и «имп/с» из прежней
                // раскладки (`DCResultView,CountsPerSecond, Nothing`) были бы распадами
                if (value < ResultTranslation.Becquerels)
                {
                    value = ResultTranslation.Becquerels;
                }
                this.resultTranslation = value;
                this.comboBox1.SelectedIndex = (int)value - (int)ResultTranslation.Becquerels;
            }
        }

        // Token: 0x17000178 RID: 376
        // (get) Token: 0x06000431 RID: 1073 RVA: 0x000135CC File Offset: 0x000117CC
        // (set) Token: 0x06000432 RID: 1074 RVA: 0x000135D4 File Offset: 0x000117D4
        public ResultCorrection ResultCorrection
        {
            get
            {
                return this.resultCorrection;
            }
            set
            {
                this.resultCorrection = value;
                this.comboBox2.SelectedIndex = (int)value;
            }
        }

        // Token: 0x06000433 RID: 1075 RVA: 0x000135EC File Offset: 0x000117EC
        public DCResultView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();
            this.comboBox1.SelectedIndex = 0;
            this.comboBox2.SelectedIndex = 0;
            this.columnModel1.Columns[1].Renderer = new ResultValueCellRenderer();
            // (`AMBER148`) Режим поправки этого окна решает и за беккерели панели
            // выделения — окно числится среди живых, пока не уничтожено.
            LiveViews.Add(this);
            this.Disposed += (s, e) => LiveViews.Remove(this);
            this.DockStateChanged += (s, e) => this.RefreshSelectionPanel();
            // (`AMBER208`) окно стало видимым (вкладка выбрана, панель открыта) —
            // перечитать разбор: таймер MainForm обновляет только ВИДИМЫЕ окна и
            // только при изменении спектра, а скрытой вкладке ничего не приходило,
            // и после открытия она показывала состояние момента своего создания
            // («FSA: no spectrum» — документов ещё не было).
            this.VisibleChanged += (s, e) =>
            {
                if (this.Visible && this.mainForm != null && !this.IsDisposed)
                {
                    this.previousCollection = null;
                    this.ShowResult(true);
                }
            };
            this.InitFsa();
        }

        /// <summary>(`AMBER148`) Все окна результатов, ещё не уничтоженные.</summary>
        static readonly System.Collections.Generic.List<DCResultView> LiveViews =
            new System.Collections.Generic.List<DCResultView>();

        /// <summary>
        /// (`AMBER148`, П192 01.10.2026) Приводить ли беккерели ПАНЕЛИ ВЫДЕЛЕНИЯ
        /// к дате отбора: да, когда хоть одно открытое (не скрытое) окно
        /// результатов стоит в режиме «поправка на полураспад». Панель своего
        /// переключателя не имеет, а два разных правила для зон и для выделения
        /// дали бы два разных числа одной линии без объяснения. Окон
        /// результатов может быть до четырёх; режим «хоть одно» выбран потому,
        /// что человек, включивший поправку, ждёт её и на панели, а сама панель
        /// подписью говорит, к какому моменту отнесено её число.
        /// </summary>
        public static bool HalfLifeCorrectionShown()
        {
            foreach (DCResultView view in LiveViews)
            {
                if (!view.IsDisposed && !view.IsHidden && view.DockPanel != null
                    && view.resultCorrection == ResultCorrection.HalfLifeCorrection)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// (`AMBER148`) Подпись момента у беккерелей панели выделения — строкой
        /// под подписью линии: «среднее за набор» без поправки, «на дату отбора
        /// … ×множитель» с ней, «среднее за набор: нет T½», когда поправка
        /// включена, а периода у линии нет. Строки — этого окна (свой resx).
        /// </summary>
        public static string SelectionMomentText(bool corrected, bool hasHalfLife, DateTime sampling, double factor)
        {
            if (!corrected)
            {
                return OwnText(KeyMomentMean);
            }

            if (!hasHalfLife)
            {
                return OwnText(KeyMomentNoHalfLife);
            }

            return string.Format(CultureInfo.InvariantCulture, OwnText(KeyMomentSampling),
                                 sampling.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                                 factor.ToString("F3", CultureInfo.InvariantCulture));
        }

        /// <summary>(`AMBER148`) Перерисовать панель выделения открытого документа.</summary>
        void RefreshSelectionPanel()
        {
            DocEnergySpectrum document = this.mainForm != null ? this.mainForm.ActiveDocument : null;
            if (document != null && document.EnergySpectrumView != null)
            {
                document.EnergySpectrumView.RefreshSelectionOverlay();
            }
        }

        /// <summary>
        /// (`AMBER148`) Заголовок столбца значений называет, к какому моменту
        /// отнесено число: прежде «Result» без поправки молча значил «среднее за
        /// набор», а с поправкой — «на дату отбора». Полная фраза — подсказкой.
        /// </summary>
        void ShowMomentHeader(MeasurementResultCollection resultCollection)
        {
            Column column = this.columnModel1.Columns[1];
            if (this.resultCorrection == ResultCorrection.HalfLifeCorrection)
            {
                // Пустая таблица (нет документа или зон) — заголовок тот же, даты нет.
                string sampling = resultCollection != null && resultCollection.ResultData != null
                                  && resultCollection.ResultData.SampleInfo != null
                    ? resultCollection.ResultData.SampleInfo.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    : "—";
                column.Text = OwnText(KeyColumnSampling);
                column.ToolTipText = string.Format(CultureInfo.InvariantCulture, OwnText(KeyColumnSamplingTip), sampling);
            }
            else
            {
                column.Text = OwnText(KeyColumnMean);
                column.ToolTipText = OwnText(KeyColumnMeanTip);
            }
        }

        const string KeyColumnMean = "DCResult_ColumnMean";
        const string KeyColumnMeanTip = "DCResult_ColumnMeanTip";
        const string KeyColumnSampling = "DCResult_ColumnSampling";
        const string KeyColumnSamplingTip = "DCResult_ColumnSamplingTip";
        const string KeyMomentMean = "DCResult_MomentMean";
        const string KeyMomentNoHalfLife = "DCResult_MomentNoHalfLife";
        const string KeyMomentSampling = "DCResult_MomentSampling";

        // Token: 0x06000434 RID: 1076 RVA: 0x00013650 File Offset: 0x00011850
        protected override string GetPersistString()
        {
            return string.Concat(new string[]
            {
                base.GetType().ToString(),
                ",",
                this.resultTranslation.ToString(),
                ", ",
                this.resultCorrection.ToString()
            });
        }

        // Token: 0x06000436 RID: 1078 RVA: 0x00013700 File Offset: 0x00011900
        public void ShowResult(bool refresh)
        {
            // (`AMBER208`, решения Amber 06–07.10.2026) строки — из разбора FSA
            // активного документа; зон ROI у окна больше нет.
            MeasurementResultCollection resultCollection = this.FsaCollection();
            if (resultCollection == null || resultCollection.ResultList == null)
            {
                this.table1.BeginUpdate();
                this.tableModel1.Rows.Clear();
                this.table1.EndUpdate();
                this.previousCollection = null;
                this.ShowMomentHeader(null);
                return;
            }
            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
            decimal errorLevel = globalConfig.MeasurementConfig.ErrorLevel;
            bool showValuesForNDResult = globalConfig.MeasurementConfig.ShowValuesForNDResult;
            if (this.previousCollection == null || this.previousCollection.SourceKey != resultCollection.SourceKey)
            {
                refresh = true;
            }
            if (this.tableModel1.Rows.Count != resultCollection.ResultList.Count)
            {
                refresh = true;
            }
            this.previousCollection = resultCollection;
            MeasurementResultManager measurementResultManager = new MeasurementResultManager();
            resultCollection = measurementResultManager.Translate(resultCollection, this.resultTranslation);
            if (this.resultCorrection == ResultCorrection.HalfLifeCorrection)
            {
                resultCollection = measurementResultManager.Correct(resultCollection);
            }
            this.ShowMomentHeader(resultCollection);
            if (errorLevel == 1m)
            {
                this.columnModel1.Columns[2].Text = Resources.Uncertain + " " + Resources.Sigma;
            } else
            {
                this.columnModel1.Columns[2].Text = Resources.Uncertain + " " + errorLevel.ToString(CultureInfo.InvariantCulture) + Resources.Sigma;
            }
            this.table1.EnableToolTips = true;
            this.table1.BeginUpdate();
            string format = "f2";
            int format_int = 2;
            if (refresh)
            {
                this.tableModel1.Rows.Clear();
                for (int i = 0; i < resultCollection.ResultList.Count; i++)
                {
                    MeasurementResult measurementResult = resultCollection.ResultList[i];
                    Row row = new Row();
                    row.Cells.Add(NameCell(measurementResult));
                    if (measurementResult.IsValid)
                    {
                        Cell cell = new Cell(measurementResult.ResultValue.ToString(format, CultureInfo.InvariantCulture), Math.Round(measurementResult.ResultValue, format_int));
                        bool flag = this.CheckDetected(measurementResult);
                        cell.Tag = flag;
                        double num = measurementResult.ResultError * (double)errorLevel;
                        double epsilon;
                        if (measurementResult.ResultValue != 0)
                        {
                            epsilon = 100.0 * num / Math.Abs(measurementResult.ResultValue);
                        } else
                        {
                            epsilon = 0;
                        }
                        if (showValuesForNDResult || flag)
                        {
                            row.Cells.Add(cell);
                            row.Cells.Add(new Cell(Resources.PlusMinus + num.ToString(format, CultureInfo.InvariantCulture) + " (" + epsilon.ToString(format, CultureInfo.InvariantCulture) + Resources.PercentCharacter + ")"));
                        }
                        else
                        {
                            row.Cells.Add(new Cell("0", 0.0));
                            row.Cells.Add(new Cell("-"));
                        }
                        if (measurementResult.MDA > 0.0)
                        {
                            row.Cells.Add(new Cell(measurementResult.MDA.ToString(format, CultureInfo.InvariantCulture), Math.Round(measurementResult.MDA, format_int)));
                        } else
                        {
                            row.Cells.Add(new Cell("0", 0.0));
                        }
                    }
                    else
                    {
                        // «Нет K» и подобные причины отличаются от ошибки
                        // счёта: без причины строка читалась бы как поломка.
                        Cell cell2 = new Cell(string.IsNullOrEmpty(measurementResult.StatusText)
                            ? Resources.ErrorString : measurementResult.StatusText);
                        // строка состояния — текст, а не «не обнаружен»: значок ND
                        // рисовался бы поверх текста (замечание Amber 06.10.2026)
                        cell2.Tag = null;
                        row.Cells.Add(cell2);
                        row.Cells.Add(new Cell(""));
                        row.Cells.Add(new Cell(""));
                    }
                    row.Tag = i;
                    this.tableModel1.Rows.Add(row);
                }
            }
            else
            {
                foreach (object obj in this.tableModel1.Rows)
                {
                    Row row2 = (Row)obj;
                    int index = (int)row2.Tag;
                    if (index >= resultCollection.ResultList.Count) { continue; }
                    MeasurementResult measurementResult2 = resultCollection.ResultList[index];
                    row2.Cells[0].Text = NameText(measurementResult2);
                    if (measurementResult2.IsValid)
                    {
                        bool flag2 = this.CheckDetected(measurementResult2);
                        row2.Cells[1].Tag = flag2;
                        double num2 = measurementResult2.ResultError * (double)errorLevel;
                        double epsilon;
                        if (measurementResult2.ResultValue != 0)
                        {
                            epsilon = 100.0 * num2 / Math.Abs(measurementResult2.ResultValue);
                        }
                        else
                        {
                            epsilon = 0;
                        }
                        if (showValuesForNDResult || flag2)
                        {
                            row2.Cells[1].Text = measurementResult2.ResultValue.ToString(format, CultureInfo.InvariantCulture);
                            row2.Cells[1].Data = Math.Round(measurementResult2.ResultValue, format_int);
                            row2.Cells[2].Text = Resources.PlusMinus + num2.ToString(format, CultureInfo.InvariantCulture) + " (" + epsilon.ToString(format, CultureInfo.InvariantCulture) + Resources.PercentCharacter + ")";
                        }
                        else
                        {
                            row2.Cells[1].Data = 0.0;
                            row2.Cells[1].Text = "0";
                            row2.Cells[2].Text = "-";
                        }
                        if (measurementResult2.MDA > 0.0)
                        {
                            row2.Cells[3].Text = measurementResult2.MDA.ToString(format, CultureInfo.InvariantCulture);
                            row2.Cells[3].Data = Math.Round(measurementResult2.MDA, format_int);
                        } else
                        {
                            row2.Cells[3].Text = "0";
                            row2.Cells[3].Data = 0.0;
                        }
                    }
                    else
                    {
                        row2.Cells[1].Text = string.IsNullOrEmpty(measurementResult2.StatusText)
                            ? Resources.ErrorString : measurementResult2.StatusText;
                        row2.Cells[1].Tag = null;
                        row2.Cells[1].Data = null;
                        row2.Cells[2].Text = string.Empty;
                        row2.Cells[2].Data = 0.0;
                        row2.Cells[3].Text = string.Empty;
                        row2.Cells[3].Data = 0.0;
                    }
                }
            }
            this.table1.EndUpdate();
        }

        static string NameText(MeasurementResult result)
        {
            return result.Line != null ? result.Line.Name ?? string.Empty : string.Empty;
        }

        static Cell NameCell(MeasurementResult result)
        {
            return new Cell(NameText(result));
        }

        /// <summary>
        /// (`S199`) Строки этого окна — из его собственного resx (как у окна
        /// отчёта FSA): общий <c>Properties/Resources</c> делят другие полосы.
        /// </summary>
        static readonly System.ComponentModel.ComponentResourceManager OwnResources =
            new System.ComponentModel.ComponentResourceManager(typeof(DCResultView));

        static string OwnText(string key)
        {
            return OwnResources.GetString(key) ?? key;
        }

        // Token: 0x06000437 RID: 1079 RVA: 0x00013C68 File Offset: 0x00011E68
        bool CheckDetected(MeasurementResult result)
        {
            bool result2 = false;
            int detectionCondition = this.globalConfigManager.GlobalConfig.MeasurementConfig.DetectionCondition;
            decimal detectionLevel = this.globalConfigManager.GlobalConfig.MeasurementConfig.DetectionLevel;
            switch (detectionCondition)
            {
                case 0:
                    if (result.ResultError > 0.0 && result.ResultValue >= result.ResultError * (double)detectionLevel)
                    {
                        result2 = true;
                    }
                    break;
                case 1:
                    // ⛔ (`AMBER192`, решение Amber 05.10.2026 «Нет MDA → «не
                    //    обнаружена»») MDA не определён (−1 у зоны без простой
                    //    разности с фоном, 0 без времени) — зона НЕ обнаружена.
                    //    Прежде «≥ −1» было истинно всегда.
                    if (result.MDA > 0.0 && result.ResultValue >= result.MDA)
                    {
                        result2 = true;
                    }
                    break;
            }
            return result2;
        }

        // Token: 0x06000438 RID: 1080 RVA: 0x00013D04 File Offset: 0x00011F04
        public void RefreshResult()
        {
            this.previousCollection = null;
        }

        // Token: 0x06000439 RID: 1081 RVA: 0x00013D10 File Offset: 0x00011F10
        void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            // список единиц начинается с беккерелей (`AMBER208`)
            this.resultTranslation = (ResultTranslation)(this.comboBox1.SelectedIndex + (int)ResultTranslation.Becquerels);
            this.ShowResult(false);
        }

        // Token: 0x0600043A RID: 1082 RVA: 0x00013D30 File Offset: 0x00011F30
        void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.resultCorrection = (ResultCorrection)this.comboBox2.SelectedIndex;
            this.ShowResult(false);
            // (`AMBER148`) режим поправки решает и за беккерели панели выделения
            this.RefreshSelectionPanel();
        }

        // Token: 0x040001A3 RID: 419
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x040001A4 RID: 420
        MainForm mainForm;

        // Token: 0x040001A5 RID: 421
        MeasurementResultCollection previousCollection;

        // (`AMBER208`, задача Amber 06.10.2026; решения вопросником: Бк — «Матрица или
        // абсолютная кривая», причина отсутствия матрицы — только в отчёте: «Убрать
        // причину из окна». Строки ~~«Из сета нуклидов»~~ и ряд ~~«Считать строку
        // родителя связанным рядом»~~, сеансов ~~«Два, как сейчас»~~ — СНЯТЫ ответом
        // Amber 07.10.2026 (`AMBER211`): «Окно результата читает сеанс документа»;
        // «…что мы видим в окне отчёта в списке изотопов - ту активность мы и
        // считаем» — строки по составу разбора: связанный ряд строкой родителя,
        // свободные члены порознь, не обнаруженные — с пределом.)
        // Источник строк — разбор FSA активного документа ЕГО сеансом (тем же,
        // что у графика и окна отчёта: один расчёт на спектр);
        // заголовок первой колонки — шкала разбора, подсказка — счёт строк; все
        // тексты — из ресурсов окна (`DCResult_Fsa*`, en + ru). Форма ROI и
        // счёт зон сняты 07.10.2026 («Сначала снять ROI, потом один коммит»).
        DocEnergySpectrum fsaDocument;

        const string KeyFsaComputing = "DCResult_FsaComputing";
        const string KeyFsaNoSpectrum = "DCResult_FsaNoSpectrum";
        const string KeyFsaScaleMatrix = "DCResult_FsaScaleMatrix";
        const string KeyFsaScaleCurve = "DCResult_FsaScaleCurve";
        const string KeyFsaHidden = "DCResult_FsaHidden";
        const string KeyFsaReasonField = "DCResult_FsaReasonField";
        const string KeyFsaReasonNoCurve = "DCResult_FsaReasonNoCurve";
        const string KeyFsaReasonCurveUnused = "DCResult_FsaReasonCurveUnused";
        const string KeyFsaReasonOrigin = "DCResult_FsaReasonOrigin";
        const string KeyFsaReasonNoResult = "DCResult_FsaReasonNoResult";
        const string KeyFsaDetails = "DCResult_FsaDetails";
        const string KeyFsaMdaHeader = "DCResult_FsaMdaHeader";
        const string KeyFsaMdaTip = "DCResult_FsaMdaTip";
        const string KeyFsaStale = "DCResult_FsaStale";

        FullSpectrumAnalysis.FsaMeasurementResult.Texts FsaTexts()
        {
            return new FullSpectrumAnalysis.FsaMeasurementResult.Texts
            {
                ScaleMatrix = OwnText(KeyFsaScaleMatrix),
                ScaleCurve = OwnText(KeyFsaScaleCurve),
                Hidden = OwnText(KeyFsaHidden),
                ReasonFieldCurve = OwnText(KeyFsaReasonField),
                ReasonNoCurve = OwnText(KeyFsaReasonNoCurve),
                ReasonCurveUnused = OwnText(KeyFsaReasonCurveUnused),
                ReasonOrigin = OwnText(KeyFsaReasonOrigin),
                ReasonNoResult = OwnText(KeyFsaReasonNoResult),
                Details = OwnText(KeyFsaDetails)
            };
        }

        void InitFsa()
        {
            // шкала разбора живёт в заголовке первой колонки — ей нужно место;
            // предел — a# ISO 11929 (решение Amber 06.10.2026 «MDA», как у зон:
            // заголовок «MDA», расшифровка в подсказке), а не MDA Карри по зоне
            this.columnModel1.Columns[0].Width = Math.Max(this.columnModel1.Columns[0].Width, 170);
            this.columnModel1.Columns[3].Text = OwnText(KeyFsaMdaHeader);
            this.columnModel1.Columns[3].ToolTipText = OwnText(KeyFsaMdaTip);
            // Решение Amber 06.10.2026 вопросником «Подсвечивать образ нуклида в стеке»:
            // выбор строки подсвечивает образ в стеке FSA тем же механизмом, что у
            // окна отчёта (`EnergySpectrumView.FsaHighlight`); окон зон у разбора нет.
            this.table1.SelectionChanged += (s, e) => this.PushFsaHighlight();
        }

        void PushFsaHighlight()
        {
            DocEnergySpectrum document = this.mainForm != null ? this.mainForm.ActiveDocument : null;
            if (document == null || document.IsDisposed || document.EnergySpectrumView == null)
            {
                return;
            }
            string layer = null;
            if (this.previousCollection != null && this.previousCollection.ResultList != null)
            {
                int[] indices = this.tableModel1.Selections.SelectedIndicies;
                if (indices != null && indices.Length > 0 && indices[0] >= 0 && indices[0] < this.tableModel1.Rows.Count
                    && this.tableModel1.Rows[indices[0]].Tag is int index
                    && index >= 0 && index < this.previousCollection.ResultList.Count)
                {
                    MeasurementResult picked = this.previousCollection.ResultList[index];
                    if (picked.IsValid && picked.Line != null)
                    {
                        layer = picked.Line.Name;
                    }
                }
            }
            // ⛔ только краска: ни расчёта, ни представления это не меняет (как у отчёта)
            document.EnergySpectrumView.FsaHighlight = layer;
        }

        void SetFsaHeader(string text, string tip)
        {
            this.columnModel1.Columns[0].Text = text;
            this.columnModel1.Columns[0].ToolTipText = tip ?? "";
        }

        MeasurementResultCollection FsaCollection()
        {
            DocEnergySpectrum document = this.mainForm != null ? this.mainForm.ActiveDocument : null;
            ResultData rd = document != null ? document.ActiveResultData : null;
            // (`AMBER211`, решение Amber 07.10.2026 вопросником, дословно: «Окно
            // результата читает сеанс документа») СЕАНС — ДОКУМЕНТА, тот же, что у
            // графика и окна отчёта: один расчёт на спектр, и настройки разбора у
            // всех трёх одни — галочки отчёта FSA (состав «из сета» и равновесие —
            // их умолчания, `FromSetForFsa` / `ChainEquilibrium`). Прежний СВОЙ сеанс
            // (П236, «Два, как сейчас») снят: на каждое обновление спектра шло два
            // расчёта по секундам, а при записи спектра — непрерывно (пауза при
            // записи — у сеанса, `FsaAnalysisSession.EnsureUpToDate`). Сеанс
            // документа окно не сбрасывает — он не его.
            if (!ReferenceEquals(this.fsaDocument, document))
            {
                // смена сета или пиков обновляет вид документа — тем же событием
                // живёт окно отчёта FSA; без него строка «выберите сет» висела бы
                // до следующего обновления спектра
                if (this.fsaDocument != null)
                {
                    this.fsaDocument.ViewRefreshed -= this.FsaDocumentViewRefreshed;
                    if (this.fsaDocument.FsaSession != null)
                    {
                        this.fsaDocument.FsaSession.Completed -= this.FsaSessionCompleted;
                    }
                }
                if (document != null)
                {
                    document.ViewRefreshed += this.FsaDocumentViewRefreshed;
                    if (document.FsaSession != null)
                    {
                        document.FsaSession.Completed += this.FsaSessionCompleted;
                    }
                }
                this.fsaDocument = document;
            }
            FullSpectrumAnalysis.FsaAnalysisSession session = document != null ? document.FsaSession : null;
            if (rd == null || session == null || rd.EnergySpectrum == null)
            {
                this.SetFsaHeader(OwnText(KeyFsaNoSpectrum), null);
                return null;
            }
            // (`AMBER211`) Отказа «выберите сет» больше нет: строки — по списку изотопов
            // отчёта, сет окну не нужен (ответ Amber 07.10.2026: «что мы видим в окне
            // отчёта в списке изотопов - ту активность мы и считаем»).
            // Те же настройки и тот же отпечаток, что у графика и отчёта, — иначе
            // три потребителя заказывали бы три разных счёта по очереди.
            FullSpectrumAnalysis.FsaCalculationOptions options = FullSpectrumAnalysis.FsaCalculationOptions.Of(rd);
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
            FullSpectrumAnalysis.FsaResult result = session.Result;
            bool running = session.IsRunning;
            if (result == null)
            {
                this.SetFsaHeader(OwnText(KeyFsaComputing), null);
                return StatusCollection(rd, OwnText(KeyFsaComputing));
            }
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            FullSpectrumAnalysis.FsaMeasurementResult.Texts texts = this.FsaTexts();
            FullSpectrumAnalysis.FsaMeasurementResult.Summary summary;
            MeasurementResultCollection built = FullSpectrumAnalysis.FsaMeasurementResult.Build(
                rd, result, nuclides.NuclideDefinitions, texts, out summary);
            // Признак свежести: пока сеанс считает новый спектр, строки — по
            // прежнему разбору, и заголовок говорит об этом.
            string head = summary.Describe(texts) + (running ? " " + OwnText(KeyFsaStale) : "");
            this.SetFsaHeader(head, summary.Details(texts));
            return built ?? StatusCollection(rd, OwnText(KeyFsaReasonNoResult));
        }

        static MeasurementResultCollection StatusCollection(ResultData rd, string text)
        {
            var line = new MeasurementLine { Name = "FSA" };
            var collection = new MeasurementResultCollection
            {
                ResultData = rd,
                SourceKey = FullSpectrumAnalysis.FsaMeasurementResult.ConfigGuid + "-status",
                MeasurementTime = 1.0,
                LiveTime = 1.0
            };
            collection.ResultList.Add(new MeasurementResult(line, 0.0, 0.0) { IsValid = false, StatusText = text });
            return collection;
        }

        /// <summary>
        /// (`AMBER211`) Окно закрывается — отписаться от сеанса и вида документа;
        /// сеанс документа не сбрасывать: он живёт у документа.
        /// </summary>
        protected override void OnFormClosed(System.Windows.Forms.FormClosedEventArgs e)
        {
            if (this.fsaDocument != null)
            {
                this.fsaDocument.ViewRefreshed -= this.FsaDocumentViewRefreshed;
                if (this.fsaDocument.FsaSession != null)
                {
                    this.fsaDocument.FsaSession.Completed -= this.FsaSessionCompleted;
                }
                this.fsaDocument = null;
            }
            base.OnFormClosed(e);
        }

        void FsaDocumentViewRefreshed(object sender, EventArgs e)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }
            this.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate
            {
                if (!this.IsDisposed)
                {
                    this.previousCollection = null;
                    this.ShowResult(true);
                }
            });
        }

        void FsaSessionCompleted(object sender, EventArgs e)
        {
            if (this.IsDisposed || !this.IsHandleCreated)
            {
                return;
            }
            try
            {
                this.BeginInvoke((System.Windows.Forms.MethodInvoker)delegate
                {
                    if (!this.IsDisposed)
                    {
                        this.previousCollection = null;
                        this.ShowResult(true);
                    }
                });
            }
            catch (InvalidOperationException)
            {
                // окно закрылось между проверкой и вызовом — результат никому не нужен
            }
        }

        // Token: 0x040001A6 RID: 422
        ResultTranslation resultTranslation;

        // Token: 0x040001A7 RID: 423
        ResultCorrection resultCorrection;
    }
}
