using BecquerelMonitor.Properties;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x02000030 RID: 48
    public partial class DCControlPanel : ToolWindow
    {
        // Token: 0x17000120 RID: 288
        // (get) Token: 0x06000288 RID: 648 RVA: 0x0000B384 File Offset: 0x00009584
        // (set) Token: 0x06000289 RID: 649 RVA: 0x0000B3B0 File Offset: 0x000095B0
        public int PresetTime
        {
            get
            {
                int result;
                if (!int.TryParse(this.realTimeLimitTextBox.Text, out result))
                {
                    return -1;
                }
                return result;
            }
            set
            {
                this.realTimeLimitTextBox.Text = value.ToString();
            }
        }

        // Token: 0x0600028A RID: 650 RVA: 0x0000B3C4 File Offset: 0x000095C4
        public DCControlPanel(MainForm mainForm)
        {
            this.InitializeComponent();
            this.mainForm = mainForm;
            this.startBtn.Enabled = true;
            this.stopBtn.Enabled = false;
            this.reloadFromConfigBtn.Enabled = true;
            this.attachBtn.Text = Resources.Attach_BTN;
            this.attachBtn.Visible = false;
            this.UpdateDeviceConfigList();
            this.UpdateROIConfigList();
            this.deviceConfigManager.DeviceConfigListChanged += this.manager_DeviceConfigChanged;
            this.roiConfigManager.ROIConfigListChanged += this.manager_ROIConfigListChanged;
        }

        // Token: 0x0600028B RID: 651 RVA: 0x0000B468 File Offset: 0x00009668
        void manager_DeviceConfigChanged(object sender, DeviceConfigChangedEventArgs e)
        {
            if (this.mainForm.ActiveDocument == null)
            {
                return;
            }
            this.UpdateDeviceConfigList();
            this.ShowDocumentStatus();
        }

        // Token: 0x0600028C RID: 652 RVA: 0x0000B498 File Offset: 0x00009698
        void manager_ROIConfigListChanged(object sender, EventArgs e)
        {
            if (this.mainForm.ActiveDocument == null)
            {
                return;
            }
            this.UpdateROIConfigList();
            this.ShowDocumentStatus();
            this.mainForm.ShowMeasurementResult(true);
        }

        // Token: 0x0600028D RID: 653 RVA: 0x0000B4D4 File Offset: 0x000096D4
        void UpdateDeviceConfigList()
        {
            this.devConfigComboBox.Items.Clear();
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            for (int i = 0; i < this.deviceConfigManager.DeviceConfigList.Count; i++)
            {
                DeviceConfigInfo deviceConfigInfo = this.deviceConfigManager.DeviceConfigList[i];
                this.devConfigComboBox.Items.Add(deviceConfigInfo.Name);
            }
        }

        // Token: 0x0600028E RID: 654 RVA: 0x0000B548 File Offset: 0x00009748
        public void UpdateROIConfigList()
        {
            this.roiConfigComboBox.Items.Clear();
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            for (int i = 0; i < this.roiConfigManager.ROIConfigList.Count; i++)
            {
                ROIConfigData roiconfigData = this.roiConfigManager.ROIConfigList[i];
                this.roiConfigComboBox.Items.Add(roiconfigData.Name);
            }
        }

        // Token: 0x0600028F RID: 655 RVA: 0x0000B5BC File Offset: 0x000097BC
        void button1_Click(object sender, EventArgs e)
        {
            this.StartMeasurement();
        }

        void button11_Click(object sender, EventArgs e)
        {
            if (this.attachBtn.Text == Resources.Attach_BTN)
            {
                bool result = this.AttachToDevice();
                if (!result) return;
                this.attachBtn.Text = Resources.Detach_BTN;
            } else
            {
                this.DetachFromDevice();
                this.attachBtn.Text = Resources.Attach_BTN;
            }
            
        }

        // Token: 0x06000290 RID: 656 RVA: 0x0000B5C4 File Offset: 0x000097C4
        public void StartMeasurement()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            if (this.PresetTime < 0)
            {
                MessageBox.Show(Resources.ERRInvalidPresetTime);
                return;
            }
            resultDataStatus.PresetTime = this.PresetTime;
            if ((int)resultDataStatus.ElapsedTime.TotalSeconds >= this.PresetTime)
            {
                return;
            }
            activeDocument.Dirty = true;
            activeDocument.UpdateSpectrum = true;
            activeResultData.Dirty = true;
            this.mainForm.setAutosaveToDocument(activeDocument);
            DCPulseView dcpulseView = this.mainForm.DCPulseView;
            activeResultData.MeasurementController.StartRecording();
            if (activeDocument.PulseDetector != null)
            {
                activeDocument.PulseDetector.PulseView = dcpulseView.PulseView;
                activeDocument.PulseDetector.NGPulseView = dcpulseView.NGPulseView;
                activeDocument.PulseDetector.DoUpdatePulseView = this.mainForm.DoUpdatePulseView;
            }
            this.mainForm.UpdateSampleInfo();
            this.ShowDocumentStatus();
        }

        public bool AttachToDevice()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return false;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            if (this.PresetTime < 0)
            {
                MessageBox.Show(Resources.ERRInvalidPresetTime);
                return false;
            }
            resultDataStatus.PresetTime = this.PresetTime;
            if ((int)resultDataStatus.ElapsedTime.TotalSeconds >= this.PresetTime)
            {
                return false;
            }
            activeDocument.Dirty = true;
            activeDocument.UpdateSpectrum = true;
            activeResultData.Dirty = true;
            this.mainForm.setAutosaveToDocument(activeDocument);
            DCPulseView dcpulseView = this.mainForm.DCPulseView;
            bool result = activeResultData.MeasurementController.AttachToDevice();
            if (activeDocument.PulseDetector != null)
            {
                activeDocument.PulseDetector.PulseView = dcpulseView.PulseView;
                activeDocument.PulseDetector.NGPulseView = dcpulseView.NGPulseView;
                activeDocument.PulseDetector.DoUpdatePulseView = this.mainForm.DoUpdatePulseView;
            }
            this.mainForm.UpdateSampleInfo();
            this.ShowDocumentStatus();
            return result;
        }

        // Token: 0x06000291 RID: 657 RVA: 0x0000B6BC File Offset: 0x000098BC
        void button2_Click(object sender, EventArgs e)
        {
            this.StopMeasurement();
        }

        // Token: 0x06000292 RID: 658 RVA: 0x0000B6C4 File Offset: 0x000098C4
        public void StopMeasurement()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            if (!activeResultData.ResultDataStatus.Recording)
            {
                return;
            }
            activeDocument.UpdateSpectrum = false;
            activeResultData.MeasurementController.StopRecording();
            if (activeDocument.PulseDetector != null)
            {
                activeDocument.PulseDetector.DoUpdatePulseView = false;
            }
            this.ShowDocumentStatus();
        }

        public void DetachFromDevice()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            if (!activeResultData.ResultDataStatus.Recording)
            {
                return;
            }
            activeDocument.UpdateSpectrum = false;
            activeResultData.MeasurementController.DetachFromDevice();
            if (activeDocument.PulseDetector != null)
            {
                activeDocument.PulseDetector.DoUpdatePulseView = false;
            }
            this.ShowDocumentStatus();
        }

        // Token: 0x06000293 RID: 659 RVA: 0x0000B730 File Offset: 0x00009930
        void button7_Click(object sender, EventArgs e)
        {
            this.ClearMeasurementResult();
        }

        // Token: 0x06000294 RID: 660 RVA: 0x0000B738 File Offset: 0x00009938
        public void ClearMeasurementResult()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            DialogResult dialogResult = MessageBox.Show(Resources.MSGClearSpectrum, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
            if (dialogResult == DialogResult.No)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.MeasurementController.ClearMeasurementResult();
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            resultDataStatus.TotalTime = TimeSpan.FromSeconds(0.0);
            resultDataStatus.ElapsedTime = TimeSpan.FromSeconds(0.0);
            activeDocument.ClearSpectrum();
            activeDocument.Dirty = true;
            this.ShowDocumentStatus();
        }

        // Token: 0x06000295 RID: 661 RVA: 0x0000B7CC File Offset: 0x000099CC
        public void ShowDocumentStatus()
        {
            UpdateDeviceConfigList();
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            this.formUpdating = true;
            this.spectrumNameLbl.Text = Path.GetFileName(activeDocument.Filename);
            this.saveWhenFinishedCheckBox.Checked = activeDocument.ActiveResultData.MeasurementController.SaveOnMeasurementEnd;
            bool flag = false;
            for (int i = 0; i < this.deviceConfigManager.DeviceConfigList.Count; i++)
            {
                DeviceConfigInfo deviceConfigInfo = this.deviceConfigManager.DeviceConfigList[i];
                if (activeResultData.DeviceConfig.Guid == deviceConfigInfo.Guid)
                {
                    this.devConfigComboBox.SelectedIndex = i;
                    flag = true;
                    break;
                }
            }
            if (!flag)
            {
                this.devConfigComboBox.SelectedIndex = -1;
            }
            flag = false;
            for (int j = 0; j < this.roiConfigManager.ROIConfigList.Count; j++)
            {
                ROIConfigData roiconfigData = this.roiConfigManager.ROIConfigList[j];
                if (activeResultData.ROIConfig != null && activeResultData.ROIConfig.Guid == roiconfigData.Guid)
                {
                    this.roiConfigComboBox.SelectedIndex = j;
                    flag = true;
                    break;
                }
            }
            if (!flag)
            {
                this.roiConfigComboBox.SelectedIndex = -1;
            }
            this.textBox1.Text = Path.GetFileName(activeResultData.BackgroundSpectrumFile);
            this.realTimeLimitTextBox.Text = resultDataStatus.PresetTime.ToString();
            if (resultDataStatus.Recording)
            {
                this.startBtn.Enabled = false;
                this.stopBtn.Enabled = true;
                this.devConfigComboBox.Enabled = false;
                this.reloadFromConfigBtn.Enabled = false;
                this.attachBtn.Text = Resources.Detach_BTN;
            }
            else
            {
                this.startBtn.Enabled = true;
                this.stopBtn.Enabled = false;
                this.devConfigComboBox.Enabled = true;
                this.reloadFromConfigBtn.Enabled = true;
                this.attachBtn.Text = Resources.Attach_BTN;
            }
            this.ShowRecordingStatus();
            if (this.mainForm.ActiveDocument.ActiveResultData.DeviceConfig.DeviceType == "AtomSpectraVCP")
            {
                this.attachBtn.Visible = true;
            } else
            {
                this.attachBtn.Visible = false;
            }
            this.formUpdating = false;
        }

        // Token: 0x06000296 RID: 662 RVA: 0x0000B9E4 File Offset: 0x00009BE4
        public void ShowRecordingStatus()
        {
            bool flag = DCControlPanel.exept_flag;
            if (flag)
            {
                DCControlPanel.exept_flag = false;
                this.StopMeasurement();
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            this.ShowMeasurementProgressBar();
            double totalSeconds = resultDataStatus.ElapsedTime.TotalSeconds;
            this.totalCntTextBox.Text = activeResultData.EnergySpectrum.TotalPulseCount.ToString();
            this.validCntTextBox.Text = activeResultData.EnergySpectrum.ValidPulseCount.ToString();
            int num = activeResultData.EnergySpectrum.TotalPulseCount - activeResultData.EnergySpectrum.ValidPulseCount;
            this.invalidCountsTextBox.Text = num.ToString(); //invalid pulses
            double num2 = 0.0;
            if (totalSeconds != 0.0)
            {
                /*
                if (activeResultData.MeasurementController.DeviceController is AtomSpectraDeviceController)
                {
                    num2 = ((AtomSpectraDeviceController)activeResultData.MeasurementController.DeviceController).getCPS();
                }
                else*/
                {
                    num2 = (double)activeResultData.EnergySpectrum.ValidPulseCount / totalSeconds;
                    //num2 = (double)(activeResultData.EnergySpectrum.TotalPulseCount) / totalSeconds;
                }
            }
            this.countRateTextBox.Text = num2.ToString("f2");
        }

        // Token: 0x06000297 RID: 663 RVA: 0x0000BAD8 File Offset: 0x00009CD8
        void ShowMeasurementProgressBar()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            ResultData activeResultData = activeDocument.ActiveResultData;
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            double totalSeconds = resultDataStatus.ElapsedTime.TotalSeconds;
            double num = totalSeconds / (double)resultDataStatus.PresetTime * 100.0;
            if (num < 0.0)
            {
                num = 0.0;
            }
            if (num > 100.0)
            {
                num = 100.0;
            }
            this.percentageProgressBar1.DoubleValue = num;
            this.percentageProgressBar1.PriorText = ((int)totalSeconds).ToString();
            this.percentageProgressBar1.Invalidate();
        }

        void percentageProgressBar1_Click(object sender, EventArgs e)
        {
            this.percentageProgressBar1.DisplayFormatCycle(e);
        }

        // Token: 0x06000298 RID: 664 RVA: 0x0000BB90 File Offset: 0x00009D90
        void DCControlPanel_Load(object sender, EventArgs e)
        {
        }

        // Token: 0x06000299 RID: 665 RVA: 0x0000BB94 File Offset: 0x00009D94
        void button10_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            ResultData activeResultData = activeDocument.ActiveResultData;
            DeviceConfigInfo config = null;
            if (activeDocument != null)
            {
                config = activeResultData.DeviceConfig;
            }
            this.mainForm.ShowDeviceConfigForm(config);
        }

        // Token: 0x0600029A RID: 666 RVA: 0x0000BBD4 File Offset: 0x00009DD4
        void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.formUpdating)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null || this.devConfigComboBox.SelectedIndex == -1)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            DeviceConfigInfo deviceConfig = activeResultData.DeviceConfig;
            activeResultData.DeviceConfig = this.deviceConfigManager.DeviceConfigList[this.devConfigComboBox.SelectedIndex];
            activeResultData.DeviceConfigReference = activeResultData.DeviceConfig.CreateReference();
            if (deviceConfig.Guid != activeResultData.DeviceConfig.Guid)
            {
                if (!this.ApplyDeviceConfigChange())
                {
                    activeResultData.DeviceConfig = deviceConfig;
                    activeResultData.DeviceConfigReference = activeResultData.DeviceConfig.CreateReference();
                    this.ShowDocumentStatus();
                }
                this.mainForm.ShowMeasurementResult(true);
            }
        }

        // Token: 0x0600029B RID: 667 RVA: 0x0000BCA8 File Offset: 0x00009EA8
        void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.formUpdating)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null || this.roiConfigComboBox.SelectedIndex == -1)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            ROIConfigData roiconfig = activeResultData.ROIConfig;
            activeResultData.ROIConfig = this.roiConfigManager.ROIConfigList[this.roiConfigComboBox.SelectedIndex];
            if (roiconfig == null || roiconfig.Guid != activeResultData.ROIConfig.Guid)
            {
                activeDocument.Dirty = true;
                activeResultData.ROIConfigReference = activeResultData.ROIConfig.CreateReference();
                this.ShowDocumentStatus();
                activeDocument.UpdateEnergySpectrum();
                this.mainForm.ShowMeasurementResult(true);
            }
        }

        // Token: 0x0600029C RID: 668 RVA: 0x0000BD6C File Offset: 0x00009F6C
        void button3_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = Resources.BackgroundSelectionDialogTitle;
            openFileDialog.Filter = Resources.SpectrumFileFilter;
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            this.textBox1.Text = Path.GetFileName(openFileDialog.FileName);
            activeResultData.BackgroundSpectrumFile = Path.GetFileName(openFileDialog.FileName);
            activeResultData.BackgroundSpectrumPathname = openFileDialog.FileName;
            this.documentManager.LoadBackgroundSpectrum(activeResultData);
            if (activeResultData.BackgroundEnergySpectrum != null && activeResultData.EnergySpectrum.NumberOfChannels != activeResultData.BackgroundEnergySpectrum.NumberOfChannels)
            {
                MessageBox.Show(Resources.ERRIncompatibleChannelParameters, Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                activeResultData.BackgroundEnergySpectrum = null;
                activeResultData.BackgroundSpectrumFile = "";
                activeResultData.BackgroundSpectrumPathname = "";
            }
            activeDocument.Dirty = true;
            this.ShowDocumentStatus();
            this.mainForm.ShowMeasurementResult(true);
            activeDocument.UpdateEnergySpectrum();
        }

        // Token: 0x0600029D RID: 669 RVA: 0x0000BE84 File Offset: 0x0000A084
        void button8_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.BackgroundEnergySpectrum = null;
            activeResultData.BackgroundSpectrumFile = "";
            activeResultData.BackgroundSpectrumPathname = "";
            activeDocument.Dirty = true;
            this.ShowDocumentStatus();
            this.mainForm.ShowMeasurementResult(true);
            activeDocument.UpdateEnergySpectrum();
        }

        // Token: 0x0600029E RID: 670 RVA: 0x0000BEEC File Offset: 0x0000A0EC
        void button4_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            ROIConfigData config = null;
            if (activeDocument != null)
            {
                ResultData activeResultData = activeDocument.ActiveResultData;
                config = activeResultData.ROIConfig;
            }
            this.mainForm.ShowROIConfigForm(config);
        }

        // Token: 0x0600029F RID: 671 RVA: 0x0000BF2C File Offset: 0x0000A12C
        void button9_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument != null)
            {
                ResultData activeResultData = activeDocument.ActiveResultData;
                activeResultData.ROIConfig = null;
                activeResultData.ROIConfigReference = null;
                activeDocument.Dirty = true;
                this.roiConfigComboBox.SelectedIndex = -1;
                activeDocument.UpdateEnergySpectrum();
                this.mainForm.ShowMeasurementResult(true);
            }
        }

        // Token: 0x060002A0 RID: 672 RVA: 0x0000BF8C File Offset: 0x0000A18C
        void button5_Click(object sender, EventArgs e)
        {
            this.ApplyDeviceConfigChange();
            this.mainForm.ShowMeasurementResult(true);
        }

        // Token: 0x060002A1 RID: 673 RVA: 0x0000BFA4 File Offset: 0x0000A1A4
        bool ApplyDeviceConfigChange()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return true;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            if (activeResultData.DeviceConfigReference == null)
            {
                return false;
            }
            string guid = activeResultData.DeviceConfigReference.Guid;
            if (guid == null || !this.deviceConfigManager.DeviceConfigMap.ContainsKey(guid))
            {
                return false;
            }
            activeResultData.DeviceConfig = this.deviceConfigManager.DeviceConfigMap[guid];
            activeResultData.DeviceConfigReference = activeResultData.DeviceConfig.CreateReference();
            // dumb fix for incorrect channel pitch for digital MCA
            if (activeResultData.EnergySpectrum.ChannelPitch != activeResultData.DeviceConfig.ChannelPitch &&
                (activeResultData.DeviceConfig.InputDeviceConfig is RadiaCodeDeviceConfig || activeResultData.DeviceConfig.InputDeviceConfig is AtomSpectraDeviceConfig))
            {
                activeResultData.EnergySpectrum.ChannelPitch = activeResultData.DeviceConfig.ChannelPitch;
            }

            int numberOfChannels = activeResultData.EnergySpectrum.NumberOfChannels;
            double channelPitch = activeResultData.EnergySpectrum.ChannelPitch;
            if (numberOfChannels != activeResultData.DeviceConfig.NumberOfChannels || channelPitch != activeResultData.DeviceConfig.ChannelPitch)
            {
                if (activeResultData.EnergySpectrum.MeasurementTime > 0)
                {
                    DialogResult dialogResult = MessageBox.Show(Resources.MSGInitializingSpectrum,
                    Resources.ConfirmationDialogTitle,
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Exclamation);
                    if (dialogResult == DialogResult.Cancel)
                    {
                        return false;
                    }
                }
                if (activeResultData.EnergySpectrum.ValidPulseCount > 0 && activeResultData.PulseCollection.Pulses.Count == 0)
                {
                    activeResultData.EnergySpectrum.Initialize();
                    resultDataStatus.TotalTime = TimeSpan.FromSeconds(0.0);
                    resultDataStatus.ElapsedTime = TimeSpan.FromSeconds(0.0);
                }
                else
                {
                    this.RebuildSpectrum(activeResultData);
                }
            }
            activeResultData.EnergySpectrum.EnergyCalibration = activeResultData.DeviceConfig.EnergyCalibration.Clone();
            this.mainForm.UpdateEnergyCalibrationView();
            FWHMPeakDetectionMethodConfig FWHMPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)activeResultData.DeviceConfig.PeakDetectionMethodConfig;
            activeResultData.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)FWHMPeakDetectionMethodConfig.Clone();
            string backgroundSpectrumFile = activeResultData.BackgroundSpectrumFile;
            string fileName = Path.GetFileName(activeResultData.DeviceConfig.BackgroundSpectrumPathname);
            activeResultData.BackgroundSpectrumFile = fileName;
            activeResultData.BackgroundSpectrumPathname = activeResultData.DeviceConfig.BackgroundSpectrumPathname;
            activeResultData.BackgroundEnergySpectrum = null;
            this.documentManager.LoadBackgroundSpectrum(activeResultData);
            if (activeResultData.BackgroundEnergySpectrum != null && activeResultData.DeviceConfig.NumberOfChannels != activeResultData.BackgroundEnergySpectrum.NumberOfChannels)
            {
                activeResultData.BackgroundEnergySpectrum = null;
                activeResultData.BackgroundSpectrumFile = "";
                activeResultData.BackgroundSpectrumPathname = "";
            }
            this.ShowDocumentStatus();
            this.mainForm.UpdateDetectedPeakView();
            activeDocument.UpdateEnergySpectrum();
            activeDocument.Dirty = true;
            activeDocument.UpdateDoseRate = true;
            if (activeDocument.EnergySpectrumView.HorizontalMagnification == HorizontalMagnification.Fit)
            {
                activeDocument.EnergySpectrumView.FitHorizontalScale();
            }
            return true;
        }

        // Token: 0x060002A2 RID: 674 RVA: 0x0000C1E8 File Offset: 0x0000A3E8
        public void RebuildSpectrum(ResultData resultData)
        {
            EnergySpectrum energySpectrum = resultData.EnergySpectrum;
            resultData.EnergySpectrum = new EnergySpectrum(resultData.DeviceConfig.ChannelPitch, resultData.DeviceConfig.NumberOfChannels);
            resultData.EnergySpectrum.MeasurementTime = energySpectrum.MeasurementTime;
            resultData.EnergySpectrum.TotalPulseCount = energySpectrum.TotalPulseCount;
            resultData.EnergySpectrum.NumberOfSamples = energySpectrum.NumberOfSamples;
            foreach (Pulse pulse in resultData.PulseCollection.Pulses)
            {
                resultData.EnergySpectrum.Increment(pulse.Height);
            }
        }

        // Token: 0x060002A3 RID: 675 RVA: 0x0000C2AC File Offset: 0x0000A4AC
        void textBox7_Validated(object sender, EventArgs e)
        {
            this.PresetTimeChanged();
        }

        // Token: 0x060002A4 RID: 676 RVA: 0x0000C2B4 File Offset: 0x0000A4B4
        void textBox7_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Return)
            {
                this.PresetTimeChanged();
                e.SuppressKeyPress = true;
                this.realTimeLimitTextBox.SelectAll();
            }
        }

        // Token: 0x060002A5 RID: 677 RVA: 0x0000C2DC File Offset: 0x0000A4DC
        void PresetTimeChanged()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            int presetTime = 0;
            if (!int.TryParse(this.realTimeLimitTextBox.Text, out presetTime))
            {
                this.realTimeLimitTextBox.Text = "0";
            }
            resultDataStatus.PresetTime = presetTime;
            activeResultData.PresetTime = presetTime;
            this.ShowMeasurementProgressBar();
            activeDocument.Dirty = true;
        }

        // Token: 0x060002A6 RID: 678 RVA: 0x0000C354 File Offset: 0x0000A554
        void button6_Click(object sender, EventArgs e)
        {
            if (this.mainForm.ActiveDocument == null)
            {
                return;
            }
            this.mainForm.SaveDocumentWithName();
        }

        // Token: 0x060002A7 RID: 679 RVA: 0x0000C384 File Offset: 0x0000A584
        void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (this.formUpdating)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            if (this.saveWhenFinishedCheckBox.Checked && !activeDocument.IsNamed)
            {
                this.mainForm.SaveDocumentWithName();
                this.formUpdating = true;
                if (!activeDocument.IsNamed)
                {
                    this.saveWhenFinishedCheckBox.Checked = false;
                }
                else
                {
                    this.saveWhenFinishedCheckBox.Checked = true;
                }
                this.formUpdating = false;
            }
            activeDocument.ActiveResultData.MeasurementController.SaveOnMeasurementEnd = this.saveWhenFinishedCheckBox.Checked;
        }

        // Token: 0x060002A8 RID: 680 RVA: 0x0000C42C File Offset: 0x0000A62C
        public void SetSaveOnMeasurementEnd(bool enable)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            this.saveWhenFinishedCheckBox.Checked = enable;
            activeDocument.ActiveResultData.MeasurementController.SaveOnMeasurementEnd = enable;
        }

        // Token: 0x040000CD RID: 205
        MainForm mainForm;

        // Token: 0x040000CE RID: 206
        DeviceConfigManager deviceConfigManager = DeviceConfigManager.GetInstance();

        // Token: 0x040000CF RID: 207
        ROIConfigManager roiConfigManager = ROIConfigManager.GetInstance();

        // Token: 0x040000D0 RID: 208
        DocumentManager documentManager = DocumentManager.GetInstance();

        // Token: 0x040000D1 RID: 209
        bool formUpdating;

        public static bool exept_flag;
    }
}
