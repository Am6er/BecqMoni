# -*- coding: utf-8 -*-
# П92 (M12): правка tools/g4cf/g4cf.cc в worktree — ключи killesc/killcarry в разборе
# аргументов, строка подсказки, печать порогов /run/dumpCouples в шапке прогона.
# CRLF файла сохраняется (newline='').
import io, sys
p = sys.argv[1]
s = io.open(p, encoding='utf-8', newline='').read()
assert '\r\n' in s

def rep(old, new, count=1):
    global s
    assert s.count(old) == count, (old[:70], s.count(old))
    s = s.replace(old, new)

rep('#include "G4Step.hh"\r\n', '#include "G4Step.hh"\r\n#include "G4Electron.hh"\r\n')

rep("    //      `corr` (угловые γ–γ корреляции каскада в RDM) и `seed <N>` (зерно\r\n"
    "    //      ГСЧ) — в любом порядке.\r\n",
    "    //      `corr` (угловые γ–γ корреляции каскада в RDM), `seed <N>` (зерно\r\n"
    "    //      ГСЧ), `killesc` и `killcarry` (рычаги П55/П92, см. шапку) — в\r\n"
    "    //      любом порядке.\r\n")

rep('    while (argc > base && (std::strcmp(argv[base], "vacuum") == 0 || std::strcmp(argv[base], "corr") == 0\r\n'
    '                           || (std::strcmp(argv[base], "seed") == 0 && argc > base + 1)))\r\n'
    '    {\r\n'
    '        if (std::strcmp(argv[base], "vacuum") == 0)\r\n'
    '        {\r\n'
    '            gVacuumWorld = true;\r\n'
    '        }\r\n',
    '    while (argc > base && (std::strcmp(argv[base], "vacuum") == 0 || std::strcmp(argv[base], "corr") == 0\r\n'
    '                           || std::strcmp(argv[base], "killesc") == 0 || std::strcmp(argv[base], "killcarry") == 0\r\n'
    '                           || (std::strcmp(argv[base], "seed") == 0 && argc > base + 1)))\r\n'
    '    {\r\n'
    '        if (std::strcmp(argv[base], "vacuum") == 0)\r\n'
    '        {\r\n'
    '            gVacuumWorld = true;\r\n'
    '        }\r\n'
    '        else if (std::strcmp(argv[base], "killesc") == 0)\r\n'
    '        {\r\n'
    '            gKillEscape = true;\r\n'
    '        }\r\n'
    '        else if (std::strcmp(argv[base], "killcarry") == 0)\r\n'
    '        {\r\n'
    '            gKillCarry = true;\r\n'
    '        }\r\n')

old_usage = ('        std::fprintf(stderr, "g4cf [vacuum] [corr] [seed <N>] [scene <file>] mono <E_keV> <N> | ion <Z> <A> <N> <windows...>"\r\n'
             '                             " | hist <E_keV> <N> <bin_keV>\\n"\r\n')
new_usage = ('        std::fprintf(stderr, "g4cf [vacuum] [corr] [seed <N>] [killesc] [killcarry] [scene <file>] mono <E_keV> <N>"\r\n'
             '                             " | ion <Z> <A> <N> <windows...> | hist <E_keV> <N> <bin_keV>\\n"\r\n')
rep(old_usage, new_usage)

old_corr = ('                             "  corr: gamma-gamma angular correlations in RDM (G4DeexPrecoParameters::"\r\n'
            '                             "SetCorrelatedGamma); default is isotropic.\\n");\r\n')
new_corr = ('                             "  corr: gamma-gamma angular correlations in RDM (G4DeexPrecoParameters::"\r\n'
            '                             "SetCorrelatedGamma); default is isotropic.\\n"\r\n'
            '                             "  killesc: kill e- leaving the crystal (no return from cladding, as in"\r\n'
            '                             " EfficiencySimulator). killcarry: kill e- entering the crystal from"\r\n'
            '                             " outside (no carry-in, as with --detour=0). Ablation levers, P55/P92.\\n");\r\n')
rep(old_corr, new_corr)

old_setup = ('        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld\\n",\r\n'
             '                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed);\r\n'
             '        std::fflush(stdout);\r\n'
             '    }\r\n')
new_setup = ('        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld killesc=%d killcarry=%d\\n",\r\n'
             '                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed,\r\n'
             '                    gKillEscape ? 1 : 0, gKillCarry ? 1 : 0);\r\n'
             '        std::fflush(stdout);\r\n'
             '    }\r\n'
             '    // Пороги рождения вторичных (production cuts) по веществам сцены — в шапку\r\n'
             '    // каждого прогона (`M12`, П92; факт П55 §5.2): умолчание cut 0.7 мм даёт в\r\n'
             '    // NaI/CsI порог явных δ-электронов ~592 кэВ, и без этой печати он невидим —\r\n'
             '    // все сверки вылета/заноса электронов идут при этом пороге. Печать, не\r\n'
             '    // настройка: умолчания арбитра не меняются.\r\n'
             '    ui->ApplyCommand("/run/dumpCouples");\r\n'
             '    std::fflush(stdout);\r\n')
rep(old_setup, new_setup)

io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
