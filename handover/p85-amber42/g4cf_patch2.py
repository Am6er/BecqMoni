# -*- coding: utf-8 -*-
r"""П85 (AMBER42): вторая правка tools/g4cf/g4cf.cc — режим `angcorr`: прямая мерка A22/A44 пары
квантов распада по направлениям в момент рождения (без детектора, кванты гасятся в стеке).
Запускать из корня дерева; переводы строк рабочей копии сохраняются."""
import io
p = 'tools/g4cf/g4cf.cc'
t = io.open(p, encoding='utf-8', newline='').read()
nl = '\r\n' if t.count('\r\n') == t.count('\n') and t.count('\n') > 0 else '\n'
BS = chr(92)
print('newline:', repr(nl))

def rep(old, new, tag):
    global t
    old = old.replace('\n', nl); new = new.replace('\n', nl)
    if old not in t:
        raise SystemExit('не нашёл фрагмент: ' + tag)
    t = t.replace(old, new, 1)

rep('#include "G4UserSteppingAction.hh"\n',
    '#include "G4UserSteppingAction.hh"\n#include "G4UserStackingAction.hh"\n#include "G4Track.hh"\n#include "G4VProcess.hh"\n', 'include')

rep("    bool gCorrelatedGamma = false;\n",
    """    bool gCorrelatedGamma = false;

    // Режим `angcorr` (П85, `AMBER42`): ПРЯМАЯ мерка угловой корреляции пары
    // квантов распада — направления квантов РДМ снимаются в момент рождения
    // (стек), сами кванты гасятся, детектор не участвует. Печатает
    // A22 = 5·<P2(cos θ)>, A44 = 9·<P4(cos θ)> с σ и гистограмму cos θ.
    // Это положительный контроль ключа `corr` без статистики сумм-пика.
    bool gAngCorrMode = false;
    double gAngE1Kev = 0.0, gAngE2Kev = 0.0;
    const int kAngBins = 20;
""", 'переменные angcorr')

rep("""    RunAction() : fAny("any", 0)
    {
        auto manager = G4AccumulableManager::Instance();
        manager->Register(fAny);
""", """    RunAction() : fAny("any", 0), fAngN("angN", 0), fAngS2("angS2", 0.0), fAngS4("angS4", 0.0),
                  fAngS22("angS22", 0.0), fAngS44("angS44", 0.0)
    {
        auto manager = G4AccumulableManager::Instance();
        manager->Register(fAny);
        manager->Register(fAngN);
        manager->Register(fAngS2);
        manager->Register(fAngS4);
        manager->Register(fAngS22);
        manager->Register(fAngS44);
        for (int i = 0; i < kAngBins; ++i)
        {
            fAngHist.push_back(new G4Accumulable<G4int>("ah" + std::to_string(i), 0));
            manager->Register(*fAngHist.back());
        }
""", 'RunAction ctor')

rep("""        long decays = run->GetNumberOfEvent();
        std::printf("RESULT decays=%ld\\n", decays);
""".replace('\\\\n', BS + 'n'), """        long decays = run->GetNumberOfEvent();
        std::printf("RESULT decays=%ld\\n", decays);
        if (gAngCorrMode)
        {
            // W(cos θ) = 1 + A22·P2 + A44·P4 при нормировке <W> = 1 даёт
            // <P_k> = A_kk/(2k+1); σ — по разбросу P_k в выборке.
            long n = fAngN.GetValue();
            double m2 = n > 0 ? fAngS2.GetValue() / n : 0.0;
            double m4 = n > 0 ? fAngS4.GetValue() / n : 0.0;
            double v2 = n > 1 ? (fAngS22.GetValue() / n - m2 * m2) / (n - 1) : 0.0;
            double v4 = n > 1 ? (fAngS44.GetValue() / n - m4 * m4) / (n - 1) : 0.0;
            std::printf("ANGCORR pairs=%ld A22=%.5f sigma=%.5f A44=%.5f sigma=%.5f E1=%.1f E2=%.1f corr=%d\\n",
                        n, 5.0 * m2, 5.0 * std::sqrt(std::max(0.0, v2)),
                        9.0 * m4, 9.0 * std::sqrt(std::max(0.0, v4)),
                        gAngE1Kev, gAngE2Kev, gCorrelatedGamma ? 1 : 0);
            for (int i = 0; i < kAngBins; ++i)
            {
                std::printf("ANGHIST %d %.2f %.2f %d\\n", i, -1.0 + 2.0 * i / kAngBins,
                            -1.0 + 2.0 * (i + 1) / kAngBins, fAngHist[i]->GetValue());
            }
        }
""".replace('\\\\n', BS + 'n'), 'печать ANGCORR')

rep("""    void Count(double edepKev)
    {
""", """    /// Пара квантов распада с косинусом угла между направлениями.
    void CountPair(double cosTheta)
    {
        double c2 = cosTheta * cosTheta;
        double p2 = 0.5 * (3.0 * c2 - 1.0);
        double p4 = 0.125 * (35.0 * c2 * c2 - 30.0 * c2 + 3.0);
        fAngN += 1;
        fAngS2 += p2;
        fAngS4 += p4;
        fAngS22 += p2 * p2;
        fAngS44 += p4 * p4;
        int bin = int((cosTheta + 1.0) * 0.5 * kAngBins);
        if (bin < 0) { bin = 0; }
        if (bin >= kAngBins) { bin = kAngBins - 1; }
        *fAngHist[bin] += 1;
    }

    void Count(double edepKev)
    {
""", 'CountPair')

