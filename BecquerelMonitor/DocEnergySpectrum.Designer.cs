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
			this.components = new global::System.ComponentModel.Container();
			global::System.ComponentModel.ComponentResourceManager resources = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.DocEnergySpectrum));
			this.toolStripContainer1 = new global::System.Windows.Forms.ToolStripContainer();
			this.toolStrip2 = new global::BecquerelMonitor.ToolStripEx();
			this.toolStripLabel2 = new global::System.Windows.Forms.ToolStripLabel();
			this.toolStripSplitButtonBgMode = new global::System.Windows.Forms.ToolStripSplitButton();
			this.showBackgroundToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.SubstractBgToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.ShowConToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.NormByEffToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.hideBackgroundToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton8 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.highDefinitionViewToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.normalViewToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton3 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.barGraphToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.lineGraphToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton6 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.noSmoothingToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.simpleMovingAverageToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.weightedMovingAverageToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton9 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.showPeaksToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.hidePeaksToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStrip1 = new global::BecquerelMonitor.ToolStripEx();
			this.toolStripLabel1 = new global::System.Windows.Forms.ToolStripLabel();
			this.toolStripSplitButton1 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.countsViewToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.cpsViewToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton2 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.toolStripNumericUpdown = new ToolStripNumericUpdown();
			this.toolStripScreenShotButton = new ToolStripButton();
			this.toolStripRefreshBgButton = new ToolStripButton();
			this.toolStripSaveButton = new ToolStripButton();
			this.toolStripSplitButton = new ToolStripButton();
            this.toolStripNumUpDownScale = new ToolStripNumericUpdown();
            this.linearViewToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.powToolStripMenuItem = new global::System.Windows.Forms .ToolStripMenuItem();
			this.logarithmicViewToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton5 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.noneToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.autoFitToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.autoFitBgToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator3 = new global::System.Windows.Forms.ToolStripSeparator();
            this.toolStripSeparator4 = new global::System.Windows.Forms.ToolStripSeparator();
            this.toolStripLabel3 = new global::System.Windows.Forms.ToolStripLabel();
            this.toolStripLabel4 = new global::System.Windows.Forms.ToolStripLabel();
            this.toolStripSplitButton4 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.channelViewToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.energyViewToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripButton2 = new global::System.Windows.Forms.ToolStripButton();
			this.toolStripButton1 = new global::System.Windows.Forms.ToolStripButton();
            this.toolStripButton3 = new global::System.Windows.Forms.ToolStripButton();
            this.toolStripSplitButton10 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.showEnergyCalibrationToolToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.hideEnergyCalibrationToolToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.energySpectrumView1 = new global::System.Windows.Forms.Panel();
			this.contextMenuStrip1 = new global::System.Windows.Forms.ContextMenuStrip(this.components);
			this.toolStripMenuItem1 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.showAllChannelsAToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator2 = new global::System.Windows.Forms.ToolStripSeparator();
			this.toolStripMenuItem2 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.setLowerThresholdLToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.setUpperThresholdHToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripMenuItem3 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.createRoiDefinitionFromSelectionSToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator1 = new global::System.Windows.Forms.ToolStripSeparator();
			this.saveSToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.closeCToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolTip1 = new global::System.Windows.Forms.ToolTip(this.components);
			this.toolStripContainer1.BottomToolStripPanel.SuspendLayout();
			this.toolStripContainer1.ContentPanel.SuspendLayout();
			this.toolStripContainer1.SuspendLayout();
			this.toolStrip2.SuspendLayout();
			this.toolStrip1.SuspendLayout();
			this.contextMenuStrip1.SuspendLayout();
			base.SuspendLayout();
			resources.ApplyResources(this.toolStripContainer1, "toolStripContainer1");
			resources.ApplyResources(this.toolStripContainer1.BottomToolStripPanel, "toolStripContainer1.BottomToolStripPanel");
			this.toolStripContainer1.BottomToolStripPanel.Controls.Add(this.toolStrip2);
			this.toolStripContainer1.BottomToolStripPanel.Controls.Add(this.toolStrip1);
			this.toolTip1.SetToolTip(this.toolStripContainer1.BottomToolStripPanel, resources.GetString("toolStripContainer1.BottomToolStripPanel.ToolTip"));
			this.toolStripContainer1.BottomToolStripPanel.Click += new global::System.EventHandler(this.toolStripContainer1_BottomToolStripPanel_Click);
			resources.ApplyResources(this.toolStripContainer1.ContentPanel, "toolStripContainer1.ContentPanel");
			this.toolStripContainer1.ContentPanel.Controls.Add(this.energySpectrumView1);
			this.toolTip1.SetToolTip(this.toolStripContainer1.ContentPanel, resources.GetString("toolStripContainer1.ContentPanel.ToolTip"));
			resources.ApplyResources(this.toolStripContainer1.LeftToolStripPanel, "toolStripContainer1.LeftToolStripPanel");
			this.toolTip1.SetToolTip(this.toolStripContainer1.LeftToolStripPanel, resources.GetString("toolStripContainer1.LeftToolStripPanel.ToolTip"));
			this.toolStripContainer1.LeftToolStripPanel.Click += new global::System.EventHandler(this.toolStripContainer1_LeftToolStripPanel_Click);
			this.toolStripContainer1.Name = "toolStripContainer1";
			resources.ApplyResources(this.toolStripContainer1.RightToolStripPanel, "toolStripContainer1.RightToolStripPanel");
			this.toolTip1.SetToolTip(this.toolStripContainer1.RightToolStripPanel, resources.GetString("toolStripContainer1.RightToolStripPanel.ToolTip"));
			this.toolStripContainer1.RightToolStripPanel.Click += new global::System.EventHandler(this.toolStripContainer1_RightToolStripPanel_Click);
			this.toolTip1.SetToolTip(this.toolStripContainer1, resources.GetString("toolStripContainer1.ToolTip"));
			resources.ApplyResources(this.toolStripContainer1.TopToolStripPanel, "toolStripContainer1.TopToolStripPanel");
			this.toolTip1.SetToolTip(this.toolStripContainer1.TopToolStripPanel, resources.GetString("toolStripContainer1.TopToolStripPanel.ToolTip"));
			this.toolStripContainer1.TopToolStripPanel.Click += new global::System.EventHandler(this.toolStripContainer1_TopToolStripPanel_Click);
			resources.ApplyResources(this.toolStrip2, "toolStrip2");
			this.toolStrip2.ClickThrough = true;
			this.toolStrip2.Items.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
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
				this.toolStripSplitButton
            });
			this.toolStrip2.Name = "toolStrip2";
			this.toolStrip2.SuppressHighlighting = false;
			this.toolTip1.SetToolTip(this.toolStrip2, resources.GetString("toolStrip2.ToolTip"));
			resources.ApplyResources(this.toolStripLabel2, "toolStripLabel2");
			this.toolStripLabel2.Name = "toolStripLabel2";
			resources.ApplyResources(this.toolStripSplitButtonBgMode, "toolStripSplitButton7");
            resources.ApplyResources(this.toolStripSeparator4, "toolStripSeparator4");
            this.toolStripSeparator4.Name = "toolStripSeparator4";
            resources.ApplyResources(this.toolStripLabel4, "toolStripLabel4");
            this.toolStripLabel4.Name = "toolStripLabel4";
            this.toolStripSplitButtonBgMode.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButtonBgMode.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.showBackgroundToolStripMenuItem,
				this.hideBackgroundToolStripMenuItem,
				this.SubstractBgToolStripMenuItem,
				this.ShowConToolStripMenuItem,
				this.NormByEffToolStripMenuItem
			});
			this.toolStripSplitButtonBgMode.Image = global::BecquerelMonitor.Properties.Resources.BG;
			this.toolStripSplitButtonBgMode.Name = "toolStripSplitButton7";
			this.toolStripSplitButtonBgMode.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton7_ButtonClick);
			this.toolStripSplitButtonBgMode.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton7_DropDownOpening);
			resources.ApplyResources(this.showBackgroundToolStripMenuItem, "showBackgroundToolStripMenuItem");
			this.showBackgroundToolStripMenuItem.Name = "showBackgroundToolStripMenuItem";
			this.showBackgroundToolStripMenuItem.Click += new global::System.EventHandler(this.showBackgroundToolStripMenuItem_Click);
            resources.ApplyResources(this.SubstractBgToolStripMenuItem, "SubstractBgToolStripMenuItem");
            this.SubstractBgToolStripMenuItem.Name = "SubstractBgToolStripMenuItem";
            this.SubstractBgToolStripMenuItem.Click += new global::System.EventHandler(this.SubstractBgToolStripMenuItem_Click);

            resources.ApplyResources(this.ShowConToolStripMenuItem, "ShowConToolStripMenuItem");
            this.ShowConToolStripMenuItem.Name = "ShowConToolStripMenuItem";
            this.ShowConToolStripMenuItem.Click += new global::System.EventHandler(this.ShowConToolStripMenuItem_Click);

            resources.ApplyResources(this.NormByEffToolStripMenuItem, "NormByEffToolStripMenuItem");
            this.NormByEffToolStripMenuItem.Name = "NormByEffToolStripMenuItem";
            this.NormByEffToolStripMenuItem.Click += new global::System.EventHandler(this.NormByEffToolStripMenuItem_Click);
			this.NormByEffToolStripMenuItem.Enabled = false;

            resources.ApplyResources(this.hideBackgroundToolStripMenuItem, "hideBackgroundToolStripMenuItem");
			this.hideBackgroundToolStripMenuItem.Name = "hideBackgroundToolStripMenuItem";
			this.hideBackgroundToolStripMenuItem.Click += new global::System.EventHandler(this.hideBackgroundToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton8, "toolStripSplitButton8");
			this.toolStripSplitButton8.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton8.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.highDefinitionViewToolStripMenuItem,
				this.normalViewToolStripMenuItem
			});
			this.toolStripSplitButton8.Image = global::BecquerelMonitor.Properties.Resources.HD;
			this.toolStripSplitButton8.Name = "toolStripSplitButton8";
			this.toolStripSplitButton8.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton8_ButtonClick);
			this.toolStripSplitButton8.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton8_DropDownOpening);
			resources.ApplyResources(this.highDefinitionViewToolStripMenuItem, "highDefinitionViewToolStripMenuItem");
			this.highDefinitionViewToolStripMenuItem.Name = "highDefinitionViewToolStripMenuItem";
			this.highDefinitionViewToolStripMenuItem.Click += new global::System.EventHandler(this.highDefinitionViewToolStripMenuItem_Click);
			resources.ApplyResources(this.normalViewToolStripMenuItem, "normalViewToolStripMenuItem");
			this.normalViewToolStripMenuItem.Name = "normalViewToolStripMenuItem";
			this.normalViewToolStripMenuItem.Click += new global::System.EventHandler(this.normalViewToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton3, "toolStripSplitButton3");
			this.toolStripSplitButton3.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton3.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.barGraphToolStripMenuItem,
				this.lineGraphToolStripMenuItem
			});
			this.toolStripSplitButton3.Image = global::BecquerelMonitor.Properties.Resources.line;
			this.toolStripSplitButton3.Name = "toolStripSplitButton3";
			this.toolStripSplitButton3.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton3_ButtonClick);
			this.toolStripSplitButton3.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton3_DropDownOpening);
			resources.ApplyResources(this.barGraphToolStripMenuItem, "barGraphToolStripMenuItem");
			this.barGraphToolStripMenuItem.Name = "barGraphToolStripMenuItem";
			this.barGraphToolStripMenuItem.Click += new global::System.EventHandler(this.barGraphToolStripMenuItem_Click);
			resources.ApplyResources(this.lineGraphToolStripMenuItem, "lineGraphToolStripMenuItem");
			this.lineGraphToolStripMenuItem.Name = "lineGraphToolStripMenuItem";
			this.lineGraphToolStripMenuItem.Click += new global::System.EventHandler(this.lineGraphToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton6, "toolStripSplitButton6");
			this.toolStripSplitButton6.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton6.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.noSmoothingToolStripMenuItem,
				this.simpleMovingAverageToolStripMenuItem,
				this.weightedMovingAverageToolStripMenuItem
			});
			this.toolStripSplitButton6.Image = global::BecquerelMonitor.Properties.Resources.NoSmooth;
			this.toolStripSplitButton6.Name = "toolStripSplitButton6";
			this.toolStripSplitButton6.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton6_ButtonClick);
			this.toolStripSplitButton6.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton6_DropDownOpening);
			resources.ApplyResources(this.noSmoothingToolStripMenuItem, "noSmoothingToolStripMenuItem");
			this.noSmoothingToolStripMenuItem.Name = "noSmoothingToolStripMenuItem";
			this.noSmoothingToolStripMenuItem.Click += new global::System.EventHandler(this.noSmoothingToolStripMenuItem_Click);
			resources.ApplyResources(this.simpleMovingAverageToolStripMenuItem, "simpleMovingAverageToolStripMenuItem");
			this.simpleMovingAverageToolStripMenuItem.Name = "simpleMovingAverageToolStripMenuItem";
			this.simpleMovingAverageToolStripMenuItem.Click += new global::System.EventHandler(this.simpleMovingAverageToolStripMenuItem_Click);
			resources.ApplyResources(this.weightedMovingAverageToolStripMenuItem, "weightedMovingAverageToolStripMenuItem");
			this.weightedMovingAverageToolStripMenuItem.Name = "weightedMovingAverageToolStripMenuItem";
			this.weightedMovingAverageToolStripMenuItem.Click += new global::System.EventHandler(this.weightedMovingAverageToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton9, "toolStripSplitButton9");
			this.toolStripSplitButton9.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton9.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.showPeaksToolStripMenuItem,
				this.hidePeaksToolStripMenuItem
			});
			this.toolStripSplitButton9.Image = global::BecquerelMonitor.Properties.Resources.peak;
			this.toolStripSplitButton9.Name = "toolStripSplitButton9";
            this.toolStripSplitButton9.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton9_ButtonClick);
			this.toolStripSplitButton9.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton9_DropDownOpening);
            resources.ApplyResources(this.toolStripScreenShotButton, "toolStripScreenShotButton");
            this.toolStripScreenShotButton.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripScreenShotButton.Image = global::BecquerelMonitor.Properties.Resources.screenshot;
            this.toolStripScreenShotButton.Name = "toolStripScreenShotButton";
            this.toolStripScreenShotButton.Click += new global::System.EventHandler(this.toolStripScreenShotButton_Click);
            resources.ApplyResources(this.toolStripRefreshBgButton, "toolStripRefreshBgButton");
            this.toolStripRefreshBgButton.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripRefreshBgButton.Image = global::BecquerelMonitor.Properties.Resources.RefreshBG;
            this.toolStripRefreshBgButton.Name = "toolStripRefreshBgButton";
            this.toolStripRefreshBgButton.Click += new global::System.EventHandler(this.toolStripRefreshBgButton_Click);
            resources.ApplyResources(this.toolStripSaveButton, "toolStripSaveButton");
            this.toolStripSaveButton.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSaveButton.Image = global::BecquerelMonitor.Properties.Resources.save;
            this.toolStripSaveButton.Name = "toolStripSaveButton";
            this.toolStripSaveButton.Click += new global::System.EventHandler(this.toolStripSaveButton_Click);
            resources.ApplyResources(this.toolStripSplitButton, "toolStripSplitButton");
            this.toolStripSplitButton.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripSplitButton.Image = global::BecquerelMonitor.Properties.Resources.split;
            this.toolStripSplitButton.Name = "toolStripSplitButton";
            this.toolStripSplitButton.Click += new global::System.EventHandler(this.toolStripSplitButton_Click);
            resources.ApplyResources(this.showPeaksToolStripMenuItem, "showPeaksToolStripMenuItem");
			this.showPeaksToolStripMenuItem.Name = "showPeaksToolStripMenuItem";
			this.showPeaksToolStripMenuItem.Click += new global::System.EventHandler(this.showPeaksToolStripMenuItem_Click);
			resources.ApplyResources(this.hidePeaksToolStripMenuItem, "hidePeaksToolStripMenuItem");
			this.hidePeaksToolStripMenuItem.Name = "hidePeaksToolStripMenuItem";
			this.hidePeaksToolStripMenuItem.Click += new global::System.EventHandler(this.hidePeaksToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStrip1, "toolStrip1");
			this.toolStrip1.ClickThrough = true;
			this.toolStrip1.ImageScalingSize = new global::System.Drawing.Size(18, 18);
			this.toolStrip1.Items.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
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
				this.toolStripSplitButton10
			});
			this.toolStrip1.Name = "toolStrip1";
			this.toolStrip1.SuppressHighlighting = false;
			this.toolTip1.SetToolTip(this.toolStrip1, resources.GetString("toolStrip1.ToolTip"));
			resources.ApplyResources(this.toolStripLabel1, "toolStripLabel1");
			this.toolStripLabel1.Name = "toolStripLabel1";
			resources.ApplyResources(this.toolStripSplitButton1, "toolStripSplitButton1");
			this.toolStripSplitButton1.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton1.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.countsViewToolStripMenuItem,
				this.cpsViewToolStripMenuItem
			});
			this.toolStripSplitButton1.Image = global::BecquerelMonitor.Properties.Resources.cps;
			this.toolStripSplitButton1.Name = "toolStripSplitButton1";
			this.toolStripSplitButton1.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton1_ButtonClick);
			this.toolStripSplitButton1.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton1_DropDownOpening);
			resources.ApplyResources(this.countsViewToolStripMenuItem, "countsViewToolStripMenuItem");
			this.countsViewToolStripMenuItem.Name = "countsViewToolStripMenuItem";
			this.countsViewToolStripMenuItem.Click += new global::System.EventHandler(this.countsViewToolStripMenuItem_Click);
			resources.ApplyResources(this.cpsViewToolStripMenuItem, "cpsViewToolStripMenuItem");
			this.cpsViewToolStripMenuItem.Name = "cpsViewToolStripMenuItem";
			this.cpsViewToolStripMenuItem.Click += new global::System.EventHandler(this.cpsViewToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton2, "toolStripSplitButton2");
			this.toolStripSplitButton2.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton2.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.linearViewToolStripMenuItem,
                this.powToolStripMenuItem,
                this.logarithmicViewToolStripMenuItem
			});
			this.toolStripSplitButton2.Image = global::BecquerelMonitor.Properties.Resources.log;
			this.toolStripSplitButton2.Name = "toolStripSplitButton2";
			this.toolStripSplitButton2.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton2_ButtonClick);
			this.toolStripSplitButton2.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton2_DropDownOpening);

            resources.ApplyResources(this.toolStripNumericUpdown, "toolStripNumericUpdown");
            this.toolStripNumericUpdown.Name = "toolStripNumericUpdown";
			this.toolStripNumericUpdown.NumericUpDownControl.DecimalPlaces = 3;
			this.toolStripNumericUpdown.NumericUpDownControl.Value = 4.0M;
			this.toolStripNumericUpdown.NumericUpDownControl.Maximum = 10.0M;
			this.toolStripNumericUpdown.NumericUpDownControl.Minimum = 1.0M;
			this.toolStripNumericUpdown.NumericUpDownControl.Increment = 0.5M;
            this.toolStripNumericUpdown.ValueChanged += new System.EventHandler(this.toolStripNumericUpdown_ValueChanged);
            this.toolStripNumericUpdown.KeyDown += this.ToolStripNumericUpdown_KeyDown;
            this.toolStripNumericUpdown.Enabled = false;
            this.toolTip1.SetToolTip(this.toolStripNumericUpdown.Control, resources.GetString("toolStripNumericUpdown.ToolTipText"));

            resources.ApplyResources(this.toolStripNumUpDownScale, "toolStripNumUpDownScale");
            this.toolStripNumUpDownScale.Name = "toolStripNumUpDownScale";
            this.toolStripNumUpDownScale.NumericUpDownControl.DecimalPlaces = 3;
            this.toolStripNumUpDownScale.NumericUpDownControl.Value = 4.0M;
            this.toolStripNumUpDownScale.NumericUpDownControl.Maximum = 10.0M;
            this.toolStripNumUpDownScale.NumericUpDownControl.Minimum = 0.1M;
            this.toolStripNumUpDownScale.NumericUpDownControl.Increment = 0.1M;
            this.toolStripNumUpDownScale.ValueChanged += new System.EventHandler(this.toolStripNumUpDownScale_ValueChanged);
            this.toolStripNumUpDownScale.KeyDown += this.toolStripNumUpDownScale_KeyDown;
            this.toolTip1.SetToolTip(this.toolStripNumUpDownScale.Control, resources.GetString("toolStripNumUpDownScale.ToolTipText"));

            resources.ApplyResources(this.linearViewToolStripMenuItem, "linearViewToolStripMenuItem");
			this.linearViewToolStripMenuItem.Name = "linearViewToolStripMenuItem";
			this.linearViewToolStripMenuItem.Click += new global::System.EventHandler(this.linearViewToolStripMenuItem_Click);
            resources.ApplyResources(this.powToolStripMenuItem, "powToolStripMenuItem");
            this.powToolStripMenuItem.Name = "powToolStripMenuItem";
            this.powToolStripMenuItem.Click += new global::System.EventHandler(this.powToolStripMenuItem_Click);
            resources.ApplyResources(this.logarithmicViewToolStripMenuItem, "logarithmicViewToolStripMenuItem");
			this.logarithmicViewToolStripMenuItem.Name = "logarithmicViewToolStripMenuItem";
			this.logarithmicViewToolStripMenuItem.Click += new global::System.EventHandler(this.logarithmicViewToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton5, "toolStripSplitButton5");
			this.toolStripSplitButton5.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton5.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.noneToolStripMenuItem,
				this.autoFitToolStripMenuItem,
				this.autoFitBgToolStripMenuItem
			});
			this.toolStripSplitButton5.Image = global::BecquerelMonitor.Properties.Resources.fit1;
			this.toolStripSplitButton5.Name = "toolStripSplitButton5";
			this.toolStripSplitButton5.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton5_ButtonClick);
			this.toolStripSplitButton5.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton5_DropDownOpening);
			resources.ApplyResources(this.noneToolStripMenuItem, "noneToolStripMenuItem");
			this.noneToolStripMenuItem.Name = "noneToolStripMenuItem";
			this.noneToolStripMenuItem.Click += new global::System.EventHandler(this.noneToolStripMenuItem_Click);
			resources.ApplyResources(this.autoFitToolStripMenuItem, "autoFitToolStripMenuItem");
			this.autoFitToolStripMenuItem.Name = "autoFitToolStripMenuItem";
			this.autoFitToolStripMenuItem.Click += new global::System.EventHandler(this.autoFitToolStripMenuItem_Click);
			resources.ApplyResources(this.autoFitBgToolStripMenuItem, "autoFitBgToolStripMenuItem");
			this.autoFitBgToolStripMenuItem.Name = "autoFitBgToolStripMenuItem";
			this.autoFitBgToolStripMenuItem.Click += new global::System.EventHandler(this.autoFitBgToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSeparator3, "toolStripSeparator3");
			this.toolStripSeparator3.Name = "toolStripSeparator3";
			resources.ApplyResources(this.toolStripLabel3, "toolStripLabel3");
			this.toolStripLabel3.Name = "toolStripLabel3";
			resources.ApplyResources(this.toolStripSplitButton4, "toolStripSplitButton4");
			this.toolStripSplitButton4.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton4.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.channelViewToolStripMenuItem,
				this.energyViewToolStripMenuItem
			});
			this.toolStripSplitButton4.Image = global::BecquerelMonitor.Properties.Resources.ene;
			this.toolStripSplitButton4.Name = "toolStripSplitButton4";
			this.toolStripSplitButton4.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton4_ButtonClick);
			this.toolStripSplitButton4.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton4_DropDownOpening);
			resources.ApplyResources(this.channelViewToolStripMenuItem, "channelViewToolStripMenuItem");
			this.channelViewToolStripMenuItem.Name = "channelViewToolStripMenuItem";
			this.channelViewToolStripMenuItem.Click += new global::System.EventHandler(this.channelViewToolStripMenuItem_Click);
			resources.ApplyResources(this.energyViewToolStripMenuItem, "energyViewToolStripMenuItem");
			this.energyViewToolStripMenuItem.Name = "energyViewToolStripMenuItem";
			this.energyViewToolStripMenuItem.Click += new global::System.EventHandler(this.energyViewToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripButton2, "toolStripButton2");
			this.toolStripButton2.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripButton2.Image = global::BecquerelMonitor.Properties.Resources.ZoominSelection;
			this.toolStripButton2.Name = "toolStripButton2";
			this.toolStripButton2.Click += new global::System.EventHandler(this.toolStripButton2_Click);
			resources.ApplyResources(this.toolStripButton1, "toolStripButton1");
			this.toolStripButton1.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripButton1.Image = global::BecquerelMonitor.Properties.Resources.fit2;
			this.toolStripButton1.Name = "toolStripButton1";
			this.toolStripButton1.Click += new global::System.EventHandler(this.toolStripButton1_Click);
            resources.ApplyResources(this.toolStripButton3, "toolStripButton3");
            this.toolStripButton3.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripButton3.Image = global::BecquerelMonitor.Properties.Resources.scale;
            this.toolStripButton3.Name = "toolStripButton3";
            this.toolStripButton3.Click += new global::System.EventHandler(this.toolStripButton3_Click);
            resources.ApplyResources(this.toolStripSplitButton10, "toolStripSplitButton10");
			this.toolStripSplitButton10.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton10.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.showEnergyCalibrationToolToolStripMenuItem,
				this.hideEnergyCalibrationToolToolStripMenuItem
			});
			this.toolStripSplitButton10.Image = global::BecquerelMonitor.Properties.Resources.EnergyCalibration;
			this.toolStripSplitButton10.Name = "toolStripSplitButton10";
			this.toolStripSplitButton10.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton10_ButtonClick);
			this.toolStripSplitButton10.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton10_DropDownOpening);
			resources.ApplyResources(this.showEnergyCalibrationToolToolStripMenuItem, "showEnergyCalibrationToolToolStripMenuItem");
			this.showEnergyCalibrationToolToolStripMenuItem.Name = "showEnergyCalibrationToolToolStripMenuItem";
			this.showEnergyCalibrationToolToolStripMenuItem.Click += new global::System.EventHandler(this.showEnergyCalibrationToolToolStripMenuItem_Click);
			resources.ApplyResources(this.hideEnergyCalibrationToolToolStripMenuItem, "hideEnergyCalibrationToolToolStripMenuItem");
			this.hideEnergyCalibrationToolToolStripMenuItem.Name = "hideEnergyCalibrationToolToolStripMenuItem";
			this.hideEnergyCalibrationToolToolStripMenuItem.Click += new global::System.EventHandler(this.hideEnergyCalibrationToolToolStripMenuItem_Click);
			resources.ApplyResources(this.energySpectrumView1, "energySpectrumView1");
			this.energySpectrumView1.BackColor = global::System.Drawing.Color.White;
			this.energySpectrumView1.Name = "energySpectrumView1";
			this.toolTip1.SetToolTip(this.energySpectrumView1, resources.GetString("energySpectrumView1.ToolTip"));
			resources.ApplyResources(this.contextMenuStrip1, "contextMenuStrip1");
			this.contextMenuStrip1.Items.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.toolStripMenuItem1,
				this.showAllChannelsAToolStripMenuItem,
				this.toolStripSeparator2,
				this.toolStripMenuItem2,
				this.toolStripMenuItem3,
				this.toolStripSeparator1,
				this.saveSToolStripMenuItem,
				this.closeCToolStripMenuItem
			});
			this.contextMenuStrip1.Name = "contextMenuStrip1";
			this.toolTip1.SetToolTip(this.contextMenuStrip1, resources.GetString("contextMenuStrip1.ToolTip"));
			this.contextMenuStrip1.Closed += new global::System.Windows.Forms.ToolStripDropDownClosedEventHandler(this.contextMenuStrip1_Closed);
			this.contextMenuStrip1.Opening += new global::System.ComponentModel.CancelEventHandler(this.contextMenuStrip1_Opening);
			resources.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
			this.toolStripMenuItem1.Name = "toolStripMenuItem1";
			this.toolStripMenuItem1.Click += new global::System.EventHandler(this.toolStripMenuItem1_Click);
			resources.ApplyResources(this.showAllChannelsAToolStripMenuItem, "showAllChannelsAToolStripMenuItem");
			this.showAllChannelsAToolStripMenuItem.Name = "showAllChannelsAToolStripMenuItem";
			this.showAllChannelsAToolStripMenuItem.Click += new global::System.EventHandler(this.showAllChannelsAToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSeparator2, "toolStripSeparator2");
			this.toolStripSeparator2.Name = "toolStripSeparator2";
			resources.ApplyResources(this.toolStripMenuItem2, "toolStripMenuItem2");
			this.toolStripMenuItem2.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.setLowerThresholdLToolStripMenuItem,
				this.setUpperThresholdHToolStripMenuItem
			});
			this.toolStripMenuItem2.Name = "toolStripMenuItem2";
			resources.ApplyResources(this.setLowerThresholdLToolStripMenuItem, "setLowerThresholdLToolStripMenuItem");
			this.setLowerThresholdLToolStripMenuItem.Name = "setLowerThresholdLToolStripMenuItem";
			this.setLowerThresholdLToolStripMenuItem.Click += new global::System.EventHandler(this.setLowerThresholdLToolStripMenuItem_Click);
			resources.ApplyResources(this.setUpperThresholdHToolStripMenuItem, "setUpperThresholdHToolStripMenuItem");
			this.setUpperThresholdHToolStripMenuItem.Name = "setUpperThresholdHToolStripMenuItem";
			this.setUpperThresholdHToolStripMenuItem.Click += new global::System.EventHandler(this.setUpperThresholdHToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripMenuItem3, "toolStripMenuItem3");
			this.toolStripMenuItem3.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.createRoiDefinitionFromSelectionSToolStripMenuItem
			});
			this.toolStripMenuItem3.Name = "toolStripMenuItem3";
			resources.ApplyResources(this.createRoiDefinitionFromSelectionSToolStripMenuItem, "createRoiDefinitionFromSelectionSToolStripMenuItem");
			this.createRoiDefinitionFromSelectionSToolStripMenuItem.Name = "createRoiDefinitionFromSelectionSToolStripMenuItem";
			this.createRoiDefinitionFromSelectionSToolStripMenuItem.Click += new global::System.EventHandler(this.createRoiDefinitionFromSelectionSToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			resources.ApplyResources(this.saveSToolStripMenuItem, "saveSToolStripMenuItem");
			this.saveSToolStripMenuItem.Name = "saveSToolStripMenuItem";
			this.saveSToolStripMenuItem.Click += new global::System.EventHandler(this.saveSToolStripMenuItem_Click);
			resources.ApplyResources(this.closeCToolStripMenuItem, "closeCToolStripMenuItem");
			this.closeCToolStripMenuItem.Name = "closeCToolStripMenuItem";
			this.closeCToolStripMenuItem.Click += new global::System.EventHandler(this.closeCToolStripMenuItem_Click);
			resources.ApplyResources(this, "$this");
			this.AllowDrop = true;
			base.CloseButtonVisible = true;
			this.ContextMenuStrip = this.contextMenuStrip1;
			base.Controls.Add(this.toolStripContainer1);
			base.DockAreas = (global::WeifenLuo.WinFormsUI.Docking.DockAreas.Float | global::WeifenLuo.WinFormsUI.Docking.DockAreas.Document);
			base.Name = "DocEnergySpectrum";
			this.toolTip1.SetToolTip(this, resources.GetString("$this.ToolTip"));
			base.Activated += new global::System.EventHandler(this.DocEnergySpectrum_Activated);
			base.Load += new global::System.EventHandler(this.DocEnergySpectrum_Load);
			base.SizeChanged += new global::System.EventHandler(this.DocEnergySpectrum_SizeChanged);
			base.Click += new global::System.EventHandler(this.DocEnergySpectrum_Click);
			base.DragDrop += new global::System.Windows.Forms.DragEventHandler(this.DocEnergySpectrum_DragDrop);
			base.DragEnter += new global::System.Windows.Forms.DragEventHandler(this.DocEnergySpectrum_DragEnter);
			base.MouseDown += new global::System.Windows.Forms.MouseEventHandler(this.DocEnergySpectrum_MouseDown);
			base.MouseUp += new global::System.Windows.Forms.MouseEventHandler(this.DocEnergySpectrum_MouseUp);
			this.toolStripContainer1.BottomToolStripPanel.ResumeLayout(false);
			this.toolStripContainer1.BottomToolStripPanel.PerformLayout();
			this.toolStripContainer1.ContentPanel.ResumeLayout(false);
			this.toolStripContainer1.ResumeLayout(false);
			this.toolStripContainer1.PerformLayout();
			this.toolStrip2.ResumeLayout(false);
			this.toolStrip2.PerformLayout();
			this.toolStrip1.ResumeLayout(false);
			this.toolStrip1.PerformLayout();
			this.contextMenuStrip1.ResumeLayout(false);
			base.ResumeLayout(false);
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
