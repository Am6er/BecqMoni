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

        bool dirty = false;
        NuclideSet selectedSet = null;
        NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
        MainForm mainForm;

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
                    Row row = new Row();
                    bool included = nuclideDefinition.Sets.Contains(this.selectedSet.Id);
                    row.Cells.Add(new Cell() { Checked = included });
                    row.Cells.Add(new Cell(nuclideDefinition.Name));
                    row.Cells.Add(new Cell(nuclideDefinition.Energy.ToString(), nuclideDefinition.Energy));
                    this.tableModelNuclides.Rows.Add(row);
                }
            }

            this.tableNuclides.ResumeLayout();
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
                this.UpdateNuclideDefinition(e.Row, include);
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

            for (int i = 0; i < this.nuclideManager.NuclideDefinitions.Count; i++)
            {
                bool include = checkCol.Text == "X";
                Row row = this.tableModelNuclides.Rows[i];
                row.Cells[NuclideCheckboxColumnIndex].Checked = include;
                this.UpdateNuclideDefinition(i, include);
            }

            this.tableNuclides.ResumeLayout();
        }

        private void UpdateNuclideDefinition(int index, bool include)
        {
            if (this.selectedSet == null)
            {
                return;
            }

            NuclideDefinition def = this.nuclideManager.NuclideDefinitions[index];
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
    }
}
