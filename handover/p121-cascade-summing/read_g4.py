# -*- coding: utf-8 -*-
"""P121: читатель логов g4cf для CF линий арбитра.

CF_G4(E) = p_G4(E) * eps_pk(E, mono) / eps_app_pure(E, ion), где
  p_G4      — квантов линии на распад по спектру эмиссии Geant4 (EMIT, сумма бинов в ±0.5 кэВ от E),
  eps_pk    — пиковая эффективность моно-прогона (RESULT window=E eps=),
  eps_app_pure — доля распадов, у которых в окне E лежит событие с подписью «только E целиком»
              (TAGFULL window=E full=E), то есть без влёта сумм соседей.
Рядом печатается и «грязная» кажущаяся (RESULT window=E eps= ион-прогона — с влётом сумм).
sigma — биномиальная по числу отсчётов окна; p_G4 и eps_pk считаются точными (их шум на порядок меньше).
"""
import math, re, sys, os
g4 = r"D:\BqMoni_Claude\p121\g4"

def parse(path):
    d = {"windows": {}, "full": {}, "emit": {}, "decays": None, "any": None, "eps_total": None}
    if not os.path.exists(path):
        return None
    for line in open(path, encoding="utf-8", errors="replace"):
        m = re.match(r"RESULT decays=(\d+)", line)
        if m: d["decays"] = int(m.group(1)); continue
        m = re.match(r"RESULT any=(\d+) eps_total=([0-9.eE+-]+)", line)
        if m: d["any"] = int(m.group(1)); d["eps_total"] = float(m.group(2)); continue
        m = re.match(r"RESULT window=([0-9.]+) counts=(\d+) eps=([0-9.eE+-]+)", line)
        if m: d["windows"][float(m.group(1))] = (int(m.group(2)), float(m.group(3))); continue
        m = re.match(r"TAGFULL window=([0-9.]+) n=(\d+) full=(\S+)", line)
        if m:
            w = float(m.group(1)); d["full"].setdefault(w, {})[m.group(3)] = int(m.group(2)); continue
        m = re.match(r"EMIT ([0-9.]+) (\d+)", line)
        if m: d["emit"][float(m.group(1))] = int(m.group(2)); continue
    return d

def emission(d, e, half=0.5):
    n = sum(c for k, c in d["emit"].items() if abs(k - e) <= half)
    return n / d["decays"]

def report(scene_tag, ion_tag, lines, mono_tag_fmt):
    ion = parse(os.path.join(g4, ion_tag + ".log"))
    if ion is None or ion["decays"] is None:
        print("  %s: ион-лог не готов" % ion_tag); return
    print("  %s: распадов %d" % (ion_tag, ion["decays"]))
    for e in lines:
        mono = parse(os.path.join(g4, (mono_tag_fmt % e) + ".log"))
        w = ion["windows"].get(e)
        if w is None:
            print("    окно %.1f: нет" % e); continue
        counts, eps_dirty = w
        full = ion["full"].get(e, {})
        key = "%.1f" % e
        n_pure = full.get(key, 0)
        p = emission(ion, e)
        eps_pure = n_pure / ion["decays"]
        sig_rel = 1.0 / math.sqrt(n_pure) if n_pure > 0 else float("nan")
        sum_in = (counts - n_pure) / counts if counts else float("nan")
        line = "    E=%.1f: p_G4=%.5f  окно %d (eps_app=%.5e), чисто-%s %d (eps_pure=%.5e), влёт/прочее в окне %.2f %%" % (
            e, p, counts, eps_dirty, key, n_pure, eps_pure, 100 * sum_in)
        if mono is not None and mono["decays"]:
            mw = mono["windows"].get(e)
            eps_pk = mw[1] if mw else float("nan")
            cf_pure = p * eps_pk / eps_pure if eps_pure > 0 else float("nan")
            cf_dirty = p * eps_pk / eps_dirty if eps_dirty > 0 else float("nan")
            line += "\n           моно: eps_pk=%.5e eps_T=%.5e (N=%d) -> CF_G4(чисто)=%.4f ± %.4f ; CF_G4(окно с влётом)=%.4f ; вынос 1-1/CF=%.4f" % (
                eps_pk, mono["eps_total"], mono["decays"], cf_pure, cf_pure * sig_rel, cf_dirty, 1 - 1 / cf_pure if cf_pure else float("nan"))
        else:
            line += "\n           моно-лог %s не готов" % (mono_tag_fmt % e)
        print(line)
        # верхние подписи окна
        top = sorted(full.items(), key=lambda kv: -kv[1])[:4]
        print("           подписи окна: " + ", ".join("%s:%d" % kv for kv in top))

if __name__ == "__main__":
    print("== контактная сцена (2 мм от крышки), Ba-133 ==")
    report("contact", "ion_ba133_contact", [356.0, 81.0, 302.9, 383.8, 276.4, 31.0, 30.6, 35.0], "mono_%.1f_contact")
    print("== контактная сцена, Cs-137 (контроль: пар нет, CF обязан быть 1) ==")
    report("contact", "ion_cs137_contact", [661.7], "mono_%.1f_contact")
    print("== контактная сцена + диск ПЭ, Na-22 ==")
    report("contact_pe", "ion_na22_contact_pe", [1274.5, 511.0], "mono_%.1f_contact_pe")
    print("== P5, Ba-133 ==")
    report("p5", "ion_ba133_p5", [356.0, 81.0, 302.9, 383.8, 31.0, 30.6], "mono_%.1f_p5")
    print("== P5 + диск ПЭ, Na-22 ==")
    report("p5_pe", "ion_na22_p5_pe", [1274.5, 511.0], "mono_%.1f_p5_pe")

if __name__ == "__main__":
    print("== контактная сцена, Mn-54 (контроль без β: одна гамма, Cr K 5.4 кэВ до кристалла не доходит) ==")
    report("contact", "ion_mn54_contact", [834.8], "mono_%.1f_contact")
