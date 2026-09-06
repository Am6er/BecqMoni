// ═══════════════════════════════════════════════════════════════════════════
//  Полоса F48, 06.09.2026. ГОДНА ЛИ ШКАЛА ИЗ НЕ-ЧИСЕЛ?
// ═══════════════════════════════════════════════════════════════════════════
//
//  ДЕФЕКТ ПРИЛОЖЕНИЯ, найден полосой F44 05.09.2026 и измерен ею же:
//  `PolynomialEnergyCalibration.CheckCalibration` объявляла ГОДНЫМИ шкалы
//  `[NaN, NaN]` и `[NaN, +∞]`. Причина в языке, а не в физике: любое сравнение
//  с `NaN` ложно, поэтому ни `Coefficients[1] == 0`, ни `prevEnrg >= 100000.0`,
//  ни `prevEnrg > ChannelToEnergy(i)` не срабатывают, и цикл проходит насквозь.
//
//  ЧТО ВИДЕЛ ЧЕЛОВЕК: две точки калибровки на ОДНОМ канале (или одна точка на
//  канале 0 — кнопка «Рассчитать» сама добавляет вторую, нулевую) дают
//  вырожденную матрицу, `CalibrationSolver.Solve` отдаёт `NaN`, проверка
//  молчит, `NaN` уезжает в коэффициенты, конфигурация прибора метится
//  изменённой и шкала сохраняется. Ни окна, ни строки в состоянии.
//
//  ЧТО МЕРЯЕТСЯ ЗДЕСЬ:
//
//    1. НАБОРЫ КОЭФФИЦИЕНТОВ. Поимённая таблица входов с ожидаемым ответом:
//       не-числа отвергаются, законные шкалы принимаются. Оба плеча в одной
//       таблице нарочно — проверка, которая только отвергает, прошла бы и на
//       `return false;` в первой строке метода.
//    2. ПУТЬ ЧЕЛОВЕКА. Точки калибровки прогоняются через тот же
//       `Utils.CalibrationSolver`, каким их гонит кнопка «Рассчитать»
//       (`DCEnergyCalibrationView.button7_Click`), и ответ проверки берётся у
//       ПОЛУЧЕННЫХ коэффициентов, а не у выдуманных.
//    3. КОРПУС, ВСЕ 129 СПЕКТРОВ. Цена ошибки в другую сторону: ни одна
//       законная шкала корпуса не смеет быть отвергнута. Считаются и
//       собственные шкалы спектров, и шкалы встроенных фонов.
//
//  ⛔ ОБРАТНЫЙ КОНТРОЛЬ — не ключ, а ДРУГАЯ СБОРКА: та же проба, собранная
//  против приложения БЕЗ заслона (`bin\Debug_F48_before`), обязана вернуть 1 и
//  назвать вход поимённо. Ключ `--expect-broken` только меняет слово в итоге,
//  чтобы прогон читался: он НЕ подменяет ни одной проверки.
//
//    CalibrationNanProbeF48.exe [--corpus=<каталог со спектрами>] [--expect-broken]
//
//  Ожидание на исправленной сборке: «ВСЕ СОШЛИСЬ», код 0.
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Serialization;
using BecquerelMonitor;

static class CalibrationNanProbeF48
{
    static int bad;
    static int checks;

    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // ⛔ Сторож модальных окон — ПЕРВЫМ ДЕЛОМ (образец `CultureProbeO14`,
        //    `A245`): негодная шкала умеет поднимать окно из
        //    `UnusableCalibration`, а нажать «ОК» в безоконном прогоне некому.
        ModalWatchStart();

