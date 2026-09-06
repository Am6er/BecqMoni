# -*- coding: utf-8 -*-
u"""Слагаемые высоты панели выделения — договор отрисовки и мерки (`A273`).

## Откуда взялась

Высоту серой панели выделения отрисовка (`EnergySpectrumView.cs`) считает
слагаемыми: подпись линии 16 px, предупреждение о споре подписи 16 px,
строка «Activity Bq: no K» с переносами отказа, отступ 6 px перед блоком
ПШПВ при Lc = 0. Ту же высоту ЖДЁТ проба `SelectionPanelProbeG10`, и ждёт
она её ВТОРОЙ КОПИЕЙ той же арифметики.

Копия разошлась. 06.09.2026 у мерки не было слагаемого «спор»: на сцене без
спора подписи (`ASN16_Cs137`) она проходила, а на сцене со спором
(`G1S16_Th228_P5`, подпись «Pb-212» 238.00 кэВ, соперников 3) отвергала
ВЕРНУЮ отрисовку — «8 НЕ СОШЛОСЬ», код 1, панель росла на 86 px против
ожидаемых 70. Отказ никто не читал полдня, а сама отрисовка была права.

## Что судится

Договор простой: каждое слагаемое высоты, ЗАВИСЯЩЕЕ от подписи и отказа
(то есть то, что исчезает у молчащей панели), в обоих файлах помечено
клеймом `// ПАНЕЛЬ: <имя>`, и наборы имён обязаны совпадать.

  * в `BecquerelMonitor/EnergySpectrumView.cs` — от строки
    `bool activityRefused =` до заливки `g.FillRectangle(Brushes.DarkGray`:
    КАЖДОЕ `infopanel_height += …;` обязано нести клеймо;
  * в `tools/effmaker/probes/SelectionPanelProbeG10.cs` — в выражении
    `int grow = …` и подготовке к нему: те же имена, столько же.

Сторож НЕ считает пиксели: числа судит проба кадром. Здесь судится ровно то,
чего проба увидеть не может, — что слагаемое, заведённое в отрисовке, не
осталось без пары в мерке.

  python tools/check_selection_panel_height.py [--selftest]

Коды возврата:
  0 — наборы клейм совпали, у каждого слагаемого отрисовки есть клеймо;
  1 — договор нарушен (расхождение названо поимённо);
  2 — самопроверка не прошла: сторож слеп к подставленной порче;
  3 — судить нечего (файла нет либо область не найдена).
"""

import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VIEW = os.path.join(ROOT, u'BecquerelMonitor', u'EnergySpectrumView.cs')
PROBE = os.path.join(ROOT, u'tools', u'effmaker', u'probes', u'SelectionPanelProbeG10.cs')

# Область отрисовки: слагаемые, зависящие от подписи и отказа, лежат между
# объявлением `activityRefused` и заливкой панели. Всё, что выше (база 104,
# 46, 88/72, 48, 54), от состояния подписи не зависит и здесь не судится.
VIEW_FROM = u'bool activityRefused ='
VIEW_TILL = u'g.FillRectangle(Brushes.DarkGray'

# Область мерки пробы: от подготовки слагаемого спора до конца выражения роста.
PROBE_FROM = u'int rivalRow ='
PROBE_TILL = u'Same("панель с отказом выше на "'

RE_TERM = re.compile(u'^\\s*infopanel_height \\+=([^;]*);(.*)$')
# ⛔ `re.M` НЕ ЛИШНИЙ: без него `$` значит «конец всего куска», и в области
# пробы находилось ОДНО последнее клеймо из четырёх — сторож при этом не падал,
# а печатал неверный разбор (поймано самопроверкой при заведении).
RE_MARK = re.compile(u'//\\s*ПАНЕЛЬ:\\s*([^\\r\\n(]+?)\\s*(?:\\(|$)', re.M)


def _utf8_console():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


_utf8_console()


def read(path):
    with io.open(path, 'r', encoding='utf-8-sig', newline='') as fh:
        return fh.read()


def region(text, start, end, what):
    u"""Кусок текста между двумя якорями; None — якоря не найдены."""
    i = text.find(start)
    if i < 0:
        print(u'⛔ %s: не найден якорь «%s»' % (what, start))
        return None
    j = text.find(end, i)
    if j < 0:
        print(u'⛔ %s: не найден якорь «%s»' % (what, end))
        return None
    return text[i:j]


def view_terms(text):
    u"""(имена клейм, слагаемых без клейма) по области отрисовки."""
    marks, naked = [], []
    for line in text.splitlines():
        m = RE_TERM.match(line)
        if not m:
            continue
        mark = RE_MARK.search(m.group(2))
        if mark is None:
            naked.append(u'infopanel_height +=%s;' % m.group(1).rstrip())
        else:
            marks.append(mark.group(1))
    return marks, naked


