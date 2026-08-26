# Собрать ИЗОЛИРОВАННЫЙ рабочий каталог для корпусного прогона кодом
# приложения (TODO S1) и построить в нём `CorpusFsaProbe.exe`.
#
#   pwsh tools/CORPUS/scripts/mk_appwd.ps1 [-Bin <сборка>] [-Wd <каталог>] [-SkipBuild] [-Force]
#
# Зачем отдельный каталог, а не `bin\Debug_Codex`, где уже лежит рецепт F25 «а»:
#
#   * приложение считает себя standalone всегда, кроме ClickOnce, и конфиг
#     берёт ОТ РАБОЧЕГО КАТАЛОГА — значит, каталог и есть конфигурация прогона;
#   * в `bin\...\config` библиотека нуклидов ЛИБО СТАРАЯ, ЛИБО ЕЁ НЕТ ВОВСЕ, и
#     прогон, запущенный оттуда, молча меряет не тот состав. Мерено 25.08.2026
#     (`T66`): в `bin\Debug_Codex\config` файла `NuclideDefinition.xml` нет
#     СОВСЕМ, а в `bin\Debug\config` лежит копия от 07.08.2026 на 44 записи
#     против 152 поставочных. Причина в `BecquerelMonitor.csproj:1261`: у этой
#     записи `Content` нет `CopyToOutputDirectory`, то есть сборка её в выход
#     не кладёт НИКОГДА — файл уезжает только в ClickOnce (`PublishFile`);
#   * `%AppData%\BecqMoni` — конфиг Amber, писать туда нельзя, а проба с
#     ключом `--rebuild` в родне (`FsaCascadeProbe`) пишет матрицы.
#
# Каталог `wd_app` попадает под `scripts/wd_*/` в .gitignore — как и остальные
# рабочие каталоги корпуса.
#
# ⛔ СПИСОК «ЧТО ОТКУДА КЛАДЁТСЯ» ЖИВЁТ НЕ ЗДЕСЬ, а в `appwd_plan.ps1`
#    (`Get-AppWdPlan`) — вместе со сторожем, который по НЕМУ ЖЕ и проверяет
#    (`T63`, урок `T61`: сторож обязан спрашивать тот же код, что кладёт файлы).
#    Здесь остались только причины, почему кладётся именно это.
#
# ⛔ ПОСЛЕ сборки оснастка проверяется сама на себе, и при расхождении этот
#    скрипт ОТКАЗЫВАЕТ кодом возврата. Запускать прогон надо через
#    `run_appwd.ps1` — он спрашивает того же сторожа и не пускает пробу.
param(
    [string]$Bin = "",
    [string]$Wd = "",
    # Оптимизированный рецепт кладёт пробы в `probes\build_rel` — оснастку из
    # `bin\Release_Codex` собирать надо оттуда, иначе приложение будет из одной
    # сборки, а пробы рядом с ним — из другой.
    [string]$ProbeBuild = "",
    [switch]$SkipBuild,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'appwd_plan.ps1')

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $Bin) { $Bin = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex' }
if (-not $Wd)  { $Wd  = Join-Path $PSScriptRoot 'wd_app' }

if (-not (Test-Path (Join-Path $Bin 'BecquerelMonitor.exe'))) {
    throw "нет $Bin\BecquerelMonitor.exe — сначала соберите приложение"
}

$plan = Get-AppWdPlan -Repo $repo -Bin $Bin -Wd $Wd -ProbeBuild $ProbeBuild

# T41: СБОРКА СТАРШЕ ИСХОДНИКОВ — и это МОЛЧАЛО. 16.08.2026 в `wd_app` лежал
# exe от 17:25, а `PowerFwhmCalibration.cs` написан в 23:35 того же дня: типа
# класс не знал, XML-десериализатор пропустил неизвестный элемент МОЛЧА,
# `rd.FwhmCalibration` осталась null, проба законно откатилась на калибровку
# прибора — и прогон отработал без единой ошибки, дав правдоподобные числа
# (понятная 1766.1 при невязке 53 %), из которых был сделан вывод «дефект в
# самом узле». На свежей сборке узел работает: 692.3 при 17.9 %.
# ⛔ До 25.08.2026 здесь стояло ПРЕДУПРЕЖДЕНИЕ — и это ровно «признак отказа
# без читателя»: собрать оснастку из протухшей сборки оно не мешало. Теперь
# отказ, с кодом возврата 3 (`T63`).
$pre = Test-AppWdBuild -Plan $plan -SkipWdChecks
if ($pre.Bad.Count -gt 0) {
    Write-Host ""
    Write-Host "⛔⛔ ОТКАЗ: ОСНАСТКУ НЕ ИЗ ЧЕГО СОБИРАТЬ" -ForegroundColor Red
    foreach ($x in $pre.Bad) { Write-Host ("   " + $x) -ForegroundColor Red }
    Write-Host ""
    if (-not $Force) {
        Write-Host "   Пересоберите приложение и пробы. Осознанно — ключ -Force." -ForegroundColor Red
        exit 3
    }
    Write-Host "⚠⚠ -Force: оснастка собирается из ПРОТУХШЕЙ сборки." -ForegroundColor Yellow
}

if (-not (Test-Path $plan.Response)) {
    # Матрицы не лежат в репозитории (см. .gitignore) — они считаются на месте.
    # Молча собрать каталог без них значит намерить «понятную» часть БЕЗ
    # матрицы и не заметить: разложение просто тихо станет хуже.
    throw "нет $($plan.Response) — сначала посчитайте матрицы (corpuseffprobe / CorpusMatrixProbe)"
}

New-Item -ItemType Directory -Force $Wd | Out-Null

# Копирование по плану. Что именно копируется и почему:
#   * сборка приложения (exe, конфиг, pdb, библиотеки, три базы, `runtimes`,
#     русский сателлит `ru`);
#   * прочие пробы СВЕЖИМИ из `tools\effmaker\probes\build` — сам этот каталог
#     строит только `CorpusFsaProbe` (ниже), а соседи (`FsaStackShot`,
#     `RoiActivityProbe`, …) попадали сюда однажды и потом лежали месяцами:
#     15.08.2026 снимок таблицы FSA рисовался пробой от 10:09 и показывал
#     разбор, которого в коде уже не было;
#   * конфиг — ПОСТАВОЧНЫЙ, а не от `mkconfig.py` (там сеты-обманки `[decoy]`);
#   * конфигурации приборов корпуса и матрицы отклика — `ResponseMatrixStore`
#     ищет матрицу в `config\device\response` рабочего каталога (S1).
# ⚠ Каталоги приборов и матриц СНАЧАЛА ОЧИЩАЮТСЯ (T33, 16.08.2026): копирование
#   поверх оставляет файл, которого в корпусе больше нет, а после переименования
#   конфигурации (B6, раздел G1S на эпохи) старая и новая несут ОДИН GUID —
#   приложение встаёт на модальном окне «Одинаковые GUID в разных файлах
#   конфигурации устройств», и в прогоне без консоли это выглядит как зависание.
#   Оба каталога целиком строятся из корпуса, так что чистить их безопасно
#   по построению; сторож это же и проверяет — лишних файлов там быть не должно.
$copied = Invoke-AppWdPlan -Plan $plan
Write-Host ("  положено файлов: {0}" -f $copied)

$killed = Remove-AppWdOrphans -Plan $plan
if ($killed.Count -gt 0) {
    Write-Host ("  убрано проб без источника: " + ($killed -join ', ')) -ForegroundColor Yellow
}

# Сама проба. ДОВЕСКИ ВЫВОДЯТСЯ ИЗ ДЕРЕВА, а не перечисляются (`T57`,
# 23.08.2026): файл каталога проб без `Main` — не проба, а общий кусок, и
# идёт довеском.
#
# ⛔ Прежде здесь лежал список имён руками, и он устарел молча:
# `ProbeDeviceConfig.cs` завели 19.08.2026 при `S82`, сюда не вписали, и
# рабочий каталог корпуса не собирался четыре дня — `csc` валился на
# «Имя "ProbeDeviceConfig" не существует в текущем контексте». Отказ был
# ГРОМКИЙ, и только это спасло; правило то же, что в `build_all.ps1`.
if (-not $SkipBuild) {
    $csc = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe'
    $facades = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades'
    $src = Join-Path $repo 'tools\effmaker\probes\CorpusFsaProbe.cs'
    $companions = @(Get-ChildItem (Join-Path $repo 'tools\effmaker\probes\*.cs') |
        Where-Object {
            -not (Select-String -Path $_.FullName -Pattern 'static\s+(int|void)\s+Main\s*\(' -Quiet)
        } | ForEach-Object { $_.FullName })
    Write-Host ("  довески без Main: " + (($companions | Split-Path -Leaf) -join ', '))
    $exe = Join-Path $Wd 'CorpusFsaProbe.exe'
    & $csc /nologo /target:exe /platform:anycpu /langversion:7.3 "/out:$exe" `
        "/r:$Wd\BecquerelMonitor.exe" `
        /r:System.dll /r:System.Core.dll /r:System.Xml.dll `
        /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        "/r:$Wd\Microsoft.Data.Sqlite.dll" "/r:$facades\netstandard.dll" `
        $src $companions
    if ($LASTEXITCODE -ne 0) { throw "csc failed" }
    # Пробам, читающим базы, нужен свой exe.config — иначе binding redirect
    # SQLitePCLRaw не применяется и чтение падает уже на месте.
    Copy-Item (Join-Path $Wd 'BecquerelMonitor.exe.config') "$exe.config" -Force
}

Write-AppWdStamp -Plan $plan -Files $copied

$dev = (Get-ChildItem (Join-Path $Wd 'config\device\*.xml') -File).Count
$mx  = (Get-ChildItem (Join-Path $Wd 'config\device\response\*.rmx') -File).Count
Write-Host ""
Write-Host "рабочий каталог: $Wd"
Write-Host ("  конфигураций приборов: {0}, матриц: {1}" -f $dev, $mx)

# ⛔ БИБЛИОТЕКА НАЗЫВАЕТ СЕБЯ В ЖУРНАЛЕ ПРОГОНА (`T66`). Сторож выше уже
#    проверил, что файл совпал с источником, — но этого мало: в дереве лежат
#    ЧЕТЫРЕ рода копий `NuclideDefinition.xml` (поставочная 152 записи,
#    корневая `config\` 143 без полей `Sets`/`Chain`, `wd_<группа>` от 114 до
#    278 от `mkconfig.py`, `probes\build` — 4-записная заготовка), и ровно на
#    этом споткнулась `S63`: потолок опознания мерен по КОРНЕВОЙ копии, а
#    корпус считался по поставочной. Отпечаток в журнале даёт привязать число
#    прогона к библиотеке ПОСТ-ФАКТУМ, а не по памяти.
$nucWd = Join-Path $Wd 'config\NuclideDefinition.xml'
if (Test-Path $nucWd) {
    $nucN = ([xml](Get-Content $nucWd -Raw)).NuclideDefinitionFile.NuclideDefinitions.Nuclide.Count
    $nucH = (Get-FileHash $nucWd -Algorithm SHA256).Hash.Substring(0, 12).ToLower()
    Write-Host ("  библиотека нуклидов: {0} записей, sha {1}" -f $nucN, $nucH)
    if ($nucN -lt 100) {
        # 4 записи — заготовка, которую приложение создаёт САМО, когда файла
        # нет (`NuclideDefinitionManager.GetInstance`). Состав библиотеки
        # задаёт и поиск пиков, и разбор FSA: прогонять по заготовке нельзя.
        Write-Host "⛔ это ЗАГОТОВКА, а не библиотека — прогонять НЕЛЬЗЯ." -ForegroundColor Red
        if (-not $Force) { exit 5 }
    }
} else {
    Write-Host "⛔ в оснастке НЕТ config\NuclideDefinition.xml" -ForegroundColor Red
    if (-not $Force) { exit 5 }
}

# ⛔ САМОПРОВЕРКА ТЕМ ЖЕ СТОРОЖЕМ, что держит прогон. Если после сборки оснастка
#    всё равно не сходится с источниками — значит, план и копирование разошлись,
#    и молчать об этом нельзя.
$left = Invoke-AppWdGuard -Plan $plan
if ($left -gt 0 -and -not $Force) {
    Write-Host "⛔ САМОПРОВЕРКА ОСНАСТКИ НЕ ПРОШЛА — прогонять НЕЛЬЗЯ." -ForegroundColor Red
    exit 4
}

Write-Host "запуск прогона — ТОЛЬКО через сторожа:"
Write-Host ("  pwsh `"{0}\run_appwd.ps1`" -Out `"{1}\tools\pie\out_app`"" -f $PSScriptRoot, $repo)
Write-Host "проверить оснастку отдельно:"
Write-Host ("  pwsh `"{0}\check_appwd.ps1`"" -f $PSScriptRoot)
