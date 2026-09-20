# -*- coding: utf-8 -*-
# П100: g4eta — угол падения к нормали (7-й аргумент), печать угла в строке ETA.
import io
p = r'D:\BqMoni_Claude\p100\g4eta\g4eta.cc'
s = io.open(p, encoding='utf-8').read()

def rep(old, new):
    global s
    assert s.count(old) == 1, (old[:60], s.count(old))
    s = s.replace(old, new)

if 'gAngleDeg' not in s:
    rep("    double gFoilMm = 0.0;          // > 0 — режим ФОЛЬГИ: пластина этой толщины, считаются прошедшие",
        "    double gFoilMm = 0.0;          // > 0 — режим ФОЛЬГИ: пластина этой толщины, считаются прошедшие\n"
        "    double gAngleDeg = 0.0;        // угол падения к нормали, градусы (0 — по нормали)")
    rep("        fGun.SetParticleMomentumDirection(G4ThreeVector(0, 0, 1));",
        "        double th = gAngleDeg * deg;\n"
        "        fGun.SetParticleMomentumDirection(G4ThreeVector(std::sin(th), 0, std::cos(th)));")
    rep("    if (argc > 6) gFoilMm = std::atof(argv[6]);   // режим фольги: толщина, мм",
        "    if (argc > 6) gFoilMm = std::atof(argv[6]);   // режим фольги: толщина, мм\n"
        "    if (argc > 7) gAngleDeg = std::atof(argv[7]);  // угол падения к нормали, градусы")
    rep('        std::printf("ETA %s %.3f %ld eta_ev=%.5f +-%.5f eta_n=%.5f eta_E=%.5f meanTfrac=%.4f\\n",\n'
        '                    gMaterial.c_str(), gEnergyKev, n, etaEv,',
        '        std::printf("ETA %s %.3f %ld angle=%.1f eta_ev=%.5f +-%.5f eta_n=%.5f eta_E=%.5f meanTfrac=%.4f\\n",\n'
        '                    gMaterial.c_str(), gEnergyKev, n, gAngleDeg, etaEv,')
    rep("//   g4eta <G4_material> <T_keV> <N> [cut_mm=0.01] [seed=20260918] [foil_mm]",
        "//   g4eta <G4_material> <T_keV> <N> [cut_mm=0.01] [seed=20260918] [foil_mm=0] [angle_deg=0]")
    rep("#include <cstdio>", "#include <cmath>\n#include <cstdio>")
    io.open(p, 'w', encoding='utf-8').write(s)
    print('ok')
else:
    print('already')
