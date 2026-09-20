# П106 (M13): цена ключа lbrem — RC103_point0, узлы 3, --n=600000, --threads=1, парами ABAB ВЫКЛ/ВКЛ (одна сборка build_p106). ⛔ --dir — копии сцен.
[Console]::OutputEncoding=[System.Text.Encoding]::UTF8
$root='D:\BqMoni_Claude\p106'
$new="$root\wt\tools\effmaker\probes\build_p106\CorpusMatrixProbe.exe"
$codes="$root\codes_bitwise_cost.txt"
foreach ($d in 'costOff','costOn') { New-Item -ItemType Directory -Force "$root\bitwise\$d" | Out-Null; Copy-Item "$root\bitwise\base\RC103_point0.in" "$root\bitwise\$d\" }
function Run($exe, $dir, $only, $tag, $keys) {
  $t0=Get-Date
  & $exe "--dir=$dir" "--only=$only" --threads=1 --target=0 --force @keys 2>&1 | Out-File -Encoding utf8 "$root\$tag.txt"
  "$tag code=$LASTEXITCODE keys=[$($keys -join ' ')] $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
}
foreach ($pair in 1,2) {
  Run $new "$root\bitwise\costOff" 'RC103_point0' "bitwise\cost_off_RC103_point0_$pair" @('--nodes=3','--n=600000')
  Run $new "$root\bitwise\costOn"  'RC103_point0' "bitwise\cost_on_RC103_point0_$pair"  @('--nodes=3','--n=600000','--lbrem=1')
}
& "$root\wt\tools\effmaker\probes\build_p106\MatrixDiffProbe.exe" "--a=$root\bitwise\costOff\RC103_point0.rmx" "--b=$root\bitwise\costOn\RC103_point0.rmx" 2>&1 | Out-File -Encoding utf8 "$root\bitwise\diff_off_on_RC103_point0.txt"
"diff off/on RC103_point0 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
"done cost_on $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
