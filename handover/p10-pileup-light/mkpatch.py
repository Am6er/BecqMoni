# -*- coding: utf-8 -*-
# Собрать версию CorpusFsaProbe.cs = HEAD + ТОЛЬКО правки П10 (без правок П11),
# чтобы распорядителю было чем разводить хунки. Якоря — ASCII.
import os, sys
head_path, cur_path, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
raw = open(head_path, "rb").read()
bom = raw[:3] == b"\xef\xbb\xbf"
s = (raw[3:] if bom else raw).decode("utf-8")
cur = open(cur_path, "rb").read()
cur = (cur[3:] if cur[:3] == b"\xef\xbb\xbf" else cur).decode("utf-8")

def take(start, end):
    i = cur.find(start); assert i >= 0, start
    j = cur.find(end, i); assert j >= 0, end
    return cur[i:j + len(end)]

NL = "\r\n"
edits = [
    # 1. строка использования
    ("    ///                  [--no-pileup] [--no-escape] [--no-background] [--limit=N] [--quiet]" + NL,
     take("    ///                  [--no-pileup] [--no-escape] [--no-background] [--limit=N] [--quiet]" + NL,
          "[--pileup-light=0|1|energy|NaI:Tl|CsI:Tl]") + take("[--pileup-light=0|1|energy|NaI:Tl|CsI:Tl]", NL)[len("[--pileup-light=0|1|energy|NaI:Tl|CsI:Tl]"):]),
    # 2. разбор ключа
    ("                if (a == \"--no-pileup\") { o.PileUp = false; continue; }" + NL,
     take("                if (a == \"--no-pileup\") { o.PileUp = false; continue; }" + NL,
          "                    o.PileUpLight = v == \"0\" || v == \"off\" ? \"0\" : v;" + NL + "                    continue;" + NL + "                }" + NL)),
    # 3. поле Options
    ("            public double AnchorZeroKev = double.NaN;",
     take("            public double AnchorZeroKev = double.NaN;", NL) + take("            public string PileUpLight = null;", NL)),
    # 4. передача в анализатор
    ("            if (!double.IsNaN(o.AnchorZeroKev))" + NL + "            {" + NL + "                analyzer.AnchorZeroKev = o.AnchorZeroKev;" + NL + "            }" + NL + NL + "            return analyzer;" + NL,
     take("            if (!double.IsNaN(o.AnchorZeroKev))" + NL, "            return analyzer;" + NL)),
    # 5. поле Row
    ("            public string AnchorNote = \"\";" + NL,
     take("            public string AnchorNote = \"\";" + NL, "            public string PileUpCurve;" + NL)),
    # 6. заполнение Row
    ("                row.Anchors = result.ScaleAnchors;" + NL,
     take("                row.Anchors = result.ScaleAnchors;" + NL, NL) + take("                row.PileUpCurve = analyzer.PileUpCurveUsed;", NL)),
    # 7. сводка
    ("                                          adcOn, anchored + unanchored);" + NL + "                    }" + NL + "                }" + NL + "            }" + NL,
     take("                                          adcOn, anchored + unanchored);" + NL + "                    }" + NL,
          "                                          anchored + unanchored);" + NL + "                    }" + NL + "                }" + NL + "            }" + NL)),
]
for k, (old, new) in enumerate(edits, 1):
    n = s.count(old)
    assert n == 1, ("anchor %d not unique in HEAD: %d" % (k, n))
    assert new.startswith(old.rstrip(NL)[:40]), ("edit %d: replacement does not start with anchor" % k)
    s = s.replace(old, new)
open(out_path, "wb").write((b"\xef\xbb\xbf" if bom else b"") + s.encode("utf-8"))
print("ok, edits:", len(edits))
