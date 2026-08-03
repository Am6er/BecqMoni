namespace BecquerelMonitor
{
    partial class EfficiencyMakerForm
    {
        System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources =
                new System.ComponentModel.ComponentResourceManager(typeof(EfficiencyMakerForm));
            this.tabControl = new System.Windows.Forms.TabControl();
            this.tabPageCalculate = new System.Windows.Forms.TabPage();
            this.geometryLabel = new System.Windows.Forms.Label();
            this.geometryTextBox = new System.Windows.Forms.TextBox();
            this.geometryBrowseButton = new System.Windows.Forms.Button();
            this.geometryClearButton = new System.Windows.Forms.Button();
            this.calcHintLabel = new System.Windows.Forms.Label();
            this.calculateButton = new System.Windows.Forms.Button();
            this.tabPageFit = new System.Windows.Forms.TabPage();
            this.referenceLabel = new System.Windows.Forms.Label();
            this.referenceTextBox = new System.Windows.Forms.TextBox();
            this.referenceBrowseButton = new System.Windows.Forms.Button();
            this.referenceClearButton = new System.Windows.Forms.Button();
            this.spectraLabel = new System.Windows.Forms.Label();
            this.spectraListBox = new System.Windows.Forms.ListBox();
            this.spectraAddButton = new System.Windows.Forms.Button();
            this.spectraRemoveButton = new System.Windows.Forms.Button();
            this.spectraClearButton = new System.Windows.Forms.Button();
            this.runButton = new System.Windows.Forms.Button();
            this.optionsGroupBox = new System.Windows.Forms.GroupBox();
            this.chainsLabel = new System.Windows.Forms.Label();
            this.chainsCheckedListBox = new System.Windows.Forms.CheckedListBox();
            this.orderLabel = new System.Windows.Forms.Label();
            this.orderNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.minIntensityLabel = new System.Windows.Forms.Label();
            this.minIntensityNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.minSignificanceLabel = new System.Windows.Forms.Label();
            this.minSignificanceNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.backgroundCheckBox = new System.Windows.Forms.CheckBox();
            this.anchorLabel = new System.Windows.Forms.Label();
            this.anchorEnergyTextBox = new System.Windows.Forms.TextBox();
            this.anchorEfficiencyTextBox = new System.Windows.Forms.TextBox();
            this.anchorHintLabel = new System.Windows.Forms.Label();
            this.outputLabel = new System.Windows.Forms.Label();
            this.outputTextBox = new System.Windows.Forms.TextBox();
            this.outputBrowseButton = new System.Windows.Forms.Button();
            this.saveButton = new System.Windows.Forms.Button();
            this.exportButton = new System.Windows.Forms.Button();
            this.statusLabel = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.logTextBox = new System.Windows.Forms.TextBox();
            this.graph = new BecquerelMonitor.EfficiencyMaker.EfficiencyCurveGraph();
            this.tabControl.SuspendLayout();
            this.tabPageCalculate.SuspendLayout();
            this.tabPageFit.SuspendLayout();
            this.optionsGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.orderNumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.minIntensityNumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.minSignificanceNumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            this.SuspendLayout();
            //
            // tabControl
            //
            // Два пути к кривой разнесены по вкладкам, а не сложены в одно окно:
            // общего у них только выход (файл ROI и график). Расчёту из
            // геометрии не нужны ни спектры, ни цепочки, ни опорная точка, а
            // подгонке не нужен файл геометрии — в общей форме половина полей
            // всегда стояла лишней и приходилось гадать, какая именно.
            this.tabControl.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right));
            this.tabControl.Controls.Add(this.tabPageCalculate);
            this.tabControl.Controls.Add(this.tabPageFit);
            this.tabControl.Location = new System.Drawing.Point(12, 12);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(942, 282);
            this.tabControl.TabIndex = 0;
            this.tabControl.SelectedIndexChanged += new System.EventHandler(this.tabControl_SelectedIndexChanged);
            //
            // tabPageCalculate
            //
            this.tabPageCalculate.Controls.Add(this.geometryLabel);
            this.tabPageCalculate.Controls.Add(this.geometryTextBox);
            this.tabPageCalculate.Controls.Add(this.geometryBrowseButton);
            this.tabPageCalculate.Controls.Add(this.geometryClearButton);
            this.tabPageCalculate.Controls.Add(this.calcHintLabel);
            this.tabPageCalculate.Controls.Add(this.calculateButton);
            this.tabPageCalculate.Location = new System.Drawing.Point(4, 22);
            this.tabPageCalculate.Name = "tabPageCalculate";
            this.tabPageCalculate.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageCalculate.Size = new System.Drawing.Size(934, 256);
            this.tabPageCalculate.TabIndex = 0;
            this.tabPageCalculate.Text = BecquerelMonitor.Properties.Resources.EfficiencyMakerTabCalculate;
            this.tabPageCalculate.UseVisualStyleBackColor = true;
            //
            // geometryLabel
            //
            this.geometryLabel.AutoSize = true;
            this.geometryLabel.Location = new System.Drawing.Point(10, 14);
            this.geometryLabel.Name = "geometryLabel";
            this.geometryLabel.Size = new System.Drawing.Size(300, 13);
            this.geometryLabel.TabIndex = 0;
            this.geometryLabel.Text = "Detector geometry (LSRM .in) - to calculate the curve instead:";
            //
            // geometryTextBox
            //
            this.geometryTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right));
            this.geometryTextBox.Location = new System.Drawing.Point(13, 33);
            this.geometryTextBox.Name = "geometryTextBox";
            this.geometryTextBox.ReadOnly = true;
            this.geometryTextBox.Size = new System.Drawing.Size(747, 20);
            this.geometryTextBox.TabIndex = 1;
            //
            // geometryBrowseButton
            //
            this.geometryBrowseButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.geometryBrowseButton.Location = new System.Drawing.Point(766, 31);
            this.geometryBrowseButton.Name = "geometryBrowseButton";
            this.geometryBrowseButton.Size = new System.Drawing.Size(80, 23);
            this.geometryBrowseButton.TabIndex = 2;
            this.geometryBrowseButton.Text = "Browse...";
            this.geometryBrowseButton.UseVisualStyleBackColor = true;
            this.geometryBrowseButton.Click += new System.EventHandler(this.geometryBrowseButton_Click);
            //
            // geometryClearButton
            //
            this.geometryClearButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.geometryClearButton.Location = new System.Drawing.Point(850, 31);
            this.geometryClearButton.Name = "geometryClearButton";
            this.geometryClearButton.Size = new System.Drawing.Size(70, 23);
            this.geometryClearButton.TabIndex = 3;
            this.geometryClearButton.Text = "Clear";
            this.geometryClearButton.UseVisualStyleBackColor = true;
            this.geometryClearButton.Click += new System.EventHandler(this.geometryClearButton_Click);
            //
            // calcHintLabel
            //
            this.calcHintLabel.ForeColor = System.Drawing.Color.DimGray;
            this.calcHintLabel.Location = new System.Drawing.Point(10, 68);
            this.calcHintLabel.MaximumSize = new System.Drawing.Size(600, 0);
            this.calcHintLabel.Name = "calcHintLabel";
            this.calcHintLabel.Size = new System.Drawing.Size(600, 32);
            this.calcHintLabel.TabIndex = 4;
            this.calcHintLabel.Text = BecquerelMonitor.Properties.Resources.EfficiencyMakerCalcHint;
            //
            // calculateButton
            //
            this.calculateButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.calculateButton.Enabled = false;
            this.calculateButton.Location = new System.Drawing.Point(13, 120);
            this.calculateButton.Name = "calculateButton";
            this.calculateButton.Size = new System.Drawing.Size(200, 26);
            this.calculateButton.TabIndex = 5;
            this.calculateButton.Text = "Calculate from geometry";
            this.calculateButton.UseVisualStyleBackColor = true;
            this.calculateButton.Click += new System.EventHandler(this.calculateButton_Click);
            //
            // tabPageFit
            //
            this.tabPageFit.Controls.Add(this.referenceLabel);
            this.tabPageFit.Controls.Add(this.referenceTextBox);
            this.tabPageFit.Controls.Add(this.referenceBrowseButton);
            this.tabPageFit.Controls.Add(this.referenceClearButton);
            this.tabPageFit.Controls.Add(this.spectraLabel);
            this.tabPageFit.Controls.Add(this.spectraListBox);
            this.tabPageFit.Controls.Add(this.spectraAddButton);
            this.tabPageFit.Controls.Add(this.spectraRemoveButton);
            this.tabPageFit.Controls.Add(this.spectraClearButton);
            this.tabPageFit.Controls.Add(this.runButton);
            this.tabPageFit.Controls.Add(this.optionsGroupBox);
            this.tabPageFit.Location = new System.Drawing.Point(4, 22);
            this.tabPageFit.Name = "tabPageFit";
            this.tabPageFit.Padding = new System.Windows.Forms.Padding(3);
            this.tabPageFit.Size = new System.Drawing.Size(934, 256);
            this.tabPageFit.TabIndex = 1;
            this.tabPageFit.Text = BecquerelMonitor.Properties.Resources.EfficiencyMakerTabFit;
            this.tabPageFit.UseVisualStyleBackColor = true;
            //
            // referenceLabel
            //
            this.referenceLabel.AutoSize = true;
            this.referenceLabel.Location = new System.Drawing.Point(10, 10);
            this.referenceLabel.Name = "referenceLabel";
            this.referenceLabel.Size = new System.Drawing.Size(200, 13);
            this.referenceLabel.TabIndex = 0;
            this.referenceLabel.Text = "Efficiency curve (ROI file, optional):";
            //
            // referenceTextBox
            //
            this.referenceTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right));
            this.referenceTextBox.Location = new System.Drawing.Point(13, 27);
            this.referenceTextBox.Name = "referenceTextBox";
            this.referenceTextBox.ReadOnly = true;
            this.referenceTextBox.Size = new System.Drawing.Size(392, 20);
            this.referenceTextBox.TabIndex = 1;
            //
            // referenceBrowseButton
            //
            this.referenceBrowseButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.referenceBrowseButton.Location = new System.Drawing.Point(411, 25);
            this.referenceBrowseButton.Name = "referenceBrowseButton";
            this.referenceBrowseButton.Size = new System.Drawing.Size(80, 23);
            this.referenceBrowseButton.TabIndex = 2;
            this.referenceBrowseButton.Text = "Browse...";
            this.referenceBrowseButton.UseVisualStyleBackColor = true;
            this.referenceBrowseButton.Click += new System.EventHandler(this.referenceBrowseButton_Click);
            //
            // referenceClearButton
            //
            this.referenceClearButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.referenceClearButton.Location = new System.Drawing.Point(495, 25);
            this.referenceClearButton.Name = "referenceClearButton";
            this.referenceClearButton.Size = new System.Drawing.Size(70, 23);
            this.referenceClearButton.TabIndex = 3;
            this.referenceClearButton.Text = "Clear";
            this.referenceClearButton.UseVisualStyleBackColor = true;
            this.referenceClearButton.Click += new System.EventHandler(this.referenceClearButton_Click);
            //
            // spectraLabel
            //
            this.spectraLabel.AutoSize = true;
            this.spectraLabel.Location = new System.Drawing.Point(10, 58);
            this.spectraLabel.Name = "spectraLabel";
            this.spectraLabel.Size = new System.Drawing.Size(200, 13);
            this.spectraLabel.TabIndex = 4;
            this.spectraLabel.Text = "Spectra measured in this geometry:";
            //
            // spectraListBox
            //
            this.spectraListBox.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right));
            this.spectraListBox.FormattingEnabled = true;
            this.spectraListBox.HorizontalScrollbar = true;
            this.spectraListBox.Location = new System.Drawing.Point(13, 75);
            this.spectraListBox.Name = "spectraListBox";
            this.spectraListBox.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            this.spectraListBox.Size = new System.Drawing.Size(392, 108);
            this.spectraListBox.TabIndex = 5;
            //
            // spectraAddButton
            //
            this.spectraAddButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.spectraAddButton.Location = new System.Drawing.Point(411, 75);
            this.spectraAddButton.Name = "spectraAddButton";
            this.spectraAddButton.Size = new System.Drawing.Size(154, 23);
            this.spectraAddButton.TabIndex = 6;
            this.spectraAddButton.Text = "Add spectra...";
            this.spectraAddButton.UseVisualStyleBackColor = true;
            this.spectraAddButton.Click += new System.EventHandler(this.spectraAddButton_Click);
            //
            // spectraRemoveButton
            //
            this.spectraRemoveButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.spectraRemoveButton.Location = new System.Drawing.Point(411, 104);
            this.spectraRemoveButton.Name = "spectraRemoveButton";
            this.spectraRemoveButton.Size = new System.Drawing.Size(154, 23);
            this.spectraRemoveButton.TabIndex = 7;
            this.spectraRemoveButton.Text = "Remove selected";
            this.spectraRemoveButton.UseVisualStyleBackColor = true;
            this.spectraRemoveButton.Click += new System.EventHandler(this.spectraRemoveButton_Click);
            //
            // spectraClearButton
            //
            this.spectraClearButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.spectraClearButton.Location = new System.Drawing.Point(411, 133);
            this.spectraClearButton.Name = "spectraClearButton";
            this.spectraClearButton.Size = new System.Drawing.Size(154, 23);
            this.spectraClearButton.TabIndex = 8;
            this.spectraClearButton.Text = "Clear list";
            this.spectraClearButton.UseVisualStyleBackColor = true;
            this.spectraClearButton.Click += new System.EventHandler(this.spectraClearButton_Click);
            //
            // runButton
            //
            this.runButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.runButton.Location = new System.Drawing.Point(13, 195);
            this.runButton.Name = "runButton";
            this.runButton.Size = new System.Drawing.Size(200, 26);
            this.runButton.TabIndex = 9;
            this.runButton.Text = "Build curve";
            this.runButton.UseVisualStyleBackColor = true;
            this.runButton.Click += new System.EventHandler(this.runButton_Click);
            //
            // optionsGroupBox
            //
            this.optionsGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.optionsGroupBox.Controls.Add(this.chainsLabel);
            this.optionsGroupBox.Controls.Add(this.chainsCheckedListBox);
            this.optionsGroupBox.Controls.Add(this.orderLabel);
            this.optionsGroupBox.Controls.Add(this.orderNumericUpDown);
            this.optionsGroupBox.Controls.Add(this.minIntensityLabel);
            this.optionsGroupBox.Controls.Add(this.minIntensityNumericUpDown);
            this.optionsGroupBox.Controls.Add(this.minSignificanceLabel);
            this.optionsGroupBox.Controls.Add(this.minSignificanceNumericUpDown);
            this.optionsGroupBox.Controls.Add(this.backgroundCheckBox);
            this.optionsGroupBox.Controls.Add(this.anchorLabel);
            this.optionsGroupBox.Controls.Add(this.anchorEnergyTextBox);
            this.optionsGroupBox.Controls.Add(this.anchorEfficiencyTextBox);
            this.optionsGroupBox.Controls.Add(this.anchorHintLabel);
            this.optionsGroupBox.Location = new System.Drawing.Point(590, 8);
            this.optionsGroupBox.Name = "optionsGroupBox";
            this.optionsGroupBox.Size = new System.Drawing.Size(330, 240);
            this.optionsGroupBox.TabIndex = 10;
            this.optionsGroupBox.TabStop = false;
            this.optionsGroupBox.Text = "Settings";
            //
            // chainsLabel
            //
            this.chainsLabel.AutoSize = true;
            this.chainsLabel.Location = new System.Drawing.Point(10, 22);
            this.chainsLabel.Name = "chainsLabel";
            this.chainsLabel.Size = new System.Drawing.Size(120, 13);
            this.chainsLabel.TabIndex = 0;
            this.chainsLabel.Text = "Chains in equilibrium:";
            //
            // chainsCheckedListBox
            //
            this.chainsCheckedListBox.CheckOnClick = true;
            this.chainsCheckedListBox.FormattingEnabled = true;
            this.chainsCheckedListBox.Location = new System.Drawing.Point(13, 39);
            this.chainsCheckedListBox.Name = "chainsCheckedListBox";
            this.chainsCheckedListBox.Size = new System.Drawing.Size(140, 94);
            this.chainsCheckedListBox.TabIndex = 1;
            //
            // orderLabel
            //
            this.orderLabel.AutoSize = true;
            this.orderLabel.Location = new System.Drawing.Point(166, 22);
            this.orderLabel.Name = "orderLabel";
            this.orderLabel.Size = new System.Drawing.Size(100, 13);
            this.orderLabel.TabIndex = 2;
            this.orderLabel.Text = "Polynomial order:";
            //
            // orderNumericUpDown
            //
            this.orderNumericUpDown.Location = new System.Drawing.Point(266, 20);
            this.orderNumericUpDown.Maximum = new decimal(new int[] { 6, 0, 0, 0 });
            this.orderNumericUpDown.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.orderNumericUpDown.Name = "orderNumericUpDown";
            this.orderNumericUpDown.Size = new System.Drawing.Size(50, 20);
            this.orderNumericUpDown.TabIndex = 3;
            this.orderNumericUpDown.Value = new decimal(new int[] { 3, 0, 0, 0 });
            //
            // minIntensityLabel
            //
            this.minIntensityLabel.AutoSize = true;
            this.minIntensityLabel.Location = new System.Drawing.Point(166, 48);
            this.minIntensityLabel.Name = "minIntensityLabel";
            this.minIntensityLabel.Size = new System.Drawing.Size(100, 13);
            this.minIntensityLabel.TabIndex = 4;
            this.minIntensityLabel.Text = "Min. yield, %:";
            //
            // minIntensityNumericUpDown
            //
            this.minIntensityNumericUpDown.DecimalPlaces = 1;
            this.minIntensityNumericUpDown.Location = new System.Drawing.Point(266, 46);
            this.minIntensityNumericUpDown.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.minIntensityNumericUpDown.Name = "minIntensityNumericUpDown";
            this.minIntensityNumericUpDown.Size = new System.Drawing.Size(50, 20);
            this.minIntensityNumericUpDown.TabIndex = 5;
            // 1.0 — это (10, scale 1); четвёртое слово decimal'а хранит знак и
            // масштаб, и «1» там не масштаб, а мусор: конструктор бросал
            // ArgumentException прямо в InitializeComponent.
            this.minIntensityNumericUpDown.Value = new decimal(new int[] { 10, 0, 0, 65536 });
            //
            // minSignificanceLabel
            //
            this.minSignificanceLabel.AutoSize = true;
            this.minSignificanceLabel.Location = new System.Drawing.Point(166, 74);
            this.minSignificanceLabel.Name = "minSignificanceLabel";
            this.minSignificanceLabel.Size = new System.Drawing.Size(100, 13);
            this.minSignificanceLabel.TabIndex = 6;
            this.minSignificanceLabel.Text = "Min. z of a line:";
            //
            // minSignificanceNumericUpDown
            //
            this.minSignificanceNumericUpDown.DecimalPlaces = 1;
            this.minSignificanceNumericUpDown.Location = new System.Drawing.Point(266, 72);
            this.minSignificanceNumericUpDown.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.minSignificanceNumericUpDown.Name = "minSignificanceNumericUpDown";
            this.minSignificanceNumericUpDown.Size = new System.Drawing.Size(50, 20);
            this.minSignificanceNumericUpDown.TabIndex = 7;
            this.minSignificanceNumericUpDown.Value = new decimal(new int[] { 40, 0, 0, 65536 });
            //
            // backgroundCheckBox
            //
            this.backgroundCheckBox.AutoSize = true;
            this.backgroundCheckBox.Checked = true;
            this.backgroundCheckBox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.backgroundCheckBox.Location = new System.Drawing.Point(169, 99);
            this.backgroundCheckBox.Name = "backgroundCheckBox";
            this.backgroundCheckBox.Size = new System.Drawing.Size(140, 17);
            this.backgroundCheckBox.TabIndex = 8;
            this.backgroundCheckBox.Text = "Subtract background";
            this.backgroundCheckBox.UseVisualStyleBackColor = true;
            //
            // anchorLabel
            //
            this.anchorLabel.AutoSize = true;
            this.anchorLabel.Location = new System.Drawing.Point(10, 140);
            this.anchorLabel.Name = "anchorLabel";
            this.anchorLabel.Size = new System.Drawing.Size(150, 13);
            this.anchorLabel.TabIndex = 9;
            this.anchorLabel.Text = "Level anchor, keV / eps:";
            //
            // anchorEnergyTextBox
            //
            this.anchorEnergyTextBox.Location = new System.Drawing.Point(166, 137);
            this.anchorEnergyTextBox.Name = "anchorEnergyTextBox";
            this.anchorEnergyTextBox.Size = new System.Drawing.Size(70, 20);
            this.anchorEnergyTextBox.TabIndex = 10;
            //
            // anchorEfficiencyTextBox
            //
            this.anchorEfficiencyTextBox.Location = new System.Drawing.Point(242, 137);
            this.anchorEfficiencyTextBox.Name = "anchorEfficiencyTextBox";
            this.anchorEfficiencyTextBox.Size = new System.Drawing.Size(74, 20);
            this.anchorEfficiencyTextBox.TabIndex = 11;
            //
            // anchorHintLabel
            //
            this.anchorHintLabel.AutoSize = true;
            this.anchorHintLabel.ForeColor = System.Drawing.Color.DimGray;
            this.anchorHintLabel.Location = new System.Drawing.Point(10, 160);
            this.anchorHintLabel.MaximumSize = new System.Drawing.Size(310, 0);
            this.anchorHintLabel.Name = "anchorHintLabel";
            this.anchorHintLabel.Size = new System.Drawing.Size(310, 26);
            this.anchorHintLabel.TabIndex = 12;
            this.anchorHintLabel.Text = "Measurements give the shape only; the absolute level comes"
                + " from the source curve or from this anchor.";
            //
            // outputLabel
            //
            this.outputLabel.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.outputLabel.AutoSize = true;
            this.outputLabel.Location = new System.Drawing.Point(12, 305);
            this.outputLabel.Name = "outputLabel";
            this.outputLabel.Size = new System.Drawing.Size(80, 13);
            this.outputLabel.TabIndex = 1;
            this.outputLabel.Text = "Output file:";
            //
            // outputTextBox
            //
            this.outputTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right));
            this.outputTextBox.Location = new System.Drawing.Point(98, 302);
            this.outputTextBox.Name = "outputTextBox";
            this.outputTextBox.Size = new System.Drawing.Size(690, 20);
            this.outputTextBox.TabIndex = 2;
            //
            // outputBrowseButton
            //
            this.outputBrowseButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.outputBrowseButton.Location = new System.Drawing.Point(794, 300);
            this.outputBrowseButton.Name = "outputBrowseButton";
            this.outputBrowseButton.Size = new System.Drawing.Size(80, 23);
            this.outputBrowseButton.TabIndex = 3;
            this.outputBrowseButton.Text = "Browse...";
            this.outputBrowseButton.UseVisualStyleBackColor = true;
            this.outputBrowseButton.Click += new System.EventHandler(this.outputBrowseButton_Click);
            //
            // saveButton
            //
            this.saveButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.saveButton.Enabled = false;
            this.saveButton.Location = new System.Drawing.Point(12, 332);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(120, 26);
            this.saveButton.TabIndex = 4;
            this.saveButton.Text = "Save curve";
            this.saveButton.UseVisualStyleBackColor = true;
            this.saveButton.Click += new System.EventHandler(this.saveButton_Click);
            //
            // exportButton
            //
            this.exportButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.exportButton.Enabled = false;
            this.exportButton.Location = new System.Drawing.Point(138, 332);
            this.exportButton.Name = "exportButton";
            this.exportButton.Size = new System.Drawing.Size(120, 26);
            this.exportButton.TabIndex = 5;
            this.exportButton.Text = "Export CSV...";
            this.exportButton.UseVisualStyleBackColor = true;
            this.exportButton.Click += new System.EventHandler(this.exportButton_Click);
            //
            // statusLabel
            //
            this.statusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right));
            this.statusLabel.AutoEllipsis = true;
            this.statusLabel.Location = new System.Drawing.Point(266, 338);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(520, 16);
            this.statusLabel.TabIndex = 6;
            //
            // progressBar
            //
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.progressBar.Location = new System.Drawing.Point(794, 335);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(160, 20);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 7;
            this.progressBar.Visible = false;
            //
            // splitContainer
            //
            this.splitContainer.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom
                | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right));
            this.splitContainer.Location = new System.Drawing.Point(12, 366);
            this.splitContainer.Name = "splitContainer";
            this.splitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.splitContainer.Panel1.Controls.Add(this.graph);
            this.splitContainer.Panel2.Controls.Add(this.logTextBox);
            this.splitContainer.Size = new System.Drawing.Size(942, 344);
            this.splitContainer.SplitterDistance = 220;
            this.splitContainer.TabIndex = 8;
            //
            // graph
            //
            this.graph.BackColor = System.Drawing.Color.White;
            this.graph.Dock = System.Windows.Forms.DockStyle.Fill;
            this.graph.Location = new System.Drawing.Point(0, 0);
            this.graph.Name = "graph";
            this.graph.Size = new System.Drawing.Size(942, 220);
            this.graph.TabIndex = 0;
            //
            // logTextBox
            //
            this.logTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.logTextBox.Font = new System.Drawing.Font("Consolas", 8.25F);
            this.logTextBox.Multiline = true;
            this.logTextBox.Name = "logTextBox";
            this.logTextBox.ReadOnly = true;
            this.logTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.logTextBox.WordWrap = false;
            this.logTextBox.TabIndex = 0;
            //
            // EfficiencyMakerForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(966, 722);
            this.Controls.Add(this.tabControl);
            this.Controls.Add(this.outputLabel);
            this.Controls.Add(this.outputTextBox);
            this.Controls.Add(this.outputBrowseButton);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.exportButton);
            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.splitContainer);
            this.MinimumSize = new System.Drawing.Size(900, 700);
            this.Name = "EfficiencyMakerForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Efficiency maker";
            // Подписи заданы выше по-английски и здесь перекрываются ресурсами:
            // ApplyResources молча пропускает отсутствующий ключ, поэтому
            // нейтральная сборка остаётся английской, а ru.resx — русской.
            // Заголовки вкладок и подсказка расчёта в этот список не входят:
            // они взяты прямо из общих Resources выше, где у них уже есть
            // русская пара.
            resources.ApplyResources(this, "$this");
            foreach (System.Windows.Forms.Control control in new System.Windows.Forms.Control[] {
                this.referenceLabel, this.referenceBrowseButton,
                this.referenceClearButton, this.spectraLabel, this.spectraAddButton,
                this.spectraRemoveButton, this.spectraClearButton, this.optionsGroupBox,
                this.chainsLabel, this.orderLabel, this.minIntensityLabel,
                this.minSignificanceLabel, this.backgroundCheckBox, this.anchorLabel,
                this.anchorHintLabel, this.outputLabel, this.outputBrowseButton,
                this.runButton, this.saveButton, this.exportButton,
                this.geometryLabel, this.geometryBrowseButton, this.geometryClearButton,
                this.calculateButton })
            {
                resources.ApplyResources(control, control.Name);
            }
            this.tabPageCalculate.ResumeLayout(false);
            this.tabPageCalculate.PerformLayout();
            this.tabPageFit.ResumeLayout(false);
            this.tabPageFit.PerformLayout();
            this.tabControl.ResumeLayout(false);
            this.optionsGroupBox.ResumeLayout(false);
            this.optionsGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.orderNumericUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.minIntensityNumericUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.minSignificanceNumericUpDown)).EndInit();
            this.splitContainer.Panel1.ResumeLayout(false);
            this.splitContainer.Panel2.ResumeLayout(false);
            this.splitContainer.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.splitContainer.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        System.Windows.Forms.TabControl tabControl;
        System.Windows.Forms.TabPage tabPageCalculate;
        System.Windows.Forms.TabPage tabPageFit;
        System.Windows.Forms.Label referenceLabel;
        System.Windows.Forms.TextBox referenceTextBox;
        System.Windows.Forms.Button referenceBrowseButton;
        System.Windows.Forms.Button referenceClearButton;
        System.Windows.Forms.Label spectraLabel;
        System.Windows.Forms.ListBox spectraListBox;
        System.Windows.Forms.Button spectraAddButton;
        System.Windows.Forms.Button spectraRemoveButton;
        System.Windows.Forms.Button spectraClearButton;
        System.Windows.Forms.Label geometryLabel;
        System.Windows.Forms.TextBox geometryTextBox;
        System.Windows.Forms.Button geometryBrowseButton;
        System.Windows.Forms.Button geometryClearButton;
        System.Windows.Forms.Label calcHintLabel;
        System.Windows.Forms.Button calculateButton;
        System.Windows.Forms.GroupBox optionsGroupBox;
        System.Windows.Forms.Label chainsLabel;
        System.Windows.Forms.CheckedListBox chainsCheckedListBox;
        System.Windows.Forms.Label orderLabel;
        System.Windows.Forms.NumericUpDown orderNumericUpDown;
        System.Windows.Forms.Label minIntensityLabel;
        System.Windows.Forms.NumericUpDown minIntensityNumericUpDown;
        System.Windows.Forms.Label minSignificanceLabel;
        System.Windows.Forms.NumericUpDown minSignificanceNumericUpDown;
        System.Windows.Forms.CheckBox backgroundCheckBox;
        System.Windows.Forms.Label anchorLabel;
        System.Windows.Forms.TextBox anchorEnergyTextBox;
        System.Windows.Forms.TextBox anchorEfficiencyTextBox;
        System.Windows.Forms.Label anchorHintLabel;
        System.Windows.Forms.Label outputLabel;
        System.Windows.Forms.TextBox outputTextBox;
        System.Windows.Forms.Button outputBrowseButton;
        System.Windows.Forms.Button runButton;
        System.Windows.Forms.Button saveButton;
        System.Windows.Forms.Button exportButton;
        System.Windows.Forms.Label statusLabel;
        System.Windows.Forms.ProgressBar progressBar;
        System.Windows.Forms.SplitContainer splitContainer;
        System.Windows.Forms.TextBox logTextBox;
        BecquerelMonitor.EfficiencyMaker.EfficiencyCurveGraph graph;
    }
}
