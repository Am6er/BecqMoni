# П70: пересборка каталога проб основного дерева (build_all, A77) после правки договора S174 в FsaDoubleCountProbe
# и повтор проб в wd_main/wd_radon (каталоги пересобираются от build_p70 тем же движением, что accept_main.ps1).
$ErrorActionPreference = 'Continue'
if (-not $env:OS) { $env:OS = 'Windows_NT' }
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$lane = 'D:\BqMoni_Claude\p70'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$build = "$repo\tools\effmaker\probes\build_p70"
$out = "$lane\out\main"
Set-Location $repo
& pwsh -NoProfile -File "$repo\tools\effmaker\probes\build_all.ps1" -Bin "$repo\BecquerelMonitor\bin\Release_p70" -Out $build *> "$lane\logs\build_all_rerun.log"
"build_all код $LASTEXITCODE"
if ($LASTEXITCODE -ne 0) { exit 1 }
foreach ($w in 'wd_main', 'wd_radon') {
    Copy-Item "$build\FsaDoubleCountProbe.exe" "$lane\$w\" -Force
    Copy-Item "$build\FsaStackShot.exe" "$lane\$w\" -Force
}
$cs = "$lane\Cs 137 в домике 24.11.2022.xml"
$as80 = "$repo\tools\CORPUS\corpus\spectra\AS80_Th232Medal.xml"
$radon2 = "$lane\radon\spectra\radon2_side.xml"
$codes = @()
Push-Location "$lane\wd_main"
& .\FsaDoubleCountProbe.exe "--spectrum=$as80" --chain=Th-232 *> "$out\dc_as80amb.log"; $codes += "dc_as80amb=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$cs" --sample=137CS *> "$out\dc_cs.log";        $codes += "dc_cs=$LASTEXITCODE"
Pop-Location
Push-Location "$lane\wd_radon"
& .\FsaDoubleCountProbe.exe "--spectrum=$as80" --chain=Th-232 *> "$out\dc_as80.log";              $codes += "dc_as80=$LASTEXITCODE"
& .\FsaDoubleCountProbe.exe "--spectrum=$radon2" --chain=Ra-226,Th-232 *> "$out\dc_radon2.log";   $codes += "dc_radon2=$LASTEXITCODE"
Pop-Location
foreach ($f in 'dc_as80amb', 'dc_cs', 'dc_as80', 'dc_radon2') {
    $n = (Select-String -Path "$out\$f.log" -Pattern '!!').Count
    $s174 = (Select-String -Path "$out\$f.log" -Pattern 'договор S174').Count
    "{0,-12} красных {1}, отказов S174 {2}, итог: {3}" -f $f, $n, $s174, (Select-String -Path "$out\$f.log" -Pattern 'ВСЕ СОШЛИСЬ|НЕ СОШЛОСЬ' | Select-Object -Last 1).Line
}
"коды: " + ($codes -join ' ')
