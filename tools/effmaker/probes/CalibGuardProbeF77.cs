// ═══════════════════════════════════════════════════════════════════════════
//  Полоса F77, 06.09.2026. СТОРОЖ ИЗГИБА ЭНЕРГОКАЛИБРОВКИ (`S42`)
// ═══════════════════════════════════════════════════════════════════════════
//
//  ЧТО ИЗМЕРЯЕТСЯ. Кнопка «Рассчитать» подгоняет шкалу по опорным точкам
//  человека. До правки степень бралась СИЛОЙ: в настройке прибора пять точек
//  и больше означали четвёртую степень всегда, то есть интерполяцию через все
//  опоры без единой свободной степени. Такая кривая проходит через свои точки
//  и врёт там, где точек нет, — а невязка подгонки этого не показывает по
//  построению. После правки степень ЗАПРАШИВАЕТСЯ: принимается наибольшая, чья
//  кривая годна и не уходит от прямой по тем же опорам дальше допустимого
//  (`CalibrationSolver.SolveGuarded`, перенос `fit_ecal`/`bend_ok` из
//  `tools/CORPUS/scripts/calibrate.py`).
//
//  ⛔ МЕРКА ВНЕШНЯЯ, а не невязка подгонки: канал каждой известной линии
//  определён ОДИН РАЗ по данным (стадией 1 конвейера корпуса, выгрузка
//  `handover/f77-s42/dump_anchors_f77.py`), и каждый вариант шкалы оценивается
//  на этом НЕПОДВИЖНОМ наборе — насколько его энергия в канале линии отстоит
//  от табличной. Невязкой подгонки мерить нельзя: она и есть то, чем
//  интерполяция себя обманывает.
//
//  ТРИ ПОЛОЖИТЕЛЬНЫХ КОНТРОЛЯ (без них числа корпуса ничего не значат):
//    1. заведомо кривая подгонка ОБЯЗАНА понижаться — в том числе дословный
//       случай из докстринга `calibrate.py` (квадратичная по опорам 689…2510,
//       дающая 5133 кэВ на канале 8191);
//    2. здоровая подгонка ОБЯЗАНА не двинуться — ни ранг, ни коэффициенты,
//       побитово (синтетика плюс все спектры корпуса, где ранг не изменился);
//    3. мерка ОБЯЗАНА видеть порчу — заведомо испорченная шкала (усиление
//       сдвинуто на 1 %) обязана дать худшее число.
//
//    CalibGuardProbeF77.exe --anchors=<anchors.csv> --lines=<lines.csv>
//                           [--order=auto|1|2|3|4] [--csv=<файл отчёта>]
//
//  Ожидание: «ВСЕ ТРИ КОНТРОЛЯ СОШЛИСЬ», код 0.
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BecquerelMonitor;
using BecquerelMonitor.Utils;

static class CalibGuardProbeF77
{
    class Spec
    {
        public string Key;
        public int Channels;
        public List<CalibrationPoint> Anchors = new List<CalibrationPoint>();
        public List<double[]> Lines = new List<double[]>();   // {энергия, канал}
    }

    static int failures;
    static int forceChannels;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string anchorsPath = null, linesPath = null, csvPath = null, orderKey = "auto";
        // Путь настройки прибора (`DeviceConfigForm`) числа каналов не знает и
        // судит шкалу по умолчанию 8192 — этот ключ повторяет тот случай.
        forceChannels = 0;
        foreach (string a in args)
        {
            if (a.StartsWith("--anchors=")) anchorsPath = a.Substring(10);
            else if (a.StartsWith("--lines=")) linesPath = a.Substring(8);
            else if (a.StartsWith("--csv=")) csvPath = a.Substring(6);
            else if (a.StartsWith("--order=")) orderKey = a.Substring(8);
            else if (a.StartsWith("--channels=")) forceChannels = int.Parse(a.Substring(11), CultureInfo.InvariantCulture);
        }
        if (anchorsPath == null || linesPath == null)
        {
            Console.WriteLine("нужны --anchors=<файл> и --lines=<файл>");
            return 2;
        }

