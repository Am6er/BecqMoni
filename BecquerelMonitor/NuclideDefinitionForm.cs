using BecquerelMonitor.Properties;
using System;
using System.Windows.Forms;
using XPTable.Events;
using XPTable.Models;

namespace BecquerelMonitor
{
    // Token: 0x02000017 RID: 23
    public partial class NuclideDefinitionForm : Form
    {
        // Token: 0x060000C2 RID: 194 RVA: 0x00003DA4 File Offset: 0x00001FA4
        public NuclideDefinitionForm()
        {
            this.InitializeComponent();
            this.button6.Enabled = false;
            this.DisableForm();
        }

        // Token: 0x060000C3 RID: 195 RVA: 0x00003DD0 File Offset: 0x00001FD0
        void NuclideDefinitionForm_Load(object sender, EventArgs e)
        {
            this.ListupNuclideDefinitions();
        }

        // Token: 0x060000C4 RID: 196 RVA: 0x00003DD8 File Offset: 0x00001FD8
        void ListupNuclideDefinitions()
        {
            this.table1.SuspendLayout();
            this.tableModel1.Rows.Clear();
            this.tableModel1.Selections.Clear();
            this.manager.NuclideDefinitions.Sort();
            foreach (NuclideDefinition nuclideDefinition in this.manager.NuclideDefinitions)
            {
                Row row = new Row();
                row.Cells.Add(new Cell(nuclideDefinition.Name));
                row.Cells.Add(new Cell(nuclideDefinition.Energy.ToString(), nuclideDefinition.Energy));
                row.Cells.Add(new Cell(nuclideDefinition.HalfLife.ToString(), nuclideDefinition.HalfLife));
                row.Tag = nuclideDefinition;
                this.tableModel1.Rows.Add(row);
                if (this.activeNuclide != null && nuclideDefinition.Name == this.activeNuclide.Name)
                {
                    this.activeNuclide = nuclideDefinition;
                    this.tableModel1.Selections.AddCell(row.Index, 0);
                }
            }
            this.table1.ResumeLayout();
        }

        // Token: 0x060000C5 RID: 197 RVA: 0x00003F34 File Offset: 0x00002134
        void LoadFormContents(NuclideDefinition nuclide)
        {
            this.contentsLoading = true;
            this.textBox1.Text = nuclide.Name;
            this.doubleTextBox1.Text = nuclide.Energy.ToString();
            this.doubleTextBox2.Text = nuclide.HalfLife.ToString();
            this.textBox2.Text = nuclide.Note;
            this.colorComboBox1.SelectedColor = nuclide.NuclideColor.Color;
            this.colorComboBox1.Refresh();
            this.contentsLoading = false;
        }

