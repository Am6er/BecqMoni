using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using ColorComboBox;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using XPTable.Events;
using XPTable.Models;

namespace BecquerelMonitor
{
    // Token: 0x020000BC RID: 188
    public partial class ROIConfigForm : Form
    {
        // Token: 0x17000287 RID: 647
        // (get) Token: 0x060008EF RID: 2287 RVA: 0x0003380C File Offset: 0x00031A0C
        // (set) Token: 0x060008F0 RID: 2288 RVA: 0x00033814 File Offset: 0x00031A14
        public ROIConfigData ActiveROIConfig
        {
            get
            {
                return this.activeROIConfig;
            }
            set
            {
                this.activeROIConfig = value;
            }
        }

        // Token: 0x060008F1 RID: 2289 RVA: 0x00033820 File Offset: 0x00031A20
        public ROIConfigForm()
        {
            this.InitializeComponent();
            base.Icon = Resources.becqmoni;
            this.comboBox1.Items.Clear();
            foreach (ROIPrimitiveDefinition roiprimitiveDefinition in ROIPrimitiveDefinition.Definitions)
            {
                this.comboBox1.Items.Add(roiprimitiveDefinition.Translation);
            }
            this.comboBox1.SelectedIndex = 0;
            this.panel1.BackColor = Color.FromArgb(212, 218, 212);
            this.button2.Enabled = false;
            this.button4.Enabled = false;
            this.buttonSave.Enabled = false;
            this.EnableForm(false);
            this.EnableROIDefinitionForm(false);
            this.SetupNuclideDefinitionList();
            int[] roiconfigListColumnSizes = this.globalConfigManager.GlobalConfig.ROIConfigListColumnSizes;
            for (int i = 0; i < this.columnModel3.Columns.Count; i++)
            {
                int columnsize = (roiconfigListColumnSizes.Length > i) ? roiconfigListColumnSizes[i] : 32;
                this.columnModel3.Columns[i].Width = ((columnsize > 32) ? columnsize : 32);
            }
        }

        // Token: 0x060008F2 RID: 2290 RVA: 0x00033988 File Offset: 0x00031B88
        void ROIConfigForm_Load(object sender, EventArgs e)
        {
            this.ListupConfigFiles();
        }

