using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace FsaEscapeLossProbeP148
{
    /// <summary>
    /// (`AMBER99`, П148 24.09.2026) ВЫНОС КАСКАДНЫМ ПАРТНЁРОМ ИЗ ПИКОВ ВЫЛЕТА.
    /// Ревизия П143: поправка на суммирование (<c>FsaCascadeSummer</c>) ложилась
    /// только на канал ПОЛНОГО ПОГЛОЩЕНИЯ, а одиночный и двойной вылет, K/L-вылет
    /// и канал «аннигиляция вне кристалла» — тоже линии постоянного положения —
    /// теряли ту же долю L_out и ничего не получали. Арбитр Geant4 (журнал П148,
    /// `g4_escape.py`, Tl-208 на контактной сцене G1S): r_X/r_пика для вылетов
    /// 0.996 / 0.980 / 1.02 / 1.003 при 1/(1 − L_out) = 1.48 у «не теряет».
    ///
    /// Здесь меряется ПРИЛОЖЕНИЕ: один спектр, одна библиотека, разбор с
    /// суммированием и без (`FsaAnalyzer.CascadeSumming`). Для каждого образа
    /// распада и каждого канала отклика печатается площадь ленты канала на
    /// единицу скорости счёта (`ChannelCurves[c]` / `CountRate`) в обоих плечах и
    /// их отношение «с / без»: у канала пика это ≈ 1/CF, у каналов вылета ДО
    /// правки — 1 (выноса нет), ПОСЛЕ — (1 − L_out) линий выше порога пар.
    /// Третье плечо — `CascadeEscapeLoss = false` (свойство П148, отражением):
    /// каналы вылета обязаны вернуться к 1 — положительный контроль правки.
    ///
    ///   fsaescapelossprobep148 --spectrum=X.xml --set=&lt;набор&gt; [--expect=fixed|none]
    ///
    /// Запускать из рабочего каталога витрины (`tools/fsa_showcase/wd`): там
    /// приборы, библиотека и матрицы под guid кривой спектра.
    /// `--expect=fixed` — приговор (код 1) по каналам вылета с заметной лентой
    /// (не меньше 1e-4 канала пика того же образа): (а) у плеча-контроля
    /// отношение «с / без» в пределах 0.5 % от 1; (б) у образа, чей пик
    /// теряет (отношение пика ниже 0.999), канал вылета теряет тоже —
    /// отношение ниже контроля; (в) у образа с САМЫМ сильным выносом из пика
    /// (у ториевого диска — Tl-208, 2614.5 + 583) вынос из вылета не меньше
    /// половины выноса из пика. Порогов строже нет нарочно: у образа ряда
    /// в канал вылета и в канал пика собираются РАЗНЫЕ линии со своими L_out
    /// (у Ac-228 вылеты — от линий выше 1022 кэВ, пик — от всех), и равенства
    /// отношений ждать нельзя.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            FsaTuningReport.Snapshot();
            string spectrumPath = null, setName = null, expect = "none";
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--expect=", StringComparison.Ordinal)) expect = a.Substring(9);
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            ResultData rd = Load(spectrumPath);
            if (setName != null)
            {
                bool found = false;
                foreach (NuclideSet set in nuclides.NuclideSets)
                {
                    if (set != null && string.Equals(set.Name, setName, StringComparison.OrdinalIgnoreCase))
                    {
                        nuclides.ActiveSet = set;
                        found = true;
                    }
                }

                if (!found)
                {
                    Console.Error.WriteLine("набора «{0}» нет", setName);
                    return 2;
                }
            }

            EnergySpectrum es = rd.EnergySpectrum;
            List<Peak> peaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            FsaCompositionInference.Report report;
            FsaSampleSpec spec = FsaCompositionInference.Infer(peaks, rd, out report);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            Console.WriteLine("состав: " + report);

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(rd.Efficiency != null ? rd.Efficiency.Guid : null,
                                                             out refusal, out fileFormat);
            if (matrix == null || rd.Efficiency == null || !rd.Efficiency.HasGeometry
                || !matrix.IsValidFor(rd.Efficiency.Geometry) || !matrix.HasChannels)
            {
                Console.Error.WriteLine("матрицы с каналами нет или она не годна геометрии ({0})", refusal);
                return 1;
            }

            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            var peakConfig = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            double deadTime = rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null
                ? rd.DeviceConfig.InputDeviceConfig.DeadTime() : 0.0;
            PropertyInfo escapeLoss = typeof(FsaAnalyzer).GetProperty("CascadeEscapeLoss");
            Console.WriteLine("SETUP\tFsaAnalyzer.CascadeEscapeLoss {0}", escapeLoss != null ? "есть" : "НЕТ (сборка до П148)");

            Func<bool, bool, string, FsaResult> run = (cascade, escape, arm) =>
            {
                var an = new FsaAnalyzer();
                FsaMatrixBinding.Bind(an, rd.Efficiency.Geometry, matrix);
                an.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
                if (peakConfig != null)
                {
                    an.MinEnergy = peakConfig.Min_Range;
                    an.MaxEnergy = peakConfig.Max_Range;
                }

                an.CascadeSumming = cascade;
                if (escapeLoss != null)
                {
                    escapeLoss.SetValue(an, escape, null);
                }

                // (`T243`) Отчёт о настройках — до Analyze, с пометкой плеча.
                FsaTuningReport.Print(an, arm);
                FsaResult r = an.Analyze(es, rd.BackgroundEnergySpectrum, rd.FwhmCalibration, library, efficiency);
                if (r == null)
                {
                    Console.Error.WriteLine("разложение не получилось: {0} — {1}", an.Refusal, an.RefusalNote);
                }

                return r;
            };

            FsaResult off = run(false, true, "без суммирования");
            FsaResult on = run(true, true, "с суммированием");
            FsaResult control = escapeLoss != null ? run(true, false, "контроль: вылет без выноса") : null;
            if (off == null || on == null)
            {
                return 1;
            }

            string[] names = Enum.GetNames(typeof(EfficiencySimulator.ResponseChannel));
            int peakChannel = (int)EfficiencySimulator.ResponseChannel.Peak;
            int bad = 0;

            // Образ с самым сильным выносом из пика — для приговора (в).
            string strongest = null;
            double strongestPeak = 1.0;
            foreach (FsaComponentResult cOn in on.Components)
            {
                FsaComponentResult cOff = Find(off, cOn.Name);
                if (cOn.Kind == FsaComponentKind.Nuisance || cOn.ChannelCurves == null || cOff == null
                    || cOff.ChannelCurves == null || !(cOn.CountRate > 0.0) || !(cOff.CountRate > 0.0))
                {
                    continue;
                }

                double pOff = Sum(cOff.ChannelCurves[peakChannel]) / cOff.CountRate;
                double pOn = Sum(cOn.ChannelCurves[peakChannel]) / cOn.CountRate;
                if (pOff > 0.0 && pOn / pOff < strongestPeak)
                {
                    strongestPeak = pOn / pOff;
                    strongest = cOn.Name;
                }
            }

            Console.WriteLine("образ с самым сильным выносом из пика: {0} (пик с/без {1})", strongest ?? "—", F(strongestPeak, "F5"));
            Console.WriteLine();
            Console.WriteLine("компонент\tканал\tбез суммирования\tс суммированием\tс/без\tконтроль (вылет без выноса)\tконтроль/без");
            foreach (FsaComponentResult cOn in on.Components)
            {
                if (cOn.Kind == FsaComponentKind.Nuisance || cOn.ChannelCurves == null || !(cOn.CountRate > 0.0))
                {
                    continue;
                }

                FsaComponentResult cOff = Find(off, cOn.Name);
                FsaComponentResult cCtl = control != null ? Find(control, cOn.Name) : null;
                if (cOff == null || cOff.ChannelCurves == null || !(cOff.CountRate > 0.0))
                {
                    continue;
                }

                double peakRatio = double.NaN;
                for (int c = 0; c < cOn.ChannelCurves.Length && c < names.Length; c++)
                {
                    double aOff = Sum(cOff.ChannelCurves[c]) / cOff.CountRate;
                    double aOn = Sum(cOn.ChannelCurves[c]) / cOn.CountRate;
                    double aCtl = cCtl != null && cCtl.ChannelCurves != null && cCtl.CountRate > 0.0
                        ? Sum(cCtl.ChannelCurves[c]) / cCtl.CountRate : double.NaN;
                    double ratio = aOff > 0.0 ? aOn / aOff : double.NaN;
                    double ctlRatio = aOff > 0.0 ? aCtl / aOff : double.NaN;
                    if (c == peakChannel) peakRatio = ratio;
                    Console.WriteLine("ESC\t{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}", cOn.Name, names[c], F(aOff, "E5"), F(aOn, "E5"),
                                      F(ratio, "F5"), F(aCtl, "E5"), F(ctlRatio, "F5"));

                    bool escapeLike = c != peakChannel && c != (int)EfficiencySimulator.ResponseChannel.Compton;
                    // Заметная лента: не меньше 1e-4 канала пика у того же компонента.
                    double peakArea = Sum(cOff.ChannelCurves[peakChannel]) / cOff.CountRate;
                    if (expect == "fixed" && escapeLike && aOff > 1e-4 * peakArea && !double.IsNaN(peakRatio))
                    {
                        // (а) контроль: без выноса из вылетов — как до правки.
                        if (double.IsNaN(ctlRatio) || Math.Abs(ctlRatio - 1.0) > 0.005)
                        {
                            Console.WriteLine("   ⛔ КОНТРОЛЬ: {0} / {1}: при CascadeEscapeLoss = false с/без {2} — не 1",
                                              cOn.Name, names[c], F(ctlRatio, "F5"));
                            bad++;
                        }

                        // (б) пик теряет — теряет и вылет.
                        if (peakRatio < 0.999 && !(ratio < ctlRatio - 1e-4))
                        {
                            Console.WriteLine("   ⛔ ПРИГОВОР: {0} / {1}: пик теряет ({2}), а вылет нет: с/без {3} против контроля {4}",
                                              cOn.Name, names[c], F(peakRatio, "F5"), F(ratio, "F5"), F(ctlRatio, "F5"));
                            bad++;
                        }

                        // (в) у образа с сильнейшим выносом — не меньше половины выноса из пика.
                        if (string.Equals(cOn.Name, strongest, StringComparison.Ordinal)
                            && !(ratio < 1.0 - 0.5 * (1.0 - peakRatio)))
                        {
                            Console.WriteLine("   ⛔ ПРИГОВОР: {0} / {1}: с/без {2} не ниже {3} — вынос из вылета меньше половины выноса из пика",
                                              cOn.Name, names[c], F(ratio, "F5"), F(1.0 - 0.5 * (1.0 - peakRatio), "F5"));
                            bad++;
                        }
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine("χ²/ndf: без суммирования {0}, с суммированием {1}{2}", F(off.Chi2Ndf, "F4"), F(on.Chi2Ndf, "F4"),
                              control != null ? ", контроль " + F(control.Chi2Ndf, "F4") : "");
            foreach (FsaComponentResult c in on.Components)
            {
                FsaComponentResult c0 = Find(off, c.Name);
                FsaComponentResult cc = control != null ? Find(control, c.Name) : null;
                Console.WriteLine("RATE\t{0}\t{1}\tбез {2}\tс {3}\tконтроль {4}", c.Name, c.Kind,
                                  F(c0 != null ? c0.CountRate : double.NaN, "R"), F(c.CountRate, "R"),
                                  F(cc != null ? cc.CountRate : double.NaN, "R"));
            }

            if (expect == "fixed")
            {
                Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
                return bad == 0 ? 0 : 1;
            }

            return 0;
        }

        static FsaComponentResult Find(FsaResult r, string name)
        {
            foreach (FsaComponentResult c in r.Components)
            {
                if (string.Equals(c.Name, name, StringComparison.Ordinal)) return c;
            }

            return null;
        }

        static double Sum(double[] v)
        {
            if (v == null) return 0.0;
            double s = 0.0;
            foreach (double x in v) s += x;
            return s;
        }

        static string F(double v, string fmt)
        {
            return v.ToString(fmt, CultureInfo.InvariantCulture);
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

            Console.WriteLine("SETUP\tприбор: {0}", ProbeDeviceConfig.Attach(rd));
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
