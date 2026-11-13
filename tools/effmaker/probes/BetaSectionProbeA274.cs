using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;

/// <summary>
/// `AMBER274`, этап Э1 (29.10.2026; исправления по критикам 30.10.2026): РАЗДЕЛ СТРОК
/// ЭЛЕКТРОНА ПРОБЫ — дописать к готовой матрице отклика и принять замером. Решение Amber
/// 29.10.2026 вопросником, дословно: «Бета пробы первой (Рекомендую)». Журнал —
/// `handover/handover-2026-10-29-amber274-e1-sample-beta.md`.
///
///     BetaSectionProbeA274 --scene=<сцена.in> --matrix=<матрица.rmx> --out=<новая.rmx>
///                          [--n=100000] [--seed=0] [--stop=<%>] [--small=<доля>] [--stop-dry] [--no-budget] [--budget=work|hist|clock|off] [--budget-share=0.97] [--small-by-kind] [--work-dump] [--threads=N] [--grid=80,60,3600] [--gpu=<rmgpu_f.dll>]
///                          [--positron=1] [--capsule=osgi|none|<вещество>:<мм>[:ring]] [--dump=<строки.csv>]
///     BetaSectionProbeA274 --section-control --scene= --matrix=   (раздел: формат, порча, капсула, физика)
///     BetaSectionProbeA274 --capsule-control --scene=<точечная сцена>   (единицы капсулы, воздух, сдвиг точки)
///     BetaSectionProbeA274 --channels-control                     (сложение 7 и 8 каналов, `Д3`)
///     BetaSectionProbeA274 --shapes=40K,137CS,...                 (формы против LNHB, линии конверсии)
///     BetaSectionProbeA274 --direct --scene=<сцена.in> --nuclides=90SR,90Y [--n=] [--direct-csv=<файл>]
///                          [--no-ce] [--with-gamma] [--grid=…]
///         (сверка с опытом и Geant4: строки электрона сцены без матрицы, свёртка с формами и
///          линиями конверсии — спектр оставленной энергии НА РАСПАД, кэВ)
///     BetaSectionProbeA274 --backscatter --scene=<сцена.in> [--n=20000]   (`Ф9`: пучок на голый кристалл)
///     BetaSectionProbeA274 --mono=624.216,655.67 --scene=<сцена.in> [--n=] [--mono-csv=<префикс>]
///         (физика 28: моноэнергетический электрон из точки сцены — оставленная энергия на
///          испущенный и энергия ВХОДА в кристалл по полосам, как таблицы критика t1a/t2)
///     BetaSectionProbeA274 --peaks=<A.rmx>,<B.rmx>   (физика 28: пик и сумма узлов двух матриц, B/A)
///     BetaSectionProbeA274 --spectrum=<nucid>:<файл>   (физика 28: таблица формы бет для `g4cf espec`)
///     BetaSectionProbeA274 --parity --scene=<сцена.in> [--gpu=<rmgpu_f.dll>] [--n=] [--grid=] [--lines=]
///         (физика 28: паритет ЦП↔GPU раздела — доля вошедших в 3σ, χ²/n свёрнутых строк ≤ 1.5)
///     Общий ключ `--estr=0|1` (физика 28) — страгглинг потерь у симуляторов пробы (`Make`);
///     Общий ключ `--mott=0|1` (физика 29, `phys29b`) — поправка Мотта при Z > 30.
///     Общий ключ `--emix=0|1` (физика 29) — перенос, сходящийся по шагу; `--step=<доля>` — доля шага
///     и в слоях, и в кристалле (`--cstep=<доля>` — только в кристалле, поверх); доля шага
///     переноса (физика 29, сходимость); `--backscatter` берёт и `--crystal=CsI|NaI|Al` и `--slab` (толстая пластина).
///     нет ключа — умолчание склада. Раздел к матрице считается физикой САМОЙ матрицы.
///     `--lines=lib[:<мин. %>]|none|<кэВ,кэВ,…>` (физика 28) — строки линий конверсии на
///     точной энергии в разделе: умолчание `lib:1` — линии рядов библиотеки нуклидов с
///     выходом ≥ 1 % на распад члена; у `--direct` — все линии своих нуклидов (`none` —
///     прежняя интерполяция узлов, плечо сравнения).
///     `--branches=lib[:<мин. %>]|none|<нуклиды>` (`AMBER274` Ф8, `f8cyl`) — СТРОКИ ВЕТВЕЙ (шов
///     события целиком: бета и кванты каскада её уровня одной историей) в разделе: умолчание
///     `lib:1` — β⁻-ветви рядов библиотеки с выходом ≥ 1 % и каскадом; `none` — ключ шва ВЫКЛ
///     (строки узлов и линий побайтно те же); у `--parity` умолчание — `none`.
///     `--branch-census [--branches=…]` — перепись ветвей (взято, пропущено и почему).
///     `--direct --event --scene= --nuclides=60CO [--n=] [--direct-csv=]` — полный распад моделью
///     тремя плечами из одних строк: шов (событие), каскад без беты (бета весами узлов), без шва
///     (все линии своими строками) — сверка с ионом `g4cf … ionhist 2 <Emax> … ion Z A N`.
///     `--photon=E1,E2 --scene= [--n=] [--direct-csv=]` (`f8cyl`, дальнее дно) — фотонные строки
///     сцены по полосам (пик, комптон, обратное рассеяние) и в CSV.
///
/// ⛔ `--out` — СВОЙ каталог: проба ПИШЕТ матрицу (правило «проба матриц пишет в
/// `--dir`»); вход `--matrix` только читается.
///
/// ⛔ `--capsule=<вещество>:<мм>` (`Р10`) — ТОЛЬКО плёнка, кольца нет (как до умолчания
/// ОСГИ); `<вещество>:<мм>:ring` — плёнка с кольцом ОСГИ; `osgi` — умолчание; `none` —
/// голая точка.
///
/// Код 1 — нарушено (названо `⛔ НАРУШЕНО`), код 2 — ключи или данные.
/// </summary>
public static class BetaSectionProbeA274
{
    static int failures;
    static string shapeCsv;
    static bool withGamma, noConversion, directLight;

    /// <summary>(`AMBER274`, физика 28) `--estr=0|1`: страгглинг у симуляторов пробы; −1 — умолчание склада.</summary>
    static int estr = -1;
    /// <summary>(`AMBER274`, физика 29) `--emix=0|1`: перенос, сходящийся по шагу, у симуляторов пробы; −1 — умолчание склада.</summary>
    static int emix = -1;
    /// <summary>(`AMBER274`, физика 29, `phys29b`) `--mott=0|1`: поправка Мотта при Z &gt; 30 у симуляторов пробы; −1 — умолчание склада.</summary>
    static int mott = -1;
    /// <summary>(`AMBER274`, физика 30, `phys29c`) `--eref=0|1`, `--dpwa=0|1`: правки критиков физики 29 и ядерная
    /// часть по DPWA у симуляторов пробы; −1 — умолчание склада. `--rparts=` — биты частей eref (рычаг замера).</summary>
    static int eref = -1, dpwa = -1, refineParts = -1;
    /// <summary>(`AMBER274`, физика 30, Ф5) `--positron-morph-control`: синтетика — строки позитрона не растягиваются.</summary>
    static bool positronMorphControl;
    /// <summary>(`AMBER274`, `phys29b`) `--morph=0,1`: растягивающая интерполяция строк у `--direct`
    /// (`ResponseElectronSection.ScaleMorph`: 0 — прежняя шапочка, 1 — растяжение) — образы из ОДНИХ строк; несколько — CSV с суффиксом `_morph<N>`.</summary>
    static string morphModes = null;
    /// <summary>(`AMBER274`, `phys29b`) `--dump-rows=<файл>`: строки `--direct` и плотности ветвей — для разбора весов вне прогона.</summary>
    static string dumpRows = null;
    /// <summary>(`AMBER274`, физика 29) `--step=`: доля шага переноса у симуляторов пробы; ≤ 0 — умолчание.</summary>
    static double stepFraction = -1.0;
    /// <summary>(`AMBER274`, физика 29) `--cstep=`: доля шага В КРИСТАЛЛЕ под ключом emix (поверх `--step=`); ≤ 0 — умолчание.</summary>
    static double crystalStepFraction = -1.0;
    /// <summary>(`AMBER274`, физика 29) `--cutoff=` (градусы отсечки смешанной схемы) и `--parts=` (половины ключа) у симуляторов пробы.</summary>
    static double cutoffDeg = -1.0;
    static int mixedParts = -1;
    /// <summary>(`AMBER274`, физика 29) `--backscatter`: вещество пластины, толстая пластина, энергии.</summary>
    static string backscatterCrystal;
    static bool backscatterSlab;
    static double[] backscatterEnergies;
    static string monoCsv;

    /// <summary>
    /// (`AMBER318`) `--stop=<%>`: останов строки раздела по шуму на ЦП, как в окне матрицы (пробный
    /// проход N/10, потолок 8×N; 0 — плоско, умолчание, как у склада). На GPU не действует.
    /// `--small=<доля>` — порог малой строки (`ResponseMatrixBuilder.SectionSmallRowShare`, второй заход
    /// `AMBER318`); `--stop-dry` — только пробный проход и таблица «порог → истории, время» в журнал;
    /// `--no-budget` — без бюджета раздела (третий заход `AMBER318`; плечо сравнения).
    /// </summary>
    static double stopTarget;

    /// <summary>(`AMBER274`, физика 28) `--lines=`: строки линий конверсии раздела.</summary>
    static string linesSpec;

    /// <summary>(`AMBER274` Ф8, `f8cyl`) `--branches=lib[:%]|none|<нуклиды>`: строки ветвей (шов события целиком) раздела и паритета.</summary>
    static string branchesSpec;

    /// <summary>(`f8cyl`, дальнее дно) `--photon=E1,E2,…`: фотонная строка сцены (Response) по полосам и в CSV `--direct-csv`.</summary>
    static string photonSpec;

    /// <summary>(`AMBER274` Ф8, `f8cyl`) `--fsa-branch-control`: веса FSA у ветви со строкой ветви (рычаг, площадь, длина образа).</summary>
    static bool fsaBranchControl;

    /// <summary>(`AMBER274` Ф8, `f8cyl`) `--event-n=`: историй строки ветви у `--direct --event` (умолчание — `--n=`).</summary>
    static int eventHistories;

    /// <summary>(`AMBER274` Ф8, `f8cyl`) `--branch-census`: перепись ветвей по `--branches=`.</summary>
    static bool branchCensus;

