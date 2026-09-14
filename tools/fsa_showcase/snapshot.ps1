# Переобъявить ЭТАЛОН витрины FSA (`T260`) — `tools\fsa_showcase\reference\*.json`.
#
# Это ОСОЗНАННОЕ действие приёмки правки отображения: скрипт гонит `FsaStackShot` на
# всех парах витрины из СВЕЖЕГО каталога проб (сторож свежести `T226` обязателен —
# со `--skip-freshness` эталон git не пишется), печатает diff числами против прежнего
# эталона по каждой паре («спектр — режим — полоса — компонент — было — стало — Δ»)
# и переписывает эталон. Таблица diff — в отчёт полосы (SKILL `todo-work` §6).
#
#   & 'tools\fsa_showcase\snapshot.ps1' [-Probes <каталог проб>] [-Only <ключ[,ключ]>]
#
# ⛔ Оператором вызова `&`, не `pwsh <файл>` (`T84`/`T91`).
# Умолчание каталога проб — `tools\effmaker\probes\build` (штатный `-Out` `build_all.ps1`),
# собранный из ЭТОГО дерева; иначе сторож откажет кодом 3 и ничего не перепишет.
# Сама логика — в `tools\check_fsa_showcase.py --snapshot`; здесь только обёртка,
# чтобы переобъявление было отдельной командой, а не ключом, который забудут снять.
param(
    [string]$Probes = '',
    [string]$Only = ''
)
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = 'utf-8'
$env:PYTHONUTF8 = '1'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$guard = Join-Path $repo 'tools\check_fsa_showcase.py'
$argv = @($guard, '--snapshot')
if ($Probes) { $argv += ('--probes=' + $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Probes)) }
if ($Only) { $argv += ('--only=' + $Only) }
Write-Host ('→ python ' + ($argv -join ' '))
& python @argv
$code = $LASTEXITCODE
if ($code -eq 0) {
    Write-Host 'эталон переобъявлен — diff выше в отчёт полосы; файлы reference\*.json — в коммит' -ForegroundColor Green
} else {
    Write-Host ("эталон НЕ переобъявлен: код {0}" -f $code) -ForegroundColor Red
}
exit $code
