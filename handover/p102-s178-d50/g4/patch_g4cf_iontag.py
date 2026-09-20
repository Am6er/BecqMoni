# -*- coding: utf-8 -*-
# П102 (S178): правка tools/g4cf/g4cf.cc — ключ `iontag` (подписи состава излучения по окнам + спектр эмиссии РДМ).
import io, sys

p = sys.argv[1]
s = io.open(p, encoding="utf-8", newline="").read()
nl = "\r\n" if "\r\n" in s else "\n"


def rep(old, new, count=1):
    global s
    old = old.replace("\n", nl)
    new = new.replace("\n", nl)
    assert s.count(old) == count, (old[:80], s.count(old))
    s = s.replace(old, new)


# 1. шапка
rep("""// Сборка — build_g4cf.bat рядом""",
"""// Ключ `iontag` (П102, `S178`, 18.09.2026) — СОСТАВ ИЗЛУЧЕНИЯ СОБЫТИЯ по окнам
// ион-режима: у каждого события, чей полный вклад попал в окно `ion`, кванты
// РДМ (гаммы и рентген атомной релаксации — у них процесс-создатель
// «Radioactivation», как у режима `angcorr`) складываются в подпись
// «E1+E2+…» (кэВ, 0.1), и подписи считаются на окно (строки `TAG`/`TAGSIG`).
// Отдельно — спектр ЭМИССИИ квантов РДМ на все события (строки `EMIT`, шаг
// 0.01 кэВ): это линии и рентген самой поставки Geant4 (PhotonEvaporation +
// EADL) в квантах на распад, без детектора. Зачем: окно полной энергии
// 1123.2 (779+344) у Geant4 держит на 10 % больше модели при согласии линий —
// подпись говорит, ЧЕМ окно заполнено, вместо гадания по схеме; спектр
// эмиссии даёт доли Kα/Kβ Geant4 против `nucdb` (тип X) напрямую.
//
// Сборка — build_g4cf.bat рядом""")

# 2. глобалы и включения
rep("""    bool gAngCorrMode = false;
""", """    bool gAngCorrMode = false;

    // Режим `iontag` (П102, `S178`): подписи состава излучения по окнам и
    // спектр эмиссии РДМ. Накопители потоков сливаются в общие под замком в
    // EndOfRunAction рабочего потока (до печати мастера — тот же порядок, что у
    // G4Accumulable::Merge).
    bool gTagMode = false;
    std::mutex gTagMutex;
    std::vector<std::map<std::string, long>> gTagCounts;   // по окнам
    std::vector<long> gTagTotals;
    std::map<int, long> gEmit;                              // бин 0.01 кэВ → квантов
    long gEmitEvents = 0;
""")
rep("""#include <map>
""", """#include <algorithm>
#include <map>
#include <mutex>
""")

