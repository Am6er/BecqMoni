using System;
using System.Collections.Generic;
using System.Windows.Forms;
using BecquerelMonitor.Properties;
using System.Text.RegularExpressions;
using System.Threading;
using System.Globalization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace BecquerelMonitor.NucBase
{
    public partial class NucBase : Form
    {
        private const int CheckedColumnIdx = 0;
        private const int NameColumnIdx = 1;
        private const int LineColumnIdx = 2;
        private const int EnergyColumnIdx = 3;
        private const int IntencityColumnIdx = 4;
        private const int SeriesColumnIdx = 5;
        private const int DecayTypeColumnIdx = 6;
        private const int HalfLifeColumnIdx = 7;

        /// <summary>
        /// Хвост подписи у характеристического рентгена элемента: «W x-ray».
        /// Не переводится: подпись стоит в файле определений и читается тем же
        /// разбором на обоих языках.
        /// </summary>
        private const string XrayNameSuffix = "x-ray";

        private string SearchedIsotope;

        public NucBase()
        {
            InitializeComponent();
        }

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
            // TryParse instead of Convert.ToDouble: non-numeric input used to throw
            // FormatException out of the search button handler.
            double lowEnergy = 0.0;
            if (this.LowEnrgTextBox.Text.Length != 0)
            {
                double.TryParse(this.LowEnrgTextBox.Text, out lowEnergy);
            }
            double highEnergy = 0.0;
            if (this.HighEnrgTextBox.Text.Length != 0)
            {
                double.TryParse(this.HighEnrgTextBox.Text, out highEnergy);
            }
            double intensity = 0.0;
            if (this.IntencityTextBox.Text.Length != 0)
            {
                double.TryParse(this.IntencityTextBox.Text, out intensity);
            }
            double half_life = -1;
            if (this.HalfLifeUOMComboBox.Text.Length > 0 && this.HalfLifeTextBox.Text.Length > 0)
            {
                double halfLifeValue;
                if (double.TryParse(this.HalfLifeTextBox.Text, out halfLifeValue))
                {
                    half_life = ConvertHalfLifeToSeconds(halfLifeValue, this.HalfLifeUOMComboBox.Text);
                }
            }

            NucBaseFramework fw = new NucBaseFramework();

            // Символ элемента без массового числа («W», «Pb») — запрос не про
            // распад, а про характеристический рентген: чем светит вольфрам
            // электрода или свинец домика, когда в нём выбило K-электрон. Ряда
            // и родителей у такого запроса нет, поэтому ветка своя и короткая.
            string element = ElementSymbol(isotopeTextBox);
            if (element != null)
            {
                this.SearchedIsotope = element;
                List<DecayRad> fluorescence = fw.GetFluorescence(
                    element, intensity: intensity, lowEnergy: lowEnergy, highEnergy: highEnergy);
                this.ResultDataGridView.Rows.Clear();
                foreach (DecayRad line in fluorescence)
                {
                    AddRow(line);
                }

                RestoreSorting();
                ClearIsotopeCard();
                if (fluorescence.Count == 0)
                {
                    MessageBox.Show(this,
                        string.Format(Resources.NucBase_NoFluorescence, element),
                        Resources.NucBase_FluorescenceTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                UpdateNuclideDefinitionControlsState();
                return;
            }

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
                // Выбран ряд — значит и выходы показываются НА РАСПАД КОРНЯ
                // ряда, а не на распад своего нуклида: у Tl-208 в ториевом ряду
                // это 35.85 % вместо 99.75 %. Ровно эти числа и ввозятся, и
                // ровно их ждёт всё, что стоит на вековом равновесии, —
                // конструктор кривой и разложение спектра.
                Dictionary<string, double> branches = fw.GetChainBranches(isotope);
                if (branches.Count > 0)
                {
                    this.ResultDataGridView.Rows.Clear();
                    foreach (KeyValuePair<string, double> member in branches.OrderByDescending(m => m.Value))
                    {
                        // Порог выхода прикладывается к ПОКАЗАННОМУ числу, а не
                        // к базовому: иначе «не ниже 1 %» отсеивало бы по
                        // величине, которой на экране нет.
                        List<DecayRad> decayRads = fw.getDecayRad(member.Key, intensity: 0.0, lowEnergy: lowEnergy, highEnergy: highEnergy, half_life_sec: half_life);
                        if (decayRads != null)
                        {
                            foreach (DecayRad decrad in decayRads)
                            {
                                decrad.Intensity *= member.Value;
                                if (decrad.Intensity < intensity)
                                {
                                    continue;
                                }

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

        /// <summary>
        /// Символ элемента, если в запросе нет массового числа: «w» -&gt; «W»,
        /// «PB» -&gt; «Pb». Иначе null — искать надо нуклид, как и раньше.
        ///
        /// Регистр приводится здесь, а не в поиске нуклида: тот всё поднимает в
        /// верхний («137CS»), а символ элемента пишется «Pb», и по «PB» в
        /// таблице ничего не найдётся.
        /// </summary>
        public static string ElementSymbol(string query)
        {
            string letters = Regex.Match(query ?? "", @"^[a-zA-Z]{1,2}$").Value;
            if (letters.Length == 0)
            {
                return null;
            }

            string symbol = letters.Substring(0, 1).ToUpperInvariant()
                            + letters.Substring(1).ToLowerInvariant();
            return MaterialDatabase.ZOf(symbol) > 0 ? symbol : null;
        }

        /// <summary>
        /// Карточка нуклида — про распад, а у элемента распада нет. Оставленная
        /// от прошлого поиска, она подписала бы рентген вольфрама периодом
        /// полураспада того, кого искали до него.
        /// </summary>
        private void ClearIsotopeCard()
        {
            this.IsotopeNameLabel.Text = this.SearchedIsotope;
            this.IsotopeZLabel.Text = MaterialDatabase.ZOf(this.SearchedIsotope).ToString();
            this.IsotopeNLabel.Text = "";
            this.IsotopeHLLabel.Text = "";
            this.IsotopeSpecActivity.Text = "";
            this.IsotopeAbundance.Text = "";
            this.ParentsDataGridView.Rows.Clear();
            this.DaughtersDataGridView.Rows.Clear();
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

        /// <summary>
        /// Подпись определения для линии характеристического рентгена: «W» -&gt;
        /// «W x-ray». Массового числа в ней нет и быть не может — по этому и
        /// отличают рентген от нуклида те, кто читает файл определений
        /// (см. <see cref="NuclideDefinition.IsElementXrayName"/>).
        /// </summary>
        public static string XrayDefinitionName(string symbol)
        {
            return (symbol ?? "").Trim() + " " + XrayNameSuffix;
        }

        /// <summary>
        /// Период полураспада в годах из ячейки таблицы вида «5.75(Y)».
        ///
        /// Вынесено из обработчика ввоза вместе с <see cref="XrayDefinitionName"/>:
        /// форму можно собрать и без главного окна, но ввоз кончается модальным
        /// сообщением, и проба на нём повисла бы. У рентгена периода нет вовсе —
        /// в ячейке ноль, и разбор обязан его пережить, а не уронить весь ввоз.
        /// </summary>
        public static double HalfLifeYearsFromCell(string cell)
        {
            string[] parts = (cell ?? "").Split('(');
            double value;
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.CurrentCulture, out value);
            // Take the full unit, not the first character: Substring(0,1) turned
            // "ms" into "m" (minutes, a x60000 error) and "us"/"ns" into unknown units.
            string unit = parts.Length > 1 ? parts[1].TrimEnd(')') : "s";
            return ConvertHalfLifeToSeconds(value, unit) / 31536000;
        }

        private static double ConvertHalfLifeToSeconds(double value, string unit)
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
            // Галочка стоит у того, за чем пришли: у гамма-линий распада и у
            // всех линий рентгена, когда искали именно рентген элемента.
            bool isGamma = decrad.DecayLine == "G"
                           || decrad.DecayLine == NucBaseFramework.FluorescenceLine;
            // ⛔ Kβ лежит в базе ДВАЖДЫ — итогом `KB` и разложением `KpB1`+`KpB2`
            // (`D33`). У лишней при сложении половины в колонке серии стоит знак
            // суммы: складывать её с соседями значит считать Kβ дважды, на
            // Lu-176 это 40.53 % вместо 33.49 %.
            //
            // ⚠ Галочку здесь снимать не надо и НЕ НАДО ПИСАТЬ КОД, который её
            // снимает: у линий распада типа `X` она и так не ставится — стоит
            // она у гамм и у рентгена ЭЛЕМЕНТА, когда искали именно его. Ветка
            // «если лишняя, снять» была бы кодом, который никогда не работает.
            string series = decrad.XrayType + (decrad.Redundant ? DecayRad.RedundantMark : "");
            string hl = decrad.HalfLife.ToString() + "(" + decrad.HalfLifeUnit + ")";
            int index = this.ResultDataGridView.Rows.Add(isGamma, decrad.Name, decrad.DecayLine, decrad.Energy, decrad.Intensity, series, decrad.DecayTypeString, hl);
            if (decrad.Redundant)
            {
                DataGridViewRow added = this.ResultDataGridView.Rows[index];
                added.Cells[SeriesColumnIdx].ToolTipText = Resources.NucBase_KSeriesRedundantHint;
                added.DefaultCellStyle.ForeColor = System.Drawing.SystemColors.GrayText;
            }
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
            if (nuc == null)
            {
                // Stable isotope (no half-life row) - nothing to display.
                return;
            }
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
                if (nuc == null)
                {
                    // Stable isotope (no half-life row) - nothing to display.
                    return;
                }
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
                int redundantSkipped = 0;
                NuclideDefinitionManager defManager = NuclideDefinitionManager.GetInstance();
                // Ряд у всех ввозимых линий один — тот, по которому шёл поиск.
                // Пишется НЕЗАВИСИМО от «дописать имя родителя»: та галочка
                // решает, как линия подписана на графике, а поле — на чей
                // распад дан выход. Раньше это было одно и то же, и выключенная
                // галочка молча теряла принадлежность к ряду.
                string chain = this.IncludeDecayChainCheckBox.Checked
                               && !string.IsNullOrEmpty(this.SearchedIsotope)
                    ? FormatIsotopeName(this.SearchedIsotope)
                    : "";
                foreach (DataGridViewRow row in this.ResultDataGridView.Rows)
                {
                    if ((bool)row.Cells[CheckedColumnIdx].Value == true)
                    {
                        // ⛔ Обе половины Kβ вместе НЕ ВЫГРУЖАЮТСЯ (`D33`): это
                        // не запрет взять помеченную строку, а запрет взять её
                        // ВМЕСТЕ с теми, чью сумму она и есть. Молча пропустить
                        // нельзя — считается и говорится вслух.
                        if (IsRedundantSeries(row) && HasCheckedCounterpart(row))
                        {
                            redundantSkipped++;
                            continue;
                        }

                        string name = (string)row.Cells[NameColumnIdx].Value;
                        bool fluorescence = NucBaseFramework.FluorescenceLine.Equals(
                            row.Cells[LineColumnIdx].Value as string, StringComparison.Ordinal);
                        // Формат имени — про нуклиды («137CS» -> «Cs-137»), у
                        // символа элемента ему не за что зацепиться. Подпись
                        // складывается своя: «W x-ray». Слово в ней не украшение
                        // — по отсутствию массового числа в имени рентген и
                        // отличается потом от нуклида (NuclideDefinition).
                        string formattedName = fluorescence
                            ? XrayDefinitionName(name)
                            : FormatIsotopeName(name);
                        double energy = (double)row.Cells[EnergyColumnIdx].Value;
                        double intencity = (double)row.Cells[IntencityColumnIdx].Value;
                        double halfLifeYears = HalfLifeYearsFromCell(
                            (string)row.Cells[HalfLifeColumnIdx].Value);

                        if (!fluorescence && IncludeDecayChainCheckBox.Checked
                            && checkBoxAppendRootName.Checked && this.SearchedIsotope != name)
                        {
                            formattedName += " (" + FormatIsotopeName(this.SearchedIsotope) + ")";
                        }

                        // Ряда у рентгена нет: выход дан не на распад родителя, а
                        // долей внутри K-серии, и вписанный сюда родитель означал
                        // бы, что линию можно ставить на вековое равновесие.
                        string rowChain = fluorescence ? "" : chain;

                        NuclideDefinition existingDef = defManager.NuclideDefinitions.FirstOrDefault(def => def.Energy == energy);
                        if (existingDef != null && checkBoxOverwriteDef.Checked)
                        {
                            existingDef.Name = formattedName;
                            existingDef.Intencity = intencity;
                            existingDef.HalfLife = halfLifeYears;
                            existingDef.Chain = rowChain;
                            updatedCount++;
                        }

                        if (existingDef == null)
                        {
                            defManager.NuclideDefinitions.Add(new NuclideDefinition()
                            {
                                Name = formattedName,
                                Chain = rowChain,
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
                    string text = string.Format(Resources.NuclideDefImportSuccess, createdCount, updatedCount);
                    if (redundantSkipped > 0)
                    {
                        text += Environment.NewLine + Environment.NewLine
                                + string.Format(Resources.NucBase_KSeriesRedundantSkipped, redundantSkipped);
                    }

                    MessageBox.Show(text);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Resources.NuclideDefImportError, ex.Message + ex.StackTrace));
            }
        }

        /// <summary>Строка помечена как лишняя при сложении (`D33`).</summary>
        static bool IsRedundantSeries(DataGridViewRow row)
        {
            string series = row.Cells[SeriesColumnIdx].Value as string;
            return series != null && series.EndsWith(DecayRad.RedundantMark, StringComparison.Ordinal);
        }

        /// <summary>
        /// Отмечена ли у того же родителя и того же типа распада ХОТЬ ОДНА
        /// строка той же Kβ с другой стороны — то есть та, чью сумму помеченная
        /// строка и представляет. Без этой проверки запрет был бы шире, чем
        /// нужно: взять помеченную строку ОДНУ никто не мешает.
        /// </summary>
        bool HasCheckedCounterpart(DataGridViewRow marked)
        {
            string name = marked.Cells[NameColumnIdx].Value as string;
            string decay = marked.Cells[DecayTypeColumnIdx].Value as string;
            string series = (marked.Cells[SeriesColumnIdx].Value as string) ?? "";
            bool markedIsTotal = series.StartsWith(FullSpectrumAnalysis.KSeriesRule.BetaTotal,
                                                   StringComparison.Ordinal);
            foreach (DataGridViewRow row in this.ResultDataGridView.Rows)
            {
                if (ReferenceEquals(row, marked) || !(row.Cells[CheckedColumnIdx].Value is bool)
                    || !(bool)row.Cells[CheckedColumnIdx].Value)
                {
                    continue;
                }

                if (!string.Equals(row.Cells[NameColumnIdx].Value as string, name, StringComparison.Ordinal)
                    || !string.Equals(row.Cells[DecayTypeColumnIdx].Value as string, decay, StringComparison.Ordinal))
                {
                    continue;
                }

                string other = (row.Cells[SeriesColumnIdx].Value as string) ?? "";
                bool otherIsTotal = other.StartsWith(FullSpectrumAnalysis.KSeriesRule.BetaTotal,
                                                    StringComparison.Ordinal);
                bool otherIsSplit = other.StartsWith("Kp", StringComparison.Ordinal);
                if (markedIsTotal ? otherIsSplit : otherIsTotal)
                {
                    return true;
                }
            }

            return false;
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
