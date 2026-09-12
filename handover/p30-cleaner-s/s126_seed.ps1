# П30: контроль «зерно против зерна» для прямых узлов (те же плечи ctrl, seed=1).
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$probe = 'C:\Users\moroz\bqp30\tools\effmaker\probes\build_p30\ResponseRowDumpProbe.exe'
$geo = 'C:\Users\moroz\bqp30\tools\CORPUS\corpus\geometries'
$out = 'C:\Users\moroz\bqp30\handover\p30-cleaner-s\rowdump'
Set-Location 'C:\Users\moroz\bqp30\tools\effmaker\probes\build_p30'
foreach ($s in @(@{ N = 'G1S_p25'; F = 'G1S_point25.in' }, @{ N = 'ASN16'; F = 'ASN16_lu_side.in' })) {
  foreach ($e in @(@{T='hi'; E='1460.8,2614.5'}, @{T='lo40_60'; E='40,60'}, @{T='lo100_200'; E='100,200'})) {
    $t0 = Get-Date
    & $probe ("--geometry=$geo\" + $s.F) --direct --peakw=0 ("--e=" + $e.E) --hist=3000000 --seed=1 ("--out=$out\" + $s.N + '_' + $e.T + '_seed1') *> ("$out\" + $s.N + '_' + $e.T + '_seed1.log')
    Write-Output ("{0} {1} seed1: exit={2} ({3} s)" -f $s.N, $e.T, $LASTEXITCODE, [int]((Get-Date)-$t0).TotalSeconds)
  }
}
