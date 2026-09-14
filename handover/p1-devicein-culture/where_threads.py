# -*- coding: utf-8 -*-
"""
П1 / `A244`. НА КАКОМ ПОТОКЕ ЖИВЁТ КАЖДОЕ ПРАВЛЕНОЕ МЕСТО.

Вопрос не праздный: костыль `MainForm` (клон культуры с подменённым
разделителем) держится ТОЛЬКО на потоке, который его сделал. Место, печатающее
на своём потоке прибора или в обратном вызове WinRT, точки не получало и до
правки — и это самая дорогая часть доли П1.

Скрипт берёт правленые строки ИЗ `git diff` (а не из списка по памяти),
находит охватывающий метод и раскладывает по потокам таблицей ниже.

  python where_threads.py            # из корня дерева
"""
import io, os, re, subprocess, sys, collections

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
FILES = ['RadiaCodeIn.cs', 'ObsidianIn.cs', 'AtomSpectraVCPDeviceForm.cs',
         'AudioInputDeviceForm.cs', 'ObsidianDeviceForm.cs', 'RadiaCodeDeviceForm.cs']

# Кто заводит поток. Метод -> откуда он исполняется. Отдельного «наверное» нет:
# метода, которого здесь нет, скрипт не прячет, а печатает как НЕ РАЗМЕЧЕН.
THREAD = {
    # --- RadiaCodeIn / ObsidianIn: свой поток чтения и поток поиска ---------
    'run': 'ФОН: readerThread (new Thread в start())',
    'connectBLE': 'ФОН: readerThread',
    'disconnectBLE': 'ФОН: readerThread',
    'DiscoverDevice': 'ФОН: discoveryThread (new Thread)',
    'Watcher_Received': 'ФОН: обратный вызов WinRT (поиск устройств)',
    'Watcher_Recived': 'ФОН: обратный вызов WinRT (поиск устройств)',
    'Characteristic_ValueChanged': 'ФОН: обратный вызов WinRT (уведомление GATT)',
    'Device_ConnectionStatusChanged': 'ФОН: обратный вызов WinRT (связь)',
    'RaiseDataReady': 'ФОН: readerThread',
    'setStatus': 'ФОН: readerThread (зовётся и с UI при пуске)',
    'FormatProtocolError': 'ФОН: зовётся из connectBLE (readerThread)',
    'writeCalibration': 'ФОН: readerThread',
    'sendCommandToDevice': 'ФОН: readerThread',
    'ReadSettingsValue': 'ФОН: readerThread',
    'TestBT': 'ФОН: продолжение await (поток пула)',
    'doDiscovery': 'ФОН: discoveryThread (new Thread) и цикл readerThread',
    'ConnectBLE': 'ФОН: readerThread (цикл переподключения)',
    'DisconnectBLE': 'ФОН: readerThread; с UI только по команде Stop',
    'Decode': 'ФОН: обратный вызов WinRT (разбор пакета спектра)',
    'Connect': 'ФОН и UI: ObsidianCalibrationIO из DeviceConfigForm — и с потока окна, и из BackgroundWorker.DoWork',
    'EnableBluetooth': 'ФОН: readerThread',
    'Append': 'ФОН: обратный вызов WinRT (сбор пакета)',
    'ParseTail': 'ФОН: обратный вызов WinRT (сбор пакета)',
    'Finish': 'ФОН: обратный вызов WinRT (сбор пакета)',
    # --- формы: поток окна --------------------------------------------------
    'LoadFormContents': 'UI: поток окна настройки прибора',
    'SaveFormContents': 'UI: поток окна настройки прибора',
    'OnTimer': 'UI: поток окна',
    'StopPulseRecording': 'UI: поток окна',
    'trackBar1_Scroll': 'UI: поток окна',
    'trackBar2_Scroll': 'UI: поток окна',
    'maskedTextBox1_TextChanged': 'UI: поток окна',
    'maskedTextBox2_TextChanged': 'UI: поток окна',
    'fillPorts': 'UI: поток окна',
    'Button1_Click': 'UI: поток окна',
    'deadTimeBtn_Click': 'UI: поток окна',
    'ComPortsBox_SelectedIndexChanged': 'UI: поток окна',
    'BaudratesBox_SelectedIndexChanged': 'UI: поток окна',
    'CommandLineIn_KeyDown': 'UI: поток окна',
    'troubleShootbtn_Click': 'UI: поток окна',
    'comboBox1_SelectedIndexChanged': 'UI: поток окна',
    'TestSerialNumber': 'ФОН: BackgroundWorker.DoWork (поток пула)',
    'TestConnection': 'UI: тело окна и обработчик RunWorkerCompleted (он тоже на UI)',
    'RadiaCodeIn_TroubleShoot': 'ФОН: зовётся ПРЯМО с потока прибора, без Post',
    'ObsidianIn_TroubleShoot': 'ФОН: зовётся ПРЯМО с потока прибора, без Post',
    # ⚠ Тело `new Action(() => …)` внутри `Control.Invoke` исполняется УЖЕ на
    #   потоке окна, хотя написано посреди обратного вызова WinRT. Считать его
    #   фоновым было бы неправдой в свою пользу.
    'Action': 'UI: тело `Control.Invoke` — переведено на поток окна',
}

