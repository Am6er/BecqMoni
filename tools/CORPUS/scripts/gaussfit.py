# -*- coding: utf-8 -*-
"""Gauss-Newton fit of a single Gaussian on a linear background, numpy only.

Poisson weights (w = 1/max(y,1)) so that a peak sitting on a big Compton
continuum is not dominated by the continuum's absolute scale.
"""
import numpy as np

FWHM_SIGMA = 2.0 * np.sqrt(2.0 * np.log(2.0))


def fit_gauss(x, y, mu0, sigma0, iterations=40):
    """Returns dict(mu, sigma, amp, area, b0, b1, sig, chi2ndf) or None."""
    x = np.asarray(x, dtype=float)
    y = np.asarray(y, dtype=float)
    if x.size < 6:
        return None
    xc = x - mu0
    # initial linear background from the two outer thirds
    edge = max(2, x.size // 6)
    xe = np.concatenate([xc[:edge], xc[-edge:]])
    ye = np.concatenate([y[:edge], y[-edge:]])
    try:
        b1, b0 = np.polyfit(xe, ye, 1)
    except Exception:
        b0, b1 = float(np.median(ye)), 0.0
    amp = max(float(y.max() - (b0 + b1 * 0.0)), 1.0)
    p = np.array([amp, 0.0, float(sigma0), float(b0), float(b1)], dtype=float)
    w = 1.0 / np.sqrt(np.maximum(y, 1.0))

    for _ in range(iterations):
        A, dmu, s, c0, c1 = p
        if s <= 1e-3:
            return None
        t = (xc - dmu) / s
        g = np.exp(-0.5 * t * t)
        model = A * g + c0 + c1 * xc
        r = (y - model) * w
        J = np.empty((x.size, 5))
        J[:, 0] = g * w
        J[:, 1] = A * g * t / s * w
        J[:, 2] = A * g * t * t / s * w
        J[:, 3] = w
        J[:, 4] = xc * w
        try:
            step, *_ = np.linalg.lstsq(J, r, rcond=None)
        except np.linalg.LinAlgError:
            return None
        # damped update keeps sigma positive and the centre inside the window
        lam = 1.0
        for _ in range(8):
            q = p + lam * step
            if q[2] > 1e-3 and abs(q[1]) < 0.6 * (xc[-1] - xc[0]):
                break
            lam *= 0.5
        else:
            return None
        p = q
        if np.max(np.abs(lam * step[:3]) / np.maximum(np.abs(p[:3]), 1e-6)) < 1e-6:
            break

    A, dmu, s, c0, c1 = p
    if A <= 0 or s <= 0:
        return None
    mu = mu0 + dmu
    model = A * np.exp(-0.5 * ((xc - dmu) / s) ** 2) + c0 + c1 * xc
    resid = (y - model) * w
    chi2ndf = float((resid ** 2).sum() / max(x.size - 5, 1))
    area = A * s * np.sqrt(2.0 * np.pi)
    # amplitude significance against the local background level
    base = max(c0 + c1 * dmu, 1.0)
    # counting significance of the net area vs background under the peak
    n_bg = base * s * np.sqrt(2.0 * np.pi)
    sig = area / np.sqrt(max(area + 2.0 * n_bg, 1.0))
    return dict(mu=float(mu), sigma=float(s), fwhm=float(s * FWHM_SIGMA), amp=float(A),
                area=float(area), b0=float(c0), b1=float(c1), sig=float(sig),
                chi2ndf=chi2ndf)


def fit_peak(counts, mu0, sigma0, window=2.6, nmin=8):
    n = len(counts)
    half = max(nmin // 2, int(round(window * sigma0)))
    lo = int(max(0, round(mu0 - half)))
    hi = int(min(n - 1, round(mu0 + half)))
    if hi - lo + 1 < nmin:
        return None
    x = np.arange(lo, hi + 1, dtype=float)
    return fit_gauss(x, counts[lo:hi + 1], mu0, sigma0)
