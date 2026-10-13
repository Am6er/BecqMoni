# -*- coding: utf-8 -*-
"""P121: Geant4 arbiter chain for AMBER60/AMBER61 (ion + mono runs on two scenes). ASCII+CRLF .cmd."""
bat = r"D:\BqMoni_Claude\p121\run_g4cf_p121.bat"
g4 = r"D:\BqMoni_Claude\p121\g4"
seed = 20260922
runs = []
def ion(tag, scene, z, a, n, windows):
    runs.append((tag, "iontag seed %d scene %s\\%s.scene ion %d %d %d %s" % (seed, g4, scene, z, a, n, " ".join(windows))))
def mono(tag, scene, e, n):
    runs.append((tag, "seed %d scene %s\\%s.scene mono %s %d" % (seed, g4, scene, e, n)))

# A. contact scene, Ba-133 (bare point), Cs-137 control
ion("ion_ba133_contact", "G1S_contact", 56, 133, 5000000, ["356.0", "81.0", "302.9", "383.8", "276.4", "31.0", "30.6", "35.0"])
for e, n in (("356.0", 2000000), ("81.0", 1000000), ("302.9", 1000000), ("383.8", 1000000), ("276.4", 1000000),
             ("31.0", 1000000), ("30.6", 1000000), ("35.0", 1000000)):
    mono("mono_%s_contact" % e, "G1S_contact", e, n)
ion("ion_cs137_contact", "G1S_contact", 55, 137, 2000000, ["661.7", "32.2", "31.8"])
mono("mono_661.7_contact", "G1S_contact", "661.7", 1000000)
# C. contact scene with PE disc, Na-22
ion("ion_na22_contact_pe", "G1S_contact_pe", 11, 22, 5000000, ["1274.5", "511.0"])
mono("mono_1274.5_contact_pe", "G1S_contact_pe", "1274.5", 2000000)
mono("mono_511.0_contact_pe", "G1S_contact_pe", "511.0", 1000000)
# D. P5 scene, Ba-133
ion("ion_ba133_p5", "G1S_point5", 56, 133, 20000000, ["356.0", "81.0", "302.9", "383.8", "31.0", "30.6"])
for e, n in (("356.0", 5000000), ("81.0", 2000000), ("302.9", 2000000), ("383.8", 2000000), ("31.0", 2000000), ("30.6", 2000000)):
    mono("mono_%s_p5" % e, "G1S_point5", e, n)
# E. P5 scene with PE disc, Na-22
ion("ion_na22_p5_pe", "G1S_point5_pe", 11, 22, 20000000, ["1274.5", "511.0"])
mono("mono_1274.5_p5_pe", "G1S_point5_pe", "1274.5", 5000000)
mono("mono_511.0_p5_pe", "G1S_point5_pe", "511.0", 2000000)

lines = ["@echo off", "rem P121 22.09.2026: Geant4 arbiter for AMBER60 (Na-22 511 back-to-back) and AMBER61 (Ba-133 K-series).",
         "cd /d %s" % g4, "echo start %%date%% %%time%% > %s\\chain_start.txt" % g4]
for tag, args in runs:
    lines.append('call "%s" %s > %s\\%s.log 2> %s\\%s.err' % (bat, args, g4, tag, g4, tag))
    lines.append("echo %s exit %%errorlevel%% %%date%% %%time%% >> %s\\chain_status.txt" % (tag, g4))
lines.append("echo exit %%errorlevel%% %%date%% %%time%% > %s\\chain_done.txt" % g4)
text = "\r\n".join(lines) + "\r\n"
text.encode("ascii")
open(r"D:\BqMoni_Claude\p121\g4_chain.cmd", "wb").write(text.encode("ascii"))
print(text)
print("runs:", len(runs))
