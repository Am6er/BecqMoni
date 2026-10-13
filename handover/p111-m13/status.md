# П111 (M13, 19.09.2026) — состояние полосы

Журнал — `handover/handover-2026-09-19-p111-m13-brem-remainder.md`. HEAD `0aab363e`, worktree `D:\BqMoni_Claude\p111\wt` (отсоединённый).

| шаг | результат | где |
|---|---|---|
| сборка worktree (Release `bin\p111`, пробы `build_p111`) | коды 0, sha exe = проб (`C39AC4C1…` HEAD; `602BAAD9…`; `84FB4BCF…` с ключом) | `codes_build.txt` |
| мерка «0.73» на HEAD | занесённые 3.164e-3 квантов/историю против 2.957e-3 арбитра — ×1.07 (на электрон ×1.11); 1461 ×1.09 | `ours/`, `g4out/`, `codes_ours.txt`, `codes_g4.txt` |
| плечо тормозного обвязки 100/60 млн | Цай 0.87 ± 0.06 (0–100 кэВ), 2BS 0.91 ± 0.07 | `tables/arm_outbrem_2614.txt` |
| стенд пластин против `g4brem` | квантов ×0.98…1.09 (300+ кэВ), назад ×0.6…0.75; таблицы тонкая = толстая (0.2 %) | `tables/slab_cmp.txt`, `tables/brem_tables_ptfe_al.txt`, `lr/`, `g4brem/out/` |
| угол: Цай против 2BS | назад ×1.4…1.5 у 2BS | `tables/ang_2bs_vs_tsai.txt` |
| ключ `lbang` (ВЫКЛ) | `check_matrix_keys` 0; ВЫКЛ = живой `RC103_point0.rmx` побитово (`ec888bc7…`), ВКЛ тело различно | `bitwise/`, `logs/check_matrix_keys.log` |
| цена | ×1.00 в шуме ±10 % (машина делилась с П113) | `bitwise/codes_cost.txt` |
| сцена корпуса ВКЛ/ВЫКЛ | RC103_point0: пик −0.02/−0.10/+0.07 %, 0–100 кэВ +0.05/+0.22/+1.36 % | `tables/scene_on_vs_live.txt` |
| перенос в главное дерево | 9 файлов кода по sha (все = HEAD до переноса) | `transfer_log.txt` |
| приёмка главного дерева | `/t:Rebuild` Debug_Codex 0, `build_all.ps1` 0, `python tools/check_all.py` 0 (42 сторожа) | `logs/` |

Не трогалось: `TODO.md`/`DONE.md`, `git add`/`commit`, поставочные `config/*`, живой склад, файлы П109, `tools/CORPUS/`.
После приёмки снять: `git worktree remove D:\BqMoni_Claude\p111\wt`, `bin\p111`, `obj\p111`, `build_p111`, каталог `D:\BqMoni_Claude\p111\`.
