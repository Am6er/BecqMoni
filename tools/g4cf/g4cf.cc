// g4cf — Geant4-арбитр каскадного суммирования (TODO F1).
//
// Сцена — ТОЧНАЯ копия сцены EfficiencySimulator для Nano16Pro_tube (цилиндр,
// снята из DescribeScene, см. tools/tccfcalc2/README.md §8):
//   CsI r0..9.27мм z0..59мм; PTFE перед 1.3мм и сбоку 1.0мм; Al перед 1.8мм,
//   сбоку 2.0мм, сзади 2.0мм; проба-вода D25 H6мм в 2мм от корпуса.
//
// Два режима:
//   mono  — изотропные гамма энергии E из объёма пробы: считает ε_полн
//           (что-то поглотилось) и ε_пик (|edep−E| < 0.5 кэВ);
//   ion   — распад иона (Co-60): по каждому окну считает «кажущуюся»
//           эффективность; CF = p·ε_пик(mono) / кажущаяся.
//
// Угловые корреляции гамма-каскада в Geant4 по умолчанию ВЫКЛЮЧЕНЫ
// (/process/had/deex/correlatedGamma false) — то есть ион-режим отвечает
// ровно на вопрос нашей формулы (изотропные совпадения), без примеси N5.
//
// Ключ `corr` (П85, `AMBER42`, 15.09.2026) ВКЛЮЧАЕТ их: до `/run/initialize`
// ставится `G4DeexPrecoParameters::SetCorrelatedGamma(true)` (то же поле, что
// у UI-команды выше; после инициализации оно заперто — `IsLocked`). Проверено по
// исходникам geant4-v11.4.2: `G4PhotonEvaporation::EmittedFragment` при
// `fCorrelatedGamma && fRDM` заводит `G4NuclearPolarization` дочернего ядра,
// `G4GammaTransition::SampleTransition` при `polarFlag && isDiscrete && 2J <= TwoJMAX`
// (умолчание 10) зовёт `G4PolarizationTransition::SampleGammaTransition` по
// спинам/мультипольностям/δ из `PhotonEvaporation` (z28.a60: 2505.753 4+, 1173.239
// «407» = E2+M3 δ=-0.0025; 1332.514 «4» = E2) — то есть RDM флаг ЧИТАЕТ, и
// отдельных данных (ICC, `G4LEVELGAMMADATA` сверх обычного) ему не нужно.
// Читатель флага — строка `SETUP correlatedGamma=…` в stdout (берётся ОБРАТНО
// из параметров после инициализации) и сводка RDM «Enable correlated gamma
// emission 1». Умолчание без ключа — прежнее, изотропное.
//
// ⛔ КЛЮЧ `vacuum` ОБЯЗАТЕЛЕН ДЛЯ СВЕРОК НА ГОЛОМ КРИСТАЛЛЕ (`T133`, `A85`,
// 03.09.2026). Мир арбитра решает 42 % полосы: та же сцена голого NaI Ø80×80 в
// полосе 55…59 кэВ даёт 8.232e-4 на историю с ВОЗДУШНЫМ миром против 5.794e-4 в
// пустоте, а в бинах 57…59 — ВТРОЕ. Сверка без ключа мерит воздух ВОКРУГ
// кристалла, а не кристалл, и выглядит при этом совершенно обычно: ни отказа, ни
// предупреждения от Geant4 не будет. Предупреждение печатает run_g4cf.bat.
// ⚠ Правило узкое нарочно: на СЦЕНАХ С ОБВЯЗКОЙ И ПРОБОЙ цена воздуха мала —
// 0.05 % полной на 59.5 кэВ (замер 02.09.2026, `A52`), — и запрещать там ключ
// незачем; ломается именно голый кристалл на мягком краю шкалы.
//
// Ключ `ionhist <шаг_кэВ> <E_max_кэВ>` (П101, `S177`, 18.09.2026) — ГИСТОГРАММА
// поглощённой энергии в ион-режиме, тем же правилом бина, что у `hist`
// (bin = int(edep/шаг + 0.5), последний бин собирает всё выше E_max). Зачем:
// окна `ion` считают только названные суммы, а вопрос П93 (какие из 82 сумм
// Eu-152 стоят не с той площадью) требует ВСЕЙ шкалы разом — и полос рядом с
// каждым окном, чтобы вычесть континуум (П90 §4.3: окно ±0.5 кэВ держит и
// комптоновский континуум линий выше). Печать — те же строки `HISTBEGIN`/`HIST`/
// `HISTEND`; окна `ion` при этом считаются, как и прежде.
//
// Ключ `iontag` (П102, `S178`, 18.09.2026) — СОСТАВ ИЗЛУЧЕНИЯ СОБЫТИЯ по окнам
// ион-режима: у каждого события, чей полный вклад попал в окно `ion`, кванты
// РДМ (гаммы и рентген атомной релаксации — у них процесс-создатель
// «Radioactivation», как у режима `angcorr`) складываются в подпись
// «E1+E2+…» (кэВ, 0.1), и подписи считаются на окно (строки `TAG`/`TAGSIG`,
// первые 60 по числу; сводка по МНОЖЕСТВУ целиком поглощённых — `TAGFULL`, без среза);
// квант, чьи потомки оставили в кристалле ровно его энергию (±0.5 кэВ), несёт
// признак `*` — подпись «778.9*+344.3*» и есть событие сумм-пика, «244.7*+121.8+40.2»
// при вкладе 284.2 — комптон-край 122 поверх целого 244, а не сумма 244+Kα2.
// Отдельно — спектр ЭМИССИИ квантов РДМ на все события (строки `EMIT`, шаг
// 0.01 кэВ): это линии и рентген самой поставки Geant4 (PhotonEvaporation +
// EADL) в квантах на распад, без детектора. Зачем: окно полной энергии
// 1123.2 (779+344) у Geant4 держит на 10 % больше модели при согласии линий —
// подпись говорит, ЧЕМ окно заполнено, вместо гадания по схеме; спектр
// эмиссии даёт доли Kα/Kβ Geant4 против `nucdb` (тип X) напрямую.
//
// Рычаги `escpop` / `killescown` / `killesccarry` / `killret` / `killretsame` /
// `killretother` / `killescbrem` / `killescdelta` / `killoutbrem` (П106, `M13`, 19.09.2026) —
// СОСТАВ «ВОЗВРАТА» ЭЛЕКТРОНА ПО НАСЕЛЕНИЯМ: свой (рождённый в кристалле) e-
// против занесённого, возврат самого e- (через ту же грань / другую) против
// тормозного и δ, рождённых им снаружи. Зачем: `def − killesc` гасит на грани
// ВСЕ e-, и население этого плеча шире нашего `detour=0`-возврата (П100 §4.4
// сравнивала эффект возврата ×0.34…0.53 разными населениями). Описание
// рычагов — у флагов ниже; читатели — `SETUP …` и строка `ESCPOP` в конце.
//
// Сборка — build_g4cf.bat рядом (vcvars64 обязан звать bat, не ps1: %PATH%
// в cmd разворачивается при разборе строки). Прогон — run_g4cf.bat (env на
// датасеты поставки). Сборка CF из логов — g4_cf.py, родные p_k — g4_pk.py.

#include "G4RunManagerFactory.hh"
#include "G4VUserDetectorConstruction.hh"
#include "G4VUserPrimaryGeneratorAction.hh"
#include "G4VUserActionInitialization.hh"
#include "G4UserRunAction.hh"
#include "G4UserEventAction.hh"
#include "G4UserSteppingAction.hh"
#include "G4UserStackingAction.hh"
#include "G4Track.hh"
#include "G4VProcess.hh"
#include "G4VModularPhysicsList.hh"
#include "G4EmStandardPhysics_option4.hh"
#include "G4DecayPhysics.hh"
#include "G4RadioactiveDecayPhysics.hh"
#include "G4NistManager.hh"
#include "G4Box.hh"
#include "G4Tubs.hh"
#include "G4LogicalVolume.hh"
#include "G4PVPlacement.hh"
#include "G4GeneralParticleSource.hh"
#include "G4Gamma.hh"
#include "G4Event.hh"
#include "G4Step.hh"
#include "G4Electron.hh"
#include "G4ProductionCutsTable.hh"
#include "G4Run.hh"
#include "G4UImanager.hh"
#include "G4SystemOfUnits.hh"
#include "G4AccumulableManager.hh"
#include "G4Accumulable.hh"
#include "globals.hh"
#include "G4Element.hh"
#include "G4Material.hh"
#include "G4PrimaryParticle.hh"
#include "G4PrimaryVertex.hh"
#include "G4IonTable.hh"
#include "G4NuclearLevelData.hh"
#include "G4DeexPrecoParameters.hh"
#include "Randomize.hh"
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <algorithm>
#include <map>
#include <mutex>
#include <set>
#include <sstream>
#include <string>
#include <vector>

namespace
{
    // Окна счёта, кэВ. Заполняются в main до старта, дальше только чтение.
    std::vector<double> gWindows;
    const double kHalfWindowKev = 0.5;

    // Режим hist: шаг бина, кэВ (0 — выключен) и число бинов. Правило бина —
    // ТО ЖЕ, что у раскладки отклика в EfficiencySimulator.Deposit:
    // bin = (int)(edep/шаг + 0.5), последний бин — пик полного поглощения.
    double gHistBinKev = 0.0;
    int gHistBins = 0;

    // Чем заполнен мир вокруг сцены: воздухом (как было всегда) или пустотой.
    //
    // ⛔ НА ГОЛОМ КРИСТАЛЛЕ КЛЮЧ ОБЯЗАТЕЛЕН (`T133`): воздух даёт в полосе
    // 55…59 кэВ 8.232e-4 против 5.794e-4, в бинах 57…59 — втрое.
    //
    // ЗАЧЕМ ключ (`A52`, 02.09.2026). Наша сцена ВОЗДУХА НЕ ЗНАЕТ: вне
    // объявленных областей у `EfficiencySimulator` пустота. У арбитра мир — куб
    // 120 см воздуха, и квант, ушедший мимо детектора, может рассеяться в нём и
    // вернуться. Пока это различие не выключено, всякий остаток сверки на
    // подпиковой полке приходится делить между «у нас нет обстановки» и «у нас
    // нет физики» на глазок. Ключ `vacuum` разводит их замером.
    bool gVacuumWorld = false;

    // Угловые γ–γ корреляции каскада в RDM (ключ `corr`, П85 `AMBER42`);
    // умолчание — изотропно, как было всегда.
    bool gCorrelatedGamma = false;

    // Режим `angcorr` (П85, `AMBER42`): ПРЯМАЯ мерка угловой корреляции пары
    // квантов распада — направления квантов РДМ снимаются в момент рождения
    // (стек), сами кванты гасятся, детектор не участвует. Печатает
    // A22 = 5·<P2(cos θ)>, A44 = 9·<P4(cos θ)> с σ и гистограмму cos θ.
    // Это положительный контроль ключа `corr` без статистики сумм-пика.
    bool gAngCorrMode = false;

