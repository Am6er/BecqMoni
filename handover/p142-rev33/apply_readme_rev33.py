# -*- coding: utf-8 -*-
r"""П142 23.09.2026 — вставить объявление базы rev33 в `tools/CORPUS/README.md`.

Заменяет раздел `## ✅ ДЕЙСТВУЮЩАЯ БАЗА` ЦЕЛИКОМ от заголовка до абзаца
«Известные цены …» включительно (то есть всё, что говорит о числах базы);
дальше идут «Как воспроизвести» и абзац плеч/журналов — их правит вторая
подстановка, потому что там названы снятые базы.

BOM и CRLF сохраняются: файл читается и пишется байтами, текст вставки —
с '\r\n'. Разделитель дробной части — точка.

  python handover/p142-rev33/apply_readme_rev33.py [--check]
"""
import io
import os
import sys

ROOT = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
README = os.path.join(ROOT, 'tools', 'CORPUS', 'README.md')
DECL = os.path.join(ROOT, 'handover', 'p142-rev33', 'readme_declare_rev33.md')

HEAD_OLD = u'## \u2705 \u0414\u0415\u0419\u0421\u0422\u0412\u0423\u042e\u0429\u0410\u042f \u0411\u0410\u0417\u0410: 21.09.2026'
TAIL_OLD = (u'\u0418\u0437\u0432\u0435\u0441\u0442\u043d\u044b\u0435 \u0446\u0435\u043d\u044b '
            u'(\u043d\u0435 \u044d\u0442\u043e\u0439 \u043f\u043e\u043b\u043e\u0441\u044b): '
            u'\u043a\u043e\u043d\u0442\u0430\u043a\u0442 ASN16 \u00d71.17 (\u041f99 \u00a77), '
            u'`RC103_Cs137_50mm` 1.18, `RC103_K40` 1.78 '
            u'\u043d\u0430 \u0441\u0430\u043c\u043e\u0439 \u0448\u0443\u043c\u043d\u043e\u0439 '
            u'\u0441\u0446\u0435\u043d\u0435 \u0441\u043a\u043b\u0430\u0434\u0430.')

# Вторая подстановка: абзац плеч и журналов под «Как воспроизвести».
ARMS_OLD = (u'\u041f\u043b\u0435\u0447\u043e \u00ab\u043a\u0430\u043a \u0444\u0438\u0437\u0438\u043a\u0430 21\u00bb '
            u'\u2014 \u0441\u043a\u043b\u0430\u0434, \u043f\u043e\u0441\u0447\u0438\u0442\u0430\u043d\u043d\u044b\u0439 '
            u'`CorpusMatrixProbe --lbang=0` (\u0442\u0435\u043b\u043e '
            u'\u043f\u043e\u0431\u0430\u0439\u0442\u043d\u043e = \u0441\u043d\u0438\u043c\u043e\u043a '
            u'\u0444\u0438\u0437\u0438\u043a\u0438 21 rev31), \u0432 \u0441\u0432\u043e\u0439 `-Store`;')
ARMS_NEW = (u'\u26d4 \u041f\u043b\u0435\u0447\u0430 \u00ab\u043a\u0430\u043a \u0444\u0438\u0437\u0438\u043a\u0430 22\u00bb '
            u'\u041d\u0415 \u0421\u0423\u0429\u0415\u0421\u0422\u0412\u0423\u0415\u0422: \u0443 '
            u'\u043f\u0440\u0430\u0432\u043e\u043a \u0444\u0438\u0437\u0438\u043a\u0438 23 '
            u'\u043a\u043b\u044e\u0447\u0435\u0439 \u043d\u0435\u0442 (\u00a71.5). '
            u'\u041f\u043b\u0435\u0447\u043e \u00ab\u043a\u0430\u043a \u0444\u0438\u0437\u0438\u043a\u0430 21\u00bb '
            u'\u2014 \u0441\u043a\u043b\u0430\u0434, \u043f\u043e\u0441\u0447\u0438\u0442\u0430\u043d\u043d\u044b\u0439 '
            u'`CorpusMatrixProbe --lbang=0`, \u0432 \u0441\u0432\u043e\u0439 `-Store` '
            u'(\u26a0 \u043f\u043e\u0431\u0438\u0442\u043e\u0432\u043e\u0441\u0442\u0438 \u0441\u043e '
            u'\u0441\u043d\u044f\u0442\u044b\u043c\u0438 \u0441\u043a\u043b\u0430\u0434\u0430\u043c\u0438 '
            u'\u0431\u043e\u043b\u044c\u0448\u0435 \u043d\u0435\u0442 \u2014 '
            u'\u0432\u0435\u0440\u0441\u0438\u044f \u0438 \u0444\u043e\u0440\u043c\u0430\u0442 '
            u'\u0434\u0440\u0443\u0433\u0438\u0435);')

