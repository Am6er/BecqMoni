# П73 (V10): полноспектральный разбор FsaStackShot --sample=137CS на четырёх спектрах стенда (матрица сцены из wd\config\device\response).
#   pwsh -NoProfile -File D:\BqMoni_Claude\p73\run_fsa.ps1 [-Extra '--no-anchor']
# Выход — D:\BqMoni_Claude\p73\out_fsa\<спектр>[__<метка>]\: rates.csv, dump.csv, stack.png, probe.txt.
param([string[]]$Extra = @(), [string]$Tag = '')
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$p = 'D:\BqMoni_Claude\p73'
$out = "$p\out_fsa"
New-Item -ItemType Directory -Force $out | Out-Null
Push-Location "$p\wd"
$codes = @(); $sw = [Diagnostics.Stopwatch]::StartNew()
foreach ($s in @('RC103_Cs137_50mm', 'RC103_Cs137_50mm_recal', 'RC103_Cs137_0cm', 'ASN16_Cs137_10cm')) {
  $k = if ($Tag) { "${s}__$Tag" } else { $s }
  $d = "$out\$k"
  New-Item -ItemType Directory -Force $d | Out-Null
  $a = @("--spectrum=$p\spectra\$s.xml", '--sample=137CS', "--rates=$d\rates.csv", '--screen', "--out=$d\stack.png", "--dump=$d\dump.csv", '--scale=pow', '--from=15', '--to=2800', '--width=1400') + $Extra
  & .\FsaStackShot.exe @a > "$d\probe.txt" 2>&1
  $code = $LASTEXITCODE
  $codes += "$k=$code"
  $chi = (Select-String -Path "$d\probe.txt" -Pattern '^chi2/ndf' | Select-Object -First 1).Line
  "{0,-40} код {1}  {2,5:F0} с  {3}" -f $k, $code, $sw.Elapsed.TotalSeconds, $chi
}
Pop-Location
"коды: $($codes -join ' ')"
$codes | Add-Content "$out\codes.txt"
