// ═══════════════════════════════════════════════════════════════════════════
//  `AMBER330`, 15.11.2026. ШКАЛА ЭНЕРГИИ, УЛЕТЕВШАЯ ВЫШЕ ОПОР
// ═══════════════════════════════════════════════════════════════════════════
//
//  ЧТО ИЗМЕРЯЕТСЯ. Спектр Lu-176 (папка спектров AS80x80) пришёл со шкалой
//  четвёртой степени, которая на линиях Lu-176 верна, а к каналу 5968 даёт
//  18 906 кэВ. Проба отвечает на три вопроса.
//
//  1. ОТКУДА ЭТА КВАРТИКА. Ключ `--file=` — копия файла; первая
//     `<EnergyCalibration>` спектра сверяется с ТОЧНОЙ интерполяцией через пять
//     опор (164; 55.79) (259; 88.34) (569; 201.83) (851; 306.78) (1429; 508.6):
//     голый `CalibrationSolver.Solve(точки, 4)` — путь «Выполнить калибровку»
//     ДО сторожа изгиба (`S42`, 06.09.2026). Совпадение до 1e-9 — положительный
//     контроль «старый путь дефект воспроизводит».
//
//  2. ДАЁТ ЛИ ЕЁ НЫНЕШНИЙ КОД. Те же пять опор — через решатель кнопки
//     «Выполнить калибровку» (`DCFwhmCalibrationView.SolveEnergy` →
//     `SolveGuarded`): степень обязана понизиться, канал 5968 — остаться ниже
//     3000 кэВ, `BendOk` квартики — «нет».
//
//  3. КВАДРАТИКА RadiaCode/Obsidian (`FitRcEnergyCalibration`) — единственная
//     подгонка шкалы панели калибровки, шедшая мимо сторожа. Положительный
//     контроль: три нижние опоры с изгибом — голый `Solve(…, 2)` улетает,
//     `SolveRcQuadratic` (с правкой) обязан дать прямую тремя числами (a₂ = 0);
//     здоровая парабола обязана выйти побитово прежней.
//
//  5. (`AMBER330`, второй круг, находка критика `crit330`) Что «Сохранить в
//     конфигурацию прибора» кладёт RadiaCode/Obsidian (`DeviceQuadratic`): при
//     основной шкале ПЕРВОЙ степени прежде уходил `null` (ветка `else`), хотя
//     годная прямая была готова. Положительный контроль: на коде до правки
//     метода нет — отказ «ДЕФЕКТ: прямая не доезжает до прибора»; с правкой —
//     прямая тремя числами (a₂ = 0), побитово та же, что основная шкала, а
//     степени 2 и 3+ — как прежде.
//
//  4. (`--fit`) Перекалибровка файла по его же пикам: пары «канал:энергия»
//     ключом `--lines=259:88.34,568:201.83,…`, степень — `--order=` (умолчание
//     4, как у прибора), тем же `SolveEnergy`. Печатает коэффициенты «R» для
//     записи в файл, энергии опор, каналов 5968 и 8191 и контрольных пар
//     `--check=6920:2614.51` (контрольная пара; взята ли она в подгонку — решает `--lines`).
//
//    CalibBendProbeA330.exe --file=<копия Lu-176.xml> [--fit --lines=… --order=N --check=…]
//
//  Ожидание: «ВСЕ КОНТРОЛИ СОШЛИСЬ», код 0. На коде до правки п. 3 даёт
//  «ДЕФЕКТ: парабола RadiaCode мимо сторожа» и код 1 (метода `SolveRcQuadratic`
//  там нет — отражение его не находит, и это тоже отказ).
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using BecquerelMonitor;
using BecquerelMonitor.Utils;

