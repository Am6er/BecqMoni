# -*- coding: utf-8 -*-
r"""П85 (AMBER42): правка tools/g4cf/g4cf.cc — ключ `corr` (угловые γ–γ корреляции в RDM).
Запускать из корня дерева. Файл в рабочей копии CRLF (autocrlf); правка сохраняет его переводы строк.
"""
import io, sys
p = 'tools/g4cf/g4cf.cc'
t = io.open(p, encoding='utf-8', newline='').read()
nl = '\r\n' if t.count('\r\n') == t.count('\n') and t.count('\n') > 0 else '\n'
BS = chr(92)  # обратный слэш: через heredoc не доезжает, поэтому собирается тут
print('newline:', repr(nl))

def rep(old, new, tag):
    global t
    old = old.replace('\n', nl); new = new.replace('\n', nl)
    if old not in t:
        raise SystemExit('не нашёл фрагмент: ' + tag)
    t = t.replace(old, new, 1)

rep("""// Угловые корреляции гамма-каскада в Geant4 по умолчанию ВЫКЛЮЧЕНЫ
// (/process/had/deex/correlatedGamma false) — то есть ион-режим отвечает
// ровно на вопрос нашей формулы (изотропные совпадения), без примеси N5.
""", """// Угловые корреляции гамма-каскада в Geant4 по умолчанию ВЫКЛЮЧЕНЫ
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
""", 'шапка')

rep('#include "G4IonTable.hh"\n',
    '#include "G4IonTable.hh"\n#include "G4NuclearLevelData.hh"\n#include "G4DeexPrecoParameters.hh"\n', 'include')

rep("    bool gVacuumWorld = false;\n",
    "    bool gVacuumWorld = false;\n\n"
    "    // Угловые γ–γ корреляции каскада в RDM (ключ `corr`, П85 `AMBER42`);\n"
    "    // умолчание — изотропно, как было всегда.\n"
    "    bool gCorrelatedGamma = false;\n", 'переменная')

rep("""    //      Перед всем этим может стоять `vacuum` — мир пустой вместо воздуха.
    int base = 1;
    if (argc > base && std::strcmp(argv[base], "vacuum") == 0)
    {
        gVacuumWorld = true;
        base++;
    }
""", """    //      Перед всем этим могут стоять `vacuum` (мир пустой вместо воздуха) и
    //      `corr` (угловые γ–γ корреляции каскада в RDM) — в любом порядке.
    int base = 1;
    while (argc > base && (std::strcmp(argv[base], "vacuum") == 0 || std::strcmp(argv[base], "corr") == 0))
    {
        if (std::strcmp(argv[base], "vacuum") == 0)
        {
            gVacuumWorld = true;
        }
        else
        {
            gCorrelatedGamma = true;
        }

        base++;
    }
""", 'разбор ключей')

rep("""        std::fprintf(stderr, "g4cf [vacuum] [scene <file>] mono <E_keV> <N> | ion <Z> <A> <N> <windows...>"
                             " | hist <E_keV> <N> <bin_keV>%sn"
                             "  vacuum: empty world instead of air. MANDATORY for bare-crystal"
                             " checks (T133): air gives 8.232e-4 vs 5.794e-4 in 55...59 keV.%sn");""" % (BS, BS),
    """        std::fprintf(stderr, "g4cf [vacuum] [corr] [scene <file>] mono <E_keV> <N> | ion <Z> <A> <N> <windows...>"
                             " | hist <E_keV> <N> <bin_keV>%sn"
                             "  vacuum: empty world instead of air. MANDATORY for bare-crystal"
                             " checks (T133): air gives 8.232e-4 vs 5.794e-4 in 55...59 keV.%sn"
                             "  corr: gamma-gamma angular correlations in RDM (G4DeexPrecoParameters::"
                             "SetCorrelatedGamma); default is isotropic.%sn");""" % (BS, BS, BS), 'usage')

rep("""    auto ui = G4UImanager::GetUIpointer();
    ui->ApplyCommand("/run/initialize");
""", """    // Угловые корреляции — ДО инициализации: после неё параметры деэкситации
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
        std::printf("SETUP correlatedGamma=%%d twoJmax=%%d vacuum=%%d%sn",
                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0);
        std::fflush(stdout);
    }
""" % BS, 'инициализация')

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok: правка внесена')