    // Режим `iontag` (П102, `S178`): подписи состава излучения по окнам и
    // спектр эмиссии РДМ. Накопители потоков сливаются в общие под замком в
    // EndOfRunAction рабочего потока (до печати мастера — тот же порядок, что у
    // G4Accumulable::Merge).
    bool gTagMode = false;
    std::mutex gTagMutex;
    std::vector<std::map<std::string, long>> gTagCounts;   // по окнам
    std::vector<std::map<std::string, long>> gFullCounts;  // по окнам: множество целиком поглощённых
    std::vector<long> gTagTotals;
    std::map<int, long> gEmit;                              // бин 0.01 кэВ → квантов
    long gEmitEvents = 0;

    // Режим `emitpair` (П102): пары энергий эмиссии; на событие — есть ли квант E1
    // (±0.35 кэВ) и есть ли при нём квант E2. Кванты гасятся — детектор не нужен,
    // 20 млн распадов идут минуты. Печать `EMITPAIR E1 E2 n1 n12 P`.
    bool gEmitPairMode = false;
    std::vector<std::pair<double, double>> gEmitPairs;
    std::vector<long> gEmitPairN1, gEmitPairN12;

    // EventAction своего потока (ставится в Actions::Build) и слив его карт —
    // зовётся из EndOfRunAction рабочего; тело после определения EventAction.
    void FlushThreadTags();

    // Зерно ГСЧ (ключ `seed <N>`, П85): без ключа Geant4 стартует с ОДНОГО и
    // того же зерна, и два одинаковых прогона побитово равны — повтор ради
    // статистики обязан менять зерно (Co-60 off 15.09.2026: два прогона по
    // 40 млн дали 3561 = 3561 отсчёт в окне 2505.7).
    long gSeed = 0;

    // ---- Рычаги, которыми АРБИТР ПОВТОРЯЕТ НАШИ ПРИБЛИЖЕНИЯ переноса
    // электрона (П55 `A72` 14.09.2026, в дерево — П92 `M12` 17.09.2026). Все
    // ВЫКЛ по умолчанию — без ключей поведение прежнее до бита. Это НЕ физика
    // арбитра, а плечи абляции: разность «умолчание − рычаг» у Geant4 против
    // разности «умолчание − ключ» у нас мерит ОДИН И ТОТ ЖЕ член порознь.
    //   killesc   — e-, у которого пред-точка шага в кристалле, а пост-точка
    //               вне, гасится на грани (`fStopAndKill`): возврата электрона
    //               из обвязки нет, как у нас (п. 3 `A72`, `AMBER44`). e+ не
    //               трогается — у нас он аннигилирует на грани, у арбитра в
    //               обвязке в шаге от неё, то же самое.
    //   killcarry — e-, у которого пред-точка ВНЕ кристалла, а пост-точка
    //               в нём, гасится на входе: заноса электронов из обвязки
    //               (проба/отражатель/корпус) в кристалл нет. Зеркало нашего
    //               `ElectronCarryDetour = 0` (`G4RawProbe --detour=0`):
    //               «G4 def − G4 killcarry» — истинный вклад заноса по полосам,
    //               «наша def − наша detour=0» — наш; сравнение — П92. e+ и
    //               здесь не трогается (его 511 кэВ от заноса не зависят).
    //   fullcarry — e-, РОЖДЁННЫЙ вне кристалла (`GetLogicalVolumeAtVertex`) и
    //               вошедший в него, при выходе из кристалла отдаёт ему весь
    //               остаток кинетической энергии и гасится: наше приближение
    //               `ElectronCarryDeposit` («долетел — принёс весь остаток»),
    //               без обратного рассеяния из CsI и вылета через другую грань.
    //               «G4 fullcarry» против «наша ref» отделяет судьбу занесённого
    //               электрона В КРИСТАЛЛЕ от его переноса В ОБВЯЗКЕ (П92 §3).
    bool gKillEscape = false;
    bool gKillCarry = false;
    bool gFullCarry = false;

    // ---- Рычаги П106 (`M13`, 19.09.2026): СОСТАВ «ВОЗВРАТА» ПО НАСЕЛЕНИЯМ.
    // `def − killesc` гасит на грани ЛЮБОЙ e-, покидающий кристалл, — и
    // рождённый в кристалле, и занесённый снаружи (тот же e-, что учтён и в
    // `def − killcarry`: населения плеч пересекаются, и «ни возврата, ни
    // заноса» ≈ killesc + killcarry − def несёт член пересечения). Наша сторона
    // мерит возврат ТОЛЬКО рождённых в кристалле (`--detour=0` снимает занос
    // целиком), поэтому П100 §4.4 сравнивала разные населения. Рычаги ниже
    // разводят их; все ВЫКЛ по умолчанию, без ключей — прежний ход до бита.
    //   killescown   — e-, РОЖДЁННЫЙ В КРИСТАЛЛЕ (`GetLogicalVolumeAtVertex`),
    //                  гасится на выходе из него: нет ни его возврата, ни его
    //                  тормозного/δ снаружи. `def − killescown` — весь эффект
    //                  вылетевшего своего e- (наш `elmix1_detour0 − off_detour0`).
    //   killesccarry — e-, рождённый ВНЕ кристалла, гасится на выходе из него:
    //                  `def − killesccarry` — возврат занесённого e- (член
    //                  пересечения плеч killesc/killcarry).
    //   killret      — свой (рождённый в кристалле) e-, уже покидавший кристалл,
    //                  гасится при ПОВТОРНОМ входе: `def − killret` — возврат
    //                  самого электрона; тормозное и δ, рождённые снаружи до
    //                  возврата, остаются. `killret − killescown` — их вклад.
    //   killretsame / killretother — то же, но только если грань входа ТА ЖЕ,
    //                  что грань последнего выхода / ДРУГАЯ (грани — по
    //                  положению точки на поверхности области `crystal`
    //                  сцены: брус — 6 граней, кольцо — торцы и бока).
    //   killescbrem  — квант, РОЖДЁННЫЙ ВНЕ кристалла в родословной своего
    //                  вылетевшего e- (сам e-, его потомки), гасится при
    //                  постановке в стек: `def − killescbrem` — вклад тормозного
    //                  (и флуоресценции) вылетевшего e-, поглощённого в кристалле.
    //   killescdelta — e-/e+, рождённый ВНЕ кристалла в той же родословной
    //                  (δ-электрон вылетевшего), гасится: `def − killescdelta`.
    // Читатели: строка `SETUP … killescown=… `, счётчики `ESCPOP` в конце прогона
    // (вылетов своих/занесённых, возвратов своих по граням, погашено квантов/δ
    // родословной, самопроверка геометрии грани `facemiss`).
    bool gKillEscOwn = false;
    bool gKillEscCarry = false;
    bool gKillRet = false;
    bool gKillRetSame = false;
    bool gKillRetOther = false;
    bool gKillEscBrem = false;
    bool gKillEscDelta = false;
    //   killoutbrem  — квант ТОРМОЗНОГО (`eBrem`), рождённый ВНЕ кристалла лептоном
    //                  (e-/e+), который и сам рождён вне кристалла (фото-/комптон-
    //                  электрон обвязки, пробы), гасится при постановке в стек
    //                  (кванты аннигиляции позитрона обвязки — НЕ гасятся, считаются):
    //                  `def − killoutbrem` — вклад тормозного (и флуоресценции
    //                  от ионизации) электронов обвязки — наше
    //                  `OutsideBremsstrahlung` (толстая мишень в точке рождения,
    //                  изотропно). Родословная вылетевших своих e- сюда не
    //                  входит (у них свой рычаг `killescbrem`).
    bool gKillOutBrem = false;
    // Нужна ли родословная вылетевших (любой из рычагов П106 или их счётчики).
    bool gEscPop = false;

    // Моно-режим сценного генератора (П85): косинус угла вылета первички к оси
    // сцены (ось z — на центр кристалла) запоминается на событие, чтобы в конце
    // события накопить Q_k = <P_k(cos θ)> по событиям пика и по событиям с любым
    // вкладом — геометрическая половина корреляции, посчитанная самим арбитром
    // (сверка с AngularQkProbe). Печать: строки QK/QKT.
    G4ThreadLocal double gPrimaryCos = 2.0;   // 2 = не задан (ион, GPS)
    double gAngE1Kev = 0.0, gAngE2Kev = 0.0;
    const int kAngBins = 20;

    // ---- Сцена из файла (effsim --dump-scene): материалы, области, источник.
    struct SceneMat
    {
        double density = 0.0;                      // г/см³
        std::vector<std::pair<int, double>> parts; // Z, массовая доля
    };

    struct SceneRegion
    {
        bool box = false;
        std::string mat;
        double a = 0.0, b = 0.0;   // tub: rIn,rOut; box: ax,ay (полуразмеры)
        double z0 = 0.0, z1 = 0.0;
        bool crystal = false;
    };

    struct SceneSource
    {
        // point | cyl | box | marinelli — как у сэмплеров EfficiencySimulator
        std::string kind;
        double p[5] = { 0, 0, 0, 0, 0 };
    };

    bool gSceneLoaded = false;
    std::map<std::string, SceneMat> gSceneMats;
    std::vector<SceneRegion> gSceneRegions;
    SceneSource gSceneSource;

    bool LoadScene(const char* path)
    {
        std::ifstream in(path);
        if (!in)
        {
            std::fprintf(stderr, "сцена не читается: %s\n", path);
            return false;
        }

        std::string line;
        bool inside = false;
        while (std::getline(in, line))
        {
            std::istringstream ss(line);
            std::string word;
            if (!(ss >> word))
            {
                continue;
            }

            if (word == "SCENE") { inside = true; continue; }
            if (word == "END") { break; }
            if (!inside) { continue; }

            if (word == "mat")
            {
                std::string id;
                SceneMat m;
                ss >> id >> m.density;
                std::string part;
                while (ss >> part)
                {
                    size_t colon = part.find(':');
                    m.parts.emplace_back(std::atoi(part.substr(0, colon).c_str()),
                                         std::atof(part.substr(colon + 1).c_str()));
                }

                gSceneMats[id] = m;
            }
            else if (word == "region")
            {
                SceneRegion r;
                std::string shape, flag;
                ss >> shape >> r.mat >> r.a >> r.b >> r.z0 >> r.z1 >> flag;
                r.box = shape == "box";
                r.crystal = flag == "crystal";
                gSceneRegions.push_back(r);
            }
            else if (word == "source")
            {
                ss >> gSceneSource.kind;
                for (int i = 0; i < 5 && (ss >> gSceneSource.p[i]); ++i)
                {
                }
            }
        }

        if (gSceneRegions.empty() || gSceneSource.kind.empty())
        {
            std::fprintf(stderr, "сцена пуста или без источника: %s\n", path);
            return false;
        }

        gSceneLoaded = true;
        return true;
    }

    // ---- Геометрические предикаты коаксиальной сцены (всё центрировано).

