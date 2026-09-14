@echo off
rem P55: build of the arbiter copy with flags nofluct/nodelta/lowcut/killesc (D:\BqMoni_Claude\p55\g4)
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
set "PATH=C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin;C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja;%PATH%"
cmake -G Ninja -DCMAKE_BUILD_TYPE=Release -DGeant4_DIR="C:\Users\moroz\source\repos\GEANT4\geant4-11.4.2-win64\lib\cmake\Geant4" -S "D:\BqMoni_Claude\p55\g4" -B "D:\BqMoni_Claude\p55\g4\build" || exit /b 1
ninja -C "D:\BqMoni_Claude\p55\g4\build"
