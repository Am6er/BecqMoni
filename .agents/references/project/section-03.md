<!-- Контекст Codex; действующие правила — в корневом AGENTS.md. -->

## 🔨 EVERY PROBLEM GOES INTO `TODO.md`. NO EXCEPTIONS.

**`TODO.md` in the repository root is the single entry point to everything unfinished
and the single register of every known problem.** It lists ALL tasks with a priority
(P0 blocker … P3) and a link to the `*.md` document holding the details.

### The rule, and it is absolute

**The moment you find a problem — anywhere, in anything, by any means — you add a row to
`TODO.md`. Before you do anything else with it.**

This applies to a problem found:

- while doing something else entirely, in code you were not asked to touch;
- in a journal, a README, a comment, a config, a data file, a probe;
- in someone else's area, in third-party data, in a vendor supply;
- reported by the user in passing;
- that you fixed on the spot — if the fix is partial, the remainder is a row;
- that you decided NOT to fix — it goes to the "do NOT do this" table with the reason.

A problem that lives only in a journal, only in a code comment, only in a chat message
or only in your head **does not exist** and will be lost. Writing it down somewhere else
is not a substitute; the journal gets the details, `TODO.md` gets the row. Both, always.

### The other rules

- **Before starting anything**, check `TODO.md` **and `DONE.md`** — the task may already
  be listed, may be blocked by a P0, may be on the "do NOT do this" list at the bottom,
  or may have been closed weeks ago and now live in `DONE.md`. Searching only `TODO.md`
  makes finished work look undone.
- **`DONE.md` is the archive of fully closed rows, and only Amber moves rows into it,
  by hand.** An agent that closes a task strikes the row out **and leaves it in
  `TODO.md`**; deciding that nothing is left over is Amber's call, not yours. Never
  move, delete or "tidy" rows into `DONE.md` yourself.
- **`TODO.md` holds tasks only.** Never dump explanations, numbers, measurements or
  rationale into it. Those live in the linked document; `TODO.md` says what and where.
- **Closed a task? Strike the row out in `TODO.md`** and write the outcome into its
  document. Never the other way round.
- **Finished a review, an audit or an investigation? Verify coverage mechanically** —
  every finding must have a row. Checking by eye has already failed once: eleven items
  out of thirty-three were missing on the first pass.
- ⛔ **Заход обязан УМЕНЬШАТЬ число открытых строк** (распоряжение Amber 05.09.2026).
  Мера захода — разность открытых строк до и после, а не число закрытий: 05.09.2026
  девять добросовестных полос закрыли 18 строк и завели 76 (открытых 220 → 266), и
  реестр, растущий на каждом заходе, перестаёт быть списком дел. Находка становится
  строкой В ПОСЛЕДНЮЮ очередь — сперва починить в той же полосе (дешевле часа, свои
  файлы), потом дописать в существующую строку о том же, факт без «Сделать» — в журнал,
  решение Amber — в вопросник того же дня, отказ делать — в таблицу «Чего делать НЕ
  надо»; строка — только с причиной, почему нельзя было закрыть на месте. Пока баланс
  волны положительный, следующие полосы идут по порождённым строкам. Порядок целиком —
  `.agents/skills/becqmoni-todo-work/SKILL.md` §9.

### Завещания — сквозные, по итогам работы; читать первым делом свежее

Перенесено из `README.md` 15.09.2026 (решения Amber вопросником, дословно: **«Только раздел
«For developers»»**, **«Обновить при переносе»**). Полные журналы полос — `handover/` (270 после
чистки 16.10.2026, `AMBER229`, правилом чистки 15.09.2026 `AMBER41`; снятые — в истории git); здесь — только сквозные, свежие сверху.

| документ | о чём |
|---|---|
| [math-physics-review-2026-09-15.md](../../../handover/math-physics-review-2026-09-15.md) | ревизия математики и физики среза `d1eab74c..6aa928b2` (merge `pie` → `master`); находки — строками `AMBER34`–`AMBER36`, код не тронут |
| [handover-2026-09-15-amber-todo.md](../../../handover/handover-2026-09-15-amber-todo.md) | перепроверка постановок серии `AMBER` и исправления П77–П80 (график эффективности, отказ калибровки, кривая сцены `ISO`, живое время) |
| [handover-2026-09-14-p76-e43-rc103-gap-rev24.md](../../../handover/handover-2026-09-14-p76-e43-rc103-gap-rev24.md) | действующая база корпуса `out_rev24_*`: зазор 3.5 мм RC103, шаблон «RadiaCode-103», лестница rev23 → rev24 |
| [handover-2026-09-14-p74-t260-showcase.md](../../../handover/handover-2026-09-14-p74-t260-showcase.md) | витрина FSA `tools/fsa_showcase` и сторож картинки разбора на спектрах Amber — так принимаются правки отображения |
| [handover-2026-09-13-p50-physics18-store.md](../../../handover/handover-2026-09-13-p50-physics18-store.md), [handover-2026-09-14-p51-store-swap-corpus-rev22.md](../../../handover/handover-2026-09-14-p51-store-swap-corpus-rev22.md) | единый счёт склада матриц физики 18 (ночь 13→14.09): образец счётного захода — worktree, перенос склада, полный корпус, объявление базы |
| [handover-2026-09-10-s3-channels.md](../../../handover/handover-2026-09-10-s3-channels.md), [response-channels-for-review-2026-09-11.md](../../../handover/response-channels-for-review-2026-09-11.md) | четыре канала отклика через весь разбор (`S3`); определения каналов, код и два сырых отклика — выдача рецензенту |
| [full-spectrum-analysis-review-2026-09-07.md](../../../handover/full-spectrum-analysis-review-2026-09-07.md) | ревизия `FullSpectrumAnalysis` (`A287`…), семь проходов 07–08.09 — файлы `handover/full-spectrum-analysis-*.md` |
| [handover-2026-09-13-p52-todo-done.md](../../../handover/handover-2026-09-13-p52-todo-done.md) | перенос `TODO` → `DONE`: правило «только без остатка», `tools/move_done.py` |
| [handover-2026-08-17-matrix-speed.md](../../../handover/handover-2026-08-17-matrix-speed.md) | модель разрешения в корпусе (перенос и степенная форма), скорость расчёта матриц, клеймо, PerfView |
| [handover-2026-08-14.md](../../../handover/handover-2026-08-14.md) | метрология FSA (пределы), правки поставки, паспортные активности, корпус-70 |
| [handover-2026-08-13.md](../../../handover/handover-2026-08-13.md) | полноспектральный разбор, цена счёта, подписи пиков; что ждёт решения |
| [tools/effmaker/handover-response-matrix.md](../../../tools/effmaker/handover-response-matrix.md) | матрица отклика и всё, что на ней меряно |
| [tools/effmaker/handover-2026-08-05.md](../../../tools/effmaker/handover-2026-08-05.md) | конструктор эффективности, сверка с чужими программами, данные |
| [tools/interspec/handover-2026-08-05.md](../../../tools/interspec/handover-2026-08-05.md) | разбор InterSpec: совпадения, кривые GADRAS, происхождение пиков |
