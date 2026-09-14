# Заход 06.09.2026, полоса G4: целевая платформа у каждой пробы (`T237`)

Дерево `pie`, вершина `7a7bcba3`. Реестры не правились. Файлы полосы:
новый довесок `tools/effmaker/probes/_TargetFramework.cs`, сторож в
`tools/effmaker/probes/build_all.ps1`, раздел в `tools/effmaker/probes/README.md`,
снятие частного атрибута у девяти проб. Каталоги: `BecquerelMonitor\bin\Debug_G4`,
`obj\G4`, `tools\effmaker\probes\build_g4` (штатный), `build_g4_arms` (плечи
замера, вынесены из каталога проб — сторож плана считает их посторонними, и
правильно). Артефакты — `handover/g4-target-framework/` (номера ниже — префиксы
файлов там).

## 0. Что было в дереве до полосы

* `build_all.ps1` выводит довески правилом «файл без `Main`» (`T57`) и кладёт
  каждый КАЖДОЙ пробе — значит общий довесок с атрибутом не требует правки
  структуры скрипта: достаточно файла. Довесков было три: `GadrasDetector.cs`,
  `ProbeDeviceConfig.cs`, `ResidualScan.cs`.
* Свой `[assembly: TargetFramework(".NETFramework,Version=v4.8")]` несли
  **девять** проб (заведён порознь 05–06.09.2026): `CalibGraphProbeF35`,
  `CultureProbeO14`, `GraphCultureProbeF27`, `LabelPathProbeF49`,
  `MatrixRefusalProbeP8`, `ModalThreadProbeO25`, `PeakFwhmUnitsProbeF41`,
  `RestCultureProbeF28`, `RestCultureProbeF47`. Восемь из них печатают платформу
  в шапке (`AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName`),
  `LabelPathProbeF49` и `PeakFwhmUnitsProbeF41` — нет.
* Приложение: `Program.ApplyLanguage` ставит `CurrentUICulture` потоку и
  `DefaultThreadCurrentUICulture`; `DefaultThreadCurrentCulture` не ставится
  нарочно (`A238`, `A244`).

## 1. Механизм — черновая проба, два плеча (`01-scratch-*.txt`)

`TfScratchProbe.cs` (в scratch) собрана голым `csc` дважды: без довеска и с
`_TargetFramework.cs`. Печатает платформу процесса, два переключателя и
культуру, которую видят `Task.Run` / `new Thread` / `ThreadPool` / `Parallel.For`
после присваивания инварианта на главном потоке (ОС ru-RU).

| | без довеска | с довеском |
|---|---|---|
| `TargetFrameworkName` | **НЕ ОБЪЯВЛЕНА** | `.NETFramework,Version=v4.8` |
| `Switch.System.Globalization.NoAsyncCurrentCulture` | **True** | по умолчанию платформы |
| `Switch.System.IO.UseLegacyPathHandling` | **True** | по умолчанию платформы |
| `Task.Run` / `new Thread` / `ThreadPool` после `CurrentCulture = Invariant` | **ru-RU, «1,5»** | инвариант, «1.5» |
| то же при `DefaultThreadCurrentCulture = de-DE` | de-DE, «1,5» | инвариант (течёт контекст) |
| `Parallel.For(0, 1)` | инвариант — итерация исполнилась на главном потоке | инвариант |

Обе двери (`Thread.CurrentThread.CurrentCulture` и `CultureInfo.CurrentCulture`)
ведут себя одинаково — различия между ними нет ни в одном плече. Что читает
платформа — атрибут ВХОДНОЙ сборки; `BecquerelMonitor.exe`, которое проба лишь
загружает, на процесс пробы не действует (в плече «без довеска» приложение не
загружалось вовсе, но и у `CultureProbeO14` рядом с приложением итог тот же — §3).

## 2. Что сделано

