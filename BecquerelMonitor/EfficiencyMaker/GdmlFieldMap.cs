using System;
using System.Collections.Generic;
using System.Globalization;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>Части геометрии, которые импорт GDML кладёт в поля редактора (галки окна импорта).</summary>
    [Flags]
    public enum GdmlParts
    {
        None = 0,
        Detector = 1,
        Sample = 2,
        Shield = 4,
    }

    /// <summary>Слой стопки вдоль луча: вещество и толщина, мм.</summary>
    public sealed class GdmlStackItem
    {
        public GdmlMaterial Material;
        public string Name = "";
        public double Thickness;
        public bool Gas;
        public GdmlVolumeInstance Volume;
    }

    /// <summary>Строка сверки «в файле → в полях».</summary>
    public sealed class GdmlMapLine
    {
        public GdmlParts Part;
        public string InFile = "";
        public string InFields = "";
    }

    /// <summary>
    /// (`AMBER322`) Раскладка геометрии GDML в поля редактора: «Детектор» (кристалл, отражатель, зазор, корпус, оправа),
    /// «Источник» (цилиндр перед торцом или маринелли) и «Защита» (слои вокруг сцены). Толщины снимаются лучами
    /// (<see cref="GdmlLocator"/>) от кристалла наружу; имён томов раскладка не читает, кроме роли пробы у читателя.
    ///
    /// Раскладка — упрощение, и каждое сработавшее упрощение названо в <see cref="Simplifications"/>:
    /// слои одного рода сложены (вещество корпуса — преобладающее по массовой толщине, толщина — равная по массовой
    /// толщине, решение Amber 11.11.2026 «По массовой толщине»); бок один на все стороны — ближний (прецедент шаблона
    /// «RadiaCode-110», `AMBER307`); передняя грань — к пробе, без пробы — напротив фотоприёмника.
    /// Что в поля не легло — <see cref="Unused"/> («не используется в геометрии»).
    /// </summary>
    public sealed class GdmlFieldMap
    {
        const double Tolerance = 0.02;

        readonly GdmlGeometry geometry;
        readonly GdmlOverview overview;
        readonly GdmlLocator locator;
        readonly HashSet<string> reflectors = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Что раскладка нашла в файле и может положить в поля.</summary>
        public GdmlParts Found;

        /// <summary>Почему детектор не разложен (ключ ресурса и запасной текст); null — разложен.</summary>
        public string Refusal;

        /// <summary>Почему проба из файла не переносится; null — перенесена или пробы нет.</summary>
        public string SampleProblem;

        public readonly List<GdmlMapLine> Lines = new List<GdmlMapLine>();
        public readonly List<KeyValuePair<GdmlParts, string>> Simplifications = new List<KeyValuePair<GdmlParts, string>>();

        readonly GeometryModel fields = new GeometryModel();
        readonly Dictionary<GdmlParts, HashSet<GdmlVolumeInstance>> used = new Dictionary<GdmlParts, HashSet<GdmlVolumeInstance>>();
        readonly Dictionary<GdmlParts, HashSet<string>> usedMaterials = new Dictionary<GdmlParts, HashSet<string>>();

        GdmlVolumeInstance crystalVolume, sampleVolume, shieldFrom;

        /// <summary>Кристалл и проба, по которым построена раскладка (выбранные человеком или узнанные программой).</summary>
        public GdmlVolumeInstance Crystal
        {
            get { return this.crystalVolume; }
        }

        public GdmlVolumeInstance Sample
        {
            get { return this.sampleVolume; }
        }

        /// <summary>Вещества защиты изнутри наружу через запятую и число её деталей: строка списка «найдено лучами».</summary>
        public string ShieldMaterials = "";

        public int ShieldParts;

        /// <summary>Годится ли деталь кристаллом по форме: короб или сплошной цилиндр.</summary>
        public static bool CrystalShapeFits(GdmlVolumeInstance volume)
        {
            double a, b, c;
            return volume != null && volume.Solid != null && volume.BoundsKnown
                   && (volume.Solid.Kind == GdmlSolidKind.Box || IsCylinder(volume.Solid, out a, out b, out c));
        }
        double[] deviceMin, deviceMax;

        // Система кристалла в мире.
        double[] center, front, sideU, sideV;
        double halfDepth, halfU, halfV;
        bool cylinder;
        double frontEdge, lateralEdge, backEdge;     // от центра кристалла до наружной поверхности прибора
        double sceneFront, sceneLateral;             // то же до края сцены (с сосудом пробы)

        /// <summary>Имя мировой оси передней грани со знаком: «+Z».</summary>
        public string FrontName = "";

        /// <summary>Имя стороны, по которой взят бок.</summary>
        public string SideName = "";

        GdmlFieldMap(GdmlGeometry geometry, GdmlOverview overview)
        {
            this.geometry = geometry;
            this.overview = overview;
            this.locator = new GdmlLocator(geometry);
            foreach (GdmlParts part in new[] { GdmlParts.Detector, GdmlParts.Sample, GdmlParts.Shield })
            {
                this.used[part] = new HashSet<GdmlVolumeInstance>();
                this.usedMaterials[part] = new HashSet<string>(StringComparer.Ordinal);
            }
        }

        public static GdmlFieldMap Build(GdmlGeometry geometry, GdmlOverview overview)
        {
            return Build(geometry, overview, null, null, null);
        }

        /// <param name="crystal">Кристалл, выбранный человеком; null — узнанный читателем.</param>
        /// <param name="sample">Проба, выбранная человеком; null — узнанная читателем.</param>
        /// <param name="shieldFrom">Деталь, с которой начинается защита, выбранная человеком; null — всё твёрдое за краем сцены.</param>
        public static GdmlFieldMap Build(GdmlGeometry geometry, GdmlOverview overview, GdmlVolumeInstance crystal, GdmlVolumeInstance sample,
                                         GdmlVolumeInstance shieldFrom)
        {
            GdmlFieldMap map = new GdmlFieldMap(geometry, overview ?? GdmlOverview.Of(geometry));
            map.crystalVolume = crystal ?? map.overview.Crystal;
            map.sampleVolume = sample ?? map.overview.Sample;
            map.shieldFrom = shieldFrom;
            if (ReferenceEquals(map.sampleVolume, map.crystalVolume))
            {
                map.sampleVolume = null;
            }

            // Габарит прибора читатель считал вокруг СВОЕГО кристалла: при другом выборе его нет.
            if (ReferenceEquals(map.crystalVolume, map.overview.Crystal))
            {
                map.deviceMin = map.overview.DeviceMin;
                map.deviceMax = map.overview.DeviceMax;
            }

            foreach (GdmlMaterialRow row in map.overview.Materials)
            {
                if (row.Role == GdmlRole.Reflector)
                {
                    map.reflectors.Add(row.Name);
                }
            }

            if (map.LayDetector())
            {
                map.Found |= GdmlParts.Detector;
                if (map.LaySample())
                {
                    map.Found |= GdmlParts.Sample;
                }

                if (map.LayShield())
                {
                    map.Found |= GdmlParts.Shield;
                }
            }

            return map;
        }

        // ------------------------------------------------------------------
        // Векторы
        // ------------------------------------------------------------------

        static double[] Add(double[] a, double[] b, double k)
        {
            return new[] { a[0] + k * b[0], a[1] + k * b[1], a[2] + k * b[2] };
        }

        static double[] Neg(double[] a)
        {
            return new[] { -a[0], -a[1], -a[2] };
        }

        static double Dot(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        static string AxisName(double[] d)
        {
            int axis = 0;
            for (int i = 1; i < 3; i++)
            {
                if (Math.Abs(d[i]) > Math.Abs(d[axis]))
                {
                    axis = i;
                }
            }

            return (d[axis] < 0.0 ? "−" : "+") + "XYZ"[axis];
        }

        static bool IsGas(GdmlVolumeInstance volume)
        {
            return volume == null || volume.Material == null || (volume.Material.HasDensity && volume.Material.Density < 0.01);
        }

        static double Density(GdmlMaterial material)
        {
            return material != null && material.HasDensity ? material.Density : 0.0;
        }

        static double R4(double value)
        {
            return Math.Round(value, 4);
        }

        static string N(double value)
        {
            return GdmlSketch.Fmt(value);
        }

        static string Display(GdmlMaterial material)
        {
            string name = material == null ? "" : material.Name;
            return name.StartsWith("G4_", StringComparison.Ordinal) ? name.Substring(3) : name;
        }

        /// <summary>Расстояние вдоль луча до выхода из габарита; 0 — начало снаружи.</summary>
        static double Exit(double[] o, double[] d, double[] min, double[] max)
        {
            double exit = double.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if (o[i] < min[i] - 1e-6 || o[i] > max[i] + 1e-6)
                {
                    return 0.0;
                }

                if (Math.Abs(d[i]) > 1e-12)
                {
                    exit = Math.Min(exit, ((d[i] > 0.0 ? max[i] : min[i]) - o[i]) / d[i]);
                }
            }

            return exit == double.MaxValue ? 0.0 : Math.Max(0.0, exit);
        }

        double WorldReach(double[] o, double[] d)
        {
            double reach = this.geometry.FrameMin == null ? 0.0 : Exit(o, d, this.geometry.FrameMin, this.geometry.FrameMax);
            return reach > 0.0 ? reach : 1000.0;
        }

        /// <summary>Стопка вдоль луча: соседние отрезки одного вещества слиты.</summary>
        List<GdmlStackItem> Stack(double[] origin, double[] direction, double length)
        {
            List<GdmlStackItem> items = new List<GdmlStackItem>();
            foreach (GdmlRaySegment segment in this.locator.Ray(origin, direction, length))
            {
                bool gas = IsGas(segment.Volume);
                GdmlMaterial material = segment.Volume == null ? null : segment.Volume.Material;
                string name = segment.Volume == null ? "" : segment.Volume.MaterialName;
                GdmlStackItem last = items.Count > 0 ? items[items.Count - 1] : null;
                if (last != null && last.Gas == gas && (gas || last.Name == name))
                {
                    last.Thickness += segment.Length;
                    if (gas && last.Material == null)
                    {
                        last.Material = material;
                        last.Name = name;
                    }
                }
                else
                {
                    items.Add(new GdmlStackItem { Material = material, Name = name, Thickness = segment.Length, Gas = gas, Volume = segment.Volume });
                }
            }

            return items;
        }

        static void TrimGas(List<GdmlStackItem> items)
        {
            while (items.Count > 0 && items[items.Count - 1].Gas)
            {
                items.RemoveAt(items.Count - 1);
            }
        }

        static double Total(List<GdmlStackItem> items)
        {
            double sum = 0.0;
            foreach (GdmlStackItem item in items)
            {
                sum += item.Thickness;
            }

            return sum;
        }

        string StackText(List<GdmlStackItem> items)
        {
            List<string> parts = new List<string>();
            foreach (GdmlStackItem item in items)
            {
                string name = item.Gas ? (item.Material == null ? GdmlText.T("GdmlMapVoid", "gap") : Display(item.Material)) : Display(item.Material);
                parts.Add(name + " " + N(item.Thickness));
            }

            return parts.Count == 0 ? GdmlText.T("GdmlMapNothing", "nothing") : string.Join(" + ", parts.ToArray());
        }

        // ------------------------------------------------------------------
        // Вещества
        // ------------------------------------------------------------------

        static List<GeometryMaterial> library;

        /// <summary>
        /// Вещество поля из вещества файла. Состав и плотность — файла; имя — библиотечное, когда состав совпал с
        /// веществом библиотеки (по имени вещества кристалла редактор берёт пресет разрешения, `AMBER321`).
        /// </summary>
        internal static GeometryMaterial ToField(GdmlMaterial material)
        {
            GeometryMaterial result = new GeometryMaterial();
            if (material == null)
            {
                return result;
            }

            result.Name = Display(material);
            result.Density = Density(material);
            foreach (KeyValuePair<int, double> part in material.Fractions)
            {
                result.Fractions[part.Key] = part.Value;
            }

            if (library == null)
            {
                List<GeometryMaterial> all = new List<GeometryMaterial>();
                foreach (GeometryMaterialLibrary.Entry entry in GeometryMaterialLibrary.Seed())
                {
                    try
                    {
                        all.Add(GeometryMaterialLibrary.Make(entry, entry.Density));
                    }
                    catch (Exception)
                    {
                    }
                }

                if (all.Count == 0)
                {
                    return result;
                }

                library = all;
            }

            foreach (GeometryMaterial known in library)
            {
                if (known.Fractions.Count != result.Fractions.Count || known.Fractions.Count == 0)
                {
                    continue;
                }

                bool same = true;
                foreach (KeyValuePair<int, double> part in result.Fractions)
                {
                    double other;
                    // Допуск 0.02: файл может нести состав грубо (CsI долями 0.5 / 0.5 при настоящих 0.5115 / 0.4885).
                    same &= known.Fractions.TryGetValue(part.Key, out other) && Math.Abs(other - part.Value) <= 0.02;
                }

                if (same && known.Density > 0.0 && Math.Abs(known.Density - result.Density) <= 0.25 * known.Density)
                {
                    result.Name = known.Name;
                    break;
                }
            }

            return result;
        }

        // ------------------------------------------------------------------
        // Детектор
        // ------------------------------------------------------------------

        sealed class Layers
        {
            public double Reflector, Gap, Housing, HousingGeometric;
            public GdmlMaterial ReflectorMaterial, GapMaterial, HousingMaterial;
            public bool Mixed, Summed;
            public List<GdmlStackItem> Items;
            public double MassThickness;
            public bool StartsWithReflector;
        }

        Layers Classify(List<GdmlStackItem> items)
        {
            Layers layers = new Layers { Items = items };
            Dictionary<string, double> mass = new Dictionary<string, double>(StringComparer.Ordinal);
            Dictionary<string, GdmlMaterial> byName = new Dictionary<string, GdmlMaterial>(StringComparer.Ordinal);
            double gapBest = 0.0, reflectorBest = 0.0;
            int solids = 0, gaps = 0;
            bool first = true;
            foreach (GdmlStackItem item in items)
            {
                layers.MassThickness += item.Thickness * Density(item.Material);
                if (item.Gas)
                {
                    layers.Gap += item.Thickness;
                    gaps++;
                    if (item.Material != null && item.Thickness > gapBest)
                    {
                        gapBest = item.Thickness;
                        layers.GapMaterial = item.Material;
                    }

                    continue;
                }

                if (this.reflectors.Contains(item.Name))
                {
                    layers.StartsWithReflector |= first;
                    layers.Reflector += item.Thickness;
                    if (item.Thickness > reflectorBest)
                    {
                        reflectorBest = item.Thickness;
                        layers.ReflectorMaterial = item.Material;
                    }
                }
                else
                {
                    solids++;
                    layers.HousingGeometric += item.Thickness;
                    double sum;
                    mass.TryGetValue(item.Name, out sum);
                    mass[item.Name] = sum + item.Thickness * Density(item.Material);
                    byName[item.Name] = item.Material;
                }

                first = false;
            }

            double most = -1.0, all = 0.0;
            foreach (KeyValuePair<string, double> pair in mass)
            {
                all += pair.Value;
                if (pair.Value > most)
                {
                    most = pair.Value;
                    layers.HousingMaterial = byName[pair.Key];
                }
            }

            layers.Mixed = mass.Count > 1;
            layers.Summed = solids > 1 || gaps > 1;
            double density = Density(layers.HousingMaterial);
            layers.Housing = layers.Mixed && density > 0.0 ? all / density : layers.HousingGeometric;
            return layers;
        }

        sealed class Face
        {
            public double[] Direction;
            public double Half;
            public Layers Layers;
            public double Reach;        // от грани кристалла до наружной поверхности прибора
        }

        Face Look(double[] direction, double half)
        {
            double[] origin = Add(this.center, direction, half);
            double length = this.deviceMin != null ? Exit(origin, direction, this.deviceMin, this.deviceMax) : 0.0;
            List<GdmlStackItem> items;
            if (length > 0.0)
            {
                items = this.Stack(origin, direction, length);
            }
            else
            {
                // Прибор не узнан: стопка до первого просвета шире 5 мм после твёрдого слоя.
                items = this.Stack(origin, direction, this.WorldReach(origin, direction));
                bool solid = false;
                for (int i = 0; i < items.Count; i++)
                {
                    solid |= !items[i].Gas;
                    if (solid && items[i].Gas && items[i].Thickness > 5.0)
                    {
                        items.RemoveRange(i, items.Count - i);
                        break;
                    }
                }
            }

            TrimGas(items);
            return new Face { Direction = direction, Half = half, Layers = this.Classify(items), Reach = Total(items) };
        }

        bool Refuse(string key, string fallback)
        {
            this.Refusal = GdmlText.T(key, fallback);
            return false;
        }

        bool LayDetector()
        {
            GdmlVolumeInstance crystal = this.crystalVolume;
            if (crystal == null || crystal.Solid == null || !crystal.BoundsKnown)
            {
                return this.Refuse("GdmlWhyNoCrystal", "the crystal was not recognised");
            }

            int crystals = 0;
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume != null && volume.Solid != null && GdmlGeometry.LooksLikeDetector(volume.Material)
                    && !ReferenceEquals(volume, this.sampleVolume) && volume.VolumeCm3 > 0.5 * crystal.VolumeCm3)
                {
                    crystals++;
                }
            }

            if (crystals > 1)
            {
                return this.Refuse("GdmlWhyManyCrystals", "there is more than one crystal");
            }

            GdmlTransform t = crystal.Transform ?? GdmlTransform.Identity();
            GdmlSolid solid = crystal.Solid;
            double[] half = new double[3];
            double zMid = 0.0;
            if (solid.Kind == GdmlSolidKind.Box)
            {
                half[0] = 0.5 * solid.SizeX; half[1] = 0.5 * solid.SizeY; half[2] = 0.5 * solid.SizeZ;
            }
            else if (IsCylinder(solid, out half[0], out half[2], out zMid))
            {
                this.cylinder = true;
                half[1] = half[0];
            }
            else
            {
                return this.Refuse("GdmlWhyCrystalShape", "the crystal is neither a cylinder nor a box");
            }

            GdmlPoint c = t.Apply(new GdmlPoint(0.0, 0.0, zMid));
            GdmlPoint zero = t.Apply(new GdmlPoint(0, 0, 0));
            this.center = new[] { c.X, c.Y, c.Z };
            double[][] axes = new double[3][];
            GdmlPoint[] ends = { t.Apply(new GdmlPoint(1, 0, 0)), t.Apply(new GdmlPoint(0, 1, 0)), t.Apply(new GdmlPoint(0, 0, 1)) };
            for (int i = 0; i < 3; i++)
            {
                axes[i] = new[] { ends[i].X - zero.X, ends[i].Y - zero.Y, ends[i].Z - zero.Z };
            }

            // Кандидаты передней грани: у бруска шесть, у цилиндра два торца.
            List<Face> faces = new List<Face>();
            for (int i = this.cylinder ? 2 : 0; i < 3; i++)
            {
                faces.Add(this.Look(axes[i], half[i]));
                faces.Add(this.Look(Neg(axes[i]), half[i]));
            }

            Face frontFace = this.PickFront(faces);
            this.front = frontFace.Direction;
            this.halfDepth = frontFace.Half;
            int frontAxis = 0;
            for (int i = 1; i < 3; i++)
            {
                if (Math.Abs(Dot(axes[i], this.front)) > Math.Abs(Dot(axes[frontAxis], this.front)))
                {
                    frontAxis = i;
                }
            }

            int ua = (frontAxis + 1) % 3, va = (frontAxis + 2) % 3;
            this.sideU = axes[ua];
            this.sideV = axes[va];
            this.halfU = half[ua];
            this.halfV = half[va];
            this.FrontName = AxisName(this.front);

            List<Face> sides = new List<Face>
            {
                this.Look(this.sideU, this.halfU), this.Look(Neg(this.sideU), this.halfU),
                this.Look(this.sideV, this.halfV), this.Look(Neg(this.sideV), this.halfV),
            };
            Face side = sides[0];
            double far = 0.0;
            foreach (Face candidate in sides)
            {
                far = Math.Max(far, candidate.Reach);
                if (candidate.Reach < side.Reach - 1e-6)
                {
                    side = candidate;
                }
            }

            this.SideName = AxisName(side.Direction);
            Face back = this.Look(Neg(this.front), this.halfDepth);

            GeometryModel g = this.fields;
            g.IsScintillator = true;
            g.Facing = GeometryDetectorFacing.Front;
            if (this.cylinder)
            {
                g.Shape = CrystalShape.Cylinder;
                g.CrystalDiameter = R4(2.0 * half[0]);
                g.CrystalHeight = R4(2.0 * half[2]);
            }
            else
            {
                g.Shape = CrystalShape.Box;
                g.CrystalBoxX = R4(2.0 * this.halfU);
                g.CrystalBoxY = R4(2.0 * this.halfV);
                g.CrystalBoxZ = R4(2.0 * this.halfDepth);
            }

            g.Crystal = ToField(crystal.Material);
            Layers f = frontFace.Layers, s = side.Layers;
            g.FrontReflectorThickness = R4(f.Reflector);
            g.SideReflectorThickness = R4(s.Reflector);
            g.FrontGapThickness = R4(f.Gap);
            g.SideGapThickness = R4(s.Gap);
            g.FrontCladdingThickness = R4(f.Housing);
            g.SideCladdingThickness = R4(s.Housing);
            g.Reflector = ToField(f.ReflectorMaterial ?? s.ReflectorMaterial);
            g.Gap = ToField(f.GapMaterial ?? s.GapMaterial);
            GdmlMaterial housing = f.HousingMaterial ?? s.HousingMaterial;
            g.Cladding = ToField(housing);

            // Оправа — первый твёрдый слой вещества корпуса за кристаллом; фотоприёмник и начинка в модель не входят.
            g.MountingThickness = 0.0;
            if (housing != null)
            {
                foreach (GdmlStackItem item in back.Layers.Items)
                {
                    if (!item.Gas && item.Name == housing.Name)
                    {
                        g.MountingThickness = R4(item.Thickness);
                        break;
                    }
                }
            }

            this.frontEdge = this.halfDepth + frontFace.Reach;
            this.backEdge = this.halfDepth + back.Reach;
            this.lateralEdge = 0.0;
            for (int i = 0; i < sides.Count; i++)
            {
                this.lateralEdge = Math.Max(this.lateralEdge, (i < 2 ? this.halfU : this.halfV) + sides[i].Reach);
            }

            this.sceneFront = this.frontEdge;
            this.sceneLateral = this.lateralEdge;

            // Кто вошёл: кристалл и всё из веществ, которые легли в поля, внутри прибора.
            HashSet<string> taken = this.usedMaterials[GdmlParts.Detector];
            foreach (GdmlMaterial material in new[] { f.ReflectorMaterial, s.ReflectorMaterial, f.HousingMaterial, s.HousingMaterial })
            {
                if (material != null)
                {
                    taken.Add(material.Name);
                }
            }

            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || volume.Solid == null || IsGas(volume))
                {
                    continue;
                }

                if (ReferenceEquals(volume, crystal) || (taken.Contains(volume.MaterialName) && this.InsideDevice(volume)))
                {
                    this.used[GdmlParts.Detector].Add(volume);
                }
            }

            // Сверка.
            string crystalText = this.cylinder
                ? string.Format(CultureInfo.InvariantCulture, "⌀{0} × {1}", N(g.CrystalDiameter), N(g.CrystalHeight))
                : string.Format(CultureInfo.InvariantCulture, "{0} × {1} × {2}", N(g.CrystalBoxX), N(g.CrystalBoxY), N(g.CrystalBoxZ));
            this.Line(GdmlParts.Detector,
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapCrystal", "Crystal {0}, {1}"), Display(crystal.Material), GdmlOverview.SizeText(crystal, this.geometry.Axis)),
                (this.cylinder ? GdmlText.T("GdmlMapCylinder", "cylinder") : GdmlText.T("GdmlMapBox", "box")) + " " + crystalText);
            this.Line(GdmlParts.Detector,
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapToSample", "Towards the sample ({0}): {1}"), this.FrontName, this.StackText(f.Items)),
                this.LayersText(f));
            this.Line(GdmlParts.Detector,
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapSide", "At the side ({0}): {1}"), this.SideName, this.StackText(s.Items)),
                this.LayersText(s));
            this.Line(GdmlParts.Detector,
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapBehind", "Behind the crystal: {0}"), this.StackText(back.Layers.Items)),
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapMounting", "mounting {0}"), N(g.MountingThickness)));

            if (f.Summed || s.Summed)
            {
                this.Simplify(GdmlParts.Detector, GdmlText.T("GdmlSimplSummed", "Layers of the same kind are added together."));
            }

            if (f.Mixed || s.Mixed)
            {
                this.Simplify(GdmlParts.Detector, string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSimplMass", "Housing layers of different materials are replaced by {0} of the same mass thickness."), Display(housing)));
            }

            if (far > side.Reach * (1.0 + Tolerance) + 1e-6)
            {
                this.Simplify(GdmlParts.Detector, string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSimplSide", "The side is taken from the nearest side ({0}); other sides differ."), this.SideName));
            }

            return true;
        }

        string LayersText(Layers layers)
        {
            return string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapLayers", "reflector {0}, gap {1}, housing {2}"),
                N(R4(layers.Reflector)), N(R4(layers.Gap)), N(R4(layers.Housing)));
        }

        void Line(GdmlParts part, string inFile, string inFields)
        {
            this.Lines.Add(new GdmlMapLine { Part = part, InFile = inFile, InFields = inFields });
        }

        void Simplify(GdmlParts part, string text)
        {
            this.Simplifications.Add(new KeyValuePair<GdmlParts, string>(part, text));
        }

        bool InsideDevice(GdmlVolumeInstance volume)
        {
            if (this.deviceMin == null || !volume.BoundsKnown)
            {
                return true;
            }

            for (int i = 0; i < 3; i++)
            {
                if (volume.Min[i] < this.deviceMin[i] - 1e-6 || volume.Max[i] > this.deviceMax[i] + 1e-6)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Передняя грань. С пробой — та, что смотрит на пробу. Без пробы — напротив фотоприёмника: единственная грань,
        /// не закрытая отражателем, при закрытой противоположной. Иначе — грань с наименьшей массовой толщиной до наружной
        /// поверхности прибора.
        /// </summary>
        Face PickFront(List<Face> faces)
        {
            GdmlVolumeInstance sample = this.sampleVolume;
            if (sample != null && sample.BoundsKnown)
            {
                double[] to = new double[3];
                double length = 0.0;
                for (int i = 0; i < 3; i++)
                {
                    to[i] = 0.5 * (sample.Min[i] + sample.Max[i]) - this.center[i];
                    length += to[i] * to[i];
                }

                length = Math.Sqrt(length);
                Face best = null;
                foreach (Face face in faces)
                {
                    if (best == null || Dot(face.Direction, to) > Dot(best.Direction, to))
                    {
                        best = face;
                    }
                }

                if (length > 1.0 && Dot(best.Direction, to) > 0.5 * length)
                {
                    this.Simplify(GdmlParts.Detector, GdmlText.T("GdmlSimplFaceSample", "The crystal face turned to the sample is taken as the front."));
                    return best;
                }
            }

            Face bare = null;
            int bares = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                Face opposite = faces[i ^ 1];
                if (!faces[i].Layers.StartsWithReflector && opposite.Layers.StartsWithReflector)
                {
                    bare = opposite;
                    bares++;
                }
            }

            if (bares == 1)
            {
                this.Simplify(GdmlParts.Detector, GdmlText.T("GdmlSimplFaceNoSample", "The face opposite the photosensor is taken as the front."));
                return bare;
            }

            Face thin = faces[0];
            foreach (Face face in faces)
            {
                if (face.Layers.MassThickness < thin.Layers.MassThickness)
                {
                    thin = face;
                }
            }

            this.Simplify(GdmlParts.Detector, GdmlText.T("GdmlSimplFaceThin", "The face with the thinnest cover is taken as the front."));
            return thin;
        }

        /// <summary>Тело вращения — сплошной цилиндр: радиус, полувысота и середина по своей оси z.</summary>
        static bool IsCylinder(GdmlSolid solid, out double radius, out double halfHeight, out double zMid)
        {
            radius = 0.0;
            halfHeight = 0.0;
            zMid = 0.0;
            if (solid.DeltaPhi < 360.0 - 1e-6)
            {
                return false;
            }

            double zmin = double.MaxValue, zmax = double.MinValue, area = 0.0;
            if (solid.Kind == GdmlSolidKind.Polycone && solid.Planes.Count >= 2)
            {
                for (int i = 0; i < solid.Planes.Count; i++)
                {
                    double[] plane = solid.Planes[i];
                    radius = Math.Max(radius, plane[2]);
                    zmin = Math.Min(zmin, plane[0]);
                    zmax = Math.Max(zmax, plane[0]);
                    if (i > 0)
                    {
                        double[] before = solid.Planes[i - 1];
                        area += Math.Abs(plane[0] - before[0]) * 0.5 * ((plane[2] - plane[1]) + (before[2] - before[1]));
                    }
                }
            }
            else if (solid.Kind == GdmlSolidKind.GenericPolycone && solid.Profile.Count >= 3)
            {
                int n = solid.Profile.Count;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    radius = Math.Max(radius, solid.Profile[i][0]);
                    zmin = Math.Min(zmin, solid.Profile[i][1]);
                    zmax = Math.Max(zmax, solid.Profile[i][1]);
                    area += solid.Profile[j][0] * solid.Profile[i][1] - solid.Profile[i][0] * solid.Profile[j][1];
                }

                area = Math.Abs(area) / 2.0;
            }
            else
            {
                return false;
            }

            if (!(radius > 0.0) || !(zmax > zmin))
            {
                return false;
            }

            halfHeight = 0.5 * (zmax - zmin);
            zMid = 0.5 * (zmax + zmin);

            // Сплошной цилиндр — профиль заполняет прямоугольник «радиус × высота» (фаски до 1 % площади не в счёт).
            return area >= 0.99 * radius * (zmax - zmin);
        }

        // ------------------------------------------------------------------
        // Проба
        // ------------------------------------------------------------------

        sealed class Run
        {
            public double From, To;
            public GdmlVolumeInstance Volume;
            public bool Gas;
            public string Name = "";
            public GdmlMaterial Material;

            public double Length
            {
                get { return this.To - this.From; }
            }
        }

        /// <summary>Отрезки луча с координатой от начала; соседние твёрдые одного вещества и соседние газы слиты.</summary>
        List<Run> Runs(double[] origin, double[] direction, double length)
        {
            List<Run> runs = new List<Run>();
            foreach (GdmlRaySegment segment in this.locator.Ray(origin, direction, length))
            {
                bool gas = IsGas(segment.Volume);
                bool sample = ReferenceEquals(segment.Volume, this.sampleVolume);
                string name = segment.Volume == null ? "" : segment.Volume.MaterialName;
                Run last = runs.Count > 0 ? runs[runs.Count - 1] : null;
                bool lastSample = last != null && ReferenceEquals(last.Volume, this.sampleVolume);
                if (last != null && last.Gas == gas && lastSample == sample && (gas || sample || last.Name == name)
                    && !ReferenceEquals(segment.Volume, this.shieldFrom))
                {
                    last.To = segment.To;
                }
                else
                {
                    runs.Add(new Run
                    {
                        From = segment.From, To = segment.To, Volume = segment.Volume, Gas = gas && !sample, Name = name,
                        Material = segment.Volume == null ? null : segment.Volume.Material,
                    });
                }
            }

            return runs;
        }

        bool IsSample(Run run)
        {
            return run != null && ReferenceEquals(run.Volume, this.sampleVolume);
        }

        static int IndexAt(List<Run> runs, double t)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                if (t >= runs[i].From && t < runs[i].To)
                {
                    return i;
                }
            }

            return -1;
        }

        bool SampleFails(string key, string fallback)
        {
            this.SampleProblem = GdmlText.T(key, fallback);
            GdmlVolumeInstance sample = this.sampleVolume;
            this.Line(GdmlParts.Sample,
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapSample", "Sample {0}, {1}"), Display(sample.Material), GdmlOverview.SizeText(sample, this.geometry.Axis)),
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapNotMoved", "not transferred: {0}"), this.SampleProblem));
            return false;
        }

        bool LaySample()
        {
            GdmlVolumeInstance sample = this.sampleVolume;
            if (sample == null || !sample.BoundsKnown)
            {
                return false;
            }

            double reach = this.WorldReach(this.center, this.front) + 1.0;
            List<Run> axis = this.Runs(this.center, this.front, reach);
            int at = axis.FindIndex(this.IsSample);
            if (at < 0 || axis[at].From < this.frontEdge - 1e-6)
            {
                return this.SampleFails("GdmlWhyOffAxis", "it is not on the crystal axis");
            }

            // От наружной поверхности прибора до пробы: просвет, затем стенка.
            double gap = 0.0, wall = 0.0, wallBest = 0.0;
            GdmlMaterial wallMaterial = null;
            for (int i = 0; i < at; i++)
            {
                // Край прибора и граница отрезка — одно число с точностью счёта: остаток тоньше микрона слоем не считается.
                double from = Math.Max(axis[i].From, this.frontEdge);
                if (axis[i].To <= from + 1e-6)
                {
                    continue;
                }

                if (axis[i].Gas && wall == 0.0)
                {
                    gap += axis[i].To - from;
                }
                else if (!axis[i].Gas)
                {
                    wall += axis[i].To - from;
                    if (axis[i].To - from > wallBest)
                    {
                        wallBest = axis[i].To - from;
                        wallMaterial = axis[i].Material;
                    }
                }
                else
                {
                    return this.SampleFails("GdmlWhyShape", "its shape is not one the editor has");
                }
            }

            double cap = axis[at].Length, sampleEnd = axis[at].To;
            double farGap = 0.0, farWall = 0.0;
            int next = at + 1;
            if (next < axis.Count && axis[next].Gas && next + 1 < axis.Count && !axis[next + 1].Gas
                && wallMaterial != null && axis[next + 1].Name == wallMaterial.Name)
            {
                farGap = axis[next].Length;
                next++;
            }

            if (next < axis.Count && !axis[next].Gas && !this.IsSample(axis[next])
                && (farGap == 0.0 || (wallMaterial != null && axis[next].Name == wallMaterial.Name)))
            {
                farWall = axis[next].Length;
                wallMaterial = wallMaterial ?? axis[next].Material;
            }

            GeometryModel g = this.fields;
            List<Run> across = this.Runs(this.center, this.sideU, this.WorldReach(this.center, this.sideU) + 1.0);
            int ring = across.FindIndex(this.IsSample);
            bool marinelli = ring >= 0;
            double[] probeFrom = marinelli ? this.center : Add(this.center, this.front, axis[at].From + 0.5 * cap);
            if (!marinelli)
            {
                across = this.Runs(probeFrom, this.sideU, this.WorldReach(probeFrom, this.sideU) + 1.0);
                ring = across.FindIndex(this.IsSample);
                if (ring != 0)
                {
                    return this.SampleFails("GdmlWhyShape", "its shape is not one the editor has");
                }
            }

            double inner = across[ring].From, outer = across[ring].To;
            double holeWall = 0.0, sideWall = 0.0;
            if (ring > 0 && !across[ring - 1].Gas && across[ring - 1].From >= this.lateralEdge - 1e-6)
            {
                holeWall = across[ring - 1].Length;
                wallMaterial = wallMaterial ?? across[ring - 1].Material;
            }

            if (ring + 1 < across.Count && !across[ring + 1].Gas)
            {
                sideWall = across[ring + 1].Length;
                wallMaterial = wallMaterial ?? across[ring + 1].Material;
            }

            // Круглая и соосная: те же радиусы в обратную сторону, вбок и по диагонали.
            double[] diagonal = { 0.0, 0.0, 0.0 };
            for (int i = 0; i < 3; i++)
            {
                diagonal[i] = (this.sideU[i] + this.sideV[i]) / Math.Sqrt(2.0);
            }

            foreach (double[] other in new[] { Neg(this.sideU), this.sideV, Neg(this.sideV), diagonal })
            {
                List<Run> check = this.Runs(probeFrom, other, this.WorldReach(probeFrom, other) + 1.0);
                int found = check.FindIndex(this.IsSample);
                if (found < 0 || Math.Abs(check[found].To - outer) > Tolerance * outer + 0.05
                    || Math.Abs(check[found].From - inner) > Tolerance * Math.Max(inner, 1.0) + 0.05)
                {
                    return this.SampleFails(ReferenceEquals(other, diagonal) ? "GdmlWhyShape" : "GdmlWhyOffAxis",
                        ReferenceEquals(other, diagonal) ? "its shape is not one the editor has" : "it is not on the crystal axis");
                }
            }

            // Высоты: лучи вдоль оси внутри пробы и внутри стенок.
            double big = this.WorldReach(this.center, this.front) + this.WorldReach(this.center, Neg(this.front)) + 2.0;
            double back = this.WorldReach(this.center, Neg(this.front)) + 1.0;
            Func<double, double, Predicate<Run>, Run> along = (radius, coordinate, want) =>
            {
                double[] from = Add(Add(this.center, this.sideU, radius), this.front, -back);
                List<Run> runs = this.Runs(from, this.front, big);
                int index = IndexAt(runs, coordinate + back);
                if (index < 0 || !want(runs[index]))
                {
                    return null;
                }

                return new Run { From = runs[index].From - back, To = runs[index].To - back, Material = runs[index].Material };
            };

            double middle = marinelli ? 0.5 * (inner + outer) : 0.5 * outer;
            Run column = along(middle, marinelli ? 0.0 : axis[at].From + 0.5 * cap, this.IsSample);
            if (column == null)
            {
                return this.SampleFails("GdmlWhyShape", "its shape is not one the editor has");
            }

            double height = column.Length;
            Run shell = sideWall > 0.0 ? along(outer + 0.5 * sideWall, 0.5 * (column.From + column.To), run => !run.Gas) : null;
            double body = shell != null ? shell.Length : farWall + farGap + height + (marinelli ? 0.0 : wall);

            g.BeakerWall = ToField(wallMaterial);
            g.Source = ToField(sample.Material);
            g.Scene = GeometrySceneKind.None;
            HashSet<GdmlVolumeInstance> taken = this.used[GdmlParts.Sample];
            taken.Add(sample);
            string shape;
            if (marinelli)
            {
                Run well = holeWall > 0.0 ? along(inner - 0.5 * holeWall, 0.0, run => !run.Gas) : null;
                double ceiling = this.frontEdge + gap;                     // потолок колодца, от центра кристалла
                double mouth = well != null ? well.From : column.From;     // устье колодца
                g.SourceType = GeometrySourceType.Marinelli;
                g.MarinelliHoleShape = GeometryHoleShape.Round;
                g.MarinelliToDetectorDistance = R4(gap);
                g.MarinelliHoleEndWallThickness = R4(wall);
                g.MarinelliHoleSideThickness = R4(holeWall);
                g.MarinelliHoleDiameter = R4(2.0 * (inner - holeWall));
                g.MarinelliHoleHeight = R4(ceiling - mouth);
                g.MarinelliSourceHeight = R4(height);
                g.MarinelliSideThickness = R4(sideWall);
                g.MarinelliBeakerDiameter = R4(2.0 * (outer + sideWall));
                g.MarinelliEndWallThickness = R4(farWall);
                // Сосуд не короче «дальнее дно + пустота над пробой + проба»: конусная стенка обрывает луч раньше.
                g.MarinelliBeakerHeight = R4(Math.Max(body, farWall + farGap + height));
                shape = string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlMapMarinelli", "Marinelli ⌀{0} × {1}, well ⌀{2} × {3}, sample height {4}"),
                    N(g.MarinelliBeakerDiameter), N(g.MarinelliBeakerHeight), N(g.MarinelliHoleDiameter), N(g.MarinelliHoleHeight), N(g.MarinelliSourceHeight));
                this.sceneLateral = Math.Max(this.sceneLateral, outer + sideWall);
            }
            else
            {
                g.SourceType = GeometrySourceType.Cylinder;
                g.BeakerToDetectorDistance = R4(gap);
                g.BeakerEndWallThickness = R4(wall);
                g.BeakerSideWallThickness = R4(sideWall);
                g.BeakerDiameter = R4(2.0 * (outer + sideWall));
                g.SourceHeight = R4(cap);
                g.BeakerFarEndWallThickness = R4(farWall);
                g.BeakerHeight = R4(Math.Max(body, wall + cap + farWall));
                shape = string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlMapCylinderVessel", "cylinder ⌀{0} × {1}, sample height {2}, {3} from the detector"),
                    N(g.BeakerDiameter), N(g.BeakerHeight), N(g.SourceHeight), N(g.BeakerToDetectorDistance));
                this.sceneLateral = Math.Max(this.sceneLateral, outer + sideWall);
            }

            // Дальний край сцены — дальний край сосуда: у цилиндра стенка может идти выше пробы.
            this.sceneFront = marinelli ? sampleEnd + farGap + farWall : Math.Max(sampleEnd + farGap + farWall, this.frontEdge + gap + g.BeakerHeight);

            // Стенки сосуда: твёрдые тома вещества стенки вне прибора, задетые лучами или вмещающие пробу.
            if (wallMaterial != null)
            {
                this.usedMaterials[GdmlParts.Sample].Add(wallMaterial.Name);
                foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
                {
                    if (volume != null && volume.Solid != null && !IsGas(volume) && volume.MaterialName == wallMaterial.Name
                        && !this.used[GdmlParts.Detector].Contains(volume) && this.NearSample(volume, sample))
                    {
                        taken.Add(volume);
                    }
                }
            }

            this.Line(GdmlParts.Sample,
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapSample", "Sample {0}, {1}"), Display(sample.Material), GdmlOverview.SizeText(sample, this.geometry.Axis)),
                shape);
            this.Line(GdmlParts.Sample,
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapVessel", "Vessel {0}: towards the detector {1}, side {2}, far end {3}"),
                    wallMaterial == null ? "—" : Display(wallMaterial), N(wall), N(marinelli ? holeWall : sideWall), N(farWall)),
                marinelli
                    ? string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapMarinelliWalls", "well wall {0} / {1}, outer wall {2}, far end {3}"),
                        N(R4(holeWall)), N(R4(wall)), N(R4(sideWall)), N(R4(farWall)))
                    : string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapCylinderWalls", "near end {0}, side {1}, far end {2}"),
                        N(R4(wall)), N(R4(sideWall)), N(R4(farWall))));
            return true;
        }

        /// <summary>Том касается габарита пробы или вмещает его: стенка её сосуда, а не защита и не прибор.</summary>
        bool NearSample(GdmlVolumeInstance volume, GdmlVolumeInstance sample)
        {
            if (!volume.BoundsKnown)
            {
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                double room = 0.25 * (sample.Max[i] - sample.Min[i]) + 5.0;
                if (volume.Min[i] < sample.Min[i] - room || volume.Max[i] > sample.Max[i] + room)
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Защита
        // ------------------------------------------------------------------

        sealed class ShieldRay
        {
            public double First = -1.0;      // от начала луча до первой твёрдой грани защиты
            public readonly List<GdmlStackItem> Items = new List<GdmlStackItem>();
        }

        ShieldRay Beyond(double[] origin, double[] direction, double edge)
        {
            ShieldRay ray = new ShieldRay();
            List<Run> all = this.Runs(origin, direction, this.WorldReach(origin, direction) + 1.0);
            if (this.shieldFrom != null)
            {
                Run start = all.Find(run => ReferenceEquals(run.Volume, this.shieldFrom));
                if (start != null)
                {
                    edge = Math.Max(edge, start.From);
                }
            }

            foreach (Run run in all)
            {
                if (run.Gas || this.IsSample(run) || run.To <= edge + 1e-6)
                {
                    continue;
                }

                if (run.Volume != null && (this.used[GdmlParts.Detector].Contains(run.Volume) || this.used[GdmlParts.Sample].Contains(run.Volume)
                                           || this.InsideDevice(run.Volume) && this.deviceMin != null))
                {
                    continue;
                }

                double from = Math.Max(run.From, edge);
                if (ray.First < 0.0)
                {
                    ray.First = from;
                }

                ray.Items.Add(new GdmlStackItem { Material = run.Material, Name = run.Name, Thickness = run.To - from, Volume = run.Volume });
            }

            return ray;
        }

        bool LayShield()
        {
            // Вбок — от центра кристалла; вперёд и назад — вдоль оси рядом со сценой: по оси назад идёт сам прибор.
            ShieldRay side = this.Beyond(this.center, this.sideU, this.sceneLateral);
            ShieldRay other = this.Beyond(this.center, Neg(this.sideU), this.sceneLateral);
            ShieldRay third = this.Beyond(this.center, this.sideV, this.sceneLateral);
            if (side.First < 0.0 || other.First < 0.0 || third.First < 0.0
                || Math.Abs(side.First - other.First) > Tolerance * side.First + 0.5 || Math.Abs(side.First - third.First) > Tolerance * side.First + 0.5)
            {
                return false;
            }

            double offset = 0.5 * (this.sceneLateral + side.First);
            double[] beside = Add(this.center, this.sideU, offset);
            ShieldRay floor = this.Beyond(beside, this.front, 0.0);
            ShieldRay lid = this.Beyond(beside, Neg(this.front), 0.0);
            if (floor.First < 0.0 && lid.First < 0.0)
            {
                return false;
            }

            GeometryModel g = this.fields;
            g.InShield = true;
            g.ShieldInnerDiameter = R4(2.0 * side.First);
            double floorAt = floor.First >= 0.0 ? floor.First : this.sceneFront;
            double lidAt = lid.First >= 0.0 ? lid.First : this.backEdge;
            g.ShieldInnerHeight = R4(floorAt + lidAt);
            g.ShieldFloorGap = R4(Math.Max(0.0, floorAt - this.sceneFront));
            g.ShieldLayers = new List<GeometryShieldLayer>();
            List<string> order = new List<string>();
            Dictionary<string, GeometryShieldLayer> layers = new Dictionary<string, GeometryShieldLayer>(StringComparer.Ordinal);
            bool summed = false;
            Action<ShieldRay, Action<GeometryShieldLayer, double>> spread = (ray, put) =>
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                for (int at = 0; at < ray.Items.Count; at++)
                {
                    GdmlStackItem item = ray.Items[at];
                    GeometryShieldLayer layer;
                    if (!layers.TryGetValue(item.Name, out layer))
                    {
                        layer = new GeometryShieldLayer { Material = ToField(item.Material) };
                        layers[item.Name] = layer;

                        // Слой, которого нет на луче вбок, встаёт по месту на своём луче: перед ближайшим следующим
                        // за ним слоем, который уже известен (медь внутри кадмия — внутри, а не снаружи стали).
                        int place = order.Count;
                        for (int next = at + 1; next < ray.Items.Count; next++)
                        {
                            int known = order.IndexOf(ray.Items[next].Name);
                            if (known >= 0)
                            {
                                place = known;
                                break;
                            }
                        }

                        order.Insert(place, item.Name);
                    }

                    summed |= !seen.Add(item.Name);
                    put(layer, item.Thickness);
                    if (item.Volume != null)
                    {
                        this.usedMaterials[GdmlParts.Shield].Add(item.Name);
                    }
                }
            };
            spread(side, (layer, t) => layer.SideThickness = R4(layer.SideThickness + t));
            spread(floor, (layer, t) => layer.FloorThickness = R4(layer.FloorThickness + t));
            spread(lid, (layer, t) => layer.LidThickness = R4(layer.LidThickness + t));
            List<string> shown = new List<string>();
            foreach (string name in order)
            {
                g.ShieldLayers.Add(layers[name]);
                shown.Add(layers[name].Material.Name);
            }

            this.ShieldMaterials = string.Join(", ", shown.ToArray());

            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume != null && volume.Solid != null && !IsGas(volume) && this.usedMaterials[GdmlParts.Shield].Contains(volume.MaterialName)
                    && !this.used[GdmlParts.Detector].Contains(volume) && !this.used[GdmlParts.Sample].Contains(volume)
                    && !(this.deviceMin != null && this.InsideDevice(volume)))
                {
                    this.used[GdmlParts.Shield].Add(volume);
                }
            }

            this.ShieldParts = this.used[GdmlParts.Shield].Count;
            this.Line(GdmlParts.Shield,
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapShieldCavity", "Cavity: {0} mm across, {1} mm along the axis"),
                    N(2.0 * side.First), N(floorAt + lidAt)),
                string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapShieldCavityFields", "cavity ⌀{0} × {1}, {2} beyond the scene"),
                    N(g.ShieldInnerDiameter), N(g.ShieldInnerHeight), N(g.ShieldFloorGap)));
            foreach (GeometryShieldLayer layer in g.ShieldLayers)
            {
                this.Line(GdmlParts.Shield,
                    string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapShieldLayer", "Layer {0}"), layer.Material.Name),
                    string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapShieldLayerFields", "side {0}, beyond the sample {1}, behind the detector {2}"),
                        N(layer.SideThickness), N(layer.FloorThickness), N(layer.LidThickness)));
            }

            this.Simplify(GdmlParts.Shield, GdmlText.T("GdmlSimplShield",
                "The shield is a set of nested cylinders: thicknesses are measured at the side, beyond the sample and behind the detector."));
            if (summed)
            {
                this.Simplify(GdmlParts.Shield, GdmlText.T("GdmlSimplShieldSummed", "Shield layers of the same material are added together."));
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Итог
        // ------------------------------------------------------------------

        /// <summary>Положить выбранные части в модель; прочие поля модели не трогаются.</summary>
        public void Apply(GeometryModel g, GdmlParts parts)
        {
            parts &= this.Found;
            GeometryModel f = this.fields;
            if ((parts & GdmlParts.Detector) != 0)
            {
                g.IsScintillator = true;
                g.Shape = f.Shape;
                g.Facing = f.Facing;
                g.CrystalDiameter = f.CrystalDiameter;
                g.CrystalHeight = f.CrystalHeight;
                g.CrystalBoxX = f.CrystalBoxX;
                g.CrystalBoxY = f.CrystalBoxY;
                g.CrystalBoxZ = f.CrystalBoxZ;
                g.FrontReflectorThickness = f.FrontReflectorThickness;
                g.SideReflectorThickness = f.SideReflectorThickness;
                g.FrontGapThickness = f.FrontGapThickness;
                g.SideGapThickness = f.SideGapThickness;
                g.FrontCladdingThickness = f.FrontCladdingThickness;
                g.SideCladdingThickness = f.SideCladdingThickness;
                g.MountingThickness = f.MountingThickness;
                g.Crystal = f.Crystal.Clone();
                Take(f.Reflector, m => g.Reflector = m);
                Take(f.Gap, m => g.Gap = m);
                Take(f.Cladding, m => g.Cladding = m);
                g.DropDeadCrystalSize();
            }

            if ((parts & GdmlParts.Sample) != 0)
            {
                g.Scene = GeometrySceneKind.None;
                g.SourceType = f.SourceType;
                g.Source = f.Source.Clone();
                Take(f.BeakerWall, m => g.BeakerWall = m);
                if (f.SourceType == GeometrySourceType.Marinelli)
                {
                    g.MarinelliHoleShape = GeometryHoleShape.Round;
                    g.MarinelliToDetectorDistance = f.MarinelliToDetectorDistance;
                    g.MarinelliHoleEndWallThickness = f.MarinelliHoleEndWallThickness;
                    g.MarinelliHoleSideThickness = f.MarinelliHoleSideThickness;
                    g.MarinelliHoleDiameter = f.MarinelliHoleDiameter;
                    g.MarinelliHoleHeight = f.MarinelliHoleHeight;
                    g.MarinelliSourceHeight = f.MarinelliSourceHeight;
                    g.MarinelliSideThickness = f.MarinelliSideThickness;
                    g.MarinelliBeakerDiameter = f.MarinelliBeakerDiameter;
                    g.MarinelliEndWallThickness = f.MarinelliEndWallThickness;
                    g.MarinelliBeakerHeight = f.MarinelliBeakerHeight;
                }
                else
                {
                    g.BeakerToDetectorDistance = f.BeakerToDetectorDistance;
                    g.BeakerEndWallThickness = f.BeakerEndWallThickness;
                    g.BeakerSideWallThickness = f.BeakerSideWallThickness;
                    g.BeakerDiameter = f.BeakerDiameter;
                    g.SourceHeight = f.SourceHeight;
                    g.BeakerFarEndWallThickness = f.BeakerFarEndWallThickness;
                    g.BeakerHeight = f.BeakerHeight;
                }
            }

            if ((parts & GdmlParts.Shield) != 0)
            {
                g.InShield = true;
                g.ShieldInnerDiameter = f.ShieldInnerDiameter;
                g.ShieldInnerHeight = f.ShieldInnerHeight;
                // Зазор до дна отсчитан от дальнего края пробы ФАЙЛА. Проба задаётся вручную — её края файл не знает:
                // сцена ставится на дно защиты (зазор 0), полость и слои — как в файле.
                g.ShieldFloorGap = (parts & GdmlParts.Sample) != 0 ? f.ShieldFloorGap : 0.0;
                g.ShieldLayers = new List<GeometryShieldLayer>();
                foreach (GeometryShieldLayer layer in f.ShieldLayers)
                {
                    g.ShieldLayers.Add(new GeometryShieldLayer
                    {
                        Material = layer.Material.Clone(), SideThickness = layer.SideThickness,
                        FloorThickness = layer.FloorThickness, LidThickness = layer.LidThickness,
                    });
                }
            }
        }

        static void Take(GeometryMaterial material, Action<GeometryMaterial> put)
        {
            // Слоя в файле нет (толщина 0) — вещество поля остаётся прежним.
            if (material != null && !material.IsEmpty)
            {
                put(material.Clone());
            }
        }

        /// <summary>Вошёл ли том в поля при таком выборе частей.</summary>
        public bool IsUsed(GdmlVolumeInstance volume, GdmlParts parts)
        {
            foreach (GdmlParts part in new[] { GdmlParts.Detector, GdmlParts.Sample, GdmlParts.Shield })
            {
                if ((parts & this.Found & part) != 0 && this.used[part].Contains(volume))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>«Не используется в геометрии»: твёрдые детали файла, не вошедшие в поля, тяжёлые первыми.</summary>
        public List<GdmlVolumeInstance> Unused(GdmlParts parts)
        {
            List<GdmlVolumeInstance> result = new List<GdmlVolumeInstance>();
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume != null && volume.Solid != null && !IsGas(volume) && !this.overview.IsOutOfFrame(volume) && !this.IsUsed(volume, parts))
                {
                    result.Add(volume);
                }
            }

            result.Sort((a, b) => (double.IsNaN(b.MassG) ? -1.0 : b.MassG).CompareTo(double.IsNaN(a.MassG) ? -1.0 : a.MassG));
            return result;
        }

        /// <summary>Сетки, по которым луч не дал замкнутого сечения: их толщина в поля не вошла.</summary>
        public ICollection<string> OpenMeshes
        {
            get { return this.locator.OpenMeshes; }
        }
    }
}
