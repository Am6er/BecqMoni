# -*- coding: utf-8 -*-
u"""S97: четырнадцать снятых спектров — каждого и правда нет в корпусе.

Положительный контроль обязателен: тем же шаблоном ищутся уцелевшие соседи по
группе. Если шаблон сломан, они тоже «не найдутся», и отсутствие снятых ничего
не докажет.
"""
import csv
import io
import os
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
CORPUS = os.path.join(ROOT, 'tools', 'CORPUS', 'corpus')
SPECTRA = os.path.join(CORPUS, 'spectra')

REMOVED = [
    'AS80_Background', 'ASN16_Background', 'ASN8_Background', 'CZTTeCd_Mix',
    'CZT_Cs137', 'CZT_Th232', 'LaBr3_Ore', 'LaBrBril_Background',
    'OBS_Background', 'OBS_Th232WT20', 'RC101_Th232', 'RC103_Background',
    'RC103_Co60', 'SrI2_Th232',
]
# Уцелевшие: по одному из каждой затронутой группы, где группа выжила.
ALIVE = ['OBS_UGlass', 'RC101_I131', 'RC103_K40', 'AS80_K40', 'ASN16_Cs137',
         'ASN8_Am241', 'LaBrBril_Eu152']


def keys(name, col):
    with io.open(os.path.join(CORPUS, name), encoding='utf-8-sig', newline='') as fh:
        return {r[col] for r in csv.DictReader(fh)}


man = keys('manifest.csv', 'key')
par = keys('parts.csv', 'spectrum')
mat = keys('materials.csv', 'spectrum')


def look(k):
    return (os.path.isfile(os.path.join(SPECTRA, k + '.xml')),
            k in man, k in par, k in mat)


print('%-22s %-6s %-9s %-7s %-11s %s' % (
    'спектр', 'файл', 'manifest', 'parts', 'materials', 'вердикт'))
bad = 0
for k in REMOVED:
    f, m, p, t = look(k)
    ok = not (f or m or p or t)
    bad += 0 if ok else 1
    print('%-22s %-6s %-9s %-7s %-11s %s' % (
        k, 'есть' if f else 'нет', 'есть' if m else 'нет',
        'есть' if p else 'нет', 'есть' if t else 'нет',
        'СНЯТ' if ok else '⛔ НАЙДЕН, а объявлен снятым'))
print()
for k in ALIVE:
    f, m, p, t = look(k)
    ok = f and m and p and t
    bad += 0 if ok else 1
    print('%-22s %-6s %-9s %-7s %-11s %s' % (
        k, 'есть' if f else 'нет', 'есть' if m else 'нет',
        'есть' if p else 'нет', 'есть' if t else 'нет',
        'жив (контроль шаблона)' if ok else '⛔ КОНТРОЛЬ НЕ ПРОШЁЛ'))
print()
print('файлов в corpus/spectra: %d' % len(
    [n for n in os.listdir(SPECTRA) if n.endswith('.xml')]))
print('ПРИГОВОР: %s (расхождений %d)' % ('СОШЛОСЬ' if not bad else 'РАЗОШЛОСЬ', bad))
sys.exit(0 if not bad else 1)
