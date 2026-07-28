using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;
using System.Xml.Serialization;
using XPTable.Events;
using XPTable.Models;

namespace BecquerelMonitor.RoiWizard
{
    // Окно конструктора: три шага повторяют веб-версию инструмента, но результат
    // никуда не выгружается файлом — ROI-конфигурация уходит в ROIConfigManager,
    // а набор нуклидов в NuclideDefinitionManager.
    // Наследование от DockContent делает окно родной док-панелью BecqMoni:
    // полоска заголовка, булавка автоскрытия, группировка и прилипание рисуются
    // и работают той же темой VS2015BlueTheme, что у «Обнаружения пиков» и
    // остальных панелей — имитировать ничего не нужно.
    public partial class RoiWizardForm : DockContent
    {
        // Не readonly: классификация нуклидов правится в NucBase, каталог после этого
        // перечитывается, и панель должна взять новый экземпляр — см. ReloadCatalogIfStale.
        NuclideCatalog catalog;
        int catalogVersion;
        readonly SourceSelection selection = new SourceSelection();
        LineSetBuilder builder;
        // пересоздаются при смене R: модель разрешения захватывается экземпляром,
        // иначе ширина зон считалась бы по устаревшему значению
        SetExporter exporter;
        ZoneCalculator zones;

        List<SpectralLine> lines = new List<SpectralLine>();
        List<SpectralLine> beforeMerge;
        // источник разрешения из хоста: FWHM-калибровка открытого спектра.
        // Если не задан, кнопка «из спектра» просто выключена — форма остаётся
        // самостоятельной и тестируемой без приложения.
        readonly Func<double> resolutionProvider;

        readonly List<string> groupKeys = new List<string>();
        readonly List<string> xrfSymbols = new List<string>();
        bool suspendEvents;

        public RoiWizardForm() : this(null)
        {
        }

        public RoiWizardForm(Func<double> resolutionProvider)
        {
            this.InitializeComponent();
            // цвета и шрифт — из темы веб-версии, чтобы окно выглядело так же
            WizardTheme.Apply(this);
            // закрытие панели прячет её, а не разрушает: повторное открытие из меню
            // возвращает выбранные источники и настройки нетронутыми
            this.HideOnClose = true;
            this.resolutionProvider = resolutionProvider;

            this.catalog = NuclideCatalog.GetInstance();
            this.catalogVersion = NuclideCatalog.Version;
            this.builder = new LineSetBuilder(this.catalog).Reset();
            this.zones = new ZoneCalculator(this.Resolution);
            this.exporter = new SetExporter(this.Resolution, this.zones);

            this.FillCombos();
            this.FillGroups();
            this.FillXrf();
            this.RefreshCatalog();
            this.WireEvents();

            this.buttonFromSpectrum.Enabled = resolutionProvider != null;
            this.SyncSetControls();
            this.FillXrf();
            this.FillPresets();
            this.SetFold(this.groupSecondary, false);
            this.SetFold(this.groupNear, false);
            this.LayoutSources();
            this.LayoutLineColumns();
            this.RefreshGroupList();
            this.UpdateMergeInfo();
            this.UpdateStepButtons();
            this.UpdateStatus();
        }

        ResolutionModel Resolution
        {
            get { return new ResolutionModel((double)this.numResolution.Value); }
        }


        // Панель живёт всё время работы приложения (HideOnClose), а классификацию можно
        // поправить в NucBase между её показами. Сброса кэша каталога для этого мало:
        // списки уже разложены по контролам, их надо пересобрать. Сравниваем редакцию
        // каталога при каждом показе — это дешевле подписки и не оставляет висящих
        // обработчиков на скрытом окне.
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (this.Visible)
            {
                this.ReloadCatalogIfStale();
            }
        }

        void ReloadCatalogIfStale()
        {
            if (this.catalogVersion == NuclideCatalog.Version)
            {
                return;
            }
            this.catalog = NuclideCatalog.GetInstance();
            this.catalogVersion = NuclideCatalog.Version;
            this.builder = new LineSetBuilder(this.catalog).Reset();

            // выбор источников и набранные линии не трогаем: правилась классификация,
            // а не сами линии, и терять работу пользователя из-за неё незачем
            bool suspended = this.suspendEvents;
            this.suspendEvents = true;
            try
            {
                int group = this.comboGroup.SelectedIndex;
                this.FillGroups();
                if (group >= 0 && group < this.comboGroup.Items.Count)
                {
                    this.comboGroup.SelectedIndex = group;
                }
                this.RefreshCatalog();
            }
            finally
            {
                this.suspendEvents = suspended;
            }
            this.RefreshGroupList();
        }


        // ─── наполнение ─────────────────────────────────────────────────────

        void FillCombos()
        {
            // порядок строк обязан совпадать с порядком членов MergeCriterion:
            // OnCriterionChanged приводит SelectedIndex прямо к перечислению
            this.comboCriterion.Items.AddRange(new object[] {
                RoiWizardStrings.criterionSparrow,
                RoiWizardStrings.criterionMeasured,
                RoiWizardStrings.criterionAnchored,
                RoiWizardStrings.criterionManual
            });
            this.comboCriterion.SelectedIndex = 0;

            this.comboIntensityMode.Items.AddRange(new object[] {
                RoiWizardStrings.intensityRelative,
                RoiWizardStrings.intensityAbsolute
            });
            this.comboIntensityMode.SelectedIndex = 0;

            this.comboStyle.Items.AddRange(new object[] {
                RoiWizardStrings.roiStyleMarkers,
                RoiWizardStrings.roiStyleZones,
                RoiWizardStrings.roiStyleBoth
            });
            this.comboStyle.SelectedIndex = 0;

            this.comboWidthMode.Items.AddRange(new object[] {
                RoiWizardStrings.widthModePercent,
                RoiWizardStrings.widthModeFwhm
            });
            this.comboWidthMode.SelectedIndex = 0;

            object[] units = {
                RoiWizardStrings.unitSeconds, RoiWizardStrings.unitHours,
                RoiWizardStrings.unitDays, RoiWizardStrings.unitYears };
            this.comboMinHalfLifeUnit.Items.AddRange(units);
            this.comboMinHalfLifeUnit.SelectedIndex = 2;      // сутки, как в вебе
            this.comboMaxHalfLifeUnit.Items.AddRange((object[])units.Clone());
            this.comboMaxHalfLifeUnit.SelectedIndex = 3;      // годы
            this.comboNearHalfLifeUnit.Items.AddRange((object[])units.Clone());
            this.comboNearHalfLifeUnit.SelectedIndex = 2;
            this.SyncZoneControls();
        }

        void FillGroups()
        {
            this.comboGroup.Items.Clear();
            this.groupKeys.Clear();
            string[] families = { "POPULAR", "NORM", "MED", "IND", "SNM", "FISS", "NAA", "WASTE" };
            foreach (string family in families)
            {
                int count = 0;
                foreach (CatalogNuclide nuclide in this.catalog.ByFamily(family))
                {
                    count++;
                }
                if (count == 0)
                {
                    continue;
                }
                this.groupKeys.Add("f:" + family);
                CatalogFamily entry = this.catalog.FindFamily(family);
                string title = entry == null ? family
                    : (this.russian && !string.IsNullOrEmpty(entry.TitleRu) ? entry.TitleRu : entry.Title);
                this.comboGroup.Items.Add(title + " (" + count + ")");
            }
            foreach (CatalogChain chain in this.catalog.Chains)
            {
                this.groupKeys.Add("c:" + chain.Id);
                // подпись ряда — на языке интерфейса, как и у семейств строкой выше:
                // без этого «Ряд Th-232» оставался английским посреди русского окна
                string chainTitle = this.russian && !string.IsNullOrEmpty(chain.TitleRu)
                    ? chain.TitleRu
                    : chain.Title;
                this.comboGroup.Items.Add(chainTitle + " (" + chain.Members.Count + ")");
            }
            if (this.comboGroup.Items.Count > 0)
            {
                this.comboGroup.SelectedIndex = 0;
            }
        }

        // Готовые наборы: пустой старт не объясняет инструмент, а один клик — объясняет.
        // Те же пять, что на странице.
        void FillPresets()
        {
            this.panelPresets.SuspendLayout();
            this.panelPresets.Controls.Clear();
            Label caption = new Label();
            caption.Text = this.presetsCaption;
            caption.AutoSize = true;
            caption.ForeColor = WizardTheme.Muted;
            caption.Margin = new Padding(2, 4, 2, 2);
            this.panelPresets.Controls.Add(caption);

            for (int i = 0; i < this.presets.Length; i++)
            {
                if (i > 0)
                {
                    Label separator = new Label();
                    separator.Text = "·";
                    separator.AutoSize = true;
                    separator.ForeColor = WizardTheme.Muted;
                    separator.Margin = new Padding(0, 4, 0, 2);
                    this.panelPresets.Controls.Add(separator);
                }
                Preset preset = this.presets[i];
                LinkLabel link = new LinkLabel();
                link.Text = preset.Title;
                link.AutoSize = true;
                link.LinkColor = WizardTheme.Accent;
                link.ActiveLinkColor = WizardTheme.AccentInk;
                link.LinkBehavior = LinkBehavior.HoverUnderline;
                link.Margin = new Padding(0, 4, 0, 2);
                this.tips.SetToolTip(link, preset.Hint);
                Preset captured = preset;
                link.LinkClicked += delegate { this.ApplyPreset(captured); };
                this.panelPresets.Controls.Add(link);
            }
            this.panelPresets.ResumeLayout();
        }

        void ApplyPreset(Preset preset)
        {
            foreach (string chainRoot in preset.Chains)
            {
                this.selection.Add(this.catalog, chainRoot, AddMode.Chain);
            }
            foreach (string name in preset.Nuclides)
            {
                this.selection.Add(this.catalog, name, AddMode.Single);
            }
            foreach (string code in preset.Families)
            {
                foreach (CatalogNuclide nuclide in this.catalog.ByFamily(code))
                {
                    this.selection.AddGroupMember(this.catalog, nuclide.Name);
                }
            }
            foreach (string symbol in preset.Xrf)
            {
                for (int i = 0; i < this.xrfSymbols.Count; i++)
                {
                    if (string.Equals(this.xrfSymbols[i], symbol, StringComparison.Ordinal))
                    {
                        this.checkedXrf.SetItemChecked(i, true);   // галка сама добавит элемент
                    }
                }
            }
            this.RefreshGroupList();
            this.Rebuild();
        }

        sealed class Preset
        {
            public readonly string Title;
            public readonly string Hint;
            public readonly string[] Chains;
            public readonly string[] Nuclides;
            public readonly string[] Families;
            public readonly string[] Xrf;

            public Preset(string title, string hint,
                          string[] chains, string[] nuclides, string[] families, string[] xrf)
            {
                this.Title = title;
                this.Hint = hint;
                this.Chains = chains;
                this.Nuclides = nuclides;
                this.Families = families;
                this.Xrf = xrf;
            }
        }

        static readonly string[] None = new string[0];

