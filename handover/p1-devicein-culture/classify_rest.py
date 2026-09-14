# -*- coding: utf-8 -*-
"""
П1 / `A244`. РАЗБОР ОСТАТКА СКАНЕРА ПОИМЁННО.

Счёт `scan_culture.py` нулём быть не может: сканер считает КАЖДУЮ `$"…"`
и каждый `string.Format` без культуры, не глядя, есть ли внутри число, и не
видит обёртку `FormattableString.Invariant(…)` (в ней культура названа, но
не в аргументах вызова). Поэтому остаток разбирается здесь — механически, а
не глазами:

  * каждая интерполяция разбирается на ДЫРЫ (`{…}`), каждая дыра — на корень
    выражения; корень ищется в объявленной ниже таблице типов;
  * дыра с ЧИСЛОВЫМ типом в НЕобёрнутой интерполяции — ОТКАЗ;
  * `string.Format` без культуры: разбираются аргументы после формата;
  * `ToString()` / `Parse` / `Convert.To*`: тип получателя/владельца из той же
    таблицы.

Всё, чей корень в таблице не найден, печатается как «НЕ РАЗОБРАНО» и считается
ОТКАЗОМ — молчаливого «наверное, не число» здесь нет.

Прогон:  python classify_rest.py <корень дерева>
Итог:    код 0 — числовых мест без инварианта ноль.
"""
import io, os, re, sys, csv, collections

ROOT = sys.argv[1] if len(sys.argv) > 1 else r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
HERE = os.path.dirname(os.path.abspath(__file__))

FILES = ['RadiaCodeIn.cs', 'ObsidianIn.cs', 'AtomSpectraVCPDeviceForm.cs',
         'AudioInputDeviceForm.cs', 'ObsidianDeviceForm.cs', 'RadiaCodeDeviceForm.cs']

# --- ТАБЛИЦА ТИПОВ. Корень выражения -> тип. Числовые типы перечислены в NUM.
NUM = {'int', 'long', 'short', 'byte', 'ulong', 'uint', 'float', 'double',
       'decimal', 'DateTime', 'TimeSpan'}

TYPES = {
    # --- числа (их дыры обязаны быть внутри обёртки) ---
    'discoveryAdvCount': 'int', 'discoveryLastRssi': 'short',
    'ElapsedMs': 'long', 'cps': 'double', 'sum': 'long',
    'elapsedTime': 'int', 'trshootCount': 'int', 'counter': 'int',
    'Counter': 'int', 'TIME_S': 'int', 'dumpFrom': 'int', 'dumpLen': 'int',
    'ExtraBytesCount': 'int', 'TrailerValue': 'int', 'SIZE': 'int',
    'A0': 'float', 'A1': 'float', 'A2': 'float',
    'protocolError': 'byte', 'BluetoothAddress': 'ulong',
    'Length': 'int', 'Count': 'int', 'DateTime': 'DateTime',
    'status': 'int', 'BaudRate': 'int', 'result_arr': 'string[]',
    'Volume': 'int', 'SamplesPerSecond': 'int', 'BitsPerSample': 'int',
    'NumberOfPulses': 'int', 'LowerThreshold': 'double',
    'UpperThreshold': 'double', 'PulseLowerThreshold': 'double',
    'PulseUpperThreshold': 'double', 'PulseThreshold': 'double',
    'pulseThreshold': 'double', 'num': 'int', 'value': 'int',
    'deadTime': 'double', 'rise': 'int', 'fall': 'int', 'f': 'double',
    # --- НЕ числа ---
    'ex': 'Exception', 'e': 'Exception', 'guid': 'string',
    'deviceName': 'string', 'serial': 'string', 'name': 'string',
    'model': 'string', 'addrBLE': 'string', 'addressble': 'string',
    'addressBle': 'string', 'nextStatus': 'string', 'connStatus': 'string',
    'preConnStatus': 'string', 'cccdStatus': 'string',
    'GetStateString': 'string', 'SafeConnStatus': 'string',
    'FormatProtocolError': 'string', 'BitConverter': 'string',
    'Resources': 'string', 'Environment': 'string',
    'btRadio': 'Radio', 'adapter': 'BluetoothAdapter',
    'localDevice': 'BluetoothLEDevice', 'localWriteCharacteristic': 'GattCharacteristic',
    'localNotifyCharacteristic': 'GattCharacteristic',
    'watcher': 'Watcher', 'setResult': 'RadioAccessStatus',
    'servicesResult': 'GattDeviceServicesResult',
    'writeResult': 'GattCharacteristicsResult',
    'notifyResult': 'GattCharacteristicsResult',
    'connectionStatus': 'BluetoothConnectionStatus?',
    'writeStatus': 'GattCommunicationStatus', 'result': 'GattCommunicationStatus',
    'currentState': 'State', 'prevState': 'State', 'reasonState': 'State',
    'trshoot': 'bool', 'hadService': 'bool', 'hadWrite': 'bool',
    'hadNotify': 'bool', 'isRunning': 'bool',
    'deviceConfigForm': 'DeviceConfigForm', 'packet': 'RCSpectrum',
    'args': 'EventArgs', 'buffer': 'byte[]', 'Buffer': 'byte[]',
    'comboBox1': 'ComboBox', 'comPortsBox': 'ComboBox',
    'baudratesBox': 'ComboBox', 'Guid': 'Guid', 'device': 'Device',
    'dev': 'BluetoothLEDevice', 'calibration': 'PolynomialEnergyCalibration',
    'output': 'string[]', 'this': 'self',
    'reason': 'string', 'refusal': 'string', 'failure': 'string',
    'serialNumber': 'string', 'Coefficients': 'double[]',
}

