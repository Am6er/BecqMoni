using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using BecquerelMonitor;

/// <summary>
/// FWHM выделения: высота пика — вершиной параболы по логарифму, а не одним
/// шумным каналом (`AMBER231`, решение Amber 17.10.2026 «Чинить сейчас»).
///
/// Зовёт ровно то, что зовёт панель выделения, —
/// <c>EnergyResolutionCalculator.CalculateFWHM(double[], …)</c>, — и рядом
/// держит ПРЕЖНИЙ алгоритм (вершина = канал с наибольшим чистым отсчётом),
/// переписанный дословно: он — положительный контроль, что синтетика смещение
/// вообще показывает.
///
/// Плечи:
///   1. гаусс без шума на ступеньке 4 % (σ = 1.3 / 3 / 8 каналов, центр
///      0 / 0.3 / 0.5 канала) — новая ширина не хуже прежней и близка к истине;
///   2. шум Пуассона, 1500 выделений на точку: смещение среднего; прежний счёт
///      при σ = 8 и ≤ 1000 отсчётов в вершине обязан показать ≤ −2 % (контроль),
///      новый — по модулю меньше прежнего и ≤ 1.5 % при ≥ 300 отсчётов;
///   3. неудобные входы: чистый отсчёт ≤ 0 у вершины (FSA за вычетом фона),
///      пик у края выделения, плоская вершина — без исключений, запасной ход;
///      вершина и полувысота результата согласованы (полувысота — середина);
///   4. корпус: ВСЕ спектры с 137CS в manifest, окно ±1.5 FWHM модели группы —
///      новый счёт не отказывает там, где прежний считал; печать прежней и
///      новой ширины (часть корпуса — по parts.csv).
///
///   FwhmTopProbeAmber231.exe --corpus=&lt;tools\CORPUS\corpus&gt;
///
/// Коды: 0 — сошлось; 1 — расхождения (перечислены); 2 — не запустилась.
/// </summary>
static class FwhmTopProbeAmber231
{
    static readonly List<string> Bad = new List<string>();
    static int checks;
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = IC;
        string corpus = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--corpus=", StringComparison.Ordinal)) corpus = a.Substring(9);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (string.IsNullOrEmpty(corpus) || !File.Exists(Path.Combine(corpus, "manifest.csv")))
        {
            Console.Error.WriteLine("FwhmTopProbeAmber231.exe --corpus=<каталог корпуса с manifest.csv>");
            return 2;
        }

        try
        {
            RunExact();
            RunNoise();
            RunAwkward();
            RunCorpus(corpus);
        }
        catch (Exception e)
        {
            Bad.Add("проба оборвалась: " + e);
        }

        Console.WriteLine();
        if (Bad.Count == 0)
        {
            Console.WriteLine("СОШЛОСЬ: проверок {0}, расхождений нет", checks);
            return 0;
        }

