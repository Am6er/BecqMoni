using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Deployment.Application;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using System.Xml.Serialization;
using WeifenLuo.WinFormsUI.Docking;

namespace BecquerelMonitor
{
    // Token: 0x020000CF RID: 207
    public partial class MainForm : Form
    {

        public static SynchronizationContext originalContext;

        // Token: 0x170002D4 RID: 724
        // (get) Token: 0x06000A40 RID: 2624 RVA: 0x0003C540 File Offset: 0x0003A740
        // (set) Token: 0x06000A41 RID: 2625 RVA: 0x0003C548 File Offset: 0x0003A748
        public DocEnergySpectrum ActiveDocument
        {
            get
            {
                return this.activeDocument;
            }
            set
            {
                this.activeDocument = value;
            }
        }

        // Token: 0x170002D5 RID: 725
        // (get) Token: 0x06000A42 RID: 2626 RVA: 0x0003C554 File Offset: 0x0003A754
        public DCControlPanel ControlPanel
        {
            get
            {
                return this.dcControlPanel;
            }
        }

        // Token: 0x170002D6 RID: 726
        // (get) Token: 0x06000A43 RID: 2627 RVA: 0x0003C55C File Offset: 0x0003A75C
        // (set) Token: 0x06000A44 RID: 2628 RVA: 0x0003C564 File Offset: 0x0003A764
        public bool DoUpdatePulseView
        {
            get
            {
                return this.doUpdatePulseView;
            }
            set
            {
                this.doUpdatePulseView = value;
                this.dcPulseView.DoUpdatePulseView = value;
                if (this.activeDocument != null && this.activeDocument.PulseDetector != null)
                {
                    this.activeDocument.PulseDetector.DoUpdatePulseView = value;
                }
            }
        }

        // Token: 0x170002D7 RID: 727
        // (get) Token: 0x06000A45 RID: 2629 RVA: 0x0003C5B4 File Offset: 0x0003A7B4
        // (set) Token: 0x06000A46 RID: 2630 RVA: 0x0003C5BC File Offset: 0x0003A7BC
        public DCPulseView DCPulseView
        {
            get
            {
                return this.dcPulseView;
            }
            set
            {
                this.dcPulseView = value;
            }
        }

        // Token: 0x06000A47 RID: 2631 RVA: 0x0003C5C8 File Offset: 0x0003A7C8
        public MainForm(string[] args)
        {
            BecquerelMonitor.Package package = new BecquerelMonitor.Package();
            originalContext = SynchronizationContext.Current;
            if (!package.IsStandAlone && !Directory.Exists(userDirectory) || !Directory.Exists(userDirectoryConfig))
            {
                try
                {
                    string directoryName = Path.GetDirectoryName(Application.ExecutablePath);
                    Environment.CurrentDirectory = directoryName;
                    Directory.CreateDirectory(userDirectory);
                    Directory.CreateDirectory(userDirectory + "\\config");
                    foreach (string dirPath in Directory.GetDirectories(directoryName + "\\config", "*", SearchOption.AllDirectories))
                    {
                        Directory.CreateDirectory(dirPath.Replace(directoryName + "\\config", userDirectoryConfig));
                    }
                    foreach (string newPath in Directory.GetFiles(directoryName + "\\config", "*.*", SearchOption.AllDirectories))
                    {
                        File.Copy(newPath, newPath.Replace(directoryName + "\\config", userDirectoryConfig), true);
                    }

                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
            this.UpdateApplicationTitle();
            this.globalConfigManager = GlobalConfigManager.GetInstance();
            this.globalConfig = this.globalConfigManager.GlobalConfig;
            if (this.globalConfig == null)
            {
                base.Close();
                return;
            }
            if (this.globalConfig.Language != "OS")
            {
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(this.globalConfig.Language);
                System.Globalization.CultureInfo customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
                customCulture.NumberFormat.NumberDecimalSeparator = ".";
                System.Threading.Thread.CurrentThread.CurrentCulture = customCulture;
            }
            DeviceType.InitializeDeviceTypes();
            ThermometerType.InitializeThermometerTypes();
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            this.globalConfigManager.PrepareConfigFile();
            this.resultManager = new MeasurementResultManager();
            this.doseRateManager = new DoseRateManager();
            this.InitializeComponent();
            base.Icon = BecquerelMonitor.Properties.Resources.becqmoni;
            this.toolStripMenuItem6.Visible = false;
            this.toolStripMenuItem3.Visible = false;
            this.toolStripMenuItem5.Visible = false;
            this.toolStripMenuItem7.Visible = false;
            this.toolStripSeparator9.Visible = false;
            this.toolStripMenuItem2.Visible = false;
            this.toolStripSeparator4.Visible = false;
            this.fWHM用ToolStripMenuItem.Visible = false;
            this.startupForm = new StartupForm();
            this.startupForm.MessageText = BecquerelMonitor.Properties.Resources.InitializingMessage;
            this.startupForm.Show();
            this.startupForm.Refresh();
            if (args != null && args.Length == 1)
            {
                this.OpenFileName = args[0];
            }
        }

        // Token: 0x06000A48 RID: 2632 RVA: 0x0003C778 File Offset: 0x0003A978
        void MainForm_Load(object sender, EventArgs e)
        {
            this.InitializeToolViews();
            this.m_deserializeDockContent = new DeserializeDockContent(this.GetContentFromPersistString);
            this.dcPulseView.MainForm = this;
            this.deviceConfigManager = DeviceConfigManager.GetInstance();
            this.roiConfigManager = ROIConfigManager.GetInstance();
            this.deviceConfigManager.DeviceConfigListChanged += this.manager_DeviceConfigChanged;
            this.roiConfigManager.ROIConfigListChanged += this.manager_ROIConfigListChanged;
            if (this.globalConfig != null)
            {
                base.Width = ((this.globalConfig.MainFormWidth < 640) ? 640 : this.globalConfig.MainFormWidth);
                base.Height = ((this.globalConfig.MainFormHeight < 480) ? 480 : this.globalConfig.MainFormHeight);
                Rectangle totalBound = this.GetTotalBound();
                base.Left = this.globalConfig.MainFormLeft;
                base.Top = this.globalConfig.MainFormTop;
                if (base.Left + base.Width > totalBound.Right)
                {
                    base.Left = totalBound.Right - base.Width;
                }
                if (base.Left < totalBound.Left)
                {
                    base.Left = totalBound.Left;
                }
                if (base.Top + base.Height > totalBound.Bottom)
                {
                    base.Top = totalBound.Bottom - base.Height;
                }
                if (base.Top < totalBound.Top)
                {
                    base.Top = totalBound.Top;
                }
                if (this.globalConfig.MainFormMaximized)
                {
                    base.WindowState = FormWindowState.Maximized;
                }
                this.DoUpdatePulseView = this.globalConfig.ShowPulseShape;
                this.documentManager.DefaultEnergyScale = this.defaultEnergyScale;
                this.layoutMode = this.globalConfig.LayoutMode;
                this.UpdateLayoutCheckState();
                this.UpdateLanguageCheckState();
            }
            this.dockPanel1.SuspendLayout(true);
            string text = this.LayoutConfigFile(this.layoutMode);
            if (File.Exists(text))
            {
                this.dockPanel1.LoadFromXml(text, this.m_deserializeDockContent);
            }
            this.dockPanel1.ResumeLayout(true, true);
            this.initialized = true;
            this.timer = new System.Windows.Forms.Timer();
            this.timer.Interval = 100;
            this.timer.Tick += this.OnTimer;
            this.timer.Start();
            this.startupForm.Close();
            this.startupForm = null;
            foreach (DocEnergySpectrum docEnergySpectrum in this.documentManager.DocumentList)
            {
                docEnergySpectrum.SetDefaultHorizontalScale();
            }

            if (OpenFileName != null)
            {
                this.OpenExistingDocument(this.OpenFileName);
            }
        }

        // Token: 0x06000A49 RID: 2633 RVA: 0x0003CA98 File Offset: 0x0003AC98
        void InitializeToolViews()
        {
            this.dcControlPanel = new DCControlPanel(this);
            this.dcDebugPanel = new DCDebugPanel(this);
            this.dcPulseView = new DCPulseView(this);
            this.dcSampleInfoView = new DCSampleInfoView(this);
            this.dcSpectrumListView = new DCSpectrumListView(this);
            this.dcPeakDetectionView = new DCPeakDetectionView(this);
            this.dcSpectrumExplorerView = new DCSpectrumExplorerView(this);
            this.dcEnergyCalibrationView = new DCEnergyCalibrationView(this);
            this.dcControlPanel.Enabled = false;
            this.dcSampleInfoView.Enabled = false;
            this.dcSpectrumListView.Enabled = false;
            this.dcPeakDetectionView.Enabled = false;
            this.dcEnergyCalibrationView.Enabled = false;

        }

        // Token: 0x06000A4A RID: 2634 RVA: 0x0003CB48 File Offset: 0x0003AD48
        void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!this.initialized)
            {
                return;
            }
            foreach (DocEnergySpectrum docEnergySpectrum in this.documentManager.DocumentList)
            {
                DeviceController dc = docEnergySpectrum.ActiveResultData.MeasurementController.DeviceController;
                if (dc is AtomSpectraDeviceController)
                {
                    ((AtomSpectraDeviceController)dc).applicationCLose();
                }

                if (!docEnergySpectrum.IsNamed && !docEnergySpectrum.Dirty)
                {
                    this.UnsubscribeDocumentEvent(docEnergySpectrum);
                    docEnergySpectrum.Close();
                }
            }
            this.mainFormClosing = true;
            string fileName = this.LayoutConfigFile(this.layoutMode);
            try
            {
                this.dockPanel1.SaveAsXml(fileName);
            }
            catch (Exception)
            {
            }
            if (this.deviceConfigForm != null && !this.deviceConfigForm.IsDisposed)
            {
                this.deviceConfigForm.Close();
            }
            if (this.roiConfigForm != null && !this.roiConfigForm.IsDisposed)
            {
                this.roiConfigForm.Close();
            }
            GlobalConfigInfo globalConfigInfo = this.globalConfigManager.GlobalConfig;
            if (base.WindowState == FormWindowState.Maximized)
            {
                globalConfigInfo.MainFormMaximized = true;
                Rectangle restoreBounds = base.RestoreBounds;
                globalConfigInfo.MainFormTop = restoreBounds.Top;
                globalConfigInfo.MainFormLeft = restoreBounds.Left;
                globalConfigInfo.MainFormWidth = restoreBounds.Width;
                globalConfigInfo.MainFormHeight = restoreBounds.Height;
            }
            else
            {
                globalConfigInfo.MainFormMaximized = false;
                globalConfigInfo.MainFormTop = base.Top;
                globalConfigInfo.MainFormLeft = base.Left;
                globalConfigInfo.MainFormWidth = base.Width;
                globalConfigInfo.MainFormHeight = base.Height;
            }
            globalConfigInfo.ShowPulseShape = this.DoUpdatePulseView;
            globalConfigInfo.LayoutMode = this.layoutMode;
            this.globalConfigManager.SaveConfigFile();
        }

