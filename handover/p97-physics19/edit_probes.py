# -*- coding: utf-8 -*-
r"""П97 — правка комментариев проб под физику 19 (ВКЛ умолчанием). Каждая замена — ровно один раз,
байты CRLF сохраняются (файл читается и пишется бинарно; замены — в utf-8 с CRLF)."""
import os
import sys

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass

WT = r'D:\BqMoni_Claude\p97\wt'
NL = u'\r\n'
EDITS = {
    'tools/effmaker/probes/CorpusMatrixProbe.cs': [
        (NL.join([
            u"// Входит в клеймо (`eltr=1`), хвост `ELTR`. ⛔ ВЫКЛ умолчанием до единого",
            u"// счёта склада: ВКЛ — физика 19, пересчёт 46 сцен (~8 ч) и переобъявление",
            u"// базы — по отдельному решению Amber («Спросить по приёмке правки»).", u""]),
         NL.join([
            u"// Входит в клеймо (`eltr=1`), хвост `ELTR`. ✅ ВКЛ умолчанием с 18.09.2026 —",
            u"// физика 19 (П97: единый счёт склада 46 сцен, база `out_rev28_*`; решение",
            u"// Amber 17.09.2026 «ВКЛ сейчас, единый счёт ночью»); `--eltr=0` — абляция",
            u"// «как физика 18» (тело побайтно = склад физики 18, клеймо только `phys=`).", u""])),
        (NL.join([
            u"                // занос и возврат; входит в клеймо (`eltr=1`). ВЫКЛ умолчанием",
            u"                // до единого счёта (физика 19); `--eltr=1` — матрица честно",
            u"                // другая по клейму.", u""]),
         NL.join([
            u"                // занос и возврат; входит в клеймо (`eltr=1`). ВКЛ умолчанием",
            u"                // с физики 19 (П97, 18.09.2026); `--eltr=0` — абляция, матрица",
            u"                // честно другая по клейму (без `eltr=1;`).", u""])),
    ],
    'tools/effmaker/probes/G4RawProbe.cs': [
        (NL.join([
            u"    /// процессу, дошедший — в перенос по кристаллу. Умолчание — склада (ВЫКЛ до",
            u"    /// единого счёта физики 19). Мерка: RC103 (П55 и живая) 662/1461/59.5, диск", u""]),
         NL.join([
            u"    /// процессу, дошедший — в перенос по кристаллу. Умолчание — склада (ВКЛ с",
            u"    /// физики 19, П97 18.09.2026; `--eltr=0` — плечо «как физика 18»). Мерка: RC103 (П55 и живая) 662/1461/59.5, диск", u""])),
        (u"            bool eltr = store.ElectronLayerTransport;   // `AMBER44`/`M12`, П94 — умолчание склада (ВЫКЛ до физики 19)" + NL,
         u"            bool eltr = store.ElectronLayerTransport;   // `AMBER44`/`M12`, П94 — умолчание склада (ВКЛ с физики 19, П97)" + NL),
    ],
}


def main():
    bad = 0
    for rel, pairs in EDITS.items():
        p = os.path.join(WT, *rel.split('/'))
        b = open(p, 'rb').read()
        for old, new in pairs:
            ob, nb = old.encode('utf-8'), new.encode('utf-8')
            n = b.count(ob)
            if n != 1:
                print(u'⛔ %s: образец встречается %d раз: %r' % (rel, n, old[:60]))
                bad += 1
                continue
            b = b.replace(ob, nb)
        if not bad:
            open(p, 'wb').write(b)
        print(u'%s: правок %d' % (rel, len(pairs)))
    return 1 if bad else 0


if __name__ == '__main__':
    sys.exit(main())
