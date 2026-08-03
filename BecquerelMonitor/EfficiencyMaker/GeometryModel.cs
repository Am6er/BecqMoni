using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Вещество: массовые доли элементов и плотность. Массовый коэффициент
    /// ослабления смеси — сумма по элементам с массовыми весами (правило
    /// аддитивности Брэгга).
    /// </summary>
    public sealed class GeometryMaterial
    {
        public string Name = "";

        public double Density;                       // г/см3

        /// <summary>Z -> массовая доля.</summary>
        public readonly Dictionary<int, double> Fractions = new Dictionary<int, double>();

        /// <summary>Линейный коэффициент ослабления, 1/см.</summary>
        public double LinearAttenuation(double energyKev)
        {
            double massAttenuation = 0.0;
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                massAttenuation += pair.Value * AttenuationData.MassAttenuation(pair.Key, energyKev);
            }

            return massAttenuation * this.Density;
        }

        /// <summary>Электронов на см³ — для сечения Клейна — Нишины.</summary>
        public double ElectronDensity()
        {
            const double Avogadro = 6.02214076e23;
            double perGram = 0.0;
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                double mass;
                if (!AttenuationData.AtomicMass.TryGetValue(pair.Key, out mass) || !(mass > 0.0))
                {
                    continue;
                }

                perGram += pair.Value * pair.Key * Avogadro / mass;
            }

            return perGram * this.Density;
        }

        /// <summary>Все ли элементы вещества есть в таблице ослабления.</summary>
        public bool IsKnown(out int missingZ)
        {
            foreach (KeyValuePair<int, double> pair in this.Fractions)
            {
                if (pair.Value > 0.0 && !AttenuationData.HasElement(pair.Key))
                {
                    missingZ = pair.Key;
                    return false;
                }
            }

            missingZ = 0;
            return true;
        }
    }

    public enum GeometrySourceType
    {
        Point,
        Cylinder,
        Marinelli
    }

    /// <summary>
    /// Модель геометрии из файла `.in` конструктора геометрий LSRM
    /// (GeometryMaster). Формат — плоский список `ключ = значение единица`
    /// с комментариями `//`; в файле присутствуют ВСЕ блоки (коаксиальный и
    /// сцинтилляционный детектор, три типа источника), а работает тот, что
    /// назван в DetectorType и SourceType.
    ///
    /// Разбирается сцинтилляционная ветвь: коаксиальные детекторы (HPGe) вне
    /// предмета — там пик разрешается сам, и задача другая.
    /// </summary>
    public sealed class GeometryModel
    {
        public string Name = "";

        public bool IsScintillator;

        public GeometrySourceType SourceType;

        // Кристалл, см
        public double CrystalDiameter;
        public double CrystalHeight;
        public double FrontReflectorThickness;
        public double SideReflectorThickness;
        public double FrontCladdingThickness;
        public double SideCladdingThickness;
        public double MountingThickness;

        // Источник, см
        public double PointDistance;

        public double BeakerToDetectorDistance;
        public double BeakerDiameter;
        public double BeakerHeight;
        public double BeakerSideWallThickness;
        public double BeakerEndWallThickness;
        public double SourceHeight;

        public double MarinelliBeakerDiameter;
        public double MarinelliBeakerHeight;
        public double MarinelliHoleDiameter;
        public double MarinelliHoleHeight;
        public double MarinelliSideThickness;
        public double MarinelliEndWallThickness;
        public double MarinelliHoleSideThickness;
        public double MarinelliHoleEndWallThickness;
        public double MarinelliSourceHeight;
        public double MarinelliToDetectorDistance;

        public GeometryMaterial Crystal = new GeometryMaterial();
        public GeometryMaterial Reflector = new GeometryMaterial();
        public GeometryMaterial Cladding = new GeometryMaterial();
        public GeometryMaterial BeakerWall = new GeometryMaterial();
        public GeometryMaterial Source = new GeometryMaterial();

        static readonly Regex Line = new Regex(@"^\s*([A-Za-z_][A-Za-z0-9_\[\]\.]*)\s*=\s*(.+?)\s*$",
                                               RegexOptions.Compiled);

        public static GeometryModel Load(string path)
        {
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(path))
            {
                int comment = raw.IndexOf("//", StringComparison.Ordinal);
                string text = comment >= 0 ? raw.Substring(0, comment) : raw;
                Match m = Line.Match(text);
                if (m.Success)
                {
                    kv[m.Groups[1].Value] = m.Groups[2].Value;
                }
            }

            GeometryModel g = new GeometryModel();
            g.Name = Path.GetFileNameWithoutExtension(path);
            g.IsScintillator = Get(kv, "DetectorType").IndexOf("SCINT", StringComparison.OrdinalIgnoreCase) >= 0;

            string source = Get(kv, "SourceType").ToUpperInvariant();
            g.SourceType = source.StartsWith("MARINELLI") ? GeometrySourceType.Marinelli
                : source.StartsWith("CYLINDER") ? GeometrySourceType.Cylinder
                : GeometrySourceType.Point;

            g.CrystalDiameter = Num(kv, "DS_CrystalDiameter");
            g.CrystalHeight = Num(kv, "DS_CrystalHeight");
            g.FrontReflectorThickness = Num(kv, "DS_CrystalFrontReflectorThickness");
            g.SideReflectorThickness = Num(kv, "DS_CrystalSideReflectorThickness");
            g.FrontCladdingThickness = Num(kv, "DS_CrystalFrontCladdingThickness");
            g.SideCladdingThickness = Num(kv, "DS_CrystalSideCladdingThickness");
            g.MountingThickness = Num(kv, "DS_DetectorMountingThickness");

            g.PointDistance = Num(kv, "pdistance");

            g.BeakerToDetectorDistance = Num(kv, "SC_BeakerToDetectorFrontDistance");
            g.BeakerDiameter = Num(kv, "SC_BeakerDiameter");
            g.BeakerHeight = Num(kv, "SC_BeakerHeight");
            g.BeakerSideWallThickness = Num(kv, "SC_BeakerSideWallThickness");
            g.BeakerEndWallThickness = Num(kv, "SC_BeakerEndWallThickness");
            g.SourceHeight = Num(kv, "SC_SourceHeight");

            g.MarinelliBeakerDiameter = Num(kv, "SM_BeakerDiameter");
            g.MarinelliBeakerHeight = Num(kv, "SM_BeakerHeight");
            g.MarinelliHoleDiameter = Num(kv, "SM_BeakerHoleDiameter");
            g.MarinelliHoleHeight = Num(kv, "SM_BeakerHoleHeight");
            g.MarinelliSideThickness = Num(kv, "SM_BeakerSideThickness");
            g.MarinelliEndWallThickness = Num(kv, "SM_BeakerEndWallThickness");
            g.MarinelliHoleSideThickness = Num(kv, "SM_BeakerHoleSideThickness");
            g.MarinelliHoleEndWallThickness = Num(kv, "SM_BeakerHoleEndWallThickness");
            g.MarinelliSourceHeight = Num(kv, "SM_SourceHeight");
            // У Маринелли своё расстояние до детектора: в файле есть оба ключа,
            // и брать цилиндрический для маринеллевской геометрии нельзя.
            g.MarinelliToDetectorDistance = Num(kv, "SM_BeakerToDetectorFrontDistance");

            g.Crystal = Material(kv, "DS_", "Crystal", "M_DS_Crystal.MName");
            g.Reflector = Material(kv, "DS_", "CrystalReflector", "M_DS_Reflector.MName");
            g.Cladding = Material(kv, "DS_", "CrystalCladding", "M_DS_Crystal_Cladding.MName");

            string prefix = g.SourceType == GeometrySourceType.Marinelli ? "SM_" : "SC_";
            g.BeakerWall = Material(kv, prefix, "Wall", "M_" + prefix + "Beaker.MName");
            g.Source = Material(kv, prefix, "Source", "M_" + prefix + "Source.MName");
            return g;
        }

        static string Get(Dictionary<string, string> kv, string key)
        {
            string v;
            return kv.TryGetValue(key, out v) ? v : "";
        }

        /// <summary>Значение с единицей: «5.03 cm» -> 5.03. Единица всегда см.</summary>
        static double Num(Dictionary<string, string> kv, string key)
        {
            string v = Get(kv, key);
            if (v.Length == 0)
            {
                return 0.0;
            }

            Match m = Regex.Match(v, @"^\s*(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)");
            double value;
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                                                CultureInfo.InvariantCulture, out value)
                ? value : 0.0;
        }

        /// <summary>
        /// Вещество собирается из троек, разложенных по файлу:
        /// `<prefix>Ro<part>` — плотность, `<prefix>Z<part>[i]` — номер элемента,
        /// `<prefix>Fractions<part>[i]` — его массовая доля.
        /// </summary>
        static GeometryMaterial Material(Dictionary<string, string> kv, string prefix,
                                         string part, string nameKey)
        {
            GeometryMaterial m = new GeometryMaterial();
            m.Name = Get(kv, nameKey).Trim();
            m.Density = Num(kv, prefix + "Ro" + part);
            for (int i = 0; i < 24; i++)
            {
                string zKey = prefix + "Z" + part + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string fKey = prefix + "Fractions" + part + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                if (!kv.ContainsKey(zKey))
                {
                    continue;
                }

                int z = (int)Num(kv, zKey);
                double fraction = Num(kv, fKey);
                if (z > 0 && fraction > 0.0)
                {
                    double have;
                    m.Fractions.TryGetValue(z, out have);
                    m.Fractions[z] = have + fraction;
                }
            }

            return m;
        }

        public string Describe()
        {
            string source;
            switch (this.SourceType)
            {
                case GeometrySourceType.Point:
                    source = string.Format(CultureInfo.InvariantCulture, "точка, {0:F2} см", this.PointDistance);
                    break;
                case GeometrySourceType.Cylinder:
                    source = string.Format(CultureInfo.InvariantCulture,
                        "цилиндр D={0:F2} H={1:F2} на {2:F2} см", this.BeakerDiameter,
                        this.SourceHeight, this.BeakerToDetectorDistance);
                    break;
                default:
                    source = string.Format(CultureInfo.InvariantCulture,
                        "Маринелли D={0:F2} Dhole={1:F2} H={2:F2} на {3:F2} см",
                        this.MarinelliBeakerDiameter, this.MarinelliHoleDiameter,
                        this.MarinelliSourceHeight, this.MarinelliToDetectorDistance);
                    break;
            }

            return string.Format(CultureInfo.InvariantCulture,
                "{0}: кристалл {1} {2:F3}x{3:F3} см, ро {4:F3}; источник — {5}, {6}",
                this.Name, this.Crystal.Name, this.CrystalDiameter, this.CrystalHeight,
                this.Crystal.Density, source, this.Source.Name);
        }
    }
}
