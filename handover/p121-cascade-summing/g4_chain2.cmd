@echo off
rem P121: clean control for the arbiter CF method - Mn-54 (EC, one gamma 834.8, Cr K 5.4 keV cannot reach the crystal): CF must be 1.
cd /d D:\BqMoni_Claude\p121\g4
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" iontag seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene ion 25 54 2000000 834.8 > D:\BqMoni_Claude\p121\g4\ion_mn54_contact.log 2> D:\BqMoni_Claude\p121\g4\ion_mn54_contact.err
echo ion_mn54_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain2_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene mono 834.8 1000000 > D:\BqMoni_Claude\p121\g4\mono_834.8_contact.log 2> D:\BqMoni_Claude\p121\g4\mono_834.8_contact.err
echo mono_834.8_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain2_status.txt
echo exit %errorlevel% %date% %time% > D:\BqMoni_Claude\p121\g4\chain2_done.txt
