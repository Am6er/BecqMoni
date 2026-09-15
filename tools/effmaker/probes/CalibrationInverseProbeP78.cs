// ═══════════════════════════════════════════════════════════════════════════
//  Полоса П78, 15.09.2026. AMBER36: ДОСТИЖИМ ЛИ ОТКАЗ РЕШАТЕЛЯ В EnergyToChannel?
// ═══════════════════════════════════════════════════════════════════════════
//
//  СТРОКА `AMBER36`: `PolynomialEnergyCalibration.EnergyToChannel` при порядке
//  > 4 на отказе решателя (`FindRoots.OfFunction`) отвечает `return 0` молча.
//  Решение Amber 15.09.2026, дословно: «Сначала доказать дефект на допустимых
//  числах (рекомендую); без подтверждения поправить постановку и оставить код.»
//
//  ЧТО МЕРЯЕТСЯ. Проба зовёт ЖИВОЙ `EnergyToChannel` собранного приложения и
//  рядом — ТОТ ЖЕ `MathNet.Numerics.FindRoots.OfFunction` с ТОЙ ЖЕ лямбдой и
//  теми же границами `[0, N]`, что стоят в `EnrgToChannel` (отражением: у проб
//  нет ссылки на MathNet.Numerics.dll, а сборка лежит рядом с приложением).
//  Бросил решатель — значит приложение прошло через `catch { return 0; }`.
//  Так «молчаливый ноль» отличается от честного нуля зажима `enrg < c0`.
//
//  Отказы решателя считаются РАЗДЕЛЬНО:
//    catchValid   — на ДОПУСТИМЫХ числах: конечная энергия, монотонная шкала,
//                   свежий кеш, N ≥ 1. Больше нуля — дефект ДОКАЗАН (исход A).
//    catchInvalid — на недопустимых (NaN, протухший кеш, плоская шкала): это
//                   ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ читателя — доказательство, что
//                   проба ВИДИТ отказ решателя, когда он есть.
//    outOfRange   — ответ вне [0, N]: не ноль, а другой род беды (наблюдение).
//
//  РАЗДЕЛЫ (кандидаты из задания полосы):
//    §0 среда: сборка, MathNet, расширяет ли решатель отрезок;
//    §1 допустимые шкалы порядков 1…6, четыре размера N, сетка энергий по всей
//       шкале и особые точки (края, ±∞, 1e9, double.Epsilon);
//    §2 (а) протухший кеш maxEnergy — запись `Coefficients[i]` через геттер
//       без `InvalidateCache` (в приложении таких мест НЕТ — перепись в журнале);
//    §3 (б) NaN и ±∞ как энергия; NaN как коэффициент (заслон F48);
//    §4 (в) плоские шкалы (maxEnergy == c0) и нулевой старший коэффициент;
//    §5 (г) немонотонная шкала порядков 3 и 5 (горб внутри [0, N]);
//    §6 (д) чужое N: шкала на 1024 канала, вызов с умолчанием 8192; N = 0;
//    §7 (е) касательно-плоские шкалы (тройной и пятикратный корень) — предел
//       итераций Брента и точность;
//    §8 двери входа степени > 4: XML-документ (`DocumentManager.CheckDocument`,
//       `OpenDocument` без окон), конфигурация прибора (без заслона).
//
//    CalibrationInverseProbeP78.exe [--tmp=<каталог для XML>]
//
//  Ожидание: «catchValid = 0», код 0. Код 1 — расхождение с ожиданием
//  (в том числе catchValid > 0 — тогда дефект доказан и строка чинится).
// ═══════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;
using BecquerelMonitor;

static class CalibrationInverseProbeP78
{
    static int checks;
    static int bad;
    static int solverCallsValid;   // сколько раз допустимые числа дошли до решателя
    static int catchValid;
    static int catchInvalid;
    static int outOfRange;
    static readonly List<string> catchLog = new List<string>();
    static readonly List<string> rangeLog = new List<string>();

    static MethodInfo ofFunction;
    static Assembly mathNet;

    static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
    static string I(int v) { return v.ToString(CultureInfo.InvariantCulture); }

    static void Check(bool ok, string what)
    {
        checks++;
        if (!ok)
        {
            bad++;
            Console.WriteLine("  НЕ СОШЛОСЬ: " + what);
        }
    }

    // ------------------------------------------------------------------
    // Многочлен и лямбда — ТЕ ЖЕ, что в EnrgToChannel приложения.
    // ------------------------------------------------------------------
    static double Poly(double[] c, int order, double x)
    {
        int top = Math.Min(order, c.Length - 1);
        double v = 0.0;
        for (int i = top; i >= 0; i--) v = v * x + c[i];
        return v;
    }

    static Func<double, double> AppLambda(double[] c, int order, double enrg)
    {
        if (order == 4) return x => c[4] * x * x * x * x + c[3] * x * x * x + c[2] * x * x + c[1] * x + c[0] - enrg;
        if (order == 3) return x => c[3] * x * x * x + c[2] * x * x + c[1] * x + c[0] - enrg;
        int top = Math.Min(order, c.Length - 1);
        return x =>
        {
            double v = 0.0;
            for (int i = top; i >= 0; i--) v = v * x + c[i];
            return v - enrg;
        };
    }

    static bool IsMonotone(double[] c, int order, int n)
    {
        double prev = Poly(c, order, 0);
        for (int i = 1; i <= n; i++)
        {
            double e = Poly(c, order, i);
            if (e < prev) return false;
            prev = e;
        }
        return true;
    }

