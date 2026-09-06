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
// Настройки — УМОЛЧАНИЯ `ResponseMatrixOptions` (5–3000 кэВ, 140 узлов, бин
// 2 кэВ, 3 млн историй, останов по шуму 3 %, вся физика включена). Здесь они
// не переписываются нарочно: матрица корпуса обязана быть такой же, какую
// получит человек, нажавший «посчитать» в приложении.
//
// ⚠ ЧИСЛА ВЫШЕ — СПРАВКА, А НЕ ИСТОЧНИК: проба не задаёт ни одного из них, она
// берёт `new ResponseMatrixOptions()`. Спрашивать умолчание надо ТАМ
// (`ResponseMatrix.cs`, поля `MinEnergyKev`/`NodeCount`/`Histories`), а не
// здесь. Цена привычки читать комментарий как код измерена 05.09.2026 (`S132`):
// решение Amber «пересчитать все 44 матрицы под 3 млн историй» было принято по
// строке этого комментария, где стояло «300 тыс.», — а код к тому времени три
// дня как считал 3 млн, и склад уже был пересчитан 03.09.2026.
//
// Печатается по каждой: клеймо (версия физики, историй, сетка), время, доля
// пика на 662 кэВ и ВЗВЕШЕННАЯ ошибка континуума (T15) — та, по которой форма
// предупреждает о шуме. Порог там 5 %.
//
//   corpusmatrixprobe [--dir=tools\CORPUS\corpus\geometries] [--only=<ключ>]
//                     [--n=3000000] [--nodes=140] [--threads=N] [--force]
//                     [--pairth=1] [--positron=1] [--posoffset=0] [--rayl2=1]
//                     [--cone=1] [--peakw=1]
//
// `--peakw=1` (`E34`) — допуск пика сборщика из геометрии вместо нуля; тоже
// выключен умолчанием и тоже входит в клеймо.
//
// Четыре последних — рычаги физики 02.09.2026 (`S130`); умолчанием все
// выключены, включённый входит в клеймо и честно гонит матрицу в пересчёт.
class CorpusMatrixProbe
{
    /// <summary>
    /// Строгий разбор булева ключа (`A77`). Принимает РОВНО `0` и `1`; на всё
    /// остальное бросает, и прогон кончается в первую секунду.
    ///
    /// ⛔ Так сделано не из аккуратности, а по цене. Прежний разбор был
    /// `a.Substring(n) != "0"` — то есть ЛЮБОЕ неизвестное значение он толковал
    /// как истину и молчал. 02.09.2026 к `--cone=` добавили третье значение
    /// `far`, а прогон пошёл каталогом проб, собранным ДО правки: старый разбор
    /// сравнил «far» с «0», получил «не ноль» и включил конус ВСЕМ 44 сценам.
    /// Ни отказа, ни предупреждения — ключ синтаксически прежний, значение
    /// просто «истинное». ⚠ Ни побитовый замер, ни клеймо этого не ловят:
    /// содержимое матриц вышло верным (на ближних сценах конус тождественен),
    /// испорчено ПРОИСХОЖДЕНИЕ — они пометились `cone=on`, и следующий прочёл
    /// бы их как «посчитаны с наведением». Цена молчания — три часа счёта.
    ///
    /// Именно поэтому отказ, а не предупреждение: предупреждение в начале
    /// трёхчасового прогона никто не читает.
    /// </summary>
    static bool Flag(string arg, int prefix, string alsoNamed = null)
    {
        string key = arg.Substring(0, prefix);
        string value = arg.Substring(prefix);
        if (value == "0") return false;
        if (value == "1") return true;
        throw new ArgumentException(
            "ключ " + key + " понимает только "
            + (alsoNamed == null ? "0 и 1" : "0, 1 и " + alsoNamed)
            + ", а получил «" + value + "». Разбор строгий с 03.09.2026 (`A77`): "
            + "прежний считал ЛЮБОЕ неизвестное значение истиной и молчал.");
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string dir = Path.Combine("tools", "CORPUS", "corpus", "geometries");
        string only = null;
        string dump = null;
        bool force = false;
        var options = new ResponseMatrixOptions();
        bool coneFar = false;                    // `A57`: конус только дальним
        try
        {
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
                options.ResolveEdges = Flag(a, 8);
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
                options.AnalogContinuum = Flag(a, 8);
            else if (a.StartsWith("--scat=", StringComparison.Ordinal))
                // АБЛЯЦИЯ, как и `--acont=`: выключает однократное рассеяние по
                // дороге к кристаллу (и вместе с ним проводку промахнувшихся
                // лучей до выхода из сцены). Даёт долю времени, которую эта
                // поправка стоит; вклад её в полную эффективность ~15 %.
                options.SingleScatter = Flag(a, 7);
            else if (a.StartsWith("--bound=", StringComparison.Ordinal))
                // АБЛЯЦИЯ: рассеяние на СВЯЗАННОМ электроне (физика 7) — угол со
                // множителем отбора, доплеровское размытие, когерентное своим
                // каналом. Всё это отбором с перебросом, то есть недёшево;
                // ключ показывает, сколько именно оно стоит.
                options.BoundScattering = Flag(a, 8);
            else if (a.StartsWith("--roulette=", StringComparison.Ordinal))
                // `T43`, решение Amber: рулетка по весу поправки на однократное
                // рассеяние. Ноль — прежний счёт. ⚠ Судить её временем прогона
                // НЕЛЬЗЯ: она размен времени на шум, и мерилом служит время до
                // цели по шуму (гнать с `--target=`).
                options.ScatterRoulette = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--npl=", StringComparison.Ordinal))
                // АБЛЯЦИЯ (`F11`): отклик в шкале СВЕТА — каждый электронный
                // вклад взвешивается кривой L(E), бины пересчитываются с якорем
                // по пику. Выключенный ключ возвращает шкалу энергии. Нужен,
                // чтобы понять, отчего линия флуоресценции в строке матрицы
                // стоит не на своей энергии (`F27`).
                options.LightNonproportionality = Flag(a, 6);
            else if (a.StartsWith("--pairth=", StringComparison.Ordinal))
                // `S121`/`S130`: пороговая интерполяция сечения рождения пар
                // (XCOM). Умолчанием ВЫКЛЮЧЕНА решением Amber; ключ — рычаг
                // замера `S125`. Включённая меняет клеймо, поэтому пересчёт
                // идёт честно, а прежние матрицы остаются годными.
                options.XcomPairThreshold = Flag(a, 9);
            else if (a.StartsWith("--positron=", StringComparison.Ordinal))
                // `S120`/`S130`: раздельный перенос e− и e+ пары. Рычаг замера
                // `S126`, ПЕРВАЯ его половина.
                options.PositronTransport = Flag(a, 11);
            else if (a.StartsWith("--posoffset=", StringComparison.Ordinal))
                // ВТОРАЯ половина `S126`: смещать ли точку аннигиляции на конец
                // пробега позитрона. Мерить порознь — ошибки разные и могут
                // погасить друг друга. Действует только с `--positron=1`.
                options.PositronOffset = Flag(a, 12);
            else if (a.StartsWith("--klcasc=", StringComparison.Ordinal))
                // `A101`: атомный каскад K→L — одно поглощение отдаёт ДВА
                // кванта. С 04.09.2026 УМОЛЧАНИЕ (физика 16, решение Amber);
                // ключ остался АБЛЯЦИЕЙ — `--klcasc=0` возвращает прежний счёт
                // и пишется в клеймо, то есть такая матрица честно другая.
                options.KLCascade = Flag(a, 9);
            else if (a.StartsWith("--rayl2=", StringComparison.Ordinal))
                // `N13`/`S130`: когерентное своим каналом во ВЗВЕШЕННОЙ ветви
                // (проводка к кристаллу). Рычаг замера `S127`.
                options.RayleighToCrystal = Flag(a, 8);
            else if (a.StartsWith("--cone=", StringComparison.Ordinal))
                // `A57`: наводить аналоговый розыгрыш конусом на габарит СЦЕНЫ
                // (не детектора — иначе режется вещество пробы). Это оценщик, а
                // не физика: ожидание то же, дисперсия меньше — на дальней сцене
                // шум континуума 11.26 → 0.95 % при тех же историях. Умолчанием
                // ВЫКЛЮЧЕН, включённый меняет клеймо.
                // `far` (решение Amber 02.09.2026) — включить конус ТОЛЬКО там,
                // где источник дальний, то есть где он вне габарита сцены и
                // конусу есть куда наводиться. Ближние сцены считаются как
                // считались, и под новый код не попадают.
            {
                coneFar = a.Substring(7) == "far";
                options.AnalogConeSampling = !coneFar && Flag(a, 7, "far");
            }
            else if (a.StartsWith("--peakw=", StringComparison.Ordinal))
                // ⛔ `E34`, решение Amber 02.09.2026 (ветка «б»): допуск пика у
                // сборщика — ИЗ ГЕОМЕТРИИ, а не ноль. Нулевой запирал поправку
                // на однократное рассеяние: `InPeak` требует «недобрало не
                // больше допуска», а у рассеявшегося кванта недобор
                // положителен всегда.
                //
                // Умолчанием ВЫКЛЮЧЕН — включение двигает ПИК всех матриц, то
                // есть требует пересчёта склада и новой базы корпуса (`B24`).
                // Включённый входит в клеймо (`peakw=1`), поэтому такая матрица
                // честно другая и поверх поставочной не ляжет.
                options.PeakToleranceFromGeometry = Flag(a, 8);
            else if (a.StartsWith("--fluo=", StringComparison.Ordinal))
                // `F27`, АБЛЯЦИЯ: флуоресценция пробы и обвязки. Выключенный
                // ключ возвращает прежнее «фотон погиб вне кристалла» — только
                // так и меряется, что она даёт, без смены версии физики.
                options.SampleFluorescence = Flag(a, 7);
            else if (a == "--recollect")
                // `T43`, ЗАМЕР: разбирать луч заново на каждом шаге. Считается
                // то же самое, но разборов становится столько же, сколько шагов;
                // по разности времени и разности их числа видно, чего стоит один
                // разбор. Заведён, когда профиль снять было нечем; с 17.08.2026
                // профиль снимается без запроса прав (`CLAUDE.md` §Profiling,
                // задания `BqPerfView*`), ключ остаётся дешёвой мерой рядом с ним.
                // В счёте не применять.
                EfficiencySimulator.MeasureCollectCost = true;
            else if (a == "--force") force = true;
            // `A60`, АБЛЯЦИЯ: склад без вылета L-рентгена. Входит в
            // клеймо (`nolx=1`), то есть такая матрица честно другая.
            else if (a == "--no-lxray") options.LXrayEscape = false;

            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }
        }
        catch (ArgumentException e)
        {
            // `A77`: отказ, а не предупреждение. Прогон склада идёт часами, и
            // предупреждение в его первой строке никто не прочтёт — а мёртвый
            // ключ виден только по клейму, то есть уже после счёта.
            Console.Error.WriteLine("⛔ " + e.Message);
            return 2;
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

        // ⚠ ЗАДАННОЕ ЗЕРНО НАДО НАЗЫВАТЬ ВСЛУХ (`T114`, 31.08.2026). Зерно
        // входит в КЛЕЙМО (`ResponseMatrix.ComputeStamp`: `seed=` пишется, если
        // оно не штатное), поэтому матрица с ним — ЗАКОННАЯ, но ДРУГАЯ: её
        // отпечаток не совпадает с поставочным, и подложить её в рабочий
        // каталог можно только осознанно, ради A/B.
        //
        // ⛔ До 31.08.2026 такая матрица не годилась вовсе: зерно шло в клеймо,
        // а в файл не писалось, и после чтения с диска `Options.Seed` = 0 —
        // клеймо пересчитывалось БЕЗ `seed=` и не сходилось само с собой,
        // `IsValidFor` отвечал «нет» навсегда, разбор печатал «БЕЗ МАТРИЦЫ» у
        // всех спектров сцены. Починено подъёмом формата 6 → 7 по решению
        // Amber; здесь остаётся предупреждение, потому что тихо подменённая
        // матрица другого розыгрыша — ровно тот сорт ошибки, который не
        // проявляется, а смещает результат.
        if (options.Seed != 0)
        {
            Console.WriteLine();
            Console.WriteLine("⚠ ЗЕРНО ЗАДАНО ({0}) — это ДРУГАЯ матрица, не поставочная.", options.Seed);
            Console.WriteLine("  Зерно входит и в файл, и в клеймо (формат 7), поэтому разбор её ПРИМЕТ —");
            Console.WriteLine("  но только там, куда её положили нарочно. Для A/B по шуму это и нужно;");
            Console.WriteLine("  случайно оставленная в рабочем каталоге, она молча сместит числа. T114.");
        }
        // ⛔ `E34`: ключ, двигающий ПИК, называется вслух — по тому же доводу,
        // что и зерно выше. Матрица с ним законная, но ДРУГАЯ: её клеймо несёт
        // `peakw=1`, и в поставочный склад она годится только нарочно.
        if (options.PeakToleranceFromGeometry)
        {
            Console.WriteLine();
            Console.WriteLine("⚠ ДОПУСК ПИКА ИЗ ГЕОМЕТРИИ (--peakw=1, `E34`) — это ДРУГАЯ матрица, не поставочная.");
            Console.WriteLine("  Пик считается по допуску ПШПВ(E)/2 вместо нуля; клеймо несёт peakw=1.");
        }

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

            // ⛔ КОНУС — ПО СВОЙСТВУ СЦЕНЫ, А НЕ ПО СПИСКУ (`A57`).
            //
            // Решение Amber 02.09.2026: включать наведение там, где источник
            // ДАЛЬНИЙ. Спрашиваем это у самой сцены (`SourceOutsideScene`), а не
            // держим список геометрий: список пришлось бы править при каждой
            // новой сцене и однажды забыть, а забытая сцена молча получила бы
            // матрицу с шумом 4.5 % — тот самый, при котором волны видны глазом.
            //
            // Ставится ДО гварда: ключ входит в клеймо, и матрица с конусом
            // обязана отличаться от матрицы без него.
            if (coneFar)
            {
                var probe = new EfficiencySimulator(geometry);
                options.AnalogConeSampling = probe.SourceOutsideScene();
            }

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
                // ⛔ ЗНАК НАЗЫВАЕТСЯ СЛОВОМ, А НЕ ВЫВОДИТСЯ ЧИТАТЕЛЕМ ИЗ ЧИСЛА
                // (`S132`, 05.09.2026). Прежде здесь стояло «в {N} раза
                // дешевле» безусловно, и на физике 16 строка вышла «в 0.5 раза
                // дешевле» — при том, что догонка потратила 899.7 млн историй
                // против 420 млн плоских, то есть ВДВОЕ ДОРОЖЕ. Число было
                // верным, слово — нет, а решения принимаются по словам: ровно
                // на такой строке комментария и стояла посылка `S132`.
                double ratio = matrix.HistoriesSpent > 0
                    ? flat / matrix.HistoriesSpent : 0.0;
                Console.WriteLine("   историй  : {0:N0} против {1:N0} плоских — в {2:F1} раза {3}; "
                                  + "самый дорогой узел {4:N0}",
                                  (double)matrix.HistoriesSpent, flat,
                                  ratio >= 1.0 ? ratio : (ratio > 0.0 ? 1.0 / ratio : 0.0),
                                  ratio >= 1.0 ? "дешевле" : "ДОРОЖЕ",
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
                    // `dropped_pct` — замер к `S55`: доля историй аналоговой
                    // ветки, выброшенных правилом «округлилось в бин пика».
                    w.WriteLine("node,energy_kev,histories,error_pct,seconds_wall,dropped,scored,dropped_pct,dropped_scat,scat_pct");
                    long[] dropped = ResponseMatrixBuilder.NodeDropped;
                    long[] scored = ResponseMatrixBuilder.NodeScored;
                    long[] droppedScat = ResponseMatrixBuilder.NodeDroppedScattered;
                    for (int i = 0; i < matrix.Energies.Length; i++)
                    {
                        long d = dropped != null && i < dropped.Length ? dropped[i] : 0L;
                        long sc = scored != null && i < scored.Length ? scored[i] : 0L;
                        long ds = droppedScat != null && i < droppedScat.Length ? droppedScat[i] : 0L;
                        w.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1:F3},{2},{3:F3},{4:F3},{5},{6},{7:F3},{8},{9:F3}", i, matrix.Energies[i],
                            matrix.NodeHistories[i],
                            matrix.NodeErrors != null ? matrix.NodeErrors[i] : 0.0,
                            matrix.NodeSeconds != null ? matrix.NodeSeconds[i] : 0.0,
                            d, sc, d + sc > 0L ? 100.0 * d / (d + sc) : 0.0,
                            ds, d + sc > 0L ? 100.0 * ds / (d + sc) : 0.0));
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