# ⛔ Имена локальных ОДИНАКОВЫ в разных файлах, а типы РАЗНЫЕ: `status` в
#    `AtomSpectraVCPDeviceForm` — int, в `ObsidianIn` — перечисление ответа
#    GATT. Общая таблица на этом ошиблась бы в одну сторону, поэтому у файла
#    есть своя поправка, и она сильнее общей.
PER_FILE = {
    'BecquerelMonitor/ObsidianIn.cs': {'status': 'GattCommunicationStatus'},
    'BecquerelMonitor/RadiaCodeIn.cs': {'status': 'GattCommunicationStatus'},
}

# Свойства/поля, читаемые ПОСЛЕ точки: если хвост здесь, он и решает.
TAIL_WINS = {'Length', 'Count', 'Counter', 'TIME_S', 'ExtraBytesCount',
             'TrailerValue', 'SIZE', 'A0', 'A1', 'A2', 'BluetoothAddress',
             'Message', 'StackTrace', 'Name', 'State', 'Status', 'Value',
             'BaudRate', 'Services', 'CharacteristicProperties', 'Text',
             'ProtocolError', 'Guid', 'NewGuid', 'Now', 'SelectedItem',
             'ConnectionStatus', 'Coefficients'}
TAIL_TYPES = {'Message': 'string', 'StackTrace': 'string', 'Name': 'string',
              'State': 'enum', 'Status': 'enum', 'Value': 'enum',
              'Services': 'collection', 'CharacteristicProperties': 'enum',
              'Text': 'string', 'ProtocolError': 'byte', 'Guid': 'Guid',
              'NewGuid': 'Guid', 'Now': 'DateTime', 'SelectedItem': 'object',
              'ConnectionStatus': 'enum', 'Coefficients': 'double'}


def strip_str(src):
    """Как в scan_culture.py: содержимое литералов и комментариев -> пробелы,
    но маркер `$\"` остаётся."""
    out = list(src)
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == '/' and i + 1 < n and src[i + 1] == '/':
            j = src.find('\n', i)
            j = n if j < 0 else j
            for k in range(i, j):
                out[k] = ' '
            i = j
            continue
        if c == '/' and i + 1 < n and src[i + 1] == '*':
            j = src.find('*/', i + 2)
            j = n if j < 0 else j + 2
            for k in range(i, j):
                if src[k] != '\n':
                    out[k] = ' '
            i = j
            continue
        i += 1
    return ''.join(out)


