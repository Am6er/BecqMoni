# -*- coding: utf-8 -*-
u"""П74 (`T260`): сборка КОПИЙ спектров витрины FSA — `tools/fsa_showcase/spectra/`.

Исходники на YandexDisk и в OneDrive — ТОЛЬКО ЧТЕНИЕ; корпус — только чтение.

  * `ASN16_Cs137_house.xml`   — `!ASN16\\Cs 137 в домике 24.11.2022.xml` байт в байт:
    фон ВСТРОЕН (`<BackgroundEnergySpectrum>`), кривая «Точка в защите» guid
    `c482e3bc-…` с геометрией (точка, домик, CsI 15×18×60) — та, что видит Amber.
    Матрица — по этой геометрии, в складе витрины (`scenes/ASN16_point_house.in`,
    выписан `ExportScene.cs`; круг Render сошёлся побайтно).
  * `AS80_Th232_disk.xml`     — `!AS80x80\\калибровка 08.09.2026\\Th-232.xml`, фон
    встроен; узел `<Efficiency>` ЗАМЕНЁН узлом корпусного `AS80_Th232Medal.xml`
    (сцена `AS80_th_disk`, guid `c2b5212c-…`, ториевое стекло 4.345 г/см³ по
    `AMBER3`): у Amber в файле лежит геометрия того же диска, но с веществом
    «Glass, plate» 2.4 г/см³ (клеймо `0772361e…` против `24850bda…` сцены),
    и матрица корпусной сцены к ней не подошла бы. Матрица — живой склад корпуса
    по guid (ссылка, не копия).
  * `ASN16_Radon_filter2.xml` — `!ASN16\\Радон деревня 11 часов спустя.xml` байт в
    байт; узел `<Efficiency>` (сцена `ASN16_rn_side`, широкая грань, вата 0.15)
    вписывает `CorpusEffProbe` при `rebuild_store.ps1` — здесь его нет.

Уголь `G1S24_Rn222Coal_Mar_eq01` и `ASN16_Cs137_10cm` витрина берёт из
`tools/CORPUS/corpus/spectra/` по пути (в манифесте `showcase.json`), копий не держит.

  python handover/p74-t260/mk_spectra.py
"""
import hashlib
import io
import os
import re
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
OUT = os.path.join(REPO, 'tools', 'fsa_showcase', 'spectra')
YD = u'C:\\Users\\moroz\\YandexDisk\\Спектры'
SRC = {
    u'ASN16_Cs137_house.xml': os.path.join(YD, u'!ASN16', u'Cs 137 в домике 24.11.2022.xml'),
    u'ASN16_Radon_filter2.xml': os.path.join(YD, u'!ASN16', u'Радон деревня 11 часов спустя.xml'),
    u'AS80_Th232_disk.xml': os.path.join(YD, u'!AS80x80', u'калибровка 08.09.2026', u'Th-232.xml'),
}
CORPUS_TH = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'spectra', 'AS80_Th232Medal.xml')


def sha(path):
    h = hashlib.sha256()
    with open(path, 'rb') as fh:
        h.update(fh.read())
    return h.hexdigest()[:16]


def efficiency_node(text):
    u"""Границы узла `<Efficiency>` уровня ResultData (тот, что начинается с `<Guid>`):
    закрывающий тег — первый `</Efficiency>` после `<UseResponseMatrix>`."""
    m = re.search(r'<Efficiency>\s*<Guid>', text)
    if not m:
        return None
    s = m.start()
    u = text.find('<UseResponseMatrix>', s)
    if u < 0:
        raise SystemExit(u'узел Efficiency без UseResponseMatrix')
    e = text.find('</Efficiency>', u)
    return s, e + len('</Efficiency>')


def main():
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    for name, src in SRC.items():
        if not os.path.isfile(src):
            raise SystemExit(u'нет исходника: ' + src)
        dst = os.path.join(OUT, name)
        shutil.copyfile(src, dst)
        print(u'%s <- %s  sha %s' % (name, src, sha(dst)))

    # Th-232: узел Efficiency — корпусный (сцена AS80_th_disk).
    th = os.path.join(OUT, u'AS80_Th232_disk.xml')
    text = io.open(th, encoding='utf-8', newline='').read()
    corpus = io.open(CORPUS_TH, encoding='utf-8', newline='').read()
    mine = efficiency_node(text)
    theirs = efficiency_node(corpus)
    if mine is None or theirs is None:
        raise SystemExit(u'узел Efficiency не найден: свой %r, корпусный %r' % (mine, theirs))
    node = corpus[theirs[0]:theirs[1]]
    guid = re.search(r'<Guid>([^<]+)</Guid>', node).group(1)
    gname = re.search(r'<Name>([^<]+)</Name>', node).group(1)
    old = re.search(r'<Guid>([^<]+)</Guid>', text[mine[0]:mine[1]]).group(1)
    text = text[:mine[0]] + node + text[mine[1]:]
    io.open(th, 'w', encoding='utf-8', newline='').write(text)
    print(u'AS80_Th232_disk.xml: узел Efficiency %s (%s) заменён корпусным %s (%s); sha %s'
          % (old, u'Amber', guid, gname, sha(th)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
