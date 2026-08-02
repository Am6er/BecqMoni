using BecquerelMonitor;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace Pie
{
    /// <summary>
    /// Headless full-spectrum decomposition (FSA) harness.
    ///
    /// Unlike the peak-search pipeline (PeakDetector), this tool does not look
    /// for peaks at all: the measured spectrum is modeled as a non-negative
    /// linear combination of nuclide response templates ("elementary spectra"
    /// generated from line tables + the detector's energy/FWHM calibrations),
    /// a background term and a continuum term, with the spectrometer's energy
    /// drift (gain/zero) treated as free model parameters. The linear part is
    /// solved by weighted NNLS; the drift by a coarse grid search; robustness
    /// by Huber-type reweighting.
    ///
    /// Build (after the main project, same pattern as the corpus workdirs):
    ///   csc /target:exe /platform:anycpu /langversion:7.3 /out:&lt;wd&gt;\pie.exe
    ///       /r:&lt;wd&gt;\BecquerelMonitor.exe /r:System.dll /r:System.Core.dll
    ///       /r:System.Xml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
    ///       tools\pie\Program.cs
    /// Run from a corpus workdir tools/CORPUS/scripts/wd_* (config/ + spectra/).
    /// </summary>
    internal static class Program
    {
        static int Main(string[] args)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                Options options = Options.Parse(args);
                if (!string.IsNullOrEmpty(options.WorkingDirectory))
                {
                    Environment.CurrentDirectory = options.WorkingDirectory;
                }

                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();

                List<Component> library = BuildLibrary(nuclideManager, options.Components,
                                                       options.SplitChains);

                // образы из измеренных эталонов замещают одноимённые расчётные
                foreach (KeyValuePair<string, string> std in options.Standards)
                {
                    Component comp = LoadStandard(std.Key, std.Value, options.ResultIndex);
                    int at = library.FindIndex(c => string.Equals(c.Name, std.Key, StringComparison.OrdinalIgnoreCase));
                    if (at >= 0) library[at] = comp; else library.Add(comp);
                }

                if (library.Count == 0)
                {
                    Console.Error.WriteLine("No usable components.");
                    return 1;
                }
                Console.Error.WriteLine("Component library: " + string.Join(", ",
                    library.Select(c => c.Name + "(" + c.Lines.Count + " lines, " + c.Kind + ")")));

                Dictionary<string, EffCurve> effCurves = EffCurve.LoadTable(options.EffCurvePath);

                using (StreamWriter runsWriter = new StreamWriter(options.OutPrefix + "_runs.csv", false, Encoding.UTF8))
                using (StreamWriter compWriter = new StreamWriter(options.OutPrefix + "_components.csv", false, Encoding.UTF8))
                {
                    runsWriter.WriteLine("spectrum,mode,eff,live_s,counts_range,ncomp,nactive,chi2ndf,gain,offset_ch,bg_cps,bg_z,ms");
                    compWriter.WriteLine("spectrum,mode,component,kind,amp_cps,damp_cps,z,share_pct,peak_counts");

                    foreach (string file in ResolveSpectrumFiles(options.InputPath))
                    {
                        try
                        {
                            RunOne(file, options, library, effCurves, runsWriter, compWriter);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine("{0} failed: {1}", Path.GetFileName(file), ex.Message);
                            runsWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "{0},{1},,,,,,ERROR,,,,,", Csv(Path.GetFileNameWithoutExtension(file)), options.Mode));
                            Failures++;
                        }
                        runsWriter.Flush();
                        compWriter.Flush();
                    }
                }

                return Failures > 0 ? 2 : 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        static int Failures;
        static int PriorCol = -1;
        static double PriorWeight;
        static string CurrentMode = "snip";

        // ------------------------------------------------------------------
        // One spectrum
        // ------------------------------------------------------------------

        static void RunOne(string file, Options options, List<Component> library,
                           Dictionary<string, EffCurve> effCurves,
                           StreamWriter runsWriter, StreamWriter compWriter)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            CurrentMode = options.Mode;
            Stopwatch watch = Stopwatch.StartNew();

            ResultData rd = LoadResultData(file, options.ResultIndex);
            EnergySpectrum es = rd.EnergySpectrum;
            int nch = es.NumberOfChannels;
            double liveT = es.LiveTime > 0 ? es.LiveTime : es.MeasurementTime;
            if (liveT <= 0)
            {
                Console.Error.WriteLine("{0}: no live/measurement time, cps columns are raw counts", name);
                liveT = 1.0;
            }

            PolynomialEnergyCalibration pcal = es.EnergyCalibration as PolynomialEnergyCalibration;
            if (pcal != null) pcal.CheckCalibration(nch);
            EnergyCalibration cal = es.EnergyCalibration;
            FwhmCalibration fwhmCal = rd.FwhmCalibration;

            int chLo = ClampChannel(EnergyToChannelSafe(cal, options.EMin, nch), nch);
            int chHi = ClampChannel(EnergyToChannelSafe(cal, options.EMax, nch), nch);
            // Последний канал АЦП — канал переполнения: в него падают все
            // события выше шкалы (RC103_Background: 11 k отсчётов при соседях
            // ~5). Образа у такой структуры нет, фит её объяснить не может —
            // верхний канал исключается, когда диапазон дотянулся до края.
            if (chHi >= nch - 1) chHi = nch - 2;
            if (chHi <= chLo + 10) throw new InvalidOperationException("degenerate fit range");

            // --bg-file: внешнее фоновое измерение вместо встроенного. Файл
            // грузится без конфига устройства (нужны только спектр и его
            // калибровка) и передискретизируется в шкалу образца через
            // энергию. Дальше обычный путь фона: по умолчанию вычитание с
            // beta = 1 (фитовать фон нельзя — его континуум вырожден с
            // шапками, и NNLS зануляет весь образ, см. журнал).
            if (!string.IsNullOrEmpty(options.BgFile))
            {
                EnergySpectrum ext = LoadSpectrumOnly(options.BgFile, options.ResultIndex);
                double[] src = ext.Spectrum.Select(v => (double)v).ToArray();
                double[] mapped = ResampleToSample(src, ext.EnergyCalibration, cal, nch);
                EnergySpectrum bgSpectrum = es.Clone();
                bgSpectrum.Spectrum = mapped.Select(v => (int)Math.Round(Math.Max(0.0, v))).ToArray();
                bgSpectrum.MeasurementTime = ext.MeasurementTime;
                bgSpectrum.LiveTime = ext.LiveTime;
                rd.BackgroundEnergySpectrum = bgSpectrum;
            }

            EffCurve eff = null;
            if (effCurves.Count > 0 && !effCurves.TryGetValue(name, out eff))
            {
                Console.Error.WriteLine("{0}: no efficiency curve in table, fitting without one", name);
            }

            // образы из эталонов — в шкалу этого спектра
            foreach (Component comp in library)
            {
                if (comp.StandardCps == null) continue;
                comp.ResampledCps = ResampleToSample(comp.StandardCps, comp.StandardCal, cal, nch);
                comp.ResampledPeaksCps = ResampleToSample(comp.StandardPeaksCps, comp.StandardCal, cal, nch);
            }

            // --- data, continuum, background -------------------------------
            int[] raw = es.Spectrum;
            double[] y = new double[nch];
            double[] var = new double[nch];
            int[] continuum = null;

            EnergySpectrum bg = options.UseBackground ? rd.BackgroundEnergySpectrum : null;
            if (bg != null && (bg.Spectrum == null || bg.NumberOfChannels != nch))
            {
                Console.Error.WriteLine("{0}: background skipped (channel mismatch)", name);
                bg = null;
            }
            double bgScale = 0.0;
            double bgFixedCps = 0.0;
            double[] bgTemplate = null;                 // fitted as one more column
            if (bg != null)
            {
                double bgLive = bg.LiveTime > 0 ? bg.LiveTime : bg.MeasurementTime;
                if (bgLive > 0) bgScale = liveT / bgLive; else bg = null;
            }

            if (options.Mode == "snip")
            {
                continuum = Snip(fwhmCal, es);
                for (int i = 0; i < nch; i++)
                {
                    y[i] = raw[i] - continuum[i];
                    double c = options.Xi * continuum[i];
                    var[i] = Math.Max(raw[i], 1.0) + c * c;
                }
                if (bg != null)
                {
                    // only the peaked part of the background: its continuum is
                    // already part of the foreground continuum estimate
                    int[] bgCont = Snip(fwhmCal, bg);
                    bgTemplate = new double[nch];
                    for (int i = 0; i < nch; i++)
                    {
                        bgTemplate[i] = Math.Max(0.0, bg.Spectrum[i] - bgCont[i]) * bgScale;
                    }
                }
            }
            else // spline
            {
                for (int i = 0; i < nch; i++)
                {
                    y[i] = raw[i];
                    var[i] = Math.Max(raw[i], 1.0);
                }
                if (bg != null)
                {
                    bgTemplate = new double[nch];
                    for (int i = 0; i < nch; i++) bgTemplate[i] = bg.Spectrum[i] * bgScale;
                }
            }

            // Фон измерен и нормирован по живому времени — по умолчанию он не
            // подбирается, а вычитается (beta = 1). Свободный beta вырожден с
            // компонентами образца: колонка «K-40 пробы» лежит внутри колонки
            // «фон с комнатным K-40», и NNLS, взяв фон первым, уже не получает
            // градиента, чтобы переложить пик на пробу (наблюдалось на
            // RC103_K40). --bg=fit оставлен как режим с байесовским приколом.
            if (bgTemplate != null && options.BgMode == "fixed")
            {
                for (int i = 0; i < nch; i++)
                {
                    y[i] -= bgTemplate[i];
                    double b = bg.Spectrum[i] * bgScale;
                    var[i] += Math.Max(Math.Abs(b) * bgScale, bgScale * bgScale);
                }
                bgFixedCps = SumRange(bgTemplate, chLo, chHi) / liveT;
                bgTemplate = null;
            }

            // --- fixed (non-drifting) columns ------------------------------
            List<double[]> fixedCols = new List<double[]>();
            List<string> fixedNames = new List<string>();
            int bgCol = -1;
            if (bgTemplate != null)
            {
                bgCol = fixedCols.Count;
                fixedCols.Add(bgTemplate);
                fixedNames.Add("bg");
                // Фон измерен, а не подбирается: без прикола коэффициент фона
                // вырожден с компонентами образца (комнатный K-40 против K-40
                // в пробе), и NNLS раздувает фон вместо образца. Мягкий
                // байесовский прикол beta ~ 1 +/- BgSigma.
                PriorCol = bgCol;
                PriorWeight = 1.0 / (options.BgSigma * options.BgSigma);
            }
            else
            {
                PriorCol = -1;
                PriorWeight = 0.0;
            }
            if (options.Mode == "spline")
            {
                foreach (double[] hat in BuildHatBasis(fwhmCal, chLo, chHi, nch))
                {
                    fixedCols.Add(hat);
                    fixedNames.Add("hat");
                }
            }

            // --- drift grid ------------------------------------------------
            double bestGain = 1.0, bestOffset = 0.0, bestChi2 = double.MaxValue;
            double[] baseWeights = new double[nch];
            for (int i = 0; i < nch; i++) baseWeights[i] = 1.0 / var[i];

            int gSteps = options.GainSteps, oSteps = options.OffsetSteps;
            // --offset-range задан в кэВ; в каналы пересчитывается по среднему
            // наклону шкалы, иначе одна и та же величина означает разное для
            // 1024- и 8192-канальных приборов. Наклон — по фактическим
            // энергиям границ фита: EMin/EMax могут выходить за шкалу спектра,
            // и после клампа каналов деление на (EMax − EMin) искажало бы его.
            double eLoFit = cal.ChannelToEnergy(chLo), eHiFit = cal.ChannelToEnergy(chHi);
            double chPerKev = eHiFit > eLoFit
                ? (chHi - chLo) / (eHiFit - eLoFit)
                : (chHi - chLo) / Math.Max(1.0, options.EMax - options.EMin);
            double offRangeCh = options.OffsetRangeKev * chPerKev;
            int bestGi = 0, bestOi = 0;
            for (int gi = 0; gi < gSteps; gi++)
            {
                double gain = gSteps == 1 ? 1.0
                    : 1.0 - options.GainRange + 2.0 * options.GainRange * gi / (gSteps - 1);
                for (int oi = 0; oi < oSteps; oi++)
                {
                    double offset = oSteps == 1 ? 0.0
                        : -offRangeCh + 2.0 * offRangeCh * oi / (oSteps - 1);
                    FitResult fr = FitOnce(library, fixedCols, cal, fwhmCal, eff, gain, offset,
                                           chLo, chHi, nch, y, baseWeights, null);
                    if (fr != null && fr.Chi2 < bestChi2)
                    {
                        bestChi2 = fr.Chi2;
                        bestGain = gain;
                        bestOffset = offset;
                        bestGi = gi;
                        bestOi = oi;
                    }
                }
            }
            // Оптимум на краю сетки — признак, что реальный дрейф больше
            // диапазона: шаблоны недоведены, невязка систематическая, и её
            // подбирают фантомы (наблюдалось на группе AS80x80).
            bool gainOnEdge = gSteps > 1 && (bestGi == 0 || bestGi == gSteps - 1);
            bool offsetOnEdge = oSteps > 1 && (bestOi == 0 || bestOi == oSteps - 1);
            if (gainOnEdge || offsetOnEdge)
            {
                Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}: warning: drift optimum on grid edge (gain={1:F4}, offset={2:F2} ch) — consider wider {3}",
                    name, bestGain, bestOffset,
                    gainOnEdge && offsetOnEdge ? "--gain-range/--offset-range"
                        : gainOnEdge ? "--gain-range" : "--offset-range"));
            }

            // --- final fit at best drift, with Huber reweighting -----------
            FitResult best = FitHuber(library, fixedCols, cal, fwhmCal, eff, bestGain, bestOffset,
                                      chLo, chHi, nch, y, var, baseWeights, options, null);

            // --- «предварительный анализ состава»: второй проход без
            // компонентов, не прошедших порог значимости в первом ------------
            if (options.RefitZ > 0)
            {
                List<Component> keep = new List<Component>();
                for (int k = 0; k < best.Columns.Count; k++)
                {
                    Component cc = best.Columns[k].Component;
                    if (cc != null && best.Z[k] >= options.RefitZ) keep.Add(cc);
                }
                int total = best.Columns.Count(c => c.Component != null);
                if (keep.Count > 0 && keep.Count < total)
                {
                    best = FitHuber(library, fixedCols, cal, fwhmCal, eff, bestGain, bestOffset,
                                    chLo, chHi, nch, y, var, baseWeights, options, keep);
                }
            }

            watch.Stop();

            // --- report ----------------------------------------------------
            double countsRange = 0;
            for (int i = chLo; i <= chHi; i++) countsRange += raw[i];

            // shares over sample components only (bg, continuum and nuisance
            // X-ray series excluded)
            double totalPeakCounts = 0;
            for (int k = 0; k < best.Columns.Count; k++)
            {
                Component cc = best.Columns[k].Component;
                if (cc != null && cc.Kind != "nuisance") totalPeakCounts += best.PeakCounts[k];
            }

            double bgCps = bgFixedCps, bgZ = 0;
            if (bgCol >= 0)
            {
                int k = best.FixedColumnIndex(bgCol);
                if (k >= 0) { bgCps = best.Amp[k] / liveT * SumRange(bgTemplate, chLo, chHi); bgZ = best.Z[k]; }
            }

            Console.WriteLine();
            Console.WriteLine("=== {0}  (mode={1}, eff={2}, live {3:F0} s, {4:F0} counts in range) ===",
                name, options.Mode, eff != null ? "yes" : "no", liveT, countsRange);
            Console.WriteLine("    drift: gain={0:F4} offset={1:+0.00;-0.00} ch   chi2/ndf={2:F2}   bg={3:F3} cps (z={4:F1})   {5} ms",
                bestGain, bestOffset, best.Chi2Ndf, bgCps, bgZ, watch.ElapsedMilliseconds);

            List<string> rows = new List<string>();
            for (int k = 0; k < best.Columns.Count; k++)
            {
                Component comp = best.Columns[k].Component;
                if (comp == null) continue;
                // Для расчётных образов amp/liveT — скорость счёта (cps).
                // Для образов из эталонов образ уже в cps, поэтому amp/liveT —
                // безразмерное отношение активности пробы к активности эталона.
                double ampCps = best.Amp[k] / liveT;
                double dampCps = best.Sigma[k] / liveT;
                double share = comp.Kind != "nuisance" && totalPeakCounts > 0
                    ? 100.0 * best.PeakCounts[k] / totalPeakCounts : 0.0;
                compWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4:G6},{5:G6},{6:F2},{7:F2},{8:G6}",
                    Csv(name), options.Mode, Csv(comp.Name), comp.Kind,
                    ampCps, dampCps, best.Z[k], share, best.PeakCounts[k]));
                if (best.Amp[k] > 0 || best.Z[k] != 0)
                {
                    int bar = (int)Math.Round(share / 2.0);
                    rows.Add(string.Format(CultureInfo.InvariantCulture,
                        "    {0,-10} {1,12:G5} {2,10:G3} {3,8:F1} {4,7:F1}%  {5}",
                        comp.Name, ampCps, dampCps, best.Z[k], share, new string('#', Math.Max(0, bar))));
                }
            }
            Console.WriteLine("    component       cps        +/-cps        z    share");
            foreach (string row in rows) Console.WriteLine(row);

            runsWriter.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3:F1},{4:F0},{5},{6},{7:F3},{8:F5},{9:F3},{10:G6},{11:F2},{12}",
                Csv(name), options.Mode, eff != null ? 1 : 0, liveT, countsRange,
                best.Columns.Count(c => c.Component != null), best.ActiveCount,
                best.Chi2Ndf, bestGain, bestOffset, bgCps, bgZ, watch.ElapsedMilliseconds));

            if (!string.IsNullOrEmpty(options.DumpDir))
            {
                DumpModel(options.DumpDir, name, cal, raw, y, continuum, best, chLo, chHi, fixedNames);
            }
        }

        // ------------------------------------------------------------------
        // Model / solver
        // ------------------------------------------------------------------

        sealed class FitColumn
        {
            public Component Component;      // null for fixed columns (bg, hats)
            public int FixedIndex = -1;
            public double[] Values;
        }

        sealed class FitResult
        {
            public List<FitColumn> Columns;
            public double[] Amp;
            public double[] Sigma;
            public double[] Z;
            public double[] PeakCounts;      // amp * sum(template) over fit range
            public double Chi2;
            public double Chi2Ndf;
            public int ActiveCount;
            public double[] Residual;

            public int FixedColumnIndex(int fixedIndex)
            {
                for (int k = 0; k < Columns.Count; k++)
                {
                    if (Columns[k].Component == null && Columns[k].FixedIndex == fixedIndex) return k;
                }
                return -1;
            }
        }

        static FitResult FitHuber(List<Component> library, List<double[]> fixedCols,
                                  EnergyCalibration cal, FwhmCalibration fwhmCal, EffCurve eff,
                                  double gain, double offset, int chLo, int chHi, int nch,
                                  double[] y, double[] var, double[] baseWeights,
                                  Options options, List<Component> subset)
        {
            double[] weights = (double[])baseWeights.Clone();
            FitResult best = null;
            int huberPasses = options.HuberM > 0 ? 3 : 1;
            for (int pass = 0; pass < huberPasses; pass++)
            {
                best = FitOnce(library, fixedCols, cal, fwhmCal, eff, gain, offset,
                               chLo, chHi, nch, y, weights, subset);
                if (best == null) throw new InvalidOperationException("fit failed");
                if (pass + 1 == huberPasses) break;
                // Huber: |r|/sigma > m  ->  weight *= m*sigma/|r|
                for (int i = chLo; i <= chHi; i++)
                {
                    double sigma = Math.Sqrt(var[i]);
                    double r = Math.Abs(best.Residual[i]);
                    double m = options.HuberM * sigma;
                    weights[i] = r > m ? (1.0 / var[i]) * (m / r) : 1.0 / var[i];
                }
            }
            return best;
        }

        static FitResult FitOnce(List<Component> library, List<double[]> fixedCols,
                                 EnergyCalibration cal, FwhmCalibration fwhmCal, EffCurve eff,
                                 double gain, double offset, int chLo, int chHi, int nch,
                                 double[] y, double[] weights, List<Component> subset)
        {
            List<FitColumn> cols = new List<FitColumn>();
            foreach (Component comp in library)
            {
                if (subset != null && !subset.Contains(comp)) continue;
                double[] t = BuildTemplate(comp, cal, fwhmCal, eff, gain, offset, chLo, chHi, nch);
                if (t == null) continue;
                cols.Add(new FitColumn { Component = comp, Values = t });
            }
            for (int f = 0; f < fixedCols.Count; f++)
            {
                cols.Add(new FitColumn { FixedIndex = f, Values = fixedCols[f] });
            }
            if (cols.Count == 0) return null;

            int m = cols.Count;
            int n = chHi - chLo + 1;

            // normal equations G = A'WA, c = A'Wy
            double[,] G = new double[m, m];
            double[] c = new double[m];
            for (int a = 0; a < m; a++)
            {
                double[] ta = cols[a].Values;
                double dot = 0;
                for (int i = chLo; i <= chHi; i++) dot += ta[i] * weights[i] * y[i];
                c[a] = dot;
                for (int b = a; b < m; b++)
                {
                    double[] tb = cols[b].Values;
                    double g = 0;
                    for (int i = chLo; i <= chHi; i++) g += ta[i] * weights[i] * tb[i];
                    G[a, b] = g;
                    G[b, a] = g;
                }
            }

            // априорный прикол на коэффициент фона: (beta - 1)^2 / sigma^2
            if (PriorCol >= 0 && PriorWeight > 0)
            {
                for (int k = 0; k < m; k++)
                {
                    if (cols[k].Component == null && cols[k].FixedIndex == PriorCol)
                    {
                        G[k, k] += PriorWeight;
                        c[k] += PriorWeight;   // prior mean = 1
                        break;
                    }
                }
            }

            bool[] active;
            double[] x = NnlsSolve(G, c, m, out active);

            // residual + chi2
            double[] model = new double[nch];
            for (int k = 0; k < m; k++)
            {
                if (x[k] <= 0) continue;
                double[] t = cols[k].Values;
                for (int i = chLo; i <= chHi; i++) model[i] += x[k] * t[i];
            }
            double chi2 = 0;
            double[] residual = new double[nch];
            for (int i = chLo; i <= chHi; i++)
            {
                double r = y[i] - model[i];
                residual[i] = r;
                chi2 += r * r * weights[i];
            }
            int nActive = 0;
            for (int k = 0; k < m; k++) if (active[k]) nActive++;
            double chi2ndf = chi2 / Math.Max(1, n - nActive);

            // uncertainties: inverse of active-set normal matrix, inflated by
            // sqrt(chi2/ndf) when the model does not reach the statistics
            double[] sigma = new double[m];
            double[] z = new double[m];
            double inflate = Math.Sqrt(Math.Max(1.0, chi2ndf));
            List<int> act = new List<int>();
            for (int k = 0; k < m; k++) if (active[k]) act.Add(k);
            if (act.Count > 0)
            {
                double[,] Ga = new double[act.Count, act.Count];
                for (int a = 0; a < act.Count; a++)
                    for (int b = 0; b < act.Count; b++)
                        Ga[a, b] = G[act[a], act[b]];
                double[,] inv = InvertSymmetric(Ga, act.Count);
                if (inv != null)
                {
                    for (int a = 0; a < act.Count; a++)
                    {
                        double d = inv[a, a];
                        sigma[act[a]] = d > 0 ? Math.Sqrt(d) * inflate : 0.0;
                    }
                }
            }
            for (int k = 0; k < m; k++)
            {
                if (!active[k] && G[k, k] > 0) sigma[k] = inflate / Math.Sqrt(G[k, k]);
                z[k] = sigma[k] > 0 ? x[k] / sigma[k] : 0.0;
            }

            double[] peakCounts = new double[m];
            for (int k = 0; k < m; k++)
            {
                peakCounts[k] = x[k] * SumRange(cols[k].Values, chLo, chHi);
            }

            return new FitResult
            {
                Columns = cols,
                Amp = x,
                Sigma = sigma,
                Z = z,
                PeakCounts = peakCounts,
                Chi2 = chi2,
                Chi2Ndf = chi2ndf,
                ActiveCount = nActive,
                Residual = residual,
            };
        }

        static double[] BuildTemplate(Component comp, EnergyCalibration cal, FwhmCalibration fwhmCal,
                                      EffCurve eff, double gain, double offset,
                                      int chLo, int chHi, int nch)
        {
            if (comp.StandardCps != null)
            {
                return BuildStandardTemplate(comp, gain, offset, chLo, chHi, nch);
            }

            double[] t = new double[nch];
            bool any = false;
            foreach (NuclideLine line in comp.Lines)
            {
                double p0 = EnergyToChannelSafe(cal, line.Energy, nch);
                if (double.IsNaN(p0)) continue;
                double p = gain * p0 + offset;
                double w = fwhmCal.ChannelToFwhm(p);
                if (w <= 0 || double.IsNaN(w)) continue;
                double sigma = w / 2.35482;
                double q = line.Intensity / 100.0;
                if (eff != null)
                {
                    double e = eff.Eval(line.Energy);
                    if (e <= 0) continue;
                    q *= e;
                }
                int lo = Math.Max(chLo, (int)Math.Floor(p - 5 * sigma));
                int hi = Math.Min(chHi, (int)Math.Ceiling(p + 5 * sigma));
                if (hi < lo) continue;
                double norm = q / (sigma * Math.Sqrt(2.0 * Math.PI));
                for (int i = lo; i <= hi; i++)
                {
                    double d = (i - p) / sigma;
                    t[i] += norm * Math.Exp(-0.5 * d * d);
                }
                any = true;
            }
            return any ? t : null;
        }

        /// <summary>
        /// Образ из измеренного эталона: дрейф (a, b) применяется аффинной
        /// передискретизацией по шкале каналов, как и к расчётным образам.
        /// </summary>
        static double[] BuildStandardTemplate(Component comp, double gain, double offset,
                                              int chLo, int chHi, int nch)
        {
            double[] src = CurrentMode == "snip" ? comp.ResampledPeaksCps : comp.ResampledCps;
            if (src == null || src.Length != nch) return null;
            double[] t = new double[nch];
            bool any = false;
            for (int i = chLo; i <= chHi; i++)
            {
                double u = (i - offset) / gain;
                int u0 = (int)Math.Floor(u);
                if (u0 < 0 || u0 + 1 >= nch) continue;
                double f = u - u0;
                double v = src[u0] * (1.0 - f) + src[u0 + 1] * f;
                if (v > 0) { t[i] = v; any = true; }
            }
            return any ? t : null;
        }

        /// <summary>
        /// Передискретизация образа эталона из его шкалы каналов в шкалу
        /// образца через энергию, с сохранением интеграла: значение канала
        /// образца — интеграл кусочно-постоянного образа по соответствующему
        /// интервалу каналов эталона.
        /// </summary>
        static double[] ResampleToSample(double[] src, EnergyCalibration calStd,
                                         EnergyCalibration calSample, int nchSample)
        {
            double[] dst = new double[nchSample];
            int nchStd = src.Length;
            double uPrev = double.NaN;
            for (int i = 0; i < nchSample; i++)
            {
                double u0 = uPrev;
                if (double.IsNaN(u0))
                {
                    u0 = EnergyToChannelSafe(calStd, calSample.ChannelToEnergy(i - 0.5), nchStd);
                }
                double u1 = EnergyToChannelSafe(calStd, calSample.ChannelToEnergy(i + 0.5), nchStd);
                uPrev = u1;
                if (double.IsNaN(u0) || double.IsNaN(u1) || u1 <= u0) continue;
                double lo = Math.Max(0.0, u0), hi = Math.Min(nchStd - 1e-9, u1);
                if (hi <= lo) continue;
                double sum = 0;
                int b0 = (int)Math.Floor(lo), b1 = (int)Math.Floor(hi);
                if (b0 == b1)
                {
                    sum = src[b0] * (hi - lo);
                }
                else
                {
                    sum += src[b0] * (b0 + 1 - lo);
                    for (int b = b0 + 1; b < b1; b++) sum += src[b];
                    sum += src[b1] * (hi - b1);
                }
                dst[i] = sum;
            }
            return dst;
        }

        /// <summary>Fast NNLS (Bro &amp; de Jong) on precomputed normal equations.</summary>
        static double[] NnlsSolve(double[,] G, double[] c, int m, out bool[] active)
        {
            double[] x = new double[m];
            active = new bool[m];
            // Колонки, добавление которых дало сингулярную активную матрицу
            // (дубликат/коллинеарность): без бана градиент не меняется, и
            // внешний цикл выбирал бы тот же индекс до исчерпания бюджета.
            bool[] banned = new bool[m];
            double tol = 1e-10 * MaxDiag(G, m);
            double[] w = (double[])c.Clone();

            for (int iter = 0; iter < 30 * m; iter++)
            {
                int j = -1;
                double wmax = tol;
                for (int k = 0; k < m; k++)
                {
                    if (!active[k] && !banned[k] && w[k] > wmax) { wmax = w[k]; j = k; }
                }
                if (j < 0) break;
                active[j] = true;

                while (true)
                {
                    double[] z = SolveActive(G, c, active, m);
                    if (z == null) { active[j] = false; banned[j] = true; break; }
                    bool allPositive = true;
                    double alpha = 1.0;
                    for (int k = 0; k < m; k++)
                    {
                        if (active[k] && z[k] <= 0)
                        {
                            allPositive = false;
                            double a = x[k] / (x[k] - z[k]);
                            if (a < alpha) alpha = a;
                        }
                    }
                    if (allPositive)
                    {
                        for (int k = 0; k < m; k++) x[k] = active[k] ? z[k] : 0.0;
                        break;
                    }
                    for (int k = 0; k < m; k++)
                    {
                        if (active[k])
                        {
                            x[k] += alpha * (z[k] - x[k]);
                            if (x[k] <= tol) { x[k] = 0.0; active[k] = false; }
                        }
                    }
                }

                // w = c - Gx
                for (int a = 0; a < m; a++)
                {
                    double s = c[a];
                    for (int b = 0; b < m; b++) if (x[b] != 0) s -= G[a, b] * x[b];
                    w[a] = s;
                }
            }
            return x;
        }

        static double MaxDiag(double[,] G, int m)
        {
            double mx = 0;
            for (int k = 0; k < m; k++) if (G[k, k] > mx) mx = G[k, k];
            return mx > 0 ? mx : 1.0;
        }

        static double[] SolveActive(double[,] G, double[] c, bool[] active, int m)
        {
            List<int> idx = new List<int>();
            for (int k = 0; k < m; k++) if (active[k]) idx.Add(k);
            int n = idx.Count;
            if (n == 0) return null;
            double[,] a = new double[n, n + 1];
            for (int r = 0; r < n; r++)
            {
                for (int q = 0; q < n; q++) a[r, q] = G[idx[r], idx[q]];
                a[r, n] = c[idx[r]];
            }
            if (!GaussSolve(a, n)) return null;
            double[] z = new double[m];
            for (int r = 0; r < n; r++) z[idx[r]] = a[r, n];
            return z;
        }

        static bool GaussSolve(double[,] a, int n)
        {
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                for (int r = col + 1; r < n; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col])) piv = r;
                }
                if (Math.Abs(a[piv, col]) < 1e-30) return false;
                if (piv != col)
                {
                    for (int q = col; q <= n; q++)
                    {
                        double tmp = a[col, q]; a[col, q] = a[piv, q]; a[piv, q] = tmp;
                    }
                }
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = a[r, col] / a[col, col];
                    if (f == 0) continue;
                    for (int q = col; q <= n; q++) a[r, q] -= f * a[col, q];
                }
            }
            for (int r = 0; r < n; r++) a[r, n] /= a[r, r];
            return true;
        }

        static double[,] InvertSymmetric(double[,] src, int n)
        {
            double[,] a = new double[n, 2 * n];
            for (int r = 0; r < n; r++)
            {
                for (int q = 0; q < n; q++) a[r, q] = src[r, q];
                a[r, n + r] = 1.0;
            }
            for (int col = 0; col < n; col++)
            {
                int piv = col;
                for (int r = col + 1; r < n; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col])) piv = r;
                }
                if (Math.Abs(a[piv, col]) < 1e-30) return null;
                if (piv != col)
                {
                    for (int q = 0; q < 2 * n; q++)
                    {
                        double tmp = a[col, q]; a[col, q] = a[piv, q]; a[piv, q] = tmp;
                    }
                }
                double d = a[col, col];
                for (int q = 0; q < 2 * n; q++) a[col, q] /= d;
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = a[r, col];
                    if (f == 0) continue;
                    for (int q = 0; q < 2 * n; q++) a[r, q] -= f * a[col, q];
                }
            }
            double[,] inv = new double[n, n];
            for (int r = 0; r < n; r++)
                for (int q = 0; q < n; q++) inv[r, q] = a[r, n + q];
            return inv;
        }

        // ------------------------------------------------------------------
        // Continuum
        // ------------------------------------------------------------------

        static int[] Snip(FwhmCalibration fwhmCal, EnergySpectrum es)
        {
            SpectrumAriphmetics sa = new SpectrumAriphmetics(fwhmCal, es, SmoothingMethod.None);
            EnergySpectrum cont = sa.Continuum();
            return cont.Spectrum;
        }

        /// <summary>
        /// Piecewise-linear "hat" basis for the continuum in spline mode. Knot
        /// spacing is tied to the local FWHM so the continuum cannot absorb
        /// single peaks.
        /// </summary>
        static List<double[]> BuildHatBasis(FwhmCalibration fwhmCal, int chLo, int chHi, int nch)
        {
            List<int> knots = new List<int>();
            double minStep = (chHi - chLo) / 64.0;
            int ch = chLo;
            while (ch < chHi)
            {
                knots.Add(ch);
                double w = fwhmCal.ChannelToFwhm(ch);
                if (double.IsNaN(w) || w < 1) w = 1;
                ch += (int)Math.Max(1, Math.Max(4.0 * w, minStep));
            }
            // Узел в 1 канале от chHi дал бы «шапку-спицу» — почти коллинеарную
            // колонку; сливаем только этот вырожденный случай. Порог шире
            // (например, 4·FWHM) нельзя: на грубых детекторах он снимает
            // легитимный узел и дубит континуум у верхнего края —
            // χ²/ndf RC103-разреза портился 14.2 → 16.0.
            if (knots.Count > 1 && chHi - knots[knots.Count - 1] < 2)
            {
                knots.RemoveAt(knots.Count - 1);
            }
            knots.Add(chHi);

            List<double[]> hats = new List<double[]>();
            for (int k = 0; k < knots.Count; k++)
            {
                int left = k > 0 ? knots[k - 1] : knots[k];
                int mid = knots[k];
                int right = k + 1 < knots.Count ? knots[k + 1] : knots[k];
                double[] hat = new double[nch];
                for (int i = left; i <= right; i++)
                {
                    double v;
                    if (i == mid) v = 1.0;
                    else if (i < mid) v = left == mid ? 1.0 : (double)(i - left) / (mid - left);
                    else v = right == mid ? 1.0 : (double)(right - i) / (right - mid);
                    if (v > 0) hat[i] = v;
                }
                hats.Add(hat);
            }
            return hats;
        }

        // ------------------------------------------------------------------
        // Component library
        // ------------------------------------------------------------------

        sealed class NuclideLine
        {
            public string Nuclide;     // leading token of the definition name
            public double Energy;
            public double Intensity;   // % per decay of the (chain parent) nuclide
        }

        sealed class Component
        {
            public string Name;
            public string Kind;        // "chain" | "single" | "nuisance" | "standard"
            public List<NuclideLine> Lines = new List<NuclideLine>();
            // Образ из измеренного эталона (канонический путь FSA): скорость
            // счёта по каналам В ШКАЛЕ ЭТАЛОНА, полный и без собственного
            // континуума. У каждого спектра корпуса своя калибровка, поэтому
            // перед фитом образ передискретизируется в шкалу образца (поля
            // Resampled*, заполняются в RunOne на каждый спектр заново).
            public double[] StandardCps;
            public double[] StandardPeaksCps;
            public EnergyCalibration StandardCal;
            public double[] ResampledCps;
            public double[] ResampledPeaksCps;
        }

        static List<Component> BuildLibrary(NuclideDefinitionManager nm, List<string> requested,
                                            List<string> splitChains)
        {
            Dictionary<string, List<Component>> splitParts =
                new Dictionary<string, List<Component>>(StringComparer.OrdinalIgnoreCase);
            // chain components from nuclide sets in the workdir config
            Dictionary<string, Component> chains = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
            foreach (NuclideSet set in nm.NuclideSets)
            {
                if (set == null || string.IsNullOrEmpty(set.Name)) continue;
                if (set.Name.IndexOf("~decoy", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                string prefix = set.Name.Split('|')[0].Trim();
                if (chains.ContainsKey(prefix)) continue;
                Component comp = new Component { Name = prefix, Kind = "chain" };
                foreach (NuclideDefinition def in nm.NuclideDefinitions)
                {
                    if (def == null || def.Sets == null || !def.Sets.Contains(set.Id)) continue;
                    comp.Lines.Add(new NuclideLine
                    {
                        Nuclide = (def.Name ?? "").Split(' ')[0],
                        Energy = def.Energy,
                        Intensity = def.Intencity,
                    });
                }
                if (comp.Lines.Count > 0) chains[prefix] = comp;
            }

            // Сет U-238 в конфиге несёт всю цепочку, включая радиевую ветвь.
            // Образец «голова ряда без радия» (урановое стекло) такой шаблон
            // фитовать не может: NNLS зануляет U-238 целиком и вешает 63 кэВ
            // Th-234 на чужой компонент. Радиевую ветвь здесь вырезаем — её
            // целиком представляет компонент Ra-226; равновесный ряд U-238
            // тогда «U-238 (голова) + Ra-226».
            Component u238, ra226;
            if (chains.TryGetValue("U-238", out u238) && chains.TryGetValue("Ra-226", out ra226))
            {
                HashSet<string> radium = new HashSet<string>(
                    ra226.Lines.Select(l => l.Nuclide), StringComparer.OrdinalIgnoreCase);
                u238.Lines.RemoveAll(l => radium.Contains(l.Nuclide));
                if (u238.Lines.Count == 0) chains.Remove("U-238");
            }

            // --split-chain: цепочка разрезается на дочерние нуклиды, каждый
            // фитуется свободно. Жёсткая связка интенсивностей внутри одного
            // образа — сила метода против фантомов, но при перекосе
            // эффективности/каскадов она заставляет NNLS «бросать» слабые
            // линии (Ac-228 911/969). Отношение амплитуд дочерних — заодно
            // проверка векового равновесия.
            foreach (string chainName in splitChains)
            {
                Component chain;
                if (!chains.TryGetValue(chainName, out chain))
                {
                    Console.Error.WriteLine("--split-chain: no such chain: " + chainName);
                    continue;
                }
                chains.Remove(chainName);
                // Части не кладутся в chains: имя дочернего нуклида может
                // совпадать с самостоятельной цепочкой (Th-228 внутри
                // Th-232), и запись в общий словарь подменяла бы её огрызком.
                List<Component> parts = new List<Component>();
                foreach (var grp in chain.Lines.GroupBy(l => l.Nuclide, StringComparer.OrdinalIgnoreCase))
                {
                    Component part = new Component { Name = grp.Key, Kind = "chain" };
                    part.Lines.AddRange(grp);
                    parts.Add(part);
                }
                splitParts[chainName] = parts;
            }

            Dictionary<string, Component> singles = BuiltinSingles();

            List<Component> result = new List<Component>();
            HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string wanted in requested)
            {
                if (!taken.Add(wanted))
                {
                    // дубль дал бы две идентичные колонки и сингулярный NNLS
                    Console.Error.WriteLine("Duplicate component ignored: " + wanted);
                    continue;
                }
                List<Component> parts;
                if (splitParts.TryGetValue(wanted, out parts))
                {
                    result.AddRange(parts);
                    continue;
                }
                Component comp;
                if (chains.TryGetValue(wanted, out comp)) { result.Add(comp); continue; }
                if (singles.TryGetValue(wanted, out comp)) { result.Add(comp); continue; }
                Console.Error.WriteLine("Component not available (no set, no builtin table): " + wanted);
            }
            return result;
        }

        static Dictionary<string, Component> BuiltinSingles()
        {
            var d = new Dictionary<string, Component>(StringComparer.OrdinalIgnoreCase);
            Action<string, double[,]> add = (compName, lines) =>
            {
                Component comp = new Component { Name = compName, Kind = "single" };
                for (int i = 0; i < lines.GetLength(0); i++)
                {
                    comp.Lines.Add(new NuclideLine { Energy = lines[i, 0], Intensity = lines[i, 1] });
                }
                d[compName] = comp;
            };
            add("K-40", new double[,] { { 1460.822, 10.66 } });
            add("Cs-137", new double[,] { { 661.657, 85.10 } });
            add("Am-241", new double[,] { { 59.541, 35.92 }, { 26.345, 2.31 } });
            add("Co-60", new double[,] { { 1173.228, 99.85 }, { 1332.492, 99.9826 } });
            add("I-131", new double[,] {
                { 364.489, 81.5 }, { 636.989, 7.16 }, { 284.305, 6.12 },
                { 80.185, 2.62 }, { 722.911, 1.77 } });
            add("Eu-152", new double[,] {
                { 121.782, 28.53 }, { 244.697, 7.55 }, { 344.279, 26.59 }, { 411.116, 2.24 },
                { 443.965, 2.80 }, { 778.904, 12.93 }, { 867.380, 4.23 }, { 964.079, 14.51 },
                { 1085.837, 10.11 }, { 1089.737, 1.73 }, { 1112.076, 13.67 }, { 1212.948, 1.42 },
                { 1299.142, 1.62 }, { 1408.013, 20.87 } });
            add("Ba-133", new double[,] {
                { 80.998, 34.06 }, { 79.614, 2.65 }, { 276.399, 7.16 },
                { 302.851, 18.34 }, { 356.013, 62.05 }, { 383.848, 8.94 } });
            add("Lu-176", new double[,] { { 88.34, 14.5 }, { 201.83, 78.0 }, { 306.78, 93.6 } });
            // Характеристический рентген — не нуклиды, а «мешающие» образы:
            // флуоресценция вольфрама (ториевые WT-электроды) и свинца (домик).
            // Без них NNLS вешает пик 58-59 кэВ на Am-241 (59.5 кэВ).
            // Интенсивности — относительные веса внутри серии.
            add("Xray-W", new double[,] {
                { 59.318, 100.0 }, { 57.981, 57.6 }, { 67.244, 22.0 }, { 69.067, 8.0 } });
            add("Xray-Pb", new double[,] {
                { 74.969, 100.0 }, { 72.804, 59.5 }, { 84.936, 23.0 }, { 87.300, 8.0 } });
            // Пики вылета от 2614.5 кэВ (Tl-208): одиночный (−511) и двойной
            // (−1022). Образы генерируются только для пиков полного
            // поглощения, а доли вылета зависят от кристалла — поэтому это
            // отдельные мешающие образы со свободной амплитудой, а не строки
            // внутри ториевого образа.
            add("SE-2614", new double[,] { { 2103.5, 100.0 } });
            add("DE-2614", new double[,] { { 1592.5, 100.0 } });
            d["Xray-W"].Kind = "nuisance";
            d["Xray-Pb"].Kind = "nuisance";
            d["SE-2614"].Kind = "nuisance";
            d["DE-2614"].Kind = "nuisance";
            return d;
        }

        // ------------------------------------------------------------------
        // Efficiency curves (CSV: spectrum,E_keV,eps) — log-log interpolation
        // ------------------------------------------------------------------

        sealed class EffCurve
        {
            readonly double[] logE;
            readonly double[] logEps;

            EffCurve(List<KeyValuePair<double, double>> points)
            {
                points.Sort((a, b) => a.Key.CompareTo(b.Key));
                // дубль энергии дал бы нулевой шаг интерполяции и NaN в шаблоне
                List<KeyValuePair<double, double>> uniq = new List<KeyValuePair<double, double>>(points.Count);
                foreach (KeyValuePair<double, double> p in points)
                {
                    if (uniq.Count > 0 && p.Key <= uniq[uniq.Count - 1].Key) continue;
                    uniq.Add(p);
                }
                logE = uniq.Select(p => Math.Log(p.Key)).ToArray();
                logEps = uniq.Select(p => Math.Log(Math.Max(p.Value, 1e-12))).ToArray();
            }

            public double Eval(double energy)
            {
                if (logE.Length == 0) return 1.0;
                double x = Math.Log(Math.Max(energy, 1.0));
                if (x <= logE[0]) return Math.Exp(logEps[0]);
                if (x >= logE[logE.Length - 1]) return Math.Exp(logEps[logE.Length - 1]);
                int hi = 1;
                while (logE[hi] < x) hi++;
                double f = (x - logE[hi - 1]) / (logE[hi] - logE[hi - 1]);
                return Math.Exp(logEps[hi - 1] + f * (logEps[hi] - logEps[hi - 1]));
            }

            public static Dictionary<string, EffCurve> LoadTable(string path)
            {
                var result = new Dictionary<string, EffCurve>(StringComparer.OrdinalIgnoreCase);
                if (string.IsNullOrEmpty(path)) return result;
                var raw = new Dictionary<string, List<KeyValuePair<double, double>>>(StringComparer.OrdinalIgnoreCase);
                string[] lines = File.ReadAllLines(path);
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0) continue;
                    string[] parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    double e, eps;
                    if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out e)) continue;
                    if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out eps)) continue;
                    List<KeyValuePair<double, double>> list;
                    if (!raw.TryGetValue(parts[0], out list))
                    {
                        list = new List<KeyValuePair<double, double>>();
                        raw[parts[0]] = list;
                    }
                    list.Add(new KeyValuePair<double, double>(e, eps));
                }
                foreach (var kv in raw)
                {
                    if (kv.Value.Count >= 2) result[kv.Key] = new EffCurve(kv.Value);
                }
                return result;
            }
        }

        // ------------------------------------------------------------------
        // IO / plumbing
        // ------------------------------------------------------------------

        static ResultData LoadResultData(string spectrumFile, int resultIndex)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (FileStream stream = new FileStream(spectrumFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData resultData = resultIndex < file.ResultDataList.Count
                ? file.ResultDataList[resultIndex]
                : file.ResultDataList.First();
            EnsureSpectrumIntegrity(resultData.EnergySpectrum);
            EnsureSpectrumIntegrity(resultData.BackgroundEnergySpectrum);

            DeviceConfigInfo deviceConfig = DeviceConfigManager.GetInstance().DeviceConfigList
                .FirstOrDefault(candidate => candidate.Guid == resultData.DeviceConfigReference.Guid);
            if (deviceConfig == null)
            {
                throw new InvalidOperationException("Device config not found for " + resultData.DeviceConfigReference.Guid);
            }
            resultData.DeviceConfig = deviceConfig;

            if (resultData.FwhmCalibration == null)
            {
                FWHMPeakDetectionMethodConfig fwhmPeakConfig =
                    (FWHMPeakDetectionMethodConfig)deviceConfig.PeakDetectionMethodConfig;
                resultData.FwhmCalibration = fwhmPeakConfig.FwhmCalibration != null
                    ? fwhmPeakConfig.FwhmCalibration.Clone()
                    : FwhmCalibration.DefaultCalibration(fwhmPeakConfig, resultData.EnergySpectrum.EnergyCalibration);
            }
            if (resultData.FwhmCalibration == null)
            {
                throw new InvalidOperationException("No FWHM calibration available");
            }

            return resultData;
        }

        /// <summary>
        /// Элементарный спектр из измеренного эталона: вычесть встроенный фон
        /// (нормировка по живому времени), привести к скорости счёта; вторая
        /// версия — без собственного континуума (для режима snip).
        /// </summary>
        static Component LoadStandard(string name, string path, int resultIndex)
        {
            ResultData rd = LoadResultData(path, resultIndex);
            EnergySpectrum es = rd.EnergySpectrum;
            int nch = es.NumberOfChannels;
            double liveT = es.LiveTime > 0 ? es.LiveTime : es.MeasurementTime;
            if (liveT <= 0) throw new InvalidOperationException("standard has no live time: " + path);

            double bgScale = 0.0;
            EnergySpectrum bg = rd.BackgroundEnergySpectrum;
            if (bg != null && bg.Spectrum != null && bg.NumberOfChannels == nch)
            {
                double bgLive = bg.LiveTime > 0 ? bg.LiveTime : bg.MeasurementTime;
                if (bgLive > 0) bgScale = liveT / bgLive;
            }

            int[] net = new int[nch];
            for (int i = 0; i < nch; i++)
            {
                double v = es.Spectrum[i];
                if (bgScale > 0) v -= bg.Spectrum[i] * bgScale;
                net[i] = (int)Math.Max(0.0, Math.Round(v));
            }

            EnergySpectrum esNet = es.Clone();
            esNet.Spectrum = net;
            int[] cont = Snip(rd.FwhmCalibration, esNet);

            Component comp = new Component { Name = name, Kind = "standard" };
            comp.StandardCps = new double[nch];
            comp.StandardPeaksCps = new double[nch];
            comp.StandardCal = es.EnergyCalibration;
            for (int i = 0; i < nch; i++)
            {
                comp.StandardCps[i] = net[i] / liveT;
                comp.StandardPeaksCps[i] = Math.Max(0, net[i] - cont[i]) / liveT;
            }
            Console.Error.WriteLine("standard {0} <- {1} ({2} ch, {3:F0} s live)", name, path, nch, liveT);
            return comp;
        }

        /// <summary>
        /// Загрузка только спектра с калибровкой — без поиска конфига
        /// устройства (для внешних файлов, чьи приборы не заведены в workdir).
        /// </summary>
        static EnergySpectrum LoadSpectrumOnly(string path, int resultIndex)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }
            ResultData rd = resultIndex < file.ResultDataList.Count
                ? file.ResultDataList[resultIndex]
                : file.ResultDataList.First();
            EnsureSpectrumIntegrity(rd.EnergySpectrum);
            PolynomialEnergyCalibration pcal = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (pcal != null) pcal.CheckCalibration(rd.EnergySpectrum.NumberOfChannels);
            return rd.EnergySpectrum;
        }

        static void EnsureSpectrumIntegrity(EnergySpectrum spectrum)
        {
            if (spectrum == null || spectrum.Spectrum == null) return;
            if (spectrum.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < spectrum.Spectrum.Length; i++) total += spectrum.Spectrum[i];
                spectrum.TotalPulseCount = total;
                spectrum.ValidPulseCount = total;
            }
        }

        static List<string> ResolveSpectrumFiles(string inputPath)
        {
            if (File.Exists(inputPath))
            {
                return new List<string> { Path.GetFullPath(inputPath) };
            }
            if (!Directory.Exists(inputPath))
            {
                throw new DirectoryNotFoundException(inputPath);
            }
            return Directory.GetFiles(inputPath, "*.xml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static double EnergyToChannelSafe(EnergyCalibration cal, double energy, int nch)
        {
            try { return cal.EnergyToChannel(energy, nch); }
            catch { return double.NaN; }
        }

        static int ClampChannel(double ch, int nch)
        {
            if (double.IsNaN(ch)) return 0;
            if (ch < 0) return 0;
            if (ch > nch - 1) return nch - 1;
            return (int)Math.Round(ch);
        }

        static double SumRange(double[] v, int lo, int hi)
        {
            double s = 0;
            for (int i = lo; i <= hi; i++) s += v[i];
            return s;
        }

        static void DumpModel(string dir, string name, EnergyCalibration cal,
                              int[] raw, double[] y, int[] continuum, FitResult best,
                              int chLo, int chHi, List<string> fixedNames)
        {
            Directory.CreateDirectory(dir);

            // покомпонентные вклады; шапки континуума схлопываются в одну кривую
            List<KeyValuePair<string, double[]>> curves = new List<KeyValuePair<string, double[]>>();
            double[] hats = null;
            for (int k = 0; k < best.Columns.Count; k++)
            {
                if (best.Amp[k] <= 0) continue;
                double[] contrib = new double[best.Columns[k].Values.Length];
                for (int i = chLo; i <= chHi; i++) contrib[i] = best.Amp[k] * best.Columns[k].Values[i];
                Component comp = best.Columns[k].Component;
                if (comp != null)
                {
                    curves.Add(new KeyValuePair<string, double[]>(comp.Name, contrib));
                }
                else if (fixedNames[best.Columns[k].FixedIndex] == "hat")
                {
                    if (hats == null) hats = contrib;
                    else for (int i = chLo; i <= chHi; i++) hats[i] += contrib[i];
                }
                else
                {
                    curves.Add(new KeyValuePair<string, double[]>("bgfit", contrib));
                }
            }
            if (hats != null) curves.Add(new KeyValuePair<string, double[]>("hats", hats));

            string path = Path.Combine(dir, name + "_model.csv");
            using (StreamWriter w = new StreamWriter(path, false, Encoding.UTF8))
            {
                StringBuilder header = new StringBuilder("channel,energy,raw,continuum,y,model,residual");
                foreach (KeyValuePair<string, double[]> c in curves) header.Append(',').Append(Csv(c.Key));
                w.WriteLine(header.ToString());
                for (int i = chLo; i <= chHi; i++)
                {
                    double model = y[i] - best.Residual[i];
                    StringBuilder sb = new StringBuilder(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F2},{2},{3},{4:F3},{5:F3},{6:F3}",
                        i, cal.ChannelToEnergy(i), raw[i],
                        continuum != null ? continuum[i] : 0, y[i], model, best.Residual[i]));
                    foreach (KeyValuePair<string, double[]> c in curves)
                    {
                        sb.Append(',').Append(c.Value[i].ToString("G6", CultureInfo.InvariantCulture));
                    }
                    w.WriteLine(sb.ToString());
                }
            }
        }

        static string Csv(string value)
        {
            if (value == null) return "";
            if (value.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // ------------------------------------------------------------------
        // Options
        // ------------------------------------------------------------------

        sealed class Options
        {
            public string InputPath;
            public string WorkingDirectory;
            public string Mode = "snip";                  // snip | spline
            public List<string> Components = new List<string>
            {
                "Th-232", "Ra-226", "U-238", "U-235",
                "K-40", "Cs-137", "Am-241", "Co-60", "I-131", "Eu-152", "Ba-133",
                "Xray-W", "Xray-Pb", "SE-2614", "DE-2614",
            };
            public double EMin = 40;
            public double EMax = 2800;
            public string EffCurvePath;
            public double Xi = 0.03;
            public double HuberM = 3.0;
            public double GainRange = 0.008;
            public int GainSteps = 9;
            public double OffsetRangeKev = 3.0;
            public int OffsetSteps = 9;
            public bool UseBackground = true;
            public double RefitZ = 3.0;
            public double BgSigma = 0.15;
            public string BgMode = "fixed";               // fixed | fit
            public string BgFile;
            public List<KeyValuePair<string, string>> Standards = new List<KeyValuePair<string, string>>();
            public List<string> SplitChains = new List<string>();
            public string OutPrefix = "pie";
            public string DumpDir;
            public int ResultIndex = 0;

            public static Options Parse(string[] args)
            {
                Options o = new Options();
                foreach (string arg in args)
                {
                    string a = arg;
                    string value = null;
                    int eq = arg.IndexOf('=');
                    if (eq >= 0) { a = arg.Substring(0, eq); value = arg.Substring(eq + 1); }
                    switch (a)
                    {
                        case "--input": o.InputPath = value; break;
                        case "--workdir": o.WorkingDirectory = value; break;
                        case "--mode": o.Mode = value; break;
                        case "--components": o.Components = value.Split(',').Select(s => s.Trim())
                            .Where(s => s.Length > 0).ToList(); break;
                        case "--emin": o.EMin = D(value); break;
                        case "--emax": o.EMax = D(value); break;
                        case "--eff-curve": o.EffCurvePath = value; break;
                        case "--xi": o.Xi = D(value); break;
                        case "--huber": o.HuberM = D(value); break;
                        case "--gain-range": o.GainRange = D(value); break;
                        case "--gain-steps": o.GainSteps = (int)D(value); break;
                        case "--offset-range": o.OffsetRangeKev = D(value); break;
                        case "--offset-steps": o.OffsetSteps = (int)D(value); break;
                        case "--no-bg": o.UseBackground = false; break;
                        case "--refit-z": o.RefitZ = D(value); break;
                        case "--bg-sigma": o.BgSigma = D(value); break;
                        case "--bg": o.BgMode = value; break;
                        case "--bg-file": o.BgFile = value; break;
                        case "--split-chain": o.SplitChains.AddRange(
                            value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)); break;
                        case "--standard":
                        {
                            // формат Имя=путь ('=' а не ':' — в путях Windows есть двоеточие)
                            string[] kv = value.Split(new[] { '=' }, 2);
                            if (kv.Length != 2) throw new ArgumentException("--standard=Name=path.xml");
                            o.Standards.Add(new KeyValuePair<string, string>(kv[0].Trim(), kv[1].Trim()));
                            break;
                        }
                        case "--no-drift": o.GainSteps = 1; o.OffsetSteps = 1; break;
                        case "--out": o.OutPrefix = value; break;
                        case "--dump-model": o.DumpDir = value; break;
                        case "--result-index": o.ResultIndex = (int)D(value); break;
                        default:
                            throw new ArgumentException("Unknown option: " + arg);
                    }
                }
                if (string.IsNullOrEmpty(o.InputPath))
                {
                    throw new ArgumentException("--input=<file|dir> is required");
                }
                if (o.Mode != "snip" && o.Mode != "spline")
                {
                    throw new ArgumentException("--mode must be snip or spline");
                }
                return o;
            }

            static double D(string s)
            {
                return double.Parse(s, CultureInfo.InvariantCulture);
            }
        }
    }
}
