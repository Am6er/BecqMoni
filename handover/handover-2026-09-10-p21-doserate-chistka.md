# Полоса П21, 10.09.2026 — `AMBER13`: исполнены четыре решения Amber по вкладке `DoseRate`

Заход ПРАВОЧНЫЙ, в отличие от разборной П19 того же дня: та измерила цену чистки
и остановилась, эта чистку сделала — потому что решением (1) Amber цену приняла.

Читатель полосы — свой: `tools/p21_view/DoseRateCleanupP21Probe.cs`, четыре
раздела, 14 проверок, три вида порчи. Артефакты — `handover/p21-doserate/`.

⛔ Окно `BecqMoni` не поднималось. И вкладка прибора, и вкладка Efficiency
обмеряны ОТРАЖЕНИЕМ из собранной сборки тем же приёмом, что у П19.

---

## 0. Что мерено и чем

| прогон | сборка | ключ | код | файл |
|---|---|---|---|---|
| ДО правок | `bin\Debug_P21_before` | — | **1** (6 проверок, 5 непрошедших) | `p21-before.txt` |
| ПОСЛЕ правок | `bin\Debug_P21` | — | **0** (14 проверок, 0 непрошедших) | `p21-main.txt` |
| порча «ждём имя, которого нет» | `bin\Debug_P21` | `--sabotage=absent` | **0** (отказ получен) | `p21-sabotage-absent.txt` |
| порча «экспорту отрезана шапка» | `bin\Debug_P21` | `--sabotage=noheader` | **0** (отказ получен) | `p21-sabotage-noheader.txt` |
| порча «кривая не положена в конфиг» | `bin\Debug_P21` | `--sabotage=nosave` | **0** (отказ получен) | `p21-sabotage-nosave.txt` |

⛔ **Один и тот же исходник читателя собран ДВАЖДЫ** — против сборки до правок и
против сборки после. Иначе разности не вышло бы вовсе: «до» просто не
скомпилировалось бы против нового кода. Отсюда правило внутри читателя — всё
НОВОЕ (`ImportLsrmEfficiency`, `efficiencyImportButton`) зовётся отражением по
имени, а не напрямую.

## 0а. Посылка П19 сверена с деревом ПЕРЕД правкой — расхождение НОЛЬ

Числа П19 сняты часом раньше, и сверять их было надо. Прогон «до» даёт
**15 именованных контролов** на `tabPage7`, поимённо теми же: `groupBox3`,
`table4`, `button15`, `button16`, `buttonClearDoseRate`, `labelDREstimateTitle`,
`comboDoseRateEfficiency`, `comboDoseRateSpectrum`, `buttonEstimateDRConf`,
`labelDoseRateValue`, `upDownDoseRateValue`, `labelEffNote`, `buttonLoadEff`,
`labelSpectrumNote`, `buttonLoadDoseRateSpectrum`. **Расхождение с П19 — 0 из 15.**

---

## 1. Решения (1) и (2). Вкладка почищена: 15 → 1

⚠ **Ожидание захода было «стало 3», вышло «стало 1». Разница ровно −2, и она
названа решением (4):** П19 считала остаток ДО того, как Amber решила судьбу
`buttonLoadEff` / `labelEffNote`. Решение (4) уносит эту пару на вкладку
Efficiency, и остаётся один `comboDoseRateEfficiency`.

**Снято 14 контролов из 15:**

| что | почему |
|---|---|
| `groupBox3` → `table4`, `button15`, `button16`, `buttonClearDoseRate` | решение (2) «Снять целиком»: ручная сверка с дозиметром убирается ПОЛНОСТЬЮ |
| `comboDoseRateSpectrum`, `buttonLoadDoseRateSpectrum`, `labelSpectrumNote` | эталонный спектр — вход расчёта, в конфигурации не хранился |
| `upDownDoseRateValue`, `labelDoseRateValue` | объявленная доза эталона |
| `buttonEstimateDRConf`, `labelDREstimateTitle` | кнопка оценки и её заголовок |
| `buttonLoadEff`, `labelEffNote` | решение (4): ввоз ЛСРМ переехал на Efficiency |

**Остался 1:** `comboDoseRateEfficiency`. ⛔ Он не снят НАРОЧНО: перечень (в)
строки называет его единственным, кого не снимают, а ЗАМЕЩАЮТ («на его место —
выбор вида облучения и сцены поля»), а замещение — это пункт (б), то есть
`GeometrySceneKind` в `EfficiencyMaker/GeometryModel.cs`, полосе запрещённом.
Список остался на месте без потребителя: расчёт по кривой ПРОБЫ снят вместе с
кнопкой оценки. Вопрос Amber — снимать ли и его до готовности (б) — вынесен
отдельным списком.

