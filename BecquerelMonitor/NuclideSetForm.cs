using BecquerelMonitor.Properties;
using System.Globalization;
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Windows.UI.Notifications;
using XPTable.Models;

namespace BecquerelMonitor
{
    public partial class NuclideSetForm : Form
    {
        private const int NuclideCheckboxColumnIndex = 0;
        private const int SetNameColumnIndex = 0;
        private const int SetHidePeaksColumnIndex = 1;
        private const int SetIntensityLinesColumnIndex = 2;

        bool dirty = false;
        /// <summary>(П326) За время окна правили хоть раз — пересчёт при закрытии; «Сохранить» его не гасит.</summary>
        bool changed = false;
        NuclideSet selectedSet = null;
        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
        MainForm mainForm;

        public NuclideSetForm()
        {
            InitializeComponent();
        }

        public NuclideSetForm(MainForm mainForm)
        {
            InitializeComponent();

            this.mainForm = mainForm;
            this.Icon = Resources.becqmoni;
            // (П316, Н1) Подпись пустой таблицы изотопов — из ресурса формы
            // («Выберите набор слева»); при выбранном наборе таблица пуста только
            // от фильтра, и подпись меняется на свою (UpdateTableNuclides).
            this.noSetSelectedText = this.tableNuclides.NoItemsText;
            this.TakeOpeningSnapshot();
            this.UpdateTableNuclides();
            this.RenderTableSets();
        }

        private void UpdateTableNuclides()
        {
            this.tableNuclides.SuspendLayout();
            this.tableModelNuclides.Rows.Clear();
            this.nuclideManager.NuclideDefinitions.Sort();
            if (this.noSetSelectedText != null)
            {
                this.tableNuclides.NoItemsText = this.selectedSet == null
                    ? this.noSetSelectedText
                    : Resources.NuclideSetNoMatchingNuclides;
            }

            if (this.selectedSet != null)
            {
                foreach (NuclideDefinition nuclideDefinition in this.nuclideManager.NuclideDefinitions)
                {
                    if (!string.IsNullOrWhiteSpace(this.textBoxFilter.Text)
                        && !string.IsNullOrWhiteSpace(nuclideDefinition.Name)
                        && !nuclideDefinition.Name.ToLowerInvariant().Contains(this.textBoxFilter.Text.ToLowerInvariant()))
                    {
                        continue;
                    }

                    Row row = CreateNuclideRow(nuclideDefinition, this.selectedSet.Id);
                    this.tableModelNuclides.Rows.Add(row);
                }
            }

            this.tableNuclides.ResumeLayout();
        }

        private Row CreateNuclideRow(NuclideDefinition nuclideDefinition, Guid selectedSetId)
        {
            Row row = new Row();
            bool included = nuclideDefinition.Sets.Contains(selectedSetId);
            row.Cells.Add(new Cell() { Checked = included });
            row.Cells.Add(new Cell(nuclideDefinition.Name));
            row.Cells.Add(new Cell(nuclideDefinition.Energy.ToString(CultureInfo.InvariantCulture), nuclideDefinition.Energy));
            row.Tag = nuclideDefinition;

            return row;
        }

        private void RenderTableSets()
        {
            this.tableSets.SuspendLayout();
            this.tableModelSets.Rows.Clear();

            foreach (NuclideSet nuclideSet in this.nuclideManager.NuclideSets)
            {
                Row row = this.CreateNuclideSetRow(nuclideSet);
                this.tableModelSets.Rows.Add(row);
            }

            this.tableSets.ResumeLayout();
        }

        private void buttonAddSet_Click(object sender, EventArgs e)
        {
            // (П316, Н3) Имя нового набора — из ресурсов: в русском окне стояло
            // английское «New set 6».
            NuclideSet set = new NuclideSet()
            {
                Id = Guid.NewGuid(),
                Name = string.Format(CultureInfo.InvariantCulture, Resources.NuclideSetNewName,
                                     this.tableModelSets.Rows.Count + 1)
            };

            this.tableSets.SuspendLayout();
            Row row = this.CreateNuclideSetRow(set);
            this.tableModelSets.Rows.Add(row);
            this.tableSets.ResumeLayout();

            this.nuclideManager.NuclideSets.Add(set);
            this.MarkAsDirty();
        }

