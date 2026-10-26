using BecquerelMonitor;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace CompressedAxisShot
{
    /// <summary>
    /// (ПРОТОТИП, задача Amber 26.10.2026) СНИМКИ СЖАТОЙ ОСИ X — «сумма каналов
    /// столбца» против прежнего «один канал на столбец».
    ///
    ///   compressedaxisshot --spectrum=&lt;файл&gt; --out=&lt;каталог&gt; --tag=&lt;до|после&gt;
    ///                      [--hs=0.125,0.116] [--rebin=8] [--scale=log|lin]
    ///
    /// ⛔ Окно приложения не запускается: документ — настоящий
    /// <c>DocEnergySpectrum</c> в форме-носителе за краем экрана, кадр снимает
    /// настоящий <c>EnergySpectrumView.OnPaint</c> (<c>DrawToBitmap</c>). Своей
    /// отрисовки у пробы нет.
    ///
    /// Снимки: спектр как есть при каждом <c>--hs</c> (масштаб поля «X-Axis»,
    /// пикселей на канал) и КОНТРОЛЬ — тот же спектр, перегруппированный по
    /// <c>--rebin</c> каналов (сумма), при масштабе <c>hs·rebin</c>, то есть
    /// при том же числе пикселей на кэВ. Новая отрисовка обязана совпасть с
    /// контролем по форме и уровню. Печатает площадь видимого (сумма высот
    /// столбцов) — у новой она равна сумме отсчётов, у прежней в rebin раз меньше.
    /// Плюс панель курсора на вершине самого высокого пика в 500…700 кэВ.
    /// </summary>
    static class Program
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// `--select-kev=a..b` (жёлтые линии ПШПВ, дефект AMBER266 26.10.2026):
        /// выделение по энергиям вместо курсора; печатается верх жёлтой
        /// вертикали в пикселях против верха нарисованного столбца под ней.
        /// </summary>
        static double[] selectKev;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string spectrumPath = null, outDir = null, tag = "shot", scale = "log", writeRebin = null;
            double[] scales = { 0.125, 0.116 };
            int rebin = 8;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outDir = a.Substring(6);
                else if (a.StartsWith("--tag=", StringComparison.Ordinal)) tag = a.Substring(6);
                else if (a.StartsWith("--scale=", StringComparison.Ordinal)) scale = a.Substring(8);
                else if (a.StartsWith("--rebin=", StringComparison.Ordinal)) rebin = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--write-rebin=", StringComparison.Ordinal)) writeRebin = a.Substring(14);
                else if (a.StartsWith("--select-kev=", StringComparison.Ordinal))
                {
                    string[] ends = a.Substring(13).Split(new[] { ".." }, StringSplitOptions.RemoveEmptyEntries);
                    selectKev = new[] { double.Parse(ends[0], CultureInfo.InvariantCulture), double.Parse(ends[1], CultureInfo.InvariantCulture) };
                }
                else if (a.StartsWith("--hs=", StringComparison.Ordinal))
                    scales = Array.ConvertAll(a.Substring(5).Split(','), s => double.Parse(s, CultureInfo.InvariantCulture));
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (spectrumPath != null && writeRebin != null)
            {
                // перегруппированный файл спектра — для контроля FSA (FsaStackShot)
                var ser = new XmlSerializer(typeof(ResultDataFile));
                ResultDataFile f;
                using (var st = new FileStream(spectrumPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    f = (ResultDataFile)ser.Deserialize(st);
                ResultData rd0 = f.ResultDataList[0];
                rd0.EnergySpectrum = Rebin(rd0.EnergySpectrum, rebin);
                if (rd0.BackgroundEnergySpectrum != null) rd0.BackgroundEnergySpectrum = Rebin(rd0.BackgroundEnergySpectrum, rebin);
                using (var st = new FileStream(writeRebin, FileMode.Create, FileAccess.Write))
                    ser.Serialize(st, f);
                Console.WriteLine("перегруппирован по {0}: {1}", rebin, writeRebin);
                return 0;
            }
            if (spectrumPath == null || outDir == null) { Console.Error.WriteLine("нужны --spectrum= и --out="); return 2; }
            Directory.CreateDirectory(outDir);
            Console.WriteLine("приложение: " + typeof(EnergySpectrumView).Assembly.Location);
            bool fixedBuild = typeof(EnergySpectrumView).GetMethod("OverlapSum", BindingFlags.Static | BindingFlags.NonPublic,
                                                                   null, new[] { typeof(double[]), typeof(double), typeof(double) }, null) != null;
            Console.WriteLine("сборка: " + (fixedBuild ? "С ПРАВКОЙ (сумма каналов столбца)" : "БЕЗ ПРАВКИ (один канал на столбец)"));

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();
            MainForm mainForm = new MainForm();

            foreach (double hs in scales)
            {
                Shot(spectrumPath, 1, hs, scale, Path.Combine(outDir, string.Format(CultureInfo.InvariantCulture, "{0}_orig_hs{1:0.000}.png", tag, hs)));
            }
            // контроль: перегруппировка в N/rebin каналов при том же числе пикселей на кэВ
            Shot(spectrumPath, rebin, scales[0] * rebin, scale,
                 Path.Combine(outDir, string.Format(CultureInfo.InvariantCulture, "{0}_rebin{1}_hs{2:0.000}.png", tag, rebin, scales[0] * rebin)));
            mainForm.Dispose();
            return 0;
        }

        static void Shot(string path, int rebin, double hs, string scale, string png)
        {
            DocEnergySpectrum doc = OpenDocument(path, rebin);
            EnergySpectrum s = doc.ActiveResultData.EnergySpectrum;
            int plotWidth = (int)Math.Ceiling(s.NumberOfChannels * hs) + 30;
            Form host = Host(doc, plotWidth + 90, 420);
            EnergySpectrumView view = doc.EnergySpectrumView;
            view.VerticalScaleType = scale == "lin" ? VerticalScaleType.LinearScale : VerticalScaleType.LogarithmicScale;
            view.HorizontalUnit = HorizontalUnit.Energy;
            view.PrepareViewData();
            view.HorizontalScale = hs;
            view.PrepareViewData();
            view.RecalcScrollBar();

            // курсор — на вершину самого высокого пика 500…700 кэВ (Tl-208 583 у тория)
            int peakCh = -1;
            double best = -1;
            for (int i = 0; i < s.NumberOfChannels; i++)
            {
                double e = s.EnergyCalibration.ChannelToEnergy(i);
                if (e < 500 || e > 700) continue;
                if (s.Spectrum[i] > best) { best = s.Spectrum[i]; peakCh = i; }
            }
            if (selectKev != null)
            {
                view.SelectionStart = (int)Math.Round(s.EnergyCalibration.EnergyToChannel(selectKev[0], s.NumberOfChannels));
                view.SelectionEnd = (int)Math.Round(s.EnergyCalibration.EnergyToChannel(selectKev[1], s.NumberOfChannels));
            }
            else
            {
                int x = (int)Invoke(view, "PeakX", (double)peakCh);
                Set(view, "cursorX", x);
                Set(view, "cursorChannel", peakCh);
            }

            using (Bitmap frame = new Bitmap(view.Width, view.Height))
            {
                view.DrawToBitmap(frame, new Rectangle(0, 0, view.Width, view.Height));
                frame.Save(png, ImageFormat.Png);
                if (selectKev != null)
                {
                    MeasureFwhm(view, s, frame);
                }
            }

            // площадь видимого: сумма высот столбцов в отсчётах — тем же правилом,
            // что рисует вид (карта кадра; у старой сборки — один канал)
            double drawnSum = DrawnSum(view, s);
            long total = 0;
            foreach (int c in s.Spectrum) total += c;
            Console.WriteLine("{0}: каналов {1}, hs {2:0.000} (каналов на столбец {3:0.00}), сумма отсчётов {4}, сумма высот столбцов {5:0} ({6:0.000} от суммы)",
                              Path.GetFileName(png), s.NumberOfChannels, hs, 1.0 / hs, total, drawnSum, drawnSum / total);
            host.Dispose();
            doc.Dispose();
        }

        /// <summary>
        /// Верх жёлтой вертикали ПШПВ (пиксели `Pens.Yellow` в её столбце) против
        /// верха столбца спектра под ней — `GetSpectrumValueY` того значения,
        /// которое вид рисует в этом столбце (сумма при сжатии, иначе канал).
        /// </summary>
        static void MeasureFwhm(EnergySpectrumView view, EnergySpectrum s, Bitmap frame)
        {
            object an = Get(view, "selectionAnalytics");
            object fwhm = an == null ? null : an.GetType().GetProperty("FwhmResult", Any).GetValue(an, null);
            if (fwhm == null)
            {
                Console.WriteLine("  ПШПВ: результата нет");
                return;
            }

            double maxChannel = Convert.ToDouble(fwhm.GetType().GetProperty("MaxChannel").GetValue(fwhm, null), CultureInfo.InvariantCulture);
            int x = (int)Invoke(view, "PeakX", maxChannel);
            if (x < 1 || x >= frame.Width - 1)
            {
                Console.WriteLine("  ПШПВ: вершина вне кадра (x = {0}), мерить нечего", x);
                return;
            }

            // вертикаль — столбец (x−1…x+1), где жёлтых точек БОЛЬШЕ всего:
            // в соседнем может лежать лишь точка линии полувысоты или базы
            int yellowTop = -1, bestCount = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                int count = 0, top = -1;
                for (int y = 0; y < frame.Height; y++)
                {
                    Color c = frame.GetPixel(x + dx, y);
                    if (c.R == 255 && c.G == 255 && c.B == 0) { count++; if (top < 0) top = y; }
                }
                if (count > bestCount) { bestCount = count; yellowTop = top; }
            }

            double drawn;
            MethodInfo tryCol = typeof(EnergySpectrumView).GetMethod("TryCompressedColumnValue", Any);
            object[] a = { s.DrawingSpectrum, x, 0.0 };
            if (tryCol != null && (bool)tryCol.Invoke(view, a)) drawn = (double)a[2];
            else drawn = s.DrawingSpectrum[(int)Math.Round(maxChannel)];
            if ((VerticalUnit)Get(view, "verticalUnit") == VerticalUnit.CountsPerSecond && s.EffectiveLiveTime != 0.0) drawn /= s.EffectiveLiveTime;
            int drawnTop = (int)Invoke(view, "GetSpectrumValueY", drawn);
            Console.WriteLine("  ПШПВ: вершина в канале {0:0.0}, столбец x = {1}; верх жёлтой вертикали y = {2}, верх нарисованного столбца y = {3}, расхождение {4} пкс",
                              maxChannel, x, yellowTop, drawnTop, yellowTop < 0 ? "—" : (yellowTop - drawnTop).ToString(CultureInfo.InvariantCulture));
        }

        static double DrawnSum(EnergySpectrumView view, EnergySpectrum s)
        {
            MethodInfo channelAt = typeof(EnergySpectrumView).GetMethod("ChannelAtPixel", Any);
            MethodInfo tryCol = typeof(EnergySpectrumView).GetMethod("TryCompressedColumnValue", Any);
            int left = (int)Get(view, "left");
            int width = (int)Get(view, "width");
            double sum = 0;
            for (int x = left + 1; x < left + width; x++)
            {
                if (tryCol != null)
                {
                    object[] a = { s.DrawingSpectrum, x, 0.0 };
                    if ((bool)tryCol.Invoke(view, a)) { sum += (double)a[2]; continue; }
                }
                int ch = (int)channelAt.Invoke(view, new object[] { s, s.EnergyCalibration, x });
                if (ch >= 0 && ch < s.DrawingSpectrum.Length) sum += s.DrawingSpectrum[ch];
            }
            return sum;
        }

        static DocEnergySpectrum OpenDocument(string path, int rebin)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }
            ResultData rd = file.ResultDataList[0];
            rd.BackgroundEnergySpectrum = null;
            EnergySpectrum s = rd.EnergySpectrum;
            if (rebin > 1)
            {
                s = Rebin(s, rebin);
                rd.EnergySpectrum = s;
            }
            if (s.TotalPulseCount == 0)
            {
                long total = 0;
                foreach (int v in s.Spectrum) total += v;
                s.TotalPulseCount = total; s.ValidPulseCount = total;
            }
            ProbeDeviceConfig.Attach(rd);
            if (rd.MeasurementController == null) rd.MeasurementController = new MeasurementController(null, rd);
            var doc = new DocEnergySpectrum(path);
            doc.ResultDataFile = file;
            doc.ActiveResultDataIndex = 0;
            doc.UpdateEnergySpectrum();
            return doc;
        }

        static EnergySpectrum Rebin(EnergySpectrum s, int rebin)
        {
            int n = s.NumberOfChannels / rebin;
            int[] grouped = new int[n];
            for (int i = 0; i < n * rebin; i++) grouped[i / rebin] += s.Spectrum[i];
            // калибровка группы j: центр группы — канал rebin·j + (rebin − 1)/2
            var cal = (PolynomialEnergyCalibration)s.EnergyCalibration;
            double[] c = cal.Coefficients;
            double shift = (rebin - 1) / 2.0;
            var nc = new double[c.Length];
            // E(rebin·j + shift) = Σ c_k (rebin·j + shift)^k — разложение бинома
            for (int k = 0; k < c.Length; k++)
                for (int m = 0; m <= k; m++)
                    nc[m] += c[k] * Binom(k, m) * Math.Pow(rebin, m) * Math.Pow(shift, k - m);
            var ncal = new PolynomialEnergyCalibration();
            ncal.PolynomialOrder = cal.PolynomialOrder;
            ncal.Coefficients = nc;
            var ns = new EnergySpectrum(1, n);
            ns.EnergyCalibration = ncal;
            Array.Copy(grouped, ns.Spectrum, n);
            ns.MeasurementTime = s.MeasurementTime;
            ns.LiveTime = s.LiveTime;
            long t = 0; foreach (int v in grouped) t += v;
            ns.TotalPulseCount = t; ns.ValidPulseCount = t;
            return ns;
        }

        static double Binom(int n, int k)
        {
            double r = 1;
            for (int i = 1; i <= k; i++) r = r * (n - k + i) / i;
            return r;
        }

        static Form Host(Form content, int width, int height)
        {
            var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-6000, -4000),
                ShowInTaskbar = false,
                ClientSize = new Size(width, height)
            };
            content.TopLevel = false;
            content.Dock = DockStyle.Fill;
            host.Controls.Add(content);
            content.Show();
            host.Show();
            return host;
        }

        static object Get(object o, string name)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, Any);
                if (f != null) return f.GetValue(o);
            }
            throw new InvalidOperationException("нет поля " + name);
        }

        static void Set(object o, string name, object value)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, Any);
                if (f != null) { f.SetValue(o, value); return; }
            }
            throw new InvalidOperationException("нет поля " + name);
        }

        static object Invoke(object o, string name, params object[] a)
        {
            MethodInfo m = o.GetType().GetMethod(name, Any);
            if (m == null) throw new InvalidOperationException("нет метода " + name);
            return m.Invoke(o, a);
        }
    }
}
