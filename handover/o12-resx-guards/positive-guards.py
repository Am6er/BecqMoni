# -*- coding: utf-8 -*-
u"""Положительный контроль трёх починок сторожей resx (полоса О12, 05.09.2026).

Проверяется НЕ «сторож молчит на дереве» — молчание ничего не доказывает, — а
что сторож ОТКАЗЫВАЕТ на порче и ПРОПУСКАЕТ исправное. Порча наносится только
в КОПИЯХ, собранных во временном каталоге; дерево не трогается ни разу.

Три предмета:

* `T230` — формат файла: BOM + CRLF. Копия нормального `.resx` переводится в LF,
  четыре сторожа обязаны отказать словами «ФОРМАТ …»; возврат в CRLF побайтово —
  код 0.
* `T228` — `check_resx_designer.py` разбирает вызовы обёрток по ТЕКСТУ ЦЕЛИКОМ.
  Многострочный вызов с отсутствующим ключом обязан быть НАЗВАН; рядом
  показывается, что ПРЕЖНИЙ построчный разбор его не видел вовсе.
* `T229` — `check_resx.py` считает пустое значение в `*.ru.resx` непереведённой
  строкой. Три плеча: значение пусто (отказ), значение есть (пропуск), ключ снят
  (по-прежнему отказ — не сломано то, что работало).

    python handover/o12-resx-guards/positive-guards.py

Возвращает 0, если ВСЕ плечи легли как ожидалось.
"""
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
TOOLS = os.path.join(REPO, 'tools')
APP = os.path.join(REPO, 'BecquerelMonitor')
BOM = b'\xef\xbb\xbf'

sys.path.insert(0, TOOLS)
import check_resx_designer as designer  # noqa: E402

GUARDS = ('check_resx', 'check_resx_letters', 'check_resx_designer', 'check_resx_zorder')

RESULTS = []


def run(guard, root, extra=()):
    u"""Запустить сторожа на дереве `root`. Возвращает (код, вывод)."""
    if guard == 'check_resx_zorder':
        argv = [sys.executable, os.path.join(TOOLS, guard + '.py'), '--root', root]
    else:
        argv = [sys.executable, os.path.join(TOOLS, guard + '.py'), '--list', root]
    env = dict(os.environ, PYTHONIOENCODING='utf-8')
    done = subprocess.run(list(argv) + list(extra), capture_output=True, env=env)
    text = done.stdout.decode('utf-8', 'replace') + done.stderr.decode('utf-8', 'replace')
    return done.returncode, text


def check(label, ok, detail=''):
    RESULTS.append((label, ok, detail))
    print(u'  %-6s %s%s' % (u'ЛЁГ' if ok else u'НЕ ЛЁГ', label, (u'   — ' + detail) if detail else u''))


def to_eol(path, crlf):
    u"""Побайтовый перевод файла в CRLF или в LF. BOM сохраняется как был."""
    with open(path, 'rb') as fh:
        data = fh.read()
    bom = data.startswith(BOM)
    body = data[len(BOM):] if bom else data
    body = body.replace(b'\r\n', b'\n').replace(b'\r', b'\n')
    if crlf:
        body = body.replace(b'\n', b'\r\n')
    with open(path, 'wb') as fh:
        fh.write((BOM if bom else b'') + body)


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'wb') as fh:
        fh.write(BOM + text.replace('\r\n', '\n').replace('\n', '\r\n').encode('utf-8'))


RESX_HEAD = u'''<?xml version="1.0" encoding="utf-8"?>
<root>
  <xsd:schema id="root" xmlns="" xmlns:xsd="http://www.w3.org/2001/XMLSchema"
              xmlns:msdata="urn:schemas-microsoft-com:xml-msdata">
    <xsd:element name="root" msdata:IsDataSet="true" />
  </xsd:schema>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter</value></resheader>
'''


def resx(pairs):
    u"""Текст `.resx` из [(имя, значение)]. `None` вместо значения — пустое."""
    body = u''
    for name, value in pairs:
        body += u'  <data name="%s" xml:space="preserve">\n    <value>%s</value>\n  </data>\n' % (
            name, value if value is not None else u'')
    return RESX_HEAD + body + u'</root>\n'


# ---------------------------------------------------------------------------
# T230 — формат файла
# ---------------------------------------------------------------------------

