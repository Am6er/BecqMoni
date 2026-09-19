// П111 (M13, 19.09.2026): ОПОРА ПО ТОРМОЗНОМУ ИЗЛУЧЕНИЮ ЭЛЕКТРОНА В СЛОЕ — Geant4 той же физики,
// что арбитр стенда (G4EmStandardPhysics_option4, как tools/g4cf и handover/p100-m13/g4eta).
// Электрон энергии T падает из пустоты на пластину вещества (толстую 2 см или слой заданной толщины,
// за ним — второй слой или пустота) под углом к нормали; считаются КВАНТЫ ТОРМОЗНОГО (процесс eBrem),
// рождённые в веществе пластины/слоёв:
//   brem_all   — квантов с k ≥ kmin на падающий электрон, от ВСЕХ лептонов события (первичный + δ);
//   brem_prim  — то же, только от первичного электрона (трек 1);
//   brem_E     — излучённая энергия (k ≥ kmin) на электрон, кэВ;
//   fwd/back   — доля квантов, рождённых в переднюю (по ходу первичного, z > 0) / заднюю полусферу;
//   гистограмма k по полосам (кэВ): 5-10, 10-20, 20-30, 30-50, 50-70, 70-100, 100-150, 150-200, 200-300,
//                                   300-500, 500-700, 700-1000, 1000-1500, 1500-2000, 2000+;
//   esc_fwd/esc_back — квантов (k ≥ kmin), ВЫШЕДШИХ из вещества в пустоту вперёд (z > 0) / назад;
//   path_prim  — истинный путь первичного e- в веществе, г/см² (сумма длин шагов × плотность);
//   path_all   — то же для всех e-/e+;
//   eta        — доля событий с e- (T > 50 эВ), вышедшим назад (как g4eta, самопроверка).
// Пороги рождения: e- по range cut (умолчание 1 мм — как у арбитра стенда: порог δ 541 кэВ в PTFE),
// гамма — свой range cut (умолчание 0.001 мм → порог упирается в 990 эВ), чтобы счёт квантов ≥ kmin
// не зависел от порога рождения (у арбитра стенда при 1 мм порог 4.4 кэВ PTFE / 6.9 кэВ Al).
//   g4brem <G4_material> <T_keV> <N> [cut_e_mm=1.0] [seed=20260919] [slab_mm=0] [angle_deg=0]
//          [layer2 layer2_mm] [cut_g_mm=0.001] [kmin_keV=5]
//   slab_mm = 0 — толстая пластина 2 см. layer2 — вещество и толщина второго слоя ("-" 0 — нет).
// Печать: одна строка BREM ... и одна строка HIST ... (числа на падающий электрон).
#include "G4RunManagerFactory.hh"
#include "G4VUserDetectorConstruction.hh"
#include "G4VUserPrimaryGeneratorAction.hh"
#include "G4VUserActionInitialization.hh"
#include "G4UserSteppingAction.hh"
#include "G4UserStackingAction.hh"
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
#include "G4Positron.hh"
#include "G4Gamma.hh"
#include "G4Event.hh"
#include "G4Step.hh"
#include "G4Track.hh"
#include "G4VProcess.hh"
#include "G4Run.hh"
#include "G4UImanager.hh"
#include "G4SystemOfUnits.hh"
#include "G4AccumulableManager.hh"
#include "G4Accumulable.hh"
#include "G4ProductionCutsTable.hh"
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
    long gSeed = 20260919;
    double gSlabMm = 0.0;          // > 0 — слой этой толщины; 0 — толстая пластина 2 см
    double gAngleDeg = 0.0;
    std::string gLayer2;
    double gLayer2Mm = 0.0;
    double gKminKev = 5.0;
    G4LogicalVolume* gSlab = nullptr;
    G4LogicalVolume* gLayer2Lv = nullptr;
    const int kBins = 15;
    const double kEdges[kBins + 1] = { 5, 10, 20, 30, 50, 70, 100, 150, 200, 300, 500, 700, 1000, 1500, 2000, 1e9 };

    bool InMatter(const G4LogicalVolume* lv) { return lv != nullptr && (lv == gSlab || lv == gLayer2Lv); }
    int BinOf(double kKev)
    {
        for (int i = 0; i < kBins; i++) if (kKev < kEdges[i + 1]) return i;
        return kBins - 1;
    }
}