        Console.WriteLine("⛔ РАЗОШЛОСЬ: проверок {0}, расхождений {1}", checks, Bad.Count);
        foreach (string b in Bad) Console.WriteLine("  - " + b);
        return 1;
    }

    static void Check(bool ok, string what)
    {
        checks++;
        Console.WriteLine("  {0} {1}", ok ? "ок" : "⛔", what);
        if (!ok) Bad.Add(what);
    }

    static string F(double v, string fmt = "F2")
    {
        return v.ToString(fmt, IC);
    }

    // ------------------------------------------------------------------
    // Синтетика
    // ------------------------------------------------------------------

    const double Sqrt2 = 1.4142135623730951;
    static readonly double FwhmPerSigma = 2.0 * Math.Sqrt(2.0 * Math.Log(2.0));

    static double Erfc(double x)
    {
        // Абрамовиц–Стиган 7.1.26 — с запасом для ступеньки подложки.
        double t = 1.0 / (1.0 + 0.3275911 * Math.Abs(x));
        double y = t * (0.254829592 + t * (-0.284496736 + t * (1.421413741 + t * (-1.453152027 + t * 1.061405429))));
        double e = y * Math.Exp(-x * x);
        return x >= 0 ? e : 2.0 - e;
    }

    static double[] Peak(int n, double x0, double sigma, double amp, double bg, double step, Random rng)
    {
        double[] y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double x = i - x0;
            double mu = amp * Math.Exp(-0.5 * x * x / (sigma * sigma)) + bg + step * 0.5 * Erfc(x / (sigma * Sqrt2));
            y[i] = rng == null ? mu : Poisson(Math.Max(mu, 0.0), rng);
        }

        return y;
    }

    static double Poisson(double mu, Random rng)
    {
        if (mu > 50.0)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return Math.Max(0.0, Math.Round(mu + Math.Sqrt(mu) * z));
        }

        double l = Math.Exp(-mu), p = 1.0;
        int k = 0;
        while (true)
        {
            p *= rng.NextDouble();
            if (p <= l) return k;
            k++;
        }
    }

    static readonly EnergyCalibration Identity = new PolynomialEnergyCalibration();

    /// <summary>Новый счёт приложения: ширина в каналах; NaN — отказ.</summary>
    static double NewWidth(double[] y, int a, int b)
    {
        EnergyResolutionResult r = EnergyResolutionCalculator.CalculateFWHM(y, y.Length, Identity, a, b);
        return r == null ? double.NaN : r.RightChannel - r.LeftChannel;
    }

    /// <summary>
    /// ПРЕЖНИЙ счёт приложения (до `AMBER231`) дословно: база — прямая между
    /// краями, вершина — канал с наибольшим чистым отсчётом, полувысота —
    /// половина его чистого отсчёта.
    /// </summary>
    static double OldWidth(double[] counts, int startChannel, int endChannel)
    {
        int centroid = startChannel;
        double s = counts[startChannel], e = counts[endChannel];
        Func<int, double> baseAt = ch => s + (e - s) * (ch - startChannel) / (double)(endChannel - startChannel);
        double best = counts[centroid] - baseAt(centroid);
        for (int i = startChannel; i <= endChannel; i++)
        {
            if (counts[i] - baseAt(i) > best) { best = counts[i] - baseAt(i); centroid = i; }
        }

        double halfNet = best / 2.0;
        if (!(halfNet > 0.0)) return double.NaN;
        Func<int, double> net = ch => counts[ch] - baseAt(ch);
        double left = -1.0;
        for (int j = startChannel + 1; j <= centroid; j++)
        {
            double nj = net(j);
            if (nj > halfNet)
            {
                double np = net(j - 1);
                if (!(nj > np)) return double.NaN;
                left = (j - 1) + (halfNet - np) / (nj - np);
                break;
            }
        }

        if (left < 0.0) return double.NaN;
        double right = -1.0;
        for (int k = endChannel - 1; k >= centroid; k--)
        {
            double nk = net(k);
            if (nk > halfNet)
            {
                double nn = net(k + 1);
                if (!(nk > nn)) return double.NaN;
                right = (k + 1) - (halfNet - nn) / (nk - nn);
                break;
            }
        }

        return right < 0.0 ? double.NaN : right - left;
    }

    static void Window(double sigma, out int n, out int a, out int b, out double mid)
    {
        double fw = FwhmPerSigma * sigma;
        int w = (int)Math.Round(1.5 * fw);
        n = 2 * w + 60;
        mid = n / 2.0;
        a = (int)Math.Round(mid) - w;
        b = (int)Math.Round(mid) + w;
    }

    static void RunExact()
    {
        Console.WriteLine("== плечо 1: гаусс без шума, ступенька 4 %, окно ±1.5 FWHM ==");
        foreach (double sigma in new[] { 1.3, 3.0, 8.0 })
        {
            double truth = FwhmPerSigma * sigma;
            int n, a, b;
            double mid;
            Window(sigma, out n, out a, out b, out mid);
            double worstNew = 0.0, worstOld = 0.0;
            foreach (double shift in new[] { 0.0, 0.3, 0.5 })
            {
                double[] y = Peak(n, mid + shift, sigma, 10000.0, 2000.0, 400.0, null);
                double wn = NewWidth(y, a, b) / truth - 1.0;
                double wo = OldWidth(y, a, b) / truth - 1.0;
                worstNew = Math.Max(worstNew, Math.Abs(wn));
                worstOld = Math.Max(worstOld, Math.Abs(wo));
            }

            // «Не хуже прежнего» — с допуском 0.1 п.п.: у широкого пика без шума обе
            // ошибки — доли десятой процента (дискретизация пересечения), и
            // равенство до последнего знака ничего не говорит о вершине.
            double limit = sigma >= 8.0 ? 0.005 : sigma >= 3.0 ? 0.01 : 0.025;
            Check(worstNew <= limit && worstNew <= worstOld + 0.001,
                  string.Format(IC, "σ = {0} кан.: худшая ошибка ширины новым {1} %, прежним {2} % (предел {3} %)",
                                sigma, F(100 * worstNew), F(100 * worstOld), F(100 * limit, "F1")));
        }
    }

    static void RunNoise()
    {
        Console.WriteLine();
        Console.WriteLine("== плечо 2: шум Пуассона, 1500 выделений на точку, ступенька 4 %, подложка 20 % ==");
        Random rng = new Random(20261017);
        foreach (double sigma in new[] { 3.0, 8.0 })
        {
            double truth = FwhmPerSigma * sigma;
            int n, a, b;
            double mid;
            Window(sigma, out n, out a, out b, out mid);
            foreach (double amp in new[] { 10000.0, 1000.0, 300.0, 100.0 })
            {
                List<double> wn = new List<double>(), wo = new List<double>();
                for (int t = 0; t < 1500; t++)
                {
                    double[] y = Peak(n, mid + rng.NextDouble() - 0.5, sigma, amp, 0.2 * amp, 0.04 * amp, rng);
                    double x = NewWidth(y, a, b);
                    double o = OldWidth(y, a, b);
                    if (!double.IsNaN(x)) wn.Add(x);
                    if (!double.IsNaN(o)) wo.Add(o);
                }

                double bn = wn.Average() / truth - 1.0;
                double bo = wo.Average() / truth - 1.0;
                double sn = Sd(wn) / truth, so = Sd(wo) / truth;
                string line = string.Format(IC,
                    "σ = {0}, вершина {1}: прежний {2} % (±{3}), новый {4} % (±{5}); отказов {6}/{7}",
                    sigma, amp, F(100 * bo), F(100 * so), F(100 * bn), F(100 * sn), 1500 - wo.Count, 1500 - wn.Count);
                if (sigma >= 8.0 && amp <= 1000.0)
                {
                    Check(bo <= -0.02, "контроль — прежний счёт смещён: " + line);
                    Check(Math.Abs(bn) < Math.Abs(bo), "новый по модулю меньше прежнего: " + line);
                }

                if (amp >= 300.0)
                {
                    Check(Math.Abs(bn) <= 0.015, "|смещение| нового ≤ 1.5 %: " + line);
                }
                else
                {
                    Console.WriteLine("  · " + line);
                }

                Check(wn.Count >= wo.Count, "новый не отказывает чаще прежнего: " + line);
            }
        }
    }

    static double Sd(List<double> v)
    {
        double m = v.Average();
        return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / v.Count);
    }

    static void RunAwkward()
    {
        Console.WriteLine();
        Console.WriteLine("== плечо 3: неудобные входы ==");
        int n, a, b;
        double mid;
        Window(8.0, out n, out a, out b, out mid);

        // FSA за вычетом фона: крылья ниже нуля, у вершины тоже есть ≤ 0.
        double[] y = Peak(n, mid, 8.0, 1000.0, -600.0, 0.0, null);
        y[(int)mid + 2] = -5.0;
        EnergyResolutionResult r = Safe(y, a, b);
        Check(r != null || double.IsNaN(OldWidth(y, a, b)),
              "чистый отсчёт ≤ 0 у вершины — считает, если считал прежний: " + Describe(r));

        // Пик у края выделения: окно параболы выходит за выделение.
        double[] edge = Peak(n, a + 3.0, 8.0, 1000.0, 200.0, 0.0, null);
        EnergyResolutionResult re = Safe(edge, a, b);
        Check(true, "пик у края выделения — без исключения: " + Describe(re));

        // Плоская вершина: пять одинаковых каналов.
        double[] flat = Peak(n, mid, 8.0, 1000.0, 200.0, 40.0, null);
        for (int i = -2; i <= 2; i++) flat[(int)mid + i] = flat[(int)mid];
        EnergyResolutionResult rf = Safe(flat, a, b);
        Check(rf != null, "плоская вершина: " + Describe(rf));

        // Согласованность для отрисовки: полувысота — середина вершины над базой.
        double[] g = Peak(n, mid + 0.3, 8.0, 1000.0, 200.0, 40.0, new Random(7));
        EnergyResolutionResult rg = Safe(g, a, b);
        Check(rg != null && Math.Abs((rg.MaxValue - rg.MaxBaseValue) - 2.0 * (rg.HalfValue - rg.MaxBaseValue)) < 1e-6,
              "вершина − база = 2 × (полувысота − база): " + Describe(rg));
    }

    static EnergyResolutionResult Safe(double[] y, int a, int b)
    {
        try
        {
            return EnergyResolutionCalculator.CalculateFWHM(y, y.Length, Identity, a, b);
        }
        catch (Exception e)
        {
            Bad.Add("исключение: " + e.Message);
            return null;
        }
    }

    static string Describe(EnergyResolutionResult r)
    {
        return r == null
            ? "отказ (null)"
            : string.Format(IC, "ширина {0} кан., вершина {1}, полувысота {2}, база {3}",
                            F(r.RightChannel - r.LeftChannel), F(r.MaxValue, "F1"), F(r.HalfValue, "F1"), F(r.MaxBaseValue, "F1"));
    }

    // ------------------------------------------------------------------
    // Корпус
    // ------------------------------------------------------------------

    static void RunCorpus(string corpus)
    {
        Console.WriteLine();
        Console.WriteLine("== плечо 4: корпус, все спектры с 137CS, пик 661.657 кэВ, окно ±1.5 FWHM модели группы ==");
        List<Dictionary<string, string>> rows = Csv(Path.Combine(corpus, "manifest.csv"));
        Dictionary<string, string> parts = Csv(Path.Combine(corpus, "parts.csv"))
            .ToDictionary(r => r["spectrum"], r => r["part"]);
        List<Dictionary<string, string>> cs = rows
            .Where(r => r["nuclides"].IndexOf("137CS", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        Console.WriteLine("  спектров: {0}", cs.Count);
        Check(cs.Count > 0, "в manifest есть спектры с 137CS");
        Console.WriteLine("  {0,-26} {1,-8} {2,-8} {3,8} {4,8} {5,8}", "спектр", "группа", "часть", "прежн.%", "нов.%", "разн.%");
        Dictionary<string, List<double>> diffs = new Dictionary<string, List<double>>();
        foreach (Dictionary<string, string> row in cs)
        {
            string key = row["key"];
            string part;
            if (!parts.TryGetValue(key, out part)) part = "?";
            int index = int.Parse(string.IsNullOrEmpty(row["result_data"]) ? "0" : row["result_data"], IC);
            double pct = double.Parse(row["fwhm_662_pct"], IC);
            double[] y;
            PolynomialEnergyCalibration cal;
            Load(Path.Combine(corpus, "spectra", key + ".xml"), index, out y, out cal);
            double c0 = ChannelOf(cal, 661.657, y.Length);
            double dedc = cal.ChannelToEnergy(c0 + 0.5) - cal.ChannelToEnergy(c0 - 0.5);
            double fw = pct / 100.0 * 661.657 / dedc;
            int lo = (int)(c0 - 0.5 * fw), hi = (int)(c0 + 0.5 * fw) + 1;
            int top = lo;
            double bestSm = double.MinValue;
            for (int i = Math.Max(lo, 1); i < Math.Min(hi, y.Length - 2); i++)
            {
                double sm = (y[i - 1] + y[i] + y[i + 1]) / 3.0;
                if (sm > bestSm) { bestSm = sm; top = i; }
            }

            int a = (int)Math.Round(top - 1.5 * fw), b = (int)Math.Round(top + 1.5 * fw);
            double wo = OldWidth(y, a, b);
            EnergyResolutionResult rn = EnergyResolutionCalculator.CalculateFWHM(y, y.Length, cal, a, b);
            double wn = rn == null ? double.NaN : rn.RightChannel - rn.LeftChannel;
            Console.WriteLine("  {0,-26} {1,-8} {2,-8} {3,8} {4,8} {5,8}", key, row["det"], part,
                              F(100 * wo / fw - 100), F(100 * wn / fw - 100), F(100 * (wn - wo) / wo));
            if (!double.IsNaN(wo))
            {
                Check(!double.IsNaN(wn), key + ": новый счёт не отказал там, где прежний считал");
            }

            if (!double.IsNaN(wo) && !double.IsNaN(wn))
            {
                List<double> list;
                if (!diffs.TryGetValue(part, out list)) diffs[part] = list = new List<double>();
                list.Add((wn - wo) / wo);
            }
        }

        foreach (KeyValuePair<string, List<double>> kv in diffs)
        {
            List<double> d = kv.Value.OrderBy(x => x).ToList();
            Console.WriteLine("  часть {0}: спектров {1}, (новый − прежний)/прежний: медиана {2} %, размах {3} … {4} %",
                              kv.Key, d.Count, F(100 * d[d.Count / 2]), F(100 * d[0]), F(100 * d[d.Count - 1]));
        }
    }

    static void Load(string path, int index, out double[] y, out PolynomialEnergyCalibration cal)
    {
        XmlDocument doc = new XmlDocument();
        doc.Load(path);
        XmlNodeList list = doc.SelectNodes("/ResultDataFile/ResultDataList/ResultData");
        XmlNode es = list[index].SelectSingleNode("EnergySpectrum");
        double[] coeffs = es.SelectNodes("EnergyCalibration/Coefficients/Coefficient").Cast<XmlNode>()
            .Select(x => double.Parse(x.InnerText, NumberStyles.Float, IC)).ToArray();
        cal = new PolynomialEnergyCalibration { PolynomialOrder = coeffs.Length - 1, Coefficients = coeffs };
        y = es.SelectNodes("Spectrum/DataPoint").Cast<XmlNode>()
            .Select(x => double.Parse(x.InnerText, NumberStyles.Float, IC)).ToArray();
    }

    static double ChannelOf(EnergyCalibration cal, double e, int n)
    {
        double lo = 0.0, hi = n - 1;
        for (int i = 0; i < 80; i++)
        {
            double m = 0.5 * (lo + hi);
            if (cal.ChannelToEnergy(m) < e) lo = m; else hi = m;
        }

        return 0.5 * (lo + hi);
    }

    /// <summary>CSV с кавычками (в manifest графа «why» содержит запятые).</summary>
    static List<Dictionary<string, string>> Csv(string path)
    {
        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        List<string> header = Split(lines[0].TrimStart('﻿'));
        List<Dictionary<string, string>> rows = new List<Dictionary<string, string>>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            List<string> cells = Split(lines[i]);
            Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int c = 0; c < header.Count; c++) row[header[c]] = c < cells.Count ? cells[c] : "";
            rows.Add(row);
        }

        return rows;
    }

    static List<string> Split(string line)
    {
        List<string> cells = new List<string>();
        StringBuilder cur = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cur.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }

        cells.Add(cur.ToString());
        return cells;
    }
}
