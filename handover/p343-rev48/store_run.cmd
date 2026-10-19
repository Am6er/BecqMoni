@echo off
rem P343 store run (M14, physics 27): dense scenes x2 histories first, then the rest without --force (T36 keeps dense, stamp keeps the A77 pair).
set P=D:\BqMoni_Claude\p343
set PR=D:\BqMoni_Claude\p343\wt\tools\effmaker\probes\build_p343
set DLL=D:\BqMoni_Claude\p343\wt\tools\effmaker\gpu\bin\rmgpu_f.dll
cd /d %P%
"%PR%\CorpusMatrixProbe.exe" --dir=%P%\store --gpu=%DLL% --target=0 --n=6000000 --only=RC103_point50,ASN16_point10_house,G1S_point25,G1S_point25_p24 > %P%\logs\store_dense.log 2> %P%\logs\store_dense.err
echo dense exit %ERRORLEVEL% %DATE% %TIME% > %P%\logs\store_dense_done.txt
"%PR%\CorpusMatrixProbe.exe" --dir=%P%\store --gpu=%DLL% --target=0 --n=3000000 > %P%\logs\store_main.log 2> %P%\logs\store_main.err
echo main exit %ERRORLEVEL% %DATE% %TIME% > %P%\logs\store_main_done.txt