        Console.WriteLine("сторож изгиба: MaxBend = " +
                          CalibrationSolver.MaxBend.ToString("R", CultureInfo.InvariantCulture) +
                          ", пол допуска " +
                          CalibrationSolver.BendFloorKeV.ToString("R", CultureInfo.InvariantCulture) + " кэВ");

        Controls();
        Corpus(anchorsPath, linesPath, orderKey, csvPath);

        Console.WriteLine();
        if (failures == 0) Console.WriteLine("ВСЕ ТРИ КОНТРОЛЯ СОШЛИСЬ");
        else Console.WriteLine("ОТКАЗОВ: " + failures);
        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    //  Контроли
    // ------------------------------------------------------------------

    static void Controls()
    {
        Console.WriteLine();
        Console.WriteLine("=== контроль 1: заведомо кривая подгонка обязана понижаться ===");

        // Дословный случай из докстринга `calibrate.py`: пять опор между
        // каналами 689 и 2510 на квадратичной 41.9 + 0.257·ch + 4.5e-5·ch²,
        // которая на канале 8191 даёт 5133 кэВ — процентов на 70 выше того,
        // что говорит усиление.
        int[] chs = { 689, 1100, 1600, 2100, 2510 };
        List<CalibrationPoint> sick = new List<CalibrationPoint>();
        foreach (int ch in chs)
        {
            double e = 41.9 + 0.257 * ch + 4.5e-5 * ch * ch;
            sick.Add(new CalibrationPoint(ch, (decimal)e, 1000));
        }
        Downgrades("квадратичная 41.9+0.257ch+4.5e-5ch² по опорам 689…2510, шкала 8192", sick, 2, 8192, true);

        // Пять точек на прямой, одна сбита на 8 кэВ: четвёртая степень пройдёт
        // через все пять и уедет за опорами.
        int[] chs2 = { 200, 900, 1700, 2600, 3300 };
        double[] es2 = { 80.0, 360.0, 680.0 + 8.0, 1040.0, 1320.0 };
        List<CalibrationPoint> wobbly = new List<CalibrationPoint>();
        for (int i = 0; i < chs2.Length; i++) wobbly.Add(new CalibrationPoint(chs2[i], (decimal)es2[i], 1000));
        Downgrades("пять опор на прямой с одной сбитой точкой, запрошена степень 4", wobbly, 4, 8192, true);

        Console.WriteLine();
        Console.WriteLine("=== контроль 2: здоровая подгонка не двигается, побитово ===");

        // Честная квадратичная с широким охватом: 0.4·ch + 3e-6·ch².
        int[] chs3 = { 120, 900, 2000, 4000, 6000, 7800 };
        List<CalibrationPoint> healthy = new List<CalibrationPoint>();
        foreach (int ch in chs3)
        {
            double e = 2.0 + 0.4 * ch + 3e-6 * ch * ch;
            healthy.Add(new CalibrationPoint(ch, (decimal)e, 1000));
        }
        SameBits("честная квадратичная, шесть опор по всей шкале, степень 2", healthy, 2, 8192);
        SameBits("она же, запрошена степень 1", healthy, 1, 8192);
    }

    static void Downgrades(string name, List<CalibrationPoint> points, int order, int channels, bool mustDrop)
    {
        double[] before = null;
        try { before = CalibrationSolver.Solve(points, order); }
        catch (Exception) { }
        int usedOrder;
        double[] after = CalibrationSolver.SolveGuarded(points, order, channels, false, out usedOrder);

        int orderBefore = before == null ? -1 : before.Length - 1;
        double topBefore = before == null ? double.NaN : Poly(before, channels - 1);
        double topAfter = after == null ? double.NaN : Poly(after, channels - 1);
        Console.WriteLine("  " + name);
        Console.WriteLine("    было: степень " + orderBefore.ToString(CultureInfo.InvariantCulture) +
                          ", на канале " + (channels - 1).ToString(CultureInfo.InvariantCulture) + " -> " +
                          topBefore.ToString("F1", CultureInfo.InvariantCulture) + " кэВ");
        Console.WriteLine("    стало: степень " + usedOrder.ToString(CultureInfo.InvariantCulture) +
                          ", на канале " + (channels - 1).ToString(CultureInfo.InvariantCulture) + " -> " +
                          topAfter.ToString("F1", CultureInfo.InvariantCulture) + " кэВ");
        bool dropped = after != null && usedOrder < orderBefore;
        Verdict(mustDrop ? "степень понижена" : "степень сохранена", mustDrop, dropped);
    }

