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
# -Out tools\effmaker\probes\build (в .gitignore).
#
# Коды возврата: 0 — собрались все, приложение рядом с пробами сошлось со
# сборкой по sha256, поставочная библиотека нуклидов на месте; 1 — есть
# сломанные или занятые пробы (перечень поимённо), либо приложение в `-Out` не
# то, против которого собирали (`T69`); 2 — нечем собирать (нет сборки
# приложения) или нечего класть (нет поставочного конфига, `T73`); 3 — сторож
# `appwd_plan.ps1` недоступен, и сверить приложение не с чем.
#
# Каждой собранной пробе кладётся рядом её
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

# ⛔ СТОРОЖ БЕРЁТСЯ ГОТОВЫМ И ПОДКЛЮЧАЕТСЯ ЗДЕСЬ, ДО ВСЯКОЙ РАБОТЫ (`T69`).
# Всё, чем этот скрипт себя проверяет, живёт в `tools\CORPUS\scripts\appwd_plan.ps1`
# (`T63`): там же лежит и план оснастки корпуса, и сверки. Своих таких же здесь
# нет нарочно — второй список «что чему обязано соответствовать» в этом дереве
# уже дважды устаревал молча (`T61`, `T57`).
# Спрашивается в начале, чтобы не собирать семь десятков проб и только потом
# узнать, что проверить их нечем.
$planFile = Join-Path $repo 'tools\CORPUS\scripts\appwd_plan.ps1'
if (-not (Test-Path -LiteralPath $planFile)) {
    Write-Host "НЕТ СТОРОЖА: $planFile" -ForegroundColor Red
    Write-Host "  Сверить приложение и библиотеку рядом с пробами нечем — каталог недоверенный (T69)." -ForegroundColor Red
    exit 3
}
. $planFile
$need = @('Get-AppWdPlan', 'Test-AppWdBuild', 'Test-AppWdLibrary')
$lost = @($need | Where-Object { -not (Get-Command $_ -ErrorAction SilentlyContinue) })
if ($lost.Count) {
    Write-Host ("в $planFile нет: {0}" -f ($lost -join ', ')) -ForegroundColor Red
    Write-Host "  Сторож переехал — проверять нечем, а молча собирать нельзя (T69)." -ForegroundColor Red
    exit 3
}

# ⛔ СТОРОЖ, КОТОРЫЙ НЕ ОТРАБОТАЛ, — ЭТО ОТКАЗ, А НЕ «ПРОВЕРЕНО».
# Мерено 26.08.2026 на себе: `appwd_plan.ps1` правили в соседней сессии и убрали
# ключ `-SkipWdChecks`. Вызов свалился ошибкой привязки параметра — НЕ
# останавливающей скрипт, — сторож вернул `$null`, а `$null.Bad.Count` в
# PowerShell это 0. Проверка «находок нет» прошла, и сборка отчиталась «сошлось»,
# НИЧЕГО НЕ СВЕРИВ. Ровно тот класс ошибки, который она и должна ловить.
# Поэтому: ошибку ловим, ответ проверяем на форму, и любое «не отработал» —
# код возврата 3, а не тишина.
function Invoke-AppWdCheck {
    param([Parameter(Mandatory)][string]$What, [Parameter(Mandatory)][scriptblock]$Body)
    $ErrorActionPreference = 'Stop'
    try { $r = & $Body } catch {
        Write-Host ("СТОРОЖ НЕ ОТРАБОТАЛ ($What): {0}" -f $_.Exception.Message) -ForegroundColor Red
        Write-Host "  Ничего не сверено. Числа с этого каталога недоверенные (T69)." -ForegroundColor Red
        exit 3
    }
    if ($null -eq $r -or $null -eq $r.PSObject.Properties['Bad']) {
        Write-Host "СТОРОЖ НЕ ОТРАБОТАЛ ($What): ответ без поля Bad" -ForegroundColor Red
        Write-Host "  Ничего не сверено. Числа с этого каталога недоверенные (T69)." -ForegroundColor Red
        exit 3
    }
    $r
}

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

