using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    /// <summary>
    /// Раскладка формы матрицы отклика. Собирается кодом, а не дизайнером, —
    /// как и вкладка «Эффективность» в конфигурации устройства: там же лежит
    /// причина, по которой у этих экранов нет `.resx` с координатами.
    /// </summary>
    public partial class ResponseMatrixForm
    {
        const int Pad = 12;
        const int FormWidth = 560;
        const int LabelWidth = 130;
        const int FieldWidth = 90;

        /// <summary>Шаг строк в подробностях: высота шрифта плюс воздух.</summary>
        const int DetailsRowPitch = 20;

        void BuildLayout()
        {
            this.Text = Resources.ResponseMatrixTitle;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(FormWidth, 470);

            int y = Pad;

            // --- зачем это вообще -----------------------------------------
            var about = new Label
            {
                Text = Resources.ResponseMatrixWhatFor,
                Location = new Point(Pad, y),
                Size = new Size(FormWidth - 2 * Pad, 96),
                ForeColor = SystemColors.GrayText
            };
            this.Controls.Add(about);
            y += about.Height + 8;

            // --- состояние -------------------------------------------------
            this.stateLabel = new Label
            {
                Location = new Point(Pad, y),
                Size = new Size(FormWidth - 2 * Pad, 34),
                Font = new Font(this.Font, FontStyle.Bold)
            };
            this.Controls.Add(this.stateLabel);
            y += this.stateLabel.Height + 2;

            // Версии генерации прямым текстом: физика переноса и формат файла.
            // Браковка по ним молчалива (Load просто вернёт null), и без этой
            // строки «нет матрицы» и «матрица есть, но другого поколения»
            // неотличимы.
            this.versionsLabel = new Label
            {
                Location = new Point(Pad, y),
                Size = new Size(FormWidth - 2 * Pad, 18),
                ForeColor = SystemColors.GrayText
            };
            this.Controls.Add(this.versionsLabel);
            y += this.versionsLabel.Height + 4;

            // Подробности — не одна многострочная надпись, а строки по одной на
            // метку с явным шагом: у Label межстрочный интервал прибит к высоте
            // шрифта, и четыре строки подряд читаются одним слипшимся блоком.
            this.detailsPanel = new Panel
            {
                Location = new Point(Pad, y),
                Size = new Size(FormWidth - 2 * Pad, DetailsRowPitch * 4 + 6)
            };
            this.Controls.Add(this.detailsPanel);
            y += this.detailsPanel.Height + 10;

            // --- выключатель использования (W11) ---------------------------
            // Раньше годная матрица включалась в разбор сама и выключателя не
            // было вовсе. Галка пишет в конфигурацию кривой
            // (EfficiencyConfigData.UseResponseMatrix) и действует на все
            // спектры с этой кривой; место ей здесь по решению Amber.
            this.useMatrixCheck = new CheckBox
            {
                Text = Resources.ResponseMatrixUseInFsa,
                Location = new Point(Pad, y),
                Size = new Size(FormWidth - 2 * Pad, 22),
                Checked = this.config == null || this.config.UseResponseMatrix,
            };
            this.useMatrixCheck.CheckedChanged += this.UseMatrixChanged;
            this.Controls.Add(this.useMatrixCheck);
            y += this.useMatrixCheck.Height + 6;

            // --- параметры -------------------------------------------------
            var box = new GroupBox
            {
                Text = Resources.ResponseMatrixParameters,
                Location = new Point(Pad, y),
                Size = new Size(FormWidth - 2 * Pad, 116)
            };
            this.Controls.Add(box);

            int row = 20;
            this.minEnergyBox = this.Field(box, Resources.ResponseMatrixMinEnergy, Pad, row, 10, 500, 0, 30);
            this.maxEnergyBox = this.Field(box, Resources.ResponseMatrixMaxEnergy,
                                           Pad + LabelWidth + FieldWidth + 24, row, 500, 10000, 0, 3000);

            row += 28;
            this.nodesBox = this.Field(box, Resources.ResponseMatrixNodes, Pad, row, 8, 500, 0, 100);
            this.binBox = this.Field(box, Resources.ResponseMatrixBin,
                                     Pad + LabelWidth + FieldWidth + 24, row, 1, 20, 0, 2);

            row += 28;
            this.historiesBox = this.Field(box, Resources.ResponseMatrixHistories, Pad, row,
                                           1000, 10000000, 0, 300000);
            this.threadsBox = this.Field(box, Resources.ResponseMatrixThreads,
                                         Pad + LabelWidth + FieldWidth + 24, row,
                                         1, 64, 0, Math.Max(1, Environment.ProcessorCount - 1));

            this.historiesBox.Increment = 50000;
            this.nodesBox.Increment = 10;

            // Разделитель тысяч — только у числа историй. У энергии он вреден:
            // «3,000 кэВ» читается как три.
            this.historiesBox.ThousandsSeparator = true;

            // Оценка времени пересчитывается на любое изменение: параметры и
            // время связаны прямо, и человек должен видеть цену сразу, а не
            // после нажатия «Посчитать».
            this.minEnergyBox.ValueChanged += this.ParametersChanged;
            this.maxEnergyBox.ValueChanged += this.ParametersChanged;
            this.nodesBox.ValueChanged += this.ParametersChanged;
            this.binBox.ValueChanged += this.ParametersChanged;
            this.historiesBox.ValueChanged += this.ParametersChanged;
            this.threadsBox.ValueChanged += this.ParametersChanged;

            y += box.Height + 6;

            this.estimateLabel = new Label
            {
                Location = new Point(Pad, y),
                Size = new Size(FormWidth - 2 * Pad, 18)
            };
            this.Controls.Add(this.estimateLabel);
            y += 24;

            // --- ход счёта -------------------------------------------------
            this.progressBar = new ProgressBar
            {
                Location = new Point(Pad, y),
                Size = new Size(FormWidth - 2 * Pad - 90, 20)
            };
            this.Controls.Add(this.progressBar);

            this.cancelButton = new Button
            {
                Text = Resources.ResponseMatrixCancel,
                Location = new Point(FormWidth - Pad - 82, y - 1),
                Size = new Size(82, 24),
                Enabled = false
            };
            this.cancelButton.Click += this.CancelClick;
            this.Controls.Add(this.cancelButton);
            y += 26;

            // Три строки, а не две: к «готово за …» дописывается предупреждение
            // о статистике континуума (F23), и оно длинное.
            this.progressLabel = new Label
            {
                Location = new Point(Pad, y),
                Size = new Size(FormWidth - 2 * Pad, 48)
            };
            this.Controls.Add(this.progressLabel);
            y += 54;

            // --- кнопки ----------------------------------------------------
            this.computeButton = new Button
            {
                Text = Resources.ResponseMatrixCompute,
                Location = new Point(Pad, y),
                Size = new Size(120, 26)
            };
            this.computeButton.Click += this.ComputeClick;
            this.Controls.Add(this.computeButton);

            this.saveButton = new Button
            {
                Text = Resources.ResponseMatrixSave,
                Location = new Point(Pad + 128, y),
                Size = new Size(240, 26),
                Enabled = false
            };
            this.saveButton.Click += this.SaveClick;
            this.Controls.Add(this.saveButton);

            this.closeButton = new Button
            {
                Text = Resources.ResponseMatrixClose,
                Location = new Point(FormWidth - Pad - 82, y),
                Size = new Size(82, 26)
            };
            this.closeButton.Click += delegate { this.Close(); };
            this.Controls.Add(this.closeButton);

            this.CancelButton = this.closeButton;
            this.ClientSize = new Size(FormWidth, y + 26 + Pad);
        }

        /// <summary>Разложить подробности по строкам с шагом.</summary>
        void SetDetails(string text)
        {
            this.detailsText = text ?? "";
            this.detailsPanel.Controls.Clear();
            if (this.detailsText.Length == 0)
            {
                return;
            }

            string[] lines = this.detailsText.Split(
                new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                this.detailsPanel.Controls.Add(new Label
                {
                    Text = lines[i],
                    Location = new Point(0, i * DetailsRowPitch),
                    Size = new Size(this.detailsPanel.Width, DetailsRowPitch),
                    TextAlign = ContentAlignment.MiddleLeft
                });
            }
        }

        void ParametersChanged(object sender, EventArgs e)
        {
            this.UpdateEstimateAsync();
        }

        NumericUpDown Field(Control parent, string caption, int x, int y,
                            decimal min, decimal max, int decimals, decimal value)
        {
            parent.Controls.Add(new Label
            {
                Text = caption,
                Location = new Point(x, y + 3),
                Size = new Size(LabelWidth, 18),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var box = new NumericUpDown
            {
                Location = new Point(x + LabelWidth, y),
                Size = new Size(FieldWidth, 20),
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Value = value
            };
            parent.Controls.Add(box);
            return box;
        }
    }
}
