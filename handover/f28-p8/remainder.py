# -*- coding: utf-8 -*-
"""F28: ПОИМЁННЫЙ разбор остатка сканера по доле П8.

Каждое место остатка получает разряд. Разряды:
  ИНВ   — культура в этой же строке УЖЕ инвариантна (сканер не узнаёт довод
          после receiver'а: `x.ToString(CultureInfo.InvariantCulture)` внутри
          склейки он считает как «склейка числа»);
  СТР   — все доводы строковые (имя, путь, сообщение исключения) — числа нет;
  ТИП   — получатель не число: Guid, Version, enum, StringBuilder, Exception,
          object-ячейка со строкой;
  ЧИСЛО→ЧИСЛО — `Convert.ToInt32(double)` / `Convert.ToDouble(int)`: это
          преобразование числа в число, разбора текста нет вовсе;
  ВРЕМЯ — длительность/дата, решение Amber «остаются в раскладке человека».
Печатает таблицу и число мест, оставшихся БЕЗ разряда (это и есть настоящие).
"""
import csv, io, os, sys, collections
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from census import mine

SLASH = chr(92)
SRC = {}


def line_of(path, n):
    if path not in SRC:
        try:
            SRC[path] = io.open(path, encoding='utf-8-sig', newline='').read().split('\n')
        except UnicodeDecodeError:
            SRC[path] = io.open(path, encoding='cp1251', newline='').read().split('\n')
    lines = SRC[path]
    return lines[n - 1].strip() if 0 < n <= len(lines) else ''


def statement(path, n):
    """Строка находки И продолжение ОДНОГО оператора — до строки, кончающейся ';'.

    ⛔ Окно «одна до, две после» было бы ЛОЖНО ЩЕДРЫМ: инвариант СОСЕДНЕГО
    оператора зачёл бы место как разобранное. Берётся ровно свой оператор.
    """
    if path not in SRC:
        line_of(path, n)
    lines = SRC[path]
    parts = []
    i = n - 1
    while i < len(lines) and i < n - 1 + 12:
        st = lines[i].rstrip()
        parts.append(st.strip())
        if st.endswith(';'):
            break
        i += 1
    return ' '.join(parts)


# получатели, которые НЕ являются числом
TYPE_RECEIVERS = ('Guid.NewGuid()', 'stringBuilder', 'text', 'exception',
                  '.GetName().Version', 'currentDeployment.CurrentVersion',
                  'base.GetType()', 'varEnum', 'error',
                  'DeviceListcomboBox.SelectedItem',
                  'properties[guid].Value')

TIME = {('BecquerelMonitor/PercentageProgressBar.cs', 141),
        ('BecquerelMonitor/PercentageProgressBar.cs', 143)}


def classify(r):
    f = r['file'].replace(SLASH, '/')
    n = int(r['line'])
    recv = r['receiver']
    api = r['api']
    src = line_of(f, n)
    ctx = statement(f, n)
    if (f, n) in TIME:
        return 'ВРЕМЯ'
    if api.startswith('Convert.To') and r['side'] == 'parse':
        return 'ЧИСЛО→ЧИСЛО'
    if recv.endswith(TYPE_RECEIVERS) or recv in TYPE_RECEIVERS:
        return 'ТИП'
    if recv.startswith('this.ResultDataGridView'):
        return 'ТИП'
    if 'InvariantCulture' in ctx:
        return 'ИНВ'
    return ''


src_tsv = sys.argv[1]
rows = [r for r in csv.DictReader(io.open(src_tsv, encoding='utf-8'),
                                  delimiter='\t', quoting=csv.QUOTE_NONE)
        if mine(r['file'].replace(SLASH, '/'))]

