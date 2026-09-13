using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace AngularQkProbe
{
    /// <summary>
    /// КОЭФФИЦИЕНТЫ ОСЛАБЛЕНИЯ УГЛОВОЙ КОРРЕЛЯЦИИ Q_k(E) СЦЕНЫ (`N14`, П49
    /// 13.09.2026) — геометрическая половина корреляции, которой у сумматора
    /// нет. Считает тем же переносом, что и матрица (<see cref="EfficiencySimulator"/>,
    /// симулятор собран ШТАТНЫМ `ResponseMatrixBuilder.MakeSimulator` по настройкам
    /// лежащей матрицы), и кладёт сайдкар `&lt;ключ&gt;.qk` рядом с `&lt;ключ&gt;.in`.
    ///
    ///     Q_k(E) = Σ_i s_i·P_k(cos θ_i) / Σ_i s_i,   k = 2, 4,
    ///
    /// где s_i — пиковый счёт истории (вес × попадание в пик полного поглощения),
    /// θ_i — угол вылета к оси «точка вылета → центр кристалла», сумма — по
    /// точкам розыгрыша и направлениям.
    ///
    /// ⛔ `EfficiencyMaker` НЕ ПРАВИТСЯ (ночной счёт склада идёт из HEAD), а
    /// направление история разыгрывает внутри себя. Поэтому направление
    /// ПОДСМАТРИВАЕТСЯ: состояние ГСЧ сохраняется, штатные `InCone`/`Isotropic`
    /// зовутся отражением ровно так, как их зовёт `OneHistory`, состояние
    /// возвращается — и `OneHistory` разыгрывает ТО ЖЕ направление. Что подсмотр
    /// не сдвигает поток, доказывает положительный контроль в начале каждой
    /// сцены: средний счёт моего обхода обязан совпасть ДО БИТА со штатным
    /// `Efficiency(E)` при том же зерне и том же числе историй.
    ///
    /// Ключи:
    ///   --store=&lt;каталог&gt;   склад сцен (`&lt;ключ&gt;.in` + `&lt;ключ&gt;.rmx`); умолчание —
    ///                       `tools\CORPUS\corpus\geometries` от корня дерева
    ///   --scene=a,b,c       ключи сцен; `--all` — все `.in` склада
    ///   --out=&lt;каталог&gt;     куда класть `.qk` (умолчание — склад)
    ///   --n=100000          историй на узел
    ///   --nodes=24 --emin=30 --emax=3000   сетка узлов, log-равномерная
    ///   --threads=N         по узлам
    ///   --seed=N            зерно настроек (0 — зерно склада/умолчание)
    ///   --far=&lt;мм&gt;          КОНТРОЛЬ ПРЕДЕЛА: точечный источник отодвигается
    ///                       на это расстояние; рядом печатается геометрическое
    ///                       Q_k чёрного диска того же радиуса (Q_k → 1)
    ///   --no-save           только печать
    /// Код возврата: 0 — все сцены посчитаны и контроль сошёлся; 1 — отказ.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string store = null, outDir = null;
            var scenes = new List<string>();
            bool all = false, save = true;
            int n = 100000, nodes = 24, threads = Environment.ProcessorCount, seed = 0;
            double emin = 30.0, emax = 3000.0, far = 0.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--scene=", StringComparison.Ordinal)) scenes.AddRange(a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a == "--all") all = true;
                else if (a == "--no-save") save = false;
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) nodes = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--emin=", StringComparison.Ordinal)) emin = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--emax=", StringComparison.Ordinal)) emax = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--far=", StringComparison.Ordinal)) far = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (store == null)
            {
                string here = AppDomain.CurrentDomain.BaseDirectory;
                // build_<полоса> → probes → effmaker → tools → корень
                string root = Path.GetFullPath(Path.Combine(here, "..", "..", "..", ".."));
                store = Path.Combine(root, "tools", "CORPUS", "corpus", "geometries");
            }

            if (!Directory.Exists(store))
            {
                Console.Error.WriteLine("склада нет: " + store);
                return 2;
            }

            if (all)
            {
                foreach (string f in Directory.GetFiles(store, "*.in"))
                {
                    scenes.Add(Path.GetFileNameWithoutExtension(f));
                }
            }

            if (scenes.Count == 0)
            {
                Console.Error.WriteLine("сцен не названо: --scene=a,b или --all");
                return 2;
            }

            if (outDir == null)
            {
                outDir = store;
            }

            Directory.CreateDirectory(outDir);

            Console.WriteLine("AngularQkProbe (N14): склад {0}", store);
            Console.WriteLine("узлов {0} ({1}…{2} кэВ, log), историй на узел {3}, потоков {4}, зерно {5}{6}",
                              nodes, F(emin, 1), F(emax, 1), n, threads, seed,
                              far > 0.0 ? ", точечный источник отодвинут на " + F(far, 1) + " мм" : "");
            Console.WriteLine();

            int bad = 0;
            foreach (string key in scenes)
            {
                try
                {
                    if (!Scene(store, outDir, key, n, nodes, emin, emax, threads, seed, far, save))
                    {
                        bad++;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("⛔ {0}: {1}", key, ex.Message);
                    bad++;
                }
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "все сцены посчитаны, контроль сошёлся" : "⛔ ОТКАЗОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static string F(double v, int digits)
        {
            return v.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------
        // Отражение: штатный симулятор, его розыгрыш точки и направления
        // ------------------------------------------------------------------

        const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;

        static readonly Type SimType = typeof(EfficiencySimulator);
        static readonly MethodInfo MakeSimulator = typeof(ResponseMatrixBuilder).GetMethod("MakeSimulator", PrivStatic);
        static readonly MethodInfo EnsureBuilt = SimType.GetMethod("EnsureBuilt", Priv);
        static readonly MethodInfo InCone = SimType.GetMethod("InCone", Priv);
        static readonly MethodInfo Isotropic = SimType.GetMethod("Isotropic", Priv);
        static readonly FieldInfo SourceField = SimType.GetField("source", Priv);
        static readonly FieldInfo SphereZ = SimType.GetField("sphereZ", Priv);
        static readonly FieldInfo SphereR = SimType.GetField("sphereR", Priv);
        static readonly FieldInfo State = SimType.GetField("state", Priv);
        static MethodInfo oneHistory;

        static MethodInfo OneHistory
        {
            get
            {
                if (oneHistory == null)
                {
                    foreach (MethodInfo m in SimType.GetMethods(Priv))
                    {
                        if (m.Name == "OneHistory" && m.GetParameters().Length == 7)
                        {
                            oneHistory = m;
                        }
                    }
                }

                return oneHistory;
            }
        }

        static void CheckReflection()
        {
            if (MakeSimulator == null || EnsureBuilt == null || InCone == null || Isotropic == null
                || SourceField == null || SphereZ == null || SphereR == null || State == null
                || OneHistory == null)
            {
                throw new InvalidOperationException(
                    "отражение не нашло штатных членов симулятора (MakeSimulator/EnsureBuilt/InCone/Isotropic/"
                    + "source/sphereZ/sphereR/state/OneHistory) — код симулятора изменился, проба слепа");
            }
        }

        /// <summary>Накопители одного узла.</summary>
        sealed class Node
        {
            public double E;
            public double S0, S2, S4;           // Σ s, Σ s·P2, Σ s·P4 — пик
            public double S00, S22, S44, S02, S04;  // суммы квадратов и произведений
            public double T0, T2, T4;           // то же для ПОЛНОЙ эффективности (любой занос)
            public double T00, T22, T44, T02, T04;
            public long N;
            public double Control;              // штатный Efficiency(E) тем же зерном — контроль до бита
            public double Mine;                 // мой обход — среднее счёта

            public double Q(int k)
            {
                return this.S0 > 0.0 ? (k == 2 ? this.S2 : this.S4) / this.S0 : 0.0;
            }

            public double QT(int k)
            {
                return this.T0 > 0.0 ? (k == 2 ? this.T2 : this.T4) / this.T0 : 0.0;
            }

            /// <summary>Шум Q_k дельта-методом: var(Q) = [var(sP) − 2Q·cov(sP,s) + Q²·var(s)] / (N·⟨s⟩²).</summary>
            public double Err(int k)
            {
                return Err(this.N, this.S0, k == 2 ? this.S2 : this.S4, this.S00,
                           k == 2 ? this.S22 : this.S44, k == 2 ? this.S02 : this.S04);
            }

            public double ErrT(int k)
            {
                return Err(this.N, this.T0, k == 2 ? this.T2 : this.T4, this.T00,
                           k == 2 ? this.T22 : this.T44, k == 2 ? this.T02 : this.T04);
            }

            static double Err(long n, double s0, double sk, double s00, double skk, double s0k)
            {
                if (n < 2 || !(s0 > 0.0))
                {
                    return 0.0;
                }

                double m0 = s0 / n;
                double mk = sk / n;
                double v0 = s00 / n - m0 * m0;
                double vk = skk / n - mk * mk;
                double c0k = s0k / n - m0 * mk;
                double q = mk / m0;
                double var = (vk - 2.0 * q * c0k + q * q * v0) / (n * m0 * m0);
                return var > 0.0 ? Math.Sqrt(var) : 0.0;
            }
        }

        static bool Scene(string store, string outDir, string key, int n, int nodeCount,
                          double emin, double emax, int threads, int seed, double far, bool save)
        {
            CheckReflection();
            string inPath = Path.Combine(store, key + ".in");
            if (!File.Exists(inPath))
            {
                Console.WriteLine("⛔ {0}: нет файла сцены {1}", key, inPath);
                return false;
            }

            GeometryModel geometry = GeometryModel.Load(inPath);
            string matrixPath = Path.Combine(store, key + ".rmx");
            ResponseMatrix matrix = File.Exists(matrixPath) ? ResponseMatrix.Load(matrixPath) : null;
            ResponseMatrixOptions options = matrix != null && matrix.Options != null
                ? matrix.Options.Clone()
                : new ResponseMatrixOptions();
            if (seed != 0)
            {
                options.Seed = seed;
            }

            string sceneName = key;
            if (far > 0.0)
            {
                if (geometry.SourceType != GeometrySourceType.Point)
                {
                    Console.WriteLine("⛔ {0}: --far= только для точечного источника, здесь {1}", key, geometry.SourceType);
                    return false;
                }

                geometry.PointDistance = far;
                sceneName = key + "_far" + far.ToString("F0", CultureInfo.InvariantCulture);
            }

            Console.WriteLine("=== {0}: {1}; матрица {2}", sceneName, geometry.Describe(),
                              matrix != null ? matrix.Stamp : "НЕТ (умолчания настроек)");

            double[] grid = new double[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                double t = nodeCount > 1 ? (double)i / (nodeCount - 1) : 0.0;
                grid[i] = Math.Round(Math.Exp(Math.Log(emin) + (Math.Log(emax) - Math.Log(emin)) * t), 3);
            }

            Node[] result = new Node[nodeCount];
            var failures = new List<string>();
            DateTime started = DateTime.Now;
            Parallel.For(0, nodeCount, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) }, i =>
            {
                try
                {
                    result[i] = Compute(geometry, options, i, grid[i], n, i == 0);
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add(F(grid[i], 1) + " кэВ: " + ex.Message);
                    }
                }
            });

            if (failures.Count > 0)
            {
                foreach (string f in failures)
                {
                    Console.WriteLine("⛔ узел {0}", f);
                }

                return false;
            }

            // Положительный контроль подсмотра направления: узел 0 гонится
            // дважды — штатным Efficiency(E) и моим обходом, оба с одного
            // зерна; средние обязаны совпасть до бита.
            bool controlOk = result[0].Control == result[0].Mine;
            Console.WriteLine("контроль подсмотра (узел {0} кэВ, зерно узла): штатный Efficiency = {1:R}, мой обход = {2:R} — {3}",
                              F(grid[0], 1), result[0].Control, result[0].Mine,
                              controlOk ? "ДО БИТА" : "⛔ РАСХОДЯТСЯ, подсмотр сдвигает поток");

            Console.WriteLine("   E, кэВ        ε_пика        Q2     ±       Q4     ±   |   ε_полн(взвеш.)     Q2T     ±      Q4T     ±");
            var table = new AngularAttenuation
            {
                Scene = sceneName,
                GeometrySha = AngularAttenuation.FingerprintOf(geometry),
                MatrixStamp = matrix != null ? matrix.Stamp : "",
                Histories = n,
                Seed = options.Seed,
                Built = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                Energies = new double[nodeCount],
                Q2 = new double[nodeCount],
                Q4 = new double[nodeCount],
                Q2Err = new double[nodeCount],
                Q4Err = new double[nodeCount],
                PeakEff = new double[nodeCount],
                Q2T = new double[nodeCount],
                Q4T = new double[nodeCount],
                Q2TErr = new double[nodeCount],
                Q4TErr = new double[nodeCount],
                TotalEff = new double[nodeCount]
            };
            double worstErr = 0.0;
            for (int i = 0; i < nodeCount; i++)
            {
                Node node = result[i];
                table.Energies[i] = node.E;
                table.Q2[i] = node.Q(2);
                table.Q4[i] = node.Q(4);
                table.Q2Err[i] = node.Err(2);
                table.Q4Err[i] = node.Err(4);
                table.PeakEff[i] = node.S0 / node.N;
                table.Q2T[i] = node.QT(2);
                table.Q4T[i] = node.QT(4);
                table.Q2TErr[i] = node.ErrT(2);
                table.Q4TErr[i] = node.ErrT(4);
                table.TotalEff[i] = node.T0 / node.N;
                worstErr = Math.Max(worstErr, Math.Max(Math.Max(table.Q2Err[i], table.Q4Err[i]),
                                                       Math.Max(table.Q2TErr[i], table.Q4TErr[i])));
                Console.WriteLine("   {0,8}  {1,12}  {2,7} {3,6}  {4,7} {5,6}   |   {6,12}  {7,7} {8,6}  {9,7} {10,6}",
                                  F(node.E, 1), table.PeakEff[i].ToString("G5", CultureInfo.InvariantCulture),
                                  F(table.Q2[i], 4), F(table.Q2Err[i], 4), F(table.Q4[i], 4), F(table.Q4Err[i], 4),
                                  table.TotalEff[i].ToString("G5", CultureInfo.InvariantCulture),
                                  F(table.Q2T[i], 4), F(table.Q2TErr[i], 4), F(table.Q4T[i], 4), F(table.Q4TErr[i], 4));
            }

            Console.WriteLine("наибольший шум Q_k по узлам: {0}; посчитано за {1} с",
                              F(worstErr, 4), F((DateTime.Now - started).TotalSeconds, 1));

            if (geometry.SourceType == GeometrySourceType.Point)
            {
                // Геометрический предел: чёрный диск радиуса R на расстоянии d,
                // ε(θ) = 1 внутри конуса, cos θ_max = d/√(d²+R²):
                // Q_k = ∫_c^1 P_k(x) dx / (1 − c).
                double r = geometry.CrystalDiameter / 2.0;
                double d = geometry.PointDistance;
                if (r > 0.0 && d > 0.0)
                {
                    double c = d / Math.Sqrt(d * d + r * r);
                    double q2 = (Int2(1.0) - Int2(c)) / (1.0 - c);
                    double q4 = (Int4(1.0) - Int4(c)) / (1.0 - c);
                    Console.WriteLine("геометрический предел (чёрный диск R = {0} мм на d = {1} мм, без глубины и боковых входов): Q2 = {2}, Q4 = {3}; "
                                      + "у самого низкого узла посчитано Q2 = {4}, Q4 = {5}",
                                      F(r, 1), F(d, 1), F(q2, 4), F(q4, 4), F(table.Q2[0], 4), F(table.Q4[0], 4));
                }
            }

            if (save)
            {
                string outPath = Path.Combine(outDir, sceneName + AngularAttenuation.Extension);
                table.Save(outPath);
                AngularAttenuation back = AngularAttenuation.Load(outPath);
                bool roundTrip = back != null && back.Count == nodeCount
                                 && string.Equals(back.GeometrySha, table.GeometrySha, StringComparison.Ordinal)
                                 && Math.Abs(back.Q(2, 661.7) - table.Q(2, 661.7)) < 1e-5;   // файл хранит шесть знаков
                Console.WriteLine("сайдкар: {0} — {1}", outPath, roundTrip ? "записан и прочитан обратно" : "⛔ ОБРАТНОЕ ЧТЕНИЕ НЕ СОШЛОСЬ");
                if (!roundTrip)
                {
                    return false;
                }
            }

            Console.WriteLine();
            return controlOk;
        }

        static double Int2(double x)
        {
            // ∫ P2 dx = (x³ − x)/2
            return 0.5 * (x * x * x - x);
        }

        static double Int4(double x)
        {
            // ∫ P4 dx = (7x⁵ − 10x³ + 3x)/8
            return (7.0 * x * x * x * x * x - 10.0 * x * x * x + 3.0 * x) / 8.0;
        }

        /// <summary>
        /// Один узел: штатный симулятор узла, n историй, у каждой — подсмотр
        /// направления. Узел 0 сперва гонится штатным `Efficiency(E)` с тем же
        /// зерном (контроль подсмотра), затем поток сбрасывается на то же зерно.
        /// </summary>
        static Node Compute(GeometryModel geometry, ResponseMatrixOptions options, int index, double energyKev,
                            int n, bool control)
        {
            var sim = (EfficiencySimulator)MakeSimulator.Invoke(null, new object[] { geometry, options, index, energyKev });
            EnsureBuilt.Invoke(sim, null);
            int seed = options.Seed != 0 ? options.Seed : sim.Seed;
            ulong streamSeed = (ulong)seed + (ulong)(index + 1) * 0x9E3779B97F4A7C15UL;

            var node = new Node { E = energyKev };
            if (control)
            {
                sim.Histories = n;
                sim.ResetStream(streamSeed);
                double err;
                node.Control = sim.Efficiency(energyKev, out err);
            }

            sim.ResetStream(streamSeed);
            object source = SourceField.GetValue(sim);
            MethodInfo nextWeighted = source.GetType().GetMethod("NextWeighted", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo retune = source.GetType().GetMethod("Retune", BindingFlags.Public | BindingFlags.Instance);
            double sphereZ = (double)SphereZ.GetValue(sim);
            double sphereR = (double)SphereR.GetValue(sim);

            // Ровно как `Run`: перестройка розыгрыша под энергию узла, потом
            // n историй «точка с весом → история».
            retune.Invoke(source, new object[] { sim, energyKev });
            object[] pointArgs = new object[4];
            object[] dirArgs;
            object[] histArgs = new object[7];
            // Полная эффективность — той же историей: гистограмма из ТРЁХ бинов
            // (полбина = E/2) копит любой занос в кристалл; пустой занос в неё
            // не попадает (`Deposit` пропускает ноль), случайных чисел она не
            // тянет — контроль до бита это подтверждает. ⚠ Это взвешенная
            // оценка ε_T (занижена против аналоговой на 12…15 % по уровню);
            // нужна только её угловая ФОРМА.
            double[] hist = new double[3];
            double binKev = energyKev / 2.0;
            for (int i = 0; i < n; i++)
            {
                pointArgs[0] = sim;
                double pointWeight = (double)nextWeighted.Invoke(source, pointArgs);
                double x = (double)pointArgs[1], y = (double)pointArgs[2], z = (double)pointArgs[3];

                // Подсмотр направления: те же ветки, что у `OneHistory`.
                double dz = sphereZ - z;
                double dist = Math.Sqrt(x * x + y * y + dz * dz);
                ulong saved = (ulong)State.GetValue(sim);
                double ux, uy, uz;
                if (dist > sphereR)
                {
                    double cosMax = Math.Sqrt(Math.Max(0.0, 1.0 - sphereR * sphereR / (dist * dist)));
                    dirArgs = new object[] { -x / dist, -y / dist, dz / dist, cosMax, 0.0, 0.0, 0.0 };
                    InCone.Invoke(sim, dirArgs);
                    ux = (double)dirArgs[4]; uy = (double)dirArgs[5]; uz = (double)dirArgs[6];
                }
                else
                {
                    dirArgs = new object[] { 0.0, 0.0, 0.0 };
                    Isotropic.Invoke(sim, dirArgs);
                    ux = (double)dirArgs[0]; uy = (double)dirArgs[1]; uz = (double)dirArgs[2];
                }

                State.SetValue(sim, saved);

                hist[0] = 0.0; hist[1] = 0.0; hist[2] = 0.0;
                histArgs[0] = energyKev; histArgs[1] = x; histArgs[2] = y; histArgs[3] = z;
                histArgs[4] = hist; histArgs[5] = binKev; histArgs[6] = pointWeight;
                double score = (double)OneHistory.Invoke(sim, histArgs);
                double total = hist[0] + hist[1] + hist[2];

                // Ось — на центр кристалла из точки вылета; точка в центре — ось z.
                double cos = dist > 0.0 ? (ux * (-x) + uy * (-y) + uz * dz) / dist : uz;
                double c2 = cos * cos;
                double p2 = 0.5 * (3.0 * c2 - 1.0);
                double p4 = 0.125 * (35.0 * c2 * c2 - 30.0 * c2 + 3.0);
                double s2 = score * p2, s4 = score * p4;
                node.S0 += score; node.S2 += s2; node.S4 += s4;
                node.S00 += score * score; node.S22 += s2 * s2; node.S44 += s4 * s4;
                node.S02 += score * s2; node.S04 += score * s4;
                double t2 = total * p2, t4 = total * p4;
                node.T0 += total; node.T2 += t2; node.T4 += t4;
                node.T00 += total * total; node.T22 += t2 * t2; node.T44 += t4 * t4;
                node.T02 += total * t2; node.T04 += total * t4;
                node.N++;
            }

            node.Mine = node.S0 / n;
            return node;
        }
    }
}
