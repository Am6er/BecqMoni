# Сборка приложения полосы П54: -Config Debug|Release, -Out <OutputPath>, -Obj <IntermediateOutputPath>, -Log <файл>
param(
    [Parameter(Mandatory)][string]$Config,
    [Parameter(Mandatory)][string]$Out,
    [Parameter(Mandatory)][string]$Obj,
    [Parameter(Mandatory)][string]$Log
)
$env:OS = 'Windows_NT'
Set-Location 'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
$sw = [Diagnostics.Stopwatch]::StartNew()
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
    'BecquerelMonitor\BecquerelMonitor.csproj' /t:Build "/p:Configuration=$Config" /p:Platform=AnyCPU `
    /p:SignManifests=false /p:GenerateManifests=false "/p:OutputPath=$Out" "/p:IntermediateOutputPath=$Obj" `
    /v:minimal /nologo *> $Log
$code = $LASTEXITCODE
"msbuild $Config -> $Out : exit=$code  $([int]$sw.Elapsed.TotalSeconds) s"
Get-Content $Log | Select-String 'error|warning' | Select-Object -First 20
exit $code
