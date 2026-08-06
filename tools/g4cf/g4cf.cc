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

        // Проверка пересечений: сёстры Geant4 обязаны не перекрываться, а наша
        // сцена разрешает «первую победившую». Найдено перекрытие — ОТКАЗ,
        // молча строить нечестную сцену нельзя.
        for (size_t i = 0; i < gSceneRegions.size(); ++i)
        {
            for (size_t j = i + 1; j < gSceneRegions.size(); ++j)
            {
                const SceneRegion& p = gSceneRegions[i];
                const SceneRegion& q = gSceneRegions[j];
                if (p.z1 <= q.z0 + 1e-9 || q.z1 <= p.z0 + 1e-9)
                {
                    continue;
                }

                double pLo = p.box ? 0.0 : p.a;
                double pHi = p.box ? std::sqrt(p.a * p.a + p.b * p.b) : p.b;
                double qLo = q.box ? 0.0 : q.a;
                double qHi = q.box ? std::sqrt(q.a * q.a + q.b * q.b) : q.b;
                if (pHi <= qLo + 1e-9 || qHi <= pLo + 1e-9)
                {
                    continue;
                }

                std::fprintf(stderr,
                             "перекрытие областей %zu и %zu (z и радиусы пересекаются) — "
                             "сцену в Geant4 так строить нельзя\n", i, j);
                return false;
            }
        }

        gSceneLoaded = true;
        return true;
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
        auto air = nist->FindOrBuildMaterial("G4_AIR");
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

        int regionIndex = 0;
        for (const SceneRegion& r : gSceneRegions)
        {
            std::string name = "r" + std::to_string(regionIndex++);
            G4VSolid* solid = r.box
                ? static_cast<G4VSolid*>(new G4Box(name, r.a * cm, r.b * cm,
                                                   0.5 * (r.z1 - r.z0) * cm))
                : static_cast<G4VSolid*>(new G4Tubs(name, r.a * cm, r.b * cm,
                                                    0.5 * (r.z1 - r.z0) * cm,
                                                    0.0, 360.0 * deg));
            auto logical = new G4LogicalVolume(solid, mats[r.mat], name);
            // checkOverlaps=true: вторая линия обороны после своей проверки
            new G4PVPlacement(nullptr, G4ThreeVector(0, 0, 0.5 * (r.z0 + r.z1) * cm),
                              logical, name, worldL, false, 0, true);
            if (r.crystal)
            {
                fCrystal = logical;
            }
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
    RunAction() : fAny("any", 0)
    {
        auto manager = G4AccumulableManager::Instance();
        manager->Register(fAny);
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

    void Count(double edepKev)
    {
        if (edepKev > 1e-3)
        {
            fAny += 1;
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
};

class EventAction : public G4UserEventAction
{
public:
    explicit EventAction(RunAction* run) : fRun(run) {}

    void BeginOfEventAction(const G4Event*) override { fEdepKev = 0.0; }

    void EndOfEventAction(const G4Event*) override { fRun->Count(fEdepKev); }

    void Add(double edepKev) { fEdepKev += edepKev; }

private:
    RunAction* fRun;
    double fEdepKev = 0.0;
};

class SteppingAction : public G4UserSteppingAction
{
public:
    explicit SteppingAction(EventAction* event) : fEvent(event) {}

    void UserSteppingAction(const G4Step* step) override
    {
        if (step->GetPreStepPoint()->GetTouchableHandle()->GetVolume()
                ->GetLogicalVolume() == Detector::fCrystal)
        {
            fEvent->Add(step->GetTotalEnergyDeposit() / keV);
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
    }

    void BuildForMaster() const override { SetUserAction(new RunAction()); }
};

int main(int argc, char** argv)
{
    // g4cf [scene <файл>] mono <E_кэВ> <N>
    //      | g4cf [scene <файл>] ion <Z> <A> <N> <окно1_кэВ> [окно2 ...]
    //      | g4cf [scene <файл>] hist <E_кэВ> <N> <шаг_бина_кэВ>
    // Файл сцены — вывод effsim --dump-scene; без него сцена вшитая (tube).
    int base = 1;
    if (argc > 2 && std::strcmp(argv[1], "scene") == 0)
    {
        if (!LoadScene(argv[2]))
        {
            return 2;
        }

        base = 3;
    }

    if (argc < base + 3)
    {
        std::fprintf(stderr, "g4cf [scene <file>] mono <E_keV> <N> | ion <Z> <A> <N> <windows...>"
                             " | hist <E_keV> <N> <bin_keV>\n");
        return 2;
    }

    bool ion = std::strcmp(argv[base], "ion") == 0;
    bool hist = std::strcmp(argv[base], "hist") == 0;
    int argAt = base + 1;
    double energyKev = 0.0;
    int z = 0, a = 0;
    if (ion)
    {
        z = std::atoi(argv[argAt++]);
        a = std::atoi(argv[argAt++]);
    }
    else
    {
        energyKev = std::atof(argv[argAt++]);
        gWindows.push_back(energyKev);
    }

    long decays = std::atol(argv[argAt++]);
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

    auto runManager = G4RunManagerFactory::CreateRunManager(G4RunManagerType::Default);
    runManager->SetNumberOfThreads(12);
    runManager->SetUserInitialization(new Detector());
    runManager->SetUserInitialization(new Physics());
    runManager->SetUserInitialization(new Actions());

    auto ui = G4UImanager::GetUIpointer();
    ui->ApplyCommand("/run/initialize");
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

    std::snprintf(buffer, sizeof buffer, "/run/beamOn %ld", decays);
    ui->ApplyCommand(buffer);

    delete runManager;
    return 0;
}
