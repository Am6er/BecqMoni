using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// S61/A38: отделяет угловой отклик кристалла от вклада его обвязки.
///
/// Для одной геометрии точечного источника отклик считается на двух расстояниях.
/// Затем опыт повторяется без обвязки, только с торцевыми и только с боковыми
/// слоями. Если разница близкой и дальней точки остаётся у голого кристалла,
/// причина в распределении длин хорд; если исчезает — в обвязке.
///
///     incidenceresponseprobe --geometry=G1S_point5.in [--e=661.657]
///                            [--n=400000] [--near=50] [--far=250] [--bin=2]
///                            [--full-only]
///
/// Расстояния задаются в миллиметрах, как в GeometryModel.
/// `--full-only` считает только настоящую обвязку — для дорогого контрольного
/// плеча без трёх абляций.
/// </summary>
static class IncidenceResponseProbe
{
    sealed class Result
    {
        public double[][] Channels;
        public double[] Total;
        public double Sum;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        string geometryPath = null;
        double energy = 661.657;
        double binKev = 2.0;
        double nearMm = 50.0;
        double farMm = 250.0;
        int histories = 400000;
        bool fullOnly = false;

        foreach (string arg in args)
        {
            if (arg.StartsWith("--geometry=", StringComparison.Ordinal))
                geometryPath = arg.Substring(11);
            else if (arg.StartsWith("--e=", StringComparison.Ordinal))
                energy = Parse(arg.Substring(4));
            else if (arg.StartsWith("--bin=", StringComparison.Ordinal))
                binKev = Parse(arg.Substring(6));
            else if (arg.StartsWith("--near=", StringComparison.Ordinal))
                nearMm = Parse(arg.Substring(7));
            else if (arg.StartsWith("--far=", StringComparison.Ordinal))
                farMm = Parse(arg.Substring(6));
            else if (arg.StartsWith("--n=", StringComparison.Ordinal))
                histories = Int32.Parse(arg.Substring(4), CultureInfo.InvariantCulture);
            else if (arg == "--full-only")
                fullOnly = true;
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + arg);
                return 2;
            }
        }

        if (geometryPath == null || !File.Exists(geometryPath))
        {
            Console.Error.WriteLine("нужен --geometry=<файл .in>");
            return 2;
        }

        GeometryModel geometry = GeometryModel.Load(geometryPath);
        if (geometry.SourceType != GeometrySourceType.Point)
        {
            Console.Error.WriteLine("нужна геометрия точечного источника");
            return 2;
        }

        Console.WriteLine("геометрия: {0}", geometry.Describe());
        Console.WriteLine("энергия {0:F3} кэВ; {1} историй; бин {2:F2} кэВ; расстояния {3:F1}/{4:F1} мм",
                          energy, histories, binKev, nearMm, farMm);
        Console.WriteLine();
        Console.WriteLine("вариант                         L1 весь   L1 комптон   край близко/далеко   далеко/близко");

        Report("полная обвязка", geometry.Clone(), energy, binKev, histories, nearMm, farMm);
        if (fullOnly)
        {
            return 0;
        }

        GeometryModel naked = geometry.Clone();
        ClearFront(naked);
        ClearSide(naked);
        naked.MountingThickness = 0.0;
        Report("голый кристалл", naked, energy, binKev, histories, nearMm, farMm);

        GeometryModel front = geometry.Clone();
        ClearSide(front);
        front.MountingThickness = 0.0;
        Report("только торцевая обвязка", front, energy, binKev, histories, nearMm, farMm);

        GeometryModel side = geometry.Clone();
        ClearFront(side);
        side.MountingThickness = 0.0;
        Report("только боковая обвязка", side, energy, binKev, histories, nearMm, farMm);
        return 0;
    }

    static double Parse(string value)
    {
        return Double.Parse(value, CultureInfo.InvariantCulture);
    }

    static void ClearFront(GeometryModel geometry)
    {
        geometry.FrontReflectorThickness = 0.0;
        geometry.FrontGapThickness = 0.0;
        geometry.FrontCladdingThickness = 0.0;
    }

    static void ClearSide(GeometryModel geometry)
    {
        geometry.SideReflectorThickness = 0.0;
        geometry.SideGapThickness = 0.0;
        geometry.SideCladdingThickness = 0.0;
    }

    static void Report(string name, GeometryModel template, double energy, double binKev,
                       int histories, double nearMm, double farMm)
    {
        GeometryModel near = template.Clone();
        near.PointDistance = nearMm;
        GeometryModel far = template.Clone();
        far.PointDistance = farMm;

        Result a = Run(near, energy, binKev, histories);
        Result b = Run(far, energy, binKev, histories);
        double all = NormalizedL1(a.Total, a.Sum, b.Total, b.Sum);

        int compton = (int)EfficiencySimulator.ResponseChannel.Compton;
        double ac = Sum(a.Channels[compton]);
        double bc = Sum(b.Channels[compton]);
        double comp = NormalizedL1(a.Channels[compton], ac, b.Channels[compton], bc);

        double edge = energy - energy / (1.0 + 2.0 * energy / 511.0);
        double edgeA = Band(a.Total, binKev, edge - 38.0, edge + 22.0) / a.Sum;
        double edgeB = Band(b.Total, binKev, edge - 38.0, edge + 22.0) / b.Sum;
        Console.WriteLine("{0,-30} {1,7:F2}%   {2,7:F2}%      {3,6:F2}/{4,6:F2}%          {5,6:F3}",
                          name, 100.0 * all, 100.0 * comp,
                          100.0 * edgeA, 100.0 * edgeB, edgeA > 0.0 ? edgeB / edgeA : 0.0);
    }

    static Result Run(GeometryModel geometry, double energy, double binKev, int histories)
    {
        var simulator = new EfficiencySimulator(geometry.Clone())
        {
            Histories = histories,
            PeakHalfWidthKev = 0.0,
            // Штатный строитель матрицы отключает эту ветвь при аналоговом
            // континууме и нулевом допуске: её бины всё равно перезаписываются.
            // Здесь нужны те же настройки, иначе сравнивается не склад матриц.
            SingleScatter = false,
            // Наведение на всю сцену меняет только дисперсию и возвращает весом
            // телесный угол. Для дальней точки иначе сотни тысяч историй дают
            // лишь тысячи событий и угловое A/B тонет в шуме.
            AnalogConeSampling = true
        };
        simulator.ResetStream(20260909UL);
        double error;
        double[][] channels = simulator.ResponseByChannel(energy, binKev, out error);
        var total = new double[channels[0].Length];
        for (int channel = 0; channel < channels.Length; channel++)
        {
            for (int bin = 0; bin < total.Length; bin++)
            {
                total[bin] += channels[channel][bin];
            }
        }

        return new Result { Channels = channels, Total = total, Sum = Sum(total) };
    }

    static double Sum(double[] values)
    {
        double sum = 0.0;
        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
        }

        return sum;
    }

    static double NormalizedL1(double[] a, double sumA, double[] b, double sumB)
    {
        if (!(sumA > 0.0) || !(sumB > 0.0))
        {
            return 0.0;
        }

        int length = Math.Min(a.Length, b.Length);
        double l1 = 0.0;
        for (int i = 0; i < length; i++)
        {
            l1 += Math.Abs(a[i] / sumA - b[i] / sumB);
        }

        return l1;
    }

    static double Band(double[] values, double binKev, double fromKev, double toKev)
    {
        int first = Math.Max(0, (int)Math.Ceiling(fromKev / binKev));
        int last = Math.Min(values.Length - 1, (int)Math.Floor(toKev / binKev));
        double sum = 0.0;
        for (int i = first; i <= last; i++)
        {
            sum += values[i];
        }

        return sum;
    }
}
