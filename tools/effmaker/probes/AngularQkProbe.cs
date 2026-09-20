using BecquerelMonitor.EfficiencyMaker;
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
    /// ЧИТАТЕЛЬ-КОНТРОЛЬ КОЭФФИЦИЕНТОВ ОСЛАБЛЕНИЯ УГЛОВОЙ КОРРЕЛЯЦИИ Q_k(E),
    /// ЛЕЖАЩИХ В МАТРИЦЕ (`AMBER46`, П87 16.09.2026; заведена П49 13.09.2026
    /// как ПИСАТЕЛЬ сайдкаров `.qk` — писатель снят по постановке Amber
    /// 16.09.2026 «Никаких сайдкаров. Стоп.»: Q_k теперь строит сам построитель
    /// матрицы из ТЕХ ЖЕ историй, что её строки, и кладёт блоком `ANGK`
    /// формата 9).
    ///
    /// Что делает. На КАЖДОМ узле лежащей матрицы `&lt;ключ&gt;.rmx` проба
    /// повторяет счёт узла независимым путём — ПОДСМОТРОМ направления
    /// отражением, как П49: симулятор собирается ШТАТНЫМ
    /// `ResponseMatrixBuilder.MakeSimulator` по настройкам матрицы (то же зерно
    /// узла, тот же допуск пика), точка — `source.NextWeighted`, состояние ГСЧ
    /// сохраняется, штатные `InCone`/`Isotropic` зовутся ровно так, как их зовёт
    /// `OneHistory`, состояние возвращается — и `OneHistory` разыгрывает ТО ЖЕ
    /// направление, отдавая пиковый счёт (возврат) и полный занос (гистограмма
    /// из трёх бинов). Моменты копятся ТЕМ ЖЕ классом `AngularMomentSums`, что у
    /// построителя, и сравниваются с блоком матрицы:
    ///
    ///   * историй столько же, сколько записано у узла матрицы (умолчание), —
    ///     ожидание ПОБИТОВОЕ: десять чисел узла обязаны совпасть до бита;
    ///   * `--n=` иное — ожидание в шуме: |Δ| ≤ tol·√(σ²_матрицы + σ²_пробы).
    ///
    /// Два положительных контроля: (1) подсмотр не сдвигает поток — на первом
    /// проверяемом узле штатный `Efficiency(E)` тем же зерном и числом историй
    /// обязан совпасть с моим средним ДО БИТА; (2) `--plant=&lt;узел&gt;` портит Q₂
    /// этого узла в ПРОЧИТАННОЙ матрице на +0.05 — проба ОБЯЗАНА покраснеть
    /// (код 1); не покраснела — слепа.
    ///
    /// Ключи:
    ///   --store=&lt;каталог&gt;   склад сцен (`&lt;ключ&gt;.in` + `&lt;ключ&gt;.rmx`); умолчание —
    ///                       `tools\CORPUS\corpus\geometries` от корня дерева
    ///                       (⛔ живой склад проба НЕ ПИШЕТ — только читает)
    ///   --scene=a,b,c       ключи сцен; `--all` — все `.rmx` склада
    ///   --n=N               историй на узел; 0 (умолчание) — как у матрицы, побитово
    ///   --every=K           только каждый K-й узел матрицы (быстрая проверка)
    ///   --threads=N         по узлам
    ///   --tol=3.0           допуск в σ при `--n=` ≠ матрице
    ///   --plant=&lt;узел&gt;     положительный контроль: испортить Q₂ узла в памяти
    /// Код возврата: 0 — все узлы всех сцен сошлись и контроль подсмотра до
    /// бита; 1 — расхождение (узлы названы); 2 — ключи/файлы.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string store = null;
            var scenes = new List<string>();
            bool all = false;
            int n = 0, every = 1, threads = Environment.ProcessorCount, plant = -1;
            double tol = 3.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
                else if (a.StartsWith("--scene=", StringComparison.Ordinal)) scenes.AddRange(a.Substring(8).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a == "--all") all = true;
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--every=", StringComparison.Ordinal)) every = Math.Max(1, int.Parse(a.Substring(8), CultureInfo.InvariantCulture));
                else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--tol=", StringComparison.Ordinal)) tol = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--plant=", StringComparison.Ordinal)) plant = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
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
                foreach (string f in Directory.GetFiles(store, "*.rmx"))
                {
                    scenes.Add(Path.GetFileNameWithoutExtension(f));
                }
            }

            if (scenes.Count == 0)
            {
                Console.Error.WriteLine("сцен не названо: --scene=a,b или --all");
                return 2;
            }

            Console.WriteLine("AngularQkProbe (AMBER46, читатель-контроль): склад {0}", store);
            Console.WriteLine("историй на узел: {0}; узлы: {1}; потоков {2}; допуск {3} σ{4}",
                              n > 0 ? n.ToString(CultureInfo.InvariantCulture) + " (ожидание — в шуме)" : "как у матрицы (ожидание — ДО БИТА)",
                              every > 1 ? "каждый " + every.ToString(CultureInfo.InvariantCulture) + "-й" : "все",
                              threads, F(tol, 1),
                              plant >= 0 ? "; ⚠ ПОДСАДКА: Q2 узла " + plant.ToString(CultureInfo.InvariantCulture) + " испорчен на +0.05 — проба ОБЯЗАНА покраснеть" : "");
            Console.WriteLine();

            int bad = 0;
            foreach (string key in scenes)
            {
                try
                {
                    if (!Scene(store, key, n, every, threads, tol, plant))
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
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ: Q_k матрицы = независимый подсмотр на всех проверенных узлах" : "⛔ ОТКАЗОВ: " + bad);
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

        /// <summary>Итог одного узла: накопители подсмотра и контроль потока.</summary>
        sealed class Node
        {
            public int Index;
            public double E;
            public AngularMomentSums Sums;
            public double Control;              // штатный Efficiency(E) тем же зерном — контроль до бита
            public double Mine;                 // мой обход — среднее пикового счёта
        }

        static bool Scene(string store, string key, int n, int every, int threads, double tol, int plant)
        {
            CheckReflection();
            string inPath = Path.Combine(store, key + ".in");
            string matrixPath = Path.Combine(store, key + ".rmx");
            if (!File.Exists(inPath) || !File.Exists(matrixPath))
            {
                Console.WriteLine("⛔ {0}: нет файла сцены или матрицы ({1}, {2})", key, inPath, matrixPath);
                return false;
            }

            GeometryModel geometry = GeometryModel.Load(inPath);
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrix.Load(matrixPath, out refusal, out fileFormat);
            if (matrix == null)
            {
                Console.WriteLine("⛔ {0}: матрица не прочиталась — {1}{2}", key, refusal,
                                  refusal == MatrixRefusal.OldFormat
                                      ? " (формат " + fileFormat.ToString(CultureInfo.InvariantCulture)
                                        + ", читаем " + ResponseMatrix.FormatVersion.ToString(CultureInfo.InvariantCulture) + ")"
                                      : "");
                return false;
            }

            AngularAttenuation qk = matrix.AngularQk;
            if (qk == null || qk.Count != matrix.Energies.Length)
            {
                Console.WriteLine("⛔ {0}: у матрицы формата {1} НЕТ блока Q_k (собрана не построителем?) — сравнивать нечего",
                                  key, fileFormat);
                return false;
            }

            if (!matrix.IsValidFor(geometry))
            {
                Console.WriteLine("⛔ {0}: клеймо матрицы не сходится с `.in` — узлы считались бы по другой сцене", key);
                return false;
            }

            ResponseMatrixOptions options = matrix.Options.Clone();
            Console.WriteLine("=== {0}: {1}; матрица формат {2}, {3} узлов, клеймо {4}", key, geometry.Describe(),
                              fileFormat, matrix.Energies.Length, matrix.Stamp);

            if (plant >= 0)
            {
                if (plant >= qk.Count)
                {
                    Console.WriteLine("⛔ --plant={0}: узла нет (узлов {1})", plant, qk.Count);
                    return false;
                }

                qk.Q2[plant] += 0.05;
                Console.WriteLine("⚠ ПОДСАДКА: Q2 узла {0} ({1} кэВ) испорчен на +0.05 в памяти", plant, F(qk.Energies[plant], 1));
            }

            var picked = new List<int>();
            for (int i = 0; i < matrix.Energies.Length; i += every)
            {
                picked.Add(i);
            }

            if (plant >= 0 && !picked.Contains(plant))
            {
                picked.Add(plant);
            }

            Node[] result = new Node[picked.Count];
            var failures = new List<string>();
            DateTime started = DateTime.Now;
            Parallel.For(0, picked.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) }, slot =>
            {
                int index = picked[slot];
                try
                {
                    long histories = n > 0 ? n : qk.Histories[index];
                    result[slot] = Compute(geometry, options, index, matrix.Energies[index],
                                           (int)Math.Min(int.MaxValue, Math.Max(1L, histories)), slot == 0);
                }
                catch (Exception ex)
                {
                    lock (failures)
                    {
                        failures.Add(F(matrix.Energies[index], 1) + " кэВ: " + ex.Message);
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

            // Положительный контроль подсмотра направления: первый проверяемый
            // узел гонится дважды — штатным Efficiency(E) и моим обходом, оба с
            // одного зерна; средние обязаны совпасть до бита.
            bool controlOk = result[0].Control == result[0].Mine;
            Console.WriteLine("контроль подсмотра (узел {0} кэВ, зерно узла): штатный Efficiency = {1:R}, мой обход = {2:R} — {3}",
                              F(result[0].E, 1), result[0].Control, result[0].Mine,
                              controlOk ? "ДО БИТА" : "⛔ РАСХОДЯТСЯ, подсмотр сдвигает поток");

            Console.WriteLine("   узел   E, кэВ   ист.матр  ист.пробы     Q2 матр    Q2 проба   ΔQ2/σ     Q4 матр    Q4 проба   ΔQ4/σ    Q2T матр   Q2T проба  ΔQ2T/σ   вердикт");
            int badNodes = 0, bitwiseNodes = 0, noiseNodes = 0;
            double worstSigma = 0.0, worstAbs = 0.0;
            foreach (Node node in result)
            {
                int i = node.Index;
                AngularMomentSums m = node.Sums;
                bool sameHistories = m.N == qk.Histories[i];
                string verdict;
                bool ok;
                double[] mine = { m.Q(2), m.Q(4), m.Err(2), m.Err(4), m.QT(2), m.QT(4), m.ErrT(2), m.ErrT(4),
                                  m.PeakEfficiency, m.TotalEfficiency };
                double[] theirs = { qk.Q2[i], qk.Q4[i], qk.Q2Err[i], qk.Q4Err[i], qk.Q2T[i], qk.Q4T[i],
                                    qk.Q2TErr[i], qk.Q4TErr[i], qk.PeakEff[i], qk.TotalEff[i] };
                if (sameHistories)
                {
                    // Тот же код накопителя, то же зерно, то же число историй —
                    // десять чисел обязаны совпасть ДО БИТА.
                    int off = 0;
                    for (int c = 0; c < mine.Length; c++)
                    {
                        if (mine[c] != theirs[c])
                        {
                            off++;
                        }
                    }

                    ok = off == 0;
                    verdict = ok ? "ДО БИТА" : "⛔ РАЗОШЛИСЬ (" + off.ToString(CultureInfo.InvariantCulture) + " из 10 чисел)";
                    if (ok)
                    {
                        bitwiseNodes++;
                    }
                }
                else
                {
                    // Иное число историй — независимая выборка: в шуме обеих.
                    double worst = 0.0;
                    int[] pairs = { 0, 1, 4, 5 };
                    int[] errs = { 2, 3, 6, 7 };
                    for (int c = 0; c < pairs.Length; c++)
                    {
                        double sigma = Math.Sqrt(theirs[errs[c]] * theirs[errs[c]] + mine[errs[c]] * mine[errs[c]]);
                        double d = Math.Abs(mine[pairs[c]] - theirs[pairs[c]]);
                        double z = sigma > 0.0 ? d / sigma : (d > 0.0 ? double.PositiveInfinity : 0.0);
                        worst = Math.Max(worst, z);
                        worstAbs = Math.Max(worstAbs, d);
                    }

                    ok = worst <= tol;
                    worstSigma = Math.Max(worstSigma, worst);
                    verdict = ok ? "в шуме (худшее " + F(worst, 2) + " σ)" : "⛔ ВНЕ ДОПУСКА (" + F(worst, 2) + " σ)";
                    if (ok)
                    {
                        noiseNodes++;
                    }
                }

                if (!ok)
                {
                    badNodes++;
                }

                Console.WriteLine("   {0,4}  {1,8}  {2,9}  {3,9}   {4,9} {5,9} {6,7}   {7,9} {8,9} {9,7}   {10,9} {11,9} {12,7}   {13}",
                                  i, F(node.E, 1), qk.Histories[i], m.N,
                                  F(theirs[0], 5), F(mine[0], 5), Z(mine[0], theirs[0], mine[2], theirs[2]),
                                  F(theirs[1], 5), F(mine[1], 5), Z(mine[1], theirs[1], mine[3], theirs[3]),
                                  F(theirs[4], 5), F(mine[4], 5), Z(mine[4], theirs[4], mine[6], theirs[6]),
                                  verdict);
            }

            Console.WriteLine("узлов проверено {0}: до бита {1}, в шуме {2}, расхождений {3}{4}; {5} с",
                              result.Length, bitwiseNodes, noiseNodes, badNodes,
                              noiseNodes + badNodes > 0 && !double.IsInfinity(worstSigma)
                                  ? "; худшее " + F(worstSigma, 2) + " σ, наибольшее |Δ| " + F(worstAbs, 5) : "",
                              F((DateTime.Now - started).TotalSeconds, 1));
            Console.WriteLine();
            return controlOk && badNodes == 0;
        }

        static string Z(double a, double b, double sa, double sb)
        {
            double sigma = Math.Sqrt(sa * sa + sb * sb);
            double d = a - b;
            if (d == 0.0)
            {
                return "=";
            }

            return sigma > 0.0 ? F(d / sigma, 2) : "∞";
        }

        /// <summary>
        /// Один узел: штатный симулятор узла (тот же индекс, энергия, зерно,
        /// допуск, что у построителя), n историй, у каждой — подсмотр
        /// направления. При `control` узел сперва гонится штатным
        /// `Efficiency(E)` с тем же зерном (контроль подсмотра), затем поток
        /// сбрасывается на то же зерно.
        /// </summary>
        static Node Compute(GeometryModel geometry, ResponseMatrixOptions options, int index, double energyKev,
                            int n, bool control)
        {
            var sim = (EfficiencySimulator)MakeSimulator.Invoke(null, new object[] { geometry, options, index, energyKev });
            EnsureBuilt.Invoke(sim, null);
            int seed = options.Seed != 0 ? options.Seed : sim.Seed;
            ulong streamSeed = (ulong)seed + (ulong)(index + 1) * 0x9E3779B97F4A7C15UL;

            var node = new Node { Index = index, E = energyKev, Sums = new AngularMomentSums() };
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
            // n историй «точка с весом → история». `Run` берёт n не меньше 1000.
            int histories = Math.Max(1000, n);
            retune.Invoke(source, new object[] { sim, energyKev });
            object[] pointArgs = new object[4];
            object[] dirArgs;
            object[] histArgs = new object[7];
            // Полный занос — той же историей: гистограмма из ТРЁХ бинов
            // (полбина = E/2) копит любой занос в кристалл; пустой занос в неё
            // не попадает (`Deposit` пропускает ноль), случайных чисел она не
            // тянет — контроль до бита это подтверждает. Сумма трёх бинов — ровно
            // то, что построитель копит полем `historyDeposit`.
            double[] hist = new double[3];
            double binKev = energyKev / 2.0;
            double sum = 0.0;
            for (int i = 0; i < histories; i++)
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
                sum += score;

                // Ось — на центр кристалла из точки вылета; точка в центре — ось z
                // (П49 §1.1, то же выражение, что в `OneHistory`).
                double cos = dist > 0.0 ? (ux * (-x) + uy * (-y) + uz * dz) / dist : uz;
                node.Sums.Add(score, total, cos);
            }

            node.Mine = sum / histories;
            return node;
        }
    }
}
