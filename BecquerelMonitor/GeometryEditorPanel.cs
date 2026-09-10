using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    /// <summary>
    /// Редактор геометрии — вкладка конструктора кривой эффективности.
    ///
    /// Был отдельной формой, писавшей файл `.in`. Стал контролом, потому что
    /// геометрия переехала в конфигурацию прибора: файла у неё нет, сохранять
    /// нечего и некуда, а правится она там же, где и кривая.
    ///
    /// Зачем свой, когда есть GMaster: формат LSRM умеет только цилиндрические
    /// кристаллы, а у половины наших детекторов кристалл прямоугольный, и
    /// приведение к цилиндру теряет объём (28 % у Обсидиана, 21.5 % у RC103).
    /// Здесь форма задаётся честно, а в файл вдобавок кладётся равнообъёмный
    /// цилиндр по правилу самого LSRM — чтобы файл открывался и их программой.
    ///
    /// Разметка собирается кодом, а не дизайнером: полей полсотни, и руками
    /// расставленный контрол однажды уже потерялся вовсе — не появился ни на
    /// экране, ни в дереве UI Automation.
    /// </summary>
    public sealed class GeometryEditorPanel : UserControl
    {
        readonly Dictionary<string, TextBox> fields =
            new Dictionary<string, TextBox>(StringComparer.Ordinal);

        readonly Dictionary<string, ComboBox> materials =
            new Dictionary<string, ComboBox>(StringComparer.Ordinal);

        readonly Dictionary<string, Label> compositions =
            new Dictionary<string, Label>(StringComparer.Ordinal);

        /// <summary>
        /// Какой вид веществ показывает список этой строки. Нужен, чтобы после
        /// правки библиотеки (E20) собрать списки заново — тем же набором, что
        /// и при разметке.
        /// </summary>
        readonly Dictionary<string, GeometryMaterialLibrary.MaterialKind> materialKinds =
            new Dictionary<string, GeometryMaterialLibrary.MaterialKind>(StringComparer.Ordinal);

        // Вещества из файла, которых нет в библиотеке. Пока пользователь не
        // выбрал замену из списка, в модель идёт ровно то, что пришло из файла:
        // подстановка первой строки библиотеки означала бы, что майлар молча
        // становится фторопластом — размеры целы, кривая чужая.
        readonly Dictionary<string, GeometryMaterial> foreignMaterials =
            new Dictionary<string, GeometryMaterial>(StringComparer.Ordinal);

        RadioButton cylinderRadio;
        RadioButton boxRadio;
        ComboBox facingCombo;
        Button fwhmSuggestButton;
        double fwhmSuggestionPercent;
        Label equivalentLabel;
        ComboBox sourceTypeCombo;
        Panel pointPanel, cylinderPanel, marinelliPanel, boxPanel;
        Panel sourceMaterialsPanel;

        /// <summary>
        /// Галка «измерение в защите» (`AMBER12`) и её пояснение. Стоят на
        /// вкладке пробы, под веществами: защита — свойство ОБСТАНОВКИ
        /// измерения, как и съёмка на грунте, а не слой детектора и не
        /// стенка сосуда.
        /// </summary>
        CheckBox shieldCheck;

        Panel shieldPanel;
        Panel cylinderSizePanel, boxSizePanel;

        // (`AMBER1`) Столбик размеров обвязки и стоящая под ним стопка веществ.
        // Держатся полями, потому что строка «зазор сбоку» СНИМАЕТСЯ у
        // цилиндра, столбик после этого пересобирается, и вещества обязаны
        // поехать за ним: они стоят по абсолютной координате, а не стыковкой.
        Panel wrappingPanel, materialsPanel;
        ComboBox presetCombo;
        Label sceneLabel;

        /// <summary>
        /// Строки списка «Тип источника» по порядку: форма плюс вид съёмки
        /// (E27). Две последние — съёмки в поле; форма у них штатная, и весь
        /// расчёт идёт прежним кодом, а вид говорит, каким правилом считаются
        /// размеры. Таблица одна на список, чтение и запись — чтобы порядок
        /// строк нельзя было развести по трём местам.
        /// </summary>
        static readonly KeyValuePair<GeometrySourceType, GeometrySceneKind>[] SourceKinds =
        {
            new KeyValuePair<GeometrySourceType, GeometrySceneKind>(
                GeometrySourceType.Point, GeometrySceneKind.None),
            new KeyValuePair<GeometrySourceType, GeometrySceneKind>(
                GeometrySourceType.Cylinder, GeometrySceneKind.None),
            new KeyValuePair<GeometrySourceType, GeometrySceneKind>(
                GeometrySourceType.Marinelli, GeometrySceneKind.None),
            new KeyValuePair<GeometrySourceType, GeometrySceneKind>(
                GeometrySourceType.Box, GeometrySceneKind.None),
            new KeyValuePair<GeometrySourceType, GeometrySceneKind>(
                GeometrySourceType.Cylinder, GeometrySceneKind.Ground),
            new KeyValuePair<GeometrySourceType, GeometrySceneKind>(
                GeometrySourceType.Marinelli, GeometrySceneKind.Borehole),
        };

        static int IndexOfSource(GeometryModel g)
        {
            for (int i = 0; i < SourceKinds.Length; i++)
            {
                if (SourceKinds[i].Key == g.SourceType && SourceKinds[i].Value == g.Scene)
                {
                    return i;
                }
            }

            // Вид съёмки, не сошедшийся с формой (файл правили руками): форма
            // главнее — она и есть то, чем будет считаться сцена.
            for (int i = 0; i < SourceKinds.Length; i++)
            {
                if (SourceKinds[i].Key == g.SourceType && SourceKinds[i].Value == GeometrySceneKind.None)
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// Верхняя энергия расчёта, кэВ — по ней размечаются готовые сцены
        /// (E27). Умолчание совпадает с заводским полем «до» конструктора
        /// кривой; настоящее значение подаёт форма, как и подсказку разрешения.
        /// </summary>
        double sceneEnergyKev = 3000.0;

        GeometrySketch detectorSketch, sourceSketch;

        GeometryModel model;

        /// <summary>
        /// Геометрия как её оставили. Панель правит СВОЮ копию: пока не нажали
        /// «Сохранить», чужая конфигурация остаётся нетронутой.
        /// </summary>
        public GeometryModel Model
        {
            get { return this.model; }
        }

        /// <summary>Правили ли что-нибудь с последней загрузки.</summary>
        public bool Dirty { get; private set; }

        /// <summary>Сообщить наружу, что править начали, — для кнопки «Сохранить».</summary>
        public event EventHandler Changed;

        public GeometryEditorPanel()
        {
            this.model = Blank();
            this.BuildLayout();
            // Через SetModel, а не напрямую: заготовка заезжает в поля тем же
            // путём, что и чужая геометрия, и так же не считается правкой.
            // Прямой вызов LoadFromModel объявлял панель изменённой сразу при
            // создании — до того, как пользователь её увидел.
            this.SetModel(null);
        }

        /// <summary>
        /// Показать другую геометрию. Пустая означает «геометрии нет» —
        /// подставляется заготовка, а не нули: от нулевой толщины отражателя
        /// расчёт молча меняет смысл.
        /// </summary>
        public void SetModel(GeometryModel source)
        {
            // Загрузка проходит через те же обработчики, что и правка руками, и
            // хвост LoadFromModel перерисовывает чертёж уже со снятым флагом
            // loading. Без своей заглушки открытие конфигурации объявляло бы её
            // изменённой, ничего не изменив.
            this.suppressChanged = true;
            try
            {
                this.model = source == null ? Blank() : source.Clone();
                this.LoadFromModel();
            }
            finally
            {
                this.suppressChanged = false;
            }

            this.Dirty = false;
        }

        bool suppressChanged;

        /// <summary>
        /// Подсказка разрешения из ПШПВ-калибровки привязанного прибора, % на
        /// 662 кэВ (E14). Ноль — подсказки нет, кнопка прячется. Считает её
        /// ФОРМА: панель редактирует геометрию и про прибор не знает, а тянуть
        /// сюда конфигурацию ради одного числа значило бы связать редактор со
        /// всем деревом конфигов.
        /// </summary>
        public void SetFwhmSuggestion(double percent)
        {
            this.fwhmSuggestionPercent = percent > 0.0 ? percent : 0.0;
            if (this.fwhmSuggestButton != null)
            {
                this.fwhmSuggestButton.Visible = this.fwhmSuggestionPercent > 0.0;
            }
        }

        /// <summary>
        /// Верхняя энергия расчёта, кэВ (E27). По ней — и только по ней —
        /// размечаются готовые сцены съёмки в поле: свободный пробег в грунте
        /// растёт вдвое от 662 к 3000 кэВ, и сцена, посчитанная по середине
        /// шкалы, занижала бы её верх. Решение Amber 16.08.2026.
        ///
        /// Знает это ФОРМА, а не панель: поле «до» стоит в её же настройках
        /// расчёта, а тянуть их сюда значило бы связать редактор геометрии со
        /// всем конструктором — тем же доводом, что и у подсказки разрешения.
        /// </summary>
        public void SetSceneEnergy(double kev)
        {
            if (kev > 0.0)
            {
                this.sceneEnergyKev = kev;
                this.UpdateSceneHint("");
            }
        }

        /// <summary>
        /// Забрать отредактированное. false — в полях ошибка, о ней уже
        /// сказано пользователю.
        /// </summary>
        public bool TryCommit()
        {
            if (!this.MarkBadValues())
            {
                MessageBox.Show(this, Resources.GeometryEditorErrorNumber,
                                Resources.GeometryEditorTitle,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            GeometryModel g = this.BuildModel();
            string error = this.Validate(g);
            if (error != null)
            {
                MessageBox.Show(this, error, Resources.GeometryEditorTitle,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            this.model = g;
            this.Dirty = false;
            return true;
        }

        void RaiseChanged()
        {
            if (this.suppressChanged)
            {
                return;
            }

            this.Dirty = true;
            EventHandler handler = this.Changed;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Заготовка для новой геометрии: сцинтиллятор в типичной обвязке.
        /// Числа — не «ноль», а правдоподобные: пустая форма заставляет
        /// заполнять двадцать полей вслепую, а от нулевой толщины отражателя
        /// расчёт молча меняет смысл. Единица — МИЛЛИМЕТР, как и везде в модели.
        ///
        /// Открыта наружу (T16, 08.08.2026): это и есть «встроенный шаблон»,
        /// которым пробы обязаны строить геометрию, когда её надо восстановить.
        /// Второй набор тех же чисел, набранный в пробе руками, разошёлся бы с
        /// формой при первой же правке заготовки.
        /// </summary>
        public static GeometryModel Blank()
        {
            GeometryModel g = new GeometryModel
            {
                Name = "geometry",
                IsScintillator = true,
                SourceType = GeometrySourceType.Point,
                CrystalDiameter = 25.4,
                CrystalHeight = 25.4,
                FrontReflectorThickness = 1.0,
                SideReflectorThickness = 1.0,
                FrontCladdingThickness = 0.5,
                SideCladdingThickness = 0.5,
                // Зазор между отражателем и корпусом (`AMBER1`): расстояние по
                // умолчанию НОЛЬ — слово Amber. Ноль означает «слоя нет», и
                // сцена собирается ровно как до задачи.
                FrontGapThickness = 0.0,
                SideGapThickness = 0.0,
                MountingThickness = 1.0,
                PointDistance = 100.0,
                BeakerToDetectorDistance = 5.0,
                BeakerDiameter = 40.0,
                BeakerHeight = 20.0,
                BeakerSideWallThickness = 1.0,
                BeakerEndWallThickness = 1.0,
                SourceHeight = 20.0,
                MarinelliToDetectorDistance = 1.0,
                MarinelliBeakerDiameter = 114.0,
                MarinelliBeakerHeight = 89.0,
                MarinelliHoleDiameter = 61.0,
                MarinelliHoleHeight = 53.0,
                MarinelliSideThickness = 2.0,
                MarinelliEndWallThickness = 2.0,
                MarinelliHoleSideThickness = 2.0,
                MarinelliHoleEndWallThickness = 2.0,
                MarinelliSourceHeight = 85.0,
            };

            g.Crystal = Make("Cesium iodide");
            g.Reflector = Make("Polytetrafluoroethylene");
            // Наполнитель зазора — ВОЗДУХ по слову Amber (`AMBER1`). При
            // нулевой толщине он ничего не меняет; вещество ставится сразу,
            // чтобы человек, набрав толщину, не получил молчаливый вакуум.
            g.Gap = Make("Air, dry");
            g.Cladding = Make("Aluminum");
            g.BeakerWall = Make("Polyethylene");
            // Воздух, а не вода: заготовка открывается с точечным источником, и
            // вещество пробы в ней — то, чего у пользователя ЕЩЁ нет. Вода
            // самопоглощением молча съедает низ шкалы, и заметить это в готовой
            // кривой нечем; воздух не поглощает ничего, и всё, что стоит в
            // сумме, поставил человек.
            g.Source = Make("Air, dry");
            return g;
        }

        static GeometryMaterial Make(string name)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            return entry != null
                ? GeometryMaterialLibrary.Make(entry, entry.Density)
                : new GeometryMaterial();
        }

        // ------------------------------------------------------------------
        // Разметка
        // ------------------------------------------------------------------

        void BuildLayout()
        {
            // Панель, а не окно: ни заголовка, ни размера, ни кнопок «ОК» и
            // «Отмена» — их место занимает общая кнопка сохранения конструктора.
            TabControl tabs = new TabControl { Dock = DockStyle.Fill };

            TabPage detector = new TabPage(Resources.GeometryEditorTabDetector) { UseVisualStyleBackColor = true };
            TabPage source = new TabPage(Resources.GeometryEditorTabSource) { UseVisualStyleBackColor = true };
            tabs.TabPages.Add(detector);
            tabs.TabPages.Add(source);
            this.Controls.Add(tabs);

            this.BuildDetectorTab(detector);
            this.BuildSourceTab(source);

        }

        void BuildDetectorTab(TabPage tab)
        {
            this.detectorSketch = this.AddSketch(tab, GeometrySketch.SketchMode.Detector);
            Panel page = FieldColumn(tab);

            // Готовые детекторы — самым верхом: обвязку сцинтиллятора по памяти
            // не восстановить, а ошибка в ней стоит десятков процентов.
            page.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(14, 14),
                Text = Resources.GeometryEditorPreset,
            });

            this.presetCombo = new ComboBox
            {
                Location = new Point(160, 11),
                Size = new Size(300, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            this.presetCombo.Items.Add(Resources.GeometryEditorPresetPrompt);
            foreach (GeometryPresets.Preset preset in GeometryPresets.Items)
            {
                this.presetCombo.Items.Add(preset);
            }

            this.presetCombo.SelectedIndex = 0;
            this.presetCombo.SelectedIndexChanged += this.PresetChanged;
            page.Controls.Add(this.presetCombo);

            this.cylinderRadio = new RadioButton
            {
                AutoSize = true,
                Location = new Point(14, 46),
                Text = Resources.GeometryEditorShapeCylinder,
                Checked = true,
            };

            this.boxRadio = new RadioButton
            {
                AutoSize = true,
                Location = new Point(190, 46),
                Text = Resources.GeometryEditorShapeBox,
            };

            this.cylinderRadio.CheckedChanged += this.ShapeChanged;
            page.Controls.Add(this.cylinderRadio);
            page.Controls.Add(this.boxRadio);

            // E21: какой стороной детектор обращён к пробе. Стоит рядом с
            // формой кристалла нарочно — это свойство той же пары «кристалл и
            // проба», и включается оно только у бруска: у цилиндра боковая
            // постановка не осесимметрична и сценой не выражается.
            //
            // Цена ошибки здесь измерена: у спектра Lu₂O₃ на Nano 16 Pro
            // разница между «с торца» и «сбоку» — втрое по каскадной сумме, и
            // разбор списывал её на несуществующую линию 511 (S46, §13и).
            this.facingCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(336, 44),
                Size = new Size(268, 21),
            };
            this.facingCombo.Items.Add(Resources.GeometryEditorFacingFront);
            this.facingCombo.Items.Add(Resources.GeometryEditorFacingSide);
            this.facingCombo.SelectedIndex = 0;
            this.facingCombo.SelectedIndexChanged += this.FacingChanged;
            page.Controls.Add(this.facingCombo);

            this.cylinderSizePanel = new Panel { Location = new Point(0, ShapeTop), Width = 620 };
            int y = 0;
            this.Row(this.cylinderSizePanel, ref y, "CrystalDiameter", Resources.GeometryEditorCrystalDiameter);
            this.Row(this.cylinderSizePanel, ref y, "CrystalHeight", Resources.GeometryEditorCrystalHeight);
            page.Controls.Add(this.cylinderSizePanel);

            // Подпись о равноценном цилиндре в русском в одну строку не
            // помещается, а панель детей обрезает — и строка с объёмом
            // кристалла пропадала под соседним полем. Высота считается по ней
            // же (`FitPanel`), поэтому число здесь больше не стоит: вторая
            // строка подписи расширяет панель сама.
            this.boxSizePanel = new Panel { Location = new Point(0, ShapeTop), Width = 620, Visible = false };
            y = 0;
            this.Row(this.boxSizePanel, ref y, "CrystalBoxX", Resources.GeometryEditorBoxX);
            this.Row(this.boxSizePanel, ref y, "CrystalBoxY", Resources.GeometryEditorBoxY);
            this.Row(this.boxSizePanel, ref y, "CrystalBoxZ", Resources.GeometryEditorBoxZ);
            this.equivalentLabel = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(14, y + 4),
                MaximumSize = new Size(600, 0),
            };
            this.boxSizePanel.Controls.Add(this.equivalentLabel);
            page.Controls.Add(this.boxSizePanel);

            Panel rest = new Panel { Location = new Point(0, ShapeTop), Width = 620 };
            this.wrappingPanel = rest;
            y = 0;
            this.Row(rest, ref y, "FrontReflectorThickness", Resources.GeometryEditorFrontReflector);
            this.Row(rest, ref y, "SideReflectorThickness", Resources.GeometryEditorSideReflector);
            // (`AMBER1`, задача Amber 07.09.2026) Зазор стоит МЕЖДУ отражателем
            // и корпусом и в столбике тоже: порядок строк здесь читается как
            // порядок слоёв от кристалла наружу, и переставить их значит
            // рассказать про прибор неправду.
            this.Row(rest, ref y, "FrontGapThickness", Resources.GeometryEditorFrontGap);
            this.Row(rest, ref y, "SideGapThickness", Resources.GeometryEditorSideGap);
            this.Row(rest, ref y, "FrontCladdingThickness", Resources.GeometryEditorFrontCladding);
            this.Row(rest, ref y, "SideCladdingThickness", Resources.GeometryEditorSideCladding);
            this.Row(rest, ref y, "MountingThickness", Resources.GeometryEditorMounting);

            // Разрешение прибора (E14): без него допуск пика нулевой и поправка
            // на однократное рассеяние не даёт ничего — а ввести его раньше
            // было негде, ключ DS_Fwhm662 читался только из файла. Кнопка
            // подставляет число из ПШПВ-калибровки привязанного прибора; сама
            // подстановка живёт у формы — панель прибора не знает.
            int fwhmRowY = y;
            this.Row(rest, ref y, "FwhmAt662Percent", Resources.GeometryEditorFwhm662,
                     Resources.GeometryEditorUnitPercent);
            this.fwhmSuggestButton = new Button
            {
                Location = new Point(430, fwhmRowY - 1),
                Size = new Size(150, 23),
                Text = Resources.GeometryEditorFwhmFromDevice,
                UseVisualStyleBackColor = true,
                Visible = false,
            };
            this.fwhmSuggestButton.Click += (s, e) =>
            {
                if (this.fwhmSuggestionPercent > 0.0)
                {
                    // Присваивание текстом, как правка руками: TextChanged
                    // поднимет Changed, и «Сохранить» оживёт.
                    this.fields["FwhmAt662Percent"].Text =
                        this.fwhmSuggestionPercent.ToString("0.###", CultureInfo.InvariantCulture);
                }
            };
            rest.Controls.Add(this.fwhmSuggestButton);
            page.Controls.Add(rest);

            Panel mats = new Panel { Location = new Point(0, ShapeTop), Width = 620 };
            this.materialsPanel = mats;
            y = 0;
            this.MaterialRow(mats, ref y, "Crystal", Resources.GeometryEditorCrystalMaterial,
                             GeometryMaterialLibrary.MaterialKind.Crystal);
            this.MaterialRow(mats, ref y, "Reflector", Resources.GeometryEditorReflectorMaterial,
                             GeometryMaterialLibrary.MaterialKind.Reflector);
            // (`AMBER1`) Наполнитель зазора берётся из списка ПРОБ, а не
            // отражателей: зазор — это вещество, НАЛИТОЕ В ПУСТОТУ, и
            // единственный газ библиотеки (`Air, dry`) значится именно там.
            // Своего вида веществ ему не заведено нарочно: он был бы пуст —
            // воздух пришлось бы либо задваивать, либо отнять у проб, где он
            // стоит умолчанием заготовки. Всё остальное достаётся из «прочих»,
            // которые дописываются в конец любого списка.
            this.MaterialRow(mats, ref y, "Gap", Resources.GeometryEditorGapMaterial,
                             GeometryMaterialLibrary.MaterialKind.Source);
            this.MaterialRow(mats, ref y, "Cladding", Resources.GeometryEditorCladdingMaterial,
                             GeometryMaterialLibrary.MaterialKind.Cladding);
            page.Controls.Add(mats);

            // Окно открывается цилиндром, а у цилиндра бока у зазора нет
            // (`AMBER1`). Событие смены формы здесь не сработает — переключатель
            // УЖЕ отмечен, — поэтому строка снимается прямо тут.
            this.UpdateGapRows(this.boxRadio.Checked);
        }

        /// <summary>
        /// Верх первой панели вкладки детектора: под готовым детектором, формой
        /// кристалла и стороной, обращённой к пробе. Всё, что ниже, стоит друг
        /// за другом и своей координаты не имеет.
        /// </summary>
        const int ShapeTop = 70;

        /// <summary>Просвет между соседними панелями столбика.</summary>
        const int PanelGap = 6;

        /// <summary>
        /// Запас под нижним контролом панели. Восемь точек — ровно столько
        /// оставляла прежняя ручная арифметика (шаг строки 28 при поле высотой
        /// 20), так что вид панелей от перехода на счёт не поехал.
        /// </summary>
        const int PanelPad = 8;

        /// <summary>
        /// Высота панели ПО ЕЁ СОДЕРЖИМОМУ: низ самого нижнего показанного
        /// контрола плюс запас.
        ///
        /// ⛔ Числом высоту не писать. Панель детей ОБРЕЗАЕТ молча — ни
        /// прокрутки, ни следа: колонка вокруг (`FieldColumn`) прокручивает
        /// себя, а не чужое переполнение, и контрол просто исчезает. Так уже
        /// вышло дважды: `E27` увёл за край вещества пробы, а `AMBER1` добавил
        /// в стопку веществ детектора четвёртую строку («наполнитель зазора»)
        /// — 4 × 48 = 192 против записанных 150, и «Cladding material»
        /// обрезался на снимке Amber 08.09.2026.
        ///
        /// ⛔ Спрашивать <see cref="Control.Visible"/> здесь НЕЛЬЗЯ — см.
        /// <see cref="RowControls.Shown"/>: у WinForms она ФАКТИЧЕСКАЯ и false
        /// у всех детей невыбранной вкладки разом, и панель ужалась бы в точку.
        /// Судим по НАШЕМУ решению — <see cref="HiddenControls"/>.
        /// </summary>
        void FitPanel(Control panel)
        {
            if (panel == null)
            {
                return;
            }

            HashSet<Control> hidden = this.HiddenControls();
            int bottom = 0;
            foreach (Control child in panel.Controls)
            {
                if (hidden.Contains(child) || child.Bottom <= bottom)
                {
                    continue;
                }

                bottom = child.Bottom;
            }

            panel.Height = Math.Max(1, bottom + PanelPad);
        }

        /// <summary>
        /// Контролы, снятые НАШИМ решением: в высоту панели они не считаются.
        /// Строки полей помнят это в <see cref="RowControls.Shown"/>, строки
        /// веществ — в <see cref="materialRowShown"/>; всё остальное показано.
        /// </summary>
        HashSet<Control> HiddenControls()
        {
            HashSet<Control> hidden = new HashSet<Control>();
            foreach (RowControls row in this.rows.Values)
            {
                if (row.Shown)
                {
                    continue;
                }

                hidden.Add(row.Label);
                hidden.Add(row.Box);
                hidden.Add(row.Units);
            }

            foreach (KeyValuePair<string, List<Control>> pair in this.materialRows)
            {
                bool shown;
                if (!this.materialRowShown.TryGetValue(pair.Key, out shown) || shown)
                {
                    continue;
                }

                foreach (Control control in pair.Value)
                {
                    hidden.Add(control);
                }
            }

            return hidden;
        }

        /// <summary>
        /// Пересобрать вкладку детектора: размеры кристалла, обвязка и вещества
        /// встают друг за другом, каждая панель — по своему содержимому.
        ///
        /// Зовётся всякий раз, когда содержимое могло поехать: сменилась форма
        /// кристалла (панель размеров у бруска выше на строку и подпись),
        /// снялась или вернулась строка зазора сбоку (`AMBER1`), переписался
        /// состав вещества или подпись о равноценном цилиндре — обе AutoSize и
        /// в русском переносятся на вторую строку.
        ///
        /// ⚠ Двух соседей <see cref="Reflow"/> не знает и знать не может:
        /// кнопка «взять у прибора» стоит в том же столбике по абсолютной
        /// координате строки полуширины, а стопка веществ — сосед столбика,
        /// а не его строка. Оба переставляются здесь, иначе кнопка уезжает от
        /// своего поля, а вещества оставляют под собой пустую полосу.
        /// </summary>
        void ReflowDetector()
        {
            if (this.wrappingPanel == null)
            {
                return;
            }

            // Панели двух форм кристалла стоят одна поверх другой: место под
            // обвязкой считается по ТОЙ, что выбрана, а не по самой высокой.
            Panel shape = this.boxRadio != null && this.boxRadio.Checked
                        ? this.boxSizePanel : this.cylinderSizePanel;
            if (shape != null)
            {
                this.FitPanel(shape);
                this.wrappingPanel.Top = shape.Bottom + PanelGap;
            }

            this.Reflow(this.wrappingPanel);

            RowControls fwhm;
            if (this.fwhmSuggestButton != null
                && this.rows.TryGetValue("FwhmAt662Percent", out fwhm))
            {
                // ⚠ Сперва кнопку на место, и только потом мерить панель:
                // после снятой строки кнопка ещё стоит внизу и завысила бы
                // высоту, а второй мерки уже не будет.
                this.fwhmSuggestButton.Top = fwhm.Box.Top - 1;
                this.FitPanel(this.wrappingPanel);
            }

            if (this.materialsPanel != null)
            {
                this.FitPanel(this.materialsPanel);
                this.materialsPanel.Top = this.wrappingPanel.Bottom + PanelGap;
            }
        }

        /// <summary>
        /// То же для вкладки источника: вещества встают под ТЕМ набором полей,
        /// что сейчас показан, и панель веществ меряется по своим строкам.
        /// </summary>
        void ReflowSource()
        {
            if (this.sourceMaterialsPanel == null)
            {
                return;
            }

            Panel shown = this.ShownSourcePanel();
            if (shown != null)
            {
                this.FitPanel(shown);
                this.sourceMaterialsPanel.Top = shown.Bottom + SourceMaterialsGap;
            }

            this.FitPanel(this.sourceMaterialsPanel);
            this.ReflowShield();
        }

        /// <summary>
        /// Поставить панель защиты (`AMBER12`) под стопкой веществ пробы.
        ///
        /// ⛔ Зовётся из ОБОИХ мест, где стопка веществ меняет высоту:
        /// <see cref="ReflowSource"/> и <see cref="ReflowMaterials"/>.
        /// Иначе снятая строка вещества оставляла бы галку висеть на
        /// прежнем месте — с наложением на соседа или с провалом.
        /// </summary>
        void ReflowShield()
        {
            if (this.shieldPanel == null || this.sourceMaterialsPanel == null)
            {
                return;
            }

            this.shieldPanel.Top = this.sourceMaterialsPanel.Bottom + SourceMaterialsGap;
            this.FitPanel(this.shieldPanel);
        }

        /// <summary>Просвет над стопкой веществ пробы — шире, чем между полями.</summary>
        const int SourceMaterialsGap = 16;

        /// <summary>
        /// Панель полей той формы источника, что выбрана в списке. Спрашиваем
        /// СПИСОК, а не `Visible` панелей — по той же причине, что и в
        /// <see cref="FitPanel"/>.
        /// </summary>
        Panel ShownSourcePanel()
        {
            int index = this.sourceTypeCombo != null ? this.sourceTypeCombo.SelectedIndex : -1;
            if (index < 0 || index >= SourceKinds.Length)
            {
                return this.pointPanel;
            }

            switch (SourceKinds[index].Key)
            {
                case GeometrySourceType.Cylinder:
                    return this.cylinderPanel;
                case GeometrySourceType.Marinelli:
                    return this.marinelliPanel;
                case GeometrySourceType.Box:
                    return this.boxPanel;
                default:
                    return this.pointPanel;
            }
        }

        /// <summary>
        /// Показать «зазор сбоку» только там, где бок у зазора есть.
        ///
        /// ⛔ Решение Amber 07.09.2026, вопросником, дословно: «ASN 80x80 —
        /// цилиндр, у неё только торец, а бока нет. Для неё и таких же, где
        /// бока нет, — одно поле. Где есть и торец и бок — каждое
        /// индивидуальное поле.» То есть у ЦИЛИНДРА строка снимается, а у
        /// бруска стоит.
        ///
        /// ⚠ Правило сказано ПРО ЗАЗОР и только про него: боковые толщины
        /// отражателя и корпуса у цилиндра остаются на месте.
        /// </summary>
        void UpdateGapRows(bool box)
        {
            this.ShowRow("SideGapThickness", box, null);
            this.ReflowDetector();
        }

        /// <summary>
        /// Чертёж справа от полей — как в конструкторе LSRM. Без него из двух
        /// десятков чисел не видно, что за что отвечает.
        ///
        /// Раскладка на Dock, а не на Anchor. С якорем чертёж вылез втрое за
        /// свои границы: Anchor выставлялся в инициализаторе, ДО добавления в
        /// родителя, и привязки считались от ещё не размеченной вкладки
        /// (200x100) — при её росте до настоящего размера контрол вырос на ту
        /// же разницу. Dock считается на разметке и такого не знает.
        /// </summary>
        GeometrySketch AddSketch(TabPage page, GeometrySketch.SketchMode mode)
        {
            GeometrySketch sketch = new GeometrySketch { Mode = mode, Dock = DockStyle.Fill };
            page.Controls.Add(sketch);
            // Заполняющий контрол должен стоять ПЕРВЫМ в списке: стыковка идёт с
            // конца списка к началу, и последним размещается тот, кто занимает
            // остаток. С обратным порядком чертёж получал всю ширину вкладки и
            // просто закрывался колонкой полей — видна была его правая треть.
            sketch.BringToFront();
            return sketch;
        }

        /// <summary>
        /// Левая колонка с полями: чертёж занимает всё, что осталось.
        ///
        /// Прокрутка (E27) — потому что колонка стала выше: у маринелли поля
        /// источника и без того доходили до 440 точек, а строка готовой сцены
        /// добавила ещё 64, и на форме, ужатой до её же MinimumSize, вещества
        /// пробы уезжали за нижний край НЕЗАМЕТНО — контрол не пропадает, он
        /// просто обрезается родителем. Ширина колонки взята с запасом на
        /// полосу прокрутки: панели полей внутри — 620 точек.
        /// </summary>
        static Panel FieldColumn(TabPage page)
        {
            Panel column = new Panel { Dock = DockStyle.Left, Width = 640, AutoScroll = true };
            page.Controls.Add(column);
            column.SendToBack();
            return column;
        }

        void BuildSourceTab(TabPage tab)
        {
            this.sourceSketch = this.AddSketch(tab, GeometrySketch.SketchMode.Source);
            Panel page = FieldColumn(tab);
            Label typeLabel = new Label
            {
                AutoSize = true,
                Location = new Point(14, 15),
                Text = Resources.GeometryEditorSourceType,
            };

            this.sourceTypeCombo = new ComboBox
            {
                Location = new Point(210, 12),
                Size = new Size(180, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            // Порядок строк = SourceKinds. Съёмки в поле (E27) стоят ЗДЕСЬ, в
            // одном списке с формами, а не отдельным списком рядом: для
            // человека это такая же геометрия пробы, как маринелли, и выбор у
            // неё один — либо она, либо цилиндр.
            this.sourceTypeCombo.Items.Add(Resources.GeometryEditorSourcePoint);
            this.sourceTypeCombo.Items.Add(Resources.GeometryEditorSourceCylinder);
            this.sourceTypeCombo.Items.Add(Resources.GeometryEditorSourceMarinelli);
            this.sourceTypeCombo.Items.Add(Resources.GeometryEditorSourceBox);
            this.sourceTypeCombo.Items.Add(Resources.GeometryEditorSourceGround);
            this.sourceTypeCombo.Items.Add(Resources.GeometryEditorSourceBorehole);
            this.sourceTypeCombo.Width = 260;
            this.sourceTypeCombo.SelectedIndexChanged += this.SourceTypeChanged;
            page.Controls.Add(typeLabel);
            page.Controls.Add(this.sourceTypeCombo);

            // Чем сцена вышла такой. Без этой строки размеры выглядят взятыми с
            // потолка: полтора метра грунта под прибором объясняются только
            // свободным пробегом, а его в полях нет. Молчит у обычных сцен.
            this.sceneLabel = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(14, 40),
                MaximumSize = new Size(590, 0),
            };
            page.Controls.Add(this.sceneLabel);

            this.pointPanel = new Panel { Location = new Point(0, SourceFieldsTop), Width = 620 };
            int y = 0;
            this.Row(this.pointPanel, ref y, "PointDistance", Resources.GeometryEditorPointDistance);
            page.Controls.Add(this.pointPanel);

            this.cylinderPanel = new Panel { Location = new Point(0, SourceFieldsTop), Width = 620, Visible = false };
            y = 0;
            this.Row(this.cylinderPanel, ref y, "BeakerDiameter", Resources.GeometryEditorBeakerDiameter);
            this.Row(this.cylinderPanel, ref y, "BeakerHeight", Resources.GeometryEditorBeakerHeight);
            this.Row(this.cylinderPanel, ref y, "BeakerSideWallThickness", Resources.GeometryEditorBeakerSideWall);
            this.Row(this.cylinderPanel, ref y, "BeakerEndWallThickness", Resources.GeometryEditorBeakerEndWall);
            this.Row(this.cylinderPanel, ref y, "SourceHeight", Resources.GeometryEditorSourceHeight);
            this.Row(this.cylinderPanel, ref y, "BeakerToDetectorDistance", Resources.GeometryEditorBeakerToDetector);
            page.Controls.Add(this.cylinderPanel);

            this.marinelliPanel = new Panel { Location = new Point(0, SourceFieldsTop), Width = 620, Visible = false };
            y = 0;
            this.Row(this.marinelliPanel, ref y, "MarinelliBeakerDiameter", Resources.GeometryEditorBeakerDiameter);
            this.Row(this.marinelliPanel, ref y, "MarinelliBeakerHeight", Resources.GeometryEditorBeakerHeight);
            this.Row(this.marinelliPanel, ref y, "MarinelliHoleDiameter", Resources.GeometryEditorHoleDiameter);
            this.Row(this.marinelliPanel, ref y, "MarinelliHoleHeight", Resources.GeometryEditorHoleHeight);
            this.Row(this.marinelliPanel, ref y, "MarinelliSideThickness", Resources.GeometryEditorBeakerSideWall);
            this.Row(this.marinelliPanel, ref y, "MarinelliEndWallThickness", Resources.GeometryEditorBeakerEndWall);
            this.Row(this.marinelliPanel, ref y, "MarinelliHoleSideThickness", Resources.GeometryEditorHoleSideWall);
            this.Row(this.marinelliPanel, ref y, "MarinelliHoleEndWallThickness", Resources.GeometryEditorHoleEndWall);
            this.Row(this.marinelliPanel, ref y, "MarinelliSourceHeight", Resources.GeometryEditorSourceHeight);
            this.Row(this.marinelliPanel, ref y, "MarinelliToDetectorDistance", Resources.GeometryEditorBeakerToDetector);
            page.Controls.Add(this.marinelliPanel);

            // Прямоугольная кювета. Поля те же, что у цилиндрической, только
            // вместо диаметра две стороны — их и меряют на приборе, полными.
            this.boxPanel = new Panel { Location = new Point(0, SourceFieldsTop), Width = 620, Visible = false };
            y = 0;
            this.Row(this.boxPanel, ref y, "BoxSourceX", Resources.GeometryEditorBoxSourceX);
            this.Row(this.boxPanel, ref y, "BoxSourceY", Resources.GeometryEditorBoxSourceY);
            this.Row(this.boxPanel, ref y, "BoxSideWallThickness", Resources.GeometryEditorBeakerSideWall);
            this.Row(this.boxPanel, ref y, "BoxEndWallThickness", Resources.GeometryEditorBeakerEndWall);
            this.Row(this.boxPanel, ref y, "BoxSourceHeight", Resources.GeometryEditorSourceHeight);
            this.Row(this.boxPanel, ref y, "BoxToDetectorDistance", Resources.GeometryEditorBeakerToDetector);
            page.Controls.Add(this.boxPanel);

            // Вещества стоят под ТЕМ, что сейчас показано, а не под самым
            // высоким из трёх: у точечного источника одно поле, у маринелли
            // одиннадцать, и место под маринелли оставляло у точки пустую
            // полосу в три сотни точек.
            this.sourceMaterialsPanel = new Panel { Location = new Point(0, SourceFieldsTop), Width = 620 };
            y = 0;
            this.MaterialRow(this.sourceMaterialsPanel, ref y, "BeakerWall",
                             Resources.GeometryEditorWallMaterial,
                             GeometryMaterialLibrary.MaterialKind.BeakerWall);
            this.MaterialRow(this.sourceMaterialsPanel, ref y, "Source",
                             Resources.GeometryEditorSourceMaterial,
                             GeometryMaterialLibrary.MaterialKind.Source);
            page.Controls.Add(this.sourceMaterialsPanel);

            // (`AMBER12`, задача Amber 09.09.2026) Измерение в защите. Домик
            // в сцену расчёта НЕ кладётся и клейма матрицы не меняет —
            // признак читает только разбор: он говорит, что вокруг детектора
            // есть обстановка, в которой квант рассеивается назад. Такое
            // рассеяние матрица не считает (её сцена — кристалл, обвязка и
            // проба), и при защите образ обратного рассеяния остаётся.
            this.shieldPanel = new Panel { Location = new Point(0, SourceFieldsTop), Width = 620 };
            this.shieldCheck = new CheckBox
            {
                AutoSize = true,
                Location = new Point(14, 0),
                Text = Resources.GeometryEditorInShield,
            };
            this.shieldCheck.CheckedChanged += this.ValueChanged;
            this.shieldPanel.Controls.Add(this.shieldCheck);
            Label shieldHint = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(32, 22),
                MaximumSize = new Size(570, 0),
                Text = Resources.GeometryEditorInShieldHint,
            };
            this.shieldPanel.Controls.Add(shieldHint);
            page.Controls.Add(this.shieldPanel);

            // Столбик собирается сразу: список форм источника ещё не трогали, а
            // окно уже может открыться на этой вкладке.
            this.ReflowSource();
        }

        /// <summary>Верх полей источника: под списком форм и подсказкой сцены.</summary>
        const int SourceFieldsTop = 76;

        /// <summary>Строка «подпись — поле — см».</summary>
        void Row(Control parent, ref int y, string key, string caption)
        {
            this.Row(parent, ref y, key, caption, null);
        }

        void Row(Control parent, ref int y, string key, string caption, string unit)
        {
            if (Find(key) == null)
            {
                // Поле без места в модели: оно бы заполнялось пользователем и
                // никуда не попадало. Ошибка разработчика, и ловить её надо
                // сразу, а не по кривой, которая тихо посчиталась не по тем
                // размерам.
                throw new InvalidOperationException("нет места в модели для поля " + key);
            }

            Label label = new Label
            {
                AutoSize = true,
                Location = new Point(14, y + 4),
                Text = caption,
            };

            parent.Controls.Add(label);

            TextBox box = new TextBox
            {
                Location = new Point(300, y),
                Size = new Size(90, 20),
                TextAlign = HorizontalAlignment.Right,
            };
            box.TextChanged += this.ValueChanged;
            // Фокус в поле подсвечивает его размер на чертеже. Из двадцати
            // чисел иначе не понять, какое из них сейчас правишь, — а у тонких
            // слоёв подписи стоят вплотную друг к другу.
            box.Enter += (s, e) => this.SetHighlight(key);
            box.Leave += (s, e) => this.FieldLeft(key);
            parent.Controls.Add(box);
            this.fields[key] = box;

            Label units = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(396, y + 4),
                Text = unit ?? Resources.GeometryEditorUnitMm,
            };

            parent.Controls.Add(units);

            // Строка запоминается ЦЕЛИКОМ и по порядку: у съёмок в поле сосуда
            // нет вовсе, и его поля с них снимаются (E27) — а снять надо все
            // три контрола сразу и потом сдвинуть оставшиеся, иначе на месте
            // убранной строки остаётся дыра.
            this.rows[key] = new RowControls { Label = label, Box = box, Units = units };
            this.rowOrder.Add(new KeyValuePair<Control, string>(parent, key));
            y += 28;
        }

        sealed class RowControls
        {
            public Label Label;
            public TextBox Box;
            public Label Units;
            public string Caption;          // подпись «как обычно», для возврата

            /// <summary>
            /// Показана ли строка ПО НАШЕМУ РЕШЕНИЮ. Спрашивать об этом сам
            /// контрол нельзя: `Control.Visible` у WinForms — видимость
            /// ФАКТИЧЕСКАЯ, и пока не выбрана вкладка, она false у всех детей
            /// разом. На этом уже обожглись: пересборка столбика, случившаяся
            /// при невыбранной вкладке, сочла невидимыми ВСЕ строки, ужала
            /// панель до одной точки, и поля источника пропали с экрана совсем.
            /// </summary>
            public bool Shown = true;
        }

        readonly Dictionary<string, RowControls> rows =
            new Dictionary<string, RowControls>(StringComparer.Ordinal);

        readonly List<KeyValuePair<Control, string>> rowOrder =
            new List<KeyValuePair<Control, string>>();

        /// <summary>
        /// Показать строку или снять её, заодно поменяв подпись. Пустая подпись
        /// — оставить прежнюю.
        /// </summary>
        void ShowRow(string key, bool visible, string caption)
        {
            RowControls row;
            if (!this.rows.TryGetValue(key, out row))
            {
                return;
            }

            if (row.Caption == null)
            {
                row.Caption = row.Label.Text;
            }

            row.Label.Text = string.IsNullOrEmpty(caption) ? row.Caption : caption;
            row.Shown = visible;
            row.Label.Visible = visible;
            row.Box.Visible = visible;
            row.Units.Visible = visible;
        }

        /// <summary>
        /// Пересобрать столбик строк панели: видимые встают подряд, снятые не
        /// оставляют дыры. Панель ужимается по последней строке — под ней стоят
        /// вещества, и лишняя пустота уехала бы вместе с ними.
        /// </summary>
        void Reflow(Control parent)
        {
            int y = 0;
            foreach (KeyValuePair<Control, string> pair in this.rowOrder)
            {
                if (pair.Key != parent)
                {
                    continue;
                }

                RowControls row = this.rows[pair.Value];
                if (!row.Shown)
                {
                    continue;
                }

                row.Label.Top = y + 4;
                row.Box.Top = y;
                row.Units.Top = y + 4;
                y += 28;
            }

            this.FitPanel(parent);
        }

        /// <summary>
        /// Наполнить список веществ: сначала СВОЙ вид, потом «прочие».
        ///
        /// Прочие — 268 веществ таблицы ЛСРМ (`materials.dat`), у которых
        /// назначения в файле нет. Разложить их по нашим пяти видам можно было
        /// бы только угадыванием — свинец это оправа или проба, стекло сосуд
        /// или образец, зависит от съёмки. Поэтому они идут ПОСЛЕ выверенного
        /// короткого списка, а не вместо него и не вместо выбора человека: и
        /// привычные вещества остаются сверху, и не спрятано ничего.
        /// </summary>
        static void FillMaterialCombo(ComboBox combo, GeometryMaterialLibrary.MaterialKind kind)
        {
            foreach (GeometryMaterialLibrary.Entry entry in GeometryMaterialLibrary.Of(kind))
            {
                combo.Items.Add(entry);
            }

            if (kind == GeometryMaterialLibrary.MaterialKind.Other)
            {
                return;
            }

            foreach (GeometryMaterialLibrary.Entry entry
                     in GeometryMaterialLibrary.Of(GeometryMaterialLibrary.MaterialKind.Other))
            {
                combo.Items.Add(entry);
            }
        }

        /// <summary>Строка «вещество — плотность — состав».</summary>
        void MaterialRow(Control parent, ref int y, string key, string caption,
                         GeometryMaterialLibrary.MaterialKind kind)
        {
            List<Control> group = new List<Control>();
            this.materialRows[key] = group;
            Label title = new Label
            {
                AutoSize = true,
                Location = new Point(14, y + 4),
                Text = caption,
            };

            parent.Controls.Add(title);
            group.Add(title);

            ComboBox combo = new ComboBox
            {
                Location = new Point(160, y),
                Size = new Size(230, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            FillMaterialCombo(combo, kind);
            combo.SelectedIndexChanged += (s, e) => this.MaterialChanged(key);
            parent.Controls.Add(combo);
            group.Add(combo);
            this.materials[key] = combo;
            this.materialKinds[key] = kind;

            TextBox density = new TextBox
            {
                Location = new Point(396, y),
                Size = new Size(70, 20),
                TextAlign = HorizontalAlignment.Right,
            };
            density.TextChanged += this.ValueChanged;
            parent.Controls.Add(density);
            group.Add(density);
            this.fields[key + ".Density"] = density;

            Label densityUnits = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(472, y + 4),
                Text = Resources.GeometryEditorUnitDensity,
            };

            parent.Controls.Add(densityUnits);
            group.Add(densityUnits);

            // E20: список веществ правится отсюда. Кнопка стоит у КАЖДОЙ строки
            // нарочно: своё вещество заводят тогда, когда в списке его не нашли,
            // — то есть глядя ровно в этот список, а не разыскивая пункт меню.
            Button edit = new Button
            {
                Location = new Point(520, y - 1),
                Size = new Size(30, 22),
                Text = Resources.GeometryEditorMaterialsEdit,
                UseVisualStyleBackColor = true,
            };
            new ToolTip().SetToolTip(edit, Resources.GeometryEditorMaterialsEditHint);
            edit.Click += (s, e) => this.EditMaterials(key, kind);
            parent.Controls.Add(edit);
            group.Add(edit);

            Label composition = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(160, y + 24),
                MaximumSize = new Size(440, 0),
            };
            parent.Controls.Add(composition);
            group.Add(composition);
            this.compositions[key] = composition;

            y += 48;
        }

        /// <summary>
        /// Контролы строки вещества — чтобы снимать её целиком: у съёмок в поле
        /// сосуда НЕТ, и «стенка сосуда — полиэтилен» под ними была неправдой
        /// (замечание Amber 16.08.2026). Вещество при этом остаётся в модели как
        /// было: стенка нулевой толщины ничего не меняет, а стирать чужой выбор
        /// ради вида — хуже.
        /// </summary>
        readonly Dictionary<string, List<Control>> materialRows =
            new Dictionary<string, List<Control>>(StringComparer.Ordinal);

        /// <summary>Показана ли строка вещества по нашему решению — см. RowControls.Shown.</summary>
        readonly Dictionary<string, bool> materialRowShown =
            new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Показать строку вещества или снять её; строки сдвигаются.</summary>
        void ShowMaterialRow(string key, bool visible)
        {
            List<Control> group;
            if (!this.materialRows.TryGetValue(key, out group))
            {
                return;
            }

            this.materialRowShown[key] = visible;
            foreach (Control c in group)
            {
                c.Visible = visible;
            }
        }

        /// <summary>Пересобрать столбик строк веществ, как <see cref="Reflow"/>.</summary>
        void ReflowMaterials()
        {
            int y = 0;
            foreach (string key in new[] { "BeakerWall", "Source" })
            {
                List<Control> group;
                bool shown;
                if (!this.materialRows.TryGetValue(key, out group)
                    || (this.materialRowShown.TryGetValue(key, out shown) && !shown))
                {
                    continue;
                }

                // Подпись строки стоит на четыре точки ниже её верха — сдвиг
                // считается от неё, а прикладывается ко всей группе разом.
                int shift = y + 4 - group[0].Top;
                foreach (Control c in group)
                {
                    c.Top += shift;
                }

                y += 48;
            }

            this.FitPanel(this.sourceMaterialsPanel);
            this.ReflowShield();
        }

        // ------------------------------------------------------------------
        // Данные
        // ------------------------------------------------------------------

        bool loading;

        /// <summary>
        /// Поле формы и его место в модели. ОДНА таблица на чтение и на запись:
        /// раздельные списки уже подвели — «расстояние до детектора» у маринелли
        /// заводилось, писалось при сохранении, но не читалось при загрузке, и
        /// правка готового файла молча обнуляла его. Здесь такое невозможно по
        /// построению: поле без места в модели не создать (Row бросит), а место
        /// без поля видно в проверке ниже.
        /// </summary>
        sealed class FieldMap
        {
            public string Key;
            public Func<GeometryModel, double> Read;
            public Action<GeometryModel, double> Write;
        }

        static readonly List<FieldMap> Map = BuildMap();

        static List<FieldMap> BuildMap()
        {
            List<FieldMap> map = new List<FieldMap>();
            Action<string, Func<GeometryModel, double>, Action<GeometryModel, double>> add =
                (key, read, write) => map.Add(new FieldMap { Key = key, Read = read, Write = write });

            // Размеры кристалла. У бруска, которого в файле ещё не было,
            // подставляются габариты цилиндра — чтобы поля не открывались
            // пустыми и не превращались в нули при первом же сохранении.
            //
            // ⛔ (`A94`, решение Amber 04.09.2026) ПОЛЕ ЧУЖОЙ ФОРМЫ НЕ ПИШЕТСЯ В
            // МОДЕЛЬ, А СНИМАЕТСЯ. Панель невыбранной формы и так скрыта, но
            // `BuildModel` проходит по ВСЕМУ этому списку, и у бруска в модели
            // оседали размеры цилиндра: задать их было можно, а увидеть негде —
            // ни в файле, ни в отпечатке, ни в сцене. Ровно об это сломался
            // сторож `A47`.
            //
            // На ЧТЕНИЕ поле показывает то, чем эта форма обернётся в файле
            // (у бруска — производный цилиндр равной площади торца), чтобы
            // переключение формы не открывало пустых строк. Приём не новый:
            // тем же способом ниже подставляются габариты бруска у цилиндра.
            add("CrystalDiameter",
                g => g.Shape == CrystalShape.Box
                     ? GeometryWriter.EquivalentDiameter(g.CrystalBoxX, g.CrystalBoxY)
                     : g.CrystalDiameter,
                (g, v) => g.CrystalDiameter = g.Shape == CrystalShape.Box ? 0.0 : v);
            add("CrystalHeight",
                g => g.Shape == CrystalShape.Box ? g.CrystalBoxZ : g.CrystalHeight,
                (g, v) => g.CrystalHeight = g.Shape == CrystalShape.Box ? 0.0 : v);
            add("CrystalBoxX", g => g.CrystalBoxX > 0.0 ? g.CrystalBoxX : g.CrystalDiameter,
                (g, v) => g.CrystalBoxX = g.Shape == CrystalShape.Box ? v : 0.0);
            add("CrystalBoxY", g => g.CrystalBoxY > 0.0 ? g.CrystalBoxY : g.CrystalDiameter,
                (g, v) => g.CrystalBoxY = g.Shape == CrystalShape.Box ? v : 0.0);
            add("CrystalBoxZ", g => g.CrystalBoxZ > 0.0 ? g.CrystalBoxZ : g.CrystalHeight,
                (g, v) => g.CrystalBoxZ = g.Shape == CrystalShape.Box ? v : 0.0);
            add("FrontReflectorThickness", g => g.FrontReflectorThickness, (g, v) => g.FrontReflectorThickness = v);
            add("SideReflectorThickness", g => g.SideReflectorThickness, (g, v) => g.SideReflectorThickness = v);
            add("FrontGapThickness", g => g.FrontGapThickness, (g, v) => g.FrontGapThickness = v);
            add("SideGapThickness", g => g.SideGapThickness, (g, v) => g.SideGapThickness = v);
            add("FrontCladdingThickness", g => g.FrontCladdingThickness, (g, v) => g.FrontCladdingThickness = v);
            add("SideCladdingThickness", g => g.SideCladdingThickness, (g, v) => g.SideCladdingThickness = v);
            add("MountingThickness", g => g.MountingThickness, (g, v) => g.MountingThickness = v);

            // Проценты, не миллиметры: Scaled() это поле сознательно не трогает.
            add("FwhmAt662Percent", g => g.FwhmAt662Percent, (g, v) => g.FwhmAt662Percent = v);

            add("PointDistance", g => g.PointDistance, (g, v) => g.PointDistance = v);

            add("BeakerDiameter", g => g.BeakerDiameter, (g, v) => g.BeakerDiameter = v);
            add("BeakerHeight", g => g.BeakerHeight, (g, v) => g.BeakerHeight = v);
            add("BeakerSideWallThickness", g => g.BeakerSideWallThickness, (g, v) => g.BeakerSideWallThickness = v);
            add("BeakerEndWallThickness", g => g.BeakerEndWallThickness, (g, v) => g.BeakerEndWallThickness = v);
            add("SourceHeight", g => g.SourceHeight, (g, v) => g.SourceHeight = v);
            add("BeakerToDetectorDistance", g => g.BeakerToDetectorDistance,
                (g, v) => g.BeakerToDetectorDistance = v);

            add("MarinelliBeakerDiameter", g => g.MarinelliBeakerDiameter, (g, v) => g.MarinelliBeakerDiameter = v);
            add("MarinelliBeakerHeight", g => g.MarinelliBeakerHeight, (g, v) => g.MarinelliBeakerHeight = v);
            add("MarinelliHoleDiameter", g => g.MarinelliHoleDiameter, (g, v) => g.MarinelliHoleDiameter = v);
            add("MarinelliHoleHeight", g => g.MarinelliHoleHeight, (g, v) => g.MarinelliHoleHeight = v);
            add("MarinelliSideThickness", g => g.MarinelliSideThickness, (g, v) => g.MarinelliSideThickness = v);
            add("MarinelliEndWallThickness", g => g.MarinelliEndWallThickness,
                (g, v) => g.MarinelliEndWallThickness = v);
            add("MarinelliHoleSideThickness", g => g.MarinelliHoleSideThickness,
                (g, v) => g.MarinelliHoleSideThickness = v);
            add("MarinelliHoleEndWallThickness", g => g.MarinelliHoleEndWallThickness,
                (g, v) => g.MarinelliHoleEndWallThickness = v);
            add("MarinelliSourceHeight", g => g.MarinelliSourceHeight, (g, v) => g.MarinelliSourceHeight = v);
            add("MarinelliToDetectorDistance", g => g.MarinelliToDetectorDistance,
                (g, v) => g.MarinelliToDetectorDistance = v);

            // Прямоугольная кювета. Пока её размеров в модели нет, поля
            // открываются цилиндрическими — так же, как брусок кристалла
            // подставляет габариты цилиндра: иначе переключение типа источника
            // показывает пустые поля, а первое же сохранение делает из них нули.
            // Сторона по умолчанию равна диаметру, а не стороне равной площади:
            // пользователь меряет кювету линейкой, и подсказка должна быть той
            // величиной, которую он в неё впишет.
            //
            // ⛔ (`A134`, 04.09.2026) ЧУЖОМУ ВИДУ ИСТОЧНИКА ЭТИ ПОЛЯ СНИМАЮТСЯ —
            // тем же правилом `A94`, каким снимаются размеры цилиндра у бруска.
            // Подсказка на ЧТЕНИИ остаётся подсказкой; в модель она попадает,
            // только если прямоугольная кювета ВЫБРАНА.
            //
            // Что было. `BuildModel` проходит по всему списку, вид источника при
            // этом не спрашивался, и у цилиндрической (точечной, маринелли)
            // геометрии подсказка оседала в модели как настоящие размеры. Дальше
            // `GeometryWriter` пишет `SB_*` всегда, а `ResponseMatrix.ComputeStamp`
            // берёт ИМЕННО ЭТОТ ТЕКСТ — значит простое открытие-сохранение
            // геометрии двигало отпечаток и объявляло посчитанную матрицу
            // устаревшей при той же сцене. Измерено 04.09.2026 на всех 66 файлах
            // склада: отпечаток менялся у 66 из 66, ключи `SB_*` — у 65
            // (единственный, у кого не менялся, — сама кювета `Nano16Pro_box.in`).
            add("BoxSourceX", g => g.BoxSourceX > 0.0 ? g.BoxSourceX : g.BeakerDiameter,
                (g, v) => g.BoxSourceX = g.SourceType == GeometrySourceType.Box ? v : 0.0);
            add("BoxSourceY", g => g.BoxSourceY > 0.0 ? g.BoxSourceY : g.BeakerDiameter,
                (g, v) => g.BoxSourceY = g.SourceType == GeometrySourceType.Box ? v : 0.0);
            add("BoxSourceHeight", g => g.BoxSourceHeight > 0.0 ? g.BoxSourceHeight : g.SourceHeight,
                (g, v) => g.BoxSourceHeight = g.SourceType == GeometrySourceType.Box ? v : 0.0);
            add("BoxSideWallThickness",
                g => g.BoxSideWallThickness > 0.0 ? g.BoxSideWallThickness : g.BeakerSideWallThickness,
                (g, v) => g.BoxSideWallThickness = g.SourceType == GeometrySourceType.Box ? v : 0.0);
            add("BoxEndWallThickness",
                g => g.BoxEndWallThickness > 0.0 ? g.BoxEndWallThickness : g.BeakerEndWallThickness,
                (g, v) => g.BoxEndWallThickness = g.SourceType == GeometrySourceType.Box ? v : 0.0);
            add("BoxToDetectorDistance",
                g => g.BoxToDetectorDistance > 0.0 ? g.BoxToDetectorDistance : g.BeakerToDetectorDistance,
                (g, v) => g.BoxToDetectorDistance = g.SourceType == GeometrySourceType.Box ? v : 0.0);
            return map;
        }

        static FieldMap Find(string key)
        {
            foreach (FieldMap field in Map)
            {
                if (string.Equals(field.Key, key, StringComparison.Ordinal))
                {
                    return field;
                }
            }

            return null;
        }

        void LoadFromModel()
        {
            this.loading = true;
            try
            {
                GeometryModel g = this.model;
                foreach (FieldMap field in Map)
                {
                    this.Set(field.Key, field.Read(g));
                }

                // Состав пришедшей геометрии СОХРАНЯЕТСЯ (`A139`): открытие —
                // не правка, и подменять доли библиотечными нельзя.
                this.SelectMaterial("Crystal", g.Crystal, true);
                this.SelectMaterial("Reflector", g.Reflector, true);
                this.SelectMaterial("Gap", g.Gap, true);
                this.SelectMaterial("Cladding", g.Cladding, true);
                this.SelectMaterial("BeakerWall", g.BeakerWall, true);
                this.SelectMaterial("Source", g.Source, true);

                this.boxRadio.Checked = g.Shape == CrystalShape.Box;
                this.cylinderRadio.Checked = g.Shape != CrystalShape.Box;
                // Та же беда, что при постройке: у геометрии ТОЙ ЖЕ формы
                // переключатель не меняется, события нет, и строка зазора
                // осталась бы от прошлой геометрии.
                this.UpdateGapRows(g.Shape == CrystalShape.Box);
                // E21: сторона, обращённая к пробе. Ставится ПОСЛЕ формы —
                // ShapeChanged гасит выбор у цилиндра, и порядок значим.
                this.facingCombo.Enabled = g.Shape == CrystalShape.Box;
                this.facingCombo.SelectedIndex =
                    g.Facing == GeometryDetectorFacing.Side && g.Shape == CrystalShape.Box ? 1 : 0;
                // Строка списка — это ПАРА «форма плюс вид съёмки» (E27), и
                // ищется она по обоим полям сразу: у съёмки в поле форма та же,
                // что у обычного цилиндра, и по одной форме их не различить.
                this.sourceTypeCombo.SelectedIndex = IndexOfSource(g);
                this.sceneShown = g.Scene != GeometrySceneKind.None;
                // (`AMBER12`) Защита от вида съёмки не зависит: домик бывает
                // и у точки, и у маринелли, поэтому это отдельная галка, а не
                // строка списка сцен.
                if (this.shieldCheck != null)
                {
                    this.shieldCheck.Checked = g.InShield;
                }
            }
            finally
            {
                this.loading = false;
            }

            this.ShapeChanged(null, EventArgs.Empty);
            this.SourceTypeChanged(null, EventArgs.Empty);
            this.RefreshSketch();
        }

        /// <summary>
        /// Подсветить размер на обоих чертежах. Обоих, а не только видимого:
        /// вкладку переключают, и разбираться, какой чертёж сейчас на экране,
        /// здесь незачем — невидимый всё равно не рисуется.
        /// </summary>
        void SetHighlight(string key)
        {
            if (this.detectorSketch != null)
            {
                this.detectorSketch.HighlightKey = key;
            }

            if (this.sourceSketch != null)
            {
                this.sourceSketch.HighlightKey = key;
            }
        }

        /// <summary>
        /// Перерисовать чертёж по тому, что сейчас в полях. Модель собирается
        /// заново на каждое изменение: чертёж обязан показывать НЫНЕШНИЕ числа,
        /// иначе он врёт убедительнее, чем пустое место.
        /// </summary>
        void RefreshSketch()
        {
            if (this.detectorSketch == null || this.sourceSketch == null)
            {
                return;
            }

            GeometryModel g = this.BuildModel();
            this.detectorSketch.SetModel(g);
            this.sourceSketch.SetModel(g);

            // Признак правки взводится ЗДЕСЬ, а не в каждом обработчике: через
            // перерисовку чертежа проходит любое изменение геометрии — поле,
            // форма кристалла, тип источника, вещество, готовый детектор. Точка
            // одна, и добавленное завтра поле попадёт в неё само.
            //
            // Загрузка модели чертёж тоже обновляет, но правкой не является,
            // поэтому под флагом.
            if (!this.loading)
            {
                this.RaiseChanged();
            }
        }

        /// <param name="keepComposition">
        /// ⛔ ПОКАЗАТЬ вещество — не значит ЗАМЕНИТЬ его составом из библиотеки
        /// (`A139`, тем же правилом, каким `A134` решает вид источника: чтение
        /// поля остаётся подсказкой и данными не становится).
        ///
        /// `true` — открывается ЧУЖАЯ геометрия, и её состав обязан пережить
        /// открытие-сохранение дословно. Прежде состав подменялся библиотечным
        /// всякий раз, когда имя вещества в ней НАХОДИЛОСЬ, и у ввезённых из
        /// ЛСРМ файлов это двигало десять строк долей (`SC_FractionsWall`,
        /// `SC_FractionsSource` и их близнецы в блоке маринелли): файл хранит
        /// 0.04196, библиотека — 0.0419585. Текст `.in` менялся, отпечаток
        /// матрицы вместе с ним, человек ничего не правил. У 44 корпусных
        /// геометрий разницы не было: они писаны нашим писателем из той же
        /// библиотеки, и числа совпадали.
        ///
        /// `false` — вернулись из правки БИБЛИОТЕКИ, И ЭТО ВЕЩЕСТВО ею тронуто:
        /// новый состав взять неоткуда, кроме неё, он и есть смысл той правки.
        /// ⛔ Слоты, которых правка не касалась, идут сюда с `true` (`A262`):
        /// решает не «откуда пришли», а «изменилось ли ЭТО вещество».
        /// </param>
        void SelectMaterial(string key, GeometryMaterial material, bool keepComposition)
        {
            ComboBox combo = this.materials[key];
            int index = -1;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                GeometryMaterialLibrary.Entry entry = (GeometryMaterialLibrary.Entry)combo.Items[i];
                if (material != null && string.Equals(entry.Name, material.Name, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            // Вещество из файла может не значиться в библиотеке — тогда оно
            // остаётся как есть: список пустеет (выбирать его строку значило бы
            // врать), состав из файла написан рядом, а первый осознанный выбор
            // из списка вещество заменяет. Раньше здесь выбиралась первая
            // строка библиотеки, и первый же коммит подменял состав файла ею.
            //
            // Найденное же в библиотеке имя даёт СТРОКУ СПИСКА, а состав всё
            // равно остаётся пришедшим (`keepComposition`): осознанная замена —
            // это выбор из списка руками, и её ловит `MaterialChanged`.
            if (material != null && material.Fractions.Count > 0 && (index < 0 || keepComposition))
            {
                this.foreignMaterials[key] = material.Clone();
                combo.SelectedIndex = index;
            }
            else
            {
                // ⛔ НЕ НАЙДЕННОЕ ВЕЩЕСТВО ОСТАВЛЯЕТ СПИСОК ПУСТЫМ, А НЕ БЕРЁТ
                // ПЕРВУЮ СТРОКУ (`A303`). Здесь стояло
                // `index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1)`, и
                // первая строка подставлялась молча — в том числе ПУСТОМУ
                // веществу, которого в файле нет вовсе. Цена измерена
                // 10.09.2026: у 14 геометрий `tools/effmaker/models` из 14
                // блока зазора в файле нет, а круг «открыл — сохранил» писал
                // туда `M_DS_Gap.MName = Water, liquid`, `DS_RoCrystalGap` 0 →
                // 1, `DS_nCrystalGapElements` 0 → 2. Вода — не случайность и не
                // умолчание зазора: список у зазора взят у ПРОБ (`AMBER1`), а
                // первая строка проб в библиотеке — `Water, liquid`.
                //
                // ⚠ Сегодня сцена от этого не двигалась (толщина зазора у всех
                // нулевая), и потому подмена не ловилась ни отпечатком, ни
                // расчётом: вред ждал первого, кто наберёт толщину, — ровно
                // того случая, ради которого `Blank()` ставит зазору `Air, dry`
                // заранее, «чтобы человек, набрав толщину, не получил
                // молчаливый вакуум».
                //
                // Пустой список — это и есть «вещество не задано»: писатель
                // возвращает в файл тот же пустой блок, что там был, а слой с
                // толщиной, но без вещества, ловит разбор геометрии
                // (`GeometryModel.CheckLayers`, строка про зазор) и говорит о
                // нём в журнале расчёта. Довод тот же, что абзацем выше про
                // вещество, которого нет в библиотеке: выбрать за человека
                // строку списка значит соврать.
                this.foreignMaterials.Remove(key);
                combo.SelectedIndex = index;
            }

            double density = material != null && material.Density > 0.0
                ? material.Density
                : (combo.SelectedIndex >= 0
                   ? ((GeometryMaterialLibrary.Entry)combo.Items[combo.SelectedIndex]).Density : 0.0);
            this.Set(key + ".Density", density);
            this.compositions[key].Text = GeometryMaterialLibrary.Describe(
                this.MaterialOf(key, density));
            this.ReflowAfterComposition(key);
        }

        /// <summary>
        /// Пересобрать ту вкладку, где стоит строка вещества: её состав только
        /// что переписан, а подпись состава AutoSize и может стать двустрочной.
        /// </summary>
        void ReflowAfterComposition(string key)
        {
            Label composition;
            if (!this.compositions.TryGetValue(key, out composition))
            {
                return;
            }

            if (composition.Parent == this.sourceMaterialsPanel)
            {
                this.ReflowSource();
            }
            else
            {
                this.ReflowDetector();
            }
        }

        GeometryMaterial MaterialOf(string key, double density)
        {
            GeometryMaterial foreign;
            if (this.foreignMaterials.TryGetValue(key, out foreign))
            {
                GeometryMaterial copy = foreign.Clone();
                copy.Density = density > 0.0 ? density : foreign.Density;
                return copy;
            }

            ComboBox combo = this.materials[key];
            if (combo.SelectedIndex < 0)
            {
                return new GeometryMaterial();
            }

            GeometryMaterialLibrary.Entry entry = (GeometryMaterialLibrary.Entry)combo.Items[combo.SelectedIndex];
            return GeometryMaterialLibrary.Make(entry, density);
        }

        /// <summary>
        /// Открыть библиотеку веществ (E20) и, если её сохранили, пересобрать
        /// ВСЕ списки — не только тот, из которого пришли: правка библиотеки
        /// общая, и вещество могло уехать из одного вида в другой.
        ///
        /// Выбранное запоминается ВЕЩЕСТВОМ, а не номером строки: номера после
        /// правки чужие. Вернуть его берётся тот же `SelectMaterial`, что
        /// работает при открытии геометрии, — и переименованное вещество он
        /// оставит как есть, а список опустошит, вместо того чтобы молча выбрать
        /// первое попавшееся.
        ///
        /// ⛔ Состав из библиотеки берётся ТОЛЬКО у слотов, чьё вещество эта
        /// правка и правда изменила (`A262`). Прежде он брался у всех пяти —
        /// и «ОК», нажатый без единой правки, двигал клеймо сцены, объявляя
        /// посчитанную матрицу устаревшей на пустом месте: у файлов ЛСРМ доли
        /// записаны короче библиотечных (`0.04196` против `0.0419585`), а
        /// отпечатку эта разница неотличима от настоящей. Кого правка
        /// коснулась, решает <see cref="GeometryMaterialLibrary.CompositionChanged"/>
        /// по снимкам библиотеки ДО и ПОСЛЕ окна.
        /// </summary>
        void EditMaterials(string key, GeometryMaterialLibrary.MaterialKind kind)
        {
            Dictionary<string, GeometryMaterial> was =
                new Dictionary<string, GeometryMaterial>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, ComboBox> pair in this.materials)
            {
                was[pair.Key] = this.MaterialOf(pair.Key, this.Get(pair.Key + ".Density"));
            }

            List<GeometryMaterialLibrary.Entry> before = SnapshotMaterials();

            using (GeometryMaterialEditorForm form = new GeometryMaterialEditorForm(kind))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            List<GeometryMaterialLibrary.Entry> after = SnapshotMaterials();

            bool wasLoading = this.loading;
            this.loading = true;
            try
            {
                foreach (KeyValuePair<string, ComboBox> pair in this.materials)
                {
                    pair.Value.Items.Clear();
                    FillMaterialCombo(pair.Value, this.materialKinds[pair.Key]);

                    // Состав из библиотеки — только там, где правка его и
                    // изменила (`A139` живёт, `A262` вылечена): осознанная
                    // правка обязана доехать до геометрии, а нетронутое
                    // вещество обязано остаться СВОИМ, до последнего знака
                    // записи. Отличается ровно этим и от загрузки, и от
                    // прежнего поведения.
                    GeometryMaterial material = was[pair.Key];
                    bool touched = material != null
                        && GeometryMaterialLibrary.CompositionChanged(material.Name, before, after);
                    this.SelectMaterial(pair.Key, material, !touched);
                }
            }
            finally
            {
                this.loading = wasLoading;
            }

            // Состав правленого вещества мог измениться — а он ХРАНИТСЯ в
            // геометрии, долями элементов, а не именем. Значит, это правка, и
            // «Сохранить» обязана ожить: иначе библиотека уже новая, а в
            // конфигурации прибора остался прежний состав.
            this.RefreshSketch();
        }

        /// <summary>
        /// Снимок действующей библиотеки — СВОИМИ копиями, а не ссылками: после
        /// правки `GeometryMaterialStore` держит уже другой список, а прежние
        /// записи должны пережить окно, иначе сравнивать «до» будет не с чем.
        /// </summary>
        static List<GeometryMaterialLibrary.Entry> SnapshotMaterials()
        {
            List<GeometryMaterialLibrary.Entry> copy = new List<GeometryMaterialLibrary.Entry>();
            foreach (GeometryMaterialLibrary.Entry entry in GeometryMaterialStore.Entries)
            {
                if (entry != null)
                {
                    copy.Add(entry.Clone());
                }
            }

            return copy;
        }

        void MaterialChanged(string key)
        {
            if (this.loading)
            {
                return;
            }

            ComboBox combo = this.materials[key];
            if (combo.SelectedIndex >= 0)
            {
                // Выбор из списка — осознанная замена: вещество из файла с
                // этого момента забыто.
                this.foreignMaterials.Remove(key);
                GeometryMaterialLibrary.Entry entry = (GeometryMaterialLibrary.Entry)combo.Items[combo.SelectedIndex];
                this.Set(key + ".Density", entry.Density);
            }

            this.compositions[key].Text = GeometryMaterialLibrary.Describe(
                this.MaterialOf(key, this.Get(key + ".Density")));
            this.ReflowAfterComposition(key);
        }

        void ValueChanged(object sender, EventArgs e)
        {
            if (this.loading)
            {
                return;
            }

            this.MarkBadValues();
            this.UpdateEquivalent();
            foreach (string key in new[] { "Crystal", "Reflector", "Gap", "Cladding", "BeakerWall", "Source" })
            {
                this.compositions[key].Text = GeometryMaterialLibrary.Describe(
                    this.MaterialOf(key, this.Get(key + ".Density")));
            }

            // Состав — тоже AutoSize: у сложного вещества он переносится, и
            // строка под ним обрезалась бы краем панели.
            this.ReflowDetector();
            this.ReflowSource();

            this.UpdateSceneHint("");
            this.RefreshSketch();
        }

        void UpdateEquivalent()
        {
            double d = GeometryWriter.EquivalentDiameter(this.Get("CrystalBoxX"), this.Get("CrystalBoxY"));
            // Диаметр — в миллиметрах, как и поля рядом. Объём кристалла
            // остаётся в см³: так его называют в паспорте детектора, и 16.2 см³
            // читаются, а 16200 мм³ — нет.
            double volume = this.Get("CrystalBoxX") * this.Get("CrystalBoxY") * this.Get("CrystalBoxZ")
                            / (GeometryModel.MmPerCm * GeometryModel.MmPerCm * GeometryModel.MmPerCm);
            this.equivalentLabel.Text = string.Format(CultureInfo.InvariantCulture,
                Resources.GeometryEditorEquivalent, d, volume);

            // Подпись AutoSize и в русском переносится на вторую строку: панель
            // размеров от этого выше, а обвязка с веществами под ней — ниже.
            this.ReflowDetector();
        }

        /// <summary>
        /// Подставить готовый детектор. Меняется ТОЛЬКО детектор: источник
        /// остаётся тот, что выбран на своей вкладке — один и тот же кристалл
        /// меряют и в маринелли, и точечным источником.
        ///
        /// Список возвращается к приглашению: это действие, а не состояние.
        /// Оставленное имя лгало бы, как только тронут любое поле.
        /// </summary>
        void PresetChanged(object sender, EventArgs e)
        {
            if (this.loading || this.presetCombo.SelectedIndex <= 0)
            {
                return;
            }

            GeometryPresets.Preset preset =
                this.presetCombo.SelectedItem as GeometryPresets.Preset;
            if (preset == null)
            {
                return;
            }

            GeometryModel g = this.BuildModel();
            preset.Apply(g);
            this.model = g;
            this.LoadFromModel();
            this.presetCombo.SelectedIndex = 0;

            // Сцена считается ИЗ ДЕТЕКТОРА, а пресет его целиком и подменяет
            // (E32): без пересчёта в полях остались бы размеры от прежнего
            // прибора — молча, и это худший вид ошибки.
            this.RecomputeScene();
        }

        /// <summary>
        /// Строка под списком сцен: из чего сложились размеры и во что это
        /// обошлось. Молчит, пока сцену не выбрали, — рассказывать про
        /// свободный пробег у банки с водой незачем; зато после выбора идёт за
        /// полями, чтобы не остаться числами, которых в полях уже нет.
        /// </summary>
        void UpdateSceneHint(string substituted)
        {
            if (this.sceneLabel == null)
            {
                return;
            }

            if (!this.sceneShown)
            {
                this.sceneLabel.Text = "";
                return;
            }

            GeometryModel g = this.BuildModel();
            double mfp = GeometryScenes.MeanFreePathMm(g.Source, this.sceneEnergyKev);
            double volume = GeometryScenes.SampleVolumeCm3(g);
            double mass = volume * (g.Source != null ? g.Source.Density : 0.0);
            string text = string.Format(CultureInfo.InvariantCulture, Resources.GeometryEditorScene,
                                        mfp / GeometryModel.MmPerCm, this.sceneEnergyKev,
                                        volume / 1000.0, mass / 1000.0);
            if (substituted.Length > 0)
            {
                text += string.Format(CultureInfo.InvariantCulture,
                                      Resources.GeometryEditorSceneMaterial, substituted);
            }

            this.sceneLabel.Text = text;
        }

        /// <summary>Показывать ли строку про сцену — см. UpdateSceneHint.</summary>
        bool sceneShown;

        /// <summary>Идёт пересчёт сцены — сторож против рекурсии.</summary>
        bool applyingScene;

        /// <summary>
        /// Поля ДЕТЕКТОРА, из которых формула считает размеры сцены (E32).
        ///
        /// Список поимённый, а не «все поля»: пересчёт переписывает размеры
        /// ИСТОЧНИКА, и запускать его от правки самого источника значило бы
        /// стирать её на месте. Сюда входит ровно то, что читают
        /// <c>GeometryScenes.DetectorOuterDiameterMm</c> (лунка) и
        /// <c>CrystalHeightAboveSampleMm</c> (радиус на земле).
        /// </summary>
        static readonly string[] SceneDetectorFields =
        {
            "CrystalDiameter", "CrystalHeight",
            "CrystalBoxX", "CrystalBoxY", "CrystalBoxZ",
            "FrontReflectorThickness", "SideReflectorThickness",
            // (`AMBER1`) Зазор — такой же вынос детектора, как обвязка:
            // торцевой углубляет кристалл под корпусом, боковой раздувает
            // поперечник. Обе величины читает `GeometryScenes`, и пропуск их
            // здесь оставил бы сцену посчитанной по прежнему прибору.
            "FrontGapThickness", "SideGapThickness",
            "FrontCladdingThickness", "SideCladdingThickness",
        };

        /// <summary>
        /// Пересчитать размеры сцены, если выбран автосчитаемый источник (E32,
        /// задача Amber 17.08.2026).
        ///
        /// Зачем. Формула срабатывала ровно один раз — когда строку выбрали в
        /// списке источников. После этого можно было сменить пресет детектора
        /// или набрать другой кристалл, и сцена оставалась от ПРЕЖНЕГО прибора:
        /// поля заполнены, числа правдоподобны, а посчитаны по чужому детектору.
        ///
        /// ⛔ При ЗАГРУЗКЕ геометрии не зовётся никогда. Сохранённые размеры
        /// принадлежат человеку — он мог поправить их руками, — и пересчёт за
        /// ним при каждом открытии стирал бы правку. Отсюда же и сторож
        /// <c>loading</c>: <c>LoadFromModel</c> сам двигает поля и без него
        /// пересчёт вызывал бы себя без конца.
        /// </summary>
        void RecomputeScene()
        {
            if (this.loading || this.applyingScene)
            {
                return;
            }

            int index = this.sourceTypeCombo.SelectedIndex;
            if (index < 0 || index >= SourceKinds.Length)
            {
                return;
            }

            GeometrySceneKind scene = SourceKinds[index].Value;
            if (scene == GeometrySceneKind.None)
            {
                return;
            }

            // Пересчитывать по недочитанным полям нельзя: на месте опечатки
            // стоял бы ноль, и сцена вышла бы нулевого размера молча.
            if (!this.MarkBadValues())
            {
                return;
            }

            this.applyingScene = true;
            try
            {
                GeometryModel g = this.BuildModel();
                g.Scene = scene;
                string substituted = GeometryScenes.Apply(g, this.sceneEnergyKev);
                this.model = g;
                this.sceneShown = true;
                this.LoadFromModel();
                this.UpdateSceneHint(substituted);
            }
            finally
            {
                this.applyingScene = false;
            }

            this.RaiseChanged();
        }

        /// <summary>
        /// Правка поля закончена (фокус ушёл). Пересчёт сцены висит именно здесь,
        /// а не на <c>TextChanged</c>: пересчёт переписывает ВСЕ поля, и на
        /// каждом нажатии клавиши он выдёргивал бы число из-под набирающего.
        /// </summary>
        void FieldLeft(string key)
        {
            this.SetHighlight(null);
            if (Array.IndexOf(SceneDetectorFields, key) >= 0)
            {
                this.RecomputeScene();
            }
        }

        void ShapeChanged(object sender, EventArgs e)
        {
            bool box = this.boxRadio.Checked;
            this.boxSizePanel.Visible = box;
            this.cylinderSizePanel.Visible = !box;
            this.UpdateGapRows(box);
            if (box)
            {
                this.UpdateEquivalent();
            }
            else if (this.facingCombo != null && this.facingCombo.SelectedIndex != 0)
            {
                // Переключились на цилиндр — боковая постановка перестала быть
                // выразимой, и оставлять её выбранной нельзя: сцена собралась бы
                // передней, а в поле стояло бы «сбоку».
                this.facingCombo.SelectedIndex = 0;
            }

            if (this.facingCombo != null)
            {
                this.facingCombo.Enabled = box;
            }

            this.RefreshSketch();

            // Форма кристалла — тоже размер детектора (E32): в лунку брусок
            // входит ДИАГОНАЛЬЮ обвязки, цилиндр — диаметром, и переключение
            // формы меняет поперечник, из которого лунка и посчитана.
            this.RecomputeScene();
        }

        void FacingChanged(object sender, EventArgs e)
        {
            this.RefreshSketch();
            this.ValueChanged(sender, e);
        }

        void SourceTypeChanged(object sender, EventArgs e)
        {
            int index = this.sourceTypeCombo.SelectedIndex;
            if (index < 0 || index >= SourceKinds.Length)
            {
                return;
            }

            GeometrySourceType type = SourceKinds[index].Key;
            GeometrySceneKind scene = SourceKinds[index].Value;

            // Выбрали съёмку в поле (E27) — размеры считаются формулой ЗДЕСЬ,
            // при выборе, а не потом: смысл этих двух строк списка ровно в том,
            // что руками такую сцену не набирают. Не при загрузке модели:
            // сохранённые размеры принадлежат человеку, и пересчитывать их за
            // ним значило бы стирать правку при каждом открытии.
            if (scene != GeometrySceneKind.None && !this.loading && !this.applyingScene)
            {
                // Сторож против рекурсии: LoadFromModel в конце сам зовёт этот
                // обработчик, и без него пересчёт вызывал бы сам себя без конца.
                this.applyingScene = true;
                try
                {
                    GeometryModel g = this.BuildModel();
                    g.Scene = scene;
                    string substituted = GeometryScenes.Apply(g, this.sceneEnergyKev);
                    this.model = g;
                    this.sceneShown = true;
                    this.LoadFromModel();
                    this.UpdateSceneHint(substituted);
                }
                finally
                {
                    this.applyingScene = false;
                }

                return;
            }

            this.model.Scene = scene;
            this.pointPanel.Visible = type == GeometrySourceType.Point;
            this.cylinderPanel.Visible = type == GeometrySourceType.Cylinder;
            this.marinelliPanel.Visible = type == GeometrySourceType.Marinelli;
            this.boxPanel.Visible = type == GeometrySourceType.Box;

            this.ApplySceneFields(scene);

            // Вещества подтягиваются под видимый набор полей: стенка сосуда у
            // точечного источника не спрашивается вовсе, но само вещество пробы
            // нужно всегда. Какая панель показана, `ReflowSource` спрашивает у
            // того же списка — второй копии этого выбора здесь не держим.
            this.ReflowSource();

            this.UpdateSceneHint("");
            this.RefreshSketch();
        }

        /// <summary>
        /// Какие поля источника показывать и как их называть при съёмке в поле
        /// (E27, замечание Amber: «сосуда здесь нет как такового»).
        ///
        /// У прибора на земле и в лунке СОСУДА НЕТ: стенки нулевые, и строки про
        /// них — не «нули по умолчанию», а вопрос ни о чём. Снимаются и они, и
        /// строка вещества стенки, а оставшиеся размеры называются своими
        /// именами: сцена, грунт, лунка — а не «сосуд» и «колодец».
        ///
        /// Само ВЕЩЕСТВО стенки в модели остаётся нетронутым: при нулевой
        /// толщине оно ни на что не влияет, а стирать чужой выбор ради вида —
        /// хуже, чем не показывать его.
        /// </summary>
        void ApplySceneFields(GeometrySceneKind scene)
        {
            bool ground = scene == GeometrySceneKind.Ground;
            bool hole = scene == GeometrySceneKind.Borehole;
            bool vessel = !ground && !hole;

            this.ShowRow("BeakerHeight", vessel, null);
            this.ShowRow("BeakerSideWallThickness", vessel, null);
            this.ShowRow("BeakerEndWallThickness", vessel, null);
            this.ShowRow("BeakerDiameter", true,
                         ground ? Resources.GeometryEditorSceneDiameter : null);
            this.ShowRow("SourceHeight", true,
                         ground ? Resources.GeometryEditorSceneDepth : null);
            this.ShowRow("BeakerToDetectorDistance", true,
                         ground ? Resources.GeometryEditorSceneGap : null);

            this.ShowRow("MarinelliBeakerHeight", vessel, null);
            this.ShowRow("MarinelliSideThickness", vessel, null);
            this.ShowRow("MarinelliEndWallThickness", vessel, null);
            this.ShowRow("MarinelliHoleSideThickness", vessel, null);
            this.ShowRow("MarinelliHoleEndWallThickness", vessel, null);
            this.ShowRow("MarinelliBeakerDiameter", true,
                         hole ? Resources.GeometryEditorSceneDiameter : null);
            this.ShowRow("MarinelliHoleDiameter", true,
                         hole ? Resources.GeometryEditorSceneHoleDiameter : null);
            this.ShowRow("MarinelliHoleHeight", true,
                         hole ? Resources.GeometryEditorSceneHoleDepth : null);
            this.ShowRow("MarinelliSourceHeight", true,
                         hole ? Resources.GeometryEditorSceneDepth : null);
            this.ShowRow("MarinelliToDetectorDistance", true,
                         hole ? Resources.GeometryEditorSceneStandoff : null);

            this.Reflow(this.cylinderPanel);
            this.Reflow(this.marinelliPanel);

            this.ShowMaterialRow("BeakerWall", vessel);
            this.ReflowMaterials();
        }

        void Set(string key, double value)
        {
            TextBox box;
            if (this.fields.TryGetValue(key, out box))
            {
                box.Text = value.ToString("G8", CultureInfo.InvariantCulture);
            }
        }

        double Get(string key)
        {
            double value;
            TryGet(key, out value);
            return value;
        }

        bool TryGet(string key, out double value)
        {
            value = 0.0;
            TextBox box;
            if (!this.fields.TryGetValue(key, out box))
            {
                return false;
            }

            // Запятая принимается наравне с точкой: раскладка русская, и на
            // цифровом блоке там запятая.
            return double.TryParse((box.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out value);
        }

        static readonly Color BadValueColor = Color.FromArgb(0xFF, 0xE0, 0xE0);

        /// <summary>
        /// Пометить поля, значение которых не прочиталось. Молчать здесь нельзя:
        /// неразобранное число превращалось в НОЛЬ, и опечатка в толщине
        /// отражателя тихо убирала отражатель совсем — расчёт при этом честно
        /// доводился до конца и выдавал кривую не той геометрии.
        /// </summary>
        bool MarkBadValues()
        {
            // Не участвуют в модели ровно поля невыбранной формы кристалла и
            // невыбранных типов источника. Определять это по Visible нельзя:
            // видимость эффективная, и когда коммит приходит с ДРУГОЙ вкладки
            // конструктора (кнопка расчёта, общий Save), скрыта вся панель —
            // каждое поле отчитывалось «не относится», и опечатки снова молча
            // превращались в ноль.
            List<Control> inactive = new List<Control>
            {
                this.boxRadio.Checked ? this.cylinderSizePanel : this.boxSizePanel,
            };
            // ⚠ По НОМЕРУ строки списка судить нельзя, только по её типу
            // источника. С появлением съёмок в поле (E27) строк стало шесть, и
            // номера 4 и 5 — это тоже цилиндр и маринелли: сравнение с 1 и 2
            // объявляло их панели неотносящимися, и опечатка в размерах сцены
            // снова молча уезжала нулём. Найдено 17.08.2026 при E33.
            int index = this.sourceTypeCombo.SelectedIndex;
            GeometrySourceType source = index >= 0 && index < SourceKinds.Length
                ? SourceKinds[index].Key : GeometrySourceType.Point;
            if (source != GeometrySourceType.Point)
            {
                inactive.Add(this.pointPanel);
            }

            if (source != GeometrySourceType.Cylinder)
            {
                inactive.Add(this.cylinderPanel);
            }

            if (source != GeometrySourceType.Marinelli)
            {
                inactive.Add(this.marinelliPanel);
            }

            if (source != GeometrySourceType.Box)
            {
                inactive.Add(this.boxPanel);
            }

            bool ok = true;
            foreach (KeyValuePair<string, TextBox> pair in this.fields)
            {
                double value;
                bool good = TryGet(pair.Key, out value);
                pair.Value.BackColor = good ? SystemColors.Window : BadValueColor;
                if (!good && !IsUnder(pair.Value, inactive))
                {
                    ok = false;
                }
            }

            // Связки размеров подсвечиваются ТУТ ЖЕ, а не только при сохранении
            // (E33, вопрос Amber «ограничивать ввод или предъявлять список»):
            // ограничение на месте не даёт набрать промежуточное число, а один
            // только список при сохранении оставляет человека гадать, какое из
            // двадцати полей спорит. Подсветка не мешает набирать и показывает
            // поле; список при сохранении остаётся и называет причину словами.
            //
            // Возвращаемое значение при этом НЕ трогается: несогласованные
            // размеры — не «поле не читается», их ловит Validate.
            if (ok)
            {
                foreach (GeometryScenes.Issue issue in
                         GeometryScenes.Inconsistencies(this.BuildModel()))
                {
                    TextBox box;
                    if (issue.Field != null && this.fields.TryGetValue(issue.Field, out box)
                        && !IsUnder(box, inactive))
                    {
                        box.BackColor = BadValueColor;
                    }
                }
            }

            return ok;
        }

        static bool IsUnder(Control control, List<Control> containers)
        {
            for (Control c = control; c != null; c = c.Parent)
            {
                if (containers.Contains(c))
                {
                    return true;
                }
            }

            return false;
        }

        GeometryModel BuildModel()
        {
            // КОПИЯ, а не this.model. Собирать поверх своей же модели нельзя:
            // чертёж пересобирается на каждое изменение поля, а загрузка эти
            // изменения и порождает — и первое же поле переписывало только что
            // загруженную модель тем, что ЕЩЁ стоит в полях. Числа это
            // переживало (загрузка идёт дальше и дописывает их), а тип источника
            // и форма кристалла — нет: их LoadFromModel читает из модели ПОСЛЕ
            // полей, то есть уже из затёртой. Маринелли молча превращался в
            // точку на девяноста сантиметрах — все размеры на месте, сцена
            // другая.
            GeometryModel g = this.model.Clone();
            g.IsScintillator = true;
            g.Shape = this.boxRadio.Checked ? CrystalShape.Box : CrystalShape.Cylinder;
            // Боковая постановка только у бруска: у цилиндра она не
            // осесимметрична, и молча собрать сцену «как-нибудь» нельзя.
            g.Facing = this.facingCombo != null && this.facingCombo.SelectedIndex == 1
                       && g.Shape == CrystalShape.Box
                ? GeometryDetectorFacing.Side
                : GeometryDetectorFacing.Front;

            // (`AMBER12`) Измерение в защите.
            g.InShield = this.shieldCheck != null && this.shieldCheck.Checked;

            // ⛔ (`A134`) ВИД ИСТОЧНИКА РЕШАЕТСЯ ДО ПОЛЕЙ, а не после. Тем же
            // правилом, каким `A94` решает форму кристалла: карта полей
            // спрашивает у модели, СВОЯ ли ей эта строка, и чужой форме поле
            // снимает. Пока вид источника ставился ПОСЛЕ перебора, спросить
            // было не у кого — писатели видели вид, оставшийся от предыдущей
            // модели, и подсказка прямоугольной кюветы оседала в цилиндрической
            // геометрии настоящими числами.
            int kind = this.sourceTypeCombo.SelectedIndex;
            if (kind < 0 || kind >= SourceKinds.Length)
            {
                kind = 0;
            }

            g.SourceType = SourceKinds[kind].Key;
            g.Scene = SourceKinds[kind].Value;

            foreach (FieldMap field in Map)
            {
                field.Write(g, this.Get(field.Key));
            }

            g.Crystal = this.MaterialOf("Crystal", this.Get("Crystal.Density"));
            g.Reflector = this.MaterialOf("Reflector", this.Get("Reflector.Density"));
            g.Gap = this.MaterialOf("Gap", this.Get("Gap.Density"));

            // ⛔ (`AMBER1`) У ЦИЛИНДРА БОКА У ЗАЗОРА НЕТ — решение Amber
            // 07.09.2026. Ноль ставится ЗДЕСЬ, а не стиранием поля: поле у
            // цилиндра снято с окна, и число в нём — это значение, набранное
            // для бруска. Стереть его значило бы потерять набранное при
            // случайном переключении формы туда и обратно; оставить как есть —
            // тихо посчитать цилиндр с боковым зазором.
            if (g.Shape != CrystalShape.Box)
            {
                g.SideGapThickness = 0.0;
            }
            g.Cladding = this.MaterialOf("Cladding", this.Get("Cladding.Density"));
            g.BeakerWall = this.MaterialOf("BeakerWall", this.Get("BeakerWall.Density"));
            g.Source = this.MaterialOf("Source", this.Get("Source.Density"));
            return g;
        }

        /// <summary>
        /// Что проверяется перед записью. Не «всё подряд», а то, от чего расчёт
        /// молча меняет смысл: нулевой размер кристалла даёт пустую сцену и
        /// нулевую кривую, колодец шире стакана — вывернутую наизнанку пробу.
        /// </summary>
        string Validate(GeometryModel g)
        {
            if (g.Shape == CrystalShape.Box)
            {
                if (!(g.CrystalBoxX > 0.0) || !(g.CrystalBoxY > 0.0) || !(g.CrystalBoxZ > 0.0))
                {
                    return Resources.GeometryEditorErrorCrystal;
                }
            }
            else if (!(g.CrystalDiameter > 0.0) || !(g.CrystalHeight > 0.0))
            {
                return Resources.GeometryEditorErrorCrystal;
            }

            if (!(g.Crystal.Density > 0.0))
            {
                return Resources.GeometryEditorErrorDensity;
            }

            int missingZ;
            if (!g.Crystal.IsKnown(out missingZ))
            {
                return string.Format(CultureInfo.InvariantCulture, Resources.EfficiencyMakerGeometryUnknownElement, missingZ);
            }

            if (g.SourceType == GeometrySourceType.Marinelli)
            {
                if (!(g.MarinelliBeakerDiameter > g.MarinelliHoleDiameter))
                {
                    return Resources.GeometryEditorErrorMarinelliHole;
                }

                if (!(g.MarinelliSourceHeight > 0.0) || !(g.MarinelliBeakerHeight > 0.0))
                {
                    return Resources.GeometryEditorErrorSourceSize;
                }
            }
            else if (g.SourceType == GeometrySourceType.Cylinder
                     && (!(g.BeakerDiameter > 0.0) || !(g.SourceHeight > 0.0)))
            {
                return Resources.GeometryEditorErrorSourceSize;
            }
            else if (g.SourceType == GeometrySourceType.Box
                     && (!(g.BoxSourceX > 0.0) || !(g.BoxSourceY > 0.0)
                         || !(g.BoxSourceHeight > 0.0)))
            {
                return Resources.GeometryEditorErrorSourceSize;
            }

            // Связки «детектор — источник» и «сосуд — проба» (E33). Списком, а
            // не первой попавшейся: несогласованность обычно не одна, и человек,
            // поправив названную, тут же получал бы следующую.
            List<GeometryScenes.Issue> issues = GeometryScenes.Inconsistencies(g);
            if (issues.Count > 0)
            {
                StringBuilder text = new StringBuilder(Resources.GeometryEditorErrorLinked);
                foreach (GeometryScenes.Issue issue in issues)
                {
                    text.AppendLine();
                    text.Append("   • ");
                    text.Append(IssueText(issue));
                }

                return text.ToString();
            }

            return null;
        }

        /// <summary>Текст несогласованности своей строкой ресурсов (E33).</summary>
        static string IssueText(GeometryScenes.Issue issue)
        {
            string format = Resources.ResourceManager.GetString(issue.Resource);
            if (string.IsNullOrEmpty(format))
            {
                return issue.Resource;
            }

            return string.Format(CultureInfo.InvariantCulture, format, issue.Value, issue.Limit);
        }

    }
}