**Полей «поправка к показанию» не заведено ни одного** — решение (2) дословно.

### Что снято в коде

* `DeviceConfigForm.cs`: `table4_EditingStopped`, `button15_Click`,
  `button16_Click`, `buttonClearDoseRate_Click`, `buttonEstimateDRConf_Click`,
  `CalculateDoseRateConfig`, `EvaluateButtonEstimateDRState`,
  `buttonLoadDoseRateSpectrum_Click`, `buttonLoadEff_Click`,
  `FillDoseRateSpectrumCombo`, `OpenSpectrumChoices`,
  `comboDoseRateSpectrum_SelectedIndexChanged`, `BuildDoseRateTab`, поля
  `doseRateSpectrum`, `doseRateToolTip`, `doseRateFileChoice`,
  `doseRateFileCurve`, `doseRateFileCurveName`, чтение и запись точек в
  `LoadFormContents` / `SaveFormContents`, а также помощник `getDouble`
  (единственными его читателями были четыре ячейки `tableModel4`).
* `DeviceConfigForm.Designer.cs`: объявления, создание, `SuspendLayout`/
  `ResumeLayout`, `BeginInit`/`EndInit` и блоки свойств всех снятых, включая
  `columnModel4`, `tableModel4` и четыре `numberColumn5..8`.
* `DeviceConfigForm.resx` — 145 записей, `DeviceConfigForm.ru.resx` — 16.
  `>>comboDoseRateEfficiency.ZOrder` переномерован 2 → 0.

⛔ **`DoseRateEstimator.Estimate` НЕ снят**: пункт (а) строки переводит его с
пиковой эффективности на полную, а не выбрасывает. Снята только кнопка, которая
его звала.

---

## 2. Решение (3) — это ЗАПИСЬ В СТРОКУ, кода полоса не трогала

Текст требования для пункта (б) отдан распорядителю дословно; сам
`EfficiencyMaker/**` занят соседней полосой и не правился.

Суть, чтобы она не потерялась: сегодня строка матрицы отклика нормирована «на
квант, испущенный источником в 4π» (`ResponseMatrix.cs:20`), а `Factor(E)`
намеренно хранится с точностью до общего множителя (`DoseRate.cs:198`), потому
что нормировка на эталон этот множитель сокращает. Снятие эталона сокращение
убирает: сцене облучения ICRP (плоская волна AP/PA/ISO/ROT) нужен отклик на
ЕДИНИЧНЫЙ ФЛЮЕНС (1/см²), а `Factor` — явный множитель перехода к мкЗв/ч.

---

## 3. Решение (4). Ввоз ЛСРМ заведён на Efficiency и КРИВАЯ СОХРАНЯЕТСЯ

Кнопка «Ввоз ЛСРМ...» / «Import LSRM...» — третьим рядом шапки вкладки
Efficiency, отдельно от шести кнопок правки: те шесть работают с ВЫБРАННОЙ
кривой, а эта заводит новую и выбора не требует.

Ввоз разделён надвое нарочно:

* `DeviceConfigForm.ImportLsrmEfficiency(device, path, out problem)` —
  `internal static`, окна не поднимает: разбирает файл прежним общим
  `ReadLsrmEfficiencyExport` и кладёт результат в `device.EfficiencyConfigs`
  как обычную `EfficiencyConfigData` с `Origin = EfficiencyOrigin.Lsrm`;
* `efficiencyImportButton_Click` — диалог, сообщение об отказе, обновление
  списка и дирти-флажок.

Так ввоз проверяется пробой БЕЗ диалога — что и сделано.

**Измерено (`p21-main.txt`, §3):** подставной экспорт из пяти точек, первая
заявлена с погрешностью 554 % и правилом `T174` отсекается; ввезено 4 точки,
происхождение помечено `Lsrm`; конфигурация сериализована тем же
`XmlSerializer(typeof(DeviceConfigInfo))`, прочитана обратно — **кривая найдена
по своему `Guid`, 4 точки, первая 40.00 кэВ, последняя ε = 0.005177**. Это и
есть доказательство «сохраняется»: чтение ПОСЛЕ записи, а не «поле заполнено».

⚠ **Высота шапки — число, и оно устаревало бы молча.** Третий ряд кнопок
прибавил 32 точки, высота панели поднята 166 → 198, и читатель проверяет не
«кнопка создана», а «кнопка ЦЕЛИКОМ внутри шапки»: низ 102 при высоте 198.

⚠ Геометрии у ввезённой кривой НЕТ — экспорт ЛСРМ её не несёт. Это законное
состояние `EfficiencyConfigData`: такой кривой пользуются, но её не
пересчитывают и матрицу по ней не считают.

