using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace FsaGateRescueProbe
{
    /// <summary>
    /// СТОРОЖ ЗАЩИТЫ СОСТАВА ОТ ГЕЙТА ΔD&lt;0 (`AMBER3`).
    ///
    ///     fsagaterescueprobe --spectrum=&lt;файл спектра&gt; [--matrix=&lt;файл .rmx&gt;]
    ///
    /// Гейт по парциальной невязке (P6 «б») снимает нуклидную колонку, чьё
    /// присутствие ухудшает невязку её же пиковых окон. На спектре, где
    /// смещена ВСЯ модель (чужая матрица, не то вещество пробы), это верно для
    /// каждого объявленного нуклида разом — и до `AMBER3` гейт выносил состав
    /// ЦЕЛИКОМ, оставляя человеку вердикт «состав пересилен приборным
    /// образом» у спектра, где ряд виден глазом.
    ///
    /// ⛔ ПРАВИЛ ЗАЩИТЫ ТЕПЕРЬ ДВА, И ЭТО ВАЖНО ДЛЯ ПРИЁМКИ (`T256`). Проба
    /// написана 08.09.2026 под ПЕРВОЕ и требовала
    /// <see cref="FsaAnalyzer.GateNuclidesRescued"/> &gt; 0; 10.09.2026 решением
    /// Amber «Не вправе: ввести потолок по z» заведено ВТОРОЕ —
    /// <see cref="FsaAnalyzer.GateZCeiling"/>, — и оно вступает РАНЬШЕ: колонка
    /// со значимостью выше потолка не судится вовсе
    /// (<see cref="FsaAnalyzer.GateNuclidesSpared"/>), а пощажённая колонка
    /// ОТМЕНЯЕТ самоотключение. На случае Amber (`Ac-228`, z = 43.11) теперь
    /// сработает потолок, и `Rescued` останется нулём — то есть прежнее
    /// ожидание стало ЛОЖНЫМ отказом. Судится потому ИСХОД, а не механизм:
    /// состав обязан уцелеть, и проба называет, ЧЕМ именно он спасён.
    ///
    /// Два плеча, оба на ОДНОМ спектре — иначе «правка сработала» неотличимо
    /// от «правка не понадобилась»:
    ///
    ///   1. С МАТРИЦЕЙ — гейт видит нуклидные колонки, состав обязан уцелеть:
    ///      сработало хотя бы одно из двух правил (`Spared` &gt; 0 либо
    ///      `Rescued` &gt; 0) и нуклидная доля больше половины стека.
    ///   2. БЕЗ МАТРИЦЫ (тот же спектр, та же кривая) — самоотключение обязано
    ///      МОЛЧАТЬ: возвращённых 0. Это положительный контроль на то, что
    ///      правка не выключила гейт вовсе.
    ///
    /// ⛔ ГДЕ БРАТЬ МАТРИЦУ. Склад матриц — `config\device\response` РЯДОМ С
    /// EXE (<c>ResponseMatrixStore</c>), и в изолированном каталоге проб он
    /// пуст: до 10.09.2026 оба плеча падали здесь ещё до всякой физики, с
    /// «матрица не взята, отказ NoFile», и красный цвет пробы не значил ничего.
    /// Ключ `--matrix=` берёт файл НАПРЯМУЮ — например из корпуса
    /// (`tools/CORPUS/corpus/geometries/<геометрия>.rmx`). Клеймо всё равно
    /// сверяется с геометрией спектра: подсунуть чужую матрицу ключом нельзя.
    ///
    /// Коды возврата: 0 — «ВСЕ СОШЛИСЬ»; 1 — правило нарушено; 2 — мерить
    /// нечем (нет спектра, нет матрицы, разбор не получился). ⛔ Второй и
    /// первый разведены нарочно: проба, отвечающая «нарушено» на отсутствие
    /// входа, своим красным ничего не сообщает.
    /// </summary>
    static class Program
    {
        static int bad;

        /// <summary>
        /// (`T256`) Файл матрицы, заданный ключом `--matrix=`; null — берётся
        /// склад рядом с exe, как в приложении.
        /// </summary>
        static string matrixFile;

        /// <summary>
        /// (`T256`) Плечо 1 осталось БЕЗ МАТРИЦЫ, то есть ничего не измерило.
        /// Отдельный признак, а не `bad++`: «правило нарушено» и «мерить
        /// нечем» — разные приговоры, и второй лечится входом, а не кодом.
        /// </summary>
        static bool nothingToMeasure;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей: полоса это статика,
            // отражение её не видит, и снятая позже она уже могла быть уведена.
            FsaTuningReport.Snapshot();

            string path = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) path = a.Substring(11);
                else if (a.StartsWith("--matrix=", StringComparison.Ordinal)) matrixFile = a.Substring(9);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (path == null)
            {
                Console.Error.WriteLine("нужен --spectrum=<файл спектра, у которого есть матрица отклика>");
                return 2;
            }

            if (matrixFile != null && !File.Exists(matrixFile))
            {
                Console.Error.WriteLine("нет файла матрицы: " + matrixFile);
                return 2;
            }

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();

            ResultData rd = Load(path, nuclides);
            if (rd == null) return 2;

            FsaCalculationOptions options = FsaCalculationOptions.Of(rd);

            int judged, rescued, spared;
            FsaResult withMatrix = Run(rd, options, true, out judged, out rescued, out spared);
            Console.WriteLine();
            Console.WriteLine("=== 1. С МАТРИЦЕЙ: гейт выносил бы состав целиком ===");
            if (withMatrix == null)
            {
                Console.WriteLine("  ⛔ разбор не получился");
                nothingToMeasure = true;
            }
            else if (!withMatrix.ResponseMatrixUsed)
            {
                // ⛔ (`T256`) НЕ «НАРУШЕНО», А «МЕРИТЬ НЕЧЕМ». Плечо построено
                // на том, что модель смещена приборным образом; без матрицы
                // смещать нечем, и всякий вывод отсюда был бы о другой задаче.
                Console.WriteLine("  ⛔ МАТРИЦА НЕ ВЗЯТА — плечо ничего не мерит."
                                  + " Дайте её ключом --matrix=<файл .rmx>"
                                  + " (корпус: tools/CORPUS/corpus/geometries/<геометрия>.rmx)");
                nothingToMeasure = true;
            }
            else
            {
                Console.WriteLine("  нуклидных колонок: судил {0}, пощадил потолком z≥{1:F1} — {2};"
                                  + " возвращено самоотключением {3}",
                                  judged, CeilingOf(), spared, rescued);
                Console.WriteLine("  нуклидная доля {0:F2} %, χ²/ndf {1:F3}",
                                  withMatrix.NuclideSharePercent, withMatrix.Chi2Ndf);
                Same("матрица взята (иначе плечо ничего не мерит)", true, withMatrix.ResponseMatrixUsed);
                Same("гейт ВИДЕЛ хотя бы одну нуклидную колонку", true, judged + spared > 0);
                // ⛔ (`T256`) СУДИТСЯ ИСХОД, А НЕ МЕХАНИЗМ. До потолка спасало
                // одно правило (самоотключение), теперь их два, и на случае
                // Amber первым вступает потолок — требование «Rescued > 0»
                // стало бы ложным отказом на верно работающей защите.
                Same("состав спасён правилом AMBER3 (потолок ЛИБО самоотключение)",
                     true, spared > 0 || rescued > 0);
                Same("состав не пуст", true, withMatrix.NuclideSharePercent > 50.0);
                Same("вердикта «пересилен приборным образом» нет", false, withMatrix.CompositionSuppressed);
                Console.WriteLine("  спас: {0}",
                                  spared > 0 ? "ПОТОЛОК ЗНАЧИМОСТИ (колонок " + spared + ")"
                                  : rescued > 0 ? "САМООТКЛЮЧЕНИЕ (колонок " + rescued + ")"
                                  : "никто — гейту нечего было спасать на этом спектре");
            }

            FsaResult noMatrix = Run(rd, options, false, out judged, out rescued, out spared);
            Console.WriteLine();
            Console.WriteLine("=== 2. БЕЗ МАТРИЦЫ (положительный контроль: правка молчит) ===");
            if (noMatrix == null)
            {
                Console.WriteLine("  ⛔ разбор не получился");
                nothingToMeasure = true;
            }
            else
            {
                Console.WriteLine("  нуклидных колонок: судил {0}, пощадил потолком {1};"
                                  + " возвращено самоотключением {2}", judged, spared, rescued);
                Console.WriteLine("  нуклидная доля {0:F2} %, χ²/ndf {1:F3}",
                                  noMatrix.NuclideSharePercent, noMatrix.Chi2Ndf);
                Same("матрицы нет", false, noMatrix.ResponseMatrixUsed);
                // ⛔ (`T256`) «ВИДЕЛ», а не «судил»: с потолком гейт теряет
                // власть над девятью колонками из десяти (замер полосы П5), и
                // требование `judged > 0` красило бы законную работу потолка
                // в отказ. Проверяется то, ради чего плечо и написано: гейт не
                // выключен ВОВСЕ.
                Same("гейт ВИДЕЛ хотя бы одну нуклидную колонку", true, judged + spared > 0);
                Same("самоотключение НЕ срабатывало", 0, rescued);
                Same("состав не пуст", true, noMatrix.NuclideSharePercent > 50.0);
            }

            Console.WriteLine();
            if (nothingToMeasure)
            {
                Console.WriteLine("НЕЧЕМ МЕРИТЬ: вход не даёт плечу того, на чём оно построено"
                                  + (bad > 0 ? "; сверх того НЕ СОШЛОСЬ: " + bad : ""));
                return 2;
            }

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Тот же путь, каким считает сеанс разбора (`FsaAnalysisSession`), но
        /// с рычагом «матрица отклика» и с чтением счётчиков анализатора.
        /// </summary>
        static FsaResult Run(ResultData rd, FsaCalculationOptions options, bool useMatrix,
                             out int judged, out int rescued, out int spared)
        {
            judged = 0;
            rescued = 0;
            spared = 0;

            EnergySpectrum spectrum = rd.EnergySpectrum.Clone();
            FwhmCalibration fwhm = rd.FwhmCalibration != null ? rd.FwhmCalibration.Clone() : null;
            EfficiencyConfigData efficiencyConfig = rd.Efficiency != null ? rd.Efficiency.Copy() : null;
            FsaEfficiency efficiency = FsaEfficiency.FromConfig(efficiencyConfig);

            var analyzer = new FsaAnalyzer();
            options.ApplyTo(analyzer);
            lastCeiling = analyzer.GateZCeiling;
            FsaTuningReport.Print(analyzer);

            if (useMatrix && efficiencyConfig != null && efficiencyConfig.HasGeometry
                && efficiencyConfig.UseResponseMatrix)
            {
                MatrixRefusal refusal;
                int fileFormat;
                // (`T256`) Ключ `--matrix=` — тот же читатель файла, каким
                // пользуется склад: разница ровно в том, ОТКУДА взят путь.
                // Клеймо ниже сверяется в обоих случаях одинаково.
                ResponseMatrix matrix = matrixFile != null
                    ? ResponseMatrix.Load(matrixFile, out refusal, out fileFormat)
                    : ResponseMatrixStore.Load(efficiencyConfig.Guid, out refusal, out fileFormat);
                if (matrix != null && matrix.IsValidFor(efficiencyConfig.Geometry))
                {
                    analyzer.ResponseMatrix = matrix;
                    analyzer.ScintillatorMaterial =
                        EfficiencySimulator.ScintillatorNameOf(efficiencyConfig.Geometry);
                    Console.WriteLine("  матрица взята: {0}",
                                      matrixFile != null ? matrixFile
                                      : ResponseMatrixStore.PathOf(efficiencyConfig.Guid));
                }
                else
                {
                    // ⛔ Отказ называет ОБА своих разряда: файла нет — это одно,
                    // а «файл прочитан, клеймо не сошлось с геометрией» —
                    // совсем другое, и лечится оно пересчётом, а не путём.
                    Console.WriteLine("  ⚠ матрица не взята: файл {0}, отказ {1}{2}",
                                      matrix != null ? "прочитан" : "не прочитан", refusal,
                                      matrix != null ? "; клеймо НЕ сошлось с геометрией спектра" : "");
                }
            }
            else if (useMatrix)
            {
                // (`T256`) Молчание тут читалось как «матрицы не нашлось», а на
                // деле её и не искали: у кривой нет геометрии либо матрица
                // выключена в самой кривой.
                Console.WriteLine("  ⚠ матрицу не искали: {0}",
                                  efficiencyConfig == null ? "кривой эффективности нет вовсе"
                                  : !efficiencyConfig.HasGeometry ? "у кривой нет геометрии"
                                  : "матрица выключена в кривой (UseResponseMatrix)");
            }

            List<FsaComponent> library;
            if (options.DbLookups)
            {
                FsaCompositionInference.Report inferred;
                FsaSampleSpec spec = FsaCompositionInference.Infer(
                    new List<Peak>(rd.DetectedPeaks), rd, out inferred);
                options.ApplyTo(spec);
                library = FsaSampleLibrary.Build(spec);
            }
            else
            {
                library = FsaLibrary.BuildFromPeaks(new List<Peak>(rd.DetectedPeaks),
                    NuclideDefinitionManager.GetInstance().NuclideDefinitions, null, options.AtomicXray);
            }

            FsaResult result = analyzer.Analyze(spectrum, null, fwhm, library, efficiency);
            if (result == null && analyzer.GeometryRefused)
            {
                // (`T256`, `A277`) Отказ по геометрии называет себя и здесь:
                // одно слово «разбор не получился» стояло на шести причинах.
                Console.WriteLine("  ⛔ гейт геометрии (A277): у кривой нет геометрии,"
                                  + " разбора не было");
            }

            judged = analyzer.GateNuclidesJudged;
            rescued = analyzer.GateNuclidesRescued;
            spared = analyzer.GateNuclidesSpared;
            return result;
        }

        /// <summary>
        /// (`T256`) Потолок значимости ТОГО САМОГО анализатора, который считал:
        /// второй копии числа здесь нет (`T82`), и печатать «z≥10» константой
        /// значило бы завести вторую истину, расходящуюся с первой молча.
        /// </summary>
        static double CeilingOf()
        {
            return lastCeiling;
        }

        static double lastCeiling;

        static void Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0} {1,-62} {2}{3}", ok ? "ok  " : "⛔ ", what, got,
                              ok ? string.Empty : "  вместо " + expected);
            if (!ok) bad++;
        }

        static ResultData Load(string path, NuclideDefinitionManager nuclides)
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine("нет файла: " + path);
                return null;
            }

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

            Console.WriteLine("SETUP\t{0}: прибор {1}", Path.GetFileName(path), ProbeDeviceConfig.Attach(rd));

            if (rd.FwhmCalibration == null
                && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
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

            rd.DetectedPeaks = new PeakDetector().DetectPeak(
                rd, BackgroundMode.Invisible, SmoothingMethod.None,
                nuclides.ActiveSet, nuclides.NuclideDefinitions);
            Console.WriteLine("SETUP\t{0}: пиков {1}, каналов {2}, кривая {3}",
                              Path.GetFileName(path), rd.DetectedPeaks.Count,
                              rd.EnergySpectrum.NumberOfChannels,
                              rd.Efficiency != null ? rd.Efficiency.Name : "(нет)");
            return rd;
        }
    }
}
