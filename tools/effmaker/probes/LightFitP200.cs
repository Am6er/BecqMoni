using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace LightFitP200
{
    /// <summary>
    /// (`AMBER152`/`AMBER155`, П200 01.10.2026) Подгонка кривой света
    /// сцинтиллятора сквозь ТЕКУЩИЙ перенос.
    ///
    /// Свет события в симуляторе — Σ dᵢ·y(teᵢ) (`EfficiencySimulator.AddLight`),
    /// а `LightYieldCurve.Of` интерполирует y ЛИНЕЙНО по ln E между узлами
    /// кривой. Значит фотонный свет пика (`LastPhotonLightScale`) — ЛИНЕЙНАЯ
    /// функция значений узлов y_k: S(E) = Σ_k a_k(E)·y_k, а свет не тянет ни
    /// одного случайного числа (тот же поток историй при любой кривой). Проба
    /// снимает коэффициенты a_k(E) разностью «база / узел k + δ» на одном
    /// потоке — точно, без статистики разности, — и подгонка формы кривой к
    /// фотонным точкам дальше идёт в питоне без переноса.
    ///
    ///     lightfitp200 --geometry=X.in --energies=10,20,... [--n=200000]
    ///                  [--curve=файл]           подставить кривую (E_кэВ y на строку)
    ///                  [--jac=out.csv] [--delta=0.01]
    ///                  [--rows=2614.5,661.657 --rowout=префикс] [--bin=1]
    ///
    /// --curve= подменяет узлы кривой вещества кристалла (та же сетка или
    /// своя, строго по возрастанию E) отражением в общий кэшированный объект
    /// кривой — все симуляторы процесса берут её. --rows= выписывает строку
    /// отклика (после пересчёта в шкалу света, пик на энергии линии) по
    /// каналам `ResponseByChannel` в `<префикс>_<E>.csv`: столбцы E_кэВ,
    /// total, peak, compton, se, xk, de, xl, out.
    ///
    /// Умолчания физики — склада (`ResponseMatrixOptions`, как у
    /// `LightScaleProbe`): kdip, ecomp, E_q, p.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null, curvePath = null, jacPath = null, rowOut = null;
            int histories = 200000;
            double delta = 0.01, binKev = 1.0;
            var energies = new List<double>();
            var rows = new List<double>();
            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--curve=", StringComparison.Ordinal)) curvePath = a.Substring(8);
                else if (a.StartsWith("--jac=", StringComparison.Ordinal)) jacPath = a.Substring(6);
                else if (a.StartsWith("--delta=", StringComparison.Ordinal)) delta = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--rowout=", StringComparison.Ordinal)) rowOut = a.Substring(9);
                else if (a.StartsWith("--energies=", StringComparison.Ordinal)) energies = ParseList(a.Substring(11));
                else if (a.StartsWith("--rows=", StringComparison.Ordinal)) rows = ParseList(a.Substring(7));
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            if (rows.Count > 0 && rowOut == null)
            {
                Console.Error.WriteLine("--rows= требует --rowout=<префикс>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            var store = new ResponseMatrixOptions();
            Action<EfficiencySimulator> setup = sim =>
            {
                sim.LightNonproportionality = true;
                sim.LightSubKevCurve = ResponseMatrixOptions.KDipCurveHalf(store.KDipLight);
                sim.LightCascadeSplit = ResponseMatrixOptions.KDipCascadeHalf(store.KDipLight);
                sim.ElectronAnyMaterial = store.ElectronAnyMaterial;
            };

            var first = new EfficiencySimulator(geometry.Clone());
            setup(first);
            MaterialDatabase.LightYieldCurve curve = first.LightYieldCurve;
            if (curve == null)
            {
                Console.Error.WriteLine("у вещества кристалла нет кривой света — подгонять нечего");
                return 3;
            }

            FieldInfo fe = typeof(MaterialDatabase.LightYieldCurve).GetField("energyKev", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo fy = typeof(MaterialDatabase.LightYieldCurve).GetField("yieldRel", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fe == null || fy == null)
            {
                Console.Error.WriteLine("поля кривой energyKev/yieldRel не найдены отражением — проба устарела");
                return 3;
            }

            if (curvePath != null)
            {
                var ce = new List<double>();
                var cy = new List<double>();
                foreach (string line in File.ReadAllLines(curvePath))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t[0] == '#' || char.IsLetter(t[0]))
                    {
                        continue;
                    }

                    string[] p = t.Split(new[] { '\t', ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    ce.Add(double.Parse(p[0], CultureInfo.InvariantCulture));
                    cy.Add(double.Parse(p[1], CultureInfo.InvariantCulture));
                }

                for (int i = 1; i < ce.Count; i++)
                {
                    if (!(ce[i] > ce[i - 1]))
                    {
                        Console.Error.WriteLine("кривая не по возрастанию E: строка " + i.ToString(CultureInfo.InvariantCulture));
                        return 2;
                    }
                }

                fe.SetValue(curve, ce.ToArray());
                fy.SetValue(curve, cy.ToArray());
                curve.Variant = "подставлена: " + Path.GetFileName(curvePath);
            }

            double[] nodesE = (double[])fe.GetValue(curve);
            double[] baseY = (double[])((double[])fy.GetValue(curve)).Clone();
            Console.WriteLine("геометрия: {0}", geometry.Describe());
            Console.WriteLine("кривая: {0} [{1}], узлов {2}; историй {3}", curve.Material, curve.Variant, nodesE.Length, histories);

            Func<double, double> photon = e =>
            {
                var sim = new EfficiencySimulator(geometry.Clone()) { Histories = histories, PeakHalfWidthKev = 0.0 };
                setup(sim);
                sim.ResetStream((ulong)sim.Seed ^ (ulong)Math.Round(e * 64.0) * 0x9E3779B97F4A7C15UL);
                double err;
                sim.Response(e, binKev, out err);
                return sim.LastPhotonLightScale;
            };

            StreamWriter jac = null;
            if (jacPath != null)
            {
                jac = new StreamWriter(jacPath, false, new UTF8Encoding(false));
                jac.WriteLine("E_kev,k,node_kev,coef,S0");
            }

            foreach (double e in energies)
            {
                fy.SetValue(curve, (double[])baseY.Clone());
                double s0 = photon(e);
                Console.WriteLine("  {0,8:F3}  S={1}", e, s0.ToString("F5", CultureInfo.InvariantCulture));
                if (jac == null)
                {
                    continue;
                }

                // Узлы, способные войти в свет: все ниже первой точки выше E.
                int kMax = 0;
                while (kMax < nodesE.Length - 1 && nodesE[kMax] < e * 1.0001)
                {
                    kMax++;
                }

                double sumCoef = 0.0;
                for (int k = 0; k <= kMax; k++)
                {
                    double[] y = (double[])baseY.Clone();
                    y[k] += delta;
                    fy.SetValue(curve, y);
                    double sk = photon(e);
                    double coef = (sk - s0) / delta;
                    sumCoef += coef;
                    jac.WriteLine("{0},{1},{2},{3},{4}",
                        e.ToString("R", CultureInfo.InvariantCulture),
                        k.ToString(CultureInfo.InvariantCulture),
                        nodesE[k].ToString("R", CultureInfo.InvariantCulture),
                        coef.ToString("R", CultureInfo.InvariantCulture),
                        s0.ToString("R", CultureInfo.InvariantCulture));
                }

                jac.Flush();
                // Σ a_k — свет при y ≡ 1 (пропорциональная шкала): обязан быть ≈ 1.
                Console.WriteLine("            Σa_k = {0} (узлов {1})", sumCoef.ToString("F5", CultureInfo.InvariantCulture), kMax + 1);
            }

            if (jac != null)
            {
                jac.Dispose();
            }

            fy.SetValue(curve, (double[])baseY.Clone());
            foreach (double e in rows)
            {
                var sim = new EfficiencySimulator(geometry.Clone()) { Histories = histories, PeakHalfWidthKev = 0.0 };
                setup(sim);
                sim.ResetStream((ulong)sim.Seed ^ (ulong)Math.Round(e * 64.0) * 0x9E3779B97F4A7C15UL);
                double err;
                double[][] ch = sim.ResponseByChannel(e, binKev, out err);
                string path = rowOut + "_" + e.ToString("0.###", CultureInfo.InvariantCulture) + ".csv";
                using (var w = new StreamWriter(path, false, new UTF8Encoding(false)))
                {
                    w.WriteLine("E_kev,total,peak,compton,se,xk,de,xl,out");
                    int bins = ch[0].Length;
                    for (int i = 0; i < bins; i++)
                    {
                        var sb = new StringBuilder();
                        sb.Append((i * binKev).ToString("R", CultureInfo.InvariantCulture));
                        double tot = 0.0;
                        for (int c = 0; c < ch.Length; c++)
                        {
                            tot += ch[c][i];
                        }

                        sb.Append(',').Append(tot.ToString("R", CultureInfo.InvariantCulture));
                        for (int c = 0; c < ch.Length; c++)
                        {
                            sb.Append(',').Append(ch[c][i].ToString("R", CultureInfo.InvariantCulture));
                        }

                        w.WriteLine(sb.ToString());
                    }
                }

                Console.WriteLine("строка {0} кэВ: {1}; S={2}", e.ToString("F3", CultureInfo.InvariantCulture), path,
                    sim.LastPhotonLightScale.ToString("F5", CultureInfo.InvariantCulture));
            }

            return 0;
        }

        static List<double> ParseList(string s)
        {
            var list = new List<double>();
            foreach (string part in s.Split(','))
            {
                if (part.Trim().Length > 0)
                {
                    list.Add(double.Parse(part.Trim(), CultureInfo.InvariantCulture));
                }
            }

            return list;
        }
    }
}
