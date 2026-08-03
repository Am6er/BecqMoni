using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Запись файла геометрии `.in`.
    ///
    /// Файл собирается ЦЕЛИКОМ, в том же порядке и с теми же комментариями, что
    /// у конструктора геометрий LSRM, — чтобы его открывал не только наш расчёт,
    /// но и GMaster. Поэтому пишутся и блоки, которых мы не читаем вовсе
    /// (коаксиальный детектор, воздух): их значения переносятся из исходного
    /// файла (<see cref="GeometryModel.Raw"/>), а если файла не было — берутся
    /// умолчания, помеченные ниже.
    ///
    /// Имена ключей в формате нерегулярны, и это не описка: у отражателя тип
    /// долей называется `DS_FractionTypeReflector`, а не `...CrystalReflector`,
    /// как у остальных, а счётчик элементов вакуума — `DC_nVacuum`, без
    /// `Elements`. Таблица слоёв ниже повторяет формат буква в букву; всякая
    /// «нормализация» здесь ломает чтение файла их программой.
    /// </summary>
    public static class GeometryWriter
    {
        /// <summary>Описание одного вещества в файле: как называются его ключи.</summary>
        sealed class Slot
        {
            public string CountKey;
            public string RoKey;
            public string ZPart;
            public string FractionsPart;
            public string FractionTypeKey;
            public string NamePrefix;
        }

        static readonly Slot DsCrystal = new Slot
        {
            CountKey = "DS_nCrystalElements", RoKey = "DS_RoCrystal",
            ZPart = "DS_ZCrystal", FractionsPart = "DS_FractionsCrystal",
            FractionTypeKey = "DS_FractionTypeCrystal", NamePrefix = "M_DS_Crystal",
        };

        static readonly Slot DsCladding = new Slot
        {
            CountKey = "DS_nCrystalCladdingElements", RoKey = "DS_RoCrystalCladding",
            ZPart = "DS_ZCrystalCladding", FractionsPart = "DS_FractionsCrystalCladding",
            FractionTypeKey = "DS_FractionTypeCrystalCladding", NamePrefix = "M_DS_Crystal_Cladding",
        };

        static readonly Slot DsReflector = new Slot
        {
            CountKey = "DS_nCrystalReflectorElements", RoKey = "DS_RoCrystalReflector",
            ZPart = "DS_ZCrystalReflector", FractionsPart = "DS_FractionsCrystalReflector",
            // именно Reflector, без Crystal — так в формате
            FractionTypeKey = "DS_FractionTypeReflector", NamePrefix = "M_DS_Reflector",
        };

        static Slot Wall(string prefix)
        {
            return new Slot
            {
                CountKey = prefix + "_nWallElements", RoKey = prefix + "_RoWall",
                ZPart = prefix + "_ZWall", FractionsPart = prefix + "_FractionsWall",
                FractionTypeKey = prefix + "_FractionTypeWall", NamePrefix = "M_" + prefix + "_Beaker",
            };
        }

        static Slot SourceSlot(string prefix)
        {
            return new Slot
            {
                CountKey = prefix + "_nSourceElements", RoKey = prefix + "_RoSource",
                ZPart = prefix + "_ZSource", FractionsPart = prefix + "_FractionsSource",
                FractionTypeKey = prefix + "_FractionTypeSource", NamePrefix = "M_" + prefix + "_Source",
            };
        }

        static Slot EmptySpace(string prefix)
        {
            return new Slot
            {
                CountKey = prefix + "_nEmptySpaceElements", RoKey = prefix + "_RoEmptySpace",
                ZPart = prefix + "_ZEmptySpace", FractionsPart = prefix + "_FractionsEmptySpace",
                FractionTypeKey = prefix + "_FractionTypeEmptySpace", NamePrefix = "M_" + prefix + "_EmptySpace",
            };
        }

        public static void Save(GeometryModel model, string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Кодировка та же, что у файлов LSRM: однобайтная кириллица в
            // комментариях. UTF-8 их программа не ждёт.
            File.WriteAllText(path, Render(model), Encoding.GetEncoding(1251));
        }

        public static string Render(GeometryModel model)
        {
            StringBuilder text = new StringBuilder();
            Action<string> line = value => text.Append(value).Append("\r\n");
            Action<string, double> cm = (key, value) => line(string.Format(
                CultureInfo.InvariantCulture, "{0} = {1} cm", key, Trim(value)));

            line("//---------------------------------");
            line("//DETECTOR PARAMETERS BLOCK:");
            line("//---------------------------------");
            line("//Detector types: COAXIAL, SCINTILLATOR");
            line("");
            line("");
            line("DetectorType = SCINTILLATOR");
            line("");
            line("// Coaxial detector");
            foreach (string key in CoaxialKeys)
            {
                cm(key, Carried(model, key));
            }

            line("");
            line("// Scintillator detector");
            // Габариты цилиндра. У прямоугольного кристалла они не выдумываются,
            // а считаются по правилу самого LSRM — равная площадь торца
            // D = 2*sqrt(X*Y/pi), высота = длина бруска. Так файл остаётся
            // осмысленным и для GMaster, который бруска не знает.
            double diameter = model.CrystalDiameter;
            double height = model.CrystalHeight;
            if (model.Shape == CrystalShape.Box)
            {
                diameter = EquivalentDiameter(model.CrystalBoxX, model.CrystalBoxY);
                height = model.CrystalBoxZ;
            }

            cm("DS_CrystalDiameter", diameter);
            cm("DS_CrystalHeight", height);
            cm("DS_CrystalFrontReflectorThickness", model.FrontReflectorThickness);
            cm("DS_CrystalSideReflectorThickness", model.SideReflectorThickness);
            cm("DS_CrystalFrontCladdingThickness", model.FrontCladdingThickness);
            cm("DS_CrystalSideCladdingThickness", model.SideCladdingThickness);
            cm("DS_DetectorMountingThickness", model.MountingThickness);
            line("");
            line("");
            line("");
            line("//---------------------------------");
            line("//SOURCE PARAMETERS BLOCK:");
            line("//---------------------------------");
            line("//Source types: POINT, CYLINDER, MARINELLI");
            line("");
            line("SourceType = " + (model.SourceType == GeometrySourceType.Marinelli ? "MARINELLI"
                                    : model.SourceType == GeometrySourceType.Cylinder ? "CYLINDER" : "POINT"));
            line("");
            line("//Point source");
            cm("pdistance", model.PointDistance);
            line("");
            line("//Cylindrical source");
            cm("SC_BeakerToDetectorFrontDistance", model.BeakerToDetectorDistance);
            cm("SC_BeakerDiameter", model.BeakerDiameter);
            cm("SC_BeakerHeight", model.BeakerHeight);
            cm("SC_BeakerSideWallThickness", model.BeakerSideWallThickness);
            cm("SC_BeakerEndWallThickness", model.BeakerEndWallThickness);
            cm("SC_SourceHeight", model.SourceHeight);
            line("");
            line("//Marinelli beaker source");
            cm("SM_BeakerToDetectorFrontDistance", model.MarinelliToDetectorDistance);
            cm("SM_BeakerDiameter", model.MarinelliBeakerDiameter);
            cm("SM_BeakerHeight", model.MarinelliBeakerHeight);
            cm("SM_BeakerHoleDiameter", model.MarinelliHoleDiameter);
            cm("SM_BeakerHoleHeight", model.MarinelliHoleHeight);
            cm("SM_BeakerSideThickness", model.MarinelliSideThickness);
            cm("SM_BeakerEndWallThickness", model.MarinelliEndWallThickness);
            cm("SM_BeakerHoleSideThickness", model.MarinelliHoleSideThickness);
            cm("SM_BeakerHoleEndWallThickness", model.MarinelliHoleEndWallThickness);
            cm("SM_SourceHeight", model.MarinelliSourceHeight);
            line("");
            line("");
            line("//---------------------------------");
            line("//MATERIAL PARAMETERS BLOCK:");
            line("//---------------------------------");
            line("//Coaxial detector materials:");
            line("//---------------------------");
            line("");
            line("");
            line("// Crystal");
            Carry(text, model, "DC_nCrystalElements", "DC_RoCrystal", "DC_ZCrystal",
                  "DC_FractionsCrystal", "DC_FractionTypeCrystal", "M_DC_Crystal",
                  "Germanium", 5.323, "Ge1");
            line("");
            line("");
            line("// Crystal Cladding");
            Carry(text, model, "DC_nCrystalSideCladdingElements", "DC_RoCrystalSideCladding",
                  "DC_ZCrystalSideCladding", "DC_FractionsCrystalSideCladding",
                  "DC_FractionTypeCrystalSideCladding", "M_DC_Crystal_Cladding",
                  "Aluminum", 2.7, "Al1");
            line("");
            line("");
            line("//Crystal Mounting");
            Carry(text, model, "DC_nCrystalMountingElements", "DC_RoCrystalMounting",
                  "DC_ZCrystalMounting", "DC_FractionsCrystalMounting",
                  "DC_FractionTypeCrystalMounting", "M_DC_Crystal_Mounting",
                  "Aluminum", 2.7, "Al1");
            line("");
            line("");
            line("//Detector Cap");
            Carry(text, model, "DC_nDetectorCapElements", "DC_RoDetectorCap",
                  "DC_ZDetectorCap", "DC_FractionsDetectorCap",
                  "DC_FractionTypeDetectorCap", "M_DC_Detector_Cap",
                  "Aluminum", 2.7, "Al1");
            line("");
            line("");
            line("//Vacuum");
            // счётчик у вакуума называется DC_nVacuum, без Elements
            Carry(text, model, "DC_nVacuum", "DC_RoVacuum", "DC_ZVacuum",
                  "DC_FractionsVacuum", "DC_FractionTypeVacuum", "M_DC_Vacuum",
                  "Aluminum", 1e-10, "Al1");
            line("");
            line("");
            line("// Scintillation detector materials:");
            line("//----------------------------------");
            line("");
            line("");
            line("// Crystal");
            Material(text, DsCrystal, model.Crystal);
            line("");
            line("");
            line("// Crystal Cladding ");
            Material(text, DsCladding, model.Cladding);
            line("");
            line("");
            line("// Reflector ");
            Material(text, DsReflector, model.Reflector);
            line("");
            line("");
            line("");
            line("// Cylindrical beaker materials:");
            line("//----------------------------");
            line("");
            line("// Walls ");
            Material(text, Wall("SC"), model.BeakerWall);
            line("");
            line("");
            line("//Source ");
            Material(text, SourceSlot("SC"), model.Source);
            line("");
            line("");
            line("// Empty space ");
            CarrySlot(text, model, EmptySpace("SC"));
            line("");
            line("");
            line("");
            line("//Marinelli beaker materials:");
            line("//---------------------------");
            line("");
            line("// Walls ");
            Material(text, Wall("SM"), model.BeakerWall);
            line("");
            line("");
            line("");
            line("//Source  ");
            Material(text, SourceSlot("SM"), model.Source);
            line("");
            line("");
            line("");
            line("// Empty space ");
            CarrySlot(text, model, EmptySpace("SM"));
            line("");

            // Наше расширение формата — настоящая форма кристалла. Идёт в самом
            // конце и отдельным комментарием: их программа таких ключей не
            // знает и пропускает, а человеку надо понимать, откуда они взялись.
            if (model.Shape == CrystalShape.Box)
            {
                line("");
                line("// Настоящая форма кристалла (наше расширение формата).");
                line("// Длинная сторона Z вдоль оси детектора, торец X*Y смотрит на источник.");
                cm("DS_CrystalBoxX", model.CrystalBoxX);
                cm("DS_CrystalBoxY", model.CrystalBoxY);
                cm("DS_CrystalBoxZ", model.CrystalBoxZ);
            }

            return text.ToString();
        }

        /// <summary>Приведение бруска к цилиндру по правилу LSRM: равная площадь торца.</summary>
        public static double EquivalentDiameter(double x, double y)
        {
            return x > 0.0 && y > 0.0 ? 2.0 * Math.Sqrt(x * y / Math.PI) : 0.0;
        }

        static readonly string[] CoaxialKeys =
        {
            "DC_CrystalDiameter", "DC_CrystalHeight", "DC_CrystalHoleDiameter",
            "DC_CrystalHoleHeight", "DC_CrystalFrontDeadLayer", "DC_CrystalSideDeadLayer",
            "DC_CrystalBackDeadLayer", "DC_CrystalHoleBottomDeadLayer",
            "DC_CrystalHoleSideDeadLayer", "DC_CrystalSideCladdingThickness",
            "DC_CapToCrystalDistance", "DC_DetectorCapDiameter",
            "DC_DetectorCapFrontThickness", "DC_DetectorCapSideThickness",
            "DC_DetectorCapBackThickness", "DC_DetectorMountingThickness",
        };

        /// <summary>Значение из исходного файла, иначе ноль.</summary>
        static double Carried(GeometryModel model, string key)
        {
            string raw;
            if (!model.Raw.TryGetValue(key, out raw))
            {
                return 0.0;
            }

            System.Text.RegularExpressions.Match m =
                System.Text.RegularExpressions.Regex.Match(raw, @"^\s*(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)");
            double value;
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                                                CultureInfo.InvariantCulture, out value)
                ? value : 0.0;
        }

        /// <summary>Вещество, которого мы не показываем: как было, иначе умолчание.</summary>
        static void Carry(StringBuilder text, GeometryModel model, string countKey, string roKey,
                          string zPart, string fractionsPart, string fractionTypeKey,
                          string namePrefix, string defaultName, double defaultDensity,
                          string defaultFormula)
        {
            Slot slot = new Slot
            {
                CountKey = countKey, RoKey = roKey, ZPart = zPart,
                FractionsPart = fractionsPart, FractionTypeKey = fractionTypeKey,
                NamePrefix = namePrefix,
            };

            GeometryMaterial material = Read(model, slot);
            if (material == null)
            {
                material = GeometryMaterialLibrary.Make(
                    new GeometryMaterialLibrary.Entry
                    {
                        Name = defaultName, Formula = defaultFormula, Density = defaultDensity
                    }, defaultDensity);
            }

            Material(text, slot, material);
        }

        static void CarrySlot(StringBuilder text, GeometryModel model, Slot slot)
        {
            GeometryMaterial material = Read(model, slot);
            if (material == null)
            {
                // Воздух. Состав в файлах LSRM у «пустого места» записан водой
                // при воздушной плотности; повторять эту странность не будем,
                // но и значить она ничего не может: 0.15 % на 5 см при 40 кэВ.
                material = GeometryMaterialLibrary.Make(
                    new GeometryMaterialLibrary.Entry
                    {
                        Name = "Air, dry", Formula = "N2 O1", Density = 0.001205
                    }, 0.001205);
            }

            Material(text, slot, material);
        }

        /// <summary>Прочитать вещество слота из исходного файла или null.</summary>
        static GeometryMaterial Read(GeometryModel model, Slot slot)
        {
            string density;
            if (!model.Raw.TryGetValue(slot.RoKey, out density))
            {
                return null;
            }

            GeometryMaterial material = new GeometryMaterial();
            string name;
            material.Name = model.Raw.TryGetValue(slot.NamePrefix + ".MName", out name) ? name.Trim() : "";
            double value;
            material.Density = double.TryParse(density.Trim(), NumberStyles.Float,
                                               CultureInfo.InvariantCulture, out value) ? value : 0.0;
            for (int i = 0; i < 24; i++)
            {
                string zKey = slot.ZPart + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string fKey = slot.FractionsPart + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string zRaw, fRaw;
                if (!model.Raw.TryGetValue(zKey, out zRaw) || !model.Raw.TryGetValue(fKey, out fRaw))
                {
                    continue;
                }

                int z;
                double fraction;
                if (int.TryParse(zRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out z)
                    && double.TryParse(fRaw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out fraction)
                    && z > 0 && fraction > 0.0)
                {
                    double have;
                    material.Fractions.TryGetValue(z, out have);
                    material.Fractions[z] = have + fraction;
                }
            }

            return material.Fractions.Count > 0 ? material : null;
        }

        static void Material(StringBuilder text, Slot slot, GeometryMaterial material)
        {
            List<int> order = new List<int>(material.Fractions.Keys);
            order.Sort();
            Action<string> line = value => text.Append(value).Append("\r\n");

            line(string.Format(CultureInfo.InvariantCulture, "{0} = {1}", slot.CountKey, order.Count));
            line(string.Format(CultureInfo.InvariantCulture, "{0} = {1}", slot.RoKey, Trim(material.Density)));
            for (int i = 0; i < order.Count; i++)
            {
                line(string.Format(CultureInfo.InvariantCulture, "{0}[{1}] = {2}", slot.ZPart, i, order[i]));
                line(string.Format(CultureInfo.InvariantCulture, "{0}[{1}] = {2:G6}",
                                   slot.FractionsPart, i, material.Fractions[order[i]]));
            }

            line(slot.FractionTypeKey + " = MASS");
            line(slot.NamePrefix + ".MName = " + material.Name);
            line(slot.NamePrefix + ".Nmaterials = 1");
            // Имя дополнено пробелами до 41 знака — так в файлах LSRM; на разбор
            // не влияет, но глазами файлы сравнивать удобнее.
            line(slot.NamePrefix + ".Name[0] = " + material.Name.PadRight(41));
            line(slot.NamePrefix + ".MatRelWeight[0] = 1");
        }

        /// <summary>Число без хвоста нулей: 5.90 -> 5.9, 3.0 -> 3.</summary>
        static string Trim(double value)
        {
            string text = value.ToString("G8", CultureInfo.InvariantCulture);
            return text;
        }
    }
}
