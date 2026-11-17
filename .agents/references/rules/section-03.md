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
