using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace EffSim
{
    /// <summary>
    /// Харнесс расчёта кривой эффективности по файлу геометрии и сверки с
    /// эталонной кривой, посчитанной монте-карловской программой LSRM.
    ///
    ///   effsim --geometry=X.in [--ref=curve.txt] [--n=200000] [--out=curve.csv]
    ///   effsim --all=Models --refdir="Exported Curves"   — сверка по всем парам
    /// </summary>
    static class Program
    {
        // пара «файл геометрии -> эталонная кривая»; пары установлены по
        // содержимому файлов и по величине эффективности (см. журнал)
        static readonly string[,] Pairs =
        {
            { "Nano16Pro.in", "Nano 16 - cilinder - 5cm dist.txt" },
            { "Nano16Pro_tube.in", "Nano 16 - cilinder.txt" },
            { "Nano16Pro_Marinelli.in", "Nano 16 - marinelli.txt" },
            { "Obsidian Marinelli 0.5.in", "Obsidian - marinelli 0.5.txt" },
            { "RadiaCode_AuthorMarinelli0.2.in", "RadiaCode - author marinelli 0.2.txt" },
            { "RadiaCode_AuthorMarinelli0.5.in", "RadiaCode - author marinelli 0.5.txt" },
            { "RadiaCode_Marinelli0.5.in", "RadiaCode - marinelli 0.5.txt" },
        };

        static int Main(string[] args)
        {
            try
            {
                string geometry = null, reference = null, outPath = null, all = null, refDir = null;
                int n = 200000;
                bool mountFront = false;
                bool electron = true, brems = true, xray = true, cohPass = true, scatter = true;
                bool kfracEnergy = true;
                double detour = 1.0, halfWidth = 0.0, halfWidthFraction = 0.0, fwhm662 = 0.0;
                double point = -1.0;
                string list = null;
                bool total = false;
                bool dumpScene = false;
                double histBin = 0.0;
                double escSlope = -1.0;         // < 0 — умолчание симулятора
                double escT0 = -1.0;
                foreach (string arg in args)
                {
                    int eq = arg.IndexOf('=');
                    string key = eq > 0 ? arg.Substring(0, eq) : arg;
                    string value = eq > 0 ? arg.Substring(eq + 1) : "";
                    switch (key)
                    {
                        case "--geometry": geometry = value; break;
                        case "--ref": reference = value; break;
                        case "--out": outPath = value; break;
                        case "--n": n = int.Parse(value, CultureInfo.InvariantCulture); break;
                        case "--all": all = value; break;
                        case "--refdir": refDir = value; break;
                        case "--mount-front": mountFront = true; break;
                        case "--electron": electron = true; break;
                        case "--no-electron": electron = false; break;
                        case "--no-brems": brems = false; break;
                        case "--no-xray": xray = false; break;
                        case "--no-coherent-pass": cohPass = false; break;
                        case "--no-scatter": scatter = false; break;
                        // доля K-оболочки константой со скачка на крае, как до
                        // пооболочечных сечений EPICS2017 — измерительный ключ
                        case "--no-kfrac-e": kfracEnergy = false; break;
                        // разрешение прибора: ПШПВ на 662 кэВ в процентах; даёт
                        // допуск пика по энергии, как DS_Fwhm662 в геометрии
                        case "--fwhm662":
                            fwhm662 = double.Parse(value, CultureInfo.InvariantCulture); break;
                        case "--detour": detour = double.Parse(value, CultureInfo.InvariantCulture); break;
                        // наклон эффективной глубины вылета электрона, 1/МэВ
                        case "--esc-slope":
                            escSlope = double.Parse(value, CultureInfo.InvariantCulture); break;
                        // порог включения вылета, кэВ
                        case "--esc-t0":
                            escT0 = double.Parse(value, CultureInfo.InvariantCulture); break;
                        case "--peak-halfwidth":
                            halfWidth = double.Parse(value, CultureInfo.InvariantCulture); break;
                        // допуск как доля энергии: пик имеет ширину, и она
                        // пропорциональна энергии, а не постоянна в кэВ
                        // подменить источник точечным на заданном расстоянии:
                        // так меряются заводские коэффициенты BecqMoni
                        case "--point": point = double.Parse(value, CultureInfo.InvariantCulture); break;
                        case "--energies": list = value; break;
                        // печатать и ПОЛНУЮ эффективность (аналоговый счёт,
                        // сфера) — для сверки с Geant4 (eps_total ион/моно)
                        case "--total": total = true; break;
                        // напечатать машинный дамп сцены для g4cf --scene и выйти
                        case "--dump-scene": dumpScene = true; break;
                        // напечатать гистограмму отклика (шаг в кэВ) по списку
                        // --energies — для сверки с g4cf hist. Свет выключен:
                        // сверка идёт в шкале ПОГЛОЩЁННОЙ энергии.
                        case "--hist":
                            histBin = double.Parse(value, CultureInfo.InvariantCulture); break;
                        case "--hw-frac":
                            halfWidthFraction = double.Parse(value, CultureInfo.InvariantCulture); break;
                        default: throw new ArgumentException("Unknown option: " + arg);
                    }
                }

                if (all != null)
                {
                    for (int i = 0; i < Pairs.GetLength(0); i++)
                    {
                        RunOne(Path.Combine(all, Pairs[i, 0]),
                               refDir == null ? null : Path.Combine(refDir, Pairs[i, 1]),
                               null, n, mountFront, electron, brems, xray, cohPass, scatter, kfracEnergy, detour, halfWidth, halfWidthFraction, fwhm662, point, list, total, escSlope, escT0);
                        Console.WriteLine();
                    }

                    return 0;
                }

                if (geometry == null)
                {
                    Console.Error.WriteLine("--geometry is required");
                    return 1;
                }

                if (dumpScene)
                {
                    GeometryModel dg = GeometryModel.Load(geometry);
                    if (point >= 0.0)
                    {
                        dg.SourceType = GeometrySourceType.Point;
                        dg.PointDistance = point;
                    }

                    Console.WriteLine(new EfficiencySimulator(dg).DumpScene());
                    return 0;
                }

                if (histBin > 0.0)
                {
                    if (string.IsNullOrEmpty(list))
                    {
                        Console.Error.WriteLine("--hist требует --energies");
                        return 1;
                    }

                    GeometryModel hg = GeometryModel.Load(geometry);
                    EfficiencySimulator hsim = new EfficiencySimulator(hg)
                    {
                        Histories = n,
                        ElectronEscape = electron,
                        Bremsstrahlung = brems,
                        XrayEscape = xray,
                        CoherentPassesThrough = cohPass,
                        SingleScatter = scatter,
                        KFractionByEnergy = kfracEnergy,
                        ElectronDetour = detour,
                        // сверка с Geant4 — в шкале ПОГЛОЩЁННОЙ энергии
                        LightNonproportionality = false,
                    };
                    foreach (string one in list.Split(','))
                    {
                        double e = double.Parse(one, CultureInfo.InvariantCulture);
                        double relErr;
                        double[] h = hsim.Response(e, histBin, out relErr);
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "OURHISTBEGIN e_kev={0:R} bins={1} bin_kev={2:R} hist={3}",
                            e, h.Length, histBin, n));
                        for (int i = 0; i < h.Length; i++)
                        {
                            if (h[i] > 0.0)
                            {
                                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                                                "OURHIST {0} {1:E6}", i, h[i]));
                            }
                        }

                        Console.WriteLine("OURHISTEND");
                    }

                    return 0;
                }

                RunOne(geometry, reference, outPath, n, mountFront, electron, brems, xray, cohPass, scatter, kfracEnergy, detour, halfWidth, halfWidthFraction, fwhm662, point, list, total, escSlope, escT0);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        static void RunOne(string geometryPath, string referencePath, string outPath, int n,
                           bool mountFront, bool electron, bool brems, bool xray, bool cohPass, bool scatter, bool kfracEnergy, double detour, double halfWidth,
                           double halfWidthFraction, double fwhm662, double point, string list,
                           bool total = false, double escSlope = -1.0, double escT0 = -1.0)
        {
            GeometryModel g = GeometryModel.Load(geometryPath);
            if (point >= 0.0)
            {
                g.SourceType = GeometrySourceType.Point;
                g.PointDistance = point;
            }

            if (fwhm662 > 0.0)
            {
                g.FwhmAt662Percent = fwhm662;
            }

            Console.WriteLine("=== {0}", Path.GetFileNameWithoutExtension(geometryPath));
            Console.WriteLine("    {0}", g.Describe());
            if (!g.IsScintillator)
            {
                Console.WriteLine("    пропуск: не сцинтиллятор");
                return;
            }

            EfficiencySimulator sim = new EfficiencySimulator(g)
            {
                Histories = n,
                MountingInFront = mountFront,
                ElectronEscape = electron,
                Bremsstrahlung = brems,
                XrayEscape = xray,
                CoherentPassesThrough = cohPass,
                SingleScatter = scatter,
                KFractionByEnergy = kfracEnergy,
                ElectronDetour = detour,
                PeakHalfWidthKev = halfWidth,
            };
            if (escSlope >= 0.0)
            {
                sim.ElectronEscapeSlope = escSlope;
            }

            if (escT0 >= 0.0)
            {
                sim.ElectronEscapeT0Kev = escT0;
            }

            Console.WriteLine("    сцена: {0}", sim.DescribeScene());
            Console.WriteLine("    электрон: {0}, вылет {1}, тормозное {2}, detour {3:F2}, допуск {4}, рентген {5}, когерентное сквозь {6}, рассеяние {7}, доля K по энергии {8} ",
                              sim.ElectronMaterialName == "" ? "состава нет в ESTAR" : sim.ElectronMaterialName,
                              electron ? "да" : "нет", brems ? "да" : "нет", detour, halfWidth,
                              xray ? "да" : "нет", cohPass ? "да" : "нет", scatter ? "да" : "нет",
                              kfracEnergy ? "да" : "нет");

            List<double> energies;
            Dictionary<double, double> truth = null;
            if (!string.IsNullOrEmpty(list))
            {
                energies = list.Split(',')
                               .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToList();
            }
            else if (!string.IsNullOrEmpty(referencePath) && File.Exists(referencePath))
            {
                truth = LoadCurve(referencePath);
                // Эталоны сняты с разным шагом (20 и 50 кэВ); берём около
                // полутора десятков точек, кривая гладкая.
                List<double> full = truth.Keys.Where(e => e >= 40.0 && e <= 3000.0)
                                              .OrderBy(e => e).ToList();
                int stride = Math.Max(1, full.Count / 15);
                energies = full.Where((e, i) => i % stride == 0).ToList();
            }
            else
            {
                energies = new List<double> { 50, 60, 80, 100, 150, 200, 300, 400, 600, 662,
                                              800, 1000, 1250, 1461, 1800, 2200, 2614 };
            }

            List<string> csv = new List<string> { "E_keV,eps_sim,err_pct,eps_ref,ratio" };
            List<double> ratios = new List<double>();
            Console.WriteLine("    {0,7}  {1,12}  {2,7}  {3,12}  {4,7}", "E,кэВ", "расчёт", "±%", "эталон", "отн.");
            foreach (double e in energies)
            {
                sim.PeakHalfWidthKev = halfWidthFraction > 0.0 ? halfWidthFraction * e
                    : halfWidth > 0.0 ? halfWidth
                    : g.PeakHalfWidthKev(e);
                double err;
                double eps = sim.Efficiency(e, out err);
                if (total)
                {
                    double totalError;
                    double totalEff = sim.TotalEfficiency(e, out totalError);
                    Console.WriteLine("    {0,7:F0}  полная {1:E4} ±{2:F2} %",
                                      e, totalEff, totalError);
                }

                double refValue = 0.0;
                string ratio = "-";
                if (truth != null && truth.TryGetValue(e, out refValue) && refValue > 0.0)
                {
                    double k = eps / refValue;
                    ratios.Add(k);
                    ratio = k.ToString("F3", CultureInfo.InvariantCulture);
                }

                csv.Add(string.Format(CultureInfo.InvariantCulture, "{0:G6},{1:E5},{2:F2},{3:E5},{4}",
                                      e, eps, err, refValue, ratio));
                Console.WriteLine("    {0,7:F0}  {1,12:E4}  {2,7:F2}  {3,12:E4}  {4,7}",
                                  e, eps, err, refValue, ratio);
            }

            if (ratios.Count > 0)
            {
                ratios.Sort();
                double median = ratios[ratios.Count / 2];
                double lo = ratios[0], hi = ratios[ratios.Count - 1];
                Console.WriteLine("    ИТОГ: медиана расчёт/эталон = {0:F3}, разброс {1:F3}..{2:F3} по {3} точкам",
                                  median, lo, hi, ratios.Count);
            }

            if (!string.IsNullOrEmpty(outPath))
            {
                File.WriteAllLines(outPath, csv);
                Console.WriteLine("    записано: {0}", outPath);
            }
        }

        /// <summary>Эталон LSRM: «энергия кэВ \t эффективность \t погрешность %».</summary>
        static Dictionary<double, double> LoadCurve(string path)
        {
            Dictionary<double, double> map = new Dictionary<double, double>();
            foreach (string line in File.ReadAllLines(path))
            {
                string[] parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                double e, v;
                if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out e)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                    && e > 0.0 && v > 0.0)
                {
                    map[e] = v;
                }
            }

            return map;
        }
    }
}
