# -*- coding: utf-8 -*-
r"""П142 23.09.2026 — перенести ЗАПИСАННЫЕ геометрии витрины FSA на библиотеку веществ
физики 23 (`AMBER53`: сухой воздух по NIST — C/N/O/Ar вместо N/O).

⛔ ЗАЧЕМ, ИНАЧЕ ВИТРИНА НЕ СЧИТАЕТСЯ ВОВСЕ. Клеймо матрицы считается по `GeometryModel`,
СОБРАННОЙ ИЗ `.in`, а состав вещества там приходит ПО ИМЕНИ из `GeometryMaterialStore` —
то есть с физики 23 «Air, dry» четырёхэлементный. Клеймо геометрии спектра считается по
`<Geometry>`, ЗАПИСАННОЙ в конфиге прибора (или в спектре), а там лежали ЯВНЫЕ доли
двухэлементного воздуха из файлов Amber. С физики 23 клейма разошлись, и `FsaStackShot`
отказал на первой же паре: `ОТПЕЧАТОК НЕ СОШЁЛСЯ` — матрица `phys=23;558a527b…`, геометрия
спектра `phys=23;9f7a09c4…`.

У спектров КОРПУСА того же не случилось: там `<Geometry>` перезаписывает `CorpusEffProbe`
из пересозданной сцены, и NIST-воздух попал в узел сам (`ASN16_Radon_filter2.xml` витрины —
единственный, кто назван в `scenes\index.csv`, и он уже с аргоном). Прочие узлы витрины
переписывать нечем — отсюда этот перенос.

⚠ Это ТОТ ЖЕ перенос, что приложение делает человеку (`AMBER67`, П133, проба
`MaterialMigrationProbe --expect=new`: «слоты с аргоном»): у витрины нет
`config\GeometryMaterials.xml`, и сама она не переносится.

Меняются ТОЛЬКО доли слотов вещества «Air, dry» с ДВУМЯ элементами (Z=7, Z=8) на четыре
NIST; плотность, имена, размеры, кривые, guid, отсчёты спектров — не тронуты. Сцены `.in`
НЕ правятся нарочно: их состав читатель всё равно берёт из библиотеки по имени.

  python handover/p142-rev33/showcase_air_nist.py [--check|--revert]

Код 0 — сделано (или при `--check` — найдено ожидаемое), 1 — ничего не нашлось.
"""
import io
import os
import re
import sys

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
SHOW = os.path.join(ROOT, 'tools', 'fsa_showcase')
FILES = [
    os.path.join(SHOW, 'config', 'device', '1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml'),
    os.path.join(SHOW, 'config', 'device', 'Atom Spectra 80x80.xml'),
    os.path.join(SHOW, 'spectra', 'AS80_Th232_disk.xml'),
    os.path.join(SHOW, 'spectra', 'ASN16_Cs137_house.xml'),
    os.path.join(SHOW, 'spectra', 'ASN16_Radon_filter2.xml'),
]

# Пара «было → стало» ищется ВНУТРИ блока <Fractions> слота «Air, dry» с двумя элементами.
OLD2 = re.compile(
    r'(<Name>Air, dry</Name>\s*<Density>[^<]*</Density>\s*<Fractions>\s*)'
    r'<Element Z="7" Fraction="(0\.6364[0-9]*)" />(\s*)<Element Z="8" Fraction="(0\.3635[0-9]*)" />'
    r'(\s*</Fractions>)', re.S)
NEW4 = re.compile(
    r'(<Name>Air, dry</Name>\s*<Density>[^<]*</Density>\s*<Fractions>\s*)'
    r'<Element Z="6" Fraction="0\.000124" />(\s*)<Element Z="7" Fraction="0\.755268" />'
    r'\s*<Element Z="8" Fraction="0\.231781" />\s*<Element Z="18" Fraction="0\.012827" />'
    r'(\s*</Fractions>)', re.S)


def to_new(m):
    sep = m.group(3)
    return (m.group(1)
            + u'<Element Z="6" Fraction="0.000124" />' + sep
            + u'<Element Z="7" Fraction="0.755268" />' + sep
            + u'<Element Z="8" Fraction="0.231781" />' + sep
            + u'<Element Z="18" Fraction="0.012827" />'
            + m.group(5))


def to_old(m):
    sep = m.group(2)
    return (m.group(1)
            + u'<Element Z="7" Fraction="0.63648302312054683" />' + sep
            + u'<Element Z="8" Fraction="0.36351697687945328" />'
            + m.group(3))


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else ''
    total = 0
    for path in FILES:
        raw = open(path, 'rb').read()
        bom = raw.startswith(b'\xef\xbb\xbf')
        text = raw.decode('utf-8-sig')
        pat, repl = (NEW4, to_old) if mode == '--revert' else (OLD2, to_new)
        n = len(pat.findall(text))
        name = os.path.basename(path)
        print(u'%-52s \u0441\u043b\u043e\u0442\u043e\u0432 \u043a \u043f\u0440\u0430\u0432\u043a\u0435: %d' % (name, n))
        total += n
        if n == 0 or mode == '--check':
            continue
        text = pat.sub(repl, text)
        out = ((u'\ufeff' if bom else u'') + text).encode('utf-8')
        with open(path, 'wb') as fh:
            fh.write(out)
        print(u'   %d \u2192 %d \u0431\u0430\u0439\u0442, CRLF %d, \u043e\u0434\u0438\u043d\u043e\u0447\u043d\u044b\u0445 LF %d'
              % (len(raw), len(out), out.count(b'\r\n'), out.count(b'\n') - out.count(b'\r\n')))
    print(u'\u0412\u0421\u0415\u0413\u041e \u0441\u043b\u043e\u0442\u043e\u0432: %d' % total)
    return 0 if total else 1


if __name__ == '__main__':
    sys.exit(main())
