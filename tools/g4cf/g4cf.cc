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
// Сборка и прогон — build_run.ps1 рядом.

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
#include "G4Event.hh"
#include "G4Step.hh"
#include "G4Run.hh"
#include "G4UImanager.hh"
#include "G4SystemOfUnits.hh"
#include "G4AccumulableManager.hh"
#include "G4Accumulable.hh"
#include "globals.hh"
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <vector>

namespace
{
    // Окна счёта, кэВ. Заполняются в main до старта, дальше только чтение.
    std::vector<double> gWindows;
    const double kHalfWindowKev = 0.5;
}

class Detector : public G4VUserDetectorConstruction
{
public:
    G4VPhysicalVolume* Construct() override
    {
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
    }

private:
    G4Accumulable<G4int> fAny;
    std::vector<G4Accumulable<G4int>*> fPeaks;
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
        SetUserAction(new Generator());
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
    // g4cf mono <E_кэВ> <N>  |  g4cf ion <Z> <A> <N> <окно1_кэВ> [окно2 ...]
    if (argc < 4)
    {
        std::fprintf(stderr, "g4cf mono <E_keV> <N> | g4cf ion <Z> <A> <N> <windows...>\n");
        return 2;
    }

    bool ion = std::strcmp(argv[1], "ion") == 0;
    int argAt = 2;
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
    // Источник — объём пробы, изотропно.
    ui->ApplyCommand("/gps/pos/type Volume");
    ui->ApplyCommand("/gps/pos/shape Cylinder");
    ui->ApplyCommand("/gps/pos/centre 0 0 -0.81 cm");
    ui->ApplyCommand("/gps/pos/radius 1.25 cm");
    ui->ApplyCommand("/gps/pos/halfz 0.3 cm");
    ui->ApplyCommand("/gps/ang/type iso");
    char buffer[64];
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

    std::snprintf(buffer, sizeof buffer, "/run/beamOn %ld", decays);
    ui->ApplyCommand(buffer);

    delete runManager;
    return 0;
}
