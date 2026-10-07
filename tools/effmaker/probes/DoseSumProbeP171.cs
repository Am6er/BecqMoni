using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace DoseSumProbeP171
{
    /// <summary>
    /// Полоса П171 (28.09.2026): мощность дозы, вторая часть ревизии «дубль 4» —
    /// `AMBER115`, `AMBER131`, `AMBER116`, `AMBER103`. Каждая строка сперва
    /// воспроизводится числом; проба гоняется на прежней сборке (положительный
    /// контроль) и на исправленной.
    ///
    ///   §1 `AMBER115` — h*(10)/K_a между узлами 10 и 15 кэВ ICRP 74 против модели
    ///      `ln h = a − b·μ_воды(E)` (μ воды — сечения XCOM из `matdb.sqlite`:
    ///      H 0.111894, O 0.888106 по массе). Приёмка: на 10.5…14.5 кэВ
    ///      |h/модель − 1| ≤ 10 %; все 25 узлов — побитово табличные; выше 15 кэВ —
    ///      побитово прежняя схема (значение линейно, энергия логарифмически).
    ///   §2 `AMBER131` — μ_en/ρ сухого воздуха против NIST (Hubbell &amp; Seltzer,
    ///      NISTIR 5632, таблица 4, «Air, Dry (near sea level)») и K_a/Φ ICRP 119
    ///      (таблица I.1) на 1…10 МэВ. Приёмка: |Δ| ≤ 0.5 % на всех узлах NIST
    ///      от 1 до 10 МэВ.
    ///   §3 `AMBER116` — истинное суммирование каскадов в дозе: синтетический
    ///      спектр Co-60 и Na-22 из строк матрицы сцены с суммированием и без
    ///      него (события распада независимы, угловая корреляция не учтена),
    ///      показание приложения на обоих — отношение печатается.
    ///   §4 `AMBER216` (07.10.2026; прежде здесь мерилась подпись `AMBER103`,
    ///      снятая решением Amber «Только значение мощности дозы с погрешностью,
    ///      без всяких текстов.») — строка дозы есть ровно «[≈ ]число ±число
    ///      (n%) единица» (en и ru), в том числе при неполном покрытии и у
    ///      сцены с источником, а подписи `QuantityLabel` в сборке больше нет.
    ///
    ///     dosesumprobep171 [--dir=&lt;корпус&gt;] [--quiet]
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;
        static string corpusDir = @"tools\CORPUS\corpus";
        static bool quiet;

        // ICRP 74, таблица A.21 — копия для побитовой сверки узлов.
        static readonly double[] NodeKev =
        {
            10, 15, 20, 30, 40, 50, 60, 80, 100, 150, 200, 300, 400, 500, 600,
            800, 1000, 1500, 2000, 3000, 4000, 5000, 6000, 8000, 10000,
        };

        static readonly double[] NodeH =
        {
            0.008, 0.26, 0.61, 1.10, 1.47, 1.67, 1.74, 1.72, 1.65, 1.49, 1.40,
            1.31, 1.26, 1.23, 1.21, 1.19, 1.17, 1.15, 1.14, 1.13, 1.12, 1.11,
            1.11, 1.11, 1.10,
        };

        // NIST (Hubbell & Seltzer), воздух сухой, μ_en/ρ, см²/г.
        static readonly double[] NistKev = { 1000, 1250, 1500, 2000, 3000, 4000, 5000, 6000, 8000, 10000 };
        static readonly double[] NistMuEn = { 2.789e-2, 2.666e-2, 2.547e-2, 2.345e-2, 2.057e-2, 1.870e-2,
                                              1.740e-2, 1.647e-2, 1.525e-2, 1.450e-2 };

        // ICRP 119, таблица I.1, K_a/Φ, пГр·см² (те же числа, что в DoseAirProbeF63).
        static readonly double[] IcrpKev = { 1000, 2000, 4000, 6000, 8000, 10000 };
        static readonly double[] IcrpKaPhi = { 4.47, 7.51, 12.0, 15.8, 19.5, 23.2 };
        const double KaPhiUnit = 0.1602176634;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) corpusDir = a.Substring(6);
                else if (a == "--quiet") quiet = true;
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            GlobalConfigManager.GetInstance();
            try
            {
                Ambient115();
                Air131();
                if (!Summing116()) return 2;
                if (!Label103()) return 2;
            }
            catch (Exception ex)
            {
                Console.WriteLine("!! проба сорвалась: " + ex);
                return 3;
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "СОШЛОСЬ (" + checks.ToString(CultureInfo.InvariantCulture) + ")"
                : "РАЗОШЛОСЬ: " + failed.ToString(CultureInfo.InvariantCulture)
                  + " из " + checks.ToString(CultureInfo.InvariantCulture));
            return failed == 0 ? 0 : 1;
        }

        // ==================================================================
        // §1. AMBER115
        // ==================================================================

        static double MuWater(double e)
        {
            MaterialDatabase.Element h, o;
            if (!MaterialDatabase.TryGet(1, out h) || !MaterialDatabase.TryGet(8, out o))
            {
                throw new InvalidOperationException("нет H или O в matdb");
            }

            return 0.111894 * MaterialDatabase.Interpolate(h.EnergyKev, h.Total, e)
                   + 0.888106 * MaterialDatabase.Interpolate(o.EnergyKev, o.Total, e);
        }

        /// <summary>Прежняя схема: значение линейно, энергия логарифмически.</summary>
        static double OldScheme(double e)
        {
            int lo = 0;
            while (lo < NodeKev.Length - 2 && NodeKev[lo + 1] <= e) lo++;
            int hi = lo + 1;
            double t = (Math.Log(e) - Math.Log(NodeKev[lo])) / (Math.Log(NodeKev[hi]) - Math.Log(NodeKev[lo]));
            return NodeH[lo] + t * (NodeH[hi] - NodeH[lo]);
        }

        static void Ambient115()
        {
            Head("§1. AMBER115: h*(10)/K_a на 10…15 кэВ против модели ln h = a − b·μ_воды");
            double mu10 = MuWater(10.0), mu15 = MuWater(15.0), mu20 = MuWater(20.0), mu30 = MuWater(30.0);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  μ воды (XCOM из matdb): 10 кэВ {0:F4}, 15 {1:F4}, 20 {2:F4}, 30 {3:F4} см²/г"
                + " (NIST 5.329 / 1.673 / 0.8096 / 0.3756)", mu10, mu15, mu20, mu30));
            double b1015 = Math.Log(NodeH[1] / NodeH[0]) / (mu10 - mu15);
            double b1520 = Math.Log(NodeH[2] / NodeH[1]) / (mu15 - mu20);
            double b2030 = Math.Log(NodeH[3] / NodeH[2]) / (mu20 - mu30);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  b (г/см²) по соседним узлам: 10–15 {0:F3}, 15–20 {1:F3}, 20–30 {2:F3}"
                + " (≈ 1 г/см² — 10 мм ткани шара ICRU)", b1015, b1520, b2030));

            Func<double, int, double> model = (e, seg) =>
            {
                double muLo = MuWater(NodeKev[seg]), muHi = MuWater(NodeKev[seg + 1]);
                double t = (muLo - MuWater(e)) / (muLo - muHi);
                return Math.Exp(Math.Log(NodeH[seg]) + t * Math.Log(NodeH[seg + 1] / NodeH[seg]));
            };

            foreach (double e in new[] { 10.25, 10.5, 11.0, 11.5, 12.0, 12.5, 13.0, 13.5, 14.0, 14.5, 14.9 })
            {
                double app = DoseRateCoefficients.AmbientDoseConversion(e);
                double m = model(e, 0);
                double old = OldScheme(e);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:F2} кэВ: приложение {1:F5}, модель {2:F5}, прежняя схема {3:F5} (×{4:F3} к модели)"
                    + " — приложение/модель ×{5:F3}", e, app, m, old, old / m, app / m));
                if (e >= 10.5 && e <= 14.5)
                {
                    Ok(Math.Abs(app / m - 1.0) <= 0.10, string.Format(CultureInfo.InvariantCulture,
                        "{0:F2} кэВ: h*(10)/K_a в пределах 10 % модели (×{1:F3})", e, app / m));
                }
            }

            // Соседние участки: какая схема ближе к модели — для обоснования,
            // почему правка не выходит за 15 кэВ.
            for (int seg = 1; seg <= 2; seg++)
            {
                double worstLin = 0.0, worstLog = 0.0;
                for (int k = 1; k < 10; k++)
                {
                    double f = k / 10.0;
                    double e = NodeKev[seg] * Math.Pow(NodeKev[seg + 1] / NodeKev[seg], f);
                    double m = model(e, seg);
                    double lin = OldScheme(e);
                    double lg = Math.Exp(Math.Log(NodeH[seg]) + f * Math.Log(NodeH[seg + 1] / NodeH[seg]));
                    worstLin = Math.Max(worstLin, Math.Abs(lin / m - 1.0));
                    worstLog = Math.Max(worstLog, Math.Abs(lg / m - 1.0));
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  участок {0}–{1} кэВ: худшее |схема/модель − 1| — значение линейно {2:F2} %, ln h по ln E {3:F2} %",
                    NodeKev[seg], NodeKev[seg + 1], 100.0 * worstLin, 100.0 * worstLog));
            }

            bool nodes = true;
            for (int i = 0; i < NodeKev.Length; i++)
            {
                nodes &= DoseRateCoefficients.AmbientDoseConversion(NodeKev[i]) == NodeH[i];
            }

            Ok(nodes, "все 25 узлов ICRP 74 — побитово табличные");

            bool same = true;
            int count = 0;
            for (double e = 15.0; e <= 9990.0; e *= 1.0137)
            {
                if (e > 15.0)
                {
                    same &= DoseRateCoefficients.AmbientDoseConversion(e) == OldScheme(e);
                    count++;
                }
            }

            Ok(same, string.Format(CultureInfo.InvariantCulture,
                "выше 15 кэВ ({0} энергий до 10 МэВ) — побитово прежняя схема", count));
        }

        // ==================================================================
        // §2. AMBER131
        // ==================================================================

        static void Air131()
        {
            Head("§2. AMBER131: μ_en/ρ воздуха против NIST и ICRP 119 на 1…10 МэВ");
            for (int i = 0; i < NistKev.Length; i++)
            {
                double app = DoseRateCoefficients.MassEnergyAbsorptionAir(NistKev[i]) * 10.0;
                double d = 100.0 * (app / NistMuEn[i] - 1.0);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:F0} кэВ: приложение {1:E4} см²/г, NIST {2:E4}, Δ {3:+0.00;-0.00} %", NistKev[i], app,
                    NistMuEn[i], d));
                Ok(Math.Abs(d) <= 0.5, string.Format(CultureInfo.InvariantCulture,
                    "{0:F0} кэВ: μ_en/ρ воздуха в пределах 0.5 % NIST ({1:+0.00;-0.00} %)", NistKev[i], d));
            }

            for (int i = 0; i < IcrpKev.Length; i++)
            {
                double app = DoseRateCoefficients.MassEnergyAbsorptionAir(IcrpKev[i]) * 10.0;
                double icrp = IcrpKaPhi[i] / (IcrpKev[i] * KaPhiUnit);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:F0} кэВ: против K_a/Φ ICRP 119 Δ {1:+0.00;-0.00} %", IcrpKev[i], 100.0 * (app / icrp - 1.0)));
            }

            // Доля g и μ_tr — у исправленной сборки (отражением: у прежней их нет).
            MethodInfo gOf = typeof(DoseRateCoefficients).GetMethod("RadiativeFractionAir",
                                                                   BindingFlags.Public | BindingFlags.Static);
            var energies = new[] { 30.0, 59.5409, 100.0, 300.0, 661.657, 1250.0, 2614.511, 3000.0, 6000.0, 10000.0 };
            foreach (double e in energies)
            {
                double mu = DoseRateCoefficients.MassEnergyAbsorptionAir(e) * 10.0;
                string g = gOf == null
                    ? "g — нет в сборке"
                    : string.Format(CultureInfo.InvariantCulture, "g = {0:E4}", (double)gOf.Invoke(null, new object[] { e }));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,9:F3} кэВ: μ_en/ρ {1:R} см²/г; {2}", e, mu, g));
            }
        }

        // ==================================================================
        // §3. AMBER116
        // ==================================================================

        sealed class Scene
        {
            public string Spectrum;
            public string Matrix;
            public double Fwhm662Percent;
        }

        static readonly Scene[] Scenes =
        {
            new Scene { Spectrum = "RC103_Cs137_0cm", Matrix = "RC103_point0", Fwhm662Percent = 8.26 },
            new Scene { Spectrum = "AS80_Cs137_0cm", Matrix = "AS80_point0", Fwhm662Percent = 7.22 },
            new Scene { Spectrum = "G1S16_Cs137_P5", Matrix = "G1S_point5_p16", Fwhm662Percent = 6.61 },
            new Scene { Spectrum = "ASN16_Cs137_10cm", Matrix = "ASN16_point10_house", Fwhm662Percent = 6.26 },
        };

        static readonly Dictionary<string, double> summingByScene = new Dictionary<string, double>();

        static bool Summing116()
        {
            Head("§3. AMBER116: истинное суммирование каскадов Co-60 и Na-22 в показании дозы");
            var manager = new DoseRateManager(Config());
            foreach (Scene scene in Scenes)
            {
                ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", scene.Spectrum + ".xml"));
                if (data == null || data.Efficiency == null || !data.Efficiency.HasGeometry)
                {
                    Console.WriteLine("ОСНАСТКА: нет спектра или кривой с геометрией: " + scene.Spectrum);
                    return false;
                }

                ResponseMatrix matrix = LoadMatrix(Path.Combine(corpusDir, "geometries", scene.Matrix + ".rmx"),
                                                   data.Efficiency.Geometry);
                if (matrix == null)
                {
                    Console.WriteLine("ОСНАСТКА: нет матрицы " + scene.Matrix);
                    return false;
                }

                EnergySpectrum spectrum = data.EnergySpectrum;
                EnergyCalibration cal = spectrum.EnergyCalibration;
                int n = spectrum.NumberOfChannels;
                double seconds = spectrum.EffectiveLiveTime;
                DoseRateInput input = DoseRateInput.Of(data.Efficiency, matrix);
                var edges = new double[n + 1];
                for (int i = 1; i < n; i++) edges[i] = cal.ChannelToEnergy(i - 0.5);
                edges[0] = 2.0 * cal.ChannelToEnergy(0.0) - edges[1];
                edges[n] = 2.0 * cal.ChannelToEnergy(n - 1) - edges[n - 1];
                double scaleHigh = cal.ChannelToEnergy(n);
                double step = matrix.BinKev;
                int cells = (int)Math.Ceiling(Math.Max(scaleHigh, 2700.0) / step) + 4;
                double fwhm662 = scene.Fwhm662Percent / 100.0 * 661.657;
                Func<double, double> sigma = data.FwhmCalibration != null
                    ? OwnSigma(data)
                    : x => fwhm662 * Math.Sqrt(Math.Max(x, 1.0) / 661.657) / 2.3548200450309493;

                double[] r511 = matrix.Evaluate(510.999, cells);
                double[] r1173 = matrix.Evaluate(1173.228, cells);
                double[] r1275 = matrix.Evaluate(1274.537, cells);
                double[] r1332 = matrix.Evaluate(1332.492, cells);
                double e511 = r511.Sum(), e1173 = r1173.Sum(), e1275 = r1275.Sum(), e1332 = r1332.Sum();
                Console.WriteLine();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0} (матрица {1}): шкала до {2:F0} кэВ, G = {3:E4} 1/см²; полная ε: 511 {4:F4}, 1173 {5:F4},"
                    + " 1275 {6:F4}, 1332 {7:F4}", scene.Spectrum, scene.Matrix, scaleHigh, input.FluencePerPhoton,
                    e511, e1173, e1275, e1332));

                int[] original = (int[])spectrum.Spectrum.Clone();
                try
                {
                    const double decays = 2.0e8;
                    double decaysPerSecond = decays / seconds;

                    // Co-60: 1173 и 1332 в каскаде, оба на распад.
                    double[] co0 = Add(r1173, r1332);
                    double[] co1 = Add(Add(Scale(r1173, 1.0 - e1332), Scale(r1332, 1.0 - e1173)), Convolve(r1173, r1332));
                    double coTruth = decaysPerSecond * input.FluencePerPhoton
                                     * (DoseRateCoefficients.DoseRatePerFluenceRate(1173.228)
                                        + DoseRateCoefficients.DoseRatePerFluenceRate(1332.492));
                    Pair(manager, data, input, "Co-60", co0, co1, coTruth, step, edges, sigma, decays, scene);

                    // Na-22: β+ 0.903 на распад (пара 511 встречная — одновременно в
                    // кристалл не попадают, её отклик 2·r511), 1275 на каждый распад.
                    const double betaPlus = 0.903;
                    double[] pair = Scale(r511, 2.0);
                    double[] na0 = Add(r1275, Scale(pair, betaPlus));
                    double[] naBeta = Add(Add(Scale(r1275, 1.0 - 2.0 * e511), Scale(pair, 1.0 - e1275)), Convolve(r1275, pair));
                    double[] na1 = Add(Scale(naBeta, betaPlus), Scale(r1275, 1.0 - betaPlus));
                    double naTruth = decaysPerSecond * input.FluencePerPhoton
                                     * (DoseRateCoefficients.DoseRatePerFluenceRate(1274.537)
                                        + 2.0 * betaPlus * DoseRateCoefficients.DoseRatePerFluenceRate(510.999));
                    Pair(manager, data, input, "Na-22", na0, na1, naTruth, step, edges, sigma, decays, scene);
                }
                finally
                {
                    Array.Copy(original, spectrum.Spectrum, original.Length);
                }
            }

            return true;
        }

        static void Pair(DoseRateManager manager, ResultData data, DoseRateInput input, string nuclide,
                         double[] without, double[] with, double truth, double step, double[] edges,
                         Func<double, double> sigma, double decays, Scene scene)
        {
            DoseRate d0 = Reading(manager, data, input, Place(without, step, edges, sigma, decays));
            DoseRate d1 = Reading(manager, data, input, Place(with, step, edges, sigma, decays));
            if (!string.IsNullOrEmpty(d0.Refusal) || !string.IsNullOrEmpty(d1.Refusal))
            {
                Console.WriteLine("    " + nuclide + ": ОТКАЗ «" + d0.Refusal + d1.Refusal + "»");
                return;
            }

            double ratio = d1.Rate / d0.Rate;
            summingByScene[scene.Matrix + ":" + nuclide] = ratio;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "    {0}: без суммирования {1:F5} мкЗв/ч (к ответу руками {2:+0.00;-0.00} %), с суммированием {3:F5}"
                + " — суммирование даёт {4:+0.00;-0.00} %", nuclide, d0.Rate, 100.0 * (d0.Rate / truth - 1.0),
                d1.Rate, 100.0 * (ratio - 1.0)));
            PrintNote(d1);
        }

        static void PrintNote(DoseRate d)
        {
            Console.WriteLine("      строка: " + d);
        }

        static double[] Add(double[] a, double[] b)
        {
            var r = new double[Math.Max(a.Length, b.Length)];
            for (int i = 0; i < r.Length; i++) r[i] = (i < a.Length ? a[i] : 0.0) + (i < b.Length ? b[i] : 0.0);
            return r;
        }

        static double[] Scale(double[] a, double f)
        {
            return a.Select(v => v * f).ToArray();
        }

        /// <summary>
        /// Свёртка двух строк: бин b — отрезок [(b − ½)·шаг, (b + ½)·шаг), сумма
        /// центров b₁ + b₂ — бин b₁ + b₂.
        /// </summary>
        static double[] Convolve(double[] a, double[] b)
        {
            var r = new double[a.Length + b.Length];
            for (int i = 0; i < a.Length; i++)
            {
                if (!(a[i] > 0.0)) continue;
                for (int j = 0; j < b.Length; j++)
                {
                    if (b[j] > 0.0) r[i + j] += a[i] * b[j];
                }
            }

            return r;
        }

        static DoseRate Reading(DoseRateManager manager, ResultData data, DoseRateInput input, double[] counts)
        {
            int[] target = data.EnergySpectrum.Spectrum;
            for (int i = 0; i < target.Length; i++)
            {
                double v = Math.Round(counts[i]);
                target[i] = v > int.MaxValue ? int.MaxValue : (int)v;
            }

            return manager.Calculate(data, input);
        }

        // ==================================================================
        // §4. AMBER103
        // ==================================================================

        static bool Label103()
        {
            Head("§4. AMBER216: строка дозы — только значение с погрешностью");
            ResultData data = LoadSpectrum(Path.Combine(corpusDir, "spectra", "RC103_Cs137_0cm.xml"));
            ResponseMatrix matrix = data == null ? null
                : LoadMatrix(Path.Combine(corpusDir, "geometries", "RC103_point0.rmx"), data.Efficiency.Geometry);
            if (data == null || matrix == null)
            {
                Console.WriteLine("ОСНАСТКА: нет RC103_Cs137_0cm или матрицы RC103_point0");
                return false;
            }

            var manager = new DoseRateManager(Config());
            DoseRate withMatrix = manager.Calculate(data, DoseRateInput.Of(data.Efficiency, matrix));
            DoseRate peak = manager.Calculate(data, DoseRateInput.Of(data.Efficiency, null));
            MethodInfo label = typeof(DoseRate).GetMethod("QuantityLabel", BindingFlags.Public | BindingFlags.Instance);
            Ok(label == null, "подписи `QuantityLabel` в сборке нет");

            // Положительный контроль приписок: та же доза с неполным покрытием и
            // признаком сцены с источником — прежняя сборка дописывала к строке
            // «(covers 50 % of counts)» и «(sample geometry; …)».
            DoseRate marked = manager.Calculate(data, DoseRateInput.Of(data.Efficiency, matrix));
            marked.Coverage = 0.5;
            marked.SourceScene = true;

            var line = new System.Text.RegularExpressions.Regex(
                @"^(≈ )?[0-9]+\.[0-9]+ ±[0-9]+\.[0-9]+ \([0-9]+\.[0-9]%\) [^\s()]+$");
            CultureInfo keep = BecquerelMonitor.Properties.Resources.Culture;
            try
            {
                foreach (string culture in new[] { "en", "ru" })
                {
                    BecquerelMonitor.Properties.Resources.Culture = new CultureInfo(culture);
                    foreach (DoseRate d in new[] { withMatrix, peak, marked })
                    {
                        string path = d == withMatrix ? "матрица" : d == peak ? "пиковая" : "покрытие 50 %, сцена с источником";
                        string text = d.ToString();
                        Console.WriteLine("  " + culture + ", " + path + ": «" + text + "»");
                        Ok(line.IsMatch(text), culture + ", " + path + ": только значение с погрешностью");
                        Ok((d == peak) == text.StartsWith(DoseRate.ApproximateMark, StringComparison.Ordinal),
                           culture + ", " + path + ": знак «≈» ровно у пиковой");
                    }
                }
            }
            finally
            {
                BecquerelMonitor.Properties.Resources.Culture = keep;
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Оснастка — по образцу DoseGridProbe.
        // ------------------------------------------------------------------

        static double[] Place(double[] row, double step, double[] edges, Func<double, double> sigma, double scale)
        {
            var result = new double[edges.Length - 1];
            const int sub = 8;
            for (int b = 0; b < row.Length; b++)
            {
                double v = row[b];
                if (!(v > 0.0)) continue;
                for (int s = 0; s < sub; s++)
                {
                    double x = (b - 0.5 + (s + 0.5) / sub) * step;
                    Deposit(result, edges, x, sigma, scale * v / sub);
                }
            }

            return result;
        }

        static void Deposit(double[] result, double[] edges, double x, Func<double, double> sigma, double mass)
        {
            int n = result.Length;
            if (sigma == null)
            {
                int k = Array.BinarySearch(edges, x);
                if (k < 0) k = ~k - 1;
                if (k >= 0 && k < n) result[k] += mass;
                return;
            }

            double s = sigma(x);
            int lo = Array.BinarySearch(edges, x - 7.0 * s);
            if (lo < 0) lo = ~lo - 1;
            if (lo < 0) lo = 0;
            if (lo >= n) return;
            double prev = Phi((edges[lo] - x) / s);
            for (int k = lo; k < n; k++)
            {
                double next = Phi((edges[k + 1] - x) / s);
                result[k] += mass * (next - prev);
                prev = next;
                if (edges[k + 1] > x + 7.0 * s) break;
            }
        }

        static double Phi(double z)
        {
            return 0.5 * Erfc(-z / Math.Sqrt(2.0));
        }

        static Func<double, double> OwnSigma(ResultData data)
        {
            EnergyCalibration cal = data.EnergySpectrum.EnergyCalibration;
            FwhmCalibration fwhm = data.FwhmCalibration;
            int n = data.EnergySpectrum.NumberOfChannels;
            return x =>
            {
                double ch = cal.EnergyToChannel(x, n);
                double width = cal.ChannelToEnergy(ch + 0.5) - cal.ChannelToEnergy(ch - 0.5);
                double s = fwhm.ChannelToFwhm(ch) * width / 2.3548200450309493;
                return s > 1e-6 && !double.IsNaN(s) ? s : 1e-6;
            };
        }

        static double Erfc(double x)
        {
            double z = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.5 * z);
            double ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418
                         + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587
                         + t * (-0.82215223 + t * 0.17087277)))))))));
            return x >= 0.0 ? ans : 2.0 - ans;
        }

        static void Head(string text)
        {
            Console.WriteLine();
            Console.WriteLine(text);
            Console.WriteLine(new string('-', Math.Min(100, text.Length)));
        }

        static void Ok(bool ok, string what)
        {
            checks++;
            if (!ok) failed++;
            if (!quiet || !ok) Console.WriteLine("  [" + (ok ? "ок" : "НЕТ") + "]   " + what);
        }

        static ResultData LoadSpectrum(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("  нет " + path);
                return null;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                return file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
            }
        }

        static ResponseMatrix LoadMatrix(string path, GeometryModel geometry)
        {
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrix.Load(path, out refusal, out fileFormat);
            if (matrix == null)
            {
                Console.WriteLine("  матрица {0}: {1} (формат {2})", path, refusal, fileFormat);
                return null;
            }

            if (geometry != null && !matrix.IsValidFor(geometry))
            {
                matrix.Stamp = ResponseMatrix.ComputeStamp(geometry, matrix.Options);
            }

            return matrix;
        }

        static GlobalConfigManager Config()
        {
            var manager = new GlobalConfigManager();
            var info = new GlobalConfigInfo();
            if (info.ColorConfig != null
                && (info.ColorConfig.SpectrumColorList == null || info.ColorConfig.SpectrumColorList.Count == 0))
            {
                info.ColorConfig.InitializeSpectrumColor();
            }

            manager.GlobalConfig = info;
            return manager;
        }
    }
}
