using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using XPTable.Models;
using XPTable.Renderers;

namespace BecquerelMonitor
{
	// Token: 0x0200004F RID: 79
	public partial class DCPeakDetectionView : ToolWindow
	{
		// Token: 0x0600043D RID: 1085 RVA: 0x00014210 File Offset: 0x00012410
		public DCPeakDetectionView(MainForm mainForm)
		{
			this.mainForm = mainForm;
			this.InitializeComponent();
		}

		// Token: 0x0600043E RID: 1086 RVA: 0x0001423C File Offset: 0x0001243C
		public void ShowPeakDetectionResult()
		{
			DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
			ResultData activeResultData = activeDocument.ActiveResultData;
			SimplePeakDetectionMethodConfig simplePeakDetectionMethodConfig = (SimplePeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
			this.numericUpDown1.Value = simplePeakDetectionMethodConfig.PolynomialOrder;
			this.numericUpDown2.Value = simplePeakDetectionMethodConfig.WindowSize;
			this.numericUpDown3.Value = (decimal)simplePeakDetectionMethodConfig.Threshold;
			this.UpdatePeakDetectionResult();
		}

		// Token: 0x0600043F RID: 1087 RVA: 0x000142B8 File Offset: 0x000124B8
		public void UpdatePeakDetectionResult()
		{
			DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
			ResultData activeResultData = activeDocument.ActiveResultData;
			PeakDetector peakDetector = new PeakDetector();
			List<Peak> list = peakDetector.DetectPeak(activeResultData);
			this.tableModel1.Rows.Clear();
			foreach (Peak peak in list)
			{
				Row row = new Row();
				string text = "(不明)";
				string text2 = "－";
				if (peak.Nuclide != null)
				{
					text = peak.Nuclide.Name;
					double num = peak.Nuclide.Energy - peak.Energy;
					double num2 = (peak.Nuclide.Energy - peak.Energy) / peak.Nuclide.Energy * 100.0;
					text2 = num.ToString("f2") + " (" + num2.ToString("f2") + "％)";
				}
				row.Cells.Add(new Cell(text));
				row.Cells.Add(new Cell(peak.Energy.ToString("f2")));
				row.Cells.Add(new Cell(text2));
				row.Cells.Add(new Cell(peak.Channel.ToString()));
				this.tableModel1.Rows.Add(row);
			}
		}

		// Token: 0x06000440 RID: 1088 RVA: 0x00014468 File Offset: 0x00012668
		void numericUpDown1_ValueChanged(object sender, EventArgs e)
		{
			DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
			ResultData activeResultData = activeDocument.ActiveResultData;
			SimplePeakDetectionMethodConfig simplePeakDetectionMethodConfig = (SimplePeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
			simplePeakDetectionMethodConfig.PolynomialOrder = (int)this.numericUpDown1.Value;
			if (simplePeakDetectionMethodConfig.PolynomialOrder >= simplePeakDetectionMethodConfig.WindowSize)
			{
				int num = simplePeakDetectionMethodConfig.PolynomialOrder + 1;
				if (num % 2 == 0)
				{
					num++;
				}
				this.numericUpDown2.Value = num;
				simplePeakDetectionMethodConfig.WindowSize = num;
			}
			this.UpdatePeakDetectionResult();
			activeDocument.EnergySpectrumView.Invalidate();
		}

		// Token: 0x06000441 RID: 1089 RVA: 0x00014500 File Offset: 0x00012700
		void numericUpDown2_ValueChanged(object sender, EventArgs e)
		{
			DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
			ResultData activeResultData = activeDocument.ActiveResultData;
			SimplePeakDetectionMethodConfig simplePeakDetectionMethodConfig = (SimplePeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
			int num = (int)this.numericUpDown2.Value;
			if (num == simplePeakDetectionMethodConfig.WindowSize)
			{
				return;
			}
			if (num <= simplePeakDetectionMethodConfig.PolynomialOrder)
			{
				num = simplePeakDetectionMethodConfig.PolynomialOrder + 1;
				if (num % 2 == 0)
				{
					num++;
				}
				this.numericUpDown2.Value = num;
			}
			else if (num % 2 == 0)
			{
				num++;
				this.numericUpDown2.Value = num;
			}
			simplePeakDetectionMethodConfig.WindowSize = num;
			this.UpdatePeakDetectionResult();
			activeDocument.EnergySpectrumView.Invalidate();
		}

		// Token: 0x06000442 RID: 1090 RVA: 0x000145BC File Offset: 0x000127BC
		void numericUpDown3_ValueChanged(object sender, EventArgs e)
		{
			DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
			ResultData activeResultData = activeDocument.ActiveResultData;
			SimplePeakDetectionMethodConfig simplePeakDetectionMethodConfig = (SimplePeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
			simplePeakDetectionMethodConfig.Threshold = (double)((int)this.numericUpDown3.Value);
			this.UpdatePeakDetectionResult();
			activeDocument.EnergySpectrumView.Invalidate();
		}

		// Token: 0x06000443 RID: 1091 RVA: 0x00014614 File Offset: 0x00012814
		void button1_Click(object sender, EventArgs e)
		{
			this.mainForm.ShowNuclideDefinitionForm();
		}

		// Token: 0x040001B3 RID: 435
		MainForm mainForm;

		// Token: 0x040001B4 RID: 436
		DocumentManager documentManager = DocumentManager.GetInstance();

		// Token: 0x040001B5 RID: 437
		GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();
	}
}
