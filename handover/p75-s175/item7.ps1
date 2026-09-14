# П75 (S175, п. 7 — мера, не правка): сколько сплайна разносится по слоям правилом S76 ниже и выше порога доверия
# матрицы (100 кэВ) на корпусных спектрах с матрицей — уголь G1S24 (сцена G1S_mar1l_coal_046_p24 живого склада) и
# Co-57 G1S16 (хвост с плечом уширения выше порога). Каталог: пробы build_p75 + config оснастки малой базы wt_b
# (корпусные приборы + матрицы живого склада, только чтение). Cs-137 в домике и эталон AS80 — в accept (SPREAD).
#   pwsh -File D:\BqMoni_Claude\p75\item7.ps1
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$lane = 'D:\BqMoni_Claude\p75'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$build = "$repo\tools\effmaker\probes\build_p75"
$out = "$lane\out\item7"
New-Item -ItemType Directory -Force $out | Out-Null
$wd = "$lane\wd_corpus"
robocopy $build $wd /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { "robocopy build код $LASTEXITCODE"; exit 4 }
robocopy "$lane\wt_b\tools\CORPUS\scripts\wd_p75b\config" "$wd\config" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -gt 7) { "robocopy config код $LASTEXITCODE"; exit 4 }
if (Test-Path "$wd\config\NuclideDefinition.xml") { Remove-Item "$wd\config\NuclideDefinition.xml" -Force }
"wd_corpus: матриц $((Get-ChildItem "$wd\config\device\response\*.rmx").Count), приборов $((Get-ChildItem "$wd\config\device\*.xml").Count)"
$codes = @()
function Shot([string]$key, [string]$spectrum, [string[]]$extra) {
    Push-Location $wd
    $a = @("--spectrum=$spectrum") + $extra + @("--out=$out\$key.png", "--rates=$out\rates_$key.csv", "--dump=$out\curves_$key.csv", '--screen', '--scale=pow', '--width=1600')
    & .\FsaStackShot.exe @a > "$out\$key.log" 2>&1
    $c = $LASTEXITCODE
    Pop-Location
    $script:codes += "$key=$c"
    "--- $key (код $c)"
    Select-String -Path "$out\$key.log" -Pattern '^(chi2/ndf|ROW|отвязанный хвост|хвост в слоях|серый слой|разнос сплайна|SPREAD|матриц)' | ForEach-Object { $_.Line.Substring(0, [Math]::Min(260, $_.Line.Length)) }
}
Shot 'coal_eq01' "$repo\tools\CORPUS\corpus\spectra\G1S24_Rn222Coal_Mar_eq01.xml" @('--chain=Ra-226,Th-228')
Shot 'co57_p5'   "$repo\tools\CORPUS\corpus\spectra\G1S16_Co57_P5.xml" @('--sample=57CO')
Shot 'cs137_0cm' "$repo\tools\CORPUS\corpus\spectra\AS80_Cs137_0cm.xml" @('--sample=137CS')
"коды: " + ($codes -join ' ')
exit 0
