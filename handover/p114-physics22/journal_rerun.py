# -*- coding: utf-8 -*-
r"""П114 — заметка о повторном прогоне после правки комментариев (календарь захода) в §6.3 журнала."""
import io
import sys
sys.stdout.reconfigure(encoding='utf-8')
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\handover\handover-2026-09-19-p114-physics22-rev32.md'
t = io.open(p, encoding='utf-8', newline='').read()
old = u"""полная и малая совпали. Сводка: «ключ ВКЛ у 94 из 94; таблица Q_k сцены нашлась у 92; спектров с парами A_kk≠0 50, пар 8990; множитель
0.4859…1.4279»; `KEYS: НИЧЕГО`, `SETUP: НИЧЕГО`."""
new = u"""полная и малая совпали. Сводка: «ключ ВКЛ у 94 из 94; таблица Q_k сцены нашлась у 92; спектров с парами A_kk≠0 50, пар 8990; множитель
0.4859…1.4279»; `KEYS: НИЧЕГО`, `SETUP: НИЧЕГО`.

⚠ **Прогон снят дважды.** Первый — 15:31 (`sources=4e75c3c0…`, exe `24cf7ed3…`; клейма — `run_out_rev32main_*_first.json`, sha256 CSV —
`out_rev32_*_first_sha256.txt`). После него в комментариях кода (летопись `ResponseMatrix.cs`, шапки проб, тексты `check_matrix_keys.py`,
docstring `check_corpus_scenes.py`, README §1.5) формулировка «ночь 19→20.09.2026» исправлена на «19–21.09.2026» (счёт шёл двумя заходами —
`fix_dates.py`, `fix_dates2.py`, и в worktree тоже); правка комментариев меняет `sources=`, поэтому по `A77` главное дерево пересобрано
(`bin\\p114`/`build_p114` 16:12–16:13, sha256 exe **`ae896b3ccea1934b…`**; штатные `Debug_Codex` + `probes\\build` 16:14) и прогон повторён
16:14 в `out_rev32_*_2` (`run_arm.ps1 -Arm rev32main -Tag _2`): **все 96 + 24 CSV побитово = первому прогону, кроме столбцов `ms`/`cpu_ms`**
(`Compare-Object` sha256: различия ровно в 16 + 4 `*_runs.csv`, столбцы `['cpu_ms', 'ms']`), Σχ² 475.9 / 322.7, `итого 92 100 % 0 0`. Каталоги
второго прогона переименованы в объявляемые `out_rev32_full`/`out_rev32_mini` (первые сняты), клеймо — `sources=5b371450889b70b0…`,
`corpus=38bfb1a0…` (тот же), `head=24cbcafd`; отпечаток в README и текстах §8 — от второго прогона (`fix_sources.py`)."""
assert t.count(old) == 1
t = t.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok')
