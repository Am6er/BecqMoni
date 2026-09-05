# Сторож оснастки корпусного прогона отдельной командой (`T63`).
#
#   & 'tools\CORPUS\scripts\check_appwd.ps1' [-Bin <сборка>] [-Wd <оснастка>] [-ProbeBuild <пробы>] [-Store <склад>]
#   & 'tools\CORPUS\scripts\check_appwd.ps1' -SelfTest   # доезжают ли ключи -Extra (T84)
#
# ⛔ Звать оператором вызова `&`, а не `pwsh <файл>.ps1` (`T84`/`T91`): при запуске
#    файлом аргументы разбирает командная строка, а не PowerShell-парсер. У этого
#    скрипта массивов нет, но правило одно на всю оснастку.
#
# Коды возврата: 0 — оснастка сошлась с источниками по sha256, по временам
# сборок, по числу записей библиотеки нуклидов, не несёт постороннего
# загружаемого файла и помечена отметкой о сборке; 6 — план оснастки не строится
# вовсе (нет каталога проб, нет `CorpusFsaProbe.exe`); любое другое число —
# сколько нашлось отказных расхождений. Ничего не чинит: чинит `mk_appwd.ps1`,
# а запуск прогона держит `run_appwd.ps1`.
#
# Весь разбор — в `appwd_plan.ps1`: там же лежит и список «что откуда кладётся»,
# по которому оснастку СОБИРАЮТ. Двух списков нет нарочно (урок `T61`).
#
# ⛔ `-SelfTest` (`T84`, 05.09.2026) — приёмка ПЕРЕДАЧИ ключей, а не оснастки:
#    сколько ключей `-Extra` доезжает до `param()` при трёх способах запуска
#    (один ключ, два, три). Мерится на копии блока `param()` из `run_appwd.ps1`
#    (файл пишется во временный каталог и печатает `$Extra.Count`), потому что
#    сам `run_appwd.ps1` без оснастки не доходит до печати. Ждём: `& <файл>` и
#    `pwsh -Command "& <файл>"` — все ключи; `pwsh -File <файл>` — ВСЕГДА один
#    (это и есть грабля, и приёмка обязана её ПОКАЗЫВАТЬ, а не прятать).
#    Приёмка, которая проходит всегда, ничего не меряет — поэтому запрещённая
#    форма мерится тоже, и её «1» при двух и трёх ключах входит в ожидание.
param(
    [string]$Bin = '',
    [string]$Wd  = '',
    [string]$ProbeBuild = '',
    # `S138`: склад матриц ПЛЕЧА. Без ключа сторож сверял клейма только со
    # штатным складом корпуса и на оснастке малой базы (`wd_mini16`, склад
    # `tools\effmaker\out\mini16`) давал 44 «НЕТ В ОСНАСТКЕ [матрица отклика]»
    # на ровном месте (полоса C6, 05.09.2026). У `mk_appwd.ps1` и
    # `run_appwd.ps1` ключ был, у сторожа отдельной командой — нет.
    [string]$Store = '',
    [switch]$SelfTest
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'appwd_plan.ps1')

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path

