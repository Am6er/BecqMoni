<!-- Контекст Codex; действующие правила — в корневом AGENTS.md. -->

## Testing / verification

⛔ **Приёмка любого коммита — `python tools/check_all.py`, код 0** (решение Amber
06.09.2026, `T220`). Читатель зовёт сорок два сторожа `tools/check_*.py` (`GUARDS`;
18.09.2026, П105) за ~3 минуты и печатает каждое обращение; список сторожей и заведомо красные (с
причиной, в `KNOWN_RED`) — в шапке самого `check_all.py`. Красный сторож, который
держится открытой строкой реестра, обходить новым исключением нельзя.

There is **no unit-test project.** The peak-detection code paths are
verified offline against reference spectra using tools in `tools/`:

- **`tools/CORPUS/`** — the spectrum corpus and the toolkit that builds and checks it
  (`corpus/`, `data/` efficiency curves and calibrations, `scripts/` pipeline, `probes/`).
  `README.md` describes the corpus in its CURRENT state — what is in it, the declared base, how to
  run it, the analysis rules and the traps; its history (how it was assembled, retired bases) lives
  in git (`git show 37b069de:tools/CORPUS/README.md`). See below.
- **`tools/pie/`** — full-spectrum decomposition harness that runs over the corpus.
- **`tools/centroid_tests/`** — centroid-method (center-of-mass vs argmax) comparison report.

### ⛔ Every corpus number must name its part (blocker lifted 2026-08-09)

Corpus runs are **allowed** — the 2026-08-06 block was lifted on 2026-08-09 when the corpus was
split. What it was about is still true and is now handled by the split, not by a ban: the
detector response matrix (`tools/effmaker/handover-response-matrix.md`) is computed **from
geometry** and is **specific to each geometry** — measured: one crystal in three source placements
gave peak fractions 0.848 / 0.822 / 0.815 and up to a quarter's spread in the continuum. Mixing
spectra that have a matrix with spectra that do not averages two different models, invisibly.

So the corpus is kept in parts (`corpus/parts.csv`) and **every quoted number must say which part
it came from**: **known** — geometry and matrix present (82 spectra), **unknown** — neither
(39), **excluded** — germanium, out of scope (8). Numbers of two parts are never added together.
A spectrum without a geometry is kept in the corpus **on purpose**, as the anchor of its group's
resolution model — it is not an unfinished "known" one, and no "build it a geometry" task is
raised for it. The current rule and the split are in `tools/CORPUS/README.md`; the history behind
them — in git, by the pointer in its header.

### ⛔ First postulate: every spectrum has its OWN peak base, tied to the nuclide measured

