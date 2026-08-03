using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace LayerProbe
{
    /// <summary>
    /// Чего стоит каждый сопутствующий слой файла геометрии. По очереди
    /// убирается ОДИН слой — обнулением ПЛОТНОСТИ, а не толщины: так расстояние
    /// от пробы до кристалла не меняется, и разница отвечает ровно на вопрос
    /// «сколько поглощает это вещество», а не «что будет, если детектор
    /// подвинуть».
    ///
    ///   layerprobe --geometry=X.in [--n=400000]
    /// </summary>
    static class Program
    {
        static readonly double[] Energies =
        { 50, 60, 80, 100, 150, 200, 300, 400, 662, 1173, 1461, 2614 };

        static int Main(string[] args)
        {
            string path = null;
            int n = 400000;
            foreach (string arg in args)
            {
                int eq = arg.IndexOf('=');
                string key = eq > 0 ? arg.Substring(0, eq) : arg;
                string value = eq > 0 ? arg.Substring(eq + 1) : "";
                if (key == "--geometry") path = value;
                else if (key == "--n") n = int.Parse(value, CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("unknown: " + arg); return 1; }
            }

            if (path == null) { Console.Error.WriteLine("--geometry required"); return 1; }

            GeometryModel probe = GeometryModel.Load(path);
            Console.WriteLine("=== {0}", Path.GetFileNameWithoutExtension(path));
            Console.WriteLine("    {0}", probe.Describe());
            Console.WriteLine("    отражатель {0} ({1:F2} г/см3, {2:F2}/{3:F2} см), корпус {4} ({5:F2} г/см3, {6:F2}/{7:F2} см), оправа {8:F2} см",
                probe.Reflector.Name, probe.Reflector.Density,
                probe.FrontReflectorThickness, probe.SideReflectorThickness,
                probe.Cladding.Name, probe.Cladding.Density,
                probe.FrontCladdingThickness, probe.SideCladdingThickness,
                probe.MountingThickness);
            Console.WriteLine("    стенка {0} ({1:F2} г/см3), проба {2} ({3:F2} г/см3)",
                probe.BeakerWall.Name, probe.BeakerWall.Density,
                probe.Source.Name, probe.Source.Density);
            Console.WriteLine();

            // Плотность делается ничтожной, а не нулевой. Ноль ВЫБРАСЫВАЕТ
            // область из сцены (Add отвергает вещество без плотности), а области
            // вложены и ищутся по порядку — на освободившееся место немедленно
            // натекает объемлющий слой. Обнулив отражатель, мы не убирали его, а
            // заменяли на алюминий корпуса: он плотнее и тяжелее, и «убранный»
            // отражатель давал -8 % вместо честного плюса. С 1e-9 область
            // остаётся на месте и просто перестаёт поглощать.
            const double Vacuum = 1e-9;
            var variants = new List<KeyValuePair<string, Action<GeometryModel, EfficiencySimulator>>>
            {
                Make("отражатель не поглощает", (g, s) => g.Reflector.Density = Vacuum),
                Make("корпус не поглощает",     (g, s) => g.Cladding.Density = Vacuum),
                Make("оправа убрана",           (g, s) => g.MountingThickness = 0.0),
                // В файле задана только ТОЛЩИНА оправы, без указания, спереди
                // она или сзади. Разница между двумя прочтениями — цена того,
                // что мы этого не знаем.
                Make("оправа спереди",          (g, s) => s.MountingInFront = true),
                Make("стенка не поглощает",     (g, s) => g.BeakerWall.Density = Vacuum),
                Make("самопоглощения нет",      (g, s) => g.Source.Density = Vacuum),
                Make("всё, кроме кристалла", (g, s) =>
                {
                    g.Reflector.Density = Vacuum;
                    g.Cladding.Density = Vacuum;
                    g.MountingThickness = 0.0;
                    g.BeakerWall.Density = Vacuum;
                    g.Source.Density = Vacuum;
                }),
                Make("плотность кристалла -5 %", (g, s) => g.Crystal.Density *= 0.95),
            };

            double[] baseline = new double[Energies.Length];
            double[] baseErr = new double[Energies.Length];
            Run(path, null, n, baseline, baseErr);

            Console.Write("{0,-28}", "вариант \\ E, кэВ");
            foreach (double e in Energies) Console.Write("{0,8:F0}", e);
            Console.WriteLine();
            Console.Write("{0,-28}", "расчёт как есть");
            for (int i = 0; i < Energies.Length; i++) Console.Write("{0,8:E1}", baseline[i]);
            Console.WriteLine();
            Console.Write("{0,-28}", "  статистика, ±%");
            for (int i = 0; i < Energies.Length; i++) Console.Write("{0,8:F1}", baseErr[i]);
            Console.WriteLine();
            Console.WriteLine("--- ниже: изменение к расчёту как есть, % ---");

            double[] variantEps = new double[Energies.Length];
            double[] variantErr = new double[Energies.Length];
            foreach (var variant in variants)
            {
                Run(path, variant.Value, n, variantEps, variantErr);
                Console.Write("{0,-28}", variant.Key);
                for (int i = 0; i < Energies.Length; i++)
                {
                    double delta = baseline[i] > 0.0 ? (variantEps[i] / baseline[i] - 1.0) * 100.0 : 0.0;
                    Console.Write("{0,8:F1}", delta);
                }

                Console.WriteLine();
            }

            return 0;
        }

        static KeyValuePair<string, Action<GeometryModel, EfficiencySimulator>> Make(
            string name, Action<GeometryModel, EfficiencySimulator> apply)
        {
            return new KeyValuePair<string, Action<GeometryModel, EfficiencySimulator>>(name, apply);
        }

        /// <summary>
        /// Геометрия перечитывается с диска на каждый вариант: правки идут по
        /// живым объектам материалов, и переиспользованная модель тащила бы за
        /// собой обнуления предыдущего варианта.
        /// </summary>
        static void Run(string path, Action<GeometryModel, EfficiencySimulator> apply, int n,
                        double[] values, double[] errors)
        {
            GeometryModel g = GeometryModel.Load(path);
            EfficiencySimulator sim = new EfficiencySimulator(g) { Histories = n };
            if (apply != null)
            {
                // Сцена собирается лениво, при первом счёте, поэтому правки и
                // геометрии, и настроек симулятора успевают попасть в неё.
                apply(g, sim);
            }

            for (int i = 0; i < Energies.Length; i++)
            {
                double err;
                values[i] = sim.Efficiency(Energies[i], out err);
                errors[i] = err;
            }
        }
    }
}
