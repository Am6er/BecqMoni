# П56: читатель спектров BecqMoni (.xml) — каналы, калибровка (полином), живое время, фон (встроенный или файл)
import re, math, numpy as np
def _floats(block, tag):
    return [float(x) for x in re.findall(r'<%s>([^<]+)</%s>'%(tag,tag), block)]
class Spec:
    def __init__(self, path, bgpath=None):
        with open(path, encoding='utf-8') as f: t=f.read()
        # первый <EnergySpectrum> — проба; <BackgroundEnergySpectrum> — фон
        m=re.search(r'<EnergySpectrum>(.*?)</EnergySpectrum>', t, re.S)
        self.name=path
        self._parse(m.group(1), 'sample')
        mb=re.search(r'<BackgroundEnergySpectrum>(.*?)</BackgroundEnergySpectrum>', t, re.S)
        self.bg=None
        if mb:
            self.bg=Spec.__new__(Spec); self.bg._parse(mb.group(1),'bg'); self.bg.name=path+'#bg'
        elif bgpath:
            self.bg=Spec(bgpath)
        self.bgfile=(re.search(r'<BackgroundSpectrumFile>([^<]*)<', t) or [None,None])[1]
    def _parse(self, b, kind):
        self.n=int(re.search(r'<NumberOfChannels>(\d+)', b).group(1))
        self.coef=_floats(re.search(r'<Coefficients>(.*?)</Coefficients>', b, re.S).group(1), 'Coefficient')
        self.live=float(re.search(r'<LiveTime>([^<]+)', b).group(1))
        self.real=float(re.search(r'<MeasurementTime>([^<]+)', b).group(1))
        self.counts=np.array(_floats(re.search(r'<Spectrum>(.*?)</Spectrum>', b, re.S).group(1), 'DataPoint'))
        assert len(self.counts)==self.n, (len(self.counts), self.n)
        ch=np.arange(self.n, dtype=float)
        self.keV=sum(c*ch**i for i,c in enumerate(self.coef))
        # границы каналов (для сумм по кэВ)
        chb=np.arange(self.n+1, dtype=float)-0.5
        self.keVb=sum(c*chb**i for i,c in enumerate(self.coef))
    def E2ch(self, E):
        return float(np.interp(E, self.keV, np.arange(self.n)))
    def net(self):
        """проба − фон, приведённый по живому времени (в отсчётах пробы); фон перебинирован по своей шкале в шкалу пробы"""
        if self.bg is None: return self.counts.copy(), self.counts.copy()
        k=self.live/self.bg.live
        bgc=rebin(self.bg, self.keVb)*k
        return self.counts-bgc, bgc
def rebin(src, keVb_dst):
    """перенос отсчётов src (по границам src.keVb) в сетку границ keVb_dst — линейно внутри канала"""
    cum=np.concatenate([[0.0], np.cumsum(src.counts)])
    cdst=np.interp(keVb_dst, src.keVb, cum)
    return np.diff(cdst)
def fwhm_of(spec, E, p662=7.65):
    return p662/100*662*math.sqrt(E/662)
