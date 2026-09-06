using System;
using System.Globalization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Utils;

/// <summary>
/// Эффективность в пике 662 кэВ по РЕАЛЬНОМУ спектру точечного источника с
/// паспортной активностью — и она же по расчёту. Единственная точка, где обе
/// модели можно поверить действительностью.
///
///   real spectrum.xml активность_Бк [geometry.in] [расстояние_см]
/// </summary>
static class Real
{
    static double Energy = 661.657;
    static double Intensity = 0.851;     // выход на распад

    static void Main(string[] args)
    {
        GlobalConfigManager.GetInstance();
        DeviceConfigManager.GetInstance();
        NuclideDefinitionManager.GetInstance();

        string path = args[0];
        double activity = double.Parse(args[1], CultureInfo.InvariantCulture);
        // --line=энергия:выход  --keep-source (не подменять источник точечным)
        bool keepSource = false;
        bool electron = false, brems = true;
        double halfWidthFactor = -1.0;
        double window = 4.0;
        bool step = true;
        foreach (string a in args)
        {
            if (a.StartsWith("--line="))
            {
                string[] pp = a.Substring(7).Split(':');
                Energy = double.Parse(pp[0], CultureInfo.InvariantCulture);
                Intensity = double.Parse(pp[1], CultureInfo.InvariantCulture);
            }
            else if (a == "--keep-source") keepSource = true;
            else if (a == "--electron") electron = true;
            else if (a == "--no-brems") brems = false;
            // доля ПШПВ, которую событие может потерять и остаться в пике
            else if (a == "--no-step") step = false;
            else if (a.StartsWith("--window="))
                window = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--hw="))
                halfWidthFactor = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
        }

        ResultData rd = EfficiencyFitter.LoadResultData(path, 0);
        EnergySpectrum spec = rd.EnergySpectrum;
        EnergyCalibration cal = spec.EnergyCalibration;
        FwhmCalibration fwhmCal = rd.FwhmCalibration;
        int nch = spec.NumberOfChannels;
        double live = spec.LiveTime > 0 ? spec.LiveTime : spec.MeasurementTime;

        Console.WriteLine("Спектр: {0}", System.IO.Path.GetFileName(path));
        Console.WriteLine("каналов {0}, живое время {1:F0} с, всего отсчётов {2:N0}",
                          nch, live, Sum(spec.Spectrum, 0, nch - 1));

        double center = cal.EnergyToChannel(Energy);
        double fwhm = fwhmCal.ChannelToFwhm(center);
        double perCh = Math.Abs(cal.ChannelToEnergy(center + 0.5) - cal.ChannelToEnergy(center - 0.5));
        Console.WriteLine("пик: канал {0:F1}, ПШПВ {1:F2} кан = {2:F1} кэВ = {3:F2} %",
                          center, fwhm, fwhm * perCh, 100.0 * fwhm * perCh / Energy);

        // фон, если он вложен в файл
        double[] counts = new double[nch];
        for (int i = 0; i < nch; i++) counts[i] = spec.Spectrum[i];
        EnergySpectrum bg = rd.BackgroundEnergySpectrum;
        if (bg != null && bg.Spectrum != null && bg.NumberOfChannels == nch)
        {
            double bgLive = bg.LiveTime > 0 ? bg.LiveTime : bg.MeasurementTime;
            if (bgLive > 0)
            {
                double k = live / bgLive;
                for (int i = 0; i < nch; i++) counts[i] -= k * bg.Spectrum[i];
                Console.WriteLine("вычтен встроенный фон, коэффициент {0:F3}", k);
            }
        }

        // Центр ищется, а не берётся с калибровки — так делает и приложение
        // (CenterSearchFwhm = 0.3 ПШПВ). Без поиска фит на этом спектре садился
        // мимо пика на 0.2 ПШПВ и терял десятую часть площади: калибровка
        // ставит 662 кэВ на канал 1733, а пик стоит на 1712.
        double area, sigma, bestChi2 = double.MaxValue, bestCenter = center;
        area = 0.0; sigma = 0.0;
        double searchStep = Math.Max(0.1, fwhm / 20.0);
        for (double shift = -0.3 * fwhm; shift <= 0.3 * fwhm + 1e-9; shift += searchStep)
        {
            double a, sg, chi2;
            FitPeak(counts, spec.Spectrum, center + shift, fwhm, fwhmCal, nch, window, step,
                    out a, out sg, out chi2);
            if (a > 0.0 && chi2 < bestChi2)
            {
                bestChi2 = chi2;
                bestCenter = center + shift;
                area = a;
                sigma = sg;
            }
        }

        Console.WriteLine("центр фита: канал {0:F1} (калибровка даёт {1:F1}, сдвиг {2:F2} ПШПВ)",
                          bestCenter, center, (bestCenter - center) / fwhm);
        Console.WriteLine("площадь пика {0:N0} +/- {1:N0} ({2:F2} %)", area, sigma, 100.0 * sigma / area);

        double eps = area / (live * activity * Intensity);
        Console.WriteLine();
        Console.WriteLine("ИЗМЕРЕНО:  eps({2:F0}) = {0:E4}   при активности {1:N0} Бк", eps, activity, Energy);

        if (args.Length >= 4)
        {
            GeometryModel g = GeometryModel.Load(args[2]);
            if (!keepSource)
            {
                g.SourceType = GeometrySourceType.Point;
                g.PointDistance = double.Parse(args[3], CultureInfo.InvariantCulture);
            }
            double fwhmKev = fwhm * perCh;
            EfficiencySimulator sim = new EfficiencySimulator(g)
            {
                Histories = 400000,
                ElectronEscape = electron,
                Bremsstrahlung = brems,
                PeakHalfWidthKev = halfWidthFactor > 0.0 ? halfWidthFactor * fwhmKev : 0.0,
            };
            double err;
            double calc = sim.Efficiency(Energy, out err);
            Console.WriteLine("РАСЧЁТ:    eps({3:F0}) = {0:E4}   +/-{1:F2} %   ({2})",
                              calc, err, g.Describe(), Energy);
            Console.WriteLine("           электрон {0} ({1}), тормозное {2}, допуск {3:F1} кэВ",
                              electron ? "да" : "нет",
                              sim.ElectronMaterialName == "" ? "нет в ESTAR" : sim.ElectronMaterialName,
                              brems ? "да" : "нет", sim.PeakHalfWidthKev);
            Console.WriteLine();
            Console.WriteLine("расчёт / измерение = {0:F3}", calc / eps);
        }
    }

