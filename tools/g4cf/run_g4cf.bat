@echo off
rem Запуск g4cf: датасеты бинарной поставки geant4-11.4.2-win64 + DLL в PATH.
rem
rem Дамп физики 32 задаёт вещество окружающего мира. Для старого дампа
rem без строки world используется G4_AIR. Для воспроизведения контроля в вакууме:
rem     run_g4cf.bat vacuum scene <файл> hist <E_кэВ> <N> <шаг_бина>
rem
rem Ключ `corr` (П85, `AMBER42`, 15.09.2026) — угловые γ–γ корреляции каскада в
rem    RDM (`G4DeexPrecoParameters::SetCorrelatedGamma(true)` до `/run/initialize`);
rem    без ключа арбитр изотропен, как и был. Ставится там же, где `vacuum`, в
rem    любом порядке: run_g4cf.bat corr scene <файл> ion <Z> <A> <N> <окна…>.
rem    Читатель: строка `SETUP correlatedGamma=1` в stdout и сводка RDM
rem    «Enable correlated gamma emission 1». Шапка g4cf.cc — что именно читает флаг.

rem При запуске со сценой g4cf печатает материал, плотность и границу мира.

set "G4ROOT=C:\Users\moroz\source\repos\GEANT4"
set "PATH=%G4ROOT%\geant4-11.4.2-win64\bin;%PATH%"
set "G4LEDATA=%G4ROOT%\G4EMLOW8.8"
set "G4LEVELGAMMADATA=%G4ROOT%\PhotonEvaporation6.1.2"
set "G4RADIOACTIVEDATA=%G4ROOT%\RadioactiveDecay6.1.2"
set "G4ENSDFSTATEDATA=%G4ROOT%\G4ENSDFSTATE3.0"
set "G4PARTICLEXSDATA=%G4ROOT%\G4PARTICLEXS4.2"
set "G4NEUTRONHPDATA=%G4ROOT%\G4NDL4.7.1"
set "G4PIIDATA=%G4ROOT%\G4PII1.3"
set "G4REALSURFACEDATA=%G4ROOT%\RealSurface2.2"
set "G4SAIDXSDATA=%G4ROOT%\G4SAIDDATA2.0"
set "G4ABLADATA=%G4ROOT%\G4ABLA3.3"
set "G4INCLDATA=%G4ROOT%\G4INCL1.3"
set "G4CHANNELINGDATA=%G4ROOT%\G4CHANNELING2.0"
set "G4NUDEXLIBDATA=%G4ROOT%\G4NUDEXLIB1.0"
set "G4URRPTDATA=%G4ROOT%\G4URRPT1.1"
set "G4TENDLDATA=%G4ROOT%\G4TENDL1.4"
rem (AMBER274, physics 29, lane phys29, 01.11.2026) The exe is taken NEXT TO this file
rem (build\g4cf.exe of the same tree), not by an absolute path into one checkout: a worktree
rem or another lane used to launch the main tree's exe silently. Override: set G4CF_EXE.
rem A stale build\g4cf.exe does not know the keys electron/espec and silently runs the
rem built-in scene (critic of physics, 30.10.2026) - rebuild with cmake after editing g4cf.cc.
if not defined G4CF_EXE set "G4CF_EXE=%~dp0build\g4cf.exe"
if not exist "%G4CF_EXE%" ( echo [g4cf] no %G4CF_EXE% - build tools\g4cf with cmake first>&2 & exit /b 2 )
"%G4CF_EXE%" %*