        readonly Preset[] presets = {
            new Preset(RoiWizardStrings.preset1_Title, RoiWizardStrings.preset1_Hint,
                       new string[] { "Th-232", "U-238" }, new string[] { "K-40" }, None, None),
            new Preset(RoiWizardStrings.preset2_Title, RoiWizardStrings.preset2_Hint,
                       None, new string[] { "Cs-137", "Co-60" }, None, None),
            new Preset(RoiWizardStrings.preset3_Title, RoiWizardStrings.preset3_Hint,
                       None, new string[] { "Am-241", "Ba-133", "Eu-152", "Cs-137", "Co-60" }, None, None),
            new Preset(RoiWizardStrings.preset4_Title, RoiWizardStrings.preset4_Hint,
                       None, None, new string[] { "MED" }, None),
            new Preset(RoiWizardStrings.preset5_Title, RoiWizardStrings.preset5_Hint,
                       None, None, None, new string[] { "Pb", "W", "La", "Ba", "I" })
        };

        readonly ToolTip tips = new ToolTip();
        string presetsCaption { get { return RoiWizardStrings.presetsCaption; } }

        // Словарик кодов: пояснение выбранного семейства и чем задана классификация.
        // Всплывает поверх списка и закрывается щелчком по себе или Esc — как .infoPop.
        void ToggleFamilyInfo()
        {
            if (this.labelFamilyInfo.Visible)
            {
                this.labelFamilyInfo.Visible = false;
                return;
            }
            this.UpdateFamilyInfo();
            this.labelFamilyInfo.Visible = true;
            this.labelFamilyInfo.BringToFront();
        }

        void UpdateFamilyInfo()
        {
            int index = this.comboGroup.SelectedIndex;
            string text = "";
            if (index >= 0 && index < this.groupKeys.Count &&
                this.groupKeys[index].StartsWith("f:", StringComparison.Ordinal))
            {
                CatalogFamily family = this.catalog.FindFamily(this.groupKeys[index].Substring(2));
                if (family != null)
                {
                    string title = this.russian && !string.IsNullOrEmpty(family.TitleRu)
                        ? family.TitleRu : family.Title;
                    string info = this.russian && !string.IsNullOrEmpty(family.InfoRu)
                        ? family.InfoRu : family.Info;
                    text = title + " — " + info + Environment.NewLine + Environment.NewLine;
                }
            }
            string standard = this.russian && !string.IsNullOrEmpty(this.catalog.FamilyStandardRu)
                ? this.catalog.FamilyStandardRu : this.catalog.FamilyStandard;
            this.labelFamilyInfo.Text = text + (standard == null ? "" : standard);
        }

        void FillXrf()
        {
            this.checkedXrf.Items.Clear();
            this.xrfSymbols.Clear();
            foreach (XrfElement element in this.catalog.XrfElements)
            {
                this.xrfSymbols.Add(element.Symbol);
                string context = this.russian && !string.IsNullOrEmpty(element.ContextRu)
                    ? element.ContextRu
                    : element.Context;
                this.checkedXrf.Items.Add(element.Symbol + " — " + context);
            }
        }

