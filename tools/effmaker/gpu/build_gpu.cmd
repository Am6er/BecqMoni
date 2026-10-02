@echo off
rem build_gpu.cmd - rmgpu.dll (double, stage 1) and rmgpu_f.dll (float, production).
rem ASCII only on purpose: cmd misparses UTF-8 batch files. Rationale for every flag - README.md, section "Build".
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 2
cd /d "%~dp0" || exit /b 2
if not exist bin mkdir bin
set COMMON=%RM_EXTRA% -arch=sm_86 -std=c++17 -O3 -shared -Xcompiler "/utf-8 /EHsc" -diag-suppress 177,550
nvcc %COMMON% -fmad=false -o bin\rmgpu.dll api.cu || exit /b 1
nvcc %COMMON% -DRM_REAL_FLOAT -maxrregcount=64 -o bin\rmgpu_f.dll api.cu || exit /b 1
cl /nologo /O2 /EHsc /utf-8 rm_replay.cpp /Fobin\rm_replay.obj /Febin\rm_replay.exe || exit /b 1
echo rmgpu: OK
exit /b 0
