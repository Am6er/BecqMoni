# П101: ключ `ionhist` в g4cf.cc. Файл — CRLF; читаем/пишем newline='' и подставляем \r\n сами.
import io, sys
p = sys.argv[1]
s = io.open(p, encoding='utf-8', newline='').read()
assert '\r\n' in s, 'ожидался CRLF'
def nl(t):
    return t.replace('\n', '\r\n')
reps = []
reps.append((
"""// Сборка — build_g4cf.bat рядом (vcvars64 обязан звать bat, не ps1: %PATH%""",
"""// Ключ `ionhist <шаг_кэВ> <E_max_кэВ>` (П101, `S177`, 18.09.2026) — ГИСТОГРАММА
// поглощённой энергии в ион-режиме, тем же правилом бина, что у `hist`
// (bin = int(edep/шаг + 0.5), последний бин собирает всё выше E_max). Зачем:
// окна `ion` считают только названные суммы, а вопрос П93 (какие из 82 сумм
// Eu-152 стоят не с той площадью) требует ВСЕЙ шкалы разом — и полос рядом с
// каждым окном, чтобы вычесть континуум (П90 §4.3: окно ±0.5 кэВ держит и
// комптоновский континуум линий выше). Печать — те же строки `HISTBEGIN`/`HIST`/
// `HISTEND`; окна `ion` при этом считаются, как и прежде.
//
// Сборка — build_g4cf.bat рядом (vcvars64 обязан звать bat, не ps1: %PATH%"""))
reps.append((
"""    //      | g4cf [scene <файл>] hist <E_кэВ> <N> <шаг_бина_кэВ>
    // Файл сцены — вывод effsim --dump-scene; без него сцена вшитая (tube).""",
"""    //      | g4cf [scene <файл>] hist <E_кэВ> <N> <шаг_бина_кэВ>
    //      | g4cf ionhist <шаг_кэВ> <E_max_кэВ> [scene <файл>] ion <Z> <A> <N> [окна…]
    // Файл сцены — вывод effsim --dump-scene; без него сцена вшитая (tube)."""))
reps.append((
"""                           || std::strcmp(argv[base], "fullcarry") == 0
                           || (std::strcmp(argv[base], "seed") == 0 && argc > base + 1)))
    {
        if (std::strcmp(argv[base], "vacuum") == 0)
        {
            gVacuumWorld = true;
        }""",
"""                           || std::strcmp(argv[base], "fullcarry") == 0
                           || (std::strcmp(argv[base], "seed") == 0 && argc > base + 1)
                           || (std::strcmp(argv[base], "ionhist") == 0 && argc > base + 2)))
    {
        if (std::strcmp(argv[base], "vacuum") == 0)
        {
            gVacuumWorld = true;
        }
        else if (std::strcmp(argv[base], "ionhist") == 0)
        {
            // (П101) Гистограмма всей шкалы в ион-режиме: шаг и верх, кэВ.
            // Длина — по правилу раскладки отклика (последний бин — всё выше).
            gHistBinKev = std::atof(argv[base + 1]);
            double maxKev = std::atof(argv[base + 2]);
            if (!(gHistBinKev > 0.0) || !(maxKev > gHistBinKev))
            {
                std::fprintf(stderr, "ionhist: needs bin > 0 and Emax > bin, keV\\n");
                return 2;
            }

            gHistBins = int(maxKev / gHistBinKev + 0.5) + 1;
            base += 2;
        }"""))
reps.append((
"""        std::fprintf(stderr, "g4cf [vacuum] [corr] [seed <N>] [killesc] [killcarry] [fullcarry] [scene <file>] mono <E_keV> <N>\"""",
"""        std::fprintf(stderr, "g4cf [vacuum] [corr] [seed <N>] [ionhist <bin_keV> <Emax_keV>] [killesc] [killcarry] [fullcarry] [scene <file>] mono <E_keV> <N>\""""))
reps.append((
"""        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld killesc=%d killcarry=%d fullcarry=%d\\n",
                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed,
                    gKillEscape ? 1 : 0, gKillCarry ? 1 : 0, gFullCarry ? 1 : 0);""",
"""        std::printf("SETUP correlatedGamma=%d twoJmax=%d vacuum=%d seed=%ld killesc=%d killcarry=%d fullcarry=%d histbin=%.3f histbins=%d\\n",
                    deex->CorrelatedGamma() ? 1 : 0, deex->GetTwoJMAX(), gVacuumWorld ? 1 : 0, gSeed,
                    gKillEscape ? 1 : 0, gKillCarry ? 1 : 0, gFullCarry ? 1 : 0, gHistBinKev, gHistBins);"""))
for i, (a, b) in enumerate(reps):
    a = nl(a); b = nl(b)
    assert s.count(a) == 1, 'замена %d: найдено %d' % (i, s.count(a))
    s = s.replace(a, b, 1)
io.open(p, 'w', encoding='utf-8', newline='').write(s)
print('ok, замен', len(reps))
