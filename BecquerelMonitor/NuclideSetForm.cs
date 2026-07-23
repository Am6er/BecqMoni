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
        private const int NuclideAnchorColumnIndex = 3;
        private const int SetNameColumnIndex = 0;
        private const int SetHidePeaksColumnIndex = 1;

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
            row.Cells.Add(new Cell() { Checked = nuclideDefinition.IsAnchor });
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
            row.Cells.Add(new Cell(set.Name));
            row.Cells.Add(new Cell() { Checked = set.HideUnknownPeaks });
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
            this.UpdateTableNuclides();
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
            else if (e.Cell.Index == NuclideAnchorColumnIndex)
            {
                // Якорная линия library-fit: флаг глобальный для линии (не
                // пер-сетовый) — линия, выбранная якорем, обычно якорь во всех
                // сетах, где присутствует (например, Tl-208 2614.5).
                NuclideDefinition nuclideDefinition = this.tableModelNuclides.Rows[e.Row].Tag as NuclideDefinition;
                if (nuclideDefinition != null)
                {
                    nuclideDefinition.IsAnchor = !e.Cell.Checked;
                    this.MarkAsDirty();
                }
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
                foreach (NuclideDefinition nuclide in this.nuclideManager.NuclideDefinitions)
                {
                    nuclide.Sets.Remove(this.selectedSet.Id);
                }

                this.tableSets.SuspendLayout();
                this.tableModelSets.Rows.RemoveAt(indexToRemove);
                this.tableSets.ResumeLayout();

                this.selectedSet = null;
                this.MarkAsDirty();
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

            this.mainForm.RefresNuclideSetList();
        }

        private void tableSets_EditingStopped(object sender, XPTable.Events.CellEditEventArgs e)
        {
            if (this.selectedSet == null)
            {
                return;
            }

            if (e.Cell.Index == SetNameColumnIndex)
            {
                this.selectedSet.Name = e.Cell.Text;
                this.MarkAsDirty();
            }
        }

        private void tableSets_CellCheckChanged(object sender, XPTable.Events.CellCheckBoxEventArgs e)
        {
            if (this.selectedSet == null)
            {
                return;
            }

            if (e.Cell.Index == SetHidePeaksColumnIndex)
            {
                this.selectedSet.HideUnknownPeaks = e.Cell.Checked;
                this.MarkAsDirty();
            }
        }

        private void textBoxFilter_TextChanged(object sender, EventArgs e)
        {
            this.UpdateTableNuclides();
        }
    }
}
