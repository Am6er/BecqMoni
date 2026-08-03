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
            this.ClientSize = new Size(660, 610);
            this.MinimumSize = new Size(676, 649);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.Icon = Resources.becqmoni;

            TabControl tabs = new TabControl
            {
                Location = new Point(12, 12),
                Size = new Size(636, 500),
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
                Location = new Point(12, 526),
                Text = Resources.GeometryEditorFile,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            };

            this.pathTextBox = new TextBox
            {
                Location = new Point(12, 544),
                Size = new Size(530, 20),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            };

            Button browse = new Button
            {
                Location = new Point(548, 542),
                Size = new Size(100, 24),
                Text = Resources.GeometryEditorBrowse,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                UseVisualStyleBackColor = true,
            };
            browse.Click += this.BrowseClick;

            Button ok = new Button
            {
                Location = new Point(428, 574),
                Size = new Size(106, 26),
                Text = Resources.GeometryEditorSave,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                UseVisualStyleBackColor = true,
            };
            ok.Click += this.SaveClick;

            Button cancel = new Button
            {
                Location = new Point(542, 574),
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

        void BuildDetectorTab(TabPage page)
        {
            this.cylinderRadio = new RadioButton
            {
                AutoSize = true,
                Location = new Point(14, 12),
                Text = Resources.GeometryEditorShapeCylinder,
                Checked = true,
            };

            this.boxRadio = new RadioButton
            {
                AutoSize = true,
                Location = new Point(190, 12),
                Text = Resources.GeometryEditorShapeBox,
            };

            this.cylinderRadio.CheckedChanged += this.ShapeChanged;
            page.Controls.Add(this.cylinderRadio);
            page.Controls.Add(this.boxRadio);

            this.cylinderSizePanel = new Panel { Location = new Point(0, 36), Size = new Size(620, 56) };
            int y = 0;
            this.Row(this.cylinderSizePanel, ref y, "CrystalDiameter", Resources.GeometryEditorCrystalDiameter);
            this.Row(this.cylinderSizePanel, ref y, "CrystalHeight", Resources.GeometryEditorCrystalHeight);
            page.Controls.Add(this.cylinderSizePanel);

            this.boxSizePanel = new Panel { Location = new Point(0, 36), Size = new Size(620, 106), Visible = false };
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

            Panel rest = new Panel { Location = new Point(0, 148), Size = new Size(620, 152) };
            y = 0;
            this.Row(rest, ref y, "FrontReflectorThickness", Resources.GeometryEditorFrontReflector);
            this.Row(rest, ref y, "SideReflectorThickness", Resources.GeometryEditorSideReflector);
            this.Row(rest, ref y, "FrontCladdingThickness", Resources.GeometryEditorFrontCladding);
            this.Row(rest, ref y, "SideCladdingThickness", Resources.GeometryEditorSideCladding);
            this.Row(rest, ref y, "MountingThickness", Resources.GeometryEditorMounting);
            page.Controls.Add(rest);

            Panel mats = new Panel { Location = new Point(0, 306), Size = new Size(620, 150) };
            y = 0;
            this.MaterialRow(mats, ref y, "Crystal", Resources.GeometryEditorCrystalMaterial,
                             GeometryMaterialLibrary.MaterialKind.Crystal);
            this.MaterialRow(mats, ref y, "Reflector", Resources.GeometryEditorReflectorMaterial,
                             GeometryMaterialLibrary.MaterialKind.Reflector);
            this.MaterialRow(mats, ref y, "Cladding", Resources.GeometryEditorCladdingMaterial,
                             GeometryMaterialLibrary.MaterialKind.Cladding);
            page.Controls.Add(mats);
        }

        void BuildSourceTab(TabPage page)
        {
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

        void LoadFromModel()
        {
            this.loading = true;
            try
            {
                GeometryModel g = this.model;
                this.Set("CrystalDiameter", g.CrystalDiameter);
                this.Set("CrystalHeight", g.CrystalHeight);
                this.Set("CrystalBoxX", g.CrystalBoxX > 0.0 ? g.CrystalBoxX : g.CrystalDiameter);
                this.Set("CrystalBoxY", g.CrystalBoxY > 0.0 ? g.CrystalBoxY : g.CrystalDiameter);
                this.Set("CrystalBoxZ", g.CrystalBoxZ > 0.0 ? g.CrystalBoxZ : g.CrystalHeight);
                this.Set("FrontReflectorThickness", g.FrontReflectorThickness);
                this.Set("SideReflectorThickness", g.SideReflectorThickness);
                this.Set("FrontCladdingThickness", g.FrontCladdingThickness);
                this.Set("SideCladdingThickness", g.SideCladdingThickness);
                this.Set("MountingThickness", g.MountingThickness);
                this.Set("PointDistance", g.PointDistance);
                this.Set("BeakerDiameter", g.BeakerDiameter);
                this.Set("BeakerHeight", g.BeakerHeight);
                this.Set("BeakerSideWallThickness", g.BeakerSideWallThickness);
                this.Set("BeakerEndWallThickness", g.BeakerEndWallThickness);
                this.Set("SourceHeight", g.SourceHeight);
                this.Set("BeakerToDetectorDistance", g.BeakerToDetectorDistance);
                this.Set("MarinelliBeakerDiameter", g.MarinelliBeakerDiameter);
                this.Set("MarinelliBeakerHeight", g.MarinelliBeakerHeight);
                this.Set("MarinelliHoleDiameter", g.MarinelliHoleDiameter);
                this.Set("MarinelliHoleHeight", g.MarinelliHoleHeight);
                this.Set("MarinelliSideThickness", g.MarinelliSideThickness);
                this.Set("MarinelliEndWallThickness", g.MarinelliEndWallThickness);
                this.Set("MarinelliHoleSideThickness", g.MarinelliHoleSideThickness);
                this.Set("MarinelliHoleEndWallThickness", g.MarinelliHoleEndWallThickness);
                this.Set("MarinelliSourceHeight", g.MarinelliSourceHeight);

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

            this.UpdateEquivalent();
            foreach (string key in new[] { "Crystal", "Reflector", "Cladding", "BeakerWall", "Source" })
            {
                this.compositions[key].Text = GeometryMaterialLibrary.Describe(
                    this.MaterialOf(key, this.Get(key + ".Density")));
            }
        }

        void UpdateEquivalent()
        {
            double d = GeometryWriter.EquivalentDiameter(this.Get("CrystalBoxX"), this.Get("CrystalBoxY"));
            double volume = this.Get("CrystalBoxX") * this.Get("CrystalBoxY") * this.Get("CrystalBoxZ");
            this.equivalentLabel.Text = string.Format(CultureInfo.InvariantCulture,
                Resources.GeometryEditorEquivalent, d, volume);
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
        }

        void SourceTypeChanged(object sender, EventArgs e)
        {
            int index = this.sourceTypeCombo.SelectedIndex;
            this.pointPanel.Visible = index == 0;
            this.cylinderPanel.Visible = index == 1;
            this.marinelliPanel.Visible = index == 2;
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
            TextBox box;
            if (!this.fields.TryGetValue(key, out box))
            {
                return 0.0;
            }

            // Запятая принимается наравне с точкой: раскладка русская, и на
            // цифровом блоке там запятая.
            double value;
            return double.TryParse((box.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out value) ? value : 0.0;
        }

        GeometryModel BuildModel()
        {
            GeometryModel g = this.model;
            g.IsScintillator = true;
            g.Shape = this.boxRadio.Checked ? CrystalShape.Box : CrystalShape.Cylinder;
            g.CrystalDiameter = this.Get("CrystalDiameter");
            g.CrystalHeight = this.Get("CrystalHeight");
            g.CrystalBoxX = this.Get("CrystalBoxX");
            g.CrystalBoxY = this.Get("CrystalBoxY");
            g.CrystalBoxZ = this.Get("CrystalBoxZ");
            g.FrontReflectorThickness = this.Get("FrontReflectorThickness");
            g.SideReflectorThickness = this.Get("SideReflectorThickness");
            g.FrontCladdingThickness = this.Get("FrontCladdingThickness");
            g.SideCladdingThickness = this.Get("SideCladdingThickness");
            g.MountingThickness = this.Get("MountingThickness");
            g.PointDistance = this.Get("PointDistance");
            g.BeakerDiameter = this.Get("BeakerDiameter");
            g.BeakerHeight = this.Get("BeakerHeight");
            g.BeakerSideWallThickness = this.Get("BeakerSideWallThickness");
            g.BeakerEndWallThickness = this.Get("BeakerEndWallThickness");
            g.SourceHeight = this.Get("SourceHeight");
            g.BeakerToDetectorDistance = this.Get("BeakerToDetectorDistance");
            g.MarinelliBeakerDiameter = this.Get("MarinelliBeakerDiameter");
            g.MarinelliBeakerHeight = this.Get("MarinelliBeakerHeight");
            g.MarinelliHoleDiameter = this.Get("MarinelliHoleDiameter");
            g.MarinelliHoleHeight = this.Get("MarinelliHoleHeight");
            g.MarinelliSideThickness = this.Get("MarinelliSideThickness");
            g.MarinelliEndWallThickness = this.Get("MarinelliEndWallThickness");
            g.MarinelliHoleSideThickness = this.Get("MarinelliHoleSideThickness");
            g.MarinelliHoleEndWallThickness = this.Get("MarinelliHoleEndWallThickness");
            g.MarinelliSourceHeight = this.Get("MarinelliSourceHeight");
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
