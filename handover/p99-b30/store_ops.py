# -*- coding: utf-8 -*-
r"""П99 (18.09.2026, B30) — склад матриц: снимок живого, склад полосы, перенос по sha256.

  python store_ops.py backup         -- живой склад (главное дерево) -> D:\BqMoni_Claude\p99\store_backup\ ЦЕЛИКОМ
                                        (46 .in, 46 .rmx, response\ 46, index.csv) + sha256.txt; каждая копия сверена
  python store_ops.py mkstore        -- склад полосы D:\BqMoni_Claude\p99\store\: 49 .in и index.csv ИЗ WORKTREE
                                        (правленые сцены и три новые), .rmx НЕ копируются — всё, что там лежит, посчитано полосой
  python store_ops.py verify_before  -- живой склад = снимок по sha256 (46/46/46 + index.csv) — живой склад не трогался
  python store_ops.py swap           -- 5 сцен: store\<key>.rmx -> живой <key>.rmx; store\response\<guid>.rmx -> живой response\<guid>.rmx
                                        (guid = guid узла <Efficiency> спектров worktree; для двух G1S — тот же, что был);
                                        три новых .in + два правленых .in + index.csv -> живой каталог сцен (git-файлы)
  python store_ops.py verify_after   -- живой склад: 49 .in = worktree; 5 .rmx = складу полосы; 44 .rmx = снимку; response 49,
                                        множество sha256 response = множеству по ключам; клеймо phys=19 у всех 49

Пишет только в D:\BqMoni_Claude\p99\ и (swap) в живой склад. Разделитель дробной части — точка.
"""
import csv
import hashlib
import io
import os
import re
import shutil
import sys
import xml.etree.ElementTree as ET

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
LIVE = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'geometries')
WT = r'D:\BqMoni_Claude\p99\wt'
WT_GEOM = os.path.join(WT, 'tools', 'CORPUS', 'corpus', 'geometries')
WT_SPECTRA = os.path.join(WT, 'tools', 'CORPUS', 'corpus', 'spectra')
STORE = r'D:\BqMoni_Claude\p99\store'
BACKUP = r'D:\BqMoni_Claude\p99\store_backup'
ART = r'D:\BqMoni_Claude\p99\art'
KEYS = ['G1S_point5', 'G1S_point25', 'RC103_point50', 'ASN16_point0_house', 'ASN16_point10_house']
NEW_IN = ['RC103_point50', 'ASN16_point0_house', 'ASN16_point10_house']
CHANGED_IN = ['G1S_point5', 'G1S_point25']
N_OLD = 46
N_NEW = 49


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def files(d, ext):
    return sorted(f for f in os.listdir(d) if f.lower().endswith(ext)) if os.path.isdir(d) else []


def listing(d, ext):
    return dict((f, sha(os.path.join(d, f))) for f in files(d, ext))


def write_list(rows, path):
    with io.open(path, 'w', encoding='utf-8', newline='') as fh:
        for f in sorted(rows):
            fh.write(u'%s  %s\n' % (rows[f], f))


def stamp_of(rmx):
    u"""Клеймо `phys=N;<sha>` из заголовка .rmx (текст в файле)."""
    with open(rmx, 'rb') as fh:
        head = fh.read(4096)
    m = re.search(rb'phys=(\d+);([0-9a-f]{64})', head)
    return m.group(0).decode('ascii') if m else None


def guid_of(spectrum_key):
    p = os.path.join(WT_SPECTRA, spectrum_key + '.xml')
    rd = ET.parse(p).getroot().find('ResultDataList/ResultData')
    eff = rd.find('Efficiency') if rd is not None else None
    return eff.findtext('Guid') if eff is not None else None


def key_spectra(index_csv):
    out = {}
    with io.open(index_csv, encoding='utf-8-sig', newline='') as fh:
        for row in csv.DictReader(fh):
            out.setdefault(row['geometry'], []).append(row['spectrum'])
    return out


