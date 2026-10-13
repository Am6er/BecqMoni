using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

/// <summary>
/// (`AMBER205`, П235 05.10.2026) Сцена сосуда полной высоты — приёмка без счёта.
///
/// Печатает по каждой геометрии: вид источника и сцены, клеймо матрицы при
/// умолчаниях настроек (`ResponseMatrix.ComputeStamp`), признак «сцена сосуда
/// другая» (`EfficiencySimulator.VesselBeyondSample` — отражением: в сборке до
/// правки его нет, и проба обязана собираться против обеих) и ПОЛНЫЙ список
/// областей собранной сцены (`regionArray`: границы `R`-форматом, вещество,
/// плотность) с полями источника. Две выдачи — сборка до правки и после —
/// сравниваются построчно: у сцены без запаса и донышка строки обязаны совпасть
/// побайтно (положительный контроль «сцена до бита прежняя»), у сосуда с
/// запасом — разойтись ровно областями стенки и донышка.
///
///     vesselsceneprobep235 [--dir=<каталог .in>] [--amber=<каталог config\device>]
///                          [--synth=<шаблон .in>] [--marinelli=<.in>] [--cylinder=<.in>]
///                          [--out=<файл>]
///
/// `--amber` — ТОЛЬКО ЧТЕНИЕ: геометрии узлов `Geometry` конфигураций приборов.
/// `--synth` — из шаблона строятся точка, ISO, брусок, «на земле», «в лунке».
/// `--marinelli`/`--cylinder` — контроль (а): та же сцена с высотой сосуда =
/// высоте пробы и без дальнего донышка.
/// </summary>
static class VesselSceneProbeP235
{
    static readonly StringBuilder Out = new StringBuilder();

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string dir = null, amber = null, synth = null, mar = null, cyl = null, outPath = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
            else if (a.StartsWith("--amber=", StringComparison.Ordinal)) amber = a.Substring(8);
            else if (a.StartsWith("--synth=", StringComparison.Ordinal)) synth = a.Substring(8);
            else if (a.StartsWith("--marinelli=", StringComparison.Ordinal)) mar = a.Substring(12);
            else if (a.StartsWith("--cylinder=", StringComparison.Ordinal)) cyl = a.Substring(11);
            else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        int fails = 0;
        if (dir != null)
        {
            foreach (string f in Directory.GetFiles(dir, "*.in"))
            {
                fails += Report("corpus/" + Path.GetFileNameWithoutExtension(f), GeometryModel.Load(f));
            }
        }

        if (amber != null)
        {
            foreach (string f in Directory.GetFiles(amber, "*.xml"))
            {
                var doc = new XmlDocument();
                doc.Load(f);
                XmlNodeList nodes = doc.SelectNodes("//EfficiencyConfigData/Geometry");
                int i = 0;
                foreach (XmlNode node in nodes)
                {
                    var ser = new XmlSerializer(typeof(GeometryModel), new XmlRootAttribute("Geometry"));
                    GeometryModel g;
                    using (var r = new XmlNodeReader(node))
                    {
                        g = (GeometryModel)ser.Deserialize(r);
                    }

                    fails += Report("amber/" + Path.GetFileNameWithoutExtension(f) + "#" + (i++), g);
                }
            }
        }

        if (synth != null)
        {
            GeometryModel t = GeometryModel.Load(synth);
            GeometryModel p = t.Clone();
            p.SourceType = GeometrySourceType.Point;
            fails += Report("synth/point", p);

            GeometryModel iso = t.Clone();
            GeometryScenes.Iso(iso);
            fails += Report("synth/iso", iso);

            GeometryModel box = t.Clone();
            box.SourceType = GeometrySourceType.Box;
            box.BoxSourceX = 60; box.BoxSourceY = 40; box.BoxSourceHeight = 20;
            box.BoxSideWallThickness = 1; box.BoxEndWallThickness = 1; box.BoxToDetectorDistance = 2;
            box.BeakerWall = t.BeakerWall.Clone();
            box.Source = t.Source.Clone();
            fails += Report("synth/box", box);

            GeometryModel ground = t.Clone();
            GeometryScenes.Ground(ground, 3000.0);
            fails += Report("synth/ground", ground);

            GeometryModel hole = t.Clone();
            GeometryScenes.Borehole(hole, 3000.0);
            fails += Report("synth/borehole", hole);
        }