# ручной разряд СТР: доводы сплошь строковые — проверено чтением
STRING_ONLY = {
    ('BecquerelMonitor/AboutForm.cs', 19), ('BecquerelMonitor/AboutForm.cs', 66),
    ('BecquerelMonitor/DocEnergySpectrum.cs', 1121),
    ('BecquerelMonitor/MeasurementController.cs', 219),
    ('BecquerelMonitor/MeasurementController.cs', 270),
    ('BecquerelMonitor/NuclideDefinitionForm.cs', 176),
    ('BecquerelMonitor/NucBase/DataBase.cs', 82),
    ('BecquerelMonitor/NucBase/NucBase.cs', 205),
    ('BecquerelMonitor/NucBase/NucBase.cs', 477),
    ('BecquerelMonitor/NucBase/NucBase.cs', 571),
    ('BecquerelMonitor/NucBase/NucBase.cs', 1068),
    ('BecquerelMonitor/NucBase/NucBase.cs', 1130),
    ('BecquerelMonitor/NucBase/NucBase.cs', 1138),
    ('BecquerelMonitor/NucBase/NucBase.cs', 1140),
    ('BecquerelMonitor/NucBase/NucBase.cs', 1142),
    ('BecquerelMonitor/NucBase/NucBase.cs', 1144),
    ('BecquerelMonitor/ObsidianDeviceController.cs', 79),
    ('BecquerelMonitor/RadiaCodeDeviceController.cs', 77),
    ('BecquerelMonitor/DocumentManager.cs', 1406),
    ('BecquerelMonitor/Utils/SpectrumAriphmetics.cs', 237),
    ('BecquerelMonitor/N42/Util.cs', 482),
    ('BecquerelMonitor/N42/Util.cs', 737),
    ('BecquerelMonitor/N42/Util.cs', 738),
    ('BecquerelMonitor/N42/Util.cs', 739),
    ('BecquerelMonitor/N42/Util.cs', 740),
    ('BecquerelMonitor/N42/Util.cs', 741),
    ('BecquerelMonitor/N42/Util.cs', 1186),
    ('BecquerelMonitor/N42/Util.cs', 1194),
    ('BecquerelMonitor/N42/Util.cs', 775),
    ('BecquerelMonitor/DocumentManager.cs', 2057),
    ('BecquerelMonitor/DocumentManager.cs', 2071),
    ('BecquerelMonitor/DocumentManager.cs', 2075),
    ('BecquerelMonitor/DocumentManager.cs', 2179),
    ('BecquerelMonitor/DocumentManager.cs', 2182),
    ('BecquerelMonitor/DocumentManager.cs', 2185),
    ('BecquerelMonitor/DocumentManager.cs', 2188),
    ('BecquerelMonitor/DocumentManager.cs', 2328),
    ('BecquerelMonitor/N42/Util.cs', 330),
}
for f in ('BecquerelMonitor/DocumentManager.cs',):
    pass

kinds = collections.Counter()
unknown = []
out = []
for r in rows:
    f = r['file'].replace(SLASH, '/')
    n = int(r['line'])
    k = classify(r)
    if not k and (f, n) in STRING_ONLY:
        k = 'СТР'
    # ⛔ Разряд ставится НЕ по первому доводу из таблицы сканера, а по
    #    прочитанному оператору: `handover/f28-p8/formats-left.txt` печатает
    #    их целиком, и все 25 прочитаны глазами. Так нашлись ДВА настоящих
    #    (`DocumentManager.cs:1225` и `:1307` — целые доводы), оба починены.
    if not k and r['api'] == 'string.Format' and r['args'].startswith('Resources.'):
        k = 'СТР'
    kinds[k or 'НЕ РАЗОБРАНО'] += 1
    if not k:
        unknown.append((f, n, r['side'], r['api'], r['receiver'], line_of(f, n)[:130]))
    out.append('%s:%s\t%s\t%s\t%s\t%s' % (f, n, k or 'НЕ РАЗОБРАНО', r['side'],
                                          r['api'], line_of(f, n)[:130]))

dst = sys.argv[2] if len(sys.argv) > 2 else None
if dst:
    io.open(dst, 'w', encoding='utf-8', newline='\n').write('\n'.join(out) + '\n')

print('остаток сканера по доле П8: %d' % len(rows))
for k, v in sorted(kinds.items(), key=lambda kv: -kv[1]):
    print('  %-14s %d' % (k, v))
print()
if unknown:
    print('--- НЕ РАЗОБРАНО (%d) ---' % len(unknown))
    for u in unknown:
        print('%s:%d  %s %s  recv=%s  | %s' % u)
