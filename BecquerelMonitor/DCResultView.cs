using BecquerelMonitor.Properties;
using System;
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
        }

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
                this.tableModel1.Rows.Clear();
                this.table1.EndUpdate();
                this.previousCollection = null;
                return;
            }
            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
            decimal errorLevel = globalConfig.MeasurementConfig.ErrorLevel;
            bool showValuesForNDResult = globalConfig.MeasurementConfig.ShowValuesForNDResult;
            if (this.previousCollection == null || this.previousCollection.ROIConfig.Guid != resultCollection.ROIConfig.Guid)
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
                    row.Cells.Add(new Cell(measurementResult.ROIDefinition.Name));
                    if (measurementResult.IsValid)
                    {
                        Cell cell = new Cell(measurementResult.ResultValue.ToString(format), Math.Round(measurementResult.ResultValue, format_int));
                        bool flag = this.CheckDetected(measurementResult);
                        cell.Tag = flag;
                        double num = measurementResult.ResultError * (double)errorLevel;
                        if (showValuesForNDResult || flag)
                        {
                            row.Cells.Add(cell);
                            row.Cells.Add(new Cell(num.ToString(format), Math.Round(num, format_int)));
                        }
                        else
                        {
                            row.Cells.Add(new Cell("0", 0.0));
                            row.Cells.Add(new Cell("0", 0.0));
                        }
                        if (measurementResult.MDA > 0.0)
                        {
                            row.Cells.Add(new Cell(measurementResult.MDA.ToString(format), Math.Round(measurementResult.MDA, format_int)));
                        } else
                        {
                            row.Cells.Add(new Cell("0", 0.0));
                        }
                    }
                    else
                    {
                        Cell cell2 = new Cell(Resources.ErrorString);
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
                    MeasurementResult measurementResult2 = resultCollection.ResultList[index];
                    if (measurementResult2.IsValid)
                    {
                        bool flag2 = this.CheckDetected(measurementResult2);
                        row2.Cells[1].Tag = flag2;
                        double num2 = measurementResult2.ResultError * (double)errorLevel;
                        if (showValuesForNDResult || flag2)
                        {
                            row2.Cells[1].Text = measurementResult2.ResultValue.ToString(format);
                            row2.Cells[1].Data = Math.Round(measurementResult2.ResultValue, format_int);
                            row2.Cells[2].Text = num2.ToString(format);
                            row2.Cells[2].Data = Math.Round(num2, format_int);
                        }
                        else
                        {
                            row2.Cells[1].Data = 0.0;
                            row2.Cells[1].Text = "0";
                            row2.Cells[2].Data = 0.0;
                            row2.Cells[2].Text = "0";
                        }
                        if (measurementResult2.MDA > 0.0)
                        {
                            row2.Cells[3].Text = measurementResult2.MDA.ToString(format);
                            row2.Cells[3].Data = Math.Round(measurementResult2.MDA, format_int);
                        } else
                        {
                            row2.Cells[3].Text = "0";
                            row2.Cells[3].Data = 0.0;
                        }
                    }
                    else
                    {
                        row2.Cells[1].Text = Resources.ErrorString;
                        row2.Cells[1].Tag = false;
                        row2.Cells[2].Data = 0.0;
                        row2.Cells[3].Data = 0.0;
                    }
                }
            }
            this.table1.EndUpdate();
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
