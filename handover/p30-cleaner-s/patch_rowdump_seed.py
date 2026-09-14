# П30: ключ --seed= у прямого плеча ResponseRowDumpProbe — контроль «зерно против зерна» (правило MatrixDiffProbe).
import io
p='tools/effmaker/probes/ResponseRowDumpProbe.cs'
raw=io.open(p,'rb').read()
bom = raw.startswith(b'\xef\xbb\xbf')
s=raw.decode('utf-8-sig').replace('\r\n','\n')
def rep(a,b):
    global s
    assert s.count(a)==1, (s.count(a), a[:70])
    s=s.replace(a,b)
rep("""        static int? DirectHistories;
""","""        static int? DirectHistories;
        static int? DirectSeed;
""")
rep("""                else if (a.StartsWith("--hist=", StringComparison.Ordinal))
                {
                    DirectHistories = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                }
""","""                else if (a.StartsWith("--hist=", StringComparison.Ordinal))
                {
                    DirectHistories = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--seed=", StringComparison.Ordinal))
                {
                    DirectSeed = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                }
""")
rep("""            if (DirectHistories.HasValue)
            {
                options.Histories = DirectHistories.Value;
                options.ContinuumErrorTarget = 0.0;
            }
""","""            if (DirectHistories.HasValue)
            {
                options.Histories = DirectHistories.Value;
                options.ContinuumErrorTarget = 0.0;
            }
            if (DirectSeed.HasValue)
            {
                options.Seed = DirectSeed.Value;
            }
""")
rep("""                + (options.RayleighToCrystal ? "_rayl2" : "")
""","""                + (options.RayleighToCrystal ? "_rayl2" : "")
                + (DirectSeed.HasValue ? "_seed" + DirectSeed.Value.ToString(CultureInfo.InvariantCulture) : "")
""")
out=(b'\xef\xbb\xbf' if bom else b'')+s.replace('\n','\r\n').encode('utf-8')
io.open(p,'wb').write(out)
print('ok')
