using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace CascadeNestProbe
{
    /// <summary>
    /// (`A314`, П232 05.10.2026) ВЛОЖЕННЫЙ КАСКАД РЕЛАКСАЦИИ — частота и сторож.
    ///
    /// `RelaxationElectrons` ведёт электроны каскада через `ElectronLoss`, а тот
    /// рекурсией (тормозное → `InCrystal` → фотопоглощение или комптон) может войти
    /// в каскад снова. До правки вложенный вызов писал общий стек `cascadeStack` с
    /// нуля поверх незавершённого внешнего. Проба гоняет узлы матрицы тем же
    /// симулятором, что построитель склада (`ResponseMatrixBuilder.MakeSimulator`,
    /// отражением; настройки — умолчания `ResponseMatrixOptions`, как у склада), и
    /// печатает два счётчика симулятора:
    ///
    /// * `CountCascadeNested` — сколько раз каскад вошёл вложенно (частота события);
    /// * `CountCascadeClobbered` — сколько раз внешний каскад нашёл свои ещё не
    ///   снятые вакансии переписанными (сторож дефекта: до правки — не ноль,
    ///   после — ноль).
    ///
    /// Плюс сумма гистограмм узла (`R`) — чтобы два прогона разных сборок сводились
    /// построчно: у узла без порчи числа обязаны совпасть побитово.
    ///
    ///   CascadeNestProbe --dir=&lt;каталог .in&gt; --only=A,B --n=200000 [--e=40,100,662]
    ///                    [--crystal=&lt;вещество библиотеки&gt;]
    ///
    /// Код возврата: 0 — порчи нет; 2 — порча есть (`CountCascadeClobbered` &gt; 0);
    /// 1 — ошибка ключей или входа.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch (Exception)
            {
            }

            string dir = null, only = null, crystal = null;
            int n = 200000;
            var energies = new List<double> { 40, 100, 300, 662, 1461, 3000 };
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
                else if (a.StartsWith("--only=", StringComparison.Ordinal)) only = a.Substring(7);
                else if (a.StartsWith("--crystal=", StringComparison.Ordinal)) crystal = a.Substring(10);
                else if (a.StartsWith("--n=", StringComparison.Ordinal))
                    n = int.Parse(a.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--e=", StringComparison.Ordinal))
                {
                    energies.Clear();
                    foreach (string s in a.Substring(4).Split(','))
                    {
                        energies.Add(double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    Console.Error.WriteLine("не знаю ключа: " + a);
                    return 1;
                }
            }

            if (dir == null || only == null || !Directory.Exists(dir))
            {
                Console.Error.WriteLine("нужны --dir=<каталог .in> и --only=<сцены>");
                return 1;
            }

            MethodInfo make = typeof(ResponseMatrixBuilder).GetMethod(
                "MakeSimulator", BindingFlags.NonPublic | BindingFlags.Static);
            if (make == null)
            {
                Console.Error.WriteLine("нет ResponseMatrixBuilder.MakeSimulator — проба устарела");
                return 1;
            }

            var options = new ResponseMatrixOptions();
            long allNested = 0, allClobbered = 0, allHistories = 0;
            Console.WriteLine("сцена\tузел\tкэВ\tисторий\tвложенных\tиспорчено\tсумма_гистограмм\tс");
            foreach (string key in only.Split(','))
            {
                string path = Path.Combine(dir, key.Trim() + ".in");
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine("НЕТ СЦЕНЫ: " + path);
                    return 1;
                }

                GeometryModel geometry = GeometryModel.Load(path);
                if (crystal != null)
                {
                    // Тяжёлый кристалл (BGO, LaBr₃, …) — сцена та же, вещество из
                    // библиотеки: у Z ≥ 57 оже-электроны L-серии уже ≥ 5 кэВ и
                    // дают тормозное на ВТОРОМ шаге каскада, когда стек не пуст.
                    GeometryMaterialLibrary.Entry ce = GeometryMaterialLibrary.ByName(crystal);
                    if (ce == null)
                    {
                        Console.Error.WriteLine("нет в библиотеке вещества кристалла: " + crystal);
                        return 1;
                    }

                    geometry.Crystal = GeometryMaterialLibrary.Make(ce, ce.Density);
                    geometry.Crystal.Name = ce.Name;
                }

                double[] grid = options.BuildGrid(geometry);
                foreach (double e in energies)
                {
                    // ближайший узел сетки склада — тем же зерном, что узел склада
                    int index = 0;
                    for (int i = 1; i < grid.Length; i++)
                    {
                        if (Math.Abs(grid[i] - e) < Math.Abs(grid[index] - e))
                        {
                            index = i;
                        }
                    }

                    var sim = (EfficiencySimulator)make.Invoke(
                        null, new object[] { geometry, options, index, grid[index] });
                    sim.Histories = n;
                    var watch = Stopwatch.StartNew();
                    double err;
                    double[][] h = sim.ResponseByChannel(grid[index], options.BinKev, out err);
                    watch.Stop();
                    double sum = 0.0;
                    foreach (double[] row in h)
                    {
                        if (row == null) continue;
                        foreach (double v in row) sum += v;
                    }

                    allNested += sim.CountCascadeNested;
                    allClobbered += sim.CountCascadeClobbered;
                    allHistories += n;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2:F3}\t{3}\t{4}\t{5}\t{6:R}\t{7:F1}",
                        key.Trim(), index, grid[index], n, sim.CountCascadeNested,
                        sim.CountCascadeClobbered, sum, watch.Elapsed.TotalSeconds));
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "ИТОГ: историй {0}, вложенных каскадов {1} ({2:E2} на историю), испорчено {3} ({4:E2} на историю)",
                allHistories, allNested, allHistories > 0 ? (double)allNested / allHistories : 0.0,
                allClobbered, allHistories > 0 ? (double)allClobbered / allHistories : 0.0));
            return allClobbered > 0 ? 2 : 0;
        }
    }
}
