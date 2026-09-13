# П44: сборка приложения Release в свой каталог (bin\Release_p44, obj\Release_p44).
$ErrorActionPreference = 'Continue'
$env:OS = 'Windows_NT'
$root = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$log = 'D:\BqMoni_Claude\p44\build_app.log'
& $msbuild "$root\BecquerelMonitor\BecquerelMonitor.csproj" /t:Build /p:Configuration=Release /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false /p:OutputPath='bin\Release_p44\' `
    /p:IntermediateOutputPath='obj\Release_p44\' /nologo /v:m 2>&1 | Out-File -Encoding utf8 $log
$code = $LASTEXITCODE
"exit=$code" | Out-File -Append -Encoding utf8 $log
Get-Content $log | Select-String -Pattern 'error|warning CS|exit=' | Select-Object -First 40
"BUILD_APP_EXIT=$code"
exit $code
