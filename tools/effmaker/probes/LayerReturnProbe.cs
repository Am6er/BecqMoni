using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

/// <summary>
/// Поверка П94 (17.09.2026, `AMBER44`): ОБРАТНОЕ РАССЕЯНИЕ ЭЛЕКТРОНА В СЛОЯХ
/// ОБВЯЗКИ переносом `TransportInLayers` (ключ `ElectronLayerTransport`) —
/// против табличных коэффициентов Табаты (1971, толстая мишень, нормальное
/// падение; числа П55 §2.3): PTFE η(100) 0.10, η(480) 0.08, η(1000) 0.06,
/// η(2000) 0.04; Al 0.16 / 0.14 / 0.11 / 0.08.
///
/// Электрон энергии T ставится ВПЛОТНУЮ к грани кристалла снаружи (сдвиг 1e-7,
/// как у выхода из кристалла в `EscapeOrReturn`) и пускается ОТ кристалла под
/// углом θ к нормали грани (0° — нормальное падение на слой); зовётся
/// приватный `TransportInLayers` (отражением). Считаются: доля вернувшихся в
/// кристалл (это и есть η слоя для данной геометрии слоёв — у RC103 это
/// PTFE 1 мм + Al 1 мм + пустота, то есть при T ≲ 500 кэВ толстая мишень
/// PTFE, выше — составная), средняя энергия возврата в долях T и средний
/// косинус угла возврата к нормали. Вторая половина (`--crystal`): электрон
/// пускается ВНУТРЬ кристалла с той же грани, зовётся `ElectronLoss`
/// (перенос по кристаллу) при ключе ВЫКЛ — доля унесённой энергии и (по
/// счётчику `CountLayerEscapes` при ключе ВКЛ на геометрии без обвязки)
/// доля вылетевших: обратное рассеяние от CsI/NaI (Табата Z≈50: η ≈ 0.45…0.5).
///
///     layerreturnprobe --geometry=RC103_point0_p55.in [--face=front|side]
///                      [--energies=50,100,200,300,500,1000,2000] [--angles=0,45,70]
///                      [--n=200000] [--seed=20260917] [--crystal] [--x0=<см>] [--z0=<см>]
///
/// Мерка П94 (RC103 П55, PTFE 1 мм + Al 1 мм + пустота, нормальное падение): η =
/// 0.062 (100 кэВ) / 0.047 (500) / 0.031 (1000) — ×0.6 к Табате для PTFE (0.10 /
/// 0.08 / 0.06); от кристалла CsI (голая сцена) η_esc = 0.46 / 0.43 / 0.39 — как
/// у Табаты для Z ≈ 50. Недобор в лёгком веществе — хвост однократного рассеяния
/// на большие углы, которого у гауссова шарнира нет (журнал П94 §5.4, §8).
///
/// Код 0 — посчитано; 2 — ключи/геометрия. Порогов нет — это мерка, числа в журнал.
/// </summary>
static class LayerReturnProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string geometryPath = null, face = "front";
        var energies = new List<double> { 50, 100, 200, 300, 500, 1000, 2000 };
        var angles = new List<double> { 0, 45, 70 };
        int n = 200000;
        ulong seed = 20260917UL;
        bool crystal = false;
        double x0Override = double.NaN, z0Override = double.NaN;   // точка на грани, см (замер)
        foreach (string a in args)
        {
            if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
            else if (a.StartsWith("--face=", StringComparison.Ordinal)) face = a.Substring(7);
            else if (a.StartsWith("--x0=", StringComparison.Ordinal)) x0Override = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--z0=", StringComparison.Ordinal)) z0Override = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--energies=", StringComparison.Ordinal))
            {
                energies.Clear();
                foreach (string s in a.Substring(11).Split(',')) energies.Add(double.Parse(s, CultureInfo.InvariantCulture));
            }
            else if (a.StartsWith("--angles=", StringComparison.Ordinal))
            {
                angles.Clear();
                foreach (string s in a.Substring(9).Split(',')) angles.Add(double.Parse(s, CultureInfo.InvariantCulture));
            }
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = ulong.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a == "--crystal") crystal = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (geometryPath == null)
        {
            Console.Error.WriteLine("нужен --geometry=<файл.in>");
            return 2;
        }

        GeometryModel geometry = GeometryModel.Load(geometryPath);
        Console.WriteLine("геометрия: {0}", geometry.Describe());
        MethodInfo walk = typeof(EfficiencySimulator).GetMethod(
            "TransportInLayers", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo loss = typeof(EfficiencySimulator).GetMethod(
            "ElectronLoss", BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(double), typeof(double), typeof(double), typeof(double), typeof(int) }, null);
        if (walk == null || loss == null)
        {
            Console.Error.WriteLine("⛔ EfficiencySimulator.TransportInLayers / ElectronLoss не найдены — сборка чужая");
            return 2;
        }

        // Грань кристалла: сцена ставит кристалл от z = 0 (передняя грань) до
        // высоты; сторона — x = половина ширины бруса (или радиус).
        double half = 0.05 * Math.Max(geometry.CrystalBoxX > 0 ? geometry.CrystalBoxX : geometry.CrystalDiameter,
                                      1e-9);
        double x0 = 0.0, z0 = 0.0, nx = 0.0, nz = -1.0;    // нормаль наружу
        if (face == "side")
        {
            // ⚠ У бруса высота — CrystalBoxZ, CrystalHeight там ноль: без этого
            // точка ложилась на ребро (z = 0), и η выходила вдвое меньше (П94).
            x0 = half; z0 = 0.05 * (geometry.CrystalBoxZ > 0 ? geometry.CrystalBoxZ : geometry.CrystalHeight);
            nx = 1.0; nz = 0.0;
        }
        else if (face != "front")
        {
            Console.Error.WriteLine("--face= front|side");
            return 2;
        }

        if (!double.IsNaN(x0Override)) x0 = x0Override;
        if (!double.IsNaN(z0Override)) z0 = z0Override;
        Console.WriteLine("грань: {0}, точка ({1:0.####}, 0, {2:0.####}) см, нормаль наружу ({3}, 0, {4}); N = {5}, зерно {6}",
                          face, x0, z0, nx, nz, n, seed);
        Console.WriteLine();
        Console.WriteLine("ОБРАТНОЕ РАССЕЯНИЕ ОТ СЛОЁВ ОБВЯЗКИ (TransportInLayers, ключ ВКЛ): доля вернувшихся η, средняя энергия возврата ⟨T′⟩/T, средний |cos| угла возврата к нормали");
        Console.WriteLine("{0,8} {1,6} {2,9} {3,9} {4,9} {5,9}", "T, кэВ", "θ°", "η", "±", "⟨T′⟩/T", "⟨|cos|⟩");
        foreach (double te in energies)
        {
            foreach (double ang in angles)
            {
                var sim = new EfficiencySimulator(geometry.Clone());
                sim.ElectronLayerTransport = true;
                string name = sim.LightYieldName;          // EnsureBuilt
                sim.ResetStream(seed);
                double th = ang * Math.PI / 180.0;
                int back = 0; double sumT = 0.0, sumCos = 0.0;
                for (int i = 0; i < n; i++)
                {
                    // Азимут — случайный вокруг нормали: направление наружу под углом θ.
                    double phi = 2.0 * Math.PI * (i + 0.5) / n;
                    double ux, uy, uz;
                    if (nz != 0.0)
                    {
                        ux = Math.Sin(th) * Math.Cos(phi); uy = Math.Sin(th) * Math.Sin(phi); uz = nz * Math.Cos(th);
                    }
                    else
                    {
                        ux = nx * Math.Cos(th); uy = Math.Sin(th) * Math.Cos(phi); uz = Math.Sin(th) * Math.Sin(phi);
                    }

                    object[] arg = { x0 + nx * 1e-7, 0.0, z0 + nz * 1e-7, ux, uy, uz, te, 0 };
                    bool entered = (bool)walk.Invoke(sim, arg);
                    if (entered)
                    {
                        back++;
                        sumT += (double)arg[6];
                        double cx = (double)arg[3], cz = (double)arg[5];
                        sumCos += Math.Abs(cx * nx + cz * nz);
                    }
                }

                double eta = back / (double)n;
                Console.WriteLine("{0,8:0.#} {1,6:0} {2,9:0.0000} {3,9:0.0000} {4,9:0.000} {5,9:0.000}",
                                  te, ang, eta, Math.Sqrt(eta * (1 - eta) / n),
                                  back > 0 ? sumT / back / te : 0.0, back > 0 ? sumCos / back : 0.0);
            }
        }

        if (crystal)
        {
            Console.WriteLine();
            Console.WriteLine("ОБРАТНОЕ РАССЕЯНИЕ ОТ КРИСТАЛЛА (ElectronLoss, перенос по кристаллу, ключ ВЫКЛ): электрон ВНУТРЬ с той же грани; доля унесённой энергии ⟨lost⟩/T и (ключ ВКЛ, счётчик) доля вылетевших");
            Console.WriteLine("{0,8} {1,6} {2,9} {3,9}", "T, кэВ", "θ°", "lost/T", "η_esc");
            MethodInfo loss9 = typeof(EfficiencySimulator).GetMethod(
                "ElectronLoss", BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(double), typeof(double), typeof(double), typeof(double), typeof(int),
                        typeof(EfficiencySimulator).GetNestedType("ElectronBirth", BindingFlags.NonPublic),
                        typeof(double), typeof(double), typeof(double) }, null);
            Type birthType = typeof(EfficiencySimulator).GetNestedType("ElectronBirth", BindingFlags.NonPublic);
            object given = Enum.Parse(birthType, "Given");
            foreach (double te in energies)
            {
                foreach (double ang in angles)
                {
                    var sim = new EfficiencySimulator(geometry.Clone());
                    sim.ElectronLayerTransport = true;     // ради счётчика вылетов; возврата на голой сцене нет
                    string name = sim.LightYieldName;
                    sim.ResetStream(seed);
                    double th = ang * Math.PI / 180.0;
                    double lostSum = 0.0;
                    for (int i = 0; i < n; i++)
                    {
                        double phi = 2.0 * Math.PI * (i + 0.5) / n;
                        double ux, uy, uz;
                        if (nz != 0.0)
                        {
                            ux = Math.Sin(th) * Math.Cos(phi); uy = Math.Sin(th) * Math.Sin(phi); uz = -nz * Math.Cos(th);
                        }
                        else
                        {
                            ux = -nx * Math.Cos(th); uy = Math.Sin(th) * Math.Cos(phi); uz = Math.Sin(th) * Math.Sin(phi);
                        }

                        object[] arg = { x0 - nx * 1e-7, 0.0, z0 - nz * 1e-7, te, 0, given, ux, uy, uz };
                        lostSum += (double)loss9.Invoke(sim, arg);
                    }

                    Console.WriteLine("{0,8:0.#} {1,6:0} {2,9:0.000} {3,9:0.0000}", te, ang, lostSum / n / te,
                                      sim.CountLayerEscapes / (double)n);
                }
            }
        }

        return 0;
    }
}
