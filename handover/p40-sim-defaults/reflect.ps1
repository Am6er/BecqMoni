# П40 13.09.2026 — отражением из сборки: умолчания полей EfficiencySimulator против умолчаний
# ResponseMatrixOptions (склад), для ДВУХ каталогов проб: build_p38 (HEAD 83f79e86, ДО П40) и
# build_p40 (после). Ожидание: у build_p38 три поля физики 16 расходятся со складом, у build_p40 —
# все десять полей правила I сошлись. Сборка грузится в отдельный процесс на каталог (LoadFrom
# держит первую загруженную версию).
param([string]$Bin)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$exe = "$root\tools\effmaker\probes\$Bin\BecquerelMonitor.exe"
$asm = [System.Reflection.Assembly]::LoadFrom($exe)
$optT = $asm.GetType('BecquerelMonitor.EfficiencyMaker.ResponseMatrixOptions')
$simT = $asm.GetType('BecquerelMonitor.EfficiencyMaker.EfficiencySimulator')
$opt = [Activator]::CreateInstance($optT)
$sim = [Activator]::CreateInstance($simT, @($null))
$phys = $asm.GetType('BecquerelMonitor.EfficiencyMaker.ResponseMatrix').GetField('PhysicsVersion').GetRawConstantValue()
"каталог $Bin, exe sha256 $((Get-FileHash $exe).Hash.ToLower().Substring(0,16))…, PhysicsVersion=$phys"
$kdip = $optT.GetField('KDipLight').GetValue($opt)
$curveHalf = $optT.GetMethod('KDipCurveHalf').Invoke($null, @([int]$kdip))
$cascHalf = $optT.GetMethod('KDipCascadeHalf').Invoke($null, @([int]$kdip))
$rows = @(
  @('LightSubKevCurve', "KDipCurveHalf(KDipLight=$kdip)", $curveHalf),
  @('LightCascadeSplit', "KDipCascadeHalf(KDipLight=$kdip)", $cascHalf),
  @('SplitXrayShells', 'SplitXrayShells', $optT.GetField('SplitXrayShells').GetValue($opt)),
  @('LightBinUnified', 'LightBinUnified', $optT.GetField('LightBinUnified').GetValue($opt)),
  @('PeakChannelByTolerance', 'PeakChannelByTolerance', $optT.GetField('PeakChannelByTolerance').GetValue($opt)),
  @('LYieldSupply', 'LYieldSupply', $optT.GetField('LYieldSupply').GetValue($opt)),
  @('ElectronTransport', 'ElectronTransport', $optT.GetField('ElectronTransport').GetValue($opt)),
  @('PositronTransport', 'PositronTransport', $optT.GetField('PositronTransport').GetValue($opt)),
  @('PositronOffset', 'PositronOffset', $optT.GetField('PositronOffset').GetValue($opt)),
  @('RayleighToCrystal', 'RayleighToCrystal', $optT.GetField('RayleighToCrystal').GetValue($opt))
)
$diverged = 0
foreach ($r in $rows) {
  $simV = $simT.GetField($r[0]).GetValue($sim)
  $same = ("$simV" -eq "$($r[2])")
  if (-not $same) { $diverged++ }
  "  {0,-24} sim={1,-6} store[{2}]={3,-6} {4}" -f $r[0], $simV, $r[1], $r[2], $(if ($same) { 'сошлись' } else { 'РАЗОШЛИСЬ' })
}
"итог ${Bin}: полей правила I $($rows.Count), разошлись $diverged"
