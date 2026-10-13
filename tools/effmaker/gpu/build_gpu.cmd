@echo off
rem build_gpu.cmd - rmgpu.dll (double, stage 1) and rmgpu_f.dll (float, production).
rem ASCII only on purpose: cmd misparses UTF-8 batch files. Rationale for every flag - README.md, section "Build".
rem AMBER219 (P245, 07.10.2026): the kernel sources are fingerprinted (sha256 over *.cu *.cuh *.h *.inc, sorted by
rem name, concatenated) and the fingerprint plus the target arch go into rm_build_info(); the float DLL is then copied
rem next to the application sources (BecquerelMonitor\rmgpu_f.dll, shipped by ClickOnce - decision of Amber
rem "in git, like SpecUtilsNet.dll"). tools\check_gpu_dll.py recomputes the same fingerprint and refuses a stale copy.
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 2
cd /d "%~dp0" || exit /b 2
if not exist bin mkdir bin
set ARCH=sm_86
for /f "usebackq delims=" %%h in (`powershell -NoProfile -ExecutionPolicy Bypass -File src_hash.ps1`) do set SRCHASH=%%h
if "%SRCHASH%"=="" ( echo src_hash.ps1 gave no fingerprint & exit /b 2 )
echo sources: %SRCHASH%
set COMMON=%RM_EXTRA% -arch=%ARCH% -std=c++17 -O3 -shared -Xcompiler "/utf-8 /EHsc" -diag-suppress 177,550 -DRM_SRC_HASH=\"%SRCHASH%\" -DRM_ARCH=\"%ARCH%\"
nvcc %COMMON% -fmad=false -o bin\rmgpu.dll api.cu || exit /b 1
nvcc %COMMON% -DRM_REAL_FLOAT -maxrregcount=64 -o bin\rmgpu_f.dll api.cu || exit /b 1
cl /nologo /O2 /EHsc /utf-8 rm_replay.cpp /Fobin\rm_replay.obj /Febin\rm_replay.exe || exit /b 1
copy /y bin\rmgpu_f.dll ..\..\..\BecquerelMonitor\rmgpu_f.dll >nul || exit /b 1
echo rmgpu: OK
exit /b 0
