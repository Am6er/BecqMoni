# -*- coding: utf-8 -*-
r"""П85 (AMBER42): третья правка tools/g4cf/g4cf.cc — ключ `seed <N>` (зерно ГСЧ; без ключа — умолчание
Geant4, то есть прогон побитово повторяем и повтор НЕ добавляет статистики)."""
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

rep("""    bool gAngCorrMode = false;
""", """    bool gAngCorrMode = false;

    // Зерно ГСЧ (ключ `seed <N>`, П85): без ключа Geant4 стартует с ОДНОГО и
    // того же зерна, и два одинаковых прогона побитово равны — повтор ради
    // статистики обязан менять зерно (Co-60 off 15.09.2026: два прогона по
    // 40 млн дали 3561 = 3561 отсчёт в окне 2505.7).
    long gSeed = 0;
""", 'переменная seed')

rep("""    //      Перед всем этим могут стоять `vacuum` (мир пустой вместо воздуха) и
    //      `corr` (угловые γ–γ корреляции каскада в RDM) — в любом порядке.
    int base = 1;
    while (argc > base && (std::strcmp(argv[base], "vacuum") == 0 || std::strcmp(argv[base], "corr") == 0))
    {
        if (std::strcmp(argv[base], "vacuum") == 0)
        {
            gVacuumWorld = true;
        }
        else
        {
            gCorrelatedGamma = true;
        }

        base++;
    }
""", """    //      Перед всем этим могут стоять `vacuum` (мир пустой вместо воздуха),
    //      `corr` (угловые γ–γ корреляции каскада в RDM) и `seed <N>` (зерно
    //      ГСЧ) — в любом порядке.
    int base = 1;
    while (argc > base && (std::strcmp(argv[base], "vacuum") == 0 || std::strcmp(argv[base], "corr") == 0
                           || (std::strcmp(argv[base], "seed") == 0 && argc > base + 1)))
    {
        if (std::strcmp(argv[base], "vacuum") == 0)
        {
            gVacuumWorld = true;
        }
        else if (std::strcmp(argv[base], "seed") == 0)
        {
            gSeed = std::atol(argv[base + 1]);
            base++;
        }
        else
        {
            gCorrelatedGamma = true;
        }

        base++;
    }
""", 'разбор seed')

rep("""        std::fprintf(stderr, "g4cf [vacuum] [corr] [scene <file>] mono <E_keV> <N> | ion <Z> <A> <N> <windows...>"
""", """        std::fprintf(stderr, "g4cf [vacuum] [corr] [seed <N>] [scene <file>] mono <E_keV> <N> | ion <Z> <A> <N> <windows...>"
""", 'usage')

rep("""    auto runManager = G4RunManagerFactory::CreateRunManager(G4RunManagerType::Default);
""", """    if (gSeed != 0)
    {
        // До создания менеджера: мастер раздаёт зёрна потокам от своего ГСЧ.
        G4Random::setTheSeed(gSeed);
    }

    auto runManager = G4RunManagerFactory::CreateRunManager(G4RunManagerType::Default);
""", 'setTheSeed')

rep("""        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d\\n",
                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0);
""".replace('\\\\n', BS + 'n'), """        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld\\n",
                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed);
""".replace('\\\\n', BS + 'n'), 'печать seed')

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('ok: ключ seed внесён')
