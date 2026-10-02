# П210 (02.10.2026): `T266` — чтение конфигов и спектров на ЗАПИСЬ; умолчания поверх конфига при выходе

Полоса П210, worktree `D:\BqMoni_Claude\p210\wt` от `master` `a3bb42ab`. Дефект ПРИЛОЖЕНИЯ, найден
полосой П209; строку `T266` заводит распорядитель. Корпус правка не трогает — прогон не нужен.

## 1. Посылка и перепроверка

Посылка: `GlobalConfigManager.LoadConfigFile` открывал `config\BecquerelMonitor.xml` голым
`new FileStream(путь, FileMode.Open)` — это `FileAccess.ReadWrite` (умолчание) при `FileShare.Read`;
сбой открытия даёт окно и работу на встроенных умолчаниях, а выход (`MainForm.cs:402`,
`SaveConfigFile`) пишет умолчания ПОВЕРХ конфига человека.

**Посылка подтвердилась целиком и оказалась УЖЕ реальности в двух местах:**

* отказывает не только «второй держатель с записью», а ЛЮБОЙ держатель, даже простой читатель
  `File.OpenRead` (`FileAccess.Read` + `FileShare.Read`): его разделение не допускает запись, а
  голый `FileMode.Open` просит запись. То есть антивирус или индексатор, держащий файл на
  чтение, тоже ломал загрузку;
* «умолчания поверх файла» были не только у главного конфига: библиотека нуклидов писала
  заготовку из четырёх записей поверх непрочитанного файла при ЛЮБОМ сохранении из редактора
  (на запуске запись была закрыта `File.Exists`, а в `SaveDefinitionFile` — нет); новые
  конфигурации прибора/ROI выбирали имя только по СПИСКУ, и незагрузившийся файл того же имени
  (дубль GUID, битый, занятый) затирался молча. В копии конфига Amber такие файлы ЕСТЬ:
  `device\AtomSpectraVCP.xml`, `ROI\Ra-226 Intensities.xml`, `ROI\Th-232 Intensities.xml`
  (дубли GUID — данные Amber, их не трогали).

Места голого `FileMode.Open` — поиск по всему `BecquerelMonitor\*.cs` (`new FileStream(`,
`File.Open(`, `FileMode.`, `OpenRead`, `StreamReader(`, `XmlReader.Create(`, `ReadAll*`): ровно
десять, все в посылке — `GlobalConfigManager.cs:726`, `DeviceConfigManager.cs:105,111`,
`DocumentManager.cs:373,498,610,2572`, `NuclideDefinitionManager.cs:209`,
`ROIConfigManager.cs:114,263`. Прочие читатели (`File.OpenRead`, `StreamReader(путь)`,
`XmlReader.Create(путь)`, `File.ReadAll*` в `DocumentManager` — ввоз CSV/N42/Atom Spectra,
`DeviceConfigForm.cs:2633`, `MainForm.cs:3762`, `N42\Util.cs:1657`) уже просят только чтение:
файл «только чтение» и параллельные читатели им не мешают. Их `FileShare.Read` оставлен
НАРОЧНО: это отказ открывать файл, который в этот миг ПИШЕТ другая программа, а ввоз CSV/текста
по полузаписанному файлу дал бы тихо обрезанный спектр, а не отказ. `EfficiencyMaker\*` уже с
`FileAccess.Read` (и вне полосы).

Проба `tools/effmaker/probes/FileShareProbeP210.cs`, три режима:

* `--mode=matrix --spec=<спектр>` — цели × сцены. Цели: `global` (`GlobalConfigManager`),
  `nuclide`, `device`, `roi`, `open` (`OpenDocument`), `load` (`LoadDocument`), `bg`
  (`LoadBackgroundSpectrum`). Сцены: `plain`, `readonly` (атрибут), `holdRW_shRW` (держатель
  запись + разделение записи — облако, редактор), `holdRW_shR` (прежний приём самого
  приложения — второй экземпляр), `holdR_shR` (простой читатель), `holdRW_shNone` —
  ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: держатель без разделения обязан ломать чтение при любом коде;
* `--mode=loop --seconds=N` — сбросить и перечитать главный конфиг подряд N секунд; пускается
  тремя процессами сразу (`D:\BqMoni_Claude\p210\parallel.ps1`);
* `--mode=save` — запись поверх непрочитанного (см. §3).

Каталоги опыта — `D:\BqMoni_Claude\p210\probe_old` / `probe_new`: сборка Release +
копия конфига Amber (`OneDrive\Desktop\Debug\config` → `D:\BqMoni_Claude\p210\cfg`), спектр —
копия `YandexDisk\Спектры\!ASN16\Cs 137 в домике 24.11.2022.xml`.

