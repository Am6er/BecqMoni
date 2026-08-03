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
    /// Конструктор файла геометрии `.in`.
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
    public class GeometryEditorForm : Form
    {
        readonly Dictionary<string, TextBox> fields =
            new Dictionary<string, TextBox>(StringComparer.Ordinal);

        readonly Dictionary<string, ComboBox> materials =
            new Dictionary<string, ComboBox>(StringComparer.Ordinal);

        readonly Dictionary<string, Label> compositions =
            new Dictionary<string, Label>(StringComparer.Ordinal);

        RadioButton cylinderRadio;
        RadioButton boxRadio;
        Label equivalentLabel;
        ComboBox sourceTypeCombo;
        Panel pointPanel, cylinderPanel, marinelliPanel;
        Panel cylinderSizePanel, boxSizePanel;
        TextBox pathTextBox;
        ComboBox presetCombo;
        GeometrySketch detectorSketch, sourceSketch;

        GeometryModel model;

        /// <summary>Путь сохранённого файла или null, если отменили.</summary>
        public string SavedPath { get; private set; }

        public GeometryEditorForm(GeometryModel source)
        {
            this.model = source ?? Blank();
            this.BuildLayout();
            this.LoadFromModel();
        }

        /// <summary>
        /// Заготовка для новой геометрии: сцинтиллятор в типичной обвязке.
        /// Числа — не «ноль», а правдоподобные: пустая форма заставляет
        /// заполнять двадцать полей вслепую, а от нулевой толщины отражателя
        /// расчёт молча меняет смысл.
        /// </summary>
        static GeometryModel Blank()
        {
            GeometryModel g = new GeometryModel
            {
                Name = "geometry",
                IsScintillator = true,
                SourceType = GeometrySourceType.Point,
                CrystalDiameter = 2.54,
                CrystalHeight = 2.54,
                FrontReflectorThickness = 0.1,
                SideReflectorThickness = 0.1,
                FrontCladdingThickness = 0.05,
                SideCladdingThickness = 0.05,
                MountingThickness = 0.1,
                PointDistance = 10.0,
                BeakerToDetectorDistance = 0.5,
                BeakerDiameter = 4.0,
                BeakerHeight = 2.0,
                BeakerSideWallThickness = 0.1,
                BeakerEndWallThickness = 0.1,
                SourceHeight = 2.0,
                MarinelliToDetectorDistance = 0.1,
                MarinelliBeakerDiameter = 11.4,
                MarinelliBeakerHeight = 8.9,
                MarinelliHoleDiameter = 6.1,
                MarinelliHoleHeight = 5.3,
                MarinelliSideThickness = 0.2,
                MarinelliEndWallThickness = 0.2,
                MarinelliHoleSideThickness = 0.2,
                MarinelliHoleEndWallThickness = 0.2,
                MarinelliSourceHeight = 8.5,
            };

            g.Crystal = Make("Cesium iodide");
            g.Reflector = Make("Polytetrafluoroethylene");
            g.Cladding = Make("Aluminum");
            g.BeakerWall = Make("Polyethylene");
            g.Source = Make("Water, liquid");
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
            this.Text = Resources.GeometryEditorTitle;
            this.ClientSize = new Size(1060, 650);
            this.MinimumSize = new Size(1076, 689);
            this.StartPosition = FormStartPosition.CenterParent;
            this.Icon = Resources.becqmoni;

            TabControl tabs = new TabControl
            {
                Location = new Point(12, 12),
                Size = new Size(1036, 540),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };

            TabPage detector = new TabPage(Resources.GeometryEditorTabDetector) { UseVisualStyleBackColor = true };
            TabPage source = new TabPage(Resources.GeometryEditorTabSource) { UseVisualStyleBackColor = true };
            tabs.TabPages.Add(detector);
            tabs.TabPages.Add(source);
            this.Controls.Add(tabs);

            this.BuildDetectorTab(detector);
            this.BuildSourceTab(source);

            Label pathLabel = new Label
            {
                AutoSize = true,
                Location = new Point(12, 566),
                Text = Resources.GeometryEditorFile,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };

            this.pathTextBox = new TextBox
            {
                Location = new Point(12, 584),
                Size = new Size(930, 20),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };

            Button browse = new Button
            {
                Location = new Point(948, 582),
                Size = new Size(100, 24),
                Text = Resources.GeometryEditorBrowse,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                UseVisualStyleBackColor = true,
            };
            browse.Click += this.BrowseClick;

            Button ok = new Button
            {
                Location = new Point(828, 614),
                Size = new Size(106, 26),
                Text = Resources.GeometryEditorSave,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                UseVisualStyleBackColor = true,
            };
            ok.Click += this.SaveClick;

            Button cancel = new Button
            {
                Location = new Point(942, 614),
                Size = new Size(106, 26),
                Text = Resources.GeometryEditorCancel,
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                UseVisualStyleBackColor = true,
            };

            this.Controls.Add(pathLabel);
            this.Controls.Add(this.pathTextBox);
            this.Controls.Add(browse);
            this.Controls.Add(ok);
            this.Controls.Add(cancel);
            this.CancelButton = cancel;
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

            this.boxSizePanel = new Panel { Location = new Point(0, 70), Size = new Size(620, 106), Visible = false };
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

            Panel rest = new Panel { Location = new Point(0, 182), Size = new Size(620, 152) };
            y = 0;
            this.Row(rest, ref y, "FrontReflectorThickness", Resources.GeometryEditorFrontReflector);
            this.Row(rest, ref y, "SideReflectorThickness", Resources.GeometryEditorSideReflector);
            this.Row(rest, ref y, "FrontCladdingThickness", Resources.GeometryEditorFrontCladding);
            this.Row(rest, ref y, "SideCladdingThickness", Resources.GeometryEditorSideCladding);
            this.Row(rest, ref y, "MountingThickness", Resources.GeometryEditorMounting);
            page.Controls.Add(rest);

            Panel mats = new Panel { Location = new Point(0, 340), Size = new Size(620, 150) };
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

            Panel mats = new Panel { Location = new Point(0, 336), Size = new Size(620, 100) };
            y = 0;
            this.MaterialRow(mats, ref y, "BeakerWall", Resources.GeometryEditorWallMaterial,
                             GeometryMaterialLibrary.MaterialKind.BeakerWall);
            this.MaterialRow(mats, ref y, "Source", Resources.GeometryEditorSourceMaterial,
                             GeometryMaterialLibrary.MaterialKind.Source);
            page.Controls.Add(mats);
        }

        /// <summary>Строка «подпись — поле — см».</summary>
        void Row(Control parent, ref int y, string key, string caption)
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
            parent.Controls.Add(box);
            this.fields[key] = box;

            parent.Controls.Add(new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                Location = new Point(396, y + 4),
                Text = Resources.GeometryEditorUnitCm,
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
                    g.SourceType == GeometrySourceType.Marinelli ? 2
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
            // остаётся как есть, а список показывает первую строку только для
            // того, чтобы его можно было заменить осознанно.
            combo.SelectedIndex = index >= 0 ? index : (combo.Items.Count > 0 ? 0 : -1);
            double density = material != null && material.Density > 0.0
                ? material.Density
                : (combo.SelectedIndex >= 0
                   ? ((GeometryMaterialLibrary.Entry)combo.Items[combo.SelectedIndex]).Density : 0.0);
            this.Set(key + ".Density", density);
            this.compositions[key].Text = GeometryMaterialLibrary.Describe(
                index >= 0 || material == null || material.Fractions.Count == 0
                    ? this.MaterialOf(key, density)
                    : material);
        }

        GeometryMaterial MaterialOf(string key, double density)
        {
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
            double volume = this.Get("CrystalBoxX") * this.Get("CrystalBoxY") * this.Get("CrystalBoxZ");
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
            bool ok = true;
            foreach (KeyValuePair<string, TextBox> pair in this.fields)
            {
                double value;
                bool good = TryGet(pair.Key, out value);
                pair.Value.BackColor = good ? SystemColors.Window : BadValueColor;
                // Поля скрытой вкладки источника к делу не относятся: в модель
                // пойдёт только выбранный тип.
                if (!good && pair.Value.Parent != null && pair.Value.Parent.Visible)
                {
                    ok = false;
                }
            }

            return ok;
        }

        GeometryModel BuildModel()
        {
            GeometryModel g = this.model;
            g.IsScintillator = true;
            g.Shape = this.boxRadio.Checked ? CrystalShape.Box : CrystalShape.Cylinder;
            foreach (FieldMap field in Map)
            {
                field.Write(g, this.Get(field.Key));
            }

            g.SourceType = this.sourceTypeCombo.SelectedIndex == 2 ? GeometrySourceType.Marinelli
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

            return null;
        }

        // ------------------------------------------------------------------

        void BrowseClick(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = Resources.EfficiencyMakerGeometryFilter;
                dialog.FileName = this.pathTextBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.pathTextBox.Text = dialog.FileName;
                }
            }
        }

        void SaveClick(object sender, EventArgs e)
        {
            if (!this.MarkBadValues())
            {
                MessageBox.Show(this, Resources.GeometryEditorErrorNumber, this.Text,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            GeometryModel g = this.BuildModel();
            string error = this.Validate(g);
            if (error != null)
            {
                MessageBox.Show(this, error, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string path = (this.pathTextBox.Text ?? "").Trim();
            if (path.Length == 0)
            {
                this.BrowseClick(sender, e);
                path = (this.pathTextBox.Text ?? "").Trim();
                if (path.Length == 0)
                {
                    return;
                }
            }

            try
            {
                GeometryWriter.Save(g, path);
                // Перечитать своим же загрузчиком: если записанное не читается,
                // это должно выясниться здесь, а не при расчёте кривой.
                GeometryModel back = GeometryModel.Load(path);
                if (!back.IsScintillator)
                {
                    throw new InvalidOperationException(Resources.EfficiencyMakerGeometryNotScintillator);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            this.SavedPath = path;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