    // i целиком внутри j (для вложенной расстановки и сброса затенённых)
    bool Inside(const SceneRegion& i, const SceneRegion& j)
    {
        const double eps = 1e-9;
        if (i.z0 < j.z0 - eps || i.z1 > j.z1 + eps)
        {
            return false;
        }

        if (!i.box && !j.box)
        {
            return i.a >= j.a - eps && i.b <= j.b + eps;
        }

        if (i.box && j.box)
        {
            return i.a <= j.a + eps && i.b <= j.b + eps;
        }

        if (i.box && !j.box)     // брус внутри кольца: только в сплошном
        {
            return j.a <= eps && std::sqrt(i.a * i.a + i.b * i.b) <= j.b + eps;
        }

        // кольцо внутри бруса: наружный радиус в полуразмерах
        return i.b <= std::min(j.a, j.b) + eps;
    }

    // Грубое «могут пересекаться»: z-диапазоны и латеральные охваты
    bool MayOverlap(const SceneRegion& p, const SceneRegion& q)
    {
        const double eps = 1e-9;
        if (p.z1 <= q.z0 + eps || q.z1 <= p.z0 + eps)
        {
            return false;
        }

        double pLo = p.box ? 0.0 : p.a;
        double pHi = p.box ? std::sqrt(p.a * p.a + p.b * p.b) : p.b;
        double qLo = q.box ? 0.0 : q.a;
        double qHi = q.box ? std::sqrt(q.a * q.a + q.b * q.b) : q.b;
        return pHi > qLo + eps && qHi > pLo + eps;
    }

    // (П106) Грань области `crystal` сцены, на которой лежит точка (мм, Geant4):
    // ближайшая по расстоянию плоскость/поверхность. Брус: 1 z0, 2 z1, 3 −x,
    // 4 +x, 5 −y, 6 +y; кольцо: 1 z0, 2 z1, 3 наружный бок, 4 внутренний бок.
    // Без сцены (вшитый tube) — 0: грани не различаются. `miss` — точка дальше
    // 1 мкм от всех граней (самопроверка: у выхода/входа такого быть не должно).
    int CrystalFace(const G4ThreeVector& posMm, bool& miss)
    {
        miss = false;
        const SceneRegion* c = nullptr;
        for (const auto& r : gSceneRegions)
        {
            if (r.crystal) { c = &r; break; }
        }

        if (c == nullptr)
        {
            return 0;
        }

        double x = posMm.x() / 10.0, y = posMm.y() / 10.0, z = posMm.z() / 10.0;   // см сцены
        double d[7];
        int n = 0;
        d[1] = std::fabs(z - c->z0);
        d[2] = std::fabs(z - c->z1);
        if (c->box)
        {
            d[3] = std::fabs(x + c->a);
            d[4] = std::fabs(x - c->a);
            d[5] = std::fabs(y + c->b);
            d[6] = std::fabs(y - c->b);
            n = 6;
        }
        else
        {
            double rho = std::sqrt(x * x + y * y);
            d[3] = std::fabs(rho - c->b);
            d[4] = c->a > 0.0 ? std::fabs(rho - c->a) : 1e30;
            n = 4;
        }

        int best = 1;
        for (int i = 2; i <= n; ++i)
        {
            if (d[i] < d[best]) { best = i; }
        }

        miss = d[best] > 1e-4;      // 1 мкм
        return best;
    }

    // (П106) Точка (мм, Geant4) внутри области `crystal` сцены? Для вторичных
    // при постановке в стек: `GetLogicalVolumeAtVertex` там ещё не выставлен
    // (его ставит SetInitialStep перед первым шагом), а положение рождения
    // известно. Точка рождения вторичного лежит строго внутри объёма шага,
    // на границе процессы вторичных не рождают.
    bool InCrystalGeom(const G4ThreeVector& posMm)
    {
        const SceneRegion* c = nullptr;
        for (const auto& r : gSceneRegions)
        {
            if (r.crystal) { c = &r; break; }
        }

        if (c == nullptr)
        {
            return false;
        }

        double x = posMm.x() / 10.0, y = posMm.y() / 10.0, z = posMm.z() / 10.0;
        if (z < c->z0 || z > c->z1)
        {
            return false;
        }

        if (c->box)
        {
            return std::fabs(x) <= c->a && std::fabs(y) <= c->b;
        }

        double rho = std::sqrt(x * x + y * y);
        return rho >= c->a && rho <= c->b;
    }
}

class Detector : public G4VUserDetectorConstruction
{
public:
    G4VPhysicalVolume* Construct() override
    {
        if (gSceneLoaded)
        {
            return ConstructFromScene();
        }

        auto nist = G4NistManager::Instance();
        auto air = nist->FindOrBuildMaterial("G4_AIR");
        auto csi = nist->FindOrBuildMaterial("G4_CESIUM_IODIDE");
        auto ptfe = nist->FindOrBuildMaterial("G4_TEFLON");
        auto al = nist->FindOrBuildMaterial("G4_Al");
        auto water = nist->FindOrBuildMaterial("G4_WATER");

        auto worldS = new G4Box("world", 30 * cm, 30 * cm, 30 * cm);
        auto worldL = new G4LogicalVolume(worldS, air, "world");
        auto worldP = new G4PVPlacement(nullptr, {}, worldL, "world", nullptr, false, 0);

        // Числа — сантиметры сцены DescribeScene, ось z её же.
        auto put = [&](const char* name, G4Material* m, double rIn, double rOut,
                       double z0, double z1)
        {
            auto s = new G4Tubs(name, rIn * cm, rOut * cm, 0.5 * (z1 - z0) * cm,
                                0.0, 360.0 * deg);
            auto l = new G4LogicalVolume(s, m, name);
            new G4PVPlacement(nullptr, G4ThreeVector(0, 0, 0.5 * (z0 + z1) * cm),
                              l, name, worldL, false, 0);
            return l;
        };

        fCrystal = put("crystal", csi, 0.0, 0.927, 0.0, 5.90);
        put("ptfe_front", ptfe, 0.0, 0.927, -0.13, 0.0);
        put("ptfe_side", ptfe, 0.927, 1.027, -0.13, 5.90);
        put("al_front", al, 0.0, 1.227, -0.31, -0.13);
        put("al_side", al, 1.027, 1.227, -0.13, 5.90);
        put("al_back", al, 0.0, 1.227, 5.90, 6.10);
        put("sample", water, 0.0, 1.25, -1.11, -0.51);
        return worldP;
    }

    /// Сцена из файла effsim --dump-scene: те же области, тот же порядок.
    G4VPhysicalVolume* ConstructFromScene()
    {
        auto nist = G4NistManager::Instance();
        auto air = nist->FindOrBuildMaterial(gVacuumWorld ? "G4_Galactic" : "G4_AIR");
        auto worldS = new G4Box("world", 60 * cm, 60 * cm, 60 * cm);
        auto worldL = new G4LogicalVolume(worldS, air, "world");
        auto worldP = new G4PVPlacement(nullptr, {}, worldL, "world", nullptr, false, 0);

        std::map<std::string, G4Material*> mats;
        int matIndex = 0;
        for (auto& entry : gSceneMats)
        {
            auto m = new G4Material("scene_mat_" + std::to_string(matIndex++),
                                    entry.second.density * g / cm3,
                                    int(entry.second.parts.size()));
            for (auto& part : entry.second.parts)
            {
                m->AddElement(nist->FindOrBuildElement(part.first), part.second);
            }

            mats[entry.first] = m;
        }

        // Сцена симулятора — «первая победившая», области перекрываются
        // ВЛОЖЕНИЕМ (кристалл поверх сплошной оболочки). У Geant4 та же
        // семантика достигается расстановкой мать-дочь: дочерний объём
        // перекрывает материнский. Ранняя область — дочь самой МАЛОЙ из
        // более поздних, целиком её содержащих.
        size_t count = gSceneRegions.size();

        // 1. Область, целиком накрытая БОЛЕЕ РАННЕЙ, не выигрывает нигде —
        // выбросить (так наша сцена и считает: до неё поиск не доходит).
        std::vector<bool> dead(count, false);
        for (size_t j = 0; j < count; ++j)
        {
            for (size_t i = 0; i < j && !dead[j]; ++i)
            {
                if (!dead[i] && Inside(gSceneRegions[j], gSceneRegions[i]))
                {
                    std::fprintf(stderr,
                                 "область %zu целиком затенена областью %zu — выброшена "
                                 "(в сцене симулятора она тоже недостижима)\n", j, i);
                    dead[j] = true;
                }
            }
        }

        // 2. Мать каждой области; частичное пересечение без вложения — отказ.
        std::vector<int> mother(count, -1);
        for (size_t i = 0; i < count; ++i)
        {
            if (dead[i])
            {
                continue;
            }

            double best = 1e300;
            for (size_t j = i + 1; j < count; ++j)
            {
                if (dead[j] || !Inside(gSceneRegions[i], gSceneRegions[j]))
                {
                    continue;
                }

                const SceneRegion& m = gSceneRegions[j];
                double volume = (m.z1 - m.z0)
                    * (m.box ? 4.0 * m.a * m.b : CLHEP::pi * (m.b * m.b - m.a * m.a));
                if (volume < best)
                {
                    best = volume;
                    mother[i] = int(j);
                }
            }
        }

        for (size_t i = 0; i < count; ++i)
        {
            for (size_t j = i + 1; j < count; ++j)
            {
                if (dead[i] || dead[j] || mother[i] == int(j) || mother[j] == int(i))
                {
                    continue;
                }

                if (MayOverlap(gSceneRegions[i], gSceneRegions[j])
                    && !Inside(gSceneRegions[i], gSceneRegions[j])
                    && !Inside(gSceneRegions[j], gSceneRegions[i]))
                {
                    std::fprintf(stderr,
                                 "области %zu и %zu пересекаются, но не вложены — "
                                 "сцену в Geant4 честно не построить, ОТКАЗ\n", i, j);
                    std::exit(2);
                }
            }
        }

        // 3. Расстановка: сначала матери (обход от конца списка), потом дети.
        std::vector<G4LogicalVolume*> logicals(count, nullptr);
        for (size_t k = count; k-- > 0;)
        {
            if (dead[k])
            {
                continue;
            }

            const SceneRegion& r = gSceneRegions[k];
            std::string name = "r" + std::to_string(k);
            G4VSolid* solid = r.box
                ? static_cast<G4VSolid*>(new G4Box(name, r.a * cm, r.b * cm,
                                                   0.5 * (r.z1 - r.z0) * cm))
                : static_cast<G4VSolid*>(new G4Tubs(name, r.a * cm, r.b * cm,
                                                    0.5 * (r.z1 - r.z0) * cm,
                                                    0.0, 360.0 * deg));
            logicals[k] = new G4LogicalVolume(solid, mats[r.mat], name);
            if (r.crystal)
            {
                fCrystal = logicals[k];
            }
        }

        for (size_t k = count; k-- > 0;)
        {
            if (dead[k])
            {
                continue;
            }

            const SceneRegion& r = gSceneRegions[k];
            double zc = 0.5 * (r.z0 + r.z1);
            G4LogicalVolume* into = worldL;
            if (mother[k] >= 0)
            {
                const SceneRegion& m = gSceneRegions[mother[k]];
                into = logicals[mother[k]];
                zc -= 0.5 * (m.z0 + m.z1);      // сдвиг относительно матери
            }

            // checkOverlaps=true: вторая линия обороны после своей проверки
            new G4PVPlacement(nullptr, G4ThreeVector(0, 0, zc * cm),
                              logicals[k], "r" + std::to_string(k), into, false, 0, true);
        }

        return worldP;
    }