| цель | plain | readonly | holdRW_shRW | holdRW_shR | holdR_shR | holdRW_shNone (контроль) |
|---|---|---|---|---|---|---|
| старый код, все 7 целей | ok | ОТКАЗ | ОТКАЗ | ОТКАЗ | ОТКАЗ | ОТКАЗ |
| новый код, все 7 целей | ok | ok | ok | ok | ok | ОТКАЗ |

Старый: рабочих отказов **28 из 28**, код 1 (причины: `UnauthorizedAccessException` на атрибуте,
`IOException` «файл используется другим процессом» под держателями); новый: **0 из 28**, код 0;
контроль отказал у 7 из 7 целей в обеих сборках (держатель держит — замер не пуст).

Параллельное чтение главного конфига, три процесса по 10 с:

| сборка | загрузок | отказов |
|---|---|---|
| старая | 39219 (12098 + 13524 + 13597) | **13734** (3652 + 5020 + 5062), код 1 у всех трёх |
| новая | 27178 (9336 + 8896 + 8946) | **0**, код 0 у всех трёх |

## 2. Правка

* Десять мест чтения → `new FileStream(путь, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)`.
  Почему `ReadWrite`, а не `Read`: свои файлы приложение пишет атомарно (`AtomicFileWriter`:
  `.tmp` и `File.Replace`), целевой файл полузаписанным от нас не бывает; держатель с записью
  на деле — синхронизатор облака или редактор; а полузаписанный чужой XML не разберётся
  `XmlSerializer` и даст ОТКАЗ, не тихо другие числа.
* `GlobalConfigManager`: признак `loadFailedOverExistingFile` — файл ЕСТЬ (не
  `FileNotFound`/`DirectoryNotFound` и `File.Exists`), а прочитать не вышло (занят, нет прав,
  битый XML). Тогда `SaveConfigFile` НЕ пишет (окна на выходе нет — довод `MainForm.SaveLayoutXml`:
  выход бывает и при завершении сеанса Windows). Файла нет вовсе (первый запуск) — признак ложь,
  умолчания пишутся как прежде. Окно отказа загрузки теперь называет путь и причину
  (`ERRFailureReason`, `AppUi.Reason`) и говорит, что файл НЕ будет переписан — новая строка
  ресурса `MSGUnreadableConfigNotOverwritten` в `Resources.resx` и `Resources.ru.resx`
  (+ `Resources.Designer.cs`).
* `NuclideDefinitionManager`: тот же признак в `LoadDefinitionFile` (удачное чтение снимает);
  `SaveDefinitionFile` при нём отказывает окном (`ERRSavingNuclideDefinitionFile` + путь +
  `MSGUnreadableConfigNotOverwritten`) и возвращает `false` — вызывающие (`NuclideDefinitionForm`,
  `NuclideSetForm`, `NucBase`) уже проверяют возврат.
* `DeviceConfigManager`/`ROIConfigManager`: `IsFilenameTaken(имя)` — занято списком ИЛИ файлом на
  диске; формы (`DeviceConfigForm`/`ROIConfigForm.AssignNewFilename`) берут его вместо обхода
  списка. `SaveConfig` при ПЕРЕИМЕНОВАНИИ отказывает, если файл с новым именем уже лежит на диске
  (смена одного регистра — тот же файл, не отказ); вызывающий показывает `ERRDuplicateConfigName`,
  что верно по смыслу.
* Прочие пути записи при выходе проверены: раскладка (`SaveLayoutXml`) уже защищена `AMBER43`
  (сломанный файл откладывается, запись атомарная); `DeviceConfigManager`/`ROIConfigManager` при
  выходе не пишут вовсе; документы — только по слову человека.

## 3. Приёмка

**Проба `--mode=save`** (старый / новый код, копия конфига):

| сцена | старый | новый |
|---|---|---|
| (а) библиотека: чтение под держателем без разделения → отпущен → `SaveDefinitionFile` | `true`, файл ПЕРЕПИСАН (sha 5AE488901F2D → E31009130958, 14241 → 216 байт) | `false`, файл ЦЕЛ (sha 5AE488901F2D) |
| (б) `device\AtomSpectraVCP.xml` (дубль GUID, на диске, в списке нет) | форма сочла бы имя свободным | `IsFilenameTaken = true` |
| (б) `ROI\Ra-226 Intensities.xml`, `ROI\Th-232 Intensities.xml` | так же | `true`, `true` |
| итог | код 1, мест 4 | код 0 |

**Экраном — собранное приложение на КОПИИ конфига Amber** (`D:\BqMoni_Claude\p210\app_old` /
`app_new`, сборки `bin\Release_p210_old` (master) / `bin\Release_p210`). Окнами правил не мышью
человека (во время проверки Amber работала за машиной, захват экрана прерван), а сообщениями:
UI Automation / `EnumWindows` + `WM_CLOSE` и `PrintWindow` в файл — сценарии
`D:\BqMoni_Claude\p210\exitscene.ps1`, `roscene.ps1`, `enumwin.ps1`, `uia.ps1`.

