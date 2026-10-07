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

namespace DoseShieldProbe
{
    /// <summary>
    /// Полоса П165 (28.09.2026): три строки мощности дозы — `AMBER114`,
    /// `AMBER102`, `AMBER117`. Проба мерит посылку каждой на нынешнем коде и
    /// принимает лечение.
    ///
    ///   §1 `AMBER114` — СВИНЦОВЫЙ ДОМИК. Показание по спектру источника
    ///      Cs-137 с паспортом против свободного поля `A·G·Σ y·h*(E)`
    ///      (G — множитель приложения); показание фона файла (тот же прибор,
    ///      то же место) вычитается. Спектры в домике (`InShield`) против
    ///      спектров без домика; эффективная площадь торца `ε/G` на энергиях
    ///      рассеяния от стен против средней проекции кристалла S/4.
    ///      Замер П165: посылка («×2.6 к свободному полю, +75…+95 %») НЕ
    ///      подтвердилась — 0.0426 из 0.0778 мкЗв/ч показания `ASN16_Cs137_10cm`
    ///      даёт фон самого домика (фон файла), источник за вычетом фона —
    ///      ×1.17 к свободному полю, в разбросе приборов без домика (×1.04…×1.28).
    ///      Лечения нет; проба держит, что признак домика числа не меняет.
    ///   §2 `AMBER102` — ФОН ПО КРИВОЙ ТОЧКИ. Эффективная площадь точечной
    ///      сцены `ε_пик/G` против сцены поля `ISO` той же геометрии
    ///      (`tools/effmaker/out/p6_iso`), и показание фонового спектра по
    ///      обеим кривым. Приёмка лечения: у кривой не-ISO к числу приписка,
    ///      у ISO — нет.
    ///   §3 `AMBER117` — ОТКАЗ ПО ЯРЛЫКУ. Ослабление нерассеянного потока в
    ///      самой пробе `a(E) = ∫dV e^{−μl}/(4πr²) / ∫dV/(4πr²)` (l — путь в
    ///      пробе до центра кристалла; по направлениям из центра:
    ///      `∫dΩ (1 − e^{−μL})/μ`, L — хорда пробы) — своим счётом; тело грунта
    ///      сосудом (сцена «нет») и все объёмные пробы корпуса.
    ///      Приёмка лечения: грунт сосудом — отказ словами, объёмные пробы
    ///      корпуса ниже порога — число побитово прежнее.
    ///
    ///     doseshieldprobe [--dir=&lt;корпус&gt;] [--iso=&lt;каталог p6_iso&gt;]
    ///                     [--soil-spectrum=&lt;файл&gt;]   спектр с грунтом сосудом — для экрана
    ///
    /// Положительный контроль — прежняя сборка: приёмки лечения §2 и §3 краснеют.
    /// </summary>
    static class Program
    {
        static int failed;
        static int checks;
        static string corpusDir = @"tools\CORPUS\corpus";
        static string isoDir = @"tools\effmaker\out\p6_iso";
        static string soilSpectrum;

        /// <summary>
        /// Линии Cs-137 (ENSDF/DDEP, выходы на распад): 661.657 кэВ и K-рентген
        /// Ba (Kα2, Kα1, Kβ — сгруппированно). Рентген даёт около процента
        /// свободного поля.
        /// </summary>
        static readonly double[][] Cs137Lines =
        {
            new[] { 661.657, 0.8510 },
            new[] { 31.817, 0.0199 },
            new[] { 32.194, 0.0364 },
            new[] { 36.4, 0.0133 },
        };

        sealed class Source
        {
            public string Spectrum;
            public double Becquerel;
        }

        /// <summary>
        /// Один чек-источник Cs-137 (паспорт 9.25 кБк на 02.01.2002), активности
        /// на даты съёмок — `manifest.csv` корпуса.
        /// </summary>
        static readonly Source[] Sources =
        {
            new Source { Spectrum = "ASN16_Cs137_10cm", Becquerel = 5712.0 },
            new Source { Spectrum = "ASN16_Cs137", Becquerel = 5715.5 },
            new Source { Spectrum = "RC103_Cs137_50mm", Becquerel = 5235.6 },
            new Source { Spectrum = "RC103_Cs137_0cm", Becquerel = 5564.0 },
            new Source { Spectrum = "AS80_Cs137_0cm", Becquerel = 5369.0 },
        };

