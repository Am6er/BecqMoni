# -*- coding: utf-8 -*-
"""Сравнение гейтов значимости на всём корпусе.

Меряет одно и то же четырьмя критериями на одних и тех же спектрах и одних и
тех же сетах-обманках:

  z         Fisher z фитованной амплитуды — исходный критерий
  dd        тест отношения правдоподобий ΔD с перефитом соседей
  shape     устойчивость к смене модели фона: чистая площадь значима над
            ЛИНЕЙНОЙ и над КВАДРАТИЧНОЙ подложкой по крыльям
  dd+shape  ΔD как дешёвый предварительный отсев, затем устойчивость

Две цифры на критерий:

  recall    доля сильных табличных линий цепочки (I >= 1 % на распад родителя,
            внутри диапазона детектора, слитые в пределах 0.6 FWHM — за одну),
            на которые сел хоть какой-нибудь итоговый пик. Считается только на
            спектрах, где цепочка по манифесту действительно есть.
  фантомы   доля принятых линий сета-обманки среди ПРЕДЪЯВЛЕННЫХ — то есть
            среди прогонов, где якорь сработал и фит вообще запустился.
            Прогон, где якорь не совпал ни с одним пиком, даёт ноль фантомов и
            полный набор линий в знаменатель, отчего слабый спектр выглядит
            чистым, хотя ничего не фитилось.

Запуск:
    python gate_study.py --run      собрать сеты, рабочие каталоги и прогнать
    python gate_study.py --report   разобрать готовые CSV
"""
import csv
import os
import shutil
import glob
import subprocess
import sys
from collections import defaultdict

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
LAB = os.path.dirname(HERE)
CORPUS = os.path.join(LAB, 'corpus')
OUT = os.path.join(HERE, 'out_gate')
BUILD = os.path.join(os.path.dirname(os.path.dirname(LAB)),
                     'BecquerelMonitor', 'bin', 'Debug_Codex')
APPDATA_CONFIG = os.path.join(os.environ.get('APPDATA', ''), 'BecqMoni', 'config')

sys.path.insert(0, HERE)
from chains import chain_lines, CHAINS, ANCHORS          # noqa: E402

# Точки сравнения: имя -> ключи харнесса. Порог значимости оказался почти
# безразличен (от z = 2 до z = 6 recall двигается на 4 п.п.), поэтому дальше
# перебирается ГЕОМЕТРИЯ окна и порядок подложки — то, что решает, может ли
# квадратика повторить сам пик, и не залезли ли крылья на соседнюю линию.
VARIANTS = [
    ('chain+absence', ['--chain-scatter=1.25', '--chain-min-lines=6', '--absence-miss=0.35'], 'production'),
    ('chain+absence', ['--chain-scatter=1.25', '--chain-min-lines=6', '--absence-miss=0.35', '--eff-curve=%s' % os.path.join(os.path.dirname(HERE), 'data', 'eff_by_spectrum_lsrm.csv')], 'кривые LSRM'),
    ('chain+absence', ['--chain-scatter=1.25', '--chain-min-lines=6', '--absence-miss=0.35', '--eff-curve=%s' % os.path.join(os.path.dirname(HERE), 'data', 'eff_by_spectrum_lsrm_sens.csv')], 'LSRM, др. геометрия'),
]

K, IMIN = 0.7, 1.0
I_REF = 1.0        # линия идёт в знаменатель recall с этой интенсивности, %

# Голова ряда U-238: то, что живёт в урановом стекле, где ряд оборван на радии.
U238_HEAD = {'238U', '234TH', '234PAm1', '234PA', '234U', '230TH'}

# Как метка цепочки в манифесте отображается в режим подсчёта recall.
CHAIN_MODE = {'Th-232': ('Th-232', 'pos'), 'Th-228': ('Th-228', 'pos'),
              'Ra-226': ('Ra-226', 'pos'),
              'U-238': ('U-238', 'pos'), 'U-238u': ('U-238', 'head'),
              'U-235': ('U-235', 'pos')}


def read_csv(path, encoding='utf-8-sig'):
    with open(path, encoding=encoding, newline='') as fh:
        return list(csv.DictReader(fh))


