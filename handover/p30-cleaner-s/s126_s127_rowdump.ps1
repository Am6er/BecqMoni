# П30 (S126/S127): прямые узлы матрицы двумя сценами (тонкий CsI ASN16_lu_side, объёмный NaI G1S_point25),
# плоский счёт 3 000 000 историй, плечи: ctrl / pos1off0 / pos1off1 (S126, узлы 1460.8 и 2614.5) и ctrl / rayl2 (S127, узлы 40,60 и 100,200).
$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$probe = 'C:\Users\moroz\bqp30\tools\effmaker\probes\build_p30\ResponseRowDumpProbe.exe'
$geo = 'C:\Users\moroz\bqp30\tools\CORPUS\corpus\geometries'
$out = 'C:\Users\moroz\bqp30\handover\p30-cleaner-s\rowdump'
Set-Location 'C:\Users\moroz\bqp30\tools\effmaker\probes\build_p30'
$scenes = @(@{ N = 'G1S_p25'; F = 'G1S_point25.in' }, @{ N = 'ASN16'; F = 'ASN16_lu_side.in' })
foreach ($s in $scenes) {
  foreach ($arm in @(@{ N='ctrl'; K=@() }, @{ N='pos1off0'; K=@('--positron=1','--posoffset=0') }, @{ N='pos1off1'; K=@('--positron=1','--posoffset=1') })) {
    $t0 = Get-Date
    & $probe ("--geometry=$geo\" + $s.F) --direct --peakw=0 --e=1460.8,2614.5 --hist=3000000 ("--out=$out\" + $s.N + '_hi_' + $arm.N) @($arm.K) *> ("$out\" + $s.N + '_hi_' + $arm.N + '.log')
    Write-Output ("{0} hi {1}: exit={2} ({3} s)" -f $s.N, $arm.N, $LASTEXITCODE, [int]((Get-Date)-$t0).TotalSeconds)
  }
  foreach ($arm in @(@{ N='ctrl'; K=@() }, @{ N='rayl2'; K=@('--rayl2=1') })) {
    foreach ($e in @('40,60','100,200')) {
      $t0 = Get-Date
      $tag = $e.Replace(',', '_')
      & $probe ("--geometry=$geo\" + $s.F) --direct --peakw=0 --e=$e --hist=3000000 ("--out=$out\" + $s.N + '_lo' + $tag + '_' + $arm.N) @($arm.K) *> ("$out\" + $s.N + '_lo' + $tag + '_' + $arm.N + '.log')
      Write-Output ("{0} lo {1} {2}: exit={3} ({4} s)" -f $s.N, $tag, $arm.N, $LASTEXITCODE, [int]((Get-Date)-$t0).TotalSeconds)
    }
  }
}