        private Row CreateNuclideSetRow(NuclideSet set)
        {
            Row row = new Row();
            // Набор — в Tag строки: обработчики галок и правки имени должны
            // брать набор ИЗ СТРОКИ СОБЫТИЯ, а не из выделения — событие может
            // прийти по строке, которая выделенной не является.
            row.Tag = set;
            row.Cells.Add(new Cell(set.Name));
            row.Cells.Add(new Cell() { Checked = set.HideUnknownPeaks });
            row.Cells.Add(new Cell() { Checked = set.ShowIntensityLines });
            return row;
        }

        private void tableSets_SelectionChanged(object sender, XPTable.Events.SelectionEventArgs e)
        {
            if (e.NewSelectedIndicies.Length > 0)
            {
                int newIndex = e.NewSelectedIndicies[0];
                if (newIndex < this.nuclideManager.NuclideSets.Count)
                {
                    this.selectedSet = this.nuclideManager.NuclideSets[newIndex];
                }
                else
                {
                    this.selectedSet = null;
                }
            } 
            else
            {
                this.selectedSet = null;
            }

            this.buttonDeleteSet.Enabled = this.selectedSet != null;
            this.buttonAssignColor.Enabled = this.selectedSet != null;
            this.ShowSetColor();
            this.UpdateTableNuclides();
        }

        /// <summary>
        /// Показать в поле выбора цвет набора, если он у набора один. Разные
        /// цвета внутри набора оставляют поле как есть: подставить любой из них
        /// значило бы назвать его цветом набора, которым он не является.
        /// </summary>
        void ShowSetColor()
        {
            if (this.selectedSet == null)
            {
                return;
            }

            bool first = true;
            System.Drawing.Color common = System.Drawing.Color.Empty;
            foreach (NuclideDefinition nuclideDefinition in this.nuclideManager.NuclideDefinitions)
            {
                if (!nuclideDefinition.Sets.Contains(this.selectedSet.Id))
                {
                    continue;
                }

                System.Drawing.Color color = nuclideDefinition.NuclideColor.Color;
                if (first)
                {
                    common = color;
                    first = false;
                }
                else if (common != color)
                {
                    return;
                }
            }

            if (!first)
            {
                this.assignColorComboBox.SelectedColor = common;
            }
        }

        /// <summary>
        /// Покрасить все нуклиды выбранного набора в один цвет.
        ///
        /// Цвет хранится у НУКЛИДА (<see cref="NuclideDefinition.NuclideColor"/>),
        /// а не у набора: им же красятся пики и вертикальные линии интенсивностей,
        /// и второго источника цвета заводить не за чем. Набор здесь — способ
        /// выбрать, кого красить, разом: импортируемые из NucBase определения все
        /// получают зелёный, и в зелёной заливке спектра их линии не видно.
        /// </summary>
        void buttonAssignColor_Click(object sender, EventArgs e)
        {
            if (this.selectedSet == null)
            {
                return;
            }

            System.Drawing.Color color = this.assignColorComboBox.SelectedColor;
            int painted = 0;
            foreach (NuclideDefinition nuclideDefinition in this.nuclideManager.NuclideDefinitions)
            {
                if (!nuclideDefinition.Sets.Contains(this.selectedSet.Id))
                {
                    continue;
                }

                nuclideDefinition.NuclideColor.Color = color;
                painted++;
            }

            if (painted == 0)
            {
                // ⛔ `T106`. Обработчик зовёт отражением `SetColorProbe`
                // (`Click(form, "buttonAssignColor_Click")`) — безоконный
                // путь (`S100`).
                AppUi.Report(Resources.NuclideSetAssignColorEmpty,
                             Resources.ConfirmationDialogTitle,
                             MessageBoxIcon.Information);
                return;
            }

            this.MarkAsDirty();
            // Цветом красятся линии интенсивностей и подписи пиков — перерисовать
            // сразу, иначе действие выглядит несработавшим.
            this.RefreshActiveChart();
        }

        private void tableNuclides_CellClick(object sender, XPTable.Events.CellMouseEventArgs e)
        {
            if (this.selectedSet == null)
            {
                return;
            }

            if (e.Cell.Index == NuclideCheckboxColumnIndex)
            {
                bool include = !e.Cell.Checked;
                this.UpdateNuclideDefinition(this.tableModelNuclides.Rows[e.Row].Tag as NuclideDefinition, include);
            }
        }

