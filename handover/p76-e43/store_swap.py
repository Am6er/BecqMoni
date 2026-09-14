# -*- coding: utf-8 -*-
r"""П76 (E43) — снимок живого склада физики 18 и перенос ДВУХ матриц полосы (контактные сцены RC103 с зазором
3.5 мм: `RC103_point0`, `RC103_lu_front`) из склада полосы в живой склад. Образец — handover/p66-rev23/store_swap.py (П66).

  python handover/p76-e43/store_swap.py backup   -- живой склад (46 .rmx + response/ 46) -> D:\BqMoni_Claude\p76\store_backup\
                                                    + sha256-списки; сверка с П66 «после переноса» (live_store_after_*.txt):
                                                    живой склад не трогался с объявления rev23
  python handover/p76-e43/store_swap.py swap     -- D:\BqMoni_Claude\p76\store\ -> живой склад: 2 .rmx по ключам (поверх живых)
                                                    и 2 response/<guid>.rmx (поверх — guid от имени сцены тот же)
  python handover/p76-e43/store_swap.py verify   -- живой склад против склада полосы (2) и снимка (44 нетронутых);
                                                    множества sha256 по ключам и под guid равны

Ничего не удаляется и не добавляется: guid сцены выводится из ИМЕНИ сцены (`CorpusEffProbe.StableGuid`), ключи те же,
поэтому старые `response/<guid>.rmx` перезаписываются, сирот не остаётся. Точка — разделитель, newline=''.
"""
import hashlib
import io
import os
import shutil
import sys

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
LIVE = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'geometries')
STORE = r'D:\BqMoni_Claude\p76\store'
BACKUP = r'D:\BqMoni_Claude\p76\store_backup'
ART = os.path.join(ROOT, 'handover', 'p76-e43')
P66 = os.path.join(ROOT, 'handover', 'p66-rev23')
LANE_KEYS = ('RC103_point0', 'RC103_lu_front')
N_LIVE, N_LANE = 46, 2


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def rmx(d):
    return sorted(f for f in os.listdir(d) if f.lower().endswith('.rmx')) if os.path.isdir(d) else []


def listing(d):
    return [(f, sha(os.path.join(d, f)), os.path.getsize(os.path.join(d, f))) for f in rmx(d)]


def write_list(rows, path):
    with io.open(path, 'w', encoding='utf-8', newline='') as fh:
        for f, h, n in rows:
            fh.write(u'%s  %8d  %s\n' % (h, n, f))


def read_ref(path):
    out = {}
    with io.open(path, encoding='utf-8-sig') as fh:
        for line in fh:
            parts = line.split()
            if len(parts) >= 2:
                out[parts[-1].lstrip('*')] = parts[0].lower()
    return out


def compare(rows, ref, what, subset=False):
    u"""subset=True — судятся только имена из ref (остальные строки rows не в счёт)."""
    bad = 0
    names = set(f for f, _, _ in rows)
    for f, h, n in rows:
        if f not in ref:
            if not subset:
                print(u'  %s: %s -- нет в эталонном списке' % (what, f)); bad += 1
        elif ref[f] != h:
            print(u'  %s: %s -- sha256 РАЗОШЁЛСЯ' % (what, f)); bad += 1
    for f in ref:
        if f not in names:
            print(u'  %s: %s -- есть в эталоне, нет здесь' % (what, f)); bad += 1
    print(u'%s: файлов %d, в эталоне %d, расхождений %d' % (what, len(rows), len(ref), bad))
    return bad


def backup():
    os.makedirs(os.path.join(BACKUP, 'response'), exist_ok=True)
    live = listing(LIVE)
    resp = listing(os.path.join(LIVE, 'response'))
    for f, _, _ in live:
        shutil.copy2(os.path.join(LIVE, f), os.path.join(BACKUP, f))
    for f, _, _ in resp:
        shutil.copy2(os.path.join(LIVE, 'response', f), os.path.join(BACKUP, 'response', f))
    write_list(live, os.path.join(BACKUP, 'sha256_rmx.txt'))
    write_list(resp, os.path.join(BACKUP, 'sha256_response.txt'))
    write_list(live, os.path.join(ART, 'live_store_before_sha256.txt'))
    write_list(resp, os.path.join(ART, 'live_store_before_response_sha256.txt'))
    bk, bkr = listing(BACKUP), listing(os.path.join(BACKUP, 'response'))
    same = (bk == live) and (bkr == resp)
    print(u'снимок: %d .rmx + %d response/ -> %s; копия равна оригиналу: %s'
          % (len(live), len(resp), BACKUP, u'да' if same else u'НЕТ'))
    bad = compare(live, read_ref(os.path.join(P66, 'live_store_after_sha256.txt')),
                  u'живой склад против П66 «после переноса» (ключи)')
    bad += compare(resp, read_ref(os.path.join(P66, 'live_store_after_response_sha256.txt')),
                   u'живой response/ против П66 «после переноса»')
    return 0 if same and bad == 0 and len(live) == N_LIVE and len(resp) == N_LIVE else 1


