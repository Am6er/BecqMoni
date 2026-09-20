# П94: побитовость ВЫКЛ — матрица отклика на копиях сцен склада двумя сборками (stand = HEAD f744dbed + wt.diff пробы;
# new = правка A+B+ключ eltr, ключ ВЫКЛ умолчанием) → MatrixDiffProbe попарно и против копии живого склада.
# ⛔ --dir — ТОЛЬКО копии (bitwise\stand, bitwise\new); живой склад tools\CORPUS\corpus\geometries не трогается.
param([string]$Only = 'RC103_point0', [string]$Extra = '', [int]$Threads = 8)
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'D:\BqMoni_Claude\p94'
$codes = "$root\codes_bitwise.txt"
$extraKeys = @()
if ($Extra.Trim() -ne '') { $extraKeys = $Extra.Trim().Split(' ') }
foreach ($side in @('stand', 'new')) {
    $probe = if ($side -eq 'stand') { "$root\build_stand\CorpusMatrixProbe.exe" } else { "$root\wt\tools\effmaker\probes\build_p94\CorpusMatrixProbe.exe" }
    $t0 = Get-Date
    & $probe "--dir=$root\bitwise\$side" "--only=$Only" "--threads=$Threads" --target=0 --force @extraKeys 2>&1 |
        Out-File -Encoding utf8 "$root\bitwise\matrix_${side}_$Only.txt"
    "$side $Only code=$LASTEXITCODE keys=[$($extraKeys -join ' ')] $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
}
$diff = "$root\wt\tools\effmaker\probes\build_p94\MatrixDiffProbe.exe"
& $diff "--a=$root\bitwise\stand\$Only.rmx" "--b=$root\bitwise\new\$Only.rmx" 2>&1 | Out-File -Encoding utf8 "$root\bitwise\diff_stand_new_$Only.txt"
"diff stand/new $Only code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
if ($extraKeys.Count -eq 0) {
    & $diff "--a=$root\bitwise\store\$Only.rmx" "--b=$root\bitwise\new\$Only.rmx" 2>&1 | Out-File -Encoding utf8 "$root\bitwise\diff_store_new_$Only.txt"
    "diff store/new $Only code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
}
"done $Only $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
