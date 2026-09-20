# -*- coding: utf-8 -*-
# П102: iontag v3 — сводка окна по МНОЖЕСТВУ целиком поглощённых квантов (TAGFULL, без среза), TAGSIG остаётся со срезом 60.
import io, sys
p = sys.argv[1]
s = io.open(p, encoding="utf-8", newline="").read()
nl = "\r\n" if "\r\n" in s else "\n"


def rep(old, new, count=1):
    global s
    old = old.replace("\n", nl); new = new.replace("\n", nl)
    assert s.count(old) == count, (old[:80], s.count(old))
    s = s.replace(old, new)


rep("""            if (sig.empty()) { sig = "-"; }
            ++fTags[i][sig];
            ++fTagTotals[i];
""", """            if (sig.empty()) { sig = "-"; }
            ++fTags[i][sig];
            ++fTagTotals[i];

            // Сводка по множеству ЦЕЛИКОМ поглощённых квантов — ключ короткий,
            // печатается без среза (подписей с хвостом L-рентгена — сотни).
            std::string full;
            for (auto& e : es)
            {
                if (!e.second) { continue; }
                std::snprintf(buf, sizeof buf, "%.1f", e.first);
                if (!full.empty()) { full += "+"; }
                full += buf;
            }

            if (full.empty()) { full = "-"; }
            ++fFull[i][full];
""")
rep("""        for (size_t i = 0; i < fTags.size(); ++i)
        {
            for (auto& kv : fTags[i]) { gTagCounts[i][kv.first] += kv.second; }
            gTagTotals[i] += fTagTotals[i];
            fTags[i].clear();
            fTagTotals[i] = 0;
        }
""", """        if (gFullCounts.size() < gWindows.size())
        {
            gFullCounts.resize(gWindows.size());
        }

        for (size_t i = 0; i < fTags.size(); ++i)
        {
            for (auto& kv : fTags[i]) { gTagCounts[i][kv.first] += kv.second; }
            for (auto& kv : fFull[i]) { gFullCounts[i][kv.first] += kv.second; }
            gTagTotals[i] += fTagTotals[i];
            fTags[i].clear();
            fFull[i].clear();
            fTagTotals[i] = 0;
        }
""")
rep("""        fTags.assign(gWindows.size(), {});
        fTagTotals.assign(gWindows.size(), 0);
""", """        fTags.assign(gWindows.size(), {});
        fFull.assign(gWindows.size(), {});
        fTagTotals.assign(gWindows.size(), 0);
""")
rep("""    std::vector<std::map<std::string, long>> fTags;
    std::vector<long> fTagTotals;
""", """    std::vector<std::map<std::string, long>> fTags;
    std::vector<std::map<std::string, long>> fFull;   // окно → множество целиком поглощённых → n
    std::vector<long> fTagTotals;
""")
rep("""    std::vector<std::map<std::string, long>> gTagCounts;   // по окнам
""", """    std::vector<std::map<std::string, long>> gTagCounts;   // по окнам
    std::vector<std::map<std::string, long>> gFullCounts;  // по окнам: множество целиком поглощённых
""")
old_print = """                size_t shown = 0;
                for (auto& row : rows)
                {
                    if (shown++ >= 60) { break; }
                    std::printf("TAGSIG window=%.3f n=%ld sig=%s\\n", gWindows[i], row.second, row.first.c_str());
                }
            }
"""
new_print = """                size_t shown = 0;
                for (auto& row : rows)
                {
                    if (shown++ >= 60) { break; }
                    std::printf("TAGSIG window=%.3f n=%ld sig=%s\\n", gWindows[i], row.second, row.first.c_str());
                }

                if (i < gFullCounts.size())
                {
                    std::vector<std::pair<std::string, long>> fulls(gFullCounts[i].begin(), gFullCounts[i].end());
                    std::sort(fulls.begin(), fulls.end(),
                              [](const std::pair<std::string, long>& a, const std::pair<std::string, long>& b)
                              { return a.second > b.second; });
                    for (auto& row : fulls)
                    {
                        std::printf("TAGFULL window=%.3f n=%ld full=%s\\n", gWindows[i], row.second, row.first.c_str());
                    }
                }
            }
"""
rep(old_print, new_print)
rep("""// «E1+E2+…» (кэВ, 0.1), и подписи считаются на окно (строки `TAG`/`TAGSIG`);""",
    """// «E1+E2+…» (кэВ, 0.1), и подписи считаются на окно (строки `TAG`/`TAGSIG`,
// первые 60 по числу; сводка по МНОЖЕСТВУ целиком поглощённых — `TAGFULL`, без среза);""")
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("ok")
