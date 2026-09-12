# П30 (`S66`): состав «Из NucBase» на всём корпусе ТРЕМЯ плечами ОДНИМ двоичным файлом
# (образец — handover/f51-a205/sweep_f51.ps1):
#   base      — умолчание приложения (якоря вкл, новизна вкл);
#   noanchor  — --no-anchor;
#   nonovel   — --no-novel.
param(
  [string]$ProbeDir = 'tools\effmaker\probes\build_p30',
  [string]$Out      = 'handover\p30-cleaner-s'
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$repo = 'C:\Users\moroz\bqp30'
[Environment]::CurrentDirectory = $repo
$probe = Join-Path $repo "$ProbeDir\FsaInferProbeF51.exe"
$spectra = Get-ChildItem (Join-Path $repo 'tools\CORPUS\corpus\spectra\*.xml') | Sort-Object Name
$t0 = Get-Date
foreach ($arm in @('base','noanchor','nonovel')) {
  $lines = New-Object System.Collections.Generic.List[string]
  foreach ($s in $spectra) {
    $argv = @("--spectrum=$($s.FullName)")
    if ($arm -eq 'noanchor') { $argv += '--no-anchor' }
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
  $path = Join-Path $repo "$Out\sweep_s66-$arm.txt"
  Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
  "написано: $path (" + $lines.Count + " строк, " + [int]((Get-Date)-$t0).TotalSeconds + " с)"
}
