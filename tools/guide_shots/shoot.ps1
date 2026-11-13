# Конвейер снимков окон программы для пользовательского гида `docs/` (`AMBER319`, 08.11.2026).
#
# ЧТО СНИМАЕТ. Снимки гида RU и EN; сцена — один запуск программы, из неё берутся снимки:
#
#   сцена              снимки                                что на них
#   main               main, panel-control, panel-sample,    ASN16_Cs137 корпуса, сет «Cs-137 + K-40»,
#                      spectrum-bar, view-modes              «Обнаружение пиков», курсор у 3172 кэВ;
#                                                            полоса под графиком и раскрытый список
#                                                            режимов фона (`AMBER328`)
#   main-fsa           main-fsa, panel-result, panel-fsa,    ториевый диск AS80, сет «Th-232 + K-40»,
#                      fsa-button                            FSA включён кнопкой FSA; отчёт прокручен
#                                                            к «Качеству»; раскрытый список кнопки FSA
#   main-fsa-nomatrix  main-fsa-nomatrix                     то же без матрицы на складе копии
#   devcfg             devcfg-general, -device-rc, -analysis, окно конфигурации прибора, набор
#                      -refs, -device-obs, -audio,           приборов гида (`make_copy.py`, GUIDE_SET);
#                      -ausb, -aser, -device-as              ⛔ Н1: порты COM99, серийные закрыты
#   devcfg-ecal        devcfg-ecal                           то же, копия из четырёх приборов
#   devcfg-eff         devcfg-eff, respmatrix                кривая «AS80_th_disk», окно матрицы
#   devtcp             devcfg-device-astcp, -asrest          AtomSpectra TCP/REST, TCP и REST
#   maker              effmaker-detector, -source, -shield,  конструктор кривой «Цилиндр 40×15»,
#                      effmaker-calc                         расчёт — «AS80_th_disk»
#   gdml               effmaker-gdml, effmaker-gdml-import   импорт GDML (ключ -Gdml): подвкладка «Файл GDML» с
#                                                            блоком «Сопоставление» и окно импорта «что
#                                                            содержится в файле» (`AMBER322`)
#   nucdef             nucdef, nucsets, nucbase              библиотека нуклидов, наборы, «База изотопов»
#   dialogs            about, prefs-chart, -color, -sound,   «О приложении», настройки программы
#                      prefs-system
#
# НЕ снимаются (вырезки другой раскладки окна, меню, всплывающие окна): panel-ecal, panel-fwhm,
# panel-peaks, panel-speclist, select-panel, set-hint, graph-peak-shape,
# menu-file, menu-file-import, menu-view, menu-spectrum, menu-tool, menu-help — таблица
# «снимок → сцена» и причины — в журнале `handover/handover-2026-11-08-guideshot.md`. Новая сцена
# добавляется в `GuideShot.cs` (действия человека) и в `make_copy.py` (`SCENES`: спектр, набор
# приборов, склад копии); снимок — в таблицу `$sceneOf` ниже.
#
# КАК ЗАПУСТИТЬ — одной командой, из любого каталога, оператором вызова:
#
#   & 'tools\guide_shots\shoot.ps1' -Shots main-fsa,main-fsa-nomatrix -Build -Install
#
#   -Shots    имена снимков из таблицы выше или `all` (умолчание); сцена снимается один раз,
#             даже если из неё берутся два снимка (`main` и `panel-control`).
#   -Lang     ru,en (умолчание — оба).
#   -Build    пересобрать приложение (bin\guideshot, obj\guideshot) и пробы (`mk.ps1`);
#             без ключа берётся сборка, уже лежащая в bin\guideshot (её нет — собирается).
#   -Install  положить снятое в docs\img\<язык>\ (`install_shots.ps1`): попиксельная сверка с
#             прежним снимком печатается до замены. Без ключа снимки только в <Work>\shots.
#             Снимок другого размера ставится, только если назван в -Resized.
#   -Resized  снимки, которым разрешено сменить размер при установке (их точки `data-hot`
#             пересчитаны по <Work>\shots\<язык>\<снимок>.hot.txt): так 13.11.2026 впервые
#             сняты конвейером `spectrum-bar` и `view-modes` (`AMBER328`).
#   -Work     временный каталог (умолчание D:\BqMoni_Claude\guideshot); в git он не идёт.
#   -StaleControl  положительный контроль метки «!»: сцена `main` после кадра выбирает в
#             списке «Геометрия» первую помеченную кривую и снимает панель ещё раз
#             (<Work>\shots\<язык>\panel-control-stale.png; в гид не ставится).
#   -DiskCurve  кривая диска сцен FSA: corpus (умолчание, решение Amber 08.11.2026 «Свежая кривая,
#             числа обновить») или file — своя кривая файла витрины phys=18, как 07.11.2026.
#   -Gdml     файл GDML сцены `gdml` (снимки effmaker-gdml и effmaker-gdml-import); умолчание —
#             учебная сцена дерева tools\effmaker\probes\data\gdml_a322\a322_shielded.gdml: в ней
#             узнаются все три роли — детектор, образец, защита (`AMBER322`). Снимок 07.11.2026 снят с
#             публичного RC110_detector.gdml (geant4-detector-models), которого в дереве нет: в нём
#             только детектор, в окне импорта списки образца и защиты пусты.
#   -Store    склад матриц корпуса (умолчание tools\CORPUS\corpus\geometries\response этого
#             дерева). Склад в git не входит: в отдельном worktree указать склад основного
#             дерева, иначе отказ — снимок FSA без матрицы молча не снимается.
#
# Пробы (`mk.ps1`) пересобираются при каждом запуске; приложение — только с -Build.
#
# НА КАКОЙ КОПИИ. Программа запускается из <Work>\app — копии сборки с конфигом рядом
# (портативная раскладка), а не из %AppData%. Конфиг копии — КОНФИГ ГИДА из дерева, нейтральный
# и закреплённый (`make_copy.py`): главный конфиг `config\BecquerelMonitor.xml` рядом, поставочная
# библиотека нуклидов, приборы — заглушки корпуса под именами моделей с придуманными датами,
# портом COM99, серийными, закрытыми точками; кривая конструктора придумана. Конфиг человека
# (%AppData%\BecqMoni) конвейер не читает — снимок от его правок не меняется; читает его только
# `check.py` (список того, чего на снимке быть не должно). Спектры сцен — из дерева (корпус и
# витрина FSA), имя файла фона — нейтральное, на языке снимка. ⛔ К приборам проба не
# обращается: порты копии несуществующие (Н1, журнал 07.11.2026).
#
# КАК СНИМАЕТ. `GuideShot.exe` лежит в каталоге копии и зовёт `Program.Main` приложения:
# окно строит сама программа. Всё — на СКРЫТОМ рабочем столе (`HiddenRun.exe`): на экран
# человека окна не выходят. Кадр — `PrintWindow` окна 1880×1040 по рамке DWM (1866×1033).
# Действия человека (сет, режим FSA, вкладка, курсор) — вызовы обработчиков приложения.
#
# ПРОВЕРКА. После съёмки `check.py --shots` ищет в текстах окна (<сцена>.txt — подписи,
# поля, пункты списков, строки таблиц) личные метки настоящего конфига; картинку всё равно
# смотреть глазами: надписи на графике в текстах окна не видны.
#
# Коды возврата: 0 — снято (и установлено); 1 — съёмка сцены отказала (код пробы в выводе);
# 2 — неизвестный снимок; 3 — личное в текстах окна; 10–14 — сборка (`mk.ps1`).
param(
    [string[]]$Shots = @('all'),
    [string[]]$Lang = @('ru', 'en'),
    [string]$Work = 'D:\BqMoni_Claude\guideshot',
    [switch]$Build,
    [switch]$Install,
    [switch]$StaleControl,
    [string]$Store = '',
    [ValidateSet('corpus', 'file')][string]$DiskCurve = 'corpus',
    [string]$Gdml = '',
    [string[]]$Resized = @()
)
if (-not $Gdml) { $Gdml = Join-Path $PSScriptRoot '..\effmaker\probes\data\gdml_a322\a322_shielded.gdml' }
$Gdml = [IO.Path]::GetFullPath($Gdml)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$bin = Join-Path $repo 'BecquerelMonitor\bin\guideshot'
$tools = Join-Path $Work 'tools'
$app = Join-Path $Work 'app'

