# П275 — мелкие находки итогового экрана П273 (14.10.2026)

Полоса ночного захода 13→14.10.2026. Постановка — распорядитель, по находкам 2–6 журнала
[handover-2026-10-14-p273-final-screen.md](handover-2026-10-14-p273-final-screen.md); решения Amber не
требовались. Коммитов полоса не делала.

## Что сделано

1. **Разделитель-запятая в «Isotope Database».** Энергия и выход лежат в ячейках числом `double` (их так
   читает импорт и сортирует колонка), печатала их `DataGridView` культурой потока: «111,238», «727,33» и в
   en, и в ru. Поставщик формата `InvariantCulture` поставлен на стиль трёх таблиц формы (`NucBase.InitGrids`);
   значение ячейки не тронуто, разбора текста обратно нет (колонки только для чтения). Попутно колонка периода
   показывает единицу на языке интерфейса через `CellFormatting` — «60.55(мин)»; текст ячейки «60.55(m)» —
   договор с разбором `HalfLifeYearsFromCell`, он прежний.
2. **«Backscatter» в русском отчёте FSA.** Подпись — в `FsaPalette.DisplayName` (там же «Наложения»,
   «Рентген …»), а не в `FsaResult.BackscatterLayerName`: служебное имя слоя прежнее (по нему цвет, поиск
   построителя, пробы), человеку — ресурс `FSABackscatterName` (en «Backscatter», ru «Обратное рассеяние» —
   как переключатель окна отчёта). Комментарий константы в `FsaResult.cs` дописан.
3. **Карточка NucBase в ru.** Подпись периода «λ:» → «T½:» (λ — постоянная распада; показывается именно
   период — `NucBaseFramework.HalfLifeCaption`, строка `half_life` базы). Подпись отбора по периоду «λ >» →
   «T½ >» в обеих культурах (отбор `life.Seconds > T` — это период, не λ), подпись сдвинута с 408 на 397, чтобы
   не налезать на поле. Единицы периода — ресурсами `NucBase_Unit*` (en — коды базы, побайтно как было; ru —
   нс, мкс, мс, с, мин, ч, сут, лет; образец «лет» — поле периода редактора нуклидов), «STABLE» →
   `NucBase_HalfLifeStable` (ru «стабилен»). Пункты единиц у поля отбора переведены в `NucBase.ru.resx`, а
   пересчёт берёт КОД по месту пункта (`NucBase.FilterUnits`), не текст пункта. Раскладка: `label13` 136 → 160,
   `IsotopeSpecActivity` 225 → 252 (en; длинные периоды «> 3.705e-13 ns» налезали и в en), ru —
   `IsotopeSpecActivity` 286, `IsotopeAbundance` 437.
4. **Редактор нуклидов.** `table1_EditingStopped` сверяет закрытое значение ячейки с определением
   (`NumberEdited`: то же `Convert.ToDecimal`, что делает редактор ячейки); совпало — определение не трогается,
   «изменено» не взводится, вопроса «Сохранить?» нет. Попутно период вида 5.808599695585997E-06 больше не
   проходит круг double → decimal → double (терял 16-ю цифру). Поле периода 80 → 140 точек, подпись «years/лет»
   187 → 247: «5.808599695586E-06» видно целиком.
5. **Косметика.** (а) Поле предела счёта: 64 → 90 точек (нужно 90 на «2147483647» по мерке пробы). В прежнем
   месте между колонками до «Chart Refresh Cycle» было 84 точки — поле и подпись перенесены строкой правой
   колонки под «Привязку масштаба» (en: подпись 276,83, поле 395,80; ru: 301,83 / 436,80). (б) Причина отказа
   FSA в окне результата: перечёркивали её линии сетки колонок — дефект `XPTable/Models/Table.cs`:
   `GetWholeLineFlags` обходил строки `irow < bottomRow` и при строках, кончающихся выше низа таблицы, не
   обходил последнюю (единственную) — охват `ColSpan` не учитывался. Обход теперь включает последнюю строку
   (и `PaintBrokenGridColumn` — -1 у `RowIndexAt`). `DCResultView.cs` (полоса П274) не тронут. Обрезка справа
   на снимке П273 — узкая панель с горизонтальной прокруткой всей таблицы, а не перенос: при ширине панели
   экрана текст в три колонки умещается.

