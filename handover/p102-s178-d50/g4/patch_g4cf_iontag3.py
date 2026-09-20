# -*- coding: utf-8 -*-
# П102 (S178): iontag v2 — у кванта РДМ в подписи признак `*` полного поглощения (вклад его потомков в кристалл = E ± 0.5 кэВ).
import io, sys
p = sys.argv[1]
s = io.open(p, encoding="utf-8", newline="").read()
nl = "\r\n" if "\r\n" in s else "\n"


def rep(old, new, count=1):
    global s
    old = old.replace("\n", nl); new = new.replace("\n", nl)
    assert s.count(old) == count, (old[:80], s.count(old))
    s = s.replace(old, new)


rep("""// «E1+E2+…» (кэВ, 0.1), и подписи считаются на окно (строки `TAG`/`TAGSIG`).""",
"""// «E1+E2+…» (кэВ, 0.1), и подписи считаются на окно (строки `TAG`/`TAGSIG`);
// квант, чьи потомки оставили в кристалле ровно его энергию (±0.5 кэВ), несёт
// признак `*` — подпись «778.9*+344.3*» и есть событие сумм-пика, «244.7*+121.8+40.2»
// при вкладе 284.2 — комптон-край 122 поверх целого 244, а не сумма 244+Kα2.""")

rep("""    void BeginOfEventAction(const G4Event*) override
    {
        fEdepKev = 0.0;
        fGammas.clear();
    }
""", """    void BeginOfEventAction(const G4Event*) override
    {
        fEdepKev = 0.0;
        fGammas.clear();
        fGammaDep.clear();
        fAncestor.clear();
    }
""")

rep("""    void AddGamma(double eKev, const G4ThreeVector& dir) { fGammas.emplace_back(eKev, dir); }
""", """    void AddGamma(double eKev, const G4ThreeVector& dir) { fGammas.emplace_back(eKev, dir); }

    /// `iontag`: квант РДМ с треком `trackId` — предок самому себе.
    void AddTaggedGamma(int trackId, double eKev, const G4ThreeVector& dir)
    {
        fGammas.emplace_back(eKev, dir);
        fGammaDep.push_back(0.0);
        fAncestor[trackId] = int(fGammas.size()) - 1;
    }

    /// `iontag`: потомок наследует предка-квант родителя (если тот известен).
    void Inherit(int trackId, int parentId)
    {
        auto it = fAncestor.find(parentId);
        if (it != fAncestor.end())
        {
            fAncestor[trackId] = it->second;
        }
    }

    /// `iontag`: вклад шага трека — на счёт его предка-кванта.
    void AddTo(int trackId, double edepKev)
    {
        auto it = fAncestor.find(trackId);
        if (it != fAncestor.end())
        {
            fGammaDep[it->second] += edepKev;
        }
    }
""")

rep("""            std::vector<double> es;
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
""", """            std::vector<std::pair<double, bool>> es;
            for (size_t k = 0; k < fGammas.size(); ++k)
            {
                if (fGammas[k].first > 1.0)
                {
                    bool full = k < fGammaDep.size() && std::fabs(fGammaDep[k] - fGammas[k].first) < kHalfWindowKev;
                    es.emplace_back(fGammas[k].first, full);
                }
            }

            std::sort(es.begin(), es.end(),
                      [](const std::pair<double, bool>& a, const std::pair<double, bool>& b) { return a.first > b.first; });
            std::string sig;
            char buf[32];
            for (auto& e : es)
            {
                std::snprintf(buf, sizeof buf, "%.1f%s", e.first, e.second ? "*" : "");
                if (!sig.empty()) { sig += "+"; }
                sig += buf;
            }
""")

rep("""    std::vector<std::pair<double, G4ThreeVector>> fGammas;
    std::vector<std::map<std::string, long>> fTags;
""", """    std::vector<std::pair<double, G4ThreeVector>> fGammas;
    std::vector<double> fGammaDep;              // `iontag`: вклад потомков кванта в кристалл, кэВ
    std::map<int, int> fAncestor;               // `iontag`: трек → индекс предка-кванта
    std::vector<std::map<std::string, long>> fTags;
""")

rep("""        if (track->GetDefinition() == G4Gamma::GammaDefinition())
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
""", """        if (track->GetDefinition() == G4Gamma::GammaDefinition())
        {
            const G4VProcess* creator = track->GetCreatorProcess();
            if (creator != nullptr && creator->GetProcessName().find("adioactiv") != std::string::npos)
            {
                if (gTagMode)
                {
                    fEvent->AddTaggedGamma(track->GetTrackID(), track->GetKineticEnergy() / keV,
                                           track->GetMomentumDirection());
                }
                else
                {
                    fEvent->AddGamma(track->GetKineticEnergy() / keV, track->GetMomentumDirection());
                }
            }
            else if (gTagMode)
            {
                fEvent->Inherit(track->GetTrackID(), track->GetParentID());
            }

            // `iontag` только записывает квант — перенос идёт как обычно.
            return gAngCorrMode ? fKill : fUrgent;
        }

        if (gTagMode)
        {
            // Электроны, позитроны и прочее: наследуют предка-квант родителя.
            fEvent->Inherit(track->GetTrackID(), track->GetParentID());
            return fUrgent;
        }
""")

rep("""        if (preInCrystal)
        {
            fEvent->Add(step->GetTotalEnergyDeposit() / keV);
        }
""", """        if (preInCrystal)
        {
            fEvent->Add(step->GetTotalEnergyDeposit() / keV);
            if (gTagMode)
            {
                fEvent->AddTo(step->GetTrack()->GetTrackID(), step->GetTotalEnergyDeposit() / keV);
            }
        }
""")

io.open(p, "w", encoding="utf-8", newline="").write(s)
print("ok")