def backup():
    if os.path.isdir(BACKUP) and os.listdir(BACKUP):
        print(u'⛔ снимок уже есть: %s — не перезаписываю' % BACKUP)
        return 1
    os.makedirs(os.path.join(BACKUP, 'response'), exist_ok=True)
    os.makedirs(ART, exist_ok=True)
    rows = {}
    counts = {}
    total = 0
    for sub in ('', 'response'):
        src_dir = os.path.join(LIVE, sub)
        for f in sorted(os.listdir(src_dir)):
            p = os.path.join(src_dir, f)
            if not os.path.isfile(p):
                continue
            ext = os.path.splitext(f)[1].lower()
            if sub == '' and ext not in ('.in', '.rmx', '.csv'):
                continue
            if sub == 'response' and ext != '.rmx':
                continue
            dst = os.path.join(BACKUP, sub, f)
            shutil.copy2(p, dst)
            s1, s2 = sha(p), sha(dst)
            if s1 != s2:
                print(u'⛔ копия не совпала: %s' % f)
                return 1
            rel = (sub + '/' if sub else '') + f
            rows[rel] = s1
            counts[(sub, ext)] = counts.get((sub, ext), 0) + 1
            total += os.path.getsize(p)
    write_list(rows, os.path.join(BACKUP, 'sha256.txt'))
    write_list(rows, os.path.join(ART, 'store_backup_sha256.txt'))
    print(u'снимок: %d файлов, %d байт -> %s' % (len(rows), total, BACKUP))
    for k in sorted(counts):
        print(u'  %-10s %-5s %d' % (k[0] or u'(корень)', k[1], counts[k]))
    ok = counts.get(('', '.in'), 0) == N_OLD and counts.get(('', '.rmx'), 0) == N_OLD \
        and counts.get(('response', '.rmx'), 0) == N_OLD and counts.get(('', '.csv'), 0) == 1
    print(u'СНИМОК СНЯТ' if ok else u'⛔ СОСТАВ СНИМКА НЕ %d/%d/%d + index.csv' % (N_OLD, N_OLD, N_OLD))
    return 0 if ok else 1


def mkstore():
    if os.path.isdir(STORE) and files(STORE, '.rmx'):
        print(u'⛔ в складе полосы уже есть .rmx — не перезаписываю')
        return 1
    os.makedirs(STORE, exist_ok=True)
    n = 0
    for f in files(WT_GEOM, '.in'):
        shutil.copy2(os.path.join(WT_GEOM, f), os.path.join(STORE, f))
        n += 1
    shutil.copy2(os.path.join(WT_GEOM, 'index.csv'), os.path.join(STORE, 'index.csv'))
    print(u'склад полосы: %d .in + index.csv из worktree -> %s (.rmx не копировались)' % (n, STORE))
    return 0 if n == N_NEW else 1


def verify_before():
    bad = 0
    for ext, sub in (('.rmx', ''), ('.in', ''), ('.rmx', 'response')):
        live = listing(os.path.join(LIVE, sub), ext)
        back = listing(os.path.join(BACKUP, sub), ext)
        diff = sorted(set(live) ^ set(back)) + sorted(f for f in live if f in back and live[f] != back[f])
        print(u'%-9s %-5s живой %d, снимок %d, расхождений %d' % (sub or u'(корень)', ext, len(live), len(back), len(diff)))
        for f in diff:
            print(u'   %s' % f)
        bad += len(diff)
    if sha(os.path.join(LIVE, 'index.csv')) != sha(os.path.join(BACKUP, 'index.csv')):
        print(u'   index.csv РАЗОШЁЛСЯ со снимком'); bad += 1
    write_list(listing(LIVE, '.rmx'), os.path.join(ART, 'live_store_before_sha256.txt'))
    write_list(listing(os.path.join(LIVE, 'response'), '.rmx'), os.path.join(ART, 'live_response_before_sha256.txt'))
    print(u'ЖИВОЙ СКЛАД = СНИМОК' if bad == 0 else u'⛔ ЖИВОЙ СКЛАД НЕ РАВЕН СНИМКУ: %d' % bad)
    return 0 if bad == 0 else 1


