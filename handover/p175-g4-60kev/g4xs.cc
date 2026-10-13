// П175: сечения фотона у арбитра (option4, как g4cf) для веществ сцены и элементов.
// g4xs <scene.txt> <E1_кэВ> [E2 ...]  — печать XS <вещество> <E> <процесс> <1/см> <см2/г>
#include "G4RunManagerFactory.hh"
#include "G4VUserDetectorConstruction.hh"
#include "G4VUserPrimaryGeneratorAction.hh"
#include "G4VModularPhysicsList.hh"
#include "G4EmStandardPhysics_option4.hh"
#include "G4NistManager.hh"
#include "G4Material.hh"
#include "G4Box.hh"
#include "G4LogicalVolume.hh"
#include "G4PVPlacement.hh"
#include "G4ParticleGun.hh"
#include "G4Gamma.hh"
#include "G4EmCalculator.hh"
#include "G4SystemOfUnits.hh"
#include <fstream>
#include <sstream>
#include <vector>
#include <string>
#include <cstdio>

struct Mat { std::string name; double dens; std::vector<std::pair<int,double>> parts; G4Material* m = nullptr; };
static std::vector<Mat> gMats;

class Det : public G4VUserDetectorConstruction {
public:
  G4VPhysicalVolume* Construct() override {
    auto nist = G4NistManager::Instance();
    auto worldL = new G4LogicalVolume(new G4Box("w", 1*m, 1*m, 1*m), nist->FindOrBuildMaterial("G4_Galactic"), "w");
    auto worldP = new G4PVPlacement(nullptr, {}, worldL, "w", nullptr, false, 0);
    int k = 0;
    for (auto& e : gMats) {
      e.m = new G4Material("scene_" + e.name, e.dens * g / cm3, int(e.parts.size()));
      for (auto& p : e.parts) e.m->AddElement(nist->FindOrBuildElement(p.first), p.second);
      auto l = new G4LogicalVolume(new G4Box("b", 1*cm, 1*cm, 1*cm), e.m, "b" + e.name);
      new G4PVPlacement(nullptr, G4ThreeVector(-80*cm + 5*cm*k, 0, 0), l, "b" + e.name, worldL, false, 0);
      ++k;
    }
    return worldP;
  }
};
class Phys : public G4VModularPhysicsList { public: Phys() { RegisterPhysics(new G4EmStandardPhysics_option4()); } };
class Gen : public G4VUserPrimaryGeneratorAction {
public:
  Gen() { fGun.SetParticleDefinition(G4Gamma::Definition()); fGun.SetParticleEnergy(60*keV); }
  void GeneratePrimaries(G4Event* e) override { fGun.GeneratePrimaryVertex(e); }
  G4ParticleGun fGun;
};

int main(int argc, char** argv) {
  std::ifstream in(argv[1]); std::string line;
  while (std::getline(in, line)) {
    std::istringstream ss(line); std::string w; ss >> w;
    if (w != "mat") continue;
    Mat m; ss >> m.name >> m.dens; std::string t;
    while (ss >> t) { auto c = t.find(':'); m.parts.push_back({std::stoi(t.substr(0, c)), std::stod(t.substr(c + 1))}); }
    gMats.push_back(m);
  }
  std::vector<double> es; for (int i = 2; i < argc; ++i) es.push_back(std::atof(argv[i]));
  auto rm = G4RunManagerFactory::CreateRunManager(G4RunManagerType::Serial);
  rm->SetUserInitialization(new Det()); rm->SetUserInitialization(new Phys()); rm->SetUserAction(new Gen());
  rm->Initialize(); rm->BeamOn(0);
  G4EmCalculator calc;
  const char* procs[] = {"phot", "compt", "Rayl", "conv"};
  for (auto& e : gMats)
    for (double E : es) {
      double tot = 0;
      for (auto p : procs) {
        double s = calc.ComputeCrossSectionPerVolume(E * keV, "gamma", p, e.m->GetName(), 0.0) * cm;  // 1/см
        tot += s;
        std::printf("XS %s %.3f %s %.6e %.6e\n", e.name.c_str(), E, p, s, s / e.dens);
      }
      std::printf("XS %s %.3f total %.6e %.6e\n", e.name.c_str(), E, tot, tot / e.dens);
    }
  int zs[] = {1, 6, 7, 8, 9, 13, 17, 18, 19, 53, 55};
  auto nist = G4NistManager::Instance();
  for (int Z : zs)
    for (double E : es) {
      auto el = nist->FindOrBuildElement(Z); double Aamu = el->GetN(); double tot = 0;
      for (auto p : procs) {
        double s = calc.ComputeCrossSectionPerAtom(E * keV, "gamma", p, el, 0.0);
        double mu = s / barn * 0.60221408 / Aamu;   // см2/г
        tot += mu;
        std::printf("XSZ %d %.3f %s %.6e\n", Z, E, p, mu);
      }
      std::printf("XSZ %d %.3f total %.6e\n", Z, E, tot);
    }
  std::fflush(stdout); std::_Exit(0);
}
