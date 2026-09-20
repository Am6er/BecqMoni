# -*- coding: utf-8 -*-
# П92 (M12): ключ --detour=<x> у G4RawProbe (worktree) — ElectronCarryDetour, рычаг абляции
# заноса электронов из обвязки (0 — заноса нет, зеркало killcarry у арбитра). CRLF и BOM сохраняются.
import io, sys
p = sys.argv[1]
raw = io.open(p, 'rb').read()
assert raw.startswith(b'\xef\xbb\xbf')
s = raw[3:].decode('utf-8')
assert '\r\n' in s

def rep(old, new, count=1):
    global s
    assert s.count(old) == count, (old[:70], s.count(old))
    s = s.replace(old, new)

rep('    ///                [--ecomp=0|1] [--bpath=0|1|2]\r\n',
    '    ///                [--ecomp=0|1] [--bpath=0|1|2] [--detour=0.7]\r\n'
    '    ///\r\n'
    '    /// `--detour=<x>` (`M12`, П92 17.09.2026) — РЫЧАГ АБЛЯЦИИ заноса электронов\r\n'
    '    /// из обвязки: `ElectronCarryDetour` (умолчание 0.7 — доля пробега CSDA по\r\n'
    '    /// прямой). `--detour=0` — заноса нет вовсе (зеркало ключа `killcarry` у\r\n'
    '    /// арбитра `g4cf`): «def − detour=0» у нас против «def − killcarry» у Geant4\r\n'
    '    /// мерит вклад заноса по полосам порознь. Не настройка склада — замер.\r\n')

rep('            int bpath = store.BremAlongPath;            // `M3`, П44 — умолчание склада (2 с физики 18, П50)\r\n',
    '            int bpath = store.BremAlongPath;            // `M3`, П44 — умолчание склада (2 с физики 18, П50)\r\n'
    '            double detour = -1.0;                       // <0 — умолчание симулятора (`M12`, П92)\r\n')

rep('                if (a.StartsWith("--esc-soft=", StringComparison.Ordinal))\r\n',
    '                // `M12` (П92): доля пробега заносимого электрона по прямой; 0 — заноса нет.\r\n'
    '                if (a.StartsWith("--detour=", StringComparison.Ordinal))\r\n'
    '                {\r\n'
    '                    detour = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);\r\n'
    '                    if (detour < 0.0)\r\n'
    '                    {\r\n'
    '                        Console.Error.WriteLine("--detour= принимает число >= 0: " + a);\r\n'
    '                        return 2;\r\n'
    '                    }\r\n'
    '\r\n'
    '                    continue;\r\n'
    '                }\r\n'
    '                if (a.StartsWith("--esc-soft=", StringComparison.Ordinal))\r\n')

rep('            simulator.BremAlongPath = bpath;            // `M3`, П44\r\n',
    '            simulator.BremAlongPath = bpath;            // `M3`, П44\r\n'
    '            if (detour >= 0.0) { simulator.ElectronCarryDetour = detour; }   // `M12`, П92\r\n')

rep('            Console.WriteLine("тормозное вдоль пути (`M3`, --bpath=): {0}{1}",\r\n',
    '            Console.WriteLine("занос электрона из обвязки (`M12`, --detour=): доля пробега по прямой {0}{1}",\r\n'
    '                              simulator.ElectronCarryDetour.ToString("0.###", CultureInfo.InvariantCulture),\r\n'
    '                              detour < 0.0 ? " (умолчание симулятора)" : detour == 0.0 ? " (ключом — ЗАНОСА НЕТ)" : " (ключом)");\r\n'
    '            Console.WriteLine("тормозное вдоль пути (`M3`, --bpath=): {0}{1}",\r\n')

io.open(p, 'wb').write(b'\xef\xbb\xbf' + s.encode('utf-8'))
print('ok')
