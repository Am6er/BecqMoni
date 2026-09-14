using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

// ПРИЁМКА СЦЕНЫ ИЗОТРОПНОГО ПОЛЯ (`AMBER13` (б), полоса П2 12.09.2026, решение
// Amber «Оставь только ISO»).
//
// Что мерится и чем это доказывается:
//
//   (1) ОТВЕТ НЕ ЗАВИСИТ ОТ РАДИУСА СФЕРЫ. Эффективная площадь A_эфф(E), см²,
//       считается на R = 50 и 100 см при одинаковом числе историй — числа
//       обязаны сойтись в пределах статистики (расхождение в σ печатается).
//       Это даровой контроль нормировки на флюенс: при нормировке «на квант»
//       ответ падал бы вчетверо от 50 к 100 см.
//   (2) ГОЛЫЙ КРИСТАЛЛ, ВХОД БЕЗ ОСЛАБЛЕНИЯ (ScoreEntranceOnly): эффективная
//       площадь входа = средняя проекция выпуклого тела = S/4 по теореме Коши;
//       у цилиндра S = 2πr² + 2πrh считается руками. Плюс ε_полная на 30 кэВ
//       того же голого кристалла — предел полного поглощения — печатается
//       рядом (справочно: пробег там 0.3 мм, и в кристалл входит почти всё).
//   (3) ФАЙЛ `.in`: ключи `DS_Scene = ISO` и `DS_FieldRadius` пишутся и
//       читаются обратно; ИСХОДНЫЙ файл без ключей читается прежним (сцена
//       None, радиус 0).
//   (4) МАТРИЦА (`--matrix`): маленькая матрица сцены поля пишется и читается
//       с признаком `PerUnitFluence` (хвост NORM), клеймо сходится само с
//       собой; старый `.rmx` (`--old=`) читается как `PerEmittedQuantum`.
//
// ПОЛОЖИТЕЛЬНЫЕ КОНТРОЛИ — проба ОБЯЗАНА отказать (код 1):
//   --sabotage=quantum  нормировка «на квант» вместо флюенса: A делится на πR²,
//                       как если бы множитель площади забыли; ловит (1);
//   --sabotage=nocos    выброшен множитель 4·cos θ ламбертова испускания
//                       (`EfficiencySimulator.IsoFieldNoCosineWeight`): A
//                       выходит вчетверо меньше; ловит (2).
//
//   isofieldprobe [--in=tools\CORPUS\corpus\geometries\G1S_point5.in]
//                 [--out=<каталог для .in/.rmx>] [--n=200000] [--e=661.657]
//                 [--r=50,100] [--sabotage=none|quantum|nocos] [--matrix]
//                 [--old=<старый .rmx>] [--seed=N]
//
// Коды: 0 — все проверки прошли; 1 — есть отказ (при порче — ожидаемо);
// 2 — не с чем работать (ключ, файл).
class IsoFieldProbe
{
    static int failed;

