$env:OS = 'Windows_NT'
$msb = 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
$proj = 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\BecquerelMonitor.csproj'
& $msb $proj /t:Build /p:Configuration=Release /p:Platform=AnyCPU /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=bin\Release_p48\" "/p:IntermediateOutputPath=obj\Release_p48\" /nologo /v:m /clp:ErrorsOnly`;Summary
Write-Host "MSBUILD EXIT $LASTEXITCODE"
exit $LASTEXITCODE
