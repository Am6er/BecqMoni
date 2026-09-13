$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
& $msb 'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_p48\" "/p:IntermediateOutputPath=obj\Release_p48\" /nologo /v:m /clp:ErrorsOnly
Write-Host "MSBUILD EXIT $LASTEXITCODE"; if ($LASTEXITCODE -ne 0) { exit 100 }
Write-Host ('app sha: ' + (Get-FileHash 'BecquerelMonitor\bin\Release_p48\BecquerelMonitor.exe' -Algorithm SHA256).Hash + ' ' + (Get-Item 'BecquerelMonitor\bin\Release_p48\BecquerelMonitor.exe').LastWriteTime.ToString('HH:mm:ss'))
$f = 'tools\effmaker\probes\FsaStackShot.cs'
try {
  Copy-Item 'D:\BqMoni_Claude\p48\FsaStackShot.head.cs' $f -Force
  Write-Host ('подложен HEAD: ' + (Get-FileHash $f -Algorithm SHA256).Hash)
  & 'tools\effmaker\probes\build_all.ps1' -Bin 'BecquerelMonitor\bin\Release_p48' -Out 'D:\BqMoni_Claude\p48\build_head'
  $c1 = $LASTEXITCODE; Write-Host "BUILD_HEAD EXIT $c1"
} finally {
  Copy-Item 'D:\BqMoni_Claude\p48\FsaStackShot.p48.cs' $f -Force
  Write-Host ('возвращён мой: ' + (Get-FileHash $f -Algorithm SHA256).Hash)
}
& 'tools\effmaker\probes\build_all.ps1' -Bin 'BecquerelMonitor\bin\Release_p48' -Out 'tools\effmaker\probes\build_p48'
$c2 = $LASTEXITCODE; Write-Host "BUILD_P48 EXIT $c2"
& 'tools\CORPUS\scripts\mk_appwd.ps1' -Bin 'BecquerelMonitor\bin\Release_p48' -Wd 'tools\CORPUS\scripts\wd_p48' -ProbeBuild 'tools\effmaker\probes\build_p48'
$c3 = $LASTEXITCODE; Write-Host "MK_APPWD EXIT $c3"
exit ([int]$c1 + [int]$c2 + [int]$c3)
