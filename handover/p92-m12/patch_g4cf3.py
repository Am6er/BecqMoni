# -*- coding: utf-8 -*-
# П92: починка printf — бэкслэш-n внутри строкового литерала вместо голого перевода строки.
import io, sys
p = sys.argv[1]
s = io.open(p, 'rb').read()
old = b'std::printf("CUTS (production thresholds per material couple):\n");'
new = b'std::printf("CUTS (production thresholds per material couple):\\n");'
assert s.count(old) == 1, s.count(old)
s = s.replace(old, new)
io.open(p, 'wb').write(s)
i = s.find(b'CUTS (production')
print(s[i:i + 70])
print('crlf', s.count(b'\r\n'), 'lf-only', s.count(b'\n') - s.count(b'\r\n'))
