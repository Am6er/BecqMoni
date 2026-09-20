# -*- coding: utf-8 -*-
# П100: g4eta — второй слой за первым (состав обвязки RC103: PTFE 1 мм + Al 1 мм + пустота): аргументы 8, 9.
import io
p = r'D:\BqMoni_Claude\p100\g4eta\g4eta.cc'
s = io.open(p, encoding='utf-8').read()

def rep(old, new):
    global s
    assert s.count(old) == 1, (old[:60], s.count(old))
    s = s.replace(old, new)

if 'gLayer2' not in s:
    rep("    double gAngleDeg = 0.0;        // угол падения к нормали, градусы (0 — по нормали)",
        "    double gAngleDeg = 0.0;        // угол падения к нормали, градусы (0 — по нормали)\n"
        "    std::string gLayer2;           // второй слой за первым (пусто — нет); тогда режим фольги не считается\n"
        "    double gLayer2Mm = 0.0;")
    rep("""        new G4PVPlacement(nullptr, G4ThreeVector(0, 0, half), gSlab, "Slab", world, false, 0);
        return worldPv;""",
        """        new G4PVPlacement(nullptr, G4ThreeVector(0, 0, half), gSlab, "Slab", world, false, 0);
        if (!gLayer2.empty() && gLayer2Mm > 0.0)
        {
            auto mat2 = nist->FindOrBuildMaterial(gLayer2);
            if (mat2 == nullptr)
            {
                std::fprintf(stderr, "no material %s\\n", gLayer2.c_str());
                std::exit(2);
            }

            double half2 = 0.5 * gLayer2Mm * mm;
            auto box2 = new G4Box("Layer2", 4.0 * cm, 4.0 * cm, half2);
            auto lv2 = new G4LogicalVolume(box2, mat2, "Layer2");
            new G4PVPlacement(nullptr, G4ThreeVector(0, 0, 2.0 * half + half2), lv2, "Layer2", world, false, 0);
        }

        return worldPv;""")
    rep("""        else if (gFoilMm > 0.0 && preIn && !postIn && post->GetMomentumDirection().z() > 0.0
                 && step->GetTrack()->GetParentID() == 0)""",
        """        else if (gFoilMm > 0.0 && gLayer2.empty() && preIn && !postIn && post->GetMomentumDirection().z() > 0.0
                 && step->GetTrack()->GetParentID() == 0)""")
    rep("""        if (gFoilMm > 0.0)
        {
            long nf = fEvent->fFwdN.GetValue();""",
        """        if (gFoilMm > 0.0 && gLayer2.empty())
        {
            long nf = fEvent->fFwdN.GetValue();""")
    rep("""    if (argc > 7) gAngleDeg = std::atof(argv[7]);  // угол падения к нормали, градусы""",
        """    if (argc > 7) gAngleDeg = std::atof(argv[7]);  // угол падения к нормали, градусы
    if (argc > 9) { gLayer2 = argv[8]; gLayer2Mm = std::atof(argv[9]); }   // второй слой за первым""")
    rep("""//   g4eta <G4_material> <T_keV> <N> [cut_mm=0.01] [seed=20260918] [foil_mm=0] [angle_deg=0]""",
        """//   g4eta <G4_material> <T_keV> <N> [cut_mm=0.01] [seed=20260918] [foil_mm=0] [angle_deg=0] [layer2 layer2_mm]
// Два слоя (foil_mm — толщина первого, layer2 — вещество и толщина второго, за ними пустота) —
// обвязка RC103 П55 как она есть: G4_TEFLON 1.0 + G4_Al 1.0.""")
    rep("""        std::printf("ETA %s %.3f %ld angle=%.1f eta_ev=%.5f""",
        """        std::printf("ETA %s%s %.3f %ld angle=%.1f eta_ev=%.5f""")
    rep("""                    gMaterial.c_str(), gEnergyKev, n, gAngleDeg, etaEv,""",
        """                    gMaterial.c_str(), gLayer2.empty() ? "" : ("+" + gLayer2).c_str(), gEnergyKev, n, gAngleDeg, etaEv,""")
    io.open(p, 'w', encoding='utf-8').write(s)
    print('ok')
else:
    print('already')
