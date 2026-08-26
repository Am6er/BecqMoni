# Запуск корпусного прогона ЧЕРЕЗ СТОРОЖА (`T63`) — то есть читатель отказа.
#
#   & 'tools\CORPUS\scripts\run_appwd.ps1' -Out <каталог>
#   & 'tools\CORPUS\scripts\run_appwd.ps1' -Out <каталог> -Extra '--lib=sample','--sthr=0.30'
#   & 'tools\CORPUS\scripts\run_appwd.ps1' -Out <каталог> -Bin <сборка> -Wd <оснастка> -Force
#
# ⛔ ЗВАТЬ ОПЕРАТОРОМ ВЫЗОВА `&` ИЗ ТЕКУЩЕЙ СЕССИИ, А НЕ ПОРОЖДАТЬ `pwsh` НА ФАЙЛ
#    (`T84`). Массив `-Extra` уходит в порождённый процесс ЧЕРЕЗ КОМАНДНУЮ
#    СТРОКУ и схлопывается. Измерено 27.08.2026 на копии этой же шапки `param()`,
#    аргумент `-Extra '--lib=sample','--sthr=0.30'`:
#      * `& <файл> …` из текущей сессии  -> `$Extra` = ДВА элемента, как задумано;
#      * `pwsh <файл> …` / `pwsh -File <файл> …` -> ОДИН элемент, и в нём лежат
#        сами кавычки: `'--lib=sample','--sthr=0.30'`. Проба на такой аргумент
#        печатает «неизвестный ключ» и возвращает 2;
#      * `pwsh -File <файл> -Extra @('--lib=sample','--sthr=0.30')` -> `$Extra`
#        первый ключ, `$Rest` второй, то есть код 64 ниже.
#    ⚠ С ОДНИМ ключом разницы не видно (один элемент и там, и там) — ломается
#      молча начиная со второго. Склеенный аргумент ловит проверка ниже, код 65.
#
# ⛔ Зачем эта обёртка вообще нужна. Признак без читателя — главная грабля этого
#    проекта: `B20` завела `matrix_note` «отпечаток НЕ сошёлся», и с 18.08 по
#    23.08 весь корпус гонялся БЕЗ матрицы, потому что этот признак никто не
#    спрашивал. Сторож оснастки, который только печатает предупреждение, — ровно
#    то же самое. Здесь отказ ОСТАНАВЛИВАЕТ прогон: проба не запускается,
#    код возврата 2, ни одного файла в `-Out` не появляется. Код 6 — план
#    оснастки не строится вовсе (нет каталога проб, нет `CorpusFsaProbe.exe`),
#    код 64 — лишние аргументы, код 65 — склеенный ключ пробы (`T84`).
#
# ⛔ Сторож спрашивает и ЧИСЛО ЗАПИСЕЙ библиотеки нуклидов (`T66`,
#    `Test-AppWdLibrary`). Прежде эта проверка жила только в `mk_appwd.ps1`,
#    то есть мимо прогона: сверка по sha256 её не заменяет — она сравнивает
#    копию с ИСТОЧНИКОМ, а вырожденный источник даёт вырожденную копию,
#    совпадающую с ним побайтно. Опыт 26.08.2026: 4-записная заготовка в обеих
#    точках — сторож печатал «свежая», и проба запускалась с кодом 0.
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
    Write-Host "   И звать ОПЕРАТОРОМ ВЫЗОВА из текущей сессии: & '<путь>\run_appwd.ps1' … (T84)." -ForegroundColor Red
    Write-Host "   Форма 'pwsh -File <файл> -Extra @(a,b)' даёт ровно этот отказ: второй ключ уезжает сюда." -ForegroundColor Red
    exit 64
}

# ⛔ СКЛЕЕННЫЙ КЛЮЧ — ЭТО ГРАБЛЯ ЗАПУСКА, А НЕ ОПЕЧАТКА (`T84`, 27.08.2026).
#    Без этой проверки склейку называет ПРОБА, и называет неверно: «неизвестный
#    ключ: '--lib=sample','--sthr=0.30'», код 2, — из чего следует вывод «сломан
#    ключ» вместо «сломан способ запуска». Мерка ловит ровно три признака,
#    и все три невозможны у настоящего ключа:
#      * кавычка ВНУТРИ аргумента — её вносит расщепление командной строки;
#      * `--` после запятой или пробела — это ВТОРОЙ ключ в том же аргументе
#        (запятая сама по себе законна: `--groups=G1S,ASN16`, `--only=a,b`);
#      * аргумент, не начинающийся с `--`, — ключей иного вида у пробы нет.
$glued = [System.Collections.Generic.List[string]]::new()
foreach ($e in @($Extra)) {
    if ($e -match '["'']')      { $glued.Add("кавычки внутри аргумента: $e"); continue }
    if ($e -match '[,\s]\s*--') { $glued.Add("два ключа в одном аргументе: $e"); continue }
    if ($e -notmatch '^--')     { $glued.Add("ключ пробы обязан начинаться с --: $e") }
}
if ($glued.Count -gt 0) {
    Write-Host ""
    Write-Host "⛔⛔ ОТКАЗ: КЛЮЧИ ПРОБЫ СКЛЕИЛИСЬ ПРИ ЗАПУСКЕ — ПРОБА НЕ ЗАПУЩЕНА" -ForegroundColor Red
    foreach ($g in $glued) { Write-Host ("   " + $g) -ForegroundColor Red }
    Write-Host ""
    Write-Host '   Дело не в самом ключе, а в способе запуска: pwsh <файл>.ps1 -Extra a,b' -ForegroundColor Red
    Write-Host "   порождает процесс, и массив уходит туда одной строкой вместе с кавычками." -ForegroundColor Red
    Write-Host "   Звать надо ОПЕРАТОРОМ ВЫЗОВА из текущей сессии:" -ForegroundColor Red
    Write-Host ("       & '{0}\run_appwd.ps1' -Out <каталог> -Extra '--lib=sample','--sthr=0.30'" -f $PSScriptRoot) -ForegroundColor Red
    Write-Host ""
    exit 65
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $Wd) { $Wd = Join-Path $PSScriptRoot 'wd_app' }
$st = Read-AppWdStamp -Wd $Wd
if (-not $Bin) {
    if ($st -and $st.bin) { $Bin = [string]$st.bin }
    else { $Bin = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex' }
}
if (-not $ProbeBuild -and $st -and $st.probes) { $ProbeBuild = [string]$st.probes }

$plan = New-AppWdPlanOrDie -Repo $repo -Bin $Bin -Wd $Wd -ProbeBuild $ProbeBuild
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
