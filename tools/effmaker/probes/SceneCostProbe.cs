using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

// `E29`: ЦЕНА РАВНОМЕРНОГО РОЗЫГРЫША ТОЧКИ ВЫЛЕТА — числом, а не оценкой.
//
// Строка реестра говорит: точка вылета бросается равномерно по объёму пробы,
// и на сцене «прибор на земле» (радиус метры) почти все истории уходят из
// дальнего грунта с весом, близким к нулю. Оценка там стояла НЕ ЗАМЕРЕННАЯ
// («полезных порядка 3 %, то есть тридцатикратная цена»). Проба меряет три
// вещи и ни одну не выводит из рассуждения:
//
//   1. ВРЕМЯ. Секунды на историю и достигнутая относительная погрешность на
//      двух сценах ОДНОГО прибора — сосудной (маленькой) и полевой (большой);
//      отсюда сколько историй и сколько секунд стоит 1 % на каждой.
//   2. ОТКУДА СИГНАЛ. Доля сигнала, приходящая из круга радиуса r, против доли
//      ИСТОРИЙ, попадающих в тот же круг (у равномерного розыгрыша она равна
//      (r/R)² и от физики не зависит). Разность этих двух долей и есть
//      выброшенная работа.
//   3. ЧТО ДАЛ БЫ ДРУГОЙ РОЗЫГРЫШ. Действующее число историй
//      ESS = (Σw)²/Σw² при равномерном розыгрыше, при предложенном в строке
//      важностном (∝ exp(−μd)/s²) и при идеальном (∝ самому весу). Отношение
//      ESS — это ровно во столько раз меньше историй нужно на тот же разброс.
//
// Пункты 2 и 3 считаются КВАДРАТУРОЙ по нерассеянному потоку, а не прогоном:
// вклад точки (r, d) в пик равен exp(−μℓ)/s² с точностью до множителя, общего
// для всей сцены, и этот множитель в долях и в ESS сокращается. Квадратура
// проверяется тремя контролями, два из которых обязаны ОТКАЗАТЬ:
//
//   * идеальный розыгрыш p ∝ w обязан дать ESS/n = 1.000000 — это проверка
//     самой формулы ESS, а не физики;
//   * заведомо ПЛОХОЙ розыгрыш p ∝ r (тянет к дальнему краю) обязан дать ESS
//     ХУЖЕ равномерного — проверка, что мерка вообще различает розыгрыши;
//   * форма профиля сверяется с симулятором: ε усечённой сцены (радиус вдвое
//     меньше) к ε полной — квадратура и прогон обязаны сойтись в пределах
//     статистики прогона. Расходятся — квадратуре верить нельзя, и числа
//     пунктов 2–3 из отчёта снимаются.
//
//   scenecostprobe [--geom=<файл .in>] [--energy=662] [--top=3000]
//                  [--n=20000] [--seed=20260910] [--nr=1200] [--nd=300]
//                  [--skip-run] [--frac=0.5]
class SceneCostProbe
{
    // Последний замер `Measure`: нужен пункту 3, чтобы перевести выигрыш ESS в
    // секунды той же сцены, а не оставить его отвлечённым числом.
    static double LastNeeded, LastPerHistoryUs, LastEss, LastEssSmall;

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string geom = Path.Combine("tools", "CORPUS", "corpus", "geometries",
                                   "ASN16_lu_side.in");
        double energy = 662.0, top = 3000.0, frac = 0.5;
        int n = 20000, seed = 20260910, nr = 1200, nd = 300;
        // Историй у сцен РАЗНОЕ число нарочно: на большой действующая выборка
        // (ESS) в тысячи раз меньше числа историй, и одинаковое n дало бы одной
        // из двух сцен бессмысленный разброс.
        int nSmall = 200000, nBig = 4000000;
        bool skipRun = false;
        // Полевая сцена — ГРУНТ по определению: вещество пробы из файла
        // геометрии (у корпусных это оксид лютеция, порошок в баночке) дало бы
        // сцену в семь тонн лютеция, то есть не ту задачу. Ключ `--sample=`
        // подменяет вещество ТОЛЬКО большой сцены; малая идёт как в файле.
        string sample = "Soil";

