# Сверка каталога проб с исходниками ПЕРЕД ЗАПУСКОМ — пункт (2) строки `A77`.
#
#   & 'tools\effmaker\probes\run_fresh.ps1' -Exe <путь к .exe> -Extra '--n=200','--pairth=1'
#   & 'tools\effmaker\probes\run_fresh.ps1' -Exe <...> -CheckOnly       # только сверка
#
# ⛔ Звать оператором вызова `&`, а не `pwsh -File <файл>` (`T84`/`T91`): при
#    запуске файлом до `param()` доезжает ВСЕГДА ОДИН элемент массива `-Extra`.
#
# ═══ Зачем это вообще есть ═══════════════════════════════════════════════════
#
# ⛔ 02.09.2026 трёхчасовой прогон склада пошёл каталогом проб, собранным ЧАСОМ
#    РАНЬШЕ правки (`build_rel` 20:26 против исходника 21:28). Старый разбор
#    увидел новое значение ключа `--cone=far`, сравнил его с «0», счёл «не ноль»
#    и включил конус ВСЕМ 44 сценам. Ни отказа, ни предупреждения.
#    ⚠ Ни побитовый замер, ни клеймо этого не ловят: содержимое матриц вышло
#    верным, испорчено ПРОИСХОЖДЕНИЕ — они пометились `cone=on`. Цена — три часа.
#
# Строгий разбор ключей (пункт 1 `A77`) закрыл ПОЛОВИНУ беды — ту, где проба не
# понимает значения. Вторая половина в том, что проба просто СТАРАЯ, и никакой
# строгостью этого не видно: старый ключ понят по-старому и молчит.
#
# Сторож свежести (`T226`) в дереве УЖЕ ЕСТЬ и накрывает любой `-Out`, но
# срабатывает при СБОРКЕ, а дыра была в ЗАПУСКЕ из каталога, который никто не
# пересобирал. Корпусный прогон этим накрыт (`run_appwd.ps1` зовёт
# `Invoke-AppWdGuard`), а запуск пробы прямо из `build_*` — нет. Этот файл и
# есть тот недостающий ЧИТАТЕЛЬ.
#
# ⛔ Своего списка «что чему обязано соответствовать» здесь НЕТ и быть не должно
#    (урок `T61`/`T57`): весь разбор берётся готовым из
#    `tools\CORPUS\scripts\appwd_plan.ps1`. Здесь только вызов и отказ.
#
# ⚠ ЧЕГО ЭТО НЕ ДАЁТ. Оно не мешает запустить exe напрямую — принудить к сверке
#   из PowerShell нечем. Оно даёт ОДНО движение, которым прогон и сверяется, и
#   запускается: сверку нельзя «забыть», не забыв заодно и про сам запуск.
#
# Коды возврата:
#   0…N — прогон состоялся, возвращается КОД САМОЙ ПРОБЫ (при -CheckOnly — 0);
#    70 — нет пробы по пути -Exe;
#    71 — нет сторожа appwd_plan.ps1, он не читается или сменил подпись;
#    72 — каталог не заверен и сборку не найти (сверять не с чем);
#    73 — ОТКАЗ: каталог разошёлся с исходниками.
# ⛔ Числа за 70 нарочно: коды 0…9 принадлежат пробе, и путать «проба отказала»
#    с «проба не запускалась» нельзя.
# ⛔ `PositionalBinding = $false` — НЕ УКРАШЕНИЕ, а тот же `A77` в себе самом.
# Без него PowerShell связывает лишние ключи ПО МЕСТУ: вызов
# `& run_fresh.ps1 -Exe <проба> --n=200` клал «--n=200» в `-Bin`, и сторож шёл
# искать «--n=200\BecquerelMonitor.exe». Поймано на ПЕРВОМ ЖЕ контроле
# 06.09.2026: отказ был кодом 72 и выглядел как «каталог не заверен», хотя на
# деле просто не доехал ключ — то есть ровно молчаливая подстановка, ради
# которой строка и заведена. С этой строкой неизвестный ключ — ОШИБКА
# СВЯЗЫВАНИЯ, а не тихая подмена.
# Ключи пробы передаются ТОЛЬКО через `-Extra`, как у `run_appwd.ps1` (`T84`).
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    # Сборка приложения, против которой собран каталог. Пусто — берётся из
    # отметки .appwd.json самого каталога: каталог сам знает, из чего собран,
    # и спрашивать это у запускающего значит дать ему ошибиться.
    [string]$Bin = '',
    [switch]$CheckOnly,
    [string[]]$Extra = @()
)
$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path

if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Host "НЕТ ПРОБЫ: $Exe" -ForegroundColor Red
    exit 70
}
$Exe = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Exe).Path)
$dir = Split-Path -Parent $Exe

$planFile = Join-Path $repo 'tools\CORPUS\scripts\appwd_plan.ps1'
if (-not (Test-Path -LiteralPath $planFile)) {
    Write-Host "НЕТ СТОРОЖА: $planFile - сверить каталог с исходниками нечем (A77)" -ForegroundColor Red
    exit 71
}
try { . $planFile } catch {
    Write-Host "СТОРОЖ НЕ ЧИТАЕТСЯ: $planFile" -ForegroundColor Red
    Write-Host ("  {0}" -f $_.Exception.Message) -ForegroundColor Red
    exit 71
}

