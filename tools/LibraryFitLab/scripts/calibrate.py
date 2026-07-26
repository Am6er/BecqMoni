# -*- coding: utf-8 -*-
"""Re-calibrate the eight test spectra (energy + FWHM) into lab/spectra/.

Stage 1  the candidate calibration lines are curated per spectrum *by the
         detector's own resolution*: a table line is usable only if no other
         line of the same sample with at least 20% of its intensity sits within
         1.5 FWHM. That is what kills Ac-228 911/965/969 on the 1024-channel
         RC-103 (they merge into one 934 keV lump) and Tl-208 583 / Bi-214 609
         in granite, both of which dragged the polynomial before.

Stage 2  per spectrum: iterative energy recalibration. Position tolerance starts
         wide (3.5%, enough to absorb a wrong gain) and tightens as the
         polynomial is refitted. Weak spectra fall back to the gain of the
         strong spectrum of the same detector when their own fit is worse.

Stage 3  per detector: one resolution model FWHM[keV](E) fitted to every
         accepted peak of every spectrum of that detector -- resolution belongs
         to the crystal, not to the sample, so the 363-second uranium glass
         inherits it instead of being fitted on its own noise. The model is
         projected back into each spectrum's channel space and written out as
         SqrtFwhmCalibration coefficients, monotonicity enforced.
"""
import os
import json
import numpy as np
import xml.etree.ElementTree as ET
from spectrum import Spectrum, monotone
from gaussfit import fit_peak, FWHM_SIGMA
from chains import chain_lines, CHAINS
import gainscan

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, 'spectra')

SPECTRA = [
    dict(det='ASN16', key='ASN16_Th232', chains=['Th-232'], extra='WT',
         path=r'C:\Users\moroz\YandexDisk\Спектры\!ASN16\Th-232 16.06.2026.xml'),
    dict(det='ASN16', key='ASN16_Charoite', chains=['Ra-226'], extra='',
         path=r'C:\Users\moroz\YandexDisk\Спектры\!ASN16\Чароит в домике.xml'),
    dict(det='ASN16', key='ASN16_UGlass', chains=['U-238u', 'U-235'], extra='',
         path=r'C:\Users\moroz\YandexDisk\Спектры\!ASN16\UGlass nos 12.xml'),
    dict(det='ASN16', key='ASN16_Granite', chains=['Th-232', 'U-238', 'U-235'], extra='',
         path=r'C:\Users\moroz\YandexDisk\Спектры\!ASN16\Гранит.xml'),
    dict(det='AS80x80', key='AS80_Th232WT20', chains=['Th-232'], extra='WT',
         path=r'C:\Users\moroz\YandexDisk\Спектры\!AS80x80\Th232(WT-20).xml'),
    # 28.08.2025 pair: same detector, far better statistics than the originals
    # (uranium glass 23 960 s / 8.6 M counts against 363 s / 127 k), and both
    # carry a measured background spectrum, which none of the older files do.
    dict(det='AS80x80', key='AS80_Th232_v2', chains=['Th-232'], extra='',
         path=r'C:\Users\moroz\YandexDisk\Спектры\!AS80x80\28.08.2025\Th-232.xml'),
    dict(det='AS80x80', key='AS80_UGlass', chains=['U-238u', 'U-235'], extra='',
         path=r'C:\Users\moroz\YandexDisk\Спектры\!AS80x80\28.08.2025\UGlass.xml'),
    dict(det='AS80x80', key='AS80_Charoite', chains=['Ra-226'], extra='',
         path=r'C:\Users\moroz\YandexDisk\Спектры\!AS80x80\Камни\Чароит.xml'),
    # At 1024 channels almost every Th-232 line is part of a blend, and the
    # automatic anchor picker keeps drifting between solutions: with the
    # Ac-228 911/965/969 lump (49% pure) in, the fit goes linear and lands 13 keV
    # out; without 2614 it extrapolates the low end across the whole range. The
    # four anchors below are the ones the detector genuinely resolves, so they
    # are named explicitly instead of being rediscovered each run.
    dict(det='RC103', key='RC103_Th232WT20', chains=['Th-232'], extra='WT',
         wanted=[238.63, 338.32, 583.19, 2614.51],
         path=r'C:\Users\moroz\YandexDisk\Спектры\!КОТ-103\Th-232 WT-20.xml'),
]

