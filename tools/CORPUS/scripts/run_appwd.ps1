# Запуск корпусного прогона ЧЕРЕЗ СТОРОЖА (`T63`) — то есть читатель отказа.
#
#   pwsh tools/CORPUS/scripts/run_appwd.ps1 -Out <каталог>
#   pwsh tools/CORPUS/scripts/run_appwd.ps1 -Out <каталог> -Extra '--lib=sample','--sthr=0.30'
#   pwsh tools/CORPUS/scripts/run_appwd.ps1 -Out <каталог> -Bin <сборка> -Wd <оснастка> -Force
#
# ⛔ Зачем эта обёртка вообще нужна. Признак без читателя — главная грабля этого
#    проекта: `B20` завела `matrix_note` «отпечаток НЕ сошёлся», и с 18.08 по
#    23.08 весь корпус гонялся БЕЗ матрицы, потому что этот признак никто не
#    спрашивал. Сторож оснастки, который только печатает предупреждение, — ровно
#    то же самое. Здесь отказ ОСТАНАВЛИВАЕТ прогон: проба не запускается,
#    код возврата 2, ни одного файла в `-Out` не появляется.
#
# ⛔ Ключи пробы передаются ИМЕНОВАННЫМИ параметрами, а не россыпью. Причина
#    измерена 25.08.2026 при первой же проверке этого файла: PowerShell рвёт
#    хвостовой аргумент `--out=C:\путь` ПО ДВОЕТОЧИЮ (синтаксис `-Имя:Значение`),
#    и проба получила `--out=C` и `\путь` двумя кусками — то есть молча писала
#    бы результат не туда. Здесь строку для пробы собирает сам скрипт.
#
# `-Force` есть, и он громкий: бывает нужно нарочно прогнать старой оснасткой
# (A/B по сборкам). Такой прогон обязан быть осознанным, а не случайным.
param(
    [Parameter(Mandatory)][string]$Out,
    [string]$Corpus = '',
    [string[]]$Extra = @(),
    [string]$Bin = '',
    [string]$Wd  = '',
    [string]$ProbeBuild = '',
    [switch]$Force,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Rest
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'appwd_plan.ps1')

if ($Rest -and $Rest.Count -gt 0) {
    Write-Host "⛔ ЛИШНИЕ АРГУМЕНТЫ: $($Rest -join ' ')" -ForegroundColor Red
    Write-Host "   Ключи пробы передавайте так: -Out <каталог> -Extra '--lib=sample','--sthr=0.30'" -ForegroundColor Red
    Write-Host "   Россыпью нельзя: PowerShell рвёт '--out=C:\путь' по двоеточию." -ForegroundColor Red
    exit 64
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $Wd) { $Wd = Join-Path $PSScriptRoot 'wd_app' }
$st = Read-AppWdStamp -Wd $Wd
if (-not $Bin) {
    if ($st -and $st.bin) { $Bin = [string]$st.bin }
    else { $Bin = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex' }
}
if (-not $ProbeBuild -and $st -and $st.probes) { $ProbeBuild = [string]$st.probes }

$plan = Get-AppWdPlan -Repo $repo -Bin $Bin -Wd $Wd -ProbeBuild $ProbeBuild
if (-not $Corpus) { $Corpus = $plan.Corpus }
$bad = Invoke-AppWdGuard -Plan $plan

if ($bad -gt 0) {
    if (-not $Force) {
        Write-Host "⛔ ПРОБА НЕ ЗАПУЩЕНА. Соберите оснастку заново: pwsh mk_appwd.ps1" -ForegroundColor Red
        Write-Host "   (осознанный прогон протухшей оснасткой — ключ -Force)" -ForegroundColor Red
        exit 2
    }
    Write-Host "⚠⚠ -Force: ПРОГОН НА ПРОТУХШЕЙ ОСНАСТКЕ. Числа этого прогона" -ForegroundColor Yellow
    Write-Host "   в журнал и в базу корпуса НЕ ГОДЯТСЯ." -ForegroundColor Yellow
}

$probe = Join-Path $Wd 'CorpusFsaProbe.exe'
if (-not (Test-Path -LiteralPath $probe)) { throw "нет $probe" }

$argv = @("--corpus=$Corpus", "--out=$Out") + $Extra
Write-Host ("запуск: CorpusFsaProbe.exe " + ($argv -join ' '))

Push-Location -LiteralPath $Wd
try {
    & $probe @argv
    $rc = $LASTEXITCODE
} finally {
    Pop-Location
}
exit $rc
