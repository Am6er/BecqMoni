using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BecquerelMonitor.FullSpectrumAnalysis;

// П194 (AMBER141): плечо --floor — шапки с нижней границей −3σ шума данных после вычитания (FsaAnalyzer.ContinuumFloorOf), то есть ровно правка П194.
// П191: СМЕЩЕНИЕ АМПЛИТУДЫ ПРИ ПЕРЕВЗВЕШИВАНИИ ПО МОДЕЛИ, ОБОРВАННОМ НА ТРЁХ ПРОХОДАХ.
// Зовётся САМ FitOnce приложения (отражением), как его зовёт FitHuber: проход 0 — веса по данным
// 1/(max(N,1)+фон), проход k — веса 1/(max(μ̂_{k−1},1)+фон). Синтетика: пик (гаусс) на слабом континууме
// из шапок, вычтенный фон известен точно. Печатается среднее по копиям отношение амплитуды к истине
// после k проходов, k = 1 (Нейман), 2, 3 (как в приложении), … 8, и «истинный» пирсоновский предел.
public static class IrlsBiasP194
{
    static int Poisson(double mu, Random rng)
    {
        if (!(mu > 0.0)) return 0;
        if (mu > 60.0)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double v = mu + Math.Sqrt(mu) * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return v <= 0.0 ? 0 : (int)Math.Floor(v + 0.5);
        }