        // Token: 0x060008F3 RID: 2291 RVA: 0x00033990 File Offset: 0x00031B90
        void ROIConfigForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!this.ConfirmSaveROIConfig())
            {
                return;
            }
            this.globalConfigManager.GlobalConfig.ROIConfigFormWidth = base.Width;
            this.globalConfigManager.GlobalConfig.ROIConfigFormHeight = base.Height;
            int[] array = new int[this.columnModel3.Columns.Count];
            this.globalConfigManager.GlobalConfig.ROIConfigListColumnSizes = array;
            for (int i = 0; i < this.columnModel3.Columns.Count; i++)
            {
                array[i] = this.columnModel3.Columns[i].Width;
            }
        }

        // Token: 0x060008F4 RID: 2292 RVA: 0x00033A38 File Offset: 0x00031C38
        void ListupConfigFiles()
        {
            this.table3.SuspendLayout();
            this.tableModel3.Rows.Clear();
            this.tableModel3.Selections.Clear();
            foreach (ROIConfigData roiconfigData in this.manager.ROIConfigList)
            {
                ROIConfigData roiconfigData2 = roiconfigData.Clone();
                Row row = new Row();
                row.Cells.Add(new Cell(roiconfigData2.Name));
                row.Cells.Add(new Cell(roiconfigData2.LastUpdated.ToShortDateString() + " " + roiconfigData2.LastUpdated.ToLongTimeString()));
                if (roiconfigData2.HasEfficiency)
                {
                    row.Cells.Add(new Cell("", true));
                }
                row.Tag = roiconfigData2;
                this.tableModel3.Rows.Add(row);
                if (this.activeROIConfig != null && this.activeROIConfig.Guid == roiconfigData2.Guid)
                {
                    this.activeROIConfig = roiconfigData2;
                    this.tableModel3.Selections.AddCell(row.Index, 0);
                }
            }
            this.table3.ResumeLayout();
        }

        // Token: 0x060008F5 RID: 2293 RVA: 0x00033B84 File Offset: 0x00031D84
        void UpdateConfigFilesList()
        {
            foreach (object obj in this.tableModel3.Rows)
            {
                Row row = (Row)obj;
                ROIConfigData roiconfigData = (ROIConfigData)row.Tag;
                row.Cells[0].Text = roiconfigData.Name;
                row.Cells[1].Text = roiconfigData.LastUpdated.ToShortDateString() + " " + roiconfigData.LastUpdated.ToLongTimeString();
            }
        }

        // Token: 0x060008F6 RID: 2294 RVA: 0x00033C44 File Offset: 0x00031E44
        void button7_Click(object sender, EventArgs e)
        {
            if (!this.ConfirmSaveROIConfig())
            {
                return;
            }
            string text = this.AssignNewFilename();
            if (text == null)
            {
                return;
            }
            ROIConfigData config = this.manager.CreateConfig(text).Clone();
            this.activeROIConfig = config;
            this.LoadFormContents(config);
            this.ListupConfigFiles();
            this.textBox1.SelectAll();
            this.textBox1.Focus();
        }

        // Token: 0x060008F7 RID: 2295 RVA: 0x00033CAC File Offset: 0x00031EAC
        void button10_Click(object sender, EventArgs e)
        {
            if (!this.ConfirmSaveROIConfig())
            {
                return;
            }
            string filename = Path.GetFileNameWithoutExtension(this.activeROIConfig.Filename) + " (Copy).xml";
            ROIConfigData roiconfigData = this.manager.DuplicateConfig(this.activeROIConfig, filename);
            if (roiconfigData == null)
            {
                return;
            }
            this.activeROIConfig = roiconfigData;
            this.LoadFormContents(roiconfigData);
            this.ListupConfigFiles();
            this.textBox1.SelectAll();
            this.textBox1.Focus();
        }

        // Token: 0x060008F8 RID: 2296 RVA: 0x00033D2C File Offset: 0x00031F2C
        void button8_Click(object sender, EventArgs e)
        {
            if (this.activeROIConfig == null)
            {
                return;
            }
            DialogResult dialogResult = MessageBox.Show(string.Format(Resources.MSGDeleteROIConfig, this.activeROIConfig.Name), Resources.ConfirmationDialogTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
            if (dialogResult == DialogResult.OK)
            {
                this.manager.DeleteConfig(this.activeROIConfig);
                this.activeROIConfig = null;
                this.EnableForm(false);
                this.ListupConfigFiles();
            }
        }

        // Token: 0x060008F9 RID: 2297 RVA: 0x00033D98 File Offset: 0x00031F98
        bool ConfirmSaveROIConfig()
        {
            if (this.activeROIConfig != null && this.activeROIConfig.Dirty)
            {
                DialogResult dialogResult = MessageBox.Show(Resources.MSGConfirmSaveConfig, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
                if (dialogResult == DialogResult.Yes)
                {
                    this.SaveFormContents(this.activeROIConfig);
                    this.SaveROIDefinitionFormContents(this.activeROIDefinition);
                    if (!this.manager.SaveConfig(this.activeROIConfig))
                    {
                        MessageBox.Show(Resources.ERRDuplicateConfigName);
                        return false;
                    }
                }
                else
                {
                    this.activeROIConfig = this.manager.ROIConfigMap[this.activeROIConfig.Guid].Clone();
                }
                this.ResetActiveROIConfigDirty();
                this.ListupConfigFiles();
            }
            return true;
        }

        // Token: 0x060008FA RID: 2298 RVA: 0x00033E50 File Offset: 0x00032050
        void SetActiveROIConfigDirty()
        {
            if (this.contentsLoading)
            {
                return;
            }
            if (this.activeROIConfig == null)
            {
                return;
            }
            this.activeROIConfig.Dirty = true;
            this.buttonSave.Enabled = true;
        }

        // Token: 0x060008FB RID: 2299 RVA: 0x00033E84 File Offset: 0x00032084
        void ResetActiveROIConfigDirty()
        {
            if (this.activeROIConfig == null)
            {
                return;
            }
            this.activeROIConfig.Dirty = false;
            this.buttonSave.Enabled = false;
        }

        // Token: 0x060008FC RID: 2300 RVA: 0x00033EAC File Offset: 0x000320AC
        void textBox3_TextChanged(object sender, EventArgs e)
        {
            this.textBox4.Text = this.ConvertNameToFilename(this.textBox3.Text);
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x060008FD RID: 2301 RVA: 0x00033ED0 File Offset: 0x000320D0
        void textBox5_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x060008FE RID: 2302 RVA: 0x00033ED8 File Offset: 0x000320D8
        void button5_Click(object sender, EventArgs e)
        {
            if (this.activeROIConfig == null)
            {
                return;
            }
            if (!this.SaveFormContents(this.activeROIConfig))
            {
                MessageBox.Show(Resources.ERRInvalidInputForm);
                return;
            }
            if (this.activeROIDefinition != null && !this.SaveROIDefinitionFormContents(this.activeROIDefinition))
            {
                MessageBox.Show(Resources.ERRInvalidInputForm);
                return;
            }
            if (!this.manager.SaveConfig(this.activeROIConfig))
            {
                MessageBox.Show(Resources.ERRDuplicateConfigName);
                return;
            }
            this.ResetActiveROIConfigDirty();
            this.UpdateConfigFilesList();
        }

        // Token: 0x060008FF RID: 2303 RVA: 0x00033F68 File Offset: 0x00032168
        string ConvertNameToFilename(string name)
        {
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
            foreach (char oldChar in invalidFileNameChars)
            {
                name = name.Replace(oldChar, '_');
            }
            return name + ".xml";
        }

        // Token: 0x06000900 RID: 2304 RVA: 0x00033FAC File Offset: 0x000321AC
        string AssignNewFilename()
        {
            for (int i = 1; i < 999; i++)
            {
                string text = Resources.NewROIConfigPrefix + "(" + i.ToString() + ").xml";
                bool flag = false;
                foreach (ROIConfigData roiconfigData in this.manager.ROIConfigList)
                {
                    if (text == roiconfigData.Filename)
                    {
                        flag = true;
                        break;
                    }
                }
                if (!flag)
                {
                    return text;
                }
            }
            return null;
        }

        // Token: 0x06000901 RID: 2305 RVA: 0x0003405C File Offset: 0x0003225C
        void button6_Click(object sender, EventArgs e)
        {
            if (!this.ConfirmSaveROIConfig())
            {
                return;
            }
            base.Close();
        }

        // Token: 0x06000902 RID: 2306 RVA: 0x00034070 File Offset: 0x00032270
        void button1_Click(object sender, EventArgs e)
        {
            ROIDefinitionData roidefinitionData = new ROIDefinitionData();
            roidefinitionData.Name = this.CreateNewROIName();
            this.activeROIConfig.ROIDefinitions.Add(roidefinitionData);
            this.ListupROIDefinitions(this.activeROIConfig);
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x06000903 RID: 2307 RVA: 0x000340B8 File Offset: 0x000322B8
        public void CreateNewROIWithRegion(ROIConfigData roiConfig, double lowerLimit, double upperLimit)
        {
            bool flag = false;
            foreach (object obj in this.tableModel3.Rows)
            {
                Row row = (Row)obj;
                ROIConfigData roiconfigData = (ROIConfigData)row.Tag;
                if (roiConfig.Guid == roiconfigData.Guid)
                {
                    flag = true;
                    this.tableModel3.Selections.Clear();
                    this.tableModel3.Selections.AddCell(row.Index, 0);
                    break;
                }
            }
            if (!flag)
            {
                return;
            }
            ROIDefinitionData roidefinitionData = new ROIDefinitionData();
            roidefinitionData.LowerLimit = lowerLimit;
            roidefinitionData.UpperLimit = upperLimit;
            roidefinitionData.Name = this.CreateNewROIName();
            this.activeROIConfig.ROIDefinitions.Add(roidefinitionData);
            this.ListupROIDefinitions(this.activeROIConfig);
            foreach (object obj2 in this.tableModel1.Rows)
            {
                Row row2 = (Row)obj2;
                ROIDefinitionData roidefinitionData2 = (ROIDefinitionData)row2.Tag;
                if (roidefinitionData2 == roidefinitionData)
                {
                    this.tableModel1.Selections.Clear();
                    this.tableModel1.Selections.AddCell(row2.Index, 0);
                    break;
                }
            }
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x06000904 RID: 2308 RVA: 0x00034258 File Offset: 0x00032458
        string CreateNewROIName()
        {
            string text = "";
            for (int i = 0; i < 9999; i++)
            {
                text = "New ROI(" + this.newROIIndex + ")";
                this.newROIIndex++;
                bool flag = false;
                foreach (ROIDefinitionData roidefinitionData in this.activeROIConfig.ROIDefinitions)
                {
                    if (text == roidefinitionData.Name)
                    {
                        flag = true;
                        break;
                    }
                }
                if (!flag)
                {
                    break;
                }
            }
            return text;
        }

        // Token: 0x06000905 RID: 2309 RVA: 0x00034318 File Offset: 0x00032518
        void table3_SelectionChanged(object sender, SelectionEventArgs e)
        {
            if (this.reenter)
            {
                return;
            }
            this.reenter = true;
            ROIConfigData roiconfigData = null;
            Row row = null;
            if (this.table3.SelectedItems.Length > 0)
            {
                roiconfigData = (ROIConfigData)this.table3.SelectedItems[0].Tag;
                row = this.table3.SelectedItems[0];
            }
            if (!this.ConfirmSaveROIConfig())
            {
                return;
            }
            if (roiconfigData != null)
            {
                this.activeROIConfig = roiconfigData;
                this.tableModel3.Selections.Clear();
                this.tableModel3.Selections.AddCell(row.Index, 0);
                this.LoadFormContents(this.activeROIConfig);
                this.button8.Enabled = true;
                this.EnableForm(true);
            }
            else
            {
                this.button8.Enabled = false;
                this.EnableForm(false);
            }
            this.EnableROIDefinitionForm(false);
            this.activeROIDefinition = null;
            this.button2.Enabled = false;
            this.tableModel1.Selections.Clear();
            this.reenter = false;
        }

        // Token: 0x06000906 RID: 2310 RVA: 0x0003442C File Offset: 0x0003262C
        void EnableForm(bool enable)
        {
            this.textBox3.Enabled = enable;
            this.textBox5.Enabled = enable;
            this.panel1.Enabled = enable;
        }

        // Token: 0x06000907 RID: 2311 RVA: 0x00034454 File Offset: 0x00032654
        void button2_Click(object sender, EventArgs e)
        {
            if (this.activeROIDefinition != null)
            {
                DialogResult dialogResult = MessageBox.Show(string.Format(Resources.MSGDeleteROIDefinition, this.activeROIDefinition.Name), Resources.ConfirmationDialogTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
                if (dialogResult == DialogResult.OK)
                {
                    this.activeROIConfig.ROIDefinitions.Remove(this.activeROIDefinition);
                    this.ListupROIDefinitions(this.activeROIConfig);
                    this.EnableROIDefinitionForm(false);
                }
                this.SetActiveROIConfigDirty();
            }
        }

        // Token: 0x06000908 RID: 2312 RVA: 0x000344CC File Offset: 0x000326CC
        void button3_Click(object sender, EventArgs e)
        {
            ROIPrimitiveDefinition roiprimitiveDefinition = ROIPrimitiveDefinition.Definitions[this.comboBox1.SelectedIndex];
            ROIPrimitiveOperation roiprimitiveOperation = ROIPrimitiveOperation.Operations[0];
            ROIPrimitiveData roiprimitiveData = (ROIPrimitiveData)Activator.CreateInstance(roiprimitiveDefinition.TypeOfData);
            roiprimitiveData.Primitive = roiprimitiveDefinition;
            roiprimitiveData.PrimitiveType = roiprimitiveDefinition.Name;
            roiprimitiveData.Operation = roiprimitiveOperation;
            roiprimitiveData.OperationType = roiprimitiveOperation.Name;
            roiprimitiveData.InitFromDefinition(this.activeROIDefinition);
            this.activeROIDefinition.ROIPrimitives.Add(roiprimitiveData);
            this.ShowPrimitiveList(this.activeROIDefinition);
            this.ListupROIDefinitions(this.activeROIConfig);
            Row row = this.tableModel2.Rows[this.tableModel2.Rows.Count - 1];
            this.tableModel2.Selections.Clear();
            this.tableModel2.Selections.AddCell(row.Index, 0);
            this.tabControl1.SelectedIndex = (int)row.Tag;
            this.table2.Select();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x06000909 RID: 2313 RVA: 0x000345D0 File Offset: 0x000327D0
        void button4_Click(object sender, EventArgs e)
        {
            if (this.table2.SelectedItems.Length > 0)
            {
                DialogResult dialogResult = MessageBox.Show(Resources.MSGDeleteROIPrimitive, Resources.ConfirmationDialogTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
                if (dialogResult == DialogResult.OK)
                {
                    Row row = this.table2.SelectedItems[0];
                    this.tableModel2.Rows.Remove(row);
                    this.activeROIDefinition.ROIPrimitives.RemoveAt((int)row.Tag);
                    this.ShowPrimitiveList(this.activeROIDefinition);
                    this.ListupROIDefinitions(this.activeROIConfig);
                }
                this.SetActiveROIConfigDirty();
            }
        }

        // Token: 0x0600090A RID: 2314 RVA: 0x0003466C File Offset: 0x0003286C
        void LoadFormContents(ROIConfigData config)
        {
            this.contentsLoading = true;
            this.textBox3.Text = config.Name;
            this.textBox5.Text = config.Note;
            this.ListupROIDefinitions(config);
            this.contentsLoading = false;
        }

        // Token: 0x0600090B RID: 2315 RVA: 0x000346BC File Offset: 0x000328BC
        void ListupROIDefinitions(ROIConfigData config)
        {
            this.table1.BeginUpdate();
            this.tableModel1.Rows.Clear();
            foreach (ROIDefinitionData roidefinitionData in config.ROIDefinitions)
            {
                Row row = new Row();
                row.Cells.Add(new Cell(roidefinitionData.Name, roidefinitionData.Enabled));
                string text = roidefinitionData.LowerLimit.ToString() + " - " + roidefinitionData.UpperLimit.ToString() + " keV";
                row.Cells.Add(new Cell(text));
                row.Cells.Add(new Cell(roidefinitionData.ROIPrimitives.Count.ToString()));
                row.Tag = roidefinitionData;
                if (this.activeROIDefinition == roidefinitionData)
                {
                    this.activeROIDefinition = roidefinitionData;
                    this.tableModel1.Selections.AddCell(row.Index, 0);
                }
                this.tableModel1.Rows.Add(row);
            }
            this.table1.EndUpdate();
        }

        // Token: 0x0600090C RID: 2316 RVA: 0x00034804 File Offset: 0x00032A04
        void UpdateROIDefinitionList()
        {
            this.table1.BeginUpdate();
            foreach (object obj in this.tableModel1.Rows)
            {
                Row row = (Row)obj;
                ROIDefinitionData roidefinitionData = (ROIDefinitionData)row.Tag;
                row.Cells[0].Checked = roidefinitionData.Enabled;
                row.Cells[0].Text = roidefinitionData.Name;
                row.Cells[1].Text = roidefinitionData.LowerLimit.ToString() + " - " + roidefinitionData.UpperLimit.ToString() + " keV";
                row.Cells[2].Text = roidefinitionData.ROIPrimitives.Count.ToString();
            }
            this.table1.EndUpdate();
        }

        // Token: 0x0600090D RID: 2317 RVA: 0x0003491C File Offset: 0x00032B1C
        bool SaveFormContents(ROIConfigData config)
        {
            try
            {
                if (config.Guid == null || config.Guid == "")
                {
                    config.Guid = Guid.NewGuid().ToString();
                }
                config.Name = this.textBox3.Text;
                config.Filename = this.textBox4.Text;
                config.Note = this.textBox5.Text;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        // Token: 0x0600090E RID: 2318 RVA: 0x000349BC File Offset: 0x00032BBC
        void table1_SelectionChanged(object sender, SelectionEventArgs e)
        {
            if (this.table1.SelectedItems.Length > 0)
            {
                ROIDefinitionData roi = (ROIDefinitionData)this.table1.SelectedItems[0].Tag;
                this.activeROIDefinition = roi;
                this.button2.Enabled = true;
                this.LoadROIDefinitionFormContents(roi);
                this.EnableROIDefinitionForm(true);
                this.table1.Select();
            }
            else
            {
                this.activeROIDefinition = null;
                this.EnableROIDefinitionForm(false);
                this.button2.Enabled = false;
            }
            this.button4.Enabled = false;
            this.tableModel2.Selections.Clear();
        }

        // Token: 0x0600090F RID: 2319 RVA: 0x00034A64 File Offset: 0x00032C64
        void LoadROIDefinitionFormContents(ROIDefinitionData roi)
        {
            this.contentsLoading = true;
            this.textBox1.Text = roi.Name;
            this.checkBox1.Checked = roi.Enabled;
            this.doubleTextBox3.Text = roi.BecquerelCoefficient.ToString();
            this.doubleTextBox4.Text = roi.BecquerelCoefficientError.ToString();
            this.doubleTextBox5.Text = roi.PeakEnergy.ToString();
            this.doubleTextBox6.Text = roi.HalfLife.ToString();
            this.doubleTextBox7.Text = roi.Intencity.ToString();
            this.doubleTextBox1.Text = roi.LowerLimit.ToString();
            this.doubleTextBox2.Text = roi.UpperLimit.ToString();
            this.colorComboBox1.SelectedColor = roi.Color.Color;
            this.colorComboBox1.Refresh();
            this.textBox2.Text = roi.Note;
            this.ShowPrimitiveList(roi);
            this.contentsLoading = false;
        }

        // Token: 0x06000910 RID: 2320 RVA: 0x00034B7C File Offset: 0x00032D7C
        void ShowPrimitiveList(ROIDefinitionData roi)
        {
            this.tabControl1.TabPages.Clear();
            this.table2.BeginUpdate();
            this.tableModel2.Rows.Clear();
            int num = 0;
            foreach (ROIPrimitiveData roiprimitiveData in roi.ROIPrimitives)
            {
                ROIPrimitiveDefinition primitive = roiprimitiveData.Primitive;
                if (primitive == null)
                {
                    MessageBox.Show(Resources.ERRInvalidROIPrimitive);
                    return;
                }
                ROIPrimitiveOperation operation = roiprimitiveData.Operation;
                if (operation == null)
                {
                    MessageBox.Show(Resources.ERRInvalidROIPrimitiveOperation);
                    return;
                }
                if (roiprimitiveData.Control != null)
                {
                    roiprimitiveData.Control.Dispose();
                }
                ROIPrimitiveControl roiprimitiveControl = (ROIPrimitiveControl)Activator.CreateInstance(primitive.TypeOfControl);
                roiprimitiveControl.PrepareForm(this.activeROIConfig);
                string text = (num + 1).ToString() + ") " + primitive.Translation;
                TabPage tabPage = new TabPage(text);
                roiprimitiveData.Control = roiprimitiveControl;
                roiprimitiveControl.LoadFormContents(roiprimitiveData);
                roiprimitiveControl.Parent = tabPage;
                roiprimitiveControl.Dock = DockStyle.Fill;
                roiprimitiveControl.ROIPrimitiveModified += this.control_ROIPrimitiveModified;
                this.tabControl1.TabPages.Add(tabPage);
                Row row = new Row();
                row.Cells.Add(new Cell((num + 1).ToString()));
                row.Cells.Add(new Cell(primitive.Translation));
                Cell cell = new Cell(operation.Translation, operation.Bitmap);
                row.Cells.Add(cell);
                row.Cells.Add(new Cell(this.GetPrimitiveRegionString(roiprimitiveData)));
                row.Cells.Add(new Cell(roiprimitiveData.Note));
                row.Tag = num;
                this.tableModel2.Rows.Add(row);
                num++;
            }
            this.table2.EndUpdate();
        }

        // Token: 0x06000911 RID: 2321 RVA: 0x00034DB0 File Offset: 0x00032FB0
        string GetPrimitiveRegionString(ROIPrimitiveData prim)
        {
            string result = "";
            if (prim is ROISimpleDifferenceData)
            {
                ROISimpleDifferenceData roisimpleDifferenceData = (ROISimpleDifferenceData)prim;
                result = string.Concat(new object[]
                {
                    roisimpleDifferenceData.LowerLimit,
                    " - ",
                    roisimpleDifferenceData.UpperLimit,
                    " keV"
                });
            }
            else if (prim is ROICovellMethodData)
            {
                ROICovellMethodData roicovellMethodData = (ROICovellMethodData)prim;
                result = string.Concat(new object[]
                {
                    roicovellMethodData.LowerLimit,
                    " - ",
                    roicovellMethodData.UpperLimit,
                    " keV"
                });
            }
            else if (prim is ROIReferenceData)
            {
                ROIReferenceData roireferenceData = (ROIReferenceData)prim;
                result = roireferenceData.Reference;
            }
            return result;
        }

        // Token: 0x06000912 RID: 2322 RVA: 0x00034E8C File Offset: 0x0003308C
        void UpdatePrimitiveList()
        {
            this.table2.BeginUpdate();
            if (this.activeROIDefinition == null)
            {
                return;
            }
            foreach (object obj in this.tableModel2.Rows)
            {
                Row row = (Row)obj;
                int index = (int)row.Tag;
                ROIPrimitiveData roiprimitiveData = this.activeROIDefinition.ROIPrimitives[index];
                row.Cells[1].Text = roiprimitiveData.Primitive.Translation;
                row.Cells[2].Text = roiprimitiveData.Operation.Translation;
                row.Cells[2].Image = roiprimitiveData.Operation.Bitmap;
                row.Cells[3].Text = this.GetPrimitiveRegionString(roiprimitiveData);
                row.Cells[4].Text = roiprimitiveData.Note;
            }
            this.table2.EndUpdate();
        }

        // Token: 0x06000913 RID: 2323 RVA: 0x00034FBC File Offset: 0x000331BC
        void control_ROIPrimitiveModified(object sender, EventArgs e)
        {
            this.buttonSave.Enabled = true;
            foreach (ROIPrimitiveData roiprimitiveData in this.activeROIDefinition.ROIPrimitives)
            {
                if (!roiprimitiveData.Control.SaveFormContents(roiprimitiveData))
                {
                    return;
                }
            }
            this.UpdatePrimitiveList();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x06000914 RID: 2324 RVA: 0x00035044 File Offset: 0x00033244
        bool SaveROIDefinitionFormContents(ROIDefinitionData roi)
        {
            try
            {
                roi.Name = this.textBox1.Text;
                roi.Enabled = this.checkBox1.Checked;
                roi.BecquerelCoefficient = double.Parse(this.doubleTextBox3.Text);
                roi.BecquerelCoefficientError = double.Parse(this.doubleTextBox4.Text);
                roi.PeakEnergy = double.Parse(this.doubleTextBox5.Text);
                roi.HalfLife = double.Parse(this.doubleTextBox6.Text);
                roi.Intencity = double.Parse(this.doubleTextBox7.Text);
                roi.LowerLimit = double.Parse(this.doubleTextBox1.Text);
                roi.UpperLimit = double.Parse(this.doubleTextBox2.Text);
                roi.Color.Color = this.colorComboBox1.SelectedColor;
                if (roi.UpperLimit < roi.LowerLimit)
                {
                    roi.UpperLimit = roi.LowerLimit;
                    this.doubleTextBox2.Text = roi.UpperLimit.ToString();
                }
                roi.Note = this.textBox2.Text;
            }
            catch (Exception)
            {
                return false;
            }
            foreach (ROIPrimitiveData roiprimitiveData in roi.ROIPrimitives)
            {
                if (!roiprimitiveData.Control.SaveFormContents(roiprimitiveData))
                {
                    return false;
                }
            }
            return true;
        }

        // Token: 0x06000915 RID: 2325 RVA: 0x000351EC File Offset: 0x000333EC
        void EnableROIDefinitionForm(bool enable)
        {
            this.groupBox1.Enabled = enable;
            this.comboBox1.Enabled = enable;
            this.button3.Enabled = enable;
            this.button4.Enabled = enable;
            this.table2.Enabled = enable;
            this.tabControl1.Enabled = enable;
        }

        // Token: 0x06000916 RID: 2326 RVA: 0x00035248 File Offset: 0x00033448
        void table2_SelectionChanged(object sender, SelectionEventArgs e)
        {
            if (this.table2.SelectedItems.Length > 0)
            {
                int selectedIndex = (int)this.table2.SelectedItems[0].Tag;
                this.tabControl1.SelectedIndex = selectedIndex;
                this.table2.Select();
            }
            this.button4.Enabled = true;
        }

        // Token: 0x06000917 RID: 2327 RVA: 0x000352AC File Offset: 0x000334AC
        void table1_CellCheckChanged(object sender, CellCheckBoxEventArgs e)
        {
            this.contentsLoading = true;
            int row = e.Row;
            Row row2 = this.tableModel1.Rows[row];
            this.activeROIConfig.ROIDefinitions[row].Enabled = row2.Cells[0].Checked;
            if (this.activeROIDefinition == (ROIDefinitionData)row2.Tag)
            {
                this.checkBox1.Checked = row2.Cells[0].Checked;
            }
            this.contentsLoading = false;
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x06000918 RID: 2328 RVA: 0x00035344 File Offset: 0x00033544
        void textBox1_TextChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.Name = this.textBox1.Text;
            this.UpdateROIDefinitionList();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x06000919 RID: 2329 RVA: 0x00035374 File Offset: 0x00033574
        void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.Enabled = this.checkBox1.Checked;
            this.UpdateROIDefinitionList();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x0600091A RID: 2330 RVA: 0x000353A4 File Offset: 0x000335A4
        void doubleTextBox5_TextChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.PeakEnergy = this.doubleTextBox5.GetValue();
            this.UpdateROIDefinitionList();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x0600091B RID: 2331 RVA: 0x000353D4 File Offset: 0x000335D4
        void doubleTextBox1_TextChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.LowerLimit = this.doubleTextBox1.GetValue();
            this.UpdateROIDefinitionList();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x0600091C RID: 2332 RVA: 0x00035404 File Offset: 0x00033604
        void doubleTextBox2_TextChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.UpperLimit = this.doubleTextBox2.GetValue();
            this.UpdateROIDefinitionList();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x0600091D RID: 2333 RVA: 0x00035434 File Offset: 0x00033634
        void colorComboBox1_ColorChanged(object sender, ColorChangeArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.Color.Color = this.colorComboBox1.SelectedColor;
            this.UpdateROIDefinitionList();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x0600091E RID: 2334 RVA: 0x00035478 File Offset: 0x00033678
        void doubleTextBox3_TextChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.BecquerelCoefficient = this.doubleTextBox3.GetValue();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x0600091F RID: 2335 RVA: 0x000354A4 File Offset: 0x000336A4
        void doubleTextBox4_TextChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.BecquerelCoefficientError = this.doubleTextBox4.GetValue();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x06000920 RID: 2336 RVA: 0x000354D0 File Offset: 0x000336D0
        void textBox2_TextChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.Note = this.textBox2.Text;
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x06000921 RID: 2337 RVA: 0x00035500 File Offset: 0x00033700
        void doubleTextBox6_TextChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.HalfLife = this.doubleTextBox6.GetValue();
            this.SetActiveROIConfigDirty();
        }

        void doubleTextBox7_TextChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            this.activeROIDefinition.Intencity = this.doubleTextBox7.GetValue();
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x06000922 RID: 2338 RVA: 0x0003552C File Offset: 0x0003372C
        void button9_Click(object sender, EventArgs e)
        {
            NuclideDefinition nuclideDefinition = (NuclideDefinition)this.comboBox2.SelectedItem;
            if (nuclideDefinition == null)
            {
                return;
            }
            double num = (double)this.numericUpDown1.Value / 100.0;
            ROIDefinitionData roidefinitionData = new ROIDefinitionData();
            roidefinitionData.Name = nuclideDefinition.Name;
            roidefinitionData.PeakEnergy = nuclideDefinition.Energy;
            roidefinitionData.LowerLimit = Math.Floor(nuclideDefinition.Energy - nuclideDefinition.Energy * num / 2.0);
            roidefinitionData.UpperLimit = Math.Round(nuclideDefinition.Energy + nuclideDefinition.Energy * num / 2.0);
            roidefinitionData.HalfLife = nuclideDefinition.HalfLife;
            roidefinitionData.Intencity = nuclideDefinition.Intencity;
            if (this.activeROIConfig.HasEfficiency && nuclideDefinition.Intencity > 0)
            {
                ROIAriphmetics roiAriphmetics = new ROIAriphmetics(this.activeROIConfig);
                ROIEfficiencyData effData = roiAriphmetics.CalculateEfficiency(nuclideDefinition.Energy);
                if (effData != null && effData.Efficiency > 0)
                {
                    roidefinitionData.BecquerelCoefficient = (1 / effData.Efficiency) / (nuclideDefinition.Intencity / 100);
                    if (effData.ErrorPercent > 0)
                    {
                        roidefinitionData.BecquerelCoefficientError = roidefinitionData.BecquerelCoefficient * (effData.ErrorPercent / 100);
                    }
                }
            }

            this.activeROIConfig.ROIDefinitions.Add(roidefinitionData);
            this.ListupROIDefinitions(this.activeROIConfig);
            this.SetActiveROIConfigDirty();
        }

        // Token: 0x06000923 RID: 2339 RVA: 0x00035610 File Offset: 0x00033810
        void SetupNuclideDefinitionList()
        {
            NuclideDefinitionManager instance = NuclideDefinitionManager.GetInstance();
            foreach (NuclideDefinition item in instance.NuclideDefinitions)
            {
                if (!item.Visible) continue;
                this.comboBox2.Items.Add(item);
            }
            this.button9.Enabled = (this.comboBox2.SelectedItem != null);
        }

        // Token: 0x06000924 RID: 2340 RVA: 0x00035698 File Offset: 0x00033898
        void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.button9.Enabled = (this.comboBox2.SelectedItem != null);
        }

        // Token: 0x04000507 RID: 1287
        ROIConfigManager manager = ROIConfigManager.GetInstance();

        // Token: 0x04000508 RID: 1288
        ROIConfigData activeROIConfig;

        // Token: 0x04000509 RID: 1289
        ROIDefinitionData activeROIDefinition;

        // Token: 0x0400050A RID: 1290
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x0400050B RID: 1291
        int newROIIndex = 1;

        // Token: 0x0400050C RID: 1292
        bool contentsLoading;

        // Token: 0x0400050D RID: 1293
        bool reenter;

        private void buttonEfficiency_Click(object sender, EventArgs e)
        {
            using (ROIEditEfficiencyDialog dialog = new ROIEditEfficiencyDialog(this))
            {
                dialog.ShowDialog();
                if (activeROIConfig.Dirty)
                {
                    this.buttonSave.Enabled = true;
                }
            }
        }
    }
}
