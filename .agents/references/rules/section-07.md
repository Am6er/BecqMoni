<!-- Контекст Codex; действующие правила — в корневом AGENTS.md. -->

## Testing / verification

⛔ **Приёмка любого коммита — `python tools/check_all.py`, код 0** (решение Amber
06.09.2026, `T220`). Читатель зовёт одиннадцать сторожей `tools/check_*.py` за
~26 секунд и печатает каждое обращение; список сторожей и заведомо красные (с
причиной, в `KNOWN_RED`) — в шапке самого `check_all.py`. Красный сторож, который
держится открытой строкой реестра, обходить новым исключением нельзя.

There is **no unit-test project.** The peak-detection code paths are
verified offline against reference spectra using tools in `tools/`:

- **`tools/CORPUS/`** — the spectrum corpus and the toolkit that builds and checks it
  (`corpus/`, `data/` efficiency curves and calibrations, `scripts/` pipeline, `probes/`).
  `README.md` is the lab notebook: how the corpus was assembled, what is in it, where the
  efficiency curves came from. See below.
- **`tools/pie/`** — full-spectrum decomposition harness that runs over the corpus.
- **`tools/centroid_tests/`** — centroid-method (center-of-mass vs argmax) comparison report.

### ⛔ Every corpus number must name its part (blocker lifted 2026-08-09)

Corpus runs are **allowed** — the 2026-08-06 block was lifted on 2026-08-09 when the corpus was
split. What it was about is still true and is now handled by the split, not by a ban: the
detector response matrix (`tools/effmaker/handover-response-matrix.md`) is computed **from
geometry** and is **specific to each geometry** — measured: one crystal in three source placements
gave peak fractions 0.848 / 0.822 / 0.815 and up to a quarter's spread in the continuum. Mixing
spectra that have a matrix with spectra that do not averages two different models, invisibly.

So the corpus is kept in parts (`corpus/parts.csv`) and **every quoted number must say which part
it came from**: **known** — geometry and matrix present (82 spectra), **unknown** — neither
(39), **excluded** — germanium, out of scope (8). Numbers of two parts are never added together.
A spectrum without a geometry is kept in the corpus **on purpose**, as the anchor of its group's
resolution model — it is not an unfinished "known" one, and no "build it a geometry" task is
raised for it. Details and rationale in `tools/CORPUS/README.md`.

### ⛔ First postulate: every spectrum has its OWN peak base, tied to the nuclide measured

Amber's rule, 17.08.2026. Not a preference and not one task among others — this is how the
corpus is analysed. The application's general library is the **source** the base is assembled
from, never the list handed to a spectrum: the corpus already knows what was measured
(`manifest.csv`, the spectrum's own name, the nuclide column of `summary.csv`), and offering
the whole library hands unexplained structure to the first candidate that fits. Measured cost
(`N18`): `ASN16_Lu176` acquired a **Pu-238** at 1.7 % and z = 31.77 on a single 152 keV line
of 0.0009 % intensity, sitting in the backscatter/Compton-edge band of lutetium's own 306.78.

**Assemble the base from ALL radiations of the decay, not from gamma lines** — Lu-176's only
gamma below 200 keV is 88.34, while the real spectrum down there rests on Hf K X-rays at
54–65 keV (`nucdb.decay_radiations`, type `X`); a gamma-only base left 405σ at 60.8 keV.
Background and the ubiquitous K-40 / Th-232 / Ra-226 belong in the base too. ⚠ Two traps
measured the same day: recall and phantom counts are computed **relative to the list offered**,
so narrowing it improves them by itself and such a run cannot be compared with the old base
directly; and a narrow base starves the gain fit — seven lines drove it into the edge of its
±0.8 % grid. Task: `S56` (blocker); details in `tools/CORPUS/README.md`.

### The corpus is the unit of measurement

**Every measurement runs over the whole corpus — `tools/CORPUS/corpus/spectra`, all 129
spectra of all 19 detector groups. Numbers obtained on a subset do not go into the journal and
do not support a conclusion.**

The reason is in the journal: the nine spectra the earlier results rested on were three similar
scintillators inside one third of a resolution decade, and conclusions drawn there did not
survive contact with the rest. The corpus spans 0.20 % (HPGe) to 13.3 % (Obsidian) FWHM at
662 keV, 1024 to 16384 channels and 0.019 M to 251 M counts precisely so that a criterion cannot
be tuned to the middle of the range without it showing. A criterion that helps on ASN16 and
falls apart on HPGe is not an improvement, and only the full corpus makes that visible.

`corpus/manifest.csv` is the truth table (what each sample contains), `corpus/detectors.csv`
the per-group resolution model and range, `corpus/devices/` the device configs the spectra
reference. Rebuild the corpus with `scripts/build_corpus.py`, check it with
`scripts/check_corpus.py`, assemble the per-detector run workdirs (`scripts/wd_*`, gitignored)
with `scripts/mkconfig.py`.