    static void SameBits(string name, List<CalibrationPoint> points, int order, int channels)
    {
        double[] before = CalibrationSolver.Solve(points, order);
        int usedOrder;
        double[] after = CalibrationSolver.SolveGuarded(points, order, channels, false, out usedOrder);
        bool same = before != null && after != null && before.Length == after.Length && usedOrder == order;
        if (same)
        {
            for (int i = 0; i < before.Length; i++)
            {
                if (BitConverter.DoubleToInt64Bits(before[i]) != BitConverter.DoubleToInt64Bits(after[i]))
                {
                    same = false;
                    break;
                }
            }
        }
        Console.WriteLine("  " + name + " -> [" + Show(after) + "]");
        Verdict("совпало побитово", true, same);
    }

    // ------------------------------------------------------------------
    //  Корпус
    // ------------------------------------------------------------------

    static void Corpus(string anchorsPath, string linesPath, string orderKey, string csvPath)
    {
        Dictionary<string, Spec> specs = Load(anchorsPath, linesPath);
        Console.WriteLine();
        Console.WriteLine("=== корпус: " + specs.Count.ToString(CultureInfo.InvariantCulture) +
                          " спектров, опор " + specs.Values.Sum(s => s.Anchors.Count).ToString(CultureInfo.InvariantCulture) +
                          ", линий мерки " + specs.Values.Sum(s => s.Lines.Count).ToString(CultureInfo.InvariantCulture) + " ===");

        int moved = 0, sameBits = 0, refusedBefore = 0, refusedAfter = 0;
        int better = 0, worse = 0, equal = 0;
        double sumBefore = 0.0, sumAfter = 0.0, sumOutBefore = 0.0, sumOutAfter = 0.0, sumOutTangent = 0.0;
        double sumSpoiled = 0.0;
        List<string> rows = new List<string>();
        rows.Add("key,channels,anchors,lines,order_req,order_before,order_after,miss_before,miss_after,miss_out_before,miss_out_after,worst_before,worst_after");

        foreach (string key in specs.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            Spec s = specs[key];
            List<CalibrationPoint> points = s.Anchors.OrderBy(p => p.Channel).ToList();
            if (points.Count < 2) continue;

            int requested;
            if (orderKey == "auto") requested = points.Count >= 5 ? 4 : points.Count - 1;
            else requested = int.Parse(orderKey, CultureInfo.InvariantCulture);

            double[] before = null;
            try { before = CalibrationSolver.Solve(points, requested); }
            catch (Exception) { }
            if (before != null && !Finite(before)) before = null;
            int orderBefore = before == null ? -1 : before.Length - 1;
            if (before == null) refusedBefore++;

            int orderAfter;
            int channels = forceChannels > 0 ? forceChannels : s.Channels;
            double[] after = CalibrationSolver.SolveGuarded(points, requested, channels, false, out orderAfter);
            if (after == null) { refusedAfter++; orderAfter = -1; }

            double chLo = points.Min(p => (double)p.Channel);
            double chHi = points.Max(p => (double)p.Channel);

            double mb, mba, ob, oa, wb, wa;
            Miss(before, s.Lines, chLo, chHi, out mb, out wb, out ob);
            Miss(after, s.Lines, chLo, chHi, out mba, out wa, out oa);

            sumBefore += mb; sumAfter += mba;
            sumOutBefore += ob; sumOutAfter += oa;

            // ЦЕНА ВТОРОЙ ПОЛОВИНЫ `S42` числом: сколько ещё снимет
            // касательная за крайними опорами, если её вообще делать. Здесь
            // она только СЧИТАЕТСЯ на принятой шкале — в классе калибровки её
            // нет и полей границ тоже (они не сериализуются, см. опись
            // `tools/pie/s42-calibration-readers.md`).
            sumOutTangent += MissTangent(after, s.Lines, chLo, chHi);

            // Контроль 3 живёт здесь: та же принятая шкала с усилением,
            // сдвинутым на 1 %, обязана дать худшее число.
            if (after != null)
            {
                double[] spoiled = (double[])after.Clone();
                spoiled[1] *= 1.01;
                double sm, sw, so;
                Miss(spoiled, s.Lines, chLo, chHi, out sm, out sw, out so);
                sumSpoiled += sm;
            }

            bool bits = before != null && after != null && before.Length == after.Length;
            if (bits)
            {
                for (int i = 0; i < before.Length; i++)
                {
                    if (BitConverter.DoubleToInt64Bits(before[i]) != BitConverter.DoubleToInt64Bits(after[i]))
                    {
                        bits = false;
                        break;
                    }
                }
            }
            if (bits) sameBits++;
            else
            {
                moved++;
                double d = mba - mb;
                string verdict = Math.Abs(d) < 1e-9 ? "ровно" : (d < 0 ? "ЛУЧШЕ" : "хуже");
                if (Math.Abs(d) < 1e-9) equal++; else if (d < 0) better++; else worse++;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-24} опор {1}, шкала {2,5}: степень {3} -> {4}; промах {5:F2} -> {6:F2} кэВ ({7}), вне опор {8:F2} -> {9:F2}",
                    key, points.Count, s.Channels, orderBefore, orderAfter, mb, mba, verdict, ob, oa));
            }

            rows.Add(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4}",
                key, s.Channels, points.Count, s.Lines.Count, requested, orderBefore, orderAfter,
                mb, mba, ob, oa, wb, wa));
        }

        Console.WriteLine();
        Console.WriteLine("  спектров без изменений (побитово): " + sameBits.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  спектров сдвинулось: " + moved.ToString(CultureInfo.InvariantCulture) +
                          " — лучше " + better.ToString(CultureInfo.InvariantCulture) +
                          ", хуже " + worse.ToString(CultureInfo.InvariantCulture) +
                          ", ровно " + equal.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("  отказ подгонки: было " + refusedBefore.ToString(CultureInfo.InvariantCulture) +
                          ", стало " + refusedAfter.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  суммарный промах по всем линиям: {0:F1} -> {1:F1} кэВ", sumBefore, sumAfter));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  он же вне отрезка опор:          {0:F1} -> {1:F1} кэВ", sumOutBefore, sumOutAfter));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  цена второй половины `S42`: касательная за крайними опорами дала бы вне опор {0:F1} кэВ ({1:+0.0;-0.0} против принятой)",
            sumOutTangent, sumOutTangent - sumOutAfter));

        Console.WriteLine();
        Console.WriteLine("=== контроль 3: видит ли мерка порчу (усиление сдвинуто на 1 %) ===");
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  принятая шкала {0:F1} кэВ, испорченная {1:F1} кэВ", sumAfter, sumSpoiled));
        Verdict("испорченная хуже принятой", true, sumSpoiled > sumAfter);

        if (!string.IsNullOrEmpty(csvPath))
        {
            File.WriteAllLines(csvPath, rows.ToArray(), new UTF8Encoding(false));
            Console.WriteLine("  отчёт: " + csvPath);
        }
    }

    static void Miss(double[] coefficients, List<double[]> lines, double chLo, double chHi,
                     out double sum, out double worst, out double outside)
    {
        sum = 0.0; worst = 0.0; outside = 0.0;
        if (coefficients == null)
        {
            // Отказ подгонки — не «ноль промаха»: чтобы отказ не выглядел
            // победой, он считается промахом в полную энергию линии.
            foreach (double[] l in lines)
            {
                sum += l[0];
                outside += (l[1] < chLo || l[1] > chHi) ? l[0] : 0.0;
                if (l[0] > worst) worst = l[0];
            }
            return;
        }
        foreach (double[] l in lines)
        {
            double miss = Math.Abs(Poly(coefficients, l[1]) - l[0]);
            sum += miss;
            if (miss > worst) worst = miss;
            if (l[1] < chLo || l[1] > chHi) outside += miss;
        }
    }

    /// <summary>
    /// Промах ТОЛЬКО по линиям вне отрезка опор, если за крайними опорами
    /// кривую продолжить КАСАТЕЛЬНОЙ (вторая половина `S42`). Считается на
    /// принятой шкале и ничего в приложении не меняет — это оценка выигрыша,
    /// а не реализация.
    /// </summary>
    static double MissTangent(double[] coefficients, List<double[]> lines, double chLo, double chHi)
    {
        if (coefficients == null) return lines.Sum(l => (l[1] < chLo || l[1] > chHi) ? l[0] : 0.0);
        double sum = 0.0;
        foreach (double[] l in lines)
        {
            double ch = l[1];
            if (ch >= chLo && ch <= chHi) continue;
            double edge = ch < chLo ? chLo : chHi;
            double e = Poly(coefficients, edge) + Slope(coefficients, edge) * (ch - edge);
            sum += Math.Abs(e - l[0]);
        }
        return sum;
    }

    static double Slope(double[] coefficients, double x)
    {
        double v = 0.0;
        for (int i = 1; i < coefficients.Length; i++) v += i * coefficients[i] * Math.Pow(x, i - 1);
        return v;
    }

    static Dictionary<string, Spec> Load(string anchorsPath, string linesPath)
    {
        Dictionary<string, Spec> specs = new Dictionary<string, Spec>(StringComparer.Ordinal);
        bool head = true;
        foreach (string line in File.ReadAllLines(anchorsPath))
        {
            if (head) { head = false; continue; }
            if (line.Length == 0) continue;
            string[] f = line.Split(',');
            Spec s;
            if (!specs.TryGetValue(f[0], out s))
            {
                s = new Spec();
                s.Key = f[0];
                s.Channels = int.Parse(f[1], CultureInfo.InvariantCulture);
                specs[f[0]] = s;
            }
            s.Anchors.Add(new CalibrationPoint(int.Parse(f[2], CultureInfo.InvariantCulture),
                                               decimal.Parse(f[3], CultureInfo.InvariantCulture),
                                               int.Parse(f[4], CultureInfo.InvariantCulture)));
        }
        head = true;
        foreach (string line in File.ReadAllLines(linesPath))
        {
            if (head) { head = false; continue; }
            if (line.Length == 0) continue;
            string[] f = line.Split(',');
            Spec s;
            if (!specs.TryGetValue(f[0], out s)) continue;
            s.Lines.Add(new double[] { double.Parse(f[1], CultureInfo.InvariantCulture),
                                       double.Parse(f[2], CultureInfo.InvariantCulture) });
        }
        return specs;
    }

    static double Poly(double[] coefficients, double x)
    {
        double v = 0.0;
        for (int i = coefficients.Length - 1; i >= 0; i--) v = v * x + coefficients[i];
        return v;
    }

    static bool Finite(double[] v)
    {
        foreach (double d in v) if (double.IsNaN(d) || double.IsInfinity(d)) return false;
        return true;
    }

    static string Show(double[] v)
    {
        if (v == null) return "null";
        return string.Join(", ", v.Select(d => d.ToString("R", CultureInfo.InvariantCulture)).ToArray());
    }

    static void Verdict(string what, bool expected, bool actual)
    {
        if (expected == actual) Console.WriteLine("    ✅ " + what);
        else { Console.WriteLine("    ⛔ ОТКАЗ: " + what + " — не выполнено"); failures++; }
    }
}
