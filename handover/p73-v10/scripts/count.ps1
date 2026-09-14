# П73 (V10): матрицы полосы умолчаниями физики 18 (как живой склад: 140 узлов 5-3000 кэВ, бин 2, 3 000 000 историй, --target=0).
#   pwsh -NoProfile -File D:\BqMoni_Claude\p73\count.ps1
$env:OS = 'Windows_NT'
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$p = 'D:\BqMoni_Claude\p73'
$b = "$p\wt\tools\effmaker\probes\build_p73"
Set-Location $b
$codes = @()
foreach ($key in @('RC103_point50', 'ASN16_point10')) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  "== $key  $(Get-Date -Format 'HH:mm:ss')"
  & .\CorpusMatrixProbe.exe --dir="$p\store" --only=$key --threads=10 --target=0 > "$p\count_$key.log" 2>&1
  $c = $LASTEXITCODE
  $codes += "$key=$c"
  "   код $c, $($sw.Elapsed.TotalSeconds.ToString('F0')) с  $(Get-Date -Format 'HH:mm:ss')"
  Get-Content "$p\count_$key.log" | Select-String 'клеймо|время|счёт|шум конт|файл' | ForEach-Object { '   ' + $_.Line }
}
"коды: $($codes -join ' ')"
$codes | Set-Content "$p\count_codes.txt"