        // ⛔ ТИХАЯ ПОТЕРЯ КВАНТА — ОТДЕЛЬНАЯ СТРОКА ОТЧЁТА (`A65`).
        // Очереди переноса конечны (вылеты и отложенные кванты истории), и
        // при переполнении квант отбрасывается. Замер 02.09.2026: на 59.5 и
        // 662 кэВ отброса нет вовсе, на 2614 — 3 кванта на 2 млн историй, то
        // есть 1.5e-6 на историю при шуме розыгрыша 0.5 %. Но молчащий отброс
        // однажды вырастет и никем не будет замечен, поэтому он печатается
        // ВСЕГДА, когда он ненулевой, — и печатается ДОЛЕЙ, а не штуками.
        long droppedTotal = EfficiencySimulator.TotalEscapeDropped
                       + EfficiencySimulator.TotalPendingDropped;
        if (droppedTotal > 0)
        {
            Console.WriteLine("⚠ отброшено переполнением очередей: вылетов {0}, отложенных {1}",
                              EfficiencySimulator.TotalEscapeDropped,
                              EfficiencySimulator.TotalPendingDropped);
        }

        Console.WriteLine(quiet ? "ВСЕ СОШЛИСЬ" : "ЕСТЬ ШУМНЫЕ");
        return quiet ? 0 : 1;
    }
}
