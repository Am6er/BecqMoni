using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaChannelShot
{
    /// <summary>
    /// (`S3`) КАРТИНКИ РАЗДЕЛЕНИЯ ОТКЛИКА ПО КАНАЛАМ — по одной на канал.
    ///
    /// Задача Amber 10.09.2026, консоль: «Отобрази на картинках FSA проделанное
    /// разделение на каналы… Нужно показать в консоли картинки для каждого слоя
    /// отдельно», и вопросником — «Картинка на каждый КАНАЛ».
    ///
    /// ⛔ РИСУЕТ ПРОБА, А НЕ ПРИЛОЖЕНИЕ, и это не небрежность. Второй этап `S3`
    /// (каналы на экран `BecqMoni`) ОТМЕНЁН решением Amber того же дня —
    /// «будем делать потом в другом виде». Поэтому в `EnergySpectrumView`
    /// каналов нет и не заводится: здесь своя отрисовка, разведочная, и
    /// выдавать её за вид приложения нельзя. Тем и отличается от
    /// `FsaStackShot`, который нарочно рисует НАСТОЯЩИМ кодом вида.
    ///
    /// Что кладётся в каждую картинку:
    ///   * серым — измерение за вычетом фона (то, что видит человек);
    ///   * тонкой линией — полная модель;
    ///   * заливкой — ОДИН канал отклика, сумма по всем компонентам состава.
    ///
    /// И одна общая картинка, где все четыре канала сложены стопкой — по ней
    /// видно, что они и составляют модель.
    ///
    ///   fsachannelshot --spectrum=X.xml [--out=префикс] [--set=Имя]
    ///                  [--from=0] [--to=3000] [--ceiling=N] [--pow=4]
    ///                  [--width=1500] [--height=760] [--matrix-any]
    ///
    /// `--pow=` — вертикальная шкала (корень степени), ровно кнопка «POW» под
    /// графиком: в линейной шкале мелкая структура сливается с осью (`S88`).
    /// `--pow=1` даёт линейную.
    ///
    /// ⚠ Библиотека собирается ПО ПИКАМ из поставочных определений — как это
    /// делает `FsaStackShot` и как видит человек в окне. Для КОРПУСНЫХ чисел
    /// так делать нельзя (правило `--lib=sample`), но здесь картинка про живой
    /// спектр, а не про корпус.
    /// </summary>
    static class Program
    {
        /// <summary>
        /// ⛔ ВТОРОЙ КАНАЛ — НЕ «КОМПТОН», И НАЗЫВАТЬ ЕГО ТАК НЕЛЬЗЯ.
        /// `EfficiencySimulator.ChannelOf` кладёт в него ВСЯКУЮ историю, из
        /// которой что-то вылетело, если это не 511 и не K-рентген кристалла:
        /// рассеянный квант, ушедший электрон, тормозное, L-рентген.
        ///
        /// ⚠ Причём при НУЛЕВОМ допуске пика (`PeakToleranceFromGeometry`
        /// выключен умолчанием, ~~`E34`~~) туда уходит даже утечка в доли
        /// кЭВ — и депозит у такой истории почти полный, то есть после
        /// уширения она ложится РОВНО ПОД ПИКОМ. Именно так на картинке
        /// тория появился узкий пик при 58 кЭВ (K-рентген вольфрама),
        /// из-за которого спросила Amber 10.09.2026: в «комптоне» пика быть
        /// не должно, а в «неполном поглощении» — бывает.
        /// </summary>
        static readonly string[] ChannelNames =
        {
            "полное поглощение",
            "неполное поглощение (комптон, электроны, тормозное)",
            "вылет 511", "вылет рентгена кристалла"
        };

        static readonly Color[] ChannelColors =
        {
            Color.FromArgb(210, 32, 96, 200),    // пик — синий
            Color.FromArgb(210, 232, 126, 40),   // комптон — оранжевый
            Color.FromArgb(210, 150, 60, 190),   // вылет 511 — фиолетовый
            Color.FromArgb(210, 205, 50, 60)     // вылет рентгена — красный
        };

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — до разбора ключей.
            FsaTuningReport.Snapshot();

            string spectrumPath = null, outPrefix = null, setName = null;
            double from = 0.0, to = 0.0, ceiling = 0.0, power = 4.0;
            int width = 1500, height = 760;
            double floorKev = -1.0, who = -1.0;
            string dumpPath = null;
            bool matrixAny = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPrefix = a.Substring(6);
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--from=", StringComparison.Ordinal)) from = Num(a, 7);
                else if (a.StartsWith("--to=", StringComparison.Ordinal)) to = Num(a, 5);
                else if (a.StartsWith("--ceiling=", StringComparison.Ordinal)) ceiling = Num(a, 10);
                else if (a.StartsWith("--pow=", StringComparison.Ordinal)) power = Num(a, 6);
                else if (a.StartsWith("--width=", StringComparison.Ordinal)) width = (int)Num(a, 8);
                else if (a.StartsWith("--height=", StringComparison.Ordinal)) height = (int)Num(a, 9);
                else if (a.StartsWith("--floor=", StringComparison.Ordinal)) floorKev = Num(a, 8);
                else if (a.StartsWith("--dump=", StringComparison.Ordinal)) dumpPath = a.Substring(7);
                else if (a.StartsWith("--who=", StringComparison.Ordinal)) who = Num(a, 6);
                else if (a == "--matrix-any") matrixAny = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл>");
                return 2;
            }

            if (outPrefix == null)
            {
                outPrefix = Path.GetFileNameWithoutExtension(spectrumPath);
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(spectrumPath);
            Console.WriteLine("спектр : {0}", Path.GetFileName(spectrumPath));
            Console.WriteLine("прибор : {0}", ProbeDeviceConfig.Attach(rd));

            if (setName != null && !SelectSet(nuclides, setName))
            {
                return 2;
            }

            // ---- матрица: без неё каналов нет вовсе ------------------------
            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(
                rd.Efficiency != null ? rd.Efficiency.Guid : null, out refusal, out fileFormat);
            bool stampOk = matrix != null && rd.Efficiency != null && rd.Efficiency.HasGeometry
                           && matrix.IsValidFor(rd.Efficiency.Geometry);
            if (matrix == null)
            {
                Console.Error.WriteLine("⛔ матрицы НЕТ ({0}, формат файла {1}) — каналов не существует, "
                                        + "рисовать нечего", refusal, fileFormat);
                return 1;
            }

            if (!stampOk)
            {
                if (!matrixAny)
                {
                    Console.Error.WriteLine("⛔ ОТПЕЧАТОК НЕ СОШЁЛСЯ: матрица есть, но не для этой геометрии.");
                    if (rd.Efficiency != null && rd.Efficiency.HasGeometry)
                    {
                        Console.Error.WriteLine("   у матрицы          : {0}", matrix.Stamp);
                        Console.Error.WriteLine("   у геометрии спектра: {0}",
                            ResponseMatrix.ComputeStamp(rd.Efficiency.Geometry, matrix.Options));
                    }

                    Console.Error.WriteLine("   осознанно — ключ --matrix-any (числа станут негодными)");
                    return 1;
                }

                Console.WriteLine("⚠ --matrix-any: матрица взята НЕСМОТРЯ на отпечаток — "
                                  + "картинка иллюстративная, числа корпусными не являются");
            }

            if (!matrix.HasChannels)
            {
                Console.Error.WriteLine("⛔ у матрицы нет раскладки по каналам");
                return 1;
            }

            Console.WriteLine("кривая : {0}", rd.Efficiency != null ? rd.Efficiency.Name : "—");
            Console.WriteLine("матрица: каналов {0}, бин {1} кэВ, клеймо {2}",
                              matrix.ChannelRows.Length,
                              matrix.BinKev.ToString("F2", CultureInfo.InvariantCulture),
                              matrix.Stamp);

            // ---- разбор ----------------------------------------------------
            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
            Console.WriteLine("состав : пиков {0}, образов {1}", peaks.Count, library.Count);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("⛔ библиотека пуста");
                return 1;
            }

            string material = EfficiencySimulator.ScintillatorNameOf(
                rd.Efficiency != null ? rd.Efficiency.Geometry : null);
            var analyzer = new FsaAnalyzer { ResponseMatrix = matrix, ScintillatorMaterial = material };
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

            FsaCalculationOptions.Of(rd).ApplyTo(analyzer);

            // (`S13`) ПОРОГ ДОВЕРИЯ КОНТИНУУМУ — ключом, ради A/B. Ниже этого
            // порога континуум образа ОТВЯЗЫВАЕТСЯ в свою свободную колонку,
            // кроме пиковых окон линий, — и именно это оставляет на картинке
            // комптоновского канала узкий пик с обрывом (вопрос Amber 10.09.2026).
            // `--floor=0` снимает отвязку целиком.
            if (floorKev >= 0.0)
            {
                analyzer.ResponseContinuumTrustFloorKev = floorKev;
            }

            Console.WriteLine("порог  : отвязка континуума ниже {0} кЭВ",
                              analyzer.ResponseContinuumTrustFloorKev.ToString("F1", CultureInfo.InvariantCulture));
            FsaTuningReport.Print(analyzer, "картинки каналов");

            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration, library,
                                                FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("⛔ разложение не получилось");
                return 1;
            }

            Console.WriteLine("разбор : chi2/ndf {0}, невязка {1} %, состав {2}",
                              result.Chi2Ndf.ToString("F3", CultureInfo.InvariantCulture),
                              (result.ModelResidual * 100.0).ToString("F1", CultureInfo.InvariantCulture),
                              result.Components.Count);

            // ---- суммы каналов по всем компонентам -------------------------
            int channels = result.Model.Length;
            int count = EfficiencySimulator.ResponseChannelCount;
            double[][] byChannel = new double[count][];
            for (int c = 0; c < count; c++)
            {
                byChannel[c] = new double[channels];
            }

            int withChannels = 0;
            foreach (FsaComponentResult component in result.Components)
            {
                if (component.ChannelCurves == null)
                {
                    continue;
                }

                withChannels++;
                for (int c = 0; c < count && c < component.ChannelCurves.Length; c++)
                {
                    double[] row = component.ChannelCurves[c];
                    if (row == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < channels && i < row.Length; i++)
                    {
                        byChannel[c][i] += row[i];
                    }
                }
            }

            if (withChannels == 0)
            {
                Console.Error.WriteLine("⛔ ни у одного компонента нет раскладки по каналам — "
                                        + "образ построен не по матрице");
                return 1;
            }

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ КАРТИНКИ: сумма каналов обязана дать
            // ленты компонентов. Картинка, нарисованная по расползшимся числам,
            // выглядит ровно так же, как по сошедшимся.
            double sumChannels = 0.0, sumCurves = 0.0;
            for (int c = 0; c < count; c++)
            {
                sumChannels += Sum(byChannel[c]);
            }

            foreach (FsaComponentResult component in result.Components)
            {
                if (component.ChannelCurves != null)
                {
                    sumCurves += Sum(component.Curve);
                }
            }

            double gap = sumCurves > 0.0 ? Math.Abs(sumChannels - sumCurves) / sumCurves : 0.0;
            Console.WriteLine("сверка : Σ каналов {0} против Σ лент {1}, расхождение {2}",
                              sumChannels.ToString("F1", CultureInfo.InvariantCulture),
                              sumCurves.ToString("F1", CultureInfo.InvariantCulture),
                              gap.ToString("E2", CultureInfo.InvariantCulture));
            if (gap > 1.0E-9)
            {
                Console.Error.WriteLine("⛔ Σ каналов ≠ Σ лент — картинки рисовать нельзя");
                return 1;
            }

            // ---- что рисуем ------------------------------------------------
            EnergyCalibration calibration = rd.EnergySpectrum.EnergyCalibration;
            if (!(to > from))
            {
                from = 0.0;
                to = calibration.ChannelToEnergy(channels - 1);
            }

            double[] measured = new double[channels];
            int[] raw = rd.EnergySpectrum.Spectrum;
            for (int i = 0; i < channels; i++)
            {
                double back = result.Background != null && i < result.Background.Length
                    ? result.Background[i] : 0.0;
                measured[i] = (i < raw.Length ? raw[i] : 0.0) - back;
            }

            var scene = new Scene
            {
                Calibration = calibration,
                Channels = channels,
                From = from,
                To = to,
                Power = power > 0.0 ? power : 1.0,
                Width = width,
                Height = height,
                Title = Path.GetFileNameWithoutExtension(spectrumPath),
                Subtitle = string.Format(CultureInfo.InvariantCulture,
                    "{0} · χ²/ndf {1:F2} · кривая «{2}»",
                    ProbeDeviceConfig.Attach(rd), result.Chi2Ndf,
                    rd.Efficiency != null ? rd.Efficiency.Name : "—")
            };

            scene.Ceiling = ceiling > 0.0 ? ceiling : Ceiling(measured, scene);
            Console.WriteLine();
            Console.WriteLine("=== доли каналов в полосе {0}…{1} кэВ ===",
                              from.ToString("F0", CultureInfo.InvariantCulture),
                              to.ToString("F0", CultureInfo.InvariantCulture));

            double total = 0.0;
            double[] area = new double[count];
            for (int c = 0; c < count; c++)
            {
                area[c] = Band(byChannel[c], scene);
                total += area[c];
            }

            if (dumpPath != null)
            {
                var rows = new List<string>();
                rows.Add("channel;energy_kev;measured;model;peak;compton;esc511;escxray");
                for (int i = 0; i < channels; i++)
                {
                    double e = calibration.ChannelToEnergy(i);
                    if (e < from || e > to)
                    {
                        continue;
                    }

                    rows.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0};{1:F3};{2:F4};{3:F4};{4:F4};{5:F4};{6:F4};{7:F4}",
                        i, e, measured[i], result.Model[i],
                        byChannel[0][i], byChannel[1][i], byChannel[2][i], byChannel[3][i]));
                }

                File.WriteAllLines(dumpPath, rows, new UTF8Encoding(true));
                Console.WriteLine("дамп  : {0}", Path.GetFullPath(dumpPath));
            }

            // (вопрос Amber 10.09.2026) КТО ДАЁТ ЭТОТ БИН — поимённо.
            // Картинка показывает СУММУ по составу, и «откуда здесь пик» по ней не
            // видно никак: спор «это рассеяние или что-то ещё» решается
            // только именами компонентов.
            if (who > 0.0)
            {
                int bin = -1;
                double best = double.MaxValue;
                for (int i = 0; i < channels; i++)
                {
                    double d = Math.Abs(calibration.ChannelToEnergy(i) - who);
                    if (d < best)
                    {
                        best = d;
                        bin = i;
                    }
                }

                Console.WriteLine();
                Console.WriteLine("=== кто даёт {0:F1} кЭВ (канал АЦП {1}) ===", who, bin);
                Console.WriteLine("{0,-16} {1,12} {2,12} {3,12} {4,12}",
                                  "компонент", "пик", "комптон", "вылет 511", "вылет x");
                foreach (FsaComponentResult component in result.Components)
                {
                    if (component.ChannelCurves == null)
                    {
                        continue;
                    }

                    double[] v = new double[count];
                    double any = 0.0;
                    for (int c = 0; c < count && c < component.ChannelCurves.Length; c++)
                    {
                        double[] row = component.ChannelCurves[c];
                        v[c] = row != null && bin < row.Length ? row[bin] : 0.0;
                        any += v[c];
                    }

                    if (any <= 0.0)
                    {
                        continue;
                    }

                    Console.WriteLine("{0,-16} {1,12:F1} {2,12:F1} {3,12:F1} {4,12:F1}",
                                      component.Name, v[0], v[1], v[2], v[3]);
                }

                Console.WriteLine("{0,-16} {1,12:F1} {2,12:F1} {3,12:F1} {4,12:F1}   ← всего",
                                  "ИТОГО", byChannel[0][bin], byChannel[1][bin],
                                  byChannel[2][bin], byChannel[3][bin]);
            }

            var files = new List<string>();
            for (int c = 0; c < count; c++)
            {
                double share = total > 0.0 ? 100.0 * area[c] / total : 0.0;
                Console.WriteLine("  {0,-26} {1,12} отсчётов  {2,6} %",
                                  ChannelNames[c],
                                  area[c].ToString("F0", CultureInfo.InvariantCulture),
                                  share.ToString("F2", CultureInfo.InvariantCulture));

                string path = outPrefix + "-" + (c + 1) + "-" + Slug(c) + ".png";
                DrawOne(path, scene, measured, result.Model, byChannel[c], c, share);
                files.Add(path);
            }

            // ⛔ ОБЩАЯ КАРТИНКА — В ЛИНЕЙНОЙ ШКАЛЕ, и это не мелочь.
            // В POW-шкале высота есть корень четвёртой степени от счёта, то есть
            // площади СКЛАДЫВАЮТСЯ НЕ ПО-ГЛАЗУ: канал в 0.45 % площади
            // занимает там до пятой части высоты, и стопка читается как
            // «вылет рентгена — половина модели», чего нет. Первая редакция
            // так и выглядела. Поэтому стопка рисуется линейной — там
            // высота честно отвечает доле, — а форма каждого канала порознь
            // смотрится на его собственной картинке в POW.
            var linear = new Scene
            {
                Calibration = scene.Calibration, Channels = scene.Channels,
                From = scene.From, To = scene.To, Ceiling = scene.Ceiling, Power = 1.0,
                Width = scene.Width, Height = scene.Height,
                Title = scene.Title, Subtitle = scene.Subtitle
            };

            string all = outPrefix + "-0-все-каналы.png";
            DrawAll(all, linear, measured, byChannel, area, total);
            files.Insert(0, all);

            Console.WriteLine();
            foreach (string f in files)
            {
                Console.WriteLine("КАРТИНКА\t{0}", Path.GetFullPath(f));
            }

            return 0;
        }

        // ==================================================================
        // Отрисовка
        // ==================================================================

        sealed class Scene
        {
            public EnergyCalibration Calibration;
            public int Channels;
            public double From, To, Ceiling, Power;
            public int Width, Height;
            public string Title, Subtitle;

            public int Left { get { return 88; } }
            public int Right { get { return this.Width - 24; } }
            public int Top { get { return 94; } }
            public int Bottom { get { return this.Height - 54; } }

            public float X(double energy)
            {
                double t = (energy - this.From) / (this.To - this.From);
                return (float)(this.Left + t * (this.Right - this.Left));
            }

            public float Y(double counts)
            {
                double v = counts > 0.0 ? counts : 0.0;
                double top = this.Ceiling > 0.0 ? this.Ceiling : 1.0;
                double t = Math.Pow(v / top, 1.0 / this.Power);
                if (t > 1.0)
                {
                    t = 1.0;
                }

                return (float)(this.Bottom - t * (this.Bottom - this.Top));
            }
        }

        static void DrawOne(string path, Scene scene, double[] measured, double[] model,
                            double[] channel, int index, double share)
        {
            using (var bmp = new Bitmap(scene.Width, scene.Height))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Frame(g, scene, ChannelNames[index],
                      string.Format(CultureInfo.InvariantCulture,
                                    "канал {0} из 4 · {1:F2} % площади модели в полосе",
                                    index + 1, share));
                Fill(g, scene, channel, ChannelColors[index]);
                Line(g, scene, model, Color.FromArgb(150, 20, 20, 20), 1.4f);
                Line(g, scene, measured, Color.FromArgb(190, 90, 90, 90), 1.0f);
                Legend(g, scene, new[]
                {
                    Tuple.Create(ChannelNames[index], ChannelColors[index], true),
                    Tuple.Create("модель целиком", Color.FromArgb(150, 20, 20, 20), false),
                    Tuple.Create("измерение за вычетом фона", Color.FromArgb(190, 90, 90, 90), false)
                });
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void DrawAll(string path, Scene scene, double[] measured, double[][] byChannel,
                            double[] area, double total)
        {
            using (var bmp = new Bitmap(scene.Width, scene.Height))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                Frame(g, scene, "все четыре канала стопкой",
                      "сумма каналов и есть модель · шкала ЛИНЕЙНАЯ, чтобы высота отвечала доле");

                // ⛔ ПОРЯДОК ЗАЛИВОК — ОТ БОЛЬШЕЙ СУММЫ К МЕНЬШЕЙ, иначе стопка
                // врёт: заливка непрозрачна, и слой, нарисованный последним,
                // закрывает всё под собой. Первая редакция копила снизу вверх и
                // рисовала полную сумму ПОСЛЕДНЕЙ — на картинке остался один
                // синий канал пика, а комптона не было видно вовсе, хотя по
                // числам он больше половины.
                //
                // Поэтому рисуется «хвост от c и выше»: сначала все четыре
                // (цветом первого), потом без первого, и так далее. Каждая
                // следующая заливка строго меньше предыдущей и ложится поверх —
                // видно и целое, и каждую часть.
                int channels = scene.Channels;
                for (int c = 0; c < byChannel.Length; c++)
                {
                    double[] top = new double[channels];
                    for (int k = c; k < byChannel.Length; k++)
                    {
                        for (int i = 0; i < channels; i++)
                        {
                            top[i] += byChannel[k][i];
                        }
                    }

                    Fill(g, scene, top, ChannelColors[c]);
                }

                Line(g, scene, measured, Color.FromArgb(190, 90, 90, 90), 1.0f);

                var items = new List<Tuple<string, Color, bool>>();
                for (int c = 0; c < byChannel.Length; c++)
                {
                    double share = total > 0.0 ? 100.0 * area[c] / total : 0.0;
                    items.Add(Tuple.Create(
                        string.Format(CultureInfo.InvariantCulture, "{0} — {1:F2} %",
                                      ChannelNames[c], share),
                        ChannelColors[c], true));
                }

                items.Add(Tuple.Create("измерение за вычетом фона", Color.FromArgb(190, 90, 90, 90), false));
                Legend(g, scene, items.ToArray());
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void Frame(Graphics g, Scene scene, string title, string note)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.White);

            using (var head = new Font("Segoe UI", 15f, FontStyle.Bold))
            using (var sub = new Font("Segoe UI", 9.5f))
            using (var small = new Font("Segoe UI", 8.5f))
            using (var ink = new SolidBrush(Color.FromArgb(30, 30, 30)))
            using (var grey = new SolidBrush(Color.FromArgb(110, 110, 110)))
            using (var axis = new Pen(Color.FromArgb(90, 90, 90), 1.2f))
            using (var grid = new Pen(Color.FromArgb(232, 232, 232), 1f))
            {
                g.DrawString(scene.Title + " — " + title, head, ink, 20, 10);
                g.DrawString(scene.Subtitle, sub, grey, 22, 36);
                g.DrawString(note, sub, grey, 22, 53);

                // Сетка по энергии.
                double step = NiceStep(scene.To - scene.From);
                for (double e = Math.Ceiling(scene.From / step) * step; e <= scene.To; e += step)
                {
                    float x = scene.X(e);
                    g.DrawLine(grid, x, scene.Top, x, scene.Bottom);
                    g.DrawString(e.ToString("F0", CultureInfo.InvariantCulture), small, grey,
                                 x - 14, scene.Bottom + 6);
                }

                g.DrawString("кэВ", small, grey, scene.Right - 26, scene.Bottom + 24);
                g.DrawString(string.Format(CultureInfo.InvariantCulture,
                                           "отсчёты, шкала POW {0:F0} · потолок {1:F0}",
                                           scene.Power, scene.Ceiling),
                             small, grey, 20, scene.Top - 16);
                g.DrawLine(axis, scene.Left, scene.Top, scene.Left, scene.Bottom);
                g.DrawLine(axis, scene.Left, scene.Bottom, scene.Right, scene.Bottom);
            }
        }

        static void Fill(Graphics g, Scene scene, double[] curve, Color color)
        {
            var points = new List<PointF>();
            points.Add(new PointF(scene.X(scene.From), scene.Y(0.0)));
            for (int i = 0; i < scene.Channels; i++)
            {
                double e = scene.Calibration.ChannelToEnergy(i);
                if (e < scene.From || e > scene.To)
                {
                    continue;
                }

                points.Add(new PointF(scene.X(e), scene.Y(i < curve.Length ? curve[i] : 0.0)));
            }

            points.Add(new PointF(scene.X(scene.To), scene.Y(0.0)));
            if (points.Count < 3)
            {
                return;
            }

            using (var brush = new SolidBrush(color))
            {
                g.FillPolygon(brush, points.ToArray());
            }
        }

        static void Line(Graphics g, Scene scene, double[] curve, Color color, float thickness)
        {
            var points = new List<PointF>();
            for (int i = 0; i < scene.Channels; i++)
            {
                double e = scene.Calibration.ChannelToEnergy(i);
                if (e < scene.From || e > scene.To)
                {
                    continue;
                }

                points.Add(new PointF(scene.X(e), scene.Y(i < curve.Length ? curve[i] : 0.0)));
            }

            if (points.Count < 2)
            {
                return;
            }

            using (var pen = new Pen(color, thickness))
            {
                g.DrawLines(pen, points.ToArray());
            }
        }

        static void Legend(Graphics g, Scene scene, Tuple<string, Color, bool>[] items)
        {
            using (var font = new Font("Segoe UI", 9.5f))
            using (var ink = new SolidBrush(Color.FromArgb(40, 40, 40)))
            {
                int x = scene.Right - 320;
                int y = scene.Top + 10;
                using (var back = new SolidBrush(Color.FromArgb(225, 255, 255, 255)))
                using (var edge = new Pen(Color.FromArgb(210, 210, 210)))
                {
                    var box = new Rectangle(x - 12, y - 8, 320, 20 * items.Length + 14);
                    g.FillRectangle(back, box);
                    g.DrawRectangle(edge, box);
                }

                foreach (Tuple<string, Color, bool> item in items)
                {
                    using (var brush = new SolidBrush(item.Item2))
                    using (var pen = new Pen(item.Item2, 2.2f))
                    {
                        if (item.Item3)
                        {
                            g.FillRectangle(brush, x, y + 4, 16, 10);
                        }
                        else
                        {
                            g.DrawLine(pen, x, y + 9, x + 16, y + 9);
                        }
                    }

                    g.DrawString(item.Item1, font, ink, x + 24, y);
                    y += 20;
                }
            }
        }

        // ==================================================================
        // Мелочь
        // ==================================================================

        static double Ceiling(double[] measured, Scene scene)
        {
            double top = 0.0;
            for (int i = 0; i < scene.Channels && i < measured.Length; i++)
            {
                double e = scene.Calibration.ChannelToEnergy(i);
                if (e < scene.From || e > scene.To)
                {
                    continue;
                }

                if (measured[i] > top)
                {
                    top = measured[i];
                }
            }

            return top > 0.0 ? top : 1.0;
        }

        static double Band(double[] curve, Scene scene)
        {
            double sum = 0.0;
            for (int i = 0; i < scene.Channels && i < curve.Length; i++)
            {
                double e = scene.Calibration.ChannelToEnergy(i);
                if (e >= scene.From && e <= scene.To)
                {
                    sum += curve[i];
                }
            }

            return sum;
        }

        static double NiceStep(double span)
        {
            double raw = span / 12.0;
            double[] steps = { 10, 20, 25, 50, 100, 200, 250, 500, 1000 };
            foreach (double s in steps)
            {
                if (raw <= s)
                {
                    return s;
                }
            }

            return 1000.0;
        }

        static string Slug(int channel)
        {
            switch (channel)
            {
                case 0: return "пик";
                case 1: return "комптон";
                case 2: return "вылет511";
                default: return "рентген";
            }
        }

        static double Sum(double[] curve)
        {
            double s = 0.0;
            if (curve == null)
            {
                return s;
            }

            foreach (double v in curve)
            {
                s += v;
            }

            return s;
        }

        static double Num(string arg, int at)
        {
            return double.Parse(arg.Substring(at), CultureInfo.InvariantCulture);
        }

        static bool SelectSet(NuclideDefinitionManager nuclides, string name)
        {
            foreach (NuclideSet set in nuclides.NuclideSets)
            {
                if (string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    nuclides.ActiveSet = set;
                    return true;
                }
            }

            Console.Error.WriteLine("набора «{0}» нет; есть:", name);
            foreach (NuclideSet set in nuclides.NuclideSets)
            {
                Console.Error.WriteLine("   {0}", set.Name);
            }

            return false;
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
