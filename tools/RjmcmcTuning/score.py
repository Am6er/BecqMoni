#!/usr/bin/env python3
# Scores RjmcmcHarness output against tabular gamma lines of Th-232 and Ra-226 (U-238).
# Matching window is physical: a line matches a peak if |E_line - E_peak| <= 0.5 * FWHM_keV(peak),
# where FWHM_keV = FWHM_channels * (E/ch) of that peak. No absolute energy filter.
# Set env TOL=<keV> to force a fixed absolute window instead (legacy comparisons).
# Usage:  python score.py out/*.txt
import sys, re, glob, os

# DDEP/ENSDF evaluated lines, abs. emission >= ~1% (plus historical 830.5), 2026 check.
TH232 = [
    (129.1,"Ac228"),(209.3,"Ac228"),(238.6,"Pb212"),(241.0,"Ra224"),
    (270.2,"Ac228"),(277.4,"Tl208"),(300.1,"Pb212"),
    (328.0,"Ac228"),(338.3,"Ac228"),(409.5,"Ac228"),(463.0,"Ac228"),
    (583.2,"Tl208"),(727.3,"Bi212"),(755.3,"Ac228"),(772.3,"Ac228"),
    (785.4,"Bi212"),(794.9,"Ac228"),(830.5,"Ac228"),(835.7,"Ac228"),
    (860.6,"Tl208"),(911.2,"Ac228"),(964.8,"Ac228"),(969.0,"Ac228"),
    (1588.2,"Ac228"),(1620.5,"Bi212"),(1630.6,"Ac228"),(2614.5,"Tl208"),
]
RA226 = [
    (186.2,"Ra226"),(242.0,"Pb214"),(295.2,"Pb214"),(351.9,"Pb214"),
    (609.3,"Bi214"),(665.4,"Bi214"),(768.4,"Bi214"),(786.0,"Pb214"),
    (806.2,"Bi214"),
    (934.1,"Bi214"),(1120.3,"Bi214"),(1155.2,"Bi214"),(1238.1,"Bi214"),
    (1280.9,"Bi214"),(1377.7,"Bi214"),(1401.5,"Bi214"),(1408.0,"Bi214"),
    (1509.2,"Bi214"),(1661.3,"Bi214"),(1729.6,"Bi214"),(1764.5,"Bi214"),
    (1847.4,"Bi214"),(2118.5,"Bi214"),(2204.2,"Bi214"),(2447.9,"Bi214"),
]
COMMON = [(511.0,"annih"),(1460.8,"K40")]

PEAK_RE = re.compile(
    r"ch=\s*([\-\d]+)\s+E=\s*([\-\d.]+)\s*keV\s+SNR=\s*([\d.]+)\s+FWHM=\s*([\d.]+)")

def parse_final_peaks(text):
    peaks=[]; grab=False
    for ln in text.splitlines():
        if ln.strip().startswith("Final peaks:"):
            grab=True; continue
        if ln.strip().startswith("RJMCMC extras"):
            grab=False; continue
        if grab:
            m=PEAK_RE.search(ln)
            if m:
                ch=int(m.group(1)); e=float(m.group(2))
                snr=float(m.group(3)); fwhm_ch=float(m.group(4))
                fwhm_kev = fwhm_ch * (e/ch) if ch > 0 and e > 0 else 0.0
                peaks.append((ch, e, snr, fwhm_kev))
    return peaks

def peak_tolerance(peak, fixed_tol):
    if fixed_tol is not None:
        return fixed_tol
    # physical window: the line must fall inside the peak core
    return max(1.0, 0.5 * peak[3])

def greedy_match(peaks, lines, fixed_tol):
    used=[False]*len(peaks); matched=[]
    for (le,lab) in lines:
        best=-1; bestd=None
        for i,p in enumerate(peaks):
            if used[i]: continue
            d=abs(p[1]-le)
            if d <= peak_tolerance(p, fixed_tol) and (bestd is None or d < bestd):
                bestd=d; best=i
        if best>=0:
            used[best]=True; matched.append((le,lab,peaks[best][1],round(bestd,1)))
    mset={m[0] for m in matched}
    uml=[(le,lab) for (le,lab) in lines if le not in mset]
    ump=[peaks[i][1] for i in range(len(peaks)) if not used[i]]
    return matched, uml, ump

def main():
    tol_env=os.environ.get("TOL")
    fixed_tol=float(tol_env) if tol_env else None
    files=[]
    for a in sys.argv[1:]:
        g=sorted(glob.glob(a))
        files += g if g else [a]
    for f in files:
        text=open(f,encoding='utf-8',errors='ignore').read()
        base=os.path.basename(f).lower()
        # series by filename prefix (th*/ra*); substring search broke on names like 'birthmix'
        series,sname=(TH232+COMMON,"Th-232") if base.startswith('th') else (RA226+COMMON,"Ra-226")
        peaks=parse_final_peaks(text)
        matched,uml,ump=greedy_match(peaks,series,fixed_tol)
        tolinfo = "+/-%gkeV" % fixed_tol if fixed_tol is not None else "0.5*FWHM(peak)"
        print("=== %s  [%s] finalPeaks=%d matched=%d/%d tol=%s" % (os.path.basename(f),sname,len(peaks),len(matched),len(series),tolinfo))
        print("  matched:", ", ".join("%s%.0f(p%.0f,d%s)"%(lab,le,v,d) for le,lab,v,d in matched))
        print("  MISSED :", ", ".join("%s%.0f"%(lab,le) for le,lab in uml))
        print("  extraPk:", ", ".join("%.0f"%e for e in sorted(ump)))
        print()

if __name__=="__main__":
    main()