def interp_span(s, start):
    """[start] = '$' в '$\"'. Вернуть индекс за закрывающей кавычкой."""
    i, depth, n = start + 2, 0, len(s)
    while i < n:
        c = s[i]
        if c == '\\':
            i += 2
            continue
        if c in '{}' and i + 1 < n and s[i + 1] == c:
            i += 2
            continue
        if c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
        elif c == '"':
            if depth == 0:
                return i + 1
            j = i + 1
            while j < n and s[j] != '"':
                j += 2 if s[j] == '\\' else 1
            i = j
        i += 1
    return -1


def holes(text):
    """Выражения дыр интерполяции (без формата после ':')."""
    res, i, n = [], 0, len(text)
    while i < n:
        if text[i] == '{' and i + 1 < n and text[i + 1] == '{':
            i += 2
            continue
        if text[i] == '{':
            depth, j = 1, i + 1
            while j < n and depth:
                if text[j] == '{':
                    depth += 1
                elif text[j] == '}':
                    depth -= 1
                elif text[j] == '"':
                    j += 1
                    while j < n and text[j] != '"':
                        j += 1
                j += 1
            res.append(text[i + 1:j - 1])
            i = j
            continue
        i += 1
    return res


ROOT_RE = re.compile(r'[A-Za-z_][A-Za-z0-9_]*')


def type_of(expr):
    """Тип выражения дыры: NUM / NONUM / '?' (не разобрано)."""
    e = expr.strip()
    # формат после ':' на верхнем уровне отрезан вызывающим
    if not e:
        return 'NONUM'
    # ⛔ Выражение, у которого культура НАЗВАНА внутри, уже инвариантно и
    #    числом наружу не выходит — это строка. Иначе `…Now.ToString("…",
    #    CultureInfo.InvariantCulture)` судилось бы по корню `Now` как дата.
    if 'InvariantCulture' in e:
        return 'NONUM'
    if re.match(r'^\s*"', e) and e.count('"') >= 2:   # строковый литерал
        return 'NONUM'
    if re.match(r'^[0-9]', e):
        return 'NUM'
    if e.startswith('(') and ('?' in e):          # тернарник
        inner = [type_of(x) for x in re.split(r'[?:]', e[1:-1]) if x.strip()]
        return 'NUM' if 'NUM' in inner else ('?' if '?' in inner else 'NONUM')
    names = ROOT_RE.findall(e)
    if not names:
        return 'NUM' if re.search(r'[0-9]', e) else 'NONUM'
    # хвост решает, если он в списке
    for nm in reversed(names):
        if nm in TAIL_WINS:
            t = TAIL_TYPES.get(nm) or TYPES.get(nm)
            if t is None:
                return '?'
            return 'NUM' if t in NUM else 'NONUM'
    for nm in names:
        if nm in ('this', 'base'):
            continue
        t = TYPES.get(nm)
        if t is not None:
            return 'NUM' if t in NUM else 'NONUM'
    return '?'


def classify(path, rel):
    # ⛔ Поправка файла ставится НА ВРЕМЯ разбора этого файла и снимается
    #    после: без снятия она текла в следующие файлы и там `status`
    #    переставал быть числом — правило теряло смысл молча.
    saved = {}
    for k, v in PER_FILE.get(rel, {}).items():
        saved[k] = TYPES.get(k, KeyError)
        TYPES[k] = v
    try:
        return _classify(path, rel)
    finally:
        for k, v in saved.items():
            if v is KeyError:
                TYPES.pop(k, None)
            else:
                TYPES[k] = v