        // Token: 0x06000A4B RID: 2635 RVA: 0x0003CD0C File Offset: 0x0003AF0C
        IDockContent GetContentFromPersistString(string persistString)
        {
            string[] array = persistString.Split(new char[]
            {
                ','
            });
            string a = array[0];
            if (a == typeof(DCControlPanel).ToString())
            {
                return this.dcControlPanel;
            }
            if (a == typeof(DCDebugPanel).ToString())
            {
                return this.dcDebugPanel;
            }
            if (a == typeof(DCPulseView).ToString())
            {
                return this.dcPulseView;
            }
            if (a == typeof(DCResultView).ToString())
            {
                DCResultView dcresultView = new DCResultView(this);
                dcresultView.Enabled = false;
                dcresultView.FormClosed += this.dcResultView_FormClosed;
                this.dcResultViewList.Add(dcresultView);
                if (array.Length >= 2)
                {
                    ResultTranslation resultTranslation;
                    try
                    {
                        resultTranslation = (ResultTranslation)Enum.Parse(typeof(ResultTranslation), array[1]);
                    }
                    catch (Exception)
                    {
                        resultTranslation = ResultTranslation.Nothing;
                    }
                    dcresultView.ResultTranslation = resultTranslation;
                }
                if (array.Length >= 3)
                {
                    ResultCorrection resultCorrection;
                    try
                    {
                        resultCorrection = (ResultCorrection)Enum.Parse(typeof(ResultCorrection), array[2]);
                    }
                    catch (Exception)
                    {
                        resultCorrection = ResultCorrection.Nothing;
                    }
                    dcresultView.ResultCorrection = resultCorrection;
                }
                return dcresultView;
            }
            if (a == typeof(DCSampleInfoView).ToString())
            {
                return this.dcSampleInfoView;
            }
            if (a == typeof(DCSpectrumListView).ToString())
            {
                return this.dcSpectrumListView;
            }
            if (a == typeof(DCPeakDetectionView).ToString())
            {
                return this.dcPeakDetectionView;
            }
            if (a == typeof(DCSpectrumExplorerView).ToString())
            {
                return this.dcSpectrumExplorerView;
            }
            if (a == typeof(DCEnergyCalibrationView).ToString())
            {
                return this.dcEnergyCalibrationView;
            }
            if (!(a == typeof(DocEnergySpectrum).ToString()))
            {
                return null;
            }
            if (array.Length != 3)
            {
                return null;
            }
            if (this.startupForm != null)
            {
                this.startupForm.AppendMessage(string.Format(BecquerelMonitor.Properties.Resources.LoadingFileMessage, Path.GetFileName(array[1])));
                Application.DoEvents();
            }
            DocEnergySpectrum docEnergySpectrum = this.documentManager.OpenDocument(array[1]);
            this.SubscribeDocumentEvent(docEnergySpectrum);
            if (docEnergySpectrum == null)
            {
                return null;
            }
            docEnergySpectrum.DockAreas = DockAreas.Document;
            return docEnergySpectrum;
        }

        // Token: 0x06000A4C RID: 2636 RVA: 0x0003CFA4 File Offset: 0x0003B1A4
        void OnTimer(object sender, EventArgs e)
        {
            foreach (DocEnergySpectrum docEnergySpectrum in this.documentManager.DocumentList)
            {
                foreach (ResultData resultData in docEnergySpectrum.ResultDataFile.ResultDataList)
                {
                    resultData.MeasurementController.OnTimer(sender, e);
                }
            }
            this.count2000 += 100;
            if (this.count2000 >= 2000)
            {
                this.count2000 = 0;
                if (this.activeDocument != null && (this.activeDocument.UpdateSpectrum || this.activeDocument.UpdateMeasurementResult))
                {
                    this.dcPeakDetectionView.ShowPeakDetectionResult();
                }
                if (this.activeDocument != null && this.activeDocument.UpdateDetectedPeaks)
                {
                    this.dcPeakDetectionView.ShowPeakDetectionResult();
                    this.activeDocument.UpdateDetectedPeaks = false;
                }
            }
            this.count1000 += 100;
            if (this.count1000 >= 1000)
            {
                this.count1000 = 0;
                if (this.activeDocument != null && (this.activeDocument.UpdateSpectrum || this.activeDocument.UpdateMeasurementResult))
                {
                    this.UpdateCalibrationPeak();
                }
            }
            this.count500 += 100;
            if (this.count500 >= 500)
            {
                this.count500 = 0;
                if (this.activeDocument != null && (this.activeDocument.UpdateSpectrum || this.activeDocument.UpdateMeasurementResult))
                {
                    this.ShowMeasurementResult(false);
                    this.activeDocument.UpdateMeasurementResult = false;
                }
            }
            this.count200 += 100;
            if (this.count200 >= 200)
            {
                this.count200 = 0;
                if (this.activeDocument != null && (this.activeDocument.UpdateSpectrum || this.activeDocument.UpdateMeasurementResult))
                {
                    this.ShowDoseRate();
                }
                if (this.activeDocument != null && this.activeDocument.UpdateDoseRate)
                {
                    this.ShowDoseRate();
                    this.activeDocument.UpdateDoseRate = false;
                }
            }
            this.countChart += 100;
            if (this.countChart >= this.globalConfigManager.GlobalConfig.ChartViewConfig.ChartRefreshCycle)
            {
                foreach (DocEnergySpectrum docEnergySpectrum2 in this.documentManager.DocumentList)
                {
                    if (docEnergySpectrum2.UpdateSpectrum)
                    {
                        ResultData activeResultData = docEnergySpectrum2.ActiveResultData;
                        activeResultData.EndTime = DateTime.Now;
                        docEnergySpectrum2.EnergySpectrumView.RecalcChartParameters();
                        docEnergySpectrum2.EnergySpectrumView.PrepareViewData();
                        docEnergySpectrum2.EnergySpectrumView.RecalcScrollBar();
                        docEnergySpectrum2.EnergySpectrumView.Invalidate();
                    }
                }
                if (this.activeDocument != null)
                {
                    ResultData activeResultData2 = this.activeDocument.ActiveResultData;
                    bool testing = activeResultData2.ResultDataStatus.Testing;
                }
                this.countChart = 0;
            }
            this.countAutoSave += 100;
            if (this.countAutoSave >= this.globalConfigManager.GlobalConfig.AutosavePeriod * 60 * 1000)
            {
                this.countAutoSave = 0;
                foreach (DocEnergySpectrum docEnergySpectrum in this.documentManager.DocumentList)
                {
                    if ((docEnergySpectrum.Dirty || docEnergySpectrum.UpdateSpectrum) && docEnergySpectrum.AutoSave)
                    {
                        SaveDocument(docEnergySpectrum);
                        DateTime dt = DateTime.Now;
                        SetStatusTextLeft(String.Format(Resources.AutosaveText, dt.ToString()));
                    }
                }
            }
            if (this.activeDocument != null)
            {
                ResultData activeResultData3 = this.activeDocument.ActiveResultData;
                if (activeResultData3.ResultDataStatus.Recording)
                {
                    this.dcControlPanel.ShowRecordingStatus();
                }
            }
            if (this.doUpdatePulseView && this.activeDocument != null)
            {
                ResultData activeResultData4 = this.activeDocument.ActiveResultData;
                if (activeResultData4.ResultDataStatus.Recording)
                {
                    this.dcPulseView.PulseView.Invalidate();
                    this.dcPulseView.NGPulseView.Invalidate();
                }
            }
        }

        // Token: 0x06000A4D RID: 2637 RVA: 0x0003D32C File Offset: 0x0003B52C
        public void ShowMeasurementResult(bool refresh)
        {
            if (this.activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = this.activeDocument.ActiveResultData;
            MeasurementResultCollection resultCollection = this.resultManager.Calculate(activeResultData);
            activeResultData.MeasurementResultCollection = this.resultManager.Translate(resultCollection, ResultTranslation.BecquerelsPerKilogram);
            foreach (DCResultView dcresultView in this.dcResultViewList)
            {
                dcresultView.ShowResult(resultCollection, refresh);
            }
        }

        // Token: 0x06000A4E RID: 2638 RVA: 0x0003D3C0 File Offset: 0x0003B5C0
        public void ShowDoseRate()
        {
            if (this.activeDocument != null && this.activeDocument.ActiveResultData.DeviceConfig.DoseRateConfig != null && 
                this.activeDocument.ActiveResultData.DeviceConfig.DoseRateConfig.DoseRateCalibrationPoints.Count > 0)
            {
                DoseRate doseRate = this.doseRateManager.Calculate(this.activeDocument.ActiveResultData,
                    this.activeDocument.ActiveResultData.DeviceConfig.DoseRateConfig,
                    this.activeDocument.EnergySpectrumView.BackgroundMode);
                if (this.dcDoseRateView != null)
                {
                    SetStatusTextRight(Resources.DoseRate + " " + doseRate.ToString());
                    this.dcDoseRateView.ShowDoseRate(doseRate);
                } else
                {
                    SetStatusTextRight(Resources.DoseRate + " " + doseRate.ToString());
                }
            } else
            {
                ClearStatusTextRight();
            }
        }

        // Token: 0x06000A4F RID: 2639 RVA: 0x0003D3C4 File Offset: 0x0003B5C4
        public void UpdateSpectrumListView()
        {
            if (this.activeDocument == null)
            {
                return;
            }
            if (this.dcSpectrumListView != null && !this.dcSpectrumListView.IsDisposed)
            {
                this.dcSpectrumListView.ShowSpectrumList(this.activeDocument);
            }
        }

        // Token: 0x06000A50 RID: 2640 RVA: 0x0003D400 File Offset: 0x0003B600
        public void SetStatusMessage(string message, Color color, bool doScroll)
        {
            if (this.dcStatusMessageView == null)
            {
                return;
            }
            this.dcStatusMessageView.Message = message;
            this.dcStatusMessageView.MessageColor = color;
            this.dcStatusMessageView.DoScroll = doScroll;
        }

        // Token: 0x06000A51 RID: 2641 RVA: 0x0003D434 File Offset: 0x0003B634
        public void UpdateDetectedPeakView()
        {
            if (this.activeDocument == null)
            {
                return;
            }
            this.dcPeakDetectionView.ShowPeakDetectionResult();
        }

        // Token: 0x06000A52 RID: 2642 RVA: 0x0003D450 File Offset: 0x0003B650
        public void UpdateCalibrationPeak()
        {
            ResultData activeResultData = this.activeDocument.ActiveResultData;
            if (activeResultData.ResultDataStatus.Stabilization)
            {
                PeakStabilizer peakStabilizer = new PeakStabilizer();
                peakStabilizer.Stabilize(activeResultData);
                this.dcEnergyCalibrationView.SetEnergyCalibration(activeResultData.EnergySpectrum.EnergyCalibration, activeResultData.DeviceConfig.EnergyCalibration);
                this.activeDocument.Dirty = true;
            }
            else
            {
                activeResultData.CalibrationPeaks.Clear();
            }
            this.activeDocument.UpdateEnergySpectrum();
        }

        // Token: 0x06000A53 RID: 2643 RVA: 0x0003D4D4 File Offset: 0x0003B6D4
        void 終了XToolStripMenuItem_Click(object sender, EventArgs e)
        {
            base.Close();
        }

        // Token: 0x06000A54 RID: 2644 RVA: 0x0003D4DC File Offset: 0x0003B6DC
        void コントロ\u30FCルパネルCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.dcControlPanel.IsDisposed)
            {
                this.dcControlPanel = new DCControlPanel(this);
            }
            this.dcControlPanel.Show(this.dockPanel1);
            if (this.activeDocument != null)
            {
                this.dcControlPanel.ShowDocumentStatus();
            }
        }