Amber's rule, 17.08.2026. Not a preference and not one task among others — this is how the
corpus is analysed. The application's general library is the **source** the base is assembled
from, never the list handed to a spectrum: the corpus already knows what was measured
(`manifest.csv`, the spectrum's own name, the nuclide column of `summary.csv`), and offering
the whole library hands unexplained structure to the first candidate that fits. Measured cost
(`N18`): `ASN16_Lu176` acquired a **Pu-238** at 1.7 % and z = 31.77 on a single 152 keV line
of 0.0009 % intensity, sitting in the backscatter/Compton-edge band of lutetium's own 306.78.

**Assemble the base from ALL radiations of the decay, not from gamma lines** — Lu-176's only
gamma below 200 keV is 88.34, while the real spectrum down there rests on Hf K X-rays at
54–65 keV (`nucdb.decay_radiations`, type `X`); a gamma-only base left 405σ at 60.8 keV.
Background and the ubiquitous K-40 / Th-232 / Ra-226 belong in the base too. ⚠ Two traps
measured the same day: recall and phantom counts are computed **relative to the list offered**,
so narrowing it improves them by itself and such a run cannot be compared with the old base
directly; and a narrow base starves the gain fit — seven lines drove it into the edge of its
±0.8 % grid. Task: `S56` (blocker); the rule as it stands — `tools/CORPUS/README.md` («Первый
постулат»); the measurements behind it — in git by the pointer in its header.

### ⛔ Есть доказательство, что данные базы неверны, — спросить вопросником, а не пройти мимо

Распоряжение Amber 05.11.2026, дословно: **«Глобальное правило - если есть обоснование, что какие-то данные в нашей базе некорректны и есть доказательство, не молча пропускать это мимо, а задавать вопросом.»** Повод — выход K-40 1460.82 (`AMBER287`). Находка с обоснованием и доказательством идёт в вопросник сразу: какое число, чей источник, что показал замер, что изменится при правке. Записывает в базу только её слово (приказ 09.08.2026 «ЛЮБОЕ изменение базы только через меня» в силе). Находка без доказательства — не повод для вопросника: сперва замер.

### ⛔ Корпус старше модели: новичок доказывает право его менять — критиками

Распоряжение Amber 02.11.2026. Повод — общий счёт волны AMBER270–285: после физики 30 «вне паспорта» 15 → 19 из 80, почти всё — точечные сцены. Её слова, дословно: **«Переизмерение! Спектры в корпусе более приоритетны. Нужно доказать, что "новчичок" имеет право их менять через критиков.»** — и следом, о том, где это правило записать: **«Запиши глобально. Вес корпуса влияет на новые приборы и спектры.»** Главное — её уточнение того же дня, дословно: **«Да, основная идея такая - корпус у нас сейчас СТАБИЛЕН. Если появляется новый прибор - он должен доказать, что нужно менять весь корпус.»**

Общая установка, Amber 02.11.2026, дословно: **«Общая установка. Работаем так, чтобы не ухудшить ситуацию по корпусу.»** Это мерка любой правки, а не только новых приборов. Правка, после которой корпус по паспорту (число «вне», сумма выходов за порог) или по χ² на общей полосе стал хуже, не принимается, пока критики не доказали её право. Замер «до/после» на корпусе — часть приёмки такой правки.

То есть корпус — устойчивая точка отсчёта, а новый прибор (его спектры, его геометрия, его сюрприз) — гость. Новый прибор не повод крутить модель под себя. Если ради него хочется менять физику, данные или умолчания FSA, он должен доказать, что меняться должен ВЕСЬ корпус. Пример, на котором правило родилось: правки FSA ради тория в маринелли RC-110 (`AMBER269`) — к критикам.

Измеренные спектры корпуса с паспортами — истина. Модель — подсудимая: физика переноса, склад матриц, ядерные данные `nucdb`/`matdb`, умолчания FSA, калибровки. Отсюда:

* **Любая правка, сдвинувшая совпадение корпуса с паспортом, — НОВИЧОК, и бремя доказательства на нём.** Сдвигом считается любое изменение числа «вне паспорта» и отношений FSA/паспорт. Критики (правило «нет сходимости — вызывай критиков») показывают механизм, число и положительный контроль, почему сдвиг верен. Не доказано — слой откатывается. Корпус под него не «переобъявляют», и правка, ухудшившая корпус, не становится новой базой молча.
* **Вес корпуса распространяется на новые приборы и спектры.** Правка, сделанная ради нового прибора или нового спектра (пример — торий в маринелли RC-110), корпус не перевешивает. Улучшение на новом спектре ценой корпуса принимается только с доказательством критиков. И обратно: то, что испорчено на корпусе, испорчено и у новых приборов.
* **Сомнение — в пользу корпуса.** Amber 02.11.2026, дословно: **«Если есть сомнения у критиков - пусть отменяют свои решения»**. Критик, который не может доказать правку (в том числе ту, что раньше рекомендовал сам или другой критик, — например, Ф6), выносит вердикт «отменить» и сдаёт готовый патч отката. «Оставить и посмотреть» вердиктом не является.
* **Новая база (`rev…`) не объявляется и волна не коммитится, пока каждый слой, сдвинувший паспорт, не прошёл критиков.** Решение того же дня: «После критика и его правки».
* **Расхождение модели со спектром Amber решается в пользу спектра.** Её слова о точечных Cs-137 «впритык», 02.11.2026: «Если есть расхождения, то правда в моих спектрах.»

### ⛔ Приём нового спектра в корпус (`AMBER314`)

Распоряжение Amber 06.11.2026, дословно: **«Принимай новые правила. Если спектр подходит под все критерии алгоритма FSA, в нем известна геометрия и нет проблем с всякими статистическими критериями, то этот спектр принимается в корпус. Только дополнительно нужно сделать перекалибровку своими силами по энергии и по ПШПВ.»** Уточнения вопросником того же дня, дословно: критерии — **«Сторож по окну FSA (Рекомендую)»**; паспорт вне допуска — **«Не принимать вне допуска»**; связь с правилом 02.11 — **«Дополняет (Рекомендую)»**; срок — **«После этой волны (Рекомендую)»**.

* **Проход считает сторож, а не глаз:** геометрия и матрица есть, разбор без отказа, не меньше 2 опор шкалы, нет строки «фон больше спектра», нет переполнения, главная линия значима; паспорт, если он есть, в допуске. Пороги утверждаются таблицей на нынешнем корпусе (`AMBER314`).
* **Калибровка по энергии и FWHM — своими силами**, корпусным конвейером, а не из файла.
* **Правило дополняет «Корпус старше модели», а не заменяет его.** Принятый спектр не даёт права менять физику, данные и умолчания: это по-прежнему через критиков. Новый спектр двигает калибровки своей группы (раздел 1.2 `tools/CORPUS/README.md`), поэтому после приёма база снимается заново.
* **Пересборка читает библиотеку Amber только с её разрешения** (правило 16.08.2026 в силе).

### The corpus is the unit of measurement

**Every measurement runs over the whole corpus — `tools/CORPUS/corpus/spectra`, all 131
spectra of all 19 detector groups. Numbers obtained on a subset do not go into the journal and
do not support a conclusion.**

The reason is in the journal: the nine spectra the earlier results rested on were three similar
scintillators inside one third of a resolution decade, and conclusions drawn there did not
survive contact with the rest. The corpus spans 0.19 % (HPGe) to 12.2 % (RC-101) FWHM at
662 keV, 1024 to 16384 channels and 0.0186 M to 251 M counts precisely so that a criterion cannot
be tuned to the middle of the range without it showing. A criterion that helps on ASN16 and
falls apart on HPGe is not an improvement, and only the full corpus makes that visible.

`corpus/manifest.csv` is the truth table (what each sample contains), `corpus/detectors.csv`
the per-group resolution model and range, `corpus/devices/` the device configs the spectra
reference. Rebuild the corpus with `scripts/build_corpus.py`, check it with
`scripts/check_corpus.py`, assemble the per-detector run workdirs (`scripts/wd_*`, gitignored)
with `scripts/mkconfig.py`.
