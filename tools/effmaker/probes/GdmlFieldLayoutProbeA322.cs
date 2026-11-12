using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

/// <summary>
/// ИМПОРТ GDML В ПОЛЯ РЕДАКТОРА ГЕОМЕТРИИ (`AMBER322`, полоса a322, 11.11.2026).
///
/// Постановка Amber 07.11.2026, дословно: «При импорте gdlm файла - есть отображение в детекторе вида - вообще всё.
/// В источнике - ничего не изменилось. Кнопка - посчитать эффективность - не доступна. И как это потом сохраняется?»
/// Решения — в журнале `handover/handover-2026-11-11-a322.md`.
///
///     GdmlFieldLayoutProbeA322.exe --tmp=&lt;каталог&gt; [--data=&lt;gdml_a303&gt;] [--rc110=&lt;RC110_detector.gdml&gt;] [--real=&lt;каталог&gt;]
///                                  [--shots=&lt;каталог&gt; --lang=ru|en]
///         без --shots: самопроверка без окон на экране, «СОШЛОСЬ» и код 0, иначе 1.
///         с --shots: снимки окна импорта и подвкладки «Файл GDML» (пускать ТОЛЬКО на скрытом столе, `HiddenRun.exe`).
///
/// Плечи:
///   А. Раскладка лучами на своих учебных сценах (числа известны по построению): цилиндр с сосудом перед торцом,
///      маринелли, то же в защите; проба не на оси — не переносится; кристалл-труба и два кристалла — отказ.
///   Б. `RC110_detector.gdml` (ключ --rc110): поля равны шаблону «RadiaCode-110», собранному руками по тому же файлу.
///   В. Учебные файлы читателя (--data): ни одного исключения; раскладка есть только там, где узнан кристалл.
///   Г. Настоящие файлы (--real): печать раскладки и сверка главных чисел.
///   Д. Галки: четыре сочетания с детектором — в модель ложится только отмеченное, прочее не тронуто.
///   Е. Окно: импорт заполняет поля, вкладки взятых частей уходят, «Расчёт» жив и считает; отказ — поля прежние.
///   Ж. Сохранение и открытие: запись об импорте и копия файла рядом с конфигурацией; открытие — как после импорта;
///      без копии — все вкладки и строка; кривая без импорта в XML блока не несёт.
///   З. Сопоставление руками: «Пересчитать» со снятой галкой возвращает вкладку; негодный кристалл — отказ.
///   И. Чертёж: детали вне полей бледные, детали в полях — в полном цвете (положительный контроль — без бледности).
/// </summary>
static class GdmlFieldLayoutProbeA322
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static int bad, checks;
    static string tmp;

    static void Ok(bool condition, string what, string detail = "")
    {
        checks++;
        if (!condition)
        {
            bad++;
        }

        if (!condition || detail.Length > 0)
        {
            Console.WriteLine("  {0} {1}{2}", condition ? "да " : "НЕТ", what, detail.Length > 0 ? " | " + detail : "");
        }
        else
        {
            Console.WriteLine("  да  " + what);
        }
    }

    static void Near(double got, double want, string what)
    {
        Ok(Math.Abs(got - want) <= 1e-3, what + " = " + want.ToString("0.####", Inv), "получено " + got.ToString("0.####", Inv));
    }

    // ------------------------------------------------------------------
    //  Учебные сцены
    // ------------------------------------------------------------------

    sealed class Scene
    {
        readonly StringBuilder solids = new StringBuilder(), volumes = new StringBuilder(), placed = new StringBuilder();
        int n;

        public void Tube(string name, string material, double rmin, double rmax, double z0, double z1, double x = 0.0)
        {
            string solid = "s" + (++this.n).ToString(Inv);
            this.solids.AppendFormat(Inv, "<tube name=\"{0}\" rmin=\"{1}\" rmax=\"{2}\" z=\"{3}\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"/>\n", solid, rmin, rmax, z1 - z0);
            this.Place(name, material, solid, x, 0.5 * (z0 + z1));
        }

        public void Polycone(string name, string material, params double[] zRminRmax)
        {
            string solid = "s" + (++this.n).ToString(Inv);
            this.solids.AppendFormat(Inv, "<polycone name=\"{0}\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\">\n", solid);
            for (int i = 0; i + 2 < zRminRmax.Length; i += 3)
            {
                this.solids.AppendFormat(Inv, "<zplane z=\"{0}\" rmin=\"{1}\" rmax=\"{2}\"/>\n", zRminRmax[i], zRminRmax[i + 1], zRminRmax[i + 2]);
            }

            this.solids.Append("</polycone>\n");
            this.Place(name, material, solid, 0.0, 0.0);
        }

        void Place(string name, string material, string solid, double x, double z)
        {
            this.volumes.AppendFormat(Inv, "<volume name=\"{0}\"><materialref ref=\"{1}\"/><solidref ref=\"{2}\"/></volume>\n", name, material, solid);
            this.placed.AppendFormat(Inv, "<physvol name=\"pv_{0}\"><volumeref ref=\"{0}\"/><position x=\"{1}\" y=\"0\" z=\"{2}\" unit=\"mm\"/></physvol>\n", name, x, z);
        }

        public string Write(string file)
        {
            string text = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<gdml>\n<define/>\n<materials/>\n<solids>\n"
                + "<box name=\"WorldBox\" x=\"1000\" y=\"1000\" z=\"1000\" lunit=\"mm\"/>\n" + this.solids + "</solids>\n<structure>\n" + this.volumes
                + "<volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"WorldBox\"/>\n" + this.placed + "</volume>\n</structure>\n"
                + "<setup name=\"Default\" version=\"1.0\"><world ref=\"World\"/></setup>\n</gdml>\n";
            string path = Path.Combine(tmp, file);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }
    }

    /// <summary>Детектор: NaI ⌀40 × 40, MgO 2 мм с торца и сбоку, Al 1 мм, стекло за кристаллом.</summary>
    static Scene Detector(bool hollow = false)
    {
        Scene s = new Scene();
        s.Tube("Crystal", "G4_SODIUM_IODIDE", hollow ? 5.0 : 0.0, 20.0, -20.0, 20.0);
        s.Tube("MgO_side", "G4_MAGNESIUM_OXIDE", 20.0, 22.0, -20.0, 20.0);
        s.Tube("MgO_face", "G4_MAGNESIUM_OXIDE", 0.0, 22.0, 20.0, 22.0);
        s.Tube("PMT_glass", "G4_Pyrex_Glass", 0.0, 22.0, -25.0, -20.0);
        s.Tube("Can_side", "G4_Al", 22.0, 23.0, -25.0, 22.0);
        s.Tube("Can_face", "G4_Al", 0.0, 23.0, 22.0, 23.0);
        return s;
    }

    static Scene Beaker(Scene s, double x = 0.0)
    {
        s.Tube("Beaker_bottom", "G4_POLYETHYLENE", 0.0, 31.0, 25.0, 26.0, x);
        s.Tube("Beaker_side", "G4_POLYETHYLENE", 30.0, 31.0, 26.0, 50.0, x);
        s.Tube("Sample_soil", "G4_SILICON_DIOXIDE", 0.0, 30.0, 26.0, 46.0, x);
        return s;
    }

    static Scene Marinelli(Scene s)
    {
        s.Tube("Well_ceiling", "G4_POLYETHYLENE", 0.0, 25.0, 24.0, 25.0);
        s.Tube("Well_side", "G4_POLYETHYLENE", 25.0, 26.0, -16.0, 25.0);
        s.Polycone("Sample_water", "G4_WATER", -16.0, 26.0, 50.0, 25.0, 26.0, 50.0, 25.0, 0.0, 50.0, 45.0, 0.0, 50.0);
        s.Tube("Outer_side", "G4_POLYETHYLENE", 50.0, 51.0, -16.0, 46.0);
        s.Tube("Lid", "G4_POLYETHYLENE", 0.0, 50.0, 45.0, 46.0);
        return s;
    }

    static Scene Shield(Scene s)
    {
        s.Tube("Cu_side", "G4_Cu", 60.0, 62.0, -40.0, 70.0);
        s.Tube("Pb_side", "G4_Pb", 62.0, 112.0, -40.0, 70.0);
        s.Tube("Cu_far", "G4_Cu", 0.0, 112.0, 70.0, 72.0);
        s.Tube("Pb_far", "G4_Pb", 0.0, 112.0, 72.0, 122.0);
        s.Tube("Cu_near", "G4_Cu", 23.5, 112.0, -42.0, -40.0);
        s.Tube("Pb_near", "G4_Pb", 23.5, 112.0, -92.0, -42.0);
        return s;
    }

    static GdmlFieldMap Lay(string path, out GdmlGeometry geometry)
    {
        geometry = GdmlGeometry.Read(path);
        return GdmlFieldMap.Build(geometry, GdmlOverview.Of(geometry));
    }

    static GeometryModel Fields(GdmlFieldMap map, GdmlParts parts, GeometryModel onto = null)
    {
        GeometryModel g = onto ?? new GeometryModel();
        map.Apply(g, parts);
        return g;
    }

    static string sceneBeaker, sceneMarinelli, sceneShielded, sceneOffAxis, sceneHollow, sceneTwo;

    static void Scenes()
    {
        Console.WriteLine("=== А. Раскладка лучами: учебные сцены с известными числами ===");
        sceneBeaker = Beaker(Detector()).Write("a322_beaker.gdml");
        sceneMarinelli = Marinelli(Detector()).Write("a322_marinelli.gdml");
        sceneShielded = Shield(Beaker(Detector())).Write("a322_shielded.gdml");
        sceneOffAxis = Beaker(Detector(), 70.0).Write("a322_offaxis.gdml");
        sceneHollow = Detector(true).Write("a322_hollow.gdml");
        Scene two = Detector();
        two.Tube("Crystal2", "G4_SODIUM_IODIDE", 0.0, 20.0, 100.0, 140.0);
        sceneTwo = two.Write("a322_two.gdml");

        GdmlGeometry file;
        GdmlFieldMap map = Lay(sceneBeaker, out file);
        Ok(map.Found == (GdmlParts.Detector | GdmlParts.Sample), "цилиндр с сосудом: найдены детектор и проба", map.Found + " " + map.Refusal + " " + map.SampleProblem);
        GeometryModel g = Fields(map, map.Found);
        Ok(g.Shape == CrystalShape.Cylinder && g.Facing == GeometryDetectorFacing.Front, "кристалл — цилиндр, проба у торца");
        Near(g.CrystalDiameter, 40.0, "диаметр кристалла");
        Near(g.CrystalHeight, 40.0, "длина кристалла");
        Near(g.FrontReflectorThickness, 2.0, "отражатель с торца");
        Near(g.SideReflectorThickness, 2.0, "отражатель сбоку");
        Near(g.FrontGapThickness, 0.0, "зазор с торца");
        Near(g.FrontCladdingThickness, 1.0, "корпус с торца");
        Near(g.SideCladdingThickness, 1.0, "корпус сбоку");
        Near(g.MountingThickness, 0.0, "оправа (за кристаллом стекло, не корпус)");
        Ok(map.FrontName == "+Z", "передняя грань — к пробе, +Z", map.FrontName);
        Ok(g.Crystal.Name == "Sodium iodide" && Math.Abs(g.Crystal.Density - 3.667) < 0.01, "вещество кристалла — библиотечное имя, плотность файла", g.Crystal.Name + " " + g.Crystal.Density.ToString(Inv));
        Ok(g.SourceType == GeometrySourceType.Cylinder, "проба — цилиндр", g.SourceType.ToString());
        Near(g.BeakerToDetectorDistance, 2.0, "от сосуда до торца детектора");
        Near(g.BeakerEndWallThickness, 1.0, "стенка сосуда с торца");
        Near(g.BeakerSideWallThickness, 1.0, "стенка сосуда сбоку");
        Near(g.BeakerDiameter, 62.0, "диаметр сосуда");
        Near(g.SourceHeight, 20.0, "высота пробы");
        Near(g.BeakerHeight, 25.0, "высота сосуда");
        Near(g.BeakerFarEndWallThickness, 0.0, "дальнее дно (крышки нет)");
        Ok(g.Source.Fractions.ContainsKey(14) && g.BeakerWall.Fractions.ContainsKey(6), "вещества пробы и стенки — из файла", g.Source.Name + " / " + g.BeakerWall.Name);
        List<GdmlVolumeInstance> unused = map.Unused(map.Found);
        Ok(unused.Count == 1 && unused[0].VolumeName == "PMT_glass", "не используется в геометрии: только стекло ФЭУ", string.Join(", ", unused.Select(v => v.VolumeName).ToArray()));

        map = Lay(sceneMarinelli, out file);
        Ok(map.Found == (GdmlParts.Detector | GdmlParts.Sample), "маринелли: найдены детектор и проба", map.Found + " " + map.SampleProblem);
        g = Fields(map, map.Found);
        Ok(g.SourceType == GeometrySourceType.Marinelli && g.MarinelliHoleShape == GeometryHoleShape.Round, "проба — маринелли с круглым колодцем");
        Near(g.MarinelliToDetectorDistance, 1.0, "от потолка колодца до торца");
        Near(g.MarinelliHoleEndWallThickness, 1.0, "потолок колодца");
        Near(g.MarinelliHoleSideThickness, 1.0, "стенка колодца");
        Near(g.MarinelliHoleDiameter, 50.0, "диаметр колодца");
        Near(g.MarinelliHoleHeight, 40.0, "глубина колодца");
        Near(g.MarinelliSourceHeight, 61.0, "высота пробы");
        Near(g.MarinelliSideThickness, 1.0, "наружная стенка");
        Near(g.MarinelliBeakerDiameter, 102.0, "диаметр сосуда");
        Near(g.MarinelliEndWallThickness, 1.0, "дальнее дно");
        Near(g.MarinelliBeakerHeight, 62.0, "высота сосуда");
        Ok(map.Unused(map.Found).Count == 1, "маринелли: вне полей одна деталь", map.Unused(map.Found).Count.ToString(Inv));

        map = Lay(sceneShielded, out file);
        Ok(map.Found == (GdmlParts.Detector | GdmlParts.Sample | GdmlParts.Shield), "сцена в защите: найдены все три части", map.Found.ToString());
        g = Fields(map, map.Found);
        Ok(g.InShield && g.ShieldLayers.Count == 2, "защита: два слоя", g.ShieldLayers.Count.ToString(Inv));
        if (g.ShieldLayers.Count == 2)
        {
            Ok(g.ShieldLayers[0].Material.Fractions.ContainsKey(29) && g.ShieldLayers[1].Material.Fractions.ContainsKey(82), "слои изнутри наружу: медь, свинец");
            Near(g.ShieldLayers[0].SideThickness, 2.0, "медь сбоку");
            Near(g.ShieldLayers[1].SideThickness, 50.0, "свинец сбоку");
            Near(g.ShieldLayers[0].FloorThickness, 2.0, "медь за пробой");
            Near(g.ShieldLayers[1].FloorThickness, 50.0, "свинец за пробой");
            Near(g.ShieldLayers[0].LidThickness, 2.0, "медь за детектором");
            Near(g.ShieldLayers[1].LidThickness, 50.0, "свинец за детектором");
        }

        Near(g.ShieldInnerDiameter, 120.0, "полость: диаметр");
        Near(g.ShieldInnerHeight, 110.0, "полость: высота");
        Near(g.ShieldFloorGap, 20.0, "от дальнего края сосуда до защиты");
        Ok(map.ShieldParts == 6, "деталей защиты — шесть", map.ShieldParts.ToString(Inv) + ": " + map.ShieldMaterials);

        // Защита «с детали»: с выбранного свинца — медь внутри него в защиту не идёт.
        GdmlVolumeInstance lead = file.Volumes.First(v => v != null && v.VolumeName == "Pb_side");
        GdmlFieldMap from = GdmlFieldMap.Build(file, GdmlOverview.Of(file), null, null, lead);
        GeometryModel g2 = Fields(from, from.Found);
        // Медь сбоку осталась внутри выбранной детали и в защиту не идёт, а за пробой и за детектором она есть:
        // слой меди встаёт ВНУТРИ свинца (по месту на своём луче), а не последним снаружи.
        Ok(g2.ShieldLayers.Count == 2 && g2.ShieldLayers[0].Material.Fractions.ContainsKey(29) && g2.ShieldLayers[1].Material.Fractions.ContainsKey(82)
           && g2.ShieldLayers[0].SideThickness == 0.0 && Math.Abs(g2.ShieldLayers[0].FloorThickness - 2.0) < 1e-3
           && Math.Abs(g2.ShieldLayers[1].SideThickness - 50.0) < 1e-3 && Math.Abs(g2.ShieldInnerDiameter - 124.0) < 1e-3,
            "защита с выбранной детали Pb_side: полость ⌀124, медь (0 сбоку, 2 за пробой) внутри свинца",
            string.Join("; ", g2.ShieldLayers.Select(l => l.Material.Name + " " + l.SideThickness.ToString(Inv) + "/" + l.FloorThickness.ToString(Inv) + "/" + l.LidThickness.ToString(Inv)).ToArray()));

        // Слои корпуса из разных веществ — равной массовой толщиной (решение Amber «По массовой толщине»):
        // к пробе Al 1 + полиэтилен 2 + Al 1 → Al (ρ₁·2 + ρ₂·2) / ρ₁, а не геометрические 4.
        Scene mixedScene = Detector();
        mixedScene.Tube("Pad", "G4_POLYETHYLENE", 0.0, 23.0, 23.0, 25.0);
        mixedScene.Tube("Cover", "G4_Al", 0.0, 23.0, 25.0, 26.0);
        string sceneMixed = mixedScene.Write("a322_mixed.gdml");
        map = Lay(sceneMixed, out file);
        g = Fields(map, map.Found);
        double rhoAl = file.Volumes.First(v => v != null && v.VolumeName == "Cover").Material.Density;
        double rhoPe = file.Volumes.First(v => v != null && v.VolumeName == "Pad").Material.Density;
        double massEquivalent = (rhoAl * 2.0 + rhoPe * 2.0) / rhoAl;
        Ok((map.Found & GdmlParts.Detector) != 0 && massEquivalent < 3.0 && Math.Abs(g.FrontCladdingThickness - Math.Round(massEquivalent, 4)) < 1e-3
           && g.Cladding.Fractions.ContainsKey(13),
            "корпус из Al 1 + полиэтилена 2 + Al 1 — алюминий равной массовой толщины " + massEquivalent.ToString("0.####", Inv) + ", а не 4",
            "получено " + g.FrontCladdingThickness.ToString(Inv));
        Ok(map.Simplifications.Any(s => s.Value.Contains("same mass thickness")), "упрощение «равная массовая толщина» названо в сводке");

        map = Lay(sceneOffAxis, out file);
        Ok(map.Found == GdmlParts.Detector && !string.IsNullOrEmpty(map.SampleProblem), "проба в стороне от оси: не переносится, причина названа", map.Found + ": " + map.SampleProblem);
        Ok(map.Lines.Any(l => l.Part == GdmlParts.Sample && l.InFields.Contains("not transferred")), "в сверке строка «не перенесена»");

        map = Lay(sceneHollow, out file);
        Ok(map.Found == GdmlParts.None && map.Refusal != null && map.Refusal.Contains("neither a cylinder nor a box"), "кристалл-труба: отказ раскладки с причиной", map.Refusal);
        map = Lay(sceneTwo, out file);
        Ok(map.Found == GdmlParts.None && map.Refusal != null && map.Refusal.Contains("more than one"), "два кристалла: отказ раскладки с причиной", map.Refusal);

        // Выбор кристалла человеком на сцене с двумя кристаллами отказа не снимает (их по-прежнему два),
        // а на сцене с трубой выбор сплошной детали кристаллом раскладку даёт.
        GdmlGeometry hollow = GdmlGeometry.Read(sceneHollow);
        GdmlVolumeInstance glass = hollow.Volumes.First(v => v != null && v.VolumeName == "PMT_glass");
        Ok(GdmlFieldMap.CrystalShapeFits(glass) && !GdmlFieldMap.CrystalShapeFits(hollow.Volumes.First(v => v != null && v.VolumeName == "Crystal")),
            "форма: сплошной цилиндр годится кристаллом, труба — нет");
        GdmlFieldMap chosen = GdmlFieldMap.Build(hollow, GdmlOverview.Of(hollow), glass, null, null);
        Ok((chosen.Found & GdmlParts.Detector) != 0 && Math.Abs(Fields(chosen, chosen.Found).CrystalDiameter - 44.0) < 1e-3, "кристалл, выбранный человеком, раскладывается", chosen.Found + " " + chosen.Refusal);
    }

    // ------------------------------------------------------------------
    //  RC-110 против шаблона
    // ------------------------------------------------------------------

    static void Rc110(string path)
    {
        Console.WriteLine();
        Console.WriteLine("=== Б. RC110_detector.gdml против шаблона «RadiaCode-110» ===");
        GdmlGeometry file;
        GdmlFieldMap map = Lay(path, out file);
        Ok(map.Found == GdmlParts.Detector, "найден детектор, пробы и защиты в файле нет", map.Found.ToString());
        GeometryModel g = Fields(map, map.Found);
        GeometryModel t = new GeometryModel();
        GeometryPresets.Items.First(p => p.Name == "RadiaCode-110").Apply(t);
        Ok(g.Shape == CrystalShape.Box && t.Shape == CrystalShape.Box, "брусок");
        foreach (KeyValuePair<string, Func<GeometryModel, double>> field in new Dictionary<string, Func<GeometryModel, double>>
        {
            { "CrystalBoxX", m => m.CrystalBoxX }, { "CrystalBoxY", m => m.CrystalBoxY }, { "CrystalBoxZ", m => m.CrystalBoxZ },
            { "FrontReflectorThickness", m => m.FrontReflectorThickness }, { "SideReflectorThickness", m => m.SideReflectorThickness },
            { "FrontGapThickness", m => m.FrontGapThickness }, { "SideGapThickness", m => m.SideGapThickness },
            { "FrontCladdingThickness", m => m.FrontCladdingThickness }, { "SideCladdingThickness", m => m.SideCladdingThickness },
            { "MountingThickness", m => m.MountingThickness },
        })
        {
            Near(field.Value(g), field.Value(t), field.Key + " как у шаблона");
        }

        Ok(map.FrontName == "+Z" && map.SideName == "−X", "передняя грань +Z (напротив SiPM), бок — ближняя сторона −X", map.FrontName + " " + map.SideName);
        Ok(g.Crystal.Name == t.Crystal.Name, "вещество кристалла — то же имя, что у шаблона", g.Crystal.Name);
        List<GdmlVolumeInstance> unused = map.Unused(map.Found);
        Ok(unused.Count == 6 && unused[0].VolumeName == "Battery_log", "не используется в геометрии: шесть деталей, тяжёлая первой",
            string.Join(", ", unused.Select(v => v.VolumeName).ToArray()));

        // Кривая из полей импорта против кривой шаблона: точка на 100 мм.
        foreach (GeometryModel m in new[] { g, t })
        {
            m.SourceType = GeometrySourceType.Point;
            m.IsScintillator = true;
            m.PointDistance = 100.0;
            m.Source = new GeometryMaterial();
        }

        g.Gap = t.Gap.Clone();
        EfficiencyFitResult a = EfficiencyCalculation.Run(g, 20000, null, null);
        EfficiencyFitResult b = EfficiencyCalculation.Run(t, 20000, null, null);
        Ok(a != null && a.Ok && b != null && b.Ok && a.Curve.Count == b.Curve.Count && a.Curve.Count > 5, "кривая по полям импорта считается",
            a == null || b == null ? "нет" : a.Curve.Count.ToString(Inv) + " и " + b.Curve.Count.ToString(Inv) + " точек, Ok " + a.Ok + " " + b.Ok + " " + a.Error + " | " + b.Error);
        if (a != null && a.Ok && b != null && b.Ok && a.Curve.Count == b.Curve.Count)
        {
            double worst = 0.0;
            for (int i = 0; i < a.Curve.Count; i++)
            {
                if (a.Curve[i].Energy >= 60.0 && b.Curve[i].Efficiency > 0.0)
                {
                    double off = Math.Abs(a.Curve[i].Efficiency / b.Curve[i].Efficiency - 1.0);
                    if (i % 12 == 0)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "       {0,7:0.#} кэВ: импорт {1:0.######}, шаблон {2:0.######}, {3:+0.0;-0.0} %, погрешность {4:0.0} / {5:0.0} %", a.Curve[i].Energy,
                            a.Curve[i].Efficiency, b.Curve[i].Efficiency, 100.0 * (a.Curve[i].Efficiency / b.Curve[i].Efficiency - 1.0), a.Curve[i].ErrorPercent, b.Curve[i].ErrorPercent));
                    }

                    // Сверх шума счёта: отклонение за вычетом трёх погрешностей разности.
                    double noise = 0.03 * Math.Sqrt(a.Curve[i].ErrorPercent * a.Curve[i].ErrorPercent + b.Curve[i].ErrorPercent * b.Curve[i].ErrorPercent);
                    worst = Math.Max(worst, Math.Max(0.0, off - noise));
                }
            }

            Ok(worst < 0.05, "кривая импорта против шаблона, от 60 кэВ: сверх трёх погрешностей счёта расхождение меньше 5 % (вещества файла против библиотечных)",
                "наибольшее сверх шума " + (100.0 * worst).ToString("0.0", Inv) + " %");
        }
    }

    // ------------------------------------------------------------------
    //  Учебные файлы читателя и настоящие файлы
    // ------------------------------------------------------------------

    static void Data(string dir)
    {
        Console.WriteLine();
        Console.WriteLine("=== В. Учебные файлы читателя: раскладка не падает ===");
        int files = 0, read = 0, laid = 0, thrown = 0, wrong = 0;
        List<string> names = new List<string>();
        foreach (string path in Directory.GetFiles(dir, "*.gdml"))
        {
            files++;
            try
            {
                GdmlGeometry geometry = GdmlGeometry.Read(path);
                if (!string.IsNullOrEmpty(geometry.FatalError))
                {
                    continue;
                }

                read++;
                GdmlOverview overview = GdmlOverview.Of(geometry);
                GdmlFieldMap map = GdmlFieldMap.Build(geometry, overview);
                if ((map.Found & GdmlParts.Detector) != 0)
                {
                    laid++;
                    names.Add(Path.GetFileName(path));
                    GeometryModel g = Fields(map, map.Found);
                    wrong += overview.Crystal == null || !g.HasScintillatorCrystal() ? 1 : 0;
                }
                else
                {
                    wrong += map.Refusal == null ? 1 : 0;
                }
            }
            catch (Exception ex)
            {
                thrown++;
                Console.WriteLine("    исключение: " + Path.GetFileName(path) + ": " + ex.GetType().Name + " " + ex.Message);
            }
        }

        Ok(files > 100 && thrown == 0, "файлов " + files.ToString(Inv) + ", прочитано " + read.ToString(Inv) + ", исключений нет", "исключений " + thrown.ToString(Inv));
        Ok(wrong == 0, "раскладка есть только при узнанном кристалле, отказ всегда с причиной", "нарушений " + wrong.ToString(Inv));
        Ok(laid >= 1, "разложено файлов: " + laid.ToString(Inv), string.Join(", ", names.ToArray()));
    }

    static void Real(string dir)
    {
        Console.WriteLine();
        Console.WriteLine("=== Г. Настоящие файлы ===");
        foreach (string path in Directory.GetFiles(dir, "*.gdml", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            GdmlGeometry file;
            GdmlFieldMap map = Lay(path, out file);
            GeometryModel g = Fields(map, map.Found);
            Console.WriteLine("  -- " + name + ": " + map.Found + (map.Refusal == null ? "" : " отказ: " + map.Refusal) + (map.SampleProblem == null ? "" : " проба: " + map.SampleProblem));
            foreach (GdmlMapLine line in map.Lines)
            {
                Console.WriteLine("       [" + line.Part + "] " + line.InFile + "  →  " + line.InFields);
            }

            Console.WriteLine("       " + GdmlOverview.UnusedText(map, map.Found));
            Ok((map.Found & GdmlParts.Detector) != 0, name + ": детектор разложен");
            bool marinelli = name.IndexOf("marinelli", StringComparison.OrdinalIgnoreCase) >= 0;
            Ok(((map.Found & GdmlParts.Sample) != 0) == marinelli && (!marinelli || g.SourceType == GeometrySourceType.Marinelli),
                name + (marinelli ? ": проба — маринелли" : ": пробы в файле нет"));
            if (marinelli)
            {
                // Стенка сосуда — пластик сосуда, а не остаток корпуса прибора: в `GS2020_marinelli_1000ml_water` край
                // прибора и граница отрезка луча совпадают с точностью счёта, и остаток в 1e-10 мм алюминия становился «стенкой».
                Ok(g.BeakerWall.Fractions.ContainsKey(6) && !g.BeakerWall.Fractions.ContainsKey(13) && g.MarinelliHoleEndWallThickness > 0.5,
                    name + ": стенка сосуда — пластик (углерод), не алюминий корпуса", g.BeakerWall.Name + " " + g.MarinelliHoleEndWallThickness.ToString(Inv));
                Ok(!map.Unused(map.Found).Any(v => v.MaterialName.EndsWith(g.BeakerWall.Name, StringComparison.Ordinal)), name + ": детали вещества сосуда не в списке «не используется»", g.BeakerWall.Name);
            }

            if (name.StartsWith("GS2020", StringComparison.Ordinal))
            {
                Near(g.FrontCladdingThickness, 2.772, name + ": корпус к пробе — равная массовая толщина Al (Al 1 + пена 2 + Al 1.75; геометрически 4.75)");
                Near(g.CrystalDiameter, 50.8, name + ": кристалл ⌀");
                Near(g.CrystalHeight, 50.8, name + ": кристалл, длина");
            }

            if (name.StartsWith("gamma1s", StringComparison.Ordinal))
            {
                Near(g.CrystalDiameter, 63.0, name + ": кристалл ⌀");
                Near(g.SideCladdingThickness, 2.682, name + ": корпус сбоку — равная массовая толщина Al (Al 0.5 + резина 2 + Al 1.5; геометрически 4)");
                bool shielded = name.Contains("shield") || name.Contains("marinelli");
                Ok(((map.Found & GdmlParts.Shield) != 0) == shielded, name + (shielded ? ": защита найдена лучами" : ": защиты нет"), map.ShieldMaterials);
            }

            string error = g.FacingError;
            Ok(string.IsNullOrEmpty(error), name + ": модель без отказа постановки", error ?? "");

            // Редактор принимает разложенную геометрию: вкладки взятых частей с экрана уходят, и поправить
            // поле, на которое он пожаловался бы, было бы негде.
            using (GeometryEditorPanel editor = new GeometryEditorPanel())
            {
                GeometryModel onto = editor.Model.Clone();
                map.Apply(onto, map.Found);
                string refusal = (string)typeof(GeometryEditorPanel).GetMethod("Validate", Any, null, new[] { typeof(GeometryModel) }, null).Invoke(editor, new object[] { onto });
                bool handSource = (map.Found & GdmlParts.Shield) != 0 && (map.Found & GdmlParts.Sample) == 0;
                if (handSource)
                {
                    // Защита из файла вокруг пробы, заданной вручную: сцена редактора (точка на 100 мм) в камеру может не
                    // войти — это правит человек на «Источнике». Проверяется, что зазор до дна не взят от пробы файла.
                    Ok(onto.ShieldFloorGap == 0.0 && onto.InShield, name + ": защита без пробы из файла — сцена на дне защиты (зазор 0)", onto.ShieldFloorGap.ToString(Inv));
                }
                else
                {
                    Ok(refusal == null, name + ": редактор принимает разложенную геометрию", refusal ?? "");
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Галки
    // ------------------------------------------------------------------

    static string Xml<T>(T value)
    {
        using (StringWriter writer = new StringWriter(Inv))
        {
            new XmlSerializer(typeof(T)).Serialize(writer, value);
            return writer.ToString();
        }
    }

    static void Parts()
    {
        Console.WriteLine();
        Console.WriteLine("=== Д. Галки: в модель ложится только отмеченное ===");
        GdmlGeometry file;
        GdmlFieldMap map = Lay(sceneShielded, out file);
        GeometryModel blank = new GeometryModel();
        GeometryPresets.Items[0].Apply(blank);
        blank.SourceType = GeometrySourceType.Point;
        blank.PointDistance = 77.0;
        foreach (GdmlParts parts in new[]
        {
            GdmlParts.Detector, GdmlParts.Detector | GdmlParts.Sample, GdmlParts.Detector | GdmlParts.Shield,
            GdmlParts.Detector | GdmlParts.Sample | GdmlParts.Shield,
        })
        {
            GeometryModel g = Fields(map, parts, blank.Clone());
            bool sample = (parts & GdmlParts.Sample) != 0, shield = (parts & GdmlParts.Shield) != 0;
            Ok(Math.Abs(g.CrystalDiameter - 40.0) < 1e-3
               && (sample ? g.SourceType == GeometrySourceType.Cylinder && Math.Abs(g.BeakerDiameter - 62.0) < 1e-3
                          : g.SourceType == GeometrySourceType.Point && g.PointDistance == 77.0 && g.BeakerDiameter == blank.BeakerDiameter)
               && (shield ? g.InShield && g.ShieldLayers.Count == 2 : !g.InShield && g.ShieldLayers.Count == 0),
               "выбрано: " + parts + " — взято ровно это", "источник " + g.SourceType + ", слоёв защиты " + g.ShieldLayers.Count.ToString(Inv));
        }
    }

    // ------------------------------------------------------------------
    //  Окно
    // ------------------------------------------------------------------

    static object Member(object target, string name)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, Any);
            if (field != null)
            {
                return field.GetValue(target);
            }
        }

        throw new MissingFieldException(name);
    }

    static bool Enabled(Control control)
    {
        return (bool)typeof(Control).GetMethod("GetState", Any, null, new[] { typeof(int) }, null).Invoke(control, new object[] { 4 });
    }

    static bool Import(EfficiencyMakerForm form, string path, GdmlParts? parts, out string problem)
    {
        Func<GdmlGeometry, GdmlOverview, GdmlFieldMap, GdmlChoice> choose = null;
        if (parts != null)
        {
            choose = (file, overview, guessed) =>
            {
                GdmlChoice choice = GdmlChoice.Guess(guessed);
                choice.TakeSample &= (parts.Value & GdmlParts.Sample) != 0;
                choice.TakeShield &= (parts.Value & GdmlParts.Shield) != 0;
                return choice;
            };
        }

        object[] args = { path, choose, null };
        bool ok = (bool)typeof(EfficiencyMakerForm).GetMethod("ImportGdmlFileWith", Any).Invoke(form, args);
        problem = (string)args[2];
        return ok;
    }

    static string TabNames(GeometryEditorPanel panel)
    {
        TabControl tabs = (TabControl)Member(panel, "tabs");
        return string.Join("|", tabs.TabPages.Cast<TabPage>().Select(p => p.Text).ToArray());
    }

    static void Window()
    {
        Console.WriteLine();
        Console.WriteLine("=== Е. Окно: импорт заполняет поля, вкладки взятого уходят, расчёт жив ===");
        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
            Button calc = (Button)Member(form, "calculateButton");
            Button save = (Button)Member(form, "saveButton");
            Label status = (Label)Member(form, "statusLabel");
            Ok(TabNames(panel) == "Detector|Source|Shield", "до импорта: три вкладки", TabNames(panel));
            string before = ResponseMatrix.GeometryFingerprint(panel.Model);

            string problem;
            foreach (KeyValuePair<GdmlParts, string> arm in new Dictionary<GdmlParts, string>
            {
                { GdmlParts.Detector, "Source|Shield|GDML file" },
                { GdmlParts.Detector | GdmlParts.Sample, "Shield|GDML file" },
                { GdmlParts.Detector | GdmlParts.Shield, "Source|GDML file" },
                { GdmlParts.Detector | GdmlParts.Sample | GdmlParts.Shield, "GDML file" },
            })
            {
                bool imported = Import(form, sceneShielded, arm.Key, out problem);
                Ok(imported && panel.GdmlTaken == arm.Key && TabNames(panel) == arm.Value, "галки " + arm.Key + ": вкладки — " + arm.Value, TabNames(panel) + " " + problem);
            }

            TabControl tabs = (TabControl)Member(panel, "tabs");
            Ok(tabs.SelectedTab != null && tabs.SelectedTab.Text == "GDML file", "после импорта открыта подвкладка «Файл GDML»");
            Ok(Enabled(calc), "«Посчитать эффективность» доступна");
            Ok(panel.TryCommit(), "поля приняты редактором (TryCommit)");
            GeometryModel model = panel.Model;
            Ok(Math.Abs(model.CrystalDiameter - 40.0) < 1e-3 && model.SourceType == GeometrySourceType.Cylinder && Math.Abs(model.BeakerDiameter - 62.0) < 1e-3
               && model.InShield && model.ShieldLayers.Count == 2 && Math.Abs(model.ShieldInnerDiameter - 120.0) < 1e-3,
               "в полях — геометрия файла: кристалл ⌀40, сосуд ⌀62, защита из двух слоёв");
            Ok(status.Text.Contains("a322_shielded.gdml") && status.Text.Contains("detector, sample, shield") && status.Text.Contains("Not used in the geometry: parts 1, "),
                "статус называет файл, что взято и что не используется", status.Text);
            Ok(panel.GdmlStripText.Contains("Taken from the file: detector, sample, shield"), "строка-итог над вкладками", panel.GdmlStripText);
            string summary = panel.GdmlSummaryText;
            Ok(summary.Contains("In the file → in the fields") && summary.Contains("Simplifications") && summary.Contains("▸ Not used in the geometry: parts 1, ")
               && !summary.Contains("PMT_glass — ") && summary.Contains("File details"),
               "сводка: сверка, упрощения, свёрнутый список «не используется», подробности файла");
            panel.ToggleGdmlUnused();
            Ok(panel.GdmlSummaryText.Contains("▾ Not used in the geometry") && panel.GdmlSummaryText.Contains("PMT_glass — Pyrex_Glass"), "список «не используется» разворачивается");
            panel.ToggleGdmlUnused();

            EfficiencyFitResult curve = EfficiencyCalculation.Run(model, 4000, null, null);
            Ok(curve != null && curve.Ok && curve.Curve.Count > 5 && curve.Curve.All(p => p.Efficiency >= 0.0) && curve.Curve.Any(p => p.Efficiency > 1e-4),
                "кривая по импортированной геометрии считается", curve == null ? "нет" : curve.Curve.Count.ToString(Inv) + " точек, наибольшая "
                + curve.Curve.Max(p => p.Efficiency).ToString("0.#####", Inv));

            // Отказ раскладки: поля прежние, файл только смотрится.
            string fields = ResponseMatrix.GeometryFingerprint(panel.Model);
            bool shown = Import(form, sceneHollow, null, out problem);
            Ok(shown && panel.GdmlMode && panel.GdmlTaken == GdmlParts.None && TabNames(panel) == "Detector|Source|Shield|GDML file",
                "отказ (кристалл-труба): файл показан, все вкладки на месте", TabNames(panel));
            Ok(panel.TryCommit() && ResponseMatrix.GeometryFingerprint(panel.Model) == fields && Enabled(calc), "отказ: поля прежние, расчёт доступен");
            Ok(status.Text.Contains("Fields unchanged") && status.Text.Contains("neither a cylinder nor a box"), "отказ: причина в статусе", status.Text);

            // Импорт другого файла поверх: вкладки по новому файлу.
            Import(form, sceneBeaker, null, out problem);
            Ok(TabNames(panel) == "Shield|GDML file", "повторный импорт (детектор и проба): осталась «Защита»", TabNames(panel));
            panel.SetModel(null);
            Ok(TabNames(panel) == "Detector|Source|Shield" && !panel.GdmlMode, "новая геометрия: показ файла снят, три вкладки", TabNames(panel));
            GC.KeepAlive(before);
            GC.KeepAlive(save);
        }
    }

    static bool SaveInto(EfficiencyMakerForm form)
    {
        return (bool)typeof(EfficiencyMakerForm).GetMethod("SaveIntoConfig", Any).Invoke(form, null);
    }

    static void SaveOpen()
    {
        Console.WriteLine();
        Console.WriteLine("=== Ж. Сохранение и открытие ===");
        string store = Path.Combine(tmp, "gdml_store");
        if (Directory.Exists(store))
        {
            Directory.Delete(store, true);
        }

        GdmlCopyStore.DirectoryOverride = store;
        DeviceConfigInfo device = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "a322" };
        EfficiencyConfigData config = new EfficiencyConfigData("a322");
        string plainBefore = Xml(config);
        Ok(!plainBefore.Contains("GdmlImport"), "кривая без импорта: блока GdmlImport в XML нет");

        string problem, sha;
        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            form.BindTo(device, config);
            GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
            Import(form, sceneBeaker, null, out problem);
            Ok(SaveInto(form), "«Сохранить» после импорта проходит");
            Ok(config.GdmlImport != null && config.GdmlImport.FileName == "a322_beaker.gdml" && config.GdmlImport.Sample && !config.GdmlImport.Shield,
                "в конфигурации запись об импорте: имя файла без пути, взяты детектор и проба");
            sha = config.GdmlImport == null ? "" : config.GdmlImport.Sha256;
            string copy = GdmlCopyStore.Find(sha);
            Ok(copy != null && File.ReadAllBytes(copy).SequenceEqual(File.ReadAllBytes(sceneBeaker)), "копия файла рядом с конфигурацией побайтно равна исходному", copy ?? "нет");
            Ok(config.Geometry != null && Math.Abs(config.Geometry.BeakerDiameter - 62.0) < 1e-3, "геометрия в конфигурации — поля импорта");
            GC.KeepAlive(panel);
        }

        string xml = Xml(config);
        Ok(xml.Contains("<GdmlImport FileName=\"a322_beaker.gdml\"") && !xml.Contains(tmp), "XML: блок GdmlImport с именем файла, пути в нём нет");
        EfficiencyConfigData reopened;
        using (StringReader reader = new StringReader(xml))
        {
            reopened = (EfficiencyConfigData)new XmlSerializer(typeof(EfficiencyConfigData)).Deserialize(reader);
        }

        Ok(reopened.GdmlImport != null && reopened.GdmlImport.Sha256 == sha && reopened.Copy().GdmlImport != null && reopened.Duplicate("x").GdmlImport != null,
            "запись переживает XML, копию и дубликат кривой");

        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            form.BindTo(device, reopened);
            GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
            Ok(panel.GdmlMode && panel.GdmlTaken == (GdmlParts.Detector | GdmlParts.Sample) && TabNames(panel) == "Shield|GDML file",
                "открытие сохранённой кривой: как после импорта", TabNames(panel));
            Ok(panel.TryCommit() && ResponseMatrix.GeometryFingerprint(panel.Model) == ResponseMatrix.GeometryFingerprint(reopened.Geometry),
                "открытие: поля — сохранённые, отпечаток геометрии тот же");
            Ok(!panel.GdmlSummaryText.Contains("differ from what this version"), "открытие: сохранённые поля сходятся с раскладкой копии");
            Ok(!(bool)Member(form, "dirty"), "открытие не помечает окно изменённым");
        }

        // Копии нет: все вкладки, строка об этом; пересохранение без правки запись сохраняет.
        File.Delete(GdmlCopyStore.PathOf(sha));
        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            form.BindTo(device, reopened);
            GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
            Label status = (Label)Member(form, "statusLabel");
            Ok(!panel.GdmlMode && TabNames(panel) == "Detector|Source|Shield" && status.Text.Contains("the file copy is missing"),
                "копии нет: все вкладки на месте и строка об этом", status.Text);
            typeof(EfficiencyMakerForm).GetMethod("SetDirty", Any).Invoke(form, null);
            Ok(SaveInto(form) && reopened.GdmlImport != null, "копии нет, геометрию не правили: запись об импорте при сохранении остаётся");
        }

        // Испорченная копия не принимается за копию.
        Directory.CreateDirectory(store);
        File.WriteAllText(GdmlCopyStore.PathOf(sha), "<gdml/>");
        Ok(GdmlCopyStore.Find(sha) == null, "копия с чужим содержимым не принимается (sha256 не сходится)");
        Ok(GdmlCopyStore.Find("..\\x") == null && GdmlCopyStore.Find("") == null, "имя копии — только sha256");
        GdmlCopyStore.DirectoryOverride = null;
    }

    static void Remap()
    {
        Console.WriteLine();
        Console.WriteLine("=== З. Сопоставление руками ===");
        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
            string problem;
            Import(form, sceneBeaker, null, out problem);
            GdmlMappingPanel mapping = (GdmlMappingPanel)Member(panel, "gdmlMapping");
            Button apply = (Button)Member(mapping, "applyButton");
            Ok(!Enabled(apply), "«Пересчитать» недоступна, пока выбор равен применённому");
            GdmlChoice choice = panel.GdmlChoice.Clone();
            choice.TakeSample = false;
            mapping.Show(choice, false);
            Ok(Enabled(apply) && TabNames(panel) == "Shield|GDML file", "смена выбора поля не трогает, «Пересчитать» доступна", TabNames(panel));
            typeof(EfficiencyMakerForm).GetMethod("GdmlRemap", Any).Invoke(form, new object[] { null, EventArgs.Empty });
            Ok(panel.GdmlTaken == GdmlParts.Detector && TabNames(panel) == "Source|Shield|GDML file", "«Пересчитать» со снятой галкой образца: вкладка «Источник» вернулась", TabNames(panel));
            Ok(panel.GdmlStripText.Contains("Mapping changed by hand"), "строка-итог: сопоставление изменено вручную", panel.GdmlStripText);

            // Образец возвращается под файл: вкладка «Источник», которую человек настраивал, уходит — вопрос общей
            // дверью `AppUi.AskYesNo` (без окон и без ответа пробы она бросает, а не вешает окно).
            GdmlChoice back = panel.GdmlChoice.Clone();
            back.TakeSample = true;
            back.Sample = panel.GdmlGuess.Sample;
            string asked = null;
            bool answer = false;
            Func<string, string, string, bool> before = AppUi.Probe;
            AppUi.Probe = (kind, text, caption) =>
            {
                asked = kind + ": " + text;
                return answer;
            };
            try
            {
                mapping.Show(back, false);
                typeof(EfficiencyMakerForm).GetMethod("GdmlRemap", Any).Invoke(form, new object[] { null, EventArgs.Empty });
                Ok(asked != null && asked.StartsWith("ask: ", StringComparison.Ordinal) && asked.Contains("Source") && asked.Contains("will be lost")
                   && panel.GdmlTaken == GdmlParts.Detector && TabNames(panel) == "Source|Shield|GDML file",
                    "вкладка уходит под файл: задан вопрос; ответ «нет» — ничего не меняется", asked ?? "вопроса не было");
                answer = true;
                asked = null;
                mapping.Show(back, false);
                typeof(EfficiencyMakerForm).GetMethod("GdmlRemap", Any).Invoke(form, new object[] { null, EventArgs.Empty });
                Ok(asked != null && panel.GdmlTaken == (GdmlParts.Detector | GdmlParts.Sample) && TabNames(panel) == "Shield|GDML file",
                    "ответ «да» — образец взят из файла, вкладка «Источник» ушла", TabNames(panel));
            }
            finally
            {
                AppUi.Probe = before;
            }

            GdmlChoice off = panel.GdmlChoice.Clone();
            off.TakeSample = false;
            mapping.Show(off, false);
            typeof(EfficiencyMakerForm).GetMethod("GdmlRemap", Any).Invoke(form, new object[] { null, EventArgs.Empty });

            // Кристаллом выбрана деталь негодной формы (труба корпуса).
            GdmlChoice wrong = panel.GdmlChoice.Clone();
            wrong.Crystal = panel.Gdml.Volumes.First(v => v != null && v.VolumeName == "Can_side");
            string fields = ResponseMatrix.GeometryFingerprint(panel.Model);
            mapping.Show(wrong, false);
            typeof(EfficiencyMakerForm).GetMethod("GdmlRemap", Any).Invoke(form, new object[] { null, EventArgs.Empty });
            Label notice = (Label)Member(mapping, "notice");
            panel.TryCommit();
            Ok(ReferenceEquals(panel.GdmlChoice.Crystal, panel.GdmlGuess.Crystal) && ResponseMatrix.GeometryFingerprint(panel.Model) == fields && notice.Text.Contains("does not fit as the crystal"),
                "негодный кристалл: отказ с причиной, выбор и поля прежние", notice.Text);
        }

        // Окно импорта: итог пересчитывается при смене выбора, «Импортировать» недоступна без кристалла.
        GdmlGeometry file = GdmlGeometry.Read(sceneShielded);
        GdmlOverview overview = GdmlOverview.Of(file);
        GdmlFieldMap guessed = GdmlFieldMap.Build(file, overview);
        using (GdmlImportDialog dialog = new GdmlImportDialog(file, overview, guessed))
        {
            Button ok = (Button)Member(dialog, "ok");
            Label result = (Label)Member(dialog, "result");
            Ok(Enabled(ok) && dialog.Choice.TakeSample && dialog.Choice.TakeShield && result.Text.Contains("detector, sample, shield"),
                "окно импорта: угаданное отмечено, итог назван", result.Text);
            GdmlChoice only = dialog.Choice.Clone();
            only.TakeSample = false;
            dialog.Choose(only);
            Ok(dialog.Choice.Parts == (GdmlParts.Detector | GdmlParts.Shield) && result.Text.Contains("Set by hand: sample"), "окно импорта: детектор и защита без образца", result.Text);
        }

        GdmlGeometry hollow = GdmlGeometry.Read(sceneHollow);
        GdmlOverview hollowOverview = GdmlOverview.Of(hollow);
        using (GdmlImportDialog dialog = new GdmlImportDialog(hollow, hollowOverview, GdmlFieldMap.Build(hollow, hollowOverview)))
        {
            Button ok = (Button)Member(dialog, "ok");
            Ok(!Enabled(ok), "окно импорта: кристалл не разложен — «Импортировать» недоступна");
            GdmlChoice hand = dialog.Choice.Clone();
            hand.Crystal = hollow.Volumes.First(v => v != null && v.VolumeName == "PMT_glass");
            dialog.Choose(hand);
            Ok(Enabled(ok), "окно импорта: кристалл выбран человеком — «Импортировать» доступна");
        }
    }

    // ------------------------------------------------------------------
    //  Чертёж
    // ------------------------------------------------------------------

    static Dictionary<int, int> Paint(GdmlSketch sketch)
    {
        Dictionary<int, int> colors = new Dictionary<int, int>();
        sketch.Size = new Size(700, 500);
        using (Bitmap surface = new Bitmap(700, 500))
        using (Graphics g = Graphics.FromImage(surface))
        {
            typeof(GdmlSketch).GetMethod("OnPaint", Any).Invoke(sketch, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 700, 500)) });
            for (int y = 0; y < 500; y += 1)
            {
                for (int x = 0; x < 700; x += 1)
                {
                    int key = surface.GetPixel(x, y).ToArgb();
                    int count;
                    colors.TryGetValue(key, out count);
                    colors[key] = count + 1;
                }
            }
        }

        return colors;
    }

    static int Count(Dictionary<int, int> colors, Color color)
    {
        int count;
        return colors.TryGetValue(color.ToArgb(), out count) ? count : 0;
    }

    /// <summary>Подписи чертежа вкладки по порядку: чертёж рисуется в картинку вызовом OnPaint, окно не показывается.</summary>
    static List<string> SketchWords(GdmlSketch sketch)
    {
        if (sketch.Width < 600 || sketch.Height < 500)
        {
            sketch.Dock = DockStyle.None;
            sketch.Size = new Size(712, 650);
        }

        using (Bitmap bitmap = new Bitmap(sketch.Width, sketch.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            typeof(GdmlSketch).GetMethod("OnPaint", Any).Invoke(sketch, new object[] { new PaintEventArgs(graphics, new Rectangle(0, 0, bitmap.Width, bitmap.Height)) });
        }

        return ((List<GdmlSketch.SketchMark>)Member(sketch, "paintMarks")).Select(m => m.Text).ToList();
    }

    static string GroupAfter(List<string> words, string head)
    {
        int at = words.FindIndex(t => t == head);
        return at < 0 ? "(нет группы)" : string.Join(" | ", words.Skip(at + 1));
    }

    /// <summary>
    /// (`AMBER301`, вердикт критика `crit301b`, мутант k1) Предпросмотр выбора через НАСТОЯЩИЙ обработчик `ChoiceChanged`
    /// вкладки «Файл GDML»: импорт учебной сцены в окно конструктора, смена выбора в блоке «Сопоставление» без
    /// «Пересчитать». Слова чертежа (легенда, выноски, стрелка) обязаны остаться от применённой раскладки, а свинец —
    /// побледнеть (положительный контроль: подсветка работает). Прежний обработчик ставил саму бледность (`SetPale`), и
    /// легенда писала «Не вошло в расчёт: Защита…» про детали, которые в расчёте есть. Сцена перенесена из пробы критика.
    /// </summary>
    static void Preview()
    {
        Console.WriteLine();
        Console.WriteLine("=== К. Предпросмотр выбора на вкладке «Файл GDML»: слова чертежа — о применённой раскладке ===");
        const string NotIn = "Not in the calculation:";
        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
            string problem;
            bool imported = Import(form, sceneShielded, null, out problem);
            Ok(imported && panel.GdmlTaken == (GdmlParts.Detector | GdmlParts.Sample | GdmlParts.Shield), "импорт учебной сцены: взято всё", panel.GdmlTaken + " " + problem);
            GdmlSketch sketch = (GdmlSketch)Member(panel, "gdmlSketch");
            GdmlMappingPanel mapping = (GdmlMappingPanel)Member(panel, "gdmlMapping");
            MethodInfo fill = typeof(GdmlSketch).GetMethod("FillOf", Any);
            GdmlVolumeInstance lead = panel.Gdml.Volumes.First(v => v != null && v.VolumeName == "Pb_side");

            List<string> before = SketchWords(sketch);
            Color full = (Color)fill.Invoke(sketch, new object[] { lead });
            Ok(before.Contains("In the calculation:") && before.Any(t => t.StartsWith("Shield: Pb, ", StringComparison.Ordinal)) && !GroupAfter(before, NotIn).Contains("Pb"),
                "применено всё: свинец в группе «В расчёте»", GroupAfter(before, NotIn));

            // Человек снял галку «и защита»; «Пересчитать» не нажата.
            GdmlChoice choice = panel.GdmlChoice.Clone();
            choice.TakeShield = false;
            choice.ShieldFrom = null;
            mapping.Show(choice, false);
            List<string> preview = SketchWords(sketch);
            Color dim = (Color)fill.Invoke(sketch, new object[] { lead });
            Ok(before.SequenceEqual(preview), "предпросмотр: слова чертежа (легенда, выноски, стрелка) те же, что у применённой раскладки", "«Не вошло»: " + GroupAfter(preview, NotIn));
            Ok(dim.ToArgb() != full.ToArgb(), "предпросмотр: свинец на чертеже бледнее (подсветка работает — положительный контроль)", full + " → " + dim);
            Ok(Member(sketch, "highlight") != null, "предпросмотр: подсветка стоит");

            // «Пересчитать»: теперь защита не в расчёте, и слова обязаны это сказать.
            typeof(EfficiencyMakerForm).GetMethod("GdmlRemap", Any).Invoke(form, new object[] { null, EventArgs.Empty });
            List<string> applied = SketchWords(sketch);
            Ok((panel.GdmlTaken & GdmlParts.Shield) == 0 && GroupAfter(applied, NotIn).Contains("Pb"), "после пересчёта свинец в группе «Не вошло в расчёт»", GroupAfter(applied, NotIn));
            Ok(Member(sketch, "highlight") == null, "после пересчёта подсветка снята");
            Ok(!applied.Contains("→"), "после пересчёта: проба взята из файла — стрелки «к пробе» нет", "взято " + panel.GdmlTaken);

            // Отказ пересчёта (кристаллом выбрана негодная деталь): списки и чертёж возвращаются к применённому.
            GdmlChoice wrong = panel.GdmlChoice.Clone();
            wrong.Crystal = lead;
            mapping.Show(wrong, false);
            Ok(Member(sketch, "highlight") != null, "негодный выбор: подсветка стоит до пересчёта");
            typeof(EfficiencyMakerForm).GetMethod("GdmlRemap", Any).Invoke(form, new object[] { null, EventArgs.Empty });
            List<string> refused = SketchWords(sketch);
            Ok(Member(sketch, "highlight") == null && applied.SequenceEqual(refused), "отказ пересчёта: подсветка снята, слова прежние", ((Label)Member(mapping, "notice")).Text);
        }
    }

    static void Sketch()
    {
        Console.WriteLine();
        Console.WriteLine("=== И. Чертёж: детали вне полей бледные ===");
        GdmlGeometry file = GdmlGeometry.Read(sceneBeaker);
        GdmlOverview overview = GdmlOverview.Of(file);
        GdmlFieldMap map = GdmlFieldMap.Build(file, overview);
        HashSet<GdmlVolumeInstance> unused = new HashSet<GdmlVolumeInstance>(map.Unused(map.Found));
        GdmlVolumeInstance glass = file.Volumes.First(v => v != null && v.VolumeName == "PMT_glass");
        GdmlVolumeInstance crystal = file.Volumes.First(v => v != null && v.VolumeName == "Crystal");
        using (GdmlSketch sketch = new GdmlSketch())
        {
            sketch.SetGeometry(file);
            Color full = (Color)typeof(GdmlSketch).GetMethod("FillOf", Any).Invoke(sketch, new object[] { glass });
            Color crystalColor = (Color)typeof(GdmlSketch).GetMethod("FillOf", Any).Invoke(sketch, new object[] { crystal });
            Dictionary<int, int> plain = Paint(sketch);
            sketch.SetPale(unused.Contains);
            Color pale = (Color)typeof(GdmlSketch).GetMethod("FillOf", Any).Invoke(sketch, new object[] { glass });
            Dictionary<int, int> dimmed = Paint(sketch);
            Ok(full.ToArgb() != pale.ToArgb(), "цвет бледной детали отличается от цвета вещества", full + " → " + pale);
            Ok(Count(plain, full) > 200 && Count(plain, pale) == 0, "контроль: без бледности стекло в полном цвете", Count(plain, full).ToString(Inv) + " точек");
            // (`AMBER301`, 11.11.2026) Значок легенды вещества вне расчёта бледнеет вместе с деталью (группа «Не вошло в расчёт»):
            // точки полного цвета ушли в бледные все, и с тела, и со значка.
            // Значок 11×11 точек: бледный обведён серой рамкой, а не тёмным цветом вещества, и сглаживание края даёт разницу в несколько точек.
            Ok(Count(dimmed, pale) > 200 && Count(dimmed, full) == 0 && Math.Abs(Count(plain, full) - Count(dimmed, pale)) <= 121,
                "с бледностью: тело стекла и его значок легенды бледные, точки полного цвета ушли в бледные", Count(dimmed, pale).ToString(Inv) + " точек бледных, " + Count(dimmed, full).ToString(Inv) + " полного цвета, без бледности полного " + Count(plain, full).ToString(Inv));
            Ok(Count(dimmed, crystalColor) == Count(plain, crystalColor) && Count(plain, crystalColor) > 200, "кристалл (в полях) — в полном цвете, как и без бледности",
                Count(dimmed, crystalColor).ToString(Inv) + " точек");
            sketch.SetPale(null);
            Dictionary<int, int> again = Paint(sketch);
            Ok(Count(again, full) == Count(plain, full), "снятие бледности возвращает прежний чертёж");
        }
    }

    // ------------------------------------------------------------------
    //  Третий заход (вердикт критика `crit322`)
    // ------------------------------------------------------------------

    static bool ImportWith(EfficiencyMakerForm form, string path, Func<GdmlGeometry, GdmlFieldMap, GdmlChoice> choose)
    {
        Func<GdmlGeometry, GdmlOverview, GdmlFieldMap, GdmlChoice> wrapped = (file, overview, guessed) => choose(file, guessed);
        object[] args = { path, wrapped, null };
        return (bool)typeof(EfficiencyMakerForm).GetMethod("ImportGdmlFileWith", Any).Invoke(form, args);
    }

    static GdmlVolumeInstance Part(GdmlGeometry file, string name)
    {
        return file.Volumes.First(v => v != null && v.VolumeName == name);
    }

    static Color FillOf(GdmlSketch sketch, GdmlVolumeInstance volume)
    {
        return (Color)typeof(GdmlSketch).GetMethod("FillOf", Any).Invoke(sketch, new object[] { volume });
    }

    static Color PlainOf(GdmlSketch sketch, GdmlVolumeInstance volume)
    {
        return (Color)typeof(GdmlSketch).GetMethod("ColorOf", Any, null, new[] { typeof(string) }, null).Invoke(sketch, new object[] { volume.MaterialName });
    }

    static GeometryFwhmOrigin Origin(GeometryEditorPanel panel)
    {
        return (GeometryFwhmOrigin)Member(panel, "fwhmSource");
    }

    static string FwhmText(GeometryEditorPanel panel)
    {
        return ((Dictionary<string, TextBox>)Member(panel, "fields"))["FwhmAt662Percent"].Text;
    }

    static void SelectPreset(GeometryEditorPanel panel, string name)
    {
        ComboBox presets = (ComboBox)Member(panel, "presetCombo");
        for (int i = 0; i < presets.Items.Count; i++)
        {
            if (presets.Items[i].ToString() == name)
            {
                presets.SelectedIndex = i;
                return;
            }
        }

        throw new InvalidOperationException("нет шаблона " + name);
    }

    static void Third()
    {
        Console.WriteLine();
        Console.WriteLine("=== К. FWHM после импорта (`AMBER321`): пресет нового вещества, число человека и калибровки остаётся ===");
        double nai = CrystalResolutionPresets.Find("Sodium iodide").FwhmAt662Percent;
        string naiText = CrystalResolutionPresets.FieldText(nai);
        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
            string problem;

            // 1. Шаблон RC-110 (CsI, число шаблона) → импорт NaI: пресет NaI.
            SelectPreset(panel, "RadiaCode-110");
            string template = FwhmText(panel);
            Ok(template == "8.57" && Origin(panel) == GeometryFwhmOrigin.Template, "до импорта: шаблон RC-110, 8.57, происхождение «шаблон»", template + " " + Origin(panel));
            Import(form, sceneBeaker, null, out problem);
            Ok(FwhmText(panel) == naiText && Origin(panel) == GeometryFwhmOrigin.Preset && naiText != template,
                "импорт NaI после шаблона CsI: в поле пресет NaI " + naiText + ", а не число прежнего детектора", FwhmText(panel) + " " + Origin(panel));
            panel.TryCommit();
            Ok(Math.Abs(panel.Model.FwhmAt662Percent - nai) < 1e-9 && panel.Model.FwhmOrigin == GeometryFwhmOrigin.Preset, "то же — в модели, которая идёт в расчёт и в конфигурацию");

            // Двойник поля на «Файл GDML»: то же число, правка идёт в поле редактора как правка человека.
            GdmlMappingPanel mapping = (GdmlMappingPanel)Member(panel, "gdmlMapping");
            TextBox twin = (TextBox)Member(panel, "gdmlFwhmBox");
            ComboBox facingTwin = (ComboBox)Member(panel, "gdmlFacingCombo");
            Ok(ReferenceEquals(twin.Parent, mapping.DetectorExtras) && mapping.DetectorExtras.Height > 40 && twin.Text == naiText,
                "на «Файл GDML» в блоке «Сопоставление» — поле FWHM с тем же числом", twin.Text + ", высота места " + mapping.DetectorExtras.Height.ToString(Inv));
            Label facingLabel = (Label)Member(panel, "gdmlFacingLabel");
            Func<Control, bool> shown = c => (bool)typeof(Control).GetMethod("GetState", Any, null, new[] { typeof(int) }, null).Invoke(c, new object[] { 2 });
            Ok(!shown(facingTwin) && !shown(facingLabel) && mapping.DetectorExtras.Height < 60, "у цилиндра списка «сторона к пробе» и его подписи нет",
                "высота места " + mapping.DetectorExtras.Height.ToString(Inv));
            twin.Text = "6.3";
            Ok(FwhmText(panel) == "6.3" && Origin(panel) == GeometryFwhmOrigin.User, "правка двойника — число человека в поле редактора", FwhmText(panel) + " " + Origin(panel));
            panel.SetFwhmSuggestion(7.7);
            Button twinButton = (Button)Member(panel, "gdmlFwhmButton");
            Ok((bool)typeof(Control).GetMethod("GetState", Any, null, new[] { typeof(int) }, null).Invoke(twinButton, new object[] { 2 }),
                "кнопка «Из калибровки прибора» есть рядом с двойником, когда у прибора есть калибровка");
            twinButton.PerformClick();
            panel.ApplyFwhmFromCalibration(7.7);
            Ok(twin.Text == FwhmText(panel) && Origin(panel) == GeometryFwhmOrigin.Calibration, "число из калибровки видно в двойнике", twin.Text + " " + Origin(panel));

            // 2. Число калибровки импорт не перетирает.
            Import(form, sceneMarinelli, null, out problem);
            Ok(Origin(panel) == GeometryFwhmOrigin.Calibration && FwhmText(panel) == twin.Text && FwhmText(panel) != naiText, "число калибровки импорт не перетирает", FwhmText(panel) + " " + Origin(panel));

            // 3. Число человека импорт не перетирает.
            panel.SetModel(null);
            ((Dictionary<string, TextBox>)Member(panel, "fields"))["FwhmAt662Percent"].Text = "2.94";
            Import(form, sceneBeaker, null, out problem);
            Ok(FwhmText(panel) == "2.94" && Origin(panel) == GeometryFwhmOrigin.User, "число человека (2.94) импорт не перетирает", FwhmText(panel) + " " + Origin(panel));

            // 4. Кристаллом выбран свинец (пресета нет): число шаблона обнуляется.
            panel.SetModel(null);
            SelectPreset(panel, "RadiaCode-110");
            ImportWith(form, sceneShielded, (file, guessed) =>
            {
                GdmlChoice choice = GdmlChoice.Guess(guessed);
                choice.Crystal = Part(file, "Pb_far");
                choice.TakeSample = false;
                choice.TakeShield = false;
                return choice;
            });
            double left;
            bool parsed = double.TryParse(FwhmText(panel), NumberStyles.Float, Inv, out left);
            Ok(panel.GdmlTaken == GdmlParts.Detector && (!parsed || left == 0.0) && Origin(panel) == GeometryFwhmOrigin.Unset,
                "кристалл без пресета (свинец): число шаблона 8.57 снято, поле — ноль", "«" + FwhmText(panel) + "» " + Origin(panel) + ", взято " + panel.GdmlTaken);

            // 5. Брусок: «сторона к пробе» видна в блоке и пишется в редактор.
            if (rc110Path != null)
            {
                panel.SetModel(null);
                Import(form, rc110Path, null, out problem);
                Ok(shown(facingTwin) && mapping.DetectorExtras.Height > 70, "у бруска (RC-110) в блоке есть список «сторона к пробе»", "высота места " + mapping.DetectorExtras.Height.ToString(Inv));
                Ok(shown(facingLabel) && facingLabel.Text == "Side facing the sample" && ReferenceEquals(facingLabel.Parent, mapping.DetectorExtras)
                   && facingLabel.Bottom <= facingTwin.Top && facingTwin.Bottom <= mapping.DetectorExtras.Height,
                    "у списка есть подпись «Side facing the sample», она над списком, и список целиком в отведённом месте",
                    "«" + facingLabel.Text + "», подпись до " + facingLabel.Bottom.ToString(Inv) + ", список " + facingTwin.Top.ToString(Inv) + "–" + facingTwin.Bottom.ToString(Inv));
                facingTwin.SelectedIndex = 1;
                panel.TryCommit();
                Ok(panel.Model.Facing == GeometryDetectorFacing.Side, "выбор «сбоку» в блоке доходит до модели");
                facingTwin.SelectedIndex = 0;
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== Л. Ручное сопоставление сохраняется и возвращается при открытии ===");
        string store = Path.Combine(tmp, "gdml_store3");
        if (Directory.Exists(store))
        {
            Directory.Delete(store, true);
        }

        GdmlCopyStore.DirectoryOverride = store;
        DeviceConfigInfo device = new DeviceConfigInfo { Guid = Guid.NewGuid().ToString(), Name = "a322" };
        foreach (string arm in new[] { "shield", "crystal" })
        {
            EfficiencyConfigData config = new EfficiencyConfigData("a322-" + arm);
            using (EfficiencyMakerForm form = new EfficiencyMakerForm())
            {
                form.BindTo(device, config);
                ImportWith(form, arm == "shield" ? sceneShielded : sceneHollow, (file, guessed) =>
                {
                    GdmlChoice choice = GdmlChoice.Guess(guessed);
                    if (arm == "shield")
                    {
                        choice.TakeShield = true;
                        choice.ShieldFrom = Part(file, "Pb_side");
                    }
                    else
                    {
                        choice.Crystal = Part(file, "PMT_glass");
                    }

                    return choice;
                });
                Ok(SaveInto(form), arm + ": «Сохранить» проходит");
            }

            GdmlImportInfo info = config.GdmlImport;
            string xml = Xml(config);
            if (arm == "shield")
            {
                Ok(info != null && info.ShieldPart == "Pb_side" && info.Shield && xml.Contains("ShieldPart=\"Pb_side\""), "защита «с детали Pb_side» записана в конфигурацию", info == null ? "нет записи" : "«" + info.ShieldPart + "»");
            }
            else
            {
                Ok(info != null && info.CrystalPart == "PMT_glass" && xml.Contains("CrystalPart=\"PMT_glass\""), "кристалл, выбранный вручную (PMT_glass), записан в конфигурацию", info == null ? "нет записи" : "«" + info.CrystalPart + "»");
            }

            EfficiencyConfigData reopened;
            using (StringReader reader = new StringReader(xml))
            {
                reopened = (EfficiencyConfigData)new XmlSerializer(typeof(EfficiencyConfigData)).Deserialize(reader);
            }

            using (EfficiencyMakerForm form = new EfficiencyMakerForm())
            {
                form.BindTo(device, reopened);
                GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
                GdmlChoice choice = panel.GdmlChoice;
                if (arm == "shield")
                {
                    Ok(choice != null && choice.ShieldFrom != null && choice.ShieldFrom.VolumeName == "Pb_side" && Math.Abs(panel.Model.ShieldInnerDiameter - 124.0) < 1e-3
                       && panel.GdmlStripText.Contains("Mapping changed by hand"),
                        "открытие: защита снова «с детали Pb_side», полость ⌀124, строка «сопоставление изменено вручную»", panel.GdmlStripText);
                }
                else
                {
                    Ok(choice != null && choice.Crystal != null && choice.Crystal.VolumeName == "PMT_glass" && Math.Abs(panel.Model.CrystalDiameter - 44.0) < 1e-3,
                        "открытие: кристалл снова PMT_glass, ⌀44", choice == null || choice.Crystal == null ? "нет" : choice.Crystal.VolumeName);
                }

                Ok(!panel.GdmlSummaryText.Contains("differ from what this version"), arm + ": сохранённые поля сходятся с раскладкой по записанному выбору");
            }
        }

        GdmlCopyStore.DirectoryOverride = null;

        Console.WriteLine();
        Console.WriteLine("=== М. Панель и окно импорта просят у чертежа бледность ===");
        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
            GdmlSketch sketch = (GdmlSketch)Member(panel, "gdmlSketch");
            string problem;
            Import(form, sceneBeaker, null, out problem);
            GdmlVolumeInstance glass = Part(panel.Gdml, "PMT_glass"), crystal = Part(panel.Gdml, "Crystal"), wall = Part(panel.Gdml, "Beaker_side");
            Ok(FillOf(sketch, glass).ToArgb() != PlainOf(sketch, glass).ToArgb(), "подвкладка «Файл GDML»: деталь вне полей (стекло ФЭУ) бледная");
            Ok(FillOf(sketch, crystal).ToArgb() == PlainOf(sketch, crystal).ToArgb() && FillOf(sketch, wall).ToArgb() == PlainOf(sketch, wall).ToArgb(),
                "подвкладка: кристалл и стенка сосуда (в полях) — в полном цвете");
            Import(form, sceneBeaker, GdmlParts.Detector, out problem);
            Ok(FillOf(sketch, Part(panel.Gdml, "Beaker_side")).ToArgb() != PlainOf(sketch, Part(panel.Gdml, "Beaker_side")).ToArgb(),
                "образец не взят: его сосуд на чертеже бледный");
            Import(form, sceneHollow, null, out problem);
            Ok(FillOf(sketch, Part(panel.Gdml, "PMT_glass")).ToArgb() == PlainOf(sketch, Part(panel.Gdml, "PMT_glass")).ToArgb(), "отказ раскладки: бледных деталей нет");
        }

        GdmlGeometry shielded = GdmlGeometry.Read(sceneShielded);
        GdmlOverview shieldedOverview = GdmlOverview.Of(shielded);
        using (GdmlImportDialog dialog = new GdmlImportDialog(shielded, shieldedOverview, GdmlFieldMap.Build(shielded, shieldedOverview)))
        {
            GdmlSketch sketch = (GdmlSketch)Member(dialog, "sketch");
            GdmlVolumeInstance glass = Part(shielded, "PMT_glass"), lead = Part(shielded, "Pb_side"), crystal = Part(shielded, "Crystal");
            Ok(FillOf(sketch, glass).ToArgb() != PlainOf(sketch, glass).ToArgb() && FillOf(sketch, crystal).ToArgb() == PlainOf(sketch, crystal).ToArgb()
               && FillOf(sketch, lead).ToArgb() == PlainOf(sketch, lead).ToArgb(), "окно импорта: выбранное (кристалл, защита) в полном цвете, прочее бледное");
            GdmlChoice only = dialog.Choice.Clone();
            only.TakeShield = false;
            dialog.Choose(only);
            Ok(FillOf(sketch, lead).ToArgb() != PlainOf(sketch, lead).ToArgb(), "окно импорта: галка защиты снята — защита бледнеет");
        }

        Console.WriteLine();
        Console.WriteLine("=== Н. Защита из файла, проба вручную: если проба не входит — сказано, что сделать ===");
        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
            Label status = (Label)Member(form, "statusLabel");
            ((Dictionary<string, TextBox>)Member(panel, "fields"))["PointDistance"].Text = "400";
            string problem;
            Import(form, sceneShielded, GdmlParts.Detector | GdmlParts.Shield, out problem);
            string refusal = (string)typeof(GeometryEditorPanel).GetMethod("CommitProblem", Any).Invoke(panel, new object[] { typeof(GeometryEditorPanel).GetMethod("BuildModel", Any).Invoke(panel, null) });
            Ok(panel.GdmlFitNote != null && status.Text.Contains("Change it on the Source tab") && panel.GdmlSummaryText.Contains("Change it on the Source tab"),
                "точка на 400 мм в полость 110 мм не входит: сказано в статусе и в сводке", status.Text);
            Ok(refusal != null && refusal.Contains("Change it on the Source tab"), "отказ редактора при расчёте говорит, что сделать", (refusal ?? "нет отказа").Replace("\r\n", " | "));
            Ok(TabNames(panel) == "Source|GDML file", "вкладка «Источник» на месте — править есть где", TabNames(panel));
            ((Dictionary<string, TextBox>)Member(panel, "fields"))["PointDistance"].Text = "20";
            Ok(typeof(GeometryEditorPanel).GetMethod("CommitProblem", Any).Invoke(panel, new object[] { typeof(GeometryEditorPanel).GetMethod("BuildModel", Any).Invoke(panel, null) }) == null,
                "проба поправлена (точка на 20 мм): редактор геометрию принимает");
        }
    }

    static string rc110Path;
    static GdmlParts? shotParts;

    // ------------------------------------------------------------------
    //  Снимки (скрытый стол)
    // ------------------------------------------------------------------

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    static void Shot(Form form, string path)
    {
        Application.DoEvents();
        System.Threading.Thread.Sleep(400);
        Application.DoEvents();
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        {
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = g.GetHdc();
                PrintWindow(form.Handle, hdc, 2);
                g.ReleaseHdc(hdc);
            }

            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        Console.WriteLine("снимок: " + path);
    }

    static int Shots(string dir, string lang, List<string> files)
    {
        Directory.CreateDirectory(dir);
        foreach (string path in files)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            GdmlGeometry file = GdmlGeometry.Read(path);
            GdmlOverview overview = GdmlOverview.Of(file);
            GdmlFieldMap guessed = GdmlFieldMap.Build(file, overview);
            using (GdmlImportDialog dialog = new GdmlImportDialog(file, overview, guessed))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(40, 40);
                dialog.Show();
                Shot(dialog, Path.Combine(dir, "a322-dialog-" + name + "-" + lang + ".png"));
                dialog.Hide();
            }

            using (EfficiencyMakerForm form = new EfficiencyMakerForm())
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(20, 20);
                form.Size = new Size(1180, 860);
                form.Show();
                string problem;
                Import(form, path, shotParts, out problem);
                Shot(form, Path.Combine(dir, "a322-file-" + name + (shotParts == null ? "" : "-noSample") + "-" + lang + ".png"));
                GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
                TabControl tabs = (TabControl)Member(panel, "tabs");
                if (tabs.TabPages.Count > 1)
                {
                    tabs.SelectedIndex = 0;
                    Shot(form, Path.Combine(dir, "a322-tab-" + name + "-" + lang + ".png"));
                }

                form.Hide();
            }
        }

        return 0;
    }

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = Inv;
        string data = null, rc110 = null, real = null, shots = null, lang = "en";
        List<string> shotFiles = new List<string>();
        foreach (string a in args)
        {
            if (a.StartsWith("--tmp=", StringComparison.Ordinal)) tmp = a.Substring(6);
            else if (a.StartsWith("--data=", StringComparison.Ordinal)) data = a.Substring(7);
            else if (a.StartsWith("--rc110=", StringComparison.Ordinal)) rc110 = a.Substring(8);
            else if (a.StartsWith("--real=", StringComparison.Ordinal)) real = a.Substring(7);
            else if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring(8);
            else if (a.StartsWith("--lang=", StringComparison.Ordinal)) lang = a.Substring(7);
            else if (a.StartsWith("--file=", StringComparison.Ordinal)) shotFiles.Add(a.Substring(7));
            else if (a == "--no-sample") shotParts = GdmlParts.Detector | GdmlParts.Shield;
        }

        if (string.IsNullOrEmpty(tmp))
        {
            Console.Error.WriteLine("нужен --tmp=<каталог>");
            return 2;
        }

        Directory.CreateDirectory(tmp);
        System.Threading.Thread.CurrentThread.CurrentUICulture = shots != null && lang == "ru" ? new CultureInfo("ru") : Inv;
        typeof(EfficiencyMakerForm).Assembly.GetType("BecquerelMonitor.Properties.Resources").GetProperty("Culture", Any)
            .SetValue(null, System.Threading.Thread.CurrentThread.CurrentUICulture, null);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        DeviceType.InitializeDeviceTypes();
        ThermometerType.InitializeThermometerTypes();
        GeometryTemplateStore.PathOverride = Path.Combine(tmp, "GeometryTemplates.xml");

        if (shots != null)
        {
            return Shots(shots, lang, shotFiles);
        }

        Scenes();
        rc110Path = rc110;
        if (rc110 != null) Rc110(rc110);
        if (data != null) Data(data);
        if (real != null) Real(real);
        Parts();
        Window();
        SaveOpen();
        Remap();
        Preview();
        Sketch();
        Third();

        Console.WriteLine();
        Console.WriteLine(bad == 0
            ? "СОШЛОСЬ: импорт GDML раскладывается в поля, считается и сохраняется; проверок " + checks.ToString(Inv)
            : "НЕ СОШЛОСЬ: расхождений " + bad.ToString(Inv) + " из " + checks.ToString(Inv));
        return bad == 0 ? 0 : 1;
    }
}
