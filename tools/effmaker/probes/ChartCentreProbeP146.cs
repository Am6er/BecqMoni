using BecquerelMonitor;
using System;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace ChartCentreProbeP146
{
    /// <summary>
    /// `AMBER88` (полоса П146, 24.09.2026) — ГДЕ ГЛАВНЫЙ ГРАФИК РИСУЕТ КАНАЛ
    /// ОТНОСИТЕЛЬНО ЕГО ЭНЕРГИИ.
    ///
    /// Приложение объявило «номер канала = его центр»: энергия канала k —
    /// `ChannelToEnergy(k)`. Линии библиотеки, линейка и опоры FSA ставятся
    /// ровно в E; спектр — столбиком и ломаной. Проба собирает
    /// `EnergySpectrumView` без окна (отражением, как `FsaStackShot`), кладёт
    /// спектр с известным пиком в канале k и снимает ПИКСЕЛИ:
    ///
    /// * столбик канала k (`DrawBarChart`) — какие колонки он занял и где их
    ///   середина;
    /// * вершина ломаной (`DrawLineChart`) — колонка самой высокой точки;
    /// * флажок найденного пика (`PeakX(k)` — та проекция, какой ставятся
    ///   флажки, курсор, калибровочные пики);
    /// * карта «пиксель → канал» (`TryMapPixelToChannel`) в колонке, где стоит
    ///   энергия E(k);
    /// * энергия, которую покажет курсор, наведённый на вершину ломаной
    ///   (обратная формула линейки: `(x − scrollX − left) / hs / ppe + offset`).
    ///
    /// Всё — против колонки E(k) по формуле линии библиотеки
    /// (`EnergySpectrumView.cs`, «линии нуклидов»). В режиме каналов — то же
    /// против `EnergyToChannel(E) · hs`.
    ///
    /// `--expect-centre` — все пять совпадают с E(k) в пределах одной колонки
    /// (и карта даёт ровно k); иначе код 1. Проба безоконная.
    /// </summary>
    static class Program
    {
        static int bad = 0;
        static bool expect = false;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            double h = 2.5, e0 = 10.0; int n = 1024, k = 264; int hs = 4;
            foreach (string a in args)
            {
                if (a == "--expect-centre") expect = true;
                else if (a.StartsWith("--h=")) h = double.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--hs=")) hs = int.Parse(a.Substring(5), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--k=")) k = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--n=")) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            Console.WriteLine("сборка: " + typeof(EnergySpectrumView).Assembly.Location);
            Console.WriteLine("шкала E = " + F(e0) + " + " + F(h) + "·ch, каналов " + n + ", пик в канале k = " + k
                              + " (E(k) = " + F(e0 + h * k) + " кэВ), увеличение hs = " + hs);
            Console.WriteLine();
            foreach (HorizontalUnit unit in new[] { HorizontalUnit.Energy, HorizontalUnit.Channel })
                Measure(unit, h, e0, n, k, hs);
            if (bad > 0) { Console.Error.WriteLine("ОТКАЗ: не сошлось ожиданий — " + bad); return 1; }
            Console.WriteLine("ЗАМЕР СНЯТ.");
            return 0;
        }

        static void Measure(HorizontalUnit unit, double h, double e0, int n, int k, int hs)
        {
            Console.WriteLine("=== РЕЖИМ " + (unit == HorizontalUnit.Energy ? "ЭНЕРГИИ" : "КАНАЛОВ") + " ===");
            PolynomialEnergyCalibration cal = new PolynomialEnergyCalibration();
            cal.PolynomialOrder = 1;
            cal.Coefficients = new double[] { e0, h };
            const int left = 1, height = 200;
            int width = n * hs + 20;

            // Спектр 1: один отсчётный канал k (столбик). Спектр 2: гаусс с
            // центром ровно в k (ломаная, вершина в k).
            EnergySpectrum spike = MakeSpectrum(cal, n, ch => ch == k ? 100.0 : 0.0);
            EnergySpectrum gauss = MakeSpectrum(cal, n, ch => 100.0 * Math.Exp(-0.5 * (ch - k) * (ch - k) / 9.0));

            using (EnergySpectrumView view = new EnergySpectrumView())
            {
                Setup(view, spike, cal, unit, n, left, width, height, hs, e0, h);
                double xE = unit == HorizontalUnit.Energy
                    ? ((e0 + h * k) - e0) * (1.0 / h) * hs + left
                    : cal.EnergyToChannel(e0 + h * k, n) * hs + left;
                double pxPerKev = hs / h;

                // 1. столбик
                int x0 = -1, x1 = -1;
                using (Bitmap bmp = new Bitmap(width + left + 2, height + 2))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    Invoke(view, "DrawBarChart", g, Brushes.White, spike, cal, false);
                    for (int x = 0; x < bmp.Width; x++)
                        if (bmp.GetPixel(x, height - 2).R > 128) { if (x0 < 0) x0 = x; x1 = x; }
                }
                double barMid = (x0 + x1 + 1) / 2.0;
                Report("столбик канала k: колонки " + x0 + "…" + x1 + ", середина", barMid, xE, pxPerKev);

                // 2. вершина ломаной
                Set(view, "energySpectrum", gauss);
                int vx = -1, vy = int.MaxValue;
                using (Bitmap bmp = new Bitmap(width + left + 2, height + 2))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    using (Pen pen = new Pen(Color.White, 1f))
                        Invoke(view, "DrawLineChart", g, pen, gauss, cal, false);
                    for (int x = 0; x < bmp.Width; x++)
                        for (int y = 0; y < bmp.Height; y++)
                            if (bmp.GetPixel(x, y).R > 128 && y < vy) { vy = y; vx = x; }
                }
                Report("вершина ломаной (гаусс с центром в k)", vx, xE, pxPerKev);

                // 3. флажок пика
                int peakX = (int)Invoke(view, "PeakX", (double)k);
                Report("флажок пика PeakX(k)", peakX, xE, pxPerKev);

                // 4. карта пиксель → канал в колонке E(k)
                object[] pa = new object[] { (int)Math.Floor(xE), spike, cal, false, 0 };
                MethodInfo map = typeof(EnergySpectrumView).GetMethod("TryMapPixelToChannel", BindingFlags.Instance | BindingFlags.NonPublic);
                map.Invoke(view, pa);
                int mapped = (int)pa[4];
                Console.WriteLine("  карта «пиксель → канал» в колонке E(k) = " + Math.Floor(xE) + ": канал " + mapped
                                  + (mapped == k ? "  (= k)" : "  ⛔ ≠ k"));
                if (expect && mapped != k) bad++;

                // 5. что покажет курсор на вершине ломаной
                if (unit == HorizontalUnit.Energy)
                {
                    double cursorE = (vx - 0 - left) / (double)hs / (1.0 / h) + e0;
                    Console.WriteLine("  курсор на вершине ломаной покажет " + F(cursorE) + " кэВ при E(k) = "
                                      + F(e0 + h * k) + ": разница " + F(cursorE - (e0 + h * k)) + " кэВ (h/2 = " + F(h / 2) + ")");
                    if (expect && Math.Abs(cursorE - (e0 + h * k)) > 1.0 / pxPerKev) bad++;
                }
            }
            Console.WriteLine();
        }

        static void Report(string what, double x, double xE, double pxPerKev)
        {
            double d = x - xE;
            Console.WriteLine("  " + what + " x = " + F(x) + "; колонка E(k) x = " + F(xE)
                              + "; сдвиг " + F(d) + " px = " + F(d / pxPerKev) + " кэВ");
            if (expect && Math.Abs(d) > 1.0) { Console.WriteLine("    ⛔ НЕ СОШЛОСЬ: ждали |сдвиг| ≤ 1 px"); bad++; }
        }

        static EnergySpectrum MakeSpectrum(PolynomialEnergyCalibration cal, int n, Func<int, double> f)
        {
            EnergySpectrum s = new EnergySpectrum(1, n);
            s.EnergyCalibration = cal;
            double[] d = new double[n];
            for (int i = 0; i < n; i++) { d[i] = f(i); s.Spectrum[i] = (int)Math.Round(d[i]); }
            s.DrawingSpectrum = d;
            s.MeasurementTime = 1.0;
            return s;
        }

        static void Setup(EnergySpectrumView view, EnergySpectrum s, EnergyCalibration cal, HorizontalUnit unit,
                          int n, int left, int width, int height, int hs, double e0, double h)
        {
            Set(view, "energySpectrum", s);
            Set(view, "energyCalibration", cal);
            Set(view, "baseEnergyCalibration", cal);
            Set(view, "numberOfChannels", n);
            Set(view, "horizontalUnit", unit);
            Set(view, "verticalUnit", VerticalUnit.Counts);
            Set(view, "verticalScaleType", VerticalScaleType.LinearScale);
            Set(view, "height", height);
            Set(view, "width", width);
            Set(view, "left", left);
            Set(view, "scrollX", 0);
            Set(view, "scrollY", 0);
            Set(view, "scrollBaseY", 0.0);
            Set(view, "verticalScale", 1.0);
            Set(view, "horizontalScale", (double)hs);
            Set(view, "totalMinValue", 0.0);
            Set(view, "valueRange", 110.0);
            Set(view, "energyViewOffset", e0);
            Set(view, "pixelPerEnergy", 1.0 / h);
            Set(view, "dirty", false);
        }

        static void Set(object o, string name, object value)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new InvalidOperationException("нет поля " + name);
            if (f.FieldType == typeof(double) && value is int) value = (double)(int)value;
            f.SetValue(o, value);
        }

        static object Invoke(object o, string name, params object[] a)
        {
            MethodInfo m = o.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (m == null) throw new InvalidOperationException("нет метода " + name);
            return m.Invoke(o, a);
        }

        static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
    }
}