def manifest():
    return read_csv(os.path.join(CORPUS, 'manifest.csv'))


def detectors():
    return {r['det']: r for r in read_csv(os.path.join(CORPUS, 'detectors.csv'))}


def res_of(det_row):
    return [float(det_row['res_c0']), float(det_row['res_c1']), float(det_row['res_c2'])]


def fwhm_kev(res, e):
    return float(np.sqrt(max(res[0] + res[1] * e + res[2] * e * e, 1e-6)))


def reference_lines(chain, res, lo, hi, mode):
    """Знаменатель recall: сильные линии цепочки, слитые в пределах 0.6 FWHM."""
    rows = [r for r in chain_lines(CHAINS[chain])
            if lo <= r['energy'] <= hi and r['i_chain'] >= I_REF]
    if mode == 'head':
        rows = [r for r in rows if r['nucid'] in U238_HEAD]
    rows.sort(key=lambda r: r['energy'])
    merged = []
    for r in rows:
        if merged and abs(r['energy'] - merged[-1]['energy']) < 0.6 * fwhm_kev(res, r['energy']):
            if r['i_chain'] > merged[-1]['i_chain']:
                merged[-1] = r
            continue
        merged.append(r)
    return merged


# ---------------------------------------------------------------------------
# прогон
# ---------------------------------------------------------------------------
def build_sets(dets):
    """mkconfig.py в одной точке (k = 0.7, I_min = 1 %) на все группы корпуса."""
    cmd = [sys.executable, os.path.join(HERE, 'mkconfig.py'),
           '--kind=real,decoy', '--k=%.2f' % K, '--imin=%.2f' % IMIN,
           '--dets=' + ','.join(dets)]
    env = dict(os.environ, PYTHONIOENCODING='utf-8')
    subprocess.run(cmd, check=True, cwd=HERE, env=env,
                   stdout=subprocess.DEVNULL)


def prepare_workdir(det, keys):
    """Рабочий каталог группы: сборка приложения, конфиг корпуса и её спектры.

    NuclideDefinition.xml уже положил туда mkconfig.py — его не трогаем.
    """
    wd = os.path.join(HERE, 'wd_%s' % det)
    for name in os.listdir(BUILD):
        src = os.path.join(BUILD, name)
        if os.path.isfile(src):
            shutil.copyfile(src, os.path.join(wd, name))
    config = os.path.join(wd, 'config')
    for name in ('BecquerelMonitor.xml',):
        src = os.path.join(APPDATA_CONFIG, name)
        if os.path.isfile(src):
            shutil.copyfile(src, os.path.join(config, name))
    devices = os.path.join(config, 'device')
    if os.path.isdir(devices):
        shutil.rmtree(devices)
    shutil.copytree(os.path.join(CORPUS, 'devices'), devices)

    spectra = os.path.join(wd, 'spectra')
    if os.path.isdir(spectra):
        shutil.rmtree(spectra)
    os.makedirs(spectra)
    for key in keys:
        shutil.copyfile(os.path.join(CORPUS, 'spectra', key + '.xml'),
                        os.path.join(spectra, key + '.xml'))
    return wd


def points():
    """[(метка, гейт, доп. ключи)] — что именно гоняется."""
    out = []
    for item in VARIANTS:
        gate, extra = item[0], item[1]
        label = item[2] if len(item) > 2 else gate
        out.append((label, gate, extra))
    return out


def tag_of(det, label):
    safe = label.replace('+', '-').replace('/', '_')
    return '%s_%s' % (det, safe)


def set_names(det):
    """Восемь сетов группы: четыре цепочки, настоящие и обманки."""
    names = []
    for chain in CHAINS:
        names.append('%s|k%.2f|i%.2f' % (chain, K, IMIN))
        names.append('%s~decoy|k%.2f|i%.2f' % (chain, K, IMIN))
    return names


