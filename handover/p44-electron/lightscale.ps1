# П44 — `F11` (г): кривая света LaBr3:Ce / CeBr3 под ключом --ecomp=1 против ВЫКЛ (шкала пропорциональна)
# и против NaI:Tl (AS80) той же геометрией (голый Ø80×80, точка 5 мм). Энергии — с K-краями Br (13.47),
# La (38.92), Ce (40.44) и опорами Ходюка.
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$bin = "$root\tools\effmaker\probes\build_p44"
$scenes = 'D:\BqMoni_Claude\p44\scenes'
$out = 'D:\BqMoni_Claude\p44\light'
New-Item -ItemType Directory -Force $out | Out-Null
$E = '10,12,13,14,15,20,25,30,35,38,38.5,39,39.5,40,40.5,41,42,45,59.5,80,100,150,200,300,450,661.657,1000,1332.5'
Push-Location $bin
foreach ($g in 'LaBr3_bare_gap5', 'CeBr3_bare_gap5') {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & "$bin\LightScaleProbe.exe" "--geometry=$scenes\$g.in" --n=200000 "--energies=$E" --ecomp=1 "--curve=$out\curve_$g.txt" *> "$out\ls_$($g)_ecomp1.txt"
    "$g ecomp1 code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s" | Out-File -Append "$out\codes.txt"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & "$bin\LightScaleProbe.exe" "--geometry=$scenes\$g.in" --n=200000 "--energies=$E" --ecomp=0 *> "$out\ls_$($g)_ecomp0.txt"
    "$g ecomp0 code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s" | Out-File -Append "$out\codes.txt"
}
# NaI той же геометрией — контроль «числа разумны против NaI»; ключ NaI не трогает (таблица NIST есть).
$sw = [Diagnostics.Stopwatch]::StartNew()
& "$bin\LightScaleProbe.exe" "--geometry=$scenes\AS80_bare_gap5.in" --n=200000 "--energies=$E" --ecomp=1 "--curve=$out\curve_AS80_bare_gap5.txt" *> "$out\ls_AS80_bare_gap5_ecomp1.txt"
"AS80 ecomp1 code=$LASTEXITCODE t=$([int]$sw.Elapsed.TotalSeconds)s" | Out-File -Append "$out\codes.txt"
& "$bin\LightScaleProbe.exe" "--geometry=$scenes\AS80_bare_gap5.in" --n=200000 "--energies=$E" --ecomp=0 *> "$out\ls_AS80_bare_gap5_ecomp0.txt"
"AS80 ecomp0 code=$LASTEXITCODE" | Out-File -Append "$out\codes.txt"
Pop-Location
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append "$out\codes.txt"
