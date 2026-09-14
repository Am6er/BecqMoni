# -*- coding: utf-8 -*-
"""П72 (T259) — ПАТЧ ДЛЯ СЛЕДУЮЩЕГО ПЕРЕОБЪЯВЛЕНИЯ, НЕ ПРИМЕНЁН. Учит генератор корпуса
(`build_corpus.py`, `calibrate.py` — набор клейма `T244`) метке-члену ряда («Rn-222») тем же
правилом `chain_labels`, что и `score.py`, и переписывает истину угля в `corpus_def.py`
на `Rn-222;Th-228`. Три файла входят в клеймо `corpus/generator.json`: правка без полной
пересборки корпуса красит сторож `check_corpus_generator.py`, поэтому применять ВМЕСТЕ с
`rebuild_corpus.py --from-library` (по разрешению Amber) и переобъявлением базы.

Что меняется:
  * `build_corpus.sample_xrays` / `sample_lines`, `calibrate.sample_lines`: корень и линии
    метки, которой нет в `chains.CHAINS`, — через `chain_labels` (подряд от члена с обрывом
    по периоду); у меток из `CHAINS` и у `U-238u` — ровно прежний путь (побитово тот же
    корпус у 131 спектра, меняются только 4 строки угля);
  * `corpus_def.COAL`: `chains=['Rn-222', 'Th-228']`, в `why` оговорка «формы нет» снята.

    python handover/p72-t258-t259/generator_switch_patch.py --apply   (иначе — только проверка якорей)
"""
import io, os, sys

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
SCRIPTS = os.path.join(REPO, 'tools', 'CORPUS', 'scripts')

