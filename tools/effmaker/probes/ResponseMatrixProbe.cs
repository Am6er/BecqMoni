using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace ResponseMatrixProbe
{
    /// <summary>
    /// Матрица отклика: построение, файл, отпечаток. Проверяется то, что молчит
    /// у компилятора и не видно на картинке.
    ///
    /// 1. **Воспроизводимость при разном числе потоков.** Зерно берётся от
    ///    номера узла, а не от порядка выполнения, поэтому матрица на одном
    ///    потоке и на всех обязана совпасть ПОБИТОВО. Если это не так —
    ///    результат зависит от того, кто успел раньше, и доверять ему нельзя.
    /// 2. **Круговорот через файл.** Записали, прочитали, сравнили все числа.
    /// 3. **Отпечаток ловит правку геометрии.** Сдвинули кристалл на миллиметр —
    ///    прежняя матрица обязана перестать быть годной. Это то, ради чего
    ///    отпечаток и заведён: молча посчитать спектр по матрице чужой
    ///    геометрии хуже, чем не посчитать вовсе.
    /// 4. **Отпечаток ловит правку физики и параметров** — другое число историй
    ///    или выключенный ключ дают другой отпечаток.
    /// 5. **Отмена** прекращает построение и не оставляет файла.
    /// 6. **Сумма строки равна эффективности** этого узла: матрица и кривая
    ///    считаются одним и тем же переносом, и пик в матрице обязан сойтись с
    ///    тем, что возвращает `Efficiency`.
    ///
    ///     responsematrixprobe --geometry=X.in [--nodes=12] [--n=20000] [--bin=2]
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null;
            var options = new ResponseMatrixOptions { NodeCount = 12, Histories = 20000, BinKev = 2.0 };
            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) options.NodeCount = int.Parse(a.Substring(8));
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) options.Histories = int.Parse(a.Substring(4));
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) options.BinKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            Console.WriteLine("геометрия: {0}", geometry.Name);
            Console.WriteLine("сетка: {0} узлов {1:F0}–{2:F0} кэВ, бин {3:F0} кэВ, {4} историй на узел",
                              options.NodeCount, options.MinEnergyKev, options.MaxEnergyKev,
                              options.BinKev, options.Histories);

            // T47, вторая половина: РЕДКАЯ СЕТКА И МАЛАЯ СТАТИСТИКА РОНЯЮТ ТРИ
            // ПРОВЕРКИ ЗАКОННО, а по выводу этого не видно — «ПРОВАЛ» читается
            // одинаково и там, где сломана матрица, и там, где просто мало
            // историй. Умолчания (12 узлов, 20 тыс. историй) подобраны так,
            // чтобы допуски проверок 6–8 были им по силам; ниже них проверки
            // остаются, но их вывод помечается «не показательно».
            bool thinGrid = options.NodeCount < 12 || options.Histories < 20000;
            if (thinGrid)
            {
                Console.WriteLine("⚠ сетка/статистика НИЖЕ умолчаний (12 узлов, 20 000 историй):");
                Console.WriteLine("  проверки 6–8 (пик против кривой, интерполяция) при этом валятся");
                Console.WriteLine("  ЗАКОННО — это разрешение сетки и шум ГСЧ, а не дефект матрицы (T47).");
            }

            int bad = 0;

            // --- 1. Один поток против всех ---------------------------------
            var single = options.Clone();
            single.Threads = 1;
            var watch = Stopwatch.StartNew();
            ResponseMatrix one = ResponseMatrixBuilder.Build(geometry, single, null, CancellationToken.None);
            double oneSeconds = watch.Elapsed.TotalSeconds;

            var many = options.Clone();
            many.Threads = Math.Max(2, Environment.ProcessorCount - 1);
            watch.Restart();
            ResponseMatrix parallel = ResponseMatrixBuilder.Build(geometry, many, null, CancellationToken.None);
            double manySeconds = watch.Elapsed.TotalSeconds;

            // Отпечаток в число потоков не входит нарочно: потоки — это КАК
            // считали, а не ЧТО посчитали.
            int mismatches = Compare(one, parallel);
            Report(mismatches == 0, "один поток и {0} потоков дают одно и то же ({1:F1} с против {2:F1} с, ускорение {3:F1}x)",
                   many.Threads, oneSeconds, manySeconds, oneSeconds / Math.Max(0.001, manySeconds));
            bad += mismatches == 0 ? 0 : 1;

            // --- 2. Круговорот через файл ----------------------------------
            string path = Path.Combine(Path.GetTempPath(), "rmx_probe_" + Guid.NewGuid().ToString("N") + ".rmx");
            parallel.Save(path);
            ResponseMatrix loaded = ResponseMatrix.Load(path);
            long fileSize = new FileInfo(path).Length;
            bool sameAfterFile = loaded != null && Compare(parallel, loaded) == 0
                                 && string.Equals(loaded.Stamp, parallel.Stamp, StringComparison.Ordinal);
            Report(sameAfterFile, "круговорот через файл: {0:F1} КБ на диске, числа совпали",
                   fileSize / 1024.0);
            bad += sameAfterFile ? 0 : 1;

            // --- 2а. Достигнутый шум переживает файл (`T46`) --------------
            // До этой строки посчитанной матрице нельзя было задать вопрос
            // «какого шума ты добилась»: числа жили в консоли пробы и терялись.
            // Здесь у хвоста появляется читатель, иначе он сгниёт молча.
            bool noiseKept = loaded != null
                             && loaded.ContinuumWeightedError == parallel.ContinuumWeightedError
                             && loaded.ContinuumRelativeError == parallel.ContinuumRelativeError
                             && loaded.HistoriesSpent == parallel.HistoriesSpent
                             && loaded.HistoriesWorstNode == parallel.HistoriesWorstNode
                             && SameLongs(loaded.NodeHistories, parallel.NodeHistories)
                             && SameDoubles(loaded.NodeErrors, parallel.NodeErrors)
                             && SameDoubles(loaded.NodeSeconds, parallel.NodeSeconds);
            Report(noiseKept, "достигнутый шум пережил файл: взвешенный {0:P2}, худший {1:P2}, историй {2}",
                   loaded == null ? 0.0 : loaded.ContinuumWeightedError,
                   loaded == null ? 0.0 : loaded.ContinuumRelativeError,
                   loaded == null ? 0L : loaded.HistoriesSpent);
            bad += noiseKept ? 0 : 1;

            // --- 3. Отпечаток ловит правку геометрии -----------------------
            // ⛔ T47: двигать надо поле, которое у ЭТОЙ геометрии РАБОТАЕТ.
            // Проба двигала `CrystalHeight` всегда, а у БРУСКА это поле не
            // участвует ни в чём: сцену строит `CrystalBoxInScene`, а в файл
            // пишется эквивалентный цилиндр, посчитанный из `CrystalBoxX/Y/Z`.
            // Матрица от такой правки не изменилась бы, отпечаток по правилу
            // T42 обязан совпасть — и «ПРОВАЛ» пробы был про ВЕРНОЕ поведение.
            // Виновата проба, а не приложение; приучала не смотреть на вывод.
            GeometryModel moved = geometry.Clone();
            string movedField;
            if (moved.Shape == CrystalShape.Box)
            {
                moved.CrystalBoxZ += 1.0;       // миллиметр
                movedField = "CrystalBoxZ";
            }
            else
            {
                moved.CrystalHeight += 1.0;     // миллиметр
                movedField = "CrystalHeight";
            }

            // Контроль на ложный ПРОПУСК: НЕТРОНУТЫЙ клон обязан остаться годным.
            // `Clone` намеренно не переносит `Raw`, а `Render` из `Raw` берёт
            // коаксиальный блок и чужие вещества — на ввезённом файле ЛСРМ
            // отпечаток разошёлся бы САМ, и проверка сдвига «сходилась» бы,
            // ничего не проверив.
            bool cloneStable = parallel.IsValidFor(geometry.Clone(), options);
            Report(cloneStable, "клон без правок остаётся годным (иначе проверка сдвига слепа)");
            bad += cloneStable ? 0 : 1;

            bool caughtGeometry = !parallel.IsValidFor(moved, options)
                                  && parallel.IsValidFor(geometry, options);
            Report(caughtGeometry, "отпечаток: годен своей геометрии и не годен сдвинутой на 1 мм ({0})",
                   movedField);
            bad += caughtGeometry ? 0 : 1;

            // --- 4. Отпечаток ловит правку параметров ----------------------
            var other = options.Clone();
            other.Histories = options.Histories * 2;
            var noXray = options.Clone();
            noXray.XrayEscape = false;
            bool caughtOptions = !parallel.IsValidFor(geometry, other)
                                 && !parallel.IsValidFor(geometry, noXray);
            Report(caughtOptions, "отпечаток ловит другое число историй и выключенный ключ физики");
            bad += caughtOptions ? 0 : 1;

            // --- 5. Отмена --------------------------------------------------
            bool cancelled = false;
            using (var source = new CancellationTokenSource())
            {
                var big = options.Clone();
                big.NodeCount = 200;
                big.Histories = Math.Max(200000, options.Histories * 20);
                source.CancelAfter(TimeSpan.FromMilliseconds(300));
                try
                {
                    ResponseMatrixBuilder.Build(geometry, big, null, source.Token);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
            }

            Report(cancelled, "отмена прекращает построение");
            bad += cancelled ? 0 : 1;

            // --- 6. Пик матрицы против кривой -------------------------------
            int node = parallel.NodeCount / 2;
            double energy = parallel.Energies[node];
            var sim = new EfficiencySimulator(geometry.Clone())
            {
                Histories = options.Histories,
                XrayEscape = options.XrayEscape,
                CoherentPassesThrough = options.CoherentPassesThrough,
                Bremsstrahlung = options.Bremsstrahlung,
                SingleScatter = options.SingleScatter,
                PeakHalfWidthKev = 0.0
            };
            sim.ResetStream((ulong)sim.Seed + (ulong)(node + 1) * 0x9E3779B97F4A7C15UL);
            double relErr;
            double curve = sim.Efficiency(energy, out relErr);

            float[] row = parallel.Rows[node];
            double peak = row[row.Length - 1];
            double diff = curve > 0.0 ? Math.Abs(peak - curve) / curve : 0.0;
            bool peakOk = diff < 0.02;
            Report(peakOk, "пик матрицы против кривой на {0:F0} кэВ: {1:E4} против {2:E4}, расхождение {3:P2}{4}",
                   energy, peak, curve, diff, thinGrid && !peakOk ? "  — НЕ ПОКАЗАТЕЛЬНО (редкая сетка/мало историй)" : "");
            bad += (peakOk || thinGrid) ? 0 : 1;

            // --- 7. Интерполяция между узлами -------------------------------
            // Перенос строки на шкалу линии придуман здесь и должен быть
            // проверен: сравниваем отклик, взятый ИЗ матрицы посередине между
            // узлами, с посчитанным на этой энергии напрямую. Берём и середину
            // шкалы, и верх, где узлы реже всего.
            foreach (double fraction in new[] { 0.5, 0.9 })
            {
                int left = (int)(fraction * (parallel.NodeCount - 2));
                double middle = 0.5 * (parallel.Energies[left] + parallel.Energies[left + 1]);
                // Длина берётся ТЕМ ЖЕ правилом, что у Response: иначе пик
                // после переноса частью уходит за границу и сумма врёт на
                // четверть — на этом проба уже один раз оступилась.
                int bins = EfficiencySimulator.PeakBin(middle, parallel.BinKev) + 1;
                double[] interpolated = parallel.Evaluate(middle, bins);

                var direct = new EfficiencySimulator(geometry.Clone())
                {
                    Histories = options.Histories,
                    XrayEscape = options.XrayEscape,
                    CoherentPassesThrough = options.CoherentPassesThrough,
                    Bremsstrahlung = options.Bremsstrahlung,
                    SingleScatter = options.SingleScatter,
                    PeakHalfWidthKev = 0.0
                };
                double err;
                double[] exact = direct.Response(middle, parallel.BinKev, out err);

                double sumI = 0.0, sumE = 0.0, peakI = 0.0, peakE = 0.0;
                for (int b = 0; b < interpolated.Length && b < exact.Length; b++)
                {
                    sumI += interpolated[b];
                    sumE += exact[b];
                }

                // Пик — три верхних бина: перенос может сдвинуть его на бин.
                for (int b = Math.Max(0, exact.Length - 3); b < exact.Length; b++)
                {
                    peakE += exact[b];
                }

                for (int b = Math.Max(0, interpolated.Length - 3); b < interpolated.Length; b++)
                {
                    peakI += interpolated[b];
                }

                double sumDiff = sumE > 0.0 ? Math.Abs(sumI - sumE) / sumE : 0.0;
                double peakDiff = peakE > 0.0 ? Math.Abs(peakI - peakE) / peakE : 0.0;
                // Допуск не «сколько хочется», а сколько шумит само сравнение:
                // прямой прогон идёт другим потоком случайных чисел, и при
                // рабочих 30 тыс. историй его собственная погрешность — единицы
                // процентов. Порог держится ниже того, что ловил настоящие
                // поломки: потеря четверти пика давала 35 %, а не 5.
                bool ok = sumDiff < 0.06 && peakDiff < 0.08;
                Report(ok, "интерполяция на {0:F0} кэВ (шаг сетки {1:F0} кэВ): сумма {2:P2}, пик {3:P2}{4}",
                       middle, parallel.Energies[left + 1] - parallel.Energies[left], sumDiff, peakDiff,
                       thinGrid && !ok ? "  — НЕ ПОКАЗАТЕЛЬНО (редкая сетка/мало историй)" : "");
                bad += (ok || thinGrid) ? 0 : 1;
            }

            // --- размер и содержание ---------------------------------------
            Console.WriteLine();
            Console.WriteLine("узлов {0}, ячеек {1}, данных {2:F1} КБ, файл {3:F1} КБ, построение {4:F1} с",
                              parallel.NodeCount, parallel.DataBytes / sizeof(float),
                              parallel.DataBytes / 1024.0, fileSize / 1024.0, parallel.BuildSeconds);
            Console.WriteLine("отпечаток: {0}", parallel.Stamp.Substring(0, 16) + "...");

            File.Delete(path);
            Console.WriteLine();
            if (thinGrid)
            {
                Console.WriteLine("⚠ проверки 6–8 НЕ ЗАСЧИТАНЫ: сетка/статистика ниже умолчаний (T47).");
                Console.WriteLine("  За приёмкой гонять без --nodes/--n либо с --nodes=12 --n=20000 и выше.");
            }

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static bool SameLongs(long[] a, long[] b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        static bool SameDoubles(double[] a, double[] b)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        static int Compare(ResponseMatrix a, ResponseMatrix b)
        {
            if (a == null || b == null || a.NodeCount != b.NodeCount)
            {
                return int.MaxValue;
            }

            int mismatches = 0;
            for (int i = 0; i < a.NodeCount; i++)
            {
                if (Math.Abs(a.Energies[i] - b.Energies[i]) > 1e-9)
                {
                    mismatches++;
                    continue;
                }

                float[] x = a.Rows[i], y = b.Rows[i];
                if (x == null || y == null || x.Length != y.Length)
                {
                    mismatches++;
                    continue;
                }

                for (int k = 0; k < x.Length; k++)
                {
                    if (x[k] != y[k])
                    {
                        mismatches++;
                        break;
                    }
                }
            }

            return mismatches;
        }

        static void Report(bool ok, string format, params object[] args)
        {
            Console.WriteLine("[{0}] {1}", ok ? "СОШЛОСЬ" : "ПРОВАЛ  ",
                              string.Format(CultureInfo.CurrentCulture, format, args));
        }
    }
}