def arm_format(work):
    print(u'\n=== T230: формат файла (BOM + CRLF) — копия нормального resx ===')
    root = os.path.join(work, 't230', 'BecquerelMonitor')
    os.makedirs(root)
    for suffix in ('.resx', '.ru.resx', '.Designer.cs'):
        shutil.copyfile(os.path.join(APP, 'ObsidianDeviceForm' + suffix),
                        os.path.join(root, 'ObsidianDeviceForm' + suffix))
    target = os.path.join(root, 'ObsidianDeviceForm.resx')
    to_eol(target, crlf=True)          # копия дерева могла приехать любой
    before = open(target, 'rb').read()

    print(u'\n-- плечо 0: нетронутая копия (BOM + CRLF)')
    for guard in GUARDS:
        code, text = run(guard, root)
        check(u'%s на нетронутой копии код 0' % guard, code == 0,
              u'код %d' % code)

    print(u'\n-- плечо 1: тот же файл переведён в LF')
    to_eol(target, crlf=False)
    for guard in GUARDS:
        code, text = run(guard, root)
        named = 'ObsidianDeviceForm.resx' in text and u'ФОРМАТ' in text
        check(u'%s отказал и НАЗВАЛ файл' % guard, code == 1 and named,
              u'код %d, назвал %s' % (code, named))
    code, text = run('check_resx', root, extra=('--no-format',))
    check(u'ключ --no-format снимает плечо формата', code == 0, u'код %d' % code)

    print(u'\n-- плечо 2: возврат в CRLF побайтово')
    to_eol(target, crlf=True)
    same = open(target, 'rb').read() == before
    check(u'файл вернулся ПОБАЙТОВО тем же', same)
    for guard in GUARDS:
        code, text = run(guard, root)
        check(u'%s снова код 0' % guard, code == 0, u'код %d' % code)


# ---------------------------------------------------------------------------
# T228 — многострочный вызов обёртки
# ---------------------------------------------------------------------------

WRAP_CS = u'''using BecquerelMonitor.Properties;

namespace Probe
{
    static class Wrap
    {
        public static string Text(string key, string fallback)
        {
            string value = Resources.ResourceManager.GetString(key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
    }
}
'''


def use_cs(multiline_key, single_key):
    return u'''namespace Probe
{
    class Use
    {
        public string Single()
        {
            return Wrap.Text("%s", "fallback one");
        }

        public string Multi(double x)
        {
            return string.Format(
                Wrap.Text(
                    "%s",
                    "fallback two: {0}"),
                x);
        }
    }
}
''' % (single_key, multiline_key)


def old_one_step(root):
    u"""ПРЕЖНИЙ разбор (до `T228`): построчно и без вложенных скобок."""
    wraps = designer.wrappers(root)
    found = []
    calls = [(re.compile(r'\b%s\s*\(([^()]*)\)' % re.escape(name)), name, pos)
             for name, pos in sorted(wraps.items())]
    for is_designer in (True, False):
        for path in designer.sources(root, designer=is_designer):
            for line in designer.read(path).replace('\r\n', '\n').split('\n'):
                for pattern, name, pos in calls:
                    for args in pattern.findall(line):
                        parts = designer.split_args(args)
                        if pos < len(parts):
                            lit = re.match(r'^\s*"([^"]*)"\s*$', parts[pos])
                            if lit:
                                found.append(lit.group(1))
    return found


