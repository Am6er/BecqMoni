using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace FsaChannelViewProbe
{
    /// <summary>
    /// (`AMBER45`, П104 18.09.2026) РЕЖИМ ПОКАЗА СЛОЯ МАТРИЦЫ ОТКЛИКА — комбо
    /// «Matrix layer» окна отчёта и стопка канала на графике: ТОЖДЕСТВА того,
    /// что нарисовано, против того, что лежит в результате разбора.
    ///
    /// Задача Amber 15.09.2026 и описание вида 18.09.2026 («В FSA Report в
    /// группе Display добавить combo box: "Matrix layer". Значение по умолчанию
    /// - All (отображать как это выглядит сейчас) … При его выборе происходит
    /// его отрисовка на спектре»); уточнения вопросником того же дня: стопка
    /// по компонентам, как сейчас; части без каналов спрятать; лента невязки
    /// как есть, по своей галке.
    ///
    ///   fsachannelviewprobe --spectrum=X.xml --sample=137CS[,40K] [--chain=Th-232]
    ///                       [--shield=82] [--out=каталог]
    ///
    /// Стенд приёмки П104 (из `tools\fsa_showcase\wd`):
    ///   FsaChannelViewProbe.exe --spectrum=..\spectra\ASN16_Cs137_house.xml --sample=137CS,40K --shield=82
    ///
    /// ⛔ ПРОБА ОСНАСТКИ: матрица берётся из склада `config\device\response`
    /// рядом с exe (рабочий каталог витрины `tools\fsa_showcase\wd`), состав —
    /// из базы по ключам (`AMBER19`: поставочный список не читается, одиночке
    /// менеджера подложена пустышка, гейт в конце — код 12).
    ///
    /// Что доказывается (вид собирается ОТРАЖЕНИЕМ, как у `FsaStackShot`, кадр
    /// строит настоящий `EnergySpectrumView.BuildFsaFrameData`):
    ///
    ///   0. перечисление `FsaMatrixLayer` привязано к каналам симулятора: число
    ///      каналов и номер каждого;
    ///   (а) «All» = прежняя стопка ТОЧНО: верх каждой ленты равен независимому
    ///      накоплению лент `Curve` слоёв представления (тот же порядок, бит в бит);
    ///   (б) для КАЖДОГО канала: нарисованная лента слоя (разность соседних
    ///      накоплений) = `ChannelCurves[канал]` этого слоя; в стопке нет ни
    ///      одного слоя без каналов; порядок и имена — как у стопки «All» без
    ///      слоёв без каналов; подслой сумм-пиков есть только у канала `Peak`;
    ///   (в) Σ по каналам верхов стопок = верх «All» − подложка − хвост слоёв с
    ///      каналами − ленты слоёв без каналов (тождество `S3`/`S175` на
    ///      уровне картинки); верх полной модели (`fsaModelTop`) при канале
    ///      равен верху стопки «All» бит в бит — лента невязки не двигается;
    ///   (г) спектр БЕЗ матрицы: просьба о канале не исполняется (кадр собран
    ///      как «All», стопка та же), комбо окна погашено с подсказкой-причиной
    ///      и показывает «All», просьба помнится; с матрицей комбо живо и стоит
    ///      на просимом;
    ///   (д) картинка канала отличается от «All» (число закрашенных пикселей),
    ///      каналы `Peak` и `Compton` отличаются между собой.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — ВСЕГДА, без ключа: (1) подмена одного канала
    /// в результате (ChannelCurves[Peak] первого матричного компонента ×1.01
    /// на всей длине) обязана сломать (в) — стопка канала перестаёт сходиться
    /// с лентой; (2) подмена кадра (накопление одного слоя +1 % в стопке
    /// канала) обязана сломать (б). Не сломала — проба красная, что бы ни
    /// показал честный прогон.
    ///
    /// Снимки стопок (PNG) — только в `--out=` (стенд полосы), в git не идут.
    ///
    /// Коды возврата: 0 — сошлось; 1 — не сошлось / опыт негоден; 2 — нечего
    /// читать; 12 — гейт `AMBER19`.
    /// </summary>
    static class Program
    {
        static int bad;

        const double Rel = 1e-9;
        const double Abs = 1e-6;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Application.SetCompatibleTextRenderingDefault(false);
            FsaTuningReport.Snapshot();

            string spectrumPath = null, outDir = null;
            var chains = new List<string>();
            var nuclides = new List<string>();
            // Z элементов защиты (`--shield=82`): образы рентгена защиты строятся по
            // матрице и несут каналы — ещё слои в стопке канала, как у FsaStackShot.
            var shieldZ = new List<int>();
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--chain=", StringComparison.Ordinal)) chains.AddRange(a.Substring(8).Split(','));
                else if (a.StartsWith("--sample=", StringComparison.Ordinal)) nuclides.AddRange(a.Substring(9).Split(','));
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--shield=", StringComparison.Ordinal))
                {
                    foreach (string z in a.Substring(9).Split(','))
                    {
                        if (z.Trim().Length > 0) shieldZ.Add(int.Parse(z.Trim(), CultureInfo.InvariantCulture));
                    }
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null || (chains.Count == 0 && nuclides.Count == 0))
            {
                Console.Error.WriteLine("нужны --spectrum=<файл> и состав: --sample=137CS[,40K] и/или --chain=Th-232");
                return 2;
            }

            // 0. Перечисление привязано к каналам симулятора.
            Console.WriteLine("=== 0. FsaMatrixLayer против ResponseChannel ===");
            Same("число каналов", EfficiencySimulator.ResponseChannelCount, FsaMatrixLayers.Channels.Length);
            for (int c = 0; c < FsaMatrixLayers.Channels.Length; c++)
            {
                Same("номер канала " + FsaMatrixLayers.Channels[c], c, (int)FsaMatrixLayers.Channels[c]);
            }

            Same("All вне номеров каналов", -1, (int)FsaMatrixLayer.All);

            // (`AMBER19`) Пустышка одиночке — ДО первого обращения: код показа
            // (вид, отпечаток сеанса) зовёт менеджер сам.
            PrimeEmptySuppliedLibrary();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр  : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор  : {0}", ProbeDeviceConfig.Attach(rd));

            ResponseMatrix matrix = null;
            if (rd.Efficiency != null && rd.Efficiency.HasGeometry)
            {
                MatrixRefusal refusal;
                int fileFormat;
                ResponseMatrix loaded = ResponseMatrixStore.Load(rd.Efficiency.Guid, out refusal, out fileFormat);
                if (loaded != null && loaded.IsValidFor(rd.Efficiency.Geometry))
                {
                    matrix = loaded;
                }
                else
                {
                    Console.WriteLine("матрица : НЕТ — {0}, формат файла {1}", refusal, fileFormat);
                }
            }

            // ⛔ Положительный контроль входа: без матрицы каналов нет, и все
            // тождества прошли бы на пустоте.
            if (matrix == null)
            {
                Console.Error.WriteLine("⛔ без матрицы каналов нет — проверять нечего, опыт негоден");
                return 1;
            }

            Console.WriteLine("матрица : есть, каналов {0}, бин {1} кэВ",
                              matrix.HasChannels ? matrix.ChannelRows.Length : 0,
                              matrix.BinKev.ToString("F3", CultureInfo.InvariantCulture));

            var nucids = new List<string>();
            foreach (string n in nuclides)
            {
                nucids.Add(NucidOf(n.Trim()));
            }

            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nucids, true, true);
            FsaCalculationOptions.Of(rd).ApplyTo(spec);
            foreach (int z in shieldZ)
            {
                if (!spec.ShieldElements.Contains(z)) spec.ShieldElements.Add(z);
            }

            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            Console.WriteLine("состав  : образов {0} (из базы)", library.Count);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("⛔ библиотека пуста");
                return 1;
            }

            FsaResult withMatrix = Run(rd, library, matrix, "с матрицей");
            FsaResult noMatrix = Run(rd, library, null, "без матрицы");
            if (withMatrix == null || noMatrix == null)
            {
                return 1;
            }

            // Честный прогон: все тождества обязаны сойтись.
            Console.WriteLine();
            Console.WriteLine("=== честный прогон ===");
            int before = bad;
            Judge(rd, withMatrix, noMatrix, outDir, false);
            Console.WriteLine("честный прогон: находок {0}", bad - before);

            // ⛔ Положительный контроль 1: подмена канала в результате → (в) ломается.
            Console.WriteLine();
            Console.WriteLine("=== положительный контроль 1: ChannelCurves[Peak] первого матричного компонента ×1.01 ===");
            FsaResult spoiled = Run(rd, library, matrix, "с матрицей, для подмены");
            if (spoiled == null)
            {
                return 1;
            }

            string planted = PlantChannel(spoiled);
            Console.WriteLine("подсажено: {0}", planted);
            int silent = bad;
            bad = 0;
            int caught = JudgeIdentityC(rd, spoiled);
            bool control1 = caught > 0;
            Console.WriteLine(control1
                ? "  контроль 1 ПОЙМАН: тождество (в) дало {0} находок на подсаженном результате"
                : "  ⛔ КОНТРОЛЬ 1 ПРОВАЛЕН: подсаженный канал не замечен ({0} находок)", caught);
            bad = silent + (control1 ? 0 : 1);

            // ⛔ Положительный контроль 2: подмена кадра → (б) ломается.
            Console.WriteLine();
            Console.WriteLine("=== положительный контроль 2: накопление одного слоя стопки канала Peak +1 % ===");
            silent = bad;
            bad = 0;
            caught = JudgeIdentityB(rd, withMatrix, FsaMatrixLayer.Peak, true);
            bool control2 = caught > 0;
            Console.WriteLine(control2
                ? "  контроль 2 ПОЙМАН: тождество (б) дало {0} находок на подсаженном кадре"
                : "  ⛔ КОНТРОЛЬ 2 ПРОВАЛЕН: подсаженное накопление не замечено ({0} находок)", caught);
            bad = silent + (control2 ? 0 : 1);

            Console.WriteLine();
            Console.WriteLine(bad == 0
                ? "ВСЕ СОШЛИСЬ: стопка канала — те же слои тем же порядком, каждая лента = свой канал; «All» = прежняя стопка; без матрицы режим недостижим; оба контроля пойманы"
                : string.Format(CultureInfo.InvariantCulture, "НЕ СОШЛОСЬ: {0}", bad));
            return SuppliedLibraryGate(bad == 0 ? 0 : 1);
        }

        // ==================================================================
        // Разбор
        // ==================================================================

        static FsaResult Run(ResultData rd, List<FsaComponent> library, ResponseMatrix matrix, string title)
        {
            var analyzer = new FsaAnalyzer();
            FsaCalculationOptions.Of(rd).ApplyTo(analyzer);
            // Матрица — ТЕМ ЖЕ кодом, что в приложении (вещество, признак защиты).
            FsaMatrixBinding.Bind(analyzer, rd.Efficiency != null ? rd.Efficiency.Geometry : null, matrix);
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

            FsaTuningReport.Print(analyzer, title);
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("разложение не получилось: {0}", title);
                bad++;
                return null;
            }

            int withChannels = 0;
            foreach (FsaComponentResult c in result.Components)
            {
                if (c.ChannelCurves != null)
                {
                    withChannels++;
                }
            }

            Console.WriteLine("плечо «{0}»: chi2/ndf {1}, компонентов {2}, из них с каналами {3}, матрица {4}",
                              title, result.Chi2Ndf.ToString("F4", CultureInfo.InvariantCulture),
                              result.Components.Count, withChannels, result.ResponseMatrixUsed ? "учтена" : "нет");
            return result;
        }

        // ==================================================================
        // Вид: собрать кадр отражением, как FsaStackShot
        // ==================================================================

        sealed class Frame
        {
            public EnergySpectrumView View;
            public FsaMatrixLayer Requested;
            public FsaMatrixLayer Built;
            public List<FsaStackLayer> Presentation;
            public List<FsaStackLayer> Stack;
            public double[][] Cumulative;
            public double[][] SumPeakLevel;
            public double[][] StackCurves;
            public double[] ModelTop;
            public double[] Zero;
            public int Channels;
        }

        /// <summary>Вид без формы с готовым результатом в сеансе; кадр собран для слоя.</summary>
        static Frame Build(ResultData rd, FsaResult result, FsaMatrixLayer layer)
        {
            const int width = 1600, height = 700, left = 1;
            EnergySpectrum spectrum = rd.EnergySpectrum;
            EnergyCalibration calibration = spectrum.EnergyCalibration;
            double toKev = calibration.ChannelToEnergy(spectrum.NumberOfChannels - 1);
            double ceiling = 0.0;
            for (int i = 0; i < result.Model.Length; i++)
            {
                if (result.Model[i] > ceiling)
                {
                    ceiling = result.Model[i];
                }
            }

            var view = new EnergySpectrumView();
            Set(view, "energySpectrum", spectrum);
            Set(view, "activeResultData", rd);
            if (rd.BackgroundEnergySpectrum != null)
            {
                Set(view, "backgroundEnergySpectrum", rd.BackgroundEnergySpectrum);
                Set(view, "backgroundEnergyCalibration", rd.BackgroundEnergySpectrum.EnergyCalibration);
                Set(view, "backgroundNumberOfChannels", rd.BackgroundEnergySpectrum.NumberOfChannels);
            }

            Set(view, "energyCalibration", calibration);
            Set(view, "numberOfChannels", spectrum.NumberOfChannels);
            Set(view, "backgroundMode", BackgroundMode.ShowFSA);
            Set(view, "horizontalUnit", HorizontalUnit.Energy);
            Set(view, "verticalUnit", VerticalUnit.Counts);
            Set(view, "verticalScaleType", VerticalScaleType.PowerScale);
            Set(view, "pownum", 4.0);
            Set(view, "totalMinValuePow", 0.0);
            Set(view, "valueRangePow", Math.Pow(ceiling, 0.25));
            Set(view, "totalMinValueLog", 0.0);
            Set(view, "valueRangeLog", Math.Log10(ceiling));
            Set(view, "height", height);
            Set(view, "width", width - left);
            Set(view, "left", left);
            Set(view, "scrollX", 0);
            Set(view, "scrollY", 0);
            Set(view, "scrollBaseY", 0.0);
            Set(view, "verticalScale", 1.0);
            Set(view, "horizontalScale", 1.0);
            Set(view, "totalMinValue", 0.0);
            Set(view, "valueRange", ceiling);
            Set(view, "energyViewOffset", 0.0);
            Set(view, "pixelPerEnergy", (width - left) / toKev);
            Set(view, "dirty", false);
            Set(view, "fsaMatrixLayer", layer);

            object session = typeof(EnergySpectrumView).GetProperty(
                "FsaSession", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view, null);
            Field(session.GetType(), "result").SetValue(session, result);

            // Кадр собирает САМ вид — тем же вызовом, что перед отрисовкой.
            MethodInfo get = typeof(EnergySpectrumView).GetMethod(
                "GetFsaPresentation", BindingFlags.Instance | BindingFlags.NonPublic);
            var presentation = (FsaPresentation)get.Invoke(view, new object[] { result });

            return new Frame
            {
                View = view,
                Requested = layer,
                Built = (FsaMatrixLayer)Field(typeof(EnergySpectrumView), "fsaFrameLayer").GetValue(view),
                Presentation = presentation.Layers,
                Stack = (List<FsaStackLayer>)Field(typeof(EnergySpectrumView), "fsaStack").GetValue(view),
                Cumulative = (double[][])Field(typeof(EnergySpectrumView), "fsaCumulative").GetValue(view),
                SumPeakLevel = (double[][])Field(typeof(EnergySpectrumView), "fsaSumPeakLevel").GetValue(view),
                StackCurves = (double[][])Field(typeof(EnergySpectrumView), "fsaStackCurves").GetValue(view),
                ModelTop = (double[])Field(typeof(EnergySpectrumView), "fsaModelTop").GetValue(view),
                Zero = (double[])Field(typeof(EnergySpectrumView), "fsaZeroLevel").GetValue(view),
                Channels = spectrum.NumberOfChannels
            };
        }

        /// <summary>Нарисовать кадр настоящим `ShowFsaOverlay`; вернуть число закрашенных пикселей.</summary>
        static int Paint(Frame frame, string pngPath)
        {
            using (var image = new Bitmap(1600, 700))
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.Black);
                    MethodInfo show = typeof(EnergySpectrumView).GetMethod(
                        "ShowFsaOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
                    object drawn = show.Invoke(frame.View, new object[] { g });
                    if (!(drawn is bool) || !(bool)drawn)
                    {
                        Console.WriteLine("  ⛔ ShowFsaOverlay вернул {0} — стопка не нарисована", drawn);
                        bad++;
                    }
                }

                int painted = 0;
                for (int y = 0; y < image.Height; y++)
                {
                    for (int x = 0; x < image.Width; x++)
                    {
                        if (image.GetPixel(x, y).ToArgb() != Color.Black.ToArgb())
                        {
                            painted++;
                        }
                    }
                }

                if (pngPath != null)
                {
                    image.Save(pngPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                return painted;
            }
        }

        // ==================================================================
        // Тождества
        // ==================================================================

        static void Judge(ResultData rd, FsaResult withMatrix, FsaResult noMatrix, string outDir, bool spoilFrame)
        {
            if (outDir != null && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            // (а) «All» = прежняя стопка бит в бит.
            Console.WriteLine("--- (а) «All»: верх каждой ленты = независимое накопление лент Curve ---");
            Frame all = Build(rd, withMatrix, FsaMatrixLayer.All);
            Same("(а) кадр собран как All", FsaMatrixLayer.All, all.Built);
            Same("(а) стопка — все слои представления", all.Presentation.Count, all.Stack.Count);
            double[] running = new double[all.Channels];
            int exact = 0, total = 0;
            for (int k = 0; k < all.Presentation.Count; k++)
            {
                double[] curve = all.Presentation[k].Curve;
                for (int i = 0; i < all.Channels; i++)
                {
                    running[i] = running[i] + (i < curve.Length ? curve[i] : 0.0);
                    total++;
                    if (BitConverter.DoubleToInt64Bits(running[i]) == BitConverter.DoubleToInt64Bits(all.Cumulative[k][i]))
                    {
                        exact++;
                    }
                }

                if (!ReferenceEquals(all.StackCurves[k], all.Presentation[k].Curve))
                {
                    Console.WriteLine("  ⛔ (а) слой {0}: кривая стопки — не лента слоя", all.Presentation[k].Name);
                    bad++;
                }
            }

            Console.WriteLine("  накоплений сверено {0}, побитово равны {1}", total, exact);
            if (exact != total)
            {
                Console.WriteLine("  ⛔ (а) стопка «All» разошлась с независимым накоплением на {0} точках", total - exact);
                bad++;
            }

            Same("(а) верх полной модели — та же ссылка, что верх стопки", true,
                 all.Stack.Count > 0 && ReferenceEquals(all.ModelTop, all.Cumulative[all.Stack.Count - 1]));
            int paintedAll = Paint(all, outDir != null ? Path.Combine(outDir, "channel_view_all.png") : null);
            Console.WriteLine("  закрашено пикселей: {0}", paintedAll);

            // (б) каждый канал.
            var tops = new List<double[]>();
            var painted = new Dictionary<FsaMatrixLayer, int>();
            var hidden = new List<FsaStackLayer>();
            foreach (FsaStackLayer layer in all.Presentation)
            {
                if (layer.ChannelCurves == null)
                {
                    hidden.Add(layer);
                }
            }

            Console.WriteLine("  слоёв без каналов (прячутся в режиме слоя): {0} — {1}", hidden.Count, Names(hidden));
            foreach (FsaMatrixLayer layer in FsaMatrixLayers.Channels)
            {
                Console.WriteLine("--- (б) канал {0} ---", layer);
                Frame frame = Build(rd, withMatrix, layer);
                JudgeB(frame, all, spoilFrame && layer == FsaMatrixLayer.Peak);
                double[] top = frame.Stack.Count > 0 ? frame.Cumulative[frame.Stack.Count - 1] : frame.Zero;
                tops.Add(top);
                Console.WriteLine("  Σ стопки канала по спектру: {0}", Sum(top).ToString("F3", CultureInfo.InvariantCulture));

                // Верх полной модели при канале — бит в бит верх стопки «All».
                int diff = 0;
                for (int i = 0; i < all.Channels; i++)
                {
                    if (BitConverter.DoubleToInt64Bits(frame.ModelTop[i]) != BitConverter.DoubleToInt64Bits(all.ModelTop[i]))
                    {
                        diff++;
                    }
                }

                if (diff > 0)
                {
                    Console.WriteLine("  ⛔ (в) верх полной модели при канале {0} разошёлся с верхом «All» на {1} точках", layer, diff);
                    bad++;
                }

                painted[layer] = Paint(frame, outDir != null
                    ? Path.Combine(outDir, "channel_view_" + layer.ToString().ToLowerInvariant() + ".png") : null);
                Console.WriteLine("  закрашено пикселей: {0}", painted[layer]);
            }

            // (в) Σ каналов = All − подложка − хвост (у слоёв с каналами) − ленты слоёв без каналов.
            Console.WriteLine("--- (в) Σ по каналам верхов стопок против верха «All» ---");
            JudgeC(all, tops, hidden);

            // (г) без матрицы.
            Console.WriteLine("--- (г) спектр без матрицы: режим канала недостижим ---");
            Frame none = Build(rd, noMatrix, FsaMatrixLayer.Compton);
            Same("(г) просили Compton", FsaMatrixLayer.Compton, none.Requested);
            Same("(г) кадр собран как All", FsaMatrixLayer.All, none.Built);
            Same("(г) стопка — все слои представления", none.Presentation.Count, none.Stack.Count);
            Same("(г) у слоёв без матрицы каналов нет", false, FsaMatrixLayers.HasChannels(none.Presentation));
            string tipDead = JudgeWindow(rd, noMatrix, FsaMatrixLayer.Compton, false);
            string tipAlive = JudgeWindow(rd, withMatrix, FsaMatrixLayer.Compton, true);
            if (string.Equals(tipDead, tipAlive, StringComparison.Ordinal))
            {
                Console.WriteLine("  ⛔ (г) подсказка погашенного комбо та же, что у живого, — причины нет: «{0}»", tipDead);
                bad++;
            }
            else
            {
                Console.WriteLine("  (г) подсказка погашенного комбо называет причину, у живого — иная");
            }

            // (д) картинка канала — другая.
            Console.WriteLine("--- (д) картинка канала отличается от «All» ---");
            if (painted[FsaMatrixLayer.Peak] >= paintedAll)
            {
                Console.WriteLine("  ⛔ (д) канал Peak закрасил не меньше пикселей, чем «All» ({0} против {1})",
                                  painted[FsaMatrixLayer.Peak], paintedAll);
                bad++;
            }

            if (painted[FsaMatrixLayer.Peak] == painted[FsaMatrixLayer.Compton])
            {
                Console.WriteLine("  ⛔ (д) каналы Peak и Compton закрасили одинаково ({0}) — картинки неотличимы",
                                  painted[FsaMatrixLayer.Peak]);
                bad++;
            }

            Console.WriteLine("  All {0}, Peak {1}, Compton {2} пикселей", paintedAll,
                              painted[FsaMatrixLayer.Peak], painted[FsaMatrixLayer.Compton]);
        }

        /// <summary>(б) на одном кадре канала: лента слоя = его канал, слоёв без каналов нет, подслой сумм только у Peak.</summary>
        static void JudgeB(Frame frame, Frame all, bool spoil)
        {
            Same("(б) кадр собран как " + frame.Requested, frame.Requested, frame.Built);
            // Ожидание — из ЭТОГО кадра (его снимок представления: слои — те же
            // объекты, что в стопке) и из кадра «All» (порядок и имена: стопка
            // канала — стопка «All» без слоёв без каналов).
            var expected = FsaMatrixLayers.Drawn(frame.Presentation, frame.Requested);
            var order = FsaMatrixLayers.Drawn(all.Presentation, frame.Requested);
            Same("(б) число слоёв стопки", expected.Count, frame.Stack.Count);
            Same("(б) число слоёв стопки против «All» без слоёв без каналов", order.Count, frame.Stack.Count);
            for (int k = 0; k < Math.Min(expected.Count, frame.Stack.Count); k++)
            {
                if (!ReferenceEquals(expected[k], frame.Stack[k]))
                {
                    Console.WriteLine("  ⛔ (б) слой {0}: в стопке {1}, а в представлении кадра на этом месте {2}",
                                      k, frame.Stack[k].Name, expected[k].Name);
                    bad++;
                }

                if (k < order.Count && !string.Equals(order[k].Name, frame.Stack[k].Name, StringComparison.Ordinal))
                {
                    Console.WriteLine("  ⛔ (б) слой {0}: в стопке {1}, а в стопке «All» на этом месте {2} — порядок не тот",
                                      k, frame.Stack[k].Name, order[k].Name);
                    bad++;
                }

                if (frame.Stack[k].ChannelCurves == null)
                {
                    Console.WriteLine("  ⛔ (б) слой {0} без каналов нарисован в режиме канала", frame.Stack[k].Name);
                    bad++;
                }
            }

            int sumLayers = 0;
            if (all.SumPeakLevel != null)
            {
                foreach (double[] level in all.SumPeakLevel)
                {
                    if (level != null)
                    {
                        sumLayers++;
                    }
                }
            }

            Console.WriteLine("  подслоёв сумм-пиков в стопке «All»: {0}{1}", sumLayers,
                              sumLayers == 0 ? " — проверка «подслой только у Peak» на этой сцене вырождена (каскада нет)" : "");

            if (spoil && frame.Stack.Count > 0)
            {
                // Подсадка кадра: накопление первого слоя +1 % там, где оно не ноль.
                double[] level = frame.Cumulative[0];
                int touched = 0;
                for (int i = 0; i < level.Length; i++)
                {
                    if (level[i] > 0.0)
                    {
                        level[i] *= 1.01;
                        touched++;
                    }
                }

                Console.WriteLine("  подсажено: накопление слоя {0} ×1.01 на {1} каналах", frame.Stack[0].Name, touched);
            }

            int channel = (int)frame.Requested;
            int worst = 0;
            double worstGap = 0.0;
            string worstName = "";
            for (int k = 0; k < frame.Stack.Count; k++)
            {
                double[] lower = k > 0 ? frame.Cumulative[k - 1] : frame.Zero;
                double[] upper = frame.Cumulative[k];
                double[][] curves = frame.Stack[k].ChannelCurves;
                double[] curve = curves != null && channel < curves.Length ? curves[channel] : null;
                double scale = Math.Max(1.0, Max(upper));
                for (int i = 0; i < frame.Channels; i++)
                {
                    double drawn = upper[i] - lower[i];
                    double want = curve != null && i < curve.Length ? curve[i] : 0.0;
                    double gap = Math.Abs(drawn - want);
                    if (gap > Abs + Rel * scale)
                    {
                        worst++;
                        if (gap > worstGap)
                        {
                            worstGap = gap;
                            worstName = frame.Stack[k].Name + " канал " + i;
                        }
                    }
                }

                // Кривая стопки — та же ссылка, что канал слоя (пустой канал — пустой массив).
                if (curve != null && !ReferenceEquals(frame.StackCurves[k], curve))
                {
                    Console.WriteLine("  ⛔ (б) слой {0}: кривая стопки — не ChannelCurves[{1}] слоя", frame.Stack[k].Name, channel);
                    bad++;
                }

                bool sums = frame.SumPeakLevel != null && frame.SumPeakLevel[k] != null;
                if (sums && frame.Requested != FsaMatrixLayer.Peak)
                {
                    Console.WriteLine("  ⛔ (б) слой {0}: подслой сумм-пиков нарисован в канале {1}", frame.Stack[k].Name, frame.Requested);
                    bad++;
                }
            }

            if (worst > 0)
            {
                Console.WriteLine("  ⛔ (б) лента ≠ канал слоя на {0} точках; худшая {1}: |Δ| = {2}",
                                  worst, worstName, worstGap.ToString("G6", CultureInfo.InvariantCulture));
                bad++;
            }
            else
            {
                Console.WriteLine("  (б) сошлось: {0} слоёв, каждая лента = ChannelCurves[{1}] слоя (rel {2}, abs {3})",
                                  frame.Stack.Count, channel, Rel, Abs);
            }
        }

        /// <summary>(в): Σ каналов = All − Σ(подложка + хвост) слоёв с каналами − Σ лент слоёв без каналов.</summary>
        static void JudgeC(Frame all, List<double[]> tops, List<FsaStackLayer> hidden)
        {
            int channels = all.Channels;
            double[] sum = new double[channels];
            foreach (double[] top in tops)
            {
                for (int i = 0; i < channels; i++)
                {
                    sum[i] += top[i];
                }
            }

            double[] want = new double[channels];
            double spreadSum = 0.0, tailSum = 0.0, hiddenSum = 0.0;
            for (int i = 0; i < channels; i++)
            {
                want[i] = all.ModelTop[i];
            }

            foreach (FsaStackLayer layer in all.Presentation)
            {
                if (layer.ChannelCurves == null)
                {
                    for (int i = 0; i < channels && i < layer.Curve.Length; i++)
                    {
                        want[i] -= layer.Curve[i];
                        hiddenSum += layer.Curve[i];
                    }

                    continue;
                }

                if (layer.ContinuumCurve != null)
                {
                    for (int i = 0; i < channels && i < layer.ContinuumCurve.Length; i++)
                    {
                        want[i] -= layer.ContinuumCurve[i];
                        spreadSum += layer.ContinuumCurve[i];
                    }
                }

                if (layer.TailCurve != null)
                {
                    for (int i = 0; i < channels && i < layer.TailCurve.Length; i++)
                    {
                        want[i] -= layer.TailCurve[i];
                        tailSum += layer.TailCurve[i];
                    }
                }
            }

            double scale = Math.Max(1.0, Max(all.ModelTop));
            int worst = 0;
            double worstGap = 0.0;
            int worstAt = -1;
            for (int i = 0; i < channels; i++)
            {
                double gap = Math.Abs(sum[i] - want[i]);
                if (gap > Abs + Rel * scale)
                {
                    worst++;
                    if (gap > worstGap)
                    {
                        worstGap = gap;
                        worstAt = i;
                    }
                }
            }

            Console.WriteLine("  Σ каналов {0}; верх All {1}; подложка слоёв с каналами {2}; хвост {3}; ленты без каналов {4}",
                              Sum(sum).ToString("F3", CultureInfo.InvariantCulture),
                              Sum(all.ModelTop).ToString("F3", CultureInfo.InvariantCulture),
                              spreadSum.ToString("F3", CultureInfo.InvariantCulture),
                              tailSum.ToString("F3", CultureInfo.InvariantCulture),
                              hiddenSum.ToString("F3", CultureInfo.InvariantCulture));
            if (worst > 0)
            {
                Console.WriteLine("  ⛔ (в) Σ каналов ≠ All − подложка − хвост − спрятанное на {0} точках; худшая канал {1}: |Δ| = {2}",
                                  worst, worstAt, worstGap.ToString("G6", CultureInfo.InvariantCulture));
                bad++;
            }
            else
            {
                Console.WriteLine("  (в) сошлось на всех {0} каналах спектра (rel {1}, abs {2})", channels, Rel, Abs);
            }
        }

        /// <summary>Только тождество (в) — для положительного контроля 1.</summary>
        static int JudgeIdentityC(ResultData rd, FsaResult result)
        {
            Frame all = Build(rd, result, FsaMatrixLayer.All);
            var tops = new List<double[]>();
            foreach (FsaMatrixLayer layer in FsaMatrixLayers.Channels)
            {
                Frame frame = Build(rd, result, layer);
                tops.Add(frame.Stack.Count > 0 ? frame.Cumulative[frame.Stack.Count - 1] : frame.Zero);
            }

            var hidden = new List<FsaStackLayer>();
            foreach (FsaStackLayer layer in all.Presentation)
            {
                if (layer.ChannelCurves == null)
                {
                    hidden.Add(layer);
                }
            }

            int before = bad;
            JudgeC(all, tops, hidden);
            return bad - before;
        }

        /// <summary>Только тождество (б) на одном канале — для положительного контроля 2.</summary>
        static int JudgeIdentityB(ResultData rd, FsaResult result, FsaMatrixLayer layer, bool spoil)
        {
            Frame all = Build(rd, result, FsaMatrixLayer.All);
            Frame frame = Build(rd, result, layer);
            int before = bad;
            JudgeB(frame, all, spoil);
            return bad - before;
        }

        /// <summary>
        /// Подмена одного канала в результате: ChannelCurves[Peak] первого
        /// компонента с каналами ×1.01. Лента `Curve` не трогается — ровно так
        /// выглядела бы раскладка, разошедшаяся с лентой.
        /// </summary>
        static string PlantChannel(FsaResult result)
        {
            int peak = (int)FsaMatrixLayer.Peak;
            foreach (FsaComponentResult component in result.Components)
            {
                if (component.ChannelCurves == null || peak >= component.ChannelCurves.Length
                    || component.ChannelCurves[peak] == null)
                {
                    continue;
                }

                double[] curve = component.ChannelCurves[peak];
                double moved = 0.0;
                for (int i = 0; i < curve.Length; i++)
                {
                    moved += 0.01 * curve[i];
                    curve[i] *= 1.01;
                }

                return string.Format(CultureInfo.InvariantCulture, "{0}: ChannelCurves[Peak] ×1.01, добавлено {1} отсч.",
                                     component.Name, moved.ToString("F1", CultureInfo.InvariantCulture));
            }

            bad++;
            return "⛔ компонента с каналами нет — подсаживать нечего";
        }

        // ==================================================================
        // Окно отчёта: комбо «Matrix layer»
        // ==================================================================

        /// <summary>
        /// Окно отчёта на подложенном результате: живо ли комбо, что показывает,
        /// помнит ли просьбу; возвращает подсказку комбо — у погашенного она
        /// обязана быть ДРУГОЙ, чем у живого (причина, а не общее «меняет
        /// только график»), это сверяет вызывающий.
        /// </summary>
        static string JudgeWindow(ResultData rd, FsaResult result, FsaMatrixLayer request, bool expectAlive)
        {
            var session = new FsaAnalysisSession();
            Field(session.GetType(), "result").SetValue(session, result);
            Field(session.GetType(), "stamp").SetValue(session,
                FsaAnalysisSession.BuildStamp(rd, rd.BackgroundEnergySpectrum != null));
            using (var report = new FSAReportView(null))
            {
                report.RequestedMatrixLayer = request;
                report.SetProbeSource(session, rd);
                Application.DoEvents();
                var combo = (ComboBox)Field(typeof(FSAReportView), "matrixLayerComboBox").GetValue(report);
                string tag = expectAlive ? "с матрицей" : "без матрицы";
                string tip = report.ToolTipOf(combo);
                Console.WriteLine("  окно ({0}): комбо {1}, пункт «{2}», подсказка «{3}», просьба {4}", tag,
                                  combo.Enabled ? "живо" : "погашено", combo.SelectedItem, tip,
                                  report.RequestedMatrixLayer);
                Same("(г) окно " + tag + ": режим достижим", expectAlive, report.MatrixLayerAvailable);
                Same("(г) окно " + tag + ": комбо живо", expectAlive, combo.Enabled);
                Same("(г) окно " + tag + ": просьба помнится", request, report.RequestedMatrixLayer);
                Same("(г) окно " + tag + ": пунктов в комбо", 1 + FsaMatrixLayers.Channels.Length, combo.Items.Count);
                string shown = expectAlive ? FSAReportView.MatrixLayerText(request) : FSAReportView.MatrixLayerText(FsaMatrixLayer.All);
                Same("(г) окно " + tag + ": показан пункт", shown, (string)combo.SelectedItem);
                if (string.IsNullOrEmpty(tip) || tip.StartsWith("FSAReport_", StringComparison.Ordinal))
                {
                    Console.WriteLine("  ⛔ (г) окно {0}: подсказки комбо нет или стоит ключ ресурса («{1}»)", tag, tip);
                    bad++;
                }

                return tip ?? "";
            }
        }

        // ==================================================================
        // Вспомогательное
        // ==================================================================

        static void Same(string title, object expected, object actual)
        {
            bool ok = Equals(expected, actual);
            Console.WriteLine("  {0} {1}: {2}{3}", ok ? "  " : "⛔", title, actual, ok ? "" : " (ждали " + expected + ")");
            if (!ok)
            {
                bad++;
            }
        }

        static string Names(List<FsaStackLayer> layers)
        {
            var names = new List<string>();
            foreach (FsaStackLayer layer in layers)
            {
                names.Add(layer.Name);
            }

            return names.Count == 0 ? "—" : string.Join(", ", names.ToArray());
        }

        static double Sum(double[] curve)
        {
            double s = 0.0;
            if (curve != null)
            {
                foreach (double v in curve)
                {
                    s += v;
                }
            }

            return s;
        }

        static double Max(double[] curve)
        {
            double m = 0.0;
            if (curve != null)
            {
                foreach (double v in curve)
                {
                    if (v > m)
                    {
                        m = v;
                    }
                }
            }

            return m;
        }

        static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field != null)
                {
                    return field;
                }
            }

            throw new InvalidOperationException("нет поля " + name + " у " + type.Name);
        }

        static void Set(object target, string name, object value)
        {
            Field(target.GetType(), name).SetValue(target, value);
        }

        /// <summary>(`AMBER19`) Пустышка одиночке менеджера — как у `FsaStackShot`.</summary>
        static void PrimeEmptySuppliedLibrary()
        {
            var stub = new NuclideDefinitionManager { NuclideDefinitionFile = new NuclideDefinitionFile() };
            Field(typeof(NuclideDefinitionManager), "isLoaded").SetValue(stub, true);
            FieldInfo instance = typeof(NuclideDefinitionManager).GetField(
                "instance", BindingFlags.Static | BindingFlags.NonPublic);
            if (instance == null)
            {
                throw new InvalidOperationException("нет поля instance у NuclideDefinitionManager");
            }

            instance.SetValue(null, stub);
        }

        /// <summary>(`AMBER19`) Одиночка обязана остаться пустышкой: список с определениями — код 12.</summary>
        static int SuppliedLibraryGate(int code)
        {
            var instance = (NuclideDefinitionManager)typeof(NuclideDefinitionManager)
                .GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            int definitions = instance.NuclideDefinitionFile != null && instance.NuclideDefinitionFile.NuclideDefinitions != null
                ? instance.NuclideDefinitionFile.NuclideDefinitions.Count : -1;
            int sets = instance.NuclideDefinitionFile != null && instance.NuclideDefinitionFile.NuclideSets != null
                ? instance.NuclideDefinitionFile.NuclideSets.Count : -1;
            Console.WriteLine("NuclideDefinitionManager за прогон: обращений {0} (код показа приложения); список у одиночки: определений {1}, наборов {2}",
                              NuclideDefinitionManager.RaiseCount, definitions, sets);
            if (definitions != 0 || sets != 0)
            {
                Console.Error.WriteLine("⛔ AMBER19: поставочный список поднялся ({0} определений, {1} наборов) — числа негодны", definitions, sets);
                return 12;
            }

            return code;
        }

        /// <summary>«Cs-137» → «137CS»; nucid как есть.</summary>
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
