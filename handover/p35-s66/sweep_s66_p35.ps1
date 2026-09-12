# П35 (`S66`): контроль снятия якоря — состав «Из NucBase» на всём корпусе ДВУМЯ плечами
# ОДНИМ двоичным файлом (образец — handover/p30-cleaner-s/sweep_s66.ps1):
#   base      — умолчание приложения (якоря больше нет, новизна вкл);
#   nonovel   — --no-novel (положительный контроль: сравнение обязано увидеть 6 фантомов у 5 спектров).
# Формат вывода тот же, что у П30, — сравнивается s66_diff.py против sweep_s66-{base,noanchor,nonovel}.txt П30.
param(
  [string]$ProbeDir = 'tools\effmaker\probes\build_p35',
  [string]$Out      = 'handover\p35-s66'
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
[Environment]::CurrentDirectory = $repo
$probe = Join-Path $repo "$ProbeDir\FsaInferProbeF51.exe"
$spectra = Get-ChildItem (Join-Path $repo 'tools\CORPUS\corpus\spectra\*.xml') | Sort-Object Name
$t0 = Get-Date
foreach ($arm in @('base','nonovel')) {
  $lines = New-Object System.Collections.Generic.List[string]
  foreach ($s in $spectra) {
    $argv = @("--spectrum=$($s.FullName)")
    if ($arm -eq 'nonovel')  { $argv += '--no-novel' }
    Push-Location (Join-Path $repo $ProbeDir)
    $text = & $probe @argv 2>&1
    $rc = $LASTEXITCODE
    Pop-Location
    $spec = @($text | Select-String -Pattern '^SPEC\t(ряд|одиночка)\t' | ForEach-Object { $_.Line })
    $size = @($text | Select-String -Pattern '^LIBSIZE\t' | ForEach-Object { ($_.Line -split "`t")[1] })
    $rep  = @($text | Select-String -Pattern '^REPORT\t' | ForEach-Object { $_.Line })
    $rule = @($text | Select-String -Pattern '^правило обрыва ряда' | ForEach-Object { $_.Line })
    $lines.Add("### $($s.BaseName)")
    $lines.Add("EXIT=$rc")
    foreach ($r in $rule) { $lines.Add($r) }
    $lines.Add("LIBSIZE=" + $(if ($size.Count) { $size[0] } else { '?' }))
    foreach ($r in $rep) { $lines.Add($r) }
    if ($spec.Count -eq 0) { $lines.Add("SPEC`t(пусто)") }
    foreach ($l in $spec) { $lines.Add($l) }
  }
  $path = Join-Path $repo "$Out\sweep_s66-p35-$arm.txt"
  Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
  "написано: $path (" + $lines.Count + " строк, " + [int]((Get-Date)-$t0).TotalSeconds + " с)"
}
# Отрицательный контроль ключа: `--no-anchor` обязан быть отвергнут кодом 2 («неизвестный ключ»).
Push-Location (Join-Path $repo $ProbeDir)
$first = $spectra[0].FullName
$null = & $probe "--spectrum=$first" '--no-anchor' 2>&1
"контроль ключа: --no-anchor -> код $LASTEXITCODE (ожидается 2)"
Pop-Location