## Чем подтверждено

* Проба `tools/effmaker/probes/NucBaseCardProbeP275.cs` (без окна, ru-RU и en-US): числа таблицы с точкой
  (положительный контроль — голая таблица на ru-RU печатает «727,33»), ячейка хранит `double`, разбор периода
  прежний; подпись слоя; карточка на худших строках базы (длиннейшие период, удельная активность,
  распространённость, предел «>») без налезаний — первый прогон до правки раскладки нашёл 12 налезаний;
  пункты единиц = `HalfLifeUnitCaption(FilterUnits[i])`; редактор: три ячейки открыты/закрыты без ввода —
  `Dirty=False`, «Сохранить» погашена, значения побитово (период с 17 цифрами), вписанная энергия взводит
  «изменено» (контроль); поле периода и предела счёта вмещают число, соседей не задевают. Итог «СОШЛОСЬ», код 0.
* Соседние пробы из каталога полосы: `FsaQualityRowProbe` (раздел 6 сравнивает с переводом имени, см. ниже) —
  «ВСЕ СОШЛИСЬ»; `NuclideEditProbeP266`, `DecayDataProbeP266`, `CultureProbeO14`, `NumericCultureProbeF68`,
  `SmallFixesProbeP269`, `AutosaveEmptyTimeProbeP268`, `ChainProbe`, `FsaPaletteProbe`,
  `FsaResultWindowProbeP254` (оба — на `fsa_showcase/spectra/ASN16_Cs137_house.xml`) — код 0.
  `XrayLinesProbe` — 1 расхождение «W Kβ: 67.4765, ожидалось 68.117», вывод побайтно тот же на каноническом
  каталоге `build` (HEAD) — не от этой полосы.