# The strongest spectrum of each detector, used as the gain reference for the
# weak ones and as the backbone of the resolution model.
REFERENCE = {'ASN16': 'ASN16_Th232', 'AS80x80': 'AS80_Th232WT20', 'RC103': 'RC103_Th232WT20'}

GUID_FIX = {'5ca5111f-0794-4789-8ece-32943a19329c': '33100348-421f-475d-adda-33736e6af7f8'}

# Lines that are always present on top of the sample: K-40 from the room and
# the 511 keV annihilation continuum peak.
AMBIENT = [(1460.82, 3.0, 'K-40'), (511.0, 2.0, 'annih')]
# Tungsten K X-rays of the WT-20 electrode.
W_XRAY = [(57.98, 30.0, 'W Ka2'), (59.32, 52.0, 'W Ka1'), (67.24, 18.0, 'W Kb')]


def sample_lines(entry):
    """All lines the sample can emit, with chain-normalised intensity."""
    rows = []
    for ch in entry['chains']:
        if ch == 'U-238u':
            # uranium glass: the series is broken at Ra-226, only the head lives
            for r in chain_lines('238U'):
                if r['nucid'] in ('238U', '234TH', '234PAm1', '234PA', '234U'):
                    rows.append((r['energy'], r['i_chain'], r['name']))
            continue
        for r in chain_lines(CHAINS[ch]):
            rows.append((r['energy'], r['i_chain'], r['name']))
    rows.extend(AMBIENT)
    if entry.get('extra') == 'WT':
        rows.extend(W_XRAY)
    rows.sort()
    return rows


# Reference lines we would like to use, if the detector can resolve them.
WANTED = [63.29, 92.60, 143.76, 185.72, 238.63, 295.22, 338.32, 351.93, 583.19,
          609.32, 727.33, 766.38, 911.20, 968.97, 1001.03, 1120.29, 1238.12,
          1460.82, 1588.20, 1764.49, 2204.10, 2614.51]


def blend(energy, rows, fwhm_kev, span=1.0):
    """What the detector actually sees at `energy`.

    Everything within span*FWHM merges into one observed peak, so the position
    to calibrate against is the intensity-weighted centroid of the group, not
    the table energy of its strongest member. Returns (e_eff, purity) where
    purity is the strongest member's share -- a group whose leader owns less
    than half of the intensity is not a usable anchor, and only nearly pure
    groups may feed the resolution model.
    """
    mine = max((i for e, i, _ in rows if abs(e - energy) < 0.6), default=0.0)
    if mine <= 0:
        return None, 0.0
    num = 0.0
    den = 0.0
    for e, i, _ in rows:
        if abs(e - energy) <= span * fwhm_kev:
            num += i * e
            den += i
    if den <= 0:
        return None, 0.0
    return num / den, mine / den


def curate(entry, res_fn, min_purity=0.60):
    rows = sample_lines(entry)
    out = []
    for e in entry.get('wanted') or WANTED:
        hit = [(en, i, n) for en, i, n in rows if abs(en - e) < 0.6]
        if not hit:
            continue
        e_eff, purity = blend(e, rows, res_fn(e))
        if e_eff is None or purity < min_purity:
            continue
        label = hit[0][2] if purity > 0.9 else '%s~%.0f%%' % (hit[0][2], 100 * purity)
        out.append((e_eff, label, purity, e))
    # two wanted lines that merged into the same group would be fitted twice
    out.sort()
    dedup = []
    for item in out:
        if dedup and abs(item[0] - dedup[-1][0]) < 0.3 * res_fn(item[0]):
            if item[2] > dedup[-1][2]:
                dedup[-1] = item
            continue
        dedup.append(item)
    return dedup


