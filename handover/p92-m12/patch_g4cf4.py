# -*- coding: utf-8 -*-
# П92: третий рычаг арбитра — fullcarry: e-, РОЖДЁННЫЙ вне кристалла и вошедший в него, при выходе из
# кристалла отдаёт кристаллу весь остаток кинетической энергии и гасится — наше приближение
# ElectronCarryDeposit («долетевший электрон приносит весь остаток»: ни обратного рассеяния из CsI,
# ни вылета через другую грань). «G4 fullcarry» против «наша ref» отделяет судьбу занесённого электрона
# В КРИСТАЛЛЕ от его переноса В ОБВЯЗКЕ (разброс пробега, направление).
import io, sys
p = sys.argv[1]
s = io.open(p, encoding='utf-8', newline='').read()
assert '\r\n' in s

def rep(old, new, count=1):
    global s
    assert s.count(old) == count, (old[:70], s.count(old))
    s = s.replace(old, new)

rep('    bool gKillEscape = false;\r\n    bool gKillCarry = false;\r\n',
    '    //   fullcarry — e-, РОЖДЁННЫЙ вне кристалла (`GetLogicalVolumeAtVertex`) и\r\n'
    '    //               вошедший в него, при выходе из кристалла отдаёт ему весь\r\n'
    '    //               остаток кинетической энергии и гасится: наше приближение\r\n'
    '    //               `ElectronCarryDeposit` («долетел — принёс весь остаток»),\r\n'
    '    //               без обратного рассеяния из CsI и вылета через другую грань.\r\n'
    '    //               «G4 fullcarry» против «наша ref» отделяет судьбу занесённого\r\n'
    '    //               электрона В КРИСТАЛЛЕ от его переноса В ОБВЯЗКЕ (П92 §3).\r\n'
    '    bool gKillEscape = false;\r\n    bool gKillCarry = false;\r\n    bool gFullCarry = false;\r\n')

rep('        if (!gKillEscape && !gKillCarry)\r\n',
    '        if (!gKillEscape && !gKillCarry && !gFullCarry)\r\n')

rep('        if ((gKillEscape && preInCrystal && !postInCrystal) || (gKillCarry && !preInCrystal && postInCrystal))\r\n'
    '        {\r\n'
    '            step->GetTrack()->SetTrackStatus(fStopAndKill);\r\n'
    '        }\r\n',
    '        if ((gKillEscape && preInCrystal && !postInCrystal) || (gKillCarry && !preInCrystal && postInCrystal))\r\n'
    '        {\r\n'
    '            step->GetTrack()->SetTrackStatus(fStopAndKill);\r\n'
    '        }\r\n'
    '\r\n'
    '        // fullcarry (П92): занесённый e- выходит из кристалла — остаток его\r\n'
    '        // кинетики засчитывается кристаллу, трек гасится (наше приближение).\r\n'
    '        if (gFullCarry && preInCrystal && !postInCrystal\r\n'
    '            && step->GetTrack()->GetLogicalVolumeAtVertex() != Detector::fCrystal)\r\n'
    '        {\r\n'
    '            fEvent->Add(step->GetPostStepPoint()->GetKineticEnergy() / keV);\r\n'
    '            step->GetTrack()->SetTrackStatus(fStopAndKill);\r\n'
    '        }\r\n')

rep('                           || std::strcmp(argv[base], "killesc") == 0 || std::strcmp(argv[base], "killcarry") == 0\r\n',
    '                           || std::strcmp(argv[base], "killesc") == 0 || std::strcmp(argv[base], "killcarry") == 0\r\n'
    '                           || std::strcmp(argv[base], "fullcarry") == 0\r\n')

rep('        else if (std::strcmp(argv[base], "killcarry") == 0)\r\n'
    '        {\r\n'
    '            gKillCarry = true;\r\n'
    '        }\r\n',
    '        else if (std::strcmp(argv[base], "killcarry") == 0)\r\n'
    '        {\r\n'
    '            gKillCarry = true;\r\n'
    '        }\r\n'
    '        else if (std::strcmp(argv[base], "fullcarry") == 0)\r\n'
    '        {\r\n'
    '            gFullCarry = true;\r\n'
    '        }\r\n')

rep('g4cf [vacuum] [corr] [seed <N>] [killesc] [killcarry] [scene <file>] mono <E_keV> <N>"',
    'g4cf [vacuum] [corr] [seed <N>] [killesc] [killcarry] [fullcarry] [scene <file>] mono <E_keV> <N>"')

rep('                             " outside (no carry-in, as with --detour=0). Ablation levers, P55/P92.\\n");\r\n',
    '                             " outside (no carry-in, as with --detour=0). fullcarry: an e- born outside"\r\n'
    '                             " deposits its whole remaining energy when leaving the crystal (our"\r\n'
    '                             " ElectronCarryDeposit). Ablation levers, P55/P92.\\n");\r\n')

rep('        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld killesc=%d killcarry=%d\\n",\r\n'
    '                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed,\r\n'
    '                    gKillEscape ? 1 : 0, gKillCarry ? 1 : 0);\r\n',
    '        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld killesc=%d killcarry=%d fullcarry=%d\\n",\r\n'
    '                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed,\r\n'
    '                    gKillEscape ? 1 : 0, gKillCarry ? 1 : 0, gFullCarry ? 1 : 0);\r\n')

rep("    //      ГСЧ), `killesc` и `killcarry` (рычаги П55/П92, см. шапку) — в\r\n",
    "    //      ГСЧ), `killesc`, `killcarry`, `fullcarry` (рычаги П55/П92, см. шапку) — в\r\n")

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
