# -*- coding: utf-8 -*-
"""A244 / полоса П3: DeviceConfigForm.cs — печать и разбор чисел инвариантом."""
import io, re, sys

p = 'BecquerelMonitor/DeviceConfigForm.cs'
src = io.open(p, encoding='utf-8-sig', newline='').read()
report = []

def sub(pattern, repl, expect, tag):
    global src
    src, n = re.subn(pattern, repl, src)
    report.append((tag, n, expect))
    if n != expect:
        print('!!! %s: ожидалось %d, заменено %d' % (tag, expect, n))

# 1. Печать в текстовые поля формы. `textBox16` ИСКЛЮЧЁН нарочно: там печатается
#    объект PolynomialEnergyCalibration, у его ToString() перегрузки с
#    провайдером нет (файл вне доли полосы).
sub(r'(this\.(?:doubleTextBox\d+|integerTextBox\d+|numericUpDown\d+)\.Text = [A-Za-z0-9_.\[\]]+)\.ToString\(\);',
    r'\1.ToString(CultureInfo.InvariantCulture);', 25, 'печать в поля формы')

# 2. Пороги, печатаемые в поля ЧУЖОЙ формы прибора (её разбор — полоса П1).
sub(r'((?:lower|upper)ThresholdTextBox\.Text = threshold)\.ToString\(\);',
    r'\1.ToString(CultureInfo.InvariantCulture);', 2, 'пороги в форму прибора')

# 3. Номер в имени нового файла конфигурации.
sub(r'(Resources\.NewDeviceConfigPrefix \+ "\(" \+ i)\.ToString\(\)',
    r'\1.ToString(CultureInfo.InvariantCulture)', 1, 'номер в имени файла')

# 4. Номер строки в таблице точек калибровки.
sub(r'row\.Cells\.Add\(new Cell\(num\.ToString\(\)\)\);',
    r'row.Cells.Add(new Cell(num.ToString(CultureInfo.InvariantCulture)));', 1, 'номер строки таблицы')

# 5. Шестнадцатеричные печать и разбор: культура их не портит, но и провайдер
#    им положен — иначе место остаётся в счёте сканера как неразобранное.
sub(r'\.ToString\("X"\)', r'.ToString("X", CultureInfo.InvariantCulture)', 2, 'hex-печать')
sub(r'(uint\.Parse\(result_arr\[10\], System\.Globalization\.NumberStyles\.AllowHexSpecifier)\)',
    r'\1, CultureInfo.InvariantCulture)', 1, 'hex-разбор uint')
sub(r'(ulong\.Parse\(CalibrationCoefficients\[i\], System\.Globalization\.NumberStyles\.AllowHexSpecifier)\)',
    r'\1, CultureInfo.InvariantCulture)', 1, 'hex-разбор ulong')

# 6. Команда, уходящая В ПРИБОР по COM-порту.
sub(r'device\.sendCommand\("-cal " \+ i \+ " " \+ result_list\[i\]\);',
    r'device.sendCommand("-cal " + i.ToString(CultureInfo.InvariantCulture) + " " + result_list[i]);',
    1, 'команда -cal в прибор')
sub(r'status_msg = status_msg \+ "-cal " \+ i \+ " "',
    r'status_msg = status_msg + "-cal " + i.ToString(CultureInfo.InvariantCulture) + " "',
    1, 'протокол отправки')

# 7. Подпись хода записи коэффициентов — число процентов.
sub(r'string\.Format\(Resources\.WriteCalibrationToAtomProProgress, args\.ProgressPercentage\)',
    r'string.Format(CultureInfo.InvariantCulture, Resources.WriteCalibrationToAtomProProgress, args.ProgressPercentage)',
    1, 'подпись хода записи')

# 8. Разбор ввода человека.
sub(r'int\.Parse\(this\.doubleTextBox5\.Text\)', r'UserNumber.ParseInt(this.doubleTextBox5.Text)', 1, 'разбор времени измерения')
sub(r'int\.Parse\(this\.integerTextBox1\.Text\)', r'UserNumber.ParseInt(this.integerTextBox1.Text)', 1, 'разбор числа каналов')
sub(r'double\.Parse\(this\.doubleTextBox6\.Text\)', r'UserNumber.ParseDouble(this.doubleTextBox6.Text)', 1, 'разбор шага канала')
sub(r'double\.Parse\(this\.numericUpDown(\d+)\.Text\)', r'UserNumber.ParseDouble(this.numericUpDown\1.Text)', 5, 'разбор коэффициентов')
sub(r'decimal\.Parse\(text\)', r'UserNumber.ParseDecimal(text)', 1, 'разбор канала точки')
sub(r'decimal\.Parse\(text2\)', r'UserNumber.ParseDecimal(text2)', 2, 'разбор энергии точки')

# 9. Сообщения ЛСРМ, несущие числа.
sub(r'(problem = string\.Format\(CultureInfo\.)CurrentCulture(,\s*\r?\n?\s*DoseRateCoefficients\.Text\("DoseRateLsrmShortLine")',
    r'\1InvariantCulture\2', 1, 'ЛСРМ: короткая строка')
sub(r'(problem = string\.Format\(CultureInfo\.)CurrentCulture(, "\{0\} \(line \{1\}\): \{2\}")',
    r'\1InvariantCulture\2', 1, 'ЛСРМ: исключение с номером строки')
sub(r'(problem = string\.Format\(CultureInfo\.)CurrentCulture(,\s*\r?\n?\s*DoseRateCoefficients\.Text\("DoseRateLsrmNoPoints")',
    r'\1InvariantCulture\2', 1, 'ЛСРМ: мало точек')

io.open(p, 'w', encoding='utf-8-sig', newline='').write(src)
total = sum(n for _, n, _ in report)
bad = [(t, n, e) for t, n, e in report if n != e]
for t, n, e in report:
    print('%-32s %3d (ждали %d)%s' % (t, n, e, '  <-- РАСХОЖДЕНИЕ' if n != e else ''))
print('всего замен:', total)
sys.exit(1 if bad else 0)
