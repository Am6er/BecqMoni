# П53 (13.09.2026): `AMBER24` — свои шаблоны детекторов в редакторе геометрий

Ветка `pie`, дерево на HEAD `d5f86eda` (полоса стартовала на `5f06b05f`; сосед П50
закоммитил физику 18 по ходу). Артефакты — `handover/p53-amber24/`.

## Постановка (Amber, 13.09.2026, консоль, дословно)

«в редакторе геометрий рядом с combobox выбора шаблонов детекторов добавить
возможность удаления, сохранения, клонирования текущей геометрии детектора.
Существующий список геометрий изменить нельзя. Можно только сохранить/удалить
свою. Эти геометрии не привязаны к конкретному Device, они доступны для любых
устройств созданных в DeviceConfigForm.» Хранение: «Тогда пусть этот конфиг xml
располагается в config».

Решения вопросником 13.09.2026 (дословно): состав — «Только детектор, как вшитые»;
кнопки — «Список помнит выбранный»; файл — «Рядом с GeometryMaterials.xml в config/».
Вшитые (`GeometryPresets`, 7 детекторов) не меняются.

## Что сделано

### Детекторная часть — по коду `GeometryPresets.Build`, не додумано

Пресеты задают ровно эти поля `GeometryModel` (помощники `Box`/`Cylinder`,
`Wrapping`, `Crystal`, `Reflector`, `Fwhm` и прямое `FrontGapThickness = 21.7` у 80x80):

| группа | поля |
|---|---|
| кристалл | `Shape`, `CrystalDiameter`, `CrystalHeight`, `CrystalBoxX`, `CrystalBoxY`, `CrystalBoxZ` (+ `DropDeadCrystalSize`, `A94`) |
| обвязка | `FrontReflectorThickness`, `SideReflectorThickness`, `FrontGapThickness`, `SideGapThickness`, `FrontCladdingThickness`, `SideCladdingThickness`, `MountingThickness` |
| вещества | `Crystal`, `Reflector`, `Gap`, `Cladding` (имя, плотность, состав — у порошка плотность своя) |
| разрешение | `FwhmAt662Percent` |

НЕ переносятся (пресеты их не трогают): `Name`, `IsScintillator`, `Facing`, `InShield`,
`SourceType`, `Scene`, все поля источника/сосуда/пробы, `BeakerWall`, `Source`.
Это и есть шаблон — `GeometryTemplate` (`FromModel`/`CaptureFrom`/`Apply`/`Fingerprint`).

### Файлы

* **новый** `BecquerelMonitor/EfficiencyMaker/GeometryTemplateStore.cs` — `GeometryTemplate`,
  `GeometryTemplateConfig` (`<GeometryTemplates><Templates><Template Name="…">`),
  `GeometryTemplateStore`: `FilePath` через `Package.GetInstance().GeometryTemplates`,
  `PathOverride` (подмена пути для проб), `LoadError`, `Reload`, `Items`, `Find`,
  `IsBuiltinName`, `NameConflict` (Empty/Builtin/Taken, без учёта регистра),
  `FreeName` («имя 2», «имя 3»…), `Add`/`Replace`/`Remove` — запись сразу в файл.
  ⚠ `Replace`/`Remove` — ПО ИМЕНИ, не по ссылке: панель держит объект из списка,
  каким он был при постройке, а после `Reload` (второй открытый редактор) в
  хранилище другой объект того же имени; по ссылке правка уходила в файл прежними
  полями — поймано пробой на первом же прогоне (плечо 2, «файл не изменился»).
* `BecquerelMonitor/Package.cs` — `GeometryTemplates`: `%AppData%\BecqMoni\config\GeometryTemplates.xml`
  (ClickOnce) / `<сборка>\config\GeometryTemplates.xml` (портативно). Не в `config\device`.
* `BecquerelMonitor/GeometryEditorPanel.cs`:
  * список `presetCombo`: подсказка → 7 вшитых → свои (в порядке файла), свои
    помечены `«имя (свой)» / «name (own)»` обёрткой `OwnTemplateRow` (имя в файле
    остаётся чистым);
  * `PresetChanged` больше НЕ сбрасывает индекс на 0 — список помнит выбранный;
    `SetModel` (чужая геометрия приехала) ставит подсказку;
  * ряд кнопок под списком (в одну строку три русские подписи в 620 точек не
    влезают): «Сохранить» / «Клонировать...» / «Удалить», ширина по подписи
    (`GetPreferredSize`), подсказки-тултипы; форма кристалла и всё ниже сдвинуты на
    28 точек (`ShapeRowTop`, `ShapeTop` — константы, не числа по месту);
  * `SaveTemplate()` — переписать СВОЙ выбранный текущими полями (на вшитом/подсказке
    кнопка неактивна, вызов даёт false); `CloneTemplate()` — новый свой из текущих
    полей, имя из диалога (`DeviceConfigForm.AskName`, открыт `internal`), предлагается
    свободное («RadiaCode-101 2»), после — новый выбран; `DeleteTemplate()` — свой
    выбранный с подтверждением (OKCancel), после — подсказка, поля не трогаются;
  * отказы (пустое имя, имя вшитого, имя своего, битый файл, не записалось) — через
    `AppUi.Report`: окно в приложении, строка в поток ошибок у пробы;
  * точки подмены для проб: `TemplateNamePrompt`, `TemplateDeleteConfirm`; без окон и
    без подмены — исключение, как `AppUi.AskYesNo`;
  * битый файл: `LoadError` непуст → в списке только вшитые, «Клонировать»/«Сохранить»
    ОТКАЗЫВАЮТ с причиной (поверх непрочитанного файла не пишем — довод
    `GeometryMaterialStore`);
  * для проб: `SelectPresetByName`, `SelectedOwnTemplate`, `SelectedBuiltinPreset`,
    `PresetRowTexts`.