    static G4LogicalVolume* fCrystal;
};

G4LogicalVolume* Detector::fCrystal = nullptr;

class Physics : public G4VModularPhysicsList
{
public:
    Physics()
    {
        RegisterPhysics(new G4EmStandardPhysics_option4());
        RegisterPhysics(new G4DecayPhysics());
        RegisterPhysics(new G4RadioactiveDecayPhysics());
    }
};

class Generator : public G4VUserPrimaryGeneratorAction
{
public:
    void GeneratePrimaries(G4Event* event) override { fGps.GeneratePrimaryVertex(event); }

private:
    G4GeneralParticleSource fGps;
};

namespace
{
    // Параметры первички сценного генератора; заполняются в main.
    double gSceneEnergyKev = 0.0;
    bool gSceneIon = false;
    int gSceneZ = 0, gSceneA = 0;
}

/// Генератор сценного режима: положение — ТОЧНАЯ копия сэмплеров
/// EfficiencySimulator (равномерно по объёму, радиус корнем, у маринелли
/// крышка долей объёма), направление изотропно, частица — гамма или ион.
class SceneGenerator : public G4VUserPrimaryGeneratorAction
{
public:
    void GeneratePrimaries(G4Event* event) override
    {
        const SceneSource& s = gSceneSource;
        double x = 0.0, y = 0.0, z = 0.0;
        if (s.kind == "point")
        {
            z = s.p[0];
        }
        else if (s.kind == "cyl")
        {
            double rr = s.p[0] * std::sqrt(G4UniformRand());
            double phi = 2.0 * CLHEP::pi * G4UniformRand();
            x = rr * std::cos(phi);
            y = rr * std::sin(phi);
            z = s.p[1] + (s.p[2] - s.p[1]) * G4UniformRand();
        }
        else if (s.kind == "box")
        {
            x = s.p[0] * (2.0 * G4UniformRand() - 1.0);
            y = s.p[1] * (2.0 * G4UniformRand() - 1.0);
            z = s.p[2] + (s.p[3] - s.p[2]) * G4UniformRand();
        }
        else                                        // marinelli
        {
            double rIn = s.p[0], rOut = s.p[1], z0 = s.p[2], z1 = s.p[3], zCap = s.p[4];
            double annulus = (rOut * rOut - rIn * rIn) * (z1 - z0);
            double cap = rIn * rIn * std::max(0.0, zCap - z0);
            double capFraction = (annulus + cap) > 0.0 ? cap / (annulus + cap) : 0.0;
            double rr, zz;
            if (G4UniformRand() < capFraction)
            {
                rr = rIn * std::sqrt(G4UniformRand());
                zz = z0 + (zCap - z0) * G4UniformRand();
            }
            else
            {
                double a = rIn * rIn, b = rOut * rOut;
                rr = std::sqrt(a + (b - a) * G4UniformRand());
                zz = z0 + (z1 - z0) * G4UniformRand();
            }

            double phi = 2.0 * CLHEP::pi * G4UniformRand();
            x = rr * std::cos(phi);
            y = rr * std::sin(phi);
            z = zz;
        }

        auto vertex = new G4PrimaryVertex(
            G4ThreeVector(x * cm, y * cm, z * cm), 0.0);
        if (gSceneIon)
        {
            auto ion = G4IonTable::GetIonTable()->GetIon(gSceneZ, gSceneA, 0.0);
            auto particle = new G4PrimaryParticle(ion);
            particle->SetKineticEnergy(0.0);
            particle->SetCharge(0.0);
            vertex->SetPrimary(particle);
        }
        else
        {
            double cosT = 2.0 * G4UniformRand() - 1.0;
            double sinT = std::sqrt(std::max(0.0, 1.0 - cosT * cosT));
            double phi = 2.0 * CLHEP::pi * G4UniformRand();
            gPrimaryCos = cosT;
            auto particle = new G4PrimaryParticle(
                G4Gamma::GammaDefinition(),
                sinT * std::cos(phi) * gSceneEnergyKev * keV,
                sinT * std::sin(phi) * gSceneEnergyKev * keV,
                cosT * gSceneEnergyKev * keV);
            vertex->SetPrimary(particle);
        }

        event->AddPrimaryVertex(vertex);
    }
};

/// Счётчики — канонический паттерн B1: экземпляр и у мастера, и у каждого
/// потока; Merge в конце сливает, мастер печатает.
class RunAction : public G4UserRunAction
{
public:
    RunAction() : fAny("any", 0), fAngN("angN", 0), fAngS2("angS2", 0.0), fAngS4("angS4", 0.0),
                  fAngS22("angS22", 0.0), fAngS44("angS44", 0.0),
                  fQkN("qkN", 0), fQkS2("qkS2", 0.0), fQkS4("qkS4", 0.0),
                  fQtN("qtN", 0), fQtS2("qtS2", 0.0), fQtS4("qtS4", 0.0)
    {
        auto manager = G4AccumulableManager::Instance();
        manager->Register(fAny);
        manager->Register(fQkN);
        manager->Register(fQkS2);
        manager->Register(fQkS4);
        manager->Register(fQtN);
        manager->Register(fQtS2);
        manager->Register(fQtS4);
        manager->Register(fAngN);
        manager->Register(fAngS2);
        manager->Register(fAngS4);
        manager->Register(fAngS22);
        manager->Register(fAngS44);
        for (int i = 0; i < kAngBins; ++i)
        {
            fAngHist.push_back(new G4Accumulable<G4int>("ah" + std::to_string(i), 0));
            manager->Register(*fAngHist.back());
        }
        for (size_t i = 0; i < gWindows.size(); ++i)
        {
            fPeaks.push_back(new G4Accumulable<G4int>("w" + std::to_string(i), 0));
            manager->Register(*fPeaks.back());
        }

        for (int i = 0; i < gHistBins; ++i)
        {
            fHist.push_back(new G4Accumulable<G4int>("h" + std::to_string(i), 0));
            manager->Register(*fHist.back());
        }

        // (П106) Счётчики населений вылета/возврата — см. EscWhat.
        for (int i = 0; i < kEscN; ++i)
        {
            fEsc.push_back(new G4Accumulable<G4int>("esc" + std::to_string(i), 0));
            manager->Register(*fEsc.back());
        }
    }

    /// (П106) Что считать в `ESCPOP`.
    enum EscWhat
    {
        EscOwnExit = 0,      // выходов своего (рождённого в кристалле) e- из кристалла
        EscCarryExit,        // выходов занесённого (рождённого вне) e- из кристалла
        EscOwnReturn,        // повторных входов своего e- (всего)
        EscOwnReturnSame,    // из них через ту же грань, что последний выход
        EscOwnReturnOther,   // через другую грань
        EscOwnReturnNoFace,  // грань не определена (сцены нет или нет записи выхода)
        EscLineageGammaOut,  // квантов, рождённых вне кристалла в родословной вылетевшего
        EscLineageLeptonOut, // e-/e+ там же
        EscKilledGamma,      // погашено квантов (killescbrem)
        EscKilledLepton,     // погашено e-/e+ (killescdelta)
        EscFaceMiss,         // самопроверка: точка выхода/входа дальше 1 мкм от всех граней
        EscLineageInside,    // самопроверка: потомок в родословной «вне» сделал первый шаг ИЗ кристалла
        EscOutLepton,        // лептонов (e-/e+), рождённых вне кристалла (не в родословной вылетевших своих)
        EscOutBremGamma,     // квантов, рождённых вне кристалла такими лептонами
        EscKilledOutBrem,    // из них погашено (killoutbrem)
        EscOutAnnihGamma,    // квантов аннигиляции позитрона, рождённого вне кристалла (рычаг их не трогает)
        EscOutOtherGamma,    // прочих квантов от лептонов вне кристалла (самопроверка: ожидается ~0)
        EscOutLeptonCompt,   // из лептонов вне кристалла: рождённых комптоном
        EscOutLeptonPhot,    // фотоэффектом
        EscOutLeptonConv,    // парой
        EscOutLeptonIoni,    // ионизацией (δ-электроны)
        EscOutLeptonAbove20, // из всех: с кинетикой ≥ 20 кэВ (наш порог переноса в слоях)
        kEscN
    };

    void CountEsc(EscWhat what) { *fEsc[what] += 1; }

    void BeginOfRunAction(const G4Run*) override
    {
        G4AccumulableManager::Instance()->Reset();
    }

