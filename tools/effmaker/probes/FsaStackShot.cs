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
    ///                [--infer] [--no-equilibrium] [--no-matrix] [--lib-dump]
    ///                [--set=Ra-226] [--lines=Esc-I]
    ///                [--no-atomic] [--no-room] [--no-backscatter] [--refit-z=0]
    ///                [--from=200] [--to=700] [--ceiling=2000] [--width=1400]
    ///
    /// `--ceiling` подрезает шкалу отсчётов: без него высокие пики забирают всё
    /// поле, и мелкая структура (сумм-пики) сливается с осью.
    ///
    /// `--set=` — тот самый выбор «Use set:» из панели поиска пиков. Без него
    /// проба берёт `ActiveSet` = null, то есть ВСЕ нуклиды, и подписи пиков
    /// расходятся с экраном человека; а подписи задают состав (`S57`), так что
    /// снимок получается не про тот спектр. Имя сверяется с именами наборов
    /// `NuclideDefinitionManager.NuclideSets`, промах — отказ, а не молчание.
    ///
    /// `--lines=` — линии ОДНОГО образа с весами и нуклидом-родителем. Заведён
    /// под `S80`: по картинке видно, что лента образа стоит не там, где ей
    /// положено, а чем именно она набрана — по картинке не видно.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null, efficiencyName = null, outPath = "stack.png";
            string setName = null, linesOf = null;
            // Развязка гейтов и приборных образов — `S81`: «кто чью полку
            // забирает» иначе не разделить. `refitZ` NaN — не трогать умолчание
            // анализатора, а не «поставить ноль».
            bool atomic = true, backscatter = true, room = true;
            double refitZ = double.NaN;
            // (S69/S70) Ветка галки «состав из баз»: библиотеку собирает
            // `FsaSampleLibrary` по выведенному составу — ровно то, что видит
            // человек с включённой галкой. Без ключа остаётся прежний путь.
            bool infer = false, equilibrium = true, needMatrix = true, libDump = false;
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
                else if (a == "--infer") infer = true;
                else if (a == "--no-matrix") needMatrix = false;
                else if (a == "--lib-dump") libDump = true;
                else if (a.StartsWith("--set=", StringComparison.Ordinal)) setName = a.Substring(6);
                else if (a.StartsWith("--lines=", StringComparison.Ordinal)) linesOf = a.Substring(8);
                else if (a == "--no-atomic") atomic = false;
                else if (a == "--no-room") room = false;
                else if (a == "--no-backscatter") backscatter = false;
                else if (a.StartsWith("--refit-z=", StringComparison.Ordinal))
                    refitZ = double.Parse(a.Substring(10), CultureInfo.InvariantCulture);
                else if (a == "--no-equilibrium") equilibrium = false;
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

            // Набор — ДО поиска пиков: им подписываются пики, а подписи задают
            // выведенный состав (`S57`). Прочитанный после, он не изменил бы
            // ничего и врал бы молча.
            if (setName != null && !SelectSet(nuclides, setName))
            {
                return 2;
            }

            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            List<FsaComponent> library;
            if (infer)
            {
                FsaCompositionInference.Report report;
                FsaSampleSpec spec = FsaCompositionInference.Infer(peaks, rd, out report);
                spec.Equilibrium = equilibrium;
                spec.AtomicXray = atomic;
                spec.Room = room;
                library = FsaSampleLibrary.Build(spec);
                Console.WriteLine("состав: " + report);
                peaks = new PeakDetector().DetectPeak(
                    rd, BackgroundMode.Invisible, SmoothingMethod.None,
                    null, FsaSampleLibrary.AsDefinitions(library));
            }
            else
            {
                library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
            }
            // Состав библиотеки построчно — мерка приёмки `S70`: связка
            // равновесия не смеет убирать ни одного компонента, кроме
            // слияния членов ряда, и проверяется это сравнением ДВУХ таких
            // распечаток, а не рассуждением.
            if (libDump)
            {
                foreach (FsaComponent c in library)
                {
                    Console.WriteLine("LIB	{0}	{1}	{2}", c.Name, c.Kind, c.Lines.Count);
                }
            }

            // (S80) Линии образа — с весом и нуклидом-родителем. Печатается ДО
            // разбора: вопрос «чем набрана эта лента» относится к ОБРАЗУ, а не
            // к тому, что из него вышло после фита.
            if (linesOf != null)
            {
                bool found = false;
                foreach (FsaComponent c in library)
                {
                    if (!string.Equals(c.Name, linesOf, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    found = true;
                    Console.WriteLine("линии образа {0} ({1}, всего {2}):", c.Name, c.Kind, c.Lines.Count);
                    foreach (FsaLine line in c.Lines)
                    {
                        Console.WriteLine("LINE\t{0}\t{1}\t{2}",
                                          line.Energy.ToString("F3", CultureInfo.InvariantCulture),
                                          line.Intensity.ToString("E4", CultureInfo.InvariantCulture),
                                          line.Nuclide);
                    }
                }

                if (!found)
                {
                    Console.Error.WriteLine("образа «{0}» в библиотеке нет", linesOf);
                }
            }

            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста");
                return 1;
            }

            ResponseMatrix matrix = ResponseMatrixStore.Load(rd.Efficiency != null ? rd.Efficiency.Guid : null);
            if (matrix == null || rd.Efficiency == null || !rd.Efficiency.HasGeometry
                || !matrix.IsValidFor(rd.Efficiency.Geometry))
            {
                // ⛔ Молчать об этом нельзя: снимок без матрицы — ДРУГОЕ
                // разложение, и выдавать его за настоящее уже случалось. Ключ
                // `--no-matrix` заведён затем, чтобы смотреть на ЛЕГЕНДУ там,
                // где кривой у спектра нет вовсе, и отказ остаётся отказом,
                // пока о нём не попросили вслух.
                if (needMatrix)
                {
                    Console.Error.WriteLine("матрицы нет или отпечаток не сошёлся — рисовать нечего");
                    return 1;
                }

                Console.WriteLine("⚠ БЕЗ МАТРИЦЫ (--no-matrix): разложение другое, числа не сравнивать");
                matrix = null;
            }

            // Фон подаётся ТОТ ЖЕ, что в окне приложения. До 15.08.2026 здесь
            // стоял null, и снимок показывал разбор без вычитания фона, выдавая
            // его за настоящий: у `G1S_K40_Denta` это χ²/ndf 3.62 против 1.72.
            // (ключ снят по B6 15.08.2026, тот же спектр — `G1S24_K40_Denta120`)
            var analyzer = new FsaAnalyzer();
            if (matrix != null)
            {
                analyzer.ResponseMatrix = matrix;

                // ⛔ Вещество кристалла идёт ВМЕСТЕ с матрицей — так его ставит
                // `FsaOverlay`, и без него каскадное суммирование считает сумму
                // не по свету (`S20`). Проба, собирающая анализатор иначе, чем
                // приложение, показывает не тот разбор, ради показа которого
                // заведена.
                analyzer.ScintillatorMaterial =
                    EfficiencySimulator.ScintillatorNameOf(rd.Efficiency.Geometry);
            }

            // Окно совпадения — мёртвое время прибора (`S27`), диапазон — тот
            // же, что у поиска пиков. Обе строки повторяют `FsaOverlay`.
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

            analyzer.Backscatter = backscatter;
            if (!double.IsNaN(refitZ))
            {
                analyzer.RefitZ = refitZ;
            }
            FsaResult result = analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum,
                                                rd.FwhmCalibration,
                                                library, FsaEfficiency.FromConfig(rd.Efficiency));
            if (result == null)
            {
                Console.Error.WriteLine("разложение не получилось");
                return 1;
            }

            Console.WriteLine("chi2/ndf {0:F3}, невязка модели {1:F1} %, суммирование {2}",
                              result.Chi2Ndf, result.ModelResidual * 100.0,
                              result.CascadeSummingUsed ? "да" : "нет");

            // Состав и подавленные — ТЕКСТОМ рядом с картинкой (`S81`). Доля
            // берётся у слоёв, а не у компонентов: у приборных образов
            // `SharePercent` компонента считается по пиковым окнам, а печатает
            // экран долю СЛОЯ (`S76`), и сравнивать надо с тем, что видно.
            List<FsaStackLayer> shot = result.BuildStackedLayers(9);
            foreach (FsaStackLayer layer in shot)
            {
                Console.WriteLine("ROW\t{0}\t{1}\t{2}", layer.Name, layer.Kind,
                                  layer.SharePercent.ToString("F3", CultureInfo.InvariantCulture));
            }

            foreach (FsaSuppressedImage cut in result.SuppressedImages)
            {
                Console.WriteLine("CUT\t{0}\t{1}\tz={2}", cut.Name, cut.Kind,
                                  cut.Z.ToString("F2", CultureInfo.InvariantCulture));
            }

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

        /// <summary>
        /// Поставить набор нуклидов активным — то же поле, что правит панель
        /// поиска пиков (<c>NuclideDefinitionManager.ActiveSet</c>). Промах по
        /// имени — отказ со списком того, что есть: молчаливый откат на «все
        /// нуклиды» дал бы другой состав и выглядел бы как работающий прогон.
        /// </summary>
        static bool SelectSet(NuclideDefinitionManager nuclides, string name)
        {
            var have = new List<string>();
            foreach (NuclideSet set in nuclides.NuclideSets)
            {
                if (set == null)
                {
                    continue;
                }

                have.Add(set.Name);
                if (string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    nuclides.ActiveSet = set;
                    Console.WriteLine("набор: {0}", set.Name);
                    return true;
                }
            }

            Console.Error.WriteLine("набора «{0}» нет; есть: {1}", name, string.Join(", ", have.ToArray()));
            return false;
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

            // ⛔ Настройки поиска пиков — ИЗ КОНФИГУРАЦИИ ПРИБОРА, если своих у
            // спектра в файле нет. В приложении это делает `DocumentManager`
            // через `FWHMPeakDetectionMethodConfig.AdoptFrom` при открытии
            // документа, и без этого проба ищет пики другим SNR и другим
            // допуском, а анализатор получает другой диапазон, — то есть
            // показывает разбор не того спектра, который человек видит на
            // экране. То же самое и по той же причине делает `CorpusFsaProbe`.
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
