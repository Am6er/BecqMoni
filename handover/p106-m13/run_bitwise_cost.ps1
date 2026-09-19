# П106 (M13): (1) побитовость умолчаний — матрица RC103_point0 и AS80_th_disk (узлы 2) сборкой base (HEAD a7ad8933 без правки)
# и new (правка: рычаги замера, все умолчанием ВКЛ) → MatrixDiffProbe; (2) цена — RC103_point0, узлы 3, --n фиксированный,
# --threads=1, парами ABAB base/new (правка — только счётчики и грань выхода у своих вылетов). ⛔ --dir — только копии сцен (.in).
[Console]::OutputEncoding=[System.Text.Encoding]::UTF8
$root='D:\BqMoni_Claude\p106'
$store='C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\CORPUS\corpus\geometries'
$base="$root\wt_base\tools\effmaker\probes\build_p106_base\CorpusMatrixProbe.exe"
$new="$root\wt\tools\effmaker\probes\build_p106\CorpusMatrixProbe.exe"
$diff="$root\wt\tools\effmaker\probes\build_p106\MatrixDiffProbe.exe"
$codes="$root\codes_bitwise_cost.txt"
foreach ($d in 'base','new','costA','costB') { New-Item -ItemType Directory -Force "$root\bitwise\$d" | Out-Null }
foreach ($s in 'RC103_point0','AS80_th_disk') { foreach ($d in 'base','new','costA','costB') { Copy-Item "$store\$s.in" "$root\bitwise\$d\" } }
function Run($exe, $dir, $only, $tag, $keys) {
  $t0=Get-Date
  & $exe "--dir=$dir" "--only=$only" --threads=1 --target=0 --force @keys 2>&1 | Out-File -Encoding utf8 "$root\$tag.txt"
  "$tag code=$LASTEXITCODE keys=[$($keys -join ' ')] $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
}
# (1) побитовость
Run $base "$root\bitwise\base" 'RC103_point0' 'bitwise\matrix_base_RC103_point0' @('--nodes=2','--n=300000')
Run $new  "$root\bitwise\new"  'RC103_point0' 'bitwise\matrix_new_RC103_point0'  @('--nodes=2','--n=300000')
& $diff "--a=$root\bitwise\base\RC103_point0.rmx" "--b=$root\bitwise\new\RC103_point0.rmx" 2>&1 | Out-File -Encoding utf8 "$root\bitwise\diff_base_new_RC103_point0.txt"
"diff base/new RC103_point0 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
Run $base "$root\bitwise\base" 'AS80_th_disk' 'bitwise\matrix_base_AS80_th_disk' @('--nodes=2','--n=100000')
Run $new  "$root\bitwise\new"  'AS80_th_disk' 'bitwise\matrix_new_AS80_th_disk'  @('--nodes=2','--n=100000')
& $diff "--a=$root\bitwise\base\AS80_th_disk.rmx" "--b=$root\bitwise\new\AS80_th_disk.rmx" 2>&1 | Out-File -Encoding utf8 "$root\bitwise\diff_base_new_AS80_th_disk.txt"
"diff base/new AS80_th_disk code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
# (2) цена парами ABAB (--threads=1)
foreach ($pair in 1,2) {
  Run $base "$root\bitwise\costA" 'RC103_point0' "bitwise\cost_base_RC103_point0_$pair" @('--nodes=3','--n=600000')
  Run $new  "$root\bitwise\costB" 'RC103_point0' "bitwise\cost_new_RC103_point0_$pair"  @('--nodes=3','--n=600000')
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
