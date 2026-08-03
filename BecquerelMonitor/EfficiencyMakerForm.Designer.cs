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
            this.inputGroupBox = new System.Windows.Forms.GroupBox();
            this.referenceLabel = new System.Windows.Forms.Label();
            this.referenceTextBox = new System.Windows.Forms.TextBox();
            this.referenceBrowseButton = new System.Windows.Forms.Button();
            this.referenceClearButton = new System.Windows.Forms.Button();
            this.spectraLabel = new System.Windows.Forms.Label();
            this.spectraListBox = new System.Windows.Forms.ListBox();
            this.spectraAddButton = new System.Windows.Forms.Button();
            this.spectraRemoveButton = new System.Windows.Forms.Button();
            this.spectraClearButton = new System.Windows.Forms.Button();
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
            this.runButton = new System.Windows.Forms.Button();
            this.saveButton = new System.Windows.Forms.Button();
            this.exportButton = new System.Windows.Forms.Button();
            this.statusLabel = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.logTextBox = new System.Windows.Forms.TextBox();
            this.graph = new BecquerelMonitor.EfficiencyMaker.EfficiencyCurveGraph();
            this.inputGroupBox.SuspendLayout();
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
            // inputGroupBox
            //
            this.inputGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right));
            this.inputGroupBox.Controls.Add(this.referenceLabel);
            this.inputGroupBox.Controls.Add(this.referenceTextBox);
            this.inputGroupBox.Controls.Add(this.referenceBrowseButton);
            this.inputGroupBox.Controls.Add(this.referenceClearButton);
            this.inputGroupBox.Controls.Add(this.spectraLabel);
            this.inputGroupBox.Controls.Add(this.spectraListBox);
            this.inputGroupBox.Controls.Add(this.spectraAddButton);
            this.inputGroupBox.Controls.Add(this.spectraRemoveButton);
            this.inputGroupBox.Controls.Add(this.spectraClearButton);
            this.inputGroupBox.Location = new System.Drawing.Point(12, 12);
            this.inputGroupBox.Name = "inputGroupBox";
            this.inputGroupBox.Size = new System.Drawing.Size(600, 214);
            this.inputGroupBox.TabIndex = 0;
            this.inputGroupBox.TabStop = false;
            this.inputGroupBox.Text = "Input";
            //
            // referenceLabel
            //
            this.referenceLabel.AutoSize = true;
            this.referenceLabel.Location = new System.Drawing.Point(10, 24);
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
            this.referenceTextBox.Location = new System.Drawing.Point(13, 41);
            this.referenceTextBox.Name = "referenceTextBox";
            this.referenceTextBox.ReadOnly = true;
            this.referenceTextBox.Size = new System.Drawing.Size(415, 20);
            this.referenceTextBox.TabIndex = 1;
            //
            // referenceBrowseButton
            //
            this.referenceBrowseButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.referenceBrowseButton.Location = new System.Drawing.Point(434, 39);
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
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.referenceClearButton.Location = new System.Drawing.Point(518, 39);
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
            this.spectraLabel.Location = new System.Drawing.Point(10, 72);
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
            this.spectraListBox.Location = new System.Drawing.Point(13, 89);
            this.spectraListBox.Name = "spectraListBox";
            this.spectraListBox.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
            this.spectraListBox.Size = new System.Drawing.Size(415, 95);
            this.spectraListBox.TabIndex = 5;
            //
            // spectraAddButton
            //
            this.spectraAddButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.spectraAddButton.Location = new System.Drawing.Point(434, 89);
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
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.spectraRemoveButton.Location = new System.Drawing.Point(434, 118);
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
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.spectraClearButton.Location = new System.Drawing.Point(434, 147);
            this.spectraClearButton.Name = "spectraClearButton";
            this.spectraClearButton.Size = new System.Drawing.Size(154, 23);
            this.spectraClearButton.TabIndex = 8;
            this.spectraClearButton.Text = "Clear list";
            this.spectraClearButton.UseVisualStyleBackColor = true;
            this.spectraClearButton.Click += new System.EventHandler(this.spectraClearButton_Click);
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
            this.optionsGroupBox.Location = new System.Drawing.Point(624, 12);
            this.optionsGroupBox.Name = "optionsGroupBox";
            this.optionsGroupBox.Size = new System.Drawing.Size(330, 214);
            this.optionsGroupBox.TabIndex = 1;
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
            this.outputLabel.Location = new System.Drawing.Point(12, 236);
            this.outputLabel.Name = "outputLabel";
            this.outputLabel.Size = new System.Drawing.Size(80, 13);
            this.outputLabel.TabIndex = 2;
            this.outputLabel.Text = "Output file:";
            //
            // outputTextBox
            //
            this.outputTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right));
            this.outputTextBox.Location = new System.Drawing.Point(98, 233);
            this.outputTextBox.Name = "outputTextBox";
            this.outputTextBox.Size = new System.Drawing.Size(690, 20);
            this.outputTextBox.TabIndex = 3;
            //
            // outputBrowseButton
            //
            this.outputBrowseButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.outputBrowseButton.Location = new System.Drawing.Point(794, 231);
            this.outputBrowseButton.Name = "outputBrowseButton";
            this.outputBrowseButton.Size = new System.Drawing.Size(80, 23);
            this.outputBrowseButton.TabIndex = 4;
            this.outputBrowseButton.Text = "Browse...";
            this.outputBrowseButton.UseVisualStyleBackColor = true;
            this.outputBrowseButton.Click += new System.EventHandler(this.outputBrowseButton_Click);
            //
            // runButton
            //
            this.runButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.runButton.Location = new System.Drawing.Point(12, 263);
            this.runButton.Name = "runButton";
            this.runButton.Size = new System.Drawing.Size(120, 26);
            this.runButton.TabIndex = 5;
            this.runButton.Text = "Build curve";
            this.runButton.UseVisualStyleBackColor = true;
            this.runButton.Click += new System.EventHandler(this.runButton_Click);
            //
            // saveButton
            //
            this.saveButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.saveButton.Enabled = false;
            this.saveButton.Location = new System.Drawing.Point(138, 263);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(120, 26);
            this.saveButton.TabIndex = 6;
            this.saveButton.Text = "Save curve";
            this.saveButton.UseVisualStyleBackColor = true;
            this.saveButton.Click += new System.EventHandler(this.saveButton_Click);
            //
            // exportButton
            //
            this.exportButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.exportButton.Enabled = false;
            this.exportButton.Location = new System.Drawing.Point(264, 263);
            this.exportButton.Name = "exportButton";
            this.exportButton.Size = new System.Drawing.Size(120, 26);
            this.exportButton.TabIndex = 7;
            this.exportButton.Text = "Export CSV...";
            this.exportButton.UseVisualStyleBackColor = true;
            this.exportButton.Click += new System.EventHandler(this.exportButton_Click);
            //
            // progressBar
            //
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right));
            this.progressBar.Location = new System.Drawing.Point(794, 266);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(160, 20);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 8;
            this.progressBar.Visible = false;
            //
            // statusLabel
            //
            this.statusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right));
            this.statusLabel.AutoEllipsis = true;
            this.statusLabel.Location = new System.Drawing.Point(396, 269);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(392, 16);
            this.statusLabel.TabIndex = 9;
            //
            // splitContainer
            //
            this.splitContainer.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom
                | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right));
            this.splitContainer.Location = new System.Drawing.Point(12, 298);
            this.splitContainer.Name = "splitContainer";
            this.splitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.splitContainer.Panel1.Controls.Add(this.graph);
            this.splitContainer.Panel2.Controls.Add(this.logTextBox);
            this.splitContainer.Size = new System.Drawing.Size(942, 412);
            this.splitContainer.SplitterDistance = 300;
            this.splitContainer.TabIndex = 10;
            //
            // graph
            //
            this.graph.BackColor = System.Drawing.Color.White;
            this.graph.Dock = System.Windows.Forms.DockStyle.Fill;
            this.graph.Location = new System.Drawing.Point(0, 0);
            this.graph.Name = "graph";
            this.graph.Size = new System.Drawing.Size(942, 300);
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
            this.Controls.Add(this.inputGroupBox);
            this.Controls.Add(this.optionsGroupBox);
            this.Controls.Add(this.outputLabel);
            this.Controls.Add(this.outputTextBox);
            this.Controls.Add(this.outputBrowseButton);
            this.Controls.Add(this.runButton);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.exportButton);
            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.splitContainer);
            this.MinimumSize = new System.Drawing.Size(820, 560);
            this.Name = "EfficiencyMakerForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Efficiency maker";
            // Подписи заданы выше по-английски и здесь перекрываются ресурсами:
            // ApplyResources молча пропускает отсутствующий ключ, поэтому
            // нейтральная сборка остаётся английской, а ru.resx — русской.
            resources.ApplyResources(this, "$this");
            foreach (System.Windows.Forms.Control control in new System.Windows.Forms.Control[] {
                this.inputGroupBox, this.referenceLabel, this.referenceBrowseButton,
                this.referenceClearButton, this.spectraLabel, this.spectraAddButton,
                this.spectraRemoveButton, this.spectraClearButton, this.optionsGroupBox,
                this.chainsLabel, this.orderLabel, this.minIntensityLabel,
                this.minSignificanceLabel, this.backgroundCheckBox, this.anchorLabel,
                this.anchorHintLabel, this.outputLabel, this.outputBrowseButton,
                this.runButton, this.saveButton, this.exportButton })
            {
                resources.ApplyResources(control, control.Name);
            }
            this.inputGroupBox.ResumeLayout(false);
            this.inputGroupBox.PerformLayout();
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

        System.Windows.Forms.GroupBox inputGroupBox;
        System.Windows.Forms.Label referenceLabel;
        System.Windows.Forms.TextBox referenceTextBox;
        System.Windows.Forms.Button referenceBrowseButton;
        System.Windows.Forms.Button referenceClearButton;
        System.Windows.Forms.Label spectraLabel;
        System.Windows.Forms.ListBox spectraListBox;
        System.Windows.Forms.Button spectraAddButton;
        System.Windows.Forms.Button spectraRemoveButton;
        System.Windows.Forms.Button spectraClearButton;
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
