import sqlite3, math, os
db = r"C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\matdb.sqlite"
con = sqlite3.connect("file:" + db + "?mode=ro", uri=True)
def table(mat):
    rows = con.execute("select energy_kev, yield_rel from scint_electron_light_yield where material=? order by energy_kev", (mat,)).fetchall()
    return rows
def interp(tab, e):
    if e <= tab[0][0]: return tab[0][1]
    if e >= tab[-1][0]: return tab[-1][1]
    for i in range(1, len(tab)):
        if e <= tab[i][0]:
            e0,r0 = tab[i-1]; e1,r1 = tab[i]
            t = (math.log(e)-math.log(e0))/(math.log(e1)-math.log(e0))
            return r0 + (r1-r0)*t
def apparent(tab, e1, e2):
    L = lambda e: interp(tab,e)*e
    target = L(e1)+L(e2)
    lo, hi = (e1+e2)*0.5, (e1+e2)*1.5
    for _ in range(60):
        mid = 0.5*(lo+hi)
        if L(mid) < target: lo = mid
        else: hi = mid
    return 0.5*(lo+hi)
NaI = [(10.0,1.0519),(20.0,1.1074),(30.0,1.0891),(32.0,1.0851),(33.0,1.0829),(34.0,1.0635),(35.0,1.0661),(36.0,1.0703),(40.0,1.0797),(50.0,1.0804),(60.0,1.0723),(81.0,1.0546),(100.0,1.0436),(122.0,1.0351),(200.0,1.0215),(356.0,1.0136),(661.657,1.0076),(1000.0,1.0048),(1173.0,1.0039),(1332.0,1.0032),(1408.0,1.0030),(2614.0,1.0009)]
CsI = [(10.0,1.0054),(20.0,1.0974),(30.0,1.0903),(32.0,1.0875),(33.0,1.0857),(34.0,1.0649),(35.0,1.0654),(36.0,1.0686),(37.0,1.0616),(38.0,1.0647),(40.0,1.0688),(50.0,1.0762),(60.0,1.0727),(81.0,1.0588),(100.0,1.0478),(122.0,1.0386),(200.0,1.0227),(356.0,1.0131),(661.657,1.0067),(1000.0,1.0041),(1332.5,1.0029)]
for mat, photon in (("CsI:Tl", CsI), ("NaI:Tl", NaI)):
    el = table(mat)
    print(mat, "electron rows:", len(el), "range", el[0], el[-1])
    for (a,b) in ((661.657,661.657),(201.83,306.78),(1173.2,1332.5)):
        print("  %s+%s = %.2f : electron(matdb) %.2f (%+.2f) ; photon(FsaLightScale) %.2f (%+.2f)" % (a,b,a+b, apparent(el,a,b), apparent(el,a,b)-(a+b), apparent(photon,a,b), apparent(photon,a,b)-(a+b)))
    # r values
    print("  r_el(662)=%.4f r_el(1324)=%.4f ; r_ph(662)=%.4f r_ph(1324)=%.4f" % (interp(el,661.657), interp(el,1323.3), interp(photon,661.657), interp(photon,1323.3)))
