# -*- coding: utf-8 -*-
# П106: LayerReturnProbe --brem — тормозное по ходу в слоях (ключ lbrem) на электрон против толстой мишени вещества первого слоя.
p = r'D:\BqMoni_Claude\p106\wt\tools\effmaker\probes\LayerReturnProbe.cs'
d = open(p, 'rb').read(); bom = d[:3] == b'\xef\xbb\xbf'
s = (d[3:] if bom else d).decode('utf-8')
n = 0
def rep(old, new):
    global s, n
    assert s.count(old) == 1, old[:70]
    s = s.replace(old, new); n += 1
rep('///                      [--elmix=0|1|both] [--cutoff=20] [--step=0.1] [--diag]\r\n',
    '///                      [--elmix=0|1|both] [--cutoff=20] [--step=0.1] [--diag] [--brem]\r\n'
    '///\r\n'
    '/// (`M13`, П106) `--brem` — ключ `ElectronLayerBremAlongPath` ВКЛ на время прогона: печатает\r\n'
    '/// квантов тормозного по ходу переноса в слоях на один пущенный электрон и их среднюю\r\n'
    '/// энергию против толстой мишени вещества первого слоя (`ThickTargetBrem.Photons(T)`) —\r\n'
    '/// поверка выхода тонкой мишени по пути: на толстом слое отношение обязано быть ≈ 1,\r\n'
    '/// на RC103 (PTFE 1 мм + Al 1 мм + пустота) — меньше ровно на долю пути, ушедшую в пустоту.\r\n')
rep('        bool crystal = false, diag = false;\r\n',
    '        bool crystal = false, diag = false, brem = false;\r\n')
rep('            else if (a == "--crystal") crystal = true;\r\n',
    '            else if (a == "--crystal") crystal = true;\r\n            else if (a == "--brem") brem = true;\r\n')
rep('        MethodInfo saveRay = typeof(EfficiencySimulator).GetMethod("SaveRay", BindingFlags.NonPublic | BindingFlags.Instance);\r\n',
    '        MethodInfo saveRay = typeof(EfficiencySimulator).GetMethod("SaveRay", BindingFlags.NonPublic | BindingFlags.Instance);\r\n'
    '        // (П106) Тормозное по ходу: приватный рычаг `layerBremEnabled` (его ставит вызывающий переноса),\r\n'
    '        // область точки старта и таблица толстой мишени её вещества — отражением.\r\n'
    '        FieldInfo bremEnabled = typeof(EfficiencySimulator).GetField("layerBremEnabled", BindingFlags.NonPublic | BindingFlags.Instance);\r\n'
    '        MethodInfo atMethod = typeof(EfficiencySimulator).GetMethod("At", BindingFlags.NonPublic | BindingFlags.Instance);\r\n'
    '        MethodInfo layerBrem = typeof(EfficiencySimulator).GetMethod("LayerBrem", BindingFlags.NonPublic | BindingFlags.Instance);\r\n')
rep('                    var sim = new EfficiencySimulator(geometry.Clone());\r\n                    sim.ElectronLayerTransport = true;\r\n                    sim.ElectronLayerMixedScattering = m;\r\n',
    '                    var sim = new EfficiencySimulator(geometry.Clone());\r\n                    sim.ElectronLayerTransport = true;\r\n                    sim.ElectronLayerMixedScattering = m;\r\n'
    '                    if (brem) { sim.ElectronLayerBremAlongPath = true; bremEnabled.SetValue(sim, true); }\r\n')
rep('                    double eta = back / (double)n;\r\n',
    '                    double eta = back / (double)n;\r\n'
    '                    if (brem)\r\n'
    '                    {\r\n'
    '                        object region = atMethod.Invoke(sim, new object[] { x0 + nx * 1e-7, 0.0, z0 + nz * 1e-7 });\r\n'
    '                        var material = region != null ? (GeometryMaterial)region.GetType().GetField("Material").GetValue(region) : null;\r\n'
    '                        var table = material != null ? (ThickTargetBrem)layerBrem.Invoke(sim, new object[] { material }) : null;\r\n'
    '                        double thick = table != null ? table.Photons(te) : 0.0;\r\n'
    '                        double perElectron = sim.CountLayerBremPhotons / (double)n;\r\n'
    '                        Console.WriteLine("    тормозное по ходу (--brem, T={0} кэВ, θ={1}°, elmix={2}): {3} квантов на электрон, средняя энергия {4} кэВ; толстая мишень вещества старта {5} квантов — отношение {6}",\r\n'
    '                                          te, ang, m ? 1 : 0, perElectron.ToString("0.00000", CultureInfo.InvariantCulture),\r\n'
    '                                          (sim.CountLayerBremPhotons > 0 ? sim.SumLayerBremKev / sim.CountLayerBremPhotons : 0.0).ToString("0.0", CultureInfo.InvariantCulture),\r\n'
    '                                          thick.ToString("0.00000", CultureInfo.InvariantCulture),\r\n'
    '                                          (thick > 0.0 ? perElectron / thick : 0.0).ToString("0.000", CultureInfo.InvariantCulture));\r\n'
    '                    }\r\n')
open(p, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + s.encode('utf-8'))
print('patched', n)
