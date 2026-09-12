# Независимая (питоновская) реализация интеграла Дебертина для сцены J —
# положительный контроль C#-реализации в пробе и проверка восстановления
# сосуда Jodłowski (710 мл) против его формулы (11).
import math, sqlite3, sys

REPO = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8'
con = sqlite3.connect('file:' + REPO.replace('\\', '/') + '/BecquerelMonitor/matdb.sqlite?mode=ro', uri=True)
cur = con.cursor()
NA = 6.02214076e23

def mass_att(z, e_kev, coherent=True):
    """см²/г лог-лог интерполяцией по XCOM (барн/атом → см²/г)."""
    aw = cur.execute('select atomic_weight from xcom_elements where z=?', (z,)).fetchone()[0]
    rows = cur.execute('select energy_ev, coherent_b, incoherent_b, photoelectric_b, pair_nuclear_b, pair_electron_b from xcom_cross_sections where z=? order by energy_ev', (z,)).fetchall()
    e = e_kev * 1000.0
    lo = None
    for i in range(len(rows) - 1):
        if rows[i][0] <= e <= rows[i + 1][0]:
            lo = i
    r0, r1 = rows[lo], rows[lo + 1]
    def tot(r):
        s = r[2] + r[3] + r[4] + r[5] + (r[1] if coherent else 0.0)
        return s * 1e-24 * NA / aw
    t0, t1 = tot(r0), tot(r1)
    f = (math.log(e) - math.log(r0[0])) / (math.log(r1[0]) - math.log(r0[0]))
    return math.exp(math.log(t0) + f * (math.log(t1) - math.log(t0)))

def mu_rho_sio2(e, coherent=True):
    return 0.46743 * mass_att(14, e, coherent) + 0.53257 * mass_att(8, e, coherent)

def jod(e, rho):
    l = math.log(e)
    return math.exp(0.32017 * math.exp(-0.033624 * l * l) * rho)

def debertin(rOut, rIn, hs, top, depth, mu_mm, nr=240, nz=240):
    zP = top + depth
    zc0, zc1 = top, hs
    i0 = imu = 0.0
    def integ(r0, r1, z0, z1, nr, nz):
        nonlocal i0, imu
        dr = (r1 - r0) / nr; dz = (z1 - z0) / nz
        for ir in range(nr):
            r = r0 + (ir + 0.5) * dr
            for iz in range(nz):
                z = z0 + (iz + 0.5) * dz
                dzp = zP - z
                d2 = r * r + dzp * dzp
                ln = math.sqrt(d2)
                tA = 1.0 - rIn / r if r > rIn else 0.0
                ta = (zc0 - z) / dzp; tb = (zc1 - z) / dzp
                tz0, tz1 = min(ta, tb), max(ta, tb)
                lo = max(tA, tz0, 0.0); hi = min(tz1, 1.0)
                inc = (hi - lo) * ln if hi > lo else 0.0
                za = max(0.0, ln - inc)
                w = 2 * math.pi * r * dr * dz / d2
                i0 += w; imu += w * math.exp(-mu_mm * za)
    integ(rIn, rOut, 0.0, hs, nr, nz)
    integ(0.0, rIn, 0.0, top, nr, max(24, nz // 4))
    return i0 / imu

# Сцена J: rOut 61.5, rIn 42.5, hs 97, top 19, глубина точки 39.1
print('E,keV  rho  mu/rho(tot)  Cs_deb(tot)  Cs_deb(nocoh)  Cs_Jod  deb/Jod-1,%  nocoh/Jod-1,%')
for e in (238.6, 338.3, 583.2, 911.2, 1460.8, 2614.5):
    for rho in (0.8, 1.6, 2.4):
        mt = mu_rho_sio2(e, True); mn = mu_rho_sio2(e, False)
        dt = debertin(61.5, 42.5, 97.0, 19.0, 39.1, mt * rho / 10.0)
        dn = debertin(61.5, 42.5, 97.0, 19.0, 39.1, mn * rho / 10.0)
        j = jod(e, rho)
        print('%7.1f %4.1f  %.5f  %.4f  %.4f  %.4f  %6.2f  %6.2f' % (e, rho, mt, dt, dn, j, (dt / j - 1) * 100, (dn / j - 1) * 100))

# Чувствительность к восстановлению сосуда: верхний слой 15/19/25 мм при том же объёме.
print()
print('верх,мм колодец,мм  Cs_deb(238.6, 2.4)  Jod')
for top in (12.0, 15.0, 19.0, 25.0, 30.0):
    # объём 710 мл: 62.08*(hh+0.1+top/10)*10... в мм: annulus area
    ann = math.pi * (61.5**2 - 42.5**2); disc = math.pi * 42.5**2
    hs = (710000.0 - disc * top) / ann
    hh = hs - 1.0 - top
    mt = mu_rho_sio2(238.6, True)
    print('%5.0f %8.1f  %.4f  %.4f' % (top, hh, debertin(61.5, 42.5, hs, top, 39.1, mt * 2.4 / 10.0), jod(238.6, 2.4)))
