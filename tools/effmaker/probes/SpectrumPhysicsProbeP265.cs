// П265 (14.10.2026): повторная ревизия физики обработки спектра после волны 05.10.2026
// (`c1051872`, AMBER163–AMBER204). Журнал — handover/handover-2026-10-14-p265-spectrum-physics-recheck.md.
// Плечи (разовые замеры, не сторож; печатают факты, «до/после» — сравнением двух сборок):
//   --arm=subtract  поиск пиков в режиме «фон вычтен»: фантомы на спектре без источника
//                   и SNR настоящего пика против режима без вычитания (--seeds=, --snr=);
//   --arm=real      то же на настоящих спектрах с фоном (--list=файл со списком .xml,
//                   --csv=вывод); приборы — по ссылке спектра из config\device рядом с exe;
//   --arm=cal       шкала энергии: круг «канал → энергия → канал», проверка степени 2,
//                   СКО подгонки на спектре больше 8192 каналов (и контроль на 1024);
//   --arm=rescale   смена числа каналов (AMBER171): E_нов(j) = E_стар(m·j + (m−1)/2), отсчёты;
//   --arm=smooth    SMA/WMA (AMBER201 Р2): сдвиг центра и площадь при окнах 2…8;
//   --arm=refine    уточнение центра (AMBER201 Р5) у краёв спектра;
//   --arm=edge      поиск пиков на пустом спектре, на одном отсчёте, на 16384 каналах.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BecquerelMonitor;
using BecquerelMonitor.Utils;

