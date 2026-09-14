# П55: счётчики переноса электрона в worktree (поверх правки var) — сколько треков ушло в перенос,
# сколько погибло ранним выходом, сколько вышло через грань и с какой энергией. Только чтение, физику
# не меняют, случайных чисел не тянут. Печать — G4RawProbe после прогона.
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

patch(sim, [
(
'''        double trackEndX, trackEndY, trackEndZ;
        bool trackEndValid;
''',
'''        double trackEndX, trackEndY, trackEndZ;
        bool trackEndValid;

        /// <summary>П55: счётчики переноса — треков всего / погибло ранним выходом без шага / вышло через грань; энергия треков и унесённая, кэВ.</summary>
        public long CountTransportTracks, CountTransportEarly, CountTransportEscapes;
        public double SumTransportKev, SumTransportEscapeKev;
'''),
])

patch(etr, [
(
'''            double residual = ElectronData.RangeOf(this.electron, te);     // г/см²
            if (!(residual > 0.0) || !(density > 0.0))
            {
                this.SetTrackEnd(x, y, z);
                return 0.0;
            }
''',
'''            double residual = ElectronData.RangeOf(this.electron, te);     // г/см²
            this.CountTransportTracks++;
            this.SumTransportKev += te;
            if (!(residual > 0.0) || !(density > 0.0))
            {
                this.SetTrackEnd(x, y, z);
                return 0.0;
            }
'''),
(
'''                this.SetTrackEnd(x, y, z);      // П55: после рекурсии тормозного
                return 0.0;
''',
'''                this.SetTrackEnd(x, y, z);      // П55: после рекурсии тормозного
                this.CountTransportEarly++;
                return 0.0;
'''),
(
'''                if (first >= toEdge)
                {
                    this.SetTrackEndOnFace(x, y, z, ux, uy, uz, toEdge);
                    return this.EscapeEnergy(residual - toEdge * density, te - radiated);
                }
''',
'''                if (first >= toEdge)
                {
                    this.SetTrackEndOnFace(x, y, z, ux, uy, uz, toEdge);
                    return this.CountEscape(this.EscapeEnergy(residual - toEdge * density, te - radiated));
                }
'''),
(
'''                if (second >= toEdge)
                {
                    this.SetTrackEndOnFace(x, y, z, ux, uy, uz, toEdge);
                    return this.EscapeEnergy(residual - (first + toEdge) * density, te - radiated);
                }
''',
'''                if (second >= toEdge)
                {
                    this.SetTrackEndOnFace(x, y, z, ux, uy, uz, toEdge);
                    return this.CountEscape(this.EscapeEnergy(residual - (first + toEdge) * density, te - radiated));
                }
'''),
(
'''        /// <summary>П55: конец трека переноса — точка внутри кристалла.</summary>
''',
'''        /// <summary>П55: счёт вылета через грань (энергия > 0).</summary>
        double CountEscape(double kev)
        {
            if (kev > 0.0)
            {
                this.CountTransportEscapes++;
                this.SumTransportEscapeKev += kev;
            }

            return kev;
        }

        /// <summary>П55: конец трека переноса — точка внутри кристалла.</summary>
'''),
])

patch(probe, [
(
'''            Console.WriteLine("П55 --posend=: точка аннигиляции = конец трека переноса {0}", posend ? "ВКЛ" : "выкл");
''',
'''            Console.WriteLine("П55 --posend=: точка аннигиляции = конец трека переноса {0}", posend ? "ВКЛ" : "выкл");
            simulatorForCounters = simulator;
'''),
(
'''            GeometryModel geometry = null;
            if (geometryPath != null)
''',
'''            GeometryModel geometry = null;
            EfficiencySimulator simulatorForCounters = null;
            if (geometryPath != null)
'''),
])

# печать счётчиков — в самом конце Main перед return 0: ищем печать «отклик:» (путь --out=)
s = io.open(probe, encoding='utf-8-sig', newline='').read()
idx = s.find('"отклик: "')
assert idx > 0, 'нет строки «отклик: »'
# найти начало строки Console.WriteLine с этим литералом
line_start = s.rfind('\n', 0, idx) + 1
nl = '\r\n' if '\r\n' in s else '\n'
ins = ('            if (simulatorForCounters != null)' + nl +
       '            {' + nl +
       '                Console.WriteLine("П55 перенос: треков {0}, ранний выход без шага {1} ({2:F2} %), вылетов через грань {3} ({4:F3} % треков, {5:F4} % историй), энергия треков {6:E3} кэВ, унесено {7:E3} кэВ ({8:F3} %)",' + nl +
       '                                  simulatorForCounters.CountTransportTracks, simulatorForCounters.CountTransportEarly,' + nl +
       '                                  100.0 * simulatorForCounters.CountTransportEarly / Math.Max(1, simulatorForCounters.CountTransportTracks),' + nl +
       '                                  simulatorForCounters.CountTransportEscapes,' + nl +
       '                                  100.0 * simulatorForCounters.CountTransportEscapes / Math.Max(1, simulatorForCounters.CountTransportTracks),' + nl +
       '                                  100.0 * simulatorForCounters.CountTransportEscapes / Math.Max(1, histories),' + nl +
       '                                  simulatorForCounters.SumTransportKev, simulatorForCounters.SumTransportEscapeKev,' + nl +
       '                                  100.0 * simulatorForCounters.SumTransportEscapeKev / Math.Max(1e-9, simulatorForCounters.SumTransportKev));' + nl +
       '            }' + nl + nl)
s = s[:line_start] + ins + s[line_start:]
io.open(probe, 'w', encoding='utf-8-sig', newline='').write(s)
print('patched cnt')
