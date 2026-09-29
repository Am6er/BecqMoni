# П180 S198: сетка (E_q, p) обрыва трека — LightScaleProbe на G1S_point5, параллельно, по K процессов
param([string]$Build = 'x1', [int]$N = 400000, [int]$K = 7, [string]$Sets = '1.0:2')
$exe = "D:\BqMoni_Claude\p180\build_p180_$Build\LightScaleProbe.exe"
$geo = 'D:\BqMoni_Claude\p180\wt\tools\CORPUS\corpus\geometries\G1S_point5.in'
$en = '10,20,30,33,33.2,33.5,34,34.5,35,35.5,36,36.5,37,38,40,50,100'
$queue = [System.Collections.Generic.Queue[string]]::new(); foreach ($s in $Sets.Split(',')) { $queue.Enqueue($s) }
$run = @()
while ($queue.Count -gt 0 -or $run.Count -gt 0) {
    $run = @($run | Where-Object { -not $_.HasExited })
    while ($run.Count -lt $K -and $queue.Count -gt 0) {
        $s = $queue.Dequeue(); $eq, $p = $s.Split(':')
        $out = "D:\BqMoni_Claude\p180\ls\g_eq${eq}_p$p.txt"
        $run += Start-Process -FilePath $exe -ArgumentList @("--geometry=$geo", "--n=$N", "--energies=$en", "--eq=$eq", "--eqp=$p") -RedirectStandardOutput $out -RedirectStandardError "$out.err" -NoNewWindow -PassThru
    }
    Start-Sleep -Seconds 5
}
'done'
