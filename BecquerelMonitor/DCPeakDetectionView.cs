using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
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
            this.columnModel1.Columns[0].Renderer = new PeakOriginCellRenderer();

            this.RefreshNuclideSets();
            this.UpdateDeconvolutionInfoButtonState();
        }

        // Token: 0x0600043E RID: 1086 RVA: 0x0001423C File Offset: 0x0001243C
        public void ShowPeakDetectionResult()
        {
            this.FormLoading = true;
            this.checkBoxDeconvolution.Checked = false;
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null || activeDocument.ActiveResultData == null)
            {
                this.tableModel1.Rows.Clear();
                this.FormLoading = false;
                this.UpdateDeconvolutionInfoButtonState();
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            DeviceConfigInfo deviceConfigInfo = activeResultData.DeviceConfig;
            if (deviceConfigInfo == null)
            {
                this.tableModel1.Rows.Clear();
                this.FormLoading = false;
                this.UpdateDeconvolutionInfoButtonState();
                return;
            }
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
                if (deviceConfigInfo != null && deviceConfigInfo.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fallbackConfig)
                {
                    activeResultData.DeviceConfig.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)fallbackConfig.Clone();
                }
            }
            // Give the ResultData its OWN config copy only when it has none. The old code
            // unconditionally assigned the device config's object (without Clone) on every
            // refresh, so SNR/tolerance became shared between documents and edits mutated
            // the global device config.
            if (!(activeResultData.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig)
                && deviceConfigInfo != null
                && deviceConfigInfo.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig deviceMethodConfig)
            {
                activeResultData.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)deviceMethodConfig.Clone();
            }
            if (!(activeResultData.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig))
            {
                this.tableModel1.Rows.Clear();
                this.FormLoading = false;
                this.UpdateDeconvolutionInfoButtonState();
                return;
            }
            this.checkBoxDeconvolution.Checked = fwhmPeakDetectionMethodConfig.UseDeconvolution;

            this.numericUpDown1.Minimum = 1;
            this.numericUpDown1.Maximum = 10000;
            this.numericUpDown1.Increment = 1;
            // Don't overwrite a value the user is editing: this method runs every 2 s
            // from the main timer during recording and used to reset the field.
            if (!this.numericUpDown1.Focused)
            {
                decimal minSnr = (decimal)fwhmPeakDetectionMethodConfig.Min_SNR;
                if (this.numericUpDown1.Value != minSnr)
                {
                    this.numericUpDown1.Value = minSnr;
                }
            }

            this.numericUpDown3.Minimum = 0;
            this.numericUpDown3.Maximum = 100;
            this.numericUpDown3.Increment = 0.1m;
            if (!this.numericUpDown3.Focused)
            {
                decimal tolerance = (decimal)fwhmPeakDetectionMethodConfig.Tolerance;
                if (this.numericUpDown3.Value != tolerance)
                {
                    this.numericUpDown3.Value = tolerance;
                }
            }

            this.FormLoading = false;
            this.UpdatePeakDetectionResult();
            this.RefreshTable();
        }

        public async void UpdatePeakDetectionResult()
        {
            if (isProcessing)
            {
                // Don't silently drop the request ("drop, not queue"): re-run once the
                // current detection finishes, so e.g. an SNR change made mid-run is not lost.
                refreshPending = true;
                return;
            }
            isProcessing = true;

            try
            {
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                if (activeDocument == null)
                {
                    return;
                }
                ResultData activeResultData = activeDocument.ActiveResultData;
                FWHMPeakDetectionMethodConfig fWHMConfig = (FWHMPeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
                if (activeResultData.FwhmCalibration == null)
                {
                    // No calibration - nothing to detect. This used to throw
                    // NotImplementedException, silently swallowed by the catch below.
                    return;
                }
                // Snapshot the detection inputs on the UI thread before handing off to the
                // background Task.Run. DetectPeak used to run against the live ResultData: the
                // device loop mutates EnergySpectrum.Spectrum in place (via originalContext.Post
                // on the UI thread) and the config fields can change mid-run, so the background
                // thread could observe a half-written spectrum or a torn config. EnergySpectrum
                // /config Clone() deep-copy their arrays, so the snapshot is fully detached.
                ResultData snapshot = new ResultData
                {
                    EnergySpectrum = activeResultData.EnergySpectrum.Clone(),
                    BackgroundEnergySpectrum = activeResultData.BackgroundEnergySpectrum != null
                        ? activeResultData.BackgroundEnergySpectrum.Clone()
                        : null,
                    PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)fWHMConfig.Clone(),
                    // Clone the calibration too (guarded like ResultData.Clone): passing the live
                    // reference let the background detector see a mid-edit FWHM calibration if the
                    // user changed it via the UI during Task.Run.
                    FwhmCalibration = activeResultData.FwhmCalibration != null
                        ? activeResultData.FwhmCalibration.Clone()
                        : null
                };
                BackgroundMode bgMode = activeDocument.EnergySpectrumView.BackgroundMode;
                SmoothingMethod smoothMethod = activeDocument.EnergySpectrumView.SmoothingMethod;

                PeakDetector peakDetector = new PeakDetector();
                List<Peak> peaks = await Task.Run(() => peakDetector.DetectPeak(snapshot,
                    bgMode,
                    smoothMethod,
                    this.selectedNuclideSet));
                activeResultData.DetectedPeaks = new List<Peak>(peaks);
                // Refresh only if the user is still on the same document: RefreshTable()
                // reads the CURRENT ActiveResultData and used to show peaks of a foreign
                // spectrum after switching documents mid-detection.
                if (this.mainForm.ActiveDocument == activeDocument)
                {
                    RefreshTable();
                }
            }
            catch (Exception ex)
            {
                // Don't swallow silently - at least leave a trace.
                System.Diagnostics.Trace.WriteLine("Peak detection failed: " + ex.Message);
            }
            finally
            {
                isProcessing = false;
                if (refreshPending)
                {
                    refreshPending = false;
                    UpdatePeakDetectionResult();
                }
            }
        }

        public void RefreshTable()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null || activeDocument.ActiveResultData == null)
            {
                this.tableModel1.Rows.Clear();
                this.UpdateDeconvolutionInfoButtonState();
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            if (activeResultData.DetectedPeaks == null)
            {
                this.tableModel1.Rows.Clear();
                this.UpdateDeconvolutionInfoButtonState();
                return;
            }
            List<Peak> peaks = new List<Peak>(activeResultData.DetectedPeaks);
            if (peaks != null)
            {
                // if peaks exist, update table
                EnergyCalibration energyCalibration = activeDocument.ActiveResultData.EnergySpectrum.EnergyCalibration;
                if (energyCalibration == null)
                {
                    this.tableModel1.Rows.Clear();
                    this.UpdateDeconvolutionInfoButtonState();
                    return;
                }

                this.tableModel1.Rows.Clear();
                foreach (Peak peak in peaks)
                {
                    Row row = new Row();
                    string text = Resources.UnknownNuclide;
                    string text2 = "";
                    if (peak.Nuclide != null)
                    {
                        text = peak.Nuclide.Name;
                        if (peak.Nuclide.Energy > 0.0)
                        {
                            double num = peak.Energy - peak.Nuclide.Energy;
                            double num2 = (peak.Energy - peak.Nuclide.Energy) / peak.Nuclide.Energy * 100.0;
                            text2 = num.ToString("f2") + " (" + num2.ToString("f2") + "%)";
                        }
                    }
                    int snr = (int)peak.SNR;
                    Cell nuclideCell = new Cell(text);
                    // Весь Peak, а не только origin: рендереру нужен и
                    // Nuclide.IsAnchor (красный якорь), и origin (синий LIB).
                    nuclideCell.Tag = peak;
                    row.Cells.Add(nuclideCell);
                    row.Cells.Add(new Cell(peak.Energy.ToString("f2"), Math.Round(peak.Energy, 2)));
                    row.Cells.Add(new Cell(text2));
                    row.Cells.Add(new Cell(peak.Channel.ToString(), peak.Channel));
                    row.Cells.Add(new Cell(snr.ToString(), snr));

                    double leftEnergy = energyCalibration.ChannelToEnergy(peak.Channel - peak.FWHM / 2.0);
                    double rightEnergy = energyCalibration.ChannelToEnergy(peak.Channel + peak.FWHM / 2.0);
                    double resolution = 100.0 * (rightEnergy - leftEnergy) / energyCalibration.ChannelToEnergy((double)peak.Channel);

                    row.Cells.Add(new Cell(peak.FWHM.ToString("f0") + ", " + resolution.ToString("f1") + "% ±" + peak.FWHM_DELTA.ToString("f1")));
                    this.tableModel1.Rows.Add(row);
                }
                activeDocument.RefreshView();
                //this.table1.AutoResizeColumnWidths();
            }

            this.UpdateDeconvolutionInfoButtonState();
        }

        public void RefreshNuclideSets()
        {
            this.comboBoxNuclSet.Items.Clear();
            string allNuclidesText = this.comboBoxNuclSetAllNuclidesText;
            if (string.IsNullOrEmpty(allNuclidesText))
            {
                allNuclidesText = "--- All Nuclides ---";
            }

            this.comboBoxNuclSet.Items.Add(allNuclidesText);
            foreach (NuclideSet set in this.nuclideManager.NuclideSets)
            {
                this.comboBoxNuclSet.Items.Add(set.Name);
            }
            if (this.selectedNuclideSet != null)
            {
                this.comboBoxNuclSet.SelectedIndex = this.nuclideManager.NuclideSets.IndexOf(this.selectedNuclideSet) + 1;
            }
            else
            {
                this.comboBoxNuclSet.SelectedIndex = 0;
            }
        }

        // Token: 0x06000440 RID: 1088 RVA: 0x00014468 File Offset: 0x00012668
        void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            if (this.FormLoading == false)
            {
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                ResultData activeResultData = activeDocument.ActiveResultData;
                FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)activeResultData.PeakDetectionMethodConfig;
                fwhmPeakDetectionMethodConfig.Min_SNR = (double)((int)this.numericUpDown1.Value);
                this.UpdatePeakDetectionResult();
                activeDocument.EnergySpectrumView.Invalidate();
            }
        }

        // Token: 0x06000441 RID: 1089 RVA: 0x00014500 File Offset: 0x00012700
        void checkBoxDeconvolution_CheckedChanged(object sender, EventArgs e)
        {
            if (this.FormLoading == false)
            {
                DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
                ResultData activeResultData = activeDocument?.ActiveResultData;
                // Mutate the per-document clone only. Taking the object from
                // DeviceConfig.PeakDetectionMethodConfig, mutating it and assigning that same
                // reference back into activeResultData.PeakDetectionMethodConfig used to alias
                // the shared global device config into the ResultData, breaking the clone made
                // in DocumentManager.PrepareDeviceConfig.
                if (!(activeResultData?.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fwhmPeakDetectionMethodConfig))
                {
                    return;
                }

                fwhmPeakDetectionMethodConfig.UseDeconvolution = this.checkBoxDeconvolution.Checked;

                // Persist the preference to the shared device config separately, without
                // aliasing it into the ResultData clone.
                DeviceConfigInfo deviceConfig = activeResultData.DeviceConfig;
                if (deviceConfig?.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig deviceFwhmConfig)
                {
                    deviceFwhmConfig.UseDeconvolution = this.checkBoxDeconvolution.Checked;
                    DeviceConfigManager deviceConfigManager = DeviceConfigManager.GetInstance();
                    if (!string.IsNullOrEmpty(deviceConfig.Guid) && deviceConfigManager.DeviceConfigMap.ContainsKey(deviceConfig.Guid))
                    {
                        deviceConfigManager.SaveConfig(deviceConfig);
                    }
                }

                this.UpdatePeakDetectionResult();
                if (activeDocument != null && activeDocument.EnergySpectrumView != null)
                {
                    activeDocument.EnergySpectrumView.Invalidate();
                }
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
                fwhmPeakDetectionMethodConfig.Tolerance = (double)this.numericUpDown3.Value;
                this.UpdatePeakDetectionResult();
                activeDocument.EnergySpectrumView.Invalidate();
            }
        }

        void ToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            int channel = 0;
            decimal diff;
            decimal energy;
            foreach (Row row in this.table1.SelectedItems)
            {
                try
                {
                    channel = Convert.ToInt32(row.Cells[3].Text);
                    if (row.Cells[2].Text.Length > 1)
                    {
                        diff = Convert.ToDecimal(row.Cells[2].Text.Split(new string[] { " " }, StringSplitOptions.None)[0]);
                    } else
                    {
                        diff = 0;
                    }
                    energy = Convert.ToDecimal(row.Cells[1].Text) - diff;
                    if (this.mainForm.ActiveDocument.ActiveResultData.EnergySpectrum.Spectrum.Length > channel)
                    {
                        this.mainForm.addCalibration(channel, energy, this.mainForm.ActiveDocument.ActiveResultData.EnergySpectrum.Spectrum[channel]);
                    } else
                    {
                        throw new Exception(Resources.ERRCalibrationChannelExceed);
                    }
                    
                } catch (Exception ex)
                {
                    MessageBox.Show(String.Format(Resources.ERRAddCalibrationPoints, channel.ToString(), ex.Message), Resources.ErrorExclamation, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void ToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            decimal energy = Convert.ToDecimal(this.table1.SelectedItems[0].Cells[1].Text);
            this.mainForm.CallNucBaseSearch(energy);
        }

        void ToolStripMenuItem1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (this.table1.SelectedItems.Length == 0)
            {
                this.toolStripMenuItem1.Enabled = false;
                this.toolStripMenuItem2.Enabled = false;
            } else
            {
                this.toolStripMenuItem1.Enabled = true;
                this.toolStripMenuItem2.Enabled = true;
            }
        }


        // Token: 0x06000443 RID: 1091 RVA: 0x00014614 File Offset: 0x00012814
        void button1_Click(object sender, EventArgs e)
        {
            this.mainForm.ShowNuclideDefinitionForm();
        }

        private void comboBoxNuclSet_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.comboBoxNuclSet.SelectedIndex > 0)
            {
                this.selectedNuclideSet = this.nuclideManager.NuclideSets[this.comboBoxNuclSet.SelectedIndex - 1];
            }
            else
            {
                this.selectedNuclideSet = null;
            }

            this.UpdatePeakDetectionResult();
        }

        void buttonDeconvolutionInfo_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            List<Peak> deconvolvedPeaks = this.GetDetectedDeconvolutionPeaks();
            if (activeDocument == null || deconvolvedPeaks.Count == 0)
            {
                return;
            }

            using (PeakDeconvolutionInfoForm dialog = new PeakDeconvolutionInfoForm(activeDocument, deconvolvedPeaks, this.selectedNuclideSet))
            {
                dialog.ShowDialog(this);
            }
        }

        List<Peak> GetDetectedDeconvolutionPeaks()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            ResultData activeResultData = activeDocument != null ? activeDocument.ActiveResultData : null;
            if (activeResultData == null || activeResultData.DetectedPeaks == null)
            {
                return new List<Peak>();
            }

            return activeResultData.DetectedPeaks
                .Where(peak => peak != null &&
                    peak.PeakSearchOrigin == PeakSearchOrigin.RJMCMC &&
                    peak.DeconvolutionInfo != null)
                .ToList();
        }

        void UpdateDeconvolutionInfoButtonState()
        {
            this.buttonDeconvolutionInfo.Enabled = this.checkBoxDeconvolution.Checked && this.GetDetectedDeconvolutionPeaks().Count > 0;
        }

        // Token: 0x040001B3 RID: 435
        MainForm mainForm;

        // Token: 0x040001B4 RID: 436
        DocumentManager documentManager = DocumentManager.GetInstance();

        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();

        bool FormLoading = false;

        string comboBoxNuclSetAllNuclidesText = null;

        private NuclideSet selectedNuclideSet = null;

        private bool isProcessing = false;

        private bool refreshPending = false;
    }
}
