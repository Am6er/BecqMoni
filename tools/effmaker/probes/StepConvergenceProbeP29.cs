using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

/// <summary>
/// `AMBER274`, физика 29 (полоса `phys29`, 01.11.2026; решение Amber 30.10.2026 вопросником,
/// дословно: «Слои + кристалл сразу»): СХОДИМОСТЬ ПЕРЕНОСА ЭЛЕКТРОНА ПО ДОЛЕ ШАГА. Журнал —
/// `handover/handover-2026-11-01-amber274-phys29.md`.
///
///     StepConvergenceProbeP29 --scene=<сцена.in> --mono=624.216 [--steps=0.2,0.1,0.05,0.025]
///                             [--n=4000000] [--threads=N] [--emix=0|1] [--mott=0|1] [--parts=3] [--estr=0|1]
///                             [--eref=0|1] [--dpwa=0|1] [--rparts=31]   (физика 30, `phys29c`)
///                             [--scan=both|layers|crystal] [--lstep=<доля>] [--cstep=<доля>]
///     StepConvergenceProbeP29 --scene=<сцена.in> --dump-scene=<файл.scene>   (сцена для g4cf)
///
/// `--scan=` — какая доля шага разворачивается списком `--steps=`: обе (умолчание), только
/// слоёв (`ElectronStepFraction`; кристалл — `--cstep=` или умолчание) или только кристалла
/// (`ElectronCrystalStepFraction`, действует под ключом emix; слои — `--lstep=` или умолчание).
///
/// Моноэлектрон из точки сцены, изотропно (как строка раздела электрона пробы,
/// `EfficiencySimulator.ElectronSourceResponse`): доля ВОШЕДШИХ в кристалл на испущенный и
/// оставленное в кристалле 1–700 кэВ на испущенный — при каждой доле шага; цена — мкс
/// ЦП на историю. Разность соседних долей (f и f/2) печатается в процентах и в σ — мера
/// приёмки «|Δ| ≤ 0.5 %». История делится на порции по потокам, у порции своё зерно
/// (индекс `7001 + 1000·k`, порция 0 — индекс 7001, как у проб критиков): итог не зависит
/// от числа потоков только через число порций — его печать называет.
///
/// Только читает сцену; ничего не пишет. Код 2 — ключи.
/// </summary>
public static class StepConvergenceProbeP29
{
    /// <summary>(`AMBER274`, физика 30) `--eref=`, `--dpwa=`, `--rparts=` — ключи физики 30 и рычаг частей; −1 — умолчание.</summary>
    static int eref = -1, dpwa = -1, refineParts = -1;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string scene = null;
        double te = 624.216;
        double[] steps = { 0.2, 0.1, 0.05, 0.025 };
        int n = 4000000, threads = Environment.ProcessorCount, emix = -1, mott = -1, parts = -1, estr = -1, chunks = 64;
        string scan = "both";
        string dumpScene = null;
        double fixedLayer = -1.0, fixedCrystal = -1.0;
        foreach (string a in args)
        {
            if (a.StartsWith("--scene=", StringComparison.Ordinal)) scene = a.Substring(8);
            else if (a.StartsWith("--mono=", StringComparison.Ordinal)) te = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--steps=", StringComparison.Ordinal))
                steps = Array.ConvertAll(a.Substring(8).Split(','), s => double.Parse(s, CultureInfo.InvariantCulture));
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--chunks=", StringComparison.Ordinal)) chunks = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--emix=", StringComparison.Ordinal)) emix = a.Substring(7) != "0" ? 1 : 0;
            else if (a.StartsWith("--mott=", StringComparison.Ordinal)) mott = a.Substring(7) != "0" ? 1 : 0;
            else if (a.StartsWith("--eref=", StringComparison.Ordinal)) eref = a.Substring(7) != "0" ? 1 : 0;
            else if (a.StartsWith("--dpwa=", StringComparison.Ordinal)) dpwa = a.Substring(7) != "0" ? 1 : 0;
            else if (a.StartsWith("--rparts=", StringComparison.Ordinal)) refineParts = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--parts=", StringComparison.Ordinal)) parts = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--estr=", StringComparison.Ordinal)) estr = a.Substring(7) != "0" ? 1 : 0;
            else if (a.StartsWith("--scan=", StringComparison.Ordinal)) scan = a.Substring(7);
            else if (a.StartsWith("--dump-scene=", StringComparison.Ordinal)) dumpScene = a.Substring(13);
            else if (a.StartsWith("--lstep=", StringComparison.Ordinal)) fixedLayer = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--cstep=", StringComparison.Ordinal)) fixedCrystal = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (scene == null)
        {
            Console.Error.WriteLine("нужен --scene=<сцена.in>");
            return 2;
        }

        GlobalConfigManager.GetInstance();
        GeometryModel g = GeometryModel.Load(scene);
        var options = new ResponseMatrixOptions();
        MethodInfo make = typeof(ResponseMatrixBuilder).GetMethod("MakeSimulator", BindingFlags.NonPublic | BindingFlags.Static);
        if (dumpScene != null)
        {
            // Сцена для арбитра (`g4cf scene`) — тем же `DumpScene`, что у `G4RawProbe --scene=`.
            var dumper = (EfficiencySimulator)make.Invoke(null, new object[] { g, options, 7001, te });
            System.IO.File.WriteAllText(dumpScene, dumper.DumpScene(), new UTF8Encoding(false));
            Console.WriteLine("сцена записана: " + dumpScene);
            return 0;
        }

        {
            var probe = (EfficiencySimulator)make.Invoke(null, new object[] { g, options, 7001, te });
            ApplyKeys(probe, -1.0, -1.0, emix, parts, estr, mott);
            Console.WriteLine("== сходимость по шагу (физика {0}): сцена {1}, капсула {2}, моно {3} кэВ, {4} историй на шаг, порций {5}, потоков {6}; emix {7} (половины {8}), estr {9}, mott {13}; меняется доля {10} (слои вне развёртки {11}, кристалл вне развёртки {12})",
                              ResponseMatrix.PhysicsVersion, g.Name, ResponseElectronSection.CapsuleText(g), te, n, chunks, threads,
                              probe.ElectronMixedTransport ? 1 : 0, probe.ElectronMixedParts, probe.ElectronStraggling ? 1 : 0,
                              scan, fixedLayer > 0 ? fixedLayer : probe.ElectronStepFraction,
                              fixedCrystal > 0 ? fixedCrystal : probe.ElectronCrystalStepFraction,
                              probe.ElectronMottHeavy ? 1 : 0);
        }

        double prevEnt = double.NaN, prevSd = double.NaN, prevDep = double.NaN, prevDsd = double.NaN;
        foreach (double f in steps)
        {
            long entered = 0, histories = 0, caps = 0, crystalSteps = 0, layerSteps = 0;
            double dep = 0.0, dep2 = 0.0, cpu = 0.0;
            object gate = new object();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Parallel.For(0, chunks, new ParallelOptions { MaxDegreeOfParallelism = threads }, k =>
            {
                int m = n / chunks + (k < n % chunks ? 1 : 0);
                var sim = (EfficiencySimulator)make.Invoke(null, new object[] { g, options, 7001 + 1000 * k, te });
                double lf = scan == "crystal" ? fixedLayer : f;
                double cf = scan == "layers" ? fixedCrystal : f;
                ApplyKeys(sim, lf, cf, emix, parts, estr, mott);
                sim.Histories = m;
                EfficiencySimulator.ElectronSourceStats st;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                double[] row = sim.ElectronSourceResponse(te, false, 1.0, 0.0, out st);
                double sec = clock.Elapsed.TotalSeconds;
                double d = 0.0;
                for (int b = 1; b < 700 && b < row.Length; b++)
                {
                    d += row[b];
                }

                lock (gate)
                {
                    entered += st.Entered;
                    histories += st.Histories;
                    dep += d * st.Histories;
                    dep2 += d * d * st.Histories;
                    cpu += sec;
                    caps += sim.CountTransportStepCap;
                    crystalSteps += sim.CountCrystalSteps;
                    layerSteps += sim.CountLayerSteps;
                }
            });

            double ent = (double)entered / histories;
            double sd = Math.Sqrt(ent * (1.0 - ent) / histories);
            // Оставленное 1–700 кэВ на испущенный: доля историй с заносом в полосе — биномиально
            // (отклик на историю — 0 или 1 в полосе, строка нормирована на историю).
            double dp = dep / histories;
            double dsd = Math.Sqrt(Math.Max(0.0, dp * (1.0 - dp) / histories));
            string delta = "";
            if (!double.IsNaN(prevEnt))
            {
                double dRel = (ent / prevEnt - 1.0) * 100.0;
                double dSig = (ent - prevEnt) / Math.Sqrt(sd * sd + prevSd * prevSd);
                double qRel = (dp / prevDep - 1.0) * 100.0;
                double qSig = (dp - prevDep) / Math.Sqrt(dsd * dsd + prevDsd * prevDsd);
                delta = string.Format(CultureInfo.InvariantCulture,
                                      "  Δ к прежнему шагу: вошло {0:+0.00;-0.00} % ({1:+0.0;-0.0}σ), оставлено {2:+0.00;-0.00} % ({3:+0.0;-0.0}σ)",
                                      dRel, dSig, qRel, qSig);
            }

            Console.WriteLine("шаг {0,-7} вошло {1:E4} ± {2:E2} ({3:F2} %)  оставлено 1–700 {4:E4} ± {5:E2}  {6:F2} мкс ЦП/ист.  шагов кристалла {7:F2}/ист., слоёв {8:F2}/ист., упоров {9}  ({10:F0} с){11}",
                              f.ToString("R", CultureInfo.InvariantCulture), ent, sd, 100.0 * sd / ent, dp, dsd,
                              cpu * 1e6 / histories, (double)crystalSteps / histories, (double)layerSteps / histories, caps,
                              watch.Elapsed.TotalSeconds, delta);
            prevEnt = ent;
            prevSd = sd;
            prevDep = dp;
            prevDsd = dsd;
        }

        return 0;
    }

    static void ApplyKeys(EfficiencySimulator sim, double layerF, double crystalF, int emix, int parts, int estr, int mott)
    {
        if (layerF > 0.0)
        {
            sim.ElectronStepFraction = layerF;
        }

        if (crystalF > 0.0)
        {
            sim.ElectronCrystalStepFraction = crystalF;
        }

        if (emix >= 0)
        {
            sim.ElectronMixedTransport = emix != 0;
        }

        if (mott >= 0)
        {
            sim.ElectronMottHeavy = mott != 0;
        }

        if (parts >= 0)
        {
            sim.ElectronMixedParts = parts;
        }

        if (eref >= 0)
        {
            sim.ElectronElasticRefine = eref != 0;
        }

        if (dpwa >= 0)
        {
            sim.ElectronScreeningDpwa = dpwa != 0;
        }

        if (refineParts >= 0)
        {
            sim.ElectronRefineParts = refineParts;
        }

        if (estr >= 0)
        {
            sim.ElectronStraggling = estr != 0;
        }
    }
}