        double target = -mu, sum = 0.0;
        int k = 0;
        while (true)
        {
            double u = rng.NextDouble();
            if (u <= 0.0) u = double.Epsilon;
            sum += Math.Log(u);
            if (sum <= target) return k;
            k++;
        }
    }

    public static void Main(string[] args)
    {
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        int channels = 240;
        double peakArea = 300.0, sigma = 4.0, centre = 120.0, cont = 2.0, bg = 40.0;
        bool rawBg = false, floorArm = false;
        int copies = 2000, maxPasses = 8;
        foreach (string a in args)
        {
            if (a.StartsWith("--area=")) peakArea = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--cont=")) cont = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--bg=")) bg = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
            else if (a == "--rawbg") rawBg = true;
            else if (a == "--floor") floorArm = true;
            else if (a.StartsWith("--copies=")) copies = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
        }

        // образ пика единичной площади
        double[] peak = new double[channels];
        for (int i = 0; i < channels; i++)
        {
            double d = (i - centre) / sigma;
            peak[i] = Math.Exp(-0.5 * d * d) / (sigma * Math.Sqrt(2.0 * Math.PI));
        }

        // шапки континуума — 12 треугольников
        var hats = new List<double[]>();
        int knots = 12;
        double step = (channels - 1) / (double)(knots - 1);
        for (int k = 0; k < knots; k++)
        {
            double[] hat = new double[channels];
            double c = k * step;
            for (int i = 0; i < channels; i++)
            {
                double t = 1.0 - Math.Abs(i - c) / step;
                hat[i] = t > 0.0 ? t : 0.0;
            }

            hats.Add(hat);
        }

        if (rawBg)
        {
            // плечо «сырой спектр + фон колонкой»: фон не вычитается, его форма (ровная) — свободная колонка ≥ 0
            double[] bgCol = new double[channels];
            for (int i = 0; i < channels; i++) bgCol[i] = 1.0;
            hats.Add(bgCol);
            Console.WriteLine("плечо --rawbg: фон не вычитается, идёт колонкой в fixedColumns");
        }

        double[] truth = new double[channels];
        for (int i = 0; i < channels; i++) truth[i] = peakArea * peak[i] + cont;
        Console.WriteLine("пик {0} отсч. (σ {1} кан.), континуум {2}/кан, вычитаемый фон {3}/кан, каналов {4}, копий {5}",
                          peakArea, sigma, cont, bg, channels, copies);

        MethodInfo fitOnce = typeof(FsaAnalyzer).GetMethod("FitOnce", BindingFlags.Instance | BindingFlags.NonPublic);
        var analyzer = new FsaAnalyzer();
        if (floorArm && !rawBg)
        {
            typeof(FsaAnalyzer).GetField("continuumColumns", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(analyzer, hats.Count);
            // граница — та же функция приложения: ContinuumFloorOf(шапки, дисперсия канала = ожидание сырого + шум фона, как в Analyze)
            double[] bgVar = truth.Select(v => Math.Max(v + bg, 1.0) + bg).ToArray();
            object floor = typeof(FsaAnalyzer).GetMethod("ContinuumFloorOf", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { hats, bgVar, 0, channels - 1 });
            typeof(FsaAnalyzer).GetField("continuumFloor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(analyzer, floor);
            Console.WriteLine("плечо --floor: граница шапки {0:F2} (3σ шума данных)", ((double[])floor)[hats.Count / 2]);
        }
        var rng = new Random(20260930);
        double[] sumAmp = new double[maxPasses + 1];
        double[] sumAmp2 = new double[maxPasses + 1];
        double[] sumSig = new double[maxPasses + 1];
        double sumPearsonFull = 0.0;
        int zeros3 = 0; var amps3 = new List<double>();
        for (int c = 0; c < copies; c++)
        {
            double[] y = new double[channels];
            double[] variance = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                int raw = Poisson(truth[i] + bg, rng);
                y[i] = rawBg ? raw : raw - bg;
                variance[i] = rawBg ? Math.Max(raw, 1.0) : Math.Max(raw, 1.0) + bg;       // как в Analyze: max(N,1) + шум фона
            }

            double[] weights = new double[channels];
            for (int i = 0; i < channels; i++) weights[i] = 1.0 / variance[i];
            for (int pass = 1; pass <= maxPasses; pass++)
            {
                var comp = new FsaComponent("peak", FsaComponentKind.Single) { FixedTemplate = peak };
                object fit = fitOnce.Invoke(analyzer, new object[]
                {
                    new List<FsaComponent> { comp }, hats, null, null, null, 1.0, 0.0, 0, channels - 1, channels, y, weights, null
                });
                double[] amp = (double[])fit.GetType().GetField("Amplitude").GetValue(fit);
                double[] sig = (double[])fit.GetType().GetField("Sigma").GetValue(fit);
                double[] model = (double[])fit.GetType().GetField("Model").GetValue(fit);
                sumAmp[pass] += amp[0];
                sumAmp2[pass] += amp[0] * amp[0];
                sumSig[pass] += sig[0];
                if (pass == 3) { if (amp[0] == 0.0) zeros3++; amps3.Add(amp[0]); }
                // веса следующего прохода — по модели этого (ModelVariance: max(μ̂,1) + Extra)
                for (int i = 0; i < channels; i++)
                {
                    weights[i] = rawBg ? 1.0 / Math.Max(model[i], 1.0) : 1.0 / (Math.Max(model[i] + bg, 1.0) + bg);
                }
            }
        }

        amps3.Sort();
        Console.WriteLine("проход 3: копий с амплитудой 0 (зажим NNLS самого образа) {0} из {1}; медиана A/истина {2:F4}", zeros3, copies, amps3[amps3.Count / 2] / peakArea);
        Console.WriteLine("{0,6} {1,12} {2,12} {3,12} {4,10}", "проход", "A/истина", "±", "разброс/A", "σ заявл/A");
        for (int pass = 1; pass <= maxPasses; pass++)
        {
            double mean = sumAmp[pass] / copies;
            double sd = Math.Sqrt(Math.Max(sumAmp2[pass] / copies - mean * mean, 0.0));
            Console.WriteLine("{0,6} {1,12:F4} {2,12:F4} {3,12:F4} {4,10:F4}{5}", pass, mean / peakArea, sd / peakArea / Math.Sqrt(copies),
                              sd / peakArea, sumSig[pass] / copies / peakArea, pass == 3 ? "   <- как в приложении (3 прохода)" : (pass == 1 ? "   <- Нейман" : ""));
        }
    }
}

