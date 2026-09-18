using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace G4RawProbe
{
    /// <summary>
    /// СЫРОЙ отклик нашего переноса рядом со сценой для внешнего арбитра —
    /// одним заходом, без уширения и без фита.
    ///
    /// ЗАЧЕМ. `G4CompareProbe` сверяет с Geant4 уже УШИРЕННУЮ модель на шкале
    /// измеренного спектра, и в такой сверке слиты три вещи: перенос, форма
    /// пика и энергетическая калибровка. Для вопроса «какой физики у нас нет»
    /// нужен голый перенос: распределение ПОГЛОЩЁННОЙ энергии, бин в бин с
    /// `g4cf … hist`. Пик вылета, комптоновский край, обратное рассеяние и
    /// K-вылет в сырой гистограмме видны сами, а уширение их прячет.
    ///
    ///     g4rawprobe --geometry=X.in [--spectrum=X.xml] --energy=661.657
    ///                [--n=2000000] [--bin=1] [--seed=20260902]
    ///                [--out=raw.csv] [--scene=scene.txt]
    ///                [--bands=1-12,13-25,55-59] [--peakw] [--lys=0|1|2]
    ///                [--etr=0|1] [--etr-step=0.1] [--kdip=0|1|2|3]
    ///                [--positron=0|1] [--posoffset=0|1] [--rayl2[=0|1]]
    ///                [--ecomp=0|1] [--bpath=0|1|2] [--detour=0.7] [--eltr=0|1]
    ///
    /// `--eltr=1` (`AMBER44` + правка заноса `M12`, П94 17.09.2026; решения Amber
    /// «Перенос в слоях обвязки», «Одной полосой с AMBER44»): ключ
    /// `ElectronLayerTransport` — электрон, вылетевший из кристалла, ведётся в
    /// слоях обвязки (ESTAR слоя, Хайленд, переходы между слоями) и может
    /// вернуться; занос рождённого в слое — тем же переносом, направление по
    /// процессу, дошедший — в перенос по кристаллу. Умолчание — склада (ВКЛ с
    /// физики 19, П97 18.09.2026; `--eltr=0` — плечо «как физика 18»). Мерка: RC103 (П55 и живая) 662/1461/59.5, диск
    /// AS80 1461/2614 против `g4cf` умолчанием (арбитр С возвратом) по полосам
    /// П92 (`cmp92.py`); `--detour=0` у нас против `killcarry` у арбитра — что
    /// вне заноса ничего не сдвинулось. ⚠ Доля `--detour=<x>` при `--eltr=1` не
    /// читается (обхода по прямой там нет), но `--detour=0` значит «заноса нет»
    /// в ОБОИХ режимах: плечо `--eltr=1 --detour=0` — возврат без заноса.
    ///
    /// `--detour=<x>` (`M12`, П92 17.09.2026) — РЫЧАГ АБЛЯЦИИ заноса электронов
    /// из обвязки: `ElectronCarryDetour` (умолчание 0.7 — доля пробега CSDA по
    /// прямой). `--detour=0` — заноса нет вовсе (зеркало ключа `killcarry` у
    /// арбитра `g4cf`): «def − detour=0» у нас против «def − killcarry» у Geant4
    /// мерит вклад заноса по полосам порознь. Не настройка склада — замер.
    ///
    /// `--ecomp=1` (`N4`/`F11` (г), П44 13.09.2026): электрон в произвольном
    /// веществе — пробег по составу слоя и тормозное электронов, рождённых вне
    /// кристалла (проба, оправа, стенка). `--bpath=N` (`M3`, П44): тормозное
    /// вдоль пути переноса (1 изотропно, 2 по электрону). Умолчания — склада
    /// (с 14.09.2026, физика 18, П50: `ecomp=1`, `bpath=2`; сверки П44
    /// воспроизводятся `--ecomp=0 --bpath=0`). Мерка: голые RC103 / AS80 на
    /// 662 / 2614 и диск `AS80_th_disk` 2614 против `g4cf` по полосам.
    ///
    /// ⛔ УМОЛЧАНИЯ КЛЮЧЕЙ ФИЗИКИ — ОТ СКЛАДА, а не литералами (П37
    /// 13.09.2026, физика 17): `--lys=`, `--etr=`, `--kdip=`, `--positron=`,
    /// `--posoffset=`, `--rayl2=` берут умолчание у `ResponseMatrixOptions`
    /// (то, чем считается склад матриц), и проба без ключей мерит ТУ ЖЕ
    /// физику, что склад («проверять то, что БУДЕТ ИСПОЛЬЗОВАНО»; П27 уже
    /// платила за литерал `kdip`: до неё проба мерила не тот каскад, что
    /// склад). Сверки прежних полос воспроизводятся явными ключами:
    /// П20/П27 — `--etr=0 --lys=0 --positron=0 --rayl2=0` (и `--kdip=0` для
    /// сверок до П27), П23 §3 — `--etr=0 --positron=0 --rayl2=0 --lys=N`.
    /// Печать «умолчание склада» / «ключом» у каждого — ниже в выводе.
    ///
    /// `--kdip=N` (заведено П27): K-провал/раздельный каскад, умолчание —
    /// склада (1 с 12.09.2026); `--kdip=0` воспроизводит сверки до П27.
    ///
    /// `--etr=1` (П27 12.09.2026, приёмка `A72` — решение Amber «Вести
    /// электрон переносом»): ключ `ElectronTransport` — электрон ведётся
    /// переносом по кристаллу вместо эффективной глубины вылета; `--etr-step=`
    /// — доля остаточного пробега на шаг (`ElectronStepFraction`, умолчание
    /// 0.1) для замера сходимости по шагу. Мерка: голые RC103 / OBS / AS80,
    /// `--no-light --bin=1` против `g4cf vacuum hist` (П20 §3, П27 §3–4).
    ///
    /// `--lys=N` (П23 12.09.2026, приёмка `M9` — решение Amber «ω_L из
    /// fluorescence_yield + f13 в СЛЕДУЮЩИЙ единый счёт склада»): уровень
    /// ключа `LYieldSupply` — 0 EADL без переходов Костера—Кронига;
    /// 1 — ω_L из xraylib + переходы EADL; 2 (умолчание склада с 13.09.2026)
    /// — и переходы из xraylib (таблица `coster_kronig`, без неё отказ). Мерка ~~`A101`~~: голый NaI Ø80×80,
    /// 59.541 кэВ, `--no-light --bin=1 --bands=55-56,55-59` против Geant4
    /// (5.268e-4 / 5.794e-4 на историю).
    ///
    /// `--out=` — `keV,response` (доля на историю; ПОСЛЕДНИЙ бин — пик полного
    /// поглощения, см. <see cref="EfficiencySimulator.Response"/>).
    /// `--scene=` — `DumpScene()` в формате, который читает `g4cf scene`.
    ///
    /// ⛔ `--bands=` СЧИТАЕТ ПОЛОСЫ САМА ПРОБА (`T134`). До 10.09.2026 полосы
    /// каждый раз считались отдельным скриптом поверх `--out=`, у каждой сверки
    /// своим, — то есть числа полос разных заходов сравнивались на веру. Ключ
    /// берёт список `a-b` в кэВ через запятую и печатает по каждой полосе долю
    /// на историю и долю от полной суммы.
    ///
    /// ⚠ Полоса считается по КОНТИНУУМУ: бины `i` с энергией `i*bin` в `[a, b]`
    /// и `i &lt; N-1`. Последний бин — пик полного поглощения, его энергия в
    /// `--out=` записана как `(N-1)*bin` и в полосу НЕ входит, иначе полоса у
    /// верхнего края шкалы молча вобрала бы весь пик. Пик печатается отдельной
    /// строкой выше.
    /// </summary>
    static class Program
    {
        /// <summary>`--ключ=0|1` — только эти два значения, иначе отказ (как у `--etr=`).</summary>
        static bool Flag01(string arg, int prefix)
        {
            string v = arg.Substring(prefix);
            if (v != "0" && v != "1")
            {
                throw new ArgumentException(arg.Substring(0, prefix) + " принимает только 0 или 1: " + arg);
            }

            return v == "1";
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string geometryPath = null, spectrumPath = null;
            string outPath = null, scenePath = null;
            double energyKev = 661.657, binKev = 1.0;
            int histories = 2000000, seed = 20260902;
            // ⛔ ШКАЛА. Наш отклик по умолчанию переложен в шкалу СВЕТА
            // (`F11`): прибор меряет не энергию, а свет, и у сцинтиллятора
            // выход на килоэлектронвольт от энергии зависит. Geant4 отдаёт
            // ЭНЕРГОВЫДЕЛЕНИЕ и о свете не знает — значит сверять надо с
            // выключенной непропорциональностью, иначе узкие особенности
            // (пики вылета, 511) сравниваются со сдвигом.
            bool light = true;
            // `A68`: допуск пика из геометрии вместо нуля (`E34`).
            bool peakw = false;
            // Конус на габарит сцены в аналоговой ветви (`A57`) — ключ замера A/B.
            bool cone = false;
            // ⛔ Умолчания ключей физики — У СКЛАДА (`ResponseMatrixOptions`),
            // не литералами здесь: копия числа разошлась бы с полем при первой
            // же смене умолчания молча (`S37`; П27 платила за `kdip`).
            var store = new ResponseMatrixOptions();
            // Когерентное своим каналом во взвешенной ветви (`N13`) — рычаг `A58`;
            // умолчание — склада (ВКЛ с 13.09.2026, физика 17).
            bool rayl2 = store.RayleighToCrystal;
            // Пара: раздельный перенос позитрона и смещение вершины (`S126`);
            // умолчание — склада (обе половины ВКЛ с 13.09.2026).
            bool positron = store.PositronTransport, posoffset = store.PositronOffset;
            double escT0 = -1.0;      // <0 — не трогать умолчание (`A63`)
            // Ключи АБЛЯЦИИ каналов утечки (`A63`): чем держится каждая полоса.
            bool xray = true, esc = true, brem = true;
            bool noLXray = false;                       // `A60`
            bool noKLCascade = false;                   // `A101`
            int lys = store.LYieldSupply;               // `M9`, П23 — умолчание склада (2 с 13.09.2026)
            bool etr = store.ElectronTransport;         // `A72`, П27 — умолчание склада (ВКЛ с 13.09.2026)
            double etrStep = -1.0;                      // <0 — умолчание симулятора
            int kdip = store.KDipLight;                 // `F11` (а)/П17: K-провал и раздельный каскад — умолчание склада (1)
            bool ecomp = store.ElectronAnyMaterial;     // `N4`/`F11` (г), П44 — умолчание склада (ВКЛ с физики 18, П50)
            int bpath = store.BremAlongPath;            // `M3`, П44 — умолчание склада (2 с физики 18, П50)
            double detour = -1.0;                       // <0 — умолчание симулятора (`M12`, П92)
            bool eltr = store.ElectronLayerTransport;   // `AMBER44`/`M12`, П94 — умолчание склада (ВКЛ с физики 19, П97)
            double escSlope = -1.0;
            double escSoft = -1.0, escSoftKev = -1.0;   // `A63`
            double escCurve = -1.0;                     // `A70`
            double[][] bands = null;                    // `T134`, --bands=
            foreach (string a in args)
            {
                if (a == "--no-light") { light = false; continue; }
                // ⛔ `A68`: допуск пика ИЗ ГЕОМЕТРИИ (`E34`, ключ склада
                // `--peakw=1`). Не удобство, а ПЛЕЧО встречной проверки ниже:
                // взвешенная ветвь зовёт пиком «недобрало не больше допуска»,
                // аналоговая — «округлилось в бин пика», и при нулевом допуске
                // это РАЗНЫЕ множества. С допуском из геометрии полуширина
                // ПШПВ перекрывает окно округления, определения сходятся, и
                // отношение ветвей начинает мерить перенос, а не разнобой
                // определений.
                if (a == "--peakw") { peakw = true; continue; }
                if (a == "--cone") { cone = true; continue; }
                if (a == "--rayl2") { rayl2 = true; continue; }
                if (a.StartsWith("--rayl2=", StringComparison.Ordinal)) { rayl2 = Flag01(a, 8); continue; }
                // `S126` (П37): обе половины пары — умолчание склада, ключи для абляции.
                if (a.StartsWith("--positron=", StringComparison.Ordinal)) { positron = Flag01(a, 11); continue; }
                if (a.StartsWith("--posoffset=", StringComparison.Ordinal)) { posoffset = Flag01(a, 12); continue; }
                if (a == "--no-xray") { xray = false; continue; }
                if (a == "--no-esc") { esc = false; continue; }
                if (a == "--no-brem") { brem = false; continue; }
                // `A60`, АБЛЯЦИЯ: снять вылет L-рентгена. Выключенный
                // ключ возвращает счёт физики 14 до последнего бита.
                if (a == "--no-lxray") { noLXray = true; continue; }
                // `A101`: атомный каскад K→L. Умолчанием ВКЛ (физика 16), как и
                // в расчёте матрицы, — иначе проба мерила бы не то, что склад.
                // Ключ выключает его для абляции.
                if (a == "--no-klcasc") { noKLCascade = true; continue; }
                // `M9` (П23): источник ω_L и переходы Костера—Кронига, уровень 0/1/2.
                if (a.StartsWith("--lys=", StringComparison.Ordinal))
                {
                    lys = int.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                    continue;
                }
                // `A72` (П27): перенос электрона вместо эффективной глубины.
                if (a.StartsWith("--etr=", StringComparison.Ordinal))
                {
                    string v = a.Substring(6);
                    if (v != "0" && v != "1")
                    {
                        Console.Error.WriteLine("--etr= принимает только 0 или 1: " + a);
                        return 2;
                    }

                    etr = v == "1";
                    continue;
                }
                // ⛔ `F11` (а)/П17 (заведено П27 12.09.2026): уровень K-провала —
                // обе половины теми же выражениями, что у построителя матрицы
                // (KDipCurveHalf / KDipCascadeHalf). УМОЛЧАНИЕ 1 — как у склада с
                // 12.09.2026 (ResponseMatrixOptions.KDipLight): проба обязана
                // мерить ту физику, которой считает склад («проверять то, что
                // БУДЕТ ИСПОЛЬЗОВАНО»). До П27 ключа не было, и все сверки с
                // арбитром шли при 0 — воспроизводятся `--kdip=0`. Для `--no-light`
                // значима только половина каскада: фотоэлектрон получает
                // e − E_связи, релаксация — электронами EADL, а не одним куском;
                // на 59.5 кэВ это решает полосы 32…54 (П27 §4: без раздельного
                // каскада перенос давал +39/−23/−21 %, с ним −2/+8/−2 %).
                if (a.StartsWith("--kdip=", StringComparison.Ordinal))
                {
                    kdip = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--etr-step=", StringComparison.Ordinal))
                {
                    etrStep = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
                    continue;
                }
                // `N4`/`F11` (г) и `M3` (П44): электрон в произвольном веществе,
                // тормозное вдоль пути.
                if (a.StartsWith("--ecomp=", StringComparison.Ordinal))
                {
                    ecomp = Flag01(a, 8);
                    continue;
                }
                if (a.StartsWith("--bpath=", StringComparison.Ordinal))
                {
                    string v = a.Substring(8);
                    if (v != "0" && v != "1" && v != "2")
                    {
                        Console.Error.WriteLine("--bpath= принимает только 0, 1 или 2: " + a);
                        return 2;
                    }

                    bpath = int.Parse(v, CultureInfo.InvariantCulture);
                    continue;
                }
                // `M12` (П92): доля пробега заносимого электрона по прямой; 0 — заноса нет.
                if (a.StartsWith("--detour=", StringComparison.Ordinal))
                {
                    detour = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                    if (detour < 0.0)
                    {
                        Console.Error.WriteLine("--detour= принимает число >= 0: " + a);
                        return 2;
                    }

                    continue;
                }
                // `AMBER44`/`M12` (П94): перенос электрона в слоях обвязки — занос и возврат.
                if (a.StartsWith("--eltr=", StringComparison.Ordinal))
                {
                    eltr = Flag01(a, 7);
                    continue;
                }
                if (a.StartsWith("--esc-soft=", StringComparison.Ordinal))
                {
                    escSoft = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--esc-curve=", StringComparison.Ordinal))
                {
                    escCurve = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--esc-soft-kev=", StringComparison.Ordinal))
                {
                    escSoftKev = double.Parse(a.Substring(15), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--esc-slope=", StringComparison.Ordinal))
                {
                    escSlope = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--esc-t0=", StringComparison.Ordinal))
                {
                    // `A63`: порог включения вылета электрона, кэВ. Умолчание 350.
                    //
                    // ⛔ «Ниже порога вылета нет ВООБЩЕ» — БОЛЬШЕ НЕ ПРАВДА, и
                    // здесь это стояло написанным (снято 10.09.2026, полоса П1).
                    // Порогово-линейная часть действительно даёт ноль ниже 350,
                    // но `A63` добавила к глубине вылета МЯГКОЕ слагаемое
                    // `ElectronEscapeSoftAmp·exp(−T/ElectronEscapeSoftKev)`,
                    // и при T = 26 кэВ (фотоэлектрон линии 59.5) оно равно
                    // 0.5·exp(−0.26) = 0.386 — то есть вылет там ЕСТЬ и он
                    // велик. Мерено на этой же пробе: `--no-esc` двигает
                    // отношение ветвей на 59.5 кэВ с 1.0097 до 1.0045.
                    escT0 = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                    continue;
                }
                if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--energy=", StringComparison.Ordinal)) energyKev = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--bin=", StringComparison.Ordinal)) binKev = double.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--n=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--seed=", StringComparison.Ordinal)) seed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--scene=", StringComparison.Ordinal)) scenePath = a.Substring(8);
                else if (a.StartsWith("--bands=", StringComparison.Ordinal))
                {
                    // `T134`: список полос `a-b` через запятую, границы в кэВ.
                    // ⛔ Плохой список — ОТКАЗ, а не пустой разбор: молча
                    // пропущенная полоса выглядит как «полос нет», и это ровно
                    // тот случай, из-за которого ключ и заводится.
                    if (!TryParseBands(a.Substring(8), out bands))
                    {
                        Console.Error.WriteLine("не разобрал --bands=, нужен список «a-b» через запятую, границы в кэВ: " + a.Substring(8));
                        return 2;
                    }

                    continue;
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            GeometryModel geometry = null;
            if (geometryPath != null)
            {
                if (!File.Exists(geometryPath))
                {
                    Console.Error.WriteLine("нет файла геометрии: " + geometryPath);
                    return 2;
                }

                geometry = GeometryModel.Load(geometryPath);
            }
            else if (spectrumPath != null)
            {
                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                ResultDataFile file;
                var serializer = new XmlSerializer(typeof(ResultDataFile));
                using (var stream = new FileStream(spectrumPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    file = (ResultDataFile)serializer.Deserialize(stream);
                }

                ResultData rd = file.ResultDataList[0];
                if (rd.Efficiency == null || !rd.Efficiency.HasGeometry)
                {
                    Console.Error.WriteLine("у спектра нет кривой с геометрией");
                    return 2;
                }

                geometry = rd.Efficiency.Geometry;
            }
            else
            {
                Console.Error.WriteLine("нужен --geometry=<файл.in> или --spectrum=<файл.xml>");
                return 2;
            }

            var simulator = new EfficiencySimulator(geometry);
            simulator.Seed = seed;
            simulator.Histories = histories;
            simulator.LightNonproportionality = light;
            simulator.LXrayEscape = !noLXray;           // `A60`
            simulator.KLCascade = !noKLCascade;         // `A101`
            simulator.LYieldSupply = lys;               // `M9`, П23
            simulator.ElectronTransport = etr;          // `A72`, П27
            simulator.ElectronAnyMaterial = ecomp;      // `N4`/`F11` (г), П44
            simulator.BremAlongPath = bpath;            // `M3`, П44
            if (detour >= 0.0) { simulator.ElectronCarryDetour = detour; }   // `M12`, П92
            simulator.ElectronLayerTransport = eltr;    // `AMBER44`/`M12`, П94
            simulator.LightSubKevCurve = ResponseMatrixOptions.KDipCurveHalf(kdip);
            simulator.LightCascadeSplit = ResponseMatrixOptions.KDipCascadeHalf(kdip);
            if (etrStep > 0.0) { simulator.ElectronStepFraction = etrStep; }
            simulator.AnalogConeSampling = cone;
            simulator.RayleighToCrystal = rayl2;
            simulator.PositronTransport = positron;     // `S126`, П37
            simulator.PositronOffset = posoffset;
            simulator.XrayEscape = xray;
            simulator.ElectronEscape = esc;
            simulator.Bremsstrahlung = brem;
            if (escSoft >= 0.0) { simulator.ElectronEscapeSoftAmp = escSoft; }
            if (escCurve > 0.0) { simulator.ElectronEscapeCurve = escCurve; }
            if (escSoftKev > 0.0) { simulator.ElectronEscapeSoftKev = escSoftKev; }
            if (escSlope >= 0.0)
            {
                simulator.ElectronEscapeSlope = escSlope;
            }

            if (escT0 >= 0.0)
            {
                simulator.ElectronEscapeT0Kev = escT0;
                Console.WriteLine("порог вылета электрона: {0} кэВ (умолчание 350)", escT0);
            }
            if (peakw)
            {
                simulator.PeakHalfWidthKev = geometry.PeakHalfWidthKev(energyKev);
            }

            Console.WriteLine("допуск пика: {0} кэВ ({1})",
                              simulator.PeakHalfWidthKev.ToString("F3", CultureInfo.InvariantCulture),
                              peakw ? "ИЗ ГЕОМЕТРИИ, --peakw (`E34`)" : "ноль, как у поставочного склада");
            Console.WriteLine("шкала: {0}", light ? "СВЕТ (как в матрице)" : "энерговыделение (как у Geant4)");
            Console.WriteLine("выходы L-флуоресценции (`M9`, --lys=): {0}",
                              lys == 0 ? "0 — EADL, переходов Костера—Кронига нет (как до 12.09.2026)"
                              : lys == 1 ? "1 — ω_L из xraylib (fluorescence_yield), переходы f12/f13/f23 по EADL"
                              : "2 — ω_L и переходы из xraylib (coster_kronig)");
            Console.WriteLine("розыгрыш аналоговой: {0}", cone ? "КОНУС на габарит сцены (`A57`)" : "полная сфера");
            Console.WriteLine("когерентное в проводке своим каналом (`S127`, --rayl2=): {0}{1}",
                              rayl2 ? "ВКЛ" : "выкл",
                              rayl2 == store.RayleighToCrystal ? " (умолчание склада)" : " (ключом)");
            Console.WriteLine("пара (`S126`, --positron= / --posoffset=): перенос позитрона {0}, смещение вершины {1}{2}",
                              positron ? "ВКЛ" : "выкл", posoffset ? "ВКЛ" : "выкл",
                              positron == store.PositronTransport && posoffset == store.PositronOffset
                                  ? " (умолчание склада)" : " (ключом)");
            Console.WriteLine("K-провал/каскад (`F11` (а), --kdip=): {0} (кривая света {1}, раздельный каскад {2})", kdip,
                              simulator.LightSubKevCurve ? "ВКЛ" : "выкл", simulator.LightCascadeSplit ? "ВКЛ" : "выкл");
            Console.WriteLine("вылет электрона (`A72`, --etr=): {0}",
                              !esc ? "ВЫКЛЮЧЕН (--no-esc)"
                              : etr ? "ПЕРЕНОС (Заутер/кинематика/Цай, шаг " + simulator.ElectronStepFraction.ToString("0.###", CultureInfo.InvariantCulture) + " пробега, Хайленд, вылет по грани)"
                              : "эффективная глубина (как до 12.09.2026)");
            Console.WriteLine("электрон в произвольном веществе (`N4`, --ecomp=): {0}{1}",
                              ecomp ? "ВКЛ (пробег и тормозное обвязки по составу)" : "выкл (вода, тормозного обвязки нет)",
                              ecomp == store.ElectronAnyMaterial ? " (умолчание склада)" : " (ключом)");
            Console.WriteLine("занос электрона из обвязки (`M12`, --detour=): доля пробега по прямой {0}{1}",
                              simulator.ElectronCarryDetour.ToString("0.###", CultureInfo.InvariantCulture),
                              detour < 0.0 ? " (умолчание симулятора)" : detour == 0.0 ? " (ключом — ЗАНОСА НЕТ)" : " (ключом)");
            Console.WriteLine("перенос электрона в слоях обвязки — занос и возврат (`AMBER44`/`M12`, --eltr=): {0}{1}",
                              eltr ? (detour == 0.0 ? "ВКЛ, ЗАНОСА НЕТ (--detour=0): только возврат вылетевшего из кристалла"
                                                   : "ВКЛ (возврат вылетевшего из кристалла; занос переносом в слое и по кристаллу, доля --detour= не читается)")
                                   : "выкл (вылет — конец истории; занос по прямой с detour, остаток куском)",
                              eltr == store.ElectronLayerTransport ? " (умолчание склада)" : " (ключом)");
            Console.WriteLine("тормозное вдоль пути (`M3`, --bpath=): {0}{1}",
                              bpath == 0 ? "выкл (в точке рождения)" : bpath == 1 ? "1 (на шагах переноса, изотропно)" : "2 (на шагах переноса, по электрону)",
                              bpath == store.BremAlongPath ? " (умолчание склада)" : " (ключом)");

            if (scenePath != null)
            {
                File.WriteAllText(scenePath, simulator.DumpScene(), new UTF8Encoding(false));
                Console.WriteLine("сцена: " + scenePath);
            }

            double error;
            double[] response = simulator.Response(energyKev, binKev, out error);
            if (response == null)
            {
                Console.Error.WriteLine("отклик не посчитался");
                return 1;
            }

            double sum = 0.0;
            for (int i = 0; i < response.Length; i++)
            {
                sum += response[i];
            }

            Console.WriteLine("E={0} кэВ, историй {1}, бин {2} кэВ, бинов {3}", energyKev, histories, binKev, response.Length);
            Console.WriteLine("пик {0:E6}, полная {1:E6}, ошибка взвешенной ветки {2:F2} %, шум континуума {3:F2} %",
                              response[response.Length - 1], sum, error, simulator.LastContinuumRelativeError);

            // ⛔ ВСТРЕЧНАЯ ПРОВЕРКА ДВУХ ВЕТВЕЙ. Пик берётся у ВЗВЕШЕННОЙ ветви,
            // континуум — у АНАЛОГОВОЙ, и это два независимых оценивателя одной
            // величины. Аналоговая своё попадание в пик выбрасывает
            // (`CountPeakBinDropped`), но СЧИТАЕТ, — значит её оценку пика можно
            // напечатать и сверить с взвешенной. Разошлись — расходятся ветви, а
            // не мы с арбитром.
            //
            // ⛔ ЧИТАТЬ ОТНОШЕНИЕ БЕЗ `--peakw` НЕЛЬЗЯ (`A68`, 10.09.2026).
            // Ветви зовут пиком РАЗНОЕ: взвешенная — «недобрало не больше
            // `PeakHalfWidthKev`» (`InPeak`), аналоговая — «округлилось в бин
            // пика» (`bin >= peak`, без оговорки). При нулевом допуске второе
            // МНОЖЕСТВО ШИРЕ первого, и часть отношения — разнобой
            // определений, а не физика. Мерить перенос — плечом `--peakw`, где
            // допуск перекрывает окно округления и определения совпадают.
            int n = Math.Max(1000, histories);
            double analogPeak = simulator.WeightPeakBinDropped / n;
            Console.WriteLine("пик аналоговой ветви {0:E6} ({1} историй из {2}), взвеш./аналог. = {3:F4}",
                              analogPeak, simulator.CountPeakBinDropped, n,
                              analogPeak > 0.0 ? response[response.Length - 1] / analogPeak : 0.0);

            // ⛔ ТРЕТИЙ ОЦЕНИВАТЕЛЬ ТОЙ ЖЕ ВЕЛИЧИНЫ — `TotalEfficiency` (`A56`).
            // Полную эффективность считают ДВА разных обхода: сумма отклика выше
            // и этот. Обход у него свой, и правка `A54` (пары вне кристалла) в
            // него не дошла — расхождение выше 1022 кэВ было ровно об этом.
            // Печатается рядом, чтобы следующее такое расхождение увидел кто
            // угодно, а не только тот, кто пошёл его искать.
            // ⛔ ПЕРЕПОЛНЕНИЕ ОЧЕРЕДЕЙ (`A65`): отброшенный квант — это тихо
            // заниженный континуум. Счётчики были заведены и НЕ ЧИТАЛИСЬ ни
            // одной пробой — печатаются здесь, чтобы отказ было видно.
            Console.WriteLine("отброшено переполнением: очередь квантов {0}, вылеты {1}",
                              simulator.CountPendingDropped, simulator.CountEscapeDropped);
            // (`AMBER44`/`M12`, П94) Счётчики переноса электрона в слоях обвязки: без ключа все нули.
            Console.WriteLine("перенос в слоях обвязки (`AMBER44`): вылетов из кристалла в слои {0}, из них вернулось {1} ({2} на историю, средняя энергия возврата {3} кэВ), занесено из слоя в кристалл {4} ({5} на историю)",
                              simulator.CountLayerEscapes, simulator.CountLayerReturns,
                              ((double)simulator.CountLayerReturns / histories).ToString("0.000E+00", CultureInfo.InvariantCulture),
                              (simulator.CountLayerReturns > 0 ? simulator.SumLayerReturnKev / simulator.CountLayerReturns : 0.0).ToString("0.0", CultureInfo.InvariantCulture),
                              simulator.CountLayerCarries,
                              ((double)simulator.CountLayerCarries / histories).ToString("0.000E+00", CultureInfo.InvariantCulture));
            Console.WriteLine("комптонов в кристалле {0}, с вакансией {1}, ответили рентгеном {2} (`A61`)",
                              simulator.CountCrystalCompton, simulator.CountCrystalVacancy,
                              simulator.CountVacancyXray);
            Console.WriteLine("флуоресценция: K-квантов {0}, L-квантов {1} (`A60`)",
                              simulator.CountKXray, simulator.CountLXray);
            // `A101`: знаменатель рядом с числителем нарочно — без него
            // «мало каскадов» неотличимо от «мало Kα».
            Console.WriteLine("каскад K→L: вакансий на L {0}, из них ответили квантом {1} (`A101`)",
                              simulator.CountKLVacancy, simulator.CountKLCascade);

            double totalError;
            double totalSecond = simulator.TotalEfficiency(energyKev, out totalError);
            Console.WriteLine("полная вторым обходом (TotalEfficiency) {0:E6} ± {1:F2} %, отклик/обход = {2:F4}",
                              totalSecond, totalError, totalSecond > 0.0 ? sum / totalSecond : 0.0);

            // ⛔ ПОЛОСЫ СЧИТАЕТ САМА ПРОБА (`T134`). Прежде их считали скриптом
            // поверх `--out=` — у каждой сверки своим, и числа полос разных
            // заходов сравнивались на веру. Теперь определение полосы одно и
            // живёт рядом с расчётом.
            if (bands != null)
            {
                Console.WriteLine();
                Console.WriteLine("ПОЛОСЫ КОНТИНУУМА (бины i*{0} кэВ, i < {1}; бин пика НЕ входит)",
                                  binKev.ToString("F3", CultureInfo.InvariantCulture),
                                  response.Length - 1);
                // Континуум — всё, кроме последнего бина: он несёт пик полного
                // поглощения, а его энергия в `--out=` записана как (N-1)*bin.
                double continuum = sum - response[response.Length - 1];
                foreach (double[] bd in bands)
                {
                    double inBand = 0.0;
                    int binsInBand = 0;
                    for (int i = 0; i < response.Length - 1; i++)
                    {
                        double kev = i * binKev;
                        if (kev >= bd[0] && kev <= bd[1])
                        {
                            inBand += response[i];
                            binsInBand++;
                        }
                    }

                    // ⚠ Пустая полоса — не ноль отклика, а промах по шкале, и
                    // это разные беды. Число бинов печатается рядом, чтобы их
                    // было видно ОТДЕЛЬНО.
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,8:F2}...{1,8:F2} кэВ: {2:E6}  ({3:F3} % отклика, {4:F3} % континуума, бинов {5})",
                        bd[0], bd[1], inBand,
                        sum > 0.0 ? 100.0 * inBand / sum : 0.0,
                        continuum > 0.0 ? 100.0 * inBand / continuum : 0.0,
                        binsInBand));
                }
            }

            if (outPath != null)
            {
                using (var writer = new StreamWriter(outPath, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("keV,response");
                    for (int i = 0; i < response.Length; i++)
                    {
                        writer.WriteLine("{0},{1}",
                                         (i * binKev).ToString("F3", CultureInfo.InvariantCulture),
                                         response[i].ToString("E8", CultureInfo.InvariantCulture));
                    }
                }

                Console.WriteLine("отклик: " + outPath);
            }

            return 0;
        }

        /// <summary>
        /// Разбор `--bands=a-b,c-d,…` (`T134`). Границы в кэВ, разделитель
        /// дробной части — ТОЧКА, разбор явной инвариантной культурой
        /// (приказ Amber 05.09.2026), как и у всех остальных ключей пробы.
        /// ⛔ Возвращает false на ЛЮБОМ изъяне списка: пустой список, пара без
        /// дефиса, нечисло, отрицательная граница, перевёрнутая полоса.
        /// Молчаливый пропуск здесь неотличим от «полос нет».
        /// </summary>
        static bool TryParseBands(string spec, out double[][] bands)
        {
            bands = null;
            if (string.IsNullOrEmpty(spec))
            {
                return false;
            }

            string[] parts = spec.Split(',');
            var list = new double[parts.Length][];
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                // ⛔ Длина проверяется ДО поиска: `IndexOf(знак, 1)` на пустой
                //    строке бросает, и хвостовая запятая («1-12,») валила пробу
                //    исключением вместо отказа с доводом. Поймано положительным
                //    контролем 10.09.2026 — шестым плохим входом из шести.
                if (p.Length < 3)
                {
                    return false;
                }

                // Дефис ищется ПОСЛЕ первого знака, иначе «-5-10» распалось бы
                // по своему же минусу; отрицательных границ у шкалы всё равно нет.
                int dash = p.IndexOf('-', 1);
                if (dash <= 0 || dash == p.Length - 1)
                {
                    return false;
                }

                double lo, hi;
                if (!double.TryParse(p.Substring(0, dash), NumberStyles.Float, CultureInfo.InvariantCulture, out lo)
                    || !double.TryParse(p.Substring(dash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out hi))
                {
                    return false;
                }

                if (lo < 0.0 || hi < lo)
                {
                    return false;
                }

                list[i] = new[] { lo, hi };
            }

            bands = list;
            return true;
        }
    }
}
