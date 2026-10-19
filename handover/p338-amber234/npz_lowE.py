"""Их матрица (npz, ОИСН-10) в мягкой зоне: доли пика, K-вылета иода, континуума на узлах."""
import sys
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
z = np.load('D:/BqMoni_Claude/p338/g4/response_matrix_marinelli1l_oisn10.npz')
E = z['E_nodes']; C = z['counts_edep'].astype(float); N = z['n_primary'].astype(float); x = z['bin_centers_keV']
print('бин 0..1 кэВ пуст у всех узлов:', bool((C[:, 0] == 0).all()))
want = [16, 18, 20, 25, 28, 30, 32, 32.8, 33.5, 36, 40, 46.5, 50, 59.5, 66, 74.97, 84.986, 93.347]
print(' E узла | n_prim  | ε_tot   | пик[E-1,E+1) | K-выход [E-33.6,E-27.8) | доля пика | доля K-вых | континуум ниже K-вых | выше E+1')
for w in want:
    i = int(np.argmin(abs(E - w))); e = E[i]; r = C[i] / N[i]
    tot = r.sum()
    pk = r[(x >= e - 1) & (x < e + 1)].sum()
    ke = r[(x >= e - 33.6) & (x < e - 27.8)].sum() if e > 33.17 else 0.0
    lo = r[(x < e - 33.6)].sum() if e > 33.17 else r[x < e - 1].sum()
    mid = r[(x >= e - 27.8) & (x < e - 1)].sum() if e > 33.17 else 0.0
    print(f'{e:7.3f} | {N[i]:8.2e} | {tot:.4e} | {pk:.4e} | {ke:.4e} | {pk/tot:.4f} | {ke/tot:.4f} | {lo/tot:.4f} (+{mid/tot:.4f} между) | {r[x >= e + 1].sum()/tot:.5f}')
# K-escape relative to peak vs E (classic check: escape/peak ratio decreasing with E)
print()
print('отношение K-вылет/пик по узлам выше K-края (их узлы 33.5–100 кэВ):')
for i in np.flatnonzero((E > 33.3) & (E < 101)):
    e = E[i]; r = C[i] / N[i]
    pk = r[(x >= e - 1) & (x < e + 1)].sum(); ke = r[(x >= e - 33.6) & (x < e - 27.8)].sum()
    print(f'  {e:7.3f}: {ke/pk:.4f}', end='')
print()
