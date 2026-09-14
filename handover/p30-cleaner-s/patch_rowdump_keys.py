# П30 (S126/S127): ключи --positron= --posoffset= --rayl2= --hist= у ПРЯМОГО плеча ResponseRowDumpProbe.
# ⛔ Правка ТОЛЬКО стенда bqp30: в основном дереве этот файл правит сосед незакоммичено, туда не копируется.
import io
p='tools/effmaker/probes/ResponseRowDumpProbe.cs'
raw=io.open(p,'rb').read()
crlf = raw.count(b'\r\n')==raw.count(b'\n')
s=raw.decode('utf-8-sig').replace('\r\n','\n')
def rep(a,b):
    global s
    assert s.count(a)==1, (s.count(a), a[:70])
    s=s.replace(a,b)
rep("""        static bool? PeakChannelByTolerance;
""","""        static bool? PeakChannelByTolerance;

        /// (П30 12.09.2026, `S126`/`S127`) Ключи физики пары и когерентного у
        /// прямого плеча: `--positron=`, `--posoffset=`, `--rayl2=`; `--hist=` —
        /// историй на узел (плоский счёт, `--target=0` внутри). null — умолчание класса.
        static bool? PositronTransport, PositronOffset, RayleighToCrystal;
        static int? DirectHistories;
""")
rep("""                else if (a.StartsWith("--pkch=", StringComparison.Ordinal))
                {
""","""                else if (a.StartsWith("--positron=", StringComparison.Ordinal))
                {
                    PositronTransport = a.Substring(11) == "1";
                }
                else if (a.StartsWith("--posoffset=", StringComparison.Ordinal))
                {
                    PositronOffset = a.Substring(12) == "1";
                }
                else if (a.StartsWith("--rayl2=", StringComparison.Ordinal))
                {
                    RayleighToCrystal = a.Substring(8) == "1";
                }
                else if (a.StartsWith("--hist=", StringComparison.Ordinal))
                {
                    DirectHistories = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--pkch=", StringComparison.Ordinal))
                {
""")
rep("""            if (LYieldSupply.HasValue)
            {
                options.LYieldSupply = LYieldSupply.Value;
            }
""","""            if (LYieldSupply.HasValue)
            {
                options.LYieldSupply = LYieldSupply.Value;
            }
            if (PositronTransport.HasValue)
            {
                options.PositronTransport = PositronTransport.Value;
            }
            if (PositronOffset.HasValue)
            {
                options.PositronOffset = PositronOffset.Value;
            }
            if (RayleighToCrystal.HasValue)
            {
                options.RayleighToCrystal = RayleighToCrystal.Value;
            }
            if (DirectHistories.HasValue)
            {
                options.Histories = DirectHistories.Value;
                options.ContinuumErrorTarget = 0.0;
            }
""")
rep("""                + (options.LYieldSupply != 0 ? "_lys" + options.LYieldSupply.ToString(CultureInfo.InvariantCulture) : "")
""","""                + (options.LYieldSupply != 0 ? "_lys" + options.LYieldSupply.ToString(CultureInfo.InvariantCulture) : "")
                + (options.PositronTransport ? (options.PositronOffset ? "_pos1off1" : "_pos1off0") : "")
                + (options.RayleighToCrystal ? "_rayl2" : "")
""")
out=s.replace('\n','\r\n') if crlf else s
io.open(p,'wb').write(out.encode('utf-8'))
print('ok crlf=%s'%crlf)
