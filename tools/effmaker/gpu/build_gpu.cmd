@echo off
chcp 65001 >nul
rem build_gpu.cmd — сборка rmgpu.dll (double, ступень 1 приёмки) и rmgpu_f.dll (float, рабочая).
rem Полоса П221 (AMBER160). Нужны CUDA 12.8 (nvcc) и VS 2022 (cl.exe x64).
rem
rem ⚠ -fmad=false у double-сборки НЕ украшение: .NET Framework 4.8 (RyuJIT x64) не сливает
rem a*b+c в FMA, а nvcc сливает по умолчанию — и ступень 1 (сверка истории с CPU почти
rem побитово) расходилась бы на последнем разряде почти на каждой операции.
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 2
cd /d "%~dp0" || exit /b 2
if not exist bin mkdir bin
set COMMON=-arch=sm_86 -std=c++17 -O3 -shared -Xcompiler "/utf-8 /EHsc" -diag-suppress 177,550
nvcc %COMMON% -fmad=false -o bin\rmgpu.dll api.cu || exit /b 1
nvcc %COMMON% -DRM_REAL_FLOAT -o bin\rmgpu_f.dll api.cu || exit /b 1
echo rmgpu: OK
exit /b 0