def swap():
    spectra_by_key = key_spectra(os.path.join(STORE, 'index.csv'))
    moved = 0
    for key in KEYS:
        src = os.path.join(STORE, key + '.rmx')
        if not os.path.isfile(src):
            print(u'⛔ нет %s' % src); return 1
        st = stamp_of(src)
        if not st or not st.startswith('phys=19;'):
            print(u'⛔ %s: клеймо %s — не физика 19' % (key, st)); return 1
        guids = set(guid_of(s) for s in spectra_by_key[key])
        if len(guids) != 1 or None in guids:
            print(u'⛔ %s: guid узлов спектров не один: %s' % (key, guids)); return 1
        guid = guids.pop()
        src_r = os.path.join(STORE, 'response', guid + '.rmx')
        if not os.path.isfile(src_r) or sha(src_r) != sha(src):
            print(u'⛔ %s: response\\%s.rmx нет или не равен %s.rmx' % (key, guid, key)); return 1
        dst = os.path.join(LIVE, key + '.rmx')
        dst_r = os.path.join(LIVE, 'response', guid + '.rmx')
        was = sha(dst) if os.path.isfile(dst) else None
        was_r = sha(dst_r) if os.path.isfile(dst_r) else None
        shutil.copy2(src, dst)
        shutil.copy2(src_r, dst_r)
        if sha(dst) != sha(src) or sha(dst_r) != sha(src):
            print(u'⛔ %s: копия не сошлась' % key); return 1
        print(u'%-22s %s  guid %s  %s -> %s (response %s)' % (
            key, st[:24], guid, (was or u'НОВАЯ')[:8], sha(dst)[:8], (was_r or u'НОВАЯ')[:8]))
        moved += 1
    # сцены и опись — git-файлы живого каталога
    for key in NEW_IN + CHANGED_IN:
        shutil.copy2(os.path.join(WT_GEOM, key + '.in'), os.path.join(LIVE, key + '.in'))
    shutil.copy2(os.path.join(WT_GEOM, 'index.csv'), os.path.join(LIVE, 'index.csv'))
    print(u'перенесено матриц: %d (по ключу и по guid), сцен .in: %d, опись index.csv' % (moved, len(NEW_IN + CHANGED_IN)))
    return 0 if moved == len(KEYS) else 1


def verify_after():
    bad = 0
    live_in = listing(LIVE, '.in'); wt_in = listing(WT_GEOM, '.in')
    d = sorted(set(live_in) ^ set(wt_in)) + sorted(f for f in live_in if f in wt_in and live_in[f] != wt_in[f])
    print(u'.in: живой %d, worktree %d, расхождений %d %s' % (len(live_in), len(wt_in), len(d), d))
    bad += len(d)
    if sha(os.path.join(LIVE, 'index.csv')) != sha(os.path.join(WT_GEOM, 'index.csv')):
        print(u'   index.csv живой != worktree'); bad += 1
    live_rmx = listing(LIVE, '.rmx'); store_rmx = listing(STORE, '.rmx'); back_rmx = listing(BACKUP, '.rmx')
    for key in KEYS:
        f = key + '.rmx'
        if live_rmx.get(f) != store_rmx.get(f):
            print(u'   %s: живой != склад полосы' % f); bad += 1
    untouched = [f for f in live_rmx if f[:-4] not in KEYS]
    for f in untouched:
        if live_rmx[f] != back_rmx.get(f):
            print(u'   %s: нетронутая, а != снимку' % f); bad += 1
    print(u'.rmx: живой %d; перенесённых %d = складу полосы; нетронутых %d = снимку' % (len(live_rmx), len(KEYS), len(untouched)))
    resp = listing(os.path.join(LIVE, 'response'), '.rmx')
    same = set(resp.values()) == set(live_rmx.values())
    print(u'response: %d файлов, множество sha256 = множеству по ключам: %s' % (len(resp), same))
    if not same or len(resp) != N_NEW or len(live_rmx) != N_NEW:
        bad += 1
    stamps = {}
    for f in live_rmx:
        st = stamp_of(os.path.join(LIVE, f)) or '?'
        stamps[st.split(';')[0]] = stamps.get(st.split(';')[0], 0) + 1
    print(u'клейма живых .rmx: %s' % stamps)
    if stamps != {'phys=19': N_NEW}:
        bad += 1
    write_list(live_rmx, os.path.join(ART, 'live_store_after_sha256.txt'))
    write_list(resp, os.path.join(ART, 'live_response_after_sha256.txt'))
    print(u'ПЕРЕНОС ПРИНЯТ' if bad == 0 else u'⛔ ПЕРЕНОС НЕ ПРИНЯТ: %d' % bad)
    return 0 if bad == 0 else 1


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    fn = {'backup': backup, 'mkstore': mkstore, 'verify_before': verify_before,
          'swap': swap, 'verify_after': verify_after}.get(cmd)
    if fn is None:
        print(__doc__); sys.exit(2)
    sys.exit(fn())
