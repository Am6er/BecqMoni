@echo off
rem P121 22.09.2026: Geant4 arbiter for AMBER60 (Na-22 511 back-to-back) and AMBER61 (Ba-133 K-series).
cd /d D:\BqMoni_Claude\p121\g4
echo start %date% %time% > D:\BqMoni_Claude\p121\g4\chain_start.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" iontag seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene ion 56 133 5000000 356.0 81.0 302.9 383.8 276.4 31.0 30.6 35.0 > D:\BqMoni_Claude\p121\g4\ion_ba133_contact.log 2> D:\BqMoni_Claude\p121\g4\ion_ba133_contact.err
echo ion_ba133_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene mono 356.0 2000000 > D:\BqMoni_Claude\p121\g4\mono_356.0_contact.log 2> D:\BqMoni_Claude\p121\g4\mono_356.0_contact.err
echo mono_356.0_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene mono 81.0 1000000 > D:\BqMoni_Claude\p121\g4\mono_81.0_contact.log 2> D:\BqMoni_Claude\p121\g4\mono_81.0_contact.err
echo mono_81.0_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene mono 302.9 1000000 > D:\BqMoni_Claude\p121\g4\mono_302.9_contact.log 2> D:\BqMoni_Claude\p121\g4\mono_302.9_contact.err
echo mono_302.9_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene mono 383.8 1000000 > D:\BqMoni_Claude\p121\g4\mono_383.8_contact.log 2> D:\BqMoni_Claude\p121\g4\mono_383.8_contact.err
echo mono_383.8_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene mono 276.4 1000000 > D:\BqMoni_Claude\p121\g4\mono_276.4_contact.log 2> D:\BqMoni_Claude\p121\g4\mono_276.4_contact.err
echo mono_276.4_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene mono 31.0 1000000 > D:\BqMoni_Claude\p121\g4\mono_31.0_contact.log 2> D:\BqMoni_Claude\p121\g4\mono_31.0_contact.err
echo mono_31.0_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene mono 30.6 1000000 > D:\BqMoni_Claude\p121\g4\mono_30.6_contact.log 2> D:\BqMoni_Claude\p121\g4\mono_30.6_contact.err
echo mono_30.6_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene mono 35.0 1000000 > D:\BqMoni_Claude\p121\g4\mono_35.0_contact.log 2> D:\BqMoni_Claude\p121\g4\mono_35.0_contact.err
echo mono_35.0_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" iontag seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene ion 55 137 2000000 661.7 32.2 31.8 > D:\BqMoni_Claude\p121\g4\ion_cs137_contact.log 2> D:\BqMoni_Claude\p121\g4\ion_cs137_contact.err
echo ion_cs137_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact.scene mono 661.7 1000000 > D:\BqMoni_Claude\p121\g4\mono_661.7_contact.log 2> D:\BqMoni_Claude\p121\g4\mono_661.7_contact.err
echo mono_661.7_contact exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" iontag seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact_pe.scene ion 11 22 5000000 1274.5 511.0 > D:\BqMoni_Claude\p121\g4\ion_na22_contact_pe.log 2> D:\BqMoni_Claude\p121\g4\ion_na22_contact_pe.err
echo ion_na22_contact_pe exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact_pe.scene mono 1274.5 2000000 > D:\BqMoni_Claude\p121\g4\mono_1274.5_contact_pe.log 2> D:\BqMoni_Claude\p121\g4\mono_1274.5_contact_pe.err
echo mono_1274.5_contact_pe exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_contact_pe.scene mono 511.0 1000000 > D:\BqMoni_Claude\p121\g4\mono_511.0_contact_pe.log 2> D:\BqMoni_Claude\p121\g4\mono_511.0_contact_pe.err
echo mono_511.0_contact_pe exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" iontag seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_point5.scene ion 56 133 20000000 356.0 81.0 302.9 383.8 31.0 30.6 > D:\BqMoni_Claude\p121\g4\ion_ba133_p5.log 2> D:\BqMoni_Claude\p121\g4\ion_ba133_p5.err
echo ion_ba133_p5 exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_point5.scene mono 356.0 5000000 > D:\BqMoni_Claude\p121\g4\mono_356.0_p5.log 2> D:\BqMoni_Claude\p121\g4\mono_356.0_p5.err
echo mono_356.0_p5 exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_point5.scene mono 81.0 2000000 > D:\BqMoni_Claude\p121\g4\mono_81.0_p5.log 2> D:\BqMoni_Claude\p121\g4\mono_81.0_p5.err
echo mono_81.0_p5 exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_point5.scene mono 302.9 2000000 > D:\BqMoni_Claude\p121\g4\mono_302.9_p5.log 2> D:\BqMoni_Claude\p121\g4\mono_302.9_p5.err
echo mono_302.9_p5 exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_point5.scene mono 383.8 2000000 > D:\BqMoni_Claude\p121\g4\mono_383.8_p5.log 2> D:\BqMoni_Claude\p121\g4\mono_383.8_p5.err
echo mono_383.8_p5 exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_point5.scene mono 31.0 2000000 > D:\BqMoni_Claude\p121\g4\mono_31.0_p5.log 2> D:\BqMoni_Claude\p121\g4\mono_31.0_p5.err
echo mono_31.0_p5 exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_point5.scene mono 30.6 2000000 > D:\BqMoni_Claude\p121\g4\mono_30.6_p5.log 2> D:\BqMoni_Claude\p121\g4\mono_30.6_p5.err
echo mono_30.6_p5 exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" iontag seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_point5_pe.scene ion 11 22 20000000 1274.5 511.0 > D:\BqMoni_Claude\p121\g4\ion_na22_p5_pe.log 2> D:\BqMoni_Claude\p121\g4\ion_na22_p5_pe.err
echo ion_na22_p5_pe exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_point5_pe.scene mono 1274.5 5000000 > D:\BqMoni_Claude\p121\g4\mono_1274.5_p5_pe.log 2> D:\BqMoni_Claude\p121\g4\mono_1274.5_p5_pe.err
echo mono_1274.5_p5_pe exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
call "D:\BqMoni_Claude\p121\run_g4cf_p121.bat" seed 20260922 scene D:\BqMoni_Claude\p121\g4\G1S_point5_pe.scene mono 511.0 2000000 > D:\BqMoni_Claude\p121\g4\mono_511.0_p5_pe.log 2> D:\BqMoni_Claude\p121\g4\mono_511.0_p5_pe.err
echo mono_511.0_p5_pe exit %errorlevel% %date% %time% >> D:\BqMoni_Claude\p121\g4\chain_status.txt
echo exit %errorlevel% %date% %time% > D:\BqMoni_Claude\p121\g4\chain_done.txt
