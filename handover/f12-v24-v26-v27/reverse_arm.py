# -*- coding: utf-8 -*-
u"""`V26`, обратное плечо: сторож «поправка усиления ≤ 5 %» у `gain` кусает при
n ≤ 2 и НЕ кусает при n ≥ 3 — на копиях, двумя файлами `corpus_calib.py`
(HEAD и правленый) над ОДНИМИ парами.

Два опыта на уровне `choose`:

  A. «сторож при n = 2 и n = 3»: у спектров с одной опорой, где `gain` по ней
     отвергнут сторожем (drift > 5 %), к опоре добавляются 1 или 2 опоры,
     СОГЛАСНЫЕ с этой поправкой (положены калибровкой `gain` по одной линии,
     то есть с тем же дрейфом > 5 %). При n = 2 HEAD впускает `gain`, правленый
     — нет; при n = 3 оба впускают одинаково: (метка, коэффициенты) совпадают.
  B. «всё прочее как прежде»: у всех спектров с опорами к их парам добавляются
     две нулевые опоры (n ≥ 3), и `choose` HEAD против правленого обязан дать
     побитово те же метку и коэффициенты.

Запуск: python reverse_arm.py <calib_head.py> <calib_v26.py> <raw> <calib_json> <out.csv>
"""
import csv
import importlib.util
import io
import json
import os
import sys

import numpy as np

SCRIPTS = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..',
                       'tools', 'CORPUS', 'scripts')
sys.path.insert(0, os.path.abspath(SCRIPTS))


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    sys.modules[name] = mod
    spec.loader.exec_module(mod)
    return mod


def main(argv):
    head_path, v26_path, raw, calib_json, out_csv = argv
    head = load('calib_head_mod', head_path)
    v26 = load('calib_v26_mod', v26_path)
    import calib_null_check as cnc
    from spectrum import Spectrum
    J = json.load(io.open(calib_json, encoding='utf-8'))
    rows = []
    a_diff2, a_same3, a_n = 0, 0, 0
    b_same, b_n = 0, 0
    for key in sorted(J):
        rec = J[key]
        if rec['n'] < 1:
            continue
        sp = Spectrum(os.path.join(raw, key + '.xml'))
        nmax = sp.n
        stored_h, stored_v = head.Ecal(sp.ecal, nmax), v26.Ecal(sp.ecal, nmax)
        res_a = rec['res_a']
        pairs = [dict(ch=p['ch'], e_ref=p['e'], sig=p['sig'], purity=1.0, fwhm=1.0,
                      area=0.0, sig_fit=0.0) for p in rec['pairs']]
        cal1 = head.Ecal(rec['coef'], nmax)

        def same(res_h, res_v):
            return res_h[0] == res_v[0] and np.allclose(res_h[1].coef, res_v[1].coef,
                                                        rtol=0, atol=1e-12)

        # B: две нулевые опоры от ответа стадии 1 -> n >= 3
        extra = []
        for place, e_star in cnc.placements(cal1, pairs, nmax)[:2]:
            extra.append(cnc.synthetic_pair(cal1, e_star, pairs, res_a))
        while len(pairs) + len(extra) < 3 and extra:
            e = dict(extra[-1])
            e['ch'] += 3.0
            e['e_ref'] = float(cal1.energy(e['ch']))
            extra.append(e)
        if len(pairs) + len(extra) >= 3:
            t3 = pairs + extra
            rh = head.choose(stored_h, t3, res_a, nmax)
            rv = v26.choose(stored_v, t3, res_a, nmax)
            b_n += 1
            b_same += same(rh, rv)
            rows.append(dict(spectrum=key, arm='B_null_n>=3', n=len(t3), tag_head=rh[0],
                             tag_v26=rv[0], same=same(rh, rv), drift_pct=''))

        # A: одна опора, gain по ней отвергнут сторожем
        if rec['n'] != 1:
            continue
        gain = head.affine_of(stored_h, pairs, nmax, scale_only=True)
        if gain is None:
            continue
        drift = head.gain_drift(gain, stored_h, nmax)
        if drift <= 0.05:
            continue
        a_n += 1
        cons = []
        for place, e_star in cnc.placements(gain, pairs, nmax):
            cons.append(cnc.synthetic_pair(gain, e_star, pairs, res_a))
        if len(cons) < 2:
            continue
        for n_tot, tab in ((2, pairs + cons[:1]), (3, pairs + cons[:2])):
            rh = head.choose(stored_h, tab, res_a, nmax)
            rv = v26.choose(stored_v, tab, res_a, nmax)
            s = same(rh, rv)
            if n_tot == 2:
                a_diff2 += not s
            else:
                a_same3 += s
            rows.append(dict(spectrum=key, arm='A_gain_consistent', n=n_tot, tag_head=rh[0],
                             tag_v26=rv[0], same=s, drift_pct=round(100 * drift, 2)))
    with io.open(out_csv, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=['spectrum', 'arm', 'n', 'tag_head', 'tag_v26',
                                          'same', 'drift_pct'])
        w.writeheader()
        w.writerows(rows)
    print(u'A: спектров с одной опорой и gain, отвергнутым сторожем (drift > 5 %%): %d; '
          u'при n = 2 ответы РАЗОШЛИСЬ у %d; при n = 3 СОВПАЛИ у %d' % (a_n, a_diff2, a_same3))
    for r in rows:
        if r['arm'].startswith('A'):
            print(u'   %-22s n=%d drift %s %%: HEAD %-12s правленый %-12s %s'
                  % (r['spectrum'], r['n'], r['drift_pct'], r['tag_head'], r['tag_v26'],
                     u'совпало' if r['same'] else u'РАЗОШЛОСЬ'))
    print(u'B: две нулевые опоры (n >= 3) у %d спектров: HEAD и правленый совпали у %d'
          % (b_n, b_same))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
