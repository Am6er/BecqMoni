using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

/// <summary>
/// Поверка П94 (17.09.2026, `AMBER44`) и П100 (18.09.2026, `M13`): ОБРАТНОЕ
/// РАССЕЯНИЕ ЭЛЕКТРОНА В СЛОЯХ ОБВЯЗКИ переносом `TransportInLayers` (ключ
/// `ElectronLayerTransport`) — против табличных коэффициентов Табаты (1971,
/// толстая мишень, нормальное падение; числа П55 §2.3): PTFE η(100) 0.10,
/// η(480) 0.08, η(1000) 0.06, η(2000) 0.04; Al 0.16 / 0.14 / 0.11 / 0.08;
/// MgO ≈ 0.13 / 0.11 / 0.08 / 0.06.
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
/// (`M13`, П100) `--elmix=0|1|both` — смешанная схема упругого рассеяния в
/// слоях (`ElectronLayerMixedScattering`; `both` — оба столбца рядом, это и
/// есть положительный контроль: ВЫКЛ обязан давать прежние 0.062/0.047/0.032
/// на RC103 П55); `--cutoff=<град>` — угол отсечки жёстких столкновений
/// (`LayerHardCutoffDeg`, умолчание 20; поверка независимости — 10/20/30);
/// `--diag` — числа ОДНОГО ШАГА переноса по веществам сцены: пробег, шаг,
/// θ₀ Хайленда, средний 1 − cos шага (Хайленд / мягкий транспорт / полный
/// транспорт Резерфорда), свободный пробег до жёсткого столкновения,
/// вероятность отклонения > 90° за шаг у шарнира и у однократного рассеяния
/// — диагноз недобора хвоста числом; `--step=<доля>` —
/// доля остаточного пробега на шаг (`ElectronStepFraction`, умолчание 0.1) для
/// поверки сходимости η по шагу в слое.
///
///     layerreturnprobe --geometry=RC103_point0_p55.in [--face=front|side]
///                      [--energies=50,100,200,300,500,1000,2000] [--angles=0,45,70]
///                      [--n=200000] [--seed=20260917] [--crystal] [--x0=<см>] [--z0=<см>]
///                      [--elmix=0|1|both] [--cutoff=20] [--step=0.1] [--diag]
///
/// Мерка П94 (RC103 П55, PTFE 1 мм + Al 1 мм + пустота, нормальное падение, ключ
/// `elmix` ВЫКЛ): η = 0.062 (100 кэВ) / 0.047 (500) / 0.031 (1000) — ×0.6 к Табате
/// для PTFE (0.10 / 0.08 / 0.06); от кристалла CsI (голая сцена) η_esc = 0.46 /
/// 0.43 / 0.39 — как у Табаты для Z ≈ 50. Недобор в лёгком веществе — хвост
/// однократного рассеяния на большие углы, которого у гауссова шарнира нет
/// (журнал П94 §5.4, §8; П100 — смешанная схема, приёмка ±20 % к Табате).
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
        bool crystal = false, diag = false;
        string elmix = "both";
        double cutoff = double.NaN, stepFraction = double.NaN;
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
            else if (a.StartsWith("--elmix=", StringComparison.Ordinal))
            {
                elmix = a.Substring(8);
                if (elmix != "0" && elmix != "1" && elmix != "both") { Console.Error.WriteLine("--elmix= 0|1|both"); return 2; }
            }
            else if (a.StartsWith("--cutoff=", StringComparison.Ordinal)) cutoff = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--step=", StringComparison.Ordinal)) stepFraction = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a == "--crystal") crystal = true;
            else if (a == "--diag") diag = true;
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
        // ⛔ (П100) Кэш луча прячется и возвращается ВОКРУГ каждого вызова, как это делает
        // `EscapeOrReturn` в симуляторе (`SaveRay`/`RestoreRay`, П94 §7.1). Без этого проба
        // зовёт `TransportInLayers` подряд из ОДНОЙ точки в 1 нм от грани, и `At` доверяет
        // лучу прошлой истории, если точка лежит на нём в 10 нм: электрон, вернувшийся в
        // кристалл в нанометрах от начала (при смешанной схеме жёсткое столкновение бывает
        // и в нанометрах от грани), оставляет луч, на котором лежит стартовая точка, — и
        // все следующие истории «возвращаются» сразу с полной энергией, кэш залипает
        // (измерено П100: 100 кэВ, 70°, N = 100 000 — η 0.71 и ⟨T′⟩/T 0.92 против 0.41 и
        // 0.72 при другом зерне). В симуляторе этого нет: там снимок делает `EscapeOrReturn`.
        MethodInfo saveRay = typeof(EfficiencySimulator).GetMethod("SaveRay", BindingFlags.NonPublic | BindingFlags.Instance);
        MethodInfo restoreRay = typeof(EfficiencySimulator).GetMethod("RestoreRay", BindingFlags.NonPublic | BindingFlags.Instance);
        if (walk == null || loss == null || saveRay == null || restoreRay == null)
        {
            Console.Error.WriteLine("⛔ EfficiencySimulator.TransportInLayers / ElectronLoss / SaveRay / RestoreRay не найдены — сборка чужая");
            return 2;
        }

        var store = new ResponseMatrixOptions();
        var modes = new List<bool>();
        if (elmix == "both") { modes.Add(false); modes.Add(true); }
        else modes.Add(elmix == "1");
        Console.WriteLine("смешанная схема в слоях (`M13`, --elmix=): {0}; умолчание склада — {1}; отсечка {2}",
                          elmix, store.ElectronLayerMixedScattering ? "ВКЛ" : "ВЫКЛ",
                          double.IsNaN(cutoff)
                              ? "умолчание симулятора (" + new EfficiencySimulator(geometry.Clone()).LayerHardCutoffDeg.ToString("0.#", CultureInfo.InvariantCulture) + "°)"
                              : cutoff.ToString("0.#", CultureInfo.InvariantCulture) + "° (ключом)");

        if (diag)
        {
            int code = Diagnose(geometry, energies, cutoff);
            if (code != 0) return code;
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
        Console.WriteLine("ОБРАТНОЕ РАССЕЯНИЕ ОТ СЛОЁВ ОБВЯЗКИ (TransportInLayers, ключ eltr ВКЛ): доля вернувшихся η, средняя энергия возврата ⟨T′⟩/T, средний |cos| угла возврата к нормали, жёстких столкновений на шаг; столбцы — по режимам elmix");
        var head = new StringBuilder();
        head.AppendFormat("{0,8} {1,6}", "T, кэВ", "θ°");
        foreach (bool m in modes)
        {
            head.AppendFormat(" | {0,11} {1,7} {2,7} {3,7} {4,8}", (m ? "elmix=1" : "elmix=0") + " η", "±", "⟨T′⟩/T", "⟨|cos|⟩", "жёст/шаг");
        }

        Console.WriteLine(head.ToString());
        foreach (double te in energies)
        {
            foreach (double ang in angles)
            {
                var row = new StringBuilder();
                row.AppendFormat("{0,8:0.#} {1,6:0}", te, ang);
                foreach (bool m in modes)
                {
                    var sim = new EfficiencySimulator(geometry.Clone());
                    sim.ElectronLayerTransport = true;
                    sim.ElectronLayerMixedScattering = m;
                    if (!double.IsNaN(cutoff)) sim.LayerHardCutoffDeg = cutoff;
                    if (!double.IsNaN(stepFraction)) sim.ElectronStepFraction = stepFraction;   // сходимость по шагу (как G4RawProbe --etr-step=)
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
                        saveRay.Invoke(sim, null);
                        bool entered = (bool)walk.Invoke(sim, arg);
                        restoreRay.Invoke(sim, null);
                        if (entered)
                        {
                            back++;
                            sumT += (double)arg[6];
                            double cx = (double)arg[3], cz = (double)arg[5];
                            sumCos += Math.Abs(cx * nx + cz * nz);
                        }
                    }

                    double eta = back / (double)n;
                    row.AppendFormat(" | {0,11:0.0000} {1,7:0.0000} {2,7:0.000} {3,7:0.000} {4,8:0.000}",
                                     eta, Math.Sqrt(eta * (1 - eta) / n),
                                     back > 0 ? sumT / back / te : 0.0, back > 0 ? sumCos / back : 0.0,
                                     sim.CountLayerSteps > 0 ? (double)sim.CountLayerHardCollisions / sim.CountLayerSteps : 0.0);
                }

                Console.WriteLine(row.ToString());
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

    /// <summary>
    /// (`M13`, П100) Числа ОДНОГО ШАГА переноса (0.1 остаточного пробега CSDA)
    /// по веществам сцены — отражением на тех же приватных функциях, что
    /// считает перенос: шаг, θ₀ Хайленда, доля мягкого транспортного сечения,
    /// средний 1 − cos шага у Хайленда (θ₀²), у мягкой части (транспорт ниже
    /// отсечки) и полный (транспорт Резерфорда), свободный пробег до жёсткого
    /// столкновения выше отсечки, вероятность отклонения > 90° за шаг у шарнира
    /// (экспонента по 1 − cos со средним θ₀²) и у однократного рассеяния
    /// (1 − exp(−Σnσ(&gt;90°)·s), с мажорантой Мотта).
    /// </summary>
    static int Diagnose(GeometryModel geometry, List<double> energies, double cutoff)
    {
        Type simType = typeof(EfficiencySimulator);
        BindingFlags priv = BindingFlags.NonPublic | BindingFlags.Instance;
        MethodInfo highland = simType.GetMethod("HighlandTheta0", BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo carry = simType.GetMethod("CarryMedium", priv);
        MethodInfo x0Of = simType.GetMethod("LayerRadiationLength", priv);
        MethodInfo elements = simType.GetMethod("LayerScatterElements", priv);
        MethodInfo elastic = simType.GetMethod("LayerElasticStep", priv);
        MethodInfo cutMu = simType.GetMethod("LayerHardCutoffMu", priv);
        if (highland == null || carry == null || x0Of == null || elements == null || elastic == null || cutMu == null)
        {
            Console.Error.WriteLine("⛔ приватные HighlandTheta0 / CarryMedium / LayerRadiationLength / LayerScatterElements / LayerElasticStep / LayerHardCutoffMu не найдены — сборка чужая");
            return 2;
        }

        var sim = new EfficiencySimulator(geometry.Clone());
        sim.ElectronLayerTransport = true;
        sim.ElectronLayerMixedScattering = true;
        if (!double.IsNaN(cutoff)) sim.LayerHardCutoffDeg = cutoff;
        string built = sim.LightYieldName;
        double muCut = (double)cutMu.Invoke(sim, null);
        Console.WriteLine();
        Console.WriteLine("ДИАГНОЗ ОДНОГО ШАГА (0.1 CSDA) по веществам сцены: отсечка {0:0.#}° (1−cos = {1:0.0000}). θ₀ — Хайленд на шаг; ⟨μ⟩ — средний 1−cos θ шага: Хайленд θ₀², мягкий (транспорт ниже отсечки), полный (транспорт Резерфорда Z(Z+1)); P>90° — вероятность отклонения больше 90° за шаг у шарнира Хайленда и у однократного рассеяния (мажоранта Мотта)",
                          sim.LayerHardCutoffDeg, muCut);
        Console.WriteLine("{0,-26} {1,7} {2,10} {3,10} {4,7} {5,9} {6,9} {7,9} {8,7} {9,10} {10,9} {11,11} {12,11} {13,8}",
                          "вещество", "T, кэВ", "R, г/см²", "шаг, см", "θ₀", "⟨μ⟩ Хайл", "⟨μ⟩ мягк", "⟨μ⟩ полн", "п/Х", "λ_hard, см", "жёст/шаг", "P>90° шарн", "P>90° одн", "отн");
        var mats = new List<KeyValuePair<string, GeometryMaterial>>
        {
            new KeyValuePair<string, GeometryMaterial>("отражатель", geometry.Reflector),
            new KeyValuePair<string, GeometryMaterial>("зазор", geometry.Gap),
            new KeyValuePair<string, GeometryMaterial>("корпус", geometry.Cladding),
            new KeyValuePair<string, GeometryMaterial>("стенка сосуда", geometry.BeakerWall),
            new KeyValuePair<string, GeometryMaterial>("проба", geometry.Source),
            new KeyValuePair<string, GeometryMaterial>("кристалл (справочно)", geometry.Crystal),
        };
        foreach (var pair in mats)
        {
            GeometryMaterial m = pair.Value;
            if (m == null || m.Fractions.Count == 0 || !(m.Density > 0.0)) continue;
            var medium = (ElectronData.Material)carry.Invoke(sim, new object[] { m });
            double x0 = (double)x0Of.Invoke(sim, new object[] { m });
            object el = elements.Invoke(sim, new object[] { m });
            int count = ((Array)el).Length;
            double[] share = new double[Math.Max(count, 1)];
            foreach (double te in energies)
            {
                double range = ElectronData.RangeOf(medium, te);
                double stepG = 0.1 * range, stepCm = stepG / m.Density;
                double tMid = ElectronData.EnergyOfRange(medium, range - 0.5 * stepG);
                double theta0 = (double)highland.Invoke(null, new object[] { tMid, stepG / x0 });
                double s = theta0 * theta0;
                double pHinge = s > 50.0 ? 0.5 : (Math.Exp(-1.0 / s) - Math.Exp(-2.0 / s)) / (1.0 - Math.Exp(-2.0 / s));
                object[] argCut = { el, te, muCut, 0.0, 0.0, share };
                elastic.Invoke(sim, argCut);
                double hardPerCm = (double)argCut[3], softTr = (double)argCut[4];
                object[] arg90 = { el, te, 1.0, 0.0, 0.0, share };
                elastic.Invoke(sim, arg90);
                double hard90 = (double)arg90[3];
                object[] argAll = { el, te, 2.0, 0.0, 0.0, share };     // отсечка 180° — всё мягкое = полный транспорт
                elastic.Invoke(sim, argAll);
                double totalTr = (double)argAll[4];
                double muSoft = 1.0 - Math.Exp(-softTr * stepCm), muTotal = 1.0 - Math.Exp(-totalTr * stepCm);
                double pSingle = 1.0 - Math.Exp(-hard90 * stepCm);
                Console.WriteLine("{0,-26} {1,7:0.#} {2,10:0.00000} {3,10:0.00000} {4,7:0.000} {5,9:0.0000} {6,9:0.0000} {7,9:0.0000} {8,7:0.00} {9,10:0.0000} {10,9:0.000} {11,11:0.00E+00} {12,11:0.00E+00} {13,8:0.#}",
                                  pair.Key + " " + m.Name, te, range, stepCm, theta0, s, muSoft, muTotal, s > 0.0 ? muTotal / s : 0.0,
                                  hardPerCm > 0.0 ? 1.0 / hardPerCm : double.PositiveInfinity,
                                  1.0 - Math.Exp(-hardPerCm * stepCm), pHinge, pSingle,
                                  pHinge > 0.0 ? pSingle / pHinge : double.PositiveInfinity);
            }
        }

        Console.WriteLine();
        return 0;
    }
}
