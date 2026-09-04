# -*- coding: utf-8 -*-
u"""Формат файла `.resx` — BOM и переводы строк. Общее плечо сверок `check_resx*.py`.

## Правило формата (`T156`, 05.09.2026)

**Каждый `*.resx` дерева — UTF-8 С BOM и переводы строк CRLF, без примесей.**
Так пишет конструктор Visual Studio, так лежит большинство, и под это написана
всякая машинная правка ресурсов в `tools/`.

## Почему правило нужно сторожу, а не только памятке

Строка `T156` называла два файла-отщепенца из сорока двух. Пересчёт по ВСЕМУ
дереву 05.09.2026 дал другое: **75** файлов, из них по правилу **60**, а
**15** — в пяти разных видах (BOM+LF, BOM+смешанные, без BOM+CRLF, без BOM+LF).
`DCFwhmCalibrationView.resx`, который строка звала «LF», был на деле СМЕШАННЫМ:
3 CRLF на 5118 LF — след одной правки, вписавшей три строки чужим переводом.
Глазами и `git diff` такое не видно, потому что:

⚠ **git хранит ВСЕ `.resx` с LF** (`git ls-files --eol` → `i/lf` у 75 из 75), а
CRLF в рабочем дереве делает `core.autocrlf=true` из СИСТЕМНОГО
`C:/Program Files/Git/etc/gitconfig`; `.gitattributes` в дереве нет. Отсюда:

* правка перевода строк в `git diff` НЕ ВИДНА вовсе — ни как изменение, ни как
  откат; в индекс всё равно уйдёт LF;
* на машине с `autocrlf=false` (или Linux) свежее дерево будет целиком LF, и
  сторож переводов строк там покраснеет на всех 75 файлах. Это честно: там
  и правка под CRLF откажет на всех 75. Снять — `--no-format`, а по-хорошему
  завести `.gitattributes` с `*.resx text eol=crlf` (заведено строкой реестра);
* BOM — часть содержимого, и git его хранит; отсутствие BOM правится диффом
  в одну строку и переживает любую машину.

## Что здесь есть

`describe(path)` — (bom, eol, crlf, lf, cr), байтами, без разбора XML.
`problems(paths)` — список строк «ФОРМАТ  файл: чем плох» для файлов не по
правилу; пустой список — все по правилу. Сверка, получившая непустой список,
ОБЯЗАНА напечатать его и вернуть 1: молчаливое чтение чужого формата и есть
та ловушка, ради которой это плечо заведено (в заходе `A118` правка честно
отказала, но заход был потерян на выяснении, почему).

⚠ Читать `.resx` для ПРАВКИ только байтами или с `newline=''`: текстовый режим
питона молча съедает `\r` (памятка «питон рвёт текст на одиночном CR»).
"""
import os

BOM = b'\xef\xbb\xbf'


def describe(path):
    u"""(bom, eol, crlf, lf, cr): eol — 'CRLF' | 'LF' | 'CR' | 'MIXED' | 'NONE'."""
    with open(path, 'rb') as fh:
        data = fh.read()
    bom = data.startswith(BOM)
    crlf = data.count(b'\r\n')
    lf = data.count(b'\n') - crlf
    cr = data.count(b'\r') - crlf
    kinds = [k for k, n in (('CRLF', crlf), ('LF', lf), ('CR', cr)) if n]
    eol = kinds[0] if len(kinds) == 1 else ('NONE' if not kinds else 'MIXED')
    return bom, eol, crlf, lf, cr


def is_normal(path):
    bom, eol, _c, _l, _r = describe(path)
    return bom and eol == 'CRLF'


def problems(paths):
    u"""Строки-отказы для файлов не по правилу BOM + CRLF."""
    out = []
    for path in paths:
        bom, eol, crlf, lf, cr = describe(path)
        why = []
        if not bom:
            why.append(u'без BOM')
        if eol == 'MIXED':
            why.append(u'переводы строк смешаны (CRLF %d, LF %d, CR %d)' % (crlf, lf, cr))
        elif eol != 'CRLF':
            why.append(u'переводы строк %s, а не CRLF' % eol)
        if why:
            out.append(u'ФОРМАТ  %s: %s' % (path.replace(os.sep, '/'), u'; '.join(why)))
    return out
