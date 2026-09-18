# П100: итоговые таблицы LayerReturnProbe (build_p100_lr, N=200000): толстые мишени PTFE/Al/MgO/C/Cu (0°), обвязка RC103 П55 (0/45/70°), кристалл CsI (--crystal, голая сцена), развёртка по шагу и отсечке.
[Console]::OutputEncoding=[System.Text.Encoding]::UTF8
$p='D:\BqMoni_Claude\p100\wt\tools\effmaker\probes\build_p100_lr\LayerReturnProbe.exe'
$t='D:\BqMoni_Claude\p100\tables'
$codes='D:\BqMoni_Claude\p100\codes_lr.txt'
function Run($name, $argv) {
  $t0=Get-Date
  & $p @argv 2>&1 | Out-File -Encoding utf8 "$t\$name.txt"
  "$name code=$LASTEXITCODE dt=$([int]((Get-Date)-$t0).TotalSeconds)s $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
}
foreach ($m in 'ptfe','al','mgo','c','cu') {
  Run "lr_thick_$m" @("--geometry=D:\BqMoni_Claude\p100\geo\tabata_$m.in", '--energies=100,500,1000,2000', '--angles=0', '--n=200000')
}
Run 'lr_rc103_p55_final' @('--geometry=D:\BqMoni_Claude\p100\geo\RC103_point0_p55.in', '--energies=100,500,1000,2000', '--angles=0,45,70', '--n=200000', '--diag')
Run 'lr_as80_disk_final' @('--geometry=D:\BqMoni_Claude\p100\geo\AS80_th_disk.in', '--energies=100,500,1000,2000', '--angles=0,45', '--n=200000')
Run 'lr_crystal_csi' @('--geometry=D:\BqMoni_Claude\p100\geo\RC103_point0_p55_bare.in', '--energies=100,500,1000,2000', '--angles=0', '--n=100000', '--elmix=0', '--crystal')
foreach ($st in 0.05,0.02,0.01) { Run "lr_step_$st" @('--geometry=D:\BqMoni_Claude\p100\geo\tabata_ptfe.in', '--energies=100,500,1000', '--angles=0', '--n=100000', "--step=$st") }
foreach ($c in 10,30,45) { Run "lr_cutoff_$c" @('--geometry=D:\BqMoni_Claude\p100\geo\tabata_ptfe.in', '--energies=100,500,1000', '--angles=0', '--n=100000', '--elmix=1', "--cutoff=$c") }
"done $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
