# -*- coding: utf-8 -*-
"""Minimal spectrum IO + SNIP continuum + peak measurement, numpy only.

Used to audit / redo the energy and FWHM calibrations of the test spectra before
they are fed to the harness. Mirrors the conventions of the production code:
energy calibration is a polynomial in channel, FWHM calibration is
SqrtFwhmCalibration -- FWHM[ch] = sqrt(c0 + c1*ch + c2*ch^2).
"""
import numpy as np
import xml.etree.ElementTree as ET


class Spectrum(object):
    def __init__(self, path):
        self.path = path
        self.tree = ET.parse(path)
        root = self.tree.getroot()
        rd = root.find('ResultDataList/ResultData')
        self.rd = rd
        es = rd.find('EnergySpectrum')
        self.es = es
        self.counts = np.array([int(d.text) for d in es.findall('Spectrum/DataPoint')], dtype=float)
        self.n = int(es.find('NumberOfChannels').text)
        self.ecal = np.array([float(x.text) for x in es.findall('EnergyCalibration/Coefficients/Coefficient')])
        # Ссылка на прибор бывает неполной: у части файлов есть только имя, а
        # GUID отсутствует вовсе (E5, 16.08.2026: `K-40 деревня маринелли 0.5`
        # на RC-103 — файл писал другой инструмент). Падать здесь нельзя:
        # рабочая копия корпуса всё равно переписывает ссылку на конфигурацию
        # своего детектора, а до этого места дело просто не доходило.
        def _text(node):
            return None if node is None or node.text is None else node.text

        self.device = _text(rd.find('DeviceConfigReference/Name'))
        self.guid = _text(rd.find('DeviceConfigReference/Guid'))
        lt = es.find('LiveTime')
        self.live = float(lt.text) if lt is not None and lt.text else float(es.find('MeasurementTime').text)
        fw = rd.find('SqrtFwhmCalibration')
        self.fwhm_coef = None
        if fw is not None:
            cs = [float(x.text) for x in fw.findall('Coefficients/Coefficient')]
            if len(cs) == 3 and any(cs):
                self.fwhm_coef = np.array(cs)

    # --- calibration ---
    def energy(self, ch):
        ch = np.asarray(ch, dtype=float)
        return sum(c * ch ** i for i, c in enumerate(self.ecal))

    def channel(self, energy, lo=0.0, hi=None):
        """Invert the energy polynomial by bisection on a monotone grid."""
        hi = self.n - 1 if hi is None else hi
        grid = np.arange(0, self.n, dtype=float)
        e = self.energy(grid)
        return float(np.interp(energy, e, grid))

    def dEdch(self, ch):
        return sum(i * c * float(ch) ** (i - 1) for i, c in enumerate(self.ecal) if i >= 1)

    def fwhm_ch(self, ch):
        if self.fwhm_coef is None:
            return None
        v = self.fwhm_coef[0] + self.fwhm_coef[1] * ch + self.fwhm_coef[2] * ch * ch
        return np.sqrt(np.maximum(v, 0.0))


def snip(counts, iterations=24):
    """SNIP continuum estimate on the log-log-sqrt transformed spectrum."""
    y = np.log(np.log(np.sqrt(np.maximum(counts, 0.0) + 1.0) + 1.0) + 1.0)
    n = len(y)
    for p in range(iterations, 0, -1):
        left = np.concatenate([y[:p], y[:-p]])
        right = np.concatenate([y[p:], y[-p:]])
        y = np.minimum(y, 0.5 * (left + right))
    z = (np.exp(np.exp(y) - 1.0) - 1.0) ** 2 - 1.0
    return np.maximum(z, 0.0)


def measure_peak(counts, continuum, ch0, half_window):
    """Centroid + FWHM (channels) of a peak near ch0 on a continuum-subtracted
    spectrum. Returns (centroid, fwhm, net_area, peak_height) or None."""
    n = len(counts)
    lo = max(0, int(ch0 - half_window))
    hi = min(n - 1, int(ch0 + half_window))
    if hi - lo < 4:
        return None
    net = counts[lo:hi + 1] - continuum[lo:hi + 1]
    if net.size == 0:
        return None
    # light 3-point smoothing so noise does not pick the apex
    k = np.array([0.25, 0.5, 0.25])
    sm = np.convolve(net, k, mode='same')
    imax = int(np.argmax(sm))
    height = sm[imax]
    if height <= 0:
        return None
    apex = lo + imax
    # half-maximum crossings by linear interpolation
    half = height / 2.0
    i = imax
    while i > 0 and sm[i] > half:
        i -= 1
    if sm[i] > half:
        return None
    left = lo + i + (half - sm[i]) / max(sm[i + 1] - sm[i], 1e-9)
    j = imax
    while j < len(sm) - 1 and sm[j] > half:
        j += 1
    if sm[j] > half:
        return None
    right = lo + j - (half - sm[j]) / max(sm[j - 1] - sm[j], 1e-9)
    fwhm = right - left
    if fwhm <= 0:
        return None
    # centroid over +-1.2 FWHM around the apex
    a = max(lo, int(apex - 1.2 * fwhm))
    b = min(hi, int(apex + 1.2 * fwhm))
    seg = np.maximum(counts[a:b + 1] - continuum[a:b + 1], 0.0)
    if seg.sum() <= 0:
        return None
    idx = np.arange(a, b + 1, dtype=float)
    centroid = float((idx * seg).sum() / seg.sum())
    return centroid, float(fwhm), float(seg.sum()), float(height)


def fit_sqrt_fwhm(channels, fwhms, weights=None):
    """Least squares fit of FWHM^2 = c0 + c1*ch + c2*ch^2."""
    ch = np.asarray(channels, dtype=float)
    y = np.asarray(fwhms, dtype=float) ** 2
    w = np.ones_like(ch) if weights is None else np.asarray(weights, dtype=float)
    A = np.vstack([np.ones_like(ch), ch, ch ** 2]).T * w[:, None]
    coef, *_ = np.linalg.lstsq(A, y * w, rcond=None)
    return coef


def monotone(coef, nmax):
    ch = np.arange(0, nmax, dtype=float)
    v = coef[0] + coef[1] * ch + coef[2] * ch ** 2
    f = np.sqrt(np.maximum(v, 0.0))
    return bool(np.all(np.diff(f) >= -1e-12))
