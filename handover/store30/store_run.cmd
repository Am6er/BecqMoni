@echo off
rem store30 (AMBER274, physics 30): photon dense x2 first, photon rest 3M without --force (stamp keeps the A77 pair, T36 keeps dense),
rem then electron sections on GPU (sections.ps1), then corpus curves into the worktree spectra (the probe fills store response).
set S=D:\BqMoni_Claude\store30
set PR=D:\BqMoni_Claude\store30\wt\tools\effmaker\probes\build_s30
set DLL=D:\BqMoni_Claude\store30\wt\tools\effmaker\gpu\bin\rmgpu_f.dll
cd /d %S%
"%PR%\CorpusMatrixProbe.exe" --dir=%S%\photon --gpu=%DLL% --target=0 --n=6000000 --only=RC103_point50,ASN16_point10_house,G1S_point25,G1S_point25_p24 > %S%\logs\photon_dense.log 2> %S%\logs\photon_dense.err
echo dense exit %ERRORLEVEL% %DATE% %TIME% > %S%\logs\photon_dense_done.txt
"%PR%\CorpusMatrixProbe.exe" --dir=%S%\photon --gpu=%DLL% --target=0 --n=3000000 > %S%\logs\photon_main.log 2> %S%\logs\photon_main.err
echo main exit %ERRORLEVEL% %DATE% %TIME% > %S%\logs\photon_main_done.txt
"C:\Program Files\PowerShell\7\pwsh.exe" -NoProfile -File %S%\scripts\sections.ps1 > %S%\logs\sections.log 2> %S%\logs\sections.err
echo sections exit %ERRORLEVEL% %DATE% %TIME% > %S%\logs\sections_done.txt
"%PR%\CorpusEffProbe.exe" --dir=%S%\store --spectra=D:\BqMoni_Claude\store30\wt\tools\CORPUS\corpus\spectra --n=400000 > %S%\logs\curves.log 2> %S%\logs\curves.err
echo curves exit %ERRORLEVEL% %DATE% %TIME% > %S%\logs\curves_done.txt
