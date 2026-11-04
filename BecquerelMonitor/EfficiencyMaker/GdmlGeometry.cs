using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>Точка/вектор в миллиметрах (или углы в градусах — там, где так договорено).</summary>
    public struct GdmlPoint
    {
        public double X;
        public double Y;
        public double Z;

        public GdmlPoint(double x, double y, double z)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }
    }

    public enum GdmlSolidKind
    {
        Unknown,
        Box,
        Polycone,
        GenericPolycone,
        Tessellated,
        Subtraction,
    }

    /// <summary>
    /// Тело GDML. Геометрия хранится в СВОИХ единицах (мм), а не в единицах
    /// файла: у box/polycone они задаются атрибутом <c>lunit</c>, у
    /// tessellated — координатами <c>define/position</c>.
    /// </summary>
    public sealed class GdmlSolid
    {
        public string Name = "";
        public string Tag = "";
        public GdmlSolidKind Kind;

        // box
        public double SizeX, SizeY, SizeZ;

        // polycone: z, rmin, rmax (по одной строке на zplane)
        public readonly List<double[]> Planes = new List<double[]>();

        // genericPolycone: r, z
        public readonly List<double[]> Profile = new List<double[]>();

        // tessellated: вершины и индексы по три на треугольник
        public readonly List<GdmlPoint> Vertices = new List<GdmlPoint>();
        public readonly List<int> Triangles = new List<int>();

        public double StartPhi;
        public double DeltaPhi = 360.0;

        // subtraction
        public string FirstRef = "";
        public readonly List<string> SecondRefs = new List<string>();
        public GdmlSolid First;
        public readonly List<GdmlSolid> Second = new List<GdmlSolid>();

        public double VolumeMm3;
        public bool VolumeKnown;
        public bool BoundsKnown;
        public readonly double[] Min = { 0.0, 0.0, 0.0 };
        public readonly double[] Max = { 0.0, 0.0, 0.0 };
        public string Note = "";

        public bool Computed;

        public double VolumeCm3
        {
            get { return this.VolumeMm3 / 1000.0; }
        }
    }

    /// <summary>Вещество GDML: плотность и массовые доли элементов.</summary>
    public sealed class GdmlMaterial
    {
        public string Name = "";
        public double Density;
        public bool HasDensity;

        /// <summary>Z -> массовая доля.</summary>
        public readonly Dictionary<int, double> Fractions = new Dictionary<int, double>();

        /// <summary>Ссылки состава из файла (имя и доля).</summary>
        public readonly List<GdmlMaterialEntry> Refs = new List<GdmlMaterialEntry>();

        /// <summary>Ссылки, состав которых не найден (не подменяем их ничем).</summary>
        public readonly List<string> Missing = new List<string>();

        /// <summary>Вещества нет ни в файле, ни во встроенной таблице G4.</summary>
        public bool Unknown;

        /// <summary>Состав собран не полностью: часть ссылок не разрешилась.</summary>
        public bool Partial;

        /// <summary>Откуда состав: «файл», «G4», «неизвестно».</summary>
        public string Origin = "";

        public string CompositionText()
        {
            List<int> order = new List<int>(this.Fractions.Keys);
            order.Sort();
            StringBuilder text = new StringBuilder();
            foreach (int z in order)
            {
                if (text.Length > 0)
                {
                    text.Append(", ");
                }

                text.AppendFormat(CultureInfo.InvariantCulture, "{0} {1:F4}",
                                  GdmlG4Materials.SymbolOf(z), this.Fractions[z]);
            }

            return text.ToString();
        }
    }

    /// <summary>Положение и поворот (углы Эйлера X→Y→Z, как в Geant4).</summary>
    public sealed class GdmlTransform
    {
        readonly double[,] rotation = new double[3, 3];
        readonly double[] translation = new double[3];

        public static GdmlTransform Identity()
        {
            GdmlTransform t = new GdmlTransform();
            t.rotation[0, 0] = 1.0;
            t.rotation[1, 1] = 1.0;
            t.rotation[2, 2] = 1.0;
            return t;
        }

        public static GdmlTransform FromEuler(double rx, double ry, double rz,
                                              double tx, double ty, double tz)
        {
            double cx = Math.Cos(rx * Math.PI / 180.0), sx = Math.Sin(rx * Math.PI / 180.0);
            double cy = Math.Cos(ry * Math.PI / 180.0), sy = Math.Sin(ry * Math.PI / 180.0);
            double cz = Math.Cos(rz * Math.PI / 180.0), sz = Math.Sin(rz * Math.PI / 180.0);

            GdmlTransform t = new GdmlTransform();
            // R = Rz * Ry * Rx — порядок Geant4: сначала X, потом Y, потом Z.
            t.rotation[0, 0] = cz * cy;
            t.rotation[0, 1] = cz * sy * sx - sz * cx;
            t.rotation[0, 2] = cz * sy * cx + sz * sx;
            t.rotation[1, 0] = sz * cy;
            t.rotation[1, 1] = sz * sy * sx + cz * cx;
            t.rotation[1, 2] = sz * sy * cx - cz * sx;
            t.rotation[2, 0] = -sy;
            t.rotation[2, 1] = cy * sx;
            t.rotation[2, 2] = cy * cx;
            t.translation[0] = tx;
            t.translation[1] = ty;
            t.translation[2] = tz;
            return t;
        }

        public GdmlPoint Apply(GdmlPoint p)
        {
            return new GdmlPoint(
                this.rotation[0, 0] * p.X + this.rotation[0, 1] * p.Y + this.rotation[0, 2] * p.Z + this.translation[0],
                this.rotation[1, 0] * p.X + this.rotation[1, 1] * p.Y + this.rotation[1, 2] * p.Z + this.translation[1],
                this.rotation[2, 0] * p.X + this.rotation[2, 1] * p.Y + this.rotation[2, 2] * p.Z + this.translation[2]);
        }

        /// <summary>Наложить дочернее положение: this * child.</summary>
        public GdmlTransform Compose(GdmlTransform child)
        {
            GdmlTransform t = new GdmlTransform();
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    double sum = 0.0;
                    for (int k = 0; k < 3; k++)
                    {
                        sum += this.rotation[i, k] * child.rotation[k, j];
                    }

                    t.rotation[i, j] = sum;
                }

                t.translation[i] = this.rotation[i, 0] * child.translation[0]
                                 + this.rotation[i, 1] * child.translation[1]
                                 + this.rotation[i, 2] * child.translation[2]
                                 + this.translation[i];
            }

            return t;
        }
    }

    /// <summary>Один размещённый том (volume + physvol).</summary>
    public sealed class GdmlVolumeInstance
    {
        public string VolumeName = "";
        public string InstanceName = "";
        public string MaterialName = "";
        public string SolidName = "";
        public GdmlSolid Solid;
        public GdmlMaterial Material;
        public GdmlTransform Transform;

        public double[] Min;
        public double[] Max;
        public double VolumeCm3 = -1.0;
        public double MassG = double.NaN;
        public int Depth;
        public int Children;
        public GdmlVolumeInstance Parent;

        public bool BoundsKnown
        {
            get { return this.Min != null && this.Max != null; }
        }

        public string MaterialDescription()
        {
            if (this.Material == null || this.Material.Name.Length == 0)
            {
                return GdmlText.T("GdmlSummaryNoMaterial", "(no material)");
            }

            string composition = this.Material.CompositionText();
            string text;
            if (this.Material.Unknown)
            {
                text = this.Material.Name + " "
                     + GdmlText.T("GdmlSummaryCompositionUnknown", "(composition unknown)");
            }
            else if (this.Material.HasDensity)
            {
                text = string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryMaterial", "{0} (rho {1} g/cm3): {2}"),
                    this.Material.Name, GdmlText.N(this.Material.Density), composition);
            }
            else
            {
                text = string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryMaterialNoDensity", "{0} (density unknown): {1}"),
                    this.Material.Name, composition);
            }

            if (this.Material.Partial && this.Material.Missing.Count > 0)
            {
                text += " " + string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryPartialShort", "[incomplete: {0}]"),
                    string.Join(", ", this.Material.Missing.ToArray()));
            }

            return text;
        }
    }

    /// <summary>
    /// Читатель GDML — родного формата геометрии Geant4.
    ///
    /// Постановка `AMBER278`: тела <c>box</c>, <c>subtraction</c>,
    /// <c>polycone</c>, <c>genericPolycone</c>, <c>tessellated</c>; составы
    /// <c>G4_*</c> в файле не записаны — берутся из своей таблицы
    /// <see cref="GdmlG4Materials"/>; незнакомое тело или вещество — строкой
    /// сводки, без молчаливой подмены.
    ///
    /// Файлы GS2020 весят 11.8 МБ и несут ~90 тысяч треугольников, поэтому
    /// секции <c>define</c>, <c>materials</c>, <c>solids</c> и <c>structure</c>
    /// читаются по одной и отпускаются: целого дерева файла в памяти нет.
    ///
    /// cadmesh (https://github.com/christopherpoole/cadmesh) — только справка:
    /// это библиотека Geant4, которая заводит сетки CAD (STL/OBJ/PLY) телами
    /// Geant4. GDML она не читает, и импорта CAD здесь нет.
    /// </summary>
    public sealed class GdmlGeometry
    {
        /// <summary>Порог «крупного тома» сводки, см3 (решение Amber: крупные поимённо, мелочь сводно).</summary>
        public const double LargeThreshold = 0.1;

        public string FileName = "";
        public string FatalError;
        public readonly List<GdmlVolumeInstance> Volumes = new List<GdmlVolumeInstance>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Materials = new List<string>();

        public int UnknownSolids;
        public int UnknownMaterials;
        public int MaterialCount;

        /// <summary>Габарит всех томов без мира, мм.</summary>
        public double[] FrameMin;
        public double[] FrameMax;

        /// <summary>Ось прибора: 'x', 'y' или 'z'.</summary>
        public char Axis = 'z';

        /// <summary>Координата поперечного сечения (центр кристалла), мм.</summary>
        public double EndPlane;

        /// <summary>Имя тома кристалла, если он узнан.</summary>
        public string CrystalName = "";

        readonly Dictionary<string, GdmlPoint> positions = new Dictionary<string, GdmlPoint>(StringComparer.Ordinal);
        readonly Dictionary<string, GdmlPoint> rotations = new Dictionary<string, GdmlPoint>(StringComparer.Ordinal);
        readonly Dictionary<string, double> constants = new Dictionary<string, double>(StringComparer.Ordinal);
        readonly Dictionary<string, GdmlMaterial> fileMaterials = new Dictionary<string, GdmlMaterial>(StringComparer.Ordinal);
        readonly Dictionary<string, GdmlMaterial> resolvedMaterials = new Dictionary<string, GdmlMaterial>(StringComparer.Ordinal);
        readonly Dictionary<string, GdmlSolid> solids = new Dictionary<string, GdmlSolid>(StringComparer.Ordinal);
        readonly Dictionary<string, GdmlVolumeDef> volumeDefs = new Dictionary<string, GdmlVolumeDef>(StringComparer.Ordinal);
        readonly List<GdmlVolumeDef> volumeOrder = new List<GdmlVolumeDef>();

        double lengthUnit = 1.0;   // мм
        double angleUnit = 1.0;    // градусы

        sealed class GdmlVolumeDef
        {
            public string Name = "";
            public string MaterialRef = "";
            public string SolidRef = "";
            public readonly List<GdmlChild> Children = new List<GdmlChild>();
        }

        sealed class GdmlChild
        {
            public string Name = "";
            public string VolumeRef = "";
            public GdmlTransform Placement;
            public GdmlVolumeDef Def;
        }

        // ------------------------------------------------------------------
        // Чтение
        // ------------------------------------------------------------------

        public static GdmlGeometry Read(string path)
        {
            GdmlGeometry geometry = new GdmlGeometry();
            geometry.FileName = path;

            try
            {
                XmlReaderSettings settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreWhitespace = true,
                    CloseInput = true,
                };

                using (XmlReader reader = XmlReader.Create(path, settings))
                {
                    reader.MoveToContent();
                    if (reader.NodeType != XmlNodeType.Element
                        || !string.Equals(reader.LocalName, "gdml", StringComparison.Ordinal))
                    {
                        geometry.FatalError = string.Format(CultureInfo.InvariantCulture,
                            "root element is <{0}>, not <gdml>",
                            reader.NodeType == XmlNodeType.Element ? reader.LocalName : reader.NodeType.ToString());
                        return geometry;
                    }

                    geometry.ReadRoot(reader);
                    reader.Read();
                    while (!reader.EOF)
                    {
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            switch (reader.LocalName)
                            {
                                case "define":
                                    geometry.ReadDefine(LoadSection(reader));
                                    break;
                                case "materials":
                                    geometry.ReadMaterials(LoadSection(reader));
                                    break;
                                case "solids":
                                    geometry.ReadSolids(LoadSection(reader));
                                    break;
                                case "structure":
                                    geometry.ReadStructure(LoadSection(reader));
                                    break;
                                default:
                                    reader.Skip();
                                    break;
                            }
                        }
                        else
                        {
                            reader.Read();
                        }
                    }
                }
            }
            catch (XmlException ex)
            {
                geometry.FatalError = ex.Message;
                return geometry;
            }

            if (geometry.FatalError == null)
            {
                geometry.ResolveMaterials();
                geometry.ComputeAllSolids();
                geometry.BuildInstances();
                geometry.Measure();
            }

            return geometry;
        }

        static XElement LoadSection(XmlReader reader)
        {
            using (XmlReader sub = reader.ReadSubtree())
            {
                XElement element = XElement.Load(sub, LoadOptions.None);
                reader.Skip();
                return element;
            }
        }

        void ReadRoot(XmlReader reader)
        {
            string lunit = reader.GetAttribute("lunit");
            if (!string.IsNullOrEmpty(lunit))
            {
                this.lengthUnit = LengthScale(lunit);
            }

            string aunit = reader.GetAttribute("aunit");
            if (!string.IsNullOrEmpty(aunit))
            {
                this.angleUnit = AngleScale(aunit);
            }
        }

        void ReadDefine(XElement define)
        {
            foreach (XElement element in define.Elements())
            {
                string tag = element.Name.LocalName;
                if (tag == "position")
                {
                    string name = Attr(element, "name");
                    double x, y, z;
                    if (this.TryVector(element, out x, out y, out z))
                    {
                        this.positions[name] = new GdmlPoint(x, y, z);
                    }
                    else
                    {
                        this.Warnings.Add("define/position " + name + ": coordinates are not numbers");
                    }
                }
                else if (tag == "rotation")
                {
                    string name = Attr(element, "name");
                    double x, y, z;
                    if (this.TryAngles(element, out x, out y, out z))
                    {
                        this.rotations[name] = new GdmlPoint(x, y, z);
                    }
                    else
                    {
                        this.Warnings.Add("define/rotation " + name + ": angles are not numbers");
                    }
                }
                else if (tag == "constant" || tag == "variable" || tag == "quantity")
                {
                    string name = Attr(element, "name");
                    double value;
                    if (this.TryNumber(Attr(element, "value"), out value))
                    {
                        this.constants[name] = value;
                    }
                }
                else
                {
                    this.Warnings.Add("define: unsupported <" + tag + ">");
                }
            }
        }

        void ReadMaterials(XElement materials)
        {
            foreach (XElement element in materials.Elements())
            {
                if (!string.Equals(element.Name.LocalName, "material", StringComparison.Ordinal))
                {
                    this.Warnings.Add("materials: unsupported <" + element.Name.LocalName + ">");
                    continue;
                }

                GdmlMaterial material = new GdmlMaterial();
                material.Name = Attr(element, "name");
                material.Origin = "file";

                XElement density = element.Element("D");
                if (density != null)
                {
                    double value;
                    if (this.TryNumber(Attr(density, "value"), out value))
                    {
                        material.Density = value * DensityScale(Attr(density, "unit"));
                        material.HasDensity = true;
                    }
                    else
                    {
                        this.Warnings.Add("material " + material.Name + ": density is not a number");
                    }
                }

                foreach (XElement part in element.Elements())
                {
                    string tag = part.Name.LocalName;
                    if (tag == "D")
                    {
                        continue;
                    }

                    if (tag == "fraction" || tag == "composite")
                    {
                        string reference = Attr(part, "ref");
                        double value;
                        if (!this.TryNumber(Attr(part, "n"), out value))
                        {
                            this.Warnings.Add("material " + material.Name + ": fraction of " + reference + " is not a number");
                            continue;
                        }

                        if (tag == "composite")
                        {
                            this.Warnings.Add("material " + material.Name
                                + ": <composite> read as a mass fraction (atom fractions are not converted)");
                        }

                        GdmlMaterialEntry entry = new GdmlMaterialEntry(reference, value);
                        material.Refs.Add(entry);
                    }
                    else
                    {
                        this.Warnings.Add("material " + material.Name + ": unsupported <" + tag + ">");
                    }
                }

                this.fileMaterials[material.Name] = material;
            }
        }

        void ReadSolids(XElement section)
        {
            foreach (XElement element in section.Elements())
            {
                GdmlSolid solid = this.ReadSolid(element);
                if (!string.IsNullOrEmpty(solid.Name))
                {
                    this.solids[solid.Name] = solid;
                }
            }

            foreach (GdmlSolid solid in this.solids.Values)
            {
                if (solid.Kind != GdmlSolidKind.Subtraction)
                {
                    continue;
                }

                solid.First = this.FindSolid(solid.FirstRef, solid);
                foreach (string reference in solid.SecondRefs)
                {
                    GdmlSolid second = this.FindSolid(reference, solid);
                    if (second != null)
                    {
                        solid.Second.Add(second);
                    }
                }
            }
        }

        GdmlSolid FindSolid(string name, GdmlSolid owner)
        {
            GdmlSolid solid;
            if (this.solids.TryGetValue(name, out solid))
            {
                return solid;
            }

            this.Warnings.Add("solid " + owner.Name + ": reference " + name + " not found");
            owner.Note = "reference " + name + " not found";
            return null;
        }

        GdmlSolid ReadSolid(XElement element)
        {
            GdmlSolid solid = new GdmlSolid();
            solid.Name = Attr(element, "name");
            solid.Tag = element.Name.LocalName;
            string tag = element.Name.LocalName;

            switch (tag)
            {
                case "box":
                {
                    solid.Kind = GdmlSolidKind.Box;
                    double unit = this.SolidLengthUnit(element);
                    double x, y, z;
                    this.TryNumber(Attr(element, "x"), out x);
                    this.TryNumber(Attr(element, "y"), out y);
                    this.TryNumber(Attr(element, "z"), out z);
                    solid.SizeX = x * unit;
                    solid.SizeY = y * unit;
                    solid.SizeZ = z * unit;
                    break;
                }

                case "polycone":
                {
                    solid.Kind = GdmlSolidKind.Polycone;
                    double unit = this.SolidLengthUnit(element);
                    string aunit = Attr(element, "aunit");
                    solid.StartPhi = this.Angle(Attr(element, "startphi"), aunit);
                    string delta = Attr(element, "deltaphi");
                    solid.DeltaPhi = string.IsNullOrEmpty(delta) ? 360.0 : this.Angle(delta, aunit);
                    foreach (XElement plane in element.Elements())
                    {
                        if (!string.Equals(plane.Name.LocalName, "zplane", StringComparison.Ordinal))
                        {
                            this.Warnings.Add("polycone " + solid.Name + ": unsupported <" + plane.Name.LocalName + ">");
                            continue;
                        }

                        double z, rmin, rmax;
                        this.TryNumber(Attr(plane, "z"), out z);
                        this.TryNumber(Attr(plane, "rmin"), out rmin);
                        this.TryNumber(Attr(plane, "rmax"), out rmax);
                        solid.Planes.Add(new[] { z * unit, rmin * unit, rmax * unit });
                    }

                    if (solid.Planes.Count < 2)
                    {
                        solid.Note = "polycone has fewer than two z-planes";
                        this.Warnings.Add("polycone " + solid.Name + ": fewer than two z-planes");
                    }

                    break;
                }

                case "genericPolycone":
                {
                    solid.Kind = GdmlSolidKind.GenericPolycone;
                    double unit = this.SolidLengthUnit(element);
                    string aunit = Attr(element, "aunit");
                    solid.StartPhi = this.Angle(Attr(element, "startphi"), aunit);
                    string delta = Attr(element, "deltaphi");
                    solid.DeltaPhi = string.IsNullOrEmpty(delta) ? 360.0 : this.Angle(delta, aunit);
                    foreach (XElement point in element.Elements())
                    {
                        if (!string.Equals(point.Name.LocalName, "rzpoint", StringComparison.Ordinal))
                        {
                            this.Warnings.Add("genericPolycone " + solid.Name + ": unsupported <" + point.Name.LocalName + ">");
                            continue;
                        }

                        double r, z;
                        this.TryNumber(Attr(point, "r"), out r);
                        this.TryNumber(Attr(point, "z"), out z);
                        solid.Profile.Add(new[] { r * unit, z * unit });
                    }

                    if (solid.Profile.Count < 3)
                    {
                        solid.Note = "genericPolycone has fewer than three points";
                        this.Warnings.Add("genericPolycone " + solid.Name + ": fewer than three points");
                    }

                    break;
                }

                case "tessellated":
                    solid.Kind = GdmlSolidKind.Tessellated;
                    this.ReadTessellated(element, solid);
                    break;

                case "subtraction":
                {
                    solid.Kind = GdmlSolidKind.Subtraction;
                    foreach (XElement part in element.Elements())
                    {
                        string partTag = part.Name.LocalName;
                        if (partTag == "first")
                        {
                            solid.FirstRef = Attr(part, "ref");
                        }
                        else if (partTag == "second")
                        {
                            solid.SecondRefs.Add(Attr(part, "ref"));
                        }
                        else
                        {
                            this.Warnings.Add("subtraction " + solid.Name + ": unsupported <" + partTag + ">");
                        }
                    }

                    if (solid.FirstRef.Length == 0)
                    {
                        solid.Note = "subtraction without <first>";
                        this.Warnings.Add("subtraction " + solid.Name + ": no <first>");
                    }

                    break;
                }

                default:
                    solid.Kind = GdmlSolidKind.Unknown;
                    solid.Note = "unsupported <" + tag + ">";
                    this.Warnings.Add("solid " + solid.Name + ": unsupported body <" + tag + ">");
                    break;
            }

            return solid;
        }

        void ReadTessellated(XElement element, GdmlSolid solid)
        {
            List<GdmlPoint> local = new List<GdmlPoint>();
            Dictionary<string, int> named = new Dictionary<string, int>(StringComparer.Ordinal);
            double unit = this.SolidLengthUnit(element);

            foreach (XElement vertex in element.Elements())
            {
                if (string.Equals(vertex.Name.LocalName, "vertex", StringComparison.Ordinal))
                {
                    double x, y, z;
                    this.TryNumber(Attr(vertex, "x"), out x);
                    this.TryNumber(Attr(vertex, "y"), out y);
                    this.TryNumber(Attr(vertex, "z"), out z);
                    local.Add(new GdmlPoint(x * unit, y * unit, z * unit));
                }
            }

            int skipped = 0;
            foreach (XElement face in element.Elements())
            {
                string tag = face.Name.LocalName;
                if (tag != "triangular" && tag != "quadrangular")
                {
                    continue;
                }

                string type = Attr(face, "type");
                bool relative = string.Equals(type, "RELATIVE", StringComparison.OrdinalIgnoreCase);
                int[] indices = new int[tag == "quadrangular" ? 4 : 3];
                bool ok = true;
                for (int i = 0; i < indices.Length; i++)
                {
                    string key = "vertex" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    string value = Attr(face, key);
                    int index;
                    if (!this.ResolveVertex(value, relative, local, named, out index))
                    {
                        ok = false;
                        break;
                    }

                    indices[i] = index;
                }

                if (!ok)
                {
                    skipped++;
                    continue;
                }

                solid.Triangles.Add(indices[0]);
                solid.Triangles.Add(indices[1]);
                solid.Triangles.Add(indices[2]);
                if (indices.Length == 4)
                {
                    solid.Triangles.Add(indices[0]);
                    solid.Triangles.Add(indices[2]);
                    solid.Triangles.Add(indices[3]);
                }
            }

            solid.Vertices.AddRange(local);
            if (skipped > 0)
            {
                solid.Note = skipped.ToString(CultureInfo.InvariantCulture) + " face(s) skipped";
                this.Warnings.Add("tessellated " + solid.Name + ": " + solid.Note);
            }

            if (solid.Triangles.Count == 0)
            {
                solid.Note = "no triangles";
                this.Warnings.Add("tessellated " + solid.Name + ": no triangles");
            }
        }

        bool ResolveVertex(string value, bool relative, List<GdmlPoint> local,
                           Dictionary<string, int> named, out int index)
        {
            index = -1;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (relative)
            {
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                       && index >= 0 && index < local.Count;
            }

            if (named.TryGetValue(value, out index))
            {
                return true;
            }

            GdmlPoint point;
            if (this.positions.TryGetValue(value, out point))
            {
                index = local.Count;
                local.Add(point);
                named[value] = index;
                return true;
            }

            // Некоторые писатели кладут координаты прямо в tessellated/vertex,
            // а треугольник ссылается номером. Поддерживаем и это.
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index)
                   && index >= 0 && index < local.Count;
        }

        void ReadStructure(XElement structure)
        {
            foreach (XElement element in structure.Elements())
            {
                if (!string.Equals(element.Name.LocalName, "volume", StringComparison.Ordinal))
                {
                    this.Warnings.Add("structure: unsupported <" + element.Name.LocalName + ">");
                    continue;
                }

                GdmlVolumeDef definition = new GdmlVolumeDef();
                definition.Name = Attr(element, "name");
                foreach (XElement part in element.Elements())
                {
                    string tag = part.Name.LocalName;
                    if (tag == "materialref")
                    {
                        definition.MaterialRef = Attr(part, "ref");
                    }
                    else if (tag == "solidref")
                    {
                        definition.SolidRef = Attr(part, "ref");
                    }
                    else if (tag == "physvol")
                    {
                        GdmlChild child = this.ReadPhysvol(part);
                        if (child != null)
                        {
                            definition.Children.Add(child);
                        }
                    }
                }

                this.volumeDefs[definition.Name] = definition;
                this.volumeOrder.Add(definition);
            }

            foreach (GdmlVolumeDef definition in this.volumeDefs.Values)
            {
                foreach (GdmlChild child in definition.Children)
                {
                    GdmlVolumeDef target;
                    if (this.volumeDefs.TryGetValue(child.VolumeRef, out target))
                    {
                        child.Def = target;
                    }
                    else
                    {
                        this.Warnings.Add("volume " + definition.Name + ": child " + child.VolumeRef + " not found");
                    }
                }
            }
        }

        GdmlChild ReadPhysvol(XElement element)
        {
            GdmlChild child = new GdmlChild();
            child.Name = Attr(element, "name");
            child.Placement = GdmlTransform.Identity();

            double tx = 0.0, ty = 0.0, tz = 0.0;
            double rx = 0.0, ry = 0.0, rz = 0.0;

            foreach (XElement part in element.Elements())
            {
                string tag = part.Name.LocalName;
                if (tag == "volumeref")
                {
                    child.VolumeRef = Attr(part, "ref");
                }
                else if (tag == "positionref")
                {
                    GdmlPoint point;
                    if (this.positions.TryGetValue(Attr(part, "ref"), out point))
                    {
                        tx = point.X;
                        ty = point.Y;
                        tz = point.Z;
                    }
                    else
                    {
                        this.Warnings.Add("physvol " + child.Name + ": position " + Attr(part, "ref") + " not found");
                    }
                }
                else if (tag == "position")
                {
                    double x, y, z;
                    if (this.TryVector(part, out x, out y, out z))
                    {
                        tx = x;
                        ty = y;
                        tz = z;
                    }
                }
                else if (tag == "rotationref")
                {
                    GdmlPoint point;
                    if (this.rotations.TryGetValue(Attr(part, "ref"), out point))
                    {
                        rx = point.X;
                        ry = point.Y;
                        rz = point.Z;
                    }
                    else
                    {
                        this.Warnings.Add("physvol " + child.Name + ": rotation " + Attr(part, "ref") + " not found");
                    }
                }
                else if (tag == "rotation")
                {
                    double x, y, z;
                    if (this.TryAngles(part, out x, out y, out z))
                    {
                        rx = x;
                        ry = y;
                        rz = z;
                    }
                }
            }

            child.Placement = GdmlTransform.FromEuler(rx, ry, rz, tx, ty, tz);
            return child;
        }

        // ------------------------------------------------------------------
        // Вещества
        // ------------------------------------------------------------------

        void ResolveMaterials()
        {
            HashSet<string> visiting = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in this.fileMaterials.Keys)
            {
                this.ResolveMaterial(name, visiting);
            }

            foreach (string name in GdmlG4Materials.Table.Keys)
            {
                this.ResolveMaterial(name, visiting);
            }
        }

        GdmlMaterial ResolveMaterial(string name, HashSet<string> visiting)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            GdmlMaterial resolved;
            if (this.resolvedMaterials.TryGetValue(name, out resolved))
            {
                return resolved;
            }

            if (visiting.Contains(name))
            {
                this.Warnings.Add("material " + name + ": reference cycle");
                resolved = new GdmlMaterial { Name = name, Unknown = true, Origin = "cycle" };
                this.resolvedMaterials[name] = resolved;
                return resolved;
            }

            GdmlMaterial source;
            if (this.fileMaterials.TryGetValue(name, out source))
            {
                resolved = new GdmlMaterial { Name = name, Origin = "file" };
                resolved.Density = source.Density;
                resolved.HasDensity = source.HasDensity;

                if (source.Refs.Count == 0)
                {
                    GdmlMaterial table;
                    if (GdmlG4Materials.Table.TryGetValue(name, out table))
                    {
                        CopyComposition(table, resolved);
                        if (!resolved.HasDensity && table.HasDensity)
                        {
                            resolved.Density = table.Density;
                            resolved.HasDensity = true;
                        }

                        resolved.Origin = "file + G4";
                    }
                    else if (resolved.HasDensity)
                    {
                        resolved.Unknown = false;
                        this.Warnings.Add("material " + name + ": density only, composition is unknown");
                    }
                    else
                    {
                        resolved.Unknown = true;
                    }
                }
                else
                {
                    visiting.Add(name);
                    foreach (GdmlMaterialEntry entry in source.Refs)
                    {
                        GdmlMaterial sub = this.ResolveMaterial(entry.Reference, visiting);
                        if (sub == null || sub.Unknown)
                        {
                            resolved.Partial = true;
                            if (!resolved.Missing.Contains(entry.Reference))
                            {
                                resolved.Missing.Add(entry.Reference);
                            }

                            continue;
                        }

                        if (sub.Partial)
                        {
                            resolved.Partial = true;
                            foreach (string missing in sub.Missing)
                            {
                                if (!resolved.Missing.Contains(missing))
                                {
                                    resolved.Missing.Add(missing);
                                }
                            }
                        }

                        foreach (KeyValuePair<int, double> fraction in sub.Fractions)
                        {
                            double value;
                            resolved.Fractions.TryGetValue(fraction.Key, out value);
                            resolved.Fractions[fraction.Key] = value + entry.Fraction * fraction.Value;
                        }
                    }

                    visiting.Remove(name);
                    if (resolved.Fractions.Count == 0 && !resolved.HasDensity)
                    {
                        resolved.Unknown = true;
                    }
                }

                this.resolvedMaterials[name] = resolved;
                return resolved;
            }

            GdmlMaterial tableMaterial;
            if (GdmlG4Materials.Table.TryGetValue(name, out tableMaterial))
            {
                resolved = new GdmlMaterial { Name = name, Origin = "G4" };
                CopyComposition(tableMaterial, resolved);
                resolved.Density = tableMaterial.Density;
                resolved.HasDensity = tableMaterial.HasDensity;
                this.resolvedMaterials[name] = resolved;
                return resolved;
            }

            this.Warnings.Add("material " + name + ": not found in the file or in the built-in G4 table");
            resolved = new GdmlMaterial { Name = name, Unknown = true, Origin = "unknown" };
            this.resolvedMaterials[name] = resolved;
            return resolved;
        }

        static void CopyComposition(GdmlMaterial source, GdmlMaterial target)
        {
            foreach (KeyValuePair<int, double> fraction in source.Fractions)
            {
                target.Fractions[fraction.Key] = fraction.Value;
            }
        }

        // ------------------------------------------------------------------
        // Объёмы и габариты тел
        // ------------------------------------------------------------------

        void ComputeAllSolids()
        {
            foreach (GdmlSolid solid in this.solids.Values)
            {
                this.ComputeSolid(solid, new HashSet<GdmlSolid>());
            }
        }

        void ComputeSolid(GdmlSolid solid, HashSet<GdmlSolid> visiting)
        {
            if (solid == null || solid.Computed)
            {
                return;
            }

            if (!visiting.Add(solid))
            {
                this.Warnings.Add("solid " + solid.Name + ": reference cycle");
                solid.VolumeKnown = false;
                solid.Computed = true;
                return;
            }

            switch (solid.Kind)
            {
                case GdmlSolidKind.Box:
                    solid.VolumeMm3 = Math.Abs(solid.SizeX * solid.SizeY * solid.SizeZ);
                    solid.VolumeKnown = solid.SizeX > 0.0 && solid.SizeY > 0.0 && solid.SizeZ > 0.0;
                    SetBounds(solid, -solid.SizeX / 2.0, solid.SizeX / 2.0,
                                    -solid.SizeY / 2.0, solid.SizeY / 2.0,
                                    -solid.SizeZ / 2.0, solid.SizeZ / 2.0);
                    break;

                case GdmlSolidKind.Polycone:
                    this.ComputePolycone(solid);
                    break;

                case GdmlSolidKind.GenericPolycone:
                    this.ComputeGenericPolycone(solid);
                    break;

                case GdmlSolidKind.Tessellated:
                    this.ComputeTessellated(solid);
                    break;

                case GdmlSolidKind.Subtraction:
                    this.ComputeSubtraction(solid, visiting);
                    break;

                default:
                    solid.VolumeKnown = false;
                    break;
            }

            visiting.Remove(solid);
            solid.Computed = true;
        }

        void ComputePolycone(GdmlSolid solid)
        {
            if (solid.Planes.Count < 2)
            {
                solid.VolumeKnown = false;
                return;
            }

            double volume = 0.0;
            double rmax = 0.0;
            double zmin = double.MaxValue;
            double zmax = double.MinValue;
            for (int i = 0; i + 1 < solid.Planes.Count; i++)
            {
                double[] a = solid.Planes[i];
                double[] b = solid.Planes[i + 1];
                double dz = b[0] - a[0];
                if (dz == 0.0)
                {
                    continue;
                }

                double term = b[2] * b[2] + b[2] * a[2] + a[2] * a[2]
                            - b[1] * b[1] - b[1] * a[1] - a[1] * a[1];
                volume += Math.PI / 3.0 * dz * term;
            }

            foreach (double[] plane in solid.Planes)
            {
                rmax = Math.Max(rmax, plane[2]);
                zmin = Math.Min(zmin, plane[0]);
                zmax = Math.Max(zmax, plane[0]);
            }

            volume = Math.Abs(volume);
            if (Math.Abs(solid.DeltaPhi - 360.0) > 1e-9)
            {
                volume *= Math.Abs(solid.DeltaPhi) / 360.0;
                solid.Note = "partial revolution " + GdmlText.N(solid.DeltaPhi) + " deg";
                this.Warnings.Add("polycone " + solid.Name + ": " + solid.Note);
            }

            solid.VolumeMm3 = volume;
            solid.VolumeKnown = volume > 0.0;
            SetBounds(solid, -rmax, rmax, -rmax, rmax, zmin, zmax);
        }

        void ComputeGenericPolycone(GdmlSolid solid)
        {
            if (solid.Profile.Count < 3)
            {
                solid.VolumeKnown = false;
                return;
            }

            double moment = 0.0;
            double rmax = 0.0;
            double zmin = double.MaxValue;
            double zmax = double.MinValue;
            for (int i = 0; i < solid.Profile.Count; i++)
            {
                double[] a = solid.Profile[i];
                double[] b = solid.Profile[(i + 1) % solid.Profile.Count];
                moment += (a[0] + b[0]) * (a[0] * b[1] - b[0] * a[1]);
                rmax = Math.Max(rmax, Math.Max(a[0], b[0]));
                zmin = Math.Min(zmin, Math.Min(a[1], b[1]));
                zmax = Math.Max(zmax, Math.Max(a[1], b[1]));
            }

            double volume = Math.Abs(2.0 * Math.PI * moment / 6.0);
            if (Math.Abs(solid.DeltaPhi - 360.0) > 1e-9)
            {
                volume *= Math.Abs(solid.DeltaPhi) / 360.0;
                solid.Note = "partial revolution " + GdmlText.N(solid.DeltaPhi) + " deg";
                this.Warnings.Add("genericPolycone " + solid.Name + ": " + solid.Note);
            }

            solid.VolumeMm3 = volume;
            solid.VolumeKnown = volume > 0.0;
            SetBounds(solid, -rmax, rmax, -rmax, rmax, zmin, zmax);
        }

        void ComputeTessellated(GdmlSolid solid)
        {
            if (solid.Triangles.Count == 0 || solid.Vertices.Count == 0)
            {
                solid.VolumeKnown = false;
                return;
            }

            double six = 0.0;
            double xmin = double.MaxValue, xmax = double.MinValue;
            double ymin = double.MaxValue, ymax = double.MinValue;
            double zmin = double.MaxValue, zmax = double.MinValue;

            for (int i = 0; i + 2 < solid.Triangles.Count; i += 3)
            {
                GdmlPoint a = solid.Vertices[solid.Triangles[i]];
                GdmlPoint b = solid.Vertices[solid.Triangles[i + 1]];
                GdmlPoint c = solid.Vertices[solid.Triangles[i + 2]];
                six += a.X * (b.Y * c.Z - b.Z * c.Y)
                     + a.Y * (b.Z * c.X - b.X * c.Z)
                     + a.Z * (b.X * c.Y - b.Y * c.X);
            }

            foreach (GdmlPoint point in solid.Vertices)
            {
                xmin = Math.Min(xmin, point.X);
                xmax = Math.Max(xmax, point.X);
                ymin = Math.Min(ymin, point.Y);
                ymax = Math.Max(ymax, point.Y);
                zmin = Math.Min(zmin, point.Z);
                zmax = Math.Max(zmax, point.Z);
            }

            solid.VolumeMm3 = Math.Abs(six) / 6.0;
            solid.VolumeKnown = solid.VolumeMm3 > 0.0 && solid.Note.Length == 0;
            SetBounds(solid, xmin, xmax, ymin, ymax, zmin, zmax);
        }

        void ComputeSubtraction(GdmlSolid solid, HashSet<GdmlSolid> visiting)
        {
            if (solid.First == null)
            {
                solid.VolumeKnown = false;
                return;
            }

            this.ComputeSolid(solid.First, visiting);
            solid.VolumeMm3 = solid.First.VolumeMm3;
            solid.VolumeKnown = solid.First.VolumeKnown;
            CopyBounds(solid.First, solid);

            foreach (GdmlSolid second in solid.Second)
            {
                this.ComputeSolid(second, visiting);
                if (!solid.First.VolumeKnown || !second.VolumeKnown)
                {
                    solid.VolumeKnown = false;
                    continue;
                }

                double cut = IntersectionVolume(solid.First, second);
                if (double.IsNaN(cut))
                {
                    solid.Note = "subtraction overlap is not computed";
                    solid.VolumeKnown = false;
                    this.Warnings.Add("subtraction " + solid.Name + ": overlap with " + second.Name + " is not computed");
                }
                else
                {
                    solid.VolumeMm3 -= cut;
                }
            }

            if (solid.VolumeMm3 < 0.0)
            {
                solid.VolumeMm3 = 0.0;
            }
        }

        /// <summary>
        /// Объём пересечения тел. Для двух параллелепипедов — точно (AABB);
        /// для прочих — только когда второй габарит целиком внутри первого.
        /// Иначе NaN: объём не выдумываем.
        /// </summary>
        static double IntersectionVolume(GdmlSolid first, GdmlSolid second)
        {
            if (first.Kind == GdmlSolidKind.Box && second.Kind == GdmlSolidKind.Box)
            {
                double dx = Math.Min(first.Max[0], second.Max[0]) - Math.Max(first.Min[0], second.Min[0]);
                double dy = Math.Min(first.Max[1], second.Max[1]) - Math.Max(first.Min[1], second.Min[1]);
                double dz = Math.Min(first.Max[2], second.Max[2]) - Math.Max(first.Min[2], second.Min[2]);
                return dx > 0.0 && dy > 0.0 && dz > 0.0 ? dx * dy * dz : 0.0;
            }

            if (second.BoundsKnown && first.BoundsKnown
                && second.Min[0] >= first.Min[0] && second.Max[0] <= first.Max[0]
                && second.Min[1] >= first.Min[1] && second.Max[1] <= first.Max[1]
                && second.Min[2] >= first.Min[2] && second.Max[2] <= first.Max[2])
            {
                return second.VolumeMm3;
            }

            return double.NaN;
        }

        static void SetBounds(GdmlSolid solid,
                              double xmin, double xmax,
                              double ymin, double ymax,
                              double zmin, double zmax)
        {
            solid.Min[0] = xmin;
            solid.Min[1] = ymin;
            solid.Min[2] = zmin;
            solid.Max[0] = xmax;
            solid.Max[1] = ymax;
            solid.Max[2] = zmax;
            solid.BoundsKnown = true;
        }

        static void CopyBounds(GdmlSolid source, GdmlSolid target)
        {
            if (!source.BoundsKnown)
            {
                return;
            }

            for (int i = 0; i < 3; i++)
            {
                target.Min[i] = source.Min[i];
                target.Max[i] = source.Max[i];
            }

            target.BoundsKnown = true;
        }

        // ------------------------------------------------------------------
        // Размещение томов
        // ------------------------------------------------------------------

        void BuildInstances()
        {
            if (this.volumeDefs.Count == 0)
            {
                this.Warnings.Add("structure: no volumes");
                return;
            }

            HashSet<string> referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (GdmlVolumeDef definition in this.volumeDefs.Values)
            {
                foreach (GdmlChild child in definition.Children)
                {
                    referenced.Add(child.VolumeRef);
                }
            }

            GdmlVolumeDef world = null;
            GdmlVolumeDef candidate;
            if (this.volumeDefs.TryGetValue("World", out candidate))
            {
                world = candidate;
            }
            else
            {
                foreach (GdmlVolumeDef definition in this.volumeOrder)
                {
                    if (!referenced.Contains(definition.Name))
                    {
                        world = definition;
                        break;
                    }
                }
            }

            if (world == null)
            {
                this.Warnings.Add("structure: no world volume");
                return;
            }

            this.AddInstance(world, GdmlTransform.Identity(), world.Name, 0, null);
        }

        void AddInstance(GdmlVolumeDef definition, GdmlTransform transform, string instanceName, int depth,
                         GdmlVolumeInstance parent)
        {
            if (definition == null || depth > 64)
            {
                this.Warnings.Add("structure: too deep or missing volume at " + instanceName);
                return;
            }

            GdmlVolumeInstance instance = new GdmlVolumeInstance();
            instance.VolumeName = definition.Name;
            instance.InstanceName = instanceName;
            instance.MaterialName = definition.MaterialRef;
            instance.SolidName = definition.SolidRef;
            instance.Transform = transform;
            instance.Depth = depth;
            instance.Children = definition.Children.Count;
            instance.Parent = parent;

            GdmlSolid solid;
            if (this.solids.TryGetValue(definition.SolidRef, out solid))
            {
                instance.Solid = solid;
            }
            else
            {
                this.Warnings.Add("volume " + definition.Name + ": solid " + definition.SolidRef + " not found");
            }

            instance.Material = this.ResolveMaterial(definition.MaterialRef, new HashSet<string>(StringComparer.Ordinal));

            if (instance.Solid != null && instance.Solid.BoundsKnown)
            {
                TransformBounds(instance.Solid, transform, out instance.Min, out instance.Max);
                instance.VolumeCm3 = instance.Solid.VolumeKnown ? instance.Solid.VolumeCm3 : -1.0;
                if (instance.VolumeCm3 >= 0.0 && instance.Material != null && instance.Material.HasDensity)
                {
                    instance.MassG = instance.VolumeCm3 * instance.Material.Density;
                }
            }

            if (depth > 0)
            {
                this.Volumes.Add(instance);
            }

            foreach (GdmlChild child in definition.Children)
            {
                if (child.Def == null)
                {
                    continue;
                }

                GdmlTransform childTransform = transform.Compose(child.Placement);
                this.AddInstance(child.Def, childTransform, child.Name, depth + 1, instance);
            }
        }

        static void TransformBounds(GdmlSolid solid, GdmlTransform transform, out double[] min, out double[] max)
        {
            min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
            max = new[] { double.MinValue, double.MinValue, double.MinValue };
            for (int i = 0; i < 8; i++)
            {
                GdmlPoint corner = new GdmlPoint(
                    (i & 1) == 0 ? solid.Min[0] : solid.Max[0],
                    (i & 2) == 0 ? solid.Min[1] : solid.Max[1],
                    (i & 4) == 0 ? solid.Min[2] : solid.Max[2]);
                GdmlPoint world = transform.Apply(corner);
                min[0] = Math.Min(min[0], world.X);
                min[1] = Math.Min(min[1], world.Y);
                min[2] = Math.Min(min[2], world.Z);
                max[0] = Math.Max(max[0], world.X);
                max[1] = Math.Max(max[1], world.Y);
                max[2] = Math.Max(max[2], world.Z);
            }
        }

        // ------------------------------------------------------------------
        // Меры для сводки и чертежа
        // ------------------------------------------------------------------

        void Measure()
        {
            HashSet<string> materialNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> unknownMaterialNames = new HashSet<string>(StringComparer.Ordinal);
            this.UnknownSolids = 0;

            this.FrameMin = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
            this.FrameMax = new[] { double.MinValue, double.MinValue, double.MinValue };

            foreach (GdmlVolumeInstance volume in this.Volumes)
            {
                if (!string.IsNullOrEmpty(volume.MaterialName))
                {
                    materialNames.Add(volume.MaterialName);
                }

                if (volume.Material != null && (volume.Material.Unknown || volume.Material.Partial))
                {
                    unknownMaterialNames.Add(volume.MaterialName);
                }

                if (volume.Solid == null || volume.Solid.Kind == GdmlSolidKind.Unknown)
                {
                    this.UnknownSolids++;
                }

                if (volume.BoundsKnown)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        this.FrameMin[i] = Math.Min(this.FrameMin[i], volume.Min[i]);
                        this.FrameMax[i] = Math.Max(this.FrameMax[i], volume.Max[i]);
                    }
                }
            }

            this.MaterialCount = materialNames.Count;
            this.UnknownMaterials = unknownMaterialNames.Count;
            this.Materials.Clear();
            this.Materials.AddRange(materialNames);

            if (this.Volumes.Count == 0)
            {
                return;
            }

            this.Axis = this.DetectAxis();
            this.FindCrystal();
            this.EndPlane = this.CrystalCenter(this.Axis);
        }

        char DetectAxis()
        {
            foreach (GdmlVolumeInstance volume in this.Volumes)
            {
                if (volume.Solid != null
                    && (volume.Solid.Kind == GdmlSolidKind.Polycone
                        || volume.Solid.Kind == GdmlSolidKind.GenericPolycone))
                {
                    return 'z';
                }
            }

            double dx = this.FrameMax[0] - this.FrameMin[0];
            double dy = this.FrameMax[1] - this.FrameMin[1];
            double dz = this.FrameMax[2] - this.FrameMin[2];
            if (dx >= dy && dx >= dz)
            {
                return 'x';
            }

            return dy >= dz ? 'y' : 'z';
        }

        void FindCrystal()
        {
            this.CrystalName = "";
            foreach (GdmlVolumeInstance volume in this.Volumes)
            {
                string name = volume.VolumeName ?? "";
                if (name.IndexOf("crystal", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("кристалл", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    this.CrystalName = name;
                    return;
                }
            }
        }

        double CrystalCenter(char axis)
        {
            if (this.CrystalName.Length > 0)
            {
                foreach (GdmlVolumeInstance volume in this.Volumes)
                {
                    if (volume.BoundsKnown
                        && string.Equals(volume.VolumeName, this.CrystalName, StringComparison.Ordinal))
                    {
                        return AxisCenter(volume, axis);
                    }
                }
            }

            int index = axis == 'x' ? 0 : axis == 'y' ? 1 : 2;
            return (this.FrameMin[index] + this.FrameMax[index]) / 2.0;
        }

        public static double AxisCenter(GdmlVolumeInstance volume, char axis)
        {
            int index = axis == 'x' ? 0 : axis == 'y' ? 1 : 2;
            return (volume.Min[index] + volume.Max[index]) / 2.0;
        }

        // ------------------------------------------------------------------
        // Сводка
        // ------------------------------------------------------------------

        public string Describe()
        {
            if (!string.IsNullOrEmpty(this.FatalError))
            {
                return string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryFatal", "{0}: GDML could not be read - {1}"),
                    Path.GetFileName(this.FileName), this.FatalError);
            }

            StringBuilder text = new StringBuilder();
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlSummaryHeader",
                    "{0}: GDML geometry - {1} volume(s), {2} material(s); unsupported bodies: {3}, unknown compositions: {4}."),
                Path.GetFileName(this.FileName), this.Volumes.Count, this.MaterialCount,
                this.UnknownSolids, this.UnknownMaterials));
            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlSummaryBodies", "Bodies: {0}"), this.SolidSummary()));
            text.AppendLine();

            List<GdmlVolumeInstance> large = new List<GdmlVolumeInstance>();
            List<GdmlVolumeInstance> small = new List<GdmlVolumeInstance>();
            List<GdmlVolumeInstance> unclear = new List<GdmlVolumeInstance>();

            foreach (GdmlVolumeInstance volume in this.Volumes)
            {
                if (volume.Solid == null || volume.Solid.Kind == GdmlSolidKind.Unknown
                    || !volume.Solid.VolumeKnown)
                {
                    unclear.Add(volume);
                }
                else if (volume.VolumeCm3 >= LargeThreshold)
                {
                    large.Add(volume);
                }
                else
                {
                    small.Add(volume);
                }
            }

            large.Sort((a, b) => b.VolumeCm3.CompareTo(a.VolumeCm3));

            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlSummaryLarge", "Large volumes (from {0} cm3):"),
                GdmlText.N(LargeThreshold)));
            foreach (GdmlVolumeInstance volume in large)
            {
                text.AppendLine(this.VolumeLine(volume));
            }

            text.AppendLine();
            text.AppendLine(this.SmallLine(small));

            if (unclear.Count > 0)
            {
                text.AppendLine();
                text.AppendLine(GdmlText.T("GdmlSummaryUnclear", "Not measured:"));
                foreach (GdmlVolumeInstance volume in unclear)
                {
                    text.AppendLine(this.VolumeLine(volume));
                }
            }

            List<string> unknownMaterialNames = new List<string>();
            List<string> partialMaterialNames = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GdmlVolumeInstance volume in this.Volumes)
            {
                if (volume.Material == null || !seen.Add(volume.MaterialName))
                {
                    continue;
                }

                if (volume.Material.Unknown)
                {
                    unknownMaterialNames.Add(volume.MaterialName);
                }
                else if (volume.Material.Partial)
                {
                    partialMaterialNames.Add(volume.MaterialName);
                }
            }

            if (unknownMaterialNames.Count > 0 || partialMaterialNames.Count > 0)
            {
                text.AppendLine();
                foreach (string name in unknownMaterialNames)
                {
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        GdmlText.T("GdmlSummaryUnknownMaterial",
                            "  composition {0} is unknown: not in the file and not in the built-in G4 table"),
                        name));
                }

                foreach (string name in partialMaterialNames)
                {
                    GdmlMaterial material;
                    string missing = "";
                    if (this.resolvedMaterials.TryGetValue(name, out material))
                    {
                        missing = string.Join(", ", material.Missing.ToArray());
                    }

                    text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        GdmlText.T("GdmlSummaryPartialMaterial",
                            "  composition {0} is incomplete: unknown {1}"),
                        name, missing));
                }
            }

            if (this.Warnings.Count > 0)
            {
                text.AppendLine();
                text.AppendLine(GdmlText.T("GdmlSummaryWarnings", "Notes:"));
                foreach (string warning in this.Warnings)
                {
                    text.AppendLine("  " + warning);
                }
            }

            return text.ToString();
        }

        string VolumeLine(GdmlVolumeInstance volume)
        {
            if (volume.Solid == null || volume.Solid.Kind == GdmlSolidKind.Unknown)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryUnknownSolid", "  unsupported body: {0} - {1}"),
                    volume.VolumeName,
                    volume.Solid == null ? "?" : volume.Solid.Tag);
            }

            string dimensions = volume.BoundsKnown
                ? string.Format(CultureInfo.InvariantCulture, "{0} x {1} x {2} mm",
                    GdmlText.N(volume.Max[0] - volume.Min[0]),
                    GdmlText.N(volume.Max[1] - volume.Min[1]),
                    GdmlText.N(volume.Max[2] - volume.Min[2]))
                : "? mm";
            string volumeText = volume.VolumeCm3 >= 0.0
                ? GdmlText.N(volume.VolumeCm3) + " cm3"
                : "? cm3";
            string massText = !double.IsNaN(volume.MassG)
                ? GdmlText.N(volume.MassG) + " g"
                : "? g";

            StringBuilder extra = new StringBuilder();
            if (volume.Solid.Kind == GdmlSolidKind.Tessellated)
            {
                extra.AppendFormat(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryMesh", "; mesh {0} triangles"),
                    volume.Solid.Triangles.Count / 3);
            }
            else if (volume.Solid.Kind == GdmlSolidKind.Subtraction)
            {
                extra.AppendFormat(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummarySubtraction", "; subtraction of {0}"),
                    volume.Solid.Second.Count);
            }

            if (volume.Children > 0)
            {
                extra.AppendFormat(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryContainer", "; container of {0}"), volume.Children);
            }

            if (!volume.Solid.VolumeKnown && volume.Solid.Note.Length > 0)
            {
                extra.Append("; ").Append(volume.Solid.Note);
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{0}  [{1}]  {2}  {3}  {4}{5}",
                volume.VolumeName, volume.MaterialDescription(),
                dimensions, volumeText, massText, extra);
        }

        string SmallLine(List<GdmlVolumeInstance> small)
        {
            double volume = 0.0;
            double mass = 0.0;
            int massUnknown = 0;
            Dictionary<string, double> massByMaterial = new Dictionary<string, double>(StringComparer.Ordinal);
            Dictionary<string, int> countByMaterial = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (GdmlVolumeInstance item in small)
            {
                if (item.VolumeCm3 >= 0.0)
                {
                    volume += item.VolumeCm3;
                }

                string name = string.IsNullOrEmpty(item.MaterialName) ? "?" : item.MaterialName;
                int count;
                countByMaterial.TryGetValue(name, out count);
                countByMaterial[name] = count + 1;

                if (double.IsNaN(item.MassG))
                {
                    massUnknown++;
                }
                else
                {
                    mass += item.MassG;
                    double value;
                    massByMaterial.TryGetValue(name, out value);
                    massByMaterial[name] = value + item.MassG;
                }
            }

            StringBuilder text = new StringBuilder();
            text.Append(string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlSummarySmall", "Other: {0} volume(s), {1} cm3, {2} g"),
                small.Count, GdmlText.N(volume), GdmlText.N(mass)));

            List<string> names = new List<string>(countByMaterial.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
            {
                double value;
                bool hasMass = massByMaterial.TryGetValue(name, out value);
                text.AppendLine();
                text.Append(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummarySmallByMaterial", "  {0}: {1} volume(s), {2} g"),
                    name, countByMaterial[name], hasMass ? GdmlText.N(value) : "?"));
            }

            if (massUnknown > 0)
            {
                text.AppendLine();
                text.Append(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummarySmallUnknownMass", "  mass unknown: {0} volume(s)"),
                    massUnknown));
            }

            return text.ToString();
        }

        string SolidSummary()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (GdmlVolumeInstance volume in this.Volumes)
            {
                string tag = volume.Solid == null ? "?" : volume.Solid.Tag;
                int count;
                counts.TryGetValue(tag, out count);
                counts[tag] = count + 1;
            }

            List<string> tags = new List<string>(counts.Keys);
            tags.Sort(StringComparer.Ordinal);
            StringBuilder text = new StringBuilder();
            foreach (string tag in tags)
            {
                if (text.Length > 0)
                {
                    text.Append(", ");
                }

                text.AppendFormat(CultureInfo.InvariantCulture, "{0} {1}", tag, counts[tag]);
            }

            return text.ToString();
        }

        /// <summary>Одна строка для журнала окна.</summary>
        public string DescribeLog()
        {
            if (!string.IsNullOrEmpty(this.FatalError))
            {
                return string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryFatal", "{0}: GDML could not be read - {1}"),
                    Path.GetFileName(this.FileName), this.FatalError);
            }

            return string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlImported",
                    "GDML imported: {0}; {1} volume(s), {2} material(s), unsupported bodies: {3}, unknown compositions: {4}."),
                Path.GetFileName(this.FileName), this.Volumes.Count, this.MaterialCount,
                this.UnknownSolids, this.UnknownMaterials);
        }

        // ------------------------------------------------------------------
        // Разбор чисел и единиц
        // ------------------------------------------------------------------

        static string Attr(XElement element, string name)
        {
            XAttribute attribute = element.Attribute(name);
            return attribute == null ? "" : attribute.Value;
        }

        bool TryVector(XElement element, out double x, out double y, out double z)
        {
            string unit = Attr(element, "unit");
            double scale = string.IsNullOrEmpty(unit) ? this.lengthUnit : LengthScale(unit);
            x = y = z = 0.0;
            if (!this.TryNumber(Attr(element, "x"), out x)
                || !this.TryNumber(Attr(element, "y"), out y)
                || !this.TryNumber(Attr(element, "z"), out z))
            {
                return false;
            }

            x *= scale;
            y *= scale;
            z *= scale;
            return true;
        }

        bool TryAngles(XElement element, out double x, out double y, out double z)
        {
            string unit = Attr(element, "unit");
            double scale = string.IsNullOrEmpty(unit) ? this.angleUnit : AngleScale(unit);
            x = y = z = 0.0;
            if (!this.TryNumber(Attr(element, "x"), out x)
                || !this.TryNumber(Attr(element, "y"), out y)
                || !this.TryNumber(Attr(element, "z"), out z))
            {
                return false;
            }

            x *= scale;
            y *= scale;
            z *= scale;
            return true;
        }

        double SolidLengthUnit(XElement element)
        {
            string unit = Attr(element, "lunit");
            return string.IsNullOrEmpty(unit) ? this.lengthUnit : LengthScale(unit);
        }

        bool TryNumber(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            double constant;
            if (this.constants.TryGetValue(text.Trim(), out constant))
            {
                value = constant;
                return true;
            }

            return false;
        }

        double Angle(string text, string unit)
        {
            double value;
            if (!this.TryNumber(text, out value))
            {
                return 0.0;
            }

            return value * (string.IsNullOrEmpty(unit) ? this.angleUnit : AngleScale(unit));
        }

        static double LengthScale(string unit)
        {
            if (string.IsNullOrEmpty(unit))
            {
                return 1.0;
            }

            switch (unit.Trim())
            {
                case "mm": return 1.0;
                case "cm": return 10.0;
                case "m": return 1000.0;
                case "um": return 0.001;
                case "nm": return 1.0e-6;
                case "angstrom": return 1.0e-7;
                case "fermi": return 1.0e-12;
                case "inch": return 25.4;
                case "ft": return 304.8;
                case "mil": return 0.0254;
                default: return 1.0;
            }
        }

        static double AngleScale(string unit)
        {
            if (string.IsNullOrEmpty(unit))
            {
                return 1.0;
            }

            switch (unit.Trim())
            {
                case "deg": return 1.0;
                case "rad": return 180.0 / Math.PI;
                case "mrad": return 180.0 / Math.PI / 1000.0;
                case "urad": return 180.0 / Math.PI / 1000000.0;
                default: return 1.0;
            }
        }

        static double DensityScale(string unit)
        {
            if (string.IsNullOrEmpty(unit))
            {
                return 1.0;
            }

            switch (unit.Trim())
            {
                case "g/cm3": return 1.0;
                case "kg/m3": return 0.001;
                case "mg/cm3": return 0.001;
                case "g/m3": return 1.0e-6;
                default: return 1.0;
            }
        }
    }

    /// <summary>Строка ресурсов с запасным текстом и инвариантные числа сводки.</summary>
    public static class GdmlText
    {
        public static string T(string key, string fallback)
        {
            string text = Resources.ResourceManager.GetString(key, Resources.Culture);
            return string.IsNullOrEmpty(text) ? fallback : text;
        }

        public static string N(double value)
        {
            return value.ToString("0.#####", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Ссылка на вещество в составе (имя и массовая доля).</summary>
    public sealed class GdmlMaterialEntry
    {
        public readonly string Reference;
        public readonly double Fraction;

        public GdmlMaterialEntry(string reference, double fraction)
        {
            this.Reference = reference;
            this.Fraction = fraction;
        }
    }
}