        private void ToggleNuclideSelection()
        {
            if (this.selectedSet == null)
            {
                return;
            }

            this.tableNuclides.SuspendLayout();
            Column checkCol = this.columnModelNuclides.Columns[NuclideCheckboxColumnIndex];
            checkCol.Text = checkCol.Text == "X"
                ? ""
                : "X";

            // Итерируем строки ТАБЛИЦЫ, а не весь список нуклидов: при
            // активном фильтре строк меньше, чем определений, и Rows[i] за
            // пределами списка возвращает null (NRE). Заодно «выделить всё»
            // честно действует только на видимые (отфильтрованные) строки.
            bool includeAll = checkCol.Text == "X";
            for (int i = 0; i < this.tableModelNuclides.Rows.Count; i++)
            {
                Row row = this.tableModelNuclides.Rows[i];
                NuclideDefinition nuclideDefinition = row?.Tag as NuclideDefinition;
                if (nuclideDefinition == null)
                {
                    continue;
                }

                row.Cells[NuclideCheckboxColumnIndex].Checked = includeAll;
                this.UpdateNuclideDefinition(nuclideDefinition, includeAll);
            }

            this.tableNuclides.ResumeLayout();
        }

        private void UpdateNuclideDefinition(NuclideDefinition def, bool include)
        {
            if (include)
            {
                def.Sets.Add(this.selectedSet.Id);
            }
            else
            {
                def.Sets.Remove(this.selectedSet.Id);
            }

            this.MarkAsDirty();
        }

        private void tableNuclides_HeaderClick(object sender, XPTable.Events.HeaderMouseEventArgs e)
        {
            if (e.Index == NuclideCheckboxColumnIndex)
            {
                ToggleNuclideSelection();
            }
        }