    // Опорный канал монотонной шкалы: бисекция g(x) = P(x) − e на [0, N], 200 шагов.
    static double Bisect(double[] c, int order, int n, double e)
    {
        double lo = 0.0, hi = n;
        double glo = Poly(c, order, lo) - e;
        if (glo == 0) return 0.0;
        if (Poly(c, order, hi) - e <= 0) return hi;
        for (int i = 0; i < 200; i++)
        {
            double m = 0.5 * (lo + hi);
            double gm = Poly(c, order, m) - e;
            if (gm == 0) return m;
            if (Math.Sign(gm) == Math.Sign(glo)) { lo = m; glo = gm; } else hi = m;
        }
        return 0.5 * (lo + hi);
    }

    static Exception SolverThrows(Func<double, double> f, double lo, double hi, out double root)
    {
        root = double.NaN;
        try
        {
            root = (double)ofFunction.Invoke(null, new object[] { f, lo, hi, 1e-8, 100 });
            return null;
        }
        catch (TargetInvocationException e) { return e.InnerException ?? e; }
    }

    // ------------------------------------------------------------------
    // Сцена: шкала + её паспорт.
    // ------------------------------------------------------------------
    sealed class Scene
    {
        public string Name;
        public int Order;
        public int N;
        public double[] C;
        public PolynomialEnergyCalibration Cal;
        public bool Valid;      // CheckCalibration(N) приложения
        public bool Monotone;   // свой обход по каналам
        public bool NoChannelCheck; // §7: у плоской точки опорный канал сам ненадёжен — сверяется только энергия
        public double E0 { get { return C[0]; } }
        public double Emax { get { return Poly(C, Order, N); } }
    }

    static Scene Make(string name, int order, int n, double[] c)
    {
        Scene s = new Scene { Name = name, Order = order, N = n, C = c };
        s.Cal = new PolynomialEnergyCalibration { PolynomialOrder = order, Coefficients = (double[])c.Clone() };
        s.Valid = s.Cal.CheckCalibration(n);
        s.Monotone = IsMonotone(c, order, n);
        return s;
    }

    // Коэффициенты по безразмерной шкале u = x/N: E(u) = Σ k_i u^i  →  c_i = k_i / N^i
    static double[] FromU(int n, params double[] k)
    {
        double[] c = new double[k.Length];
        for (int i = 0; i < k.Length; i++) c[i] = k[i] / Math.Pow(n, i);
        return c;
    }

    static string Describe(Scene s)
    {
        return s.Name + " (порядок " + I(s.Order) + ", N=" + I(s.N) + ", CheckCalibration=" + (s.Valid ? "годна" : "ОТВЕРГНУТА")
            + ", монотонна=" + (s.Monotone ? "да" : "нет") + ", E0=" + F(s.E0) + ", Emax=" + F(s.Emax) + ")";
    }

    // ------------------------------------------------------------------
    // Один вызов: живой EnergyToChannel + тот же решатель отражением.
    // validNumber — считать ли отказ решателя доказательством дефекта.
    // ------------------------------------------------------------------
    static double ProbeEnergy(Scene s, double e, bool validNumber, string tag, double staleEmax = double.NaN)
    {
        double emax = double.IsNaN(staleEmax) ? s.Emax : staleEmax;   // §2: зажим по кешу, а не по настоящему краю
        string kind;
        double expect = double.NaN;
        if (e > emax) { kind = "зажим сверху"; expect = s.N; }
        else if (e < 0 || e < s.C[0]) { kind = "зажим снизу"; expect = 0; }
        else kind = "решатель";

        double r;
        try { r = s.Cal.EnergyToChannel(e, s.N); }
        catch (Exception ex)
        {
            Check(false, s.Name + " E=" + F(e) + ": EnergyToChannel бросил " + ex.GetType().Name + ": " + ex.Message);
            return double.NaN;
        }

        if (kind != "решатель")
        {
            if (validNumber) Check(r == expect, s.Name + " E=" + F(e) + " " + kind + ": ждали " + F(expect) + ", получили " + F(r) + " [" + tag + "]");
            return r;
        }

        if (validNumber) solverCallsValid++;
        if (s.Order >= 3)
        {
            double root;
            Exception solverEx = SolverThrows(AppLambda(s.C, s.Order, e), 0, s.N, out root);
            if (solverEx != null)
            {
                if (validNumber) catchValid++; else catchInvalid++;
                catchLog.Add((validNumber ? "ДОПУСТИМОЕ  " : "недопустимое ") + s.Name + " порядок " + I(s.Order) + " N=" + I(s.N)
                    + " E=" + F(e) + " → " + solverEx.GetType().Name + ": " + solverEx.Message
                    + "; EnergyToChannel вернул " + F(r) + " [" + tag + "]");
                // Приложение на исключении отвечает нулём — читатель обязан видеть именно его.
                Check(r == 0, s.Name + " E=" + F(e) + ": решатель бросил, а EnergyToChannel вернул " + F(r) + ", не 0 [" + tag + "]");
                return r;
            }
            // Решатель не бросал: живой ответ обязан совпасть с ответом того же решателя.
            if (validNumber) Check(Math.Abs(r - root) <= 1e-6 * Math.Max(1.0, Math.Abs(root)),
                s.Name + " E=" + F(e) + ": живой ответ " + F(r) + " ≠ решатель отражением " + F(root) + " [" + tag + "]");
        }

        if (double.IsNaN(r) || r < 0 || r > s.N * (1.0 + 1e-12))
        {
            outOfRange++;
            rangeLog.Add(s.Name + " порядок " + I(s.Order) + " N=" + I(s.N) + " E=" + F(e) + " → канал " + F(r) + " [" + tag + "]");
            if (validNumber) Check(false, s.Name + " E=" + F(e) + ": канал вне [0, N]: " + F(r) + " [" + tag + "]");
            return r;
        }
        if (validNumber)
        {
            double expected = Bisect(s.C, s.Order, s.N, e);
            if (!s.NoChannelCheck)
                Check(Math.Abs(r - expected) <= 1e-6 * Math.Max(1.0, Math.Abs(expected)),
                    s.Name + " E=" + F(e) + ": канал " + F(r) + " против опорного " + F(expected) + " (бисекция) [" + tag + "]");
            Check(!(r == 0 && expected > 1e-3), s.Name + " E=" + F(e) + ": НУЛЕВОЙ канал при честном " + F(expected) + " [" + tag + "]");
            double back = Poly(s.C, s.Order, r);
            double tol = 1e-9 * Math.Max(1.0, Math.Abs(e)) + 1e-6;
            Check(Math.Abs(back - e) <= tol, s.Name + " E=" + F(e) + ": обратный ход дал " + F(back) + " (канал " + F(r) + ", |Δ|=" + F(Math.Abs(back - e)) + ") [" + tag + "]");
        }
        return r;
    }

