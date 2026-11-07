using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace EscapeLayerProbeA311
{
    /// <summary>
    /// (`AMBER311`) КУДА ДЕВАЮТСЯ SE и DE ОТ 2614 кэВ ПРИ МАТРИЦЕ ОТКЛИКА.
    ///
    /// Постановка Amber 05.11.2026: «Asn80*80 спектр ториевого диска. При
    /// отсутствующей матрице отклика есть SE, DE от 2614. При наличии матрицы
    /// отклика - они исчезают. Выяснить почему.»
    ///
    ///     escapelayerprobea311 --spectrum=&lt;AS80_Th232Medal.xml&gt; [--set=Th-232 + K-40]
    ///                          [--efficiency=&lt;кривая прибора&gt;] [--out=&lt;каталог выгрузок&gt;]
    ///                          [--spike=N] [--no-controls] [--rows-only]
    ///     escapelayerprobea311 --table=&lt;список&gt; --corpus=&lt;каталог&gt; --store=&lt;склад .rmx&gt;
    ///
    /// Один спектр, один exe, шесть разборов: состав {«Из NucBase», «из сета», «по
    /// подписям»} × {без матрицы, с матрицей}. Анализатор собирается теми же
    /// движениями и в том же порядке, что у `FsaAnalysisSession.Capture`: фасад
    /// настроек, свойства спектра, `FsaMatrixBinding.Bind`, окно совпадения,
    /// диапазон. Состав — теми же вызовами, что `FsaAnalysisSession.Compute`.
    ///
    /// Что печатается по каждому разбору:
    ///
    ///   LIB    — образы вылета и аннигиляции в библиотеке ДО гейта;
    ///   COMP   — они же в результате (то есть что гейт и отсев оставили);
    ///   ROW    — строки легенды `FsaPresentationBuilder.Build` — то, что на экране;
    ///   POS    — на каких каналах модель с матрицей ставит пик 2614.5, SE и DE. ⛔ У
    ///            этого спектра шкала файла (полином 4-й степени) гнётся: SE лежит
    ///            около 2190…2206 кэВ оси, и зона «2103.5 кэВ оси» пика не содержит.
    ///            Поэтому зоны две: «ось» (как читает человек) и «по месту»;
    ///   ZONE   — в зоне ±3σ: сеть (данные за вычетом фона), модель, невязка и её
    ///            значимость, подложка;
    ///   LAYER  — в той же зоне по компонентам: лента образа и каждый канал отклика;
    ///   GAUSS  — площадь над квадратичной подложкой (гаусс той же ширины): сеть,
    ///            модель, каналы. Сумма в окне мешает пик с комптоном, площадь — нет;
    ///   RATIO  — SE/пик и DE/пик: из данных, из свободной колонки, из матрицы;
    ///   SCALE  — данные против ФОРМЫ канала: сеть = a + b·t + α·(модель − канал) +
    ///            β·канал(со сдвигом): β = 1 — матрица несёт вылет в той доле, что
    ///            в данных; лучший сдвиг — насколько он не там.
    ///
    /// Положительные контроли, без которых «всё сошлось» ничего не значит:
    ///
    ///   K0  гейт снят при матрице: свободные SE/DE рядом с матричным образом;
    ///   K1  в спектр подмешан гаусс известной площади N на место колонки SE: без
    ///       матрицы и при снятом гейте его обязана взять колонка; при матрице —
    ///       невязка и сплайн (колонки нет), а канал SE расти не обязан;
    ///   K2  в спектр подмешан образ Tl-208 × 0.5 при выключенном равновесии: каналы
    ///       Peak, SE, DE обязаны вырасти вместе;
    ///   K3  каналы вылета вынуты из матрицы в памяти: то, что они держали в зоне SE,
    ///       обязан забрать сплайн или невязка — в модель пик кладут именно эти каналы.
    ///
    /// Код возврата: 0 — все контроли сработали; 1 — контроль не сработал;
    /// 2 — нечего мерить (нет спектра, матрицы, сета).
    /// </summary>
    static class Probe
    {
        const double Parent = 2614.511;
        const double SeKev = Parent - 511.0;
        const double DeKev = Parent - 1022.0;
        const double AnnKev = 511.0;
        const int Se = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilation;
        const int De = (int)EfficiencySimulator.ResponseChannel.EscapeAnnihilationDouble;
        const int PeakCh = (int)EfficiencySimulator.ResponseChannel.Peak;

        static readonly string[] ChannelNames = { "Peak", "Compton", "SE", "XK", "DE", "XL", "AnnOut", "Beta" };
        static string outDir;
        static int bad;

        enum CompositionPath { NucBase, Set, Labels }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru");
            FsaTuningReport.Snapshot();

            string spectrumPath = null, setName = "Th-232 + K-40", efficiencyName = null;
            // (критик 06.11.2026) Свободная колонка без матрицы при N = 30000 упирается в границу
            // NNLS: конфиг Amber 27.2 / 80.4 / 97.2 % при 30000 / 90000 / 300000. Порог не двигается.
            double spike = 300000.0;
            const double SpikeAtGate = 30000.0;
            bool controls = true, rowsOnly = false;
            string tableList = null, tableStore = null, tableCorpus = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--efficiency=", StringComparison.Ordinal)) efficiencyName = a.Substring(13);
                else if (a.StartsWith("--spike=", StringComparison.Ordinal)) spike = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a == "--no-controls") controls = false;
                else if (a == "--rows-only") rowsOnly = true;
                else if (a.StartsWith("--table=", StringComparison.Ordinal)) tableList = a.Substring(8);
                else if (a.StartsWith("--store=", StringComparison.Ordinal)) tableStore = a.Substring(8);
                else if (a.StartsWith("--corpus=", StringComparison.Ordinal)) tableCorpus = a.Substring(9);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (tableList != null)
            {
                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                return EscapeTable(tableCorpus, tableStore, tableList);
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=");
                return 2;
            }

            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            ResultData rd = Load(spectrumPath);
            if (efficiencyName != null)
            {
                foreach (DeviceConfigInfo device in DeviceConfigManager.GetInstance().DeviceConfigList)
                {
                    foreach (EfficiencyConfigData curve in device.EfficiencyConfigs)
                    {
                        if (string.Equals(curve.Name, efficiencyName, StringComparison.OrdinalIgnoreCase))
                        {
                            rd.Efficiency = curve.Copy();
                        }
                    }
                }
            }

            if (rd.Efficiency == null)
            {
                Console.Error.WriteLine("у спектра нет кривой");
                return 2;
            }

            NuclideSet active = null;
            foreach (NuclideSet set in nuclides.NuclideSets)
            {
                if (set != null && string.Equals(set.Name, setName, StringComparison.OrdinalIgnoreCase))
                {
                    active = set;
                }
            }

            if (active == null)
            {
                Console.Error.WriteLine("сета «{0}» нет", setName);
                return 2;
            }

            nuclides.ActiveSet = active;
            Console.WriteLine("SETUP\tсет\t{0}\tопределений\t{1}\tфон в разборе\t{2}", active.Name, nuclides.NuclideDefinitions.Count,
                              rd.BackgroundEnergySpectrum != null ? "да" : "нет");

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(rd.Efficiency.Guid, out refusal, out fileFormat);
            if (matrix == null || !matrix.IsValidFor(rd.Efficiency.Geometry))
            {
                Console.Error.WriteLine("матрица не годна: {0}, формат {1}, файл прочитан: {2}, клеймо: {3}", refusal, fileFormat,
                                        matrix != null, matrix != null ? matrix.Stamp : "-");
                return 2;
            }

            Console.WriteLine("SETUP\tматрица\t{0}\tузлов {1}\tбин {2} кэВ\tисторий {3}\tкристалл {4}×{5} мм {6}\tклеймо {7}",
                              rd.Efficiency.Name, matrix.NodeCount, F(matrix.BinKev), matrix.Histories,
                              F(rd.Efficiency.Geometry.CrystalDiameter), F(rd.Efficiency.Geometry.CrystalHeight),
                              rd.Efficiency.Geometry.Crystal != null ? rd.Efficiency.Geometry.Crystal.Name : "?", matrix.Stamp);

            MatrixTruth(matrix, Parent);
            MatrixTruth(matrix, 1630.627);
            MatrixTruth(matrix, 1588.2);

            // --- поиск пиков: подпись не зависит от матрицы, она раньше разбора
            List<Peak> peaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None,
                                                             nuclides.ActiveSet, nuclides.NuclideDefinitions);
            rd.DetectedPeaks = peaks;
            PrintPeaks("сет приложения", peaks);

            var arms = new Dictionary<string, Arm>();
            foreach (CompositionPath path in new[] { CompositionPath.NucBase, CompositionPath.Set, CompositionPath.Labels })
            {
                string prefix = path == CompositionPath.NucBase ? "NucBase" : (path == CompositionPath.Set ? "Set" : "Labels");
                Arm withM = Run(prefix + " с матрицей", rd, rd.EnergySpectrum, peaks, nuclides, path, matrix, null);
                Arm noM = Run(prefix + " без матрицы", rd, rd.EnergySpectrum, peaks, nuclides, path, null, null);
                if (withM == null || noM == null)
                {
                    return 1;
                }

                Positions pos = Positions.Of(withM);
                noM.Print(pos, true);
                withM.Print(pos, true);
                arms[prefix + "+"] = withM;
                arms[prefix + "-"] = noM;
                Ratios(noM, withM, pos);
                ScaleTests(withM, pos);
            }

            // Какой подписью метит пики состав из библиотеки (путь корпуса).
            List<FsaComponent> libForLabels = FsaSampleLibrary.Build(DeclaredSpec(rd, nuclides, new FsaCalculationOptions()));
            PrintPeaks("библиотека разбора (AsDefinitions)",
                       new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None, null,
                                                     FsaSampleLibrary.AsDefinitions(libForLabels)));

            // --- (решение Amber 06.11.2026) строки вылета под нуклидом и галка «Пики вылета»
            var offOptions = new FsaCalculationOptions { DbLookups = true, FromSet = true, EscapeAndAnnihilation = false };
            Arm galkaOff = RunWith("Set с матрицей, галка «Пики вылета» выкл", rd, rd.EnergySpectrum, peaks, nuclides,
                                   CompositionPath.Set, matrix, null, offOptions);
            RowsAcceptance(arms["Set+"], arms["Set-"], galkaOff);
            if (rowsOnly)
            {
                return bad == 0 ? 0 : 1;
            }

            // --- (критик 06.11.2026) Н1, Н2: только замер
            ScaleStudy(rd, matrix, nuclides);
            WidthStudy("из сета", arms["Set+"], matrix);
            WidthStudy("по подписям", arms["Labels+"], matrix);
            Arm noWidth = Run("Set с матрицей, множитель ширины выкл", rd, rd.EnergySpectrum, peaks, nuclides, CompositionPath.Set, matrix,
                              a => a.AnchorWidth = false);
            Console.WriteLine("H2\tплечо без множителя ширины: χ²/ndf {0}, усиление {1}, χ²/кан зоны пика {2}", F3(noWidth.Result.Chi2Ndf),
                              noWidth.Result.Gain.ToString("F6", CultureInfo.InvariantCulture), F3(noWidth.ChiInZone(Positions.Of(noWidth).ChPeak)));
            WidthStudy("из сета, множитель выкл", noWidth, matrix);
            Console.WriteLine("H3\tпо подписям с матрицей: {0}", arms["Labels+"].Summary(Positions.Of(arms["Labels+"])));
            Console.WriteLine("H3\tиз сета с матрицей: {0}", arms["Set+"].Summary(Positions.Of(arms["Set+"])));

            if (!controls)
            {
                return bad == 0 ? 0 : 1;
            }

            Arm baseFree = arms["Set-"];
            Arm baseMatrix = arms["Set+"];
            Positions p0 = Positions.Of(baseMatrix);

            // --- K0: гейт снят при матрице
            Console.WriteLine();
            Console.WriteLine("=== K0: гейт снят при матрице (свободные SE/DE рядом с матричным образом) ===");
            Arm k0 = Run("K0 Set с матрицей, гейт снят", rd, rd.EnergySpectrum, peaks, nuclides, CompositionPath.Set, matrix, a => a.EscapeGate = false);
            k0.Print(p0, false);
            Check("K0: при снятом гейте колонки SE/DE в библиотеке есть", k0.LibraryHas("SE-2615") && k0.LibraryHas("DE-2615"));
            Console.WriteLine("K0\tколонка SE-2615 {0} отсч. (её место — {1} кэВ оси), DE-2615 {2} отсч.; у слоя Tl-208 при гейте канал SE {3}, DE {4}",
                              F(k0.ColumnArea("SE-")), F(k0.AxisKev(k0.NominalChannel(SeKev))), F(k0.ColumnArea("DE-")),
                              F(baseMatrix.ChannelSum("Tl-208", Se)), F(baseMatrix.ChannelSum("Tl-208", De)));

            // --- K1: подмешанный гаусс
            double chFree = baseFree.NominalChannel(SeKev);
            Console.WriteLine();
            Console.WriteLine("=== K1: подмешан гаусс площадью N = {0} ===", F(spike));
            EnergySpectrum atColumn = rd.EnergySpectrum.Clone();
            double n1 = AddGauss(atColumn, rd.FwhmCalibration, chFree, spike);
            Arm k1Free = Run("K1 Set без матрицы", rd, atColumn, peaks, nuclides, CompositionPath.Set, null, null);
            k1Free.Print(p0, false);
            double dFree = k1Free.ColumnArea("SE-") - baseFree.ColumnArea("SE-");
            double dFreeSpline = k1Free.ContinuumInZone(chFree) - baseFree.ContinuumInZone(chFree);
            Console.WriteLine("K1\tбез матрицы: ΔSE-2615 = {0} из {1} ({2} %), Δсплайн в ±3σ {3} ({4} %)", F(dFree), F(n1), F(100.0 * dFree / n1),
                              F(dFreeSpline), F(100.0 * dFreeSpline / n1));
            Check("K1: без матрицы подмешанное берёт колонка SE-2615 (≥ 60 %)", dFree >= 0.6 * n1);

            // (критик 06.11.2026) При матрице шкала идёт через карту каналов, и `NominalChannel` её
            // не знает: 5437 при колонке на 5613. Место — по карте самого плеча, из SE и DE линии 2614.5.
            double chGate = ColumnChannelByMap(k0, SeKev);
            Console.WriteLine("K1\tместо колонки SE-2615 при снятом гейте по карте плеча: канал {0}", F(chGate));
            EnergySpectrum atGate = rd.EnergySpectrum.Clone();
            double n2 = AddGauss(atGate, rd.FwhmCalibration, chGate, spike);
            Arm k1Gate = Run("K1 Set с матрицей, гейт снят", rd, atGate, peaks, nuclides, CompositionPath.Set, matrix, a => a.EscapeGate = false);
            k1Gate.Print(p0, false);
            double dGate = k1Gate.ColumnArea("SE-") - k0.ColumnArea("SE-");
            Console.WriteLine("K1\tматрица, гейт снят: ΔSE-2615 = {0} из {1} ({2} %)", F(dGate), F(n2), F(100.0 * dGate / n2));
            Check("K1: при снятом гейте подмешанное берёт колонка SE-2615 (≥ 50 %)", dGate >= 0.5 * n2);

            EnergySpectrum atSe = rd.EnergySpectrum.Clone();
            double n3 = AddGauss(atSe, rd.FwhmCalibration, p0.ChSe, SpikeAtGate);
            Arm k1Matrix = Run("K1 Set с матрицей, гейт стоит", rd, atSe, peaks, nuclides, CompositionPath.Set, matrix, null);
            k1Matrix.Print(p0, false);
            double dRes = k1Matrix.DiffInZone(p0.ChSe) - baseMatrix.DiffInZone(p0.ChSe);
            double dCont = k1Matrix.ContinuumInZone(p0.ChSe) - baseMatrix.ContinuumInZone(p0.ChSe);
            double dLayer = k1Matrix.ChannelSum("Tl-208", Se) - baseMatrix.ChannelSum("Tl-208", Se);
            Console.WriteLine("K1\tматрица, гейт стоит: Δ(данные−модель) в зоне {0} ({1} %), Δсплайн в зоне {2} ({3} %), Δканал SE весь {4}, N {5}",
                              F(dRes), F(100.0 * dRes / n3), F(dCont), F(100.0 * dCont / n3), F(dLayer), F(n3));
            Check("K1: при гейте колонки нет — подмешанное ушло в невязку и сплайн, а не в канал SE (≥ 50 %)",
                  dRes + dCont >= 0.5 * n3 && dLayer < 0.3 * n3);

            // --- K2: подмешан образ Tl-208 × 0.5, равновесие выключено
            Console.WriteLine();
            // ⚠ K2 — тождество: каналы слоя делят одну амплитуду, и вырасти порознь не могут. Он
            // проверяет проводку, а не то, что канал верен; доказательством не считается.
            Console.WriteLine("=== K2: подмешан образ Tl-208 × 0.5 при выключенном равновесии (тождество, не доказательство) ===");
            var freeOptions = new FsaCalculationOptions { DbLookups = true, ChainEquilibrium = false, FromSet = true };
            Arm k2Base = RunWith("K2 база, равновесие выкл", rd, rd.EnergySpectrum, peaks, nuclides, CompositionPath.Set, matrix, null, freeOptions);
            FsaComponentResult tl = k2Base.Component("Tl-208");
            if (tl == null || tl.Curve == null)
            {
                Check("K2: у разбора есть слой Tl-208 с лентой", false);
            }
            else
            {
                EnergySpectrum more = rd.EnergySpectrum.Clone();
                for (int i = 0; i < more.Spectrum.Length && i < tl.Curve.Length; i++)
                {
                    more.Spectrum[i] += (int)Math.Round(0.5 * tl.Curve[i]);
                }

                Arm k2More = RunWith("K2 подмешано, равновесие выкл", rd, more, peaks, nuclides, CompositionPath.Set, matrix, null, freeOptions);
                foreach (int k in new[] { PeakCh, Se, De })
                {
                    double was = k2Base.ChannelSum("Tl-208", k), now = k2More.ChannelSum("Tl-208", k);
                    Console.WriteLine("K2\tTl-208 канал {0}: {1} → {2} (×{3})", ChannelNames[k], F(was), F(now), F3(now / was));
                    Check("K2: канал " + ChannelNames[k] + " вырос вместе со слоем (×1.3…1.7)", now / was > 1.3 && now / was < 1.7);
                }
            }

            // --- K3: каналы вылета вынуты из матрицы
            Console.WriteLine();
            Console.WriteLine("=== K3: каналы SE и DE вынуты из матрицы в памяти ===");
            MatrixRefusal r2;
            int f2;
            ResponseMatrix gutted = ResponseMatrixStore.Load(rd.Efficiency.Guid, out r2, out f2);
            double removed = Gut(gutted);
            Console.WriteLine("K3\tвынуто из строк матрицы: {0} % суммы всех строк", F3(100.0 * removed));
            Arm k3 = Run("K3 Set, матрица без каналов вылета", rd, rd.EnergySpectrum, peaks, nuclides, CompositionPath.Set, gutted, null);
            k3.Print(p0, false);
            double m0 = baseMatrix.ModelInZone(p0.ChSe), m3 = k3.ModelInZone(p0.ChSe);
            double d0 = baseMatrix.DiffInZone(p0.ChSe), d3 = k3.DiffInZone(p0.ChSe);
            double c0 = baseMatrix.ContinuumInZone(p0.ChSe), c3 = k3.ContinuumInZone(p0.ChSe);
            double se3 = k3.ChannelSum("Tl-208", Se);
            Console.WriteLine("K3\tв зоне SE: канал SE всего {0} → {1}; сплайн {2} → {3}; данные−модель {4} → {5}; модель {6} → {7}",
                              F(baseMatrix.ChannelSum("Tl-208", Se)), F(se3), F(c0), F(c3), F(d0), F(d3), F(m0), F(m3));
            Console.WriteLine("K3\tχ²/кан зоны SE: {0} → {1}", F3(baseMatrix.ChiInZone(p0.ChSe)), F3(k3.ChiInZone(p0.ChSe)));
            Check("K3: каналы пусты, и вынутое в зоне SE берёт сплайн или невязка", se3 == 0.0 && (c3 - c0) + (d3 - d0) > 0.0);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ИТОГ: все контроли сработали" : "ИТОГ: НЕ СРАБОТАЛО контролей: " + bad);
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // разбор — теми же движениями, что FsaAnalysisSession
        // ------------------------------------------------------------------

        static FsaSampleSpec DeclaredSpec(ResultData rd, NuclideDefinitionManager nuclides, FsaCalculationOptions options)
        {
            List<FsaSampleChain> chains;
            List<string> list;
            FsaMeasurementResult.DeclaredOf(nuclides.ActiveSet, nuclides.NuclideDefinitions, out chains, out list);
            FsaSampleSpec spec = FsaSampleSpec.Declared(rd, chains, list, options.ChainEquilibrium, options.AtomicXray);
            options.ApplyTo(spec);
            return spec;
        }

        static Arm Run(string name, ResultData rd, EnergySpectrum spectrum, List<Peak> peaks,
                       NuclideDefinitionManager nuclides, CompositionPath path, ResponseMatrix matrix, Action<FsaAnalyzer> tune)
        {
            var options = new FsaCalculationOptions
            {
                DbLookups = path != CompositionPath.Labels,
                FromSet = path == CompositionPath.Set
            };
            return RunWith(name, rd, spectrum, peaks, nuclides, path, matrix, tune, options);
        }

        static Arm RunWith(string name, ResultData rd, EnergySpectrum spectrum, List<Peak> peaks,
                           NuclideDefinitionManager nuclides, CompositionPath path, ResponseMatrix matrix,
                           Action<FsaAnalyzer> tune, FsaCalculationOptions options)
        {
            List<FsaComponent> library;
            string composition;
            if (path == CompositionPath.Set)
            {
                library = FsaSampleLibrary.Build(DeclaredSpec(rd, nuclides, options));
                composition = "из сета";
            }
            else if (path == CompositionPath.Labels)
            {
                // Тем же вызовом, что `FsaAnalysisSession.Compute` (`AMBER306`).
                FsaSampleSpec labelSpec = FsaSampleSpec.Declared(rd, null, null, options.ChainEquilibrium, options.AtomicXray);
                options.ApplyTo(labelSpec);
                FsaLibrary.LabelReport labels;
                library = FsaLibrary.BuildFromLabels(peaks, nuclides.NuclideDefinitions, labelSpec, out labels);
                composition = "по подписям пиков: " + labels;
            }
            else
            {
                FsaCompositionInference.Report report;
                FsaSampleSpec spec = FsaCompositionInference.Infer(peaks, rd, out report);
                options.ApplyTo(spec);
                library = FsaSampleLibrary.Build(spec);
                composition = "из NucBase: " + report;
            }

            var analyzer = new FsaAnalyzer();
            options.ApplyTo(analyzer);
            // (`AMBER305`) склад физики 30 без раздела электрона: как корпус `--sample-beta=0`
            analyzer.SampleBeta = false;
            analyzer.AdoptSpectrum(rd);
            if (matrix != null)
            {
                FsaMatrixBinding.Bind(analyzer, rd.Efficiency.Geometry, matrix);
            }

            if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
            {
                double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
            }

            if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            if (tune != null)
            {
                tune(analyzer);
            }

            // (`T101`, `T243`) ЧЕМ СЧИТАЛИ ЭТОТ РАЗБОР — вслух и до счёта.
            FsaTuningReport.Print(analyzer, name);
            FsaResult result = analyzer.Analyze(spectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("{0}: разбор отказал: {1} {2}", name, analyzer.Refusal, analyzer.RefusalNote);
                bad++;
                return null;
            }

            return new Arm(name, composition, rd, spectrum, library, result, matrix != null);
        }

        // ------------------------------------------------------------------
        // где стоят пики: положение берётся у модели, из канала отклика
        // ------------------------------------------------------------------

        sealed class Positions
        {
            public int ChPeak, ChSe, ChDe, Ch511;

            public static Positions Of(Arm withMatrix)
            {
                var p = new Positions();
                FsaComponentResult tl = withMatrix.Component("Tl-208");
                if (tl == null || tl.ChannelCurves == null)
                {
                    throw new InvalidOperationException("у разбора с матрицей нет слоя Tl-208 с каналами");
                }

                p.ChPeak = ArgMax(tl.ChannelCurves[PeakCh], withMatrix.ChannelOf(2000.0), tl.Curve.Length - 20);
                p.ChSe = ArgMax(tl.ChannelCurves[Se], withMatrix.ChannelOf(1800.0), p.ChPeak);
                p.ChDe = ArgMax(tl.ChannelCurves[De], withMatrix.ChannelOf(1300.0), p.ChSe);
                p.Ch511 = (int)Math.Round(withMatrix.NominalChannel(AnnKev));
                Console.WriteLine("POS\t{0}\tмодель ставит: пик 2614.5 на канал {1} ({2} кэВ оси); SE на {3} ({4}); DE на {5} ({6}); 511 на {7} ({8})",
                                  withMatrix.Name, p.ChPeak, F(withMatrix.AxisKev(p.ChPeak)), p.ChSe, F(withMatrix.AxisKev(p.ChSe)),
                                  p.ChDe, F(withMatrix.AxisKev(p.ChDe)), p.Ch511, F(withMatrix.AxisKev(p.Ch511)));
                return p;
            }

            static int ArgMax(double[] y, double from, int to)
            {
                int best = (int)from;
                double top = double.MinValue;
                const int half = 15;
                for (int i = (int)from; i <= to && i + half < y.Length; i++)
                {
                    double s = 0.0;
                    for (int k = i - half; k <= i + half; k++)
                    {
                        s += y[k];
                    }

                    if (s > top)
                    {
                        top = s;
                        best = i;
                    }
                }

                return best;
            }
        }

        // ------------------------------------------------------------------
        // одна запись разбора
        // ------------------------------------------------------------------

        sealed class Arm
        {
            public readonly string Name;
            readonly string composition;
            readonly ResultData rd;
            readonly EnergyCalibration calibration;
            readonly List<FsaComponent> library;
            public readonly FsaResult Result;
            public readonly double[] Net;
            public readonly double[] ModelFit;
            readonly double[] variance;
            readonly bool withMatrix;

            public Arm(string name, string composition, ResultData rd, EnergySpectrum spectrum, List<FsaComponent> library,
                       FsaResult result, bool withMatrix)
            {
                this.Name = name;
                this.composition = composition;
                this.rd = rd;
                this.calibration = spectrum.EnergyCalibration;
                this.library = library;
                this.Result = result;
                this.Net = result.FitSpectrum(spectrum.Spectrum);
                this.ModelFit = result.FitModel();
                this.variance = result.FitVariance;
                this.withMatrix = withMatrix;
            }

            public bool LibraryHas(string name)
            {
                foreach (FsaComponent c in this.library)
                {
                    if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }

            public FsaComponentResult Component(string name)
            {
                foreach (FsaComponentResult c in this.Result.Components)
                {
                    if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return c;
                    }
                }

                return null;
            }

            public double ChannelOf(double kev)
            {
                return this.calibration.EnergyToChannel(kev, this.Net.Length);
            }

            public double AxisKev(double channel)
            {
                return this.calibration.ChannelToEnergy(channel + 0.5);
            }

            /// <summary>Канал линии при сетке усиления разбора: g·кан(E) + сдвиг.</summary>
            public double NominalChannel(double kev)
            {
                return this.Result.Gain * this.ChannelOf(kev) + this.Result.OffsetChannels;
            }

            public double SigmaChannels(double channel)
            {
                return this.rd.FwhmCalibration.ChannelToFwhm(channel) / 2.3548;
            }

            /// <summary>
            /// Пиковые отсчёты колонок SE-/DE- ОТ САМОГО СИЛЬНОГО РОДИТЕЛЯ (имена 2614/2615).
            /// ⚠ Не сумма ленты: при снятом гейте свободная колонка идёт через матрицу как
            /// линия своей энергии и несёт ещё и свой комптон (лента 30157, пик 7373).
            /// </summary>
            public double ColumnArea(string prefix)
            {
                double sum = 0.0;
                foreach (FsaComponentResult c in this.Result.Components)
                {
                    if (c.Name.StartsWith(prefix + "2614", StringComparison.OrdinalIgnoreCase)
                        || c.Name.StartsWith(prefix + "2615", StringComparison.OrdinalIgnoreCase))
                    {
                        sum += c.PeakCounts;
                    }
                }

                return sum;
            }

            /// <summary>Пиковые отсчёты колонки по точному имени; 0 — её нет в результате.</summary>
            public double PeakCountsOf(string name)
            {
                FsaComponentResult c = this.Component(name);
                return c != null ? c.PeakCounts : 0.0;
            }

            public double ChannelSum(string component, int channel)
            {
                FsaComponentResult c = this.Component(component);
                return c != null && c.ChannelCurves != null && channel < c.ChannelCurves.Length && c.ChannelCurves[channel] != null
                    ? Sum(c.ChannelCurves[channel], 0, c.ChannelCurves[channel].Length - 1)
                    : 0.0;
            }

            void Window(double center, double sigmas, out int lo, out int hi)
            {
                double sigma = this.SigmaChannels(center);
                lo = Math.Max(0, (int)Math.Round(center - sigmas * sigma));
                hi = Math.Min(this.Net.Length - 1, (int)Math.Round(center + sigmas * sigma));
            }

            public double ModelInZone(double center)
            {
                int lo, hi;
                this.Window(center, 3.0, out lo, out hi);
                return Sum(this.ModelFit, lo, hi);
            }

            public double DiffInZone(double center)
            {
                int lo, hi;
                this.Window(center, 3.0, out lo, out hi);
                return Sum(this.Net, lo, hi) - Sum(this.ModelFit, lo, hi);
            }

            public string Summary(Positions pos)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "χ²/ndf {0}; SE данные/модель {1}/{2}; пик данные/модель {3}/{4}; зона пика данные−модель {5} ({6}σ), χ²/кан {7}; DE χ²/кан {8}",
                    F3(this.Result.Chi2Ndf), F(this.Gauss(this.Net, pos.ChSe)), F(this.Gauss(this.ModelFit, pos.ChSe)),
                    F(this.Gauss(this.Net, pos.ChPeak)), F(this.Gauss(this.ModelFit, pos.ChPeak)),
                    F(this.DiffInZone(pos.ChPeak)), F(this.DiffSigma(pos.ChPeak)), F3(this.ChiInZone(pos.ChPeak)), F3(this.ChiInZone(pos.ChDe)));
            }

            double DiffSigma(double center)
            {
                int lo, hi;
                this.Window(center, 3.0, out lo, out hi);
                double var = Sum(this.variance, lo, hi);
                return var > 0.0 ? this.DiffInZone(center) / Math.Sqrt(var) : 0.0;
            }

            public double ChiInZone(double center)
            {
                int lo, hi;
                this.Window(center, 3.0, out lo, out hi);
                double chi = 0.0;
                for (int i = lo; i <= hi; i++)
                {
                    double d = this.Net[i] - this.ModelFit[i];
                    if (this.variance[i] > 0.0)
                    {
                        chi += d * d / this.variance[i];
                    }
                }

                return chi / Math.Max(1, hi - lo + 1);
            }

            /// <summary>Гаусс со свободными центром и шириной над квадратичной подложкой: окно одно на весь перебор.</summary>
            public void FreeWidth(double[] y, double center, out double bestCenter, out double bestSigma, out double bestArea)
            {
                bestCenter = center;
                bestSigma = double.NaN;
                bestArea = double.NaN;
                int lo, hi;
                double calSigma = this.SigmaChannels(center);
                this.Window(center, 4.0, out lo, out hi);
                double top = double.MaxValue;
                const int p = 4;
                for (double f = 0.5; f <= 1.7; f += 0.02)
                {
                    double sigma = calSigma * f;
                    for (double c = center - 1.5 * calSigma; c <= center + 1.5 * calSigma; c += Math.Max(1.0, 0.04 * calSigma))
                    {
                        var a = new double[p, p];
                        var b = new double[p];
                        var row = new double[p];
                        double yy = 0.0;
                        for (int i = lo; i <= hi && i < y.Length; i++)
                        {
                            double t = (i - center) / (4.0 * calSigma);
                            double z = (i + 0.5 - c) / sigma;
                            row[0] = 1.0;
                            row[1] = t;
                            row[2] = t * t;
                            row[3] = Math.Exp(-0.5 * z * z) / (sigma * Math.Sqrt(2.0 * Math.PI));
                            double w = this.variance != null && this.variance[i] > 0.0 ? 1.0 / this.variance[i] : 1.0;
                            yy += w * y[i] * y[i];
                            for (int r = 0; r < p; r++)
                            {
                                b[r] += w * row[r] * y[i];
                                for (int s = 0; s < p; s++)
                                {
                                    a[r, s] += w * row[r] * row[s];
                                }
                            }
                        }

                        var bb = (double[])b.Clone();
                        double[] x = Solve(a, b, p);
                        if (x == null || !(x[3] > 0.0))
                        {
                            continue;
                        }

                        double dot = 0.0;
                        for (int r = 0; r < p; r++)
                        {
                            dot += x[r] * bb[r];
                        }

                        double chi = yy - dot;
                        if (chi < top)
                        {
                            top = chi;
                            bestCenter = c;
                            bestSigma = sigma;
                            bestArea = x[3];
                        }
                    }
                }
            }

            public double ContinuumInZone(double center)
            {
                int lo, hi;
                this.Window(center, 3.0, out lo, out hi);
                return Sum(this.Result.FitContinuum(), lo, hi);
            }

            /// <summary>
            /// Площадь гаусса (ширина — по калибровке FWHM) над квадратичной подложкой,
            /// МНК с весом 1/дисперсия в окне ±3.5σ; центр фиксирован.
            /// </summary>
            public double Gauss(double[] y, double center, out double chi2)
            {
                chi2 = double.NaN;
                if (y == null)
                {
                    return 0.0;
                }

                int lo, hi;
                this.Window(center, 3.5, out lo, out hi);
                double sigma = this.SigmaChannels(center);
                const int p = 4;
                var a = new double[p, p];
                var b = new double[p];
                var row = new double[p];
                double yy = 0.0;
                for (int i = lo; i <= hi && i < y.Length; i++)
                {
                    double t = (i - center) / (3.5 * sigma);
                    row[0] = 1.0;
                    row[1] = t;
                    row[2] = t * t;
                    double z = (i + 0.5 - center) / sigma;
                    row[3] = Math.Exp(-0.5 * z * z) / (sigma * Math.Sqrt(2.0 * Math.PI));
                    double w = this.variance != null && i < this.variance.Length && this.variance[i] > 0.0 ? 1.0 / this.variance[i] : 1.0;
                    yy += w * y[i] * y[i];
                    for (int r = 0; r < p; r++)
                    {
                        b[r] += w * row[r] * y[i];
                        for (int c = 0; c < p; c++)
                        {
                            a[r, c] += w * row[r] * row[c];
                        }
                    }
                }

                var bb = (double[])b.Clone();
                double[] x = Solve(a, b, p);
                if (x == null)
                {
                    return double.NaN;
                }

                double dot = 0.0;
                for (int r = 0; r < p; r++)
                {
                    dot += x[r] * bb[r];
                }

                chi2 = yy - dot;
                return x[3];
            }

            public double Gauss(double[] y, double center)
            {
                double chi2;
                return this.Gauss(y, center, out chi2);
            }

            /// <summary>Гаусс со свободным центром ±span каналов: лучший по χ² на канал.</summary>
            public double FreeGauss(double[] y, double center, int span, out double best)
            {
                double top = double.MaxValue, area = double.NaN;
                best = center;
                for (double c = center - span; c <= center + span; c += 5.0)
                {
                    double chi2, s = this.Gauss(y, c, out chi2);
                    double norm = chi2 / (7.0 * this.SigmaChannels(c));
                    if (s > 0.0 && norm < top)
                    {
                        top = norm;
                        area = s;
                        best = c;
                    }
                }

                return area;
            }

            /// <summary>
            /// Насколько данные согласны С ФОРМОЙ канала: сеть = a + b·t +
            /// α·(модель − канал) + β·канал(сдвиг s), МНК в окне ±3σ. Печатает β при
            /// s = 0 и лучший сдвиг s*.
            /// </summary>
            public void ScaleTest(string title, double[] channelCurve, int center)
            {
                int lo, hi;
                this.Window(center, 3.0, out lo, out hi);
                double keVPerCh = Math.Abs(this.AxisKev(center + 1) - this.AxisKev(center));
                double bestChi = double.MaxValue, bestBeta = double.NaN, bestAlpha = double.NaN;
                int bestShift = 0;
                double beta0 = double.NaN, alpha0 = double.NaN, chi0 = double.NaN, err0 = double.NaN;
                double spread = 3.0 * this.SigmaChannels(center);
                for (int s = -200; s <= 200; s += 5)
                {
                    const int p = 4;
                    var a = new double[p, p];
                    var b = new double[p];
                    var row = new double[p];
                    double yy = 0.0;
                    for (int i = lo; i <= hi; i++)
                    {
                        double w = this.variance != null && this.variance[i] > 0.0 ? 1.0 / this.variance[i] : 1.0;
                        int j = i - s;
                        row[0] = 1.0;
                        row[1] = (i - center) / spread;
                        row[2] = this.ModelFit[i] - channelCurve[i];
                        row[3] = j >= 0 && j < channelCurve.Length ? channelCurve[j] : 0.0;
                        yy += w * this.Net[i] * this.Net[i];
                        for (int r = 0; r < p; r++)
                        {
                            b[r] += w * row[r] * this.Net[i];
                            for (int c = 0; c < p; c++)
                            {
                                a[r, c] += w * row[r] * row[c];
                            }
                        }
                    }

                    var bb = (double[])b.Clone();
                    var aa = (double[,])a.Clone();
                    double[] x = Solve(a, b, p);
                    if (x == null)
                    {
                        continue;
                    }

                    double dot = 0.0;
                    for (int r = 0; r < p; r++)
                    {
                        dot += x[r] * bb[r];
                    }

                    double chi = yy - dot;
                    if (chi < bestChi)
                    {
                        bestChi = chi;
                        bestShift = s;
                        bestBeta = x[3];
                        bestAlpha = x[2];
                    }

                    if (s == 0)
                    {
                        beta0 = x[3];
                        alpha0 = x[2];
                        chi0 = chi;
                        err0 = Math.Sqrt(Math.Max(0.0, InverseDiag(aa, p, 3)));
                    }
                }

                int ndf = hi - lo + 1 - 4;
                Console.WriteLine("SCALE\t{0}\tкан. {1}\tβ {2} ± {3}\tα {4}\tχ²/ndf окна {5}\t→ лучший сдвиг {6} кан. ({7} кэВ оси): β {8}\tα {9}\tχ²/ndf {10}",
                                  title, center, F3(beta0), F3(err0), F3(alpha0), F3(chi0 / ndf), bestShift, F(bestShift * keVPerCh),
                                  F3(bestBeta), F3(bestAlpha), F3(bestChi / ndf));
            }

            public void Print(Positions pos, bool full)
            {
                Console.WriteLine();
                Console.WriteLine("=== {0} ===", this.Name);
                Console.WriteLine("ARM\tсостав {0}", this.composition);
                Console.WriteLine("ARM\tχ²/ndf {0}\tневязка ε {1} %\tусиление {2}\tсдвиг {3} кан.\tматрица в разборе {4}",
                                  F3(this.Result.Chi2Ndf), F(this.Result.ModelResidual * 100.0),
                                  this.Result.Gain.ToString("F6", CultureInfo.InvariantCulture),
                                  F3(this.Result.OffsetChannels), this.withMatrix ? "да" : "нет");

                foreach (FsaComponent c in this.library)
                {
                    if (FsaLibrary.IsEscapeImage(c.Name) || FsaResult.IsAnnihilationImage(c.Name) || c.CrystalEscape)
                    {
                        Console.WriteLine("LIB\t{0}\tродитель {1}\tлиния {2} кэВ", c.Name, c.EscapeParent ?? "-",
                                          c.Lines.Count > 0 ? F(c.Lines[0].Energy) : "-");
                    }
                }

                foreach (FsaComponentResult c in this.Result.Components)
                {
                    if (FsaLibrary.IsEscapeImage(c.Name) || FsaResult.IsAnnihilationImage(c.Name)
                        || c.Name.StartsWith("Esc-", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("COMP\t{0}\tотсчётов ленты {1}\tz {2}\tдоля {3} %\tскорость {4}/с\tмакс. на {5} кэВ оси",
                                          c.Name, F(Sum(c.Curve, 0, c.Curve.Length - 1)), F(c.Z), F(c.SharePercent),
                                          c.CountRate.ToString("G5", CultureInfo.InvariantCulture),
                                          F(this.AxisKev(MaxIndex(c.Curve))));
                    }
                }

                if (full)
                {
                    FsaPresentation presentation = FsaPresentationBuilder.Build(this.Result, FsaGrouping.Daughters, false);
                    foreach (FsaReportRow row in presentation.Rows)
                    {
                        Console.WriteLine("ROW\t{0}\t{1}\t{2}", row.Kind, row.Name, (row.Value ?? "").Replace('\n', ' '));
                    }
                }

                // Зоны постановки: энергия ОСИ, как читает человек с графика.
                this.PrintZone("ось 2103.5", this.ChannelOf(SeKev), false);
                this.PrintZone("ось 1592.5", this.ChannelOf(DeKev), false);
                // Зоны по месту: там, куда модель с матрицей кладёт пик.
                this.PrintZone("SE по месту", pos.ChSe, full);
                this.PrintZone("DE по месту", pos.ChDe, full);
                this.PrintZone("511", pos.Ch511, full);
                this.PrintZone("пик 2614.5", pos.ChPeak, full);
                this.Dump();
            }

            void PrintZone(string title, double center, bool full)
            {
                int lo, hi;
                this.Window(center, 3.0, out lo, out hi);
                double net = Sum(this.Net, lo, hi);
                double model = Sum(this.ModelFit, lo, hi);
                double var = Sum(this.variance, lo, hi);
                double continuum = Sum(this.Result.FitContinuum(), lo, hi);
                double chi = 0.0;
                for (int i = lo; i <= hi; i++)
                {
                    double d = this.Net[i] - this.ModelFit[i];
                    if (this.variance[i] > 0.0)
                    {
                        chi += d * d / this.variance[i];
                    }
                }

                Console.WriteLine("ZONE\t{0}\tканал {1} ({2}…{3}), ось {4}…{5} кэВ\tсеть {6}\tмодель {7}\tданные−модель {8} ({9} %, {10}σ)\tχ²/кан {11}\tсплайн {12}",
                                  title, F(center), lo, hi, F(this.AxisKev(lo)), F(this.AxisKev(hi)), F(net), F(model), F(net - model),
                                  F3(100.0 * (net - model) / Math.Max(1.0, net)), F(var > 0.0 ? (net - model) / Math.Sqrt(var) : 0.0),
                                  F3(chi / Math.Max(1, hi - lo + 1)), F(continuum));
                Console.WriteLine("GAUSS\t{0}\tсеть {1}\tмодель {2}\tневязка {3}", title, F(this.Gauss(this.Net, center)),
                                  F(this.Gauss(this.ModelFit, center)), F(this.Gauss(Diff(this.Net, this.ModelFit), center)));
                if (!full)
                {
                    return;
                }

                foreach (FsaComponentResult c in this.Result.Components)
                {
                    double inZone = Sum(c.Curve, lo, hi);
                    if (!(inZone > 0.003 * Math.Max(1.0, net)))
                    {
                        continue;
                    }

                    var text = new StringBuilder();
                    text.AppendFormat(CultureInfo.InvariantCulture, "LAYER\t{0}\t{1}\tв зоне {2} ({3} % зоны)\tгаусс {4}",
                                      title, c.Name, F(inZone), F(100.0 * inZone / Math.Max(1.0, net)), F(this.Gauss(c.Curve, center)));
                    if (c.ChannelCurves != null)
                    {
                        for (int k = 0; k < c.ChannelCurves.Length; k++)
                        {
                            if (c.ChannelCurves[k] == null)
                            {
                                continue;
                            }

                            double s = Sum(c.ChannelCurves[k], lo, hi);
                            if (s > 0.002 * Math.Max(1.0, net))
                            {
                                text.AppendFormat(CultureInfo.InvariantCulture, "\t{0} {1} [г {2}]",
                                                  k < ChannelNames.Length ? ChannelNames[k] : "?" + k, F(s),
                                                  F(this.Gauss(c.ChannelCurves[k], center)));
                            }
                        }
                    }

                    Console.WriteLine(text.ToString());
                }
            }

            void Dump()
            {
                if (outDir == null)
                {
                    return;
                }

                string file = Path.Combine(outDir, Sanitize(this.Name) + ".csv");
                using (var w = new StreamWriter(file, false, new UTF8Encoding(false)))
                {
                    var head = new List<string> { "channel", "kev", "net", "model", "continuum", "variance" };
                    var columns = new List<double[]> { this.Net, this.ModelFit, this.Result.FitContinuum(), this.variance };
                    foreach (FsaComponentResult c in this.Result.Components)
                    {
                        head.Add(c.Name);
                        columns.Add(c.Curve);
                        if (c.ChannelCurves != null)
                        {
                            foreach (int k in new[] { PeakCh, Se, De })
                            {
                                head.Add(c.Name + ":" + ChannelNames[k]);
                                columns.Add(k < c.ChannelCurves.Length ? c.ChannelCurves[k] : null);
                            }
                        }
                    }

                    w.WriteLine(string.Join(",", head.ToArray()));
                    for (int i = 0; i < this.Net.Length; i++)
                    {
                        var row = new StringBuilder();
                        row.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',')
                           .Append(this.AxisKev(i).ToString("F2", CultureInfo.InvariantCulture));
                        foreach (double[] col in columns)
                        {
                            row.Append(',').Append(col != null && i < col.Length ? col[i].ToString("G6", CultureInfo.InvariantCulture) : "0");
                        }

                        w.WriteLine(row.ToString());
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // сверка отношений SE/пик, DE/пик; данные против формы канала
        // ------------------------------------------------------------------

        static void Ratios(Arm free, Arm withMatrix, Positions pos)
        {
            Console.WriteLine();
            Console.WriteLine("=== отношения к пику 2614.5: {0} / {1} ===", free.Name, withMatrix.Name);
            double chPk, chSeData, chDeData;
            double dataPk = withMatrix.FreeGauss(withMatrix.Net, pos.ChPeak, 150, out chPk);
            double dataSe = withMatrix.FreeGauss(withMatrix.Net, pos.ChSe, 150, out chSeData);
            double dataDe = withMatrix.FreeGauss(withMatrix.Net, pos.ChDe, 150, out chDeData);
            Console.WriteLine("RATIO\tданные (гаусс, свободный центр)\tпик {0} на кан. {1} ({2} кэВ оси)\tSE {3} на кан. {4} ({5}), к пику {6}\tу DE {7} на кан. {8} ({9}), вместе с Ac-228 1588/1631; к пику {10}",
                              F(dataPk), F(chPk), F(withMatrix.AxisKev(chPk)), F(dataSe), F(chSeData), F(withMatrix.AxisKev(chSeData)), F3(dataSe / dataPk),
                              F(dataDe), F(chDeData), F(withMatrix.AxisKev(chDeData)), F3(dataDe / dataPk));
            Console.WriteLine("RATIO\tданные минус положение модели, кан.\tпик {0}\tSE {1}\tDE {2}",
                              F(chPk - pos.ChPeak), F(chSeData - pos.ChSe), F(chDeData - pos.ChDe));

            FsaComponentResult tlFreeLayer = free.Component("Tl-208");
            double chFreePeak = free.NominalChannel(Parent);
            double tlFree = free.Gauss(tlFreeLayer != null ? tlFreeLayer.Curve : null, chFreePeak);
            double colSe = free.ColumnArea("SE-");
            double colDe = free.ColumnArea("DE-");
            Console.WriteLine("RATIO\tбез матрицы: колонки\tTl-208 пик (гаусс) {0} на кан. {1}\tSE-2615 {2} (к пику {3}), её место кан. {4} ({5} кэВ оси)\tDE-2615 {6} (к пику {7}), её место кан. {8} ({9} кэВ оси)",
                              F(tlFree), F(chFreePeak), F(colSe), F3(colSe / tlFree),
                              F(free.NominalChannel(SeKev)), F(free.AxisKev(free.NominalChannel(SeKev))),
                              F(colDe), F3(colDe / tlFree),
                              F(free.NominalChannel(DeKev)), F(free.AxisKev(free.NominalChannel(DeKev))));

            // Колонки от линий Ac-228: столько ли они берут, сколько вылета у этих линий вообще может быть.
            FsaComponentResult acFree = free.Component("Ac-228");
            double ac1588 = free.Gauss(acFree != null ? acFree.Curve : null, free.NominalChannel(1588.2));
            double ac1631 = free.Gauss(acFree != null ? acFree.Curve : null, free.NominalChannel(1630.627));
            Console.WriteLine("RATIO\tбез матрицы: сток?\tAc-228 у 1588 (гаусс) {0}, у 1631 {1}\tSE-1588 {2}\tDE-1588 {3}\tSE-1631 {4}\tDE-1631 {5}",
                              F(ac1588), F(ac1631), F(free.PeakCountsOf("SE-1588")), F(free.PeakCountsOf("DE-1588")),
                              F(free.PeakCountsOf("SE-1631")), F(free.PeakCountsOf("DE-1631")));

            FsaComponentResult tl = withMatrix.Component("Tl-208");
            double pk = withMatrix.Gauss(tl.ChannelCurves[PeakCh], pos.ChPeak);
            double se = withMatrix.Gauss(tl.ChannelCurves[Se], pos.ChSe);
            double de = withMatrix.Gauss(tl.ChannelCurves[De], pos.ChDe);
            Console.WriteLine("RATIO\tс матрицей: слой Tl-208, каналы\tпик (гаусс) {0}, весь канал {1}\tSE (гаусс) {2}, к пику {3}, весь канал {4}\tDE (гаусс) {5}, к пику {6}, весь канал {7}",
                              F(pk), F(withMatrix.ChannelSum("Tl-208", PeakCh)), F(se), F3(se / pk), F(withMatrix.ChannelSum("Tl-208", Se)),
                              F(de), F3(de / pk), F(withMatrix.ChannelSum("Tl-208", De)));
            Console.WriteLine("RATIO\tс матрицей: модель / данные (гаусс по месту)\tSE {0} / {1}\tDE {2} / {3}\tпик {4} / {5}",
                              F(withMatrix.Gauss(withMatrix.ModelFit, pos.ChSe)), F(withMatrix.Gauss(withMatrix.Net, pos.ChSe)),
                              F(withMatrix.Gauss(withMatrix.ModelFit, pos.ChDe)), F(withMatrix.Gauss(withMatrix.Net, pos.ChDe)),
                              F(withMatrix.Gauss(withMatrix.ModelFit, pos.ChPeak)), F(withMatrix.Gauss(withMatrix.Net, pos.ChPeak)));
        }

        // ------------------------------------------------------------------
        // приёмка строк вылета: тот же судья на настоящем разборе и на трёх мутантах
        // ------------------------------------------------------------------

        static void RowsAcceptance(Arm on, Arm free, Arm off)
        {
            Console.WriteLine();
            Console.WriteLine("=== строки вылета под нуклидом: {0} ===", on.Name);
            Positions pos = Positions.Of(on);
            foreach (FsaComponentResult c in on.Result.Components)
            {
                if (c.EscapeLines == null)
                {
                    continue;
                }

                foreach (FsaEscapeLine line in c.EscapeLines)
                {
                    Console.WriteLine("ESC\t{0}\t{1}\tлиния {2}\tна {3} кэВ\tканал {4} ({5} кэВ оси)\t{6} отсч.",
                                      c.Name, line.IsDouble ? "DE" : "SE", F(line.ParentKev), F(line.EnergyKev),
                                      F(line.Channel), F(on.AxisKev(line.Channel)), F(line.Counts));
                }

                Console.WriteLine("ESCSUM\t{0}\tподслой {1}\tстрок всего {2}\tканалы SE+DE {3}", c.Name,
                                  F(Sum(c.EscapePeakCurve, 0, c.EscapePeakCurve != null ? c.EscapePeakCurve.Length - 1 : -1)),
                                  F(TotalCounts(c.EscapeLines)), F(on.ChannelSum(c.Name, Se) + on.ChannelSum(c.Name, De)));
            }

            foreach (FsaGrouping grouping in new[] { FsaGrouping.Daughters, FsaGrouping.Parents })
            {
                foreach (FsaReportRow row in FsaPresentationBuilder.Build(on.Result, grouping, false).Rows)
                {
                    if (row.Kind == FsaReportRowKind.EscapePeaks || row.Kind == FsaReportRowKind.EscapePeakLine)
                    {
                        Console.WriteLine("ESCROW\t{0}\t{1}\t{2}\t{3}", grouping, row.Kind, row.Name, row.Value);
                    }
                }
            }

            double seCenter, seSigma, seArea;
            on.FreeWidth(on.Component("Tl-208").ChannelCurves[Se], pos.ChSe, out seCenter, out seSigma, out seArea);
            Console.WriteLine("ESCSUM\tканал SE Tl-208, гаусс свободной ширины: площадь {0}, σ {1} кан.", F(seArea), F(seSigma));
            int honest = JudgeRows(on, free, off, pos, seArea, true);
            Check("строки вылета: настоящий разбор чист", honest == 0);

            // Мутант 1: строки вылета теряются.
            var savedLines = new Dictionary<FsaComponentResult, List<FsaEscapeLine>>();
            var savedCurves = new Dictionary<FsaComponentResult, double[]>();
            foreach (FsaComponentResult c in on.Result.Components)
            {
                savedLines[c] = c.EscapeLines;
                savedCurves[c] = c.EscapePeakCurve;
                c.EscapeLines = null;
                c.EscapePeakCurve = null;
            }

            int lost = JudgeRows(on, free, off, pos, seArea, false);
            Console.WriteLine("МУТАНТ\tстроки вылета теряются\tнаходок {0}", lost);
            Check("мутант «строки вылета теряются» пойман", lost > 0);
            foreach (FsaComponentResult c in on.Result.Components)
            {
                c.EscapeLines = savedLines[c];
                c.EscapePeakCurve = savedCurves[c];
            }

            // Мутант 2: галка не отключает.
            var offLines = new Dictionary<FsaComponentResult, List<FsaEscapeLine>>();
            foreach (FsaComponentResult c in off.Result.Components)
            {
                offLines[c] = c.EscapeLines;
                FsaComponentResult twin = on.Component(c.Name);
                c.EscapeLines = twin != null ? twin.EscapeLines : null;
            }

            int ignored = JudgeRows(on, free, off, pos, seArea, false);
            Console.WriteLine("МУТАНТ\tгалка не отключает\tнаходок {0}", ignored);
            Check("мутант «галка не отключает» пойман", ignored > 0);
            foreach (FsaComponentResult c in off.Result.Components)
            {
                c.EscapeLines = offLines[c];
            }

            // Мутант 3: двойной счёт — подслой сложен с лентой ещё раз.
            FsaComponentResult tl = on.Component("Tl-208");
            double[] savedCurve = tl.Curve;
            double[] doubled = (double[])tl.Curve.Clone();
            for (int i = 0; i < doubled.Length && tl.EscapePeakCurve != null && i < tl.EscapePeakCurve.Length; i++)
            {
                doubled[i] += tl.EscapePeakCurve[i];
            }

            tl.Curve = doubled;
            int twice = JudgeRows(on, free, off, pos, seArea, false);
            Console.WriteLine("МУТАНТ\tдвойной счёт\tнаходок {0}", twice);
            Check("мутант «двойной счёт» пойман", twice > 0);
            tl.Curve = savedCurve;
        }

        /// <summary>Канал места колонки при матрице: по SE и DE линии 2614.5 этого же плеча (карта каналов и усиление — его).</summary>
        static double ColumnChannelByMap(Arm arm, double kev)
        {
            FsaComponentResult tl = arm.Component("Tl-208");
            FsaEscapeLine se = null, de = null;
            if (tl != null && tl.EscapeLines != null)
            {
                foreach (FsaEscapeLine line in tl.EscapeLines)
                {
                    if (Math.Abs(line.ParentKev - Parent) < 1.0)
                    {
                        if (line.IsDouble) de = line; else se = line;
                    }
                }
            }

            if (se == null || de == null)
            {
                return arm.NominalChannel(kev);
            }

            return de.Channel + (kev - de.EnergyKev) * (se.Channel - de.Channel) / (se.EnergyKev - de.EnergyKev);
        }

        // ------------------------------------------------------------------
        // (критик 06.11.2026) Н1: шкала и состав; Н2: ширина пика
        // ------------------------------------------------------------------

        /// <summary>Опора у энергии: модель, замер, отношение ширин; печать одной строкой.</summary>
        static string AnchorText(FsaResult result, double kev)
        {
            foreach (FsaScaleAnchor anchor in result.ScaleAnchors)
            {
                if (Math.Abs(anchor.LineKev - kev) < 3.0)
                {
                    return string.Format(CultureInfo.InvariantCulture, "опора {0}: модель {1}, замер {2}, Δ {3}, ширина данные/ядро {4}, использ. {5}",
                                         F(anchor.LineKev), F(anchor.ModelKev), F(anchor.MeasuredKev), F(anchor.MeasuredKev - anchor.ModelKev),
                                         F3(anchor.WidthRatio), anchor.Used);
                }
            }

            return "опоры у " + F(kev) + " нет";
        }

        /// <summary>Н1: что сдвигает усиление — состав. Плечи: сет; ряд манифеста; ряд и защита Pb (как у корпуса).</summary>
        static void ScaleStudy(ResultData rd, ResponseMatrix matrix, NuclideDefinitionManager nuclides)
        {
            Console.WriteLine();
            Console.WriteLine("=== Н1: шкала против состава ===");
            List<FsaSampleChain> setChains;
            List<string> setNuclides;
            FsaMeasurementResult.DeclaredOf(nuclides.ActiveSet, nuclides.NuclideDefinitions, out setChains, out setNuclides);
            var chainOnly = new List<FsaSampleChain> { FsaSampleChain.FromLabel("Th-232") };
            var arms = new[]
            {
                new { Name = "сет как есть", Chains = setChains, Nuclides = setNuclides, Shield = false, K = false },
                new { Name = "сет + защита Pb", Chains = setChains, Nuclides = setNuclides, Shield = true, K = false },
                new { Name = "ряд Th-232, как manifest", Chains = chainOnly, Nuclides = new List<string>(), Shield = false, K = false },
                new { Name = "ряд + защита Pb, состав корпуса", Chains = chainOnly, Nuclides = new List<string>(), Shield = true, K = false },
                new { Name = "ряд + K-40", Chains = chainOnly, Nuclides = new List<string> { "40K" }, Shield = false, K = true },
            };
            foreach (var plan in arms)
            {
                var options = new FsaCalculationOptions { DbLookups = true };
                FsaSampleSpec spec = FsaSampleSpec.Declared(rd, plan.Chains, plan.Nuclides, options.ChainEquilibrium, options.AtomicXray);
                options.ApplyTo(spec);
                if (plan.Shield && !spec.ShieldElements.Contains(82))
                {
                    spec.ShieldElements.Add(82);
                }

                List<FsaComponent> library = FsaSampleLibrary.Build(spec);
                var analyzer = new FsaAnalyzer();
                options.ApplyTo(analyzer);
                analyzer.SampleBeta = false;
                analyzer.AdoptSpectrum(rd);
                FsaMatrixBinding.Bind(analyzer, rd.Efficiency.Geometry, matrix);
                if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
                {
                    double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                    analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
                }

                if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
                {
                    analyzer.MinEnergy = peakConfig.Min_Range;
                    analyzer.MaxEnergy = peakConfig.Max_Range;
                }

                FsaTuningReport.Print(analyzer, plan.Name);
                FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration, library,
                                                    FsaEfficiency.FromConfig(rd.Efficiency));
                if (result == null)
                {
                    Console.WriteLine("H1\t{0}\tотказ {1}", plan.Name, analyzer.Refusal);
                    continue;
                }

                Console.WriteLine("H1\t{0}\tусиление {1}\tχ²/ndf {2}\tширина ×{3} по {4} опорам\tопор {5}\t{6}", plan.Name,
                                  result.Gain.ToString("F6", CultureInfo.InvariantCulture), F3(result.Chi2Ndf),
                                  F3(result.WidthScale), result.WidthAnchorsUsed, result.ScaleAnchorsUsed, AnchorText(result, Parent));
            }
        }

        /// <summary>Н2: откуда ширина пика модели: файл (калибровка), множитель опор, ядро, матрица, данные.</summary>
        static void WidthStudy(string title, Arm arm, ResponseMatrix matrix)
        {
            Console.WriteLine();
            Console.WriteLine("=== Н2: ширина пика, {0}: множитель ширины ×{1} по {2} опорам ===", title, F3(arm.Result.WidthScale), arm.Result.WidthAnchorsUsed);
            FsaComponentResult tl = arm.Component("Tl-208");
            int bins = (int)(Parent / matrix.BinKev) + 3;
            var row = new double[bins];
            matrix.AccumulateChannel(row, Parent, 1.0, PeakCh);
            double m0 = 0.0, m1 = 0.0, m2 = 0.0;
            for (int b = 0; b < bins; b++)
            {
                m0 += row[b];
                m1 += row[b] * b;
            }

            m1 /= Math.Max(1e-30, m0);
            for (int b = 0; b < bins; b++)
            {
                m2 += row[b] * (b - m1) * (b - m1);
            }

            Console.WriteLine("H2\tстрока матрицы, канал Peak у 2614.5: rms {0} кэВ при бине {1} кэВ", F3(Math.Sqrt(m2 / Math.Max(1e-30, m0)) * matrix.BinKev), F(matrix.BinKev));
            foreach (double kev in new[] { 238.6, 338.3, 583.2, 911.2, Parent })
            {
                Console.WriteLine("H2\t{0}", AnchorText(arm.Result, kev));
            }

            Positions pos = Positions.Of(arm);
            double[] others = Diff(arm.ModelFit, tl.ChannelCurves[PeakCh]);
            double[] dataPeak = Diff(arm.Net, others);
            foreach (var target in new[] { new { Name = "пик 2614.5", Center = (double)pos.ChPeak }, new { Name = "SE", Center = (double)pos.ChSe } })
            {
                double[] image = target.Name == "SE" ? tl.ChannelCurves[Se] : tl.ChannelCurves[PeakCh];
                double[] data = target.Name == "SE" ? Diff(arm.Net, Diff(arm.ModelFit, image)) : dataPeak;
                double cal = arm.SigmaChannels(target.Center);
                double cNet, sNet, aNet, cModel, sModel, aModel, cImage, sImage, aImage, cData, sData, aData;
                arm.FreeWidth(arm.Net, target.Center, out cNet, out sNet, out aNet);
                arm.FreeWidth(arm.ModelFit, target.Center, out cModel, out sModel, out aModel);
                arm.FreeWidth(image, target.Center, out cImage, out sImage, out aImage);
                arm.FreeWidth(data, target.Center, out cData, out sData, out aData);
                Console.WriteLine("H2\t{0}\tσ калибровки {1} кан., с множителем {2}\tобраз: σ {3} (кан. {4})\tмодель: σ {5} (кан. {6})\tсеть: σ {7} (кан. {8}), площадь {9}\tданные минус прочие: σ {10} (кан. {11})\tмодель/сеть {12}\tобраз/калибровка {13}\tсеть/калибровка {14}",
                                  target.Name, F(cal), F(cal * arm.Result.WidthScale), F(sImage), F(cImage), F(sModel), F(cModel), F(sNet), F(cNet), F(aNet),
                                  F(sData), F(cData), F3(sModel / sNet), F3(sImage / cal), F3(sNet / cal));
            }
        }

        // ------------------------------------------------------------------
        // Amber 06.11.2026, дословно: «Ты же понимаешь, что под SE/DE я имел ввиду ВСЕ SE/DE которые
        // уже детектируются, а не только от Tl-208». Таблица всех линий: строка или названная причина.
        // ------------------------------------------------------------------

        sealed class HonestLine
        {
            public string Layer;
            public FsaEscapeLine Line;
            public double ChannelArea;
            public double GroupCounts;
            public double GroupSigma;
            public bool GroupShown;
            public int GroupSize;
        }

        /// <summary>Вход: строка на спектр «ключ;ряды через запятую;нуклиды через запятую». Склад — `.rmx` по имени кривой.</summary>
        static int EscapeTable(string corpus, string store, string listFile)
        {
            foreach (string raw in File.ReadAllLines(listFile))
            {
                string[] parts = raw.Trim().Split(';');
                if (parts.Length < 3 || parts[0].StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string file = File.Exists(parts[0]) ? parts[0] : Path.Combine(corpus, "spectra", parts[0] + ".xml");
                ResultData rd = Load(file);
                string[] names = parts[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                string[] nuclides = parts[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                ResponseMatrix matrix = ResponseMatrix.Load(Path.Combine(store, rd.Efficiency.Name + ".rmx"));
                if (!matrix.IsValidFor(rd.Efficiency.Geometry))
                {
                    Console.WriteLine("ESCTAB\t{0}\tматрица не годна для геометрии спектра", parts[0]);
                    bad++;
                    continue;
                }

                var options = new FsaCalculationOptions { DbLookups = true };
                FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, names, nuclides, options.ChainEquilibrium, options.AtomicXray);
                options.ApplyTo(spec);
                List<FsaComponent> library = FsaSampleLibrary.Build(spec);
                var analyzer = new FsaAnalyzer();
                options.ApplyTo(analyzer);
                analyzer.SampleBeta = false;
                analyzer.AdoptSpectrum(rd);
                FsaMatrixBinding.Bind(analyzer, rd.Efficiency.Geometry, matrix);
                if (rd.DeviceConfig != null && rd.DeviceConfig.InputDeviceConfig != null)
                {
                    double deadTime = rd.DeviceConfig.InputDeviceConfig.DeadTime();
                    analyzer.CoincidenceWindowSec = deadTime > 0.0 ? deadTime : 0.0;
                }

                if (rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
                {
                    analyzer.MinEnergy = peakConfig.Min_Range;
                    analyzer.MaxEnergy = peakConfig.Max_Range;
                }

                FsaTuningReport.Print(analyzer, parts[0]);
                FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration, library,
                                                    FsaEfficiency.FromConfig(rd.Efficiency));
                if (result == null)
                {
                    Console.WriteLine("ESCTAB\t{0}\tотказ разбора {1}", parts[0], analyzer.Refusal);
                    bad++;
                    continue;
                }

                var arm = new Arm(parts[0], "manifest", rd, rd.EnergySpectrum, library, result, true);
                List<Peak> peaks = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None, null, new List<NuclideDefinition>());
                List<HonestLine> honest = HonestLines(arm, result);
                PrintEscapeTable(parts[0], arm, library, result, honest, peaks);
                int found = JudgeLines(result, honest, true);
                Check(parts[0] + ": у каждой линии с видимым каналом есть строка", found == 0);

                // Мутант: показ только самой сильной линии.
                var saved = new Dictionary<FsaComponentResult, List<FsaEscapeLine>>();
                foreach (FsaComponentResult c in result.Components)
                {
                    saved[c] = c.EscapeLines;
                    if (c.EscapeLines != null && c.EscapeLines.Count > 0)
                    {
                        FsaEscapeLine top = c.EscapeLines[0];
                        foreach (FsaEscapeLine line in c.EscapeLines)
                        {
                            if (line.Counts > top.Counts)
                            {
                                top = line;
                            }
                        }

                        c.EscapeLines = new List<FsaEscapeLine> { top };
                    }
                }

                int mutated = JudgeLines(result, honest, false);
                Console.WriteLine("МУТАНТ\t{0}\tпоказ только самой сильной линии\tнаходок {1}", parts[0], mutated);
                if (honest.Exists(h => h.GroupShown))
                {
                    int shownLines = honest.FindAll(h => h.GroupShown).Count;
                    Check(parts[0] + ": мутант «только самая сильная линия» пойман", mutated > 0 || shownLines <= 1);
                }

                // Мутант на вторую ветвь судьи: порог спрятал всё, заметных групп нет, строк нет.
                // Пик канала от 3σ обязан остаться находкой, когда соседей с именем нет.
                foreach (FsaComponentResult c in result.Components)
                {
                    c.EscapeLines = c.EscapeLines != null ? new List<FsaEscapeLine>() : null;
                }

                List<HonestLine> hiddenAll = honest.ConvertAll(h => new HonestLine
                {
                    Layer = h.Layer, Line = h.Line, ChannelArea = h.ChannelArea, GroupCounts = h.GroupCounts,
                    GroupSigma = h.GroupSigma, GroupShown = false, GroupSize = h.GroupSize
                });
                int hiddenFound = JudgeLines(result, hiddenAll, false);
                Console.WriteLine("МУТАНТ\t{0}\tпорог спрятал все группы\tнаходок {1}", parts[0], hiddenFound);
                if (honest.Exists(h => h.Line.Counts >= FsaPresentationBuilder.SumPeakMinSigmas * h.GroupSigma))
                {
                    Check(parts[0] + ": мутант «порог спрятал всё» пойман", hiddenFound > 0);
                }

                foreach (KeyValuePair<FsaComponentResult, List<FsaEscapeLine>> item in saved)
                {
                    item.Key.EscapeLines = item.Value;
                }
            }

            Console.WriteLine(bad == 0 ? "ИТОГ ТАБЛИЦЫ: сошлось" : "ИТОГ ТАБЛИЦЫ: НЕ СОШЛОСЬ, находок " + bad);
            return bad == 0 ? 0 : 1;
        }

        static List<HonestLine> HonestLines(Arm arm, FsaResult result)
        {
            var list = new List<HonestLine>();
            foreach (FsaComponentResult c in result.Components)
            {
                if (c.EscapeLines == null)
                {
                    continue;
                }

                List<FsaSumPeakGroup> groups = FsaPresentationBuilder.EscapeGroups(result, c.EscapeLines);
                foreach (FsaEscapeLine line in c.EscapeLines)
                {
                    FsaSumPeakGroup owner = groups.Find(g => g.Lines.Contains(line));
                    list.Add(new HonestLine
                    {
                        Layer = c.Name,
                        Line = line,
                        ChannelArea = arm.Gauss(c.ChannelCurves[line.IsDouble ? De : Se], line.Channel),
                        GroupCounts = owner != null ? owner.Counts : 0.0,
                        GroupSigma = owner != null ? owner.Sigma : 0.0,
                        GroupShown = owner != null && owner.Shown,
                        GroupSize = owner != null ? owner.Lines.Count : 0
                    });
                }
            }

            return list;
        }

        /// <summary>
        /// Что на месте линии уже названо строками: отсчёты других линий заметных групп того же слоя и
        /// вида в ±FWHM. Гаусс ±3.5σ меряет весь канал, и пик соседней линии, у которой строка есть,
        /// иначе считался бы непоказанным пиком этой (Bi-214 SE 2192.6 рядом с 2204.2, `G1S24_Ra226_Mar_2`).
        /// </summary>
        static double NamedNearby(HonestLine h, List<HonestLine> honest)
        {
            double named = 0.0;
            foreach (HonestLine other in honest)
            {
                if (!ReferenceEquals(other, h) && other.GroupShown && other.Layer == h.Layer && other.Line.IsDouble == h.Line.IsDouble
                    && Math.Abs(other.Line.Channel - h.Line.Channel) <= h.Line.FwhmChannels)
                {
                    named += other.Line.Counts;
                }
            }

            return named;
        }

        /// <summary>
        /// Находки: линия заметной группы, линия с записью от 3σ или с неназванной площадью канала от 3σ —
        /// без строки, в подсказке которой она названа. Запись нужна отдельно: в тесном ряду гаусс канала
        /// пик не видит (`G1S24_Ra226_Mar`: мутант «порог спрятал всё» без неё давал 0 находок).
        /// </summary>
        static int JudgeLines(FsaResult result, List<HonestLine> honest, bool say)
        {
            List<FsaReportRow> rows = FsaPresentationBuilder.Build(result, FsaGrouping.Daughters, false).Rows;
            int found = 0;
            foreach (HonestLine h in honest)
            {
                double limit = FsaPresentationBuilder.SumPeakMinSigmas * h.GroupSigma;
                double named = NamedNearby(h, honest);
                bool due = h.GroupShown || h.Line.Counts >= limit || h.ChannelArea - named >= limit;
                if (!due)
                {
                    if (say && h.ChannelArea >= limit)
                    {
                        Console.WriteLine("СНЯТО\t{0} {1}{2}: площадь канала {3}, 3σ {4}, у самой линии {5}, соседние заметные линии в ±FWHM несут {6}",
                                          h.Layer, h.Line.IsDouble ? "DE " : "SE ", F(h.Line.ParentKev), F(h.ChannelArea), F(limit), F(h.Line.Counts), F(named));
                    }

                    continue;
                }

                string mark = "линия " + F(h.Line.ParentKev) + " кэВ";
                string kind = h.Line.IsDouble ? "DE " : "SE ";
                bool has = rows.Exists(r => r.Kind == FsaReportRowKind.EscapePeakLine && r.Layer != null && r.Layer.Name == h.Layer
                                            && r.Name.StartsWith(kind, StringComparison.Ordinal) && r.Hint != null && r.Hint.Contains(mark));
                if (!has)
                {
                    found++;
                    if (say)
                    {
                        Console.WriteLine("НАХОДКА\t{0} {1}{2}: площадь канала {3}, 3σ {4}, строки нет", h.Layer, kind, F(h.Line.ParentKev), F(h.ChannelArea), F(3.0 * h.GroupSigma));
                    }
                }
            }

            return found;
        }

        static void PrintEscapeTable(string key, Arm arm, List<FsaComponent> library, FsaResult result, List<HonestLine> honest, List<Peak> peaks)
        {
            honest.Sort((x, y) => y.Line.Counts.CompareTo(x.Line.Counts));
            int disputed = 0;
            foreach (HonestLine h in honest)
            {
                double fwhm = h.Line.FwhmChannels;
                Peak near = null;
                foreach (Peak p in peaks)
                {
                    if (Math.Abs(p.Channel - h.Line.Channel) < 0.5 * fwhm && (near == null || p.SNR > near.SNR))
                    {
                        near = p;
                    }
                }

                string reason = h.GroupShown
                    ? (h.GroupSize > 1 ? "в строке группы из " + h.GroupSize : "своя строка")
                    : "ниже порога 3σ окна";
                bool dispute = !h.GroupShown && near != null && near.SNR >= 5.0;
                if (dispute)
                {
                    disputed++;
                }

                if (h.Line.Counts >= 1.0 || h.GroupShown)
                {
                    Console.WriteLine("ESCTAB\t{0}\t{1}\t{2}\tлиния {3}\tна {4}\tотсч. {5}\tгруппа {6}\t3σ {7}\t{8}\tпик данных рядом {9}{10}",
                                      key, h.Layer, h.Line.IsDouble ? "DE" : "SE", F(h.Line.ParentKev), F(h.Line.EnergyKev), F(h.Line.Counts),
                                      F(h.GroupCounts), F(3.0 * h.GroupSigma), reason,
                                      near != null ? F(near.Energy) + " SNR " + F(near.SNR) : "нет", dispute ? "\tСПОРНО" : "");
                }
            }

            int missing = 0;
            int total = 0;
            foreach (FsaComponent c in library)
            {
                if (c.Kind == FsaComponentKind.Nuisance)
                {
                    continue;
                }

                foreach (FsaLine line in c.Lines)
                {
                    if (!(line.Energy > FsaLibrary.PairThresholdKev) || !(line.Intensity > 0.0))
                    {
                        continue;
                    }

                    total++;
                    if (!honest.Exists(h => Math.Abs(h.Line.ParentKev - line.Energy) < 0.05))
                    {
                        missing++;
                        Console.WriteLine("ESCTAB\t{0}\t{1}\tлиния {2}, выход {3} %\tзаписи нет: нуклид не в результате или канал пуст",
                                          key, line.Nuclide ?? c.Name, F(line.Energy), line.Intensity.ToString("G3", CultureInfo.InvariantCulture));
                    }
                }
            }

            Console.WriteLine("ESCSUM2\t{0}\tлиний выше порога пар в библиотеке {1}\tбез записи {2}\tзаписей SE+DE {3}\tв строках {4}\tспорных {5}",
                              key, total, missing, honest.Count, honest.FindAll(h => h.GroupShown).Count, disputed);
        }

        static double TotalCounts(List<FsaEscapeLine> lines)
        {
            double sum = 0.0;
            foreach (FsaEscapeLine line in lines)
            {
                sum += line.Counts;
            }

            return sum;
        }

        /// <summary>Судья строк вылета; число находок. <paramref name="say"/> — печатать ли их.</summary>
        static int JudgeRows(Arm on, Arm free, Arm off, Positions pos, double channelArea, bool say)
        {
            var bad2 = new List<string>();

            // 1. Строки: у Tl-208 есть SE и DE, энергия — настоящее положение, а не арифметика.
            int seRows = 0, deRows = 0, rows = 0;
            foreach (FsaReportRow row in FsaPresentationBuilder.Build(on.Result, FsaGrouping.Daughters, false).Rows)
            {
                if (row.Kind != FsaReportRowKind.EscapePeakLine)
                {
                    continue;
                }

                rows++;
                if (row.Layer != null && row.Layer.Name == "Tl-208")
                {
                    if (row.Name.StartsWith("SE", StringComparison.Ordinal)) seRows++;
                    if (row.Name.StartsWith("DE", StringComparison.Ordinal)) deRows++;
                }
            }

            if (seRows == 0 || deRows == 0)
            {
                bad2.Add("у Tl-208 нет строк SE и DE: SE " + seRows + ", DE " + deRows);
            }

            // 2. Место и площадь строки SE от 2614.5 — против канала разбора.
            FsaComponentResult tl = on.Component("Tl-208");
            FsaEscapeLine se = null;
            if (tl != null && tl.EscapeLines != null)
            {
                foreach (FsaEscapeLine line in tl.EscapeLines)
                {
                    if (!line.IsDouble && Math.Abs(line.ParentKev - Parent) < 1.0)
                    {
                        se = line;
                    }
                }
            }

            if (se == null)
            {
                bad2.Add("нет записи SE от 2614.5");
            }
            else
            {
                double sigma = on.SigmaChannels(se.Channel);
                if (Math.Abs(se.Channel - pos.ChSe) > 0.5 * sigma)
                {
                    bad2.Add("место SE " + F(se.Channel) + " не у канала отклика " + pos.ChSe);
                }

                // Гаусс с шириной калибровки врёт, когда ядро уже или шире неё (±25 % на этих спектрах);
                // поэтому сверка — с гауссом свободной ширины по самому каналу.
                if (Math.Abs(se.Counts - channelArea) > 0.25 * channelArea)
                {
                    bad2.Add("отсчёты SE " + F(se.Counts) + " против канала (гаусс свободной ширины) " + F(channelArea));
                }

                if (Math.Abs(se.EnergyKev - SeKev) < 1.0)
                {
                    bad2.Add("энергия SE — арифметика E − 511, а не положение");
                }
            }

            // 3. Галка выключена: ни строк, ни подслоя, ни записей.
            foreach (FsaReportRow row in FsaPresentationBuilder.Build(off.Result, FsaGrouping.Daughters, false).Rows)
            {
                if (row.Kind == FsaReportRowKind.EscapePeaks || row.Kind == FsaReportRowKind.EscapePeakLine)
                {
                    bad2.Add("галка выкл, а строка есть: " + row.Name);
                }
            }

            foreach (FsaComponentResult c in off.Result.Components)
            {
                if (c.EscapeLines != null || c.EscapePeakCurve != null)
                {
                    bad2.Add("галка выкл, а у компонента есть вылеты: " + c.Name);
                }
            }

            // 4. Без матрицы — по-прежнему свободные колонки, новых строк нет.
            foreach (FsaReportRow row in FsaPresentationBuilder.Build(free.Result, FsaGrouping.Daughters, false).Rows)
            {
                if (row.Kind == FsaReportRowKind.EscapePeaks || row.Kind == FsaReportRowKind.EscapePeakLine)
                {
                    bad2.Add("без матрицы появилась строка: " + row.Name);
                }
            }

            // 5. Двойного счёта нет: лента по-прежнему равна сумме своих каналов, а подслой —
            // внутри каналов SE и DE; строки не трогают долей.
            foreach (FsaComponentResult c in on.Result.Components)
            {
                if (c.ChannelCurves == null || c.Curve == null)
                {
                    continue;
                }

                double all = 0.0;
                foreach (double[] curve in c.ChannelCurves)
                {
                    all += Sum(curve, 0, curve != null ? curve.Length - 1 : -1);
                }

                double ribbon = Sum(c.Curve, 0, c.Curve.Length - 1);
                if (Math.Abs(all - ribbon) > 1.0E-6 * Math.Max(1.0, ribbon))
                {
                    bad2.Add(c.Name + ": лента " + F(ribbon) + " не равна сумме каналов " + F(all));
                }

                if (c.EscapePeakCurve != null)
                {
                    double hatch = Sum(c.EscapePeakCurve, 0, c.EscapePeakCurve.Length - 1);
                    double channels = on.ChannelSum(c.Name, Se) + on.ChannelSum(c.Name, De);
                    if (hatch > channels * (1.0 + 1.0E-6))
                    {
                        bad2.Add(c.Name + ": подслой вылета " + F(hatch) + " больше каналов SE+DE " + F(channels));
                    }
                }
            }

            if (say)
            {
                foreach (string item in bad2)
                {
                    Console.WriteLine("НАХОДКА\t" + item);
                }
            }

            return bad2.Count;
        }

        static void ScaleTests(Arm withMatrix, Positions pos)
        {
            FsaComponentResult tl = withMatrix.Component("Tl-208");
            withMatrix.ScaleTest("канал SE слоя Tl-208", tl.ChannelCurves[Se], pos.ChSe);
            withMatrix.ScaleTest("канал DE слоя Tl-208", tl.ChannelCurves[De], pos.ChDe);
            withMatrix.ScaleTest("канал Peak слоя Tl-208", tl.ChannelCurves[PeakCh], pos.ChPeak);
        }

        /// <summary>Прямо из матрицы: отклик линии — положение и площадь пиков каналов вылета.</summary>
        static void MatrixTruth(ResponseMatrix matrix, double line)
        {
            int bins = (int)(line / matrix.BinKev) + 3;
            var seRow = new double[bins];
            var deRow = new double[bins];
            var pkRow = new double[bins];
            matrix.AccumulateChannel(seRow, line, 1.0, Se);
            matrix.AccumulateChannel(deRow, line, 1.0, De);
            matrix.AccumulateChannel(pkRow, line, 1.0, PeakCh);
            double pk = Sum(pkRow, 0, bins - 1), se = Sum(seRow, 0, bins - 1), de = Sum(deRow, 0, bins - 1);
            int seAt = MaxIndex(seRow), deAt = MaxIndex(deRow), pkAt = MaxIndex(pkRow);
            double sePeak = Around(seRow, seAt, 10) - Around(seRow, seAt - 30, 10);
            double dePeak = Around(deRow, deAt, 10) - Around(deRow, deAt + 30, 10);
            Console.WriteLine("MATRIX\t{0} на квант: пик {1} на {2} кэВ\tканал SE {3}: пик {4} на {5} кэВ (E−511 = {6}; смещение {7}; к пику {8})\tканал DE {9}: пик {10} на {11} кэВ (E−1022 = {12}; смещение {13}; к пику {14})",
                              F(line), E(pk), F(pkAt * matrix.BinKev), E(se), E(sePeak), F(seAt * matrix.BinKev), F(line - 511.0),
                              F(seAt * matrix.BinKev - (line - 511.0)), F3(sePeak / pk),
                              E(de), E(dePeak), F(deAt * matrix.BinKev), F(line - 1022.0),
                              F(deAt * matrix.BinKev - (line - 1022.0)), F3(dePeak / pk));
        }

        static double Around(double[] row, int c, int half)
        {
            double sum = 0.0;
            for (int b = c - half; b <= c + half; b++)
            {
                if (b >= 0 && b < row.Length)
                {
                    sum += row[b];
                }
            }

            return sum;
        }

        static int MaxIndex(double[] y)
        {
            int at = 0;
            for (int i = 1; y != null && i < y.Length; i++)
            {
                if (y[i] > y[at])
                {
                    at = i;
                }
            }

            return at;
        }

        /// <summary>Вынуть каналы вылета из матрицы В ПАМЯТИ. Возвращает вынутую долю суммы всех строк.</summary>
        static double Gut(ResponseMatrix matrix)
        {
            double total = 0.0, removed = 0.0;
            for (int n = 0; n < matrix.Rows.Length; n++)
            {
                float[] row = matrix.Rows[n];
                foreach (int k in new[] { Se, De })
                {
                    float[] channel = ChannelRow(matrix, k, n);
                    if (channel == null)
                    {
                        continue;
                    }

                    for (int b = 0; b < channel.Length; b++)
                    {
                        removed += channel[b];
                        if (b < row.Length)
                        {
                            row[b] = Math.Max(0f, row[b] - channel[b]);
                        }

                        channel[b] = 0f;
                    }
                }

                for (int b = 0; b < row.Length; b++)
                {
                    total += row[b];
                }
            }

            return removed / Math.Max(1e-30, total + removed);
        }

        static float[] ChannelRow(ResponseMatrix matrix, int channel, int node)
        {
            return matrix.ChannelRows != null && channel < matrix.ChannelRows.Length ? matrix.ChannelRows[channel][node] : null;
        }

        // ------------------------------------------------------------------
        // поиск пиков
        // ------------------------------------------------------------------

        static void PrintPeaks(string title, List<Peak> peaks)
        {
            Console.WriteLine();
            Console.WriteLine("=== пики спектра, подписи: {0}; всего {1} ===", title, peaks.Count);
            foreach (Peak p in peaks)
            {
                var candidates = new List<string>();
                if (p.NuclideCandidates != null)
                {
                    foreach (NuclideDefinition n in p.NuclideCandidates)
                    {
                        candidates.Add(n.Name + "@" + F(n.Energy));
                    }
                }

                Console.WriteLine("PEAK\t{0} кэВ оси\tканал {1}\tSNR {2}\tподпись {3}\tкандидаты {4}",
                                  F(p.Energy), p.Channel, F(p.SNR), p.Nuclide != null ? p.Nuclide.Name : "-",
                                  candidates.Count > 0 ? string.Join("; ", candidates.ToArray()) : "-");
            }
        }

        // ------------------------------------------------------------------
        // подмешивание и мелочи
        // ------------------------------------------------------------------

        static double AddGauss(EnergySpectrum spectrum, FwhmCalibration fwhm, double ch, double area)
        {
            int channels = spectrum.Spectrum.Length;
            double sigma = fwhm.ChannelToFwhm(ch) / 2.3548;
            double added = 0.0;
            for (int i = Math.Max(0, (int)(ch - 6.0 * sigma)); i <= Math.Min(channels - 1, (int)(ch + 6.0 * sigma)); i++)
            {
                double x = (i + 0.5 - ch) / sigma;
                int count = (int)Math.Round(area * Math.Exp(-0.5 * x * x) / (sigma * Math.Sqrt(2.0 * Math.PI)));
                spectrum.Spectrum[i] += count;
                added += count;
            }

            Console.WriteLine("K1\tподмешано {0} отсчётов на канал {1}, σ {2} кан.", F(added), F(ch), F(sigma));
            return added;
        }

        /// <summary>Диагональный элемент обратной матрицы: дисперсия оценки при весах 1/дисперсия.</summary>
        static double InverseDiag(double[,] a, int n, int k)
        {
            var unit = new double[n];
            unit[k] = 1.0;
            double[] x = Solve((double[,])a.Clone(), unit, n);
            return x != null ? x[k] : double.NaN;
        }

        static double[] Solve(double[,] a, double[] b, int n)
        {
            for (int i = 0; i < n; i++)
            {
                int pivot = i;
                for (int r = i + 1; r < n; r++)
                {
                    if (Math.Abs(a[r, i]) > Math.Abs(a[pivot, i]))
                    {
                        pivot = r;
                    }
                }

                if (Math.Abs(a[pivot, i]) < 1e-300)
                {
                    return null;
                }

                if (pivot != i)
                {
                    for (int c = 0; c < n; c++)
                    {
                        double t = a[i, c];
                        a[i, c] = a[pivot, c];
                        a[pivot, c] = t;
                    }

                    double u = b[i];
                    b[i] = b[pivot];
                    b[pivot] = u;
                }

                for (int r = i + 1; r < n; r++)
                {
                    double f = a[r, i] / a[i, i];
                    for (int c = i; c < n; c++)
                    {
                        a[r, c] -= f * a[i, c];
                    }

                    b[r] -= f * b[i];
                }
            }

            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double s = b[i];
                for (int c = i + 1; c < n; c++)
                {
                    s -= a[i, c] * x[c];
                }

                x[i] = s / a[i, i];
            }

            return x;
        }

        static double[] Diff(double[] a, double[] b)
        {
            var d = new double[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                d[i] = a[i] - (b != null && i < b.Length ? b[i] : 0.0);
            }

            return d;
        }

        static double Sum(double[] y, int lo, int hi)
        {
            double s = 0.0;
            for (int i = lo; y != null && i <= hi && i < y.Length; i++)
            {
                s += y[i];
            }

            return s;
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("{0}\t{1}", ok ? "КОНТРОЛЬ ОК" : "⛔ КОНТРОЛЬ НЕ СРАБОТАЛ", what);
            if (!ok)
            {
                bad++;
            }
        }

        static string Sanitize(string name)
        {
            var b = new StringBuilder();
            foreach (char c in name)
            {
                b.Append(char.IsLetterOrDigit(c) ? c : '_');
            }

            return b.ToString();
        }

        static string F(double v)
        {
            return v.ToString("F1", CultureInfo.InvariantCulture);
        }

        static string F3(double v)
        {
            return v.ToString("F3", CultureInfo.InvariantCulture);
        }

        static string E(double v)
        {
            return v.ToString("E3", CultureInfo.InvariantCulture);
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
