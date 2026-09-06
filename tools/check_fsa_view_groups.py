# -*- coding: utf-8 -*-
u"""Переключатель окна отчёта FSA — в группе СВОЕГО РОДА (`A265`).

## Откуда взялась

До 06.09.2026 отрисовочная галка «Невязка модели» (`residualBandCheckBox`)
стояла шестой в ряду ПЯТИ РАСЧЁТНЫХ, внутри одной с ними группы
«Дополнительные компоненты модели». Пять из шести меняли расчёт и вели к
пересчёту разложения, шестая правила только показ, и различала их
ЕДИНСТВЕННО подсказка при наведении.

Цена смешения уже уплачена одним заходом: вопрос ~~`A264`~~ («приводит ли
отключение сумм-пиков к вычету их из `counts`») родился именно из него —
«отключение сумм-пиков» прочиталось как отрисовочное, тогда как галка
`Каскадное суммирование` расчётная и числа при ней меняются законно.

Решением Amber 06.09.2026 отрисовочные вынесены в отдельную группу
«Отрисовка» внизу окна, и граница обязана быть видна БЕЗ НАВЕДЕНИЯ. Здесь
читатель этого решения: без него следующая галка ляжет в первую попавшуюся
группу, и заметить это будет опять нечем.

## ⛔ Род переключателя судится ПО ФАКТУ, а не по списку имён

Список имён пришлось бы держать в согласии с окном руками, и он разошёлся бы
молча — ровно та беда, ради которой сторож заведён. Здесь род выводится из
кода:

* `Foo.Designer.cs` даёт дерево — кто в каком потоке, поток в какой рамке —
  и обработчик каждого переключателя (`X.CheckedChanged += … (this.H)`);
* `Foo.cs` даёт тело обработчика: идёт ли он через `ApplyCalculationChange`
  — единственную дверь, которой окно пишет настройки разбора (она правит
  копию конфигурации спектра, умолчание прибора и обесценивает кэш сеанса).

Прошёл через эту дверь — РАСЧЁТНЫЙ; не прошёл — отрисовочный.

⚠ Это ВТОРОЙ, независимый признак: проба `FsaViewGroupsProbe` судит тот же
вопрос иначе — переключает элемент в построенном окне и смотрит, изменился ли
ОТПЕЧАТОК настроек. Признаки нарочно разные: статический ловит правку, ещё не
собранную, живой — расхождение кода с тем, что окно делает на самом деле.

## Что проверяется

1. группа показа (`displayGroupBox`) в окне ЕСТЬ — иначе судить нечем;
2. каждый расчётный переключатель лежит ВНЕ неё;
3. каждый отрисовочный лежит В НЕЙ;
4. у каждого переключателя есть обработчик — переключатель без обработчика
   рода не имеет и молча не судится, а называется поимённо;
5. подписи группы показа есть в ОБОИХ `.resx` (английский первичен).

## Самопроверка

⛔ Проверка «все на местах» проходит и на пустом списке, поэтому на каждом
прогоне сторож судит ещё и ПОРЧЕНУЮ копию исходника, где одна галка
переложена в чужую группу: он обязан назвать её поимённо. Не назвал —
сторож красный, что бы ни показало настоящее дерево.

Коды возврата: 0 — сошлось; 1 — не сошлось; 2 — нечего читать.
"""
import io
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DESIGNER = os.path.join(REPO, 'BecquerelMonitor', 'FSAReportView.Designer.cs')
CODE = os.path.join(REPO, 'BecquerelMonitor', 'FSAReportView.cs')
RESX = os.path.join(REPO, 'BecquerelMonitor', 'FSAReportView.resx')
RESX_RU = os.path.join(REPO, 'BecquerelMonitor', 'FSAReportView.ru.resx')

DISPLAY_GROUP = u'displayGroupBox'
CALC_DOOR = u'ApplyCalculationChange'

SWITCH_TYPES = (u'System.Windows.Forms.CheckBox', u'System.Windows.Forms.RadioButton')

RE_NEW = re.compile(u'this\\.(\\w+) = new ([\\w.]+)\\(')
RE_ADD = re.compile(u'this\\.(\\w+)\\.Controls\\.Add\\(this\\.(\\w+)\\)')
RE_FORM_ADD = re.compile(u'^\\s*this\\.Controls\\.Add\\(this\\.(\\w+)\\)', re.M)
RE_HANDLER = re.compile(u'this\\.(\\w+)\\.CheckedChanged \\+= new System\\.EventHandler\\(this\\.(\\w+)\\)')


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


def parse_designer(text):
    u"""(типы, родитель, обработчик) по designer-коду."""
    kinds = dict(RE_NEW.findall(text))
    parent = {}
    for owner, child in RE_ADD.findall(text):
        parent[child] = owner
    for child in RE_FORM_ADD.findall(text):
        parent.setdefault(child, u'$this')
    handler = dict(RE_HANDLER.findall(text))
    return kinds, parent, handler


def group_of(name, kinds, parent):
    u"""Ближайшая рамка-группа вверх по дереву; None — её нет."""
    seen = set()
    up = parent.get(name)
    while up is not None and up not in seen:
        seen.add(up)
        if kinds.get(up) == u'System.Windows.Forms.GroupBox':
            return up
        up = parent.get(up)
    return None


