# П55: п. 4 — тонкий CsI ASN16_lu_side при 2614, 40 млн историй, posend 0/1 (шум SE ~1.5 %).
$ErrorActionPreference = 'Continue'
$probe = "D:\BqMoni_Claude\p55\wt\tools\effmaker\probes\build_p55_var\G4RawProbe.exe"
$geo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$out = 'D:\BqMoni_Claude\p55\ours'
$codes = "$out\codes_var40.txt"
foreach ($k in @(0, 1)) {
    $name = "ASN16_lu_side_2614.511_var40posend$k"
    $t0 = Get-Date
    & $probe "--geometry=$geo\ASN16_lu_side.in" "--energy=2614.511" "--n=40000000" --no-light --bin=1 "--posend=$k" "--out=$out\ours_$name.csv" 2>&1 | Out-File -Encoding utf8 "$out\ours_$name.txt"
    "var40 $name code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
}
"done var40 $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
