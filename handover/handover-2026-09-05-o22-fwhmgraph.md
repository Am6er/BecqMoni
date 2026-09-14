# A243 — `FWHMCalibrationGraph.Init` без сторожа `null`: замер достижимости

Полоса **О22**, 05.09.2026. Ветка `pie`. Журнал ведётся ПО ШАГАМ, а не в конце.

Артефакты: `handover/o22-fwhmgraph/`.

---

## Шаг 0. Расхождение посылки с реальностью — путь к файлу

Строка `A243` называет файл `BecquerelMonitor/FWHMCalibrationGraph.cs`. Такого файла
**нет**: класс лежит в `BecquerelMonitor/Utils/FWHMCalibrationGraph.cs`
(пространство имён `BecquerelMonitor.Utils`, `.csproj:674`). Правка сделана по
настоящему пути.

## Шаг 1. Что именно разыменовывается в `Init` — разбор до замера

```
51  public void Init(FwhmCalibration fwhmCalibration, int maxchannel)
52  {
53      this.energyCalibration = (PolynomialEnergyCalibration)this.mainForm.ActiveDocument
54                                   .ActiveResultData.EnergySpectrum.EnergyCalibration.Clone();
55
56      this.maxChannels = maxchannel;
57      this.fwhmCalibration = fwhmCalibration.Clone();     // <- ГОЛОЕ разыменование АРГУМЕНТА
58      this.points = CalibrationPeak.ClonePeaks(this.fwhmCalibration.CalibrationPeaks);
...
```
(нумерация файла: 53 — цепочка `mainForm…EnergyCalibration`, 56 — `fwhmCalibration.Clone()`)

Голых разыменований в методе **два разных рода**, и строка `A243` называет только
первый:

* **род I — аргумент** (`fwhmCalibration.Clone()`, файл:56). Это и есть `A243`.
* **род II — цепочка через `mainForm`** (файл:53): `ActiveDocument`,
  `ActiveResultData`, `EnergySpectrum`, `EnergyCalibration` — четыре звена без
  сторожа.

## Шаг 2. Кто зовёт `Init` — сплошной поиск по дереву

`grep -rn "FWHMCalibrationGraph" --include=*.cs .` — вызов `Init` **один**:

| место | что |
|---|---|
| `DCFwhmCalibrationView.cs:986-987` | `new FWHMCalibrationGraph(this.mainForm)` + `graph.Init(fwhmCalibration, …)` |
| `tools/effmaker/probes/FwhmViewReachProbeO13.cs:422` | проба (не приложение) |

Над вызовом стоит сторож, поставленный при закрытии `A236` (`DCFwhmCalibrationView.cs:976-984`):

```
976  ResultData graphResultData = mainForm.ActiveDocument != null ? mainForm.ActiveDocument.ActiveResultData : null;
977  EnsureFwhmCalibration(graphResultData);
978  if (fwhmCalibration == null || graphResultData == null
979      || graphResultData.FwhmCalibration == null
980      || graphResultData.EnergySpectrum == null)
981  { UpdateCalibrateButtonState(); return; }
```

Он закрывает род I (аргумент — то самое поле `fwhmCalibration`) и три звена рода II
из четырёх. **Не закрыто последнее звено — `EnergySpectrum.EnergyCalibration`.**
Это и стало вторым предметом замера.

⚠ Кнопку `viewCalibrationButton` `UpdateCalibrateButtonState` (258-288) **не гасит**
ни при какой кривой — то есть человек её нажимает всегда, и весь вопрос в стороже.


## Шаг 3. Замер. Проба расширена сценой «ГРАФИК» (`SceneGraphInitA243`)

Расширена `tools/effmaker/probes/FwhmViewReachProbeO13.cs` — та самая проба, которой
мерилась `A236`: у неё уже есть безоконный стенд (документ, `MainForm` через
`GetUninitializedObject`, обе карты примитивов ROI ДО менеджеров-одиночек) и вызов
настоящей двери в своём потоке STA с таймаутом.

Добавлено: `Stand.InitRaw(cal)` — зовёт `Init` НАПРЯМУЮ с любым аргументом и после
вызова читает **поля самого графика** (`fwhmCalibration`, `points`, `originalpoints`,
`maxChannels`, `maxFWHM`); `Stand.KillEnergyCalibration()`; `Stand.Channels`.

Прогон ДО правки: `handover/o22-fwhmgraph/o22-1-probe-before.txt`, код возврата **0**
(«ОБА ПЛЕЧА СОШЛИСЬ»).

### Положительный контроль, плечо 1 — проба ДОСТАЁТ до `Init`

```
ПЛЕЧО 1. Init(живая кривая, 1024)
    исход: БЕЗ ОТКАЗА
    след ВНУТРИ графика: fwhmCalibration есть, points 3, originalpoints 3,
                         maxChannels 1024, maxFWHM 23.573
    [КОНТРОЛЬ] СОШЁЛСЯ  `Init` ИСПОЛНИЛСЯ — поля графика заполнены
```
След не «объект создался», а строки 56-62 тела: три точки доехали до `points` И до
`originalpoints`, `maxFWHM` посчитан `RecalculateCalibration`.

### Положительный контроль, плечо 2 — проба ЛОВИТ настоящее падение

```
ПЛЕЧО 2а. Init(null, 1024) — род I, аргумент
    исход: ОТКАЗ  NullReferenceException @ FWHMCalibrationGraph.cs:56 (Init)
ПЛЕЧО 2б. Init(живая кривая) при EnergyCalibration == null — род II
    исход: ОТКАЗ  NullReferenceException @ FWHMCalibrationGraph.cs:53 (Init)
```
Оба падения названы ПО СТРОКЕ. Правка чужого файла для этого не понадобилась: задание
допускает прямой вызов отражением, и он оказался достаточен.

