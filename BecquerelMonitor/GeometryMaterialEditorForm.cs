using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    /// <summary>
    /// Редактор библиотеки веществ конструктора геометрий (`E20`).
    ///
    /// Зачем он. До 15.08.2026 список веществ был зашит в
    /// `GeometryMaterialLibrary.Build()`, и своё вещество заводилось только
    /// правкой исходника и пересборкой — так и появился оксид лютеция. Хуже
    /// того, плотность рядом со списком правилась, но НИГДЕ не запоминалась:
    /// «оксид лютеция, порошок, ρ = 1.06» приходилось набирать руками при
    /// каждом расчёте, и опечатка в нём была невидима. Цена измерена: проба,
    /// молча оставшаяся воздухом, завысила кривую AS80x80 в 2.6 раза (`E19`).
    ///
    /// Что здесь можно, чего нельзя. Вещество задаётся ЛИБО формулой, либо
    /// смесью других веществ с массовыми весами — ровно как в формате `.in`
    /// (`Nmaterials`, `Name[i]`, `MatRelWeight[i]`). Вписывать массовые доли
    /// элементов руками нельзя нарочно: в файлах LSRM они записаны шестью
    /// знаками, и опечатка в такой строке кривую портит молча.
    ///
    /// Состав, который получится, показан внизу ВСЕГДА и пересчитывается на
    /// каждую букву — это единственная видимая проверка того, что формула
    /// разобрана так, как задумано: «Cs1 I1» и «CsI» дают разное, и увидеть это
    /// можно только по долям.
    ///
    /// Разметка кодом, а не дизайнером — как в `GeometryEditorPanel`, по той же
    /// причине (см. его шапку).
    /// </summary>
    public sealed class GeometryMaterialEditorForm : Form
    {
        readonly List<GeometryMaterialLibrary.Entry> working =
            new List<GeometryMaterialLibrary.Entry>();

        ListBox list;
        ComboBox filterCombo, kindCombo;
        TextBox nameBox, abbrBox, densityBox, formulaBox;
        RadioButton formulaRadio, mixtureRadio, tableRadio;
        Panel formulaPanel, mixturePanel;
        DataGridView componentsGrid;
        DataGridViewComboBoxColumn componentColumn;
        Label compositionLabel, problemLabel, loadErrorLabel;
        Button removeButton, densityFromPartsButton;

        bool loading;

        /// <summary>
        /// Вид, на котором список открылся. Пришедший из строки редактора
        /// геометрии: человек нажал «…» у пробы — значит, ему нужны пробы, а не
        /// все сорок веществ разом.
        /// </summary>
        public GeometryMaterialEditorForm(GeometryMaterialLibrary.MaterialKind kind)
        {
            // Копии, а не ссылки на общий список: «Отмена» обязана оставить
            // библиотеку нетронутой. И берутся ВСЕ вещества, а не только
            // выбранного вида — смесь пробы может состоять из чего угодно, а вид
            // задаёт лишь то, что показано сразу.
            foreach (GeometryMaterialLibrary.Entry entry in GeometryMaterialStore.Entries)
            {
                this.working.Add(entry.Clone());
            }

            this.BuildLayout();
            this.loading = true;
            try
            {
                this.filterCombo.SelectedIndex = 1 + (int)kind;
            }
            finally
            {
                this.loading = false;
            }

            this.RefreshList(null);
        }

        // ------------------------------------------------------------------
        // Разметка
        // ------------------------------------------------------------------

        void BuildLayout()
        {
            this.Text = Resources.GeometryMaterialsTitle;
            this.Icon = Resources.becqmoni;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(790, 560);
            this.MinimumSize = new Size(806, 599);

            this.loadErrorLabel = new Label
            {
                AutoSize = false,
                ForeColor = Color.Firebrick,
                Location = new Point(12, 10),
                Size = new Size(766, 32),
                Visible = false,
            };
            this.Controls.Add(this.loadErrorLabel);

            if (!string.IsNullOrEmpty(GeometryMaterialStore.LoadError))
            {
                // Молчать здесь нельзя: библиотека сейчас ВШИТАЯ, своих веществ
                // человек не видит, а «Сохранить» заменит непрочитанный файл.
                this.loadErrorLabel.Text = string.Format(CultureInfo.CurrentCulture,
                    Resources.GeometryMaterialsLoadFailed,
                    GeometryMaterialStore.FilePath, GeometryMaterialStore.LoadError);
                this.loadErrorLabel.Visible = true;
            }

            int top = this.loadErrorLabel.Visible ? 48 : 12;

            this.filterCombo = new ComboBox
            {
                Location = new Point(12, top),
                Size = new Size(240, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            this.filterCombo.Items.Add(Resources.GeometryMaterialsKindAll);
            foreach (GeometryMaterialLibrary.MaterialKind kind
                     in Enum.GetValues(typeof(GeometryMaterialLibrary.MaterialKind)))
            {
                this.filterCombo.Items.Add(KindName(kind));
            }

            this.filterCombo.SelectedIndex = 0;
            this.filterCombo.SelectedIndexChanged += (s, e) =>
            {
                if (!this.loading)
                {
                    this.RefreshList(this.Selected());
                }
            };
            this.Controls.Add(this.filterCombo);

            this.list = new ListBox
            {
                Location = new Point(12, top + 27),
                Size = new Size(240, 430),
                IntegralHeight = false,
            };
            this.list.SelectedIndexChanged += (s, e) => this.LoadSelected();
            this.Controls.Add(this.list);

            Button addButton = new Button
            {
                Location = new Point(12, top + 463),
                Size = new Size(116, 25),
                Text = Resources.GeometryMaterialsAdd,
                UseVisualStyleBackColor = true,
            };
            addButton.Click += this.AddClicked;
            this.Controls.Add(addButton);

            this.removeButton = new Button
            {
                Location = new Point(136, top + 463),
                Size = new Size(116, 25),
                Text = Resources.GeometryMaterialsRemove,
                UseVisualStyleBackColor = true,
            };
            this.removeButton.Click += this.RemoveClicked;
            this.Controls.Add(this.removeButton);

            this.BuildFields(top);

            Button ok = new Button
            {
                DialogResult = DialogResult.None,
                Location = new Point(578, top + 463),
                Size = new Size(96, 25),
                Text = Resources.GeometryMaterialsSave,
                UseVisualStyleBackColor = true,
            };
            ok.Click += this.SaveClicked;
            this.Controls.Add(ok);

            Button cancel = new Button
            {
                DialogResult = DialogResult.Cancel,
                Location = new Point(682, top + 463),
                Size = new Size(96, 25),
                Text = Resources.GeometryMaterialsCancel,
                UseVisualStyleBackColor = true,
            };
            this.Controls.Add(cancel);
            this.CancelButton = cancel;
        }

        void BuildFields(int top)
        {
            int x = 268;
            int y = top;

            this.Controls.Add(Caption(x, y + 3, Resources.GeometryMaterialsKind));
            this.kindCombo = new ComboBox
            {
                Location = new Point(x + 130, y),
                Size = new Size(200, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            foreach (GeometryMaterialLibrary.MaterialKind kind
                     in Enum.GetValues(typeof(GeometryMaterialLibrary.MaterialKind)))
            {
                this.kindCombo.Items.Add(KindName(kind));
            }

            this.kindCombo.SelectedIndexChanged += (s, e) => this.FieldChanged(true);
            this.Controls.Add(this.kindCombo);
            y += 30;

            this.Controls.Add(Caption(x, y + 3, Resources.GeometryMaterialsAbbr));
            this.abbrBox = new TextBox { Location = new Point(x + 130, y), Size = new Size(200, 20) };
            this.abbrBox.TextChanged += (s, e) => this.FieldChanged(true);
            this.Controls.Add(this.abbrBox);
            y += 26;

            this.Controls.Add(Caption(x, y + 3, Resources.GeometryMaterialsName));
            this.nameBox = new TextBox { Location = new Point(x + 130, y), Size = new Size(370, 20) };
            this.nameBox.TextChanged += (s, e) => this.FieldChanged(true);
            this.Controls.Add(this.nameBox);
            y += 22;

            // Имя пишется в файл `.in` и по нему же ищутся составляющие смесей —
            // поэтому оно не украшение, и сказать об этом надо там, где его
            // набирают.
            this.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(x + 130, y),
                MaximumSize = new Size(370, 0),
                Text = Resources.GeometryMaterialsNameHint,
            });
            y += 34;

            this.Controls.Add(Caption(x, y + 3, Resources.GeometryMaterialsDensity));
            this.densityBox = new TextBox
            {
                Location = new Point(x + 130, y),
                Size = new Size(90, 20),
                TextAlign = HorizontalAlignment.Right,
            };
            this.densityBox.TextChanged += (s, e) => this.FieldChanged(false);
            this.Controls.Add(this.densityBox);
            this.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(x + 226, y + 3),
                Text = Resources.GeometryEditorUnitDensity,
            });

            // Плотность смеси выводится из плотностей её частей: объёмы
            // складываются. Кнопка, а не автоподстановка, — плотность у одного
            // и того же вещества зависит от набивки, и молча переписывать
            // введённое человеком число нельзя.
            this.densityFromPartsButton = new Button
            {
                Location = new Point(x + 268, y - 1),
                Size = new Size(110, 23),
                Text = Resources.GeometryMaterialsDensityFromParts,
                UseVisualStyleBackColor = true,
            };
            this.densityFromPartsButton.Click += this.DensityFromPartsClick;
            this.Controls.Add(this.densityFromPartsButton);
            y += 32;

            this.formulaRadio = new RadioButton
            {
                AutoSize = true,
                Checked = true,
                Location = new Point(x, y),
                Text = Resources.GeometryMaterialsByFormula,
            };
            this.mixtureRadio = new RadioButton
            {
                AutoSize = true,
                Location = new Point(x + 180, y),
                Text = Resources.GeometryMaterialsByMixture,
            };

            // Третий способ — доли, ввезённые таблицей ЛСРМ. Выбрать его руками
            // нельзя (доли с клавиатуры не вписывают), но ПОКАЗАТЬ обязательно:
            // иначе человек правил бы формулу у вещества, состав которого от
            // формулы не зависит, и не понимал бы, почему ничего не меняется.
            // Переключение на формулу или смесь доли СНИМАЕТ, и состав внизу
            // меняется сразу — отказ от таблицы виден, а не случается молча.
            this.tableRadio = new RadioButton
            {
                AutoSize = true,
                Enabled = false,
                Location = new Point(x + 360, y),
                Text = Resources.GeometryMaterialsByTable,
            };
            this.formulaRadio.CheckedChanged += this.SourceKindChanged;
            this.mixtureRadio.CheckedChanged += this.SourceKindChanged;
            this.Controls.Add(this.formulaRadio);
            this.Controls.Add(this.mixtureRadio);
            this.Controls.Add(this.tableRadio);
            y += 26;

            this.formulaPanel = new Panel { Location = new Point(x, y), Size = new Size(510, 190) };
            this.formulaPanel.Controls.Add(Caption(0, 3, Resources.GeometryMaterialsFormula));
            this.formulaBox = new TextBox { Location = new Point(130, 0), Size = new Size(370, 20) };
            this.formulaBox.TextChanged += (s, e) => this.FieldChanged(false);
            this.formulaPanel.Controls.Add(this.formulaBox);
            this.formulaPanel.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(130, 24),
                MaximumSize = new Size(370, 0),
                Text = Resources.GeometryMaterialsFormulaHint,
            });
            this.Controls.Add(this.formulaPanel);

            this.mixturePanel = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(510, 190),
                Visible = false,
            };
            this.mixturePanel.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(0, 0),
                MaximumSize = new Size(500, 0),
                Text = Resources.GeometryMaterialsMixtureHint,
            });

            this.componentsGrid = new DataGridView
            {
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                AllowUserToResizeRows = false,
                Location = new Point(0, 34),
                RowHeadersWidth = 24,
                Size = new Size(500, 152),
            };
            this.componentColumn = new DataGridViewComboBoxColumn
            {
                HeaderText = Resources.GeometryMaterialsComponent,
                Width = 330,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            };
            DataGridViewTextBoxColumn weightColumn = new DataGridViewTextBoxColumn
            {
                HeaderText = Resources.GeometryMaterialsWeight,
                Width = 120,
            };
            this.componentsGrid.Columns.Add(this.componentColumn);
            this.componentsGrid.Columns.Add(weightColumn);
            this.componentsGrid.CellValueChanged += (s, e) => this.FieldChanged(false);
            // Выбор в выпадающем списке ячейки иначе доходит до модели только
            // после ухода из ячейки — состав внизу отставал на один щелчок.
            this.componentsGrid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (this.componentsGrid.IsCurrentCellDirty)
                {
                    this.componentsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            this.componentsGrid.UserDeletedRow += (s, e) => this.FieldChanged(false);
            this.mixturePanel.Controls.Add(this.componentsGrid);
            this.Controls.Add(this.mixturePanel);
            y += 198;

            this.Controls.Add(Caption(x, y, Resources.GeometryMaterialsComposition));
            this.compositionLabel = new Label
            {
                AutoSize = false,
                ForeColor = Color.DimGray,
                Location = new Point(x + 130, y),
                Size = new Size(370, 46),
            };
            this.Controls.Add(this.compositionLabel);
            y += 50;

            this.problemLabel = new Label
            {
                AutoSize = false,
                ForeColor = Color.Firebrick,
                Location = new Point(x, y),
                Size = new Size(500, 32),
            };
            this.Controls.Add(this.problemLabel);
        }

        static Label Caption(int x, int y, string text)
        {
            return new Label { AutoSize = true, Location = new Point(x, y), Text = text };
        }

        static string KindName(GeometryMaterialLibrary.MaterialKind kind)
        {
            switch (kind)
            {
                case GeometryMaterialLibrary.MaterialKind.Crystal:
                    return Resources.GeometryMaterialsKindCrystal;
                case GeometryMaterialLibrary.MaterialKind.Reflector:
                    return Resources.GeometryMaterialsKindReflector;
                case GeometryMaterialLibrary.MaterialKind.Cladding:
                    return Resources.GeometryMaterialsKindCladding;
                case GeometryMaterialLibrary.MaterialKind.BeakerWall:
                    return Resources.GeometryMaterialsKindBeakerWall;
                case GeometryMaterialLibrary.MaterialKind.Source:
                    return Resources.GeometryMaterialsKindSource;
                default:
                    return Resources.GeometryMaterialsKindOther;
            }
        }

        // ------------------------------------------------------------------
        // Список
        // ------------------------------------------------------------------

        /// <summary>Что показывать в списке — с оглядкой на выбранный вид.</summary>
        List<GeometryMaterialLibrary.Entry> Listed()
        {
            List<GeometryMaterialLibrary.Entry> shown = new List<GeometryMaterialLibrary.Entry>();
            foreach (GeometryMaterialLibrary.Entry entry in this.working)
            {
                if (this.filterCombo.SelectedIndex <= 0
                    || (int)entry.Kind == this.filterCombo.SelectedIndex - 1)
                {
                    shown.Add(entry);
                }
            }

            return shown;
        }

        void RefreshList(GeometryMaterialLibrary.Entry keep)
        {
            this.loading = true;
            try
            {
                this.list.Items.Clear();
                foreach (GeometryMaterialLibrary.Entry entry in this.Listed())
                {
                    this.list.Items.Add(entry);
                }

                int index = keep != null ? this.list.Items.IndexOf(keep) : -1;
                this.list.SelectedIndex = index >= 0 ? index : (this.list.Items.Count > 0 ? 0 : -1);
            }
            finally
            {
                this.loading = false;
            }

            this.LoadSelected();
        }

        /// <summary>
        /// Посчитать плотность смеси из плотностей её частей (объёмы
        /// складываются). Отказ — не молчание и не подстановка «примерно»:
        /// строкой проблем, теми же словами, что и остальные отказы формы.
        /// </summary>
        void DensityFromPartsClick(object sender, EventArgs e)
        {
            GeometryMaterialLibrary.Entry entry = this.Selected();
            if (entry == null)
            {
                return;
            }

            double density;
            string problem;
            if (!GeometryMaterialLibrary.TryDensityFromComponents(entry, this.Lookup,
                                                                  out density, out problem))
            {
                this.problemLabel.Text = problem;
                return;
            }

            this.densityBox.Text = density.ToString("0.######", CultureInfo.InvariantCulture);
            this.problemLabel.Text = string.Format(CultureInfo.CurrentCulture,
                                                   Resources.GeometryMaterialsDensityDone, density);
        }

        GeometryMaterialLibrary.Entry Selected()
        {
            return this.list.SelectedIndex >= 0
                ? (GeometryMaterialLibrary.Entry)this.list.Items[this.list.SelectedIndex]
                : null;
        }

        void LoadSelected()
        {
            GeometryMaterialLibrary.Entry entry = this.Selected();
            // Флаг ЗАПОМИНАЕТСЯ, а не сбрасывается в конце: сюда приходят из
            // `RefreshList`, у которого он уже поднят, и голое `false` в хвосте
            // снимало бы чужую защиту на середине перебора списка.
            bool was = this.loading;
            this.loading = true;
            try
            {
                bool has = entry != null;
                this.kindCombo.Enabled = has;
                this.abbrBox.Enabled = has;
                this.nameBox.Enabled = has;
                this.densityBox.Enabled = has;
                this.formulaRadio.Enabled = has;
                this.mixtureRadio.Enabled = has;
                this.formulaBox.Enabled = has;
                this.componentsGrid.Enabled = has;
                this.removeButton.Enabled = has;

                this.kindCombo.SelectedIndex = has ? (int)entry.Kind : -1;
                this.abbrBox.Text = has ? entry.Abbr ?? "" : "";
                this.nameBox.Text = has ? entry.Name ?? "" : "";
                this.densityBox.Text = has
                    ? entry.Density.ToString("0.######", CultureInfo.InvariantCulture) : "";
                this.formulaBox.Text = has ? entry.Formula ?? "" : "";

                bool table = has && entry.ElementFractions.Count > 0;
                bool mixture = has && !table && entry.IsMixture;
                this.tableRadio.Enabled = table;
                this.tableRadio.Checked = table;
                this.mixtureRadio.Checked = mixture;
                this.formulaRadio.Checked = !table && !mixture;
                this.formulaPanel.Visible = !table && !mixture;
                this.mixturePanel.Visible = mixture;

                // Строки СНАЧАЛА, список выбора потом: ячейка выпадающего списка
                // с уже негодным значением роняет `DataGridView` исключением
                // «value is not valid», а не пустеет.
                this.componentsGrid.Rows.Clear();
                this.FillComponentChoices();
                if (has)
                {
                    foreach (GeometryMaterialComponent component in entry.Components)
                    {
                        int row = this.componentsGrid.Rows.Add();
                        this.componentsGrid.Rows[row].Cells[0].Value =
                            this.componentColumn.Items.Contains(component.Material)
                                ? component.Material : null;
                        this.componentsGrid.Rows[row].Cells[1].Value =
                            component.Weight.ToString("0.######", CultureInfo.InvariantCulture);
                    }
                }
            }
            finally
            {
                this.loading = was;
            }

            this.ShowComposition();
        }

        /// <summary>
        /// Чем можно наполнять смесь: всеми веществами библиотеки, кроме самого
        /// правимого. Себя в составляющие не подставить — это кратчайшее кольцо.
        /// </summary>
        void FillComponentChoices()
        {
            GeometryMaterialLibrary.Entry entry = this.Selected();
            this.componentColumn.Items.Clear();
            foreach (GeometryMaterialLibrary.Entry other in this.working)
            {
                if (!ReferenceEquals(other, entry) && !string.IsNullOrEmpty(other.Name))
                {
                    this.componentColumn.Items.Add(other.Name);
                }
            }
        }

        // ------------------------------------------------------------------
        // Правка
        // ------------------------------------------------------------------

        /// <summary>
        /// Снять с полей то, что там набрано, в выбранное вещество.
        /// <paramref name="relist"/> — перебрать ли список: имя, сокращение и вид
        /// меняют его строку, а плотность с формулой — нет, и перебор на каждую
        /// цифру плотности уводил бы фокус из поля.
        /// </summary>
        void FieldChanged(bool relist)
        {
            if (this.loading)
            {
                return;
            }

            GeometryMaterialLibrary.Entry entry = this.Selected();
            if (entry == null)
            {
                return;
            }

            entry.Abbr = this.abbrBox.Text.Trim();
            entry.Name = this.nameBox.Text.Trim();
            entry.Kind = this.kindCombo.SelectedIndex >= 0
                ? (GeometryMaterialLibrary.MaterialKind)this.kindCombo.SelectedIndex
                : entry.Kind;

            double density;
            entry.Density = double.TryParse(this.densityBox.Text.Trim(), NumberStyles.Float,
                                            CultureInfo.InvariantCulture, out density)
                ? density : 0.0;

            // Доли из таблицы правке не подлежат — правится всё остальное (имя,
            // вид, плотность). А уход с таблицы на формулу или смесь есть ОТКАЗ
            // от ввезённых долей, и он обязан быть ВИДЕН: состав внизу
            // пересчитается сразу.
            if (this.tableRadio.Checked)
            {
                if (relist)
                {
                    this.RefreshList(entry);
                    return;
                }

                this.ShowComposition();
                return;
            }

            entry.ElementFractions.Clear();

            if (this.mixtureRadio.Checked)
            {
                entry.Formula = "";
                entry.Components.Clear();
                foreach (DataGridViewRow row in this.componentsGrid.Rows)
                {
                    if (row.IsNewRow)
                    {
                        continue;
                    }

                    string name = row.Cells[0].Value as string;
                    double weight;
                    if (!string.IsNullOrEmpty(name)
                        && double.TryParse(Convert.ToString(row.Cells[1].Value, CultureInfo.InvariantCulture),
                                           NumberStyles.Float, CultureInfo.InvariantCulture, out weight))
                    {
                        entry.Components.Add(new GeometryMaterialComponent
                        {
                            Material = name,
                            Weight = weight,
                        });
                    }
                }
            }
            else
            {
                entry.Components.Clear();
                entry.Formula = this.formulaBox.Text.Trim();
            }

            if (relist)
            {
                this.RefreshList(entry);
                return;
            }

            this.ShowComposition();
        }

        void SourceKindChanged(object sender, EventArgs e)
        {
            this.formulaPanel.Visible = this.formulaRadio.Checked;
            this.mixturePanel.Visible = this.mixtureRadio.Checked;
            this.FieldChanged(false);
        }

        /// <summary>
        /// Состав, который получится, — и словами то, чем он плох. Считается на
        /// каждую букву: это единственная видимая проверка формулы.
        /// </summary>
        void ShowComposition()
        {
            GeometryMaterialLibrary.Entry entry = this.Selected();
            if (entry == null)
            {
                this.compositionLabel.Text = "";
                this.problemLabel.Text = "";
                return;
            }

            GeometryMaterial material = GeometryMaterialLibrary.Make(entry, entry.Density, this.Lookup);
            this.compositionLabel.Text = GeometryMaterialLibrary.Describe(material);
            this.problemLabel.Text = this.Problem(entry) ?? "";
        }

        GeometryMaterialLibrary.Entry Lookup(string name)
        {
            foreach (GeometryMaterialLibrary.Entry entry in this.working)
            {
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>Что не так с веществом, словами. null — всё в порядке.</summary>
        string Problem(GeometryMaterialLibrary.Entry entry)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                return Resources.GeometryMaterialsErrorNoName;
            }

            int same = 0;
            foreach (GeometryMaterialLibrary.Entry other in this.working)
            {
                if (string.Equals(other.Name, entry.Name, StringComparison.OrdinalIgnoreCase))
                {
                    same++;
                }
            }

            if (same > 1)
            {
                // Имя — ключ: по нему ищутся составляющие смесей и по нему
                // вещество узнаётся при чтении файла геометрии.
                return string.Format(CultureInfo.CurrentCulture,
                                     Resources.GeometryMaterialsErrorDuplicate, entry.Name);
            }

            if (!(entry.Density > 0.0))
            {
                return Resources.GeometryMaterialsErrorDensity;
            }

            if (GeometryMaterialLibrary.HasCycle(entry, this.Lookup))
            {
                return Resources.GeometryMaterialsErrorCycle;
            }

            if (entry.IsMixture)
            {
                foreach (GeometryMaterialComponent component in entry.Components)
                {
                    if (this.Lookup(component.Material) == null)
                    {
                        return string.Format(CultureInfo.CurrentCulture,
                                             Resources.GeometryMaterialsErrorNoComponent,
                                             component.Material);
                    }
                }
            }

            GeometryMaterial material = GeometryMaterialLibrary.Make(entry, entry.Density, this.Lookup);
            if (material.Fractions.Count == 0)
            {
                return entry.IsMixture
                    ? Resources.GeometryMaterialsErrorEmptyMixture
                    : Resources.GeometryMaterialsErrorFormula;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Кнопки
        // ------------------------------------------------------------------

        void AddClicked(object sender, EventArgs e)
        {
            GeometryMaterialLibrary.MaterialKind kind = this.filterCombo.SelectedIndex > 0
                ? (GeometryMaterialLibrary.MaterialKind)(this.filterCombo.SelectedIndex - 1)
                : GeometryMaterialLibrary.MaterialKind.Source;

            GeometryMaterialLibrary.Entry entry = new GeometryMaterialLibrary.Entry
            {
                Name = Unique(Resources.GeometryMaterialsNewName),
                Abbr = "",
                Formula = "",
                Density = 1.0,
                Kind = kind,
            };

            this.working.Add(entry);
            this.RefreshList(entry);
            this.nameBox.Focus();
            this.nameBox.SelectAll();
        }

        string Unique(string wanted)
        {
            string name = wanted;
            int n = 2;
            while (this.Lookup(name) != null)
            {
                name = wanted + " " + n.ToString(CultureInfo.InvariantCulture);
                n++;
            }

            return name;
        }

        void RemoveClicked(object sender, EventArgs e)
        {
            GeometryMaterialLibrary.Entry entry = this.Selected();
            if (entry == null)
            {
                return;
            }

            // Вещество, входящее в чью-то смесь, молча удалять нельзя: смесь
            // после этого посчитается БЕЗ него — то есть неверно, и никакой
            // ошибки при этом не будет.
            List<string> users = new List<string>();
            foreach (GeometryMaterialLibrary.Entry other in this.working)
            {
                if (ReferenceEquals(other, entry))
                {
                    continue;
                }

                foreach (GeometryMaterialComponent component in other.Components)
                {
                    if (string.Equals(component.Material, entry.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        users.Add(other.Name);
                        break;
                    }
                }
            }

            if (users.Count > 0)
            {
                MessageBox.Show(this,
                    string.Format(CultureInfo.CurrentCulture, Resources.GeometryMaterialsErrorInUse,
                                  entry.Name, string.Join(", ", users.ToArray())),
                    Resources.GeometryMaterialsTitle, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            if (MessageBox.Show(this,
                    string.Format(CultureInfo.CurrentCulture, Resources.GeometryMaterialsRemoveAsk, entry.Name),
                    Resources.GeometryMaterialsTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                != DialogResult.Yes)
            {
                return;
            }

            this.working.Remove(entry);
            this.RefreshList(null);
        }

        void SaveClicked(object sender, EventArgs e)
        {
            // Проверяются ВСЕ вещества, а не показанное: испортить можно любое,
            // а увидеть — только то, что открыто.
            foreach (GeometryMaterialLibrary.Entry entry in this.working)
            {
                string problem = this.Problem(entry);
                if (problem == null)
                {
                    continue;
                }

                this.loading = true;
                try
                {
                    this.filterCombo.SelectedIndex = 0;
                }
                finally
                {
                    this.loading = false;
                }

                this.RefreshList(entry);
                // ⛔ `T106`, довод — как у второго такого места ниже.
                AppUi.Report(
                    string.Format(CultureInfo.CurrentCulture, Resources.GeometryMaterialsErrorAt,
                                  entry.Name, problem),
                    Resources.GeometryMaterialsTitle, MessageBoxIcon.Exclamation);
                return;
            }

            try
            {
                GeometryMaterialStore.Save(this.working);
            }
            catch (Exception error)
            {
                // ⛔ `T106`. `SaveClicked` зовёт отражением `MaterialLibraryProbe`
                // (`Invoke(form, "SaveClicked", …)`), то есть путь безоконный
                // (`S100`). Дверь `AppUi` в окне ведёт себя как прежний
                // `MessageBox`, а в прогоне отдаёт отказ вместо зависания.
                AppUi.Report(
                    string.Format(CultureInfo.CurrentCulture, Resources.GeometryMaterialsSaveFailed,
                                  GeometryMaterialStore.FilePath, error.Message),
                    Resources.GeometryMaterialsTitle, MessageBoxIcon.Hand);
                return;
            }

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
