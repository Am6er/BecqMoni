# -*- coding: utf-8 -*-
"""Мерка теории ε/β⁺ (ecbeta.py) на E-записях ENSDF, где раздел IB/IE ЕСТЬ — П169.

Читает поставку ENSDF2 (та же, что у import_ensdf.py), берёт E-записи с числами IB>0,
IE>0 и их погрешностями, энергию перехода E0 = Q + E(родителя) − E(уровня) из P- и
L-записей, Z дочери — из nucdb.nuclides, энергии связи — matdb.eadl_binding.
Сравнивает долю β⁺ = IB/(IB+IE) с теорией; «в пределах» — |Δ| ≤ σ доли по DIB, DIE.
Ключ --no-parent-energy — E(родителя) = 0 (как видит читатель приложения, у
которого энергии родителя в базе нет).
"""
import math
import os
import re
import sqlite3
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ecbeta  # noqa: E402

SRC = r"C:\LSRM\NuclideMaster\TCCFCALC\LIB\ENSDF2"
WT = r"D:\BqMoni_Claude\p147\wt\BecquerelMonitor"


def num(t):
    t = t.strip().replace("E+", "E")
    try:
        return float(t)
    except ValueError:
        return None


def ensdf_unc(value, unc):
    value, unc = value.strip(), unc.strip()
    if not re.match(r"^\d+$", unc or ""):
        return None
    mant, _, exp = value.upper().partition("E")
    dec = len(mant.split(".")[1]) if "." in mant else 0
    u = int(unc) * 10.0 ** (-dec)
    return u * 10.0 ** int(exp) if exp else u


def records():
    for name in sorted(os.listdir(SRC)):
        if not name.upper().endswith(".ENX"):
            continue
        ds = None
        for raw in open(os.path.join(SRC, name), encoding="latin-1").read().split("\n"):
            line = raw.rstrip("\r")
            if len(line) < 8:
                continue
            if len(line) > 9 and line[8] == "*" and line[5] == " " and line[7] == " ":
                ds = dict(nucid=line[:5].strip(), dsid=line[9:39].strip(), q=None, ep=None, lev=None)
                if not (ds["nucid"].upper().startswith("290XX") or "FAKE" in ds["dsid"].upper()):
                    records.counter += 1
                    ds["id"] = records.counter
                else:
                    ds["id"] = None
                continue
            if ds is None or line[5] != " " or line[6] != " ":
                continue
            k = line[7]
            if k == "P" and ds["q"] is None:
                ds["q"] = num(line[64:74])
                ds["ep"] = num(line[9:19])
            elif k == "L":
                ds["lev"] = num(line[9:19])
            elif k == "E":
                yield ds, line


records.counter = 0


def app_parent_energies(scheme, nuc):
    """Энергия родителя так, как её может найти читатель приложения по базе:
    период родителя набора совпал с периодом основного состояния (nucdb) — 0;
    иначе — уровень ядра-родителя в ensdf_levels любого набора с тем же периодом
    (±2 %); не нашлось — 0."""
    gs = {}
    for nid, hl in nuc.execute("select nucid, half_life_sec from nuclides where l_seqno = 0"):
        gs[nid.upper()] = hl
    levels = {}
    for nid, e, hl in scheme.execute(
            "select d.nucid, l.energy_kev, l.half_life_sec from ensdf_levels l"
            " join ensdf_datasets d on d.id = l.dataset_id where l.half_life_sec > 0 and l.energy_kev > 0"):
        levels.setdefault(nid.upper(), []).append((e, hl))
    out = {}
    for did, pn, phl in scheme.execute("select id, parent_nucid, parent_hl_sec from ensdf_datasets"):
        e = 0.0
        if pn and phl and phl > 0:
            g = gs.get(pn.upper())
            if not (g and abs(g - phl) <= 0.02 * phl):
                cand = [le for (le, lhl) in levels.get(pn.upper(), []) if abs(lhl - phl) <= 0.02 * phl]
                if cand:
                    e = min(cand)
        out[did] = e
    return out


