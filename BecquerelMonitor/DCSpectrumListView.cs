using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using XPTable.Editors;
using XPTable.Events;
using XPTable.Models;

namespace BecquerelMonitor
{
    public partial class DCSpectrumListView : ToolWindow
    {
        private readonly MainForm mainForm;
        private readonly DocumentManager documentManager = DocumentManager.GetInstance();
        private readonly GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        public DCSpectrumListView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            InitializeComponent();
            this.columnModel1.Columns[0].Renderer = new LegendCellRenderer();
            ((Bitmap)this.button5.Image).MakeTransparent(Color.White);
            ((Bitmap)this.button6.Image).MakeTransparent(Color.White);
        }

        public void ShowSpectrumList(DocEnergySpectrum doc)
        {
            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
            int spectrumCount = doc.ResultDataFile.ResultDataList.Count;

            this.table1.BeginUpdate();
            this.tableModel1.Rows.Clear();

            for (int i = 0; i < spectrumCount; i++)
            {
                ResultData resultData = doc.ResultDataFile.ResultDataList[i];
                Row row = new Row();
                Cell cell = new Cell();
                cell.Checked = resultData.Visible;

                if (resultData == doc.ActiveResultData)
                {
                    cell.Data = globalConfig.ColorConfig.ActiveSpectrumColor.Color;
                }
                else
                {
                    cell.Data = globalConfig.ColorConfig.SpectrumColorList[i].Color;
                }

                row.Cells.Add(cell);

                string text = resultData.SampleInfo.Name;
                if (string.IsNullOrEmpty(text))
                {
                    text = Resources.UnnamedSpectrumName;
                }

                row.Cells.Add(new Cell(text));
                row.Tag = resultData;
                this.tableModel1.Rows.Add(row);
            }

            this.button1.Enabled = (spectrumCount < this.globalConfigManager.MaximumSpectrumPerFile);
            this.button3.Enabled = (spectrumCount < this.globalConfigManager.MaximumSpectrumPerFile);

            try
            {
                this.tableModel1.Selections.AddCell(doc.ActiveResultDataIndex, 0);
                this.table1.EnsureVisible(doc.ActiveResultDataIndex, 0);
            }
            catch (Exception)
            {
            }

            this.table1.EndUpdate();
            this.button5.Enabled = (doc.ActiveResultDataIndex != 0);
            this.button6.Enabled = (doc.ActiveResultDataIndex != doc.ResultDataFile.ResultDataList.Count - 1);
        }

        private void table1_SelectionChanged(object sender, SelectionEventArgs e)
        {
            GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            int num = 0;

            if (this.table1.SelectedItems.Length > 0)
            {
                num = this.table1.SelectedItems[0].Index;
            }

            for (int i = 0; i < activeDocument.ResultDataFile.ResultDataList.Count; i++)
            {
                Row row = this.tableModel1.Rows[i];
                Cell cell = row.Cells[0];
                ResultData resultData = (ResultData)row.Tag;
                resultData.Selected = row.AnyCellsSelected;

                if (row.Index == num)
                {
                    cell.Data = globalConfig.ColorConfig.ActiveSpectrumColor.Color;
                }
                else
                {
                    cell.Data = globalConfig.ColorConfig.SpectrumColorList[i].Color;
                }
            }

            this.table1.Refresh();
            this.mainForm.ActiveResultDataChanged(num);
            this.button5.Enabled = (activeDocument.ActiveResultDataIndex != 0);
            this.button6.Enabled = (activeDocument.ActiveResultDataIndex != activeDocument.ResultDataFile.ResultDataList.Count - 1);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            this.mainForm.AddNewSpectrum(activeDocument);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            this.mainForm.DeleteActiveSpectrum(activeDocument);
        }

        private void button3_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            this.mainForm.LoadSpectrumFromFile(activeDocument);
        }

        private void table1_CellCheckChanged(object sender, CellCheckBoxEventArgs e)
        {
            Row row = e.Cell.Row;
            ResultData resultData = (ResultData)row.Tag;
            resultData.Visible = e.Cell.Checked;

            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            activeDocument.UpdateEnergySpectrum();
            activeDocument.Dirty = true;
        }

        private void button5_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            int activeResultDataIndex = activeDocument.ActiveResultDataIndex;

            if (activeResultDataIndex == 0)
            {
                return;
            }

            ResultData activeResultData = activeDocument.ActiveResultData;
            activeDocument.ResultDataFile.ResultDataList.RemoveAt(activeResultDataIndex);
            activeDocument.ResultDataFile.ResultDataList.Insert(activeResultDataIndex - 1, activeResultData);
            activeDocument.ActiveResultDataIndex = activeResultDataIndex - 1;
            this.mainForm.UpdateAllView();
            this.table1.Focus();
            activeDocument.Dirty = true;
        }

        private void button6_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            int activeResultDataIndex = activeDocument.ActiveResultDataIndex;

            if (activeResultDataIndex >= activeDocument.ResultDataFile.ResultDataList.Count - 1)
            {
                return;
            }

            ResultData activeResultData = activeDocument.ActiveResultData;
            activeDocument.ResultDataFile.ResultDataList.RemoveAt(activeResultDataIndex);
            activeDocument.ResultDataFile.ResultDataList.Insert(activeResultDataIndex + 1, activeResultData);
            activeDocument.ActiveResultDataIndex = activeResultDataIndex + 1;
            this.mainForm.UpdateAllView();
            this.table1.Focus();
            activeDocument.Dirty = true;
        }

        private void button4_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            this.mainForm.SaveSpectrumToFile(activeDocument);
        }

        private void table1_EditingStopped(object sender, CellEditEventArgs e)
        {
            Cell cell = e.Cell;
            Row row = cell.Row;

            if (e.Column == 1)
            {
                ResultData resultData = (ResultData)row.Tag;
                string name = resultData.SampleInfo.Name;
                string text = ((TextCellEditor)e.Editor).TextBox.Text;

                if (name != text && (!(name == "") || !(text == Resources.UnnamedSpectrumName)))
                {
                    resultData.SampleInfo.Name = text;
                    this.mainForm.UpdateSampleInfo();
                    this.mainForm.ActiveDocument.Dirty = true;
                }
            }
        }
    }
}