# Снимок -> сцена (таблица снимков гида и режимов — в журнале handover/handover-2026-11-08-guideshot.md).
$sceneOf = [ordered]@{
    'main' = 'main'; 'panel-control' = 'main'; 'panel-sample' = 'main'; 'spectrum-bar' = 'main'; 'view-modes' = 'main'
    'main-fsa' = 'main-fsa'; 'panel-result' = 'main-fsa'; 'panel-fsa' = 'main-fsa'; 'fsa-button' = 'main-fsa'
    'main-fsa-nomatrix' = 'main-fsa-nomatrix'
    'devcfg-general' = 'devcfg'; 'devcfg-device-rc' = 'devcfg'; 'devcfg-analysis' = 'devcfg'; 'devcfg-refs' = 'devcfg'
    'devcfg-ecal' = 'devcfg-ecal'; 'devcfg-device-obs' = 'devcfg'; 'devcfg-device-audio' = 'devcfg'
    'devcfg-device-ausb' = 'devcfg'; 'devcfg-device-aser' = 'devcfg'; 'devcfg-device-as' = 'devcfg'
    'devcfg-eff' = 'devcfg-eff'; 'respmatrix' = 'devcfg-eff'
    'devcfg-device-astcp' = 'devtcp'; 'devcfg-device-asrest' = 'devtcp'
    'effmaker-detector' = 'maker'; 'effmaker-source' = 'maker'; 'effmaker-shield' = 'maker'; 'effmaker-calc' = 'maker'
    'effmaker-gdml' = 'gdml'; 'effmaker-gdml-import' = 'gdml'
    'nucdef' = 'nucdef'; 'nucsets' = 'nucdef'; 'nucbase' = 'nucdef'
    'about' = 'dialogs'; 'prefs-chart' = 'dialogs'; 'prefs-color' = 'dialogs'; 'prefs-sound' = 'dialogs'; 'prefs-system' = 'dialogs'
}
$Shots = @($Shots | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
if ($Shots -contains 'all') { $Shots = @($sceneOf.Keys) }
foreach ($s in $Shots) { if (-not $sceneOf.Contains($s)) { Write-Host "снимок не снимается конвейером: $s" -ForegroundColor Red; exit 2 } }
$scenes = @($Shots | ForEach-Object { $sceneOf[$_] } | Select-Object -Unique)
$Lang = @($Lang | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

# Пробы собираются всегда (секунды): GuideShot.exe из прежнего захода против новой сборки —
# ровно состояние «приложение свежее, проба старая», которого не видно.
& (Join-Path $PSScriptRoot 'mk.ps1') -Work $Work -SkipApp:(-not $Build -and (Test-Path -LiteralPath (Join-Path $bin 'BecquerelMonitor.exe')))
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$mc = Join-Path $PSScriptRoot 'make_copy.py'
$storeArg = @("--disk-curve=$DiskCurve")
if ($Store) { $storeArg += "--store=$Store" }
& python -I $mc base "--work=$Work" "--bin=$bin" "--repo=$repo" @storeArg
if ($LASTEXITCODE -ne 0) { Write-Host 'копия программы: отказ' -ForegroundColor Red; exit 1 }
Copy-Item -LiteralPath (Join-Path $tools 'GuideShot.exe') -Destination $app -Force
# Перенаправления сборок приложения (SQLitePCLRaw и др.) — те же, что у BecquerelMonitor.exe.
Copy-Item -LiteralPath (Join-Path $app 'BecquerelMonitor.exe.config') -Destination (Join-Path $app 'GuideShot.exe.config') -Force

$failed = @()
foreach ($l in $Lang) {
    $out = Join-Path $Work "shots\$l"
    New-Item -ItemType Directory -Force $out | Out-Null
    foreach ($sc in $scenes) {
        & python -I $mc stage "--work=$Work" "--scene=$sc" "--lang=$l" "--repo=$repo"
        if ($LASTEXITCODE -ne 0) { $failed += "${sc}/${l}: копия"; continue }
        $extra = @()
        if ($StaleControl -and $sc -eq 'main') { $extra += '--stale-control' }
        if ($sc -eq 'gdml') { $extra += "--gdml=$Gdml" }
        & (Join-Path $tools 'HiddenRun.exe') 900 (Join-Path $app 'GuideShot.exe') "--scene=$sc" "--out=$out" @extra *> (Join-Path $out "$sc.console.txt")
        $code = $LASTEXITCODE
        Write-Host ("{0}/{1}: код {2}" -f $sc, $l, $code)
        if ($code -ne 0) { $failed += "${sc}/${l}: код ${code} (журнал $out\$sc.log)" }
    }
}
if ($failed.Count -gt 0) { $failed | ForEach-Object { Write-Host "ОТКАЗ $_" -ForegroundColor Red }; exit 1 }

& python -I (Join-Path $PSScriptRoot 'check.py') "--shots=$(Join-Path $Work 'shots')" "--repo=$repo"
if ($LASTEXITCODE -ne 0) { Write-Host 'в текстах окна найдено личное — снимки не установлены' -ForegroundColor Red; exit 3 }

if ($Install) {
    & (Join-Path $PSScriptRoot 'install_shots.ps1') -Shots $Shots -Lang $Lang -Work $Work -Resized $Resized
    exit $LASTEXITCODE
}
exit 0
