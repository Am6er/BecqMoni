# -*- coding: utf-8 -*-
"""ОБРАТНЫЙ КОНТРОЛЬ полосы F23 (`A212`): снять три сторожа `null` и вернуть их.

⛔ Правка живёт РОВНО столько, сколько идёт сборка каталога `build_f23_before`.
   В дереве работают соседние полосы, и держать общий файл изменённым дольше
   нельзя. Поэтому порядок такой: `--off` -> сборка -> `--on` -> сверка sha256.

⛔ Возврат идёт из БАЙТОВОЙ копии, а не `git checkout`: откатывать общий файл
   целиком запрещено (память: «НИКОГДА не откатывать общий файл целиком»).

⚠ Читается и пишется с `newline=''`: питон иначе рвёт текст на одиночном CR и
   переводы строк уезжают молча.
"""
import hashlib
import io
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TARGET = os.path.join(ROOT, 'BecquerelMonitor', 'DocumentManager.cs')
BACKUP = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'DocumentManager.cs.f23bak')

# Место 1: заведение локальной кривой. Место 2 и 3: оба её потребителя —
# передний спектр и фон. Тексты дословные, счёт замен проверяется.
GUARD_1 = ("FwhmCalibration fwhmCalibration = doc.ActiveResultData.FwhmCalibration != null\n"
           "                                                  ? doc.ActiveResultData.FwhmCalibration.Clone()\n"
           "                                                  : null;")
NAKED_1 = "FwhmCalibration fwhmCalibration = doc.ActiveResultData.FwhmCalibration.Clone();"

GUARD_23 = "resultData.FwhmCalibration = fwhmCalibration != null ? fwhmCalibration.Clone() : null;"
NAKED_23 = "resultData.FwhmCalibration = fwhmCalibration.Clone();"


def sha(path):
    with open(path, 'rb') as f:
        return hashlib.sha256(f.read()).hexdigest()[:16]


def read():
    with io.open(TARGET, 'r', encoding='utf-8-sig', newline='') as f:
        return f.read()


def write(text):
    with io.open(TARGET, 'w', encoding='utf-8-sig', newline='') as f:
        f.write(text)


def off():
    if os.path.exists(BACKUP):
        sys.exit('ОТКАЗ: копия уже лежит — сторожа, похоже, СНЯТЫ. Сперва --on')
    with open(TARGET, 'rb') as src, open(BACKUP, 'wb') as dst:
        dst.write(src.read())
    text = read()
    n1 = text.count(GUARD_1)
    n23 = text.count(GUARD_23)
    if n1 != 1 or n23 != 2:
        os.remove(BACKUP)
        sys.exit('ОТКАЗ: ожидалось 1 и 2 места, найдено %d и %d — текст в дереве другой' % (n1, n23))
    text = text.replace(GUARD_1, NAKED_1).replace(GUARD_23, NAKED_23)
    write(text)
    print('СТОРОЖА СНЯТЫ: мест 1 + 2 = 3; копия %s' % BACKUP)
    print('  было  sha256 %s' % sha(BACKUP))
    print('  стало sha256 %s' % sha(TARGET))


def on():
    if not os.path.exists(BACKUP):
        sys.exit('ОТКАЗ: копии нет — возвращать нечего')
    was = sha(BACKUP)
    with open(BACKUP, 'rb') as src, open(TARGET, 'wb') as dst:
        dst.write(src.read())
    now = sha(TARGET)
    os.remove(BACKUP)
    print('СТОРОЖА ВОЗВРАЩЕНЫ')
    print('  sha256 копии  %s' % was)
    print('  sha256 в дереве %s' % now)
    print('  ПОБАЙТНО: %s' % ('СОШЛОСЬ' if was == now else '⛔ РАСХОЖДЕНИЕ'))
    if was != now:
        sys.exit(1)


if __name__ == '__main__':
    if len(sys.argv) != 2 or sys.argv[1] not in ('--off', '--on'):
        sys.exit('нужен ключ --off или --on')
    (off if sys.argv[1] == '--off' else on)()
