using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

/// <summary>
/// Поверка П44 (13.09.2026): тормозное ВДОЛЬ ПУТИ (`M3`, ключ `BremAlongPath`)
/// и электрон в ПРОИЗВОЛЬНОМ ВЕЩЕСТВЕ (`N4`/`F11` (г), ключ `ElectronAnyMaterial`).
///
/// Две части, каждая со своим положительным контролем.
///
/// 1. БАЛАНС ШАГОВ (`--geometry=`, кристалл такой величины, что электрон из
///    центра не вылетает, — NaI Ø200×200 мм): электрон энергии T рождается в
///    центре N раз, зовётся `ElectronLoss` (отражением: метод приватный),
///    считаются кванты тормозного (`CountBremPhotons`) и их энергия
///    (`SumBremKev`) на электрон — при `bpath=0` (точка рождения, толстая
///    мишень) и при `bpath=1/2` (на шагах переноса, тонкая мишень при
///    текущей энергии; ранний выход по ближайшей грани ВЫКЛЮЧЕН рычагом
///    `ElectronTransportNoEarlyExit`, иначе из центра большого кристалла ни
///    один электрон не шагал бы и сверка мерила бы толстую мишень саму с
///    собой). Интеграл шагов по всему пути торможения ОБЯЗАН сойтись с толстой мишенью
///    (`ThickTargetBrem.Photons`/`Radiated`) — иначе тонкие шаги считают не
///    то же самое. Порог: 2 % по числу квантов при N ≥ 100000 (шум Пуассона
///    ≈ 1/√(N·⟨n⟩) ≈ 0.4 % при ⟨n⟩ ≈ 0.5), либо 4 шума, если он больше.
///    Положительный контроль — `--step=1.0`: один шаг на весь пробег, тонкая
///    мишень при энергии середины пути, умноженная на весь пробег, — грубая
///    квадратура, и она ОБЯЗАНА разойтись с толстой мишенью за порог (иначе
///    сверка слепа к качеству интегрирования по шагам).
///
/// 2. ПРОИЗВОЛЬНЫЙ СОСТАВ (`--compositions`): `ElectronData.ForComposition`
///    на веществе геометрии с составом вшитого (LaBr₃, CeBr₃, PTFE, Al, вода)
///    обязан дать ту же таблицу, что `ElectronData.ByName` (расхождение ≤
///    0.1 % — та же плотность, тот же I; путь «массовые доли → атомы формулы»
///    новый); положительный контроль — тот же состав с ДРУГОЙ плотностью
///    (LaBr₃ при 1.0 г/см³ вместо 5.08) обязан РАЗОЙТИСЬ (эффект плотности,
///    &gt; 0.3 % на 1 МэВ). Для веществ, которых нет во вшитых (MgO-отражатель
///    0.8, стекло ториевого диска, воздух), печатаются пробег и выход против
///    воды — числа для журнала, и толстая мишень (`ThickTargetBrem.For`) —
///    квантов и энергия на электрон 1 МэВ, подтяжка к ESTAR.
///
///     brempathprobe --geometry=big_nai.in [--energies=100,300,662,1461,2614]
///                   [--n=100000] [--bpath=1|2] [--seed=20260913] [--compositions]
///
/// Код 0 — все пороги выполнены; 1 — расхождение баланса или состава; 2 — ключи.
/// </summary>
static class BremPathProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        string geometryPath = null;
        int n = 100000;
        int bpath = 1;
        ulong seed = 20260913UL;
        bool compositions = false;
        double stepFraction = -1.0;
        var energies = new List<double> { 100, 300, 661.657, 1461, 2614.511 };
        foreach (string a in args)
        {
            if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--bpath=", StringComparison.Ordinal)) bpath = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = ulong.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--step=", StringComparison.Ordinal)) stepFraction = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a == "--compositions") compositions = true;
            else if (a.StartsWith("--energies=", StringComparison.Ordinal))
            {
                energies.Clear();
                foreach (string part in a.Substring(11).Split(','))
                {
                    energies.Add(double.Parse(part.Trim(), CultureInfo.InvariantCulture));
                }
            }
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }
        }

        int failures = 0;
        if (compositions)
        {
            failures += Compositions();
        }

        if (geometryPath != null)
        {
            if (!File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нет геометрии: " + geometryPath);
                return 2;
            }

            failures += Balance(geometryPath, energies, n, bpath, seed, stepFraction);
        }
        else if (!compositions)
        {
            Console.Error.WriteLine("нужен --geometry= и/или --compositions");
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ИТОГ: все пороги выполнены" : "ИТОГ: ОТКАЗ, нарушений " + failures);
        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 1. Баланс шагов
    // ------------------------------------------------------------------

    static int Balance(string geometryPath, List<double> energies, int n, int bpath, ulong seed,
                       double stepFraction)
    {
        GeometryModel geometry = GeometryModel.Load(geometryPath);
        Console.WriteLine("геометрия: {0}", geometry.Describe());
        MethodInfo loss = typeof(EfficiencySimulator).GetMethod(
            "ElectronLoss", BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(double), typeof(double), typeof(double), typeof(double), typeof(int) }, null);
        if (loss == null)
        {
            Console.Error.WriteLine("⛔ EfficiencySimulator.ElectronLoss(x,y,z,te,depth) не найден — сборка чужая");
            return 1;
        }

        int failures = 0;
        Console.WriteLine();
        Console.WriteLine("баланс тормозного: точка рождения (bpath=0) против шагов (bpath={0}), N={1} электронов на узел{2}",
                          bpath, n, stepFraction > 0.0 ? ", шаг " + stepFraction.ToString("0.###", CultureInfo.InvariantCulture) : "");
        Console.WriteLine("{0,9} {1,10} {2,10} {3,10} {4,10} {5,10} {6,10} {7,8} {8,8}",
                          "T, кэВ", "табл n", "точка n", "шаги n", "табл кэВ", "точка кэВ", "шаги кэВ", "Δn %", "ΔE %");
        foreach (double te in energies)
        {
            double[] point = Run(geometry, te, n, 0, seed, stepFraction, loss);
            double[] path = Run(geometry, te, n, bpath, seed, stepFraction, loss);
            var probe = new EfficiencySimulator(geometry.Clone());
            probe.BremAlongPath = bpath;
            string name = probe.LightYieldName;        // EnsureBuilt
            ThickTargetBrem table = TableOf(probe);
            double tabN = table != null ? table.Photons(te) : double.NaN;
            double tabE = table != null ? table.Radiated(te) : double.NaN;
            double dn = 100.0 * (path[0] / point[0] - 1.0);
            double de = 100.0 * (path[1] / point[1] - 1.0);
            // Порог 2 % по числу квантов: шум двух независимых выборок по
            // √2/√(N·n̄), при N = 100000 и n̄ ≈ 0.3…3 — 0.2…0.6 %.
            double noise = 100.0 * Math.Sqrt(2.0 / Math.Max(1.0, n * point[0]));
            bool ok = Math.Abs(dn) <= Math.Max(2.0, 4.0 * noise);
            if (!ok)
            {
                failures++;
            }

            Console.WriteLine("{0,9:0.###} {1,10:0.0000} {2,10:0.0000} {3,10:0.0000} {4,10:0.00} {5,10:0.00} {6,10:0.00} {7,8:+0.00;-0.00} {8,8:+0.00;-0.00}{9}",
                              te, tabN, point[0], path[0], tabE, point[1], path[1], dn, de,
                              ok ? "" : "   ⛔ шаги разошлись с толстой мишенью (порог " + Math.Max(2.0, 4.0 * noise).ToString("0.0", CultureInfo.InvariantCulture) + " %)");
        }

        return failures;
    }

    /// <summary>Квантов и кэВ тормозного на электрон энергии te из центра кристалла.</summary>
    static double[] Run(GeometryModel geometry, double te, int n, int bpath, ulong seed,
                        double stepFraction, MethodInfo loss)
    {
        var sim = new EfficiencySimulator(geometry.Clone());
        sim.BremAlongPath = bpath;
        // Без раннего выхода: в кристалле 20×20 см из центра он срабатывал бы
        // у каждого электрона, и шаги не делались бы вовсе (контроль
        // `--step=1.0` это и показал — не разошёлся).
        sim.ElectronTransportNoEarlyExit = true;
        if (stepFraction > 0.0)
        {
            sim.ElectronStepFraction = stepFraction;
        }

        string name = sim.LightYieldName;              // EnsureBuilt: сцена и таблицы
        sim.ResetStream(seed);
        double zc = CrystalCentreZ(geometry);
        object[] arg = { 0.0, 0.0, zc, te, 0 };
        sim.CountBremPhotons = 0;
        sim.SumBremKev = 0.0;
        for (int i = 0; i < n; i++)
        {
            loss.Invoke(sim, arg);
        }

        return new[] { sim.CountBremPhotons / (double)n, sim.SumBremKev / n };
    }

    static double CrystalCentreZ(GeometryModel geometry)
    {
        // Сцена: кристалл от z = 0 до высоты (см. DumpScene), центр — половина
        // высоты в сантиметрах; модель хранит миллиметры.
        return 0.05 * geometry.CrystalHeight;
    }

    static ThickTargetBrem TableOf(EfficiencySimulator sim)
    {
        FieldInfo f = typeof(EfficiencySimulator).GetField("bremTable", BindingFlags.NonPublic | BindingFlags.Instance);
        return f == null ? null : (ThickTargetBrem)f.GetValue(sim);
    }

    // ------------------------------------------------------------------
    // 2. Произвольный состав
    // ------------------------------------------------------------------

    static GeometryMaterial Make(string name, double density, int[] z, double[] massFractions)
    {
        var m = new GeometryMaterial { Name = name, Density = density };
        for (int i = 0; i < z.Length; i++)
        {
            m.Fractions[z[i]] = massFractions[i];
        }

        return m;
    }

    static int Compositions()
    {
        int failures = 0;
        Console.WriteLine("произвольный состав (`N4`): ForComposition против ByName на вшитых");
        Dictionary<int, double> mass = MaterialDatabase.AtomicMass;
        string[] names = { "LaBr3", "CeBr3", "PTFE", "Al", "Water", "NaI", "CsI" };
        foreach (string name in names)
        {
            EstarCalculator.Compound c = ElectronData.CompoundByName(name);
            ElectronData.Material builtin = ElectronData.ByName(name);
            // массовые доли из формулы — тем же путём, что ESTAR (ATB)
            double total = 0.0;
            for (int i = 0; i < c.Z.Length; i++) total += c.Atoms[i] * mass[c.Z[i]];
            double[] w = new double[c.Z.Length];
            for (int i = 0; i < c.Z.Length; i++) w[i] = c.Atoms[i] * mass[c.Z[i]] / total;
            ElectronData.Material computed = ElectronData.ForComposition(Make(name, c.DensityGCm3, c.Z, w));
            double worstR = 0.0, worstY = 0.0;
            for (int i = 0; i < builtin.Energy.Length; i++)
            {
                worstR = Math.Max(worstR, Math.Abs(computed.Range[i] / builtin.Range[i] - 1.0));
                worstY = Math.Max(worstY, Math.Abs(computed.Yield[i] / builtin.Yield[i] - 1.0));
            }

            bool ok = worstR <= 1e-3 && worstY <= 1e-3;
            if (!ok) failures++;
            Console.WriteLine("  {0,-6} пробег худший узел {1,8:0.0000} %, выход {2,8:0.0000} %{3}",
                              name, 100 * worstR, 100 * worstY, ok ? "" : "   ⛔ разошлось с ByName (порог 0.1 %)");
        }

        // Положительный контроль: та же формула LaBr3, плотность 1.0 — эффект
        // плотности обязан сдвинуть пробег на 1 МэВ заметно.
        {
            EstarCalculator.Compound c = ElectronData.CompoundByName("LaBr3");
            double total = 0.0;
            for (int i = 0; i < c.Z.Length; i++) total += c.Atoms[i] * mass[c.Z[i]];
            double[] w = new double[c.Z.Length];
            for (int i = 0; i < c.Z.Length; i++) w[i] = c.Atoms[i] * mass[c.Z[i]] / total;
            ElectronData.Material light = ElectronData.ForComposition(Make("LaBr3 rho=1", 1.0, c.Z, w));
            ElectronData.Material builtin = ElectronData.ByName("LaBr3");
            double d = ElectronData.RangeOf(light, 1000.0) / ElectronData.RangeOf(builtin, 1000.0) - 1.0;
            bool seen = Math.Abs(d) > 3e-3;
            if (!seen) failures++;
            Console.WriteLine("  положительный контроль: LaBr3 при 1.0 г/см³ против 5.08 — пробег 1 МэВ {0:+0.00;-0.00} % {1}",
                              100 * d, seen ? "(разошлось, контроль видим)" : "⛔ НЕ разошлось — контроль слеп");
        }

        // Правило NIST для смесей: I по Брэггу, у элементов тяжелее неона —
        // 1.13·I_элемента (ESTAR.f:714-734); LaBr₃ так и считан самим ESTAR
        // (454.5 эВ). Разница с «чистым» Брэггом (без 1.13) — числом, чтобы
        // назвать цену правила, а не спорить о нём.
        foreach (string name in new[] { "LaBr3", "CeBr3" })
        {
            EstarCalculator.Compound c = ElectronData.CompoundByName(name);
            double[] grid = { 0.01, 0.1, 1.0, 2.6 };
            EstarCalculator.Result nist = EstarCalculator.Compute(c, grid);
            EstarCalculator.Compound plain = new EstarCalculator.Compound
            {
                Name = c.Name + " I/1.13", Z = c.Z, Atoms = c.Atoms, DensityGCm3 = c.DensityGCm3,
                PotentialEv = nist.PotentialEv / 1.13,
            };
            EstarCalculator.Result other = EstarCalculator.Compute(plain, grid);
            Console.WriteLine("  {0}: I по правилу NIST (Брэгг, 1.13 у Z>10) = {1:0.0} эВ; без 1.13 — {2:0.0} эВ: пробег 10/100/1000/2600 кэВ {3:+0.00;-0.00} / {4:+0.00;-0.00} / {5:+0.00;-0.00} / {6:+0.00;-0.00} %, выход 1 МэВ {7:+0.00;-0.00} %",
                              name, nist.PotentialEv, plain.PotentialEv.Value,
                              100 * (other.RangeGCm2[0] / nist.RangeGCm2[0] - 1), 100 * (other.RangeGCm2[1] / nist.RangeGCm2[1] - 1),
                              100 * (other.RangeGCm2[2] / nist.RangeGCm2[2] - 1), 100 * (other.RangeGCm2[3] / nist.RangeGCm2[3] - 1),
                              100 * (other.Yield[2] / nist.Yield[2] - 1));
        }

        Console.WriteLine();
        Console.WriteLine("вещества сцен, которых нет во вшитых (без ключа считались ВОДОЙ):");
        ElectronData.Material water = ElectronData.ByName("Water");
        var extra = new List<GeometryMaterial>
        {
            Make("MgO отражатель 0.8", 0.8, new[] { 8, 12 }, new[] { 0.396964, 0.603036 }),
            Make("воздух 0.001205", 0.001205, new[] { 7, 8 }, new[] { 0.636483, 0.363517 }),
            Make("стекло ториевого диска 4.345", 4.345, new[] { 5, 8, 20, 56, 57, 90 },
                 new[] { 0.05404, 0.232722, 0.042882, 0.271383, 0.258363, 0.14061 }),
            Make("полиэтилен 0.94", 0.94, new[] { 1, 6 }, new[] { 0.143716, 0.856284 }),
        };
        Console.WriteLine("{0,-30} {1,12} {2,12} {3,10} {4,10} {5,10} {6,10}",
                          "вещество", "R(1МэВ) г/см²", "R/R(вода)", "Y(1МэВ)", "Y/Y(вода)", "квантов", "кэВ");
        foreach (GeometryMaterial m in extra)
        {
            ElectronData.Material e = ElectronData.ForComposition(m);
            if (e == null)
            {
                failures++;
                Console.WriteLine("  {0,-28} ⛔ ForComposition вернула null", m.Name);
                continue;
            }

            double r = ElectronData.RangeOf(e, 1000.0), y = ElectronData.YieldOf(e, 1000.0);
            ThickTargetBrem t = ThickTargetBrem.For(m, e, 5.0);
            Console.WriteLine("  {0,-28} {1,12:0.0000} {2,12:0.000} {3,10:0.00000} {4,10:0.000} {5,10:0.000} {6,10:0.0}{7}",
                              m.Name, r, r / ElectronData.RangeOf(water, 1000.0), y,
                              y / ElectronData.YieldOf(water, 1000.0),
                              t != null ? t.Photons(1000.0) : double.NaN,
                              t != null ? t.Radiated(1000.0) : double.NaN,
                              t != null ? "  подтяжка " + t.Anchor(1000.0).ToString("0.000", CultureInfo.InvariantCulture) : "  ⛔ спектра тормозного нет");
        }

        Console.WriteLine();
        return failures;
    }
}
