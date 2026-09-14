# П55: переделка правки ElectronTransport.cs — конец трека в поле ТОЛЬКО перед return
# (рекурсия тормозного внутри переноса иначе перезапишет его посреди трека), и раздельные
# вызовы электрона/позитрона в ветке пары со сбросом признака перед позитроном.
import io, subprocess

wt = r'D:\BqMoni_Claude\p55\wt'
sim = wt + r'\BecquerelMonitor\EfficiencyMaker\EfficiencySimulator.cs'
etr = wt + r'\BecquerelMonitor\EfficiencyMaker\ElectronTransport.cs'

subprocess.check_call(['git', '-C', wt, 'checkout', '--', 'BecquerelMonitor/EfficiencyMaker/ElectronTransport.cs'])

def patch(path, pairs):
    s = io.open(path, encoding='utf-8-sig', newline='').read()
    nl = '\r\n' if '\r\n' in s else '\n'
    for old, new in pairs:
        old = old.replace('\n', nl)
        new = new.replace('\n', nl)
        assert s.count(old) == 1, (path, old[:60], s.count(old))
        s = s.replace(old, new, 1)
    io.open(path, 'w', encoding='utf-8-sig', newline='').write(s)

patch(etr, [
(
'''            double density = this.geometry.Crystal.Density;
            double x0 = this.CrystalRadiationLength();
            double residual = ElectronData.RangeOf(this.electron, te);     // г/см²
            if (!(residual > 0.0) || !(density > 0.0))
            {
                return 0.0;
            }

            // Ранний выход: пробег короче расстояния до ближайшей грани —
            // погибнет внутри при любой траектории.
            if (!this.ElectronTransportNoEarlyExit
                && residual / density <= this.CrystalNearestFace(x, y, z))
            {
                if (alongPath)
                {
                    this.RestBremsstrahlung(x, y, z, ux, uy, uz, te, te, depth, ref radiated, ref lost);
                }

                return 0.0;
            }
''',
'''            double density = this.geometry.Crystal.Density;
            double x0 = this.CrystalRadiationLength();
            double residual = ElectronData.RangeOf(this.electron, te);     // г/см²
            if (!(residual > 0.0) || !(density > 0.0))
            {
                this.SetTrackEnd(x, y, z);
                return 0.0;
            }

            // Ранний выход: пробег короче расстояния до ближайшей грани —
            // погибнет внутри при любой траектории.
            if (!this.ElectronTransportNoEarlyExit
                && residual / density <= this.CrystalNearestFace(x, y, z))
            {
                if (alongPath)
                {
                    this.RestBremsstrahlung(x, y, z, ux, uy, uz, te, te, depth, ref radiated, ref lost);
                }

                this.SetTrackEnd(x, y, z);      // П55: после рекурсии тормозного
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
''',
'''                double first = stepCm * this.Uniform();
                double toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (first >= toEdge)
                {
                    this.SetTrackEndOnFace(x, y, z, ux, uy, uz, toEdge);
                    return this.EscapeEnergy(residual - toEdge * density, te - radiated);
                }
'''),
(
'''                double second = stepCm - first;
                toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (second >= toEdge)
                {
                    return this.EscapeEnergy(residual - (first + toEdge) * density, te - radiated);
                }
''',
'''                double second = stepCm - first;
                toEdge = this.CrystalPath(x, y, z, ux, uy, uz);
                if (second >= toEdge)
                {
                    this.SetTrackEndOnFace(x, y, z, ux, uy, uz, toEdge);
                    return this.EscapeEnergy(residual - (first + toEdge) * density, te - radiated);
                }
'''),
(
'''                if (!this.ElectronTransportNoEarlyExit
                    && residual / density <= this.CrystalNearestFace(x, y, z))
                {
                    if (alongPath)
                    {
                        this.RestBremsstrahlung(x, y, z, ux, uy, uz, t, te, depth, ref radiated, ref lost);
                    }

                    return 0.0;
                }
            }

            return 0.0;
        }
''',
'''                if (!this.ElectronTransportNoEarlyExit
                    && residual / density <= this.CrystalNearestFace(x, y, z))
                {
                    if (alongPath)
                    {
                        this.RestBremsstrahlung(x, y, z, ux, uy, uz, t, te, depth, ref radiated, ref lost);
                    }

                    this.SetTrackEnd(x, y, z);
                    return 0.0;
                }
            }

            this.SetTrackEnd(x, y, z);
            return 0.0;
        }

        /// <summary>П55: конец трека переноса — точка внутри кристалла.</summary>
        void SetTrackEnd(double x, double y, double z)
        {
            this.trackEndX = x; this.trackEndY = y; this.trackEndZ = z; this.trackEndValid = true;
        }

        /// <summary>П55: конец трека переноса — точка выхода на грани, чуть внутри (как зажим PositronStop).</summary>
        void SetTrackEndOnFace(double x, double y, double z, double ux, double uy, double uz, double toEdge)
        {
            double m = Math.Max(0.0, toEdge - 1e-7);
            this.SetTrackEnd(x + ux * m, y + uy * m, z + uz * m);
        }
'''),
])

patch(sim, [
(
'''                    double share = this.Uniform();
                    escaped = lost
                           + this.ElectronLoss(x, y, z, kinetic * share, depth,
                                               ElectronBirth.Pair, ux, uy, uz)
                           + this.ElectronLoss(x, y, z, kinetic * (1.0 - share), depth,
                                               ElectronBirth.Pair, ux, uy, uz);
''',
'''                    double share = this.Uniform();
                    escaped = lost
                           + this.ElectronLoss(x, y, z, kinetic * share, depth,
                                               ElectronBirth.Pair, ux, uy, uz);
                    this.trackEndValid = false;     // П55: ниже — позитрон
                    escaped += this.ElectronLoss(x, y, z, kinetic * (1.0 - share), depth,
                                                 ElectronBirth.Pair, ux, uy, uz);
'''),
])
print("patched2")
