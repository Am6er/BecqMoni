using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace EditorProbe
{
    /// <summary>
    /// Проверка формы конструктора геометрии БЕЗ кликов: модель загружается в
    /// форму и тут же собирается обратно. Всё, что форма показала неверно или
    /// не показала вовсе, вылезет расхождением.
    ///
    /// Именно так ловится ошибка, найденная руками: поле «расстояние от стакана
    /// Маринелли до детектора» заводилось и писалось, но не читалось — правка
    /// готового файла молча обнуляла его.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("editorprobe <каталог моделей>");
                return 1;
            }

            Application.EnableVisualStyles();
            int bad = 0;
            foreach (string path in Directory.GetFiles(args[0], "*.in"))
            {
                bad += Check(path) ? 0 : 1;
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("РАСХОЖДЕНИЙ: {0}", bad));
            return bad == 0 ? 0 : 2;
        }

        static bool Check(string path)
        {
            GeometryModel before = GeometryModel.Load(path);
            Dictionary<string, double> was = Snapshot(before);

            GeometryModel after;
            using (GeometryEditorForm form = new GeometryEditorForm(GeometryModel.Load(path)))
            {
                form.CreateControl();
                MethodInfo build = typeof(GeometryEditorForm).GetMethod(
                    "BuildModel", BindingFlags.Instance | BindingFlags.NonPublic);
                after = (GeometryModel)build.Invoke(form, null);
            }

            Dictionary<string, double> now = Snapshot(after);
            Console.WriteLine("=== {0}", Path.GetFileName(path));
            bool ok = true;
            foreach (KeyValuePair<string, double> pair in was)
            {
                double other = now[pair.Key];
                if (Math.Abs(other - pair.Value) > 1e-9)
                {
                    Console.WriteLine("    {0}: {1} -> {2}", pair.Key,
                        pair.Value.ToString("G8", CultureInfo.InvariantCulture),
                        other.ToString("G8", CultureInfo.InvariantCulture));
                    ok = false;
                }
            }

            Console.WriteLine(ok ? "    все поля прошли форму без потерь" : "    РАСХОЖДЕНИЕ");
            return ok;
        }

        static Dictionary<string, double> Snapshot(GeometryModel g)
        {
            Dictionary<string, double> map = new Dictionary<string, double>(StringComparer.Ordinal);
            map["CrystalBoxX"] = g.CrystalBoxX;
            map["CrystalBoxY"] = g.CrystalBoxY;
            map["CrystalBoxZ"] = g.CrystalBoxZ;
            map["FrontRefl"] = g.FrontReflectorThickness;
            map["SideRefl"] = g.SideReflectorThickness;
            map["FrontClad"] = g.FrontCladdingThickness;
            map["SideClad"] = g.SideCladdingThickness;
            map["Mounting"] = g.MountingThickness;
            map["PointDistance"] = g.PointDistance;
            map["BeakerD"] = g.BeakerDiameter;
            map["BeakerH"] = g.BeakerHeight;
            map["BeakerSide"] = g.BeakerSideWallThickness;
            map["BeakerEnd"] = g.BeakerEndWallThickness;
            map["SourceH"] = g.SourceHeight;
            map["BeakerDist"] = g.BeakerToDetectorDistance;
            map["MarD"] = g.MarinelliBeakerDiameter;
            map["MarH"] = g.MarinelliBeakerHeight;
            map["MarHoleD"] = g.MarinelliHoleDiameter;
            map["MarHoleH"] = g.MarinelliHoleHeight;
            map["MarSide"] = g.MarinelliSideThickness;
            map["MarEnd"] = g.MarinelliEndWallThickness;
            map["MarHoleSide"] = g.MarinelliHoleSideThickness;
            map["MarHoleEnd"] = g.MarinelliHoleEndWallThickness;
            map["MarSourceH"] = g.MarinelliSourceHeight;
            map["MarDist"] = g.MarinelliToDetectorDistance;
            map["CrystalRo"] = g.Crystal.Density;
            map["ReflectorRo"] = g.Reflector.Density;
            map["CladdingRo"] = g.Cladding.Density;
            map["WallRo"] = g.BeakerWall.Density;
            map["SourceRo"] = g.Source.Density;
            return map;
        }
    }
}
