# -*- coding: utf-8 -*-
"""Правильщик G7 (`T137`, решение Amber 06.09.2026 «перевести на utf-8 тем же блоком»).

Три сторожа держали вывод в cp1251 с `errors='replace'`/`'backslashreplace'`; ставится тот же
блок, что у G5 (`reconfigure(encoding='utf-8', errors='replace')` на stdout и stderr).
`check_corpus.py` и `gaussfit_check.py` импортируются (`gate_blind_check.py`,
`ecal_accept_check.py`) — у них блок идёт в тело `if __name__ == '__main__'`, как у G5 в модулях
двойного назначения; у `gate_blind_check.py` импортёров нет — блок на месте прежнего.

Байты: файлы читаются и пишутся как есть (LF, без BOM — проверяется до и после);
каждая замена обязана встретиться ровно один раз.

  python fix_utf8.py <repo>
"""
import os, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
REPO = sys.argv[1]
S = os.path.join(REPO, 'tools', 'CORPUS', 'scripts')

BLOCK = (
    "for _stream in (sys.stdout, sys.stderr):\n"
    "    try:\n"
    "        _stream.reconfigure(encoding='utf-8', errors='replace')\n"
    "    except (AttributeError, ValueError):  # поток подменён (StringIO) или закрыт\n"
    "        pass\n"
)
MAIN_OLD = "if __name__ == '__main__':\n    sys.exit(main())\n"

def indent(text, n=4):
    return ''.join((' ' * n + ln if ln.strip() else ln) for ln in text.splitlines(True))

WHY_MAIN = (
    "    # T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.\n"
    "    # Кодировка utf-8, а не «replace/backslashreplace при cp1251»: по замеру G5 06.09.2026\n"
    "    # (handover/g5-cp1251/04-readers.md) оба канала агента и check_all.py декодируют utf-8, и\n"
    "    # при cp1251 ВЕСЬ русский приходит как ������. Решение Amber 06.09.2026 — utf-8 тем же\n"
    "    # блоком (G7). Блок в теле __main__, а не у импортов: модуль импортируют, и потоки\n"
    "    # импортёра трогать нельзя.\n"
)
WHY_MODULE = (
    "# T137: cp1251-консоль не роняет печать знаков вне неё (⛔, →, σ): приговор кодом важнее вида.\n"
    "# Кодировка utf-8, а не `backslashreplace` при cp1251: по замеру G5 06.09.2026\n"
    "# (handover/g5-cp1251/04-readers.md) оба канала агента и check_all.py декодируют utf-8, и при\n"
    "# cp1251 ВЕСЬ русский приходит как ������. Решение Amber 06.09.2026 — utf-8 тем же блоком (G7).\n"
)

EDITS = {
    'check_corpus.py': [
        # прежний довод про replace-без-смены-кодировки (`A71`) — снят целиком, блок уходит в __main__
        ('span',
         "# ⛔ ПРИЁМКА ОБЯЗАНА ДОЙТИ ДО ВЕРДИКТА В ЛЮБОЙ КОНСОЛИ (`A71`, 02.09.2026).\n",
         "    except (AttributeError, ValueError):        # поток не текстовый или подменён\n        pass\n\n",
         ""),
        ('once', MAIN_OLD,
         "if __name__ == '__main__':\n"
         "    # `A71` (02.09.2026): приёмка обязана дойти до вердикта в любой консоли.\n"
         + WHY_MAIN + indent(BLOCK) + "    sys.exit(main())\n"),
    ],
    'gaussfit_check.py': [
        ('once',
         "# Мерка печатает ⛔ и ⚠, а консоль сопровождающего бывает не в UTF-8: на cp1251\n"
         "# прогон падал `UnicodeEncodeError` в самом конце, ПОСЛЕ всех вычислений и до\n"
         "# сводки. Признак, который не доехал до бумаги, признаком не является.\n"
         "try:\n"
         "    sys.stdout.reconfigure(errors='backslashreplace')\n"
         "except Exception:\n"
         "    pass\n"
         "\n",
         ""),
        ('once', MAIN_OLD,
         "if __name__ == '__main__':\n"
         "    # Мерка печатает ⛔ и ⚠: на cp1251 прогон падал `UnicodeEncodeError` в самом конце,\n"
         "    # ПОСЛЕ всех вычислений и до сводки. Признак, не доехавший до бумаги, признаком не является.\n"
         + WHY_MAIN + indent(BLOCK) + "    sys.exit(main())\n"),
    ],
    'gate_blind_check.py': [
        ('once',
         "# Мерка печатает ⛔ и ⚠; консоль сопровождающего бывает не в UTF-8.\n"
         "try:\n"
         "    sys.stdout.reconfigure(errors='backslashreplace')\n"
         "except Exception:\n"
         "    pass\n",
         WHY_MODULE + BLOCK),
    ],
}

for name, edits in EDITS.items():
    path = os.path.join(S, name)
    raw = open(path, 'rb').read()
    crlf, bom = raw.count(b'\r\n'), raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8')
    for e in edits:
        if e[0] == 'once':
            _, old, new = e
            assert text.count(old) == 1, (name, 'once', text.count(old), old[:60])
            text = text.replace(old, new)
        else:
            _, start, end, new = e
            assert text.count(start) == 1 and text.count(end) == 1, (name, 'span')
            i = text.index(start); j = text.index(end) + len(end)
            assert j > i, (name, 'span order')
            text = text[:i] + new + text[j:]
    out = text.encode('utf-8')
    assert out.count(b'\r\n') == crlf and out.startswith(b'\xef\xbb\xbf') == bom, (name, 'байты')
    open(path, 'wb').write(out)
    compile(text, path, 'exec')
    print(u'%-22s правок %d, CRLF %d, BOM %s, reconfigure: %d, utf-8: %d, cp1251-политик: %d'
          % (name, len(edits), crlf, bom, text.count('.reconfigure('),
             text.count("encoding='utf-8', errors='replace'"),
             text.count("reconfigure(errors=")))
