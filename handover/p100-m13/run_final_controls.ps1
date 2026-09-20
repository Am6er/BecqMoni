# П100: контроли на ФИНАЛЬНОЙ сборке build_p100 (после правки крайнего случая «слой без элементов»): (1) побитовость ВЫКЛ против base;
# (2) η ВКЛ на RC103 П55 (50 тыс., зерно штатное) — обязано совпасть с прежними 0.0715/0.0524/0.0384 (тот же путь, тот же поток);
# (3) цена парой ABAB --threads=1 на RC103_point0 и AS80_th_disk.
[Console]::OutputEncoding=[System.Text.Encoding]::UTF8
$root='D:\BqMoni_Claude\p100'
$new="$root\wt\tools\effmaker\probes\build_p100\CorpusMatrixProbe.exe"
$diff="$root\wt\tools\effmaker\probes\build_p100\MatrixDiffProbe.exe"
$lr="$root\wt\tools\effmaker\probes\build_p100\LayerReturnProbe.exe"
$codes="$root\codes_final.txt"
function Run($exe, $dir, $only, $tag, $keys) {
  $t0=Get-Date
  & $exe "--dir=$dir" "--only=$only" --threads=1 --target=0 --force @keys 2>&1 | Out-File -Encoding utf8 "$root\$tag.txt"
  "$tag code=$LASTEXITCODE keys=[$($keys -join ' ')] $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
}
Run $new "$root\bitwise\new" 'RC103_point0' 'bitwise\matrix_final_RC103_point0' @('--nodes=2','--n=300000')
& $diff "--a=$root\bitwise\base\RC103_point0.rmx" "--b=$root\bitwise\new\RC103_point0.rmx" 2>&1 | Out-File -Encoding utf8 "$root\bitwise\diff_base_final_RC103_point0.txt"
"diff base/final RC103_point0 code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
& $lr --geometry=D:\BqMoni_Claude\p100\geo\RC103_point0_p55.in --energies=100,500,1000 --angles=0 --n=50000 2>&1 | Out-File -Encoding utf8 "$root\tables\lr_final_control.txt"
"lr final control code=$LASTEXITCODE $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
foreach ($only in 'RC103_point0','AS80_th_disk') {
  $n = if ($only -eq 'RC103_point0') { '--n=600000' } else { '--n=200000' }
  foreach ($rep in 1,2) {
    Run $new "$root\cost\off" $only "cost\matrix_off_${only}_$rep" @('--nodes=3', $n)
    Run $new "$root\cost\on"  $only "cost\matrix_on_${only}_$rep"  @('--nodes=3', $n, '--elmix=1')
  }
}
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