def lane_lists():
    lane = listing(STORE)
    laner = listing(os.path.join(STORE, 'response'))
    return lane, laner


def swap():
    lane, laner = lane_lists()
    if len(lane) != N_LANE or len(laner) != N_LANE:
        print(u'ОТКАЗ: в складе полосы %d .rmx и %d response/, ждали %d/%d' % (len(lane), len(laner), N_LANE, N_LANE))
        return 1
    live_names = set(rmx(LIVE))
    live_resp = set(rmx(os.path.join(LIVE, 'response')))
    for f, _, _ in lane:
        key = f[:-4]
        if key not in LANE_KEYS:
            print(u'ОТКАЗ: в складе полосы чужой ключ %s — переносятся только %s' % (f, ', '.join(LANE_KEYS))); return 1
        if f not in live_names:
            print(u'ОТКАЗ: ключа %s нет в живом складе — перенос только поверх известных' % f); return 1
    for f, _, _ in laner:
        if f not in live_resp:
            print(u'ОТКАЗ: response/%s нет в живом складе — guid обязан быть прежним (StableGuid от имени)' % f); return 1
    if not os.path.isdir(BACKUP) or len(rmx(BACKUP)) != N_LIVE:
        print(u'ОТКАЗ: сперва backup (снимка нет или он неполон)'); return 1
    for f, _, _ in lane:
        shutil.copy2(os.path.join(STORE, f), os.path.join(LIVE, f))
    for f, _, _ in laner:
        shutil.copy2(os.path.join(STORE, 'response', f), os.path.join(LIVE, 'response', f))
    print(u'перенесено: %d .rmx + %d response/ из %s' % (len(lane), len(laner), STORE))
    return verify()


def verify():
    lane, laner = lane_lists()
    live = listing(LIVE)
    resp = listing(os.path.join(LIVE, 'response'))
    write_list(live, os.path.join(ART, 'live_store_after_sha256.txt'))
    write_list(resp, os.path.join(ART, 'live_store_after_response_sha256.txt'))
    lane_ref = dict((f, h) for f, h, _ in lane)
    laner_ref = dict((f, h) for f, h, _ in laner)
    bad = compare(live, lane_ref, u'живой склад против склада полосы (%d по ключам)' % N_LANE, subset=True)
    bad += compare(resp, laner_ref, u'живой response/ против склада полосы (%d под guid)' % N_LANE, subset=True)
    # нетронутые: всё, чего нет в складе полосы, равно снимку
    bk = read_ref(os.path.join(BACKUP, 'sha256_rmx.txt'))
    bkr = read_ref(os.path.join(BACKUP, 'sha256_response.txt'))
    rest = [(f, h, n) for f, h, n in live if f not in lane_ref]
    restr = [(f, h, n) for f, h, n in resp if f not in laner_ref]
    bad += compare(rest, dict((f, h) for f, h in bk.items() if f not in lane_ref), u'нетронутые по ключам против снимка')
    bad += compare(restr, dict((f, h) for f, h in bkr.items() if f not in laner_ref), u'нетронутые под guid против снимка')
    # перенесённые ОБЯЗАНЫ отличаться от снимка: иначе перенесли то, что было
    changed = sum(1 for f, h in lane_ref.items() if bk.get(f) != h)
    print(u'перенесённые против снимка: изменилось %d из %d' % (changed, len(lane_ref)))
    if changed != len(lane_ref):
        print(u'ОТКАЗ: перенесённая матрица равна прежней — перенос ничего не сменил'); bad += 1
    s_key = sorted(h for _, h, _ in live)
    s_guid = sorted(h for _, h, _ in resp)
    print(u'множества sha256 по ключам и под guid %s (%d / %d)' % (u'РАВНЫ' if s_key == s_guid else u'РАЗОШЛИСЬ', len(live), len(resp)))
    if s_key != s_guid:
        bad += 1
    if len(live) != N_LIVE or len(resp) != N_LIVE:
        print(u'ОТКАЗ: в живом складе %d / %d, ждали %d / %d' % (len(live), len(resp), N_LIVE, N_LIVE)); bad += 1
    return 0 if bad == 0 else 1


if __name__ == '__main__':
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass
    cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    sys.exit({'backup': backup, 'swap': swap, 'verify': verify}.get(cmd, lambda: 2)())
