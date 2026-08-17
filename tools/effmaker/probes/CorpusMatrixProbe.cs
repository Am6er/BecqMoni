using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

// B1, шаг 2: матрица отклика НА КАЖДУЮ геометрию понятной части корпуса.
//
// Матрица считается из геометрии и своя на каждую: измерено, что один кристалл
// в трёх расположениях источника даёт долю пика 0.848 / 0.822 / 0.815 и до
// четверти разброса в континууме. Поэтому девять геометрий, построенных
// `CorpusGeomProbe`, — это девять матриц, а не одна.
//
// Настройки — УМОЛЧАНИЯ `ResponseMatrixOptions` (30–3000 кэВ, 100 узлов, бин
// 2 кэВ, 300 тыс. историй, вся физика включена). Здесь они не переписываются
// нарочно: матрица корпуса обязана быть такой же, какую получит человек,
// нажавший «посчитать» в приложении.
//
// Печатается по каждой: клеймо (версия физики, историй, сетка), время, доля
// пика на 662 кэВ и ВЗВЕШЕННАЯ ошибка континуума (T15) — та, по которой форма
// предупреждает о шуме. Порог там 5 %.
//
//   corpusmatrixprobe [--dir=tools\CORPUS\corpus\geometries] [--only=<ключ>]
//                     [--n=300000] [--nodes=100] [--threads=N] [--force]
class CorpusMatrixProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string dir = Path.Combine("tools", "CORPUS", "corpus", "geometries");
        string only = null;
        string dump = null;
        bool force = false;
        var options = new ResponseMatrixOptions();
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
            else if (a.StartsWith("--only=", StringComparison.Ordinal)) only = a.Substring(7);
            else if (a.StartsWith("--n=", StringComparison.Ordinal))
                options.Histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--nodes=", StringComparison.Ordinal))
                options.NodeCount = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--threads=", StringComparison.Ordinal))
                // T35, дешёвый выигрыш №4: параллелить ПО СЦЕНАМ, а не внутри
                // сцены. Ключ нужен, чтобы запустить несколько процессов по
                // нескольку потоков и замерить, что выходит быстрее.
                options.Threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--target=", StringComparison.Ordinal))
                // T35, дешёвый выигрыш №3: считать узел ДО ЗАДАННОГО ШУМА, а не
                // плоским числом историй. Ноль — прежний плоский счёт, для A/B.
                options.ContinuumErrorTarget = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--edges=", StringComparison.Ordinal))
                // `E31`: разрешать K-края веществ сцены в сетке. Включено
                // умолчанием; ключ нужен, чтобы выделить вклад краёв отдельно от
                // остального — иначе пересчёт меняет две вещи разом.
                options.ResolveEdges = a.Substring(8) != "0";
            else if (a.StartsWith("--emin=", StringComparison.Ordinal))
                // Диапазон сетки — чтобы профилировать ОДИН узел, как требует
                // раздел Profiling в CLAUDE.md: профиль всей сцены смешивает
                // низкие узлы (одно взаимодействие) с высокими (пары, вторички).
                options.MinEnergyKev = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--emax=", StringComparison.Ordinal))
                options.MaxEnergyKev = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed=", StringComparison.Ordinal))
                // `T43`: независимая выборка тем же кодом. Нужна для приёмки
                // правок, меняющих ЧИСЛО розыгрышей: сравнивать «было/стало»
                // можно только с шумом ГСЧ, а его измеряет второе зерно.
                options.Seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--dump=", StringComparison.Ordinal))
                // `T43`: поузловая раскладка в CSV. Сводных чисел мало — надо
                // видеть, ГДЕ потрачены истории и какой узел не дотянул.
                dump = a.Substring(7);
            else if (a.StartsWith("--acont=", StringComparison.Ordinal))
                // АБЛЯЦИЯ, не режим счёта: выключенный ключ возвращает прежний
                // взвешенный континуум с его измеренным недобором. Нужен, чтобы
                // узнать, во что обходится аналоговая ветка — она гонит СВОИ n
                // историй поверх взвешенных, и без замера доля её работы
                // неизвестна. Матрицу, посчитанную так, в дело не пускать.
                options.AnalogContinuum = a.Substring(8) != "0";
            else if (a.StartsWith("--scat=", StringComparison.Ordinal))
                // АБЛЯЦИЯ, как и `--acont=`: выключает однократное рассеяние по
                // дороге к кристаллу (и вместе с ним проводку промахнувшихся
                // лучей до выхода из сцены). Даёт долю времени, которую эта
                // поправка стоит; вклад её в полную эффективность ~15 %.
                options.SingleScatter = a.Substring(7) != "0";
            else if (a.StartsWith("--bound=", StringComparison.Ordinal))
                // АБЛЯЦИЯ: рассеяние на СВЯЗАННОМ электроне (физика 7) — угол со
                // множителем отбора, доплеровское размытие, когерентное своим
                // каналом. Всё это отбором с перебросом, то есть недёшево;
                // ключ показывает, сколько именно оно стоит.
                options.BoundScattering = a.Substring(8) != "0";
            else if (a.StartsWith("--roulette=", StringComparison.Ordinal))
                // `T43`, решение Amber: рулетка по весу поправки на однократное
                // рассеяние. Ноль — прежний счёт. ⚠ Судить её временем прогона
                // НЕЛЬЗЯ: она размен времени на шум, и мерилом служит время до
                // цели по шуму (гнать с `--target=`).
                options.ScatterRoulette = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
            else if (a == "--recollect")
                // `T43`, ЗАМЕР: разбирать луч заново на каждом шаге. Считается
                // то же самое, но разборов становится столько же, сколько шагов;
                // по разности времени и разности их числа видно, чего стоит один
                // разбор. Нужно потому, что профиль требует прав, а их может не
                // быть. В счёте не применять.
                EfficiencySimulator.MeasureCollectCost = true;
            else if (a == "--force") force = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (!Directory.Exists(dir))
        {
            Console.Error.WriteLine("нет каталога геометрий: " + dir);
            return 2;
        }

        GlobalConfigManager.GetInstance();

        List<string> files = new List<string>(Directory.GetFiles(dir, "*.in"));
        files.Sort(StringComparer.Ordinal);
        if (only != null)
        {
            // Список через запятую — как у `CorpusFsaProbe`. Нужен, чтобы
            // раздать сцены нескольким процессам (T35, дешёвый выигрыш №4):
            // по одному ключу за запуск это столько же запусков, сколько сцен.
            List<string> wanted = new List<string>(only.Split(','));
            files.RemoveAll(f => !wanted.Contains(Path.GetFileNameWithoutExtension(f)));
            if (files.Count == 0)
            {
                Console.Error.WriteLine("нет геометрий «" + only + "» в " + dir);
                return 2;
            }
        }

        Console.WriteLine("Матрицы отклика понятной части корпуса (B1)");
        Console.WriteLine("сетка: {0} узлов {1:F0}-{2:F0} кэВ, бин {3:F0} кэВ, {4} историй на узел",
                          options.NodeCount, options.MinEnergyKev, options.MaxEnergyKev,
                          options.BinKev, options.Histories);
        if (options.ContinuumErrorTarget > 0.0)
        {
            Console.WriteLine("останов по шуму: цель {0:F1} % на узел, проба 1/{1}, потолок x{2} (T35)",
                              options.ContinuumErrorTarget, options.PilotDivisor,
                              options.MaxHistoriesFactor);
            Console.WriteLine("  «историй на узел» выше — НОМИНАЛ, от которого считаются проба и потолок;");
            Console.WriteLine("  сколько потрачено на самом деле, печатается у каждой сцены");
        }
        else
        {
            Console.WriteLine("останов по шуму ВЫКЛЮЧЕН — плоский счёт (T35)");
        }

        Console.WriteLine();

        bool quiet = true;
        int skipped = 0, built = 0;
        var total = Stopwatch.StartNew();
        foreach (string path in files)
        {
            string key = Path.GetFileNameWithoutExtension(path);
            GeometryModel geometry = GeometryModel.Load(path);

            // ГВАРД ГЛОБАЛЬНОГО ПЕРЕСЧЁТА (указание Amber 16.08.2026).
            //
            // Считать надо ТОЛЬКО то, что изменилось. Глобальный пересчёт
            // осмыслен, когда изменилась картина целиком — например поднялась
            // версия физики переноса; тогда клеймо не сойдётся СРАЗУ У ВСЕХ, и
            // пропусков не будет ни одного. Отдельный ключ на это не нужен, и
            // в этом суть: признак «пора считать всё» вычисляется, а не
            // объявляется руками.
            //
            // Клеймо (`ResponseMatrix.ComputeStamp`) покрывает версию физики,
            // ВСЕ параметры расчёта и полный текст геометрии, поэтому «сошлось»
            // значит «эта матрица посчитана ровно из этого и ровно так».
            //
            // ⚠ Зачем это заведено. Прогон без `--only` пересчитывал ВСЁ
            // подряд: 16.08.2026 так ушло 35 минут на кривые, которые не
            // менялись (и `T36` — тем же способом гущая матрица молча вернулась
            // к штатной густоте). Ручной `--only` для этого не годится: он
            // требует, чтобы человек ЗАРАНЕЕ знал список изменившегося, а
            // ошибка в списке молчит.
            string outPathExisting = Path.Combine(dir, key + ".rmx");
            if (!force && File.Exists(outPathExisting))
            {
                ResponseMatrix have = ResponseMatrix.Load(outPathExisting);
                if (have != null && have.IsValidFor(geometry, options))
                {
                    Console.WriteLine("== {0} ==", key);
                    Console.WriteLine("   пропущена: клеймо сошлось, пересчитывать нечего");
                    Console.WriteLine();
                    skipped++;
                    continue;
                }

                // ⛔ ГУЩЕ ШТАТНОЙ — НЕ ТРОГАТЬ. Это `T36` дословно: «проба
                // должна отказываться понижать густоту без ключа».
                //
                // Клеймо не сходится и тогда, когда матрица посчитана ЛУЧШЕ
                // требуемого — историй в ней больше, чем в умолчаниях. Считать
                // такую «устаревшей» и молча переписывать штатной — это ровно
                // та потеря, ради которой строка `T36` и заведена: 16.08.2026
                // густая `G1S_point25` (1.2 М историй, шум 2.84 %) была так
                // затёрта штатной (300 к, 5.67 %) ДВАЖДЫ — второй раз этим
                // самым гвардом, пока в нём не было этой ветки.
                //
                // Проверяется годность по ЕЁ СОБСТВЕННЫМ параметрам: геометрия
                // и версия физики те же, разошлись только историй. Тогда она не
                // устарела, а лучше, и пересчёт был бы понижением.
                if (have != null && have.IsValidFor(geometry) && have.Histories > options.Histories)
                {
                    Console.WriteLine("== {0} ==", key);
                    Console.WriteLine("   пропущена: посчитана ГУЩЕ штатной ({0} историй против {1}),"
                                      + " понижать без --force не буду (T36)",
                                      have.Histories, options.Histories);
                    Console.WriteLine();
                    skipped++;
                    continue;
                }
            }

            ResponseMatrixBuilder.ResetWalkCounters();
            TimeSpan cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
            var watch = Stopwatch.StartNew();
            ResponseMatrix matrix = ResponseMatrixBuilder.Build(
                geometry, options, null, CancellationToken.None);
            watch.Stop();
            double cpuSeconds = (Process.GetCurrentProcess().TotalProcessorTime - cpuBefore)
                                .TotalSeconds;

            string outPath = Path.Combine(dir, key + ".rmx");
            matrix.Save(outPath);
            built++;

            // Порог приёмки — та цель, до которой считали, а не назначенные
            // когда-то 5 %: с 17.08.2026 цель по измерению 3 % (`T35`), и
            // сравнивать достигнутое надо с ней. При выключенном останове
            // остаётся прежний порог — иначе плоские прогоны для A/B стали бы
            // «шумными» задним числом.
            double noiseLimit = options.ContinuumErrorTarget > 0.0
                ? options.ContinuumErrorTarget : 5.0;
            bool noisy = matrix.ContinuumWeightedError > noiseLimit;
            quiet &= !noisy;
            Console.WriteLine("== {0} ==", key);
            Console.WriteLine("   клеймо   : {0}", matrix.Stamp);
            // Время НА ЧАСАХ про эту машину, а не про этот счёт, и путать их
            // дорого: T28 трое суток числилась «матрица подорожала вдвое»
            // (34.7 → 67.3 мин на девяти геометриях, результат тот же). Замер
            // 13.08.2026 на ОДНОЙ геометрии: 106 с, 184 с и — когда рядом
            // считалась вторая такая же — 351 с, при неизменном шуме 1.52 %.
            // Часами тут мерить нечего.
            //
            // Поэтому рядом печатается ЦП-время на историю: оно про код и ни
            // про что больше. Подорожал счёт — вырастет оно; забрал ядра
            // сосед — вырастут только часы, а доля ядер покажет, кто виноват.
            int threads = options.Threads > 0
                ? options.Threads
                : Math.Max(1, Environment.ProcessorCount - 1);
            double share = watch.Elapsed.TotalSeconds > 0.0
                ? cpuSeconds / watch.Elapsed.TotalSeconds : 0.0;
            // ⚠ Историй берётся ПОТРАЧЕННОЕ, а не «узлы × номинал»: при останове
            // по шуму (`T35`) у каждого узла своё число, и произведение врёт в
            // разы — а на нём стоит единственная величина, которой меряют код.
            double histories = matrix.HistoriesSpent > 0
                ? matrix.HistoriesSpent
                : (double)options.NodeCount * options.Histories;
            double flat = (double)matrix.Energies.Length * options.Histories;
            Console.WriteLine("   время    : {0:F1} с на часах, ядер {1:F1} из {2}{3}",
                              watch.Elapsed.TotalSeconds, share, threads,
                              share < 0.5 * threads ? "  — МАШИНУ ДЕЛИМ" : "");
            Console.WriteLine("   счёт     : {0:F1} с ЦП, {1:F2} мкс на историю  <- сравнивать надо ЭТО",
                              cpuSeconds, histories > 0.0 ? 1.0E6 * cpuSeconds / histories : 0.0);
            if (options.ContinuumErrorTarget > 0.0)
            {
                Console.WriteLine("   историй  : {0:N0} против {1:N0} плоских — в {2:F1} раза дешевле; "
                                  + "самый дорогой узел {3:N0}",
                                  (double)matrix.HistoriesSpent, flat,
                                  matrix.HistoriesSpent > 0 ? flat / matrix.HistoriesSpent : 0.0,
                                  (double)matrix.HistoriesWorstNode);
            }

            // `T43`: цена истории почти не зависит от энергии — значит время
            // съедает обход сцены, а не транспорт. Эти три числа говорят, сколько
            // его: сколько раз на историю спрошены область, граница и ослабление.
            if (ResponseMatrixBuilder.WalkHistories > 0)
            {
                double perHistory = (double)ResponseMatrixBuilder.WalkHistories;
                Console.WriteLine("   обход    : на историю {0:F1} шага границ, {1:F1} поиска области, "
                                  + "{2:F1} интерполяции μ, {3:F2} сбора пересечений",
                                  ResponseMatrixBuilder.WalkStep / perHistory,
                                  ResponseMatrixBuilder.WalkAt / perHistory,
                                  ResponseMatrixBuilder.WalkMu / perHistory,
                                  ResponseMatrixBuilder.WalkCollect / perHistory);
            }

            Console.WriteLine("   шум конт.: взвешенная {0:F2} %  {1}",
                              matrix.ContinuumWeightedError,
                              noisy ? string.Format(CultureInfo.InvariantCulture,
                                                    "ВЫШЕ ПОРОГА {0:F1} %", noiseLimit)
                                    : "тихо");
            if (dump != null && matrix.NodeHistories != null)
            {
                string dumpPath = files.Count > 1
                    ? Path.Combine(Path.GetDirectoryName(dump) ?? ".",
                                   Path.GetFileNameWithoutExtension(dump) + "-" + key + ".csv")
                    : dump;
                using (var w = new StreamWriter(dumpPath, false, new UTF8Encoding(true)))
                {
                    // `seconds_wall` — время прохода узла по часам, не ЦП: при 15
                    // потоках на 8 ядрах завышено, но узлы между собой сравнимы.
                    w.WriteLine("node,energy_kev,histories,error_pct,seconds_wall");
                    for (int i = 0; i < matrix.Energies.Length; i++)
                    {
                        w.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1:F3},{2},{3:F3},{4:F3}", i, matrix.Energies[i],
                            matrix.NodeHistories[i],
                            matrix.NodeErrors != null ? matrix.NodeErrors[i] : 0.0,
                            matrix.NodeSeconds != null ? matrix.NodeSeconds[i] : 0.0));
                    }
                }

                Console.WriteLine("   раскладка: {0}", dumpPath);
            }

            Console.WriteLine("   файл     : {0} ({1:F1} МБ)",
                              outPath, new FileInfo(outPath).Length / 1048576.0);
            Console.WriteLine();
        }

        total.Stop();
        // Пропущенное называется ЧИСЛОМ, а не молчанием: «посчитано 0 из 45» —
        // это нормальный исход, когда ничего не менялось, и он должен читаться
        // как нормальный, а не как «проба не сработала».
        Console.WriteLine("матриц: {0} — посчитано {1}, пропущено {2} (клеймо сошлось); всего {3:F1} мин",
                          files.Count, built, skipped, total.Elapsed.TotalMinutes);
        if (built == 0 && skipped > 0)
        {
            Console.WriteLine("ничего не изменилось — пересчитывать было нечего");
        }
        else if (skipped == 0 && built > 1)
        {
            Console.WriteLine("пересчитаны ВСЕ — значит изменилась картина целиком"
                              + " (версия физики или параметры расчёта)");
        }

        Console.WriteLine(quiet ? "ВСЕ СОШЛИСЬ" : "ЕСТЬ ШУМНЫЕ");
        return quiet ? 0 : 1;
    }
}