        void RefreshCatalog()
        {
            string filter = this.textSearch.Text.Trim();
            this.tableCatalog.SuspendLayout();
            this.tableModelCatalog.Rows.Clear();
            foreach (CatalogNuclide nuclide in this.catalog.Nuclides)
            {
                if (filter.Length > 0 &&
                    nuclide.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (nuclide.Families ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                Row row = new Row();
                row.Cells.Add(new Cell(nuclide.Name));
                row.Cells.Add(new Cell(nuclide.Families ?? ""));
                row.Cells.Add(new Cell(HalfLifeLabel(nuclide.HalfLifeText ?? ""),
                                       nuclide.HalfLifeYears));
                // счётчики уходят рендереру парой чисел: «γ» и «X» красятся по-разному
                row.Cells.Add(new Cell(
                    nuclide.Gamma.Count.ToString(CultureInfo.InvariantCulture) + " " +
                    nuclide.Xray.Count.ToString(CultureInfo.InvariantCulture), nuclide.LineCount));
                if (nuclide.LineCount == 0)
                {
                    row.ForeColor = WizardTheme.NoLines;   // .nuc.nolines — нечего искать в спектре
                }
                row.Tag = nuclide;
                this.tableModelCatalog.Rows.Add(row);
            }
            // ResumeLayout(true), а не без аргумента: без отложенного прохода XPTable
            // не пересчитывает диапазон вертикальной полосы после смены строк, и она
            // упирается в максимум, не дойдя до последних строк (клавишами дойти можно,
            // ползунком нет), а при устаревшей позиции показывает хвост списка на пустом фоне.
            this.tableCatalog.ResumeLayout(true);
            this.LayoutCatalogColumns();
        }

        // Блоки вторичных пиков и поиска близких линий свёрнуты по умолчанию — на
        // странице это .group с .gbody{display:none}. GroupBox так не умеет: прячем
        // содержимое и ужимаем высоту до заголовка. Щелчок по полосе заголовка (верхние
        // строки блока) переключает состояние, маркер ▾/▴ стоит в подписи.
        readonly Dictionary<GroupBox, int> foldedHeights = new Dictionary<GroupBox, int>();
        readonly Dictionary<GroupBox, string> foldedTitles = new Dictionary<GroupBox, string>();
        readonly Dictionary<GroupBox, bool> foldedOpen = new Dictionary<GroupBox, bool>();

        int HeaderHeight(GroupBox box)
        {
            return box.Font.Height + 8;
        }

        void SetFold(GroupBox box, bool open)
        {
            if (!this.foldedHeights.ContainsKey(box))
            {
                this.foldedHeights[box] = box.Height;
                this.foldedTitles[box] = box.Text;
            }
            this.foldedOpen[box] = open;
            foreach (Control child in box.Controls)
            {
                child.Visible = open;
            }
            box.Height = open ? this.foldedHeights[box] : this.HeaderHeight(box);
            box.Text = this.foldedTitles[box] + (open ? "  ▴" : "  ▾");
            this.LayoutLines();
        }

        void ToggleFold(GroupBox box, int y)
        {
            if (y <= this.HeaderHeight(box))
            {
                bool open;
                this.SetFold(box, !(this.foldedOpen.TryGetValue(box, out open) && open));
            }
        }

        // Вкладка «Линии» складывается сверху вниз: свернув блок, всё под ним
        // поднимается, а остаток высоты достаётся таблице линий.
        void LayoutLines()
        {
            int width = this.tabLines.ClientSize.Width;
            int height = this.tabLines.ClientSize.Height;
            if (width < 120 || height < 120)
            {
                return;
            }
            const int Pad = 8;
            const int Gap = 6;
            int y = 6;
            GroupBox[] boxes = {
                this.groupResolution, this.groupFilters, this.groupSecondary, this.groupNear };
            foreach (GroupBox box in boxes)
            {
                box.SetBounds(Pad, y, width - Pad * 2, box.Height);
                y += box.Height + Gap;
            }
            int rest = height - Pad - y;
            if (rest < 60)
            {
                rest = 60;
            }
            this.tableLines.SetBounds(Pad, y, width - Pad * 2, rest);
        }

        // Три колонки шага 1 делят ширину поровну — .cols3 на странице задана как
        // grid-template-columns: repeat(3, 1fr). Привязки WinForms умеют только
        // «держать край», поэтому доли считаются здесь; полоса «Выбрано» прижата к низу.
        void LayoutSources()
        {
            int width = this.tabSources.ClientSize.Width;
            int height = this.tabSources.ClientSize.Height;
            if (width < 120 || height < 120)
            {
                return;
            }
            const int Pad = 8;
            const int Gap = 8;
            const int Top = 6;
            this.groupSelected.SetBounds(Pad, height - Pad - this.groupSelected.Height,
                                         width - Pad * 2, this.groupSelected.Height);
            // доли из .cols3: 1fr 1.15fr 1fr — средняя колонка шире, в ней комбобокс
            // группы и три кнопки раскрытия
            int free = width - Pad * 2 - Gap * 2;
            int column = free * 100 / 315;
            int middle = free - column * 2;
            int boxHeight = this.groupSelected.Top - Gap - Top;
            this.groupSearch.SetBounds(Pad, Top, column, boxHeight);
            this.groupGroup.SetBounds(Pad + column + Gap, Top, middle, boxHeight);
            this.groupXrf.SetBounds(Pad + column + Gap + middle + Gap, Top, column, boxHeight);

            // блок пресетов переносится по ширине колонки, и число строк меняется:
            // высота блока пересчитывается под фактический перенос, иначе нижняя
            // строка срезается краем панели; таблица каталога отдаёт ей место
            int presetWidth = column - 12;
            int presetHeight = this.panelPresets.GetPreferredSize(
                new Size(presetWidth, 0)).Height;
            int presetTop = this.groupSearch.ClientSize.Height - 8 - presetHeight;
            this.panelPresets.SetBounds(6, presetTop, presetWidth, presetHeight);
            this.labelSearchHint.Top = presetTop - this.labelSearchHint.Height - 2;
            this.tableCatalog.Height = this.labelSearchHint.Top - 4 - this.tableCatalog.Top;

            // ряды кнопок делят ширину своей панели: жёсткие ширины не влезают,
            // когда колонка уже суммы масштабированных кнопок (.line — flex-строка)
            LayoutButtonRow(this.groupSearch, new Control[] {
                this.buttonAddSingle, this.buttonAddFamily, this.buttonAddChain },
                new int[] { 104, 122, 122 });
            LayoutButtonRow(this.groupGroup, new Control[] {
                this.buttonGroupAll, this.buttonGroupFamily, this.buttonGroupChain },
                new int[] { 104, 140, 104 });
        }

        static void LayoutButtonRow(Control box, Control[] buttons, int[] shares)
        {
            const int Pad = 8;
            const int Gap = 6;
            int free = box.ClientSize.Width - Pad * 2 - Gap * (buttons.Length - 1);
            if (free < 120)
            {
                return;
            }
            int total = 0;
            foreach (int share in shares)
            {
                total += share;
            }
            int x = Pad;
            for (int i = 0; i < buttons.Length; i++)
            {
                int width = i == buttons.Length - 1
                    ? box.ClientSize.Width - Pad - x
                    : free * shares[i] / total;
                buttons[i].SetBounds(x, buttons[i].Top, width, buttons[i].Height);
                x += width + Gap;
            }
        }

        // Свободное место таблицы линий уходит в имя нуклида: с пометкой цепочки
        // «Ra-228 X L (Th-232)» подписи длинные, а числовые колонки фиксированы.
        // Доли берутся из ТЕКУЩИХ ширин, а не из чисел разметки: границы колонок
        // тянутся мышью (XPTable это умеет), и жёсткие доли возвращали бы их
        // к исходным на первом же изменении размера окна.
        void LayoutLineColumns()
        {
            int free = this.tableLines.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
                       - this.columnLineSelected.Width;
            if (free < 400)
            {
                return;
            }
            Column[] columns = {
                this.columnLineName, this.columnLineEnergy, this.columnLineIntensity,
                this.columnLineRelative, this.columnLineHalfLife, this.columnLineType };
            int total = 0;
            foreach (Column column in columns)
            {
                total += column.Width;
            }
            if (total <= 0 || total == free)
            {
                return;
            }
            int used = 0;
            for (int i = 0; i < columns.Length - 1; i++)
            {
                int width = Math.Max(24, free * columns[i].Width / total);
                columns[i].Width = width;
                used += width;
            }
            columns[columns.Length - 1].Width = Math.Max(24, free - used);   // остаток — последней
        }

        // В таблице находок колонки держат свою ширину, а лишнее место уходит в пустой
        // столбец справа: на странице такая таблица шириной по содержимому, и числа
        // стоят сразу за именем нуклида, а не через полстраницы пустоты.
        void LayoutNearColumns()
        {
            int used = this.columnNearDelta.Width
                       + this.columnNearName.Width
                       + this.columnNearEnergy.Width
                       + this.columnNearIntensity.Width
                       + this.columnNearType.Width
                       + this.columnNearHalfLife.Width
                       + this.columnNearAdd.Width;
            int free = this.tableNear.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
                       - used;
            if (free > 0 && free != this.columnNearFill.Width)
            {
                this.columnNearFill.Width = free;
            }
        }

        // Свободное место забирает колонка семейств: «T½ γN X N» остаётся прижатым
        // к правому краю строки — это margin-left:auto у .nuc .hl на странице.
        void LayoutCatalogColumns()
        {
            int free = this.tableCatalog.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
                       - this.columnCatalogName.Width
                       - this.columnCatalogHalfLife.Width
                       - this.columnCatalogLines.Width;
            if (free > 60 && free != this.columnCatalogFamilies.Width)
            {
                this.columnCatalogFamilies.Width = free;
            }
        }

        // ─── события ────────────────────────────────────────────────────────

        void WireEvents()
        {
            this.tabs.SelectedIndexChanged += delegate { this.UpdateStepButtons(); };
            this.buttonHelp.Click += delegate { this.ShowHelp(); };
            this.buttonStepPrev.Click += delegate { this.GoStep(-1); };
            this.buttonStepNext.Click += delegate { this.GoStep(1); };
            this.tabSources.Resize += delegate { this.LayoutSources(); };
            this.tabLines.Resize += delegate { this.LayoutLines(); };
            this.tabExport.Resize += delegate { this.LayoutExport(); };
            this.groupSecondary.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                this.ToggleFold(this.groupSecondary, e.Y);
            };
            this.groupNear.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                this.ToggleFold(this.groupNear, e.Y);
            };
            this.tableCatalog.Resize += delegate { this.LayoutCatalogColumns(); };
            this.tableLines.Resize += delegate { this.LayoutLineColumns(); };
            this.textSearch.TextChanged += delegate { this.RefreshCatalog(); };
            this.buttonAddSingle.Click += delegate { this.AddFromCatalog(AddMode.Single); };
            this.buttonAddFamily.Click += delegate { this.AddFromCatalog(AddMode.FamilyLines); };
            this.buttonAddChain.Click += delegate { this.AddFromCatalog(AddMode.Chain); };
            this.tableCatalog.DoubleClick += delegate { this.AddFromCatalog(AddMode.Single); };

            this.comboGroup.SelectedIndexChanged += delegate
            {
                this.RefreshGroupList();
                if (this.labelFamilyInfo.Visible)
                {
                    this.UpdateFamilyInfo();     // словарик открыт — сразу про новую группу
                }
            };
            this.buttonFamilyInfo.Click += delegate { this.ToggleFamilyInfo(); };
            this.labelFamilyInfo.Click += delegate { this.labelFamilyInfo.Visible = false; };
            this.checkedGroup.ItemCheck += this.OnGroupItemCheck;
            this.buttonGroupAll.Click += delegate { this.AddFromGroup(AddMode.Single); };
            this.buttonGroupFamily.Click += delegate { this.AddFromGroup(AddMode.FamilyLines); };
            this.buttonGroupChain.Click += delegate { this.AddFromGroup(AddMode.Chain); };
            this.checkedXrf.ItemCheck += this.OnXrfCheck;

            this.buttonClear.Click += delegate
            {
                this.selection.Clear();
                for (int i = 0; i < this.checkedXrf.Items.Count; i++)
                {
                    this.checkedXrf.SetItemChecked(i, false);
                }
                this.RefreshGroupList();
                this.Rebuild();
            };

            this.numResolution.ValueChanged += delegate { this.UpdateMergeInfo(); };
            this.comboCriterion.SelectedIndexChanged += this.OnCriterionChanged;
            this.numFactor.ValueChanged += delegate { this.UpdateMergeInfo(); };
            this.buttonMerge.Click += delegate { this.MergeLines(); };
            this.buttonUnmerge.Click += delegate { this.UnmergeLines(); };

            EventHandler rebuild = delegate { this.Rebuild(); };
            this.checkIntensity.CheckedChanged += rebuild;
            this.numMinIntensity.ValueChanged += rebuild;
            this.comboIntensityMode.SelectedIndexChanged += rebuild;
            this.checkEnergy.CheckedChanged += rebuild;
            this.numMinEnergy.ValueChanged += rebuild;
            this.numMaxEnergy.ValueChanged += rebuild;
            this.checkHalfLife.CheckedChanged += rebuild;
            this.numMinHalfLife.ValueChanged += rebuild;
            this.comboMinHalfLifeUnit.SelectedIndexChanged += rebuild;
            this.numMaxHalfLife.ValueChanged += rebuild;
            this.comboMaxHalfLifeUnit.SelectedIndexChanged += rebuild;
            EventHandler refreshLines = delegate { this.RefreshLines(); };
            this.checkHideUnselected.CheckedChanged += refreshLines;
            this.checkTypeGamma.CheckedChanged += refreshLines;
            this.checkTypeXray.CheckedChanged += refreshLines;
            this.checkTypeXrf.CheckedChanged += refreshLines;
            this.checkTypeSecondary.CheckedChanged += refreshLines;
            this.checkEquilibrium.CheckedChanged += rebuild;

            this.buttonSelectAll.Click += delegate { this.SetVisibleSelected(true); };
            this.buttonSelectNone.Click += delegate { this.SetVisibleSelected(false); };
            this.buttonGenerateSecondary.Click += delegate { this.GenerateSecondary(); };
            this.buttonNearSearch.Click += delegate { this.SearchNearby(); };
            this.tableNear.CellButtonClicked += delegate(object sender, CellButtonEventArgs e)
            {
                // что добавлять, знает сама ячейка: строки переупорядочиваются
                // сортировкой по столбцу, и номер строки списку находок уже не равен
                NearHit hit = e.Cell == null ? null : e.Cell.Tag as NearHit;
                if (hit != null)
                {
                    this.AddFromNearby(hit);
                }
            };
            this.tableNear.Resize += delegate { this.LayoutNearColumns(); };
            this.buttonSelectTop.Click += delegate
            {
                LineSetBuilder.SelectTopPerNuclide(this.lines, (int)this.numTopN.Value);
                this.RefreshLines();
            };
            this.tableLines.CellCheckChanged += this.OnLineCheckChanged;

            this.comboStyle.SelectedIndexChanged += delegate { this.SyncZoneControls(); this.RunChecks(); };
            this.comboWidthMode.SelectedIndexChanged += delegate { this.SyncZoneControls(); this.RunChecks(); };
            this.numZonePercent.ValueChanged += delegate { this.RunChecks(); };
            this.numZoneFactor.ValueChanged += delegate { this.RunChecks(); };
            this.buttonColorByChain.Click += delegate { this.SetColorMode(true); };
            this.buttonColorByNuclide.Click += delegate { this.SetColorMode(false); };
            this.buttonPreview.Click += delegate { this.PreviewXml(); };
            this.buttonCreateRoi.Click += delegate { this.CreateRoiConfig(); };
            this.buttonCreateSet.Click += delegate { this.CreateNuclideSet(); };
            // при «полном наборе» таблица и ручной якорь не участвуют — набор собирается
            // заново из источников, поэтому выбор якоря отдаётся автоматике
            this.checkFullSet.CheckedChanged += delegate { this.SyncSetControls(); this.RunChecks(); };
            this.numAnchors.ValueChanged += delegate { this.RunChecks(); };
            this.buttonFromSpectrum.Click += delegate { this.TakeResolutionFromSpectrum(); };
            this.tabs.SelectedIndexChanged += delegate
            {
                if (this.tabs.SelectedTab == this.tabExport)
                {
                    this.RefreshAnchorCombo();
                    this.RunChecks();
                }
            };
        }

        void OnCriterionChanged(object sender, EventArgs e)
        {
            MergeCriterion criterion = (MergeCriterion)this.comboCriterion.SelectedIndex;
            this.suspendEvents = true;
            this.numFactor.Value = (decimal)MergeCriterionInfo.DefaultFactor(criterion);
            // предел Sparrow — величина физическая, менять её руками смысла нет
            this.numFactor.Enabled = criterion != MergeCriterion.Sparrow;
            this.suspendEvents = false;
            this.UpdateMergeInfo();
        }

        void OnXrfCheck(object sender, ItemCheckEventArgs e)
        {
            if (this.suspendEvents)
            {
                return;
            }
            string symbol = this.xrfSymbols[e.Index];
            if (e.NewValue == CheckState.Checked)
            {
                this.selection.XrfElements.Add(symbol);
            }
            else
            {
                this.selection.XrfElements.Remove(symbol);
            }
            this.BeginInvoke((MethodInvoker)delegate { this.Rebuild(); });
        }

        void OnLineCheckChanged(object sender, XPTable.Events.CellCheckBoxEventArgs e)
        {
            if (this.suspendEvents)
            {
                return;
            }
            Row row = this.tableModelLines.Rows[e.Row];
            SpectralLine line = row.Tag as SpectralLine;
            if (line != null)
            {
                line.Selected = row.Cells[0].Checked;
                row.BackColor = line.Selected ? WizardTheme.Selection : WizardTheme.Card;
                this.UpdateStatus();
            }
        }

        // ─── выбор источников ───────────────────────────────────────────────

        void AddFromCatalog(AddMode mode)
        {
            CatalogNuclide nuclide = this.CurrentCatalogNuclide();
            if (nuclide == null)
            {
                return;
            }
            this.selection.Add(this.catalog, nuclide.Name, mode);
            this.Rebuild();
        }

        CatalogNuclide CurrentCatalogNuclide()
        {
            int index = this.tableCatalog.SelectedIndicies.Length > 0
                ? this.tableCatalog.SelectedIndicies[0]
                : -1;
            if (index < 0 || index >= this.tableModelCatalog.Rows.Count)
            {
                // если строка не выбрана — берём точное совпадение из поля поиска
                return this.catalog.Find(this.textSearch.Text.Trim());
            }
            return this.tableModelCatalog.Rows[index].Tag as CatalogNuclide;
        }

