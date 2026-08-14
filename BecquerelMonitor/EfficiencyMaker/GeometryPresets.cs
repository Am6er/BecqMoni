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
    ///
    /// Размеры — в МИЛЛИМЕТРАХ, как и вся модель; в файлах `.in`, откуда они
    /// взяты, стоят сантиметры.
    ///
    /// РАЗРЕШЕНИЕ пресет тоже задаёт (E14, 08.08.2026), и это не украшение:
    /// без <see cref="GeometryModel.FwhmAt662Percent"/> допуск пика нулевой, а
    /// при нулевом допуске поправка на однократное рассеяние
    /// (<see cref="EfficiencySimulator.SingleScatter"/>) не даёт НИЧЕГО —
    /// рассеянный на малый угол квант в пик не возвращается, и низ шкалы
    /// занижен примерно на десятую часть на 28 кэВ. Человек, выбравший пресет
    /// и посчитавший кривую, терял эту поправку молча.
    ///
    /// Числа — ИЗМЕРЕННЫЕ полуширины на 662 кэВ по группам корпуса
    /// (`tools/CORPUS/corpus/detectors.csv`, колонка `fwhm_662_pct`), решение
    /// Amber 08.08.2026. Это средние по группе, а не паспорт конкретного
    /// экземпляра: у своего прибора разрешение берётся из его ПШПВ-калибровки
    /// кнопкой «из прибора» в редакторе геометрии.
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

        static List<Preset> Build()
        {
            List<Preset> list = new List<Preset>();

            // Atom Spectra Nano 16 — из Nano16Pro.in. Кристалл прямоугольный,
            // формат .in этого не умеет, у нас умеет.
            list.Add(Make("Atom Spectra Nano 16", g =>
            {
                Box(g, 15.0, 18.0, 60.0);
                Wrapping(g, 1.3, 1.0, 1.8, 2.0, 2.0);
                Crystal(g, "Cesium iodide");
                Fwhm(g, 6.66);                        // корпус, группа ASN16
            }));

            // RadiaCode-101/103 — из RadiaCode_Marinelli0.5.in: куб 1 см.
            // Кристалл и обвязка у них одни, разрешение РАЗНОЕ (10.85 против
            // 8.49 % — четверть), поэтому строк две: одним числом обслужить оба
            // прибора нечестно, а разрешение теперь часть пресета.
            list.Add(Make("RadiaCode-101", g =>
            {
                Box(g, 10.0, 10.0, 10.0);
                Wrapping(g, 1.0, 1.0, 1.0, 1.0, 1.0);
                Crystal(g, "Cesium iodide");
                Fwhm(g, 10.85);                       // корпус, группа RC101
            }));

            list.Add(Make("RadiaCode-103", g =>
            {
                Box(g, 10.0, 10.0, 10.0);
                Wrapping(g, 1.0, 1.0, 1.0, 1.0, 1.0);
                Crystal(g, "Cesium iodide");
                Fwhm(g, 8.49);                        // корпус, группа RC103
            }));

            // Obsidian — из «Obsidian Marinelli 0.5.in».
            list.Add(Make("Obsidian", g =>
            {
                Box(g, 7.0, 7.0, 30.0);
                Wrapping(g, 1.0, 1.0, 1.0, 1.0, 1.0);
                Crystal(g, "Cesium iodide");
                Fwhm(g, 15.08);                       // корпус, группа OBS
            }));

            // Два «Pro» своего файла не имеют; обвязка взята от Nano 16, как
            // сказано при постановке, — ЗАИМСТВОВАНИЕ, а не измерение: если у
            // этих приборов отражатель или корпус другие, кривая уедет, и
            // проверять их надо отдельно. Кристалл 80x80 — ИОДИД НАТРИЯ.
            //
            // История этой строки, чтобы её не переставили в третий раз:
            // изначально стоял NaI, взятый из имени прибора (допущение);
            // 14.08.2026 заменён на CsI по прямому слову Amber; 15.08.2026
            // Amber эту замену ОТМЕНИЛА («я ошибся, кристалл NaI»), и здесь
            // снова иодид натрия. Файл геометрии
            // `tools/CORPUS/corpus/geometries/AS80_point0.in` всё это время
            // оставался с NaI (записан до подмены и заново не переписывался),
            // поэтому матрица `AS80_point0.rmx` и кривая этой геометрии верны
            // и пересчёта НЕ ТРЕБУЮТ — см. вычеркнутую строку `B7`.
            list.Add(Make("Atom Spectra Pro 80x80", g =>
            {
                Cylinder(g, 80.0, 80.0);
                Wrapping(g, 1.3, 1.0, 1.8, 2.0, 2.0);
                Crystal(g, "Sodium iodide");
                Fwhm(g, 7.65);                        // корпус, группа AS80x80
            }));

            // Гамма-1С УДС-ГЦ 63x63 — детектор, на котором снята самая богатая
            // пачка корпуса (группа G1S, 12 спектров в пяти НАЗВАННЫХ
            // геометриях с паспортными активностями). Своего файла `.in` у него
            // нет: кристалл взят из имени прибора — иодид натрия, цилиндр
            // 63x63 мм, — а обвязка ЗАИМСТВОВАНА от Nano 16 тем же ходом и с
            // той же оговоркой, что у двух «Pro»: если отражатель или корпус у
            // него другие, кривая уедет, и проверять это надо отдельно.
            // Заведён 09.08.2026 под реорганизацию корпуса (B1).
            list.Add(Make("Gamma-1S UDS-GC 63x63", g =>
            {
                Cylinder(g, 63.0, 63.0);
                Wrapping(g, 1.3, 1.0, 1.8, 2.0, 2.0);
                Crystal(g, "Sodium iodide");
                Fwhm(g, 6.44);                        // корпус, группа G1S
            }));

            // Разрешения у этого нет: группы 40x40 в корпусе не измерено, а
            // взять «как у 80x80» нельзя — размер кристалла на полуширину
            // влияет, и заимствование обвязки к разрешению не относится.
            // Ноль ставится ЯВНО, а не пропуском строки: пресет накладывается
            // на то, что уже набрано в полях, и молчаливый пропуск оставил бы
            // здесь разрешение предыдущего детектора — 6.66 % от Nano 16 у
            // прибора, который никто не мерил. Ноль виден в поле и честно
            // выключает поправку на однократное рассеяние.
            list.Add(Make("Atom Spectra Pro 40x40", g =>
            {
                Cylinder(g, 40.0, 40.0);
                Wrapping(g, 1.3, 1.0, 1.8, 2.0, 2.0);
                Crystal(g, "Sodium iodide");
                Fwhm(g, 0.0);
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

        /// <summary>Полуширина пика 662 кэВ, % — см. заголовок класса (E14).</summary>
        static void Fwhm(GeometryModel g, double percent)
        {
            g.FwhmAt662Percent = percent;
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