    static void Check(string what, bool ok, string detail)
    {
        Console.WriteLine("   {0,-58} {1}{2}", what, ok ? "ок" : "ПРОВАЛ",
                          detail != null ? "  " + detail : "");
        if (!ok)
        {
            failed++;
        }
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

        string inPath = Path.Combine("tools", "CORPUS", "corpus", "geometries", "G1S_point5.in");
        string outDir = null;
        string oldRmx = null;
        int histories = 200000;
        double energy = 661.657;
        double[] radiiCm = { 50.0, 100.0 };
        string sabotage = "none";
        bool matrix = false;
        int seed = 0;
        foreach (string a in args)
        {
            if (a.StartsWith("--in=", StringComparison.Ordinal)) inPath = a.Substring(5);
            else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
            else if (a.StartsWith("--old=", StringComparison.Ordinal)) oldRmx = a.Substring(6);
            else if (a.StartsWith("--n=", StringComparison.Ordinal))
                histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--e=", StringComparison.Ordinal))
                energy = double.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed=", StringComparison.Ordinal))
                seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--r=", StringComparison.Ordinal))
            {
                string[] parts = a.Substring(4).Split(',');
                radiiCm = new double[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    radiiCm[i] = double.Parse(parts[i], CultureInfo.InvariantCulture);
                }
            }
            else if (a.StartsWith("--sabotage=", StringComparison.Ordinal)) sabotage = a.Substring(11);
            else if (a == "--matrix") matrix = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (sabotage != "none" && sabotage != "quantum" && sabotage != "nocos")
        {
            Console.Error.WriteLine("--sabotage= понимает none, quantum и nocos, а получил «" + sabotage + "»");
            return 2;
        }

        if (!File.Exists(inPath))
        {
            Console.Error.WriteLine("нет файла геометрии: " + inPath);
            return 2;
        }

        if (outDir == null)
        {
            outDir = Path.Combine(Path.GetTempPath(), "isofieldprobe");
        }

        Directory.CreateDirectory(outDir);
        GlobalConfigManager.GetInstance();

        Console.WriteLine("сцена изотропного поля (AMBER13 (б)): {0}", inPath);
        Console.WriteLine("историй на плечо {0}, энергия {1:F3} кэВ, радиусы {2} см, порча: {3}",
                          histories, energy, string.Join(", ", Array.ConvertAll(radiiCm,
                              r => r.ToString("F0", CultureInfo.InvariantCulture))), sabotage);
        Console.WriteLine();

        GeometryModel original = GeometryModel.Load(inPath);
        Check("исходный файл: сцена None, радиус поля 0",
              original.Scene == GeometrySceneKind.None && original.FieldRadius == 0.0,
              original.Scene + " / " + original.FieldRadius.ToString("F1", CultureInfo.InvariantCulture));

        GeometryModel iso = original.Clone();
        iso.Scene = GeometrySceneKind.Iso;
        GeometryScenes.Apply(iso, 3000.0);
        double floorMm = GeometryScenes.MinFieldRadiusMm(iso);
        Check("сцена ISO собрана: форма POINT, радиус >= габарита",
              iso.SourceType == GeometrySourceType.Point && iso.FieldRadius >= floorMm,
              string.Format(CultureInfo.InvariantCulture, "R = {0:F0} мм, габарит {1:F1} мм",
                            iso.FieldRadius, floorMm));
        Check("нормировка по геометрии — на единичный флюенс",
              ResponseMatrix.NormalizationOf(iso) == ResponseMatrixNormalization.PerUnitFluence
              && ResponseMatrix.NormalizationOf(original) == ResponseMatrixNormalization.PerEmittedQuantum,
              null);
        Check("связки размеров сцены ISO молчат",
              GeometryScenes.Inconsistencies(iso).Count == 0, null);
        GeometryModel tooSmall = iso.Clone();
        tooSmall.FieldRadius = 0.5 * floorMm;
        List<GeometryScenes.Issue> issues = GeometryScenes.Inconsistencies(tooSmall);
        Check("радиус внутри габарита — связка отзывается на FieldRadius",
              issues.Count == 1 && issues[0].Field == "FieldRadius"
              && issues[0].Resource == "GeometryEditorErrorFieldRadiusSmall", null);
        Console.WriteLine();

        // (3) файл `.in` туда и обратно
        string isoPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(inPath) + "_iso.in");
        GeometryWriter.Save(iso, isoPath);
        string text = File.ReadAllText(isoPath);
        GeometryModel back = GeometryModel.Load(isoPath);
        Check("файл .in несёт DS_Scene = ISO и DS_FieldRadius",
              text.Contains("DS_Scene = ISO") && text.Contains("DS_FieldRadius = "), null);
        Check("прочитан обратно: сцена ISO, радиус тот же",
              back.Scene == GeometrySceneKind.Iso
              && Math.Abs(back.FieldRadius - iso.FieldRadius) < 1e-6,
              string.Format(CultureInfo.InvariantCulture, "{0} / {1:F3} мм", back.Scene, back.FieldRadius));
        Check("клеймо ISO отличается от клейма исходной сцены",
              ResponseMatrix.ComputeStamp(iso, new ResponseMatrixOptions())
              != ResponseMatrix.ComputeStamp(original, new ResponseMatrixOptions()), null);
        Console.WriteLine();

        // (1) A_эфф на нескольких радиусах
        Console.WriteLine("(1) эффективная площадь на {0:F1} кэВ, см² — от радиуса зависеть не должна:",
                          energy);
        double[] peak = new double[radiiCm.Length], peakErr = new double[radiiCm.Length];
        double[] total = new double[radiiCm.Length], totalErr = new double[radiiCm.Length];
        for (int i = 0; i < radiiCm.Length; i++)
        {
            GeometryModel g = iso.Clone();
            g.FieldRadius = radiiCm[i] * GeometryModel.MmPerCm;
            GeometryScenes.Iso(g);
            if (Math.Abs(g.FieldRadius - radiiCm[i] * GeometryModel.MmPerCm) > 1e-9)
            {
                Console.WriteLine("   радиус {0:F0} см меньше габарита, поднят до {1:F1} см",
                                  radiiCm[i], g.FieldRadius / GeometryModel.MmPerCm);
            }

            EfficiencySimulator sim = MakeSimulator(g, histories, seed, sabotage);
            if (i == 0)
            {
                Console.WriteLine("   " + sim.DescribeScene().Replace("\n", "\n   "));
                Check("симулятор: PerUnitFluence", sim.PerUnitFluence, null);
            }

            double err;
            double a = sim.Efficiency(energy, out err);
            double scale = sabotage == "quantum" ? 1.0 / (Math.PI * radiiCm[i] * radiiCm[i]) : 1.0;
            peak[i] = a * scale;
            peakErr[i] = err;

            EfficiencySimulator sim2 = MakeSimulator(g, histories, seed, sabotage);
            double err2;
            double t = sim2.TotalEfficiency(energy, out err2);
            total[i] = t * scale;
            totalErr[i] = err2;
            Console.WriteLine("   R = {0,5:F0} см: A_пик = {1:F4} см² ± {2:F2} %   A_полн = {3:F4} см² ± {4:F2} %",
                              radiiCm[i], peak[i], err, total[i], err2);
        }

        for (int i = 1; i < radiiCm.Length; i++)
        {
            double sigma = Math.Sqrt(Math.Pow(peak[0] * peakErr[0] / 100.0, 2.0)
                                     + Math.Pow(peak[i] * peakErr[i] / 100.0, 2.0));
            double pull = sigma > 0.0 ? Math.Abs(peak[i] - peak[0]) / sigma : double.PositiveInfinity;
            Check(string.Format(CultureInfo.InvariantCulture, "A_пик({0:F0}) = A_пик({1:F0}) в пределах 3σ",
                                radiiCm[0], radiiCm[i]),
                  pull <= 3.0,
                  string.Format(CultureInfo.InvariantCulture, "расхождение {0:F3} см² = {1:F2} σ, отношение {2:F4}",
                                peak[i] - peak[0], pull, peak[0] > 0.0 ? peak[i] / peak[0] : double.NaN));
            double sigmaT = Math.Sqrt(Math.Pow(total[0] * totalErr[0] / 100.0, 2.0)
                                      + Math.Pow(total[i] * totalErr[i] / 100.0, 2.0));
            double pullT = sigmaT > 0.0 ? Math.Abs(total[i] - total[0]) / sigmaT : double.PositiveInfinity;
            Check(string.Format(CultureInfo.InvariantCulture, "A_полн({0:F0}) = A_полн({1:F0}) в пределах 3σ",
                                radiiCm[0], radiiCm[i]),
                  pullT <= 3.0,
                  string.Format(CultureInfo.InvariantCulture, "расхождение {0:F3} см² = {1:F2} σ, отношение {2:F4}",
                                total[i] - total[0], pullT, total[0] > 0.0 ? total[i] / total[0] : double.NaN));
        }

        Console.WriteLine();

        // (2) голый кристалл: вход = S/4 (Коши)
        Console.WriteLine("(2) голый цилиндрический кристалл: площадь входа против S/4 (Коши):");
        GeometryModel bare = iso.Clone();
        bare.Shape = CrystalShape.Cylinder;
        bare.FrontReflectorThickness = 0.0;
        bare.SideReflectorThickness = 0.0;
        bare.FrontGapThickness = 0.0;
        bare.SideGapThickness = 0.0;
        bare.FrontCladdingThickness = 0.0;
        bare.SideCladdingThickness = 0.0;
        bare.MountingThickness = 0.0;
        bare.FieldRadius = radiiCm[0] * GeometryModel.MmPerCm;
        GeometryScenes.Iso(bare);
        double rCm = 0.5 * bare.CrystalDiameter / GeometryModel.MmPerCm;
        double hCm = bare.CrystalHeight / GeometryModel.MmPerCm;
        double surface = 2.0 * Math.PI * rCm * rCm + 2.0 * Math.PI * rCm * hCm;
        double cauchy = surface / 4.0;
        Console.WriteLine("   кристалл Ø{0:F2} × {1:F2} см: S = {2:F3} см², S/4 = {3:F3} см²",
                          2.0 * rCm, hCm, surface, cauchy);

        EfficiencySimulator entrance = MakeSimulator(bare, histories, seed, sabotage);
        entrance.ScoreEntranceOnly = true;
        double entErr;
        double aEnt = entrance.Efficiency(energy, out entErr)
                      * (sabotage == "quantum" ? 1.0 / (Math.PI * radiiCm[0] * radiiCm[0]) : 1.0);
        double entSigma = aEnt * entErr / 100.0;
        double entPull = entSigma > 0.0 ? Math.Abs(aEnt - cauchy) / entSigma : double.PositiveInfinity;
        Console.WriteLine("   A_вход = {0:F3} см² ± {1:F2} %  (S/4 = {2:F3}; отношение {3:F4}, {4:F2} σ)",
                          aEnt, entErr, cauchy, aEnt / cauchy, entPull);
        Check("площадь входа = S/4 в пределах 3σ и 1 %",
              entPull <= 3.0 && Math.Abs(aEnt / cauchy - 1.0) <= 0.01, null);

        EfficiencySimulator soft = MakeSimulator(bare, histories, seed, sabotage);
        double softErr;
        double aSoft = soft.TotalEfficiency(30.0, out softErr)
                       * (sabotage == "quantum" ? 1.0 / (Math.PI * radiiCm[0] * radiiCm[0]) : 1.0);
        Console.WriteLine("   ε_полная на 30 кэВ того же кристалла: A = {0:F3} см² ± {1:F2} % — {2:F4} от S/4 (справочно)",
                          aSoft, softErr, aSoft / cauchy);
        Console.WriteLine();

        // (4) матрица: признак нормировки в файле и обратно
        if (matrix)
        {
            Console.WriteLine("(4) матрица сцены поля (3 узла, {0} историй):", Math.Max(1000, histories / 10));
            ResponseMatrixOptions options = new ResponseMatrixOptions
            {
                MinEnergyKev = 30.0,
                MaxEnergyKev = 700.0,
                NodeCount = 3,
                Histories = Math.Max(1000, histories / 10),
                Threads = 1,
                ContinuumErrorTarget = 0.0,
                JointNodes = 0,
                Seed = seed,
            };
            GeometryModel g = iso.Clone();
            g.FieldRadius = radiiCm[0] * GeometryModel.MmPerCm;
            GeometryScenes.Iso(g);
            ResponseMatrix built = ResponseMatrixBuilder.Build(g, options, null, CancellationToken.None);
            string rmx = Path.Combine(outDir, Path.GetFileNameWithoutExtension(inPath) + "_iso.rmx");
            built.Save(rmx);
            ResponseMatrix loaded = ResponseMatrix.Load(rmx);
            Check("построена с признаком PerUnitFluence",
                  built.Normalization == ResponseMatrixNormalization.PerUnitFluence, null);
            Check("после чтения с диска признак тот же (хвост NORM)",
                  loaded != null && loaded.Normalization == ResponseMatrixNormalization.PerUnitFluence, null);
            Check("прочитанная матрица сходится сама с собой (IsValidFor)",
                  loaded != null && loaded.IsValidFor(g, options), null);
            if (loaded != null)
            {
                double rowSum = 0.0;
                int last = loaded.Rows.Length - 1;
                foreach (float v in loaded.Rows[last])
                {
                    rowSum += v;
                }

                Console.WriteLine("   узел {0:F1} кэВ: сумма строки {1:F4} см² (это A_полн узла), пик {2:F4} см²",
                                  loaded.Energies[last], rowSum, loaded.Rows[last][loaded.Rows[last].Length - 1]);
            }

            if (oldRmx != null && File.Exists(oldRmx))
            {
                ResponseMatrix old = ResponseMatrix.Load(oldRmx);
                Check("старый .rmx без хвоста NORM читается как PerEmittedQuantum",
                      old != null && old.Normalization == ResponseMatrixNormalization.PerEmittedQuantum,
                      Path.GetFileName(oldRmx));
            }

            Console.WriteLine();
        }

        if (sabotage != "none")
        {
            Console.WriteLine("ПОРЧА «{0}»: проба ОБЯЗАНА отказать — провалов {1}", sabotage, failed);
        }

        Console.WriteLine(failed == 0 ? "ВСЕ ПРОВЕРКИ ПРОШЛИ" : "ПРОВАЛОВ: " + failed);
        return failed == 0 ? 0 : 1;
    }

    static EfficiencySimulator MakeSimulator(GeometryModel g, int histories, int seed, string sabotage)
    {
        EfficiencySimulator sim = new EfficiencySimulator(g)
        {
            Histories = histories,
            IsoFieldNoCosineWeight = sabotage == "nocos",
        };
        if (seed != 0)
        {
            sim.ResetStream((ulong)seed);
        }

        return sim;
    }
}
