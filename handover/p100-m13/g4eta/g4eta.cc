// П100 (M13, 18.09.2026): ОПОРА ПО ОБРАТНОМУ РАССЕЯНИЮ ЭЛЕКТРОНОВ — Geant4 той же
// физики, что арбитр стенда (G4EmStandardPhysics_option4, как в tools/g4cf).
// Электрон энергии T падает по нормали на толстую пластину вещества (2 см, ≫ пробега
// до 2 МэВ) из пустоты; считаются электроны, вышедшие обратно через переднюю грань:
//   eta_ev   — доля событий, где хотя бы один e- (T > 50 эВ) вышел назад (то же, что η
//              класса I у LayerReturnProbe: один электрон на историю);
//   eta_n    — число вышедших назад e- на падающий (с δ-электронами, как в опытах);
//   eta_E    — доля вынесенной назад энергии;
//   <T'>/T   — средняя энергия вышедшего назад электрона в долях T (по событиям).
// Материалы — NIST: G4_TEFLON, G4_Al, G4_MAGNESIUM_OXIDE, G4_C, G4_Cu, G4_CESIUM_IODIDE …
//   g4eta <G4_material> <T_keV> <N> [cut_mm=0.01] [seed=20260918] [foil_mm=0] [angle_deg=0] [layer2 layer2_mm]
// Два слоя (foil_mm — толщина первого, layer2 — вещество и толщина второго, за ними пустота) —
// обвязка RC103 П55 как она есть: G4_TEFLON 1.0 + G4_Al 1.0.
// Печать: одна строка ETA material T N eta_ev eta_n eta_E meanTfrac.
// Режим ФОЛЬГИ (foil_mm > 0): пластина этой толщины; для прошедших ПЕРВИЧНЫХ электронов —
// доля прошедших, средний 1 − cos θ к оси, доли с θ > 20/45/60°, средняя энергия в долях T
// (строка FOIL) — поверка углового распределения ОДНОГО ШАГА нашего переноса (0.1 пробега).
#include "G4RunManagerFactory.hh"
#include "G4VUserDetectorConstruction.hh"
#include "G4VUserPrimaryGeneratorAction.hh"
#include "G4VUserActionInitialization.hh"
#include "G4UserSteppingAction.hh"
#include "G4UserEventAction.hh"
#include "G4UserRunAction.hh"
#include "G4VModularPhysicsList.hh"
#include "G4EmStandardPhysics_option4.hh"
#include "G4NistManager.hh"
#include "G4Box.hh"
#include "G4LogicalVolume.hh"
#include "G4PVPlacement.hh"
#include "G4ParticleGun.hh"
#include "G4Electron.hh"
#include "G4Event.hh"
#include "G4Step.hh"
#include "G4Run.hh"
#include "G4UImanager.hh"
#include "G4SystemOfUnits.hh"
#include "G4AccumulableManager.hh"
#include "G4Accumulable.hh"
#include "Randomize.hh"
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>

namespace
{
    std::string gMaterial = "G4_TEFLON";
    double gEnergyKev = 500.0;
    long gSeed = 20260918;
    double gFoilMm = 0.0;          // > 0 — режим ФОЛЬГИ: пластина этой толщины, считаются прошедшие
    double gAngleDeg = 0.0;        // угол падения к нормали, градусы (0 — по нормали)
    std::string gLayer2;           // второй слой за первым (пусто — нет); тогда режим фольги не считается
    double gLayer2Mm = 0.0;
    G4LogicalVolume* gSlab = nullptr;
}

class Detector : public G4VUserDetectorConstruction
{
public:
    G4VPhysicalVolume* Construct() override
    {
        auto nist = G4NistManager::Instance();
        auto vacuum = nist->FindOrBuildMaterial("G4_Galactic");
        auto mat = nist->FindOrBuildMaterial(gMaterial);
        if (mat == nullptr)
        {
            std::fprintf(stderr, "no material %s\n", gMaterial.c_str());
            std::exit(2);
        }

        auto worldBox = new G4Box("World", 10.0 * cm, 10.0 * cm, 10.0 * cm);
        auto world = new G4LogicalVolume(worldBox, vacuum, "World");
        auto worldPv = new G4PVPlacement(nullptr, {}, world, "World", nullptr, false, 0);
        // Пластина 8×8×2 см (или фольга заданной толщины), передняя грань в z = 0, тело при z > 0.
        double half = gFoilMm > 0.0 ? 0.5 * gFoilMm * mm : 1.0 * cm;
        auto slabBox = new G4Box("Slab", 4.0 * cm, 4.0 * cm, half);
        gSlab = new G4LogicalVolume(slabBox, mat, "Slab");
        new G4PVPlacement(nullptr, G4ThreeVector(0, 0, half), gSlab, "Slab", world, false, 0);
        if (!gLayer2.empty() && gLayer2Mm > 0.0)
        {
            auto mat2 = nist->FindOrBuildMaterial(gLayer2);
            if (mat2 == nullptr)
            {
                std::fprintf(stderr, "no material %s\n", gLayer2.c_str());
                std::exit(2);
            }

            double half2 = 0.5 * gLayer2Mm * mm;
            auto box2 = new G4Box("Layer2", 4.0 * cm, 4.0 * cm, half2);
            auto lv2 = new G4LogicalVolume(box2, mat2, "Layer2");
            new G4PVPlacement(nullptr, G4ThreeVector(0, 0, 2.0 * half + half2), lv2, "Layer2", world, false, 0);
        }

        return worldPv;
    }
};