def main():
    no_parent = "--no-parent-energy" in sys.argv
    app_parent = "--app-parent-energy" in sys.argv
    # --ground-only: только наборы с родителем в ОСНОВНОМ состоянии (E родителя = 0) —
    # ровно те, что берёт читатель приложения (изомер он отсекает `IsomerTail`)
    ground_only = "--ground-only" in sys.argv
    nuc = sqlite3.connect("file:%s\\nucdb.sqlite?mode=ro" % WT, uri=True)
    mat = sqlite3.connect("file:%s\\matdb.sqlite?mode=ro" % WT, uri=True)
    zof = {}
    for nid, z in nuc.execute("select nucid, z from nuclides"):
        zof[nid.upper()] = z
    bind = {}
    for z, sid, b in mat.execute("select z, shell_id, binding_ev from eadl_binding"):
        bind.setdefault(z, {})[sid] = b / 1000.0
    app_pe = {}
    if app_parent:
        scheme = sqlite3.connect("file:%s?mode=ro" % os.path.join(WT, "schemedb.sqlite"), uri=True)
        app_pe = app_parent_energies(scheme, nuc)
    tot = inside1 = inside2 = 0
    ratios = []
    by_z = Counter()
    bad = []
    for ds, line in records():
        ib, ie = num(line[21:29]), num(line[31:39])
        if not ib or not ie or ib <= 0 or ie <= 0:
            continue
        dib, die = ensdf_unc(line[21:29], line[29:31]), ensdf_unc(line[31:39], line[39:41])
        if dib is None or die is None or ds["q"] is None or ds["lev"] is None:
            continue
        if ground_only and (ds["ep"] or 0.0) != 0.0:
            continue
        if app_parent:
            ep = app_pe.get(ds["id"], 0.0)
        else:
            ep = 0.0 if no_parent else (ds["ep"] or 0.0)
        e0 = ds["q"] + ep - ds["lev"]
        m = re.match(r"^(\d+)([A-Za-z]+)$", ds["nucid"])
        if not m:
            continue
        a = int(m.group(1))
        z = zof.get(ds["nucid"].upper())
        if z is None or z not in bind:
            continue
        s_pred = ecbeta.beta_plus_share(z, a, e0, bind[z])
        s_rec = ib / (ib + ie)
        sig = math.hypot(ie * dib, ib * die) / (ib + ie) ** 2
        tot += 1
        d = abs(s_pred - s_rec)
        if d <= sig:
            inside1 += 1
        if d <= 2 * sig:
            inside2 += 1
        r_pred = (1.0 - s_pred) / s_pred if s_pred > 0 else float("inf")
        ratios.append(r_pred / (ie / ib))
        if d > 2 * sig:
            bad.append((d / max(sig, 1e-12), ds["dsid"], ds["lev"], e0, ib, ie, s_rec, s_pred))
    ratios.sort()
    q = lambda f: ratios[int(f * (len(ratios) - 1))]
    print("E-записей с IB>0, IE>0 и погрешностями: %d" % tot)
    print("доля β⁺ в пределах 1σ записи: %d (%.1f %%), в пределах 2σ: %d (%.1f %%)"
          % (inside1, 100.0 * inside1 / tot, inside2, 100.0 * inside2 / tot))
    print("отношение (ε/β⁺)теор / (IE/IB)ENSDF: медиана %.4f, 5 %% %.4f, 25 %% %.4f, 75 %% %.4f, 95 %% %.4f"
          % (q(0.5), q(0.05), q(0.25), q(0.75), q(0.95)))
    within5 = sum(1 for r in ratios if abs(r - 1) <= 0.05)
    within10 = sum(1 for r in ratios if abs(r - 1) <= 0.10)
    print("отношение в пределах ±5 %%: %d (%.1f %%), ±10 %%: %d (%.1f %%)"
          % (within5, 100.0 * within5 / tot, within10, 100.0 * within10 / tot))
    bad.sort(reverse=True)
    print("худшие (Δ/σ, набор, уровень, E0, IB, IE, доля ENSDF, доля теории):")
    for b in bad[:15]:
        print("   %.1f  %-28s %9s %8.1f  %8.4g %8.4g  %.4f  %.4f" % b)


if __name__ == "__main__":
    main()
