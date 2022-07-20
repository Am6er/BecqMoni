using System;
using System.Collections.Generic;
using XPTable.Models;

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
            this.FormLoading = true;
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            ResultData activeResultData = activeDocument.ActiveResultData;
            DeviceConfigInfo deviceConfigInfo = activeResultData.DeviceConfig;
            if (deviceConfigInfo.Guid == null)
            {
                List<DeviceConfigInfo> deviceConfigInfos = DeviceConfigManager.GetInstance().DeviceConfigList;
                DateTime maxTime = new DateTime();
                DeviceConfigInfo lastConfigInfo = null;
                foreach (DeviceConfigInfo devinfo in deviceConfigInfos)
                {
                    if (lastConfigInfo == null)
                    {
                        lastConfigInfo = devinfo;
                        maxTime = devinfo.LastUpdated;
                    } else
                    {
                        if (devinfo.LastUpdated > maxTime)
                        {
                            lastConfigInfo=devinfo;
                            maxTime=devinfo.LastUpdated;
                        }
                    }
                }
                deviceConfigInfo = lastConfigInfo;
                activeResultData.DeviceConfig.PeakDetectionMethodConfig = deviceConfigInfo.PeakDetectionMethodConfig;
            }
            activeResultData.PeakDetectionMethodConfig = deviceConfigInfo.PeakDetectionMethodConfig;
            FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
            this.numericUpDown1.Minimum = 1;
            this.numericUpDown1.Maximum = 10000;
            this.numericUpDown1.Increment = 1;
            this.numericUpDown1.Value = (decimal)fwhmPeakDetectionMethodConfig.Min_SNR;

            this.numericUpDown2.Minimum = 1;
            this.numericUpDown2.Maximum = 1000;
            this.numericUpDown2.Increment = 1;
            this.numericUpDown2.Value = fwhmPeakDetectionMethodConfig.Max_Items;

            this.numericUpDown3.Minimum = 0;
            this.numericUpDown3.Maximum = 100;
            this.numericUpDown3.Increment = 1;
            this.numericUpDown3.Value = (decimal)fwhmPeakDetectionMethodConfig.Tolerance;

            this.FormLoading = false;
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
            if (Correct_min_snr == true)
            {
                this.numericUpDown1.Value = (decimal)Min_snr_value;
                this.numericUpDown1.ForeColor = System.Drawing.Color.Red;
                Correct_min_snr = false;
                Min_snr_value = 0.0;
            }
            foreach (Peak peak in list)
            {
                Row row = new Row();
                string text = "(Unknown)";
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
                row.Cells.Add(new Cell(peak.SNR.ToString()));
                row.Cells.Add(new Cell(peak.FWHM.ToString()));
                this.tableModel1.Rows.Add(row);
            }
            this.table1.AutoResizeColumnWidths();
        }

        // Token: 0x06000440 RID: 1088 RVA: 0x00014468 File Offset: 0x00012668
        void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            if (this.FormLoading == false)
            {
                if (!Correct_min_snr)
                {
                    this.numericUpDown1.ForeColor = System.Drawing.Color.Black;
                }
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                ResultData activeResultData = activeDocument.ActiveResultData;
                FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
                fwhmPeakDetectionMethodConfig.Min_SNR = (double)((int)this.numericUpDown1.Value);
                this.UpdatePeakDetectionResult();
                activeDocument.EnergySpectrumView.Invalidate();
            }
        }

        // Token: 0x06000441 RID: 1089 RVA: 0x00014500 File Offset: 0x00012700
        void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            if (this.FormLoading == false)
            {
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                ResultData activeResultData = activeDocument.ActiveResultData;
                FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
                fwhmPeakDetectionMethodConfig.Max_Items = (int)this.numericUpDown2.Value;
                this.UpdatePeakDetectionResult();
                activeDocument.EnergySpectrumView.Invalidate();
            }
        }

        // Token: 0x06000442 RID: 1090 RVA: 0x000145BC File Offset: 0x000127BC
        void numericUpDown3_ValueChanged(object sender, EventArgs e)
        {
            if (this.FormLoading == false)
            {
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                ResultData activeResultData = activeDocument.ActiveResultData;
                FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
                fwhmPeakDetectionMethodConfig.Tolerance = (double)((int)this.numericUpDown3.Value);
                this.UpdatePeakDetectionResult();
                activeDocument.EnergySpectrumView.Invalidate();
            }
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

        bool FormLoading = false;

        public static bool Correct_min_snr = false;
        public static double Min_snr_value = 0.0;
    }
}