def _classify(path, rel):
    with io.open(path, 'r', encoding='utf-8-sig', newline='') as fh:
        src = fh.read()
    code = strip_str(src)
    rows = []

    # --- интерполяции
    for mo in re.finditer(r'\$"', code):
        st = mo.start()
        end = interp_span(code, st)
        body = src[st + 2:end - 1]
        wrapped = code[:st].rstrip().endswith('FormattableString.Invariant(')
        hs = []
        for h in holes(body):
            # отрезать формат после последнего ':' верхнего уровня
            depth, cut = 0, None
            for k, ch in enumerate(h):
                if ch in '([{':
                    depth += 1
                elif ch in ')]}':
                    depth -= 1
                elif ch == ':' and depth == 0 and not (k and h[k - 1] == '?'):
                    cut = k
            hs.append((h[:cut] if cut is not None else h, h))
        kinds = [(type_of(a), raw) for a, raw in hs]
        num = [r for k, r in kinds if k == 'NUM']
        unk = [r for k, r in kinds if k == '?']
        line = code.count('\n', 0, st) + 1
        if unk:
            verdict = 'НЕ РАЗОБРАНО: ' + '; '.join(unk)
        elif not num:
            verdict = 'чисел в дырах нет'
        elif wrapped:
            verdict = 'ИНВАРИАНТ (FormattableString.Invariant), числа: ' + '; '.join(num)
        else:
            verdict = 'ОТКАЗ — число без инварианта: ' + '; '.join(num)
        rows.append((rel, line, 'interpolation', verdict))

    # --- string.Format без культуры
    for mo in re.finditer(r'\b(?:string|String)\.Format\s*\(', code):
        op = code.index('(', mo.start())
        depth, i = 0, op
        while i < len(code):
            if code[i] == '(':
                depth += 1
            elif code[i] == ')':
                depth -= 1
                if depth == 0:
                    break
            i += 1
        a = code[op + 1:i]
        if re.search(r'Culture|Invariant|NumberFormat|provider', a.split(',')[0]):
            continue
        # аргументы после формата
        parts, depth, cur = [], 0, ''
        for ch in a:
            if ch in '([{':
                depth += 1
            elif ch in ')]}':
                depth -= 1
            if ch == ',' and depth == 0:
                parts.append(cur)
                cur = ''
            else:
                cur += ch
        parts.append(cur)
        args = parts[1:]
        kinds = [(type_of(x), x.strip()) for x in args if x.strip()]
        num = [r for k, r in kinds if k == 'NUM']
        unk = [r for k, r in kinds if k == '?']
        line = code.count('\n', 0, mo.start()) + 1
        if unk:
            verdict = 'НЕ РАЗОБРАНО: ' + '; '.join(unk)
        elif not num:
            verdict = 'чисел в аргументах нет'
        else:
            verdict = 'ОТКАЗ — число без инварианта: ' + '; '.join(num)
        rows.append((rel, line, 'string.Format', verdict))

    # --- ToString() без культуры
    for mo in re.finditer(r'\.ToString\s*\(\s*\)', code):
        st = mo.start()
        j = st - 1
        while j >= 0 and (code[j].isalnum() or code[j] in '_.)]'):
            if code[j] in ')]':
                d = 1
                j -= 1
                while j >= 0 and d:
                    if code[j] in ')]':
                        d += 1
                    elif code[j] in '([':
                        d -= 1
                    j -= 1
                continue
            j -= 1
        recv = code[j + 1:st]
        t = type_of(recv)
        line = code.count('\n', 0, st) + 1
        if t == '?':
            verdict = 'НЕ РАЗОБРАНО: ' + recv
        elif t == 'NUM':
            verdict = 'ОТКАЗ — число без инварианта: ' + recv
        else:
            verdict = 'не число: ' + recv
        rows.append((rel, line, 'ToString()', verdict))

    # --- склейка числа со строкой, как её видит сканер.
    #     ⚠ Сканер смотрит ТОЛЬКО на имя после '+' и не видит, что следом
    #     стоит .ToString(CultureInfo.InvariantCulture); здесь смотрим.
    for mo in re.finditer(r'"\s*\+\s*([A-Za-z_][A-Za-z0-9_]*)(?![A-Za-z0-9_(])', code):
        nm = mo.group(1)
        if TYPES.get(nm) not in NUM:
            continue
        tail = code[mo.end():mo.end() + 80]
        line = code.count('\n', 0, mo.start()) + 1
        if tail.lstrip().startswith('.ToString(') and 'InvariantCulture' in tail[:60]:
            verdict = 'ИНВАРИАНТ (ToString(InvariantCulture) после склейки): ' + nm
        else:
            verdict = 'ОТКАЗ — число в склейке без инварианта: ' + nm
        rows.append((rel, line, 'concat', verdict))

    # --- разбор без культуры
    for mo in re.finditer(r'\b([A-Za-z_][A-Za-z0-9_.]*)\.(TryParse|Parse)\s*\(', code):
        op = code.index('(', mo.start())
        depth, i = 0, op
        while i < len(code):
            if code[i] == '(':
                depth += 1
            elif code[i] == ')':
                depth -= 1
                if depth == 0:
                    break
            i += 1
        a = code[op + 1:i]
        if re.search(r'Culture|Invariant|NumberFormat|provider', a):
            continue
        owner = mo.group(1).split('.')[-1]
        line = code.count('\n', 0, mo.start()) + 1
        if owner in NUM or owner in ('Double', 'Single', 'Int32', 'Int64', 'UInt64'):
            verdict = 'ОТКАЗ — разбор числа без инварианта: ' + owner
        else:
            verdict = 'не число: ' + owner + '.' + mo.group(2)
        rows.append((rel, line, 'Parse', verdict))

    for mo in re.finditer(r'(?<![A-Za-z0-9_.])Convert\.To([A-Za-z0-9]+)\s*\(', code):
        op = code.index('(', mo.start())
        depth, i = 0, op
        while i < len(code):
            if code[i] == '(':
                depth += 1
            elif code[i] == ')':
                depth -= 1
                if depth == 0:
                    break
            i += 1
        a = code[op + 1:i]
        if re.search(r'Culture|Invariant|NumberFormat|provider', a):
            continue
        line = code.count('\n', 0, mo.start()) + 1
        t = type_of(a)
        if t == 'NUM':
            verdict = 'не разбор строки, числовая перегрузка: Convert.To%s(%s)' % (mo.group(1), a.strip())
        elif t == '?':
            verdict = 'НЕ РАЗОБРАНО: Convert.To%s(%s)' % (mo.group(1), a.strip())
        else:
            verdict = 'ОТКАЗ — разбор строки без инварианта: Convert.To' + mo.group(1)
        rows.append((rel, line, 'Convert.To*', verdict))

    return rows