# 3. EventAction: локальные накопители и слив
rep("""    void Add(double edepKev) { fEdepKev += edepKev; }

    void AddGamma(double eKev, const G4ThreeVector& dir) { fGammas.emplace_back(eKev, dir); }

private:
    RunAction* fRun;
    double fEdepKev = 0.0;
    std::vector<std::pair<double, G4ThreeVector>> fGammas;
};""", """    void Add(double edepKev) { fEdepKev += edepKev; }

    void AddGamma(double eKev, const G4ThreeVector& dir) { fGammas.emplace_back(eKev, dir); }

    /// `iontag`: подписи окон и спектр эмиссии этого события — в локальные карты.
    void Tag()
    {
        ++fEmitEvents;
        for (auto& g : fGammas)
        {
            ++fEmit[int(g.first * 100.0 + 0.5)];
        }

        for (size_t i = 0; i < gWindows.size(); ++i)
        {
            if (std::fabs(fEdepKev - gWindows[i]) >= kHalfWindowKev)
            {
                continue;
            }

            std::vector<double> es;
            for (auto& g : fGammas)
            {
                if (g.first > 1.0)
                {
                    es.push_back(g.first);
                }
            }

            std::sort(es.begin(), es.end(), [](double a, double b) { return a > b; });
            std::string sig;
            char buf[32];
            for (double e : es)
            {
                std::snprintf(buf, sizeof buf, "%.1f", e);
                if (!sig.empty()) { sig += "+"; }
                sig += buf;
            }

            if (sig.empty()) { sig = "-"; }
            ++fTags[i][sig];
            ++fTagTotals[i];
        }
    }

    /// Слив локальных карт в общие (под замком); зовётся из EndOfRunAction рабочего.
    void Flush()
    {
        std::lock_guard<std::mutex> lock(gTagMutex);
        if (gTagCounts.size() < gWindows.size())
        {
            gTagCounts.resize(gWindows.size());
            gTagTotals.resize(gWindows.size(), 0);
        }

        for (size_t i = 0; i < fTags.size(); ++i)
        {
            for (auto& kv : fTags[i]) { gTagCounts[i][kv.first] += kv.second; }
            gTagTotals[i] += fTagTotals[i];
            fTags[i].clear();
            fTagTotals[i] = 0;
        }

        for (auto& kv : fEmit) { gEmit[kv.first] += kv.second; }
        fEmit.clear();
        gEmitEvents += fEmitEvents;
        fEmitEvents = 0;
    }

    void PrepareTag()
    {
        fTags.assign(gWindows.size(), {});
        fTagTotals.assign(gWindows.size(), 0);
    }

private:
    RunAction* fRun;
    double fEdepKev = 0.0;
    std::vector<std::pair<double, G4ThreeVector>> fGammas;
    std::vector<std::map<std::string, long>> fTags;
    std::vector<long> fTagTotals;
    std::map<int, long> fEmit;
    long fEmitEvents = 0;
};

namespace
{
    // Указатель на EventAction своего потока — чтобы EndOfRunAction рабочего слил его карты.
    G4ThreadLocal EventAction* gThreadEvent = nullptr;
}""")

rep("""        fRun->Count(fEdepKev);
    }
""", """        fRun->Count(fEdepKev);
        if (gTagMode)
        {
            Tag();
        }
    }
""")

# 4. StackingAction: собирать кванты РДМ и в режиме iontag (не гася)
rep("""        if (!gAngCorrMode || track->GetParentID() <= 0)
        {
            return fUrgent;
        }

        if (track->GetDefinition() == G4Gamma::GammaDefinition())
        {
            const G4VProcess* creator = track->GetCreatorProcess();
            if (creator != nullptr && creator->GetProcessName().find("adioactiv") != std::string::npos)
            {
                fEvent->AddGamma(track->GetKineticEnergy() / keV, track->GetMomentumDirection());
            }

            return fKill;
        }
""", """        if ((!gAngCorrMode && !gTagMode) || track->GetParentID() <= 0)
        {
            return fUrgent;
        }

        if (track->GetDefinition() == G4Gamma::GammaDefinition())
        {
            const G4VProcess* creator = track->GetCreatorProcess();
            if (creator != nullptr && creator->GetProcessName().find("adioactiv") != std::string::npos)
            {
                fEvent->AddGamma(track->GetKineticEnergy() / keV, track->GetMomentumDirection());
            }

            // `iontag` только записывает квант — перенос идёт как обычно.
            return gAngCorrMode ? fKill : fUrgent;
        }

        if (gTagMode)
        {
            return fUrgent;
        }
""")

# 5. RunAction: слив рабочего и печать мастера
rep("""    void EndOfRunAction(const G4Run* run) override
    {
        G4AccumulableManager::Instance()->Merge();
        if (!IsMaster())
        {
            return;
        }
""", """    void EndOfRunAction(const G4Run* run) override
    {
        G4AccumulableManager::Instance()->Merge();
        if (!IsMaster())
        {
            if (gTagMode && gThreadEvent != nullptr)
            {
                gThreadEvent->Flush();
            }

            return;
        }
""")

