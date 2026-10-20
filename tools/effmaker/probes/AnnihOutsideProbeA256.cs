using System;
using System.Globalization;
using System.Reflection;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

// AMBER256 (20.10.2026): ЧТО ЛЕЖИТ В КАНАЛЕ № 6 «Аннигиляция вне кристалла» —
// и есть ли в нём линия 1022.
//
// Посылка строки: кристалл модели — один цилиндр или один брусок, то есть тело
// ВЫПУКЛОЕ, а кванты одной пары вне кристалла летят строго навстречу
// (`AnalogTransport`, `PushPending(x, y, z, -ax, -ay, -az, …)`). Прямая через
// точку вне выпуклого тела пересекает его по ОДНУ сторону от точки, поэтому
// напрямую в кристалл попадает не больше одного кванта пары. Полный занос 1022
// возможен, только если второй вернулся без потери энергии — когерентным
// (рэлеевским) рассеянием в обвязке, пробе или домике.
//
// Узел — ТОТ ЖЕ симулятор, что у построителя (`ResponseMatrixBuilder.MakeSimulator`
// и `ResolutionHalfWidth` отражением), строка по каналам — `ResponseByChannel`.
// Канал № 6 делится на полосы по положению его линии 511 (c — центр тяжести
// максимума в 470…550 кэВ; шкала света линейна, поэтому линия двух квантов стоит на
// 2c): ниже линии 511 (их комптон), линия 511, «511 + часть второго» (c…2c), линия
// 2c и выше неё. Площадь линии — над линейной подложкой боковых полос; шум — по
// разбросу боковых бинов вокруг той же прямой.
//
// Плечи: как есть и `--coh=0` — рэлей выключен ВЕЗДЕ (`RayleighScatter = false`:
// ни розыгрыша, ни сечения в ослаблении). Если линия 2c рождается возвратом рэлеем,
// без него она обязана исчезнуть, а линия 511 — остаться (положительный контроль
// окна: тем же окном линия 511 видна).
//
//   AnnihOutsideProbeA256 --in=<сцена.in> [--kev=2614.5] [--n=1000000] [--coh=0] [--seed=7]
//                         [--csv=<файл>]   (выписка: кэВ, канал 6, вся строка)
//   AnnihOutsideProbeA256 --matrix=<файл.rmx>   (те же полосы по сохранённым узлам)
public static class AnnihOutsideProbeA256
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string inPath = null;
        double kev = 2614.5;
        int n = 1000000;
        int seed = 7;
        bool coherent = true;
        string csvPath = null;
        string matrixPath = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=")) inPath = a.Substring(5);
            else if (a.StartsWith("--kev=")) kev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--n=")) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed=")) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a == "--coh=0") coherent = false;
            else if (a == "--coh=1") coherent = true;
            else if (a.StartsWith("--csv=")) csvPath = a.Substring(6);
            else if (a.StartsWith("--matrix=")) matrixPath = a.Substring(9);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (matrixPath != null)
        {
            return Stored(matrixPath);
        }

        if (inPath == null)
        {
            Console.Error.WriteLine("нужен --in=<сцена.in> или --matrix=<файл.rmx>");
            return 2;
        }

        GlobalConfigManager.GetInstance();
        GeometryModel g = GeometryModel.Load(inPath);
        var options = new ResponseMatrixOptions();
        MethodInfo make = typeof(ResponseMatrixBuilder).GetMethod("MakeSimulator", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo resolution = typeof(ResponseMatrixBuilder).GetMethod("ResolutionHalfWidth", BindingFlags.NonPublic | BindingFlags.Static);
        var sim = (EfficiencySimulator)make.Invoke(null, new object[] { g, options, seed, kev });
        sim.Histories = n;
        sim.ResolutionPeakHalfWidthKev = (double)resolution.Invoke(null, new object[] { options, g, kev });
        sim.RayleighScatter = sim.RayleighScatter && coherent;
        double err;
        DateTime t0 = DateTime.Now;
        double[][] rows = sim.ResponseByChannel(kev, options.BinKev, out err);
        double seconds = (DateTime.Now - t0).TotalSeconds;
        double bin = options.BinKev;
        double[] ch = rows[(int)EfficiencySimulator.ResponseChannel.AnnihilationOutside];

        double all = 0.0;
        foreach (double[] r in rows) foreach (double v in r) all += v;
        double sum6 = 0.0;
        foreach (double v in ch) sum6 += v;

        Console.WriteLine("{0} {1:F1} кэВ, n {2}, рэлей {3}, бин {4} кэВ, {5:F0} с: Σ строки {6:E4}, Σ канала 6 {7:E4} ({8:F3} % строки)",
            System.IO.Path.GetFileNameWithoutExtension(inPath), kev, n, sim.RayleighScatter ? "ВКЛ" : "ВЫКЛ",
            bin, seconds, all, sum6, 100.0 * sum6 / Math.Max(all, 1e-300));
        if (!(sum6 > 0.0))
        {
            Console.WriteLine("   канал 6 пуст");
            return 0;
        }

        // Центр линии 511: максимум в 470…550 кэВ и центр тяжести трёх бинов вокруг.
        int lo = (int)(470.0 / bin), hi = Math.Min(ch.Length - 1, (int)(550.0 / bin));
        int m = lo;
        for (int i = lo; i <= hi; i++) if (ch[i] > ch[m]) m = i;
        double w0 = 0.0, w1 = 0.0;
        for (int i = Math.Max(0, m - 1); i <= Math.Min(ch.Length - 1, m + 1); i++) { w0 += ch[i]; w1 += ch[i] * (i + 0.5) * bin; }
        double c511 = w1 / w0;
        double half = Math.Max(6.0, 3.0 * bin);     // полуширина окна линии, кэВ

        double area511, noise511, area1022, noise1022;
        Line(ch, bin, c511, half, out area511, out noise511);
        Line(ch, bin, 2.0 * c511, half, out area1022, out noise1022);

        double below = Band(ch, bin, 0.0, c511 - half);
        double line511 = Band(ch, bin, c511 - half, c511 + half);
        double between = Band(ch, bin, c511 + half, 2.0 * c511 - half);
        double line1022 = Band(ch, bin, 2.0 * c511 - half, 2.0 * c511 + half);
        double above = Band(ch, bin, 2.0 * c511 + half, double.MaxValue);
        Console.WriteLine("   линия 511 у {0:F1} кэВ, окно ±{1:F0}: полосы канала 6, доля Σ канала:", c511, half);
        Console.WriteLine("     ниже 511 (их комптон)   {0,8:F4}", below / sum6);
        Console.WriteLine("     окно 511                {0,8:F4}", line511 / sum6);
        Console.WriteLine("     511…1022 (сумма)        {0,8:F4}", between / sum6);
        Console.WriteLine("     окно 1022 (у {0:F1})    {1,8:F4}", 2.0 * c511, line1022 / sum6);
        Console.WriteLine("     выше 1022               {0,8:F4}", above / sum6);
        Console.WriteLine("   над подложкой: линия 511 {0:E3} ± {1:E2} ({2:F4} Σ канала); линия 1022 {3:E3} ± {4:E2}; 1022/511 = {5:E2} ± {6:E2}",
            area511, noise511, area511 / sum6, area1022, noise1022, area1022 / area511, noise1022 / area511);

        Console.Write("   бины у 1022:");
        int c2 = (int)(2.0 * c511 / bin);
        for (int i = Math.Max(0, c2 - 8); i <= Math.Min(ch.Length - 1, c2 + 8); i++)
        {
            Console.Write(" {0:F0}:{1:E2}", (i + 0.5) * bin, ch[i]);
        }

        Console.WriteLine();

        if (csvPath != null)
        {
            using (var w = new System.IO.StreamWriter(csvPath, false, new System.Text.UTF8Encoding(false)))
            {
                w.WriteLine("kev,channel6,row");
                for (int i = 0; i < ch.Length; i++)
                {
                    double r = 0.0;
                    foreach (double[] row in rows) r += row[i];
                    w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F1},{1:E6},{2:E6}", (i + 0.5) * bin, ch[i], r));
                }
            }
        }

        return 0;
    }

    // Склад: те же полосы канала 6 по СОХРАНЁННЫМ строкам каждого узла выше порога
    // пар (матрица читается с пути, склад не трогается).
    static int Stored(string path)
    {
        ResponseMatrix matrix = ResponseMatrix.Load(path);
        if (matrix == null)
        {
            Console.Error.WriteLine("матрица не прочитана: " + path);
            return 2;
        }

        int six = (int)EfficiencySimulator.ResponseChannel.AnnihilationOutside;
        double bin = matrix.BinKev;
        Console.WriteLine("{0}: {1}", System.IO.Path.GetFileName(path), matrix.Stamp);
        Console.WriteLine("{0,8} {1,9} {2,7} {3,7} {4,7} {5,7} {6,7} {7,7} {8,8}",
            "узел", "Σ6/Σстр", "c511", "<511", "511", "сумма", "1022", ">1022", "посл.бин");
        for (int k = 0; k < matrix.Energies.Length; k++)
        {
            float[] row6 = matrix.ChannelRows[six][k];
            if (row6 == null || row6.Length == 0)
            {
                continue;
            }

            double[] ch = new double[row6.Length];
            double sum6 = 0.0, all = 0.0, last = 0.0;
            for (int i = 0; i < ch.Length; i++)
            {
                ch[i] = row6[i];
                sum6 += ch[i];
                if (ch[i] > 0.0) last = (i + 0.5) * bin;
            }

            foreach (float[][] channel in matrix.ChannelRows)
            {
                if (channel[k] != null) foreach (float v in channel[k]) all += v;
            }

            if (!(sum6 > 0.0))
            {
                continue;
            }

            int lo = (int)(470.0 / bin), hi = Math.Min(ch.Length - 1, (int)(550.0 / bin));
            int m = lo;
            for (int i = lo; i <= hi; i++) if (ch[i] > ch[m]) m = i;
            double w0 = 0.0, w1 = 0.0;
            for (int i = Math.Max(0, m - 1); i <= Math.Min(ch.Length - 1, m + 1); i++) { w0 += ch[i]; w1 += ch[i] * (i + 0.5) * bin; }
            double c = w0 > 0.0 ? w1 / w0 : 511.0;
            double half = Math.Max(6.0, 3.0 * bin);
            Console.WriteLine("{0,8:F1} {1,9:E2} {2,7:F1} {3,7:F4} {4,7:F4} {5,7:F4} {6,7:F4} {7,7:F4} {8,8:F0}",
                matrix.Energies[k], sum6 / all, c,
                Band(ch, bin, 0.0, c - half) / sum6,
                Band(ch, bin, c - half, c + half) / sum6,
                Band(ch, bin, c + half, 2.0 * c - half) / sum6,
                Band(ch, bin, 2.0 * c - half, 2.0 * c + half) / sum6,
                Band(ch, bin, 2.0 * c + half, double.MaxValue) / sum6,
                last);
        }

        return 0;
    }

    static double Band(double[] ch, double bin, double fromKev, double toKev)
    {
        double s = 0.0;
        for (int i = 0; i < ch.Length; i++)
        {
            double e = (i + 0.5) * bin;
            if (e >= fromKev && e < toKev) s += ch[i];
        }

        return s;
    }

    // Площадь линии в окне `centre ± half` над прямой по боковым полосам
    // [centre − 3·half, centre − half) и (centre + half, centre + 3·half]; шум —
    // ср.-кв. отклонение боковых бинов от той же прямой, умноженное на √(бинов окна).
    static void Line(double[] ch, double bin, double centre, double half, out double area, out double noise)
    {
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        int k = 0;
        for (int i = 0; i < ch.Length; i++)
        {
            double e = (i + 0.5) * bin;
            double d = Math.Abs(e - centre);
            if (d > half && d <= 3.0 * half)
            {
                sx += e; sy += ch[i]; sxx += e * e; sxy += e * ch[i]; k++;
            }
        }

        double det = k * sxx - sx * sx;
        double b = det != 0.0 ? (k * sxy - sx * sy) / det : 0.0;
        double a0 = k > 0 ? (sy - b * sx) / k : 0.0;
        double r2 = 0.0;
        for (int i = 0; i < ch.Length; i++)
        {
            double e = (i + 0.5) * bin;
            double d = Math.Abs(e - centre);
            if (d > half && d <= 3.0 * half)
            {
                double r = ch[i] - (a0 + b * e);
                r2 += r * r;
            }
        }

        area = 0.0;
        int inWindow = 0;
        for (int i = 0; i < ch.Length; i++)
        {
            double e = (i + 0.5) * bin;
            if (Math.Abs(e - centre) <= half)
            {
                area += ch[i] - (a0 + b * e);
                inWindow++;
            }
        }

        noise = k > 2 ? Math.Sqrt(r2 / (k - 2) * inWindow) : double.NaN;
    }
}
