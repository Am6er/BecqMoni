# -*- coding: utf-8 -*-
r"""store55 (rev55, полоса `d55` 13.11.2026: склад `uc_full` полосы `a315u` — float-DLL GPU с `uc` умолчанием
сборки + разделы электрона с ключами λ и L₀, клеймо раздела `cl=2;l0=1`) — перенос склада матриц из каталога полосы
в живой склад корпуса основного дерева; копия handover/store54/store54_swap.py, сменены только STORE и BACKUP.

Склад `uc_full` лежит ТОЛЬКО по ключу сцены (D:\BqMoni_Claude\a315u\store\uc_full\<ключ>.rmx), каталога response\ у
него нет. Соответствие guid -> ключ берётся из ЖИВОГО склада до подмены: response\<guid>.rmx побитно равен
geometries\<ключ>.rmx (sha256), и это соответствие обязано быть взаимно однозначным (51 на 51) — иначе отказ.
Таблица соответствия пишется в снимок (guid_map.txt) и читается оттуда при swap/verify/restore.

⛔ Подмена — ТОЛЬКО вместе с умолчаниями `FsaBetaLibrary.CoulombLambda = true` и `FermiFiniteSize = true`
(вердикт критика `crit55`): при прежних умолчаниях разбор раздел склада не берёт (код 68).

  python -I store55_swap.py backup   -- снимок живого склада (51 .rmx + response/51) в
                                        D:\BqMoni_Claude\store_backup_rev54\ + sha256-списки + guid_map.txt;
                                        каталог снимка обязан НЕ существовать
  python -I store55_swap.py swap     -- склад полосы -> живой склад: <ключ>.rmx и response/<guid>.rmx (= uc_full\<ключ>.rmx);
                                        только после backup, живой склад обязан быть равен снимку; затем verify
  python -I store55_swap.py verify   -- живой склад = склад полосы по sha256 (обе половины через guid_map)
  python -I store55_swap.py restore  -- снимок -> живой склад (откат на rev54), затем сверка со снимком
  ключ --root=<корень дерева> — корень основного дерева (умолчание: два уровня выше файла, если файл в handover/store55/)

Коды: 0 — сошлось; 1 — расхождение; 2 — стенд.
"""
import hashlib, io, os, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
STORE = r'D:\BqMoni_Claude\a315u\store\uc_full'
BACKUP = r'D:\BqMoni_Claude\store_backup_rev54'
EXPECT = 51


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def rmx(d):
    return sorted(f for f in os.listdir(d) if f.lower().endswith('.rmx')) if os.path.isdir(d) else []


def table(d):
    return dict((f, sha(os.path.join(d, f))) for f in rmx(d))


def write_list(t, path):
    with io.open(path, 'w', encoding='utf-8', newline='\n') as fh:
        for f in sorted(t):
            fh.write(u'%s  %s\n' % (t[f], f))


def compare(a, b, what):
    bad = [f for f in sorted(set(a) | set(b)) if a.get(f) != b.get(f)]
    print(u'%s: %d / %d, расхождений %d' % (what, len(a) - len([f for f in bad if f in a]), len(a), len(bad)))
    for f in bad[:20]:
        print(u'   ⛔ %s: %s / %s' % (f, (a.get(f) or u'нет')[:16], (b.get(f) or u'нет')[:16]))
    return not bad


def read_map():
    p = os.path.join(BACKUP, 'guid_map.txt')
    m = {}
    with io.open(p, encoding='utf-8') as fh:
        for line in fh:
            line = line.strip()
            if line:
                g, k = line.split('  ')
                m[g] = k
    return m


def verify(live, live_resp):
    gm = read_map()
    a, ar = table(live), table(live_resp)
    s = table(STORE)
    want_resp = dict((g, s.get(k)) for g, k in gm.items())
    ok = len(s) == EXPECT and len(gm) == EXPECT
    ok &= compare(dict((f, a.get(f)) for f in s), s, u'по ключу (живой = склад полосы)')
    ok &= compare(dict((g, ar.get(g)) for g in want_resp), want_resp, u'response (живой = склад полосы по guid_map)')
    same = set(table(live).values()) == set(table(live_resp).values())
    print(u'множества sha256 по ключу и по guid в живом складе: %s' % (u'РАВНЫ' if same else u'⛔ РАЗНЫЕ'))
    return ok and same


