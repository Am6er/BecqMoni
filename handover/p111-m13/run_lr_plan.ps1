# П111 (M13): наша сторона стенда пластин — LayerReturnProbe --brem (build_p111) по тем же связкам, что g4brem.
# Выход: D:\BqMoni_Claude\p111\lr\<name>.txt, коды — codes_lr.txt.
param([int]$N = 100000, [string]$Probe = 'D:\BqMoni_Claude\p111\wt\tools\effmaker\probes\build_p111\LayerReturnProbe.exe',
      [string]$OutDir = 'D:\BqMoni_Claude\p111\lr', [string]$Extra = '', [string]$Only = '')
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = 'D:\BqMoni_Claude\p111'
New-Item -ItemType Directory -Force $OutDir | Out-Null
$codes = "$root\codes_lr.txt"
$configs = @(
    @('ptfe20', 'slab_ptfe20',      '0'),
    @('al20',   'slab_al20',        '0'),
    @('ptfe1',  'slab_ptfe1',       '0,45,70'),
    @('ptfe03', 'slab_ptfe03',      '0'),
    @('al1',    'slab_al1',         '0'),
    @('rc103',  'RC103_point0_p55', '0,45,70')
)
foreach ($c in $configs) {
    $name = $c[0]
    if ($Only -ne '' -and $name -ne $Only) { continue }
    $txt = "$OutDir\$name.txt"
    if (Test-Path $txt) { "lr $name SKIP" | Out-File -Append $codes; continue }
    $t0 = Get-Date
    $argv = @("--geometry=$root\geo\$($c[1]).in", '--energies=100,300,500,1000,2000', "--angles=$($c[2])", "--n=$N", '--elmix=1', '--brem')
    if ($Extra -ne '') { $argv += $Extra.Split(' ') }
    & $Probe @argv 2>&1 | Out-File -Encoding utf8 $txt
    "lr $name code=$LASTEXITCODE N=$N extra=[$Extra] probe=$(Split-Path -Leaf (Split-Path $Probe)) $(Get-Date -Format 'HH:mm:ss') dt=$([int]((Get-Date)-$t0).TotalSeconds)s" | Out-File -Append $codes
}
"done lr $OutDir $(Get-Date -Format 'HH:mm:ss')" | Out-File -Append $codes
