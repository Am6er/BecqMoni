# Сборка ВСЕХ проб и харнесс-файлов effmaker — проверка, что ничего не
# сломано молча (TODO T3). Проекта у проб нет нарочно (см. README); цена
# этого — компилятор молчит про файл, который перестал собираться, и проба
# выглядит как «сегодня не гоняли». Этот скрипт — тот самый читатель
# признака отказа: гонять после ЛЮБОГО удаления или переименования в
# приложении, а не только перед запуском конкретной пробы.
#
#   pwsh tools\effmaker\probes\build_all.ps1 [-Bin <каталог сборки приложения>]
#                                            [-Out <куда класть exe>]
#
# Умолчания: -Bin BecquerelMonitor\bin\Debug_Codex (агентская сборка),
# -Out tools\effmaker\probes\build (в .gitignore). Выход 1 — есть сломанные,
# с перечнем поимённо. Каждой собранной пробе кладётся рядом её
# <имя>.exe.config — копия BecquerelMonitor.exe.config (T32, закрыт 16.08.2026):
# без него инициализатор Microsoft.Data.Sqlite.SqliteConnection валит пробу
# TypeInitializationException на первом обращении к базе, потому что редирект
# SQLitePCLRaw.core живёт только в конфиге. Прежде конфиги здесь не делались
# «потому что сборке они не мешают» — но собирают пробы ради ЗАПУСКА, каталог
# build\ в .gitignore, и файл терялся при каждой чистой сборке у каждого. Цена
# ошибки — час на §13г журнала матрицы и повтор на MatrixRangeProbe,
# MaterialDbProbe и BoxSourceProbe 15.08.2026. Лишний конфиг у пробы, базы не
# читающей, не стоит ничего.
param(
    [string]$Bin = "",
    [string]$Out = ""
)
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $Bin) { $Bin = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex' }
if (-not $Out) { $Out = Join-Path $PSScriptRoot 'build' }
if (-not (Test-Path (Join-Path $Bin 'BecquerelMonitor.exe'))) {
    Write-Host "нет $Bin\BecquerelMonitor.exe — сначала соберите приложение"
    exit 2
}
New-Item -ItemType Directory -Force $Out | Out-Null

# Свежий BecquerelMonitor.exe — В КАТАЛОГ ПРОБ, каждый прогон. Пробы компилируются
# против $Bin, но ГРУЗЯТ сборку из своего каталога — и 14.08.2026 там пролежал
# exe от 09.08 (физика 10): матрицы и кривые двух новых геометрий посчитались
# устаревшей физикой при свежем исходнике. Копия обязана обновляться здесь же,
# где собираются пробы, — тем же движением (грабля класса mk_appwd).
# Базы — тем же правилом: рядом лежали matdb/nucdb/schemedb от 09.08 01:25 —
# ДО импорта fluorescence_k (01:46), и физика 11 падала на «no such table».
#
# ⚠ T45: список копируемого был ПОИМЁННЫМ и не включал зависимостей NuGet
# (`Microsoft.Data.Sqlite.dll`, `SQLitePCLRaw.*`, `SpecUtilsNet.dll`) и нативной
# `runtimes\win-x64\native\e_sqlite3.dll`. Рабочий `probes\build` жил только
# потому, что их туда положили когда-то давно; в ЧИСТЫЙ каталог (`build_rel`,
# T44) скрипт клал пробы, которые собираются и не запускаются: компилятор
# молчит, а первое обращение к базе валится `FileNotFoundException`, следом
# `TypeInitializationException` / «Library e_sqlite3 not found». Признак отказа
# не там, где причина, — и выглядит как поломка пробы.
# Поэтому здесь МАСКИ, а не имена, и ровно те же, что у `mk_appwd.ps1`: новая
# зависимость приложения подхватывается сама, без второго места, где о ней надо
# помнить.
$flat = @('BecquerelMonitor.exe', 'BecquerelMonitor.pdb', 'BecquerelMonitor.exe.config',
          '*.dll', '*.sqlite')
foreach ($mask in $flat) {
    Get-ChildItem (Join-Path $Bin $mask) -File -ErrorAction SilentlyContinue |
        Copy-Item -Destination $Out -Force
}

# Нативный провайдер SQLite лежит ПОДКАТАЛОГОМ (`runtimes\win-x64\native\
# e_sqlite3.dll`) и маской выше не берётся: без него SQLitePCLRaw находит
# управляемую обёртку и не находит саму библиотеку.
$rtSrc = Join-Path $Bin 'runtimes'
if (Test-Path $rtSrc) { Copy-Item $rtSrc $Out -Recurse -Force }

# Сателлит с русскими строками — той же копией и по той же причине (W22).
# Он лежит ОТДЕЛЬНОЙ папкой, и без неё проба, проверяющая обе локализации,
# молча мерит английские строки дважды и говорит, что проверила две. Раньше об
# этом было написано в README пробы FsaStackShot — предупреждение в тексте
# читателя не заменяет.
# ⚠ Копируются ФАЙЛЫ, а не сама папка: `Copy-Item <папка> <папка>` при уже
# существующем назначении кладёт копию ВНУТРЬ — получается `build\ru\ru`, а
# грузится при этом внешняя, то есть та, что лежала там с прошлого раза. Ровно
# так 16.08.2026 проба печатала английские строки при выставленной ru-RU: свежий
# сателлит уезжал в `ru\ru`, а читался лежалый.
$ruSrc = Join-Path $Bin 'ru'
if (Test-Path $ruSrc) {
    $ruDst = Join-Path $Out 'ru'
    New-Item -ItemType Directory -Force $ruDst | Out-Null
    Copy-Item (Join-Path $ruSrc '*') $ruDst -Recurse -Force
}

$csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
$facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'

# Полный набор ссылок сразу всем: лишняя ссылка бесплатна, а раздача особых
# случаев по пробам уже дважды устаревала молча.
$refs = @(
    "/r:$Bin\BecquerelMonitor.exe",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Xml.dll',
    '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll',
    "/r:$Bin\Microsoft.Data.Sqlite.dll",
    "/r:$Bin\WeifenLuo.WinFormsUI.Docking.dll",
    "/r:$facades\netstandard.dll"
)

$fail = @()
$locked = @()
$built = 0
$sources = @(Get-ChildItem (Join-Path $repo 'tools\effmaker\*.cs')) +
           @(Get-ChildItem (Join-Path $PSScriptRoot '*.cs'))
foreach ($f in $sources) {
    # Файлы без `Main` идут довеском к своим пробам, а сами не собираются.
    if ($f.Name -in @('GadrasDetector.cs', 'ResidualScan.cs', 'ProbeDeviceConfig.cs')) { continue }
    $extra = @()
    if ($f.Name -in @('GadrasProbe.cs', 'ResponseProbe.cs')) {
        $extra = @(Join-Path $PSScriptRoot 'GadrasDetector.cs')
    }
    if ($f.Name -in @('FsaCascadeProbe.cs', 'CorpusFsaProbe.cs')) {
        $extra = @(Join-Path $PSScriptRoot 'ResidualScan.cs')
    }
    # `S82`: разбор ссылки на прибор — один на все пробы, что его зовут.
    if ($f.Name -in @('FsaStackShot.cs', 'CorpusFsaProbe.cs')) {
        $extra += (Join-Path $PSScriptRoot 'ProbeDeviceConfig.cs')
    }
    $exe = Join-Path $Out ($f.BaseName + '.exe')

    # T41, вторая половина: ПЕРЕСБОРКА ПОВЕРХ РАБОТАЮЩЕЙ ПРОБЫ ОСТАВЛЯЕТ ОТ НЕЁ
    # ПУСТОЕ МЕСТО. 17.08.2026 `CorpusMatrixProbe.exe` считал матрицы в фоне,
    # csc не смог его переписать («файл используется») — и exe ИСЧЕЗ; следующий
    # фоновый запуск умер строкой «команда не распознана» и вышел с кодом 0,
    # то есть выглядел как успешный счёт. Занятый файл поэтому не трогаем вовсе:
    # проверяем ДО компиляции, называем поимённо и валим прогон в конце. Дыры
    # на месте рабочей пробы не остаётся.
    if (Test-Path $exe) {
        try {
            $h = [System.IO.File]::Open($exe, 'Open', 'ReadWrite', 'None')
            $h.Close()
        } catch {
            $locked += $f.BaseName
            Write-Host "ЗАНЯТ $($f.Name) — $($f.BaseName).exe запущен, не трогаю" -ForegroundColor Yellow
            continue
        }
    }

    $log = & $csc /nologo /target:exe /langversion:7.3 "/out:$exe" @refs $f.FullName @extra 2>&1
    if ($LASTEXITCODE -ne 0) {
        $fail += $f.Name
        Write-Host "FAIL $($f.Name)"
        $log | Select-Object -First 6 | ForEach-Object { Write-Host "    $_" }
        # Компилятор сносит цель ДО того, как убедится, что может её записать:
        # неудача оставляет не старый exe, а его отсутствие. Об этом надо сказать
        # отдельно — «FAIL» читается как «осталось как было».
        if (-not (Test-Path $exe)) {
            Write-Host "    ⚠ $($f.BaseName).exe при этом ИСЧЕЗ — прежней сборки на месте больше нет" -ForegroundColor Yellow
        }
    } else {
        # T32: конфиг кладётся ТУТ ЖЕ, тем же движением, что и сборка. Отдельный
        # проход по каталогу был бы вторым местом, где о пробе надо помнить.
        $appConfig = Join-Path $Out 'BecquerelMonitor.exe.config'
        if (Test-Path $appConfig) { Copy-Item $appConfig "$exe.config" -Force }
        Write-Host "ok   $($f.Name)"
        $built++
    }
}
Write-Host "----"
# Занятые пробы — НЕ успех (T41): в каталоге осталась СТАРАЯ сборка,
# а выглядело бы это как «все собрались» — тот же класс ошибки, что и исчезнувший exe.
if ($locked.Count) { Write-Host "ЗАНЯТЫ (старая сборка на месте): $($locked -join ', ')" }
if ($fail.Count) { Write-Host "СЛОМАНО: $($fail -join ', ')"; exit 1 }
if ($locked.Count) { exit 1 }
# Считаем СОБРАННОЕ, а не «всего минус один»: довесков без `Main` стало два, и
# прежняя формула начала врать ровно в тот день, когда появился второй.
Write-Host "все собрались: $built файлов (плюс $($sources.Count - $built) без Main, идут довеском)"