        if (mar != null)
        {
            GeometryModel m = GeometryModel.Load(mar);
            fails += Report("ctl/marinelli-as-is", m.Clone());
            GeometryModel a = m.Clone();
            a.MarinelliBeakerHeight = a.MarinelliSourceHeight;
            a.MarinelliEndWallThickness = 0.0;
            fails += Report("ctl/marinelli-a", a);
            GeometryModel e = m.Clone();
            e.MarinelliEndWallThickness = 0.0;
            fails += Report("ctl/marinelli-noend", e);
        }

        if (cyl != null)
        {
            GeometryModel c = GeometryModel.Load(cyl);
            fails += Report("ctl/cylinder-as-is", c.Clone());
            GeometryModel a = c.Clone();
            a.BeakerHeight = a.SourceHeight;
            fails += Report("ctl/cylinder-a", a);
            GeometryModel h = c.Clone();
            h.BeakerHeight = c.SourceHeight + c.BeakerEndWallThickness + 20.0;
            fails += Report("ctl/cylinder-head20", h);
        }

        string text = Out.ToString();
        if (outPath != null)
        {
            File.WriteAllText(outPath, text, new UTF8Encoding(false));
        }

        Console.Write(text);
        return fails > 0 ? 1 : 0;
    }

    static string R(double v)
    {
        return v.ToString("R", CultureInfo.InvariantCulture);
    }

    static object Field(object o, string name)
    {
        Type t = o.GetType();
        while (t != null)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(o);
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) return p.GetValue(o, null);
            t = t.BaseType;
        }

        return null;
    }

    static int Report(string name, GeometryModel g)
    {
        string stamp = ResponseMatrix.ComputeStamp(g, new ResponseMatrixOptions());
        MethodInfo vb = typeof(EfficiencySimulator).GetMethod("VesselBeyondSample",
            BindingFlags.Static | BindingFlags.Public);
        string vessel = vb == null ? "n/a" : ((bool)vb.Invoke(null, new object[] { g }) ? "1" : "0");
        Out.AppendFormat(CultureInfo.InvariantCulture, "== {0} | src={1} scene={2} | stamp={3}{4}",
                         name, g.SourceType, g.Scene, stamp, Environment.NewLine);
        Out.AppendFormat(CultureInfo.InvariantCulture, "   vessel={0}{1}", vessel, Environment.NewLine);
        try
        {
            var sim = new EfficiencySimulator(g.Clone());
            bool unused = sim.PerUnitFluence;           // EnsureBuilt
            Array regions = (Array)Field(sim, "regionArray");
            for (int i = 0; i < regions.Length; i++)
            {
                object r = regions.GetValue(i);
                var m = (GeometryMaterial)Field(r, "Material");
                Out.AppendFormat(CultureInfo.InvariantCulture,
                    "   region {0}: box={1} rin={2} rout={3} ax={4} ay={5} z={6}..{7} mat={8} rho={9} cr={10}{11}",
                    i, Field(r, "IsBox"), R((double)Field(r, "RIn")), R((double)Field(r, "ROut")),
                    R((double)Field(r, "AX")), R((double)Field(r, "AY")),
                    R((double)Field(r, "ZMin")), R((double)Field(r, "ZMax")),
                    m == null ? "-" : m.Name, m == null ? "-" : R(m.Density), Field(r, "IsCrystal"),
                    Environment.NewLine);
            }

            object src = Field(sim, "source");
            var sb = new StringBuilder();
            foreach (FieldInfo f in src.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object v = f.GetValue(src);
                if (v is double) sb.Append(' ').Append(f.Name).Append('=').Append(R((double)v));
            }

            Out.AppendFormat(CultureInfo.InvariantCulture, "   source {0}:{1}{2}", src.GetType().Name, sb, Environment.NewLine);
            foreach (string s in new[] { "sphereZ", "sphereR", "pathSceneZ", "pathSceneR", "sceneRMax", "sceneZMin", "sceneZMax" })
            {
                object v = Field(sim, s);
                Out.AppendFormat(CultureInfo.InvariantCulture, "   {0}={1}{2}", s, v is double ? R((double)v) : "?", Environment.NewLine);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Out.AppendFormat(CultureInfo.InvariantCulture, "   ОТКАЗ: {0}: {1}{2}", ex.GetType().Name, ex.Message, Environment.NewLine);
            return 1;
        }
    }
}
