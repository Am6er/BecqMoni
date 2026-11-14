using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace CrystalOwnShelfProbeA274c
{
    /// <summary>
    /// (`AMBER274`, полоса `a274c`, 14.11.2026) ОКНО K-ПИКА СЛОЯ ВНЕ ПОДГОНКИ И χ²
    /// (<see cref="FsaAnalyzer.CrystalOwnShelfMask"/>): замер на спектрах `LABR_BRIL` и мутантах.
    ///
    /// Плечи одного прогона, на одном спектре и одном разделе слоя:
    ///   А  — без слоя (ключ окна ВКЛ) и А0 — без слоя, ключ окна ВЫКЛ: обязаны совпасть побитно;
    ///   Б  — слой ВКЛ, окно ВЫКЛ (состояние до правки `a274c`);
    ///   В  — слой ВКЛ, окно ВКЛ (умолчание ключа окна или `--shelf=от:до`).
    ///
    /// Что судится (код возврата):
    ///   1. без слоя ключ окна не меняет ни одного бита (модель, χ², усиление, нуль);
    ///   2. плечо В сняло каналы окна, плечо Б — ни одного;
    ///   3. ОТДАННАЯ ПРИВЯЗКА плеча В не дальше 0.15 ПШПВ от привязки без слоя на сетке энергий выше окна
    ///      (порог — решение Amber 13.11.2026 вопросником, дословно: «Строгий 0.15 (Рекомендую)»);
    ///   4. опор выше окна у плеча В не меньше, чем без слоя (откат привязки выключен у обоих — счёт опор самой
    ///      привязки, а не исхода отката);
    ///   4а. если без слоя отката привязки нет, у плеча В его тоже нет (`crit274c`: окно 28–47 на номинале);
    ///   5. `--expect-off-fails` — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: плечо Б обязано провалить условие 3 (так у
    ///      `LaBrBril_Eu152` при `--crit-cal=1.005`: без окна разбор бросает верное усиление, `crit274g2`).
    ///
    /// Мутанты слоя и шкалы — те же, что у `CrystalOwnActivityProbeA274` (`a274g`, `crit295z`, `crit274g`):
    /// `--mut-shift=кэВ[:от:до]`, `--own-scale=k`, `--crit-amp=k[:от:до]`, `--crit-cal=g`.
    ///
    ///     CrystalOwnShelfProbeA274c --spectrum=... --cache=каталог (--nuclides=152EU | --chains=Th-228)
    ///                               [--shelf=off|от:до] [--ratio=k] [--histories=100000] [--expect-off-fails]
    ///                               [--skip-masked] [мутанты]
    /// </summary>
    static class Program
    {
        static int bad;
        static readonly List<string> chains = new List<string>();
        static readonly List<string> nuclides = new List<string>();
        static string cacheDir;
        static bool shelfGiven, shelfOff, skipMasked;
        static double shelfLo, shelfHi, shelfRatio = double.NaN;

        sealed class Arm
        {
            public string Name;
            public FsaResult Result;
            public double[] Layer;
            public int Masked;
            public string Note;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей.
            FsaTuningReport.Snapshot();

            string spectrumPath = null;
            int histories = 100000;
            double ownScale = 1.0, mutShift = 0.0, mutLo = -1.0, mutHi = 1e9;
            double camp = 1.0, campLo = -1.0, campHi = 1e9, ccal = 1.0;
            bool expectOffFails = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal))
                {
                    spectrumPath = a.Substring(11);
                }
                else if (a.StartsWith("--cache=", StringComparison.Ordinal))
                {
                    cacheDir = a.Substring(8);
                }
                else if (a.StartsWith("--histories=", StringComparison.Ordinal))
                {
                    histories = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal))
                {
                    nuclides.AddRange(a.Substring(11).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (a.StartsWith("--chains=", StringComparison.Ordinal))
                {
                    chains.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (a == "--shelf=off")
                {
                    shelfGiven = true;
                    shelfOff = true;
                }
                else if (a.StartsWith("--shelf=", StringComparison.Ordinal))
                {
                    string[] p = a.Substring(8).Split(':');
                    shelfGiven = true;
                    shelfLo = double.Parse(p[0], CultureInfo.InvariantCulture);
                    shelfHi = double.Parse(p[1], CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--ratio=", StringComparison.Ordinal))
                {
                    shelfRatio = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else if (a == "--skip-masked")
                {
                    skipMasked = true;
                }
                else if (a == "--expect-off-fails")
                {
                    expectOffFails = true;
                }
                else if (a.StartsWith("--own-scale=", StringComparison.Ordinal))
                {
                    ownScale = double.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--mut-shift=", StringComparison.Ordinal))
                {
                    string[] p = a.Substring(12).Split(':');
                    mutShift = double.Parse(p[0], CultureInfo.InvariantCulture);
                    if (p.Length == 3)
                    {
                        mutLo = double.Parse(p[1], CultureInfo.InvariantCulture);
                        mutHi = double.Parse(p[2], CultureInfo.InvariantCulture);
                    }
                }
                else if (a.StartsWith("--crit-amp=", StringComparison.Ordinal))
                {
                    string[] p = a.Substring(11).Split(':');
                    camp = double.Parse(p[0], CultureInfo.InvariantCulture);
                    if (p.Length == 3)
                    {
                        campLo = double.Parse(p[1], CultureInfo.InvariantCulture);
                        campHi = double.Parse(p[2], CultureInfo.InvariantCulture);
                    }
                }
                else if (a.StartsWith("--crit-cal=", StringComparison.Ordinal))
                {
                    ccal = double.Parse(a.Substring(11), CultureInfo.InvariantCulture);
                }
                else
                {
                    Console.Error.WriteLine("непонятный ключ: " + a);
                    return 2;
                }
            }

            if (spectrumPath == null || cacheDir == null || (chains.Count == 0 && nuclides.Count == 0))
            {
                Console.Error.WriteLine("нужны --spectrum=, --cache= и проба (--nuclides= или --chains=)");
                return 2;
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            GeometryModel scene = LanthanumScene();
            string note;
            ResponseCrystalSection section = CrystalOwnActivityBuilder.Build(scene, 2.0, histories, 0x274, out note);
            Console.WriteLine("слой собран: записей {0}{1}", section.Count, string.IsNullOrEmpty(note) ? "" : "; " + note);
            ResponseMatrix matrix = Matrix(scene);
            if (matrix == null)
            {
                return 2;
            }

            matrix.CrystalSection = section;
            matrix.CrystalSectionDamage = null;

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("=== {0} ===", Path.GetFileName(spectrumPath));
            ScaleOwn(section, ownScale);
            MutShift(section, mutShift, mutLo, mutHi);
            CritAmp(section, camp, campLo, campHi);
            CritCal(rd, ccal);

            // ---- плечи -------------------------------------------------
            Arm bare = Run("А без слоя", rd, scene, matrix, null, true, false);
            Arm bare0 = Run("А0 без слоя, окно выкл", rd, scene, matrix, null, false, false);
            Arm off = Run("Б слой, окно выкл", rd, scene, matrix, section, false, false);
            Arm on = Run("В слой, окно вкл", rd, scene, matrix, section, !shelfOff, false);
            if (bare.Result == null || bare0.Result == null || off.Result == null || on.Result == null)
            {
                Console.WriteLine("НЕ СОШЛОСЬ: разбор не прошёл");
                return 1;
            }

            // откат привязки выключен: счёт опор самой привязки
            Arm bareNoRollback = Run("А без слоя, откат выкл", rd, scene, matrix, null, true, true);
            Arm onNoRollback = Run("В слой, окно вкл, откат выкл", rd, scene, matrix, section, !shelfOff, true);

            Console.WriteLine();
            Console.WriteLine("{0,-30} {1,10} {2,9} {3,6} {4,6} {5,10} {6,6}", "плечо", "усиление", "нуль,кан", "опор", "откат", "χ²/ndf П", "снято");
            foreach (Arm arm in new[] { bare, bare0, off, on, bareNoRollback, onNoRollback })
            {
                if (arm.Result == null)
                {
                    Console.WriteLine("{0,-30} отказ", arm.Name);
                    continue;
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-30} {1,10:F6} {2,9:F3} {3,6} {4,6} {5,10:F2} {6,6}",
                    arm.Name, arm.Result.Gain, arm.Result.OffsetChannels, arm.Result.ScaleAnchorsUsed,
                    arm.Result.AnchorRolledBack ? "да" : "нет", arm.Result.Chi2NdfPoisson, arm.Masked));
                if (arm.Result.AnchorRolledBack)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    откат: с привязкой усиление {0:F6}, опор {1}, χ²/ndf {2:F2}",
                        arm.Result.AnchorRollbackGain, arm.Result.AnchorRollbackAnchors, arm.Result.AnchorRollbackChi2NdfPoisson));
                }

                if (!string.IsNullOrEmpty(arm.Note))
                {
                    Console.WriteLine("    примечание слоя: " + arm.Note);
                }
            }

            Console.WriteLine();
            Console.WriteLine("{0,-30} {1,-14} {2,12} {3,9}", "плечо", "компонент", "скорость", "z");
            foreach (Arm arm in new[] { bare, off, on })
            {
                foreach (FsaComponentResult component in arm.Result.Components)
                {
                    if (component.Kind != FsaComponentKind.Nuisance && component.CountRate > 0.0)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0,-30} {1,-14} {2,12:F2} {3,9:F2}",
                            arm.Name, component.Name, component.CountRate, component.Z));
                    }
                }
            }

            // χ² Пуассона по полосам на полной модели (модель + вычтенный слой на шкале отданного разбора)
            EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
            double winLo = shelfGiven && !shelfOff ? shelfLo : new FsaAnalyzer().CrystalOwnShelfLoKev;
            double winHi = shelfGiven && !shelfOff ? shelfHi : new FsaAnalyzer().CrystalOwnShelfHiKev;
            double[] edges = { 0.0, winLo, winHi, 100.0, 500.0, 1400.0, 1600.0, 4000.0 };
            Console.WriteLine();
            Console.WriteLine("χ² Пуассона (1/max(y,1)) по полосам, полная модель:");
            var header = new StringBuilder(string.Format("{0,-30}", "плечо"));
            for (int b = 0; b + 1 < edges.Length; b++)
            {
                header.Append(string.Format(CultureInfo.InvariantCulture, " {0,11}", edges[b].ToString("F0", CultureInfo.InvariantCulture) + "–" + edges[b + 1].ToString("F0", CultureInfo.InvariantCulture)));
            }

            Console.WriteLine(header.ToString());
            foreach (Arm arm in new[] { bare, off, on })
            {
                var sums = new double[edges.Length - 1];
                for (int i = 0; i < rd.EnergySpectrum.NumberOfChannels; i++)
                {
                    double y = rd.EnergySpectrum.Spectrum[i];
                    double m = arm.Result.Model[i] + (arm.Layer != null ? arm.Layer[i] : 0.0);
                    double kev = cal.ChannelToEnergy(i);
                    for (int b = 0; b + 1 < edges.Length; b++)
                    {
                        if (kev >= edges[b] && kev < edges[b + 1])
                        {
                            sums[b] += (y - m) * (y - m) / Math.Max(y, 1.0);
                        }
                    }
                }

                var line = new StringBuilder(string.Format("{0,-30}", arm.Name));
                foreach (double s in sums)
                {
                    line.Append(string.Format(CultureInfo.InvariantCulture, " {0,11:F1}", s));
                }

                Console.WriteLine(line.ToString());
            }

            // ---- суд ---------------------------------------------------
            Console.WriteLine();
            bool same = BitConverter.DoubleToInt64Bits(bare.Result.Chi2NdfPoisson) == BitConverter.DoubleToInt64Bits(bare0.Result.Chi2NdfPoisson)
                        && BitConverter.DoubleToInt64Bits(bare.Result.Gain) == BitConverter.DoubleToInt64Bits(bare0.Result.Gain)
                        && BitConverter.DoubleToInt64Bits(bare.Result.OffsetChannels) == BitConverter.DoubleToInt64Bits(bare0.Result.OffsetChannels)
                        && Same(bare.Result.Model, bare0.Result.Model);
            Check("без слоя ключ окна не меняет ни одного бита (модель, χ², усиление, нуль)", same);
            Check("без слоя окно не снимает каналов", bare.Masked == 0 && bare0.Masked == 0);
            Check("слой без окна каналов не снимает", off.Masked == 0);
            if (!shelfOff)
            {
                Check("слой с окном снял каналы окна", on.Masked > 0);
            }

            double worstOn = ScaleShift("В слой, окно вкл", rd, bare.Result, on.Result, winHi);
            double worstOff = ScaleShift("Б слой, окно выкл", rd, bare.Result, off.Result, winHi);
            Check("отданная привязка со слоем и окном не дальше 0.15 ПШПВ от привязки без слоя (сетка выше окна)", worstOn <= 0.15);
            // (`crit274c`) Сдвиг шкалы 0.15 ПШПВ откат на номинале не ловит: брошенная привязка там близка к шкале файла.
            // Поэтому флаг отката судится сам: без слоя привязка удержана — со слоем и окном её бросать нельзя.
            if (!bare.Result.AnchorRolledBack)
            {
                Check("без слоя отката нет — у слоя с окном его тоже нет", !on.Result.AnchorRolledBack);
            }

            if (expectOffFails)
            {
                Check("ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: без окна отданная привязка со слоем дальше 0.15 ПШПВ (проверка ловит)", worstOff > 0.15);
            }

            if (bareNoRollback.Result != null && onNoRollback.Result != null)
            {
                int n0 = AnchorsAbove(bareNoRollback.Result, winHi), n1 = AnchorsAbove(onNoRollback.Result, winHi);
                Console.WriteLine("опор выше окна (откат выкл): без слоя {0}, слой с окном {1}; усиление {2:F6} → {3:F6}",
                                  n0, n1, bareNoRollback.Result.Gain, onNoRollback.Result.Gain);
                Check("опор выше окна со слоем и окном не меньше, чем без слоя (откат выкл)", n0 > 0 && n1 >= n0);
            }
            else
            {
                Check("разбор при выключенном откате прошёл", false);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Наибольший сдвиг шкалы плеча против разбора без слоя у энергий взятых опор выше окна, в ПШПВ.</summary>
        static double ScaleShift(string name, ResultData rd, FsaResult without, FsaResult with, double aboveKev)
        {
            EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
            // Сдвиг шкалы линеен по энергии: судится на сетке от верха окна до верха спектра, шаг 50 кэВ.
            double worst = 0.0, worstKev = 0.0, worstShift = 0.0;
            int channels = rd.EnergySpectrum.NumberOfChannels;
            double top = cal.ChannelToEnergy(channels - 2);
            for (double e = Math.Max(aboveKev, 50.0); e <= top; e += 50.0)
            {
                int c = (int)Math.Round(cal.EnergyToChannel(e, channels));
                double kevPerChannel = cal.ChannelToEnergy(c + 1) - cal.ChannelToEnergy(c);
                double shift = (with.Gain - without.Gain) * e + (with.OffsetChannels - without.OffsetChannels) * kevPerChannel;
                double ratio = Math.Abs(shift) / FwhmKev(rd, e);
                if (ratio >= worst)
                {
                    worst = ratio;
                    worstKev = e;
                    worstShift = shift;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "отданная привязка [{0}]: усиление {1:F6} → {2:F6}, нуль {3:F3} → {4:F3} кан.; наибольший сдвиг шкалы {5:+0.00;-0.00} кэВ у {6:F1} кэВ = {7:F3} ПШПВ",
                name, without.Gain, with.Gain, without.OffsetChannels, with.OffsetChannels, worstShift, worstKev, worst));
            return worst;
        }

        static int AnchorsAbove(FsaResult result, double aboveKev)
        {
            int used = 0;
            foreach (FsaScaleAnchor anchor in result.ScaleAnchors ?? new List<FsaScaleAnchor>())
            {
                if (anchor.Used && !double.IsNaN(anchor.LineKev) && anchor.LineKev > aboveKev)
                {
                    used++;
                }
            }

            return used;
        }

        static double FwhmKev(ResultData rd, double kev)
        {
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            int channels = rd.EnergySpectrum.NumberOfChannels;
            double channel = calibration.EnergyToChannel(kev, channels);
            int c = (int)Math.Round(channel);
            double kevPerChannel = calibration.ChannelToEnergy(c + 1) - calibration.ChannelToEnergy(c);
            return rd.FwhmCalibration.ChannelToFwhm(channel) * kevPerChannel;
        }

        static Arm Run(string name, ResultData rd, GeometryModel geometry, ResponseMatrix matrix,
                       ResponseCrystalSection section, bool shelf, bool rollbackOff)
        {
            var analyzer = new FsaAnalyzer();
            analyzer.PileUp = false;
            // Проба идёт по матрице без кривой эффективности: сцену она задаёт сама.
            analyzer.RequireGeometry = false;
            analyzer.CrystalOwnLayer = section != null;
            analyzer.CrystalOwnShelfMask = shelf;
            if (shelf && shelfGiven && !shelfOff)
            {
                analyzer.CrystalOwnShelfLoKev = shelfLo;
                analyzer.CrystalOwnShelfHiKev = shelfHi;
            }

            if (!double.IsNaN(shelfRatio))
            {
                analyzer.CrystalOwnShelfPeakRatio = shelfRatio;
            }

            if (skipMasked)
            {
                analyzer.AnchorWindowSkipsMasked = true;
            }

            if (rollbackOff)
            {
                analyzer.AnchorRollback = false;
            }

            FsaTuningReport.Print(analyzer, name);
            FsaMatrixBinding.Bind(analyzer, geometry, matrix);
            analyzer.CrystalOwnSection = section;
            if (rd.Efficiency == null)
            {
                rd.Efficiency = new EfficiencyConfigData { Geometry = geometry };
            }
            else
            {
                rd.Efficiency.Geometry = geometry;
            }

            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nuclides, true, true);
            FsaCalculationOptions.Of(rd).ApplyTo(spec);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            var arm = new Arm { Name = name };
            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста");
                return arm;
            }

            arm.Result = analyzer.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration, library, efficiency);
            if (arm.Result == null)
            {
                Console.Error.WriteLine("отказ разбора [{0}]: {1} — {2}", name, analyzer.Refusal, analyzer.RefusalNote);
                return arm;
            }

            arm.Layer = analyzer.CrystalOwnFixedCountsAt(arm.Result.Gain, arm.Result.OffsetChannels);
            arm.Masked = analyzer.CrystalOwnShelfMaskChannels;
            arm.Note = arm.Result.CrystalOwnNote;
            return arm;
        }

        // ---- сцена, матрица, мутанты: те же, что у `CrystalOwnActivityProbeA274` ----

        static GeometryModel LanthanumScene()
        {
            return new GeometryModel
            {
                Name = "LaBr3 38x38 (AMBER274)",
                IsScintillator = true,
                Shape = CrystalShape.Cylinder,
                CrystalDiameter = 38.0,
                CrystalHeight = 38.0,
                Crystal = Material("Lanthanum bromide"),
                Reflector = Material("Polytetrafluoroethylene"),
                Cladding = Material("Aluminum"),
                Gap = Material("Air, dry"),
                FrontReflectorThickness = 1.0,
                SideReflectorThickness = 1.5,
                FrontCladdingThickness = 1.0,
                SideCladdingThickness = 1.0,
                FrontGapThickness = 0.0,
                SideGapThickness = 0.0,
                MountingThickness = 0.0,
            };
        }

        static GeometryMaterial Material(string name)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            return entry != null ? GeometryMaterialLibrary.Make(entry, entry.Density) : new GeometryMaterial();
        }

        /// <summary>Матрица сцены — из кэша пробы слоя (`a274_lanthanum_bromide_38x38.rmx`); нет годной — собирается тем же рецептом.</summary>
        static ResponseMatrix Matrix(GeometryModel geometry)
        {
            string path = Path.Combine(cacheDir, "a274_lanthanum_bromide_38x38.rmx");
            if (File.Exists(path))
            {
                MatrixRefusal refusal;
                int format;
                ResponseMatrix loaded = ResponseMatrix.Load(path, out refusal, out format);
                if (loaded != null && loaded.IsValidFor(geometry))
                {
                    Console.WriteLine("матрица из файла: {0}", path);
                    return loaded;
                }

                Console.WriteLine("матрица из файла не годна ({0}) — сборка заново", refusal);
            }

            var options = new ResponseMatrixOptions
            {
                NodeCount = 16,
                Histories = 20000,
                BinKev = 2.0,
                MinEnergyKev = 30.0,
                MaxEnergyKev = 3000.0,
            };
            ResponseMatrix matrix = ResponseMatrixBuilder.Build(geometry, options, null, System.Threading.CancellationToken.None);
            Console.WriteLine("матрица собрана: {0} узлов", matrix.Energies != null ? matrix.Energies.Length : 0);
            matrix.Save(path);
            return matrix;
        }

        static void ScaleOwn(ResponseCrystalSection section, double scale)
        {
            if (scale == 1.0)
            {
                return;
            }

            foreach (CrystalOwnEntry entry in section.Entries)
            {
                if (!entry.Free)
                {
                    entry.ActivityBq *= scale;
                }
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "МУТАНТ: амплитуда слоя ×{0:F3}", scale));
        }

        static void MutShift(ResponseCrystalSection section, double shift, double lo, double hi)
        {
            if (shift == 0.0)
            {
                return;
            }

            double bin = section.BinKev;
            foreach (CrystalOwnEntry entry in section.Entries)
            {
                double[] src = entry.PerDecay;
                int n = src.Length;
                var dst = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double e = i * bin;
                    if (e < lo || e > hi)
                    {
                        dst[i] += src[i];
                        continue;
                    }

                    double target = i + shift / bin;
                    int j = (int)Math.Floor(target);
                    double f = target - j;
                    if (j >= 0 && j < n) dst[j] += src[i] * (1.0 - f);
                    if (j + 1 >= 0 && j + 1 < n) dst[j + 1] += src[i] * f;
                }

                entry.PerDecay = dst;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "МУТАНТ: слой сдвинут на {0:F2} кэВ в полосе {1:F0}…{2:F0} кэВ", shift, lo, hi));
        }

        static void CritAmp(ResponseCrystalSection section, double k, double lo, double hi)
        {
            if (k == 1.0)
            {
                return;
            }

            foreach (CrystalOwnEntry entry in section.Entries)
            {
                double[] h = (double[])entry.PerDecay.Clone();
                for (int i = 0; i < h.Length; i++)
                {
                    double e = i * section.BinKev;
                    if (e >= lo && e <= hi)
                    {
                        h[i] *= k;
                    }
                }

                entry.PerDecay = h;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "МУТАНТ: амплитуда слоя ×{0:F3} в полосе {1:F0}…{2:F0} кэВ", k, lo, hi));
        }

        static void CritCal(ResultData rd, double g)
        {
            if (g == 1.0)
            {
                return;
            }

            var pc = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (pc == null)
            {
                Console.WriteLine("МУТАНТ: калибровка не полином — шкала не изменена");
                return;
            }

            var c = (double[])pc.Coefficients.Clone();
            for (int i = 0; i < c.Length; i++)
            {
                c[i] *= g;
            }

            pc.Coefficients = c;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "МУТАНТ: шкала спектра ×{0:F4}", g));
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

            return rd;
        }

        static bool Same(double[] a, double[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (BitConverter.DoubleToInt64Bits(a[i]) != BitConverter.DoubleToInt64Bits(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("{0}: {1}", ok ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ", what);
            if (!ok)
            {
                bad++;
            }
        }
    }
}
