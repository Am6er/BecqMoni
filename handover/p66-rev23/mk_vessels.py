# -*- coding: utf-8 -*-
r"""П66 (AMBER29, решение Amber 14.09.2026 «Сосуды сейчас, отдельно»): 17 сцен `G1S_mar1l_*`
корпуса — сосуд ПО ЧЕРТЕЖУ ОМАСН вместо восстановленного «из объёма», плюс 18-я сцена угля
с радоном (решение «3 ранних + 1 равновесный») тем же сосудом.

Чертёж ОМАСН (`C:\Users\moroz\YandexDisk\Спектры\G1S\ОМАСН.pdf`, разбор П64 §0.3): корпус
Ø154 (крышка Ø156.5 — не моделируется), высота 112, колодец Ø97 глубиной 65. Семантика
модели (`EfficiencySimulator`, ветка Marinelli; `CorpusGeomProbe.SampleVolumeMm3`):
`SM_BeakerDiameter` — НАРУЖНЫЙ Ø, `SM_BeakerHoleDiameter` — ВНУТРЕННИЙ Ø колодца (стенки
снаружи от него), `SM_BeakerHoleHeight` — глубина колодца, `SM_SourceHeight` — полная высота
пробы; стенки 0.2 см — как принял П64 (у корпусных сцен те же). Высота засыпки — из
паспортного объёма пробы (`data/lsrm_spectrum_geometry.csv`, `объём_мл`; у всех 17 — 1000 мл):
h = (V + π·r_in²·h_кол)/(π·r_out²), r_in = 48.5 + 2 = 50.5 мм, r_out = 77 − 2 = 75 мм →
86.06 мм (П64: 86.1). Плотность набивки не трогается (масса/объём те же).

Меняются РОВНО пять строк каждой сцены (проверяется диффом): `SM_BeakerDiameter`,
`SM_BeakerHeight`, `SM_BeakerHoleDiameter`, `SM_BeakerHoleHeight`, `SM_SourceHeight`.
Корпусный сосуд был: Ø135.196 × 104, колодец Ø76 × 70, засыпка 100 мм (объём ровно 1000 см³).

Сцена угля `G1S_mar1l_coal_046_p24` — из `G1S_mar1l_oisn06_057_p24` (после смены сосуда):
вещество пробы в обоих блоках (SC_/SM_) — углерод Z = 6, ρ 0.461 (461 г / 1000 мл, П64), имя
«Activated charcoal» (в `matdb` угля нет, состав задан явно); `DS_Fwhm662` корпусный 6.44 %.

    python handover/p66-rev23/mk_vessels.py [--apply]

Без `--apply` — только печать плана и проверка объёмов. С `--apply`:
  * `handover/p66-rev23/scenes_before/<ключ>.in` — 17 сцен ДО (копия байт в байт);
  * `tools/CORPUS/corpus/geometries/<ключ>.in` — 17 сцен ПОСЛЕ + новая сцена угля;
  * `handover/p66-rev23/scenes_after/` — копии всех 18;
  * `D:\BqMoni_Claude\p66\store\` — те же 18 `.in` + `index.csv` только этих сцен
    (склад полосы для `CorpusMatrixProbe --dir=` / `CorpusEffProbe --dir=`);
  * `tools/CORPUS/corpus/geometries/index.csv` — +4 строки сцены угля.
Файлы cp1251 + CRLF, как пишет `CorpusGeomProbe`. Разделитель дробной части — точка.
"""
import csv
import io
import math
import os
import re
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
GEOM = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'geometries')
DATA = os.path.join(REPO, 'tools', 'CORPUS', 'data', 'lsrm_spectrum_geometry.csv')
STORE = r'D:\BqMoni_Claude\p66\store'
BEFORE = os.path.join(HERE, 'scenes_before')
AFTER = os.path.join(HERE, 'scenes_after')

# чертёж ОМАСН, мм
D_OUT, H_BEAKER, D_HOLE, H_HOLE, WALL = 154.0, 112.0, 97.0, 65.0, 2.0

COAL_KEY = 'G1S_mar1l_coal_046_p24'
COAL_TEMPLATE = 'G1S_mar1l_oisn06_057_p24'
COAL_RHO = '0.461'
COAL_SPECTRA = ['G1S24_Rn222Coal_Mar_20m', 'G1S24_Rn222Coal_Mar_2h',
                'G1S24_Rn222Coal_Mar_3h', 'G1S24_Rn222Coal_Mar_eq01']
COAL_VESSEL = u'Маринелли, набивка активированный уголь 0.461 г/см³, вплотную'

KEYS = ['SM_BeakerDiameter', 'SM_BeakerHeight', 'SM_BeakerHoleDiameter',
        'SM_BeakerHoleHeight', 'SM_SourceHeight']


