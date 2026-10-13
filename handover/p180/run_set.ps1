# П180 S203: набор независимых прогонов G4RawProbe (зёрна), маринелли RC-103; один процесс = одно ядро
param([Parameter(Mandatory)][string]$Build, [Parameter(Mandatory)][string]$Tag, [Parameter(Mandatory)][double]$E,
      [int]$N = 25000000, [int]$K = 8, [string]$Extra = '')
$exe = "D:\BqMoni_Claude\p180\build_p180_$Build\G4RawProbe.exe"
$geo = 'D:\BqMoni_Claude\p180\wt\tools\CORPUS\corpus\geometries\RC103_marinelli05_kcl.in'
$dir = 'D:\BqMoni_Claude\p180\s203'
$procs = @()
for ($i = 0; $i -lt $K; $i++) {
    $seed = 180000 + [int]$E * 100 + $i
    $out = "$dir\${Tag}_E$([int]$E)_s$i"
    $args = @("--geometry=$geo", "--energy=$E", "--n=$N", '--bin=0.1', '--no-light', '--peakw', "--seed=$seed", "--out=$out.csv")
    if ($Extra) { $args += $Extra.Split(' ') }
    $procs += Start-Process -FilePath $exe -ArgumentList $args -RedirectStandardOutput "$out.txt" -RedirectStandardError "$out.err" -NoNewWindow -PassThru
}
$procs | Wait-Process
foreach ($p in $procs) { "code=$($p.ExitCode)" }
