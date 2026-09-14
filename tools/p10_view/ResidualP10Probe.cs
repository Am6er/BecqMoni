// Мерка полосы П10 захода 10.09.2026 по строке `A300` — признак подрезки у
// строки невязки в окне отчёта FSA.
//
// ⛔ ОКНО ПРИЛОЖЕНИЯ НЕ ЗАПУСКАЕТСЯ. Строки заполняет НАСТОЯЩЕЕ окно отчёта
// (`FSAReportView` в форме-носителе за краем экрана), текст берётся из ЕГО
// таблицы, а не из построителя представления: мерить надо то, что увидит
// человек.
//
// ⛔ ЗАЧЕМ СВОЙ ФАЙЛ: `tools/effmaker/probes/**` в этом заходе занят другими
// полосами — читать и запускать можно, править нельзя.
//
//   residualp10probe --spectrum=<файл спектра С ФОНОМ>
//
// Плечи:
//   1. ПОДРЕЗКА СЧЁТОМ: сколько каналов подрезала показная кривая — на спектре
//      с вычтенным фоном и на нём же БЕЗ фона.
//   2. СТРОКА: подпись строки невязки в настоящей таблице, обе культуры.
//      ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — тот же спектр без фона: подрезки нет, и
//      признак ОБЯЗАН МОЛЧАТЬ. Без него «признак есть» ничего не значило бы:
//      подпись, приклеенная всегда, выглядела бы так же.
//   3. ЧИСЛА НЕ ТРОНУТЫ: половины невязки печатаются как есть — решение Amber
//      10.09.2026 «Число оставить, ленту подписать».
//
// ⚠ Матрицы отклика у спектра может не быть — для мерки ЭКРАНА это законно,
// числа отсюда в журнал разбора не идут. Проба говорит об этом вслух.
//
// Коды возврата: 0 — все плечи сошлись; 1 — хоть одно разошлось; 2 — отказ
// оснастки.

using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using XPTable.Models;

