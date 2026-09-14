# П30 (S126/S127): чтение дампов прямых узлов ResponseRowDumpProbe — по узлу: Σ строки (полная эффективность),
# канал peak (ФЭП), Σ esc_se, Σ esc_de, Σ compton; плечи против контроля в процентах.
import csv, io, os, sys, glob
sys.stdout.reconfigure(encoding='utf-8')
d = r'C:\Users\moroz\bqp30\handover\p30-cleaner-s\rowdump'
def load(path):
    nodes = {}
    for r in csv.DictReader(io.open(path, encoding='utf-8-sig', newline=''), delimiter=';'):
        k = float(r['node_kev'])
        n = nodes.setdefault(k, dict(total=0.0, peak=0.0, se=0.0, de=0.0, compton=0.0, xray=0.0, lx=0.0))
        n['total'] += float(r['total']); n['peak'] += float(r['peak']); n['se'] += float(r['esc_se'])
        n['de'] += float(r['esc_de']); n['compton'] += float(r['compton']); n['xray'] += float(r['esc_xray']); n['lx'] += float(r['esc_lx'])
    return nodes
def pct(a, b): return '%+.2f %%' % (100 * (a / b - 1)) if b > 0 else '—'
for scene in ['G1S_p25', 'ASN16']:
    print('=== сцена %s ===' % scene)
    for tag, arms in [('hi', ['seed1', 'pos1off0', 'pos1off1']), ('lo40_60', ['seed1', 'rayl2']), ('lo100_200', ['seed1', 'rayl2'])]:
        cp = os.path.join(d, '%s_%s_ctrl.csv' % (scene, tag))
        if not os.path.exists(cp): print('  нет', cp); continue
        ctrl = load(cp)
        for k in sorted(ctrl):
            c = ctrl[k]
            print('  узел %7.1f кэВ ctrl: Σ=%.4e ФЭП=%.4e (%.2f %% строки) SE=%.3e (%.3f %%) DE=%.3e (%.3f %%) compton=%.4e' % (k, c['total'], c['peak'], 100 * c['peak'] / c['total'], c['se'], 100 * c['se'] / c['total'], c['de'], 100 * c['de'] / c['total'], c['compton']))
            for arm in arms:
                ap = os.path.join(d, '%s_%s_%s.csv' % (scene, tag, arm))
                if not os.path.exists(ap): print('     нет', ap); continue
                a = load(ap)[k]
                print('     %-9s Σ %s  ФЭП %s  SE %s  DE %s  compton %s  (SE %.3f %%, DE %.3f %% строки)' % (arm, pct(a['total'], c['total']), pct(a['peak'], c['peak']), pct(a['se'], c['se']), pct(a['de'], c['de']), pct(a['compton'], c['compton']), 100 * a['se'] / a['total'], 100 * a['de'] / a['total']))
