# П67: кривая + привязка матрицы к по-сценным копиям спектров для ОДНОЙ сцены (CorpusEffProbe --only), затем матрица
# копируется в оснастку wd\config\device\response.  pwsh -NoProfile -File D:\BqMoni_Claude\p67\attach.ps1 -Key <ключ сцены>
param([Parameter(Mandatory=$true)][string]$Key)
$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$p='D:\BqMoni_Claude\p67'; $b="$p\wt\tools\effmaker\probes\build_p67"
Push-Location $b
& .\CorpusEffProbe.exe --dir=$p\store --spectra=$p\spectra_scenes --only=$Key 2>&1 | Tee-Object -Append "$p\eff.log"
$code = $LASTEXITCODE
Pop-Location
"eff $Key code=$code $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$p\eff_codes.log"
$rsp = "$p\wd\config\device\response"
New-Item -ItemType Directory -Force $rsp | Out-Null
Copy-Item "$p\store\response\*.rmx" $rsp -Force
exit $code