        // Token: 0x06000A55 RID: 2645 RVA: 0x0003D530 File Offset: 0x0003B730
        void パルス表示PToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.dcPulseView.IsDisposed)
            {
                this.dcPulseView = new DCPulseView(this);
            }
            this.dcPulseView.Show(this.dockPanel1);
            if (this.activeDocument != null && this.activeDocument.PulseDetector != null)
            {
                this.activeDocument.PulseDetector.PulseView = this.dcPulseView.PulseView;
                this.activeDocument.PulseDetector.NGPulseView = this.dcPulseView.NGPulseView;
                this.activeDocument.PulseDetector.DoUpdatePulseView = this.doUpdatePulseView;
            }
        }

        // Token: 0x06000A56 RID: 2646 RVA: 0x0003D5D8 File Offset: 0x0003B7D8
        public void ShowSampleInfoView()
        {
            if (this.dcSampleInfoView.IsDisposed)
            {
                this.dcSampleInfoView = new DCSampleInfoView(this);
            }
            this.dcSampleInfoView.Show(this.dockPanel1);
            if (this.activeDocument != null)
            {
                this.dcSampleInfoView.LoadFormContents();
            }
        }

        // Token: 0x06000A57 RID: 2647 RVA: 0x0003D62C File Offset: 0x0003B82C
        void 試料情報SToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ShowSampleInfoView();
        }

        // Token: 0x06000A58 RID: 2648 RVA: 0x0003D634 File Offset: 0x0003B834
        void スペクトル一覧LToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.dcSpectrumListView.IsDisposed)
            {
                this.dcSpectrumListView = new DCSpectrumListView(this);
            }
            this.dcSpectrumListView.Show(this.dockPanel1);
            if (this.activeDocument != null)
            {
                this.dcSpectrumListView.ShowSpectrumList(this.activeDocument);
            }
        }

        // Token: 0x06000A59 RID: 2649 RVA: 0x0003D690 File Offset: 0x0003B890
        void toolStripMenuItem6_Click(object sender, EventArgs e)
        {
        }

        // Token: 0x06000A5A RID: 2650 RVA: 0x0003D694 File Offset: 0x0003B894
        void スペクトルエクスプロ\u30FCラEToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (this.dcSpectrumExplorerView.IsDisposed)
            {
                this.dcSpectrumExplorerView = new DCSpectrumExplorerView(this);
            }
            this.dcSpectrumExplorerView.Show(this.dockPanel1);
        }

        // Token: 0x06000A5B RID: 2651 RVA: 0x0003D6C4 File Offset: 0x0003B8C4
        void デバッグ用パネルToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.dcDebugPanel.IsDisposed)
            {
                this.dcDebugPanel = new DCDebugPanel(this);
            }
            this.dcDebugPanel.Show(this.dockPanel1);
        }

        // Token: 0x06000A5C RID: 2652 RVA: 0x0003D6F4 File Offset: 0x0003B8F4
        void ピ\u30FCク検出DToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.dcPeakDetectionView.IsDisposed)
            {
                this.dcPeakDetectionView = new DCPeakDetectionView(this);
            }
            this.dcPeakDetectionView.Show(this.dockPanel1);
        }

        // Token: 0x06000A5D RID: 2653 RVA: 0x0003D724 File Offset: 0x0003B924
        void toolStripMenuItem3_Click(object sender, EventArgs e)
        {
            if (this.dcStatusMessageView == null || this.dcStatusMessageView.IsDisposed)
            {
                this.dcStatusMessageView = new DCStatusMessageView(this);
            }
            this.dcStatusMessageView.Show(this.dockPanel1);
        }

        // Token: 0x06000A5E RID: 2654 RVA: 0x0003D754 File Offset: 0x0003B954
        void toolStripMenuItem4_Click(object sender, EventArgs e)
        {
            foreach (DocEnergySpectrum docEnergySpectrum in this.documentManager.DocumentList)
            {
                if (docEnergySpectrum != this.activeDocument)
                {
                    docEnergySpectrum.ActiveEnergyCalibration = false;
                }
            }
            this.ShowEnergyCalibrationView(true);
        }

        // Token: 0x06000A5F RID: 2655 RVA: 0x0003D7C8 File Offset: 0x0003B9C8
        public void ShowEnergyCalibrationView(bool visible)
        {
            if (visible)
            {
                if (this.dcEnergyCalibrationView.IsDisposed)
                {
                    this.dcEnergyCalibrationView = new DCEnergyCalibrationView(this);
                }
                this.dcEnergyCalibrationView.Show(this.dockPanel1);
            }
            else if (!this.dcEnergyCalibrationView.IsDisposed)
            {
                this.dcEnergyCalibrationView.Hide();
            }
            if (this.activeDocument != null)
            {
                this.dcEnergyCalibrationView.SetStabilizerState(this.activeDocument.ActiveResultData);
            }
        }

        // Token: 0x06000A60 RID: 2656 RVA: 0x0003D850 File Offset: 0x0003BA50
        public void UpdateEnergyCalibrationView()
        {
            this.dcEnergyCalibrationView.SetEnergyCalibration(this.activeDocument.ActiveResultData.EnergySpectrum.EnergyCalibration, this.activeDocument.ActiveResultData.DeviceConfig.EnergyCalibration);
        }

        // Token: 0x06000A61 RID: 2657 RVA: 0x0003D898 File Offset: 0x0003BA98
        void toolStripMenuItem5_Click(object sender, EventArgs e)
        {
            if (this.dcDoseRateView == null || this.dcDoseRateView.IsDisposed)
            {
                this.dcDoseRateView = new DCDoseRateView(this);
            }
            this.dcDoseRateView.Show(this.dockPanel1);
            ShowDoseRate();
        }

        // Token: 0x06000A62 RID: 2658 RVA: 0x0003D8C8 File Offset: 0x0003BAC8
        void 測定結果表示RToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.dcResultViewList.Count >= 4)
            {
                return;
            }
            DCResultView dcresultView = new DCResultView(this);
            if (this.activeDocument == null)
            {
                dcresultView.Enabled = false;
            }
            this.dcResultViewList.Add(dcresultView);
            dcresultView.Show(this.dockPanel1);
            this.ShowMeasurementResult(false);
        }

        // Token: 0x06000A63 RID: 2659 RVA: 0x0003D924 File Offset: 0x0003BB24
        void デバイス構成定義DToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ShowDeviceConfigForm(null);
        }

        // Token: 0x06000A64 RID: 2660 RVA: 0x0003D930 File Offset: 0x0003BB30
        void 新規スペクトルNToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum docEnergySpectrum = this.documentManager.CreateDocument();
            if (docEnergySpectrum != null)
            {
                docEnergySpectrum.DockAreas = DockAreas.Document;
                this.SubscribeDocumentEvent(docEnergySpectrum);
                docEnergySpectrum.Show(this.dockPanel1);
                docEnergySpectrum.SetDefaultHorizontalScale();
                this.ShowMeasurementResult(true);
            }
        }

        // Token: 0x06000A65 RID: 2661 RVA: 0x0003D97C File Offset: 0x0003BB7C
        public DocEnergySpectrum CreateSpectrumFile(string filename)
        {
            DocEnergySpectrum docEnergySpectrum = this.documentManager.CreateDocument(filename);
            if (docEnergySpectrum != null)
            {
                docEnergySpectrum.DockAreas = DockAreas.Document;
                this.SubscribeDocumentEvent(docEnergySpectrum);
                docEnergySpectrum.Show(this.dockPanel1);
                docEnergySpectrum.SetDefaultHorizontalScale();
                this.ShowMeasurementResult(true);
            }
            return docEnergySpectrum;
        }

        // Token: 0x06000A66 RID: 2662 RVA: 0x0003D9CC File Offset: 0x0003BBCC
        void デ\u30FCタを開くToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = Resources.OpenFileDialogTitle;
            openFileDialog.Filter = Resources.SpectrumFileFilter;
            openFileDialog.Multiselect = true;
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            foreach (string fileName in openFileDialog.FileNames)
            {
                DocEnergySpectrum docEnergySpectrum = this.documentManager.OpenDocument(fileName);
                if (docEnergySpectrum != null)
                {
                    docEnergySpectrum.DockAreas = DockAreas.Document;
                    this.SubscribeDocumentEvent(docEnergySpectrum);
                    docEnergySpectrum.Show(this.dockPanel1);
                    docEnergySpectrum.SetDefaultHorizontalScale();
                    this.ShowMeasurementResult(true);
                }
            }
        }

        // Token: 0x06000A67 RID: 2663 RVA: 0x0003DA18 File Offset: 0x0003BC18
        void OpenExistingDocument(string filename)
        {
            DocEnergySpectrum docEnergySpectrum = this.documentManager.OpenDocument(filename);
            if (docEnergySpectrum != null)
            {
                docEnergySpectrum.DockAreas = DockAreas.Document;
                this.SubscribeDocumentEvent(docEnergySpectrum);
                docEnergySpectrum.Show(this.dockPanel1);
                docEnergySpectrum.SetDefaultHorizontalScale();
                this.ShowMeasurementResult(true);
            }
        }

        // Token: 0x06000A68 RID: 2664 RVA: 0x0003DA64 File Offset: 0x0003BC64
        void dockPanel1_ActiveDocumentChanged(object sender, EventArgs e)
        {
            if (this.activeDocument != null && this.activeDocument.PulseDetector != null)
            {
                this.activeDocument.PulseDetector.PulseView = null;
                this.activeDocument.PulseDetector.NGPulseView = null;
                this.activeDocument.PulseDetector.DoUpdatePulseView = false;
            }
            this.activeDocument = (DocEnergySpectrum)this.dockPanel1.ActiveDocument;
            this.dcPulseView.PulseView.PulseShape = null;
            this.dcPulseView.NGPulseView.PulseShape = null;
            this.dcPulseView.PulseView.Invalidate();
            this.dcPulseView.NGPulseView.Invalidate();
            if (this.activeDocument != null)
            {
                this.activeDocument.IsActivating = true;
                this.dcControlPanel.ShowDocumentStatus();
                this.dcSampleInfoView.LoadFormContents();
                this.ShowMeasurementResult(true);
                this.ShowDoseRate();
                this.dcPeakDetectionView.ShowPeakDetectionResult();
                this.dcControlPanel.Enabled = true;
                this.dcSampleInfoView.Enabled = true;
                foreach (DCResultView dcresultView in this.dcResultViewList)
                {
                    dcresultView.Enabled = true;
                }
                this.dcSpectrumListView.Enabled = true;
                this.dcSpectrumListView.ShowSpectrumList(this.activeDocument);
                this.dcPeakDetectionView.Enabled = true;
                this.dcEnergyCalibrationView.SetEnergyCalibration(this.activeDocument.ActiveResultData.EnergySpectrum.EnergyCalibration, this.activeDocument.ActiveResultData.DeviceConfig.EnergyCalibration);
                this.dcEnergyCalibrationView.SetStabilizerState(this.activeDocument.ActiveResultData);
                this.dcEnergyCalibrationView.Enabled = true;
                this.activeDocument.ActiveEnergyCalibration = this.dcEnergyCalibrationView.Visible;
                foreach (DocEnergySpectrum docEnergySpectrum in this.documentManager.DocumentList)
                {
                    if (docEnergySpectrum != this.activeDocument)
                    {
                        docEnergySpectrum.ActiveEnergyCalibration = false;
                    }
                }
                if (this.activeDocument.PulseDetector != null)
                {
                    this.activeDocument.PulseDetector.PulseView = this.dcPulseView.PulseView;
                    this.activeDocument.PulseDetector.NGPulseView = this.dcPulseView.NGPulseView;
                    this.activeDocument.PulseDetector.DoUpdatePulseView = this.doUpdatePulseView;
                }
            }
            else
            {
                this.dcControlPanel.Enabled = false;
                this.dcSampleInfoView.Enabled = false;
                foreach (DCResultView dcresultView2 in this.dcResultViewList)
                {
                    dcresultView2.Enabled = false;
                }
                this.dcSpectrumListView.Enabled = false;
                this.dcPeakDetectionView.Enabled = false;
                this.dcEnergyCalibrationView.Enabled = false;
            }
            this.UpdateApplicationTitle();
            this.Refresh();
        }

        // Token: 0x06000A69 RID: 2665 RVA: 0x0003DDA4 File Offset: 0x0003BFA4
        public void ActiveResultDataChanged(int activeResultDataIndex)
        {
            this.dcPulseView.PulseView.PulseShape = null;
            this.dcPulseView.NGPulseView.PulseShape = null;
            this.dcPulseView.PulseView.Invalidate();
            this.dcPulseView.NGPulseView.Invalidate();
            this.activeDocument.ActiveResultDataIndex = activeResultDataIndex;
            this.activeDocument.UpdateEnergySpectrum();
            this.dcControlPanel.ShowDocumentStatus();
            this.dcSampleInfoView.LoadFormContents();
            this.ShowMeasurementResult(true);
            this.ShowDoseRate();
            this.dcPeakDetectionView.ShowPeakDetectionResult();
            this.dcEnergyCalibrationView.SetEnergyCalibration(this.activeDocument.ActiveResultData.EnergySpectrum.EnergyCalibration, this.activeDocument.ActiveResultData.DeviceConfig.EnergyCalibration);
            this.dcEnergyCalibrationView.SetStabilizerState(this.activeDocument.ActiveResultData);
            this.activeDocument.ActiveEnergyCalibration = this.dcEnergyCalibrationView.Visible;
            this.dcControlPanel.Enabled = true;
            this.dcSampleInfoView.Enabled = true;
            foreach (DCResultView dcresultView in this.dcResultViewList)
            {
                dcresultView.Enabled = true;
            }
            this.dcSpectrumListView.Enabled = true;
            this.UpdateApplicationTitle();
            this.Refresh();
        }

        // Token: 0x06000A6A RID: 2666 RVA: 0x0003DF18 File Offset: 0x0003C118
        void UpdateApplicationTitle()
        {
            string version = GlobalConfigManager.GetInstance().VersionString;
            if (this.activeDocument != null)
            {
                this.Text = Resources.ApplicationTitle + " " + version + " - " + Path.GetFileName(this.activeDocument.Filename);
                return;
            }
            this.Text = Resources.ApplicationTitle + " " + version;
        }

        // Token: 0x06000A6B RID: 2667 RVA: 0x0003DF58 File Offset: 0x0003C158
        public void UpdateSampleInfo()
        {
            this.dcSampleInfoView.LoadFormContents();
        }

        // Token: 0x06000A6C RID: 2668 RVA: 0x0003DF68 File Offset: 0x0003C168
        void activeDocument_MeasurementTerminated(object sender, EventArgs e)
        {
            this.dcControlPanel.ShowDocumentStatus();
        }

        // Token: 0x06000A6D RID: 2669 RVA: 0x0003DF78 File Offset: 0x0003C178
        public DeviceConfigForm ShowDeviceConfigForm(DeviceConfigInfo config)
        {
            if (this.deviceConfigForm == null || this.deviceConfigForm.IsDisposed)
            {
                this.deviceConfigForm = new DeviceConfigForm();
                this.deviceConfigForm.StartPosition = FormStartPosition.Manual;
                this.deviceConfigForm.Width = this.globalConfigManager.GlobalConfig.DeviceConfigFormWidth;
                this.deviceConfigForm.Height = this.globalConfigManager.GlobalConfig.DeviceConfigFormHeight;
                if (this.deviceConfigForm.Width < 640)
                {
                    this.deviceConfigForm.Width = 640;
                }
                if (this.deviceConfigForm.Height < 640)
                {
                    this.deviceConfigForm.Height = 640;
                }
                this.deviceConfigForm.Left = base.Left + (base.Width - this.deviceConfigForm.Width) / 2;
                this.deviceConfigForm.Top = base.Top + (base.Height - this.deviceConfigForm.Height) / 2;
                this.deviceConfigForm.Owner = this;
            }
            this.deviceConfigForm.ActiveDeviceConfig = config;
            this.deviceConfigForm.Show();
            this.deviceConfigForm.Activate();
            return this.deviceConfigForm;
        }

        public void addCalibration(int channel, decimal energy, int count)
        {
            this.dcEnergyCalibrationView.AddCalibration(channel, energy, count);
        }

        // Token: 0x06000A6E RID: 2670 RVA: 0x0003E0BC File Offset: 0x0003C2BC
        public DeviceConfigForm ShowDeviceConfigStabilizerForm(DeviceConfigInfo config)
        {
            DeviceConfigForm deviceConfigForm = this.ShowDeviceConfigForm(config);
            //deviceConfigForm.deviceConfigurationChanged += deviceConfigChanged;
            deviceConfigForm.ShowStabilizerForm(config);
            return deviceConfigForm;
        }

        // Token: 0x06000A6F RID: 2671 RVA: 0x0003E0E0 File Offset: 0x0003C2E0
        void rOI定義RToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ShowROIConfigForm(null);
        }

        // Token: 0x06000A70 RID: 2672 RVA: 0x0003E0EC File Offset: 0x0003C2EC
        public ROIConfigForm ShowROIConfigForm(ROIConfigData config)
        {
            if (this.roiConfigForm == null || this.roiConfigForm.IsDisposed)
            {
                this.roiConfigForm = new ROIConfigForm();
                this.roiConfigForm.StartPosition = FormStartPosition.Manual;
                this.roiConfigForm.Width = this.globalConfigManager.GlobalConfig.ROIConfigFormWidth;
                this.roiConfigForm.Height = this.globalConfigManager.GlobalConfig.ROIConfigFormHeight;
                if (this.roiConfigForm.Width < 640)
                {
                    this.roiConfigForm.Width = 640;
                }
                if (this.roiConfigForm.Height < 480)
                {
                    this.roiConfigForm.Height = 480;
                }
                this.roiConfigForm.Left = base.Left + (base.Width - this.roiConfigForm.Width) / 2;
                this.roiConfigForm.Top = base.Top + (base.Height - this.roiConfigForm.Height) / 2;
                this.roiConfigForm.Owner = this;
            }
            this.roiConfigForm.ActiveROIConfig = config;
            this.roiConfigForm.Show();
            this.roiConfigForm.Activate();
            return this.roiConfigForm;
        }

        // Token: 0x06000A71 RID: 2673 RVA: 0x0003E230 File Offset: 0x0003C430
        void 核種定義の編集NToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.ShowNuclideDefinitionForm();
        }

        void OpenConfigNToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("explorer.exe", userDirectoryConfig);
        }

        void NucDB_Click(object sender, EventArgs e)
        {
            ShowNucBaseView();
        }

        void ShowNucBaseView()
        {
            if (this.nucBaseView == null || this.nucBaseView.IsDisposed)
            {
                this.nucBaseView = new NucBase.NucBase(this);
                this.nucBaseView.Show();
            }
            else
            {
                this.nucBaseView.BringToFront();
            }
        }

        public void CallNucBaseSearch(decimal Energy)
        {
            ShowNucBaseView();
            this.nucBaseView.CallSearch(Energy);
        }

        // Token: 0x06000A72 RID: 2674 RVA: 0x0003E23C File Offset: 0x0003C43C
        public NuclideDefinitionForm ShowNuclideDefinitionForm()
        {
            if (this.nuclideDefinitionForm == null || this.nuclideDefinitionForm.IsDisposed)
            {
                this.nuclideDefinitionForm = new NuclideDefinitionForm();
                this.nuclideDefinitionForm.StartPosition = FormStartPosition.Manual;
                this.nuclideDefinitionForm.Left = base.Left + (base.Width - this.nuclideDefinitionForm.Width) / 2;
                this.nuclideDefinitionForm.Top = base.Top + (base.Height - this.nuclideDefinitionForm.Height) / 2;
                this.nuclideDefinitionForm.Owner = this;
            }
            this.nuclideDefinitionForm.Show();
            this.nuclideDefinitionForm.Activate();
            return this.nuclideDefinitionForm;
        }

        // Token: 0x06000A73 RID: 2675 RVA: 0x0003E2F4 File Offset: 0x0003C4F4
        Rectangle GetTotalBound()
        {
            int num = 0;
            int num2 = 0;
            int num3 = 0;
            int num4 = 0;
            foreach (Screen screen in Screen.AllScreens)
            {
                int x = screen.Bounds.X;
                int y = screen.Bounds.Y;
                int num5 = screen.Bounds.X + screen.Bounds.Width;
                int num6 = screen.Bounds.Y + screen.Bounds.Height;
                if (x < num)
                {
                    num = x;
                }
                if (y < num3)
                {
                    num3 = y;
                }
                if (num2 < num5)
                {
                    num2 = num5;
                }
                if (num4 < num6)
                {
                    num4 = num6;
                }
            }
            Rectangle result = new Rectangle(num, num3, num2 - num, num4 - num3);
            return result;
        }

        // Token: 0x06000A74 RID: 2676 RVA: 0x0003E3E0 File Offset: 0x0003C5E0
        void デ\u30FCタを閉じるCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.CloseActiveDocument();
        }

        void CloseAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int docCount = this.documentManager.DocumentList.Count;
            for (int i = 1; i <= docCount; i++)
            {
                this.CloseActiveDocument();
            }
        }

        void CombineSpectrasToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.activeDocument == null)
            {
                MessageBox.Show(Resources.CombineEmptySpectrum);
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = Resources.OpenFileDialogTitle;
            openFileDialog.Filter = Resources.SpectrumFileFilter;
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            DocumentManager combinedDocManager = new DocumentManager();
            DocEnergySpectrum combinedSpectrum = combinedDocManager.OpenDocument(openFileDialog.FileName);
            SpectrumAriphmetics sa = new SpectrumAriphmetics(this.activeDocument);
            this.activeDocument = sa.CombineWith(combinedSpectrum);
            sa.Dispose();
            this.activeDocument.Dirty = true;
            this.UpdateAllView();
            this.UpdateDetectedPeakView();
        }

        void ConcatSpectrums(DocEnergySpectrum docEnergySpectrum, int newChan)
        {
            CreateDocument();
            if (newChan < docEnergySpectrum.ActiveResultData.EnergySpectrum.NumberOfChannels)
            {
                this.activeDocument.ActiveResultData.EnergySpectrum = SpectrumAriphmetics.ConcatSpectrum(docEnergySpectrum.ActiveResultData.EnergySpectrum, newChan);
            } else
            {
                this.activeDocument.ActiveResultData.EnergySpectrum = SpectrumAriphmetics.RestoreSpectrum(docEnergySpectrum.ActiveResultData.EnergySpectrum, newChan);
            }
            
            this.activeDocument.ActiveResultData.DeviceConfigReference = null;
            this.activeDocument.ActiveResultData.DeviceConfig = new DeviceConfigInfo();
            if (docEnergySpectrum.ActiveResultData.BackgroundEnergySpectrum != null)
            {
                if (newChan < docEnergySpectrum.ActiveResultData.BackgroundEnergySpectrum.NumberOfChannels)
                {
                    this.activeDocument.ActiveResultData.BackgroundEnergySpectrum = SpectrumAriphmetics.ConcatSpectrum(docEnergySpectrum.ActiveResultData.BackgroundEnergySpectrum, newChan);
                } else
                {
                    this.activeDocument.ActiveResultData.BackgroundEnergySpectrum = SpectrumAriphmetics.RestoreSpectrum(docEnergySpectrum.ActiveResultData.BackgroundEnergySpectrum, newChan);
                }
                this.activeDocument.ActiveResultData.BackgroundSpectrumFile = docEnergySpectrum.ActiveResultData.BackgroundSpectrumFile;
            }
            this.activeDocument.ActiveResultData.ResultDataStatus = docEnergySpectrum.ActiveResultData.ResultDataStatus;
            this.activeDocument.ActiveResultData.PresetTime = docEnergySpectrum.ActiveResultData.PresetTime;
            this.activeDocument.ActiveResultData.EndTime = docEnergySpectrum.ActiveResultData.EndTime;
            this.activeDocument.ActiveResultData.PulseCollection = docEnergySpectrum.ActiveResultData.PulseCollection;
            this.activeDocument.ActiveResultData.SampleInfo = docEnergySpectrum.ActiveResultData.SampleInfo;
            this.activeDocument.ActiveResultData.StartTime = docEnergySpectrum.ActiveResultData.StartTime;

            this.activeDocument.Dirty = true;
            this.UpdateAllView();
            return;
        }

        // Token: 0x06000A75 RID: 2677 RVA: 0x0003E3E8 File Offset: 0x0003C5E8
        void CloseActiveDocument()
        {
            if (this.activeDocument != null)
            {
                this.StopRecordingOrTesting(this.activeDocument);
                this.DestroyVCPThreads(this.activeDocument);
                if (this.ConfirmSaveDocument(this.activeDocument))
                {
                    this.UnsubscribeDocumentEvent(this.activeDocument);
                    this.documentManager.CloseDocument(this.activeDocument);
                }
            }

            GC.Collect();
        }

        // Token: 0x06000A76 RID: 2678 RVA: 0x0003E440 File Offset: 0x0003C640
        public void CloseDocument(string filename, bool force)
        {
            foreach (DocEnergySpectrum docEnergySpectrum in this.documentManager.DocumentList)
            {
                if (docEnergySpectrum.Filename == filename)
                {
                    this.StopRecordingOrTesting(docEnergySpectrum);
                    this.DestroyVCPThreads(docEnergySpectrum);
                    if (force || this.ConfirmSaveDocument(docEnergySpectrum))
                    {
                        this.UnsubscribeDocumentEvent(docEnergySpectrum);
                        this.documentManager.CloseDocument(docEnergySpectrum);
                        break;
                    }
                    break;
                }
            }
        }

        // Token: 0x06000A77 RID: 2679 RVA: 0x0003E4DC File Offset: 0x0003C6DC
        bool ConfirmSaveDocument(DocEnergySpectrum doc)
        {
            if (doc.Dirty)
            {
                DialogResult dialogResult = MessageBox.Show(string.Format(BecquerelMonitor.Properties.Resources.MSGFileOverwriteConfirmation, Path.GetFileName(doc.Filename)), BecquerelMonitor.Properties.Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation);
                if (dialogResult == DialogResult.Cancel)
                {
                    return false;
                }
                if (dialogResult == DialogResult.Yes)
                {
                    if (!doc.IsNamed)
                    {
                        if (!this.documentManager.SaveDocumentWithName(doc))
                        {
                            return false;
                        }
                    }
                    else if (!this.documentManager.SaveDocument(doc))
                    {
                        return false;
                    }
                }
                doc.Dirty = false;
            }
            return true;
        }

        // Token: 0x06000A78 RID: 2680 RVA: 0x0003E564 File Offset: 0x0003C764
        void StopRecordingOrTesting(DocEnergySpectrum doc)
        {
            ResultData activeResultData = doc.ActiveResultData;
            ResultDataStatus resultDataStatus = activeResultData.ResultDataStatus;
            if (resultDataStatus.Recording)
            {
                doc.ActiveResultData.MeasurementController.StopRecording();
            }
            bool testing = resultDataStatus.Testing;
        }

        // Token: 0x06000A79 RID: 2681 RVA: 0x0003E5A8 File Offset: 0x0003C7A8
        void デ\u30FCタを保存SToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.SaveActiveDocument();
        }

        void AutoSaveStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.activeDocument != null)
            {
                if (this.activeDocument.AutoSave)
                {
                    this.activeDocument.AutoSave = false;
                } else
                {
                    this.activeDocument.AutoSave = this.SaveActiveDocument();
                }
            }
            this.スペクトルSToolStripMenuItem.ShowDropDown();
        }

        public bool SaveActiveDocument()
        {
            if (SaveDocument(this.activeDocument))
            {
                this.dcSampleInfoView.SaveFormContents();
                this.dcControlPanel.ShowDocumentStatus();
                this.UpdateApplicationTitle();
                return true;
            }
            this.UpdateApplicationTitle();
            return false;
        }

        // Token: 0x06000A7B RID: 2683 RVA: 0x0003E624 File Offset: 0x0003C824
        public bool SaveDocument(DocEnergySpectrum doc)
        {
            if (doc != null)
            {
                //this.StopRecordingOrTesting(doc);
                //this.DestroyVCPThreads(doc);
                if (!doc.IsNamed)
                {
                    return this.documentManager.SaveDocumentWithName(doc);
                }
                return this.documentManager.SaveDocument(doc);
            }
            return false;
        }

        // Token: 0x06000A7C RID: 2684 RVA: 0x0003E660 File Offset: 0x0003C860
        void デ\u30FCタを名前を付けて保存RToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.SaveDocumentWithName();
        }

        // Token: 0x06000A7D RID: 2685 RVA: 0x0003E668 File Offset: 0x0003C868
        public void SaveDocumentWithName()
        {
            if (this.activeDocument != null)
            {
                this.documentManager.SaveDocumentWithName(this.activeDocument);
                this.dcControlPanel.ShowDocumentStatus();
            }
            this.UpdateApplicationTitle();
        }

        // Token: 0x06000A7E RID: 2686 RVA: 0x0003E698 File Offset: 0x0003C898
        void ファイルFToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            bool enabled = this.activeDocument != null && this.activeDocument.Dirty;
            this.デ\u30FCタを保存SToolStripMenuItem.Enabled = enabled;
            this.デ\u30FCタを名前を付けて保存RToolStripMenuItem.Enabled = (this.activeDocument != null);
            this.デ\u30FCタを閉じるCToolStripMenuItem.Enabled = (this.activeDocument != null);
            this.CloseAllToolStripMenuItem.Enabled = (this.activeDocument != null);
            this.CombineSpectrasToolStripMenuItem.Enabled = (this.activeDocument != null);
        }

        // Token: 0x06000A7F RID: 2687 RVA: 0x0003E700 File Offset: 0x0003C900
        void スペクトルSToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
        {
            if (this.activeDocument == null)
            {
                this.新規スペクトルNToolStripMenuItem1.Enabled = false;
                this.削除DToolStripMenuItem.Enabled = false;
                this.既存ファイルから追加FToolStripMenuItem.Enabled = false;
                this.測定開始SToolStripMenuItem.Enabled = false;
                this.測定停止TToolStripMenuItem.Enabled = false;
                this.デ\u30FCタ消去CToolStripMenuItem.Enabled = false;
                this.ConcatSpectrumsStripMenuItem.Enabled = false;
                this.CutoffStripMenuItem.Enabled = false;
                this.AutoSaveStripMenuItem.Enabled = false;
                this.toolStripMenuItem1.Enabled = false;
                return;
            }
            bool enabled = this.activeDocument.ResultDataFile.ResultDataList.Count < this.globalConfigManager.MaximumSpectrumPerFile;
            this.新規スペクトルNToolStripMenuItem1.Enabled = enabled;
            this.既存ファイルから追加FToolStripMenuItem.Enabled = enabled;
            this.CombineSpectrasToolStripMenuItem.Enabled = enabled;
            this.ConcatSpectrumsStripMenuItem.Enabled = enabled;
            this.CutoffStripMenuItem.Enabled = enabled;
            this.AutoSaveStripMenuItem.Enabled = enabled;
            this.AutoSaveStripMenuItem.Checked = this.activeDocument.AutoSave;
            this.toolStripMenuItem1.Enabled = enabled;
            this.測定開始SToolStripMenuItem.Enabled = !this.activeDocument.ActiveResultData.ResultDataStatus.Recording;
            this.測定停止TToolStripMenuItem.Enabled = this.activeDocument.ActiveResultData.ResultDataStatus.Recording;
        }

        // Token: 0x06000A80 RID: 2688 RVA: 0x0003E7E4 File Offset: 0x0003C9E4
        void バ\u30FCジョン情報AToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new AboutForm
            {
                Owner = this
            }.ShowDialog();
        }

        void UpdatesAToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                ApplicationDeployment updateCheck = null;
                UpdateCheckInfo info = null;

                try
                {
                    updateCheck = ApplicationDeployment.CurrentDeployment;
                    info = updateCheck.CheckForDetailedUpdate();
                }
                catch
                {
                    MessageBox.Show(Resources.ERRUpdateApp);
                    return;
                }
                if (info.UpdateAvailable)
                {
                    DialogResult dialogResult = MessageBox.Show(String.Format(Resources.MSGUpdateLong,info.AvailableVersion.ToString()),
                        Resources.MSGUpdateShort, MessageBoxButtons.OKCancel);
                    if (dialogResult == DialogResult.OK)
                    {
                        updateCheck.Update();
                        MessageBox.Show(Resources.MSGRestartNeeded);
                        if (WineCheck.isWine())
                        {
                            Process cleanCache = new Process();
                            ProcessStartInfo cleanCacheInfo = new ProcessStartInfo();
                            cleanCacheInfo.WindowStyle = ProcessWindowStyle.Hidden;
                            cleanCacheInfo.FileName = "Rundll32.exe";
                            cleanCacheInfo.Arguments = "dfshim CleanOnlineAppCache";
                            cleanCache.StartInfo = cleanCacheInfo;
                            cleanCache.Start();
                        }
                        Application.Restart();
                    }
                }
                else
                {
                    MessageBox.Show(String.Format(Resources.MSGNoNewVersion, updateCheck.CurrentVersion.ToString()));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(String.Format(Resources.ERRUpdateExc, ex.Message));
            }
        }

        // Token: 0x06000A81 RID: 2689 RVA: 0x0003E80C File Offset: 0x0003CA0C
        void cSVCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.activeDocument != null)
            {
                this.documentManager.ExportDocumentToCsv(this.activeDocument);
            }
        }

        void ECSVToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.activeDocument != null)
            {
                this.documentManager.ExportDocumentToECSV(this.activeDocument,
                    this.activeDocument.EnergySpectrumView.BackgroundMode,
                    this.activeDocument.EnergySpectrumView.SmoothingMethod);
            }
        }

        // Token: 0x06000A82 RID: 2690 RVA: 0x0003E82C File Offset: 0x0003CA2C
        void SubscribeDocumentEvent(DocEnergySpectrum doc)
        {
            if (doc == null)
            {
                return;
            }
            doc.FormClosing += this.DocEnergySpectrum_FormClosing;
            doc.FormClosed += this.DocEnergySpectrum_FormClosed;
            doc.SaveDocument += this.DocEnergySpectrum_SaveDocument;
            doc.CloseDocument += this.DocEnergySpectrum_CloseDocument;
            doc.CreateNewROI += this.DocEnergySpectrum_CreateNewROI;
            doc.SetLowerThreshold += this.DocEnergySpectrum_SetLowerThreshold;
            doc.SetUpperThreshold += this.DocEnergySpectrum_SetUpperThreshold;
            doc.ShowEnergyCalibrationView += this.DocEnergySpectrum_ShowEnergyCalibrationView;
            doc.AddSpectrumToDocument += this.DocEnergySpectrum_AddSpectrumToDocument;
            foreach (ResultData resultData in doc.ResultDataFile.ResultDataList)
            {
                MeasurementController measurementController = resultData.MeasurementController;
                measurementController.MeasurementTerminated += this.activeDocument_MeasurementTerminated;
            }
        }

        // Token: 0x06000A83 RID: 2691 RVA: 0x0003E948 File Offset: 0x0003CB48
        void UnsubscribeDocumentEvent(DocEnergySpectrum doc)
        {
            if (doc == null)
            {
                return;
            }
            doc.FormClosing -= this.DocEnergySpectrum_FormClosing;
            doc.FormClosed -= this.DocEnergySpectrum_FormClosed;
            doc.SaveDocument -= this.DocEnergySpectrum_SaveDocument;
            doc.CloseDocument -= this.DocEnergySpectrum_CloseDocument;
            doc.CreateNewROI -= this.DocEnergySpectrum_CreateNewROI;
            doc.SetLowerThreshold -= this.DocEnergySpectrum_SetLowerThreshold;
            doc.SetUpperThreshold -= this.DocEnergySpectrum_SetUpperThreshold;
            doc.ShowEnergyCalibrationView -= this.DocEnergySpectrum_ShowEnergyCalibrationView;
            doc.AddSpectrumToDocument -= this.DocEnergySpectrum_AddSpectrumToDocument;
            foreach (ResultData resultData in doc.ResultDataFile.ResultDataList)
            {
                MeasurementController measurementController = resultData.MeasurementController;
                measurementController.MeasurementTerminated -= this.activeDocument_MeasurementTerminated;
            }
        }

        // Token: 0x06000A84 RID: 2692 RVA: 0x0003EA64 File Offset: 0x0003CC64
        void DocEnergySpectrum_CreateNewROI(object sender, EventArgs e)
        {
            DocEnergySpectrum docEnergySpectrum = (DocEnergySpectrum)sender;
            ResultData activeResultData = docEnergySpectrum.ActiveResultData;
            ROIConfigForm roiconfigForm = this.ShowROIConfigForm(activeResultData.ROIConfig);
            int selectionStart = docEnergySpectrum.EnergySpectrumView.SelectionStart;
            int selectionEnd = docEnergySpectrum.EnergySpectrumView.SelectionEnd;
            EnergyCalibration energyCalibration = activeResultData.EnergySpectrum.EnergyCalibration;
            int num;
            int num2;
            if (selectionStart < selectionEnd)
            {
                num = selectionStart;
                num2 = selectionEnd;
            }
            else
            {
                num = selectionEnd;
                num2 = selectionStart;
            }
            int num3 = (int)Math.Floor(energyCalibration.ChannelToEnergy((double)num));
            int num4 = (int)Math.Ceiling(energyCalibration.ChannelToEnergy((double)num2));
            roiconfigForm.CreateNewROIWithRegion(activeResultData.ROIConfig, (double)num3, (double)num4);
        }

        // Token: 0x06000A85 RID: 2693 RVA: 0x0003EB08 File Offset: 0x0003CD08
        void DocEnergySpectrum_SetLowerThreshold(object sender, EventArgs e)
        {
            DocEnergySpectrum docEnergySpectrum = (DocEnergySpectrum)sender;
            ResultData activeResultData = docEnergySpectrum.ActiveResultData;
            DeviceConfigInfo deviceConfig = activeResultData.DeviceConfig;
            DeviceConfigForm deviceConfigForm = this.ShowDeviceConfigForm(deviceConfig);
            int cursorChannel = docEnergySpectrum.EnergySpectrumView.CursorChannel;
            double threshold = (double)cursorChannel * activeResultData.DeviceConfig.ChannelPitch;
            deviceConfigForm.SetLowerThreshold(deviceConfig, threshold);
        }

        // Token: 0x06000A86 RID: 2694 RVA: 0x0003EB60 File Offset: 0x0003CD60
        void DocEnergySpectrum_SetUpperThreshold(object sender, EventArgs e)
        {
            DocEnergySpectrum docEnergySpectrum = (DocEnergySpectrum)sender;
            ResultData activeResultData = docEnergySpectrum.ActiveResultData;
            DeviceConfigInfo deviceConfig = activeResultData.DeviceConfig;
            DeviceConfigForm deviceConfigForm = this.ShowDeviceConfigForm(deviceConfig);
            int cursorChannel = docEnergySpectrum.EnergySpectrumView.CursorChannel;
            double threshold = (double)cursorChannel * activeResultData.DeviceConfig.ChannelPitch;
            deviceConfigForm.SetUpperThreshold(deviceConfig, threshold);
        }

        void DestroyVCPThreads (DocEnergySpectrum docEnergySpectrum)
        {
            if (docEnergySpectrum.ActiveResultData.MeasurementController.DeviceController is AtomSpectraDeviceController)
            {
                string guid = docEnergySpectrum.ActiveResultData.DeviceConfig.Guid;
                int documents_with_same_device_config_guid = 0;
                foreach (DocEnergySpectrum doc in this.documentManager.DocumentList)
                {
                    if (guid.Equals(doc.ActiveResultData.DeviceConfig.Guid) && doc.ActiveResultData.MeasurementController.DeviceController != null)
                    {
                        documents_with_same_device_config_guid++;
                    }
                }
                if (documents_with_same_device_config_guid < 2)
                {
                    AtomSpectraVCPIn.cleanUp(docEnergySpectrum.ActiveResultData.DeviceConfig.Guid);
                }
            }
        }

        // Token: 0x06000A87 RID: 2695 RVA: 0x0003EBB8 File Offset: 0x0003CDB8
        void DocEnergySpectrum_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!(sender is DocEnergySpectrum))
            {
                return;
            }
            DocEnergySpectrum docEnergySpectrum = (DocEnergySpectrum)sender;
            this.StopRecordingOrTesting(docEnergySpectrum);
            this.DestroyVCPThreads(docEnergySpectrum);
            if (!this.ConfirmSaveDocument(docEnergySpectrum))
            {
                e.Cancel = true;
                return;
            }
            docEnergySpectrum.FormClosing -= this.DocEnergySpectrum_FormClosing;
        }

        // Token: 0x06000A88 RID: 2696 RVA: 0x0003EC0C File Offset: 0x0003CE0C
        void DocEnergySpectrum_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (!(sender is DocEnergySpectrum))
            {
                return;
            }
            DocEnergySpectrum docEnergySpectrum = (DocEnergySpectrum)sender;
            if (!this.mainFormClosing)
            {
                //this.dockPanel1.DocumentStyle = 
                this.documentManager.DocumentList.Remove(docEnergySpectrum);
                GC.Collect();
            }
            docEnergySpectrum.FormClosed -= this.DocEnergySpectrum_FormClosed;
        }

        // Token: 0x06000A89 RID: 2697 RVA: 0x0003EC64 File Offset: 0x0003CE64
        void DocEnergySpectrum_SaveDocument(object sender, EventArgs e)
        {
            this.SaveActiveDocument();
        }

        // Token: 0x06000A8A RID: 2698 RVA: 0x0003EC6C File Offset: 0x0003CE6C
        void DocEnergySpectrum_CloseDocument(object sender, EventArgs e)
        {
            this.CloseActiveDocument();
        }

        // Token: 0x06000A8B RID: 2699 RVA: 0x0003EC74 File Offset: 0x0003CE74
        void DocEnergySpectrum_ShowEnergyCalibrationView(object sender, ShowEnergyCalibrationViewEventArgs e)
        {
            this.ShowEnergyCalibrationView(e.Visible);
        }

        // Token: 0x06000A8C RID: 2700 RVA: 0x0003EC84 File Offset: 0x0003CE84
        void DocEnergySpectrum_AddSpectrumToDocument(object sender, AddSpectrumToDocumentEventArgs e)
        {
            if (!(sender is DocEnergySpectrum))
            {
                return;
            }
            DocEnergySpectrum docEnergySpectrum = (DocEnergySpectrum)sender;
            foreach (string pathname in e.Pathnames)
            {
                ResultDataFile resultDataFile = this.documentManager.LoadDocument(docEnergySpectrum, pathname);
                foreach (ResultData resultData in resultDataFile.ResultDataList)
                {
                    if (docEnergySpectrum.ResultDataFile.ResultDataList.Count >= this.globalConfigManager.MaximumSpectrumPerFile)
                    {
                        break;
                    }
                    docEnergySpectrum.ResultDataFile.ResultDataList.Add(resultData);
                    resultData.MeasurementController = new MeasurementController(docEnergySpectrum, resultData);
                }
            }
            if (docEnergySpectrum == this.activeDocument)
            {
                this.dcSpectrumListView.ShowSpectrumList(docEnergySpectrum);
                docEnergySpectrum.UpdateEnergySpectrum();
            }
        }

        // Token: 0x06000A8D RID: 2701 RVA: 0x0003ED80 File Offset: 0x0003CF80
        public void AddNewSpectrum(DocEnergySpectrum doc)
        {
            ResultData resultData = doc.CreateResultData();
            resultData.MeasurementController.MeasurementTerminated += this.activeDocument_MeasurementTerminated;
            doc.Dirty = true;
            resultData.BackgroundSpectrumFile = Path.GetFileName(resultData.DeviceConfig.BackgroundSpectrumPathname);
            resultData.BackgroundSpectrumPathname = resultData.DeviceConfig.BackgroundSpectrumPathname;
            this.documentManager.LoadBackgroundSpectrum(resultData);
            if (doc == this.activeDocument)
            {
                this.dcSampleInfoView.LoadFormContents();
                this.dcControlPanel.ShowDocumentStatus();
                this.dcEnergyCalibrationView.SetEnergyCalibration(this.activeDocument.ActiveResultData.EnergySpectrum.EnergyCalibration, this.activeDocument.ActiveResultData.DeviceConfig.EnergyCalibration);
                this.dcEnergyCalibrationView.SetStabilizerState(this.activeDocument.ActiveResultData);
                this.dcEnergyCalibrationView.Enabled = true;
                doc.UpdateEnergySpectrum();
                this.ShowMeasurementResult(true);
                this.dcSpectrumListView.ShowSpectrumList(doc);
            }
        }

        public void SetStatusTextLeft(string Text)
        {
            this.toolStripStatusLabel1.Text = Text;
        }

        public void SetStatusTextRight(string Text)
        {
            this.toolStripStatusLabel3.Text = Text;
        }

        public void ClearStatusTextLeft()
        {
            this.toolStripStatusLabel1.Text = "";
        }

        public void ClearStatusTextRight()
        {
            this.toolStripStatusLabel3.Text = "";
        }

        // Token: 0x06000A8E RID: 2702 RVA: 0x0003EE7C File Offset: 0x0003D07C
        public void DeleteActiveSpectrum(DocEnergySpectrum doc)
        {
            if (doc.ResultDataFile.ResultDataList.Count < 2)
            {
                MessageBox.Show(BecquerelMonitor.Properties.Resources.ERRCannotDeleteSpectrum, BecquerelMonitor.Properties.Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return;
            }
            ResultData activeResultData = doc.ActiveResultData;
            string name = activeResultData.SampleInfo.Name;
            string text;
            if (name == null || name == "")
            {
                text = BecquerelMonitor.Properties.Resources.DeleteUnnamedSpectrumMessage;
            }
            else
            {
                text = string.Format(BecquerelMonitor.Properties.Resources.DeleteSpectrumMessage, name);
            }
            DialogResult dialogResult = MessageBox.Show(text, BecquerelMonitor.Properties.Resources.ConfirmationDialogTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
            if (dialogResult != DialogResult.OK)
            {
                return;
            }
            doc.DeleteActiveResultData();
            doc.Dirty = true;
            if (doc == this.activeDocument)
            {
                this.dcSampleInfoView.LoadFormContents();
                this.dcControlPanel.ShowDocumentStatus();
                doc.UpdateEnergySpectrum();
                this.ShowMeasurementResult(true);
                this.dcSpectrumListView.ShowSpectrumList(doc);
            }
        }

        // Token: 0x06000A8F RID: 2703 RVA: 0x0003EF58 File Offset: 0x0003D158
        public void UpdateAllView()
        {
            DocEnergySpectrum docEnergySpectrum = this.activeDocument;
            this.dcSampleInfoView.LoadFormContents();
            this.dcControlPanel.ShowDocumentStatus();
            docEnergySpectrum.UpdateEnergySpectrum();
            this.ShowMeasurementResult(true);
            this.dcSpectrumListView.ShowSpectrumList(docEnergySpectrum);
            this.UpdateApplicationTitle();
            this.UpdateEnergyCalibrationView();
            if (docEnergySpectrum.EnergySpectrumView.PeakMode == PeakMode.Visible)
            {
                UpdateDetectedPeakView();
            }
        }

        // Token: 0x06000A90 RID: 2704 RVA: 0x0003EFA0 File Offset: 0x0003D1A0
        public void LoadSpectrumFromFile(DocEnergySpectrum doc)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = BecquerelMonitor.Properties.Resources.ImportSpectraFromFileDialogTitle;
            openFileDialog.Filter = BecquerelMonitor.Properties.Resources.SpectrumFileFilter;
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            openFileDialog.Multiselect = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            bool flag = false;
            foreach (string pathname in openFileDialog.FileNames)
            {
                ResultDataFile resultDataFile = this.documentManager.LoadDocument(doc, pathname);
                foreach (ResultData resultData in resultDataFile.ResultDataList)
                {
                    if (doc.ResultDataFile.ResultDataList.Count >= this.globalConfigManager.MaximumSpectrumPerFile)
                    {
                        flag = true;
                        break;
                    }
                    doc.ResultDataFile.ResultDataList.Add(resultData);
                    resultData.MeasurementController.MeasurementTerminated += this.activeDocument_MeasurementTerminated;
                    doc.Dirty = true;
                    resultData.MeasurementController = new MeasurementController(doc, resultData);
                }
                if (flag)
                {
                    break;
                }
            }
            if (doc == this.activeDocument)
            {
                this.dcSpectrumListView.ShowSpectrumList(doc);
                doc.UpdateEnergySpectrum();
            }
        }

        // Token: 0x06000A91 RID: 2705 RVA: 0x0003F0F4 File Offset: 0x0003D2F4
        public void SaveSpectrumToFile(DocEnergySpectrum doc)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = BecquerelMonitor.Properties.Resources.ExportSpectraToFileDialogTitle;
            saveFileDialog.Filter = BecquerelMonitor.Properties.Resources.SpectrumFileFilter;
            saveFileDialog.FilterIndex = 1;
            saveFileDialog.RestoreDirectory = true;
            if (saveFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            string fileName = saveFileDialog.FileName;
            foreach (DocEnergySpectrum docEnergySpectrum in this.documentManager.DocumentList)
            {
                if (docEnergySpectrum.IsNamed && docEnergySpectrum.Filename == fileName && doc.Filename != fileName)
                {
                    MessageBox.Show(BecquerelMonitor.Properties.Resources.ERRCannotOverwrite, BecquerelMonitor.Properties.Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                    return;
                }
            }
            ResultDataFile resultDataFile = new ResultDataFile();
            foreach (ResultData resultData in doc.ResultDataFile.ResultDataList)
            {
                if (resultData.Selected)
                {
                    resultDataFile.ResultDataList.Add(resultData);
                }
            }
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                using (FileStream fileStream = new FileStream(fileName, FileMode.Create))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(ResultDataFile));
                    xmlSerializer.Serialize(fileStream, resultDataFile);
                }
                Cursor.Current = Cursors.Default;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(BecquerelMonitor.Properties.Resources.ERRFileSaveFailure, fileName, ex.Message));
            }
        }

        // Token: 0x06000A92 RID: 2706 RVA: 0x0003F2C0 File Offset: 0x0003D4C0
        void cSVファイルCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = Resources.CsvImportDialogTitle;
            openFileDialog.Filter = Resources.CsvFileFilter;
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            if (this.activeDocument == null)
            {
                CreateDocument();
            }

            int presetTime = this.dcControlPanel.PresetTime;
            this.documentManager.ImportCsvToDocument(this.activeDocument, presetTime, openFileDialog.FileName);
            this.UpdateAllView();
        }

        // Token: 0x06000A93 RID: 2707 RVA: 0x0003F300 File Offset: 0x0003D500
        void 基本設定BToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.globalConfigForm == null || this.globalConfigForm.IsDisposed)
            {
                this.globalConfigForm = new GlobalConfigForm(this);
                this.globalConfigForm.StartPosition = FormStartPosition.Manual;
                this.globalConfigForm.Left = base.Left + (base.Width - this.globalConfigForm.Width) / 2;
                this.globalConfigForm.Top = base.Top + (base.Height - this.globalConfigForm.Height) / 2;
                this.globalConfigForm.Owner = this;
            }
            GlobalConfigInfo globalConfigInfo = this.globalConfigManager.GlobalConfig;
            this.globalConfigForm.LoadFormContents(globalConfigInfo);
            this.globalConfigForm.Show();
            this.globalConfigForm.Activate();
        }

        // Token: 0x06000A94 RID: 2708 RVA: 0x0003F3CC File Offset: 0x0003D5CC
        void manager_DeviceConfigChanged(object sender, DeviceConfigChangedEventArgs e)
        {
            EasyControlConfig easyControlConfig = this.globalConfigManager.GlobalConfig.EasyControlConfig;
            if (easyControlConfig.DeviceConfigReference != null)
            {
                easyControlConfig.DeviceConfig = this.deviceConfigManager.DeviceConfigMap[easyControlConfig.DeviceConfigReference.Guid];
            }
        }

        // Token: 0x06000A95 RID: 2709 RVA: 0x0003F41C File Offset: 0x0003D61C
        void manager_ROIConfigListChanged(object sender, EventArgs e)
        {
            EasyControlConfig easyControlConfig = this.globalConfigManager.GlobalConfig.EasyControlConfig;
            if (easyControlConfig.ROIConfigReference != null)
            {
                easyControlConfig.ROIConfig = this.roiConfigManager.ROIConfigMap[easyControlConfig.ROIConfigReference.Guid];
            }
            foreach (DocEnergySpectrum docEnergySpectrum in this.documentManager.DocumentList)
            {
                foreach (ResultData resultData in docEnergySpectrum.ResultDataFile.ResultDataList)
                {
                    string guid = resultData.ROIConfigReference.Guid;
                    if (guid != null && this.roiConfigManager.ROIConfigMap.ContainsKey(guid))
                    {
                        resultData.ROIConfig = this.roiConfigManager.ROIConfigMap[guid];
                        resultData.ROIConfigReference = resultData.ROIConfig.CreateReference();
                    }
                    docEnergySpectrum.UpdateEnergySpectrum();
                }
            }
            this.ShowMeasurementResult(true);
        }

        // Token: 0x06000A96 RID: 2710 RVA: 0x0003F560 File Offset: 0x0003D760
        void ベクモニ旧形式v093bToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DocEnergySpectrum docEnergySpectrum = this.documentManager.ImportDocument093b();
            if (docEnergySpectrum != null)
            {
                docEnergySpectrum.DockAreas = DockAreas.Document;
                this.SubscribeDocumentEvent(docEnergySpectrum);
                docEnergySpectrum.Show(this.dockPanel1);
                this.ShowMeasurementResult(true);
            }
        }

        void CreateDocument()
        {
            DocEnergySpectrum docEnergySpectrum = this.documentManager.CreateDocument();
            if (docEnergySpectrum != null)
            {
                docEnergySpectrum.DockAreas = DockAreas.Document;
                this.SubscribeDocumentEvent(docEnergySpectrum);
                docEnergySpectrum.Show(this.dockPanel1);
                docEnergySpectrum.SetDefaultHorizontalScale();
                this.ShowMeasurementResult(true);
            }
        }

        void AtomSpectraStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = Resources.ImportAtomSpectraFileDialogTitle;
            openFileDialog.Filter = Resources.AtomSpectraFileFilter;
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            if (this.activeDocument == null)
            {
                CreateDocument();
            }

            this.documentManager.ImportDocumentAtomSpectra(this.activeDocument, openFileDialog.FileName);
            this.activeDocument.Dirty = true;
            this.UpdateAllView();
        }

        void N42StripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = Resources.ImportN42FileDialogTitle;
            openFileDialog.Filter = Resources.N42FileFilter;
            openFileDialog.FilterIndex = 1;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            if (this.activeDocument == null)
            {
                CreateDocument();
            }

            this.documentManager.ImportDocumentN42(this.activeDocument, openFileDialog.FileName);
            this.activeDocument.Dirty = true;
            this.UpdateAllView();
        }

        void N42ExpStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.activeDocument != null)
            {
                this.documentManager.ExportDocumentN42(this.activeDocument);
                this.UpdateAllView();
            }
        }

        void AtomSpectraExpStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.activeDocument != null)
            {
                this.documentManager.ExportDocumentAtomSpectra(this.activeDocument);
                this.UpdateAllView();
            }
        }

        // Token: 0x06000A97 RID: 2711 RVA: 0x0003F5A8 File Offset: 0x0003D7A8
        void ROIConfigManager_ROIConfigListChanged(object sender, EventArgs e)
        {
            this.ShowMeasurementResult(true);
        }

        // Token: 0x06000A98 RID: 2712 RVA: 0x0003F5B4 File Offset: 0x0003D7B4
        void dcResultView_FormClosed(object sender, FormClosedEventArgs e)
        {
        }

        // Token: 0x06000A99 RID: 2713 RVA: 0x0003F5B8 File Offset: 0x0003D7B8
        public void RefreshAllView()
        {
            this.dcSampleInfoView.LoadFormContents();
            this.ShowMeasurementResult(true);
            if (this.dcEnergyCalibrationView != null)
            {
                this.dcEnergyCalibrationView.UpdateEnergyCalibrationConfig();
                this.dcEnergyCalibrationView.LoadCalibrationPoints();
            }
            foreach (DocEnergySpectrum docEnergySpectrum in this.documentManager.DocumentList)
            {
                docEnergySpectrum.EnergySpectrumView.RecalcChartParameters();
                docEnergySpectrum.EnergySpectrumView.PrepareViewData();
                docEnergySpectrum.EnergySpectrumView.RecalcScrollBar();
                docEnergySpectrum.EnergySpectrumView.Invalidate();
            }
        }

        // Token: 0x06000A9A RID: 2714 RVA: 0x0003F650 File Offset: 0x0003D850
        void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] array = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string path in array)
                {
                    if (Path.GetExtension(path) != ".xml")
                    {
                        return;
                    }
                    if (!File.Exists(path))
                    {
                        return;
                    }
                }
                e.Effect = DragDropEffects.Copy;
            }
        }

        // Token: 0x06000A9B RID: 2715 RVA: 0x0003F6D8 File Offset: 0x0003D8D8
        void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] array = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (array.Length == 1)
                {
                    string filename = array[0];
                    this.OpenExistingDocument(filename);
                    return;
                }
                if (array.Length > 1)
                {
                    foreach (string text in array)
                    {
                    }
                }
            }
        }

        // Token: 0x06000A9C RID: 2716 RVA: 0x0003F754 File Offset: 0x0003D954
        void マニュアルMToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start("https://t.me/software_kbradar");
        }

        // Token: 0x06000A9D RID: 2717 RVA: 0x0003F764 File Offset: 0x0003D964
        void 新規スペクトルNToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            this.AddNewSpectrum(this.activeDocument);
        }

        // Token: 0x06000A9E RID: 2718 RVA: 0x0003F774 File Offset: 0x0003D974
        void 削除DToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.DeleteActiveSpectrum(this.activeDocument);
        }

        // Token: 0x06000A9F RID: 2719 RVA: 0x0003F784 File Offset: 0x0003D984
        void ファイルからスペクトルを追加FToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.LoadSpectrumFromFile(this.activeDocument);
        }

        // Token: 0x06000AA0 RID: 2720 RVA: 0x0003F794 File Offset: 0x0003D994
        void toolStripMenuItem1_Click(object sender, EventArgs e)
        {
            this.SaveSpectrumToFile(this.activeDocument);
        }

        void ConcatSpectrumsStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.activeDocument == null)
            {
                return;
            }

            using (ChanNumberChangeDialog dialog = new ChanNumberChangeDialog(this))
            {
                dialog.ShowDialog();
                int newChan = dialog.SendData();
                if (newChan > 64 && newChan != this.activeDocument.ActiveResultData.EnergySpectrum.NumberOfChannels)
                {
                    ConcatSpectrums(this.activeDocument, newChan);
                }
                else
                {
                    MessageBox.Show(Resources.ERRChanNumber);
                }
            }            
        }

        void CutoffStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.activeDocument == null)
            {
                return;
            }

            using (SpectrumCutOffDialog dialog = new SpectrumCutOffDialog(this))
            {
                dialog.ShowDialog();
                (bool resultstatus, bool isEnergy, double energyVal, int channel) = dialog.SendData();
                if (!resultstatus)
                {
                    return;
                }
                if (energyVal == 0.0 && channel == 0)
                {
                    MessageBox.Show(Resources.ERRChanNumber);
                    return;
                }
                if (!isEnergy && channel >= this.activeDocument.ActiveResultData.EnergySpectrum.NumberOfChannels)
                {
                    MessageBox.Show(Resources.ERRChanNumber);
                    return;
                }
                if (isEnergy)
                {
                    PolynomialEnergyCalibration calibration = (PolynomialEnergyCalibration)this.activeDocument.ActiveResultData.EnergySpectrum.EnergyCalibration;
                    int chan = (int)calibration.EnergyToChannel(energyVal);
                    if (chan >= this.activeDocument.ActiveResultData.EnergySpectrum.NumberOfChannels)
                    {
                        MessageBox.Show(Resources.ERRChanNumber);
                        return;
                    }
                }
                Cutoff(this.activeDocument, isEnergy, energyVal: energyVal, channel: channel);
            }
        }

        void Cutoff(DocEnergySpectrum docEnergySpectrum, bool isEnergy, double energyVal = 0.0, int channel = 0)
        {
            CreateDocument();
            this.activeDocument.ActiveResultData.EnergySpectrum = SpectrumAriphmetics.Cutoff(docEnergySpectrum.ActiveResultData.EnergySpectrum, isEnergy: isEnergy, energyVal: energyVal, channel: channel);
            this.activeDocument.ActiveResultData.DeviceConfigReference = null;
            this.activeDocument.ActiveResultData.DeviceConfig = new DeviceConfigInfo();
            if (docEnergySpectrum.ActiveResultData.BackgroundEnergySpectrum != null)
            {
                if (isEnergy)
                {
                    this.activeDocument.ActiveResultData.BackgroundEnergySpectrum = SpectrumAriphmetics.CutoffSpectrumChannels(docEnergySpectrum.ActiveResultData.BackgroundEnergySpectrum, this.activeDocument.ActiveResultData.EnergySpectrum.NumberOfChannels);
                } else
                {
                    this.activeDocument.ActiveResultData.BackgroundEnergySpectrum = SpectrumAriphmetics.Cutoff(docEnergySpectrum.ActiveResultData.BackgroundEnergySpectrum, isEnergy: isEnergy, energyVal: energyVal, channel: channel);
                }
            }
            this.activeDocument.ActiveResultData.ResultDataStatus = docEnergySpectrum.ActiveResultData.ResultDataStatus;
            this.activeDocument.ActiveResultData.PresetTime = docEnergySpectrum.ActiveResultData.PresetTime;
            this.activeDocument.ActiveResultData.EndTime = docEnergySpectrum.ActiveResultData.EndTime;
            this.activeDocument.ActiveResultData.PulseCollection = docEnergySpectrum.ActiveResultData.PulseCollection;
            this.activeDocument.ActiveResultData.SampleInfo = docEnergySpectrum.ActiveResultData.SampleInfo;
            this.activeDocument.ActiveResultData.StartTime = docEnergySpectrum.ActiveResultData.StartTime;

            this.activeDocument.Dirty = true;
            this.UpdateAllView();
            return;
        }

        // Token: 0x06000AA1 RID: 2721 RVA: 0x0003F7A4 File Offset: 0x0003D9A4
        void 測定開始SToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.dcControlPanel.StartMeasurement();
        }

        // Token: 0x06000AA2 RID: 2722 RVA: 0x0003F7B4 File Offset: 0x0003D9B4
        void 測定停止TToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.dcControlPanel.StopMeasurement();
        }

        // Token: 0x06000AA3 RID: 2723 RVA: 0x0003F7C4 File Offset: 0x0003D9C4
        void デ\u30FCタ消去CToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.dcControlPanel.ClearMeasurementResult();
        }

        // Token: 0x06000AA4 RID: 2724 RVA: 0x0003F7D4 File Offset: 0x0003D9D4
        void fWHM用ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ResultData activeResultData = this.activeDocument.ActiveResultData;
            EnergySpectrum energySpectrum = activeResultData.EnergySpectrum;
            string text = "fwhm.txt";
            try
            {
                int num = (int)energySpectrum.EnergyCalibration.EnergyToChannel(530.0);
                int num2 = (int)energySpectrum.EnergyCalibration.EnergyToChannel(780.0);
                double num3 = (double)energySpectrum.Spectrum[num] / energySpectrum.MeasurementTime;
                double num4 = (double)energySpectrum.Spectrum[num2] / energySpectrum.MeasurementTime;
                using (StreamWriter streamWriter = new StreamWriter(text, false, Encoding.GetEncoding(932)))
                {
                    for (int i = 0; i < energySpectrum.NumberOfChannels; i++)
                    {
                        double num5 = energySpectrum.EnergyCalibration.ChannelToEnergy((double)i);
                        if (num5 >= 627.0 && num5 <= 780.0)
                        {
                            double num6 = (double)energySpectrum.Spectrum[i] / energySpectrum.MeasurementTime;
                            num6 -= (double)(num2 - i) / (double)(num2 - num) * (num3 - num4);
                            streamWriter.WriteLine(num5 + " " + num6);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(BecquerelMonitor.Properties.Resources.ERRFileSaveFailure, text, ex.Message));
            }
        }

        // Token: 0x06000AA5 RID: 2725 RVA: 0x0003F960 File Offset: 0x0003DB60
        void toolStripMenuItem2_Click(object sender, EventArgs e)
        {
            this.OutputAnalysisReport();
        }

        // Token: 0x06000AA6 RID: 2726 RVA: 0x0003F968 File Offset: 0x0003DB68
        public void OutputAnalysisReport()
        {
        }

        // Token: 0x06000AA7 RID: 2727 RVA: 0x0003F96C File Offset: 0x0003DB6C
        void dockPanel1_ContentRemoved(object sender, DockContentEventArgs e)
        {
            if (e.Content is DCResultView)
            {
                DCResultView item = (DCResultView)e.Content;
                this.dcResultViewList.Remove(item);
            }
        }

        // Token: 0x06000AA8 RID: 2728 RVA: 0x0003F9A8 File Offset: 0x0003DBA8
        void oSDefaultToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.globalConfigManager.GlobalConfig.Language = "OS";
            this.UpdateLanguageCheckState();
            MessageBox.Show(BecquerelMonitor.Properties.Resources.RestartRequiredMessage);
        }

        // Token: 0x06000AA9 RID: 2729 RVA: 0x0003F9D0 File Offset: 0x0003DBD0
        void neutralToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.globalConfigManager.GlobalConfig.Language = "";
            this.UpdateLanguageCheckState();
            MessageBox.Show(BecquerelMonitor.Properties.Resources.RestartRequiredMessage);
        }

        // Token: 0x06000AAA RID: 2730 RVA: 0x0003F9F8 File Offset: 0x0003DBF8
        void jaJPToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.globalConfigManager.GlobalConfig.Language = "ru-RU";
            this.UpdateLanguageCheckState();
            MessageBox.Show(BecquerelMonitor.Properties.Resources.RestartRequiredMessage);
        }

        // Token: 0x06000AAB RID: 2731 RVA: 0x0003FA20 File Offset: 0x0003DC20
        void UpdateLanguageCheckState()
        {
            foreach (object obj in this.languageToolStripMenuItem.DropDownItems)
            {
                ToolStripMenuItem toolStripMenuItem = (ToolStripMenuItem)obj;
                toolStripMenuItem.Checked = false;
            }
            string language = this.globalConfigManager.GlobalConfig.Language;
            if (language == "ru-RU")
            {
                this.jaJPToolStripMenuItem.Checked = true;
                return;
            }
            if (language == "")
            {
                this.neutralToolStripMenuItem.Checked = true;
                return;
            }
            this.oSDefaultToolStripMenuItem.Checked = true;
        }

        // Token: 0x06000AAC RID: 2732 RVA: 0x0003FAE0 File Offset: 0x0003DCE0
        void UpdateLayoutCheckState()
        {
            foreach (object obj in this.toolStripMenuItem7.DropDownItems)
            {
                ToolStripMenuItem toolStripMenuItem = (ToolStripMenuItem)obj;
                toolStripMenuItem.Checked = false;
            }
            if (this.layoutMode == LayoutMode.UserMode)
            {
                this.userLayoutToolStripMenuItem.Checked = true;
                return;
            }
            if (this.layoutMode == LayoutMode.ExpertMode)
            {
                this.expertLayoutToolStripMenuItem.Checked = true;
            }
        }

        // Token: 0x06000AAD RID: 2733 RVA: 0x0003FB78 File Offset: 0x0003DD78
        public void UpdateDeviceConfigForm()
        {
            if (this.deviceConfigForm != null && !this.deviceConfigForm.IsDisposed)
            {
                this.deviceConfigForm.UpdateModifiedConfigFile();
            }
        }

        // Token: 0x06000AAE RID: 2734 RVA: 0x0003FBA0 File Offset: 0x0003DDA0
        void userLayoutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.layoutMode == LayoutMode.UserMode)
            {
                return;
            }
            string text = this.LayoutConfigFile(this.layoutMode);
            try
            {
                this.dockPanel1.SaveAsXml(text);
            }
            catch (Exception)
            {
            }
            this.layoutMode = LayoutMode.UserMode;
            this.UpdateLayoutCheckState();
            text = this.LayoutConfigFile(this.layoutMode);
            this.dockPanel1.SuspendLayout(true);
            this.CloseAllDocuments();
            this.InitializeToolViews();
            if (File.Exists(text))
            {
                this.dockPanel1.LoadFromXml(text, this.m_deserializeDockContent);
            }
            this.dockPanel1.ResumeLayout(true, true);
        }

        // Token: 0x06000AAF RID: 2735 RVA: 0x0003FC4C File Offset: 0x0003DE4C
        void expertLayoutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.layoutMode == LayoutMode.ExpertMode)
            {
                return;
            }
            string text = this.LayoutConfigFile(this.layoutMode);
            try
            {
                this.dockPanel1.SaveAsXml(text);
            }
            catch (Exception)
            {
            }
            this.layoutMode = LayoutMode.ExpertMode;
            this.UpdateLayoutCheckState();
            text = this.LayoutConfigFile(this.layoutMode);
            this.dockPanel1.SuspendLayout(true);
            this.CloseAllDocuments();
            this.InitializeToolViews();
            if (File.Exists(text))
            {
                this.dockPanel1.LoadFromXml(text, this.m_deserializeDockContent);
            }
            this.dockPanel1.ResumeLayout(true, true);
        }

        // Token: 0x06000AB0 RID: 2736 RVA: 0x0003FCF8 File Offset: 0x0003DEF8
        string LayoutConfigFile(LayoutMode mode)
        {
            string str = "ExpertMode.xml";
            return userDirectoryLayout + str;
        }

        // Token: 0x06000AB1 RID: 2737 RVA: 0x0003FD20 File Offset: 0x0003DF20
        void CloseAllDocuments()
        {
            if (this.dockPanel1.DocumentStyle == DocumentStyle.SystemMdi)
            {
                foreach (Form form in base.MdiChildren)
                {
                    form.Close();
                }
                return;
            }
            for (int j = this.dockPanel1.Contents.Count - 1; j >= 0; j--)
            {
                if (this.dockPanel1.Contents[j] != null)
                {
                    IDockContent dockContent = this.dockPanel1.Contents[j];
                    dockContent.DockHandler.Close();
                }
            }
        }

        public List<DocEnergySpectrum> DocumentList
        {
            get
            {
                return this.documentManager.DocumentList;
            }
        }

        // Token: 0x040005C7 RID: 1479
        LayoutMode layoutMode;

        // Token: 0x040005C8 RID: 1480
        DeserializeDockContent m_deserializeDockContent;

        // Token: 0x040005C9 RID: 1481
        DocumentManager documentManager = DocumentManager.GetInstance();

        // Token: 0x040005CA RID: 1482
        DeviceConfigManager deviceConfigManager;

        // Token: 0x040005CB RID: 1483
        ROIConfigManager roiConfigManager;

        // Token: 0x040005CC RID: 1484
        MeasurementResultManager resultManager;

        // Token: 0x040005CD RID: 1485
        DoseRateManager doseRateManager;

        // Token: 0x040005CE RID: 1486
        GlobalConfigManager globalConfigManager;

        // Token: 0x040005CF RID: 1487
        StartupForm startupForm;

        // Token: 0x040005D0 RID: 1488
        DeviceConfigForm deviceConfigForm;

        // Token: 0x040005D1 RID: 1489
        ROIConfigForm roiConfigForm;

        // Token: 0x040005D2 RID: 1490
        NuclideDefinitionForm nuclideDefinitionForm;

        // Token: 0x040005D3 RID: 1491
        GlobalConfigForm globalConfigForm;

        // Token: 0x040005D4 RID: 1492
        DCControlPanel dcControlPanel;

        // Token: 0x040005D5 RID: 1493
        DCDebugPanel dcDebugPanel;

        // Token: 0x040005D6 RID: 1494
        DCPulseView dcPulseView;

        // Token: 0x040005D7 RID: 1495
        DCSampleInfoView dcSampleInfoView;

        // Token: 0x040005D8 RID: 1496
        DCSpectrumListView dcSpectrumListView;

        // Token: 0x040005D9 RID: 1497
        DCSpectrumExplorerView dcSpectrumExplorerView;

        // Token: 0x040005DA RID: 1498
        DCPeakDetectionView dcPeakDetectionView;

        // Token: 0x040005DB RID: 1499
        DCStatusMessageView dcStatusMessageView;

        // Token: 0x040005DC RID: 1500
        DCEnergyCalibrationView dcEnergyCalibrationView;

        // Token: 0x040005DD RID: 1501
        DCDoseRateView dcDoseRateView;

        NucBase.NucBase nucBaseView;

        // Token: 0x040005DE RID: 1502
        List<DCResultView> dcResultViewList = new List<DCResultView>();

        // Token: 0x040005DF RID: 1503
        DocEnergySpectrum activeDocument;

        // Token: 0x040005E0 RID: 1504
        System.Windows.Forms.Timer timer;

        // Token: 0x040005E1 RID: 1505
        bool doUpdatePulseView = true;

        // Token: 0x040005E2 RID: 1506
        double defaultEnergyScale = 1.0;

        // Token: 0x040005E3 RID: 1507
        bool mainFormClosing;

        // Token: 0x040005E4 RID: 1508
        bool initialized;

        // Token: 0x040005E5 RID: 1509
        GlobalConfigInfo globalConfig;

        // Token: 0x040005E6 RID: 1510
        int count2000;

        // Token: 0x040005E7 RID: 1511
        int count1000;

        // Token: 0x040005E8 RID: 1512
        int count500;

        // Token: 0x040005E9 RID: 1513
        int count200;

        // Token: 0x040005EA RID: 1514
        int countChart;

        int countAutoSave;

        string userDirectory = Package.GetInstance().UserDirectory;

        string userDirectoryConfig = Package.GetInstance().Config;

        string userDirectoryLayout = Package.GetInstance().Layout;

        string OpenFileName;
    }
}
