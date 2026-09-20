# -*- coding: utf-8 -*-
"""П88: независимый (питон) diff четырёх файлов Amber против копий до правки — вторая проверка после записи пробы."""
import io, sys, difflib, hashlib, os
sys.stdout.reconfigure(encoding='utf-8')
B = r'C:\Users\moroz\YandexDisk\Спектры\!AS80x80'
K = r'D:\BqMoni_Claude\p88\backup'
names = [r'Фон дом 09.09.2026.xml', r'калибровка 08.09.2026\Th-232.xml', r'Th-232(медальон ребром) - дистанция 81мм.xml',
         r'Th-232(медальон ребром) - дистанция 81мм с другого бока.xml']
for n in names:
    a = os.path.join(K, n); b = os.path.join(B, n)
    ba = open(a, 'rb').read(); bb = open(b, 'rb').read()
    la = ba.decode('utf-8-sig').split('\r\n'); lb = bb.decode('utf-8-sig').split('\r\n')
    print('== %s' % n)
    print('   до: %d байт sha256 %s | после: %d байт sha256 %s | BOM после: %s, CRLF после: %d, одиночных LF: %d, хвост без перевода строки: %s' %
          (len(ba), hashlib.sha256(ba).hexdigest(), len(bb), hashlib.sha256(bb).hexdigest(), bb[:3] == b'\xef\xbb\xbf', bb.count(b'\r\n'), bb.count(b'\n') - bb.count(b'\r\n'), not bb.endswith(b'\n')))
    for line in difflib.unified_diff(la, lb, 'до', 'после', n=0, lineterm=''):
        print('   ' + line)
