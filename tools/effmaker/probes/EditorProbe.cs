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
    /// Проверка конструктора геометрии БЕЗ кликов: модель загружается в панель
    /// и тут же собирается обратно. Всё, что панель показала неверно или не
    /// показала вовсе, вылезет расхождением.
    ///
    /// Именно так ловится ошибка, найденная руками: поле «расстояние от стакана
    /// Маринелли до детектора» заводилось и писалось, но не читалось — правка
    /// готового файла молча обнуляла его.
    ///
    /// Конструктор переехал из формы `GeometryEditorForm` в панель
    /// `GeometryEditorPanel` (вкладка «Geometry Editor» конструктора кривой), и
    /// геометрия теперь въезжает в него не конструктором, а `SetModel`.
    /// Проверяются оба следствия переезда: что `SetModel` доносит все поля и
    /// что панель не объявляет чужую геометрию изменённой, ничего не изменив, —
    /// на этом флаге висит кнопка «Сохранить».
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
            bool dirty;
            string invalid;
            using (GeometryEditorPanel panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                panel.SetModel(GeometryModel.Load(path));
                dirty = panel.Dirty;

                // BuildModel и Validate закрыты — зовём отражением. Открытый
                // TryCommit для пробы не годится: на ошибке он показывает
                // MessageBox и проба повисает без человека у экрана.
                after = (GeometryModel)Invoke(panel, "BuildModel");
                invalid = (string)Invoke(panel, "Validate", after);
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

            if (dirty)
            {
                Console.WriteLine("    ЗАГРУЗКА ОБЪЯВЛЕНА ПРАВКОЙ: Dirty=true сразу после SetModel");
                ok = false;
            }

            if (invalid != null)
            {
                Console.WriteLine("    ПАНЕЛЬ НЕ ПРИНИМАЕТ СВОЮ ЖЕ ГЕОМЕТРИЮ: {0}", invalid);
                ok = false;
            }

            Console.WriteLine(ok ? "    все поля прошли панель без потерь" : "    РАСХОЖДЕНИЕ");
            return ok;
        }

        static object Invoke(GeometryEditorPanel panel, string name, params object[] args)
        {
            MethodInfo method = typeof(GeometryEditorPanel).GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new MissingMethodException("GeometryEditorPanel." + name);
            }

            return method.Invoke(panel, args);
        }

        static Dictionary<string, double> Snapshot(GeometryModel g)
        {
            Dictionary<string, double> map = new Dictionary<string, double>(StringComparer.Ordinal);
            // Не только размеры: тип источника и форма кристалла решают, КАКИЕ
            // размеры вообще читаются. Потерянный тип источника превращает
            // маринелли в точку на девяноста сантиметрах, и все числа при этом
            // остаются на местах — расхождения по ним не будет.
            map["SourceType"] = (double)(int)g.SourceType;
            map["Shape"] = (double)(int)g.Shape;
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