def main():
    global ROOT
    mode = sys.argv[1] if len(sys.argv) > 1 else ''
    for x in sys.argv[2:]:
        if x.startswith('--root='):
            ROOT = x.split('=', 1)[1]
    live = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'geometries')
    live_resp = os.path.join(live, 'response')
    b_resp = os.path.join(BACKUP, 'response')
    if not os.path.isdir(live):
        print(u'⛔ нет живого склада %s' % live)
        return 2
    if mode == 'backup':
        if os.path.exists(BACKUP):
            print(u'⛔ %s уже есть — снимок не перезаписывается' % BACKUP)
            return 2
        a, ar = table(live), table(live_resp)
        if len(a) != EXPECT or len(ar) != EXPECT:
            print(u'⛔ в живом складе %d .rmx и %d response (ждём %d)' % (len(a), len(ar), EXPECT))
            return 2
        by_sha = {}
        for k, h in a.items():
            by_sha.setdefault(h, []).append(k)
        if any(len(v) != 1 for v in by_sha.values()):
            print(u'⛔ у разных ключей одинаковый sha256 — соответствие guid -> ключ неоднозначно')
            return 2
        gm = {}
        for g, h in ar.items():
            if h not in by_sha:
                print(u'⛔ response\\%s не равен ни одному ключу' % g)
                return 2
            gm[g] = by_sha[h][0]
        if len(set(gm.values())) != EXPECT:
            print(u'⛔ соответствие guid -> ключ не взаимно однозначно (%d ключей на %d guid)' % (len(set(gm.values())), len(gm)))
            return 2
        os.makedirs(b_resp)
        for f in a:
            shutil.copy2(os.path.join(live, f), os.path.join(BACKUP, f))
        for f in ar:
            shutil.copy2(os.path.join(live_resp, f), os.path.join(b_resp, f))
        b, br = table(BACKUP), table(b_resp)
        write_list(b, os.path.join(BACKUP, 'sha256_rmx.txt'))
        write_list(br, os.path.join(BACKUP, 'sha256_response.txt'))
        with io.open(os.path.join(BACKUP, 'guid_map.txt'), 'w', encoding='utf-8', newline='\n') as fh:
            for g in sorted(gm):
                fh.write(u'%s  %s\n' % (g, gm[g]))
        ok = compare(a, b, u'снимок по ключу') & compare(ar, br, u'снимок response')
        print(u'снимок: %s (%d + %d), guid_map %d' % (BACKUP, len(b), len(br), len(gm)))
        return 0 if ok else 1
    if mode == 'swap':
        if not os.path.isdir(BACKUP):
            print(u'⛔ сперва backup: снимка %s нет' % BACKUP)
            return 2
        if not (compare(table(live), table(BACKUP), u'живой = снимок, по ключу')
                and compare(table(live_resp), table(b_resp), u'живой = снимок, response')):
            print(u'⛔ живой склад сменился после снимка — перенос не начат')
            return 1
        st = table(STORE)
        gm = read_map()
        if len(st) != EXPECT or set(gm.values()) != set(st):
            print(u'⛔ склад полосы неполон или ключи не те: %d .rmx' % len(st))
            return 2
        for f in st:
            shutil.copyfile(os.path.join(STORE, f), os.path.join(live, f))
        for g, k in gm.items():
            shutil.copyfile(os.path.join(STORE, k), os.path.join(live_resp, g))
        print(u'перенесено: %d по ключу, %d response' % (len(st), len(gm)))
        return 0 if verify(live, live_resp) else 1
    if mode == 'verify':
        return 0 if verify(live, live_resp) else 1
    if mode == 'restore':
        if not os.path.isdir(BACKUP):
            print(u'⛔ снимка %s нет' % BACKUP)
            return 2
        for f in rmx(BACKUP):
            shutil.copyfile(os.path.join(BACKUP, f), os.path.join(live, f))
        for f in rmx(b_resp):
            shutil.copyfile(os.path.join(b_resp, f), os.path.join(live_resp, f))
        ok = compare(table(live), table(BACKUP), u'живой = снимок, по ключу') & compare(table(live_resp), table(b_resp), u'живой = снимок, response')
        return 0 if ok else 1
    print(__doc__)
    return 2


if __name__ == '__main__':
    for _stream in (sys.stdout, sys.stderr):
        try:
            _stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass
    sys.exit(main())
