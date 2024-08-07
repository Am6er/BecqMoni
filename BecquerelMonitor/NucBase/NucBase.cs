﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using System.Text.RegularExpressions;
using System.Threading;
using System.Globalization;
using BecquerelMonitor;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace BecquerelMonitor.NucBase
{
    public partial class NucBase : Form
    {
        private const int CheckedColumnIdx = 0;
        private const int NameColumnIdx = 1;
        private const int EnergyColumnIdx = 3;
        private const int IntencityColumnIdx = 4;
        private const int HalfLifeColumnIdx = 7;
        private string SearchedIsotope;

        public NucBase(Form mainForm)
        {
            InitializeComponent();
            this.mainForm = mainForm;
            this.Icon = Resources.becqmoni;
            this.IncludeDecayChainCheckBox.Enabled = false;
            this.comboBoxNameFormat.SelectedIndex = 1;
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            DoSearch();
        }

        private void DoSearch()
        {
            GC.Collect();
            string isotopeTextBox = this.IsotopeTextBox.Text.Trim().Replace("-", "");
            Match isomerRegex = Regex.Match(isotopeTextBox, @"[m]\d{0,1}$");
            string isomer = "";
            string isotope = isotopeTextBox.ToUpper();
            if (isomerRegex.Index + isomerRegex.Length == isotopeTextBox.Length) 
            {
                isomer = isomerRegex.Value;
                isotope = isotopeTextBox.Substring(0, isomerRegex.Index).ToUpper();
            }
            string isotope_number = Regex.Match(isotope, @"\d+").Value;
            string isotope_name = Regex.Match(isotope, @"[a-zA-Z]+").Value;
            isotope = isotope_number + isotope_name + isomer;
            this.SearchedIsotope = isotope;
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
            if (this.HalfLifeUOMComboBox.Text.Length > 0 && this.HalfLifeTextBox.Text.Length > 0)
            {
                half_life = ConvertHalfLifeToSeconds(Convert.ToDouble(this.HalfLifeTextBox.Text), this.HalfLifeUOMComboBox.Text);
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
                        AddRow(decrad);
                    }
                    RestoreSorting();
                }
            }
            else
            {
                List<string> daughters = fw.GetDaughters(isotope, intensity);
                if (!daughters.Contains(isotope)) daughters.Add(isotope);
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
                                AddRow(decrad);
                                Trace.WriteLine($"{this.ResultDataGridView.Rows.Count} rows added");
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
                this.IsotopeSpecActivity.Text = nuc.SpecialActivity.ToString("e2") + " " + Resources.Bkg;
                this.IsotopeAbundance.Text = nuc.Abundance.ToString() + " %";

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

            UpdateNuclideDefinitionControlsState();
        }

        private void UpdateNuclideDefinitionControlsState()
        {
            bool hasRows = this.ResultDataGridView.Rows.Count > 0;

            buttonImportDef.Enabled = hasRows;
            checkBoxOverwriteDef.Enabled = hasRows;
            checkBoxAppendRootName.Enabled = IncludeDecayChainCheckBox.Checked;
            checkBoxAppendRootName.Checked = IncludeDecayChainCheckBox.Checked;
            comboBoxNameFormat.Enabled = hasRows;
            labelNameFormat.Enabled = hasRows;
        }

        private double ConvertHalfLifeToSeconds(double value, string unit)
        {
            double coeff;

            switch (unit)
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
                case "d":
                    coeff = 86400;
                    break;
                case "Y":
                    coeff = 31536000;
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
            
            return coeff * value;
        }

        private void AddRow(DecayRad decrad)
        {
            // TODO: use data binding?
            bool isGamma = decrad.DecayLine == "G";
            string hl = decrad.HalfLife.ToString() + "(" + decrad.HalfLifeUnit + ")";
            this.ResultDataGridView.Rows.Add(isGamma, decrad.Name, decrad.DecayLine, decrad.Energy, decrad.Intensity, decrad.XrayType, decrad.DecayTypeString, hl);
        }

        public void CallSearch(decimal energy)
        {
            double delta = 10;
            double lowenergy = (double)energy - delta;
            double highenergy = (double)energy + delta;
            if (lowenergy < 0)
            {
                lowenergy = 0;
            }

            this.LowEnrgTextBox.Text = lowenergy.ToString();
            this.HighEnrgTextBox.Text = highenergy.ToString();

            DoSearch();
        }

        private void ResultDataGridView_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1)
            {
                if (e.ColumnIndex == CheckedColumnIdx)
                {
                    ToggleSelection();
                }

                return;
            }
            string isotope = this.ResultDataGridView.Rows[e.RowIndex].Cells[NameColumnIdx].Value.ToString();
            NucBaseFramework fw = new NucBaseFramework();
            Nuclide nuc = fw.getNuclude(isotope);
            this.IsotopeNameLabel.Text = isotope;
            this.IsotopeZLabel.Text = nuc.Z.ToString();
            this.IsotopeNLabel.Text = nuc.N.ToString();
            this.IsotopeHLLabel.Text = nuc.HalfLife.ToString() + " " + nuc.HalfLifeUOM;
            this.IsotopeSpecActivity.Text = nuc.SpecialActivity.ToString("e2") + " " + Resources.Bkg;
            this.IsotopeAbundance.Text = nuc.Abundance.ToString() + " %";

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

        private void ToggleSelection()
        {
            this.ResultDataGridView.SuspendLayout();
            DataGridViewColumn checkCol = this.ResultDataGridView.Columns[CheckedColumnIdx];
            checkCol.HeaderText = checkCol.HeaderText == "X"
                ? ""
                : "X";

            foreach (DataGridViewRow row in this.ResultDataGridView.Rows)
            {
                row.Cells[CheckedColumnIdx].Value = checkCol.HeaderText == "X";
            }
            this.ResultDataGridView.RefreshEdit();
            this.ResultDataGridView.ResumeLayout();
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
            string isotope = this.ResultDataGridView.Rows[e.RowIndex].Cells[NameColumnIdx].Value.ToString();
            if (isotope != null)
            {
                NucBaseFramework fw = new NucBaseFramework();
                Nuclide nuc = fw.getNuclude(isotope);
                this.IsotopeNameLabel.Text = isotope;
                this.IsotopeZLabel.Text = nuc.Z.ToString();
                this.IsotopeNLabel.Text = nuc.N.ToString();
                this.IsotopeHLLabel.Text = nuc.HalfLife.ToString() + " " + nuc.HalfLifeUOM;
                this.IsotopeSpecActivity.Text = nuc.SpecialActivity.ToString("e2") + " " + Resources.Bkg;
                this.IsotopeAbundance.Text = nuc.Abundance.ToString() + " %";

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

        private void IsotopeTextBox_Enter(object sender, EventArgs e)
        {
            int DisplayTime = 10000;
            this.toolTip1.Show(Resources.NucBase_IsotopeTextBoxTooltip1, this.IsotopeTextBox, 0, -23, DisplayTime);
        }

        private void buttonImportDef_Click(object sender, EventArgs e)
        {
            try
            {
                int updatedCount = 0;
                int createdCount = 0;
                NuclideDefinitionManager defManager = NuclideDefinitionManager.GetInstance();
                foreach (DataGridViewRow row in this.ResultDataGridView.Rows)
                {
                    if ((bool)row.Cells[CheckedColumnIdx].Value == true)
                    {
                        string name = (string)row.Cells[NameColumnIdx].Value;
                        string formattedName = FormatIsotopeName(name);
                        double energy = (double)row.Cells[EnergyColumnIdx].Value;
                        double intencity = (double)row.Cells[IntencityColumnIdx].Value;
                        double halfLife = Convert.ToDouble(((string)row.Cells[HalfLifeColumnIdx].Value).Split('(')[0]);
                        string halfLifeUnit = ((string)row.Cells[HalfLifeColumnIdx].Value).Split('(')[1].Substring(0, 1);
                        double halfLifeYears = ConvertHalfLifeToSeconds(halfLife, halfLifeUnit) / 31536000;

                        if (IncludeDecayChainCheckBox.Checked && checkBoxAppendRootName.Checked && this.SearchedIsotope != name)
                        {
                            formattedName += " (" + FormatIsotopeName(this.SearchedIsotope) + ")";
                        }

                        NuclideDefinition existingDef = defManager.NuclideDefinitions.FirstOrDefault(def => def.Energy == energy);
                        if (existingDef != null && checkBoxOverwriteDef.Checked)
                        {
                            existingDef.Name = formattedName;
                            existingDef.Intencity = intencity;
                            existingDef.HalfLife = halfLifeYears;
                            updatedCount++;
                        }
                        
                        if (existingDef == null)
                        {
                            defManager.NuclideDefinitions.Add(new NuclideDefinition()
                            {
                                Name = formattedName,
                                Energy = energy,
                                Intencity = intencity,
                                HalfLife = halfLifeYears,
                                Visible = true,
                                NuclideColor = new SerializableColor(System.Drawing.Color.Green)
                            });
                            createdCount++;
                        }
                    }
                }

                if (updatedCount > 0 || createdCount > 0)
                {
                    defManager.SaveDefinitionFile();
                    MessageBox.Show(string.Format(Resources.NuclideDefImportSuccess, createdCount, updatedCount));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.NuclideDefImportError, ex.Message + ex.StackTrace));
            }
        }

        private string FormatIsotopeName(string nameFromDb)
        {
            Regex nameFormat = new Regex("^([0-9]+){1}([A-Z]+){1}(m[0-9]+)?$");
            Match match = nameFormat.Match(nameFromDb);
            if (!match.Success)
            {
                return nameFromDb;
            }

            string mass = match.Groups[1].Value;
            string isotope = match.Groups[2].Value;
            string isotopeLower = $"{isotope.Substring(0, 1)}{isotope.Substring(1).ToLower()}";
            string isomer = match.Groups.Count > 3
                ? match.Groups[3].Value
                : string.Empty;

            switch (comboBoxNameFormat.SelectedIndex)
            {
                case 0: // 137CS, 234PAm1
                    return $"{mass}{isotope}{isomer}";
                case 1: // Cs137, Pa234m1
                    return $"{isotopeLower}{mass}{isomer}";
                case 2: // Cs-137, Pa-234m1
                    return $"{isotopeLower}-{mass}{isomer}";
                default: // Cs137, Pa234m1
                    return $"{isotopeLower}{mass}{isomer}";
            }
        }

        private void IncludeDecayChainCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            UpdateNuclideDefinitionControlsState();
        }
    }
}
