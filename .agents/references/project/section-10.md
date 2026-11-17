<!-- Контекст Codex; действующие правила — в корневом AGENTS.md. -->

## Conventions

- ⛔ **НИКАКИЕ СКРИНШОТЫ НЕ КОММИТИТЬ** (распоряжение Amber 17.09.2026, дословно: «Запиши в
  правило: никакие скриншоты не комитить!»; поводом был коммит `304f9822` с девятью PNG приёмки
  экраном — переписан без них). Снимки экрана и любые картинки-свидетельства (PNG/JPG/BMP/GIF,
  включая обрезки и увеличения) живут ТОЛЬКО на диске — в `handover/<полоса>/` или в
  `D:\BqMoni_Claude\<полоса>\`; в git их не добавлять ни поимённо, ни каталогом (`git add
  handover/<полоса>` добавит их — называть файлы). Журнал перечисляет имена снимков и пишет
  «на диске, не в git»; строки `TODO.md` ссылаются на ЖУРНАЛ, а не на PNG (иначе `check_registry`
  краснеет N8 на файл вне репозитория). Числа, которые доказывает снимок, переносятся в журнал
  текстом — свидетельством в git остаётся текст, не картинка. Заслоны: `.gitignore` на эти
  расширения под `handover/`, сторож `tools/check_no_images.py` в `check_all` (индекс git без
  картинок под `handover/` и `tools/`); 17.09.2026 из HEAD снято 240 картинок (`89d8f9bf`).
  ➕ **Единственное исключение — снимки окон для пользовательского гида в `docs/`** (решение
  Amber 19.10.2026 вопросником, дословно: «Снимки в docs/ — исключение», `AMBER239`). Это
  содержимое справки, а не свидетельство приёмки: только под `docs/`, только окна программы для
  страниц гида. Снимки приёмки экраном по-прежнему в git не идут.
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
- **Сабагенты — на модели по сложности задачи** (решение Amber 05.10.2026; повод дословно:
  «заменой слова Ввоз на Импорт справится и Sonnet, для такой задачи Opus излишен»). Параметр
  `model` вызова `Agent`: `haiku` — механика (поиск, подсчёт), `sonnet` — правка по готовому
  рецепту с простой приёмкой, `opus` — где легко ошибиться по смыслу (перепроверка, потоки,
  калибровка, FSA, перенос, форматы). Без параметра полоса наследует модель распорядителя.
  Сомнение — `opus`: слабая модель чаще срезает углы на замере и контроле. Таблица с
  примерами — `.agents/skills/becqmoni-todo-work/SKILL.md` §3а.
- ⛔ **«Импорт», а не «ввоз»** (Amber 05.10.2026, дословно: «Нужно заменить жаргонный термин
  "Ввоз" на "Импорт" везде, где он встречается»; правилом — вопросником того же дня). Во всех
  новых русских текстах — интерфейс (`*.ru.resx`), тексты отказов, комментарии, отчёты, журналы,
  строки `TODO.md`, сообщения коммитов — «импорт», «импортировать», «импортированный»; «ввоз»,
  «ввозить», «ввезённый» не писать. То же для пары: «экспорт», а не «вывоз» (решение Amber 05.10.2026, вопросником: «Да, тем же объёмом»). Замена старого — строка `AMBER203`.
- ⛔ **Сокращение — «FSA», а не «ППР»** (Amber 14.10.2026, дословно: «Запиши: починить локализацию
  ППР не должно быть, должно быть везде FSA»; правилом — вопросником того же дня: «Правило в
  AGENTS.md + память»). В русском интерфейсе (`*.ru.resx`), журналах, строках `TODO.md`, сообщениях
  коммитов и заданиях полосам полноспектральный разбор сокращается только «FSA»; «ППР» не писать.
  Полное русское название («полноспектральное разложение/разбор») там, где в английском стоит полное
  «Full-spectrum decomposition/analysis», остаётся — объём выбран Amber: «ППР» → «FSA» + меню как в
  en. Старые журналы не правятся: «ППР» в них — цитаты прежнего интерфейса.
- ⛔ **Ширина пика — «FWHM», а не «ПШПВ»** (Amber 19.10.2026 вопросником, дословно: «Везде FWHM
  (Рекомендую)»; повод — разнобой «FWHM Калибровка» / «Калибровка по FWHM» / колонка «ПШПВ» в русском
  интерфейсе, найден разбором `AMBER239`). В русском интерфейсе (`*.ru.resx`) и в пользовательском
  гиде `docs/` — «FWHM»; в гиде один раз раскрыть: «FWHM — ширина пика на половине высоты».
  Служебные строки кода (исключения безоконного режима, заметки для проб) и старые журналы не
  правятся. Замена 19.10.2026 — `AMBER240`.
- ⛔ **Пользовательский гид — «гид», а не «гайд»** (Amber 20.10.2026, дословно: «Замени слова гайд на
  гид.»; объём — вопросником того же дня: «Страницы + правило на будущее (Рекомендую)»). В страницах
  `docs/`, отчётах, журналах, строках `TODO.md`, сообщениях коммитов и заданиях полосам — «гид», «гида»,
  «в гиде»; английское «guide» остаётся. Старые журналы и закрытые строки не правятся: «гайд» в них —
  цитаты. Замена 20.10.2026 — `AMBER247`.
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
  change on 2026-08-08, layout in `database/scheme.md` §0 and `tools/nucdb/split_db.py`.
  ⛔ `LSRM Geometries/` (модели `.in` и экспортированные кривые ЛСРМ) СНЯТ из дерева 15.09.2026
  решением Amber («Удалить вместе с каталогом»); копии моделей с нашими ключами —
  `tools/effmaker/models/`, экспортов в дереве нет (два из восьми живут теми же точками в
  поставочных `config/ROI/Obsidian Marinelli 0.5.xml` и `RadiaCode Marinelli 0.5.xml`).
  Пробы, читавшие каталог, берут его только ключом `--lsrm=`.
- ⛔ **Поставочные `config/NuclideDefinition.xml`, `config/device/*.xml`, `config/ROI/*.xml`
  (и зеркало `BecquerelMonitor/config/`) — НЕ ТРОГАТЬ** (приказ Amber 05.09.2026: «Я их сам
  пересоздам руками. Дефекты на это не принимать, существующие — удалить»). Находка про их
  содержимое (GUID, точки дозы, кривые, ROI-ссылки, подписи, что ClickOnce их не несёт) — не
  строка реестра и не правка; в отчёте одна фраза «поставочные, по приказу 05.09 не
  принимается». Оснастка, которая эти файлы ЧИТАЕТ (клеймо, план, сторожа), — можно; всё, что
  их пишет или требует переписать, — нет. Строки об этом удалены 05.09.2026, запись стоит в
  таблице «Чего делать НЕ надо» `TODO.md`.
