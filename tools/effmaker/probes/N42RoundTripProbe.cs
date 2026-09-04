using BecquerelMonitor;
using BecquerelMonitor.N42;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace N42RoundTripProbe
{
    /// <summary>
    /// ФАЙЛЫ N42 В ДЕРЕВЕ И КРУГ «ВЫВОЗ → ВВОЗ» (`A143`, решение Amber 04.09.2026).
    ///
    /// ⛔ ЧЕСТНАЯ ОГОВОРКА, КОТОРУЮ НЕЛЬЗЯ ОПУСКАТЬ. Файл, выгруженный НАШИМ
    /// приложением, доказывает круг «мы → мы» и по-прежнему НИЧЕГО не говорит
    /// о файлах от чужих приборов: и запись, и чтение здесь наши, и общая
    /// ошибка понимания спецификации в таком круге НЕ ВИДНА вовсе. Настоящей
    /// проверкой совместимости был бы файл, записанный ЧУЖИМ прибором, — его
    /// в дереве по-прежнему нет.
    ///
    /// Что проба делает:
    ///   --mode=export  — берёт спектры корпуса, выгружает их в N42 ТЕМ ЖЕ
    ///                    путём, каким выгружает приложение (Util.ExportToN42
    ///                    + XmlSerializer с настройками DocumentManager), и
    ///                    сразу ввозит обратно настоящей дверью
    ///                    DocumentManager.ImportDocumentN42, сверяя числа;
    ///   --mode=import  — ввозит все *.n42 каталога и печатает СЛЕПОК каждого
    ///                    (отсчёты, шкала, времена) — этим слепком два плеча
    ///                    сборок сверяются между собой;
    ///   --mode=cases   — кладёт СОЧИНЁННЫЕ входы разбора калибровки (границы
    ///                    энергий, нечисло, порядок 5, пустые коэффициенты):
    ///                    их назначение — развести причины отказа (`A136`),
    ///                    и они нарочно лежат ОТДЕЛЬНО от настоящих.
    ///
    /// Проба безоконная (входная сборка не BecquerelMonitor.exe), то есть
    /// AppUi.HasWindows == false и мерится ОТКАЗНАЯ половина всех дверей.
    /// </summary>
    static class Program
    {
        static string mode = "export";
        static string corpus = null;
        static string outDir = null;
        static string only = null;
        static string culture = null;
        static string manifest = null;
        static string round2 = null;
        static bool dot = false;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            foreach (string a in args)
            {
                if (a.StartsWith("--mode=")) mode = a.Substring(7);
                else if (a.StartsWith("--corpus=")) corpus = a.Substring(9);
                else if (a.StartsWith("--out=")) outDir = a.Substring(6);
                else if (a.StartsWith("--only=")) only = a.Substring(7);
                else if (a.StartsWith("--culture=")) culture = a.Substring(10);
                else if (a.StartsWith("--manifest=")) manifest = a.Substring(11);
                // `A156`: ВТОРОЙ КРУГ. Ключ отдельный и каталог отдельный нарочно:
                //   файлы второго оборота не смеют попасть в tools\CORPUS\n42,
                //   иначе перевыгрузка дерева удвоит его содержимое. Без ключа
                //   второй круг не гоняется вовсе — как было до 05.09.2026.
                else if (a.StartsWith("--round2=")) round2 = a.Substring(9);
                else if (a == "--dot") dot = true;
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            // ⛔ КУЛЬТУРА СТАВИТСЯ ЧЕСТНО, БЕЗ ПОДПОРКИ (`A142`). Прочие пробы
            //    дерева подменяют разделитель дробной части на точку, повторяя
            //    то, что делает MainForm.cs:162-164 при запуске окон. Здесь
            //    этого НЕ делается нарочно: измеряется как раз то, что будет
            //    у всякого безоконного вызывающего, — и подпорка спрятала бы
            //    ровно измеряемый дефект.
            if (!string.IsNullOrEmpty(culture))
            {
                CultureInfo ci = new CultureInfo(culture);
                Thread.CurrentThread.CurrentCulture = ci;
                Thread.CurrentThread.CurrentUICulture = ci;
            }

            // ⚠ `--dot` повторяет ДОСЛОВНО то, что делает окно приложения:
            //    MainForm.cs:162-164 клонирует текущую культуру и ставит ей
            //    разделителем дробной части ТОЧКУ. Именно в этом состоянии
            //    живёт вывоз и ввоз N42 у человека за экраном; без окон этой
            //    строки нет никто не выполняет. Ключ заведён затем, чтобы
            //    обе половины были измеримы ОТДЕЛЬНО, а не смешаны.
            if (dot)
            {
                CultureInfo custom = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
                custom.NumberFormat.NumberDecimalSeparator = ".";
                Thread.CurrentThread.CurrentCulture = custom;
            }

            // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + typeof(DocumentManager).Assembly.Location);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location)
                                                 .ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine("  культура прогона: " + Thread.CurrentThread.CurrentCulture.Name
                              + " (разделитель дробной части «"
                              + Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator + "»)"
                              + (dot ? "  [--dot: повторена строка MainForm.cs:162-164]" : "  [без подпорки MainForm]"));
            Console.WriteLine();

            if (mode == "export") return Export();
            if (mode == "import") return Import();
            if (mode == "cases") return Cases();
            if (mode == "il") return Il();
            Console.Error.WriteLine("неизвестный --mode: " + mode);
            return 2;
        }

        // ==================================================================
        // ВЫВОЗ КОРПУСНЫХ СПЕКТРОВ И КРУГ
        // ==================================================================

        static int Export()
        {
            if (corpus == null || outDir == null)
            {
                Console.Error.WriteLine("нужны --corpus=<каталог спектров> и --out=<куда класть n42>");
                return 2;
            }
            Directory.CreateDirectory(outDir);

            List<string> names = new List<string>();
            if (only != null)
            {
                foreach (string n in only.Split(','))
                {
                    if (n.Trim().Length > 0) names.Add(n.Trim());
                }
            }
            else
            {
                foreach (string p in Directory.GetFiles(corpus, "*.xml"))
                {
                    names.Add(Path.GetFileNameWithoutExtension(p));
                }
                names.Sort(StringComparer.Ordinal);
            }

            Console.WriteLine("=== ВЫВОЗ КОРПУСНЫХ СПЕКТРОВ В N42 ===");
            Console.WriteLine("  корпус: " + Path.GetFullPath(corpus));
            Console.WriteLine("  вывоз в: " + Path.GetFullPath(outDir));
            Console.WriteLine("  спектров под вывоз: " + names.Count);
            Console.WriteLine("  ImportSpectrumWithEmptyConfig: "
                              + GlobalConfigManager.GetInstance().GlobalConfig.ImportSpectrumWithEmptyConfig);
            Console.WriteLine();

            List<string> rows = new List<string>();
            rows.Add("spectrum,n42,bytes,channels_src,channels_back,channels_equal,"
                     + "counts_diff_channels,counts_max_abs_diff,sum_src,sum_back,"
                     + "order_src,order_back,coeff_max_rel_diff,max_dE_keV,"
                     + "meastime_src,meastime_back,livetime_src,livetime_back,counts_ok,time_ok,"
                     + "time_max_abs_s,bg_src,bg_back,start_ok,"
                     // `A155`: живое время ФОНА — своя величина, а не время измерения.
                     + "bg_live_src,bg_live_back,bg_live_ok,bg_live_diff_s,"
                     // `A156`: второй оборот круга. -1 значит «не гонялся» (нет --round2).
                     + "start2_ok,start2_shift_s");

            int bad = 0;
            foreach (string name in names)
            {
                string src = Path.Combine(corpus, name + ".xml");
                if (!File.Exists(src))
                {
                    Console.WriteLine("⛔ " + name + ": нет файла " + src);
                    bad++;
                    continue;
                }
                string dst = Path.Combine(outDir, name + ".n42");
                string row = OneSpectrum(name, src, dst);
                if (row == null) { bad++; continue; }
                rows.Add(row);
            }

            if (manifest != null)
            {
                // Происхождение файлов — МАШИНОЧИТАЕМОЕ и рядом с ними: какой
                // спектр корпуса стал каким n42 и сошёлся ли круг числом.
                using (StreamWriter w = new StreamWriter(manifest, false, new UTF8Encoding(false)))
                {
                    w.NewLine = "\r\n";
                    foreach (string r in rows) w.WriteLine(r);
                }
                Console.WriteLine("список происхождения: " + Path.GetFullPath(manifest));
            }

            // ⛔ Столбцы ищутся ПО ИМЕНИ, а не по смещению с конца. Прежде здесь
            //    стояло c[c.Length - 2] и c[c.Length - 1], и первый же новый
            //    столбец (05.09.2026 их прибавилось три) молча сместил бы обе
            //    величины ИТОГА, не изменив ни строки вывода: та же грабля, что
            //    «старый разбор понимает новое значение ключа по-старому».
            List<string> head = new List<string>(rows[0].Split(','));
            int iCounts = head.IndexOf("counts_ok");
            int iTime = head.IndexOf("time_ok");
            int iCoeff = head.IndexOf("coeff_max_rel_diff");
            int iDt = head.IndexOf("time_max_abs_s");
            int iBgSrc = head.IndexOf("bg_src");
            int iBgBack = head.IndexOf("bg_back");
            int iStart = head.IndexOf("start_ok");
            int iBgLive = head.IndexOf("bg_live_ok");
            int iBgLiveDiff = head.IndexOf("bg_live_diff_s");
            int iStart2 = head.IndexOf("start2_ok");
            if (iCounts < 0 || iTime < 0 || iCoeff < 0 || iDt < 0 || iBgSrc < 0 || iBgBack < 0
                || iStart < 0 || iBgLive < 0 || iBgLiveDiff < 0 || iStart2 < 0)
            {
                Console.Error.WriteLine("⛔ шапка списка происхождения разошлась с ИТОГОМ");
                return 2;
            }

            int countsBad = 0, timeBad = 0, coeffBad = 0, bgBad = 0, startBad = 0;
            int bgLiveBad = 0, start2Bad = 0, start2Run = 0;
            double worstTime = 0.0, worstBgLive = 0.0;
            for (int i = 1; i < rows.Count; i++)
            {
                string[] c = rows[i].Split(',');
                if (c[iCounts] != "1") countsBad++;
                if (c[iTime] != "1") timeBad++;
                if (double.Parse(c[iCoeff], CultureInfo.InvariantCulture) != 0.0) coeffBad++;
                double dt = double.Parse(c[iDt], NumberStyles.Float, CultureInfo.InvariantCulture);
                if (dt > worstTime) worstTime = dt;
                if (c[iBgSrc] != c[iBgBack]) bgBad++;
                if (c[iStart] != "1") startBad++;
                // `A155`: у файлов БЕЗ фона столбец «-», и в счёт он не идёт.
                if (c[iBgLive] == "0") bgLiveBad++;
                if (c[iBgLiveDiff] != "-")
                {
                    double dbg = double.Parse(c[iBgLiveDiff], NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (dbg > worstBgLive) worstBgLive = dbg;
                }
                // `A156`: второй оборот считается только там, где он гонялся.
                if (c[iStart2] != "-")
                {
                    start2Run++;
                    if (c[iStart2] != "1") start2Bad++;
                }
            }

            Console.WriteLine();
            Console.WriteLine("ИТОГ: выгружено " + (rows.Count - 1)
                              + ", отказов вывоза/ввоза " + bad
                              + ", ОТСЧЁТЫ разошлись у " + countsBad
                              + ", ВРЕМЕНА разошлись у " + timeBad
                              + " (наибольший остаток " + worstTime.ToString("E3", CultureInfo.InvariantCulture) + " с)"
                              + ", ШКАЛА разошлась у " + coeffBad
                              + ", ПРИЗНАК ФОНА потерян у " + bgBad
                              + ", ЖИВОЕ ВРЕМЯ ФОНА потеряно у " + bgLiveBad
                              + " (наибольший остаток " + worstBgLive.ToString("E3", CultureInfo.InvariantCulture) + " с)"
                              + ", НАЧАЛО НАБОРА потеряно у " + startBad
                              + ", ВТОРОЙ КРУГ гнался у " + start2Run
                              + " и потерял начало у " + start2Bad);
            return bad == 0 && countsBad == 0 ? 0 : 1;
        }

        static string OneSpectrum(string name, string src, string dst)
        {
            Console.WriteLine("--- " + name + " ---");

            DocEnergySpectrum doc;
            try
            {
                doc = DocumentManager.GetInstance().OpenDocument(src);
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ⛔ корпусный спектр НЕ ОТКРЫЛСЯ: " + ex.GetType().Name + ": " + Flat(ex.Message));
                return null;
            }
            if (doc == null)
            {
                Console.WriteLine("  ⛔ корпусный спектр НЕ ОТКРЫЛСЯ: вернулся null");
                return null;
            }

            ResultData srcRd = doc.ResultDataFile.ResultDataList[0];
            EnergySpectrum srcEs = srcRd.EnergySpectrum;
            PolynomialEnergyCalibration srcCal = srcEs.EnergyCalibration as PolynomialEnergyCalibration;

            // ВЫВОЗ ТЕМ ЖЕ ПУТЁМ, ЧТО У ПРИЛОЖЕНИЯ. DocumentManager.ExportDocumentN42
            // отличается от этих строк ровно диалогом сохранения файла и
            // обёрткой catch: сам файл строит Util.ExportToN42, а записывает
            // XmlSerializer с настройками xmlSettings (UTF8, Indent).
            string exportSaid = ExportOne(doc, dst);
            if (exportSaid != null)
            {
                Console.WriteLine("  ⛔ ВЫВОЗ ОТКАЗАЛ: " + exportSaid);
                return null;
            }

            long bytes = new FileInfo(dst).Length;
            Console.WriteLine("  вывезено: " + Path.GetFileName(dst) + ", " + bytes + " байт");

            // ВВОЗ ОБРАТНО — настоящей дверью приложения.
            DocEnergySpectrum back = new DocEnergySpectrum();
            string said = null;
            try
            {
                DocumentManager.GetInstance().ImportDocumentN42(back, dst);
            }
            catch (Exception ex)
            {
                said = ex.GetType().Name + ": " + Flat(ex.Message);
            }
            if (said != null)
            {
                Console.WriteLine("  ⛔ ВВОЗ ОТКАЗАЛ: " + said);
                return null;
            }

            ResultData backRd = back.ResultDataFile.ResultDataList[0];
            EnergySpectrum backEs = backRd.EnergySpectrum;
            PolynomialEnergyCalibration backCal = backEs.EnergyCalibration as PolynomialEnergyCalibration;

            int chSrc = srcEs.NumberOfChannels;
            int chBack = backEs.NumberOfChannels;
            long sumSrc = srcEs.Spectrum.Sum(x => (long)x);
            long sumBack = backEs.Spectrum.Sum(x => (long)x);

            int diffChannels = 0;
            long maxAbs = 0;
            int n = Math.Min(srcEs.Spectrum.Length, backEs.Spectrum.Length);
            for (int i = 0; i < n; i++)
            {
                long d = (long)srcEs.Spectrum[i] - backEs.Spectrum[i];
                if (d != 0)
                {
                    diffChannels++;
                    if (Math.Abs(d) > maxAbs) maxAbs = Math.Abs(d);
                }
            }
            diffChannels += Math.Abs(srcEs.Spectrum.Length - backEs.Spectrum.Length);

            int ordSrc = srcCal == null ? -1 : srcCal.PolynomialOrder;
            int ordBack = backCal == null ? -1 : backCal.PolynomialOrder;
            double coeffRel = CoeffDiff(srcCal, backCal);

            // ⚠ Отношение коэффициентов НЕ ЕСТЬ мера расхождения ШКАЛЫ, а
            //    считают по шкале. Поэтому здесь же меряется то, чем шкала
            //    пользуется: наибольший по всем каналам сдвиг энергии в кэВ.
            double maxdE = 0.0;
            if (srcCal != null && backCal != null)
            {
                for (int i = 0; i < chSrc; i++)
                {
                    double d = Math.Abs(srcCal.ChannelToEnergy(i) - backCal.ChannelToEnergy(i));
                    if (d > maxdE) maxdE = d;
                }
            }

            bool countsOk = chSrc == chBack && diffChannels == 0 && sumSrc == sumBack;
            bool timeOk = srcEs.MeasurementTime == backEs.MeasurementTime
                          && srcEs.LiveTime == backEs.LiveTime;

            // `A147`: «разошлось» — приговор, а НЕ величина. Прежде здесь было
            //   только равенство, и потеря целой секунды выглядела так же, как
            //   промах на единицу младшего разряда double. Мерить надо остаток.
            double dTime = Math.Max(Math.Abs(srcEs.MeasurementTime - backEs.MeasurementTime),
                                    Math.Abs(srcEs.LiveTime - backEs.LiveTime));

            // `A150`: пережил ли круг ПРИЗНАК ФОНА. До правки фон приезжал вторым
            //   равноправным спектром списка, и мерить это было нечем.
            bool bgSrc = srcRd.BackgroundEnergySpectrum != null;
            bool bgBack = backRd.BackgroundEnergySpectrum != null;

            // `A146`: пережило ли круг ВРЕМЯ НАЧАЛА НАБОРА. ⚠ Сравниваются РАЗНЫЕ
            //   поля нарочно: вывоз берёт ResultData.StartTime, а ввоз кладёт
            //   прочитанное в SampleInfo.Time — это не описка пробы, а
            //   несимметричность самого разбора, и мерить надо то, что есть.
            // ⚠ `A156` ПОПРАВИЛА ЭТУ МЕРКУ. Прежде здесь стояло только
            //   SampleInfo.Time — потому что ввоз клал прочитанное ТОЛЬКО туда, а
            //   вывоз берёт ResultData.StartTime. Мерить надо ТО ПОЛЕ, КОТОРЫМ
            //   ПОЛЬЗУЕТСЯ ВЫВОЗ, иначе первый круг сходится, а второй теряет дату
            //   и мерка этого не видит. Печатаются оба поля.
            double startShift = (backRd.StartTime - srcRd.StartTime).TotalSeconds;
            double sampleShift = (backRd.SampleInfo.Time - srcRd.StartTime).TotalSeconds;
            bool startOk = Math.Abs(startShift) < 1.0;

            // `A155`: ЖИВОЕ ВРЕМЯ ФОНА. Прежде вывоз писал в LiveTimeDuration фона
            //   его же MeasurementTime, то есть живое время фона в файл не уходило
            //   вовсе. Столбец «-» значит «фона нет и мерить нечего».
            double bgLiveSrc = bgSrc ? srcRd.BackgroundEnergySpectrum.LiveTime : 0.0;
            double bgLiveBack = bgBack ? backRd.BackgroundEnergySpectrum.LiveTime : 0.0;
            string bgLiveOk = !bgSrc ? "-" : (bgBack && bgLiveSrc == bgLiveBack ? "1" : "0");

            bool ok = countsOk && ordSrc == ordBack && coeffRel == 0.0 && timeOk
                      && bgSrc == bgBack && startOk;

            Console.WriteLine("  каналов: " + chSrc + " → " + chBack
                              + (chSrc == chBack ? "  ✓" : "  ⛔ РАЗОШЛОСЬ"));
            Console.WriteLine("  отсчёты: сумма " + sumSrc + " → " + sumBack
                              + ", различающихся каналов " + diffChannels
                              + ", наибольшая разница " + maxAbs
                              + (diffChannels == 0 && sumSrc == sumBack ? "  ✓" : "  ⛔ РАЗОШЛОСЬ"));
            Console.WriteLine("  шкала: порядок " + ordSrc + " → " + ordBack
                              + ", наибольшая ОТНОСИТЕЛЬНАЯ разница коэффициентов "
                              + coeffRel.ToString("E3", CultureInfo.InvariantCulture)
                              + (ordSrc == ordBack && coeffRel == 0.0 ? "  ✓" : "  ⚠ РАЗОШЛОСЬ"));
            Console.WriteLine("    было: " + Coeffs(srcCal));
            Console.WriteLine("    стало: " + Coeffs(backCal));
            Console.WriteLine("    наибольший сдвиг ЭНЕРГИИ по всем каналам: "
                              + maxdE.ToString("E3", CultureInfo.InvariantCulture) + " кэВ");
            Console.WriteLine("  времена: измерение "
                              + srcEs.MeasurementTime.ToString("0.###", CultureInfo.InvariantCulture) + " → "
                              + backEs.MeasurementTime.ToString("0.###", CultureInfo.InvariantCulture)
                              + ", живое "
                              + srcEs.LiveTime.ToString("0.###", CultureInfo.InvariantCulture) + " → "
                              + backEs.LiveTime.ToString("0.###", CultureInfo.InvariantCulture)
                              + (timeOk ? "  ✓"
                                 : ("  ⚠ РАЗОШЛОСЬ на "
                                    + dTime.ToString("E3", CultureInfo.InvariantCulture) + " с")));
            Console.WriteLine("  спектров в файле после ввоза: " + back.ResultDataFile.ResultDataList.Count
                              + " (у корпусного было " + doc.ResultDataFile.ResultDataList.Count
                              + " + фон " + (bgSrc ? "есть" : "нет") + ")");
            Console.WriteLine("  ФОН: было " + (bgSrc ? "есть" : "нет") + " → стало "
                              + (bgBack ? "есть" : "нет")
                              + (bgSrc == bgBack ? "  ✓" : "  ⚠ ПРИЗНАК ФОНА ПОТЕРЯН")
                              + (bgSrc && bgBack
                                 ? ("; живое фона " + srcRd.BackgroundEnergySpectrum.LiveTime.ToString("0.###", CultureInfo.InvariantCulture)
                                    + " → " + backRd.BackgroundEnergySpectrum.LiveTime.ToString("0.###", CultureInfo.InvariantCulture))
                                 : ""));
            Console.WriteLine("  НАЧАЛО НАБОРА: сдвиг StartTime " + startShift.ToString("0.###", CultureInfo.InvariantCulture)
                              + " с, SampleInfo.Time " + sampleShift.ToString("0.###", CultureInfo.InvariantCulture)
                              + " с" + (startOk ? "  ✓" : "  ⚠ ВРЕМЯ НАЧАЛА ПОТЕРЯНО"));

            // ==============================================================
            // ВТОРОЙ ОБОРОТ КРУГА (`A156`)
            //
            // ⛔ Первый оборот НЕ ЛОВИТ дефекта «вывоз и ввоз работают с разными
            //    полями»: прочитанное ложится в поле, из которого вывоз не берёт,
            //    и это видно только тогда, когда ввезённый документ выгружают
            //    СНОВА. Поэтому оборотов два: вывоз → ввоз → вывоз → ввоз.
            // ==============================================================
            string start2 = "-";
            double start2Shift = 0.0;
            if (round2 != null)
            {
                Directory.CreateDirectory(round2);
                string dst2 = Path.Combine(round2, Path.GetFileNameWithoutExtension(dst) + ".round2.n42");
                string said2 = ExportOne(back, dst2);
                if (said2 != null)
                {
                    Console.WriteLine("  ⛔ ВТОРОЙ ВЫВОЗ ОТКАЗАЛ: " + said2);
                    start2 = "0";
                }
                else
                {
                    DocEnergySpectrum back2 = new DocEnergySpectrum();
                    string imported2 = null;
                    try
                    {
                        DocumentManager.GetInstance().ImportDocumentN42(back2, dst2);
                    }
                    catch (Exception ex)
                    {
                        imported2 = ex.GetType().Name + ": " + Flat(ex.Message);
                    }
                    if (imported2 != null)
                    {
                        Console.WriteLine("  ⛔ ВТОРОЙ ВВОЗ ОТКАЗАЛ: " + imported2);
                        start2 = "0";
                    }
                    else
                    {
                        ResultData back2Rd = back2.ResultDataFile.ResultDataList[0];
                        start2Shift = (back2Rd.StartTime - srcRd.StartTime).TotalSeconds;
                        start2 = Math.Abs(start2Shift) < 1.0 ? "1" : "0";
                        Console.WriteLine("  ВТОРОЙ КРУГ: начало " + srcRd.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                                          + " → " + back2Rd.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                                          + ", сдвиг " + start2Shift.ToString("0.###", CultureInfo.InvariantCulture) + " с"
                                          + (start2 == "1" ? "  ✓" : "  ⛔ ДАТА НЕ ДОЖИЛА ДО ВТОРОГО ОБОРОТА"));
                    }
                }
            }
            Console.WriteLine("  КРУГ: " + (ok ? "СОШЁЛСЯ ПОЛНОСТЬЮ"
                              : ("отсчёты " + (countsOk ? "СОШЛИСЬ ТОЧНО" : "РАЗОШЛИСЬ")
                                 + ", шкала " + (coeffRel == 0.0 ? "сошлась точно"
                                     : ("сошлась до " + maxdE.ToString("E2", CultureInfo.InvariantCulture) + " кэВ"))
                                 + ", времена " + (timeOk ? "сошлись" : "ПОТЕРЯЛИ ДРОБНУЮ ЧАСТЬ"))));
            Console.WriteLine();

            return string.Join(",", new string[] {
                name, Path.GetFileName(dst), bytes.ToString(CultureInfo.InvariantCulture),
                chSrc.ToString(CultureInfo.InvariantCulture),
                chBack.ToString(CultureInfo.InvariantCulture),
                (chSrc == chBack) ? "1" : "0",
                diffChannels.ToString(CultureInfo.InvariantCulture),
                maxAbs.ToString(CultureInfo.InvariantCulture),
                sumSrc.ToString(CultureInfo.InvariantCulture),
                sumBack.ToString(CultureInfo.InvariantCulture),
                ordSrc.ToString(CultureInfo.InvariantCulture),
                ordBack.ToString(CultureInfo.InvariantCulture),
                coeffRel.ToString("E3", CultureInfo.InvariantCulture),
                maxdE.ToString("E3", CultureInfo.InvariantCulture),
                srcEs.MeasurementTime.ToString("0.###", CultureInfo.InvariantCulture),
                backEs.MeasurementTime.ToString("0.###", CultureInfo.InvariantCulture),
                srcEs.LiveTime.ToString("0.###", CultureInfo.InvariantCulture),
                backEs.LiveTime.ToString("0.###", CultureInfo.InvariantCulture),
                countsOk ? "1" : "0", timeOk ? "1" : "0",
                dTime.ToString("E3", CultureInfo.InvariantCulture),
                bgSrc ? "1" : "0", bgBack ? "1" : "0", startOk ? "1" : "0",
                // ⚠ Формат "R", а не «0.######»: у трёх файлов дерева расхождение
                //   живого времени фона живёт в 11-м знаке (предел сетки 100 нс,
                //   `A147`), и шесть знаков после запятой печатали ОДНО И ТО ЖЕ
                //   число у столбцов, приговор по которым «разошлось».
                bgSrc ? bgLiveSrc.ToString("R", CultureInfo.InvariantCulture) : "-",
                bgBack ? bgLiveBack.ToString("R", CultureInfo.InvariantCulture) : "-",
                bgLiveOk,
                bgSrc && bgBack
                    ? Math.Abs(bgLiveSrc - bgLiveBack).ToString("E3", CultureInfo.InvariantCulture)
                    : "-",
                start2, start2 == "-" ? "-" : start2Shift.ToString("0.###", CultureInfo.InvariantCulture) });
        }

        /// <summary>
        /// Вывоз ОДНОГО документа тем же путём, что у приложения: Util.ExportToN42
        /// плюс XmlSerializer с настройками DocumentManager. Возвращает null, если
        /// вывоз удался, иначе — сказанное отказом.
        ///
        /// ⚠ Метод выделен для ВТОРОГО круга (`A156`): вывоз обязан быть тем же
        /// самым, иначе второй оборот мерил бы другой код и находка не значила бы
        /// ничего.
        /// </summary>
        static string ExportOne(DocEnergySpectrum doc, string dst)
        {
            try
            {
                Util util = new Util();
                RadInstrumentData radN42Object = util.ExportToN42(doc);
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(RadInstrumentData));
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.Encoding = Encoding.UTF8;
                settings.Indent = true;
                BecquerelMonitor.Utils.AtomicFileWriter.Write(dst, delegate(Stream fileStream)
                {
                    using (XmlWriter writer = XmlWriter.Create(fileStream, settings))
                    {
                        xmlSerializer.Serialize(writer, radN42Object);
                        writer.Flush();
                    }
                });
                return null;
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + Flat(ex.Message);
            }
        }

        static double CoeffDiff(PolynomialEnergyCalibration a, PolynomialEnergyCalibration b)
        {
            if (a == null || b == null) return double.NaN;
            if (a.Coefficients.Length != b.Coefficients.Length) return double.PositiveInfinity;
            double worst = 0.0;
            for (int i = 0; i < a.Coefficients.Length; i++)
            {
                double x = a.Coefficients[i], y = b.Coefficients[i];
                if (x == y) continue;
                double scale = Math.Max(Math.Abs(x), Math.Abs(y));
                double rel = scale == 0.0 ? Math.Abs(x - y) : Math.Abs(x - y) / scale;
                if (rel > worst) worst = rel;
            }
            return worst;
        }

        static string Coeffs(PolynomialEnergyCalibration c)
        {
            if (c == null) return "калибровки нет";
            return "[" + string.Join(", ", c.Coefficients.Select(
                x => x.ToString("R", CultureInfo.InvariantCulture)).ToArray()) + "]";
        }

        // ==================================================================
        // ВВОЗ КАТАЛОГА — СЛЕПОК, КОТОРЫМ СВЕРЯЮТСЯ ДВА ПЛЕЧА СБОРОК
        // ==================================================================

        static int Import()
        {
            if (outDir == null)
            {
                Console.Error.WriteLine("нужен --out=<каталог с n42>");
                return 2;
            }
            string[] files = Directory.GetFiles(outDir, "*.n42");
            Array.Sort(files, StringComparer.Ordinal);

            Console.WriteLine("=== ВВОЗ КАТАЛОГА " + Path.GetFullPath(outDir) + " ===");
            Console.WriteLine("  файлов: " + files.Length);
            Console.WriteLine();

            // Два раздела нарочно. СЛЕПОК сверяется между плечами посимвольно:
            // в нём только числа и приговор. ГОЛОСА — то, что приложение
            // говорит человеку; они между плечами МЕНЯЮТСЯ, в этом и работа.
            List<string> prints = new List<string>();
            List<string> voices = new List<string>();
            int ok = 0, failed = 0;

            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                DocEnergySpectrum doc = new DocEnergySpectrum();
                string said = null;

                // ⛔ СКАЗАННОЕ БЕЗ ОТКАЗА ТОЖЕ ЛОВИТСЯ (`A157`, `A160`).
                //    AppUi.Report без окон пишет в поток ошибок и работу
                //    продолжает — то есть приложение говорит, а проба этого не
                //    видела вовсе и печатала «(молча)». Признак без читателя это
                //    не признак: поток подменяется на время ввоза.
                TextWriter realErr = Console.Error;
                StringWriter caught = new StringWriter();
                Console.SetError(caught);
                try
                {
                    DocumentManager.GetInstance().ImportDocumentN42(doc, f);
                }
                catch (Exception ex)
                {
                    said = ex.GetType().Name + ": " + Flat(ex.Message);
                    Exception inner = ex.InnerException;
                    while (inner != null)
                    {
                        said += " <- " + inner.GetType().Name + ": " + Flat(inner.Message);
                        inner = inner.InnerException;
                    }
                }
                finally
                {
                    Console.SetError(realErr);
                }
                string spoken = Flat(caught.ToString()).Trim();

                if (said == null)
                {
                    ok++;
                    prints.Add(name + " | ВВЕЗЁН | " + Print(doc));
                    voices.Add(name + " | ВВЕЗЁН | " + (spoken.Length == 0 ? "(молча)" : spoken));
                }
                else
                {
                    failed++;
                    prints.Add(name + " | ОТКАЗ | " + Print(doc));
                    voices.Add(name + " | ОТКАЗ | " + said
                               + (spoken.Length == 0 ? "" : "   [сказано вслух: " + spoken + "]"));
                }
            }

            Console.WriteLine("=== ПРИГОВОРЫ (ОБЯЗАНЫ совпасть между плечами) ===");
            // ⛔ Отдельный раздел заведён 05.09.2026 (полоса `A146`…`A152`).
            //    Слепок целиком между плечами УЖЕ НЕ СОВПАДАЕТ по построению:
            //    времена получают дробную часть (`A147`), коэффициенты — последнюю
            //    цифру (`A148`), фон уезжает из списка в поле фона (`A150`). А
            //    главное требование полосы — «список успешно импортируемых файлов
            //    не имеет права измениться» — про ПРИГОВОР, а не про числа. Раз
            //    числа меняются нарочно, приговор обязан жить отдельной строкой,
            //    которую можно сверить посимвольно.
            foreach (string p in prints)
            {
                int bar = p.IndexOf('|');
                int bar2 = p.IndexOf('|', bar + 1);
                Console.WriteLine(p.Substring(0, bar2).TrimEnd());
            }
            Console.WriteLine();
            Console.WriteLine("=== СЛЕПОК (числа; меняется ТОЛЬКО названными правками) ===");
            foreach (string p in prints) Console.WriteLine(p);
            Console.WriteLine();
            Console.WriteLine("ВВЕЗЕНО: " + ok + "   ОТКАЗАНО: " + failed);
            Console.WriteLine();
            Console.WriteLine("=== ГОЛОСА (между плечами МЕНЯЮТСЯ нарочно) ===");
            foreach (string v in voices) Console.WriteLine(v);
            return 0;
        }

        static string Print(DocEnergySpectrum doc)
        {
            try
            {
                if (doc == null || doc.ResultDataFile == null) return "документа нет";
                int count = doc.ResultDataFile.ResultDataList.Count;
                StringBuilder sb = new StringBuilder();
                sb.Append("спектров ").Append(count);
                for (int i = 0; i < count; i++)
                {
                    ResultData rd = doc.ResultDataFile.ResultDataList[i];
                    EnergySpectrum es = rd.EnergySpectrum;
                    if (es == null) { sb.Append(" | [").Append(i).Append("] спектра нет"); continue; }
                    long sum = es.Spectrum == null ? -1 : es.Spectrum.Sum(x => (long)x);
                    PolynomialEnergyCalibration p = es.EnergyCalibration as PolynomialEnergyCalibration;
                    sb.Append(" | [").Append(i).Append("] кан ").Append(es.NumberOfChannels)
                      .Append(", сумма ").Append(sum)
                      .Append(", всего ").Append(es.TotalPulseCount)
                      .Append(", изм ").Append(es.MeasurementTime.ToString("0.######", CultureInfo.InvariantCulture))
                      .Append(", живое ").Append(es.LiveTime.ToString("0.######", CultureInfo.InvariantCulture))
                      // `A146`: время начала набора берётся из файла и молча
                      //   подменяется на «сейчас», если запись не прочиталась, —
                      //   поэтому в слепке печатается не само время, а СОВПАЛО ЛИ
                      //   оно с «сейчас» с точностью до минуты. Печатать саму дату
                      //   нельзя: у двух плеч она была бы разной по построению.
                      .Append(", начало ").Append(
                          rd.SampleInfo == null ? "нет"
                          : (Math.Abs((DateTime.Now - rd.SampleInfo.Time).TotalMinutes) < 1.0
                             ? "ПОТЕРЯНО (сейчас)"
                             : rd.SampleInfo.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))
                      // `A156`: ВТОРОЕ ПОЛЕ ВРЕМЕНИ, И ИМЕННО ИМ ПОЛЬЗУЕТСЯ ВЫВОЗ.
                      //   Прежде слепок его не печатал вовсе, и «дата доехала»
                      //   мерилось по полю, которое вывоз не читает: круг сходился
                      //   на первом обороте и терял дату на втором.
                      .Append(", StartTime ").Append(
                          Math.Abs((DateTime.Now - rd.StartTime).TotalMinutes) < 1.0
                          ? "ПОТЕРЯНО (сейчас)"
                          : rd.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                      .Append(", шкала ").Append(p == null
                          ? (es.EnergyCalibration == null ? "нет" : es.EnergyCalibration.GetType().Name)
                          : ("порядок " + p.PolynomialOrder + " " + Coeffs(p)));
                    // `A150`: фон — ОТДЕЛЬНАЯ сущность документа, а не строка списка.
                    //   До правки он приезжал вторым равноправным спектром, и этой
                    //   пометки в слепке не было вовсе.
                    EnergySpectrum bg = rd.BackgroundEnergySpectrum;
                    sb.Append(bg == null ? ", фона нет"
                        : (", ФОН кан " + bg.NumberOfChannels
                           + ", сумма " + bg.Spectrum.Sum(x => (long)x)
                           + ", изм " + bg.MeasurementTime.ToString("0.######", CultureInfo.InvariantCulture)
                           + ", живое " + bg.LiveTime.ToString("0.######", CultureInfo.InvariantCulture)
                           + ", подпись «" + (rd.BackgroundSpectrumFile ?? "нет") + "»"));
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "слепок не снялся: " + ex.GetType().Name + ": " + Flat(ex.Message);
            }
        }

        // ==================================================================
        // СОЧИНЁННЫЕ ВХОДЫ РАЗБОРА КАЛИБРОВКИ — для `A136`, `A140`, `A142`
        // ==================================================================

        static int Cases()
        {
            if (outDir == null)
            {
                Console.Error.WriteLine("нужен --out=<куда класть сочинённые входы>");
                return 2;
            }
            Directory.CreateDirectory(outDir);

            Write(Path.Combine(outDir, "case1_boundary.n42"), Head("case1")
                  + "  <EnergyCalibration id=\"EC1\">\r\n"
                  + "    <EnergyBoundaryValues>" + Edges() + "</EnergyBoundaryValues>\r\n"
                  + "  </EnergyCalibration>\r\n" + Tail());

            Write(Path.Combine(outDir, "case2_notanumber.n42"), Head("case2")
                  + "  <EnergyCalibration id=\"EC1\">\r\n"
                  + "    <CoefficientValues>3.5 abc</CoefficientValues>\r\n"
                  + "  </EnergyCalibration>\r\n" + Tail());

            Write(Path.Combine(outDir, "case3_order5.n42"), Head("case3")
                  + "  <EnergyCalibration id=\"EC1\">\r\n"
                  + "    <CoefficientValues>1 2 3 4 5 6</CoefficientValues>\r\n"
                  + "  </EnergyCalibration>\r\n" + Tail());

            Write(Path.Combine(outDir, "case4_empty.n42"), Head("case4")
                  + "  <EnergyCalibration id=\"EC1\">\r\n"
                  + "    <CoefficientValues></CoefficientValues>\r\n"
                  + "  </EnergyCalibration>\r\n" + Tail());

            Write(Path.Combine(outDir, "case5_healthy.n42"), Head("case5")
                  + "  <EnergyCalibration id=\"EC1\">\r\n"
                  + "    <CoefficientValues>3.5 12.5</CoefficientValues>\r\n"
                  + "  </EnergyCalibration>\r\n" + Tail());

            Write(Path.Combine(outDir, "case6_nocalib.n42"), Head("case6") + Tail());

            // Путь RadiologicalInstrumentData (`A141`) — третий разбор N42 в
            // этом файле, и до 05.09.2026 у него было СВОЁ, третье соглашение:
            // калибровка подгонялась только при пяти точках и больше, иначе в
            // документ молча уходила y = x, а подогнанный полином не
            // проверялся CheckCalibration вовсе.
            Write(Path.Combine(outDir, "case7_rad_few.n42"), Rad(4));
            Write(Path.Combine(outDir, "case8_rad_many.n42"), Rad(1024));

            // `A152`: вход, у которого каналов БОЛЬШЕ, чем у документа (8192 у
            // пустого DocEnergySpectrum). Прежде число каналов ФАЙЛА в спектр не
            // записывалось вовсе, и цикл разбора выходил за конец массива
            // документа — ввоз падал IndexOutOfRangeException. ⚠ Это ЕДИНСТВЕННЫЙ
            // вход полосы, у которого приговор меняется нарочно, и он сочинённый:
            // настоящих файлов RadiologicalInstrumentData в дереве нет.
            Write(Path.Combine(outDir, "case9_rad_over.n42"), Rad(9000));

            // ==============================================================
            // КЛАСС ИЗМЕРЕНИЯ (`A160`), КРАЙНИЕ СЛУЧАИ ФОНА (`A150`),
            // НЕЧИТАЕМАЯ ДАТА (`A157`) И ОТРИЦАТЕЛЬНЫЙ ОТСЧЁТ (`A158`).
            //
            // ⚠ Все они СОЧИНЕНЫ и говорят о поведении РАЗБОРА, а не о том, что
            //    такие файлы встречаются. Настоящих файлов с классом Calibration
            //    в дереве нет — как нет и настоящего RadiologicalInstrumentData.
            // ==============================================================
            Write(Path.Combine(outDir, "case10_calibclass.n42"),
                  Multi("case10", Meas("M1", "Foreground", "2026-09-05T12:00:00Z", Counts(0))
                                + Meas("M2", "Calibration", "2026-09-05T13:00:00Z", Counts(1))));

            Write(Path.Combine(outDir, "case11_intrinsic.n42"),
                  Multi("case11", Meas("M1", "Foreground", "2026-09-05T12:00:00Z", Counts(0))
                                + Meas("M2", "IntrinsicActivity", "2026-09-05T13:00:00Z", Counts(1))));

            // Крайний случай: в файле НЕТ ни одного ввозимого измерения.
            Write(Path.Combine(outDir, "case12_calibonly.n42"),
                  Multi("case12", Meas("M1", "Calibration", "2026-09-05T12:00:00Z", Counts(0))));

            // Крайние случаи фона (`A150`): цепляться не к чему, только фон, два фона.
            Write(Path.Combine(outDir, "case13_bgfirst.n42"),
                  Multi("case13", Meas("M1", "Background", "2026-09-05T12:00:00Z", Counts(0))
                                + Meas("M2", "Foreground", "2026-09-05T13:00:00Z", Counts(1))));
            Write(Path.Combine(outDir, "case14_bgonly.n42"),
                  Multi("case14", Meas("M1", "Background", "2026-09-05T12:00:00Z", Counts(0))));
            Write(Path.Combine(outDir, "case15_twobg.n42"),
                  Multi("case15", Meas("M1", "Foreground", "2026-09-05T12:00:00Z", Counts(0))
                                + Meas("M2", "Background", "2026-09-05T13:00:00Z", Counts(1))
                                + Meas("M3", "Background", "2026-09-05T14:00:00Z", Counts(2))));

            // `A157`: дата, какую писала СТАРАЯ сборка под ar-SA — год по хиджре и
            //   арабское «до полудня». Ни XmlConvert, ни en-US, ни инвариантная
            //   культура её не читают. ⚠ Под ar-SA она прочитается: приговор такого
            //   входа зависит от культуры прогона, и мерить его надо под en-US.
            Write(Path.Combine(outDir, "case16_baddate.n42"),
                  Multi("case16", Meas("M1", "Foreground", "04/05/44 10:07:57 ص", Counts(0))));

            // `A158`: ОТРИЦАТЕЛЬНЫЙ ОТСЧЁТ — положительный контроль культурного
            //   чтения целых. У 41 культуры из 890 (замер 05.09.2026 на этой
            //   машине) знак минуса НЕ «-»: например у sv-SE это U+2212. Под такой
            //   культурой старый int.Parse на строке «-3» из файла отказывает, а
            //   инвариантный читает. На положительных отсчётах разницы нет вовсе —
            //   потому дефект и держался на данных, а не на разборе.
            Write(Path.Combine(outDir, "case17_negcount.n42"),
                  Multi("case17", Meas("M1", "Foreground", "2026-09-05T12:00:00Z", NegCounts())));

            Console.WriteLine("сочинённые входы положены в " + Path.GetFullPath(outDir));
            Console.WriteLine("⚠ Они СОЧИНЕНЫ и доказывают поведение РАЗБОРА, а не совместимость с приборами.");
            return 0;
        }

        static void Write(string path, string xml)
        {
            File.WriteAllText(path, xml, new UTF8Encoding(false));
            Console.WriteLine("  " + Path.GetFileName(path));
        }

        static string Head(string tag)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<RadInstrumentData xmlns=\"http://physics.nist.gov/N42/2011/N42\" n42DocUUID=\"probe-a136-"
                 + tag + "\">\r\n"
                 + "  <RadInstrumentInformation id=\"RadInstrument\">\r\n"
                 + "    <RadInstrumentManufacturerName>PROBE</RadInstrumentManufacturerName>\r\n"
                 + "    <RadInstrumentModelName>A136</RadInstrumentModelName>\r\n"
                 + "    <RadInstrumentClassCode>Radionuclide Identifier</RadInstrumentClassCode>\r\n"
                 + "    <RadInstrumentVersion>\r\n"
                 + "      <RadInstrumentComponentName>Hardware</RadInstrumentComponentName>\r\n"
                 + "      <RadInstrumentComponentVersion>1</RadInstrumentComponentVersion>\r\n"
                 + "    </RadInstrumentVersion>\r\n"
                 + "  </RadInstrumentInformation>\r\n";
        }

        /// <summary>
        /// Файл N42-2012 с ОДНОЙ шкалой и любым числом измерений (`A150`, `A160`).
        /// </summary>
        static string Multi(string tag, string measurements)
        {
            return Head(tag)
                 + "  <EnergyCalibration id=\"EC1\">\r\n"
                 + "    <CoefficientValues>3.5 12.5</CoefficientValues>\r\n"
                 + "  </EnergyCalibration>\r\n"
                 + measurements
                 + "</RadInstrumentData>\r\n";
        }

        static string Meas(string id, string classCode, string startDateTime, string counts)
        {
            return "  <RadMeasurement id=\"" + id + "\">\r\n"
                 + "    <MeasurementClassCode>" + classCode + "</MeasurementClassCode>\r\n"
                 + "    <StartDateTime>" + startDateTime + "</StartDateTime>\r\n"
                 + "    <RealTimeDuration>PT300S</RealTimeDuration>\r\n"
                 + "    <Spectrum id=\"S-" + id + "\" energyCalibrationReference=\"EC1\">\r\n"
                 + "      <LiveTimeDuration>PT295S</LiveTimeDuration>\r\n"
                 + "      <ChannelData>" + counts + "</ChannelData>\r\n"
                 + "    </Spectrum>\r\n"
                 + "  </RadMeasurement>\r\n";
        }

        /// <summary>64 канала отсчётов; сдвиг делает измерения РАЗЛИЧИМЫМИ по сумме.</summary>
        static string Counts(int shift)
        {
            StringBuilder counts = new StringBuilder();
            for (int i = 0; i < 64; i++)
            {
                if (i > 0) counts.Append(' ');
                counts.Append((i % 7) + 1 + shift);
            }
            return counts.ToString();
        }

        /// <summary>64 канала, у одного отсчёт ОТРИЦАТЕЛЬНЫЙ (`A158`).</summary>
        static string NegCounts()
        {
            StringBuilder counts = new StringBuilder();
            for (int i = 0; i < 64; i++)
            {
                if (i > 0) counts.Append(' ');
                counts.Append(i == 7 ? -3 : (i % 7) + 1);
            }
            return counts.ToString();
        }

        static string Edges()
        {
            StringBuilder edges = new StringBuilder();
            for (int i = 0; i <= 64; i++)
            {
                if (i > 0) edges.Append(' ');
                edges.Append((i * 12.5).ToString(CultureInfo.InvariantCulture));
            }
            return edges.ToString();
        }

        static string Tail()
        {
            StringBuilder counts = new StringBuilder();
            for (int i = 0; i < 64; i++)
            {
                if (i > 0) counts.Append(' ');
                counts.Append((i % 7) + 1);
            }
            return "  <RadMeasurement id=\"M1\">\r\n"
                 + "    <MeasurementClassCode>Foreground</MeasurementClassCode>\r\n"
                 + "    <StartDateTime>2026-09-05T12:00:00Z</StartDateTime>\r\n"
                 + "    <RealTimeDuration>PT300S</RealTimeDuration>\r\n"
                 + "    <Spectrum id=\"S1\" radDetectorInformationReference=\"D1\" energyCalibrationReference=\"EC1\">\r\n"
                 + "      <LiveTimeDuration>PT295S</LiveTimeDuration>\r\n"
                 + "      <ChannelData>" + counts + "</ChannelData>\r\n"
                 + "    </Spectrum>\r\n"
                 + "  </RadMeasurement>\r\n"
                 + "</RadInstrumentData>\r\n";
        }

        /// <summary>
        /// Вход пути RadiologicalInstrumentData на n каналов.
        ///
        /// Шкала задана СПИСКОМ энергий каналов (CalibrationEquation = List) —
        /// иного этот разбор не принимает. Полином под ней настоящий 4-го
        /// порядка: E(i) = 5 + 0.5·i + 1e-6·i² + 1e-11·i³ + 1e-14·i⁴, растёт
        /// монотонно и на 8192-м канале даёт ≈ 4219 кэВ, то есть подгонка
        /// обязана и сойтись, и пройти CheckCalibration. ⚠ Энергии записаны
        /// с ДРОБНОЙ частью нарочно: тем же входом мерится третье место
        /// культурно-зависимого разбора (`A142`, decimal.Parse).
        /// </summary>
        static string Rad(int n)
        {
            StringBuilder energies = new StringBuilder();
            StringBuilder counts = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                double e = 5.0 + 0.5 * i + 1e-6 * i * i + 1e-11 * i * i * i + 1e-14 * i * i * i * i;
                if (i > 0) { energies.Append(' '); counts.Append(' '); }
                energies.Append(e.ToString("0.######", CultureInfo.InvariantCulture));
                counts.Append((i % 7) + 1);
            }
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<RadiologicalInstrumentData xmlns=\"http://physics.nist.gov/N42/2006/N42\">\r\n"
                 + "  <MeasurementGroup>\r\n"
                 + "    <Measurement>\r\n"
                 + "      <Spectrum>\r\n"
                 + "        <InstrumentInformation>\r\n"
                 + "          <Manufacturer>PROBE</Manufacturer>\r\n"
                 + "          <Model>A141</Model>\r\n"
                 + "          <SerialNumber>" + n + "</SerialNumber>\r\n"
                 + "        </InstrumentInformation>\r\n"
                 + "        <EnergyCalibration>\r\n"
                 + "          <CalibrationEquation>List</CalibrationEquation>\r\n"
                 + "          <ChannelEnergies>" + energies + "</ChannelEnergies>\r\n"
                 + "        </EnergyCalibration>\r\n"
                 + "        <ChannelData NumberOfChannels=\"" + n + "\">" + counts + "</ChannelData>\r\n"
                 + "        <LiveTime>295</LiveTime>\r\n"
                 + "        <SpectrumType>Item</SpectrumType>\r\n"
                 + "      </Spectrum>\r\n"
                 + "    </Measurement>\r\n"
                 + "  </MeasurementGroup>\r\n"
                 + "</RadiologicalInstrumentData>\r\n";
        }

        // ==================================================================
        // ЧТО ЗОВЁТ СОБРАННЫЙ МЕТОД — разбор IL, а не исходника
        //
        // Оконная половина каждой двери (AppUi.Report) в безоконной пробе не
        // исполняется, и доказать поведением «текст выбирается по причине»
        // нельзя. Поэтому мерится АРТЕФАКТ: настоящий проход по опкодам
        // собранного метода. Приём взят у `A135` целиком, второго не заводится.
        // ==================================================================

        static int Il()
        {
            Assembly app = typeof(DocumentManager).Assembly;
            Type util = app.GetType("BecquerelMonitor.N42.Util");
            string[] wanted = { "RadInstrumentData", "N42InstrumentData", "RadiologicalInstrumentData" };
            foreach (string w in wanted)
            {
                MethodInfo m = util.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(x => x.Name == "ImportFromN42"
                                         && x.GetParameters().Length == 3
                                         && x.GetParameters()[0].ParameterType.Name == w);
                Console.WriteLine("=== IL Util.ImportFromN42(" + w + ", ...) ===");
                if (m == null) { Console.WriteLine("  ⛔ метода нет"); continue; }
                Walk(m);
                Console.WriteLine();
            }
            return 0;
        }

        static void Walk(MethodInfo m)
        {
            byte[] il = m.GetMethodBody().GetILAsByteArray();
            Module mod = m.Module;
            Dictionary<short, System.Reflection.Emit.OpCode> table =
                new Dictionary<short, System.Reflection.Emit.OpCode>();
            foreach (FieldInfo f in typeof(System.Reflection.Emit.OpCodes)
                     .GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.FieldType != typeof(System.Reflection.Emit.OpCode)) continue;
                System.Reflection.Emit.OpCode op = (System.Reflection.Emit.OpCode)f.GetValue(null);
                table[op.Value] = op;
            }

            List<string> literals = new List<string>();
            List<string> calls = new List<string>();
            int i = 0;
            while (i < il.Length)
            {
                short code = il[i];
                if (il[i] == 0xFE) { code = (short)(0xFE00 | il[i + 1]); i += 2; } else { i += 1; }
                System.Reflection.Emit.OpCode op;
                if (!table.TryGetValue(code, out op)) { Console.WriteLine("  ⛔ неизвестный опкод"); break; }
                int operand;
                switch (op.OperandType)
                {
                    case System.Reflection.Emit.OperandType.InlineNone: operand = 0; break;
                    case System.Reflection.Emit.OperandType.ShortInlineBrTarget:
                    case System.Reflection.Emit.OperandType.ShortInlineI:
                    case System.Reflection.Emit.OperandType.ShortInlineVar: i += 1; operand = 0; break;
                    case System.Reflection.Emit.OperandType.InlineVar: i += 2; operand = 0; break;
                    case System.Reflection.Emit.OperandType.InlineBrTarget:
                    case System.Reflection.Emit.OperandType.InlineI:
                    case System.Reflection.Emit.OperandType.ShortInlineR: i += 4; operand = 0; break;
                    case System.Reflection.Emit.OperandType.InlineI8:
                    case System.Reflection.Emit.OperandType.InlineR: i += 8; operand = 0; break;
                    case System.Reflection.Emit.OperandType.InlineSwitch:
                        { int k = BitConverter.ToInt32(il, i); i += 4 + 4 * k; operand = 0; break; }
                    case System.Reflection.Emit.OperandType.InlineString:
                    case System.Reflection.Emit.OperandType.InlineMethod:
                    case System.Reflection.Emit.OperandType.InlineField:
                    case System.Reflection.Emit.OperandType.InlineType:
                    case System.Reflection.Emit.OperandType.InlineTok:
                    case System.Reflection.Emit.OperandType.InlineSig:
                        operand = BitConverter.ToInt32(il, i); i += 4; break;
                    default:
                        Console.WriteLine("  ⛔ неучтённый вид операнда " + op.OperandType);
                        i = il.Length; operand = 0; break;
                }
                if (op.OperandType == System.Reflection.Emit.OperandType.InlineString)
                {
                    literals.Add(mod.ResolveString(operand));
                }
                else if (op.OperandType == System.Reflection.Emit.OperandType.InlineMethod)
                {
                    try
                    {
                        MethodBase mb = mod.ResolveMethod(operand);
                        if (mb.DeclaringType == null) continue;
                        string full = mb.DeclaringType.Name + "." + mb.Name;
                        if (mb.DeclaringType.Name == "Resources"
                            || mb.Name == "get_Message"
                            || mb.Name == "CheckCalibration"
                            || mb.Name == "HasEnergyBoundaryValues"
                            || mb.Name == "get_EnergyBoundaryValues"
                            || mb.Name == "get_HasWindows"
                            || mb.Name == "Report"
                            || mb.Name == "Parse")
                        {
                            calls.Add(full + (mb.Name == "Parse"
                                ? "(" + string.Join(",", mb.GetParameters()
                                    .Select(p => p.ParameterType.Name).ToArray()) + ")"
                                : ""));
                        }
                    }
                    catch { }
                }
            }
            foreach (string c in calls.Distinct().OrderBy(x => x, StringComparer.Ordinal))
            {
                Console.WriteLine("  " + calls.Count(x => x == c) + "× " + c);
            }
            Console.WriteLine("  строковых литералов в методе: " + literals.Count);
        }

        static string Flat(string s)
        {
            if (s == null) return "";
            return s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