static class CalibBendProbeA330
{
    static int failures;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ок    " : "  ОТКАЗ ") + what);
        if (!ok) failures++;
    }

    static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

    static double Poly(double[] c, double x)
    {
        double e = 0.0;
        for (int i = c.Length - 1; i >= 0; i--) e = e * x + c[i];
        return e;
    }

    static List<CalibrationPoint> Points(params double[] chE)
    {
        var list = new List<CalibrationPoint>();
        for (int i = 0; i < chE.Length; i += 2)
            list.Add(new CalibrationPoint((int)chE[i], (decimal)chE[i + 1], 0));
        return list;
    }

    static double[] FileCalibration(string path)
    {
        var doc = new XmlDocument();
        doc.Load(path);
        XmlNode node = doc.SelectSingleNode("/ResultDataFile/ResultDataList/ResultData/EnergySpectrum/EnergyCalibration/Coefficients");
        return node.ChildNodes.Cast<XmlNode>()
                   .Where(n => n.Name == "Coefficient")
                   .Select(n => double.Parse(n.InnerText, CultureInfo.InvariantCulture)).ToArray();
    }

    static double[] SolveEnergy(List<CalibrationPoint> pts, int order, int channels, out int used)
    {
        MethodInfo m = typeof(DCFwhmCalibrationView).GetMethod("SolveEnergy", BindingFlags.NonPublic | BindingFlags.Static);
        if (m == null) throw new Exception("нет DCFwhmCalibrationView.SolveEnergy");
        object[] a = { pts, order, channels, false, 0 };
        double[] r = (double[])m.Invoke(null, a);
        used = (int)a[4];
        return r;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string file = null, linesKey = null, checkKey = null;
        int fitOrder = 4;
        bool fit = false;
        foreach (string a in args)
        {
            if (a.StartsWith("--file=")) file = a.Substring(7);
            else if (a.StartsWith("--lines=")) linesKey = a.Substring(8);
            else if (a.StartsWith("--check=")) checkKey = a.Substring(8);
            else if (a.StartsWith("--order=")) fitOrder = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a == "--fit") fit = true;
        }
        const int N = 8192;
        var five = Points(164, 55.79, 259, 88.34, 569, 201.83, 851, 306.78, 1429, 508.6);

        // ── 1. Происхождение квартики ────────────────────────────────────────
        Console.WriteLine("1. Квартика файла = точная интерполяция через пять опор (путь до S42)");
        double[] old = CalibrationSolver.Solve(five, 4);
        Console.WriteLine("   Solve(5 опор, 4): " + string.Join(", ", old.Select(F)));
        Console.WriteLine("   E(5968) = " + Poly(old, 5968).ToString("f1", CultureInfo.InvariantCulture)
                          + " кэВ, E(8191) = " + Poly(old, 8191).ToString("f1", CultureInfo.InvariantCulture) + " кэВ");
        if (file != null)
        {
            double[] inFile = FileCalibration(file);
            Console.WriteLine("   в файле:          " + string.Join(", ", inFile.Select(F)));
            double worst = 0.0;
            for (int i = 0; i < 5; i++) worst = Math.Max(worst, Math.Abs(old[i] - inFile[i]) / Math.Abs(inFile[i]));
            Console.WriteLine("   наибольшее относительное расхождение коэффициентов: " + worst.ToString("e2", CultureInfo.InvariantCulture));
            Check(inFile.Length == 5 && worst < 1e-9, "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: голый Solve по пяти опорам воспроизводит шкалу файла");
        }
        double[] line = CalibrationSolver.Solve(five, 1);
        Check(!CalibrationSolver.BendOk(old, line, N, Math.Max(5.0, 0.5 * 164)), "BendOk отвергает квартику файла");
        var oldCal = new PolynomialEnergyCalibration { Coefficients = (double[])old.Clone(), PolynomialOrder = 4 };
        Console.WriteLine("   (справка) CheckCalibration квартики на 8192 каналах: " + oldCal.CheckCalibration(N)
                          + " — открытие файла её принимает молча, предел энергии 100 000 кэВ");

        // ── 2. Нынешний решатель кнопки «Выполнить калибровку» ───────────────
        Console.WriteLine("2. Те же опоры через SolveEnergy (кнопка «Выполнить калибровку», 17716b7b)");
        int used;
        double[] now = SolveEnergy(five, 4, N, out used);
        Console.WriteLine("   степень " + used + ": " + string.Join(", ", now.Select(F)));
        Console.WriteLine("   E(5968) = " + Poly(now, 5968).ToString("f1", CultureInfo.InvariantCulture)
                          + " кэВ, E(8191) = " + Poly(now, 8191).ToString("f1", CultureInfo.InvariantCulture) + " кэВ");
        Check(used < 4 && Poly(now, 5968) < 3000.0, "нынешний решатель квартику НЕ даёт: степень понижена, канал 5968 < 3000 кэВ");

        // ── 3. Парабола RadiaCode/Obsidian ────────────────────────────────────
        Console.WriteLine("3. Парабола RadiaCode/Obsidian (FitRcEnergyCalibration)");
        MethodInfo rc = typeof(DCFwhmCalibrationView).GetMethod("SolveRcQuadratic", BindingFlags.NonPublic | BindingFlags.Static);
        if (rc == null)
        {
            Check(false, "ДЕФЕКТ: парабола RadiaCode мимо сторожа — SolveRcQuadratic нет, путь зовёт голый Solve(точки, 2)");
        }
        else
        {
            var bent = Points(200, 60.0, 600, 200.0, 1000, 380.0);
            double[] bare = CalibrationSolver.Solve(bent, 2);
            double[] guarded = (double[])rc.Invoke(null, new object[] { bent, N, false });
            Console.WriteLine("   изогнутые опоры: голый Solve E(8191) = " + Poly(bare, 8191).ToString("f0", CultureInfo.InvariantCulture)
                              + " кэВ; со сторожем " + (guarded == null ? "null" : string.Join(", ", guarded.Select(F))));
            Check(!CalibrationSolver.BendOk(bare, CalibrationSolver.Solve(bent, 1), N, 100.0),
                  "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: голая парабола по этим опорам гнётся сверх меры");
            Check(guarded != null && guarded.Length == 3 && guarded[2] == 0.0 && Poly(guarded, 8191) < 4000.0,
                  "со сторожем — прямая тремя числами (a₂ = 0), E(8191) < 4000 кэВ");
            var healthy = Points(100, 32.0, 900, 330.0, 1800, 662.0, 3700, 1332.0);
            double[] h0 = CalibrationSolver.Solve(healthy, 2);
            double[] h1 = (double[])rc.Invoke(null, new object[] { healthy, N, false });
            Check(h1 != null && h1.Length == 3 && h0.Zip(h1, (x, y) => x.Equals(y)).All(b => b),
                  "здоровая парабола — побитово прежняя (" + string.Join(", ", h0.Select(F)) + ")");
        }

        // ── 5. Шкала, уходящая в RadiaCode/Obsidian ─────────────────────────
        Console.WriteLine("5. «Сохранить в конфигурацию прибора»: шкала RadiaCode/Obsidian (DeviceQuadratic)");
        MethodInfo dq = typeof(DCFwhmCalibrationView).GetMethod("DeviceQuadratic", BindingFlags.NonPublic | BindingFlags.Static);
        if (dq == null || rc == null)
        {
            Check(false, "ДЕФЕКТ: прямая не доезжает до прибора — при основной шкале 1-й степени RadiaCode/Obsidian получает null (метода DeviceQuadratic нет)");
        }
        else
        {
            Func<PolynomialEnergyCalibration, PolynomialEnergyCalibration, PolynomialEnergyCalibration> Dq =
                (m, f) => (PolynomialEnergyCalibration)dq.Invoke(null, new object[] { m, f });
            // основная шкала — та прямая, которую сторож дал по пяти опорам файла (п. 2)
            var mainLine = new PolynomialEnergyCalibration { Coefficients = (double[])now.Clone(), PolynomialOrder = now.Length - 1 };
            double[] rcFive = (double[])rc.Invoke(null, new object[] { five, N, false });
            var fittedFive = rcFive == null ? null : new PolynomialEnergyCalibration { Coefficients = rcFive, PolynomialOrder = 2 };
            var g1 = Dq(mainLine, null);
            Check(mainLine.PolynomialOrder == 1 && g1 != null && g1.PolynomialOrder == 2 && g1.Coefficients.Length == 3
                  && g1.Coefficients[0].Equals(now[0]) && g1.Coefficients[1].Equals(now[1]) && g1.Coefficients[2] == 0.0,
                  "степень 1, параболы нет — прибор получает ту же прямую тремя числами (a₂ = 0), побитово");
            var g2 = Dq(mainLine, fittedFive);
            Check(g2 != null && fittedFive != null && g2.Coefficients.SequenceEqual(fittedFive.Coefficients)
                  && g2.Coefficients[0].Equals(now[0]) && g2.Coefficients[1].Equals(now[1]),
                  "степень 1, парабола подогнана (SolveRcQuadratic понизил до прямой) — уходит она, и это та же прямая");
            Check(g2 != null && g2.CheckCalibration(N), "CheckCalibration прямой прибора");
            var quad = new PolynomialEnergyCalibration { Coefficients = new[] { -6.41, 0.3795, -4.79e-6 }, PolynomialOrder = 2 };
            var g3 = Dq(quad, fittedFive);
            Check(g3 != null && g3.Coefficients.SequenceEqual(quad.Coefficients), "степень 2 — как прежде: основная шкала");
            var quart = new PolynomialEnergyCalibration { Coefficients = (double[])old.Clone(), PolynomialOrder = 4 };
            var g4 = Dq(quart, fittedFive);
            Check(g4 != null && g4.Coefficients.SequenceEqual(fittedFive.Coefficients), "степень 4 — как прежде: подогнанная парабола");
            Check(Dq(quart, null) == null, "степень 4 без параболы — как прежде: null");
        }

        // ── 4. Перекалибровка файла ──────────────────────────────────────────
        if (fit)
        {
            Console.WriteLine("4. Перекалибровка по пикам спектра (SolveEnergy, запрошена степень " + fitOrder + ")");
            var pairs = linesKey.Split(',').Select(s => s.Split(':')).ToArray();
            var pts = new List<CalibrationPoint>();
            foreach (var pr in pairs)
                pts.Add(new CalibrationPoint(int.Parse(pr[0], CultureInfo.InvariantCulture), decimal.Parse(pr[1], CultureInfo.InvariantCulture), 0));
            int u;
            double[] c = SolveEnergy(pts, fitOrder, N, out u);
            Check(c != null, "подгонка годна");
            if (c != null)
            {
                Console.WriteLine("   степень " + u);
                for (int i = 0; i < c.Length; i++) Console.WriteLine("   COEF " + F(c[i]));
                foreach (var p in pts)
                    Console.WriteLine("   канал " + p.Channel + ": " + Poly(c, p.Channel).ToString("f2", CultureInfo.InvariantCulture)
                                      + " кэВ (линия " + ((double)p.Energy).ToString("f2", CultureInfo.InvariantCulture) + ")");
                if (checkKey != null)
                    foreach (var pr in checkKey.Split(',').Select(s => s.Split(':')))
                        Console.WriteLine("   КОНТРОЛЬ канал " + pr[0] + ": " + Poly(c, double.Parse(pr[0], CultureInfo.InvariantCulture)).ToString("f1", CultureInfo.InvariantCulture)
                                          + " кэВ (линия " + pr[1] + ")");
                Console.WriteLine("   канал 5968: " + Poly(c, 5968).ToString("f1", CultureInfo.InvariantCulture)
                                  + " кэВ; канал 8191: " + Poly(c, 8191).ToString("f1", CultureInfo.InvariantCulture) + " кэВ");
                var cal = new PolynomialEnergyCalibration { Coefficients = (double[])c.Clone(), PolynomialOrder = c.Length - 1 };
                Check(cal.CheckCalibration(N), "CheckCalibration новой шкалы");
            }
        }

        Console.WriteLine(failures == 0 ? "ВСЕ КОНТРОЛИ СОШЛИСЬ" : ("ОТКАЗОВ: " + failures));
        return failures == 0 ? 0 : 1;
    }
}