### Замер достижимости через НАСТОЯЩУЮ дверь

| сцена | дверь `ViewCalibrationButton_Click` | вывод |
|---|---|---|
| кривая ПУСТА (`FwhmCalibration == null`) | БЕЗ ОТКАЗА — сторож 978-984 вернул | **род I НЕДОСТИЖИМ** |
| кривая есть, `EnergySpectrum.EnergyCalibration == null` | ОТКАЗ `NullReferenceException @ FWHMCalibrationGraph.cs:53` | **род II ДОСТИЖИМ** |

⚠ Контроль, что дверь на пустой кривой не просто «промолчала»: на ЖИВОЙ кривой та же
дверь **повисает на `ShowDialog`** (сцена «ЖИВАЯ», плечо О13в), то есть она доходит до
окна, когда данные есть. Значит «БЕЗ ОТКАЗА» на пустой — это возврат сторожем, а не
немой отказ пробы.

⚠ Кнопка `viewCalibrationButton` на пустой сцене — `Enabled`. Путь к двери у человека
открыт всегда; держит только сторож.

## Шаг 4. Что сделано и чего НЕ сделано

### Род I (то, о чём строка `A243`) — сторож НЕ поставлен, и это решение замера

`FWHMCalibrationGraph.cs:56` (`fwhmCalibration.Clone()`) остаётся голым **нарочно**:
зовущий один, и путь к нему перекрыт сторожем `A236`. Ставить второй сторож поверх
доказанно недостижимого пути — ровно та слепая правка, из-за которой `A236` и
заводилась.

⚠ Голый `return` внутри `Init` был бы к тому же ХУЖЕ падения: `Init` — `void`, объект
остался бы с `fwhmCalibration == null`, `ShowDialog` дошёл бы до `OnPaint` →
`RecalculateCalibration()` → тот же `NullReferenceException`, но уже в отрисовке, где
у него нет ни имени, ни повода. Честный сторож здесь потребовал бы менять сигнатуру
(`bool Init`) — цена, которой недостижимое место не стоит.

### Род II — ДОСТИЖИМ, закрыт сторожем у вызывающего, по образцу `A236`

`BecquerelMonitor/DCFwhmCalibrationView.cs:978-985` — в существующий сторож добавлено
**четвёртое звено**:

```csharp
    || graphResultData.EnergySpectrum.EnergyCalibration == null)
```

Почему у вызывающего, а не в `Init`: образец `A236` — сторож стоит там, где ещё есть
что не делать (окно просто не открывается), а не там, где уже поздно.

Происхождение состояния «спектр без энергокалибровки»: **измерено не в этом заходе,
а полосой `A95`** и записано в коде `EnergySpectrum.Clone()` — `CheckDocument` без
поправок судит такой документ негодным, `FsaAnalyzer.Analyze` отказывается его
разбирать. Двери `CreateDocument`/`OpenDocument` при ответе «Нет» возвращают `null`
(документ не открывается), а ввозные двери (`DocumentManager.cs:1226`, `1472`) уходят
`return`-ом из `void`, оставляя документ ОТКРЫТЫМ. Этот последний путь своим замером
здесь не воспроизводился (безоконно `AppUi.AskYesNo` бросает, ответить за человека
некому) — но сторож стоит бесплатно, и его цена не зависит от частоты пути.

## Шаг 5. Приёмка — падение ушло

Прогон ПОСЛЕ правки: `handover/o22-fwhmgraph/o22-2-probe-after.txt`, код **0**.

| сцена | ДО правки | ПОСЛЕ |
|---|---|---|
| дверь, кривая ПУСТА | БЕЗ ОТКАЗА | БЕЗ ОТКАЗА |
| дверь, `EnergyCalibration == null` | `NRE @ FWHMCalibrationGraph.cs:53` | **БЕЗ ОТКАЗА** |
| прямой `Init(null)` | `NRE @ …:56` | `NRE @ …:56` |
| прямой `Init` при пустой энергокалибровке | `NRE @ …:53` | `NRE @ …:53` |

⛔ Две нижние строки — не остаток, а **доказательство, что уход не от слепоты пробы**:
на той же самой сцене прямой вызов `Init` падает по-прежнему и по-прежнему называет
строку; изменилось только то, что дверь до него не доводит.

Сборка приложения — код 0; `build_all.ps1` — код 0 (112 проб, сторож полосы `S101`
сошёлся, описания FSA сверены).

## Шаг 6. Находки

1. **Расхождение посылки** — путь файла в `A243` неверен (см. шаг 0). Строки не
   завожу: поправляется текстом закрытия.
2. **Факт без «Сделать»**: `PeakShapePreviewGraph.Init`
   (`BecquerelMonitor/Utils/PeakShapePreviewGraph.cs:60-66`) — соседнее окно того же
   разряда, зовётся из того же вида (`ViewPeakShapeButton_Click`). Дефекта НЕТ: там
   `fwhmCalibration?.Clone()` и `title ?? string.Empty`, цепочки через `mainForm` в
   методе нет вовсе. Проверено сплошным чтением метода, строки не завожу.
3. **Факт без «Сделать»**: кнопка `viewCalibrationButton` не гасится
   `UpdateCalibrateButtonState` ни при какой кривой (в отличие от
   `getAllPeaksButton`/`executeCalibrationButton`). Дефектом не является — сторож
   двери отрабатывает первым, измерено обеими сценами; строки не завожу.

**Итог по строкам реестра: 0 заведено, 1 закрыта (`A243`).**
