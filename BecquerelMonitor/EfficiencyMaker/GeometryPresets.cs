using System;
using System.Collections.Generic;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Готовые детекторы. Числа взяты из файлов геометрий проекта
    /// (`tools/effmaker/models`), а не набраны на глаз: обвязку сцинтиллятора по
    /// памяти не восстановить, а ошибка в ней стоит десятков процентов —
    /// измерено, что корпус и отражатель вместе дают +45 % на 50 кэВ.
    ///
    /// Пресет задаёт ТОЛЬКО детектор: источник остаётся тот, что выбран на
    /// своей вкладке. Кристалл один и тот же меряют и в маринелли, и точечным
    /// источником, и подменять заодно геометрию пробы значило бы стирать
    /// работу пользователя.
    /// </summary>
    public static class GeometryPresets
    {
        public sealed class Preset
        {
            public string Name;
            public Action<GeometryModel> Apply;

            public override string ToString()
            {
                return this.Name;
            }
        }

        static readonly List<Preset> All = Build();

        public static List<Preset> Items
        {
            get { return All; }
        }

        public static Preset ByName(string name)
        {
            foreach (Preset preset in All)
            {
                if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return preset;
                }
            }

            return null;
        }

        static List<Preset> Build()
        {
            List<Preset> list = new List<Preset>();

            // Atom Spectra Nano 16 — из Nano16Pro.in. Кристалл прямоугольный,
            // формат .in этого не умеет, у нас умеет.
            list.Add(Make("Atom Spectra Nano 16", g =>
            {
                Box(g, 1.5, 1.8, 6.0);
                Wrapping(g, 0.13, 0.1, 0.18, 0.2, 0.2);
                Crystal(g, "Cesium iodide");
            }));

            // RadiaCode-101/103 — из RadiaCode_Marinelli0.5.in: куб 1 см.
            list.Add(Make("RadiaCode-101 / 103", g =>
            {
                Box(g, 1.0, 1.0, 1.0);
                Wrapping(g, 0.1, 0.1, 0.1, 0.1, 0.1);
                Crystal(g, "Cesium iodide");
            }));

            // Obsidian — из «Obsidian Marinelli 0.5.in».
            list.Add(Make("Obsidian", g =>
            {
                Box(g, 0.7, 0.7, 3.0);
                Wrapping(g, 0.1, 0.1, 0.1, 0.1, 0.1);
                Crystal(g, "Cesium iodide");
            }));

            // Два «Pro» своего файла не имеют. Кристалл — иодид натрия,
            // цилиндр 80x80 и 40x40 мм; обвязка взята от Nano 16, как сказано
            // при постановке. Это ЗАИМСТВОВАНИЕ, а не измерение: если у этих
            // приборов отражатель или корпус другие, кривая уедет, и проверять
            // их надо отдельно.
            list.Add(Make("Atom Spectra Pro 80x80", g =>
            {
                Cylinder(g, 8.0, 8.0);
                Wrapping(g, 0.13, 0.1, 0.18, 0.2, 0.2);
                Crystal(g, "Sodium iodide");
            }));

            list.Add(Make("Atom Spectra Pro 40x40", g =>
            {
                Cylinder(g, 4.0, 4.0);
                Wrapping(g, 0.13, 0.1, 0.18, 0.2, 0.2);
                Crystal(g, "Sodium iodide");
            }));

            return list;
        }

        static Preset Make(string name, Action<GeometryModel> apply)
        {
            return new Preset { Name = name, Apply = apply };
        }

        static void Box(GeometryModel g, double x, double y, double z)
        {
            g.Shape = CrystalShape.Box;
            g.CrystalBoxX = x;
            g.CrystalBoxY = y;
            g.CrystalBoxZ = z;
            // Цилиндр всё равно заполняется — по правилу LSRM, равная площадь
            // торца: файл должен оставаться осмысленным и для их программы.
            g.CrystalDiameter = GeometryWriter.EquivalentDiameter(x, y);
            g.CrystalHeight = z;
        }

        static void Cylinder(GeometryModel g, double diameter, double height)
        {
            g.Shape = CrystalShape.Cylinder;
            g.CrystalDiameter = diameter;
            g.CrystalHeight = height;
            g.CrystalBoxX = g.CrystalBoxY = g.CrystalBoxZ = 0.0;
        }

        static void Wrapping(GeometryModel g, double frontReflector, double sideReflector,
                             double frontCladding, double sideCladding, double mounting)
        {
            g.FrontReflectorThickness = frontReflector;
            g.SideReflectorThickness = sideReflector;
            g.FrontCladdingThickness = frontCladding;
            g.SideCladdingThickness = sideCladding;
            g.MountingThickness = mounting;
            g.Reflector = Material("Polytetrafluoroethylene");
            g.Cladding = Material("Aluminum");
        }

        static void Crystal(GeometryModel g, string name)
        {
            g.Crystal = Material(name);
        }

        static GeometryMaterial Material(string name)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            return entry != null
                ? GeometryMaterialLibrary.Make(entry, entry.Density)
                : new GeometryMaterial();
        }
    }
}
