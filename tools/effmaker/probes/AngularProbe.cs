using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Globalization;
using System.Text;

namespace AngularProbe
{
    /// <summary>
    /// Поверка угловых γ-γ корреляций (N5): коэффициенты A₂₂ и A₄₄ против
    /// справочных значений и против схем уровней в базе.
    ///
    ///     angularprobe [--nuclide=28:60] [--pair=1173.2:1332.5]
    ///
    /// **Раздел 1 — учебные каскады.** Значения A₂₂ и A₄₄ для чистых
    /// переходов табличны и не зависят ни от чего, кроме спинов: это
    /// единственная проверка, которая ловит ошибку в символах Вигнера, в
    /// знаке фазы и в порядке спинов сразу. Классика — Co-60 (4→2→0, оба
    /// E2): 0.1020 и 0.0091; каскад 0→2→0: 0.3571 и 1.1429.
    ///
    /// **Раздел 2 — символы Вигнера порознь**, на значениях, которые
    /// считаются в уме: 3j и 6j с нулями и единицами.
    ///
    /// **Раздел 3 — схема из базы**: берётся нуклид и пара линий, находятся
    /// переходы, проверяется, что они каскад (конец первого = начало
    /// второго), и печатаются коэффициенты со спинами и мультипольностями,
    /// по которым они получены.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            int z = 28, a = 60;
            double e1 = 1173.239, e2 = 1332.514;
            foreach (string s in args)
            {
                if (s.StartsWith("--nuclide=", StringComparison.Ordinal))
                {
                    string[] parts = s.Substring(10).Split(':');
                    z = int.Parse(parts[0], CultureInfo.InvariantCulture);
                    a = int.Parse(parts[1], CultureInfo.InvariantCulture);
                }
                else if (s.StartsWith("--pair=", StringComparison.Ordinal))
                {
                    string[] parts = s.Substring(7).Split(':');
                    e1 = double.Parse(parts[0], CultureInfo.InvariantCulture);
                    e2 = double.Parse(parts[1], CultureInfo.InvariantCulture);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + s);
                    return 2;
                }
            }

            int bad = 0;
            bad += Textbook();
            bad += Wigner();
            FromDatabase(z, a, e1, e2);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "все сверки сошлись" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // E2 = 4, M1 = 3, E1 = 2, E3 = 6 в кодировке Geant4; смесь M1+E2 = 304.
        const int E1 = 2, M1 = 3, E2 = 4, E3 = 6;

        static int Textbook()
        {
            Console.WriteLine("1. Учебные каскады: A22 и A44 чистых переходов");
            Console.WriteLine();
            Console.WriteLine("   каскад              мультиполи      A22 (справка)      A44 (справка)");

            // Первые три — каскады, чьи A_kk приводятся в учебниках числом.
            // Остальные собраны из табличных F-коэффициентов
            // (Ферентц — Розенцвейг): F₂(2,2,0,2) = −0.5976,
            // F₄(2,2,0,2) = +1.0690, F₂(1,1,1,2) = +0.4183,
            // F₂(1,1,2,2) = −0.4183, F₂(1,1,3,2) = +0.1195.
            //
            // ОСТОРОЖНО, здесь уже наступали: четыре ожидания в первой
            // редакции пробы были выписаны по памяти и оказались неверны —
            // разошёлся не код, а «справка». Каждое число ниже либо
            // учебничное, либо произведение двух табличных F.
            int bad = 0;
            bad += Check("4 → 2 → 0", 4, 2, 0, E2, 0.0, E2, 0.0, 0.1020, 0.0091);
            bad += Check("0 → 2 → 0", 0, 2, 0, E2, 0.0, E2, 0.0, 0.3571, 1.1429);
            bad += Check("1 → 1 → 0", 1, 1, 0, E1, 0.0, E1, 0.0, -0.2500, 0.0000);
            bad += Check("1 → 2 → 0", 1, 2, 0, E1, 0.0, E2, 0.0, -0.2500, 0.0000);
            bad += Check("2 → 2 → 0", 2, 2, 0, E1, 0.0, E2, 0.0, 0.2500, 0.0000);
            bad += Check("3 → 2 → 0", 3, 2, 0, E1, 0.0, E2, 0.0, -0.0714, 0.0000);
            bad += Check("6 → 4 → 2", 6, 4, 2, E2, 0.0, E2, 0.0, 0.1020, 0.0091);
            Console.WriteLine();
            Console.WriteLine("   Проверка знака смешивания: 2(M1+E2)→2→0. При δ = 0 это чистый");
            Console.WriteLine("   M1, при δ → ∞ — чистый E2, и коэффициент обязан перейти от");
            Console.WriteLine("   одного предела к другому непрерывно и через ноль.");
            Console.WriteLine();
            Console.WriteLine("        δ        A22");
            foreach (double d in new[] { 0.0, 0.2, 0.5, 1.0, 2.0, 5.0, 100.0 })
            {
                AngularCorrelation.Coefficients w =
                    AngularCorrelation.For(2, 2, 0, 304, d, E2, 0.0);
                Console.WriteLine("   {0,8:F1}   {1,8:F4}", d, w.A22);
            }

            Console.WriteLine();
            return bad;
        }

        static int Check(string name, double j1, double j2, double j3,
                         int mult1, double d1, int mult2, double d2,
                         double a22, double a44)
        {
            AngularCorrelation.Coefficients w =
                AngularCorrelation.For(j1, j2, j3, mult1, d1, mult2, d2);
            bool ok = Math.Abs(w.A22 - a22) < 5e-4 && Math.Abs(w.A44 - a44) < 5e-4;
            Console.WriteLine("   {0,-16}  {1,4}+{2,-6}  {3,9:F5} ({4,8:F5})  {5,9:F5} ({6,8:F5})  {7}",
                              name, mult1, mult2, w.A22, a22, w.A44, a44, ok ? "ok" : "РАСХОЖДЕНИЕ");
            return ok ? 0 : 1;
        }

        static int Wigner()
        {
            Console.WriteLine("2. Символы Вигнера порознь (аргументы удвоены)");
            Console.WriteLine();
            int bad = 0;
            // (1 1 0; 0 0 0) = -1/sqrt(3);  (1 1 2; 0 0 0) = sqrt(2/15)
            bad += Value("(1 1 0; 0 0 0)",
                         AngularCorrelation.ThreeJ(2, 2, 0, 0, 0, 0), -1.0 / Math.Sqrt(3.0));
            bad += Value("(1 1 2; 0 0 0)",
                         AngularCorrelation.ThreeJ(2, 2, 4, 0, 0, 0), Math.Sqrt(2.0 / 15.0));
            // (2 2 2; 1 -1 0) = sqrt(2/35)/2 и {1 1 0; 1 1 1} = -1/3 —
            // оба посчитаны формулой Рака на бумаге, оба разошлись с памятью
            bad += Value("(2 2 2; 1 -1 0)",
                         AngularCorrelation.ThreeJ(4, 4, 4, 2, -2, 0), 0.5 * Math.Sqrt(2.0 / 35.0));
            // {1 1 0; 1 1 1} = 1/3 ; {1 1 1; 1 1 1} = 1/6
            bad += Value("{1 1 0; 1 1 1}",
                         AngularCorrelation.SixJ(2, 2, 0, 2, 2, 2), -1.0 / 3.0);
            bad += Value("{1 1 1; 1 1 1}",
                         AngularCorrelation.SixJ(2, 2, 2, 2, 2, 2), 1.0 / 6.0);
            // полуцелые: {1/2 1/2 1; 1/2 1/2 1} = 1/6
            bad += Value("{1/2 1/2 1; 1/2 1/2 1}",
                         AngularCorrelation.SixJ(1, 1, 2, 1, 1, 2), 1.0 / 6.0);
            Console.WriteLine();
            return bad;
        }

        static int Value(string name, double got, double want)
        {
            bool ok = Math.Abs(got - want) < 1e-10;
            Console.WriteLine("   {0,-24} {1,14:F10}  ждали {2,14:F10}  {3}",
                              name, got, want, ok ? "ok" : "РАСХОЖДЕНИЕ");
            return ok ? 0 : 1;
        }

        static void FromDatabase(int z, int a, double e1, double e2)
        {
            Console.WriteLine("3. Схема из базы: Z = {0}, A = {1}, пара {2:F1} + {3:F1} кэВ",
                              z, a, e1, e2);
            Console.WriteLine();

            AngularCorrelation.Scheme scheme = AngularCorrelation.SchemeOf(z, a);
            if (scheme == null)
            {
                Console.WriteLine("   схемы нет: таблиц g4_level/g4_gamma в базе нет"
                                  + " либо нуклида в них нет");
                return;
            }

            Console.WriteLine("   уровней со спином {0}, переходов {1}",
                              scheme.Jpi.Count, scheme.Transitions.Count);

            AngularCorrelation.Transition first = scheme.Find(e1, 0.5);
            AngularCorrelation.Transition second = scheme.Find(e2, 0.5);
            Describe("первый ", first, scheme);
            Describe("второй ", second, scheme);

            if (first == null || second == null)
            {
                return;
            }

            // Каскадом пара может быть в любом порядке: который из квантов
            // испущен раньше, определяет схема, а не порядок в ключе.
            AngularCorrelation.Coefficients w = scheme.Cascade(first, second);
            if (w.IsIsotropic)
            {
                w = scheme.Cascade(second, first);
            }

            if (w.IsIsotropic)
            {
                Console.WriteLine("   корреляции нет: либо это не каскад"
                                  + " (конец первого не начало второго), либо спинов не хватило");
                return;
            }

            Console.WriteLine("   {0}", w);
            Console.WriteLine();
            Console.WriteLine("      θ, °      W(θ)");
            foreach (int angle in new[] { 0, 30, 60, 90, 120, 150, 180 })
            {
                Console.WriteLine("      {0,4}    {1,7:F4}",
                                  angle, w.At(Math.Cos(angle * Math.PI / 180.0)));
            }
        }

        static void Describe(string tag, AngularCorrelation.Transition t,
                             AngularCorrelation.Scheme scheme)
        {
            if (t == null)
            {
                Console.WriteLine("   {0}— перехода такой энергии в схеме нет", tag);
                return;
            }

            double jFrom, jTo;
            string from = scheme.Jpi.TryGetValue(t.FromSeq, out jFrom)
                ? jFrom.ToString("F1", CultureInfo.InvariantCulture) : "?";
            string to = scheme.Jpi.TryGetValue(t.ToSeq, out jTo)
                ? jTo.ToString("F1", CultureInfo.InvariantCulture) : "?";
            Console.WriteLine("   {0}{1,9:F3} кэВ: уровень {2} → {3}, спины {4} → {5},"
                              + " мультипольность {6}, δ = {7}",
                              tag, t.EnergyKev, t.FromSeq, t.ToSeq, from, to,
                              t.Multipolarity, t.Mixing.ToString("F4", CultureInfo.InvariantCulture));
        }
    }
}
