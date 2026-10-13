# П184 (30.09.2026), S197: угловое распределение когерентного — наш F(x,Z) из matdb
# (таблица epdl_form_factor, F² линейна по t = x², как у ScatteringData) против
# фита Каллена у Geant4 (G4RayleighAngularGenerator, option4 без поляризации).
# Мерка на сечениях, без переноса. База — только чтение (mode=ro).
# python coh_angle.py <matdb.sqlite> <G4RayleighAngularGenerator.cc> [E_keV ...]
import sys, re, math, sqlite3

db, cc = sys.argv[1], sys.argv[2]
energies = [float(x) for x in sys.argv[3:]] or [32.0, 60.0]
INV = 8.065543937e6  # x[1/см] = INV * E[кэВ] * sin(θ/2), как ScatteringData.InverseCmPerKev

src = open(cc, encoding='latin-1').read()
PP = {}
for k in range(9):
    m = re.search(r'PP%d\[101\]\s*=\s*\{(.*?)\};' % k, src, re.S)
    body = re.sub(r'//[^\n]*', '', m.group(1))
    PP[k] = [float(v) for v in body.replace('\n', ' ').split(',') if v.strip()]
    assert len(PP[k]) == 101, (k, len(PP[k]))

con = sqlite3.connect('file:' + db.replace('\\', '/') + '?mode=ro', uri=True)


def ff_table(z):
    rows = con.execute('select x_percm, ff from epdl_form_factor where z=? order by x_percm', (z,)).fetchall()
    return [r[0] for r in rows], [r[1] for r in rows]


def f2_ours(tab, x):
    xs, fs = tab
    t = x * x
    ts = [a * a for a in xs]
    if t <= ts[0]:
        return fs[0] ** 2
    lo, hi = 0, len(ts) - 1
    if t >= ts[hi]:
        return 0.0  # за краем таблицы розыгрыш не ходит (FormFactorTop)
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if ts[mid] <= t:
            lo = mid
        else:
            hi = mid
    a, b = fs[lo] ** 2, fs[hi] ** 2
    return a + (b - a) * (t - ts[lo]) / (ts[hi] - ts[lo])


def f2_g4(z, x):
    s = 0.0
    for i in range(3):
        s += PP[i][z] * (1.0 + PP[3 + i][z] * x * x) ** (-PP[6 + i][z])
    return s


def moments(f2, e, nodes=20000):
    k = INV * e
    # интеграл по cos: dσ/dcos ∝ F²·(1+cos²)/2; сетка по u = 1−cos с узлами гуще у 0
    tot = mc = back = fwd10 = 0.0
    prev = None
    for j in range(nodes + 1):
        u = 2.0 * (j / nodes) ** 3
        c = 1.0 - u
        x = k * math.sqrt(max(0.0, u / 2.0))
        v = f2(x) * 0.5 * (1.0 + c * c)
        if prev is not None:
            du = u - prev[0]
            vm = 0.5 * (v + prev[1])
            cm = 1.0 - 0.5 * (u + prev[0])
            tot += vm * du
            mc += vm * cm * du
            if cm < 0.0:
                back += vm * du
            if cm > math.cos(math.radians(10.0)):
                fwd10 += vm * du
        prev = (u, v)
    return tot, mc / tot, back / tot, fwd10 / tot


print('Z  E_кэВ  ∫(наш)/∫(G4)  <cos> наш / G4   доля назад наш / G4   доля <10° наш / G4   F(0) наш / G4')
for e in energies:
    for z in (1, 6, 8, 9, 13, 17, 19, 53, 55):
        tab = ff_table(z)
        a = moments(lambda x: f2_ours(tab, x), e)
        b = moments(lambda x: f2_g4(z, x), e)
        print(f'{z:2d} {e:5.1f}  {a[0] / b[0]:.4f}   {a[1]:.4f} / {b[1]:.4f}   {a[2]:.4f} / {b[2]:.4f}   '
              f'{a[3]:.4f} / {b[3]:.4f}   {tab[1][0]:.3f} / {math.sqrt(f2_g4(z, 0.0)):.3f}')
