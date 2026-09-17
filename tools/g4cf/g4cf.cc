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
#include <map>
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
    }

    void BeginOfRunAction(const G4Run*) override
    {
        G4AccumulableManager::Instance()->Reset();
    }

    void EndOfRunAction(const G4Run* run) override
    {
        G4AccumulableManager::Instance()->Merge();
        if (!IsMaster())
        {
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
    }

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
    }

    void Add(double edepKev) { fEdepKev += edepKev; }

    void AddGamma(double eKev, const G4ThreeVector& dir) { fGammas.emplace_back(eKev, dir); }

private:
    RunAction* fRun;
    double fEdepKev = 0.0;
    std::vector<std::pair<double, G4ThreeVector>> fGammas;
};

/// Режим `angcorr`: квант распада записывается в момент постановки в стек и
/// ГАСИТСЯ (перенос не нужен); ион и всё прочее идут как обычно.
class StackingAction : public G4UserStackingAction
{
public:
    explicit StackingAction(EventAction* event) : fEvent(event) {}

    G4ClassificationOfNewTrack ClassifyNewTrack(const G4Track* track) override
    {
        if (!gAngCorrMode || track->GetParentID() <= 0)
        {
            return fUrgent;
        }

        if (track->GetDefinition() == G4Gamma::GammaDefinition())
        {
            const G4VProcess* creator = track->GetCreatorProcess();
            if (creator != nullptr && creator->GetProcessName().find("adioactiv") != std::string::npos)
            {
                fEvent->AddGamma(track->GetKineticEnergy() / keV, track->GetMomentumDirection());
            }

            return fKill;
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
    // Файл сцены — вывод effsim --dump-scene; без него сцена вшитая (tube).
    //      Перед всем этим могут стоять `vacuum` (мир пустой вместо воздуха),
    //      `corr` (угловые γ–γ корреляции каскада в RDM), `seed <N>` (зерно
    //      ГСЧ), `killesc`, `killcarry`, `fullcarry` (рычаги П55/П92, см. шапку) — в
    //      любом порядке.
    int base = 1;
    while (argc > base && (std::strcmp(argv[base], "vacuum") == 0 || std::strcmp(argv[base], "corr") == 0
                           || std::strcmp(argv[base], "killesc") == 0 || std::strcmp(argv[base], "killcarry") == 0
                           || std::strcmp(argv[base], "fullcarry") == 0
                           || (std::strcmp(argv[base], "seed") == 0 && argc > base + 1)))
    {
        if (std::strcmp(argv[base], "vacuum") == 0)
        {
            gVacuumWorld = true;
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

    if (argc < base + 3)
    {
        std::fprintf(stderr, "g4cf [vacuum] [corr] [seed <N>] [killesc] [killcarry] [fullcarry] [scene <file>] mono <E_keV> <N>"
                             " | ion <Z> <A> <N> <windows...> | hist <E_keV> <N> <bin_keV>\n"
                             "  vacuum: empty world instead of air. MANDATORY for bare-crystal"
                             " checks (T133): air gives 8.232e-4 vs 5.794e-4 in 55...59 keV.\n"
                             "  corr: gamma-gamma angular correlations in RDM (G4DeexPrecoParameters::"
                             "SetCorrelatedGamma); default is isotropic.\n"
                             "  killesc: kill e- leaving the crystal (no return from cladding, as in"
                             " EfficiencySimulator). killcarry: kill e- entering the crystal from"
                             " outside (no carry-in, as with --detour=0). fullcarry: an e- born outside"
                             " deposits its whole remaining energy when leaving the crystal (our"
                             " ElectronCarryDeposit). Ablation levers, P55/P92.\n");
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
        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld killesc=%d killcarry=%d fullcarry=%d\n",
                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed,
                    gKillEscape ? 1 : 0, gKillCarry ? 1 : 0, gFullCarry ? 1 : 0);
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
