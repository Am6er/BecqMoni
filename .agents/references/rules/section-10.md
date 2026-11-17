<!-- Контекст Codex; действующие правила — в корневом AGENTS.md. -->

## Conventions

- **If something is missing — ASK immediately.** When a file, config, device
  entry, credential, or piece of context you need is absent or doesn't match
  (e.g. a spectrum references a device config that isn't in the workdir), stop
  and ask the user right away instead of silently substituting a workaround or
  a different file. Silent substitutions hide real mismatches the user knows
  how to resolve.
- **Debugging: check callers first.** When investigating a bug, always start by finding every
  usage of the method/function/property in question — direct calls, callbacks, event handlers,
  delegates, overrides, reflection/serialization sites — before concluding what's wrong or
  changing it. The bug is often in how/where it's called (or that it's called from an
  unexpected place), and a fix can break other callers. Grep the whole solution for the symbol
  first.
- **Decompiler-origin code.** ~130 `.cs` files carry `// Token: 0x...` / `RID` comments left
  by a decompiler. This is normal here — don't strip them wholesale; match the surrounding
  style when editing.
- **Localization.** English is primary, Russian is the second language. Nearly every WinForms
  type has a paired `Foo.resx` + `Foo.ru.resx` (and `Properties/Resources.resx` +
  `Resources.ru.resx`). When you add user-facing strings to a form, update both resx files.
  Some code comments and the tuning docs are in Russian.
- ⛔ **Разделитель дробной части у чисел — ВСЕГДА ТОЧКА** (правило Amber 05.09.2026:
  «Никаких других вариантов быть не должно в принципе»). Число печатается И разбирается
  явной инвариантной культурой (`CultureInfo.InvariantCulture`), а не культурой потока;
  `ToString()`, `Parse`, `TryParse`, `string.Format` и интерполяция без культуры — дефект,
  даже если человек этого числа не видит. Подменять разделитель в клоне системной культуры
  (`DefaultThreadCurrentCulture`) НЕЛЬЗЯ: `CurrentCulture` управляет ещё и разбором, и файл,
  записанный с точкой и прочитанный с запятой, даёт не отказ, а другое число, тихо. Обе
  стороны — печать и разбор — чинятся вместе (`A242`). **Группировки разрядов нет вовсе** (решение Amber 05.09.2026): `n1`/`n2` → `f1`/`f2`, `1234.50`, ни запятой, ни пробела в группах.
- **Config/data files.** `config/` holds shipped default configs (device, ROI, layout,
  nuclide definitions); the three `*db.sqlite` files hold the reference data — split by rate of
  change on 2026-08-08, layout in `database/scheme.md` §0 and `tools/nucdb/split_db.py`; `LSRM Geometries/`
  contains detector-efficiency geometry data.
- ⛔ **Поставочные `config/NuclideDefinition.xml`, `config/device/*.xml`, `config/ROI/*.xml`
  (и зеркало `BecquerelMonitor/config/`) — НЕ ТРОГАТЬ** (приказ Amber 05.09.2026: «Я их сам
  пересоздам руками. Дефекты на это не принимать, существующие — удалить»). Находка про их
  содержимое (GUID, точки дозы, кривые, ROI-ссылки, подписи, что ClickOnce их не несёт) — не
  строка реестра и не правка; в отчёте одна фраза «поставочные, по приказу 05.09 не
  принимается». Оснастка, которая эти файлы ЧИТАЕТ (клеймо, план, сторожа), — можно; всё, что
  их пишет или требует переписать, — нет. Строки об этом удалены 05.09.2026, запись стоит в
  таблице «Чего делать НЕ надо» `TODO.md`.
