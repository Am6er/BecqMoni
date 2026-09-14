using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MarinelliSelfAbsProbeE10
{
    /// <summary>
    /// `E10`: сверка САМОПОГЛОЩЕНИЯ нашей маринелли-модели с опубликованными
    /// факторами (полоса П36, 12.09.2026).
    ///
    /// Что считается. Для стакана Маринелли вокруг NaI 3″×3″ (76.2×76.2 мм) —
    /// пиковая эффективность ε(E, ρ) при заполнении SiO₂ плотности ρ = 0…2.8
    /// г/см³ (и водой ρ = 1) и тот же стакан С ВОЗДУХОМ, ε(E, воздух). Фактор
    /// самопоглощения относительно воздуха в соглашении статей (Jodłowski 2006,
    /// Çetinkaya 2025):
    ///
    ///     Cs^a(E, ρ) = ε(E, воздух) / ε(E, ρ)   (≥ 1, множитель к активности),
    ///
    /// наше F = ε(ρ)/ε(воздух) = 1/Cs^a.
    ///
    /// Внешняя мера. Статья Çetinkaya 2025 (RPC 230:112560, FLUKA, маринелли
    /// 0.5/1 л) — за стеной: аннотация и выдержки есть, таблица 4 и формула —
    /// нет. Открытая замена — Jodłowski, Nukleonika 2006;51(S2):S21–S25: метод
    /// Дебертина (точечный детектор), маринелли 710 мл (внешний Ø 125 мм, слой
    /// пробы 19 мм, сосуд алюминиевый со стенкой 1 мм — Jodłowski 2010), матрица
    /// SiO₂, E = 150…2600 кэВ, ρ = 0.0013…2.4; подгонка формулой Боливара:
    ///
    ///     Cs^a(E, ρ) = exp[0.32017 · exp(−0.033624 · (ln E)²) · ρ]   (ур. 11),
    ///
    /// точность подгонки к его расчёту 0.5 %, метода — 1–2 %, положение
    /// точечного детектора — ≤ 1 %.
    ///
    /// Три плеча на каждую сцену:
    ///   МК    — наш перенос (`EfficiencySimulator`), полная физика умолчанием;
    ///   ДЕБ   — интеграл Дебертина по ТОЙ ЖЕ сцене нашими же μ (matdb/XCOM),
    ///           точечный детектор в центре кристалла; считается здесь же,
    ///           дважды: μ полное и μ без когерентного;
    ///   ФОРМ  — формула Jodłowski (только сцена J — его сосуд; у сцен 0.5/1 л
    ///           внешних чисел нет, они стоят «на будущее» под таблицу 4).
    ///
    /// Размеры сосудов 0.5 и 1 л — ДОПУЩЕНИЕ (в статье за стеной): полипропилен
    /// 1.5 мм, колодец Ø 84 мм под корпус 3″; 0.5 л: Ø 120, колодец 60 мм, верх
    /// 19 мм; 1 л: Ø 140, колодец 75 мм, верх 22 мм. Сосуд J восстановлен по
    /// объёму 710 мл при заданных Ø 125 / слой 19 / верх 19 (= слой) → колодец
    /// 77 мм.
    ///
    ///   MarinelliSelfAbsProbeE10 [--out=&lt;каталог&gt;] [--n=200000] [--threads=8]
    ///        [--scenes=J,C05,C1] [--energies=238.6,...] [--rhos=0.4,...]
    ///        [--quick]   — две точки (первые две единицы работы, `A77`)
    ///        [--crystal=10] [--no-scatter] [--no-cohpass] [--tag=имя] — разборка
    ///        причины: кристалл меньше ставится на ТУ ЖЕ глубину центра (зазор
    ///        добирает разницу) ≈ точечный детектор; выключатели физики — знак
    ///        вклада возврата рассеянного в пик и когерентного «насквозь».
    ///
    /// Код возврата: 0 — посчитано и оба положительных контроля прошли
    /// (ρ→0 даёт F = 1.000 ± 0.5 %; вода на 238.6 кэВ даёт F ≤ 0.95);
    /// 1 — контроль провален или ошибка.
    /// </summary>
    static class Program
    {
        sealed class Scene
        {
            public string Id;
            public string Title;
            public double BeakerDiameter, SideWall, HoleDiameter, HoleWall, HoleEndWall, HoleHeight, TopLayer;
            public string WallMaterial;
            public double WallDensity;
            /// <summary>Глубина точечного детектора под нижней поверхностью пробы, мм (для ДЕБ).</summary>
            public double PointDepth;
            /// <summary>Есть ли внешняя формула (Jodłowski) для этой сцены.</summary>
            public bool HasFormula;
        }

        sealed class Job
        {
            public Scene Scene;
            public double EnergyKev;
            public double Rho;
            public string Material;      // "SiO2" | "H2O" | "Air"
            public double Eps, ErrPct;
        }

        static int Main(string[] args)
        {
            // ⛔ (`T247`) Культура ЦЕЛИКОМ инвариантная, приказ Amber 05.09.2026:
            //    печать и разбор чисел точкой.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        static int Run(string[] args)
        {
            string outDir = null;
            int histories = 200000;
            int threads = Math.Max(1, Environment.ProcessorCount - 2);
            string sceneList = "J,C05,C1";
            double[] energies = { 238.6, 338.3, 583.2, 911.2, 1460.8, 2614.5 };
            double[] rhos = { 0.02, 0.4, 0.8, 1.2, 1.6, 2.0, 2.4, 2.8 };
            bool quick = false;
            double crystalMm = 76.2;
            bool singleScatter = true, coherentPass = true;
            string tag = "";
            foreach (string arg in args)
            {
                int eq = arg.IndexOf('=');
                string key = eq > 0 ? arg.Substring(0, eq) : arg;
                string value = eq > 0 ? arg.Substring(eq + 1) : "";
                switch (key)
                {
                    case "--out": outDir = value; break;
                    case "--n": histories = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--threads": threads = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--scenes": sceneList = value; break;
                    case "--energies": energies = ParseList(value); break;
                    case "--rhos": rhos = ParseList(value); break;
                    case "--quick": quick = true; break;
                    // Разборка причины (§5 журнала): маленький кристалл на той же
                    // глубине центра ≈ точечный детектор Дебертина; выключатели
                    // физики — знак вклада возврата рассеянного и когерентного.
                    case "--crystal": crystalMm = double.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--no-scatter": singleScatter = false; break;
                    case "--no-cohpass": coherentPass = false; break;
                    case "--tag": tag = value; break;
                    default:
                        Console.Error.WriteLine("неизвестный ключ: " + arg);
                        return 1;
                }
            }

            if (outDir == null)
            {
                outDir = Path.Combine(Environment.CurrentDirectory, "p36-e10-out");
            }

            Directory.CreateDirectory(outDir);

            List<Scene> scenes = new List<Scene>();
            foreach (string id in sceneList.Split(','))
            {
                scenes.Add(MakeScene(id.Trim()));
            }

            if (quick)
            {
                // Первые две единицы работы (`A77`): одна энергия, воздух и ρ = 1.6.
                energies = new[] { 238.6 };
                rhos = new[] { 1.6 };
                scenes = new List<Scene> { scenes[0] };
            }

            Console.WriteLine("E10: самопоглощение маринелли, NaI {2}x{2} мм, историй {0}, потоков {1}, возврат рассеянного {3}, когерентное сквозь {4}",
                              histories, threads, crystalMm, singleScatter ? "да" : "нет", coherentPass ? "да" : "нет");

            // Задания: на каждую сцену — воздух, вода ρ=1 и SiO₂ по списку ρ, на каждую энергию.
            List<Job> jobs = new List<Job>();
            foreach (Scene sc in scenes)
            {
                foreach (double e in energies)
                {
                    jobs.Add(new Job { Scene = sc, EnergyKev = e, Rho = 0.001205, Material = "Air" });
                    jobs.Add(new Job { Scene = sc, EnergyKev = e, Rho = 1.0, Material = "H2O" });
                    foreach (double rho in rhos)
                    {
                        jobs.Add(new Job { Scene = sc, EnergyKev = e, Rho = rho, Material = "SiO2" });
                    }
                }
            }

            Console.WriteLine("заданий: {0}", jobs.Count);
            DateTime t0 = DateTime.Now;
            int done = 0;
            object gate = new object();
            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = threads };
            Parallel.ForEach(jobs, po, job =>
            {
                GeometryModel g = Build(job.Scene, job.Material, job.Rho, crystalMm);
                EfficiencySimulator sim = new EfficiencySimulator(g)
                {
                    Histories = histories,
                    PeakHalfWidthKev = g.PeakHalfWidthKev(job.EnergyKev),
                    SingleScatter = singleScatter,
                    CoherentPassesThrough = coherentPass,
                };
                double err;
                job.Eps = sim.Efficiency(job.EnergyKev, out err);
                job.ErrPct = err;
                lock (gate)
                {
                    done++;
                    if (done <= 2 || done % 20 == 0 || done == jobs.Count)
                    {
                        Console.WriteLine("  [{0}/{1}] {2} E={3} {4} rho={5}: eps={6:E4} ±{7:F2} %  ({8:F0} с)",
                                          done, jobs.Count, job.Scene.Id, job.EnergyKev, job.Material,
                                          job.Rho, job.Eps, job.ErrPct, (DateTime.Now - t0).TotalSeconds);
                    }
                }
            });

            Console.WriteLine("счёт занял {0:F0} с", (DateTime.Now - t0).TotalSeconds);

            // Сводка по сценам.
            bool ok = true;
            List<string> csv = new List<string>
            {
                "scene,E_keV,material,rho,eps,err_pct,eps_air,err_air_pct,F_mc,Cs_mc,Cs_mc_err_pct,Cs_deb_total,Cs_deb_nocoh,Cs_formula,d_mc_deb_total_pct,d_mc_formula_pct,mu_rho_total,mu_rho_nocoh"
            };
            foreach (Scene sc in scenes)
            {
                Console.WriteLine();
                Console.WriteLine("=== сцена {0}: {1}", sc.Id, sc.Title);
                double depthUsed = sc.PointDepth > 0.0 ? sc.PointDepth : CrystalCentreDepth + sc.HoleEndWall;
                Console.WriteLine("    объём пробы {0:F1} мл; точечный детектор ДЕБ на {1:F1} мм под нижней поверхностью пробы",
                                  SampleVolumeMl(sc), depthUsed);
                {
                    // Чувствительность интеграла Дебертина к глубине точки — факт для журнала.
                    double muT, muN;
                    MassAttenuation("SiO2", 238.6, out muT, out muN);
                    double a = Debertin(sc, muT * 2.4, 39.1), b = Debertin(sc, muT * 2.4, CrystalCentreDepth + sc.HoleEndWall);
                    Console.WriteLine("    ДЕБ на 238.6 кэВ, ρ = 2.4: глубина 39.1 мм → Cs {0:F4}; глубина {1:F1} мм → Cs {2:F4} (разница {3:F2} %)",
                                      a, CrystalCentreDepth + sc.HoleEndWall, b, (b / a - 1.0) * 100.0);
                }
                Console.WriteLine("    {0,7} {1,5} {2,5} | {3,10} {4,6} | {5,7} {6,7} {7,7} {8,7} | {9,7} {10,7}",
                                  "E,кэВ", "вещ.", "ρ", "ε", "±%", "Cs_МК", "Cs_ДЕБ", "ДЕБ_нк", "Cs_ФОРМ", "МК−ДЕБ%", "МК−ФОРМ%");
                foreach (double e in energies)
                {
                    Job air = jobs.Find(j => j.Scene == sc && j.EnergyKev == e && j.Material == "Air");
                    foreach (Job job in jobs)
                    {
                        if (job.Scene != sc || job.EnergyKev != e || job.Material == "Air")
                        {
                            continue;
                        }

                        double f = job.Eps / air.Eps;
                        double cs = 1.0 / f;
                        double csErr = Math.Sqrt(job.ErrPct * job.ErrPct + air.ErrPct * air.ErrPct);
                        double muT, muN;
                        MassAttenuation(job.Material, e, out muT, out muN);
                        double debT = Debertin(sc, muT * job.Rho, sc.PointDepth);
                        double debN = Debertin(sc, muN * job.Rho, sc.PointDepth);
                        double form = sc.HasFormula && job.Material == "SiO2" ? Jodlowski(e, job.Rho) : double.NaN;
                        double dDeb = (cs / debT - 1.0) * 100.0;
                        double dForm = double.IsNaN(form) ? double.NaN : (cs / form - 1.0) * 100.0;
                        Console.WriteLine("    {0,7:F1} {1,5} {2,5:F2} | {3,10:E3} {4,6:F2} | {5,7:F4} {6,7:F4} {7,7:F4} {8,7} | {9,7:F2} {10,7}",
                                          e, job.Material, job.Rho, job.Eps, job.ErrPct, cs, debT, debN,
                                          double.IsNaN(form) ? "-" : form.ToString("F4", CultureInfo.InvariantCulture),
                                          dDeb,
                                          double.IsNaN(dForm) ? "-" : dForm.ToString("F2", CultureInfo.InvariantCulture));
                        csv.Add(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3},{4:E6},{5:F3},{6:E6},{7:F3},{8:F5},{9:F5},{10:F3},{11:F5},{12:F5},{13},{14:F3},{15},{16:F6},{17:F6}",
                            sc.Id, e, job.Material, job.Rho, job.Eps, job.ErrPct, air.Eps, air.ErrPct, f, cs, csErr,
                            debT, debN, double.IsNaN(form) ? "" : form.ToString("F5", CultureInfo.InvariantCulture),
                            dDeb, double.IsNaN(dForm) ? "" : dForm.ToString("F3", CultureInfo.InvariantCulture),
                            muT, muN));

                        // Положительные контроли.
                        if (job.Material == "SiO2" && job.Rho <= 0.05)
                        {
                            bool pass = Math.Abs(f - 1.0) <= 0.005 + 0.01 * csErr;
                            Console.WriteLine("      контроль ρ→0: F = {0:F4} (ожидание 1.000 ± 0.5 %) — {1}", f, pass ? "ДА" : "НЕТ");
                            ok &= pass;
                        }

                        if (job.Material == "H2O" && Math.Abs(e - 238.6) < 0.5)
                        {
                            bool pass = f <= 0.95;
                            Console.WriteLine("      контроль воды на 238.6: F = {0:F4} (ожидание ≤ 0.95) — {1}", f, pass ? "ДА" : "НЕТ");
                            ok &= pass;
                        }
                    }
                }
            }

            string csvPath = Path.Combine(outDir, tag == "" ? "selfabs.csv" : "selfabs_" + tag + ".csv");
            File.WriteAllLines(csvPath, csv, new UTF8Encoding(false));
            Console.WriteLine();
            Console.WriteLine("записано: {0}", csvPath);
            Console.WriteLine(ok ? "КОНТРОЛИ: ДА" : "КОНТРОЛИ: НЕТ");
            return ok ? 0 : 1;
        }

        static double[] ParseList(string value)
        {
            string[] parts = value.Split(',');
            double[] list = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                list[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
            }

            return list;
        }

        // ------------------------------------------------------------------
        // Сцены
        // ------------------------------------------------------------------

        static Scene MakeScene(string id)
        {
            switch (id)
            {
                case "J":
                    // Jodłowski 2006/2010: 710 мл, внешний Ø 125, алюминий 1 мм,
                    // слой пробы 19 мм → колодец Ø 83 (проба внутри r = 42.5);
                    // верх 19 мм (= слой, «20 мм слой вокруг детектора»);
                    // колодец 77 мм — из объёма. Точечный детектор его HPGe —
                    // 39.1 мм под нижней поверхностью пробы.
                    return new Scene
                    {
                        Id = "J", Title = "Jodłowski 710 мл, Al 1 мм, слой 19 мм",
                        BeakerDiameter = 125.0, SideWall = 1.0, HoleDiameter = 83.0, HoleWall = 1.0,
                        HoleEndWall = 1.0, HoleHeight = 77.0, TopLayer = 19.0,
                        WallMaterial = "Aluminum", WallDensity = 2.7,
                        PointDepth = 39.1, HasFormula = true,
                    };
                case "C05":
                    return new Scene
                    {
                        Id = "C05", Title = "маринелли 0.5 л (допущение): Ø 120, PP 1.5, колодец Ø 84×60, верх 19",
                        BeakerDiameter = 120.0, SideWall = 1.5, HoleDiameter = 84.0, HoleWall = 1.5,
                        HoleEndWall = 1.5, HoleHeight = 60.0, TopLayer = 19.0,
                        WallMaterial = "Polypropylene", WallDensity = 0.905,
                        PointDepth = 0.0, HasFormula = false,
                    };
                case "C1":
                    return new Scene
                    {
                        Id = "C1", Title = "маринелли 1 л (допущение): Ø 140, PP 1.5, колодец Ø 84×75, верх 22",
                        BeakerDiameter = 140.0, SideWall = 1.5, HoleDiameter = 84.0, HoleWall = 1.5,
                        HoleEndWall = 1.5, HoleHeight = 75.0, TopLayer = 22.0,
                        WallMaterial = "Polypropylene", WallDensity = 0.905,
                        PointDepth = 0.0, HasFormula = false,
                    };
                default:
                    throw new ArgumentException("неизвестная сцена: " + id);
            }
        }

        /// <summary>Глубина центра кристалла NaI 3×3 под нижней поверхностью пробы при нашей обвязке, мм.</summary>
        const double CrystalCentreDepth = 38.1 + 2.0 + 0.5;   // полкристалла + отражатель + корпус (расстояние сосуда 0)

        static GeometryModel Build(Scene sc, string material, double rho, double crystalMm)
        {
            // Кристалл меньше 76.2 мм ставится НА ТУ ЖЕ ГЛУБИНУ ЦЕНТРА: зазор
            // перед ним добирает разницу, и точка Дебертина остаётся той же.
            double gap = Math.Max(0.0, 0.5 * (76.2 - crystalMm));
            GeometryModel g = new GeometryModel
            {
                Name = "e10_" + sc.Id,
                IsScintillator = true,
                Shape = CrystalShape.Cylinder,
                CrystalDiameter = crystalMm,
                CrystalHeight = crystalMm,
                FrontReflectorThickness = 2.0,
                SideReflectorThickness = 2.0,
                FrontCladdingThickness = 0.5,
                SideCladdingThickness = 0.5,
                MountingThickness = 2.0,
                FrontGapThickness = gap,
                SideGapThickness = 0.0,
                FwhmAt662Percent = 7.0,
                SourceType = GeometrySourceType.Marinelli,
                MarinelliBeakerDiameter = sc.BeakerDiameter,
                MarinelliSideThickness = sc.SideWall,
                MarinelliHoleDiameter = sc.HoleDiameter,
                MarinelliHoleSideThickness = sc.HoleWall,
                MarinelliHoleEndWallThickness = sc.HoleEndWall,
                MarinelliEndWallThickness = sc.SideWall,
                MarinelliHoleHeight = sc.HoleHeight,
                MarinelliSourceHeight = sc.TopLayer + sc.HoleEndWall + sc.HoleHeight,
                MarinelliBeakerHeight = sc.TopLayer + sc.HoleEndWall + sc.HoleHeight + sc.SideWall,
                MarinelliToDetectorDistance = 0.0,
            };
            g.Crystal = Material("Sodium iodide", 0.0);
            g.Reflector = Material("Magnesium oxide", 0.8);
            g.Cladding = Material("Aluminum", 0.0);
            g.Gap = Material("Air, dry", 0.0);
            g.BeakerWall = Material(sc.WallMaterial, sc.WallDensity);
            switch (material)
            {
                case "Air": g.Source = Material("Air, dry", 0.001205); break;
                case "H2O": g.Source = Material("Water, liquid", rho); break;
                default: g.Source = Material("Silicon dioxide", rho); break;
            }

            return g;
        }

        static GeometryMaterial Material(string name, double density)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            if (entry == null)
            {
                throw new InvalidOperationException("в библиотеке веществ нет: " + name);
            }

            return GeometryMaterialLibrary.Make(entry, density > 0.0 ? density : entry.Density);
        }

        /// <summary>Объём пробы сцены, мл — по тем же формулам, что строит сцену симулятор.</summary>
        static double SampleVolumeMl(Scene sc)
        {
            double rOut = 0.5 * sc.BeakerDiameter - sc.SideWall;
            double rIn = 0.5 * sc.HoleDiameter + sc.HoleWall;
            double hs = sc.TopLayer + sc.HoleEndWall + sc.HoleHeight;
            double annulus = Math.PI * (rOut * rOut - rIn * rIn) * hs;
            double disc = Math.PI * rIn * rIn * sc.TopLayer;
            return (annulus + disc) / 1000.0;
        }

        // ------------------------------------------------------------------
        // Внешние меры
        // ------------------------------------------------------------------

        /// <summary>Jodłowski 2006, ур. (11): Cs^a для маринелли 710 мл, SiO₂; E в кэВ, ρ в г/см³.</summary>
        static double Jodlowski(double energyKev, double rho)
        {
            double l = Math.Log(energyKev);
            return Math.Exp(0.32017 * Math.Exp(-0.033624 * l * l) * rho);
        }

        /// <summary>Массовые коэффициенты ослабления вещества пробы, см²/г: полный и без когерентного.</summary>
        static void MassAttenuation(string material, double energyKev, out double total, out double noCoherent)
        {
            // Массовые доли: SiO₂ — Si 0.46743, O 0.53257; вода — H 0.11190, O 0.88810.
            int[] z; double[] w;
            if (material == "H2O")
            {
                z = new[] { 1, 8 }; w = new[] { 0.11190, 0.88810 };
            }
            else
            {
                z = new[] { 14, 8 }; w = new[] { 0.46743, 0.53257 };
            }

            total = 0.0; noCoherent = 0.0;
            for (int i = 0; i < z.Length; i++)
            {
                double t = AttenuationData.MassAttenuation(z[i], energyKev);
                double c = PartialCrossSections.MassCrossSection(z[i], energyKev, PhotonProcess.Coherent);
                total += w[i] * t;
                noCoherent += w[i] * (t - c);
            }
        }

        /// <summary>
        /// Интеграл Дебертина (Debertin, Ren 1989; Jodłowski 2006 ур. 7–9) для
        /// маринелли: точечный детектор на оси на глубине <paramref name="pointDepthMm"/>
        /// под нижней поверхностью пробы (ноль — центр нашего кристалла),
        /// вес элемента объёма exp(−μ·z_a)/d², z_a — путь В ПРОБЕ до детектора
        /// (стенки и полость исключены; их ослабление в ОТНОШЕНИИ сокращается).
        /// Возвращает Cs^a = I(0)/I(μ). μ — линейный, 1/см; размеры сцены в мм.
        /// </summary>
        static double Debertin(Scene sc, double muPerCm, double pointDepthMm)
        {
            double depth = pointDepthMm > 0.0 ? pointDepthMm : CrystalCentreDepth + sc.HoleEndWall;
            double rOut = 0.5 * sc.BeakerDiameter - sc.SideWall;      // проба снаружи
            double rIn = 0.5 * sc.HoleDiameter + sc.HoleWall;         // проба внутри (граница колодца со стенкой)
            double hs = sc.TopLayer + sc.HoleEndWall + sc.HoleHeight;
            // Ось z: 0 — верх пробы (дальний от детектора край), растёт к детектору.
            // Проба: кольцо r∈[rIn,rOut], z∈[0,hs]; диск r<rIn, z∈[0,TopLayer].
            // Полость (со стенками): r<rIn, z∈[TopLayer, hs]. Нижняя поверхность
            // пробы над колодцем — z = TopLayer; детектор в точке (0, TopLayer + depth).
            double zP = sc.TopLayer + depth;
            double zCav0 = sc.TopLayer, zCav1 = hs;
            double mu = muPerCm / 10.0;                                 // 1/мм
            double i0 = 0.0, iMu = 0.0;
            const int Nr = 240, Nz = 240;
            // Кольцо.
            Integrate(rIn, rOut, 0.0, hs, Nr, Nz, zP, rIn, zCav0, zCav1, mu, ref i0, ref iMu);
            // Диск сверху.
            Integrate(0.0, rIn, 0.0, sc.TopLayer, Nr, Math.Max(24, Nz / 4), zP, rIn, zCav0, zCav1, mu, ref i0, ref iMu);
            return i0 / iMu;
        }

        static void Integrate(double r0, double r1, double z0, double z1, int nr, int nz,
                              double zP, double rIn, double zCav0, double zCav1, double mu,
                              ref double i0, ref double iMu)
        {
            double dr = (r1 - r0) / nr, dz = (z1 - z0) / nz;
            for (int ir = 0; ir < nr; ir++)
            {
                double r = r0 + (ir + 0.5) * dr;
                for (int iz = 0; iz < nz; iz++)
                {
                    double z = z0 + (iz + 0.5) * dz;
                    double dzp = zP - z;
                    double d2 = r * r + dzp * dzp;
                    double len = Math.Sqrt(d2);
                    // Отрезок (r,z)→(0,zP): r(t) = r(1−t), z(t) = z + dzp·t, t∈[0,1].
                    // Внутри полости: r(t) < rIn ⇔ t > 1 − rIn/r; z(t)∈[zCav0,zCav1].
                    double tA = r > rIn ? 1.0 - rIn / r : 0.0;
                    double tz0, tz1;
                    if (Math.Abs(dzp) < 1e-12)
                    {
                        bool inside = z >= zCav0 && z <= zCav1;
                        tz0 = inside ? 0.0 : 1.0; tz1 = inside ? 1.0 : 0.0;
                    }
                    else
                    {
                        double ta = (zCav0 - z) / dzp, tb = (zCav1 - z) / dzp;
                        tz0 = Math.Min(ta, tb); tz1 = Math.Max(ta, tb);
                    }

                    double lo = Math.Max(Math.Max(tA, tz0), 0.0);
                    double hi = Math.Min(tz1, 1.0);
                    double inCavity = hi > lo ? (hi - lo) * len : 0.0;
                    double za = Math.Max(0.0, len - inCavity);
                    double w = 2.0 * Math.PI * r * dr * dz / d2;
                    i0 += w;
                    iMu += w * Math.Exp(-mu * za);
                }
            }
        }
    }
}
