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
            this.textBoxName.Text = sampleInfo.Name;
            this.textBoxLocation.Text = sampleInfo.Location;
            this.dateTimePickerSampleTime.Value = sampleInfo.Time;
            this.dateTimePicker2.Value = activeResultData.StartTime;
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.WeightUnit == WeightUnit.Kilogram)
            {
                this.label5.Text = "kg";
                this.numericUpDownWeight.Minimum = 0.001m;
                this.numericUpDownWeight.Maximum = 100m;
                this.numericUpDownWeight.Increment = 0.1m;
                this.numericUpDownWeight.Value = (decimal)sampleInfo.Weight;
            }
            else
            {
                this.label5.Text = "g";
                this.numericUpDownWeight.Minimum = 0.001m;
                this.numericUpDownWeight.Maximum = 100000m;
                this.numericUpDownWeight.Increment = 100m;
                this.numericUpDownWeight.Value = (decimal)sampleInfo.Weight * 1000m;
            }
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.VolumeUnit == VolumeUnit.Liter)
            {
                this.label6.Text = "l";
                this.numericUpDownWeight.Minimum = 0.001m;
                this.numericUpDownVolume.Maximum = 100m;
                this.numericUpDownVolume.Increment = 0.1m;
                this.numericUpDownVolume.Value = (decimal)sampleInfo.Volume;
            }
            else
            {
                this.label6.Text = "ml";
                this.numericUpDownWeight.Minimum = 0.001m;
                this.numericUpDownVolume.Maximum = 100000m;
                this.numericUpDownVolume.Increment = 100m;
                this.numericUpDownVolume.Value = (decimal)sampleInfo.Volume * 1000m;
            }
            this.textBoxNote.Text = sampleInfo.Note;
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
            activeResultData.SampleInfo.Name = this.textBoxName.Text;
            activeResultData.SampleInfo.Location = this.textBoxLocation.Text;
            activeResultData.SampleInfo.Time = this.dateTimePickerSampleTime.Value;
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.WeightUnit == WeightUnit.Kilogram)
            {
                activeResultData.SampleInfo.Weight = (double)this.numericUpDownWeight.Value;
            }
            else
            {
                activeResultData.SampleInfo.Weight = (double)(this.numericUpDownWeight.Value / 1000m);
            }
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.VolumeUnit == VolumeUnit.Liter)
            {
                activeResultData.SampleInfo.Volume = (double)this.numericUpDownVolume.Value;
            }
            else
            {
                activeResultData.SampleInfo.Volume = (double)(this.numericUpDownVolume.Value / 1000m);
            }
            activeResultData.SampleInfo.Note = this.textBoxNote.Text;
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

        // Token: 0x06000948 RID: 2376 RVA: 0x00036588 File Offset: 0x00034788
        void textBoxNote_TextChanged(object sender, EventArgs e)
        {
            this.UpdateNoteValue();
            this.SetActiveDocumentDirty();
        }

        // Token: 0x06000949 RID: 2377 RVA: 0x00036590 File Offset: 0x00034790
        void textBoxName_Changed(object sender, EventArgs e)
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
            activeResultData.SampleInfo.Name = this.textBoxName.Text;

            this.SetActiveDocumentDirty();
            this.mainForm.UpdateSpectrumListView();
        }

        // Token: 0x0600094A RID: 2378 RVA: 0x000365E8 File Offset: 0x000347E8
        void textBoxLocation_Changed(object sender, EventArgs e)
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
            activeResultData.SampleInfo.Location = this.textBoxLocation.Text;

            this.SetActiveDocumentDirty();
        }

        // Token: 0x0600094C RID: 2380 RVA: 0x0003668C File Offset: 0x0003488C
        void dateTimePickerSampleTime_ValueChanged(object sender, EventArgs e)
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
            activeResultData.SampleInfo.Time = this.dateTimePickerSampleTime.Value;

            this.UpdateMeasurementResult();
            this.SetActiveDocumentDirty();
        }

        // Token: 0x0600094D RID: 2381 RVA: 0x00036690 File Offset: 0x00034890
        void numericUpDownWeight_ValueChanged(object sender, EventArgs e)
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
                activeResultData.SampleInfo.Weight = (double)this.numericUpDownWeight.Value;
            }
            else
            {
                activeResultData.SampleInfo.Weight = (double)(this.numericUpDownWeight.Value / 1000m);
            }

            this.UpdateMeasurementResult();
            this.SetActiveDocumentDirty();
        }

        // Token: 0x0600094E RID: 2382 RVA: 0x0003673C File Offset: 0x0003493C
        void numericUpDownVolume_ValueChanged(object sender, EventArgs e)
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
                activeResultData.SampleInfo.Volume = (double)this.numericUpDownVolume.Value;
            }
            else
            {
                activeResultData.SampleInfo.Volume = (double)(this.numericUpDownVolume.Value / 1000m);
            }

            this.UpdateMeasurementResult();
            this.SetActiveDocumentDirty();
        }

        void UpdateNoteValue()
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
            activeResultData.SampleInfo.Note = this.textBoxNote.Text;
        }

        // Token: 0x06000951 RID: 2385 RVA: 0x00036840 File Offset: 0x00034A40
        void UpdateMeasurementResult()
        {
            this.mainForm.ShowMeasurementResult(false);
        }

        // Token: 0x0400052A RID: 1322
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x0400052B RID: 1323
        MainForm mainForm;

        // Token: 0x0400052C RID: 1324
        bool contentsLoading;
    }
}
