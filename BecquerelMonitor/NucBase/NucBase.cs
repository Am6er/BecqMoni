﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using System.Text.RegularExpressions;
using System.Threading;
using System.Globalization;
using BecquerelMonitor;
using System.ComponentModel;

namespace BecquerelMonitor.NucBase
{
    public partial class NucBase : Form
    {
        public NucBase(Form mainForm)
        {
            InitializeComponent();
            this.mainForm = mainForm;
            this.Icon = Resources.becqmoni;
            this.IncludeDecayChainCheckBox.Enabled = false;
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            DoSearch();
        }

        private void DoSearch()
        {
            GC.Collect();
            string isotope = this.IsotopeTextBox.Text.Trim().Replace("-", "").ToUpper();
            string isotope_number = Regex.Match(isotope, @"\d+").Value;
            string isotope_name = Regex.Match(isotope, @"[a-zA-Z]+").Value;
            isotope = isotope_number + isotope_name;
            bool incDecayChain = this.IncludeDecayChainCheckBox.Checked;
            double lowEnergy = 0.0;
            if (this.LowEnrgTextBox.Text.Length != 0)
            {
                lowEnergy = Convert.ToDouble(this.LowEnrgTextBox.Text);
            }
            double highEnergy = 0.0;
            if (this.HighEnrgTextBox.Text.Length != 0)
            {
                highEnergy = Convert.ToDouble(this.HighEnrgTextBox.Text);
            }
            double intensity = 0.0;
            if (this.IntencityTextBox.Text.Length != 0)
            {
                intensity = Convert.ToDouble(this.IntencityTextBox.Text);
            }
            double half_life = -1;
            if (this.HalfLifeUOMComboBox.SelectedText.Length > 0 && this.HalfLifeTextBox.Text.Length > 0)
            {
                double coeff;
                switch (this.HalfLifeUOMComboBox.Text)
                {
                    case "s":
                        coeff = 1;
                        break;
                    case "m":
                        coeff = 60;
                        break;
                    case "h":
                        coeff = 3600;
                        break;
                    case "Y":
                        coeff = 1314000;
                        break;
                    case "ms":
                        coeff = 1.0 / 1000.0;
                        break;
                    case "us":
                        coeff = 1.0 / 1000000.0;
                        break;
                    case "ns":
                        coeff = 1.0 / 1000000000.0;
                        break;
                    default:
                        coeff = 1.0;
                        break;
                }
                half_life = coeff * Convert.ToDouble(this.HalfLifeTextBox.Text);
            }


            NucBaseFramework fw = new NucBaseFramework();

            if (!incDecayChain)
            {
                List<DecayRad> decayRads = fw.getDecayRad(isotope, intensity: intensity, lowEnergy: lowEnergy, highEnergy: highEnergy, half_life_sec: half_life);
                if (decayRads != null)
                {
                    this.ResultDataGridView.Rows.Clear();
                    foreach (DecayRad decrad in decayRads)
                    {
                        this.ResultDataGridView.Rows.Add(decrad.Name, decrad.DecayLine, decrad.Energy, decrad.Intensity, decrad.XrayType, decrad.DecayTypeString);
                    }
                    RestoreSorting();
                }
            }
            else
            {
                List<string> daughters = fw.GetDaughters(isotope);
                daughters.Add(isotope);
                if (daughters.Count > 0)
                {
                    this.ResultDataGridView.Rows.Clear();
                    foreach (string daughter in daughters)
                    {
                        List<DecayRad> decayRads = fw.getDecayRad(daughter, intensity: intensity, lowEnergy: lowEnergy, highEnergy: highEnergy, half_life_sec: half_life);
                        if (decayRads != null)
                        {
                            foreach (DecayRad decrad in decayRads)
                            {
                                this.ResultDataGridView.Rows.Add(decrad.Name, decrad.DecayLine, decrad.Energy, decrad.Intensity, decrad.XrayType, decrad.DecayTypeString);
                            }
                        }
                    }
                    RestoreSorting();
                }
            }


            if (this.IsotopeTextBox.Text.Length == 0)
            {
                return;
            }
            Nuclide nuc = fw.getNuclude(isotope);
            if (nuc != null)
            {
                this.IsotopeNameLabel.Text = isotope;
                this.IsotopeZLabel.Text = nuc.Z.ToString();
                this.IsotopeNLabel.Text = nuc.N.ToString();
                this.IsotopeHLLabel.Text = nuc.HalfLife.ToString() + " " + nuc.HalfLifeUOM;

                this.ParentsDataGridView.Rows.Clear();
                foreach (Decay parent in nuc.Parents)
                {
                    this.ParentsDataGridView.Rows.Add(parent.NucName, parent.DecayTypeString, parent.DecayPercent);
                }

                this.DaughtersDataGridView.Rows.Clear();
                foreach (Decay daughter in nuc.Daughters)
                {
                    this.DaughtersDataGridView.Rows.Add(daughter.NucName, daughter.DecayTypeString, daughter.DecayPercent);
                }
            }
        }

