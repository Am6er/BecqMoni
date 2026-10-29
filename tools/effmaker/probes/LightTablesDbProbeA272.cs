using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace LightTablesDbProbeA272
{
    /// <summary>
    /// (`AMBER272`, 29.10.2026) Приёмка переноса фотонных кривых света FSA из кода в
    /// `matdb` (`scint_photon_light_curves` / `scint_photon_light_yield`).
    ///
    ///     lighttablesdbprobea272 --head=<каталог с NaITable_HEAD.tsv и CsITable_HEAD.tsv>
    ///                            [--db=<matdb.sqlite>] [--before=<matdb.sqlite до записи>]
    ///                            [--control=<копия с подменённым узлом>]
    ///                            [--electron-out=<каталог>]
    ///
    /// 1. ПОБИТОВО: `FsaLightScale.Relative/ShiftKev/MaxShiftKev` из базы против прежних
    ///    массивов кода (сняты из `HEAD` в `--head`) — на узлах и на плотной сетке 1…3000 кэВ
    ///    (прежняя интерполяция повторена здесь дословно); выбор `CurveFor` по прежнему правилу.
    /// 2. `CurveFor("LaBr3:Ce")`, `CurveFor("CeBr3")` — не null (новые вещества записью в базу).
    /// 3. Отпечаток переноса `SimulatorDataFingerprint` до и после записи — равен.
    /// 4. Положительный контроль: копия базы с подменённым узлом обязана дать расхождение.
    /// 5. `--electron-out`: кривые электронов LaBr3:Ce/CeBr3 табличной формы
    ///    (`LightYieldPayne` без продолжения и обрыва, тормозная по составу) — узлы как есть,
    ///    для варианта развилки «строками в scint_electron_light_yield» (в базу НЕ пишутся).
    ///
    /// Код возврата: 0 — всё сошлось и контроль пойман; 1 — расхождение; 2 — ошибка запуска.
    /// </summary>
    static class Program
    {
        static Type scale;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string head = null, db = null, before = null, control = null, electronOut = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--head=", StringComparison.Ordinal)) head = a.Substring(7);
                else if (a.StartsWith("--db=", StringComparison.Ordinal)) db = a.Substring(5);
                else if (a.StartsWith("--before=", StringComparison.Ordinal)) before = a.Substring(9);
                else if (a.StartsWith("--control=", StringComparison.Ordinal)) control = a.Substring(10);
                else if (a.StartsWith("--electron-out=", StringComparison.Ordinal)) electronOut = a.Substring(15);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (head == null)
            {
                Console.Error.WriteLine("нужен --head=<каталог прежних массивов>");
                return 2;
            }

            scale = typeof(FsaAnalyzer).Assembly.GetType("BecquerelMonitor.FullSpectrumAnalysis.FsaLightScale");
            if (scale == null)
            {
                Console.Error.WriteLine("FsaLightScale отражением не нашёлся");
                return 2;
            }

            var reference = new Dictionary<string, double[][]>
            {
                { "NaI:Tl", ReadHead(Path.Combine(head, "NaITable_HEAD.tsv")) },
                { "CsI:Tl", ReadHead(Path.Combine(head, "CsITable_HEAD.tsv")) },
            };

            bool ok = true;
            Reload(db);
            Console.WriteLine("база: {0}", db ?? "рядом с программой");
            int bad = Compare(reference, true);
            Console.WriteLine("ИТОГ 1 (побитово против кода HEAD): {0}", bad == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ " + bad);
            ok &= bad == 0;

            bad = CompareCurveFor();
            Console.WriteLine("ИТОГ 1б (CurveFor по прежнему правилу для NaI/CsI): {0}", bad == 0 ? "СОШЛОСЬ" : "РАСХОЖДЕНИЙ " + bad);
            ok &= bad == 0;

            foreach (string m in new[] { "LaBr3:Ce", "CeBr3" })
            {
                string c = FsaCascadeSummer.PhotonCurveFor(m);
                double r10 = c != null ? Relative(c, 10.0) : double.NaN;
                double r662 = c != null ? Relative(c, 661.657) : double.NaN;
                Console.WriteLine("ИТОГ 2 CurveFor(\"{0}\") = {1}{2}", m, c ?? "null",
                                  c != null ? string.Format(CultureInfo.InvariantCulture, "; r(10)/r(661.657) = {0:F4}", r10 / r662) : "");
                ok &= c != null;
            }

            foreach (string m in new[] { "SrI2", "GAGG", "CdTe", "Ge" })
            {
                Console.WriteLine("    CurveFor(\"{0}\") = {1}", m, FsaCascadeSummer.PhotonCurveFor(m) ?? "null");
            }

            if (before != null)
            {
                string fpBefore = MaterialDatabase.SimulatorDataFingerprint(before);
                string fpAfter = MaterialDatabase.SimulatorDataFingerprint(db ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "matdb.sqlite"));
                Console.WriteLine("ИТОГ 3 отпечаток переноса: до {0}, после {1} — {2}", fpBefore, fpAfter,
                                  fpBefore == fpAfter ? "РАВНЫ" : "РАЗНЫЕ");
                ok &= fpBefore == fpAfter;
            }

            if (control != null)
            {
                Reload(control);
                int caught = Compare(reference, false);
                Console.WriteLine("ИТОГ 4 положительный контроль ({0}): расхождений {1} — {2}", control, caught,
                                  caught > 0 ? "ПОЙМАНО" : "НЕ ПОЙМАНО");
                ok &= caught > 0;
                if (before != null)
                {
                    Console.WriteLine("    отпечаток переноса копии-контроля: {0}", MaterialDatabase.SimulatorDataFingerprint(control));
                }

                Reload(db);
            }

            if (before != null)
            {
                // Отказ чтения называется, а не глотается: база без таблиц (прежняя) —
                // кривой нет, и в собиратель отказов FSA уходит запись.
                Reload(before);
                int was = FsaDatabaseFailures.ProcessCount;
                string c = FsaCascadeSummer.PhotonCurveFor("NaI:Tl");
                int now = FsaDatabaseFailures.ProcessCount;
                Console.WriteLine("ИТОГ 5 база без таблиц ({0}): CurveFor(\"NaI:Tl\") = {1}, отказов FSA +{2} — {3}",
                                  before, c ?? "null", now - was, c == null && now > was ? "НАЗВАН" : "НЕ НАЗВАН");
                ok &= c == null && now > was;
                Reload(db);
            }

            if (electronOut != null)
            {
                foreach (string m in new[] { "LaBr3:Ce", "CeBr3" })
                {
                    WriteElectron(m, electronOut);
                }
            }

            Console.WriteLine(ok ? "ПРИЁМКА: КОД 0" : "ПРИЁМКА: РАСХОЖДЕНИЕ");
            return ok ? 0 : 1;
        }

        static double[][] ReadHead(string path)
        {
            var rows = new List<double[]>();
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line.StartsWith("energy_kev", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] p = line.Split('\t');
                rows.Add(new[] { double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture) });
            }

            return rows.ToArray();
        }

        static void Reload(string path)
        {
            scale.GetMethod("Reload", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { path });
        }

        static double Relative(string curve, double e)
        {
            return (double)scale.GetMethod("Relative", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { curve, e });
        }

        static double ShiftKev(string curve, double e, double e0)
        {
            return (double)scale.GetMethod("ShiftKev", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { curve, e, e0 });
        }

        static double MaxShiftKev(string curve, double e0, double maxKev)
        {
            return (double)scale.GetMethod("MaxShiftKev", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { curve, e0, maxKev });
        }

        /// <summary>Прежний `FsaLightScale.Interp` дословно (`ab0cac39`).</summary>
        static double OldInterp(double[][] table, double e)
        {
            if (!(e > 0.0) || e <= table[0][0])
            {
                return table[0][1];
            }

            int last = table.Length - 1;
            if (e >= table[last][0])
            {
                return table[last][1];
            }

            for (int i = 1; i <= last; i++)
            {
                if (e <= table[i][0])
                {
                    double e0 = table[i - 1][0], r0 = table[i - 1][1];
                    double e1 = table[i][0], r1 = table[i][1];
                    double t = (Math.Log(e) - Math.Log(e0)) / (Math.Log(e1) - Math.Log(e0));
                    return r0 + (r1 - r0) * t;
                }
            }

            return table[last][1];
        }

        static double OldShift(double[][] table, double e, double e0)
        {
            double r = OldInterp(table, e);
            double r0 = OldInterp(table, e0);
            return !(r0 > 0.0) ? double.NaN : e * r / r0 - e;
        }

        static bool Same(double a, double b)
        {
            return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
        }

        static int Compare(Dictionary<string, double[][]> reference, bool verbose)
        {
            int bad = 0;
            foreach (KeyValuePair<string, double[][]> pair in reference)
            {
                string curve = pair.Key;
                double[][] table = pair.Value;
                int nodes = 0, nodeBad = 0, dense = 0, denseBad = 0;
                foreach (double[] node in table)
                {
                    nodes++;
                    if (!Same(Relative(curve, node[0]), node[1]))
                    {
                        nodeBad++;
                        if (verbose || nodeBad <= 3)
                        {
                            Console.WriteLine("    {0}: узел {1} кэВ — база {2:R}, код {3:R}", curve, node[0], Relative(curve, node[0]), node[1]);
                        }
                    }
                }

                // Плотная сетка: вся функция, а не только узлы — и ShiftKev к трём опорным.
                for (int i = 0; i <= 4000; i++)
                {
                    double e = Math.Pow(10.0, -0.5 + 4.0 * i / 4000.0);   // 0.316…3162 кэВ
                    dense++;
                    bool same = Same(Relative(curve, e), OldInterp(table, e));
                    foreach (double e0 in new[] { 661.657, 1460.82, 2614.511 })
                    {
                        same &= Same(ShiftKev(curve, e, e0), OldShift(table, e, e0));
                    }

                    if (!same)
                    {
                        denseBad++;
                    }
                }

                foreach (double e0 in new[] { 661.657, 1460.82 })
                {
                    foreach (double maxKev in new[] { 0.0, 300.0, 3000.0 })
                    {
                        double up = 0.0;
                        foreach (double[] point in table)
                        {
                            if (maxKev > 0.0 && point[0] > maxKev)
                            {
                                continue;
                            }

                            double s = OldShift(table, point[0], e0);
                            if (!double.IsNaN(s) && s > up)
                            {
                                up = s;
                            }
                        }

                        dense++;
                        if (!Same(MaxShiftKev(curve, e0, maxKev), up))
                        {
                            denseBad++;
                        }
                    }
                }

                Console.WriteLine("  {0}: узлов {1}, не сошлось {2}; плотная сетка и сдвиги {3} точек, не сошлось {4}",
                                  curve, nodes, nodeBad, dense, denseBad);
                bad += nodeBad + denseBad;
            }

            return bad;
        }

        /// <summary>Прежний `FsaLightScale.CurveFor` дословно (`ab0cac39`).</summary>
        static string OldCurveFor(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.StartsWith("NaI", StringComparison.Ordinal)) return "NaI:Tl";
            if (s.StartsWith("CsI", StringComparison.Ordinal)) return "CsI:Tl";
            return null;
        }

        static int CompareCurveFor()
        {
            int bad = 0;
            string[] names =
            {
                null, "", "NaI:Tl", "NaI", "NaI(Tl)", "NaITl", "CsI:Tl", "CsI:Na", "CsI", "CsI(Tl)",
                "Ge", "CdTe", "CZT", "BGO", "GSO", "SrI2", "GAGG", "nai:tl", "Na", "Cs",
            };
            foreach (string n in names)
            {
                string got = FsaCascadeSummer.PhotonCurveFor(n);
                string want = OldCurveFor(n);
                if (got != want)
                {
                    bad++;
                    Console.WriteLine("    CurveFor({0}) = {1}, прежнее правило — {2}", n ?? "null", got ?? "null", want ?? "null");
                }
            }

            Console.WriteLine("  CurveFor: имён {0}, не сошлось {1}", names.Length, bad);
            return bad;
        }

        static void WriteElectron(string material, string dir)
        {
            MaterialDatabase.LightYieldCurve curve = MaterialDatabase.LightYieldPayne(material, 0.0, false, 0.0, true, 2.0);
            if (curve == null)
            {
                Console.WriteLine("кривая электронов {0}: null", material);
                return;
            }

            FieldInfo fe = typeof(MaterialDatabase.LightYieldCurve).GetField("energyKev", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fy = typeof(MaterialDatabase.LightYieldCurve).GetField("yieldRel", BindingFlags.NonPublic | BindingFlags.Instance);
            var e = (double[])fe.GetValue(curve);
            var y = (double[])fy.GetValue(curve);
            string path = Path.Combine(dir, material.Replace(':', '_') + "_electron_table.tsv");
            using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                w.WriteLine("# {0} [{1}] — табличная форма LightYieldPayne(η базы, без продолжения ниже 1 кэВ и обрыва, тормозная по составу)", material, curve.Variant);
                w.WriteLine("energy_kev\tyield_rel");
                for (int i = 0; i < e.Length; i++)
                {
                    w.WriteLine("{0}\t{1}", e[i].ToString("R", CultureInfo.InvariantCulture), y[i].ToString("R", CultureInfo.InvariantCulture));
                }
            }

            Console.WriteLine("кривая электронов {0}: {1} узлов {2:R}…{3:R} кэВ, L/E(10) {4:F4}, горб {5:F4} → {6}",
                              material, e.Length, e[0], e[e.Length - 1], curve.Of(10.0), Max(y), path);
        }

        static double Max(double[] v)
        {
            double m = double.NegativeInfinity;
            foreach (double x in v) m = Math.Max(m, x);
            return m;
        }
    }
}