def probe_terms(text):
    return [m.group(1) for m in RE_MARK.finditer(text)]


def judge(view_text, probe_text, quiet=False):
    u"""0 — договор цел; 1 — нарушен; 3 — судить нечего. Печатает разбор."""
    say = (lambda *a: None) if quiet else (lambda s: sys.stdout.write(s + u'\n'))

    vr = region(view_text, VIEW_FROM, VIEW_TILL, u'EnergySpectrumView.cs')
    pr = region(probe_text, PROBE_FROM, PROBE_TILL, u'SelectionPanelProbeG10.cs')
    if vr is None or pr is None:
        return 3

    marks, naked = view_terms(vr)
    probe = probe_terms(pr)
    say(u'отрисовка: слагаемых высоты %d, из них с клеймом %d — %s'
        % (len(marks) + len(naked), len(marks), u', '.join(marks) or u'нет'))
    say(u'мерка пробы: клейм %d — %s' % (len(probe), u', '.join(probe) or u'нет'))

    bad = 0
    for term in naked:
        say(u'⛔ слагаемое высоты без клейма `// ПАНЕЛЬ: <имя>`: %s' % term)
        bad += 1

    only_view = [m for m in marks if m not in probe]
    only_probe = [m for m in probe if m not in marks]
    for m in only_view:
        say(u'⛔ слагаемое «%s» есть в отрисовке, а в мерке пробы его НЕТ '
            u'— проба будет отвергать верную панель' % m)
        bad += 1
    for m in only_probe:
        say(u'⛔ слагаемое «%s» есть в мерке пробы, а в отрисовке его НЕТ '
            u'— проба ждёт того, чего не рисуют' % m)
        bad += 1

    dup = [m for m in set(marks) if marks.count(m) > 1]
    for m in sorted(dup):
        say(u'⛔ клеймо «%s» в отрисовке стоит дважды — имена обязаны быть разными' % m)
        bad += 1

    if bad == 0:
        say(u'СОШЛОСЬ: %d слагаемых высоты, у каждого клеймо и пара в мерке пробы'
            % len(marks))
    return 1 if bad else 0


def selftest():
    u"""⛔ Сторож обязан ВИДЕТЬ порчу: судим порченые копии в памяти.

    Три порчи, по одной на каждое правило: слагаемое без клейма, слагаемое
    отрисовки без пары в пробе, слагаемое пробы без пары в отрисовке.
    """
    view = read(VIEW)
    probe = read(PROBE)
    if judge(view, probe, quiet=True) != 0:
        print(u'⛔ САМОПРОВЕРКА: дерево как есть уже не сходится — сначала почини его')
        return 2

    cases = [
        (u'слагаемое без клейма',
         view.replace(u'infopanel_height += 16; // ПАНЕЛЬ: подпись',
                      u'infopanel_height += 16;', 1), probe),
        (u'слагаемое отрисовки без пары в пробе',
         view, probe.replace(u'int rivalRow = rivals > 0 ? 16 : 0; // ПАНЕЛЬ: спор',
                             u'int rivalRow = rivals > 0 ? 16 : 0;', 1)),
        (u'слагаемое пробы без пары в отрисовке',
         view.replace(u'infopanel_height += 6; // ПАНЕЛЬ: отступ',
                      u'infopanel_height += 6; // ПАНЕЛЬ: отступа-нет', 1), probe),
    ]
    ok = True
    for name, v, p in cases:
        if v == view and p == probe:
            print(u'⛔ САМОПРОВЕРКА «%s»: порча не подставилась — образец не найден' % name)
            ok = False
            continue
        code = judge(v, p, quiet=True)
        print(u'  самопроверка «%s»: код %d %s' % (name, code, u'' if code == 1 else u'⛔'))
        if code != 1:
            ok = False
    if not ok:
        print(u'⛔ САМОПРОВЕРКА НЕ ПРОШЛА: сторож слеп к подставленной порче')
        return 2
    print(u'  САМОПРОВЕРКА ПРОШЛА: все три порчи названы')
    return 0


def main():
    selfcheck = '--selftest' in sys.argv[1:]
    for path in (VIEW, PROBE):
        if not os.path.exists(path):
            print(u'⛔ нет файла: %s' % path)
            return 3
    if selfcheck:
        return selftest()
    code = selftest()
    if code != 0:
        return code
    return judge(read(VIEW), read(PROBE))


if __name__ == '__main__':
    sys.exit(main())