        static Dictionary<string, string> geometryOf;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) corpusDir = a.Substring(6);
                else if (a.StartsWith("--iso=", StringComparison.Ordinal)) isoDir = a.Substring(6);
                else if (a.StartsWith("--soil-spectrum=", StringComparison.Ordinal)) soilSpectrum = a.Substring(16);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            GlobalConfigManager.GetInstance();
            geometryOf = ReadParts(Path.Combine(corpusDir, "parts.csv"));
            if (geometryOf == null)
            {
                Console.WriteLine("ОСНАСТКА: нет parts.csv в " + corpusDir);
                return 2;
            }

            if (!Shield() || !IsoBackground() || !Thickness())
            {
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0
                ? "СОШЛОСЬ (" + checks.ToString(CultureInfo.InvariantCulture) + ")"
                : "РАЗОШЛОСЬ: " + failed.ToString(CultureInfo.InvariantCulture)
                  + " из " + checks.ToString(CultureInfo.InvariantCulture));
            return failed == 0 ? 0 : 1;
        }

        // ==================================================================
        // §1. AMBER114 — домик
        // ==================================================================

        static bool Shield()
        {
            Head("§1. AMBER114: показание по источнику с паспортом против свободного поля");
            var manager = new DoseRateManager(Config());
            var excess = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (Source src in Sources)
            {
                ResultData data = LoadSpectrum(src.Spectrum);
                if (data == null) return false;
                GeometryModel geometry = data.Efficiency.Geometry;
                bool shield = geometry.InShield;

                // Множитель — от геометрии БЕЗ признака домика: он геометрии не
                // меняет, а лечение по признаку отказывает.
                GeometryModel free = geometry.Clone();
                free.InShield = false;
                string note;
                double g = DoseRateGeometry.FluencePerPhoton(free, out note);
                double expected = 0.0;
                foreach (double[] line in Cs137Lines)
                {
                    expected += src.Becquerel * line[1] * g * DoseRateCoefficients.DoseRatePerFluenceRate(line[0]);
                }

                double expected662 = src.Becquerel * Cs137Lines[0][1] * g
                                     * DoseRateCoefficients.DoseRatePerFluenceRate(Cs137Lines[0][0]);
                ResponseMatrix matrix = LoadMatrix(src.Spectrum, geometry);
                Console.WriteLine();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: {1}, домик {2}; A = {3:F1} Бк; {4}", src.Spectrum, geometryOf[src.Spectrum],
                    shield ? "ДА" : "нет", src.Becquerel, note));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    свободное поле: {0:F5} мкЗв/ч (661.7 — {1:F5}, K-рентген Ba — {2:F5})",
                    expected, expected662, expected - expected662));

                // Показание приложения — по кривой как есть (после лечения у
                // домика — отказ) и по кривой без признака (число прежним ходом).
                ResultData asFree = WithEfficiency(data, free);
                foreach (bool useMatrix in new[] { true, false })
                {
                    if (useMatrix && matrix == null) continue;
                    string path = useMatrix ? "матрица" : "«≈»";
                    DoseRate app = manager.Calculate(data, DoseRateInputOf(data.Efficiency, useMatrix ? matrix : null));
                    DoseRate fg = manager.Calculate(asFree, DoseRateInputOf(asFree.Efficiency, useMatrix ? matrix : null));
                    DoseRate bg = null;
                    if (data.BackgroundEnergySpectrum != null && data.BackgroundEnergySpectrum.Spectrum != null)
                    {
                        ResultData bgData = WithSpectrum(asFree, data.BackgroundEnergySpectrum);
                        bg = manager.Calculate(bgData, DoseRateInputOf(asFree.Efficiency, useMatrix ? matrix : null));
                    }

                    if (!string.IsNullOrEmpty(fg.Refusal))
                    {
                        Console.WriteLine("    " + path + ": ОТКАЗ без домика «" + Short(fg.Refusal) + "»");
                        continue;
                    }

                    double bgRate = bg != null && string.IsNullOrEmpty(bg.Refusal) ? bg.Rate : double.NaN;
                    double net = fg.Rate - (double.IsNaN(bgRate) ? 0.0 : bgRate);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    {0}: показание {1:F5} мкЗв/ч; фон файла {2:F5}; за вычетом фона {3:F5} = свободное ×{4:F3}"
                        + " ({5:+0.0;-0.0} %)", path, fg.Rate, bgRate, net, net / expected, 100.0 * (net / expected - 1.0)));

                    // Разбивка: диапазон 662 (от комптоновского края до верха пика)
                    // и прочие; фон — по тем же диапазонам.
                    double peakDose = 0.0, peakBg = 0.0, low = 0.0, lowBg = 0.0, mid = 0.0, midBg = 0.0;
                    for (int k = 0; k < fg.Ranges.Count; k++)
                    {
                        DoseRateRange r = fg.Ranges[k];
                        DoseRateRange rb = bg != null && string.IsNullOrEmpty(bg.Refusal) && k < bg.Ranges.Count
                            ? bg.Ranges[k] : null;
                        double d = r.Skipped ? 0.0 : r.DoseRate;
                        double db = rb == null || rb.Skipped ? 0.0 : rb.DoseRate;
                        if (r.LowKev < 740.0 && r.HighKev > 580.0) { peakDose += d; peakBg += db; }
                        else if (r.HighKev <= 120.0) { low += d; lowBg += db; }
                        else if (r.HighKev <= 580.0) { mid += d; midBg += db; }
                    }

                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "      по диапазонам (за вычетом фона): ниже 120 кэВ {0:F5} ({1:F5}); 120…580 кэВ {2:F5} ({3:F5});"
                        + " у 662 {4:F5} ({5:F5}) = свободное 661.7 ×{6:F3}",
                        low, low - lowBg, mid, mid - midBg, peakDose, peakDose - peakBg, (peakDose - peakBg) / expected662));

                    if (useMatrix)
                    {
                        // Посылка `AMBER114` («показание ×2.6 к свободному полю»)
                        // сравнивала ВСЁ показание — вместе с фоном домика — с
                        // полем одного источника. Проба держит то, что намерено:
                        // признак домика числа не меняет (у домика и без него —
                        // одно и то же), а избыток источника над свободным полем
                        // печатается выше рядом с приборами без домика.
                        Ok(string.IsNullOrEmpty(app.Refusal) && app.Rate == fg.Rate,
                           src.Spectrum + ": доза по кривой " + (shield ? "в домике" : "без домика")
                           + " — число, то же, что у копии без признака домика");
                        excess[src.Spectrum] = net / expected;
                    }
                }

                // Эффективная площадь торца на энергиях рассеяния от стен против
                // средней проекции кристалла (Коши: S/4 выпуклого тела).
                if (shield && matrix != null)
                {
                    GeometryModel c = geometry.InCentimeters();
                    double ax, ay, hc;
                    c.CrystalBoxInScene(out ax, out ay, out hc);
                    double sx = 2.0 * ax, sy = 2.0 * ay;
                    double front = sx * sy;
                    double s4 = 0.5 * (sx * sy + sx * hc + sy * hc);
                    foreach (double e in new[] { 75.0, 85.0, 185.0, 250.0 })
                    {
                        double eps = DoseRateInput.FullEfficiency(matrix, e);
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "    {0,6:F1} кэВ: ε/G = {1:F3} см² (торец {2:F3} см²); S/4 кристалла {3:F3} см² — поток сбоку"
                            + " завышен ×{4:F2}", e, eps / g, front, s4, s4 / (eps / g)));
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("  итог §1 (матрица, за вычетом фона файла, к свободному полю): "
                              + string.Join("; ", excess.Select(p => p.Key + " ×" + p.Value.ToString("F3", CultureInfo.InvariantCulture))));
            return true;
        }

        // ==================================================================
        // §2. AMBER102 — фон по кривой точки
        // ==================================================================

        static bool IsoBackground()
        {
            Head("§2. AMBER102: фон по кривой точечной сцены против сцены поля ISO той же геометрии");
            string isoCurve = Path.Combine(isoDir, "G1S_point5_iso100_curve.xml");
            if (!File.Exists(isoCurve))
            {
                Console.WriteLine("ОСНАСТКА: нет " + isoCurve);
                return false;
            }

            EfficiencyConfigData iso;
            using (var fs = new FileStream(isoCurve, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                iso = (EfficiencyConfigData)new XmlSerializer(typeof(EfficiencyConfigData)).Deserialize(fs);
            }

            ResultData point = LoadSpectrum("G1S16_Cs137_P5");
            ResultData background = LoadSpectrum("G1S16_Cs137_P25");
            if (point == null || background == null) return false;
            string note;
            double g = DoseRateGeometry.FluencePerPhoton(point.Efficiency.Geometry, out note);
            Console.WriteLine("  точка: " + point.Efficiency.Name + ", " + note + "; поле: " + iso.Name
                              + " (" + iso.ComputeStamp + ")");
            DoseRateInput pin = DoseRateInputOf(point.Efficiency, null);
            DoseRateInput iin = DoseRateInputOf(iso, null);
            foreach (double e in new[] { 20.0, 30.0, 60.0, 100.0, 200.0, 350.0, 662.0, 1000.0, 1460.0, 2600.0 })
            {
                double apt = pin.EfficiencyAt(e) / g;
                double aiso = iin.EfficiencyAt(e);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0,6:F0} кэВ: A_пт = ε/G = {1,8:F3} см², A_iso = {2,8:F3} см²; A_пт/A_iso = {3:F3} → фон по кривой"
                    + " точки {4:+0.0;-0.0} %", e, apt, aiso, apt / aiso, 100.0 * (aiso / apt - 1.0)));
            }

            var manager = new DoseRateManager(Config());
            foreach (string which in new[] { "BackgroundEnergySpectrum G1S16_Cs137_P25", "BackgroundEnergySpectrum G1S16_Cs137_P5" })
            {
                ResultData src = which.EndsWith("P25", StringComparison.Ordinal) ? background : point;
                ResultData bgPoint = WithSpectrum(WithEfficiencyData(src, point.Efficiency), src.BackgroundEnergySpectrum);
                ResultData bgIso = WithSpectrum(WithEfficiencyData(src, iso), src.BackgroundEnergySpectrum);
                DoseRate dp = manager.Calculate(bgPoint, pin);
                DoseRate di = manager.Calculate(bgIso, iin);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: по кривой точки {1:F5} мкЗв/ч, по ISO {2:F5}; точка/ISO − 1 = {3:+0.0;-0.0} %",
                    which, dp.Rate, di.Rate, 100.0 * (dp.Rate / di.Rate - 1.0)));
                Console.WriteLine("    строка точки: «" + dp + "»");
                Console.WriteLine("    строка ISO:   «" + di + "»");

                // Лечение: у не-ISO кривой — признак сцены с источником, по нему
                // строка состояния даёт подсказку об ISO; у ISO признака нет.
                // (`AMBER216`, решение Amber 07.10.2026 «Только значение мощности
                // дозы с погрешностью, без всяких текстов.») Приписки к самой
                // строке нет ни у той, ни у другой.
                Ok(dp.SourceScene, which + ": у кривой точки признак сцены с источником (подсказка об ISO)");
                Ok(!di.SourceScene, which + ": у кривой ISO признака нет");
                Ok(!dp.ToString().Contains("ISO") && !di.ToString().Contains("ISO"),
                   which + ": приписки об ISO в строке нет");
            }

            return true;
        }

        // ==================================================================
        // §3. AMBER117 — ослабление в пробе
        // ==================================================================

        static bool Thickness()
        {
            Head("§3. AMBER117: ослабление нерассеянного потока в пробе, a(E) = I(μ)/I(0)");

            // Тело грунта сосудом — ровно то, что П145 мерил отрицательным
            // контролем: сцена «на земле» Gamma-1S 63×63, сцена снята руками.
            GeometryModel blank = GeometryEditorPanel.Blank();
            GeometryPresets.Items.First(p => p.Name == "Gamma-1S UDS-GC 63x63").Apply(blank);
            GeometryModel ground = blank.Clone();
            GeometryScenes.Ground(ground, 3000.0);
            GeometryModel vessel = ground.Clone();
            vessel.Scene = GeometrySceneKind.None;
            double gv = Report("грунт сосудом (сцена «нет»), Gamma-1S 63×63", vessel);
            string refusal = RefusalOf(vessel);
            if (!string.IsNullOrEmpty(soilSpectrum))
            {
                // Для проверки экраном: спектр `G1S16_Cs137_P5` с кривой, чья
                // геометрия — грунт сосудом. Пишется только по ключу и только
                // туда, куда сказано.
                string from = Path.Combine(corpusDir, "spectra", "G1S16_Cs137_P5.xml");
                ResultDataFile file;
                var serializer = new XmlSerializer(typeof(ResultDataFile));
                using (var fs = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    file = (ResultDataFile)serializer.Deserialize(fs);
                }

                GeometryModel soilVessel = vessel.Clone();
                soilVessel.Name = "Soil as vessel (P165)";
                file.ResultDataList[0].Efficiency.Geometry = soilVessel;
                file.ResultDataList[0].Efficiency.UseResponseMatrix = false;
                using (var fs = new FileStream(soilSpectrum, FileMode.Create, FileAccess.Write))
                {
                    serializer.Serialize(fs, file);
                }

                Console.WriteLine("  записан спектр для экрана: " + soilSpectrum);
            }
            Ok(refusal != null, "грунт сосудом: доза отказывает словами"
               + (refusal == null ? " (G = " + gv.ToString("E4", CultureInfo.InvariantCulture) + ")" : " «" + Short(refusal) + "»"));

            // Все объёмные пробы корпуса части known: a(E) на энергиях линий и
            // a_eff — среднее по дозе диапазонов (путь матрицы).
            var manager = new DoseRateManager(Config());
            Console.WriteLine();
            Console.WriteLine("  объёмные пробы корпуса (known): a на 60/662/1460.8 кэВ; a_eff — по дозе диапазонов (матрица)");
            var aeffs = new List<KeyValuePair<string, double>>();
            foreach (KeyValuePair<string, string> pair in geometryOf.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                ResultData data = LoadSpectrum(pair.Key, true);
                if (data == null || data.Efficiency == null || !data.Efficiency.HasGeometry) continue;
                GeometryModel geo = data.Efficiency.Geometry;
                if (geo.SourceType == GeometrySourceType.Point || geo.Scene != GeometrySceneKind.None) continue;
                Body body = BodyOf(geo);
                if (body == null) continue;
                double i0 = Integral(body, 0.0);
                ResponseMatrix matrix = LoadMatrix(pair.Key, geo);
                DoseRate app = manager.Calculate(data, DoseRateInputOf(data.Efficiency, matrix));

                // Число до лечения — той же кривой через сосуд без признака
                // толщины не получить: лечение отказывает по физике. Поэтому
                // a_eff — по диапазонам прежнего хода, если число есть, иначе —
                // по диапазонам копии, где проба — воздух (форма та же).
                // До лечения число есть у всех; после — у отказанных a_eff не
                // по чему считать (диапазоны пусты), печатается NaN.
                DoseRate basis = string.IsNullOrEmpty(app.Refusal) ? app : null;

                double num = 0.0, den = 0.0;
                if (basis != null)
                {
                    foreach (DoseRateRange r in basis.Ranges)
                    {
                        if (r.Skipped || !(r.DoseRate > 0.0)) continue;
                        num += r.DoseRate * Integral(body, geo.Source.LinearAttenuation(r.RepresentativeKev)) / i0;
                        den += r.DoseRate;
                    }
                }

                double aeff = den > 0.0 ? num / den : double.NaN;
                aeffs.Add(new KeyValuePair<string, double>(pair.Key, aeff));

                // Лечение: у приложения своё ослабление (своя сетка хорд) —
                // сверка с этим независимым счётом и порог.
                MethodInfo integral = typeof(DoseRateGeometry).GetMethod("SampleFluenceIntegral",
                    BindingFlags.Public | BindingFlags.Static);
                PropertyInfo transmission = typeof(DoseRate).GetProperty("SampleTransmission");
                if (integral != null && transmission != null)
                {
                    double mu662 = geo.Source.LinearAttenuation(661.657);
                    double appA = (double)integral.Invoke(null, new object[] { geo, mu662 })
                                  / (double)integral.Invoke(null, new object[] { geo, 0.0 });
                    double probeA = Integral(body, mu662) / i0;
                    double appAeff = (double)transmission.GetValue(app, null);
                    Ok(Math.Abs(appA / probeA - 1.0) < 3e-3
                       && string.IsNullOrEmpty(app.Refusal) && Math.Abs(appAeff - aeff) < 5e-3,
                       string.Format(CultureInfo.InvariantCulture,
                           "{0}: число есть; a(662) приложения {1:F4} = пробы {2:F4}; a_eff приложения {3:F4} = пробы {4:F4}",
                           pair.Key, appA, probeA, appAeff, aeff));
                }
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0,-26} {1,-10} {2,-26} ρ {3,5:F2}: a(60) {4:F3}, a(662) {5:F3}, a(1461) {6:F3}; a_eff {7:F3};"
                    + " доза {8}",
                    pair.Key, geo.SourceType, geo.Source.Name, geo.Source.Density,
                    Integral(body, geo.Source.LinearAttenuation(59.54)) / i0,
                    Integral(body, geo.Source.LinearAttenuation(661.657)) / i0,
                    Integral(body, geo.Source.LinearAttenuation(1460.822)) / i0,
                    aeff,
                    string.IsNullOrEmpty(app.Refusal) ? app.Rate.ToString("R", CultureInfo.InvariantCulture) + " мкЗв/ч" : "ОТКАЗ «" + Short(app.Refusal) + "»"));
            }

            var finite = aeffs.Where(p => !double.IsNaN(p.Value)).OrderBy(p => p.Value).ToList();
            if (finite.Count > 0)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  a_eff по корпусу: {0} проб, наименьшее {1:F3} ({2}), наибольшее {3:F3}",
                    finite.Count, finite[0].Value, finite[0].Key, finite[finite.Count - 1].Value));
            }

            return true;
        }

        static double Report(string name, GeometryModel model)
        {
            string note;
            double g = DoseRateGeometry.FluencePerPhoton(model, out note);
            Body b = BodyOf(model);
            double i0 = Integral(b, 0.0);
            Console.WriteLine("  " + name + ": " + note);
            Ok(Math.Abs(i0 / (g * b.Volume) - 1.0) < 2e-3,
               string.Format(CultureInfo.InvariantCulture,
                   "положительный контроль тела: I(0) по направлениям = G·V приложения ({0:+0.0000;-0.0000} %)",
                   100.0 * (i0 / (g * b.Volume) - 1.0)));
            foreach (double e in new[] { 59.54, 661.657, 1460.822, 2614.511 })
            {
                double mu = model.Source.LinearAttenuation(e);
                double ia = Integral(b, mu);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0,8:F3} кэВ: μ = {1:F5} 1/см; a = {2:F4} (без ослабления ×{3:F2})", e, mu, ia / i0, i0 / ia));
            }

            return g;
        }

        /// <summary>
        /// Отказ дозы по кривой с геометрией <paramref name="model"/>: на
        /// множителе (сборка входа) либо в расчёте — спектр `G1S16_Cs137_P5`
        /// той же кривой (пиковой, «≈») с подменённой геометрией.
        /// </summary>
        static string RefusalOf(GeometryModel model)
        {
            ResultData data = LoadSpectrum("G1S16_Cs137_P5");
            ResultData moved = WithEfficiency(data, model);
            DoseRateInput input;
            try
            {
                input = DoseRateInput.Of(moved.Efficiency, null);
            }
            catch (DoseRateRefusalException ex)
            {
                return ex.Message;
            }

            DoseRate rate = new DoseRateManager(Config()).Calculate(moved, input);
            Console.WriteLine("    доза по спектру G1S16_Cs137_P5 с этой геометрией: «" + rate + "»");
            return string.IsNullOrEmpty(rate.Refusal) ? null : rate.Refusal;
        }

        sealed class Body
        {
            public double Zc;
            public int Kind;               // 0 — цилиндр, 1 — маринелли, 2 — кювета
            public double R, Z0, Z1;       // внешнее тело: радиус (цилиндр/маринелли), z-слой
            public double Ax, Ay;          // кювета: полуширины
            public double Rin, Zin;        // маринелли: колодец ρ < Rin при z > Zin (не проба)
            public double Volume;
        }

        /// <summary>Тело пробы в системе <see cref="DoseRateGeometry.FluencePerPhoton"/>.</summary>
        static Body BodyOf(GeometryModel model)
        {
            GeometryModel g = model.InCentimeters();
            double hc = g.CrystalHeight, ax0, ay0;
            if (g.Shape == CrystalShape.Box) g.CrystalBoxInScene(out ax0, out ay0, out hc);
            double tfr = g.FrontReflectorThickness, tfc = g.FrontCladdingThickness;
            double tfg = Math.Max(0.0, g.FrontGapThickness);
            if (g.Facing == GeometryDetectorFacing.Side)
            {
                tfr = g.SideReflectorThickness;
                tfc = g.SideCladdingThickness;
                tfg = Math.Max(0.0, g.SideGapThickness);
            }

            double zFace = -(tfr + tfg + tfc);
            var b = new Body { Zc = 0.5 * hc };
            switch (g.SourceType)
            {
                case GeometrySourceType.Cylinder:
                    b.Kind = 0;
                    b.R = Math.Max(0.0, 0.5 * g.BeakerDiameter - g.BeakerSideWallThickness);
                    b.Z1 = zFace - g.BeakerToDetectorDistance - g.BeakerEndWallThickness;
                    b.Z0 = b.Z1 - g.SourceHeight;
                    b.Volume = Math.PI * b.R * b.R * (b.Z1 - b.Z0);
                    return b;
                case GeometrySourceType.Marinelli:
                {
                    double rh = 0.5 * g.MarinelliHoleDiameter;
                    double ths = g.MarinelliHoleSideThickness, the = g.MarinelliHoleEndWallThickness;
                    double rOut = Math.Max(0.5 * g.MarinelliBeakerDiameter, rh + ths + 0.1);
                    double rSrcOut = Math.Max(rh + ths, rOut - g.MarinelliSideThickness);
                    double hs = g.MarinelliSourceHeight, hh = g.MarinelliHoleHeight;
                    double zCeiling = zFace - g.MarinelliToDetectorDistance;
                    double cap = Math.Max(0.0, hs - hh);
                    b.Kind = 1;
                    b.R = rSrcOut;
                    b.Z0 = zCeiling - the - cap;
                    b.Z1 = b.Z0 + hs;
                    b.Rin = rh + ths;
                    b.Zin = zCeiling - the;
                    b.Volume = Math.PI * b.Rin * b.Rin * cap + Math.PI * (rSrcOut * rSrcOut - b.Rin * b.Rin) * hs;
                    return b;
                }
                case GeometrySourceType.Box:
                    b.Kind = 2;
                    b.Ax = Math.Max(0.0, 0.5 * g.BoxSourceX - g.BoxSideWallThickness);
                    b.Ay = Math.Max(0.0, 0.5 * g.BoxSourceY - g.BoxSideWallThickness);
                    b.Z1 = zFace - g.BoxToDetectorDistance - g.BoxEndWallThickness;
                    b.Z0 = b.Z1 - g.BoxSourceHeight;
                    b.Volume = 4.0 * b.Ax * b.Ay * (b.Z1 - b.Z0);
                    return b;
                default:
                    return null;
            }
        }

        /// <summary>`I(μ) = (1/4π)∫dΩ (1 − e^{−μL})/μ`, см; μ = 0 — `(1/4π)∫L dΩ`.</summary>
        static double Integral(Body b, double mu)
        {
            int nc = b.Kind == 2 ? 4000 : 40000;
            int nf = b.Kind == 2 ? 64 : 1;
            double sum = 0.0;
            double dc = 2.0 / nc;
            for (int i = 0; i < nc; i++)
            {
                double c = -1.0 + (i + 0.5) * dc;      // косинус к оси ВНИЗ (к пробе)
                double s = Math.Sqrt(Math.Max(0.0, 1.0 - c * c));
                double part = 0.0;
                for (int j = 0; j < nf; j++)
                {
                    double f = (j + 0.5) * 0.5 * Math.PI / nf;   // четверть — по симметрии кюветы
                    double L = Chord(b, c, s * Math.Cos(f), s * Math.Sin(f), s);
                    if (!(L > 0.0)) continue;
                    part += mu > 0.0 ? (1.0 - Math.Exp(-mu * L)) / mu : L;
                }

                sum += part / nf;
            }

            return 0.5 * sum * dc;
        }

        /// <summary>Хорда пробы по лучу из центра кристалла (z = Zc − t·c).</summary>
        static double Chord(Body b, double c, double dx, double dy, double s)
        {
            double inf = double.PositiveInfinity;
            // z-слой [Z0, Z1].
            double t0, t1;
            if (c != 0.0)
            {
                double ta = (b.Zc - b.Z1) / c, tb = (b.Zc - b.Z0) / c;
                t0 = Math.Min(ta, tb);
                t1 = Math.Max(ta, tb);
            }
            else
            {
                if (b.Zc < b.Z0 || b.Zc > b.Z1) return 0.0;
                t0 = -inf; t1 = inf;
            }

            t0 = Math.Max(0.0, t0);
            if (b.Kind == 2)
            {
                double tx = Math.Abs(dx) > 0.0 ? b.Ax / Math.Abs(dx) : inf;
                double ty = Math.Abs(dy) > 0.0 ? b.Ay / Math.Abs(dy) : inf;
                t1 = Math.Min(t1, Math.Min(tx, ty));
            }
            else
            {
                t1 = Math.Min(t1, s > 0.0 ? b.R / s : inf);
            }

            if (b.Kind == 1)
            {
                double side = s > 0.0 ? b.Rin / s : inf;
                double bottom = c > 0.0 ? (b.Zc - b.Zin) / c : inf;
                t0 = Math.Max(t0, Math.Min(side, bottom));
            }

            return Math.Max(0.0, t1 - t0);
        }

        // ==================================================================
        // Общее
        // ==================================================================

        /// <summary>
        /// Вход дозы; отказ на сборке входа — входом-отказом (null → отказ в
        /// `Calculate`), чтобы проба шла дальше.
        /// </summary>
        static DoseRateInput DoseRateInputOf(EfficiencyConfigData curve, ResponseMatrix matrix)
        {
            try
            {
                return DoseRateInput.Of(curve, matrix);
            }
            catch (DoseRateRefusalException)
            {
                return null;
            }
        }

        static ResultData WithEfficiency(ResultData data, GeometryModel geometry)
        {
            EfficiencyConfigData curve = Clone(data.Efficiency);
            curve.Geometry = geometry;
            return WithEfficiencyData(data, curve);
        }

        static ResultData WithEfficiencyData(ResultData data, EfficiencyConfigData curve)
        {
            ResultData copy = data.Clone();
            copy.Efficiency = curve;
            return copy;
        }

        static ResultData WithSpectrum(ResultData data, EnergySpectrum spectrum)
        {
            ResultData copy = data.Clone();
            copy.EnergySpectrum = spectrum;
            return copy;
        }

        static EfficiencyConfigData Clone(EfficiencyConfigData curve)
        {
            var serializer = new XmlSerializer(typeof(EfficiencyConfigData));
            using (var ms = new MemoryStream())
            {
                serializer.Serialize(ms, curve);
                ms.Position = 0;
                return (EfficiencyConfigData)serializer.Deserialize(ms);
            }
        }

        static Dictionary<string, string> ReadParts(string path)
        {
            if (!File.Exists(path)) return null;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8).Skip(1))
            {
                string[] cells = line.Split(',');
                if (cells.Length >= 4 && cells[2] == "known" && cells[3].Length > 0)
                {
                    map[cells[0]] = cells[3];
                }
            }

            return map;
        }

        static ResultData LoadSpectrum(string key, bool quiet = false)
        {
            string path = Path.Combine(corpusDir, "spectra", key + ".xml");
            if (!File.Exists(path))
            {
                if (!quiet) Console.WriteLine("ОСНАСТКА: нет " + path);
                return null;
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var file = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                ResultData data = file.ResultDataList.Count > 0 ? file.ResultDataList[0] : null;
                if (data == null || data.Efficiency == null || !data.Efficiency.HasGeometry)
                {
                    if (!quiet) Console.WriteLine("ОСНАСТКА: у спектра нет кривой с геометрией: " + key);
                    return null;
                }

                return data;
            }
        }

        static ResponseMatrix LoadMatrix(string key, GeometryModel geometry)
        {
            string name;
            if (!geometryOf.TryGetValue(key, out name)) return null;
            string path = Path.Combine(corpusDir, "geometries", name + ".rmx");
            if (!File.Exists(path)) return null;
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrix.Load(path, out refusal, out fileFormat);
            if (matrix == null) return null;
            if (geometry != null && !matrix.IsValidFor(geometry))
            {
                // Как у `DoseGridProbe`: клеймо — в памяти, файл не пишется.
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

        static string Short(string text)
        {
            return text.Length > 140 ? text.Substring(0, 140) + "…" : text;
        }

        static void Head(string title)
        {
            Console.WriteLine();
            Console.WriteLine(title);
        }

        static void Ok(bool condition, string what)
        {
            checks++;
            if (!condition) failed++;
            Console.WriteLine((condition ? "  [ok]   " : "  [FAIL] ") + what);
        }
    }
}
