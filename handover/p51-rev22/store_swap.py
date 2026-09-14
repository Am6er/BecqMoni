# -*- coding: utf-8 -*-
r"""П51 шаг 1–2 — снимок живого склада физики 17 и перенос склада физики 18 из worktree П50.

  python handover/p51-rev22/store_swap.py backup   -- 45 .rmx + response/ -> D:\BqMoni_Claude\store_phys17_backup\
                                                     sha256 списки; сверка с П50 live_store_phys17_sha256.txt
  python handover/p51-rev22/store_swap.py swap     -- worktree -> живой склад (по ключам и response/<guid>),
                                                     сверка sha256 с store_sha256.txt / store_response_sha256.txt П50
  python handover/p51-rev22/store_swap.py verify   -- только сверка живого склада с П50
  python handover/p51-rev22/store_swap.py spectra  -- 85 спектров с узлами <Efficiency> физики 18 из worktree
                                                     -> живой корпус; до копии — контроль, что файл worktree
                                                     отличается от git HEAD ТОЛЬКО внутри узла <Efficiency>

Ничего не удаляется: файлы перезаписываются поимённо. Точка — разделитель, newline=''.
Образец — handover/p38-rev21/store_swap.py (П38).
"""
import hashlib, io, os, re, shutil, subprocess, sys

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
LIVE = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'geometries')
LIVE_SPECTRA = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'spectra')
WT_ROOT = r'D:\BqMoni_Claude\p50\wt'
WT = os.path.join(WT_ROOT, 'tools', 'CORPUS', 'corpus', 'geometries')
WT_SPECTRA = os.path.join(WT_ROOT, 'tools', 'CORPUS', 'corpus', 'spectra')
BACKUP = r'D:\BqMoni_Claude\store_phys17_backup'
ART = os.path.join(ROOT, 'handover', 'p51-rev22')
P50 = os.path.join(ROOT, 'handover', 'p50-physics18')


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


def read_ref(path):
    u"""Список П50: `sha  size  name` либо `sha *name` (sha256sum) -> {name: sha}."""
    out = {}
    with io.open(path, encoding='utf-8-sig') as fh:
        for line in fh:
            parts = line.split()
            if len(parts) >= 2:
                out[parts[-1].lstrip('*')] = parts[0].lower()
    return out


def compare(rows, ref, what):
    bad = 0
    names = set(f for f, _, _ in rows)
    for f, h, n in rows:
        if f not in ref:
            print(u'  %s: %s -- нет в списке П50' % (what, f)); bad += 1
        elif ref[f] != h:
            print(u'  %s: %s -- sha256 РАЗОШЁЛСЯ' % (what, f)); bad += 1
    for f in ref:
        if f not in names:
            print(u'  %s: %s -- есть у П50, нет здесь' % (what, f)); bad += 1
    print(u'%s: файлов %d, у П50 %d, расхождений %d' % (what, len(rows), len(ref), bad))
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
    write_list(live, os.path.join(ART, 'live_store_phys17_before_sha256.txt'))
    write_list(resp, os.path.join(ART, 'live_store_phys17_before_response_sha256.txt'))
    bk = listing(BACKUP)
    bkr = listing(os.path.join(BACKUP, 'response'))
    same = (bk == live) and (bkr == resp)
    print(u'снимок: %d .rmx + %d response/ -> %s; копия равна оригиналу: %s'
          % (len(live), len(resp), BACKUP, u'да' if same else u'НЕТ'))
    bad = compare(live, read_ref(os.path.join(P50, 'live_store_phys17_sha256.txt')),
                  u'живой склад ф17 против снимка П50 (до работы П50)')
    return 0 if same and bad == 0 and len(live) == 45 and len(resp) == 45 else 1


def verify(tag='after'):
    live = listing(LIVE)
    resp = listing(os.path.join(LIVE, 'response'))
    write_list(live, os.path.join(ART, 'live_store_phys18_%s_sha256.txt' % tag))
    write_list(resp, os.path.join(ART, 'live_store_phys18_%s_response_sha256.txt' % tag))
    bad = compare(live, read_ref(os.path.join(P50, 'store_sha256.txt')), u'живой склад против store_sha256 П50')
    bad += compare(resp, read_ref(os.path.join(P50, 'store_response_sha256.txt')),
                   u'живой response/ против store_response_sha256 П50')
    # множество sha256 файлов по ключам = множеству sha256 файлов под guid (П20 §6 п. 4)
    s_key = sorted(h for _, h, _ in live)
    s_guid = sorted(h for _, h, _ in resp)
    print(u'множества sha256 по ключам и под guid %s' % (u'РАВНЫ' if s_key == s_guid else u'РАЗОШЛИСЬ'))
    if s_key != s_guid:
        bad += 1
    return 0 if bad == 0 else 1