class Ecal(object):
    def __init__(self, coef, nmax):
        self.coef = np.asarray(coef, dtype=float)
        self.nmax = nmax
        self._grid = np.arange(0, nmax, dtype=float)
        self._e = self.energy(self._grid)

    def energy(self, ch):
        ch = np.asarray(ch, dtype=float)
        return sum(c * ch ** i for i, c in enumerate(self.coef))

    def channel(self, e):
        return float(np.interp(e, self._e, self._grid))

    def dEdch(self, ch):
        return float(sum(i * c * float(ch) ** (i - 1) for i, c in enumerate(self.coef) if i >= 1))

    def monotone(self):
        return bool(np.all(np.diff(self._e) > 0))


def res_seed(r662):
    return lambda e: r662 * np.sqrt(662.0 / max(float(e), 5.0)) * max(float(e), 5.0)


def measure(counts, ecal, lines, res_fn, tol_frac, min_sig=4.0, width_tol=None):
    n = len(counts)
    out = []
    for e_ref, label, purity, e_table in lines:
        ch0 = ecal.channel(e_ref)
        if ch0 < 4 or ch0 > n - 5:
            continue
        dedch = ecal.dEdch(ch0)
        if dedch <= 0:
            continue
        sigma0 = max(res_fn(e_ref) / dedch / FWHM_SIGMA, 1.0)
        # The linear calibration is already fixed by the gain scan, so the peak
        # can only be a fraction of a width away. A wide search here is what let
        # earlier versions latch onto the neighbouring stronger line.
        slack = tol_frac * ch0 + 0.6 * sigma0 * FWHM_SIGMA
        r = fit_peak(counts, ch0, sigma0, window=2.4)
        if r is None or r['sig'] < min_sig:
            continue
        if abs(r['mu'] - ch0) > slack:
            continue
        ratio = r['fwhm'] / (sigma0 * FWHM_SIGMA)
        lo, hi = (0.5, 2.0) if width_tol is None else (1.0 - width_tol, 1.0 + width_tol)
        if ratio < lo or ratio > hi:
            continue
        out.append(dict(e_ref=e_ref, label=label, purity=purity, e_table=e_table,
                        ch=r['mu'], fwhm=r['fwhm'],
                        sig=r['sig'], area=r['area'], chi2=r['chi2ndf']))
    out.sort(key=lambda a: a['ch'])
    dedup = []
    for a in out:
        if dedup and abs(a['ch'] - dedup[-1]['ch']) < 0.4 * min(a['fwhm'], dedup[-1]['fwhm']):
            if a['sig'] > dedup[-1]['sig']:
                dedup[-1] = a
            continue
        dedup.append(a)
    return dedup


def fit_ecal(accepted, order, nmax, reference=None, max_bend=0.15):
    """Least-squares energy polynomial, guarded against wild extrapolation.

    A quadratic fitted to lines that only span part of the range can pass through
    every point and still be nonsense outside their support - the 28.08.2025
    Th-232 run produced `41.9 + 0.257*ch + 4.5e-5*ch^2`, which agrees with its
    five anchors between channels 689 and 2510 and reaches 5133 keV at channel
    8191, about 70% above where the gain says it should be.

    The guard is against *bending*, not against the stored calibration: these
    detectors are close to linear, so the fit may not depart from a straight line
    through its own anchors by more than max_bend anywhere in the range. Testing
    against the stored polynomial instead was tried and does not work - the
    stored 4th-order curves do their own excursions at the ends and punish good
    refits for it.
    """
    ch = np.array([a['ch'] for a in accepted])
    e = np.array([a['e_ref'] for a in accepted])
    w = np.sqrt(np.minimum([a['sig'] for a in accepted], 100.0))
    order = int(min(order, len(ch) - 1))
    line = np.polyfit(ch, e, 1, w=w) if len(ch) >= 2 else None
    lo = max(5.0, 0.5 * float(ch.min()))
    while order >= 1:
        A = np.vstack([ch ** i for i in range(order + 1)]).T * w[:, None]
        coef, *_ = np.linalg.lstsq(A, e * w, rcond=None)
        cal = Ecal(coef, nmax)
        if cal.monotone() and bend_ok(cal, line, nmax, max_bend, lo):
            return cal, order
        order -= 1
    return None, 0