    void EndOfRunAction(const G4Run* run) override
    {
        G4AccumulableManager::Instance()->Merge();
        if (!IsMaster())
        {
            if (gTagMode)
            {
                FlushThreadTags();
            }

            return;
        }

        long decays = run->GetNumberOfEvent();
        std::printf("RESULT decays=%ld\n", decays);
        if (fQkN.GetValue() > 0 || fQtN.GetValue() > 0)
        {
            long n = fQkN.GetValue(), nt = fQtN.GetValue();
            std::printf("QK window=%.3f peak_events=%ld Q2=%.5f Q4=%.5f\n", gWindows.empty() ? 0.0 : gWindows[0], n,
                        n > 0 ? fQkS2.GetValue() / n : 0.0, n > 0 ? fQkS4.GetValue() / n : 0.0);
            std::printf("QKT any_events=%ld Q2T=%.5f Q4T=%.5f\n", nt,
                        nt > 0 ? fQtS2.GetValue() / nt : 0.0, nt > 0 ? fQtS4.GetValue() / nt : 0.0);
        }

        if (gAngCorrMode)
        {
            // W(cos θ) = 1 + A22·P2 + A44·P4 при нормировке <W> = 1 даёт
            // <P_k> = A_kk/(2k+1); σ — по разбросу P_k в выборке.
            long n = fAngN.GetValue();
            double m2 = n > 0 ? fAngS2.GetValue() / n : 0.0;
            double m4 = n > 0 ? fAngS4.GetValue() / n : 0.0;
            double v2 = n > 1 ? (fAngS22.GetValue() / n - m2 * m2) / (n - 1) : 0.0;
            double v4 = n > 1 ? (fAngS44.GetValue() / n - m4 * m4) / (n - 1) : 0.0;
            std::printf("ANGCORR pairs=%ld A22=%.5f sigma=%.5f A44=%.5f sigma=%.5f E1=%.1f E2=%.1f corr=%d\n",
                        n, 5.0 * m2, 5.0 * std::sqrt(std::max(0.0, v2)),
                        9.0 * m4, 9.0 * std::sqrt(std::max(0.0, v4)),
                        gAngE1Kev, gAngE2Kev, gCorrelatedGamma ? 1 : 0);
            for (int i = 0; i < kAngBins; ++i)
            {
                std::printf("ANGHIST %d %.2f %.2f %d\n", i, -1.0 + 2.0 * i / kAngBins,
                            -1.0 + 2.0 * (i + 1) / kAngBins, fAngHist[i]->GetValue());
            }
        }
        std::printf("RESULT any=%d eps_total=%.6e\n", fAny.GetValue(),
                    decays > 0 ? double(fAny.GetValue()) / decays : 0.0);
        for (size_t i = 0; i < gWindows.size(); ++i)
        {
            std::printf("RESULT window=%.3f counts=%d eps=%.6e\n",
                        gWindows[i], fPeaks[i]->GetValue(),
                        decays > 0 ? double(fPeaks[i]->GetValue()) / decays : 0.0);
        }

        if (gHistBins > 0)
        {
            std::printf("HISTBEGIN bins=%d bin_kev=%.6f decays=%ld\n",
                        gHistBins, gHistBinKev, decays);
            for (int i = 0; i < gHistBins; ++i)
            {
                if (fHist[i]->GetValue() > 0)
                {
                    std::printf("HIST %d %d\n", i, fHist[i]->GetValue());
                }
            }

            std::printf("HISTEND\n");
        }

        if (gEscPop)
        {
            // (П106) Населения вылета/возврата — читатель рычагов killesc*/killret*.
            std::printf("ESCPOP decays=%ld own_exit=%d carry_exit=%d own_return=%d same=%d other=%d noface=%d"
                        " lineage_gamma_out=%d lineage_lepton_out=%d killed_gamma=%d killed_lepton=%d"
                        " facemiss=%d lineage_inside=%d out_lepton=%d out_brem_gamma=%d killed_out_brem=%d out_annih_gamma=%d out_other_gamma=%d"
                        " out_lepton_compt=%d out_lepton_phot=%d out_lepton_conv=%d out_lepton_ioni=%d out_lepton_ge20kev=%d\n",
                        decays, fEsc[EscOwnExit]->GetValue(), fEsc[EscCarryExit]->GetValue(),
                        fEsc[EscOwnReturn]->GetValue(), fEsc[EscOwnReturnSame]->GetValue(),
                        fEsc[EscOwnReturnOther]->GetValue(), fEsc[EscOwnReturnNoFace]->GetValue(),
                        fEsc[EscLineageGammaOut]->GetValue(), fEsc[EscLineageLeptonOut]->GetValue(),
                        fEsc[EscKilledGamma]->GetValue(), fEsc[EscKilledLepton]->GetValue(),
                        fEsc[EscFaceMiss]->GetValue(), fEsc[EscLineageInside]->GetValue(),
                        fEsc[EscOutLepton]->GetValue(), fEsc[EscOutBremGamma]->GetValue(),
                        fEsc[EscKilledOutBrem]->GetValue(), fEsc[EscOutAnnihGamma]->GetValue(), fEsc[EscOutOtherGamma]->GetValue(),
                        fEsc[EscOutLeptonCompt]->GetValue(), fEsc[EscOutLeptonPhot]->GetValue(), fEsc[EscOutLeptonConv]->GetValue(),
                        fEsc[EscOutLeptonIoni]->GetValue(), fEsc[EscOutLeptonAbove20]->GetValue());
        }

        if (gTagMode)
        {
            std::lock_guard<std::mutex> lock(gTagMutex);
            std::printf("EMITBEGIN events=%ld bin_kev=0.01\n", gEmitEvents);
            for (auto& kv : gEmit)
            {
                std::printf("EMIT %.2f %ld\n", kv.first * 0.01, kv.second);
            }

            std::printf("EMITEND\n");
            for (size_t k = 0; k < gEmitPairs.size() && k < gEmitPairN1.size(); ++k)
            {
                std::printf("EMITPAIR %.3f %.3f n1=%ld n12=%ld P=%.6f\n", gEmitPairs[k].first, gEmitPairs[k].second,
                            gEmitPairN1[k], gEmitPairN12[k],
                            gEmitPairN1[k] > 0 ? double(gEmitPairN12[k]) / gEmitPairN1[k] : 0.0);
            }
            for (size_t i = 0; i < gTagCounts.size(); ++i)
            {
                std::printf("TAG window=%.3f total=%ld distinct=%zu\n", gWindows[i], gTagTotals[i],
                            gTagCounts[i].size());
                std::vector<std::pair<std::string, long>> rows(gTagCounts[i].begin(), gTagCounts[i].end());
                std::sort(rows.begin(), rows.end(),
                          [](const std::pair<std::string, long>& a, const std::pair<std::string, long>& b)
                          { return a.second > b.second; });
                size_t shown = 0;
                for (auto& row : rows)
                {
                    if (shown++ >= 60) { break; }
                    std::printf("TAGSIG window=%.3f n=%ld sig=%s\n", gWindows[i], row.second, row.first.c_str());
                }

                if (i < gFullCounts.size())
                {
                    std::vector<std::pair<std::string, long>> fulls(gFullCounts[i].begin(), gFullCounts[i].end());
                    std::sort(fulls.begin(), fulls.end(),
                              [](const std::pair<std::string, long>& a, const std::pair<std::string, long>& b)
                              { return a.second > b.second; });
                    for (auto& row : fulls)
                    {
                        std::printf("TAGFULL window=%.3f n=%ld full=%s\n", gWindows[i], row.second, row.first.c_str());
                    }
                }
            }

            std::printf("TAGEND\n");
        }

        std::fflush(stdout);
    }

    /// Пара квантов распада с косинусом угла между направлениями.
    void CountPair(double cosTheta)
    {
        double c2 = cosTheta * cosTheta;
        double p2 = 0.5 * (3.0 * c2 - 1.0);
        double p4 = 0.125 * (35.0 * c2 * c2 - 30.0 * c2 + 3.0);
        fAngN += 1;
        fAngS2 += p2;
        fAngS4 += p4;
        fAngS22 += p2 * p2;
        fAngS44 += p4 * p4;
        int bin = int((cosTheta + 1.0) * 0.5 * kAngBins);
        if (bin < 0) { bin = 0; }
        if (bin >= kAngBins) { bin = kAngBins - 1; }
        *fAngHist[bin] += 1;
    }

    void Count(double edepKev)
    {
        if (edepKev > 1e-3)
        {
            fAny += 1;
        }

        // Q_k арбитра: cos θ первички (только сценный моно-режим) по событиям
        // пика первого окна и по событиям с любым вкладом.
        if (gPrimaryCos <= 1.0 && !gWindows.empty())
        {
            double c2 = gPrimaryCos * gPrimaryCos;
            double p2 = 0.5 * (3.0 * c2 - 1.0);
            double p4 = 0.125 * (35.0 * c2 * c2 - 30.0 * c2 + 3.0);
            if (edepKev > 1e-3)
            {
                fQtN += 1;
                fQtS2 += p2;
                fQtS4 += p4;
            }

            if (std::fabs(edepKev - gWindows[0]) < kHalfWindowKev)
            {
                fQkN += 1;
                fQkS2 += p2;
                fQkS4 += p4;
            }
        }

        for (size_t i = 0; i < gWindows.size(); ++i)
        {
            if (std::fabs(edepKev - gWindows[i]) < kHalfWindowKev)
            {
                *fPeaks[i] += 1;
            }
        }

        if (gHistBins > 0 && edepKev > 1e-3)
        {
            int bin = int(edepKev / gHistBinKev + 0.5);
            if (bin >= gHistBins)
            {
                bin = gHistBins - 1;
            }

            *fHist[bin] += 1;
        }
    }

private:
    G4Accumulable<G4int> fAny;
    std::vector<G4Accumulable<G4int>*> fPeaks;
    std::vector<G4Accumulable<G4int>*> fHist;
    std::vector<G4Accumulable<G4int>*> fEsc;      // (П106) населения вылета/возврата
    G4Accumulable<G4int> fAngN;
    G4Accumulable<G4double> fAngS2, fAngS4, fAngS22, fAngS44;
    std::vector<G4Accumulable<G4int>*> fAngHist;
    G4Accumulable<G4int> fQkN;
    G4Accumulable<G4double> fQkS2, fQkS4;
    G4Accumulable<G4int> fQtN;
    G4Accumulable<G4double> fQtS2, fQtS4;
};

class EventAction : public G4UserEventAction
{
public:
    explicit EventAction(RunAction* run) : fRun(run) {}

    void BeginOfEventAction(const G4Event*) override
    {
        fEdepKev = 0.0;
        fGammas.clear();
        fGammaDep.clear();
        fAncestor.clear();
        if (gEscPop)
        {
            fEscLineage.clear();
            fExitFace.clear();
            fOutLeptons.clear();
        }
    }

    // ---- (П106) Лептоны, рождённые вне кристалла (обвязка, проба), — родители
    // квантов рычага `killoutbrem`.
    void MarkOutLepton(int trackId) { fOutLeptons.insert(trackId); }
    bool IsOutLepton(int trackId) const { return fOutLeptons.count(trackId) != 0; }

    // ---- (П106) Родословная вылетевших своих e-: сам e- (рождён в кристалле,
    // покидал его) и все его потомки, где бы ни родились. Грань последнего
    // выхода — по треку, для killretsame/killretother.
    void MarkEscaped(int trackId, int face)
    {
        fEscLineage.insert(trackId);
        fExitFace[trackId] = face;
    }

    bool InLineage(int trackId) const { return fEscLineage.count(trackId) != 0; }

    void Inherit106(int trackId, int parentId)
    {
        if (fEscLineage.count(parentId) != 0)
        {
            fEscLineage.insert(trackId);
        }
    }

    /// Грань последнего выхода трека; 0 — записи нет.
    int ExitFace(int trackId) const
    {
        auto it = fExitFace.find(trackId);
        return it == fExitFace.end() ? 0 : it->second;
    }

    RunAction* Run() const { return fRun; }

    void EndOfEventAction(const G4Event*) override
    {
        if (gAngCorrMode)
        {
            // Ровно один квант E1 и ровно один E2 (±1 кэВ) — иначе пара не та.
            int i1 = -1, i2 = -1, n1 = 0, n2 = 0;
            for (size_t i = 0; i < fGammas.size(); ++i)
            {
                if (std::fabs(fGammas[i].first - gAngE1Kev) < 1.0) { i1 = int(i); ++n1; }
                else if (std::fabs(fGammas[i].first - gAngE2Kev) < 1.0) { i2 = int(i); ++n2; }
            }

            if (n1 == 1 && n2 == 1)
            {
                fRun->CountPair(fGammas[i1].second.dot(fGammas[i2].second));
            }

            return;
        }

        fRun->Count(fEdepKev);
        if (gTagMode)
        {
            Tag();
        }
    }

    void Add(double edepKev) { fEdepKev += edepKev; }

    void AddGamma(double eKev, const G4ThreeVector& dir) { fGammas.emplace_back(eKev, dir); }

    /// `iontag`: квант РДМ с треком `trackId` — предок самому себе.
    void AddTaggedGamma(int trackId, double eKev, const G4ThreeVector& dir)
    {
        fGammas.emplace_back(eKev, dir);
        fGammaDep.push_back(0.0);
        fAncestor[trackId] = int(fGammas.size()) - 1;
    }

    /// `iontag`: потомок наследует предка-квант родителя (если тот известен).
    void Inherit(int trackId, int parentId)
    {
        auto it = fAncestor.find(parentId);
        if (it != fAncestor.end())
        {
            fAncestor[trackId] = it->second;
        }
    }

