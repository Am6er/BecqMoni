# -*- coding: utf-8 -*-
r"""П66 (AMBER29, решение Amber 14.09.2026 «3 ранних + 1 равновесный»): четыре спектра угля с
радоном (G1S, маринелли 1 л, ноябрь 2025) — закреплёнными копиями корпуса (`corpus/pinned/`,
`from_corpus=True` в `corpus_def.py`, как `ASN16_Lu176`/`AS80_Lu176`): исходники — `.spe`
ЛСРМ на YandexDisk (только чтение), XML-формы в библиотеке нет, и корпус держит копию сам
(правило Amber 16.08.2026 `B8`).

XML — `handover/p64-amber29/mk_spectra.py` (прибор корпуса G1S24, фон 19.11.2025 ВСТРОЕН,
ПШПВ измеренная; пересборка корпуса заменит узел ПШПВ моделью группы — это известная цена,
П64 §0.2: прибор в ноябре 2025 шире модели G1S24 на 7–12 %). Здесь меняется ТОЛЬКО
`<Note>` — запись о постановке в стиле корпуса (паспорта у пробы нет).

    python handover/p66-rev23/mk_pinned.py <каталог XML mk_spectra.py>
"""
import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, os.pardir, os.pardir))
PINNED = os.path.join(REPO, 'tools', 'CORPUS', 'corpus', 'pinned')

# корпусный ключ -> (ключ П64, старт съёмки по MEASBEGIN, что это)
MAP = [
    ('G1S24_Rn222Coal_Mar_20m', 'coal_t20m', u'«20 мин от начала» 01.11.2025 13:12:52, 1481 с'),
    ('G1S24_Rn222Coal_Mar_2h', 'coal_t2h', u'«2 часа от начала» 01.11.2025 14:18:32, 3529 с'),
    ('G1S24_Rn222Coal_Mar_3h', 'coal_t3h', u'«3 часа от начала» 01.11.2025 15:18:30, 3600 с'),
    ('G1S24_Rn222Coal_Mar_eq01', 'coal_e01', u'«в равновесии с дпр_01» 01.11.2025 16:22:10, 14 400 с'),
]

NOTE = (u'П66 AMBER29 (решение Amber 14.09.2026): активированный уголь 461 г с Rn-222, '
        u'адсорбированным прокачкой воздуха; маринелли 1 л ОМАСН (чертёж), G1S24; %s; '
        u'фон — вода в той же маринелли 19.11.2025 (422 720 с), встроен; паспорта нет')


def main():
    src = sys.argv[1]
    os.makedirs(PINNED, exist_ok=True)
    for key, p64, what in MAP:
        path = os.path.join(src, p64 + '.xml')
        raw = open(path, 'rb').read()
        if raw.count(b'\r\n'):
            raise SystemExit('%s: CRLF в XML mk_spectra (ждали LF)' % p64)
        text = raw.decode('utf-8')
        n = text.count('<Note>')
        if n != 1:
            raise SystemExit('%s: <Note> встречается %d раз' % (p64, n))
        text = re.sub(r'<Note>[^<]*</Note>', lambda m: u'<Note>%s</Note>' % (NOTE % what), text, count=1)
        out = os.path.join(PINNED, key + '.xml')
        if os.path.exists(out):
            raise SystemExit('уже есть: ' + out)
        with io.open(out, 'w', encoding='utf-8', newline='\n') as fh:
            fh.write(text)
        m = re.findall(r'<LiveTime>([^<]+)', text)
        print('%-28s <- %-10s live %s / фон %s, байт %d' % (key, p64, m[0], m[1], len(text.encode('utf-8'))))
    return 0


if __name__ == '__main__':
    sys.exit(main())
