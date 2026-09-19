# -*- coding: utf-8 -*-
# П106: рычаг killoutbrem в main/SETUP/usage g4cf.cc (файл CRLF, правка байтами).
p = r'D:\BqMoni_Claude\p106\g4\g4cf.cc'
d = open(p, 'rb').read()
s = d.decode('utf-8')
n = 0
BS = '\\'   # обратный слэш — символом, чтобы не гадать
old = '                           || std::strcmp(argv[base], "killescbrem") == 0 || std::strcmp(argv[base], "killescdelta") == 0\r\n'
new = old + '                           || std::strcmp(argv[base], "killoutbrem") == 0\r\n'
assert s.count(old) == 1, 1; s = s.replace(old, new); n += 1
old = '        else if (std::strcmp(argv[base], "killescdelta") == 0) { gEscPop = gKillEscDelta = true; }\r\n'
new = old + '        else if (std::strcmp(argv[base], "killoutbrem") == 0) { gEscPop = gKillOutBrem = true; }\r\n'
assert s.count(old) == 1, 2; s = s.replace(old, new); n += 1
old = '                    " escpop=%d killescown=%d killesccarry=%d killret=%d killretsame=%d killretother=%d killescbrem=%d killescdelta=%d' + BS + 'n",'
new = '                    " escpop=%d killescown=%d killesccarry=%d killret=%d killretsame=%d killretother=%d killescbrem=%d killescdelta=%d killoutbrem=%d' + BS + 'n",'
assert s.count(old) == 1, 3; s = s.replace(old, new); n += 1
old = '                    gKillRetSame ? 1 : 0, gKillRetOther ? 1 : 0, gKillEscBrem ? 1 : 0, gKillEscDelta ? 1 : 0);'
new = '                    gKillRetSame ? 1 : 0, gKillRetOther ? 1 : 0, gKillEscBrem ? 1 : 0, gKillEscDelta ? 1 : 0, gKillOutBrem ? 1 : 0);'
assert s.count(old) == 1, 4; s = s.replace(old, new); n += 1
old = '                             " for e-/e+). Reader: SETUP flags and the ESCPOP line.' + BS + 'n");'
new = ('                             " for e-/e+), killoutbrem (kill gammas born outside by e-/e+ born outside:"\r\n'
       '                             " bremsstrahlung of cladding electrons). Reader: SETUP flags and the ESCPOP line.' + BS + 'n");')
assert s.count(old) == 1, 5; s = s.replace(old, new); n += 1
old = '        std::fprintf(stderr, "escpop/killescown/killesccarry/killret*/killescbrem/killescdelta: need `scene <file>`' + BS + 'n");'
new = '        std::fprintf(stderr, "escpop/killescown/killesccarry/killret*/killescbrem/killescdelta/killoutbrem: need `scene <file>`' + BS + 'n");'
assert s.count(old) == 1, 6; s = s.replace(old, new); n += 1
old = '// Рычаги `escpop` / `killescown` / `killesccarry` / `killret` / `killretsame` /\r\n// `killretother` / `killescbrem` / `killescdelta` (П106, `M13`, 19.09.2026) —'
new = '// Рычаги `escpop` / `killescown` / `killesccarry` / `killret` / `killretsame` /\r\n// `killretother` / `killescbrem` / `killescdelta` / `killoutbrem` (П106, `M13`, 19.09.2026) —'
assert s.count(old) == 1, 7; s = s.replace(old, new); n += 1
open(p, 'wb').write(s.encode('utf-8'))
print('patched', n)
d = open(p, 'rb').read()
print('CRLF', d.count(b'\r\n'), 'LF', d.count(b'\n') - d.count(b'\r\n'))
