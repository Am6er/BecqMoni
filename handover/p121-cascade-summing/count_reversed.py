import sqlite3
c = sqlite3.connect(r"file:C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/BecquerelMonitor/schemedb.sqlite?mode=ro", uri=True)
rows = c.execute("select multipolarity, mixing_ratio, intensity_ppm from g4_gamma where multipolarity >= 100").fetchall()
def order(x): return x // 2
rev = {}; rev_pos = {}; zero_rev = 0; same = 0
for m, d, i in rows:
    hi, lo = m // 100, m % 100
    if not (2 <= hi <= 16 and 2 <= lo <= 16): continue
    if order(hi) > order(lo):
        if d is not None and d != 0.0:
            rev[m] = rev.get(m, 0) + 1
            if i is not None and i > 0: rev_pos[m] = rev_pos.get(m, 0) + 1
        else:
            zero_rev += 1
    elif order(hi) == order(lo):
        same += 1
print("смешанных кодов всего:", len(rows))
print("обратный порядок (старшая первой), δ≠0:", sum(rev.values()), dict(sorted(rev.items())))
print("  из них с intensity_ppm>0:", sum(rev_pos.values()), dict(sorted(rev_pos.items())))
print("обратный порядок, δ=0 (не трогаются):", zero_rev)
print("равный порядок (E2+M2 и т.п.):", same)