1. **`tools/effmaker/probes/_TargetFramework.cs`** — довесок без `Main`:
   атрибут `TargetFramework(".NETFramework,Version=v4.8")` + помощник
   `ProbeTargetFramework` (`Expected`, `Declared`, `Matches`, `Describe()` —
   строка для шапки с платформой и `NoAsyncCurrentCulture`, `Assert()` — то же с
   отказом при чужой платформе).
2. **`build_all.ps1`** — после вывода довесков читатель признака (`T237`):
   довесков с `[assembly: TargetFramework` обязан быть РОВНО ОДИН, иначе
   `Deny-Guard`; пробы со своим атрибутом сверх довеска называются поимённо с
   предупреждением «ждите CS0579» (сам отказ — законный, компилятора); строка
   «целевая платформа проб: довесок _TargetFramework.cs» в выводе. Структура
   скрипта (коды 0/1/2/3/4/6, отпечаток набора `T226`, порядок раскладки) не
   тронута; новый довесок входит в отпечаток набора как любой другой.
3. **Девять проб** — частный атрибут снят (`strip_attr.py` в scratch, по одному
   образцу, комментарий над атрибутом заменён тремя строками со ссылкой на
   довесок; печать платформы в шапках оставлена как была). `git diff --stat`:
   9 файлов, +27/−54, только эти строки.
4. **`README.md`** проб — абзац о довеске, ручной сборке (`csc … _TargetFramework.cs`)
   и CS0579.

## 3. Замер: 15 проб + копия, два плеча (`03-`…`07-`, `before/`, `after/`)

