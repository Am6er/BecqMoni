import sys, re
import numpy as np
sys.stdout.reconfigure(encoding='utf-8')
G = 'D:/BqMoni_Claude/p338/g4/'; OUT = 'D:/BqMoni_Claude/p338/show/'
DEV_TPL = r'C:/Users/moroz/source/repos/BQ Eng res .NET 4.8/tools/fsa_showcase/config/device/Atom Spectra 80x80.xml'
DEV_GUID = 'a7c0e2b4-3380-4b8e-9d2a-2020a0c33801'   # постоянный, своей полосы
DEV_NAME = 'Atom GS2020 PRO MAX (P338)'


def INV(x):
    return repr(float(x))


def csv(fn):
    hdr = {}; c = []
    for line in open(G + fn, encoding='utf-8'):
        if line.startswith('#'):
            k, _, v = line[1:].partition(':'); hdr.setdefault(k.strip(), []).append(v.strip()); continue
        if line.startswith('channel'):
            continue
        c.append(float(line.split(',')[1]))
    return hdr, np.array(c)


def coeffs(s):
    return [float(x) for x in re.search(r'coeffs:\s*([-0-9.eE+ ]+)', s).group(1).split()[:5]]


def E(cf, ch):
    return sum(c * np.asarray(ch, float) ** i for i, c in enumerate(cf))


K, P = 0.7880103601331053, 0.6335125944403551   # их закон ПШПВ(E) = k·E^p, кэВ (страница, fwhm_cal)
PTS = [(238.632, 24.073), (583.187, 40.624), (911.204, 57.351), (964.766, 59.686), (968.971, 59.867), (2614.511, 107.42)]


def fwhm_ch(cf):
    ch = np.arange(300, 6000, 10.0); e = E(cf, ch); sl = np.gradient(e, ch)
    fw = K * e ** P / sl; p, lna = np.polyfit(np.log(ch), np.log(fw), 1)
    return float(np.exp(lna)), float(p), float(np.max(np.abs(np.exp(lna) * ch ** p / fw - 1)))


def chan(cf, e0):
    ch = np.arange(0, 8192, 0.01); return float(np.interp(e0, E(cf, ch), ch))


def spec_block(tag, cf, counts, live, real, s):
    out = [f'{s}<{tag}>', f'{s}  <NumberOfChannels>8192</NumberOfChannels>', f'{s}  <ChannelPitch>1</ChannelPitch>',
           f'{s}  <EnergyCalibration>', f'{s}    <PolynomialOrder>4</PolynomialOrder>', f'{s}    <Coefficients>']
    out += [f'{s}      <Coefficient>{INV(c)}</Coefficient>' for c in cf]
    tot = int(round(counts.sum()))
    out += [f'{s}    </Coefficients>', f'{s}  </EnergyCalibration>', f'{s}  <ValidPulseCount>{tot}</ValidPulseCount>',
            f'{s}  <TotalPulseCount>{tot}</TotalPulseCount>', f'{s}  <MeasurementTime>{INV(real)}</MeasurementTime>',
            f'{s}  <LiveTime>{INV(live)}</LiveTime>', f'{s}  <NumberOfSamples>0</NumberOfSamples>', f'{s}  <Spectrum>']
    out += [f'{s}    <DataPoint>{int(round(v))}</DataPoint>' for v in counts]
    out += [f'{s}  </Spectrum>', f'{s}</{tag}>']
    return out


def fwhm_block(cf, s):
    a, p, dev = fwhm_ch(cf)
    out = [f'{s}<PowerFwhmCalibration>', f'{s}  <CalibrationPeaks>']
    for e0, f in PTS:
        c = chan(cf, e0); sl = (E(cf, c + 0.5) - E(cf, c - 0.5))
        out += [f'{s}    <Peak>', f'{s}      <Channel>{int(round(c))}</Channel>', f'{s}      <Energy>{INV(e0)}</Energy>',
                f'{s}      <FWHM>{INV(1.05 * f / sl)}</FWHM>', f'{s}    </Peak>']
    out += [f'{s}  </CalibrationPeaks>', f'{s}  <Coefficients>', f'{s}    <Coefficient>{INV(a)}</Coefficient>',
            f'{s}    <Coefficient>{INV(p)}</Coefficient>', f'{s}  </Coefficients>', f'{s}  <PeakType>0</PeakType>',
            f'{s}  <ExpGaussExpLeftTail>1.2</ExpGaussExpLeftTail>', f'{s}  <ExpGaussExpRightTail>2.6</ExpGaussExpRightTail>',
            f'{s}  <VoigtSigma>1</VoigtSigma>', f'{s}  <VoigtGamma>1</VoigtGamma>', f'{s}</PowerFwhmCalibration>']
    return out, a, p, dev


SRC = ('спектр и фон — CSV репозитория VibeEngineering-LLC/geant4-detector-models (Atom-GS2020-PRO-MAX/spectra), '
       'шкалы — page_scale их страницы; П338 AMBER234')