def main():
    allrows = []
    for f in FILES:
        allrows += classify(os.path.join(ROOT, 'BecquerelMonitor', f), 'BecquerelMonitor/' + f)
    bad = [r for r in allrows if r[3].startswith('ОТКАЗ') or r[3].startswith('НЕ РАЗОБРАНО')]
    by = collections.Counter()
    out = io.open(os.path.join(HERE, 'rest-named.txt'), 'w', encoding='utf-8', newline='')
    cur = None
    for r in sorted(allrows, key=lambda r: (r[0], r[1])):
        if r[0] != cur:
            cur = r[0]
            out.write(u'\n=== %s ===\n' % cur)
        out.write(u'  %5d %-14s %s\n' % (r[1], r[2], r[3]))
        key = r[3].split(':')[0].split(',')[0]
        by[key] += 1
    out.write(u'\n--- СВОДКА ---\n')
    for k, v in by.most_common():
        out.write(u'  %4d  %s\n' % (v, k))
    out.write(u'\nвсего разобрано мест: %d\n' % len(allrows))
    out.write(u'мест ОТКАЗА (число без инварианта либо не разобрано): %d\n' % len(bad))
    out.close()
    print('разобрано мест: %d' % len(allrows))
    for k, v in by.most_common():
        print('  %4d  %s' % (v, k))
    print('ОТКАЗОВ: %d' % len(bad))
    for r in bad:
        print('   %s:%d %s — %s' % (r[0], r[1], r[2], r[3]))
    sys.exit(1 if bad else 0)


main()
