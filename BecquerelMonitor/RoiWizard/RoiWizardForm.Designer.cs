namespace BecquerelMonitor.RoiWizard
{
    partial class RoiWizardForm
    {
        System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        // Разметка собрана руками, без дизайнера: три вкладки повторяют шаги веб-версии.
        // Подписи заданы по-английски — базовый язык интерфейса BecqMoni; русский
        // накладывается в RoiWizardForm.ApplyRussian() по текущей культуре UI.
        void InitializeComponent()
        {
            this.tabs = new System.Windows.Forms.TabControl();
            this.tabSources = new System.Windows.Forms.TabPage();
            this.tabLines = new System.Windows.Forms.TabPage();
            this.tabExport = new System.Windows.Forms.TabPage();

            // — шаг 1: поиск и группы
            this.groupSearch = new System.Windows.Forms.GroupBox();
            this.textSearch = new System.Windows.Forms.TextBox();
            this.buttonAddSingle = new System.Windows.Forms.Button();
            this.buttonAddFamily = new System.Windows.Forms.Button();
            this.buttonAddChain = new System.Windows.Forms.Button();
            this.tableCatalog = new XPTable.Models.Table();
            this.columnModelCatalog = new XPTable.Models.ColumnModel();
            this.columnCatalogName = new XPTable.Models.TextColumn();
            this.columnCatalogFamilies = new XPTable.Models.TextColumn();
            this.columnCatalogHalfLife = new XPTable.Models.TextColumn();
            this.columnCatalogHalfLife.Comparer = typeof(XPTable.Sorting.NumberComparer);
            this.columnCatalogLines = new XPTable.Models.TextColumn();
            this.columnCatalogLines.Comparer = typeof(XPTable.Sorting.NumberComparer);
            this.tableModelCatalog = new XPTable.Models.TableModel();

            this.groupGroup = new System.Windows.Forms.GroupBox();
            this.comboGroup = new System.Windows.Forms.ComboBox();
            this.buttonGroupAll = new System.Windows.Forms.Button();
            this.buttonGroupFamily = new System.Windows.Forms.Button();
            this.buttonGroupChain = new System.Windows.Forms.Button();
            this.checkedGroup = new System.Windows.Forms.CheckedListBox();
            this.buttonFamilyInfo = new System.Windows.Forms.Button();
            this.labelFamilyInfo = new System.Windows.Forms.Label();
            this.labelSearchHint = new System.Windows.Forms.Label();
            this.panelPresets = new System.Windows.Forms.FlowLayoutPanel();
            this.labelXrfHint = new System.Windows.Forms.Label();
            this.labelGroupHint = new System.Windows.Forms.Label();
            this.groupXrf = new System.Windows.Forms.GroupBox();
            this.checkedXrf = new System.Windows.Forms.CheckedListBox();
            this.labelXrf = new System.Windows.Forms.Label();

            this.groupSelected = new System.Windows.Forms.GroupBox();
            this.panelSelected = new System.Windows.Forms.FlowLayoutPanel();
            this.buttonClear = new System.Windows.Forms.Button();

            // — шаг 2: разрешение, слияние, фильтры, таблица линий
            this.groupResolution = new System.Windows.Forms.GroupBox();
            this.labelResolution = new System.Windows.Forms.Label();
            this.numResolution = new System.Windows.Forms.NumericUpDown();
            this.buttonFromSpectrum = new System.Windows.Forms.Button();
            this.labelCriterion = new System.Windows.Forms.Label();
            this.comboCriterion = new System.Windows.Forms.ComboBox();
            this.numFactor = new System.Windows.Forms.NumericUpDown();
            this.labelFactor = new System.Windows.Forms.Label();
            this.buttonMerge = new System.Windows.Forms.Button();
            this.buttonUnmerge = new System.Windows.Forms.Button();
            this.labelMergeInfo = new System.Windows.Forms.Label();

            this.groupFilters = new System.Windows.Forms.GroupBox();
            this.checkIntensity = new System.Windows.Forms.CheckBox();
            this.numMinIntensity = new System.Windows.Forms.NumericUpDown();
            this.comboIntensityMode = new System.Windows.Forms.ComboBox();
            this.checkEnergy = new System.Windows.Forms.CheckBox();
            this.numMinEnergy = new System.Windows.Forms.NumericUpDown();
            this.numMaxEnergy = new System.Windows.Forms.NumericUpDown();
            this.checkHalfLife = new System.Windows.Forms.CheckBox();
            this.numMinHalfLife = new System.Windows.Forms.NumericUpDown();
            this.comboMinHalfLifeUnit = new System.Windows.Forms.ComboBox();
            this.numMaxHalfLife = new System.Windows.Forms.NumericUpDown();
            this.comboMaxHalfLifeUnit = new System.Windows.Forms.ComboBox();
            this.checkHideUnselected = new System.Windows.Forms.CheckBox();
            this.labelTypes = new System.Windows.Forms.Label();
            this.checkTypeGamma = new System.Windows.Forms.CheckBox();
            this.checkTypeXray = new System.Windows.Forms.CheckBox();
            this.checkTypeXrf = new System.Windows.Forms.CheckBox();
            this.checkTypeSecondary = new System.Windows.Forms.CheckBox();
            this.checkEquilibrium = new System.Windows.Forms.CheckBox();
            this.groupSecondary = new System.Windows.Forms.GroupBox();
            this.labelSecondaryMin = new System.Windows.Forms.Label();
            this.numSecondaryMin = new System.Windows.Forms.NumericUpDown();
            this.checkSecBackscatter = new System.Windows.Forms.CheckBox();
            this.checkSecComptonEdge = new System.Windows.Forms.CheckBox();
            this.checkSecSingleEscape = new System.Windows.Forms.CheckBox();
            this.checkSecDoubleEscape = new System.Windows.Forms.CheckBox();
            this.checkSecIodine = new System.Windows.Forms.CheckBox();
            this.checkSecAnnihilation = new System.Windows.Forms.CheckBox();
            this.checkSecSum = new System.Windows.Forms.CheckBox();
            this.checkSecPileUp = new System.Windows.Forms.CheckBox();
            this.buttonGenerateSecondary = new System.Windows.Forms.Button();
            this.groupNear = new System.Windows.Forms.GroupBox();
            this.labelNearEnergy = new System.Windows.Forms.Label();
            this.numNearEnergy = new System.Windows.Forms.NumericUpDown();
            this.labelNearWindow = new System.Windows.Forms.Label();
            this.numNearWindow = new System.Windows.Forms.NumericUpDown();
            this.labelNearIntensity = new System.Windows.Forms.Label();
            this.numNearIntensity = new System.Windows.Forms.NumericUpDown();
            this.labelNearHalfLife = new System.Windows.Forms.Label();
            this.numNearHalfLife = new System.Windows.Forms.NumericUpDown();
            this.comboNearHalfLifeUnit = new System.Windows.Forms.ComboBox();
            this.buttonNearSearch = new System.Windows.Forms.Button();
            this.labelNearHint = new System.Windows.Forms.Label();
            this.tableNear = new XPTable.Models.Table();
            this.columnModelNear = new XPTable.Models.ColumnModel();
            this.columnNearDelta = new XPTable.Models.TextColumn();
            this.columnNearDelta.Comparer = typeof(XPTable.Sorting.NumberComparer);
            this.columnNearName = new XPTable.Models.TextColumn();
            this.columnNearEnergy = new XPTable.Models.TextColumn();
            this.columnNearEnergy.Comparer = typeof(XPTable.Sorting.NumberComparer);
            this.columnNearIntensity = new XPTable.Models.TextColumn();
            this.columnNearIntensity.Comparer = typeof(XPTable.Sorting.NumberComparer);
            this.columnNearType = new XPTable.Models.TextColumn();
            this.columnNearHalfLife = new XPTable.Models.TextColumn();
            this.columnNearHalfLife.Comparer = typeof(XPTable.Sorting.NumberComparer);
            this.columnNearAdd = new XPTable.Models.ButtonColumn();
            this.columnNearFill = new XPTable.Models.TextColumn();
            this.tableModelNear = new XPTable.Models.TableModel();
            this.buttonSelectAll = new System.Windows.Forms.Button();
            this.buttonSelectNone = new System.Windows.Forms.Button();
            this.numTopN = new System.Windows.Forms.NumericUpDown();
            this.labelTopN = new System.Windows.Forms.Label();
            this.buttonSelectTop = new System.Windows.Forms.Button();

            this.tableLines = new XPTable.Models.Table();
            this.columnModelLines = new XPTable.Models.ColumnModel();
            this.columnLineSelected = new XPTable.Models.CheckBoxColumn();
            this.columnLineName = new XPTable.Models.TextColumn();
            this.columnLineEnergy = new XPTable.Models.TextColumn();
            this.columnLineEnergy.Comparer = typeof(XPTable.Sorting.NumberComparer);
            this.columnLineIntensity = new XPTable.Models.TextColumn();
            this.columnLineIntensity.Comparer = typeof(XPTable.Sorting.NumberComparer);
            this.columnLineRelative = new XPTable.Models.TextColumn();
            this.columnLineRelative.Comparer = typeof(XPTable.Sorting.NumberComparer);
            this.columnLineHalfLife = new XPTable.Models.TextColumn();
            this.columnLineHalfLife.Comparer = typeof(XPTable.Sorting.NumberComparer);
            this.columnLineType = new XPTable.Models.TextColumn();
            this.tableModelLines = new XPTable.Models.TableModel();

            // — шаг 3: оформление и экспорт
            this.groupStyle = new System.Windows.Forms.GroupBox();
            this.labelStyle = new System.Windows.Forms.Label();
            this.comboStyle = new System.Windows.Forms.ComboBox();
            this.labelWidth = new System.Windows.Forms.Label();
            this.comboWidthMode = new System.Windows.Forms.ComboBox();
            this.numZonePercent = new System.Windows.Forms.NumericUpDown();
            this.numZoneFactor = new System.Windows.Forms.NumericUpDown();
            this.labelColors = new System.Windows.Forms.Label();
            this.buttonColorByChain = new System.Windows.Forms.Button();
            this.buttonColorByNuclide = new System.Windows.Forms.Button();
            this.panelColors = new System.Windows.Forms.FlowLayoutPanel();

            this.groupExport = new System.Windows.Forms.GroupBox();
            this.labelConfigName = new System.Windows.Forms.Label();
            this.textConfigName = new System.Windows.Forms.TextBox();
            this.buttonCreateRoi = new System.Windows.Forms.Button();
            this.buttonPreview = new System.Windows.Forms.Button();
            this.textPreview = new System.Windows.Forms.TextBox();
            this.labelSetName = new System.Windows.Forms.Label();
            this.textSetName = new System.Windows.Forms.TextBox();
            this.labelAnchor = new System.Windows.Forms.Label();
            this.comboAnchor = new System.Windows.Forms.ComboBox();
            this.buttonCreateSet = new System.Windows.Forms.Button();
            this.checkFullSet = new System.Windows.Forms.CheckBox();
            this.labelAnchorCount = new System.Windows.Forms.Label();
            this.numAnchors = new System.Windows.Forms.NumericUpDown();
            this.listIssues = new System.Windows.Forms.ListBox();
            this.labelIssues = new System.Windows.Forms.Label();

            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.statusLabel = new System.Windows.Forms.ToolStripStatusLabel();
            this.buttonHelp = new System.Windows.Forms.ToolStripButton();
            this.buttonStepPrev = new System.Windows.Forms.ToolStripButton();
            this.buttonStepNext = new System.Windows.Forms.ToolStripButton();

            ((System.ComponentModel.ISupportInitialize)(this.numResolution)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFactor)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinIntensity)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinEnergy)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxEnergy)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTopN)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinHalfLife)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxHalfLife)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSecondaryMin)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numNearEnergy)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numNearWindow)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numNearIntensity)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numNearHalfLife)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numZonePercent)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numZoneFactor)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAnchors)).BeginInit();
            this.SuspendLayout();

            // ─── вкладки ───────────────────────────────────────────────────
            // размер задаётся до наполнения страниц: иначе дети запомнят расстояния
            // до краёв страницы размером 200x100 по умолчанию и на реальном разъедутся
            this.tabs.Size = new System.Drawing.Size(1180, 598);
            this.tabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabs.Controls.Add(this.tabSources);
            this.tabs.Controls.Add(this.tabLines);
            this.tabs.Controls.Add(this.tabExport);

            // размер каждой странице явно: TabControl размечает только выбранную,
            // остальные остаются 200x100 и портят привязки своих детей
            this.tabSources.Size = new System.Drawing.Size(1172, 572);
            this.tabLines.Size = new System.Drawing.Size(1172, 572);
            this.tabExport.Size = new System.Drawing.Size(1172, 572);
            this.tabSources.Text = RoiWizardStrings.tabSources_Text;
            this.tabSources.Padding = new System.Windows.Forms.Padding(6);
            this.tabSources.UseVisualStyleBackColor = true;
            this.tabLines.Text = RoiWizardStrings.tabLines_Text;
            this.tabLines.Padding = new System.Windows.Forms.Padding(6);
            this.tabLines.UseVisualStyleBackColor = true;
            this.tabExport.Text = RoiWizardStrings.tabExport_Text;
            this.tabExport.Padding = new System.Windows.Forms.Padding(6);
            this.tabExport.UseVisualStyleBackColor = true;

            // ─── шаг 1 ─────────────────────────────────────────────────────
            this.groupSearch.Text = RoiWizardStrings.groupSearch_Text;
            this.groupSearch.Location = new System.Drawing.Point(8, 6);
            this.groupSearch.Size = new System.Drawing.Size(376, 340);
            this.groupSearch.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;

            this.textSearch.Location = new System.Drawing.Point(8, 20);
            this.textSearch.Size = new System.Drawing.Size(360, 21);
            this.textSearch.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.buttonAddSingle.Text = RoiWizardStrings.buttonAddSingle_Text;
            this.buttonAddSingle.Location = new System.Drawing.Point(8, 48);
            this.buttonAddSingle.Size = new System.Drawing.Size(104, 25);
            this.buttonAddFamily.Text = RoiWizardStrings.buttonAddFamily_Text;
            this.buttonAddFamily.Location = new System.Drawing.Point(118, 48);
            this.buttonAddFamily.Size = new System.Drawing.Size(122, 25);
            this.buttonAddChain.Text = RoiWizardStrings.buttonAddChain_Text;
            this.buttonAddChain.Location = new System.Drawing.Point(246, 48);
            this.buttonAddChain.Size = new System.Drawing.Size(122, 25);

            this.tableCatalog.Location = new System.Drawing.Point(8, 80);
            this.tableCatalog.Size = new System.Drawing.Size(360, 190);
            this.tableCatalog.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            this.tableCatalog.BorderColor = System.Drawing.Color.Black;
            this.tableCatalog.ColumnModel = this.columnModelCatalog;
            this.tableCatalog.FullRowSelect = true;
            this.tableCatalog.GridLines = XPTable.Models.GridLines.Rows;
            this.tableCatalog.TableModel = this.tableModelCatalog;
            this.tableCatalog.HeaderRenderer = new CenteredHeaderRenderer();
            // строка списка нуклидов повторяет .nuc на странице: имя, бейджи семейств,
            // приглушённый хвост «T½ γN XN». Высота 18 px — line-height 16 плюс padding.
            this.tableModelCatalog.RowHeight = 18;
            this.columnCatalogName.Editable = false;   // таблицы только для чтения: правки идут через контролы
            this.columnCatalogName.Text = RoiWizardStrings.columnCatalogName_Text;
            this.columnCatalogName.Width = 72;
            this.columnCatalogFamilies.Editable = false;
            this.columnCatalogFamilies.Text = RoiWizardStrings.columnCatalogFamilies_Text;
            this.columnCatalogFamilies.Width = 132;
            this.columnCatalogFamilies.Renderer = new FamilyBadgeCellRenderer();
            this.columnCatalogHalfLife.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.columnCatalogHalfLife.Editable = false;
            this.columnCatalogHalfLife.Text = "T½";
            this.columnCatalogHalfLife.Width = 78;
            this.columnCatalogHalfLife.Renderer = new HintCellRenderer();
            this.columnCatalogLines.Editable = false;
            this.columnCatalogLines.Text = RoiWizardStrings.columnCatalogLines_Text;
            this.columnCatalogLines.Width = 56;
            this.columnCatalogLines.Renderer = new LineCountCellRenderer();
            this.columnModelCatalog.Columns.AddRange(new XPTable.Models.Column[] {
                this.columnCatalogName, this.columnCatalogFamilies,
                this.columnCatalogHalfLife, this.columnCatalogLines });

            this.groupSearch.Controls.Add(this.textSearch);
            this.groupSearch.Controls.Add(this.buttonAddSingle);
            this.groupSearch.Controls.Add(this.buttonAddFamily);
            this.groupSearch.Controls.Add(this.buttonAddChain);
            this.labelSearchHint.Text = RoiWizardStrings.labelSearchHint_Text;
            this.labelSearchHint.Location = new System.Drawing.Point(8, 274);
            this.labelSearchHint.Size = new System.Drawing.Size(360, 16);
            this.labelSearchHint.Anchor = System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            // строка пресетов: готовые наборы одним щелчком, как .presets на странице
            this.panelPresets.Location = new System.Drawing.Point(6, 292);
            this.panelPresets.Size = new System.Drawing.Size(364, 44);
            this.panelPresets.WrapContents = true;   // .presets переносится: flex-wrap:wrap
            this.panelPresets.AutoScroll = false;
            this.panelPresets.Anchor = System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.groupSearch.Controls.Add(this.tableCatalog);
            this.groupSearch.Controls.Add(this.labelSearchHint);
            this.groupSearch.Controls.Add(this.panelPresets);

            this.groupGroup.Text = RoiWizardStrings.groupGroup_Text;
            this.groupGroup.Location = new System.Drawing.Point(392, 6);
            this.groupGroup.Size = new System.Drawing.Size(376, 340);
            this.groupGroup.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left;
            this.comboGroup.Location = new System.Drawing.Point(8, 22);
            this.comboGroup.Size = new System.Drawing.Size(330, 23);
            this.comboGroup.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboGroup.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.buttonFamilyInfo.Text = "i";
            this.buttonFamilyInfo.Location = new System.Drawing.Point(342, 22);
            this.buttonFamilyInfo.Size = new System.Drawing.Size(26, 23);
            this.buttonFamilyInfo.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Right;
            // словарик кодов — поверх списка, чтобы не двигать вёрстку (.infoPop)
            this.labelFamilyInfo.Location = new System.Drawing.Point(8, 47);
            this.labelFamilyInfo.Size = new System.Drawing.Size(360, 158);
            this.labelFamilyInfo.BackColor = System.Drawing.Color.FromArgb(255, 255, 225);
            this.labelFamilyInfo.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.labelFamilyInfo.Padding = new System.Windows.Forms.Padding(6, 5, 6, 5);
            this.labelFamilyInfo.Visible = false;
            this.labelFamilyInfo.Cursor = System.Windows.Forms.Cursors.Hand;
            this.labelFamilyInfo.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.buttonGroupAll.Text = RoiWizardStrings.buttonGroupAll_Text;
            this.buttonGroupAll.Location = new System.Drawing.Point(8, 50);
            this.buttonGroupAll.Size = new System.Drawing.Size(104, 25);
            this.buttonGroupFamily.Text = RoiWizardStrings.buttonGroupFamily_Text;
            this.buttonGroupFamily.Location = new System.Drawing.Point(118, 50);
            this.buttonGroupFamily.Size = new System.Drawing.Size(140, 25);
            this.buttonGroupChain.Text = RoiWizardStrings.buttonGroupChain_Text;
            this.buttonGroupChain.Location = new System.Drawing.Point(264, 50);
            this.buttonGroupChain.Size = new System.Drawing.Size(104, 25);
            this.checkedGroup.Location = new System.Drawing.Point(8, 82);
            this.checkedGroup.Size = new System.Drawing.Size(360, 230);
            this.checkedGroup.CheckOnClick = true;
            this.checkedGroup.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            this.labelGroupHint.Text = "Tick a nuclide - the buttons apply to it.";
            this.labelGroupHint.Location = new System.Drawing.Point(8, 316);
            this.labelGroupHint.Size = new System.Drawing.Size(360, 18);
            this.labelGroupHint.Anchor = System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.groupXrf.Text = RoiWizardStrings.groupXrf_Text;
            this.groupXrf.Location = new System.Drawing.Point(776, 6);
            this.groupXrf.Size = new System.Drawing.Size(396, 340);
            this.groupXrf.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            this.labelXrf.Text = RoiWizardStrings.labelXrf_Text;
            this.labelXrf.Location = new System.Drawing.Point(8, 20);
            this.labelXrf.AutoSize = true;
            this.checkedXrf.Location = new System.Drawing.Point(8, 44);
            this.checkedXrf.Size = new System.Drawing.Size(380, 258);
            this.checkedXrf.HorizontalScrollbar = true;
            this.labelXrfHint.Text = RoiWizardStrings.labelXrfHint_Text;
            this.labelXrfHint.Location = new System.Drawing.Point(8, 306);
            this.labelXrfHint.Size = new System.Drawing.Size(380, 28);
            this.labelXrfHint.Anchor = System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.checkedXrf.CheckOnClick = true;
            this.checkedXrf.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            this.groupGroup.Controls.Add(this.labelFamilyInfo);
            this.groupGroup.Controls.Add(this.comboGroup);
            this.groupGroup.Controls.Add(this.buttonFamilyInfo);
            this.groupGroup.Controls.Add(this.buttonGroupAll);
            this.groupGroup.Controls.Add(this.buttonGroupFamily);
            this.groupGroup.Controls.Add(this.buttonGroupChain);
            this.groupGroup.Controls.Add(this.checkedGroup);
            this.groupGroup.Controls.Add(this.labelGroupHint);
            this.groupXrf.Controls.Add(this.labelXrf);
            this.groupXrf.Controls.Add(this.checkedXrf);
            this.groupXrf.Controls.Add(this.labelXrfHint);

            this.groupSelected.Text = RoiWizardStrings.groupSelected_Text;
            this.groupSelected.Location = new System.Drawing.Point(8, 352);
            this.groupSelected.Size = new System.Drawing.Size(1156, 72);
            this.groupSelected.Anchor = System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.panelSelected.Location = new System.Drawing.Point(8, 18);
            this.panelSelected.Size = new System.Drawing.Size(1038, 48);
            this.panelSelected.AutoScroll = true;
            this.panelSelected.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            this.buttonClear.Text = RoiWizardStrings.buttonClear_Text;
            this.buttonClear.Location = new System.Drawing.Point(1054, 18);
            this.buttonClear.Size = new System.Drawing.Size(94, 25);
            this.buttonClear.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Right;
            this.groupSelected.Controls.Add(this.panelSelected);
            this.groupSelected.Controls.Add(this.buttonClear);

            this.tabSources.Controls.Add(this.groupSearch);
            this.tabSources.Controls.Add(this.groupGroup);
            this.tabSources.Controls.Add(this.groupXrf);
            this.tabSources.Controls.Add(this.groupSelected);

            // ─── шаг 2 ─────────────────────────────────────────────────────
            this.groupResolution.Text = RoiWizardStrings.groupResolution_Text;
            this.groupResolution.Location = new System.Drawing.Point(8, 6);
            this.groupResolution.Size = new System.Drawing.Size(1156, 80);
            this.groupResolution.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.labelResolution.Text = RoiWizardStrings.labelResolution_Text;
            this.labelResolution.Location = new System.Drawing.Point(8, 23);
            this.labelResolution.AutoSize = true;
            this.numResolution.Location = new System.Drawing.Point(102, 20);
            this.numResolution.Size = new System.Drawing.Size(56, 21);
            this.numResolution.DecimalPlaces = 1;
            this.numResolution.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            this.numResolution.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            this.numResolution.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            this.numResolution.Value = new decimal(new int[] { 75, 0, 0, 65536 });
            this.buttonFromSpectrum.Text = RoiWizardStrings.buttonFromSpectrum_Text;
            this.buttonFromSpectrum.Location = new System.Drawing.Point(164, 19);
            this.buttonFromSpectrum.Size = new System.Drawing.Size(104, 23);
            this.labelCriterion.Text = RoiWizardStrings.labelCriterion_Text;
            this.labelCriterion.Location = new System.Drawing.Point(276, 23);
            this.labelCriterion.AutoSize = true;
            this.comboCriterion.Location = new System.Drawing.Point(358, 20);
            this.comboCriterion.Size = new System.Drawing.Size(300, 23);
            this.comboCriterion.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.numFactor.Location = new System.Drawing.Point(666, 20);
            this.numFactor.Size = new System.Drawing.Size(56, 21);
            this.numFactor.DecimalPlaces = 2;
            this.numFactor.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            this.numFactor.Minimum = new decimal(new int[] { 5, 0, 0, 131072 });
            this.numFactor.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.numFactor.Value = new decimal(new int[] { 85, 0, 0, 131072 });
            this.labelFactor.Text = RoiWizardStrings.labelFactor_Text;
            this.labelFactor.Location = new System.Drawing.Point(728, 23);
            this.labelFactor.AutoSize = true;
            this.buttonMerge.Text = RoiWizardStrings.buttonMerge_Text;
            this.buttonMerge.Location = new System.Drawing.Point(838, 19);
            this.buttonMerge.Size = new System.Drawing.Size(150, 25);
            this.buttonUnmerge.Text = RoiWizardStrings.buttonUnmerge_Text;
            this.buttonUnmerge.Location = new System.Drawing.Point(996, 19);
            this.buttonUnmerge.Size = new System.Drawing.Size(158, 25);
            this.groupResolution.Controls.Add(this.labelResolution);
            this.groupResolution.Controls.Add(this.numResolution);
            this.groupResolution.Controls.Add(this.buttonFromSpectrum);
            this.groupResolution.Controls.Add(this.labelCriterion);
            this.groupResolution.Controls.Add(this.comboCriterion);
            this.groupResolution.Controls.Add(this.numFactor);
            this.groupResolution.Controls.Add(this.labelFactor);
            this.groupResolution.Controls.Add(this.buttonMerge);
            this.groupResolution.Controls.Add(this.buttonUnmerge);
            this.groupResolution.Controls.Add(this.labelMergeInfo);

            this.labelMergeInfo.Location = new System.Drawing.Point(10, 52);
            this.labelMergeInfo.Size = new System.Drawing.Size(1136, 18);
            this.labelMergeInfo.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;

            this.groupFilters.Text = RoiWizardStrings.groupFilters_Text;
            this.groupFilters.Location = new System.Drawing.Point(8, 92);
            this.groupFilters.Size = new System.Drawing.Size(1156, 106);
            this.groupFilters.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.checkIntensity.Text = RoiWizardStrings.checkIntensity_Text;
            this.checkIntensity.Location = new System.Drawing.Point(8, 21);
            this.checkIntensity.Size = new System.Drawing.Size(124, 20);
            this.numMinIntensity.Location = new System.Drawing.Point(136, 20);
            this.numMinIntensity.Size = new System.Drawing.Size(52, 21);
            this.numMinIntensity.DecimalPlaces = 1;
            this.numMinIntensity.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numMinIntensity.Value = new decimal(new int[] { 3, 0, 0, 0 });
            this.comboIntensityMode.Location = new System.Drawing.Point(196, 20);
            this.comboIntensityMode.Size = new System.Drawing.Size(292, 23);
            this.comboIntensityMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.checkEnergy.Text = RoiWizardStrings.checkEnergy_Text;
            this.checkEnergy.Location = new System.Drawing.Point(500, 21);
            this.checkEnergy.Size = new System.Drawing.Size(92, 20);
            this.numMinEnergy.Location = new System.Drawing.Point(596, 20);
            this.numMinEnergy.Size = new System.Drawing.Size(60, 21);
            this.numMinEnergy.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            this.numMinEnergy.Value = new decimal(new int[] { 10, 0, 0, 0 });
            this.numMaxEnergy.Location = new System.Drawing.Point(662, 20);
            this.numMaxEnergy.Size = new System.Drawing.Size(60, 21);
            this.numMaxEnergy.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            this.numMaxEnergy.Value = new decimal(new int[] { 3000, 0, 0, 0 });
            // фильтр по периоду полураспада — как в вебе: два поля со своими единицами
            this.checkHalfLife.Text = RoiWizardStrings.checkHalfLife_Text;
            this.checkHalfLife.Location = new System.Drawing.Point(738, 21);
            this.checkHalfLife.Size = new System.Drawing.Size(40, 20);
            this.numMinHalfLife.Location = new System.Drawing.Point(782, 20);
            this.numMinHalfLife.Size = new System.Drawing.Size(52, 21);
            this.numMinHalfLife.DecimalPlaces = 2;
            this.numMinHalfLife.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.numMinHalfLife.Value = new decimal(new int[] { 1, 0, 0, 0 });
            this.comboMinHalfLifeUnit.Location = new System.Drawing.Point(840, 20);
            this.comboMinHalfLifeUnit.Size = new System.Drawing.Size(56, 21);
            this.comboMinHalfLifeUnit.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.numMaxHalfLife.Location = new System.Drawing.Point(906, 20);
            this.numMaxHalfLife.Size = new System.Drawing.Size(52, 21);
            this.numMaxHalfLife.DecimalPlaces = 2;
            this.numMaxHalfLife.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.comboMaxHalfLifeUnit.Location = new System.Drawing.Point(964, 20);
            this.comboMaxHalfLifeUnit.Size = new System.Drawing.Size(56, 21);
            this.comboMaxHalfLifeUnit.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;

            this.checkHideUnselected.Text = RoiWizardStrings.checkHideUnselected_Text;
            this.checkHideUnselected.Location = new System.Drawing.Point(626, 48);
            this.checkHideUnselected.Size = new System.Drawing.Size(180, 20);

            this.checkEquilibrium.Text = RoiWizardStrings.checkEquilibrium_Text;
            this.labelTypes.Text = RoiWizardStrings.labelTypes_Text;
            this.labelTypes.Location = new System.Drawing.Point(8, 75);
            this.labelTypes.Size = new System.Drawing.Size(66, 16);
            this.checkTypeGamma.Text = "γ";
            this.checkTypeGamma.Location = new System.Drawing.Point(78, 73);
            this.checkTypeGamma.Size = new System.Drawing.Size(40, 20);
            this.checkTypeGamma.Checked = true;
            this.checkTypeXray.Text = RoiWizardStrings.checkTypeXray_Text;
            this.checkTypeXray.Location = new System.Drawing.Point(120, 73);
            this.checkTypeXray.Size = new System.Drawing.Size(90, 20);
            this.checkTypeXray.Checked = true;
            this.checkTypeXrf.Text = RoiWizardStrings.checkTypeXrf_Text;
            this.checkTypeXrf.Location = new System.Drawing.Point(212, 73);
            this.checkTypeXrf.Size = new System.Drawing.Size(60, 20);
            this.checkTypeXrf.Checked = true;
            this.checkTypeSecondary.Text = RoiWizardStrings.checkTypeSecondary_Text;
            this.checkTypeSecondary.Location = new System.Drawing.Point(274, 73);
            this.checkTypeSecondary.Size = new System.Drawing.Size(96, 20);
            this.checkTypeSecondary.Checked = true;

            this.checkEquilibrium.Location = new System.Drawing.Point(402, 73);
            this.checkEquilibrium.Size = new System.Drawing.Size(560, 20);
            this.checkEquilibrium.Checked = true;
            this.buttonSelectAll.Text = RoiWizardStrings.buttonSelectAll_Text;
            this.buttonSelectAll.Location = new System.Drawing.Point(8, 46);
            this.buttonSelectAll.Size = new System.Drawing.Size(140, 25);
            this.buttonSelectNone.Text = RoiWizardStrings.buttonSelectNone_Text;
            this.buttonSelectNone.Location = new System.Drawing.Point(152, 46);
            this.buttonSelectNone.Size = new System.Drawing.Size(130, 25);
            this.labelTopN.Text = RoiWizardStrings.labelTopN_Text;
            this.labelTopN.Location = new System.Drawing.Point(292, 50);
            this.labelTopN.Size = new System.Drawing.Size(136, 18);
            this.numTopN.Location = new System.Drawing.Point(432, 47);
            this.numTopN.Size = new System.Drawing.Size(48, 21);
            this.numTopN.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numTopN.Value = new decimal(new int[] { 5, 0, 0, 0 });
            this.buttonSelectTop.Text = RoiWizardStrings.buttonSelectTop_Text;
            this.buttonSelectTop.Location = new System.Drawing.Point(488, 46);
            this.buttonSelectTop.Size = new System.Drawing.Size(126, 25);
            // Панель вторичных пиков повторяет блок веб-версии: порог по родительской
            // линии, восемь видов особенностей и кнопка расчёта. Расчёт по кнопке, а не
            // автоматически: маркеры добавляются к текущему набору линий.
            this.groupSecondary.Text = RoiWizardStrings.groupSecondary_Text;
            this.groupSecondary.Location = new System.Drawing.Point(8, 204);
            this.groupSecondary.Size = new System.Drawing.Size(1156, 78);
            this.groupSecondary.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.labelSecondaryMin.Text = RoiWizardStrings.labelSecondaryMin_Text;
            this.labelSecondaryMin.Location = new System.Drawing.Point(8, 26);
            this.labelSecondaryMin.Size = new System.Drawing.Size(140, 18);
            this.numSecondaryMin.Location = new System.Drawing.Point(152, 23);
            this.numSecondaryMin.Size = new System.Drawing.Size(56, 23);
            this.numSecondaryMin.DecimalPlaces = 1;
            this.numSecondaryMin.Maximum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numSecondaryMin.Value = new decimal(new int[] { 10, 0, 0, 0 });
            this.checkSecBackscatter.Text = RoiWizardStrings.checkSecBackscatter_Text;
            this.checkSecBackscatter.Location = new System.Drawing.Point(224, 24);
            this.checkSecBackscatter.Size = new System.Drawing.Size(150, 20);
            this.checkSecBackscatter.Checked = true;
            this.checkSecComptonEdge.Text = RoiWizardStrings.checkSecComptonEdge_Text;
            this.checkSecComptonEdge.Location = new System.Drawing.Point(380, 24);
            this.checkSecComptonEdge.Size = new System.Drawing.Size(166, 20);
            this.checkSecSingleEscape.Text = RoiWizardStrings.checkSecSingleEscape_Text;
            this.checkSecSingleEscape.Location = new System.Drawing.Point(552, 24);
            this.checkSecSingleEscape.Size = new System.Drawing.Size(146, 20);
            this.checkSecSingleEscape.Checked = true;
            this.checkSecDoubleEscape.Text = RoiWizardStrings.checkSecDoubleEscape_Text;
            this.checkSecDoubleEscape.Location = new System.Drawing.Point(704, 24);
            this.checkSecDoubleEscape.Size = new System.Drawing.Size(156, 20);
            this.checkSecDoubleEscape.Checked = true;
            this.checkSecIodine.Text = RoiWizardStrings.checkSecIodine_Text;
            this.checkSecIodine.Location = new System.Drawing.Point(224, 48);
            this.checkSecIodine.Size = new System.Drawing.Size(190, 20);
            this.checkSecAnnihilation.Text = RoiWizardStrings.checkSecAnnihilation_Text;
            this.checkSecAnnihilation.Location = new System.Drawing.Point(420, 48);
            this.checkSecAnnihilation.Size = new System.Drawing.Size(146, 20);
            this.checkSecSum.Text = RoiWizardStrings.checkSecSum_Text;
            this.checkSecSum.Location = new System.Drawing.Point(572, 48);
            this.checkSecSum.Size = new System.Drawing.Size(180, 20);
            this.checkSecPileUp.Text = RoiWizardStrings.checkSecPileUp_Text;
            this.checkSecPileUp.Location = new System.Drawing.Point(758, 48);
            this.checkSecPileUp.Size = new System.Drawing.Size(120, 20);
            this.buttonGenerateSecondary.Text = RoiWizardStrings.buttonGenerateSecondary_Text;
            this.buttonGenerateSecondary.Location = new System.Drawing.Point(940, 22);
            this.buttonGenerateSecondary.Size = new System.Drawing.Size(150, 25);
            this.groupSecondary.Controls.Add(this.labelSecondaryMin);
            this.groupSecondary.Controls.Add(this.numSecondaryMin);
            this.groupSecondary.Controls.Add(this.checkSecBackscatter);
            this.groupSecondary.Controls.Add(this.checkSecComptonEdge);
            this.groupSecondary.Controls.Add(this.checkSecSingleEscape);
            this.groupSecondary.Controls.Add(this.checkSecDoubleEscape);
            this.groupSecondary.Controls.Add(this.checkSecIodine);
            this.groupSecondary.Controls.Add(this.checkSecAnnihilation);
            this.groupSecondary.Controls.Add(this.checkSecSum);
            this.groupSecondary.Controls.Add(this.checkSecPileUp);
            this.groupSecondary.Controls.Add(this.buttonGenerateSecondary);
            this.groupFilters.Controls.Add(this.checkIntensity);
            this.groupFilters.Controls.Add(this.numMinIntensity);
            this.groupFilters.Controls.Add(this.comboIntensityMode);
            this.groupFilters.Controls.Add(this.checkEnergy);
            this.groupFilters.Controls.Add(this.numMinEnergy);
            this.groupFilters.Controls.Add(this.numMaxEnergy);
            this.groupFilters.Controls.Add(this.checkHalfLife);
            this.groupFilters.Controls.Add(this.numMinHalfLife);
            this.groupFilters.Controls.Add(this.comboMinHalfLifeUnit);
            this.groupFilters.Controls.Add(this.numMaxHalfLife);
            this.groupFilters.Controls.Add(this.comboMaxHalfLifeUnit);
            this.groupFilters.Controls.Add(this.checkHideUnselected);
            this.groupFilters.Controls.Add(this.labelTypes);
            this.groupFilters.Controls.Add(this.checkTypeGamma);
            this.groupFilters.Controls.Add(this.checkTypeXray);
            this.groupFilters.Controls.Add(this.checkTypeXrf);
            this.groupFilters.Controls.Add(this.checkTypeSecondary);
            this.groupFilters.Controls.Add(this.checkEquilibrium);
            this.groupFilters.Controls.Add(this.buttonSelectAll);
            this.groupFilters.Controls.Add(this.buttonSelectNone);
            this.groupFilters.Controls.Add(this.numTopN);
            this.groupFilters.Controls.Add(this.labelTopN);
            this.groupFilters.Controls.Add(this.buttonSelectTop);
            this.tabLines.Controls.Add(this.groupSecondary);

            this.groupNear.Text = RoiWizardStrings.groupNear_Text;
            this.groupNear.Location = new System.Drawing.Point(8, 288);
            this.groupNear.Size = new System.Drawing.Size(1156, 178);
            this.groupNear.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.labelNearEnergy.Text = RoiWizardStrings.labelNearEnergy_Text;
            this.labelNearEnergy.Location = new System.Drawing.Point(8, 26);
            this.labelNearEnergy.Size = new System.Drawing.Size(90, 18);
            this.numNearEnergy.Location = new System.Drawing.Point(102, 23);
            this.numNearEnergy.Size = new System.Drawing.Size(72, 23);
            this.numNearEnergy.DecimalPlaces = 2;
            this.numNearEnergy.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            this.numNearEnergy.Value = new decimal(new int[] { 362, 0, 0, 0 });
            this.labelNearWindow.Text = RoiWizardStrings.labelNearWindow_Text;
            this.labelNearWindow.Location = new System.Drawing.Point(186, 26);
            this.labelNearWindow.Size = new System.Drawing.Size(72, 18);
            this.numNearWindow.Location = new System.Drawing.Point(262, 23);
            this.numNearWindow.Size = new System.Drawing.Size(60, 23);
            this.numNearWindow.DecimalPlaces = 1;
            this.numNearWindow.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numNearWindow.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numNearWindow.Value = new decimal(new int[] { 10, 0, 0, 0 });
            this.labelNearIntensity.Text = RoiWizardStrings.labelNearIntensity_Text;
            this.labelNearIntensity.Location = new System.Drawing.Point(334, 26);
            this.labelNearIntensity.Size = new System.Drawing.Size(50, 18);
            this.numNearIntensity.Location = new System.Drawing.Point(388, 23);
            this.numNearIntensity.Size = new System.Drawing.Size(60, 23);
            this.numNearIntensity.DecimalPlaces = 2;
            this.numNearIntensity.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            this.labelNearHalfLife.Text = RoiWizardStrings.labelNearHalfLife_Text;
            this.labelNearHalfLife.Location = new System.Drawing.Point(460, 26);
            this.labelNearHalfLife.Size = new System.Drawing.Size(44, 18);
            this.numNearHalfLife.Location = new System.Drawing.Point(508, 23);
            this.numNearHalfLife.Size = new System.Drawing.Size(60, 23);
            this.numNearHalfLife.DecimalPlaces = 2;
            this.numNearHalfLife.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            this.comboNearHalfLifeUnit.Location = new System.Drawing.Point(574, 23);
            this.comboNearHalfLifeUnit.Size = new System.Drawing.Size(64, 23);
            this.comboNearHalfLifeUnit.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.buttonNearSearch.Text = RoiWizardStrings.buttonNearSearch_Text;
            this.buttonNearSearch.Location = new System.Drawing.Point(654, 22);
            this.buttonNearSearch.Size = new System.Drawing.Size(110, 25);
            // подсказка о найденном: сколько всего и сколько показано — на странице
            // она стоит под таблицей, здесь встала в свободное место строки фильтров
            this.labelNearHint.Location = new System.Drawing.Point(776, 26);
            this.labelNearHint.Size = new System.Drawing.Size(360, 18);
            this.labelNearHint.Text = "";

            // Результаты — таблица со строкой на линию и кнопкой «+ добавить» в каждой,
            // как на странице: нуклид добавляется прямо из находки, не возвращаясь к шагу 1.
            this.tableNear.Location = new System.Drawing.Point(8, 52);
            this.tableNear.Size = new System.Drawing.Size(1140, 118);
            this.tableNear.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.tableNear.BorderColor = System.Drawing.Color.Black;
            this.tableNear.ColumnModel = this.columnModelNear;
            this.tableNear.TableModel = this.tableModelNear;
            this.tableNear.FullRowSelect = true;
            this.tableNear.GridLines = XPTable.Models.GridLines.Rows;
            this.tableNear.HeaderRenderer = new CenteredHeaderRenderer();
            this.tableModelNear.RowHeight = 20;
            this.columnNearDelta.Editable = false;
            this.columnNearDelta.Text = RoiWizardStrings.columnNearDelta_Text;
            this.columnNearDelta.Width = 60;
            this.columnNearDelta.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.columnNearDelta.Renderer = new NumberCellRenderer();
            this.columnNearName.Editable = false;
            this.columnNearName.Text = RoiWizardStrings.columnLineName_Text;
            this.columnNearName.Width = 200;
            this.columnNearEnergy.Editable = false;
            this.columnNearEnergy.Text = RoiWizardStrings.columnLineEnergy_Text;
            this.columnNearEnergy.Width = 90;
            this.columnNearEnergy.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.columnNearEnergy.Renderer = new NumberCellRenderer();
            this.columnNearIntensity.Editable = false;
            this.columnNearIntensity.Text = RoiWizardStrings.columnLineIntensity_Text;
            this.columnNearIntensity.Width = 90;
            this.columnNearIntensity.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.columnNearIntensity.Renderer = new NumberCellRenderer();
            this.columnNearType.Editable = false;
            this.columnNearType.Text = RoiWizardStrings.columnLineType_Text;
            this.columnNearType.Width = 80;
            this.columnNearType.Renderer = new LineTypeCellRenderer();
            this.columnNearHalfLife.Editable = false;
            this.columnNearHalfLife.Text = RoiWizardStrings.columnLineHalfLife_Text;
            this.columnNearHalfLife.Width = 100;
            this.columnNearHalfLife.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.columnNearHalfLife.Renderer = new NumberCellRenderer();
            this.columnNearAdd.Text = "";
            this.columnNearAdd.Width = 110;
            this.columnNearAdd.Resizable = false;
            this.columnNearAdd.Sortable = false;
            this.columnNearAdd.Renderer = new NearAddCellRenderer();
            // на странице таблица находок шириной по содержимому, а XPTable всегда
            // занимает контрол целиком — лишнее место забирает пустой столбец справа,
            // иначе оно растянуло бы колонку с именем и оторвало числа от подписей
            this.columnNearFill.Text = "";
            this.columnNearFill.Editable = false;
            this.columnNearFill.Sortable = false;
            this.columnNearFill.Width = 40;
            this.columnModelNear.Columns.AddRange(new XPTable.Models.Column[] {
                this.columnNearDelta, this.columnNearName, this.columnNearEnergy,
                this.columnNearIntensity, this.columnNearType, this.columnNearHalfLife,
                this.columnNearAdd, this.columnNearFill });
            this.groupNear.Controls.Add(this.labelNearEnergy);
            this.groupNear.Controls.Add(this.numNearEnergy);
            this.groupNear.Controls.Add(this.labelNearWindow);
            this.groupNear.Controls.Add(this.numNearWindow);
            this.groupNear.Controls.Add(this.labelNearIntensity);
            this.groupNear.Controls.Add(this.numNearIntensity);
            this.groupNear.Controls.Add(this.labelNearHalfLife);
            this.groupNear.Controls.Add(this.numNearHalfLife);
            this.groupNear.Controls.Add(this.comboNearHalfLifeUnit);
            this.groupNear.Controls.Add(this.buttonNearSearch);
            this.groupNear.Controls.Add(this.labelNearHint);
            this.groupNear.Controls.Add(this.tableNear);
            this.tabLines.Controls.Add(this.groupNear);

            this.tableLines.Location = new System.Drawing.Point(8, 416);
            this.tableLines.Size = new System.Drawing.Size(1156, 150);
            this.tableLines.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.tableLines.BorderColor = System.Drawing.Color.Black;
            this.tableLines.ColumnModel = this.columnModelLines;
            this.tableLines.FullRowSelect = true;
            this.tableLines.GridLines = XPTable.Models.GridLines.Rows;
            this.tableLines.TableModel = this.tableModelLines;
            this.tableLines.HeaderRenderer = new CenteredHeaderRenderer();
            // строка 20 px: шрифт темы 9 pt в штатные 15 px не помещается,
            // а на странице строка таблицы 21-22 px
            this.tableModelLines.RowHeight = 20;
            this.columnLineSelected.Resizable = false;
            this.columnLineSelected.Sortable = false;
            this.columnLineSelected.Alignment = XPTable.Models.ColumnAlignment.Center;
            this.columnLineSelected.Text = "✓";
            this.columnLineSelected.Width = 30;
            this.columnLineName.Editable = false;
            this.columnLineName.Text = RoiWizardStrings.columnLineName_Text;
            this.columnLineName.Width = 320;
            this.columnLineEnergy.Editable = false;
            this.columnLineEnergy.Text = RoiWizardStrings.columnLineEnergy_Text;
            this.columnLineEnergy.Width = 90;
            this.columnLineEnergy.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.columnLineEnergy.Renderer = new NumberCellRenderer();
            this.columnLineIntensity.Editable = false;
            this.columnLineIntensity.Text = RoiWizardStrings.columnLineIntensity_Text;
            this.columnLineIntensity.Width = 90;
            this.columnLineIntensity.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.columnLineIntensity.Renderer = new IntensityBarCellRenderer();
            this.columnLineRelative.Editable = false;
            this.columnLineRelative.Text = RoiWizardStrings.columnLineRelative_Text;
            this.columnLineRelative.Width = 80;
            this.columnLineRelative.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.columnLineRelative.Renderer = new NumberCellRenderer();
            this.columnLineHalfLife.Editable = false;
            this.columnLineHalfLife.Text = RoiWizardStrings.columnLineHalfLife_Text;
            this.columnLineHalfLife.Width = 90;
            this.columnLineHalfLife.Alignment = XPTable.Models.ColumnAlignment.Right;
            this.columnLineHalfLife.Renderer = new NumberCellRenderer();
            this.columnLineType.Editable = false;
            this.columnLineType.Renderer = new LineTypeCellRenderer();
            this.columnLineType.Text = RoiWizardStrings.columnLineType_Text;
            this.columnLineType.Width = 80;
            this.columnModelLines.Columns.AddRange(new XPTable.Models.Column[] {
                this.columnLineSelected, this.columnLineName, this.columnLineEnergy,
                this.columnLineIntensity, this.columnLineRelative,
                this.columnLineHalfLife, this.columnLineType });

            this.tabLines.Controls.Add(this.groupResolution);
            this.tabLines.Controls.Add(this.groupFilters);
            this.tabLines.Controls.Add(this.tableLines);

            // ─── шаг 3 ─────────────────────────────────────────────────────
            this.groupStyle.Text = RoiWizardStrings.groupStyle_Text;
            this.groupStyle.Location = new System.Drawing.Point(8, 6);
            this.groupStyle.Size = new System.Drawing.Size(1156, 104);
            this.groupStyle.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.labelStyle.Text = RoiWizardStrings.labelStyle_Text;
            this.labelStyle.Location = new System.Drawing.Point(8, 23);
            this.labelStyle.AutoSize = true;
            this.comboStyle.Location = new System.Drawing.Point(56, 20);
            this.comboStyle.Size = new System.Drawing.Size(260, 21);
            this.comboStyle.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.labelWidth.Text = RoiWizardStrings.labelWidth_Text;
            this.labelWidth.Location = new System.Drawing.Point(330, 23);
            this.labelWidth.AutoSize = true;
            this.comboWidthMode.Location = new System.Drawing.Point(420, 20);
            this.comboWidthMode.Size = new System.Drawing.Size(220, 21);
            this.comboWidthMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.numZonePercent.Location = new System.Drawing.Point(648, 20);
            this.numZonePercent.Size = new System.Drawing.Size(56, 21);
            this.numZonePercent.DecimalPlaces = 1;
            this.numZonePercent.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numZonePercent.Maximum = new decimal(new int[] { 50, 0, 0, 0 });
            this.numZonePercent.Value = new decimal(new int[] { 5, 0, 0, 0 });
            this.numZoneFactor.Location = new System.Drawing.Point(710, 20);
            this.numZoneFactor.Size = new System.Drawing.Size(56, 21);
            this.numZoneFactor.DecimalPlaces = 1;
            this.numZoneFactor.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            this.numZoneFactor.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
            this.numZoneFactor.Value = new decimal(new int[] { 3, 0, 0, 0 });
            this.groupStyle.Controls.Add(this.labelStyle);
            this.groupStyle.Controls.Add(this.comboStyle);
            this.groupStyle.Controls.Add(this.labelWidth);
            this.groupStyle.Controls.Add(this.comboWidthMode);
            this.groupStyle.Controls.Add(this.numZonePercent);
            this.labelColors.Text = RoiWizardStrings.labelColors_Text;
            this.labelColors.Location = new System.Drawing.Point(8, 60);
            this.labelColors.Size = new System.Drawing.Size(70, 18);
            this.buttonColorByChain.Text = RoiWizardStrings.buttonColorByChain_Text;
            this.buttonColorByChain.Location = new System.Drawing.Point(80, 56);
            this.buttonColorByChain.Size = new System.Drawing.Size(110, 25);
            this.buttonColorByNuclide.Text = RoiWizardStrings.buttonColorByNuclide_Text;
            this.buttonColorByNuclide.Location = new System.Drawing.Point(196, 56);
            this.buttonColorByNuclide.Size = new System.Drawing.Size(110, 25);
            // чипы владельцев: цветной квадрат + подпись, клик по квадрату открывает выбор
            this.panelColors.Location = new System.Drawing.Point(316, 56);
            this.panelColors.Size = new System.Drawing.Size(836, 28);
            this.panelColors.AutoScroll = true;
            this.panelColors.WrapContents = false;
            this.groupStyle.Controls.Add(this.numZoneFactor);
            this.groupStyle.Controls.Add(this.labelColors);
            this.groupStyle.Controls.Add(this.buttonColorByChain);
            this.groupStyle.Controls.Add(this.buttonColorByNuclide);
            this.groupStyle.Controls.Add(this.panelColors);

            this.groupExport.Text = RoiWizardStrings.groupExport_Text;
            this.groupExport.Location = new System.Drawing.Point(8, 114);
            this.groupExport.Size = new System.Drawing.Size(1156, 120);
            this.groupExport.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.labelConfigName.Text = RoiWizardStrings.labelConfigName_Text;
            this.labelConfigName.Location = new System.Drawing.Point(8, 23);
            this.labelConfigName.AutoSize = true;
            this.textConfigName.Location = new System.Drawing.Point(148, 20);
            this.textConfigName.Size = new System.Drawing.Size(220, 21);
            this.textConfigName.Text = "IAEA lines";
            this.buttonCreateRoi.Text = RoiWizardStrings.buttonCreateRoi_Text;
            this.buttonCreateRoi.Location = new System.Drawing.Point(376, 19);
            this.buttonCreateRoi.Size = new System.Drawing.Size(180, 23);
            this.buttonPreview.Text = RoiWizardStrings.buttonPreview_Text;
            this.buttonPreview.Location = new System.Drawing.Point(564, 19);
            this.buttonPreview.Size = new System.Drawing.Size(130, 23);
            this.labelSetName.Text = RoiWizardStrings.labelSetName_Text;
            this.labelSetName.Location = new System.Drawing.Point(8, 53);
            this.labelSetName.AutoSize = true;
            this.textSetName.Location = new System.Drawing.Point(148, 50);
            this.textSetName.Size = new System.Drawing.Size(220, 21);
            this.textSetName.Text = RoiWizardStrings.textSetName_Text;
            this.labelAnchor.Text = RoiWizardStrings.labelAnchor_Text;
            this.labelAnchor.Location = new System.Drawing.Point(376, 53);
            this.labelAnchor.AutoSize = true;
            this.comboAnchor.Location = new System.Drawing.Point(468, 50);
            this.comboAnchor.Size = new System.Drawing.Size(278, 21);
            this.comboAnchor.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.buttonCreateSet.Text = RoiWizardStrings.buttonCreateSet_Text;
            this.buttonCreateSet.Location = new System.Drawing.Point(754, 49);
            this.buttonCreateSet.Size = new System.Drawing.Size(192, 23);
            this.checkFullSet.Text = RoiWizardStrings.checkFullSet_Text;
            this.checkFullSet.Location = new System.Drawing.Point(148, 80);
            // AutoSize, а не фиксированные 220 px: подпись несёт числа фильтра
            // («0,7·FWHM, ≥1 %») и в 220 обрезалась на середине, а в русской раскладке
            // она ещё длиннее. Соседи справа отодвинуты, запас до кнопки создания есть.
            this.checkFullSet.AutoSize = true;
            this.labelAnchorCount.Text = RoiWizardStrings.labelAnchorCount_Text;
            this.labelAnchorCount.Location = new System.Drawing.Point(470, 82);
            this.labelAnchorCount.AutoSize = true;
            this.numAnchors.Location = new System.Drawing.Point(540, 79);
            this.numAnchors.Size = new System.Drawing.Size(60, 21);
            this.numAnchors.Minimum = 1;
            this.numAnchors.Maximum = 9;
            this.numAnchors.Value = 3;
            this.groupExport.Controls.Add(this.labelConfigName);
            this.groupExport.Controls.Add(this.textConfigName);
            this.groupExport.Controls.Add(this.buttonCreateRoi);
            this.groupExport.Controls.Add(this.buttonPreview);
            this.groupExport.Controls.Add(this.labelSetName);
            this.groupExport.Controls.Add(this.textSetName);
            this.groupExport.Controls.Add(this.labelAnchor);
            this.groupExport.Controls.Add(this.comboAnchor);
            this.groupExport.Controls.Add(this.buttonCreateSet);
            this.groupExport.Controls.Add(this.checkFullSet);
            this.groupExport.Controls.Add(this.labelAnchorCount);
            this.groupExport.Controls.Add(this.numAnchors);

            this.labelIssues.Text = RoiWizardStrings.labelIssues_Text;
            this.labelIssues.Location = new System.Drawing.Point(12, 240);
            this.labelIssues.AutoSize = true;
            this.listIssues.Location = new System.Drawing.Point(8, 258);
            this.listIssues.Size = new System.Drawing.Size(1156, 158);
            this.listIssues.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            // моноширинный текст: предпросмотр повторяет <pre> на странице
            this.textPreview.Location = new System.Drawing.Point(8, 424);
            this.textPreview.Size = new System.Drawing.Size(1156, 140);
            this.textPreview.Multiline = true;
            this.textPreview.ReadOnly = true;
            this.textPreview.WordWrap = false;
            this.textPreview.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.textPreview.Font = new System.Drawing.Font("Consolas", 8.25F);
            this.textPreview.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.listIssues.HorizontalScrollbar = true;

            this.tabExport.Controls.Add(this.groupStyle);
            this.tabExport.Controls.Add(this.groupExport);
            this.tabExport.Controls.Add(this.labelIssues);
            this.tabExport.Controls.Add(this.listIssues);
            this.tabExport.Controls.Add(this.textPreview);

            // ─── числа в полях настроек ────────────────────────────────────
            // Все счётчики выровнены по правому краю: разряды выстраиваются в столбик,
            // и число не отъезжает от подписи при вводе. Отступ от правого края задаёт
            // WizardTheme.Apply — свойства для него у поля ввода нет.
            this.numResolution.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numFactor.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numMinIntensity.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numMinEnergy.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numMaxEnergy.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numMinHalfLife.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numMaxHalfLife.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numTopN.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numSecondaryMin.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numNearEnergy.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numNearWindow.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numNearIntensity.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numNearHalfLife.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numZonePercent.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numZoneFactor.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;
            this.numAnchors.TextAlign = System.Windows.Forms.HorizontalAlignment.Right;

            // ─── строка состояния ──────────────────────────────────────────
            // счётчик занимает всё свободное место, кнопки прижаты вправо — как на странице
            this.statusLabel.Spring = true;
            this.statusLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.statusLabel.Text = "";
            this.buttonStepPrev.Text = "◂ Back";
            this.buttonStepPrev.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.buttonStepPrev.AutoToolTip = false;
            this.buttonStepNext.Text = "Next ▸";
            this.buttonStepNext.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.buttonStepNext.AutoToolTip = false;
            this.buttonHelp.Text = RoiWizardStrings.buttonHelp_Text;
            this.buttonHelp.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.buttonHelp.AutoToolTip = false;
            this.buttonHelp.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
                this.statusLabel, this.buttonHelp, this.buttonStepPrev, this.buttonStepNext });


            // ─── форма ─────────────────────────────────────────────────────
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1180, 620);
            this.MinimumSize = new System.Drawing.Size(1000, 500);
            this.ShowIcon = false;
            this.Controls.Add(this.tabs);
            this.Controls.Add(this.statusStrip);
            this.Name = "RoiWizardForm";
            this.Text = RoiWizardStrings.form_Title;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;

            ((System.ComponentModel.ISupportInitialize)(this.numResolution)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numFactor)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinIntensity)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinEnergy)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxEnergy)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTopN)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMinHalfLife)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxHalfLife)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSecondaryMin)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numNearEnergy)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numNearWindow)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numNearIntensity)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numNearHalfLife)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numZonePercent)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numZoneFactor)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numAnchors)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        System.Windows.Forms.TabControl tabs;
        System.Windows.Forms.TabPage tabSources;
        System.Windows.Forms.TabPage tabLines;
        System.Windows.Forms.TabPage tabExport;

        System.Windows.Forms.GroupBox groupSearch;
        System.Windows.Forms.TextBox textSearch;
        System.Windows.Forms.Button buttonAddSingle;
        System.Windows.Forms.Button buttonAddFamily;
        System.Windows.Forms.Button buttonAddChain;
        XPTable.Models.Table tableCatalog;
        XPTable.Models.ColumnModel columnModelCatalog;
        XPTable.Models.TextColumn columnCatalogName;
        XPTable.Models.TextColumn columnCatalogFamilies;
        XPTable.Models.TextColumn columnCatalogHalfLife;
        XPTable.Models.TextColumn columnCatalogLines;
        XPTable.Models.TableModel tableModelCatalog;

        System.Windows.Forms.GroupBox groupGroup;
        System.Windows.Forms.GroupBox groupXrf;
        System.Windows.Forms.CheckedListBox checkedGroup;
        System.Windows.Forms.Button buttonFamilyInfo;
        System.Windows.Forms.Label labelFamilyInfo;
        System.Windows.Forms.Label labelSearchHint;
        System.Windows.Forms.FlowLayoutPanel panelPresets;
        System.Windows.Forms.Label labelXrfHint;
        System.Windows.Forms.Label labelGroupHint;
        System.Windows.Forms.ComboBox comboGroup;
        System.Windows.Forms.Button buttonGroupAll;
        System.Windows.Forms.Button buttonGroupFamily;
        System.Windows.Forms.Button buttonGroupChain;
        System.Windows.Forms.Label labelXrf;
        System.Windows.Forms.CheckedListBox checkedXrf;

        System.Windows.Forms.GroupBox groupSelected;
        System.Windows.Forms.FlowLayoutPanel panelSelected;
        System.Windows.Forms.Button buttonClear;

        System.Windows.Forms.GroupBox groupResolution;
        System.Windows.Forms.Label labelResolution;
        System.Windows.Forms.NumericUpDown numResolution;
        System.Windows.Forms.Button buttonFromSpectrum;
        System.Windows.Forms.Label labelCriterion;
        System.Windows.Forms.ComboBox comboCriterion;
        System.Windows.Forms.NumericUpDown numFactor;
        System.Windows.Forms.Label labelFactor;
        System.Windows.Forms.Button buttonMerge;
        System.Windows.Forms.Button buttonUnmerge;
        System.Windows.Forms.Label labelMergeInfo;

        System.Windows.Forms.GroupBox groupFilters;
        System.Windows.Forms.CheckBox checkIntensity;
        System.Windows.Forms.NumericUpDown numMinIntensity;
        System.Windows.Forms.ComboBox comboIntensityMode;
        System.Windows.Forms.CheckBox checkEnergy;
        System.Windows.Forms.NumericUpDown numMinEnergy;
        System.Windows.Forms.NumericUpDown numMaxEnergy;
        System.Windows.Forms.CheckBox checkEquilibrium;
        System.Windows.Forms.GroupBox groupSecondary;
        System.Windows.Forms.Label labelSecondaryMin;
        System.Windows.Forms.NumericUpDown numSecondaryMin;
        System.Windows.Forms.CheckBox checkSecBackscatter;
        System.Windows.Forms.CheckBox checkSecComptonEdge;
        System.Windows.Forms.CheckBox checkSecSingleEscape;
        System.Windows.Forms.CheckBox checkSecDoubleEscape;
        System.Windows.Forms.CheckBox checkSecIodine;
        System.Windows.Forms.CheckBox checkSecAnnihilation;
        System.Windows.Forms.CheckBox checkSecSum;
        System.Windows.Forms.CheckBox checkSecPileUp;
        System.Windows.Forms.Button buttonGenerateSecondary;
        System.Windows.Forms.GroupBox groupNear;
        System.Windows.Forms.Label labelNearEnergy;
        System.Windows.Forms.NumericUpDown numNearEnergy;
        System.Windows.Forms.Label labelNearWindow;
        System.Windows.Forms.NumericUpDown numNearWindow;
        System.Windows.Forms.Label labelNearIntensity;
        System.Windows.Forms.NumericUpDown numNearIntensity;
        System.Windows.Forms.Label labelNearHalfLife;
        System.Windows.Forms.NumericUpDown numNearHalfLife;
        System.Windows.Forms.ComboBox comboNearHalfLifeUnit;
        System.Windows.Forms.Button buttonNearSearch;
        System.Windows.Forms.Label labelNearHint;
        XPTable.Models.Table tableNear;
        XPTable.Models.ColumnModel columnModelNear;
        XPTable.Models.TextColumn columnNearDelta;
        XPTable.Models.TextColumn columnNearName;
        XPTable.Models.TextColumn columnNearEnergy;
        XPTable.Models.TextColumn columnNearIntensity;
        XPTable.Models.TextColumn columnNearType;
        XPTable.Models.TextColumn columnNearHalfLife;
        XPTable.Models.ButtonColumn columnNearAdd;
        XPTable.Models.TextColumn columnNearFill;
        XPTable.Models.TableModel tableModelNear;
        System.Windows.Forms.Button buttonSelectAll;
        System.Windows.Forms.Button buttonSelectNone;
        System.Windows.Forms.NumericUpDown numTopN;
        System.Windows.Forms.Label labelTopN;
        System.Windows.Forms.Button buttonSelectTop;

        XPTable.Models.Table tableLines;
        XPTable.Models.ColumnModel columnModelLines;
        XPTable.Models.CheckBoxColumn columnLineSelected;
        XPTable.Models.TextColumn columnLineName;
        XPTable.Models.TextColumn columnLineEnergy;
        XPTable.Models.TextColumn columnLineIntensity;
        XPTable.Models.TextColumn columnLineRelative;
        XPTable.Models.TextColumn columnLineHalfLife;
        XPTable.Models.TextColumn columnLineType;
        System.Windows.Forms.CheckBox checkHalfLife;
        System.Windows.Forms.NumericUpDown numMinHalfLife;
        System.Windows.Forms.ComboBox comboMinHalfLifeUnit;
        System.Windows.Forms.NumericUpDown numMaxHalfLife;
        System.Windows.Forms.ComboBox comboMaxHalfLifeUnit;
        System.Windows.Forms.CheckBox checkHideUnselected;
        System.Windows.Forms.Label labelTypes;
        System.Windows.Forms.CheckBox checkTypeGamma;
        System.Windows.Forms.CheckBox checkTypeXray;
        System.Windows.Forms.CheckBox checkTypeXrf;
        System.Windows.Forms.CheckBox checkTypeSecondary;
        XPTable.Models.TableModel tableModelLines;

        System.Windows.Forms.GroupBox groupStyle;
        System.Windows.Forms.Label labelStyle;
        System.Windows.Forms.ComboBox comboStyle;
        System.Windows.Forms.Label labelWidth;
        System.Windows.Forms.ComboBox comboWidthMode;
        System.Windows.Forms.NumericUpDown numZonePercent;
        System.Windows.Forms.NumericUpDown numZoneFactor;
        System.Windows.Forms.Label labelColors;
        System.Windows.Forms.Button buttonColorByChain;
        System.Windows.Forms.Button buttonColorByNuclide;
        System.Windows.Forms.FlowLayoutPanel panelColors;

        System.Windows.Forms.GroupBox groupExport;
        System.Windows.Forms.Label labelConfigName;
        System.Windows.Forms.TextBox textConfigName;
        System.Windows.Forms.Button buttonCreateRoi;
        System.Windows.Forms.Button buttonPreview;
        System.Windows.Forms.TextBox textPreview;
        System.Windows.Forms.Label labelSetName;
        System.Windows.Forms.TextBox textSetName;
        System.Windows.Forms.Label labelAnchor;
        System.Windows.Forms.ComboBox comboAnchor;
        System.Windows.Forms.Button buttonCreateSet;
        System.Windows.Forms.CheckBox checkFullSet;
        System.Windows.Forms.Label labelAnchorCount;
        System.Windows.Forms.NumericUpDown numAnchors;
        System.Windows.Forms.Label labelIssues;
        System.Windows.Forms.ListBox listIssues;

        System.Windows.Forms.StatusStrip statusStrip;
        System.Windows.Forms.ToolStripStatusLabel statusLabel;
        System.Windows.Forms.ToolStripButton buttonHelp;
        System.Windows.Forms.ToolStripButton buttonStepPrev;
        System.Windows.Forms.ToolStripButton buttonStepNext;
    }
}