---

## 4. Цена решения (1) — ЧИСЛОМ, до и после

Гейт `MainForm.ShowDoseRate` (строка 687) показывает дозу ровно при
`DoseRateConfig.DoseRateCalibrationPoints.Count > 0`. Читатель считает, у
скольких конфигураций гейт ОТКРЫТ, разбирая файлы ТЕМ ЖЕ сериализатором, что и
приложение.

| каталог | до | после |
|---|---|---|
| поставочный `config/device` (10 файлов) | **1** — `RC-103.xml`, 36 точек | **0** |
| живой `%AppData%\BecqMoni\config\device` (9 файлов) | **3** — `1.Atom Spectra Nano 16 Pro PM 1610B.xml` (3), `1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml` (3), `RC-103.xml` (36) | **0** |
| ВСЕГО (19 файлов) | **4** | **0** |

То есть **показание мощности дозы пропало у 3 живых конфигураций из 9 и у
поставочного RC-103** — ровно та цена, которую Amber назвала заранее и приняла,
и ровно то число, что измерила П19 (2.566 ±0.000 мкЗв/ч → строка состояния
пуста). Расхождения с П19 нет.

### Как именно перестали читать — и почему НЕ удалением свойства

`DoseRateConfig.DoseRateCalibrationPoints` помечено `[XmlIgnore]`. Что это даёт:

* конфигурация с `<DoseRateCalibrationPoints>` на диске читается БЕЗ них —
  незнакомый элемент `XmlSerializer` пропускает молча;
* при пересохранении элемент не пишется вовсе, то есть «рудимент исчезает» —
  дословно как сказала Amber. Проверено прямо: конфигурации с двумя точками
  сериализуются в XML, где слова `DoseRateCalibrationPoint` нет;
* поставочные `config/device/*.xml` НЕ ТРОНУТЫ (приказ 05.09.2026): 36 точек
  `RC-103.xml` остались на месте, их просто перестали читать.

⛔ **Свойство САМО не снято, и это не мягкость, а необходимость.** Пять файлов
оснастки в запретных полосе каталогах — `tools/effmaker/probes/DoseCoefProbeO2.cs`,
`DosePointsProbeF9.cs`, `DoseRateProbe.cs`, `BoundProbeF59.cs`,
`tools/CORPUS/probes/DeviceConfigProbe.cs` — обращаются к нему по имени;
удаление свойства сделало бы каталог проб несобираемым, а править их полосе
нельзя. Сверх того, свойство — вход БУДУЩЕГО расчёта: пункт (а) переводит
`DoseRateEstimator.Estimate` и `DoseRateManager.Calculate` с пиковой
эффективности на полную, а не выбрасывает их.

⚠ `MainForm.cs` полоса НЕ ПРАВИЛА. Гейт работает как был и сам даёт нужное
поведение: список пуст → `ClearStatusTextRight()`, строка состояния пуста.

⚠ Заодно починен сеттер `DoseRateCalibrationPoints`: он падал
`NullReferenceException` на `value.Sort()` при `value == null` (находка П19 §7,
строкой не ставшая). Теперь `value ?? new List<…>()`.

---

## 5. Что нашлось попутно

⛔ **`tools/effmaker/probes/DoseRateProbe.cs` стал ЧИТАТЕЛЕМ СНЯТОГО.** Его
раздел `TabLayout()` требует, чтобы `comboDoseRateSpectrum` был полем формы и
чтобы в `DeviceConfigForm.resx` лежали `comboDoseRateSpectrum.Location` и
`.Size`; его порчи (`--sabotage=long|word|hidden|overlap`) правят `labelEffNote`
и берут `buttonLoadEff`. После чистки ни того, ни другого нет. **Проба
СОБИРАЕТСЯ** (всё обращение — через отражение и строки), но её раздел вкладки
теперь отказывает, а порчи бросают «на вкладке нет контрола». Каталог
`tools/effmaker/**` полосе П21 запрещён; строка `AMBER13` уже называет
`DoseRateProbe` своей приёмкой, поэтому это дописка в неё, а не новая строка.

⚠ Факт без «сделать»: `DosePointsProbeF9` читает точки из конфигураций на диске
и теперь получит 0 у всех — не отказ, а другое измерение.

⚠ Факт без «сделать»: диалоговый заголовок `EffCalcMCImportDialogTitle`
(«ЛСРМ EffCalcMC.txt → ROI») называет ROI, куда кривая не кладётся с переезда
эффективности из ROI в устройство. Новый ввоз им не пользуется — у него свой
`EfficiencyTabImportLsrmTitle` («ЛСРМ EffCalcMC.txt → кривая эффективности»);
старый ключ остаётся живым у ввоза EffCalcMC в конструкторе кривой.

