# -*- coding: utf-8 -*-
import io
p = r"D:\BqMoni_Claude\p122\wt\tools\estar\estar.py"
s = io.open(p, encoding="utf-8", newline="").read()
assert "\r\n" in s
old1 = u"""    Брэггу, только когда состав ввели руками. Разница не всегда мелкая: у
    иодида цезия и иодида натрия правило Брэгга даёт табличное значение до
    сотых (553.10 и 452.01 против 553.1 и 452.0), а у германата висмута —
    523.5 против табличных 534.1, и это уже 0.4 % в пробеге.
    \"\"\""""
new1 = u"""    Брэггу, только когда состав ввели руками. Правило Брэгга с потенциалами
    «элемента в соединении» даёт табличное значение с точностью округления
    таблицы: CsI 553.10, NaI 452.01, BGO 534.0 против 553.1, 452.0 и 534.1
    (`AMBER56`; до 22.09.2026 у BGO выходило 523.5 — кислороду шёл элементный
    потенциал 95 эВ вместо 106, см. `bragg_potential`).
    \"\"\""""
old2 = u"""    ln I = Σ wᵢ (Z/A)ᵢ ln Iᵢ / Σ wᵢ (Z/A)ᵢ, причём для элементов ТЯЖЕЛЕЕ неона
    ESTAR берёт не табличное I элемента, а 1.13·I: в соединении электроны
    связаны сильнее, чем в чистом веществе.
    \"\"\"
    weights = dict(db.execute("select z, atomic_weight from xcom_elements"))
    pot = dict(db.execute("select z, potential_ev from estar_element_potential"))
    zav = 0.0
    acc = 0.0
    for z, w in fractions.items():
        za = z / weights[z]
        value = pot[z] if z < 10 else 1.13 * pot[z]
        zav += w * za
        acc += w * za * math.log(value)
    return math.exp(acc / zav)"""
new2 = u"""    ln I = Σ wᵢ (Z/A)ᵢ ln Iᵢ / Σ wᵢ (Z/A)ᵢ. Потенциал элемента В СОЕДИНЕНИИ
    ESTAR берёт двумя способами: у элементов ЛЕГЧЕ неона (Z < 10) — табличное
    ICRU 37 «элемент в соединении, конденсированная фаза» (`POTCON`: H 19.2,
    C 81, N 82, O 106, F 112 — столбец `potential_cond_ev`), у остальных —
    1.13·I чистого элемента (`POTH`): в соединении электроны связаны сильнее,
    чем в чистом веществе.

    ⛔ (`AMBER56`, П123 22.09.2026) До того при Z < 10 брался ЭЛЕМЕНТНЫЙ
    `potential_ev` (`POTH`: C 78, O 95): BGO по Брэггу 523.52 против табличных
    534.1, вода 69.0 против 75.0. Та же правка — в
    `BecquerelMonitor/EfficiencyMaker/EstarCalculator.cs` (`BraggPotential`);
    оба счёта обязаны давать одно число (сверка — `EstarPotentialProbe` против
    драйвера `estar_bragg.py` полосы П123, журнал
    `handover/handover-2026-09-22-p123-materials-estar.md`). Газовую фазу
    (`POTGAS`) ни тот, ни этот счёт не различают — признака фазы у вещества нет.
    \"\"\"
    weights = dict(db.execute("select z, atomic_weight from xcom_elements"))
    pot = dict(db.execute("select z, potential_ev from estar_element_potential"))
    cond = dict(db.execute("select z, potential_cond_ev from estar_element_potential"
                           " where potential_cond_ev is not null"))
    zav = 0.0
    acc = 0.0
    for z, w in fractions.items():
        za = z / weights[z]
        value = cond.get(z, pot[z]) if z < 10 else 1.13 * pot[z]
        zav += w * za
        acc += w * za * math.log(value)
    return math.exp(acc / zav)"""
crlf = lambda t: t.replace("\n", "\r\n")
old1, new1, old2, new2 = map(crlf, (old1, new1, old2, new2))
assert s.count(old1) == 1, "old1 %d" % s.count(old1)
assert s.count(old2) == 1, "old2 %d" % s.count(old2)
s = s.replace(old1, new1).replace(old2, new2)
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("ok")
