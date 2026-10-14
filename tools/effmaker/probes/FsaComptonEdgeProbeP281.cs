using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace FsaComptonEdgeProbeP281
{
    /// <summary>
    /// П281 (14.10.2026; решение Amber 14.10.2026 вопросником, дословно: «Молчать у краёв
    /// комптона (Рекомендую)»): ПИК ВНЕ СОСТАВА У КРАЯ КОМПТОНА В РАЗБОРЕ БЕЗ МАТРИЦЫ
    /// (`AMBER210`, <c>FsaAnalyzer.FindUncoveredPeaks</c>).
    ///
    /// Без матрицы образ линии — голый пик, комптоновский континуум описывает подложка
    /// сплайна, и избыток у уступа комптона сильной линии состава получал имя чужой линии
    /// библиотеки рядом (П251: `G1S16_K40_Mar` без матрицы — «1167.4 кэВ: пик вне состава,
    /// рядом Cs-134 1168.0, Co-60 1173.0, Bi-214 1155.2»).
    ///
    /// Три раздела:
    ///   (1) спектры (`--spectrum=`, можно несколько): разбор сеансом приложения без
    ///       матрицы и (если склад даёт годную матрицу, `--store=`) с матрицей; печать
    ///       пиков вне состава и ревизии отбора; у разбора без матрицы — края комптона
    ///       сильных линий состава (след анализатора `LastComptonEdges`, отражением: на
    ///       сборке до правки его нет) и ПЕРЕПИСЬ всех серий избытка у каждого края —
    ///       смещение центра серии от края в ПШПВ края, а также положение уступа В ДАННЫХ
    ///       (точка самого крутого спада сглаженных данных и максимум уступа под ней);
    ///   (2) `--dump=&lt;файл&gt;` — то, что правка обязана сохранить побитово (пики вне
    ///       состава и ревизия отбора, χ²/ndf, компоненты) формой "R", по разборам;
    ///   (3) `--synthetic` — положительный контроль правила отражением: синтетический
    ///       избыток на краю комптона линии состава с кандидатом библиотеки рядом.
    ///
    ///     FsaComptonEdgeProbeP281 --spectrum=a.xml [--spectrum=b.xml …]
    ///                             [--store=tools\CORPUS\corpus\geometries\response]
    ///                             [--arms=none,matrix] [--dump=out.txt] [--synthetic]
    ///
    /// Коды: 0 — посчитано (и синтетика по ожиданию); 1 — синтетика не по ожиданию;
    /// 2 — ключи/файлы; 3 — разбор не завершился.
    /// </summary>
    static class Program
    {
        static FsaAnalyzer lastAnalyzer;
        static string storeDir;
        static StreamWriter dump;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            var spectra = new List<string>();
            bool armNone = true, armMatrix = true, synthetic = false;
            string dumpPath = null;
            int timeoutS = 180;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectra.Add(a.Substring(11));
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) storeDir = a.Substring(8);
                else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dumpPath = a.Substring(7);
                else if (a.StartsWith("--timeout=", StringComparison.Ordinal)) timeoutS = int.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--arms=", StringComparison.Ordinal))
                {
                    string arms = a.Substring(7);
                    armNone = arms.Contains("none");
                    armMatrix = arms.Contains("matrix");
                }
                else if (a == "--synthetic") synthetic = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            int code = 0;
            if (synthetic)
            {
                code = Synthetic() ? 0 : 1;
            }

            if (spectra.Count == 0)
            {
                return code;
            }

            if (dumpPath != null)
            {
                dump = new StreamWriter(dumpPath, false, new UTF8Encoding(false));
            }

            try
            {
                FsaAnalysisSession.ProbeAnalyzerHook = analyzer => lastAnalyzer = analyzer;
                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
                foreach (string path in spectra)
                {
                    if (!File.Exists(path))
                    {
                        Console.Error.WriteLine("нет файла: " + path);
                        code = Math.Max(code, 2);
                        continue;
                    }

                    if (armNone)
                    {
                        int rc = RunArm(path, nuclides, false, timeoutS);
                        code = Math.Max(code, rc);
                    }

                    if (armMatrix)
                    {
                        int rc = RunArm(path, nuclides, true, timeoutS);
                        code = Math.Max(code, rc);
                    }
                }
            }
            finally
            {
                if (dump != null) dump.Dispose();
            }

            return code;
        }

        static string R(double x)
        {
            return x.ToString("R", CultureInfo.InvariantCulture);
        }

        static string F(double x, string f)
        {
            return double.IsNaN(x) ? "—" : x.ToString(f, CultureInfo.InvariantCulture);
        }

        static void Dump(string line)
        {
            if (dump != null) dump.WriteLine(line);
        }

        static int RunArm(string path, NuclideDefinitionManager nuclides, bool withMatrix, int timeoutS)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string arm = withMatrix ? "с матрицей" : "без матрицы";
            Console.WriteLine();
            Console.WriteLine("=== {0} — {1} ===", name, arm);
            ResultData rd = Load(path, nuclides);
            EfficiencyConfigData eff = rd.Efficiency;
            if (withMatrix)
            {
                if (eff == null || !eff.HasGeometry)
                {
                    Console.WriteLine("  разбора с матрицей нет: у спектра нет кривой с геометрией");
                    return 0;
                }

                string from = AttachMatrix(eff);
                if (from == null)
                {
                    return 0;
                }

                Console.WriteLine("  матрица: {0}", from);
            }
            else if (eff != null)
            {
                eff.UseResponseMatrix = false;
            }

            lastAnalyzer = null;
            var session = new FsaAnalysisSession();
            var done = new ManualResetEvent(false);
            session.Completed += (s, e) => done.Set();
            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);
            session.EnsureUpToDate(rd, rd.BackgroundEnergySpectrum != null, options);
            bool finished = done.WaitOne(TimeSpan.FromSeconds(timeoutS));
            FsaResult result = session.Result;
            if (!finished || result == null)
            {
                Console.WriteLine("  разбор не завершился: {0}", session.Status);
                return 3;
            }

            if (withMatrix != result.ResponseMatrixUsed)
            {
                Console.WriteLine("  ⚠ матрица применена {0} — ждали {1}", result.ResponseMatrixUsed, withMatrix);
            }

            Console.WriteLine("  матрица {0}; χ²/ndf {1}; компонентов {2}", result.ResponseMatrixUsed ? "применена" : "нет",
                              F(result.Chi2Ndf, "F2"), result.Components.Count);
            string tag = name + "|" + (result.ResponseMatrixUsed ? "matrix" : "none");
            Dump(tag + "|chi2ndf|" + R(result.Chi2Ndf) + "|" + R(result.Chi2NdfPoisson));
            foreach (FsaComponentResult c in result.Components)
            {
                Console.WriteLine("    компонент {0,-22} {1,-8} пик.отсч {2,12} z {3,8}", c.Name, c.Kind, F(c.PeakCounts, "F0"), F(c.Z, "F1"));
                Dump(tag + "|component|" + c.Name + "|" + c.Kind + "|" + R(c.CountRate) + "|" + R(c.PeakCounts) + "|" + R(c.Z));
            }

            Console.WriteLine("  пиков вне состава: {0}", result.UncoveredPeaks.Count);
            foreach (FsaUncoveredPeak u in result.UncoveredPeaks)
            {
                var names = new List<string>();
                foreach (FsaUncoveredCandidate c in u.Candidates) names.Add(c.Nuclide + " " + F(c.EnergyKev, "F1"));
                Console.WriteLine("    ПИК {0,8} кэВ  ширина {1,6}  избыток {2,6} %  z {3,7}  библиотека: {4}",
                                  F(u.EnergyKev, "F1"), F(u.WidthKev, "F1"), F(100.0 * u.ExcessShare, "F1"), F(u.Z, "F1"),
                                  string.Join(", ", names));
                Dump(tag + "|peak|" + R(u.EnergyKev) + "|" + R(u.WidthKev) + "|" + R(u.ExcessCounts) + "|" + R(u.ExcessShare)
                     + "|" + R(u.Z) + "|" + string.Join(";", names));
            }

            PropertyInfo edgeOfRejection = typeof(FsaUncoveredRejection).GetProperty("ComptonEdgeKev");
            Console.WriteLine("  ревизия отбора: {0}", result.UncoveredRejections.Count);
            foreach (FsaUncoveredRejection j in result.UncoveredRejections)
            {
                string edge = edgeOfRejection != null ? F((double)edgeOfRejection.GetValue(j), "F1") : "—";
                Console.WriteLine("    {0,8} кэВ  ширина {1,6}  избыток {2,6} %  z {3,7}  {4,-12} линия состава {5,7}  край {6,7}  кандидат {7}",
                                  F(j.EnergyKev, "F1"), F(j.WidthKev, "F1"), F(100.0 * j.ExcessShare, "F1"), F(j.Z, "F1"), j.Reason,
                                  F(j.NearestCoveredKev, "F1"), edge,
                                  j.NearestCandidate != null ? j.NearestCandidate.Nuclide + " " + F(j.NearestCandidate.EnergyKev, "F1") : "—");
                // Дамп «прежнего поведения»: причина и все поля, кроме нового поля края.
                Dump(tag + "|rejected|" + j.Reason + "|" + R(j.EnergyKev) + "|" + R(j.WidthKev) + "|" + R(j.ExcessShare) + "|" + R(j.Z)
                     + "|" + R(j.NearestCoveredKev) + "|"
                     + (j.NearestCandidate != null ? j.NearestCandidate.Nuclide + " " + R(j.NearestCandidate.EnergyKev) : ""));
            }

            if (!result.ResponseMatrixUsed)
            {
                Census(rd, result);
            }

            return 0;
        }

        /// <summary>
        /// Перепись серий избытка у краёв комптона сильных линий состава: края — из следа
        /// анализатора (`LastComptonEdges`: [линия, доля от сильнейшей, край, ПШПВ края]); серии —
        /// тем же правилом, что у разбора (`r/√D ≥ 2`, разрыв до двух каналов), по следу фита.
        /// </summary>
        static void Census(ResultData rd, FsaResult result)
        {
            FsaAnalyzer a = lastAnalyzer;
            PropertyInfo trace = typeof(FsaAnalyzer).GetProperty("LastComptonEdges");
            if (a == null || trace == null)
            {
                Console.WriteLine("  след краёв комптона: {0}", a == null ? "крючок не ставился" : "в этой сборке его нет (до правки)");
                return;
            }

            var edges = trace.GetValue(a) as List<double[]>;
            if (edges == null || edges.Count == 0)
            {
                Console.WriteLine("  след краёв комптона: пуст");
                return;
            }

            EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
            double[] r = a.LastFitResidual, v = a.LastFitVariance, m = a.LastFitModel, sub = a.LastFitSubtracted;
            int lo = Math.Max(a.LastFitFirstChannel, result.ResidualFloorChannel);
            int hi = Math.Min(a.LastFitLastChannel, Math.Min(r.Length, Math.Min(v.Length, m.Length)) - 1);
            List<int[]> runs = Runs(r, v, lo, hi);
            int n = Math.Min(r.Length, m.Length);
            double[] y = new double[n];
            for (int i = 0; i < n; i++) y[i] = m[i] + r[i];

            Console.WriteLine("  края комптона сильных линий состава (след анализатора): {0}", edges.Count);
            foreach (double[] e in edges)
            {
                double line = e[0], share = e[1], edge = e[2], fwhm = e[3];
                Console.WriteLine("    линия {0,8} кэВ  доля {1,6}  край {2,8} кэВ  ПШПВ края {3,6} кэВ",
                                  F(line, "F1"), F(share, "F3"), F(edge, "F1"), F(fwhm, "F1"));
                if (!(fwhm > 0.0))
                {
                    continue;
                }

                // Уступ в ДАННЫХ: сглаживание ящиком ±ПШПВ/6, самый крутой спад в [Ec − 2, Ec + 1] ПШПВ,
                // максимум уступа — первый локальный максимум ниже спада.
                double chEdge = cal.EnergyToChannel(edge, n);
                double chPerKev = Math.Abs(cal.EnergyToChannel(edge + 1.0, n) - chEdge);
                int half = Math.Max(1, (int)Math.Round(fwhm * chPerKev / 6.0));
                int c0 = Math.Max(lo + half + 1, (int)Math.Floor(cal.EnergyToChannel(edge - 2.0 * fwhm, n)));
                int c1 = Math.Min(hi - half - 1, (int)Math.Ceiling(cal.EnergyToChannel(edge + 1.0 * fwhm, n)));
                Func<int, double> ys = i =>
                {
                    double s = 0.0;
                    for (int k = i - half; k <= i + half; k++) s += y[k];
                    return s / (2 * half + 1);
                };
                int steep = -1;
                double steepest = 0.0;
                for (int i = c0; i <= c1; i++)
                {
                    double d = ys(i + 1) - ys(i - 1);
                    if (d < steepest)
                    {
                        steepest = d;
                        steep = i;
                    }
                }

                if (steep > 0)
                {
                    int top = steep;
                    int floor = Math.Max(lo + half + 1, (int)Math.Floor(cal.EnergyToChannel(edge - 3.0 * fwhm, n)));
                    while (top - 1 >= floor && ys(top - 1) > ys(top)) top--;
                    double eSteep = cal.ChannelToEnergy(steep), eTop = cal.ChannelToEnergy(top);
                    Console.WriteLine("      данные: крутой спад {0} кэВ ({1} ПШПВ), максимум уступа {2} кэВ ({3} ПШПВ){4}",
                                      F(eSteep, "F1"), F((eSteep - edge) / fwhm, "+0.00;-0.00"),
                                      F(eTop, "F1"), F((eTop - edge) / fwhm, "+0.00;-0.00"),
                                      top <= floor ? " — максимума нет до Ec − 3 ПШПВ (спад монотонный)" : "");
                }

                foreach (int[] run in runs)
                {
                    double excess = 0.0, variance = 0.0, expected = 0.0, moment = 0.0;
                    for (int i = run[0]; i <= run[1]; i++)
                    {
                        if (!(v[i] > 0.0)) continue;
                        double ri = Math.Max(r[i], 0.0);
                        excess += ri;
                        variance += v[i];
                        moment += ri * cal.ChannelToEnergy(i);
                        double mu = m[i] + (sub != null && i < sub.Length ? sub[i] : 0.0);
                        if (mu > 0.0) expected += mu;
                    }

                    if (!(excess > 0.0) || !(variance > 0.0)) continue;
                    double centre = moment / excess;
                    double offset = (centre - edge) / fwhm;
                    if (offset < -3.0 || offset > 1.5) continue;
                    double z = excess / Math.Sqrt(variance);
                    double share2 = expected > 0.0 ? excess / expected : double.NaN;
                    double width = Math.Abs(cal.ChannelToEnergy(run[1] + 1) - cal.ChannelToEnergy(run[0]));
                    Console.WriteLine("      серия {0,8} кэВ  смещение {1,6} ПШПВ  ширина {2,5} ПШПВ  избыток {3,6} %  z {4,7}",
                                      F(centre, "F1"), F(offset, "+0.00;-0.00"), F(width / fwhm, "F2"),
                                      F(100.0 * share2, "F1"), F(z, "F1"));
                }
            }
        }

        static List<int[]> Runs(double[] r, double[] v, int lo, int hi)
        {
            var runs = new List<int[]>();
            int start = -1, last = -1;
            for (int i = lo; i <= hi; i++)
            {
                if (!(v[i] > 0.0) || r[i] / Math.Sqrt(v[i]) < 2.0) continue;
                if (start >= 0 && i - last > 3)
                {
                    runs.Add(new[] { start, last });
                    start = -1;
                }

                if (start < 0) start = i;
                last = i;
            }

            if (start >= 0) runs.Add(new[] { start, last });
            return runs;
        }

        /// <summary>
        /// Положительный контроль правила отражением (по образцу `FsaUncoveredChainProbeP255`):
        /// шкала 1 кэВ/кан, ПШПВ √(3·кан) (61 кэВ на 1243); в составе — одиночка «Kk-1461» с одной линией 1460.8;
        /// модель — фон 1000 отсчётов и пик линии; избыток — гаусс на заданной энергии; в
        /// библиотеке приложения — приманка «Xx-… (проба)» на энергии избытка; край E_c = 1243.3,
        /// ПШПВ на крае 61. Плечи:
        ///   E — избыток под краем (1195, −0.8 ПШПВ, как уступ в замере), без матрицы →
        ///       `ComptonEdge` (до правки — пик Xx);
        ///   M — то же, след фита «по матрице» → пик Xx (с матрицей правило молчит);
        ///   F — избыток вдали от края (1000, −4 ПШПВ), без матрицы → пик Xx (лишнего не глушит);
        ///   W — линия состава слабая (её пика в модели нет) → пик Xx (край только у сильных линий);
        ///   A — избыток НАД краем (1262, +0.3 ПШПВ) → пик Xx (окно выше края не заходит).
        /// </summary>
        static bool Synthetic()
        {
            MethodInfo find = typeof(FsaAnalyzer).GetMethod("FindUncoveredPeaks", BindingFlags.Instance | BindingFlags.NonPublic);
            Type fitType = typeof(FsaAnalyzer).GetNestedType("FitResult", BindingFlags.NonPublic);
            if (find == null || fitType == null)
            {
                Console.WriteLine("синтетика: метод или тип следа фита не найден");
                return false;
            }

            Console.WriteLine("=== синтетика (отражением) ===");
            string e = SyntheticArm(find, fitType, "E", 1195.0, false, true);
            string mm = SyntheticArm(find, fitType, "M", 1195.0, true, true);
            string f = SyntheticArm(find, fitType, "F", 1000.0, false, true);
            string w = SyntheticArm(find, fitType, "W", 1195.0, false, false);
            string up = SyntheticArm(find, fitType, "A", 1262.0, false, true);
            bool ok = e == "ComptonEdge" && mm == "peak:Xx-1195 (проба)" && f == "peak:Xx-1000 (проба)"
                      && w == "peak:Xx-1195 (проба)" && up == "peak:Xx-1262 (проба)";
            Console.WriteLine(ok ? "СИНТЕТИКА: ВСЕ ПЯТЬ ПЛЕЧ ПО ОЖИДАНИЮ"
                                 : "СИНТЕТИКА: РАСХОЖДЕНИЕ (ждали E ComptonEdge, M/F/W/A — пик Xx)");
            return ok;
        }

        static string SyntheticArm(MethodInfo find, Type fitType, string arm, double at, bool fromMatrix, bool strongLine)
        {
            const int channels = 3000;
            const double line = 1460.8;
            var calibration = new PolynomialEnergyCalibration();
            var fwhmCalibration = new SimpleSqrtFwhmCalibration { Coefficients = new[] { 0.0, 3.0 } };
            double sAt = fwhmCalibration.ChannelToFwhm(at) / 2.3548, sLine = fwhmCalibration.ChannelToFwhm(line) / 2.3548;
            double[] residual = new double[channels], variance = new double[channels], model = new double[channels];
            double[] curve = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                double g = Math.Exp(-0.5 * (i - at) * (i - at) / (sAt * sAt));
                curve[i] = strongLine ? 50000.0 * Math.Exp(-0.5 * (i - line) * (i - line) / (sLine * sLine)) : 0.0;
                model[i] = 1000.0 + curve[i];
                variance[i] = model[i];
                residual[i] = 300.0 * g;
            }

            object fit = Activator.CreateInstance(fitType, true);
            fitType.GetField("Residual").SetValue(fit, residual);
            fitType.GetField("Variance").SetValue(fit, variance);
            fitType.GetField("Model").SetValue(fit, model);
            fitType.GetField("FromResponseMatrix").SetValue(fit, fromMatrix);

            var analyzer = new FsaAnalyzer { EscapeAndAnnihilation = false };
            string bait = "Xx-" + ((int)Math.Round(at)).ToString(CultureInfo.InvariantCulture) + " (проба)";
            analyzer.LibraryLines = new List<FsaLine> { new FsaLine(bait, at, 0.0) };
            var component = new FsaComponent("Kk-1461", FsaComponentKind.Single);
            component.Lines.Add(new FsaLine("Kk-1461", line, 10.7));
            var library = new List<FsaComponent> { component };
            var result = new FsaResult { ResidualFloorChannel = 0 };
            result.Components.Add(new FsaComponentResult
            {
                Name = "Kk-1461", Kind = FsaComponentKind.Single, CountRate = 1.0, Curve = curve
            });
            // Вторая линия состава (сильнейшая), чтобы у плеча W линия 1460.8 была слабой
            if (!strongLine)
            {
                var other = new FsaComponent("Oo-300", FsaComponentKind.Single);
                other.Lines.Add(new FsaLine("Oo-300", 300.0, 90.0));
                library.Add(other);
                double s300 = fwhmCalibration.ChannelToFwhm(300.0) / 2.3548;
                double[] c300 = new double[channels];
                for (int i = 0; i < channels; i++)
                {
                    c300[i] = 50000.0 * Math.Exp(-0.5 * (i - 300.0) * (i - 300.0) / (s300 * s300));
                    model[i] += c300[i];
                    variance[i] = model[i];
                }

                result.Components.Add(new FsaComponentResult { Name = "Oo-300", Kind = FsaComponentKind.Single, CountRate = 1.0, Curve = c300 });
            }

            find.Invoke(analyzer, new object[] { result, fit, library, calibration, fwhmCalibration, 1, channels - 2, channels });
            string verdict = "нет серии";
            foreach (FsaUncoveredRejection j in result.UncoveredRejections)
            {
                if (Math.Abs(j.EnergyKev - at) > 30.0) continue;
                verdict = j.Reason.ToString();
                Console.WriteLine("  {0}: отвергнута {1} кэВ — {2}", arm, F(j.EnergyKev, "F1"), j.Reason);
            }

            foreach (FsaUncoveredPeak p in result.UncoveredPeaks)
            {
                if (Math.Abs(p.EnergyKev - at) > 30.0) continue;
                verdict = "peak:" + (p.Candidates.Count > 0 ? p.Candidates[0].Nuclide : "—");
                Console.WriteLine("  {0}: ПИК ВНЕ СОСТАВА {1} кэВ, z {2}, библиотека {3}", arm, F(p.EnergyKev, "F1"), F(p.Z, "F1"),
                                  p.Candidates.Count > 0 ? p.Candidates[0].Nuclide : "—");
            }

            Console.WriteLine("  {0}: {1}", arm, verdict);
            return verdict;
        }

        /// <summary>Матрица кривой: склад приложения, иначе `--store` в память (как у P195, `T269`).</summary>
        static string AttachMatrix(EfficiencyConfigData efficiency)
        {
            MatrixRefusal refusal;
            int format;
            ResponseMatrixSource source;
            string spectrumRefusal;
            ResponseMatrix own = ResponseMatrixStore.Resolve(efficiency, out refusal, out format, out source, out spectrumRefusal);
            if (own != null && own.IsValidFor(efficiency.Geometry))
            {
                return "склад приложения (" + source + ")";
            }

            if (string.IsNullOrEmpty(storeDir))
            {
                Console.WriteLine("  разбора с матрицей нет: в складе приложения матрицы нет, --store не дан");
                return null;
            }

            string file = Path.Combine(storeDir, efficiency.Guid + ".rmx");
            if (!File.Exists(file))
            {
                Console.WriteLine("  разбора с матрицей нет: нет {0}", file);
                return null;
            }

            ResponseMatrix matrix = ResponseMatrix.Load(file);
            if (matrix == null || !matrix.IsValidFor(efficiency.Geometry))
            {
                Console.WriteLine("  разбора с матрицей нет: матрица {0} {1}", file, matrix == null ? "не читается" : "негодна для геометрии");
                return null;
            }

            efficiency.EmbeddedMatrix = EmbeddedResponseMatrix.FromFile(efficiency.Guid, file, matrix);
            return "--store " + Path.GetFileName(file);
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
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

            Console.WriteLine("  прибор: {0}", ProbeDeviceConfig.Attach(rd));
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
                }

                if (cfg.FwhmCalibration != null) rd.FwhmCalibration = cfg.FwhmCalibration.Clone();
            }

            if (rd.SampleInfo == null) rd.SampleInfo = new SampleInfoData();
            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None, nuclides.ActiveSet, nuclides.NuclideDefinitions);
            return rd;
        }
    }
}