# ⛔ ПОСТАВОЧНАЯ БИБЛИОТЕКА НУКЛИДОВ — ТЕМ ЖЕ ДВИЖЕНИЕМ (`T73`, 26.08.2026).
# Без неё проба, запущенная отсюда, не падает и не молчит — она ЗАВОДИТ СЕБЕ
# БИБЛИОТЕКУ САМА. `NuclideDefinitionManager.GetInstance()` не находит
# `config\NuclideDefinition.xml` (`Package.NuclideDefinition` при `IsStandAlone`
# — а это всё, кроме ClickOnce, — отдаёт путь ОТНОСИТЕЛЬНЫЙ, то есть считает его
# от каталога ЗАПУСКА, а он тут ровно `$Out`),
# зовёт `InitializeNuclideDefinitionFile()` и СОХРАНЯЕТ заготовку на диск:
# Cs134 605, Cs137 662, Cs134 798, K40 1460 — четыре линии, `Intencity` 0,
# пустой `Chain`, ни одного сета. Против 152 нуклидов и 5 сетов в поставке.
# Состав библиотеки задаёт и поиск пиков, и разбор FSA, поэтому проба отсюда
# разбирает спектр по четырём линиям и печатает правдоподобные числа не про то.
# Измерено в день заведения строки: здесь лежала ровно такая заготовка от
# 18.08.2026 10:54, md5 c5254a6f — руками её никто не клал, её написала проба,
# запущенная из этого каталога, и с тех пор её же и читали.
# Записав заготовку, та же ветка показывает `MessageBox` — безусловно, без
# оглядки на наличие окон (`NuclideDefinitionManager.cs:153`): безоконная проба
# виснет на нём насмерть, и со стороны это выглядит как «долго считает».
# Конфиг берётся ПОСТАВОЧНЫЙ, из `BecquerelMonitor\config`, а не сгенерированный:
# в рабочих каталогах корпуса лежат сеты-обманки `[decoy]` под изучение гейта.
$cfgDir = Join-Path $Out 'config'
$nucSrc = Join-Path $repo 'BecquerelMonitor\config\NuclideDefinition.xml'
$nucDst = Join-Path $cfgDir 'NuclideDefinition.xml'
if (-not (Test-Path -LiteralPath $nucSrc)) {
    Write-Host "нет $nucSrc — поставочной библиотеки нуклидов в дереве не осталось" -ForegroundColor Red
    exit 2
}
New-Item -ItemType Directory -Force $cfgDir | Out-Null
Copy-Item -LiteralPath $nucSrc -Destination $nucDst -Force