    /// `iontag`: вклад шага трека — на счёт его предка-кванта.
    void AddTo(int trackId, double edepKev)
    {
        auto it = fAncestor.find(trackId);
        if (it != fAncestor.end())
        {
            fGammaDep[it->second] += edepKev;
        }
    }

    /// `iontag`: подписи окон и спектр эмиссии этого события — в локальные карты.
    void Tag()
    {
        ++fEmitEvents;
        for (auto& g : fGammas)
        {
            ++fEmit[int(g.first * 100.0 + 0.5)];
        }

        if (gEmitPairMode)
        {
            if (fPairN1.size() < gEmitPairs.size())
            {
                fPairN1.assign(gEmitPairs.size(), 0);
                fPairN12.assign(gEmitPairs.size(), 0);
            }

            for (size_t k = 0; k < gEmitPairs.size(); ++k)
            {
                bool has1 = false, has2 = false;
                int i1 = -1;
                for (size_t q = 0; q < fGammas.size(); ++q)
                {
                    if (!has1 && std::fabs(fGammas[q].first - gEmitPairs[k].first) < 0.35) { has1 = true; i1 = int(q); }
                }

                if (!has1) { continue; }
                for (size_t q = 0; q < fGammas.size(); ++q)
                {
                    if (int(q) != i1 && std::fabs(fGammas[q].first - gEmitPairs[k].second) < 0.35) { has2 = true; break; }
                }

                ++fPairN1[k];
                if (has2) { ++fPairN12[k]; }
            }
        }

        for (size_t i = 0; i < gWindows.size(); ++i)
        {
            if (std::fabs(fEdepKev - gWindows[i]) >= kHalfWindowKev)
            {
                continue;
            }

            std::vector<std::pair<double, bool>> es;
            for (size_t k = 0; k < fGammas.size(); ++k)
            {
                if (fGammas[k].first > 1.0)
                {
                    bool full = k < fGammaDep.size() && std::fabs(fGammaDep[k] - fGammas[k].first) < kHalfWindowKev;
                    es.emplace_back(fGammas[k].first, full);
                }
            }

            std::sort(es.begin(), es.end(),
                      [](const std::pair<double, bool>& a, const std::pair<double, bool>& b) { return a.first > b.first; });
            std::string sig;
            char buf[32];
            for (auto& e : es)
            {
                std::snprintf(buf, sizeof buf, "%.1f%s", e.first, e.second ? "*" : "");
                if (!sig.empty()) { sig += "+"; }
                sig += buf;
            }

            if (sig.empty()) { sig = "-"; }
            ++fTags[i][sig];
            ++fTagTotals[i];

            // Сводка по множеству ЦЕЛИКОМ поглощённых квантов — ключ короткий,
            // печатается без среза (подписей с хвостом L-рентгена — сотни).
            std::string full;
            for (auto& e : es)
            {
                if (!e.second) { continue; }
                std::snprintf(buf, sizeof buf, "%.1f", e.first);
                if (!full.empty()) { full += "+"; }
                full += buf;
            }

            if (full.empty()) { full = "-"; }
            ++fFull[i][full];
        }
    }

    /// Слив локальных карт в общие (под замком); зовётся из EndOfRunAction рабочего.
    void Flush()
    {
        std::lock_guard<std::mutex> lock(gTagMutex);
        if (gTagCounts.size() < gWindows.size())
        {
            gTagCounts.resize(gWindows.size());
            gTagTotals.resize(gWindows.size(), 0);
        }

        if (gFullCounts.size() < gWindows.size())
        {
            gFullCounts.resize(gWindows.size());
        }

        for (size_t i = 0; i < fTags.size(); ++i)
        {
            for (auto& kv : fTags[i]) { gTagCounts[i][kv.first] += kv.second; }
            for (auto& kv : fFull[i]) { gFullCounts[i][kv.first] += kv.second; }
            gTagTotals[i] += fTagTotals[i];
            fTags[i].clear();
            fFull[i].clear();
            fTagTotals[i] = 0;
        }

        for (auto& kv : fEmit) { gEmit[kv.first] += kv.second; }
        fEmit.clear();
        gEmitEvents += fEmitEvents;
        fEmitEvents = 0;
        if (gEmitPairMode)
        {
            if (gEmitPairN1.size() < gEmitPairs.size())
            {
                gEmitPairN1.assign(gEmitPairs.size(), 0);
                gEmitPairN12.assign(gEmitPairs.size(), 0);
            }

            for (size_t k = 0; k < fPairN1.size(); ++k)
            {
                gEmitPairN1[k] += fPairN1[k];
                gEmitPairN12[k] += fPairN12[k];
                fPairN1[k] = 0;
                fPairN12[k] = 0;
            }
        }
    }

    void PrepareTag()
    {
        fTags.assign(gWindows.size(), {});
        fFull.assign(gWindows.size(), {});
        fTagTotals.assign(gWindows.size(), 0);
    }

private:
    RunAction* fRun;
    double fEdepKev = 0.0;
    std::vector<std::pair<double, G4ThreeVector>> fGammas;
    std::vector<double> fGammaDep;              // `iontag`: вклад потомков кванта в кристалл, кэВ
    std::map<int, int> fAncestor;               // `iontag`: трек → индекс предка-кванта
    std::vector<std::map<std::string, long>> fTags;
    std::vector<std::map<std::string, long>> fFull;   // окно → множество целиком поглощённых → n
    std::vector<long> fTagTotals;
    std::vector<long> fPairN1, fPairN12;              // `emitpair`
    std::map<int, long> fEmit;
    long fEmitEvents = 0;
    std::set<int> fEscLineage;                        // (П106) родословная вылетевших своих e-
    std::map<int, int> fExitFace;                     // (П106) трек → грань последнего выхода
    std::set<int> fOutLeptons;                        // (П106) e-/e+, рождённые вне кристалла
};

namespace
{
    // Указатель на EventAction своего потока — чтобы EndOfRunAction рабочего слил его карты.
    G4ThreadLocal EventAction* gThreadEvent = nullptr;

    void FlushThreadTags()
    {
        if (gThreadEvent != nullptr)
        {
            gThreadEvent->Flush();
        }
    }
}

/// Режим `angcorr`: квант распада записывается в момент постановки в стек и
/// ГАСИТСЯ (перенос не нужен); ион и всё прочее идут как обычно.
class StackingAction : public G4UserStackingAction
{
public:
    explicit StackingAction(EventAction* event) : fEvent(event) {}

    G4ClassificationOfNewTrack ClassifyNewTrack(const G4Track* track) override
    {
        // (П106) Родословная вылетевшего своего e-: потомок, РОЖДЁННЫЙ ВНЕ
        // кристалла, наследует метку, считается (квант / лептон) и по рычагу
        // гасится. Потомок, рождённый в кристалле (тормозное вернувшегося e-),
        // метки не наследует — это уже следствие возврата, а не судьба снаружи.
        if (gEscPop && track->GetParentID() > 0 && fEvent->InLineage(track->GetParentID())
            && !InCrystalGeom(track->GetPosition()))
        {
            fEvent->Inherit106(track->GetTrackID(), track->GetParentID());
            bool gamma = track->GetDefinition() == G4Gamma::GammaDefinition();
            int pdg = std::abs(track->GetDefinition()->GetPDGEncoding());
            bool lepton = pdg == 11;
            if (gamma)
            {
                fEvent->Run()->CountEsc(RunAction::EscLineageGammaOut);
                if (gKillEscBrem)
                {
                    fEvent->Run()->CountEsc(RunAction::EscKilledGamma);
                    return fKill;
                }
            }
            else if (lepton)
            {
                fEvent->Run()->CountEsc(RunAction::EscLineageLeptonOut);
                if (gKillEscDelta)
                {
                    fEvent->Run()->CountEsc(RunAction::EscKilledLepton);
                    return fKill;
                }
            }
        }
        else if (gEscPop && track->GetParentID() > 0 && !InCrystalGeom(track->GetPosition()))
        {
            // (П106) Вне родословной вылетевших: лептон, рождённый вне кристалла,
            // запоминается; квант от такого лептона — тормозное/флуоресценция
            // обвязки (`killoutbrem`).
            int pdg = std::abs(track->GetDefinition()->GetPDGEncoding());
            if (pdg == 11)
            {
                fEvent->MarkOutLepton(track->GetTrackID());
                fEvent->Run()->CountEsc(RunAction::EscOutLepton);
                // Население по процессу-создателю и по нашему порогу переноса (20 кэВ).
                const G4VProcess* cp = track->GetCreatorProcess();
                std::string cname = cp != nullptr ? cp->GetProcessName() : std::string();
                if (cname.find("compt") != std::string::npos) { fEvent->Run()->CountEsc(RunAction::EscOutLeptonCompt); }
                else if (cname.find("phot") != std::string::npos) { fEvent->Run()->CountEsc(RunAction::EscOutLeptonPhot); }
                else if (cname.find("conv") != std::string::npos) { fEvent->Run()->CountEsc(RunAction::EscOutLeptonConv); }
                else if (cname.find("Ioni") != std::string::npos) { fEvent->Run()->CountEsc(RunAction::EscOutLeptonIoni); }
                if (track->GetKineticEnergy() >= 20.0 * keV) { fEvent->Run()->CountEsc(RunAction::EscOutLeptonAbove20); }
            }
            else if (track->GetDefinition() == G4Gamma::GammaDefinition() && fEvent->IsOutLepton(track->GetParentID()))
            {
                // ⚠ Только ТОРМОЗНОЕ (`eBrem`): у позитрона, рождённого в обвязке
                // парой, кванты аннигиляции тоже «рождены вне кристалла лептоном,
                // рождённым вне», но это не тормозное — наша сторона ведёт их
                // фотонным обходом (`A52`), и рычаг их не трогает. Первая редакция
                // рычага (П106, 02:00) гасила и их: плечо арбитра выходило на
                // 511-кэВных квантах шире нашего `--ret-kill=outbrem`, особенно у
                // ториевого стекла (пары ∝ Z²). Флуоресценции от ионизации
                // электронами у option4 нет (PIXE выкл) — прочие кванты считаются
                // отдельно как самопроверка.
                const G4VProcess* creator = track->GetCreatorProcess();
                std::string pname = creator != nullptr ? creator->GetProcessName() : std::string();
                if (pname.find("annihil") != std::string::npos)
                {
                    fEvent->Run()->CountEsc(RunAction::EscOutAnnihGamma);
                }
                else if (pname.find("Brem") != std::string::npos)
                {
                    fEvent->Run()->CountEsc(RunAction::EscOutBremGamma);
                    if (gKillOutBrem)
                    {
                        fEvent->Run()->CountEsc(RunAction::EscKilledOutBrem);
                        return fKill;
                    }
                }
                else
                {
                    fEvent->Run()->CountEsc(RunAction::EscOutOtherGamma);
                }
            }
        }

        if ((!gAngCorrMode && !gTagMode) || track->GetParentID() <= 0)
        {
            return fUrgent;
        }

        if (track->GetDefinition() == G4Gamma::GammaDefinition())
        {
            const G4VProcess* creator = track->GetCreatorProcess();
            if (creator != nullptr && creator->GetProcessName().find("adioactiv") != std::string::npos)
            {
                if (gTagMode)
                {
                    fEvent->AddTaggedGamma(track->GetTrackID(), track->GetKineticEnergy() / keV,
                                           track->GetMomentumDirection());
                }
                else
                {
                    fEvent->AddGamma(track->GetKineticEnergy() / keV, track->GetMomentumDirection());
                }
            }
            else if (gTagMode)
            {
                fEvent->Inherit(track->GetTrackID(), track->GetParentID());
            }

            // `iontag` только записывает квант — перенос идёт как обычно;
            // `emitpair` без детектора — квант гасится, как в `angcorr`.
            return (gAngCorrMode || gEmitPairMode) ? fKill : fUrgent;
        }

        if (gEmitPairMode && track->GetDefinition()->GetPDGEncoding() < 1000000000)
        {
            return fKill;      // электроны/нейтрино распада не нужны, ионы (дочерние состояния) — нужны
        }

        if (gTagMode)
        {
            // Электроны, позитроны и прочее: наследуют предка-квант родителя.
            fEvent->Inherit(track->GetTrackID(), track->GetParentID());
            return fUrgent;
        }

        // электроны/нейтрино распада тоже не нужны; ионы (дочерние состояния) — нужны
        if (track->GetDefinition()->GetPDGEncoding() < 1000000000)
        {
            return fKill;
        }

        return fUrgent;
    }

private:
    EventAction* fEvent;
};

