# П100 (M13): (1) побитовость ВЫКЛ — матрица RC103_point0 (узлы 2) сборкой base (HEAD 93dafe11 без правки) и new (правка, ключ ВЫКЛ) → MatrixDiffProbe;
# (2) цена — RC103_point0 и AS80_th_disk, узлы 3, --n фиксированный, --threads=1, ВЫКЛ/ВКЛ (--elmix=1). ⛔ --dir — только копии сцен.
[Console]::OutputEncoding=[System.Text.Encoding]::UTF8
$root='D:\BqMoni_Claude\p100'
$base="$root\wt_base\tools\effmaker\probes\build_p100_base\CorpusMatrixProbe.exe"
$new="$root\wt\tools\effmaker\probes\build_p100\CorpusMatrixProbe.exe"
$diff="$root\wt\tools\effmaker\probes\build_p100\MatrixDiffProbe.exe"
$codes="$root\codes_bitwise_cost.txt"
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
# (2) цена (--threads=1; фон — две другие полосы и стенд П100)
foreach ($only in 'RC103_point0','AS80_th_disk') {
  $n = if ($only -eq 'RC103_point0') { '--n=600000' } else { '--n=200000' }
  Run $new "$root\cost\off" $only "cost\matrix_off_$only" @('--nodes=3', $n)
  Run $new "$root\cost\on"  $only "cost\matrix_on_$only"  @('--nodes=3', $n, '--elmix=1')
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
