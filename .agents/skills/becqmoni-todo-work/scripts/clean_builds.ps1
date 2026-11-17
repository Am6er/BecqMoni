param([switch]$Apply, [string]$ProjectRoot = (Join-Path $PSScriptRoot '..\..\..\..'))
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ProjectRoot).ProviderPath.TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $root 'BecquerelMonitor\BecquerelMonitor.csproj'))) { throw 'Ожидался корень проекта BecqMoni' }
$cut = (Get-Date).AddHours(-24)
$sets = @(
 @{ Dir = (Join-Path $root 'BecquerelMonitor\bin'); Keep = @('Debug','Release','Debug_Codex','Release_Codex'); Pattern = '*' },
 @{ Dir = (Join-Path $root 'BecquerelMonitor\obj'); Keep = @('Debug','Release','Debug_Codex','Release_Codex'); Pattern = '*' },
 @{ Dir = (Join-Path $root 'tools\effmaker\probes'); Keep = @('build','build_rel'); Pattern = 'build*' },
 @{ Dir = (Join-Path $root 'tools\CORPUS\scripts'); Keep = @('wd_app'); Pattern = 'wd_*' }
)
$nDel=0; $nKeep=0; $totalDel=0L; $failed=@()
foreach ($s in $sets) {
 if (-not (Test-Path -LiteralPath $s.Dir)) { continue }
 foreach ($d in Get-ChildItem -LiteralPath $s.Dir -Directory -Filter $s.Pattern -Force) {
  $target = [IO.Path]::GetFullPath($d.FullName)
  if (-not $target.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetDirectoryName($target) -ine [IO.Path]::GetFullPath($s.Dir)) { throw 'Каталог вне разрешённой области уборки' }
  if ($d.Attributes -band [IO.FileAttributes]::ReparsePoint) { $nKeep++; Write-Output ('KEEP LINK '+$d.FullName); continue }
  if (@(Get-ChildItem -LiteralPath $target -Recurse -Force -Attributes ReparsePoint).Count) { $nKeep++; Write-Output ('KEEP LINKED CONTENT '+$target); continue }
  $files=@(Get-ChildItem -LiteralPath $target -Recurse -File -Force)
  $size=($files|Measure-Object Length -Sum).Sum; if(-not $size){$size=0}
  $newest=($files|Measure-Object LastWriteTime -Maximum).Maximum; if(-not $newest){$newest=$d.LastWriteTime}
  if(($s.Keep -contains $d.Name) -or $newest -ge $cut){$nKeep++;Write-Output ('KEEP '+$target);continue}
  $nDel++;$totalDel+=$size
  if($Apply){
   try {
    if((Resolve-Path -LiteralPath $target).ProviderPath -ine $target){throw 'Изменился путь каталога'}
    Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
    Write-Output ('DEL '+$target)
   } catch {$failed+=$target;Write-Output ('FAIL '+$target+' — '+$_.Exception.Message)}
  } else {Write-Output ('WOULD '+$target)}
 }
}
Write-Output ('итого: к удалению {0}, остаётся {1}, байт {2}, отказов {3}' -f $nDel,$nKeep,$totalDel,$failed.Count)
if($failed.Count){exit 1}
