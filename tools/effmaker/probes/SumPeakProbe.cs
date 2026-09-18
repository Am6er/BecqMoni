using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace SumPeakProbe
{
    /// <summary>
    /// (`S170`, П71 14.09.2026) СУММ-ПИК ДВУХ ЛИНИЙ ПРОТИВ ДАННЫХ — РАЗВОД
    /// МНОЖИТЕЛЕЙ. Мерка П49 («данные = измерено − модель без каскада» в
    /// окне 2460…2560) смешивала три вещи: уровень сумм-пика модели, его
    /// ПОЛОЖЕНИЕ на шкале (сумма по свету стоит не там, где кладёт модель) и
    /// подложку, которую сплайн модели без каскада подкладывает под сумм-пик.
    /// Здесь каждая мерится порознь и без модели там, где можно:
    ///
    ///   * ДАННЫЕ: чистые площади трёх пиков (E₁, E₂, E₁+E₂) над ЛИНЕЙНОЙ
    ///     подложкой по боковым полосам, центроиды в каналах и кэВ, ПШПВ;
    ///     паспортная активность на дату съёмки (`SampleInfo/Note`);
    ///     измеренные ε_p(E₁), ε_p(E₂) = N/(A·t·I) и активность методом
    ///     сумм-пика A = N₁·N₂/(N_Σ·t) — ей эффективность не нужна вовсе;
    ///   * МАТРИЦА/СУММАТОР: ε_p узлов, κ (`JointFactor`), выживание,
    ///     угловой множитель, площадь сумм-пика на распад, видимая сумма по
    ///     свету (`ApparentSum`) и её канал по калибровке спектра;
    ///   * РАЗБОР: пять плеч анализатора (матрица без каскада; +CF; +сумм-пики
    ///     изотропно; +угловые корреляции; +наложения) — амплитуда нуклида в
    ///     распадах/с против паспорта, отсчёты МОДЕЛИ в тех же окнах, что у
    ///     данных, тем же линейным вычетом, подслой сумм-пиков целиком и его
    ///     центроид, и мерка П49 в её окне — для положительного контроля.
    ///
    ///   sumpeakprobe --spectrum=X.xml --sample=Co-60 [--pair=1173.228:1332.492]
    ///                [--window=2460:2560] [--pileup-tau=<мкс>]
    ///
    /// Запускать из оснастки корпуса (`mk_appwd.ps1`). Состав — из базы по
    /// ключам (`AMBER19`), как у `FsaCascadeProbe`.
    /// </summary>
    static class Program
    {
        const double Ln2 = 0.69314718055994531;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            var nuclides = new List<string>();
            double e1 = 1173.228, e2 = 1332.492, i1 = 99.85, i2 = 99.983;
            double winLo = 2460.0, winHi = 2560.0;
            double tauUs = 0.0;
            double halfLifeDays = 1925.28;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--sample=", StringComparison.Ordinal))
                    nuclides.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--pair=", StringComparison.Ordinal))
                {
                    string[] p = a.Substring(7).Split(':');
                    e1 = double.Parse(p[0], CultureInfo.InvariantCulture);
                    e2 = double.Parse(p[1], CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--window=", StringComparison.Ordinal))
                {
                    string[] p = a.Substring(9).Split(':');
                    winLo = double.Parse(p[0], CultureInfo.InvariantCulture);
                    winHi = double.Parse(p[1], CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--pileup-tau=", StringComparison.Ordinal))
                    tauUs = double.Parse(a.Substring(13), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--halflife-days=", StringComparison.Ordinal))
                    halfLifeDays = double.Parse(a.Substring(16), CultureInfo.InvariantCulture);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (spectrumPath == null || nuclides.Count == 0)
            {
                Console.Error.WriteLine("нужны --spectrum=<файл> и --sample=<nucid>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            EnergySpectrum es = rd.EnergySpectrum;
            EnergyCalibration cal = es.EnergyCalibration;
            int[] raw = es.Spectrum;
            double live = es.LiveTime > 0.0 ? es.LiveTime : es.MeasurementTime;
            double real = es.MeasurementTime;
            long total = 0;
            foreach (int v in raw) total += v;

            Console.WriteLine("спектр: {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("каналов {0}; live {1:F1} с, real {2:F1} с, мёртвое {3:F2} %; всего {4} отсчётов, {5:F0} имп/с",
                              raw.Length, live, real, real > 0 ? 100.0 * (1.0 - live / real) : 0.0, total, total / live);

            // ---- паспорт --------------------------------------------------
            double aPass = 0.0, dA = 0.0;
            {
                string note = rd.SampleInfo != null && rd.SampleInfo.Note != null ? rd.SampleInfo.Note.ToString() : "";
                Match m = Regex.Match(note, @"A=(\d+)\s*Бк\s*dA=([\d.]+)%\s*(\d\d)-(\d\d)-(\d{4})");
                if (m.Success && rd.SampleInfo.Time != DateTime.MinValue)
                {
                    double a0 = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    dA = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    var d0 = new DateTime(int.Parse(m.Groups[5].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[3].Value));
                    double days = (rd.SampleInfo.Time - d0).TotalDays;
                    aPass = a0 * Math.Exp(-Ln2 * days / halfLifeDays);
                    Console.WriteLine("паспорт: A₀ = {0:F0} Бк ±{1:F0} % на {2:yyyy-MM-dd}; съёмка {3:yyyy-MM-dd}, {4:F0} сут → A = {5:F0} Бк",
                                      a0, dA, d0, rd.SampleInfo.Time, days, aPass);
                }
                else
                {
                    Console.WriteLine("паспорт: не разобран («{0}»)", note);
                }
            }

            // ---- данные: три пика --------------------------------------
            Console.WriteLine();
            Console.WriteLine("=== ДАННЫЕ: пики над линейной подложкой ===");
            Window w1 = Measure(raw, cal, e1, 60.0, 30.0, "E1");
            Window w2 = Measure(raw, cal, e2, 60.0, 30.0, "E2");
            Window ws = Measure(raw, cal, e1 + e2, 130.0, 50.0, "E1+E2");
            foreach (Window w in new[] { w1, w2, ws })
            {
                Console.WriteLine("  {0,-6} каналы {1}..{2} (макс {3}), подложка {4:F1}/{5:F1}: gross {6:F0}, net {7:F0}; центроид {8:F2} кан = {9:F1} кэВ; ПШПВ {10:F1} кан = {11:F1} кэВ ({12:F2} %)",
                                  w.Name, w.Lo, w.Hi - 1, w.Max, w.BgLeft, w.BgRight, w.Gross, w.Net,
                                  w.CentroidCh, w.CentroidKev, w.FwhmCh, w.FwhmKev, 100.0 * w.FwhmKev / w.CentroidKev);
            }

            // Аффинная шкала по двум пикам: куда падает канал суммы относительно E1+E2.
            double gainCh = (e2 - e1) / (w2.CentroidCh - w1.CentroidCh);
            double zeroKev = e1 - gainCh * w1.CentroidCh;
            double sumByAffine = zeroKev + gainCh * ws.CentroidCh;
            double sumChByCal = cal.EnergyToChannel(e1 + e2, raw.Length);
            Console.WriteLine("  аффинная шкала по E1/E2: {0:F4} кэВ/кан, ноль {1:F2}; сумм-пик по ней {2:F1} кэВ = E1+E2 {3:+0.0;-0.0} кэВ; "
                              + "канал суммы {4:F2} против канала E1+E2 по калибровке спектра {5:F2} ({6:+0.00;-0.00} кан, {7:+0.0;-0.0} кэВ по калибровке)",
                              gainCh, zeroKev, sumByAffine, sumByAffine - (e1 + e2), ws.CentroidCh, sumChByCal,
                              ws.CentroidCh - sumChByCal, ws.CentroidKev - (e1 + e2));

            double epsMeas1 = 0.0, epsMeas2 = 0.0;
            if (aPass > 0.0)
            {
                epsMeas1 = w1.Net / (aPass * live * i1 / 100.0);
                epsMeas2 = w2.Net / (aPass * live * i2 / 100.0);
                double aSum = w1.Net * w2.Net / (ws.Net * live);
                double aSumT = aSum + total / live;
                Console.WriteLine("  по паспорту: ε_p(E1) = {0:F5}, ε_p(E2) = {1:F5} (наблюдённые, без CF)", epsMeas1, epsMeas2);
                Console.WriteLine("  метод сумм-пика (без эффективности): A = N1·N2/(NΣ·t) = {0:F0} Бк = {1:F3} паспорта; с полным счётом (Бринкман) {2:F0} Бк = {3:F3}",
                                  aSum, aSum / aPass, aSumT, aSumT / aPass);
                Console.WriteLine("  ε_p(E2) из NΣ/N1 = {0:F5} (= ε_p(E2)·W/(1−ε_T)), ε_p(E1) из NΣ/N2 = {1:F5}",
                                  ws.Net / w1.Net, ws.Net / w2.Net);
            }

            // ---- случайные наложения: оценка по τ ------------------------
            {
                double r1 = w1.Net / live, r2 = w2.Net / live;
                double tauDead = total > 0 ? (real - live) / total * 1e6 : 0.0;
                Console.WriteLine("  случайные совпадения E1+E2 за live: 2·τ·R1·R2·t = {0:F0}·τ[мкс]; τ по мёртвому времени {1:F2} мкс → {2:F0} отсчётов = {3:F1} % NΣ{4}",
                                  2.0 * r1 * r2 * live * 1e-6, tauDead, 2.0 * r1 * r2 * live * tauDead * 1e-6,
                                  100.0 * 2.0 * r1 * r2 * live * tauDead * 1e-6 / ws.Net,
                                  tauUs > 0.0 ? string.Format(CultureInfo.InvariantCulture, "; при τ = {0:F2} мкс → {1:F0} = {2:F1} %",
                                                              tauUs, 2.0 * r1 * r2 * live * tauUs * 1e-6, 100.0 * 2.0 * r1 * r2 * live * tauUs * 1e-6 / ws.Net) : "");
            }

            // ---- библиотека, матрица, сумматор -------------------------
            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, new List<string>(), NucidsOf(nuclides), true, true);
            FsaCalculationOptions.Of(rd).ApplyTo(spec);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            List<Peak> peaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             null, FsaSampleLibrary.AsDefinitions(library));
            Console.WriteLine();
            Console.WriteLine("состав: {0}; пиков {1}, компонентов {2}", string.Join(",", nuclides.ToArray()), peaks.Count, library.Count);

            string guid = rd.Efficiency != null ? rd.Efficiency.Guid : null;
            ResponseMatrix matrix = ResponseMatrixStore.Load(guid);
            bool valid = matrix != null && rd.Efficiency != null && rd.Efficiency.HasGeometry && matrix.IsValidFor(rd.Efficiency.Geometry);
            Console.WriteLine("матрица: {0}, отпечаток {1}; клеймо {2}", matrix == null ? "нет" : "есть", valid ? "сошёлся" : "НЕ сошёлся",
                              matrix != null ? matrix.Stamp : "-");
            if (!valid)
            {
                return 1;
            }

            string scintillator = EfficiencySimulator.ScintillatorNameOf(rd.Efficiency.Geometry);
            FsaCascadeSummer summer = FsaCascadeSummer.Create(matrix, scintillator);
            if (summer == null)
            {
                Console.Error.WriteLine("суммирователь не создан");
                return 1;
            }

            // (`AMBER46`, П87) Таблица Q_k — из самой матрицы (формат 9), сайдкаров нет.
            AngularAttenuation qk = matrix.AngularQk;
            double epsP1 = summer.PeakEfficiency(e1), epsP2 = summer.PeakEfficiency(e2);
            double epsT1 = summer.TotalEfficiency(e1), epsT2 = summer.TotalEfficiency(e2);
            double kappa = matrix.JointFactor(e1, e2);
            double apparent = summer.ApparentSum(e1, e2);
            Console.WriteLine();
            Console.WriteLine("=== МАТРИЦА / СУММАТОР (сцена {0}) ===", rd.Efficiency.Name);
            Console.WriteLine("  ε_p(E1) = {0:F5}, ε_p(E2) = {1:F5}; ε_T(E1) = {2:F5}, ε_T(E2) = {3:F5}; κ(E1,E2) = {4:F4}; кривая света {5}",
                              epsP1, epsP2, epsT1, epsT2, kappa, summer.LightYieldName);
            if (matrix.JointEnergies != null && matrix.JointKappa != null)
            {
                // κ на узлах сетки вокруг пары — с погрешностью и числом точек: у ТОЧЕЧНОГО источника κ ≡ 1,
                // и отклонение — либо шум замера, либо смещение оценщика.
                var sb = new StringBuilder();
                int n = matrix.JointEnergies.Length;
                for (int i = 0; i < n; i++)
                {
                    if (matrix.JointEnergies[i] < 900.0 || matrix.JointEnergies[i] > 1700.0) continue;
                    for (int j = i; j < n; j++)
                    {
                        if (matrix.JointEnergies[j] < 900.0 || matrix.JointEnergies[j] > 1700.0) continue;
                        double err = matrix.JointKappaError != null ? matrix.JointKappaError[i][j] : double.NaN;
                        sb.AppendFormat(CultureInfo.InvariantCulture, " ({0:F0},{1:F0}): {2:F4}±{3:F4};", matrix.JointEnergies[i], matrix.JointEnergies[j], matrix.JointKappa[i][j], err);
                    }
                }

                Console.WriteLine("  κ по узлам сетки (точек {0}):{1}", matrix.JointPoints, sb);
            }

            Console.WriteLine("  видимая сумма по свету {0:F2} кэВ (E1+E2 = {1:F2}, {2:+0.00;-0.00}); канал по калибровке спектра {3:F2} против канала суммы в данных {4:F2} ({5:+0.00;-0.00} кан = {6:+0.0;-0.0} кэВ)",
                              apparent, e1 + e2, apparent - (e1 + e2), cal.EnergyToChannel(apparent, raw.Length), ws.CentroidCh,
                              ws.CentroidCh - cal.EnergyToChannel(apparent, raw.Length), ws.CentroidKev - apparent);
            if (epsMeas1 > 0.0)
            {
                Console.WriteLine("  матрица/паспорт: ε_p(E1) {0:F3}, ε_p(E2) {1:F3}; произведение {2:F3}; сумм-пик при паспортной A: {3:F0} против net {4:F0} = {5:F3}",
                                  epsP1 / epsMeas1, epsP2 / epsMeas2, epsP1 * epsP2 / (epsMeas1 * epsMeas2),
                                  aPass * live * i1 / 100.0 * epsP1 * epsP2 * kappa, ws.Net, aPass * live * i1 / 100.0 * epsP1 * epsP2 * kappa / ws.Net);
            }

            FsaComponent target = null;
            foreach (FsaComponent c in library)
            {
                if (c.Kind != FsaComponentKind.Nuisance && (target == null || c.Lines.Count > target.Lines.Count))
                {
                    target = c;
                }
            }

            // (П86, 15.09.2026) Множитель угловой корреляции сумм-пика W — из
            // САМОГО сумматора (площадь ВКЛ / ВЫКЛ), а не числом: зашитое 1.0845
            // было множителем Co-60 на `G1S_point5` при неверном знаке δ
            // (П85 находка 1; с верным — 1.0825) и другой сцене не принадлежит.
            double sumAreaIso = double.NaN, sumAreaAng = double.NaN;
            foreach (int ang in new[] { 0, 1 })
            {
                FsaCascadeSummer s = FsaCascadeSummer.Create(matrix, scintillator);
                s.AngularCorrelations = ang == 1;
                s.AngularQk = qk;
                FsaCascadeSummer.Correction corr = s.For(target);
                if (corr == null)
                {
                    Console.WriteLine("  сумматор ({0}): поправок нет", ang == 1 ? "угловые ВКЛ" : "изотропно");
                    continue;
                }

                var sb = new StringBuilder();
                for (int i = 0; i < target.Lines.Count; i++)
                {
                    if (Math.Abs(corr.LineFactors[i] - 1.0) > 1e-9)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, " {0:F1}: CF {1:F4};", target.Lines[i].Energy, 1.0 / corr.LineFactors[i]);
                    }
                }

                foreach (FsaCascadeSummer.SumPeak sp in corr.SumPeaks)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, " сумм {0:F2} кэВ площадь {1:E4}/распад;", sp.Energy, sp.Area);
                }

                Console.WriteLine("  сумматор ({0}):{1} угловых пар {2}{3}", ang == 1 ? "угловые ВКЛ" : "изотропно", sb, s.AngularPairs,
                                  s.AngularPairs > 0 ? string.Format(CultureInfo.InvariantCulture, " множитель {0:F4}", s.AngularFactorMax) : "");
                if (corr.SumPeaks.Count > 0)
                {
                    if (ang == 1) sumAreaAng = corr.SumPeaks[0].Area; else sumAreaIso = corr.SumPeaks[0].Area;
                }

                if (aPass > 0.0 && corr.SumPeaks.Count > 0)
                {
                    double area = corr.SumPeaks[0].Area;
                    Console.WriteLine("    сумм-пик при паспортной A: {0:F0} отсчётов против net {1:F0} = {2:F3}; A по данным из площади: {3:F0} Бк = {4:F3} паспорта",
                                      aPass * live * area, ws.Net, aPass * live * area / ws.Net, ws.Net / (live * area), ws.Net / (live * area) / aPass);
                }
            }

            // ---- пять плеч анализатора ---------------------------------
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            var arms = new List<Arm>();
            foreach (string name in new[] { "матрица без каскада", "+CF", "+сумм-пики (изотропно)", "+угловые корреляции", "+наложения (изотропно)" })
            {
                FsaAnalyzer an = new FsaAnalyzer();
                an.ResponseMatrix = matrix;
                an.ScintillatorMaterial = scintillator;
                an.AngularQk = qk;
                an.PileUp = name.StartsWith("+наложения", StringComparison.Ordinal);
                an.CascadeSumming = name != "матрица без каскада";
                an.CascadeSumPeaks = an.CascadeSumming && name != "+CF";
                an.CascadeSumAngular = name == "+угловые корреляции";
                if (arms.Count == 0)
                {
                    FsaTuningReport.Print(an, "плечо 1");
                }

                FsaResult r = an.Analyze(es, null, rd.FwhmCalibration, library, efficiency);
                if (r == null)
                {
                    Console.Error.WriteLine("разложение не получилось: " + name);
                    return 1;
                }

                arms.Add(new Arm { Name = name, Result = r, Analyzer = an });
            }

            Console.WriteLine();
            Console.WriteLine("=== РАЗБОР: пять плеч (окна те же, что у данных, вычет подложки тот же) ===");
            Console.WriteLine("  {0,-26} {1,8} {2,9} {3,7} {4,9} {5,7} {6,9} {7,7} {8,9} {9,10} {10,9} {11,8}",
                              "плечо", "chi2/ndf", "A_fit,Бк", "A/пасп", "M1 net", "M1/N1", "M2 net", "M2/N2", "MΣ net", "подслой Σ", "MΣ/NΣ", "П49");
            foreach (Arm arm in arms)
            {
                FsaResult r = arm.Result;
                FsaComponentResult comp = null;
                foreach (FsaComponentResult c in r.Components)
                {
                    if (string.Equals(c.Name, target.Name, StringComparison.Ordinal)) { comp = c; }
                }

                double aFit = comp != null ? comp.CountRate : 0.0;
                double c1m, c2m, csm;
                double m1 = NetOf(r.Model, w1, out c1m), m2 = NetOf(r.Model, w2, out c2m), ms = NetOf(r.Model, ws, out csm);
                double sub = 0.0, subC = 0.0;
                if (comp != null && comp.SumPeakCurve != null)
                {
                    for (int i = 0; i < comp.SumPeakCurve.Length; i++)
                    {
                        sub += comp.SumPeakCurve[i];
                        subC += comp.SumPeakCurve[i] * i;
                    }
                }

                // мерка П49: окно winLo..winHi по калибровке, «данные» = измерено − модель без каскада
                double p49 = double.NaN;
                {
                    double meas = 0.0, a = 0.0, b = 0.0;
                    for (int i = r.FirstChannel; i <= r.LastChannel && i < raw.Length; i++)
                    {
                        double e = cal.ChannelToEnergy(i);
                        if (e < winLo || e > winHi) continue;
                        meas += raw[i]; a += arms[0].Result.Model[i]; b += r.Model[i];
                    }

                    if (meas != a) p49 = (b - a) / (meas - a);
                }

                Console.WriteLine("  {0,-26} {1,8:F3} {2,9:F0} {3,7:F3} {4,9:F0} {5,7:F3} {6,9:F0} {7,7:F3} {8,9:F0} {9,10:F0} {10,9:F3} {11,8:F3}",
                                  arm.Name, r.Chi2Ndf, aFit, aPass > 0 ? aFit / aPass : double.NaN, m1, m1 / w1.Net, m2, m2 / w2.Net,
                                  ms, sub, sub > 0 ? sub / ws.Net : ms / ws.Net, p49);
                Console.WriteLine("      центроиды модели − данных, кан: E1 {0:+0.00;-0.00} ({1:F2} − {2:F2}), E2 {3:+0.00;-0.00} ({4:F2} − {5:F2}), Σ {6:+0.00;-0.00} ({7:F2} − {8:F2}); усиление фита {9:F5}, ноль {10:F2} кан",
                                  c1m - w1.CentroidCh, c1m, w1.CentroidCh, c2m - w2.CentroidCh, c2m, w2.CentroidCh, csm - ws.CentroidCh, csm, ws.CentroidCh, r.Gain, r.OffsetChannels);
                if (sub > 0.0)
                {
                    double cch = subC / sub;
                    double mom2 = 0.0;
                    for (int i = 0; i < comp.SumPeakCurve.Length; i++) mom2 += comp.SumPeakCurve[i] * (i - cch) * (i - cch);
                    double sigCh = Math.Sqrt(mom2 / sub);
                    Console.WriteLine("      подслой сумм-пиков: {0:F0} отсчётов, центроид {1:F2} кан = {2:F1} кэВ (данные {3:F2} кан = {4:F1} кэВ, {5:+0.00;-0.00} кан); σ {6:F2} кан → ПШПВ {7:F1} кан = {8:F1} кэВ; в окне суммы {9:F0}",
                                      sub, cch, cal.ChannelToEnergy(cch), ws.CentroidCh, ws.CentroidKev, ws.CentroidCh - cch, sigCh, 2.3548 * sigCh,
                                      cal.ChannelToEnergy(cch + 1.1774 * sigCh) - cal.ChannelToEnergy(cch - 1.1774 * sigCh), SumOf(comp.SumPeakCurve, ws.Lo, ws.Hi));
                }

                if (arm.Analyzer.PileUp)
                {
                    foreach (FsaComponentResult c in r.Components)
                    {
                        if (!string.Equals(c.Name, FsaResult.PileUpLayerName, StringComparison.Ordinal)) continue;
                        double rate = total / live;
                        double amplitude = c.CountRate * live;
                        double tau = rate > 0.0 ? amplitude / (2.0 * rate * total) * 1e6 : 0.0;
                        Console.WriteLine("      наложения: амплитуда {0:E3}, z = {1:F1}, τ = {2:F3} мкс; образ в окне суммы {3:F0}",
                                          amplitude, c.Z, tau, SumOf(c.Curve, ws.Lo, ws.Hi));
                    }
                }
            }

            // ---- мерка П49 по окнам и определениям: что из 1.5 — окно, что — сплайн ----
            {
                FsaComponentResult comp = null;
                foreach (FsaComponentResult c in arms[2].Result.Components)
                {
                    if (string.Equals(c.Name, target.Name, StringComparison.Ordinal)) comp = c;
                }

                double[] sub = comp != null && comp.SumPeakCurve != null ? comp.SumPeakCurve : new double[raw.Length];
                double[] m1 = arms[0].Result.Model, m3 = arms[2].Result.Model;
                int nws = ws.Hi - ws.Lo;
                Console.WriteLine();
                Console.WriteLine("=== мерка П49 по окнам: данные = изм − (модель без каскада | линейная подложка); модель = (с каскадом − без | подслой сумм) ===");
                Console.WriteLine("  {0,-22} {1,9} {2,9} {3,9} {4,9} {5,9} {6,9} {7,9} {8,9}", "окно, кэВ", "изм", "без каск", "подложка", "Σ(с−без)", "подслой", "П49 (a)", "(б) подл", "(в) подсл/подл");
                foreach (double[] win in new[] { new[] { winLo, winHi }, new[] { winLo + 40.0, winHi + 40.0 }, new[] { 2400.0, 2700.0 }, new[] { ws.LoKev, ws.HiKev } })
                {
                    double meas = 0.0, a = 0.0, b = 0.0, bg = 0.0, s = 0.0;
                    for (int i = Math.Max(1, ws.Lo - 40); i < Math.Min(raw.Length, ws.Hi + 40); i++)
                    {
                        double e = cal.ChannelToEnergy(i);
                        if (e < win[0] || e > win[1]) continue;
                        meas += raw[i]; a += m1[i]; b += m3[i]; s += sub[i];
                        bg += ws.BgLeft + (ws.BgRight - ws.BgLeft) * (i - ws.Lo + 0.5) / nws;
                    }

                    Console.WriteLine("  {0,-22} {1,9:F0} {2,9:F0} {3,9:F0} {4,9:F0} {5,9:F0} {6,9:F3} {7,9:F3} {8,9:F3}",
                                      string.Format(CultureInfo.InvariantCulture, "{0:F0}–{1:F0}", win[0], win[1]), meas, a, bg, b - a, s,
                                      (b - a) / (meas - a), (b - a) / (meas - bg), s / (meas - bg));
                }
            }

            // ---- захват окна: полные пики модели (канал полного поглощения) против её же окон ----
            // Окно с линейным вычетом ловит НЕ весь пик (хвосты, изгиб подложки на комптоновском
            // крае); доля захвата снимается с МОДЕЛИ, у которой полный пик известен точно —
            // канал 0 слоя нуклида, — и той же долей восстанавливаются полные площади ДАННЫХ.
            {
                FsaStackLayer nuc = null;
                foreach (FsaStackLayer layer in arms[2].Result.BuildStackedLayers(FsaResult.DefaultMaxNamedLayers))
                {
                    if (string.Equals(layer.Name, target.Name, StringComparison.Ordinal)) nuc = layer;
                }

                FsaComponentResult comp = null;
                foreach (FsaComponentResult c in arms[2].Result.Components)
                {
                    if (string.Equals(c.Name, target.Name, StringComparison.Ordinal)) comp = c;
                }

                if (nuc != null && nuc.ChannelCurves != null && nuc.ChannelCurves[0] != null && comp != null)
                {
                    double[] full = nuc.ChannelCurves[0];
                    double mid = 0.5 * (e1 + e2);
                    int a0 = (int)cal.EnergyToChannel(e1 - 2.5 * w1.FwhmKev, raw.Length), a1 = (int)cal.EnergyToChannel(mid, raw.Length);
                    int b1 = (int)cal.EnergyToChannel(e2 + 2.5 * w2.FwhmKev, raw.Length);
                    double f1 = SumOf(full, a0, a1), f2 = SumOf(full, a1, b1);
                    double sub = 0.0;
                    foreach (double v in comp.SumPeakCurve ?? new double[0]) sub += v;
                    // сумм-пик тоже лежит в канале 0 — вычесть его из f2, если окно E2 его задевает (не задевает: 2.5 ПШПВ)
                    double m1 = NetOf(arms[2].Result.Model, w1), m2 = NetOf(arms[2].Result.Model, w2), msn = NetOf(arms[2].Result.Model, ws);
                    double c1 = m1 / f1, c2 = m2 / f2, cs = msn / sub;
                    double n1 = w1.Net / c1, n2 = w2.Net / c2, ns = ws.Net / cs;
                    Console.WriteLine();
                    Console.WriteLine("=== ЗАХВАТ ОКНА (по модели плеча 3) и ПОЛНЫЕ площади данных ===");
                    Console.WriteLine("  полный пик модели (канал 0): E1 {0:F0} [{1}..{2}), E2 {3:F0} [{2}..{4}), Σ {5:F0}; окно ловит E1 {6:F3}, E2 {7:F3}, Σ {8:F3}",
                                      f1, a0, a1, f2, b1, sub, c1, c2, cs);
                    Console.WriteLine("  данные, полные площади: N1 {0:F0}, N2 {1:F0}, NΣ {2:F0}; модель/данные по полным: E1 {3:F3}, E2 {4:F3}, Σ {5:F3}",
                                      n1, n2, ns, f1 / n1, f2 / n2, sub / ns);
                    double aFit = comp.CountRate;
                    Console.WriteLine("  ε_p модели через полный пик: E1 {0:F5} (матрица {1:F5}/CF = {2:F5}), E2 {3:F5} (матрица {4:F5}/CF = {5:F5})",
                                      f1 / (aFit * live * i1 / 100.0), epsP1, epsP1 / 1.0302, f2 / (aFit * live * i2 / 100.0), epsP2, epsP2 / 1.0316);
                    if (aPass > 0.0)
                    {
                        double eo1 = n1 / (aPass * live * i1 / 100.0), eo2 = n2 / (aPass * live * i2 / 100.0);
                        Console.WriteLine("  по паспорту (полные): ε_p(E1) набл {0:F5}, ε_p(E2) набл {1:F5}; матрица набл/паспорт: E1 {2:F3}, E2 {3:F3}",
                                          eo1, eo2, f1 / (aFit * live * i1 / 100.0) / eo1, f2 / (aFit * live * i2 / 100.0) / eo2);
                        double aSum = n1 * n2 / (ns * live);
                        double wAng = sumAreaIso > 0.0 && sumAreaAng > 0.0 ? sumAreaAng / sumAreaIso : double.NaN;
                        Console.WriteLine("  метод сумм-пика (полные): A = {0:F0} Бк = {1:F3} паспорта; ×W {2:F4} (сумматор ВКЛ/ВЫКЛ) → {3:F3}; Бринкман (+T/t) {4:F3}",
                                          aSum, aSum / aPass, wAng, aSum * wAng / aPass, (aSum + total / live) / aPass);
                        Console.WriteLine("  A_fit/паспорт {0:F3}; NΣ/N1 = {1:F5} = ε_p(E2)·W·(1+pu)/(1−L1); модели MΣ/M1 = {2:F5} = ε_p(E2)·κ/(1−L1)",
                                          aFit / aPass, ns / n1, sub / f1);
                    }
                }
            }

            // ---- чей континуум под сумм-пиком: слои и каналы исхода ----
            Console.WriteLine();
            Console.WriteLine("=== кто стоит на участке: сумма по полосе слева [{0}..{1}) кан / в окне суммы [{2}..{3}) / справа [{4}..{5}) ===",
                              ws.Lo - ws.BwSave, ws.Lo, ws.Lo, ws.Hi, ws.Hi, ws.Hi + ws.BwSave);
            Console.WriteLine("  измерено: {0:F0} / {1:F0} / {2:F0}", SumOf(ToDouble(raw), ws.Lo - ws.BwSave, ws.Lo), SumOf(ToDouble(raw), ws.Lo, ws.Hi), SumOf(ToDouble(raw), ws.Hi, ws.Hi + ws.BwSave));
            foreach (Arm arm in arms)
            {
                Console.WriteLine("  плечо «{0}»: модель {1:F0} / {2:F0} / {3:F0}", arm.Name,
                                  SumOf(arm.Result.Model, ws.Lo - ws.BwSave, ws.Lo), SumOf(arm.Result.Model, ws.Lo, ws.Hi), SumOf(arm.Result.Model, ws.Hi, ws.Hi + ws.BwSave));
                foreach (FsaStackLayer layer in arm.Result.BuildStackedLayers(FsaResult.DefaultMaxNamedLayers))
                {
                    if (layer.Curve == null) continue;
                    var sb = new StringBuilder();
                    sb.AppendFormat(CultureInfo.InvariantCulture, "      слой {0,-14} {1,8:F1} / {2,8:F1} / {3,8:F1}", layer.Name,
                                    SumOf(layer.Curve, ws.Lo - ws.BwSave, ws.Lo), SumOf(layer.Curve, ws.Lo, ws.Hi), SumOf(layer.Curve, ws.Hi, ws.Hi + ws.BwSave));
                    if (layer.ContinuumCurve != null)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "; подложка слоя {0:F1} / {1:F1} / {2:F1}",
                                        SumOf(layer.ContinuumCurve, ws.Lo - ws.BwSave, ws.Lo), SumOf(layer.ContinuumCurve, ws.Lo, ws.Hi), SumOf(layer.ContinuumCurve, ws.Hi, ws.Hi + ws.BwSave));
                    }

                    if (layer.SumPeakCurve != null)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "; подслой сумм {0:F1} / {1:F1} / {2:F1}",
                                        SumOf(layer.SumPeakCurve, ws.Lo - ws.BwSave, ws.Lo), SumOf(layer.SumPeakCurve, ws.Lo, ws.Hi), SumOf(layer.SumPeakCurve, ws.Hi, ws.Hi + ws.BwSave));
                    }

                    if (layer.ChannelCurves != null)
                    {
                        for (int k = 0; k < layer.ChannelCurves.Length; k++)
                        {
                            if (layer.ChannelCurves[k] == null) continue;
                            double a = SumOf(layer.ChannelCurves[k], ws.Lo - ws.BwSave, ws.Lo), b = SumOf(layer.ChannelCurves[k], ws.Lo, ws.Hi), c = SumOf(layer.ChannelCurves[k], ws.Hi, ws.Hi + ws.BwSave);
                            if (a + b + c > 0.05)
                            {
                                sb.AppendFormat(CultureInfo.InvariantCulture, "; канал {0} {1:F1} / {2:F1} / {3:F1}", k, a, b, c);
                            }
                        }
                    }

                    Console.WriteLine(sb.ToString());
                }
            }

            // ---- дамп участка по плечам ------------------------------
            Console.WriteLine();
            Console.WriteLine("=== участок {0:F0}–{1:F0} кэВ по каналам: измерено | плечи 1..5 | подслой Σ плеча 3 ===", ws.LoKev, ws.HiKev);
            FsaComponentResult comp3 = null;
            foreach (FsaComponentResult c in arms[2].Result.Components)
            {
                if (string.Equals(c.Name, target.Name, StringComparison.Ordinal)) comp3 = c;
            }

            for (int i = ws.Lo - 8; i < ws.Hi + 8 && i < raw.Length; i++)
            {
                var sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "  {0,5} {1,8:F1} {2,8}", i, cal.ChannelToEnergy(i), raw[i]);
                foreach (Arm arm in arms)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, " {0,9:F1}", i < arm.Result.Model.Length ? arm.Result.Model[i] : double.NaN);
                }

                sb.AppendFormat(CultureInfo.InvariantCulture, " {0,9:F1}", comp3 != null && comp3.SumPeakCurve != null && i < comp3.SumPeakCurve.Length ? comp3.SumPeakCurve[i] : 0.0);
                Console.WriteLine(sb.ToString());
            }

            int raised = NuclideDefinitionManager.RaiseCount;
            Console.WriteLine("NuclideDefinitionManager за прогон: обращений {0}", raised);
            return raised > 0 ? 12 : 0;
        }

        sealed class Arm
        {
            public string Name;
            public FsaResult Result;
            public FsaAnalyzer Analyzer;
        }

        sealed class Window
        {
            public string Name;
            public int Lo, Hi, Max, BwSave;
            public double LoKev, HiKev;
            public double BgLeft, BgRight, Gross, Net, CentroidCh, CentroidKev, FwhmCh, FwhmKev;
        }

        /// <summary>
        /// Окно ±half кэВ вокруг максимума в ±half кэВ от e; подложка — среднее по
        /// полосе bg кэВ с каждой стороны, линейно между ними. Та же процедура
        /// применяется и к модели (<see cref="NetOf"/>), чтобы вычет был одинаков.
        /// </summary>
        static Window Measure(int[] raw, EnergyCalibration cal, double e, double half, double bg, string name)
        {
            int c0 = (int)Math.Round(cal.EnergyToChannel(e, raw.Length));
            int wch = (int)Math.Round(cal.EnergyToChannel(e + half, raw.Length) - c0);
            int lo = Math.Max(1, c0 - wch), hi = Math.Min(raw.Length - 1, c0 + wch + 1);
            int imax = lo;
            for (int i = lo; i < hi; i++) if (raw[i] > raw[imax]) imax = i;
            lo = Math.Max(1, imax - wch); hi = Math.Min(raw.Length - 1, imax + wch + 1);
            int bw = Math.Max(2, (int)Math.Round(cal.EnergyToChannel(e + bg, raw.Length) - cal.EnergyToChannel(e, raw.Length)));
            double[] d = new double[raw.Length];
            for (int i = 0; i < raw.Length; i++) d[i] = raw[i];
            Window w = new Window { Name = name, Lo = lo, Hi = hi, Max = imax, LoKev = cal.ChannelToEnergy(lo), HiKev = cal.ChannelToEnergy(hi) };
            Fill(d, w, bw);
            // ПШПВ по полувысоте над подложкой
            int n = hi - lo;
            double bmax = w.BgLeft + (w.BgRight - w.BgLeft) * (imax - lo + 0.5) / n;
            double h = raw[imax] - bmax;
            double left = Cross(d, w, imax, -1, h / 2.0), right = Cross(d, w, imax, +1, h / 2.0);
            w.FwhmCh = right - left;
            w.FwhmKev = cal.ChannelToEnergy(w.CentroidCh + w.FwhmCh / 2.0) - cal.ChannelToEnergy(w.CentroidCh - w.FwhmCh / 2.0);
            w.CentroidKev = cal.ChannelToEnergy(w.CentroidCh);
            w.BwSave = bw;
            return w;
        }

        static void Fill(double[] d, Window w, int bw)
        {
            // (П102) Окно у нижнего/верхнего края шкалы: полоса подложки
            // обрезается краем, а не выходит за массив (пара с K-рентгеном 40 кэВ
            // на G1S16_Eu152_P5 ложилась ниже первого канала и роняла пробу
            // IndexOutOfRange до печати ε_p матрицы).
            double bl = 0.0, br = 0.0;
            int nl = 0, nr = 0;
            for (int i = Math.Max(0, w.Lo - bw); i < w.Lo; i++) { bl += d[i]; nl++; }
            for (int i = w.Hi; i < Math.Min(d.Length, w.Hi + bw); i++) { br += d[i]; nr++; }
            bl = nl > 0 ? bl / nl : 0.0; br = nr > 0 ? br / nr : 0.0;
            int n = w.Hi - w.Lo;
            double net = 0.0, cen = 0.0, gross = 0.0;
            for (int k = 0; k < n; k++)
            {
                int i = w.Lo + k;
                double b = bl + (br - bl) * (k + 0.5) / n;
                gross += d[i];
                net += d[i] - b;
                cen += (d[i] - b) * i;
            }

            w.BgLeft = bl; w.BgRight = br; w.Gross = gross; w.Net = net; w.CentroidCh = net != 0.0 ? cen / net : double.NaN;
        }

        static double Cross(double[] d, Window w, int imax, int step, double half)
        {
            int n = w.Hi - w.Lo;
            int i = imax;
            while (i > 0 && i < d.Length - 1)
            {
                double b = d.Length > 0 ? w.BgLeft + (w.BgRight - w.BgLeft) * (i - w.Lo + 0.5) / n : 0.0;
                if (d[i] - b < half)
                {
                    int j = i - step;
                    double bj = w.BgLeft + (w.BgRight - w.BgLeft) * (j - w.Lo + 0.5) / n;
                    double v1 = d[j] - bj, v2 = d[i] - b;
                    return j + (v1 - half) / (v1 - v2) * step;
                }

                i += step;
            }

            return double.NaN;
        }

        /// <summary>Чистая площадь МОДЕЛИ в окне данных тем же линейным вычетом по тем же полосам.</summary>
        static double NetOf(double[] model, Window w)
        {
            double c;
            return NetOf(model, w, out c);
        }

        /// <summary>То же, и центроид модели в окне (по чистому счёту).</summary>
        static double NetOf(double[] model, Window w, out double centroidCh)
        {
            centroidCh = double.NaN;
            if (model == null) return double.NaN;
            double[] d = new double[model.Length];
            Array.Copy(model, d, model.Length);
            Window m = new Window { Lo = w.Lo, Hi = w.Hi };
            Fill(d, m, w.BwSave);
            centroidCh = m.CentroidCh;
            return m.Net;
        }

        static double[] ToDouble(int[] v)
        {
            double[] d = new double[v.Length];
            for (int i = 0; i < v.Length; i++) d[i] = v[i];
            return d;
        }

        static double SumOf(double[] v, int lo, int hi)
        {
            double s = 0.0;
            for (int i = lo; i < hi && i < v.Length; i++) s += v[i];
            return s;
        }

        static List<string> NucidsOf(List<string> labels)
        {
            var nucids = new List<string>();
            foreach (string label in labels)
            {
                int dash = label.IndexOf('-');
                nucids.Add(dash < 0 ? label.ToUpperInvariant() : label.Substring(dash + 1) + label.Substring(0, dash).ToUpperInvariant());
            }

            return nucids;
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            string deviceNote = ProbeDeviceConfig.Attach(rd);
            Console.WriteLine("прибор: {0}", deviceNote);
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null)
                {
                    rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
                }
            }

            return rd;
        }
    }
}
