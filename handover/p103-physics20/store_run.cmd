@echo off
rem P103 18-19.09.2026 (physics 20, key elmix ON by default): store count, recipe --threads=10 --target=0 --n=3000000,
rem far scenes (RC103_point50, ASN16_point10_house, G1S_point25) --n=6000000 (Amber 18.09.2026: far points x2).
cd /d D:\BqMoni_Claude\p103\wt\tools\effmaker\probes\build_p103
echo start %date% %time% > D:\BqMoni_Claude\p103\count_start.txt
D:\BqMoni_Claude\p103\wt\tools\effmaker\probes\build_p103\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p103\store --threads=10 --target=0 --n=6000000 --only=RC103_point50,ASN16_point10_house,G1S_point25 --dump=D:\BqMoni_Claude\p103\art\dumps\store_far.csv >> D:\BqMoni_Claude\p103\count.log 2>> D:\BqMoni_Claude\p103\count.err
echo far exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p103\count_far_done.txt
D:\BqMoni_Claude\p103\wt\tools\effmaker\probes\build_p103\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p103\store --threads=10 --target=0 --n=3000000 --dump=D:\BqMoni_Claude\p103\art\dumps\store.csv >> D:\BqMoni_Claude\p103\count.log 2>> D:\BqMoni_Claude\p103\count.err
echo exit %errorlevel% %date% %time% > D:\BqMoni_Claude\p103\count_done.txt
