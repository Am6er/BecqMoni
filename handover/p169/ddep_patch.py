# -*- coding: utf-8 -*-
"""Правка интенсивностей опорных линий `nucdb.decay_radiations` к DDEP — AMBER132, П169.

Решение Amber 28.09.2026 вопросником, дословно: «DDEP — правка базы твоей рукой
(Рекомендую)». Полоса П169 базу НЕ пишет: этот скрипт готовит правку, запись —
рукой Amber (`--write`).

Источник: DDEP, файлы Nucleide-Lara LNHB — http://www.lnhb.fr/nuclides/<нуклид>.lara.txt,
скачаны 28.09.2026 в `ddep/` рядом со скриптом (редакция каждого нуклида — в выдаче,
столбец «ref»). Сверка — `ddep_compare.py` (опорная линия: γ или K-рентген с
I(DDEP) >= 1 %, линии дочерей самого распада; дублеты — суммой в окне ±0.6 кэВ).

Что правится. Нуклид берётся, если хоть одна его опорная линия расходится с DDEP
больше `--sigma` совместных неопределённостей (умолчание 2). У взятого нуклида
правятся ВСЕ его опорные линии, сопоставленные ОДНОЙ строке базы и одной линии
DDEP, — чтобы линии одного нуклида были из одной оценки; дублеты не правятся
(у суммы нет одного числа DDEP). Пишутся три столбца: `intensity_num` (его читает
приложение — `FsaSampleLibrary`), `intensity` (текст DDEP как есть) и
`intensity_unc` (погрешность в последних знаках, как у ENSDF). Строки не
добавляются и не удаляются.

Заслон: перед записью у каждой строки сверяется нынешнее `intensity_num` с тем,
что видела сверка; не сошлось хоть у одной — отказ без записи. Всё — одной
транзакцией.

    python ddep_patch.py --db <копия nucdb.sqlite> [--sigma 2] [--write]

Без `--write` — только печать того, что будет сделано, и оценка сдвига
активностей (A ∝ 1/I: отношение Σ I(было) / Σ I(станет) по опорным γ-линиям
нуклида и по самой сильной из них).
"""
import argparse
import hashlib
import math
import os
import sqlite3
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ddep_compare  # noqa: E402


def decimals(text):
    text = text.strip().upper().split("E")[0]
    return len(text.split(".")[1]) if "." in text else 0


