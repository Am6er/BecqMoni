"""(П343: копия yd_move.py П336, только три файла генератора — T270) YandexDisk переехал на D: (Amber 17.10.2026) — замена префикса пути побайтно.

Запуск: python -I yd_move.py <корень репозитория>
"""
import os
import sys

ROOT = sys.argv[1]
FILES = [
    'tools/CORPUS/scripts/build_corpus.py',
    'tools/CORPUS/scripts/calibrate.py',
    'tools/CORPUS/scripts/corpus_def.py',
]
BS = b'\\'
OLD1 = b'C:' + BS + b'Users' + BS + b'moroz' + BS + b'YandexDisk'
NEW1 = b'D:' + BS + b'YandexDisk'
OLD2 = b'C:' + BS * 2 + b'Users' + BS * 2 + b'moroz' + BS * 2 + b'YandexDisk'
NEW2 = b'D:' + BS * 2 + b'YandexDisk'
OLD3 = b'C:/Users/moroz/YandexDisk'
NEW3 = b'D:/YandexDisk'
total = 0
for rel in FILES:
    path = os.path.join(ROOT, rel)
    data = open(path, 'rb').read()
    n2 = data.count(OLD2)
    data = data.replace(OLD2, NEW2)          # сперва двойные слэши: одинарный образец — их подстрока
    n1 = data.count(OLD1)
    data = data.replace(OLD1, NEW1)
    n3 = data.count(OLD3)
    data = data.replace(OLD3, NEW3)
    open(path, 'wb').write(data)
    total += n1 + n2 + n3
    print('{:<52} одинарных {:>3}, двойных {:>3}, прямых {:>3}'.format(rel, n1, n2, n3))
print('замен всего:', total)