class Detector : public G4VUserDetectorConstruction
{
public:
    G4VPhysicalVolume* Construct() override
    {
        auto nist = G4NistManager::Instance();
        auto vacuum = nist->FindOrBuildMaterial("G4_Galactic");
        auto mat = nist->FindOrBuildMaterial(gMaterial);
        if (mat == nullptr) { std::fprintf(stderr, "no material %s\n", gMaterial.c_str()); std::exit(2); }
        auto worldBox = new G4Box("World", 10.0 * cm, 10.0 * cm, 10.0 * cm);
        auto world = new G4LogicalVolume(worldBox, vacuum, "World");
        auto worldPv = new G4PVPlacement(nullptr, {}, world, "World", nullptr, false, 0);
        double half = gSlabMm > 0.0 ? 0.5 * gSlabMm * mm : 1.0 * cm;
        auto slabBox = new G4Box("Slab", 4.0 * cm, 4.0 * cm, half);
        gSlab = new G4LogicalVolume(slabBox, mat, "Slab");
        new G4PVPlacement(nullptr, G4ThreeVector(0, 0, half), gSlab, "Slab", world, false, 0);
        if (!gLayer2.empty() && gLayer2 != "-" && gLayer2Mm > 0.0)
        {
            auto mat2 = nist->FindOrBuildMaterial(gLayer2);
            if (mat2 == nullptr) { std::fprintf(stderr, "no material %s\n", gLayer2.c_str()); std::exit(2); }
            double half2 = 0.5 * gLayer2Mm * mm;
            auto box2 = new G4Box("Layer2", 4.0 * cm, 4.0 * cm, half2);
            gLayer2Lv = new G4LogicalVolume(box2, mat2, "Layer2");
            new G4PVPlacement(nullptr, G4ThreeVector(0, 0, 2.0 * half + half2), gLayer2Lv, "Layer2", world, false, 0);
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
    void BeginOfEventAction(const G4Event*) override { fBack = false; }
    void EndOfEventAction(const G4Event*) override { fEvents += 1; if (fBack) fEventsBack += 1; }
    void NoteBack() { fBack = true; }

    G4Accumulable<long> fEvents{0}, fEventsBack{0};
    G4Accumulable<long> fBremAll{0}, fBremPrim{0}, fBremFwd{0}, fBremBack{0}, fEscFwd{0}, fEscBack{0};
    G4Accumulable<double> fBremE{0.0}, fBremEPrim{0.0}, fEscE{0.0}, fPathPrim{0.0}, fPathAll{0.0};
    G4Accumulable<long> fHist[kBins];
    G4Accumulable<long> fHistPrim[kBins];
    G4Accumulable<long> fHistEsc[kBins];
private:
    bool fBack = false;
};

class StackingAction : public G4UserStackingAction
{
public:
    explicit StackingAction(EventAction* ev) : fEvent(ev) {}
    G4ClassificationOfNewTrack ClassifyNewTrack(const G4Track* track) override
    {
        if (track->GetDefinition() == G4Gamma::GammaDefinition() && track->GetParentID() > 0)
        {
            const G4VProcess* creator = track->GetCreatorProcess();
            std::string pname = creator != nullptr ? creator->GetProcessName() : std::string();
            if (pname.find("Brem") != std::string::npos)
            {
                double k = track->GetKineticEnergy() / keV;
                // Точка рождения — в веществе (z внутри слоёв); в пустоте тормозного нет, проверка — самоконтроль.
                double z = track->GetPosition().z();
                double thick = (gSlabMm > 0.0 ? gSlabMm * mm : 2.0 * cm) + (gLayer2Lv != nullptr ? gLayer2Mm * mm : 0.0);
                if (k >= gKminKev && z >= -1e-6 * mm && z <= thick + 1e-6 * mm)
                {
                    fEvent->fBremAll += 1;
                    fEvent->fBremE += k;
                    int b = BinOf(k);
                    fEvent->fHist[b] += 1;
                    if (track->GetMomentumDirection().z() > 0.0) fEvent->fBremFwd += 1; else fEvent->fBremBack += 1;
                    if (track->GetParentID() == 1)
                    {
                        fEvent->fBremPrim += 1;
                        fEvent->fBremEPrim += k;
                        fEvent->fHistPrim[b] += 1;
                    }
                }
            }
        }
        return fUrgent;
    }
private:
    EventAction* fEvent;
};

class SteppingAction : public G4UserSteppingAction
{
public:
    explicit SteppingAction(EventAction* ev) : fEvent(ev) {}
    void UserSteppingAction(const G4Step* step) override
    {
        auto track = step->GetTrack();
        auto pre = step->GetPreStepPoint();
        auto post = step->GetPostStepPoint();
        auto preVol = pre->GetPhysicalVolume();
        auto postVol = post->GetPhysicalVolume();
        bool preIn = preVol != nullptr && InMatter(preVol->GetLogicalVolume());
        bool postIn = postVol != nullptr && InMatter(postVol->GetLogicalVolume());
        auto def = track->GetDefinition();
        if (def == G4Electron::Definition() || def == G4Positron::Definition())
        {
            if (preIn)
            {
                // Путь в г/см²: длина шага (см) × плотность (г/см³). ⚠ Имя `g` здесь — единица CLHEP (грамм),
                // локальную переменную так называть нельзя (первая редакция затенила её и мерила мусор, C4700).
                double pathG = step->GetStepLength() / cm * (pre->GetMaterial()->GetDensity() / (g / cm3));
                fEvent->fPathAll += pathG;
                if (track->GetParentID() == 0) fEvent->fPathPrim += pathG;
            }
            if (def == G4Electron::Definition() && preIn && !postIn && post->GetMomentumDirection().z() < 0.0
                && post->GetKineticEnergy() > 50.0 * eV)
            {
                fEvent->NoteBack();
                track->SetTrackStatus(fStopAndKill);
            }
            else if (preIn && !postIn)
            {
                track->SetTrackStatus(fStopAndKill);      // ушёл в пустоту вперёд/вбок — дальше не нужен
            }
        }
        else if (def == G4Gamma::GammaDefinition())
        {
            if (preIn && !postIn)
            {
                double k = post->GetKineticEnergy() / keV;
                const G4VProcess* creator = track->GetCreatorProcess();
                std::string pname = creator != nullptr ? creator->GetProcessName() : std::string();
                if (k >= gKminKev && pname.find("Brem") != std::string::npos)
                {
                    if (post->GetMomentumDirection().z() > 0.0) fEvent->fEscFwd += 1; else fEvent->fEscBack += 1;
                    fEvent->fEscE += k;
                    fEvent->fHistEsc[BinOf(k)] += 1;
                }
                track->SetTrackStatus(fStopAndKill);      // вышел в пустоту — не возвращается
            }
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
            am->Register(ev->fEvents); am->Register(ev->fEventsBack);
            am->Register(ev->fBremAll); am->Register(ev->fBremPrim); am->Register(ev->fBremFwd); am->Register(ev->fBremBack);
            am->Register(ev->fEscFwd); am->Register(ev->fEscBack);
            am->Register(ev->fBremE); am->Register(ev->fBremEPrim); am->Register(ev->fEscE); am->Register(ev->fPathPrim); am->Register(ev->fPathAll);
            for (int i = 0; i < kBins; i++) { am->Register(ev->fHist[i]); am->Register(ev->fHistPrim[i]); am->Register(ev->fHistEsc[i]); }
        }
    }
    void BeginOfRunAction(const G4Run*) override { G4AccumulableManager::Instance()->Reset(); }
    void EndOfRunAction(const G4Run*) override
    {
        G4AccumulableManager::Instance()->Merge();
        if (!IsMaster() || fEvent == nullptr) return;
        long n = fEvent->fEvents.GetValue();
        if (n <= 0) return;
        double inv = 1.0 / n;
        long ba = fEvent->fBremAll.GetValue(), bp = fEvent->fBremPrim.GetValue();
        std::printf("BREM %s%s%s T=%.3f N=%ld angle=%.1f slab_mm=%.4f kmin=%.2f brem_all=%.6f +-%.6f brem_prim=%.6f brem_E=%.4f brem_E_prim=%.4f fwd=%.6f back=%.6f esc_fwd=%.6f esc_back=%.6f esc_E=%.4f path_prim=%.6f path_all=%.6f eta=%.5f\n",
                    gMaterial.c_str(), (gLayer2.empty() || gLayer2 == "-") ? "" : "+", (gLayer2.empty() || gLayer2 == "-") ? "" : gLayer2.c_str(),
                    gEnergyKev, n, gAngleDeg, gSlabMm, gKminKev,
                    ba * inv, std::sqrt((double)ba) * inv, bp * inv,
                    fEvent->fBremE.GetValue() * inv, fEvent->fBremEPrim.GetValue() * inv,
                    fEvent->fBremFwd.GetValue() * inv, fEvent->fBremBack.GetValue() * inv,
                    fEvent->fEscFwd.GetValue() * inv, fEvent->fEscBack.GetValue() * inv, fEvent->fEscE.GetValue() * inv,
                    fEvent->fPathPrim.GetValue() * inv, fEvent->fPathAll.GetValue() * inv,
                    (double)fEvent->fEventsBack.GetValue() * inv);
        std::printf("HIST all");
        for (int i = 0; i < kBins; i++) std::printf(" %.6e", fEvent->fHist[i].GetValue() * inv);
        std::printf("\nHIST prim");
        for (int i = 0; i < kBins; i++) std::printf(" %.6e", fEvent->fHistPrim[i].GetValue() * inv);
        std::printf("\nHIST esc");
        for (int i = 0; i < kBins; i++) std::printf(" %.6e", fEvent->fHistEsc[i].GetValue() * inv);
        std::printf("\n");
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
        SetUserAction(new StackingAction(ev));
    }
    void BuildForMaster() const override { SetUserAction(new RunAction(nullptr)); }
};

int main(int argc, char** argv)
{
    if (argc < 4)
    {
        std::fprintf(stderr, "g4brem <G4_material> <T_keV> <N> [cut_e_mm=1.0] [seed] [slab_mm=0] [angle_deg=0] [layer2 layer2_mm] [cut_g_mm=0.001] [kmin_keV=5]\n");
        return 2;
    }
    gMaterial = argv[1];
    gEnergyKev = std::atof(argv[2]);
    long n = std::atol(argv[3]);
    double cutEMm = argc > 4 ? std::atof(argv[4]) : 1.0;
    if (argc > 5) gSeed = std::atol(argv[5]);
    if (argc > 6) gSlabMm = std::atof(argv[6]);
    if (argc > 7) gAngleDeg = std::atof(argv[7]);
    if (argc > 9) { gLayer2 = argv[8]; gLayer2Mm = std::atof(argv[9]); }
    double cutGMm = argc > 10 ? std::atof(argv[10]) : 0.001;
    if (argc > 11) gKminKev = std::atof(argv[11]);
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
    char buf[160];
    std::snprintf(buf, sizeof buf, "/run/setCutForAGivenParticle e- %.5f mm", cutEMm);
    ui->ApplyCommand(buf);
    std::snprintf(buf, sizeof buf, "/run/setCutForAGivenParticle e+ %.5f mm", cutEMm);
    ui->ApplyCommand(buf);
    std::snprintf(buf, sizeof buf, "/run/setCutForAGivenParticle gamma %.5f mm", cutGMm);
    ui->ApplyCommand(buf);
    runManager->Initialize();
    std::printf("SETUP material=%s layer2=%s layer2_mm=%.4f T=%.3f keV N=%ld cut_e=%.5f mm cut_g=%.5f mm kmin=%.2f keV slab_mm=%.4f angle=%.1f seed=%ld physics=G4EmStandardPhysics_option4\n",
                gMaterial.c_str(), gLayer2.c_str(), gLayer2Mm, gEnergyKev, n, cutEMm, cutGMm, gKminKev, gSlabMm, gAngleDeg, gSeed);
    std::printf("CUTS (production thresholds per material couple):\n");
    G4ProductionCutsTable::GetProductionCutsTable()->DumpCouples();
    std::fflush(stdout);
    runManager->BeamOn((G4int)n);
    delete runManager;
    return 0;
}
