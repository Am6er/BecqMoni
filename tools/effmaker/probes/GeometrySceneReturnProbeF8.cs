using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

/// <summary>
/// (`AMBER274`, полоса `f8diag` 02.11.2026) ВОЗВРАТ ИЗ ПОЛЕВОЙ СЦЕНЫ К СОСУДУ в редакторе
/// геометрии. Наблюдение полосы `a269fix5`: при смене типа источника с «на земле» на
/// «цилиндр» в цилиндр переезжают размеры сцены — диаметр в метры, глубина грунта, нулевые
/// стенки и зазор. Проба ведёт панель без окна (поля — отражением, как
/// `GeometryEditorP150Probe`) и одна годится и ДО, и ПОСЛЕ правки: нового API не зовёт.
///
///     geometrysceneReturnProbeF8
///
/// Сценарии: (1) цилиндр 70×20 → «на земле» → «цилиндр»; (2) цилиндр → «на земле» →
/// «в лунке» → «маринелли» → «цилиндр» (обе формы сосуда); (3) точка 250 мм → «поле ISO» →
/// «точка»; (4) открыта геометрия «на земле» (своих чисел сосуда нет) → «цилиндр» — заготовка;
/// (5) положительный контроль чтения: «на земле» действительно ставит метры, иначе мерить
/// нечего; (6) смена сосуда на сосуд (цилиндр → маринелли → цилиндр) чисел не трогает.
///
/// Код возврата: 0 — размеры сосуда после возврата — свои (или заготовка), не сцены;
/// 1 — в сосуд переехали размеры сцены (так ДО правки); 2 — нечем мерить.
/// </summary>
static class GeometrySceneReturnProbeF8
{
    static int defects, broken;

    const int Point = 0, Cylinder = 1, Marinelli = 2, Ground = 4, Borehole = 5, Iso = 6;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();

        CylinderRoundTrip();
        ThroughBorehole();
        IsoRoundTrip();
        OpenedAsGround();
        VesselToVessel();

        Console.WriteLine();
        if (broken > 0)
        {
            Console.WriteLine("ИТОГ: МЕРИТЬ НЕЧЕМ ({0})", broken);
            return 2;
        }

