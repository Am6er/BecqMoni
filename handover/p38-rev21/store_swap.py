# -*- coding: utf-8 -*-
"""П38 шаг 1 — снимок живого склада физики 16 и перенос склада физики 17 из worktree П37.

  python handover/p38-rev21/store_swap.py backup   -- 45 .rmx + response/ -> C:\\Users\\moroz\\store_phys16_backup_2026-09-13\\
                                                     sha256 списки; сверка с П37 live_store_phys16_sha256.txt
  python handover/p38-rev21/store_swap.py swap     -- worktree -> живой склад (по ключам и response/<guid>),
                                                     сверка sha256 с store_sha256.txt / store_response_sha256.txt П37
  python handover/p38-rev21/store_swap.py verify   -- только сверка живого склада с П37

Ничего не удаляется: файлы перезаписываются поимённо. Точка — разделитель, newline=''.
"""
import hashlib, io, os, shutil, sys

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
LIVE = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'geometries')
WT = r'C:\Users\moroz\bqp37\tools\CORPUS\corpus\geometries'
BACKUP = r'C:\Users\moroz\store_phys16_backup_2026-09-13'
ART = os.path.join(ROOT, 'handover', 'p38-rev21')
P37 = os.path.join(ROOT, 'handover', 'p37-store')


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def rmx(d):
    return sorted(f for f in os.listdir(d) if f.lower().endswith('.rmx'))


def listing(d):
    return [(f, sha(os.path.join(d, f)), os.path.getsize(os.path.join(d, f))) for f in rmx(d)]


def write_list(rows, path):
    with io.open(path, 'w', encoding='utf-8', newline='') as fh:
        for f, h, n in rows:
            fh.write(u'%s  %8d  %s\n' % (h, n, f))


def read_p37(path):
    out = {}
    with io.open(path, encoding='utf-8') as fh:
        for line in fh:
            parts = line.split()
            if len(parts) >= 2:
                out[parts[-1]] = parts[0]
    return out


def compare(rows, ref, what):
    bad = 0
    names = set(f for f, _, _ in rows)
    for f, h, n in rows:
        if f not in ref:
            print(u'  %s: %s -- нет в списке П37' % (what, f)); bad += 1
        elif ref[f] != h:
            print(u'  %s: %s -- sha256 РАЗОШЁЛСЯ' % (what, f)); bad += 1
    for f in ref:
        if f not in names:
            print(u'  %s: %s -- есть у П37, нет здесь' % (what, f)); bad += 1
    print(u'%s: файлов %d, у П37 %d, расхождений %d' % (what, len(rows), len(ref), bad))
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
    write_list(live, os.path.join(ART, 'live_store_phys16_before_sha256.txt'))
    write_list(resp, os.path.join(ART, 'live_store_phys16_before_response_sha256.txt'))
    # снимок в копии равен оригиналу
    bk = listing(BACKUP)
    bkr = listing(os.path.join(BACKUP, 'response'))
    same = (bk == live) and (bkr == resp)
    print(u'снимок: %d .rmx + %d response/ -> %s; копия равна оригиналу: %s'
          % (len(live), len(resp), BACKUP, u'да' if same else u'НЕТ'))
    bad = compare(live, read_p37(os.path.join(P37, 'live_store_phys16_sha256.txt')),
                  u'живой склад ф16 против снимка П37 (до работы П37)')
    return 0 if same and bad == 0 else 1


def verify():
    live = listing(LIVE)
    resp = listing(os.path.join(LIVE, 'response'))
    write_list(live, os.path.join(ART, 'live_store_phys17_after_sha256.txt'))
    write_list(resp, os.path.join(ART, 'live_store_phys17_after_response_sha256.txt'))
    bad = compare(live, read_p37(os.path.join(P37, 'store_sha256.txt')), u'живой склад против store_sha256 П37')
    bad += compare(resp, read_p37(os.path.join(P37, 'store_response_sha256.txt')),
                   u'живой response/ против store_response_sha256 П37')
    return 0 if bad == 0 else 1


def swap():
    wt = rmx(WT)
    wtr = rmx(os.path.join(WT, 'response'))
    if len(wt) != 45 or len(wtr) != 45:
        print(u'ОТКАЗ: в worktree %d .rmx и %d response/, ждали 45/45' % (len(wt), len(wtr)))
        return 1
    for f in wt:
        shutil.copy2(os.path.join(WT, f), os.path.join(LIVE, f))
    for f in wtr:
        shutil.copy2(os.path.join(WT, 'response', f), os.path.join(LIVE, 'response', f))
    print(u'перенесено: %d .rmx + %d response/ из %s' % (len(wt), len(wtr), WT))
    return verify()


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    sys.exit({'backup': backup, 'swap': swap, 'verify': verify}.get(cmd, lambda: 2)())
