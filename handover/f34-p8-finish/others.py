# -*- coding: utf-8 -*-
"""F34: остаток сканера в файлах ВНЕ 29 файлов F28 — есть ли там ещё доля П8."""
import csv, io, sys, collections

MINE = """DoseRate.cs NucBase/NucBase.cs NucBase/Decay.cs NucBase/DecayRad.cs N42/Util.cs
NuclideDefinitionForm.cs PercentageProgressBar.cs FWHMPeakDetector/PeakFinder.cs
AudioInputDeviceController.cs ChanNumberChangeDialog.cs EnergySpectrum.cs GeometryEditorPanel.cs
GeometryMaterialEditorForm.cs GlobalConfigManager.cs NuclideDefinition.cs NuclideSetForm.cs
ObsidianDeviceController.cs PowerFwhmCalibration.cs RadiaCodeDeviceController.cs
SimpleSqrtFwhmCalibration.cs SpectrumCutOffDialog.cs SqrtFwhmCalibration.cs
WinMM/NativeMethods.cs WinMM/WaveIn.cs WinMM/WaveOut.cs DocumentManager.cs
PolynomialEnergyCalibration.cs PulseView.cs Utils/BecquerelCoefficient.cs""".split()
MINE = set('BecquerelMonitor/' + m for m in MINE)

rows = []
with io.open(sys.argv[1], encoding='utf-8', newline='') as f:
    for r in csv.DictReader(f, delimiter='\t', quoting=csv.QUOTE_NONE):
        r['file'] = r['file'].replace('\\', '/')
        if r['file'] not in MINE:
            rows.append(r)

c = collections.Counter(r['file'] for r in rows)
k = collections.Counter(r['kind'] for r in rows)
s = collections.Counter(r['side'] for r in rows)
print('мест вне 29 файлов F28: %d, файлов: %d' % (len(rows), len(c)))
print('по стороне: %s' % dict(s))
print('по выводу типа: %s' % dict(k))
print()
print('── NUM (сканер уверен, что это ЧИСЛО) — только эти могут быть настоящими ──')
num = [r for r in rows if r['kind'] == 'NUM']
print('всего NUM: %d' % len(num))
for r in sorted(num, key=lambda x: (x['file'], int(x['line']))):
    print('  %s:%s\t%s\t%s\t%s' % (r['file'], r['line'], r['side'], r['api'], r['receiver'][:60]))
print()
print('── по файлам (все виды) ──')
for f_, n in c.most_common(40):
    print('  %-58s %3d' % (f_, n))