if ($SelfTest) {
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("appwd_selftest_" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force $tmp | Out-Null
    $stand = Join-Path $tmp 'extra_stand.ps1'
    # Ровно та же шапка, что у `run_appwd.ps1`: `[string[]]$Extra` плюс хвост
    # `ValueFromRemainingArguments` — иначе `pwsh -File … @(a,b)` мерился бы не так.
    @(
        'param([string]$Out = "", [string[]]$Extra = @(),'
        '      [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest)'
        # ⚠ `@($null).Count` равен 1, а не 0 — несвязанный `$Rest` считать явно.
        '$rc = if ($null -eq $Rest) { 0 } else { @($Rest).Count }'
        'Write-Output ("{0} {1}" -f @($Extra).Count, $rc)'
    ) | Set-Content -LiteralPath $stand -Encoding utf8
    $keys = @('--band=whole', '--only=ASN16_Cs137', '--share-thr=0.30')
    $rows = @()
    foreach ($n in 1..3) {
        $k = @($keys | Select-Object -First $n)
        $lit = "'" + ($k -join "','") + "'"
        # 1. оператор вызова из текущей сессии
        $got = (& $stand -Out x -Extra $k | Select-Object -Last 1)
        $rows += [pscustomobject]@{ Keys = $n; How = '& <файл>';                     Got = $got; Want = "$n 0" }
        # 2. новый процесс, но разбор PowerShell-парсером
        $got = (pwsh -NoProfile -Command "& '$stand' -Out x -Extra $lit" | Select-Object -Last 1)
        $rows += [pscustomobject]@{ Keys = $n; How = 'pwsh -Command "& <файл>"';     Got = $got; Want = "$n 0" }
        # 3. запуск ФАЙЛОМ — запрещённая форма; ждём ровно её беду: один элемент
        $got = (pwsh -NoProfile -File $stand -Out x -Extra $lit | Select-Object -Last 1)
        $rows += [pscustomobject]@{ Keys = $n; How = 'pwsh -File <файл> (ЗАПРЕЩЕНО)'; Got = $got; Want = '1 0' }
    }
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    $bad = @($rows | Where-Object { $_.Got -ne $_.Want })
    Write-Host ""
    Write-Host "=== ПРИЁМКА ПЕРЕДАЧИ КЛЮЧЕЙ -Extra (T84): «доехало ключей, ушло в хвост» ===" -ForegroundColor Cyan
    foreach ($r in $rows) {
        $mark = if ($r.Got -eq $r.Want) { 'ok ' } else { '⛔ ' }
        Write-Host ("  {0} {1} ключ(ей), {2,-32} -> {3,-5} (ждали {4})" -f $mark, $r.Keys, $r.How, $r.Got, $r.Want)
    }
    Write-Host ""
    if ($bad.Count -eq 0) {
        Write-Host "  ПРИЁМКА ПРОШЛА: & и pwsh -Command довозят все ключи; pwsh -File теряет всё сверх первого" -ForegroundColor Green
        Write-Host ""
        exit 0
    }
    Write-Host ("⛔⛔ ПРИЁМКА НЕ ПРОШЛА: расхождений {0}" -f $bad.Count) -ForegroundColor Red
    Write-Host ""
    exit $bad.Count
}

if (-not $Wd) { $Wd = Join-Path $PSScriptRoot 'wd_app' }
# ⛔ ПУТЬ ОСНАСТКИ — В АБСОЛЮТНЫЙ (`T91`, 05.09.2026): план сверяет обход каталога
#    (абсолютные `FullName`) со склейкой от параметра, и на относительном `-Wd`
#    сторож объявлял всю оснастку посторонней — отказ не по той причине.
$Wd = [System.IO.Path]::GetFullPath($Wd).TrimEnd('\')
# Из какой сборки оснастку собирали, знает её собственная отметка: иначе
# оснастку из `bin\Release_Codex` сторож сверял бы с `bin\Debug_Codex`
# и отказывал бы на ровном месте. Отметки нет — оснастка либо не собиралась,
# либо самопроверка при сборке не прошла: тогда умолчания, и сторож откажет —
# именно за отсутствие отметки (`Test-AppWdStamp`, `T80`; до 27.08.2026 эта
# фраза была обещанием без читателя, и оснастка без отметки давала код 0).
$st = Read-AppWdStamp -Wd $Wd
if (-not $Bin) {
    if ($st -and $st.bin) { $Bin = [string]$st.bin }
    else { $Bin = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex' }
}
if (-not $ProbeBuild -and $st -and $st.probes) { $ProbeBuild = [string]$st.probes }

$plan = New-AppWdPlanOrDie -Repo $repo -Bin $Bin -Wd $Wd -ProbeBuild $ProbeBuild -Store $Store
exit (Invoke-AppWdGuard -Plan $plan)
