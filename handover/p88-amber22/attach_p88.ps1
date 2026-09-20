# П88: кривая + привязка матрицы к по-сценным копиям спектров для ОДНОЙ сцены (CorpusEffProbe --only), затем матрица
# копируется в оснастку wd\config\device\response. Без аргументов — все сцены описи, у которых есть .rmx и ещё нет узла
# <Efficiency> хотя бы в одной копии.   pwsh -NoProfile -File D:\BqMoni_Claude\p88\attach_p88.ps1 [-Key <ключ сцены>]
param([string]$Key = '')
$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$p = 'D:\BqMoni_Claude\p88'; $b = "$p\wt\tools\effmaker\probes\build_p88"
$rows = Import-Csv "$p\store\index.csv"
$keys = @()
if ($Key) { $keys = @($Key) }
else {
  $seen = @{}
  foreach ($r in $rows) {
    $k = $r.geometry
    if ($seen.ContainsKey($k)) { continue }
    $seen[$k] = $true
    if (-not (Test-Path "$p\store\$k.rmx")) { "$k : нет .rmx — пропуск"; continue }
    $need = $false
    foreach ($rr in $rows | Where-Object { $_.geometry -eq $k }) {
      $spec = "$p\spectra_scenes\$($rr.spectrum).xml"
      if (-not (Select-String -Path $spec -Pattern ("<Name>" + $k + "</Name>") -Quiet)) { $need = $true }
    }
    if ($need) { $keys += $k }
  }
}
Push-Location $b
$codes = @()
foreach ($k in $keys) {
  & .\CorpusEffProbe.exe --dir=$p\store --spectra=$p\spectra_scenes --only=$k 2>&1 | Tee-Object -Append "$p\logs\eff.log" | Select-String "разброс|guid|СОШЛИСЬ|ОТКАЗ|шумн|записей" | ForEach-Object { "$k : $($_.Line)" }
  $codes += "$k=$LASTEXITCODE"
  "eff $k code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\logs\eff_codes.log"
}
Pop-Location
$rsp = "$p\wd\config\device\response"
New-Item -ItemType Directory -Force $rsp | Out-Null
Copy-Item "$p\store\response\*.rmx" $rsp -Force
"коды: " + ($codes -join ' ')
$bad = @($codes | Where-Object { $_ -notlike '*=0' })
if ($bad.Count -gt 0) { "НЕ НУЛЕВЫЕ: $($bad -join ' ')"; exit 1 }
exit 0
