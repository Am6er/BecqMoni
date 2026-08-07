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
using System.Linq;
using System.Text;
using System.Xml.Serialization;

namespace FsaPaletteProbe
{
    /// <summary>
    /// Макет оформления стека FSA: один и тот же спектр, четыре варианта
    /// раскраски, картинками — чтобы решать глазами, а не по описанию.
    ///
    /// Повод. В рабочей палитре `Backscatter180` получает тот же розовый, что
    /// Cs-137. Это не совпадение, а следствие устройства: производные образы
    /// (обратное рассеяние) в именной таблице `FsaPalette` отсутствуют и уходят
    /// в `Fallback`, а `Fallback` состоит ровно из цветов именованных нуклидов
    /// (Th-232, Ra-226, U-235, Cs-137, U-238). Столкновение гарантировано
    /// устройством, а не невезением.
    ///
    /// Варианты:
    ///
    ///   now       как сейчас — контрольный снимок
    ///   named     производные образы заведены в именной таблице
    ///   free      плюс fallback выдаёт только цвета, не занятые в ЭТОМ спектре
    ///   category  плюс категория различается ВИДОМ заливки: нуклид — плотная,
    ///             приборный артефакт (континуум, рассеяние, вылеты, рентген,
    ///             «прочее») — приглушённая со штриховкой
    ///
    ///     fsapaletteprobe --spectrum=spectra\ASN16_Cs137.xml
    ///                     [--background=spectra\ASN16_Background.xml]
    ///                     [--out=<каталог>] [--width=1400] [--height=760]
    ///
    /// Запускать из рабочего каталога корпуса (`tools\CORPUS\scripts\wd_ASN16`):
    /// нужны и конфигурация прибора, и определения нуклидов.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, backgroundPath = null, outDir = ".", geometryPath = null;
            string efficiencyName = null;
            int width = 1400, height = 760;
            bool rebuild = false;
            foreach (string a in args)
            {
                if (a == "--rebuild") { rebuild = true; continue; }
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--background=", StringComparison.Ordinal)) backgroundPath = a.Substring(13);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--width=", StringComparison.Ordinal)) width = int.Parse(a.Substring(8));
                else if (a.StartsWith("--height=", StringComparison.Ordinal)) height = int.Parse(a.Substring(9));
                else if (a.StartsWith("--geometry=", StringComparison.Ordinal)) geometryPath = a.Substring(11);
                // кривая по имени из живой конфигурации прибора — для спектров,
                // в файле которых кривой нет (как выбор в панели измерения)
                else if (a.StartsWith("--efficiency=", StringComparison.Ordinal)) efficiencyName = a.Substring(13);
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
            if (efficiencyName != null)
            {
                // Копией, как EfficiencyFromItem в панели измерения: Guid у
                // копии тот же, значит и матрица в хранилище — та же, что
                // увидит приложение.
                foreach (DeviceConfigInfo device in DeviceConfigManager.GetInstance().DeviceConfigList)
                {
                    foreach (EfficiencyConfigData curve in device.EfficiencyConfigs)
                    {
                        if (string.Equals(curve.Name, efficiencyName, StringComparison.OrdinalIgnoreCase))
                        {
                            rd.Efficiency = curve.Copy();
                            Console.WriteLine("кривая «{0}» из прибора «{1}», геометрия {2}",
                                              curve.Name, device.Name,
                                              rd.Efficiency.HasGeometry ? "есть" : "НЕТ");
                            break;
                        }
                    }

                    if (rd.Efficiency != null)
                    {
                        break;
                    }
                }

                if (rd.Efficiency == null)
                {
                    Console.Error.WriteLine("кривая «{0}» не нашлась ни в одном приборе", efficiencyName);
                    return 2;
                }
            }

            EnergySpectrum background = backgroundPath != null
                ? Load(backgroundPath).EnergySpectrum : null;

            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            Console.WriteLine("пиков найдено: {0}", peaks.Count);

            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
            Console.WriteLine("компонентов в библиотеке: {0}", library.Count);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста — рисовать нечего");
                return 1;
            }

            FsaAnalyzer analyzer = new FsaAnalyzer();

            // Сравнение «с матрицей и без»: библиотека, спектр и фон одни и те
            // же, разница только в том, есть ли у образа компонента континуум.
            if (geometryPath != null)
            {
                ResponseMatrix matrix;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (geometryPath == "self")
                {
                    // Геометрия и матрица берутся так же, как их берёт
                    // приложение: у кривой ЭТОГО спектра и из хранилища по её
                    // Guid, с проверкой отпечатка.
                    matrix = ResponseMatrixStore.Load(rd.Efficiency != null ? rd.Efficiency.Guid : null);
                    bool valid = matrix != null && rd.Efficiency != null && rd.Efficiency.HasGeometry
                                 && matrix.IsValidFor(rd.Efficiency.Geometry);
                    Console.WriteLine("матрица из хранилища: {0}, отпечаток {1}",
                                      matrix == null ? "нет" : "есть", valid ? "сошёлся" : "НЕ сошёлся");
                    if (!valid && rebuild && rd.Efficiency != null && rd.Efficiency.HasGeometry)
                    {
                        // Пересчёт по геометрии ТОЙ ЖЕ кривой и запись в то же
                        // хранилище — ровно то, что делает форма матрицы в
                        // приложении. Нужно после смены формата файла: старая
                        // матрица не читается, а считать её вручную негде.
                        Console.WriteLine("пересчитываю матрицу по геометрии кривой...");
                        matrix = ResponseMatrixBuilder.Build(rd.Efficiency.Geometry, new ResponseMatrixOptions(),
                                                             null, System.Threading.CancellationToken.None);
                        ResponseMatrixStore.Save(rd.Efficiency.Guid, matrix);
                        valid = matrix.IsValidFor(rd.Efficiency.Geometry);
                        Console.WriteLine("  пересчитано за {0:F0} с, отпечаток {1}",
                                          matrix.BuildSeconds, valid ? "сошёлся" : "НЕ сошёлся");
                    }

                    if (!valid)
                    {
                        return 1;
                    }
                }
                else
                {
                    GeometryModel geometry = GeometryModel.Load(geometryPath);
                    var options = new ResponseMatrixOptions { NodeCount = 60, Histories = 60000, BinKev = 2.0 };
                    Console.WriteLine("считаю матрицу отклика по {0}...", Path.GetFileName(geometryPath));
                    matrix = ResponseMatrixBuilder.Build(geometry, options, null,
                                                        System.Threading.CancellationToken.None);
                }

                Console.WriteLine("  {0:F1} с, {1} узлов, {2:F0} КБ",
                                  sw.Elapsed.TotalSeconds, matrix.NodeCount, matrix.DataBytes / 1024.0);

                sw.Restart();
                FsaResult without = analyzer.Analyze(rd.EnergySpectrum, background, rd.FwhmCalibration,
                                                     library, FsaEfficiency.FromConfig(rd.Efficiency));
                double withoutMs = sw.Elapsed.TotalMilliseconds;

                analyzer.ResponseMatrix = matrix;
                sw.Restart();
                FsaResult with = analyzer.Analyze(rd.EnergySpectrum, background, rd.FwhmCalibration,
                                                  library, FsaEfficiency.FromConfig(rd.Efficiency));
                double withMs = sw.Elapsed.TotalMilliseconds;

                Console.WriteLine();
                Console.WriteLine("{0,-16} {1,12} {2,12}", "", "без матрицы", "с матрицей");
                Console.WriteLine("{0,-16} {1,12:F2} {2,12:F2}", "chi2/ndf",
                                  without != null ? without.Chi2Ndf : 0.0,
                                  with != null ? with.Chi2Ndf : 0.0);
                Console.WriteLine("{0,-16} {1,12:F0} {2,12:F0}", "мс", withoutMs, withMs);
                if (without != null && with != null)
                {
                    var a = without.BuildStackedLayers(6);
                    var b = with.BuildStackedLayers(6);
                    foreach (var layer in a)
                    {
                        var mate = b.Find(l => l.Name == layer.Name);
                        Console.WriteLine("{0,-16} {1,12:F2} {2,12}", layer.Name, layer.SharePercent,
                                          mate != null ? mate.SharePercent.ToString("F2") : "-");
                    }

                    foreach (var layer in b)
                    {
                        if (a.Find(l => l.Name == layer.Name) == null)
                        {
                            Console.WriteLine("{0,-16} {1,12} {2,12:F2}", layer.Name, "-", layer.SharePercent);
                        }
                    }
                }

                Console.WriteLine();
            }

            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, background, rd.FwhmCalibration,
                                                library, FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("разложение не получилось");
                return 1;
            }

            List<FsaStackLayer> layers = result.BuildStackedLayers(6);
            Console.WriteLine("слоёв: {0}, chi2/ndf {1:F2}", layers.Count, result.Chi2Ndf);

            Directory.CreateDirectory(outDir);
            foreach (Style style in Style.All)
            {
                Dictionary<string, Look> looks = style.Assign(layers);
                Console.WriteLine();
                Console.WriteLine("--- {0}: {1}", style.Name, style.Comment);
                foreach (FsaStackLayer layer in layers)
                {
                    Look look = looks[layer.Name];
                    Console.WriteLine("   {0,-16} {1,6:F2} %  #{2:X6}{3}",
                                      layer.Name, layer.SharePercent,
                                      look.Fill.ToArgb() & 0xFFFFFF,
                                      look.Hatched ? "  штриховка" : "");
                }

                string path = Path.Combine(outDir, "fsa_" + style.Name + ".png");
                using (Bitmap bmp = Render(result, layers, looks, style, rd.EnergySpectrum,
                                           background, width, height))
                {
                    bmp.Save(path, ImageFormat.Png);
                }
                Console.WriteLine("   -> {0}", path);
            }

            return 0;
        }

        // ------------------------------------------------------------------
        // Варианты оформления
        // ------------------------------------------------------------------

        sealed class Look
        {
            public Color Fill;
            public bool Hatched;
        }

        sealed class Style
        {
            public string Name;
            public string Comment;
            public Func<List<FsaStackLayer>, Dictionary<string, Look>> Assign;

            public static readonly Style[] All =
            {
                new Style { Name = "app", Comment = "палитра приложения", Assign = AssignApp }
            };
        }

        /// <summary>Палитра приложения — та самая, что рисует график.</summary>
        static Dictionary<string, Look> AssignApp(List<FsaStackLayer> layers)
        {
            var map = new Dictionary<string, Look>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Color> colors = FsaPalette.Assign(layers.ConvertAll(l => l.Name));
            foreach (FsaStackLayer layer in layers)
            {
                Color color;
                colors.TryGetValue(layer.Name, out color);
                map[layer.Name] = new Look { Fill = color };
            }

            return map;
        }

        /// <summary>Именная таблица, дополненная производными образами.</summary>
        static readonly Dictionary<string, Color> Named = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            { "Th-232", Hex(0x0072B2) }, { "Ra-226", Hex(0xD55E00) }, { "U-238", Hex(0xE69F00) },
            { "U-235", Hex(0x009E73) }, { "K-40", Hex(0xF0E442) },   { "Cs-137", Hex(0xCC79A7) },
            { "Am-241", Hex(0x6A3D9A) }, { "Co-60", Hex(0x332288) }, { "I-131", Hex(0x117733) },
            { "Eu-152", Hex(0x44AA99) }, { "Ba-133", Hex(0x999933) }, { "Lu-176", Hex(0x88CCEE) },
            { "Th-228", Hex(0x44AA99) },
            // производные образы — своя, «приборная» гамма: холодные серо-синие
            // и коричневые, никем из нуклидов не занятые
            { "Backscatter", Hex(0x7A8B99) }, { "Backscatter180", Hex(0x4C5B69) },
            { "Xray-W", Hex(0x997700) }, { "Xray-Pb", Hex(0x6B4E3D) },
            { "SE-2614", Hex(0xDDCC77) }, { "DE-2614", Hex(0x805B3A) },
            { "other", Hex(0x9E9E9E) }, { "continuum", Hex(0xB0B7BD) }
        };

        /// <summary>
        /// Резерв для незнакомых НУКЛИДОВ. Цветов именной таблицы в нём нет, и
        /// все тона насыщенные: первый макет выдал Ac-228 (61 % спектра) серый
        /// #7F7F7F из приглушённой части резерва, и главный нуклид спектра
        /// прочитался как приборный артефакт. Приглушённое остаётся за
        /// артефактами, и смешивать эти две гаммы нельзя.
        /// </summary>
        static readonly Color[] Reserve =
        {
            Hex(0x56B4E9), Hex(0xE41A1C), Hex(0x984EA3), Hex(0x7FBC41),
            Hex(0x00BFC4), Hex(0x8DA0CB), Hex(0xFF9DA7), Hex(0x59A14F)
        };

        static Dictionary<string, Look> AssignNamed(List<FsaStackLayer> layers)
        {
            var map = new Dictionary<string, Look>(StringComparer.OrdinalIgnoreCase);
            int fallback = 0;
            foreach (FsaStackLayer layer in layers)
            {
                Color color;
                if (!Named.TryGetValue(layer.Name, out color))
                {
                    color = Reserve[fallback++ % Reserve.Length];
                }

                map[layer.Name] = new Look { Fill = color };
            }

            return map;
        }

        /// <summary>
        /// То же, но резервный цвет выбирается с оглядкой на уже занятые в ЭТОМ
        /// спектре: незнакомый образ не может получить цвет соседа по легенде.
        /// </summary>
        static Dictionary<string, Look> AssignFree(List<FsaStackLayer> layers)
        {
            var map = new Dictionary<string, Look>(StringComparer.OrdinalIgnoreCase);
            var taken = new List<Color>();
            foreach (FsaStackLayer layer in layers)
            {
                Color color;
                if (Named.TryGetValue(layer.Name, out color))
                {
                    taken.Add(color);
                }
            }

            int next = 0;
            foreach (FsaStackLayer layer in layers)
            {
                Color color;
                if (!Named.TryGetValue(layer.Name, out color))
                {
                    do
                    {
                        color = Reserve[next++ % Reserve.Length];
                    }
                    while (next <= Reserve.Length && taken.Any(c => Near(c, color)));
                    taken.Add(color);
                }

                map[layer.Name] = new Look { Fill = color };
            }

            return map;
        }

        /// <summary>
        /// Плюс различие по КАТЕГОРИИ: нуклид — плотная заливка, приборный
        /// артефакт — приглушённая со штриховкой. Совпадение оттенка после
        /// этого перестаёт быть ошибкой чтения.
        /// </summary>
        static Dictionary<string, Look> AssignCategory(List<FsaStackLayer> layers)
        {
            Dictionary<string, Look> map = AssignFree(layers);
            foreach (FsaStackLayer layer in layers)
            {
                if (layer.Kind != FsaComponentKind.Nuisance
                    && !string.Equals(layer.Name, "other", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Look look = map[layer.Name];
                look.Hatched = true;
                look.Fill = Desaturate(look.Fill, 0.55);
            }

            return map;
        }

        static bool Near(Color a, Color b)
        {
            return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 90;
        }

        static Color Desaturate(Color c, double amount)
        {
            double grey = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return Color.FromArgb(
                (int)(c.R + (grey - c.R) * amount),
                (int)(c.G + (grey - c.G) * amount),
                (int)(c.B + (grey - c.B) * amount));
        }

        static Color Hex(int rgb)
        {
            return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }

        // ------------------------------------------------------------------
        // Отрисовка
        // ------------------------------------------------------------------

        /// <summary>
        /// Стек по логарифму, как в приложении: ленты снизу вверх, поверх —
        /// белая линия модели и зелёная линия измеренного спектра.
        /// </summary>
        static Bitmap Render(FsaResult result, List<FsaStackLayer> layers,
                             Dictionary<string, Look> looks, Style style,
                             EnergySpectrum spectrum, EnergySpectrum background,
                             int width, int height)
        {
            const int Left = 60, Right = 20, Top = 34, Bottom = 40;
            int plotW = width - Left - Right, plotH = height - Top - Bottom;
            int channels = spectrum.NumberOfChannels;
            EnergyCalibration cal = spectrum.EnergyCalibration;

            double eLo = 0.0, eHi = 1000.0;   // весь интересный кусок цезиевого спектра
            double[] net = new double[channels];
            double scale = 0.0;
            if (background != null)
            {
                double bgLive = background.LiveTime > 0.0 ? background.LiveTime : background.MeasurementTime;
                double live = spectrum.LiveTime > 0.0 ? spectrum.LiveTime : spectrum.MeasurementTime;
                scale = bgLive > 0.0 ? live / bgLive : 0.0;
            }
            for (int i = 0; i < channels; i++)
            {
                double v = spectrum.Spectrum[i];
                if (scale > 0.0 && i < background.Spectrum.Length) v -= background.Spectrum[i] * scale;
                net[i] = v;
            }

            double top = 1.0;
            for (int i = 0; i < channels; i++)
            {
                double e = cal.ChannelToEnergy(i);
                if (e < eLo || e > eHi) continue;
                if (net[i] > top) top = net[i];
            }

            Func<double, double> ylog = v => v <= 1.0 ? 0.0 : Math.Log10(v) / Math.Log10(top * 1.3);
            Func<double, int> ypix = v => Top + plotH - (int)Math.Round(Math.Max(0.0, Math.Min(1.0, ylog(v))) * plotH);
            Func<double, int> xpix = e => Left + (int)Math.Round((e - eLo) / (eHi - eLo) * plotW);

            var bmp = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(0x2B, 0x36, 0x3C));
                g.SmoothingMode = SmoothingMode.None;

                // сетка и шкала
                using (var grid = new Pen(Color.FromArgb(60, Color.White)))
                using (var font = new Font("Segoe UI", 8f))
                using (Brush text = new SolidBrush(Color.FromArgb(200, Color.White)))
                {
                    for (double e = 0; e <= eHi; e += 100)
                    {
                        int x = xpix(e);
                        g.DrawLine(grid, x, Top, x, Top + plotH);
                        g.DrawString(((int)e).ToString(), font, text, x - 12, Top + plotH + 4);
                    }

                    g.DrawString(style.Name + " — " + style.Comment, new Font("Segoe UI", 10f, FontStyle.Bold),
                                 text, Left, 8);
                }

                // накопленные суммы слоёв
                double[][] cum = new double[layers.Count][];
                for (int k = 0; k < layers.Count; k++)
                {
                    cum[k] = new double[channels];
                    for (int i = 0; i < channels; i++)
                    {
                        cum[k][i] = (k > 0 ? cum[k - 1][i] : 0.0)
                                  + (i < layers[k].Curve.Length ? layers[k].Curve[i] : 0.0);
                    }
                }

                for (int k = 0; k < layers.Count; k++)
                {
                    Look look = looks[layers[k].Name];
                    Brush brush = look.Hatched
                        ? (Brush)new HatchBrush(HatchStyle.ForwardDiagonal,
                                                Color.FromArgb(235, look.Fill),
                                                Color.FromArgb(160, look.Fill))
                        : new SolidBrush(Color.FromArgb(235, look.Fill));
                    using (brush)
                    {
                        for (int x = Left; x < Left + plotW; x++)
                        {
                            double e = eLo + (eHi - eLo) * (x - Left) / (double)plotW;
                            int ch = ClampChannel(cal, e, channels);
                            double lower = k > 0 ? cum[k - 1][ch] : 0.0;
                            double upper = cum[k][ch];
                            if (upper <= 1.0) continue;
                            int yTop = ypix(upper);
                            int yBottom = lower > 1.0 ? ypix(lower) : Top + plotH;
                            if (yBottom > yTop) g.FillRectangle(brush, x, yTop, 1, yBottom - yTop);
                        }
                    }
                }

                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawCurve(g, Color.FromArgb(200, Color.White), cum[layers.Count - 1], cal, channels,
                          eLo, eHi, Left, plotW, ypix);
                DrawCurve(g, Color.FromArgb(220, 0x7C, 0xE0, 0x7C), net, cal, channels,
                          eLo, eHi, Left, plotW, ypix);

                DrawLegend(g, layers, looks, result, width);
            }

            return bmp;
        }

        static void DrawCurve(Graphics g, Color color, double[] values, EnergyCalibration cal,
                              int channels, double eLo, double eHi, int left, int plotW,
                              Func<double, int> ypix)
        {
            var points = new List<Point>();
            for (int x = left; x < left + plotW; x++)
            {
                double e = eLo + (eHi - eLo) * (x - left) / (double)plotW;
                int ch = ClampChannel(cal, e, channels);
                double v = ch < values.Length ? values[ch] : 0.0;
                if (v <= 1.0) continue;
                points.Add(new Point(x, ypix(v)));
            }

            if (points.Count > 1)
            {
                using (var pen = new Pen(color, 1.2f))
                {
                    g.DrawLines(pen, points.ToArray());
                }
            }
        }

        static void DrawLegend(Graphics g, List<FsaStackLayer> layers, Dictionary<string, Look> looks,
                               FsaResult result, int width)
        {
            using (var font = new Font("Segoe UI", 9f))
            using (Brush text = new SolidBrush(Color.White))
            using (Brush box = new SolidBrush(Color.FromArgb(190, 0x1E, 0x26, 0x2B)))
            {
                int w = 250, rowH = 17;
                int h = rowH * (layers.Count + 1) + 10;
                int x0 = width - w - 30, y0 = 44;
                g.FillRectangle(box, x0, y0, w, h);
                for (int k = 0; k < layers.Count; k++)
                {
                    Look look = looks[layers[k].Name];
                    int y = y0 + 6 + k * rowH;
                    Brush swatch = look.Hatched
                        ? (Brush)new HatchBrush(HatchStyle.ForwardDiagonal,
                                                look.Fill, Color.FromArgb(160, look.Fill))
                        : new SolidBrush(look.Fill);
                    using (swatch)
                    {
                        g.FillRectangle(swatch, x0 + 8, y + 3, 12, 10);
                    }

                    g.DrawString(FsaPalette.DisplayName(layers[k].Name), font, text, x0 + 26, y);
                    g.DrawString(layers[k].SharePercent.ToString("F2") + " %", font, text, x0 + w - 60, y);
                }

                g.DrawString("chi2/ndf " + result.Chi2Ndf.ToString("F2"), font, text,
                             x0 + 26, y0 + 6 + layers.Count * rowH);
            }
        }

        static int ClampChannel(EnergyCalibration cal, double energy, int channels)
        {
            double ch;
            try { ch = cal.EnergyToChannel(energy, channels); }
            catch { return 0; }
            if (double.IsNaN(ch) || ch < 0.0) return 0;
            if (ch > channels - 1) return channels - 1;
            return (int)Math.Round(ch);
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList.First();
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++) total += s.Spectrum[i];
                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig)
                && rd.DeviceConfig != null
                && rd.DeviceConfig.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fromDevice)
            {
                rd.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)fromDevice.Clone();
            }

            // ПШПВ-калибровка: файл может её не содержать — приложение в этом
            // случае берёт её из конфигурации метода поиска, достраивая
            // умолчанием (DocEnergySpectrum, тот же порядок и те же проверки).
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
