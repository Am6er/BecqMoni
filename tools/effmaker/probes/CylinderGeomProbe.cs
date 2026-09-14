using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

// T16: собрать геометрию «Цилиндр» ИЗ ВСТРОЕННЫХ ШАБЛОНОВ приложения и
// записать её файлом `.in`.
//
// Зачем проба вообще. Кривой «Цилиндр», на которой сняты §11 и §11а журнала
// матрицы, не осталось ни в одной конфигурации, и её числа стали
// невоспроизводимы. Указание Amber 08.08.2026: взять модель Nano16Pro из
// пресетов, встроенных в код, и оттуда же — шаблон цилиндрического сосуда,
// расстояние 50 мм.
//
// Числа НЕ набираются здесь руками: детектор берётся у
// `GeometryPresets` («Atom Spectra Nano 16», собран из `Nano16Pro.in`), сосуд —
// у `GeometryEditorPanel.Blank()`, то есть у той самой заготовки, которую
// видит человек, открывая редактор геометрии. Второй набор тех же чисел в
// пробе разошёлся бы с формой при первой правке.
//
// Что здесь ЗАДАНО, а не унаследовано, — ровно одно: расстояние до торца.
// Всё остальное, включая вещество пробы (у заготовки это ВОЗДУХ, а не грунт),
// приходит из шаблонов как есть.
//
//   cylindergeomprobe --out=Nano16Pro_cyl50.in [--distance=50] [--name=Цилиндр]
class CylinderGeomProbe
{
    const string DetectorPreset = "Atom Spectra Nano 16";

    static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string outPath = null, name = "Цилиндр";
        double distanceMm = 50.0;
        foreach (string a in args)
        {
            if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a.StartsWith("--name=", StringComparison.Ordinal)) name = a.Substring(7);
            else if (a.StartsWith("--distance=", StringComparison.Ordinal))
                distanceMm = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (outPath == null) { Console.Error.WriteLine("нужен --out=<файл .in>"); return 2; }

        // Менеджеры нужны библиотеке веществ: пресеты зовут GeometryMaterialLibrary,
        // а она читает matdb.
        GlobalConfigManager.GetInstance();

        GeometryModel g = GeometryEditorPanel.Blank();
        GeometryPresets.Preset preset =
            GeometryPresets.Items.FirstOrDefault(p => p.Name == DetectorPreset);
        if (preset == null)
        {
            Console.Error.WriteLine("во встроенных пресетах нет «" + DetectorPreset + "»");
            return 1;
        }

        preset.Apply(g);
        g.Name = name;
        g.SourceType = GeometrySourceType.Cylinder;
        g.BeakerToDetectorDistance = distanceMm;

        GeometryWriter.Save(g, outPath);

        Console.WriteLine("геометрия «{0}» -> {1}", g.Name, Path.GetFullPath(outPath));
        Console.WriteLine();
        Console.WriteLine("детектор  : пресет «{0}»", DetectorPreset);
        Console.WriteLine("  кристалл: {0}, {1}",
                          g.Shape == CrystalShape.Box
                              ? string.Format("брус {0:F1}x{1:F1}x{2:F1} мм",
                                              g.CrystalBoxX, g.CrystalBoxY, g.CrystalBoxZ)
                              : string.Format("цилиндр {0:F1}x{1:F1} мм",
                                              g.CrystalDiameter, g.CrystalHeight),
                          g.Crystal.Name);
        Console.WriteLine("  обвязка : отражатель {0:F1}/{1:F1}, корпус {2:F1}/{3:F1}, оправа {4:F1} мм",
                          g.FrontReflectorThickness, g.SideReflectorThickness,
                          g.FrontCladdingThickness, g.SideCladdingThickness, g.MountingThickness);
        Console.WriteLine("сосуд     : шаблон заготовки редактора");
        Console.WriteLine("  {0:F1} мм диаметр, {1:F1} мм высота, стенки {2:F1}/{3:F1} мм,"
                          + " слой пробы {4:F1} мм",
                          g.BeakerDiameter, g.BeakerHeight,
                          g.BeakerSideWallThickness, g.BeakerEndWallThickness, g.SourceHeight);
        Console.WriteLine("  до торца: {0:F1} мм  <- ЗАДАНО", g.BeakerToDetectorDistance);
        Console.WriteLine("  стенка  : {0}", g.BeakerWall.Name);
        Console.WriteLine("  проба   : {0}  <- из шаблона, НЕ грунт", g.Source.Name);
        if (!(g.FwhmAt662Percent > 0.0))
        {
            Console.WriteLine();
            Console.WriteLine("ВНИМАНИЕ: разрешение (DS_Fwhm662) в шаблонах не задано —"
                              + " поправка на однократное рассеяние работать не будет.");
        }

        return 0;
    }
}
