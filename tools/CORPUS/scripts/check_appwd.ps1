# Сторож оснастки корпусного прогона отдельной командой (`T63`).
#
#   pwsh tools/CORPUS/scripts/check_appwd.ps1 [-Bin <сборка>] [-Wd <оснастка>] [-ProbeBuild <пробы>]
#
# Коды возврата: 0 — оснастка сошлась с источниками по sha256, по временам
# сборок и по числу записей библиотеки нуклидов; 6 — план оснастки не строится
# вовсе (нет каталога проб, нет `CorpusFsaProbe.exe`); любое другое число —
# сколько нашлось отказных расхождений. Ничего не чинит: чинит `mk_appwd.ps1`,
# а запуск прогона держит `run_appwd.ps1`.
#
# Весь разбор — в `appwd_plan.ps1`: там же лежит и список «что откуда кладётся»,
# по которому оснастку СОБИРАЮТ. Двух списков нет нарочно (урок `T61`).
param(
    [string]$Bin = '',
    [string]$Wd  = '',
    [string]$ProbeBuild = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'appwd_plan.ps1')

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $Wd) { $Wd = Join-Path $PSScriptRoot 'wd_app' }
# Из какой сборки оснастку собирали, знает её собственная отметка: иначе
# оснастку из `bin\Release_Codex` сторож сверял бы с `bin\Debug_Codex`
# и отказывал бы на ровном месте. Отметки нет — оснастка либо не собиралась,
# либо самопроверка при сборке не прошла: тогда умолчания, и сторож откажет.
$st = Read-AppWdStamp -Wd $Wd
if (-not $Bin) {
    if ($st -and $st.bin) { $Bin = [string]$st.bin }
    else { $Bin = Join-Path $repo 'BecquerelMonitor\bin\Debug_Codex' }
}
if (-not $ProbeBuild -and $st -and $st.probes) { $ProbeBuild = [string]$st.probes }

$plan = New-AppWdPlanOrDie -Repo $repo -Bin $Bin -Wd $Wd -ProbeBuild $ProbeBuild
exit (Invoke-AppWdGuard -Plan $plan)