class SteppingAction : public G4UserSteppingAction
{
public:
    explicit SteppingAction(EventAction* event) : fEvent(event) {}

    void UserSteppingAction(const G4Step* step) override
    {
        bool preInCrystal = step->GetPreStepPoint()->GetTouchableHandle()->GetVolume()
                ->GetLogicalVolume() == Detector::fCrystal;
        if (preInCrystal)
        {
            fEvent->Add(step->GetTotalEnergyDeposit() / keV);
            if (gTagMode)
            {
                fEvent->AddTo(step->GetTrack()->GetTrackID(), step->GetTotalEnergyDeposit() / keV);
            }
        }

        // (П106) Населения вылета/возврата e- по грани кристалла — см. шапку
        // рычагов killescown/killesccarry/killret*/killescbrem/killescdelta.
        if (gEscPop && step->GetTrack()->GetDefinition() == G4Electron::Definition())
        {
            G4Track* track = step->GetTrack();
            RunAction* run = fEvent->Run();
            // Самопроверка геометрического теста стека: потомок, помеченный
            // «рождён вне кристалла», первый шаг делает ИЗ кристалла — расхождение.
            if (track->GetCurrentStepNumber() == 1 && track->GetParentID() > 0 && preInCrystal
                && fEvent->InLineage(track->GetTrackID()))
            {
                run->CountEsc(RunAction::EscLineageInside);
            }

            auto postVol = step->GetPostStepPoint()->GetTouchableHandle()->GetVolume();
            bool postIn = postVol != nullptr && postVol->GetLogicalVolume() == Detector::fCrystal;
            if (preInCrystal != postIn)
            {
                bool own = track->GetLogicalVolumeAtVertex() == Detector::fCrystal;
                bool miss = false;
                int face = CrystalFace(step->GetPostStepPoint()->GetPosition(), miss);
                if (miss)
                {
                    run->CountEsc(RunAction::EscFaceMiss);
                }

                if (preInCrystal)
                {
                    // Выход из кристалла.
                    if (own)
                    {
                        run->CountEsc(RunAction::EscOwnExit);
                        fEvent->MarkEscaped(track->GetTrackID(), face);
                        if (gKillEscOwn)
                        {
                            track->SetTrackStatus(fStopAndKill);
                        }
                    }
                    else
                    {
                        run->CountEsc(RunAction::EscCarryExit);
                        if (gKillEscCarry)
                        {
                            track->SetTrackStatus(fStopAndKill);
                        }
                    }
                }
                else if (own && fEvent->InLineage(track->GetTrackID()))
                {
                    // Повторный вход своего e-, уже покидавшего кристалл.
                    run->CountEsc(RunAction::EscOwnReturn);
                    int exitFace = fEvent->ExitFace(track->GetTrackID());
                    bool known = face != 0 && exitFace != 0;
                    bool same = known && face == exitFace;
                    run->CountEsc(!known ? RunAction::EscOwnReturnNoFace
                                  : same ? RunAction::EscOwnReturnSame : RunAction::EscOwnReturnOther);
                    if (gKillRet || (gKillRetSame && same) || (gKillRetOther && known && !same))
                    {
                        track->SetTrackStatus(fStopAndKill);
                    }
                }
            }
        }

        if (!gKillEscape && !gKillCarry && !gFullCarry)
        {
            return;
        }

        // Рычаги П55/П92 (см. шапку): судьба электрона на грани кристалла.
        // Вклад шага в кристалл уже засчитан выше; остаток энергии пропадает
        // — ровно как у EfficiencySimulator, где вылетевший e- обратно не
        // ведётся (killesc), а при detour=0 заносимый e- до кристалла не
        // доходит (killcarry).
        // Только e-: позитрон в обоих рычагах не трогается (его 511 кэВ у нас
        // летят из точки рождения пары независимо от заноса кинетики).
        if (step->GetTrack()->GetDefinition() != G4Electron::Definition())
        {
            return;
        }

        auto postVolume = step->GetPostStepPoint()->GetTouchableHandle()->GetVolume();
        bool postInCrystal = postVolume != nullptr && postVolume->GetLogicalVolume() == Detector::fCrystal;
        if ((gKillEscape && preInCrystal && !postInCrystal) || (gKillCarry && !preInCrystal && postInCrystal))
        {
            step->GetTrack()->SetTrackStatus(fStopAndKill);
        }

        // fullcarry (П92): занесённый e- выходит из кристалла — остаток его
        // кинетики засчитывается кристаллу, трек гасится (наше приближение).
        if (gFullCarry && preInCrystal && !postInCrystal
            && step->GetTrack()->GetLogicalVolumeAtVertex() != Detector::fCrystal)
        {
            fEvent->Add(step->GetPostStepPoint()->GetKineticEnergy() / keV);
            step->GetTrack()->SetTrackStatus(fStopAndKill);
        }
    }

private:
    EventAction* fEvent;
};

class Actions : public G4VUserActionInitialization
{
public:
    void Build() const override
    {
        if (gSceneLoaded)
        {
            SetUserAction(new SceneGenerator());
        }
        else
        {
            SetUserAction(new Generator());
        }

        auto run = new RunAction();
        SetUserAction(run);
        auto event = new EventAction(run);
        if (gTagMode)
        {
            event->PrepareTag();
            gThreadEvent = event;
        }

        SetUserAction(event);
        SetUserAction(new SteppingAction(event));
        SetUserAction(new StackingAction(event));
    }

    void BuildForMaster() const override { SetUserAction(new RunAction()); }
};

