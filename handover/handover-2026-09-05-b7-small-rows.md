# Полоса B7, 05.09.2026 — три мелкие строки: `A173`, `A174`, `A178`

Ветка `pie`, коммитов нет. Все три строки закрыты; приёмка — числом и текстом
ниже. `TODO.md` не правился (текст закрытия отдан оркестратору).

## Сборка: дерево не собирается, собиралась копия

В дереве лежит чужой незавершённый кусок `EfficiencyMakerForm.Designer.cs`
(две строки `this.airWarningLabel = …` без объявления поля) — `CS1513`/`CS1061`.
Как и полоса B5: копия дерева в scratchpad (`BecquerelMonitor` без `bin`/`obj`,
`packages`, `nuget.config`, `sln`), в ней ТОЛЬКО этот файл возвращён к `HEAD`
байт в байт (через `cmd /c "git show … > файл"`; сверено SHA256 с `git show`).
В самом дереве файл не тронут.

⚠ Две ямы по дороге, обе мои:

* `git show … | Set-Content -NoNewline` СКЛЕИЛ файл в одну строку (`CS1513`
  на позиции 31477 первой строки). Восстанавливать байты — только
  перенаправлением `cmd`, не через конвейер PowerShell.
* Проект на `PackageReference`: свой `BaseIntermediateOutputPath` (`obj\B7_old`)
  означает, что `project.assets.json` там НЕТ, и сборка падает 112 ошибками
  `CS0246` («MathNet», «WeifenLuo»). Лекарство — ключ `/restore` в той же
  команде MSBuild; пакеты вендорены, сеть не нужна.

Сборки (в копии): `bin\Debug_B7_old` — до правки `Report` (23:40:52),
`bin\Debug_B7` — после (23:41:54); обе exit 0, 0 предупреждений.

## `A174` — `AppUi.Report` печатала «BecqMoni: : текст»

Файл `BecquerelMonitor/GlobalConfigManager.cs`, только метод `Report` (правки B5
по `ReasonWalk` в дереве целы: замена одного блока байтами, CRLF+BOM
сохранены). Разделитель «: » ставится только за непустым заголовком:

```csharp
string head = string.IsNullOrEmpty(caption) ? "" : caption + ": ";
Console.Error.WriteLine("BecqMoni: " + head + AppUi.OneLine(text));
```

Вызовы в `N42/Util.cs` не тронуты (файл чужой), чинилась дверь.

**Мерка** — плечо `заголовок` в `tools/effmaker/probes/ReasonProbe.cs`
(перехват `Console.Error` через `SetError`, три стороны). Проба собрана `csc`
ровно так, как это делает `build_all.ps1` (тот же набор `/r:`), против обеих
сборок; каталоги `tools\effmaker\probes\build_b7_old` и `build_b7`, в каждый
скопировано содержимое соответствующего `bin` плюс `ReasonProbe.exe.config`.
Полный `build_all.ps1` не гонялся: нужна одна проба, а его сторожа заточены
под каталог дерева.

| сборка | без заголовка | с заголовком | две строки | код |
|---|---|---|---|---|
| старая (`build_b7_old`) | `BecqMoni: : в файле N42 …` — **НЕТ** | `BecqMoni: Ввоз N42: в файле N42 …` — ДА | `BecqMoni: : первая вторая` — **НЕТ** | **1** (НЕ СОШЛОСЬ: 2) |
| новая (`build_b7`) | `BecqMoni: в файле N42 …` — ДА | `BecqMoni: Ввоз N42: в файле N42 …` — ДА | `BecqMoni: первая вторая` — ДА | **0** (ВСЕ СОШЛИСЬ) |

Плечо «с заголовком» зелёное на ОБЕИХ сборках — это и есть «байт в байт
прежний формат». Остальные плечи `ReasonProbe` (`A129`/`A144`/`A165`) на обеих
сборках зелёные, как и до полосы.

`AppUi.AskYesNo` строит ту же строку «BecqMoni: заголовок: текст» — проверено:
все шесть вызовов (`DocumentManager.cs`) идут с непустым
`Resources.ResetCalibrationQuestion`, строки не нужно.

## `A173` — «у 1 измерений»

Помощника склонения по числу в дереве нет (искал `Plural`, `Склон`, `WordForm`
по `*.cs` — ноль). Взята форма без склонения. Байты: BOM и 2109 CRLF до и после,
LF-only 0; `Resources.Designer.cs` не изменился (значение, не имя).

