# П75 (S175): рабочие каталоги снимков от каталога проб (-Build) с суффиксом (-Suffix):
#   wd_main<Suffix>  = пробы + config Amber (копия amber_debug\config: склад, приборы, библиотека) + копия AS80_th_disk.rmx;
#   wd_radon<Suffix> = пробы + корпусные приборы ASN16/AS80 (HEAD) + матрица ASN16_rn_side полосы + AS80_th_disk; NuclideDefinition.xml снят.
#   pwsh -File D:\BqMoni_Claude\p75\mk_wd.ps1 -Build <каталог проб> [-Suffix _a]
param([string]$Build = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\effmaker\probes\build_p75', [string]$Suffix = '')
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$lane = 'D:\BqMoni_Claude\p75'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$as80rmx = "$repo\tools\CORPUS\corpus\geometries\response\c2b5212c-6b5a-50d8-7870-0b0c3104daa2.rmx"

$wd = "$lane\wd_main$Suffix"
robocopy $Build $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { "robocopy build код $LASTEXITCODE"; exit 4 }
robocopy "$lane\amber_debug\config" "$wd\config" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { "robocopy config код $LASTEXITCODE"; exit 4 }
Copy-Item $as80rmx "$wd\config\device\response\" -Force
"wd_main${Suffix}: матриц $((Get-ChildItem "$wd\config\device\response\*.rmx").Count), приборов $((Get-ChildItem "$wd\config\device\*.xml").Count), exe $((Get-FileHash "$wd\BecquerelMonitor.exe" -Algorithm SHA256).Hash.Substring(0,16))"

$wr = "$lane\wd_radon$Suffix"
robocopy $Build $wr /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { "robocopy build (radon) код $LASTEXITCODE"; exit 4 }
$dev = "$wr\config\device"
Get-ChildItem "$dev\*.xml" -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
Copy-Item "$repo\tools\CORPUS\corpus\devices\1.Atom Spectra Nano 16 Pro RadiaScan 701A.xml" $dev
Copy-Item "$repo\tools\CORPUS\corpus\devices\Atom Spectra 80x80.xml" $dev
if (Test-Path "$wr\config\NuclideDefinition.xml") { Remove-Item "$wr\config\NuclideDefinition.xml" -Force }
New-Item -ItemType Directory -Force "$dev\response" | Out-Null
Get-ChildItem "$dev\response\*.rmx" -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
if (Test-Path "$lane\radon\store\response") { Copy-Item "$lane\radon\store\response\*.rmx" "$dev\response\" }
Copy-Item $as80rmx "$dev\response\" -Force
"wd_radon${Suffix}: матриц $((Get-ChildItem "$dev\response\*.rmx").Count), приборов $((Get-ChildItem "$dev\*.xml").Count)"
exit 0