        string corpus = null;
        bool expectBroken = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--corpus=", StringComparison.Ordinal)) corpus = a.Substring(9);
            else if (a == "--expect-broken") expectBroken = true;
        }

        Console.WriteLine("сборка приложения: " + typeof(PolynomialEnergyCalibration).Assembly.Location);
        try
        {
            Console.WriteLine("собрана:           "
                + File.GetLastWriteTime(typeof(PolynomialEnergyCalibration).Assembly.Location)
                      .ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) { Console.WriteLine("собрана:           не прочитана: " + ex.Message); }

        Coefficients();
        HumanPath();
        Corpus(corpus);

        ModalWatchStop();
        Console.WriteLine();
        Console.WriteLine("проверок: " + checks.ToString(CultureInfo.InvariantCulture));
        if (bad == 0)
        {
            Console.WriteLine(expectBroken
                ? "⛔ КОНТРОЛЬ НЕ СРАБОТАЛ: сборка БЕЗ заслона прошла проверку — значит проверка ничего не проверяет"
                : "ВСЕ СОШЛИСЬ");
            return expectBroken ? 3 : 0;
        }

        Console.WriteLine("НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture)
            + (expectBroken ? " (так и надо: это обратный контроль на сборке без заслона)" : ""));
        return 1;
    }

    // ------------------------------------------------------------------
    // 1. НАБОРЫ КОЭФФИЦИЕНТОВ
    // ------------------------------------------------------------------

    /// <summary>
    /// Таблица «вход → ожидаемый ответ». Числа входа печатаются и разбираются
    /// инвариантной культурой (`A244`); `R` нарочно — `NaN` и `∞` обязаны быть
    /// видны как есть.
    /// </summary>
    static void Coefficients()
    {
        Console.WriteLine();
        Console.WriteLine("=== 1. наборы коэффициентов: годна ли шкала ===");

        Case("не-числа: [NaN, NaN]", 1, false, double.NaN, double.NaN);
        Case("не-числа: [NaN, +∞]", 1, false, double.NaN, double.PositiveInfinity);
        Case("не-числа: [1, NaN] (наклон не число)", 1, false, 1.0, double.NaN);
        Case("не-числа: [NaN, 1] (сдвиг не число)", 1, false, double.NaN, 1.0);
        Case("не-числа: [+∞, 1]", 1, false, double.PositiveInfinity, 1.0);
        Case("не-числа: [-∞, 1]", 1, false, double.NegativeInfinity, 1.0);
        Case("не-числа: [0, +∞]", 1, false, 0.0, double.PositiveInfinity);
        Case("не-числа: степень 2, [0, 1, NaN]", 2, false, 0.0, 1.0, double.NaN);
        Case("не-числа: степень 2, [NaN, 1, 1e-6]", 2, false, double.NaN, 1.0, 1e-06);

        // ⛔ ВТОРОЕ ПЛЕЧО. Без него проверка прошла бы и на методе, который
        //    отвергает ВСЁ, — а это дефект дороже исходного: ни один спектр
        //    тогда не открылся бы.
        Case("законная линейная: [0, 1]", 1, true, 0.0, 1.0);
        Case("законная линейная: [-5.5, 0.732]", 1, true, -5.5, 0.732);
        Case("законная квадратичная: [0.5, 0.73, 1.1e-6]", 2, true, 0.5, 0.73, 1.1e-06);

        // Прежние отказы — их ответ обязан остаться прежним.
        Case("вырожденная (наклон 0): [0, 0]", 1, false, 0.0, 0.0);
        Case("убывающая шкала: [1000, -0.5]", 1, false, 1000.0, -0.5);
    }

    static void Case(string name, int order, bool expected, params double[] coefficients)
    {
        var calibration = new PolynomialEnergyCalibration();
        calibration.PolynomialOrder = order;
        calibration.Coefficients = (double[])coefficients.Clone();

        bool answer;
        string note = "";
        try
        {
            answer = calibration.CheckCalibration(channels: 1024);
        }
        catch (Exception ex)
        {
            answer = false;
            note = " (бросила " + ex.GetType().Name + ")";
        }

        Verdict(name + " = [" + Show(coefficients) + "]" + note, expected, answer);
    }

    static string Show(double[] values)
    {
        var parts = new List<string>();
        foreach (double v in values)
        {
            parts.Add(v.ToString("R", CultureInfo.InvariantCulture));
        }

        return string.Join(", ", parts);
    }

    // ------------------------------------------------------------------
    // 2. ПУТЬ ЧЕЛОВЕКА
    // ------------------------------------------------------------------

    /// <summary>
    /// То же, что делает кнопка «Рассчитать»: точки → `CalibrationSolver` →
    /// коэффициенты → `CheckCalibration`. Выдуманных коэффициентов здесь нет
    /// нарочно: беда пришла из решателя, и мерить надо его выход.
    /// </summary>
    static void HumanPath()
    {
        Console.WriteLine();
        Console.WriteLine("=== 2. путь человека: точки калибровки -> решатель -> проверка ===");

        Points("две точки на ОДНОМ канале (512 -> 661.7 и 512 -> 1460.8)", 1, false,
               new[] { 512, 512 }, new[] { 661.7, 1460.8 });

        // Одна точка: кнопка сама добавляет нулевую (`button7_Click`), и если
        // единственная точка стоит на канале 0, обе оказываются на нём же.
        Points("одна точка на канале 0 плюс добавленная кнопкой нулевая", 1, false,
               new[] { 0, 0 }, new[] { 661.7, 0.0 });

        // ⛔ ВТОРОЕ ПЛЕЧО: настоящая калибровка тем же путём обязана пройти.
        Points("две разные точки (0 -> 0 и 512 -> 661.7)", 1, true,
               new[] { 0, 512 }, new[] { 0.0, 661.7 });
        Points("три точки, степень 2", 2, true,
               new[] { 100, 512, 900 }, new[] { 129.5, 661.7, 1173.2 });
    }

    static void Points(string name, int order, bool expected, int[] channels, double[] energies)
    {
        var points = new List<CalibrationPoint>();
        for (int i = 0; i < channels.Length; i++)
        {
            points.Add(new CalibrationPoint(channels[i], (decimal)energies[i], 0));
        }

        double[] matrix;
        try
        {
            matrix = BecquerelMonitor.Utils.CalibrationSolver.Solve(points, order);
        }
        catch (Exception ex)
        {
            Console.WriteLine("  решатель бросил " + ex.GetType().Name + " — до проверки дело не дошло");
            Verdict(name + ": решатель отказал ЯВНО", expected, false);
            return;
        }

        if (matrix == null)
        {
            Verdict(name + ": решатель вернул null", expected, false);
            return;
        }

        var calibration = new PolynomialEnergyCalibration();
        calibration.PolynomialOrder = matrix.Length - 1;
        calibration.Coefficients = matrix;
        bool answer = calibration.CheckCalibration(channels: 1024);
        Verdict(name + " -> [" + Show(matrix) + "]", expected, answer);
    }

    // ------------------------------------------------------------------
    // 3. КОРПУС
    // ------------------------------------------------------------------

    /// <summary>
    /// Цена ошибки в ДРУГУЮ сторону. Заслон, отвергающий лишнее, хуже
    /// исходного дефекта: спектр перестал бы открываться. Меряется на всех
    /// 129 спектрах корпуса — и на их встроенных фонах, у которых шкала своя.
    /// </summary>
    static void Corpus(string dir)
    {
        Console.WriteLine();
        Console.WriteLine("=== 3. корпус: ни одна законная шкала не отвергнута ===");

        if (string.IsNullOrEmpty(dir))
        {
            dir = Path.Combine(Repo(), "tools", "CORPUS", "corpus", "spectra");
        }

        if (!Directory.Exists(dir))
        {
            Console.WriteLine("  каталога спектров нет: " + dir);
            Verdict("каталог спектров корпуса найден", true, false);
            return;
        }

        string[] files = Directory.GetFiles(dir, "*.xml");
        Array.Sort(files, StringComparer.Ordinal);
        Console.WriteLine("  каталог: " + dir);
        Console.WriteLine("  файлов:  " + files.Length.ToString(CultureInfo.InvariantCulture));

        var serializer = new XmlSerializer(typeof(ResultDataFile));
        int scales = 0, rejected = 0, notPolynomial = 0, unreadable = 0;
        var names = new List<string>();

        foreach (string path in files)
        {
            ResultDataFile file;
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    file = (ResultDataFile)serializer.Deserialize(stream);
                }
            }
            catch (Exception ex)
            {
                unreadable++;
                Console.WriteLine("  НЕ ПРОЧИТАН {0}: {1}", Path.GetFileName(path), ex.GetType().Name);
                continue;
            }

            foreach (ResultData rd in file.ResultDataList)
            {
                Judge(Path.GetFileName(path) + " · спектр", rd.EnergySpectrum,
                      ref scales, ref rejected, ref notPolynomial, names);
                Judge(Path.GetFileName(path) + " · фон", rd.BackgroundEnergySpectrum,
                      ref scales, ref rejected, ref notPolynomial, names);
            }
        }

        Console.WriteLine("  шкал полиномиальных: {0}, не полиномиальных: {1}, файлов не прочитано: {2}",
                          scales.ToString(CultureInfo.InvariantCulture),
                          notPolynomial.ToString(CultureInfo.InvariantCulture),
                          unreadable.ToString(CultureInfo.InvariantCulture));
        foreach (string n in names)
        {
            Console.WriteLine("  ⛔ ОТВЕРГНУТА: " + n);
        }

        Verdict("все 129 файлов корпуса прочитаны", 129, files.Length);
        Verdict("шкал проверено больше сотни (иначе мерить нечего)", true, scales > 100);
        Verdict("законных шкал корпуса отвергнуто", 0, rejected);
    }

    static void Judge(string name, EnergySpectrum spectrum, ref int scales, ref int rejected,
                      ref int notPolynomial, List<string> names)
    {
        if (spectrum == null)
        {
            return;
        }

        var calibration = spectrum.EnergyCalibration as PolynomialEnergyCalibration;
        if (calibration == null)
        {
            if (spectrum.EnergyCalibration != null) notPolynomial++;
            return;
        }

        scales++;
        bool answer;
        try
        {
            answer = calibration.CheckCalibration(channels: spectrum.NumberOfChannels);
        }
        catch (Exception ex)
        {
            answer = false;
            name += " (бросила " + ex.GetType().Name + ")";
        }

        if (!answer)
        {
            rejected++;
            names.Add(name + " степень " + calibration.PolynomialOrder.ToString(CultureInfo.InvariantCulture)
                      + ", [" + Show(calibration.Coefficients) + "]");
        }
    }

    // ------------------------------------------------------------------
    // Служебное
    // ------------------------------------------------------------------

    static string Repo()
    {
        string dir = Path.GetDirectoryName(typeof(CalibrationNanProbeF48).Assembly.Location);
        for (int i = 0; i < 8 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "tools", "CORPUS")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return Directory.GetCurrentDirectory();
    }

    static void Verdict(string what, object expected, object got)
    {
        checks++;
        bool ok = Equals(expected, got);
        if (!ok) bad++;
        Console.WriteLine("  {0} {1}: ждали {2}, вышло {3}",
                          ok ? "ок  " : "⛔ НЕТ", what, Word(expected), Word(got));
    }

    static string Word(object value)
    {
        if (value is bool) return (bool)value ? "ГОДНА" : "отвергнута";
        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  СТОРОЖ МОДАЛЬНЫХ ОКОН (приём взят у `CultureProbeO14`, `A245`).
    //  Каждые 200 мс перечисляет окна СВОЕГО процесса класса `#32770`,
    //  называет их текст, засчитывает расхождение и посылает `WM_CLOSE`:
    //  безоконный прогон не имеет права висеть на «ОК».
    // ══════════════════════════════════════════════════════════════════════

    const string DialogClass = "#32770";
    const uint WM_CLOSE = 0x0010;

    delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowProc lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hWnd, EnumWindowProc lpfn, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    static volatile bool modalWatchStop;
    static Thread modalWatchThread;

    static void ModalWatchStart()
    {
        modalWatchThread = new Thread(delegate()
        {
            uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            var known = new Dictionary<long, bool>();
            while (!modalWatchStop)
            {
                var found = new List<IntPtr>();
                try
                {
                    EnumWindows(delegate(IntPtr h, IntPtr l)
                    {
                        uint pid;
                        GetWindowThreadProcessId(h, out pid);
                        if (pid != self) return true;
                        var cls = new StringBuilder(64);
                        GetClassNameW(h, cls, cls.Capacity);
                        if (cls.ToString() == DialogClass) found.Add(h);
                        return true;
                    }, IntPtr.Zero);
                }
                catch (Exception) { }

                foreach (IntPtr h in found)
                {
                    long key = h.ToInt64();
                    if (known.ContainsKey(key)) continue;
                    known[key] = true;
                    bad++;
                    Console.WriteLine("⛔ БЕЗОКОННЫЙ ПУТЬ УПЁРСЯ В МОДАЛЬНОЕ ОКНО: «" + ModalText(h)
                        + "» — сторож закрывает его сам, разряд `A245`");
                    try { PostMessage(h, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
                    catch (Exception) { }
                }

                Thread.Sleep(200);
            }
        });
        modalWatchThread.IsBackground = true;
        modalWatchThread.Start();
    }

    static string ModalText(IntPtr dialog)
    {
        var acc = new StringBuilder();
        try
        {
            EnumChildWindows(dialog, delegate(IntPtr ch, IntPtr l)
            {
                var cls = new StringBuilder(64);
                GetClassNameW(ch, cls, cls.Capacity);
                if (cls.ToString() == "Static")
                {
                    var txt = new StringBuilder(512);
                    GetWindowTextW(ch, txt, txt.Capacity);
                    string s = txt.ToString().Trim();
                    if (s.Length > 0)
                    {
                        if (acc.Length > 0) acc.Append(" / ");
                        acc.Append(s);
                    }
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception) { }

        return acc.Length == 0 ? "(текст не прочитан)" : acc.ToString();
    }

    static void ModalWatchStop()
    {
        modalWatchStop = true;
        if (modalWatchThread != null) modalWatchThread.Join(2000);
    }
}
