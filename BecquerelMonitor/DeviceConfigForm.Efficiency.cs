using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    /// <summary>
    /// Вкладка «Эффективность» конфигурации прибора.
    ///
    /// Кривая привязана к ПРИБОРУ И ГЕОМЕТРИИ: эффективность полного поглощения
    /// зависит от телесного угла и самопоглощения в пробе, поэтому один и тот же
    /// кристалл в маринелли, в банке и с точечным источником даёт три разные
    /// кривые. Отсюда список: конфигураций у прибора много, действует одна.
    ///
    /// Вкладка построена кодом, а не конструктором форм: строки берутся из общих
    /// ресурсов, где у них уже есть русская пара, и правка не требует трогать
    /// resx формы на четыре тысячи строк.
    /// </summary>
    public partial class DeviceConfigForm
    {
        TabPage efficiencyTabPage;
        ComboBox efficiencyCombo;
        GeometrySketch efficiencySketch;
        Label efficiencySummaryLabel;
        Panel efficiencyHeader;
        Button efficiencyNewButton, efficiencyEditButton, efficiencyRenameButton;
        Button efficiencyDuplicateButton, efficiencyDeleteButton, efficiencyMatrixButton;

        /// <summary>
        /// Собрать вкладку и вставить её СРАЗУ ЗА калибровкой энергии: кривая —
        /// это тоже градуировка прибора, и стоять ей рядом с остальными.
        /// </summary>
        void BuildEfficiencyTab()
        {
            this.efficiencyTabPage = new TabPage
            {
                Text = Resources.DeviceConfigEfficiencyTab,
                UseVisualStyleBackColor = true,
                Padding = new Padding(3),
            };

            // Ширина вкладки НЕ РАСТЯГИВАЕТСЯ: tabControl1 привязан Top|Bottom|
            // Right, то есть с окном меняется только высота, а страница всегда
            // 490 точек. Раскладка считается от этого числа: шесть кнопок в один
            // ряд не помещаются даже впритык, поэтому два ряда по три.
            const int Margin = 12;
            const int Width = 490 - 2 * Margin;   // 466
            const int Gap = 8;
            const int ButtonWidth = (Width - 2 * Gap) / 3;   // 150

            // Шапка и чертёж разложены доком, а не якорями. Якоря считают
            // растяжение от размера, который у страницы НА МОМЕНТ СБОРКИ ещё не
            // тот: она получает свои 490x599 позже, и растянутый на разницу
            // чертёж уезжал за край (768x1086 при поле 490x599). Док от
            // размера не зависит вовсе.
            this.efficiencyHeader = new Panel { Dock = DockStyle.Top, Height = 166 };
            Panel header = this.efficiencyHeader;

            int y = Margin;
            this.efficiencyNewButton = this.EfficiencyButton(
                Resources.EfficiencyTabNew, Margin, y, ButtonWidth);
            this.efficiencyEditButton = this.EfficiencyButton(
                Resources.EfficiencyTabEdit, Margin + ButtonWidth + Gap, y, ButtonWidth);
            this.efficiencyRenameButton = this.EfficiencyButton(
                Resources.EfficiencyTabRename, Margin + 2 * (ButtonWidth + Gap), y, ButtonWidth);

            y += 32;
            this.efficiencyDuplicateButton = this.EfficiencyButton(
                Resources.EfficiencyTabDuplicate, Margin, y, ButtonWidth);
            this.efficiencyDeleteButton = this.EfficiencyButton(
                Resources.EfficiencyTabDelete, Margin + ButtonWidth + Gap, y, ButtonWidth);
            this.efficiencyMatrixButton = this.EfficiencyButton(
                Resources.EfficiencyTabResponseMatrix, Margin + 2 * (ButtonWidth + Gap), y, ButtonWidth);

            this.efficiencyNewButton.Click += this.efficiencyNewButton_Click;
            this.efficiencyEditButton.Click += this.efficiencyEditButton_Click;
            this.efficiencyRenameButton.Click += this.efficiencyRenameButton_Click;
            this.efficiencyDuplicateButton.Click += this.efficiencyDuplicateButton_Click;
            this.efficiencyDeleteButton.Click += this.efficiencyDeleteButton_Click;
            this.efficiencyMatrixButton.Click += this.efficiencyMatrixButton_Click;

            // Подпись отдельной строкой над списком, а не слева от него:
            // «Конфигурация эффективности:» съедает треть ширины, и списку
            // остаётся меньше, чем нужно на имя файла кривой.
            y += 38;
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(Margin, y),
                Text = Resources.EfficiencyTabList,
            });

            y += 18;
            this.efficiencyCombo = new ComboBox
            {
                Location = new Point(Margin, y),
                Size = new Size(Width, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };

            this.efficiencyCombo.SelectedIndexChanged += this.efficiencyCombo_SelectedIndexChanged;
            header.Controls.Add(this.efficiencyCombo);

            y += 28;
            this.efficiencySummaryLabel = new Label
            {
                AutoSize = false,
                Location = new Point(Margin, y),
                Size = new Size(Width, 30),
                ForeColor = Color.DimGray,
            };

            header.Controls.Add(this.efficiencySummaryLabel);

            // Чертёж забирает всё, что осталось под шапкой: высота с окном
            // растёт, ширина нет.
            this.efficiencySketch = new GeometrySketch
            {
                Mode = GeometrySketch.SketchMode.Overview,
                Dock = DockStyle.Fill,
                Margin = new Padding(Margin),
            };

            Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Margin, 0, Margin, Margin) };
            body.Controls.Add(this.efficiencySketch);

            // Заполняющий добавляется ПЕРВЫМ, прижатый сверху — вторым: док
            // разбирается от старших индексов к младшим, и шапка обязана занять
            // своё место раньше, чем остаток отдадут чертежу.
            this.efficiencyTabPage.Controls.Add(body);
            this.efficiencyTabPage.Controls.Add(header);

            // Вставка именно за калибровкой энергии. Индекс ищется, а не пишется
            // числом: порядок вкладок в конструкторе меняют, и жёсткая четвёрка
            // однажды поставила бы вкладку не туда молча.
            //
            // Вставляется НЕ через TabPages.Insert. Он до создания дескриптора
            // окна кладёт страницу только в Controls, а в TabPages она не
            // попадает: получается семь контролов против шести вкладок, и
            // вкладки на форме нет вовсе. Ошибка тихая — ни исключения, ни
            // предупреждения, страница по всем признакам «создана и
            // родительская привязка есть».
            //
            // Add и Remove этим не страдают (ими же пользуется
            // HideTempcoTabPage), поэтому хвост снимается и возвращается на
            // место следом за новой вкладкой.
            int index = this.tabControl1.TabPages.IndexOf(this.tabPage2);
            if (index < 0)
            {
                this.tabControl1.TabPages.Add(this.efficiencyTabPage);
                return;
            }

            List<TabPage> tail = new List<TabPage>();
            for (int i = this.tabControl1.TabPages.Count - 1; i > index; i--)
            {
                tail.Insert(0, this.tabControl1.TabPages[i]);
                this.tabControl1.TabPages.RemoveAt(i);
            }

            this.tabControl1.TabPages.Add(this.efficiencyTabPage);
            foreach (TabPage page in tail)
            {
                this.tabControl1.TabPages.Add(page);
            }
        }

        Button EfficiencyButton(string text, int x, int y, int width)
        {
            Button button = new Button
            {
                Location = new Point(x, y),
                Size = new Size(width, 26),
                Text = text,
                UseVisualStyleBackColor = true,
            };

            this.efficiencyHeader.Controls.Add(button);
            return button;
        }

        // ------------------------------------------------------------------
        // Загрузка и сохранение
        // ------------------------------------------------------------------

        /// <summary>
        /// Наполнить список конфигурациями прибора и выбрать действующую.
        /// </summary>
        void LoadEfficiencyTab(DeviceConfigInfo config)
        {
            this.efficiencyCombo.Items.Clear();
            this.efficiencyCombo.Items.Add(Resources.EfficiencyTabNone);
            int selected = 0;
            if (config != null && config.EfficiencyConfigs != null)
            {
                foreach (EfficiencyConfigData item in config.EfficiencyConfigs)
                {
                    int i = this.efficiencyCombo.Items.Add(item);
                    if (item.Guid == config.ActiveEfficiencyGuid)
                    {
                        selected = i;
                    }
                }
            }

            this.efficiencyCombo.SelectedIndex = selected;
            this.UpdateEfficiencyView();
        }

        /// <summary>
        /// Из полей формы в конфигурацию попадает только ВЫБОР действующей:
        /// сам список правится кнопками сразу, на месте, потому что «создать» и
        /// «удалить» — это действия, а не редактируемые значения.
        /// </summary>
        void SaveEfficiencyTab(DeviceConfigInfo config)
        {
            if (config == null)
            {
                return;
            }

            EfficiencyConfigData selected = this.SelectedEfficiency();
            config.ActiveEfficiencyGuid = selected == null ? null : selected.Guid;
        }

        EfficiencyConfigData SelectedEfficiency()
        {
            return this.efficiencyCombo == null
                ? null
                : this.efficiencyCombo.SelectedItem as EfficiencyConfigData;
        }

        /// <summary>
        /// Перерисовать миниатюру и подпись под списком. Подпись обязана
        /// называть ровно то, что мешает: «конфигураций нет» и «кривая без
        /// геометрии» — разные беды с разным лечением.
        /// </summary>
        /// <summary>
        /// Матрица отклика для геометрии выбранной кривой. Форма работает с той
        /// же копией конфигурации, что и вкладка, поэтому геометрия в ней —
        /// ровно та, что человек видит в чертеже.
        /// </summary>
        void efficiencyMatrixButton_Click(object sender, EventArgs e)
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            if (config == null || !config.HasGeometry)
            {
                return;
            }

            using (ResponseMatrixForm form = new ResponseMatrixForm(config))
            {
                form.ShowDialog(this);
            }
        }

        void UpdateEfficiencyView()
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            bool has = this.activeDeviceConfig != null
                       && this.activeDeviceConfig.EfficiencyConfigs.Count > 0;

            // «Изменить» доступно и без геометрии: это единственный способ её
            // ДОПИСАТЬ. У кривой, восстановленной по измерениям, геометрии нет,
            // и запертая кнопка оставляла такую кривую навсегда непересчитываемой.
            this.efficiencyEditButton.Enabled = config != null;
            this.efficiencyRenameButton.Enabled = config != null;
            this.efficiencyDuplicateButton.Enabled = config != null;
            this.efficiencyDeleteButton.Enabled = config != null;
            // Матрица считается ИЗ ГЕОМЕТРИИ: у кривой, восстановленной по
            // измерениям, её нет, и считать не из чего.
            this.efficiencyMatrixButton.Enabled = config != null && config.HasGeometry;

            if (config == null)
            {
                this.efficiencySummaryLabel.Text = has ? "" : Resources.EfficiencyTabEmpty;
                this.efficiencySketch.SetModel(null);
                return;
            }

            List<string> parts = new List<string>();
            if (config.HasCurve)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, Resources.EfficiencyTabSummary,
                                        config.Curve.Count,
                                        (int)config.Curve[0].Energy,
                                        (int)config.Curve[config.Curve.Count - 1].Energy));
            }

            if (!config.HasGeometry)
            {
                parts.Add(Resources.EfficiencyTabNoGeometry);
            }

            this.efficiencySummaryLabel.Text = string.Join("   ", parts.ToArray());
            this.efficiencySketch.SetModel(config.Geometry);
        }

        void efficiencyCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.UpdateEfficiencyView();
            if (!this.contentsLoading)
            {
                this.SetActiveDeviceConfigDirty();
            }
        }

        // ------------------------------------------------------------------
        // Кнопки списка
        // ------------------------------------------------------------------

        /// <summary>
        /// Создать конфигурацию: имя спрашивается здесь, содержимое — в
        /// конструкторе кривой. Пустая конфигурация заводится сразу, до того
        /// как в ней что-нибудь появится: конструктору нужно, во что писать.
        /// </summary>
        void efficiencyNewButton_Click(object sender, EventArgs e)
        {
            if (this.activeDeviceConfig == null)
            {
                return;
            }

            string name = AskName(this, Resources.EfficiencyTabRenameTitle,
                                  Resources.EfficiencyTabNewName);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            EfficiencyConfigData config = new EfficiencyConfigData(name)
            {
                Origin = EfficiencyOrigin.Measurement,
            };

            this.activeDeviceConfig.EfficiencyConfigs.Add(config);
            this.RefreshEfficiencyList(config.Guid);
            this.SetActiveDeviceConfigDirty();
            this.OpenEfficiencyMaker(config);
        }

        /// <summary>
        /// Изменить выбранную. По существу это правка её геометрии — и правка
        /// в том числе ОТСУТСТВУЮЩЕЙ: у кривой без геометрии конструктор
        /// открывается на заготовке, чтобы её было где дописать.
        /// </summary>
        void efficiencyEditButton_Click(object sender, EventArgs e)
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            if (config == null)
            {
                return;
            }

            this.OpenEfficiencyMaker(config);
        }

        // Открытые конструкторы кривой. Форма прибора правит КЛОН конфигурации,
        // и при смене строки списка (или отказе от сохранения) клон выбрасывается —
        // привязанный к нему конструктор писал бы своё «Сохранить» в объект,
        // до которого больше никому нет дела. Такие конструкторы закрываются
        // вместе с клоном (см. CloseEfficiencyMakers).
        readonly List<EfficiencyMakerForm> openEfficiencyMakers = new List<EfficiencyMakerForm>();

        /// <summary>
        /// Закрыть конструкторы, привязанные к выбрасываемому клону конфигурации.
        /// Молчаливая альтернатива хуже: окно оставалось бы живым, а его
        /// «Сохранить» уходило бы в сироту — часы монте-карло пропадали бы без
        /// единого признака.
        /// </summary>
        void CloseEfficiencyMakers()
        {
            foreach (EfficiencyMakerForm maker in this.openEfficiencyMakers.ToArray())
            {
                maker.Close();
            }

            this.openEfficiencyMakers.Clear();
        }

        /// <summary>
        /// Открыть конструктор кривой для этой конфигурации. Окно немодальное —
        /// прогон по пачке спектров долгий, и держать за него конфигурацию
        /// прибора нельзя; правит оно тот же объект, что лежит в списке.
        /// </summary>
        void OpenEfficiencyMaker(EfficiencyConfigData config)
        {
            EfficiencyMakerForm maker = new EfficiencyMakerForm();
            maker.BindTo(this.activeDeviceConfig, config);

            // Обновляться надо по ЗАКРЫТИЮ конструктора, а не сразу после Show:
            // окно немодальное, и сохранение случится когда-то потом. Без этой
            // подписки вкладка сразу после «Сохранить» показывала «кривая без
            // геометрии», держала «Изменить» недоступной и рисовала пустой
            // эскиз — при том что и кривая, и геометрия уже лежали в
            // конфигурации. Обманывал только вид, и заметить это можно было
            // единственным способом: переключить список туда и обратно.
            DeviceConfigInfo boundDevice = this.activeDeviceConfig;
            maker.FormClosed += delegate
            {
                if (this.IsDisposed)
                {
                    return;
                }

                this.openEfficiencyMakers.Remove(maker);

                // Клон, к которому был привязан конструктор, уже заменён
                // (смена строки списка): обновлять вкладку не по чему, а
                // дирти-флажок относился бы к ЧУЖОЙ конфигурации.
                if (!object.ReferenceEquals(boundDevice, this.activeDeviceConfig))
                {
                    return;
                }

                this.RefreshEfficiencyList(config.Guid);
                this.SetActiveDeviceConfigDirty();
            };

            this.openEfficiencyMakers.Add(maker);
            maker.Show(this);
        }

        void efficiencyRenameButton_Click(object sender, EventArgs e)
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            if (config == null)
            {
                return;
            }

            string name = AskName(this, Resources.EfficiencyTabRenameTitle, config.Name);
            if (string.IsNullOrEmpty(name) || name == config.Name)
            {
                return;
            }

            config.Name = name;
            config.LastUpdated = DateTime.Now;
            this.RefreshEfficiencyList(config.Guid);
            this.SetActiveDeviceConfigDirty();
        }

        void efficiencyDuplicateButton_Click(object sender, EventArgs e)
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            if (config == null || this.activeDeviceConfig == null)
            {
                return;
            }

            EfficiencyConfigData copy = config.Duplicate(
                string.Format(Resources.EfficiencyTabCopySuffix, config.Name));
            this.activeDeviceConfig.EfficiencyConfigs.Add(copy);
            this.RefreshEfficiencyList(copy.Guid);
            this.SetActiveDeviceConfigDirty();
        }

        void efficiencyDeleteButton_Click(object sender, EventArgs e)
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            if (config == null || this.activeDeviceConfig == null)
            {
                return;
            }

            DialogResult answer = MessageBox.Show(this,
                string.Format(Resources.EfficiencyTabDeleteConfirm, config.Name),
                Resources.ConfirmationDialogTitle,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
            if (answer != DialogResult.OK)
            {
                return;
            }

            this.activeDeviceConfig.EfficiencyConfigs.Remove(config);
            if (this.activeDeviceConfig.ActiveEfficiencyGuid == config.Guid)
            {
                this.activeDeviceConfig.ActiveEfficiencyGuid = null;
            }

            this.RefreshEfficiencyList(this.activeDeviceConfig.ActiveEfficiencyGuid);
            this.SetActiveDeviceConfigDirty();
        }

        void RefreshEfficiencyList(string selectGuid)
        {
            bool wasLoading = this.contentsLoading;
            this.contentsLoading = true;
            try
            {
                this.efficiencyCombo.Items.Clear();
                this.efficiencyCombo.Items.Add(Resources.EfficiencyTabNone);
                int selected = 0;
                if (this.activeDeviceConfig != null)
                {
                    foreach (EfficiencyConfigData item in this.activeDeviceConfig.EfficiencyConfigs)
                    {
                        int i = this.efficiencyCombo.Items.Add(item);
                        if (item.Guid == selectGuid)
                        {
                            selected = i;
                        }
                    }
                }

                this.efficiencyCombo.SelectedIndex = selected;
            }
            finally
            {
                this.contentsLoading = wasLoading;
            }

            this.UpdateEfficiencyView();
        }

        /// <summary>
        /// Однострочный ввод. Своё окошко, а не InputBox из VisualBasic: тянуть
        /// в проект целую сборку ради одного поля незачем, а её здесь нет.
        /// Пустая строка означает отказ.
        /// </summary>
        static string AskName(IWin32Window owner, string title, string current)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = title;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ClientSize = new Size(360, 96);
                dialog.Icon = Resources.becqmoni;

                TextBox box = new TextBox
                {
                    Location = new Point(12, 16),
                    Size = new Size(336, 20),
                    Text = current ?? "",
                };

                Button ok = new Button
                {
                    Location = new Point(180, 56),
                    Size = new Size(80, 26),
                    Text = Resources.GeometryEditorSave,
                    DialogResult = DialogResult.OK,
                };

                Button cancel = new Button
                {
                    Location = new Point(268, 56),
                    Size = new Size(80, 26),
                    Text = Resources.GeometryEditorCancel,
                    DialogResult = DialogResult.Cancel,
                };

                dialog.Controls.Add(box);
                dialog.Controls.Add(ok);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                box.SelectAll();

                return dialog.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : "";
            }
        }
    }
}