def swap():
    wt = rmx(WT)
    wtr = rmx(os.path.join(WT, 'response'))
    if len(wt) != 45 or len(wtr) != 45:
        print(u'ОТКАЗ: в worktree %d .rmx и %d response/, ждали 45/45' % (len(wt), len(wtr)))
        return 1
    live_names = set(rmx(LIVE)); live_resp = set(rmx(os.path.join(LIVE, 'response')))
    if set(wt) != live_names:
        print(u'ОТКАЗ: имена по ключам в worktree и живом складе расходятся: %s'
              % sorted(set(wt) ^ live_names))
        return 1
    if set(wtr) != live_resp:
        print(u'ОТКАЗ: имена под guid в worktree и живом складе расходятся: %s'
              % sorted(set(wtr) ^ live_resp))
        return 1
    for f in wt:
        shutil.copy2(os.path.join(WT, f), os.path.join(LIVE, f))
    for f in wtr:
        shutil.copy2(os.path.join(WT, 'response', f), os.path.join(LIVE, 'response', f))
    print(u'перенесено: %d .rmx + %d response/ из %s' % (len(wt), len(wtr), WT))
    return verify()


RE_EFF = re.compile(r'<Efficiency>.*</Efficiency>', re.S)


def _git_show(path_rel):
    out = subprocess.run(['git', 'show', 'HEAD:' + path_rel.replace('\\', '/')], cwd=ROOT,
                         stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if out.returncode != 0:
        return None
    return out.stdout


def _strip_eff(data):
    u"""Текст без узла <Efficiency>…</Efficiency> (последний закрывающий тег строки — ловушка T30)."""
    text = data.decode('utf-8-sig')
    return RE_EFF.sub('<Efficiency/>', text, count=1)


def spectra():
    out = subprocess.run(['git', 'status', '--porcelain', '--', 'tools/CORPUS/corpus/spectra'],
                         cwd=WT_ROOT, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    names = []
    for line in out.stdout.decode('utf-8', 'replace').splitlines():
        if line[:2].strip() != 'M':
            continue
        rel = line[3:].strip().strip('"')
        names.append(os.path.basename(rel))
    names.sort()
    print(u'изменённых спектров в worktree: %d' % len(names))
    if len(names) != 85:
        print(u'ОТКАЗ: ждали 85')
        return 1
    bad = 0; copied = 0; stamp18 = 0
    for name in names:
        wt_path = os.path.join(WT_SPECTRA, name)
        head = _git_show('tools/CORPUS/corpus/spectra/' + name)
        if head is None:
            print(u'  %s: нет в git HEAD' % name); bad += 1; continue
        live_path = os.path.join(LIVE_SPECTRA, name)
        with open(live_path, 'rb') as fh:
            live_bytes = fh.read()
        # индекс git хранит LF, рабочие копии обоих деревьев — CRLF (autocrlf): сравнение
        # с блобом HEAD — после нормализации; байты worktree копируются как есть (CRLF).
        if live_bytes.replace(b'\r\n', b'\n') != head:
            print(u'  %s: живой файл ≠ git HEAD ещё ДО переноса (после CRLF->LF)' % name); bad += 1; continue
        with open(wt_path, 'rb') as fh:
            wt_bytes = fh.read()
        if wt_bytes.count(b'\r\n') != wt_bytes.count(b'\n') or live_bytes.count(b'\r\n') != live_bytes.count(b'\n'):
            print(u'  %s: переводы строк смешаны (worktree CRLF %d/LF %d, живой %d/%d)' % (
                name, wt_bytes.count(b'\r\n'), wt_bytes.count(b'\n'),
                live_bytes.count(b'\r\n'), live_bytes.count(b'\n'))); bad += 1; continue
        if _strip_eff(wt_bytes.replace(b'\r\n', b'\n')) != _strip_eff(head):
            print(u'  %s: worktree отличается от HEAD НЕ ТОЛЬКО узлом <Efficiency>' % name); bad += 1; continue
        m = re.search(r'<ComputeStamp>([^<]*)</ComputeStamp>', wt_bytes.decode('utf-8-sig'))
        if m and m.group(1).startswith('phys=18;') and 'ecomp=1' in m.group(1) and 'bpath=2' in m.group(1):
            stamp18 += 1
        else:
            print(u'  %s: клеймо кривой не физики 18: %s' % (name, m.group(1) if m else u'нет')); bad += 1; continue
        shutil.copy2(wt_path, live_path)
        copied += 1
    print(u'скопировано: %d; клеймо phys=18 ecomp=1 bpath=2: %d; отказов: %d' % (copied, stamp18, bad))
    # приёмка по живым файлам: 85 узлов phys=18, phys=17 — 0 (по всему каталогу spectra)
    n18 = n17 = nother = 0
    for f in sorted(os.listdir(LIVE_SPECTRA)):
        if not f.endswith('.xml'):
            continue
        with open(os.path.join(LIVE_SPECTRA, f), 'rb') as fh:
            t = fh.read().decode('utf-8-sig')
        m = re.search(r'<ComputeStamp>([^<]*)</ComputeStamp>', t)
        if not m:
            continue
        if m.group(1).startswith('phys=18;'):
            n18 += 1
        elif m.group(1).startswith('phys=17;'):
            n17 += 1
        else:
            nother += 1
    print(u'живые спектры: клеймо phys=18 — %d, phys=17 — %d, иное — %d' % (n18, n17, nother))
    return 0 if bad == 0 and copied == 85 and n18 == 85 and n17 == 0 else 1


if __name__ == '__main__':
    cmd = sys.argv[1] if len(sys.argv) > 1 else ''
    sys.exit({'backup': backup, 'swap': swap, 'verify': verify, 'spectra': spectra}.get(cmd, lambda: 2)())
