using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace SumPeakLightProbe
{
    /// <summary>
    /// (`S167`, полоса П18-FSA-замеры 12.09.2026) ГДЕ СТОИТ СУММ-ПИК У МОДЕЛИ И
    /// ГДЕ — У ДАННЫХ, кэВ, обеими кривыми света.
    ///
    /// Вопрос строки: сумм-пик каскада ставится по видимой энергии суммы
    /// (`FsaCascadeSummer.ApparentSum`), и кривых света для этого в дереве ДВЕ
    /// (полоса П10): электронная из `matdb` (её берёт `ApparentSum`) и фотонная
    /// `FsaLightScale` (её берут привязка шкалы и образ наложений). Какая ставит
    /// сумм-пик НА ДАННЫЕ — вопрос замера, и это он.
    ///
    /// КАК МЕРИТСЯ. Разбор идёт целиком, как в корпусной пробе (те же настройки,
    /// та же библиотека по манифесту, та же матрица, привязка шкалы ВКЛ). Из
    /// результата берутся три кривые по каналам: измерение за вычетом фона
    /// (`FsaResult.FitSpectrum`, `A284`), модель целиком (`Model`) и подслой
    /// сумм-пиков (Σ `SumPeakCurve` по компонентам). В окне вокруг каждого
    /// сумм-пика ±<c>--window</c>·ПШПВ считаются:
    ///
    ///   * `c_model` — центр тяжести подслоя сумм-пиков S;
    ///   * `c_data`  — центр тяжести ОСТАТКА Q = D − (M − S), то есть данных за
    ///     вычетом всего, что модель ставит в окно КРОМЕ сумм-пика (комптон
    ///     чужих линий, подложка, фон, сумм-континуум); положительная часть Q;
    ///   * максимумы S и Q после сглаживания на четверть ПШПВ;
    ///   * площади A_S и A_Q и их отношение — недобор сумм-пика (`S112`).
    ///
    /// Окно ставится ДВАЖДЫ: сперва вокруг центра модели, затем — вокруг
    /// найденного центра данных, чтобы окно, выбранное по модели, не тянуло
    /// центр данных к модели. ⚠ Q несёт и ВСЮ ошибку модели в окне (не только
    /// сумм-пик), поэтому рядом печатается доля S в модели окна: где она мала,
    /// `c_data` мерит не сумм-пик.
    ///
    /// ПЛЕЧИ (`--arms=`): `electron` — как в дереве (умолчание анализатора);
    /// `photon` — `FsaAnalyzer.CascadeSumPhotonLight` включён; `noanchor` —
    /// привязка шкалы выключена (положение сумм-пика в шкале калибровки
    /// спектра, без множителя света); `lossjoint` — (`S166`) вынос из пика с
    /// совместной эффективностью κ (`CascadeLossJointFactor`), кривая
    /// электронная; `both` — фотонная кривая и κ разом. Плечи `S166` читаются
    /// по столбцу `A_data/A_model` — недобор сумм-пика. Положительный контроль метода — опоры
    /// привязки: у одиночных линий `ModelKev` против `MeasuredKev` печатается
    /// той же строкой, и там расхождение обязано быть в пределах ±σ опоры.
    ///
    ///   sumpeaklightprobe --spectrum=X.xml (--chain=Th-232 | --nuclides=176LU)
    ///                     [--arms=electron,photon,noanchor,lossjoint,both] [--window=1.0]
    ///                     [--dump=file.csv]
    ///
    /// ⛔ ПРОБА ОСНАСТКИ КОРПУСА (`wd_*`): состав — только ключами (`AMBER19`),
    /// `NuclideDefinitionManager` не поднимается; в конце печатается счётчик
    /// обращений к нему, не ноль — код 12. Запускать из рабочего каталога
    /// корпуса (`mk_appwd.ps1`).
    /// </summary>
    static class Program
    {
        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей.
            FsaTuningReport.Snapshot();

            string spectrumPath = null, dumpPath = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            var arms = new List<string> { "electron", "photon" };
            double window = 1.0;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal))
                    chains.AddRange(a.Substring(8).Split(','));
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal))
                    nuclides.AddRange(a.Substring(11).Split(','));
                else if (a.StartsWith("--sample=", StringComparison.Ordinal))
                    nuclides.AddRange(a.Substring(9).Split(','));
                else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dumpPath = a.Substring(7);
                else if (a.StartsWith("--window=", StringComparison.Ordinal))
                    window = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--arms=", StringComparison.Ordinal))
                {
                    arms.Clear();
                    arms.AddRange(a.Substring(7).Split(','));
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            if (chains.Count == 0 && nuclides.Count == 0)
            {
                Console.Error.WriteLine("нужен состав: --chain=Th-232 и/или --nuclides=176LU");
                return 2;
            }

            foreach (string arm in arms)
            {
                if (arm != "electron" && arm != "photon" && arm != "noanchor"
                    && arm != "lossjoint" && arm != "both")
                {
                    Console.Error.WriteLine("--arms= знает electron, photon, noanchor, lossjoint, both; дано: {0}", arm);
                    return 2;
                }
            }

            foreach (string label in chains)
            {
                if (FsaSampleChain.FromLabel(label) == null)
                {
                    Console.Error.WriteLine("--chain={0}: неизвестный ряд; известные: {1}",
                                            label, string.Join(", ", FsaSampleChain.KnownLabels));
                    return 2;
                }
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр  : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор  : {0}", ProbeDeviceConfig.Attach(rd));

            ResponseMatrix matrix = null;
            string material = null;
            if (rd.Efficiency != null && rd.Efficiency.HasGeometry && rd.Efficiency.UseResponseMatrix)
            {
                MatrixRefusal refusal;
                int fileFormat;
                ResponseMatrix loaded = ResponseMatrixStore.Load(rd.Efficiency.Guid, out refusal, out fileFormat);
                if (loaded != null && loaded.IsValidFor(rd.Efficiency.Geometry))
                {
                    matrix = loaded;
                    material = EfficiencySimulator.ScintillatorNameOf(rd.Efficiency.Geometry);
                }
                else if (loaded == null)
                {
                    Console.WriteLine("матрица : НЕТ — {0}, формат файла {1}", refusal, fileFormat);
                }
                else
                {
                    Console.WriteLine("матрица : файл есть, но клеймо не сошлось");
                }
            }

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ВХОДА: без матрицы сумм-пиков нет вовсе.
            if (matrix == null)
            {
                Console.Error.WriteLine("⛔ без матрицы сумм-пики не строятся — мерить нечего, опыт негоден");
                return 1;
            }

            Console.WriteLine("матрица : есть, вещество {0}, бин {1} кэВ, фотонная кривая {2}",
                              material ?? "-", matrix.BinKev.ToString("F3", CultureInfo.InvariantCulture),
                              FsaCascadeSummer.PhotonCurveFor(material) ?? "НЕТ");

            var nucids = new List<string>();
            foreach (string nucid in nuclides)
            {
                nucids.Add(NucidOf(nucid));
            }

            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nucids, true, true);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            Console.WriteLine("состав  : образов {0} (--lib=sample)", library.Count);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("⛔ библиотека пуста");
                return 1;
            }

            StreamWriter dump = null;
            if (dumpPath != null)
            {
                dump = new StreamWriter(dumpPath, false, new UTF8Encoding(false));
                dump.WriteLine("spectrum,arm,pairs,E_plain,E_app,fwhm_keV,pass,"
                               + "c_model,c_data,c_data_signed,d_centre,max_model,max_data,d_max,"
                               + "A_model,A_data,ratio,S_share,lo,hi");
            }

            foreach (string arm in arms)
            {
                Console.WriteLine();
                Console.WriteLine("=== плечо «{0}» ===", arm);
                FsaAnalyzer analyzer;
                FsaResult result = Run(rd, library, matrix, material, arm, out analyzer);
                if (result == null)
                {
                    continue;
                }

                // Список сумм-пиков — у сумматора С НАСТРОЙКАМИ ПЛЕЧА.
                FsaCascadeSummer summer = FsaCascadeSummer.Create(
                    matrix, material, analyzer.CoincidenceWindowSec, analyzer.CascadeXrayPartners,
                    analyzer.CascadeAnnihilationPartners, analyzer.CascadeIsomerPartners,
                    analyzer.CascadeDecayTimeProbability);
                if (summer == null)
                {
                    Console.Error.WriteLine("⛔ сумматор не создался: {0}", FsaCascadeSummer.Failure ?? "-");
                    bad++;
                    continue;
                }

                if (analyzer.CascadeSumPhotonLight)
                {
                    summer.PhotonLightCurve = FsaCascadeSummer.PhotonCurveFor(material);
                }

                Console.WriteLine("кривая суммы: {0}",
                                  summer.LightYieldName.Length > 0 ? summer.LightYieldName : "НЕТ — по энергии");

                var peaks = new List<FsaCascadeSummer.SumPeak>();
                foreach (FsaComponent component in library)
                {
                    FsaCascadeSummer.Correction correction = summer.For(component);
                    if (correction == null || correction.SumPeaks == null)
                    {
                        continue;
                    }

                    peaks.AddRange(correction.SumPeaks);
                }

                Console.WriteLine("сумм-пиков у сумматора: {0}", peaks.Count);
                foreach (FsaCascadeSummer.SumPeak p in peaks)
                {
                    double plain = p.FromKev + p.WithKev + p.ThirdKev;
                    Console.WriteLine("SUM\t{0}\t{1} + {2}{3}\tΣE {4}\tE_вид {5}\tсдвиг {6}\tплощадь {7}",
                                      p.Nuclide, F(p.FromKev, "F2"), F(p.WithKev, "F2"),
                                      p.IsTriple ? " + " + F(p.ThirdKev, "F2") : "",
                                      F(plain, "F2"), F(p.Energy, "F2"), F(p.Energy - plain, "+0.00;-0.00"),
                                      F(p.Area, "E3"));
                }

                Measure(rd, result, peaks, window, arm, dump, Path.GetFileNameWithoutExtension(spectrumPath));
            }

            if (dump != null)
            {
                dump.Dispose();
                Console.WriteLine("дамп: {0}", dumpPath);
            }

            // (`AMBER19`) поставочный список не поднимался
            int lookups = NuclideDefinitionManager.RaiseCount;
            Console.WriteLine();
            Console.WriteLine("обращений к NuclideDefinitionManager за прогон: {0}", lookups);
            if (lookups != 0)
            {
                Console.Error.WriteLine("⛔ поставочный список поднимался — код 12");
                return 12;
            }

            Console.WriteLine(bad == 0 ? "ИТОГ: замер снят" : "ИТОГ: отказов " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Группы сумм-пиков (по видимой энергии в пределах полуширины) и их
        /// положение у модели и у данных.
        /// </summary>
        static void Measure(ResultData rd, FsaResult result, List<FsaCascadeSummer.SumPeak> peaks,
                            double window, string arm, StreamWriter dump, string spectrumName)
        {
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            int[] raw = rd.EnergySpectrum.Spectrum;
            int channels = result.Model.Length;
            double[] data = result.FitSpectrum(raw);
            double[] sum = new double[channels];
            foreach (FsaComponentResult c in result.Components)
            {
                if (c.SumPeakCurve == null)
                {
                    continue;
                }

                for (int i = 0; i < channels && i < c.SumPeakCurve.Length; i++)
                {
                    sum[i] += c.SumPeakCurve[i];
                }
            }

            double[] energy = new double[channels];
            for (int i = 0; i < channels; i++)
            {
                energy[i] = calibration.ChannelToEnergy(i);
            }

            double sumTotal = 0.0;
            for (int i = 0; i < channels; i++)
            {
                sumTotal += sum[i];
            }

            Console.WriteLine("подслой сумм-пиков в результате: площадь {0} отсчётов", F(sumTotal, "F1"));
            if (!(sumTotal > 0.0))
            {
                Console.WriteLine("⚠ подслоя нет — сумм-пики в отчётный фит не попали, мерить нечего");
                return;
            }

            // Группировка по видимой энергии: пики ближе половины ПШПВ — один.
            var groups = new List<List<FsaCascadeSummer.SumPeak>>();
            var sorted = new List<FsaCascadeSummer.SumPeak>(peaks);
            sorted.Sort((x, y) => x.Energy.CompareTo(y.Energy));
            foreach (FsaCascadeSummer.SumPeak p in sorted)
            {
                if (!(p.Area > 0.0))
                {
                    continue;
                }

                double fwhm = FwhmKev(rd, calibration, p.Energy, channels);
                if (groups.Count > 0)
                {
                    List<FsaCascadeSummer.SumPeak> last = groups[groups.Count - 1];
                    if (Math.Abs(p.Energy - last[last.Count - 1].Energy) < 0.5 * fwhm)
                    {
                        last.Add(p);
                        continue;
                    }
                }

                groups.Add(new List<FsaCascadeSummer.SumPeak> { p });
            }

            Console.WriteLine("групп сумм-пиков: {0}; окно ±{1} ПШПВ", groups.Count,
                              F(window, "F2"));
            Console.WriteLine("GROUP\tпары\tΣE\tE_вид\tПШПВ\tпроход\tc_model\tc_data\tΔc\tmax_model\tmax_data\tΔmax\tA_model\tA_data\tA_data/A_model\tдоля S в модели окна\tокно");
            foreach (List<FsaCascadeSummer.SumPeak> group in groups)
            {
                double areaAll = 0.0, plainW = 0.0, appW = 0.0;
                var names = new List<string>();
                foreach (FsaCascadeSummer.SumPeak p in group)
                {
                    areaAll += p.Area;
                    plainW += p.Area * (p.FromKev + p.WithKev + p.ThirdKev);
                    appW += p.Area * p.Energy;
                    names.Add(F(p.FromKev, "F0") + "+" + F(p.WithKev, "F0")
                              + (p.IsTriple ? "+" + F(p.ThirdKev, "F0") : ""));
                }

                double plain = plainW / areaAll, app = appW / areaAll;
                double fwhm = FwhmKev(rd, calibration, app, channels);
                string pairs = string.Join(";", names.ToArray());

                // Проход 1: окно вокруг видимой энергии — ищем центр модели.
                double centre = app;
                for (int pass = 1; pass <= 2; pass++)
                {
                    double lo = centre - window * fwhm, hi = centre + window * fwhm;
                    double aModel = 0.0, mModel = 0.0, aData = 0.0, mData = 0.0, aSigned = 0.0, mSigned = 0.0;
                    double modelAll = 0.0;
                    for (int i = 0; i < channels; i++)
                    {
                        double e = energy[i];
                        if (e < lo || e > hi)
                        {
                            continue;
                        }

                        double s = sum[i];
                        double rest = result.Model[i] - s;
                        double q = data[i] - rest;
                        aModel += s;
                        mModel += s * e;
                        modelAll += result.Model[i];
                        aSigned += q;
                        mSigned += q * e;
                        if (q > 0.0)
                        {
                            aData += q;
                            mData += q * e;
                        }
                    }

                    double cModel = aModel > 0.0 ? mModel / aModel : double.NaN;
                    double cData = aData > 0.0 ? mData / aData : double.NaN;
                    double cSigned = aSigned > 0.0 ? mSigned / aSigned : double.NaN;
                    double maxModel = SmoothedMax(sum, null, null, energy, lo, hi, fwhm, calibration, channels);
                    double maxData = SmoothedMax(data, result.Model, sum, energy, lo, hi, fwhm, calibration, channels);
                    double share = modelAll > 0.0 ? aModel / modelAll : 0.0;
                    Console.WriteLine("GROUP\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}\t{7}\t{8}\t{9}\t{10}\t{11}\t{12}\t{13}\t{14}\t{15}…{16}",
                                      pairs, F(plain, "F2"), F(app, "F2"), F(fwhm, "F1"), pass,
                                      F(cModel, "F2"), F(cData, "F2"), F(cData - cModel, "+0.00;-0.00"),
                                      F(maxModel, "F2"), F(maxData, "F2"), F(maxData - maxModel, "+0.00;-0.00"),
                                      F(aModel, "F1"), F(aSigned, "F1"),
                                      aModel > 0.0 ? F(aSigned / aModel, "F3") : "-",
                                      F(share, "F3"), F(lo, "F1"), F(hi, "F1"));
                    if (dump != null)
                    {
                        dump.WriteLine(string.Join(",",
                            spectrumName,
                            arm, pairs, F(plain, "F2"), F(app, "F2"), F(fwhm, "F2"), pass.ToString(CultureInfo.InvariantCulture),
                            F(cModel, "F3"), F(cData, "F3"), F(cSigned, "F3"), F(cData - cModel, "F3"),
                            F(maxModel, "F2"), F(maxData, "F2"), F(maxData - maxModel, "F2"),
                            F(aModel, "F2"), F(aSigned, "F2"), aModel > 0.0 ? F(aSigned / aModel, "F4") : "",
                            F(share, "F4"), F(lo, "F2"), F(hi, "F2")));
                    }

                    // Проход 2 — окно вокруг центра ДАННЫХ.
                    if (double.IsNaN(cData))
                    {
                        break;
                    }

                    centre = cData;
                }
            }
        }

        /// <summary>
        /// Положение максимума кривой в окне после сглаживания на четверть
        /// ПШПВ. Для данных кривая — остаток Q = D − (M − S).
        /// </summary>
        static double SmoothedMax(double[] curve, double[] model, double[] sum, double[] energy,
                                  double lo, double hi, double fwhmKev, EnergyCalibration calibration, int channels)
        {
            double middle = 0.5 * (lo + hi);
            double chMid = ChannelOf(calibration, middle, channels);
            double chLo = ChannelOf(calibration, middle - 0.5 * fwhmKev, channels);
            double chHi = ChannelOf(calibration, middle + 0.5 * fwhmKev, channels);
            int width = (int)(0.25 * Math.Abs(chHi - chLo));
            double best = double.NegativeInfinity, bestEnergy = double.NaN;
            for (int i = 0; i < channels; i++)
            {
                if (energy[i] < lo || energy[i] > hi)
                {
                    continue;
                }

                double acc = 0.0;
                int taken = 0;
                for (int k = i - width; k <= i + width; k++)
                {
                    if (k < 0 || k >= channels)
                    {
                        continue;
                    }

                    double v = curve[k];
                    if (model != null && sum != null)
                    {
                        v -= model[k] - sum[k];
                    }

                    acc += v;
                    taken++;
                }

                if (taken > 0 && acc / taken > best)
                {
                    best = acc / taken;
                    bestEnergy = energy[i];
                }
            }

            return bestEnergy;
        }

        static double FwhmKev(ResultData rd, EnergyCalibration calibration, double energyKev, int channels)
        {
            double ch = ChannelOf(calibration, energyKev, channels);
            if (rd.FwhmCalibration == null || double.IsNaN(ch))
            {
                return 0.05 * energyKev;
            }

            double f = rd.FwhmCalibration.ChannelToFwhm(ch);
            double e0 = calibration.ChannelToEnergy(ch - 0.5 * f);
            double e1 = calibration.ChannelToEnergy(ch + 0.5 * f);
            double kev = e1 - e0;
            return kev > 0.0 ? kev : 0.05 * energyKev;
        }

        static double ChannelOf(EnergyCalibration calibration, double energyKev, int channels)
        {
            // Обращение калибровки делением пополам: у калибровок нет общего
            // аналитического обратного, а монотонность по построению есть.
            double lo = 0.0, hi = channels - 1;
            if (calibration.ChannelToEnergy(lo) > energyKev)
            {
                return double.NaN;
            }

            if (calibration.ChannelToEnergy(hi) < energyKev)
            {
                return double.NaN;
            }

            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (calibration.ChannelToEnergy(mid) < energyKev)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return 0.5 * (lo + hi);
        }

        /// <summary>
        /// Разбор плеча — настройками КОРПУСНОЙ ПРОБЫ (`CorpusFsaProbe.NewAnalyzer`
        /// без ключей), а не `FsaCalculationOptions.Of(rd)`: числа плеча
        /// `electron` обязаны совпасть с `out_rev18_mini` (χ²/ndf), это
        /// положительный контроль того, что мерится тот же разбор.
        /// </summary>
        static FsaResult Run(ResultData rd, List<FsaComponent> library, ResponseMatrix matrix,
                             string material, string arm, out FsaAnalyzer analyzer)
        {
            analyzer = new FsaAnalyzer();
            analyzer.Mode = FsaAnalyzer.ContinuumMode.Spline;
            new FsaCalculationOptions
            {
                DbLookups = true,
                ChainEquilibrium = true,
                AtomicXray = true,
                CascadeSumming = true,
                Backscatter = true,
                EscapeAndAnnihilation = true,
                PileUp = true
            }.ApplyTo(analyzer);
            analyzer.CascadeXrayPartners = true;
            analyzer.CascadeDecayTimeProbability = true;
            analyzer.CascadeAnnihilationPartners = true;
            analyzer.CascadeIsomerPartners = true;
            analyzer.CoincidenceWindowSec = 0.0;
            analyzer.BackscatterWithMatrix = false;
            analyzer.EscapeGate = true;
            analyzer.NoiseGamma = 0.0;
            analyzer.NoiseBeta = 0.0;
            analyzer.PartialResiduals = false;
            analyzer.PartialResidualGate = true;
            analyzer.RebinBackgroundToSpectrum = true;
            analyzer.AnchorScale = arm != "noanchor";
            analyzer.CascadeSumPhotonLight = arm == "photon" || arm == "both";
            analyzer.CascadeLossJointFactor = arm == "lossjoint" || arm == "both";

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            FsaMatrixBinding.Bind(analyzer, rd.Efficiency.Geometry, matrix);
            FsaTuningReport.Print(analyzer, arm);

            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("разложение не получилось: {0}", arm);
                bad++;
                return null;
            }

            Console.WriteLine("плечо «{0}»: chi2/ndf {1}, невязка {2} %, каскад применён: {3}, кривая привязки {4}, форма {5}",
                              arm, F(result.Chi2Ndf, "F4"), F(result.ModelResidual * 100.0, "F2"),
                              result.CascadeSummingUsed ? "да" : "нет",
                              result.AnchorLightCurve ?? "-", result.AnchorLightForm ?? "-");
            Console.WriteLine("ANCHOR\tшкала: усиление {0}, ноль {1} кэВ ({2} кан.); {3}",
                              F(result.Gain, "F5"), F(result.AnchorOffsetKev, "F2"),
                              F(result.OffsetChannels, "F3"), result.AnchorNote ?? "-");
            if (result.ScaleAnchors != null)
            {
                foreach (FsaScaleAnchor an in result.ScaleAnchors)
                {
                    Console.WriteLine("ANCHOR\t{0}\t{1}\tлиния {2}\tмодель {3}\tизмерение {4}\tсдвиг {5} ± {6}\tсвет {7}\tz {8}\t{9}",
                                      an.Used ? "ОПОРА" : "нет", an.Component,
                                      F(an.LineKev, "F2"), F(an.ModelKev, "F2"), F(an.MeasuredKev, "F2"),
                                      F(an.ShiftKev, "F2"), F(an.SigmaKev, "F2"), F(an.LightShiftKev, "F2"),
                                      F(an.Z, "F1"), an.Refusal ?? "");
                }
            }

            return result;
        }

        static string F(double v, string format)
        {
            return double.IsNaN(v) ? "-" : v.ToString(format, CultureInfo.InvariantCulture);
        }

        /// <summary>«Cs-137» → «137CS»: nucid, как его зовёт nucdb; nucid как есть.</summary>
        static string NucidOf(string label)
        {
            int dash = label.IndexOf('-');
            if (dash < 0)
            {
                return label.ToUpperInvariant();
            }

            return label.Substring(dash + 1) + label.Substring(0, dash).ToUpperInvariant();
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
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
            {
                if (cfg.FwhmCalibration == null && rd.EnergySpectrum != null)
                {
                    cfg.FwhmCalibration = FwhmCalibration.DefaultCalibration(
                        cfg, rd.EnergySpectrum.EnergyCalibration);
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
