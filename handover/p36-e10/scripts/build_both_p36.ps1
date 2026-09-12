$env:OS = 'Windows_NT'
$repo = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
Set-Location $repo
$log = Join-Path $PSScriptRoot 'build_both_p36.log'
"=== начало $(Get-Date -Format 'HH:mm:ss')" | Tee-Object -FilePath $log
$code = 1
for ($attempt = 1; $attempt -le 3; $attempt++) {
    "--- попытка ${attempt}: приложение $(Get-Date -Format 'HH:mm:ss')" | Tee-Object -FilePath $log -Append
    & 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
      (Join-Path $repo 'BecquerelMonitor\BecquerelMonitor.csproj') `
      /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
      /p:SignManifests=false /p:GenerateManifests=false `
      /p:OutputPath='bin\Release_p36\' /p:IntermediateOutputPath='obj\Release_p36\' `
      /nologo /v:m 2>&1 | Tee-Object -FilePath $log -Append
    $app = $LASTEXITCODE
    "APP_EXIT=$app" | Tee-Object -FilePath $log -Append
    if ($app -ne 0) { $code = $app; continue }
    "--- попытка ${attempt}: пробы $(Get-Date -Format 'HH:mm:ss')" | Tee-Object -FilePath $log -Append
    & pwsh -NoProfile -File (Join-Path $repo 'tools\effmaker\probes\build_all.ps1') `
        -Bin 'BecquerelMonitor\bin\Release_p36' -Out 'tools\effmaker\probes\build_p36' 2>&1 |
        Where-Object { $_ -notmatch '^ok   ' } | Tee-Object -FilePath $log -Append
    $code = $LASTEXITCODE
    "PROBES_EXIT=$code" | Tee-Object -FilePath $log -Append
    if ($code -eq 0) { break }
    Start-Sleep -Seconds 20
}
"EXIT=$code $(Get-Date -Format 'HH:mm:ss')" | Tee-Object -FilePath $log -Append
exit $code
