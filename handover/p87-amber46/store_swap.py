# -*- coding: utf-8 -*-
r"""П87 (AMBER46) — перенос склада формата 9 (Q_k в матрице) в живой склад и снятие сайдкаров `.qk`.
Образец — handover/p76-e43/store_swap.py (П76) и П51.

  python D:\BqMoni_Claude\p87\store_swap.py verify_before  -- живой склад = снимок D:\BqMoni_Claude\p87\store_backup (46+46+46 .qk)
  python D:\BqMoni_Claude\p87\store_swap.py swap           -- D:\BqMoni_Claude\p87\store\*.rmx (46, формат 9) -> живой склад по ключам
                                                              (поверх формата 8), затем mx_swap.py --store (response/<guid>.rmx)
  python D:\BqMoni_Claude\p87\store_swap.py verify_after   -- 46 живых .rmx = склад полосы по sha256; множество sha256
                                                              response/ = множеству по ключам; формат 9 у всех; блок Q_k у всех
  python D:\BqMoni_Claude\p87\store_swap.py rm_qk          -- снять 46 `.qk` из живого склада (rm, не git rm; снимок остаётся)

Ничего не добавляется: ключи те же, guid от имени сцены тот же, сирот не остаётся. Точка — разделитель, newline=''.
"""
import hashlib
import io
import os
import shutil
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rmx_qk  # noqa: E402

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
LIVE = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'geometries')
STORE = r'D:\BqMoni_Claude\p87\store'
BACKUP = r'D:\BqMoni_Claude\p87\store_backup'
ART = r'D:\BqMoni_Claude\p87\art'
N = 46


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


def verify_before():
    bad = 0
    for ext, sub in (('.rmx', ''), ('.qk', ''), ('.in', ''), ('.rmx', 'response')):
        live = listing(os.path.join(LIVE, sub), ext)
        bk = listing(os.path.join(BACKUP, sub), ext)
        diff = [f for f in set(live) | set(bk) if live.get(f) != bk.get(f)]
        print(u'%-10s %-4s живой %d, снимок %d, расхождений %d' % (sub or 'склад', ext, len(live), len(bk), len(diff)))
        bad += len(diff)
        if len(live) != N:
            bad += 1
    print(u'ЖИВОЙ СКЛАД = СНИМОК' if bad == 0 else u'⛔ РАСХОЖДЕНИЯ: %d' % bad)
    return 0 if bad == 0 else 1


def swap():
    src = listing(STORE, '.rmx')
    if len(src) != N:
        print(u'⛔ в складе полосы %d .rmx, ждали %d' % (len(src), N))
        return 1
    live_before = listing(LIVE, '.rmx')
    if set(src) != set(live_before):
        print(u'⛔ имена не совпадают: только в складе полосы %s, только в живом %s'
              % (sorted(set(src) - set(live_before)), sorted(set(live_before) - set(src))))
        return 1
    for f in sorted(src):
        m = rmx_qk.read(os.path.join(STORE, f))
        if m['format'] != 9 or not m['qk'] or len(m['qk']) != m['nodes']:
            print(u'⛔ %s: формат %d, узлов Q_k %d из %d — не переношу' % (f, m['format'], len(m['qk'] or []), m['nodes']))
            return 1
    write_list(live_before, os.path.join(ART, 'live_store_before_swap_sha256.txt'))
    write_list(listing(os.path.join(LIVE, 'response'), '.rmx'), os.path.join(ART, 'live_response_before_swap_sha256.txt'))
    n = 0
    for f in sorted(src):
        shutil.copy2(os.path.join(STORE, f), os.path.join(LIVE, f))
        n += 1
    print(u'скопировано по ключам: %d' % n)
    after = listing(LIVE, '.rmx')
    diff = [f for f in src if after.get(f) != src[f]]
    print(u'sha256 после копии против склада полосы: расхождений %d' % len(diff))
    cmd = [sys.executable, os.path.join(ROOT, 'tools', 'CORPUS', 'scripts', 'mx_swap.py'), '--from=' + STORE, '--store']
    print(u'mx_swap: %s' % ' '.join(cmd[1:]))
    r = subprocess.run(cmd, cwd=ROOT, capture_output=True)
    print(r.stdout.decode('utf-8', 'replace')[-2000:])
    print(r.stderr.decode('utf-8', 'replace')[-1000:])
    print(u'mx_swap code=%d' % r.returncode)
    return 0 if (len(diff) == 0 and r.returncode == 0) else 1


def verify_after():
    src = listing(STORE, '.rmx')
    live = listing(LIVE, '.rmx')
    resp = listing(os.path.join(LIVE, 'response'), '.rmx')
    diff = [f for f in src if live.get(f) != src[f]]
    print(u'живых .rmx %d, = складу полосы по sha256: %d, расхождений %d' % (len(live), len(src) - len(diff), len(diff)))
    print(u'response/: %d; множество sha256 response = множеству по ключам: %s'
          % (len(resp), 'ДА' if set(resp.values()) == set(live.values()) else 'НЕТ'))
    fmts = {}
    noqk = 0
    for f in sorted(live):
        m = rmx_qk.read(os.path.join(LIVE, f))
        fmts[m['format']] = fmts.get(m['format'], 0) + 1
        if not m['qk'] or len(m['qk']) != m['nodes']:
            noqk += 1
    print(u'форматы живых: %s; без блока Q_k: %d' % (fmts, noqk))
    write_list(live, os.path.join(ART, 'live_store_after_swap_sha256.txt'))
    write_list(resp, os.path.join(ART, 'live_response_after_swap_sha256.txt'))
    ok = len(diff) == 0 and set(resp.values()) == set(live.values()) and fmts == {9: N} and noqk == 0 and len(resp) == N
    print(u'ПЕРЕНОС ПРИНЯТ' if ok else u'⛔ ПЕРЕНОС НЕ ПРИНЯТ')
    return 0 if ok else 1


def rm_qk():
    qk = files(LIVE, '.qk')
    bk = files(BACKUP, '.qk')
    if len(qk) != N or set(qk) != set(bk):
        print(u'⛔ в живом складе %d .qk (в снимке %d) — снимать не буду' % (len(qk), len(bk)))
        return 1
    for f in qk:
        if sha(os.path.join(LIVE, f)) != sha(os.path.join(BACKUP, f)):
            print(u'⛔ %s отличается от снимка — снимать не буду' % f)
            return 1
    for f in qk:
        os.remove(os.path.join(LIVE, f))
    print(u'снято .qk: %d (снимок %s\\*.qk остаётся); осталось в живом складе: %d' % (len(qk), BACKUP, len(files(LIVE, '.qk'))))
    return 0


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    sys.exit({'verify_before': verify_before, 'swap': swap, 'verify_after': verify_after, 'rm_qk': rm_qk}.get(cmd, lambda: 2)())
