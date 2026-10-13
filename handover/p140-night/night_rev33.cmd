@echo off
rem P140 23.09.2026 -- night count of physics 23 (rev33): store 49 scenes, curves, mini corpus, full corpus.
rem Amber 23.09.2026 (console): "Zapuskay nochnoy raschet, offlaynovo zapuskay, ty ne perezhivyosh limita".
rem Store recipe: --threads=10 --target=0 --n=3000000; far points x2 (--n=6000000):
rem RC103_point50, ASN16_point10_house, G1S_point25 (Amber 18.09.2026 "dalnim tochkam x2").
rem Chain: every next step runs ONLY when the previous returned 0. Each step: own log + own *_done.txt.
rem Codes of *_done.txt: see tools\CORPUS\scripts\detached_run.ps1 -Decode <code>.
rem PATH is set EXPLICITLY, %PATH% is NOT appended: the user PATH carries a stray double quote
rem ("...\170\DTS\Binn";...) which makes cmd fail to find every program listed after it -- pwsh
rem among them (measured 23.09.2026, logs\env_path.log). pwsh is also called by full path.
setlocal
set PATH=C:\Users\moroz\AppData\Local\Python\pythoncore-3.14-64;C:\Program Files\PowerShell\7;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem;C:\WINDOWS\System32\WindowsPowerShell\v1.0;C:\Program Files\Git\cmd
set PYTHONIOENCODING=utf-8
set PWSH="C:\Program Files\PowerShell\7\pwsh.exe"
set PROBES=D:\BqMoni_Claude\p122\wt\tools\effmaker\probes\build_p140
set LOGS=D:\BqMoni_Claude\p140\logs
cd /d %PROBES%
echo start %date% %time% > %LOGS%\_start.txt

rem ===== step 1a: store, three far scenes, 6 000 000 histories each =====
%PROBES%\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p140\store --threads=10 --target=0 --n=6000000 --only=RC103_point50,ASN16_point10_house,G1S_point25 --dump=D:\BqMoni_Claude\p140\art\dumps\store_far.csv >> %LOGS%\store_far.log 2>> %LOGS%\store_far.err
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\1_store_far_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 1b: store, remaining 46 scenes, 3 000 000 histories each (no --force: dense ones stay) =====
%PROBES%\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p140\store --threads=10 --target=0 --n=3000000 --dump=D:\BqMoni_Claude\p140\art\dumps\store.csv >> %LOGS%\store.log 2>> %LOGS%\store.err
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\2_store_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 2: efficiency curves of all 49 scenes, nodes <Efficiency> of 92 spectra =====
%PROBES%\CorpusEffProbe.exe --dir=D:\BqMoni_Claude\p140\store --spectra=D:\BqMoni_Claude\p122\wt\tools\CORPUS\corpus\spectra --force >> %LOGS%\curves.log 2>> %LOGS%\curves.err
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\3_curves_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 3: run workdir wd_p140 (mk_appwd + check_appwd) =====
%PWSH% -NoProfile -Command "& 'D:\BqMoni_Claude\p140\corpus_arm.ps1' -Stage wd; exit $LASTEXITCODE" >> %LOGS%\wd.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\4_wd_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 4: corpus run, SMALL base (mini.csv) =====
%PWSH% -NoProfile -Command "& 'D:\BqMoni_Claude\p140\corpus_arm.ps1' -Stage mini; exit $LASTEXITCODE" >> %LOGS%\mini.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\5_mini_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 5: corpus run, FULL corpus =====
%PWSH% -NoProfile -Command "& 'D:\BqMoni_Claude\p140\corpus_arm.ps1' -Stage full; exit $LASTEXITCODE" >> %LOGS%\full.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\6_full_done.txt
if not "%RC%"=="0" goto fail

echo ALL OK %date% %time% > %LOGS%\ALL_DONE.txt
goto end
:fail
echo CHAIN STOPPED at RC=%RC% %date% %time% > %LOGS%\CHAIN_FAILED.txt
:end
echo finish %date% %time% > %LOGS%\_finish.txt