rep("""    G4Accumulable<G4int> fAny;
    std::vector<G4Accumulable<G4int>*> fPeaks;
    std::vector<G4Accumulable<G4int>*> fHist;
};
""", """    G4Accumulable<G4int> fAny;
    std::vector<G4Accumulable<G4int>*> fPeaks;
    std::vector<G4Accumulable<G4int>*> fHist;
    G4Accumulable<G4int> fAngN;
    G4Accumulable<G4double> fAngS2, fAngS4, fAngS22, fAngS44;
    std::vector<G4Accumulable<G4int>*> fAngHist;
};
""", 'поля RunAction')

rep("""    void BeginOfEventAction(const G4Event*) override { fEdepKev = 0.0; }

    void EndOfEventAction(const G4Event*) override { fRun->Count(fEdepKev); }

    void Add(double edepKev) { fEdepKev += edepKev; }

private:
    RunAction* fRun;
    double fEdepKev = 0.0;
};
""", """    void BeginOfEventAction(const G4Event*) override
    {
        fEdepKev = 0.0;
        fGammas.clear();
    }

    void EndOfEventAction(const G4Event*) override
    {
        if (gAngCorrMode)
        {
            // Ровно один квант E1 и ровно один E2 (±1 кэВ) — иначе пара не та.
            int i1 = -1, i2 = -1, n1 = 0, n2 = 0;
            for (size_t i = 0; i < fGammas.size(); ++i)
            {
                if (std::fabs(fGammas[i].first - gAngE1Kev) < 1.0) { i1 = int(i); ++n1; }
                else if (std::fabs(fGammas[i].first - gAngE2Kev) < 1.0) { i2 = int(i); ++n2; }
            }

            if (n1 == 1 && n2 == 1)
            {
                fRun->CountPair(fGammas[i1].second.dot(fGammas[i2].second));
            }

            return;
        }

        fRun->Count(fEdepKev);
    }

    void Add(double edepKev) { fEdepKev += edepKev; }

    void AddGamma(double eKev, const G4ThreeVector& dir) { fGammas.emplace_back(eKev, dir); }

private:
    RunAction* fRun;
    double fEdepKev = 0.0;
    std::vector<std::pair<double, G4ThreeVector>> fGammas;
};

/// Режим `angcorr`: квант распада записывается в момент постановки в стек и
/// ГАСИТСЯ (перенос не нужен); ион и всё прочее идут как обычно.
class StackingAction : public G4UserStackingAction
{
public:
    explicit StackingAction(EventAction* event) : fEvent(event) {}

    G4ClassificationOfNewTrack ClassifyNewTrack(const G4Track* track) override
    {
        if (!gAngCorrMode || track->GetParentID() <= 0)
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

        // электроны/нейтрино распада тоже не нужны; ионы (дочерние состояния) — нужны
        if (track->GetDefinition()->GetPDGEncoding() < 1000000000)
        {
            return fKill;
        }

        return fUrgent;
    }

private:
    EventAction* fEvent;
};
""", 'EventAction/StackingAction')

rep("""        SetUserAction(new SteppingAction(event));
    }
""", """        SetUserAction(new SteppingAction(event));
        SetUserAction(new StackingAction(event));
    }
""", 'Actions::Build')

rep("""    // g4cf [scene <файл>] mono <E_кэВ> <N>
""", """    // g4cf [corr] angcorr <Z> <A> <N> <E1_кэВ> <E2_кэВ>   — прямая мерка A22/A44 пары
    // g4cf [scene <файл>] mono <E_кэВ> <N>
""", 'комментарий main')

rep("""    bool ion = std::strcmp(argv[base], "ion") == 0;
    bool hist = std::strcmp(argv[base], "hist") == 0;
    int argAt = base + 1;
    double energyKev = 0.0;
    int z = 0, a = 0;
    if (ion)
    {
        z = std::atoi(argv[argAt++]);
        a = std::atoi(argv[argAt++]);
    }
""", """    bool angcorr = std::strcmp(argv[base], "angcorr") == 0;
    bool ion = std::strcmp(argv[base], "ion") == 0 || angcorr;
    bool hist = std::strcmp(argv[base], "hist") == 0;
    int argAt = base + 1;
    double energyKev = 0.0;
    int z = 0, a = 0;
    if (ion)
    {
        z = std::atoi(argv[argAt++]);
        a = std::atoi(argv[argAt++]);
    }

    if (angcorr)
    {
        if (argc < argAt + 3)
        {
            std::fprintf(stderr, "angcorr: нужны <N> <E1_кэВ> <E2_кэВ>\\n");
            return 2;
        }

        gAngCorrMode = true;
        gAngE1Kev = std::atof(argv[argAt + 1]);
        gAngE2Kev = std::atof(argv[argAt + 2]);
    }
""".replace('\\\\n', BS + 'n'), 'разбор angcorr')

rep("""    long decays = std::atol(argv[argAt++]);
    if (hist)
""", """    long decays = std::atol(argv[argAt++]);
    if (angcorr)
    {
        argAt += 2;     // E1, E2 уже прочитаны
    }

    if (hist)
""", 'decays/argAt')

rep("""    std::snprintf(buffer, sizeof buffer, "/run/beamOn %ld", decays);
    ui->ApplyCommand(buffer);
""", """    if (gAngCorrMode)
    {
        std::printf("SETUP angcorr Z=%d A=%d E1=%.1f E2=%.1f\\n", z, a, gAngE1Kev, gAngE2Kev);
        std::fflush(stdout);
    }

    std::snprintf(buffer, sizeof buffer, "/run/beamOn %ld", decays);
    ui->ApplyCommand(buffer);
""".replace('\\\\n', BS + 'n'), 'печать SETUP angcorr')

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok: режим angcorr внесён')
