# -*- coding: utf-8 -*-
u"""Полоса F48: снять заслон и пометки — ОБРАТНЫЙ КОНТРОЛЬ приёмки.

Проверка, которая не отказывает на заведомо плохом входе, не проверяет
ничего. Заведомо плохой вход здесь — САМО ПРИЛОЖЕНИЕ БЕЗ ПРАВКИ, поэтому
скрипт вырезает три блока и складывает исходники в `mine\\`, а `--restore`
кладёт их обратно ПОБАЙТОВО.

    python strip_f48.py --strip      # снять (перед сборкой `Debug_F48_before`)
    python strip_f48.py --restore    # вернуть (сразу после сборки)

⛔ Возврат обязателен и в случае отказа сборки — зовите его из `finally`
   вызывающего скрипта, а не «потом руками».
⚠ Переводы строк и BOM у каждого файла свои и сохраняются как есть: читаем и
  пишем двоичным, разбивая на строки САМИ (питон иначе рвёт текст на одиночном
  CR и молча меняет CRLF).
"""
import os
import shutil
import sys

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MINE = os.path.join(REPO, u'handover', u'f48-s44-nan', u'mine')

# файл -> (метка начала блока, сколько блоков ждать)
BLOCKS = [
    (os.path.join(u'BecquerelMonitor', u'PolynomialEnergyCalibration.cs'),
     u'// Проверяются ВСЕ '
     u'коэффициенты'),
    (os.path.join(u'BecquerelMonitor', u'FullSpectrumAnalysis', u'FsaPresentationBuilder.cs'),
     u'if (result.BackgroundRejected != null)'),
    (os.path.join(u'BecquerelMonitor', u'FSAReportView.cs'),
     u'if (result.BackgroundRejected != null)'),
]


def split_lines(data):
    u"""Строки с их собственными концами, байтами: CRLF, LF и одиночный CR."""
    out = []
    start = 0
    i = 0
    n = len(data)
    while i < n:
        c = data[i:i + 1]
        if c == b'\n':
            out.append(data[start:i + 1])
            i += 1
            start = i
        elif c == b'\r':
            if data[i + 1:i + 2] == b'\n':
                out.append(data[start:i + 2])
                i += 2
            else:
                out.append(data[start:i + 1])
                i += 1
            start = i
        else:
            i += 1
    if start < n:
        out.append(data[start:])
    return out


def strip_block(path, marker):
    with open(path, 'rb') as f:
        data = f.read()
    lines = split_lines(data)
    text = [l.decode('utf-8') for l in lines]

    start = -1
    for i, line in enumerate(text):
        if marker in line:
            start = i
            break
    if start < 0:
        raise SystemExit(u'ОТКАЗ: метки «%s» нет в %s' % (marker, path))

    # Назад — до первой строки комментария блока.
    while start > 0 and text[start - 1].strip().startswith(u'//'):
        start -= 1

    # Вперёд — по скобкам, от первой строки с `if (`.
    j = start
    while u'if (' not in text[j]:
        j += 1
    depth = 0
    opened = False
    end = -1
    while j < len(text):
        depth += text[j].count(u'{') - text[j].count(u'}')
        if text[j].count(u'{') > 0:
            opened = True
        if opened and depth == 0:
            end = j
            break
        j += 1
    if end < 0:
        raise SystemExit(u'ОТКАЗ: блок в %s не закрыт' % path)

    if end + 1 < len(text) and text[end + 1].strip() == u'':
        end += 1

    removed = end - start + 1
    rest = b''.join(lines[:start] + lines[end + 1:])
    with open(path, 'wb') as f:
        f.write(rest)
    return removed


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else u''
    if mode not in (u'--strip', u'--restore'):
        raise SystemExit(u'python strip_f48.py --strip | --restore')

    if mode == u'--restore':
        for rel, _ in BLOCKS:
            src = os.path.join(MINE, os.path.basename(rel))
            dst = os.path.join(REPO, rel)
            shutil.copyfile(src, dst)
            same = open(src, 'rb').read() == open(dst, 'rb').read()
            print(u'вернул %s: побайтово %s' % (rel, u'сошлось' if same else u'НЕ СОШЛОСЬ'))
            if not same:
                raise SystemExit(1)
        return

    if not os.path.isdir(MINE):
        os.makedirs(MINE)
    for rel, marker in BLOCKS:
        full = os.path.join(REPO, rel)
        shutil.copyfile(full, os.path.join(MINE, os.path.basename(rel)))
        n = strip_block(full, marker)
        print(u'снял в %s: строк %d' % (rel, n))


main()
