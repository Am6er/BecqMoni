# -*- coding: utf-8 -*-
# П24: читатель лога плеча — расшифровывает строки, которые pwsh прочёл OEM-866 из UTF-8-выхода пробы (первый прогон,
# до [Console]::OutputEncoding=UTF8), и печатает строки сводки. python p24_log.py <лог> [образец]
import io, re, sys
sys.stdout.reconfigure(encoding='utf-8')
pat = sys.argv[2] if len(sys.argv) > 2 else u'отсев по значимости|заслон|SETUP|изменено|нуль шкалы|наложений|положение по свету|каскадн|итого|known|unknown|ПРОГОН|матриц'
for line in io.open(sys.argv[1], encoding='utf-8', errors='replace'):
    line = line.rstrip('\r\n')
    try:
        fixed = line.encode('cp866').decode('utf-8')
    except Exception:
        fixed = line
    if re.search(pat, fixed):
        print(fixed[:400])