        // Token: 0x060000C6 RID: 198 RVA: 0x00003FAC File Offset: 0x000021AC
        bool SaveFormContents(NuclideDefinition nuclide)
        {
            try
            {
                nuclide.Name = this.textBox1.Text;
                nuclide.Energy = this.doubleTextBox1.GetValue();
                nuclide.HalfLife = this.doubleTextBox2.GetValue();
                nuclide.Note = this.textBox2.Text;
                nuclide.NuclideColor.Color = this.colorComboBox1.SelectedColor;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        // Token: 0x060000C7 RID: 199 RVA: 0x00004024 File Offset: 0x00002224
        void button3_Click(object sender, EventArgs e)
        {
            NuclideDefinition item = new NuclideDefinition();
            this.manager.NuclideDefinitions.Add(item);
            this.manager.SaveDefinitionFile();
            this.activeNuclide = item;
            this.ListupNuclideDefinitions();
            this.UpdatePeakDetectionResult();
        }

        // Token: 0x060000C8 RID: 200 RVA: 0x0000406C File Offset: 0x0000226C
        void button4_Click(object sender, EventArgs e)
        {
            if (this.activeNuclide == null)
            {
                return;
            }
            DialogResult dialogResult = MessageBox.Show(string.Format(Resources.MSGDeleteNuclideDefinition, this.activeNuclide.Name), Resources.ConfirmationDialogTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
            if (dialogResult == DialogResult.OK)
            {
                this.manager.NuclideDefinitions.Remove(this.activeNuclide);
                this.manager.SaveDefinitionFile();
                if (this.manager.NuclideDefinitions.Count > 0)
                {
                    this.activeNuclide = this.manager.NuclideDefinitions[0];
                }
                this.ListupNuclideDefinitions();
                this.UpdatePeakDetectionResult();
            }
        }

        // Token: 0x060000C9 RID: 201 RVA: 0x00004110 File Offset: 0x00002310
        void EnableForm()
        {
            this.tabControl1.Enabled = true;
        }

        // Token: 0x060000CA RID: 202 RVA: 0x00004120 File Offset: 0x00002320
        void DisableForm()
        {
            this.tabControl1.Enabled = false;
        }

        // Token: 0x060000CB RID: 203 RVA: 0x00004130 File Offset: 0x00002330
        void table1_SelectionChanged(object sender, SelectionEventArgs e)
        {
            if (this.reenter)
            {
                return;
            }
            this.reenter = true;
            NuclideDefinition nuclideDefinition = null;
            Row row = null;
            if (this.table1.SelectedItems.Length > 0)
            {
                nuclideDefinition = (NuclideDefinition)this.table1.SelectedItems[0].Tag;
                row = this.table1.SelectedItems[0];
            }
            if (!this.ConfirmSaveNuclide())
            {
                this.ListupNuclideDefinitions();
                this.reenter = false;
                return;
            }
            if (nuclideDefinition != null)
            {
                this.activeNuclide = nuclideDefinition;
                this.tableModel1.Selections.Clear();
                this.tableModel1.Selections.AddCell(row.Index, 0);
                this.LoadFormContents(this.activeNuclide);
                this.button4.Enabled = true;
                this.EnableForm();
            }
            else
            {
                this.button4.Enabled = false;
                this.DisableForm();
            }
            this.reenter = false;
        }

        // Token: 0x060000CC RID: 204 RVA: 0x00004224 File Offset: 0x00002424
        bool ConfirmSaveNuclide()
        {
            if (this.activeNuclide != null && this.activeNuclide.Dirty)
            {
                DialogResult dialogResult = MessageBox.Show(Resources.MSGSavingNuclideDefinition, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
                if (dialogResult == DialogResult.Yes)
                {
                    this.SaveNuclideDefinitions();
                }
                this.ResetActiveNuclideDirty();
                this.ListupNuclideDefinitions();
            }
            return true;
        }

        // Token: 0x060000CD RID: 205 RVA: 0x00004280 File Offset: 0x00002480
        void button6_Click(object sender, EventArgs e)
        {
            if (this.activeNuclide == null)
            {
                return;
            }
            this.SaveNuclideDefinitions();
            this.ResetActiveNuclideDirty();
            this.ListupNuclideDefinitions();
        }

        // Token: 0x060000CE RID: 206 RVA: 0x000042A0 File Offset: 0x000024A0
        void SaveNuclideDefinitions()
        {
            if (!this.SaveFormContents(this.activeNuclide))
            {
                MessageBox.Show(Resources.ERRInvalidInputForm);
                return;
            }
            this.manager.SaveDefinitionFile();
            this.UpdatePeakDetectionResult();
        }

        // Token: 0x060000CF RID: 207 RVA: 0x000042D4 File Offset: 0x000024D4
        void UpdatePeakDetectionResult()
        {
            MainForm mainForm = (MainForm)base.Owner;
            mainForm.UpdateDetectedPeakView();
            if (mainForm.ActiveDocument != null)
            {
                mainForm.ActiveDocument.UpdateEnergySpectrum();
            }
        }

        // Token: 0x060000D0 RID: 208 RVA: 0x00004310 File Offset: 0x00002510
        void button5_Click(object sender, EventArgs e)
        {
            if (!this.ConfirmSaveNuclide())
            {
                return;
            }
            base.Close();
        }

        // Token: 0x060000D1 RID: 209 RVA: 0x00004324 File Offset: 0x00002524
        void SetActiveNuclideDirty()
        {
            if (this.contentsLoading)
            {
                return;
            }
            if (this.activeNuclide == null)
            {
                return;
            }
            this.activeNuclide.Dirty = true;
            this.button6.Enabled = true;
        }

        // Token: 0x060000D2 RID: 210 RVA: 0x00004358 File Offset: 0x00002558
        void ResetActiveNuclideDirty()
        {
            if (this.activeNuclide == null)
            {
                return;
            }
            this.activeNuclide.Dirty = false;
            this.button6.Enabled = false;
        }

        void colorComboBox1_ColorChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        // Token: 0x060000D3 RID: 211 RVA: 0x00004380 File Offset: 0x00002580
        void textBox1_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        // Token: 0x060000D4 RID: 212 RVA: 0x00004388 File Offset: 0x00002588
        void doubleTextBox1_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        // Token: 0x060000D5 RID: 213 RVA: 0x00004390 File Offset: 0x00002590
        void doubleTextBox2_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        // Token: 0x060000D6 RID: 214 RVA: 0x00004398 File Offset: 0x00002598
        void textBox2_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        // Token: 0x04000038 RID: 56
        NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();

        // Token: 0x04000039 RID: 57
        NuclideDefinition activeNuclide;

        // Token: 0x0400003A RID: 58
        bool contentsLoading;

        // Token: 0x0400003B RID: 59
        bool reenter;
    }
}
