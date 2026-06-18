# Build Command

Use this exact command from the repository root to build the project:

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe' `
  'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\BecquerelMonitor.csproj' `
  /t:Build `
  /p:Configuration=Debug `
  /p:Platform='AnyCPU' `
  /p:SignManifests=false `
  /p:OutputPath='bin\Debug_Codex\'
```
