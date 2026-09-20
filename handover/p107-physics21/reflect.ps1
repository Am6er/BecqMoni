# П107 — проверка ОТРАЖЕНИЕМ из каталога проб (A77: проверять то, что будет использовано):
# PhysicsVersion, FormatVersion, умолчания ResponseMatrixOptions. Образец — П97/П103 reflect.ps1.
param([string]$Probes = 'D:\BqMoni_Claude\p107\wt\tools\effmaker\probes\build_p107',
      [string]$Out = 'D:\BqMoni_Claude\p107\art\reflection_p107.txt')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$p = Join-Path $Probes 'BecquerelMonitor.exe'
$a = [Reflection.Assembly]::LoadFrom($p)
$rm = $a.GetType('BecquerelMonitor.EfficiencyMaker.ResponseMatrix')
$o = $a.GetType('BecquerelMonitor.EfficiencyMaker.ResponseMatrixOptions')
$opt = [Activator]::CreateInstance($o)
$lines = @()
$lines += "exe: $p"
$lines += "mtime: " + (Get-Item $p).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')
$lines += "sha256: " + (Get-FileHash $p).Hash.ToLower()
$lines += "PhysicsVersion = " + $rm.GetField('PhysicsVersion').GetRawConstantValue()
$lines += "FormatVersion = " + $rm.GetField('FormatVersion').GetRawConstantValue()
foreach ($n in 'ElectronLayerBremAlongPath', 'ElectronLayerMixedScattering', 'ElectronLayerTransport', 'ElectronAnyMaterial', 'BremAlongPath', 'ElectronTransport', 'PositronTransport',
               'PositronOffset', 'RayleighToCrystal', 'LightBinUnified', 'PeakChannelByTolerance', 'LYieldSupply',
               'ImportanceSampling', 'Histories', 'NodeCount', 'MinEnergyKev', 'MaxEnergyKev', 'BinKev', 'Seed',
               'JointNodes', 'JointHistories') {
    $f = $o.GetField($n)
    if ($f) { $lines += ("ResponseMatrixOptions.{0} = {1}" -f $n, $f.GetValue($opt)); continue }
    $pr = $o.GetProperty($n)
    if ($pr) { $lines += ("ResponseMatrixOptions.{0} = {1}" -f $n, $pr.GetValue($opt)) } else { $lines += "ResponseMatrixOptions.${n}: НЕТ" }
}
$lines | Tee-Object -FilePath $Out
