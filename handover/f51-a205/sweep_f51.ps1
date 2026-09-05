# Полоса F51 (`A205`): состав, ВЫВЕДЕННЫЙ из пиков, на всём корпусе — двумя
# плечами ОДНИМ двоичным файлом.
#
#   --cut=whole  — правило до 06.09.2026 (обрыв не ищется);
#   без ключа    — умолчание приложения после правки (подцепочка, `Only`).
#
# ⛔ Плечи считаются одним файлом НАРОЧНО: ветка `Whole` правкой не тронута, и
#    сравнение двух сборок мерило бы ещё и всё, что между ними изменилось.
param(
  [string]$ProbeDir = 'tools\effmaker\probes\build_f51',
  [string]$Out      = 'handover\f51-a205'
)
$ErrorActionPreference = 'Stop'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
[Environment]::CurrentDirectory = $repo
$probe = Join-Path $repo "$ProbeDir\FsaInferProbeF51.exe"
$spectra = Get-ChildItem (Join-Path $repo 'tools\CORPUS\corpus\spectra\*.xml') | Sort-Object Name

foreach ($arm in @('whole','after')) {
  $lines = New-Object System.Collections.Generic.List[string]
  foreach ($s in $spectra) {
    $argv = @("--spectrum=$($s.FullName)")
    if ($arm -eq 'whole') { $argv += '--cut=whole' }
    Push-Location (Join-Path $repo $ProbeDir)
    $text = & $probe @argv 2>&1
    Pop-Location
    $spec = @($text | Select-String -Pattern '^SPEC\t(ряд|одиночка)\t' | ForEach-Object { $_.Line })
    $size = @($text | Select-String -Pattern '^LIBSIZE\t' | ForEach-Object { ($_.Line -split "`t")[1] })
    $rep  = @($text | Select-String -Pattern '^REPORT\t' | ForEach-Object { $_.Line })
    $lines.Add("### $($s.BaseName)")
    $lines.Add("LIBSIZE=" + ($(if ($size.Count) { $size[0] } else { '?' })))
    foreach ($r in $rep) { $lines.Add($r) }
    if ($spec.Count -eq 0) { $lines.Add("SPEC`t(пусто)") }
    foreach ($l in $spec) { $lines.Add($l) }
  }
  $path = Join-Path $repo "$Out\sweep-$arm.txt"
  Set-Content -LiteralPath $path -Value $lines -Encoding UTF8
  "написано: $path (" + $lines.Count + " строк)"
}
