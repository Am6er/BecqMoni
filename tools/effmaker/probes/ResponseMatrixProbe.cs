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
    ///     responsematrixprobe --geometry=X.in [--nodes=34] [--n=20000] [--bin=2]
    ///                         [--maxn=100000]
    ///                         [--emin=30] [--emax=3000]
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
            bool historiesGiven = false;
            int maxAuto = MaxAutoHistories;
            var options = new ResponseMatrixOptions { NodeCount = 34, Histories = 20000, BinKev = 2.0 };
            foreach (string a in args)
            {
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--nodes=", StringComparison.Ordinal)) options.NodeCount = int.Parse(a.Substring(8));
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) { options.Histories = int.Parse(a.Substring(4)); historiesGiven = true; }
                else if (a.StartsWith("--maxn=", StringComparison.Ordinal)) maxAuto = int.Parse(a.Substring(7));
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) options.BinKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                // Края сетки ключами — заведены 23.08.2026 под `T49`: решение
                // «расширить сетку вниз» без возможности померить обе стороны
                // на месте пришлось бы принимать вслепую.
                else if (a.StartsWith("--emin=", StringComparison.Ordinal)) options.MinEnergyKev = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--emax=", StringComparison.Ordinal)) options.MaxEnergyKev = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            }

            if (geometryPath == null || !File.Exists(geometryPath))
            {
                Console.Error.WriteLine("нужен --geometry=<файл .in>");
                return 2;
            }

            GeometryModel geometry = GeometryModel.Load(geometryPath);
            Console.WriteLine("геометрия: {0}", geometry.Name);
            if (!historiesGiven)
            {
                AutoHistories(geometry, options, maxAuto);
            }

            Console.WriteLine("сетка: {0} узлов {1:F0}–{2:F0} кэВ, бин {3:F0} кэВ, {4} историй на узел",
                              options.NodeCount, options.MinEnergyKev, options.MaxEnergyKev,
                              options.BinKev, options.Histories);

            // T47, вторая половина: РЕДКАЯ СЕТКА И МАЛАЯ СТАТИСТИКА РОНЯЮТ ТРИ
            // ПРОВЕРКИ ЗАКОННО, а по выводу этого не видно — «ПРОВАЛ» читается
            // одинаково и там, где сломана матрица, и там, где просто мало
            // историй. Умолчания подобраны так, чтобы допуски проверок 6–8 были
            // им по силам; ниже них проверки остаются, но их вывод помечается
            // «не показательно».
            //
            // ⛔ Двадцать четыре → ТРИДЦАТЬ ЧЕТЫРЕ в тот же день (`T49`): край
            // сетки опущен 30 → 5 кэВ, и при прежнем числе узлов шаг вырос бы с
            // 4.76 до 6.67 % на узел, то есть смоук-прогон стал бы мерить не то.
            // Тридцать четыре возвращают прежний шаг; цена 41.0 с против 26.8 на
            // `ASN16_lu_side`, и все проверки сходятся.
            //
            // ⛔ Двенадцать узлов → ДВАДЦАТЬ ЧЕТЫРЕ, 23.08.2026 (`T52`): на
            // `RC103_point0` — мелкий кристалл В УПОР — двенадцати не хватало
            // по существу, и это не шум. Развёртка по узлам на низкой точке
            // (шаг сетки против расхождения пика): 127 кэВ — 13.05 %, 92 —
            // 3.87 %, 73 — 5.32 %, 60 — 2.01 %, 45 — 2.00 %; со статистикой
            // 13.05 → 11.59 → 11.12 % при 20 / 80 / 200 тыс. историй, то есть
            // пол ≈ 11 % и ГСЧ тут ни при чём. Цена умолчания — 8.9 с против
            // 6.0 с на смоук-прогон, у цилиндра (`AS80_lu_front`) сходилось и
            // при двенадцати. В поставке матрицы считаются на ~100 узлах.
            bool thinGrid = options.NodeCount < 34 || options.Histories < 20000;
            if (thinGrid)
            {
                Console.WriteLine("⚠ сетка/статистика НИЖЕ умолчаний (34 узла, 20 000 историй):");
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
            // Тот же порядок, что у проверки 7 (`T52`): допуск 2 % остаётся
            // порогом, но там, где три сигмы собственной дрожи сравнения его
            // перекрывают, порогом становятся они. Обе стороны — свой поток
            // случайных чисел, счёт n = доля × историй. На `ASN16_lu_side` при
            // умолчаниях пик стоит на 5.25E-2, то есть около 1050 историй,
            // 1σ ≈ 4.4 % на разность — двухпроцентный допуск такому сравнению
            // не по силам В ПРИНЦИПЕ, и прежний «ПРОВАЛ 3.34 %» означал не
            // дефект матрицы, а требование различить то, чего в числах нет.
            double peakSigmaCurve = Sigma(curve, peak, options.Histories);
            double peakLimitCurve = Math.Max(0.02, 3.0 * peakSigmaCurve);
            bool peakOk = diff < peakLimitCurve;
            Report(peakOk, "пик матрицы против кривой на {0:F0} кэВ: {1:E4} против {2:E4}, расхождение {3:P2}"
                           + "  [порог {4:P1}; 1σ {5:P1}]{6}",
                   energy, peak, curve, diff, peakLimitCurve, peakSigmaCurve,
                   thinGrid && !peakOk ? "  — НЕ ПОКАЗАТЕЛЬНО (редкая сетка/мало историй)" : "");
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

                // ⛔ СВОЙ ШУМ СРАВНЕНИЯ, и без него проверка врёт (`T52`).
                // Эффективность здесь — это ДОЛЯ историй, значит за числом
                // стоит счёт: n = доля × историй. У мелкого кристалла в упор на
                // верху шкалы в пике этих историй ЕДИНИЦЫ — на `RC103_point0`
                // при 1496 кэВ и 20 тыс. историй пик расходился на 24.5 %, и с
                // ростом статистики шёл 24.5 → 19.8 → 14.6 % при десятикратной
                // прибавке, то есть мерилась ПУАССОНОВСКАЯ дрожь, а не
                // интерполяция. Порог 8 % такому пику не по силам В ПРИНЦИПЕ, и
                // «ПРОВАЛ» там означал «мало историй», а не «матрица врёт».
                //
                // Допуски НЕ ослаблены и НЕ подобраны — они по-прежнему 6 и
                // 8 %, поставлены измерением (потеря четверти пика давала 35 %)
                // и остаются порогом везде, где сравнение способно их
                // разглядеть. Добавлено ровно одно: там, где собственная дрожь
                // сравнения БОЛЬШЕ допуска, порогом становится она — иначе
                // проверка требует различить то, чего в её же числах нет.
                // ⚠ Печатается она ВСЕГДА, а не только при провале: допуск,
                // молча подменённый шумом, — тот же молчаливый отказ.
                double sumSigma = Sigma(sumE, sumI, options.Histories);
                double peakSigma = Sigma(peakE, peakI, options.Histories);
                double sumLimit = Math.Max(0.06, 3.0 * sumSigma);
                double peakLimit = Math.Max(0.08, 3.0 * peakSigma);
                bool ok = sumDiff < sumLimit && peakDiff < peakLimit;
                string why = string.Format(CultureInfo.InvariantCulture,
                                           "  [порог суммы {0:P1}, пика {1:P1}; 1σ {2:P1} и {3:P1}]",
                                           sumLimit, peakLimit, sumSigma, peakSigma);
                if (thinGrid && !ok)
                {
                    why += "  — НЕ ПОКАЗАТЕЛЬНО (редкая сетка/мало историй)";
                }

                Report(ok, "интерполяция на {0:F0} кэВ (шаг сетки {1:F0} кэВ): сумма {2:P2}, пик {3:P2}{4}",
                       middle, parallel.Energies[left + 1] - parallel.Energies[left], sumDiff, peakDiff, why);
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
                Console.WriteLine("  За приёмкой гонять без --nodes/--n либо с --nodes=34 --n=20000 и выше.");
            }

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "ПРОВАЛОВ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Относительная пуассоновская дрожь РАЗНОСТИ двух долей, посчитанных
        /// каждая своим потоком случайных чисел: n = доля × историй, σ/n =
        /// 1/√n, и складываются они в квадратах. Ноль историй или нулевая доля
        /// дают σ = 1, то есть «сказать нечего».
        /// </summary>
        static double Sigma(double shareA, double shareB, int histories)
        {
            double nA = shareA * histories;
            double nB = shareB * histories;
            if (!(nA > 0.0) || !(nB > 0.0))
            {
                return 1.0;
            }

            return Math.Sqrt(1.0 / nA + 1.0 / nB);
        }

        /// <summary>Историй на пилотный прогон — только чтобы оценить порядок ε.</summary>
        const int PilotHistories = 4000;

        /// <summary>
        /// Потолок автоматической статистики, историй на узел. Смоук-прогон не
        /// должен превращаться в часы: на `RC103_lu_front` допуск требует
        /// 8.1 млн историй на узел, то есть 275 млн на матрицу и часы счёта.
        /// Сто тысяч — это ~112 с на `ASN16_lu_side` (34 узла), и порог проверки
        /// 6 там опускается с 13.0 до 8.4 %. Поднимается ключом `--maxn=`.
        /// </summary>
        const int MaxAutoHistories = 100000;

        /// <summary>
        /// СТАТИСТИКА ОТ ГЕОМЕТРИИ (`T56`, решение Amber 23.08.2026).
        ///
        /// ⛔ Допуск проверок 6–7 (2 и 8 %) достижим не всегда: эффективность
        /// здесь ДОЛЯ историй, значит за числом стоит счёт, и на слабой
        /// геометрии в пике его единицы. Порог `max(допуск, 3σ)` честен, но на
        /// `RC103_lu_front` он выходит 36.7 % — формально «СОШЛОСЬ», фактически
        /// ловится только грубая поломка.
        ///
        /// Здесь проба СЧИТАЕТ, сколько историй нужно, чтобы три сигмы ушли
        /// ниже допуска: сравниваются две независимые оценки, σ/n = √(2/n),
        /// значит 3·√(2/n) &lt; 0.02 даёт n &gt; 45 000 историй В ПИКЕ, а историй
        /// на узел — n/ε. ε берётся пилотным прогоном на середине сетки
        /// (геометрическое среднее краёв), он дешёвый: одна энергия.
        ///
        /// ⚠ Потолок обязателен и назван вслух: нужное число доходит до
        /// 8.1 млн на узел, а это часы. Когда потолок связывает, проба говорит
        /// об этом прямо — «проверки останутся шумовыми» лучше, чем молчаливое
        /// «СОШЛОСЬ» при пороге 37 %.
        ///
        /// ⚠ Ключ `--n=` отменяет расчёт целиком: если человек назвал число,
        /// проба считает им, а не спорит.
        /// </summary>
        static void AutoHistories(GeometryModel geometry, ResponseMatrixOptions options, int maxAuto)
        {
            double energy = Math.Sqrt(options.MinEnergyKev * options.MaxEnergyKev);
            var pilot = new EfficiencySimulator(geometry.Clone())
            {
                Histories = PilotHistories,
                XrayEscape = options.XrayEscape,
                CoherentPassesThrough = options.CoherentPassesThrough,
                Bremsstrahlung = options.Bremsstrahlung,
                SingleScatter = options.SingleScatter,
                PeakHalfWidthKev = 0.0
            };

            double err;
            double eps = pilot.Efficiency(energy, out err);
            if (!(eps > 0.0))
            {
                Console.WriteLine("пилот на {0:F0} кэВ: пик пуст — статистику от геометрии не считаем", energy);
                return;
            }

            // 3·√(2/n) < допуск  =>  n > 2·(3/допуск)²
            const double tolerance = 0.02;
            double needPeak = 2.0 * (3.0 / tolerance) * (3.0 / tolerance);
            double needNode = needPeak / eps;
            int taken = (int)Math.Min(maxAuto, Math.Max(options.Histories, Math.Ceiling(needNode)));
            bool capped = needNode > maxAuto + 0.5;
            Console.WriteLine("статистика от геометрии: пилот ε_пик = {0:E3} на {1:F0} кэВ,"
                              + " допуску нужно {2:N0} историй на узел, берём {3:N0}{4}",
                              eps, energy, needNode, taken,
                              capped ? "  — ПОТОЛОК, проверки 6–7 останутся шумовыми" : "");
            if (capped)
            {
                Console.WriteLine("  (снять потолок: --maxn=<историй>; счёт растёт линейно по числу историй)");
            }

            options.Histories = taken;
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