DECL = re.compile(
    # ⚠ До 16 пробелов: методы ВЛОЖЕННЫХ классов (сборщик пакета)
    #   отступлены глубже, и с {4,8} разбор проезжал мимо них к соседнему
    #   методу внешнего класса — то есть МОЛЧА приписывал место чужому потоку.
    r'^\s{4,16}(?:(?:public|private|protected|internal|static|override|virtual|new|async|'
    r'sealed|partial|unsafe|extern|volatile|readonly)\s+)*'
    # ⛔ Первым — КОРТЕЖНЫЙ возврат `(int, string, string) Foo(`: без него разбор
    #   проезжал мимо `TestSerialNumber` и МОЛЧА приписывал два его места
    #   потоку окна, хотя метод живёт в `BackgroundWorker.DoWork`.
    r'(?:\((?:[A-Za-z_][A-Za-z0-9_ ,.<>\[\]\?]*)\)|'
    r'[A-Za-z_][A-Za-z0-9_.<>,\[\]\?\(\) ]*)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(')


def changed_lines(rel):
    out = subprocess.check_output(['git', 'diff', '-U0', '--', rel], cwd=ROOT)
    txt = out.decode('utf-8', 'replace')
    lines, cur = [], 0
    for ln in txt.split('\n'):
        m = re.match(r'^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@', ln)
        if m:
            cur = int(m.group(1))
            continue
        if ln.startswith('+') and not ln.startswith('+++'):
            lines.append((cur, ln[1:]))
            cur += 1
        elif ln.startswith(' '):
            cur += 1
    return lines


def method_at(src_lines, idx):
    for j in range(idx, -1, -1):
        m = DECL.match(src_lines[j])
        if m:
            nm = m.group('name')
            if nm in ('if', 'for', 'foreach', 'while', 'switch', 'catch', 'lock',
                      'using', 'return', 'new'):
                continue
            return nm
    return '(вне метода)'


def main():
    tally = collections.Counter()
    rows = []
    for f in FILES:
        rel = 'BecquerelMonitor/' + f
        src = io.open(os.path.join(ROOT, rel), encoding='utf-8-sig', newline='').read().split('\n')
        for ln, text in changed_lines(rel):
            if text.lstrip().startswith('using '):
                continue          # вставленная директива, не место печати
            nm = method_at(src, ln - 1)
            where = THREAD.get(nm, 'НЕ РАЗМЕЧЕН')
            rows.append((rel, ln, nm, where, text.strip()[:90]))
            tally[where.split(':')[0]] += 1

    out = io.open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                               'threads-named.txt'), 'w', encoding='utf-8', newline='')
    cur = None
    for r in rows:
        if r[0] != cur:
            cur = r[0]
            out.write(u'\n=== %s ===\n' % cur)
        out.write(u'  %5d  %-34s %-46s %s\n' % (r[1], r[2], r[3], r[4]))
    out.write(u'\n--- СВОДКА ---\n')
    for k, v in tally.most_common():
        out.write(u'  %4d  %s\n' % (v, k))
    out.write(u'  всего правленых мест: %d\n' % len(rows))
    out.close()

    for k, v in tally.most_common():
        print('  %4d  %s' % (v, k))
    print('  всего правленых мест: %d' % len(rows))
    unmarked = [r for r in rows if r[3] == 'НЕ РАЗМЕЧЕН']
    for r in unmarked:
        print('   НЕ РАЗМЕЧЕН %s:%d %s' % (r[0], r[1], r[2]))
    sys.exit(1 if unmarked else 0)


main()
