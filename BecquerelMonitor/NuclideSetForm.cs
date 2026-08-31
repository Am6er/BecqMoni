using BecquerelMonitor.Properties;
using System;
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
            this.UpdateTableNuclides();
            this.RenderTableSets();
        }

        private void UpdateTableNuclides()
        {
            this.tableNuclides.SuspendLayout();
            this.tableModelNuclides.Rows.Clear();
            this.nuclideManager.NuclideDefinitions.Sort();
            
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
            row.Cells.Add(new Cell(nuclideDefinition.Energy.ToString(), nuclideDefinition.Energy));
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
            NuclideSet set = new NuclideSet()
            {
                Id = Guid.NewGuid(),
                Name = $"New set {this.tableModelSets.Rows.Count + 1}"
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
        /// выбрать, кого красить, разом: ввозимые из NucBase определения все
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
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {
            this.nuclideManager.SaveDefinitionFile();
            this.dirty = false;
            this.buttonSave.Enabled = false;
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
                if (dialogResult == DialogResult.Yes)
                {
                    this.nuclideManager.SaveDefinitionFile();
                }
            }

            // Конструктор без MainForm существует (его зовёт дизайнер форм и
            // проба, снимающая окно) — и на закрытии окна это падало NRE.
            if (this.mainForm != null)
            {
                this.mainForm.RefresNuclideSetList();
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
    }
}