def arm_designer(work):
    print(u'\n=== T228: вызов обёртки, разнесённый на строки ===')
    root = os.path.join(work, 't228', 'BecquerelMonitor')
    props = os.path.join(root, 'Properties')
    os.makedirs(props)
    write(os.path.join(props, 'Resources.resx'),
          resx([(u'ProbeKeyPresent', u'Present'), (u'ProbeKeyAlsoPresent', u'Also present')]))
    write(os.path.join(props, 'Resources.ru.resx'),
          resx([(u'ProbeKeyPresent', u'Есть'), (u'ProbeKeyAlsoPresent', u'Тоже есть')]))
    write(os.path.join(root, 'Wrap.cs'), WRAP_CS)
    use = os.path.join(root, 'Use.cs')

    print(u'\n-- плечо 0: оба вызова с ЖИВЫМИ ключами')
    write(use, use_cs(u'ProbeKeyAlsoPresent', u'ProbeKeyPresent'))
    code, text = run('check_resx_designer', root)
    check(u'исправное дерево — код 0', code == 0, u'код %d' % code)
    step, _w, _m = designer.one_step(root)
    check(u'многострочный вызов ВИДЕН новым разбором',
          any(k == u'ProbeKeyAlsoPresent' for _p, _n, k, _v in step),
          u'разобрано литералов: %d' % len(step))

    print(u'\n-- плечо 1: МНОГОСТРОЧНЫЙ вызов с отсутствующим ключом')
    write(use, use_cs(u'ProbeKeyMissingMultiline', u'ProbeKeyPresent'))
    code, text = run('check_resx_designer', root)
    named = u'ProbeKeyMissingMultiline' in text
    check(u'сторож отказал и НАЗВАЛ ключ', code == 1 and named,
          u'код %d, назвал %s' % (code, named))
    old = old_one_step(root)
    check(u'ПРЕЖНИЙ построчный разбор его НЕ видел',
          u'ProbeKeyMissingMultiline' not in old,
          u'прежний разбор нашёл: %s' % (sorted(set(old)) or u'ничего'))
    check(u'однострочный живой ключ пропущен (нет ложной тревоги)',
          u'ProbeKeyPresent' not in text)

    print(u'\n-- плечо 2: ОДНОСТРОЧНЫЙ вызов с отсутствующим ключом (не сломано старое)')
    write(use, use_cs(u'ProbeKeyAlsoPresent', u'ProbeKeyMissingSingle'))
    code, text = run('check_resx_designer', root)
    check(u'сторож отказал и НАЗВАЛ ключ', code == 1 and u'ProbeKeyMissingSingle' in text,
          u'код %d' % code)

    print(u'\n-- плечо 3: возврат к исправному дереву')
    write(use, use_cs(u'ProbeKeyAlsoPresent', u'ProbeKeyPresent'))
    code, text = run('check_resx_designer', root)
    check(u'снова код 0', code == 0, u'код %d' % code)


# ---------------------------------------------------------------------------
# T229 — опустошённый перевод
# ---------------------------------------------------------------------------

def arm_blank(work):
    print(u'\n=== T229: опустошённый перевод в *.ru.resx ===')
    root = os.path.join(work, 't229', 'BecquerelMonitor')
    os.makedirs(root)
    en = os.path.join(root, 'ProbeForm.resx')
    ru = os.path.join(root, 'ProbeForm.ru.resx')
    write(en, resx([(u'labelProbe.Text', u'Probe label'), (u'buttonProbe.Text', u'Probe button')]))

    print(u'\n-- плечо 0: полная пара')
    write(ru, resx([(u'labelProbe.Text', u'Подпись пробы'), (u'buttonProbe.Text', u'Кнопка пробы')]))
    code, text = run('check_resx', root)
    check(u'полная пара — код 0', code == 0, u'код %d' % code)

    print(u'\n-- плечо 1: значение ключа ОПУСТОШЕНО')
    write(ru, resx([(u'labelProbe.Text', None), (u'buttonProbe.Text', u'Кнопка пробы')]))
    code, text = run('check_resx', root)
    named = u'пусто в ru: labelProbe.Text' in text
    check(u'сторож отказал и назвал ключ как ПУСТОЙ', code == 1 and named,
          u'код %d, назвал %s' % (code, named))
    check(u'счётчик пустых значений не ноль', u'значение пусто: 1' in text)

    print(u'\n-- плечо 2: тот же ключ с текстом')
    write(ru, resx([(u'labelProbe.Text', u'Подпись пробы'), (u'buttonProbe.Text', u'Кнопка пробы')]))
    code, text = run('check_resx', root)
    check(u'пропуск, код 0', code == 0, u'код %d' % code)

    print(u'\n-- плечо 3: ключ СНЯТ (то, что работало и раньше)')
    write(ru, resx([(u'buttonProbe.Text', u'Кнопка пробы')]))
    code, text = run('check_resx', root)
    check(u'сторож отказал и назвал ключ как ОТСУТСТВУЮЩИЙ',
          code == 1 and u'нет в ru: labelProbe.Text' in text, u'код %d' % code)

    print(u'\n-- плечо 4: значение из одних пробелов — тоже не перевод')
    write(ru, resx([(u'labelProbe.Text', u'   '), (u'buttonProbe.Text', u'Кнопка пробы')]))
    code, text = run('check_resx', root)
    check(u'сторож отказал', code == 1 and u'пусто в ru: labelProbe.Text' in text,
          u'код %d' % code)


def main():
    work = tempfile.mkdtemp(prefix='o12-guards-')
    print(u'временное дерево: %s' % work)
    try:
        arm_format(work)
        arm_designer(work)
        arm_blank(work)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    bad = [label for label, ok, _d in RESULTS if not ok]
    print(u'\n=== ИТОГ: плеч %d, легло %d, не легло %d ==='
          % (len(RESULTS), len(RESULTS) - len(bad), len(bad)))
    for label in bad:
        print(u'   НЕ ЛЁГ: %s' % label)
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
