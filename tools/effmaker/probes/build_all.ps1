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
# Коды возврата:
#   0 — собрались все; ВСЁ, что кладётся рядом с пробами, сверено по sha256, и
#       число сверенных файлов напечатано; библиотека нуклидов не вырождена;
#   1 — есть сломанные или занятые пробы (перечень поимённо), либо каталог не
#       сошёлся с источниками: файл не доехал, протух, приложение чужое,
#       поставочного конфига нет (`T69`, `T73`, `T77`, `T79`);
#   2 — нечем собирать (нет сборки приложения);
#   3 — сторож `appwd_plan.ps1` недоступен, СМЕНИЛ ПОДПИСЬ, не отработал или
#       провалил самопроверку; либо список исходников разошёлся с планом (`T83`);
#   6 — план оснастки не строится вовсе (отказ `New-AppWdPlanOrDie`: нет
#       собранных проб, нет `BecquerelMonitor.exe.config`).
#
# ⛔ ЧТО КЛАДЁТСЯ РЯДОМ С ПРОБАМИ — СПИСКА ЗДЕСЬ НЕТ И НЕ ДОЛЖНО БЫТЬ (`T61`,
# `T83`, 27.08.2026). Единственный список «источник → место» живёт в
# `Get-AppWdPlan` (`tools\CORPUS\scripts\appwd_plan.ps1`); этот скрипт берёт
# оттуда ПЛАН, копирует по нему чужим же `Invoke-AppWdPlan` и сверяет чужим же
# `Test-AppWdPlan`. Своего копирования, своих масок и своих имён конфигов здесь
# больше нет: прежде они были, и обе копии разошлись молча (`ProbeDeviceConfig.cs`
# 19.08.2026 — четыре дня несобираемого каталога; `config\BecquerelMonitor.xml`
# 26.08.2026 — `T77`).
#
# Из плана этот скрипт берёт СВОЮ долю: всё из `$Bin` (exe, конфиг, pdb, dll,
# три базы, `runtimes\`, `ru\`), `<проба>.exe.config` каждой пробе (`T32`) и
# ПОСТАВОЧНЫЙ `config\` (`NuclideDefinition.xml` + `BecquerelMonitor.xml`).
# Приборы корпуса и матрицы отклика — оснастка КОРПУСА, сюда не едут; их
# кладёт `mk_appwd.ps1`. Род файла, которого нет ни в одном из двух списков, —
# ОТКАЗ, а не «пропустим»: значит план начал класть что-то новое, и здесь об
# этом надо знать.
param(
    [string]$Bin = "",
    [string]$Out = ""
)
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $Bin) { $Bin = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex' }
if (-not $Out) { $Out = Join-Path $PSScriptRoot 'build' }
# Пути приводятся к полным СРАЗУ: план строит `Dst` склейкой из `$Out`, а
# сторож обходит каталог `Get-ChildItem`-ом и сравнивает с `Dst` СТРОКАМИ.
# `-Out .\build` или `-Out build\` дали бы несовпадение строк при том же
# каталоге — и каждый положенный файл выглядел бы посторонним.
$Bin = [IO.Path]::GetFullPath($Bin)
$Out = [IO.Path]::GetFullPath($Out)
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
try { . $planFile } catch {
    Write-Host "СТОРОЖ НЕ ЧИТАЕТСЯ: $planFile" -ForegroundColor Red
    Write-Host ("  {0}" -f $_.Exception.Message) -ForegroundColor Red
    exit 3
}

# ⛔ КОНТРАКТ СО СТОРОЖЕМ ПРОВЕРЯЕТСЯ ПОИМЁННО И ПОПАРАМЕТРНО (`T83`, 27.08.2026).
# Между этим скриптом и `appwd_plan.ps1` есть контракт, и он уже ломался — в тот
# же час, когда оба файла правились соседними заходами: у `Test-AppWdBuild`
# убрали ключ `-SkipWdChecks`, вызов свалился ошибкой ПРИВЯЗКИ ПАРАМЕТРА (не
# останавливающей скрипт), сторож вернул `$null`, а `$null.Bad.Count` в
# PowerShell это 0 — и сборка отчиталась «сошлось», ничего не сверив.
# Поэтому проверяется не наличие имени, а ПОДПИСЬ: набор параметров обязан
# совпасть посимвольно. Сменили — падаем сразу и называем, что именно сменили.
$contract = [ordered]@{
    'Get-AppWdPlan'      = @('Repo', 'Bin', 'Wd', 'ProbeBuild')
    'New-AppWdPlanOrDie' = @('Repo', 'Bin', 'Wd', 'ProbeBuild')
    'Invoke-AppWdPlan'   = @('Plan')
    'Get-AppWdExtra'     = @('Plan')
    'Test-AppWdPlan'     = @('Plan')
    'Test-AppWdBuild'    = @('Plan')
    'Test-AppWdLibrary'  = @('Plan')
}
$common = @([System.Management.Automation.PSCmdlet]::CommonParameters) +
          @([System.Management.Automation.PSCmdlet]::OptionalCommonParameters)
$broken = [System.Collections.Generic.List[string]]::new()
foreach ($name in $contract.Keys) {
    $cmd = Get-Command $name -CommandType Function -ErrorAction SilentlyContinue
    if (-not $cmd) { $broken.Add("нет функции $name"); continue }
    $have = @($cmd.Parameters.Keys | Where-Object { $_ -notin $common } | Sort-Object)
    $want = @($contract[$name] | Sort-Object)
    if (($have -join ',') -ne ($want -join ',')) {
        $broken.Add(("$name сменила подпись: ждали ({0}), нашли ({1})" -f ($want -join ', '), ($have -join ', ')))
    }
}
if ($broken.Count) {
    Write-Host ""
    Write-Host "⛔⛔ ОТКАЗ: СТОРОЖ И СБОРЩИК РАЗОШЛИСЬ (T83)" -ForegroundColor Red
    foreach ($b in $broken) { Write-Host ("   " + $b) -ForegroundColor Red }
    Write-Host ("   Файл: {0}" -f $planFile) -ForegroundColor Red
    Write-Host "   Молча собирать нельзя: непроверенный каталог проб — это чужие числа (B20/B21)." -ForegroundColor Red
    Write-Host ""
    exit 3
}

# ⛔ СТОРОЖ, КОТОРЫЙ НЕ ОТРАБОТАЛ, — ЭТО ОТКАЗ, А НЕ «ПРОВЕРЕНО».
# Мерено 26.08.2026 на себе: вызов свалился ошибкой привязки параметра — НЕ
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

function Deny-Guard {
    param([Parameter(Mandatory)][string]$Why)
    Write-Host ""
    Write-Host "⛔⛔ ОТКАЗ: СТОРОЖ НЕ ДОКАЗАЛ, ЧТО СВЕРЯЛ (T79)" -ForegroundColor Red
    foreach ($line in ($Why -split "`n")) { Write-Host ("   " + $line) -ForegroundColor Red }
    Write-Host "   Пустой список находок при нулевом числе сверенного — это ОТКАЗ, а не «проверено»." -ForegroundColor Red
    Write-Host ""
    exit 3
}

# ⛔ ПОЛОЖИТЕЛЬНЫЙ И ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ СТОРОЖА, КАЖДЫЙ ПРОГОН (`T79`).
#
# Мерено 26.08.2026: подложили сторожа, который не сравнивает НИЧЕГО и
# возвращает пустой список находок, `Count=152`, `Sha=falshivka12` — прежняя
# сборка напечатала «приложение сошлось со сборкой по sha256» и «библиотека:
# 152 записей», то есть ВЫДАЛА ВЫДУМАННЫЙ ОТПЕЧАТОК ЗА ИЗМЕРЕННЫЙ, и вышла с
# кодом 0. Проверять ответ сторожа на форму мало: форма у выдумки правильная.
#
# Поэтому сторож перед работой прогоняется на ПОДСТАВНОЙ оснастке в %TEMP%,
# дважды:
#   * ЦЕЛОЙ — все три сверки обязаны дать РОВНО НОЛЬ находок. Сторож, который
#     отказывает всегда, бесполезен так же, как тот, что молчит всегда;
#   * ПОРЧЕНОЙ — база рядом с пробами подменена, библиотека заменена на
#     4-записную заготовку, приложение в каталоге проб чужое. Каждая из трёх
#     сверок обязана дать не меньше одной находки. Это ровно те три подмены,
#     которые проходили насквозь до 27.08.2026.
# Ни одна проверка не смотрит на ТЕКСТ находок: словами сторожа этот скрипт не
# связан, иначе переформулировка сообщения ломала бы сборку на пустом месте.
# Цена — доли секунды и десяток файлов-подстав, которые тут же сносятся.
function Assert-GuardIsAlive {
    param([Parameter(Mandatory)][string]$Repo)

    $root = Join-Path ([IO.Path]::GetTempPath()) ("bq_guardtest_" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    $fail = $null
    try {
        $fRepo   = Join-Path $root 'repo'
        $fBin    = Join-Path $root 'bin'
        $fProbes = Join-Path $root 'probes'
        $fWd     = Join-Path $root 'wd'
        foreach ($d in @((Join-Path $fRepo 'BecquerelMonitor\config'),
                         (Join-Path $fRepo 'tools\effmaker\probes'),
                         $fBin, (Join-Path $fBin 'runtimes\win-x64\native'), (Join-Path $fBin 'ru'),
                         $fProbes, $fWd)) {
            New-Item -ItemType Directory -Force $d | Out-Null
        }
        # Конфиги подставные, а не поставочные: самопроверка спрашивает СТОРОЖА,
        # и от состояния дерева зависеть не должна — иначе пропавший поставочный
        # файл выглядел бы как сломанный сторож (мерено 27.08.2026: так и вышло).
        # Записей делается заведомо больше порога вырожденности, и порог берётся
        # ЕГО ЖЕ, а не переписывается сюда числом.
        $nucN = 200
        if ($script:AppWdNuclideMin -is [int]) { $nucN = $script:AppWdNuclideMin + 10 }
        Set-Content -Encoding ascii -LiteralPath (Join-Path $fRepo 'BecquerelMonitor\config\NuclideDefinition.xml') `
            -Value ('<?xml version="1.0"?><NuclideDefinitionFile><NuclideDefinitions>' +
                    ('<Nuclide/>' * $nucN) + '</NuclideDefinitions></NuclideDefinitionFile>')
        Set-Content -Encoding ascii -LiteralPath (Join-Path $fRepo 'BecquerelMonitor\config\BecquerelMonitor.xml') `
            -Value '<?xml version="1.0"?><GlobalConfigInfo/>'
        Set-Content -LiteralPath (Join-Path $fRepo 'tools\effmaker\probes\CorpusFsaProbe.cs') -Value '// podstava' -Encoding ascii
        Set-Content -LiteralPath (Join-Path $fBin 'BecquerelMonitor.exe')        -Value 'app-podstava'     -Encoding ascii
        Set-Content -LiteralPath (Join-Path $fBin 'BecquerelMonitor.exe.config') -Value '<configuration/>' -Encoding ascii
        Set-Content -LiteralPath (Join-Path $fBin 'podstava.dll')                -Value 'dll'              -Encoding ascii
        Set-Content -LiteralPath (Join-Path $fBin 'podstava.sqlite')             -Value 'baza'             -Encoding ascii
        Set-Content -LiteralPath (Join-Path $fBin 'runtimes\win-x64\native\e_sqlite3.dll') -Value 'native'  -Encoding ascii
        Set-Content -LiteralPath (Join-Path $fBin 'ru\podstava.resources.dll')   -Value 'satellit'         -Encoding ascii
        Copy-Item -LiteralPath (Join-Path $fBin 'BecquerelMonitor.exe') -Destination (Join-Path $fProbes 'BecquerelMonitor.exe') -Force
        Set-Content -LiteralPath (Join-Path $fProbes 'CorpusFsaProbe.exe') -Value 'proba' -Encoding ascii
        # Исходник обязан быть СТАРШЕ пробы, иначе сторож законно скажет
        # «пробы старше своих исходников» и отрицательный контроль не сойдётся.
        (Get-Item -LiteralPath (Join-Path $fRepo 'tools\effmaker\probes\CorpusFsaProbe.cs')).LastWriteTime = (Get-Date).AddHours(-1)

        $p = Get-AppWdPlan -Repo $fRepo -Bin $fBin -Wd $fWd -ProbeBuild $fProbes
        Invoke-AppWdPlan -Plan $p | Out-Null

        # 1. ОТРИЦАТЕЛЬНЫЙ КОНТРОЛЬ: целая оснастка — ноль находок у всех троих.
        $n1 = @((Test-AppWdPlan    -Plan $p).Bad).Count
        $n2 = @((Test-AppWdBuild   -Plan $p).Bad).Count
        $n3 = @((Test-AppWdLibrary -Plan $p).Bad).Count
        if ($n1 -or $n2 -or $n3) {
            $fail = ("на ЦЕЛОЙ подставной оснастке сторож нашёл отказы: оснастка {0}, сборка {1}, библиотека {2} — должно быть 0/0/0." -f $n1, $n2, $n3) +
                    "`nСторож, который отказывает всегда, не отличает целый каталог от порченого."
        }

        # 2. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: три подмены, каждая обязана быть найдена.
        if (-not $fail) {
            Add-Content -LiteralPath (Join-Path $fWd 'podstava.sqlite') -Value 'porcha'
            Set-Content -LiteralPath (Join-Path $fWd 'config\NuclideDefinition.xml') -Encoding ascii -Value @'
<?xml version="1.0"?>
<NuclideDefinitionFile><NuclideDefinitions><Nuclide/><Nuclide/><Nuclide/><Nuclide/></NuclideDefinitions></NuclideDefinitionFile>
'@
            Set-Content -LiteralPath (Join-Path $fProbes 'BecquerelMonitor.exe') -Value 'chuzhoe-prilozhenie' -Encoding ascii
            $m1 = @((Test-AppWdPlan    -Plan $p).Bad).Count
            $m2 = @((Test-AppWdBuild   -Plan $p).Bad).Count
            $m3 = @((Test-AppWdLibrary -Plan $p).Bad).Count
            if ($m1 -lt 1 -or $m2 -lt 1 -or $m3 -lt 1) {
                $fail = ("на ПОРЧЕНОЙ подставной оснастке сторож промолчал: оснастка {0}, сборка {1}, библиотека {2} — должно быть >=1 у каждой." -f $m1, $m2, $m3) +
                        "`nПодменены: база рядом с пробами, библиотека нуклидов (4 записи), приложение в каталоге проб."
            }
        }
    } catch {
        $fail = "самопроверка сторожа не собралась: $($_.Exception.Message)"
    } finally {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($fail) {
        Write-Host ""
        Write-Host "⛔⛔ ОТКАЗ: СТОРОЖ ПРОВАЛИЛ САМОПРОВЕРКУ (T79)" -ForegroundColor Red
        foreach ($line in ($fail -split "`n")) { Write-Host ("   " + $line) -ForegroundColor Red }
        Write-Host ("   Файл: {0}" -f $planFile) -ForegroundColor Red
        Write-Host "   Проверять каталог проб нечем — собирать молча нельзя." -ForegroundColor Red
        Write-Host ""
        exit 3
    }
}
Assert-GuardIsAlive -Repo $repo

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
# ⛔ ЭТОТ ПЕРЕБОР ОБЯЗАН СОВПАСТЬ С `$probeSrc` ИЗ `Get-AppWdPlan` (`T83`).
# Второго списка исходников быть не должно, но компилировать надо ДО того, как
# план вообще можно построить (план требует уже собранных проб), — поэтому
# перебор здесь остаётся, а расхождение с планом ловится ниже сверкой множеств
# и валит прогон. Ключи `-File -Force` — те же, что у плана: без `-Force` скрытый
# `.cs` попадал бы в план и не попадал в сборку.
$sources = @(Get-ChildItem (Join-Path $repo 'tools\effmaker\*.cs') -File -Force -ErrorAction SilentlyContinue) +
           @(Get-ChildItem (Join-Path $repo 'tools\effmaker\probes\*.cs') -File -Force -ErrorAction SilentlyContinue)

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

# ⛔ ОСНАСТКА КЛАДЁТСЯ ПОСЛЕ СБОРКИ И ПО ЧУЖОМУ ПЛАНУ (`T77`, `T79`, `T83`).
# Порядок «сначала собрать, потом обставить» — не перестановка ради красоты:
# план перечисляет пробы, а до компиляции их в чистом каталоге ещё нет (`T45`:
# в свежий `build_rel` пробы клались, а зависимости — нет, и каталог собирался,
# но не запускался). После компиляции план полон, и одно движение кладёт ВСЁ:
# приложение, dll, три базы, `runtimes\`, `ru\`, `<проба>.exe.config` и
# ПОСТАВОЧНЫЙ `config\` целиком.
#
# `-Wd $Out` и `-ProbeBuild $Out` — это не подмена: для проб, запускаемых
# отсюда, каталог `$Out` и есть их рабочий каталог, приложение они грузят из
# него же, и `config\` приложение считает ОТ НЕГО
# (`Package.MainConfig` при `IsStandAlone` = `config\BecquerelMonitor.xml`,
# путь ОТНОСИТЕЛЬНЫЙ).
$plan = New-AppWdPlanOrDie -Repo $repo -Bin $Bin -Wd $Out -ProbeBuild $Out
foreach ($field in @('Pairs', 'ProbeSources', 'Repo', 'Bin', 'Wd', 'ProbeBuild')) {
    if ($null -eq $plan.PSObject.Properties[$field]) {
        Deny-Guard "план вернулся без поля $field — это не план `Get-AppWdPlan`, а что-то другое"
    }
}
if (@($plan.Pairs).Count -eq 0) { Deny-Guard "план пуст: класть рядом с пробами нечего, а так не бывает" }

# ⛔ КОНТРАКТ ПО ИСХОДНИКАМ (`T83`). Один и тот же набор `.cs` нужен здесь (чем
# компилировать) и плану (чем отсеять exe без исходника и чем судить о свежести
# сборки). Списка два физически — в разных файлах, — поэтому их равенство
# спрашивается механически, а не «по глазам»: расхождение валит прогон и
# называет ОБЕ стороны поимённо.
$planSrc = @($plan.ProbeSources | ForEach-Object { [IO.Path]::GetFullPath($_.FullName) })
$mySrc   = @($sources           | ForEach-Object { [IO.Path]::GetFullPath($_.FullName) })
$onlyPlan = @($planSrc | Where-Object { $mySrc   -notcontains $_ })
$onlyMine = @($mySrc   | Where-Object { $planSrc -notcontains $_ })
if ($onlyPlan.Count -or $onlyMine.Count) {
    Write-Host ""
    Write-Host "⛔⛔ ОТКАЗ: СПИСКИ ИСХОДНИКОВ ПРОБ РАЗОШЛИСЬ (T83)" -ForegroundColor Red
    foreach ($x in $onlyPlan) { Write-Host ("   есть у плана, нет у сборщика: {0}" -f $x) -ForegroundColor Red }
    foreach ($x in $onlyMine) { Write-Host ("   есть у сборщика, нет у плана: {0}" -f $x) -ForegroundColor Red }
    Write-Host ("   Сборщик: {0}" -f $PSCommandPath) -ForegroundColor Red
    Write-Host ("   План:    {0} (Get-AppWdPlan, `$probeSrc)" -f $planFile) -ForegroundColor Red
    Write-Host "   Один из двух перестал видеть пробу — собранное и сверенное это разные наборы." -ForegroundColor Red
    Write-Host ""
    exit 3
}

# Доля этого скрипта в плане. Род файла определяется полем `Why`, и род, которого
# нет ни в одном из трёх списков, — ОТКАЗ: значит план начал класть что-то новое.
$whyMine  = { $_.Why -eq 'сборка' -or $_.Why -like 'сборка\*' -or
              $_.Why -eq 'exe.config пробы' -or $_.Why -eq 'поставочный конфиг' }
$whyCorpus = @('прибор корпуса', 'матрица отклика')   # оснастка КОРПУСА, кладёт mk_appwd.ps1
$whySelf   = 'проба'                                  # `$Out` и есть каталог проб: копия самой в себя
$unknown = @($plan.Pairs |
    Where-Object { -not (& $whyMine) -and $_.Why -notin $whyCorpus -and $_.Why -ne $whySelf } |
    ForEach-Object { $_.Why } | Sort-Object -Unique)
if ($unknown.Count) {
    Write-Host ""
    Write-Host "⛔⛔ ОТКАЗ: ПЛАН КЛАДЁТ НЕИЗВЕСТНЫЙ РОД ФАЙЛОВ (T83)" -ForegroundColor Red
    foreach ($u in $unknown) { Write-Host ("   Why = {0}" -f $u) -ForegroundColor Red }
    Write-Host "   Молча пропустить нельзя: либо это кладём и мы, либо это оснастка корпуса." -ForegroundColor Red
    Write-Host ("   Решается в {0} — там же, где заведён новый род." -f $planFile) -ForegroundColor Red
    Write-Host ""
    exit 3
}

# Пары «проба» здесь обязаны быть копией файла в себя (`Wd` = `ProbeBuild` = `$Out`).
# Если это не так — скрипт зовут не так, как он задуман, и копировать вслепую нельзя.
$notSelf = @($plan.Pairs | Where-Object {
    $_.Why -eq $whySelf -and [IO.Path]::GetFullPath($_.Src) -ne [IO.Path]::GetFullPath($_.Dst) })
if ($notSelf.Count) {
    Deny-Guard ("план ведёт пробы из чужого каталога: {0} пар, первая {1} -> {2}" -f
                $notSelf.Count, $notSelf[0].Src, $notSelf[0].Dst)
}

$minePairs = @($plan.Pairs | Where-Object $whyMine)
# ⛔ РОДА, БЕЗ КОТОРЫХ КАТАЛОГ ПРОБ НЕРАБОТОСПОСОБЕН, СПРАШИВАЮТСЯ ПОИМЁННО.
# `runtimes` — `T45` (нет нативной `e_sqlite3.dll` → «Library e_sqlite3 not
# found» на первом же чтении базы); `ru` — `W22` (проба молча мерит английские
# строки дважды и говорит, что проверила две); `поставочный конфиг` — `T73`/`T77`
# (без `NuclideDefinition.xml` проба ЗАВОДИТ СЕБЕ библиотеку из четырёх линий,
# без `BecquerelMonitor.xml` `GlobalConfigManager.LoadConfigFile()` показывает
# `MessageBox` безусловно, и безоконный прогон виснет насмерть).
$haveWhy = @($plan.Pairs | ForEach-Object { $_.Why } | Sort-Object -Unique)
$mustWhy = @('сборка', 'сборка\runtimes', 'сборка\ru', 'exe.config пробы', 'поставочный конфиг', 'проба')
$lostWhy = @($mustWhy | Where-Object { $_ -notin $haveWhy })
if ($lostWhy.Count) {
    Write-Host ""
    Write-Host "⛔⛔ ОТКАЗ: ПЛАНУ НЕЧЕГО ПОЛОЖИТЬ РЯДОМ С ПРОБАМИ (T45/T73/T77)" -ForegroundColor Red
    foreach ($w in $lostWhy) { Write-Host ("   нет ни одного файла рода: {0}" -f $w) -ForegroundColor Red }
    Write-Host ("   Источник: {0}" -f $Bin) -ForegroundColor Red
    Write-Host "   Такой каталог собирается и не запускается — молча класть его нельзя." -ForegroundColor Red
    Write-Host ""
    exit 1
}

# План СВОЕЙ доли: копируется и сверяется чужим кодом, своего копирования здесь нет.
# `Exclusive` снимается нарочно: склад приборов и матриц — оснастка корпуса, этот
# скрипт его не ведёт и чистить его не вправе.
function New-SubPlan {
    param([Parameter(Mandatory)]$Base, [Parameter(Mandatory)][AllowEmptyCollection()][array]$Pairs)
    [pscustomobject]@{
        Repo = $Base.Repo; Bin = $Base.Bin; Wd = $Base.Wd
        Corpus = $Base.Corpus; Response = $Base.Response; ProbeBuild = $Base.ProbeBuild
        Pairs = $Pairs
        ProbeSources = $Base.ProbeSources
        Strays = $Base.Strays
        Exclusive = @()
    }
}
$selfPairs  = @($plan.Pairs | Where-Object { $_.Why -eq $whySelf })
$copyPlan   = New-SubPlan -Base $plan -Pairs $minePairs
$strictPlan = New-SubPlan -Base $plan -Pairs (@($minePairs) + @($selfPairs))

# ⛔ ПОСТОРОННЕЕ В КАТАЛОГЕ ПРОБ НАЗЫВАЕТСЯ, НО ПРОГОН НЕ ВАЛИТ. Это РЕШЕНИЕ,
# а не недосмотр, и вот довод. `Test-AppWdPlan` считает любой ЗАГРУЖАЕМЫЙ файл
# вне плана отказом — правило писалось под `wd_app`, оснастку, которую
# `mk_appwd.ps1` строит целиком и потому вправе чистить `Remove-AppWdExtra`-ом.
# Каталог проб — не оснастка: это ВЫХОД сборки и одновременно рабочий каталог,
# в котором лежат и продукты прогонов, и положенные руками конфиги (мерено
# 27.08.2026: в `probes\build` три `<guid>_CorpusMatrixProbe.exe` от 09–17.08,
# в `probes\build_rel` — одиннадцать `config\ROI\*.xml`, `config\layout\*.xml`
# и `config\device\AtomSpectraVCP.xml`). Отказывать на них значит завести
# сторожа, который отказывает ВСЕГДА; сносить их значит удалять чужое.
# Поэтому они перечисляются поимённо, с ЧУЖИМ же доводом из `Get-AppWdExtra`
# (второго обхода «что здесь лишнее» не заводим), и добавляются в план сверки
# парой «сам в себя»: такая сверка ничего не доказывает и в число сверенного
# НЕ ИДЁТ — она лишь снимает с них чужой вердикт. Что с ними делать дальше —
# вопрос к строке `T84`, а не к сборке.
try { $extra = @(Get-AppWdExtra -Plan $strictPlan) } catch {
    Deny-Guard ("Get-AppWdExtra оборвалась: {0}" -f $_.Exception.Message)
}
$extraLoad = @($extra | Where-Object { $_.Load })
$extraPairs = @($extraLoad | ForEach-Object {
    [pscustomobject]@{ Src = $_.File.FullName; Dst = $_.File.FullName; Why = 'постороннее (не наше)' }
})
$minePlan = New-SubPlan -Base $plan -Pairs (@($minePairs) + @($selfPairs) + @($extraPairs))
# Копирование НЕ оборачивается в `Invoke-AppWdCheck` нарочно: при
# `$ErrorActionPreference='Continue'` неудачная `Copy-Item` (запущенная отсюда
# проба держит `BecquerelMonitor.exe` или базу открытыми) ругается в консоль и
# идёт дальше — и это правильно. Что именно не доехало, скажет сверка ниже
# поимённо, а не общее «сторож не отработал».
try { Invoke-AppWdPlan -Plan $copyPlan | Out-Null } catch {
    Write-Host ("РАСКЛАДКА ОБОРВАЛАСЬ: {0}" -f $_.Exception.Message) -ForegroundColor Red
    Write-Host "  Каталог проб обставлен наполовину — пользоваться им нельзя." -ForegroundColor Red
    exit 1
}

# ⛔ СВЕРЯЕТСЯ ВСЁ, ЧТО ПОЛОЖЕНО, И СВЕРКА ДОКАЗЫВАЕТСЯ ЧИСЛОМ (`T79`).
# Мерено 26.08.2026: прежняя сборка сверяла только `BecquerelMonitor.exe` и
# число записей библиотеки. Три базы, все `*.dll`, `runtimes\win-x64\native\` и
# русский сателлит не сверялись ВОВСЕ — `matdb.sqlite`, заменённая на 20 байт
# мусора, и `nucdb.sqlite`, заменённая на XML, давали ровно ту же зелёную
# строку и код 0. Теперь сверяются все пары своей доли, и число сошедшихся
# ОБЯЗАНО совпасть с числом положенных: пустой список находок при нулевом
# числе сверенного — отказ (класс `T63`).
$chk   = Invoke-AppWdCheck 'оснастка'   { Test-AppWdPlan    -Plan $minePlan }
$guard = Invoke-AppWdCheck 'сборка'     { Test-AppWdBuild   -Plan $plan }
$lib   = Invoke-AppWdCheck 'библиотека' { Test-AppWdLibrary -Plan $plan }
$bad   = @($chk.Bad) + @($guard.Bad) + @($lib.Bad)
if ($bad.Count) {
    Write-Host ""
    Write-Host "⛔⛔ ОТКАЗ: КАТАЛОГ ПРОБ НЕ СООТВЕТСТВУЕТ ИСХОДНИКАМ (T69/T73/T77/T79)" -ForegroundColor Red
    $i = 0
    foreach ($x in $bad) { $i++; Write-Host ("  {0,2}. {1}" -f $i, $x) -ForegroundColor Red }
    Write-Host ""
    Write-Host "  Пробы ЗАПУСКАЮТСЯ из $Out и грузят приложение, базы и конфиг ОТТУДА." -ForegroundColor Red
    Write-Host "  Числа с такого каталога недействительны (B20/B21)." -ForegroundColor Red
    Write-Host "  Порядок: закрыть пробы, собрать приложение, перегнать build_all.ps1." -ForegroundColor Red
    exit 1
}
if ($null -eq $chk.PSObject.Properties['Ok']) {
    Deny-Guard 'Test-AppWdPlan вернулась без поля Ok — сколько файлов сверено, сказать нечем'
}
if ($chk.Ok -ne @($minePlan.Pairs).Count) {
    Deny-Guard ("находок нет, а сошлось {0} пар из {1} — сверены не все." -f $chk.Ok, @($minePlan.Pairs).Count)
}
if ($minePairs.Count -eq 0) {
    Deny-Guard 'ни одного файла со стороны не сверено — сверять было нечего, а так не бывает'
}
# Отпечаток и число записей библиотеки ПЕЧАТАЮТСЯ, поэтому спрашиваются на форму:
# 26.08.2026 подставной сторож вернул `Sha=falshivka12`, и прежняя сборка выдала
# эту строку за измеренную.
if ($lib.Count -le 0 -or ($lib.Sha -notmatch '^[0-9a-f]{12}$')) {
    Deny-Guard ("библиотека: записей {0}, sha '{1}' — так измеренный отпечаток не выглядит." -f $lib.Count, $lib.Sha)
}

foreach ($x in $extraLoad) {
    Write-Host ("⚠ ПОСТОРОННЕЕ в каталоге проб: {0}  {1}" -f $x.Rel, $x.File.LastWriteTime.ToString('dd.MM HH:mm')) -ForegroundColor Yellow
    Write-Host ("    {0}" -f $x.Why) -ForegroundColor Yellow
}
if (@($extra).Count -gt $extraLoad.Count) {
    Write-Host ("  (ещё {0} файлов — продукты прогонов, приложение их не грузит)" -f (@($extra).Count - $extraLoad.Count))
}
Write-Host ("сверено рядом с пробами: {0} файлов по sha256 с источником" -f $minePairs.Count)
foreach ($g in ($minePairs | Group-Object Why | Sort-Object Name)) {
    Write-Host ("    {0,-18} {1}" -f $g.Name, $g.Count)
}
Write-Host ("  (сверять не с чем ещё у {0} проб и {1} посторонних: их источник — сам этот каталог)" -f
            $selfPairs.Count, $extraLoad.Count)
Write-Host ("приложение рядом с пробами сошлось со сборкой: {0}" -f $Bin)
Write-Host ("библиотека нуклидов: {0} записей, sha {1} (поставочная)" -f $lib.Count, $lib.Sha)
# Считаем СОБРАННОЕ, а не «всего минус один»: довески без `Main` не единственны,
# и прежняя формула начала врать ровно в тот день, когда появился второй. Их
# число здесь НЕ ПИШЕТСЯ — оно выводится из дерева и печатается строкой выше.
Write-Host "все собрались: $built файлов (плюс $($sources.Count - $built) без Main, идут довеском)"
