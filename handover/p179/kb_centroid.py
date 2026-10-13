# П179: новое kb_ev = центр тяжести K-M и K-N (формула П174) для всех элементов xray_fluorescence.
# kb = (w_KM*E_KM + (w_Kb - w_KM)*E_KN) / w_Kb; E_KM, w_KM — fluorescence_k; w_Kb — xray_fluorescence;
# E_KN — медиана KpB2 nucdb по наборам, элемент узнаётся по KA1 того же набора (допуск 0.03 кэВ).
# Режимы: print (только печать) | write <путь к КОПИИ matdb>
import sqlite3, sys, statistics
root = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor'
mode = sys.argv[1]
src = sys.argv[2] if len(sys.argv) > 2 else root + r'\matdb.sqlite'
m = sqlite3.connect('file:' + src + ('?mode=ro' if mode == 'print' else ''), uri=True)
n = sqlite3.connect('file:' + root + r'\nucdb.sqlite?mode=ro', uri=True)
fk = {r[0]: r for r in m.execute('select z, ka1_ev, kb_ev, kb_weight from fluorescence_k')}
xf = {r[0]: r for r in m.execute('select z, ka1_ev, kb_ev, kb_weight from xray_fluorescence')}
sets = {}
for seq, pid, dt, tc, e in n.execute("select parent_l_seqno, parent_nucid, dec_type, type_c, energy_num from decay_radiations where type_a='X' and type_c in ('KA1','KpB2') and energy_num>0"):
    sets.setdefault((seq, pid, dt), {}).setdefault(tc, []).append(e)
kn = {}
for d in sets.values():
    if 'KA1' not in d or 'KpB2' not in d: continue
    for z, r in fk.items():
        if any(abs(a - r[1] / 1000.0) <= 0.03 for a in d['KA1']):
            kn.setdefault(z, []).extend(d['KpB2'])
            break
rows = []
for z in sorted(xf):
    if z not in fk or z not in kn:
        print('Z=%d: нет K-M (fk) или K-N (nucdb) — не меняется' % z); continue
    ekm, wkm = fk[z][2] / 1000.0, fk[z][3]
    wkb = xf[z][3]
    ekn = statistics.median(kn[z])
    new = (wkm * ekm + (wkb - wkm) * ekn) / wkb
    rows.append((z, xf[z][2] / 1000.0, new, ekm, ekn, wkm, wkb))
print('Z   kb_old   kb_new   Δ,кэВ   E_KM    E_KN    w_KM    w_Kb')
for r in rows:
    print('%3d %8.3f %8.3f %+7.3f %7.3f %7.3f %.4f %.4f' % (r[0], r[1], r[2], r[2] - r[1], r[3], r[4], r[5], r[6]))
if mode == 'write':
    m.executemany('update xray_fluorescence set kb_ev=? where z=?', [(r[2] * 1000.0, r[0]) for r in rows])
    m.commit()
    print('записано строк:', len(rows), 'в', src)