* было: `В файле N42 не прочитано время начала набора у {0} измерений (первое: {1}); вместо него подставлено текущее время. Остальное в спектре ввезено.`
* стало: `В файле N42 не прочитано время начала набора (таких измерений: {0}, первое: {1}); вместо него подставлено текущее время. Остальное в спектре ввезено.`

Английская строка (`in {0} measurement(s)`) согласована, не тронута.

Сверки на `BecquerelMonitor/Properties` до и после: `check_resx.py` код 0 → 0,
`check_resx_letters.py` код 0 → 0. По всему дереву обе дают 1 до и после — ровно
из-за трёх чужих файлов чужого формата (`DCPeakDetectionView.ru.resx` смешанные
переводы, `EfficiencyMakerForm.resx`/`.ru.resx` LF); строк про `Resources.*`
в выводе нет.

**Положительный контроль**: в scratchpad подброшена пара `Resources.resx` +
`Resources.ru.resx`, где русский файл без BOM и с LF; `check_resx.py <каталог>`
— код **1**, строка `ФОРМАТ  …/Resources.ru.resx: без BOM; переводы строк LF, а
не CRLF`, итог `РАЗОШЛОСЬ`.

## `A178` — шапка `tools/check_resx_designer.py`

Каждое утверждение шапки сверено с деревом grep-ом:

| утверждение | проверка | итог |
|---|---|---|
| три свойства `ResponseMatrix*` лежали в `Resources.Designer.cs` без ключей (`A91`) | в Designer и resx их 0 — «лежали», прошедшее | верно |
| строки снял коммит `bbbb98ab` (`A46`) | `git log -1 bbbb98ab` — «A46: времени в окне матрицы больше нет…» | верно |
| третье плечо: `GetResourceText("PeakFitChiTableTitle", …)` и `GetString(issue.Resource)` | `DCFwhmCalibrationView.cs`, `GeometryEditorPanel.cs:1922` | верно; дописано: 05.09.2026 появилась вторая обёртка `Text()` в `DoseRate.cs`, плечо разобрало её само |
| **`DCFwhmCalibrationView.cs:704` просит `PeakFitChiTableScoreColumn`, которого нет ни в одном resx** | ключ есть: `Resources.resx:42289`, `Resources.ru.resx:916` (`A154`) | **устарело — переписано** в прошедшее время с указанием `A154` |
| **естественная мера: «ОДНА находка … нет ни в одном из двух resx»** | та же | **устарело — датировано**, добавлен замер 05.09.2026: 732 литерала, 3 через один шаг, недоступных 0, по этому ключу находок нет |
| «На 04.09.2026 недоступных НОЛЬ» | сегодня 0 | верно |
| пустой ключ — забота `check_resx.py` (`ResXNullRef`) | `check_resx.py:88` | верно |
| плечо формата `T156`, ключ `--no-format` | в коде `main` | верно |

Скрипт на дереве: `python -m py_compile` ок. Коды:

* без ключей — **1**: три чужих файла чужого формата (те же три, что выше),
  по предмету — см. ниже;
* `--no-format` — **1**, но НЕ из-за формата: две настоящие находки
  `НЕТ КЛЮЧА BecquerelMonitor/DoseRate.cs:292 … "DoseRateEnergyNotPositive"` и
  `…:309 … "DoseRateNoElement"` — ключей нет ни в `Resources.resx`, ни в
  `Resources.ru.resx`. `DoseRate.cs` и `DoseRateManager.cs` сейчас правятся
  другой полосой (в `git status` — M, не закоммичено), так что это либо её
  незаконченный шаг, либо новая строка; заведена строкой без номера в отчёте.

Шапка правилась байтами (LF без BOM сохранены); чужая незакоммиченная правка
того же файла (плечо формата `T156`) цела.

## Тронутые файлы

* `BecquerelMonitor/GlobalConfigManager.cs` — только `AppUi.Report` (+ комментарий).
* `BecquerelMonitor/Properties/Resources.ru.resx` — одна строка.
* `tools/check_resx_designer.py` — три блока шапки.
* `tools/effmaker/probes/ReasonProbe.cs` — `using System.Windows.Forms`, плечо
  `Reported()` + `CaptureError()`, две вставки в шапку, вызов в `Main`.
* этот журнал.

Каталоги: `tools\effmaker\probes\build_b7`, `build_b7_old` (gitignored, как
`build*`); сборки приложения — в копии дерева в scratchpad, не в дереве.
