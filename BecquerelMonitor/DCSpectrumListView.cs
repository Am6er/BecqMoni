using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using XPTable.Editors;
using XPTable.Events;
using XPTable.Models;
using XPTable.Renderers;

namespace BecquerelMonitor
{
	// Token: 0x0200002F RID: 47
	public partial class DCSpectrumListView : ToolWindow
	{
		// Token: 0x0600027D RID: 637 RVA: 0x0000ACE4 File Offset: 0x00008EE4
		public DCSpectrumListView(MainForm mainForm)
		{
			this.mainForm = mainForm;
			this.InitializeComponent();
			this.columnModel1.Columns[0].Renderer = new LegendCellRenderer();
			((Bitmap)this.button5.Image).MakeTransparent(Color.White);
			((Bitmap)this.button6.Image).MakeTransparent(Color.White);
		}

		// Token: 0x0600027E RID: 638 RVA: 0x0000AD70 File Offset: 0x00008F70
		public void ShowSpectrumList(DocEnergySpectrum doc)
		{
			GlobalConfigInfo globalConfig = this.globalConfigManager.GlobalConfig;
			this.table1.BeginUpdate();
			this.tableModel1.Rows.Clear();
			int i;
			for (i = 0; i < doc.ResultDataFile.ResultDataList.Count; i++)
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
				if (text == "")
				{
					text = Resources.UnnamedSpectrumName;
				}
				row.Cells.Add(new Cell(text));
				row.Tag = resultData;
				this.tableModel1.Rows.Add(row);
			}
			this.button1.Enabled = (i < this.globalConfigManager.MaximumSpectrumPerFile);
			this.button3.Enabled = (i < this.globalConfigManager.MaximumSpectrumPerFile);
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

		// Token: 0x0600027F RID: 639 RVA: 0x0000AF60 File Offset: 0x00009160
		void table1_SelectionChanged(object sender, SelectionEventArgs e)
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

		// Token: 0x06000280 RID: 640 RVA: 0x0000B0BC File Offset: 0x000092BC
		void button1_Click(object sender, EventArgs e)
		{
			DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
			this.mainForm.AddNewSpectrum(activeDocument);
		}

		// Token: 0x06000281 RID: 641 RVA: 0x0000B0E8 File Offset: 0x000092E8
		void button2_Click(object sender, EventArgs e)
		{
			DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
			this.mainForm.DeleteActiveSpectrum(activeDocument);
		}

		// Token: 0x06000282 RID: 642 RVA: 0x0000B114 File Offset: 0x00009314
		void button3_Click(object sender, EventArgs e)
		{
			DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
			this.mainForm.LoadSpectrumFromFile(activeDocument);
		}

		// Token: 0x06000283 RID: 643 RVA: 0x0000B140 File Offset: 0x00009340
		void table1_CellCheckChanged(object sender, CellCheckBoxEventArgs e)
		{
			Row row = e.Cell.Row;
			ResultData resultData = (ResultData)row.Tag;
			resultData.Visible = e.Cell.Checked;
			DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
			activeDocument.UpdateEnergySpectrum();
			activeDocument.Dirty = true;
		}

		// Token: 0x06000284 RID: 644 RVA: 0x0000B194 File Offset: 0x00009394
		void button5_Click(object sender, EventArgs e)
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

		// Token: 0x06000285 RID: 645 RVA: 0x0000B214 File Offset: 0x00009414
		void button6_Click(object sender, EventArgs e)
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

		// Token: 0x06000286 RID: 646 RVA: 0x0000B2A4 File Offset: 0x000094A4
		void button4_Click(object sender, EventArgs e)
		{
			DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
			this.mainForm.SaveSpectrumToFile(activeDocument);
		}

		// Token: 0x06000287 RID: 647 RVA: 0x0000B2D0 File Offset: 0x000094D0
		void table1_EditingStopped(object sender, CellEditEventArgs e)
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

		// Token: 0x040000CA RID: 202
		MainForm mainForm;

		// Token: 0x040000CB RID: 203
		DocumentManager documentManager = DocumentManager.GetInstance();

		// Token: 0x040000CC RID: 204
		GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();
	}
}
