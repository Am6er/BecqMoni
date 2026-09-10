@echo off
rem Запуск g4cf: датасеты бинарной поставки geant4-11.4.2-win64 + DLL в PATH.
rem
rem ⛔ КЛЮЧ `vacuum` ОБЯЗАТЕЛЕН ДЛЯ СВЕРОК НА ГОЛОМ КРИСТАЛЛЕ (`T133`, ~~`A85`~~,
rem    03.09.2026). Мир арбитра решает 42 % полосы: одна и та же сцена голого
rem    NaI Ø80×80 в полосе 55…59 кэВ даёт 8.232e-4 на историю с ВОЗДУШНЫМ миром
rem    против 5.794e-4 в пустоте, а в бинах 57…59 — ВТРОЕ. Наша сцена воздуха не
rem    знает вовсе (вне объявленных областей у `EfficiencySimulator` пустота),
rem    поэтому сверка без ключа мерит воздух вокруг кристалла, а не кристалл, и
rem    выглядит при этом совершенно обычно: ни отказа, ни предупреждения от
rem    самого Geant4 не будет. Ключ ставится ПЕРВЫМ аргументом:
rem        run_g4cf.bat vacuum scene <файл> hist <E_кэВ> <N> <шаг_бина>
rem    Ниже — предупреждение на случай, когда его забыли (это и есть читатель
rem    признака: раньше признак был, а потребителя у него не было).

set "G4CF_HAS_VACUUM="
for %%A in (%*) do if /i "%%~A"=="vacuum" set "G4CF_HAS_VACUUM=1"
if not defined G4CF_HAS_VACUUM (
  echo [g4cf] WARNING: key "vacuum" NOT given - the arbiter world is AIR.>&2
  echo [g4cf] Na golom kristalle eto oshibka: mir reshaet 42%% polosy 55...59 keV.>&2
  echo [g4cf] 8.232e-4 ^(air^) vs 5.794e-4 ^(vacuum^); bins 57...59 - x3. See T133.>&2
)

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
"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\tools\g4cf\build\g4cf.exe" %*