        private void MarkAsDirty()
        {
            this.buttonSave.Enabled = true;
            this.dirty = true;
            this.changed = true;
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {
            // (`AMBER201`, мелочь 5.9, остаток `A9`, 05.10.2026) Ответ записи
            // читается: при отказе окно об ошибке показал менеджер, а пометка
            // «есть несохранённое» и кнопка остаются — прежде они гасли, и при
            // закрытии о правках наборов больше не спрашивали.
            if (!this.SaveLibrary())
            {
                return;
            }
            this.dirty = false;
            this.buttonSave.Enabled = false;
        }

        /// <summary>
        /// ⛔ (П326, 15.10.2026) Записать библиотеку — СПРОСИВ, если рядом открыт
        /// редактор нуклидов с несохранёнными правками. Решение Amber вопросником
        /// 15.10.2026, дословно: «Спросить перед записью (Рекомендую)».
        ///
        /// Библиотека и наборы лежат в одном файле, и
        /// <see cref="NuclideDefinitionManager.SaveDefinitionFile"/> пишет всё, что
        /// в памяти; редактор нуклидов немодален, его правки ячеек и галки
        /// «Видимый» живут в линиях до своего «Сохранить». Прежде «Сохранить»
        /// наборов уносил их в файл молча, а редактор нуклидов продолжал считать
        /// их несохранёнными (его «Нет» потом откатывал память, файл оставался с
        /// правкой). Ответы:
        ///  * «Да» — записать вместе: правки окна нуклидов записываются, как его
        ///    собственной кнопкой «Сохранить», и окно становится «сохранено»;
        ///  * «Нет» — записать только наборы: файл пишется с линиями ДО правок окна
        ///    нуклидов (<see cref="NuclideDefinitionForm.WriteWithoutUnsavedEdits"/>),
        ///    правки остаются в окне несохранёнными, его «Нет» вернёт память к файлу;
        ///  * «Отмена» — не писать ничего.
        /// Отказ любого пути — false, пометка «есть несохранённое» остаётся.
        /// Проба — <c>P326SetSaveAsksProbe</c>, журнал П326.
        /// </summary>
        bool SaveLibrary()
        {
            List<NuclideDefinitionForm> editors = NuclideDefinitionForm.OpenWithUnsavedEdits();
            if (editors.Count == 0)
            {
                return this.CommitWritten(this.nuclideManager.SaveDefinitionFile());
            }

            DialogResult answer = MessageBox.Show(this, Resources.MSGSavingNuclideSetUnsavedNuclides,
                                                  Resources.ConfirmationDialogTitle,
                                                  MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation);
            if (answer == DialogResult.Yes)
            {
                foreach (NuclideDefinitionForm editor in editors)
                {
                    if (!editor.SaveTogether())
                    {
                        return false;
                    }
                }
                return this.CommitWritten(true);
            }
            if (answer == DialogResult.No)
            {
                return this.CommitWritten(this.WriteWithoutEditorsEdits(editors, 0));
            }
            return false;
        }

        /// <summary>(П326) Запись «только наборы»: каждое окно нуклидов снимает свои правки на время записи.</summary>
        bool WriteWithoutEditorsEdits(List<NuclideDefinitionForm> editors, int index)
        {
            if (index == editors.Count)
            {
                return this.nuclideManager.SaveDefinitionFile();
            }
            return editors[index].WriteWithoutUnsavedEdits(() => this.WriteWithoutEditorsEdits(editors, index + 1));
        }

        /// <summary>
        /// (П326) Записано — снимок для «Нет» берётся заново. Прежде он брался
        /// один раз, на открытии окна, и «Нет» при закрытии после «Сохранить» и
        /// следующей правки откатывал в памяти и ЗАПИСАННОЕ: в файле правка была,
        /// в памяти — нет, и ближайшая запись библиотеки снимала её молча.
        /// </summary>
        bool CommitWritten(bool written)
        {
            if (written)
            {
                this.TakeOpeningSnapshot();
            }
            return written;
        }

        private void buttonDeleteSet_Click(object sender, EventArgs e)
        {
            if (this.selectedSet == null)
            {
                return;
            }

            int indexToRemove = this.nuclideManager.NuclideSets.IndexOf(this.selectedSet);
            if (indexToRemove > -1)
            {
                this.nuclideManager.NuclideSets.RemoveAt(indexToRemove);
                // Удалённый набор мог быть выбран для поиска пиков. Оставить на
                // него ссылку значило бы искать по набору, которого нет, и
                // прятать линии всех остальных: выбор непустой, а совпасть с ним
                // уже некому.
                if (this.nuclideManager.ActiveSet != null
                    && this.nuclideManager.ActiveSet.Id == this.selectedSet.Id)
                {
                    this.nuclideManager.ActiveSet = null;
                }

                foreach (NuclideDefinition nuclide in this.nuclideManager.NuclideDefinitions)
                {
                    nuclide.Sets.Remove(this.selectedSet.Id);
                }

                this.tableSets.SuspendLayout();
                this.tableModelSets.Rows.RemoveAt(indexToRemove);
                this.tableSets.ResumeLayout();

                this.selectedSet = null;
                this.MarkAsDirty();
                // Удалённый сет мог рисовать линии интенсивностей — убрать их
                // с графика сразу, как это делает и сама галка.
                this.RefreshActiveChart();
            }
        }

        private void buttonClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void NuclideSetForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.dirty)
            {
                DialogResult dialogResult = MessageBox.Show(Resources.MSGSavingNuclideSet, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
                if (dialogResult == DialogResult.Yes && !this.SaveLibrary())
                {
                    // (`AMBER201`, 5.9) Запись не удалась — окно остаётся открытым
                    // с правками, как у редактора нуклидов (`NuclideDefinitionForm`):
                    // закрыться значило бы потерять их молча. (П326) Так же и
                    // «Отмена» на вопрос о правках редактора нуклидов: не записано
                    // ничего, правки наборов целы.
                    e.Cancel = true;
                    return;
                }
                if (dialogResult == DialogResult.No)
                {
                    // (П316, Н2) «Нет» — правки этого окна снимаются. Прежде ветка
                    // ничего не делала: правки уже жили в объектах менеджера
                    // (подписи пиков и состав FSA их видели до перезапуска) и
                    // молча уходили в файл при следующей записи определений.
                    this.RollBackToOpening();
                }
            }

            // Конструктор без MainForm существует (его зовёт дизайнер форм и
            // проба, снимающая окно) — и на закрытии окна это падало NRE.
            if (this.mainForm != null)
            {
                this.mainForm.RefresNuclideSetList();

                // ⛔ (`A31`) СОСТАВ НАБОРА ПОМЕНЯЛСЯ — ПЕРЕСЧИТАТЬ. Прежде здесь
                // стоял только перебор выпадающего списка, и добавленный в набор
                // нуклид никуда не доезжал: подписи пиков ставит набор, состав
                // разложения идёт от подписей, а никто из них не пересчитывался,
                // пока человек не трогал что-нибудь ещё. Ровно то же делает
                // `NuclideDefinitionForm` после сохранения определений.
                //
                // ⚠ Условие — `dirty`, а не «всегда»: правки живут в объектах
                // менеджера с той минуты, как их внесли, поэтому пересчитывать
                // надо и когда человек отказался писать их в файл; а вот
                // открытое и закрытое без правок окно дёргать поиск пиков не
                // должно.
                //
                // (П326) Признак — `changed` («за время окна правили»), а не
                // `dirty` («есть незаписанное»): «Сохранить» гасит `dirty`, и
                // «Сохранить» → «Закрыть» уходило без пересчёта — ровно то, что
                // чинила `A31`.
                if (this.changed)
                {
                    this.mainForm.UpdateDetectedPeakView();
                    if (this.mainForm.ActiveDocument != null)
                    {
                        this.mainForm.ActiveDocument.UpdateEnergySpectrum();
                    }
                }
            }
        }

