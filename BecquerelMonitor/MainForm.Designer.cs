using WeifenLuo.WinFormsUI.Docking;

namespace BecquerelMonitor
{
	// Token: 0x020000CF RID: 207
	public partial class MainForm : global::System.Windows.Forms.Form
	{
		// Token: 0x06000A3E RID: 2622 RVA: 0x0003AD70 File Offset: 0x00038F70
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000A3F RID: 2623 RVA: 0x0003AD98 File Offset: 0x00038F98
		void InitializeComponent()
		{


			this.components = new global::System.ComponentModel.Container();

            

			global::System.ComponentModel.ComponentResourceManager resources = new global::System.ComponentModel.ComponentResourceManager(typeof(global::BecquerelMonitor.MainForm));
			global::WeifenLuo.WinFormsUI.Docking.DockPanelSkin dockPanelSkin = new global::WeifenLuo.WinFormsUI.Docking.DockPanelSkin();
			global::WeifenLuo.WinFormsUI.Docking.AutoHideStripSkin autoHideStripSkin = new global::WeifenLuo.WinFormsUI.Docking.AutoHideStripSkin();
			global::WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient = new global::WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			global::WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient = new global::WeifenLuo.WinFormsUI.Docking.TabGradient();
			global::WeifenLuo.WinFormsUI.Docking.DockPaneStripSkin dockPaneStripSkin = new global::WeifenLuo.WinFormsUI.Docking.DockPaneStripSkin();
			global::WeifenLuo.WinFormsUI.Docking.DockPaneStripGradient dockPaneStripGradient = new global::WeifenLuo.WinFormsUI.Docking.DockPaneStripGradient();
			global::WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient2 = new global::WeifenLuo.WinFormsUI.Docking.TabGradient();
			global::WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient2 = new global::WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			global::WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient3 = new global::WeifenLuo.WinFormsUI.Docking.TabGradient();
			global::WeifenLuo.WinFormsUI.Docking.DockPaneStripToolWindowGradient dockPaneStripToolWindowGradient = new global::WeifenLuo.WinFormsUI.Docking.DockPaneStripToolWindowGradient();
			global::WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient4 = new global::WeifenLuo.WinFormsUI.Docking.TabGradient();
			global::WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient5 = new global::WeifenLuo.WinFormsUI.Docking.TabGradient();
			global::WeifenLuo.WinFormsUI.Docking.DockPanelGradient dockPanelGradient3 = new global::WeifenLuo.WinFormsUI.Docking.DockPanelGradient();
			global::WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient6 = new global::WeifenLuo.WinFormsUI.Docking.TabGradient();
			global::WeifenLuo.WinFormsUI.Docking.TabGradient tabGradient7 = new global::WeifenLuo.WinFormsUI.Docking.TabGradient();
			this.menuStrip1 = new global::BecquerelMonitor.MenuStripEx();
			this.ファイルFToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.新規スペクトルNToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.デ\u30FCタを開くToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.デ\u30FCタを保存SToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.デ\u30FCタを名前を付けて保存RToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.デ\u30FCタを閉じるCToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator6 = new global::System.Windows.Forms.ToolStripSeparator();
			this.toolStripMenuItem2 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator4 = new global::System.Windows.Forms.ToolStripSeparator();
			this.インポ\u30FCトIToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.ベクモニ旧形式v093bToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.AtomSpectraStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.ConcatSpectrumsStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.CutoffStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.NormalizeSpectrumStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.ApplyDeadTimeCorrectionStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.N42StripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.N42ExpStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.AtomSpectraExpStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.cSVFileToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.エクスポ\u30FCトEToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.cSVCToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.EffCalcMCFileToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.ECSVToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.fWHM用ToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator1 = new global::System.Windows.Forms.ToolStripSeparator();
			this.終了XToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.スペクトルSToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.新規スペクトルNToolStripMenuItem1 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.削除DToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.既存ファイルから追加FToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripMenuItem1 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.exportBgToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.toolStripSeparator7 = new global::System.Windows.Forms.ToolStripSeparator();
			this.測定開始SToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.測定停止TToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.デ\u30FCタ消去CToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.CloseAllToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.AutoSaveStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.CombineSpectrasToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
            this.表示TToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripMenuItem6 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.コントロ\u30FCルパネルCToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.試料情報SToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.スペクトル一覧LToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripMenuItem4 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.ピ\u30FCク検出DToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.パルス表示PToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripMenuItem5 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripMenuItem3 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator2 = new global::System.Windows.Forms.ToolStripSeparator();
			this.測定結果表示RToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator8 = new global::System.Windows.Forms.ToolStripSeparator();
			this.toolStripMenuItem7 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.userLayoutToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.expertLayoutToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator9 = new global::System.Windows.Forms.ToolStripSeparator();
			this.languageToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.oSDefaultToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.neutralToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.jaJPToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.ツ\u30FCルTToolStripMenuItem1 = new global::System.Windows.Forms.ToolStripMenuItem();
			this.デバイス構成の編集DToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.rOI定義RToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.NuclideDefinitionToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.NuclideSetToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.OpenConfigNToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.NucDB = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator5 = new global::System.Windows.Forms.ToolStripSeparator();
			this.基本設定BToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.ヘルプHToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.マニュアルMToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator3 = new global::System.Windows.Forms.ToolStripSeparator();
			this.バ\u30FCジョン情報AToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.UpdatesAToolStripMenuItem = new global::System.Windows.Forms.ToolStripMenuItem();
			this.statusStrip1 = new global::System.Windows.Forms.StatusStrip();
			this.toolStripStatusLabel1 = new global::System.Windows.Forms.ToolStripStatusLabel();
			this.toolStripStatusLabel2 = new global::System.Windows.Forms.ToolStripStatusLabel();
            this.toolStripStatusLabel3 = new global::System.Windows.Forms.ToolStripStatusLabel();
            this.dockPanel1 = new global::WeifenLuo.WinFormsUI.Docking.DockPanel();
			this.imageList1 = new global::System.Windows.Forms.ImageList(this.components);
			this.menuStrip1.SuspendLayout();
			this.statusStrip1.SuspendLayout();
			base.SuspendLayout();
			resources.ApplyResources(this.menuStrip1, "menuStrip1");
			this.menuStrip1.ClickThrough = true;
			this.menuStrip1.Items.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.ファイルFToolStripMenuItem,
				this.スペクトルSToolStripMenuItem,
				this.表示TToolStripMenuItem,
				this.ツ\u30FCルTToolStripMenuItem1,
				this.ヘルプHToolStripMenuItem
			});
			this.menuStrip1.Name = "menuStrip1";
			this.menuStrip1.SuppressHighlighting = false;
			resources.ApplyResources(this.ファイルFToolStripMenuItem, "ファイルFToolStripMenuItem");
			this.ファイルFToolStripMenuItem.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.新規スペクトルNToolStripMenuItem,
				this.デ\u30FCタを開くToolStripMenuItem,
				this.デ\u30FCタを保存SToolStripMenuItem,
				this.デ\u30FCタを名前を付けて保存RToolStripMenuItem,
				this.デ\u30FCタを閉じるCToolStripMenuItem,
                this.CombineSpectrasToolStripMenuItem,
                this.CloseAllToolStripMenuItem,
				this.toolStripSeparator6,
				this.toolStripMenuItem2,
				this.toolStripSeparator4,
				this.インポ\u30FCトIToolStripMenuItem,
				this.エクスポ\u30FCトEToolStripMenuItem,
                this.toolStripSeparator1,
				this.終了XToolStripMenuItem
			});
			this.ファイルFToolStripMenuItem.Name = "ファイルFToolStripMenuItem";
			this.ファイルFToolStripMenuItem.DropDownOpening += new global::System.EventHandler(this.ファイルFToolStripMenuItem_DropDownOpening);
			resources.ApplyResources(this.新規スペクトルNToolStripMenuItem, "新規スペクトルNToolStripMenuItem");
			this.新規スペクトルNToolStripMenuItem.Name = "新規スペクトルNToolStripMenuItem";
			this.新規スペクトルNToolStripMenuItem.Click += new global::System.EventHandler(this.新規スペクトルNToolStripMenuItem_Click);
			resources.ApplyResources(this.デ\u30FCタを開くToolStripMenuItem, "データを開くToolStripMenuItem");
			this.デ\u30FCタを開くToolStripMenuItem.Name = "データを開くToolStripMenuItem";
			this.デ\u30FCタを開くToolStripMenuItem.Click += new global::System.EventHandler(this.デ\u30FCタを開くToolStripMenuItem_Click);
			resources.ApplyResources(this.デ\u30FCタを保存SToolStripMenuItem, "データを保存SToolStripMenuItem");
			this.デ\u30FCタを保存SToolStripMenuItem.Name = "データを保存SToolStripMenuItem";
            this.デ\u30FCタを保存SToolStripMenuItem.Click += new global::System.EventHandler(this.デ\u30FCタを保存SToolStripMenuItem_Click);
            resources.ApplyResources(this.デ\u30FCタを名前を付けて保存RToolStripMenuItem, "データを名前を付けて保存RToolStripMenuItem");
			this.デ\u30FCタを名前を付けて保存RToolStripMenuItem.Name = "データを名前を付けて保存RToolStripMenuItem";
			this.デ\u30FCタを名前を付けて保存RToolStripMenuItem.Click += new global::System.EventHandler(this.デ\u30FCタを名前を付けて保存RToolStripMenuItem_Click);
			resources.ApplyResources(this.デ\u30FCタを閉じるCToolStripMenuItem, "データを閉じるCToolStripMenuItem");
			this.デ\u30FCタを閉じるCToolStripMenuItem.Name = "データを閉じるCToolStripMenuItem";
			this.デ\u30FCタを閉じるCToolStripMenuItem.Click += new global::System.EventHandler(this.デ\u30FCタを閉じるCToolStripMenuItem_Click);
			resources.ApplyResources(this.CloseAllToolStripMenuItem, "CloseAllToolStripMenuItem");
			this.CloseAllToolStripMenuItem.Name = "CloseAllToolStripMenuItem";
			this.CloseAllToolStripMenuItem.Click += new global::System.EventHandler(this.CloseAllToolStripMenuItem_Click);

            resources.ApplyResources(this.CombineSpectrasToolStripMenuItem, "CombineSpectrasToolStripMenuItem");
            this.CombineSpectrasToolStripMenuItem.Name = "CombineSpectrasToolStripMenuItem";
            this.CombineSpectrasToolStripMenuItem.Click += new global::System.EventHandler(this.CombineSpectrasToolStripMenuItem_Click);
            resources.ApplyResources(this.toolStripSeparator6, "toolStripSeparator6");
			this.toolStripSeparator6.Name = "toolStripSeparator6";
			resources.ApplyResources(this.toolStripMenuItem2, "toolStripMenuItem2");
			this.toolStripMenuItem2.Name = "toolStripMenuItem2";
			this.toolStripMenuItem2.Click += new global::System.EventHandler(this.toolStripMenuItem2_Click);
			resources.ApplyResources(this.toolStripSeparator4, "toolStripSeparator4");
			this.toolStripSeparator4.Name = "toolStripSeparator4";
			resources.ApplyResources(this.インポ\u30FCトIToolStripMenuItem, "インポートIToolStripMenuItem");
			this.インポ\u30FCトIToolStripMenuItem.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.AtomSpectraStripMenuItem,
				this.N42StripMenuItem,
				this.cSVFileToolStripMenuItem,
				this.EffCalcMCFileToolStripMenuItem,
                this.ベクモニ旧形式v093bToolStripMenuItem
			});
			this.インポ\u30FCトIToolStripMenuItem.Name = "インポートIToolStripMenuItem";
			resources.ApplyResources(this.ベクモニ旧形式v093bToolStripMenuItem, "ベクモニ旧形式v093bToolStripMenuItem");
			resources.ApplyResources(this.AtomSpectraStripMenuItem, "AtomSpectraStripMenuItem");
			resources.ApplyResources(this.N42StripMenuItem, "N42StripMenuItem");
			this.ベクモニ旧形式v093bToolStripMenuItem.Name = "ベクモニ旧形式v093bToolStripMenuItem";
			this.ベクモニ旧形式v093bToolStripMenuItem.Click += new global::System.EventHandler(this.ベクモニ旧形式v093bToolStripMenuItem_Click);
			this.AtomSpectraStripMenuItem.Name = "AtomSpectraStripMenuItem";
			this.AtomSpectraStripMenuItem.Click += new global::System.EventHandler(this.AtomSpectraStripMenuItem_Click);
			this.N42StripMenuItem.Name = "N42StripMenuItem";
			this.N42StripMenuItem.Click += new global::System.EventHandler(this.N42StripMenuItem_Click);
			resources.ApplyResources(this.cSVFileToolStripMenuItem, "cSVFileToolStripMenuItem");
			this.cSVFileToolStripMenuItem.Name = "cSVFileToolStripMenuItem";
			this.cSVFileToolStripMenuItem.Click += new global::System.EventHandler(this.cSVファイルCToolStripMenuItem_Click);
            resources.ApplyResources(this.EffCalcMCFileToolStripMenuItem, "EffCalcMCFileToolStripMenuItem");
            this.EffCalcMCFileToolStripMenuItem.Name = "EffCalcMCFileToolStripMenuItem";
            this.EffCalcMCFileToolStripMenuItem.Click += new global::System.EventHandler(this.EffCalcMCFileToolStripMenuItem_Click);
            resources.ApplyResources(this.エクスポ\u30FCトEToolStripMenuItem, "エクスポートEToolStripMenuItem");
			this.エクスポ\u30FCトEToolStripMenuItem.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.N42ExpStripMenuItem,
				this.AtomSpectraExpStripMenuItem,
				this.cSVCToolStripMenuItem,
				this.ECSVToolStripMenuItem,
				this.fWHM用ToolStripMenuItem
			});
			this.エクスポ\u30FCトEToolStripMenuItem.Name = "エクスポートEToolStripMenuItem";
			resources.ApplyResources(this.N42ExpStripMenuItem, "N42ExpStripMenuItem");
			this.N42ExpStripMenuItem.Name = "N42ExpStripMenuItem";
			this.N42ExpStripMenuItem.Click += new global::System.EventHandler(this.N42ExpStripMenuItem_Click);
			resources.ApplyResources(this.AtomSpectraExpStripMenuItem, "AtomSpectraExpStripMenuItem");
			this.AtomSpectraExpStripMenuItem.Name = "AtomSpectraExpStripMenuItem";
			this.AtomSpectraExpStripMenuItem.Click += new global::System.EventHandler(this.AtomSpectraExpStripMenuItem_Click);
			resources.ApplyResources(this.cSVCToolStripMenuItem, "cSVCToolStripMenuItem");
			this.cSVCToolStripMenuItem.Name = "cSVCToolStripMenuItem";
			this.cSVCToolStripMenuItem.Click += new global::System.EventHandler(this.cSVCToolStripMenuItem_Click);
			resources.ApplyResources(this.ECSVToolStripMenuItem, "ECSVToolStripMenuItem");
			this.ECSVToolStripMenuItem.Name = "ECSVToolStripMenuItem";
			this.ECSVToolStripMenuItem.Click += new global::System.EventHandler(this.ECSVToolStripMenuItem_Click);
			resources.ApplyResources(this.fWHM用ToolStripMenuItem, "fWHM用ToolStripMenuItem");
			this.fWHM用ToolStripMenuItem.Name = "fWHM用ToolStripMenuItem";
			this.fWHM用ToolStripMenuItem.Click += new global::System.EventHandler(this.fWHM用ToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSeparator1, "toolStripSeparator1");
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			resources.ApplyResources(this.終了XToolStripMenuItem, "終了XToolStripMenuItem");
			this.終了XToolStripMenuItem.Name = "終了XToolStripMenuItem";
			this.終了XToolStripMenuItem.Click += new global::System.EventHandler(this.終了XToolStripMenuItem_Click);
			resources.ApplyResources(this.スペクトルSToolStripMenuItem, "スペクトルSToolStripMenuItem");
			this.スペクトルSToolStripMenuItem.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.新規スペクトルNToolStripMenuItem1,
				this.削除DToolStripMenuItem,
				this.既存ファイルから追加FToolStripMenuItem,
				this.toolStripMenuItem1,
				this.exportBgToolStripMenuItem,
				this.ConcatSpectrumsStripMenuItem,
				this.CutoffStripMenuItem,
                this.NormalizeSpectrumStripMenuItem,
				this.ApplyDeadTimeCorrectionStripMenuItem,
                this.AutoSaveStripMenuItem,
                this.toolStripSeparator7,
				this.測定開始SToolStripMenuItem,
				this.測定停止TToolStripMenuItem,
				this.デ\u30FCタ消去CToolStripMenuItem
			});
			this.スペクトルSToolStripMenuItem.Name = "スペクトルSToolStripMenuItem";
			this.スペクトルSToolStripMenuItem.DropDownOpening += new global::System.EventHandler(this.スペクトルSToolStripMenuItem_DropDownOpening);
			resources.ApplyResources(this.新規スペクトルNToolStripMenuItem1, "新規スペクトルNToolStripMenuItem1");
			this.新規スペクトルNToolStripMenuItem1.Name = "新規スペクトルNToolStripMenuItem1";
			this.新規スペクトルNToolStripMenuItem1.Click += new global::System.EventHandler(this.新規スペクトルNToolStripMenuItem1_Click);
			resources.ApplyResources(this.削除DToolStripMenuItem, "削除DToolStripMenuItem");
			this.削除DToolStripMenuItem.Name = "削除DToolStripMenuItem";
			this.削除DToolStripMenuItem.Click += new global::System.EventHandler(this.削除DToolStripMenuItem_Click);
			resources.ApplyResources(this.既存ファイルから追加FToolStripMenuItem, "既存ファイルから追加FToolStripMenuItem");
			this.既存ファイルから追加FToolStripMenuItem.Name = "既存ファイルから追加FToolStripMenuItem";
			this.既存ファイルから追加FToolStripMenuItem.Click += new global::System.EventHandler(this.ファイルからスペクトルを追加FToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripMenuItem1, "toolStripMenuItem1");
			this.toolStripMenuItem1.Name = "toolStripMenuItem1";
			this.toolStripMenuItem1.Click += new global::System.EventHandler(this.toolStripMenuItem1_Click);
            resources.ApplyResources(this.exportBgToolStripMenuItem, "exportBgToolStripMenuItem");
            this.exportBgToolStripMenuItem.Name = "exportBgToolStripMenuItem";
            this.exportBgToolStripMenuItem.Click += new global::System.EventHandler(this.exportBgToolStripMenuItem_Click);
            resources.ApplyResources(this.ConcatSpectrumsStripMenuItem, "ConcatSpectrumsStripMenuItem");
			this.ConcatSpectrumsStripMenuItem.Name = "ConcatSpectrumsStripMenuItem";
			this.ConcatSpectrumsStripMenuItem.Click += new global::System.EventHandler(this.ConcatSpectrumsStripMenuItem_Click);
			resources.ApplyResources(this.CutoffStripMenuItem, "CutoffStripMenuItem");
			this.CutoffStripMenuItem.Name = "CutoffStripMenuItem";
			this.CutoffStripMenuItem.Click += new global::System.EventHandler(this.CutoffStripMenuItem_Click);
            resources.ApplyResources(this.NormalizeSpectrumStripMenuItem, "NormalizeSpectrumStripMenuItem");
            this.NormalizeSpectrumStripMenuItem.Name = "NormalizeSpectrumStripMenuItem";
            this.NormalizeSpectrumStripMenuItem.Click += new global::System.EventHandler(this.NormalizeSpectrumStripMenuItem_Click);
            resources.ApplyResources(this.ApplyDeadTimeCorrectionStripMenuItem, "ApplyDeadTimeCorrectionStripMenuItem");
            this.ApplyDeadTimeCorrectionStripMenuItem.Name = "ApplyDeadTimeCorrectionStripMenuItem";
            this.ApplyDeadTimeCorrectionStripMenuItem.Click += new global::System.EventHandler(this.ApplyDeadTimeCorrectionStripMenuItem_Click);
            resources.ApplyResources(this.AutoSaveStripMenuItem, "AutoSaveStripMenuItem");
            this.AutoSaveStripMenuItem.Name = "AutoSaveStripMenuItem";
            this.AutoSaveStripMenuItem.Click += new global::System.EventHandler(this.AutoSaveStripMenuItem_Click);
            this.AutoSaveStripMenuItem.CheckOnClick = true;
            resources.ApplyResources(this.toolStripSeparator7, "toolStripSeparator7");
			this.toolStripSeparator7.Name = "toolStripSeparator7";
			resources.ApplyResources(this.測定開始SToolStripMenuItem, "測定開始SToolStripMenuItem");
			this.測定開始SToolStripMenuItem.Image = global::BecquerelMonitor.Properties.Resources.start;
			this.測定開始SToolStripMenuItem.Name = "測定開始SToolStripMenuItem";
			this.測定開始SToolStripMenuItem.Click += new global::System.EventHandler(this.測定開始SToolStripMenuItem_Click);
			resources.ApplyResources(this.測定停止TToolStripMenuItem, "測定停止TToolStripMenuItem");
			this.測定停止TToolStripMenuItem.Image = global::BecquerelMonitor.Properties.Resources.stop;
			this.測定停止TToolStripMenuItem.Name = "測定停止TToolStripMenuItem";
			this.測定停止TToolStripMenuItem.Click += new global::System.EventHandler(this.測定停止TToolStripMenuItem_Click);
			resources.ApplyResources(this.デ\u30FCタ消去CToolStripMenuItem, "データ消去CToolStripMenuItem");
			this.デ\u30FCタ消去CToolStripMenuItem.Image = global::BecquerelMonitor.Properties.Resources.clear;
			this.デ\u30FCタ消去CToolStripMenuItem.Name = "データ消去CToolStripMenuItem";
			this.デ\u30FCタ消去CToolStripMenuItem.Click += new global::System.EventHandler(this.デ\u30FCタ消去CToolStripMenuItem_Click);
			resources.ApplyResources(this.表示TToolStripMenuItem, "表示TToolStripMenuItem");
			this.表示TToolStripMenuItem.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.toolStripMenuItem6,
				this.コントロ\u30FCルパネルCToolStripMenuItem,
				this.試料情報SToolStripMenuItem,
				this.スペクトル一覧LToolStripMenuItem,
				this.toolStripMenuItem4,
				this.ピ\u30FCク検出DToolStripMenuItem,
				this.パルス表示PToolStripMenuItem,
				this.toolStripMenuItem5,
				this.toolStripMenuItem3,
				this.toolStripSeparator2,
				this.測定結果表示RToolStripMenuItem,
				this.toolStripSeparator8,
				this.toolStripMenuItem7,
				this.toolStripSeparator9,
				this.languageToolStripMenuItem
			});
			this.表示TToolStripMenuItem.Name = "表示TToolStripMenuItem";
			resources.ApplyResources(this.toolStripMenuItem6, "toolStripMenuItem6");
			this.toolStripMenuItem6.Name = "toolStripMenuItem6";
			this.toolStripMenuItem6.Click += new global::System.EventHandler(this.toolStripMenuItem6_Click);
			resources.ApplyResources(this.コントロ\u30FCルパネルCToolStripMenuItem, "コントロールパネルCToolStripMenuItem");
			this.コントロ\u30FCルパネルCToolStripMenuItem.Name = "コントロールパネルCToolStripMenuItem";
			this.コントロ\u30FCルパネルCToolStripMenuItem.Click += new global::System.EventHandler(this.コントロ\u30FCルパネルCToolStripMenuItem_Click);
			resources.ApplyResources(this.試料情報SToolStripMenuItem, "試料情報SToolStripMenuItem");
			this.試料情報SToolStripMenuItem.Name = "試料情報SToolStripMenuItem";
			this.試料情報SToolStripMenuItem.Click += new global::System.EventHandler(this.試料情報SToolStripMenuItem_Click);
			resources.ApplyResources(this.スペクトル一覧LToolStripMenuItem, "スペクトル一覧LToolStripMenuItem");
			this.スペクトル一覧LToolStripMenuItem.Name = "スペクトル一覧LToolStripMenuItem";
			this.スペクトル一覧LToolStripMenuItem.Click += new global::System.EventHandler(this.スペクトル一覧LToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripMenuItem4, "toolStripMenuItem4");
			this.toolStripMenuItem4.Name = "toolStripMenuItem4";
			this.toolStripMenuItem4.Click += new global::System.EventHandler(this.toolStripMenuItem4_Click);
			resources.ApplyResources(this.ピ\u30FCク検出DToolStripMenuItem, "ピーク検出DToolStripMenuItem");
			this.ピ\u30FCク検出DToolStripMenuItem.Name = "ピーク検出DToolStripMenuItem";
			this.ピ\u30FCク検出DToolStripMenuItem.Click += new global::System.EventHandler(this.ピ\u30FCク検出DToolStripMenuItem_Click);
			resources.ApplyResources(this.パルス表示PToolStripMenuItem, "パルス表示PToolStripMenuItem");
			this.パルス表示PToolStripMenuItem.Name = "パルス表示PToolStripMenuItem";
			this.パルス表示PToolStripMenuItem.Click += new global::System.EventHandler(this.パルス表示PToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripMenuItem5, "toolStripMenuItem5");
			this.toolStripMenuItem5.Name = "toolStripMenuItem5";
			this.toolStripMenuItem5.Click += new global::System.EventHandler(this.toolStripMenuItem5_Click);
			resources.ApplyResources(this.toolStripMenuItem3, "toolStripMenuItem3");
			this.toolStripMenuItem3.Name = "toolStripMenuItem3";
			this.toolStripMenuItem3.Click += new global::System.EventHandler(this.toolStripMenuItem3_Click);
			resources.ApplyResources(this.toolStripSeparator2, "toolStripSeparator2");
			this.toolStripSeparator2.Name = "toolStripSeparator2";
			resources.ApplyResources(this.測定結果表示RToolStripMenuItem, "測定結果表示RToolStripMenuItem");
			this.測定結果表示RToolStripMenuItem.Name = "測定結果表示RToolStripMenuItem";
			this.測定結果表示RToolStripMenuItem.Click += new global::System.EventHandler(this.測定結果表示RToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSeparator8, "toolStripSeparator8");
			this.toolStripSeparator8.Name = "toolStripSeparator8";
			resources.ApplyResources(this.toolStripMenuItem7, "toolStripMenuItem7");
			this.toolStripMenuItem7.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.userLayoutToolStripMenuItem,
				this.expertLayoutToolStripMenuItem
			});
			this.toolStripMenuItem7.Name = "toolStripMenuItem7";
			resources.ApplyResources(this.userLayoutToolStripMenuItem, "userLayoutToolStripMenuItem");
			this.userLayoutToolStripMenuItem.Name = "userLayoutToolStripMenuItem";
			this.userLayoutToolStripMenuItem.Click += new global::System.EventHandler(this.userLayoutToolStripMenuItem_Click);
			resources.ApplyResources(this.expertLayoutToolStripMenuItem, "expertLayoutToolStripMenuItem");
			this.expertLayoutToolStripMenuItem.Name = "expertLayoutToolStripMenuItem";
			this.expertLayoutToolStripMenuItem.Click += new global::System.EventHandler(this.expertLayoutToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSeparator9, "toolStripSeparator9");
			this.toolStripSeparator9.Name = "toolStripSeparator9";
			resources.ApplyResources(this.languageToolStripMenuItem, "languageToolStripMenuItem");
			this.languageToolStripMenuItem.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.oSDefaultToolStripMenuItem,
				this.neutralToolStripMenuItem,
				this.jaJPToolStripMenuItem
			});
			this.languageToolStripMenuItem.Name = "languageToolStripMenuItem";
			resources.ApplyResources(this.oSDefaultToolStripMenuItem, "oSDefaultToolStripMenuItem");
			this.oSDefaultToolStripMenuItem.Name = "oSDefaultToolStripMenuItem";
			this.oSDefaultToolStripMenuItem.Click += new global::System.EventHandler(this.oSDefaultToolStripMenuItem_Click);
			resources.ApplyResources(this.neutralToolStripMenuItem, "neutralToolStripMenuItem");
			this.neutralToolStripMenuItem.Name = "neutralToolStripMenuItem";
			this.neutralToolStripMenuItem.Click += new global::System.EventHandler(this.neutralToolStripMenuItem_Click);
			resources.ApplyResources(this.jaJPToolStripMenuItem, "jaJPToolStripMenuItem");
			this.jaJPToolStripMenuItem.Name = "jaJPToolStripMenuItem";
			this.jaJPToolStripMenuItem.Click += new global::System.EventHandler(this.jaJPToolStripMenuItem_Click);
			resources.ApplyResources(this.ツ\u30FCルTToolStripMenuItem1, "ツールTToolStripMenuItem1");
			this.ツ\u30FCルTToolStripMenuItem1.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.デバイス構成の編集DToolStripMenuItem,
				this.rOI定義RToolStripMenuItem,
				this.NuclideDefinitionToolStripMenuItem,
				this.NuclideSetToolStripMenuItem,
				this.NucDB,
				this.OpenConfigNToolStripMenuItem,
				this.toolStripSeparator5,
				this.基本設定BToolStripMenuItem
			});
			this.ツ\u30FCルTToolStripMenuItem1.Name = "ツールTToolStripMenuItem1";
			resources.ApplyResources(this.デバイス構成の編集DToolStripMenuItem, "デバイス構成の編集DToolStripMenuItem");
			this.デバイス構成の編集DToolStripMenuItem.Name = "デバイス構成の編集DToolStripMenuItem";
			this.デバイス構成の編集DToolStripMenuItem.Click += new global::System.EventHandler(this.デバイス構成定義DToolStripMenuItem_Click);
			resources.ApplyResources(this.rOI定義RToolStripMenuItem, "rOI定義RToolStripMenuItem");
			this.rOI定義RToolStripMenuItem.Name = "rOI定義RToolStripMenuItem";
			this.rOI定義RToolStripMenuItem.Click += new global::System.EventHandler(this.rOI定義RToolStripMenuItem_Click);
			resources.ApplyResources(this.NuclideDefinitionToolStripMenuItem, "NuclideDefinitionToolStripMenuItem");
			this.NuclideDefinitionToolStripMenuItem.Name = "NuclideDefinitionToolStripMenuItem";
			this.NuclideDefinitionToolStripMenuItem.Click += new global::System.EventHandler(this.NuclideDefinitionToolStripMenuItem_Click);
			resources.ApplyResources(this.NuclideSetToolStripMenuItem, "NuclideSetToolStripMenuItem");
			this.NuclideSetToolStripMenuItem.Name = "NuclideSetToolStripMenuItem";
			this.NuclideSetToolStripMenuItem.Click += new global::System.EventHandler(this.NuclideSetToolStripMenuItem_Click);
			resources.ApplyResources(this.OpenConfigNToolStripMenuItem, "OpenConfigNToolStripMenuItem");
			this.OpenConfigNToolStripMenuItem.Name = "OpenConfigNToolStripMenuItem";
			this.OpenConfigNToolStripMenuItem.Click += new global::System.EventHandler(this.OpenConfigNToolStripMenuItem_Click);
			resources.ApplyResources(this.NucDB, "NucDB");
			this.NucDB.Name = "NucDB";
			this.NucDB.Click += new global::System.EventHandler(this.NucDB_Click);
			resources.ApplyResources(this.toolStripSeparator5, "toolStripSeparator5");
			this.toolStripSeparator5.Name = "toolStripSeparator5";
			resources.ApplyResources(this.基本設定BToolStripMenuItem, "基本設定BToolStripMenuItem");
			this.基本設定BToolStripMenuItem.Name = "基本設定BToolStripMenuItem";
			this.基本設定BToolStripMenuItem.Click += new global::System.EventHandler(this.基本設定BToolStripMenuItem_Click);
			resources.ApplyResources(this.ヘルプHToolStripMenuItem, "ヘルプHToolStripMenuItem");
			this.ヘルプHToolStripMenuItem.DropDownItems.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.マニュアルMToolStripMenuItem,
				this.toolStripSeparator3,
				this.UpdatesAToolStripMenuItem,
				this.バ\u30FCジョン情報AToolStripMenuItem
			});
			this.ヘルプHToolStripMenuItem.Name = "ヘルプHToolStripMenuItem";
			resources.ApplyResources(this.マニュアルMToolStripMenuItem, "マニュアルMToolStripMenuItem");
			this.マニュアルMToolStripMenuItem.Name = "マニュアルMToolStripMenuItem";
			this.マニュアルMToolStripMenuItem.Click += new global::System.EventHandler(this.マニュアルMToolStripMenuItem_Click);
			resources.ApplyResources(this.toolStripSeparator3, "toolStripSeparator3");
			this.toolStripSeparator3.Name = "toolStripSeparator3";
			resources.ApplyResources(this.バ\u30FCジョン情報AToolStripMenuItem, "バージョン情報AToolStripMenuItem");
			this.バ\u30FCジョン情報AToolStripMenuItem.Name = "バージョン情報AToolStripMenuItem";
			this.バ\u30FCジョン情報AToolStripMenuItem.Click += new global::System.EventHandler(this.バ\u30FCジョン情報AToolStripMenuItem_Click);

			resources.ApplyResources(this.UpdatesAToolStripMenuItem, "UpdatesAToolStripMenuItem");
			this.UpdatesAToolStripMenuItem.Name = "UpdatesAToolStripMenuItem";
			this.UpdatesAToolStripMenuItem.Click += new global::System.EventHandler(this.UpdatesAToolStripMenuItem_Click);

			resources.ApplyResources(this.statusStrip1, "statusStrip1");
			this.statusStrip1.Items.AddRange(new global::System.Windows.Forms.ToolStripItem[]
			{
				this.toolStripStatusLabel1,
				this.toolStripStatusLabel2,
                this.toolStripStatusLabel3,
            });
			this.statusStrip1.Name = "statusStrip1";
			resources.ApplyResources(this.toolStripStatusLabel1, "toolStripStatusLabel1");
			this.toolStripStatusLabel1.Name = "toolStripStatusLabel1";
			this.toolStripStatusLabel1.AutoSize = true;
			resources.ApplyResources(this.toolStripStatusLabel2, "toolStripStatusLabel2");
			this.toolStripStatusLabel2.Name = "toolStripStatusLabel2";
			this.toolStripStatusLabel2.Spring = true;
            resources.ApplyResources(this.toolStripStatusLabel3, "toolStripStatusLabel3");
            this.toolStripStatusLabel3.Name = "toolStripStatusLabel3";
            this.toolStripStatusLabel3.AutoSize = true;
            resources.ApplyResources(this.dockPanel1, "dockPanel1");
			this.dockPanel1.BackColor = global::System.Drawing.SystemColors.Control;
			this.dockPanel1.DefaultFloatWindowSize = new global::System.Drawing.Size(400, 300);
			this.dockPanel1.DockBackColor = global::System.Drawing.SystemColors.ControlDark;
			this.dockPanel1.Name = "dockPanel1";
			this.dockPanel1.ShowDocumentIcon = true;
			this.dockPanel1.Theme = new VS2015BlueTheme();
			dockPanelGradient.EndColor = global::System.Drawing.SystemColors.ControlLight;
			dockPanelGradient.StartColor = global::System.Drawing.SystemColors.ControlLight;
			autoHideStripSkin.DockStripGradient = dockPanelGradient;
			tabGradient.EndColor = global::System.Drawing.SystemColors.Control;
			tabGradient.StartColor = global::System.Drawing.SystemColors.Control;
			tabGradient.TextColor = global::System.Drawing.SystemColors.ControlDarkDark;
			autoHideStripSkin.TabGradient = tabGradient;
            autoHideStripSkin.TextFont = new global::System.Drawing.Font("Microsoft Sans Serif", 8f);
			dockPanelSkin.AutoHideStripSkin = autoHideStripSkin;
			tabGradient2.EndColor = global::System.Drawing.SystemColors.ControlLightLight;
			tabGradient2.StartColor = global::System.Drawing.SystemColors.ControlLightLight;
			tabGradient2.TextColor = global::System.Drawing.SystemColors.ControlText;
			dockPaneStripGradient.ActiveTabGradient = tabGradient2;
			dockPanelGradient2.EndColor = global::System.Drawing.SystemColors.Control;
			dockPanelGradient2.StartColor = global::System.Drawing.SystemColors.Control;
			dockPaneStripGradient.DockStripGradient = dockPanelGradient2;
			tabGradient3.EndColor = global::System.Drawing.SystemColors.ControlLight;
			tabGradient3.StartColor = global::System.Drawing.SystemColors.ControlLight;
			tabGradient3.TextColor = global::System.Drawing.SystemColors.ControlText;
			dockPaneStripGradient.InactiveTabGradient = tabGradient3;
			dockPaneStripSkin.DocumentGradient = dockPaneStripGradient;
            dockPaneStripSkin.TextFont = new global::System.Drawing.Font("Microsoft Sans Serif", 8f);//("メイリオ", 9f);
			tabGradient4.EndColor = global::System.Drawing.SystemColors.ActiveCaption;
			tabGradient4.LinearGradientMode = global::System.Drawing.Drawing2D.LinearGradientMode.Vertical;
			tabGradient4.StartColor = global::System.Drawing.SystemColors.GradientActiveCaption;
			tabGradient4.TextColor = global::System.Drawing.SystemColors.ActiveCaptionText;
			dockPaneStripToolWindowGradient.ActiveCaptionGradient = tabGradient4;
			tabGradient5.EndColor = global::System.Drawing.SystemColors.Control;
			tabGradient5.StartColor = global::System.Drawing.SystemColors.Control;
			tabGradient5.TextColor = global::System.Drawing.SystemColors.ControlText;
			dockPaneStripToolWindowGradient.ActiveTabGradient = tabGradient5;
			dockPanelGradient3.EndColor = global::System.Drawing.SystemColors.ControlLight;
			dockPanelGradient3.StartColor = global::System.Drawing.SystemColors.ControlLight;
			dockPaneStripToolWindowGradient.DockStripGradient = dockPanelGradient3;
			tabGradient6.EndColor = global::System.Drawing.SystemColors.GradientInactiveCaption;
			tabGradient6.LinearGradientMode = global::System.Drawing.Drawing2D.LinearGradientMode.Vertical;
			tabGradient6.StartColor = global::System.Drawing.SystemColors.GradientInactiveCaption;
			tabGradient6.TextColor = global::System.Drawing.SystemColors.ControlText;
			dockPaneStripToolWindowGradient.InactiveCaptionGradient = tabGradient6;
			tabGradient7.EndColor = global::System.Drawing.Color.Transparent;
			tabGradient7.StartColor = global::System.Drawing.Color.Transparent;
			tabGradient7.TextColor = global::System.Drawing.SystemColors.ControlDarkDark;
			dockPaneStripToolWindowGradient.InactiveTabGradient = tabGradient7;
			dockPaneStripSkin.ToolWindowGradient = dockPaneStripToolWindowGradient;
			dockPanelSkin.DockPaneStripSkin = dockPaneStripSkin;
			//this.dockPanel1.Skin = dockPanelSkin;
			this.dockPanel1.ActiveDocumentChanged += new global::System.EventHandler(this.dockPanel1_ActiveDocumentChanged);
			this.dockPanel1.DocumentStyle = DocumentStyle.DockingMdi;
			this.dockPanel1.ShowAutoHideContentOnHover = true;
			this.dockPanel1.ContentRemoved += new global::System.EventHandler<global::WeifenLuo.WinFormsUI.Docking.DockContentEventArgs>(this.dockPanel1_ContentRemoved);
			this.imageList1.ImageStream = (global::System.Windows.Forms.ImageListStreamer)resources.GetObject("imageList1.ImageStream");
			this.imageList1.TransparentColor = global::System.Drawing.Color.Transparent;
			this.imageList1.Images.SetKeyName(0, "start.ico");
			this.imageList1.Images.SetKeyName(1, "stop.ico");
			this.imageList1.Images.SetKeyName(2, "clear.ico");
			this.imageList1.Images.SetKeyName(3, "reload.ico");
			resources.ApplyResources(this, "$this");
			this.AllowDrop = true;
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Font;
			base.Controls.Add(this.dockPanel1);
			base.Controls.Add(this.statusStrip1);
			base.Controls.Add(this.menuStrip1);
			base.IsMdiContainer = true;
			base.MainMenuStrip = this.menuStrip1;
			base.Name = "MainForm";
			base.FormClosing += new global::System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
			base.Load += new global::System.EventHandler(this.MainForm_Load);
			base.DragDrop += new global::System.Windows.Forms.DragEventHandler(this.MainForm_DragDrop);
			base.DragEnter += new global::System.Windows.Forms.DragEventHandler(this.MainForm_DragEnter);
			this.menuStrip1.ResumeLayout(false);
			this.menuStrip1.PerformLayout();
			this.statusStrip1.ResumeLayout(false);
			this.statusStrip1.PerformLayout();

			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000587 RID: 1415
		global::System.ComponentModel.IContainer components;

		// Token: 0x04000588 RID: 1416
		global::System.Windows.Forms.ToolStripMenuItem ファイルFToolStripMenuItem;

		// Token: 0x04000589 RID: 1417
		global::System.Windows.Forms.ToolStripMenuItem 終了XToolStripMenuItem;

		// Token: 0x0400058A RID: 1418
		global::System.Windows.Forms.StatusStrip statusStrip1;

		// Token: 0x0400058B RID: 1419
		global::System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel1;

		// Token: 0x0400058C RID: 1420
		global::System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel2;

        global::System.Windows.Forms.ToolStripStatusLabel toolStripStatusLabel3;

        // Token: 0x0400058D RID: 1421
        global::System.Windows.Forms.ToolStripMenuItem 表示TToolStripMenuItem;

		// Token: 0x0400058E RID: 1422
		global::System.Windows.Forms.ToolStripMenuItem コントロ\u30FCルパネルCToolStripMenuItem;

		// Token: 0x0400058F RID: 1423
		global::System.Windows.Forms.ToolStripMenuItem パルス表示PToolStripMenuItem;

		// Token: 0x04000590 RID: 1424
		global::System.Windows.Forms.ToolStripMenuItem 新規スペクトルNToolStripMenuItem;

		// Token: 0x04000591 RID: 1425
		global::System.Windows.Forms.ToolStripMenuItem デ\u30FCタを開くToolStripMenuItem;

		// Token: 0x04000592 RID: 1426
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator1;

		// Token: 0x04000593 RID: 1427
		global::System.Windows.Forms.ToolStripMenuItem 測定結果表示RToolStripMenuItem;

		// Token: 0x04000594 RID: 1428
		global::System.Windows.Forms.ToolStripMenuItem ツ\u30FCルTToolStripMenuItem1;

		// Token: 0x04000595 RID: 1429
		global::System.Windows.Forms.ToolStripMenuItem デバイス構成の編集DToolStripMenuItem;

		// Token: 0x04000596 RID: 1430
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator2;

		// Token: 0x04000597 RID: 1431
		global::System.Windows.Forms.ToolStripMenuItem デ\u30FCタを閉じるCToolStripMenuItem;

		// Token: 0x04000598 RID: 1432
		global::System.Windows.Forms.ToolStripMenuItem デ\u30FCタを保存SToolStripMenuItem;

		// Token: 0x04000599 RID: 1433
		global::System.Windows.Forms.ToolStripMenuItem デ\u30FCタを名前を付けて保存RToolStripMenuItem;

		// Token: 0x0400059A RID: 1434
		global::System.Windows.Forms.ToolStripMenuItem rOI定義RToolStripMenuItem;

		// Token: 0x0400059B RID: 1435
		global::System.Windows.Forms.ToolStripMenuItem ヘルプHToolStripMenuItem;

		// Token: 0x0400059C RID: 1436
		global::System.Windows.Forms.ToolStripMenuItem マニュアルMToolStripMenuItem;

		// Token: 0x0400059D RID: 1437
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator3;

		// Token: 0x0400059E RID: 1438
		global::System.Windows.Forms.ToolStripMenuItem バ\u30FCジョン情報AToolStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem UpdatesAToolStripMenuItem;

		// Token: 0x0400059F RID: 1439
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator4;

		// Token: 0x040005A0 RID: 1440
		global::System.Windows.Forms.ToolStripMenuItem インポ\u30FCトIToolStripMenuItem;

		// Token: 0x040005A1 RID: 1441
		global::System.Windows.Forms.ToolStripMenuItem エクスポ\u30FCトEToolStripMenuItem;

		// Token: 0x040005A2 RID: 1442
		global::System.Windows.Forms.ToolStripMenuItem cSVCToolStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem ECSVToolStripMenuItem;

		// Token: 0x040005A3 RID: 1443
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator5;

		// Token: 0x040005A4 RID: 1444
		global::System.Windows.Forms.ToolStripMenuItem 基本設定BToolStripMenuItem;

		// Token: 0x040005A5 RID: 1445
		global::WeifenLuo.WinFormsUI.Docking.DockPanel dockPanel1;

		// Token: 0x040005A6 RID: 1446
		global::System.Windows.Forms.ToolStripMenuItem 試料情報SToolStripMenuItem;

		// Token: 0x040005A7 RID: 1447
		global::System.Windows.Forms.ToolStripMenuItem ベクモニ旧形式v093bToolStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem AtomSpectraStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem N42StripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem N42ExpStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem AtomSpectraExpStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem ConcatSpectrumsStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem CutoffStripMenuItem;

        global::System.Windows.Forms.ToolStripMenuItem NormalizeSpectrumStripMenuItem;

        global::System.Windows.Forms.ToolStripMenuItem ApplyDeadTimeCorrectionStripMenuItem;

        // Token: 0x040005A8 RID: 1448
        global::System.Windows.Forms.ToolStripMenuItem スペクトル一覧LToolStripMenuItem;

		// Token: 0x040005A9 RID: 1449
		global::System.Windows.Forms.ToolStripMenuItem スペクトルSToolStripMenuItem;

		// Token: 0x040005AA RID: 1450
		global::System.Windows.Forms.ToolStripMenuItem 新規スペクトルNToolStripMenuItem1;

		// Token: 0x040005AB RID: 1451
		global::System.Windows.Forms.ToolStripMenuItem 削除DToolStripMenuItem;

		// Token: 0x040005AC RID: 1452
		global::System.Windows.Forms.ToolStripMenuItem 既存ファイルから追加FToolStripMenuItem;

		// Token: 0x040005AD RID: 1453
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator7;

		// Token: 0x040005AE RID: 1454
		global::System.Windows.Forms.ToolStripMenuItem 測定開始SToolStripMenuItem;

		// Token: 0x040005AF RID: 1455
		global::System.Windows.Forms.ToolStripMenuItem 測定停止TToolStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem EffCalcMCFileToolStripMenuItem;

        // Token: 0x040005B0 RID: 1456
        global::System.Windows.Forms.ToolStripMenuItem デ\u30FCタ消去CToolStripMenuItem;

		// Token: 0x040005B1 RID: 1457
		global::System.Windows.Forms.ImageList imageList1;

		// Token: 0x040005B2 RID: 1458
		global::BecquerelMonitor.MenuStripEx menuStrip1;

		// Token: 0x040005B3 RID: 1459
		global::System.Windows.Forms.ToolStripMenuItem ピ\u30FCク検出DToolStripMenuItem;

        global::System.Windows.Forms.ToolStripMenuItem AutoSaveStripMenuItem;

		// Token: 0x040005B4 RID: 1460
		global::System.Windows.Forms.ToolStripMenuItem NuclideDefinitionToolStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem NuclideSetToolStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem OpenConfigNToolStripMenuItem;

		global::System.Windows.Forms.ToolStripMenuItem NucDB;

		// Token: 0x040005B5 RID: 1461
		global::System.Windows.Forms.ToolStripMenuItem fWHM用ToolStripMenuItem;

		// Token: 0x040005B6 RID: 1462
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;

		global::System.Windows.Forms.ToolStripMenuItem exportBgToolStripMenuItem;

		// Token: 0x040005B7 RID: 1463
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem2;

		global::System.Windows.Forms.ToolStripMenuItem CloseAllToolStripMenuItem;

        global::System.Windows.Forms.ToolStripMenuItem CombineSpectrasToolStripMenuItem;

        // Token: 0x040005B8 RID: 1464
        global::System.Windows.Forms.ToolStripSeparator toolStripSeparator6;

		// Token: 0x040005B9 RID: 1465
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator8;

		// Token: 0x040005BA RID: 1466
		global::System.Windows.Forms.ToolStripMenuItem languageToolStripMenuItem;

		// Token: 0x040005BB RID: 1467
		global::System.Windows.Forms.ToolStripMenuItem neutralToolStripMenuItem;

		// Token: 0x040005BC RID: 1468
		global::System.Windows.Forms.ToolStripMenuItem jaJPToolStripMenuItem;

		// Token: 0x040005BD RID: 1469
		global::System.Windows.Forms.ToolStripMenuItem oSDefaultToolStripMenuItem;

		// Token: 0x040005BE RID: 1470
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem3;

		// Token: 0x040005BF RID: 1471
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem4;

		// Token: 0x040005C0 RID: 1472
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem5;

		// Token: 0x040005C1 RID: 1473
		global::System.Windows.Forms.ToolStripMenuItem cSVFileToolStripMenuItem;

		// Token: 0x040005C2 RID: 1474
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem6;

		// Token: 0x040005C3 RID: 1475
		global::System.Windows.Forms.ToolStripMenuItem toolStripMenuItem7;

		// Token: 0x040005C4 RID: 1476
		global::System.Windows.Forms.ToolStripSeparator toolStripSeparator9;

		// Token: 0x040005C5 RID: 1477
		global::System.Windows.Forms.ToolStripMenuItem userLayoutToolStripMenuItem;

		// Token: 0x040005C6 RID: 1478
		global::System.Windows.Forms.ToolStripMenuItem expertLayoutToolStripMenuItem;
	}
}
