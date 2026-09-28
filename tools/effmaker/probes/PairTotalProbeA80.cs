using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace PairTotalProbeA80
{
    /// <summary>
    /// ⛔ `AMBER80` (полоса П147, 24.09.2026, физика 24): ПАРЫ В ПОЛНОМ ОСЛАБЛЕНИИ.
    ///
    /// После `AMBER74` (П132) полное ослабление слоёв считается СУММОЙ КАНАЛОВ
    /// (<see cref="PartialCrossSections.MassTotal(MaterialDatabase.Element, int, int, double, double)"/>),
    /// но пары в ней — обычной лог-лог хордой по сумме ядерного канала и
    /// triplet, без пороговой формы XCOM `σ/(1 − E₀/E)³` (`S121`). Розыгрыш
    /// канала в слоях и в кристалле при ключе
    /// <see cref="EfficiencySimulator.XcomPairThreshold"/> берёт пары
    /// ПОРОГОВОЙ формой — полное и розыгрыш расходятся, а разность уходит в
    /// «остаток на фото».
    ///
    /// Проба печатает, от одних и тех же узлов `matdb`:
    ///
    /// 1. ОПОРА — поканальный кубический сплайн в лог-лог (метод XCOM; пары —
    ///    сплайн пороговой величины по открытым узлам) против двух правил
    ///    полного: «хорда» (пары лог-лог по сумме, то, что в коде сейчас без
    ///    ключа) и «порог» (пары `PairChannel` `S121`) — в серединах участков
    ///    выше 1.5 МэВ и на 2614.5 кэВ, для Pb, I, Cs, Fe, O.
    /// 2. ВЫБРОС УЗЛА — узел убран, восстановлен по соседям обоими правилами,
    ///    сравнён с табличным значением (истина известна точно).
    /// 3. СОГЛАСИЕ С РОЗЫГРЫШЕМ при ключе ВКЛ: полное минус (когерентное +
    ///    некогерентное + пары пороговой формой) = то, что розыгрыш слоя
    ///    отдаёт фотоэффекту; против честного фотоэффекта — в процентах.
    /// 4. КОД: есть ли у `MassTotal` вход с ключом пороговой формы
    ///    (`MassTotal(…, bool thresholdPair)`, ищется отражением, чтобы проба
    ///    собиралась и на коде ДО правки), и совпадает ли он ДО БИТА с
    ///    суммой каналов пробы при ключе ВКЛ и с прежним `MassTotal` при ключе
    ///    ВЫКЛ — по всем элементам, всем узлам и серединам участков.
    ///
    ///     pairtotalprobea80
    ///
    /// 5. УМОЛЧАНИЕ КЛЮЧА (физика 24, решение Amber 24.09.2026 «ВКЛ в физике 24
    ///    (Рекомендую)»): склад, симулятор и все области сцены — ВКЛ, и полное Pb на
    ///    2614.5 кэВ путём по умолчанию против сплайна.
    ///
    /// Код 0 — вход с ключом есть, ВКЛ согласован с розыгрышем до 1e-12,
    /// ВЫКЛ побитово прежний, умолчание ВКЛ везде; 1 — нет (на коде до правки —
    /// ОЖИДАЕМО).
    /// </summary>
    static class Program
    {
        static readonly int[] Zs = { 82, 53, 55, 26, 8 };
        static readonly string[] Names = { "Pb", "I", "Cs", "Fe", "O" };

        static string F(double v, int d)
        {
            return v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        static double Ch(MaterialDatabase.Element el, int i, PhotonProcess p, bool threshold)
        {
            // значение канала в узле i (массовое, см2/г)
            switch (p)
            {
                case PhotonProcess.Coherent: return el.Channels[0][i];
                case PhotonProcess.Incoherent: return el.Channels[1][i];
                case PhotonProcess.Photoelectric: return el.Channels[2][i];
                default: return el.Channels[3][i] + el.Channels[4][i];
            }
        }

        /// <summary>Полное двумя правилами по паре узлов lo/hi (любых, не только соседних).</summary>
        static double Total(MaterialDatabase.Element el, int lo, int hi, double e, bool threshold)
        {
            double le = Math.Log(e);
            return PartialCrossSections.MassCrossSection(el, lo, hi, e, le, PhotonProcess.Coherent)
                 + PartialCrossSections.MassCrossSection(el, lo, hi, e, le, PhotonProcess.Incoherent)
                 + PartialCrossSections.MassCrossSection(el, lo, hi, e, le, PhotonProcess.Photoelectric)
                 + PartialCrossSections.MassCrossSection(el, lo, hi, e, le, PhotonProcess.PairProduction, threshold);
        }

        // --- натуральный кубический сплайн по (x, y) ---
        static double Spline(List<double> xs, List<double> ys, double x)
        {
            int n = xs.Count;
            if (n < 2)
            {
                return double.NaN;
            }

            double[] h = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = xs[i + 1] - xs[i];
            }

            double[] a = new double[n], b = new double[n], c = new double[n], d = new double[n];
            double[] m = new double[n];
            b[0] = 1.0;
            b[n - 1] = 1.0;
            for (int i = 1; i < n - 1; i++)
            {
                a[i] = h[i - 1];
                b[i] = 2.0 * (h[i - 1] + h[i]);
                c[i] = h[i];
                d[i] = 6.0 * ((ys[i + 1] - ys[i]) / h[i] - (ys[i] - ys[i - 1]) / h[i - 1]);
            }

            for (int i = 1; i < n; i++)
            {
                double w = a[i] / b[i - 1];
                b[i] -= w * c[i - 1];
                d[i] -= w * d[i - 1];
            }

            m[n - 1] = d[n - 1] / b[n - 1];
            for (int i = n - 2; i >= 0; i--)
            {
                m[i] = (d[i] - c[i] * m[i + 1]) / b[i];
            }

            int k = 0;
            while (k < n - 2 && x > xs[k + 1])
            {
                k++;
            }

            double t = x - xs[k], u = xs[k + 1] - x;
            return m[k] * u * u * u / (6.0 * h[k]) + m[k + 1] * t * t * t / (6.0 * h[k])
                 + (ys[k] / h[k] - m[k] * h[k] / 6.0) * u + (ys[k + 1] / h[k] - m[k + 1] * h[k] / 6.0) * t;
        }

        /// <summary>
        /// Опора: поканальный сплайн в лог-лог; пары — сплайн ln[σ/(1 − E₀/E)³]
        /// по открытым узлам каждого из двух каналов пар. Узлы с одинаковой
        /// энергией (края поглощения) берутся верхними; сплайн строится по
        /// участку 500…20000 кэВ, где краёв нет.
        /// </summary>
        static double Reference(MaterialDatabase.Element el, double e, int skip)
        {
            double sum = 0.0;
            double le = Math.Log(e);
            for (int ch = 0; ch < 5; ch++)
            {
                var xs = new List<double>();
                var ys = new List<double>();
                for (int i = 0; i < el.EnergyKev.Length; i++)
                {
                    double ei = el.EnergyKev[i];
                    if (i == skip || ei < 500.0 || ei > 20000.0)
                    {
                        continue;
                    }

                    if (xs.Count > 0 && Math.Abs(Math.Log(ei) - xs[xs.Count - 1]) < 1e-12)
                    {
                        xs.RemoveAt(xs.Count - 1);
                        ys.RemoveAt(ys.Count - 1);
                    }

                    double v = el.Channels[ch][i];
                    if (ch >= 3)
                    {
                        double e0 = ch == 3 ? MaterialDatabase.PairNuclearThresholdKev
                                            : MaterialDatabase.PairElectronThresholdKev;
                        double s = MaterialDatabase.PairThresholdShape(ei, e0);
                        if (!(v > 0.0) || !(s > 0.0))
                        {
                            continue;
                        }

                        v /= s;
                    }
                    else if (!(v > 0.0))
                    {
                        continue;
                    }

                    xs.Add(Math.Log(ei));
                    ys.Add(Math.Log(v));
                }

                double y = Spline(xs, ys, le);
                double value = Math.Exp(y);
                if (ch >= 3)
                {
                    double e0 = ch == 3 ? MaterialDatabase.PairNuclearThresholdKev
                                        : MaterialDatabase.PairElectronThresholdKev;
                    value *= MaterialDatabase.PairThresholdShape(e, e0);
                }

                sum += value;
            }

            return sum;
        }

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            int failed = 0;

            // (1) опора и две формы
            Console.WriteLine("(1) полное ослабление, отклонение от поканального сплайна, %: хорда | порог; доля пар в μ");
            double[] points = { 1764.5, 2614.5, 2500.0, 3500.0, 4500.0 };
            for (int k = 0; k < Zs.Length; k++)
            {
                MaterialDatabase.Element el;
                if (!MaterialDatabase.TryGet(Zs[k], out el))
                {
                    Console.WriteLine("   нет элемента Z=" + Zs[k]);
                    failed++;
                    continue;
                }

                var sb = new StringBuilder();
                sb.Append("   ").Append(Names[k].PadRight(3));
                foreach (double e in points)
                {
                    int lo, hi;
                    MaterialDatabase.Bracket(el.EnergyKev, e, out lo, out hi);
                    double reference = Reference(el, e, -1);
                    double chord = Total(el, lo, hi, e, false);
                    double thr = Total(el, lo, hi, e, true);
                    double pair = PartialCrossSections.MassCrossSection(el, lo, hi, e, Math.Log(e),
                                                                        PhotonProcess.PairProduction, true);
                    sb.Append("  ").Append(F(e, 1)).Append(": ")
                      .Append(F(100.0 * (chord / reference - 1.0), 2)).Append(" | ")
                      .Append(F(100.0 * (thr / reference - 1.0), 2)).Append(" (пары ")
                      .Append(F(100.0 * pair / thr, 1)).Append(")");
                }

                Console.WriteLine(sb.ToString());
            }

            // (2) выброс узла
            Console.WriteLine();
            Console.WriteLine("(2) выброс узла: узел убран, восстановлен соседями; отклонение от табличного, %: хорда | порог");
            double[] nodes = { 1500.0, 2000.0, 3000.0, 4000.0, 5000.0 };
            for (int k = 0; k < Zs.Length; k++)
            {
                MaterialDatabase.Element el;
                if (!MaterialDatabase.TryGet(Zs[k], out el))
                {
                    continue;
                }

                var sb = new StringBuilder();
                sb.Append("   ").Append(Names[k].PadRight(3));
                foreach (double node in nodes)
                {
                    int idx = Array.IndexOf(el.EnergyKev, node);
                    if (idx <= 0 || idx >= el.EnergyKev.Length - 1)
                    {
                        continue;
                    }

                    // соседи — первый ниже и первый выше с ДРУГОЙ энергией
                    int lo = idx - 1, hi = idx + 1;
                    while (lo > 0 && el.EnergyKev[lo] == node)
                    {
                        lo--;
                    }

                    while (hi < el.EnergyKev.Length - 1 && el.EnergyKev[hi] == node)
                    {
                        hi++;
                    }

                    double table = 0.0;
                    for (int ch = 0; ch < 5; ch++)
                    {
                        table += el.Channels[ch][idx];
                    }

                    double chord = Total(el, lo, hi, node, false);
                    double thr = Total(el, lo, hi, node, true);
                    sb.Append("  ").Append(F(node, 0)).Append(" [").Append(F(el.EnergyKev[lo], 0)).Append("…")
                      .Append(F(el.EnergyKev[hi], 0)).Append("]: ")
                      .Append(F(100.0 * (chord / table - 1.0), 2)).Append(" | ")
                      .Append(F(100.0 * (thr / table - 1.0), 2));
                }

                Console.WriteLine(sb.ToString());
            }

            // (3) согласие полного с розыгрышем при ключе ВКЛ: «остаток на фото»
            Console.WriteLine();
            Console.WriteLine("(3) ключ ВКЛ: остаток на фото = полное(хорда) − ког − неког − пары(порог); против фото, %");
            foreach (int kk in new[] { 0, 1, 2 })
            {
                MaterialDatabase.Element el;
                if (!MaterialDatabase.TryGet(Zs[kk], out el))
                {
                    continue;
                }

                var sb = new StringBuilder();
                sb.Append("   ").Append(Names[kk].PadRight(3));
                foreach (double e in new[] { 1500.0, 2614.5, 3500.0 })
                {
                    int lo, hi;
                    MaterialDatabase.Bracket(el.EnergyKev, e, out lo, out hi);
                    double le = Math.Log(e);
                    double coh = PartialCrossSections.MassCrossSection(el, lo, hi, e, le, PhotonProcess.Coherent);
                    double inc = PartialCrossSections.MassCrossSection(el, lo, hi, e, le, PhotonProcess.Incoherent);
                    double ph = PartialCrossSections.MassCrossSection(el, lo, hi, e, le, PhotonProcess.Photoelectric);
                    double pairThr = PartialCrossSections.MassCrossSection(el, lo, hi, e, le, PhotonProcess.PairProduction, true);
                    double codeTotal = PartialCrossSections.MassTotal(el, lo, hi, e, le);
                    double rest = codeTotal - coh - inc - pairThr;
                    sb.Append("  ").Append(F(e, 1)).Append(": ").Append(F(100.0 * (rest / ph - 1.0), 2));
                }

                Console.WriteLine(sb.ToString());
            }

            // (4) код: вход с ключом
            Console.WriteLine();
            MethodInfo keyed = typeof(PartialCrossSections).GetMethod(
                "MassTotal", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(MaterialDatabase.Element), typeof(int), typeof(int), typeof(double), typeof(double), typeof(bool) },
                null);
            MethodInfo keyedNc = typeof(PartialCrossSections).GetMethod(
                "MassTotalWithoutCoherent", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(MaterialDatabase.Element), typeof(int), typeof(int), typeof(double), typeof(double), typeof(bool) },
                null);
            if (keyed == null || keyedNc == null)
            {
                Console.WriteLine("(4) ⛔ в коде НЕТ входа MassTotal/MassTotalWithoutCoherent(…, bool thresholdPair): "
                                  + "полное не знает ключа XcomPairThreshold");
                failed++;
            }
            else
            {
                double worstOn = 0.0, worstOnNc = 0.0;
                long offDiff = 0, count = 0;
                for (int z = 1; z <= 100; z++)
                {
                    MaterialDatabase.Element el;
                    if (!MaterialDatabase.TryGet(z, out el) || el.EnergyKev == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < el.EnergyKev.Length; i++)
                    {
                        double[] es = i + 1 < el.EnergyKev.Length
                            ? new[] { el.EnergyKev[i], Math.Sqrt(el.EnergyKev[i] * el.EnergyKev[i + 1]) }
                            : new[] { el.EnergyKev[i] };
                        foreach (double e in es)
                        {
                            int lo, hi;
                            if (!(e > 0.0) || !MaterialDatabase.Bracket(el.EnergyKev, e, out lo, out hi))
                            {
                                continue;
                            }

                            double le = Math.Log(e);
                            count++;
                            double oldT = PartialCrossSections.MassTotal(el, lo, hi, e, le);
                            double offT = (double)keyed.Invoke(null, new object[] { el, lo, hi, e, le, false });
                            double onT = (double)keyed.Invoke(null, new object[] { el, lo, hi, e, le, true });
                            double oldN = PartialCrossSections.MassTotalWithoutCoherent(el, lo, hi, e, le);
                            double offN = (double)keyedNc.Invoke(null, new object[] { el, lo, hi, e, le, false });
                            double onN = (double)keyedNc.Invoke(null, new object[] { el, lo, hi, e, le, true });
                            if (BitConverter.DoubleToInt64Bits(oldT) != BitConverter.DoubleToInt64Bits(offT)
                                || BitConverter.DoubleToInt64Bits(oldN) != BitConverter.DoubleToInt64Bits(offN))
                            {
                                offDiff++;
                            }

                            double want = Total(el, lo, hi, e, true);
                            double coh = PartialCrossSections.MassCrossSection(el, lo, hi, e, le, PhotonProcess.Coherent);
                            if (want > 0.0)
                            {
                                worstOn = Math.Max(worstOn, Math.Abs(onT / want - 1.0));
                            }

                            if (want - coh > 0.0)
                            {
                                worstOnNc = Math.Max(worstOnNc, Math.Abs(onN / (want - coh) - 1.0));
                            }
                        }
                    }
                }

                bool ok = offDiff == 0 && worstOn < 1e-12 && worstOnNc < 1e-12;
                Console.WriteLine("(4) {0} вход с ключом: точек {1}; ВЫКЛ против прежнего — расходятся {2} (обязано 0); "
                                  + "ВКЛ против суммы каналов с порогом — худшее {3:E2} / без ког. {4:E2}",
                                  ok ? "✅" : "⛔", count, offDiff, worstOn, worstOnNc);
                if (!ok)
                {
                    failed++;
                }
            }

            // (5) умолчание ключа (решение Amber 24.09.2026 «ВКЛ в физике 24 (Рекомендую)»):
            // склад, симулятор и области сцены — ВКЛ; путь по умолчанию = пороговая форма.
            Console.WriteLine();
            bool store = new ResponseMatrixOptions().XcomPairThreshold;
            string inPath = System.IO.Path.Combine("tools", "CORPUS", "corpus", "geometries", "RC103_point0.in");
            bool simKey = false, regionsOn = false;
            int regions = 0;
            if (System.IO.File.Exists(inPath))
            {
                BecquerelMonitor.GlobalConfigManager.GetInstance();
                var sim = new EfficiencySimulator(GeometryModel.Load(inPath));
                simKey = sim.XcomPairThreshold;
                typeof(EfficiencySimulator).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(sim, null);
                var list = (System.Collections.IList)typeof(EfficiencySimulator)
                    .GetField("regions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sim);
                regionsOn = list.Count > 0;
                foreach (object r in list)
                {
                    regions++;
                    FieldInfo f = r.GetType().GetField("ThresholdPair");
                    regionsOn &= f != null && (bool)f.GetValue(r);
                }
            }

            MaterialDatabase.Element pb;
            string pbLine = "";
            if (MaterialDatabase.TryGet(82, out pb))
            {
                int lo, hi;
                MaterialDatabase.Bracket(pb.EnergyKev, 2614.5, out lo, out hi);
                double reference = Reference(pb, 2614.5, -1);
                double path = keyed != null && store
                    ? (double)keyed.Invoke(null, new object[] { pb, lo, hi, 2614.5, Math.Log(2614.5), true })
                    : PartialCrossSections.MassTotal(pb, lo, hi, 2614.5, Math.Log(2614.5));
                pbLine = "; полное Pb на 2614.5 кэВ путём по умолчанию против сплайна " + F(100.0 * (path / reference - 1.0), 2) + " %";
            }

            bool okDefault = store && simKey && regionsOn;
            Console.WriteLine("(5) {0} умолчание XcomPairThreshold: склад {1}, симулятор {2}, областей сцены RC103_point0 с ключом {3} из {4}{5}",
                              okDefault ? "✅" : "⛔", store ? "ВКЛ" : "ВЫКЛ", simKey ? "ВКЛ" : "ВЫКЛ",
                              regionsOn ? regions : 0, regions, pbLine);
            if (!okDefault)
            {
                failed++;
            }

            return failed == 0 ? 0 : 1;
        }
    }
}
