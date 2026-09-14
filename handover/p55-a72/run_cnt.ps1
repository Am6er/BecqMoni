# П55: счётчики переноса электрона (сборка cnt = HEAD + рычаг posend ВЫКЛ + счётчики; физика та же).
# Печать «П55 перенос: …» в txt — доля историй с вылетом e- через грань, унесённая энергия.
$ErrorActionPreference = 'Continue'
$probe = "D:\BqMoni_Claude\p55\wt\tools\effmaker\probes\build_p55_cnt\G4RawProbe.exe"
$geo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$out = 'D:\BqMoni_Claude\p55\cnt'
New-Item -ItemType Directory -Force $out | Out-Null
$codes = "$out\codes.txt"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
foreach ($g in @('RC103_point0', 'ASN16_lu_side', 'AS80_point0', 'AS80_th_disk')) {
    foreach ($e in @(59.541, 661.657, 1460.82, 2614.511)) {
        $name = "${g}_${e}"
        $t0 = Get-Date
        & $probe "--geometry=$geo\$g.in" "--energy=$e" "--n=4000000" --no-light --bin=1 "--out=$out\cnt_$name.csv" 2>&1 | Out-File -Encoding utf8 "$out\cnt_$name.txt"
        "cnt $name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
    }
}
"done cnt $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
