using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace FsaStackShot
{
    /// <summary>
    /// Снимок стека FSA НАСТОЯЩИМ кодом отрисовки — чтобы смотреть глазами на
    /// то, что увидит человек, а не на пересказ.
    ///
    /// Рисует `EnergySpectrumView.ShowFsaOverlay` отражением: вид собирается
    /// без формы, поля вьюпорта выставляются вручную, готовое разложение
    /// кладётся прямо в `FsaOverlay`. Своего рисования здесь нет НАРОЧНО —
    /// проба, рисующая по своим правилам, показала бы не то, что приложение.
    ///
    ///   fsastackshot --spectrum=X.xml [--efficiency=Цилиндр] [--out=stack.png]
    ///                [--from=200] [--to=700] [--ceiling=2000] [--width=1400]
    ///
    /// `--ceiling` подрезает шкалу отсчётов: без него высокие пики забирают всё
    /// поле, и мелкая структура (сумм-пики) сливается с осью.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, efficiencyName = null, outPath = "stack.png";
            double fromKev = 0.0, toKev = 0.0, ceiling = 0.0;
            int width = 1400, height = 700;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--efficiency=", StringComparison.Ordinal)) efficiencyName = a.Substring(13);
                else if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--from=", StringComparison.Ordinal)) fromKev = double.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--to=", StringComparison.Ordinal)) toKev = double.Parse(a.Substring(5), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--ceiling=", StringComparison.Ordinal)) ceiling = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--width=", StringComparison.Ordinal)) width = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--height=", StringComparison.Ordinal)) height = int.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
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
            if (efficiencyName != null && !AttachEfficiency(rd, efficiencyName))
            {
                return 2;
            }

            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста");
                return 1;
            }

            ResponseMatrix matrix = ResponseMatrixStore.Load(rd.Efficiency != null ? rd.Efficiency.Guid : null);
            if (matrix == null || rd.Efficiency == null || !rd.Efficiency.HasGeometry
                || !matrix.IsValidFor(rd.Efficiency.Geometry))
            {
                Console.Error.WriteLine("матрицы нет или отпечаток не сошёлся — рисовать нечего");
                return 1;
            }

            // Фон подаётся ТОТ ЖЕ, что в окне приложения. До 15.08.2026 здесь
            // стоял null, и снимок показывал разбор без вычитания фона, выдавая
            // его за настоящий: у `G1S_K40_Denta` это χ²/ndf 3.62 против 1.72.
            // (ключ снят по B6 15.08.2026, тот же спектр — `G1S24_K40_Denta120`)
            var analyzer = new FsaAnalyzer { ResponseMatrix = matrix };
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration,
                                                library, FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("разложение не получилось");
                return 1;
            }

            Console.WriteLine("chi2/ndf {0:F3}, суммирование {1}",
                              result.Chi2Ndf, result.CascadeSummingUsed ? "да" : "нет");

            EnergySpectrum spectrum = rd.EnergySpectrum;
            EnergyCalibration calibration = spectrum.EnergyCalibration;
            if (toKev <= fromKev)
            {
                fromKev = 0.0;
                toKev = calibration.ChannelToEnergy(spectrum.NumberOfChannels - 1);
            }

            // Потолок шкалы: без него высокие пики забирают поле целиком.
            if (!(ceiling > 0.0))
            {
                ceiling = 0.0;
                for (int i = 0; i < result.Model.Length; i++)
                {
                    if (result.Model[i] > ceiling)
                    {
                        ceiling = result.Model[i];
                    }
                }
            }

            const int left = 1;
            using (var view = new EnergySpectrumView())
            using (var image = new Bitmap(width, height))
            {
                Set(view, "energySpectrum", spectrum);
                Set(view, "energyCalibration", calibration);
                Set(view, "numberOfChannels", spectrum.NumberOfChannels);
                Set(view, "backgroundMode", BackgroundMode.ShowFSA);
                Set(view, "horizontalUnit", HorizontalUnit.Energy);
                Set(view, "verticalUnit", VerticalUnit.Counts);
                Set(view, "verticalScaleType", VerticalScaleType.LinearScale);
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
                Set(view, "energyViewOffset", fromKev);
                Set(view, "pixelPerEnergy", (width - left) / (toKev - fromKev));
                Set(view, "dirty", false);

                // Готовое разложение — прямо в наложение: считать его второй раз
                // фоновым потоком пробе незачем.
                object overlay = Field(typeof(EnergySpectrumView), "fsaOverlay").GetValue(view);
                Field(overlay.GetType(), "result").SetValue(overlay, result);

                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.Black);
                    MethodInfo show = typeof(EnergySpectrumView).GetMethod(
                        "ShowFsaOverlay", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (show == null)
                    {
                        throw new InvalidOperationException("нет EnergySpectrumView.ShowFsaOverlay");
                    }

                    object drawn = show.Invoke(view, new object[] { g });
                    Console.WriteLine("отрисовано: {0}", drawn);

                    // Таблица состава — она же легенда: смотреть на стек без неё
                    // значит проверять половину того, что видит человек.
                    MethodInfo table = typeof(EnergySpectrumView).GetMethod(
                        "DrawFsaOwnTable", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (table == null)
                    {
                        throw new InvalidOperationException("нет EnergySpectrumView.DrawFsaOwnTable");
                    }

                    table.Invoke(view, new object[] { g, 20, 20, 260 });
                }

                image.Save(outPath, ImageFormat.Png);
                Console.WriteLine("{0}: {1}–{2:F0} кэВ, потолок {3:F0}", outPath, fromKev, toKev, ceiling);
            }

            return 0;
        }

        static FieldInfo Field(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
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

        static bool AttachEfficiency(ResultData rd, string name)
        {
            foreach (DeviceConfigInfo device in DeviceConfigManager.GetInstance().DeviceConfigList)
            {
                foreach (EfficiencyConfigData curve in device.EfficiencyConfigs)
                {
                    if (string.Equals(curve.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        rd.Efficiency = curve.Copy();
                        return true;
                    }
                }
            }

            Console.Error.WriteLine("кривая «{0}» не нашлась", name);
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

            if (!(rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig)
                && rd.DeviceConfig != null
                && rd.DeviceConfig.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig fromDevice)
            {
                rd.PeakDetectionMethodConfig = (FWHMPeakDetectionMethodConfig)fromDevice.Clone();
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
