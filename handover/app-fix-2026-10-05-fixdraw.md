# Полоса «fixdraw» (05.10.2026): отрисовка спектра — AMBER179, AMBER182, AMBER172(б)

Постановки — `TODO.md` (`AMBER179`, `AMBER182`, `AMBER172`), разбор
`handover/app-bug-review-2026-10-05.md` §6.1, §6.4, §2.6(б), перепроверка —
`handover/app-fix-2026-10-05-coordinator.md`. Правки точечные; `EnergySpectrumView.cs`
правили и соседние полосы — их места не тронуты.

## Как мерено

Проба `FixDrawProbe.cs` (лежит в `D:\BqMoni_Claude\fixdraw\probe\`, в дерево не клалась: сторожу
не нужна). Собирает НАСТОЯЩИЙ `EnergySpectrumView` без окна (`ResultDataList` +
`ActiveResultDataIndex`, `ClientSize` 1000×500), рисует кадр настоящим `DrawChart`
(отражением, исключения разворачиваются из `TargetInvocationException`) на спектре корпуса
`RC103_Th232WT20.xml` (есть фон в другой калибровке). Две сборки одного дерева: «до» — три
правки откачены, «после» — с правками; обе из `D:\BqMoni_Claude\fixdraw\src*` (в основном
дереве во время полосы не собирался чужой `Resources.Designer.cs` — недостающие ключи ресурсов
добавлены только в копии). Вывод: `D:\BqMoni_Claude\fixdraw\before.txt` / `after.txt`.

## AMBER179 — метка опоры стабилизатора в логарифмической шкале

* Было: `EnergySpectrumView.cs` `ShowCalibrationPeaks` — ветвь логарифма брала `Log10(num3)`, но
  вычитала `totalMinValuePow` и делила на `valueRangePow` (присваиваются только в степенной
  шкале) → деление на 0 → `(int)` = `int.MinValue` → `OverflowException` в `g.DrawLine` каждый кадр.
* Сделано: Y считается общим `GetSpectrumValueY(num3)` (знает три шкалы; те же защиты
  `num3 <= 0`), три самодельные ветки удалены.
* Замер (канал 232, значение 17652, y — низ оранжевой линии метки в кадре против y общего метода):
  * лог-шкала, до: `OverflowException` (кадр не нарисован), `valueRangePow = 0`;
  * лог-шкала, после: исключения нет, низ линии 194 = y общего метода 194 (±1 px);
  * контроль, линейная: до 433 / после 433; степенная: до 287 / после 287 (равны y общего метода);
    тем самым линейная и степенная шкалы прежние.
* Экран: стабилизатор пиков без прибора не включить, экранная проверка не делалась; проба идёт
  через настоящий `DrawChart` с `ResultData.CalibrationPeaks` — тем же путём, каким рисует окно.

## AMBER182 — `NullReferenceException` в континууме после снятия фона

* Было: `PrepareViewData` обновлял `backgroundEnergyCalibration` / `backgroundNumberOfChannels`
  только при наличии фона; после снятия фона калибровка оставалась, и `DrawLineChart`
  (`backgroundEnergyCalibration != null && isBackground`, ось каналов) разыменовывал
  `backgroundEnergySpectrum` = null.
* Сделано: `else` у `if (this.backgroundEnergySpectrum != null)` в `PrepareViewData` обнуляет
  калибровку и число каналов фона. Читатели на null проверены: `:~764`, `:~4915` (панель
  выделения и курсор) идут под `bgTime > 0` / `bg_spectrum != null`; `:~1455`, `:~2972`,
  `:~3289` сами проверяют калибровку на null; `:~2354` — под `backgroundEnergySpectrum != null`.
* Замер (линия, ось каналов, «Показать континуум», фон был → снят → кадр):
  до — `NullReferenceException`, после — исключения нет; контроль: с фоном кадр без
  исключения, 61709 точек не цвета поля в обеих сборках (континуум рисуется как раньше);
  столбики (`BarChart`): исключения нет ни до, ни после (калибровки равны — ветка не берётся).
* Попутно проверено и НЕ дефект: континуум на оси каналов при фоне в другой калибровке не
  сдвигается — вид держит фон переложенным в шкалу спектра (`AMBER109`); 30 колонок из 30 на
  ожидаемой высоте, отклонение 0 px в обеих сборках.

## AMBER172(б) — больше 16 спектров роняют перерисовку

* Было: `SpectrumColorList[i]` на 16 цветов в `EnergySpectrumView.cs` (рисование неактивных
  спектров), `DCSpectrumListView.cs` (`ShowSpectrumList`, `table1_SelectionChanged`) —
  `ArgumentOutOfRangeException` на 17-м спектре.
* Сделано: метод `ColorConfig.GetSpectrumColor(int index)` (`ColorConfig.cs`; метод, а не
  свойство — в XML не попадает) — индекс по кругу `i % Count`, отрицательный и пустой список
  защищены; три вызова переведены на него. Остальные обращения к `SpectrumColorList`:
  `GlobalConfigForm.cs` — фиксированные индексы 0…15 окна настроек (список ≥ 16 гарантирует
  `GlobalConfigManager.cs:745`), `GlobalConfigManager.cs`/`EnergySpectrumView.cs:518` — только
  `Count`; не тронуты.
* Замер: до — вид с 17 и 20 спектрами и `DCSpectrumListView.ShowSpectrumList` на 17 и 20 —
  `ArgumentOutOfRangeException`; после — без исключений; `GetSpectrumColor(0, 15, 16, 17, 19, 20,
  31, 32)` = `SpectrumColorList[i % 16]` во всех восьми случаях. Контроль: прямая индексация
  `[16]` по-прежнему падает (мерка не пустая).

## Файлы

`BecquerelMonitor/EnergySpectrumView.cs` (три точечных места), `BecquerelMonitor/ColorConfig.cs`
(+метод), `BecquerelMonitor/DCSpectrumListView.cs` (два вызова). `.resx` не трогались.

## Приёмка `check_all.py`

Код 1: отказали 5 из 44 — не от этих правок. `check_fsa_report_view`, `check_corpus_scenes`,
`check_fsa_showcase` — «КАТАЛОГ ПРОБ ПРОТУХ (T226)»: в дереве изменено 58 файлов за заход (все
полосы), канонический `probes\build` собирается распорядителем; `check_curve_generation` —
самопроверка не назвала две порчи (`GenerationNotes`, файлы конструктора — не моей полосы);
`check_registry` — 29 находок о ссылках в `TODO.md`/`DONE.md` (реестр, не код).
