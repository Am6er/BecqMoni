# П67: привязать сцены с готовыми .rmx (CorpusEffProbe), сделать .escale.xml, прогнать снимки (плечи из аргументов).
#   pwsh -NoProfile -File D:\BqMoni_Claude\p67\pipeline.ps1 eq asis
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
  pwsh -NoProfile -File "$p\attach.ps1" -Key $k | Select-String "guid|СОШЛИСЬ|ОТКАЗ|шумн|записей" | ForEach-Object { "$k : $($_.Line)" }
  $done[$k] = $true
}
$env:PYTHONIOENCODING = 'utf-8'
python "$p\py\mk_escale.py" "$p\spectra_scenes" 2>&1 | Where-Object { $_ -notmatch 'Warning' }
pwsh -NoProfile -File "$p\run_shots.ps1" -Arms ($args -join ',') 2>&1 | Tee-Object -Append "$p\run_shots.log"