def ensdf_style(value_text, unc_text):
    """('10.34','0.07') -> ('10.34','7'); ('85','0.3') -> ('85.0','3')."""
    if "E" in value_text.upper() or not unc_text:
        return value_text, ""
    dec = max(decimals(value_text), decimals(unc_text))
    value = float(value_text)
    unc = float(unc_text)
    return ("%.*f" % (dec, value)), str(int(round(unc * 10 ** dec)))


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--db", required=True)
    ap.add_argument("--sigma", type=float, default=2.0)
    ap.add_argument("--write", action="store_true")
    args = ap.parse_args()

    db = os.path.abspath(args.db)
    c = sqlite3.connect(db)
    rows = ddep_compare.compare(c)

    flagged = sorted(set(r["nuclide"] for r in rows
                         if isinstance(r["sigma"], float) and abs(r["sigma"]) > args.sigma))
    print("нуклидов с опорной линией дальше %.1fσ от DDEP: %d — %s"
          % (args.sigma, len(flagged), ", ".join(flagged)))

    plan = []
    for r in rows:
        if r["nuclide"] not in flagged or not r["dr_pks"] or "," in r["dr_pks"]:
            continue
        if not r["ddep_text"]:
            continue
        if abs(r["ours"] - r["ddep"]) <= 1e-12 * max(1.0, r["ddep"]):
            continue
        text, unc = ensdf_style(r["ddep_text"], r["ddep_unc_text"])
        plan.append((r, int(r["dr_pks"]), text, unc))

    # Итог Kβ (`KB`) — сумма разложения K'β1 + K'β2: приложение при полном
    # разложении берёт его (`KSeriesRule`), но итог в базе обязан с ним сходиться,
    # иначе правило «полное ли разложение» и прочие читатели увидят разные числа
    for n in flagged:
        kb = [(r, pk, t, u) for (r, pk, t, u) in plan
              if r["nuclide"] == n and r["kind"] in ("XK'b1", "XK'b2")]
        if len(kb) != 2:
            continue
        nid = kb[0][0]["nucid"]
        tot = c.execute("select dr_pk, intensity_num from decay_radiations where parent_nucid = $n"
                        " and type_a = 'X' and trim(type_c) = 'KB'" + ddep_compare.chains.LEVEL_CLAUSE,
                        {ddep_compare.chains.LEVEL_PARAM: nid}).fetchall()
        if len(tot) != 1:
            continue
        dec = max(decimals(t) for (r, pk, t, u) in kb)
        value = sum(float(t) for (r, pk, t, u) in kb)
        unc = math.sqrt(sum((r["ddep_unc"] or 0.0) ** 2 for (r, pk, t, u) in kb))
        text = "%.*f" % (dec, value)
        fake = dict(nuclide=n, line="KB (сумма K'β)", kind="XKB", ours=tot[0][1], ddep=value,
                    ref=kb[0][0]["ref"])
        plan.append((fake, tot[0][0], text, str(int(round(unc * 10 ** dec)))))

    print("строк к правке: %d" % len(plan))
    for r, pk, text, unc in plan:
        print("  %-8s %-14s dr_pk %-6d %10.5g -> %-9s (±%s в последних знаках)  %+6.2f %%  %s"
              % (r["nuclide"], r["line"], pk, r["ours"], text, unc or "—",
                 -100.0 * (1.0 - r["ddep"] / r["ours"]), r["ref"]))

    # оценка сдвига активности: A ∝ 1/I
    print("\nоценка сдвига активности нуклида (A ∝ 1/I), по опорным γ-линиям:")
    for n in flagged:
        g = [r for r in rows if r["nuclide"] == n and r["kind"] == "g" and isinstance(r["ours"], float)]
        patched = set(pk for (r, pk, t, u) in plan if r["nuclide"] == n)
        if not g:
            print("  %-8s γ-опорных нет (правится рентген)" % n)
            continue
        old = sum(r["ours"] for r in g)
        new = sum(r["ddep"] if r["dr_pks"] and int(r["dr_pks"].split(",")[0]) in patched else r["ours"]
                  for r in g)
        top = max(g, key=lambda r: r["ours"])
        top_new = top["ddep"] if top["dr_pks"] and int(top["dr_pks"].split(",")[0]) in patched else top["ours"]
        print("  %-8s по сумме γ-опорных %+.2f %%, по самой сильной (%s) %+.2f %%"
              % (n, 100.0 * (old / new - 1.0), top["line"], 100.0 * (top["ours"] / top_new - 1.0)))

    if not args.write:
        print("\nсухой прогон: база не тронута (%s, sha256 %s)" % (db, sha256(db)))
        return 0

    before = sha256(db)
    cur = c.cursor()
    cur.execute("begin")
    for r, pk, text, unc in plan:
        have = cur.execute("select intensity_num from decay_radiations where dr_pk = ?", (pk,)).fetchone()
        if have is None or abs(have[0] - r["ours"]) > 1e-12 * max(1.0, abs(r["ours"])):
            cur.execute("rollback")
            print("ОТКАЗ: dr_pk %d — в базе %r, сверка видела %r; ничего не записано" % (pk, have, r["ours"]))
            return 2
        cur.execute("update decay_radiations set intensity_num = ?, intensity = ?, intensity_unc = ?"
                    " where dr_pk = ?", (float(text), text, unc or None, pk))
    cur.execute("commit")
    c.close()
    print("\nзаписано строк %d в %s\n  sha256 до    %s\n  sha256 после %s"
          % (len(plan), db, before, sha256(db)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
