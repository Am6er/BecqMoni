@echo off
rem P161 24.09.2026 -- night count of physics 24 (rev34): store 49 scenes, curves, FSA showcase store, mini corpus, full corpus.
rem Amber 24.09.2026 (console): "Nachinay nochnoy progon". Branch p147-physics24, worktree D:\BqMoni_Claude\p147\wt, HEAD d3bd492b.
rem Physics by DEFAULTS (physics 24: pairth=1, out-of-cone next-event). --pairth=0 is NOT passed (ablation "as physics 23").
rem Store recipe: --threads=10 --target=0 --n=3000000; far points x2 (--n=6000000):
rem RC103_point50, ASN16_point10_house, G1S_point25 (Amber 18.09.2026 "dalnim tochkam x2").
rem Corpus rebuild from Amber's library (step 0) was done BEFORE launch, in the session (see journal P161).
rem Chain: steps 1a,1b,2 gate everything after them (code 0 required). Step 3 (showcase store) does NOT gate
rem steps 4-6: the showcase has its own store and scenes, corpus runs do not read it.
rem Each step: own log + own *_done.txt. Codes: tools\CORPUS\scripts\detached_run.ps1 -Decode <code>.
rem PATH is set EXPLICITLY, %PATH% is NOT appended (stray double quote in the user PATH, P140 finding (a)).
setlocal
set PATH=C:\Users\moroz\AppData\Local\Python\pythoncore-3.14-64;C:\Program Files\PowerShell\7;C:\WINDOWS\system32;C:\WINDOWS;C:\WINDOWS\System32\Wbem;C:\WINDOWS\System32\WindowsPowerShell\v1.0;C:\Program Files\Git\cmd
set PYTHONIOENCODING=utf-8
set PWSH="C:\Program Files\PowerShell\7\pwsh.exe"
set WT=D:\BqMoni_Claude\p147\wt
set PROBES=D:\BqMoni_Claude\p147\wt\tools\effmaker\probes\build_rel_p161
set STORE=D:\BqMoni_Claude\p161\store
set LOGS=D:\BqMoni_Claude\p161\logs
set ART=D:\BqMoni_Claude\p161\art
set PARTIAL=0
cd /d %PROBES%
echo start %date% %time% > %LOGS%\_start.txt

rem ===== step 1a: store, three far scenes, 6 000 000 histories each =====
%PROBES%\CorpusMatrixProbe.exe --dir=%STORE% --threads=10 --target=0 --n=6000000 --only=RC103_point50,ASN16_point10_house,G1S_point25 --dump=%ART%\dumps\store_far.csv >> %LOGS%\store_far.log 2>> %LOGS%\store_far.err
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\1_store_far_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 1b: store, remaining 46 scenes, 3 000 000 histories each (no --only, no --force: dense ones stay) =====
%PROBES%\CorpusMatrixProbe.exe --dir=%STORE% --threads=10 --target=0 --n=3000000 --dump=%ART%\dumps\store.csv >> %LOGS%\store.log 2>> %LOGS%\store.err
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\2_store_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 2: efficiency curves of all 49 scenes, nodes <Efficiency> of 92 spectra =====
%PROBES%\CorpusEffProbe.exe --dir=%STORE% --spectra=%WT%\tools\CORPUS\corpus\spectra --force >> %LOGS%\curves.log 2>> %LOGS%\curves.err
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\3_curves_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 3: FSA showcase store (rebuild_store.ps1 -Force, own 2-node control inside); does NOT gate =====
%PWSH% -NoProfile -Command "& 'D:\BqMoni_Claude\p161\corpus_arm.ps1' -Stage show; exit $LASTEXITCODE" >> %LOGS%\show.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\4_show_done.txt
if not "%RC%"=="0" set PARTIAL=1

rem ===== step 4: run workdir wd_p161 (mk_appwd + check_appwd) =====
%PWSH% -NoProfile -Command "& 'D:\BqMoni_Claude\p161\corpus_arm.ps1' -Stage wd; exit $LASTEXITCODE" >> %LOGS%\wd.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\5_wd_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 5: corpus run, SMALL base (mini.csv) -> out_rev34_mini =====
%PWSH% -NoProfile -Command "& 'D:\BqMoni_Claude\p161\corpus_arm.ps1' -Stage mini; exit $LASTEXITCODE" >> %LOGS%\mini.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\6_mini_done.txt
if not "%RC%"=="0" goto fail

rem ===== step 6: corpus run, FULL corpus -> out_rev34_full =====
%PWSH% -NoProfile -Command "& 'D:\BqMoni_Claude\p161\corpus_arm.ps1' -Stage full; exit $LASTEXITCODE" >> %LOGS%\full.log 2>&1
set RC=%errorlevel%
echo exit %RC% %date% %time% > %LOGS%\7_full_done.txt
if not "%RC%"=="0" goto fail

if "%PARTIAL%"=="1" goto partial
echo ALL OK %date% %time% > %LOGS%\ALL_DONE.txt
goto end
:partial
echo CORPUS OK, SHOWCASE FAILED (see 4_show_done.txt, art\showcase_store.log) %date% %time% > %LOGS%\CHAIN_PARTIAL.txt
goto end
:fail
echo CHAIN STOPPED at RC=%RC% %date% %time% > %LOGS%\CHAIN_FAILED.txt
:end
echo finish %date% %time% > %LOGS%\_finish.txt