        Console.WriteLine(defects == 0
            ? "ИТОГ: ВСЕ ПРОВЕРКИ ПРОШЛИ — после возврата из сцены размеры сосуда свои"
            : "ИТОГ: ДЕФЕКТОВ " + defects.ToString(CultureInfo.InvariantCulture) + " — в сосуд переехали размеры сцены");
        return defects == 0 ? 0 : 1;
    }

    static readonly string[] CylinderKeys =
    {
        "BeakerDiameter", "BeakerHeight", "BeakerSideWallThickness", "BeakerEndWallThickness",
        "BeakerFarEndWallThickness", "SourceHeight", "BeakerToDetectorDistance",
    };

    static readonly string[] MarinelliKeys =
    {
        "MarinelliBeakerDiameter", "MarinelliBeakerHeight", "MarinelliHoleDiameter", "MarinelliHoleHeight",
        "MarinelliSourceHeight", "MarinelliToDetectorDistance", "MarinelliSideThickness",
        "MarinelliEndWallThickness", "MarinelliHoleSideThickness", "MarinelliHoleEndWallThickness",
    };

    static readonly Dictionary<string, string> MyCylinder = new Dictionary<string, string>
    {
        { "BeakerDiameter", "70" }, { "BeakerHeight", "25" }, { "BeakerSideWallThickness", "0.8" },
        { "BeakerEndWallThickness", "0.8" }, { "BeakerFarEndWallThickness", "0.8" }, { "SourceHeight", "20" },
        { "BeakerToDetectorDistance", "3" },
    };

    static readonly Dictionary<string, string> MyMarinelli = new Dictionary<string, string>
    {
        { "MarinelliBeakerDiameter", "130" }, { "MarinelliBeakerHeight", "100" }, { "MarinelliHoleDiameter", "75" },
        { "MarinelliHoleHeight", "60" }, { "MarinelliSourceHeight", "95" }, { "MarinelliToDetectorDistance", "2" },
        { "MarinelliSideThickness", "1.5" }, { "MarinelliEndWallThickness", "1.5" },
        { "MarinelliHoleSideThickness", "1.5" }, { "MarinelliHoleEndWallThickness", "1.5" },
    };

    /// <summary>Панель с годной для сцены пробой (вода) — иначе «на земле» подменит вещество.</summary>
    static GeometryEditorPanel NewPanel()
    {
        GeometryEditorPanel panel = new GeometryEditorPanel();
        GeometryModel g = GeometryEditorPanel.Blank();
        g.Source = GeometryMaterialLibrary.Make(GeometryMaterialLibrary.ByName("Water, liquid"), 1.0);
        panel.SetModel(g);
        return panel;
    }

    static ComboBox Combo(GeometryEditorPanel panel)
    {
        return (ComboBox)Field(panel, "sourceTypeCombo");
    }

    static Dictionary<string, TextBox> Fields(GeometryEditorPanel panel)
    {
        return (Dictionary<string, TextBox>)Field(panel, "fields");
    }

    static void Type(GeometryEditorPanel panel, Dictionary<string, string> values)
    {
        foreach (var kv in values)
        {
            if (!Fields(panel).ContainsKey(kv.Key)) continue;      // поле младше сборки (дальнее дно — `f8cyl`)
            Fields(panel)[kv.Key].Text = kv.Value;
        }
    }

    static string Show(GeometryEditorPanel panel, string[] keys)
    {
        var sb = new StringBuilder();
        foreach (string k in keys)
        {
            if (!Fields(panel).ContainsKey(k)) continue;
            sb.Append(' ').Append(k.Replace("Beaker", "B.").Replace("Marinelli", "M.").Replace("Thickness", "T"))
              .Append('=').Append(Fields(panel)[k].Text);
        }

        return sb.ToString();
    }

    static void Expect(string title, GeometryEditorPanel panel, Dictionary<string, string> want)
    {
        var bad = new List<string>();
        foreach (var kv in want)
        {
            double a, b;
            if (!Fields(panel).ContainsKey(kv.Key)) continue;
            string text = Fields(panel)[kv.Key].Text;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                || !double.TryParse(kv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b)
                || Math.Abs(a - b) > 1e-9 * Math.Max(1.0, Math.Abs(b)))
            {
                bad.Add(kv.Key + " = " + text + " (ждали " + kv.Value + ")");
            }
        }

        if (bad.Count == 0)
        {
            Console.WriteLine("  ✓ " + title);
        }
        else
        {
            Console.WriteLine("  ⛔ " + title + ": " + string.Join("; ", bad));
            defects++;
        }
    }

    static Dictionary<string, string> BlankOf(string[] keys)
    {
        // Заготовка — через ту же панель: SetModel(null) кладёт Blank() в поля тем же путём.
        using (GeometryEditorPanel blank = new GeometryEditorPanel())
        {
            var d = new Dictionary<string, string>();
            foreach (string k in keys)
            {
                if (!Fields(blank).ContainsKey(k)) continue;
                d[k] = Fields(blank)[k].Text;
            }

            return d;
        }
    }

    static void CylinderRoundTrip()
    {
        Console.WriteLine("(1) цилиндр 70×20 → «на земле» → «цилиндр»");
        using (GeometryEditorPanel panel = NewPanel())
        {
            Combo(panel).SelectedIndex = Cylinder;
            Type(panel, MyCylinder);
            Console.WriteLine("    цилиндр :" + Show(panel, CylinderKeys));
            Combo(panel).SelectedIndex = Ground;
            Console.WriteLine("    на земле:" + Show(panel, CylinderKeys));
            double d;
            if (!double.TryParse(Fields(panel)["BeakerDiameter"].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out d) || !(d > 500.0))
            {
                Console.WriteLine("  ⛔ (5) положительный контроль: «на земле» не поставила диаметр сцены (" + Fields(panel)["BeakerDiameter"].Text + ")");
                broken++;
                return;
            }

            Console.WriteLine("  ✓ (5) «на земле» ставит диаметр сцены {0:F0} мм", d);
            Combo(panel).SelectedIndex = Cylinder;
            Console.WriteLine("    цилиндр :" + Show(panel, CylinderKeys));
            Expect("(1) после возврата у цилиндра свои размеры", panel, MyCylinder);
        }
    }

    static void ThroughBorehole()
    {
        Console.WriteLine("(2) цилиндр и маринелли → «на земле» → «в лунке» → «маринелли» → «цилиндр»");
        using (GeometryEditorPanel panel = NewPanel())
        {
            Combo(panel).SelectedIndex = Marinelli;
            Type(panel, MyMarinelli);
            Combo(panel).SelectedIndex = Cylinder;
            Type(panel, MyCylinder);
            Combo(panel).SelectedIndex = Ground;
            Combo(panel).SelectedIndex = Borehole;
            Console.WriteLine("    в лунке :" + Show(panel, MarinelliKeys));
            Combo(panel).SelectedIndex = Marinelli;
            Console.WriteLine("    маринелли:" + Show(panel, MarinelliKeys));
            Expect("(2) у маринелли свои размеры", panel, MyMarinelli);
            Combo(panel).SelectedIndex = Cylinder;
            Expect("(2) и у цилиндра свои", panel, MyCylinder);
        }
    }

    static void IsoRoundTrip()
    {
        Console.WriteLine("(3) точка 250 мм → «поле ISO» → «точка»");
        using (GeometryEditorPanel panel = NewPanel())
        {
            Combo(panel).SelectedIndex = Point;
            var mine = new Dictionary<string, string> { { "PointDistance", "250" } };
            Type(panel, mine);
            Combo(panel).SelectedIndex = Iso;
            Console.WriteLine("    поле    : PointDistance=" + Fields(panel)["PointDistance"].Text + " FieldRadius=" + Fields(panel)["FieldRadius"].Text);
            Combo(panel).SelectedIndex = Point;
            Console.WriteLine("    точка   : PointDistance=" + Fields(panel)["PointDistance"].Text);
            Expect("(3) у точки своё расстояние", panel, mine);
        }
    }

    static void OpenedAsGround()
    {
        Console.WriteLine("(4) открыта геометрия «на земле» → «цилиндр» (своих чисел сосуда нет — заготовка)");
        GeometryModel ground;
        using (GeometryEditorPanel maker = NewPanel())
        {
            Combo(maker).SelectedIndex = Ground;
            ground = (GeometryModel)typeof(GeometryEditorPanel).GetMethod("BuildModel", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(maker, null);
        }

        using (GeometryEditorPanel panel = new GeometryEditorPanel())
        {
            panel.SetModel(ground);
            Console.WriteLine("    открыта :" + Show(panel, CylinderKeys));
            Combo(panel).SelectedIndex = Cylinder;
            Console.WriteLine("    цилиндр :" + Show(panel, CylinderKeys));
            Expect("(4) у цилиндра — заготовка, не сцена", panel, BlankOf(CylinderKeys));
        }
    }

    static void VesselToVessel()
    {
        Console.WriteLine("(6) цилиндр → маринелли → цилиндр: числа не трогаются");
        using (GeometryEditorPanel panel = NewPanel())
        {
            Combo(panel).SelectedIndex = Cylinder;
            Type(panel, MyCylinder);
            Combo(panel).SelectedIndex = Marinelli;
            Combo(panel).SelectedIndex = Cylinder;
            Expect("(6) цилиндр тот же", panel, MyCylinder);
        }
    }

    static object Field(object o, string name)
    {
        FieldInfo f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f == null)
        {
            throw new MissingFieldException(o.GetType().Name, name);
        }

        return f.GetValue(o);
    }
}
