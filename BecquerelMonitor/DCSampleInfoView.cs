using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x020000C0 RID: 192
    public partial class DCSampleInfoView : ToolWindow
    {
        // Token: 0x06000941 RID: 2369 RVA: 0x00036158 File Offset: 0x00034358
        public DCSampleInfoView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();
        }

        // Token: 0x06000942 RID: 2370 RVA: 0x00036178 File Offset: 0x00034378
        public void LoadFormContents()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            this.contentsLoading = true;
            ResultData activeResultData = activeDocument.ActiveResultData;
            SampleInfoData sampleInfo = activeResultData.SampleInfo;
            this.textBox2.Text = sampleInfo.Name;
            this.textBox3.Text = sampleInfo.Location;
            this.dateTimePicker1.Value = sampleInfo.Time;
            this.dateTimePicker2.Value = activeResultData.StartTime;
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.WeightUnit == WeightUnit.Kilogram)
            {
                this.label5.Text = "kg";
                this.numericUpDown1.Maximum = 100m;
                this.numericUpDown1.Increment = 0.1m;
                this.numericUpDown1.DecimalPlaces = 3;
                this.numericUpDown1.Value = (decimal)sampleInfo.Weight;
            }
            else
            {
                this.label5.Text = "g";
                this.numericUpDown1.Minimum = 0.01m;
                this.numericUpDown1.Maximum = 100000m;
                this.numericUpDown1.Increment = 100m;
                this.numericUpDown1.DecimalPlaces = 2;
                this.numericUpDown1.Value = (decimal)sampleInfo.Weight * 1000m;
            }
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.VolumeUnit == VolumeUnit.Liter)
            {
                this.label6.Text = "l";
                this.numericUpDown2.Maximum = 100m;
                this.numericUpDown2.Increment = 0.1m;
                this.numericUpDown2.DecimalPlaces = 3;
                this.numericUpDown2.Value = (decimal)sampleInfo.Volume;
            }
            else
            {
                this.label6.Text = "ml";
                this.numericUpDown2.Maximum = 100000m;
                this.numericUpDown2.Increment = 100m;
                this.numericUpDown2.DecimalPlaces = 0;
                this.numericUpDown2.Value = (decimal)sampleInfo.Volume * 1000m;
            }
            this.textBox1.Text = sampleInfo.Note;
            this.contentsLoading = false;
        }

        // Token: 0x06000943 RID: 2371 RVA: 0x000363D8 File Offset: 0x000345D8
        public void SaveFormContents()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.SampleInfo.Name = this.textBox2.Text;
            activeResultData.SampleInfo.Location = this.textBox3.Text;
            activeResultData.SampleInfo.Time = this.dateTimePicker1.Value;
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.WeightUnit == WeightUnit.Kilogram)
            {
                activeResultData.SampleInfo.Weight = (double)this.numericUpDown1.Value;
            }
            else
            {
                activeResultData.SampleInfo.Weight = (double)(this.numericUpDown1.Value / 1000m);
            }
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.VolumeUnit == VolumeUnit.Liter)
            {
                activeResultData.SampleInfo.Volume = (double)this.numericUpDown2.Value;
            }
            else
            {
                activeResultData.SampleInfo.Volume = (double)(this.numericUpDown2.Value / 1000m);
            }
            activeResultData.SampleInfo.Note = this.textBox1.Text;
        }

        // Token: 0x06000944 RID: 2372 RVA: 0x0003652C File Offset: 0x0003472C
        void SetActiveDocumentDirty()
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            activeDocument.Dirty = true;
            activeDocument.ActiveResultData.Dirty = true;
        }

        // Token: 0x06000945 RID: 2373 RVA: 0x00036570 File Offset: 0x00034770
        void textBox2_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDocumentDirty();
        }

        // Token: 0x06000946 RID: 2374 RVA: 0x00036578 File Offset: 0x00034778
        void textBox3_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDocumentDirty();
        }

        // Token: 0x06000947 RID: 2375 RVA: 0x00036580 File Offset: 0x00034780
        void dateTimePicker1_ValueChanged(object sender, EventArgs e)
        {
            this.SetActiveDocumentDirty();
        }

        // Token: 0x06000948 RID: 2376 RVA: 0x00036588 File Offset: 0x00034788
        void textBox1_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveDocumentDirty();
        }

        // Token: 0x06000949 RID: 2377 RVA: 0x00036590 File Offset: 0x00034790
        void textBox2_Validated(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.SampleInfo.Name = this.textBox2.Text;
            this.mainForm.UpdateSpectrumListView();
        }

        // Token: 0x0600094A RID: 2378 RVA: 0x000365E8 File Offset: 0x000347E8
        void textBox3_Validated(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.SampleInfo.Location = this.textBox3.Text;
        }

        // Token: 0x0600094B RID: 2379 RVA: 0x00036638 File Offset: 0x00034838
        void dateTimePicker1_Validated(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.SampleInfo.Time = this.dateTimePicker1.Value;
            this.UpdateMeasurementResult();
        }

        // Token: 0x0600094C RID: 2380 RVA: 0x0003668C File Offset: 0x0003488C
        void dateTimePicker2_ValueChanged(object sender, EventArgs e)
        {
        }

        // Token: 0x0600094D RID: 2381 RVA: 0x00036690 File Offset: 0x00034890
        void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.WeightUnit == WeightUnit.Kilogram)
            {
                activeResultData.SampleInfo.Weight = (double)this.numericUpDown1.Value;
            }
            else
            {
                activeResultData.SampleInfo.Weight = (double)(this.numericUpDown1.Value / 1000m);
            }
            this.UpdateMeasurementResult();
            this.SetActiveDocumentDirty();
        }

        // Token: 0x0600094E RID: 2382 RVA: 0x0003673C File Offset: 0x0003493C
        void numericUpDown2_ValueChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.VolumeUnit == VolumeUnit.Liter)
            {
                activeResultData.SampleInfo.Volume = (double)this.numericUpDown2.Value;
            }
            else
            {
                activeResultData.SampleInfo.Volume = (double)(this.numericUpDown2.Value / 1000m);
            }
            this.UpdateMeasurementResult();
            this.SetActiveDocumentDirty();
        }

        // Token: 0x0600094F RID: 2383 RVA: 0x000367E8 File Offset: 0x000349E8
        void textBox1_Validated(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.SampleInfo.Note = this.textBox1.Text;
        }

        // Token: 0x06000950 RID: 2384 RVA: 0x0003683C File Offset: 0x00034A3C
        void DCSampleInfoView_FormClosed(object sender, FormClosedEventArgs e)
        {
        }

        // Token: 0x06000951 RID: 2385 RVA: 0x00036840 File Offset: 0x00034A40
        void UpdateMeasurementResult()
        {
            this.mainForm.ShowMeasurementResult(false);
        }

        // Token: 0x06000952 RID: 2386 RVA: 0x00036850 File Offset: 0x00034A50
        void textBox2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                if (activeDocument == null)
                {
                    return;
                }
                ResultData activeResultData = activeDocument.ActiveResultData;
                activeResultData.SampleInfo.Name = this.textBox2.Text;
                this.mainForm.UpdateSpectrumListView();
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x06000953 RID: 2387 RVA: 0x000368B0 File Offset: 0x00034AB0
        void textBox3_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                if (activeDocument == null)
                {
                    return;
                }
                ResultData activeResultData = activeDocument.ActiveResultData;
                activeResultData.SampleInfo.Location = this.textBox3.Text;
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x06000954 RID: 2388 RVA: 0x00036908 File Offset: 0x00034B08
        void numericUpDown1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x06000955 RID: 2389 RVA: 0x00036920 File Offset: 0x00034B20
        void numericUpDown2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                e.SuppressKeyPress = true;
            }
        }

        // Token: 0x0400052A RID: 1322
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x0400052B RID: 1323
        MainForm mainForm;

        // Token: 0x0400052C RID: 1324
        bool contentsLoading;
    }
}
