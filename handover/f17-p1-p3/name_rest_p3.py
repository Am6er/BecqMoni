# -*- coding: utf-8 -*-
"""
F17 / `A244`. РАЗБОР ОСТАТКА СКАНЕРА ПОИМЁННО — доля П3 «конфигурация прибора».

Шесть файлов доли П1 разбирает `classify_rest.py` (механически, по таблице
типов). Здесь остаток МЕНЬШЕ (23 места), и каждое названо ПОИМЁННО с приговором
и причиной. Место, которого нет в таблице приговоров, печатается «НЕ НАЗВАНО» и
даёт код 1: молчаливого «наверное, не число» здесь нет.

  python handover/f17-p1-p3/name_rest_p3.py <scan-after.tsv>

Код 0 — фактических мест печати/разбора числа без инварианта ноль.
"""
import csv, io, os, sys

if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')

HERE = os.path.dirname(os.path.abspath(__file__))

FILES = ('DeviceConfigForm.cs', 'DeviceConfigForm.Efficiency.cs',
         'DeviceConfigManager.cs', 'GlobalConfigForm.cs')

# (файл, строка) -> (разряд, приговор). Разряды:
#   'FALSE'   — ложное срабатывание сканера: числа в этом месте нет вовсе;
#   'FOREIGN' — число ЕСТЬ, но печатает его метод ЧУЖОГО файла (не доля F17):
#               место названо, правка уходит дописью в `A244`, доля П8;
#   'REAL'    — настоящее место МОЕЙ доли без инварианта. Таких обязан быть ноль.
VERDICT = {
    ('DeviceConfigForm.cs', 239):
        ('FALSE', u'`Resources.MessageRemoveDeviceConfig` + `Name` — довод строка, числа нет'),
    ('DeviceConfigForm.cs', 295):
        ('FALSE', u'склейка `i`, но печатается уже `i.ToString(InvariantCulture)` — сканер видит имя переменной рядом с `+ "("`'),
    ('DeviceConfigForm.cs', 524):
        ('FOREIGN', u'`RC_EnergyCalibration.ToString()` — ЧИСЛА, но печатает их `PolynomialEnergyCalibration.ToString()` (ЧУЖОЙ файл, доля П8): дописка в `A244`'),
    ('DeviceConfigForm.cs', 541):
        ('FOREIGN', u'то же для `OBS_EnergyCalibration`'),
    ('DeviceConfigForm.cs', 764):
        ('FALSE', u'`Guid.NewGuid().ToString()` — числа нет'),
    ('DeviceConfigForm.cs', 942):
        ('FALSE', u'интерполяция `{Resources.ERRBTNotSupportedByOS} … {ex.Message}` — обе дыры строки'),
    ('DeviceConfigForm.cs', 1094):
        ('FALSE', u'`ex.ToString()` — исключение, не число'),
    ('DeviceConfigForm.cs', 1290):
        ('FALSE', u'`Resources.ERRReadDataFromPort` + `ComPortName` — строка'),
    ('DeviceConfigForm.cs', 1351):
        ('FALSE', u'`Resources.ERRReadDataFromPort` + `DeviceSerial` — строка'),
    ('DeviceConfigForm.cs', 1377):
        ('FALSE', u'`Resources.ERRReadDataFromPort` + `DeviceSerial` — строка'),
    ('DeviceConfigForm.cs', 1396):
        ('FOREIGN', u'то же для `OBS_EnergyCalibration` (третье место того же чужого `ToString()`)'),
    ('DeviceConfigForm.cs', 1403):
        ('FALSE', u'`Resources.ERRReadDataFromPort` + `DeviceSerial` — строка'),
    ('DeviceConfigForm.cs', 1490):
        ('FALSE', u'склейка `i`, печатается `i.ToString(InvariantCulture)` — правлено этой же полосой'),
    ('DeviceConfigForm.cs', 1494):
        ('FALSE', u'то же, вторая склейка той же строки следа'),
    ('DeviceConfigForm.cs', 2634):
        ('FALSE', u'`Resources.ERRFileOpenFailure` + имя файла + `ex.Message` — обе строки'),
    ('DeviceConfigForm.cs', 2846):
        ('FALSE', u'`Resources.ERRFileOpenFailure` + имя файла + `problem` — обе строки'),
    ('DeviceConfigForm.Efficiency.cs', 476):
        ('FALSE', u'`Resources.EfficiencyTabCopySuffix` + `config.Name` — строка'),
    ('DeviceConfigForm.Efficiency.cs', 491):
        ('FALSE', u'`Resources.EfficiencyTabDeleteConfirm` + `config.Name` — строка'),
    ('DeviceConfigManager.cs', 121):
        ('FALSE', u'`Resources.ERRDuplicateDeviceConfigGUID` + `Filename` — строка'),
    ('DeviceConfigManager.cs', 165):
        ('FALSE', u'`Guid.NewGuid().ToString()` — числа нет'),
    ('DeviceConfigManager.cs', 205):
        ('FALSE', u'`Guid.NewGuid().ToString()` — числа нет'),
    ('DeviceConfigManager.cs', 307):
        ('FALSE', u'`Resources.ERRConfigFileRenameFailed` + `OriginalFilename` — строка'),
    ('DeviceConfigManager.cs', 465):
        ('FALSE', u'`Resources.ERRConfigFileDeleteFailed` + `OriginalFilename` — строка'),
}


def main():
    scan = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'scan-after.tsv')
    rows = []
    with io.open(scan, encoding='utf-8', newline='') as f:
        r = csv.reader(f, delimiter='\t', quoting=csv.QUOTE_NONE)
        next(r)
        for x in r:
            name = os.path.basename(x[0])
            if name in FILES:
                rows.append((name, int(x[4]), x[1], x[2]))
    rows.sort()

    out = io.open(os.path.join(HERE, 'rest-named-p3.txt'), 'w', encoding='utf-8', newline='')
    bad, foreign = [], []
    cur = None
    for name, line, side, api in rows:
        if name != cur:
            cur = name
            out.write(u'\n=== %s ===\n' % name)
        v = VERDICT.get((name, line))
        if v is None:
            note = u'НЕ НАЗВАНО — отказ'
            bad.append((name, line, note))
        else:
            kind, note = v
            if kind == 'REAL':
                note = u'НАСТОЯЩЕЕ место доли: ' + note
                bad.append((name, line, note))
            elif kind == 'FOREIGN':
                note = u'ЧУЖОЙ ФАЙЛ: ' + note
                foreign.append((name, line, note))
            else:
                note = u'ложное: ' + note
        out.write(u'  %5d %-6s %-14s %s\n' % (line, side, api, note))

    out.write(u'\nвсего мест: %d\n' % len(rows))
    out.write(u'настоящих мест МОЕЙ доли (обязан быть ноль): %d\n' % len(bad))
    out.write(u'уводящих в ЧУЖОЙ файл (дописка в `A244`): %d\n' % len(foreign))
    for name, line, note in foreign:
        out.write(u'   %s:%d — %s\n' % (name, line, note))
    out.close()

    print('мест разобрано: %d' % len(rows))
    print('настоящих мест МОЕЙ доли: %d' % len(bad))
    for name, line, note in bad:
        print('   %s:%d — %s' % (name, line, note))
    print('уводящих в ЧУЖОЙ файл (дописка в A244): %d' % len(foreign))
    for name, line, note in foreign:
        print('   %s:%d — %s' % (name, line, note))
    sys.exit(1 if bad else 0)


main()
