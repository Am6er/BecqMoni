using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace KappaPeakTotalProbe
{
    /// <summary>
    /// (`S166`, полоса П18-FSA-замеры 12.09.2026) СОВМЕСТНАЯ ЭФФЕКТИВНОСТЬ
    /// «ПИК × ПОЛНАЯ» — κ_pT — против «пик × пик» — κ_pp.
    ///
    /// Вынос из пика в `FsaCascadeSummer.SurviveAll` берёт ε_T(j) партнёра как
    /// среднее по объёму, а верное — условное на том, что опорный квант k
    /// поглощён целиком: ⟨ε_p(k)·ε_T(j)⟩/⟨ε_p(k)⟩ = κ_pT(k,j)·ε_T(j). Таблица в
    /// матрице (хвост `JNTK`) держит только κ_pp; κ_pT нигде не мерен — этот
    /// замер и есть строка.
    ///
    /// КАК МЕРИТСЯ. Точка распада разыгрывается ТЕМ ЖЕ источником, что у
    /// симулятора (`source.Next`), и из неё идут по две истории на каждую
    /// энергию (наборы A и B), ТЕМ ЖЕ переносом (`OneHistory`) — как в
    /// `JointPeakSums`. Отличие одно: истории идут С ГИСТОГРАММОЙ из двух бинов
    /// (бин = E), и её сумма даёт «квант оставил в кристалле хоть что-нибудь»
    /// той же взвешенной ветвью, что даёт пик. Совместные величины берутся
    /// между РАЗНЫМИ наборами (a_k·T_B(j), b_k·T_A(j)), поэтому верны и на
    /// диагонали.
    ///
    /// ⚠ ПРИБЛИЖЕНИЕ НАЗВАНО: полная эффективность здесь — ВЗВЕШЕННОЙ ветвью
    /// (exp(−τ) + одно рассеяние), а штатная `TotalEfficiency` — аналоговая и
    /// на упоре выше её на 12…15 % (многократное рассеяние, возврат из-за
    /// кристалла). Недостающая часть рассеяна по сцене и с точкой распада
    /// связана слабее прямой, так что κ_pT взвешенной ветви — оценка СВЕРХУ
    /// отклонения от единицы; рядом печатается ⟨ε_T⟩ обеими ветвями, чтобы
    /// цену приближения было видно числом. Симулятор — ровно тот, что строит
    /// матрицу (`ResponseMatrixBuilder.MakeSimulator` отражением, с
    /// умолчаниями `ResponseMatrixOptions` и допуском пика по энергии), и
    /// потому κ_pp этой пробы обязана сойтись с таблицей матрицы в шуме — это
    /// положительный контроль (`--matrix=`). Отрицательный контроль — точечный
    /// источник: κ_pp = κ_pT = 1 по построению.
    ///
    ///   kappapeaktotalprobe --geometry=X.in --pairs=201.83:306.78[,…] [--matrix=X.rmx]
    ///                       [--points=100000] [--seed=0]
    ///
    /// Ничего не пишет: склад матриц только читается.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null, pairs = "201.83:306.78", matrixPath = null;
            int points = 100000;
            int seed = 0;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = arg.Substring(11);
                else if (arg.StartsWith("--pairs=", StringComparison.Ordinal)) pairs = arg.Substring(8);
                else if (arg.StartsWith("--points=", StringComparison.Ordinal)) points = int.Parse(arg.Substring(9), CultureInfo.InvariantCulture);
                else if (arg.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(arg.Substring(7), CultureInfo.InvariantCulture);
                else if (arg.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = arg.Substring(9);
                else { Console.Error.WriteLine("неизвестный ключ: " + arg); return 2; }
            }

            if (geometryPath == null)
            {
                Console.Error.WriteLine("нужен --geometry=<файл.in>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            if (geometry == null)
            {
                Console.Error.WriteLine("геометрия не прочиталась: " + geometryPath);
                return 2;
            }

            ResponseMatrix stored = null;
            if (matrixPath != null)
            {
                stored = ResponseMatrix.Load(matrixPath);
                if (stored == null)
                {
                    Console.Error.WriteLine("матрица не прочиталась: " + matrixPath);
                    return 2;
                }
            }

            // Пары и их энергии — одним списком без повторов.
            var pairList = new List<double[]>();
            var energies = new List<double>();
            foreach (string item in pairs.Split(','))
            {
                string[] two = item.Split(':');
                if (two.Length != 2)
                {
                    Console.Error.WriteLine("--pairs=<E1>:<E2>[,<E1>:<E2>...]");
                    return 2;
                }

                double e1 = double.Parse(two[0], CultureInfo.InvariantCulture);
                double e2 = double.Parse(two[1], CultureInfo.InvariantCulture);
                pairList.Add(new[] { e1, e2 });
                if (!energies.Contains(e1)) energies.Add(e1);
                if (!energies.Contains(e2)) energies.Add(e2);
            }

            int m = energies.Count;
            var options = new ResponseMatrixOptions();
            if (seed != 0)
            {
                options.Seed = seed;
            }

            EfficiencySimulator sim = MakeSimulator(geometry, options, 0, energies[0]);
            double[] halfWidths = new double[m];
            for (int e = 0; e < m; e++)
            {
                halfWidths[e] = PeakTolerance(options, geometry, energies[e]);
            }

            Console.WriteLine("геометрия : {0}", geometryPath);
            Console.WriteLine("источник  : {0}", geometry.SourceType);
            Console.WriteLine("точек     : {0} (по две истории на энергию из каждой)", points);
            Console.WriteLine("энергии   : {0}", string.Join(", ", energies.ConvertAll(x => F(x, "F2")).ToArray()));
            Console.WriteLine("допуск пика по энергиям, кэВ: {0}", string.Join(", ", Array.ConvertAll(halfWidths, x => F(x, "F3"))));
            Console.WriteLine("зерно     : {0}", options.Seed != 0 ? options.Seed.ToString(CultureInfo.InvariantCulture) : "штатное");

            // Отражение: EnsureBuilt, source.Next, OneHistory — приватные.
            Type simType = typeof(EfficiencySimulator);
            MethodInfo ensureBuilt = simType.GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo sourceField = simType.GetField("source", BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo oneHistory = simType.GetMethod("OneHistory", BindingFlags.NonPublic | BindingFlags.Instance);
            if (ensureBuilt == null || sourceField == null || oneHistory == null)
            {
                Console.Error.WriteLine("⛔ отражение не нашло EnsureBuilt/source/OneHistory — симулятор переименован, проба слепа");
                return 3;
            }

            ensureBuilt.Invoke(sim, null);
            object source = sourceField.GetValue(sim);
            MethodInfo next = source.GetType().GetMethod("Next");
            if (next == null)
            {
                Console.Error.WriteLine("⛔ у источника нет Next");
                return 3;
            }

            // Накопители: Single_p, Single_T, Joint_pp[i][j], Joint_pT[i][j] (i — пик, j — полная),
            // квадраты для шума.
            double[] singleP = new double[m], singleT = new double[m];
            double[][] jointPP = New(m), jointPT = New(m), jointPP2 = New(m), jointPT2 = New(m);
            double[] a = new double[m], b = new double[m], ta = new double[m], tb = new double[m];
            double[] hist = new double[2];
            object[] nextArgs = new object[4];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int p = 0; p < points; p++)
            {
                nextArgs[0] = sim;
                next.Invoke(source, nextArgs);
                double x = (double)nextArgs[1], y = (double)nextArgs[2], z = (double)nextArgs[3];

                for (int e = 0; e < m; e++)
                {
                    sim.PeakHalfWidthKev = halfWidths[e];
                    a[e] = History(oneHistory, sim, energies[e], x, y, z, hist, out ta[e]);
                }

                for (int e = 0; e < m; e++)
                {
                    sim.PeakHalfWidthKev = halfWidths[e];
                    b[e] = History(oneHistory, sim, energies[e], x, y, z, hist, out tb[e]);
                }

                for (int i = 0; i < m; i++)
                {
                    singleP[i] += 0.5 * (a[i] + b[i]);
                    singleT[i] += 0.5 * (ta[i] + tb[i]);
                    for (int j = 0; j < m; j++)
                    {
                        double pp = 0.5 * (a[i] * b[j] + b[i] * a[j]);
                        double pt = 0.5 * (a[i] * tb[j] + b[i] * ta[j]);
                        jointPP[i][j] += pp;
                        jointPP2[i][j] += pp * pp;
                        jointPT[i][j] += pt;
                        jointPT2[i][j] += pt * pt;
                    }
                }
            }

            Console.WriteLine("розыгрыш  : {0} с", F(clock.Elapsed.TotalSeconds, "F1"));
            Console.WriteLine();
            Console.WriteLine("  {0,9} {1,12} {2,12} {3,12} {4,10}", "E, кэВ", "ε_p", "ε_T(взвеш.)", "ε_T(аналог.)", "взвеш./аналог.");
            double[] analog = new double[m];
            for (int e = 0; e < m; e++)
            {
                double err;
                sim.PeakHalfWidthKev = halfWidths[e];
                analog[e] = sim.TotalEfficiency(energies[e], out err);
                double ep = singleP[e] / points, et = singleT[e] / points;
                Console.WriteLine("  {0,9:F2} {1,12:E4} {2,12:E4} {3,12:E4} {4,10:F4}",
                                  energies[e], ep, et, analog[e], analog[e] > 0.0 ? et / analog[e] : 0.0);
            }

            Console.WriteLine();
            Console.WriteLine("  {0,9} {1,9} {2,9} {3,7} {4,9} {5,7} {6,9} {7,7}{8}",
                              "E_пик", "E_партн.", "κ_pp", "±%", "κ_pT", "±%", "κ_Tp", "±%",
                              stored != null ? "   таблица κ_pp   расх., %" : "");
            foreach (double[] pair in pairList)
            {
                foreach (int[] order in new[] { new[] { 0, 1 }, new[] { 1, 0 } })
                {
                    int i = energies.IndexOf(pair[order[0]]), j = energies.IndexOf(pair[order[1]]);
                    double kpp, epp, kpt, ept, ktp, etp;
                    Kappa(jointPP[i][j], jointPP2[i][j], singleP[i], singleP[j], points, out kpp, out epp);
                    Kappa(jointPT[i][j], jointPT2[i][j], singleP[i], singleT[j], points, out kpt, out ept);
                    Kappa(jointPT[j][i], jointPT2[j][i], singleP[j], singleT[i], points, out ktp, out etp);
                    string tail = "";
                    if (stored != null)
                    {
                        double fromTable = stored.JointFactor(energies[i], energies[j]);
                        tail = string.Format(CultureInfo.InvariantCulture, "   {0,10:F4} {1,10:F2}",
                                             fromTable, kpp > 0.0 ? (fromTable - kpp) / kpp * 100.0 : 0.0);
                    }

                    Console.WriteLine("  {0,9:F2} {1,9:F2} {2,9:F4} {3,7:F2} {4,9:F4} {5,7:F2} {6,9:F4} {7,7:F2}{8}",
                                      energies[i], energies[j], kpp, epp, kpt, ept, ktp, etp, tail);
                }
            }

            Console.WriteLine();
            Console.WriteLine("κ_pT(k,j) = ⟨ε_p(k)·ε_T(j)⟩ / (⟨ε_p(k)⟩·⟨ε_T(j)⟩): k — опорный квант в пике, j — партнёр «задел кристалл»;");
            Console.WriteLine("κ_Tp — то же с обратными ролями. Вынос из пика линии k партнёром j занижен в κ_pT(k,j) раз.");
            return 0;
        }

        static double History(MethodInfo oneHistory, EfficiencySimulator sim, double energy,
                              double x, double y, double z, double[] hist, out double total)
        {
            hist[0] = 0.0;
            hist[1] = 0.0;
            double peak = (double)oneHistory.Invoke(sim, new object[] { energy, x, y, z, hist, energy });
            total = hist[0] + hist[1];
            return peak;
        }

        static void Kappa(double joint, double joint2, double singleA, double singleB, int n,
                          out double kappa, out double errorPct)
        {
            double meanJ = joint / n;
            double meanA = singleA / n, meanB = singleB / n;
            kappa = meanA > 0.0 && meanB > 0.0 ? meanJ / (meanA * meanB) : 1.0;
            double var = Math.Max(0.0, joint2 / n - meanJ * meanJ);
            errorPct = meanJ > 0.0 ? Math.Sqrt(var / n) / meanJ * 100.0 : 0.0;
        }

        static double[][] New(int m)
        {
            double[][] t = new double[m][];
            for (int i = 0; i < m; i++)
            {
                t[i] = new double[m];
            }

            return t;
        }

        /// <summary>
        /// Симулятор — ТОТ ЖЕ, что строит матрицу (`ResponseMatrixBuilder.MakeSimulator`,
        /// отражением); если построитель переименован — обычный конструктор с
        /// предупреждением, чтобы проба не молчала о подмене.
        /// </summary>
        static EfficiencySimulator MakeSimulator(GeometryModel geometry, ResponseMatrixOptions options,
                                                 int index, double energyKev)
        {
            MethodInfo make = typeof(ResponseMatrixBuilder).GetMethod(
                "MakeSimulator", BindingFlags.NonPublic | BindingFlags.Static);
            if (make != null)
            {
                Console.WriteLine("симулятор : ResponseMatrixBuilder.MakeSimulator (как у склада), умолчания ResponseMatrixOptions");
                return (EfficiencySimulator)make.Invoke(null, new object[] { geometry, options, index, energyKev });
            }

            Console.WriteLine("⚠ симулятор: обычный конструктор — MakeSimulator построителя не найден отражением");
            return new EfficiencySimulator(geometry) { Histories = options.Histories };
        }

        static double PeakTolerance(ResponseMatrixOptions options, GeometryModel geometry, double energyKev)
        {
            MethodInfo tol = typeof(ResponseMatrixBuilder).GetMethod(
                "PeakTolerance", BindingFlags.NonPublic | BindingFlags.Static);
            if (tol != null)
            {
                return (double)tol.Invoke(null, new object[] { options, geometry, energyKev });
            }

            return options.PeakToleranceHalfBin ? 0.5 * options.BinKev : 0.0;
        }

        static string F(double v, string format)
        {
            return double.IsNaN(v) ? "-" : v.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
