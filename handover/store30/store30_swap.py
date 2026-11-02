# -*- coding: utf-8 -*-
r"""store30 (AMBER274, физика 30) — перенос склада матриц из каталога полосы в живой склад корпуса
основного дерева; образец — handover/p51-rev22/store_swap.py (П51) и протокол sha-снимка П343.

Дерево — то, где лежит этот файл (handover/store30/ -> корень на два уровня выше); пути с именем
пользователя здесь не пишутся.

  python -I handover/store30/store30_swap.py backup  -- снимок живого склада (51 .rmx + response/51) в
                                                        D:\BqMoni_Claude\store_backup_rev51\ + sha256-списки;
                                                        каталог снимка обязан НЕ существовать
  python -I handover/store30/store30_swap.py swap    -- склад полосы -> живой склад: <ключ>.rmx и
                                                        response/<guid>.rmx поимённо; только после backup,
                                                        живой склад обязан быть равен снимку; затем verify
  python -I handover/store30/store30_swap.py verify  -- живой склад = склад полосы по sha256 (обе половины),
                                                        множества sha256 по ключу и по guid равны
  python -I handover/store30/store30_swap.py restore -- снимок -> живой склад (откат), затем сверка со снимком
  (ключ --backup=<каталог> после режима — другой каталог снимка; только для опыта в песочнице)

Ничего не удаляется: файлы перезаписываются поимённо. Склад полосы — D:\BqMoni_Claude\store30\store
(51 <ключ>.rmx формата 12 с разделом esec=4 и response\<guid>.rmx — те же 51 файл под guid кривой).
Коды: 0 — сошлось; 1 — расхождение; 2 — стенд (нет каталога, снимок уже есть и т. п.).
"""
import hashlib, io, os, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
LIVE = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus', 'geometries')
LIVE_RESP = os.path.join(LIVE, 'response')
STORE = r'D:\BqMoni_Claude\store30\store'
STORE_RESP = os.path.join(STORE, 'response')
BACKUP = r'D:\BqMoni_Claude\store_backup_rev51'
BACKUP_RESP = os.path.join(BACKUP, 'response')
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


def verify(src, src_resp, what):
    a, b = table(LIVE), table(src)
    ar, br = table(LIVE_RESP), table(src_resp)
    ok = len(b) == EXPECT and len(br) == EXPECT
    if not ok:
        print(u'⛔ в источнике %d .rmx и %d response (ждём %d и %d)' % (len(b), len(br), EXPECT, EXPECT))
    ok &= compare(dict((f, a.get(f)) for f in b), b, u'по ключу (живой = %s)' % what)
    ok &= compare(dict((f, ar.get(f)) for f in br), br, u'response (живой = %s)' % what)
    same_sets = set(b.values()) == set(br.values())
    print(u'множества sha256 по ключу и по guid в %s: %s' % (what, u'РАВНЫ' if same_sets else u'⛔ РАЗНЫЕ'))
    return ok and same_sets


def main():
    global BACKUP, BACKUP_RESP
    mode = sys.argv[1] if len(sys.argv) > 1 else ''
    for a in sys.argv[2:]:
        if a.startswith('--backup='):  # только для опыта в песочнице; штатно — умолчание
            BACKUP = a.split('=', 1)[1]
            BACKUP_RESP = os.path.join(BACKUP, 'response')
    if not os.path.isdir(LIVE):
        print(u'⛔ нет живого склада %s' % LIVE)
        return 2
    if mode == 'backup':
        if os.path.exists(BACKUP):
            print(u'⛔ %s уже есть — снимок не перезаписывается' % BACKUP)
            return 2
        os.makedirs(BACKUP_RESP)
        for f in rmx(LIVE):
            shutil.copy2(os.path.join(LIVE, f), os.path.join(BACKUP, f))
        for f in rmx(LIVE_RESP):
            shutil.copy2(os.path.join(LIVE_RESP, f), os.path.join(BACKUP_RESP, f))
        a, b = table(LIVE), table(BACKUP)
        ar, br = table(LIVE_RESP), table(BACKUP_RESP)
        write_list(b, os.path.join(BACKUP, 'sha256_rmx.txt'))
        write_list(br, os.path.join(BACKUP, 'sha256_response.txt'))
        ok = compare(a, b, u'снимок по ключу') & compare(ar, br, u'снимок response')
        print(u'снимок: %s (%d + %d)' % (BACKUP, len(b), len(br)))
        return 0 if ok else 1
    if mode == 'swap':
        if not os.path.isdir(BACKUP):
            print(u'⛔ сперва backup: снимка %s нет' % BACKUP)
            return 2
        if not (compare(table(LIVE), table(BACKUP), u'живой = снимок, по ключу')
                and compare(table(LIVE_RESP), table(BACKUP_RESP), u'живой = снимок, response')):
            print(u'⛔ живой склад сменился после снимка — перенос не начат')
            return 1
        st, sr = table(STORE), table(STORE_RESP)
        if len(st) != EXPECT or len(sr) != EXPECT:
            print(u'⛔ склад полосы неполон: %d .rmx, %d response' % (len(st), len(sr)))
            return 2
        for f in st:
            shutil.copyfile(os.path.join(STORE, f), os.path.join(LIVE, f))
        for f in sr:
            shutil.copyfile(os.path.join(STORE_RESP, f), os.path.join(LIVE_RESP, f))
        print(u'перенесено: %d по ключу, %d response' % (len(st), len(sr)))
        return 0 if verify(STORE, STORE_RESP, u'склад полосы') else 1
    if mode == 'verify':
        return 0 if verify(STORE, STORE_RESP, u'склад полосы') else 1
    if mode == 'restore':
        if not os.path.isdir(BACKUP):
            print(u'⛔ снимка %s нет' % BACKUP)
            return 2
        for f in rmx(BACKUP):
            shutil.copyfile(os.path.join(BACKUP, f), os.path.join(LIVE, f))
        for f in rmx(BACKUP_RESP):
            shutil.copyfile(os.path.join(BACKUP_RESP, f), os.path.join(LIVE_RESP, f))
        return 0 if verify(BACKUP, BACKUP_RESP, u'снимок') else 1
    print(__doc__)
    return 2


if __name__ == '__main__':
    for _stream in (sys.stdout, sys.stderr):
        try:
            _stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass
    sys.exit(main())