        // Члены выбранной группы с галочками — как в веб-версии: галочка означает
        // «нуклид взят», и она же выбирает цель для кнопок раскрытия.
        void RefreshGroupList()
        {
            int index = this.comboGroup.SelectedIndex;
            this.groupMembers.Clear();
            if (index >= 0 && index < this.groupKeys.Count)
            {
                string key = this.groupKeys[index];
                if (key.StartsWith("f:", StringComparison.Ordinal))
                {
                    foreach (CatalogNuclide nuclide in this.catalog.ByFamily(key.Substring(2)))
                    {
                        this.groupMembers.Add(nuclide.Name);
                    }
                }
                else
                {
                    CatalogChain chain = this.catalog.FindChain(key.Substring(2));
                    if (chain != null)
                    {
                        foreach (string member in chain.Members)
                        {
                            if (this.catalog.Find(member) != null)
                            {
                                this.groupMembers.Add(member);
                            }
                        }
                    }
                }
            }

            this.suppressGroupCheck = true;
            this.checkedGroup.BeginUpdate();
            this.checkedGroup.Items.Clear();
            foreach (string member in this.groupMembers)
            {
                CatalogNuclide nuclide = this.catalog.Find(member);
                string title = nuclide != null && !string.IsNullOrEmpty(nuclide.HalfLifeText)
                    ? member + "   " + HalfLifeLabel(nuclide.HalfLifeText)
                    : member;
                this.checkedGroup.Items.Add(title, this.selection.Nuclides.ContainsKey(member));
            }
            this.checkedGroup.EndUpdate();
            this.suppressGroupCheck = false;
            this.SyncGroupButtons();
        }