class Physics : public G4VModularPhysicsList
{
public:
    Physics() { RegisterPhysics(new G4EmStandardPhysics_option4()); }
};

class Generator : public G4VUserPrimaryGeneratorAction
{
public:
    Generator() : fGun(1)
    {
        fGun.SetParticleDefinition(G4Electron::Definition());
        fGun.SetParticleEnergy(gEnergyKev * keV);
        fGun.SetParticlePosition(G4ThreeVector(0, 0, -1.0 * mm));
        double th = gAngleDeg * deg;
        fGun.SetParticleMomentumDirection(G4ThreeVector(std::sin(th), 0, std::cos(th)));
    }

    void GeneratePrimaries(G4Event* event) override { fGun.GeneratePrimaryVertex(event); }

private:
    G4ParticleGun fGun;
};

class EventAction : public G4UserEventAction
{
public:
    void BeginOfEventAction(const G4Event*) override { fBackN = 0; fBackE = 0.0; fFwd = false; }
    void EndOfEventAction(const G4Event*) override
    {
        fEvents += 1;
        if (fFwd)
        {
            fFwdN += 1; fFwdMu += fFwdMuEv; fFwdT += fFwdTEv;
            if (fFwdMuEv > 1.0 - std::cos(20.0 * deg)) fFwd20 += 1;
            if (fFwdMuEv > 1.0 - std::cos(45.0 * deg)) fFwd45 += 1;
            if (fFwdMuEv > 1.0 - std::cos(60.0 * deg)) fFwd60 += 1;
        }

        if (fBackN > 0)
        {
            fEventsBack += 1;
            fSumTfrac += fBackE / (gEnergyKev * keV);
        }

        fSumBackN += fBackN;
        fSumBackE += fBackE / (gEnergyKev * keV);
    }

    void Note(double t) { fBackN += 1; fBackE += t; }
    // Прошедший ПЕРВИЧНЫЙ электрон (режим фольги): 1 − cos θ к оси и энергия.
    void NoteForward(double mu, double t) { if (!fFwd) { fFwd = true; fFwdMuEv = mu; fFwdTEv = t / (gEnergyKev * keV); } }

    G4Accumulable<long> fEvents{0}, fEventsBack{0}, fSumBackN{0}, fFwdN{0}, fFwd20{0}, fFwd45{0}, fFwd60{0};
    G4Accumulable<double> fSumBackE{0.0}, fSumTfrac{0.0}, fFwdMu{0.0}, fFwdT{0.0};

private:
    int fBackN = 0;
    double fBackE = 0.0;
    bool fFwd = false;
    double fFwdMuEv = 0.0, fFwdTEv = 0.0;
};

class SteppingAction : public G4UserSteppingAction
{
public:
    explicit SteppingAction(EventAction* ev) : fEvent(ev) {}
    void UserSteppingAction(const G4Step* step) override
    {
        if (step->GetTrack()->GetDefinition() != G4Electron::Definition()) return;
        auto pre = step->GetPreStepPoint();
        auto post = step->GetPostStepPoint();
        auto preVol = pre->GetPhysicalVolume();
        auto postVol = post->GetPhysicalVolume();
        bool preIn = preVol != nullptr && preVol->GetLogicalVolume() == gSlab;
        bool postIn = postVol != nullptr && postVol->GetLogicalVolume() == gSlab;
        if (preIn && !postIn && post->GetMomentumDirection().z() < 0.0 && post->GetKineticEnergy() > 50.0 * eV)
        {
            fEvent->Note(post->GetKineticEnergy());
            step->GetTrack()->SetTrackStatus(fStopAndKill);   // вышел назад — дальше не нужен
        }
        else if (gFoilMm > 0.0 && gLayer2.empty() && preIn && !postIn && post->GetMomentumDirection().z() > 0.0
                 && step->GetTrack()->GetParentID() == 0)
        {
            fEvent->NoteForward(1.0 - post->GetMomentumDirection().z(), post->GetKineticEnergy());
            step->GetTrack()->SetTrackStatus(fStopAndKill);
        }
    }

private:
    EventAction* fEvent;
};

