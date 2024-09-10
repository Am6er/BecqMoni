using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
	// Token: 0x02000030 RID: 48
	public partial class DCControlPanel : BecquerelMonitor.ToolWindow
	{
		// Token: 0x060002A9 RID: 681 RVA: 0x0000C470 File Offset: 0x0000A670
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
		private void InitializeComponent()
		{
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DCControlPanel));
            this.imageList1 = new System.Windows.Forms.ImageList(this.components);
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.attachBtn = new System.Windows.Forms.Button();
            this.clearRoiBtn = new System.Windows.Forms.Button();
            this.clearBgBtn = new System.Windows.Forms.Button();
            this.spectrumSaveBtn = new System.Windows.Forms.Button();
            this.reloadFromConfigBtn = new System.Windows.Forms.Button();
            this.startBtn = new System.Windows.Forms.Button();
            this.stopBtn = new System.Windows.Forms.Button();
            this.clearBtn = new System.Windows.Forms.Button();
            this.roiConfigComboBox = new System.Windows.Forms.ComboBox();
            this.devConfigComboBox = new System.Windows.Forms.ComboBox();
            this.realTimeLimitTextBox = new BecquerelMonitor.IntegerTextBox();
            this.percentageProgressBar1 = new BecquerelMonitor.PercentageProgressBar();
            this.liveTimeLbl = new System.Windows.Forms.Label();
            this.deadTimeLbl = new System.Windows.Forms.Label();
            this.saveWhenFinishedCheckBox = new System.Windows.Forms.CheckBox();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.liveTimetextBox = new System.Windows.Forms.TextBox();
            this.deadTimetextBox = new System.Windows.Forms.TextBox();
            this.realTimeLbl = new System.Windows.Forms.Label();
            this.invalidCntLbl = new System.Windows.Forms.Label();
            this.totalCntTextBox = new System.Windows.Forms.TextBox();
            this.invalidCountsTextBox = new System.Windows.Forms.TextBox();
            this.validCntTextBox = new System.Windows.Forms.TextBox();
            this.validCntLbl = new System.Windows.Forms.Label();
            this.totalCntLbl = new System.Windows.Forms.Label();
            this.countRateLbl = new System.Windows.Forms.Label();
            this.countRateTextBox = new System.Windows.Forms.TextBox();
            this.roiConfigEditBtn = new System.Windows.Forms.Button();
            this.roiConfigLbl = new System.Windows.Forms.Label();
            this.selectBgBtn = new System.Windows.Forms.Button();
            this.bachgroundLbl = new System.Windows.Forms.Label();
            this.textBox1 = new System.Windows.Forms.TextBox();
            this.devConfigEditBtn = new System.Windows.Forms.Button();
            this.spectrumNameLbl = new System.Windows.Forms.TextBox();
            this.saveToLbl = new System.Windows.Forms.Label();
            this.deviceConfigLbl = new System.Windows.Forms.Label();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            // 
            // imageList1
            // 
            this.imageList1.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("imageList1.ImageStream")));
            this.imageList1.TransparentColor = System.Drawing.Color.Transparent;
            this.imageList1.Images.SetKeyName(0, "start.ico");
            this.imageList1.Images.SetKeyName(1, "stop.ico");
            this.imageList1.Images.SetKeyName(2, "clear.ico");
            this.imageList1.Images.SetKeyName(3, "reload.ico");
            // 
            // toolTip1
            // 
            this.toolTip1.AutoPopDelay = 10000;
            this.toolTip1.InitialDelay = 500;
            this.toolTip1.ReshowDelay = 100;
            // 
            // attachBtn
            // 
            this.attachBtn.Image = global::BecquerelMonitor.Properties.Resources.attach;
            resources.ApplyResources(this.attachBtn, "attachBtn");
            this.attachBtn.Name = "attachBtn";
            this.toolTip1.SetToolTip(this.attachBtn, resources.GetString("attachBtn.ToolTip"));
            this.attachBtn.UseVisualStyleBackColor = true;
            this.attachBtn.Click += new System.EventHandler(this.button11_Click);
            // 
            // clearRoiBtn
            // 
            resources.ApplyResources(this.clearRoiBtn, "clearRoiBtn");
            this.clearRoiBtn.ImageList = this.imageList1;
            this.clearRoiBtn.Name = "clearRoiBtn";
            this.toolTip1.SetToolTip(this.clearRoiBtn, resources.GetString("clearRoiBtn.ToolTip"));
            this.clearRoiBtn.UseVisualStyleBackColor = true;
            this.clearRoiBtn.Click += new System.EventHandler(this.button9_Click);
            // 
            // clearBgBtn
            // 
            resources.ApplyResources(this.clearBgBtn, "clearBgBtn");
            this.clearBgBtn.ImageList = this.imageList1;
            this.clearBgBtn.Name = "clearBgBtn";
            this.toolTip1.SetToolTip(this.clearBgBtn, resources.GetString("clearBgBtn.ToolTip"));
            this.clearBgBtn.UseVisualStyleBackColor = true;
            this.clearBgBtn.Click += new System.EventHandler(this.button8_Click);
            // 
            // spectrumSaveBtn
            // 
            resources.ApplyResources(this.spectrumSaveBtn, "spectrumSaveBtn");
            this.spectrumSaveBtn.Name = "spectrumSaveBtn";
            this.toolTip1.SetToolTip(this.spectrumSaveBtn, resources.GetString("spectrumSaveBtn.ToolTip"));
            this.spectrumSaveBtn.UseVisualStyleBackColor = true;
            this.spectrumSaveBtn.Click += new System.EventHandler(this.button6_Click);
            // 
            // reloadFromConfigBtn
            // 
            resources.ApplyResources(this.reloadFromConfigBtn, "reloadFromConfigBtn");
            this.reloadFromConfigBtn.ImageList = this.imageList1;
            this.reloadFromConfigBtn.Name = "reloadFromConfigBtn";
            this.toolTip1.SetToolTip(this.reloadFromConfigBtn, resources.GetString("reloadFromConfigBtn.ToolTip"));
            this.reloadFromConfigBtn.UseVisualStyleBackColor = true;
            this.reloadFromConfigBtn.Click += new System.EventHandler(this.button5_Click);
            // 
            // startBtn
            // 
            resources.ApplyResources(this.startBtn, "startBtn");
            this.startBtn.ImageList = this.imageList1;
            this.startBtn.Name = "startBtn";
            this.toolTip1.SetToolTip(this.startBtn, resources.GetString("startBtn.ToolTip"));
            this.startBtn.UseVisualStyleBackColor = true;
            this.startBtn.Click += new System.EventHandler(this.button1_Click);
            // 
            // stopBtn
            // 
            resources.ApplyResources(this.stopBtn, "stopBtn");
            this.stopBtn.ImageList = this.imageList1;
            this.stopBtn.Name = "stopBtn";
            this.toolTip1.SetToolTip(this.stopBtn, resources.GetString("stopBtn.ToolTip"));
            this.stopBtn.UseVisualStyleBackColor = true;
            this.stopBtn.Click += new System.EventHandler(this.button2_Click);
            // 
            // clearBtn
            // 
            resources.ApplyResources(this.clearBtn, "clearBtn");
            this.clearBtn.ImageList = this.imageList1;
            this.clearBtn.Name = "clearBtn";
            this.toolTip1.SetToolTip(this.clearBtn, resources.GetString("clearBtn.ToolTip"));
            this.clearBtn.UseVisualStyleBackColor = true;
            this.clearBtn.Click += new System.EventHandler(this.button7_Click);
            // 
            // roiConfigComboBox
            // 
            resources.ApplyResources(this.roiConfigComboBox, "roiConfigComboBox");
            this.roiConfigComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.roiConfigComboBox.FormattingEnabled = true;
            this.roiConfigComboBox.Name = "roiConfigComboBox";
            this.toolTip1.SetToolTip(this.roiConfigComboBox, resources.GetString("roiConfigComboBox.ToolTip"));
            this.roiConfigComboBox.SelectedIndexChanged += new System.EventHandler(this.comboBox2_SelectedIndexChanged);
            // 
            // devConfigComboBox
            // 
            resources.ApplyResources(this.devConfigComboBox, "devConfigComboBox");
            this.devConfigComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.devConfigComboBox.FormattingEnabled = true;
            this.devConfigComboBox.Name = "devConfigComboBox";
            this.toolTip1.SetToolTip(this.devConfigComboBox, resources.GetString("devConfigComboBox.ToolTip"));
            this.devConfigComboBox.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            // 
            // realTimeLimitTextBox
            // 
            resources.ApplyResources(this.realTimeLimitTextBox, "realTimeLimitTextBox");
            this.realTimeLimitTextBox.Name = "realTimeLimitTextBox";
            this.toolTip1.SetToolTip(this.realTimeLimitTextBox, resources.GetString("realTimeLimitTextBox.ToolTip"));
            this.realTimeLimitTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.textBox7_KeyDown);
            this.realTimeLimitTextBox.Validated += new System.EventHandler(this.textBox7_Validated);
            // 
            // percentageProgressBar1
            // 
            resources.ApplyResources(this.percentageProgressBar1, "percentageProgressBar1");
            this.percentageProgressBar1.DoubleValue = 0D;
            this.percentageProgressBar1.ForeColor = System.Drawing.Color.Black;
            this.percentageProgressBar1.Name = "percentageProgressBar1";
            this.percentageProgressBar1.OverlayColor = System.Drawing.Color.Black;
            this.percentageProgressBar1.PriorText = "28800";
            this.toolTip1.SetToolTip(this.percentageProgressBar1, resources.GetString("percentageProgressBar1.ToolTip"));
            this.percentageProgressBar1.Click += new System.EventHandler(this.percentageProgressBar1_Click);
            // 
            // liveTimeLbl
            // 
            resources.ApplyResources(this.liveTimeLbl, "liveTimeLbl");
            this.liveTimeLbl.Name = "liveTimeLbl";
            this.toolTip1.SetToolTip(this.liveTimeLbl, resources.GetString("liveTimeLbl.ToolTip"));
            // 
            // deadTimeLbl
            // 
            resources.ApplyResources(this.deadTimeLbl, "deadTimeLbl");
            this.deadTimeLbl.Name = "deadTimeLbl";
            this.toolTip1.SetToolTip(this.deadTimeLbl, resources.GetString("deadTimeLbl.ToolTip"));
            // 
            // saveWhenFinishedCheckBox
            // 
            resources.ApplyResources(this.saveWhenFinishedCheckBox, "saveWhenFinishedCheckBox");
            this.saveWhenFinishedCheckBox.Name = "saveWhenFinishedCheckBox";
            this.saveWhenFinishedCheckBox.UseVisualStyleBackColor = true;
            this.saveWhenFinishedCheckBox.CheckedChanged += new System.EventHandler(this.checkBox1_CheckedChanged);
            // 
            // groupBox1
            // 
            resources.ApplyResources(this.groupBox1, "groupBox1");
            this.groupBox1.Controls.Add(this.liveTimetextBox);
            this.groupBox1.Controls.Add(this.liveTimeLbl);
            this.groupBox1.Controls.Add(this.deadTimetextBox);
            this.groupBox1.Controls.Add(this.deadTimeLbl);
            this.groupBox1.Controls.Add(this.realTimeLimitTextBox);
            this.groupBox1.Controls.Add(this.percentageProgressBar1);
            this.groupBox1.Controls.Add(this.startBtn);
            this.groupBox1.Controls.Add(this.attachBtn);
            this.groupBox1.Controls.Add(this.stopBtn);
            this.groupBox1.Controls.Add(this.realTimeLbl);
            this.groupBox1.Controls.Add(this.invalidCntLbl);
            this.groupBox1.Controls.Add(this.clearBtn);
            this.groupBox1.Controls.Add(this.totalCntTextBox);
            this.groupBox1.Controls.Add(this.invalidCountsTextBox);
            this.groupBox1.Controls.Add(this.validCntTextBox);
            this.groupBox1.Controls.Add(this.validCntLbl);
            this.groupBox1.Controls.Add(this.totalCntLbl);
            this.groupBox1.Controls.Add(this.countRateLbl);
            this.groupBox1.Controls.Add(this.countRateTextBox);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.TabStop = false;
            // 
            // liveTimetextBox
            // 
            resources.ApplyResources(this.liveTimetextBox, "liveTimetextBox");
            this.liveTimetextBox.Name = "liveTimetextBox";
            this.liveTimetextBox.ReadOnly = true;
            this.liveTimetextBox.TabStop = false;
            // 
            // deadTimetextBox
            // 
            resources.ApplyResources(this.deadTimetextBox, "deadTimetextBox");
            this.deadTimetextBox.Name = "deadTimetextBox";
            this.deadTimetextBox.ReadOnly = true;
            this.deadTimetextBox.TabStop = false;
            // 
            // realTimeLbl
            // 
            resources.ApplyResources(this.realTimeLbl, "realTimeLbl");
            this.realTimeLbl.Name = "realTimeLbl";
            // 
            // invalidCntLbl
            // 
            resources.ApplyResources(this.invalidCntLbl, "invalidCntLbl");
            this.invalidCntLbl.Name = "invalidCntLbl";
            // 
            // totalCntTextBox
            // 
            resources.ApplyResources(this.totalCntTextBox, "totalCntTextBox");
            this.totalCntTextBox.Name = "totalCntTextBox";
            this.totalCntTextBox.ReadOnly = true;
            this.totalCntTextBox.TabStop = false;
            // 
            // invalidCountsTextBox
            // 
            resources.ApplyResources(this.invalidCountsTextBox, "invalidCountsTextBox");
            this.invalidCountsTextBox.Name = "invalidCountsTextBox";
            this.invalidCountsTextBox.ReadOnly = true;
            this.invalidCountsTextBox.TabStop = false;
            // 
            // validCntTextBox
            // 
            resources.ApplyResources(this.validCntTextBox, "validCntTextBox");
            this.validCntTextBox.Name = "validCntTextBox";
            this.validCntTextBox.ReadOnly = true;
            this.validCntTextBox.TabStop = false;
            // 
            // validCntLbl
            // 
            resources.ApplyResources(this.validCntLbl, "validCntLbl");
            this.validCntLbl.Name = "validCntLbl";
            // 
            // totalCntLbl
            // 
            resources.ApplyResources(this.totalCntLbl, "totalCntLbl");
            this.totalCntLbl.Name = "totalCntLbl";
            // 
            // countRateLbl
            // 
            resources.ApplyResources(this.countRateLbl, "countRateLbl");
            this.countRateLbl.Name = "countRateLbl";
            // 
            // countRateTextBox
            // 
            resources.ApplyResources(this.countRateTextBox, "countRateTextBox");
            this.countRateTextBox.Name = "countRateTextBox";
            this.countRateTextBox.ReadOnly = true;
            this.countRateTextBox.TabStop = false;
            // 
            // roiConfigEditBtn
            // 
            resources.ApplyResources(this.roiConfigEditBtn, "roiConfigEditBtn");
            this.roiConfigEditBtn.Name = "roiConfigEditBtn";
            this.roiConfigEditBtn.UseVisualStyleBackColor = true;
            this.roiConfigEditBtn.Click += new System.EventHandler(this.button4_Click);
            // 
            // roiConfigLbl
            // 
            resources.ApplyResources(this.roiConfigLbl, "roiConfigLbl");
            this.roiConfigLbl.Name = "roiConfigLbl";
            // 
            // selectBgBtn
            // 
            resources.ApplyResources(this.selectBgBtn, "selectBgBtn");
            this.selectBgBtn.Name = "selectBgBtn";
            this.selectBgBtn.UseVisualStyleBackColor = true;
            this.selectBgBtn.Click += new System.EventHandler(this.button3_Click);
            // 
            // bachgroundLbl
            // 
            resources.ApplyResources(this.bachgroundLbl, "bachgroundLbl");
            this.bachgroundLbl.Name = "bachgroundLbl";
            // 
            // textBox1
            // 
            resources.ApplyResources(this.textBox1, "textBox1");
            this.textBox1.Name = "textBox1";
            this.textBox1.ReadOnly = true;
            this.textBox1.TabStop = false;
            // 
            // devConfigEditBtn
            // 
            resources.ApplyResources(this.devConfigEditBtn, "devConfigEditBtn");
            this.devConfigEditBtn.Name = "devConfigEditBtn";
            this.devConfigEditBtn.UseVisualStyleBackColor = true;
            this.devConfigEditBtn.Click += new System.EventHandler(this.button10_Click);
            // 
            // spectrumNameLbl
            // 
            resources.ApplyResources(this.spectrumNameLbl, "spectrumNameLbl");
            this.spectrumNameLbl.Name = "spectrumNameLbl";
            this.spectrumNameLbl.ReadOnly = true;
            this.spectrumNameLbl.TabStop = false;
            // 
            // saveToLbl
            // 
            resources.ApplyResources(this.saveToLbl, "saveToLbl");
            this.saveToLbl.Name = "saveToLbl";
            // 
            // deviceConfigLbl
            // 
            resources.ApplyResources(this.deviceConfigLbl, "deviceConfigLbl");
            this.deviceConfigLbl.Name = "deviceConfigLbl";
            // 
            // DCControlPanel
            // 
            resources.ApplyResources(this, "$this");
            this.Controls.Add(this.clearRoiBtn);
            this.Controls.Add(this.clearBgBtn);
            this.Controls.Add(this.spectrumSaveBtn);
            this.Controls.Add(this.saveWhenFinishedCheckBox);
            this.Controls.Add(this.reloadFromConfigBtn);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.roiConfigEditBtn);
            this.Controls.Add(this.roiConfigComboBox);
            this.Controls.Add(this.roiConfigLbl);
            this.Controls.Add(this.selectBgBtn);
            this.Controls.Add(this.bachgroundLbl);
            this.Controls.Add(this.textBox1);
            this.Controls.Add(this.devConfigComboBox);
            this.Controls.Add(this.devConfigEditBtn);
            this.Controls.Add(this.spectrumNameLbl);
            this.Controls.Add(this.saveToLbl);
            this.Controls.Add(this.deviceConfigLbl);
            this.HideOnClose = true;
            this.Name = "DCControlPanel";
            this.Load += new System.EventHandler(this.DCControlPanel_Load);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

        #endregion

        System.ComponentModel.IContainer components;
		System.Windows.Forms.Button clearBtn;
		System.Windows.Forms.Label realTimeLbl;
		System.Windows.Forms.Label invalidCntLbl;
		System.Windows.Forms.TextBox invalidCountsTextBox;
		System.Windows.Forms.TextBox validCntTextBox;
		System.Windows.Forms.Label validCntLbl;
		System.Windows.Forms.Label totalCntLbl;
		System.Windows.Forms.TextBox countRateTextBox;
		System.Windows.Forms.Label deviceConfigLbl;
		System.Windows.Forms.Label countRateLbl;
		System.Windows.Forms.TextBox totalCntTextBox;
		System.Windows.Forms.Button stopBtn;
		System.Windows.Forms.Button startBtn;
		System.Windows.Forms.Label saveToLbl;
		System.Windows.Forms.TextBox spectrumNameLbl;
		System.Windows.Forms.Button devConfigEditBtn;
		System.Windows.Forms.ComboBox devConfigComboBox;
		System.Windows.Forms.TextBox textBox1;
		System.Windows.Forms.Label bachgroundLbl;
		System.Windows.Forms.Button selectBgBtn;
		System.Windows.Forms.Label roiConfigLbl;
		System.Windows.Forms.ComboBox roiConfigComboBox;
		System.Windows.Forms.Button roiConfigEditBtn;
		System.Windows.Forms.GroupBox groupBox1;
		System.Windows.Forms.ImageList imageList1;
		System.Windows.Forms.Button reloadFromConfigBtn;
		System.Windows.Forms.ToolTip toolTip1;
		BecquerelMonitor.PercentageProgressBar percentageProgressBar1;
		BecquerelMonitor.IntegerTextBox realTimeLimitTextBox;
		System.Windows.Forms.CheckBox saveWhenFinishedCheckBox;
		System.Windows.Forms.Button spectrumSaveBtn;
		System.Windows.Forms.Button clearBgBtn;
		System.Windows.Forms.Button clearRoiBtn;
		System.Windows.Forms.Button attachBtn;
        private System.Windows.Forms.TextBox deadTimetextBox;
        private System.Windows.Forms.Label deadTimeLbl;
        private System.Windows.Forms.TextBox liveTimetextBox;
        private System.Windows.Forms.Label liveTimeLbl;
    }
}