Сцена «выход после неудачной загрузки» (`exitscene.ps1`): держатель без разделения на
`config\BecquerelMonitor.xml` 20 с → запуск → окно отказа → держатель отпущен → окно закрыто →
главное окно → выход. Мера — sha256 и mtime конфига:

| сборка | до запуска | после выхода |
|---|---|---|
| старая (положительный контроль, два прогона) | 0027948671161C29… 2026-09-29 22:17:16.051, 5869 б | **3396D8BAC88A18BC… 2026-10-02 09:04:23 / 09:06:41, 5893 б — ПЕРЕПИСАН** |
| новая | 0027948671161C29… 2026-09-29 22:17:16.051, 5869 б | **0027948671161C29… 2026-09-29 22:17:16.051, 5869 б — цел** |

Чем старый код переписал конфиг (diff копии Amber против файла после выхода): 51 строка —
`ResultTranslation` BecquerelsPerKilogram → Becquerels, `DefaultVerticalScaleType` PowerScale →
LogarithmicScale, `NumberOfSMADataPoints`/`WMA` 13 → 11, `EnergyPitch` 6 → 5, размеры и
положение окон, `Language` пусто → OS, `DoSaveRawPulseData` false → true и др.

Окно отказа: старое — «Не удалось загрузить конфигурационный файл приложения.» без пути и причины;
новое — путь, «Причина: IOException: … используется другим процессом» и абзац «Файл есть, но
прочитать его не удалось … НЕ перезапишет его …».

Сцена «спектр только для чтения» (`roscene.ps1`): копия спектра с атрибутом ReadOnly открывается
аргументом командной строки, затем выход:

* старая: MessageBox «File "…\Cs137_house_readonly.xml" couldn't be opened. (Access to the path
  … is denied.)», активный документ — прежний (`Lu-176.xml`);
* новая: окна отказа нет, заголовок «BecqMoni … - Cs137_house_readonly.xml», FSA посчитан
  (Cs-137 89.19 %); выход — код 0, конфиг сохранён штатно (mtime новый, sha тот же —
  содержимое не изменилось), спектр не тронут (sha B4475DCF…, атрибут ReadOnly на месте).

Снимки (на диске, не в git), `D:\BqMoni_Claude\p210\shots\`: `old_1_load_error.png`,
`new_1_load_error.png`, `old_2_running.png`, `new_2_running.png`, `old_3_round2_1.png` (отказ
открытия «только чтение»), `old_3_main_1.png`, `new_3_main_1.png`.

Папки Amber не тронуты: в `OneDrive\Desktop\Debug\config` новейший файл — по-прежнему
`BecquerelMonitor.xml` 2026-09-29 22:17:16 (sha 0027948671161C29…); в `YandexDisk\Спектры`
файлов, изменённых за последние 3 ч, — 0 (раскладка копии открывает её спектры на чтение).

Прочее: `check_resx.py` и три `check_resx_*.py` — код 0; `build_all.ps1 -Bin
BecquerelMonitor\bin\Release_p210 -Out tools\effmaker\probes\build_p210` — код 0 (283 пробы);
`check_fsa_report_view.py --probes=tools\effmaker\probes\build_p210` — код 0 («ВСЕ СОШЛИСЬ»);
`check_corpus_scenes.py --probes=…build_p210` — код 0. `check_all.py` — код 1, отказали 7 из 44,
все — среда worktree или ожидаемое: `check_registry_refs` (ссылок `T266` в никуда 22 — строки ещё
нет в реестре, заводит распорядитель), `check_corpus_coverage` (нет `_corpus_raw`),
`check_declared_base` (нет `tools\pie\out_rev38_full`), `check_corpus_generator` (склад матриц
gitignored — «нет матрицы» в пересборке сводки), `check_fsa_report_view` и `check_corpus_scenes`
(по умолчанию ищут `probes\build`; с каталогом полосы — код 0), `check_fsa_showcase` (склад витрины
gitignored; отображение FSA правка не трогает).

## 4. Попутно (не правилось, не моё)

* Окно «Response matrix … computed with file format 9, while this build reads format 10» на копии
  конфига Amber — её матрицы посчитаны другой сборкой; ожидаемо, не дефект.
* Журнал копии: `ROI config …: элемент <ROIEfficiency>/<Note> программе неизвестен … будет потерян
  при первом сохранении` у 15 ROI Amber — поставочные/её конфиги, по приказу 05.09 не принимается.
* Старый `NuclideDefinitionManager.GetInstance` при неудаче чтения в окнах повторяет чтение и
  окно отказа на КАЖДОМ обращении к менеджеру — с правкой разделения доступа держатель-читатель
  этого больше не вызывает; остаток (держатель без разделения) — редкость, строкой не заводится.