LINKS_OLD = (u'\u0420\u0430\u0437\u0431\u043e\u0440 \u0437\u0430\u0445\u043e\u0434\u0430 \u2014\r\n'
             u'[\u0436\u0443\u0440\u043d\u0430\u043b \u041f114](../../handover/handover-2026-09-19-p114-physics22-rev32.md); '
             u'\u043f\u0440\u0435\u0436\u043d\u044f\u044f \u0431\u0430\u0437\u0430 rev31 '
             u'(\u0444\u0438\u0437\u0438\u043a\u0430 21 `lbrem=1`, \u0434\u0430\u043b\u044c\u043d\u0438\u043c \u00d72, `B31`) \u2014\r\n')
LINKS_NEW = (u'\u0420\u0430\u0437\u0431\u043e\u0440 \u0437\u0430\u0445\u043e\u0434\u0430 \u2014\r\n'
             u'[\u0436\u0443\u0440\u043d\u0430\u043b \u041f142](../../handover/handover-2026-09-23-p142-rev33-merge-declare.md) '
             u'(\u0441\u043b\u0438\u044f\u043d\u0438\u0435, \u043f\u0435\u0440\u0435\u043d\u043e\u0441 '
             u'\u0441\u043a\u043b\u0430\u0434\u0430, \u043e\u0431\u044a\u044f\u0432\u043b\u0435\u043d\u0438\u0435) '
             u'\u0438 [\u0436\u0443\u0440\u043d\u0430\u043b \u041f140](../../handover/handover-2026-09-23-p140-night-rev33-launch.md) '
             u'(\u0441\u0447\u0451\u0442); \u043f\u0440\u0435\u0436\u043d\u044f\u044f \u0431\u0430\u0437\u0430 rev32 '
             u'(\u0444\u0438\u0437\u0438\u043a\u0430 22 `lbang=1`, \u0434\u0430\u043b\u044c\u043d\u0438\u043c \u00d72, `B31`) \u2014\r\n'
             u'[\u0436\u0443\u0440\u043d\u0430\u043b \u041f114](../../handover/handover-2026-09-19-p114-physics22-rev32.md), '
             u'\u0421\u041d\u042f\u0422\u0410 23.09.2026 \u044d\u0442\u0438\u043c '
             u'\u043e\u0431\u044a\u044f\u0432\u043b\u0435\u043d\u0438\u0435\u043c; rev31 '
             u'(\u0444\u0438\u0437\u0438\u043a\u0430 21 `lbrem=1`) \u2014\r\n')


def main():
    check = '--check' in sys.argv
    raw = open(README, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    decl = io.open(DECL, encoding='utf-8').read().replace('\r\n', '\n').rstrip('\n')
    decl = decl.replace('\n', '\r\n')

    i = text.find(HEAD_OLD)
    j = text.find(TAIL_OLD)
    if i < 0 or j < 0:
        print(u'\u26d4 \u0440\u0430\u0437\u0434\u0435\u043b \u043d\u0435 \u043d\u0430\u0439\u0434\u0435\u043d: head=%d tail=%d' % (i, j))
        return 1
    j += len(TAIL_OLD)
    new = text[:i] + decl + text[j:]

    for a, b in ((ARMS_OLD, ARMS_NEW), (LINKS_OLD, LINKS_NEW)):
        if new.count(a) != 1:
            print(u'\u26d4 \u043f\u043e\u0434\u0441\u0442\u0430\u043d\u043e\u0432\u043a\u0430 \u043d\u0435 \u043e\u0434\u043d\u0430: %d' % new.count(a))
            return 1
        new = new.replace(a, b)

    if check:
        print(u'\u0433\u043e\u0442\u043e\u0432\u043e \u043a \u0432\u0441\u0442\u0430\u0432\u043a\u0435; '
              u'\u0431\u044b\u043b\u043e %d \u0431\u0430\u0439\u0442, \u0441\u0442\u0430\u043d\u0435\u0442 %d'
              % (len(raw), len((u'\ufeff' if bom else u'') + new)))
        return 0
    out = ((u'\ufeff' if bom else u'') + new).encode('utf-8')
    with open(README, 'wb') as fh:
        fh.write(out)
    print(u'README \u043f\u0435\u0440\u0435\u043f\u0438\u0441\u0430\u043d: %d \u2192 %d \u0431\u0430\u0439\u0442, CRLF %d, \u043e\u0434\u0438\u043d\u043e\u0447\u043d\u044b\u0445 LF %d'
          % (len(raw), len(out), out.count(b'\r\n'), out.count(b'\n') - out.count(b'\r\n')))
    return 0


if __name__ == '__main__':
    sys.exit(main())
