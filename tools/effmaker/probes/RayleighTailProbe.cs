using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace RayleighTailProbe
{
    /// <summary>
    /// РОЗЫГРЫШ КОГЕРЕНТНОГО УГЛА В ПОСЛЕДНЕМ (ЧАСТИЧНОМ) ОТРЕЗКЕ СЕТКИ F² — мерка
    /// `AMBER57` (П122, 22.09.2026).
    ///
    /// Посылка строки: <see cref="ScatteringData.Atom.SampleMomentumTransferSq"/> при
    /// `i == last` брала ширину отрезка `dt = tMax − t[last]`, а значение на правом
    /// конце — `f2[last + 1]`, то есть в узле ЗА `tMax`: наклон F² на частичном
    /// отрезке завышен в `(t[last+1] − t[last])/(tMax − t[last])` раз, тогда как вес
    /// того же отрезка (`PartialIntegral`) считан с верным наклоном. Часть розыгрышей
    /// упирается в зажим `delta > dt` и садится ровно на `t = tMax`, то есть θ = 180°.
    ///
    ///     rayleightailprobe [--z=11,53] [--e=20,33.5,58,60,661.657] [--n=1000000] [--seed=1]
    ///
    /// На каждую пару (Z, E) розыгрыш `n` значений `t` ОДНИМ И ТЕМ ЖЕ потоком
    /// равномерных чисел (xorshift64*, зерно `--seed`), и печатается:
    ///
    ///   * `tMax/t[last]`, во сколько раз ширина полного отрезка больше частичного
    ///     (`ratio` — во столько же раз был завышен наклон);
    ///   * доля розыгрышей в последнем отрезке — розыгрышем и АНАЛИТИЧЕСКИ
    ///     (`head/total`; обе версии кода обязаны совпасть — вес отрезка верен);
    ///   * доля розыгрышей, ЗАЖАТЫХ на `t == tMax` (θ = 180°) — мерка дефекта:
    ///     в исправленном коде она обязана быть ~0;
    ///   * среднее положение розыгрыша ВНУТРИ хвоста в долях его ширины
    ///     (`tpos`), розыгрышем и АНАЛИТИЧЕСКИ — положительный контроль правки: у
    ///     верной плотности они совпадают, у завышенного наклона розыгрыш
    ///     сдвинут к `tMax`;
    ///   * средний cos θ по F² розыгрышем и АНАЛИТИЧЕСКИ (интеграл кусочно-линейной
    ///     плотности);
    ///   * контрольная сумма всех `t` (FNV по битам) — для побитового сравнения.
    ///
    /// КОНТРОЛЬ НЕИЗМЕННОСТИ: на каждый Z проба сама находит энергию, при которой
    /// `tMax` ложится ТОЧНО на узел сетки (вес частичного отрезка ноль, ветвь
    /// `i == last` не достигается) — там ДО и ПОСЛЕ обязаны совпасть побитово.
    ///
    /// ⚠ Томсоновский множитель (1+cos²θ)/2 здесь не доигрывается: он отбором и в
    /// правке не участвует; мерка — только розыгрыш по F², где сидит дефект. Сетка
    /// читается из атома отражением (поля закрытые).
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var zs = new List<int> { 11, 53 };
            var energies = new List<double> { 20.0, 33.5, 58.0, 60.0, 661.657 };
            int n = 1000000;
            ulong seed = 1;
            foreach (string a in args)
            {
                if (a.StartsWith("--z=", StringComparison.Ordinal))
                {
                    zs.Clear();
                    foreach (string p in a.Substring(4).Split(',')) zs.Add(int.Parse(p.Trim(), CultureInfo.InvariantCulture));
                }
                else if (a.StartsWith("--e=", StringComparison.Ordinal))
                {
                    energies.Clear();
                    foreach (string p in a.Substring(4).Split(',')) energies.Add(double.Parse(p.Trim(), CultureInfo.InvariantCulture));
                }
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = ulong.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: {0}", a);
                    return 2;
                }
            }

            FieldInfo fT = typeof(ScatteringData.Atom).GetField("ffT", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fF2 = typeof(ScatteringData.Atom).GetField("ffF2", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fT == null || fF2 == null)
            {
                Console.Error.WriteLine("⛔ полей ffT/ffF2 у ScatteringData.Atom нет — проба устарела");
                return 1;
            }

            Console.WriteLine("розыгрышей на пару {0}, зерно {1}, PhysicsVersion = {2}", n, seed, ResponseMatrix.PhysicsVersion);
            Console.WriteLine();
            Console.WriteLine("{0,3} {1,10} {2,6} {3,8} {4,12} {5,12} {6,12} {7,10} {8,10} {9,12} {10,12} {11,18}",
                              "Z", "E_кэВ", "last", "ratio", "tail_mc", "tail_exact", "clamp180", "tpos_mc", "tpos_exact", "cos_mc", "cos_exact", "fnv(t)");
            foreach (int z in zs)
            {
                ScatteringData.Atom atom = ScatteringData.Of(z);
                if (atom == null)
                {
                    Console.Error.WriteLine("⛔ атома Z={0} в базе нет", z);
                    return 1;
                }

                double[] t = (double[])fT.GetValue(atom);
                double[] f2 = (double[])fF2.GetValue(atom);
                var list = new List<double>(energies);
                double control = ControlEnergy(t, 60.0);
                if (control > 0.0)
                {
                    list.Add(control);
                }

                foreach (double e in list)
                {
                    Measure(atom, t, f2, z, e, n, seed, e == control);
                }
            }

            return 0;
        }

        /// <summary>
        /// Энергия, при которой `tMax = (k·E)²` ложится ТОЧНО на узел сетки `t[k]`,
        /// ближайший к <paramref name="near"/> кэВ: перебор соседних по ulp значений
        /// `E`, пока `k·E` не воспроизведёт `x_k` до бита. Ноль — не нашлось.
        /// </summary>
        static double ControlEnergy(double[] t, double near)
        {
            double k = ScatteringData.InverseCmPerKev;
            int best = -1;
            double bestGap = double.MaxValue;
            for (int i = 1; i < t.Length - 1; i++)
            {
                double e = Math.Sqrt(t[i]) / k;
                if (Math.Abs(e - near) < bestGap)
                {
                    bestGap = Math.Abs(e - near);
                    best = i;
                }
            }

            if (best < 0)
            {
                return 0.0;
            }

            double e0 = Math.Sqrt(t[best]) / k;
            double candidate = e0;
            for (int step = 0; step < 64; step++)
            {
                double up = Step(e0, step), down = Step(e0, -step);
                double xu = k * up, xd = k * down;
                if (xu * xu == t[best]) { candidate = up; return candidate; }
                if (xd * xd == t[best]) { candidate = down; return candidate; }
            }

            return 0.0;
        }

        static double Step(double value, int ulps)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            return BitConverter.Int64BitsToDouble(bits + ulps);
        }

        static void Measure(ScatteringData.Atom atom, double[] t, double[] f2, int z, double e, int n,
                            ulong seed, bool isControl)
        {
            double xMax = ScatteringData.InverseCmPerKev * e;
            double tMax = xMax * xMax;
            int last = Segment(t, tMax);
            double ratio = last >= 0 && tMax > t[last]
                ? (t[last + 1] - t[last]) / (tMax - t[last]) : double.NaN;

            // Аналитика по кусочно-линейной F²: вес хвоста и средний t.
            double total = 0.0, tail = 0.0, moment = 0.0, tailMoment = 0.0;
            for (int i = 0; i <= last; i++)
            {
                double t0 = t[i], t1 = Math.Min(t[i + 1], tMax);
                if (!(t1 > t0)) continue;
                double slope = (f2[i + 1] - f2[i]) / (t[i + 1] - t[i]);
                double a = f2[i];
                double d = t1 - t0;
                double area = a * d + 0.5 * slope * d * d;
                // ∫ t·(a + slope·(t−t0)) dt от t0 до t1
                double m = a * 0.5 * (t1 * t1 - t0 * t0)
                           + slope * ((t1 * t1 * t1 - t0 * t0 * t0) / 3.0 - t0 * 0.5 * (t1 * t1 - t0 * t0));
                total += area;
                moment += m;
                if (i == last) { tail = area; tailMoment = m; }
            }

            double cosExact = total > 0.0 ? 1.0 - 2.0 * (moment / total) / tMax : double.NaN;
            double tailExact = total > 0.0 ? tail / total : double.NaN;
            // Условное среднее положение внутри хвоста, в долях его ширины: (⟨t⟩_хвост − t[last])/(tMax − t[last]).
            double tailPosExact = last >= 0 && tail > 0.0 && tMax > t[last]
                ? (tailMoment / tail - t[last]) / (tMax - t[last]) : double.NaN;

            ulong state = seed * 0x9E3779B97F4A7C15UL + (ulong)z * 0x100000001B3UL + (ulong)BitConverter.DoubleToInt64Bits(e);
            if (state == 0) state = 1;
            ulong fnv = 14695981039346656037UL;
            long inTail = 0, clamped = 0;
            double cosSum = 0.0, tailSum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double u = NextUniform(ref state);
                double tt = atom.SampleMomentumTransferSq(u, tMax);
                if (last >= 0 && tt > t[last]) { inTail++; tailSum += tt; }
                if (tt >= tMax) clamped++;
                double cos = 1.0 - 2.0 * tt / tMax;
                cosSum += cos;
                ulong bits = (ulong)BitConverter.DoubleToInt64Bits(tt);
                for (int b = 0; b < 8; b++)
                {
                    fnv ^= (bits >> (8 * b)) & 0xFF;
                    fnv *= 1099511628211UL;
                }
            }

            double tailPosMc = inTail > 0 && last >= 0 ? (tailSum / inTail - t[last]) / (tMax - t[last]) : double.NaN;
            Console.WriteLine("{0,3} {1,10} {2,6} {3,8} {4,12} {5,12} {6,12} {7,10} {8,10} {9,12} {10,12} {11,18}{12}",
                              z, F(e, 6), last, double.IsNaN(ratio) ? "—" : F(ratio, 3),
                              F((double)inTail / n, 7), F(tailExact, 7), F((double)clamped / n, 7),
                              double.IsNaN(tailPosMc) ? "—" : F(tailPosMc, 4), double.IsNaN(tailPosExact) ? "—" : F(tailPosExact, 4),
                              F(cosSum / n, 7), F(cosExact, 7), fnv.ToString("x16"),
                              isControl ? "  ← контроль: tMax ТОЧНО на узле сетки" : "");
        }

        /// <summary>Тот же `Segment`, что в `ScatteringData` (закрытый там).</summary>
        static int Segment(double[] grid, double x)
        {
            int n = grid.Length;
            if (!(x > grid[0])) return -1;
            if (x >= grid[n - 1]) return n - 2;
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (grid[mid] <= x) lo = mid; else hi = mid;
            }

            return lo;
        }

        /// <summary>xorshift64* → равномерное в [0, 1).</summary>
        static double NextUniform(ref ulong s)
        {
            s ^= s >> 12;
            s ^= s << 25;
            s ^= s >> 27;
            ulong r = s * 2685821657736338717UL;
            return (r >> 11) * (1.0 / 9007199254740992.0);
        }

        // ⛔ Разделитель дробной части — ТОЧКА, культурой инвариантной и явной
        // (правило Amber 05.09.2026).
        static string F(double value, int digits)
        {
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
    }
}
