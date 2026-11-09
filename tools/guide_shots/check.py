# -*- coding: utf-8 -*-
u"""Счётная сверка гида `docs/` и снимков конвейера (`AMBER319`, 08.11.2026).

  python -I tools/guide_shots/check.py [--shots=<каталог снимков>] [--repo=<корень>]

Без `--shots` — сверка страниц гида; с ним — только тексты окон свежей съёмки.

Страницы (`docs/*.html`):
  * каждый `data-shot` есть в обоих языках (`docs/img/ru`, `docs/img/en`);
  * прямоугольники `data-hot` (`-ru`, `-en`) — доли кадра в пределах [0, 1];
  * число точек `data-hot` равно числу пунктов легенды (своей `ol.shot-legend` фигуры или
    списка по `data-legend`); фигура без найденной легенды — предупреждение;
  * в русских текстах нет слов, снятых правилами Amber: «ППР», «ПШПВ», «гайд», «ввоз»;
  * личного нет: метки настоящего конфига (`make_copy.py tokens`: имена приборов, серийные,
    файлы фона, названия кривых, своих веществ и шаблонов), пути профиля, серийные номера
    RadiaCode, порты кроме COM99;
  * ключи русского и английского словаря страницы совпадают — ПРЕДУПРЕЖДЕНИЕ, не отказ: часть
    ключей страницы собирает шаблонами в коде, и разница там законна (21 такой на 07.11.2026).

Съёмка (`--shots`): в текстах окна (`<сцена>.txt`) и журналах пробы (`<сцена>.log`) обоих
языков нет личных меток. Надписи, нарисованные на графике, в тексты окна не попадают — снимок
всё равно смотреть глазами.

Код 0 — отказов нет; 1 — есть отказы (перечислены).
"""
import argparse
import glob
import io
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
BANNED_RU = [u'ППР', u'ПШПВ', u'гайд', u'ввоз', u'ввез', u'ввёз']
GENERIC = [
    (re.compile(r'[A-Za-z]:\\Users\\', re.I), u'путь профиля'),
    (re.compile(r'RC-\d{3}-(?!000000)\d{6}'), u'серийный номер RadiaCode'),
    (re.compile(r'\bCOM(?!99\b)\d+\b'), u'порт'),
    (re.compile(r'YandexDisk|OneDrive|Google Drive|Dropbox', re.I), u'папка облачного диска'),
]
KIND_WORD = {u'name': u'имя прибора', u'serial': u'серийный номер или адрес BLE',
             u'port': u'порт настоящего конфига', u'file': u'имя файла фона', u'user': u'имя профиля',
             u'curve': u'название кривой', u'material': u'своё вещество', u'template': u'шаблон'}
# Названия кривых, веществ и шаблонов из одного слова — обычные слова окна
# (тип источника, подписи), по ним искать нельзя: ищутся названия с пробелом или цифрой.
NAMED_KINDS = (u'curve', u'material', u'template')
DOC_KINDS = (u'name', u'serial', u'file', u'user') + NAMED_KINDS
ALL_KINDS = DOC_KINDS + (u'port',)


def read(path):
    with io.open(path, u'r', encoding=u'utf-8-sig') as f:
        return f.read()


def tokens(repo):
    u"""(вид, метка) настоящего конфига: name, serial, port, file, user, curve, material, template
    (`make_copy.py tokens`; строится при каждом запуске из `%AppData%`, в дерево не пишется)."""
    out = subprocess.run([sys.executable, u'-I', os.path.join(HERE, u'make_copy.py'), u'tokens', u'--repo=' + repo],
                         stdout=subprocess.PIPE, env=dict(os.environ, PYTHONIOENCODING=u'utf-8'))
    if out.returncode != 0:
        sys.exit(u'make_copy.py tokens: код %d' % out.returncode)
    return [tuple(t.split(u'\t', 1)) for t in out.stdout.decode(u'utf-8').splitlines() if u'\t' in t]


def personal(text, toks, kinds):
    u"""Найденное личное — словами, без самих меток (их нельзя печатать в журнал)."""
    found = []
    low = text.lower()
    # Порты: пункты закрытого выпадающего списка («пункт: COM…») на снимке не видны — это
    # перечень портов машины (`SerialPort.GetPortNames`), а не порт прибора. Порт, который
    # окно выбрало и в который пошло бы обращение, стоит в строке самого списка.
    ports_text = u'\n'.join(l for l in text.splitlines() if not l.lstrip().startswith(u'пункт:'))
    for kind, t in toks:
        if kind not in kinds:
            continue
        if kind in NAMED_KINDS and not re.search(r'[\s\d]', t):
            continue
        if kind == u'port':
            if re.search(r'(?<![\w])%s(?![\w])' % re.escape(t), ports_text):
                found.append(u'%s (%d знаков)' % (KIND_WORD[kind], len(t)))
            continue
        if t.isdigit():
            # короткий номер прибора в тексте гида — это числа данных (отсчёты, кэВ), не номер;
            # на снимке копии номера быть не может вовсе — там он и ищется
            if len(t) < 6 and u'port' not in kinds:
                continue
            hit = re.search(r'(?<![\w.])%s(?![\w])' % re.escape(t), text)
        else:
            hit = t.lower() in low
        if hit:
            found.append(u'%s (%d знаков)' % (KIND_WORD[kind], len(t)))
    for rx, what in GENERIC:
        # порт в тексте гида — пример, не личное; на снимке копии он обязан быть COM99
        if what == u'порт':
            if u'port' in kinds and rx.search(ports_text):
                found.append(what)
            continue
        if rx.search(text):
            found.append(what)
    return found


