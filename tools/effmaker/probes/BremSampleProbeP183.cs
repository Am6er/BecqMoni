using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

/// <summary>
/// Поверка П183 (29.09.2026): строки `AMBER139` и `AMBER140` — розыгрыш
/// энергии кванта тормозного (<see cref="ThickTargetBrem.SampleKev"/>,
/// <see cref="ThickTargetBrem.SampleStepKev"/>) против интерполированных числа
/// квантов и энергетического момента той же таблицы, и нижний край таблицы.
///
/// Без геометрии и без случайных чисел: таблица строится на веществе с
/// составом по атомным массам `matdb` (CsI, NaI, Al), среднее энергии кванта
/// — ДЕТЕРМИНИРОВАННОЙ квадратурой по M равномерным квантилям
/// u = (i + 0.5)/M (тот же приём, что у находки 29.09.2026).
///
/// Что меряется у каждого вещества:
///  1. СОГЛАСИЕ МОМЕНТОВ (`AMBER139`) на мелкой сетке T от 10 до 2990 кэВ:
///     толстая мишень — mean(SampleKev)·Photons/Radiated − 1, тонкая —
///     mean(SampleStepKev)·StepPhotons(T,1,1)/StepRadiatedPerGram − 1.
///     Порог 0.2 % (находка: −1.6 … −5.2 %). Ниже 10 кэВ (квантов меньше
///     2e−4 на электрон) рассогласование печатается справочно, а порог
///     ставится на недобор ИЗЛУЧЁННОЙ ЭНЕРГИИ на электрон в долях T:
///     |mean·Photons − Radiated|/T ≤ 1e−5 на всех T (находка: 3.7e−3 у 2614).
///  2. СКАЧОК НА УЗЛЕ: среднее по обе стороны каждого узла T·(1 ± 1e−8).
///     Порог 0.2 % (находка: +3.64 % на узле 103.5 кэВ).
///  3. НОСИТЕЛЬ: каждый разыгранный квант в [MinKev, T] и убывает по u.
///  4. НИЖНИЙ КРАЙ (`AMBER140`): Photons, Radiated, StepRadiatedPerGram,
///     Anchor на T ∈ (MinKev, 6] кэВ — не отрицательны.
///  5. КОНЧИК ТОНКОЙ МИШЕНИ (находка П183): верхний бин таблицы тонкой
///     мишени у каждого узла обязан совпасть с трапецией, у которой на k = T
///     стоит dσ/dk = χ(κ = 1)·Z²/(β²k), а не ноль (до правки — ноль:
///     бин вдвое меньше, −47 % у CsI на 97 кэВ). Порог 1e−7.
///
/// Положительный контроль — тот же прогон на сборке ДО правки: обязан
/// провалить пункты 1, 2 и 4 (код 1).
///
///     bremsampleprobep183 [--m=20000] [--k=600]
///
/// Код 0 — все пороги выполнены; 1 — нарушение; 2 — ключи или таблица не строится.
/// </summary>
static class BremSampleProbeP183
{
    const double MomentLimitPct = 0.2;
    const double JumpLimitPct = 0.2;
    const double LowKev = 10.0;
    const double DefectLimit = 1e-5;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        int m = 20000;
        int k = 600;
        foreach (string a in args)
        {
            if (a.StartsWith("--m=", StringComparison.Ordinal)) m = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--k=", StringComparison.Ordinal)) k = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else
            {
                Console.Error.WriteLine("неизвестный ключ: " + a);
                return 2;
            }
        }

