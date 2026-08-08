using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
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

        // Вещества из файла, которых нет в библиотеке. Пока пользователь не
        // выбрал замену из списка, в модель идёт ровно то, что пришло из файла:
        // подстановка первой строки библиотеки означала бы, что майлар молча
        // становится фторопластом — размеры целы, кривая чужая.
        readonly Dictionary<string, GeometryMaterial> foreignMaterials =
            new Dictionary<string, GeometryMaterial>(StringComparer.Ordinal);

        RadioButton cylinderRadio;
        RadioButton boxRadio;
        Button fwhmSuggestButton;
        double fwhmSuggestionPercent;
        Label equivalentLabel;
        ComboBox sourceTypeCombo;
        Panel pointPanel, cylinderPanel, marinelliPanel, boxPanel;
        Panel sourceMaterialsPanel;
        Panel cylinderSizePanel, boxSizePanel;
        ComboBox presetCombo;
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
        /// 662 кэВ (E11). Ноль — подсказки нет, кнопка прячется. Считает её
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
        /// </summary>
        static GeometryModel Blank()
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

            this.cylinderSizePanel = new Panel { Location = new Point(0, 70), Size = new Size(620, 56) };
            int y = 0;
            this.Row(this.cylinderSizePanel, ref y, "CrystalDiameter", Resources.GeometryEditorCrystalDiameter);
            this.Row(this.cylinderSizePanel, ref y, "CrystalHeight", Resources.GeometryEditorCrystalHeight);
            page.Controls.Add(this.cylinderSizePanel);

            // Высоты хватает на ДВЕ строки подписи о равноценном цилиндре: в
            // русском она в одну не помещается, а панель детей обрезает — и
            // строка с объёмом кристалла пропадала под соседним полем.
            this.boxSizePanel = new Panel { Location = new Point(0, 70), Size = new Size(620, 130), Visible = false };
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

            Panel rest = new Panel { Location = new Point(0, 206), Size = new Size(620, 180) };
            y = 0;
            this.Row(rest, ref y, "FrontReflectorThickness", Resources.GeometryEditorFrontReflector);
            this.Row(rest, ref y, "SideReflectorThickness", Resources.GeometryEditorSideReflector);
            this.Row(rest, ref y, "FrontCladdingThickness", Resources.GeometryEditorFrontCladding);
            this.Row(rest, ref y, "SideCladdingThickness", Resources.GeometryEditorSideCladding);
            this.Row(rest, ref y, "MountingThickness", Resources.GeometryEditorMounting);

            // Разрешение прибора (E11): без него допуск пика нулевой и поправка
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

            Panel mats = new Panel { Location = new Point(0, 392), Size = new Size(620, 150) };
            y = 0;
            this.MaterialRow(mats, ref y, "Crystal", Resources.GeometryEditorCrystalMaterial,
                             GeometryMaterialLibrary.MaterialKind.Crystal);
            this.MaterialRow(mats, ref y, "Reflector", Resources.GeometryEditorReflectorMaterial,
                             GeometryMaterialLibrary.MaterialKind.Reflector);
            this.MaterialRow(mats, ref y, "Cladding", Resources.GeometryEditorCladdingMaterial,
                             GeometryMaterialLibrary.MaterialKind.Cladding);
            page.Controls.Add(mats);
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

        /// <summary>Левая колонка с полями: чертёж занимает всё, что осталось.</summary>
        static Panel FieldColumn(TabPage page)
        {
            Panel column = new Panel { Dock = DockStyle.Left, Width = 616 };
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
            this.sourceTypeCombo.Items.Add(Resources.GeometryEditorSourcePoint);
            this.sourceTypeCombo.Items.Add(Resources.GeometryEditorSourceCylinder);
            this.sourceTypeCombo.Items.Add(Resources.GeometryEditorSourceMarinelli);
            this.sourceTypeCombo.Items.Add(Resources.GeometryEditorSourceBox);
            this.sourceTypeCombo.SelectedIndexChanged += this.SourceTypeChanged;
            page.Controls.Add(typeLabel);
            page.Controls.Add(this.sourceTypeCombo);

            this.pointPanel = new Panel { Location = new Point(0, 44), Size = new Size(620, 40) };
            int y = 0;
            this.Row(this.pointPanel, ref y, "PointDistance", Resources.GeometryEditorPointDistance);
            page.Controls.Add(this.pointPanel);

            this.cylinderPanel = new Panel { Location = new Point(0, 44), Size = new Size(620, 170), Visible = false };
            y = 0;
            this.Row(this.cylinderPanel, ref y, "BeakerDiameter", Resources.GeometryEditorBeakerDiameter);
            this.Row(this.cylinderPanel, ref y, "BeakerHeight", Resources.GeometryEditorBeakerHeight);
            this.Row(this.cylinderPanel, ref y, "BeakerSideWallThickness", Resources.GeometryEditorBeakerSideWall);
            this.Row(this.cylinderPanel, ref y, "BeakerEndWallThickness", Resources.GeometryEditorBeakerEndWall);
            this.Row(this.cylinderPanel, ref y, "SourceHeight", Resources.GeometryEditorSourceHeight);
            this.Row(this.cylinderPanel, ref y, "BeakerToDetectorDistance", Resources.GeometryEditorBeakerToDetector);
            page.Controls.Add(this.cylinderPanel);

            this.marinelliPanel = new Panel { Location = new Point(0, 44), Size = new Size(620, 282), Visible = false };
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
            this.boxPanel = new Panel { Location = new Point(0, 44), Size = new Size(620, 170), Visible = false };
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
            this.sourceMaterialsPanel = new Panel { Location = new Point(0, 336), Size = new Size(620, 100) };
            y = 0;
            this.MaterialRow(this.sourceMaterialsPanel, ref y, "BeakerWall",
                             Resources.GeometryEditorWallMaterial,
                             GeometryMaterialLibrary.MaterialKind.BeakerWall);
            this.MaterialRow(this.sourceMaterialsPanel, ref y, "Source",
                             Resources.GeometryEditorSourceMaterial,
                             GeometryMaterialLibrary.MaterialKind.Source);
            page.Controls.Add(this.sourceMaterialsPanel);
        }

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

            parent.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(14, y + 4),
                Text = caption,
            });

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
            box.Leave += (s, e) => this.SetHighlight(null);
            parent.Controls.Add(box);
            this.fields[key] = box;

            parent.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(396, y + 4),
                Text = unit ?? Resources.GeometryEditorUnitMm,
            });

            y += 28;
        }

        /// <summary>Строка «вещество — плотность — состав».</summary>
        void MaterialRow(Control parent, ref int y, string key, string caption,
                         GeometryMaterialLibrary.MaterialKind kind)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(14, y + 4),
                Text = caption,
            });

            ComboBox combo = new ComboBox
            {
                Location = new Point(160, y),
                Size = new Size(230, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            foreach (GeometryMaterialLibrary.Entry entry in GeometryMaterialLibrary.Of(kind))
            {
                combo.Items.Add(entry);
            }

            combo.SelectedIndexChanged += (s, e) => this.MaterialChanged(key);
            parent.Controls.Add(combo);
            this.materials[key] = combo;

            TextBox density = new TextBox
            {
                Location = new Point(396, y),
                Size = new Size(70, 20),
                TextAlign = HorizontalAlignment.Right,
            };
            density.TextChanged += this.ValueChanged;
            parent.Controls.Add(density);
            this.fields[key + ".Density"] = density;

            parent.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(472, y + 4),
                Text = Resources.GeometryEditorUnitDensity,
            });

            Label composition = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(160, y + 24),
                MaximumSize = new Size(440, 0),
            };
            parent.Controls.Add(composition);
            this.compositions[key] = composition;

            y += 48;
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
            add("CrystalDiameter", g => g.CrystalDiameter, (g, v) => g.CrystalDiameter = v);
            add("CrystalHeight", g => g.CrystalHeight, (g, v) => g.CrystalHeight = v);
            add("CrystalBoxX", g => g.CrystalBoxX > 0.0 ? g.CrystalBoxX : g.CrystalDiameter,
                (g, v) => g.CrystalBoxX = v);
            add("CrystalBoxY", g => g.CrystalBoxY > 0.0 ? g.CrystalBoxY : g.CrystalDiameter,
                (g, v) => g.CrystalBoxY = v);
            add("CrystalBoxZ", g => g.CrystalBoxZ > 0.0 ? g.CrystalBoxZ : g.CrystalHeight,
                (g, v) => g.CrystalBoxZ = v);
            add("FrontReflectorThickness", g => g.FrontReflectorThickness, (g, v) => g.FrontReflectorThickness = v);
            add("SideReflectorThickness", g => g.SideReflectorThickness, (g, v) => g.SideReflectorThickness = v);
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
            add("BoxSourceX", g => g.BoxSourceX > 0.0 ? g.BoxSourceX : g.BeakerDiameter,
                (g, v) => g.BoxSourceX = v);
            add("BoxSourceY", g => g.BoxSourceY > 0.0 ? g.BoxSourceY : g.BeakerDiameter,
                (g, v) => g.BoxSourceY = v);
            add("BoxSourceHeight", g => g.BoxSourceHeight > 0.0 ? g.BoxSourceHeight : g.SourceHeight,
                (g, v) => g.BoxSourceHeight = v);
            add("BoxSideWallThickness",
                g => g.BoxSideWallThickness > 0.0 ? g.BoxSideWallThickness : g.BeakerSideWallThickness,
                (g, v) => g.BoxSideWallThickness = v);
            add("BoxEndWallThickness",
                g => g.BoxEndWallThickness > 0.0 ? g.BoxEndWallThickness : g.BeakerEndWallThickness,
                (g, v) => g.BoxEndWallThickness = v);
            add("BoxToDetectorDistance",
                g => g.BoxToDetectorDistance > 0.0 ? g.BoxToDetectorDistance : g.BeakerToDetectorDistance,
                (g, v) => g.BoxToDetectorDistance = v);
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

                this.SelectMaterial("Crystal", g.Crystal);
                this.SelectMaterial("Reflector", g.Reflector);
                this.SelectMaterial("Cladding", g.Cladding);
                this.SelectMaterial("BeakerWall", g.BeakerWall);
                this.SelectMaterial("Source", g.Source);

                this.boxRadio.Checked = g.Shape == CrystalShape.Box;
                this.cylinderRadio.Checked = g.Shape != CrystalShape.Box;
                this.sourceTypeCombo.SelectedIndex =
                    g.SourceType == GeometrySourceType.Box ? 3
                    : g.SourceType == GeometrySourceType.Marinelli ? 2
                    : g.SourceType == GeometrySourceType.Cylinder ? 1 : 0;
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

        void SelectMaterial(string key, GeometryMaterial material)
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
            if (index < 0 && material != null && material.Fractions.Count > 0)
            {
                this.foreignMaterials[key] = material.Clone();
                combo.SelectedIndex = -1;
            }
            else
            {
                this.foreignMaterials.Remove(key);
                combo.SelectedIndex = index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1);
            }

            double density = material != null && material.Density > 0.0
                ? material.Density
                : (combo.SelectedIndex >= 0
                   ? ((GeometryMaterialLibrary.Entry)combo.Items[combo.SelectedIndex]).Density : 0.0);
            this.Set(key + ".Density", density);
            this.compositions[key].Text = GeometryMaterialLibrary.Describe(
                this.MaterialOf(key, density));
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
        }

        void ValueChanged(object sender, EventArgs e)
        {
            if (this.loading)
            {
                return;
            }

            this.MarkBadValues();
            this.UpdateEquivalent();
            foreach (string key in new[] { "Crystal", "Reflector", "Cladding", "BeakerWall", "Source" })
            {
                this.compositions[key].Text = GeometryMaterialLibrary.Describe(
                    this.MaterialOf(key, this.Get(key + ".Density")));
            }

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
        }

        void ShapeChanged(object sender, EventArgs e)
        {
            bool box = this.boxRadio.Checked;
            this.boxSizePanel.Visible = box;
            this.cylinderSizePanel.Visible = !box;
            if (box)
            {
                this.UpdateEquivalent();
            }

            this.RefreshSketch();
        }

        void SourceTypeChanged(object sender, EventArgs e)
        {
            int index = this.sourceTypeCombo.SelectedIndex;
            this.pointPanel.Visible = index == 0;
            this.cylinderPanel.Visible = index == 1;
            this.marinelliPanel.Visible = index == 2;
            this.boxPanel.Visible = index == 3;

            // Вещества подтягиваются под видимый набор полей: стенка сосуда у
            // точечного источника не спрашивается вовсе, но само вещество пробы
            // нужно всегда.
            Panel shown = index == 1 ? this.cylinderPanel
                        : index == 2 ? this.marinelliPanel
                        : index == 3 ? this.boxPanel
                        : this.pointPanel;
            if (this.sourceMaterialsPanel != null)
            {
                this.sourceMaterialsPanel.Top = shown.Bottom + 16;
            }

            this.RefreshSketch();
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
            int source = this.sourceTypeCombo.SelectedIndex;
            if (source != 0)
            {
                inactive.Add(this.pointPanel);
            }

            if (source != 1)
            {
                inactive.Add(this.cylinderPanel);
            }

            if (source != 2)
            {
                inactive.Add(this.marinelliPanel);
            }

            if (source != 3)
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
            foreach (FieldMap field in Map)
            {
                field.Write(g, this.Get(field.Key));
            }

            g.SourceType = this.sourceTypeCombo.SelectedIndex == 3 ? GeometrySourceType.Box
                : this.sourceTypeCombo.SelectedIndex == 2 ? GeometrySourceType.Marinelli
                : this.sourceTypeCombo.SelectedIndex == 1 ? GeometrySourceType.Cylinder
                : GeometrySourceType.Point;

            g.Crystal = this.MaterialOf("Crystal", this.Get("Crystal.Density"));
            g.Reflector = this.MaterialOf("Reflector", this.Get("Reflector.Density"));
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
                return string.Format(Resources.EfficiencyMakerGeometryUnknownElement, missingZ);
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

            return null;
        }

    }
}