def method_body(text, name):
    u"""Тело метода-обработчика по его имени; None — метода нет."""
    head = re.search(u'\\bvoid %s\\s*\\(' % re.escape(name), text)
    if head is None:
        return None
    start = text.find(u'{', head.end())
    if start < 0:
        return None
    depth = 0
    for i in range(start, len(text)):
        if text[i] == u'{':
            depth += 1
        elif text[i] == u'}':
            depth -= 1
            if depth == 0:
                return text[start:i + 1]
    return None


def judge(designer_text, code_text):
    u"""[(имя, группа, расчётный?, беда)] — беда пуста, если всё ладно."""
    kinds, parent, handler = parse_designer(designer_text)
    rows = []
    for name in sorted(kinds):
        if kinds[name] not in SWITCH_TYPES:
            continue
        group = group_of(name, kinds, parent)
        hook = handler.get(name)
        if hook is None:
            rows.append((name, group, None, u'обработчика CheckedChanged нет — род не определить'))
            continue
        body = method_body(code_text, hook)
        if body is None:
            rows.append((name, group, None, u'обработчик %s не найден в FSAReportView.cs' % hook))
            continue
        calc = CALC_DOOR in body
        trouble = u''
        if group is None:
            trouble = u'лежит вне рамок-групп'
        elif calc and group == DISPLAY_GROUP:
            trouble = u'РАСЧЁТНЫЙ (через %s), а лежит в группе показа «%s»' % (CALC_DOOR, group)
        elif not calc and group != DISPLAY_GROUP:
            trouble = u'отрисовочный, а лежит в расчётной группе «%s»' % group
        rows.append((name, group, calc, trouble))
    return rows, kinds


def spoil(designer_text):
    u"""Порченая копия: отрисовочная галка переложена в расчётный поток."""
    line = u'            this.displayFlow.Controls.Add(this.residualBandCheckBox);\n'
    if line not in designer_text:
        return None
    moved = designer_text.replace(line, u'', 1)
    anchor = u'            this.extrasFlow.Controls.Add(this.pileUpCheckBox);\n'
    if anchor not in moved:
        return None
    return moved.replace(
        anchor, anchor + u'            this.extrasFlow.Controls.Add(this.residualBandCheckBox);\n', 1)


def resx_has(path, key):
    return (u'<data name="%s"' % key) in read(path)


def main():
    for path in (DESIGNER, CODE, RESX, RESX_RU):
        if not os.path.isfile(path):
            print(u'⛔ нет файла: %s' % path)
            return 2

    designer_text = read(DESIGNER)
    code_text = read(CODE)
    rows, kinds = judge(designer_text, code_text)

    print(u'=== переключатели окна отчёта FSA: род против группы (A265) ===')
    if kinds.get(DISPLAY_GROUP) != u'System.Windows.Forms.GroupBox':
        print(u'⛔ группы показа «%s» в окне НЕТ — судить нечем' % DISPLAY_GROUP)
        return 1

    bad = 0
    calc = 0
    print(u'  %-24s %-18s %s' % (u'поле', u'группа', u'род'))
    for name, group, is_calc, trouble in rows:
        rod = u'РАСЧЁТНЫЙ' if is_calc else (u'отрисовочный' if is_calc is not None else u'?')
        print(u'  %-24s %-18s %s' % (name, group or u'(вне групп)', rod))
        if is_calc:
            calc += 1
        if trouble:
            print(u'  ⛔ %s: %s' % (name, trouble))
            bad += 1
    print(u'  всего %d: расчётных %d, отрисовочных %d' % (len(rows), calc, len(rows) - calc))
    if not rows:
        print(u'⛔ переключателей не нашлось ВОВСЕ — сторож смотрит не туда')
        bad += 1

    print()
    print(u'=== подписи группы показа ===')
    for path, what in ((RESX, u'английская'), (RESX_RU, u'русская')):
        key = DISPLAY_GROUP + u'.Text'
        ok = resx_has(path, key)
        print(u'  %-12s %s: %s' % (what, key, u'есть' if ok else u'НЕТ'))
        if not ok:
            bad += 1

    print()
    print(u'=== САМОПРОВЕРКА: порченая копия обязана быть названа ===')
    spoiled = spoil(designer_text)
    if spoiled is None:
        print(u'⛔ порчу подставить не удалось — самопроверка не состоялась')
        bad += 1
    else:
        caught = [n for n, _g, _c, t in judge(spoiled, code_text)[0]
                  if t and n == u'residualBandCheckBox']
        others = [n for n, _g, _c, t in judge(spoiled, code_text)[0] if t]
        if caught and others == caught:
            print(u'  подставлено: residualBandCheckBox → extrasFlow; названо поимённо, лишнего нет')
        else:
            print(u'  ⛔ САМОПРОВЕРКА ПРОВАЛЕНА: названо %s' % (u', '.join(others) or u'ничего'))
            bad += 1

    print()
    if bad:
        print(u'НЕ СОШЛОСЬ: %d' % bad)
        return 1
    print(u'СОШЛОСЬ: каждый переключатель в группе своего рода')
    return 0


if __name__ == '__main__':
    sys.exit(main())
