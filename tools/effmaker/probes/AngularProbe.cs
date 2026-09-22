using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AngularProbe
{
    /// <summary>
    /// Поверка угловых γ-γ корреляций (N5): коэффициенты A₂₂ и A₄₄ против
    /// справочных значений и против схем уровней в базе.
    ///
    ///     angularprobe [--nuclide=28:60] [--pair=1173.2:1332.5] [--old-sign] [--old-order]
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
    ///
    /// **Раздел 4 — десять каскадов против прямой мерки Geant4** (П85/П86,
    /// 15.09.2026, `AMBER42`): спины, мультипольности и δ берутся из схемы
    /// базы (`g4_level`/`g4_gamma` — та же поставка PhotonEvaporation, по
    /// которой Geant4 разыгрывал направления), A₂₂/A₄₄ сравниваются с
    /// измеренными `g4cf angcorr` (5 млн распадов на пару, σ ≈ 0.001…0.009).
    /// Учебные каскады раздела 1 все ЧИСТЫЕ (δ = 0) — знак δ заселяющего
    /// перехода они не видят, а он у нас был неверен (П85, находка 1); этот
    /// раздел ловит именно его: у смешанных каскадов A₂₂ ∝ 2δ·F′, а A₄₄ ∝ δ²
    /// и знака не видит. Отказ — |наше − Geant4| > 3σ + 0.003 у любого из
    /// двадцати чисел. Ключ `--old-sign` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: считает
    /// δ₁ с обратным знаком (то есть формулой ДО правки П86) и обязан
    /// краснеть на Cs-134 563+605 (−0.168 против +0.026) и ещё четырёх
    /// смешанных; без ключа обязан быть зелёным.
    ///
    /// ⚠ Строка Eu-152 964+122 (код 403 = E2+M1, δ = −9.3) с 22.09.2026
    /// (П121, `AMBER58`) сверяется с Geant4 на ПРАВЛЕННОЙ поставке
    /// PhotonEvaporation (403 → 304 у переходов с δ ≠ 0;
    /// `handover/handover-2026-09-22-p121-cascade-summing.md`): штатный
    /// Geant4 читает код позиционно (`G4GammaTransition::SampleDirection`:
    /// `L0 = mp/200; Lp = (mp%100)/2`, δ² на `Lp`) и на коде 403 кладёт δ² на
    /// M1 — то есть считает НЕ ТУ смесь; его 0.3261/0.0014 (П85) — число с
    /// тем же дефектом, что был у нас, и сверка с ним прошла тривиально.
    ///
    /// **Раздел 5 — порядок мультиполей в коде смеси (`AMBER58`).** δ ENSDF
    /// (= PhotonEvaporation = `g4_gamma`) определён как отношение СТАРШЕЙ
    /// мультипольности к младшей независимо от порядка записи кода (304 =
    /// M1+E2 и 403 = E2+M1 — одна смесь; измерено `database/scheme.md` §5г
    /// по α_K). Судится: (а) 403 и 304 с одной δ дают одни A_kk побитово;
    /// (б) при δ = 0 порядок значит «доминирующая первой» — 403 и 304 обязаны
    /// РАЗЛИЧАТЬСЯ (чистый E2 против чистого M1); (в) реплика ПОЗИЦИОННОГО
    /// чтения (δ² на второй компонент) на 964+122 воспроизводит штатного
    /// Geant4 0.3261 ± 0.0040 / 0.0014 ± 0.0054, а приложение — правленного
    /// 0.0063 ± 0.0040 / 0.3200 ± 0.0053; (г) перебор `schemedb`: сколько
    /// переходов с δ ≠ 0 записаны старшей первой и у скольких из них
    /// перестановка меняет A₂/A₄ перехода. Ключ `--old-order` —
    /// ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: в (в) судится реплика, проба обязана краснеть.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            int z = 28, a = 60;
            double e1 = 1173.239, e2 = 1332.514;
            bool oldSign = false;
            bool oldOrder = false;
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
                else if (s == "--old-sign")
                {
                    oldSign = true;
                }
                else if (s == "--old-order")
                {
                    oldOrder = true;
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
            bad += Impostors();
            FromDatabase(z, a, e1, e2);
            bad += Geant4Cascades(oldSign);
            bad += MultipoleOrder(oldOrder);

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
            Console.WriteLine("   (δ здесь — у ЗАСЕЛЯЮЩЕГО перехода: с П86 его интерференционный");
            Console.WriteLine("   член несёт (−1)^(L+L'), знак хода по δ — обратный прежнему;");
            Console.WriteLine("   числом это судит раздел 4.)");
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

        /// <summary>
        /// Ловушка самозванца (`W26`, `D31`): «ближайший по энергии» берёт не
        /// тот переход. Читатель у правки был обязан появиться вместе с ней —
        /// без него «починено» и «сломано» с виду одно и то же.
        ///
        /// У Hf-176 на линию распада 306.780 кэВ в `g4_gamma` три кандидата:
        /// настоящий 3→2 (306.640 кэВ, уровень 596.82, интенсивность 100 %) и
        /// два с нулевой интенсивностью — 167→150 (306.900, уровень 3467.40) и
        /// 191→171 (307.300). Ближе всех по энергии САМОЗВАНЕЦ 306.900
        /// (промах 0.120 против 0.140), а населить уровень 3467 кэВ β-распад
        /// Lu-176 с Q = 1194 кэВ не может в принципе.
        /// </summary>
        static int Impostors()
        {
            Console.WriteLine();
            Console.WriteLine("2а. Ловушка самозванца: Hf-176, линия 306.780 кэВ");
            Console.WriteLine();

            AngularCorrelation.Scheme scheme = AngularCorrelation.SchemeOf(72, 176);
            if (scheme == null)
            {
                Console.WriteLine("   схемы Hf-176 нет — сверку сделать нечем");
                return 1;
            }

            AngularCorrelation.Transition t = scheme.Find(306.780, 0.6);
            if (t == null)
            {
                Console.WriteLine("   перехода не нашлось вовсе — ⛔ ПРОВАЛ");
                return 1;
            }

            bool ok = t.FromSeq == 3 && t.ToSeq == 2;
            Console.WriteLine("   выбран {0:F3} кэВ, уровень {1} → {2}; ждали 306.640, 3 → 2   {3}",
                              t.EnergyKev, t.FromSeq, t.ToSeq,
                              ok ? "ok" : "⛔ САМОЗВАНЕЦ");

            // Второй признак того же: переходы с нулевой интенсивностью не
            // должны попадать в схему ВОВСЕ.
            bool impostorLoaded = false;
            foreach (AngularCorrelation.Transition x in scheme.Transitions)
            {
                if (x.FromSeq == 167 && x.ToSeq == 150)
                {
                    impostorLoaded = true;
                }
            }

            Console.WriteLine("   переход 167 → 150 (интенсивность 0) в схеме: {0}   {1}",
                              impostorLoaded ? "ЕСТЬ" : "нет",
                              impostorLoaded ? "⛔ ПРОВАЛ" : "ok");
            return (ok ? 0 : 1) + (impostorLoaded ? 1 : 0);
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

        /// <summary>
        /// Одна строка таблицы Geant4 (П85, `handover/p85-amber42/g4_angcorr_table.txt`,
        /// режим `g4cf angcorr`, 5 млн распадов на пару): нуклид ДОЧЕРНЕЙ
        /// схемы (Z, A — как в `g4_level`/`g4_gamma`), две линии и
        /// измеренные A₂₂ ± σ, A₄₄ ± σ. Порядок квантов в каскаде проба
        /// определяет по схеме, а не по порядку в строке.
        /// </summary>
        sealed class G4Case
        {
            public string Name;
            public int Z, A;
            public double E1, E2;
            public double A22, S22, A44, S44;

            public G4Case(string name, int z, int a, double e1, double e2,
                          double a22, double s22, double a44, double s44)
            {
                this.Name = name; this.Z = z; this.A = a; this.E1 = e1; this.E2 = e2;
                this.A22 = a22; this.S22 = s22; this.A44 = a44; this.S44 = s44;
            }
        }

        // Geant4 11.4.2, `G4PhotonEvaporation` с `fCorrelatedGamma`, поставка
        // PhotonEvaporation6.1.2 — та же, что в `schemedb.sqlite`. Числа — из
        // таблицы П85 дословно. Первые три и последние два каскада ЧИСТЫЕ
        // (δ = 0) — у них знак δ ничего не меняет, они держат остальную
        // формулу; пять средних — СМЕШАННЫЕ, у них A₂₂ и решает знак.
        static readonly G4Case[] Geant4Table =
        {
            new G4Case("Co-60  1173(E2+M3, δ=−0.0025)+1332", 28,  60, 1173.2, 1332.5,  0.0995, 0.0010,  0.0104, 0.0014),
            new G4Case("Y-88   898(E1)+1836",                38,  88,  898.0, 1836.1, -0.0710, 0.0010,  0.0004, 0.0014),
            new G4Case("Cs-134 605(E2)+796",                 56, 134,  604.7,  795.9,  0.1008, 0.0011,  0.0097, 0.0015),
            new G4Case("Cs-134 569(M1+E2, δ=0.26)+796",      56, 134,  569.3,  795.9,  0.1029, 0.0026,  0.0083, 0.0035),
            new G4Case("Cs-134 563(M1+E2, δ=−7.4)+605",      56, 134,  563.2,  604.7,  0.0255, 0.0036,  0.3230, 0.0048),
            new G4Case("Eu-152 1408(E1+M2, δ=0.043)+122",    62, 152, 1408.0,  121.8,  0.2165, 0.0033,  0.0042, 0.0044),
            new G4Case("Eu-152 1112(M1+E2, δ=−8.7)+122",     62, 152, 1112.1,  121.8, -0.2913, 0.0037, -0.0811, 0.0051),
            // ⛔ (П121, `AMBER58`) Правленная поставка PhotonEvaporation (403 → 304 при
            // δ ≠ 0; `patch_pe.py` в `handover/p121-cascade-summing/`), той же сборкой
            // g4cf, 5 млн распадов: A₂₂ = 0.00629 ± 0.00404, A₄₄ = 0.32004 ± 0.00529.
            // Штатная поставка той же сборкой в тот же день — 0.32611 ± 0.00400 /
            // 0.00145 ± 0.00539 (= П85): позиционное чтение Geant4, контроль в разделе 5.
            new G4Case("Eu-152 964(E2+M1, δ=−9.3)+122",      62, 152,  964.1,  121.8,  0.0063, 0.0040,  0.3200, 0.0053),
            new G4Case("Eu-152 779(E1)+344 (Gd)",            64, 152,  778.9,  344.3, -0.0748, 0.0028, -0.0019, 0.0038),
            new G4Case("Eu-152 411(E2)+344 (Gd)",            64, 152,  411.1,  344.3,  0.1007, 0.0069,  0.0066, 0.0092),
        };

        /// <summary>Допуск к σ Geant4: три сигмы плюс 0.003 (шум формулы против 5 млн распадов).</summary>
        const double G4Sigmas = 3.0, G4Floor = 0.003;

        /// <summary>
        /// Раздел 4: десять каскадов по схеме базы против Geant4. Переходы
        /// берутся <see cref="AngularCorrelation.Scheme.Find"/> с допуском
        /// 0.5 кэВ — тем же правилом «самый нижний уровень», что у сумматора;
        /// порядок квантов — по смежности (конец первого = начало второго).
        /// Печатаются три столбца A_kk: приложение, δ₁ с обратным знаком, Geant4;
        /// судится столбец приложения, с ключом `--old-sign` — столбец с
        /// обратным знаком (положительный контроль: обязан краснеть).
        /// </summary>
        static int Geant4Cascades(bool oldSign)
        {
            Console.WriteLine();
            Console.WriteLine("4. Десять каскадов по схеме базы против прямой мерки Geant4 (П85; допуск 3σ + {0:F3})",
                              G4Floor);
            if (oldSign)
            {
                Console.WriteLine("   ⚠ --old-sign: судится столбец «δ₁ обратный» — формула ДО правки П86; ждём КРАСНОГО");
            }

            Console.WriteLine();
            Console.WriteLine("   каскад                               спины      δ₁       A22 прил. | δ₁ обр. | Geant4 ± σ          A44 прил. | δ₁ обр. | Geant4 ± σ       ");
            int bad = 0;
            foreach (G4Case c in Geant4Table)
            {
                AngularCorrelation.Scheme scheme = AngularCorrelation.SchemeOf(c.Z, c.A);
                if (scheme == null)
                {
                    Console.WriteLine("   {0,-36} схемы Z={1} A={2} в базе нет — ⛔ ПРОВАЛ", c.Name, c.Z, c.A);
                    bad++;
                    continue;
                }

                AngularCorrelation.Transition first = scheme.Find(c.E1, 0.5);
                AngularCorrelation.Transition second = scheme.Find(c.E2, 0.5);
                if (first == null || second == null)
                {
                    Console.WriteLine("   {0,-36} перехода нет в схеме ({1}) — ⛔ ПРОВАЛ", c.Name,
                                      first == null ? c.E1 : c.E2);
                    bad++;
                    continue;
                }

                // Каскад — по смежности уровней; который квант первый, решает схема.
                if (first.ToSeq != second.FromSeq)
                {
                    AngularCorrelation.Transition t = first; first = second; second = t;
                }

                double jStart, jMiddle, jEnd;
                if (first.ToSeq != second.FromSeq
                    || !scheme.Jpi.TryGetValue(first.FromSeq, out jStart)
                    || !scheme.Jpi.TryGetValue(first.ToSeq, out jMiddle)
                    || !scheme.Jpi.TryGetValue(second.ToSeq, out jEnd))
                {
                    Console.WriteLine("   {0,-36} не каскад или спина нет — ⛔ ПРОВАЛ", c.Name);
                    bad++;
                    continue;
                }

                jStart = Math.Abs(jStart); jMiddle = Math.Abs(jMiddle); jEnd = Math.Abs(jEnd);
                AngularCorrelation.Coefficients app = AngularCorrelation.For(
                    jStart, jMiddle, jEnd, first.Multipolarity, first.Mixing,
                    second.Multipolarity, second.Mixing);
                // Обратный знак δ₁: у всех смешанных переходов базы L′ = L ± 1,
                // и множитель (−1)^(L+L′) правки П86 — это ровно −δ₁; значит
                // −δ₁ на входе даёт формулу ДО правки побитово.
                AngularCorrelation.Coefficients rev = AngularCorrelation.For(
                    jStart, jMiddle, jEnd, first.Multipolarity, -first.Mixing,
                    second.Multipolarity, second.Mixing);
                AngularCorrelation.Coefficients judged = oldSign ? rev : app;

                bool ok22 = Math.Abs(judged.A22 - c.A22) <= G4Sigmas * c.S22 + G4Floor;
                bool ok44 = Math.Abs(judged.A44 - c.A44) <= G4Sigmas * c.S44 + G4Floor;
                Console.WriteLine("   {0,-36} {1}→{2}→{3}  {4,7:F4}   {5,8:F4} | {6,7:F4} | {7,7:F4} ± {8:F4} {9,-3}  {10,8:F4} | {11,7:F4} | {12,7:F4} ± {13:F4} {14}",
                                  c.Name, jStart, jMiddle, jEnd, first.Mixing,
                                  app.A22, rev.A22, c.A22, c.S22, ok22 ? "ok" : "⛔",
                                  app.A44, rev.A44, c.A44, c.S44, ok44 ? "ok" : "⛔ РАСХОЖДЕНИЕ");
                bad += (ok22 ? 0 : 1) + (ok44 ? 0 : 1);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0
                                  ? "   все двадцать чисел в допуске Geant4"
                                  : "   ⛔ вне допуска Geant4: " + bad.ToString(CultureInfo.InvariantCulture)
                                    + (oldSign ? " (ожидаемо: судился обратный знак δ₁)" : ""));
            return bad;
        }

        /// <summary>
        /// Штатный Geant4 на 964+122 (позиционное чтение кода 403): П85 и повтор
        /// П121 той же сборкой — 0.32611 ± 0.00400 / 0.00145 ± 0.00539.
        /// </summary>
        const double G4Positional22 = 0.3261, G4Positional22Sigma = 0.0040,
                     G4Positional44 = 0.0014, G4Positional44Sigma = 0.0054;

        /// <summary>
        /// A_k одного перехода ПОЗИЦИОННЫМ чтением кода: δ² на ВТОРОЙ компонент
        /// (`l` — первый, `lPrime` — второй), знак интерференции — как в
        /// приложении после П86. Это формула ДО правки П121 и формула Geant4
        /// (`G4PolarizationTransition::GammaTransFCoefficient`).
        /// </summary>
        static double AkPositional(int k, int l, int lPrime, double delta, double jOther, double jMiddle)
        {
            double pure = AngularCorrelation.F(k, l, l, jOther, jMiddle);
            if (delta == 0.0)
            {
                return pure;
            }

            double cross = AngularCorrelation.F(k, l, lPrime, jOther, jMiddle);
            double high = AngularCorrelation.F(k, lPrime, lPrime, jOther, jMiddle);
            return (pure + 2.0 * delta * cross + delta * delta * high) / (1.0 + delta * delta);
        }

        /// <summary>Порядок мультиполя по коду Geant4 (E1/M1 → 1, E2/M2 → 2, …); 0 — не гамма.</summary>
        static int OrderOfCode(int code)
        {
            return code >= 2 && code <= 16 ? code / 2 : 0;
        }

        /// <summary>
        /// Раздел 5 (`AMBER58`): δ² — на старшую мультипольность. Четыре плеча,
        /// см. шапку. Возвращает число провалов.
        /// </summary>
        static int MultipoleOrder(bool oldOrder)
        {
            Console.WriteLine();
            Console.WriteLine("5. Порядок мультиполей в коде смеси (AMBER58): δ² ложится на старшую");
            if (oldOrder)
            {
                Console.WriteLine("   ⚠ --old-order: в (в) судится реплика позиционного чтения — формула ДО правки П121; ждём КРАСНОГО");
            }

            Console.WriteLine();
            int bad = 0;

            // (а) одна смесь в обе стороны записи.
            AngularCorrelation.Coefficients a = AngularCorrelation.For(2, 2, 0, 403, -9.3, E2, 0.0);
            AngularCorrelation.Coefficients b = AngularCorrelation.For(2, 2, 0, 304, -9.3, E2, 0.0);
            bool same = a.A22 == b.A22 && a.A44 == b.A44;
            Console.WriteLine("   (а) 2→2→0, δ₁ = −9.3: код 403 → {0}; код 304 → {1}   {2}",
                              a, b, same ? "ok (побитово одно)" : "⛔ РАСХОЖДЕНИЕ");
            bad += same ? 0 : 1;

            // (б) при δ = 0 первый компонент — чистое приближение: 403 = E2, 304 = M1.
            AngularCorrelation.Coefficients c = AngularCorrelation.For(2, 2, 0, 403, 0.0, E2, 0.0);
            AngularCorrelation.Coefficients d = AngularCorrelation.For(2, 2, 0, 304, 0.0, E2, 0.0);
            AngularCorrelation.Coefficients pureE2 = AngularCorrelation.For(2, 2, 0, E2, 0.0, E2, 0.0);
            AngularCorrelation.Coefficients pureM1 = AngularCorrelation.For(2, 2, 0, M1, 0.0, E2, 0.0);
            bool okZero = c.A22 == pureE2.A22 && c.A44 == pureE2.A44
                          && d.A22 == pureM1.A22 && d.A44 == pureM1.A44
                          && (c.A22 != d.A22 || c.A44 != d.A44);
            Console.WriteLine("   (б) δ₁ = 0: код 403 → {0} (чистый E2 {1}); код 304 → {2} (чистый M1 {3})   {4}",
                              c, pureE2, d, pureM1, okZero ? "ok (первый компонент чистым)" : "⛔ РАСХОЖДЕНИЕ");
            bad += okZero ? 0 : 1;

            // (в) 964+122 по схеме базы: приложение против правленного Geant4,
            // реплика позиционного чтения — против штатного.
            AngularCorrelation.Scheme scheme = AngularCorrelation.SchemeOf(62, 152);
            AngularCorrelation.Transition first = scheme != null ? scheme.Find(964.1, 0.5) : null;
            AngularCorrelation.Transition second = scheme != null ? scheme.Find(121.8, 0.5) : null;
            double jStart, jMiddle, jEnd;
            if (first == null || second == null || first.ToSeq != second.FromSeq
                || !scheme.Jpi.TryGetValue(first.FromSeq, out jStart)
                || !scheme.Jpi.TryGetValue(first.ToSeq, out jMiddle)
                || !scheme.Jpi.TryGetValue(second.ToSeq, out jEnd))
            {
                Console.WriteLine("   (в) схемы Sm-152 или каскада 964+122 в базе нет — ⛔ ПРОВАЛ");
                return bad + 1;
            }

            jStart = Math.Abs(jStart); jMiddle = Math.Abs(jMiddle); jEnd = Math.Abs(jEnd);
            AngularCorrelation.Coefficients app = AngularCorrelation.For(
                jStart, jMiddle, jEnd, first.Multipolarity, first.Mixing, second.Multipolarity, second.Mixing);
            int l1 = OrderOfCode(first.Multipolarity / 100), l1Prime = OrderOfCode(first.Multipolarity % 100);
            int l2 = OrderOfCode(second.Multipolarity >= 100 ? second.Multipolarity / 100 : second.Multipolarity);
            double delta1 = ((l1 + l1Prime) % 2 != 0) ? -first.Mixing : first.Mixing;
            AngularCorrelation.Coefficients replica = new AngularCorrelation.Coefficients();
            replica.A22 = AkPositional(2, l1, l1Prime, delta1, jStart, jMiddle) * AkPositional(2, l2, l2, 0.0, jEnd, jMiddle);
            replica.A44 = AkPositional(4, l1, l1Prime, delta1, jStart, jMiddle) * AkPositional(4, l2, l2, 0.0, jEnd, jMiddle);
            G4Case patched = Geant4Table[7];
            AngularCorrelation.Coefficients judged = oldOrder ? replica : app;
            bool ok22 = Math.Abs(judged.A22 - patched.A22) <= G4Sigmas * patched.S22 + G4Floor;
            bool ok44 = Math.Abs(judged.A44 - patched.A44) <= G4Sigmas * patched.S44 + G4Floor;
            bool rep22 = Math.Abs(replica.A22 - G4Positional22) <= G4Sigmas * G4Positional22Sigma + G4Floor;
            bool rep44 = Math.Abs(replica.A44 - G4Positional44) <= G4Sigmas * G4Positional44Sigma + G4Floor;
            Console.WriteLine("   (в) Sm-152 964 (код {0}, δ = {1:F1}) + 122, спины {2}→{3}→{4}:",
                              first.Multipolarity, first.Mixing, jStart, jMiddle, jEnd);
            Console.WriteLine("       приложение          A22 = {0,7:F4}  A44 = {1,7:F4}  | Geant4 на правленной поставке {2:F4} ± {3:F4} / {4:F4} ± {5:F4}   {6}",
                              app.A22, app.A44, patched.A22, patched.S22, patched.A44, patched.S44,
                              (oldOrder ? "(судится реплика)" : (ok22 && ok44 ? "ok" : "⛔ РАСХОЖДЕНИЕ")));
            Console.WriteLine("       реплика позиционная A22 = {0,7:F4}  A44 = {1,7:F4}  | штатный Geant4 (П85, П121)     {2:F4} ± {3:F4} / {4:F4} ± {5:F4}   {6}",
                              replica.A22, replica.A44, G4Positional22, G4Positional22Sigma, G4Positional44, G4Positional44Sigma,
                              rep22 && rep44 ? "ok (штатный арбитр = позиционное чтение)" : "⛔ РАСХОЖДЕНИЕ");
            if (oldOrder)
            {
                Console.WriteLine("       реплика против правленной поставки: {0}", ok22 && ok44 ? "ok (⛔ контроль НЕ взят)" : "⛔ КРАСНОЕ — как и должно");
            }

            bad += (ok22 ? 0 : 1) + (ok44 ? 0 : 1) + (rep22 ? 0 : 1) + (rep44 ? 0 : 1);

            // (г) перебор schemedb.
            bad += ReversedCodes();
            return bad;
        }

        /// <summary>
        /// (г) Все переходы `g4_gamma` с кодом смеси, δ ≠ 0 и СТАРШЕЙ первой:
        /// число (ревизия П119 насчитала 753), и у скольких перестановка меняет
        /// A₂ или A₄ перехода (переход берётся снимающим: j_middle = J_from,
        /// j_other = J_to; без спина обоих уровней — «нечем считать»). Провал —
        /// если таких переходов нет вовсе (перебор мерил бы пустоту).
        /// </summary>
        static int ReversedCodes()
        {
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "schemedb.sqlite");
            if (!System.IO.File.Exists(path))
            {
                Console.WriteLine("   (г) рядом с пробой нет schemedb.sqlite — ⛔ ПРОВАЛ");
                return 1;
            }

            int reversed = 0, changed = 0, noSpin = 0, zeroMixingReversed = 0;
            var byCode = new SortedDictionary<int, int>();
            double worst = 0.0;
            string worstWhere = "";
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                "Data Source=" + path + ";Mode=ReadOnly;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "select g.z, g.a, g.from_seq, g.to_seq, g.energy_ev, g.multipolarity, g.mixing_ratio,"
                        + " lf.jpi, lt.jpi from g4_gamma g"
                        + " left join g4_level lf on lf.z = g.z and lf.a = g.a and lf.seq = g.from_seq"
                        + " left join g4_level lt on lt.z = g.z and lt.a = g.a and lt.seq = g.to_seq"
                        + " where g.multipolarity >= 100";
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            int code = reader.GetInt32(5);
                            int hi = OrderOfCode(code / 100), lo = OrderOfCode(code % 100);
                            if (hi == 0 || lo == 0 || hi <= lo)
                            {
                                continue;
                            }

                            double mixing = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6);
                            if (mixing == 0.0)
                            {
                                zeroMixingReversed++;
                                continue;
                            }

                            reversed++;
                            int had;
                            byCode.TryGetValue(code, out had);
                            byCode[code] = had + 1;
                            if (reader.IsDBNull(7) || reader.IsDBNull(8))
                            {
                                noSpin++;
                                continue;
                            }

                            double jFrom = Math.Abs(reader.GetDouble(7)), jTo = Math.Abs(reader.GetDouble(8));
                            double d = mixing;
                            double a2Old = AkPositional(2, hi, lo, d, jTo, jFrom), a2New = AkPositional(2, lo, hi, d, jTo, jFrom);
                            double a4Old = AkPositional(4, hi, lo, d, jTo, jFrom), a4New = AkPositional(4, lo, hi, d, jTo, jFrom);
                            double diff = Math.Max(Math.Abs(a2Old - a2New), Math.Abs(a4Old - a4New));
                            if (diff > 1e-6)
                            {
                                changed++;
                                if (diff > worst)
                                {
                                    worst = diff;
                                    worstWhere = string.Format(CultureInfo.InvariantCulture,
                                        "Z={0} A={1} {2:F1} кэВ код {3} δ={4:G4}: A2 {5:F4}→{6:F4}, A4 {7:F4}→{8:F4}",
                                        reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(4) / 1000.0, code, d,
                                        a2Old, a2New, a4Old, a4New);
                                }
                            }
                        }
                    }
                }
            }

            var codes = new StringBuilder();
            foreach (KeyValuePair<int, int> entry in byCode)
            {
                codes.Append(entry.Key).Append(':').Append(entry.Value).Append(' ');
            }

            Console.WriteLine("   (г) schemedb: переходов смеси с δ ≠ 0 и старшей первой — {0} ({1}); с δ = 0 таких {2} (не трогаются);",
                              reversed, codes.ToString().TrimEnd(), zeroMixingReversed);
            Console.WriteLine("       перестановка меняет A₂/A₄ у {0}, спина обоих уровней нет у {1}; худшее: {2}",
                              changed, noSpin, worstWhere);
            bool ok = reversed > 0 && changed > 0;
            Console.WriteLine("       {0}", ok ? "ok (перебор не пуст)" : "⛔ ПРОВАЛ: перебор пуст");
            return ok ? 0 : 1;
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
