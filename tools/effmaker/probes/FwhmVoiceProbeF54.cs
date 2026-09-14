using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace FwhmVoiceProbeF54
{
    /// <summary>
    /// ОТКРЫТИЕ СОХРАНЁННОГО ДОКУМЕНТА БЕЗ КРИВОЙ РАЗРЕШЕНИЯ (`A240`, полоса
    /// F54, 06.09.2026).
    ///
    /// ⛔ ЧТО МЕРИТСЯ. `A234` научила говорить ОБЕ двери ввоза, а открытие
    /// сохранённого документа осталось немым: и `OpenDocument`, и
    /// `CreateDocument` идут через `CheckDocument`, который зовёт
    /// `FwhmCalibration.DefaultCalibration` СТАРОЙ подписью — причину отказа
    /// выбрасывает и возвращает <c>true</c>. Проба меряет обе половины:
    ///
    ///   --mode=open   открывает все *.xml названного каталога настоящей
    ///                 дверью `DocumentManager.OpenDocument` и считает голоса.
    ///                 ⛔ Это ОТРИЦАТЕЛЬНЫЙ контроль: на здоровых документах
    ///                 голосов обязан быть НОЛЬ, иначе правка — шум.
    ///   --mode=reach  сколько поставочных конфигураций приборов дают отказ
    ///                 `DefaultCalibration`, то есть насколько положение
    ///                 достижимо БЕЗ правки конфигурации руками. Тут же
    ///                 ⛔ ПОЛОЖИТЕЛЬНЫЙ контроль: заведомо убывающая настройка
    ///                 (FWHM_AT_0 &gt; Width_Fwhm) обязана отказать и назвать
    ///                 три числа, а голос двери — прозвучать.
    ///
    /// Код возврата 1, когда ожидание не сошлось (`--expect-open=`,
    /// по умолчанию 0 голосов на каталоге документов).
    /// </summary>
    static class Program
    {
        static string mode = "open";
        static string dir = null;
        static int expectOpen = 0;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            foreach (string a in args)
            {
                if (a.StartsWith("--mode=")) mode = a.Substring(7);
                else if (a.StartsWith("--dir=")) dir = a.Substring(6);
                else if (a.StartsWith("--expect-open="))
                {
                    expectOpen = int.Parse(a.Substring(14), CultureInfo.InvariantCulture);
                }
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + typeof(DocumentManager).Assembly.Location);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location)
                                                 .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine();

            if (mode == "open") return Open();
            if (mode == "reach") return Reach();
            Console.Error.WriteLine("неизвестный --mode: " + mode);
            return 2;
        }

        static int Open()
        {
            if (dir == null) { Console.Error.WriteLine("нужен --dir=<каталог с *.xml>"); return 2; }
            string[] files = Directory.GetFiles(dir, "*.xml");
            Array.Sort(files, StringComparer.Ordinal);

            Console.WriteLine("=== ОТКРЫТИЕ ДОКУМЕНТОВ ДВЕРЬЮ DocumentManager.OpenDocument ===");
            Console.WriteLine("  файлов: " + files.Length.ToString(CultureInfo.InvariantCulture));

            // ⛔ МЕНЕДЖЕРЫ-ОДИНОЧКИ ПРОГРЕВАЮТСЯ ЗАРАНЕЕ, И ЭТО НЕ ОПРЯТНОСТЬ.
            //    Они пишут в поток ошибок при ПЕРВОМ обращении («device configs:
            //    10 loaded», «nuclide library: 152 entries», «main config: …»),
            //    а первое обращение случается внутри первого же OpenDocument —
            //    то есть их болтовня попадала в перехват ПЕРВОГО файла и
            //    считалась его голосом. Измерено 06.09.2026: сторож отказал на
            //    AS1Pro_Ra226.xml, у которого при этом НОЛЬ спектров без кривой.
            //    Признак, который срабатывает от постороннего, ничего не мерит.
            {
                TextWriter warmErr = Console.Error;
                Console.SetError(new StringWriter());
                try
                {
                    DeviceConfigManager.GetInstance();
                    NuclideDefinitionManager.GetInstance();
                    GlobalConfigManager.GetInstance();
                    ROIConfigManager.GetInstance();
                }
                finally { Console.SetError(warmErr); }
            }

            int opened = 0, failed = 0, spoke = 0, without = 0;
            List<string> voices = new List<string>();

            foreach (string f in files)
            {
                TextWriter realErr = Console.Error;
                StringWriter caught = new StringWriter();
                Console.SetError(caught);
                DocEnergySpectrum doc = null;
                string refusal = null;
                try
                {
                    doc = DocumentManager.GetInstance().OpenDocument(f);
                }
                catch (Exception ex)
                {
                    refusal = ex.GetType().Name + ": " + Flat(ex.Message);
                }
                finally
                {
                    Console.SetError(realErr);
                }
                string said = Flat(caught.ToString()).Trim();

                if (refusal != null || doc == null) failed++;
                else opened++;

                // Отдельно от голоса — САМО состояние: сколько спектров осталось
                // без кривой разрешения. Голос без состояния был бы шумом,
                // состояние без голоса — прежней немотой.
                if (doc != null && doc.ResultDataFile != null && doc.ResultDataFile.ResultDataList != null)
                {
                    foreach (ResultData rd in doc.ResultDataFile.ResultDataList)
                    {
                        if (rd != null && rd.EnergySpectrum != null && rd.FwhmCalibration == null) without++;
                    }
                }

                if (said.Length > 0)
                {
                    spoke++;
                    voices.Add(Path.GetFileName(f) + " | " + said);
                }
            }

            Console.WriteLine("  ОТКРЫТО: " + opened.ToString(CultureInfo.InvariantCulture)
                              + "   ОТКАЗАНО: " + failed.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  спектров БЕЗ кривой разрешения после открытия: "
                              + without.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("  файлов, на которых дверь СКАЗАЛА: "
                              + spoke.ToString(CultureInfo.InvariantCulture)
                              + " (ожидалось " + expectOpen.ToString(CultureInfo.InvariantCulture) + ")");
            foreach (string v in voices) Console.WriteLine("    " + v);

            if (spoke != expectOpen)
            {
                Console.Error.WriteLine("ОТКАЗ СТОРОЖА: голосов " + spoke.ToString(CultureInfo.InvariantCulture)
                                        + ", ожидалось " + expectOpen.ToString(CultureInfo.InvariantCulture));
                return 1;
            }
            Console.WriteLine("СТОРОЖ ПРОШЁЛ.");
            return 0;
        }

        /// <summary>
        /// ДОСТИЖИМОСТЬ ОТКАЗА И ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ГОЛОСА.
        ///
        /// Первая половина отвечает на вопрос, ради которого `A240` и стояла
        /// открытой: во скольких ПОСТАВОЧНЫХ конфигурациях приборов умолчание
        /// модели разрешения не строится. Вторая — заведомо негодная настройка,
        /// на которой и отказ, и голос ОБЯЗАНЫ прозвучать; без неё нуль первой
        /// половины ничего не значил бы, потому что его дала бы и сломанная
        /// проверка.
        /// </summary>
        static int Reach()
        {
            DeviceConfigManager dcm = DeviceConfigManager.GetInstance();
            Console.WriteLine("=== ДОСТИЖИМОСТЬ ОТКАЗА DefaultCalibration ===");
            Console.WriteLine("  конфигураций приборов: "
                              + dcm.DeviceConfigList.Count.ToString(CultureInfo.InvariantCulture));

            int refused = 0, built = 0, noFwhmSection = 0;
            foreach (DeviceConfigInfo cfg in dcm.DeviceConfigList)
            {
                FWHMPeakDetectionMethodConfig fw =
                    cfg.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
                if (fw == null) { noFwhmSection++; continue; }
                string refusal;
                SimpleSqrtFwhmCalibration c = FwhmCalibration.DefaultCalibration(
                    fw, new PolynomialEnergyCalibration(), out refusal);
                if (c == null)
                {
                    refused++;
                    Console.WriteLine("    ОТКАЗ  " + cfg.Name + " — " + Flat(refusal));
                }
                else built++;
            }
            Console.WriteLine("  строится: " + built.ToString(CultureInfo.InvariantCulture)
                              + "   ОТКАЗ: " + refused.ToString(CultureInfo.InvariantCulture)
                              + "   без раздела ПШПВ: " + noFwhmSection.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine();

            // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Настройка, у которой ширина при нуле
            //    БОЛЬШЕ ширины на опорном канале: прямая убывает, и
            //    PerformCalibration её не принимает.
            Console.WriteLine("=== ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: заведомо убывающая настройка ===");
            FWHMPeakDetectionMethodConfig bad = new FWHMPeakDetectionMethodConfig();
            bad.FWHM_AT_0 = 40.5;
            bad.Ch_Fwhm = 1000;
            bad.Width_Fwhm = 1.25;
            string badRefusal;
            SimpleSqrtFwhmCalibration badCurve = FwhmCalibration.DefaultCalibration(
                bad, new PolynomialEnergyCalibration(), out badRefusal);
            Console.WriteLine("  кривая: " + (badCurve == null ? "НЕ ПОСТРОЕНА" : "построена (контроль СЛЕП)"));
            Console.WriteLine("  причина: " + Flat(badRefusal));

            // И ГОЛОС ДВЕРИ на этом же состоянии: документ есть, кривой нет.
            Console.WriteLine();
            Console.WriteLine("=== ГОЛОС ДВЕРИ НА ЭТОМ ЖЕ СОСТОЯНИИ ===");
            DocEnergySpectrum doc = new DocEnergySpectrum();
            foreach (ResultData rd in doc.ResultDataFile.ResultDataList)
            {
                rd.PeakDetectionMethodConfig = bad;
                rd.FwhmCalibration = null;
            }
            MethodInfo report = typeof(DocumentManager).GetMethod(
                "ReportMissingFwhmCalibration",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (report == null)
            {
                Console.Error.WriteLine("ОТКАЗ: метод ReportMissingFwhmCalibration не найден отражением");
                return 1;
            }
            TextWriter realErr = Console.Error;
            StringWriter caught = new StringWriter();
            Console.SetError(caught);
            try { report.Invoke(DocumentManager.GetInstance(), new object[] { doc, "проба-A240.xml" }); }
            finally { Console.SetError(realErr); }
            string spoken = Flat(caught.ToString()).Trim();
            Console.WriteLine("  сказано: " + (spoken.Length == 0 ? "(МОЛЧА — контроль СЛЕП)" : spoken));

            bool ok = badCurve == null && !string.IsNullOrEmpty(badRefusal) && spoken.Length > 0;
            Console.WriteLine();
            Console.WriteLine(ok ? "ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ СОШЁЛСЯ."
                                 : "ОТКАЗ: положительный контроль НЕ сработал.");
            return ok ? 0 : 1;
        }

        static string Flat(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ")
                    .Replace("   ", " ").Replace("  ", " ").Trim();
        }
    }
}