rep("""            std::printf("HISTEND\\n");
        }

        std::fflush(stdout);
    }
""", """            std::printf("HISTEND\\n");
        }

        if (gTagMode)
        {
            std::lock_guard<std::mutex> lock(gTagMutex);
            std::printf("EMITBEGIN events=%ld bin_kev=0.01\\n", gEmitEvents);
            for (auto& kv : gEmit)
            {
                std::printf("EMIT %.2f %ld\\n", kv.first * 0.01, kv.second);
            }

            std::printf("EMITEND\\n");
            for (size_t i = 0; i < gTagCounts.size(); ++i)
            {
                std::printf("TAG window=%.3f total=%ld distinct=%zu\\n", gWindows[i], gTagTotals[i],
                            gTagCounts[i].size());
                std::vector<std::pair<std::string, long>> rows(gTagCounts[i].begin(), gTagCounts[i].end());
                std::sort(rows.begin(), rows.end(),
                          [](const std::pair<std::string, long>& a, const std::pair<std::string, long>& b)
                          { return a.second > b.second; });
                size_t shown = 0;
                for (auto& row : rows)
                {
                    if (shown++ >= 60) { break; }
                    std::printf("TAGSIG window=%.3f n=%ld sig=%s\\n", gWindows[i], row.second, row.first.c_str());
                }
            }

            std::printf("TAGEND\\n");
        }

        std::fflush(stdout);
    }
""")

# 6. Actions::Build — регистрация потока
rep("""        auto event = new EventAction(run);
        SetUserAction(event);
""", """        auto event = new EventAction(run);
        if (gTagMode)
        {
            event->PrepareTag();
            gThreadEvent = event;
        }

        SetUserAction(event);
""")

# 7. main: ключ
rep("""    while (argc > base && (std::strcmp(argv[base], "vacuum") == 0 || std::strcmp(argv[base], "corr") == 0
                           || std::strcmp(argv[base], "killesc") == 0 || std::strcmp(argv[base], "killcarry") == 0
                           || std::strcmp(argv[base], "fullcarry") == 0""",
    """    while (argc > base && (std::strcmp(argv[base], "vacuum") == 0 || std::strcmp(argv[base], "corr") == 0
                           || std::strcmp(argv[base], "killesc") == 0 || std::strcmp(argv[base], "killcarry") == 0
                           || std::strcmp(argv[base], "fullcarry") == 0 || std::strcmp(argv[base], "iontag") == 0""")
rep("""        else if (std::strcmp(argv[base], "killesc") == 0)
        {
            gKillEscape = true;
        }""", """        else if (std::strcmp(argv[base], "iontag") == 0)
        {
            // (П102) Подписи состава излучения по окнам `ion` и спектр эмиссии РДМ.
            gTagMode = true;
        }
        else if (std::strcmp(argv[base], "killesc") == 0)
        {
            gKillEscape = true;
        }""")
rep("""    //      | g4cf ionhist <шаг_кэВ> <E_max_кэВ> [scene <файл>] ion <Z> <A> <N> [окна…]""",
    """    //      | g4cf ionhist <шаг_кэВ> <E_max_кэВ> [scene <файл>] ion <Z> <A> <N> [окна…]
    //      | g4cf iontag [scene <файл>] ion <Z> <A> <N> <окна…>   — состав излучения событий окон + спектр эмиссии РДМ""")
rep("""        std::fprintf(stderr, "g4cf [vacuum] [corr] [seed <N>] [ionhist <bin_keV> <Emax_keV>] [killesc] [killcarry] [fullcarry] [scene <file>] mono <E_keV> <N>\"""",
    """        std::fprintf(stderr, "g4cf [vacuum] [corr] [seed <N>] [ionhist <bin_keV> <Emax_keV>] [iontag] [killesc] [killcarry] [fullcarry] [scene <file>] mono <E_keV> <N>\"""")
rep("""        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld killesc=%d killcarry=%d fullcarry=%d histbin=%.3f histbins=%d\\n",
                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed,
                    gKillEscape ? 1 : 0, gKillCarry ? 1 : 0, gFullCarry ? 1 : 0, gHistBinKev, gHistBins);""",
    """        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld killesc=%d killcarry=%d fullcarry=%d histbin=%.3f histbins=%d iontag=%d\\n",
                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed,
                    gKillEscape ? 1 : 0, gKillCarry ? 1 : 0, gFullCarry ? 1 : 0, gHistBinKev, gHistBins, gTagMode ? 1 : 0);""")

io.open(p, "w", encoding="utf-8", newline="").write(s)
print("ok", len(s))
