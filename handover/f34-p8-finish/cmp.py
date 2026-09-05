# -*- coding: utf-8 -*-
"""F34: сравнение счёта сканера ДО (HEAD) и ПОСЛЕ (дерево) по файлам доли П8."""
import csv, io, sys, collections

MINE = """DoseRate.cs NucBase/NucBase.cs NucBase/Decay.cs NucBase/DecayRad.cs N42/Util.cs
NuclideDefinitionForm.cs PercentageProgressBar.cs FWHMPeakDetector/PeakFinder.cs
AudioInputDeviceController.cs ChanNumberChangeDialog.cs EnergySpectrum.cs GeometryEditorPanel.cs
GeometryMaterialEditorForm.cs GlobalConfigManager.cs NuclideDefinition.cs NuclideSetForm.cs
ObsidianDeviceController.cs PowerFwhmCalibration.cs RadiaCodeDeviceController.cs
SimpleSqrtFwhmCalibration.cs SpectrumCutOffDialog.cs SqrtFwhmCalibration.cs
WinMM/NativeMethods.cs WinMM/WaveIn.cs WinMM/WaveOut.cs DocumentManager.cs
PolynomialEnergyCalibration.cs PulseView.cs Utils/BecquerelCoefficient.cs""".split()
MINE = set('BecquerelMonitor/' + m.replace('\\', '/') for m in MINE)


def load(path):
    rows = []
    # ⛔ `args` содержит кавычки — csv с обычным цитированием СКЛЕИВАЕТ строки
    #    и молча теряет их (первый заход: 501 место в файле, 153 после разбора).
    with io.open(path, encoding='utf-8', newline='') as f:
        for r in csv.DictReader(f, delimiter='\t', quoting=csv.QUOTE_NONE):
            r['file'] = r['file'].replace('\\', '/')
            rows.append(r)
    return rows


head = load(sys.argv[1])
now = load(sys.argv[2])


def by_file(rows):
    c = collections.Counter()
    for r in rows:
        c[r['file']] += 1
    return c


hf, nf = by_file(head), by_file(now)
print('ВСЕГО по дереву: HEAD %d -> сейчас %d  (дельта %+d)' % (len(head), len(now), len(now) - len(head)))
mh = sum(v for k, v in hf.items() if k in MINE)
mn = sum(v for k, v in nf.items() if k in MINE)
print('МОИ 29 .cs доли П8: HEAD %d -> сейчас %d  (дельта %+d)' % (mh, mn, mn - mh))
oh = len(head) - mh
on = len(now) - mn
print('ОСТАЛЬНОЕ дерево:   HEAD %d -> сейчас %d  (дельта %+d, это чужие полосы)' % (oh, on, on - oh))
print()
print('── мои файлы, где счёт ещё не ноль ──')
for k in sorted(MINE):
    if nf.get(k, 0):
        print('  %-58s %3d -> %3d' % (k, hf.get(k, 0), nf.get(k, 0)))
print()
print('── мои файлы, закрытые до нуля ──')
for k in sorted(MINE):
    if hf.get(k, 0) and not nf.get(k, 0):
        print('  %-58s %3d -> 0' % (k, hf.get(k, 0)))
print()
print('── поимённый остаток в моих файлах ──')
for r in now:
    if r['file'] in MINE:
        print('  %s:%s\t%s\t%s\t%s\t%s' % (r['file'], r['line'], r['side'], r['api'],
                                           r['kind'], (r['receiver'] + ' | ' + r['args'])[:110]))
