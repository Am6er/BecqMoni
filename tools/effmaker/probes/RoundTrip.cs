using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RoundTrip
{
    /// <summary>
    /// Круговая проверка записи геометрии: файл читается, пишется нашим
    /// writer'ом, читается снова — и по обоим считается эффективность.
    ///
    /// Сверяется не текст, а ЧИСЛО: совпадение строк ничего не доказывает
    /// (можно совпасть и с одинаково неверным разбором), а совпадение кривой
    /// при одинаковом зерне означает, что сцена собралась ровно та же.
    /// </summary>
    static class Program
    {
        static readonly double[] Energies = { 50, 100, 300, 662, 1461, 2614 };

        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("roundtrip <каталог моделей> <каталог для записи>");
                return 1;
            }

            int bad = 0;
            foreach (string path in Directory.GetFiles(args[0], "*.in"))
            {
                bad += Check(path, Path.Combine(args[1], Path.GetFileName(path)), false) ? 0 : 1;
            }

            // Вторая ветвь: кристалл-цилиндр. У всех наших моделей кристалл
            // прямоугольный, и диаметр с высотой там ПРОИЗВОДНЫЕ — не читаются
            // расчётом вовсе. Чтобы проверить и их, форма принудительно
            // сводится к цилиндру: тогда эти два поля становятся входными.
            Console.WriteLine();
            Console.WriteLine("### та же проверка с принудительно цилиндрическим кристаллом");
            foreach (string path in Directory.GetFiles(args[0], "*.in"))
            {
                bad += Check(path, Path.Combine(args[1], "cyl_" + Path.GetFileName(path)), true) ? 0 : 1;
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("РАСХОЖДЕНИЙ: {0}", bad));
            return bad == 0 ? 0 : 2;
        }

        static bool Check(string source, string target, bool forceCylinder)
        {
            GeometryModel a = GeometryModel.Load(source);
            if (forceCylinder)
            {
                a.Shape = CrystalShape.Cylinder;
                a.CrystalBoxX = a.CrystalBoxY = a.CrystalBoxZ = 0.0;
            }

            GeometryWriter.Save(a, target);
            GeometryModel b = GeometryModel.Load(target);

            Console.WriteLine("=== {0}", Path.GetFileName(target));
            Console.WriteLine("    было : {0}", a.Describe());
            Console.WriteLine("    стало: {0}", b.Describe());

            bool ok = true;
            Dictionary<string, string> fa = Fields(a), fb = Fields(b);
            foreach (KeyValuePair<string, string> pair in fa)
            {
                // У прямоугольного кристалла диаметр и высота цилиндра —
                // ПРОИЗВОДНЫЕ: расчёт их не читает, а писатель нарочно
                // пересчитывает по правилу LSRM. Их расхождение не ошибка, но
                // молчать о нём нельзя — печатаем отдельной пометкой.
                bool derived = a.Shape == CrystalShape.Box
                               && (pair.Key == "CrystalDiameter" || pair.Key == "CrystalHeight");
                string other;
                if (!fb.TryGetValue(pair.Key, out other) || other != pair.Value)
                {
                    Console.WriteLine("    {0} {1}: {2} -> {3}",
                                      derived ? "производное" : "ПОЛЕ", pair.Key, pair.Value, other ?? "нет");
                    ok = ok && derived;
                }
            }

            EfficiencySimulator sa = new EfficiencySimulator(a) { Histories = 60000 };
            EfficiencySimulator sb = new EfficiencySimulator(b) { Histories = 60000 };
            foreach (double e in Energies)
            {
                double ea, eb, err;
                ea = sa.Efficiency(e, out err);
                eb = sb.Efficiency(e, out err);
                double delta = ea > 0.0 ? (eb / ea - 1.0) * 100.0 : (eb > 0.0 ? 100.0 : 0.0);
                if (Math.Abs(delta) > 1e-9)
                {
                    Console.WriteLine("    {0,6:F0} кэВ: {1:E4} -> {2:E4}  ({3:+0.000;-0.000} %)", e, ea, eb, delta);
                    ok = false;
                }
            }

            Console.WriteLine(ok ? "    кривая совпала точно" : "    РАСХОЖДЕНИЕ");
            return ok;
        }

        /// <summary>Всё, что наш разбор берёт из файла, — плоским списком.</summary>
        static Dictionary<string, string> Fields(GeometryModel g)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            Action<string, double> n = (k, v) => map[k] = v.ToString("G10", CultureInfo.InvariantCulture);
            n("CrystalDiameter", g.CrystalDiameter);
            n("CrystalHeight", g.CrystalHeight);
            n("BoxX", g.CrystalBoxX); n("BoxY", g.CrystalBoxY); n("BoxZ", g.CrystalBoxZ);
            n("FrontRefl", g.FrontReflectorThickness); n("SideRefl", g.SideReflectorThickness);
            n("FrontClad", g.FrontCladdingThickness); n("SideClad", g.SideCladdingThickness);
            n("Mount", g.MountingThickness);
            n("PointDistance", g.PointDistance);
            n("BeakerDist", g.BeakerToDetectorDistance); n("BeakerD", g.BeakerDiameter);
            n("BeakerH", g.BeakerHeight); n("BeakerSide", g.BeakerSideWallThickness);
            n("BeakerEnd", g.BeakerEndWallThickness); n("SrcH", g.SourceHeight);
            n("MarD", g.MarinelliBeakerDiameter); n("MarH", g.MarinelliBeakerHeight);
            n("MarHoleD", g.MarinelliHoleDiameter); n("MarHoleH", g.MarinelliHoleHeight);
            n("MarSide", g.MarinelliSideThickness); n("MarEnd", g.MarinelliEndWallThickness);
            n("MarHoleSide", g.MarinelliHoleSideThickness); n("MarHoleEnd", g.MarinelliHoleEndWallThickness);
            n("MarSrcH", g.MarinelliSourceHeight); n("MarDist", g.MarinelliToDetectorDistance);
            map["Shape"] = g.Shape.ToString();
            map["SourceType"] = g.SourceType.ToString();
            map["Scint"] = g.IsScintillator.ToString();
            Mat(map, "Crystal", g.Crystal); Mat(map, "Reflector", g.Reflector);
            Mat(map, "Cladding", g.Cladding); Mat(map, "Wall", g.BeakerWall);
            Mat(map, "Source", g.Source);
            return map;
        }

        static void Mat(Dictionary<string, string> map, string name, GeometryMaterial m)
        {
            map[name + ".Name"] = m.Name;
            map[name + ".Ro"] = m.Density.ToString("G8", CultureInfo.InvariantCulture);
            List<int> zs = new List<int>(m.Fractions.Keys);
            zs.Sort();
            string text = "";
            foreach (int z in zs)
            {
                text += string.Format(CultureInfo.InvariantCulture, "{0}:{1:F6} ", z, m.Fractions[z]);
            }

            map[name + ".Comp"] = text.Trim();
        }
    }
}
