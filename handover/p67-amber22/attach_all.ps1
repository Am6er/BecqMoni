# П67: привязать все сцены, у которых уже есть .rmx, но ещё нет узла <Efficiency> в копиях спектров; затем снимки.
$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$p='D:\BqMoni_Claude\p67'
$rows = Import-Csv "$p\store\index.csv"
$done = @{}
foreach ($r in $rows) {
  $k = $r.geometry
  if ($done.ContainsKey($k)) { continue }
  if (-not (Test-Path "$p\store\$k.rmx")) { continue }
  $spec = "$p\spectra_scenes\$($r.spectrum).xml"
  if (Select-String -Path $spec -Pattern ("<Name>" + $k + "</Name>") -Quiet) { $done[$k] = $true; continue }
  pwsh -NoProfile -File "$p\attach.ps1" -Key $k | Select-String "разброс|guid|СОШЛИСЬ|ОТКАЗ|шумн|записей" | ForEach-Object { "$k : $($_.Line)" }
  $done[$k] = $true
}
pwsh -NoProfile -File "$p\run_shots.ps1" -Arms ($args -join ",") 2>&1 | Tee-Object -Append "$p\run_shots.log"
