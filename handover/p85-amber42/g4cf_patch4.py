# -*- coding: utf-8 -*-
r"""П85 (AMBER42): четвёртая правка tools/g4cf/g4cf.cc — в моно-режиме сценного генератора считать
Q_k(E) = <P_k(cos θ)> по событиям пика (и по событиям с любым вкладом) — геометрическая половина
корреляции самим арбитром, для сверки с AngularQkProbe."""
import io
p = 'tools/g4cf/g4cf.cc'
t = io.open(p, encoding='utf-8', newline='').read()
nl = '\r\n' if t.count('\r\n') == t.count('\n') and t.count('\n') > 0 else '\n'
BS = chr(92)

def rep(old, new, tag):
    global t
    old = old.replace('\n', nl); new = new.replace('\n', nl)
    if old not in t:
        raise SystemExit('не нашёл фрагмент: ' + tag)
    t = t.replace(old, new, 1)

rep("""    long gSeed = 0;
""", """    long gSeed = 0;

    // Моно-режим сценного генератора (П85): косинус угла вылета первички к оси
    // сцены (ось z — на центр кристалла) запоминается на событие, чтобы в конце
    // события накопить Q_k = <P_k(cos θ)> по событиям пика и по событиям с любым
    // вкладом — геометрическая половина корреляции, посчитанная самим арбитром
    // (сверка с AngularQkProbe). Печать: строки QK/QKT.
    G4ThreadLocal double gPrimaryCos = 2.0;   // 2 = не задан (ион, GPS)
""", 'переменная gPrimaryCos')

rep("""            double cosT = 2.0 * G4UniformRand() - 1.0;
            double sinT = std::sqrt(std::max(0.0, 1.0 - cosT * cosT));
            double phi = 2.0 * CLHEP::pi * G4UniformRand();
            auto particle = new G4PrimaryParticle(
                G4Gamma::GammaDefinition(),
""", """            double cosT = 2.0 * G4UniformRand() - 1.0;
            double sinT = std::sqrt(std::max(0.0, 1.0 - cosT * cosT));
            double phi = 2.0 * CLHEP::pi * G4UniformRand();
            gPrimaryCos = cosT;
            auto particle = new G4PrimaryParticle(
                G4Gamma::GammaDefinition(),
""", 'запомнить cos θ первички')

rep("""    RunAction() : fAny("any", 0), fAngN("angN", 0), fAngS2("angS2", 0.0), fAngS4("angS4", 0.0),
                  fAngS22("angS22", 0.0), fAngS44("angS44", 0.0)
    {
        auto manager = G4AccumulableManager::Instance();
        manager->Register(fAny);
""", """    RunAction() : fAny("any", 0), fAngN("angN", 0), fAngS2("angS2", 0.0), fAngS4("angS4", 0.0),
                  fAngS22("angS22", 0.0), fAngS44("angS44", 0.0),
                  fQkN("qkN", 0), fQkS2("qkS2", 0.0), fQkS4("qkS4", 0.0),
                  fQtN("qtN", 0), fQtS2("qtS2", 0.0), fQtS4("qtS4", 0.0)
    {
        auto manager = G4AccumulableManager::Instance();
        manager->Register(fAny);
        manager->Register(fQkN);
        manager->Register(fQkS2);
        manager->Register(fQkS4);
        manager->Register(fQtN);
        manager->Register(fQtS2);
        manager->Register(fQtS4);
""", 'RunAction ctor Qk')

rep("""        if (gAngCorrMode)
        {
            // W(cos θ) = 1 + A22·P2 + A44·P4 при нормировке <W> = 1 даёт
""", """        if (fQkN.GetValue() > 0 || fQtN.GetValue() > 0)
        {
            long n = fQkN.GetValue(), nt = fQtN.GetValue();
            std::printf("QK window=%.3f peak_events=%ld Q2=%.5f Q4=%.5f\\n", gWindows.empty() ? 0.0 : gWindows[0], n,
                        n > 0 ? fQkS2.GetValue() / n : 0.0, n > 0 ? fQkS4.GetValue() / n : 0.0);
            std::printf("QKT any_events=%ld Q2T=%.5f Q4T=%.5f\\n", nt,
                        nt > 0 ? fQtS2.GetValue() / nt : 0.0, nt > 0 ? fQtS4.GetValue() / nt : 0.0);
        }

        if (gAngCorrMode)
        {
            // W(cos θ) = 1 + A22·P2 + A44·P4 при нормировке <W> = 1 даёт
""".replace('\\\\n', BS + 'n'), 'печать QK')

rep("""    void Count(double edepKev)
    {
        if (edepKev > 1e-3)
        {
            fAny += 1;
        }
""", """    void Count(double edepKev)
    {
        if (edepKev > 1e-3)
        {
            fAny += 1;
        }

        // Q_k арбитра: cos θ первички (только сценный моно-режим) по событиям
        // пика первого окна и по событиям с любым вкладом.
        if (gPrimaryCos <= 1.0 && !gWindows.empty())
        {
            double c2 = gPrimaryCos * gPrimaryCos;
            double p2 = 0.5 * (3.0 * c2 - 1.0);
            double p4 = 0.125 * (35.0 * c2 * c2 - 30.0 * c2 + 3.0);
            if (edepKev > 1e-3)
            {
                fQtN += 1;
                fQtS2 += p2;
                fQtS4 += p4;
            }

            if (std::fabs(edepKev - gWindows[0]) < kHalfWindowKev)
            {
                fQkN += 1;
                fQkS2 += p2;
                fQkS4 += p4;
            }
        }
""", 'Count Qk')

rep("""    G4Accumulable<G4int> fAngN;
    G4Accumulable<G4double> fAngS2, fAngS4, fAngS22, fAngS44;
    std::vector<G4Accumulable<G4int>*> fAngHist;
};
""", """    G4Accumulable<G4int> fAngN;
    G4Accumulable<G4double> fAngS2, fAngS4, fAngS22, fAngS44;
    std::vector<G4Accumulable<G4int>*> fAngHist;
    G4Accumulable<G4int> fQkN;
    G4Accumulable<G4double> fQkS2, fQkS4;
    G4Accumulable<G4int> fQtN;
    G4Accumulable<G4double> fQtS2, fQtS4;
};
""", 'поля Qk')

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok: Q_k в моно-режиме внесён')