        /// <summary>
        /// Набор строки события — из её Tag. Писать в <see cref="selectedSet"/>
        /// нельзя: XPTable может поднять событие ячейки до смены выделения, и
        /// значение легло бы на чужой набор.
        /// </summary>
        private NuclideSet SetOfRow(int rowIndex)
        {
            return rowIndex >= 0 && rowIndex < this.tableModelSets.Rows.Count
                ? this.tableModelSets.Rows[rowIndex].Tag as NuclideSet
                : null;
        }

        private void tableSets_EditingStopped(object sender, XPTable.Events.CellEditEventArgs e)
        {
            NuclideSet set = this.SetOfRow(e.Row);
            if (set == null)
            {
                return;
            }

            if (e.Cell.Index == SetNameColumnIndex)
            {
                // (П316, Н4) Пустое или пробельное имя не принимается: в ячейку
                // возвращается прежнее. Набор без имени уходил в выпадающий список
                // «Набор:» панели поиска пиков пустой строкой и в файл.
                if (string.IsNullOrWhiteSpace(e.Cell.Text))
                {
                    e.Cell.Text = set.Name;
                    return;
                }
                set.Name = e.Cell.Text;
                this.MarkAsDirty();
            }
        }

        private void tableSets_CellCheckChanged(object sender, XPTable.Events.CellCheckBoxEventArgs e)
        {
            NuclideSet set = this.SetOfRow(e.Row);
            if (set == null)
            {
                return;
            }

            if (e.Cell.Index == SetHidePeaksColumnIndex)
            {
                set.HideUnknownPeaks = e.Cell.Checked;
                this.MarkAsDirty();
            }
            else if (e.Cell.Index == SetIntensityLinesColumnIndex)
            {
                set.ShowIntensityLines = e.Cell.Checked;
                this.MarkAsDirty();
                // График перерисовывается сразу: окно стоит рядом со спектром
                // (перерисовка доходит и под модальным диалогом), и галка,
                // действующая только после закрытия, читалась бы как
                // неработающая.
                this.RefreshActiveChart();
            }
        }

        /// <summary>
        /// Перерисовать спектр открытого документа. Набор, галку которого
        /// щёлкнули, может быть и не выбран в панели поиска пиков — тогда на
        /// картинке ничего не изменится, и это правильно.
        /// </summary>
        private void RefreshActiveChart()
        {
            if (this.mainForm != null && this.mainForm.ActiveDocument != null)
            {
                this.mainForm.ActiveDocument.EnergySpectrumView.Invalidate();
            }
        }

        private void textBoxFilter_TextChanged(object sender, EventArgs e)
        {
            this.UpdateTableNuclides();
        }