hb, bg = csv('bg_marinelli_water_1l_33.2h.csv'); bg[-1] = 0
dev = None
for key, fn, bgi, name, note in (
        ('GS2020_Th232_oisn10', 'th232_reference_marinelli_1l.csv', 1, 'Th-232 OISN-10 1052 g (VibeEngineering)',
         'Th-232 ОИСН-10, Маринелли 1 л, 1052 г, 910 Бк/кг ± 6 %; ' + SRC),
        ('GS2020_berry_482g', 'berry_dried_marinelli_1l_482g_49.8h.csv', 2, 'Dried blueberry 482 g (VibeEngineering)',
         'Сушёная черника 482 г, Маринелли 1 л, 49.8 ч; ' + SRC)):
    hs, y = csv(fn); y[-1] = 0
    cs = coeffs(hs['page_scale_1'][0]); cb = coeffs(hb['page_scale_%d' % bgi][0])
    L = ['<?xml version="1.0" encoding="utf-8"?>',
         '<ResultDataFile xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">',
         '  <FormatVersion>120920</FormatVersion>', '  <ResultDataList>', '    <ResultData>', '      <SampleInfo>',
         f'        <Name>{name}</Name>', '        <Location />', f'        <Time>{hs["start_time"][0]}</Time>',
         '        <Weight>1</Weight>', '        <Volume>1</Volume>', f'        <Note><![CDATA[{note}]]></Note>', '      </SampleInfo>',
         '      <DeviceConfigReference>', f'        <Name>{DEV_NAME}</Name>', f'        <Guid>{DEV_GUID}</Guid>', '      </DeviceConfigReference>',
         '      <BackgroundSpectrumFile>bg_marinelli_water_1l_33.2h.csv</BackgroundSpectrumFile>',
         f'      <StartTime>{hs["start_time"][0]}</StartTime>', f'      <EndTime>{hs["end_time"][0]}</EndTime>',
         '      <PresetTime>360000000</PresetTime>']
    L += spec_block('EnergySpectrum', cs, y, float(hs['live_time_s'][0]), float(hs['real_time_s'][0]), '      ')
    L += spec_block('BackgroundEnergySpectrum', cb, bg, float(hb['live_time_s'][0]), float(hb['real_time_s'][0]), '      ')
    L += ['      <Visible>true</Visible>', '      <PulseCollection>', '        <Format>Base64 encoded binary</Format>',
          '        <Pulses />', '      </PulseCollection>']
    fb, a, p, d = fwhm_block(cs, '      '); L += fb
    L += ['    </ResultData>', '  </ResultDataList>', '</ResultDataFile>']
    open(OUT + 'spectra/' + key + '.xml', 'wb').write(('\ufeff' + '\n'.join(L) + '\n').encode('utf-8'))
    print(f'{key}: отсчётов {y.sum():.0f}, живое {hs["live_time_s"][0]} с; ПШПВ в каналах a={a:.4f} p={p:.4f} '
          f'(откл. от закона ≤ {100 * d:.2f} %)')
    if key.startswith('GS2020_berry'):
        dev = (a, p, cs)

# прибор: копия AS80 витрины, новый Guid/имя, ПШПВ GS2020, своих кривых нет
a, p, cs = dev
t = open(DEV_TPL, encoding='utf-8-sig').read()
t = t.replace('<Guid>33100348-421f-475d-adda-33736e6af7f8</Guid>', f'<Guid>{DEV_GUID}</Guid>', 1)
t = t.replace('<Name>Atom Spectra 80x80</Name>', f'<Name>{DEV_NAME}</Name>', 1)
t = re.sub(r'(<PolynomialEnergyCalibration>\s*<PolynomialOrder>4</PolynomialOrder>\s*<Coefficients>).*?(</Coefficients>)',
           lambda m: m.group(1) + ''.join(f'\n        <Coefficient>{INV(c)}</Coefficient>' for c in cs) + '\n      ' + m.group(2),
           t, count=1, flags=re.S)
t = re.sub(r'(<PowerFwhmCalibration>.*?<Coefficients>).*?(</Coefficients>\s*<PeakType>)1(</PeakType>)',
           lambda m: m.group(1) + f'\n          <Coefficient>{INV(a)}</Coefficient>\n          <Coefficient>{INV(p)}</Coefficient>\n        '
           + m.group(2) + '0' + m.group(3), t, count=1, flags=re.S)
t = re.sub(r'<EfficiencyConfigs>.*?</EfficiencyConfigs>', '<EfficiencyConfigs />', t, count=1, flags=re.S)
open(OUT + 'config/device/' + DEV_NAME + '.xml', 'wb').write(('\ufeff' + t).encode('utf-8'))
print('прибор записан; Guid', t.count(DEV_GUID), 'раз; кривых', t.count('<EfficiencyConfigData>'))