---

## 6. Приёмка сторожами

Четыре сторожа `*.resx` — **все четыре код 0**:
`check_resx.py`, `check_resx_designer.py`, `check_resx_letters.py`,
`check_resx_zorder.py`. Последний до правки честно ругался на `tabPage7`
(«детей в коде 1, записей в resx 11») и замолчал только после переномеровки
`ZOrder` — то есть он не «всегда зелёный».

`python tools/check_all.py` — **код 0, ВСЕ ЗЕЛЕНЫ: 32 из 32**. ⚠ Заход
предупреждали, что `check_probe_numbers.py` держится чужим
`tools/effmaker/probes/SceneCostProbe.cs`; на момент приёмки соседняя полоса
его починила, и сторож зелен.

**Сборка всех проб оснастки против новой сборки приложения** —
`build_all.ps1 -Bin bin\Debug_P21`, код **0**: «все собрались: 166 файлов
(плюс 5 без Main)». Это и есть доказательство, что снятие кода вкладки не
сделало каталог проб несобираемым (каталог `-Out` был временный, снят
`Remove-Item` после проверки).

⚠ **Чужая правка на полчаса завалила сборку дерева**, и это стоит записать:
`EfficiencyMaker/EfficiencySimulator.cs` соседней полосы звал
`ResponseChannel.Escape511`, которого в `ResponseMatrix.cs` ещё не было
(`CS0117`). Ловушка «старый бинарь»: проба при этом отработала кодом 0 — на
ПРЕЖНЕМ exe, потому что новый не собрался. Все числа этого журнала сняты после
того, как сборка снова стала зелёной, и читатель пересобран тем же движением.

---

## 7. Чем это повторить

```powershell
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$msb  = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$csc  = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$fac  = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\netstandard.dll'

& $msb "$repo\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Debug `
  /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false `
  /p:OutputPath='bin\Debug_P21\' /p:IntermediateOutputPath='obj\P21\'

$bin  = "$repo\BecquerelMonitor\bin\Debug_P21"
$refs = @("/r:$bin\BecquerelMonitor.exe", '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
          '/r:System.Xml.Serialization.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
          "/r:$bin\Microsoft.Data.Sqlite.dll", "/r:$bin\WeifenLuo.WinFormsUI.Docking.dll", "/r:$fac")
& $csc /nologo /target:exe /platform:anycpu /langversion:7.3 /d:TRACE `
       "/out:$bin\DoseRateCleanupP21Probe.exe" @refs "$repo\tools\p21_view\DoseRateCleanupP21Probe.cs"
Copy-Item "$bin\BecquerelMonitor.exe.config" "$bin\DoseRateCleanupP21Probe.exe.config" -Force

# ⛔ запускать ИЗ каталога сборки: форма читает config рядом с собой
Push-Location $bin
$common = @("--devices=$repo\config\device", "--devices=$env:APPDATA\BecqMoni\config\device")
& "$bin\DoseRateCleanupP21Probe.exe" @common                       # ждём 0
foreach ($s in 'absent','noheader','nosave') {
    & "$bin\DoseRateCleanupP21Probe.exe" @common "--sabotage=$s"    # ждём 0 — отказ получен
}
Pop-Location
```

Чтобы получить числа «ДО», тот же читатель собирается против сборки из
`OutputPath='bin\Debug_P21_before\'`, сделанной на дереве до правок.

---

## 8. Файлы полосы

* `BecquerelMonitor/DeviceConfigForm.cs`, `DeviceConfigForm.Designer.cs`,
  `DeviceConfigForm.Efficiency.cs`, `DeviceConfigForm.resx`,
  `DeviceConfigForm.ru.resx` — чистка вкладки и новый ввоз;
* `BecquerelMonitor/DoseRateConfig.cs` — `[XmlIgnore]` и починенный сеттер;
* `BecquerelMonitor/Properties/Resources.resx`, `Resources.ru.resx`,
  `Resources.Designer.cs` — две новые строки `EfficiencyTabImportLsrm`,
  `EfficiencyTabImportLsrmTitle`;
* `tools/p21_view/DoseRateCleanupP21Probe.cs` — читатель (новый);
* `handover/p21-doserate/p21-before.txt`, `p21-main.txt`,
  `p21-sabotage-{absent,noheader,nosave}.txt` — прогоны;
* `handover/handover-2026-09-10-p21-doserate-chistka.md` — этот журнал.

Полоса НЕ трогала: `MainForm.cs`, `DoseRate.cs`, `DoseRateManager.cs`,
`EfficiencyMaker/**`, поставочные `config/**`, `TODO.md`, `DONE.md`.
