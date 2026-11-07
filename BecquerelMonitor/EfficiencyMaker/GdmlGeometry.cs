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
        Union,
    }

    /// <summary>
    /// Тело GDML. Геометрия хранится в СВОИХ единицах (мм), а не в единицах
    /// файла: у box/polycone они задаются атрибутом <c>lunit</c>, у
    /// tessellated — координатами <c>define/position</c>. Тела <c>tube</c> и <c>cone</c>
    /// хранятся как полиякон из двух плоскостей (Kind = Polycone, Tag — исходный).
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

        // subtraction и union
        public string FirstRef = "";
        public readonly List<string> SecondRefs = new List<string>();
        public GdmlSolid First;
        public readonly List<GdmlSolid> Second = new List<GdmlSolid>();

        /// <summary>Положение второго тела, как прочитано; разложено по Second при связывании.</summary>
        public GdmlTransform SecondPlacementRead;

        /// <summary>Положение каждого тела из Second в системе вычитания; идёт вровень с Second.</summary>
        public readonly List<GdmlTransform> SecondPlacements = new List<GdmlTransform>();

        /// <summary>Положение первого тела в системе вычитания; null — на месте.</summary>
        public GdmlTransform FirstPlacement;

        public double VolumeMm3;
        public bool VolumeKnown;
        public bool BoundsKnown;
        public readonly double[] Min = { 0.0, 0.0, 0.0 };
        public readonly double[] Max = { 0.0, 0.0, 0.0 };
        public string Note = "";

        /// <summary>Тело прочитано не полностью, причина — в Note; объём не считается.</summary>
        public bool Unreadable;

        /// <summary>Вычитание, в составе которого есть неподдержанное тело.</summary>
        public bool ContainsUnknown;

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

        /// <summary>Атомная масса из файла, г/моль (<c>atom</c>); 0 — в файле нет.</summary>
        public double AtomicMass;

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

    /// <summary>
    /// Положение и поворот (углы Эйлера X→Y→Z, как в Geant4). Углы — в градусах:
    /// единицы файла переводит читатель.
    /// </summary>
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

        /// <summary>
        /// Матрица как `GetRotationMatrix` (`rotateX`, `rotateY`, `rotateZ`): R = Rz·Ry·Rx.
        /// Так размещается второе тело булевой операции (`G4GDMLReadSolids.cc:193`), без
        /// обращения. Для physvol — <see cref="FromPhysvol"/>.
        /// </summary>
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

        /// <summary>
        /// Положение physvol: `G4Transform3D(GetRotationMatrix(rotation).inverse(), position)`
        /// (`G4GDMLReadStructure.cc:476`). Обращение — суть GDML: углы в файле поворачивают
        /// систему, а не тело, поэтому y = +90° уводит +z в −x, а не в +x.
        /// </summary>
        public static GdmlTransform FromPhysvol(double rx, double ry, double rz,
                                                double tx, double ty, double tz)
        {
            GdmlTransform direct = FromEuler(rx, ry, rz, tx, ty, tz);
            GdmlTransform t = new GdmlTransform();
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    t.rotation[i, j] = direct.rotation[j, i];
                }

                t.translation[i] = direct.translation[i];
            }

            return t;
        }

        /// <summary>Зеркало справа: `transform * G4Scale3D` (`G4GDMLReadStructure.cc:477`).</summary>
        public void Mirror(double sx, double sy, double sz)
        {
            for (int i = 0; i < 3; i++)
            {
                this.rotation[i, 0] *= sx;
                this.rotation[i, 1] *= sy;
                this.rotation[i, 2] *= sz;
            }
        }

        /// <summary>Оси тела идут вдоль осей системы: габарит коробки равен самой коробке.</summary>
        public bool IsAxisAligned
        {
            get
            {
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        double value = Math.Abs(this.rotation[i, j]);
                        if (value > 1e-9 && Math.Abs(value - 1.0) > 1e-9)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }
        }

        /// <summary>Куда идёт локальная ось z, без сдвига.</summary>
        public GdmlPoint ImageOfLocalZ
        {
            get { return new GdmlPoint(this.rotation[0, 2], this.rotation[1, 2], this.rotation[2, 2]); }
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

        /// <summary>Объём тела за вычетом прямых детей, см3; -1 — неизвестен.</summary>
        public double OwnVolumeCm3 = -1.0;

        public double MassG = double.NaN;

        /// <summary>Почему нет массы при известном объёме; пусто — причина не здесь.</summary>
        public string MassNote = "";

        /// <summary>Положение прочитано. Нет — Min и Max пусты, том в списке «не измерено».</summary>
        public bool PlacementKnown = true;

        /// <summary>
        /// (fixGdml2, третий заход) Что внутри тома не размещено: `&lt;replicavol&gt;`, `&lt;paramvol&gt;`, внешний файл,
        /// потерянный ребёнок; пусто — всё. Чертёж подписывает такой том «содержимое не показано».
        /// </summary>
        public string UnknownContent = "";

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
    /// <c>polycone</c>, <c>genericPolycone</c>, <c>tessellated</c>, с `AMBER312` ещё <c>tube</c>, <c>cone</c>, <c>union</c>; составы
    /// <c>G4_*</c> в файле не записаны — берутся из своей таблицы
    /// <see cref="GdmlG4Materials"/>; незнакомое тело или вещество — строкой
    /// сводки, без молчаливой подмены.
    ///
    /// `AMBER303`: каждое нечитаемое значение — строка в <see cref="Warnings"/> с
    /// именем тела или тома и «не измерено» вместо нуля или единицы. Значения по
    /// умолчанию — только те, что заданы схемой и кодом Geant4 (длина мм, угол
    /// радиан, x/y/z у position и rotation и rmin у zplane равны 0). Эталон смысла —
    /// читатель Geant4: `G4GDMLReadStructure`, `G4GDMLReadSolids`, `G4GDMLReadDefine`.
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

        /// <summary>
        /// (`AMBER301`, полоса fixGdml2 07.11.2026) Деталей поддержанного тела, у которых не прочитаны положение или габарит:
        /// на чертеже их нет. Считается той же меркой, что подпись чертежа (<see cref="IsUnplaced"/>), — сводка и чертёж
        /// говорят одним числом. Прежде обе причины чертёж сливал в одну подпись «неподдержанное тело или неизвестное
        /// положение», а сводка писала «неподдержанных тел: 0»: на `lht.gdml` 11 деталей (7 имён) с положением через sin().
        /// </summary>
        public int UnplacedVolumes;
        public int UnknownMaterials;
        public int MaterialCount;

        /// <summary>
        /// (fixGdml2, третий заход) Вещества томов, чьи копии не размещены (`replicavol` вдоль rho/phi, `paramvol`,
        /// `divisionvol`), с причиной: в файле они есть, а в сцене и массе — нет. Сводка и легенда называют их «не учтено».
        /// </summary>
        public readonly SortedDictionary<string, string> UncountedMaterials = new SortedDictionary<string, string>(StringComparer.Ordinal);

        readonly Dictionary<string, GdmlMaterial> uncountedMaterialOf = new Dictionary<string, GdmlMaterial>(StringComparer.Ordinal);

        /// <summary>Состав вещества из <see cref="UncountedMaterials"/>; null — не найден.</summary>
        public GdmlMaterial MaterialOf(string name)
        {
            GdmlMaterial material;
            return name != null && this.uncountedMaterialOf.TryGetValue(name, out material) ? material : null;
        }

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

        readonly Dictionary<string, GdmlMaterial> fileElements = new Dictionary<string, GdmlMaterial>(StringComparer.Ordinal);
        readonly Dictionary<string, GdmlMaterial> fileIsotopes = new Dictionary<string, GdmlMaterial>(StringComparer.Ordinal);
        readonly HashSet<string> warningsSeen = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Угол по умолчанию — радиан (`RotationRead`, `PolyconeRead`); внутри хранятся градусы.</summary>
        const double AngleDefaultScale = 180.0 / Math.PI;
        const double PhiTolerance = 1e-9;

        /// <summary>kAngTolerance/2 в градусах: `dPhi >= twopi - kAngTolerance*0.5` — полный оборот.</summary>
        const double CsgPhiTolerance = 2.9e-8;
        const double MaxAngleDegrees = 1e9;

        /// <summary>Предел вложенности вычитаний: стек потока приложения — 1 МБ.</summary>
        public const int MaxSolidDepth = 256;

        /// <summary>
        /// Предел вложенности составов. Настоящие составы — 2–3 уровня; при 256 цепочка в
        /// 200000 веществ считалась 35 с (каждая сборка мусора обходит глубокий стек), при 32 — секунды.
        /// </summary>
        const int MaxMaterialDepth = 32;

        const int MaxVolumeDepth = 64;

        /// <summary>
        /// Предел числа размещённых томов. Файл в 40 уровней по две копии просит 2^40:
        /// 1512 МБ и падение процесса. Настоящие файлы набора — десятки томов.
        /// </summary>
        public const int MaxInstances = 100000;

        /// <summary>Предел шагов обхода: том с миллионом циклических детей томов не родит.</summary>
        const int MaxPlacementSteps = 1000000;

        const int MaxWarnings = 500;

        /// <summary>Проверка профиля на самопересечение квадратична по числу точек.</summary>
        const int MaxProfileCheck = 10000;

        /// <summary>
        /// Предел пар отрезков на весь файл: проверка самопересечения квадратична, и предел на тело не
        /// спасал от сорока профилей по 10000 точек (30.8 с). Одно тело на пределе `MaxProfileCheck` — 5e7 пар.
        /// </summary>
        const long MaxProfilePairs = 60000000;

        long profilePairs;

        /// <summary>setup: имя -> мир, как `setupMap` Geant4 (одноимённый — последний).</summary>
        readonly Dictionary<string, string> setups = new Dictionary<string, string>(StringComparer.Ordinal);
        string firstSetup;

        /// <summary>Константы, определённые дважды или под именем единицы: Geant4 такой файл отвергает.</summary>
        readonly HashSet<string> poisonedConstants = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>`TWOPI` и подобные: взяты без определения в файле; строка — один раз на имя.</summary>
        readonly HashSet<string> assumedConstants = new HashSet<string>(StringComparer.Ordinal);

        readonly Dictionary<string, GdmlPoint> scales = new Dictionary<string, GdmlPoint>(StringComparer.Ordinal);

        int warningsDropped;
        int placementSteps;
        bool limitHit;

        sealed class GdmlVolumeDef
        {
            public string Name = "";
            public string MaterialRef = "";
            public string SolidRef = "";
            public readonly List<GdmlChild> Children = new List<GdmlChild>();

            /// <summary>Что в томе не размещено (replicavol, внешний файл, потерянный ребёнок); пусто — всё.</summary>
            public string UnknownContent = "";

            /// <summary>Тома, чьи копии не размещены (`replicavol`, `paramvol`, `divisionvol`): их вещества сводка называет «не учтено».</summary>
            public readonly List<string> UnreadRefs = new List<string>();

            /// <summary>Вещества непрочитанных копий уже собраны: том размещается много раз, обход — один.</summary>
            public bool UnreadCollected;

            public bool ChildrenMeasured;
            public double ChildVolumeMm3;
            public bool ChildVolumeKnown;
            public string ChildNote = "";
        }

        sealed class GdmlChild
        {
            public string Name = "";
            public string VolumeRef = "";
            public GdmlTransform Placement;
            public bool PlacementKnown = true;
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
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        geometry.FatalError = string.Format(CultureInfo.InvariantCulture,
                            GdmlText.T("GdmlFatalRoot", "root element is <{0}>, not <gdml>"),
                            reader.NodeType.ToString());
                        return geometry;
                    }

                    // `G4GDMLRead::Read` имени корня не проверяет: `gdml_simple_extension` читается.
                    if (!string.Equals(reader.LocalName, "gdml", StringComparison.Ordinal))
                    {
                        geometry.Warn("GdmlWarnRootOther", "root element is <{0}>; read as <gdml>, as in Geant4", reader.LocalName);
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
                                case "setup":
                                    geometry.ReadSetup(LoadSection(reader));
                                    break;
                                case "userinfo":
                                    reader.Skip();
                                    break;
                                default:
                                    geometry.Warn("GdmlWarnUnsupportedIn", "{0}: unsupported <{1}>", "gdml", reader.LocalName);
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
                // Запрет DTD остаётся; текст исключения про `DtdProcessing` читателю ничего не скажет.
                geometry.FatalError = UsesDtd(path)
                    ? GdmlText.T("GdmlFatalDtd",
                        "the file uses <!DOCTYPE> or <!ENTITY> to include other files; this is not supported and is not read, for safety: put the included text into the file itself")
                    : ex.Message;
                return geometry;
            }

            if (geometry.FatalError == null)
            {
                geometry.ResolveMaterials();
                geometry.ComputeAllSolids();
                geometry.BuildInstances();
                geometry.Measure();
            }

            foreach (string name in geometry.assumedConstants)
            {
                geometry.WarnPriority("GdmlWarnConstantAssumed", "{0} is not defined in the file: its usual value is taken, Geant4 would refuse the file", name);
            }

            if (geometry.warningsDropped > 0)
            {
                geometry.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlWarnDropped", "... and {0} more note(s)"), geometry.warningsDropped));
            }

            return geometry;
        }

        /// <summary>Объявление DTD стоит до корня: достаточно начала файла.</summary>
        static bool UsesDtd(string path)
        {
            try
            {
                using (StreamReader stream = new StreamReader(path))
                {
                    char[] buffer = new char[1 << 20];
                    string head = new string(buffer, 0, stream.Read(buffer, 0, buffer.Length));
                    return head.IndexOf("<!DOCTYPE", StringComparison.Ordinal) >= 0
                        || head.IndexOf("<!ENTITY", StringComparison.Ordinal) >= 0;
                }
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// Хвост-указатель имени (`detLV0x8f9550`), как `G4GDMLRead::StripName`: всё от первого
        /// «0x». Вещества, элементы и изотопы Geant4 так и хранит; имена томов — только в показе.
        /// </summary>
        public static string StripName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            int index = name.IndexOf("0x", StringComparison.Ordinal);
            return index < 0 ? name : name.Substring(0, index);
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
            // Geant4 атрибутов единиц у корня не читает (`G4GDMLRead::Read`): единица
            // по умолчанию — всегда мм и радиан. Подменять её файлом нельзя, но и
            // молчать о таком файле нельзя: его автор, скорее всего, ждал другого.
            string lunit = reader.GetAttribute("lunit");
            if (!string.IsNullOrEmpty(lunit))
            {
                this.Warn("GdmlWarnRootUnit",
                    "<gdml>: attribute {0}=\"{1}\" is ignored, as in Geant4; the defaults stay mm and rad",
                    "lunit", lunit);
            }

            string aunit = reader.GetAttribute("aunit");
            if (!string.IsNullOrEmpty(aunit))
            {
                this.Warn("GdmlWarnRootUnit",
                    "<gdml>: attribute {0}=\"{1}\" is ignored, as in Geant4; the defaults stay mm and rad",
                    "aunit", aunit);
            }
        }

        void ReadSetup(XElement setup)
        {
            string name = Attr(setup, "name");
            string world = "";
            foreach (XElement part in setup.Elements())
            {
                if (string.Equals(part.Name.LocalName, "world", StringComparison.Ordinal))
                {
                    world = Attr(part, "ref");
                }
            }

            if (world.Length == 0)
            {
                this.Warn("GdmlWarnSetupNoWorld", "setup {0}: no <world ref>", name);
                return;
            }

            if (this.firstSetup == null)
            {
                this.firstSetup = name;
            }

            this.setups[name] = world;
        }

        /// <summary>
        /// Как `G4GDMLReadSetup::GetSetup`: единственная сборка — как бы ни называлась, из
        /// нескольких — «Default». Без неё Geant4 падает дальше; здесь первая и строка.
        /// </summary>
        string SetupWorld()
        {
            if (this.setups.Count == 0)
            {
                return "";
            }

            string world;
            if (this.setups.Count == 1)
            {
                return this.setups[this.firstSetup];
            }

            if (this.setups.TryGetValue("Default", out world))
            {
                return world;
            }

            this.WarnPriority("GdmlWarnSetupNoDefault", "setup: there are several setups and none is Default; setup {0} is read",
                this.firstSetup);
            return this.setups[this.firstSetup];
        }

        void ReadDefine(XElement define)
        {
            foreach (XElement element in define.Elements())
            {
                string tag = element.Name.LocalName;
                string name = Attr(element, "name");
                if (tag == "position")
                {
                    double x, y, z;
                    if (this.TryVector(element, "define/position " + name, out x, out y, out z))
                    {
                        if (this.positions.ContainsKey(name))
                        {
                            this.Warn("GdmlWarnDefineLast", "{0} {1} is defined twice; the last one is read, as in Geant4", "position", name);
                        }

                        this.positions[name] = new GdmlPoint(x, y, z);
                    }
                }
                else if (tag == "rotation")
                {
                    double x, y, z;
                    if (this.TryAngles(element, "define/rotation " + name, out x, out y, out z))
                    {
                        if (this.rotations.ContainsKey(name))
                        {
                            this.Warn("GdmlWarnDefineLast", "{0} {1} is defined twice; the last one is read, as in Geant4", "rotation", name);
                        }

                        this.rotations[name] = new GdmlPoint(x, y, z);
                    }
                }
                else if (tag == "constant" || tag == "variable" || tag == "expression")
                {
                    double value;
                    string text;
                    if (tag == "expression")
                    {
                        if (!TryDirectText(element, out text))
                        {
                            this.Warn("GdmlWarnExpressionNested", "define/expression {0}: nested elements are not an expression", name);
                            continue;
                        }
                    }
                    else
                    {
                        text = Attr(element, "value");
                    }

                    if (this.TryNumber(text, out value))
                    {
                        this.DefineConstant(tag, name, value);
                    }
                    else
                    {
                        this.Say("define/" + tag + " " + name, ReasonNumber("value", text));
                    }
                }
                else if (tag == "quantity")
                {
                    // Как `QuantityRead`: значение × единица, в системе вычислителя
                    // (мм, радиан; плотность — г/см3).
                    double value, scale;
                    string text = Attr(element, "value");
                    string unit = Attr(element, "unit");
                    if (!this.TryNumber(text, out value) || IsBad(value))
                    {
                        this.Say("define/quantity " + name, ReasonNumber("value", text));
                    }
                    else if (!TryQuantityScale(unit, out scale))
                    {
                        this.Say("define/quantity " + name, ReasonUnit(unit));
                    }
                    else if (IsBad(value * scale))
                    {
                        this.Say("define/quantity " + name, ReasonNumber("value", text));
                    }
                    else
                    {
                        this.DefineConstant(tag, name, value * scale);
                    }
                }
                else if (tag == "scale")
                {
                    double x, y, z;
                    if (this.TryScale(element, "define/scale " + name, out x, out y, out z))
                    {
                        this.scales[name] = new GdmlPoint(x, y, z);
                    }
                }
                else if (tag == "loop")
                {
                    this.WarnLoop("define", element);
                }
                else
                {
                    this.Warn("GdmlWarnUnsupportedIn", "{0}: unsupported <{1}>", "define", tag);
                }
            }
        }

        /// <summary>
        /// `loop` разворачивает значения и тела по переменной; здесь её нет, и то, что он
        /// порождает, в файле как бы не существует: об этом строка, с границами цикла.
        /// </summary>
        void WarnLoop(string owner, XElement loop)
        {
            this.Warn("GdmlWarnLoop", "{0}: <loop> is not read ({1} from {2} to {3}): what it creates is missing",
                owner, Clip(Attr(loop, "for")), Clip(Attr(loop, "from")), Clip(Attr(loop, "to")));
        }

        /// <summary>
        /// Текст самого элемента, без потомков. `XElement.Value` рекурсивен: вложенность в
        /// 20000 уровней роняла процесс переполнением стека (0xC00000FD), и его не поймать.
        /// </summary>
        static bool TryDirectText(XElement element, out string text)
        {
            StringBuilder builder = new StringBuilder();
            text = "";
            for (XNode node = element.FirstNode; node != null; node = node.NextNode)
            {
                if (node is XElement)
                {
                    return false;
                }

                XText part = node as XText;
                if (part != null)
                {
                    builder.Append(part.Value);
                }
            }

            text = builder.ToString();
            return true;
        }

        /// <summary>
        /// Как `G4GDMLEvaluator::DefineConstant`: повторное определение и имя единицы Geant4
        /// отвергает всему файлу. Два разных значения выбрать не из чего, поэтому всё, что на
        /// такую константу ссылается, — «не измерено» со своей строкой.
        /// </summary>
        void DefineConstant(string tag, string name, double value)
        {
            double previous;
            if (this.poisonedConstants.Contains(name))
            {
                return;
            }

            if (GdmlExpression.IsTaken(name))
            {
                this.Warn("GdmlWarnConstantBuiltin",
                    "define/{0} {1}: this name belongs to a unit or a constant, Geant4 refuses it; what uses it is not measured", tag, name);
                this.poisonedConstants.Add(name);
                return;
            }

            if (this.constants.TryGetValue(name, out previous))
            {
                if (previous == value)
                {
                    this.Warn("GdmlWarnConstantSame", "define/{0} {1} is defined twice with the same value; Geant4 refuses such a file", tag, name);
                    return;
                }

                this.Warn("GdmlWarnConstantTwice",
                    "define/{0} {1} is defined twice ({2} and {3}), Geant4 refuses it; what uses it is not measured",
                    tag, name, GdmlText.N(previous), GdmlText.N(value));
                this.constants.Remove(name);
                this.poisonedConstants.Add(name);
                return;
            }

            this.constants[name] = value;
        }

        void ReadMaterials(XElement materials)
        {
            foreach (XElement element in materials.Elements())
            {
                string tag = element.Name.LocalName;
                if (tag == "material")
                {
                    this.ReadMaterial(element);
                }
                else if (tag == "element")
                {
                    this.ReadElement(element);
                }
                else if (tag == "isotope")
                {
                    this.ReadIsotope(element);
                }
                else if (tag == "loop")
                {
                    this.WarnLoop("materials", element);
                }
                else
                {
                    this.Warn("GdmlWarnUnsupportedIn", "{0}: unsupported <{1}>", "materials", tag);
                }
            }
        }

        void ReadMaterial(XElement element)
        {
            GdmlMaterial material = new GdmlMaterial();
            material.Name = StripName(Attr(element, "name"));
            material.Origin = "file";
            string owner = "material " + material.Name;
            if (material.Name.Length == 0)
            {
                this.Warn("GdmlWarnNoName", "{0}: <{1}> without a name is skipped", "materials", "material");
                return;
            }

            // `G4Material::GetMaterial` находит первое из таблицы: второе читать незачем.
            if (this.fileMaterials.ContainsKey(material.Name))
            {
                this.Warn("GdmlWarnDefinedFirst", "{0} {1} is defined twice; the first one is read, as in Geant4", "material", material.Name);
                return;
            }

            string zText = Attr(element, "Z");
            if (zText.Length > 0)
            {
                int z;
                if (this.TryElementZ(zText, owner, out z))
                {
                    material.Fractions[z] = 1.0;
                }
            }

            bool hasFractions = false;
            bool hasAtoms = false;
            foreach (XElement part in element.Elements())
            {
                string tag = part.Name.LocalName;
                if (tag == "D")
                {
                    this.ReadDensity(part, material, owner);
                }
                else if (tag == "Dref")
                {
                    double value;
                    if (this.constants.TryGetValue(Attr(part, "ref"), out value) && value > 0.0)
                    {
                        material.Density = value;
                        material.HasDensity = true;
                    }
                    else
                    {
                        this.Warn("GdmlWarnDensityRef", "{0}: density reference {1} is not read",
                            owner, Attr(part, "ref"));
                    }
                }
                else if (tag == "atom")
                {
                    double mass;
                    if (this.TryAtom(part, owner, out mass))
                    {
                        material.AtomicMass = mass;
                    }
                }
                else if (tag == "fraction" || tag == "composite")
                {
                    string reference = StripName(Attr(part, "ref"));
                    string text = Attr(part, "n");
                    double value;
                    if (!this.TryNumber(text, out value) || value < 0.0)
                    {
                        // Доля не прочитана — состав неполон, а не «без этой строки».
                        this.Warn("GdmlWarnFractionNumber", "{0}: fraction of {1} = \"{2}\" is not a non-negative number",
                            owner, reference, Clip(text));
                        material.Partial = true;
                        AddMissing(material, reference);
                        continue;
                    }

                    if (tag == "composite")
                    {
                        hasAtoms = true;
                    }
                    else
                    {
                        hasFractions = true;
                    }

                    material.Refs.Add(new GdmlMaterialEntry(reference, value, tag == "composite"));
                }
                else if (tag == "atomref" || tag == "T" || tag == "Tref" || tag == "P" || tag == "Pref"
                         || tag == "MEE" || tag == "MEEref" || tag == "property")
                {
                    // Температура, давление, потенциал, оптика: на массу и состав не влияют.
                }
                else
                {
                    this.Warn("GdmlWarnUnsupportedIn", "{0}: unsupported <{1}>", owner, tag);
                }
            }

            // `MaterialRead`: есть составляющие — смесь, а Z при них не значит ничего. В
            // настоящих файлах это обычное дело: <material Z="1" name="G4_AIR"> с долями N, O, Ar.
            if (hasAtoms || hasFractions)
            {
                material.Fractions.Clear();
            }

            if (hasAtoms && hasFractions)
            {
                this.Warn("GdmlWarnMixedParts", "{0}: <fraction> and <composite> are mixed; composition is not read", owner);
                material.Refs.Clear();
                material.Partial = true;
                AddMissing(material, material.Name);
            }

            this.fileMaterials[material.Name] = material;
        }

        void ReadDensity(XElement density, GdmlMaterial material, string owner)
        {
            string text = Attr(density, "value");
            string unit = Attr(density, "unit");
            double value, scale;
            if (!this.TryNumber(text, out value))
            {
                this.Say(owner, ReasonNumber("D", text));
            }
            else if (!TryDensityScale(unit, out scale))
            {
                this.Say(owner, ReasonUnit(unit));
            }
            else if (!(value > 0.0))
            {
                this.Warn("GdmlWarnDensityValue", "{0}: density {1} is not positive; mass is not computed",
                    owner, GdmlText.N(value));
            }
            else
            {
                material.Density = value * scale;
                material.HasDensity = true;
            }
        }

        void ReadElement(XElement element)
        {
            GdmlMaterial item = new GdmlMaterial { Name = StripName(Attr(element, "name")), Origin = "file" };
            string owner = "element " + item.Name;
            if (item.Name.Length == 0)
            {
                this.Warn("GdmlWarnNoName", "{0}: <{1}> without a name is skipped", "materials", "element");
                return;
            }

            // `G4Element::GetElement` находит первый из таблицы.
            if (this.fileElements.ContainsKey(item.Name))
            {
                this.Warn("GdmlWarnDefinedFirst", "{0} {1} is defined twice; the first one is read, as in Geant4", "element", item.Name);
                return;
            }
            int z = 0;
            string zText = Attr(element, "Z");
            if (zText.Length > 0)
            {
                this.TryElementZ(zText, owner, out z);
            }

            double abundance = 0.0;
            double massSum = 0.0;
            bool massKnown = true;
            foreach (XElement part in element.Elements())
            {
                string tag = part.Name.LocalName;
                if (tag == "atom")
                {
                    double mass;
                    if (this.TryAtom(part, owner, out mass))
                    {
                        item.AtomicMass = mass;
                    }
                }
                else if (tag == "fraction")
                {
                    string reference = StripName(Attr(part, "ref"));
                    string text = Attr(part, "n");
                    double value;
                    GdmlMaterial isotope;
                    if (!this.TryNumber(text, out value) || value < 0.0)
                    {
                        this.Warn("GdmlWarnFractionNumber", "{0}: fraction of {1} = \"{2}\" is not a non-negative number",
                            owner, reference, Clip(text));
                        massKnown = false;
                    }
                    else if (!this.fileIsotopes.TryGetValue(reference, out isotope))
                    {
                        this.Warn("GdmlWarnIsotopeMissing", "{0}: isotope {1} not found", owner, reference);
                        massKnown = false;
                    }
                    else
                    {
                        foreach (int isotopeZ in isotope.Fractions.Keys)
                        {
                            if (z == 0)
                            {
                                z = isotopeZ;
                            }
                            else if (z != isotopeZ)
                            {
                                this.Warn("GdmlWarnIsotopeZ", "{0}: isotope {1} has Z = {2}, the element has Z = {3}",
                                    owner, reference, isotopeZ, z);
                                massKnown = false;
                            }
                        }

                        abundance += value;
                        massSum += value * isotope.AtomicMass;
                        massKnown = massKnown && isotope.AtomicMass > 0.0;
                    }
                }
                else
                {
                    this.Warn("GdmlWarnUnsupportedIn", "{0}: unsupported <{1}>", owner, tag);
                }
            }

            if (item.AtomicMass <= 0.0 && massKnown && abundance > 0.0)
            {
                item.AtomicMass = massSum / abundance;
            }

            if (z > 0)
            {
                item.Fractions[z] = 1.0;
            }
            else
            {
                this.Warn("GdmlWarnElementNoZ", "{0}: Z is not known", owner);
                item.Partial = true;
                AddMissing(item, item.Name);
            }

            this.fileElements[item.Name] = item;
        }

        void ReadIsotope(XElement element)
        {
            GdmlMaterial item = new GdmlMaterial { Name = StripName(Attr(element, "name")), Origin = "file" };
            string owner = "isotope " + item.Name;
            if (item.Name.Length == 0)
            {
                this.Warn("GdmlWarnNoName", "{0}: <{1}> without a name is skipped", "materials", "isotope");
                return;
            }

            // `G4Isotope::GetIsotope` находит первый из таблицы.
            if (this.fileIsotopes.ContainsKey(item.Name))
            {
                this.Warn("GdmlWarnDefinedFirst", "{0} {1} is defined twice; the first one is read, as in Geant4", "isotope", item.Name);
                return;
            }
            int z;
            if (this.TryElementZ(Attr(element, "Z"), owner, out z))
            {
                item.Fractions[z] = 1.0;
            }

            foreach (XElement part in element.Elements())
            {
                if (string.Equals(part.Name.LocalName, "atom", StringComparison.Ordinal))
                {
                    double mass;
                    if (this.TryAtom(part, owner, out mass))
                    {
                        item.AtomicMass = mass;
                    }
                }
            }

            this.fileIsotopes[item.Name] = item;
        }

        bool TryElementZ(string text, string owner, out int z)
        {
            z = 0;
            double value;
            if (this.TryNumber(text, out value)
                && Math.Abs(value - Math.Round(value)) < 1e-6
                && value >= 1.0 && value <= 118.0)
            {
                z = (int)Math.Round(value);
                return true;
            }

            this.Warn("GdmlWarnElementZ", "{0}: Z = \"{1}\" is not an element number", owner, Clip(text));
            return false;
        }

        bool TryAtom(XElement atom, string owner, out double mass)
        {
            mass = 0.0;
            string text = Attr(atom, "value");
            string unit = Attr(atom, "unit").Trim();
            double scale;
            if (unit.Length == 0 || unit == "g/mole" || unit == "g/mol")
            {
                scale = 1.0;
            }
            else if (unit == "kg/mole" || unit == "kg/mol")
            {
                scale = 1000.0;
            }
            else
            {
                this.Say(owner, ReasonUnit(unit));
                return false;
            }

            double value;
            if (!this.TryNumber(text, out value) || !(value > 0.0))
            {
                this.Say(owner, ReasonNumber("atom", text));
                return false;
            }

            mass = value * scale;
            return true;
        }

        static void AddMissing(GdmlMaterial material, string name)
        {
            if (!material.Missing.Contains(name))
            {
                material.Missing.Add(name);
            }
        }

        void ReadSolids(XElement section)
        {
            foreach (XElement element in section.Elements())
            {
                string sectionTag = element.Name.LocalName;
                if (sectionTag == "loop")
                {
                    this.WarnLoop("solids", element);
                    continue;
                }

                // Свойства поверхности, а не тело; на геометрию и массу не влияют.
                if (sectionTag == "opticalsurface")
                {
                    this.Warn("GdmlWarnOpticalSurface", "solids: optical surface {0} is not a body and is not read", Attr(element, "name"));
                    continue;
                }

                GdmlSolid solid = this.ReadSolid(element);
                if (string.IsNullOrEmpty(solid.Name))
                {
                    this.Warn("GdmlWarnSolidNoName", "solids: <{0}> without a name is skipped", solid.Tag);
                    continue;
                }

                // `G4SolidStore::GetSolid` при reverseSearch = false отдаёт первое.
                if (this.solids.ContainsKey(solid.Name))
                {
                    this.Warn("GdmlWarnDefinedFirst", "{0} {1} is defined twice; the first one is read, as in Geant4", "solid", StripName(solid.Name));
                    continue;
                }

                this.solids[solid.Name] = solid;
            }

            foreach (GdmlSolid solid in this.solids.Values)
            {
                if (solid.Kind != GdmlSolidKind.Subtraction && solid.Kind != GdmlSolidKind.Union)
                {
                    continue;
                }

                solid.First = solid.FirstRef.Length == 0 ? null : this.FindSolid(solid.FirstRef, solid);
                solid.Second.Clear();
                solid.SecondPlacements.Clear();
                foreach (string reference in solid.SecondRefs)
                {
                    GdmlSolid second = this.FindSolid(reference, solid);
                    if (second != null)
                    {
                        solid.Second.Add(second);
                        solid.SecondPlacements.Add(solid.SecondPlacementRead ?? GdmlTransform.Identity());
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

            // Вычитаемого нет — нет и объёма: «первое тело целиком» было бы подменой.
            this.Refuse(owner, name.Length == 0
                ? GdmlText.T("GdmlReasonRefEmpty", "an empty reference to a body")
                : string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonRefMissing", "reference {0} not found"), name));
            return null;
        }

        /// <summary>Тело не прочитано: причина идёт в Note и строкой в сводку, объём не считается.</summary>
        void Refuse(GdmlSolid solid, string reason)
        {
            solid.Unreadable = true;
            if (solid.Note.Length == 0)
            {
                solid.Note = reason;
            }

            this.Say(solid.Tag + " " + StripName(solid.Name), reason);
        }

        bool SolidNumber(GdmlSolid solid, XElement element, string attribute, out double value)
        {
            string text = Attr(element, attribute);
            if (text.Length == 0)
            {
                value = 0.0;
                this.Refuse(solid, ReasonMissing(attribute));
                return false;
            }

            if (this.TryNumber(text, out value))
            {
                return true;
            }

            this.Refuse(solid, ReasonNumber(attribute, text));
            return false;
        }

        double SolidLengthUnit(GdmlSolid solid, XElement element)
        {
            string unit = Attr(element, "lunit");
            double scale;
            if (TryLengthScale(unit, out scale))
            {
                return scale;
            }

            this.Refuse(solid, ReasonUnit(unit));
            return 1.0;
        }

        /// <summary>
        /// `TubeRead`, `ConeRead`: длина z — полная. G4Tubs отказывает при z ≤ 0, rmin ≥ rmax и rmin &lt; 0.
        /// G4Cons мягче: отказ только при половине z &lt; 0 и при
        /// `(rmin1 >= rmax1 || rmin2 >= rmax2 || rmin1 &lt; 0) &amp;&amp; rmin2 &lt; 0` (`G4Cons.cc:100`, `:111`).
        /// Здесь строже нарочно: кольца с вывернутыми радиусами не имеют объёма, и такое тело не измеряется, со строкой.
        /// </summary>
        void ReadCsgRevolution(GdmlSolid solid, XElement element, bool cone)
        {
            double unit = this.SolidLengthUnit(solid, element);
            this.ReadPhi(solid, element, true);

            double z, rmin1 = 0.0, rmax1, rmin2 = 0.0, rmax2;
            bool ok = this.SolidNumber(solid, element, "z", out z);
            if (cone)
            {
                ok &= this.SolidNumber(solid, element, "rmax1", out rmax1);
                ok &= this.SolidNumber(solid, element, "rmax2", out rmax2);
                if (Attr(element, "rmin1").Length > 0)
                {
                    ok &= this.SolidNumber(solid, element, "rmin1", out rmin1);
                }

                if (Attr(element, "rmin2").Length > 0)
                {
                    ok &= this.SolidNumber(solid, element, "rmin2", out rmin2);
                }
            }
            else
            {
                ok &= this.SolidNumber(solid, element, "rmax", out rmax1);
                rmax2 = rmax1;
                if (Attr(element, "rmin").Length > 0)
                {
                    ok &= this.SolidNumber(solid, element, "rmin", out rmin1);
                    rmin2 = rmin1;
                }
            }

            if (!ok)
            {
                return;
            }

            if (!(z > 0.0))
            {
                this.Refuse(solid, string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonCsgZ", "z = {0} is not positive"), GdmlText.N(z)));
                return;
            }

            bool ring = rmin1 >= 0.0 && rmin2 >= 0.0 && rmin1 <= rmax1 && rmin2 <= rmax2
                        && (cone || rmin1 < rmax1);
            if (!ring)
            {
                this.Refuse(solid, string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonRadii", "rmin = {0}, rmax = {1} at z = {2} is not a ring"),
                    GdmlText.N(rmin1), GdmlText.N(rmax1), GdmlText.N(-z / 2.0)));
                return;
            }

            solid.Planes.Add(new[] { -z / 2.0 * unit, rmin1 * unit, rmax1 * unit });
            solid.Planes.Add(new[] { z / 2.0 * unit, rmin2 * unit, rmax2 * unit });
        }

        void ReadPhi(GdmlSolid solid, XElement element, bool csg = false)
        {
            string aunit = Attr(element, "aunit");
            double scale;
            if (!TryAngleScale(aunit, out scale))
            {
                this.Refuse(solid, ReasonUnit(aunit));
                scale = AngleDefaultScale;
            }

            double value;
            if (Attr(element, "startphi").Length > 0 && this.SolidNumber(solid, element, "startphi", out value))
            {
                solid.StartPhi = value * scale;
            }

            // `G4Polycone.cc:232`, `G4GenericPolycone`: Δ ≤ 0 или больше оборота — полный оборот.
            // Пропущенный (по схеме обязателен) у Geant4 равен 0, то есть тоже полный оборот.
            // G4Tubs и G4Cons (`CheckDPhiAngle`) строже: Δ ≤ 0, в том числе пропущенный, у них ошибка.
            string delta = Attr(element, "deltaphi");
            if (csg)
            {
                if (this.SolidNumber(solid, element, "deltaphi", out value))
                {
                    double degrees = value * scale;
                    if (IsBad(degrees))
                    {
                        this.Refuse(solid, ReasonNumber("deltaphi", delta));
                    }
                    else if (degrees <= 0.0)
                    {
                        this.Refuse(solid, string.Format(CultureInfo.InvariantCulture,
                            GdmlText.T("GdmlReasonCsgDeltaPhi", "deltaphi = {0} is not positive"), Clip(delta)));
                    }
                    else if (degrees < 360.0 - CsgPhiTolerance)
                    {
                        solid.DeltaPhi = degrees;
                    }
                }
            }
            else if (delta.Length == 0)
            {
                this.Warn("GdmlWarnDeltaPhiMissing", "{0} {1}: deltaphi is missing; read as a full revolution, as in Geant4", solid.Tag, StripName(solid.Name));
            }
            else if (this.SolidNumber(solid, element, "deltaphi", out value))
            {
                double degrees = value * scale;
                if (IsBad(degrees))
                {
                    this.Refuse(solid, ReasonNumber("deltaphi", delta));
                }
                else if (degrees <= 0.0 || degrees > 360.0 + PhiTolerance)
                {
                    this.Warn("GdmlWarnDeltaPhiFull", "{0} {1}: deltaphi = \"{2}\" is read as a full revolution, as in Geant4",
                        solid.Tag, StripName(solid.Name), Clip(delta));
                }
                else
                {
                    solid.DeltaPhi = degrees;
                }
            }
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
                    double unit = this.SolidLengthUnit(solid, element);
                    double x, y, z;
                    bool ok = this.SolidNumber(solid, element, "x", out x);
                    ok &= this.SolidNumber(solid, element, "y", out y);
                    ok &= this.SolidNumber(solid, element, "z", out z);
                    if (ok && !(x > 0.0 && y > 0.0 && z > 0.0))
                    {
                        this.Refuse(solid, string.Format(CultureInfo.InvariantCulture,
                            GdmlText.T("GdmlReasonBoxSize", "size {0} x {1} x {2} is not positive"),
                            GdmlText.N(x), GdmlText.N(y), GdmlText.N(z)));
                        ok = false;
                    }

                    if (ok)
                    {
                        solid.SizeX = x * unit;
                        solid.SizeY = y * unit;
                        solid.SizeZ = z * unit;
                    }

                    break;
                }

                case "polycone":
                {
                    solid.Kind = GdmlSolidKind.Polycone;
                    double unit = this.SolidLengthUnit(solid, element);
                    this.ReadPhi(solid, element);
                    foreach (XElement plane in element.Elements())
                    {
                        if (!string.Equals(plane.Name.LocalName, "zplane", StringComparison.Ordinal))
                        {
                            this.Refuse(solid, ReasonTag(plane.Name.LocalName));
                            continue;
                        }

                        // По схеме rmin не обязателен, его умолчание — 0; z и rmax обязательны.
                        double z, rmin = 0.0, rmax;
                        bool ok = this.SolidNumber(solid, plane, "z", out z);
                        ok &= this.SolidNumber(solid, plane, "rmax", out rmax);
                        if (Attr(plane, "rmin").Length > 0)
                        {
                            ok &= this.SolidNumber(solid, plane, "rmin", out rmin);
                        }

                        if (!ok)
                        {
                            continue;
                        }

                        if (rmin < 0.0 || rmin > rmax)
                        {
                            this.Refuse(solid, string.Format(CultureInfo.InvariantCulture,
                                GdmlText.T("GdmlReasonRadii", "rmin = {0}, rmax = {1} at z = {2} is not a ring"),
                                GdmlText.N(rmin), GdmlText.N(rmax), GdmlText.N(z)));
                            continue;
                        }

                        solid.Planes.Add(new[] { z * unit, rmin * unit, rmax * unit });
                    }

                    if (solid.Planes.Count < 2)
                    {
                        this.Refuse(solid, GdmlText.T("GdmlReasonFewPlanes", "fewer than two z-planes"));
                    }
                    else if (!PlanesAreOrdered(solid))
                    {
                        this.Refuse(solid, GdmlText.T("GdmlReasonPlaneOrder", "z-planes are not ordered along z"));
                    }

                    break;
                }

                case "genericPolycone":
                {
                    solid.Kind = GdmlSolidKind.GenericPolycone;
                    double unit = this.SolidLengthUnit(solid, element);
                    this.ReadPhi(solid, element);
                    foreach (XElement point in element.Elements())
                    {
                        if (!string.Equals(point.Name.LocalName, "rzpoint", StringComparison.Ordinal))
                        {
                            this.Refuse(solid, ReasonTag(point.Name.LocalName));
                            continue;
                        }

                        double r, z;
                        bool ok = this.SolidNumber(solid, point, "r", out r);
                        ok &= this.SolidNumber(solid, point, "z", out z);
                        if (!ok)
                        {
                            continue;
                        }

                        if (r < 0.0)
                        {
                            this.Refuse(solid, string.Format(CultureInfo.InvariantCulture,
                                GdmlText.T("GdmlReasonNegativeR", "r = {0} is negative"), GdmlText.N(r)));
                            continue;
                        }

                        solid.Profile.Add(new[] { r * unit, z * unit });
                    }

                    if (solid.Profile.Count < 3)
                    {
                        this.Refuse(solid, GdmlText.T("GdmlReasonFewPoints", "fewer than three points"));
                    }

                    break;
                }

                case "tessellated":
                    solid.Kind = GdmlSolidKind.Tessellated;
                    this.ReadTessellated(element, solid);
                    break;

                case "tube":
                    solid.Kind = GdmlSolidKind.Polycone;
                    this.ReadCsgRevolution(solid, element, false);
                    break;

                case "cone":
                    solid.Kind = GdmlSolidKind.Polycone;
                    this.ReadCsgRevolution(solid, element, true);
                    break;

                case "subtraction":
                    solid.Kind = GdmlSolidKind.Subtraction;
                    this.ReadSubtraction(element, solid);
                    break;

                case "union":
                    solid.Kind = GdmlSolidKind.Union;
                    this.ReadSubtraction(element, solid);
                    break;

                default:
                    solid.Kind = GdmlSolidKind.Unknown;
                    solid.Note = ReasonTag(tag);
                    this.Warn("GdmlWarnSolidUnsupported", "solid {0}: unsupported body <{1}>", StripName(solid.Name), tag);
                    break;
            }

            return solid;
        }

        static bool PlanesAreOrdered(GdmlSolid solid)
        {
            int direction = 0;
            for (int i = 0; i + 1 < solid.Planes.Count; i++)
            {
                double dz = solid.Planes[i + 1][0] - solid.Planes[i][0];
                if (IsBad(dz))
                {
                    return false;
                }

                int sign = Math.Sign(dz);
                if (sign == 0)
                {
                    continue;
                }

                if (direction != 0 && sign != direction)
                {
                    return false;
                }

                direction = sign;
            }

            return true;
        }

        void ReadSubtraction(XElement element, GdmlSolid solid)
        {
            string owner = solid.Tag + " " + StripName(solid.Name);
            GdmlPoint position = new GdmlPoint(0.0, 0.0, 0.0);
            GdmlPoint rotation = new GdmlPoint(0.0, 0.0, 0.0);
            GdmlPoint firstPosition = new GdmlPoint(0.0, 0.0, 0.0);
            GdmlPoint firstRotation = new GdmlPoint(0.0, 0.0, 0.0);
            bool ok = true;

            foreach (XElement part in element.Elements())
            {
                string partTag = part.Name.LocalName;
                switch (partTag)
                {
                    case "first":
                        solid.FirstRef = Attr(part, "ref");
                        break;
                    case "second":
                        solid.SecondRefs.Add(Attr(part, "ref"));
                        break;
                    case "position":
                        ok &= this.TryVector(part, owner, out position.X, out position.Y, out position.Z);
                        break;
                    case "positionref":
                        ok &= this.TryPositionRef(part, owner, out position);
                        break;
                    case "rotation":
                        ok &= this.TryAngles(part, owner, out rotation.X, out rotation.Y, out rotation.Z);
                        break;
                    case "rotationref":
                        ok &= this.TryRotationRef(part, owner, out rotation);
                        break;
                    case "firstposition":
                        ok &= this.TryVector(part, owner, out firstPosition.X, out firstPosition.Y, out firstPosition.Z);
                        break;
                    case "firstpositionref":
                        ok &= this.TryPositionRef(part, owner, out firstPosition);
                        break;
                    case "firstrotation":
                        ok &= this.TryAngles(part, owner, out firstRotation.X, out firstRotation.Y, out firstRotation.Z);
                        break;
                    case "firstrotationref":
                        ok &= this.TryRotationRef(part, owner, out firstRotation);
                        break;
                    default:
                        this.Refuse(solid, ReasonTag(partTag));
                        break;
                }
            }

            if (!ok)
            {
                // Строка о самом числе уже стоит; здесь — следствие для тела.
                this.Refuse(solid, GdmlText.T("GdmlReasonSubPlacement", "placement of a body is not read"));
            }

            if (solid.FirstRef.Length == 0)
            {
                this.Refuse(solid, GdmlText.T("GdmlReasonNoFirst", "no <first>"));
            }

            if (solid.SecondRefs.Count != 1)
            {
                this.Refuse(solid, string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonSecondCount", "expected one <second>, found {0}"),
                    solid.SecondRefs.Count));
            }

            // Как `BooleanRead` (`G4GDMLReadSolids.cc:193`): матрица БЕЗ обращения,
            // в отличие от physvol (см. GdmlTransform.FromPhysvol).
            solid.SecondPlacementRead = GdmlTransform.FromEuler(rotation.X, rotation.Y, rotation.Z,
                                                                position.X, position.Y, position.Z);
            if (firstPosition.X != 0.0 || firstPosition.Y != 0.0 || firstPosition.Z != 0.0
                || firstRotation.X != 0.0 || firstRotation.Y != 0.0 || firstRotation.Z != 0.0)
            {
                solid.FirstPlacement = GdmlTransform.FromEuler(firstRotation.X, firstRotation.Y, firstRotation.Z,
                                                               firstPosition.X, firstPosition.Y, firstPosition.Z);
            }
        }

        void ReadTessellated(XElement element, GdmlSolid solid)
        {
            List<GdmlPoint> local = new List<GdmlPoint>();
            Dictionary<string, int> named = new Dictionary<string, int>(StringComparer.Ordinal);
            double unit = this.SolidLengthUnit(solid, element);

            // `TessellatedRead` читает у тела только имя: единица у сетки — на грани.
            // Сами вершины в define/position уже в мм, поэтому lunit тела на них не
            // влияет, и файл, где на него рассчитывали, должен об этом узнать.
            string solidUnit = Attr(element, "lunit");
            if (solidUnit.Length > 0 && unit != 1.0)
            {
                this.Warn("GdmlWarnTessLunit",
                    "tessellated {0}: lunit=\"{1}\" on the body is ignored, as in Geant4; only a face carries a unit",
                    StripName(solid.Name), solidUnit);
            }

            foreach (XElement vertex in element.Elements())
            {
                if (string.Equals(vertex.Name.LocalName, "vertex", StringComparison.Ordinal))
                {
                    double x, y, z;
                    bool ok = this.SolidNumber(solid, vertex, "x", out x);
                    ok &= this.SolidNumber(solid, vertex, "y", out y);
                    ok &= this.SolidNumber(solid, vertex, "z", out z);
                    if (ok)
                    {
                        local.Add(new GdmlPoint(x * unit, y * unit, z * unit));
                    }
                }
            }

            int skipped = 0;
            string firstMissing = "";
            foreach (XElement face in element.Elements())
            {
                string tag = face.Name.LocalName;
                if (tag == "vertex")
                {
                    continue;
                }

                if (tag != "triangular" && tag != "quadrangular")
                {
                    this.Refuse(solid, ReasonTag(tag));
                    continue;
                }

                // `G4GDMLReadSolids.cc:2687`: вершина грани × её собственный lunit.
                double faceScale;
                string faceUnit = Attr(face, "lunit");
                if (!TryLengthScale(faceUnit, out faceScale))
                {
                    this.Refuse(solid, ReasonUnit(faceUnit));
                    skipped++;
                    continue;
                }

                // `TriangularRead`: только точное "RELATIVE"; всё прочее у Geant4 — ABSOLUTE.
                string type = Attr(face, "type");
                bool relative = string.Equals(type, "RELATIVE", StringComparison.Ordinal);
                if (!relative && type.Length > 0 && !string.Equals(type, "ABSOLUTE", StringComparison.Ordinal))
                {
                    this.Warn("GdmlWarnFaceType", "tessellated {0}: type=\"{1}\" is read as ABSOLUTE, as in Geant4", StripName(solid.Name), Clip(type));
                }

                int[] indices = new int[tag == "quadrangular" ? 4 : 3];
                bool ok = true;
                for (int i = 0; i < indices.Length; i++)
                {
                    string key = "vertex" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    string value = Attr(face, key);
                    int index;
                    bool byName;
                    if (!this.ResolveVertex(value, faceScale, local, named, out index, out byName))
                    {
                        if (firstMissing.Length == 0)
                        {
                            firstMissing = value;
                        }

                        ok = false;
                        break;
                    }

                    // RELATIVE: вершины 2, 3, 4 — смещения от первой (`G4TriangularFacet`).
                    if (relative && byName && i > 0)
                    {
                        GdmlPoint offset = local[index];
                        GdmlPoint start = local[indices[0]];
                        index = local.Count;
                        local.Add(new GdmlPoint(start.X + offset.X, start.Y + offset.Y, start.Z + offset.Z));
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
                this.Refuse(solid, firstMissing.Length > 0
                    ? string.Format(CultureInfo.InvariantCulture,
                        GdmlText.T("GdmlReasonFacesSkipped", "{0} face(s) skipped, first missing vertex: {1}"),
                        skipped, firstMissing)
                    : string.Format(CultureInfo.InvariantCulture,
                        GdmlText.T("GdmlReasonFacesSkippedPlain", "{0} face(s) skipped"), skipped));
            }

            if (solid.Triangles.Count == 0)
            {
                this.Refuse(solid, GdmlText.T("GdmlReasonNoTriangles", "no triangles"));
            }
        }

        bool ResolveVertex(string value, double scale, List<GdmlPoint> local,
                           Dictionary<string, int> named, out int index, out bool byName)
        {
            index = -1;
            byName = false;
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            string key = scale == 1.0
                ? value
                : value + "|" + scale.ToString("R", CultureInfo.InvariantCulture);
            if (named.TryGetValue(key, out index))
            {
                byName = true;
                return true;
            }

            GdmlPoint point;
            if (this.positions.TryGetValue(value, out point))
            {
                index = local.Count;
                local.Add(new GdmlPoint(point.X * scale, point.Y * scale, point.Z * scale));
                named[key] = index;
                byName = true;
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
                if (string.Equals(element.Name.LocalName, "loop", StringComparison.Ordinal))
                {
                    this.WarnLoop("structure", element);
                    continue;
                }

                if (!string.Equals(element.Name.LocalName, "volume", StringComparison.Ordinal))
                {
                    this.Warn("GdmlWarnUnsupportedIn", "{0}: unsupported <{1}>", "structure", element.Name.LocalName);
                    continue;
                }

                GdmlVolumeDef definition = new GdmlVolumeDef();
                definition.Name = Attr(element, "name");
                foreach (XElement part in element.Elements())
                {
                    string tag = part.Name.LocalName;
                    if (tag == "materialref")
                    {
                        definition.MaterialRef = StripName(Attr(part, "ref"));
                    }
                    else if (tag == "solidref")
                    {
                        definition.SolidRef = Attr(part, "ref");
                    }
                    else if (tag == "physvol")
                    {
                        GdmlChild child = this.ReadPhysvol(part, definition);
                        if (child != null)
                        {
                            definition.Children.Add(child);
                        }
                    }
                    else if (tag == "loop")
                    {
                        this.WarnLoop("volume " + definition.Name, part);
                        definition.UnknownContent = "<loop>";

                        // (четвёртый заход) Вещества тел внутри `loop` — тоже «не учтено», как у `paramvol`/`divisionvol`.
                        definition.UnreadRefs.AddRange(VolumeRefsIn(part));
                    }
                    else if (tag == "replicavol" && this.ReadReplica(part, definition))
                    {
                        // Копии вдоль декартовой оси размещены детьми (третий заход fixGdml2); строку пишет ReadReplica.
                    }
                    else if (tag == "replicavol" || tag == "paramvol" || tag == "divisionvol")
                    {
                        // Копии этих тегов не размещаются: масса матери без них — ложь.
                        this.Warn("GdmlWarnVolumeCopies",
                            "volume {0}: <{1}> is not supported, its copies are not counted ({2})",
                            StripName(definition.Name), tag, DescribeCopies(part));
                        definition.UnknownContent = "<" + tag + ">";
                        definition.UnreadRefs.AddRange(VolumeRefsIn(part));
                    }
                    else if (tag != "auxiliary")
                    {
                        // `Volume_contentRead` считает незнакомое расширением: тоже строка.
                        this.Warn("GdmlWarnUnsupportedIn", "{0}: unsupported <{1}>", "volume " + StripName(definition.Name), tag);
                    }
                }

                if (definition.Name.Length == 0)
                {
                    this.Warn("GdmlWarnNoName", "{0}: <{1}> without a name is skipped", "structure", "volume");
                    continue;
                }

                // `G4LogicalVolumeStore::GetVolume` при reverseSearch = false отдаёт первый.
                if (this.volumeDefs.ContainsKey(definition.Name))
                {
                    this.Warn("GdmlWarnDefinedFirst", "{0} {1} is defined twice; the first one is read, as in Geant4", "volume", StripName(definition.Name));
                    continue;
                }

                if (definition.SolidRef.Length == 0)
                {
                    this.Warn("GdmlWarnVolumeNoRef", "volume {0}: no <{1}>", StripName(definition.Name), "solidref");
                }

                if (definition.MaterialRef.Length == 0)
                {
                    this.Warn("GdmlWarnVolumeNoRef", "volume {0}: no <{1}>", StripName(definition.Name), "materialref");
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
                        this.Warn("GdmlWarnChildMissing", "volume {0}: child {1} not found", StripName(definition.Name), StripName(child.VolumeRef));
                        definition.UnknownContent = StripName(child.VolumeRef);
                    }
                }
            }
        }

        /// <summary>Предел копий одной репликации: дальше — прежняя строка «не поддержано», а не сто тысяч томов.</summary>
        const int MaxReplicaCopies = 10000;

        /// <summary>
        /// (`AMBER301`, fixGdml2, третий заход по критику `critGdml2`) `replicavol` с `replicate_along_axis` по x, y или z —
        /// как `G4GDMLReadStructure::ReplicaRead` и `G4PVReplica`: `number` копий тома толщиной `width` вдоль оси, копия i — в
        /// −width·(number − 1)/2 + width·i (`G4ReplicaNavigation::ComputeTransformation`, `offset` у декартовой оси не участвует;
        /// `position`/`rotation` внутри Geant4 читает и не применяет). `lht.gdml`: калориметр — 4 ряда по 6 кристаллов CsI
        /// 6 × 6 × 30 см, прежде пустая коробка воздуха без единого кристалла. Ось rho/phi, единица не длины, нет единицы
        /// (у Geant4 это множитель 0), нецелое число — false, и остаётся прежняя строка «копии не учтены».
        /// </summary>
        bool ReadReplica(XElement part, GdmlVolumeDef definition)
        {
            // `G4GDMLReadStructure.cc:977`: без атрибута `number` Geant4 берёт 1 (значение по умолчанию переменной).
            double number = 1.0;
            string numberText = Attr(part, "number");
            if ((numberText.Length > 0 && !this.TryNumber(numberText, out number)) || number < 1.0 || number != Math.Floor(number) || number > MaxReplicaCopies)
            {
                return false;
            }

            string reference = null;
            XElement along = null;
            foreach (XElement inner in part.Elements())
            {
                string tag = inner.Name.LocalName;
                if (tag == "volumeref" && reference == null)
                {
                    reference = Attr(inner, "ref");
                }
                else if (tag == "replicate_along_axis" && along == null)
                {
                    along = inner;
                }
                else
                {
                    return false;
                }
            }

            if (string.IsNullOrEmpty(reference) || along == null)
            {
                return false;
            }

            int axis = -1;
            double width = double.NaN;
            string[] names = { "x", "y", "z", "rho", "phi" };
            foreach (XElement item in along.Elements())
            {
                switch (item.Name.LocalName)
                {
                    case "direction":
                        // `AxisRead`: значение 1 у атрибута задаёт ось; последний такой атрибут главнее.
                        foreach (XAttribute attribute in item.Attributes())
                        {
                            double flag;
                            int index = Array.IndexOf(names, attribute.Name.LocalName);
                            if (index >= 0 && this.TryNumber(attribute.Value, out flag) && flag == 1.0)
                            {
                                axis = index;
                            }
                        }

                        break;
                    case "width":
                        if (!this.TryReplicaLength(item, out width))
                        {
                            return false;
                        }

                        break;
                    case "offset":
                        double offset;
                        if (!this.TryReplicaLength(item, out offset))
                        {
                            return false;
                        }

                        break;
                    case "position":
                    case "rotation":
                    case "positionref":
                    case "rotationref":
                        break;
                    default:
                        return false;
                }
            }

            if (axis < 0 || axis > 2 || !(width > 0.0) || IsBad(width * number))
            {
                return false;
            }

            int count = (int)number;
            for (int i = 0; i < count; i++)
            {
                double at = -width * 0.5 * (count - 1) + width * i;
                GdmlChild child = new GdmlChild
                {
                    Name = StripName(reference) + "_PV",
                    VolumeRef = reference,
                    PlacementKnown = true,
                    Placement = GdmlTransform.FromPhysvol(0.0, 0.0, 0.0, axis == 0 ? at : 0.0, axis == 1 ? at : 0.0, axis == 2 ? at : 0.0),
                };
                definition.Children.Add(child);
            }

            this.Warn("GdmlWarnReplicaRead", "volume {0}: <replicavol> is read, copies: {1} ({2} along {3}, {4} mm each)",
                StripName(definition.Name), count, StripName(reference), names[axis], GdmlText.N(width));
            return true;
        }

        /// <summary>`QuantityRead`: значение и единица длины; без единицы Geant4 умножает на 0 — здесь отказ.</summary>
        bool TryReplicaLength(XElement item, out double value)
        {
            value = double.NaN;
            string unit = Attr(item, "unit");
            double scale;
            if (unit.Length == 0 || !TryLengthScale(unit, out scale) || !this.TryNumber(Attr(item, "value"), out value))
            {
                return false;
            }

            value *= scale;
            return !IsBad(value);
        }

        /// <summary>Все `volumeref` внутри элемента на любой глубине: у `loop` они стоят в `physvol`, у `paramvol` — прямо.</summary>
        static List<string> VolumeRefsIn(XElement part)
        {
            List<string> refs = new List<string>();
            foreach (XElement inner in part.Descendants())
            {
                string reference = Attr(inner, "ref");
                if (string.Equals(inner.Name.LocalName, "volumeref", StringComparison.Ordinal) && reference.Length > 0 && !refs.Contains(reference))
                {
                    refs.Add(reference);
                }
            }

            return refs;
        }

        static string DescribeCopies(XElement part)
        {
            StringBuilder text = new StringBuilder();
            string number = Attr(part, "number");
            if (number.Length == 0)
            {
                number = Attr(part, "ncopies");
            }

            if (number.Length > 0)
            {
                text.Append(Clip(number)).Append(" x ");
            }

            foreach (XElement inner in part.Elements())
            {
                if (string.Equals(inner.Name.LocalName, "volumeref", StringComparison.Ordinal))
                {
                    text.Append(StripName(Attr(inner, "ref")));
                    break;
                }
            }

            return text.Length > 0 ? text.ToString() : "?";
        }

        GdmlChild ReadPhysvol(XElement element, GdmlVolumeDef mother)
        {
            GdmlChild child = new GdmlChild();
            child.Name = Attr(element, "name");
            string owner = "physvol " + StripName(child.Name);

            GdmlPoint position = new GdmlPoint(0.0, 0.0, 0.0);
            GdmlPoint rotation = new GdmlPoint(0.0, 0.0, 0.0);
            bool known = true;
            double scaleX = 1.0, scaleY = 1.0, scaleZ = 1.0;
            string externalFile = null;

            foreach (XElement part in element.Elements())
            {
                string tag = part.Name.LocalName;
                switch (tag)
                {
                    case "volumeref":
                        child.VolumeRef = Attr(part, "ref");
                        break;
                    case "positionref":
                        known &= this.TryPositionRef(part, owner, out position);
                        break;
                    case "position":
                        known &= this.TryVector(part, owner, out position.X, out position.Y, out position.Z);
                        break;
                    case "rotationref":
                        known &= this.TryRotationRef(part, owner, out rotation);
                        break;
                    case "rotation":
                        known &= this.TryAngles(part, owner, out rotation.X, out rotation.Y, out rotation.Z);
                        break;
                    case "scale":
                        known &= this.TryScale(part, owner, out scaleX, out scaleY, out scaleZ)
                                 && this.ToMirror(owner, ref scaleX, ref scaleY, ref scaleZ);
                        break;
                    case "scaleref":
                        GdmlPoint named;
                        if (this.scales.TryGetValue(Attr(part, "ref"), out named))
                        {
                            scaleX = named.X;
                            scaleY = named.Y;
                            scaleZ = named.Z;
                            known &= this.ToMirror(owner, ref scaleX, ref scaleY, ref scaleZ);
                        }
                        else
                        {
                            this.Warn("GdmlWarnScaleRef", "{0}: <scaleref> {1} is not read; placement is not known", owner, Attr(part, "ref"));
                            known = false;
                        }

                        break;
                    case "file":
                        externalFile = Attr(part, "name");
                        break;
                    case "auxiliary":
                        break;
                    default:
                        this.Warn("GdmlWarnUnsupportedIn", "{0}: unsupported <{1}>", owner, tag);
                        break;
                }
            }

            if (externalFile != null)
            {
                this.Warn("GdmlWarnFile", "{0}: external file {1} is not read; volume {2} is incomplete",
                    owner, externalFile, StripName(mother.Name));
                mother.UnknownContent = externalFile;
                return null;
            }

            if (child.VolumeRef.Length == 0)
            {
                this.Warn("GdmlWarnPhysvolNoRef", "{0}: no <volumeref>; volume {1} is incomplete", owner, StripName(mother.Name));
                mother.UnknownContent = StripName(child.Name);
                return null;
            }

            child.PlacementKnown = known;
            child.Placement = GdmlTransform.FromPhysvol(rotation.X, rotation.Y, rotation.Z,
                                                        position.X, position.Y, position.Z);
            child.Placement.Mirror(scaleX, scaleY, scaleZ);
            return child;
        }

        bool TryPositionRef(XElement part, string owner, out GdmlPoint point)
        {
            string reference = Attr(part, "ref");
            if (this.positions.TryGetValue(reference, out point))
            {
                return true;
            }

            this.Warn("GdmlWarnPositionMissing", "{0}: position {1} not found; placement is not known", owner, reference);
            point = new GdmlPoint(0.0, 0.0, 0.0);
            return false;
        }

        bool TryRotationRef(XElement part, string owner, out GdmlPoint point)
        {
            string reference = Attr(part, "ref");
            if (this.rotations.TryGetValue(reference, out point))
            {
                return true;
            }

            this.Warn("GdmlWarnRotationMissing", "{0}: rotation {1} not found; placement is not known", owner, reference);
            point = new GdmlPoint(0.0, 0.0, 0.0);
            return false;
        }

        bool TryScale(XElement part, string owner, out double x, out double y, out double z)
        {
            x = y = z = 1.0;
            bool ok = true;
            double[] values = { 1.0, 1.0, 1.0 };
            string[] axes = { "x", "y", "z" };
            for (int i = 0; i < 3; i++)
            {
                string text = Attr(part, axes[i]);
                double value;
                if (text.Length == 0)
                {
                    continue;
                }

                if (this.TryNumber(text, out value))
                {
                    values[i] = value;
                }
                else
                {
                    this.Say(owner, ReasonNumber("scale/" + axes[i], text));
                    ok = false;
                }
            }

            x = values[0];
            y = values[1];
            z = values[2];
            return ok;
        }

        /// <summary>
        /// Как `G4ReflectionFactory::Place`: из `scale` остаются только знаки. Без отражения
        /// (произведение больше нуля) величина отбрасывается молча и положение сохраняется:
        /// `TestNTST` несёт 1.000037 в 328 местах. С отражением величины обязаны быть единицами
        /// с точностью 1e-8 (`CheckScale`), иначе Geant4 отказывает файлу: положение неизвестно.
        /// </summary>
        bool ToMirror(string owner, ref double x, ref double y, ref double z)
        {
            if (x == 1.0 && y == 1.0 && z == 1.0)
            {
                return true;
            }

            double product = x * y * z;
            bool unit = Math.Abs(Math.Abs(x) - 1.0) + Math.Abs(Math.Abs(y) - 1.0) + Math.Abs(Math.Abs(z) - 1.0) <= 1e-8;
            if (product == 0.0 || (product < 0.0 && !unit))
            {
                this.Warn("GdmlWarnScaleBad", "{0}: <scale> {1} {2} {3} is not a mirror and is not applied; placement is not known",
                    owner, GdmlText.N(x), GdmlText.N(y), GdmlText.N(z));
                x = y = z = 1.0;
                return false;
            }

            if (unit)
            {
                this.Warn("GdmlWarnScaleMirror", "{0}: <scale> {1} {2} {3} is read as a mirror",
                    owner, GdmlText.N(x), GdmlText.N(y), GdmlText.N(z));
            }
            else
            {
                // Без имени тома: у `TestNTST` одна и та же строка в 328 местах.
                this.Warn("GdmlWarnScaleSize", "<scale> {0} {1} {2}: the size is dropped and only the signs are read, as in Geant4",
                    GdmlText.N(x), GdmlText.N(y), GdmlText.N(z));
            }

            x = Math.Sign(x);
            y = Math.Sign(y);
            z = Math.Sign(z);
            return true;
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
                this.Warn("GdmlWarnMaterialCycle", "material {0}: reference cycle", name);
                resolved = new GdmlMaterial { Name = name, Unknown = true, Origin = "cycle" };
                this.resolvedMaterials[name] = resolved;
                return resolved;
            }

            // Предел глубины ставится НА РЕКУРСИЮ: цепочка в 200000 ссылок иначе
            // кончается переполнением стека, а оно роняет процесс без сообщения.
            if (visiting.Count >= MaxMaterialDepth)
            {
                this.Warn("GdmlWarnMaterialDeep", "material {0}: references are nested deeper than {1} levels; composition is not read",
                    name, MaxMaterialDepth);
                resolved = new GdmlMaterial { Name = name, Unknown = true, Origin = "too deep" };
                this.resolvedMaterials[name] = resolved;
                return resolved;
            }

            GdmlMaterial source;
            if (this.fileMaterials.TryGetValue(name, out source))
            {
                resolved = new GdmlMaterial { Name = name, Origin = "file" };
                resolved.Density = source.Density;
                resolved.HasDensity = source.HasDensity;
                resolved.AtomicMass = source.AtomicMass;
                resolved.Partial = source.Partial;
                resolved.Missing.AddRange(source.Missing);

                if (source.Fractions.Count > 0)
                {
                    // <material Z="82"> — простое вещество.
                    CopyComposition(source, resolved);
                }
                else if (source.Refs.Count == 0)
                {
                    GdmlMaterial table;
                    if (GdmlG4Materials.Table.TryGetValue(name, out table) && !source.Partial)
                    {
                        CopyComposition(table, resolved);
                        if (!resolved.HasDensity && table.HasDensity)
                        {
                            resolved.Density = table.Density;
                            resolved.HasDensity = true;
                        }

                        resolved.Origin = "file + G4";
                    }
                    else if (source.Partial)
                    {
                        // Строка о причине (нечитаемая доля) уже стоит.
                        resolved.Unknown = !resolved.HasDensity;
                    }
                    else if (resolved.HasDensity)
                    {
                        // Плотность есть, состава нет: масса считается, но в шапке
                        // это «неизвестный состав», а не просто вещество без долей.
                        this.Warn("GdmlWarnDensityOnly", "material {0}: density only, composition is unknown", name);
                        resolved.Partial = true;
                        AddMissing(resolved, name);
                    }
                    else
                    {
                        resolved.Unknown = true;
                    }
                }
                else
                {
                    visiting.Add(name);
                    this.ResolveEntries(source, resolved, visiting);
                    visiting.Remove(name);
                    if (resolved.Fractions.Count == 0 && !resolved.HasDensity)
                    {
                        resolved.Unknown = true;
                    }
                }

                if (!resolved.Partial && !resolved.Unknown)
                {
                    double sum = 0.0;
                    foreach (double fraction in resolved.Fractions.Values)
                    {
                        sum += fraction;
                    }

                    if (sum <= 0.0)
                    {
                        // composite n="0": все составляющие прочитаны, а состава нет.
                        this.Warn("GdmlWarnEmptyComposition", "material {0}: composition is empty", name);
                        resolved.Partial = true;
                        AddMissing(resolved, name);
                    }
                    else
                    {
                        if (Math.Abs(sum - 1.0) > 1e-3)
                        {
                            this.Warn("GdmlWarnFractionSum", "material {0}: fractions sum to {1}, not to 1", name, GdmlText.N(sum));
                        }

                        // `G4Material::FillVectors`: доли делятся на сумму (`G4Material.cc:566`);
                        // ESR 0.333 x 3 у Geant4 читается как 0.333333. Масса от этого не зависит.
                        List<int> keys = new List<int>(resolved.Fractions.Keys);
                        foreach (int key in keys)
                        {
                            resolved.Fractions[key] /= sum;
                        }
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

            this.Warn("GdmlWarnMaterialMissing", "material {0}: not found in the file or in the built-in G4 table", name);
            resolved = new GdmlMaterial { Name = name, Unknown = true, Origin = "unknown" };
            this.resolvedMaterials[name] = resolved;
            return resolved;
        }

        void ResolveEntries(GdmlMaterial source, GdmlMaterial resolved, HashSet<string> visiting)
        {
            Dictionary<int, double> atomMasses = new Dictionary<int, double>();
            double atomTotal = 0.0;

            foreach (GdmlMaterialEntry entry in source.Refs)
            {
                GdmlMaterial sub = this.ResolveEntry(entry.Reference, visiting);
                if (sub == null || sub.Unknown)
                {
                    resolved.Partial = true;
                    AddMissing(resolved, entry.Reference);
                    continue;
                }

                if (sub.Partial)
                {
                    resolved.Partial = true;
                    foreach (string missing in sub.Missing)
                    {
                        AddMissing(resolved, missing);
                    }
                }

                if (sub.Fractions.Count == 0)
                {
                    // Вещество только с плотностью: его доля в смеси не ложится
                    // ни на один элемент и не должна исчезать молча.
                    resolved.Partial = true;
                    AddMissing(resolved, entry.Reference);
                    continue;
                }

                if (entry.IsAtoms)
                {
                    double atomic;
                    int z;
                    if (!this.AtomicMassOf(sub, entry.Reference, source.Name, out z, out atomic))
                    {
                        resolved.Partial = true;
                        AddMissing(resolved, entry.Reference);
                        continue;
                    }

                    double part;
                    atomMasses.TryGetValue(z, out part);
                    atomMasses[z] = part + entry.Fraction * atomic;
                    atomTotal += entry.Fraction * atomic;
                }
                else
                {
                    foreach (KeyValuePair<int, double> fraction in sub.Fractions)
                    {
                        double value;
                        resolved.Fractions.TryGetValue(fraction.Key, out value);
                        resolved.Fractions[fraction.Key] = value + entry.Fraction * fraction.Value;
                    }
                }
            }

            if (atomTotal > 0.0)
            {
                foreach (KeyValuePair<int, double> part in atomMasses)
                {
                    double value;
                    resolved.Fractions.TryGetValue(part.Key, out value);
                    resolved.Fractions[part.Key] = value + part.Value / atomTotal;
                }
            }
        }

        /// <summary>
        /// Ссылка внутри состава. Порядок как `MixtureRead`: сперва элемент (файла,
        /// затем по символу, как `G4NistManager::FindOrBuildElement`), потом вещество.
        /// </summary>
        GdmlMaterial ResolveEntry(string reference, HashSet<string> visiting)
        {
            GdmlMaterial element;
            if (this.fileElements.TryGetValue(reference, out element))
            {
                return element;
            }

            int z = GdmlG4Materials.ZOf(reference);
            if (z > 0)
            {
                if (this.fileMaterials.ContainsKey(reference))
                {
                    this.Warn("GdmlWarnSymbolClash",
                        "reference {0} is both an element symbol and a material; the element is read, as in Geant4",
                        reference);
                }

                element = new GdmlMaterial { Name = reference, Origin = "symbol" };
                element.Fractions[z] = 1.0;
                this.fileElements[reference] = element;
                return element;
            }

            return this.ResolveMaterial(reference, visiting);
        }

        /// <summary>
        /// Атомная масса для `composite`: из файла, иначе из единственной таблицы
        /// приложения (<see cref="AttenuationData.AtomicMass"/>, база matdb).
        /// </summary>
        bool AtomicMassOf(GdmlMaterial sub, string reference, string owner, out int z, out double atomic)
        {
            z = 0;
            atomic = 0.0;
            if (sub.Fractions.Count != 1)
            {
                this.Warn("GdmlWarnCompositeNotElement", "material {0}: <composite> {1} is not an element", owner, reference);
                return false;
            }

            foreach (int key in sub.Fractions.Keys)
            {
                z = key;
            }

            if (sub.AtomicMass > 0.0)
            {
                atomic = sub.AtomicMass;
                return true;
            }

            try
            {
                if (AttenuationData.AtomicMass.TryGetValue(z, out atomic) && atomic > 0.0)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                this.Warn("GdmlWarnAtomicMassError", "material {0}: atomic masses are not available: {1}", owner, ex.Message);
                return false;
            }

            this.Warn("GdmlWarnAtomicMassMissing", "material {0}: no atomic mass for {1}", owner, GdmlG4Materials.SymbolOf(z));
            return false;
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
                this.Warn("GdmlWarnSolidCycle", "solid {0}: reference cycle", StripName(solid.Name));
                solid.VolumeKnown = false;
                if (solid.Note.Length == 0)
                {
                    solid.Note = GdmlText.T("GdmlReasonSolidCycle", "reference cycle");
                }

                solid.Computed = true;
                return;
            }

            // Предел глубины — на рекурсию: цепочка в 200000 вычитаний иначе роняет
            // процесс переполнением стека (0xC00000FD), а его не поймать ничем.
            if (visiting.Count > MaxSolidDepth)
            {
                this.Warn("GdmlWarnSolidDeep", "solid {0}: bodies are nested deeper than {1} levels; volume is not measured",
                    StripName(solid.Name), MaxSolidDepth);
                solid.VolumeKnown = false;
                solid.Note = string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonSolidDeep", "nested deeper than {0} levels"), MaxSolidDepth);
                visiting.Remove(solid);
                solid.Computed = true;
                return;
            }

            switch (solid.Kind)
            {
                case GdmlSolidKind.Box:
                    if (!solid.Unreadable)
                    {
                        solid.VolumeMm3 = solid.SizeX * solid.SizeY * solid.SizeZ;
                        solid.VolumeKnown = true;
                        SetBounds(solid, -solid.SizeX / 2.0, solid.SizeX / 2.0,
                                        -solid.SizeY / 2.0, solid.SizeY / 2.0,
                                        -solid.SizeZ / 2.0, solid.SizeZ / 2.0);
                    }

                    break;

                case GdmlSolidKind.Polycone:
                    if (!solid.Unreadable)
                    {
                        this.ComputePolycone(solid);
                    }

                    break;

                case GdmlSolidKind.GenericPolycone:
                    if (!solid.Unreadable)
                    {
                        this.ComputeGenericPolycone(solid);
                    }

                    break;

                case GdmlSolidKind.Tessellated:
                    this.ComputeTessellated(solid);
                    break;

                case GdmlSolidKind.Subtraction:
                    this.ComputeSubtraction(solid, visiting);
                    break;

                case GdmlSolidKind.Union:
                    this.ComputeUnion(solid, visiting);
                    break;

                default:
                    solid.VolumeKnown = false;
                    break;
            }

            if (IsBad(solid.VolumeMm3))
            {
                solid.VolumeMm3 = 0.0;
                solid.VolumeKnown = false;
                this.Refuse(solid, GdmlText.T("GdmlReasonVolumeOverflow", "volume is out of range"));
            }

            visiting.Remove(solid);
            solid.Computed = true;
        }

        void ComputePolycone(GdmlSolid solid)
        {
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

            double rmin = double.MaxValue;
            foreach (double[] plane in solid.Planes)
            {
                rmin = Math.Min(rmin, plane[1]);
                rmax = Math.Max(rmax, plane[2]);
                zmin = Math.Min(zmin, plane[0]);
                zmax = Math.Max(zmax, plane[0]);
            }

            // Знак здесь только от направления z: кольца и порядок плоскостей уже
            // проверены при чтении, поэтому модуль не прячет rmin > rmax.
            volume = Math.Abs(volume);
            volume = this.ApplyPhi(solid, volume);
            this.SetRevolution(solid, volume, rmin, rmax, zmin, zmax);
        }

        void ComputeGenericPolycone(GdmlSolid solid)
        {
            double moment = 0.0;
            double rmin = double.MaxValue;
            double rmax = 0.0;
            double zmin = double.MaxValue;
            double zmax = double.MinValue;
            for (int i = 0; i < solid.Profile.Count; i++)
            {
                double[] a = solid.Profile[i];
                double[] b = solid.Profile[(i + 1) % solid.Profile.Count];
                moment += (a[0] + b[0]) * (a[0] * b[1] - b[0] * a[1]);
                rmin = Math.Min(rmin, Math.Min(a[0], b[0]));
                rmax = Math.Max(rmax, Math.Max(a[0], b[0]));
                zmin = Math.Min(zmin, Math.Min(a[1], b[1]));
                zmax = Math.Max(zmax, Math.Max(a[1], b[1]));
            }

            // `G4GenericPolycone.cc:124` отказывает самопересекающемуся профилю; формула объёма
            // на нём вернула бы разность петель, а не тело.
            long pairs = (long)solid.Profile.Count * (solid.Profile.Count - 1) / 2;
            bool overBudget = solid.Profile.Count <= MaxProfileCheck && this.profilePairs + pairs > MaxProfilePairs;
            if (!overBudget && solid.Profile.Count <= MaxProfileCheck)
            {
                this.profilePairs += pairs;
            }

            string crossing = overBudget
                ? string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonProfileBudget", "not checked for self-crossing: the profiles of the file together exceed {0} pairs of segments"),
                    MaxProfilePairs)
                : solid.Profile.Count > MaxProfileCheck
                ? string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonProfileLong", "profile has {0} points; it is not checked for self-crossing beyond {1}"),
                    solid.Profile.Count, MaxProfileCheck)
                : ProfileCrossesItself(solid) ? GdmlText.T("GdmlReasonProfileCrossing", "profile crosses itself") : null;
            if (crossing != null)
            {
                this.Refuse(solid, crossing);
                solid.VolumeMm3 = 0.0;
                solid.VolumeKnown = false;
                SetRevolutionBounds(solid, rmin, rmax, zmin, zmax);
                return;
            }

            double volume = Math.Abs(2.0 * Math.PI * moment / 6.0);
            volume = this.ApplyPhi(solid, volume);
            this.SetRevolution(solid, volume, rmin, rmax, zmin, zmax);
        }

        /// <summary>Как `G4ReduciblePolygon::CrossesItself(1/kInfinity)`.</summary>
        static bool ProfileCrossesItself(GdmlSolid solid)
        {
            const double Tolerance = 1.0 / 9.0e99;
            double one = 1.0 - Tolerance;
            List<double[]> points = solid.Profile;
            int count = points.Count;
            for (int i = 0; i + 1 < count; i++)
            {
                double[] a = points[i];
                double[] b = points[i + 1];
                double da1 = b[0] - a[0];
                double db1 = b[1] - a[1];
                for (int j = i + 2; j < count; j++)
                {
                    double[] c = points[j];
                    double[] d = points[j + 1 == count ? 0 : j + 1];
                    double da2 = d[0] - c[0];
                    double db2 = d[1] - c[1];
                    double a12 = c[0] - a[0];
                    double b12 = c[1] - a[1];
                    double determinant = da1 * db2 - db1 * da2;
                    if (Math.Abs(determinant) > Tolerance * Tolerance)
                    {
                        double s1 = (a12 * db2 - b12 * da2) / determinant;
                        if (s1 >= Tolerance && s1 < one)
                        {
                            double s2 = -(da1 * b12 - db1 * a12) / determinant;
                            if (s2 >= Tolerance && s2 < one)
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        double ApplyPhi(GdmlSolid solid, double volume)
        {
            if (Math.Abs(solid.DeltaPhi - 360.0) > PhiTolerance)
            {
                volume *= Math.Abs(solid.DeltaPhi) / 360.0;
                solid.Note = string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlNotePartialPhi", "partial revolution {0} deg"), GdmlText.N(solid.DeltaPhi));
                this.Say(solid.Tag + " " + StripName(solid.Name), solid.Note);
            }

            return volume;
        }

        void SetRevolution(GdmlSolid solid, double volume, double rmin, double rmax, double zmin, double zmax)
        {
            solid.VolumeMm3 = volume;
            solid.VolumeKnown = volume > 0.0;
            if (volume == 0.0)
            {
                this.Refuse(solid, GdmlText.T("GdmlReasonZeroVolume", "volume is zero"));
            }

            SetRevolutionBounds(solid, rmin, rmax, zmin, zmax);
        }

        /// <summary>Габарит, как `BoundingLimits`: у неполного оборота — сектор, а не весь круг.</summary>
        static void SetRevolutionBounds(GdmlSolid solid, double rmin, double rmax, double zmin, double zmax)
        {
            double xmin = -rmax, xmax = rmax, ymin = -rmax, ymax = rmax;
            if (Math.Abs(solid.DeltaPhi - 360.0) > PhiTolerance)
            {
                double start = solid.StartPhi * Math.PI / 180.0;
                double end = (solid.StartPhi + solid.DeltaPhi) * Math.PI / 180.0;
                DiskExtent(rmin, rmax, Math.Sin(start), Math.Cos(start), Math.Sin(end), Math.Cos(end),
                           out xmin, out xmax, out ymin, out ymax);
            }

            SetBounds(solid, xmin, xmax, ymin, ymax, zmin, zmax);
        }

        /// <summary>Перенос `G4GeomTools::DiskExtent`, случай за случаем.</summary>
        static void DiskExtent(double rmin, double rmax, double sinStart, double cosStart, double sinEnd, double cosEnd,
                               out double xmin, out double xmax, out double ymin, out double ymax)
        {
            xmin = -rmax;
            ymin = -rmax;
            xmax = rmax;
            ymax = rmax;
            if (Math.Abs(sinEnd - sinStart) < 1e-9 && Math.Abs(cosEnd - cosStart) < 1e-9)
            {
                return;
            }

            int icase = cosEnd < 0 ? 1 : 0;
            if (sinEnd < 0)
            {
                icase += 2;
            }

            if (cosStart < 0)
            {
                icase += 4;
            }

            if (sinStart < 0)
            {
                icase += 8;
            }

            switch (icase)
            {
                case 0:
                    if (sinEnd < sinStart) { break; }
                    xmin = rmin * cosEnd; ymin = rmin * sinStart; xmax = rmax * cosStart; ymax = rmax * sinEnd;
                    break;
                case 1:
                    xmin = rmax * cosEnd; ymin = Math.Min(rmin * sinStart, rmin * sinEnd); xmax = rmax * cosStart; ymax = rmax;
                    break;
                case 2:
                    xmin = -rmax; ymin = -rmax; xmax = Math.Max(rmax * cosStart, rmax * cosEnd); ymax = rmax;
                    break;
                case 3:
                    xmin = -rmax; ymin = rmax * sinEnd; xmax = rmax * cosStart; ymax = rmax;
                    break;
                case 4:
                    xmin = -rmax; ymin = -rmax; xmax = rmax; ymax = Math.Max(rmax * sinStart, rmax * sinEnd);
                    break;
                case 5:
                    if (sinEnd > sinStart) { break; }
                    xmin = rmax * cosEnd; ymin = rmin * sinEnd; xmax = rmin * cosStart; ymax = rmax * sinStart;
                    break;
                case 6:
                    xmin = -rmax; ymin = -rmax; xmax = rmax * cosEnd; ymax = rmax * sinStart;
                    break;
                case 7:
                    xmin = -rmax; ymin = rmax * sinEnd; xmax = Math.Max(rmin * cosStart, rmin * cosEnd); ymax = rmax * sinStart;
                    break;
                case 8:
                    xmin = Math.Min(rmin * cosStart, rmin * cosEnd); ymin = rmax * sinStart; xmax = rmax; ymax = rmax * sinEnd;
                    break;
                case 9:
                    xmin = rmax * cosEnd; ymin = rmax * sinStart; xmax = rmax; ymax = rmax;
                    break;
                case 10:
                    if (sinEnd < sinStart) { break; }
                    xmin = rmin * cosStart; ymin = rmax * sinStart; xmax = rmax * cosEnd; ymax = rmin * sinEnd;
                    break;
                case 11:
                    xmin = -rmax; ymin = Math.Min(rmax * sinStart, rmax * sinEnd); xmax = rmax; ymax = rmax;
                    break;
                case 12:
                    xmin = rmax * cosStart; ymin = -rmax; xmax = rmax; ymax = rmax * sinEnd;
                    break;
                case 13:
                    xmin = Math.Min(rmax * cosStart, rmax * cosEnd); ymin = -rmax; xmax = rmax; ymax = rmax;
                    break;
                case 14:
                    xmin = rmax * cosStart; ymin = -rmax; xmax = rmax * cosEnd; ymax = Math.Max(rmin * sinStart, rmin * sinEnd);
                    break;
                case 15:
                    if (sinEnd > sinStart) { break; }
                    xmin = rmax * cosStart; ymin = rmax * sinEnd; xmax = rmin * cosEnd; ymax = rmin * sinStart;
                    break;
            }
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

            for (int i = 0; i < solid.Triangles.Count; i++)
            {
                GdmlPoint point = solid.Vertices[solid.Triangles[i]];
                xmin = Math.Min(xmin, point.X);
                xmax = Math.Max(xmax, point.X);
                ymin = Math.Min(ymin, point.Y);
                ymax = Math.Max(ymax, point.Y);
                zmin = Math.Min(zmin, point.Z);
                zmax = Math.Max(zmax, point.Z);
            }

            SetBounds(solid, xmin, xmax, ymin, ymax, zmin, zmax);

            if (solid.Unreadable)
            {
                solid.VolumeKnown = false;
                return;
            }

            // Сумма тетраэдров от начала координат — объём только у ЗАМКНУТОЙ сетки: у
            // незамкнутой она зависит от того, где начало (замер критика: сдвиг на 100 мм
            // меняет число). Рёбра сравниваются по координатам, а не по именам вершин.
            bool flipped;
            int unpaired = CountUnpairedEdges(solid, out flipped);
            if (unpaired > 0)
            {
                solid.VolumeMm3 = 0.0;
                solid.VolumeKnown = false;
                this.Refuse(solid, flipped
                    ? string.Format(CultureInfo.InvariantCulture,
                        GdmlText.T("GdmlReasonMeshFlipped", "some faces are turned inside out: {0} edge passes run the same way; volume is not defined"),
                        unpaired)
                    : string.Format(CultureInfo.InvariantCulture,
                        GdmlText.T("GdmlReasonOpenMesh", "mesh is not closed: {0} unpaired edge(s), volume is not defined"),
                        unpaired));
                return;
            }

            // Отрицательная сумма: все грани смотрят внутрь. Geant4 даёт −V и «negative cubic
            // volume» (`G4TessellatedSolid::SetSolidClosed`), модуль выдавал бы это за тело.
            if (six < 0.0)
            {
                solid.VolumeMm3 = 0.0;
                solid.VolumeKnown = false;
                this.Refuse(solid, GdmlText.T("GdmlReasonMeshInverted", "the faces are turned inside out (negative volume); volume is not defined"));
                return;
            }

            solid.VolumeMm3 = six / 6.0;
            solid.VolumeKnown = solid.VolumeMm3 > 0.0;
            if (solid.VolumeMm3 == 0.0)
            {
                this.Refuse(solid, GdmlText.T("GdmlReasonZeroVolume", "volume is zero"));
            }
        }

        /// <summary>
        /// Ключ ребра: (меньший номер, больший номер). Обычный код long — XOR половин, и у
        /// номеров одного порядка он вырождается: чтение файла GS2020 шло 1050 мс против 350.
        /// </summary>
        sealed class EdgeComparer : IEqualityComparer<long>
        {
            public bool Equals(long a, long b)
            {
                return a == b;
            }

            public int GetHashCode(long key)
            {
                unchecked
                {
                    return (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 32);
                }
            }
        }

        /// <summary>
        /// Сумма по рёбрам: сколько раз ребро пройдено в одну сторону сверх обратной.
        /// У замкнутой согласованной сетки — ноль.
        /// </summary>
        static int CountUnpairedEdges(GdmlSolid solid, out bool flipped)
        {
            int[] map = MergeVertices(solid.Vertices);
            Dictionary<long, int> balance = new Dictionary<long, int>(solid.Triangles.Count, new EdgeComparer());
            for (int i = 0; i + 2 < solid.Triangles.Count; i += 3)
            {
                int a = map[solid.Triangles[i]];
                int b = map[solid.Triangles[i + 1]];
                int c = map[solid.Triangles[i + 2]];
                AddEdge(balance, a, b);
                AddEdge(balance, b, c);
                AddEdge(balance, c, a);
            }

            int unpaired = 0;
            foreach (int value in balance.Values)
            {
                unpaired += Math.Abs(value);
            }

            // Перекос чётный на каждом ребре: края на месте, а не в ту сторону идут соседние
            // грани. Сетка замкнута, причина другая, и человеку чинить другое (`G01/axes.gdml`).
            flipped = unpaired > 0;
            foreach (int value in balance.Values)
            {
                flipped = flipped && value % 2 == 0;
            }

            return unpaired;
        }

        /// <summary>
        /// (`AMBER303`) Ячейка — в два допуска: в ячейке больше 1e-6 мм десятки тысяч несводимых вершин
        /// шли цепочкой квадратично (200000 вершин — больше 150 с); в ячейке 1e-9 мм их десяток, не больше.
        /// Сведение вершин как `G4TessellatedSolid::CreateVertexList`: новая вершина берёт
        /// номер первой из прежних, что ближе 0.5e-9 мм. Сетка ячеек только ускоряет поиск:
        /// соседние ячейки смотрятся, лишь когда точка лежит у их границы.
        /// </summary>
        static int[] MergeVertices(List<GdmlPoint> vertices)
        {
            const double Cell = 1.0e-9;
            const double Tolerance = 0.5e-9;
            const double Near = Tolerance / Cell;
            int[] map = new int[vertices.Count];
            int[] chain = new int[vertices.Count];
            Dictionary<long, int> heads = new Dictionary<long, int>(vertices.Count, new EdgeComparer());
            int[] low = new int[3];
            int[] high = new int[3];
            long[] cell = new long[3];

            for (int i = 0; i < map.Length; i++)
            {
                GdmlPoint p = vertices[i];
                for (int axis = 0; axis < 3; axis++)
                {
                    double scaled = (axis == 0 ? p.X : axis == 1 ? p.Y : p.Z) / Cell;
                    if (Math.Abs(scaled) < 9.0e15)
                    {
                        double floor = Math.Floor(scaled);
                        double fraction = scaled - floor;
                        cell[axis] = (long)floor;
                        low[axis] = fraction < Near ? -1 : 0;
                        high[axis] = fraction > 1.0 - Near ? 1 : 0;
                    }
                    else
                    {
                        // Шаг double здесь грубее допуска: сводятся только равные.
                        cell[axis] = BitConverter.DoubleToInt64Bits(scaled + 0.0);
                        low[axis] = 0;
                        high[axis] = 0;
                    }
                }

                int found = -1;
                for (int dx = low[0]; dx <= high[0] && found < 0; dx++)
                {
                    for (int dy = low[1]; dy <= high[1] && found < 0; dy++)
                    {
                        for (int dz = low[2]; dz <= high[2] && found < 0; dz++)
                        {
                            int candidate;
                            if (!heads.TryGetValue(CellKey(cell[0] + dx, cell[1] + dy, cell[2] + dz), out candidate))
                            {
                                continue;
                            }

                            for (; candidate >= 0; candidate = chain[candidate])
                            {
                                GdmlPoint q = vertices[candidate];
                                double ax = q.X - p.X, ay = q.Y - p.Y, az = q.Z - p.Z;
                                if (ax * ax + ay * ay + az * az < Tolerance * Tolerance)
                                {
                                    found = candidate;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (found >= 0)
                {
                    map[i] = map[found];
                    continue;
                }

                map[i] = i;
                long key = CellKey(cell[0], cell[1], cell[2]);
                int head;
                chain[i] = heads.TryGetValue(key, out head) ? head : -1;
                heads[key] = i;
            }

            return map;
        }

        static long CellKey(long x, long y, long z)
        {
            unchecked
            {
                ulong h = (ulong)x * 0x9E3779B97F4A7C15UL;
                h = (h ^ (h >> 29) ^ (ulong)y) * 0xBF58476D1CE4E5B9UL;
                h = (h ^ (h >> 29) ^ (ulong)z) * 0x94D049BB133111EBUL;
                return (long)(h ^ (h >> 32));
            }
        }

        static void AddEdge(Dictionary<long, int> balance, int a, int b)
        {
            if (a == b)
            {
                return;
            }

            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            int value;
            balance.TryGetValue(key, out value);
            balance[key] = value + (a < b ? 1 : -1);
        }

        void ComputeSubtraction(GdmlSolid solid, HashSet<GdmlSolid> visiting)
        {
            solid.VolumeKnown = false;
            solid.VolumeMm3 = 0.0;
            if (solid.First == null)
            {
                return;
            }

            this.ComputeSolid(solid.First, visiting);
            solid.ContainsUnknown = solid.First.Kind == GdmlSolidKind.Unknown || solid.First.ContainsUnknown;

            if (solid.First.BoundsKnown)
            {
                if (solid.FirstPlacement == null)
                {
                    CopyBounds(solid.First, solid);
                }
                else
                {
                    double[] min, max;
                    TransformBounds(solid.First, solid.FirstPlacement, out min, out max);
                    SetBounds(solid, min[0], max[0], min[1], max[1], min[2], max[2]);
                }
            }

            bool known = !solid.Unreadable;
            if (!solid.First.VolumeKnown)
            {
                known = false;
                NoteSubtraction(solid, solid.First);
            }

            double volume = solid.First.VolumeMm3;
            for (int i = 0; i < solid.Second.Count; i++)
            {
                GdmlSolid second = solid.Second[i];
                this.ComputeSolid(second, visiting);
                solid.ContainsUnknown = solid.ContainsUnknown
                                        || second.Kind == GdmlSolidKind.Unknown || second.ContainsUnknown;
                if (!second.VolumeKnown || !second.BoundsKnown)
                {
                    known = false;
                    NoteSubtraction(solid, second);
                    continue;
                }

                if (!known)
                {
                    continue;
                }

                string reason;
                double cut = IntersectionVolume(solid.First, solid.FirstPlacement, second, solid.SecondPlacements[i], out reason);
                if (double.IsNaN(cut))
                {
                    known = false;
                    solid.Note = reason;
                    this.Say("subtraction " + StripName(solid.Name), reason);
                }
                else
                {
                    volume -= cut;
                }
            }

            if (known)
            {
                solid.VolumeMm3 = Math.Max(volume, 0.0);
                solid.VolumeKnown = true;
            }
        }

        /// <summary>
        /// V(A∪B) = V(A) + V(B) − V(A∩B). Точно пересечение известно, когда габариты не
        /// встречаются (так же считает `G4UnionSolid::GetCubicVolume`), когда одно тело лежит
        /// внутри осевой коробки другого и когда оба — осевые коробки. Иначе Geant4 разыгрывает
        /// точки, а здесь оценка не выдаётся за число: «не измерено» с причиной.
        /// </summary>
        void ComputeUnion(GdmlSolid solid, HashSet<GdmlSolid> visiting)
        {
            solid.VolumeKnown = false;
            solid.VolumeMm3 = 0.0;
            if (solid.First == null)
            {
                return;
            }

            this.ComputeSolid(solid.First, visiting);
            solid.ContainsUnknown = solid.First.Kind == GdmlSolidKind.Unknown || solid.First.ContainsUnknown;
            GdmlSolid second = solid.Second.Count == 1 ? solid.Second[0] : null;
            if (second != null)
            {
                this.ComputeSolid(second, visiting);
                solid.ContainsUnknown = solid.ContainsUnknown || second.Kind == GdmlSolidKind.Unknown || second.ContainsUnknown;
            }

            if (!solid.First.VolumeKnown)
            {
                NoteSubtraction(solid, solid.First);
            }

            if (second != null && !second.VolumeKnown)
            {
                NoteSubtraction(solid, second);
            }

            if (second == null || !solid.First.BoundsKnown || !second.BoundsKnown)
            {
                return;
            }

            GdmlTransform firstPlacement = solid.FirstPlacement ?? GdmlTransform.Identity();
            GdmlTransform secondPlacement = solid.SecondPlacements[0];
            double[] amin, amax, bmin, bmax;
            TransformBounds(solid.First, firstPlacement, out amin, out amax);
            TransformBounds(second, secondPlacement, out bmin, out bmax);
            SetBounds(solid, Math.Min(amin[0], bmin[0]), Math.Max(amax[0], bmax[0]),
                             Math.Min(amin[1], bmin[1]), Math.Max(amax[1], bmax[1]),
                             Math.Min(amin[2], bmin[2]), Math.Max(amax[2], bmax[2]));

            if (solid.Unreadable || !solid.First.VolumeKnown || !second.VolumeKnown)
            {
                return;
            }

            double va = solid.First.VolumeMm3;
            double vb = second.VolumeMm3;
            bool firstBox = solid.First.Kind == GdmlSolidKind.Box && firstPlacement.IsAxisAligned;
            bool secondBox = second.Kind == GdmlSolidKind.Box && secondPlacement.IsAxisAligned;

            bool disjoint = false;
            bool secondInFirst = true;
            bool firstInSecond = true;
            for (int i = 0; i < 3; i++)
            {
                disjoint = disjoint || bmin[i] >= amax[i] || bmax[i] <= amin[i];
                secondInFirst = secondInFirst && bmin[i] >= amin[i] && bmax[i] <= amax[i];
                firstInSecond = firstInSecond && amin[i] >= bmin[i] && amax[i] <= bmax[i];
            }

            double volume;
            if (disjoint)
            {
                volume = va + vb;
            }
            else if (firstBox && secondInFirst)
            {
                volume = va;
            }
            else if (secondBox && firstInSecond)
            {
                volume = vb;
            }
            else if (firstBox && secondBox)
            {
                double dx = Math.Min(amax[0], bmax[0]) - Math.Max(amin[0], bmin[0]);
                double dy = Math.Min(amax[1], bmax[1]) - Math.Max(amin[1], bmin[1]);
                double dz = Math.Min(amax[2], bmax[2]) - Math.Max(amin[2], bmin[2]);
                volume = va + vb - dx * dy * dz;
            }
            else
            {
                solid.Note = string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonUnionOverlap", "overlap of {0} and {1} is not computed: their extents meet and they are not axis-aligned boxes"),
                    StripName(solid.First.Name), StripName(second.Name));
                this.Say("union " + StripName(solid.Name), solid.Note);
                return;
            }

            solid.VolumeMm3 = volume;
            solid.VolumeKnown = true;
        }

        static void NoteSubtraction(GdmlSolid solid, GdmlSolid part)
        {
            if (solid.Note.Length > 0)
            {
                return;
            }

            // Причина вложенного вычитания передаётся как есть: со своей приставкой на
            // каждом уровне цепочка в 200000 тел съедала память на этих строках.
            solid.Note = (part.Kind == GdmlSolidKind.Subtraction || part.Kind == GdmlSolidKind.Union) && part.Note.Length > 0
                ? part.Note
                : string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonPartNotMeasured", "body {0} is not measured: {1}"),
                    StripName(part.Name), part.Note);
        }

        /// <summary>
        /// Объём пересечения тел вычитания, в системе вычитания. Прямоугольник
        /// охвата равен телу только у осевой коробки, поэтому правило работает
        /// лишь при первом теле — коробке (`AMBER303`): кольцо с дыркой под
        /// вторым телом и прежний остаток вычитания так считались неверно.
        /// Иначе NaN и причина: объём не выдумываем.
        /// </summary>
        static double IntersectionVolume(GdmlSolid first, GdmlTransform firstPlacement,
                                         GdmlSolid second, GdmlTransform secondPlacement, out string reason)
        {
            reason = "";
            if (first.Kind != GdmlSolidKind.Box)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonSubFirst", "overlap with {0} is not computed: first body {1} is <{2}>, only a box is supported"),
                    StripName(second.Name), StripName(first.Name), first.Tag);
                return double.NaN;
            }

            if (firstPlacement != null && !firstPlacement.IsAxisAligned)
            {
                reason = string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonSubFirstRotated", "overlap with {0} is not computed: first body {1} is rotated"),
                    StripName(second.Name), StripName(first.Name));
                return double.NaN;
            }

            double[] amin = first.Min, amax = first.Max;
            if (firstPlacement != null)
            {
                TransformBounds(first, firstPlacement, out amin, out amax);
            }

            double[] bmin, bmax;
            TransformBounds(second, secondPlacement, out bmin, out bmax);

            bool disjoint = false;
            bool inside = true;
            for (int i = 0; i < 3; i++)
            {
                disjoint = disjoint || bmin[i] >= amax[i] || bmax[i] <= amin[i];
                inside = inside && bmin[i] >= amin[i] && bmax[i] <= amax[i];
            }

            if (disjoint)
            {
                return 0.0;
            }

            if (inside)
            {
                return second.VolumeMm3;
            }

            if (second.Kind == GdmlSolidKind.Box && secondPlacement.IsAxisAligned)
            {
                double dx = Math.Min(amax[0], bmax[0]) - Math.Max(amin[0], bmin[0]);
                double dy = Math.Min(amax[1], bmax[1]) - Math.Max(amin[1], bmin[1]);
                double dz = Math.Min(amax[2], bmax[2]) - Math.Max(amin[2], bmin[2]);
                return dx * dy * dz;
            }

            reason = string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlReasonSubPartial", "overlap with {0} is not computed: it only partly overlaps the box and is not an axis-aligned box"),
                StripName(second.Name));
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
                this.WarnPriority("GdmlWarnNoVolumes", "structure: no volumes");
                return;
            }

            GdmlVolumeDef world = this.FindWorld();
            if (world == null)
            {
                this.WarnPriority("GdmlWarnNoWorld", "structure: no world volume");
                return;
            }

            HashSet<GdmlVolumeDef> path = new HashSet<GdmlVolumeDef>();
            this.AddInstance(world, GdmlTransform.Identity(), world.Name, 0, null, true, path);

            if (this.limitHit)
            {
                // Первые сто тысяч из триллиона — не геометрия файла: рамка, массы и
                // чертёж по ним были бы неверны, поэтому не строится ничего.
                int built = this.Volumes.Count;
                this.Volumes.Clear();
                if (built >= MaxInstances)
                {
                    this.WarnPriority("GdmlWarnTooManyVolumes",
                        "structure: the file places more than {0} volumes (stopped at {1}); the structure is not built",
                        MaxInstances, built);
                }
                else
                {
                    // Шаги кончились раньше томов: том с множеством ссылок на цикл.
                    this.WarnPriority("GdmlWarnTooManySteps",
                        "structure: walking the placements took more than {0} steps ({1} volumes placed); the structure is not built",
                        MaxPlacementSteps, built);
                }
            }
        }

        /// <summary>
        /// Мир называет `setup/world`; «World» и первый том без ссылок на него —
        /// только запасные, и то с отдельной строкой сводки.
        /// </summary>
        GdmlVolumeDef FindWorld()
        {
            GdmlVolumeDef candidate;
            string setupWorld = this.SetupWorld();
            if (setupWorld.Length > 0)
            {
                if (this.volumeDefs.TryGetValue(setupWorld, out candidate))
                {
                    return candidate;
                }

                this.WarnPriority("GdmlWarnSetupMissing", "setup: world volume {0} not found", setupWorld);
            }

            GdmlVolumeDef world;
            if (!this.volumeDefs.TryGetValue("World", out world))
            {
                HashSet<string> referenced = new HashSet<string>(StringComparer.Ordinal);
                foreach (GdmlVolumeDef definition in this.volumeDefs.Values)
                {
                    foreach (GdmlChild child in definition.Children)
                    {
                        referenced.Add(child.VolumeRef);
                    }
                }

                foreach (GdmlVolumeDef definition in this.volumeOrder)
                {
                    if (!referenced.Contains(definition.Name))
                    {
                        world = definition;
                        break;
                    }
                }
            }

            if (world != null)
            {
                this.WarnPriority("GdmlWarnWorldFallback", "setup: no valid <world>; volume {0} is taken as the world", StripName(world.Name));
            }

            return world;
        }

        /// <summary>Вернуть false, если том не размещён (цикл, глубина, предел числа).</summary>
        bool AddInstance(GdmlVolumeDef definition, GdmlTransform transform, string instanceName, int depth,
                         GdmlVolumeInstance parent, bool placementKnown, HashSet<GdmlVolumeDef> path)
        {
            if (this.limitHit)
            {
                return false;
            }

            if (this.Volumes.Count >= MaxInstances || ++this.placementSteps > MaxPlacementSteps)
            {
                this.limitHit = true;
                return false;
            }

            if (path.Contains(definition))
            {
                this.Warn("GdmlWarnVolumeCycle", "structure: volume {0} contains itself (placement {1}); the copy is not placed",
                    StripName(definition.Name), instanceName);
                return false;
            }

            if (depth > MaxVolumeDepth)
            {
                this.Warn("GdmlWarnVolumeDeep", "structure: volume {0} is nested deeper than {1} levels and is not placed",
                    StripName(definition.Name), MaxVolumeDepth);
                return false;
            }

            path.Add(definition);

            GdmlVolumeInstance instance = new GdmlVolumeInstance();
            instance.VolumeName = StripName(definition.Name);
            instance.InstanceName = StripName(instanceName);
            instance.MaterialName = definition.MaterialRef;
            instance.SolidName = definition.SolidRef;
            instance.Transform = transform;
            instance.Depth = depth;
            instance.Children = definition.Children.Count;
            instance.Parent = parent;
            instance.PlacementKnown = placementKnown;
            instance.UnknownContent = definition.UnknownContent;
            if (!definition.UnreadCollected)
            {
                definition.UnreadCollected = true;
                HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
                foreach (string reference in definition.UnreadRefs)
                {
                    this.CollectUncounted(reference, definition.UnknownContent, visited);
                }
            }

            GdmlSolid solid;
            if (this.solids.TryGetValue(definition.SolidRef, out solid))
            {
                instance.Solid = solid;
            }
            else if (definition.SolidRef.Length > 0)
            {
                this.Warn("GdmlWarnVolumeNoSolid", "volume {0}: solid {1} not found", StripName(definition.Name), StripName(definition.SolidRef));
            }

            instance.Material = this.ResolveMaterial(definition.MaterialRef, new HashSet<string>(StringComparer.Ordinal));

            if (instance.Solid != null)
            {
                if (instance.Solid.VolumeKnown)
                {
                    instance.VolumeCm3 = instance.Solid.VolumeCm3;
                }

                if (instance.Solid.BoundsKnown && placementKnown)
                {
                    double[] min, max;
                    TransformBounds(instance.Solid, transform, out min, out max);
                    if (AllFinite(min) && AllFinite(max))
                    {
                        instance.Min = min;
                        instance.Max = max;
                    }
                    else
                    {
                        instance.PlacementKnown = false;
                        this.Warn("GdmlWarnBoundsOverflow", "volume {0}: bounds are out of range and are not measured", StripName(definition.Name));
                    }
                }

                if (depth > 0)
                {
                    this.SetMass(instance, definition);
                }
            }

            if (depth > 0)
            {
                this.Volumes.Add(instance);
            }

            bool complete = true;
            foreach (GdmlChild child in definition.Children)
            {
                if (child.Def == null)
                {
                    continue;
                }

                GdmlTransform childTransform = transform.Compose(child.Placement);
                complete &= this.AddInstance(child.Def, childTransform, child.Name, depth + 1, instance,
                                             instance.PlacementKnown && child.PlacementKnown, path);
            }

            path.Remove(definition);

            if (!complete && !double.IsNaN(instance.MassG))
            {
                instance.MassG = double.NaN;
                instance.MassNote = GdmlText.T("GdmlNoteMassNotPlaced", "mass is not computed: not all children are placed");
            }

            return true;
        }

        /// <summary>
        /// Вещества тома, копии которого не размещены, и всего, что лежит в нём. Каждый том — один раз (`visited`): общий
        /// подграф и цикл не обходятся дважды, развёртка DAG в 2^40 путей здесь не повторяется.
        /// </summary>
        void CollectUncounted(string reference, string reason, HashSet<string> visited)
        {
            // Явный стек, а не рекурсия: цепочка в сотню тысяч томов не должна переполнить стек потока окна (`AMBER292`).
            Stack<string> pending = new Stack<string>();
            pending.Push(reference);
            while (pending.Count > 0)
            {
                string name = pending.Pop();
                GdmlVolumeDef definition;
                if (string.IsNullOrEmpty(name) || !visited.Add(name) || !this.volumeDefs.TryGetValue(name, out definition))
                {
                    continue;
                }

                if (definition.MaterialRef.Length > 0 && !this.UncountedMaterials.ContainsKey(definition.MaterialRef))
                {
                    this.UncountedMaterials[definition.MaterialRef] = reason;
                    this.uncountedMaterialOf[definition.MaterialRef] = this.ResolveMaterial(definition.MaterialRef, new HashSet<string>(StringComparer.Ordinal));
                }

                foreach (GdmlChild child in definition.Children)
                {
                    pending.Push(child.VolumeRef);
                }

                foreach (string inner in definition.UnreadRefs)
                {
                    pending.Push(inner);
                }
            }
        }

        /// <summary>
        /// Масса как `G4LogicalVolume::GetMass`: своё вещество занимает объём тела за
        /// вычетом ПРЯМЫХ детей (массы детей считаются у их собственных строк). Иначе
        /// сосуд с водой внутри получал массу стенки вместе с водой (×5.45…×7.94).
        /// </summary>
        void SetMass(GdmlVolumeInstance instance, GdmlVolumeDef definition)
        {
            if (!instance.Solid.VolumeKnown)
            {
                return;
            }

            this.MeasureChildren(definition);
            if (!definition.ChildVolumeKnown)
            {
                instance.MassNote = string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlNoteMassChild", "mass is not computed: volume of {0} inside it is unknown"),
                    definition.ChildNote);
                return;
            }

            double own = instance.Solid.VolumeMm3 - definition.ChildVolumeMm3;
            if (own < 0.0)
            {
                if (-own > 1e-9 * instance.Solid.VolumeMm3)
                {
                    // Суммой объёмов не отличить перекрытие от ребёнка снаружи матери; Geant4 и
                    // того не проверяет. Близкие числа печатаются длиннее: «1 см3 из 1 см3» не довод.
                    double childrenCm3 = definition.ChildVolumeMm3 / 1000.0;
                    string childrenText = GdmlText.N(childrenCm3);
                    string ownText = GdmlText.N(instance.Solid.VolumeCm3);
                    if (childrenText == ownText)
                    {
                        childrenText = childrenCm3.ToString("G15", CultureInfo.InvariantCulture);
                        ownText = instance.Solid.VolumeCm3.ToString("G15", CultureInfo.InvariantCulture);
                    }

                    this.Warn("GdmlWarnChildrenNoFit",
                        "volume {0}: children take {1} cm3 of its {2} cm3: in the file they overlap it or stick out of it, mass is not computed",
                        StripName(definition.Name), childrenText, ownText);
                    instance.MassNote = GdmlText.T("GdmlNoteMassNoFit", "mass is not computed: children do not fit in it");
                    return;
                }

                own = 0.0;
            }

            instance.OwnVolumeCm3 = own / 1000.0;
            if (instance.Material != null && instance.Material.HasDensity)
            {
                instance.MassG = instance.OwnVolumeCm3 * instance.Material.Density;
            }
        }

        void MeasureChildren(GdmlVolumeDef definition)
        {
            if (definition.ChildrenMeasured)
            {
                return;
            }

            definition.ChildrenMeasured = true;
            double sum = 0.0;
            string unknown = definition.UnknownContent;
            foreach (GdmlChild child in definition.Children)
            {
                if (child.Def == null)
                {
                    continue;
                }

                GdmlSolid solid;
                if (this.solids.TryGetValue(child.Def.SolidRef, out solid) && solid.VolumeKnown)
                {
                    sum += solid.VolumeMm3;
                }
                else if (unknown.Length == 0)
                {
                    unknown = StripName(child.Def.Name);
                }
            }

            definition.ChildVolumeMm3 = sum;
            definition.ChildVolumeKnown = unknown.Length == 0;
            definition.ChildNote = unknown;
        }

        static void TransformBounds(GdmlSolid solid, GdmlTransform transform, out double[] min, out double[] max)
        {
            if (RevolutionBounds(solid, transform, out min, out max))
            {
                return;
            }

            int budget = MaxBooleanBoundsParts;
            if (BooleanBounds(solid, transform, ref budget, out min, out max))
            {
                return;
            }

            CornerBounds(solid, transform, out min, out max);
        }

        /// <summary>
        /// Частей булева тела, которые точный габарит обходит, не больше: L(i) = L(i−1) ∪ L(i−1) разворачивается в 2^i частей,
        /// а настоящие тела — единицы частей. За пределом — углы локальной коробки, как прежде (шире, но конечно и быстро).
        /// </summary>
        const int MaxBooleanBoundsParts = 64;

        /// <summary>
        /// (fixGdml2, П1б) Габарит булева тела по его частям, каждая — в своём положении: вычитание не шире первого тела,
        /// объединение — сумма габаритов частей. Углы локальной коробки повёрнутого вычитания из труб раздували его так же, как
        /// трубу (`lht.gdml`: оболочка мишени ⌀300.4 — 423.2 мм в рамке).
        /// </summary>
        static bool BooleanBounds(GdmlSolid solid, GdmlTransform transform, ref int budget, out double[] min, out double[] max)
        {
            min = null;
            max = null;
            if (solid == null || solid.First == null || --budget < 0
                || (solid.Kind != GdmlSolidKind.Subtraction && solid.Kind != GdmlSolidKind.Union))
            {
                return false;
            }

            if (!PartBounds(solid.First, solid.FirstPlacement == null ? transform : transform.Compose(solid.FirstPlacement), ref budget, out min, out max))
            {
                return false;
            }

            if (solid.Kind == GdmlSolidKind.Subtraction)
            {
                return true;
            }

            for (int i = 0; i < solid.Second.Count; i++)
            {
                GdmlTransform at = i < solid.SecondPlacements.Count && solid.SecondPlacements[i] != null
                    ? transform.Compose(solid.SecondPlacements[i])
                    : transform;
                double[] low, high;
                if (solid.Second[i] == null || !PartBounds(solid.Second[i], at, ref budget, out low, out high))
                {
                    return false;
                }

                for (int k = 0; k < 3; k++)
                {
                    min[k] = Math.Min(min[k], low[k]);
                    max[k] = Math.Max(max[k], high[k]);
                }
            }

            return true;
        }

        static bool PartBounds(GdmlSolid part, GdmlTransform transform, ref int budget, out double[] min, out double[] max)
        {
            if (part == null || !part.BoundsKnown)
            {
                min = null;
                max = null;
                return false;
            }

            if (RevolutionBounds(part, transform, out min, out max) || BooleanBounds(part, transform, ref budget, out min, out max))
            {
                return true;
            }

            if (part.Kind == GdmlSolidKind.Subtraction || part.Kind == GdmlSolidKind.Union)
            {
                return false;
            }

            CornerBounds(part, transform, out min, out max);
            return true;
        }

        static void CornerBounds(GdmlSolid solid, GdmlTransform transform, out double[] min, out double[] max)
        {
            // Сетка: габарит её вершин в положении — точный и для повёрнутой сетки; линейно по вершинам.
            // Те же вершины, что у локального габарита (`ComputeTessellated`): только те, на которые ссылаются треугольники.
            if (solid.Kind == GdmlSolidKind.Tessellated && solid.Triangles.Count > 0)
            {
                min = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
                max = new[] { double.MinValue, double.MinValue, double.MinValue };
                foreach (int index in solid.Triangles)
                {
                    GdmlPoint world = transform.Apply(solid.Vertices[index]);
                    min[0] = Math.Min(min[0], world.X);
                    min[1] = Math.Min(min[1], world.Y);
                    min[2] = Math.Min(min[2], world.Z);
                    max[0] = Math.Max(max[0], world.X);
                    max[1] = Math.Max(max[1], world.Y);
                    max[2] = Math.Max(max[2], world.Z);
                }

                return;
            }

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

        /// <summary>
        /// (`AMBER301`, fixGdml2, П1б по критику `critGdml2`) Точный габарит тела вращения в положении
        /// <paramref name="transform"/>. Восемь углов локальной коробки давали коробку повёрнутой коробки: поворот трубы вокруг
        /// СВОЕЙ оси раздувал габарит до 2R·(|cos φ| + |sin φ|) — на `lht.gdml` кольцо ⌀324 занимало в рамке 456.5 мм, мишень
        /// ⌀100 — 140.9 мм, хотя кольцо от поворота вокруг своей оси не меняется вовсе.
        ///
        /// Точка профиля (r, z) — окружность радиуса r на высоте z оси тела. По мировой оси i её координата
        /// o_i + z·a_i + r·(x_i cos φ + y_i sin φ) = o_i + z·a_i + r·s_i·cos(φ − θ_i), где a, x, y — образы локальных осей,
        /// s_i = √(x_i² + y_i²) = √(1 − a_i²), θ_i = atan2(y_i, x_i). Полный оборот: cos пробегает [−1, 1], то есть
        /// |a_i|·L/2 + R·√(1 − a_i²) у цилиндра. Неполный: cos берётся на дуге [startphi, startphi + deltaphi] — концы дуги и
        /// θ_i, θ_i + π, если они на ней. Выражение линейно по (r, z), поэтому экстремум по сечению тела — в вершине профиля:
        /// у polycone это (rmin, z) и (rmax, z) каждой плоскости (rmin нужен неполному обороту и знаку cos &lt; 0), у
        /// genericPolycone — вершины его контура. Конус — polycone из двух плоскостей, больший радиус берётся сам.
        /// </summary>
        static bool RevolutionBounds(GdmlSolid solid, GdmlTransform transform, out double[] min, out double[] max)
        {
            min = null;
            max = null;
            if (solid == null || transform == null
                || (solid.Kind != GdmlSolidKind.Polycone && solid.Kind != GdmlSolidKind.GenericPolycone))
            {
                return false;
            }

            // Без поворота (оси вдоль осей, в том числе зеркало) углы локальной коробки и так точны: читатель строит её
            // точно (`SetRevolutionBounds`, сектор неполного оборота). Формула ниже дала бы те же числа с шумом в 1e-15, а
            // ноль в 6e-16 уводит плоскость разреза с нуля на середину габарита (`o02_partial_phi`, `a_cone_hole_phi`).
            if (transform.IsAxisAligned && solid.BoundsKnown)
            {
                return false;
            }

            List<double[]> points = new List<double[]>();
            if (solid.Kind == GdmlSolidKind.Polycone)
            {
                foreach (double[] plane in solid.Planes)
                {
                    points.Add(new[] { plane[1], plane[0] });
                    points.Add(new[] { plane[2], plane[0] });
                }
            }
            else
            {
                foreach (double[] point in solid.Profile)
                {
                    points.Add(new[] { point[0], point[1] });
                }
            }

            if (points.Count == 0)
            {
                return false;
            }

            GdmlPoint origin = transform.Apply(new GdmlPoint(0.0, 0.0, 0.0));
            GdmlPoint ex = transform.Apply(new GdmlPoint(1.0, 0.0, 0.0));
            GdmlPoint ey = transform.Apply(new GdmlPoint(0.0, 1.0, 0.0));
            GdmlPoint ez = transform.Apply(new GdmlPoint(0.0, 0.0, 1.0));
            double[] o = { origin.X, origin.Y, origin.Z };
            double[] x = { ex.X - origin.X, ex.Y - origin.Y, ex.Z - origin.Z };
            double[] y = { ey.X - origin.X, ey.Y - origin.Y, ey.Z - origin.Z };
            double[] a = { ez.X - origin.X, ez.Y - origin.Y, ez.Z - origin.Z };

            double start = solid.StartPhi, delta = solid.DeltaPhi;
            if (delta < 0.0)
            {
                start += delta;
                delta = -delta;
            }

            bool full = delta >= 360.0 || Math.Abs(delta - 360.0) <= PhiTolerance;
            min = new double[3];
            max = new double[3];
            for (int i = 0; i < 3; i++)
            {
                double s = Math.Sqrt(x[i] * x[i] + y[i] * y[i]);
                double cmax = 1.0, cmin = -1.0;
                if (!full && s > 0.0)
                {
                    double theta = Math.Atan2(y[i], x[i]) * 180.0 / Math.PI;
                    double c1 = Math.Cos((start - theta) * Math.PI / 180.0);
                    double c2 = Math.Cos((start + delta - theta) * Math.PI / 180.0);
                    cmax = Math.Max(c1, c2);
                    cmin = Math.Min(c1, c2);
                    if (OnArc(theta, start, delta))
                    {
                        cmax = 1.0;
                    }

                    if (OnArc(theta + 180.0, start, delta))
                    {
                        cmin = -1.0;
                    }
                }

                double low = double.MaxValue, high = double.MinValue;
                foreach (double[] p in points)
                {
                    double along = o[i] + p[1] * a[i];
                    low = Math.Min(low, along + p[0] * s * cmin);
                    high = Math.Max(high, along + p[0] * s * cmax);
                }

                min[i] = low;
                max[i] = high;
            }

            return true;
        }

        /// <summary>Угол на дуге [start, start + delta], градусы, с любым числом оборотов.</summary>
        static bool OnArc(double angle, double start, double delta)
        {
            double offset = (angle - start) % 360.0;
            if (offset < 0.0)
            {
                offset += 360.0;
            }

            return offset <= delta;
        }

        static bool AllFinite(double[] values)
        {
            foreach (double value in values)
            {
                if (IsBad(value))
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Меры для сводки и чертежа
        // ------------------------------------------------------------------

        /// <summary>
        /// Тело не поддержано: тела нет, оно незнакомо или в его составе есть незнакомое. Одна мерка на сводку
        /// («неподдержанных тел») и чертёж (подпись «не нарисовано — неподдержанное тело»), деталями, а не именами.
        /// </summary>
        public static bool IsUnsupported(GdmlVolumeInstance volume)
        {
            return volume == null || volume.Solid == null || volume.Solid.Kind == GdmlSolidKind.Unknown || volume.Solid.ContainsUnknown;
        }

        /// <summary>Тело поддержано, а положение или габарит не прочитаны: где рисовать, неизвестно. Мерка сводки и чертежа.</summary>
        public static bool IsUnplaced(GdmlVolumeInstance volume)
        {
            return !IsUnsupported(volume) && (volume.Transform == null || !volume.PlacementKnown || !volume.BoundsKnown);
        }

        void Measure()
        {
            HashSet<string> materialNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> unknownMaterialNames = new HashSet<string>(StringComparer.Ordinal);
            this.UnknownSolids = 0;
            this.UnplacedVolumes = 0;

            this.FrameMin = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
            this.FrameMax = new[] { double.MinValue, double.MinValue, double.MinValue };
            bool anyBounds = false;

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

                if (IsUnsupported(volume))
                {
                    this.UnknownSolids++;
                }
                else if (IsUnplaced(volume))
                {
                    this.UnplacedVolumes++;
                }

                if (volume.BoundsKnown)
                {
                    anyBounds = true;
                    for (int i = 0; i < 3; i++)
                    {
                        this.FrameMin[i] = Math.Min(this.FrameMin[i], volume.Min[i]);
                        this.FrameMax[i] = Math.Max(this.FrameMax[i], volume.Max[i]);
                    }
                }
            }

            if (!anyBounds)
            {
                this.FrameMin = new[] { 0.0, 0.0, 0.0 };
                this.FrameMax = new[] { 0.0, 0.0, 0.0 };
            }

            this.MaterialCount = materialNames.Count;
            this.UnknownMaterials = unknownMaterialNames.Count;
            this.Materials.Clear();
            this.Materials.AddRange(materialNames);

            if (this.Volumes.Count == 0)
            {
                return;
            }

            this.Axis = anyBounds ? this.DetectAxis() : 'z';
            this.FindCrystal();
            this.EndPlane = this.CrystalCenter(this.Axis);
        }

        /// <summary>
        /// Ось прибора. Есть тела вращения — образ локальной z у самого большого из них: у
        /// повёрнутого полиякона ось не z. Иначе — длинная сторона рамки.
        /// </summary>
        char DetectAxis()
        {
            GdmlVolumeInstance largest = null;
            foreach (GdmlVolumeInstance volume in this.Volumes)
            {
                if (volume.Solid != null && volume.PlacementKnown && volume.Transform != null
                    && (volume.Solid.Kind == GdmlSolidKind.Polycone
                        || volume.Solid.Kind == GdmlSolidKind.GenericPolycone)
                    && (largest == null || volume.VolumeCm3 > largest.VolumeCm3))
                {
                    largest = volume;
                }
            }

            if (largest != null)
            {
                GdmlPoint axis = largest.Transform.ImageOfLocalZ;
                double ax = Math.Abs(axis.X), ay = Math.Abs(axis.Y), az = Math.Abs(axis.Z);
                if (az >= ax && az >= ay)
                {
                    return 'z';
                }

                return ax >= ay ? 'x' : 'y';
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

            // (fixGdml2, третий заход) Имя с «crystal» — ещё не кристалл: у `lht.gdml` первым идёт `vCrystalRow`, воздушный
            // ряд репликации, а кристаллы CsI — внутри него. Газ кристаллом не бывает; из названных прежде берётся вещество
            // детектора (тот же признак, что `LargestDetectorVolume`), затем любое не газ.
            string named = "";
            foreach (GdmlVolumeInstance volume in this.Volumes)
            {
                string name = volume.VolumeName ?? "";
                bool gas = volume.Material != null && volume.Material.HasDensity && volume.Material.Density < 0.01;
                if (gas || (name.IndexOf("crystal", StringComparison.OrdinalIgnoreCase) < 0
                            && name.IndexOf("кристалл", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                if (LooksLikeDetector(volume.Material))
                {
                    this.CrystalName = name;
                    return;
                }

                if (named.Length == 0)
                {
                    named = name;
                }
            }

            if (named.Length > 0)
            {
                this.CrystalName = named;
                return;
            }

            // (`AMBER317`) Имя молчит, говорит вещество: `NaI` из NaI. Тот же признак, что у ролей сводки: вид с торца
            // и подпись «через центр кристалла» не расходятся с тем, что сводка называет кристаллом.
            GdmlVolumeInstance detector = this.LargestDetectorVolume();
            if (detector != null)
            {
                this.CrystalName = detector.VolumeName ?? "";
            }
        }

        /// <summary>Самый большой том из вещества детектора; единственный такой признак чтения и ролей.</summary>
        internal GdmlVolumeInstance LargestDetectorVolume()
        {
            GdmlVolumeInstance found = null;
            foreach (GdmlVolumeInstance volume in this.Volumes)
            {
                // Проба йодированной соли или сосуд с NaI крупнее кристалла: вещество то же, но это не кристалл.
                if (volume != null && volume.Solid != null && LooksLikeDetector(volume.Material)
                    && !NameHasWord(volume.VolumeName, SampleWords) && !NameHasWord(volume.VolumeName, VesselWords)
                    && (found == null || volume.VolumeCm3 > found.VolumeCm3))
                {
                    found = volume;
                }
            }

            return found;
        }

        /// <summary>Слова пробы, сосуда и защиты в именах томов: одни на чтение и роли сводки.</summary>
        internal static readonly string[] SampleWords = { "sample", "source", "specimen", "проба", "образец" };
        internal static readonly string[] VesselWords = { "vessel", "beaker", "marinelli", "сосуд", "стакан", "кювета" };
        internal static readonly string[] ShieldWords = { "shield", "castle", "cave", "liner", "lead", "защита", "домик" };

        /// <summary>
        /// Есть ли в имени такое слово ЦЕЛИКОМ. Слова — по разделителям и по стыку строчной и прописной:
        /// `LV_SourceMatrix` — source и matrix, `LV_Opensource_shell` — opensource и shell.
        /// Подстрока давала пробу из «LV_Opensource_shell».
        /// </summary>
        internal static bool NameHasWord(string name, string[] words)
        {
            name = name ?? "";
            int start = 0;
            for (int i = 0; i <= name.Length; i++)
            {
                bool end = i == name.Length || !char.IsLetter(name[i]);
                bool hump = !end && i > start && char.IsUpper(name[i]) && char.IsLower(name[i - 1]);
                if (end || hump)
                {
                    if (i > start)
                    {
                        string word = name.Substring(start, i - start);
                        foreach (string candidate in words)
                        {
                            if (string.Equals(word, candidate, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }

                    start = end ? i + 1 : i;
                }
            }

            return false;
        }

        /// <summary>Сцинтиллятор или полупроводник по составу.</summary>
        internal static bool LooksLikeDetector(GdmlMaterial material)
        {
            if (material == null || material.Fractions.Count == 0)
            {
                return false;
            }

            Func<int, bool> has = z => material.Fractions.ContainsKey(z);
            return (has(11) && has(53)) || (has(55) && has(53)) || (has(57) && has(35)) || (has(58) && has(35))
                   || (has(83) && has(32) && has(8)) || (has(71) && has(14) && has(8))
                   || (has(64) && has(31) && has(13)) || (has(48) && has(52))
                   || (material.Fractions.Count == 1 && has(32));
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
                    || !volume.Solid.VolumeKnown || !volume.PlacementKnown)
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

            string unitMm = GdmlText.T("GdmlUnitMm", "mm");
            string unitCm3 = GdmlText.T("GdmlUnitCm3", "cm3");
            string unitG = GdmlText.T("GdmlUnitG", "g");
            string dimensions = volume.BoundsKnown
                ? string.Format(CultureInfo.InvariantCulture, "{0} x {1} x {2} {3}",
                    GdmlText.N(volume.Max[0] - volume.Min[0]),
                    GdmlText.N(volume.Max[1] - volume.Min[1]),
                    GdmlText.N(volume.Max[2] - volume.Min[2]),
                    unitMm)
                : "? " + unitMm;
            string volumeText = volume.VolumeCm3 >= 0.0
                ? GdmlText.N(volume.VolumeCm3) + " " + unitCm3
                : "? " + unitCm3;
            string massText = !double.IsNaN(volume.MassG)
                ? GdmlText.N(volume.MassG) + " " + unitG
                : "? " + unitG;

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
            else if (volume.Solid.Kind == GdmlSolidKind.Union)
            {
                extra.Append(GdmlText.T("GdmlSummaryUnion", "; union"));
            }

            if (volume.Children > 0)
            {
                extra.AppendFormat(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryContainer", "; container of {0}"), volume.Children);
            }

            if (volume.Children > 0 && volume.OwnVolumeCm3 >= 0.0)
            {
                extra.AppendFormat(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryOwnVolume", "; own volume {0} cm3"), GdmlText.N(volume.OwnVolumeCm3));
            }

            if (!volume.PlacementKnown)
            {
                extra.Append("; ").Append(GdmlText.T("GdmlSummaryNoPlacement", "placement is not read"));
            }

            if (volume.MassNote.Length > 0)
            {
                extra.Append("; ").Append(volume.MassNote);
            }

            if (!volume.Solid.VolumeKnown && volume.Solid.Note.Length > 0)
            {
                extra.Append("; ").Append(volume.Solid.Note);
            }

            // Числа — раньше состава: состав длинный, и размеры, объём и масса иначе уходили
            // на вторую и третью строку при переносе по словам.
            return string.Format(CultureInfo.InvariantCulture,
                "{0}  {1}  {2}  {3}  [{4}]{5}",
                volume.VolumeName, dimensions, volumeText, massText,
                volume.MaterialDescription(), extra);
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
                // Своё, без прямых детей: иначе вложенные тома сложились бы дважды.
                if (item.OwnVolumeCm3 >= 0.0)
                {
                    volume += item.OwnVolumeCm3;
                }
                else if (item.VolumeCm3 >= 0.0)
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

        /// <summary>Строка сводки; после предела строки считаются, а не копятся.</summary>
        void Warn(string key, string fallback, params object[] args)
        {
            if (this.Warnings.Count >= MaxWarnings)
            {
                this.warningsDropped++;
                return;
            }

            string text = args.Length == 0
                ? GdmlText.T(key, fallback)
                : string.Format(CultureInfo.InvariantCulture, GdmlText.T(key, fallback), args);
            if (this.warningsSeen.Add(text))
            {
                this.Warnings.Add(text);
            }
        }

        /// <summary>
        /// Строка, которую нельзя терять при переполненном списке: без неё пустая структура
        /// или подменённый мир остались бы без объяснения.
        /// </summary>
        void WarnPriority(string key, string fallback, params object[] args)
        {
            string text = args.Length == 0
                ? GdmlText.T(key, fallback)
                : string.Format(CultureInfo.InvariantCulture, GdmlText.T(key, fallback), args);
            if (this.warningsSeen.Add(text))
            {
                this.Warnings.Add(text);
            }
        }

        void Say(string owner, string reason)
        {
            if (this.Warnings.Count >= MaxWarnings)
            {
                this.warningsDropped++;
                return;
            }

            string text = owner + ": " + reason;
            if (this.warningsSeen.Add(text))
            {
                this.Warnings.Add(text);
            }
        }

        static string Clip(string text)
        {
            if (text == null)
            {
                return "";
            }

            return text.Length <= 60 ? text : text.Substring(0, 60) + "...";
        }

        static string ReasonNumber(string attribute, string text)
        {
            return string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlReasonNumber", "{0} = \"{1}\" is not a finite number or a known expression"),
                attribute, Clip(text));
        }

        static string ReasonMissing(string attribute)
        {
            return string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlReasonMissing", "{0} is missing"), attribute);
        }

        static string ReasonUnit(string unit)
        {
            return string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlReasonUnit", "unknown unit \"{0}\""), Clip(unit));
        }

        static string ReasonTag(string tag)
        {
            return string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlReasonTag", "unsupported <{0}>"), tag);
        }

        static bool IsBad(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value);
        }

        /// <summary>
        /// Положение: по схеме `gdml_define.xsd` x, y, z необязательны и равны 0;
        /// присутствующее, но нечитаемое значение — отказ всему вектору со строкой.
        /// </summary>
        bool TryVector(XElement element, string owner, out double x, out double y, out double z)
        {
            string unit = Attr(element, "unit");
            double scale;
            bool ok = TryLengthScale(unit, out scale);
            if (!ok)
            {
                this.Say(owner, ReasonUnit(unit));
            }

            ok &= this.Component(element, "x", owner, scale, out x);
            ok &= this.Component(element, "y", owner, scale, out y);
            ok &= this.Component(element, "z", owner, scale, out z);
            return ok;
        }

        /// <summary>Углы в градусах; единица по умолчанию — радиан, как в `RotationRead`.</summary>
        bool TryAngles(XElement element, string owner, out double x, out double y, out double z)
        {
            string unit = Attr(element, "unit");
            double scale;
            bool ok = TryAngleScale(unit, out scale);
            if (!ok)
            {
                this.Say(owner, ReasonUnit(unit));
            }

            ok &= this.Component(element, "x", owner, scale, out x);
            ok &= this.Component(element, "y", owner, scale, out y);
            ok &= this.Component(element, "z", owner, scale, out z);

            // За 2^63 Math.Cos в .NET Framework возвращает сам аргумент: матрица из таких
            // «косинусов» не поворот, а масштаб 1e299. Смысла такой угол не несёт и раньше.
            if (Math.Abs(x) > MaxAngleDegrees || Math.Abs(y) > MaxAngleDegrees || Math.Abs(z) > MaxAngleDegrees)
            {
                this.Say(owner, string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlReasonAngleRange", "angle {0} {1} {2} deg is out of range"),
                    GdmlText.N(x), GdmlText.N(y), GdmlText.N(z)));
                x = y = z = 0.0;
                return false;
            }

            return ok;
        }

        bool Component(XElement element, string attribute, string owner, double scale, out double value)
        {
            string text = Attr(element, attribute);
            value = 0.0;
            if (text.Length == 0)
            {
                return true;
            }

            if (this.TryNumber(text, out value) && !IsBad(value * scale))
            {
                value *= scale;
                return true;
            }

            value = 0.0;
            this.Say(owner, ReasonNumber(attribute, text));
            return false;
        }

        /// <summary>
        /// Число или простое выражение. NaN, бесконечность и то, что не умещается в
        /// double, — отказ: дальше по коду они становились нулём или ломали чертёж.
        /// </summary>
        bool TryNumber(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            double parsed;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                if (IsBad(parsed))
                {
                    return false;
                }

                value = parsed;
                return true;
            }

            return new GdmlExpression(text, this.constants, this.poisonedConstants, this.assumedConstants).TryEvaluate(out value);
        }

        /// <summary>
        /// Вычислитель `+ - * / ( )` над числами, константами файла, <c>pi</c> и единицами.
        /// Различаются две вещи. Значения: что можно написать в выражении. Занятые имена:
        /// что файл не вправе определить, потому что у Geant4 это имя уже есть (`setStdMath.cc`,
        /// `setSystemOfUnits.cc`). <c>PI</c>, <c>TWOPI</c>, <c>HALFPI</c> у Geant4 нет: файлы
        /// определяют их сами (107 из 144 примеров), и такое значение главнее здешнего. Без
        /// определения Geant4 отказывает, а здесь значение берётся со строкой. Значения единиц — в системе
        /// CLHEP: мм и радиан, поэтому <c>2*cm</c> = 20 и <c>360*deg</c> = 2π, ровно
        /// как у `G4GDMLEvaluator`. Функции набора Geant4 (`sin`, `sqrt`, `pow`…) — с 07.11.2026, см.
        /// <see cref="Functions"/>; степень `^` не поддержана: такой текст — отказ со строкой, а не ноль. Деление на 0 и неконечный промежуточный результат —
        /// тоже отказ: `1/(1/0)` иначе становилось нулём (`Evaluator.cc:328` делит с проверкой).
        /// </summary>
        sealed class GdmlExpression
        {
            const int MaxDepth = 64;

            static readonly Dictionary<string, double> Builtin = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                { "pi", Math.PI },

                // (fixGdml2, третий заход) Остальные константы `setStdMath` CLHEP (Geant4 11.4.2, `setStdMath.cc:34`):
                // число Эйлера и постоянная Эйлера — Маскерони, теми же цифрами.
                { "e", 2.7182818284590452354 }, { "gamma", 0.577215664901532861 },
                { "mm", 1.0 }, { "millimeter", 1.0 }, { "cm", 10.0 }, { "centimeter", 10.0 },
                { "m", 1000.0 }, { "meter", 1000.0 }, { "km", 1.0e6 }, { "kilometer", 1.0e6 },
                { "um", 1.0e-3 }, { "micrometer", 1.0e-3 }, { "nm", 1.0e-6 }, { "nanometer", 1.0e-6 },
                { "Ang", 1.0e-7 }, { "angstrom", 1.0e-7 }, { "fm", 1.0e-12 }, { "fermi", 1.0e-12 },
                { "rad", 1.0 }, { "radian", 1.0 }, { "mrad", 1.0e-3 }, { "milliradian", 1.0e-3 },
                { "deg", Math.PI / 180.0 }, { "degree", Math.PI / 180.0 },
            };

            /// <summary>Не определены в Geant4; берутся только при отсутствии в файле и со строкой.</summary>
            static readonly Dictionary<string, double> Assumed = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                { "PI", Math.PI }, { "TWOPI", 2.0 * Math.PI }, { "HALFPI", Math.PI / 2.0 },
            };

            /// <summary>Имена `setStdMath` и `setSystemOfUnits`: определение такого — отказ файлу у Geant4.</summary>
            static readonly HashSet<string> Taken = new HashSet<string>(StringComparer.Ordinal)
            {
                "A", "Bq", "C", "Ci", "F", "GBq", "GJ", "GW", "GeV", "Gs", "Gy", "H", "Hz", "J", "K", "L", "MBq", "MHz", "MJ", "MV", "MW", "MeV",
                "N", "Pa", "PeV", "S", "Sv", "T", "TeV", "V", "W", "Wb", "amp", "ampere", "angstrom", "atm", "atmosphere", "bar", "barn",
                "becquerel", "cL", "candela", "cc", "cd", "centiliter", "centimeter", "cm", "cm2", "cm3", "coulomb", "curie", "day", "decimeter",
                "deg", "degree", "diopter", "dioptre", "dpt", "e", "eV", "electronvolt", "farad", "fermi", "g", "gamma", "gauss", "gigabecquerel",
                "gigaelectronvolt", "gigajoule", "gigawatt", "gram", "gray", "henry", "hertz", "hour", "joule", "kBq", "kGs", "kHz", "kJ", "kN", "kV",
                "kW", "kbar", "keV", "kelvin", "kg", "kilobar", "kilobecquerel", "kiloelectronvolt", "kilogauss", "kilogram", "kilogray", "kilohertz",
                "kilojoule", "kilometer", "kilonewton", "kilovolt", "kilowatt", "km", "km2", "km3", "liter", "litre", "lm", "lumen", "lux", "lx", "m",
                "m2", "m3", "mA", "mCi", "mF", "mL", "mbar", "mbarn", "megabecquerel", "megaelectronvolt", "megahertz", "megajoule", "megavolt",
                "megawatt", "meter", "metre", "mg", "microampere", "microbarn", "microcurie", "microfarad", "microgray", "micrometer", "micron",
                "microsecond", "milliampere", "millibar", "millibarn", "millicurie", "millielectronvolt", "millifarad", "milligram", "milligray",
                "milliliter", "millimeter", "milliradian", "millisecond", "minute", "mm", "mm2", "mm3", "mol", "mole", "mrad", "ms", "nF", "nanoampere",
                "nanobarn", "nanofarad", "nanometer", "nanosecond", "newton", "nm", "ns", "ohm", "pF", "parsec", "pascal", "pc", "petaelectronvolt", "pi",
                "picobarn", "picofarad", "picosecond", "ps", "rad", "radian", "s", "second", "siemens", "sievert", "sr", "steradian", "teraelectronvolt",
                "tesla", "uCi", "uF", "um", "us", "volt", "watt", "weber", "year",
            };

            readonly string text;
            readonly Dictionary<string, double> constants;
            readonly HashSet<string> poisoned;
            readonly HashSet<string> assumed;
            int position;
            int depth;

            public GdmlExpression(string text, Dictionary<string, double> constants, HashSet<string> poisoned, HashSet<string> assumed)
            {
                this.text = text;
                this.constants = constants;
                this.poisoned = poisoned;
                this.assumed = assumed;
            }

            /// <summary>Имя занято у Geant4: файл не вправе его определить.</summary>
            public static bool IsTaken(string name)
            {
                return Taken.Contains(name);
            }

            public bool TryEvaluate(out double value)
            {
                if (!this.Sum(out value))
                {
                    value = 0.0;
                    return false;
                }

                this.SkipSpaces();
                if (this.position != this.text.Length || IsBad(value))
                {
                    value = 0.0;
                    return false;
                }

                return true;
            }

            void SkipSpaces()
            {
                while (this.position < this.text.Length && char.IsWhiteSpace(this.text[this.position]))
                {
                    this.position++;
                }
            }

            bool Next(char symbol)
            {
                this.SkipSpaces();
                if (this.position < this.text.Length && this.text[this.position] == symbol)
                {
                    this.position++;
                    return true;
                }

                return false;
            }

            bool Sum(out double value)
            {
                if (!this.Product(out value))
                {
                    return false;
                }

                while (true)
                {
                    bool plus = this.Next('+');
                    if (!plus && !this.Next('-'))
                    {
                        return true;
                    }

                    double right;
                    if (!this.Product(out right))
                    {
                        return false;
                    }

                    value = plus ? value + right : value - right;
                    if (IsBad(value))
                    {
                        return false;
                    }
                }
            }

            bool Product(out double value)
            {
                if (!this.Unary(out value))
                {
                    return false;
                }

                while (true)
                {
                    bool times = this.Next('*');
                    if (!times && !this.Next('/'))
                    {
                        return true;
                    }

                    double right;
                    if (!this.Unary(out right))
                    {
                        return false;
                    }

                    if (!times && right == 0.0)
                    {
                        return false;
                    }

                    value = times ? value * right : value / right;
                    if (IsBad(value))
                    {
                        return false;
                    }
                }
            }

            bool Unary(out double value)
            {
                value = 0.0;
                if (++this.depth > MaxDepth)
                {
                    return false;
                }

                bool ok;
                if (this.Next('-'))
                {
                    ok = this.Unary(out value);
                    value = -value;
                }
                else if (this.Next('+'))
                {
                    ok = this.Unary(out value);
                }
                else
                {
                    ok = this.Primary(out value);
                }

                this.depth--;
                return ok;
            }

            bool Primary(out double value)
            {
                value = 0.0;
                if (this.Next('('))
                {
                    return this.Sum(out value) && this.Next(')');
                }

                this.SkipSpaces();
                if (this.position >= this.text.Length)
                {
                    return false;
                }

                char first = this.text[this.position];
                if (char.IsDigit(first) || first == '.')
                {
                    return this.Number(out value);
                }

                if (char.IsLetter(first) || first == '_')
                {
                    int start = this.position;
                    while (this.position < this.text.Length
                           && (char.IsLetterOrDigit(this.text[this.position]) || this.text[this.position] == '_'))
                    {
                        this.position++;
                    }

                    string name = this.text.Substring(start, this.position - start);
                    int arity;
                    if (Functions.TryGetValue(name, out arity) && this.Next('('))
                    {
                        return this.Call(name, arity, out value);
                    }

                    if (this.poisoned.Contains(name))
                    {
                        return false;
                    }

                    if (this.constants.TryGetValue(name, out value) || Builtin.TryGetValue(name, out value))
                    {
                        return true;
                    }

                    if (Assumed.TryGetValue(name, out value))
                    {
                        this.assumed.Add(name);
                        return true;
                    }

                    return false;
                }

                return false;
            }

            /// <summary>
            /// (fixGdml2, решение Amber 07.11.2026 вопросником, дословно: «Добавить функции (Рекомендую)») Функции набора
            /// Geant4 — `setStdMath` CLHEP, его читает `G4GDMLEvaluator`: имя и число аргументов. Углы — радианы, как у CLHEP;
            /// градусы пишутся единицей `deg` (= π/180 в `Builtin`): `sin(30*deg)` = 0.5. Значение вне области (`sqrt(-1)`,
            /// `log(0)`, `asin(2)`) — NaN или бесконечность, и это отказ со строкой, как у деления на ноль, а не ноль.
            /// </summary>
            static readonly Dictionary<string, int> Functions = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                { "sin", 1 }, { "cos", 1 }, { "tan", 1 }, { "asin", 1 }, { "acos", 1 }, { "atan", 1 }, { "atan2", 2 },
                { "sinh", 1 }, { "cosh", 1 }, { "tanh", 1 },
                { "sqrt", 1 }, { "pow", 2 }, { "exp", 1 }, { "log", 1 }, { "log10", 1 }, { "abs", 1 }, { "min", 2 }, { "max", 2 },
            };

            /// <summary>Аргументы после открытой скобки и сама функция; число аргументов обязано совпасть.</summary>
            bool Call(string name, int arity, out double value)
            {
                value = 0.0;
                List<double> args = new List<double>();
                if (!this.Next(')'))
                {
                    while (true)
                    {
                        double argument;
                        if (!this.Sum(out argument))
                        {
                            return false;
                        }

                        args.Add(argument);
                        if (this.Next(')'))
                        {
                            break;
                        }

                        if (!this.Next(','))
                        {
                            return false;
                        }
                    }
                }

                if (args.Count != arity)
                {
                    return false;
                }

                double a = args[0], b = arity > 1 ? args[1] : 0.0;
                switch (name)
                {
                    case "sin": value = Math.Sin(a); break;
                    case "cos": value = Math.Cos(a); break;
                    case "tan": value = Math.Tan(a); break;
                    case "asin": value = Math.Asin(a); break;
                    case "acos": value = Math.Acos(a); break;
                    case "atan": value = Math.Atan(a); break;
                    case "atan2": value = Math.Atan2(a, b); break;
                    case "sinh": value = Math.Sinh(a); break;
                    case "cosh": value = Math.Cosh(a); break;
                    case "tanh": value = Math.Tanh(a); break;
                    case "sqrt": value = Math.Sqrt(a); break;
                    case "pow": value = Math.Pow(a, b); break;
                    case "exp": value = Math.Exp(a); break;
                    case "log": value = Math.Log(a); break;
                    case "log10": value = Math.Log10(a); break;
                    case "abs": value = Math.Abs(a); break;
                    case "min": value = Math.Min(a, b); break;
                    case "max": value = Math.Max(a, b); break;
                    default: return false;
                }

                return !IsBad(value);
            }

            bool Number(out double value)
            {
                int start = this.position;
                int length = this.text.Length;
                while (this.position < length && (char.IsDigit(this.text[this.position]) || this.text[this.position] == '.'))
                {
                    this.position++;
                }

                if (this.position < length && (this.text[this.position] == 'e' || this.text[this.position] == 'E'))
                {
                    int mark = this.position + 1;
                    if (mark < length && (this.text[mark] == '+' || this.text[mark] == '-'))
                    {
                        mark++;
                    }

                    if (mark < length && char.IsDigit(this.text[mark]))
                    {
                        this.position = mark;
                        while (this.position < length && char.IsDigit(this.text[this.position]))
                        {
                            this.position++;
                        }
                    }
                }

                return double.TryParse(this.text.Substring(start, this.position - start), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out value);
            }
        }

        /// <summary>Единица длины в мм. Пустая — мм; незнакомая — false, а не 1.</summary>
        static bool TryLengthScale(string unit, out double scale)
        {
            scale = 1.0;
            if (string.IsNullOrEmpty(unit))
            {
                return true;
            }

            switch (unit.Trim())
            {
                case "mm": case "millimeter": scale = 1.0; return true;
                case "cm": case "centimeter": scale = 10.0; return true;
                case "m": case "meter": scale = 1000.0; return true;
                case "km": case "kilometer": scale = 1.0e6; return true;
                case "um": case "micrometer": scale = 0.001; return true;
                case "nm": case "nanometer": scale = 1.0e-6; return true;
                case "Ang": case "angstrom": scale = 1.0e-7; return true;
                case "fm": case "fermi": scale = 1.0e-12; return true;
                case "inch": scale = 25.4; return true;
                case "ft": scale = 304.8; return true;
                case "mil": scale = 0.0254; return true;
                default: return false;
            }
        }

        /// <summary>Единица угла в градусах. Пустая — радиан, как в Geant4; незнакомая — false.</summary>
        static bool TryAngleScale(string unit, out double scale)
        {
            scale = AngleDefaultScale;
            if (string.IsNullOrEmpty(unit))
            {
                return true;
            }

            switch (unit.Trim())
            {
                case "deg": case "degree": scale = 1.0; return true;
                case "rad": case "radian": scale = 180.0 / Math.PI; return true;
                case "mrad": case "milliradian": scale = 180.0 / Math.PI / 1000.0; return true;
                case "urad": scale = 180.0 / Math.PI / 1000000.0; return true;
                default: return false;
            }
        }

        /// <summary>Плотность в г/см3. Пустая — г/см3, как `DRead`; незнакомая — false.</summary>
        static bool TryDensityScale(string unit, out double scale)
        {
            scale = 1.0;
            if (string.IsNullOrEmpty(unit))
            {
                return true;
            }

            switch (unit.Trim())
            {
                case "g/cm3": scale = 1.0; return true;
                case "kg/m3": scale = 0.001; return true;
                case "mg/cm3": scale = 0.001; return true;
                case "g/m3": scale = 1.0e-6; return true;
                default: return false;
            }
        }

        /// <summary>Единица `quantity` в системе вычислителя: мм, радиан, г/см3.</summary>
        static bool TryQuantityScale(string unit, out double scale)
        {
            if (string.IsNullOrEmpty(unit))
            {
                scale = 1.0;
                return true;
            }

            if (TryLengthScale(unit, out scale) || TryDensityScale(unit, out scale))
            {
                return true;
            }

            if (TryAngleScale(unit, out scale))
            {
                scale = scale * Math.PI / 180.0;
                return true;
            }

            scale = 1.0;
            return false;
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
            // Без этой развилки 1e300 печатается тремястами цифрами, а 1e-25 (`G4_Galactic`, fixGdml2) — нулём.
            return Math.Abs(value) >= 1e15 || (value != 0.0 && Math.Abs(value) < 1e-5)
                ? value.ToString("0.#####E+0", CultureInfo.InvariantCulture)
                : value.ToString("0.#####", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Ссылка на вещество в составе: имя и массовая доля или, у <c>composite</c>, число атомов.</summary>
    public sealed class GdmlMaterialEntry
    {
        public readonly string Reference;
        public readonly double Fraction;

        /// <summary>Fraction — число атомов (<c>composite</c>), а не массовая доля.</summary>
        public readonly bool IsAtoms;

        public GdmlMaterialEntry(string reference, double fraction, bool isAtoms)
        {
            this.Reference = reference;
            this.Fraction = fraction;
            this.IsAtoms = isAtoms;
        }
    }
}
