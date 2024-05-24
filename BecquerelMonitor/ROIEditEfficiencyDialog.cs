using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using XPTable.Models;

namespace BecquerelMonitor
{
    public partial class ROIEditEfficiencyDialog : Form
    {
        public ROIConfigForm ROIConfigForm { get; private set; }
        public bool HasChanges { get; private set; }

        public ROIEditEfficiencyDialog()
        {
            InitializeComponent();
        }

        public ROIEditEfficiencyDialog(ROIConfigForm configForm)
        {
            InitializeComponent();
            this.ROIConfigForm = configForm;
            this.Icon = Resources.becqmoni;
            this.columnModelEfficiency.Columns[0].Alignment = ColumnAlignment.Right;
            this.columnModelEfficiency.Columns[1].Alignment = ColumnAlignment.Right;
            this.columnModelEfficiency.Columns[2].Alignment = ColumnAlignment.Right;
            FillEfficiencyTable();
        }

        private void FillEfficiencyTable()
        {
            var config = this.ROIConfigForm.ActiveROIConfig;
            if (config == null || config.ROIEfficiency == null || config.ROIEfficiency.Count == 0)
            {
                return;
            }

            this.tableEfficiency.BeginUpdate();
            this.tableModelEfficiency.Rows.Clear();            
            foreach (ROIEfficiencyData effData in config.ROIEfficiency)
            {
                Row row = new Row();
                row.Cells.Add(new Cell(effData.Energy));
                row.Cells.Add(new Cell(effData.Efficiency));
                row.Cells.Add(new Cell(effData.ErrorPercent));
                
                this.tableModelEfficiency.Rows.Add(row);
            }
            this.tableEfficiency.EndUpdate();
        }

        private void buttonAddRow_Click(object sender, EventArgs e)
        {
            this.tableEfficiency.BeginUpdate();

            Row row = new Row();
            row.Cells.Add(new Cell(0));
            row.Cells.Add(new Cell(0));
            row.Cells.Add(new Cell(0));

            this.tableModelEfficiency.Rows.Add(row);
            this.tableEfficiency.EndUpdate();
            this.HasChanges = true;
        }

        private void tableEfficiency_SelectionChanged(object sender, XPTable.Events.SelectionEventArgs e)
        {
            if (e.NewSelectedIndicies.Length > 0)
            {
                this.buttonRemoveRow.Enabled = true;
            }
            else
            {
                this.buttonRemoveRow.Enabled = false;
            }
        }

        private void buttonRemoveRow_Click(object sender, EventArgs e)
        {
            if (this.tableEfficiency.SelectedIndicies.Length > 0)
            {
                this.tableModelEfficiency.Rows.RemoveAt(this.tableEfficiency.SelectedIndicies[0]);
                this.HasChanges = true;
            }
        }

        private void ROIEditEfficiencyDialog_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (this.HasChanges)
            {
                var config = this.ROIConfigForm.ActiveROIConfig;
                config.ROIEfficiency = new List<ROIEfficiencyData>();
                foreach (Row row in this.tableModelEfficiency.Rows)
                {
                    var effData = new ROIEfficiencyData()
                    {
                        Energy = Convert.ToDouble(row.Cells[0].Data),
                        Efficiency = Convert.ToDouble(row.Cells[1].Data),
                        ErrorPercent = Convert.ToDouble(row.Cells[2].Data)
                    };

                    if (effData.Energy > 0 && effData.Efficiency > 0)
                    {
                        config.ROIEfficiency.Add(effData);
                    }
                }

                config.ROIEfficiency = config.ROIEfficiency.OrderBy(effData => effData.Energy).ToList();
                config.Dirty = true;
            }
        }

        private void tableEfficiency_EditingStopped(object sender, XPTable.Events.CellEditEventArgs e)
        {
            this.HasChanges = true;
        }
    }
}
