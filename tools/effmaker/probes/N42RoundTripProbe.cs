using BecquerelMonitor;
using BecquerelMonitor.N42;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
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
    ///                    и они нарочно лежат ОТДЕЛЬНО от настоящих;
    ///   --mode=il      — обход опкодов СОБРАННЫХ методов: чем читается число
    ///                    и какие тексты зовутся (`A135`, `A158`);
    ///   --mode=res     — что отвечает ResourceManager СОБРАННОЙ сборки на
    ///                    названные ключи в обеих культурах (`A154`);
    ///   --mode=specutils — тот же каталог ВТОРОЙ дверью приложения,
    ///                    DocumentManager.ImportDocumentSpecUtils (`A175`):
    ///                    у двух дверей одного файла разные соглашения, и
    ///                    мерить надо обе.
    ///   --mode=noconfig — ОБЕ двери при ПУСТОМ списке конфигураций приборов
    ///                    (`A212`): первый запуск, испорченный профиль. Восемь
    ///                    плеч — {список полон, список пуст} × {документ пробы,
    ///                    документ приложения} × {дверь SpecUtils, дверь N42},
    ///                    и полные плечи здесь ПОЛОЖИТЕЛЬНЫЙ контроль: без них
    ///                    «упало» значило бы «проба не работает вовсе».
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
            if (mode == "res") return Res();
            if (mode == "specutils") return SpecUtils();
            if (mode == "noconfig") return NoConfig();
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

        // ==================================================================
        // ⛔ ВТОРАЯ ДВЕРЬ ТОГО ЖЕ ФАЙЛА — `DocumentManager.ImportDocumentSpecUtils`
        //    (`A175`).
        //
        //    Один и тот же .n42 приложение умеет ввозить ДВУМЯ путями: своим
        //    разбором (пункт меню «Import N42») и через SpecUtils (пункт
        //    «Import spectrum file»). У них РАЗНЫЕ соглашения о калибровочных
        //    измерениях, и до 05.09.2026 проба мерила только первый — то есть
        //    половина поведения приложения на одних и тех же файлах не мерилась
        //    ничем. Раздел ГОЛОСОВ здесь и есть измеряемая величина.
        // ==================================================================
        static int SpecUtils()
        {
            if (outDir == null)
            {
                Console.Error.WriteLine("нужен --out=<каталог с файлами спектров>");
                return 2;
            }
            string[] files = Directory.GetFiles(outDir, "*.n42");
            Array.Sort(files, StringComparer.Ordinal);

            Console.WriteLine("=== ВВОЗ ЧЕРЕЗ SpecUtils КАТАЛОГА " + Path.GetFullPath(outDir) + " ===");
            Console.WriteLine("  файлов: " + files.Length);
            Console.WriteLine();

            List<string> prints = new List<string>();
            List<string> voices = new List<string>();
            int ok = 0, failed = 0;

            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                DocEnergySpectrum doc = new DocEnergySpectrum();
                string said = null;

                TextWriter realErr = Console.Error;
                StringWriter caught = new StringWriter();
                Console.SetError(caught);
                try
                {
                    DocumentManager.GetInstance().ImportDocumentSpecUtils(doc, f, 3600);
                }
                catch (Exception ex)
                {
                    said = ex.GetType().Name + ": " + Flat(ex.Message);
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

            Console.WriteLine("=== ПРИГОВОРЫ SpecUtils ===");
            foreach (string p in prints)
            {
                int bar = p.IndexOf('|');
                int bar2 = p.IndexOf('|', bar + 1);
                Console.WriteLine(p.Substring(0, bar2).TrimEnd());
            }
            Console.WriteLine();
            Console.WriteLine("=== СЛЕПОК SpecUtils ===");
            foreach (string p in prints) Console.WriteLine(p);
            Console.WriteLine();
            Console.WriteLine("ВВЕЗЕНО: " + ok + "   ОТКАЗАНО: " + failed);
            Console.WriteLine();
            Console.WriteLine("=== ГОЛОСА SpecUtils ===");
            foreach (string v in voices) Console.WriteLine(v);
            return 0;
        }

        // ==================================================================
        // ⛔ ПУСТОЙ СПИСОК КОНФИГУРАЦИЙ ПРИБОРОВ — ЭТО ВХОД, А НЕ ПОЛОМКА
        //    ОСНАСТКИ (`A212`).
        //
        //    05.09.2026 полоса `C12` получила `NullReferenceException` на всех
        //    входах ввоза через SpecUtils и списала его на свежий каталог проб
        //    без `config\device`. Каталог был поправлен, замер выброшен — а
        //    положение осталось достижимым и у человека: `LoadAllConfigFiles`
        //    при пустом (но существующем) каталоге отдаёт ПУСТОЙ список молча,
        //    и в окнах он же заводит пустой каталог сам, когда его нет вовсе.
        //    То есть первый запуск и испорченный профиль дают ровно это.
        //
        //    Мерится ЧЕТЫРЬМЯ парами плеч, и полные плечи здесь не украшение:
        //    без них «упало при пустом списке» неотличимо от «проба не умеет
        //    ввозить вовсе». Второй разрез — КЕМ создан документ: проба до сих
        //    пор строила `new DocEnergySpectrum()` (минуя `CheckDocument`), а
        //    пункт меню зовёт `DocumentManager.CreateDocument()`. Это разные
        //    пути, и приговор у них может разойтись — тогда «у человека упадёт
        //    так же» окажется посылкой, а не измерением.
        // ==================================================================
        static int NoConfig()
        {
            if (outDir == null)
            {
                Console.Error.WriteLine("нужен --out=<каталог с файлами спектров>");
                return 2;
            }
            string[] files = Directory.GetFiles(outDir, "*.n42");
            Array.Sort(files, StringComparer.Ordinal);

            DeviceConfigManager dcm = DeviceConfigManager.GetInstance();
            ROIConfigManager rcm = ROIConfigManager.GetInstance();
            List<DeviceConfigInfo> saved = new List<DeviceConfigInfo>(dcm.DeviceConfigList);

            Console.WriteLine("=== ПУСТОЙ СПИСОК КОНФИГУРАЦИЙ ПРИБОРОВ, ОБЕ ДВЕРИ (`A212`) ===");
            Console.WriteLine("  каталог: " + Path.GetFullPath(outDir));
            Console.WriteLine("  файлов: " + files.Length);
            Console.WriteLine("  конфигураций приборов загружено: " + saved.Count);
            Console.WriteLine("  конфигураций ROI загружено: " + rcm.ROIConfigList.Count
                              + "  (не трогаются: разрез идёт по ОДНОЙ величине)");
            Console.WriteLine();

            int rc = 0;
            // Порядок нарочный: сперва полные плечи (положительный контроль),
            // потом пустые. Список возвращается на место в finally.
            try
            {
                rc |= Arm("СПИСОК ПОЛОН", "документ пробы", "SpecUtils", files);
                rc |= Arm("СПИСОК ПОЛОН", "документ пробы", "N42", files);
                rc |= Arm("СПИСОК ПОЛОН", "документ приложения", "SpecUtils", files);
                rc |= Arm("СПИСОК ПОЛОН", "документ приложения", "N42", files);

                dcm.DeviceConfigList.Clear();
                Console.WriteLine("--- список конфигураций приборов ОПУСТОШЁН: "
                                  + dcm.DeviceConfigList.Count + " ---");
                Console.WriteLine();

                rc |= Arm("СПИСОК ПУСТ", "документ пробы", "SpecUtils", files);
                rc |= Arm("СПИСОК ПУСТ", "документ пробы", "N42", files);
                rc |= Arm("СПИСОК ПУСТ", "документ приложения", "SpecUtils", files);
                rc |= Arm("СПИСОК ПУСТ", "документ приложения", "N42", files);

                // ⛔ ТРЕТЬЕ СОСТОЯНИЕ, И ОНО ЕДИНСТВЕННОЕ ДОСТАЁТ ЧЕЛОВЕКА ЗА
                //    ЭКРАНОМ. Пустой список конфигураций пунктом меню не ловится:
                //    `DocumentManager.CreateDocument` зовёт `CheckDocument`, а тот
                //    достраивает ПШПВ умолчанием — это видно двумя плечами выше.
                //    Но умолчание СТРОИТСЯ НЕ ВСЕГДА: `DefaultCalibration` кладёт
                //    прямую через (0, FWHM_AT_0) и (Ch_Fwhm, Width_Fwhm) и отдаёт
                //    null, если она не растёт (`ResultData.cs:470`).
                //    ⚠ Формы у этих трёх чисел НЕТ: они приходят из
                //    `config\device\*.xml` и из заготовок приборов, и во ВСЕХ
                //    поставочных конфигурациях дерева прямая растёт. Плечо
                //    изображает конфигурацию правленую руками, чужую или
                //    переехавшую со старого извода (у такой нет и элемента
                //    `FwhmCalibration`, иначе умолчание не считалось бы вовсе),
                //    а не два щелчка.
                //    ⚠ Правка живёт ТОЛЬКО в памяти: проба ничего не сохраняет,
                //    прежние числа возвращаются в `finally`, и поставочный
                //    `config\device\RC-103.xml` сверен с корневым после прогона —
                //    совпал. Портить чужой конфиг замером нельзя.
                dcm.DeviceConfigList.AddRange(saved);
                FWHMPeakDetectionMethodConfig broken =
                    (FWHMPeakDetectionMethodConfig)dcm.DeviceConfigList[0].PeakDetectionMethodConfig;
                double keepAt0 = broken.FWHM_AT_0, keepWidth = broken.Width_Fwhm;
                FwhmCalibration keepCurve = broken.FwhmCalibration;
                broken.FWHM_AT_0 = 40.0;
                broken.Width_Fwhm = 1.0;
                broken.FwhmCalibration = null;
                Console.WriteLine("--- у конфигурации «" + dcm.DeviceConfigList[0].Name
                                  + "» ПШПВ у нуля " + broken.FWHM_AT_0.ToString(CultureInfo.InvariantCulture)
                                  + " > ПШПВ на канале " + broken.Ch_Fwhm.ToString(CultureInfo.InvariantCulture)
                                  + " (" + broken.Width_Fwhm.ToString(CultureInfo.InvariantCulture)
                                  + "): умолчание не строится ---");
                Console.WriteLine("    проверка: FwhmCalibration.DefaultCalibration -> "
                                  + (FwhmCalibration.DefaultCalibration(broken, new PolynomialEnergyCalibration()) == null
                                     ? "null (кривая не растёт)" : "кривая построилась — ПЛЕЧО НЕ МЕРИТ"));
                Console.WriteLine();
                try
                {
                    rc |= Arm("УМОЛЧАНИЕ НЕ СТРОИТСЯ", "документ приложения", "SpecUtils", files);
                    rc |= Arm("УМОЛЧАНИЕ НЕ СТРОИТСЯ", "документ приложения", "N42", files);
                }
                finally
                {
                    broken.FWHM_AT_0 = keepAt0;
                    broken.Width_Fwhm = keepWidth;
                    broken.FwhmCalibration = keepCurve;
                }
            }
            finally
            {
                if (dcm.DeviceConfigList.Count == 0) dcm.DeviceConfigList.AddRange(saved);
            }

            Console.WriteLine("=== ИТОГ ===");
            foreach (string s in armTotals) Console.WriteLine("  " + s);
            return 0;
        }

        static readonly List<string> armTotals = new List<string>();

        /// <summary>
        /// Одно плечо: каждый файл каталога ввозится названной дверью в
        /// документ, созданный названным способом. Печатается приговор, а у
        /// отказа — ПЕРВЫЙ кадр следа внутри приложения: строка `A212` называет
        /// место падения по чтению исходника, и подтвердить его обязан след, а
        /// не чтение.
        /// </summary>
        static int Arm(string configState, string docWay, string door, string[] files)
        {
            string head = configState + " | " + docWay + " | дверь " + door;
            Console.WriteLine("=== " + head + " ===");

            int ok = 0, failed = 0, nullFwhm = 0, noDoc = 0;
            Dictionary<string, int> byKind = new Dictionary<string, int>();

            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                DocEnergySpectrum doc = null;
                string said = null;
                string frame = "";
                string fwhm = "?";

                TextWriter realErr = Console.Error;
                StringWriter caught = new StringWriter();
                Console.SetError(caught);
                try
                {
                    if (docWay == "документ приложения")
                    {
                        doc = DocumentManager.GetInstance().CreateDocument(name + ".xml");
                    }
                    else
                    {
                        doc = new DocEnergySpectrum();
                    }
                    if (doc == null)
                    {
                        noDoc++;
                        throw new InvalidOperationException("документ НЕ СОЗДАН (CreateDocument вернул null)");
                    }
                    // ⚠ ДВЕ КРИВЫЕ, А НЕ ОДНА, и падение зависит от первой.
                    //    `ResultData.FwhmCalibration` — модель РАЗРЕШЕНИЯ этого
                    //    спектра; `PeakDetectionMethodConfig.FwhmCalibration` —
                    //    та же величина в настройках поиска пиков. Отвечать надо
                    //    обе: если вторая жива при мёртвой первой, у сторожа есть
                    //    чем подставиться, если мертвы обе — подставляться нечем.
                    fwhm = doc.ActiveResultData == null
                           ? "нет активного спектра"
                           : "спектр:" + (doc.ActiveResultData.FwhmCalibration == null ? "null" : "есть")
                             + ", поиск пиков:" + PeakCfgFwhm(doc.ActiveResultData);
                    if (doc.ActiveResultData != null && doc.ActiveResultData.FwhmCalibration == null) nullFwhm++;

                    if (door == "SpecUtils")
                    {
                        DocumentManager.GetInstance().ImportDocumentSpecUtils(doc, f, 3600);
                    }
                    else
                    {
                        DocumentManager.GetInstance().ImportDocumentN42(doc, f);
                    }
                }
                catch (Exception ex)
                {
                    said = ex.GetType().Name + ": " + Flat(ex.Message);
                    frame = TopAppFrame(ex);
                    string kind = ex.GetType().Name + (frame.Length == 0 ? "" : " <- " + frame);
                    int had;
                    byKind.TryGetValue(kind, out had);
                    byKind[kind] = had + 1;
                }
                finally
                {
                    Console.SetError(realErr);
                }

                // ⛔ «НЕ УПАЛО» — ЕЩЁ НЕ «ВВЕЗЛО». Сторож null мог бы увести ввоз
                //    мимо спектров и молча отдать пустой документ, и по одному
                //    приговору это неотличимо от удачи. Поэтому в строке стоит
                //    СЛЕПОК: числа плеча со сломанным умолчанием обязаны совпасть
                //    с числами полного плеча той же двери.
                if (said == null)
                {
                    ok++;
                    Console.WriteLine("  " + name + " | ВВЕЗЁН | " + fwhm + " | " + Print(doc));
                }
                else
                {
                    failed++;
                    Console.WriteLine("  " + name + " | ОТКАЗ | " + fwhm + " | " + said
                                      + (frame.Length == 0 ? "" : "   [" + frame + "]"));
                }
            }

            StringBuilder kinds = new StringBuilder();
            foreach (KeyValuePair<string, int> kv in byKind)
            {
                if (kinds.Length > 0) kinds.Append("; ");
                kinds.Append(kv.Value).Append("× ").Append(kv.Key);
            }
            string total = head + " -> ВВЕЗЕНО " + ok + " / ОТКАЗ " + failed
                           + " (из " + files.Length + ")"
                           + ", без ПШПВ у документа: " + nullFwhm
                           + (noDoc > 0 ? ", документ не создан: " + noDoc : "")
                           + (kinds.Length == 0 ? "" : "   |   " + kinds);
            Console.WriteLine("  ИТОГ: " + total);
            Console.WriteLine();
            armTotals.Add(total);
            return 0;
        }

        /// <summary>
        /// Место падения — «файл:строка» САМОГО ГЛУБОКОГО исключения, а не
        /// внешнего.
        ///
        /// ⚠ Внешнее врёт про место нарочно: <c>ImportDocumentSpecUtils</c>
        /// ловит любую беду одним <c>catch</c> и без окон бросает свой
        /// <c>InvalidOperationException</c> — след у него начинается со строки
        /// ЭТОГО catch, и по нему место настоящего броска не найти вовсе.
        /// Строка `A212` называет место чтением исходника; подтвердить его
        /// обязан след, поэтому берётся внутреннее.
        /// </summary>
        static string TopAppFrame(Exception ex)
        {
            Exception e = ex;
            Exception deepest = ex;
            while (e != null) { deepest = e; e = e.InnerException; }

            string name = deepest.GetType().Name;
            string st = deepest.StackTrace;
            if (!string.IsNullOrEmpty(st))
            {
                foreach (string raw in st.Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.IndexOf("BecquerelMonitor", StringComparison.Ordinal) < 0) continue;
                    return name + " " + Flat(Tail(line));
                }
            }
            return name + " (следа нет)";
        }

        /// <summary>Есть ли кривая ПШПВ в настройках поиска пиков спектра.</summary>
        static string PeakCfgFwhm(ResultData rd)
        {
            FWHMPeakDetectionMethodConfig cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            if (cfg == null) return "настроек нет";
            return cfg.FwhmCalibration == null ? "null" : "есть";
        }

        /// <summary>Хвост кадра «…\Файл.cs:строка N» — без пути дерева.</summary>
        static string Tail(string frame)
        {
            int cut = frame.LastIndexOf('\\');
            return cut < 0 ? frame : frame.Substring(cut + 1);
        }

        // ==================================================================
        // ⛔ РЕСУРС ЧИТАЕТСЯ ИЗ СОБРАННОЙ СБОРКИ, А НЕ ИЗ `*.resx` (`A154`).
        //
        //    Между resx и человеком стоят компилятор ресурсов и сателлит
        //    `ru\BecquerelMonitor.resources.dll`: строка, заведённая в resx и не
        //    доехавшая до сборки, из файла выглядит целой. Спрашивается тот же
        //    ResourceManager, каким пользуется само приложение.
        //
        //    Оба контроля обязательны: без ПОЛОЖИТЕЛЬНОГО (заведомо живой
        //    ключ) ответ «ключа нет» значил бы «проба не нашла ресурсов
        //    вовсе», без ОТРИЦАТЕЛЬНОГО (заведомо несуществующий) — «проба
        //    отвечает „есть“ на что угодно».
        // ==================================================================
        static int Res()
        {
            Assembly app = typeof(DocumentManager).Assembly;
            ResourceManager rm = new ResourceManager("BecquerelMonitor.Properties.Resources", app);
            string[] keys =
            {
                "PeakFitChiTableScoreColumn",       // `A154`
                "ERRSkippedMeasurementClassN42",    // `A160`
                "ERRUnreadableStartDateTimeN42",    // `A157`, `A171`
                "PeakFitChiTableChi2PerNdpColumn",  // положительный контроль
                "ZZZ_NoSuchKeyAtAll"                // отрицательный контроль
            };
            string[] cultures = { "en-US", "ru-RU" };

            Console.WriteLine("=== РЕСУРСЫ СОБРАННОЙ СБОРКИ ===");
            Console.WriteLine("  " + app.Location);
            foreach (string key in keys)
            {
                foreach (string c in cultures)
                {
                    string value;
                    try
                    {
                        value = rm.GetString(key, new CultureInfo(c));
                    }
                    catch (Exception ex)
                    {
                        value = null;
                        Console.WriteLine("  " + key + " | " + c + " | ОТКАЗ ЧТЕНИЯ: "
                                          + ex.GetType().Name);
                        continue;
                    }
                    Console.WriteLine("  " + key + " | " + c + " | "
                                      + (value == null ? "(КЛЮЧА НЕТ)" : "«" + Flat(value) + "»"));
                }
            }
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
                      // `A177`: ТРЕТЬЕ ПОЛЕ ВРЕМЕНИ. Соседние ввозы (GBS,
                      //   SpecUtils) ставят EndTime = StartTime + время набора,
                      //   а ввоз N42 не ставил его ВООБЩЕ — оставалось
                      //   умолчание ResultData, то есть «сейчас». Печатается не
                      //   сама дата, а СОШЛОСЬ ЛИ оно с началом плюс полное
                      //   время: иначе два плеча различались бы по построению.
                      .Append(", EndTime ").Append(EndTimeVerdict(rd, es))
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

        /// <summary>
        /// Приговор полю <c>EndTime</c> (`A177`).
        ///
        /// ⛔ Печатается ПРИГОВОР, а не дата: у двух плеч «сейчас» разное по
        /// построению, и посимвольная сверка слепков сломалась бы сама собой.
        /// Три исхода и все три различимы:
        ///   «НЕ ЗАПОЛНЕНО (сейчас)» — поле осталось умолчанием ResultData;
        ///   «= начало + изм» — ровно соглашение соседей (GBS, SpecUtils);
        ///   «РАСХОДИТСЯ на N с» — заполнено, но не тем.
        /// </summary>
        static string EndTimeVerdict(ResultData rd, EnergySpectrum es)
        {
            DateTime end = rd.EndTime;
            double dueSeconds = es == null ? 0.0 : es.MeasurementTime;
            DateTime due = rd.StartTime.AddSeconds(dueSeconds);
            double off = Math.Abs((end - due).TotalSeconds);
            // ⛔ «СЕЙЧАС» СУДИТСЯ ПЕРВЫМ, И ЭТО НЕ ПОРЯДОК ПО ВКУСУ. У НЕ
            //    тронутого разбором документа StartTime = «сейчас», время
            //    набора 0 и EndTime = «сейчас» — то есть равенство
            //    «начало + изм» выполняется САМО СОБОЙ, и проверка, начатая с
            //    него, отвечала бы «сошлось» на пустой документ. Поймано
            //    замером 05.09.2026 на отказавших входах.
            if (Math.Abs((DateTime.Now - end).TotalMinutes) < 1.0)
            {
                return "НЕ ЗАПОЛНЕНО (сейчас)";
            }
            if (off < 0.001)
            {
                return "= начало + изм";
            }
            return "РАСХОДИТСЯ на " + off.ToString("0.###", CultureInfo.InvariantCulture) + " с";
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

            // `A158`: ОТРИЦАТЕЛЬНЫЙ ОТСЧЁТ.
            //
            // ⛔ ЭТО НЕ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ, И ПРЕЖНИЙ ТЕКСТ ЗДЕСЬ ВРАЛ.
            //    Он утверждал, что «у 41 культуры из 890 знак минуса НЕ дефис,
            //    например у sv-SE это U+2212», — и это число снято в PowerShell 7,
            //    то есть в .NET 8 с ICU. ПРИЛОЖЕНИЕ ЖИВЁТ В .NET Framework 4.8 с
            //    NLS, и там таблица другая: перемерено 05.09.2026 пробой на самом
            //    Framework — из 915 культур знак минуса НЕ дефис ни у ОДНОЙ, а
            //    int.Parse("-3") без культуры не отказывает и не даёт иного
            //    значения ни разу. Цена пяти мест int.Parse в этой среде РОВНО
            //    НОЛЬ, и положительного контроля им не бывает вовсе; настоящая
            //    цена есть только у double.Parse (413 культур из 915), и живёт
            //    она в EnergyCalibration.CoefficientsToArray, у которого
            //    читателей в дереве НОЛЬ.
            //
            //    Вход поэтому оставлен, но роль у него другая: он показывает, что
            //    отрицательный отсчёт из файла оба плеча читают ОДИНАКОВО, то есть
            //    правка ничего не сломала там, где могла бы.
            Write(Path.Combine(outDir, "case17_negcount.n42"),
                  Multi("case17", Meas("M1", "Foreground", "2026-09-05T12:00:00Z", NegCounts())));

            // ==============================================================
            // ⛔ ТРЕТИЙ РАЗБОР ФАЙЛА — `N42InstrumentData` (N42-2006, корень
            //    «N42InstrumentData»). До 05.09.2026 у пробы НЕ БЫЛО НИ ОДНОГО
            //    входа этого вида, а правки `A156` (время начала в оба поля) и
            //    `A158` (отсчёты инвариантной культурой) его тоже касаются: обе
            //    держались на исходнике и на IL, поведением их не мерил никто.
            //    Разборов у приложения три (DocumentManager.GetN42Type), и
            //    измеряться должны все три.
            // ==============================================================
            Write(Path.Combine(outDir, "case18_n42_2006.n42"), N42_2006("2026-09-05T12:00:00Z", 64));

            // ⚠ ТОТ ЖЕ ФАЙЛ С НЕЧИТАЕМОЙ ДАТОЙ. Здесь `A157` НЕ РАБОТАЕТ и
            //    работать не может: этот разбор зовёт XmlConvert.ToDateTime без
            //    всякой обёртки, и негодная запись роняет ввоз ЦЕЛИКОМ — тогда как
            //    разбор 2012 года в том же файле ввозит и говорит вслух. Вход
            //    заведён затем, чтобы это расхождение соглашений было ИЗМЕРЕНО, а
            //    не осталось замечанием: приговор ОТКАЗ обязан быть ОДИНАКОВ на
            //    обоих плечах — строки на изменение списка ввозимого здесь нет.
            Write(Path.Combine(outDir, "case19_2006_baddate.n42"), N42_2006("04/05/44 10:07:57 ص", 64));

            // ⚠ ВХОД, У КОТОРОГО КАНАЛОВ БОЛЬШЕ, ЧЕМ У ДОКУМЕНТА, — тот же вид,
            //    что `case9_rad_over` (`A152`), но для ТРЕТЬЕГО разбора. `A152`
            //    починила только разбор RadiologicalInstrumentData; здесь число
            //    каналов ФАЙЛА в спектр по-прежнему не записывается, и цикл
            //    выходит за конец массива документа (8192 у пустого
            //    DocEnergySpectrum, а ImportSpectrumWithEmptyConfig у агентской
            //    сборки False). Строки на починку НЕТ — вход заведён затем, чтобы
            //    остаток был ИЗМЕРЕН, а не записан замечанием; приговор ОТКАЗ
            //    обязан быть одинаков на обоих плечах.
            Write(Path.Combine(outDir, "case20_2006_over.n42"), N42_2006("2026-09-05T12:00:00Z", 9000));

            // ==============================================================
            // `A172`: ЧИСЛО КАНАЛОВ ФАЙЛА У РАЗБОРА 2006 ГОДА — контроли.
            //
            // ⛔ Одного входа на 9000 каналов мало: он показывает только, что
            //    падения больше нет. Проверяется РАВЕНСТВО числа каналов
            //    документа числу каналов файла, а оно обязано держаться и НИЖЕ
            //    умолчания документа (8192), и ВЫШЕ него. Вход на 1024 канала
            //    до правки не падал вовсе — он ложился в документ на 8192, и
            //    каналы 1024…8191 оставались нулями молча: та же беда, что
            //    закрыта `A152` у разбора RadiologicalInstrumentData, только
            //    без исключения и потому невидимая.
            // ==============================================================
            Write(Path.Combine(outDir, "case21_2006_1024.n42"), N42_2006("2026-09-05T12:00:00Z", 1024));
            Write(Path.Combine(outDir, "case22_2006_16384.n42"), N42_2006("2026-09-05T12:00:00Z", 16384));

            // ⛔ ОБЪЯВЛЕННОЕ ЧИСЛО КАНАЛОВ НЕ РАВНО ФАКТИЧЕСКОМУ — только у
            //    разбора RadiologicalInstrumentData, потому что только там оно
            //    ОБЪЯВЛЕНО (атрибут NumberOfChannels). У формата 2006 года
            //    (N42InstrumentData) такого поля НЕТ вовсе: число каналов там
            //    равно числу разделённых пробелом чисел, и разойтись ему не с
            //    чем — это свойство формата, а не недосмотр разбора.
            //    Вход заведён положительным контролем к `A172`: проверка «кан
            //    документа = кан файла» обязана на нём ОТКАЗАТЬ, а не пройти.
            Write(Path.Combine(outDir, "case23_rad_declared.n42"), RadMismatch(9000, 64));

            // `A177`: ФАЙЛ БЕЗ ВРЕМЕНИ НАЧАЛА. Положительный контроль к EndTime:
            //   там, где начала нет, EndTime не может быть «начало + изм» с
            //   настоящей датой, и проверка обязана это различать, а не
            //   отвечать «сошлось» на что угодно.
            Write(Path.Combine(outDir, "case24_nostart.n42"),
                  Multi("case24", Meas("M1", "Foreground", "", Counts(0))));

            // ==============================================================
            // ⛔ ЖИВОЕ И ПОЛНОЕ ВРЕМЯ У РАЗБОРА RadiologicalInstrumentData —
            //    ТРИ ВХОДА, И БЕЗ ВСЕХ ТРЁХ ПРОВЕРКА НИЧЕГО НЕ МЕРИТ.
            //
            //    Дефект: живое время файла клалось в поле ПОЛНОГО, а поле
            //    живого оставалось нулём (слепок `case8_rad_many`: «изм 295,
            //    живое 0»). Проверка «живое стало 295» на одном этом входе
            //    прошла бы и у правки, которая просто ПРИСВАИВАЕТ обоим полям
            //    одно и то же число, — то есть у правки, которая ничего не
            //    разводит. Поэтому входа три:
            //
            //    case25 — живое и полное РАЗНЫЕ (295 и 300). Правка, которая
            //             их не различает, здесь ОБЯЗАНА провалиться.
            //    case26 — живого времени НЕТ ВОВСЕ. Правка, которая его
            //             выдумывает (например, берёт полное), здесь ОБЯЗАНА
            //             провалиться: из ничего время не берётся.
            //    case27 — то же полное время, записанное как xs:duration
            //             («PT300S»), а не голыми секундами. Извод Alpha Hound
            //             пишет секунды, спецификация N42-2006 требует
            //             xs:duration, и читатель обязан принять обе записи —
            //             иначе «починка» работает только на сочинённом входе.
            // ==============================================================
            Write(Path.Combine(outDir, "case25_rad_realtime.n42"), RadWithRealTime(1024, "300"));
            Write(Path.Combine(outDir, "case26_rad_nolive.n42"), RadWithoutLiveTime(1024));
            Write(Path.Combine(outDir, "case27_rad_realtime_iso.n42"), RadWithRealTime(1024, "PT300S"));

            // ⛔ ЭНЕРГИЙ МЕНЬШЕ, ЧЕМ ОТСЧЁТОВ, — ВТОРАЯ ПОЛОВИНА ТОЙ ЖЕ БЕДЫ,
            //    что и `case23`. Там короток список ОТСЧЁТОВ, здесь — список
            //    ЭНЕРГИЙ, а цикл разбора ходит по обоим одним и тем же
            //    индексом. Без этого входа проверка «за край массива не
            //    выходим» мерила бы ровно одну из двух дверей.
            Write(Path.Combine(outDir, "case28_rad_shortener.n42"), RadShortEnergies(64, 10));

            // ⛔ ПОЛУРАЗОБРАННЫЙ ДОКУМЕНТ У РАЗБОРА 2006 ГОДА. Файл со ВСЕМ
            //    прочитанным спектром и ПУСТЫМ списком коэффициентов: отсчёты
            //    ложатся в документ, и только потом бросается отказ (`A140`).
            //    Снаружи это тот же вид, что `case23` у третьего разбора, —
            //    отказ, после которого в документе лежат числа отказавшего
            //    файла. Вход заведён затем, чтобы остаток был ИЗМЕРЕН.
            Write(Path.Combine(outDir, "case29_2006_nocoeff.n42"),
                  N42_2006("2026-09-05T12:00:00Z", 64).Replace("3.5 0.5", ""));

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

        /// <summary>
        /// Файл N42-2006 с корнем <c>N42InstrumentData</c> — третий разбор
        /// приложения (<c>Util.ImportFromN42(N42InstrumentData, …)</c>).
        ///
        /// Пространство имён взято у самой модели (<c>N42InstrumentData.Ns</c>),
        /// а не переписано руками: разбор идёт XmlSerializer-ом, и чужое
        /// пространство дало бы пустые поля вместо отказа. Времена — xs:duration
        /// (их читает <c>N42Seconds</c>), шкала — двучлен.
        ///
        /// ⛔ НАКЛОН ШКАЛЫ 0.5 кэВ/канал, А НЕ 12.5, И ЭТО НЕ ВКУС. Этот разбор
        /// НЕ записывает число каналов файла в спектр, поэтому
        /// <c>CheckCalibration</c> зовётся с 8192 каналами документа, а у неё
        /// есть верхний предел энергии: <c>prevEnrg >= 100000</c> — отказ. При
        /// 12.5 кэВ/канал шкала переваливает за него на 8000-м канале, и вход
        /// отказывал по ПОСТОРОННЕЙ причине, ничего не измеряя (поймано замером
        /// 05.09.2026, первая же попытка).
        /// </summary>
        static string N42_2006(string startTime, int channels)
        {
            StringBuilder counts = new StringBuilder();
            for (int i = 0; i < channels; i++)
            {
                if (i > 0) counts.Append(' ');
                counts.Append((i % 7) + 1);
            }
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<N42InstrumentData xmlns=\"" + BecquerelMonitor.N42.N42InstrumentData.Ns + "\">\r\n"
                 + "  <Measurement>\r\n"
                 + "    <InstrumentInformation>\r\n"
                 + "      <InstrumentType>Spectrometer</InstrumentType>\r\n"
                 + "      <Manufacturer>PROBE</Manufacturer>\r\n"
                 + "      <InstrumentModel>A156</InstrumentModel>\r\n"
                 + "      <InstrumentID>2006</InstrumentID>\r\n"
                 + "      <ProbeType>NaI</ProbeType>\r\n"
                 + "    </InstrumentInformation>\r\n"
                 + "    <Spectrum Type=\"PHA\">\r\n"
                 + "      <StartTime>" + startTime + "</StartTime>\r\n"
                 + "      <RealTime>PT300S</RealTime>\r\n"
                 + "      <LiveTime>PT295S</LiveTime>\r\n"
                 + "      <ChannelData>" + counts + "</ChannelData>\r\n"
                 + "    </Spectrum>\r\n"
                 + "  </Measurement>\r\n"
                 + "  <Calibration Type=\"Energy\">\r\n"
                 + "    <Equation Model=\"Polynomial\">\r\n"
                 + "      <Coefficients>3.5 0.5</Coefficients>\r\n"
                 + "    </Equation>\r\n"
                 + "  </Calibration>\r\n"
                 + "</N42InstrumentData>\r\n";
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
        /// <summary>
        /// Вход <c>RadiologicalInstrumentData</c>, у которого ОБЪЯВЛЕННОЕ число
        /// каналов не равно числу записанных отсчётов (`A172`, контроль).
        ///
        /// Разбор берёт длину массива у атрибута <c>NumberOfChannels</c>, а
        /// отсчёты и энергии читает по индексу, — то есть при declared &gt; actual
        /// он ОБЯЗАН отказать, а не молча обрезать или дописать нулями.
        /// </summary>
        static string RadMismatch(int declared, int actual)
        {
            string full = Rad(actual);
            return full.Replace("NumberOfChannels=\"" + actual + "\"",
                                "NumberOfChannels=\"" + declared + "\"");
        }

        /// <summary>
        /// Тот же вход, но с ПОЛНЫМ временем набора рядом с живым.
        ///
        /// ⚠ Подмена строкой, а не второй сборкой файла, — нарочно: так
        /// гарантировано, что от <c>Rad(n)</c> вход отличается РОВНО одним
        /// элементом, и всякая разница в слепке приписывается ему одному.
        /// ⛔ Подмена ПРОВЕРЯЕТСЯ: <c>Replace</c>, не нашедший образца,
        /// молча вернул бы исходную строку, и вход мерил бы не то, что назван.
        /// </summary>
        static string RadWithRealTime(int n, string realTime)
        {
            string full = Rad(n);
            string src = "        <LiveTime>295</LiveTime>\r\n";
            if (!full.Contains(src)) throw new Exception("RadWithRealTime: образец не найден");
            return full.Replace(src, src + "        <RealTime>" + realTime + "</RealTime>\r\n");
        }

        /// <summary>
        /// Тот же вход БЕЗ элемента живого времени вовсе (контроль «не выдумывать»).
        /// </summary>
        static string RadWithoutLiveTime(int n)
        {
            string full = Rad(n);
            string src = "        <LiveTime>295</LiveTime>\r\n";
            if (!full.Contains(src)) throw new Exception("RadWithoutLiveTime: образец не найден");
            return full.Replace(src, "");
        }

        /// <summary>
        /// Тот же вход, у которого список ЭНЕРГИЙ короче списка отсчётов.
        /// </summary>
        static string RadShortEnergies(int n, int keep)
        {
            StringBuilder energies = new StringBuilder();
            for (int i = 0; i < keep; i++)
            {
                double e = 5.0 + 0.5 * i + 1e-6 * i * i + 1e-11 * i * i * i + 1e-14 * i * i * i * i;
                if (i > 0) energies.Append(' ');
                energies.Append(e.ToString("0.######", CultureInfo.InvariantCulture));
            }
            string full = Rad(n);
            int a = full.IndexOf("<ChannelEnergies>", StringComparison.Ordinal);
            int b = full.IndexOf("</ChannelEnergies>", StringComparison.Ordinal);
            if (a < 0 || b < 0) throw new Exception("RadShortEnergies: образец не найден");
            a += "<ChannelEnergies>".Length;
            return full.Substring(0, a) + energies + full.Substring(b);
        }

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

            // ⛔ `A158`: ЧЕТЫРЕ МЕСТА ИЗ ШЕСТИ ЖИВУТ НЕ В `Util`, И ДО 05.09.2026
            //    АРТЕФАКТА У НИХ НЕ БЫЛО ВОВСЕ. Обход выше касается только трёх
            //    разборов `Util.ImportFromN42`, то есть двух мест; три `int.Parse`
            //    в `ChannelData.SpectrumToArray` и один `double.Parse` в
            //    `EnergyCalibration.CoefficientsToArray` держались на исходнике.
            //    ⚠ У второго читателей в дереве НОЛЬ — поведением он не измерим
            //    в принципе, и IL здесь единственная возможная приёмка.
            WalkNamed(app, "BecquerelMonitor.N42.ChannelData", "SpectrumToArray");
            WalkNamed(app, "BecquerelMonitor.N42.EnergyCalibration", "CoefficientsToArray");
            return 0;
        }

        static void WalkNamed(Assembly app, string typeName, string methodName)
        {
            string shortName = typeName.Substring(typeName.LastIndexOf('.') + 1);
            Console.WriteLine("=== IL " + shortName + "." + methodName + "() ===");
            Type t = app.GetType(typeName);
            if (t == null) { Console.WriteLine("  ⛔ типа нет"); Console.WriteLine(); return; }
            MethodInfo m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (m == null) { Console.WriteLine("  ⛔ метода нет"); Console.WriteLine(); return; }
            Walk(m);
            Console.WriteLine();
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
