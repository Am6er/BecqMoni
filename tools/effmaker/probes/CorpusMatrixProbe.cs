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
//                     [--n=300000] [--nodes=100]
class CorpusMatrixProbe
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string dir = Path.Combine("tools", "CORPUS", "corpus", "geometries");
        string only = null;
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
            files.RemoveAll(f => Path.GetFileNameWithoutExtension(f) != only);
            if (files.Count == 0)
            {
                Console.Error.WriteLine("нет геометрии «" + only + "» в " + dir);
                return 2;
            }
        }

        Console.WriteLine("Матрицы отклика понятной части корпуса (B1)");
        Console.WriteLine("сетка: {0} узлов {1:F0}-{2:F0} кэВ, бин {3:F0} кэВ, {4} историй на узел",
                          options.NodeCount, options.MinEnergyKev, options.MaxEnergyKev,
                          options.BinKev, options.Histories);
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

            bool noisy = matrix.ContinuumWeightedError > 5.0;
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
            double histories = (double)options.NodeCount * options.Histories;
            Console.WriteLine("   время    : {0:F1} с на часах, ядер {1:F1} из {2}{3}",
                              watch.Elapsed.TotalSeconds, share, threads,
                              share < 0.5 * threads ? "  — МАШИНУ ДЕЛИМ" : "");
            Console.WriteLine("   счёт     : {0:F1} с ЦП, {1:F2} мкс на историю  <- сравнивать надо ЭТО",
                              cpuSeconds, histories > 0.0 ? 1.0E6 * cpuSeconds / histories : 0.0);
            Console.WriteLine("   шум конт.: взвешенная {0:F2} %  {1}",
                              matrix.ContinuumWeightedError,
                              noisy ? "ВЫШЕ ПОРОГА 5 %" : "тихо");
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
