# -*- coding: utf-8 -*-
# П123 (AMBER53): ожидания проб зазора — воздух засева теперь 4 элемента NIST (C, N, O, Ar), а не 2 (N2 O1).
import io

CRLF = "\r\n"


def patch(path, pairs):
    s = io.open(path, encoding="utf-8", newline="").read()
    crlf = CRLF in s
    for a, b in pairs:
        if crlf:
            a, b = a.replace("\n", CRLF), b.replace("\n", CRLF)
        assert s.count(a) == 1, (path, a)
        s = s.replace(a, b)
    with io.open(path, "w", encoding="utf-8", newline="") as f:
        f.write(s)
    print("ok", path)


base = r"D:\BqMoni_Claude\p122\wt\tools\effmaker\probes"
patch(base + r"\GapDefaultProbe.cs", [
    (u"""            Check("элементов", 2, g.Gap.Fractions.Count);
            Check("есть азот", true, g.Gap.Fractions.ContainsKey(7));
            Check("есть кислород", true, g.Gap.Fractions.ContainsKey(8));""",
     u"""            // (`AMBER53`, П123 22.09.2026) Воздух засева — состав NIST «Air, dry
            // (near sea level)»: C, N, O, Ar — четыре элемента, а не два
            // формулы `N2 O1`; аргон — признак того, что умолчание НОВОЕ.
            Check("элементов", 4, g.Gap.Fractions.Count);
            Check("есть азот", true, g.Gap.Fractions.ContainsKey(7));
            Check("есть кислород", true, g.Gap.Fractions.ContainsKey(8));
            Check("есть аргон", true, g.Gap.Fractions.ContainsKey(18));"""),
    (u"""            Check("без <Gap>: элементов", 2, a.Geometry.Gap.Fractions.Count);""",
     u"""            Check("без <Gap>: элементов", 4, a.Geometry.Gap.Fractions.Count);"""),
    (u"""            Check("пустой <Gap>: элементов", 2, b.Geometry.Gap.Fractions.Count);""",
     u"""            Check("пустой <Gap>: элементов", 4, b.Geometry.Gap.Fractions.Count);"""),
    (u"""            Check("пустой шаблон: элементов", 2, g.Gap.Fractions.Count);""",
     u"""            Check("пустой шаблон: элементов", 4, g.Gap.Fractions.Count);"""),
    (u"""            Check("шаблон из XML: элементов", 2, g3.Gap.Fractions.Count);""",
     u"""            Check("шаблон из XML: элементов", 4, g3.Gap.Fractions.Count);"""),
])
patch(base + r"\GapProbeAmber1.cs", [
    (u"""            Check("состав наполнителя пережил запись", 2, back.Gap.Fractions.Count);""",
     u"""            // (`AMBER53`, П123 22.09.2026) Воздух засева — четыре элемента NIST (C, N, O, Ar).
            Check("состав наполнителя пережил запись", 4, back.Gap.Fractions.Count);"""),
])