def run():
    rows = manifest()
    by_det = defaultdict(list)
    for r in rows:
        by_det[r['det']].append(r['key'])
    dets = sorted(by_det)

    print('сеты для %d групп...' % len(dets))
    build_sets(dets)

    os.makedirs(OUT, exist_ok=True)
    for det in dets:
        wd = prepare_workdir(det, by_det[det])
        exe = os.path.join(wd, 'LibraryFitLab.exe')
        sets = ','.join(set_names(det))
        for label, gate, extra in points():
            tag = tag_of(det, label)
            cmd = [exe, '--workdir=%s' % wd,
                   '--input=%s' % os.path.join(wd, 'spectra'),
                   '--sets=%s' % sets, '--no-set', '--snr=4', '--deconv=false',
                   '--gate=%s' % gate,
                   '--runs=%s' % os.path.join(OUT, tag + '_runs.csv'),
                   '--peaks=%s' % os.path.join(OUT, tag + '_peaks.csv')] + extra
            proc = subprocess.run(cmd, cwd=wd, capture_output=True, text=True)
            if proc.returncode != 0:
                print('  %-24s ОШИБКА %s' % (tag, (proc.stderr or '').strip()[:120]))
        print('  %-10s %d спектров x %d точек' % (det, len(by_det[det]), len(points())))


# ---------------------------------------------------------------------------
# разбор
# ---------------------------------------------------------------------------
def parse_set(name):
    """'Th-232~decoy|k0.70|i1.00' -> ('Th-232', 'decoy')"""
    if '|' not in name:
        return None
    head = name.split('|', 1)[0]
    if head.endswith('~decoy'):
        return head[:-len('~decoy')], 'decoy'
    return head, 'real'


def coverage_check(dets):
    """Сколько групп должно было посчитаться и сколько дало непустой CSV.

    Третья ловушка покрытия подряд, и первая — не в скрипте разбора, а в самом
    прогоне: группа CZT_TECD падала при десериализации, gate_study печатал
    «ОШИБКА», но прогоны запускались с подавленным выводом, и `--report`
    разбирает готовые CSV, об упавшей группе не зная. Расхождение и есть сигнал.
    """
    missing = []
    for det in dets:
        files = glob.glob(os.path.join(OUT, '%s_*_runs.csv' % det))
        if not files:
            missing.append((det, 'нет файлов'))
            continue
        if not any(sum(1 for _ in open(f, encoding='utf-8-sig')) > 1 for f in files):
            missing.append((det, 'файлы пусты'))
    print('групп в манифесте %d, с прогоном %d' % (len(dets), len(dets) - len(missing)))
    for det, why in missing:
        print('  ГРУППА НЕ ПОСЧИТАНА: %-12s %s' % (det, why))
    return missing