* Экран (`windows-mcp`, замок `screen.lock` «П275 2026-10-14 06:08:00» взят и снят): своя сборка в
  `D:\BqMoni_Claude\p275\app`, копия `%AppData%\BecqMoni\config` (43 файла, `AtomSpectraVCP.xml` в ней не было),
  en, затем ru (`<Language>ru-RU</Language>` в копии). Набор не начинался; настройки — «Отмена». Увидено:
  Bi-212 «12.148», «727.33», «6.67»; ru — «60.55(мин)», карточка «Распространённость: 0 %», «T½: 60.55 мин»,
  «Удельная активность: 5.42e+017 Бк/г» без налезаний; отбор Cs-137 + ДПР, T½ > 1 «лет» — одна линия 661.657,
  Th-232 + ДПР, T½ > 1 «ч» — 15 линий с Bi-212 (60.55 мин > 1 ч) и без Tl-208; отчёт FSA Cs-137 в домике —
  «Обратное рассеяние — есть»; редактор: Tl-208 «5.808599695586E-06 years/лет», Enter в ячейках энергии и
  периода, переход на соседнюю строку и «Закрыть» — без вопроса, «Сохранить» погашена; предел счёта
  99999999999 → «2147483647» целиком (en и ru); пустой файл (копия `Разное с канала\новый.xml`) — причина отказа
  в три колонки, линии сетки её больше не режут (en до правки `Table.cs` — режут, ru после — нет).
  Снимки — `D:\BqMoni_Claude\p275\shots\` (на диске, не в git): `01_after_en_isotope_db_bi212`,
  `02_after_ru_cs137_report_backscatter`, `03_after_ru_isotope_db_bi212`, `03b/03c/03d_…filter…`,
  `04_after_en_nuclide_editor_tl208_enter`, `04b_after_ru_nuclide_editor_no_save_prompt`,
  `05_after_en_count_limit`, `05b_after_ru_count_limit`, `06_en_result_fsa_reason_row` (до правки сетки),
  `06b_after_ru_result_fsa_reason_row` (после); «до» по пунктам 1–4 — снимки П273.
  Отпечаток `%AppData%\BecqMoni` (45 файлов, путь|размер|mtime|sha256) до и после — совпал; файлы
  `YandexDisk\Спектры` за сеанс не менялись.
* Витрина FSA (`check_fsa_showcase.py --probes=build_p275`): до переобъявления — ровно 4 расхождения, все одной
  строкой SCREEN «строка 2: Backscatter | есть → Обратное рассеяние» у `ASN16_Cs137_house` (infer_eq,
  infer_eq_peak, infer_eq_compton) и `ASN16_Cs137_10cm / sample_nomatrix`; полосы, слои, доли, невязка — без
  изменений; 5 прочих пар совпали. Эталон этих 4 пар переобъявлен `snapshot.ps1 -Only
  ASN16_Cs137_house,ASN16_Cs137_10cm` (2026-10-14 06:20), повтор — «ВИТРИНА СОШЛАСЬ: 9 пар».
* Сторожа resx (`check_resx.py`, `_letters`, `_zorder`, `_designer`) — код 0. `check_all.py` — 42 из 46; красные
  4 (`check_fsa_report_view`, `check_corpus_scenes`, `check_fsa_showcase`, `check_bq_summing`) — код 3, свежесть
  канонического каталога проб (T226, набор исходников разошёлся с заверенным). На каталоге полосы
  `check_fsa_report_view` и `check_bq_summing` (с `--skip-freshness`: каталог протухал от правок `DCResultView.cs`
  полосой П274 во время замера) — код 0.

## Попутно

* `FsaQualityRowProbe`: 11 отказов «χ²/ndf 0.00 вместо 2.94» были и на каноническом каталоге HEAD — сцена не
  задавала `Chi2NdfPoisson`, а строка качества печатает χ² Пуассона (`AMBER209`); поле добавлено, раздел 6
  сравнивает строку с `FsaPalette.DisplayName(BackscatterLayerName)`.

## Не сделано / чужое (текстом распорядителю)

* `FSAReportView.cs` (полоса П274), строка ~1925: пометка «подавлен» печатает `result.SuppressorName` как есть —
  по-русски там будет служебное имя («Backscatter», «pile-up», «Xray-Pb»). Правка: `FsaPalette.DisplayName(
  result.SuppressorName ?? string.Empty)`; ожидание `FsaReportViewProbe.cs:985` — тем же переводом.
* Находка 1 П273 (первое переключение настройки FSA висит на «идёт расчёт» до движения мыши над графиком)
  воспроизвелась и на этой сборке при первом показе вкладки «Cs 137 в домике» — полоса П274.
* Редактор нуклидов, колонка «Полураспад» таблицы: длинные периоды видны хвостом («…99695586E-06»,
  «…255707762557»), после правки ячейки — хвостом десятичной записи; колонка узкая, правка рисовальщика XPTable
  или ширины таблицы — не в этой полосе.
* Калибровка ПШПВ (снимок П273 `12_…`): колонка FWHM показывает длинные неокруглённые числа, обрезанные слева
  («…561517814») — не в этой полосе.
* Поле предела счёта выровнено влево, соседние числовые поля — вправо (`TextAlign`), не трогалось.

## Изменённые файлы

`BecquerelMonitor/NucBase/NucBase.cs`, `NucBase.resx`, `NucBase.ru.resx`, `NucBaseFramework.cs`;
`BecquerelMonitor/FullSpectrumAnalysis/FsaPalette.cs`, `FsaResult.cs` (комментарий);
`BecquerelMonitor/NuclideDefinitionForm.cs`, `NuclideDefinitionForm.resx`;
`BecquerelMonitor/GlobalConfigForm.resx`, `GlobalConfigForm.ru.resx`;
`BecquerelMonitor/Properties/Resources.resx`, `Resources.ru.resx`, `Resources.Designer.cs` (10 ключей в конце:
`FSABackscatterName`, `NucBase_HalfLifeStable`, `NucBase_UnitNs/Us/Ms/S/Min/H/D/Y`);
`BecquerelMonitor/XPTable/Models/Table.cs`; `tools/effmaker/probes/NucBaseCardProbeP275.cs` (новая),
`FsaQualityRowProbe.cs`; `tools/fsa_showcase/reference/ASN16_Cs137_house__infer_eq{,_peak,_compton}.json`,
`ASN16_Cs137_10cm__sample_nomatrix.json`.