def fill_height_mm(volume_ml):
    r_in = 0.5 * D_HOLE + WALL
    r_out = 0.5 * D_OUT - WALL
    return (volume_ml * 1000.0 + math.pi * r_in * r_in * H_HOLE) / (math.pi * r_out * r_out)


def volume_cm3(d_out, d_hole, h_hole, h_src, wall=WALL):
    # та же формула, что CorpusGeomProbe.SampleVolumeMm3 (ветка Marinelli)
    r_hole = 0.5 * d_hole + wall
    r_out = max(0.5 * d_out, r_hole + 0.1)
    r_src = max(r_hole, r_out - wall)
    cap = max(0.0, h_src - h_hole)
    return (math.pi * (r_src * r_src - r_hole * r_hole) * h_src + math.pi * r_hole * r_hole * cap) / 1000.0


def set_key(text, key, value):
    pat = re.compile(r'^(' + re.escape(key) + r'\s*=\s*)(.*?)(\r?\n)', re.M)
    m = pat.search(text)
    if not m:
        raise SystemExit('нет ключа ' + key)
    return text[:m.start(2)] + value + text[m.end(2):]


def get_key(text, key):
    m = re.search(r'^' + re.escape(key) + r'\s*=\s*(.*?)\r?$', text, re.M)
    return m.group(1) if m else None


def replace_source(text, prefix, rho):
    start = text.index('%s_nSourceElements' % prefix)
    end = text.index('// Empty space', start)
    block = (
        "{p}_nSourceElements = 1\r\n"
        "{p}_RoSource = {rho}\r\n"
        "{p}_ZSource[0] = 6\r\n"
        "{p}_FractionsSource[0] = 1\r\n"
        "{p}_FractionTypeSource = MASS\r\n"
        "M_{p}_Source.MName = Activated charcoal\r\n"
        "M_{p}_Source.Nmaterials = 1\r\n"
        "M_{p}_Source.Name[0] = Activated charcoal                       \r\n"
        "M_{p}_Source.MatRelWeight[0] = 1\r\n"
        "\r\n"
        "\r\n"
    ).format(p=prefix, rho=rho)
    return text[:start] + block + text[end:]


def fmt(x):
    s = ('%.6f' % x).rstrip('0').rstrip('.')
    return s


