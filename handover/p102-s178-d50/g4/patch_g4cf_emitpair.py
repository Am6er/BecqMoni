# -*- coding: utf-8 -*-
# П102: ключ `emitpair E1:E2[,E1:E2…]` — условные вероятности ЭМИССИИ P(E2 | E1) по событиям распада
# (без детектора: кванты РДМ записываются в стек и гасятся, как у `angcorr`). Ставится там же, где `iontag`.
import io, sys
p = sys.argv[1]
s = io.open(p, encoding="utf-8", newline="").read()
nl = "\r\n" if "\r\n" in s else "\n"


def rep(old, new, count=1):
    global s
    old = old.replace("\n", nl); new = new.replace("\n", nl)
    assert s.count(old) == count, (old[:80], s.count(old))
    s = s.replace(old, new)


rep("""    std::map<int, long> gEmit;                              // бин 0.01 кэВ → квантов
    long gEmitEvents = 0;
""", """    std::map<int, long> gEmit;                              // бин 0.01 кэВ → квантов
    long gEmitEvents = 0;

    // Режим `emitpair` (П102): пары энергий эмиссии; на событие — есть ли квант E1
    // (±0.35 кэВ) и есть ли при нём квант E2. Кванты гасятся — детектор не нужен,
    // 20 млн распадов идут минуты. Печать `EMITPAIR E1 E2 n1 n12 P`.
    bool gEmitPairMode = false;
    std::vector<std::pair<double, double>> gEmitPairs;
    std::vector<long> gEmitPairN1, gEmitPairN12;
""")

rep("""    /// `iontag`: подписи окон и спектр эмиссии этого события — в локальные карты.
    void Tag()
    {
        ++fEmitEvents;
        for (auto& g : fGammas)
        {
            ++fEmit[int(g.first * 100.0 + 0.5)];
        }
""", """    /// `iontag`: подписи окон и спектр эмиссии этого события — в локальные карты.
    void Tag()
    {
        ++fEmitEvents;
        for (auto& g : fGammas)
        {
            ++fEmit[int(g.first * 100.0 + 0.5)];
        }

        if (gEmitPairMode)
        {
            if (fPairN1.size() < gEmitPairs.size())
            {
                fPairN1.assign(gEmitPairs.size(), 0);
                fPairN12.assign(gEmitPairs.size(), 0);
            }

            for (size_t k = 0; k < gEmitPairs.size(); ++k)
            {
                bool has1 = false, has2 = false;
                int i1 = -1;
                for (size_t q = 0; q < fGammas.size(); ++q)
                {
                    if (!has1 && std::fabs(fGammas[q].first - gEmitPairs[k].first) < 0.35) { has1 = true; i1 = int(q); }
                }

                if (!has1) { continue; }
                for (size_t q = 0; q < fGammas.size(); ++q)
                {
                    if (int(q) != i1 && std::fabs(fGammas[q].first - gEmitPairs[k].second) < 0.35) { has2 = true; break; }
                }

                ++fPairN1[k];
                if (has2) { ++fPairN12[k]; }
            }
        }
""")

rep("""        for (auto& kv : fEmit) { gEmit[kv.first] += kv.second; }
        fEmit.clear();
        gEmitEvents += fEmitEvents;
        fEmitEvents = 0;
    }
""", """        for (auto& kv : fEmit) { gEmit[kv.first] += kv.second; }
        fEmit.clear();
        gEmitEvents += fEmitEvents;
        fEmitEvents = 0;
        if (gEmitPairMode)
        {
            if (gEmitPairN1.size() < gEmitPairs.size())
            {
                gEmitPairN1.assign(gEmitPairs.size(), 0);
                gEmitPairN12.assign(gEmitPairs.size(), 0);
            }

            for (size_t k = 0; k < fPairN1.size(); ++k)
            {
                gEmitPairN1[k] += fPairN1[k];
                gEmitPairN12[k] += fPairN12[k];
                fPairN1[k] = 0;
                fPairN12[k] = 0;
            }
        }
    }
""")