    /// <summary>(`AMBER274` Ф8, `f8cyl`) `--event`: у `--direct` — полный распад тремя плечами (шов, каскад без беты, без шва).</summary>
    static bool directEvent;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string scene = null, matrixPath = null, outPath = null, dump = null, capsule = null;
        int n = 100000, seed = 0, threads = Environment.ProcessorCount;
        bool nGiven = false;
        bool positron = true;
        double[] grid = null;
        string shapes = null;
        bool direct = false, capsuleControl = false, channelsControl = false, sectionControl = false, backscatter = false;
        string directCsv = null, nuclides = null, curve = null, mono = null, census = null, peaks = null, gpuPath = null;
        bool parity = false;
        string esrcStats = null, spectrum = null;
        foreach (string a in args)
        {
            if (a.StartsWith("--shapes=", StringComparison.Ordinal)) { shapes = a.Substring(9); continue; }
            if (a == "--direct") { direct = true; continue; }
            if (a == "--with-gamma") { withGamma = true; continue; }
            if (a == "--no-ce") { noConversion = true; continue; }
            if (a == "--light") { directLight = true; continue; }
            if (a == "--capsule-control") { capsuleControl = true; continue; }
            if (a == "--channels-control") { channelsControl = true; continue; }
            if (a == "--section-control") { sectionControl = true; continue; }
            if (a == "--backscatter") { backscatter = true; continue; }
            if (a.StartsWith("--direct-csv=", StringComparison.Ordinal)) { directCsv = a.Substring(13); continue; }
            if (a.StartsWith("--nuclides=", StringComparison.Ordinal)) { nuclides = a.Substring(11); continue; }
            if (a.StartsWith("--curve=", StringComparison.Ordinal)) { curve = a.Substring(8); continue; }
            if (a.StartsWith("--shape-csv=", StringComparison.Ordinal)) { shapeCsv = a.Substring(12); continue; }
            // (`AMBER274`/`AMBER315`, a315u 13.11.2026) ключ λ `FsaBetaLibrary.CoulombLambda` (сфера; клеймо раздела `cl=2`)
            if (a.StartsWith("--clambda=", StringComparison.Ordinal))
            {
                string v = a.Substring(10);
                if (v != "0" && v != "1") { throw new ArgumentException("--clambda=0|1"); }
                BecquerelMonitor.FullSpectrumAnalysis.FsaBetaLibrary.CoulombLambda = v == "1";
                continue;
            }
            // (`AMBER274`/`AMBER315`, a315u) ключ L0 `FsaBetaLibrary.FermiFiniteSize` (клеймо раздела `l0=`)
            if (a.StartsWith("--l0=", StringComparison.Ordinal))
            {
                string v = a.Substring(5);
                if (v != "0" && v != "1") { throw new ArgumentException("--l0=0|1"); }
                BecquerelMonitor.FullSpectrumAnalysis.FsaBetaLibrary.FermiFiniteSize = v == "1";
                continue;
            }
            if (a.StartsWith("--deep=", StringComparison.Ordinal))
            {
                // (`AMBER315`) Глубокий шаг переноса в слоях: `0` — прежний путь, `1` — умолчание,
                // иное число — запас в см (испорченный контроль: `-1e9`, раздел обязан разойтись).
                string deep = a.Substring(7);
                if (deep == "0") EfficiencySimulator.LayerDeepStep = false;
                else if (deep != "1") EfficiencySimulator.LayerDeepMarginCm = double.Parse(deep, CultureInfo.InvariantCulture);
                continue;
            }

            if (a.StartsWith("--estr=", StringComparison.Ordinal)) { estr = a.Substring(7) != "0" ? 1 : 0; continue; }
            if (a.StartsWith("--emix=", StringComparison.Ordinal)) { emix = a.Substring(7) != "0" ? 1 : 0; continue; }
            if (a.StartsWith("--mott=", StringComparison.Ordinal)) { mott = a.Substring(7) != "0" ? 1 : 0; continue; }
            if (a.StartsWith("--eref=", StringComparison.Ordinal)) { eref = a.Substring(7) != "0" ? 1 : 0; continue; }
            if (a.StartsWith("--dpwa=", StringComparison.Ordinal)) { dpwa = a.Substring(7) != "0" ? 1 : 0; continue; }
            if (a.StartsWith("--rparts=", StringComparison.Ordinal)) { refineParts = int.Parse(a.Substring(9), CultureInfo.InvariantCulture); continue; }
            if (a.StartsWith("--morph=", StringComparison.Ordinal)) { morphModes = a.Substring(8); continue; }
            if (a.StartsWith("--dump-rows=", StringComparison.Ordinal)) { dumpRows = a.Substring(12); continue; }
            if (a.StartsWith("--step=", StringComparison.Ordinal)) { stepFraction = double.Parse(a.Substring(7), CultureInfo.InvariantCulture); continue; }
            if (a.StartsWith("--cstep=", StringComparison.Ordinal)) { crystalStepFraction = double.Parse(a.Substring(8), CultureInfo.InvariantCulture); continue; }
            if (a.StartsWith("--cutoff=", StringComparison.Ordinal)) { cutoffDeg = double.Parse(a.Substring(9), CultureInfo.InvariantCulture); continue; }
            if (a.StartsWith("--parts=", StringComparison.Ordinal)) { mixedParts = int.Parse(a.Substring(8), CultureInfo.InvariantCulture); continue; }
            if (a.StartsWith("--crystal=", StringComparison.Ordinal)) { backscatterCrystal = a.Substring(10); continue; }
            if (a == "--slab") { backscatterSlab = true; continue; }
            if (a.StartsWith("--energies=", StringComparison.Ordinal))
            {
                backscatterEnergies = Array.ConvertAll(a.Substring(11).Split(','), s => double.Parse(s, CultureInfo.InvariantCulture));
                continue;
            }
            if (a.StartsWith("--mono=", StringComparison.Ordinal)) { mono = a.Substring(7); continue; }
            if (a.StartsWith("--mono-csv=", StringComparison.Ordinal)) { monoCsv = a.Substring(11); continue; }
            if (a.StartsWith("--line-census=", StringComparison.Ordinal)) { census = a.Substring(14); continue; }
            if (a.StartsWith("--lines=", StringComparison.Ordinal)) { linesSpec = a.Substring(8); continue; }
            if (a.StartsWith("--branches=", StringComparison.Ordinal)) { branchesSpec = a.Substring(11); continue; }
            if (a == "--event") { directEvent = true; continue; }
            if (a.StartsWith("--event-n=", StringComparison.Ordinal)) { eventHistories = int.Parse(a.Substring(10), CultureInfo.InvariantCulture); continue; }
            if (a.StartsWith("--photon=", StringComparison.Ordinal)) { photonSpec = a.Substring(9); continue; }
            if (a == "--fsa-branch-control") { fsaBranchControl = true; continue; }
            if (a == "--branch-census") { branchCensus = true; continue; }
            if (a == "--positron-morph-control") { positronMorphControl = true; continue; }
            if (a.StartsWith("--peaks=", StringComparison.Ordinal)) { peaks = a.Substring(8); continue; }
            if (a == "--parity") { parity = true; continue; }
            if (a.StartsWith("--esrc-stats=", StringComparison.Ordinal)) { esrcStats = a.Substring(13); continue; }
            if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) { spectrum = a.Substring(11); continue; }
            if (a.StartsWith("--gpu=", StringComparison.Ordinal)) { gpuPath = a.Substring(6); continue; }
            if (a.StartsWith("--scene=", StringComparison.Ordinal)) scene = a.Substring(8);
            else if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixPath = a.Substring(9);
            else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
            else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dump = a.Substring(7);
            else if (a.StartsWith("--capsule=", StringComparison.Ordinal)) capsule = a.Substring(10);
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) { n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture); nGiven = true; }
            else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--stop=", StringComparison.Ordinal)) stopTarget = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--small=", StringComparison.Ordinal)) ResponseMatrixBuilder.SectionSmallRowShare = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
            else if (a == "--stop-dry") ResponseMatrixBuilder.SectionStopPilotOnly = true;
            else if (a == "--no-budget") ResponseMatrixBuilder.SectionBudget = false;
            else if (a == "--work-dump") ResponseMatrixBuilder.SectionWorkDump = true;
            else if (a == "--small-by-kind") ResponseMatrixBuilder.SectionSmallByKind = true;
            else if (a.StartsWith("--budget-share=", StringComparison.Ordinal)) ResponseMatrixBuilder.SectionBudgetShare = double.Parse(a.Substring(15), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--budget=", StringComparison.Ordinal))
                ResponseMatrixBuilder.SectionBudgetMode = a.Substring(9) == "hist" ? 1 : a.Substring(9) == "clock" ? 3 : a.Substring(9) == "off" ? 0 : 2;
            else if (a.StartsWith("--threads=", StringComparison.Ordinal)) threads = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--positron=", StringComparison.Ordinal)) positron = a.Substring(11) != "0";
            else if (a.StartsWith("--grid=", StringComparison.Ordinal))
            {
                string[] p = a.Substring(7).Split(',');
                grid = ResponseElectronSection.Grid(int.Parse(p[0], CultureInfo.InvariantCulture),
                                                    double.Parse(p[1], CultureInfo.InvariantCulture),
                                                    double.Parse(p[2], CultureInfo.InvariantCulture));
            }
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        GlobalConfigManager.GetInstance();
        if (positronMorphControl)
        {
            PositronMorphControl();
            return Done();
        }

        if (branchCensus)
        {
            SectionBranchList(branchesSpec);
            return Done();
        }

        if (channelsControl)
        {
            ChannelsControl();
            return Done();
        }

        if (shapes != null)
        {
            Shapes(shapes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            return Done();
        }

        if (peaks != null)
        {
            Peaks(peaks);
            return Done();
        }

        if (spectrum != null)
        {
            Spectrum(spectrum);
            return Done();
        }

        if (census != null)
        {
            if (census.StartsWith("lib", StringComparison.Ordinal))
            {
                foreach (double t in new[] { 0.001, 0.01, 0.1, 0.5, 1.0 }) LibraryLines(t, 60.0, 3600.0, true);
            }
            else
            {
                LineCensus(census);
            }

            return Done();
        }

        if (scene == null)
        {
            Console.Error.WriteLine("нужен --scene=<сцена.in>");
            return 2;
        }

        // (проверка экраном) Сцена — геометрия кривой из конфигурации прибора: `--scene=<прибор.xml> --curve=<guid>`.
        GeometryModel geometry = scene.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? CurveGeometry(scene, curve) : GeometryModel.Load(scene);
        if (geometry == null)
        {
            Console.Error.WriteLine("нет геометрии кривой " + curve + " в " + scene);
            return 2;
        }

        if (capsule != null && !ApplyCapsule(geometry, capsule))
        {
            return 2;
        }

        if (capsuleControl)
        {
            CapsuleControl(geometry);
            XmlControl(geometry);
            return Done();
        }

        if (backscatter)
        {
            Backscatter(geometry, n);
            return Done();
        }

        if (photonSpec != null)
        {
            PhotonRows(geometry, photonSpec, n, directCsv);
            return Done();
        }

        if (fsaBranchControl)
        {
            FsaBranchControl(geometry, nuclides ?? "60CO", n);
            return Done();
        }

        if (direct && directEvent)
        {
            DirectEvent(geometry, nuclides ?? "60CO", grid ?? ResponseElectronSection.DefaultGrid(), n, threads, directCsv);
            return Done();
        }

        if (direct)
        {
            Direct(geometry, nuclides ?? "90SR,90Y", grid ?? ResponseElectronSection.DefaultGrid(), n, threads, directCsv);
            return Done();
        }

        if (mono != null)
        {
            Mono(geometry, mono, n);
            return Done();
        }

        if (esrcStats != null)
        {
            EsrcStats(geometry, gpuPath, esrcStats, n, positron);
            return Done();
        }

        if (parity)
        {
            Parity(geometry, gpuPath, grid ?? ResponseElectronSection.DefaultGrid(), n, seed, positron);
            return Done();
        }

        if (matrixPath == null)
        {
            Console.Error.WriteLine("нужен --matrix=<матрица.rmx>");
            return 2;
        }

        MatrixRefusal refusal;
        int format;
        ResponseMatrix matrix = ResponseMatrix.Load(matrixPath, out refusal, out format);
        if (matrix == null)
        {
            Console.Error.WriteLine("матрица не прочитана: " + refusal + ", формат " + format);
            return 2;
        }

        Console.WriteLine("AMBER274 Э1: раздел электрона пробы — сцена {0}, матрица формата {1}, {2} узлов, бин {3} кэВ, физика {4}",
                          geometry.Name, format, matrix.Energies.Length, matrix.BinKev, ResponseMatrix.PhysicsFromStamp(matrix.Stamp));
        Console.WriteLine("  матрица годна для сцены: {0}; капсула: {1}; сдвиг точки {2} мм", matrix.IsValidFor(geometry),
                          ResponseElectronSection.CapsuleText(geometry), geometry.SourceCapsuleOffset);
        if (!matrix.IsValidFor(geometry))
        {
            Console.Error.WriteLine("⛔ матрица не годна для сцены — раздел не дописывается");
            return 2;
        }

        if (sectionControl)
        {
            SectionControl(geometry, matrix, matrixPath, format);
            return Done();
        }

        if (outPath != null)
        {
            string bodyBefore = matrix.BodyFingerprint;
            string stampBefore = matrix.Stamp;
            // (`AMBER305`) Без `--n=` раздел к матрице — рецептом склада; прежнее умолчание 100 тыс.
            // дало реализацию, принятую однажды за «значение с разделом».
            if (!nGiven)
            {
                n = ElectronSectionRecipe.StoreHistories;
            }

            double[] lineList = SectionLineList(linesSpec);
            // (`AMBER274` Ф8, `f8cyl`) Строки ветвей — шов события целиком; `--branches=none` — ВЫКЛ.
            List<ElectronBranchSpec> branchList = SectionBranchList(branchesSpec);
            ResponseElectronSection section;
            if (gpuPath != null)
            {
                // (`AMBER274`, физика 28) Раздел на GPU (`rm_esrc`) — клеймо то же, движок `gpu`.
                using (var dev = new RmGpu(gpuPath.Length > 0 ? gpuPath : RmGpu.DefaultPath, RmGpu.DefaultStackBytes))
                {
                    section = GpuBuild.BuildElectronSection(dev, geometry, matrix, grid, n, seed, positron, lineList, branchList,
                                                            CancellationToken.None, Console.WriteLine);
                }
            }
            else
            {
                section = ResponseMatrixBuilder.BuildElectronSection(
                    geometry, matrix, grid, n, seed, positron, threads, CancellationToken.None, Console.WriteLine, lineList,
                    branchList, null, stopTarget);
            }
            matrix.ElectronSection = section;
            matrix.Save(outPath);

            MatrixRefusal r2;
            int f2;
            ResponseMatrix back = ResponseMatrix.Load(outPath, out r2, out f2);
            Check(back != null, "дописанная матрица не читается: " + r2);
            if (back != null)
            {
                ElectronSectionRefusal why;
                ResponseElectronSection s = back.ElectronSectionFor(geometry, out why);
                Console.WriteLine("  (а) после дописи: формат {0}; отпечаток тела {1} → {2}; клеймо то же: {3}; годна: {4}; раздел: {5}",
                                  f2, Short(bodyBefore), Short(back.BodyFingerprint), back.Stamp == stampBefore,
                                  back.IsValidFor(geometry), why);
                Check(f2 == ResponseMatrix.FormatVersion, "формат дописанной матрицы не " + ResponseMatrix.FormatVersion);
                Check(back.BodyFingerprint == bodyBefore && back.StoredBodyFingerprint == bodyBefore,
                      "(а) тело матрицы изменилось при дописи раздела");
                Check(back.Stamp == stampBefore && back.IsValidFor(geometry), "(а) клеймо или годность фотонной части изменились");
                Check(s != null, "раздел не годен для своей сцены: " + why);
                if (s != null)
                {
                    Check(s.NodeCount == section.NodeCount && s.HasPositron == positron, "раздел прочитан не тем числом узлов");
                    bool same = true;
                    for (int i = 0; i < s.NodeCount && same; i++)
                    {
                        same = s.Electron[i].Length == section.Electron[i].Length;
                        for (int b = 0; same && b < s.Electron[i].Length; b++) same = s.Electron[i][b] == section.Electron[i][b];
                    }

                    Check(same, "строки электрона прочитаны не теми числами");
                    bool sameLines = s.LineCount == section.LineCount;
                    for (int j = 0; j < s.LineCount && sameLines; j++)
                    {
                        sameLines = s.LineEnergies[j] == section.LineEnergies[j] && s.LineElectron[j].Length == section.LineElectron[j].Length;
                        for (int b = 0; sameLines && b < s.LineElectron[j].Length; b++) sameLines = s.LineElectron[j][b] == section.LineElectron[j][b];
                    }

                    Check(sameLines, "строки линий прочитаны не теми числами");
                    Console.WriteLine("  строк линий конверсии: {0}", s.LineCount);
                    CheckBranchesRoundTrip(section, s);
                    Console.WriteLine("  клеймо раздела: {0}", s.Stamp);
                }
            }

            if (dump != null)
            {
                Dump(section, dump);
            }

            Summary(section);
        }

        return Done();
    }

    static GeometryModel CurveGeometry(string deviceXml, string guid)
    {
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(DeviceConfigInfo));
        DeviceConfigInfo device;
        using (var stream = File.OpenRead(deviceXml))
        {
            device = (DeviceConfigInfo)serializer.Deserialize(stream);
        }

        foreach (EfficiencyConfigData e in device.EfficiencyConfigs)
        {
            if (string.Equals(e.Guid, guid, StringComparison.OrdinalIgnoreCase))
            {
                return e.Geometry;
            }
        }

        return null;
    }

    static int Done()
    {
        Console.WriteLine(failures == 0 ? "ИТОГ: все проверки выполнены" : "ИТОГ: ОТКАЗ, нарушений " + failures);
        return failures == 0 ? 0 : 1;
    }

    static bool ApplyCapsule(GeometryModel g, string spec)
    {
        // `none` — голая точка (ни плёнки, ни кольца); `osgi` — умолчание ОСГИ.
        if (string.Equals(spec, "none", StringComparison.OrdinalIgnoreCase))
        {
            g.SourceCapsuleThickness = 0.0;
            g.SourceCapsule = new GeometryMaterial();
            g.SourceCapsuleRingHeight = 0.0;
            return true;
        }

        if (string.Equals(spec, "osgi", StringComparison.OrdinalIgnoreCase))
        {
            g.SourceCapsule = GeometryModel.OsgiFilm();
            g.SourceCapsuleThickness = GeometryModel.OsgiFilmMm;
            g.SourceCapsuleRingHeight = GeometryModel.OsgiRingHeightMm;
            g.SourceCapsuleRingInnerDiameter = GeometryModel.OsgiRingInnerMm;
            g.SourceCapsuleRingOuterDiameter = GeometryModel.OsgiRingOuterMm;
            return true;
        }

        string[] p = spec.Split(':');
        if (p.Length != 2 && !(p.Length == 3 && p[2] == "ring"))
        {
            Console.Error.WriteLine("--capsule=<вещество>:<мм>[:ring] | osgi | none");
            return false;
        }

        GeometryMaterialLibrary.Entry entry = null;
        foreach (GeometryMaterialLibrary.Entry e in GeometryMaterialLibrary.Seed())
        {
            if (string.Equals(e.Name, p[0], StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Abbr, p[0], StringComparison.OrdinalIgnoreCase))
            {
                entry = e;
            }
        }

        if (entry == null)
        {
            Console.Error.WriteLine("нет вещества " + p[0] + " в библиотеке веществ");
            return false;
        }

        g.SourceCapsule = GeometryMaterialLibrary.Make(entry, entry.Density);
        g.SourceCapsuleThickness = double.Parse(p[1], CultureInfo.InvariantCulture);
        // (`Р10`) Без `:ring` — только плёнка: прежний смысл ключа до умолчания ОСГИ.
        g.SourceCapsuleRingHeight = p.Length == 3 ? GeometryModel.OsgiRingHeightMm : 0.0;
        g.SourceCapsuleRingInnerDiameter = GeometryModel.OsgiRingInnerMm;
        g.SourceCapsuleRingOuterDiameter = GeometryModel.OsgiRingOuterMm;
        return true;
    }

    // ------------------------------------------------------------------ контроли

    /// <summary>
    /// (`Д1`, `Ф12`, `Ф6`) Положительный контроль единиц капсулы: выход из капсулы против
    /// CSDA, посчитанной ЗДЕСЬ из миллиметров модели (t/10 см), — расхождение в 10 раз (как
    /// у прежнего кода) видно сразу. Плюс воздух до корпуса и сдвиг точки ОСГИ.
    /// </summary>
    static void CapsuleControl(GeometryModel scene)
    {
        Console.WriteLine("== контроль единиц капсулы (Д1), воздуха (Ф12) и сдвига точки (Ф6): сцена {0}", scene.Name);
        var options = new ResponseMatrixOptions();
        MethodInfo exit = typeof(EfficiencySimulator).GetMethod("CapsuleExit", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo carry = typeof(EfficiencySimulator).GetMethod("CarryMedium", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo water = typeof(EfficiencySimulator).GetMethod("WaterTable", BindingFlags.Instance | BindingFlags.NonPublic);

        // (а) плёнка PS 1 мм, нормаль, 500 кэВ, без воздуха: ждём CSDA (критик: 273.2 кэВ).
        GeometryModel a = scene.Clone();
        ApplyCapsule(a, "PS:1");
        EfficiencySimulator sa = Make(a, options, 500.0, 1);
        sa.CapsuleAir = false;
        // (физика 28) Контроль ЕДИНИЦ — против CSDA, без разброса потерь; разброс — пункт (д).
        sa.ElectronStraggling = false;
        double got = (double)exit.Invoke(sa, new object[] { 500.0, 1.0 });
        var medium = (ElectronData.Material)carry.Invoke(sa, new object[] { a.SourceCapsule });
        double want = ElectronData.EnergyOfRange(medium, ElectronData.RangeOf(medium, 500.0) - (1.0 / 10.0) * a.SourceCapsule.Density);
        Console.WriteLine("   (а) PS 1 мм, 500 кэВ, нормаль: вышло {0:F1} кэВ, CSDA {1:F1} кэВ (критик: 273.2; прежний код 478.9)", got, want);
        Check(Math.Abs(got - want) < 1e-6 * want, "(а) выход из плёнки не равен CSDA по миллиметрам модели");
        Check(Math.Abs(want - 273.2) < 5.0, "(а) CSDA PS 1 мм не сходится с числом критика 273.2 кэВ");

        // (б) кольцо ОСГИ (Al, стенка 2.5 мм), uz = 0: ждём CSDA по 0.25 см Al.
        GeometryModel b = scene.Clone();
        ApplyCapsule(b, "osgi");
        EfficiencySimulator sb = Make(b, options, 1000.0, 2);
        sb.CapsuleAir = false;
        sb.ElectronStraggling = false;
        var al = (ElectronData.Material)carry.Invoke(sb, new object[] { GeometryModel.OsgiRingMaterial() });
        double[] critic = { 0.0, 484.8, 991.6 };
        double[] energies = { 1000.0, 1500.0, 2000.0 };
        for (int i = 0; i < energies.Length; i++)
        {
            double t = energies[i];
            double g2 = (double)exit.Invoke(sb, new object[] { t, 0.0 });
            double left = ElectronData.RangeOf(al, t) - 0.25 * 2.699;
            double w2 = left > 0.0 ? ElectronData.EnergyOfRange(al, left) : 0.0;
            Console.WriteLine("   (б) кольцо Al 2.5 мм, uz = 0, {0:F0} кэВ: вышло {1:F1}, CSDA {2:F1} (критик {3:F1})", t, g2, w2, critic[i]);
            Check(Math.Abs(g2 - w2) <= 1e-6 * Math.Max(1.0, w2), "(б) выход через кольцо не равен CSDA по 2.5 мм Al");
            Check(Math.Abs(w2 - critic[i]) < 10.0, "(б) CSDA кольца не сходится с числом критика");
        }

        // (в) воздух: голая точка, путь до корпуса = расстояние сцены.
        GeometryModel c = scene.Clone();
        ApplyCapsule(c, "none");
        EfficiencySimulator sc = Make(c, options, 500.0, 3);
        sc.ElectronStraggling = false;
        sc.EnsureBuiltForProbe();
        double airCm = c.PointDistance / 10.0;
        double g3 = (double)exit.Invoke(sc, new object[] { 500.0, 1.0 });
        var wt = (ElectronData.Material)water.Invoke(sc, null);
        double w3 = airCm > 0.0 ? ElectronData.EnergyOfRange(wt, ElectronData.RangeOf(wt, 500.0) - airCm * 1.205e-3) : 500.0;
        Console.WriteLine("   (в) воздух {0:F1} см, 500 кэВ, нормаль: вышло {1:F2}, CSDA {2:F2} кэВ", airCm, g3, w3);
        Check(Math.Abs(g3 - w3) < 1e-6 * w3, "(в) потеря в воздухе не равна CSDA по расстоянию сцены");

        // (г) ⛔ откат Ф6 (`critpt30`, 02.11.2026): точка ОСГИ стоит там же, где голая, —
        // на расстоянии сцены; сдвиг модели 0 у обеих (было: ОСГИ дальше ровно на 1.5 мм).
        double zBare = SourceZ(sc), zOsgi = SourceZ(sb);
        Console.WriteLine("   (г) точка: голая z = {0:F4} см, ОСГИ z = {1:F4} см, разность {2:F4} см (ждём 0, откат Ф6)", zBare, zOsgi, zBare - zOsgi);
        Check(zBare == zOsgi, "(г) точка ОСГИ не там же, где голая (откат Ф6)");
        Check(b.SourceCapsuleOffset == 0.0 && c.SourceCapsuleOffset == 0.0, "(г) сдвиг модели не 0 / 0 мм (откат Ф6)");

        // (д) (физика 28) Страгглинг в капсуле: та же плёнка PS 1 мм, 500 кэВ, нормаль — со
        // страгглингом средняя энергия выхода ≈ CSDA (средняя потеря сохраняется), разброс есть;
        // без него — ровно CSDA. Положительный контроль: ключ доехал до `CapsuleExit`.
        EfficiencySimulator sd = Make(a, options, 500.0, 4);
        sd.CapsuleAir = false;
        sd.ElectronStraggling = true;
        double sum = 0.0, sum2 = 0.0;
        const int Draws = 20000;
        for (int i = 0; i < Draws; i++)
        {
            double e = (double)exit.Invoke(sd, new object[] { 500.0, 1.0 });
            sum += e;
            sum2 += e * e;
        }

        double mean = sum / Draws, rms = Math.Sqrt(Math.Max(0.0, sum2 / Draws - mean * mean));
        Console.WriteLine("   (д) страгглинг: PS 1 мм, 500 кэВ, нормаль, {0} розыгрышей — средняя {1:F1} кэВ (CSDA {2:F1}), "
                          + "разброс {3:F1} кэВ; шагов с розыгрышем {4}", Draws, mean, want, rms, sd.CountStragglingSteps);
        Check(rms > 1.0 && sd.CountStragglingSteps == Draws, "(д) страгглинг не дошёл до капсулы");
        Check(Math.Abs(mean - want) < 0.03 * want, "(д) средняя энергия выхода со страгглингом ушла от CSDA больше 3 %");
    }

    /// <summary>
    /// (`Д4`) Поля капсулы в XML конфигурации/спектра: у точки с ОСГИ — ни одного элемента
    /// (файлы прежние до знака), у голой точки — есть и читаются обратно, у не точечной
    /// сцены — нет при любых значениях полей; нет элементов — читается ОСГИ.
    /// </summary>
    static void XmlControl(GeometryModel point)
    {
        Console.WriteLine("== контроль XML капсулы (Д4)");
        var serializer = new System.Xml.Serialization.XmlSerializer(typeof(GeometryModel));
        Func<GeometryModel, string> write = g =>
        {
            using (var w = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(w, g);
                return w.ToString();
            }
        };
        GeometryModel osgi = point.Clone();
        ApplyCapsule(osgi, "osgi");
        string a = write(osgi);
        GeometryModel bare = point.Clone();
        ApplyCapsule(bare, "none");
        string b = write(bare);
        GeometryModel vessel = point.Clone();
        vessel.SourceType = GeometrySourceType.Cylinder;
        ApplyCapsule(vessel, "none");
        string c = write(vessel);
        var back = (GeometryModel)serializer.Deserialize(new StringReader(b));
        var backOsgi = (GeometryModel)serializer.Deserialize(new StringReader(a));
        Console.WriteLine("   ОСГИ: элементов капсулы {0}; голая точка: {1}, прочитана толщина {2} мм; цилиндр с голой капсулой: {3}; без элементов прочитано ОСГИ: {4}",
                          Count(a), Count(b), back.SourceCapsuleThickness, Count(c), backOsgi.IsOsgiCapsule);
        Check(Count(a) == 0, "(Д4) у точки с ОСГИ поля капсулы ушли в XML");
        Check(Count(b) == 5 && back.SourceCapsuleThickness == 0.0 && back.SourceCapsuleRingHeight == 0.0,
              "(Д4) голая точка не записана пятью полями или не прочитана обратно");
        Check(Count(c) == 0, "(Д4) у не точечной сцены поля капсулы ушли в XML");
        Check(backOsgi.IsOsgiCapsule, "(Д4) без элементов прочитана не ОСГИ");
    }

    static int Count(string xml)
    {
        int n = 0;
        foreach (string tag in new[] { "<SourceCapsule>", "<SourceCapsuleThickness>", "<SourceCapsuleRingHeight>",
                                       "<SourceCapsuleRingInnerDiameter>", "<SourceCapsuleRingOuterDiameter>" })
        {
            if (xml.Contains(tag)) n++;
        }

        return n;
    }

    static double SourceZ(EfficiencySimulator sim)
    {
        sim.EnsureBuiltForProbe();
        object source = typeof(EfficiencySimulator).GetField("source", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim);
        return (double)source.GetType().GetField("z", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(source);
    }

    /// <summary>
    /// (`Р2`, `Д2`, `Р1`, `Ф5`) Контроль раздела: формат 11/12, порча снимает только раздел,
    /// сверка со сценой (капсула, физика, изотропное поле), перезапись без раздела — байты
    /// прежние.
    /// </summary>
    static void SectionControl(GeometryModel geometry, ResponseMatrix matrix, string matrixPath, int format)
    {
        string dir = Path.Combine(Path.GetTempPath(), "a274_section_control_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Console.WriteLine("== контроль раздела (Р1, Р2, Д2, Ф5)");
            // (Р1) Без раздела — формат 11 и байты входа (формат 11) прежние.
            string plain = Path.Combine(dir, "plain.rmx");
            matrix.ElectronSection = null;
            matrix.Save(plain);
            int f0, p0;
            ResponseMatrix.PeekVersions(plain, out f0, out p0);
            byte[] input = File.ReadAllBytes(matrixPath), rewritten = File.ReadAllBytes(plain);
            bool sameBytes = format == ResponseMatrix.CompatibleFormatVersion && Equal(input, rewritten);
            Console.WriteLine("   (Р1) без раздела: формат {0}; перезапись входа формата {1} побайтно та же: {2} ({3} байт)",
                              f0, format, sameBytes, rewritten.Length);
            Check(f0 == ResponseMatrix.CompatibleFormatVersion, "(Р1) матрица без раздела записана не форматом 11");
            if (format == ResponseMatrix.CompatibleFormatVersion)
            {
                Check(sameBytes, "(Р1) перезапись матрицы формата 11 без раздела изменила байты");
            }

            // Раздел маленький — шесть узлов по 2000 историй: контроль формата, не физики.
            ResponseElectronSection section = ResponseMatrixBuilder.BuildElectronSection(
                geometry, matrix, ResponseElectronSection.Grid(6, 300.0, 3000.0), 2000, 0, true, 4, CancellationToken.None, null);
            matrix.ElectronSection = section;
            string full = Path.Combine(dir, "full.rmx");
            matrix.Save(full);
            MatrixRefusal r;
            int f;
            ResponseMatrix back = ResponseMatrix.Load(full, out r, out f);
            ElectronSectionRefusal why = ElectronSectionRefusal.Missing;
            Check(back != null && f == ResponseMatrix.FormatVersion && back.ElectronSectionFor(geometry, out why) != null,
                  "раздел не читается обратно (формат 12)");
            Console.WriteLine("   с разделом: формат {0}, отпечаток раздела {1}", f, back != null ? Short(back.ElectronSection != null ? back.ElectronSection.Fingerprint : null) : "—");

            // (Д2) Другая капсула той же сцены — раздел негоден, причина «другая капсула».
            GeometryModel other = geometry.Clone();
            ApplyCapsule(other, "PET:0.1");
            ResponseElectronSection s2 = back != null ? back.ElectronSectionFor(other, out why) : null;
            Console.WriteLine("   (Д2) капсула PET 0.1 мм вместо {0}: раздел {1}, причина {2}", ResponseElectronSection.CapsuleText(geometry), s2 != null ? "ВЗЯТ" : "не взят", why);
            Check(s2 == null && why == ElectronSectionRefusal.OtherCapsule, "(Д2) раздел другой капсулы не отвергнут");

            // (Д2) Прежняя физика раздела.
            if (back != null && back.ElectronSection != null)
            {
                string stamp = back.ElectronSection.Stamp;
                back.ElectronSection.Stamp = stamp.Replace("esec=" + ResponseElectronSection.SectionPhysics.ToString(CultureInfo.InvariantCulture) + ";", "esec=1;");
                ResponseElectronSection s3 = back.ElectronSectionFor(geometry, out why);
                Console.WriteLine("   (Д2) клеймо с esec=1: раздел {0}, причина {1}", s3 != null ? "ВЗЯТ" : "не взят", why);
                Check(s3 == null && why == ElectronSectionRefusal.OtherPhysics, "(Д2) раздел прежней физики не отвергнут");
                back.ElectronSection.Stamp = stamp;
            }

            // (Ф5) Сцена изотропного поля.
            GeometryModel iso = geometry.Clone();
            iso.Scene = GeometrySceneKind.Iso;
            ResponseElectronSection s4 = back != null ? back.ElectronSectionFor(iso, out why) : null;
            Check(s4 == null && why == ElectronSectionRefusal.IsoScene, "(Ф5) раздел взят у сцены изотропного поля");
            Console.WriteLine("   (Ф5) сцена поля: причина {0}", why);

            // (Р2) Порча содержимого раздела — снимается только раздел.
            byte[] bytes = File.ReadAllBytes(full);
            byte[] spoiled = (byte[])bytes.Clone();
            spoiled[bytes.Length - 200] ^= 0x5A;
            string bad = Path.Combine(dir, "bad.rmx");
            File.WriteAllBytes(bad, spoiled);
            ResponseMatrix b2 = ResponseMatrix.Load(bad, out r, out f);
            Console.WriteLine("   (Р2) байт раздела испорчен: матрица {0}, годна {1}, раздел {2}, причина «{3}»",
                              b2 != null ? "прочитана" : "НЕТ", b2 != null && b2.IsValidFor(geometry),
                              b2 != null && b2.ElectronSection != null ? "ЕСТЬ" : "снят", b2 != null ? b2.ElectronSectionDamage : "");
            Check(b2 != null && b2.IsValidFor(geometry) && b2.ElectronSection == null && b2.ElectronSectionDamage != null,
                  "(Р2) порча раздела не сняла только раздел");

            // (Р2) Обрубок файла.
            byte[] cut = new byte[bytes.Length - 7];
            Array.Copy(bytes, cut, cut.Length);
            File.WriteAllBytes(bad, cut);
            ResponseMatrix b3 = ResponseMatrix.Load(bad, out r, out f);
            Console.WriteLine("   (Р2) файл обрублен на 7 байт: матрица {0}, годна {1}, раздел {2}, причина «{3}»",
                              b3 != null ? "прочитана" : "НЕТ", b3 != null && b3.IsValidFor(geometry),
                              b3 != null && b3.ElectronSection != null ? "ЕСТЬ" : "снят", b3 != null ? b3.ElectronSectionDamage : "");
            Check(b3 != null && b3.IsValidFor(geometry) && b3.ElectronSection == null && b3.ElectronSectionDamage != null,
                  "(Р2) обрубок раздела уронил матрицу или раздел остался");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    static bool Equal(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary>
    /// (`Д3`) Сложение раскладок по каналам: первое слагаемое — семь каналов (член без
    /// беты), второе — восемь. Σ каналов суммы обязана равняться сумме лент слагаемых.
    /// </summary>
    static void ChannelsControl()
    {
        Console.WriteLine("== контроль сложения 7 и 8 каналов (Д3)");
        const int Bins = 16;
        double[][] seven = Rows(7, Bins, 1.0), eight = Rows(8, Bins, 2.0);
        double expected = Total(seven) + Total(eight);

        MethodInfo add = null;
        foreach (Type t in typeof(FsaResult).Assembly.GetTypes())
        {
            MethodInfo m = t.GetMethod("AddChannels", BindingFlags.Static | BindingFlags.NonPublic);
            if (m != null && m.GetParameters().Length == 3)
            {
                add = m;
            }
        }

        Check(add != null, "нет FsaResult.AddChannels");
        if (add != null)
        {
            object[] argsA = { Copy(seven), eight, Bins };
            add.Invoke(null, argsA);
            var sum = (double[][])argsA[0];
            Console.WriteLine("   FsaResult.AddChannels: каналов {0}, Σ {1} (ждём 8 и {2})", sum.Length, Total(sum), expected);
            Check(sum.Length == 8 && Math.Abs(Total(sum) - expected) < 1e-9, "(Д3) AddChannels теряет восьмой канал");
        }

        MethodInfo into = typeof(FsaPresentationBuilder).GetMethod("AddChannelsInto", BindingFlags.Static | BindingFlags.NonPublic);
        Check(into != null, "нет FsaPresentationBuilder.AddChannelsInto");
        if (into != null)
        {
            var target = new FsaStackLayer { ChannelCurves = Copy(seven) };
            var source = new FsaStackLayer { ChannelCurves = eight };
            into.Invoke(null, new object[] { target, source, Bins });
            Console.WriteLine("   FsaPresentationBuilder.AddChannelsInto: каналов {0}, Σ {1} (ждём 8 и {2})",
                              target.ChannelCurves.Length, Total(target.ChannelCurves), expected);
            Check(target.ChannelCurves.Length == 8 && Math.Abs(Total(target.ChannelCurves) - expected) < 1e-9,
                  "(Д3) AddChannelsInto теряет восьмой канал");
        }
    }

    static double[][] Rows(int channels, int bins, double value)
    {
        var rows = new double[channels][];
        for (int c = 0; c < channels; c++)
        {
            rows[c] = new double[bins];
            for (int i = 0; i < bins; i++) rows[c][i] = value * (c + 1) + i;
        }

        return rows;
    }

    static double[][] Copy(double[][] rows)
    {
        var copy = new double[rows.Length][];
        for (int c = 0; c < rows.Length; c++) copy[c] = (double[])rows[c].Clone();
        return copy;
    }

    static double Total(double[][] rows)
    {
        double s = 0.0;
        foreach (double[] row in rows) if (row != null) foreach (double v in row) s += v;
        return s;
    }

    // ------------------------------------------------------------------ формы

    /// <summary>
    /// (`Ф3` критика физики 30.10.2026) Опора суда форм — средние энергии ветвей по таблицам
    /// LNHB/DDEP (`https://www.lnhb.fr/nuclides/&lt;нуклид&gt;_tables.pdf`; Lu-176 — ENSDF,
    /// NDS 107 (2006) 791), а не средние `nucdb`: те посчитаны LOGFT в тех же приближениях,
    /// и суд по ним был круговым. Нуклид, граница LNHB, выход, средняя.
    /// </summary>
    static readonly string[] LnhbMeans =
    {
        "40K,1310.91,89.56,582.898", "137CS,513.97,94.57,173.67", "137CS,1175.63,5.43,284.9",
        "90SR,545.9,100,196.0", "90Y,2278.7,99.983,926.7", "210BI,1161.2,100,317.0",
        "214BI,3270,19.67,1270", "214BI,1540,17.494,540", "214BI,1506,17.1,526", "214BI,1423,8.147,493",
        "214BI,1892,7.45,685", "214BI,1066,5.642,353", "214PB,667,46.52,207", "214PB,724,41.09,227",
        "214PB,1019,9.2,337", "208TL,1801.3,49.2,649.5", "208TL,1290.5,24.1,441.5", "208TL,1523.9,22.1,535.4",
        "212BI,2252.1,55.31,834.2", "212PB,330.4,80.8,92.7", "212PB,569,14.4,170.6",
        "228AC,1154.8,31,381.1", "228AC,1727.7,12.4,605.7", "228AC,1000.8,6.67,323.2",
        "234PAm1,2269,97.599,820.5", "234PAm1,1224,1.006,405.6", "234TH,198,77.8,53",
        "60CO,317.32,99.88,95.6", "131I,606.3,89.4,191.59", "22NA,546.44,90.3,215.62", "176LU,593.2,99.61,182.32",
        "211PB,1367,91.28,470.9",
    };

    /// <summary>(`Ф4`) Опорные линии электронов конверсии по LNHB: нуклид, оболочка, кэВ, % на распад.</summary>
    static readonly string[] LnhbConversion =
    {
        "137CS,K,624.216,7.78", "137CS,L,655.668,1.401", "137CS,M,660.364,0.2992",
        "207BI,K,481.694,1.548", "207BI,K,975.655,7.11", "207BI,L,1047.798,1.84",
        "214PB,K,151.471,5.26", "214PB,K,204.698,7.22", "214PB,K,261.406,9.26",
        "212PB,K,148.106,30.7", "208TL,K,189.36,2.86", "208TL,K,422.73,1.88",
    };

    static void Shapes(string[] nuclides)
    {
        var csv = shapeCsv != null ? new StringBuilder("nuclide,branch,form,t_kev,density\n") : null;
        Console.WriteLine("== формы бета-спектров (FsaBetaLibrary) против средних LNHB (Ф3) и линии конверсии (Ф4)");
        foreach (string nucid in nuclides)
        {
            List<FsaBeta> branches = FsaBetaLibrary.Branches(nucid);
            foreach (FsaBeta b in branches)
            {
                if (b.IsLine)
                {
                    continue;
                }

                double mean;
                double norm = FsaBetaLibrary.Integral(b, out mean);
                Check(norm > 0.0 && mean > 0.0 && mean < b.EndpointKev, nucid + ": форма пуста или средняя вне (0, E0)");
                string reference = null;
                double lnhb = double.NaN, intensity = 0.0;
                foreach (string row in LnhbMeans)
                {
                    string[] p = row.Split(',');
                    double e0 = double.Parse(p[1], CultureInfo.InvariantCulture);
                    if (string.Equals(p[0], nucid, StringComparison.OrdinalIgnoreCase) && Math.Abs(e0 - b.EndpointKev) <= Math.Max(15.0, 0.02 * e0))
                    {
                        reference = row;
                        intensity = double.Parse(p[2], CultureInfo.InvariantCulture);
                        lnhb = double.Parse(p[3], CultureInfo.InvariantCulture);
                    }
                }

                double dev = reference != null ? mean / lnhb - 1.0 : double.NaN;
                // Порог: форма таблицей — 0.5 %; кодом — 3 % у ветвей от 5 %, 6 % у слабых.
                double limit = b.ShapeId > 0 ? 0.005 : intensity >= 5.0 ? 0.03 : 0.06;
                Console.WriteLine("   {0,-8} {1} E0 {2,7:F1} кэВ, выход {3,7:G4} %, {4,-16} форма {5,-10}: средняя {6,6:F1}, LNHB {7,6:F1} ({8}){9}",
                                  nucid, b.Positron ? "β⁺" : "β⁻", b.EndpointKev, b.Intensity, b.Transition,
                                  b.ShapeId > 0 ? "табл.#" + b.ShapeId : b.UniqueOrder == 0 ? "разреш." : "уник." + b.UniqueOrder,
                                  mean, lnhb, reference != null ? (100.0 * dev).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " %" : "нет опоры",
                                  reference != null && Math.Abs(dev) > limit ? "  ⛔ порог " + (100.0 * limit).ToString("F1", CultureInfo.InvariantCulture) + " %" : "");
                if (reference != null)
                {
                    Check(Math.Abs(dev) <= limit, nucid + " " + b.EndpointKev.ToString("F0", CultureInfo.InvariantCulture)
                                                  + ": средняя формы дальше порога от LNHB");
                }

                if (csv != null)
                {
                    for (int i = 1; i < 1000; i++)
                    {
                        double t = b.EndpointKev * i / 1000.0;
                        csv.Append(nucid).Append(',').Append(b.EndpointKev.ToString("F1", CultureInfo.InvariantCulture)).Append(",own,")
                           .Append(t.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                           .Append((FsaBetaLibrary.Density(b, t) / norm).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                    }
                }
            }

            // (Ф4) Линии конверсии против LNHB: энергия ±1.5 кэВ, выход ±10 %.
            foreach (string row in LnhbConversion)
            {
                string[] p = row.Split(',');
                if (!string.Equals(p[0], nucid, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                double e = double.Parse(p[2], CultureInfo.InvariantCulture), y = double.Parse(p[3], CultureInfo.InvariantCulture);
                FsaBeta best = null;
                foreach (FsaBeta b in branches)
                {
                    if (b.IsLine && b.Shell == p[1] && Math.Abs(b.EndpointKev - e) <= 1.5
                        && (best == null || Math.Abs(b.EndpointKev - e) < Math.Abs(best.EndpointKev - e)))
                    {
                        best = b;
                    }
                }

                Console.WriteLine("   {0,-8} конверсия {1} {2,8:F2} кэВ {3,7:G4} %: у нас {4}", nucid, p[1], e, y,
                                  best != null ? best.EndpointKev.ToString("F2", CultureInfo.InvariantCulture) + " кэВ "
                                                 + best.Intensity.ToString("G4", CultureInfo.InvariantCulture) + " % ("
                                                 + (100.0 * (best.Intensity / y - 1.0)).ToString("+0.0;-0.0", CultureInfo.InvariantCulture) + " %)"
                                               : "НЕТ");
                Check(best != null && Math.Abs(best.Intensity / y - 1.0) <= 0.10, nucid + " конверсия " + p[1] + " " + p[2] + ": дальше 10 % от LNHB");
            }

            string note = FsaBetaLibrary.Note(nucid);
            Console.WriteLine("      разбор: {0}", note.Length > 700 ? note.Substring(0, 700) + "…" : note);
        }

        if (csv != null)
        {
            File.WriteAllText(shapeCsv, csv.ToString());
            Console.WriteLine("  формы — " + shapeCsv);
        }
    }

    // ------------------------------------------------------------------ перенос

    /// <summary>
    /// (`Ф9`) Пучок электронов по нормали на ГОЛЫЙ кристалл сцены (обвязка снята): доля
    /// отражённых по числу (вылеты занесённого электрона из кристалла на вход, `η`) и доля
    /// энергии, НЕ оставленной в кристалле (верхняя граница `η_E`: в неё входит и ушедшее
    /// тормозное). Опора — эмпирика Tabata (печать — журнал).
    /// </summary>
    static void Backscatter(GeometryModel scene, int n)
    {
        GeometryModel g = scene.Clone();
        g.FrontReflectorThickness = g.SideReflectorThickness = 0.0;
        g.FrontGapThickness = g.SideGapThickness = 0.0;
        g.FrontCladdingThickness = g.SideCladdingThickness = 0.0;
        g.MountingThickness = 0.0;
        g.SourceType = GeometrySourceType.Point;
        g.Scene = GeometrySceneKind.None;
        g.PointDistance = 10.0;
        ApplyCapsule(g, "none");
        // (`AMBER274`, физика 29) `--crystal=` — вещество пластины вместо кристалла сцены
        // (CsI/NaI — плотности и состав как у NIST G4_CESIUM_IODIDE/G4_SODIUM_IODIDE, Al — 2.699);
        // `--slab` — толстая пластина 80×80×20 мм (как у опоры Geant4 `g4eta`: ≫ пробега до 2 МэВ).
        if (backscatterCrystal != null)
        {
            g.Crystal = BackscatterMaterial(backscatterCrystal);
        }

        if (backscatterSlab)
        {
            g.Shape = CrystalShape.Box;
            g.CrystalBoxX = 80.0;
            g.CrystalBoxY = 80.0;
            g.CrystalBoxZ = 20.0;
        }

        double[] energies = backscatterEnergies ?? new[] { 300.0, 500.0, 1000.0, 2000.0 };
        Console.WriteLine("== обратное рассеяние электронов (Ф9): голый кристалл {0} ({1}, {2:F3} г/см³{3}), пучок по нормали, {4} историй; шаг {5} (кристалл {8}), emix {6}, mott {9}, eref {10} (части {11}), dpwa {12}, отсечка {7}",
                          g.Name, g.Crystal != null ? g.Crystal.Name : "?", g.Crystal != null ? g.Crystal.Density : 0.0,
                          backscatterSlab ? ", пластина 80×80×20 мм" : "", n,
                          stepFraction > 0 ? stepFraction.ToString("R", CultureInfo.InvariantCulture) : "умолч.",
                          emix < 0 ? "умолч." : emix.ToString(CultureInfo.InvariantCulture),
                          cutoffDeg > 0 ? cutoffDeg.ToString("R", CultureInfo.InvariantCulture) : "умолч.",
                          crystalStepFraction > 0 ? crystalStepFraction.ToString("R", CultureInfo.InvariantCulture)
                              : stepFraction > 0 ? stepFraction.ToString("R", CultureInfo.InvariantCulture) : "умолч.",
                          mott < 0 ? "умолч." : mott.ToString(CultureInfo.InvariantCulture),
                          eref < 0 ? "умолч." : eref.ToString(CultureInfo.InvariantCulture),
                          refineParts < 0 ? "умолч." : refineParts.ToString(CultureInfo.InvariantCulture),
                          dpwa < 0 ? "умолч." : dpwa.ToString(CultureInfo.InvariantCulture));
        int chunks = Math.Max(1, Math.Min(64, Environment.ProcessorCount));
        foreach (double te in energies)
        {
            long entered = 0, escapes = 0, histories = 0, hard = 0, steps = 0, caps = 0, full = 0;
            long mottRejected = 0, mottExceeded = 0, mottCap = 0, empty = 0, recomputed = 0;
            double sumDep = 0.0;
            object gate = new object();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            System.Threading.Tasks.Parallel.For(0, chunks, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = chunks }, c =>
            {
                int m = n / chunks + (c < n % chunks ? 1 : 0);
                EfficiencySimulator sim = Make(g, new ResponseMatrixOptions(), te, 11 + 1000 * c);
                sim.CapsuleAir = false;
                double z = SourceZ(sim);
                var stats = new EfficiencySimulator.ElectronSourceStats();
                double dep = 0.0;
                long whole = 0;
                for (int i = 0; i < m; i++)
                {
                    double light;
                    double d = sim.ElectronBeamHistoryForProbe(te, z, stats, out light);
                    dep += d;
                    if (d >= te - 2.0)
                    {
                        whole++;
                    }
                }

                lock (gate)
                {
                    entered += stats.Entered;
                    escapes += sim.CountLayerEscapesCarried;
                    histories += m;
                    sumDep += dep;
                    hard += sim.CountCrystalHardCollisions;
                    steps += sim.CountCrystalSteps;
                    caps += sim.CountTransportStepCap;
                    mottRejected += sim.CountMottRejected;
                    mottExceeded += sim.CountMottMajorantExceeded;
                    mottCap += sim.CountMottTriesCap;
                    empty += sim.CountMajorantEmpty;
                    recomputed += sim.CountShareRecomputed;
                    full += whole;
                }
            });
            double eta = entered > 0 ? (double)escapes / entered : double.NaN;
            Console.WriteLine("   T {0,6:F0} кэВ: вошло {1}/{2}, отражено (по числу) η = {3:F4} ± {4:F4}; не оставлено в кристалле {5:F4}; полное (≥ T − 2 кэВ) {10:F4} ± {11:F4}; шагов на историю {6:F1}, жёстких {7:F1}, упоров {8}; Мотт: отвергнуто кандидатов {12:F1} на историю, выше мажоранты {13}, предел попыток {14}; пустых мажоранты шага {15:F1} на историю, пересчётов долей {16}; {9:F1} мкс/ист.",
                              te, entered, histories, eta, Math.Sqrt(eta * (1.0 - eta) / Math.Max(1, entered)), 1.0 - sumDep / (histories * te),
                              (double)steps / histories, (double)hard / histories, caps,
                              watch.Elapsed.TotalSeconds * 1e6 * chunks / histories,
                              (double)full / histories, Math.Sqrt((double)full / histories * (1.0 - (double)full / histories) / histories),
                              (double)mottRejected / histories, mottExceeded, mottCap, (double)empty / histories, recomputed);
        }
    }

    /// <summary>
    /// (`AMBER274`, физика 30, Ф5/Н3 критиков) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: строки позитрона в образе бет не растягиваются.
    /// Синтетический раздел: узлы 300…1000 кэВ через 100, бин 2 кэВ; строка электрона узла T — δ в T; строка
    /// позитрона — δ в T + 1022 (обе аннигиляционные поглощены) и δ в 511 (один квант ушёл), поровну. Спектр —
    /// плоский до 1000 кэВ. Образ позитрона (`Accumulate(…, positron: true)`) обязан держать δ 511 в ±1 бин (доля
    /// площади ≥ 0.999 половины); прежнее поведение — те же строки как строки ЭЛЕКТРОНА (растяжение): 511 размазан.
    /// </summary>
    static void PositronMorphControl()
    {
        double[] e = { 300, 400, 500, 600, 700, 800, 900, 1000 };
        int bins = 1200;
        var ele = new float[e.Length][];
        var pos = new float[e.Length][];
        for (int k = 0; k < e.Length; k++)
        {
            ele[k] = new float[bins];
            pos[k] = new float[bins];
            ele[k][(int)Math.Round(e[k] / 2.0)] = 1f;
            pos[k][(int)Math.Round((e[k] + 1022.0) / 2.0)] = 0.5f;
            pos[k][(int)Math.Round(511.0 / 2.0)] = 0.5f;
        }

        var sec = new ResponseElectronSection { BinKev = 2.0, Energies = e, Electron = ele, Positron = pos };
        double[] w = sec.NodeWeights(t => t < 1000.0 ? 1.0 : 0.0, 1000.0);
        var imgPos = new double[bins];
        sec.Accumulate(imgPos, w, true, 1.0);
        var asElectron = new ResponseElectronSection { BinKev = 2.0, Energies = e, Electron = pos, Positron = pos };
        var imgOld = new double[bins];
        asElectron.Accumulate(imgOld, w, false, 1.0);
        Func<double[], double> near511 = img =>
        {
            double inPeak = 0.0, total = 0.0;
            for (int b = 0; b < img.Length; b++)
            {
                total += img[b];
                if (Math.Abs(b * 2.0 - 511.0) <= 3.0) inPeak += img[b];
            }

            return total > 0.0 ? inPeak / (0.5 * total) : 0.0;
        };
        double fNew = near511(imgPos), fOld = near511(imgOld);
        double sum = 0.0, sumOld = 0.0;
        foreach (double v in imgPos) sum += v;
        foreach (double v in imgOld) sumOld += v;
        Console.WriteLine("== Ф5: строки позитрона без растяжения (синтетика, {0} узлов, растяжение раздела {1})",
                          e.Length, ResponseElectronSection.ScaleMorph ? "ВКЛ" : "ВЫКЛ");
        Console.WriteLine("   доля δ 511 кэВ в ±3 кэВ: позитрон (физика 30) {0:F4}, те же строки растяжением (как было) {1:F4}; площадь {2:F6} / {3:F6}",
                          fNew, fOld, sum, sumOld);
        Check(fNew >= 0.999, "образ позитрона держит 511 в ±3 кэВ: " + fNew.ToString("F4", CultureInfo.InvariantCulture));
        Check(!ResponseElectronSection.ScaleMorph || fOld < 0.9, "положительный контроль: растяжение размазывает 511 (" + fOld.ToString("F4", CultureInfo.InvariantCulture) + ")");
        Check(Math.Abs(sum - sumOld) <= 1e-9 * Math.Max(1.0, sum), "площади образов равны");
    }

    /// <summary>(`AMBER274`, физика 29) Вещество пластины `--crystal=`: CsI, NaI, Al.</summary>
    static GeometryMaterial BackscatterMaterial(string name)
    {
        var m = new GeometryMaterial();
        switch (name.ToUpperInvariant())
        {
            case "CSI":
                m.Name = "Cesium iodide"; m.Density = 4.51; m.Fractions[53] = 0.488451; m.Fractions[55] = 0.511549;
                break;
            case "NAI":
                m.Name = "Sodium iodide"; m.Density = 3.667; m.Fractions[11] = 0.153373; m.Fractions[53] = 0.846627;
                break;
            case "AL":
                m.Name = "Aluminum"; m.Density = 2.699; m.Fractions[13] = 1.0;
                break;
            case "FE":
                // (`screen29`) железо — как NIST G4_Fe (7.874 г/см³): средний Z (сталь обвязки).
                m.Name = "Iron"; m.Density = 7.874; m.Fractions[26] = 1.0;
                break;
            default:
                throw new ArgumentException("--crystal= знает CsI, NaI, Al, Fe: " + name);
        }

        return m;
    }

    /// <summary>
    /// Сверка с опытом и Geant4: строки электрона сцены без матрицы (настройки — умолчания
    /// <see cref="ResponseMatrixOptions"/>, шкала — оставленная энергия), свёртка с
    /// формами бета-ветвей и линиями конверсии нуклидов (выход — из базы) — спектр на
    /// распад цепочки, где каждый нуклид списка — с весом 1 (равновесие). CSV: кэВ (центр
    /// бина), доля на бин на распад.
    /// </summary>
    static void Direct(GeometryModel g, string nuclides, double[] grid, int n, int threads, string csvPath)
    {
        var options = new ResponseMatrixOptions();
        double bin = options.BinKev;
        var section = new ResponseElectronSection
        {
            Stamp = "direct:" + g.Name + ":" + ResponseElectronSection.CapsuleText(g),
            BinKev = bin,
            Energies = grid,
            Electron = new float[grid.Length][],
        };
        var entered = new double[grid.Length];
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // (`З2`) `--light` — шкала строк, как у раздела: свет истории / свет пика 661.657 на кэВ.
        double lightPerKev = 0.0;
        if (directLight)
        {
            EfficiencySimulator anchor = Make(g, options, 661.657, 7000);
            anchor.Histories = 20000;
            double anchorError;
            anchor.Response(661.657, bin, out anchorError);
            lightPerKev = anchor.LastPhotonLightScale;
        }

        System.Threading.Tasks.Parallel.For(0, grid.Length, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
        {
            EfficiencySimulator sim = Make(g, options, grid[i], 7001 + i);
            sim.Histories = n;
            EfficiencySimulator.ElectronSourceStats st;
            double[] row = sim.ElectronSourceResponse(grid[i], false, bin, lightPerKev, out st);
            section.Electron[i] = ResponseElectronSection.Trim(row);
            entered[i] = (double)st.Entered / st.Histories;
        });

        // (`AMBER274`, физика 28) Строки линий конверсии своих нуклидов на ТОЧНОЙ энергии (как
        // у раздела физики 28); `--lines=none` — прежняя интерполяция узлов (плечо сравнения).
        if (!noConversion && !string.Equals(linesSpec, "none", StringComparison.OrdinalIgnoreCase))
        {
            var own = new List<double>();
            foreach (string nucid in nuclides.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
                {
                    if (b.IsLine) own.Add(b.EndpointKev);
                }
            }

            double[] lines = ResponseElectronSection.CleanLines(own.ToArray(), grid);
            if (lines != null)
            {
                section.LineEnergies = lines;
                section.LineElectron = new float[lines.Length][];
                section.Stamp += ":lines" + lines.Length.ToString(CultureInfo.InvariantCulture);
                System.Threading.Tasks.Parallel.For(0, lines.Length, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = threads }, j =>
                {
                    EfficiencySimulator sim = Make(g, options, lines[j], 8001 + j);
                    sim.Histories = n;
                    EfficiencySimulator.ElectronSourceStats st;
                    double[] row = sim.ElectronSourceResponse(lines[j], false, bin, lightPerKev, out st);
                    section.LineElectron[j] = ResponseElectronSection.Trim(row);
                });
                Console.WriteLine("   строк линий конверсии на точной энергии: {0}", lines.Length);
            }
        }
        Console.WriteLine("== сверка: сцена {0}, {1} узлов по {2} историй, {3:F1} с; капсула {4}; сдвиг точки {5} мм{6}",
                          g.Name, grid.Length, n, watch.Elapsed.TotalSeconds, ResponseElectronSection.CapsuleText(g),
                          g.SourceCapsuleOffset, noConversion ? "; без конверсии" : "");
        for (int i = 0; i < grid.Length; i += Math.Max(1, grid.Length / 10))
        {
            double area = 0.0;
            foreach (float v in section.Electron[i]) area += v;
            Console.WriteLine("   T {0,7:F1} кэВ: вошло {1:F4}, площадь строки {2:F4}", grid[i], entered[i], area);
        }

        // (`AMBER274`, `phys29b`) `--dump-rows=<файл>` — строки узлов (и линий) и плотности ветвей
        // беты на сетке 0.5 кэВ: веса промежутка у границы беты разбираются вне прогона.
        if (dumpRows != null)
        {
            using (var wr = new StreamWriter(dumpRows))
            {
                for (int i = 0; i < grid.Length; i++)
                {
                    wr.Write("ROW " + grid[i].ToString("R", CultureInfo.InvariantCulture));
                    foreach (float v in section.Electron[i]) wr.Write(" " + v.ToString("R", CultureInfo.InvariantCulture));
                    wr.WriteLine();
                }

                for (int j = 0; section.LineEnergies != null && j < section.LineEnergies.Length; j++)
                {
                    wr.Write("LINE " + section.LineEnergies[j].ToString("R", CultureInfo.InvariantCulture));
                    foreach (float v in section.LineElectron[j]) wr.Write(" " + v.ToString("R", CultureInfo.InvariantCulture));
                    wr.WriteLine();
                }

                foreach (string nucid in nuclides.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
                    {
                        double mean;
                        double norm = b.IsLine ? 1.0 : FsaBetaLibrary.Integral(b, out mean);
                        wr.Write("BRANCH " + nucid + " " + (b.IsLine ? "L" : "B") + " "
                                 + b.EndpointKev.ToString("R", CultureInfo.InvariantCulture) + " "
                                 + b.Intensity.ToString("R", CultureInfo.InvariantCulture) + " "
                                 + norm.ToString("R", CultureInfo.InvariantCulture));
                        if (!b.IsLine)
                        {
                            for (double t = 0.25; t < b.EndpointKev; t += 0.5)
                            {
                                wr.Write(" " + FsaBetaLibrary.Density(b, t).ToString("R", CultureInfo.InvariantCulture));
                            }
                        }

                        wr.WriteLine();
                    }
                }
            }

            Console.WriteLine("  строки и плотности — " + dumpRows);
        }

        string baseStamp = section.Stamp;
        string[] modes = (morphModes ?? (ResponseElectronSection.ScaleMorph ? "1" : "0"))
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        bool defaultMode = ResponseElectronSection.ScaleMorph;
        foreach (string modeText in modes)
        {
        int mode = int.Parse(modeText, CultureInfo.InvariantCulture);
        ResponseElectronSection.ScaleMorph = mode != 0;
        section.Stamp = baseStamp + ":morph" + modeText;     // кэш весов FSA — по клейму раздела
        Console.WriteLine("   -- веса узлов: режим {0} ({1})", mode, mode != 0 ? "растягивающая интерполяция строк" : "прежняя шапочка");
        int length = (int)Math.Ceiling(grid[grid.Length - 1] * 1.5 / bin) + 2;
        double[] total = new double[length];
        foreach (string nucid in nuclides.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            double[] part = new double[length];
            double sumBeta = 0.0, sumCe = 0.0;
            foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
            {
                // β⁺ — кинетикой строками электрона, как у разбора по умолчанию (`SamplePositronAtStop` ВЫКЛ).
                if (b.IsLine && noConversion) continue;
                double[] w = (double[])FsaBetaLibrary.Weights(section, b).Clone();
                double y = b.Intensity / 100.0;
                for (int k = 0; k < w.Length; k++) w[k] *= y;
                double area = section.Accumulate(part, w, false, 1.0);
                if (b.IsLine) sumCe += area; else sumBeta += area;
            }

            Console.WriteLine("   {0}: регистраций на распад — бета {1:E4}, конверсия {2:E4}, всего {3:E4}", nucid, sumBeta, sumCe, sumBeta + sumCe);
            for (int b2 = 0; b2 < length; b2++) total[b2] += part[b2];

            // Гамма и рентген распада (линии образа FSA, выше 30 кэВ) — фотонным откликом
            // той же сцены: у пластика это комптон, у бета-съёмки — заметная доля счёта.
            if (withGamma)
            {
                double gsum = 0.0;
                int index = 9000;
                foreach (double[] line in FsaSampleLibrary.DecayLines(nucid, new FsaSampleLibrary.Report()))
                {
                    if (line[0] < 30.0 || line[1] < 0.5) continue;
                    EfficiencySimulator gs = Make(g, options, line[0], index++);
                    gs.Histories = Math.Max(20000, n / 2);
                    double err;
                    double[] row = gs.Response(line[0], bin, out err);
                    for (int b2 = 0; b2 < row.Length && b2 < length; b2++)
                    {
                        double v = row[b2] * line[1] / 100.0;
                        total[b2] += v;
                        gsum += v;
                    }
                }

                Console.WriteLine("   {0}: регистраций гаммы и рентгена на распад {1:E4}", nucid, gsum);
            }
        }

        if (csvPath != null)
        {
            var sb = new StringBuilder("kev,per_decay\n");
            for (int b2 = 0; b2 < length; b2++)
            {
                if (total[b2] == 0.0) continue;
                sb.Append((b2 * bin).ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                  .Append(total[b2].ToString("R", CultureInfo.InvariantCulture)).Append("\n");
            }

            string path = modes.Length > 1
                ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(csvPath)),
                               Path.GetFileNameWithoutExtension(csvPath) + "_morph" + modeText + Path.GetExtension(csvPath))
                : csvPath;
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine("  спектр — " + path);
        }
        }

        ResponseElectronSection.ScaleMorph = defaultMode;
        section.Stamp = baseStamp;
    }

    /// <summary>
    /// (`AMBER274`, физика 28) Моноэнергетический электрон из точки сцены — повтор замера
    /// критика несходимости Cs-137 (таблицы t1a/t2, 30.10.2026) В ДЕРЕВЕ: (1) оставленная в
    /// кристалле энергия на ИСПУЩЕННЫЙ электрон (`ElectronSourceResponse`, бин 1 кэВ,
    /// изотропно); (2) энергия первичного на ВХОДЕ в кристалл (`CapsuleExit` +
    /// `CarriedElectronEnters`, только вперёд uz &gt; 0, норма — на испущенный, ×0.5) и её
    /// средняя. Полосы — те же, что у критика. Ключ `--estr=0|1` — плечо без/со страгглингом.
    /// </summary>
    static void Mono(GeometryModel g, string energies, int n)
    {
        var options = new ResponseMatrixOptions();
        BindingFlags nf = BindingFlags.Instance | BindingFlags.NonPublic;
        Type simType = typeof(EfficiencySimulator);
        int[][] depBands = { new[] { 1, 40 }, new[] { 40, 100 }, new[] { 100, 200 }, new[] { 200, 300 }, new[] { 300, 400 },
                             new[] { 400, 500 }, new[] { 500, 600 }, new[] { 600, 700 }, new[] { 1, 700 } };
        int[][] entBands = { new[] { 0, 100 }, new[] { 100, 200 }, new[] { 200, 300 }, new[] { 300, 400 }, new[] { 400, 500 },
                             new[] { 500, 600 }, new[] { 600, 700 }, new[] { 0, 700 } };
        foreach (string part in energies.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            double te = double.Parse(part, CultureInfo.InvariantCulture);
            EfficiencySimulator sim = Make(g, options, te, 7001);
            sim.Histories = n;
            EfficiencySimulator.ElectronSourceStats st;
            double[] row = sim.ElectronSourceResponse(te, false, 1.0, 0.0, out st);
            Console.WriteLine("== моно {0} кэВ, сцена {1}: страгглинг {2}, {3} историй, вошло {4:E4}, {5:F1} с; шагов с розыгрышем {6}",
                              te, g.Name, sim.ElectronStraggling ? "ВКЛ" : "ВЫКЛ", n, (double)st.Entered / st.Histories, st.Seconds,
                              sim.CountStragglingSteps);
            Console.WriteLine("   оставлено в кристалле, на испущенный:");
            foreach (int[] band in depBands)
            {
                double v = 0.0;
                for (int b = band[0]; b < band[1] && b < row.Length; b++) v += row[b];
                Console.WriteLine("     {0,4}–{1,-4} {2:E4}", band[0], band[1], v);
            }

            EfficiencySimulator s2 = Make(g, options, te, 9001);
            s2.EnsureBuiltForProbe();
            object source = simType.GetField("source", nf).GetValue(s2);
            double z0 = (double)source.GetType().GetField("z", nf).GetValue(source);
            MethodInfo forget = simType.GetMethod("ForgetRay", nf);
            MethodInfo at = simType.GetMethod("At", nf);
            MethodInfo capsule = simType.GetMethod("CapsuleExit", nf);
            MethodInfo enters = simType.GetMethod("CarriedElectronEnters", nf);
            FieldInfo pend = simType.GetField("pendCount", nf);
            object push = simType.GetField("pushAnalog", nf).GetValue(s2);
            Type birthType = simType.GetNestedType("ElectronBirth", BindingFlags.NonPublic);
            object given = Enum.Parse(birthType, "Given");
            var rng = new Random(1);
            var hist = new double[4000];
            long entered = 0;
            for (int i = 0; i < n; i++)
            {
                double cz = rng.NextDouble();
                double sz = Math.Sqrt(Math.Max(0, 1 - cz * cz)), phi = 2 * Math.PI * rng.NextDouble();
                double ux = sz * Math.Cos(phi), uy = sz * Math.Sin(phi), uz = cz;
                forget.Invoke(s2, null);
                pend.SetValue(s2, 0);
                double t = (double)capsule.Invoke(s2, new object[] { te, uz });
                object reg = at.Invoke(s2, new object[] { 0.0, 0.0, z0 });
                GeometryMaterial m = reg != null ? (GeometryMaterial)reg.GetType().GetField("Material").GetValue(reg) : null;
                object[] a = { 0.0, 0.0, z0, given, t, ux, uy, uz, m, push, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 };
                if (!(t > 0.0))
                {
                    continue;
                }

                bool ok = (bool)enters.Invoke(s2, a);
                double tIn = (double)a[16];
                if (ok && tIn > 1.0)
                {
                    entered++;
                    int b = (int)tIn;
                    if (b >= 0 && b < hist.Length) hist[b]++;
                }
            }

            double mean = 0, sum = 0;
            for (int b = 0; b < hist.Length; b++) { mean += (b + 0.5) * hist[b]; sum += hist[b]; }
            Console.WriteLine("   вход в кристалл, на испущенный (вперёд {0} из {1}); средняя энергия входа {2:F1} кэВ:",
                              entered, n, sum > 0 ? mean / sum : 0);
            foreach (int[] band in entBands)
            {
                double v = 0.0;
                for (int b = band[0]; b < band[1] && b < hist.Length; b++) v += hist[b];
                Console.WriteLine("     {0,4}–{1,-4} {2:E4}", band[0], band[1], 0.5 * v / n);
            }

            if (monoCsv != null)
            {
                var sb = new StringBuilder("kind,kev,per_emitted\n");
                for (int b = 0; b < row.Length; b++) if (row[b] > 0) sb.Append("dep,").Append(b).Append(',').Append(row[b].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                for (int b = 0; b < hist.Length; b++) if (hist[b] > 0) sb.Append("enter,").Append(b).Append(',').Append((0.5 * hist[b] / n).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                File.WriteAllText(monoCsv + "_" + te.ToString("F0", CultureInfo.InvariantCulture) + ".csv", sb.ToString());
            }
        }
    }


    /// <summary>
    /// (`AMBER274`, физика 28) Перепись линий конверсии `nucdb`: сколько линий выше порога
    /// выхода и в пределах сетки раздела — по всем родителям с гамма-линиями (на сколько строк
    /// «на точной энергии» вырастет раздел).
    /// </summary>
    static void LineCensus(string spec)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var parents = new List<string>();
        string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + db + ";Mode=ReadOnly"))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "select distinct d.parent_nucid from decay_radiations d join nuclides n on n.nucid = d.parent_nucid"
                                      + " where d.type_a = 'G' and n.half_life_sec >= " + spec;
                using (var r = command.ExecuteReader())
                {
                    while (r.Read()) parents.Add(r.GetString(0));
                }
            }
        }

        double[] thresholds = { 0.001, 0.01, 0.1, 0.5, 1.0 };
        var counts = new int[thresholds.Length];
        var energies = new List<double>[thresholds.Length];
        for (int i = 0; i < thresholds.Length; i++) energies[i] = new List<double>();
        foreach (string nucid in parents)
        {
            foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
            {
                if (!b.IsLine || b.EndpointKev < 60.0 || b.EndpointKev > 3600.0) continue;
                for (int i = 0; i < thresholds.Length; i++)
                {
                    if (b.Intensity >= thresholds[i])
                    {
                        counts[i]++;
                        energies[i].Add(b.EndpointKev);
                    }
                }
            }
        }

        Console.WriteLine("== перепись линий конверсии: родителей с гаммой и T½ ≥ {0} с — {1}, {2:F1} с", spec, parents.Count, watch.Elapsed.TotalSeconds);
        for (int i = 0; i < thresholds.Length; i++)
        {
            energies[i].Sort();
            int distinct = 0;
            double last = -1.0;
            foreach (double e in energies[i]) { if (e - last > 0.05) distinct++; last = e; }
            Console.WriteLine("   выход ≥ {0} % на распад, 60…3600 кэВ: линий {1}, различных энергий (0.05 кэВ) {2}", thresholds[i], counts[i], distinct);
        }
    }

    /// <summary>
    /// (`AMBER274`, физика 28) Линии конверсии РЯДОВ библиотеки нуклидов (поставочная
    /// `NuclideDefinition.xml` рядом с пробой): каждый нуклид библиотеки — корень, члены его
    /// ряда (`FsaSampleLibrary.ChainBranches`), линии конверсии членов с выходом (на распад
    /// члена) не ниже <paramref name="minPercent"/> в пределах сетки раздела. Различные
    /// энергии (слияние ближе 0.05 кэВ) — по возрастанию.
    /// </summary>
    static List<double> LibraryLines(double minPercent, double minKev, double maxKev, bool print)
    {
        // (`AMBER305`) Рецепт раздела — один с окном матрицы.
        return ElectronSectionRecipe.LibraryLines(minPercent, minKev, maxKev, print ? (Action<string>)Console.WriteLine : null);
    }

    /// <summary>
    /// (`AMBER274`, физика 28) Энергии строк линий раздела по ключу `--lines=`: `none` — нет;
    /// `lib[:<мин. %>]` (умолчание `lib:1`) — линии рядов библиотеки нуклидов
    /// (<see cref="LibraryLines"/>); иначе — список кэВ через запятую.
    /// </summary>
    static double[] SectionLineList(string spec)
    {
        if (spec == null)
        {
            spec = "lib:1";
        }

        if (string.Equals(spec, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (spec.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
        {
            double min = 1.0;
            int colon = spec.IndexOf(':');
            if (colon > 0)
            {
                min = double.Parse(spec.Substring(colon + 1), CultureInfo.InvariantCulture);
            }

            return LibraryLines(min, 60.0, 3600.0, true).ToArray();
        }

        var list = new List<double>();
        foreach (string p in spec.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            list.Add(double.Parse(p, CultureInfo.InvariantCulture));
        }

        return list.ToArray();
    }

    /// <summary>
    /// (`AMBER274`, физика 28) Пик и сумма строк двух матриц узел к узлу: канал `Peak`
    /// (полное поглощение) — Σ, вся строка — Σ; отношение B/A. Для приёмки «пик фотонов при
    /// страгглинге сдвинут меньше процента».
    /// </summary>
    static void Peaks(string spec)
    {
        string[] p = spec.Split(',');
        MatrixRefusal ra, rb;
        int fa, fb;
        ResponseMatrix a = ResponseMatrix.Load(p[0], out ra, out fa);
        ResponseMatrix b = ResponseMatrix.Load(p[1], out rb, out fb);
        if (a == null || b == null)
        {
            Console.Error.WriteLine("матрица не прочитана: " + ra + " / " + rb);
            failures++;
            return;
        }

        Console.WriteLine("== пик и сумма узлов: A {0}, B {1}", Path.GetFileName(p[0]), Path.GetFileName(p[1]));
        for (int i = 0; i < a.Energies.Length && i < b.Energies.Length; i++)
        {
            double pa = 0, pb = 0, sa = 0, sb = 0;
            foreach (float v in a.ChannelRows[0][i]) pa += v;
            foreach (float v in b.ChannelRows[0][i]) pb += v;
            for (int c = 0; c < a.ChannelRows.Length; c++) foreach (float v in a.ChannelRows[c][i]) sa += v;
            for (int c = 0; c < b.ChannelRows.Length; c++) foreach (float v in b.ChannelRows[c][i]) sb += v;
            Console.WriteLine("   {0,8:F2} кэВ: пик A {1:E5} B {2:E5} B/A {3:F4}; сумма A {4:E5} B {5:E5} B/A {6:F4}",
                              a.Energies[i], pa, pb, pb / pa, sa, sb, sb / sa);
        }
    }

    /// <summary>
    /// (`AMBER274`, физика 28; решение Amber «Портировать на GPU») ПАРИТЕТ ЦП↔GPU РАЗДЕЛА
    /// ЭЛЕКТРОНА ПРОБЫ: те же строки (узлы, строки линий, позитрон) построителем ЦП
    /// (`ResponseMatrixBuilder.BuildElectronSection`) и GPU (`GpuBuild.BuildElectronSection`) при
    /// одинаковом n. По каждой строке: площадь (доля на испущенную), доля вошедших, средний бин;
    /// допуск доли вошедших — 3σ биномиальной ошибки разности; строки свёрнуты гауссом
    /// разрешения (ПШПВ 7 % при 662 кэВ ∝ √E) — χ²/n по бинам с содержимым (дисперсия бина —
    /// суммой квадратов весов свёртки по счёту, вес истории 1) ≤ 1.5. Нарушение — `⛔`.
    /// Матрица-заглушка — клеймо сцены при умолчаниях склада (раздел к ней не пишется).
    /// </summary>
    static void Parity(GeometryModel g, string gpuPath, double[] grid, int n, int seed, bool positron)
    {
        var options = new ResponseMatrixOptions();
        // (`AMBER274`, физика 29) Ключи физики пробы доезжают и до паритета: `--emix=0` — плечо
        // физики 28 (цена GPU до/после), `--estr=0` — физики 27.
        if (emix >= 0)
        {
            options.ElectronMixedTransport = emix != 0;
        }

        if (mott >= 0)
        {
            options.ElectronMottHeavy = mott != 0;
        }

        if (eref >= 0)
        {
            options.ElectronElasticRefine = eref != 0;
        }

        if (dpwa >= 0)
        {
            options.ElectronScreeningDpwa = dpwa != 0;
        }

        if (estr >= 0)
        {
            options.ElectronStraggling = estr != 0;
        }

        var stub = new ResponseMatrix
        {
            Options = options,
            Stamp = ResponseMatrix.ComputeStamp(g, options),
            BinKev = options.BinKev,
        };
        double[] lines = SectionLineList(linesSpec ?? "624.216,655.67,660.36,481.69,553.84,975.65");
        // (`AMBER274` Ф8, `f8cyl`) Строки ветвей — в паритет тем же списком (`--branches=`; умолчание паритета — нет).
        List<ElectronBranchSpec> branchList = SectionBranchList(branchesSpec ?? "none");
        Console.WriteLine("== паритет ЦП↔GPU раздела электрона пробы: сцена {0}, {1} историй на строку, {2} узлов, строк линий {3}, строк ветвей {5}, позитрон {4}",
                          g.Name, n, grid.Length, lines != null ? lines.Length : 0, positron, branchList != null ? branchList.Count : 0);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        ResponseElectronSection cpu = ResponseMatrixBuilder.BuildElectronSection(g, stub, grid, n, seed, positron,
            Environment.ProcessorCount, CancellationToken.None, null, lines, branchList);
        double cpuSeconds = watch.Elapsed.TotalSeconds;
        watch.Restart();
        ResponseElectronSection gpu;
        // `--gpu=cpu` — НУЛЕВОЕ ПЛЕЧО: вторая сторона — тот же ЦП другим зерном (seed + 7). Так
        // меряется, сколько строк выходит за допуск у двух выборок ОДНОГО кода: свёрнутые бины
        // коррелированы, и χ²/n по ним шумнее, чем по независимым.
        bool nullArm = string.Equals(gpuPath, "cpu", StringComparison.OrdinalIgnoreCase);
        if (nullArm)
        {
            gpu = ResponseMatrixBuilder.BuildElectronSection(g, stub, grid, n, seed + 7, positron,
                Environment.ProcessorCount, CancellationToken.None, null, lines, branchList);
        }
        else
        {
            using (var dev = new RmGpu(gpuPath ?? RmGpu.DefaultPath, RmGpu.DefaultStackBytes))
            {
                gpu = GpuBuild.BuildElectronSection(dev, g, stub, grid, n, seed, positron, lines, branchList,
                                                    CancellationToken.None, null);
            }
        }

        double gpuSeconds = watch.Elapsed.TotalSeconds;
        Console.WriteLine("   ЦП {0:F1} с, {3} {1:F1} с; клеймо одно: {2}", cpuSeconds, gpuSeconds, cpu.Stamp == gpu.Stamp,
                          nullArm ? "ЦП (зерно + 7)" : "GPU");
        Check(nullArm || cpu.Stamp == gpu.Stamp, "клейма раздела ЦП и GPU разные");
        Console.WriteLine("   строка        T кэВ   вошло ЦП   вошло GPU  Δ/σ    площадь ЦП  площадь GPU  ГПУ/ЦП  ср.бин ЦП ср.бин GPU   χ²/n  бинов");
        int bad = 0, rows = 0, sumBins = 0, badZ = 0;
        double worstChi = 0.0, worstZ = 0.0, sumChi = 0.0;
        // (`AMBER274`, физика 30, Н2 критика замеров) Знаковые меры паритета — построчный допуск слеп к
        // сдвигу в 1 % (ловит его в 62 % повторов): сумма Σz/√строк доли вошедших; взвешенное GPU/ЦП − 1
        // доли вошедших (Σ(e_GPU − e_ЦП)/Σ e_ЦП, σ — пуассон всех строк); взвешенное GPU/ЦП − 1 среднего
        // бина строк (обратными квадратами ошибок, ошибка — разброс бина / √(историй в строке)). Порог —
        // 3σ; нулевое плечо `--gpu=cpu` обязано уложиться в него само.
        double sumZ = 0.0, sumEa = 0.0, sumDe = 0.0, sumVe = 0.0, sumWm = 0.0, sumWmD = 0.0;
        Action<string, double, float[], float[], double, double> compare = (name, te, a, b, ea, eb) =>
        {
            double sa = 0, sb = 0, ma = 0, mb = 0, qa = 0, qb = 0;
            for (int i = 0; i < a.Length; i++) { sa += a[i]; ma += i * a[i]; qa += (double)i * i * a[i]; }
            for (int i = 0; i < b.Length; i++) { sb += b[i]; mb += i * b[i]; qb += (double)i * i * b[i]; }
            double sigma = Math.Sqrt(Math.Max(1e-30, (ea * (1 - ea) + eb * (1 - eb)) / n));
            double z = (eb - ea) / sigma;
            sumZ += z;
            sumEa += ea;
            sumDe += eb - ea;
            sumVe += sigma * sigma;
            if (sa * n > 25.0 && sb * n > 25.0)
            {
                double meanA = ma / sa, meanB = mb / sb;
                double varA = Math.Max(0.0, qa / sa - meanA * meanA), varB = Math.Max(0.0, qb / sb - meanB * meanB);
                double sd = Math.Sqrt(varA / (sa * n) + varB / (sb * n)) / Math.Max(1e-30, meanA);
                if (sd > 0.0 && meanA > 0.0)
                {
                    double wgt = 1.0 / (sd * sd);
                    sumWm += wgt;
                    sumWmD += wgt * (meanB / meanA - 1.0);
                }
            }

            double chi;
            int bins;
            Chi2Smoothed(a, b, n, cpu.BinKev, te, out chi, out bins);
            rows++;
            sumChi += chi * bins;
            sumBins += bins;
            bool ok = Math.Abs(z) <= 3.0 && (bins == 0 || chi <= 1.5);
            if (!ok) bad++;
            if (Math.Abs(z) > 3.0) badZ++;
            if (bins > 0 && chi > worstChi) worstChi = chi;
            if (Math.Abs(z) > worstZ) worstZ = Math.Abs(z);
            Console.WriteLine("   {0,-9} {1,9:F2} {2,10:F5} {3,10:F5} {4,5:F2} {5,12:E4} {6,12:E4} {7,7:F4} {8,9:F1} {9,9:F1} {10,7:F3} {11,5}{12}",
                              name, te, ea, eb, z, sa, sb, sa > 0 ? sb / sa : double.NaN,
                              sa > 0 ? ma / sa * cpu.BinKev : 0.0, sb > 0 ? mb / sb * cpu.BinKev : 0.0, chi, bins, ok ? "" : "  ⚠");
        };
        for (int i = 0; i < cpu.NodeCount; i++)
        {
            compare("e- " + i.ToString(CultureInfo.InvariantCulture), cpu.Energies[i], cpu.Electron[i], gpu.Electron[i],
                    cpu.EnteredElectron[i], gpu.EnteredElectron[i]);
            if (positron)
            {
                compare("e+ " + i.ToString(CultureInfo.InvariantCulture), cpu.Energies[i], cpu.Positron[i], gpu.Positron[i],
                        cpu.EnteredPositron[i], gpu.EnteredPositron[i]);
            }
        }

        for (int j = 0; j < cpu.LineCount; j++)
        {
            compare("line " + j.ToString(CultureInfo.InvariantCulture), cpu.LineEnergies[j], cpu.LineElectron[j], gpu.LineElectron[j],
                    cpu.LineEntered[j], gpu.LineEntered[j]);
        }

        // (`Ф8`) Строки ветвей: событие и каскад (доля вошедших — беты; у каскада — той же).
        for (int j = 0; j < cpu.BranchCount; j++)
        {
            compare("ev " + j.ToString(CultureInfo.InvariantCulture), cpu.BranchEndpoints[j] + cpu.BranchLevels[j],
                    cpu.BranchEvent[j], gpu.BranchEvent[j], cpu.BranchEntered[j], gpu.BranchEntered[j]);
            compare("cas " + j.ToString(CultureInfo.InvariantCulture), cpu.BranchLevels[j],
                    cpu.BranchGamma[j], gpu.BranchGamma[j], cpu.BranchEntered[j], gpu.BranchEntered[j]);
        }

        Console.WriteLine("   итог паритета: строк {0}, вне допуска {1}; худший |Δ/σ| вошедших {2:F2}, худший χ²/n {3:F3}; "
                          + "по всем строкам Σχ²/Σn = {4:F3} ({5} бинов)", rows, bad, worstZ, worstChi,
                          sumBins > 0 ? sumChi / sumBins : 0.0, sumBins);
        // Правило: доля вошедших — в 3σ у всех строк (допускается не больше 1 % строк за 3σ,
        // ни одной за 4σ); формы — Σχ²/Σn по всем строкам ≤ 1.5 (бины ширины ПШПВ независимы).
        // ⚠ χ²/n ОДНОЙ строки (3…50 бинов) за 1.5 выходит и у двух выборок ОДНОГО кода (нулевое
        // плечо `--gpu=cpu`, журнал физики 28: 4…15 строк из 167) — построчно это не мерило,
        // печатается знаком ⚠, а не отказом.
        double aggregate = sumBins > 0 ? sumChi / sumBins : 0.0;
        double zSum = rows > 0 ? sumZ / Math.Sqrt(rows) : 0.0;
        double dEnt = sumEa > 0.0 ? sumDe / sumEa : 0.0, sEnt = sumEa > 0.0 ? Math.Sqrt(sumVe) / sumEa : 0.0;
        double dMean = sumWm > 0.0 ? sumWmD / sumWm : 0.0, sMean = sumWm > 0.0 ? 1.0 / Math.Sqrt(sumWm) : 0.0;
        Console.WriteLine("   знаковые меры (Н2 критика замеров): Σz/√строк вошедших = {0:+0.00;-0.00}; "
                          + "{5}/ЦП − 1 вошедших = {1:+0.000;-0.000} ± {2:F3} % ({6:+0.0;-0.0}σ); среднего бина = "
                          + "{3:+0.000;-0.000} ± {4:F3} % ({7:+0.0;-0.0}σ); порог 3σ",
                          zSum, 100.0 * dEnt, 100.0 * sEnt, 100.0 * dMean, 100.0 * sMean, nullArm ? "ЦП+7" : "GPU",
                          sEnt > 0.0 ? dEnt / sEnt : 0.0, sMean > 0.0 ? dMean / sMean : 0.0);
        Check(Math.Abs(zSum) <= 3.0, "паритет раздела: знаковая сумма Σz/√строк вошедших " + zSum.ToString("F2", CultureInfo.InvariantCulture) + " за 3σ");
        Check(!(sEnt > 0.0) || Math.Abs(dEnt / sEnt) <= 3.0, "паритет раздела: взвешенное отношение вошедших за 3σ");
        Check(!(sMean > 0.0) || Math.Abs(dMean / sMean) <= 3.0, "паритет раздела: взвешенное отношение среднего бина за 3σ");
        Check(badZ <= Math.Max(1, rows / 100) && worstZ <= 4.0, "паритет раздела: доля вошедших вне 3σ у " + badZ + " строк из " + rows);
        Check(aggregate <= 1.5, "паритет раздела: Σχ²/Σn = " + aggregate.ToString("F3", CultureInfo.InvariantCulture) + " больше 1.5");
    }

    /// <summary>
    /// χ²/n двух строк (доля на испущенную при n историй, вес истории 1), свёрнутых разрешением
    /// (ПШПВ 7 % при 662 кэВ ∝ √E) — бинами ширины ПШПВ; бины — где хотя бы 25 отсчётов на обе.
    /// </summary>
    static void Chi2Smoothed(float[] a, float[] b, int n, double binKev, double te, out double chi, out int bins)
    {
        // Свёртка разрешением — НЕЗАВИСИМЫМИ бинами шириной ПШПВ (7 % при 662 кэВ ∝ √E): бин
        // строки получает историю целиком (вес 1), и сумма по широкому бину — пуассон с
        // дисперсией счёт/n²; гауссово сглаживание коррелирует соседей, и χ²/n по нему шумит
        // (нулевое плечо `--gpu=cpu` выходило за 1.5 так же часто, как GPU).
        int len = Math.Max(a.Length, b.Length);
        chi = 0.0;
        bins = 0;
        int j = 0;
        while (j < len)
        {
            double e = Math.Max(20.0, (j + 0.5) * binKev);
            int width = Math.Max(1, (int)Math.Round(0.07 * 661.657 * Math.Sqrt(e / 661.657) / binKev));
            double sa = 0.0, sb = 0.0;
            for (int i = j; i < j + width && i < len; i++)
            {
                sa += i < a.Length ? a[i] : 0.0;
                sb += i < b.Length ? b[i] : 0.0;
            }

            j += width;
            double counts = (sa + sb) * n;
            if (counts < 25.0)
            {
                continue;
            }

            double d = sa - sb;
            chi += d * d / ((sa + sb) / n);
            bins++;
        }

        chi = bins > 0 ? chi / bins : 0.0;
    }

    /// <summary>
    /// (`AMBER274`, физика 28) Счётчики истории частицы пробы на ЦП и GPU при одной энергии:
    /// вошла, аннигиляций в сцене, застряла в капсуле, квантов очереди, площадь строки.
    /// Диагностика паритета (`--esrc-stats=E1,E2 --positron=1|0`).
    /// </summary>
    static void EsrcStats(GeometryModel g, string gpuPath, string energies, int n, bool positron)
    {
        var options = new ResponseMatrixOptions();
        using (var dev = new RmGpu(gpuPath ?? RmGpu.DefaultPath, RmGpu.DefaultStackBytes))
        {
            foreach (string part in energies.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double te = double.Parse(part, CultureInfo.InvariantCulture);
                EfficiencySimulator sim = Make(g, options, te, 7001);
                sim.Histories = n;
                EfficiencySimulator.ElectronSourceStats st;
                double[] row = sim.ElectronSourceResponse(te, positron, options.BinKev, 0.0, out st);
                double area = 0;
                foreach (double v in row) area += v;
                EfficiencySimulator gs = Make(g, options, te, 7001);
                dev.ForgetBlob();
                dev.Prepare(gs, false);
                uint k0, k1;
                GpuNode.NodeKey(options, gs, 7001, out k0, out k1);
                double[] scal;
                double[] grow = dev.ElectronSourceRow(te, positron, options.BinKev, 0.0, n, k0, k1, out scal, CancellationToken.None);
                double garea = 0;
                foreach (double v in grow) garea += v;
                Func<double, double, string> z = (a, b) =>
                {
                    double pa = a / n, pb = b / n;
                    double s = Math.Sqrt(Math.Max(1e-30, (pa * (1 - pa) + pb * (1 - pb)) / n));
                    return string.Format(CultureInfo.InvariantCulture, "{0:F5}/{1:F5} ({2:+0.0;-0.0}σ)", pa, pb, (pb - pa) / s);
                };
                Console.WriteLine("   {0} {1,8:F2} кэВ ЦП/GPU: вошло {2}; аннигиляций {3}; в капсуле {4}; ушло из сцены переносом {9}; квантов очереди {5:F4}/{6:F4}; площадь {7:E4}/{8:E4}",
                                  positron ? "e+" : "e-", te, z(st.Entered, scal[GpuSlots.E_ENTERED]),
                                  z(st.Annihilations, scal[GpuSlots.E_ANNIHILATIONS]), z(st.StoppedInCapsule, scal[GpuSlots.E_STOPPED_CAPSULE]),
                                  (double)st.QueuedPhotons / n, scal[GpuSlots.E_QUEUED] / n, area, garea,
                                  z(sim.CountLayerLeftScene, scal[GpuSlots.E_COUNT_LAYER_LEFT_SCENE]));
            }
        }
    }

    /// <summary>
    /// (`AMBER274`, физика 28) Таблица спектра бета-ветвей нуклида для эталона Geant4
    /// (`g4cf espec <файл>`): строки «кэВ_лево кэВ_право вес», бин 1 кэВ, вес — доля на распад
    /// (Σ ветвей (I/100)·∫бин N(T) dT / ∫ N(T) dT) — та же форма, что у образа FSA
    /// (`FsaBetaLibrary.Density`: таблица `nucdb.beta_shapes` или код по ΔJπ). Линии
    /// конверсии печатаются (энергия, выход) — для моноэнергетических прогонов эталона.
    /// </summary>
    static void Spectrum(string spec)
    {
        int colon = spec.IndexOf(':');
        string nucid = spec.Substring(0, colon), path = spec.Substring(colon + 1);
        var weights = new SortedDictionary<int, double>();
        double total = 0.0;
        foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
        {
            if (b.IsLine)
            {
                Console.WriteLine("   линия конверсии {0} {1:F3} кэВ, {2:G5} % на распад", b.Shell, b.EndpointKev, b.Intensity);
                continue;
            }

            double mean;
            double norm = FsaBetaLibrary.Integral(b, out mean);
            if (!(norm > 0.0)) continue;
            int top = (int)Math.Ceiling(b.EndpointKev);
            for (int k = 0; k < top; k++)
            {
                // Симпсон по 8 отрезкам на бин.
                double lo = k, hi = Math.Min(k + 1.0, b.EndpointKev), h = (hi - lo) / 8.0, sum = 0.0;
                if (!(hi > lo)) continue;
                for (int i = 0; i <= 8; i++)
                {
                    double t = lo + h * i;
                    if (t <= 0.0) t = 1e-6;
                    if (t >= b.EndpointKev) t = b.EndpointKev * (1.0 - 1e-9);
                    double c = i == 0 || i == 8 ? 1.0 : (i % 2 == 1 ? 4.0 : 2.0);
                    sum += c * FsaBetaLibrary.Density(b, t);
                }

                double w = sum * h / 3.0 / norm * b.Intensity / 100.0;
                double have;
                weights.TryGetValue(k, out have);
                weights[k] = have + w;
                total += w;
            }

            Console.WriteLine("   ветвь β{0} граница {1:F2} кэВ, {2:G5} % на распад, средняя {3:F2} кэВ, форма {4}",
                              b.Positron ? "+" : "-", b.EndpointKev, b.Intensity, mean, b.ShapeId);
        }

        var sb = new StringBuilder();
        foreach (KeyValuePair<int, double> kv in weights)
        {
            if (!(kv.Value > 0.0)) continue;
            sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append(' ')
              .Append((kv.Key + 1).ToString(CultureInfo.InvariantCulture)).Append(' ')
              .Append(kv.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }

        File.WriteAllText(path, sb.ToString());
        Console.WriteLine("   таблица {0}: {1} бинов, сумма весов {2:F6} (бета на распад)", path, weights.Count, total);
    }

    static EfficiencySimulator Make(GeometryModel geometry, ResponseMatrixOptions options, double te, int index)
    {
        var make = typeof(ResponseMatrixBuilder).GetMethod("MakeSimulator",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var sim = (EfficiencySimulator)make.Invoke(null, new object[] { geometry, options, index, te });
        if (estr >= 0)
        {
            sim.ElectronStraggling = estr != 0;
        }

        if (emix >= 0)
        {
            sim.ElectronMixedTransport = emix != 0;
        }

        if (mott >= 0)
        {
            sim.ElectronMottHeavy = mott != 0;
        }

        if (eref >= 0)
        {
            sim.ElectronElasticRefine = eref != 0;
        }

        if (dpwa >= 0)
        {
            sim.ElectronScreeningDpwa = dpwa != 0;
        }

        if (refineParts >= 0)
        {
            sim.ElectronRefineParts = refineParts;
        }

        if (stepFraction > 0.0)
        {
            sim.ElectronStepFraction = stepFraction;
            sim.ElectronCrystalStepFraction = stepFraction;
        }

        if (crystalStepFraction > 0.0)
        {
            sim.ElectronCrystalStepFraction = crystalStepFraction;
        }

        if (cutoffDeg > 0.0)
        {
            sim.LayerHardCutoffDeg = cutoffDeg;
        }

        if (mixedParts >= 0)
        {
            sim.ElectronMixedParts = mixedParts;
        }

        return sim;
    }

    static void Summary(ResponseElectronSection s)
    {
        Console.WriteLine("== итог раздела: узел, T кэВ, вошло e-, площадь e- (на частицу), средний бин e-, вошло e+, площадь e+");
        for (int i = 0; i < s.NodeCount; i++)
        {
            double ae = 0.0, me = 0.0, ap = 0.0;
            for (int b = 0; b < s.Electron[i].Length; b++) { ae += s.Electron[i][b]; me += b * s.Electron[i][b]; }
            if (s.HasPositron) foreach (float v in s.Positron[i]) ap += v;
            Console.WriteLine("   {0,2} {1,8:F1} {2,9:F6} {3,10:E3} {4,8:F1} {5,9:F6} {6,10:E3}",
                              i, s.Energies[i], s.EnteredElectron[i], ae, ae > 0 ? me / ae * s.BinKev : 0.0,
                              s.EnteredPositron[i], ap);
        }
    }

    static void Dump(ResponseElectronSection s, string path)
    {
        var sb = new StringBuilder();
        sb.Append("kind,node,te_kev,bin,kev,value\n");
        for (int pass = 0; pass < (s.HasPositron ? 2 : 1); pass++)
        {
            float[][] rows = pass == 0 ? s.Electron : s.Positron;
            for (int i = 0; i < s.NodeCount; i++)
            {
                for (int b = 0; b < rows[i].Length; b++)
                {
                    if (rows[i][b] == 0f) continue;
                    sb.Append(pass == 0 ? "e-" : "e+").Append(',').Append(i.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append(s.Energies[i].ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                      .Append(b.ToString(CultureInfo.InvariantCulture)).Append(',')
                      .Append((b * s.BinKev).ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                      .Append(rows[i][b].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                }
            }
        }

        File.WriteAllText(path, sb.ToString());
        Console.WriteLine("  строки — " + path);
    }

    static string Short(string h)
    {
        return string.IsNullOrEmpty(h) ? "—" : h.Substring(0, Math.Min(16, h.Length));
    }

    static void Check(bool ok, string what)
    {
        if (!ok)
        {
            failures++;
            Console.WriteLine("   ⛔ НАРУШЕНО: " + what);
        }
    }

    // ------------------------------------------------------------------
    // (`AMBER274` Ф8, полоса `f8cyl` 02.11.2026) ШОВ СОБЫТИЯ ЦЕЛИКОМ
    // ------------------------------------------------------------------

    /// <summary>
    /// (`Ф8`) Ветви строк «событие целиком» по ключу `--branches=`: `none` — нет (ключ шва
    /// ВЫКЛ); `lib[:<мин. %>]` (умолчание `lib:1`) — β⁻-ветви рядов библиотеки нуклидов с выходом
    /// не ниже порога на распад члена, у которых есть каскад; иначе — список нуклидов
    /// (`60CO,208TL`) — все их β⁻-ветви с каскадом. Печатает перепись (сколько взято и почему
    /// пропущены прочие).
    /// </summary>
    static List<ElectronBranchSpec> SectionBranchList(string spec)
    {
        if (spec == null)
        {
            spec = "lib:1";
        }

        if (string.Equals(spec, "none", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("== строки ветвей: ключ шва ВЫКЛ (--branches=none)");
            return null;
        }

        // (`AMBER305`) Рецепт раздела — один с окном матрицы.
        SortedSet<string> members;
        double min = 0.0;
        if (spec.StartsWith("lib", StringComparison.OrdinalIgnoreCase))
        {
            min = ElectronSectionRecipe.LibraryMinPercent;
            int colon = spec.IndexOf(':');
            if (colon > 0)
            {
                min = double.Parse(spec.Substring(colon + 1), CultureInfo.InvariantCulture);
            }

            members = ElectronSectionRecipe.LibraryMembers();
        }
        else
        {
            members = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in spec.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) members.Add(p.Trim());
        }

        return ElectronSectionRecipe.BranchesOf(members, min, spec, Console.WriteLine);
    }

    /// <summary>(`Ф8`) Ожидаемое каскадом число частиц у энергии (±1 кэВ), знак — вид (+ гамма, − электрон).</summary>
    static double ExpectedNear(Dictionary<double, double> expected, double kev, double sign)
    {
        double sum = 0.0;
        foreach (KeyValuePair<double, double> p in expected)
        {
            if (Math.Sign(p.Key) == Math.Sign(sign) && Math.Abs(Math.Abs(p.Key) - kev) <= 1.0)
            {
                sum += p.Value;
            }
        }

        return sum;
    }

    /// <summary>
    /// (`AMBER274` Ф8, полоса `f8cyl`) ПОЛНЫЙ РАСПАД МОДЕЛЬЮ — для сверки с ионом Geant4
    /// (`g4cf … ionhist 2 <Emax> scene <…> ion <Z> <A> <N>`): спектр оставленной энергии на распад
    /// нуклида тремя плечами из ОДНИХ строк. «Шов» (`--event=1`) — Σ ветвей с каскадом
    /// I·(строка события), прочие ветви — веса узлов, линии гаммы и конверсии — фотонные строки и
    /// строки линий на ОСТАТОК выхода (выход базы минус то, что каскады уже несут,
    /// `ElectronBranchSpec.ExpectedEmissions`). «Каскад без беты» — то же, но вместо строки
    /// события — строка одного каскада плюс бета весами узлов (совпадений бета–гамма нет,
    /// гамма–гамма есть). «Без шва» (`--event=0`) — прежняя модель: бета весами узлов, все линии
    /// — каждая своей строкой (совпадений нет вовсе). CSV: `kev,event,cascade_beta,off`.
    /// </summary>
    static void DirectEvent(GeometryModel g, string nuclides, double[] grid, int n, int threads, string csvPath)
    {
        var options = new ResponseMatrixOptions();
        double bin = options.BinKev;
        var section = new ResponseElectronSection
        {
            Stamp = "direct-event:" + g.Name + ":" + ResponseElectronSection.CapsuleText(g),
            BinKev = bin,
            Energies = grid,
            Electron = new float[grid.Length][],
        };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        double lightPerKev = 0.0;
        if (directLight)
        {
            EfficiencySimulator anchor = Make(g, options, 661.657, 7000);
            anchor.Histories = 20000;
            double anchorError;
            anchor.Response(661.657, bin, out anchorError);
            lightPerKev = anchor.LastPhotonLightScale;
        }

        var parallel = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = threads };
        System.Threading.Tasks.Parallel.For(0, grid.Length, parallel, i =>
        {
            EfficiencySimulator sim = Make(g, options, grid[i], 7001 + i);
            sim.Histories = n;
            EfficiencySimulator.ElectronSourceStats st;
            section.Electron[i] = ResponseElectronSection.Trim(sim.ElectronSourceResponse(grid[i], false, bin, lightPerKev, out st));
        });

        string[] list = nuclides.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        var ce = new List<double>();
        var specs = new List<ElectronBranchSpec>();
        var specOf = new Dictionary<FsaBeta, ElectronBranchSpec>();
        foreach (string nucid in list)
        {
            foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
            {
                if (b.IsLine)
                {
                    ce.Add(b.EndpointKev);
                    continue;
                }

                string why;
                ElectronBranchSpec s = FsaBetaLibrary.EventSpec(b, out why);
                Console.WriteLine("   ветвь {0} {1:F2} кэВ {2:G5} % → {3}", nucid, b.EndpointKev, b.Intensity,
                                  s != null ? "уровень " + s.LevelKev.ToString("F2", CultureInfo.InvariantCulture) + " кэВ, переходов "
                                              + s.TransitionCount.ToString(CultureInfo.InvariantCulture) : "без строки: " + why);
                if (s != null)
                {
                    specs.Add(s);
                    specOf[b] = s;
                }
            }
        }

        double[] lines = ResponseElectronSection.CleanLines(ce.ToArray(), grid);
        if (lines != null)
        {
            section.LineEnergies = lines;
            section.LineElectron = new float[lines.Length][];
            System.Threading.Tasks.Parallel.For(0, lines.Length, parallel, j =>
            {
                EfficiencySimulator sim = Make(g, options, lines[j], 8001 + j);
                sim.Histories = n;
                EfficiencySimulator.ElectronSourceStats st;
                section.LineElectron[j] = ResponseElectronSection.Trim(sim.ElectronSourceResponse(lines[j], false, bin, lightPerKev, out st));
            });
        }

        // Строка ветви — ПОРЦИЯМИ по потокам (`--event-n=`, умолчание n; порция c — своё зерно
        // от номера 9001 + j + 1000·c), сумма — с весом историй порции: у Co-60 одна ветвь
        // несёт 99.88 %, и без порций её считал бы один поток.
        int chunks = Math.Max(1, threads);
        int total = eventHistories > 0 ? eventHistories : n;
        var eventRows = new double[specs.Count][][];
        var eventStats = new EfficiencySimulator.BranchEventStats[specs.Count];
        var parts = new double[specs.Count * chunks][][];
        var partStats = new EfficiencySimulator.BranchEventStats[specs.Count * chunks];
        System.Threading.Tasks.Parallel.For(0, specs.Count * chunks, parallel, k =>
        {
            int j = k / chunks, c = k % chunks;
            EfficiencySimulator sim = Make(g, options, specs[j].EndpointKev, 9001 + j + 1000 * c);
            sim.Histories = Math.Max(1000, total / chunks);
            EfficiencySimulator.BranchEventStats bs;
            parts[k] = sim.BranchEventResponse(specs[j], bin, lightPerKev, out bs);
            partStats[k] = bs;
        });
        for (int j = 0; j < specs.Count; j++)
        {
            var sum = new EfficiencySimulator.BranchEventStats();
            var rows = new double[2][];
            for (int c = 0; c < chunks; c++)
            {
                EfficiencySimulator.BranchEventStats bs = partStats[j * chunks + c];
                double[][] p = parts[j * chunks + c];
                for (int r = 0; r < 2; r++)
                {
                    if (rows[r] == null) rows[r] = new double[p[r].Length];
                    for (int b = 0; b < p[r].Length && b < rows[r].Length; b++) rows[r][b] += p[r][b] * bs.Histories;
                }

                sum.Histories += bs.Histories;
                sum.Entered += bs.Entered;
                sum.CascadeQuanta += bs.CascadeQuanta;
                sum.CascadeElectrons += bs.CascadeElectrons;
                sum.CascadeCut += bs.CascadeCut;
                sum.Seconds += bs.Seconds;
            }

            for (int r = 0; r < 2; r++) for (int b = 0; b < rows[r].Length; b++) rows[r][b] /= sum.Histories;
            eventRows[j] = rows;
            eventStats[j] = sum;
        }

        int length = (int)Math.Ceiling(grid[grid.Length - 1] * 1.5 / bin) + 2;
        foreach (double[][] r in eventRows) length = Math.Max(length, r[0].Length);
        var photon = new Dictionary<double, double[]>();
        foreach (string nucid in list)
        {
            foreach (double[] line in FsaSampleLibrary.DecayLines(nucid, new FsaSampleLibrary.Report()))
            {
                if (line[0] >= 10.0 && line[1] >= 0.001) length = Math.Max(length, (int)Math.Ceiling(line[0] * 1.5 / bin) + 2);
            }
        }

        double[] on = new double[length], partial = new double[length], off = new double[length];
        Action<double[], double[], double> add = (to, from, w) =>
        {
            for (int b = 0; b < from.Length && b < to.Length; b++) to[b] += w * from[b];
        };
        int index = 9500;
        foreach (string nucid in list)
        {
            var expected = new Dictionary<double, double>();
            foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
            {
                if (b.IsLine) continue;
                double y = b.Intensity / 100.0;
                double[] node = new double[length];
                section.Accumulate(node, FsaBetaLibrary.Weights(section, b), false, 1.0);
                add(off, node, y);
                ElectronBranchSpec s;
                if (specOf.TryGetValue(b, out s))
                {
                    int j = specs.IndexOf(s);
                    add(on, eventRows[j][0], y);
                    add(partial, eventRows[j][1], y);
                    add(partial, node, y);
                    foreach (KeyValuePair<double, double> p in s.ExpectedEmissions())
                    {
                        double v;
                        expected.TryGetValue(p.Key, out v);
                        expected[p.Key] = v + y * p.Value;
                    }
                }
                else
                {
                    add(on, node, y);
                    add(partial, node, y);
                }
            }

            double coveredGamma = 0.0, residualGamma = 0.0;
            foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
            {
                if (!b.IsLine) continue;
                double y = b.Intensity / 100.0;
                double[] row = new double[length];
                section.Accumulate(row, FsaBetaLibrary.Weights(section, b), false, 1.0);
                add(off, row, y);
                double rest = y - ExpectedNear(expected, b.EndpointKev, -1.0);
                if (rest > 1e-6)
                {
                    add(on, row, rest);
                    add(partial, row, rest);
                }
            }

            // Фотонные строки линий — все разом по потокам (зерно — от номера линии в списке).
            var todo = new List<double>();
            foreach (double[] line in FsaSampleLibrary.DecayLines(nucid, new FsaSampleLibrary.Report()))
            {
                if (line[0] >= 10.0 && line[1] >= 0.001 && !photon.ContainsKey(line[0]) && !todo.Contains(line[0])) todo.Add(line[0]);
            }

            var made = new double[todo.Count][];
            int first = index;
            index += todo.Count;
            System.Threading.Tasks.Parallel.For(0, todo.Count, parallel, t =>
            {
                EfficiencySimulator gs = Make(g, options, todo[t], first + t);
                gs.Histories = n;
                double err;
                made[t] = gs.Response(todo[t], bin, out err);
            });
            for (int t = 0; t < todo.Count; t++) photon[todo[t]] = made[t];

            foreach (double[] line in FsaSampleLibrary.DecayLines(nucid, new FsaSampleLibrary.Report()))
            {
                if (line[0] < 10.0 || line[1] < 0.001) continue;
                double[] row = photon[line[0]];

                double y = line[1] / 100.0;
                add(off, row, y);
                double covered = ExpectedNear(expected, line[0], +1.0);
                double rest = y - covered;
                coveredGamma += Math.Min(y, covered);
                if (rest > 1e-6)
                {
                    add(on, row, rest);
                    add(partial, row, rest);
                    residualGamma += rest;
                }

                Console.WriteLine("   линия {0} {1,9:F3} кэВ: выход {2:F5}, каскады несут {3:F5}, остаток фотонной строкой {4:F5}",
                                  nucid, line[0], y, covered, Math.Max(0.0, rest));
            }

            Console.WriteLine("   {0}: гамма каскадами {1:F5} на распад, отдельными строками {2:F5}", nucid, coveredGamma, residualGamma);
        }

        for (int j = 0; j < specs.Count; j++)
        {
            EfficiencySimulator.BranchEventStats bs = eventStats[j];
            Console.WriteLine("   строка ветви {0} {1:F1} кэВ: {2} историй, бета вошла {3:F5}, квантов каскада {4:F4} (из них электронов {5:F5}) на распад, "
                              + "событий {6:E4}, каскадов {7:E4}, упоров {8}, {9:F1} с",
                              specs[j].Nuclide, specs[j].EndpointKev, bs.Histories, (double)bs.Entered / bs.Histories,
                              (double)bs.CascadeQuanta / bs.Histories, (double)bs.CascadeElectrons / bs.Histories,
                              Sum(eventRows[j][0]), Sum(eventRows[j][1]), bs.CascadeCut, bs.Seconds);
            Check(bs.CascadeCut == 0, "упоры каскада у ветви " + specs[j].Key);
        }

        int[][] bands =
        {
            new[] { 40, 200 }, new[] { 200, 300 }, new[] { 300, 400 }, new[] { 400, 700 }, new[] { 700, 1050 },
            new[] { 1050, 1250 }, new[] { 1250, 1400 }, new[] { 1400, 2400 }, new[] { 2400, 2600 }, new[] { 2600, 3000 },
            new[] { 3000, 4000 }, new[] { 40, 4000 },
        };
        Console.WriteLine("== полный распад {0}, сцена {1}: {2} историй на строку, {3:F1} с; на распад, полосы оставленной энергии",
                          nuclides, g.Name, n, watch.Elapsed.TotalSeconds);
        Console.WriteLine("   полоса, кэВ        шов (событие)   каскад+бета узлами   без шва     шов/без шва");
        foreach (int[] band in bands)
        {
            double a = BandSum(on, bin, band), p = BandSum(partial, bin, band), o = BandSum(off, bin, band);
            Console.WriteLine("   {0,5}–{1,-5}      {2,12:E4}   {3,12:E4}   {4,12:E4}   {5,8:F4}", band[0], band[1], a, p, o, o > 0 ? a / o : double.NaN);
        }

        if (csvPath != null)
        {
            var sb = new StringBuilder("kev,event,cascade_beta,off\n");
            for (int b = 0; b < length; b++)
            {
                if (on[b] == 0.0 && partial[b] == 0.0 && off[b] == 0.0) continue;
                sb.Append((b * bin).ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                  .Append(on[b].ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(partial[b].ToString("R", CultureInfo.InvariantCulture)).Append(',')
                  .Append(off[b].ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }

            File.WriteAllText(csvPath, sb.ToString());
            Console.WriteLine("  спектр — " + csvPath);
        }
    }

    /// <summary>
    /// (`f8cyl`, дальнее дно цилиндра) Фотонная строка сцены (`EfficiencySimulator.Response`,
    /// на испущенный квант, бин 2 кэВ с центром в 2b — сетка `g4cf hist`) по полосам: пик (±4
    /// кэВ), комптон, обратное рассеяние 150…300 кэВ, ниже 150 кэВ, всё. CSV — `kev,<E>…`.
    /// </summary>
    static void PhotonRows(GeometryModel g, string spec, int n, string csvPath)
    {
        var options = new ResponseMatrixOptions();
        double bin = options.BinKev;
        var energies = Array.ConvertAll(spec.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries),
                                        s => double.Parse(s, CultureInfo.InvariantCulture));
        var rows = new double[energies.Length][];
        int length = 0;
        System.Threading.Tasks.Parallel.For(0, energies.Length, i =>
        {
            EfficiencySimulator gs = Make(g, options, energies[i], 9300 + i);
            gs.Histories = n;
            double err;
            rows[i] = gs.Response(energies[i], bin, out err);
        });
        foreach (double[] r in rows) length = Math.Max(length, r.Length);
        Console.WriteLine("== фотонные строки сцены {0}: {1} историй; на испущенный квант", g.Name, n);
        for (int i = 0; i < energies.Length; i++)
        {
            double e = energies[i];
            Func<double, double, double> band = (lo, hi) => BandSum(rows[i], bin, new[] { (int)Math.Floor(lo), (int)Math.Ceiling(hi) });
            Console.WriteLine("   {0,9:F3} кэВ: пик ±4 {1:E4}; 300…E−10 {2:E4}; 150…300 {3:E4}; 40…150 {4:E4}; всё ≥ 2 {5:E4}",
                              e, band(e - 4, e + 4), band(300, e - 10), band(150, 300), band(40, 150), band(2, 1e9));
        }

        if (csvPath != null)
        {
            var sb = new StringBuilder("kev");
            foreach (double e in energies) sb.Append(',').Append(e.ToString("R", CultureInfo.InvariantCulture));
            sb.Append('\n');
            for (int b = 0; b < length; b++)
            {
                sb.Append((b * bin).ToString("F2", CultureInfo.InvariantCulture));
                foreach (double[] r in rows) sb.Append(',').Append((b < r.Length ? r[b] : 0.0).ToString("R", CultureInfo.InvariantCulture));
                sb.Append('\n');
            }

            File.WriteAllText(csvPath, sb.ToString());
            Console.WriteLine("  строки — " + csvPath);
        }
    }

    /// <summary>
    /// (`AMBER274` Ф8, `f8cyl`) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ПУТИ FSA: раздел с узлами и строками ветвей
    /// своих нуклидов (`n` историй) — (а) `FsaBetaLibrary.Weights` у ветви со строкой даёт веса
    /// «узлы + линии + ветви» с единицей у её строки, у ветви без строки — веса узлов; (б)
    /// `Accumulate` по ним кладёт РАЗНОСТЬ «событие − каскад» — площадь та же, что у разности строк
    /// напрямую, бины со знаком минус есть (вынос из пиков); (в) рычаг
    /// `ResponseElectronSection.BranchEvents` ВЫКЛ — веса узлов, образ прежний (равен образу раздела
    /// БЕЗ строк ветвей побайтно); (г) `TopBin` берёт длину строки события.
    /// </summary>
    static void FsaBranchControl(GeometryModel g, string nuclides, int n)
    {
        var options = new ResponseMatrixOptions();
        double bin = options.BinKev;
        double[] grid = ResponseElectronSection.Grid(20, 60.0, 3600.0);
        var specs = new List<ElectronBranchSpec>();
        foreach (string nucid in nuclides.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
            {
                string why;
                ElectronBranchSpec s = b.IsLine ? null : FsaBetaLibrary.EventSpec(b, out why);
                if (s != null) specs.Add(s);
            }
        }

        Func<bool, ResponseElectronSection> build = withBranches =>
        {
            var section = new ResponseElectronSection
            {
                Stamp = "fsa-branch-control:" + (withBranches ? "br" : "-"),
                BinKev = bin,
                Energies = grid,
                Electron = new float[grid.Length][],
            };
            for (int i = 0; i < grid.Length; i++)
            {
                EfficiencySimulator sim = Make(g, options, grid[i], 7001 + i);
                sim.Histories = n;
                EfficiencySimulator.ElectronSourceStats st;
                section.Electron[i] = ResponseElectronSection.Trim(sim.ElectronSourceResponse(grid[i], false, bin, 0.0, out st));
            }

            if (withBranches)
            {
                var list = new List<ElectronBranchSpec>(specs);
                typeof(ResponseMatrixBuilder).GetMethod("AllocateBranches", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { section, list });
                for (int j = 0; j < list.Count; j++)
                {
                    EfficiencySimulator sim = Make(g, options, list[j].EndpointKev, 9001 + j);
                    sim.Histories = n;
                    EfficiencySimulator.BranchEventStats bs;
                    double[][] rows = sim.BranchEventResponse(list[j], bin, 0.0, out bs);
                    section.BranchEvent[j] = ResponseElectronSection.Trim(rows[0]);
                    section.BranchGamma[j] = ResponseElectronSection.Trim(rows[1]);
                }
            }

            return section;
        };

        ResponseElectronSection plain = build(false), withRows = build(true);
        Console.WriteLine("== путь FSA со строками ветвей: сцена {0}, {1} узлов, строк ветвей {2}", g.Name, grid.Length, withRows.BranchCount);
        Check(withRows.BranchCount == specs.Count && specs.Count > 0, "строк ветвей нет");
        foreach (string nucid in nuclides.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (FsaBeta b in FsaBetaLibrary.Branches(nucid))
            {
                if (b.IsLine) continue;
                int j = withRows.BranchIndex(b.Nuclide, b.EndpointKev);
                double[] w = FsaBetaLibrary.Weights(withRows, b);
                bool union = w != null && w.Length == withRows.NodeCount + withRows.LineCount + withRows.BranchCount;
                if (j < 0)
                {
                    Check(!union, "(а) ветвь без строки получила веса строки ветви: " + b.EndpointKev);
                    continue;
                }

                Check(union && w[withRows.NodeCount + withRows.LineCount + j] == 1.0, "(а) у ветви " + b.EndpointKev + " не веса строки ветви");
                int length = withRows.TopBin(false, b.EndpointKev) + 4;
                Check(length >= withRows.BranchEvent[j].Length, "(г) TopBin короче строки события");
                double[] image = new double[length];
                double area = withRows.Accumulate(image, w, false, 1.0);
                double direct = 0.0;
                foreach (float v in withRows.BranchEvent[j]) direct += v;
                foreach (float v in withRows.BranchGamma[j]) direct -= v;
                int negative = 0;
                double negativeSum = 0.0;
                foreach (double v in image) if (v < 0.0) { negative++; negativeSum += v; }
                Console.WriteLine("   ветвь {0} {1:F1} кэВ: площадь образа {2:E5} (разность строк {3:E5}), бинов с минусом {4}, их сумма {5:E4}",
                                  b.Nuclide, b.EndpointKev, area, direct, negative, negativeSum);
                Check(Math.Abs(area - direct) <= 1e-9 * Math.Max(1e-12, Math.Abs(direct)) + 1e-15, "(б) площадь образа не равна разности строк");

                // (в) Рычаг ВЫКЛ — веса узлов, образ равен образу раздела без строк ветвей побайтно.
                ResponseElectronSection.BranchEvents = false;
                double[] wOff = FsaBetaLibrary.Weights(withRows, b);
                double[] wPlain = FsaBetaLibrary.Weights(plain, b);
                ResponseElectronSection.BranchEvents = true;
                double[] imOff = new double[length], imPlain = new double[length];
                withRows.Accumulate(imOff, wOff, false, 1.0);
                plain.Accumulate(imPlain, wPlain, false, 1.0);
                bool same = wOff.Length == wPlain.Length;
                for (int k = 0; same && k < length; k++) same = imOff[k] == imPlain[k];
                Console.WriteLine("   рычаг ВЫКЛ: веса узлов длины {0}, образ равен образу раздела без строк ветвей: {1}", wOff.Length, same);
                Check(same, "(в) рычаг ВЫКЛ не вернул прежний образ");
            }
        }
    }

    /// <summary>(`Ф8`) Сумма бинов b·шаг ∈ [низ, верх) — та же сетка, что у `ionhist` Geant4 (бин b — энергия b·шаг).</summary>
    static double BandSum(double[] row, double bin, int[] band)
    {
        double s = 0.0;
        for (int b = 0; b < row.Length; b++)
        {
            double e = b * bin;
            if (e >= band[0] && e < band[1]) s += row[b];
        }

        return s;
    }

    static double Sum(double[] row)
    {
        double s = 0.0;
        foreach (double v in row) s += v;
        return s;
    }

    /// <summary>
    /// (`Ф8`) Положительный контроль формата строк ветвей: записанный и прочитанный раздел
    /// несёт те же строки ветвей (нуклид, граница, уровень, строки события и каскада побитово).
    /// </summary>
    static void CheckBranchesRoundTrip(ResponseElectronSection written, ResponseElectronSection read)
    {
        bool same = read.BranchCount == written.BranchCount;
        for (int j = 0; same && j < read.BranchCount; j++)
        {
            same = read.BranchNuclides[j] == written.BranchNuclides[j] && read.BranchEndpoints[j] == written.BranchEndpoints[j]
                   && read.BranchLevels[j] == written.BranchLevels[j] && read.BranchHashes[j] == written.BranchHashes[j]
                   && SameRow(read.BranchEvent[j], written.BranchEvent[j]) && SameRow(read.BranchGamma[j], written.BranchGamma[j]);
        }

        Console.WriteLine("  строк ветвей: {0}; прочитаны теми же: {1}", read.BranchCount, same);
        Check(same, "строки ветвей прочитаны не теми числами");
    }

    static bool SameRow(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return a == b;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
