# -*- coding: utf-8 -*-
# Журналы проб, снятые через pwsh `*>`, дважды перекодированы (UTF-8 → cp866 → UTF-8).
# Читатель восстанавливает исходный текст: decode utf-8 → encode cp866 → decode utf-8.
import io, sys
for path in sys.argv[1:]:
    raw = io.open(path, 'rb').read()
    try:
        text = raw.decode('utf-8').encode('cp866').decode('utf-8')
    except (UnicodeDecodeError, UnicodeEncodeError):
        text = raw.decode('utf-8', errors='replace')
    io.open(path, 'w', encoding='utf-8', newline='').write(text)
    print('ok', path)