        foreach (string a in args)
        {
            if (a.StartsWith("--geom=", StringComparison.Ordinal)) geom = a.Substring(7);
            else if (a.StartsWith("--energy=", StringComparison.Ordinal))
                energy = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--top=", StringComparison.Ordinal))
                top = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--frac=", StringComparison.Ordinal))
                frac = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--n=", StringComparison.Ordinal))
            {
                n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                nSmall = nBig = n;
            }
            else if (a.StartsWith("--nsmall=", StringComparison.Ordinal))
                nSmall = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--nbig=", StringComparison.Ordinal))
                nBig = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--seed=", StringComparison.Ordinal))
                seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--nr=", StringComparison.Ordinal))
                nr = int.Parse(a.Substring(5), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--nd=", StringComparison.Ordinal))
                nd = int.Parse(a.Substring(5), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--sample=", StringComparison.Ordinal)) sample = a.Substring(9);
            else if (a == "--skip-run") skipRun = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        // Библиотека веществ читает matdb и файл пользователя.
        GlobalConfigManager.GetInstance();

        if (!File.Exists(geom))
        {
            Console.Error.WriteLine("нет файла геометрии: " + geom);
            return 2;
        }

        GeometryModel small = GeometryModel.Load(geom);
        GeometryModel ground = small.Clone();
        if (sample != null && sample.Length > 0)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(sample);
            if (entry == null)
            {
                Console.Error.WriteLine("в библиотеке нет вещества «" + sample + "»");
                return 2;
            }

            ground.Source = GeometryMaterialLibrary.Make(entry, entry.Density);
        }

        string substituted = GeometryScenes.Ground(ground, top);

        double mfpTop = GeometryScenes.MeanFreePathMm(ground.Source, top);
        double mfpE = GeometryScenes.MeanFreePathMm(ground.Source, energy);
        double h = GeometryScenes.CrystalHeightAboveSampleMm(ground);
        double R = 0.5 * ground.BeakerDiameter;
        double H = ground.SourceHeight;

        Console.WriteLine("геометрия: {0}", Path.GetFileName(geom));
        Console.WriteLine("энергия разбора {0:F1} кэВ, верхняя энергия сцены {1:F0} кэВ",
                          energy, top);
        Console.WriteLine();
        Console.WriteLine("== СЦЕНА МАЛАЯ (как в файле) ==");
        Console.WriteLine("  источник {0}, объём {1:F2} см3, вещество {2} {3:F3} г/см3",
                          small.SourceType, GeometryScenes.SampleVolumeCm3(small),
                          small.Source != null ? small.Source.Name : "?",
                          small.Source != null ? small.Source.Density : 0.0);
        Console.WriteLine();
        Console.WriteLine("== СЦЕНА БОЛЬШАЯ (Детектор на земле, тот же прибор) ==");
        if (substituted.Length > 0) Console.WriteLine("  " + substituted);
        Console.WriteLine("  проба {0} {1:F3} г/см3; пробег {2:F1} мм на {3:F0} кэВ,"
                          + " {4:F1} мм на {5:F0} кэВ",
                          ground.Source.Name, ground.Source.Density, mfpTop, top, mfpE, energy);
        Console.WriteLine("  радиус {0:F0} мм ({1:F2} пробега), глубина {2:F0} мм,"
                          + " середина кристалла над грунтом {3:F1} мм",
                          R, R / mfpTop, H, h);
        double volCm3 = GeometryScenes.SampleVolumeCm3(ground);
        Console.WriteLine("  объём {0:F0} л, масса {1:F0} кг",
                          volCm3 / 1000.0, volCm3 * ground.Source.Density / 1000.0);
        Console.WriteLine();

        double neededBig = 0.0, perHistoryBig = 0.0, essBigRun = 0.0;

        // ------------------------------------------------------------------
        // 1. ВРЕМЯ: тот же прибор, две сцены, один поток, одно зерно.
        // ------------------------------------------------------------------
        if (!skipRun)
        {
            Console.WriteLine("== 1. ЦЕНА ИСТОРИИ (один поток; малая {0}, большая {1} историй) ==",
                              nSmall, nBig);
            Console.WriteLine("  {0,-22} {1,12} {2,10} {3,9} {4,13} {5,10} {6,10}",
                              "сцена", "eps", "разброс,%", "мкс/ист", "историй на 1%", "секунд", "ESS/n");
            double effSmall = Measure("малая (сосуд)", small, energy, nSmall, seed);
            LastEssSmall = LastEss;
            double effGround = Measure("большая (земля)", ground, energy, nBig, seed);
            neededBig = LastNeeded;
            perHistoryBig = LastPerHistoryUs;
            essBigRun = LastEss;
            Console.WriteLine();
            Console.WriteLine("  ε большой к малой: {0:E4}",
                              effSmall > 0.0 ? effGround / effSmall : 0.0);
            Console.WriteLine();
        }

        // ------------------------------------------------------------------
        // 2-3. КВАДРАТУРА по нерассеянному потоку.
        // ------------------------------------------------------------------
        double mu = 1.0 / mfpE;                    // 1/мм
        double[] rMid = new double[nr], dMid = new double[nd];
        double dr = R / nr, dd = H / nd;
        for (int i = 0; i < nr; i++) rMid[i] = (i + 0.5) * dr;
        for (int j = 0; j < nd; j++) dMid[j] = (j + 0.5) * dd;

        // Накопители: dV ∝ r dr dd (множитель 2π общий и сокращается).
        double V = 0.0, Iw = 0.0, Iw2 = 0.0;
        double Iq = 0.0, Iw2q = 0.0;               // предложенный розыгрыш
        double Ib = 0.0, Iw2b = 0.0;               // заведомо плохой: p ∝ r
        double Ii = 0.0, Iw2i = 0.0;               // идеальный: p ∝ w (контроль)
        double[] wByR = new double[nr];

        for (int i = 0; i < nr; i++)
        {
            double r = rMid[i];
            double cellR = r * dr;
            double acc = 0.0;
            for (int j = 0; j < nd; j++)
            {
                double d = dMid[j];
                double s2 = r * r + (h + d) * (h + d);
                double s = Math.Sqrt(s2);
                double path = d * s / (h + d);     // путь в грунте до поверхности
                double w = Math.Exp(-mu * path) / s2;

                // Предложенный розыгрыш: экспонента по ГЛУБИНЕ (вертикальный
                // путь вместо наклонного — он аналитически обратим) на
                // геометрический спад 1/s². Ненормированная плотность.
                double q = Math.Exp(-mu * d) / s2;

                double cell = cellR * dd;
                V += cell;
                Iw += w * cell;
                Iw2 += w * w * cell;
                Iq += q * cell;
                Iw2q += w * w / q * cell;
                double b = r;                       // тянет к дальнему краю
                Ib += b * cell;
                Iw2b += w * w / b * cell;
                Ii += w * cell;
                Iw2i += w * w / w * cell;
                acc += w * cell;
            }

            wByR[i] = acc;
        }

        double essUniform = Iw * Iw / (V * Iw2);            // = mean²/E[w²]
        double essProposed = Iw * Iw / (Iq * Iw2q);
        double essBad = Iw * Iw / (Ib * Iw2b);
        double essIdeal = Iw * Iw / (Ii * Iw2i);            // самопроверка формулы

        Console.WriteLine("== 2. ОТКУДА СИГНАЛ (квадратура {0}x{1}, нерассеянный поток) ==", nr, nd);
        Console.WriteLine("  {0,8} {1,14} {2,14} {3,12}",
                          "r/R", "сигнала, %", "историй, %", "на историю");
        double run = 0.0;
        int shown = 0;
        double r98 = -1.0;
        double[] marks = { 0.05, 0.1, 0.2, 1.0 / 3.0, 0.5, 2.0 / 3.0, 0.8, 1.0 };
        for (int i = 0; i < nr; i++)
        {
            run += wByR[i];
            if (r98 < 0.0 && run / Iw >= 0.98) r98 = rMid[i] / R;
            while (shown < marks.Length && rMid[i] / R >= marks[shown])
            {
                double frR = marks[shown];
                double sig = 100.0 * run / Iw;
                double hist = 100.0 * frR * frR;
                Console.WriteLine("  {0,8:F3} {1,14:F2} {2,14:F2} {3,12:F3}",
                                  frR, sig, hist, hist > 0.0 ? sig / hist : 0.0);
                shown++;
            }
        }

        while (shown < marks.Length)
        {
            double frR = marks[shown++];
            Console.WriteLine("  {0,8:F3} {1,14:F2} {2,14:F2} {3,12:F3}",
                              frR, 100.0, 100.0 * frR * frR, 1.0 / (frR * frR));
        }

        Console.WriteLine();
        Console.WriteLine("  98 % сигнала набирается к r/R = {0:F3}; историй туда попадает"
                          + " {1:F2} %, остальные {2:F2} % несут 2 % сигнала",
                          r98, 100.0 * r98 * r98, 100.0 * (1.0 - r98 * r98));
        Console.WriteLine();

        Console.WriteLine("== 3. ЧТО ДАЁТ ДРУГОЙ РОЗЫГРЫШ (ESS = действующее число историй) ==");
        Console.WriteLine("  равномерный по объёму (как сейчас): ESS/n = {0:F5}"
                          + "  → на тот же разброс историй в {1:F1} раза больше",
                          essUniform, 1.0 / essUniform);
        Console.WriteLine("  предложенный exp(−μd)/s²          : ESS/n = {0:F5}"
                          + "  → выигрыш к нынешнему {1:F1}x", essProposed, essProposed / essUniform);
        Console.WriteLine("  КОНТРОЛЬ идеальный p ∝ w          : ESS/n = {0:F6}"
                          + " (обязан быть 1.000000)", essIdeal);
        Console.WriteLine("  КОНТРОЛЬ заведомо плохой p ∝ r    : ESS/n = {0:E3}"
                          + " (обязан быть ХУЖЕ равномерного в {1:F0} раз: {2})",
                          essBad, essUniform / essBad,
                          essBad < essUniform ? "ХУЖЕ, сошлось" : "⛔ НЕ ХУЖЕ — мерка слепа");
        Console.WriteLine();

        if (neededBig > 0.0)
        {
            // ⚠ ОЦЕНКА, А НЕ ЗАМЕР. Квадратура снимает ГЕОМЕТРИЧЕСКУЮ часть
            // разброса; остаточная — отклик самого кристалла (попал квант в пик
            // или нет) — важностным розыгрышем не снимается. Её величина видна
            // на малой сцене: там ESS/n ≈ ε.
            double gain = essProposed / essUniform;
            Console.WriteLine("  ⚠ ОЦЕНКА (не замер): на большой сцене 1 % стоит {0:F0} историй"
                              + " = {1:F0} с; сняв геометрическую часть разброса ({2:F0}x),"
                              + " это {3:F0} историй = {4:F1} с",
                              neededBig, neededBig * perHistoryBig / 1e6,
                              gain, neededBig / gain, neededBig / gain * perHistoryBig / 1e6);
            Console.WriteLine("     действующая выборка прогона ESS/n = {0:E2};"
                              + " после снятия геометрии ожидается {1:E2}"
                              + " (у малой сцены того же прибора {2:E2})",
                              essBigRun, essBigRun * gain, LastEssSmall);
            Console.WriteLine();
        }

        int rc = 0;
        if (Math.Abs(essIdeal - 1.0) > 1e-9)
        {
            Console.WriteLine("⛔ самопроверка ESS не прошла");
            rc = 1;
        }

        if (!(essBad < essUniform))
        {
            Console.WriteLine("⛔ мерка не отличает плохой розыгрыш от равномерного");
            rc = 1;
        }

        // ------------------------------------------------------------------
        // Контроль формы: усечённая сцена прогоном против квадратуры.
        // ------------------------------------------------------------------
        if (!skipRun)
        {
            Console.WriteLine("== КОНТРОЛЬ ФОРМЫ: сцена радиусом {0:F0} % против полной ==",
                              100.0 * frac);
            GeometryModel cut = ground.Clone();
            cut.BeakerDiameter = ground.BeakerDiameter * frac;
            cut.BeakerHeight = ground.SourceHeight;
            double errCut, errFull;
            double eCut = Efficiency(cut, energy, nBig, seed, out errCut);
            double eFull = Efficiency(ground, energy, nBig, seed, out errFull);

            // Квадратура: ε усечённой к ε полной = (доля сигнала внутри frac)
            // делённая на (долю объёма внутри frac).
            double sigIn = 0.0;
            for (int i = 0; i < nr; i++)
            {
                if (rMid[i] / R <= frac) sigIn += wByR[i];
            }

            double predicted = (sigIn / Iw) / (frac * frac);
            double measured = eFull > 0.0 ? eCut / eFull : 0.0;
            double tol = 3.0 * Math.Sqrt(errCut * errCut + errFull * errFull) / 100.0 * measured;
            Console.WriteLine("  прогон: ε(усечённой) {0:E4} ±{1:F2} %, ε(полной) {2:E4} ±{3:F2} %,"
                              + " отношение {4:F4}", eCut, errCut, eFull, errFull, measured);
            Console.WriteLine("  квадратура предсказывает {0:F4}; расхождение {1:F4}"
                              + " при допуске 3σ = {2:F4} — {3}",
                              predicted, Math.Abs(predicted - measured), tol,
                              Math.Abs(predicted - measured) <= tol
                                  ? "СОШЛОСЬ" : "⛔ РАЗОШЛОСЬ, числам квадратуры не верить");
            if (Math.Abs(predicted - measured) > tol) rc = 1;

            // Контроль, у которого допуск шире самой величины, проходит ВСЕГДА
            // и не меряет ничего: об этом надо сказать вслух, а не радоваться
            // слову «СОШЛОСЬ».
            if (tol > 0.5 * predicted)
            {
                Console.WriteLine("  ⛔ У КОНТРОЛЯ НЕТ ЗУБОВ: допуск {0:F3} шире половины"
                                  + " предсказания {1:F3} — историй мало, слово «СОШЛОСЬ»"
                                  + " ничего не значит", tol, predicted);
                rc = 1;
            }
        }

        return rc;
    }

    static double Measure(string title, GeometryModel g, double energy, int n, int seed)
    {
        double err;
        Stopwatch sw = Stopwatch.StartNew();
        double eff = Efficiency(g, energy, n, seed, out err);
        sw.Stop();
        double perHistory = sw.Elapsed.TotalSeconds / n * 1e6;
        double needed = err > 0.0 ? n * err * err : 0.0;      // историй на 1 %
        // ESS/n = 1/(1 + n·(δ/100)²): разброс δ и число историй задают
        // действующую выборку однозначно, отдельного счёта не нужно.
        double ess = 1.0 / (1.0 + n * (err / 100.0) * (err / 100.0));
        LastNeeded = needed;
        LastPerHistoryUs = perHistory;
        LastEss = ess;
        Console.WriteLine("  {0,-22} {1,12:E4} {2,10:F3} {3,9:F1} {4,13:F0} {5,10:F1} {6,10:E2}",
                          title, eff, err, perHistory, needed, needed * perHistory / 1e6, ess);
        return eff;
    }

    static double Efficiency(GeometryModel g, double energy, int n, int seed, out double err)
    {
        EfficiencySimulator sim = new EfficiencySimulator(g)
        {
            Histories = n,
            Seed = seed,
        };
        sim.PeakHalfWidthKev = g.PeakHalfWidthKev(energy);
        return sim.Efficiency(energy, out err);
    }
}
