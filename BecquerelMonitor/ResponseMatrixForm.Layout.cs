using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Windows.Forms;
using CheckBoxState = System.Windows.Forms.VisualStyles.CheckBoxState;

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

            // (П242) Галки «Use the matrix in full-spectrum analysis (FSA)»
            // (W11) здесь больше нет — снята по слову Amber 07.10.2026: «Эту
            // галку убрать. Значение по умолчанию взять - включена.» Годная
            // матрица идёт в разбор всегда (EfficiencyConfigData.UseResponseMatrix).
            //
            // (`AMBER219`, П245) На её месте — галка «Use Nvidia GPU» (постановка
            // Amber 07.10.2026). Годность GPU спрашивается при открытии
            // (`RmGpu.Probe`); негодная галка выключена, причина — подсказкой.
            //
            // ⚠ Негодная галка НЕ выключается (`Enabled`), а рисуется выключенной
            // (<see cref="GpuCheckBox.Unavailable"/>) и не переключается
            // (`AutoCheck = false`): выключенный контрол WinForms мыши не
            // получает, и ToolTip над ним не показывается вовсе — измерено
            // экраном 07.10.2026: подсказка всплывала только правее галки, над
            // панелью, а над самой галкой — нет. То есть причина, ради которой
            // подсказка и заведена, не доходила бы до человека ровно тогда,
            // когда нужна. Панель держит подсказку ещё и справа от галки.
            this.gpuPanel = new Panel
            {
                Location = new Point(Pad, y),
                Size = new Size(FormWidth - 2 * Pad, 22)
            };
            this.gpuCheck = new GpuCheckBox
            {
                Text = Resources.ResponseMatrixUseGpu,
                Location = new Point(0, 0),
                AutoSize = true,
                Checked = false,
                AutoCheck = false,
                Unavailable = true
            };
            this.gpuCheck.CheckedChanged += this.GpuCheckChanged;
            this.gpuPanel.Controls.Add(this.gpuCheck);
            this.Controls.Add(this.gpuPanel);
            this.gpuTip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100 };
            y += this.gpuPanel.Height + 6;

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

            // ⛔ РАЗДЕЛИТЕЛЯ ТЫСЯЧ НЕТ НИ У ОДНОГО ПОЛЯ (`A244`, решение Amber
            // 05.09.2026: «группировки разрядов нет вовсе»). Здесь стояло
            // `historiesBox.ThousandsSeparator = true` — единственная
            // группировка этой формы, и она бралась у КУЛЬТУРЫ ПОТОКА: на
            // русской системе «3 000 000» неразрывными пробелами, на
            // английской «3,000,000». Довод прежнего комментария («у энергии
            // разделитель вреден: 3,000 кэВ читается как три») правилом снят —
            // он вреден везде.
            //
            // ⚠ Прежде здесь стояло, что целые поля (`decimals = 0`) правилу
            // Amber «не противоречат ничем», раз разделителя ДРОБНОЙ части в
            // них не появляется. Замер это снял (`A261`, полоса F68): у целого
            // поля культура потока правит не печать, а ВВОД — штатный
            // `NumericUpDown` съедает набранную точку своим фильтром знаков и
            // разбирает содержимое `Decimal.Parse(Text, CurrentCulture)`.
            // Поля этой формы переведены на общий `InvariantNumericUpDown`,
            // который подменить культуру как раз даёт; сторож против отката —
            // `tools/check_numeric_updown.py`.

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

        /// <summary>
        /// (`AMBER219`) Галка, которая умеет быть «недоступной», оставаясь живой
        /// для мыши: при <see cref="Unavailable"/> рисуется штатным выключенным
        /// видом (`CheckBoxRenderer`, серый текст и серый квадрат), а
        /// переключение снимает вызывающий (`AutoCheck = false`). Настоящее
        /// `Enabled = false` подсказку убивает (см. раскладку выше).
        /// </summary>
        sealed class GpuCheckBox : CheckBox
        {
            bool unavailable;

            public bool Unavailable
            {
                get { return this.unavailable; }
                set
                {
                    this.unavailable = value;
                    this.AutoCheck = !value;
                    this.Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (!this.unavailable || !this.Enabled)
                {
                    base.OnPaint(e);
                    return;
                }

                CheckBoxState state = this.Checked ? CheckBoxState.CheckedDisabled : CheckBoxState.UncheckedDisabled;
                CheckBoxRenderer.DrawParentBackground(e.Graphics, this.ClientRectangle, this);
                Size glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
                var glyphAt = new Point(0, Math.Max(0, (this.Height - glyph.Height) / 2));
                var textAt = new Rectangle(glyph.Width + 3, 0, Math.Max(1, this.Width - glyph.Width - 3), this.Height);
                CheckBoxRenderer.DrawCheckBox(e.Graphics, glyphAt, textAt, this.Text, this.Font,
                                              TextFormatFlags.VerticalCenter | TextFormatFlags.Left,
                                              false, state);
            }
        }

        InvariantNumericUpDown Field(Control parent, string caption, int x, int y,
                            decimal min, decimal max, int decimals, decimal value)
        {
            parent.Controls.Add(new Label
            {
                Text = caption,
                Location = new Point(x, y + 3),
                Size = new Size(LabelWidth, 18),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var box = new InvariantNumericUpDown
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
