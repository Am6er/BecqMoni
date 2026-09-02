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
            // Нижняя граница поля 10 → 5 вместе с умолчанием сетки (`T49`):
            // поле, которое не даёт ввести умолчание, зажало бы его при первом
            // же открытии формы, и «Пересчитать» дало бы ДРУГУЮ матрицу.
            this.minEnergyBox = this.Field(box, Resources.ResponseMatrixMinEnergy, Pad, row, 5, 500, 0, 5);
            this.maxEnergyBox = this.Field(box, Resources.ResponseMatrixMaxEnergy,
                                           Pad + LabelWidth + FieldWidth + 24, row, 500, 10000, 0, 3000);

            row += 28;
            // Умолчание поля идёт за умолчанием сетки (`T49`): сто узлов при
            // крае 5 кэВ дают шаг 6.67 % на узел вместо прежних 4.76 %.
            this.nodesBox = this.Field(box, Resources.ResponseMatrixNodes, Pad, row, 8, 500, 0, 140);
            this.binBox = this.Field(box, Resources.ResponseMatrixBin,
                                     Pad + LabelWidth + FieldWidth + 24, row, 1, 20, 0, 2);

            row += 28;
            // Умолчание поля — то же, что у `ResponseMatrixOptions.Histories`
            // (3 млн, `A39`): два места, и разойтись им нельзя. Потолок поднят
            // до 30 млн — при трёх миллионах прежний в 10 млн оставлял всего
            // тройной запас на ручную правку.
            this.historiesBox = this.Field(box, Resources.ResponseMatrixHistories, Pad, row,
                                           1000, 30000000, 0, 3000000);
            this.threadsBox = this.Field(box, Resources.ResponseMatrixThreads,
                                         Pad + LabelWidth + FieldWidth + 24, row,
                                         1, 64, 0, Math.Max(1, Environment.ProcessorCount - 1));

            this.historiesBox.Increment = 500000;
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

            // (`A46`) Строки предварительной оценки времени здесь больше нет:
            // окно освободилось на её высоту.

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

        /// <summary>
        /// Разложить подробности по строкам с шагом.
        ///
        /// (W22) Панель РАСТЁТ под текст, а строки переносятся по словам.
        /// Прежде и то, и другое было прибито: высота панели — место ровно под
        /// четыре строки (`DetailsRowPitch * 4 + 6`), а сами `Label` шириной в
        /// панель и без переноса. `ResponseMatrixDetails` — как раз четыре
        /// строки, поэтому ПЯТАЯ уходила за нижний край и пропадала целиком, и
        /// пятой была именно `ResponseMatrixRangeDiffers` (E18 «б») — признак
        /// «диапазоны кривой и матрицы разошлись», ради которого всё и
        /// заводилось. Длинное предложение при этом обрезалось бы ещё и
        /// справа. Признак, до которого не доходит глаз, — не признак.
        /// </summary>
        void SetDetails(string text)
        {
            this.detailsText = text ?? "";
            this.detailsPanel.Controls.Clear();
            if (this.detailsText.Length == 0)
            {
                this.ResizeDetails(0);
                return;
            }

            string[] lines = this.detailsText.Split(
                new[] { Environment.NewLine, "\n" }, StringSplitOptions.None);
            int width = this.detailsPanel.Width;
            int top = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                // Высота строки меряется с переносом по словам, а не берётся
                // шагом: одна длинная строка занимает две и обязана быть видна
                // целиком. Шаг остаётся нижней границей — ради воздуха между
                // строками, ради которого строки и разнесены по меткам.
                Size need = TextRenderer.MeasureText(
                    lines[i], this.Font, new Size(width, int.MaxValue),
                    TextFormatFlags.WordBreak);
                int height = Math.Max(DetailsRowPitch, need.Height + 4);

                this.detailsPanel.Controls.Add(new Label
                {
                    Text = lines[i],
                    Location = new Point(0, top),
                    Size = new Size(width, height),
                    TextAlign = ContentAlignment.MiddleLeft,
                    UseMnemonic = false
                });

                top += height;
            }

            this.ResizeDetails(top + 6);
        }

        /// <summary>
        /// (W22) Подогнать высоту панели подробностей под содержимое и сдвинуть
        /// всё, что ниже неё, вместе с нижней границей окна.
        ///
        /// Форма собрана абсолютными координатами, поэтому «растянуть» панель
        /// само по себе ничего не даёт — надо развести соседей. Двигаются ВСЕ
        /// прямые потомки формы, стоящие ниже панели, а не поимённый список:
        /// список пришлось бы дополнять при каждой новой кнопке, и забытый
        /// элемент наехал бы на подробности молча.
        /// </summary>
        void ResizeDetails(int height)
        {
            int wanted = Math.Max(DetailsRowPitch, height);
            int delta = wanted - this.detailsPanel.Height;
            if (delta == 0)
            {
                return;
            }

            int edge = this.detailsPanel.Bottom;
            this.detailsPanel.Height = wanted;
            foreach (Control control in this.Controls)
            {
                if (!ReferenceEquals(control, this.detailsPanel) && control.Top >= edge)
                {
                    control.Top += delta;
                }
            }

            this.ClientSize = new Size(this.ClientSize.Width,
                                       Math.Max(0, this.ClientSize.Height + delta));
        }

        void ParametersChanged(object sender, EventArgs e)
        {

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