# ⛔ КОНТРАКТ СО СТОРОЖЕМ ПРОВЕРЯЕТСЯ ПОИМЁННО (урок T83). Пропавшая функция в
# PowerShell — не ошибка, а $null, а $null.Bad.Count равен 0: сверка прошла бы
# «успешно», ничего не сверив. Ровно это и случилось 27.08.2026.
foreach ($fn in @('Read-AppWdStamp', 'Get-AppWdPlan', 'Test-AppWdBuild')) {
    if (-not (Get-Command $fn -CommandType Function -ErrorAction SilentlyContinue)) {
        Write-Host ("СТОРОЖ НЕ НЕСЁТ ФУНКЦИИ {0} - сверять нечем (T83)" -f $fn) -ForegroundColor Red
        exit 71
    }
}

$stamp = Read-AppWdStamp -Wd $dir
if (-not $Bin) {
    if ($stamp -and $stamp.PSObject.Properties['bin'] -and $stamp.bin) { $Bin = [string]$stamp.bin }
    else {
        Write-Host "КАТАЛОГ НЕ ЗАВЕРЕН И СБОРКА НЕ НАЗВАНА: $dir" -ForegroundColor Red
        Write-Host "  Нет отметки .appwd.json с полем bin, а -Bin не задан - сверять не с чем." -ForegroundColor Red
        Write-Host ("  Пересоберите: pwsh tools\effmaker\probes\build_all.ps1 -Bin <сборка> -Out {0}" -f $dir) -ForegroundColor Red
        exit 72
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $Bin 'BecquerelMonitor.exe'))) {
    Write-Host "НЕТ СБОРКИ: $Bin\BecquerelMonitor.exe - сверить каталог с ней нечем" -ForegroundColor Red
    exit 72
}

$sw = [Diagnostics.Stopwatch]::StartNew()
# Wd = ProbeBuild — как на самопроверочном стенде build_all.ps1: сверяется
# КАТАЛОГ ПРОБ, а не оснастка корпуса (у неё свой читатель — run_appwd.ps1).
$plan  = Get-AppWdPlan -Repo $repo -Bin $Bin -Wd $dir -ProbeBuild $dir -ProbeCatalog
# ⛔ БЕЗ -Certifying: именно сверка с ЗАВЕРЕННОЙ записью и есть весь смысл.
$build = Test-AppWdBuild -Plan $plan
$sw.Stop()

Write-Host ""
Write-Host "=== СВЕРКА ПЕРЕД ЗАПУСКОМ (A77) ===" -ForegroundColor Cyan
Write-Host ("  проба   : {0}" -f (Split-Path -Leaf $Exe))
Write-Host ("  каталог : {0}" -f $dir)
Write-Host ("  сборка  : {0}" -f $Bin)
if ($stamp -and $stamp.PSObject.Properties['sources'] -and $stamp.sources.PSObject.Properties['app']) {
    Write-Host ("  наборы  : приложение {0} ({1} файлов), пробы {2} ({3})" -f
                ([string]$stamp.sources.app.fp).Substring(0, 12), $stamp.sources.app.n,
                ([string]$stamp.sources.probes.fp).Substring(0, 12), $stamp.sources.probes.n)
} else {
    Write-Host "  ! каталог НЕ ЗАВЕРЕН записью набора - свежесть судится ВРЕМЕНЕМ (T41)" -ForegroundColor Yellow
}
Write-Host ("  сверено : {0} пар за {1} с" -f $plan.Pairs.Count, $sw.Elapsed.TotalSeconds.ToString('F2', [Globalization.CultureInfo]::InvariantCulture))
foreach ($x in @($build.Note)) { Write-Host ("  ! {0}" -f $x) -ForegroundColor DarkYellow }

$bad = @($build.Bad)
if ($bad.Count -gt 0) {
    Write-Host ""
    Write-Host "ОТКАЗ: КАТАЛОГ ПРОБ НЕ СООТВЕТСТВУЕТ ИСХОДНИКАМ - ПРОГОН НЕ ЗАПУСКАЕТСЯ" -ForegroundColor Red
    $i = 0
    foreach ($x in $bad) {
        $i++
        if ($i -gt 20) { Write-Host ("  ... и ещё {0}" -f ($bad.Count - 20)) -ForegroundColor Red; break }
        Write-Host ("  {0,2}. {1}" -f $i, $x) -ForegroundColor Red
    }
    Write-Host ""
    Write-Host ("  Порядок: собрать приложение -> pwsh tools\effmaker\probes\build_all.ps1 -Bin {0} -Out {1}" -f $Bin, $dir) -ForegroundColor Red
    Write-Host "  Отказавший сторож значит «собирать в ДРУГОЙ каталог», а не «положить файл мимо него»." -ForegroundColor Red
    Write-Host ""
    exit 73
}

Write-Host "  КАТАЛОГ СОШЁЛСЯ С ИСХОДНИКАМИ" -ForegroundColor Green
Write-Host ""
if ($CheckOnly) { exit 0 }

# Ключи пробы — только `-Extra` (см. шапку про `PositionalBinding`).
$argv = @(@($Extra) | Where-Object { $_ })
Write-Host ("=== {0} {1}" -f (Split-Path -Leaf $Exe), ($argv -join ' ')) -ForegroundColor Cyan
& $Exe @argv
$rc = $LASTEXITCODE
Write-Host ""
Write-Host ("=== проба вернула {0}" -f $rc)
exit $rc