* `Properties/Resources.resx` / `.ru.resx` / `Resources.Designer.cs` — 14 строк
  `GeometryEditorTemplate*` (кнопки, тултипы, пометка, заголовок диалога,
  подтверждение, четыре отказа, ошибка записи, ошибка чтения); BOM+CRLF сохранены
  (сверено байтами).
* `BecquerelMonitor.csproj` — строка `Compile Include` для нового файла.
* **новая проба** `tools/effmaker/probes/GeometryTemplateProbe.cs`.

### Попутная находка — починена в той же полосе

По-русски подпись «Кристалл — параллелепипед» (переключатель на x=190) шире 146
точек и НАЛЕЗАЛА на список стороны к пробе (`facingCombo` стоял числом на x=336) —
видно на первом снимке `panel_ru.png` этой полосы; по-английски сходилось. Не моя
правка вскрыла (x те же), просто снимок ru раньше глазами не смотрели. Теперь левый
край списка — `boxRadio.Left + PreferredSize.Width + 8` (ru: 372), правый край
прежний 604; проба меряет `boxRadio.Right <= facingCombo.Left` в обеих культурах.

## Приёмка — числами (`handover/p53-amber24/GeometryTemplateProbe.txt`)

`GeometryTemplateProbe.exe --dir=D:\BqMoni_Claude\p53\probe` — **код 0, проверок 90,
расхождений 0**; путь файла подменён, `%AppData%\BecqMoni` не тронут.

1. Круг: A = RadiaCode-101 → «Клонировать» как X (предложено «RadiaCode-101 2»):
   в списке 9 строк, последняя «X (own)»; файл записан; после `Reload` X найден,
   `Fingerprint(X) == Fingerprint(A)`; вне детекторной части XML геометрии побайтно
   тот же (источник: цилиндр 77/33 мм, вода — целы); вшитый B = Pro 80x80 → поля = B;
   выбрать X → поля = A.
2. «Сохранить» на X после правки стороны бруска 12.5 — файл изменился,
   перечитанный X несёт 12.5, в файле `<CrystalBoxX>12.5</CrystalBoxX>` (точка).
   Положительный контроль: на вшитом «Сохранить»/«Удалить» неактивны и возвращают
   false; вшитые A и B после всего тождественны коду (отпечаток до/после); клонировать
   под «RadiaCode-101», «RADIACODE-101», «x» (свой X, другой регистр), «   » — четыре
   отказа с названной причиной; отказ человека — false без беды; своих по-прежнему 1.
3. «Удалить» X: отказ подтверждения — X остался (спрошено про «X»); подтверждение —
   X снят из списка (8 строк) и из файла (`Find("X") == null`, своих 0), выбрана
   подсказка, поля детектора не тронуты.
4. Битый XML: `LoadError` = «There is an error in XML document (1, 91)», своих 0,
   редактор строится, вшитый выбирается, клонирование поверх — отказ, файл не
   перезаписан; без файла — ошибки нет, своих 0.
5. `config\device` пробы: файлов до/после 0 = 0; файл шаблонов лежит в `config\`,
   штатный путь `…\config\GeometryTemplates.xml`, без `\device\`.
6. Разметка en/ru (снимки `panel_en.png`, `panel_ru.png`): подписи в кнопках —
   en 32/43/38 px в 72/72/72, ru 60/82/50 px в 72/93/72; правый край ряда 388 (en)
   / 409 (ru) ≤ 640; кнопки под списком, форма кристалла (74) ниже кнопок (61).
   `GeometryLayoutProbe` — код 0, 56 состояний, уползших нет.
7. `check_resx.py`, `check_resx_designer.py`, `check_resx_letters.py`,
   `check_resx_zorder.py`, `check_headless.py` — все 0; `python tools/check_all.py`
   — **код 0, 37 из 37 сторожей** (`check_all_tail.txt`).

Сборка: приложение Debug в `bin\Debug_p53` — 0 ошибок, 0 предупреждений в моих
файлах; `build_all.ps1 -Out build_p53` — код 0, 187 проб.

Отказов, названных панелью без окон (перехваченный поток ошибок пробы): 5 строк,
все — ожидаемые плечи отказа.

## Что осталось / для реестра

* Строка `AMBER24` — дописка (текст в отчёте полосы), закрывает только Amber.
* `tools/effmaker/probes/README.md` не дополнен строкой о `GeometryTemplateProbe`:
  файл был в чужих незакоммиченных правках (П50) на старте полосы — чтобы не
  столкнуться в одном файле. Одна строка, дописать при следующем касании README.
* Тултипы кнопок и тексты отказов — английский первичен, русский второй, оба заведены.

## Снять после приёмки

`BecquerelMonitor\bin\Debug_p53`, `BecquerelMonitor\obj\Debug_p53`,
`tools\effmaker\probes\build_p53`, `D:\BqMoni_Claude\p53\` — снимаются полосой
(правило 13.09.2026); артефакты уже в `handover/p53-amber24/`.