        void OnGroupItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (this.suppressGroupCheck || e.Index < 0 || e.Index >= this.groupMembers.Count)
            {
                return;
            }
            string name = this.groupMembers[e.Index];
            if (e.NewValue == CheckState.Checked)
            {
                this.selection.AddGroupMember(this.catalog, name);
            }
            else
            {
                this.selection.Remove(name);
            }
            // область действия кнопок зависит от того, что отмечено
            this.BeginInvoke((MethodInvoker)delegate { this.SyncGroupButtons(); this.Rebuild(); });
        }

        // Отмеченные члены текущей группы — цели для кнопок раскрытия.
        List<string> GroupPicked()
        {
            List<string> picked = new List<string>();
            foreach (int index in this.checkedGroup.CheckedIndices)
            {
                if (index >= 0 && index < this.groupMembers.Count)
                {
                    picked.Add(this.groupMembers[index]);
                }
            }
            return picked;
        }

        // Раскрытие («+ линии семейства», «+ цепочка») применяется к отмеченным; если
        // не отмечено ничего — ко всей группе, и тогда оно осмысленно лишь там, где есть
        // кого раскрывать. У члена ЕРН-ряда родитель задан самим рядом: подменять его
        // предшественником нельзя, иначе цепочка развалится.
        void SyncGroupButtons()
        {
            int index = this.comboGroup.SelectedIndex;
            bool isChain = index >= 0 && index < this.groupKeys.Count &&
                           this.groupKeys[index].StartsWith("c:", StringComparison.Ordinal);
            List<string> picked = this.GroupPicked();
            bool expandable;
            if (picked.Count > 0)
            {
                expandable = false;
                foreach (string name in picked)
                {
                    if (this.HasDaughters(name))
                    {
                        expandable = true;
                        break;
                    }
                }
            }
            else if (isChain)
            {
                expandable = true;
            }
            else
            {
                expandable = false;
                foreach (string name in this.groupMembers)
                {
                    CatalogNuclide nuclide = this.catalog.Find(name);
                    if (nuclide != null && string.IsNullOrEmpty(nuclide.Chain) && this.HasDaughters(name))
                    {
                        expandable = true;
                        break;
                    }
                }
            }
            this.buttonGroupFamily.Enabled = expandable;
            this.buttonGroupChain.Enabled = expandable;
            this.labelGroupHint.Text = picked.Count > 0
                ? string.Format(CultureInfo.CurrentCulture, this.hintPicked, picked.Count)
                : this.hintNone;
        }

        bool HasDaughters(string name)
        {
            CatalogNuclide nuclide = this.catalog.Find(name);
            if (nuclide == null || string.IsNullOrEmpty(nuclide.Chain))
            {
                return false;
            }
            CatalogChain chain = this.catalog.FindChain(nuclide.Chain);
            if (chain == null)
            {
                return false;
            }
            int start = chain.Members.IndexOf(name);
            return start >= 0 && start < chain.Members.Count - 1;
        }

        void AddFromGroup(AddMode mode)
        {
            int index = this.comboGroup.SelectedIndex;
            if (index < 0 || index >= this.groupKeys.Count)
            {
                return;
            }
            // раскрытие — по отмеченным; «добавить все» всегда работает по группе
            List<string> picked = this.GroupPicked();
            if (mode != AddMode.Single && picked.Count > 0)
            {
                foreach (string name in picked)
                {
                    this.selection.Add(this.catalog, name, mode);
                }
                this.RefreshGroupList();
                this.Rebuild();
                return;
            }

            string key = this.groupKeys[index];
            if (key.StartsWith("f:", StringComparison.Ordinal))
            {
                foreach (CatalogNuclide nuclide in this.catalog.ByFamily(key.Substring(2)))
                {
                    this.selection.AddGroupMember(this.catalog, nuclide.Name);
                }
            }
            else
            {
                CatalogChain chain = this.catalog.FindChain(key.Substring(2));
                if (chain == null)
                {
                    return;
                }
                if (mode == AddMode.Single)
                {
                    foreach (string member in chain.Members)
                    {
                        this.selection.AddGroupMember(this.catalog, member);
                    }
                }
                else
                {
                    this.selection.Add(this.catalog, chain.Root, mode);
                }
            }
            this.RefreshGroupList();
            this.Rebuild();
        }

        // Чип убирает свой источник по клику — крестик на странице делает то же самое.
        protected override bool ProcessCmdKey(ref Message message, Keys key)
        {
            if (key == Keys.Escape && this.labelFamilyInfo.Visible)
            {
                this.labelFamilyInfo.Visible = false;   // Esc закрывает словарик, как на странице
                return true;
            }
            return base.ProcessCmdKey(ref message, key);
        }

        void RemoveNuclide(string name)
        {
            this.selection.Remove(name);
            this.RefreshGroupList();
            this.Rebuild();
        }

        void RemoveXrf(string symbol)
        {
            for (int i = 0; i < this.xrfSymbols.Count; i++)
            {
                if (string.Equals(this.xrfSymbols[i], symbol, StringComparison.Ordinal))
                {
                    this.checkedXrf.SetItemChecked(i, false);   // снятие галки само уберёт элемент
                    return;
                }
            }
            this.selection.XrfElements.Remove(symbol);
            this.Rebuild();
        }

        // ─── пересборка набора ──────────────────────────────────────────────

        LineFilter CurrentFilter()
        {
            return new LineFilter
            {
                IntensityOn = this.checkIntensity.Checked,
                MinIntensity = (double)this.numMinIntensity.Value,
                RelativeIntensity = this.comboIntensityMode.SelectedIndex == 0,
                EnergyOn = this.checkEnergy.Checked,
                MinEnergy = (double)this.numMinEnergy.Value,
                MaxEnergy = (double)this.numMaxEnergy.Value,
                HalfLifeOn = this.checkHalfLife.Checked,
                MinHalfLifeYears = HalfLifeYears(this.numMinHalfLife, this.comboMinHalfLifeUnit),
                // пустое верхнее поле = «∞», как placeholder в вебе
                MaxHalfLifeYears = this.numMaxHalfLife.Value > 0
                    ? HalfLifeYears(this.numMaxHalfLife, this.comboMaxHalfLifeUnit)
                    : double.PositiveInfinity
            };
        }

        // единицы периода — те же, что в вебе: секунды, часы, сутки, годы
        static readonly double[] HalfLifeUnits = { 1.0 / 31557600.0, 1.0 / 8766.0, 1.0 / 365.25, 1.0 };

        static double HalfLifeYears(NumericUpDown value, ComboBox unit)
        {
            int index = unit.SelectedIndex >= 0 ? unit.SelectedIndex : HalfLifeUnits.Length - 1;
            return (double)value.Value * HalfLifeUnits[index];
        }

        void Rebuild()
        {
            this.builder.ScaleToSeriesParent = this.checkEquilibrium.Checked;
            this.lines = this.builder.Build(this.selection, this.CurrentFilter());
            this.beforeMerge = null;

            this.RefreshSelectedList();
            this.RefreshLines();
            this.RefreshColorChips();
        }

        // Полоса «Выбрано» — чипы .chip.on со страницы: фон --sel, рамка #7aa7ce,
        // текст --accent-ink и крестик, снимающий источник.
        void RefreshSelectedList()
        {
            this.panelSelected.SuspendLayout();
            this.panelSelected.Controls.Clear();
            foreach (KeyValuePair<string, string> entry in this.selection.Nuclides)
            {
                string name = entry.Key;
                this.panelSelected.Controls.Add(
                    this.Chip(name + " ×", delegate { this.RemoveNuclide(name); }));
            }
            foreach (string symbol in this.selection.XrfElements)
            {
                string element = symbol;
                this.panelSelected.Controls.Add(
                    this.Chip(this.xrfChipPrefix + element + " ×", delegate { this.RemoveXrf(element); }));
            }
            if (this.panelSelected.Controls.Count == 0)
            {
                Label empty = new Label();
                empty.Text = this.emptySelectionHint;
                empty.AutoSize = true;
                empty.ForeColor = WizardTheme.Muted;
                empty.Margin = new Padding(2, 4, 4, 2);
                this.panelSelected.Controls.Add(empty);
            }
            this.panelSelected.ResumeLayout();
        }

        Label Chip(string text, EventHandler onClick)
        {
            Label chip = new Label();
            chip.Text = text;
            chip.AutoSize = true;
            chip.Padding = new Padding(7, 1, 7, 1);     // .chip{padding:1px 7px}
            chip.Margin = new Padding(0, 2, 4, 2);      // .chipbar{gap:4px}
            chip.BackColor = WizardTheme.Selection;
            chip.ForeColor = WizardTheme.AccentInk;
            chip.Cursor = Cursors.Hand;
            chip.Click += onClick;
            // рамку рисуем сами: BorderStyle у Label даёт системный цвет, а нужен #7aa7ce
            chip.Paint += delegate(object sender, PaintEventArgs e)
            {
                Control control = (Control)sender;
                using (Pen pen = new Pen(WizardTheme.ChipLine))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
                }
            };
            return chip;
        }

        string xrfChipPrefix { get { return RoiWizardStrings.xrfChipPrefix; } }
        string emptySelectionHint { get { return RoiWizardStrings.emptySelectionHint; } }

        void RefreshLines()
        {
            this.suspendEvents = true;
            this.tableLines.SuspendLayout();
            this.tableModelLines.Rows.Clear();
            // «I отн.» — процент от сильнейшей линии того же нуклида, как в вебе
            Dictionary<string, double> strongest = new Dictionary<string, double>();
            foreach (SpectralLine line in this.lines)
            {
                double current;
                if (!strongest.TryGetValue(line.Nuclide, out current) || line.Intensity > current)
                {
                    strongest[line.Nuclide] = line.Intensity;
                }
            }
            bool hideUnselected = this.checkHideUnselected.Checked;
            foreach (SpectralLine line in this.lines)
            {
                // галки типов управляют видимостью, а не выбором: снятая «ХРИ» убирает
                // строки из таблицы, но линии остаются в наборе
                if (!this.IsTypeVisible(line.Type) || (hideUnselected && !line.Selected))
                {
                    continue;
                }
                double max;
                strongest.TryGetValue(line.Nuclide, out max);
                double relative = max > 0 ? 100.0 * line.Intensity / max : 0;

                Row row = new Row();
                row.Cells.Add(new Cell { Checked = line.Selected });
                row.Cells.Add(new Cell(line.Label));
                row.Cells.Add(new Cell(line.Energy.ToString("0.00", CultureInfo.CurrentCulture), line.Energy));
                Cell intensity = new Cell(line.Intensity.ToString("0.###", CultureInfo.CurrentCulture),
                                          line.Intensity);
                intensity.Tag = relative;          // Data занят сортировкой, доля бара — в Tag
                row.Cells.Add(intensity);
                row.Cells.Add(new Cell(relative.ToString("0.#", CultureInfo.CurrentCulture), relative));
                row.Cells.Add(new Cell(HalfLifeLabel(line.HalfLifeText ?? ""), line.HalfLifeYears));
                Cell kind = new Cell(this.TypeName(line.Type));
                kind.Tag = TypeKind(line.Type);    // цвет бейджа — по коду, не по подписи
                row.Cells.Add(kind);
                row.Tag = line;
                // отмеченная строка тонируется, как в вебе: tr.selrow{background:var(--sel)}
                row.BackColor = line.Selected ? WizardTheme.Selection : WizardTheme.Card;
                this.tableModelLines.Rows.Add(row);
            }
            // ResumeLayout(true), а не без аргумента: без отложенного прохода XPTable
            // не пересчитывает диапазон вертикальной полосы после смены строк, и она
            // упирается в максимум, не дойдя до последних строк (клавишами дойти можно,
            // ползунком нет), а при устаревшей позиции показывает хвост списка на пустом фоне.
            this.tableLines.ResumeLayout(true);
            this.suspendEvents = false;
            this.UpdateStatus();
        }

        bool IsTypeVisible(LineType type)
        {
            switch (type)
            {
                case LineType.Gamma: return this.checkTypeGamma.Checked;
                case LineType.Xray: return this.checkTypeXray.Checked;
                case LineType.Xrf: return this.checkTypeXrf.Checked;
                default: return this.checkTypeSecondary.Checked;
            }
        }

        // T½ каталог хранит по-русски и в машинной записи («1.6e+03 лет»). Показывается
        // он так же, как на странице: степень десятки верхним индексом, разделитель
        // дробной части из культуры, единица на языке интерфейса. Сам каталог остаётся
        // одноязычным — это данные, подписи к ним собирает форма.
        static string HalfLifeLabel(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            int space = text.LastIndexOf(' ');
            if (space <= 0)
            {
                return text;
            }
            return ScientificLabel(text.Substring(0, space)) + " "
                   + HalfLifeUnit(text.Substring(space + 1));
        }

        // Код единицы приходит из каталога машинной записью (s, m, h, d, y и доли
        // секунды) — каталог одноязычен, подпись собирается здесь. Доли секунды
        // остаются символами СИ: они одинаковы во всех языках, отдельных строк не нужно.
        static string HalfLifeUnit(string unit)
        {
            switch (unit)
            {
                case "s": return RoiWizardStrings.hlSeconds;
                case "m": return RoiWizardStrings.hlMinutes;
                case "h": return RoiWizardStrings.hlHours;
                case "d": return RoiWizardStrings.hlDays;
                case "y": return RoiWizardStrings.hlYears;
                // доли секунды — символы СИ, они одинаковы во всех языках; «us» пишется
                // микро-знаком, миллисекунды и наносекунды и так совпадают с кодом базы
                case "us": return "µs";
                case "ms": return "ms";
                case "ns": return "ns";
                default: return unit;
            }
        }

        static string ScientificLabel(string value)
        {
            string text = value;
            int mark = value.IndexOf('e');
            if (mark > 0)
            {
                string power = value.Substring(mark + 1);
                bool negative = power.StartsWith("-", StringComparison.Ordinal);
                power = power.TrimStart('+', '-').TrimStart('0');
                if (power.Length == 0)
                {
                    power = "0";
                }
                text = value.Substring(0, mark) + "·10" + (negative ? "⁻" : "") + Superscript(power);
            }
            return text.Replace(".", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
        }

        static string Superscript(string digits)
        {
            const string Plain = "0123456789";
            const string Raised = "⁰¹²³⁴⁵⁶⁷⁸⁹";
            StringBuilder builder = new StringBuilder(digits.Length);
            foreach (char symbol in digits)
            {
                int index = Plain.IndexOf(symbol);
                builder.Append(index >= 0 ? Raised[index] : symbol);
            }
            return builder.ToString();
        }

        string TypeName(LineType type)
        {
            switch (type)
            {
                case LineType.Gamma: return "γ";
                case LineType.Xray: return "X";
                case LineType.Xrf: return RoiWizardStrings.lineTypeXrf;
                default: return RoiWizardStrings.lineTypeSecondary;
            }
        }

        static string TypeKind(LineType type)
        {
            switch (type)
            {
                case LineType.Gamma: return "g";
                case LineType.Xray: return "x";
                case LineType.Xrf: return "xrf";
                default: return "sec";
            }
        }

        // Кнопки работают по ВИДИМЫМ строкам — как в вебе: при включённом «скрыть
        // невыбранные» или фильтре типов «снять все» не должно трогать то, чего
        // пользователь сейчас не видит.
        // Виды особенностей — ровно те же восемь, что в вебе, и с теми же умолчаниями
        SecondaryKind SelectedSecondaryKinds()
        {
            SecondaryKind kinds = SecondaryKind.None;
            if (this.checkSecBackscatter.Checked) kinds |= SecondaryKind.Backscatter;
            if (this.checkSecComptonEdge.Checked) kinds |= SecondaryKind.ComptonEdge;
            if (this.checkSecSingleEscape.Checked) kinds |= SecondaryKind.SingleEscape;
            if (this.checkSecDoubleEscape.Checked) kinds |= SecondaryKind.DoubleEscape;
            if (this.checkSecIodine.Checked) kinds |= SecondaryKind.IodineEscape;
            if (this.checkSecAnnihilation.Checked) kinds |= SecondaryKind.Annihilation;
            if (this.checkSecSum.Checked) kinds |= SecondaryKind.CascadeSum;
            if (this.checkSecPileUp.Checked) kinds |= SecondaryKind.PileUp;
            return kinds;
        }

        void GenerateSecondary()
        {
            SecondaryKind kinds = this.SelectedSecondaryKinds();
            if (kinds == SecondaryKind.None)
            {
                return;
            }
            // прежние маркеры заменяются: иначе повторное нажатие плодит дубли
            this.lines.RemoveAll(delegate(SpectralLine line) { return line.Type == LineType.Secondary; });
            // Метка берётся из ресурсов: перегрузка без неё подставляет зашитую
            // английскую DefaultAnnihilationLabel, из-за чего локализованный
            // ресурс annihilationLabel не использовался никогда.
            List<SpectralLine> generated = SecondaryPeaks.Generate(
                this.lines, this.Resolution, kinds, (double)this.numSecondaryMin.Value,
                RoiWizardStrings.annihilationLabel);
            this.lines.AddRange(generated);
            this.RefreshLines();
            this.statusLabel.Text = string.Format(CultureInfo.CurrentCulture,
                this.secondaryFormat, generated.Count);
        }

        // Кто ещё светит рядом: та же выборка, что на странице — γ и X всех нуклидов базы
        // плюс линии ХРИ, отсортированные по удалённости от заданной энергии.
        // Классический случай — 186 кэВ: Ra-226 3,6 % против U-235 57,2 %.
        void SearchNearby()
        {
            double energy = (double)this.numNearEnergy.Value;
            double window = (double)this.numNearWindow.Value;
            double minIntensity = (double)this.numNearIntensity.Value;
            double minHalfLife = HalfLifeYears(this.numNearHalfLife, this.comboNearHalfLifeUnit);

            this.nearHits.Clear();
            foreach (CatalogNuclide nuclide in this.catalog.Nuclides)
            {
                if (minHalfLife > 0 && nuclide.HalfLifeYears < minHalfLife)
                {
                    continue;
                }
                foreach (CatalogGammaLine gamma in nuclide.Gamma)
                {
                    if (Math.Abs(gamma.Energy - energy) <= window && gamma.Intensity >= minIntensity)
                    {
                        this.nearHits.Add(new NearHit(nuclide.Name, gamma.Energy, gamma.Intensity,
                                                      "γ", "g", nuclide.HalfLifeText,
                                                      nuclide.HalfLifeYears, null));
                    }
                }
                foreach (CatalogXrayLine xray in nuclide.Xray)
                {
                    if (Math.Abs(xray.Energy - energy) <= window && xray.Intensity >= minIntensity)
                    {
                        this.nearHits.Add(new NearHit(nuclide.Name, xray.Energy, xray.Intensity,
                                                      "X " + xray.Shell, "x",
                                                      nuclide.HalfLifeText,
                                                      nuclide.HalfLifeYears, null));
                    }
                }
            }
            foreach (XrfElement element in this.catalog.XrfElements)
            {
                foreach (XrfLine line in element.Lines)
                {
                    if (Math.Abs(line.Energy - energy) <= window)
                    {
                        this.nearHits.Add(new NearHit(
                            RoiWizardStrings.lineTypeXrf + " " + element.Symbol,
                            line.Energy, line.Intensity,
                            RoiWizardStrings.lineTypeXrf + " " + line.Label, "xrf",
                            "—", 0.0, element.Symbol));
                    }
                }
            }
            double centre = energy;
            this.nearHits.Sort(delegate(NearHit a, NearHit b)
            {
                return Math.Abs(a.Energy - centre).CompareTo(Math.Abs(b.Energy - centre));
            });

            this.tableNear.SuspendLayout();
            this.tableModelNear.Rows.Clear();
            int shown = Math.Min(this.nearHits.Count, NearHitLimit);
            for (int i = 0; i < shown; i++)
            {
                NearHit hit = this.nearHits[i];
                double delta = hit.Energy - energy;
                bool added = hit.XrfSymbol != null
                    ? this.selection.XrfElements.Contains(hit.XrfSymbol)
                    : this.selection.Nuclides.ContainsKey(hit.Nuclide);

                Row row = new Row();
                row.Cells.Add(new Cell((delta >= 0 ? "+" : "")
                    + delta.ToString("0.0", CultureInfo.CurrentCulture), delta));
                row.Cells.Add(new Cell(hit.Nuclide));
                row.Cells.Add(new Cell(hit.Energy.ToString("0.00", CultureInfo.CurrentCulture),
                                       hit.Energy));
                row.Cells.Add(new Cell(hit.Intensity.ToString("0.###", CultureInfo.CurrentCulture),
                                       hit.Intensity));
                Cell kind = new Cell(hit.TypeName);
                kind.Tag = hit.TypeKind;           // цвет бейджа — по коду, не по подписи
                row.Cells.Add(kind);
                row.Cells.Add(new Cell(HalfLifeLabel(hit.HalfLife), hit.HalfLifeYears));
                // тег ячейки — признак «есть что нажимать»: у добавленного нуклида
                // кнопки нет, как и на странице, вместо неё подпись «в наборе»
                Cell action = new Cell(added ? RoiWizardStrings.nearAdded
                                             : RoiWizardStrings.buttonNearAdd_Text);
                action.Tag = added ? null : (object)hit;
                row.Cells.Add(action);
                row.Tag = hit;
                this.tableModelNear.Rows.Add(row);
            }
            // ResumeLayout(true), а не без аргумента: без отложенного прохода XPTable
            // не пересчитывает диапазон вертикальной полосы после смены строк, и она
            // упирается в максимум, не дойдя до последних строк (клавишами дойти можно,
            // ползунком нет), а при устаревшей позиции показывает хвост списка на пустом фоне.
            this.tableNear.ResumeLayout(true);
            this.LayoutNearColumns();

            if (this.nearHits.Count == 0)
            {
                this.labelNearHint.Text = string.Format(CultureInfo.CurrentCulture,
                    this.nearEmptyFormat, energy, window);
            }
            else if (this.nearHits.Count > shown)
            {
                this.labelNearHint.Text = string.Format(CultureInfo.CurrentCulture,
                    RoiWizardStrings.nearMoreFormat, shown, this.nearHits.Count);
            }
            else
            {
                this.labelNearHint.Text = "";
            }
        }

        // Столько же строк, сколько показывает страница: дальше по списку идут линии,
        // отстоящие от заданной энергии сильнее любой из показанных.
        const int NearHitLimit = 40;

        void AddFromNearby(NearHit hit)
        {
            if (hit.XrfSymbol != null)
            {
                this.selection.XrfElements.Add(hit.XrfSymbol);
                for (int i = 0; i < this.xrfSymbols.Count; i++)
                {
                    if (string.Equals(this.xrfSymbols[i], hit.XrfSymbol, StringComparison.Ordinal))
                    {
                        this.checkedXrf.SetItemChecked(i, true);
                    }
                }
            }
            else
            {
                this.selection.Add(this.catalog, hit.Nuclide, AddMode.Single);
            }
            this.RefreshGroupList();
            this.Rebuild();
            this.SearchNearby();
        }

        sealed class NearHit
        {
            public readonly string Nuclide;
            public readonly double Energy;
            public readonly double Intensity;
            public readonly string TypeName;     // подпись бейджа: она переводится
            public readonly string TypeKind;     // код типа: по нему берётся цвет
            public readonly string HalfLife;
            public readonly double HalfLifeYears;   // ключ сортировки столбца T½
            public readonly string XrfSymbol;

            public NearHit(string nuclide, double energy, double intensity,
                           string typeName, string typeKind, string halfLife,
                           double halfLifeYears, string xrfSymbol)
            {
                this.HalfLifeYears = halfLifeYears;
                this.Nuclide = nuclide;
                this.Energy = energy;
                this.Intensity = intensity;
                this.TypeName = typeName;
                this.TypeKind = typeKind;
                this.HalfLife = string.IsNullOrEmpty(halfLife) ? "—" : halfLife;
                this.XrfSymbol = xrfSymbol;
            }
        }

        readonly List<NearHit> nearHits = new List<NearHit>();

        void SetColorMode(bool byChain)
        {
            this.colorByChain = byChain;
            this.buttonColorByChain.Enabled = !byChain;
            this.buttonColorByNuclide.Enabled = byChain;
            this.RefreshColorChips();
        }

        void SetVisibleSelected(bool value)
        {
            foreach (Row row in this.tableModelLines.Rows)
            {
                SpectralLine line = row.Tag as SpectralLine;
                if (line != null)
                {
                    line.Selected = value;
                }
            }
            this.RefreshLines();
        }

        // ─── слияние ────────────────────────────────────────────────────────

        void MergeLines()
        {
            if (this.lines.Count == 0)
            {
                return;
            }
            if (this.beforeMerge == null)
            {
                this.beforeMerge = new List<SpectralLine>(this.lines);
            }
            LineMerger merger = new LineMerger(this.Resolution, (double)this.numFactor.Value);
            this.lines = merger.Merge(this.beforeMerge);
            this.RefreshLines();
            this.statusLabel.Text = string.Format(CultureInfo.CurrentCulture,
                RoiWizardStrings.statusMerged, merger.MergedGroups, merger.AbsorbedLines);
        }

        void UnmergeLines()
        {
            if (this.beforeMerge == null)
            {
                return;
            }
            this.lines = new List<SpectralLine>(this.beforeMerge);
            this.beforeMerge = null;
            this.RefreshLines();
        }

        void UpdateMergeInfo()
        {
            if (this.suspendEvents)
            {
                return;
            }
            LineMerger merger = new LineMerger(this.Resolution, (double)this.numFactor.Value);
            this.labelMergeInfo.Text = string.Format(CultureInfo.CurrentCulture, this.mergeInfoFormat,
                this.numFactor.Value, merger.ThresholdAt(100), merger.ThresholdAt(662), merger.ThresholdAt(1500));
        }

        void TakeResolutionFromSpectrum()
        {
            if (this.resolutionProvider == null)
            {
                return;
            }
            double value = this.resolutionProvider();
            if (value > 0)
            {
                this.numResolution.Value = Math.Min(this.numResolution.Maximum,
                    Math.Max(this.numResolution.Minimum, (decimal)value));
            }
            else
            {
                MessageBox.Show(this, RoiWizardStrings.noResolutionFromSpectrum,
                    this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // ─── шаг 3 ──────────────────────────────────────────────────────────

        void SyncZoneControls()
        {
            bool zones = this.comboStyle.SelectedIndex != 0;
            this.comboWidthMode.Enabled = zones;
            this.numZonePercent.Enabled = zones && this.comboWidthMode.SelectedIndex == 0;
            this.numZoneFactor.Enabled = zones && this.comboWidthMode.SelectedIndex == 1;
            this.ApplyExporterSettings();
        }

        void ApplyExporterSettings()
        {
            this.zones = new ZoneCalculator(this.Resolution);
            this.zones.Style = (RoiStyle)this.comboStyle.SelectedIndex;
            this.zones.WidthMode = (ZoneWidthMode)Math.Max(0, this.comboWidthMode.SelectedIndex);
            this.zones.ZonePercent = (double)this.numZonePercent.Value;
            this.zones.ZoneFwhmFactor = (double)this.numZoneFactor.Value;
            this.exporter = new SetExporter(this.Resolution, this.zones);
        }

        void RefreshAnchorCombo()
        {
            // Выбранный вручную якорь запоминается и восстанавливается: список
            // перезаполняется при каждом возврате на вкладку экспорта, и
            // SelectedIndex = 0 ниже молча сбрасывал ручной выбор обратно на
            // «авто». Пользователь узнавал об этом только по составу ROI.
            SpectralLine keep = this.comboAnchor.SelectedIndex > 0 &&
                                this.comboAnchor.SelectedIndex - 1 < this.anchorCandidates.Count
                ? this.anchorCandidates[this.comboAnchor.SelectedIndex - 1]
                : null;
            this.comboAnchor.Items.Clear();
            // Кандидаты держатся списком, а не вычисляются по индексу заново: список
            // выбранных линий меняется галками, и индекс в комбобоксе иначе съезжает
            // на соседнюю линию. ХРИ и вторичные маркеры в кандидаты не попадают —
            // якорь на них означал бы опору с условным положением или интенсивностью.
            this.anchorCandidates.Clear();
            SpectralLine automatic = AnchorPicker.Pick(this.SelectedLines(), this.Resolution);
            this.comboAnchor.Items.Add(automatic != null
                ? string.Format(CultureInfo.CurrentCulture, RoiWizardStrings.anchorAuto,
                    automatic.Label, automatic.Energy.ToString("0.0", CultureInfo.CurrentCulture))
                : "auto");
            foreach (SpectralLine line in this.SelectedLines())
            {
                if (!AnchorPicker.IsAcceptable(line))
                {
                    continue;
                }
                this.anchorCandidates.Add(line);
                this.comboAnchor.Items.Add(line.Label + " " + line.Energy.ToString("0.0", CultureInfo.CurrentCulture));
            }
            int restored = 0;
            if (keep != null)
            {
                int at = this.anchorCandidates.IndexOf(keep);
                if (at >= 0)
                {
                    restored = at + 1;                   // нулевой пункт — «авто»
                }
            }
            this.comboAnchor.SelectedIndex = restored;
        }

        readonly List<SpectralLine> anchorCandidates = new List<SpectralLine>();
        readonly List<string> groupMembers = new List<string>();
        readonly bool russian =
            Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "ru";
        string mergeInfoFormat { get { return RoiWizardStrings.mergeInfoFormat; } }
        string statusFormat { get { return RoiWizardStrings.statusFormat; } }
        string secondaryFormat { get { return RoiWizardStrings.secondaryFormat; } }
        string nearEmptyFormat { get { return RoiWizardStrings.nearEmptyFormat; } }
        bool suppressGroupCheck;
        string hintPicked { get { return RoiWizardStrings.hintPicked; } }
        string hintNone { get { return RoiWizardStrings.hintNone; } }

        void SyncSetControls()
        {
            this.comboAnchor.Enabled = !this.checkFullSet.Checked;
            this.labelAnchor.Enabled = !this.checkFullSet.Checked;
        }

        List<SpectralLine> SelectedLines()
        {
            List<SpectralLine> result = new List<SpectralLine>();
            foreach (SpectralLine line in this.lines)
            {
                if (line.Selected)
                {
                    result.Add(line);
                }
            }
            return result;
        }

        SpectralLine CurrentAnchor()
        {
            int index = this.comboAnchor.SelectedIndex;
            if (index <= 0)
            {
                return null;                       // 0 — автоматический выбор
            }
            return index - 1 < this.anchorCandidates.Count ? this.anchorCandidates[index - 1] : null;
        }

        // Ядро отдаёт код замечания и подстановки, фразу собирает форма — иначе
        // тексты проверок пришлось бы держать в ядре и они не переводились бы.
        static string Describe(SetIssue issue)
        {
            string format;
            switch (issue.Kind)
            {
                case IssueKind.EqualEnergies: format = RoiWizardStrings.issueEqualEnergies; break;
                case IssueKind.ZeroYield: format = RoiWizardStrings.issueZeroYield; break;
                case IssueKind.AnchorIsXrf: format = RoiWizardStrings.issueAnchorIsXrf; break;
                case IssueKind.AnchorIsSecondary: format = RoiWizardStrings.issueAnchorIsSecondary; break;
                case IssueKind.NoAnchor: format = RoiWizardStrings.issueNoAnchor; break;
                case IssueKind.AnchorIsXray: format = RoiWizardStrings.issueAnchorIsXray; break;
                default: format = RoiWizardStrings.issueZonesOverlap; break;
            }
            return issue.Args == null || issue.Args.Length == 0
                ? format
                : string.Format(CultureInfo.CurrentCulture, format, issue.Args);
        }

        void RunChecks()
        {
            this.ApplyExporterSettings();
            this.listIssues.BeginUpdate();
            this.listIssues.Items.Clear();
            foreach (SetIssue issue in SetChecker.Check(this.lines, false, this.zones, this.Resolution))
            {
                this.listIssues.Items.Add(RoiWizardStrings.issuePrefixRoi + " · " + Describe(issue));
            }
            // проверяется то, что реально уйдёт в библиотеку: при рекомендованном составе
            // это не содержимое таблицы, а отобранные фильтром линии источников
            SpectralLine manual = this.checkFullSet.Checked ? null : this.CurrentAnchor();
            List<SpectralLine> manualAnchors = null;
            if (manual != null)
            {
                manualAnchors = new List<SpectralLine>();
                manualAnchors.Add(manual);
            }
            foreach (SetIssue issue in SetChecker.Check(this.LibraryLines(manualAnchors), true,
                                                        this.zones, this.Resolution, manualAnchors))
            {
                if (issue.Level == IssueLevel.Error)
                {
                    this.listIssues.Items.Add(RoiWizardStrings.issuePrefixSet + " · " + Describe(issue));
                }
            }
            if (this.listIssues.Items.Count == 0)
            {
                this.listIssues.Items.Add(RoiWizardStrings.issueNone);
            }
            this.listIssues.EndUpdate();
        }

        // Что именно ляжет в файл — тем же сериализатором, каким пишет ROIConfigManager.
        // Собирать «похожий» XML руками нельзя: предпросмотр показывал бы не тот текст.
        void PreviewXml()
        {
            List<SpectralLine> selected = this.SelectedLines();
            if (selected.Count == 0)
            {
                this.textPreview.Text = this.previewEmpty;
                return;
            }
            this.ApplyExporterSettings();
            ROIConfigData built = this.exporter.BuildRoiConfig(this.lines, this.textConfigName.Text,
                                                              this.ColorOfLine);
            // Предпросмотр должен показывать ФАЙЛ, а не заготовку: имя файла и Guid
            // берутся у той записи, которую кнопка «Создать» будет писать. При
            // перезаписи это существующая запись со своим Guid, при создании — новый.
            // Расходится только LastUpdated: его SaveConfig ставит в момент записи.
            string filename = SafeFileName(built.Name) + ".xml";
            ROIConfigData target = FindRoiConfig(filename);
            built.Filename = filename;
            built.OriginalFilename = filename;
            if (target != null)
            {
                // Перезапись: кнопка «Создать» НЕ заменяет запись целиком, а
                // правит существующую — Name и ROIDefinitions, — оставляя всё
                // прочее как было. Прежде всего кривую эффективности: в файле
                // она сохранялась, а предпросмотр показывал built, где её нет,
                // и обещание «что именно ляжет в файл» не выполнялось.
                // Зеркалим путь записи по КОПИИ, чтобы не тронуть живую запись.
                ROIConfigData preview = target.Clone();
                preview.Name = built.Name;
                preview.ROIDefinitions.Clear();
                preview.ROIDefinitions.AddRange(built.ROIDefinitions);
                preview.Filename = filename;
                preview.OriginalFilename = filename;
                built = preview;
            }
            // без шапки с замечаниями: они уже перечислены в панели «Проверка данных»
            // прямо над этим полем, а на странице такой панели нет — там шапка и нужна
            this.textPreview.Text = Serialize(built);
            this.textPreview.Select(0, 0);
        }

        // Запись конфигурации, которая уже занимает этот файл. Ключ — имя файла, а не
        // Guid: SaveConfig пишет по Filename, поэтому именно совпадение имён означает,
        // что две записи будут драться за один файл.
        static ROIConfigData FindRoiConfig(string filename)
        {
            foreach (ROIConfigData existing in ROIConfigManager.GetInstance().ROIConfigList)
            {
                if (string.Equals(existing.Filename, filename, StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }
            }
            return null;
        }

        static string Serialize(ROIConfigData config)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ROIConfigData));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream, config);
                // читаем байты, а не StringWriter: тот объявил бы кодировку utf-16,
                // тогда как в файл уходит utf-8
                return new UTF8Encoding(false).GetString(stream.ToArray()).TrimStart('\uFEFF');
            }
        }

        string previewEmpty { get { return RoiWizardStrings.previewEmpty; } }

        void CreateRoiConfig()
        {
            List<SpectralLine> selected = this.SelectedLines();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, RoiWizardStrings.noLinesSelected, this.Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            this.ApplyExporterSettings();

            List<SetIssue> issues = SetChecker.Check(this.lines, false, this.zones, this.Resolution);
            if (issues.Count > 0 && !this.Confirm(issues, false))
            {
                return;
            }

            ROIConfigData built = this.exporter.BuildRoiConfig(this.lines, this.textConfigName.Text,
                                                              this.ColorOfLine);

            // SaveConfig пишет файл по Filename, поэтому запись с уже занятым именем файла
            // не создаётся заново, а перезаписывается на месте. Второй CreateConfig оставил
            // бы в ROIConfigList две записи на один файл: обе живые, у каждой свой Guid, и
            // победила бы та, которую сохранят последней — первая молча теряла бы правки.
            string filename = SafeFileName(built.Name) + ".xml";
            ROIConfigManager manager = ROIConfigManager.GetInstance();
            ROIConfigData config = FindRoiConfig(filename);
            if (config != null)
            {
                if (!this.ConfirmOverwriteRoi(built.Name))
                {
                    return;
                }
            }
            else
            {
                // Регистрировать конфигурацию обязан сам менеджер: CreateConfig кладёт её и в
                // ROIConfigList, и в ROIConfigMap, и поднимает ROIConfigListChanged. Простое
                // добавление в список оставило бы карту пустой, а SaveConfig начинается с
                // roiConfigMap[Guid] — то есть упал бы KeyNotFoundException.
                config = manager.CreateConfig(filename);
                if (config == null)
                {
                    return;                              // менеджер уже показал сообщение об ошибке
                }
            }
            config.Name = built.Name;
            config.ROIDefinitions.Clear();
            config.ROIDefinitions.AddRange(built.ROIDefinitions);
            manager.SaveConfig(config);                  // он же поднимает ROIConfigListChanged

            this.statusLabel.Text = string.Format(CultureInfo.CurrentCulture,
                RoiWizardStrings.statusRoiCreated, config.Name, config.ROIDefinitions.Count);
        }

        // Что уходит в библиотеку: либо отмеченное в таблице, либо рекомендованный состав
        // — линии источников, отобранные измеренным фильтром (0.7·FWHM до более сильной,
        // ≥1 % на распад родителя), минуя галки и слияние. См. LineSetBuilder.
        List<SpectralLine> LibraryLines(IList<SpectralLine> mustKeep)
        {
            return this.checkFullSet.Checked
                ? this.builder.BuildRecommendedSet(this.selection, this.Resolution, mustKeep)
                : this.lines;
        }

        void CreateNuclideSet()
        {
            // Ручной якорь собирается ДО набора: в рекомендованном составе он обязан
            // пережить оба фильтра (якорь U-238 — Pa-234m 1001.03 кэВ, 0.842 %, порог по
            // интенсивности не проходит), а без якоря библиотечный фит не запускается.
            SpectralLine manualAnchor = this.checkFullSet.Checked ? null : this.CurrentAnchor();
            List<SpectralLine> mustKeep = null;
            if (manualAnchor != null)
            {
                mustKeep = new List<SpectralLine>();
                mustKeep.Add(manualAnchor);
            }

            List<SpectralLine> library = this.LibraryLines(mustKeep);
            if (Count(library) == 0)
            {
                MessageBox.Show(this, RoiWizardStrings.noLinesSelected, this.Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Ручной якорь уходит один — его выбрал пользователь. При автовыборе
            // помечается несколько линий: LibraryPeakFitter требует, чтобы с найденным
            // пиком совпала хотя бы одна, и единственный якорь делает набор хрупким.
            List<SpectralLine> anchors = mustKeep;
            int anchorCount = (int)this.numAnchors.Value;

            // для набора совпавшие энергии и нулевая интенсивность — ошибки: две линии на
            // одной позиции вырождают подгонку амплитуд, а Intencity = 0 выбрасывает линию
            // из связки по цепочке
            List<SetIssue> issues = SetChecker.Check(library, true, this.zones, this.Resolution, anchors);
            List<SetIssue> errors = issues.FindAll(delegate(SetIssue i) { return i.Level == IssueLevel.Error; });
            if (errors.Count > 0)
            {
                this.Confirm(errors, true);
                return;
            }

            // повторное нажатие добавило бы в библиотеку полный дубль записей
            if (!this.ConfirmDuplicateSet(this.textSetName.Text))
            {
                return;
            }

            List<NuclideDefinition> definitions;
            NuclideSet set = this.exporter.BuildNuclideSet(library, this.textSetName.Text, this.ColorOfLine,
                                                           anchors, anchorCount, out definitions);

            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();
            manager.NuclideSets.Add(set);
            manager.NuclideDefinitions.AddRange(definitions);
            // SaveDefinitionFile поднимает NuclideDefinitionListChanged — на него
            // подписан DCPeakDetectionView, поэтому новый набор виден в «Обнаружении
            // пиков» сразу, без перезапуска.
            manager.SaveDefinitionFile();

            int marked = definitions.FindAll(delegate(NuclideDefinition d) { return d.IsAnchor; }).Count;
            this.statusLabel.Text = string.Format(CultureInfo.CurrentCulture,
                RoiWizardStrings.statusSetCreated,
                set.Name, definitions.Count, marked);
        }

        static int Count(List<SpectralLine> lines)
        {
            int count = 0;
            foreach (SpectralLine line in lines)
            {
                if (line.Selected)
                {
                    count++;
                }
            }
            return count;
        }

        bool ConfirmOverwriteRoi(string name)
        {
            return MessageBox.Show(this,
                string.Format(CultureInfo.CurrentCulture,
                    RoiWizardStrings.confirmRoiOverwrite, name),
                this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        bool ConfirmDuplicateSet(string name)
        {
            foreach (NuclideSet existing in NuclideDefinitionManager.GetInstance().NuclideSets)
            {
                if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return MessageBox.Show(this,
                        string.Format(CultureInfo.CurrentCulture,
                            RoiWizardStrings.confirmSetDuplicate, name),
                        this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
                }
            }
            return true;
        }

        bool Confirm(List<SetIssue> issues, bool blocking)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine(blocking
                ? RoiWizardStrings.confirmErrorsHead
                : RoiWizardStrings.confirmIssuesHead);
            text.AppendLine();
            for (int i = 0; i < issues.Count && i < 8; i++)
            {
                text.AppendLine("• " + Describe(issues[i]));
            }
            if (issues.Count > 8)
            {
                text.AppendLine("…");
            }
            if (blocking)
            {
                text.AppendLine();
                text.AppendLine(RoiWizardStrings.confirmErrorsTail);
                MessageBox.Show(this, text.ToString(), this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            text.AppendLine();
            text.Append(RoiWizardStrings.confirmSaveAnyway);
            return MessageBox.Show(this, text.ToString(), this.Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        // цвет по нуклиду: одинаковые для линий одного источника, чтобы набор читался
        // цвет назначается «владельцу»: цепочке или нуклиду — как в вебе
        readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
        bool colorByChain = true;

        string OwnerOf(SpectralLine line)
        {
            if (line.Type == LineType.Xrf)
            {
                return line.Nuclide;                       // ХРИ всегда красятся по элементу
            }
            if (!this.colorByChain)
            {
                return line.Nuclide;
            }
            CatalogNuclide nuclide = this.catalog.Find(line.Nuclide);
            string root = nuclide != null ? this.catalog.ChainRoot(nuclide) : null;
            return string.IsNullOrEmpty(root) ? line.Nuclide : root;
        }

        Color ColorForOwner(string owner)
        {
            Color color;
            if (!this.colors.TryGetValue(owner, out color))
            {
                color = Palette[this.colors.Count % Palette.Length];
                this.colors[owner] = color;
            }
            return color;
        }

        // Чипы владельцев: квадрат цвета и подпись; клик по квадрату — выбор цвета,
        // как «input type=color» на странице.
        void RefreshColorChips()
        {
            List<string> owners = new List<string>();
            foreach (SpectralLine line in this.lines)
            {
                string owner = this.OwnerOf(line);
                if (!owners.Contains(owner))
                {
                    owners.Add(owner);
                }
            }
            // Полоса чипов живёт строкой кнопок режима: размеры задаются здесь, уже после
            // автомасштаба формы, поэтому считаются от фактической высоты кнопки, а не
            // от чисел разметки — иначе чипы встают выше центра.
            int row = this.buttonColorByChain.Height;
            this.panelColors.Top = this.buttonColorByChain.Top;
            this.panelColors.WrapContents = true;      // .colorchips{flex-wrap:wrap}
            this.panelColors.AutoScroll = false;
            this.panelColors.Height = row * MaxColorRows;

            this.panelColors.SuspendLayout();
            this.panelColors.Controls.Clear();
            foreach (string owner in owners)
            {
                this.panelColors.Controls.Add(this.ColorChip(owner, row - 4));
            }
            this.panelColors.ResumeLayout(true);

            // высота — по факту раскладки: сколько строк заняли чипы, столько и берём
            int bottom = row;
            foreach (Control chip in this.panelColors.Controls)
            {
                bottom = Math.Max(bottom, chip.Bottom + chip.Margin.Bottom);
            }
            if (bottom > row * MaxColorRows)
            {
                bottom = row * MaxColorRows;
                this.panelColors.AutoScroll = true;    // владельцев больше, чем строк
            }
            this.panelColors.Height = bottom;
            this.LayoutExport();
        }

        const int MaxColorRows = 3;

        // Чип цвета — коробочка .cchip: фон --chip, рамка --line, квадрат цвета
        // и подпись владельца. Щелчок по чипу открывает выбор цвета.
        Control ColorChip(string owner, int height)
        {
            const int Side = 18;
            int textWidth = TextRenderer.MeasureText(owner, this.panelColors.Font).Width;

            Panel chip = new Panel();
            chip.Size = new Size(6 + Side + 6 + textWidth + 6, height);
            chip.Margin = new Padding(0, 2, 5, 2);
            chip.BackColor = WizardTheme.Chip;
            chip.Cursor = Cursors.Hand;
            string captured = owner;
            chip.Click += delegate { this.PickColor(captured, chip); };
            chip.Paint += delegate(object sender, PaintEventArgs e)
            {
                Control box = (Control)sender;
                using (Pen pen = new Pen(WizardTheme.Line))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, box.Width - 1, box.Height - 1);
                }
                Rectangle square = new Rectangle(6, (box.Height - Side) / 2, Side, Side);
                using (SolidBrush brush = new SolidBrush(this.ColorForOwner(captured)))
                {
                    e.Graphics.FillRectangle(brush, square);
                }
                using (Pen pen = new Pen(WizardTheme.Line))
                {
                    e.Graphics.DrawRectangle(pen, square);
                }
                TextRenderer.DrawText(e.Graphics, captured, this.panelColors.Font,
                    new Rectangle(square.Right + 6, 0, box.Width - square.Right - 8, box.Height),
                    WizardTheme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            };
            return chip;
        }

        // Вкладка «Оформление и экспорт» складывается сверху вниз: полоса цветов
        // растёт на вторую строку, и всё под ней должно съезжать.
        void LayoutExport()
        {
            int width = this.tabExport.ClientSize.Width;
            int height = this.tabExport.ClientSize.Height;
            if (width < 120 || height < 160)
            {
                return;
            }
            const int Pad = 8;
            const int Gap = 6;
            this.groupStyle.Height = this.panelColors.Bottom + this.panelColors.Margin.Bottom + 12;
            int y = 6;
            this.groupStyle.SetBounds(Pad, y, width - Pad * 2, this.groupStyle.Height);
            y += this.groupStyle.Height + Gap;
            this.groupExport.SetBounds(Pad, y, width - Pad * 2, this.groupExport.Height);
            y += this.groupExport.Height + Gap;
            this.labelIssues.Location = new Point(Pad + 4, y);
            y += this.labelIssues.Height + 2;
            this.listIssues.SetBounds(Pad, y, width - Pad * 2, this.listIssues.Height);
            y += this.listIssues.Height + Gap;
            int rest = height - Pad - y;
            if (rest < 60)
            {
                rest = 60;
            }
            this.textPreview.SetBounds(Pad, y, width - Pad * 2, rest);
        }

        void PickColor(string owner, Panel swatch)
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = this.ColorForOwner(owner);
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.colors[owner] = dialog.Color;
                    swatch.BackColor = dialog.Color;
                }
            }
        }

        static readonly Color[] Palette = {
            Color.FromArgb(230, 130, 30), Color.FromArgb(192, 53, 53), Color.FromArgb(46, 125, 50),
            Color.FromArgb(21, 101, 192), Color.FromArgb(123, 31, 162), Color.FromArgb(0, 131, 143),
            Color.FromArgb(158, 122, 16), Color.FromArgb(216, 27, 96), Color.FromArgb(93, 64, 55)
        };

        Color ColorOfLine(SpectralLine line)
        {
            return this.ColorForOwner(this.OwnerOf(line));
        }

        static Color ColorOf(SpectralLine line)
        {
            int hash = 0;
            string key = line.Nuclide ?? "";
            for (int i = 0; i < key.Length; i++)
            {
                hash = (hash * 31 + key[i]) & 0x7fffffff;
            }
            return Palette[hash % Palette.Length];
        }

        // Имена устройств DOS: файл «CON.xml» не создаётся ни при каком расширении,
        // а имя, кончающееся точкой или пробелом, Windows молча обрезает — и тогда
        // Filename в конфигурации перестаёт совпадать с именем файла на диске.
        static readonly string[] ReservedNames = {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        static string SafeFileName(string name)
        {
            StringBuilder result = new StringBuilder();
            foreach (char c in name ?? "")
            {
                result.Append(Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }
            // хвостовые точки и пробелы отсекаются самой файловой системой
            string text = result.ToString().Trim().TrimEnd('.', ' ');
            if (text.Length == 0)
            {
                return "ROI set";
            }
            // зарезервировано и само имя, и оно же с любым расширением
            foreach (string reserved in ReservedNames)
            {
                if (string.Equals(text, reserved, StringComparison.OrdinalIgnoreCase))
                {
                    return text + "_";
                }
            }
            return text;
        }

        // Справка — тот же текст, что в модальном окне страницы; он лежит ресурсом,
        // выгруженным из index.html, поэтому расходиться версиям негде.
        //
        // Окно показывается такой же плавающей панелью, как и сам мастер: тогда его
        // заголовок рисует та же тема хоста, и два окна модуля выглядят одинаково.
        // Подбирать цвета руками для второго окна бессмысленно — они разъедутся при
        // любой смене темы. Вне док-системы (тесты, автономный прогон формы) остаётся
        // обычный диалог.
        void ShowHelp()
        {
            if (this.helpForm != null && !this.helpForm.IsDisposed)
            {
                this.helpForm.Activate();
                return;
            }
            this.helpForm = new HelpForm();
            if (this.DockPanel != null)
            {
                Size want = this.helpForm.Size;
                // Экранные координаты, а не координаты внутри контейнера:
                // Show(dockPanel, bounds) для плавающего окна ждёт экранные, а
                // this.Left/Top — положение панели в её родителе. На одном
                // мониторе разница выглядела просто смещением, на нескольких
                // справка уезжала в угол не того экрана.
                Rectangle onScreen = this.RectangleToScreen(this.ClientRectangle);
                Rectangle bounds = new Rectangle(
                    onScreen.Left + Math.Max(0, (onScreen.Width - want.Width) / 2),
                    onScreen.Top + Math.Max(0, (onScreen.Height - want.Height) / 2),
                    want.Width, want.Height);
                this.helpForm.Show(this.DockPanel, bounds);
            }
            else
            {
                this.helpForm.Show(this);
            }
        }

        HelpForm helpForm;

        // Кнопки внизу подписываются именами соседних шагов, а на краях — обобщённо
        // и выключены. То же поведение, что у пары кнопок в строке состояния страницы.
        void GoStep(int delta)
        {
            int target = this.tabs.SelectedIndex + delta;
            if (target >= 0 && target < this.tabs.TabCount)
            {
                this.tabs.SelectedIndex = target;
            }
        }

        void UpdateStepButtons()
        {
            int current = this.tabs.SelectedIndex;
            this.buttonStepPrev.Enabled = current > 0;
            this.buttonStepNext.Enabled = current < this.tabs.TabCount - 1;
            this.buttonStepPrev.Text = current > 0
                ? "◂ " + this.stepNames[current - 1]
                : this.stepBack;
            this.buttonStepNext.Text = current < this.tabs.TabCount - 1
                ? this.stepNames[current + 1] + " ▸"
                : this.stepForward;
        }

        string[] stepNames
        {
            get
            {
                return new string[] {
                    RoiWizardStrings.stepNuclides, RoiWizardStrings.stepLines,
                    RoiWizardStrings.stepExport };
            }
        }
        string stepBack { get { return RoiWizardStrings.stepBack; } }
        string stepForward { get { return RoiWizardStrings.stepForward; } }

        void UpdateStatus()
        {
            int selected = 0;
            Dictionary<string, bool> nuclides = new Dictionary<string, bool>();
            foreach (SpectralLine line in this.lines)
            {
                if (line.Selected)
                {
                    selected++;
                    nuclides[line.Nuclide] = true;
                }
            }
            this.statusLabel.Text = string.Format(CultureInfo.CurrentCulture, this.statusFormat,
                selected, this.lines.Count, nuclides.Count);
        }

    }
}