namespace ResidualP10Probe
{
    static class Program
    {
        static int bad;

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string spectrumPath = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (spectrumPath == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл спектра с фоном>");
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`):
            // иначе безоконный прогон встаёт на модальном окне.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            Application.EnableVisualStyles();

            try
            {
                // ⛔ ДВА ПЛЕЧА — ОДИН ФАЙЛ, отличающийся РОВНО ОДНИМ: вычтен фон
                // или нет. Разные спектры дали бы разные полосы, разные
                // библиотеки и разные числа, и «признак молчит» читалось бы как
                // свойство ДРУГОГО спектра, а не отсутствия подрезки.
                ResultData withBg = Load(spectrumPath, nuclides);
                ResultData noBg = Load(spectrumPath, nuclides);
                if (withBg == null || noBg == null)
                {
                    return 2;
                }

                if (withBg.BackgroundEnergySpectrum == null)
                {
                    Console.Error.WriteLine("у спектра нет вычтенного фона — подрезке взяться неоткуда, "
                                            + "нужен файл С фоном");
                    return 2;
                }

                noBg.BackgroundEnergySpectrum = null;

                Console.WriteLine("⚠ числа годны только для мерки ЭКРАНА, в журнал разбора не идут");
                FsaResult a = Analyze(withBg, nuclides, "с фоном");
                FsaResult b = Analyze(noBg, nuclides, "БЕЗ фона (контроль)");
                if (a == null || b == null)
                {
                    Console.Error.WriteLine("разбор не состоялся: результат пуст "
                                            + "(с фоном " + (a == null ? "нет" : "есть")
                                            + ", без фона " + (b == null ? "нет" : "есть") + ")");
                    return 2;
                }

                Clamp(withBg, a, noBg, b);
                Rows(withBg, a, noBg, b);
                Numbers(a, b);

                Console.WriteLine();
                Console.WriteLine(bad == 0 ? "ИТОГ: все плечи сошлись" : "ИТОГ: РАЗОШЛОСЬ " + bad);
                return bad == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: " + ex);
                return 2;
            }
        }

        // ==================================================================
        // 1. Подрезка счётом
        // ==================================================================

        static void Clamp(ResultData withBg, FsaResult a, ResultData noBg, FsaResult b)
        {
            Console.WriteLine();
            Console.WriteLine("=== 1. НА СКОЛЬКИХ КАНАЛАХ ПОКАЗНАЯ КРИВАЯ ПОДРЕЗАНА ===");

            int channels = withBg.EnergySpectrum.Spectrum.Length;
            int clampedA = a.ClampedChannels(withBg.EnergySpectrum.Spectrum);
            int clampedB = b.ClampedChannels(noBg.EnergySpectrum.Spectrum);
            int inBandA = ClampedInBand(withBg, a);
            int bandA = BandWidth(a);

            Console.WriteLine("  с фоном:      подрезано {0} из {1} каналов; в полосе модели {2} из {3}",
                              clampedA, channels, inBandA, bandA);
            Console.WriteLine("  БЕЗ фона:     подрезано {0} из {1} каналов", clampedB, channels);

            Say("с фоном подрезка ЕСТЬ", clampedA > 0);
            Say("БЕЗ фона подрезки НЕТ", clampedB == 0);
        }

        /// <summary>
        /// Ширина ПОЛОСЫ ФИТА: `FirstChannel`..`LastChannel` — те самые `chLo`
        /// и `chHi`, по которым считаются половины невязки. Число строки `A300`
        /// (2631 канал из 8075) считано по ней, а не по всему спектру, и без
        /// этой меры сравнить своё число с записанным нечем.
        /// </summary>
        static int BandWidth(FsaResult result)
        {
            return result.LastChannel - result.FirstChannel + 1;
        }

        static int ClampedInBand(ResultData rd, FsaResult result)
        {
            double[] fit = result.FitSpectrum(rd.EnergySpectrum.Spectrum);
            int n = 0;
            for (int i = result.FirstChannel; i <= result.LastChannel && i < fit.Length; i++)
            {
                if (fit[i] < 0.0) n++;
            }

            return n;
        }

        // ==================================================================
        // 2. Строка невязки в НАСТОЯЩЕЙ таблице
        // ==================================================================

        static void Rows(ResultData withBg, FsaResult a, ResultData noBg, FsaResult b)
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. ПОДПИСЬ СТРОКИ НЕВЯЗКИ В ТАБЛИЦЕ ОКНА, обе культуры (A300) ===");

            int clampedA = a.ClampedChannels(withBg.EnergySpectrum.Spectrum);
            foreach (string lang in new[] { "en-US", "ru-RU" })
            {
                Language(lang);

                string capA, valA, tipA;
                bool foundA = ResidualRow(withBg, a, out capA, out valA, out tipA);
                string capB, valB, tipB;
                bool foundB = ResidualRow(noBg, b, out capB, out valB, out tipB);

                Console.WriteLine("  {0} с фоном:  «{1}» = {2}", lang, capA, valA);
                Console.WriteLine("  {0}   подсказка: «{1}»", lang, tipA);
                Console.WriteLine("  {0} без фона: «{1}» = {2}", lang, capB, valB);

                Say(lang + ": строка невязки в таблице ЕСТЬ у обоих плеч", foundA && foundB);

                // Признак говорит — и говорит ЧИСЛОМ, тем самым, что насчитано
                // счётом в разделе 1. Совпадения подстроки мало: подпись
                // «подрезан на N каналах» с ЧУЖИМ N была бы такой же зелёной.
                string number = clampedA.ToString(CultureInfo.InvariantCulture);
                Say(lang + ": с фоном подпись НЕСЁТ число подрезки " + number,
                    foundA && capA.Contains(number));
                Say(lang + ": с фоном подсказка объясняет расхождение",
                    foundA && !string.IsNullOrEmpty(tipA) && tipA.Contains(number)
                    && !string.Equals(tipA, capA, StringComparison.Ordinal));

                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: подрезки нет — признака нет ВОВСЕ,
                // и подпись обязана быть в точности прежней.
                Say(lang + ": БЕЗ фона признака нет (подпись без тире и без числа)",
                    foundB && !capB.Contains(" — ") && !HasDigit(capB));
                Say(lang + ": БЕЗ фона подсказка — сама подпись, как было",
                    foundB && string.Equals(tipB, capB, StringComparison.Ordinal));

                // Подпись без признака обязана оставаться НАЧАЛОМ подписи с
                // признаком: признак ДОПИСЫВАЕТСЯ, а не подменяет строку.
                Say(lang + ": признак ДОПИСАН к прежней подписи, а не подменил её",
                    foundA && foundB && capA.StartsWith(capB, StringComparison.Ordinal));
            }

            Language("en-US");
        }

        static bool HasDigit(string text)
        {
            foreach (char c in text)
            {
                if (c >= '0' && c <= '9') return true;
            }

            return false;
        }

        /// <summary>
        /// Подпись, значение и подсказка строки невязки НАСТОЯЩЕЙ таблицы
        /// настоящего окна отчёта.
        /// </summary>
        static bool ResidualRow(ResultData rd, FsaResult result,
                                out string caption, out string value, out string tip)
        {
            caption = value = tip = null;
            var session = new FsaAnalysisSession();
            Plant(session, result, "p10");
            using (var report = new FSAReportView(null))
            using (Form host = Host(report, 460, 900))
            {
                report.SetProbeSource(session, rd);
                Application.DoEvents();

                foreach (Row row in report.ReportTable.TableModel.Rows)
                {
                    var model = row.Tag as FsaReportRow;
                    if (model == null || model.Kind != FsaReportRowKind.Residual)
                    {
                        continue;
                    }

                    caption = row.Cells[1].Text;
                    value = row.Cells[2].Text;
                    tip = row.Cells[1].ToolTipText;
                    return true;
                }
            }

            return false;
        }

        // ==================================================================
        // 3. Числа не тронуты
        // ==================================================================

        static void Numbers(FsaResult a, FsaResult b)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. ЧИСЛА НЕВЯЗКИ — как есть (решение Amber: «Число оставить») ===");
            Console.WriteLine("  с фоном:  не добавлено {0} %, лишнего {1} %, хи2/ndf {2}",
                              Pct(a.ResidualMissingShare), Pct(a.ResidualExcessShare), Num(a.Chi2Ndf));
            Console.WriteLine("  без фона: не добавлено {0} %, лишнего {1} %, хи2/ndf {2}",
                              Pct(b.ResidualMissingShare), Pct(b.ResidualExcessShare), Num(b.Chi2Ndf));
        }

        // ==================================================================
        // Оснастка
        // ==================================================================

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("нет файла: " + path);
                return null;
            }

            ResultDataFile file;
            var serializer = new XmlSerializer(typeof(ResultDataFile));
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

            ProbeDeviceConfig.Attach(rd);
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

        static FsaResult Analyze(ResultData rd, NuclideDefinitionManager nuclides, string what)
        {
            List<Peak> peaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            List<FsaComponent> library = FsaLibrary.BuildFromPeaks(peaks, nuclides.NuclideDefinitions);
            Console.WriteLine("SETUP\t{0}: пиков {1}, образов {2}", what, peaks.Count, library.Count);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста — разбирать нечего");
                return null;
            }

            var analyzer = new FsaAnalyzer();

            // ⛔ ГЕЙТ ГЕОМЕТРИИ СНЯТ, И ЭТО СКАЗАНО ВСЛУХ. `A277` (решение
            // Amber 10.09.2026) отказывает в разборе без геометрии; у спектра
            // корпуса, взятого сам по себе, кривой с геометрией нет, и разбор
            // не состоялся бы вовсе. Полосе нужна ТАБЛИЦА ОКНА, а не числа
            // разбора: подпись строки невязки от матрицы отклика не зависит ни
            // одним знаком. Числа отсюда в журнал разбора не идут — об этом
            // сказано первой же строкой вывода.
            analyzer.RequireGeometry = false;

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

            return analyzer.Analyze(rd.EnergySpectrum, rd.BackgroundEnergySpectrum, rd.FwhmCalibration,
                                    library, FsaEfficiency.FromConfig(rd.Efficiency));
        }

        static void Plant(FsaAnalysisSession session, FsaResult result, string stamp)
        {
            Field(session.GetType(), "result").SetValue(session, result);
            Field(session.GetType(), "stamp").SetValue(session, stamp);
            Field(session.GetType(), "running").SetValue(session, false);
            Field(session.GetType(), "status").SetValue(session, null);
        }

        static FieldInfo Field(Type type, string name)
        {
            FieldInfo f = type.GetField(name, Any);
            if (f == null)
            {
                throw new InvalidOperationException("не нашлось поле " + type.Name + "." + name);
            }

            return f;
        }

        static Form Host(FSAReportView report, int width, int height)
        {
            var host = new Form
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                ShowInTaskbar = false,
                ClientSize = new Size(width, height)
            };
            report.TopLevel = false;
            report.Dock = DockStyle.Fill;
            host.Controls.Add(report);
            report.Show();
            host.Show();
            return host;
        }

        static void Language(string name)
        {
            var culture = new CultureInfo(name);
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            BecquerelMonitor.Properties.Resources.Culture = culture;
        }

        static void Say(string what, bool ok)
        {
            if (!ok) bad++;
            Console.WriteLine("    {0,-70} {1}", what, ok ? "ok" : "РАЗОШЛОСЬ");
        }

        /// <summary>Точка, без группировки разрядов — правило Amber 05.09.2026.</summary>
        static string Pct(double share)
        {
            return (100.0 * share).ToString("F2", CultureInfo.InvariantCulture);
        }

        static string Num(double value)
        {
            return value.ToString("F4", CultureInfo.InvariantCulture);
        }
    }
}
