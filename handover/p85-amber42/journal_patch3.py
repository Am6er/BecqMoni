# -*- coding: utf-8 -*-
r"""П85: приёмка в §7 журнала."""
import io
p = 'handover/handover-2026-09-15-p85-amber42-arbiters.md'
t = io.open(p, encoding='utf-8', newline='').read()
nl = '\r\n' if t.count('\r\n') == t.count('\n') and t.count('\n') > 0 else '\n'
old = """Сняты после приёмки `check_all`: `Release_p85`, `obj\\Release_p85`, `build_p85`, `wd_p85`.
""".replace('\n', nl)
new = """**Приёмка `python tools/check_all.py` — код 1, 38 из 39 зелёные** (`handover/p85-amber42/check_all.log`, 52 с);
красный один — `tools/check_registry.py`, раздел «Ссылки на файлы, которых НЕТ В РЕПОЗИТОРИИ»: шесть ссылок
из `TODO.md` на журналы параллельных полос этой же волны, ещё не закоммиченные (`AMBER42` → П83, П84;
`AMBER43`…`AMBER45`) — ни одна не моя (мой журнал строкой пока не назван); тот же красный стоял и ДО моих правок
(предварительный прогон 19:10, 4 находки — все чужие). Уйдёт с коммитом волны. Сторожа по `tools/g4cf` не
смотрят; `check_fsa_showcase`, `check_build_recipe`, `check_probe_numbers` — зелёные.
Сняты после приёмки: `Release_p85` (82.5 МБ), `obj\\Release_p85` (26.9), `build_p85` (88.8), `wd_p85` (116.4).
""".replace('\n', nl)
if old not in t:
    raise SystemExit('не нашёл фрагмент')
t = t.replace(old, new, 1)
io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok')