static class SpectrumPhysicsProbeP265
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string F(double v, int d) { return v.ToString("F" + d.ToString(Inv), Inv); }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        System.Threading.Thread.CurrentThread.CurrentCulture = Inv;
        string arm = "subtract";
        int seeds = 20;
        double minSnr = 10.0;
        var files = new List<string>();
        string csv = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--arm=", StringComparison.Ordinal)) arm = a.Substring(6);
            else if (a.StartsWith("--file=", StringComparison.Ordinal)) files.Add(a.Substring(7));
            else if (a.StartsWith("--list=", StringComparison.Ordinal)) files.AddRange(System.IO.File.ReadAllLines(a.Substring(7)).Where(l => l.Trim().Length > 0));
            else if (a.StartsWith("--csv=", StringComparison.Ordinal)) csv = a.Substring(6);
            else if (a.StartsWith("--seeds=", StringComparison.Ordinal)) seeds = int.Parse(a.Substring(8), Inv);
            else if (a.StartsWith("--snr=", StringComparison.Ordinal)) minSnr = double.Parse(a.Substring(6), Inv);
            else { Console.Error.WriteLine("неизвестный ключ " + a); return 2; }
        }
        DeviceType.InitializeDeviceTypes();
        if (arm == "subtract") return Subtract(seeds, minSnr);
        if (arm == "cal") return Cal();
        if (arm == "edge") return Edge(minSnr);
        if (arm == "real") return Real(files, csv);
        if (arm == "rescale") return Rescale();
        if (arm == "smooth") return Smooth();
        if (arm == "refine") return Refine();
        Console.Error.WriteLine("неизвестное плечо " + arm);
        return 2;
    }

    // ------------------------------------------------------------------ общее

    static int Poisson(Random r, double lambda)
    {
        if (lambda <= 0) return 0;
        if (lambda < 40)
        {
            double l = Math.Exp(-lambda), p = 1.0; int k = 0;
            do { k++; p *= r.NextDouble(); } while (p > l);
            return k - 1;
        }
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Math.Max(0, (int)Math.Round(lambda + Math.Sqrt(lambda) * z));
    }

    static PolynomialEnergyCalibration Linear(double c0, double c1, int n)
    {
        var p = new PolynomialEnergyCalibration { Coefficients = new[] { c0, c1 }, PolynomialOrder = 1 };
        p.CheckCalibration(n);
        return p;
    }

    static EnergySpectrum Make(int[] counts, PolynomialEnergyCalibration cal, double time)
    {
        var es = new EnergySpectrum(1.0, counts.Length);
        es.EnergyCalibration = cal.Clone();
        es.Spectrum = (int[])counts.Clone();
        long t = 0; foreach (int c in counts) t += c;
        es.TotalPulseCount = t; es.ValidPulseCount = t;
        es.MeasurementTime = time; es.LiveTime = time;
        return es;
    }

    static ResultData Rd(EnergySpectrum fg, EnergySpectrum bg, FwhmCalibration fwhm, double minSnr, int concat)
    {
        var rd = new ResultData();
        rd.EnergySpectrum = fg;
        rd.BackgroundEnergySpectrum = bg;
        var cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;
        cfg.Min_SNR = minSnr;
        cfg.Ch_Concat = concat;
        cfg.Min_Range = 30;
        cfg.Max_Range = 2800;
        cfg.Min_FWHM_Tol = 10;
        cfg.Max_FWHM_Tol = 190;
        cfg.Max_Items = 40;
        cfg.UseCenterOfMassCentroid = true;
        rd.FwhmCalibration = fwhm;
        return rd;
    }

    static List<Peak> Detect(ResultData rd, BackgroundMode mode)
    {
        return new PeakDetector().DetectPeak(rd, mode, SmoothingMethod.None, null, new List<NuclideDefinition>());
    }

    // ------------------------------------------------------------ вычитание фона

    static int Subtract(int seeds, double minSnr)
    {
        const int n = 1024;
        const double kevPerCh = 3.0;
        var cal = Linear(0.0, kevPerCh, n);
        // ПШПВ 7 % на 662 кэВ (220.6 канала): F² = 9 + 1.034·ch, каналы.
        var fwhm = new SimpleSqrtFwhmCalibration { Coefficients = new[] { 9.0, 1.034 } };
        double peakCh = 400.0;                       // 1200 кэВ
        double sigma = fwhm.ChannelToFwhm(peakCh) / 2.35482;

        Console.WriteLine("=== П265 subtract: поиск пиков в режиме «фон вычтен» (Min_SNR " + F(minSnr, 1) + ", "
                          + seeds.ToString(Inv) + " зёрен, 1024 канала, ПШПВ 7 % на 662 кэВ) ===");
        foreach (double level in new[] { 200.0, 2000.0 })
        {
            foreach (double bgRatio in new[] { 1.0, 3.0 })
            {
                foreach (double area in new[] { 0.0, 1.0 })
                {
                    // Площадь пика: столько, чтобы SNR согласованного фильтра против
                    // ВЕРНОЙ дисперсии (спектр + фон) был ≈ 15.
                    double cont = level * Math.Exp(-peakCh / 250.0) + level * 0.05;
                    double varPerCh = cont * (1.0 + 1.0 / bgRatio);
                    double a = area * 15.0 * Math.Sqrt(varPerCh * 2.0 * Math.Sqrt(Math.PI) * sigma);
                    int phInv = 0, phSub = 0, foundInv = 0, foundSub = 0;
                    double snrInv = 0, snrSub = 0;
                    for (int s = 0; s < seeds; s++)
                    {
                        var r = new Random(1000 + s);
                        int[] fg = new int[n], bg = new int[n];
                        for (int k = 0; k < n; k++)
                        {
                            double b = level * Math.Exp(-k / 250.0) + level * 0.05;
                            double pk = a * Math.Exp(-0.5 * (k - peakCh) * (k - peakCh) / (sigma * sigma)) / (Math.Sqrt(2 * Math.PI) * sigma);
                            fg[k] = Poisson(r, b + pk);
                            bg[k] = Poisson(r, b * bgRatio);
                        }
                        var fgEs = Make(fg, cal, 1000.0);
                        var bgEs = Make(bg, cal, 1000.0 * bgRatio);
                        foreach (BackgroundMode mode in new[] { BackgroundMode.Invisible, BackgroundMode.Substract })
                        {
                            var peaks = Detect(Rd(fgEs, bgEs, fwhm.Clone(), minSnr, n), mode);
                            int phantoms = 0; bool found = false; double snr = 0;
                            foreach (Peak p in peaks)
                            {
                                if (a > 0 && Math.Abs(p.Channel - peakCh) <= 0.5 * fwhm.ChannelToFwhm(peakCh)) { found = true; snr = p.SNR; }
                                else phantoms++;
                            }
                            if (mode == BackgroundMode.Invisible) { phInv += phantoms; if (found) { foundInv++; snrInv += snr; } }
                            else { phSub += phantoms; if (found) { foundSub++; snrSub += snr; } }
                        }
                    }
                    // Ожидание при верной статистике: SNR «фон вычтен» / SNR без вычитания
                    // = √(n_s / (n_s + k²·n_b)) у пика (k = 1/фон×), без подложки пика — оценка снизу.
                    double kk = 1.0 / bgRatio;
                    double expectRatio = Math.Sqrt(1.0 / (1.0 + kk * kk * bgRatio));
                    Console.WriteLine(string.Format(Inv,
                        "уровень {0,5} фон×{1}, пик {2}: фантомов на спектр — без вычитания {3,6}, «фон вычтен» {4,6}; пик найден {5}/{6} и {7}/{6}, ср. SNR {8} и {9} (ожидание отношения ≈ {10})",
                        level, bgRatio, a > 0 ? "есть" : "нет",
                        F(phInv / (double)seeds, 2), F(phSub / (double)seeds, 2),
                        foundInv, seeds, foundSub,
                        foundInv > 0 ? F(snrInv / foundInv, 1) : "—", foundSub > 0 ? F(snrSub / foundSub, 1) : "—",
                        F(expectRatio, 3)));
                }
            }
        }
        return 0;
    }

    // ------------------------------------------------------------ шкала энергии

    static int Cal()
    {
        Console.WriteLine("=== П265 cal: шкала энергии ===");
        // (1) круг канал → энергия → канал для степеней 1–4, E(0) < 0, вогнутые и выпуклые.
        var cases = new List<Tuple<string, double[], int>>
        {
            Tuple.Create("лин E(0)=−20", new[] { -20.0, 3.0 }, 1024),
            Tuple.Create("кв выпукл E(0)=−5", new[] { -5.0, 0.3, 1e-6 }, 8192),
            Tuple.Create("кв вогн N=8192 (AMBER190)", new[] { 0.0, 0.40, -5e-6 }, 8192),
            Tuple.Create("кв выпукл E(0)=+60 (корень E=0 вне)", new[] { 60.0, 1.0, 1e-3 }, 1024),
            Tuple.Create("куб", new[] { -10.0, 2.9, 1e-4, -5e-8 }, 1024),
            Tuple.Create("4 ст", new[] { -10.0, 2.9, 1e-4, -5e-8, 1e-11 }, 1024),
            Tuple.Create("лин 16384", new[] { -1.0, 0.2, }, 16384),
        };
        foreach (var c in cases)
        {
            var p = new PolynomialEnergyCalibration { Coefficients = (double[])c.Item2.Clone(), PolynomialOrder = c.Item2.Length - 1 };
            bool ok = p.CheckCalibration(c.Item3);
            double worst = 0; double at = 0; int bad = 0;
            for (double ch = 0; ch <= c.Item3 - 1; ch += Math.Max(1, c.Item3 / 2048) + 0.37)
            {
                double e = p.ChannelToEnergy(ch);
                double back = PolynomialEnergyCalibration.ChannelOf(p, e, c.Item3);
                double d = back - ch;
                if (Math.Abs(d) > Math.Abs(worst)) { worst = d; at = ch; }
                if (Math.Abs(d) > 1e-6) bad++;
            }
            Console.WriteLine(string.Format(Inv, "  {0,-38} N={1,5} годна {2,-5} круг: худшее {3} кан. (у {4}), промахов >1e-6: {5}",
                c.Item1, c.Item3, ok, worst.ToString("G4", Inv), F(at, 2), bad));
        }

        // (2) СКО подгонки (строка состояния панели калибровки) у шкалы > 8192 каналов.
        var pts = new List<CalibrationPoint>
        {
            new CalibrationPoint(1000, 199.0m, 100),
            new CalibrationPoint(6000, 1199.0m, 100),
            new CalibrationPoint(12000, 2399.0m, 100),
        };
        double[] fit = CalibrationSolver.Solve(pts, 1);
        Console.WriteLine("  СКО подгонки (CalibrationSolver.MSE), точная прямая через три точки 1000/6000/12000 кан. (16384): "
                          + CalibrationSolver.MSE(fit, pts).ToString("G6", Inv) + " кэВ² (ожидается ≈ 0)");
        Console.WriteLine("  то же WMSE: " + CalibrationSolver.WMSE(fit, pts).ToString("G6", Inv));
        // Контроль: шкала ≤ 8192 каналов — СКО обязано остаться прежним (до округления).
        var pts2 = new List<CalibrationPoint>
        {
            new CalibrationPoint(20, 59.5m, 500), new CalibrationPoint(220, 661.7m, 2000),
            new CalibrationPoint(390, 1173.2m, 300), new CalibrationPoint(443, 1332.5m, 250), new CalibrationPoint(486, 1460.8m, 100),
        };
        double[] fit2 = CalibrationSolver.Solve(pts2, 2);
        Console.WriteLine("  контроль 1024 кан., квадратичная по 5 опорам: MSE " + CalibrationSolver.MSE(fit2, pts2).ToString("R", Inv)
                          + ", WMSE " + CalibrationSolver.WMSE(fit2, pts2).ToString("R", Inv));
        return 0;
    }

    // ------------------------------------------------------------ настоящие спектры с фоном

    static ResultData LoadXml(string path)
    {
        using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
        {
            var rdf = (ResultDataFile)new System.Xml.Serialization.XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
            return rdf.ResultDataList[0];
        }
    }

    static int Real(List<string> files, string csv)
    {
        var lines = new List<string> { "spectrum;mode;channel;energy;snr;fwhm_ch" };
        Console.WriteLine("=== П265 real: поиск пиков без вычитания и «фон вычтен», настройки прибора спектра ===");
        foreach (string f in files)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(f);
            ResultData rd;
            try { rd = LoadXml(f); }
            catch (Exception ex) { Console.WriteLine("  " + name + ": ⛔ не прочитан: " + ex.Message); continue; }
            if (rd.BackgroundEnergySpectrum == null) { Console.WriteLine("  " + name + ": фона нет — пропущен"); continue; }
            var es = rd.EnergySpectrum;
            ((PolynomialEnergyCalibration)es.EnergyCalibration).CheckCalibration(es.NumberOfChannels);
            if (es.TotalPulseCount == 0) { long t = 0; foreach (int c in es.Spectrum) t += c; es.TotalPulseCount = t; es.ValidPulseCount = t; }
            string note = ProbeDeviceConfig.Attach(rd);
            var cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (rd.FwhmCalibration == null && cfg != null)
                rd.FwhmCalibration = cfg.FwhmCalibration ?? FwhmCalibration.DefaultCalibration(cfg, es.EnergyCalibration);
            if (rd.FwhmCalibration == null) { Console.WriteLine("  " + name + ": ⛔ нет ПШПВ (" + note + ")"); continue; }
            var counts = new Dictionary<BackgroundMode, List<Peak>>();
            foreach (BackgroundMode mode in new[] { BackgroundMode.Invisible, BackgroundMode.Substract })
            {
                List<Peak> peaks;
                try { peaks = Detect(rd, mode); }
                catch (Exception ex) { Console.WriteLine("  " + name + " " + mode + ": ⛔ " + ex.GetType().Name + ": " + ex.Message); peaks = new List<Peak>(); }
                counts[mode] = peaks;
                foreach (Peak p in peaks)
                    lines.Add(string.Join(";", name, mode.ToString(), p.Channel.ToString(Inv), F(p.Energy, 2), F(p.SNR, 2), F(p.FWHM, 2)));
            }
            double fgT = es.EffectiveLiveTime, bgT = rd.BackgroundEnergySpectrum.EffectiveLiveTime;
            Console.WriteLine(string.Format(Inv, "  {0,-28} N={1,5} Min_SNR {2,4}  фон/спектр по времени {3,6}  пиков: без вычитания {4,3}, «фон вычтен» {5,3}  ({6})",
                name, es.NumberOfChannels, cfg != null ? F(cfg.Min_SNR, 1) : "?", F(bgT / Math.Max(fgT, 1e-9), 2),
                counts[BackgroundMode.Invisible].Count, counts[BackgroundMode.Substract].Count, note.Length > 40 ? note.Substring(0, 40) : note));
            Console.WriteLine("      вычтен: " + string.Join("  ", counts[BackgroundMode.Substract].OrderBy(p => p.Energy)
                .Select(p => F(p.Energy, 0) + "(" + F(p.SNR, 0) + ")")));
        }
        if (csv != null) System.IO.File.WriteAllLines(csv, lines);
        return 0;
    }

    // ------------------------------------------------------------ смена числа каналов (AMBER171)

    static double PolyAt(double[] c, double x)
    {
        double v = 0.0;
        for (int i = c.Length - 1; i >= 0; i--) v = v * x + c[i];
        return v;
    }

    static int Rescale()
    {
        Console.WriteLine("=== П265 rescale: шкала энергии при смене числа каналов (AMBER171) ===");
        var cases = new List<Tuple<string, double[], int, int>>
        {
            Tuple.Create("лин 8192→1024", new[] { -14.8, 0.3793 }, 8192, 1024),
            Tuple.Create("4 ст 8192→1024", new[] { -14.8105347832835, 0.379344706765991, -2.23164703121927e-05, 8.15559562184467e-09, -7.30570097982446e-13 }, 8192, 1024),
            Tuple.Create("кв 1024→8192", new[] { -21.4, 3.005, 1e-4 }, 1024, 8192),
            Tuple.Create("кв 4096→3000", new[] { -5.0, 0.73, 2e-6 }, 4096, 3000),
            Tuple.Create("5 ст 2048→1024", new[] { -3.0, 1.45, 1e-5, -2e-9, 3e-13, -1e-17 }, 2048, 1024),
        };
        int bad = 0;
        foreach (var c in cases)
        {
            int nOld = c.Item3, nNew = c.Item4;
            var cal = new PolynomialEnergyCalibration { Coefficients = (double[])c.Item2.Clone(), PolynomialOrder = c.Item2.Length - 1 };
            cal.CheckCalibration(nOld);
            var es = new EnergySpectrum(1.0, nOld);
            es.EnergyCalibration = cal;
            for (int k = 0; k < nOld; k++) es.Spectrum[k] = 10;
            es.MeasurementTime = 100; es.LiveTime = 100; es.TotalPulseCount = 10L * nOld; es.ValidPulseCount = es.TotalPulseCount;
            EnergySpectrum ns = nNew < nOld ? SpectrumAriphmetics.ConcatSpectrum(es, nNew) : SpectrumAriphmetics.RestoreSpectrum(es, nNew);
            double m = (double)nOld / nNew, s = (m - 1.0) / 2.0;
            double worst = 0;
            for (int j = 0; j < nNew; j++)
            {
                double want = PolyAt(c.Item2, m * j + s);
                double got = ns.EnergyCalibration.ChannelToEnergy(j);
                if (Math.Abs(got - want) > Math.Abs(worst)) worst = got - want;
            }
            long sum = 0; foreach (int v in ns.Spectrum) sum += v;
            bool ok = Math.Abs(worst) < 1e-6 && sum == es.TotalPulseCount;
            if (!ok) bad++;
            Console.WriteLine(string.Format(Inv, "  {0,-18} худшее E_нов(j) − E_стар(m·j+(m−1)/2) = {1} кэВ; отсчётов {2} → {3}  {4}",
                c.Item1, worst.ToString("G3", Inv), es.TotalPulseCount, sum, ok ? "ok" : "⛔"));
        }
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------ сглаживание (AMBER201 Р2)

    static int Smooth()
    {
        Console.WriteLine("=== П265 smooth: сдвиг центра и сохранение площади SMA/WMA ===");
        int n = 1000;
        double[] g = new double[n];
        for (int k = 0; k < n; k++) g[k] = 1000.0 * Math.Exp(-0.5 * Math.Pow((k - 500.0) / 5.0, 2));
        var sa = new SpectrumAriphmetics();
        int bad = 0;
        foreach (int w in new[] { 2, 3, 4, 5, 6, 7, 8 })
        {
            double[] sma = sa.SMA2(g, w, countlimit: int.MaxValue, progressive: false);
            double[] wma = sa.WMA2(g, w, countlimit: int.MaxValue, progressive: false);
            Func<double[], double> com = a => { double s0 = 0, s1 = 0; for (int k = 460; k <= 540; k++) { s0 += a[k]; s1 += a[k] * k; } return s1 / s0 - 500.0; };
            Func<double[], double> area = a => { double s = 0; for (int k = 0; k < n; k++) s += a[k]; return s; };
            double a0 = area(g);
            double cs = com(sma), cw = com(wma), as_ = area(sma) / a0 - 1, aw = area(wma) / a0 - 1;
            bool ok = Math.Abs(cs) < 1e-9 && Math.Abs(cw) < 1e-9 && Math.Abs(as_) < 1e-9 && Math.Abs(aw) < 1e-9;
            if (!ok) bad++;
            Console.WriteLine(string.Format(Inv, "  окно {0}: сдвиг SMA {1}, WMA {2} кан.; площадь SMA {3}, WMA {4}  {5}",
                w, cs.ToString("G3", Inv), cw.ToString("G3", Inv), as_.ToString("G3", Inv), aw.ToString("G3", Inv), ok ? "ok" : "⛔"));
        }
        return bad == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------ уточнение центра (AMBER201 Р5)

    static int Refine()
    {
        Console.WriteLine("=== П265 refine: центр пика у края спектра и у несимметричной формы ===");
        const int n = 1024;
        var cal = Linear(0.0, 3.0, n);
        var fwhm = new SimpleSqrtFwhmCalibration { Coefficients = new[] { 9.0, 1.034 } };
        foreach (double c0 in new[] { 8.3, 15.7, 40.2, 400.4, 990.6, 1012.3 })
        {
            double sig = fwhm.ChannelToFwhm(c0) / 2.35482;
            int[] sp = new int[n];
            for (int k = 0; k < n; k++)
                sp[k] = (int)Math.Round(20 + 2e5 / (Math.Sqrt(2 * Math.PI) * sig) * Math.Exp(-0.5 * Math.Pow((k - c0) / sig, 2)));
            var rd = Rd(Make(sp, cal, 100), null, fwhm.Clone(), 4.0, 1024);
            ((FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig).Min_Range = 0;
            ((FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig).Max_Range = 3100;
            var pk = Detect(rd, BackgroundMode.Invisible);
            Peak best = pk.OrderBy(p => Math.Abs(p.Energy / 3.0 - c0)).FirstOrDefault();
            Console.WriteLine(string.Format(Inv, "  гаусс у {0,7} (σ {1}): найдено {2}, ближайший центр {3} кан. (Δ {4})",
                F(c0, 1), F(sig, 2), pk.Count, best != null ? F(best.Energy / 3.0, 3) : "—",
                best != null ? (best.Energy / 3.0 - c0).ToString("+0.000;-0.000", Inv) : "—"));
        }
        return 0;
    }

    // ------------------------------------------------------------ края

    static int Edge(double minSnr)
    {
        Console.WriteLine("=== П265 edge: пустой спектр, один отсчёт, 16384 канала ===");
        var fwhm = new SimpleSqrtFwhmCalibration { Coefficients = new[] { 9.0, 1.034 } };
        foreach (int n in new[] { 1024, 16384 })
        {
            var cal = Linear(0.0, 3000.0 / n, n);
            var f = n == 1024 ? fwhm : new SimpleSqrtFwhmCalibration { Coefficients = new[] { 9.0 * 256, 1.034 * 16 } };
            var zero = Make(new int[n], cal, 100);
            string ns = n.ToString(Inv);
            try { Console.WriteLine("  N=" + ns + " нули: пиков " + Detect(Rd(zero, null, f.Clone(), minSnr, 1024), BackgroundMode.Invisible).Count.ToString(Inv)); }
            catch (Exception ex) { Console.WriteLine("  N=" + ns + " нули: ⛔ " + ex.GetType().Name + ": " + ex.Message); }
            int[] one = new int[n]; one[n / 2] = 1;
            try { Console.WriteLine("  N=" + ns + " один отсчёт: пиков " + Detect(Rd(Make(one, cal, 100), null, f.Clone(), minSnr, 1024), BackgroundMode.Invisible).Count.ToString(Inv)); }
            catch (Exception ex) { Console.WriteLine("  N=" + ns + " один отсчёт: ⛔ " + ex.GetType().Name + ": " + ex.Message); }
            int[] big = new int[n];
            var r = new Random(7);
            double sig = f.ChannelToFwhm(n * 0.4) / 2.35482;
            for (int k = 0; k < n; k++) big[k] = Poisson(r, 50 + 1e5 / (Math.Sqrt(2 * Math.PI) * sig) * Math.Exp(-0.5 * Math.Pow((k - n * 0.4) / sig, 2)));
            var t0 = DateTime.UtcNow;
            var pk = Detect(Rd(Make(big, cal, 100), null, f.Clone(), minSnr, 1024), BackgroundMode.Invisible);
            Console.WriteLine(string.Format(Inv, "  N={0} пик у {1}: найдено {2}, ближайший {3}, {4} с",
                n, F(n * 0.4, 1), pk.Count, pk.Count > 0 ? F(pk.OrderBy(q => Math.Abs(q.Channel - n * 0.4)).First().Channel, 0) : "—",
                F((DateTime.UtcNow - t0).TotalSeconds, 2)));
        }
        return 0;
    }
}