class RunAction : public G4UserRunAction
{
public:
    explicit RunAction(EventAction* ev) : fEvent(ev)
    {
        if (ev != nullptr)
        {
            auto am = G4AccumulableManager::Instance();
            am->Register(ev->fEvents); am->Register(ev->fEventsBack); am->Register(ev->fSumBackN);
            am->Register(ev->fSumBackE); am->Register(ev->fSumTfrac);
            am->Register(ev->fFwdN); am->Register(ev->fFwd20); am->Register(ev->fFwd45); am->Register(ev->fFwd60);
            am->Register(ev->fFwdMu); am->Register(ev->fFwdT);
        }
    }

    void BeginOfRunAction(const G4Run*) override { G4AccumulableManager::Instance()->Reset(); }
    void EndOfRunAction(const G4Run*) override
    {
        G4AccumulableManager::Instance()->Merge();
        if (!IsMaster() || fEvent == nullptr) return;
        long n = fEvent->fEvents.GetValue(), nb = fEvent->fEventsBack.GetValue();
        double etaEv = n > 0 ? (double)nb / n : 0.0;
        std::printf("ETA %s%s %.3f %ld angle=%.1f eta_ev=%.5f +-%.5f eta_n=%.5f eta_E=%.5f meanTfrac=%.4f\n",
                    gMaterial.c_str(), gLayer2.empty() ? "" : ("+" + gLayer2).c_str(), gEnergyKev, n, gAngleDeg, etaEv, n > 0 ? std::sqrt(etaEv * (1 - etaEv) / n) : 0.0,
                    n > 0 ? (double)fEvent->fSumBackN.GetValue() / n : 0.0,
                    n > 0 ? fEvent->fSumBackE.GetValue() / n : 0.0,
                    nb > 0 ? fEvent->fSumTfrac.GetValue() / nb : 0.0);
        if (gFoilMm > 0.0 && gLayer2.empty())
        {
            long nf = fEvent->fFwdN.GetValue();
            std::printf("FOIL %s %.3f %ld foil_mm=%.4f trans=%.5f mean_mu=%.5f P20=%.5f P45=%.5f P60=%.5f meanTfrac=%.4f\n",
                        gMaterial.c_str(), gEnergyKev, n, gFoilMm, n > 0 ? (double)nf / n : 0.0,
                        nf > 0 ? fEvent->fFwdMu.GetValue() / nf : 0.0,
                        nf > 0 ? (double)fEvent->fFwd20.GetValue() / nf : 0.0,
                        nf > 0 ? (double)fEvent->fFwd45.GetValue() / nf : 0.0,
                        nf > 0 ? (double)fEvent->fFwd60.GetValue() / nf : 0.0,
                        nf > 0 ? fEvent->fFwdT.GetValue() / nf : 0.0);
        }

        std::fflush(stdout);
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
        auto ev = new EventAction();
        SetUserAction(ev);
        SetUserAction(new RunAction(ev));
        SetUserAction(new SteppingAction(ev));
    }

    void BuildForMaster() const override { SetUserAction(new RunAction(nullptr)); }
};

int main(int argc, char** argv)
{
    if (argc < 4)
    {
        std::fprintf(stderr, "g4eta <G4_material> <T_keV> <N> [cut_mm=0.01] [seed]\n");
        return 2;
    }

    gMaterial = argv[1];
    gEnergyKev = std::atof(argv[2]);
    long n = std::atol(argv[3]);
    double cutMm = argc > 4 ? std::atof(argv[4]) : 0.01;
    if (argc > 5) gSeed = std::atol(argv[5]);
    if (argc > 6) gFoilMm = std::atof(argv[6]);   // режим фольги: толщина, мм
    if (argc > 7) gAngleDeg = std::atof(argv[7]);  // угол падения к нормали, градусы
    if (argc > 9) { gLayer2 = argv[8]; gLayer2Mm = std::atof(argv[9]); }   // второй слой за первым
    G4Random::setTheSeed(gSeed);

    auto runManager = G4RunManagerFactory::CreateRunManager(G4RunManagerType::Serial);
    runManager->SetUserInitialization(new Detector());
    runManager->SetUserInitialization(new Physics());
    runManager->SetUserInitialization(new Actions());
    auto ui = G4UImanager::GetUIpointer();
    ui->ApplyCommand("/run/verbose 0");
    ui->ApplyCommand("/event/verbose 0");
    ui->ApplyCommand("/tracking/verbose 0");
    ui->ApplyCommand("/process/em/verbose 0");
    char buf[128];
    std::snprintf(buf, sizeof buf, "/run/setCut %.4f mm", cutMm);
    ui->ApplyCommand(buf);
    runManager->Initialize();
    std::printf("SETUP material=%s T=%.3f keV N=%ld cut=%.4f mm seed=%ld physics=G4EmStandardPhysics_option4\n",
                gMaterial.c_str(), gEnergyKev, n, cutMm, gSeed);
    runManager->BeamOn((G4int)n);
    delete runManager;
    return 0;
}
