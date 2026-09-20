# -*- coding: utf-8 -*-
# П106: счётчики лептонов вне кристалла по процессу-создателю (compt/phot/conv/eIoni) и по порогу 20 кэВ — население заноса арбитра.
p = r'D:\BqMoni_Claude\p106\g4\g4cf.cc'
s = open(p, 'rb').read().decode('utf-8')
n = 0
BS = chr(92)
def rep(old, new):
    global s, n
    assert s.count(old) == 1, old[:80]
    s = s.replace(old, new); n += 1
rep('        EscOutOtherGamma,    // прочих квантов от лептонов вне кристалла (самопроверка: ожидается ~0)\r\n',
    '        EscOutOtherGamma,    // прочих квантов от лептонов вне кристалла (самопроверка: ожидается ~0)\r\n'
    '        EscOutLeptonCompt,   // из лептонов вне кристалла: рождённых комптоном\r\n'
    '        EscOutLeptonPhot,    // фотоэффектом\r\n'
    '        EscOutLeptonConv,    // парой\r\n'
    '        EscOutLeptonIoni,    // ионизацией (δ-электроны)\r\n'
    '        EscOutLeptonAbove20, // из всех: с кинетикой ≥ 20 кэВ (наш порог переноса в слоях)\r\n')
rep('                        " facemiss=%d lineage_inside=%d out_lepton=%d out_brem_gamma=%d killed_out_brem=%d out_annih_gamma=%d out_other_gamma=%d' + BS + 'n",',
    '                        " facemiss=%d lineage_inside=%d out_lepton=%d out_brem_gamma=%d killed_out_brem=%d out_annih_gamma=%d out_other_gamma=%d"\r\n'
    '                        " out_lepton_compt=%d out_lepton_phot=%d out_lepton_conv=%d out_lepton_ioni=%d out_lepton_ge20kev=%d' + BS + 'n",')
rep('                        fEsc[EscKilledOutBrem]->GetValue(), fEsc[EscOutAnnihGamma]->GetValue(), fEsc[EscOutOtherGamma]->GetValue());',
    '                        fEsc[EscKilledOutBrem]->GetValue(), fEsc[EscOutAnnihGamma]->GetValue(), fEsc[EscOutOtherGamma]->GetValue(),\r\n'
    '                        fEsc[EscOutLeptonCompt]->GetValue(), fEsc[EscOutLeptonPhot]->GetValue(), fEsc[EscOutLeptonConv]->GetValue(),\r\n'
    '                        fEsc[EscOutLeptonIoni]->GetValue(), fEsc[EscOutLeptonAbove20]->GetValue());')
rep('            if (pdg == 11)\r\n            {\r\n                fEvent->MarkOutLepton(track->GetTrackID());\r\n                fEvent->Run()->CountEsc(RunAction::EscOutLepton);\r\n            }\r\n',
    '            if (pdg == 11)\r\n            {\r\n                fEvent->MarkOutLepton(track->GetTrackID());\r\n                fEvent->Run()->CountEsc(RunAction::EscOutLepton);\r\n'
    '                // Население по процессу-создателю и по нашему порогу переноса (20 кэВ).\r\n'
    '                const G4VProcess* cp = track->GetCreatorProcess();\r\n'
    '                std::string cname = cp != nullptr ? cp->GetProcessName() : std::string();\r\n'
    '                if (cname.find("compt") != std::string::npos) { fEvent->Run()->CountEsc(RunAction::EscOutLeptonCompt); }\r\n'
    '                else if (cname.find("phot") != std::string::npos) { fEvent->Run()->CountEsc(RunAction::EscOutLeptonPhot); }\r\n'
    '                else if (cname.find("conv") != std::string::npos) { fEvent->Run()->CountEsc(RunAction::EscOutLeptonConv); }\r\n'
    '                else if (cname.find("Ioni") != std::string::npos) { fEvent->Run()->CountEsc(RunAction::EscOutLeptonIoni); }\r\n'
    '                if (track->GetKineticEnergy() >= 20.0 * keV) { fEvent->Run()->CountEsc(RunAction::EscOutLeptonAbove20); }\r\n'
    '            }\r\n')
open(p, 'wb').write(s.encode('utf-8'))
print('patched', n)