        private void ResultDataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1)
            {
                return;
            }
            string isotope = this.ResultDataGridView.Rows[e.RowIndex].Cells[0].Value.ToString();
            NucBaseFramework fw = new NucBaseFramework();
            Nuclide nuc = fw.getNuclude(isotope);
            this.IsotopeNameLabel.Text = isotope;
            this.IsotopeZLabel.Text = nuc.Z.ToString();
            this.IsotopeNLabel.Text = nuc.N.ToString();
            this.IsotopeHLLabel.Text = nuc.HalfLife.ToString() + " " + nuc.HalfLifeUOM;

            this.ParentsDataGridView.Rows.Clear();
            foreach (Decay parent in nuc.Parents)
            {
                this.ParentsDataGridView.Rows.Add(parent.NucName, parent.DecayTypeString, parent.DecayPercent);
            }

            this.DaughtersDataGridView.Rows.Clear();
            foreach (Decay daughter in nuc.Daughters)
            {
                this.DaughtersDataGridView.Rows.Add(daughter.NucName, daughter.DecayTypeString, daughter.DecayPercent);
            }
        }

        private void IsotopeTextBox_TextChanged(object sender, EventArgs e)
        {
            if (this.IsotopeTextBox.Text.Length == 0)
            {
                this.IncludeDecayChainCheckBox.Enabled = false;
                this.IncludeDecayChainCheckBox.Checked = false;
            } else
            {
                this.IncludeDecayChainCheckBox.Enabled = true;
            }
        }

        private void RestoreSorting()
        {
            ListSortDirection direction;
            if (this.ResultDataGridView.SortOrder == SortOrder.Ascending) direction = ListSortDirection.Ascending;
            else direction = ListSortDirection.Descending;
            if (this.ResultDataGridView.SortedColumn != null)
            {
                this.ResultDataGridView.Sort(this.ResultDataGridView.SortedColumn, direction);
            }
        }

        Form mainForm;

        private void IsotopeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void IntencityTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void HalfLifeTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void LowEnrgTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void HighEnrgTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch();
            }
        }

        private void ResultDataGridView_CellEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1)
            {
                return;
            }
            string isotope = this.ResultDataGridView.Rows[e.RowIndex].Cells[0].Value.ToString();
            NucBaseFramework fw = new NucBaseFramework();
            Nuclide nuc = fw.getNuclude(isotope);
            this.IsotopeNameLabel.Text = isotope;
            this.IsotopeZLabel.Text = nuc.Z.ToString();
            this.IsotopeNLabel.Text = nuc.N.ToString();
            this.IsotopeHLLabel.Text = nuc.HalfLife.ToString() + " " + nuc.HalfLifeUOM;

            this.ParentsDataGridView.Rows.Clear();
            foreach (Decay parent in nuc.Parents)
            {
                this.ParentsDataGridView.Rows.Add(parent.NucName, parent.DecayTypeString, parent.DecayPercent);
            }

            this.DaughtersDataGridView.Rows.Clear();
            foreach (Decay daughter in nuc.Daughters)
            {
                this.DaughtersDataGridView.Rows.Add(daughter.NucName, daughter.DecayTypeString, daughter.DecayPercent);
            }
        }
    }
}