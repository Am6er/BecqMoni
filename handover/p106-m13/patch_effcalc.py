# -*- coding: utf-8 -*-
# П106: ключ ElectronLayerBremAlongPath на пути кривой (EfficiencyCalculation.cs, BOM+CRLF): симулятор, рабочие потоков, клеймо кривой.
p = r'D:\BqMoni_Claude\p106\wt\BecquerelMonitor\EfficiencyMaker\EfficiencyCalculation.cs'
d = open(p, 'rb').read()
bom = d[:3] == b'\xef\xbb\xbf'
s = (d[3:] if bom else d).decode('utf-8')
n = 0
def rep(old, new):
    global s, n
    assert s.count(old) == 1, old[:70]
    s = s.replace(old, new); n += 1
rep('                ElectronLayerMixedScattering = storePhysics.ElectronLayerMixedScattering,\r\n            };\r\n',
    '                ElectronLayerMixedScattering = storePhysics.ElectronLayerMixedScattering,\r\n'
    '                // (`M13`, П106 19.09.2026) Тормозное электрона в слоях обвязки по\r\n'
    '                // ходу переноса — тем же путём: от умолчания настроек склада\r\n'
    '                // (ВЫКЛ до решения Amber о едином счёте), чтобы кривая и склад\r\n'
    '                // считали одну физику.\r\n'
    '                ElectronLayerBremAlongPath = storePhysics.ElectronLayerBremAlongPath,\r\n            };\r\n')
rep('                        ElectronLayerMixedScattering = simulator.ElectronLayerMixedScattering,\r\n                    };\r\n',
    '                        ElectronLayerMixedScattering = simulator.ElectronLayerMixedScattering,\r\n'
    '                        ElectronLayerBremAlongPath = simulator.ElectronLayerBremAlongPath,\r\n                    };\r\n')
rep('            // ВКЛ умолчанием склада, и у кривой он в клейме всегда.\r\n            result.ComputeStamp = string.Format(CultureInfo.InvariantCulture,\r\n'
    '                "phys={0}; hist={1}; grid={2:0.#}-{3:0.#} keV/{4} {5}{6}{7}{8}{9}{10}{11}{12}{13}{14}{15}{16}{17}",\r\n',
    '            // ВКЛ умолчанием склада, и у кривой он в клейме всегда.\r\n'
    '            // `; lbrem=1` (`M13`, П106 19.09.2026) — тем же именем, что у клейма\r\n'
    '            // матрицы, только включённым (ВЫКЛ умолчанием склада).\r\n'
    '            result.ComputeStamp = string.Format(CultureInfo.InvariantCulture,\r\n'
    '                "phys={0}; hist={1}; grid={2:0.#}-{3:0.#} keV/{4} {5}{6}{7}{8}{9}{10}{11}{12}{13}{14}{15}{16}{17}{18}",\r\n')
rep('                storePhysics.ElectronLayerMixedScattering ? "; elmix=1" : "");\r\n            return result;\r\n',
    '                storePhysics.ElectronLayerMixedScattering ? "; elmix=1" : "",\r\n'
    '                storePhysics.ElectronLayerBremAlongPath ? "; lbrem=1" : "");\r\n            return result;\r\n')
out = s.encode('utf-8')
open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + out)
print('patched', n, 'bom', bom)
