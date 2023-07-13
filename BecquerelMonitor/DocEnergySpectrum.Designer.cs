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
			this.toolStripSplitButton7 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.バックグラウンド表示ありToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.SubstractBgToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.ShowConToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.バックグラウンド表示なしToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton8 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.高精細表示ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.通常表示ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton3 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.バ\u30FCグラフToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.折れ線グラフToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton6 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.スム\u30FCジングなしToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.単純移動平均ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.加重移動平均ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton9 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.ピ\u30FCク表示ありToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.ピ\u30FCク表示なしToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStrip1 = new global::BecquerelMonitor.ToolStripEx();
			this.toolStripLabel1 = new global::System.Windows.Forms.ToolStripLabel();
			this.toolStripSplitButton1 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.カウント表示ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.cps表示ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton2 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.リニア表示ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.対数表示ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSplitButton5 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.なしToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.自動フィットToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.自動フィットBGToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator3 = new global::System.Windows.Forms.ToolStripSeparator();
			this.toolStripLabel3 = new global::System.Windows.Forms.ToolStripLabel();
			this.toolStripSplitButton4 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.チャネル表示ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.エネルギ\u30FC表示ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripButton2 = new global::System.Windows.Forms.ToolStripButton();
			this.toolStripButton1 = new global::System.Windows.Forms.ToolStripButton();
			this.toolStripSplitButton10 = new global::System.Windows.Forms.ToolStripSplitButton();
			this.showEnergyCalibrationToolToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.hideEnergyCalibrationToolToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.energySpectrumView1 = new global::BecquerelMonitor.EnergySpectrumView();
			this.contextMenuStrip1 = new global::System.Windows.Forms.ContextMenuStrip(this.components);
			this.toolStripMenuItem1 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.全チャネルを表示AToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator2 = new global::System.Windows.Forms.ToolStripSeparator();
			this.toolStripMenuItem2 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.下限閾値に設定LToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.上限閾値に設定HToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripMenuItem3 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.選択領域からROI定義作成SToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator1 = new global::System.Windows.Forms.ToolStripSeparator();
			this.保存SToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.閉じるCToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
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
				this.toolStripSplitButton7,
				this.toolStripSplitButton8,
				this.toolStripSplitButton3,
				this.toolStripSplitButton6,
				this.toolStripSplitButton9
			});
			this.toolStrip2.Name = "toolStrip2";
			this.toolStrip2.SuppressHighlighting = false;
			this.toolTip1.SetToolTip(this.toolStrip2, resources.GetString("toolStrip2.ToolTip"));
			resources.ApplyResources(this.toolStripLabel2, "toolStripLabel2");
			this.toolStripLabel2.Name = "toolStripLabel2";
			resources.ApplyResources(this.toolStripSplitButton7, "toolStripSplitButton7");
			this.toolStripSplitButton7.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton7.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.バックグラウンド表示ありToolStripMenuItem,
				this.バックグラウンド表示なしToolStripMenuItem,
				this.SubstractBgToolStripMenuItem,
				this.ShowConToolStripMenuItem
			});
			this.toolStripSplitButton7.Image = global::BecquerelMonitor.Properties.Resources.BG;
			this.toolStripSplitButton7.Name = "toolStripSplitButton7";
			this.toolStripSplitButton7.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton7_ButtonClick);
			this.toolStripSplitButton7.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton7_DropDownOpening);
			resources.ApplyResources(this.バックグラウンド表示ありToolStripMenuItem, "バックグラウンド表示ありToolStripMenuItem");
			this.バックグラウンド表示ありToolStripMenuItem.Name = "バックグラウンド表示ありToolStripMenuItem";
			this.バックグラウンド表示ありToolStripMenuItem.Click += new global::System.EventHandler(this.バックグラウンド表示ありToolStripMenuItem_Click);
            resources.ApplyResources(this.SubstractBgToolStripMenuItem, "SubstractBgToolStripMenuItem");
            this.SubstractBgToolStripMenuItem.Name = "SubstractBgToolStripMenuItem";
            this.SubstractBgToolStripMenuItem.Click += new global::System.EventHandler(this.SubstractBgToolStripMenuItem_Click);

            resources.ApplyResources(this.ShowConToolStripMenuItem, "ShowConToolStripMenuItem");
            this.ShowConToolStripMenuItem.Name = "ShowConToolStripMenuItem";
            this.ShowConToolStripMenuItem.Click += new global::System.EventHandler(this.ShowConToolStripMenuItem_Click);
            resources.ApplyResources(this.バックグラウンド表示なしToolStripMenuItem, "バックグラウンド表示なしToolStripMenuItem");
			this.バックグラウンド表示なしToolStripMenuItem.Name = "バックグラウンド表示なしToolStripMenuItem";
			this.バックグラウンド表示なしToolStripMenuItem.Click += new global::System.EventHandler(this.バックグラウンド表示なしToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton8, "toolStripSplitButton8");
			this.toolStripSplitButton8.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton8.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.高精細表示ToolStripMenuItem,
				this.通常表示ToolStripMenuItem
			});
			this.toolStripSplitButton8.Image = global::BecquerelMonitor.Properties.Resources.HD;
			this.toolStripSplitButton8.Name = "toolStripSplitButton8";
			this.toolStripSplitButton8.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton8_ButtonClick);
			this.toolStripSplitButton8.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton8_DropDownOpening);
			resources.ApplyResources(this.高精細表示ToolStripMenuItem, "高精細表示ToolStripMenuItem");
			this.高精細表示ToolStripMenuItem.Name = "高精細表示ToolStripMenuItem";
			this.高精細表示ToolStripMenuItem.Click += new global::System.EventHandler(this.高精細表示ToolStripMenuItem_Click);
			resources.ApplyResources(this.通常表示ToolStripMenuItem, "通常表示ToolStripMenuItem");
			this.通常表示ToolStripMenuItem.Name = "通常表示ToolStripMenuItem";
			this.通常表示ToolStripMenuItem.Click += new global::System.EventHandler(this.通常表示ToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton3, "toolStripSplitButton3");
			this.toolStripSplitButton3.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton3.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.バ\u30FCグラフToolStripMenuItem,
				this.折れ線グラフToolStripMenuItem
			});
			this.toolStripSplitButton3.Image = global::BecquerelMonitor.Properties.Resources.line;
			this.toolStripSplitButton3.Name = "toolStripSplitButton3";
			this.toolStripSplitButton3.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton3_ButtonClick);
			this.toolStripSplitButton3.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton3_DropDownOpening);
			resources.ApplyResources(this.バ\u30FCグラフToolStripMenuItem, "バーグラフToolStripMenuItem");
			this.バ\u30FCグラフToolStripMenuItem.Name = "バーグラフToolStripMenuItem";
			this.バ\u30FCグラフToolStripMenuItem.Click += new global::System.EventHandler(this.バ\u30FCグラフToolStripMenuItem_Click);
			resources.ApplyResources(this.折れ線グラフToolStripMenuItem, "折れ線グラフToolStripMenuItem");
			this.折れ線グラフToolStripMenuItem.Name = "折れ線グラフToolStripMenuItem";
			this.折れ線グラフToolStripMenuItem.Click += new global::System.EventHandler(this.折れ線グラフToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton6, "toolStripSplitButton6");
			this.toolStripSplitButton6.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton6.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.スム\u30FCジングなしToolStripMenuItem,
				this.単純移動平均ToolStripMenuItem,
				this.加重移動平均ToolStripMenuItem
			});
			this.toolStripSplitButton6.Image = global::BecquerelMonitor.Properties.Resources.NoSmooth;
			this.toolStripSplitButton6.Name = "toolStripSplitButton6";
			this.toolStripSplitButton6.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton6_ButtonClick);
			this.toolStripSplitButton6.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton6_DropDownOpening);
			resources.ApplyResources(this.スム\u30FCジングなしToolStripMenuItem, "スムージングなしToolStripMenuItem");
			this.スム\u30FCジングなしToolStripMenuItem.Name = "スムージングなしToolStripMenuItem";
			this.スム\u30FCジングなしToolStripMenuItem.Click += new global::System.EventHandler(this.スム\u30FCジングなしToolStripMenuItem_Click);
			resources.ApplyResources(this.単純移動平均ToolStripMenuItem, "単純移動平均ToolStripMenuItem");
			this.単純移動平均ToolStripMenuItem.Name = "単純移動平均ToolStripMenuItem";
			this.単純移動平均ToolStripMenuItem.Click += new global::System.EventHandler(this.単純移動平均ToolStripMenuItem_Click);
			resources.ApplyResources(this.加重移動平均ToolStripMenuItem, "加重移動平均ToolStripMenuItem");
			this.加重移動平均ToolStripMenuItem.Name = "加重移動平均ToolStripMenuItem";
			this.加重移動平均ToolStripMenuItem.Click += new global::System.EventHandler(this.加重移動平均ToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton9, "toolStripSplitButton9");
			this.toolStripSplitButton9.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton9.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.ピ\u30FCク表示ありToolStripMenuItem,
				this.ピ\u30FCク表示なしToolStripMenuItem
			});
			this.toolStripSplitButton9.Image = global::BecquerelMonitor.Properties.Resources.peak;
			this.toolStripSplitButton9.Name = "toolStripSplitButton9";
			this.toolStripSplitButton9.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton9_ButtonClick);
			this.toolStripSplitButton9.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton9_DropDownOpening);
			resources.ApplyResources(this.ピ\u30FCク表示ありToolStripMenuItem, "ピーク表示ありToolStripMenuItem");
			this.ピ\u30FCク表示ありToolStripMenuItem.Name = "ピーク表示ありToolStripMenuItem";
			this.ピ\u30FCク表示ありToolStripMenuItem.Click += new global::System.EventHandler(this.ピ\u30FCク表示ありToolStripMenuItem_Click);
			resources.ApplyResources(this.ピ\u30FCク表示なしToolStripMenuItem, "ピーク表示なしToolStripMenuItem");
			this.ピ\u30FCク表示なしToolStripMenuItem.Name = "ピーク表示なしToolStripMenuItem";
			this.ピ\u30FCク表示なしToolStripMenuItem.Click += new global::System.EventHandler(this.ピ\u30FCク表示なしToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStrip1, "toolStrip1");
			this.toolStrip1.ClickThrough = true;
			this.toolStrip1.ImageScalingSize = new global::System.Drawing.Size(18, 18);
			this.toolStrip1.Items.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.toolStripLabel1,
				this.toolStripSplitButton1,
				this.toolStripSplitButton2,
				this.toolStripSplitButton5,
				this.toolStripSeparator3,
				this.toolStripLabel3,
				this.toolStripSplitButton4,
				this.toolStripButton2,
				this.toolStripButton1,
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
				this.カウント表示ToolStripMenuItem,
				this.cps表示ToolStripMenuItem
			});
			this.toolStripSplitButton1.Image = global::BecquerelMonitor.Properties.Resources.cps;
			this.toolStripSplitButton1.Name = "toolStripSplitButton1";
			this.toolStripSplitButton1.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton1_ButtonClick);
			this.toolStripSplitButton1.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton1_DropDownOpening);
			resources.ApplyResources(this.カウント表示ToolStripMenuItem, "カウント表示ToolStripMenuItem");
			this.カウント表示ToolStripMenuItem.Name = "カウント表示ToolStripMenuItem";
			this.カウント表示ToolStripMenuItem.Click += new global::System.EventHandler(this.カウント表示ToolStripMenuItem_Click);
			resources.ApplyResources(this.cps表示ToolStripMenuItem, "cps表示ToolStripMenuItem");
			this.cps表示ToolStripMenuItem.Name = "cps表示ToolStripMenuItem";
			this.cps表示ToolStripMenuItem.Click += new global::System.EventHandler(this.cps表示ToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton2, "toolStripSplitButton2");
			this.toolStripSplitButton2.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton2.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.リニア表示ToolStripMenuItem,
				this.対数表示ToolStripMenuItem
			});
			this.toolStripSplitButton2.Image = global::BecquerelMonitor.Properties.Resources.log;
			this.toolStripSplitButton2.Name = "toolStripSplitButton2";
			this.toolStripSplitButton2.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton2_ButtonClick);
			this.toolStripSplitButton2.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton2_DropDownOpening);
			resources.ApplyResources(this.リニア表示ToolStripMenuItem, "リニア表示ToolStripMenuItem");
			this.リニア表示ToolStripMenuItem.Name = "リニア表示ToolStripMenuItem";
			this.リニア表示ToolStripMenuItem.Click += new global::System.EventHandler(this.リニア表示ToolStripMenuItem_Click);
			resources.ApplyResources(this.対数表示ToolStripMenuItem, "対数表示ToolStripMenuItem");
			this.対数表示ToolStripMenuItem.Name = "対数表示ToolStripMenuItem";
			this.対数表示ToolStripMenuItem.Click += new global::System.EventHandler(this.対数表示ToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSplitButton5, "toolStripSplitButton5");
			this.toolStripSplitButton5.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton5.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.なしToolStripMenuItem,
				this.自動フィットToolStripMenuItem,
				this.自動フィットBGToolStripMenuItem
			});
			this.toolStripSplitButton5.Image = global::BecquerelMonitor.Properties.Resources.fit1;
			this.toolStripSplitButton5.Name = "toolStripSplitButton5";
			this.toolStripSplitButton5.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton5_ButtonClick);
			this.toolStripSplitButton5.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton5_DropDownOpening);
			resources.ApplyResources(this.なしToolStripMenuItem, "なしToolStripMenuItem");
			this.なしToolStripMenuItem.Name = "なしToolStripMenuItem";
			this.なしToolStripMenuItem.Click += new global::System.EventHandler(this.なしToolStripMenuItem_Click);
			resources.ApplyResources(this.自動フィットToolStripMenuItem, "自動フィットToolStripMenuItem");
			this.自動フィットToolStripMenuItem.Name = "自動フィットToolStripMenuItem";
			this.自動フィットToolStripMenuItem.Click += new global::System.EventHandler(this.自動フィットToolStripMenuItem_Click);
			resources.ApplyResources(this.自動フィットBGToolStripMenuItem, "自動フィットBGToolStripMenuItem");
			this.自動フィットBGToolStripMenuItem.Name = "自動フィットBGToolStripMenuItem";
			this.自動フィットBGToolStripMenuItem.Click += new global::System.EventHandler(this.自動フィットBGToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSeparator3, "toolStripSeparator3");
			this.toolStripSeparator3.Name = "toolStripSeparator3";
			resources.ApplyResources(this.toolStripLabel3, "toolStripLabel3");
			this.toolStripLabel3.Name = "toolStripLabel3";
			resources.ApplyResources(this.toolStripSplitButton4, "toolStripSplitButton4");
			this.toolStripSplitButton4.DisplayStyle = global::System.Windows.Forms.ToolStripItemDisplayStyle.Image;
			this.toolStripSplitButton4.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.チャネル表示ToolStripMenuItem,
				this.エネルギ\u30FC表示ToolStripMenuItem
			});
			this.toolStripSplitButton4.Image = global::BecquerelMonitor.Properties.Resources.ene;
			this.toolStripSplitButton4.Name = "toolStripSplitButton4";
			this.toolStripSplitButton4.ButtonClick += new global::System.EventHandler(this.toolStripSplitButton4_ButtonClick);
			this.toolStripSplitButton4.DropDownOpening += new global::System.EventHandler(this.toolStripSplitButton4_DropDownOpening);
			resources.ApplyResources(this.チャネル表示ToolStripMenuItem, "チャネル表示ToolStripMenuItem");
			this.チャネル表示ToolStripMenuItem.Name = "チャネル表示ToolStripMenuItem";
			this.チャネル表示ToolStripMenuItem.Click += new global::System.EventHandler(this.チャネル表示ToolStripMenuItem_Click);
			resources.ApplyResources(this.エネルギ\u30FC表示ToolStripMenuItem, "エネルギー表示ToolStripMenuItem");
			this.エネルギ\u30FC表示ToolStripMenuItem.Name = "エネルギー表示ToolStripMenuItem";
			this.エネルギ\u30FC表示ToolStripMenuItem.Click += new global::System.EventHandler(this.エネルギ\u30FC表示ToolStripMenuItem_Click);
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
			this.energySpectrumView1.ActiveResultDataIndex = 0;
			this.energySpectrumView1.BackColor = global::System.Drawing.Color.White;
			this.energySpectrumView1.BackgroundMode = global::BecquerelMonitor.BackgroundMode.Visible;
			this.energySpectrumView1.ChartType = global::BecquerelMonitor.ChartType.LineChart;
			this.energySpectrumView1.CursorChannel = 0;
			this.energySpectrumView1.CursorX = -1;
			this.energySpectrumView1.DrawingMode = global::BecquerelMonitor.DrawingMode.Normal;
			this.energySpectrumView1.HorizontalScale = 1.0;
			this.energySpectrumView1.HorizontalUnit = global::BecquerelMonitor.HorizontalUnit.Energy;
			this.energySpectrumView1.Name = "energySpectrumView1";
			this.energySpectrumView1.PeakMode = global::BecquerelMonitor.PeakMode.Visible;
			this.energySpectrumView1.ResultDataList = null;
			this.energySpectrumView1.SelectionEnd = -1;
			this.energySpectrumView1.SelectionStart = -1;
			this.energySpectrumView1.SmoothingMethod = global::BecquerelMonitor.SmoothingMethod.None;
			this.toolTip1.SetToolTip(this.energySpectrumView1, resources.GetString("energySpectrumView1.ToolTip"));
			this.energySpectrumView1.VerticalFittingMode = global::BecquerelMonitor.VerticalFittingMode.MinMax;
			this.energySpectrumView1.VerticalScale = 1.0;
			this.energySpectrumView1.VerticalScaleType = global::BecquerelMonitor.VerticalScaleType.LogarithmicScale;
			this.energySpectrumView1.VerticalUnit = global::BecquerelMonitor.VerticalUnit.Counts;
			resources.ApplyResources(this.contextMenuStrip1, "contextMenuStrip1");
			this.contextMenuStrip1.Items.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.toolStripMenuItem1,
				this.全チャネルを表示AToolStripMenuItem,
				this.toolStripSeparator2,
				this.toolStripMenuItem2,
				this.toolStripMenuItem3,
				this.toolStripSeparator1,
				this.保存SToolStripMenuItem,
				this.閉じるCToolStripMenuItem
			});
			this.contextMenuStrip1.Name = "contextMenuStrip1";
			this.toolTip1.SetToolTip(this.contextMenuStrip1, resources.GetString("contextMenuStrip1.ToolTip"));
			this.contextMenuStrip1.Closed += new global::System.Windows.Forms.ToolStripDropDownClosedEventHandler(this.contextMenuStrip1_Closed);
			this.contextMenuStrip1.Opening += new global::System.ComponentModel.CancelEventHandler(this.contextMenuStrip1_Opening);
			resources.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
			this.toolStripMenuItem1.Name = "toolStripMenuItem1";
			this.toolStripMenuItem1.Click += new global::System.EventHandler(this.toolStripMenuItem1_Click);
			resources.ApplyResources(this.全チャネルを表示AToolStripMenuItem, "全チャネルを表示AToolStripMenuItem");
			this.全チャネルを表示AToolStripMenuItem.Name = "全チャネルを表示AToolStripMenuItem";
			this.全チャネルを表示AToolStripMenuItem.Click += new global::System.EventHandler(this.全チャネルを表示AToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSeparator2, "toolStripSeparator2");
			this.toolStripSeparator2.Name = "toolStripSeparator2";
			resources.ApplyResources(this.toolStripMenuItem2, "toolStripMenuItem2");
			this.toolStripMenuItem2.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.下限閾値に設定LToolStripMenuItem,
				this.上限閾値に設定HToolStripMenuItem
			});
			this.toolStripMenuItem2.Name = "toolStripMenuItem2";
			resources.ApplyResources(this.下限閾値に設定LToolStripMenuItem, "下限閾値に設定LToolStripMenuItem");
			this.下限閾値に設定LToolStripMenuItem.Name = "下限閾値に設定LToolStripMenuItem";
			this.下限閾値に設定LToolStripMenuItem.Click += new global::System.EventHandler(this.下限閾値に設定LToolStripMenuItem_Click);
			resources.ApplyResources(this.上限閾値に設定HToolStripMenuItem, "上限閾値に設定HToolStripMenuItem");
			this.上限閾値に設定HToolStripMenuItem.Name = "上限閾値に設定HToolStripMenuItem";
			this.上限閾値に設定HToolStripMenuItem.Click += new global::System.EventHandler(this.上限閾値に設定HToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripMenuItem3, "toolStripMenuItem3");
			this.toolStripMenuItem3.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.選択領域からROI定義作成SToolStripMenuItem
			});
			this.toolStripMenuItem3.Name = "toolStripMenuItem3";
			resources.ApplyResources(this.選択領域からROI定義作成SToolStripMenuItem, "選択領域からROI定義作成SToolStripMenuItem");
			this.選択領域からROI定義作成SToolStripMenuItem.Name = "選択領域からROI定義作成SToolStripMenuItem";
			this.選択領域からROI定義作成SToolStripMenuItem.Click += new global::System.EventHandler(this.選択領域からROI定義作成SToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			resources.ApplyResources(this.保存SToolStripMenuItem, "保存SToolStripMenuItem");
			this.保存SToolStripMenuItem.Name = "保存SToolStripMenuItem";
			this.保存SToolStripMenuItem.Click += new global::System.EventHandler(this.保存SToolStripMenuItem_Click);
			resources.ApplyResources(this.閉じるCToolStripMenuItem, "閉じるCToolStripMenuItem");
			this.閉じるCToolStripMenuItem.Name = "閉じるCToolStripMenuItem";
			this.閉じるCToolStripMenuItem.Click += new global::System.EventHandler(this.閉じるCToolStripMenuItem_Click);
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
		global::BecquerelMonitor.EnergySpectrumView energySpectrumView1;

		// Token: 0x04000113 RID: 275
		global::System.Windows.Forms.ContextMenuStrip contextMenuStrip1;

		// Token: 0x04000114 RID: 276
		global::System.Windows.Forms.ToolStripMenuItem 保存SToolStripMenuItem;

		// Token: 0x04000115 RID: 277
		global::System.Windows.Forms.ToolStripMenuItem 閉じるCToolStripMenuItem;

		// Token: 0x04000116 RID: 278
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator1;

		// Token: 0x04000117 RID: 279
		global::System.Windows.Forms.ToolTip toolTip1;

		// Token: 0x04000118 RID: 280
		global::System.Windows.Forms.ToolStripLabel toolStripLabel1;

		// Token: 0x04000119 RID: 281
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton1;

		// Token: 0x0400011A RID: 282
		global::System.Windows.Forms.ToolStripMenuItem cps表示ToolStripMenuItem;

		// Token: 0x0400011B RID: 283
		global::System.Windows.Forms.ToolStripMenuItem カウント表示ToolStripMenuItem;

		// Token: 0x0400011C RID: 284
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton2;

		// Token: 0x0400011D RID: 285
		global::System.Windows.Forms.ToolStripMenuItem 対数表示ToolStripMenuItem;

		// Token: 0x0400011E RID: 286
		global::System.Windows.Forms.ToolStripMenuItem リニア表示ToolStripMenuItem;

		// Token: 0x0400011F RID: 287
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton5;

		// Token: 0x04000120 RID: 288
		global::System.Windows.Forms.ToolStripMenuItem 自動フィットToolStripMenuItem;

		// Token: 0x04000121 RID: 289
		global::System.Windows.Forms.ToolStripMenuItem なしToolStripMenuItem;

		// Token: 0x04000122 RID: 290
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;

		// Token: 0x04000123 RID: 291
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator2;

		// Token: 0x04000124 RID: 292
		global::System.Windows.Forms.ToolStripContainer toolStripContainer1;

		// Token: 0x04000125 RID: 293
		global::System.Windows.Forms.ToolStripMenuItem 自動フィットBGToolStripMenuItem;

		// Token: 0x04000126 RID: 294
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator3;

		// Token: 0x04000127 RID: 295
		global::System.Windows.Forms.ToolStripLabel toolStripLabel3;

		// Token: 0x04000128 RID: 296
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton4;

		// Token: 0x04000129 RID: 297
		global::System.Windows.Forms.ToolStripMenuItem エネルギ\u30FC表示ToolStripMenuItem;

		// Token: 0x0400012A RID: 298
		global::System.Windows.Forms.ToolStripMenuItem チャネル表示ToolStripMenuItem;

		// Token: 0x0400012B RID: 299
		global::System.Windows.Forms.ToolStripButton toolStripButton1;

		// Token: 0x0400012C RID: 300
		global::System.Windows.Forms.ToolStripButton toolStripButton2;

		// Token: 0x0400012D RID: 301
		global::System.Windows.Forms.ToolStripMenuItem 全チャネルを表示AToolStripMenuItem;

		// Token: 0x0400012E RID: 302
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem2;

		// Token: 0x0400012F RID: 303
		global::System.Windows.Forms.ToolStripMenuItem 下限閾値に設定LToolStripMenuItem;

		// Token: 0x04000130 RID: 304
		global::System.Windows.Forms.ToolStripMenuItem 上限閾値に設定HToolStripMenuItem;

		// Token: 0x04000131 RID: 305
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem3;

		// Token: 0x04000132 RID: 306
		global::System.Windows.Forms.ToolStripMenuItem 選択領域からROI定義作成SToolStripMenuItem;

		// Token: 0x04000133 RID: 307
		global::System.Windows.Forms.ToolStripLabel toolStripLabel2;

		// Token: 0x04000134 RID: 308
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton3;

		// Token: 0x04000135 RID: 309
		global::System.Windows.Forms.ToolStripMenuItem バ\u30FCグラフToolStripMenuItem;

		// Token: 0x04000136 RID: 310
		global::System.Windows.Forms.ToolStripMenuItem 折れ線グラフToolStripMenuItem;

		// Token: 0x04000137 RID: 311
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton6;

		// Token: 0x04000138 RID: 312
		global::System.Windows.Forms.ToolStripMenuItem スム\u30FCジングなしToolStripMenuItem;

		// Token: 0x04000139 RID: 313
		global::System.Windows.Forms.ToolStripMenuItem 単純移動平均ToolStripMenuItem;

		// Token: 0x0400013A RID: 314
		global::System.Windows.Forms.ToolStripMenuItem 加重移動平均ToolStripMenuItem;

		// Token: 0x0400013B RID: 315
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton7;

		// Token: 0x0400013C RID: 316
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton8;

		// Token: 0x0400013D RID: 317
		global::System.Windows.Forms.ToolStripMenuItem バックグラウンド表示ありToolStripMenuItem;

        global::System.Windows.Forms.ToolStripMenuItem SubstractBgToolStripMenuItem;

        global::System.Windows.Forms.ToolStripMenuItem ShowConToolStripMenuItem;

        // Token: 0x0400013E RID: 318
        global::System.Windows.Forms.ToolStripMenuItem バックグラウンド表示なしToolStripMenuItem;

		// Token: 0x0400013F RID: 319
		global::System.Windows.Forms.ToolStripMenuItem 高精細表示ToolStripMenuItem;

		// Token: 0x04000140 RID: 320
		global::System.Windows.Forms.ToolStripMenuItem 通常表示ToolStripMenuItem;

		// Token: 0x04000141 RID: 321
		global::BecquerelMonitor.ToolStripEx toolStrip1;

		// Token: 0x04000142 RID: 322
		global::BecquerelMonitor.ToolStripEx toolStrip2;

		// Token: 0x04000143 RID: 323
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton9;

		// Token: 0x04000144 RID: 324
		global::System.Windows.Forms.ToolStripMenuItem ピ\u30FCク表示ありToolStripMenuItem;

		// Token: 0x04000145 RID: 325
		global::System.Windows.Forms.ToolStripMenuItem ピ\u30FCク表示なしToolStripMenuItem;

		// Token: 0x04000146 RID: 326
		global::System.Windows.Forms.ToolStripSplitButton toolStripSplitButton10;

		// Token: 0x04000147 RID: 327
		global::System.Windows.Forms.ToolStripMenuItem showEnergyCalibrationToolToolStripMenuItem;

		// Token: 0x04000148 RID: 328
		global::System.Windows.Forms.ToolStripMenuItem hideEnergyCalibrationToolToolStripMenuItem;
	}
}
