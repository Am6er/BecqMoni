# -*- coding: utf-8 -*-
import io, sys
p = r'D:\BqMoni_Claude\p103\wt\tools\effmaker\probes\BoundProbeF59.cs'
b = open(p,'rb').read()
s = b.decode('utf-8').replace('\r\n', '\n')
reps = [(u"""            // Семь ключей физики 17 (П37/П38: `lbin` `pkch` `lys=2` `etr` `e+tr` `e+off` `rayl2`),
            // два ключа физики 18 (`ecomp=1` `bpath=2`, П50; решение Amber 13.09.2026
            // «ecomp=1 + bpath=2») и ключ физики 19 (`eltr=1`, П97 18.09.2026; решение Amber
            // 17.09.2026 «ВКЛ сейчас, единый счёт ночью») — умолчания КЛАССА, одно место истины
            // (правило I `check_matrix_keys.py`): путь склада, путь кривой и поля симулятора берут их отсюда.
            Say("");
            Say("-- A120: умолчания физики 17, 18 и 19 (склад = кривая = симулятор) --");""",
u"""            // Семь ключей физики 17 (П37/П38: `lbin` `pkch` `lys=2` `etr` `e+tr` `e+off` `rayl2`),
            // два ключа физики 18 (`ecomp=1` `bpath=2`, П50; решение Amber 13.09.2026
            // «ecomp=1 + bpath=2»), ключ физики 19 (`eltr=1`, П97 18.09.2026; решение Amber
            // 17.09.2026 «ВКЛ сейчас, единый счёт ночью») и ключ физики 20 (`elmix=1`, П103
            // 19.09.2026; решение Amber 18.09.2026 по приёмке П100, дословно: «ВКЛ сейчас, единый
            // счёт ночью») — умолчания КЛАССА, одно место истины (правило I `check_matrix_keys.py`):
            // путь склада, путь кривой и поля симулятора берут их отсюда.
            Say("");
            Say("-- A120: умолчания физики 17, 18, 19 и 20 (склад = кривая = симулятор) --");"""),
(u"""            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronLayerTransport = {0}", options.ElectronLayerTransport));
            Ok(ResponseMatrix.PhysicsVersion == 19,
               string.Format(CultureInfo.InvariantCulture, "версия физики склада — 19 (есть {0})", ResponseMatrix.PhysicsVersion));""",
u"""            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronLayerTransport = {0}", options.ElectronLayerTransport));
            Say(string.Format(CultureInfo.InvariantCulture, "   ElectronLayerMixedScattering = {0}", options.ElectronLayerMixedScattering));
            Ok(ResponseMatrix.PhysicsVersion == 20,
               string.Format(CultureInfo.InvariantCulture, "версия физики склада — 20 (есть {0})", ResponseMatrix.PhysicsVersion));"""),
(u"""            Ok(options.ElectronLayerTransport,
               "ключ физики 19 умолчанием ВКЛ: eltr=1");
        }""",
u"""            Ok(options.ElectronLayerTransport,
               "ключ физики 19 умолчанием ВКЛ: eltr=1");
            Ok(options.ElectronLayerMixedScattering,
               "ключ физики 20 умолчанием ВКЛ: elmix=1");
        }""")]
for o, n in reps:
    assert s.count(o) == 1, o[:60]
    s = s.replace(o, n)
nb = s.replace('\n', '\r\n').encode('utf-8')
assert nb.count(b'\r\n') == nb.count(b'\n')
open(p,'wb').write(nb)
print('ok', nb.count(b'\r\n'), 'BOM', nb[:3] == b'\xef\xbb\xbf')