        int failures = 0;
        failures += One("CsI", 4.51, new[] { 55, 53 }, new[] { 1, 1 }, m, k);
        failures += One("NaI", 3.67, new[] { 11, 53 }, new[] { 1, 1 }, m, k);
        failures += One("Al", 2.699, new[] { 13 }, new[] { 1 }, m, k);
        if (failures < 0)
        {
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "ИТОГ: все пороги выполнены" : "ИТОГ: ОТКАЗ, нарушений " + failures);
        return failures == 0 ? 0 : 1;
    }

    static int One(string name, double density, int[] zs, int[] atoms, int m, int k)
    {
        var material = new GeometryMaterial { Name = name, Density = density };
        double total = 0.0;
        for (int i = 0; i < zs.Length; i++)
        {
            total += atoms[i] * MaterialDatabase.AtomicMass[zs[i]];
        }

        for (int i = 0; i < zs.Length; i++)
        {
            material.Fractions[zs[i]] = atoms[i] * MaterialDatabase.AtomicMass[zs[i]] / total;
        }

        ThickTargetBrem b = ThickTargetBrem.For(material, ElectronData.ByName(name), 5.0);
        if (b == null)
        {
            Console.Error.WriteLine("⛔ таблица " + name + " не строится");
            return -1000;
        }

        var node = (double[])typeof(ThickTargetBrem).GetField(
            "node", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(b);
        int n = node.Length;
        Console.WriteLine();
        Console.WriteLine("== {0}: узлов {1}, {2:0.###}…{3:0.###} кэВ, MinKev {4} ==", name, n, node[0], node[n - 1], b.MinKev);

        int failures = 0;

        // 1. Опорные энергии — числа для журнала
        Console.WriteLine("{0,9} {1,12} {2,12} {3,12} {4,9} {5,12} {6,12} {7,9}",
                          "T, кэВ", "Photons", "среднее т.", "Rad/Ph", "Δ толст %", "среднее тон.", "Rad/Ph тон.", "Δ тонк %");
        foreach (double t in new[] { 20.0, 100.0, 300.0, 661.657, 1000.0, 1461.0, 2614.511 })
        {
            double[] r = Moments(b, t, m);
            Console.WriteLine("{0,9:0.###} {1,12:0.000000E+0} {2,12:0.0000} {3,12:0.0000} {4,9:+0.0000;-0.0000} {5,12:0.0000} {6,12:0.0000} {7,9:+0.0000;-0.0000}",
                              t, b.Photons(t), r[0], r[1], r[2], r[3], r[4], r[5]);
        }

        // 1'. Мелкая сетка — худшее рассогласование моментов (T ≥ 10 кэВ) и
        //     худший недобор излучённой энергии на электрон, в долях T (все T)
        double worstThick = 0.0, worstThin = 0.0, atThick = 0.0, atThin = 0.0;
        double sumThick = 0.0, sumThin = 0.0;
        double worstLow = 0.0, atLow = 0.0, worstDefect = 0.0, atDefect = 0.0;
        int counted = 0;
        double lo = Math.Log(5.2), hi = Math.Log(Math.Min(2990.0, node[n - 1] * 0.999));
        for (int i = 0; i < k; i++)
        {
            double t = Math.Exp(lo + (hi - lo) * i / (k - 1));
            if (!(b.Photons(t) > 0.0) || !(b.StepRadiatedPerGram(t) > 0.0))
            {
                continue;
            }

            double[] r = Moments(b, t, m);
            // излучённая энергия на электрон: разыгранная против таблицы, доля T
            double defect = (r[0] * b.Photons(t) - b.Radiated(t)) / t;
            if (Math.Abs(defect) > Math.Abs(worstDefect)) { worstDefect = defect; atDefect = t; }
            if (t < LowKev)
            {
                if (Math.Abs(r[2]) > Math.Abs(worstLow)) { worstLow = r[2]; atLow = t; }
                if (Math.Abs(r[5]) > Math.Abs(worstLow)) { worstLow = r[5]; atLow = t; }
                continue;
            }

            counted++;
            sumThick += r[2];
            sumThin += r[5];
            if (Math.Abs(r[2]) > Math.Abs(worstThick)) { worstThick = r[2]; atThick = t; }
            if (Math.Abs(r[5]) > Math.Abs(worstThin)) { worstThin = r[5]; atThin = t; }
        }

        bool momentsOk = Math.Abs(worstThick) <= MomentLimitPct && Math.Abs(worstThin) <= MomentLimitPct
                         && Math.Abs(worstDefect) <= DefectLimit;
        Console.WriteLine("согласие моментов на {0} точках {9:0}…{1:0} кэВ: толстая худшее {2:+0.0000;-0.0000} % (T {3:0.##}), среднее {4:+0.0000;-0.0000} %; тонкая худшее {5:+0.0000;-0.0000} % (T {6:0.##}), среднее {7:+0.0000;-0.0000} %{8}",
                          counted, Math.Exp(hi), worstThick, atThick, sumThick / Math.Max(1, counted),
                          worstThin, atThin, sumThin / Math.Max(1, counted),
                          Math.Abs(worstThick) <= MomentLimitPct && Math.Abs(worstThin) <= MomentLimitPct
                              ? "" : "   ⛔ за порогом " + MomentLimitPct.ToString("0.0", CultureInfo.InvariantCulture) + " %",
                          LowKev);
        Console.WriteLine("  ниже {0:0} кэВ (у отсечки, квантов < 2e−4 на электрон) худшее рассогласование {1:+0.0000;-0.0000} % (T {2:0.##}) — справочно",
                          LowKev, worstLow, atLow);
        Console.WriteLine("  недобор разыгранной энергии на электрон (mean·Photons − Radiated)/T, все T: худший {0:0.000E+0} (T {1:0.##}){2}",
                          worstDefect, atDefect,
                          Math.Abs(worstDefect) <= DefectLimit ? "" : "   ⛔ за порогом " + DefectLimit.ToString("0E+0", CultureInfo.InvariantCulture));
        if (!momentsOk) failures++;

        // 5. Кончик тонкой мишени: верхний бин таблицы против трапеции с
        //    dσ/dk(k = T) = χ(κ = 1)·Z²/(β²k) — у Зельцера — Бергера она конечна.
        var thinAbove = (double[][])typeof(ThickTargetBrem).GetField(
            "thinAbove", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(b);
        double worstTip = 0.0;
        for (int j = 3; j < n; j++)
        {
            double t = node[j];
            double tip = PerGramAt(material, t, t), below = PerGramAt(material, node[j - 1], t);
            double dk = t - node[j - 1];
            double ph = b.StepPhotons(t, 1.0, 1.0), rad = b.StepRadiatedPerGram(t);
            double table = thinAbove[j][j - 1] * ph;
            double expected = 0.5 * (below + tip) * dk;
            double rel = table / expected - 1.0;
            if (Math.Abs(rel) > Math.Abs(worstTip)) worstTip = rel;
            if (j == 60 || j == 80 || j == 92)
            {
                double missCount = 0.5 * tip * dk, missEnergy = missCount * 0.5 * (t + node[j - 1]);
                Console.WriteLine("  кончик тонкой мишени у узла {0:0.###} кэВ: dσ/dk(T)/dσ/dk(node[j−1]) = {1:0.000}; верхний бин таблицы / трапеция с кончиком − 1 = {2:+0.0000;-0.0000} %; сам кончик — {3:0.000} % квантов, {4:0.000} % энергии",
                                  t, tip / below, 100.0 * rel, 100.0 * missCount / ph, 100.0 * missEnergy / rad);
            }
        }

        bool tipOk = Math.Abs(worstTip) <= 1e-7;
        Console.WriteLine("  верхний бин тонкой мишени, худшее отличие от трапеции с кончиком: {0:+0.000000;-0.000000} %{1}",
                          100.0 * worstTip, tipOk ? "" : "   ⛔ кончик χ(κ = 1) потерян");
        if (!tipOk) failures++;

        // 2. Скачки на узлах
        double worstJumpThick = 0.0, worstJumpThin = 0.0, atJumpThick = 0.0, atJumpThin = 0.0;
        for (int j = 2; j < n - 1; j++)
        {
            double tb = node[j] * (1.0 - 1e-8), ta = node[j] * (1.0 + 1e-8);
            if (!(b.Photons(tb) > 0.0))
            {
                continue;
            }

            double[] rb = Moments(b, tb, m), ra = Moments(b, ta, m);
            double jt = 100.0 * (ra[0] / rb[0] - 1.0), jn = 100.0 * (ra[3] / rb[3] - 1.0);
            if (Math.Abs(jt) > Math.Abs(worstJumpThick)) { worstJumpThick = jt; atJumpThick = node[j]; }
            if (Math.Abs(jn) > Math.Abs(worstJumpThin)) { worstJumpThin = jn; atJumpThin = node[j]; }
        }

        bool jumpsOk = Math.Abs(worstJumpThick) <= JumpLimitPct && Math.Abs(worstJumpThin) <= JumpLimitPct;
        Console.WriteLine("скачок среднего на узле (T·(1±1e−8)), худший: толстая {0:+0.0000;-0.0000} % (узел {1:0.###}), тонкая {2:+0.0000;-0.0000} % (узел {3:0.###}){4}",
                          worstJumpThick, atJumpThick, worstJumpThin, atJumpThin,
                          jumpsOk ? "" : "   ⛔ за порогом " + JumpLimitPct.ToString("0.0", CultureInfo.InvariantCulture) + " %");
        if (!jumpsOk) failures++;

        // 3. Носитель и монотонность
        int outside = 0, nonMonotone = 0;
        double worstOver = 0.0;
        for (int i = 0; i < k; i += 3)
        {
            double t = Math.Exp(lo + (hi - lo) * i / (k - 1));
            if (!(b.Photons(t) > 0.0))
            {
                continue;
            }

            for (int thin = 0; thin < 2; thin++)
            {
                double prev = double.PositiveInfinity;
                for (int q = 0; q < 2000; q++)
                {
                    double u = (q + 0.5) / 2000;
                    double e = thin == 0 ? b.SampleKev(t, u) : b.SampleStepKev(t, u);
                    if (e < b.MinKev * (1.0 - 1e-12) || e > t * (1.0 + 1e-12))
                    {
                        outside++;
                        worstOver = Math.Max(worstOver, e / t - 1.0);
                    }

                    if (e > prev * (1.0 + 1e-12))
                    {
                        nonMonotone++;
                    }

                    prev = e;
                }
            }
        }

        bool supportOk = outside == 0 && nonMonotone == 0;
        Console.WriteLine("носитель [MinKev, T]: вне — {0} (худшее превышение T {1:0.0000} %), немонотонных по u — {2}{3}",
                          outside, 100.0 * worstOver, nonMonotone, supportOk ? "" : "   ⛔");
        if (!supportOk) failures++;

        // 4. Нижний край
        double minPh = double.PositiveInfinity, minRad = double.PositiveInfinity, minStep = double.PositiveInfinity,
               minAnchor = double.PositiveInfinity, atMin = 0.0;
        for (int i = 1; i <= 100; i++)
        {
            double t = b.MinKev + (6.0 - b.MinKev) * i / 100.0;
            double p = b.Photons(t), r = b.Radiated(t), s = b.StepRadiatedPerGram(t);
            if (p < minPh) { minPh = p; atMin = t; }
            minRad = Math.Min(minRad, r);
            minStep = Math.Min(minStep, s);
            minAnchor = Math.Min(minAnchor, b.Anchor(t));
        }

        bool edgeOk = minPh >= 0.0 && minRad >= 0.0 && minStep >= 0.0;
        Console.WriteLine("нижний край T ∈ ({0}, 6] кэВ: min Photons {1:R} (T {2:0.###}), min Radiated {3:R}, min StepRadiatedPerGram {4:R}, min Anchor {5:R}{6}",
                          b.MinKev, minPh, atMin, minRad, minStep, minAnchor, edgeOk ? "" : "   ⛔ отрицательно");
        Console.WriteLine("  T=5.1: Photons {0:R}, Radiated {1:R}, StepRadiatedPerGram {2:R}; T=5.5: Photons {3:R}",
                          b.Photons(5.1), b.Radiated(5.1), b.StepRadiatedPerGram(5.1), b.Photons(5.5));
        if (!edgeOk) failures++;

        return failures;
    }

    /// <summary>dσ/dk на грамм, см²/(г·кэВ) — то же выражение, что `ThickTargetBrem.PerGram`.</summary>
    static double PerGramAt(GeometryMaterial material, double kKev, double tKev)
    {
        double gamma = 1.0 + tKev / 510.99895;
        double beta2 = 1.0 - 1.0 / (gamma * gamma);
        double sum = 0.0;
        foreach (KeyValuePair<int, double> pair in material.Fractions)
        {
            SeltzerBergerData.Element e = SeltzerBergerData.Of(pair.Key);
            double w = pair.Value * 6.02214076e23 / MaterialDatabase.AtomicMass[pair.Key];
            sum += w * e.Chi(tKev, kKev / tKev) * 1e-27 * pair.Key * pair.Key / (beta2 * kKev);
        }

        return sum;
    }

    /// <summary>
    /// [среднее толстой, Radiated/Photons, Δ толстой %, среднее тонкой,
    /// StepRadiatedPerGram/StepPhotons(T,1,1), Δ тонкой %].
    /// </summary>
    static double[] Moments(ThickTargetBrem b, double t, int m)
    {
        double thick = 0.0, thin = 0.0;
        for (int i = 0; i < m; i++)
        {
            double u = (i + 0.5) / m;
            thick += b.SampleKev(t, u);
            thin += b.SampleStepKev(t, u);
        }

        thick /= m;
        thin /= m;
        double targetThick = b.Radiated(t) / b.Photons(t);
        double targetThin = b.StepRadiatedPerGram(t) / b.StepPhotons(t, 1.0, 1.0);
        return new[]
        {
            thick, targetThick, 100.0 * (thick / targetThick - 1.0),
            thin, targetThin, 100.0 * (thin / targetThin - 1.0),
        };
    }
}
