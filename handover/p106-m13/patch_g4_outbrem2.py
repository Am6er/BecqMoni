# -*- coding: utf-8 -*-
# П106: killoutbrem гасит ТОЛЬКО тормозное (eBrem); кванты аннигиляции позитрона обвязки считаются отдельно (CRLF, байтами).
p = r'D:\BqMoni_Claude\p106\g4\g4cf.cc'
d = open(p, 'rb').read()
s = d.decode('utf-8')
n = 0
BS = '\\'
def rep(old, new):
    global s, n
    assert s.count(old) == 1, old[:80]
    s = s.replace(old, new); n += 1
rep('        EscKilledOutBrem,    // из них погашено (killoutbrem)\r\n',
    '        EscKilledOutBrem,    // из них погашено (killoutbrem)\r\n'
    '        EscOutAnnihGamma,    // квантов аннигиляции позитрона, рождённого вне кристалла (рычаг их не трогает)\r\n'
    '        EscOutOtherGamma,    // прочих квантов от лептонов вне кристалла (самопроверка: ожидается ~0)\r\n')
rep('                        " facemiss=%d lineage_inside=%d out_lepton=%d out_brem_gamma=%d killed_out_brem=%d' + BS + 'n",',
    '                        " facemiss=%d lineage_inside=%d out_lepton=%d out_brem_gamma=%d killed_out_brem=%d out_annih_gamma=%d out_other_gamma=%d' + BS + 'n",')
rep('                        fEsc[EscKilledOutBrem]->GetValue());',
    '                        fEsc[EscKilledOutBrem]->GetValue(), fEsc[EscOutAnnihGamma]->GetValue(), fEsc[EscOutOtherGamma]->GetValue());')
old = ('            else if (track->GetDefinition() == G4Gamma::GammaDefinition() && fEvent->IsOutLepton(track->GetParentID()))\r\n'
       '            {\r\n'
       '                fEvent->Run()->CountEsc(RunAction::EscOutBremGamma);\r\n'
       '                if (gKillOutBrem)\r\n'
       '                {\r\n'
       '                    fEvent->Run()->CountEsc(RunAction::EscKilledOutBrem);\r\n'
       '                    return fKill;\r\n'
       '                }\r\n'
       '            }\r\n')
new = ('            else if (track->GetDefinition() == G4Gamma::GammaDefinition() && fEvent->IsOutLepton(track->GetParentID()))\r\n'
       '            {\r\n'
       '                // ⚠ Только ТОРМОЗНОЕ (`eBrem`): у позитрона, рождённого в обвязке\r\n'
       '                // парой, кванты аннигиляции тоже «рождены вне кристалла лептоном,\r\n'
       '                // рождённым вне», но это не тормозное — наша сторона ведёт их\r\n'
       '                // фотонным обходом (`A52`), и рычаг их не трогает. Первая редакция\r\n'
       '                // рычага (П106, 02:00) гасила и их: плечо арбитра выходило на\r\n'
       '                // 511-кэВных квантах шире нашего `--ret-kill=outbrem`, особенно у\r\n'
       '                // ториевого стекла (пары ∝ Z²). Флуоресценции от ионизации\r\n'
       '                // электронами у option4 нет (PIXE выкл) — прочие кванты считаются\r\n'
       '                // отдельно как самопроверка.\r\n'
       '                const G4VProcess* creator = track->GetCreatorProcess();\r\n'
       '                std::string pname = creator != nullptr ? creator->GetProcessName() : std::string();\r\n'
       '                if (pname.find("annihil") != std::string::npos)\r\n'
       '                {\r\n'
       '                    fEvent->Run()->CountEsc(RunAction::EscOutAnnihGamma);\r\n'
       '                }\r\n'
       '                else if (pname.find("Brem") != std::string::npos)\r\n'
       '                {\r\n'
       '                    fEvent->Run()->CountEsc(RunAction::EscOutBremGamma);\r\n'
       '                    if (gKillOutBrem)\r\n'
       '                    {\r\n'
       '                        fEvent->Run()->CountEsc(RunAction::EscKilledOutBrem);\r\n'
       '                        return fKill;\r\n'
       '                    }\r\n'
       '                }\r\n'
       '                else\r\n'
       '                {\r\n'
       '                    fEvent->Run()->CountEsc(RunAction::EscOutOtherGamma);\r\n'
       '                }\r\n'
       '            }\r\n')
rep(old, new)
rep('    //   killoutbrem  — квант, рождённый ВНЕ кристалла лептоном (e-/e+), который\r\n'
    '    //                  и сам рождён вне кристалла (фото-/комптон-электрон\r\n'
    '    //                  обвязки, пробы), гасится при постановке в стек:\r\n',
    '    //   killoutbrem  — квант ТОРМОЗНОГО (`eBrem`), рождённый ВНЕ кристалла лептоном\r\n'
    '    //                  (e-/e+), который и сам рождён вне кристалла (фото-/комптон-\r\n'
    '    //                  электрон обвязки, пробы), гасится при постановке в стек\r\n'
    '    //                  (кванты аннигиляции позитрона обвязки — НЕ гасятся, считаются):\r\n')
open(p, 'wb').write(s.encode('utf-8'))
print('patched', n)
d = open(p, 'rb').read(); print('CRLF', d.count(b'\r\n'), 'LF', d.count(b'\n') - d.count(b'\r\n'))