# Копия обязана СОВПАСТЬ с поставкой, и спрашивается это ТУТ ЖЕ, за 0.6 с: при
# `$ErrorActionPreference='Continue'` неудачная `Copy-Item` ругается в консоль и
# идёт дальше, а заготовка на месте библиотеки от неё ничем не отличается, кроме
# содержимого. Мерено: заготовку, открытую на чтение (так её держит запущенная
# проба), копия не перебивает — и без этой сверки прогон пошёл бы дальше.
# ЧТО В ФАЙЛЕ ЛЕЖИТ, спрашивает не эта сверка, а `Test-AppWdLibrary` в конце:
# порог вырожденности — её число, не наше.
if (-not (Test-Path -LiteralPath $nucDst) -or
    (Get-FileHash -LiteralPath $nucSrc -Algorithm SHA256).Hash -ne
    (Get-FileHash -LiteralPath $nucDst -Algorithm SHA256).Hash) {
    Write-Host "БИБЛИОТЕКА НЕ ДОЕХАЛА: $nucDst не совпал с поставкой" -ForegroundColor Red
    Write-Host "  Проба, запущенная отсюда, заведёт себе заготовку из четырёх линий (T73)." -ForegroundColor Red
    exit 2
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

# ДОВЕСКИ ВЫВОДЯТСЯ, А НЕ ПЕРЕЧИСЛЯЮТСЯ (`T57`, 23.08.2026). Файл без `Main` —
# не проба, а общий кусок; такие идут довеском ко ВСЕМ пробам и сами не
# собираются.
#
# ⛔ Прежде и здесь, и в `mk_appwd.ps1` лежали списки имён РУКАМИ, и второй уже
# устарел молча: `ProbeDeviceConfig.cs` завели 19.08.2026 при `S82`, вписать
# забыли, и рабочий каталог корпуса не собирался четыре дня. Список, который
# надо помнить, однажды забывают — поэтому его больше нет.
#
# ⚠ Довесок кладётся КАЖДОЙ пробе, а не той, что его зовёт: лишний класс в
# сборке не стоит ничего, а «кому какой довесок» — ровно та таблица, которая и
# устаревала. Цена — секунды на прогон, и она измерена.
$companions = @($sources | Where-Object {
    -not (Select-String -Path $_.FullName -Pattern 'static\s+(int|void)\s+Main\s*\(' -Quiet)
})
$companionPaths = @($companions | ForEach-Object { $_.FullName })
if ($companionPaths.Count -gt 0) {
    Write-Host ("довески без Main: " + (($companions | ForEach-Object { $_.Name }) -join ', '))
}

foreach ($f in $sources) {
    if ($f.FullName -in $companionPaths) { continue }
    $extra = @($companionPaths)
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

# ⛔ ПРИЛОЖЕНИЕ РЯДОМ С ПРОБАМИ ОБЯЗАНО БЫТЬ ТЕМ ЖЕ, ПРОТИВ КОТОРОГО СОБИРАЛИ
# (`T69`, 26.08.2026). Пробы компилируются против `$Bin`, а ГРУЗЯТ приложение из
# СВОЕГО каталога — и это разные файлы. Копия кладётся выше тем же движением,
# что и всё остальное, но доехать она может НЕ ВСЕГДА и молча:
#   * запущенная отсюда проба держит `BecquerelMonitor.exe` открытым на чтение,
#     `Copy-Item` при `$ErrorActionPreference='Continue'` ругается в консоль и
#     идёт дальше — рядом с пробами остаётся ЧУЖОЕ приложение;
#   * приложение пересобрали, ПОКА здесь компилировались семь десятков проб.
# Измерено 25.08.2026: в `probes\build` лежал exe от 12:56:08 sha EBC3C48A0479,
# а в `bin\Debug_Codex` уже от 13:10:30 sha 14418B5AEBEF. Ни ошибки, ни
# предупреждения — проба считает СТАРЫМ приложением и выдаёт правдоподобные
# чужие числа. Это класс `B20`/`B21`, каждый из которых стоил корпусу недель.
#
# Сверка ЧУЖАЯ и берётся готовой: `Test-AppWdBuild` из
# `tools\CORPUS\scripts\appwd_plan.ps1` (`T63`) уже сравнивает
# `<ProbeBuild>\BecquerelMonitor.exe` со сборкой по sha256 — и заодно ловит
# сборку старше исходников (`T41`) и пробы старше своих `.cs`. Рядом с ней
# спрашивается `Test-AppWdLibrary`: она смотрит, ЧТО лежит в
# `<Wd>\config\NuclideDefinition.xml`, и порог вырожденности библиотеки — её
# число (`$AppWdNuclideMin`), не наше. Своих таких же сверок здесь нет нарочно.
#
# `-Wd $Out` и `-ProbeBuild $Out` — это не подмена: для проб, запускаемых отсюда,
# каталог `$Out` и есть их рабочий каталог, приложение они грузят из него же, и
# конфиг приложение считает ОТ НЕГО. Ни одна из двух сверок ничего про оснастку
# корпуса при этом не спрашивает.
#
# Спрашивается ПОСЛЕ сборки, а не до: до неё пробы законно старше своих
# исходников — это и есть то, что этот скрипт только что починил.
$plan = Invoke-AppWdCheck 'план' {
    $p = Get-AppWdPlan -Repo $repo -Bin $Bin -Wd $Out -ProbeBuild $Out
    [pscustomobject]@{ Bad = @(); Plan = $p }
}
$guard = Invoke-AppWdCheck 'сборка' { Test-AppWdBuild   -Plan $plan.Plan }
$lib   = Invoke-AppWdCheck 'библиотека' { Test-AppWdLibrary -Plan $plan.Plan }
$bad   = @($guard.Bad) + @($lib.Bad)
if ($bad.Count) {
    Write-Host ""
    Write-Host "⛔⛔ ОТКАЗ: КАТАЛОГ ПРОБ НЕ СООТВЕТСТВУЕТ ИСХОДНИКАМ (T69/T73)" -ForegroundColor Red
    $i = 0
    foreach ($x in $bad) { $i++; Write-Host ("  {0,2}. {1}" -f $i, $x) -ForegroundColor Red }
    Write-Host ""
    Write-Host "  Пробы ЗАПУСКАЮТСЯ из $Out и грузят приложение и конфиг ОТТУДА." -ForegroundColor Red
    Write-Host "  Числа с такого каталога недействительны (B20/B21)." -ForegroundColor Red
    Write-Host "  Порядок: закрыть пробы, собрать приложение, перегнать build_all.ps1." -ForegroundColor Red
    exit 1
}
Write-Host ("приложение рядом с пробами сошлось со сборкой по sha256: {0}" -f $Bin)
Write-Host ("библиотека нуклидов: {0} записей, sha {1} (поставочная)" -f $lib.Count, $lib.Sha)
# Считаем СОБРАННОЕ, а не «всего минус один»: довески без `Main` не единственны,
# и прежняя формула начала врать ровно в тот день, когда появился второй. Их
# число здесь НЕ ПИШЕТСЯ — оно выводится из дерева и печатается строкой выше.
Write-Host "все собрались: $built файлов (плюс $($sources.Count - $built) без Main, идут довеском)"
