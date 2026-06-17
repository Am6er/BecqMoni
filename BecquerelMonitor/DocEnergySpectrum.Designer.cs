using System.Windows.Forms;

namespace BecquerelMonitor
{
	// Token: 0x0200003C RID: 60
	public partial class DocEnergySpectrum : global::WeifenLuo.WinFormsUI.Docking.DockContent
	{
		// Token: 0x060002F8 RID: 760 RVA: 0x0000DA14 File Offset: 0x0000BC14
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x060002F9 RID: 761 RVA: 0x0000DA3C File Offset: 0x0000BC3C
		void InitializeComponent()
		{
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DocEnergySpectrum));
            this.toolStripContainer1 = new System.Windows.Forms.ToolStripContainer();
            this.toolStrip1 = new BecquerelMonitor.ToolStripEx();
            this.toolStripLabel1 = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSplitButton1 = new System.Windows.Forms.ToolStripSplitButton();
            this.countsViewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.cpsViewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSplitButton2 = new System.Windows.Forms.ToolStripSplitButton();
            this.linearViewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.powToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.logarithmicViewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripNumericUpdown = new BecquerelMonitor.ToolStripNumericUpdown();
            this.toolStripSplitButton5 = new System.Windows.Forms.ToolStripSplitButton();
            this.noneToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.autoFitToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.autoFitBgToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabel3 = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSplitButton4 = new System.Windows.Forms.ToolStripSplitButton();
            this.channelViewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.energyViewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripButton2 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton1 = new System.Windows.Forms.ToolStripButton();
            this.toolStripButton3 = new System.Windows.Forms.ToolStripButton();
            this.toolStripNumUpDownScale = new BecquerelMonitor.ToolStripNumericUpdown();
            this.toolStripSplitButton10 = new System.Windows.Forms.ToolStripSplitButton();
            this.showEnergyCalibrationToolToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.hideEnergyCalibrationToolToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStrip2 = new BecquerelMonitor.ToolStripEx();
            this.toolStripLabel2 = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSplitButtonBgMode = new System.Windows.Forms.ToolStripSplitButton();
            this.showBackgroundToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.hideBackgroundToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.SubstractBgToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.ShowConToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.NormByEffToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSplitButton8 = new System.Windows.Forms.ToolStripSplitButton();
            this.highDefinitionViewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.normalViewToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSplitButton3 = new System.Windows.Forms.ToolStripSplitButton();
            this.barGraphToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.lineGraphToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSplitButton6 = new System.Windows.Forms.ToolStripSplitButton();
            this.noSmoothingToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.simpleMovingAverageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.weightedMovingAverageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSplitButton9 = new System.Windows.Forms.ToolStripSplitButton();
            this.showPeaksToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.hidePeaksToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripScreenShotButton = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator4 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabel4 = new System.Windows.Forms.ToolStripLabel();
            this.toolStripSaveButton = new System.Windows.Forms.ToolStripButton();
            this.toolStripRefreshBgButton = new System.Windows.Forms.ToolStripButton();
            this.toolStripSplitButton = new System.Windows.Forms.ToolStripButton();
            this.energySpectrumView1 = new System.Windows.Forms.Panel();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
            this.showAllChannelsAToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.toolStripMenuItem2 = new System.Windows.Forms.ToolStripMenuItem();
            this.setLowerThresholdLToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.setUpperThresholdHToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripMenuItem3 = new System.Windows.Forms.ToolStripMenuItem();
            this.createRoiDefinitionFromSelectionSToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.saveSToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.closeCToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.toolStripContainer1.BottomToolStripPanel.SuspendLayout();
            this.toolStripContainer1.ContentPanel.SuspendLayout();
            this.toolStripContainer1.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this.toolStrip2.SuspendLayout();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // toolStripContainer1
            // 
            // 
            // toolStripContainer1.BottomToolStripPanel
            // 
            this.toolStripContainer1.BottomToolStripPanel.Controls.Add(this.toolStrip1);
            this.toolStripContainer1.BottomToolStripPanel.Controls.Add(this.toolStrip2);
            this.toolStripContainer1.BottomToolStripPanel.Click += new System.EventHandler(this.toolStripContainer1_BottomToolStripPanel_Click);
            // 
            // toolStripContainer1.ContentPanel
            // 
            this.toolStripContainer1.ContentPanel.Controls.Add(this.energySpectrumView1);
            resources.ApplyResources(this.toolStripContainer1.ContentPanel, "toolStripContainer1.ContentPanel");
            resources.ApplyResources(this.toolStripContainer1, "toolStripContainer1");
            // 
            // toolStripContainer1.LeftToolStripPanel
            // 
            this.toolStripContainer1.LeftToolStripPanel.Click += new System.EventHandler(this.toolStripContainer1_LeftToolStripPanel_Click);
            this.toolStripContainer1.Name = "toolStripContainer1";
            // 
            // toolStripContainer1.RightToolStripPanel
            // 
            this.toolStripContainer1.RightToolStripPanel.Click += new System.EventHandler(this.toolStripContainer1_RightToolStripPanel_Click);
            // 
            // toolStripContainer1.TopToolStripPanel
            // 
            this.toolStripContainer1.TopToolStripPanel.Click += new System.EventHandler(this.toolStripContainer1_TopToolStripPanel_Click);
            // 
            // toolStrip1
            // 
            this.toolStrip1.ClickThrough = true;
            resources.ApplyResources(this.toolStrip1, "toolStrip1");
            this.toolStrip1.ImageScalingSize = new System.Drawing.Size(18, 18);
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripLabel1,
            this.toolStripSplitButton1,
            this.toolStripSplitButton2,
            this.toolStripNumericUpdown,
            this.toolStripSplitButton5,
            this.toolStripSeparator3,
            this.toolStripLabel3,
            this.toolStripSplitButton4,
            this.toolStripButton2,
            this.toolStripButton1,
            this.toolStripButton3,
            this.toolStripNumUpDownScale,
            this.toolStripSplitButton10});
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.SuppressHighlighting = false;
            // 
            // toolStripLabel1
            // 
            this.toolStripLabel1.Name = "toolStripLabel1";
            resources.ApplyResources(this.toolStripLabel1, "toolStripLabel1");
            // 
            // toolStripSplitButton1
            // 
            this.toolStripSplitButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.countsViewToolStripMenuItem,
            this.cpsViewToolStripMenuItem});
            this.toolStripSplitButton1.Image = global::BecquerelMonitor.Properties.Resources.cps;
            resources.ApplyResources(this.toolStripSplitButton1, "toolStripSplitButton1");
            this.toolStripSplitButton1.Name = "toolStripSplitButton1";
            this.toolStripSplitButton1.ButtonClick += new System.EventHandler(this.toolStripSplitButton1_ButtonClick);
            this.toolStripSplitButton1.DropDownOpening += new System.EventHandler(this.toolStripSplitButton1_DropDownOpening);
            // 
            // countsViewToolStripMenuItem
            // 
            this.countsViewToolStripMenuItem.Name = "countsViewToolStripMenuItem";
            resources.ApplyResources(this.countsViewToolStripMenuItem, "countsViewToolStripMenuItem");
            this.countsViewToolStripMenuItem.Click += new System.EventHandler(this.countsViewToolStripMenuItem_Click);
            // 
            // cpsViewToolStripMenuItem
            // 
            this.cpsViewToolStripMenuItem.Name = "cpsViewToolStripMenuItem";
            resources.ApplyResources(this.cpsViewToolStripMenuItem, "cpsViewToolStripMenuItem");
            this.cpsViewToolStripMenuItem.Click += new System.EventHandler(this.cpsViewToolStripMenuItem_Click);
            // 
            // toolStripSplitButton2
            // 
            this.toolStripSplitButton2.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton2.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.linearViewToolStripMenuItem,
            this.powToolStripMenuItem,
            this.logarithmicViewToolStripMenuItem});
            this.toolStripSplitButton2.Image = global::BecquerelMonitor.Properties.Resources.log;
            resources.ApplyResources(this.toolStripSplitButton2, "toolStripSplitButton2");
            this.toolStripSplitButton2.Name = "toolStripSplitButton2";
            this.toolStripSplitButton2.ButtonClick += new System.EventHandler(this.toolStripSplitButton2_ButtonClick);
            this.toolStripSplitButton2.DropDownOpening += new System.EventHandler(this.toolStripSplitButton2_DropDownOpening);
            // 
            // linearViewToolStripMenuItem
            // 
            this.linearViewToolStripMenuItem.Name = "linearViewToolStripMenuItem";
            resources.ApplyResources(this.linearViewToolStripMenuItem, "linearViewToolStripMenuItem");
            this.linearViewToolStripMenuItem.Click += new System.EventHandler(this.linearViewToolStripMenuItem_Click);
            // 
            // powToolStripMenuItem
            // 
            this.powToolStripMenuItem.Name = "powToolStripMenuItem";
            resources.ApplyResources(this.powToolStripMenuItem, "powToolStripMenuItem");
            this.powToolStripMenuItem.Click += new System.EventHandler(this.powToolStripMenuItem_Click);
            // 
            // logarithmicViewToolStripMenuItem
            // 
            this.logarithmicViewToolStripMenuItem.Name = "logarithmicViewToolStripMenuItem";
            resources.ApplyResources(this.logarithmicViewToolStripMenuItem, "logarithmicViewToolStripMenuItem");
            this.logarithmicViewToolStripMenuItem.Click += new System.EventHandler(this.logarithmicViewToolStripMenuItem_Click);
            // 
            // toolStripNumericUpdown
            // 
            this.toolStripNumericUpdown.DecimalPlaces = 3;
            resources.ApplyResources(this.toolStripNumericUpdown, "toolStripNumericUpdown");
            this.toolStripNumericUpdown.Increment = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            this.toolStripNumericUpdown.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.toolStripNumericUpdown.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.toolStripNumericUpdown.Name = "toolStripNumericUpdown";
            this.toolStripNumericUpdown.Value = new decimal(new int[] {
            4000,
            0,
            0,
            196608});
            this.toolStripNumericUpdown.ValueChanged += new System.EventHandler(this.toolStripNumericUpdown_ValueChanged);
            this.toolStripNumericUpdown.KeyDown += new System.Windows.Forms.KeyEventHandler(this.ToolStripNumericUpdown_KeyDown);
            // 
            // toolStripSplitButton5
            // 
            this.toolStripSplitButton5.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton5.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.noneToolStripMenuItem,
            this.autoFitToolStripMenuItem,
            this.autoFitBgToolStripMenuItem});
            this.toolStripSplitButton5.Image = global::BecquerelMonitor.Properties.Resources.fit1;
            resources.ApplyResources(this.toolStripSplitButton5, "toolStripSplitButton5");
            this.toolStripSplitButton5.Name = "toolStripSplitButton5";
            this.toolStripSplitButton5.ButtonClick += new System.EventHandler(this.toolStripSplitButton5_ButtonClick);
            this.toolStripSplitButton5.DropDownOpening += new System.EventHandler(this.toolStripSplitButton5_DropDownOpening);
            // 
            // noneToolStripMenuItem
            // 
            this.noneToolStripMenuItem.Name = "noneToolStripMenuItem";
            resources.ApplyResources(this.noneToolStripMenuItem, "noneToolStripMenuItem");
            this.noneToolStripMenuItem.Click += new System.EventHandler(this.noneToolStripMenuItem_Click);
            // 
            // autoFitToolStripMenuItem
            // 
            this.autoFitToolStripMenuItem.Name = "autoFitToolStripMenuItem";
            resources.ApplyResources(this.autoFitToolStripMenuItem, "autoFitToolStripMenuItem");
            this.autoFitToolStripMenuItem.Click += new System.EventHandler(this.autoFitToolStripMenuItem_Click);
            // 
            // autoFitBgToolStripMenuItem
            // 
            this.autoFitBgToolStripMenuItem.Name = "autoFitBgToolStripMenuItem";
            resources.ApplyResources(this.autoFitBgToolStripMenuItem, "autoFitBgToolStripMenuItem");
            this.autoFitBgToolStripMenuItem.Click += new System.EventHandler(this.autoFitBgToolStripMenuItem_Click);
            // 
            // toolStripSeparator3
            // 
            this.toolStripSeparator3.Name = "toolStripSeparator3";
            resources.ApplyResources(this.toolStripSeparator3, "toolStripSeparator3");
            // 
            // toolStripLabel3
            // 
            this.toolStripLabel3.Name = "toolStripLabel3";
            resources.ApplyResources(this.toolStripLabel3, "toolStripLabel3");
            // 
            // toolStripSplitButton4
            // 
            this.toolStripSplitButton4.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton4.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.channelViewToolStripMenuItem,
            this.energyViewToolStripMenuItem});
            this.toolStripSplitButton4.Image = global::BecquerelMonitor.Properties.Resources.ene;
            resources.ApplyResources(this.toolStripSplitButton4, "toolStripSplitButton4");
            this.toolStripSplitButton4.Name = "toolStripSplitButton4";
            this.toolStripSplitButton4.ButtonClick += new System.EventHandler(this.toolStripSplitButton4_ButtonClick);
            this.toolStripSplitButton4.DropDownOpening += new System.EventHandler(this.toolStripSplitButton4_DropDownOpening);
            // 
            // channelViewToolStripMenuItem
            // 
            this.channelViewToolStripMenuItem.Name = "channelViewToolStripMenuItem";
            resources.ApplyResources(this.channelViewToolStripMenuItem, "channelViewToolStripMenuItem");
            this.channelViewToolStripMenuItem.Click += new System.EventHandler(this.channelViewToolStripMenuItem_Click);
            // 
            // energyViewToolStripMenuItem
            // 
            this.energyViewToolStripMenuItem.Name = "energyViewToolStripMenuItem";
            resources.ApplyResources(this.energyViewToolStripMenuItem, "energyViewToolStripMenuItem");
            this.energyViewToolStripMenuItem.Click += new System.EventHandler(this.energyViewToolStripMenuItem_Click);
            // 
            // toolStripButton2
            // 
            this.toolStripButton2.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton2.Image = global::BecquerelMonitor.Properties.Resources.ZoominSelection;
            resources.ApplyResources(this.toolStripButton2, "toolStripButton2");
            this.toolStripButton2.Name = "toolStripButton2";
            this.toolStripButton2.Click += new System.EventHandler(this.toolStripButton2_Click);
            // 
            // toolStripButton1
            // 
            this.toolStripButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton1.Image = global::BecquerelMonitor.Properties.Resources.fit2;
            resources.ApplyResources(this.toolStripButton1, "toolStripButton1");
            this.toolStripButton1.Name = "toolStripButton1";
            this.toolStripButton1.Click += new System.EventHandler(this.toolStripButton1_Click);
            // 
            // toolStripButton3
            // 
            this.toolStripButton3.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton3.Image = global::BecquerelMonitor.Properties.Resources.scale;
            resources.ApplyResources(this.toolStripButton3, "toolStripButton3");
            this.toolStripButton3.Name = "toolStripButton3";
            this.toolStripButton3.Click += new System.EventHandler(this.toolStripButton3_Click);
            // 
            // toolStripNumUpDownScale
            // 
            this.toolStripNumUpDownScale.DecimalPlaces = 3;
            this.toolStripNumUpDownScale.Increment = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.toolStripNumUpDownScale.Maximum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.toolStripNumUpDownScale.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.toolStripNumUpDownScale.Name = "toolStripNumUpDownScale";
            resources.ApplyResources(this.toolStripNumUpDownScale, "toolStripNumUpDownScale");
            this.toolStripNumUpDownScale.Value = new decimal(new int[] {
            1000,
            0,
            0,
            196608});
            this.toolStripNumUpDownScale.ValueChanged += new System.EventHandler(this.toolStripNumUpDownScale_ValueChanged);
            this.toolStripNumUpDownScale.KeyDown += new System.Windows.Forms.KeyEventHandler(this.toolStripNumUpDownScale_KeyDown);
            // 
            // toolStripSplitButton10
            // 
            this.toolStripSplitButton10.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton10.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.showEnergyCalibrationToolToolStripMenuItem,
            this.hideEnergyCalibrationToolToolStripMenuItem});
            this.toolStripSplitButton10.Image = global::BecquerelMonitor.Properties.Resources.EnergyCalibration;
            resources.ApplyResources(this.toolStripSplitButton10, "toolStripSplitButton10");
            this.toolStripSplitButton10.Name = "toolStripSplitButton10";
            this.toolStripSplitButton10.ButtonClick += new System.EventHandler(this.toolStripSplitButton10_ButtonClick);
            this.toolStripSplitButton10.DropDownOpening += new System.EventHandler(this.toolStripSplitButton10_DropDownOpening);
            // 
            // showEnergyCalibrationToolToolStripMenuItem
            // 
            this.showEnergyCalibrationToolToolStripMenuItem.Name = "showEnergyCalibrationToolToolStripMenuItem";
            resources.ApplyResources(this.showEnergyCalibrationToolToolStripMenuItem, "showEnergyCalibrationToolToolStripMenuItem");
            this.showEnergyCalibrationToolToolStripMenuItem.Click += new System.EventHandler(this.showEnergyCalibrationToolToolStripMenuItem_Click);
            // 
            // hideEnergyCalibrationToolToolStripMenuItem
            // 
            this.hideEnergyCalibrationToolToolStripMenuItem.Name = "hideEnergyCalibrationToolToolStripMenuItem";
            resources.ApplyResources(this.hideEnergyCalibrationToolToolStripMenuItem, "hideEnergyCalibrationToolToolStripMenuItem");
            this.hideEnergyCalibrationToolToolStripMenuItem.Click += new System.EventHandler(this.hideEnergyCalibrationToolToolStripMenuItem_Click);
            // 
            // toolStrip2
            // 
            this.toolStrip2.ClickThrough = true;
            resources.ApplyResources(this.toolStrip2, "toolStrip2");
            this.toolStrip2.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripLabel2,
            this.toolStripSplitButtonBgMode,
            this.toolStripSplitButton8,
            this.toolStripSplitButton3,
            this.toolStripSplitButton6,
            this.toolStripSplitButton9,
            this.toolStripScreenShotButton,
            this.toolStripSeparator4,
            this.toolStripLabel4,
            this.toolStripSaveButton,
            this.toolStripRefreshBgButton,
            this.toolStripSplitButton});
            this.toolStrip2.Name = "toolStrip2";
            this.toolStrip2.SuppressHighlighting = false;
            // 
            // toolStripLabel2
            // 
            this.toolStripLabel2.Name = "toolStripLabel2";
            resources.ApplyResources(this.toolStripLabel2, "toolStripLabel2");
            // 
            // toolStripSplitButtonBgMode
            // 
            this.toolStripSplitButtonBgMode.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButtonBgMode.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.showBackgroundToolStripMenuItem,
            this.hideBackgroundToolStripMenuItem,
            this.SubstractBgToolStripMenuItem,
            this.ShowConToolStripMenuItem,
            this.NormByEffToolStripMenuItem});
            this.toolStripSplitButtonBgMode.Image = global::BecquerelMonitor.Properties.Resources.BG;
            resources.ApplyResources(this.toolStripSplitButtonBgMode, "toolStripSplitButtonBgMode");
            this.toolStripSplitButtonBgMode.Name = "toolStripSplitButtonBgMode";
            this.toolStripSplitButtonBgMode.ButtonClick += new System.EventHandler(this.toolStripSplitButton7_ButtonClick);
            this.toolStripSplitButtonBgMode.DropDownOpening += new System.EventHandler(this.toolStripSplitButton7_DropDownOpening);
            // 
            // showBackgroundToolStripMenuItem
            // 
            this.showBackgroundToolStripMenuItem.Name = "showBackgroundToolStripMenuItem";
            resources.ApplyResources(this.showBackgroundToolStripMenuItem, "showBackgroundToolStripMenuItem");
            this.showBackgroundToolStripMenuItem.Click += new System.EventHandler(this.showBackgroundToolStripMenuItem_Click);
            // 
            // hideBackgroundToolStripMenuItem
            // 
            this.hideBackgroundToolStripMenuItem.Name = "hideBackgroundToolStripMenuItem";
            resources.ApplyResources(this.hideBackgroundToolStripMenuItem, "hideBackgroundToolStripMenuItem");
            this.hideBackgroundToolStripMenuItem.Click += new System.EventHandler(this.hideBackgroundToolStripMenuItem_Click);
            // 
            // SubstractBgToolStripMenuItem
            // 
            this.SubstractBgToolStripMenuItem.Name = "SubstractBgToolStripMenuItem";
            resources.ApplyResources(this.SubstractBgToolStripMenuItem, "SubstractBgToolStripMenuItem");
            this.SubstractBgToolStripMenuItem.Click += new System.EventHandler(this.SubstractBgToolStripMenuItem_Click);
            // 
            // ShowConToolStripMenuItem
            // 
            this.ShowConToolStripMenuItem.Name = "ShowConToolStripMenuItem";
            resources.ApplyResources(this.ShowConToolStripMenuItem, "ShowConToolStripMenuItem");
            this.ShowConToolStripMenuItem.Click += new System.EventHandler(this.ShowConToolStripMenuItem_Click);
            // 
            // NormByEffToolStripMenuItem
            // 
            resources.ApplyResources(this.NormByEffToolStripMenuItem, "NormByEffToolStripMenuItem");
            this.NormByEffToolStripMenuItem.Name = "NormByEffToolStripMenuItem";
            this.NormByEffToolStripMenuItem.Click += new System.EventHandler(this.NormByEffToolStripMenuItem_Click);
            // 
            // toolStripSplitButton8
            // 
            this.toolStripSplitButton8.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton8.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.highDefinitionViewToolStripMenuItem,
            this.normalViewToolStripMenuItem});
            this.toolStripSplitButton8.Image = global::BecquerelMonitor.Properties.Resources.HD;
            resources.ApplyResources(this.toolStripSplitButton8, "toolStripSplitButton8");
            this.toolStripSplitButton8.Name = "toolStripSplitButton8";
            this.toolStripSplitButton8.ButtonClick += new System.EventHandler(this.toolStripSplitButton8_ButtonClick);
            this.toolStripSplitButton8.DropDownOpening += new System.EventHandler(this.toolStripSplitButton8_DropDownOpening);
            // 
            // highDefinitionViewToolStripMenuItem
            // 
            this.highDefinitionViewToolStripMenuItem.Name = "highDefinitionViewToolStripMenuItem";
            resources.ApplyResources(this.highDefinitionViewToolStripMenuItem, "highDefinitionViewToolStripMenuItem");
            this.highDefinitionViewToolStripMenuItem.Click += new System.EventHandler(this.highDefinitionViewToolStripMenuItem_Click);
            // 
            // normalViewToolStripMenuItem
            // 
            this.normalViewToolStripMenuItem.Name = "normalViewToolStripMenuItem";
            resources.ApplyResources(this.normalViewToolStripMenuItem, "normalViewToolStripMenuItem");
            this.normalViewToolStripMenuItem.Click += new System.EventHandler(this.normalViewToolStripMenuItem_Click);
            // 
            // toolStripSplitButton3
            // 
            this.toolStripSplitButton3.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton3.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.barGraphToolStripMenuItem,
            this.lineGraphToolStripMenuItem});
            this.toolStripSplitButton3.Image = global::BecquerelMonitor.Properties.Resources.line;
            resources.ApplyResources(this.toolStripSplitButton3, "toolStripSplitButton3");
            this.toolStripSplitButton3.Name = "toolStripSplitButton3";
            this.toolStripSplitButton3.ButtonClick += new System.EventHandler(this.toolStripSplitButton3_ButtonClick);
            this.toolStripSplitButton3.DropDownOpening += new System.EventHandler(this.toolStripSplitButton3_DropDownOpening);
            // 
            // barGraphToolStripMenuItem
            // 
            this.barGraphToolStripMenuItem.Name = "barGraphToolStripMenuItem";
            resources.ApplyResources(this.barGraphToolStripMenuItem, "barGraphToolStripMenuItem");
            this.barGraphToolStripMenuItem.Click += new System.EventHandler(this.barGraphToolStripMenuItem_Click);
            // 
            // lineGraphToolStripMenuItem
            // 
            this.lineGraphToolStripMenuItem.Name = "lineGraphToolStripMenuItem";
            resources.ApplyResources(this.lineGraphToolStripMenuItem, "lineGraphToolStripMenuItem");
            this.lineGraphToolStripMenuItem.Click += new System.EventHandler(this.lineGraphToolStripMenuItem_Click);
            // 
            // toolStripSplitButton6
            // 
            this.toolStripSplitButton6.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton6.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.noSmoothingToolStripMenuItem,
            this.simpleMovingAverageToolStripMenuItem,
            this.weightedMovingAverageToolStripMenuItem});
            this.toolStripSplitButton6.Image = global::BecquerelMonitor.Properties.Resources.NoSmooth;
            resources.ApplyResources(this.toolStripSplitButton6, "toolStripSplitButton6");
            this.toolStripSplitButton6.Name = "toolStripSplitButton6";
            this.toolStripSplitButton6.ButtonClick += new System.EventHandler(this.toolStripSplitButton6_ButtonClick);
            this.toolStripSplitButton6.DropDownOpening += new System.EventHandler(this.toolStripSplitButton6_DropDownOpening);
            // 
            // noSmoothingToolStripMenuItem
            // 
            this.noSmoothingToolStripMenuItem.Name = "noSmoothingToolStripMenuItem";
            resources.ApplyResources(this.noSmoothingToolStripMenuItem, "noSmoothingToolStripMenuItem");
            this.noSmoothingToolStripMenuItem.Click += new System.EventHandler(this.noSmoothingToolStripMenuItem_Click);
            // 
            // simpleMovingAverageToolStripMenuItem
            // 
            this.simpleMovingAverageToolStripMenuItem.Name = "simpleMovingAverageToolStripMenuItem";
            resources.ApplyResources(this.simpleMovingAverageToolStripMenuItem, "simpleMovingAverageToolStripMenuItem");
            this.simpleMovingAverageToolStripMenuItem.Click += new System.EventHandler(this.simpleMovingAverageToolStripMenuItem_Click);
            // 
            // weightedMovingAverageToolStripMenuItem
            // 
            this.weightedMovingAverageToolStripMenuItem.Name = "weightedMovingAverageToolStripMenuItem";
            resources.ApplyResources(this.weightedMovingAverageToolStripMenuItem, "weightedMovingAverageToolStripMenuItem");
            this.weightedMovingAverageToolStripMenuItem.Click += new System.EventHandler(this.weightedMovingAverageToolStripMenuItem_Click);
            // 
            // toolStripSplitButton9
            // 
            this.toolStripSplitButton9.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton9.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.showPeaksToolStripMenuItem,
            this.hidePeaksToolStripMenuItem});
            this.toolStripSplitButton9.Image = global::BecquerelMonitor.Properties.Resources.peak;
            resources.ApplyResources(this.toolStripSplitButton9, "toolStripSplitButton9");
            this.toolStripSplitButton9.Name = "toolStripSplitButton9";
            this.toolStripSplitButton9.ButtonClick += new System.EventHandler(this.toolStripSplitButton9_ButtonClick);
            this.toolStripSplitButton9.DropDownOpening += new System.EventHandler(this.toolStripSplitButton9_DropDownOpening);
            // 
            // showPeaksToolStripMenuItem
            // 
            this.showPeaksToolStripMenuItem.Name = "showPeaksToolStripMenuItem";
            resources.ApplyResources(this.showPeaksToolStripMenuItem, "showPeaksToolStripMenuItem");
            this.showPeaksToolStripMenuItem.Click += new System.EventHandler(this.showPeaksToolStripMenuItem_Click);
            // 
            // hidePeaksToolStripMenuItem
            // 
            this.hidePeaksToolStripMenuItem.Name = "hidePeaksToolStripMenuItem";
            resources.ApplyResources(this.hidePeaksToolStripMenuItem, "hidePeaksToolStripMenuItem");
            this.hidePeaksToolStripMenuItem.Click += new System.EventHandler(this.hidePeaksToolStripMenuItem_Click);
            // 
            // toolStripScreenShotButton
            // 
            this.toolStripScreenShotButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripScreenShotButton.Image = global::BecquerelMonitor.Properties.Resources.screenshot;
            this.toolStripScreenShotButton.Name = "toolStripScreenShotButton";
            resources.ApplyResources(this.toolStripScreenShotButton, "toolStripScreenShotButton");
            this.toolStripScreenShotButton.Click += new System.EventHandler(this.toolStripScreenShotButton_Click);
            // 
            // toolStripSeparator4
            // 
            this.toolStripSeparator4.Name = "toolStripSeparator4";
            resources.ApplyResources(this.toolStripSeparator4, "toolStripSeparator4");
            // 
            // toolStripLabel4
            // 
            this.toolStripLabel4.Name = "toolStripLabel4";
            resources.ApplyResources(this.toolStripLabel4, "toolStripLabel4");
            // 
            // toolStripSaveButton
            // 
            this.toolStripSaveButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSaveButton.Image = global::BecquerelMonitor.Properties.Resources.save;
            this.toolStripSaveButton.Name = "toolStripSaveButton";
            resources.ApplyResources(this.toolStripSaveButton, "toolStripSaveButton");
            this.toolStripSaveButton.Click += new System.EventHandler(this.toolStripSaveButton_Click);
            // 
            // toolStripRefreshBgButton
            // 
            this.toolStripRefreshBgButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripRefreshBgButton.Image = global::BecquerelMonitor.Properties.Resources.RefreshBG;
            this.toolStripRefreshBgButton.Name = "toolStripRefreshBgButton";
            resources.ApplyResources(this.toolStripRefreshBgButton, "toolStripRefreshBgButton");
            this.toolStripRefreshBgButton.Click += new System.EventHandler(this.toolStripRefreshBgButton_Click);
            // 
            // toolStripSplitButton
            // 
            this.toolStripSplitButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton.Image = global::BecquerelMonitor.Properties.Resources.split;
            this.toolStripSplitButton.Name = "toolStripSplitButton";
            resources.ApplyResources(this.toolStripSplitButton, "toolStripSplitButton");
            this.toolStripSplitButton.Click += new System.EventHandler(this.toolStripSplitButton_Click);
            // 
            // energySpectrumView1
            // 
            this.energySpectrumView1.BackColor = System.Drawing.Color.White;
            resources.ApplyResources(this.energySpectrumView1, "energySpectrumView1");
            this.energySpectrumView1.Name = "energySpectrumView1";
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripMenuItem1,
            this.showAllChannelsAToolStripMenuItem,
            this.toolStripSeparator2,
            this.toolStripMenuItem2,
            this.toolStripMenuItem3,
            this.toolStripSeparator1,
            this.saveSToolStripMenuItem,
            this.closeCToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            resources.ApplyResources(this.contextMenuStrip1, "contextMenuStrip1");
            this.contextMenuStrip1.Closed += new System.Windows.Forms.ToolStripDropDownClosedEventHandler(this.contextMenuStrip1_Closed);
            this.contextMenuStrip1.Opening += new System.ComponentModel.CancelEventHandler(this.contextMenuStrip1_Opening);
            // 
            // toolStripMenuItem1
            // 
            this.toolStripMenuItem1.Name = "toolStripMenuItem1";
            resources.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
            this.toolStripMenuItem1.Click += new System.EventHandler(this.toolStripMenuItem1_Click);
            // 
            // showAllChannelsAToolStripMenuItem
            // 
            this.showAllChannelsAToolStripMenuItem.Name = "showAllChannelsAToolStripMenuItem";
            resources.ApplyResources(this.showAllChannelsAToolStripMenuItem, "showAllChannelsAToolStripMenuItem");
            this.showAllChannelsAToolStripMenuItem.Click += new System.EventHandler(this.showAllChannelsAToolStripMenuItem_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            resources.ApplyResources(this.toolStripSeparator2, "toolStripSeparator2");
            // 
            // toolStripMenuItem2
            // 
            this.toolStripMenuItem2.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.setLowerThresholdLToolStripMenuItem,
            this.setUpperThresholdHToolStripMenuItem});
            this.toolStripMenuItem2.Name = "toolStripMenuItem2";
            resources.ApplyResources(this.toolStripMenuItem2, "toolStripMenuItem2");
            // 
            // setLowerThresholdLToolStripMenuItem
            // 
            this.setLowerThresholdLToolStripMenuItem.Name = "setLowerThresholdLToolStripMenuItem";
            resources.ApplyResources(this.setLowerThresholdLToolStripMenuItem, "setLowerThresholdLToolStripMenuItem");
            this.setLowerThresholdLToolStripMenuItem.Click += new System.EventHandler(this.setLowerThresholdLToolStripMenuItem_Click);
            // 
            // setUpperThresholdHToolStripMenuItem
            // 
            this.setUpperThresholdHToolStripMenuItem.Name = "setUpperThresholdHToolStripMenuItem";
            resources.ApplyResources(this.setUpperThresholdHToolStripMenuItem, "setUpperThresholdHToolStripMenuItem");
            this.setUpperThresholdHToolStripMenuItem.Click += new System.EventHandler(this.setUpperThresholdHToolStripMenuItem_Click);
            // 
            // toolStripMenuItem3
            // 
            this.toolStripMenuItem3.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.createRoiDefinitionFromSelectionSToolStripMenuItem});
            this.toolStripMenuItem3.Name = "toolStripMenuItem3";
            resources.ApplyResources(this.toolStripMenuItem3, "toolStripMenuItem3");
            // 
            // createRoiDefinitionFromSelectionSToolStripMenuItem
            // 
            this.createRoiDefinitionFromSelectionSToolStripMenuItem.Name = "createRoiDefinitionFromSelectionSToolStripMenuItem";
            resources.ApplyResources(this.createRoiDefinitionFromSelectionSToolStripMenuItem, "createRoiDefinitionFromSelectionSToolStripMenuItem");
            this.createRoiDefinitionFromSelectionSToolStripMenuItem.Click += new System.EventHandler(this.createRoiDefinitionFromSelectionSToolStripMenuItem_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
            // 
            // saveSToolStripMenuItem
            // 
            this.saveSToolStripMenuItem.Name = "saveSToolStripMenuItem";
            resources.ApplyResources(this.saveSToolStripMenuItem, "saveSToolStripMenuItem");
            this.saveSToolStripMenuItem.Click += new System.EventHandler(this.saveSToolStripMenuItem_Click);
            // 
            // closeCToolStripMenuItem
            // 
            this.closeCToolStripMenuItem.Name = "closeCToolStripMenuItem";
            resources.ApplyResources(this.closeCToolStripMenuItem, "closeCToolStripMenuItem");
            this.closeCToolStripMenuItem.Click += new System.EventHandler(this.closeCToolStripMenuItem_Click);
            // 
            // DocEnergySpectrum
            // 
            this.AllowDrop = true;
            resources.ApplyResources(this, "$this");
            this.ContextMenuStrip = this.contextMenuStrip1;
            this.Controls.Add(this.toolStripContainer1);
            this.DockAreas = ((WeifenLuo.WinFormsUI.Docking.DockAreas)((WeifenLuo.WinFormsUI.Docking.DockAreas.Float | WeifenLuo.WinFormsUI.Docking.DockAreas.Document)));
            this.Name = "DocEnergySpectrum";
            this.Activated += new System.EventHandler(this.DocEnergySpectrum_Activated);
            this.Load += new System.EventHandler(this.DocEnergySpectrum_Load);
            this.SizeChanged += new System.EventHandler(this.DocEnergySpectrum_SizeChanged);
            this.Click += new System.EventHandler(this.DocEnergySpectrum_Click);
            this.DragDrop += new System.Windows.Forms.DragEventHandler(this.DocEnergySpectrum_DragDrop);
            this.DragEnter += new System.Windows.Forms.DragEventHandler(this.DocEnergySpectrum_DragEnter);
            this.MouseDown += new System.Windows.Forms.MouseEventHandler(this.DocEnergySpectrum_MouseDown);
            this.MouseUp += new System.Windows.Forms.MouseEventHandler(this.DocEnergySpectrum_MouseUp);
            this.toolStripContainer1.BottomToolStripPanel.ResumeLayout(false);
            this.toolStripContainer1.BottomToolStripPanel.PerformLayout();
            this.toolStripContainer1.ContentPanel.ResumeLayout(false);
            this.toolStripContainer1.ResumeLayout(false);
            this.toolStripContainer1.PerformLayout();
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this.toolStrip2.ResumeLayout(false);
            this.toolStrip2.PerformLayout();
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);

		}

        // Token: 0x04000111 RID: 273
        global::System.ComponentModel.IContainer components;

		// Token: 0x04000112 RID: 274
		global::System.Windows.Forms.Panel energySpectrumView1;

		// Token: 0x04000113 RID: 275
		global::System.Windows.Forms.ContextMenuStrip contextMenuStrip1;

		// Token: 0x04000114 RID: 276
		global::System.Windows.Forms.ToolStripMenuItem saveSToolStripMenuItem;

		// Token: 0x04000115 RID: 277
		global::System.Windows.Forms.ToolStripMenuItem closeCToolStripMenuItem;

		// Token: 0x04000116 RID: 278
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator1;

		// Token: 0x04000117 RID: 279
		global::System.Windows.Forms.ToolTip toolTip1;

		// Token: 0x04000118 RID: 280
		global::System.Windows.Forms.ToolStripLabel toolStripLabel1;

		// Token: 0x04000119 RID: 281
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton1;

		// Token: 0x0400011A RID: 282
		global::System.Windows.Forms.ToolStripMenuItem cpsViewToolStripMenuItem;

		// Token: 0x0400011B RID: 283
		global::System.Windows.Forms.ToolStripMenuItem countsViewToolStripMenuItem;

		// Token: 0x0400011C RID: 284
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton2;

		ToolStripNumericUpdown toolStripNumericUpdown;

		ToolStripButton toolStripScreenShotButton;

        ToolStripButton toolStripRefreshBgButton;

        ToolStripButton toolStripSaveButton;

		ToolStripButton toolStripSplitButton;

        ToolStripNumericUpdown toolStripNumUpDownScale;

        // Token: 0x0400011D RID: 285
        global::System.Windows.Forms.ToolStripMenuItem logarithmicViewToolStripMenuItem;

		// Token: 0x0400011E RID: 286
		global::System.Windows.Forms.ToolStripMenuItem linearViewToolStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem powToolStripMenuItem;

		// Token: 0x0400011F RID: 287
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton5;

		// Token: 0x04000120 RID: 288
		global::System.Windows.Forms.ToolStripMenuItem autoFitToolStripMenuItem;

		// Token: 0x04000121 RID: 289
		global::System.Windows.Forms.ToolStripMenuItem noneToolStripMenuItem;

		// Token: 0x04000122 RID: 290
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;

		// Token: 0x04000123 RID: 291
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator2;

		// Token: 0x04000124 RID: 292
		global::System.Windows.Forms.ToolStripContainer toolStripContainer1;

		// Token: 0x04000125 RID: 293
		global::System.Windows.Forms.ToolStripMenuItem autoFitBgToolStripMenuItem;

		// Token: 0x04000126 RID: 294
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator3;

        global::System.Windows.Forms.ToolStripSeparator toolStripSeparator4;

        // Token: 0x04000127 RID: 295
        global::System.Windows.Forms.ToolStripLabel toolStripLabel3;

        global::System.Windows.Forms.ToolStripLabel toolStripLabel4;

        // Token: 0x04000128 RID: 296
        global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton4;

		// Token: 0x04000129 RID: 297
		global::System.Windows.Forms.ToolStripMenuItem energyViewToolStripMenuItem;

		// Token: 0x0400012A RID: 298
		global::System.Windows.Forms.ToolStripMenuItem channelViewToolStripMenuItem;

		// Token: 0x0400012B RID: 299
		global::System.Windows.Forms.ToolStripButton toolStripButton1;

        global::System.Windows.Forms.ToolStripButton toolStripButton3;

        // Token: 0x0400012C RID: 300
        global::System.Windows.Forms.ToolStripButton toolStripButton2;

		// Token: 0x0400012D RID: 301
		global::System.Windows.Forms.ToolStripMenuItem showAllChannelsAToolStripMenuItem;

		// Token: 0x0400012E RID: 302
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem2;

		// Token: 0x0400012F RID: 303
		global::System.Windows.Forms.ToolStripMenuItem setLowerThresholdLToolStripMenuItem;

		// Token: 0x04000130 RID: 304
		global::System.Windows.Forms.ToolStripMenuItem setUpperThresholdHToolStripMenuItem;

		// Token: 0x04000131 RID: 305
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem3;

		// Token: 0x04000132 RID: 306
		global::System.Windows.Forms.ToolStripMenuItem createRoiDefinitionFromSelectionSToolStripMenuItem;

		// Token: 0x04000133 RID: 307
		global::System.Windows.Forms.ToolStripLabel toolStripLabel2;

		// Token: 0x04000134 RID: 308
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton3;

		// Token: 0x04000135 RID: 309
		global::System.Windows.Forms.ToolStripMenuItem barGraphToolStripMenuItem;

		// Token: 0x04000136 RID: 310
		global::System.Windows.Forms.ToolStripMenuItem lineGraphToolStripMenuItem;

		// Token: 0x04000137 RID: 311
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton6;

		// Token: 0x04000138 RID: 312
		global::System.Windows.Forms.ToolStripMenuItem noSmoothingToolStripMenuItem;

		// Token: 0x04000139 RID: 313
		global::System.Windows.Forms.ToolStripMenuItem simpleMovingAverageToolStripMenuItem;

		// Token: 0x0400013A RID: 314
		global::System.Windows.Forms.ToolStripMenuItem weightedMovingAverageToolStripMenuItem;

		// Token: 0x0400013B RID: 315
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButtonBgMode;

		// Token: 0x0400013C RID: 316
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton8;

		// Token: 0x0400013D RID: 317
		global::System.Windows.Forms.ToolStripMenuItem showBackgroundToolStripMenuItem;

        global::System.Windows.Forms.ToolStripMenuItem SubstractBgToolStripMenuItem;

        global::System.Windows.Forms.ToolStripMenuItem ShowConToolStripMenuItem;

        global::System.Windows.Forms.ToolStripMenuItem NormByEffToolStripMenuItem;

        // Token: 0x0400013E RID: 318
        global::System.Windows.Forms.ToolStripMenuItem hideBackgroundToolStripMenuItem;

		// Token: 0x0400013F RID: 319
		global::System.Windows.Forms.ToolStripMenuItem highDefinitionViewToolStripMenuItem;

		// Token: 0x04000140 RID: 320
		global::System.Windows.Forms.ToolStripMenuItem normalViewToolStripMenuItem;

		// Token: 0x04000141 RID: 321
		global::BecquerelMonitor.ToolStripEx toolStrip1;

		// Token: 0x04000142 RID: 322
		global::BecquerelMonitor.ToolStripEx toolStrip2;

		// Token: 0x04000143 RID: 323
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton9;

		// Token: 0x04000144 RID: 324
		global::System.Windows.Forms.ToolStripMenuItem showPeaksToolStripMenuItem;

		// Token: 0x04000145 RID: 325
		global::System.Windows.Forms.ToolStripMenuItem hidePeaksToolStripMenuItem;

		// Token: 0x04000146 RID: 326
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton10;

		// Token: 0x04000147 RID: 327
		global::System.Windows.Forms.ToolStripMenuItem showEnergyCalibrationToolToolStripMenuItem;

		// Token: 0x04000148 RID: 328
		global::System.Windows.Forms.ToolStripMenuItem hideEnergyCalibrationToolToolStripMenuItem;
	}
}
