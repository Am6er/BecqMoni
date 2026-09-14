# П55 (A72 оценка): наша сторона — G4RawProbe (сырой отклик, --no-light, бин 1 кэВ), умолчания
# физики 18 (склад), сборка worktree HEAD 343d17a6 -> build_p55_ref. Те же сцены/энергии, что у арбитра.
# -Tag ref: эталон; -Tag var: сборка с рычагом --posend=1 (п. 4) — только узлы с парами.
param([string]$Tag = 'ref')
$ErrorActionPreference = 'Continue'
$probe = "D:\BqMoni_Claude\p55\wt\tools\effmaker\probes\build_p55_$Tag\G4RawProbe.exe"
$geo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$out = 'D:\BqMoni_Claude\p55\ours'
New-Item -ItemType Directory -Force $out | Out-Null
$codes = "$out\codes_$Tag.txt"
if ($Tag -eq 'ref') {
    $runs = @(
        @{ g = 'RC103_point0';  e = 1460.82;  n = 40000000; k = @() },
        @{ g = 'ASN16_lu_side'; e = 1460.82;  n = 8000000;  k = @() },
        @{ g = 'AS80_point0';   e = 1460.82;  n = 4000000;  k = @() },
        @{ g = 'AS80_th_disk';  e = 2614.511; n = 8000000;  k = @() },
        @{ g = 'RC103_point0';  e = 661.657;  n = 40000000; k = @() },
        @{ g = 'ASN16_lu_side'; e = 661.657;  n = 8000000;  k = @() },
        @{ g = 'AS80_point0';   e = 661.657;  n = 4000000;  k = @() },
        @{ g = 'RC103_point0';  e = 59.541;   n = 40000000; k = @() },
        @{ g = 'ASN16_lu_side'; e = 59.541;   n = 8000000;  k = @() },
        @{ g = 'RC103_point0';  e = 2614.511; n = 40000000; k = @() }
    )
} else {
    $runs = @(
        @{ g = 'AS80_th_disk';  e = 2614.511; n = 8000000;  k = @('--posend=0') },
        @{ g = 'AS80_th_disk';  e = 2614.511; n = 8000000;  k = @('--posend=1') },
        @{ g = 'ASN16_lu_side'; e = 2614.511; n = 8000000;  k = @('--posend=0') },
        @{ g = 'ASN16_lu_side'; e = 2614.511; n = 8000000;  k = @('--posend=1') },
        @{ g = 'RC103_point0';  e = 2614.511; n = 40000000; k = @('--posend=0') },
        @{ g = 'RC103_point0';  e = 2614.511; n = 40000000; k = @('--posend=1') },
        @{ g = 'ASN16_lu_side'; e = 1460.82;  n = 8000000;  k = @('--posend=1') }
    )
}
foreach ($r in $runs) {
    $kt = ($r.k -join '') -replace '[-=]', ''
    $tag = "$($r.g)_$($r.e)_$Tag$kt"
    $t0 = Get-Date
    & $probe "--geometry=$geo\$($r.g).in" "--energy=$($r.e)" "--n=$($r.n)" --no-light --bin=1 @($r.k) "--out=$out\ours_$tag.csv" 2>&1 | Out-File -Encoding utf8 "$out\ours_$tag.txt"
    "ours $tag code=$LASTEXITCODE n=$($r.n) $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
}
"done ours $Tag $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
