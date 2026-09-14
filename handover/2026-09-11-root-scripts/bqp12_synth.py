# -*- coding: utf-8 -*-
u"""bqp12_synth.py — истина Asimov-теста (П12, 11.09.2026).

Берёт ПОДОГНАННУЮ НОВЫМИ матрицами (плечо Б, склад mini_a16) модель реального спектра —
все колонки плана (`_cols.csv`) с амплитудами (`_amps.csv`) — и пишет спектр БЕЗ шума:
передний план = round(Σ amp·col + фон_вычтенный), фон в файле — как был. Шкала, ПШПВ,
живое время — те же, что у спектра. Дробные отсчёты читатель приложения не принимает
(`int[] Spectrum`), поэтому округление, и его цена печатается числом.

Копия оснастки П31 `handover/p31-inflate/synth.py` (тот файл не правится): режим без
розыгрыша, μ как есть.

  python C:\\Users\\moroz\\bqp12_synth.py --base=G1S16_Cs137_P5 [--arm=b]
"""
import argparse, csv, io, os, sys
import numpy as np
import xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding='utf-8')
sys.path.insert(0, r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\p28-z')
import thin as T  # noqa: E402

OUT = r'C:\Users\moroz\bqp12_out'
CORPUS = r'C:\Users\moroz\bqp12_corpus'
BANDS = [(-1e9, 45.0), (45.0, 100.0), (100.0, 300.0), (300.0, 1e9)]
BN = ['<45', '45-100', '100-300', '>300']


def points(es):
    return es.find('Spectrum').findall('DataPoint')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--base', required=True)
    ap.add_argument('--arm', default='b')
    a = ap.parse_args()
    key = a.base
    d = os.path.join(OUT, a.arm + '_dump')
    amps = list(csv.DictReader(io.open(os.path.join(d, key + '_amps.csv'), encoding='utf-8-sig')))
    rows = list(csv.DictReader(io.open(os.path.join(d, key + '_cols.csv'), encoding='utf-8-sig')))
    chi = list(csv.DictReader(io.open(os.path.join(d, key + '_chi.csv'), encoding='utf-8-sig')))
    n = len(rows)
    kev = np.array([float(r['keV']) for r in rows])
    lo, hi = int(amps[0]['first']), int(amps[0]['last'])
    model = np.zeros(n)
    for am in amps:
        x = float(am['amp'])
        if x <= 0.0:
            continue
        model += x * np.array([float(r[am['name']]) for r in rows])
    model_app = np.array([float(r['model']) for r in chi])
    bg = np.array([float(r['bg']) for r in chi])
    raw = np.array([float(r['raw']) for r in chi])
    dmax = np.abs(model - model_app)[lo:hi + 1].max()
    print(u'%s: колонок %d (активных %d), окно %d..%d; Σ amp·col против model приложения: max|Δ| = %.4g (дамп F3)'
          % (key, len(amps), sum(1 for am in amps if am['active'] == '1'), lo, hi, dmax))
    assert dmax < 2e-3, dmax
    mu = np.maximum(model + bg, 0.0)
    drawn = np.rint(mu)
    w = 1.0 / np.maximum(mu, 1.0)
    win = np.zeros(n, bool); win[lo:hi + 1] = True
    cost = ((drawn - mu) ** 2 * w)[win].sum()
    parts = []
    for (e0, e1), bn in zip(BANDS, BN):
        m = win & (kev >= e0) & (kev < e1)
        parts.append('%s %.3f' % (bn, ((drawn - mu) ** 2 * w)[m].sum()))
    print(u'  истина: Σμ = %.1f (сырых было %.0f), фон Σ = %.1f; цена округления Σw·(round−μ)² = %.3f по окну (%s)'
          % (mu[win].sum(), raw[win].sum(), bg[win].sum(), cost, ', '.join(parts)))

    src = os.path.join(CORPUS, 'spectra', key + '.xml')
    tree = ET.parse(src)
    root = tree.getroot()
    f_es = root.find('.//EnergySpectrum')
    pts = points(f_es)
    assert len(pts) == n, (len(pts), n)
    for p, v in zip(pts, drawn):
        p.text = str(int(v))
    total = int(drawn.sum())
    T.set_num(f_es, 'ValidPulseCount', total, '%d')
    tp = f_es.find('TotalPulseCount')
    if tp is not None:
        tp.text = str(total)
    newkey = key + '_asimov'
    dst = os.path.join(CORPUS, 'spectra', newkey + '.xml')
    tree.write(dst, encoding='utf-8', xml_declaration=True)

    man_head, man_rows = T.read_table(os.path.join(CORPUS, 'manifest.csv'))
    par_head, par_rows = T.read_table(os.path.join(CORPUS, 'parts.csv'))
    mat_head, mat_rows = T.read_table(os.path.join(CORPUS, 'materials.csv'))

    def find(rows_, k):
        for r in rows_:
            if T.csv_cell(r, 0) == k:
                return r
        raise SystemExit('нет строки ' + k)

    def has(rows_, k):
        return any(T.csv_cell(r, 0) == k for r in rows_)

    if not has(man_rows, newkey):
        man, par, mat = find(man_rows, key), find(par_rows, key), find(mat_rows, key)
        cells = T.split_row(man); cells[0] = newkey; cells[4] = str(total)
        T.append_rows(os.path.join(CORPUS, 'manifest.csv'), [','.join(cells)])
        pc = T.split_row(par); pc[0] = newkey
        T.append_rows(os.path.join(CORPUS, 'parts.csv'), [','.join(pc)])
        mc = T.split_row(mat); mc[0] = newkey
        T.append_rows(os.path.join(CORPUS, 'materials.csv'), [','.join(mc)])
    print(u'  записан %s: отсчётов %d, sha256 %s' % (dst, total, T.sha(dst)))

    td = os.path.join(OUT, 'truth'); os.makedirs(td, exist_ok=True)
    with io.open(os.path.join(td, key + '_truth.csv'), 'w', encoding='utf-8', newline='') as f:
        f.write('ch,keV,mu_model,bg,mu_fore,rounded,w,in_window\n')
        for i in range(n):
            f.write('%d,%.3f,%r,%r,%r,%d,%r,%d\n' % (i, kev[i], float(model[i]), float(bg[i]), float(mu[i]), int(drawn[i]), float(w[i]), int(win[i])))
    with io.open(os.path.join(td, key + '_truth_amps.csv'), 'w', encoding='utf-8', newline='') as f:
        f.write('index,name,kind,amp\n')
        for am in amps:
            f.write('%s,%s,%s,%s\n' % (am['index'], am['name'], am['kind'], am['amp']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