def bend_ok(cal, line, nmax, max_bend, lo=5.0):
    """Checked only from half the lowest anchor upwards. Below that the straight
    reference line runs negative, so the comparison is meaningless - and it cost
    RC-103 its correct quadratic, rejected by 1.8 keV at channel 5."""
    if line is None:
        return True
    grid = np.arange(lo, nmax, max(1, nmax // 400), dtype=float)
    straight = np.polyval(line, grid)
    tol = np.maximum(40.0, max_bend * np.abs(straight))
    return bool(np.all(np.abs(cal.energy(grid) - straight) <= tol))


def rms(accepted, ecal):
    if not accepted:
        return float('nan')
    d = np.array([ecal.energy(a['ch']) - a['e_ref'] for a in accepted])
    return float(np.sqrt((d ** 2).mean()))


def measured_r662(accepted, ecal, default=0.08):
    if len(accepted) < 2:
        return default
    w = np.array([a['fwhm'] * ecal.dEdch(a['ch']) for a in accepted])
    e = np.array([a['e_ref'] for a in accepted])
    return float(np.median(w / e * np.sqrt(e / 662.0)))


def validate(sp, entry, ecal, r662, own=None):
    """Score a candidate calibration on a FIXED set of clean lines.

    Candidates cannot be compared by the residual of their own accepted anchors:
    each accepts a different set, and one that latches onto four easy lines beats
    one that fits eight honestly. Here every candidate is measured against the
    same lines, so the numbers mean the same thing.

    The purity bar is relaxed until at least three lines qualify - on the
    1024-channel RC-103 almost nothing is pure, and a fixed 0.85 bar left every
    candidate with two lines, an infinite score and an arbitrary winner.

    A gain that runs away from the stored one is penalised: the stored
    calibration can be a couple of per cent out, never twenty.
    """
    penalty = 0.0
    stored = Ecal(sp.ecal, sp.n)
    gain = (ecal.energy(sp.n - 1) - ecal.energy(0)) / max(sp.n - 1, 1)
    gain_ref = (stored.energy(sp.n - 1) - stored.energy(0)) / max(sp.n - 1, 1)
    if gain_ref > 0:
        drift = abs(gain - gain_ref) / gain_ref
        if drift > 0.06:
            penalty = 1000.0 * (drift - 0.06)

    res_fn = res_seed(r662)
    for purity in (0.85, 0.75, 0.60):
        lines = curate(entry, res_fn, min_purity=purity)
        hits = measure(sp.counts, ecal, lines, res_fn, tol_frac=0.005, width_tol=0.45)
        if len(hits) >= 3:
            d = np.array([ecal.energy(a['ch']) - a['e_ref'] for a in hits])
            return float(np.sqrt((d ** 2).mean())) + penalty, len(hits)
    return (own if own is not None else 1e6) + penalty, len(hits)


def calibrate_energy(sp, entry, start_ecal=None, max_order=2, r662_seed=0.07,
                     min_purity=0.60):
    """The stored calibration is never more than a peak width out, so a single
    Gauss-Newton fit seeded at the expected channel converges onto the right
    peak; the acceptance slack below only decides whether to keep the result.
    A multi-start scan was tried and removed - it latched onto the neighbouring
    stronger line - as was a global gain scan, which aliased onto harmonics of
    the line pattern."""
    r662 = r662_seed
    res_fn = res_seed(r662)
    stored = Ecal(sp.ecal, sp.n)
    ecal = Ecal(start_ecal.coef if start_ecal is not None else sp.ecal, sp.n)

    lines = curate(entry, res_fn, min_purity=min_purity)
    accepted = measure(sp.counts, ecal, lines, res_fn, tol_frac=0.02)
    if len(accepted) >= 2:
        r662 = measured_r662(accepted, ecal, r662)
        res_fn = res_seed(r662)
        lines = curate(entry, res_fn, min_purity=min_purity)
    history = [('start', ecal, accepted)]
    for tol, wt in ((0.012, None), (0.006, 0.45), (0.004, 0.35)):
        if len(accepted) < 3:
            break
        cal, order = fit_ecal(accepted, max_order, sp.n, reference=stored)
        if cal is None:
            break
        nxt = measure(sp.counts, cal, lines, res_fn, tol_frac=tol, width_tol=wt)
        if len(nxt) < 3:
            break
        ecal, accepted = cal, nxt
        r662 = measured_r662(accepted, ecal, r662)
        res_fn = res_seed(r662)
        lines = curate(entry, res_fn, min_purity=min_purity)
        history.append(('refit%d' % order, ecal, accepted))

    # One robust pass: an anchor whose residual is a wild outlier is a
    # mis-assignment, not a calibration error, and a single one of them wrecks a
    # weak spectrum (the 1584-second charoite pulled 1246 keV in at -43 keV).
    if len(accepted) >= 5:
        d = np.array([ecal.energy(a['ch']) - a['e_ref'] for a in accepted])
        mad = np.median(np.abs(d - np.median(d))) * 1.4826
        limit = max(3.0 * mad, 12.0)
        keep = [a for a, dd in zip(accepted, d) if abs(dd - np.median(d)) <= limit]
        if 4 <= len(keep) < len(accepted):
            cal, order = fit_ecal(keep, max_order, sp.n)
            if cal is not None and rms(keep, cal) < rms(accepted, ecal):
                ecal, accepted = cal, keep
                history.append(('robust%d' % order, ecal, accepted))
    return ecal, accepted, r662, history, lines


def fit_resolution_kev(points):
    e = np.array([p[0] for p in points], dtype=float)
    f = np.array([p[1] for p in points], dtype=float)
    w = np.sqrt(np.array([p[2] for p in points], dtype=float))
    for order in (2, 1):
        A = np.vstack([e ** i for i in range(order + 1)]).T * w[:, None]
        coef, *_ = np.linalg.lstsq(A, f ** 2 * w, rcond=None)
        coef = np.concatenate([coef, np.zeros(3 - len(coef))])
        grid = np.linspace(10.0, max(e.max() * 1.4, 3000.0), 500)
        v = coef[0] + coef[1] * grid + coef[2] * grid ** 2
        if np.all(v > 0) and np.all(np.diff(np.sqrt(v)) > -1e-9):
            return coef
    k = float((f ** 2 * e * w).sum() / max((e * e * w).sum(), 1e-9))
    return np.array([0.0, k, 0.0])


def resolution_fn(coef):
    def f(e):
        v = coef[0] + coef[1] * float(e) + coef[2] * float(e) ** 2
        return float(np.sqrt(max(v, 1e-6)))
    return f


def fwhm_channel_coef(ecal, res_fn, nmax):
    ch = np.linspace(1.0, nmax - 1, 400)
    fw = np.array([res_fn(ecal.energy(c)) / max(ecal.dEdch(c), 1e-9) for c in ch])
    for order in (2, 1):
        A = np.vstack([ch ** i for i in range(order + 1)]).T
        coef, *_ = np.linalg.lstsq(A, fw ** 2, rcond=None)
        coef = np.concatenate([coef, np.zeros(3 - len(coef))])
        if monotone(coef, nmax) and (coef[0] + coef[1] * ch[-1] + coef[2] * ch[-1] ** 2) > 0:
            return coef
    k = float((fw ** 2 * ch).sum() / max((ch * ch).sum(), 1e-9))
    return np.array([0.0, k, 0.0])


def write_spectrum(entry, ecal_coef, fwhm_coef, peak_type=0, left=1.0, right=1.0):
    tree = ET.parse(entry['path'])
    root = tree.getroot()
    rd = root.find('ResultDataList/ResultData')
    guid_el = rd.find('DeviceConfigReference/Guid')
    if guid_el is not None and guid_el.text in GUID_FIX:
        guid_el.text = GUID_FIX[guid_el.text]
    def apply_ecal(spectrum_element):
        cal = spectrum_element.find('EnergyCalibration')
        if cal is None:
            return
        order = cal.find('PolynomialOrder')
        if order is not None:
            order.text = str(len(ecal_coef) - 1)
        coefs = cal.find('Coefficients')
        for child in list(coefs):
            coefs.remove(child)
        for c in ecal_coef:
            ET.SubElement(coefs, 'Coefficient').text = repr(float(c))

    es = rd.find('EnergySpectrum')
    apply_ecal(es)
    # The 28.08.2025 files carry a measured background whose stored calibration
    # is identical to the foreground's. Recalibrating only the foreground would
    # leave the two on different scales, and BuildFixedBackground would then map
    # background channels through the stale polynomial.
    background = rd.find('BackgroundEnergySpectrum')
    if background is not None:
        apply_ecal(background)
    lt = es.find('LiveTime')
    if lt is None or not (lt.text or '').strip():
        if lt is None:
            lt = ET.SubElement(es, 'LiveTime')
        lt.text = es.find('MeasurementTime').text
    old = rd.find('SqrtFwhmCalibration')
    if old is not None:
        rd.remove(old)
    fw = ET.SubElement(rd, 'SqrtFwhmCalibration')
    ET.SubElement(fw, 'CalibrationPeaks')
    cs = ET.SubElement(fw, 'Coefficients')
    for c in fwhm_coef:
        ET.SubElement(cs, 'Coefficient').text = repr(float(c))
    ET.SubElement(fw, 'PeakType').text = str(peak_type)
    ET.SubElement(fw, 'ExpGaussExpLeftTail').text = repr(float(left))
    ET.SubElement(fw, 'ExpGaussExpRightTail').text = repr(float(right))
    ET.SubElement(fw, 'Chi2pNdp').text = '-1'
    if not os.path.isdir(OUT):
        os.makedirs(OUT)
    dest = os.path.join(OUT, entry['key'] + '.xml')
    tree.write(dest, encoding='utf-8', xml_declaration=True)
    return dest


def main():
    # --- pass A: reference spectrum of each detector ---
    state = {}
    for entry in SPECTRA:
        sp = Spectrum(entry['path'])
        state[entry['key']] = dict(entry=entry, sp=sp)

    PURITIES = (0.45, 0.60, 0.80)
    for det, key in REFERENCE.items():
        st = state[key]
        best = None
        for purity in PURITIES:
            ecal, acc, r662, hist, lines = calibrate_energy(
                st['sp'], st['entry'], min_purity=purity)
            score, n = validate(st['sp'], st['entry'], ecal, r662,
                                own=rms(acc, ecal) if len(acc) >= 3 else None)
            if best is None or (score, -n) < (best[0], -best[1]):
                best = (score, n, ecal, acc, r662, hist, lines, purity)
        _, _, ecal, acc, r662, hist, lines, purity = best
        st.update(ecal=ecal, accepted=acc, r662=r662, hist=hist, lines=lines,
                  mode='self/p%.2f' % purity)

    # --- pass B: the rest, self-fit vs inheriting the reference gain ---
    for entry in SPECTRA:
        st = state[entry['key']]
        if 'ecal' in st:
            continue
        ref = state[REFERENCE[entry['det']]]
        cands = []
        for purity in PURITIES:
            for tag, kw in (('self', dict()),
                            ('inherit', dict(start_ecal=ref['ecal'], r662_seed=ref['r662']))):
                ecal, acc, r662, hist, lines = calibrate_energy(
                    st['sp'], entry, min_purity=purity, **kw)
                score, n = validate(st['sp'], entry, ecal, r662,
                                own=rms(acc, ecal) if len(acc) >= 3 else None)
                cands.append((score, -n, '%s/p%.2f' % (tag, purity), ecal, acc, r662, hist, lines))
        # Last resort for the short, weak spectra: take the reference spectrum's
        # calibration verbatim. The AS80x80 files were taken at one gain setting
        # (byte-identical stored calibrations), so the correction fitted on a
        # strong run is the best estimate for a spectrum that cannot calibrate
        # itself, like the 1584-second charoite.
        score, n = validate(st['sp'], entry, ref['ecal'], ref['r662'], own=1e5)
        res_ref = res_seed(ref['r662'])
        acc_ref = measure(st['sp'].counts, ref['ecal'],
                          curate(entry, res_ref, min_purity=0.60),
                          res_ref, tol_frac=0.01, width_tol=0.5)
        cands.append((score, -n, 'ref-cal', ref['ecal'], acc_ref, ref['r662'], [], []))
        cands.sort(key=lambda c: (c[0], c[1]))
        _, _, tag, ecal, acc, r662, hist, lines = cands[0]
        st.update(ecal=ecal, accepted=acc, r662=r662, hist=hist, lines=lines, mode=tag)

    # --- pass C: per-detector resolution model ---
    res_coef = {}
    for det in REFERENCE:
        pts = []
        for entry in SPECTRA:
            if entry['det'] != det:
                continue
            st = state[entry['key']]
            for a in st['accepted']:
                # a blended group is wider than the line it is named after, so
                # only nearly pure peaks may define the resolution
                if a.get('purity', 1.0) < 0.85:
                    continue
                fw_kev = a['fwhm'] * st['ecal'].dEdch(a['ch'])
                pts.append((a['e_ref'], fw_kev, min(a['sig'], 100.0)))
        if len(pts) < 3:
            for entry in SPECTRA:
                if entry['det'] != det:
                    continue
                st = state[entry['key']]
                for a in st['accepted']:
                    fw_kev = a['fwhm'] * st['ecal'].dEdch(a['ch'])
                    pts.append((a['e_ref'], fw_kev, min(a['sig'], 100.0) * a.get('purity', 1.0)))
        res_coef[det] = fit_resolution_kev(pts)

    # --- report + write ---
    summary = []
    for entry in SPECTRA:
        st = state[entry['key']]
        sp, ecal, acc = st['sp'], st['ecal'], st['accepted']
        rf = resolution_fn(res_coef[entry['det']])
        print('== %-16s det=%-8s ch=%-5d live=%-8.0f mode=%-9s R(662)=%.1f%%' % (
            entry['key'], entry['det'], sp.n, sp.live, st['mode'], 100 * st['r662']))
        for a in acc:
            fw_kev = a['fwhm'] * ecal.dEdch(a['ch'])
            print('     %-22s %8.2f -> ch %8.2f  d=%+6.2f keV  FWHM=%6.2f keV (%4.1f%%)  model=%6.2f  sig=%7.1f' % (
                a['label'][:22], a['e_ref'], a['ch'], ecal.energy(a['ch']) - a['e_ref'],
                fw_kev, 100 * fw_kev / a['e_ref'], rf(a['e_ref']), a['sig']))
        print('   ecal rms=%.2f keV over %d lines   coef=%s' % (
            rms(acc, ecal), len(acc), ', '.join('%.10g' % c for c in ecal.coef)))
        fcoef = fwhm_channel_coef(ecal, rf, sp.n)
        print('   FWHM[keV] model: sqrt(%.6g + %.6g*E + %.6g*E^2)  -> ch coef %.8g, %.8g, %.8g' % (
            res_coef[entry['det']][0], res_coef[entry['det']][1], res_coef[entry['det']][2],
            fcoef[0], fcoef[1], fcoef[2]))
        dest = write_spectrum(entry, ecal.coef, fcoef)
        print('   -> %s' % dest)
        print()
        summary.append(dict(key=entry['key'], det=entry['det'], mode=st['mode'],
                            n_lines=len(acc), rms=rms(acc, ecal),
                            ecal=[float(c) for c in ecal.coef],
                            fwhm_ch=[float(c) for c in fcoef],
                            res_kev=[float(c) for c in res_coef[entry['det']]],
                            live=sp.live, channels=sp.n))
    with open(os.path.join(HERE, 'calibration.json'), 'w') as fh:
        json.dump(summary, fh, indent=2)
    print('resolution models:')
    for det, c in res_coef.items():
        for e in (100, 300, 600, 1000, 1500, 2615):
            pass
        print('  %-8s ' % det + '  '.join('%d keV: %.1f keV (%.1f%%)' % (
            e, resolution_fn(c)(e), 100 * resolution_fn(c)(e) / e)
            for e in (100, 300, 662, 1000, 1500, 2615)))


if __name__ == '__main__':
    main()