def dict_keys(block):
    return set(re.findall(r"'([A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)+)'\s*:", block))


def check_pages(repo, toks, errors, warnings):
    docs = os.path.join(repo, u'docs')
    pages = sorted(glob.glob(os.path.join(docs, u'*.html')))
    shots = 0
    for page in pages:
        name = os.path.basename(page)
        html = read(page)
        for p in personal(html, toks, DOC_KINDS):
            errors.append(u'%s: личное — %s' % (name, p))
        m = re.search(r'GUIDE\.texts\(\{ru:\{(.*?)\},en:\{(.*)\}\}\)', html, re.S)
        if m:
            ru, en = m.group(1), m.group(2)
            for w in BANNED_RU:
                if re.search(w, ru, re.I):
                    errors.append(u'%s: в русском тексте «%s»' % (name, w))
            kr, ke = dict_keys(ru), dict_keys(en)
            if kr != ke:
                warnings.append(u'%s: ключи RU без EN %s; EN без RU %s' % (
                    name, sorted(kr - ke)[:8], sorted(ke - kr)[:8]))
        for fig in re.finditer(r'<figure\b([^>]*)>(.*?)</figure>', html, re.S):
            attrs, body = fig.group(1), fig.group(2)
            shot = re.search(r'data-shot="([^"]+)"', attrs)
            if not shot:
                continue
            shots += 1
            sid = shot.group(1)
            for lang in (u'ru', u'en'):
                if not os.path.isfile(os.path.join(docs, u'img', lang, sid + u'.png')):
                    errors.append(u'%s: снимка нет — img/%s/%s.png' % (name, lang, sid))
            legend = None
            lid = re.search(r'data-legend="([^"]+)"', attrs)
            if lid:
                lm = re.search(r'id="%s"[^>]*>(.*?)</ol>' % re.escape(lid.group(1)), html, re.S)
                legend = len(re.findall(r'<li\b', lm.group(1))) if lm else None
            else:
                lm = re.search(r'<ol class="shot-legend[^"]*"[^>]*>(.*?)</ol>', body, re.S)
                if not lm:
                    # как guide.js: список легенды рядом с фигурой в той же сетке, вне фигур —
                    # здесь: первый после фигуры, до следующей фигуры или конца раздела
                    rest = html[fig.end():]
                    stop = min([i for i in (rest.find(u'<figure'), rest.find(u'</section>')) if i >= 0] or [len(rest)])
                    lm = re.search(r'<ol class="shot-legend[^"]*"[^>]*>(.*?)</ol>', rest[:stop], re.S)
                legend = len(re.findall(r'<li\b', lm.group(1))) if lm else None
            for hot in re.finditer(r"data-hot(-ru|-en)?='([^']*)'", attrs):
                try:
                    rects = json.loads(hot.group(2))
                except ValueError:
                    errors.append(u'%s: %s — data-hot%s не читается' % (name, sid, hot.group(1) or u''))
                    continue
                for r in rects:
                    x, y, w, h = r[:4]
                    if not (0 <= x <= 1 and 0 <= y <= 1 and w > 0 and h > 0 and x + w <= 1.0005 and y + h <= 1.0005):
                        errors.append(u'%s: %s — прямоугольник вне кадра %s' % (name, sid, r))
                if legend is None:
                    warnings.append(u'%s: %s — легенда не найдена, точек %d' % (name, sid, len(rects)))
                elif legend != len(rects):
                    errors.append(u'%s: %s — точек %d, пунктов легенды %d' % (name, sid, len(rects), legend))
    print(u'страниц %d, фигур со снимком %d' % (len(pages), shots))


def check_shots(root, toks, errors):
    n = 0
    for path in sorted(glob.glob(os.path.join(root, u'*', u'*.txt')) + glob.glob(os.path.join(root, u'*', u'*.log'))):
        n += 1
        for p in personal(read(path), toks, ALL_KINDS):
            errors.append(u'%s: личное — %s' % (os.path.relpath(path, root), p))
    print(u'текстов окна и журналов съёмки: %d' % n)
    if n == 0:
        errors.append(u'в %s нет ни одного текста окна — сверять нечего' % root)


def main():
    try:
        sys.stdout.reconfigure(encoding=u'utf-8')
        sys.stderr.reconfigure(encoding=u'utf-8')
    except AttributeError:
        pass
    p = argparse.ArgumentParser()
    p.add_argument(u'--shots', default=u'')
    p.add_argument(u'--repo', default=os.path.normpath(os.path.join(HERE, u'..', u'..')))
    a = p.parse_args()
    toks = tokens(a.repo)
    print(u'личных меток настоящего конфига: %d (сами метки не печатаются)' % len(toks))
    errors, warnings = [], []
    if a.shots:
        check_shots(a.shots, toks, errors)
    else:
        check_pages(a.repo, toks, errors, warnings)
    for w in warnings:
        print(u'предупреждение: ' + w)
    for e in errors:
        print(u'ОТКАЗ: ' + e)
    print(u'итог: отказов %d, предупреждений %d' % (len(errors), len(warnings)))
    return 1 if errors else 0


if __name__ == u'__main__':
    sys.exit(main())