int main(int argc, char** argv)
{
    // g4cf [corr] angcorr <Z> <A> <N> <E1_кэВ> <E2_кэВ>   — прямая мерка A22/A44 пары
    // g4cf [scene <файл>] mono <E_кэВ> <N>
    //      | g4cf [scene <файл>] ion <Z> <A> <N> <окно1_кэВ> [окно2 ...]
    //      | g4cf [scene <файл>] hist <E_кэВ> <N> <шаг_бина_кэВ>
    //      | g4cf ionhist <шаг_кэВ> <E_max_кэВ> [scene <файл>] ion <Z> <A> <N> [окна…]
    //      | g4cf iontag [scene <файл>] ion <Z> <A> <N> <окна…>   — состав излучения событий окон + спектр эмиссии РДМ
    //      | g4cf emitpair E1:E2[,…] [scene <файл>] ion <Z> <A> <N> [окна…] — P(E2 | E1) по эмиссии, детектор гасится
    // Файл сцены — вывод effsim --dump-scene; без него сцена вшитая (tube).
    //      Перед всем этим могут стоять `vacuum` (мир пустой вместо воздуха),
    //      `corr` (угловые γ–γ корреляции каскада в RDM), `seed <N>` (зерно
    //      ГСЧ), `killesc`, `killcarry`, `fullcarry` (рычаги П55/П92, см. шапку) — в
    //      любом порядке.
    //      Рычаги П106 (`M13`): `escpop` (только счётчики населений), `killescown`,
    //      `killesccarry`, `killret`, `killretsame`, `killretother`, `killescbrem`,
    //      `killescdelta` — там же, в любом порядке; грани — только со сценой.
    int base = 1;
    while (argc > base && (std::strcmp(argv[base], "vacuum") == 0 || std::strcmp(argv[base], "corr") == 0
                           || std::strcmp(argv[base], "killesc") == 0 || std::strcmp(argv[base], "killcarry") == 0
                           || std::strcmp(argv[base], "fullcarry") == 0 || std::strcmp(argv[base], "iontag") == 0
                           || std::strcmp(argv[base], "escpop") == 0 || std::strcmp(argv[base], "killescown") == 0
                           || std::strcmp(argv[base], "killesccarry") == 0 || std::strcmp(argv[base], "killret") == 0
                           || std::strcmp(argv[base], "killretsame") == 0 || std::strcmp(argv[base], "killretother") == 0
                           || std::strcmp(argv[base], "killescbrem") == 0 || std::strcmp(argv[base], "killescdelta") == 0
                           || std::strcmp(argv[base], "killoutbrem") == 0
                           || (std::strcmp(argv[base], "emitpair") == 0 && argc > base + 1)
                           || (std::strcmp(argv[base], "seed") == 0 && argc > base + 1)
                           || (std::strcmp(argv[base], "ionhist") == 0 && argc > base + 2)))
    {
        if (std::strcmp(argv[base], "vacuum") == 0)
        {
            gVacuumWorld = true;
        }
        else if (std::strcmp(argv[base], "escpop") == 0) { gEscPop = true; }
        else if (std::strcmp(argv[base], "killescown") == 0) { gEscPop = gKillEscOwn = true; }
        else if (std::strcmp(argv[base], "killesccarry") == 0) { gEscPop = gKillEscCarry = true; }
        else if (std::strcmp(argv[base], "killret") == 0) { gEscPop = gKillRet = true; }
        else if (std::strcmp(argv[base], "killretsame") == 0) { gEscPop = gKillRetSame = true; }
        else if (std::strcmp(argv[base], "killretother") == 0) { gEscPop = gKillRetOther = true; }
        else if (std::strcmp(argv[base], "killescbrem") == 0) { gEscPop = gKillEscBrem = true; }
        else if (std::strcmp(argv[base], "killescdelta") == 0) { gEscPop = gKillEscDelta = true; }
        else if (std::strcmp(argv[base], "killoutbrem") == 0) { gEscPop = gKillOutBrem = true; }
        else if (std::strcmp(argv[base], "ionhist") == 0)
        {
            // (П101) Гистограмма всей шкалы в ион-режиме: шаг и верх, кэВ.
            // Длина — по правилу раскладки отклика (последний бин — всё выше).
            gHistBinKev = std::atof(argv[base + 1]);
            double maxKev = std::atof(argv[base + 2]);
            if (!(gHistBinKev > 0.0) || !(maxKev > gHistBinKev))
            {
                std::fprintf(stderr, "ionhist: needs bin > 0 and Emax > bin, keV\n");
                return 2;
            }

            gHistBins = int(maxKev / gHistBinKev + 0.5) + 1;
            base += 2;
        }
        else if (std::strcmp(argv[base], "iontag") == 0)
        {
            // (П102) Подписи состава излучения по окнам `ion` и спектр эмиссии РДМ.
            gTagMode = true;
        }
        else if (std::strcmp(argv[base], "emitpair") == 0)
        {
            // (П102) Условные эмиссии P(E2 | E1) по списку пар E1:E2,…; включает iontag (спектр EMIT), детектор гасится.
            gTagMode = true;
            gEmitPairMode = true;
            std::string list(argv[base + 1]);
            size_t pos = 0;
            while (pos < list.size())
            {
                size_t comma = list.find(',', pos);
                std::string item = list.substr(pos, comma == std::string::npos ? std::string::npos : comma - pos);
                size_t colon = item.find(':');
                if (colon != std::string::npos)
                {
                    gEmitPairs.emplace_back(std::atof(item.substr(0, colon).c_str()), std::atof(item.substr(colon + 1).c_str()));
                }

                if (comma == std::string::npos) { break; }
                pos = comma + 1;
            }

            base++;
        }
        else if (std::strcmp(argv[base], "killesc") == 0)
        {
            gKillEscape = true;
        }
        else if (std::strcmp(argv[base], "killcarry") == 0)
        {
            gKillCarry = true;
        }
        else if (std::strcmp(argv[base], "fullcarry") == 0)
        {
            gFullCarry = true;
        }
        else if (std::strcmp(argv[base], "seed") == 0)
        {
            gSeed = std::atol(argv[base + 1]);
            base++;
        }
        else
        {
            gCorrelatedGamma = true;
        }

        base++;
    }

    if (argc > base + 1 && std::strcmp(argv[base], "scene") == 0)
    {
        if (!LoadScene(argv[base + 1]))
        {
            return 2;
        }

        base += 2;
    }

    // (П106) Населения по граням и геометрический тест стека знают только
    // область `crystal` СЦЕНЫ; на вшитом tube рычаги мерили бы пустоту — отказ.
    if (gEscPop && !gSceneLoaded)
    {
        std::fprintf(stderr, "escpop/killescown/killesccarry/killret*/killescbrem/killescdelta/killoutbrem: need `scene <file>`\n");
        return 2;
    }

    if (argc < base + 3)
    {
        std::fprintf(stderr, "g4cf [vacuum] [corr] [seed <N>] [ionhist <bin_keV> <Emax_keV>] [iontag] [killesc] [killcarry] [fullcarry] [scene <file>] mono <E_keV> <N>"
                             " | ion <Z> <A> <N> <windows...> | hist <E_keV> <N> <bin_keV>\n"
                             "  vacuum: empty world instead of air. MANDATORY for bare-crystal"
                             " checks (T133): air gives 8.232e-4 vs 5.794e-4 in 55...59 keV.\n"
                             "  corr: gamma-gamma angular correlations in RDM (G4DeexPrecoParameters::"
                             "SetCorrelatedGamma); default is isotropic.\n"
                             "  killesc: kill e- leaving the crystal (no return from cladding, as in"
                             " EfficiencySimulator). killcarry: kill e- entering the crystal from"
                             " outside (no carry-in, as with --detour=0). fullcarry: an e- born outside"
                             " deposits its whole remaining energy when leaving the crystal (our"
                             " ElectronCarryDeposit). Ablation levers, P55/P92.\n"
                             "  P106 (M13) return populations, scene only: escpop (counters only),"
                             " killescown (kill crystal-born e- on exit), killesccarry (kill outside-born e-"
                             " on exit), killret / killretsame / killretother (kill crystal-born e- on"
                             " re-entry: any / same face / other face), killescbrem (kill gammas born"
                             " outside in the lineage of an escaped crystal-born e-), killescdelta (same"
                             " for e-/e+), killoutbrem (kill gammas born outside by e-/e+ born outside:"
                             " bremsstrahlung of cladding electrons). Reader: SETUP flags and the ESCPOP line.\n");
        return 2;
    }

    bool angcorr = std::strcmp(argv[base], "angcorr") == 0;
    bool ion = std::strcmp(argv[base], "ion") == 0 || angcorr;
    bool hist = std::strcmp(argv[base], "hist") == 0;
    int argAt = base + 1;
    double energyKev = 0.0;
    int z = 0, a = 0;
    if (ion)
    {
        z = std::atoi(argv[argAt++]);
        a = std::atoi(argv[argAt++]);
    }

    if (angcorr)
    {
        if (argc < argAt + 3)
        {
            std::fprintf(stderr, "angcorr: нужны <N> <E1_кэВ> <E2_кэВ>\n");
            return 2;
        }

        gAngCorrMode = true;
        gAngE1Kev = std::atof(argv[argAt + 1]);
        gAngE2Kev = std::atof(argv[argAt + 2]);
    }
    else if (!ion)
    {
        energyKev = std::atof(argv[argAt++]);
        gWindows.push_back(energyKev);
    }

    long decays = std::atol(argv[argAt++]);
    if (angcorr)
    {
        argAt += 2;     // E1, E2 уже прочитаны
    }

    if (hist)
    {
        if (argAt >= argc)
        {
            std::fprintf(stderr, "hist: нужен шаг бина, кэВ\n");
            return 2;
        }

        gHistBinKev = std::atof(argv[argAt++]);
        // Длина по правилу раскладки отклика: последний бин — пик.
        gHistBins = int(energyKev / gHistBinKev + 0.5) + 1;
    }

    for (; argAt < argc; ++argAt)
    {
        gWindows.push_back(std::atof(argv[argAt]));
    }

    if (gSeed != 0)
    {
        // До создания менеджера: мастер раздаёт зёрна потокам от своего ГСЧ.
        G4Random::setTheSeed(gSeed);
    }

    auto runManager = G4RunManagerFactory::CreateRunManager(G4RunManagerType::Default);
    runManager->SetNumberOfThreads(12);
    runManager->SetUserInitialization(new Detector());
    runManager->SetUserInitialization(new Physics());
    runManager->SetUserInitialization(new Actions());

    // Угловые корреляции — ДО инициализации: после неё параметры деэкситации
    // заперты (`G4DeexPrecoParameters::IsLocked`), и Set… молча ничего не делает.
    // Ставится тем же полем, которое читает `/process/had/deex/correlatedGamma`.
    if (gCorrelatedGamma)
    {
        G4NuclearLevelData::GetInstance()->GetParameters()->SetCorrelatedGamma(true);
    }

    auto ui = G4UImanager::GetUIpointer();
    ui->ApplyCommand("/run/initialize");
    // Читатель флага: значение берётся ОБРАТНО из параметров, а не из ключа —
    // если Set… не доехал (заперт, перезаписан умолчанием), здесь будет 0.
    {
        const G4DeexPrecoParameters* deex = G4NuclearLevelData::GetInstance()->GetParameters();
        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld killesc=%d killcarry=%d fullcarry=%d histbin=%.3f histbins=%d iontag=%d"
                    " escpop=%d killescown=%d killesccarry=%d killret=%d killretsame=%d killretother=%d killescbrem=%d killescdelta=%d killoutbrem=%d\n",
                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed,
                    gKillEscape ? 1 : 0, gKillCarry ? 1 : 0, gFullCarry ? 1 : 0, gHistBinKev, gHistBins, gTagMode ? 1 : 0,
                    gEscPop ? 1 : 0, gKillEscOwn ? 1 : 0, gKillEscCarry ? 1 : 0, gKillRet ? 1 : 0,
                    gKillRetSame ? 1 : 0, gKillRetOther ? 1 : 0, gKillEscBrem ? 1 : 0, gKillEscDelta ? 1 : 0, gKillOutBrem ? 1 : 0);
        std::fflush(stdout);
    }
    // Пороги рождения вторичных (production cuts) по веществам сцены — в шапку
    // каждого прогона (`M12`, П92; факт П55 §5.2). Измерено этой печатью
    // 17.09.2026: range cut 1 мм (П55 писала 0.7 по памяти), порог явных
    // δ-электронов e- в CsI 692 кэВ, NaI 620 (592 — это порог e+), Al 598,
    // PTFE 541; без печати он невидим — все сверки вылета/заноса электронов
    // (П20/П27/П44/П55/П92) идут при этом пороге. Печать, не
    // настройка: умолчания арбитра не меняются. Таблицей, а не UI-командой
    // `/run/dumpCouples`: команда рассылается рабочим потокам и печатается
    // столько раз, сколько потоков.
    std::printf("CUTS (production thresholds per material couple):\n");
    G4ProductionCutsTable::GetProductionCutsTable()->DumpCouples();
    std::fflush(stdout);
    // Иначе Geant4 11.x молча считает стабильными нуклиды с периодом длиннее
    // порога (по умолчанию ~1 год): Co-60 (5.3 г) просто не распадался.
    ui->ApplyCommand("/process/had/rdm/thresholdForVeryLongDecayTime 1.0e+60 year");
    char buffer[64];
    if (gSceneLoaded)
    {
        // Первичку целиком делает SceneGenerator — GPS не настраивается.
        gSceneEnergyKev = energyKev;
        gSceneIon = ion;
        gSceneZ = z;
        gSceneA = a;
    }
    else
    {
        // Источник — объём пробы вшитой сцены tube, изотропно.
        ui->ApplyCommand("/gps/pos/type Volume");
        ui->ApplyCommand("/gps/pos/shape Cylinder");
        ui->ApplyCommand("/gps/pos/centre 0 0 -0.81 cm");
        ui->ApplyCommand("/gps/pos/radius 1.25 cm");
        ui->ApplyCommand("/gps/pos/halfz 0.3 cm");
        ui->ApplyCommand("/gps/ang/type iso");
        if (ion)
        {
            ui->ApplyCommand("/gps/particle ion");
            std::snprintf(buffer, sizeof buffer, "/gps/ion %d %d", z, a);
            ui->ApplyCommand(buffer);
            ui->ApplyCommand("/gps/energy 0 keV");
        }
        else
        {
            ui->ApplyCommand("/gps/particle gamma");
            std::snprintf(buffer, sizeof buffer, "/gps/energy %f keV", energyKev);
            ui->ApplyCommand(buffer);
        }
    }

    if (gAngCorrMode)
    {
        std::printf("SETUP angcorr Z=%d A=%d E1=%.1f E2=%.1f\n", z, a, gAngE1Kev, gAngE2Kev);
        std::fflush(stdout);
    }

    std::snprintf(buffer, sizeof buffer, "/run/beamOn %ld", decays);
    ui->ApplyCommand(buffer);

    delete runManager;
    return 0;
}
