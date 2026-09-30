# П186: копия CorpusFsaProbe.cs -> CorpusFsaProbeP186.cs с рычагом замера --fwhm-low=A:E0:SPAN
# ПШПВ(ch) := ПШПВ_файла(ch) · (1 + A·max(0, (E0 − E(ch))/SPAN)), E(ch) — калибровка файла.
# Рычаг ТОЛЬКО для опыта полосы: ширина внизу шкалы — причина окна Kβ и заниженной активности или нет.
import io, re
p = r'D:\BqMoni_Claude\p186\wt\tools\effmaker\probes\CorpusFsaProbe.cs'
q = r'D:\BqMoni_Claude\p186\wt\tools\effmaker\probes\CorpusFsaProbeP186.cs'
t = io.open(p, encoding='utf-8-sig', newline='').read()
nl = '\r\n' if '\r\n' in t else '\n'
n0 = t.count('namespace CorpusFsaProbe')
assert n0 == 1, n0
t = t.replace('namespace CorpusFsaProbe', 'namespace CorpusFsaProbeP186', 1)
key = '                if (a.StartsWith("--knot-fwhm=", StringComparison.Ordinal))'
assert t.count(key) == 1
ins = (
'                if (a.StartsWith("--fwhm-low=", StringComparison.Ordinal))' + nl +
'                {' + nl +
'                    // П186: рычаг опыта — доуширение низа шкалы (A:E0:SPAN).' + nl +
'                    string[] w = a.Substring(11).Split(\':\');' + nl +
'                    LowWidenFwhm.A = double.Parse(w[0], CultureInfo.InvariantCulture);' + nl +
'                    LowWidenFwhm.E0 = double.Parse(w[1], CultureInfo.InvariantCulture);' + nl +
'                    LowWidenFwhm.Span = double.Parse(w[2], CultureInfo.InvariantCulture);' + nl +
'                    continue;' + nl +
'                }' + nl + nl)
t = t.replace(key, ins + key, 1)
ret = ('                if (cfg.FwhmCalibration != null)' + nl +
       '                {' + nl +
       '                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();' + nl +
       '                }' + nl +
       '            }' + nl + nl +
       '            return rd;')
assert t.count(ret) == 1, t.count(ret)
t = t.replace(ret, ret.replace('            return rd;',
       '            if (LowWidenFwhm.A != 0.0 && rd.FwhmCalibration != null && rd.EnergySpectrum != null)' + nl +
       '            {' + nl +
       '                rd.FwhmCalibration = new LowWidenFwhm(rd.FwhmCalibration, rd.EnergySpectrum.EnergyCalibration);' + nl +
       '            }' + nl + nl +
       '            return rd;'), 1)
cls = r'''
    /// <summary>П186: обёртка кривой ПШПВ для опыта — множитель низа шкалы.</summary>
    sealed class LowWidenFwhm : FwhmCalibration
    {
        public static double A, E0 = 60.0, Span = 32.0;
        readonly FwhmCalibration inner;
        readonly EnergyCalibration ecal;
        public LowWidenFwhm(FwhmCalibration inner, EnergyCalibration ecal) { this.inner = inner; this.ecal = ecal; }
        double Factor(double ch)
        {
            double e = ecal.ChannelToEnergy(ch);
            double x = (E0 - e) / Span;
            return 1.0 + A * (x > 0.0 ? x : 0.0);
        }
        public override double ChannelToFwhm(double channel) { return inner.ChannelToFwhm(channel) * Factor(channel); }
        public override double FwhmToChannel(double fwhm) { return inner.FwhmToChannel(fwhm); }
        public override List<CalibrationPeak> CalibrationPeaks { get => inner.CalibrationPeaks; set => inner.CalibrationPeaks = value; }
        public override double[] Coefficients { get => inner.Coefficients; set => inner.Coefficients = value; }
        public override bool PerformCalibration(int maxchannels) { return inner.PerformCalibration(maxchannels); }
        public override FwhmCalibration Clone() { return new LowWidenFwhm(inner.Clone(), ecal); }
        public override void RescaleCoefficients(double mul) { inner.RescaleCoefficients(mul); }
        public override string GetFormula() { return inner.GetFormula(); }
        public override string ToString() { return inner.ToString(); }
        public override bool NotCalibrated() { return inner.NotCalibrated(); }
        public override int MinPeaksRequirement() { return inner.MinPeaksRequirement(); }
        public override int PeakType { get => inner.PeakType; set => inner.PeakType = value; }
        public override double ExpGaussExpLeftTail { get => inner.ExpGaussExpLeftTail; set => inner.ExpGaussExpLeftTail = value; }
        public override double ExpGaussExpRightTail { get => inner.ExpGaussExpRightTail; set => inner.ExpGaussExpRightTail = value; }
        public override double VoigtSigma { get => inner.VoigtSigma; set => inner.VoigtSigma = value; }
        public override double VoigtGamma { get => inner.VoigtGamma; set => inner.VoigtGamma = value; }
        public override double GaussianChi2Total { get => inner.GaussianChi2Total; set => inner.GaussianChi2Total = value; }
        public override double ExpGaussExpChi2Total { get => inner.ExpGaussExpChi2Total; set => inner.ExpGaussExpChi2Total = value; }
        public override double VoigtChi2Total { get => inner.VoigtChi2Total; set => inner.VoigtChi2Total = value; }
        public override double Chi2pNdp { get => inner.Chi2pNdp; set => inner.Chi2pNdp = value; }
    }
'''.replace('\n', nl)
anchor = '    static class Program'
assert t.count(anchor) == 1
t = t.replace(anchor, cls.lstrip(nl) + nl + anchor, 1)
hdr = '// П186 30.09.2026: копия CorpusFsaProbe.cs с рычагом опыта --fwhm-low=A:E0:SPAN (класс LowWidenFwhm); вне полосы не использовать.' + nl
io.open(q, 'w', encoding='utf-8-sig', newline='').write(hdr + t)
print('ok', len(t))