EDITS = {
    'build_corpus.py': [
        ("from chains import (chain_lines, CHAINS,             # noqa: E402\n",
         "from chains import (chain_lines, CHAINS,             # noqa: E402\n"),  # якорь без правки
        ("        root = '238U' if ch == 'U-238u' else CHAINS[ch]\n"
         "        for r in chain_lines(root, kinds=('X',)):\n",
         "        # (`T259`) метка-член ряда («Rn-222») — подряд от члена по правилу\n"
         "        # приложения (`chain_labels`); метки `CHAINS` и `U-238u` — как прежде.\n"
         "        if ch == 'U-238u' or ch in CHAINS:\n"
         "            root = '238U' if ch == 'U-238u' else CHAINS[ch]\n"
         "            xrows = chain_lines(root, kinds=('X',))\n"
         "        else:\n"
         "            import chain_labels\n"
         "            xrows = chain_labels.chain_lines(ch, kinds=('X',))\n"
         "        for r in xrows:\n"),
        ("        for r in chain_lines(CHAINS[ch]):\n"
         "            rows.append((r['energy'], r['i_chain'], r['name']))\n"
         "    for nucid in entry.get('nuclides') or []:\n"
         "        rows.extend(nuclide_lines(nucid))\n",
         "        if ch in CHAINS:\n"
         "            lines = chain_lines(CHAINS[ch])\n"
         "        else:\n"
         "            import chain_labels                       # (`T259`) подряд от члена\n"
         "            lines = chain_labels.chain_lines(ch)\n"
         "        for r in lines:\n"
         "            rows.append((r['energy'], r['i_chain'], r['name']))\n"
         "    for nucid in entry.get('nuclides') or []:\n"
         "        rows.extend(nuclide_lines(nucid))\n"),
    ],
    'calibrate.py': [
        ("        for r in chain_lines(CHAINS[ch]):\n"
         "            rows.append((r['energy'], r['i_chain'], r['name']))\n"
         "    rows.extend(AMBIENT)\n",
         "        if ch in CHAINS:\n"
         "            lines = chain_lines(CHAINS[ch])\n"
         "        else:\n"
         "            import chain_labels                       # (`T259`) подряд от члена\n"
         "            lines = chain_labels.chain_lines(ch)\n"
         "        for r in lines:\n"
         "            rows.append((r['energy'], r['i_chain'], r['name']))\n"
         "    rows.extend(AMBIENT)\n"),
    ],
    'corpus_def.py': [
        ("         chains=['Ra-226', 'Th-228'], from_corpus=True,\n",
         "         chains=['Rn-222', 'Th-228'], from_corpus=True,\n"),
        ("             'на t₀ при радоне 58.5, Бейтман П64 §4); ряд Ra-226 БЕЗ ГОЛОВЫ (Ra-226 '\n"
         "             '< 8.7, Pb-210 < 5.2 Бк на сумме), Th-228-ряд угля 7…8 Бк без Ac-228; '\n",
         "             'на t₀ при радоне 58.5, Бейтман П64 §4); подряд Rn-222 с дочерними '\n"
         "             'Po-218/Pb-214/Bi-214/Po-214 (T259; Ra-226 < 8.7, Pb-210 < 5.2 Бк на сумме), '\n"
         "             'Th-228-ряд угля 7…8 Бк без Ac-228; '\n"),
        ("             'равновесии с радоном (П64 §4: t2h на −2σ по обоим); ряд Ra-226 без '\n"
         "             'головы + Th-228-ряд угля; фон вода 19.11.2025 встроен; паспорта нет'),\n",
         "             'равновесии с радоном (П64 §4: t2h на −2σ по обоим); подряд Rn-222 (T259) '\n"
         "             '+ Th-228-ряд угля; фон вода 19.11.2025 встроен; паспорта нет'),\n"),
        ("             '15:18:30 (2 ч 26 мин после t₀), 3600 с; ряд Ra-226 без головы + '\n"
         "             'Th-228-ряд угля; фон вода 19.11.2025 встроен; паспорта нет'),\n",
         "             '15:18:30 (2 ч 26 мин после t₀), 3600 с; подряд Rn-222 (T259) + '\n"
         "             'Th-228-ряд угля; фон вода 19.11.2025 встроен; паспорта нет'),\n"),
        ("             'Бк-экв, П64 §3); ряд Ra-226 без головы + Th-228-ряд угля; фон вода '\n",
         "             'Бк-экв, П64 §3); подряд Rn-222 (T259) + Th-228-ряд угля; фон вода '\n"),
        ("# Bi-214, Po-214), самого Ra-226 нет (< 8.7 Бк на сумме 30 съёмок, П64 §5) и\n"
         "# Pb-210 нет (< 5.2 Бк; от распада радона за неделю ≈ 0.03 Бк). ⚠ Формы «ряд\n"
         "# без головы» у словаря рядов нет (`FsaSampleChain.FromLabel`: Th-232, Th-228,\n"
         "# Ra-226, U-238, U-235, U-238u; `score.CHAIN_MAP`; `chains.CHAINS`), поэтому\n"
         "# объявлен `Ra-226` с этой оговоркой — находка П66 распорядителю; при связке\n"
         "# ряда ВКЛ (умолчание корпуса) Ra-226/Pb-210 навязываются рядом (П64 §6), а в\n",
         "# Bi-214, Po-214), самого Ra-226 нет (< 8.7 Бк на сумме 30 съёмок, П64 §5) и\n"
         "# Pb-210 нет (< 5.2 Бк; от распада радона за неделю ≈ 0.03 Бк). Форма «ряд от\n"
         "# члена» — метка `Rn-222` (`T259`, П72): подряд от члена вниз по `decay_chain`,\n"
         "# пока период короче корня (Pb-210 22 г отсечён) — `FsaSampleChain.FromLabel`,\n"
         "# `chain_labels.py`, `score.chain_components`. До П72 объявлялся `Ra-226` с\n"
         "# оговоркой, и при связке ряда ВКЛ Ra-226/Pb-210 навязывались рядом (П64 §6); в\n"),
    ],
}
# Сколько раз обязан встретиться каждый якорь: строка `chains=` — у четырёх записей угля.
WANT = {"         chains=['Ra-226', 'Th-228'], from_corpus=True,\n": 4}


def main():
    apply = '--apply' in sys.argv
    for name, edits in EDITS.items():
        path = os.path.join(SCRIPTS, name)
        raw = open(path, 'rb').read()
        crlf = b'\r\n' in raw
        bom = raw.startswith(b'\xef\xbb\xbf')
        text = raw.decode('utf-8-sig').replace('\r\n', '\n')
        for old, new in edits:
            n = text.count(old)
            want = WANT.get(old, 1)
            if n != want:
                print('%s: якорь найден %d раз (ждали %d): %r' % (name, n, want, old[:60]))
                return 1
            text = text.replace(old, new)
        if apply:
            out = text.replace('\n', '\r\n') if crlf else text
            open(path, 'wb').write((b'\xef\xbb\xbf' if bom else b'') + out.encode('utf-8'))
            print('%s: применено (%d правок)' % (name, len(edits)))
        else:
            print('%s: якоря на месте (%d правок), не применено' % (name, len(edits)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
