# -*- coding: utf-8 -*-
u"""Развёртка по усилению для ОДНОГО спектра — в КОПИИ корпуса (`A278`, полоса П29).

Кладёт в копию корпуса семейство ключей `<base>__g<НННН>` (усиление 1.НННН) плюс
`<base>__ref` — тот же файл, перезаписанный БЕЗ изменения шкалы (отрицательный
контроль: одна перезапись не должна менять ничего).

Два разведённых ключа, и оба стоили замера:

  --where=fore  править ТОЛЬКО передний план (умолчание) | both — и фон тоже.
      ⛔ У `AS80_Charoite` фон снят ДРУГИМ заходом (192 ks против 1584 с), и
      замер `xcorr.py` говорит, что уехал ПЕРЕДНИЙ ПЛАН относительно фона на
      +3.3 %, а не оба вместе. Правка обоих сохраняет их взаимный сдвиг и
      лечит вдвое хуже (χ²/ndf 1.81 против 1.56 на том же усилении).

  --form=chan   E_нов(ch) = E_ст(ch/g) — то, что делает УСИЛЕНИЕ (умолчание);
      c0 остаётся, c1 делится на g, c2 — на g².
  --form=energy E_нов(ch) = E_ст(ch)/g — все коэффициенты делятся на g. Формы
      расходятся ровно на c0·(1−1/g) внизу шкалы (у этого спектра 0.44 кэВ) и
      на c2-член вверху (1.7 кэВ на 2615): мало, но названо.

    python handover/p29-recal/scan_gain.py <корпус> <ключ> <g1,g2,...> [--where=..] [--form=..]
"""
import io, os, sys, xml.etree.ElementTree as ET


def rows(path):
    with io.open(path, encoding='utf-8-sig', newline='') as f:
        head = f.readline()
        return head, [l.rstrip('\r\n') for l in f if l.strip()]


def split_row(row):
    out, cur, q = [], '', False
    for ch in row:
        if ch == '"':
            q = not q; cur += ch
        elif ch == ',' and not q:
            out.append(cur); cur = ''
        else:
            cur += ch
    out.append(cur)
    return out


def newcoef(old, g, form):
    if form == 'chan':
        return [c / g ** i for i, c in enumerate(old)]
    return [c / g for c in old]


def rescale(path, dest, g, where, form):
    tree = ET.parse(path)
    root = tree.getroot()
    tags = ('EnergySpectrum',) if where == 'fore' else ('EnergySpectrum',
                                                        'BackgroundEnergySpectrum')
    touched = []
    for tag in tags:
        for es in root.iter(tag):
            co = es.find('EnergyCalibration/Coefficients')
            if co is None:
                continue
            old = [float(x.text) for x in co]
            new = newcoef(old, g, form)
            for el, v in zip(co, new):
                el.text = repr(v)
            touched.append((tag, old, new))
    if not touched:
        raise SystemExit(u'⛔ в %s не нашлось ни одного узла калибровки' % path)
    tree.write(dest, encoding='utf-8', xml_declaration=True)
    return touched


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    opts = dict(a[2:].split('=', 1) for a in sys.argv[1:] if a.startswith('--'))
    where = opts.get('where', 'fore')
    form = opts.get('form', 'chan')
    tag = opts.get('tag', '')
    corpus, base, gains = args[0], args[1], args[2]
    gs = [float(x) for x in gains.split(',')]
    sp = os.path.join(corpus, 'spectra')
    src = os.path.join(sp, base + '.xml')
    print(u'правим: %s, форма: %s' % (u'только передний план' if where == 'fore'
                                      else u'передний план И фон', form))
    keys = []
    for g in gs:
        key = base + tag + ('__ref' if g == 1.0 else '__g%05d' % round(g * 10000))
        t = rescale(src, os.path.join(sp, key + '.xml'), g, where, form)
        keys.append(key)
        print(u'%-30s g=%.5f  узлов %d  c1 %.10f -> %.10f'
              % (key, g, len(t), t[0][1][1], t[0][2][1]))
    for name in ('manifest.csv', 'parts.csv', 'materials.csv'):
        p = os.path.join(corpus, name)
        if not os.path.isfile(p):
            continue
        head, rr = rows(p)
        srcs = [r for r in rr if split_row(r)[0] == base]
        if not srcs:
            print(u'⚠ %s: строки %s нет, пропущено' % (name, base))
            continue
        have = set(split_row(r)[0] for r in rr)
        with io.open(p, 'a', encoding='utf-8', newline='') as f:
            for key in keys:
                if key in have:
                    continue
                cells = split_row(srcs[0]); cells[0] = key
                f.write(','.join(cells) + '\n')
    print(u'\n--only=%s' % ','.join(keys))


if __name__ == '__main__':
    main()
