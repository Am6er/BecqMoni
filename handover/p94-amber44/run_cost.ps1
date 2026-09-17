# П94: цена ключа по МАТРИЦЕ — CorpusMatrixProbe на копиях сцен, ВЫКЛ и ВКЛ подряд, одни потоки, тихая машина.
param([string]$Only = 'RC103_point0', [string]$Extra = '', [int]$Threads = 8)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'D:\BqMoni_Claude\p94'
$probe = "$root\wt\tools\effmaker\probes\build_p94\CorpusMatrixProbe.exe"
$codes = "$root\codes_cost.txt"
$extraKeys = @(); if ($Extra.Trim() -ne '') { $extraKeys = $Extra.Trim().Split(' ') }
foreach ($side in @('off', 'on')) {
    $keys = @(); if ($side -eq 'on') { $keys = @('--eltr=1') }
    $t0 = Get-Date
    & $probe "--dir=$root\cost\$side" "--only=$Only" "--threads=$Threads" --target=0 --force @keys @extraKeys 2>&1 |
        Out-File -Encoding utf8 "$root\cost\matrix_${side}_$Only.txt"
    "$side $Only code=$LASTEXITCODE keys=[$($keys -join ' ') $($extraKeys -join ' ')] threads=$Threads $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
}
"done $Only $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
