using BecquerelMonitor.Properties;
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

    /// <summary>Форма кристалла.</summary>
    public enum CrystalShape
    {
        Cylinder,
        /// <summary>Прямоугольный параллелепипед: длинная сторона вдоль оси.</summary>
        Box
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

        /// <summary>
        /// Форма кристалла. Формат `.in` конструктора геометрий LSRM умеет
        /// только цилиндры, и прямоугольные сцинтилляторы там приводят к
        /// цилиндру равного объёма. Это не безобидно: равный объём и даже
        /// равная площадь торца не дают равной СРЕДНЕЙ ХОРДЫ, а именно она
        /// задаёт вероятность взаимодействия при боковом облучении. У ASN16
        /// параллелепипед 1.5x1.8x6.0 имеет хорду 4V/S = 1.440 см против
        /// 1.602 см у равнообъёмного цилиндра — на 10 % тоньше, и в стакане
        /// Маринелли, где кванты идут сбоку, цилиндр завышает эффективность.
        ///
        /// Читается из необязательных ключей DS_CrystalBoxX/Y/Z (наше
        /// расширение формата; в файлах LSRM их нет, и тогда форма
        /// цилиндрическая).
        /// </summary>
        public CrystalShape Shape = CrystalShape.Cylinder;

        public double CrystalBoxX;
        public double CrystalBoxY;
        public double CrystalBoxZ;
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

        /// <summary>
        /// Все пары «ключ = значение» разобранного файла как есть.
        ///
        /// Нужны при ЗАПИСИ: редактор правит сцинтилляционную ветвь, а в файле
        /// есть ещё коаксиальная и описания воздуха, которые мы не читаем и не
        /// показываем. Перегенерировать их из ничего значило бы подменить чужие
        /// числа своими умолчаниями, поэтому они переносятся отсюда дословно.
        /// </summary>
        public readonly Dictionary<string, string> Raw =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
            foreach (KeyValuePair<string, string> pair in kv)
            {
                g.Raw[pair.Key] = pair.Value;
            }

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

            g.CrystalBoxX = Num(kv, "DS_CrystalBoxX");
            g.CrystalBoxY = Num(kv, "DS_CrystalBoxY");
            g.CrystalBoxZ = Num(kv, "DS_CrystalBoxZ");
            if (g.CrystalBoxX > 0.0 && g.CrystalBoxY > 0.0 && g.CrystalBoxZ > 0.0)
            {
                g.Shape = CrystalShape.Box;
            }

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

            // Ключ типа долей у отражателя называется DS_FractionTypeReflector,
            // без Crystal, — в отличие от остальных. Так в формате.
            g.Crystal = Material(kv, "DS_", "Crystal", "M_DS_Crystal.MName",
                                 "DS_FractionTypeCrystal", g.Warnings);
            g.Reflector = Material(kv, "DS_", "CrystalReflector", "M_DS_Reflector.MName",
                                   "DS_FractionTypeReflector", g.Warnings);
            g.Cladding = Material(kv, "DS_", "CrystalCladding", "M_DS_Crystal_Cladding.MName",
                                  "DS_FractionTypeCrystalCladding", g.Warnings);

            string prefix = g.SourceType == GeometrySourceType.Marinelli ? "SM_" : "SC_";
            g.BeakerWall = Material(kv, prefix, "Wall", "M_" + prefix + "Beaker.MName",
                                    prefix + "FractionTypeWall", g.Warnings);
            g.Source = Material(kv, prefix, "Source", "M_" + prefix + "Source.MName",
                                prefix + "FractionTypeSource", g.Warnings);
            g.CheckLayers();
            return g;
        }

        /// <summary>
        /// Что в разобранном файле выглядит подозрительно. Пусто, если всё ясно.
        ///
        /// Заводится не «на всякий случай»: у обеих проверок ниже есть читатель
        /// — расчёт печатает это в журнал прогона, а конструктор кривой в свой.
        /// </summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>
        /// Слой с толщиной, но без вещества. Разбирать это молча нельзя: области
        /// сцены вложены и ищутся по порядку, поэтому слой без плотности не
        /// исчезает, а ЗАМЕЩАЕТСЯ слоем снаружи — забыл плотность отражателя, и
        /// на его месте оказался алюминий корпуса, который тяжелее. Расчёт при
        /// этом доводится до конца и выдаёт правдоподобную, но чужую кривую.
        /// </summary>
        void CheckLayers()
        {
            Action<double, GeometryMaterial, string> check = (thickness, material, caption) =>
            {
                if (thickness > 0.0 && (material == null || !(material.Density > 0.0)
                                        || material.Fractions.Count == 0))
                {
                    this.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        Properties.Resources.GeometryWarningNoMaterial, caption, thickness));
                }
            };

            check(Math.Max(this.FrontReflectorThickness, this.SideReflectorThickness),
                  this.Reflector, Properties.Resources.GeometryEditorReflectorMaterial);
            check(Math.Max(this.FrontCladdingThickness, this.SideCladdingThickness),
                  this.Cladding, Properties.Resources.GeometryEditorCladdingMaterial);

            double wall = this.SourceType == GeometrySourceType.Marinelli
                ? Math.Max(this.MarinelliSideThickness, this.MarinelliHoleSideThickness)
                : Math.Max(this.BeakerSideWallThickness, this.BeakerEndWallThickness);
            check(wall, this.BeakerWall, Properties.Resources.GeometryEditorWallMaterial);

            double sample = this.SourceType == GeometrySourceType.Marinelli
                ? this.MarinelliSourceHeight
                : this.SourceType == GeometrySourceType.Cylinder ? this.SourceHeight : 0.0;
            check(sample, this.Source, Properties.Resources.GeometryEditorSourceMaterial);
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
        ///
        /// Тип долей задан ключом `<prefix>FractionType<part>`. Во всех восьми
        /// поставочных файлах он MASS — так же, как подписана колонка «Weight
        /// fract» в редакторе материалов LSRM. Но ATOM формат допускает, и
        /// прочитать атомные доли как массовые значит посчитать неверно и
        /// молча: у иодида цезия атомные 0.5/0.5 против массовых 0.488/0.512,
        /// а у чего-нибудь вроде Bi4Ge3O12 разница уже в разы. Поэтому ATOM
        /// пересчитывается в массовые, а незнакомое значение — повод сказать.
        /// </summary>
        static GeometryMaterial Material(Dictionary<string, string> kv, string prefix,
                                         string part, string nameKey, string fractionTypeKey,
                                         List<string> warnings)
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

            string type = Get(kv, fractionTypeKey).Trim().ToUpperInvariant();
            if (type.StartsWith("ATOM"))
            {
                ToMassFractions(m);
                if (warnings != null)
                {
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        Properties.Resources.GeometryWarningAtomFractions,
                        m.Name.Length > 0 ? m.Name : part));
                }
            }
            else if (type.Length > 0 && !type.StartsWith("MASS") && warnings != null)
            {
                warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    Properties.Resources.GeometryWarningFractionType,
                    m.Name.Length > 0 ? m.Name : part, type));
            }

            return m;
        }

        /// <summary>
        /// Атомные доли -> массовые: доля умножается на атомную массу и всё
        /// нормируется заново. Обратное преобразование делать нечем и незачем —
        /// весь расчёт ослабления стоит на массовых долях (правило Брэгга).
        /// </summary>
        static void ToMassFractions(GeometryMaterial m)
        {
            Dictionary<int, double> mass = new Dictionary<int, double>();
            double total = 0.0;
            foreach (KeyValuePair<int, double> pair in m.Fractions)
            {
                double atomic;
                if (!AttenuationData.AtomicMass.TryGetValue(pair.Key, out atomic) || !(atomic > 0.0))
                {
                    // Элемента нет в таблице масс — пересчитать нечем; оставляем
                    // состав как есть, о самом элементе скажет IsKnown.
                    return;
                }

                double weight = pair.Value * atomic;
                mass[pair.Key] = weight;
                total += weight;
            }

            if (!(total > 0.0))
            {
                return;
            }

            m.Fractions.Clear();
            foreach (KeyValuePair<int, double> pair in mass)
            {
                m.Fractions[pair.Key] = pair.Value / total;
            }
        }

        /// <summary>
        /// Разбор геометрии одной строкой. Строка попадает в журнал прогона в
        /// окне конструктора кривой, поэтому она переводится: раньше была
        /// жёстко по-русски и в английском интерфейсе выглядела чужой.
        /// </summary>
        public string Describe()
        {
            string source;
            switch (this.SourceType)
            {
                case GeometrySourceType.Point:
                    source = string.Format(CultureInfo.InvariantCulture,
                        Resources.GeometrySourcePoint, this.PointDistance);
                    break;
                case GeometrySourceType.Cylinder:
                    source = string.Format(CultureInfo.InvariantCulture,
                        Resources.GeometrySourceCylinder, this.BeakerDiameter,
                        this.SourceHeight, this.BeakerToDetectorDistance);
                    break;
                default:
                    source = string.Format(CultureInfo.InvariantCulture,
                        Resources.GeometrySourceMarinelli,
                        this.MarinelliBeakerDiameter, this.MarinelliHoleDiameter,
                        this.MarinelliSourceHeight, this.MarinelliToDetectorDistance);
                    break;
            }

            string crystal = this.Shape == CrystalShape.Box
                ? string.Format(CultureInfo.InvariantCulture, Resources.GeometryCrystalBox,
                                this.CrystalBoxX, this.CrystalBoxY, this.CrystalBoxZ)
                : string.Format(CultureInfo.InvariantCulture, Resources.GeometryCrystalCylinder,
                                this.CrystalDiameter, this.CrystalHeight);

            return string.Format(CultureInfo.InvariantCulture, Resources.GeometryDescription,
                this.Name, this.Crystal.Name, crystal, this.Crystal.Density, source, this.Source.Name);
        }
    }
}