rep("""    std::vector<std::map<std::string, long>> fFull;   // окно → множество целиком поглощённых → n
    std::vector<long> fTagTotals;
""", """    std::vector<std::map<std::string, long>> fFull;   // окно → множество целиком поглощённых → n
    std::vector<long> fTagTotals;
    std::vector<long> fPairN1, fPairN12;              // `emitpair`
""")

# печать
rep("""            std::printf("EMITEND\\n");
""", """            std::printf("EMITEND\\n");
            for (size_t k = 0; k < gEmitPairs.size() && k < gEmitPairN1.size(); ++k)
            {
                std::printf("EMITPAIR %.3f %.3f n1=%ld n12=%ld P=%.6f\\n", gEmitPairs[k].first, gEmitPairs[k].second,
                            gEmitPairN1[k], gEmitPairN12[k],
                            gEmitPairN1[k] > 0 ? double(gEmitPairN12[k]) / gEmitPairN1[k] : 0.0);
            }
""")

# стек: в режиме emitpair кванты и электроны гасятся (как angcorr), записываются кванты РДМ
rep("""            // `iontag` только записывает квант — перенос идёт как обычно.
            return gAngCorrMode ? fKill : fUrgent;
        }
""", """            // `iontag` только записывает квант — перенос идёт как обычно;
            // `emitpair` без детектора — квант гасится, как в `angcorr`.
            return (gAngCorrMode || gEmitPairMode) ? fKill : fUrgent;
        }

        if (gEmitPairMode && track->GetDefinition()->GetPDGEncoding() < 1000000000)
        {
            return fKill;      // электроны/нейтрино распада не нужны, ионы (дочерние состояния) — нужны
        }
""")

# разбор ключа
rep("""                           || std::strcmp(argv[base], "fullcarry") == 0 || std::strcmp(argv[base], "iontag") == 0""",
    """                           || std::strcmp(argv[base], "fullcarry") == 0 || std::strcmp(argv[base], "iontag") == 0
                           || (std::strcmp(argv[base], "emitpair") == 0 && argc > base + 1)""")
rep("""        else if (std::strcmp(argv[base], "iontag") == 0)
        {
            // (П102) Подписи состава излучения по окнам `ion` и спектр эмиссии РДМ.
            gTagMode = true;
        }""", """        else if (std::strcmp(argv[base], "iontag") == 0)
        {
            // (П102) Подписи состава излучения по окнам `ion` и спектр эмиссии РДМ.
            gTagMode = true;
        }
        else if (std::strcmp(argv[base], "emitpair") == 0)
        {
            // (П102) Условные эмиссии P(E2 | E1) по списку пар E1:E2,…; включает iontag (спектр EMIT), детектор гасится.
            gTagMode = true;
            gEmitPairMode = true;
            std::string list(argv[base + 1]);
            size_t pos = 0;
            while (pos < list.size())
            {
                size_t comma = list.find(',', pos);
                std::string item = list.substr(pos, comma == std::string::npos ? std::string::npos : comma - pos);
                size_t colon = item.find(':');
                if (colon != std::string::npos)
                {
                    gEmitPairs.emplace_back(std::atof(item.substr(0, colon).c_str()), std::atof(item.substr(colon + 1).c_str()));
                }

                if (comma == std::string::npos) { break; }
                pos = comma + 1;
            }

            base++;
        }""")
rep("""    //      | g4cf iontag [scene <файл>] ion <Z> <A> <N> <окна…>   — состав излучения событий окон + спектр эмиссии РДМ""",
    """    //      | g4cf iontag [scene <файл>] ion <Z> <A> <N> <окна…>   — состав излучения событий окон + спектр эмиссии РДМ
    //      | g4cf emitpair E1:E2[,…] [scene <файл>] ion <Z> <A> <N> [окна…] — P(E2 | E1) по эмиссии, детектор гасится""")
io.open(p, "w", encoding="utf-8", newline="").write(s)
print("ok")
