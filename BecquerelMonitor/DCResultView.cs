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
                this.resultTranslation = value;
                this.comboBox1.SelectedIndex = (int)value;
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

        // Token: 0x06000435 RID: 1077 RVA: 0x000136C8 File Offset: 0x000118C8
        void button1_Click(object sender, EventArgs e)
        {
            ROIConfigData config = null;
            if (this.previousCollection != null)
            {
                config = this.previousCollection.ROIConfig;
            }
            this.mainForm.ShowROIConfigForm(config);
        }

        // Token: 0x06000436 RID: 1078 RVA: 0x00013700 File Offset: 0x00011900
        public void ShowResult(MeasurementResultCollection resultCollection, bool refresh)
        {
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
            if (this.previousCollection == null || this.previousCollection.ROIConfig.Guid != resultCollection.ROIConfig.Guid)
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
            if (this.resultTranslation == ResultTranslation.CountsPerSecond)
            {
                format = "f5";
                format_int = 5;
            }
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
                        cell2.Tag = false;
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
                    // (`AMBER133`) приписка суммирования зависит от единиц — обновляется и здесь
                    row2.Cells[0].Text = NameText(measurementResult2);
                    row2.Cells[0].ToolTipText = NameTip(measurementResult2);
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
                        row2.Cells[1].Tag = false;
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

        /// <summary>
        /// (`AMBER133`, П167 28.09.2026) Имя зоны с припиской каскадного
        /// суммирования: «Σ×1.068», когда поправка вошла в беккерели, «без
        /// поправки на Σ», когда её нет, а у нуклида линия стоит в каскаде.
        /// Полная фраза — подсказкой клетки. Прежде таблица молчала, а FSA ту
        /// же линию поправлял — два числа расходились без объяснения.
        /// </summary>
        static string NameText(MeasurementResult result)
        {
            string name = result.ROIDefinition.Name;
            if (!result.IsValid)
            {
                return name;
            }

            if (!string.IsNullOrEmpty(result.SummingNote))
            {
                name += "  [" + result.SummingNote + "]";
            }

            // (`S199`, П174) помеха природного спутника в окне зоны
            if (result.Interference != null)
            {
                name += "  [" + string.Format(CultureInfo.InvariantCulture, OwnText(KeyInterferenceCell),
                                              result.Interference.Companion ?? string.Empty,
                                              result.Interference.Factor.ToString("F2", CultureInfo.InvariantCulture))
                        + "]";
            }

            return name;
        }

        /// <summary>
        /// Подсказка клетки имени: фраза суммирования (`AMBER133`) и фраза
        /// помехи природного спутника (`S199`), каждая своей строкой.
        /// </summary>
        static string NameTip(MeasurementResult result)
        {
            string tip = result.SummingProblem;
            if (result.IsValid && result.Interference != null)
            {
                FullSpectrumAnalysis.FsaLineInterference item = result.Interference;
                string said = string.Format(CultureInfo.InvariantCulture, OwnText(KeyInterferenceTip),
                                            item.LineKev.ToString("F1", CultureInfo.InvariantCulture),
                                            item.Companion ?? string.Empty, item.Reference ?? string.Empty,
                                            item.Component ?? string.Empty,
                                            item.Factor.ToString("F2", CultureInfo.InvariantCulture));
                tip = string.IsNullOrEmpty(tip) ? said : tip + Environment.NewLine + said;
            }

            return tip;
        }

        static Cell NameCell(MeasurementResult result)
        {
            Cell cell = new Cell(NameText(result));
            cell.ToolTipText = NameTip(result);
            return cell;
        }

        /// <summary>(`S199`) Ключ собственного resx: приписка «U-235 ×1.72» к имени зоны.</summary>
        const string KeyInterferenceCell = "DCResult_InterferenceCell";

        /// <summary>(`S199`) Ключ собственного resx: полная фраза помехи — подсказка клетки.</summary>
        const string KeyInterferenceTip = "DCResult_InterferenceTip";

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
                    if (result.ResultValue >= result.MDA)
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
            this.resultTranslation = (ResultTranslation)this.comboBox1.SelectedIndex;
            this.ShowResult(this.previousCollection, false);
        }

        // Token: 0x0600043A RID: 1082 RVA: 0x00013D30 File Offset: 0x00011F30
        void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.resultCorrection = (ResultCorrection)this.comboBox2.SelectedIndex;
            this.ShowResult(this.previousCollection, false);
            // (`AMBER148`) режим поправки решает и за беккерели панели выделения
            this.RefreshSelectionPanel();
        }

        // Token: 0x040001A3 RID: 419
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x040001A4 RID: 420
        MainForm mainForm;

        // Token: 0x040001A5 RID: 421
        MeasurementResultCollection previousCollection;

        // Token: 0x040001A6 RID: 422
        ResultTranslation resultTranslation;

        // Token: 0x040001A7 RID: 423
        ResultCorrection resultCorrection;
    }
}
