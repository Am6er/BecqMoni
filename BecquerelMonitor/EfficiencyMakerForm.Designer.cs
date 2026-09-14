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
            this.calcHintLabel = new System.Windows.Forms.Label();
            this.calculateButton = new System.Windows.Forms.Button();
            this.saveButton = new System.Windows.Forms.Button();
            this.exportButton = new System.Windows.Forms.Button();
            this.statusLabel = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.logTextBox = new System.Windows.Forms.TextBox();
            this.graph = new BecquerelMonitor.EfficiencyMaker.EfficiencyCurveGraph();
            this.tabControl.SuspendLayout();
            this.tabPageCalculate.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            this.SuspendLayout();
            //
            // tabControl
            //
            // Вкладок две: редактор геометрии (его добавляет BuildGeometryTab
            // первой) и расчёт из геометрии. Вкладка «Fit to measured spectra»
            // — эмпирическое восстановление кривой по спектрам — снята
            // 13.09.2026 по решению Amber (`AMBER25`).
            this.tabControl.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right));
            this.tabControl.Controls.Add(this.tabPageCalculate);
            this.tabControl.Location = new System.Drawing.Point(12, 12);
            this.tabControl.Name = "tabControl";
            this.tabControl.SelectedIndex = 0;
            this.tabControl.Size = new System.Drawing.Size(942, 236);
            this.tabControl.TabIndex = 0;
            this.tabControl.SelectedIndexChanged += new System.EventHandler(this.tabControl_SelectedIndexChanged);
            //
            // tabPageCalculate
            //
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
            // calcHintLabel
            //
            // AutoSize обязателен. Без него метка не появилась на вкладке вовсе
            // — ни на экране, ни в дереве UI Automation, хотя текст был задан и
            // размер выставлен руками. Заданный руками Size убран: пусть высоту
            // считает сама метка по переносу в MaximumSize, иначе перевод
            // другой длины опять её обрежет.
            this.calcHintLabel.AutoSize = true;
            this.calcHintLabel.ForeColor = System.Drawing.Color.DimGray;
            this.calcHintLabel.Location = new System.Drawing.Point(10, 12);
            this.calcHintLabel.MaximumSize = new System.Drawing.Size(600, 0);
            this.calcHintLabel.Name = "calcHintLabel";
            this.calcHintLabel.TabIndex = 4;
            this.calcHintLabel.Text = BecquerelMonitor.Properties.Resources.EfficiencyMakerCalcHint;
            //
            // calculateButton
            //
            this.calculateButton.Anchor = ((System.Windows.Forms.AnchorStyles)
                (System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left));
            this.calculateButton.Enabled = false;
            this.calculateButton.Location = new System.Drawing.Point(13, 56);
            this.calculateButton.Name = "calculateButton";
            this.calculateButton.Size = new System.Drawing.Size(200, 26);
            this.calculateButton.TabIndex = 5;
            this.calculateButton.Text = "Calculate from geometry";
            this.calculateButton.UseVisualStyleBackColor = true;
            this.calculateButton.Click += new System.EventHandler(this.calculateButton_Click);
            //
            // saveButton
            //
            this.saveButton.Anchor = ((System.Windows.Forms.AnchorStyles)(System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left));
            this.saveButton.Enabled = false;
            this.saveButton.Location = new System.Drawing.Point(12, 762);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(120, 26);
            this.saveButton.TabIndex = 4;
            this.saveButton.Text = "Save curve";
            this.saveButton.UseVisualStyleBackColor = true;
            this.saveButton.Click += new System.EventHandler(this.saveButton_Click);
            //
            // exportButton
            //
            this.exportButton.Anchor = ((System.Windows.Forms.AnchorStyles)(System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left));
            this.exportButton.Enabled = false;
            this.exportButton.Location = new System.Drawing.Point(138, 762);
            this.exportButton.Name = "exportButton";
            this.exportButton.Size = new System.Drawing.Size(120, 26);
            this.exportButton.TabIndex = 5;
            this.exportButton.Text = "Export CSV...";
            this.exportButton.UseVisualStyleBackColor = true;
            this.exportButton.Click += new System.EventHandler(this.exportButton_Click);
            //
            // statusLabel
            //
            this.statusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right));
            this.statusLabel.AutoEllipsis = true;
            this.statusLabel.Location = new System.Drawing.Point(266, 768);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(520, 16);
            this.statusLabel.TabIndex = 6;
            //
            // progressBar
            //
            this.progressBar.Anchor = ((System.Windows.Forms.AnchorStyles)(System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right));
            this.progressBar.Location = new System.Drawing.Point(794, 765);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(160, 20);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 7;
            this.progressBar.Visible = false;
            //
            // splitContainer
            //
            this.splitContainer.Anchor = ((System.Windows.Forms.AnchorStyles)(System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right));
            this.splitContainer.Location = new System.Drawing.Point(12, 260);
            this.splitContainer.Name = "splitContainer";
            this.splitContainer.Orientation = System.Windows.Forms.Orientation.Horizontal;
            this.splitContainer.Panel1.Controls.Add(this.graph);
            this.splitContainer.Panel2.Controls.Add(this.logTextBox);
            this.splitContainer.Size = new System.Drawing.Size(942, 490);
            this.splitContainer.SplitterDistance = 262;
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
            // Перенос по словам: в журнале есть строки в полторы сотни знаков
            // (разбор геометрии, состав сцены), и с горизонтальной прокруткой
            // конец такой строки не виден вовсе — а именно там стоит то, из-за
            // чего строку и напечатали. Прокрутка остаётся только вертикальная:
            // при переносе горизонтальной нечего показывать.
            this.logTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.logTextBox.WordWrap = true;
            this.logTextBox.TabIndex = 0;
            //
            // EfficiencyMakerForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(966, 800);
            this.Controls.Add(this.tabControl);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.exportButton);
            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.splitContainer);
            this.Icon = BecquerelMonitor.Properties.Resources.becqmoni;
            this.MinimumSize = new System.Drawing.Size(900, 560);
            this.Name = "EfficiencyMakerForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Efficiency maker";
            // Подписи заданы выше по-английски и здесь перекрываются ресурсами:
            // ApplyResources молча пропускает отсутствующий ключ, поэтому
            // нейтральная сборка остаётся английской, а ru.resx — русской.
            // Заголовок вкладки и подсказка расчёта в этот список не входят:
            // они взяты прямо из общих Resources выше, где у них уже есть
            // русская пара.
            resources.ApplyResources(this, "$this");
            foreach (System.Windows.Forms.Control control in new System.Windows.Forms.Control[] {
                this.saveButton, this.exportButton, this.calculateButton })
            {
                resources.ApplyResources(control, control.Name);
            }
            this.tabPageCalculate.ResumeLayout(false);
            this.tabPageCalculate.PerformLayout();
            this.tabControl.ResumeLayout(false);
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
        System.Windows.Forms.Label calcHintLabel;
        System.Windows.Forms.Button calculateButton;
        System.Windows.Forms.Button saveButton;
        System.Windows.Forms.Button exportButton;
        System.Windows.Forms.Label statusLabel;
        System.Windows.Forms.ProgressBar progressBar;
        System.Windows.Forms.SplitContainer splitContainer;
        System.Windows.Forms.TextBox logTextBox;
        BecquerelMonitor.EfficiencyMaker.EfficiencyCurveGraph graph;
    }
}
