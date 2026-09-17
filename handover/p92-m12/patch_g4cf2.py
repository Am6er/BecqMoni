# -*- coding: utf-8 -*-
# П92: печать порогов напрямую таблицей (только мастер), а не UI-командой — та рассылается
# всем рабочим потокам и печатается 12 раз.
import io, sys
p = sys.argv[1]
s = io.open(p, encoding='utf-8', newline='').read()
old1 = '#include "G4Electron.hh"\r\n'
new1 = '#include "G4Electron.hh"\r\n#include "G4ProductionCutsTable.hh"\r\n'
assert s.count(old1) == 1
s = s.replace(old1, new1)
old2 = ('    // настройка: умолчания арбитра не меняются.\r\n'
        '    ui->ApplyCommand("/run/dumpCouples");\r\n')
new2 = ('    // настройка: умолчания арбитра не меняются. Таблицей, а не UI-командой\r\n'
        '    // `/run/dumpCouples`: команда рассылается рабочим потокам и печатается\r\n'
        '    // столько раз, сколько потоков.\r\n'
        '    std::printf("CUTS (production thresholds per material couple):\n");\r\n'
        '    G4ProductionCutsTable::GetProductionCutsTable()->DumpCouples();\r\n')
assert s.count(old2) == 1
s = s.replace(old2, new2)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok')
