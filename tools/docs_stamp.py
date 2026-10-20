# -*- coding: utf-8 -*-
"""Штамп версий гида docs/ перед заливкой на сайт (`AMBER248`).

Браузер держит скрипты, стили и снимки в кэше, и после заливки человек видел новую страницу со
СТАРЫМИ guide.js / quanta.js (20.10.2026: «гайд» у логотипа и 3D-заставка после их замены). Здесь
каждой ссылке на assets/* ставится ?v=<первые 8 знаков sha256 файла>, а снимкам img/<язык>/*.png —
общая версия IMGV в guide.js (хэш всего каталога img/). Изменился файл — изменился адрес.

Запуск: python tools/docs_stamp.py            — проставить; код 0
        python tools/docs_stamp.py --check    — только проверить, что штампы свежие; код 1, если нет
"""
import hashlib
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'docs')


def h8(data):
    return hashlib.sha256(data).hexdigest()[:8]


def read(p):
    with open(p, 'rb') as f:
        return f.read()


def write_text(p, s):
    with open(p, 'w', encoding='utf-8', newline='\n') as f:
        f.write(s)


def img_version():
    acc = hashlib.sha256()
    base = os.path.join(ROOT, 'img')
    for d, _, files in sorted(os.walk(base)):
        for n in sorted(files):
            p = os.path.join(d, n)
            acc.update(os.path.relpath(p, base).replace('\\', '/').encode('utf-8'))
            acc.update(read(p))
    return acc.hexdigest()[:8]


def main():
    check = '--check' in sys.argv
    stale = []
    # 1) версия снимков — внутрь guide.js (до хэша guide.js: он от неё зависит)
    gj = os.path.join(ROOT, 'assets', 'guide.js')
    s = read(gj).decode('utf-8')
    v = img_version()
    s2, n = re.subn(r"const IMGV = '[0-9a-f]*';", "const IMGV = '" + v + "';", s)
    if n != 1:
        print('guide.js: строки const IMGV не найдено ровно одной (' + str(n) + ')')
        return 2
    if s2 != s:
        stale.append('assets/guide.js (IMGV)')
        if not check:
            write_text(gj, s2)
    # 2) ссылки на assets/* в страницах
    ver = {}
    for name in sorted(os.listdir(os.path.join(ROOT, 'assets'))):
        if name.endswith(('.js', '.css')):
            ver[name] = h8(read(os.path.join(ROOT, 'assets', name)))
    for name in sorted(os.listdir(ROOT)):
        if not name.endswith('.html'):
            continue
        p = os.path.join(ROOT, name)
        s = read(p).decode('utf-8')
        s2 = s
        for a, hv in ver.items():
            s2 = re.sub(r'(["\'])assets/' + re.escape(a) + r'(\?v=[0-9a-f]*)?\1', r'\1assets/' + a + '?v=' + hv + r'\1', s2)
        if s2 != s:
            stale.append(name)
            if not check:
                write_text(p, s2)
    if check:
        if stale:
            print('штампы устарели: ' + ', '.join(stale) + ' — запустить python tools/docs_stamp.py')
            return 1
        print('штампы свежие: ' + ', '.join(a + '=' + hv for a, hv in ver.items()) + ', img=' + v)
        return 0
    print('проставлено: ' + (', '.join(stale) if stale else 'ничего — всё уже свежее'))
    print('версии: ' + ', '.join(a + '=' + hv for a, hv in ver.items()) + ', img=' + v)
    return 0


if __name__ == '__main__':
    sys.exit(main())
