# -*- coding: utf-8 -*-
"""F28 / A244 П8 «остальное». Разделение счёта сканера на МОЮ долю и чужие."""
import csv, sys, io, os, re, collections

EXCL_PREFIX = [
    'BecquerelMonitor/FullSpectrumAnalysis/',
    'BecquerelMonitor/EfficiencyMaker/',
    'BecquerelMonitor/XPTable/',
]
EXCL_NAME_RE = re.compile(
    r'^(EfficiencyMakerForm|EnergySpectrumView.*|PeakShapePreviewGraph|'
    r'DC[A-Z].*|ROI.*|RadiaCodeIn|ObsidianIn|AtomSpectraVCPDeviceForm|'
    r'AudioInputDeviceForm|ObsidianDeviceForm|RadiaCodeDeviceForm|'
    r'DoubleTextBox|IntegerTextBox|DeviceConfigForm.*|GlobalConfigForm|'
    r'DeviceConfigManager|ResponseMatrixForm.*|MainForm|'
    r'NonlinearEnergyCalibration)$')

SLASH = chr(92)


def is_graph_utils(path):
    return path.startswith('BecquerelMonitor/Utils/') and 'Graph' in os.path.basename(path)


def mine(path):
    p = path.replace(SLASH, '/')
    for pre in EXCL_PREFIX:
        if p.startswith(pre):
            return False
    if is_graph_utils(p):
        return False
    base = os.path.basename(p)
    stem = base[:-3] if base.endswith('.cs') else base
    if EXCL_NAME_RE.match(stem):
        return False
    return True


def main():
    src = sys.argv[1]
    rows = list(csv.DictReader(io.open(src, encoding='utf-8'), delimiter='\t',
                               quoting=csv.QUOTE_NONE))
    mineC = collections.Counter()
    otherC = collections.Counter()
    mine_side = collections.Counter()
    for r in rows:
        f = r['file'].replace(SLASH, '/')
        if mine(f):
            mineC[f] += 1
            mine_side[r['side']] += 1
        else:
            otherC[f] += 1
    print('всего мест по сканеру: %d' % len(rows))
    print('МОЯ доля П8: %d мест в %d файлах (печать %d, разбор %d)'
          % (sum(mineC.values()), len(mineC), mine_side['print'], mine_side['parse']))
    print('чужие доли:  %d мест в %d файлах' % (sum(otherC.values()), len(otherC)))
    print()
    print('--- МОЯ доля, по файлам ---')
    for f, n in sorted(mineC.items(), key=lambda kv: (-kv[1], kv[0])):
        print('%4d  %s' % (n, f))


if __name__ == '__main__':
    main()
