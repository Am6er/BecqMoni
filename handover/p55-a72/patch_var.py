# П55 (A72 оценка): рычаг PositronStopAtTrackEnd в worktree — точка аннигиляции позитрона
# = конец трека переноса (TransportElectron), а не PositronStop эффективной глубиной.
# ВЫКЛ — ни одного лишнего случайного числа, побитово HEAD. Только для замера, в HEAD не идёт.
import io

wt = r'D:\BqMoni_Claude\p55\wt'
sim = wt + r'\BecquerelMonitor\EfficiencyMaker\EfficiencySimulator.cs'
etr = wt + r'\BecquerelMonitor\EfficiencyMaker\ElectronTransport.cs'
probe = wt + r'\tools\effmaker\probes\G4RawProbe.cs'

def patch(path, pairs):
    s = io.open(path, encoding='utf-8-sig', newline='').read()
    nl = '\r\n' if '\r\n' in s else '\n'
    for old, new in pairs:
        old = old.replace('\n', nl)
        new = new.replace('\n', nl)
        assert s.count(old) == 1, (path, old[:60], s.count(old))
        s = s.replace(old, new, 1)
    io.open(path, 'w', encoding='utf-8-sig', newline='').write(s)

# --- EfficiencySimulator.cs: поле рычага + конец трека + ветка пары
patch(sim, [
(
'''        public bool PositronOffset = new ResponseMatrixOptions().PositronOffset;
''',
'''        public bool PositronOffset = new ResponseMatrixOptions().PositronOffset;

        /// <summary>
        /// П55 (оценка `A72` п. 4, 14.09.2026) — РЫЧАГ ЗАМЕРА, не настройка:
        /// точка аннигиляции позитрона = КОНЕЦ ЕГО ТРЕКА из переноса
        /// (<see cref="TransportElectron"/>: точка гибели внутри либо точка
        /// выхода на грани), а не <see cref="PositronStop"/> эффективной
        /// глубиной. Требует <see cref="ElectronTransport"/> и
        /// <see cref="PositronOffset"/>. Выключенный — ни одного лишнего
        /// случайного числа, побитово прежний результат.
        /// </summary>
        public bool PositronStopAtTrackEnd;

        /// <summary>П55: конец последнего трека переноса (см; заполняет <see cref="TransportElectron"/>).</summary>
        double trackEndX, trackEndY, trackEndZ;
        bool trackEndValid;
'''),
(
'''                    if (this.PositronOffset)
                    {
                        this.PositronStop(kinetic * (1.0 - share),
                                          ref px, ref py, ref pz);
                    }
''',
'''                    if (this.PositronOffset && this.PositronStopAtTrackEnd && this.ElectronTransport
                        && this.ElectronEscape && this.trackEndValid)
                    {
                        // П55: конец трека позитрона из переноса (последний
                        // вызов ElectronLoss выше — позитрон).
                        px = this.trackEndX; py = this.trackEndY; pz = this.trackEndZ;
                    }
                    else if (this.PositronOffset)
                    {
                        this.PositronStop(kinetic * (1.0 - share),
                                          ref px, ref py, ref pz);
                    }
'''),
])

# --- ElectronTransport.cs: запись конца трека во всех выходах TransportElectron
patch(etr, [
(
'''            double density = this.geometry.Crystal.Density;
            double x0 = this.CrystalRadiationLength();
            double residual = ElectronData.RangeOf(this.electron, te);     // г/см²
            if (!(residual > 0.0) || !(density > 0.0))
            {
                return 0.0;
            }
''',
'''            double density = this.geometry.Crystal.Density;
            double x0 = this.CrystalRadiationLength();
            double residual = ElectronData.RangeOf(this.electron, te);     // г/см²
            // П55: конец трека — по умолчанию точка рождения (погиб на месте).
            this.trackEndX = x; this.trackEndY = y; this.trackEndZ = z; this.trackEndValid = true;
            if (!(residual > 0.0) || !(density > 0.0))
            {
                return 0.0;
            }
'''),
(
'''                double first = stepCm * this.Uniform();
                double toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (first >= toEdge)
                {
                    return this.EscapeEnergy(residual - toEdge * density, te - radiated);
                }

                x += ux * first;
                y += uy * first;
                z += uz * first;
''',
'''                double first = stepCm * this.Uniform();
                double toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (first >= toEdge)
                {
                    // П55: вышел через грань — конец трека на грани, чуть внутри.
                    double m = Math.Max(0.0, toEdge - 1e-7);
                    this.trackEndX = x + ux * m; this.trackEndY = y + uy * m; this.trackEndZ = z + uz * m;
                    return this.EscapeEnergy(residual - toEdge * density, te - radiated);
                }

                x += ux * first;
                y += uy * first;
                z += uz * first;
                this.trackEndX = x; this.trackEndY = y; this.trackEndZ = z;
'''),
(
'''                double second = stepCm - first;
                toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (second >= toEdge)
                {
                    return this.EscapeEnergy(residual - (first + toEdge) * density, te - radiated);
                }

                x += ux * second;
                y += uy * second;
                z += uz * second;
''',
'''                double second = stepCm - first;
                toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (second >= toEdge)
                {
                    double m = Math.Max(0.0, toEdge - 1e-7);
                    this.trackEndX = x + ux * m; this.trackEndY = y + uy * m; this.trackEndZ = z + uz * m;
                    return this.EscapeEnergy(residual - (first + toEdge) * density, te - radiated);
                }

                x += ux * second;
                y += uy * second;
                z += uz * second;
                this.trackEndX = x; this.trackEndY = y; this.trackEndZ = z;
'''),
])

# --- G4RawProbe.cs: ключ --posend=0|1
patch(probe, [
(
'''            bool positron = store.PositronTransport, posoffset = store.PositronOffset;
''',
'''            bool positron = store.PositronTransport, posoffset = store.PositronOffset;
            bool posend = false;    // П55: точка аннигиляции = конец трека переноса
'''),
(
'''                if (a.StartsWith("--posoffset=", StringComparison.Ordinal)) { posoffset = Flag01(a, 12); continue; }
''',
'''                if (a.StartsWith("--posoffset=", StringComparison.Ordinal)) { posoffset = Flag01(a, 12); continue; }
                if (a.StartsWith("--posend=", StringComparison.Ordinal)) { posend = Flag01(a, 9); continue; }
'''),
(
'''            simulator.PositronOffset = posoffset;
''',
'''            simulator.PositronOffset = posoffset;
            simulator.PositronStopAtTrackEnd = posend;
            Console.WriteLine("П55 --posend=: точка аннигиляции = конец трека переноса {0}", posend ? "ВКЛ" : "выкл");
'''),
])
print("patched")