Плечо «до» (`-SkipTf -Suffix _before`): девять проб со СВОИМ атрибутом (правила
4.8), шесть без всякого (правила до 4.6), плюс копия `CultureProbeO14_noattr`
(атрибут снят, довеска нет — положительный контроль «по-старому»). Плечо «после»
(`-Suffix _after`): все шестнадцать с довеском, у девяти свой атрибут снят.
Каждый exe свежее исходника (сверено скриптом). Прогон из `build_g4`
(`config\` у приложения относительный), корпус — `tools\CORPUS\corpus\spectra`.
Сравнение — `diff` вывода с вычищенными датами и секундами.

| проба | «до» | код до→после | различий | что различается |
|---|---|---|---|---|
| `CultureProbeO14` | свой атрибут | 0→0 | **0** | — |
| `CultureProbeO14_noattr` (контроль) | без атрибута, без довеска | 0→0 | **90** | платформа «НЕ ОБЪЯВЛЕНА, NoAsyncCurrentCulture=True»; дефект у **5** стартеров в плечах 1 и 2 и у **1** на «OS» → с довеском **1 / 1 / 5** — ровно как у пробы со своим атрибутом |
| `CalibGraphProbeF35` | свой атрибут | 0→0 | 0 | — |
| `GraphCultureProbeF27` | свой атрибут | 0→0 | 0 | — |
| `RestCultureProbeF28` | свой атрибут | 0→0 | 0 | — |
| `RestCultureProbeF47` | свой атрибут | 0→0 | 0 | — |
| `MatrixRefusalProbeP8` | свой атрибут | 0→0 | 2 | GUID файла сцены `p8a-….rmx` (заводится на прогон) |
| `ModalThreadProbeO25` | свой атрибут | 0→0 | 4 | native id потока, 322→320 мс |
| `LabelPathProbeF49` | свой атрибут | 0→0 | 2 | путь к csv плеча; csv **одинаковы** (384 строки) |
| `PeakFwhmUnitsProbeF41` | свой атрибут | 0→0 | 4 | путь к csv; «длина XML 1246→1245» — `ResultData.StartTime/EndTime = DateTime.Now` внутри сериализуемого объекта, доли секунды; csv **одинаковы** (41 803 строки) |
| `FsaBackgroundMarkProbeF48` | без атрибута | 0→0 | **0** | — |
| `LabelTruthProbe` | без атрибута | 0→0 | 2 | путь к csv; csv **одинаковы** (981 строка) |
| `ModalReachProbeF22` | без атрибута | 0→0 | **0** | — |
| `ReasonProbe` | без атрибута | 0→0 | **0** | — |
| `ResponseInterpProbe`, `ResponseShapeProbe` | без атрибута | собрались | не гонялись | Монте-Карло по геометрии; тело `Parallel.For` (`Direct()`) — только числа, без `ToString`/`Parse`/`Format` (grep), правила культуры до него не дотягиваются |

Итог: там, где правила 4.8 уже были (свой атрибут), числа НЕ изменились —
девять проб, ноль содержательных различий; там, где правил не было, единственная
проба, чей замер их мерит (`CultureProbeO14` без атрибута), изменилась ровно
на разницу правил (5 ↔ 1), а четыре пробы с потоками и культурой без атрибута
не изменились — их числа от правила не зависят, но живут они теперь по
правилам приложения.

⚠ Строка `T237` говорит «6 стартеров из 6»; сегодня та же проба без атрибута
даёт **5 из 6** — стартеров у неё шесть, но два «заведённых ДО настройки» не
судятся (`НЕ СУДИТСЯ`), и по-старому не по настройке выходят пять. Число в
строке — от прежней редакции пробы (05.09.2026); в тексте закрытия пишу
измеренное.

Две грабли моего бегунка, не проб (`run_g4.ps1`): `Start-Process -ArgumentList`
разрезал путь с пробелами — «неизвестный ключ: Eng» у трёх проб (грабля из
CLAUDE.md, поймана второй раз); относительный `-OutDir` уехал в рабочий каталог
пробы — `DirectoryNotFoundException` у тех же трёх. Обе починены, три пробы
догнаны в обоих плечах (`07-`).

## 4. Кого это касается по существу (перепись 128 проб + 4 файла харнесса)

Перепись — grep по `Task.Run|Task.Factory|Parallel.|new Thread(|ThreadPool|await|BackgroundWorker`
и по присваиванию `CurrentCulture=|DefaultThreadCurrentCulture|CurrentUICulture=`.
Сетевых (`HttpClient`, `ServicePointManager`, `SecurityProtocol`) в пробах нет ни одной.

* **Мерят сами правила** (культура через задачи/потоки; свой атрибут был, снят,
  теперь довесок) — 9: `CalibGraphProbeF35`, `CultureProbeO14`,
  `GraphCultureProbeF27`, `LabelPathProbeF49`, `MatrixRefusalProbeP8`,
  `ModalThreadProbeO25`, `PeakFwhmUnitsProbeF41`, `RestCultureProbeF28`,
  `RestCultureProbeF47`. Числа не изменились (§3).
* **Ставят культуру на главном потоке и работают на других** (атрибута не
  было — жили по старым правилам) — 7: `FsaBackgroundMarkProbeF48`,
  `LabelTruthProbe`, `ModalReachProbeF22`, `ReasonProbe`, `ResponseInterpProbe`,
  `ResponseShapeProbe`, `FsaInferProbeF51` (чужая, незакоммиченная — только
  собрана). Четыре прогнаны: числа не изменились.
* **Потоки без выставления культуры** — 4: `CalibrationNanProbeF48`,
  `CrashLogProbe`, `GridStampProbeF45`, `ImportEmptyConfigProbeF23` — течь
  нечему, не гонялись.
* Остальные ~108 и харнесс — однопоточные; довесок им ничего не меняет, кроме
  того, что платформа у процесса теперь та же, что у приложения.

## 5. Штатная сборка и контроли (`08-`, `09-`, `10-`)

* `build_all.ps1 -Bin bin\Debug_G4 -Out build_g4` с довеском: «довески без
  Main: _TargetFramework.cs, GadrasDetector.cs, ProbeDeviceConfig.cs,
  ResidualScan.cs», «целевая платформа проб: довесок _TargetFramework.cs»,
  **ok у 129 файлов, FAIL 0** (в том числе чужие незакоммиченные
  `FsaInferProbeF51`, `StartTimeProbeF52`). Первый прогон (`08-`) кончился кодом
  3 — сторож плана назвал 32 моих `_before`/`_after` exe посторонними; вынесены в
  `build_g4_arms`.
* Контроль 1 — дубль атрибута: черновая проба + свой `[assembly: TargetFramework]`
  + довесок → `CS0579` (см. `11-controls.txt`).
* Контроль 2 — довесок переименован на время прогона: `build_all.ps1`
  отказывает `Deny-Guard` «довесков с [assembly: TargetFramework] должно быть
  РОВНО ОДИН … найдено 0 (T237)» (`09-`); довесок возвращён, `Test-Path` — True.
* Заключительный штатный прогон — `10-build_all-g4-final.txt` (итог дописан ниже).

## 6. Итог заключительного прогона и `check_all.py`

* `10-build_all-g4-final.txt`: **EXIT=0**; «целевая платформа проб: довесок
  _TargetFramework.cs»; **ok 129, FAIL 0**; «все собрались: 129 файлов (плюс 4
  без Main, идут довеском)»; каталог заверен (`T226`): приложение
  `818a00553d3d` (548 файлов), пробы `e4b140b36e43` (133), довесков **4**.
  Единственное предупреждение — `config\layout\ExpertMode.xml` посторонний:
  его положил мой `build_g4.ps1`, копируя `config\` целиком из `Debug_G4`
  (образец G1), не приложение и не довесок.
* `13-O14-from-build_all.txt` — `CultureProbeO14.exe` из ШТАТНОЙ сборки
  (02:19:03, исходник 02:14:25): «целевая платформа входной
  сборки=.NETFramework,Version=v4.8», стартеров с дефектом **1 / 1 / 5** — как
  прежде со своим атрибутом. То есть то, чем будут пользоваться, проверено, а
  не только плечи.
* `12-check_all.txt`: `python tools/check_all.py` — **11 из 11 сторожей код 0**.

## 7. Находки по §9

* Строка `T237` называет «6 из 6», измерено сегодня **5 из 6** (два стартера
  «ДО настройки» не судятся) — факт для журнала и текста закрытия, строкой не
  становится.
* Шесть проб с потоками и культурой без своего атрибута (§4, вторая группа)
  платформу в шапке НЕ печатают; помощник `ProbeTargetFramework.Describe()`
  есть, вызов в них не вставлял — чужие пробы сверх снятия дубля вне полосы.
  Читатель у признака при этом есть: `build_all.ps1` отказывает без довеска, и
  собрать штатно пробу без платформы больше нельзя; риск остаётся только у
  ручной `csc`-сборки, о чём написано в README. Строка — на усмотрение
  распорядителя (кандидат: «вставить печать платформы в шапки FsaBackgroundMarkProbeF48,
  LabelTruthProbe, ModalReachProbeF22, ReasonProbe, ResponseInterpProbe,
  ResponseShapeProbe»), причина, почему не на месте, — файлы чужих полос.
* Два дефекта моего бегунка (§3) починены на месте, строк нет.

## 8. Файлы полосы

Изменены: `tools/effmaker/probes/build_all.ps1` (сторож довеска после вывода
довесков), `tools/effmaker/probes/README.md` (абзац о довеске), девять проб
(`CalibGraphProbeF35`, `CultureProbeO14`, `GraphCultureProbeF27`,
`LabelPathProbeF49`, `MatrixRefusalProbeP8`, `ModalThreadProbeO25`,
`PeakFwhmUnitsProbeF41`, `RestCultureProbeF28`, `RestCultureProbeF47` — снят
частный атрибут, комментарий заменён ссылкой на довесок).
Созданы: `tools/effmaker/probes/_TargetFramework.cs`, этот журнал,
`handover/g4-target-framework/` (`build_g4.ps1`, `run_g4.ps1`, `00-`…`13-`,
`before/`, `after/`). Ничего не коммичено.