    static void Sweep(Scene s, bool validNumber, string tag)
    {
        double e0 = s.E0, emax = s.Emax;
        int steps = 1200;
        double lo = e0 - 5.0, hi = emax + 5.0;
        for (int i = 0; i <= steps; i++)
        {
            double e = lo + (hi - lo) * i / steps;
            ProbeEnergy(s, e, validNumber, tag + " сетка");
        }
        double[] specials = {
            e0, emax, e0 + 1e-12, emax - 1e-12, e0 * (1.0 + 2.220446049250313e-16), 0.0, 1e-300, double.Epsilon,
            1e9, -1e9, double.MaxValue, -double.MaxValue, double.PositiveInfinity, double.NegativeInfinity,
            (e0 + emax) / 2.0, 511.0, 661.657, 1460.82, 2614.511
        };
        foreach (double e in specials) ProbeEnergy(s, e, validNumber, tag + " особая");
    }

    // ------------------------------------------------------------------
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string tmp = null;
        foreach (string a in args) if (a.StartsWith("--tmp=", StringComparison.Ordinal)) tmp = a.Substring(6);
        if (tmp == null) tmp = Path.Combine(Path.GetTempPath(), "CalibrationInverseProbeP78");
        Directory.CreateDirectory(tmp);

        // ---------------- §0 среда ----------------
        Console.WriteLine("=== §0 СРЕДА ===");
        Console.WriteLine("  приложение: " + typeof(PolynomialEnergyCalibration).Assembly.Location);
        Console.WriteLine("  собрано:    " + File.GetLastWriteTime(typeof(PolynomialEnergyCalibration).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Console.WriteLine("  окна:       " + AppUi.HasWindows);
        AssemblyName mn = typeof(PolynomialEnergyCalibration).Assembly.GetReferencedAssemblies().FirstOrDefault(a => a.Name == "MathNet.Numerics");
        if (mn == null) { Console.WriteLine("  НЕТ ссылки на MathNet.Numerics у приложения"); return 2; }
        mathNet = Assembly.Load(mn);
        Type fr = mathNet.GetType("MathNet.Numerics.FindRoots");
        ofFunction = fr == null ? null : fr.GetMethod("OfFunction", new[] { typeof(Func<double, double>), typeof(double), typeof(double), typeof(double), typeof(int) });
        if (ofFunction == null) { Console.WriteLine("  НЕТ FindRoots.OfFunction(Func,double,double,double,int)"); return 2; }
        Console.WriteLine("  MathNet:    " + mathNet.GetName().Version + "  " + mathNet.Location);
        double r0;
        Exception ex0 = SolverThrows(x => x - 10.0, 0, 5, out r0);
        Console.WriteLine("  решатель на x−10=0 при отрезке [0,5]: " + (ex0 == null ? "корень " + F(r0) + " — отрезок РАСШИРЯЕТСЯ (ExpandReduce)" : "бросил " + ex0.GetType().Name));
        Check(ex0 == null && Math.Abs(r0 - 10.0) < 1e-6, "§0: решатель не расширяет отрезок — посылка о ExpandReduce неверна");
        double r1;
        Exception ex1 = SolverThrows(x => x * x + 1.0, 0, 5, out r1);
        Console.WriteLine("  решатель на x²+1=0 при [0,5]: " + (ex1 == null ? "корень " + F(r1) : ex1.GetType().Name + ": " + ex1.Message));
        Check(ex1 != null, "§0: решатель не бросил на уравнении без корней — читатель отказа мёртв");
        double r2;
        Exception ex2 = SolverThrows(x => double.NaN, 0, 5, out r2);
        Console.WriteLine("  решатель на f≡NaN при [0,5]: " + (ex2 == null ? "корень " + F(r2) : ex2.GetType().Name + ": " + ex2.Message));
        Check(ex2 != null, "§0: решатель не бросил на NaN");
        Console.WriteLine();

        // ---------------- §1 допустимые шкалы ----------------
        Console.WriteLine("=== §1 ДОПУСТИМЫЕ ШКАЛЫ: порядки 1…6, N ∈ {1024, 4096, 8192, 16384} ===");
        List<Scene> baseline = new List<Scene>();
        foreach (int n in new[] { 1024, 4096, 8192, 16384 })
        {
            double s8 = 8192.0 / n;   // масштаб канала к сцене AS1Pro корпуса (8192 каналов)
            baseline.Add(Make("линейная", 1, n, new[] { -3.0, 3000.0 / n }));
            baseline.Add(Make("NaI-квадрат (AS1Pro корпуса)", 2, n, new[] { -9.534038239227314, 0.38946005205837325 * s8, 3.952494338338914e-06 * s8 * s8 }));
            baseline.Add(Make("NaI-куб", 3, n, new[] { -9.5, 0.3895 * s8, 3.95e-6 * s8 * s8, -1e-11 * Math.Pow(s8, 3) }));
            baseline.Add(Make("NaI-4", 4, n, new[] { -9.5, 0.3895 * s8, 3.95e-6 * s8 * s8, -1e-11 * Math.Pow(s8, 3), 2e-16 * Math.Pow(s8, 4) }));
            baseline.Add(Make("NaI-5", 5, n, new[] { -9.5, 0.3895 * s8, 3.95e-6 * s8 * s8, -1e-11 * Math.Pow(s8, 3), 2e-16 * Math.Pow(s8, 4), -3e-20 * Math.Pow(s8, 5) }));
            baseline.Add(Make("изогнутая-5 (SpectraLine-подобная)", 5, n, FromU(n, 2.0, 3000.0, 0.0, 450.0, 0.0, -300.0)));
            baseline.Add(Make("изогнутая-6", 6, n, FromU(n, 2.0, 3000.0, 0.0, 450.0, 0.0, -300.0, 40.0)));
            baseline.Add(Make("HPGe-подобная", 3, n, new[] { -0.5, 3277.0 / n, 1e-9 * s8 * s8, -1e-14 * Math.Pow(s8, 3) }));
            baseline.Add(Make("HPGe-5", 5, n, new[] { -0.5, 3277.0 / n, 1e-9 * s8 * s8, -1e-14 * Math.Pow(s8, 3), 1e-18 * Math.Pow(s8, 4), -1e-23 * Math.Pow(s8, 5) }));
        }
        foreach (Scene s in baseline)
        {
            Console.WriteLine("  " + Describe(s));
            Check(s.Monotone, "§1: сцена " + s.Name + " немонотонна — сцена построена неверно");
            Check(s.Valid == (s.Order <= 4), "§1: CheckCalibration у " + s.Name + " порядка " + I(s.Order) + " = " + s.Valid + " (ждали " + (s.Order <= 4) + ")");
            Sweep(s, true, "§1 " + s.Name + "/N=" + I(s.N));
        }
        Console.WriteLine("  вызовов решателя на допустимых числах после §1: " + I(solverCallsValid) + ", отказов: " + I(catchValid));
        Console.WriteLine();

        // ---------------- §2 (а) протухший кеш ----------------
        Console.WriteLine("=== §2 (а) ПРОТУХШИЙ КЕШ maxEnergy: запись Coefficients[i] через геттер без InvalidateCache ===");
        {
            int n = 8192;
            // Порядок 4, c4 < 0: монотонна на [0, N] (Emax=3300), глобальный максимум ≈ 4037 при u≈1.415.
            Scene hump4 = Make("горб-4", 4, n, FromU(n, 2.0, 3000.0, 0.0, 1200.0, -900.0));
            Console.WriteLine("  " + Describe(hump4));
            Check(hump4.Valid && hump4.Monotone, "§2: горб-4 обязан быть годной шкалой на [0, N]");
            double warm = hump4.Cal.EnergyToChannel(1000.0, n);   // кеш посчитан: maxEnergy = 3300
            Console.WriteLine("  прогрев: E=1000 → канал " + F(warm));
            double[] live = hump4.Cal.Coefficients;                 // тот же массив, что внутри
            for (int i = 1; i < live.Length; i++) live[i] *= 0.5;   // новая шкала: Emax=1651, глобальный максимум ≈ 2020
            for (int i = 1; i < hump4.C.Length; i++) hump4.C[i] *= 0.5;
            Console.WriteLine("  после правки через геттер: настоящий Emax=" + F(hump4.Emax) + ", в кеше остался 3302");
            int before = catchInvalid;
            foreach (double e in new[] { 2500.0, 3000.0, 3299.0 })
            {
                double r = ProbeEnergy(hump4, e, false, "§2 горб-4 протухший кеш", 3302.0);
                Console.WriteLine("    E=" + F(e) + " → канал " + F(r) + (r == 0 ? "  ← НОЛЬ через catch" : ""));
            }
            Check(catchInvalid > before, "§2: протухший кеш на чётной степени с c4<0 НЕ довёл до catch — ожидание не сошлось");
            // Контроль: та же правка через СЕТТЕР — кеш сброшен, зажим сверху честный.
            Scene ctrl = Make("горб-4 (сеттер)", 4, n, FromU(n, 2.0, 3000.0, 0.0, 1200.0, -900.0));
            ctrl.Cal.EnergyToChannel(1000.0, n);
            double[] fresh = (double[])ctrl.Cal.Coefficients.Clone();
            for (int i = 1; i < fresh.Length; i++) fresh[i] *= 0.5;
            ctrl.Cal.Coefficients = fresh;
            for (int i = 1; i < ctrl.C.Length; i++) ctrl.C[i] *= 0.5;
            double rc = ProbeEnergy(ctrl, 2500.0, true, "§2 контроль сеттер");
            Console.WriteLine("  контроль (сеттер): E=2500 → канал " + F(rc) + " (ждали N=" + I(n) + ")");
            Check(rc == n, "§2: сеттер сбрасывает кеш — ждали зажим к N, получили " + F(rc));

            // Нечётная степень 5: протухший кеш даёт не ноль, а корень ЗА шкалой.
            Scene hump5 = Make("горб-5", 5, n, FromU(n, 2.0, 3000.0, 0.0, 1200.0, -900.0, 100.0));
            Console.WriteLine("  " + Describe(hump5));
            hump5.Cal.EnergyToChannel(1000.0, n);
            double[] live5 = hump5.Cal.Coefficients;
            for (int i = 1; i < live5.Length; i++) live5[i] *= 0.5;
            for (int i = 1; i < hump5.C.Length; i++) hump5.C[i] *= 0.5;
            foreach (double e in new[] { 2500.0, 3000.0 })
            {
                double r = ProbeEnergy(hump5, e, false, "§2 горб-5 протухший кеш", 3402.0);
                Console.WriteLine("    E=" + F(e) + " → канал " + F(r) + (r > n ? "  ← ЗА ШКАЛОЙ (Expand нашёл корень вне [0, N])" : ""));
            }
            Console.WriteLine("  ⚠ В приложении все 7 мест записи Coefficients[i] либо создают свежий объект, либо зовут");
            Console.WriteLine("    Coefficients = new double[] (сеттер, dirty), либо InvalidateCache — перепись в журнале полосы.");
        }
        Console.WriteLine();

        // ---------------- §3 (б) NaN и бесконечности ----------------
        Console.WriteLine("=== §3 (б) NaN / ±∞ КАК ЭНЕРГИЯ, NaN КАК КОЭФФИЦИЕНТ ===");
        {
            int n = 8192;
            double s8 = 1.0;
            Scene[] byOrder = {
                Make("линейная", 1, n, new[] { -3.0, 3000.0 / n }),
                Make("NaI-квадрат", 2, n, new[] { -9.534038239227314, 0.38946005205837325, 3.952494338338914e-06 }),
                Make("NaI-куб", 3, n, new[] { -9.5, 0.3895 * s8, 3.95e-6, -1e-11 }),
                Make("NaI-4", 4, n, new[] { -9.5, 0.3895, 3.95e-6, -1e-11, 2e-16 }),
                Make("изогнутая-5", 5, n, FromU(n, 2.0, 3000.0, 0.0, 450.0, 0.0, -300.0)),
                Make("изогнутая-6", 6, n, FromU(n, 2.0, 3000.0, 0.0, 450.0, 0.0, -300.0, 40.0)),
            };
            foreach (Scene s in byOrder)
            {
                int before = catchInvalid;
                double rn = ProbeEnergy(s, double.NaN, false, "§3 NaN");
                double rp = ProbeEnergy(s, double.PositiveInfinity, true, "§3 +∞");
                double rm = ProbeEnergy(s, double.NegativeInfinity, true, "§3 −∞");
                bool viaCatch = catchInvalid > before;
                Console.WriteLine("  порядок " + I(s.Order) + ": E=NaN → " + F(rn) + (viaCatch ? " (через catch: решатель бросил)" : (rn == 0 ? " (ноль БЕЗ решателя)" : ""))
                    + "; E=+∞ → " + F(rp) + "; E=−∞ → " + F(rm));
                if (s.Order >= 3) Check(viaCatch && rn == 0, "§3: порядок " + I(s.Order) + " на NaN ждали catch→0, получили " + F(rn) + ", catch=" + viaCatch);
                if (s.Order == 2) Check(rn == 0 && !viaCatch, "§3: порядок 2 на NaN ждали ноль без решателя, получили " + F(rn));
                if (s.Order == 1) Check(double.IsNaN(rn), "§3: порядок 1 на NaN ждали NaN, получили " + F(rn));
            }
            PolynomialEnergyCalibration nanCoef = new PolynomialEnergyCalibration { PolynomialOrder = 5, Coefficients = new[] { 2.0, 0.366, 0.0, 6.5e-9, 0.0, double.NaN } };
            bool nanValid = nanCoef.CheckCalibration(n);
            Console.WriteLine("  NaN в коэффициенте порядка 5: CheckCalibration = " + nanValid + " (заслон F48)");
            Check(!nanValid, "§3: NaN-коэффициент прошёл CheckCalibration");
        }
        Console.WriteLine();

        // ---------------- §4 (в) плоские шкалы ----------------
        Console.WriteLine("=== §4 (в) ПЛОСКИЕ ШКАЛЫ И НУЛЕВОЙ СТАРШИЙ КОЭФФИЦИЕНТ ===");
        {
            int n = 8192;
            Scene flat5 = Make("плоская-5 (c1..c5 = 0)", 5, n, new[] { 100.0, 0.0, 0.0, 0.0, 0.0, 0.0 });
            Console.WriteLine("  " + Describe(flat5));
            Check(!flat5.Valid, "§4: плоская шкала прошла CheckCalibration");
            foreach (double e in new[] { 50.0, 100.0, 150.0 })
            {
                int before = catchInvalid;
                double r = ProbeEnergy(flat5, e, false, "§4 плоская-5");
                Console.WriteLine("    E=" + F(e) + " → канал " + F(r) + (catchInvalid > before ? "  ← через catch (f≡0 на обоих концах)" : ""));
            }
            Scene flat3 = Make("плоская-3 (c1..c3 = 0)", 3, n, new[] { 100.0, 0.0, 0.0, 0.0 });
            Check(!flat3.Valid, "§4: плоская шкала порядка 3 прошла CheckCalibration");
            {
                int before = catchInvalid;
                double r = ProbeEnergy(flat3, 100.0, false, "§4 плоская-3");
                Console.WriteLine("  плоская-3: E=100 → канал " + F(r) + (catchInvalid > before ? "  ← через catch" : ""));
            }
            // Нулевой старший коэффициент при годных младших — так пишет файл, где 5-й коэффициент = 0.
            Scene zeroTop5 = Make("порядок 5 с c5 = 0", 5, n, new[] { -9.5, 0.3895, 3.95e-6, -1e-11, 2e-16, 0.0 });
            Console.WriteLine("  " + Describe(zeroTop5));
            Check(!zeroTop5.Valid, "§4: порядок 5 прошёл CheckCalibration");
            Sweep(zeroTop5, true, "§4 c5=0");
            Scene zeroTop3 = Make("порядок 3 с c3 = 0", 3, n, new[] { -9.5, 0.3895, 3.95e-6, 0.0 });
            Console.WriteLine("  " + Describe(zeroTop3));
            Check(!zeroTop3.Valid, "§4: порядок 3 с c3=0 прошёл CheckCalibration (заслон Coefficients[3]==0)");
            Sweep(zeroTop3, true, "§4 c3=0");
        }
        Console.WriteLine();

        // ---------------- §5 (г) немонотонные ----------------
        Console.WriteLine("=== §5 (г) НЕМОНОТОННАЯ ШКАЛА (горб внутри [0, N]) ===");
        {
            int n = 8192;
            // E(u) = 3000(u − 4u² + 4u³): убывает на (1/6, 1/2); E(1/6)=222, E(1/2)=0, E(1)=3000.
            Scene nm3 = Make("горб-внутри-3", 3, n, FromU(n, 5.0, 3000.0, -12000.0, 12000.0));
            Scene nm5 = Make("горб-внутри-5", 5, n, FromU(n, 5.0, 3000.0, -12000.0, 12000.0, 0.0, 30.0));
            foreach (Scene s in new[] { nm3, nm5 })
            {
                Console.WriteLine("  " + Describe(s));
                Check(!s.Valid && !s.Monotone, "§5: немонотонная шкала прошла CheckCalibration или обход");
                int before = catchInvalid;
                int multi = 0, notFirst = 0;
                for (int i = 0; i <= 600; i++)
                {
                    double e = s.E0 - 5.0 + (s.Emax + 10.0 - s.E0) * i / 600.0;
                    double r = ProbeEnergy(s, e, false, "§5 " + s.Name);
                    if (e >= s.E0 && e <= s.Emax && !double.IsNaN(r))
                    {
                        // первый корень слева — обходом по каналам
                        double first = double.NaN;
                        double prev = Poly(s.C, s.Order, 0) - e;
                        for (int ch = 1; ch <= n; ch++)
                        {
                            double cur = Poly(s.C, s.Order, ch) - e;
                            if (Math.Sign(prev) != Math.Sign(cur) || cur == 0) { first = ch; break; }
                            prev = cur;
                        }
                        int roots = 0;
                        prev = Poly(s.C, s.Order, 0) - e;
                        for (int ch = 1; ch <= n; ch++)
                        {
                            double cur = Poly(s.C, s.Order, ch) - e;
                            if (Math.Sign(prev) != Math.Sign(cur)) roots++;
                            prev = cur;
                        }
                        if (roots > 1) { multi++; if (Math.Abs(r - first) > 1.0) notFirst++; }
                    }
                }
                Console.WriteLine("    отказов решателя: " + I(catchInvalid - before) + "; энергий с несколькими корнями: " + I(multi)
                    + ", из них отдан НЕ первый слева: " + I(notFirst));
            }
        }
        Console.WriteLine();

        // ---------------- §6 (д) чужое N ----------------
        Console.WriteLine("=== §6 (д) ЧУЖОЕ ЧИСЛО КАНАЛОВ: шкала на 1024, вызов с умолчанием 8192; N = 0 ===");
        {
            // Порядок 4, монотонна на [0,1024]: E(1)=4044, E(8)=1000 (за шкалой многочлен падает).
            double[] c = FromU(1024, 2.0, 3000.0, 0.0, 1200.0, -(24000.0 + 614400.0 - 998.0) / 4096.0);
            Scene on1024 = Make("шкала-1024", 4, 1024, c);
            Console.WriteLine("  " + Describe(on1024));
            Check(on1024.Valid && on1024.Monotone, "§6: шкала-1024 обязана быть годной на 1024");
            Scene as8192 = Make("шкала-1024, вызвана с 8192", 4, 8192, c);
            Console.WriteLine("  " + Describe(as8192));
            int before = catchInvalid;
            int wrongBranch = 0, clampedAll = 0, total = 0;
            for (int i = 0; i <= 400; i++)
            {
                double e = on1024.E0 + (on1024.Emax - on1024.E0) * i / 400.0;   // энергии, честно лежащие на 1024-канальной шкале
                double r = ProbeEnergy(as8192, e, false, "§6 чужое N");
                total++;
                if (r == 8192) clampedAll++;
                else if (r > 1024) wrongBranch++;
            }
            Console.WriteLine("    из " + I(total) + " энергий шкалы 1024: отказов решателя " + I(catchInvalid - before)
                + ", зажато к 8192: " + I(clampedAll) + ", корень на падающей ветви (>1024): " + I(wrongBranch));
            Check(catchInvalid == before, "§6: чужое N довело до catch — ожидание (нет) не сошлось");
            // N = 0
            PolynomialEnergyCalibration z = new PolynomialEnergyCalibration { PolynomialOrder = 5, Coefficients = FromU(8192, 2.0, 3000.0, 0.0, 450.0, 0.0, -300.0) };
            double z1 = z.EnergyToChannel(1000.0, 0), z2 = z.EnergyToChannel(2.0, 0), z3 = z.EnergyToChannel(1.0, 0);
            Console.WriteLine("    N=0: E=1000 → " + F(z1) + ", E=c0=2 → " + F(z2) + ", E=1 → " + F(z3) + " (все нули: зажим к maxChannels=0 или catch на [0,0] — неразличимо и бессмысленно)");
        }
        Console.WriteLine();

        // ---------------- §7 (е) касательно-плоские ----------------
        Console.WriteLine("=== §7 (е) КАСАТЕЛЬНО-ПЛОСКИЕ ШКАЛЫ: тройной и пятикратный корень ===");
        {
            foreach (int n in new[] { 1024, 8192, 16384 })
            {
                // E(u) = 3000(u − 3u² + 3u³): E' = 3000(3u−1)² ≥ 0, плоско при u = 1/3, E(1/3) = 333.33…
                Scene t3 = Make("тройной-корень-3", 3, n, FromU(n, 0.0, 3000.0, -9000.0, 9000.0));
                // E(u) = 48000((u−0.5)^5 + 0.03125): плоско при u = 0.5, E(0.5) = 1500
                Scene t5 = Make("пятикратный-корень-5", 5, n, FromU(n, 0.0, 48000.0 * 0.3125, -48000.0 * 1.25, 48000.0 * 2.5, -48000.0 * 2.5, 48000.0));
                foreach (Scene s in new[] { t3, t5 })
                {
                    s.NoChannelCheck = true;
                    Console.WriteLine("  " + Describe(s) + (s.Monotone ? "" : "  [обход по каналам видит шум округления у плоской точки — не дефект шкалы]"));
                    if (s.Order == 3) Check(s.Valid, "§7: тройной корень порядка 3 отвергнут CheckCalibration (равенство соседних энергий допустимо)");
                    int before = catchValid;
                    Sweep(s, true, "§7 " + s.Name + "/N=" + I(s.N));
                    double flatU = s.Order == 3 ? 1.0 / 3.0 : 0.5;
                    double eFlat = Poly(s.C, s.Order, flatU * n);
                    double worst = 0.0;
                    foreach (double d in new[] { 0.0, 1e-9, -1e-9, 1e-6, -1e-6, 1e-3, -1e-3, 0.1, -0.1 })
                    {
                        double r = ProbeEnergy(s, eFlat + d, true, "§7 у плоской точки");
                        if (!double.IsNaN(r)) worst = Math.Max(worst, Math.Abs(r - Bisect(s.C, s.Order, n, eFlat + d)));
                    }
                    Console.WriteLine("    отказов решателя: " + I(catchValid - before) + "; наибольшее расхождение с опорным каналом у плоской точки: " + F(worst) + " кан. (критерий Брента |f|<1e-8 кэВ, шкала там плоская)");
                }
            }
        }
        Console.WriteLine();

        // ---------------- §8 двери входа ----------------
        Console.WriteLine("=== §8 ДВЕРИ ВХОДА СТЕПЕНИ > 4 ===");
        {
            int n = 1024;
            double[] c5 = FromU(n, 2.0, 3000.0, 0.0, 450.0, 0.0, -300.0);
            string coefXml = string.Join("", c5.Select(cv => "<Coefficient>" + F(cv) + "</Coefficient>"));

            // (1) Фрагмент калибровки — как он лежит в документе.
            string calXml = "<PolynomialEnergyCalibration><PolynomialOrder>5</PolynomialOrder><Coefficients>" + coefXml + "</Coefficients></PolynomialEnergyCalibration>";
            PolynomialEnergyCalibration fromXml;
            using (StringReader sr = new StringReader(calXml))
                fromXml = (PolynomialEnergyCalibration)new XmlSerializer(typeof(PolynomialEnergyCalibration)).Deserialize(sr);
            bool v = fromXml.CheckCalibration(n);
            Console.WriteLine("  (1) XML-фрагмент калибровки порядка 5: PolynomialOrder=" + I(fromXml.PolynomialOrder) + ", коэффициентов " + I(fromXml.Coefficients.Length)
                + ", CheckCalibration(" + I(n) + ") = " + v);
            Check(fromXml.PolynomialOrder == 5 && fromXml.Coefficients.Length == 6 && !v, "§8(1): степень 5 из XML прошла CheckCalibration или не дочиталась");
            double ch5 = fromXml.EnergyToChannel(1000.0, n);
            Console.WriteLine("      и при этом EnergyToChannel(1000) на ней РАБОТАЕТ: канал " + F(ch5) + " (обратно " + F(Poly(c5, 5, ch5)) + " кэВ)");
            Check(Math.Abs(Poly(c5, 5, ch5) - 1000.0) < 1e-6, "§8(1): обратный ход степени 5 из XML не сошёлся");

            // (2) Документ целиком — через настоящий DocumentManager.CheckDocument (private, отражением).
            StringBuilder doc = new StringBuilder();
            doc.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><ResultDataFile><FormatVersion>120920</FormatVersion><ResultDataList><ResultData>");
            doc.Append("<EnergySpectrum><NumberOfChannels>" + I(n) + "</NumberOfChannels><ChannelPitch>1</ChannelPitch>");
            doc.Append("<EnergyCalibration><PolynomialOrder>5</PolynomialOrder><Coefficients>" + coefXml + "</Coefficients></EnergyCalibration>");
            doc.Append("<ValidPulseCount>" + I(n) + "</ValidPulseCount><TotalPulseCount>" + I(n) + "</TotalPulseCount><MeasurementTime>100</MeasurementTime><NumberOfSamples>0</NumberOfSamples><Spectrum>");
            for (int i = 0; i < n; i++) doc.Append("<DataPoint>1</DataPoint>");
            doc.Append("</Spectrum></EnergySpectrum><Visible>true</Visible></ResultData></ResultDataList></ResultDataFile>");
            string docPath = Path.Combine(tmp, "order5.xml");
            File.WriteAllText(docPath, doc.ToString(), new UTF8Encoding(false));
            ResultDataFile rdf;
            using (FileStream fs = new FileStream(docPath, FileMode.Open))
                rdf = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
            PolynomialEnergyCalibration inDoc = (PolynomialEnergyCalibration)rdf.ResultDataList[0].EnergySpectrum.EnergyCalibration;
            Console.WriteLine("  (2) документ order5.xml: в спектре порядок " + I(inDoc.PolynomialOrder) + ", коэффициентов " + I(inDoc.Coefficients.Length));
            MethodInfo checkDoc = typeof(DocumentManager).GetMethod("CheckDocument", BindingFlags.NonPublic | BindingFlags.Instance);
            Check(checkDoc != null, "§8(2): нет DocumentManager.CheckDocument(ResultDataFile, bool)");
            if (checkDoc != null)
            {
                ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
                ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
                DocumentManager dm = DocumentManager.GetInstance();
                bool ok = (bool)checkDoc.Invoke(dm, new object[] { rdf, false });
                Console.WriteLine("      CheckDocument(doCorrections:false) = " + ok + " — дверь документа степень 5 ОТКАЗЫВАЕТ");
                Check(!ok, "§8(2): CheckDocument принял документ со степенью 5");
                bool ok2 = (bool)checkDoc.Invoke(dm, new object[] { rdf, true });
                PolynomialEnergyCalibration after = (PolynomialEnergyCalibration)rdf.ResultDataList[0].EnergySpectrum.EnergyCalibration;
                Console.WriteLine("      CheckDocument(doCorrections:true) = " + ok2 + "; шкала после «Да» на вопрос о сбросе: порядок " + I(after.PolynomialOrder)
                    + ", коэффициенты [" + string.Join(", ", after.Coefficients.Select(F)) + "] — то есть " + (after.PolynomialOrder == 1 && after.Coefficients[1] == 1.0 && after.Coefficients[0] == 0.0 ? "заготовка y = x" : "понижена"));
                Check(after.PolynomialOrder <= 4, "§8(2): после исправления в документе осталась степень " + I(after.PolynomialOrder));
                // OpenDocument без окон: вопрос «сбросить калибровку?» задать некому — ожидаем исключение AskYesNo.
                string openMsg;
                try
                {
                    DocEnergySpectrum opened = dm.OpenDocument(docPath);
                    openMsg = opened == null ? "вернул null" : "ОТКРЫЛ документ, степень " + I(((PolynomialEnergyCalibration)opened.ActiveResultData.EnergySpectrum.EnergyCalibration).PolynomialOrder);
                }
                catch (Exception ex) { openMsg = ex.GetType().Name + ": " + ex.Message; }
                Console.WriteLine("      OpenDocument(order5.xml) без окон: " + openMsg);
                Check(openMsg.Contains("без окон некому") || openMsg.Contains("вернул null"), "§8(2): OpenDocument без окон не остановился на вопросе о сбросе: " + openMsg);
            }

            // (3) Конфигурация прибора — заслона при загрузке нет (DeviceConfigManager.LoadAllConfigFiles).
            string devXml = "<?xml version=\"1.0\"?><DeviceConfigInfo><FormatVersion>120920</FormatVersion><Guid>00000000-0000-4000-8000-000000007800</Guid><Name>p78 order 5</Name>"
                + "<NumberOfChannels>" + I(n) + "</NumberOfChannels><DeviceType>AtomSpectraVCP</DeviceType><EnergyCalibrationType>Polynomial</EnergyCalibrationType>"
                + "<PolynomialEnergyCalibration><PolynomialOrder>5</PolynomialOrder><Coefficients>" + coefXml + "</Coefficients></PolynomialEnergyCalibration></DeviceConfigInfo>";
            DeviceConfigInfo dev;
            using (StringReader sr = new StringReader(devXml))
                dev = (DeviceConfigInfo)new XmlSerializer(typeof(DeviceConfigInfo)).Deserialize(sr);
            PolynomialEnergyCalibration devCal = dev.EnergyCalibration as PolynomialEnergyCalibration;
            Console.WriteLine("  (3) конфигурация прибора из XML: порядок " + (devCal == null ? "нет" : I(devCal.PolynomialOrder)) + ", каналов " + I(dev.NumberOfChannels)
                + " — читается БЕЗ CheckCalibration (LoadAllConfigFiles), и клонируется в новое измерение (DCControlPanel.cs:870/1009, DocEnergySpectrum.cs:488)");
            Check(devCal != null && devCal.PolynomialOrder == 5, "§8(3): степень 5 не дочиталась из конфигурации прибора");
            if (devCal != null)
            {
                Scene devScene = new Scene { Name = "конфиг прибора порядка 5", Order = 5, N = n, C = c5, Cal = devCal };
                devScene.Valid = devCal.CheckCalibration(n);
                devScene.Monotone = IsMonotone(c5, 5, n);
                Console.WriteLine("      " + Describe(devScene));
                int before = catchValid;
                Sweep(devScene, true, "§8(3) конфиг прибора");
                Console.WriteLine("      отказов решателя на этой шкале: " + I(catchValid - before) + " — ветка > 4 на ней РАБОТАЕТ");
            }
            Console.WriteLine("  (4) N42 (Util.cs:715, :1255): степень > 4 → исключение ERRUnsupportedPolynomialOrderN42; границы каналов → подгонка 4-й степени (:1530).");
            Console.WriteLine("  (5) Atom Spectra текст (DocumentManager.cs:1747): степень > 4 → исключение ERRUnsupportedCalibrationOrder.");
            Console.WriteLine("  (6) SpecUtils (DocumentManager.cs:1233): cal_size > 5 → приближение 4-й степенью (A216); cal_size ≤ 5 → степень ≤ 4.");
            Console.WriteLine("  (7) Подгонка по пикам (CalibrationSolver.Solve, DCEnergyCalibrationView, DeviceConfigForm): степень ≤ 4 по числу полей формы.");
        }
        Console.WriteLine();

        // ---------------- ИТОГ ----------------
        Console.WriteLine("=== ОТКАЗЫ РЕШАТЕЛЯ (все) ===");
        foreach (string l in catchLog) Console.WriteLine("  " + l);
        if (rangeLog.Count > 0)
        {
            Console.WriteLine("=== ОТВЕТЫ ВНЕ [0, N] ===");
            foreach (string l in rangeLog) Console.WriteLine("  " + l);
        }
        Console.WriteLine();
        Console.WriteLine("ИТОГ\tпроверок=" + I(checks) + "\tне сошлось=" + I(bad)
            + "\tвызовов решателя на допустимых числах=" + I(solverCallsValid)
            + "\tотказов решателя на ДОПУСТИМЫХ=" + I(catchValid)
            + "\tна недопустимых (контроль читателя)=" + I(catchInvalid)
            + "\tответов вне шкалы=" + I(outOfRange));
        Console.WriteLine(catchValid == 0
            ? "ВЕРДИКТ\tотказ решателя на допустимых числах НЕ ДОСТИГНУТ: catch { return 0; } на них мёртв; достигается только NaN-энергией и протухшим кешем"
            : "ВЕРДИКТ\tотказ решателя на допустимых числах ДОСТИГНУТ — дефект доказан, см. список выше");
        return bad == 0 ? 0 : 1;
    }
}