def report():
    import json

    rows = [r for r in read_csv(os.path.join(CORPUS, 'manifest.csv'))]
    coverage_check(sorted(set(r['det'] for r in rows)))
    print()
    sets_manifest = json.load(open(os.path.join(HERE, 'sets_manifest.json'), encoding='utf-8'))
    decoy_lines = {(m['det'], m['set_name']):
                   [l for l in m['lines'] if l.get('decoy')]
                   for m in sets_manifest if m['kind'] == 'decoy'}

    dets = detectors()
    truth = {}
    for r in manifest():
        present = {}
        for tag in (r['chains'] or '').split(';'):
            if tag in CHAIN_MODE:
                chain, mode = CHAIN_MODE[tag]
                present[chain] = mode
        truth[r['key']] = dict(det=r['det'], chains=present)

    labels_ref = [label for label, _, _ in points()]
    stat = defaultdict(lambda: defaultdict(lambda: dict(
        hit=0, ref=0, ph_acc=0, ph_shown=0, fired=0, runs=0, ms=[])))

    for det, det_row in dets.items():
        res = res_of(det_row)
        lo, hi = float(det_row['e_lo']), float(det_row['e_hi'])
        for label, gate, extra in points():
            tag = tag_of(det, label)
            runs_path = os.path.join(OUT, tag + '_runs.csv')
            peaks_path = os.path.join(OUT, tag + '_peaks.csv')
            if not os.path.exists(runs_path):
                continue
            runs = {int(r['run']): r for r in read_csv(runs_path, 'utf-8')}
            peaks = defaultdict(list)
            for p in read_csv(peaks_path, 'utf-8'):
                peaks[int(p['run'])].append(p)

            for run_id, meta in runs.items():
                if meta['set'] == '-':
                    # База: тот же спектр без сета вообще. Без неё цифры гейта
                    # не читаются — если recall упал до базы, библиотечный фит
                    # перестал добавлять хоть что-то, и весь смысл потерян.
                    if label != labels_ref[0]:
                        continue
                    key = meta['spectrum']
                    energies = np.array([float(p['energy']) for p in peaks.get(run_id, [])])
                    for chain, mode in truth.get(key, {}).get('chains', {}).items():
                        if mode not in ('pos', 'head'):
                            continue
                        b = stat['финдер'][det]
                        for line in reference_lines(chain, res, lo, hi, mode):
                            b['ref'] += 1
                            if energies.size and np.min(np.abs(energies - line['energy']))                                     <= 0.6 * fwhm_kev(res, line['energy']):
                                b['hit'] += 1
                    continue
                parsed = parse_set(meta['set'])
                if parsed is None:
                    continue
                chain, kind = parsed
                key = meta['spectrum']
                bucket = stat[label][det]
                bucket['runs'] += 1
                bucket['ms'].append(float(meta['ms']))

                if kind == 'real':
                    mode = truth.get(key, {}).get('chains', {}).get(chain)
                    if mode not in ('pos', 'head'):
                        continue
                    energies = np.array([float(p['energy']) for p in peaks.get(run_id, [])])
                    for line in reference_lines(chain, res, lo, hi, mode):
                        bucket['ref'] += 1
                        if energies.size and np.min(np.abs(energies - line['energy'])) \
                                <= 0.6 * fwhm_kev(res, line['energy']):
                            bucket['hit'] += 1
                else:
                    # фит не запускался — предъявленных линий не было
                    if int(meta['n_anchor']) == 0:
                        continue
                    bucket['fired'] += 1
                    shown = decoy_lines.get((det, meta['set']), [])
                    bucket['ph_shown'] += len(shown)
                    wanted = {round(l['e'], 2) for l in shown}
                    for p in peaks.get(run_id, []):
                        if p['origin'] != 'Library' or p['anchor'] == '1':
                            continue
                        e = float(p['nuclide_energy'] or p['energy'])
                        if round(e, 2) in wanted:
                            bucket['ph_acc'] += 1

    per_det = '--per-det' in sys.argv
    print('%-10s %-14s %8s %8s %8s %8s %8s' % (
        'детектор', 'гейт', 'recall', 'линий', 'фантомы', 'предъяв', 'мс'))
    print('-' * 70)
    labels = ['финдер'] + [label for label, _, _ in points()]
    totals = defaultdict(lambda: dict(hit=0, ref=0, ph_acc=0, ph_shown=0, ms=[]))
    for det in sorted(dets):
        for gate in labels:
            b = stat[gate].get(det)
            if not b or not b['ref']:
                continue
            recall = 100.0 * b['hit'] / b['ref']
            phantom = 100.0 * b['ph_acc'] / b['ph_shown'] if b['ph_shown'] else float('nan')
            if per_det:
                print('%-10s %-14s %7.1f%% %8d %7.1f%% %8d %8.0f' % (
                    det, gate, recall, b['ref'], phantom, b['ph_shown'],
                    np.median(b['ms']) if b['ms'] else 0))
            for field in ('hit', 'ref', 'ph_acc', 'ph_shown'):
                totals[gate][field] += b[field]
            totals[gate]['ms'].extend(b['ms'])
        if per_det:
            print()

    print('%-25s %8s %8s %8s %8s %8s' % ('ИТОГО', 'recall', 'линий', 'фантомы', 'предъяв', 'мс'))
    print('-' * 70)
    for gate in labels:
        t = totals[gate]
        if not t['ref']:
            continue
        phantom = ('%6.1f%%' % (100.0 * t['ph_acc'] / t['ph_shown'])) if t['ph_shown'] else '      -'
        print('%-25s %7.1f%% %8d %8s %8d %8.0f' % (
            gate, 100.0 * t['hit'] / t['ref'], t['ref'], phantom,
            t['ph_shown'], np.median(t['ms']) if t['ms'] else 0))


if __name__ == '__main__':
    if '--run' in sys.argv:
        run()
    if '--report' in sys.argv or '--run' not in sys.argv:
        report()
