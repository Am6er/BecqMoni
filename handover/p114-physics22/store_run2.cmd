@echo off
rem P114 19.09.2026 (physics 22, key lbang ON by default): store count, recipe --threads=10 --target=0 --n=3000000,
rem far scenes (RC103_point50, ASN16_point10_house, G1S_point25) --n=6000000 (Amber 18.09.2026: far points x2).
cd /d D:\BqMoni_Claude\p114\wt\tools\effmaker\probes\build_p114
echo start %date% %time% > D:\BqMoni_Claude\p114\count2_start.txt
D:\BqMoni_Claude\p114\wt\tools\effmaker\probes\build_p114\CorpusMatrixProbe.exe --dir=D:\BqMoni_Claude\p114\store --threads=10 --target=0 --n=3000000 --only=G1S_mar1l_oisn06_066_p16,G1S_mar1l_oisn06_066_p24,G1S_mar1l_oisn16_155_p16,G1S_mar1l_oisn16_155_p24,G1S_mar1l_oisn16_160_p16,G1S_mar1l_oisn16_160_p24,G1S_mar1l_oisn16_166_p16,G1S_mar1l_oisn16_166_p24,G1S_mar1l_oisn16_167_p16,G1S_mar1l_oisn16_167_p24,G1S_mar1l_risn379_100_p16,G1S_petri60_oisn06_057_p24,G1S_petri60_oisn06_062_p24,G1S_petri60_oisn06_064_p24,G1S_petri60_oisn06_067_p24,G1S_petri60_oisn10_100_p16,G1S_petri60_oisn16_155_p24,G1S_petri60_oisn16_160_p24,G1S_petri60_oisn16_167_p24,G1S_petri60_risn379_100_p16,G1S_point5,RC103_lu_front,RC103_marinelli05_kcl,RC103_point0 --dump=D:\BqMoni_Claude\p114\art\dumps\store2.csv >> D:\BqMoni_Claude\p114\count2.log 2>> D:\BqMoni_Claude\p114\count2.err
echo exit %errorlevel% %date% %time% > D:\BqMoni_Claude\p114\count2_done.txt