def main():
    apply = '--apply' in sys.argv
    with io.open(DATA, encoding='utf-8-sig', newline='') as fh:
        geo = {r[u'спектр']: r for r in csv.DictReader(fh)}
    with io.open(os.path.join(GEOM, 'index.csv'), encoding='utf-8-sig', newline='') as fh:
        index_rows = list(csv.DictReader(fh))
    scenes = sorted(set(r['geometry'] for r in index_rows if r['geometry'].startswith('G1S_mar1l_')))
    if len(scenes) != 17:
        raise SystemExit('ждали 17 сцен G1S_mar1l_*, нашли %d' % len(scenes))
    spectra_of = {}
    for r in index_rows:
        spectra_of.setdefault(r['geometry'], []).append(r['spectrum'])

    plan = []
    for key in scenes:
        raw = io.open(os.path.join(GEOM, key + '.in'), 'rb').read()
        text = raw.decode('cp1251')
        if raw.count(b'\r\n') != raw.count(b'\n'):
            raise SystemExit('%s: не CRLF целиком' % key)
        specs = spectra_of[key]
        vols = set(float(geo[s][u'объём_мл']) for s in specs)
        if len(vols) != 1:
            raise SystemExit('%s: объёмы спектров сцены расходятся: %s' % (key, vols))
        vol = vols.pop()
        h = fill_height_mm(vol)
        before = dict((k, get_key(text, k)) for k in KEYS)
        new = text
        new = set_key(new, 'SM_BeakerDiameter', '%s cm' % fmt(D_OUT / 10.0))
        new = set_key(new, 'SM_BeakerHeight', '%s cm' % fmt(H_BEAKER / 10.0))
        new = set_key(new, 'SM_BeakerHoleDiameter', '%s cm' % fmt(D_HOLE / 10.0))
        new = set_key(new, 'SM_BeakerHoleHeight', '%s cm' % fmt(H_HOLE / 10.0))
        new = set_key(new, 'SM_SourceHeight', '%.4f cm' % (h / 10.0))
        # ровно пять строк различаются
        a, b = text.split('\r\n'), new.split('\r\n')
        if len(a) != len(b):
            raise SystemExit('%s: число строк изменилось' % key)
        diff = [i for i in range(len(a)) if a[i] != b[i]]
        if len(diff) != 5:
            raise SystemExit('%s: изменилось строк %d, ждали 5' % (key, len(diff)))
        v_before = volume_cm3(float(before['SM_BeakerDiameter'].split()[0]) * 10, float(before['SM_BeakerHoleDiameter'].split()[0]) * 10,
                              float(before['SM_BeakerHoleHeight'].split()[0]) * 10, float(before['SM_SourceHeight'].split()[0]) * 10)
        v_after = volume_cm3(D_OUT, D_HOLE, H_HOLE, h)
        if abs(v_after - vol) > 0.05:
            raise SystemExit('%s: объём после %.2f != паспорт %.1f' % (key, v_after, vol))
        plan.append((key, specs, vol, h, before, v_before, v_after, raw, new))
        print('%-30s %-22s V=%.0f мл; было Ø%s h%s кол.%s×%s зас.%s (%.1f см³) -> Ø15.4 h11.2 кол.9.7×6.5 зас.%.4f см (%.1f см³)'
              % (key, ','.join(specs), vol, before['SM_BeakerDiameter'].split()[0], before['SM_BeakerHeight'].split()[0],
                 before['SM_BeakerHoleDiameter'].split()[0], before['SM_BeakerHoleHeight'].split()[0],
                 before['SM_SourceHeight'].split()[0], v_before, h / 10.0, v_after))

    # сцена угля — из шаблона ПОСЛЕ смены сосуда
    tpl = [p for p in plan if p[0] == COAL_TEMPLATE][0][8]
    assert 'SourceType = MARINELLI' in tpl and 'SM_RoSource = 0.57' in tpl
    coal = replace_source(tpl, 'SC', COAL_RHO)
    coal = replace_source(coal, 'SM', COAL_RHO)
    assert coal.count('Activated charcoal') == 4 and get_key(coal, 'SM_RoSource') == COAL_RHO
    print('%-30s %s: уголь C ρ %s, сосуд ОМАСН, засыпка %.4f см' % (COAL_KEY, ','.join(COAL_SPECTRA), COAL_RHO, fill_height_mm(1000.0) / 10.0))

    if not apply:
        print('план напечатан; --apply — записать')
        return 0

    for d in (BEFORE, AFTER, STORE):
        os.makedirs(d, exist_ok=True)
    for key, specs, vol, h, before, v_b, v_a, raw, new in plan:
        with open(os.path.join(BEFORE, key + '.in'), 'wb') as fh:
            fh.write(raw)
        out = new.encode('cp1251')
        for d in (GEOM, AFTER, STORE):
            with open(os.path.join(d, key + '.in'), 'wb') as fh:
                fh.write(out)
    out = coal.encode('cp1251')
    if os.path.exists(os.path.join(GEOM, COAL_KEY + '.in')):
        raise SystemExit('сцена угля уже есть в корпусе: ' + COAL_KEY)
    for d in (GEOM, AFTER, STORE):
        with open(os.path.join(d, COAL_KEY + '.in'), 'wb') as fh:
            fh.write(out)
    # опись корпуса: +4 строки угля (в конце, как дописывает CorpusGeomProbe новые сцены)
    idx_path = os.path.join(GEOM, 'index.csv')
    raw_idx = open(idx_path, 'rb').read()
    nl = b'\r\n' if raw_idx.count(b'\r\n') == raw_idx.count(b'\n') and raw_idx.count(b'\n') else b'\n'
    text_idx = raw_idx.decode('utf-8-sig')
    if COAL_KEY in text_idx:
        raise SystemExit('опись уже содержит ' + COAL_KEY)
    add = u''.join(u'%s,%s,Gamma-1S UDS-GC 63x63,"%s"%s' % (COAL_KEY, s, COAL_VESSEL, nl.decode()) for s in COAL_SPECTRA)
    if not text_idx.endswith(nl.decode()):
        text_idx += nl.decode()
    bom = raw_idx.startswith(b'\xef\xbb\xbf')
    with open(idx_path, 'wb') as fh:
        fh.write((b'\xef\xbb\xbf' if bom else b'') + (text_idx + add).encode('utf-8'))
    # опись склада полосы: только 18 сцен
    rows = [u'geometry,spectrum,preset,vessel']
    for r in index_rows:
        if r['geometry'] in scenes:
            rows.append(u'%s,%s,%s,"%s"' % (r['geometry'], r['spectrum'], r['preset'], r['vessel']))
    for s in COAL_SPECTRA:
        rows.append(u'%s,%s,Gamma-1S UDS-GC 63x63,"%s"' % (COAL_KEY, s, COAL_VESSEL))
    with io.open(os.path.join(STORE, 'index.csv'), 'w', encoding='utf-8', newline='') as fh:
        fh.write(u'\n'.join(rows) + u'\n')
    print('записано: %d сцен ДО -> %s; %d сцен ПОСЛЕ -> %s, %s, %s; index.csv корпуса +%d строк; index.csv склада %d строк'
          % (len(plan), BEFORE, len(plan) + 1, GEOM, AFTER, STORE, len(COAL_SPECTRA), len(rows) - 1))
    return 0


if __name__ == '__main__':
    sys.exit(main())
