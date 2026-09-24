using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace EnergyScaleProbeP151
{
    /// <summary>
    /// `S184` (полоса П151, 24.09.2026) — ОБРАТНАЯ ШКАЛА ПОЛИНОМА МЕЖДУ E(0) И
    /// НУЛЁМ. `PolynomialEnergyCalibration.EnrgToChannel` отдавала канал 0
    /// всякой энергии в [E(0), 0) при E(0) &lt; 0, хотя полином даёт там каналы
    /// 0…k. Что мерится:
    ///
    /// §A — по калибровкам ВСЕХ спектров корпуса (`--dir`): круг «канал i →
    ///      E(i) → канал» для каналов с E(i) &lt; 0 (наибольшая ошибка, каналов);
    ///      ниже E(0) — канал 0 (зажим — индекс массива — обязан остаться);
    ///      `--dump=` — ответ `EnergyToChannel` на сетке энергий для побитовой
    ///      сверки двух сборок (там, где правка ничего менять не должна:
    ///      E ≥ 0 и E &lt; E(0)).
    /// §B — ГРАФИК в режиме энергии (что видит человек): карта «пиксель →
    ///      канал» (`TryMapPixelToChannel`, её же правило у столбиков, карты
    ///      лент и курсора) на шкале спектра с E(0) &lt; 0: сколько колонок в
    ///      [E(0), 0) получили не ближайший канал и сколько каналов 0…k не
    ///      нарисованы вовсе.
    /// §C — сколько расходится ответ калибровки с продолжением разбора FSA
    ///      (`LightToChannel`: (E − E(0)) / (E(1) − E(0)) там, где калибровка
    ///      отдала 0) — то есть что сдвинется у FSA, если калибровка начнёт
    ///      отвечать сама.
    ///
    /// `--expect` — ошибки §A/§B красят код возврата (1).
    /// </summary>
    static class Program
    {
        static int bad;
        static bool expect;
        static MethodInfo channelOf;

        // `S184`: ответ «энергия → канал» тем путём, каким его берут вызывающие вне FSA:
        // `PolynomialEnergyCalibration.ChannelOf` (П151), а на сборке без него — `EnergyToChannel`.
        static double Channel(EnergyCalibration cal, double e, int n)
        {
            return channelOf != null ? (double)channelOf.Invoke(null, new object[] { cal, e, n }) : cal.EnergyToChannel(e, n);
        }

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string dir = @"tools\CORPUS\corpus\spectra";
            string dumpFile = null;
            string chart = "G1S16_Mn54_P5";
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=")) dir = a.Substring(6);
                else if (a.StartsWith("--dump=")) dumpFile = a.Substring(7);
                else if (a.StartsWith("--chart=")) chart = a.Substring(8);
                else if (a == "--expect") expect = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            channelOf = typeof(PolynomialEnergyCalibration).GetMethod("ChannelOf", BindingFlags.Public | BindingFlags.Static);
            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  ChannelOf: " + (channelOf == null ? "НЕТ — меряется EnergyToChannel" : "есть"));
            Console.WriteLine("  " + typeof(PolynomialEnergyCalibration).Assembly.Location);
            Console.WriteLine();

            var dump = new List<string>();
            Console.WriteLine("=== §A. Круг «канал → энергия → канал» ниже нуля энергии ===");
            int negative = 0, wrong = 0, clampLost = 0;
            double worstFsa = 0.0;
            string worstFsaName = "";
            PolynomialEnergyCalibration chartCal = null;
            int chartN = 0;
            foreach (string f in Directory.GetFiles(dir, "*.xml").OrderBy(x => x, StringComparer.Ordinal))
            {
                ResultData rd;
                try
                {
                    using (FileStream fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        rd = ((ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs)).ResultDataList[0];
                }
                catch (Exception ex) { Exception inner = ex; while (inner.InnerException != null) inner = inner.InnerException; Console.WriteLine("  " + Path.GetFileName(f) + ": не прочитан — " + ex.GetType().Name + " / " + inner.GetType().Name + ": " + inner.Message.Replace('\r', ' ').Replace('\n', ' ')); continue; }
                PolynomialEnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
                if (cal == null) continue;
                int n = rd.EnergySpectrum.NumberOfChannels;
                string name = Path.GetFileNameWithoutExtension(f);
                if (name == chart) { chartCal = cal; chartN = n; }

                double e0 = cal.ChannelToEnergy(0);
                // Сетка для побитовой сверки: по всей шкале и за её краями.
                double top = cal.ChannelToEnergy(n);
                var sb = new StringBuilder(name);
                for (int t = -20; t <= 220; t++)
                {
                    double e = e0 - 10.0 + (top - e0 + 20.0) * t / 200.0;
                    sb.Append('\t').Append(cal.EnergyToChannel(e, n).ToString("R", CultureInfo.InvariantCulture))
                      .Append('|').Append(Channel(cal, e, n).ToString("R", CultureInfo.InvariantCulture));
                }
                dump.Add(sb.ToString());

                if (!(e0 < 0.0)) continue;
                negative++;
                int k0 = 0;
                while (k0 < n && cal.ChannelToEnergy(k0) < 0.0) k0++;
                double worst = 0.0; int worstCh = 0;
                for (int i = 0; i < k0; i++)
                {
                    double back = Channel(cal, cal.ChannelToEnergy(i), n);
                    if (Math.Abs(back - i) > Math.Abs(worst)) { worst = back - i; worstCh = i; }
                }
                // Зажим ниже E(0) — обязан остаться нулём (индекс массива).
                double below = Channel(cal, e0 - 1.0, n);
                if (below != 0.0) clampLost++;
                // §C: калибровка против продолжения FSA на [E(0), 0).
                double step = cal.ChannelToEnergy(1) - e0;
                double fsaWorst = 0.0;
                for (int s = 0; s <= 100; s++)
                {
                    double e = e0 + (-e0) * s / 101.0;
                    // Прежний ответ LightToChannel здесь — прямая по ширине нулевого
                    // канала (калибровка отдавала 0); ответила калибровка сама
                    // (c > 0) — FSA берёт её число, и разница есть сдвиг у FSA.
                    double c = Channel(cal, e, n);
                    double d = c > 0.0 ? Math.Abs(c - (e - e0) / step) : 0.0;
                    fsaWorst = Math.Max(fsaWorst, d);
                }
                if (fsaWorst > worstFsa) { worstFsa = fsaWorst; worstFsaName = name; }
                bool ok = Math.Abs(worst) <= 1e-6;
                if (!ok) wrong++;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-28} степень {1}, E(0) = {2,8:F3} кэВ, каналов ниже нуля {3,3}: круг — наибольшая ошибка {4,9:F4} кан. (канал {5}); ниже E(0) → {6}; калибровка − прямая FSA {7:E2} кан.{8}",
                    name, cal.PolynomialOrder, e0, k0, worst, worstCh, below.ToString("R", CultureInfo.InvariantCulture), fsaWorst, ok ? "" : "  ⛔"));
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  ИТОГ §A: спектров с E(0) < 0 — {0}, с ошибкой круга — {1}, потерян зажим ниже E(0) — {2}; §C наибольшее |калибровка − прямая| {3:E2} кан. ({4})",
                negative, wrong, clampLost, worstFsa, worstFsaName));
            if (expect && (wrong > 0 || clampLost > 0)) bad++;
            Console.WriteLine();

            if (chartCal != null) Chart(chart, chartCal, chartN);

            if (dumpFile != null) File.WriteAllLines(dumpFile, dump.ToArray(), new UTF8Encoding(false));
            if (bad > 0) { Console.Error.WriteLine("ОТКАЗ: не сошлось ожиданий — " + bad); return 1; }
            Console.WriteLine("ЗАМЕР СНЯТ.");
            return 0;
        }

        // §B: график в режиме энергии, шкала спектра `name`, 1 px на 0.1 кэВ.
        static void Chart(string name, PolynomialEnergyCalibration cal, int n)
        {
            Console.WriteLine("=== §B. График в режиме энергии: " + name + " ===");
            double e0 = cal.ChannelToEnergy(0);
            double c1 = cal.Coefficients[1];
            int k0 = 0;
            while (k0 < n && cal.ChannelToEnergy(k0) < 0.0) k0++;
            EnergySpectrum s = new EnergySpectrum(1, n);
            s.EnergyCalibration = cal;
            double[] d = new double[n];
            for (int i = 0; i < n; i++) { d[i] = i + 1; s.Spectrum[i] = i + 1; }
            s.DrawingSpectrum = d;
            s.MeasurementTime = 1.0;
            const double kevPerPixel = 0.1;
            int left = 1;
            using (EnergySpectrumView view = new EnergySpectrumView())
            {
                Set(view, "energySpectrum", s);
                Set(view, "energyCalibration", cal);
                Set(view, "baseEnergyCalibration", cal);
                Set(view, "numberOfChannels", n);
                Set(view, "horizontalUnit", HorizontalUnit.Energy);
                Set(view, "left", left);
                Set(view, "scrollX", 0);
                // пиксель → E = (x − left)/hs/ppe + offset; ppe = 1/c1 (как RecalcChartParameters)
                double ppe = 1.0 / c1;
                double hs = 1.0 / (kevPerPixel * ppe);
                Set(view, "horizontalScale", hs);
                Set(view, "energyViewOffset", cal.Coefficients[0]);
                Set(view, "pixelPerEnergy", ppe);
                MethodInfo map = typeof(EnergySpectrumView).GetMethod("TryMapPixelToChannel", BindingFlags.Instance | BindingFlags.NonPublic);
                int columns = 0, mismatch = 0;
                var seen = new HashSet<int>();
                int lastPixel = left + (int)Math.Ceiling((cal.ChannelToEnergy(k0 + 2) - cal.Coefficients[0]) / kevPerPixel);
                for (int x = left; x <= lastPixel; x++)
                {
                    double e = (x - left) / hs / ppe + cal.Coefficients[0];
                    object[] pa = new object[] { x, s, cal, false, 0 };
                    map.Invoke(view, pa);
                    int ch = (int)pa[4];
                    int truth = Nearest(cal, e, n);
                    if (e < 0.0)
                    {
                        columns++;
                        if (ch != truth) mismatch++;
                    }
                    if (ch <= k0) seen.Add(ch);
                }
                int missing = 0;
                for (int i = 0; i <= k0; i++) if (!seen.Contains(i)) missing++;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  E(0) = {0:F3} кэВ, каналов ниже нуля {1}; колонок графика в [E(0), 0) — {2}, из них с НЕ ближайшим каналом — {3}; каналов 0…{1} не нарисовано вовсе — {4}",
                    e0, k0, columns, mismatch, missing));
                if (expect && (mismatch > 0 || missing > 0)) { bad++; Console.WriteLine("  ⛔ НЕ СОШЛОСЬ"); }
            }
            Console.WriteLine();
        }

        // Ближайший центр канала к энергии e — прямым перебором по ChannelToEnergy (независимо от обратной шкалы).
        static int Nearest(PolynomialEnergyCalibration cal, double e, int n)
        {
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < Math.Min(n, 4096); i++)
            {
                double dd = Math.Abs(cal.ChannelToEnergy(i) - e);
                if (dd < bd) { bd = dd; best = i; }
                if (cal.ChannelToEnergy(i) > e + 50.0) break;
            }
            return best;
        }

        static void Set(object o, string name, object value)
        {
            FieldInfo f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new InvalidOperationException("нет поля " + name);
            if (f.FieldType == typeof(double) && value is int) value = (double)(int)value;
            f.SetValue(o, value);
        }
    }
}
