@echo off
rem P111 (M13): build of g4brem (bremsstrahlung-in-slab reference) - recipe of D:\BqMoni_Claude\p92\g4\g4_build.bat (P85/P92), own dir.
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
set "PATH=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin;C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja;%PATH%"
cmake -G Ninja -DCMAKE_BUILD_TYPE=Release -DGeant4_DIR="C:\Users\moroz\source\repos\GEANT4\geant4-11.4.2-win64\lib\cmake\Geant4" -S "D:\BqMoni_Claude\p111\g4brem" -B "D:\BqMoni_Claude\p111\g4brem\build" || exit /b 1
ninja -C "D:\BqMoni_Claude\p111\g4brem\build"
