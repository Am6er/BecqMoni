# -*- coding: utf-8 -*-
u"""S142: оба плеча сторожа `check_corpus.check_materials`.

Плечо «молчит»  — настоящие таблицы корпуса, ожидается СОШЛОСЬ и True.
Плечо «отказ»   — подброс на КОПИИ таблиц (сами таблицы не трогаются):
  (1) у строки `known` стирается геометрия в parts.csv — пустые клетки
      materials.csv разрешать становится нечем;
  (2) у строки без геометрии стирается кристалл — «не знаем» под видом
      «ничего нет»;
  (3) у строки с геометрией кристалл ВПИСЫВАЕТСЯ — второй источник правды
      (ровно то, что предлагала строка S142 сделать всем 81).
Каждый подброс ставится отдельно, чтобы отказ нельзя было получить чужой
причиной. Спектры не копируются: они читаются из настоящего корпуса.
"""
import csv
import io
import os
import shutil
import sys
import tempfile

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
SCRIPTS = os.path.join(ROOT, 'tools', 'CORPUS', 'scripts')
CORPUS = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus')
SPECTRA = os.path.join(CORPUS, 'spectra')
sys.path.insert(0, SCRIPTS)
import check_corpus                                          # noqa: E402

FIELDS_P = ['spectrum', 'det', 'part', 'geometry', 'why']
FIELDS_M = ['spectrum', 'det', 'part', 'crystal', 'sample', 'shield',
            'source', 'doubt', 'why']


def read(path):
    with io.open(path, encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))


def write(path, rows, fields):
    with io.open(path, 'w', encoding='utf-8-sig', newline='') as fh:
        w = csv.DictWriter(fh, fieldnames=fields)
        w.writeheader()
        for r in rows:
            w.writerow(r)


def arm(title, mutate, spectra=None):
    print('=' * 72)
    print(title)
    tmp = tempfile.mkdtemp(prefix='f75-')
    try:
        parts = read(os.path.join(CORPUS, 'parts.csv'))
        mats = read(os.path.join(CORPUS, 'materials.csv'))
        if mutate is not None:
            mutate(parts, mats)
        write(os.path.join(tmp, 'parts.csv'), parts, FIELDS_P)
        write(os.path.join(tmp, 'materials.csv'), mats, FIELDS_M)
        got = check_corpus.check_materials(tmp, spectra or SPECTRA)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)
    print('  -> вернул %r' % got)
    return got


def by(rows, key):
    for r in rows:
        if r['spectrum'] == key:
            return r
    raise SystemExit('нет строки %s' % key)


def drop_geometry(parts, mats):
    by(parts, 'ASN16_Lu176')['geometry'] = ''
    by(parts, 'ASN16_Lu176')['part'] = 'known'   # part оставляем — судим клетку


def blank_crystal(parts, mats):
    by(mats, 'ASN16_Granite')['crystal'] = ''


def fill_crystal(parts, mats):
    by(mats, 'ASN16_Lu176')['crystal'] = 'Cs;I'


def strip_geometry_dir():
    u"""Каталог спектров, где у ОДНОГО файла вырезан блок `<Crystal>` геометрии.

    Остальные 128 — жёсткие ссылки на настоящие: копировать сотни мегабайт ради
    одного подброса незачем, а сторож обязан отличить именно испорченный файл.
    """
    d = tempfile.mkdtemp(prefix='f75-sp-')
    for name in os.listdir(SPECTRA):
        if not name.endswith('.xml'):
            continue
        try:
            os.link(os.path.join(SPECTRA, name), os.path.join(d, name))
        except OSError:
            shutil.copyfile(os.path.join(SPECTRA, name), os.path.join(d, name))
    victim = os.path.join(d, 'ASN16_Lu176.xml')
    text = io.open(victim, encoding='utf-8-sig').read()
    i, j = text.find('<Crystal>'), text.find('</Crystal>')
    assert i > 0 and j > i, 'в спектре нет блока <Crystal>'
    text = text[:i] + text[j + len('</Crystal>'):]
    os.remove(victim)                       # разорвать жёсткую ссылку
    io.open(victim, 'w', encoding='utf-8-sig', newline='').write(text)
    return d


green = arm(u'ПЛЕЧО «МОЛЧИТ»: настоящие таблицы корпуса', None)
red1 = arm(u'ПЛЕЧО «ОТКАЗ» 1: у known стёрта геометрия — пустоту нечем разрешить',
           drop_geometry)
red2 = arm(u'ПЛЕЧО «ОТКАЗ» 2: у строки без геометрии стёрт кристалл', blank_crystal)
red3 = arm(u'ПЛЕЧО «ОТКАЗ» 3: кристалл вписан поверх геометрии (то, что предлагала S142)',
           fill_crystal)

_sp = strip_geometry_dir()
try:
    red4 = arm(u'ПЛЕЧО «ОТКАЗ» 4: у спектра known вырезано вещество кристалла '
               u'из геометрии — клетка пуста, и разрешить её нечем',
               None, spectra=_sp)
finally:
    shutil.rmtree(_sp, ignore_errors=True)

print()
ok = green and not red1 and not red2 and not red3 and not red4
print('ПРИГОВОР: молчит=%r, отказы=%r/%r/%r/%r -> сторож %s'
      % (green, red1, red2, red3, red4, 'РАБОТАЕТ' if ok else 'НЕ РАБОТАЕТ'))
sys.exit(0 if ok else 1)
