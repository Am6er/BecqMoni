# -*- coding: utf-8 -*-
import os
p = os.path.join(r'D:\BqMoni_Claude\p103\wt', 'tools', 'CORPUS', 'README.md')
b = open(p, 'rb').read(); s = b.decode('utf-8-sig').replace('\r\n', '\n')
o = u"""`MatrixAuditProbe --phys=20` (пять признаков внутри файла: клеймо, узлы, истории, шум, число каналов;
`--hist=3000000` называет три густые сцены отдельно — это и ожидается) и `tools/check_matrix_keys.py`."""
n = u"""`MatrixAuditProbe --phys=20 --hist=3000000 --except=RC103_point50:6000000
--except=ASN16_point10_house:6000000 --except=G1S_point25:6000000 --noise=6` (пять признаков внутри
файла: клеймо, узлы, истории, шум, число каналов; `--except=` — густым сценам своё число историй) и
`tools/check_matrix_keys.py`."""
assert s.count(o) == 1
s = s.replace(o, n)
nb = b'\xef\xbb\xbf' + s.replace('\n', '\r\n').encode('utf-8'); open(p, 'wb').write(nb); print('ok')