    static double Sum(int[] a, int lo, int hi)
    {
        double s = 0;
        for (int i = lo; i <= hi; i++) s += a[i];
        return s;
    }

    /// <summary>Профиль + ступенька + линейная подложка, взвешенный МНК.</summary>
    static void FitPeak(double[] y, int[] raw, double center, double fwhm,
                        FwhmCalibration cal, int nch, double window, bool step,
                        out double area, out double sigma, out double chi2)
    {
        int half = (int)Math.Ceiling(window * fwhm);
        int lo = Math.Max(0, (int)Math.Round(center) - half);
        int hi = Math.Min(nch - 1, (int)Math.Round(center) + half);
        int n = hi - lo + 1;
        int m = step ? 4 : 3;                  // профиль, единица, наклон, ступенька

        double[][] basis = new double[m][];
        for (int k = 0; k < m; k++) basis[k] = new double[n];
        double shapeSum = 0.0, mid = 0.5 * (lo + hi);
        for (int i = 0; i < n; i++)
        {
            double v = PeakShapeModel.RelativeValue(lo + i - center, fwhm, cal);
            basis[0][i] = v;
            shapeSum += v;
            basis[1][i] = 1.0;
            basis[2][i] = (lo + i - mid) / n;
        }

        if (step)
        {
            double tail = 0.0;
            for (int i = n - 1; i >= 0; i--) { tail += basis[0][i]; basis[3][i] = tail; }
            if (tail > 0) for (int i = 0; i < n; i++) basis[3][i] /= tail;
        }

        double[,] a = new double[m, m];
        double[] b = new double[m];
        for (int i = 0; i < n; i++)
        {
            double w = 1.0 / Math.Max(raw[lo + i], 1.0);
            for (int p = 0; p < m; p++)
            {
                for (int q = 0; q < m; q++) a[p, q] += w * basis[p][i] * basis[q][i];
                b[p] += w * basis[p][i] * y[lo + i];
            }
        }

        // Гаусс — Жордан над [A | I | b]
        double[,] work = new double[m, 2 * m + 1];
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < m; j++) work[i, j] = a[i, j];
            work[i, m + i] = 1.0;
            work[i, 2 * m] = b[i];
        }
        for (int c = 0; c < m; c++)
        {
            int piv = c;
            for (int r = c + 1; r < m; r++) if (Math.Abs(work[r, c]) > Math.Abs(work[piv, c])) piv = r;
            if (piv != c) for (int j = 0; j <= 2 * m; j++)
            { double t = work[c, j]; work[c, j] = work[piv, j]; work[piv, j] = t; }
            double d = work[c, c];
            for (int j = 0; j <= 2 * m; j++) work[c, j] /= d;
            for (int r = 0; r < m; r++)
            {
                if (r == c || work[r, c] == 0.0) continue;
                double f = work[r, c];
                for (int j = 0; j <= 2 * m; j++) work[r, j] -= f * work[c, j];
            }
        }

        area = work[0, 2 * m] * shapeSum;
        sigma = Math.Sqrt(Math.Max(work[0, m], 0.0)) * shapeSum;

        chi2 = 0.0;
        for (int i = 0; i < n; i++)
        {
            double model = 0.0;
            for (int p = 0; p < m; p++) model += work[p, 2 * m] * basis[p][i];
            double w = 1.0 / Math.Max(raw[lo + i], 1.0);
            chi2 += w * (y[lo + i] - model) * (y[lo + i] - model);
        }
    }
}