        /// <summary>
        /// (П316, Н20) Esc закрывает окно, как «Закрыть». Не через
        /// <see cref="Form.CancelButton"/>: Esc во время правки имени набора
        /// обязан доехать до редактора ячейки XPTable (он отменяет правку по
        /// символу Esc, `TextCellEditor.OnKeyPress`), а кнопка отмены формы
        /// перехватила бы клавишу раньше — правка вместо отмены записалась бы
        /// уходом фокуса и окно начало бы закрываться.
        /// </summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape && !this.tableSets.IsEditing && !this.tableNuclides.IsEditing)
            {
                this.Close();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        /// <summary>
        /// (П316, Н2) Состояние на открытии окна: всё, что окно умеет менять.
        /// Наборы — сами объекты и их поля (на объекты по ссылке держатся
        /// документы, <see cref="DocEnergySpectrum.SelectedNuclideSet"/>, и
        /// выбор панели <see cref="NuclideDefinitionManager.ActiveSet"/>),
        /// у линий — членство в наборах и цвет.
        ///
        /// ⚠ Откат — этим снимком, а НЕ перечитыванием файла, как у редактора
        /// нуклидов (`AMBER196`), и это не вкус. Перечитывание подменяет ВСЕ
        /// объекты менеджера: (1) документы и панель держат набор по ссылке
        /// (`NuclideSets.IndexOf`), и выбор набора у каждого открытого спектра
        /// молча слетел бы на «все нуклиды»; (2) немодальный редактор нуклидов,
        /// открытый рядом со своими несохранёнными правками, потерял бы их, а его
        /// строки остались бы на объектах, которых в менеджере больше нет.
        /// «Нет» здесь значит «снять правки ЭТОГО окна», и снимок делает ровно это.
        ///
        /// (П326) Берётся заново после каждой удачной записи (<see cref="CommitWritten"/>):
        /// «Нет» снимает только НЕЗАПИСАННЫЕ правки.
        /// </summary>
        void TakeOpeningSnapshot()
        {
            this.openedSets = new List<OpenedSet>();
            foreach (NuclideSet set in this.nuclideManager.NuclideSets)
            {
                this.openedSets.Add(new OpenedSet
                {
                    Set = set,
                    Name = set.Name,
                    HideUnknownPeaks = set.HideUnknownPeaks,
                    ShowIntensityLines = set.ShowIntensityLines,
                });
            }
            this.openedLines = new List<OpenedLine>();
            foreach (NuclideDefinition definition in this.nuclideManager.NuclideDefinitions)
            {
                this.openedLines.Add(new OpenedLine
                {
                    Definition = definition,
                    Sets = definition.Sets != null ? new List<Guid>(definition.Sets) : null,
                    HasColor = definition.NuclideColor != null,
                    Color = definition.NuclideColor != null ? definition.NuclideColor.Color : System.Drawing.Color.Empty,
                });
            }
            this.openedActiveSet = this.nuclideManager.ActiveSet;
        }

        /// <summary>
        /// (П316, Н2) Вернуть состояние на открытии окна или последней записи (<see cref="TakeOpeningSnapshot"/>):
        /// удалённые наборы — те же объекты на прежних местах, добавленные —
        /// прочь, имена и галки — прежние; членство линий и цвета — прежние;
        /// выбор панели поиска пиков — прежний (удаление набора его снимало).
        /// </summary>
        void RollBackToOpening()
        {
            if (this.openedSets == null)
            {
                return;
            }
            List<NuclideSet> sets = this.nuclideManager.NuclideSets;
            sets.Clear();
            foreach (OpenedSet opened in this.openedSets)
            {
                opened.Set.Name = opened.Name;
                opened.Set.HideUnknownPeaks = opened.HideUnknownPeaks;
                opened.Set.ShowIntensityLines = opened.ShowIntensityLines;
                sets.Add(opened.Set);
            }
            foreach (OpenedLine line in this.openedLines)
            {
                if (line.Sets != null && line.Definition.Sets != null)
                {
                    line.Definition.Sets.Clear();
                    line.Definition.Sets.UnionWith(line.Sets);
                }
                if (line.HasColor && line.Definition.NuclideColor != null)
                {
                    line.Definition.NuclideColor.Color = line.Color;
                }
            }
            this.nuclideManager.ActiveSet = this.openedActiveSet;
            this.selectedSet = null;
            this.RefreshActiveChart();
        }

        sealed class OpenedSet
        {
            public NuclideSet Set;
            public string Name;
            public bool HideUnknownPeaks;
            public bool ShowIntensityLines;
        }

        sealed class OpenedLine
        {
            public NuclideDefinition Definition;
            public List<Guid> Sets;
            public bool HasColor;
            public System.Drawing.Color Color;
        }

        List<OpenedSet> openedSets;
        List<OpenedLine> openedLines;
        NuclideSet openedActiveSet;

        /// <summary>(П316, Н1) Подпись пустой таблицы изотопов из ресурса формы — «набор не выбран».</summary>
        string noSetSelectedText;
    }
}
