# П180 S198: съёмка таблицы света FSA (FsaLightScale) пробой LightScaleProbe ветки физики 25, умолчания склада
param([string]$Build = 'p25', [int]$K = 12)
$exe = "D:\BqMoni_Claude\p180\build_p180_$Build\LightScaleProbe.exe"
$nai = 'D:\BqMoni_Claude\p180\wt\tools\CORPUS\corpus\geometries\G1S_point5.in'
$csi = 'D:\BqMoni_Claude\p180\wt\handover\p18-light-anchor\ASN16_csi_point1.in'
$jobs = @(
  @('nai_a', $nai, 1000000, '10,20,30,31,32,33,33.1'),
  @('nai_b', $nai, 1000000, '33.2,33.3,33.4,33.5,33.7,34,34.5'),
  @('nai_c', $nai, 1000000, '35,35.5,36,36.5,37,37.5,38'),
  @('nai_d', $nai, 1000000, '39,40,45,50'),
  @('nai_e', $nai, 4000000, '60,81,100,122'),
  @('nai_f', $nai, 4000000, '200,356,661.657,1000'),
  @('csi_a', $csi, 4000000, '10,20,30,32,33'),
  @('csi_b', $csi, 4000000, '33.1,33.2,33.3,33.5,33.7'),
  @('csi_c', $csi, 4000000, '34,34.5,35,35.5,35.9'),
  @('csi_d', $csi, 4000000, '36.1,36.3,36.6,37,37.5'),
  @('csi_e', $csi, 4000000, '38,39,40,50,60'),
  @('csi_f', $csi, 4000000, '81,100,122,200,356,661.657,1000,1332.5')
)
$run = @()
foreach ($j in $jobs) {
    $out = "D:\BqMoni_Claude\p180\ls\shoot_$($j[0]).txt"
    $run += Start-Process -FilePath $exe -ArgumentList @("--geometry=$($j[1])", "--n=$($j[2])", "--energies=$($j[3])") -RedirectStandardOutput $out -RedirectStandardError "$out.err" -NoNewWindow -PassThru
}
$run | Wait-Process
foreach ($p in $run) { "code=$($p.ExitCode)" }
