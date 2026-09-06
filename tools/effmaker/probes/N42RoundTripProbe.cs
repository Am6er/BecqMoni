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
    ///   --mode=onevoice — РОВНО ОДИН ГОЛОС НА СОБЫТИЕ у восьмого места `A240`
    ///                    (полоса G11, 06.09.2026): `CheckDocument` строит
    ///                    умолчание кривой разрешения через `CreateDocument`,
    ///                    `OpenDocument` и `LoadBackgroundSpectrum`; при
    ///                    умолчании, которое НЕ строится, каждое из трёх
    ///                    событий считается по строкам `BecqMoni:` в потоке
    ///                    ошибок. Ожидание: создание — 1, открытие — 1, фон — 0
    ///                    (у кривой фона читателя нет: фон вычитается по
    ///                    отсчётам); исправная конфигурация — 0/0/0
    ///                    (положительный контроль ложной тревоги).
    ///                    ⛔ ОСТАТОК `A240` (полоса F66, 06.09.2026): к тому же
    ///                    `CheckDocument` ведут ещё ДВЕ двери ввоза, у которых
    ///                    читателя причины не было, — `ImportDocumentAtomSpectra`
    ///                    и `ImportCsvEnergyToDocument`. Плечо доводит события до
    ///                    ПЯТИ: входы сочиняются пробой (файл Atom Spectra
    ///                    «FORMAT: 3» и CSV «Energy,Count #…» с тем же числом
    ///                    каналов, что у документа, — иначе ввоз сбрасывает
    ///                    настройку спектра и говорит СВОЁ, не относящееся к
    ///                    кривой, слово) и ввозятся в ОТКРЫТЫЙ документ, как это
    ///                    делает пункт меню. Ожидание после правки: ввоз Atom
    ///                    Spectra — 1, ввоз CSV — 1; исправная конфигурация —
    ///                    0/0/0/0/0.
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
            if (mode == "origin") return Origin();
            if (mode == "onevoice") return OneVoice();
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
                // ⛔ ВХОД, НА КОТОРОМ ОБЕ ДВЕРИ ДОХОДЯТ ДО ОДНОГО СОСТОЯНИЯ
                //    (`A234`, 05.09.2026). Двенадцать корпусных .n42 — это
                //    спецификация 2012 года, и её разбор заводит СВОИ
                //    `ResultData` (`N42\Util.cs:876`) с умолчанием настроек
                //    поиска пиков; после такой двери кривая есть ВСЕГДА, и
                //    молчание двери N42 на корпусе — не дефект, а ОТСУТСТВИЕ
                //    состояния. ⚠ Отсюда следует, что посылка `A234` («дверь
                //    N42 ввозит 12 из 12 с FwhmCalibration = null») была про
                //    ЗАГОТОВКУ документа, а не про то, что дверь оставляет
                //    человеку: замер ДО и ПОСЛЕ разводит эти два числа.
                //    Разбор 2006 года пишет прочитанное ПРЯМО в
                //    `doc.ActiveResultData` (о том же говорит `catch` самой
                //    двери), то есть кривую документа оставляет как есть.
                //    Один файл, обе двери, одно состояние — только так
                //    сравнение «сказали ли они ОДНО И ТО ЖЕ» вообще имеет
                //    предмет.
                string spec2006 = Path.Combine(Path.GetTempPath(), "a234_2006");
                Directory.CreateDirectory(spec2006);
                File.WriteAllText(Path.Combine(spec2006, "a234_2006.n42"),
                                  N42_2006("2026-09-05T12:00:00Z", 64), new UTF8Encoding(false));
                string[] one = Directory.GetFiles(spec2006, "*.n42");
                try
                {
                    rc |= Arm("УМОЛЧАНИЕ НЕ СТРОИТСЯ", "документ приложения", "SpecUtils", files);
                    rc |= Arm("УМОЛЧАНИЕ НЕ СТРОИТСЯ", "документ приложения", "N42", files);
                    rc |= Arm("УМОЛЧАНИЕ НЕ СТРОИТСЯ, файл 2006", "документ приложения", "SpecUtils", one);
                    rc |= Arm("УМОЛЧАНИЕ НЕ СТРОИТСЯ, файл 2006", "документ приложения", "N42", one);
                }
                finally
                {
                    broken.FWHM_AT_0 = keepAt0;
                    broken.Width_Fwhm = keepWidth;
                    broken.FwhmCalibration = keepCurve;
                }

                // ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ЛОЖНОЙ ТРЕВОГИ: ТОТ ЖЕ ФАЙЛ 2006
                //    года при ИСПРАВНОЙ конфигурации. Умолчание строится,
                //    кривая у документа есть, и обе двери обязаны молчать.
                //    Без этого плеча «дверь сказала слово» ничего не значит:
                //    дверь, говорящая всегда, тоже даёт единицу.
                rc |= Arm("КОНФИГУРАЦИЯ ИСПРАВНА, файл 2006", "документ приложения", "SpecUtils", one);
                rc |= Arm("КОНФИГУРАЦИЯ ИСПРАВНА, файл 2006", "документ приложения", "N42", one);
            }
            finally
            {
                if (dcm.DeviceConfigList.Count == 0) dcm.DeviceConfigList.AddRange(saved);
            }

            Console.WriteLine("=== ИТОГ ===");
            foreach (string s in armTotals) Console.WriteLine("  " + s);
            return 0;
        }

        // ==================================================================
        // ⛔ ОТКУДА У ДОКУМЕНТА ПОСЛЕ ДВЕРИ НАСТРОЙКИ ПРИБОРА (`A239`, полоса G8,
        //    06.09.2026). Строка `A239` знала только, что после двери N42 кривая
        //    разрешения ЕСТЬ там, где по настройкам прибора документа её быть не
        //    может, и ПОДОЗРЕВАЛА `new ResultData()` в разборе 2012 года.
        //    Подозрение — не замер. Здесь по каждому спектру документа ПОСЛЕ
        //    двери печатается, чьи у него настройки: конфигурация прибора (тот
        //    же объект, что был у документа до двери, или свежий
        //    `DeviceConfigInfo`), ссылка на неё, конфигурация ROI, три числа
        //    настроек поиска пиков (FWHM_AT_0, Ch_Fwhm, Width_Fwhm) — и кривая
        //    разрешения по её опорным точкам. Сравнение — с документом ДО двери
        //    и со ВСТРОЕННЫМИ умолчаниями `new FWHMPeakDetectionMethodConfig()`.
        //
        //    ⛔ Обе двери на одном списке: приговор «настройки чужие» у одной
        //    двери без той же мерки у второй ничего не значил бы — двери уже
        //    трижды расходились на одном файле (`A160`, `A175`, `A216`).
        //
        //    Плечо со СЛОМАННЫМ умолчанием (то же, что в --mode=noconfig)
        //    показывает, чья кривая появляется там, где у прибора документа её
        //    построить нельзя: если после двери кривая есть и её опорные точки
        //    — встроенные 15/3756/103, значит человек получил модель разрешения
        //    выдуманного прибора.
        // ==================================================================
        static int Origin()
        {
            if (outDir == null)
            {
                Console.Error.WriteLine("нужен --out=<каталог с файлами спектров>");
                return 2;
            }
            string[] files = Directory.GetFiles(outDir, "*.n42");
            Array.Sort(files, StringComparer.Ordinal);

            DeviceConfigManager dcm = DeviceConfigManager.GetInstance();
            if (dcm.DeviceConfigList.Count == 0)
            {
                Console.Error.WriteLine("конфигураций приборов не загружено — мерить не с чем");
                return 2;
            }
            FWHMPeakDetectionMethodConfig builtin = new FWHMPeakDetectionMethodConfig();
            Console.WriteLine("=== ЧЬИ НАСТРОЙКИ У ДОКУМЕНТА ПОСЛЕ ДВЕРИ (`A239`) ===");
            Console.WriteLine("  каталог: " + Path.GetFullPath(outDir));
            Console.WriteLine("  файлов: " + files.Length);
            Console.WriteLine("  конфигурация документа: «" + dcm.DeviceConfigList[0].Name + "», настройки поиска пиков "
                              + Cfg3((FWHMPeakDetectionMethodConfig)dcm.DeviceConfigList[0].PeakDetectionMethodConfig));
            Console.WriteLine("  встроенные умолчания new FWHMPeakDetectionMethodConfig(): " + Cfg3(builtin));
            Console.WriteLine();

            int rc = 0;
            rc |= OriginArm("КОНФИГУРАЦИЯ ИСПРАВНА", "SpecUtils", files, builtin);
            rc |= OriginArm("КОНФИГУРАЦИЯ ИСПРАВНА", "N42", files, builtin);

            // Умолчание по прибору документа НЕ строится — то же плечо, что в
            // --mode=noconfig, правка живёт только в памяти и возвращается в finally.
            FWHMPeakDetectionMethodConfig broken =
                (FWHMPeakDetectionMethodConfig)dcm.DeviceConfigList[0].PeakDetectionMethodConfig;
            double keepAt0 = broken.FWHM_AT_0, keepWidth = broken.Width_Fwhm;
            FwhmCalibration keepCurve = broken.FwhmCalibration;
            broken.FWHM_AT_0 = 40.0;
            broken.Width_Fwhm = 1.0;
            broken.FwhmCalibration = null;
            try
            {
                Console.WriteLine("--- у конфигурации «" + dcm.DeviceConfigList[0].Name + "» умолчание не строится: "
                                  + Cfg3(broken) + " ---");
                Console.WriteLine();
                rc |= OriginArm("УМОЛЧАНИЕ НЕ СТРОИТСЯ", "SpecUtils", files, builtin);
                rc |= OriginArm("УМОЛЧАНИЕ НЕ СТРОИТСЯ", "N42", files, builtin);
            }
            finally
            {
                broken.FWHM_AT_0 = keepAt0;
                broken.Width_Fwhm = keepWidth;
                broken.FwhmCalibration = keepCurve;
            }

            Console.WriteLine("=== ИТОГ ===");
            foreach (string s in armTotals) Console.WriteLine("  " + s);
            return rc;
        }

        // ==================================================================
        // ⛔ ВОСЬМОЕ МЕСТО `A240` — `DocumentManager.CheckDocument` (полоса G11,
        //    06.09.2026). Семь мест-читателей причины `A235` чинит полоса F62;
        //    здесь меряется то, куда причина из `CheckDocument` уходит и
        //    сколько раз человек её слышит. `CheckDocument` — точка служебная
        //    (`bool`, зовётся семью методами), голоса у неё быть не должно:
        //    голос — у двери, которая привела к нему (`ReportMissingFwhmCalibration`,
        //    ~~`A234`~~/F54), и ровно один на событие.
        //
        //    Три пути к `CheckDocument`, у которых состояние «кривая не
        //    строится» достижимо без ввоза: `CreateDocument` (пункт «Новый»),
        //    `OpenDocument` (сохранённый документ без кривой при той же
        //    конфигурации) и `LoadBackgroundSpectrum` (файл фона). Каждое
        //    событие считается ОТДЕЛЬНО, строками `BecqMoni:` в потоке ошибок
        //    (`AppUi.Report` без окон пишет ровно одну строку на голос).
        //    Двери ввоза (`ImportDocumentN42`, `ImportDocumentAtomSpectra`)
        //    сюда не входят — они меряются `--mode=noconfig`.
        //
        //    Ожидание после правки: создание 1, открытие 1, фон 0; исправная
        //    конфигурация — 0/0/0. Замер ДО и ПОСЛЕ обязан дать ОДНИ числа:
        //    полоса не добавляет и не убирает голоса, она ведёт причину.
        // ==================================================================
        static int OneVoice()
        {
            DeviceConfigManager dcm = DeviceConfigManager.GetInstance();
            if (dcm.DeviceConfigList.Count == 0)
            {
                Console.Error.WriteLine("конфигураций приборов не загружено — мерить не с чем");
                return 2;
            }
            string dir = Path.Combine(Path.GetTempPath(), "g11_onevoice");
            Directory.CreateDirectory(dir);
            Console.WriteLine("=== РОВНО ОДИН ГОЛОС НА СОБЫТИЕ У CheckDocument (`A240`, восьмое место) ===");
            Console.WriteLine("  конфигурация документа: «" + dcm.DeviceConfigList[0].Name + "», настройки поиска пиков "
                              + Cfg3((FWHMPeakDetectionMethodConfig)dcm.DeviceConfigList[0].PeakDetectionMethodConfig));
            Console.WriteLine("  каталог документов: " + dir);
            Console.WriteLine();

            // Прогрев: первое обращение к менеджерам пишет в поток ошибок свои
            // строки (библиотека нуклидов, главный конфиг, каталог ROI) — это
            // не голоса двери. Измерено на плече ДО: без прогрева «создание 3».
            {
                TextWriter warmErr = Console.Error;
                Console.SetError(new StringWriter());
                try
                {
                    DocEnergySpectrum warm = DocumentManager.GetInstance().CreateDocument(Path.Combine(dir, "g11_warm.xml"));
                    if (warm != null) DocumentManager.GetInstance().CloseDocument(warm);
                }
                finally { Console.SetError(warmErr); }
            }

            int rc = 0;
            rc |= OneVoiceArm("КОНФИГУРАЦИЯ ИСПРАВНА", Path.Combine(dir, "g11_ok.xml"), 0, 0, 0, 0, 0);

            FWHMPeakDetectionMethodConfig broken =
                (FWHMPeakDetectionMethodConfig)dcm.DeviceConfigList[0].PeakDetectionMethodConfig;
            double keepAt0 = broken.FWHM_AT_0, keepWidth = broken.Width_Fwhm;
            FwhmCalibration keepCurve = broken.FwhmCalibration;
            broken.FWHM_AT_0 = 40.0;
            broken.Width_Fwhm = 1.0;
            broken.FwhmCalibration = null;
            try
            {
                Console.WriteLine("--- у конфигурации «" + dcm.DeviceConfigList[0].Name + "» умолчание не строится: "
                                  + Cfg3(broken) + "; проверка DefaultCalibration -> "
                                  + (FwhmCalibration.DefaultCalibration(broken, new PolynomialEnergyCalibration()) == null
                                     ? "null" : "кривая построилась — ПЛЕЧО НЕ МЕРИТ") + " ---");
                Console.WriteLine();
                rc |= OneVoiceArm("УМОЛЧАНИЕ НЕ СТРОИТСЯ", Path.Combine(dir, "g11_broken.xml"), 1, 1, 0, 1, 1);
            }
            finally
            {
                broken.FWHM_AT_0 = keepAt0;
                broken.Width_Fwhm = keepWidth;
                broken.FwhmCalibration = keepCurve;
            }

            rc |= ResetWipesCurve(dir);
            rc |= QuietDoors(dir);

            Console.WriteLine("=== ИТОГ ===");
            foreach (string s in armTotals) Console.WriteLine("  " + s);
            return rc;
        }

        /// <summary>
        /// ⛔ `A260` (полоса F71, 06.09.2026), решение Amber вопросником,
        /// дословно: «СНЯТЬ КРИВУЮ ВМЕСТЕ С ПРИБОРОМ».
        ///
        /// `DocumentManager.ResetSpectrumConfig` — сброс настройки спектра,
        /// которым двери ввоза встречают файл с ДРУГИМ числом каналов (и любой
        /// файл при настройке «ввозить с пустой конфигурацией»), — стирал фон,
        /// ROI и ПРИБОР, но кривую разрешения и настройки поиска пиков оставлял
        /// (замер полосы F66). Теперь снимает и их, а следом звучит УЖЕ ГОТОВЫЙ
        /// голос «кривой нет» (~~`A234`~~, `ReportMissingFwhmCalibration`).
        ///
        /// ⛔ ТРИ ПЛЕЧА, И ТРЕТЬЕ — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Без него «кривой
        /// после ввоза нет» значило бы всего лишь «проба умеет её обнулять»:
        ///   1. сброс по ЧИСЛУ КАНАЛОВ (файл другой длины, настройка снята);
        ///   2. сброс по НАСТРОЙКЕ «ввозить с пустой конфигурацией» (длина та
        ///      же) — второй путь к тому же сбросу, и он тоже обязан снимать;
        ///   3. СБРОСА НЕТ (длина та же, настройка снята) — ни кривая, ни
        ///      настройки поиска, ни фон, ни ROI, ни прибор не двигаются НИ НА
        ///      ОДНО ПОЛЕ, и голоса «кривой нет» не звучит.
        ///
        /// ⚠ Голос считается ИМЕННО ТОТ, а не всякий: у первого плеча рядом
        /// звучит предупреждение о числе каналов, и счёт «строк BecqMoni:»
        /// смешал бы их. Примета берётся из САМОГО ресурса
        /// `ERRNoFwhmCalibrationImport` собранной сборки — кусок текста между
        /// подстановками {2} и {3}, — поэтому не зависит ни от культуры, ни от
        /// правки текста.
        ///
        /// ⚠ Печатается ПОЛНАЯ опись спектра до и после, а не одна кривая:
        /// строка `A260` называла кривую и настройки поиска, но считала их не
        /// та же полоса, что писала строку. Всё, что пережило сброс, помечается
        /// в описи словом ОСТАЛОСЬ.
        /// </summary>
        static int ResetWipesCurve(string dir)
        {
            Console.WriteLine("=== ЧТО ОСТАЁТСЯ ОТ ПРЕЖНЕГО ПРИБОРА ПОСЛЕ СБРОСА НАСТРОЙКИ СПЕКТРА (`A260`) ===");
            Console.WriteLine("  примета голоса «кривой нет»: «" + CurveVoiceMark() + "»");
            Console.WriteLine();
            int rc = 0;
            rc |= ResetArm("СБРОС ПО ЧИСЛУ КАНАЛОВ", Path.Combine(dir, "g11_reset.xml"), false, true);
            rc |= ResetArm("СБРОС ПО НАСТРОЙКЕ «ввозить с пустой конфигурацией»", Path.Combine(dir, "f71_emptycfg.xml"), true, true);
            rc |= ResetArm("СБРОСА НЕТ — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ", Path.Combine(dir, "f71_noreset.xml"), false, false);
            return rc;
        }

        /// <summary>
        /// Примета голоса «кривой нет»: кусок текста ресурса
        /// `ERRNoFwhmCalibrationImport` между подстановками {2} и {3}. Ресурс
        /// спрашивается у СОБРАННОЙ сборки тем же ResourceManager, каким
        /// пользуется приложение (`A154`), и в той же культуре, в которой идёт
        /// прогон, — иначе примета не совпала бы с тем, что напечатала дверь.
        /// </summary>
        static string CurveVoiceMark()
        {
            try
            {
                ResourceManager rm = new ResourceManager("BecquerelMonitor.Properties.Resources",
                                                         typeof(DocumentManager).Assembly);
                string fmt = rm.GetString("ERRNoFwhmCalibrationImport", Thread.CurrentThread.CurrentUICulture);
                if (string.IsNullOrEmpty(fmt)) return "(ресурс не найден)";
                int a = fmt.IndexOf("{2}", StringComparison.Ordinal);
                int b = fmt.IndexOf("{3}", StringComparison.Ordinal);
                if (a < 0 || b < 0 || b <= a) return "(в тексте нет {2}…{3})";
                return fmt.Substring(a + 3, b - a - 3).Trim();
            }
            catch (Exception ex)
            {
                return "(ресурс не читается: " + ex.Message + ")";
            }
        }

        /// <summary>
        /// Опись спектра — то, из чего видно, ЧТО ИМЕННО пережило сброс.
        /// Порядок строк постоянный: два снимка сличаются построчно.
        /// </summary>
        static List<string> Inventory(ResultData rd)
        {
            List<string> v = new List<string>();
            if (rd == null) { v.Add("спектра нет"); return v; }
            v.Add("каналов                = " + (rd.EnergySpectrum == null ? "(нет спектра)"
                    : rd.EnergySpectrum.NumberOfChannels.ToString(CultureInfo.InvariantCulture)));
            v.Add("прибор.имя             = " + (rd.DeviceConfig == null ? "(нет)" : "«" + rd.DeviceConfig.Name + "»"));
            v.Add("прибор.guid            = " + (rd.DeviceConfig == null ? "(нет)" : Nz(rd.DeviceConfig.Guid)));
            v.Add("ссылка на прибор.имя   = " + (rd.DeviceConfigReference == null ? "(нет)" : "«" + Nz(rd.DeviceConfigReference.Name) + "»"));
            v.Add("ссылка на прибор.guid  = " + (rd.DeviceConfigReference == null ? "(нет)" : Nz(rd.DeviceConfigReference.Guid)));
            FWHMPeakDetectionMethodConfig cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            v.Add("настройки поиска пиков = " + Cfg3(cfg)
                  + (cfg == null ? "" : " SNR " + cfg.Min_SNR.ToString("0.###", CultureInfo.InvariantCulture)
                                        + ", допуск " + cfg.Tolerance.ToString("0.###", CultureInfo.InvariantCulture)));
            v.Add("кривая настроек        = " + (cfg == null ? "(настроек нет)" : Curve(cfg.FwhmCalibration)));
            v.Add("кривая разрешения      = " + Curve(rd.FwhmCalibration));
            v.Add("ROI                    = " + (rd.ROIConfig == null ? "(нет)" : "«" + Nz(rd.ROIConfig.Name) + "»"));
            v.Add("ссылка на ROI          = " + (rd.ROIConfigReference == null ? "(нет)" : Nz(rd.ROIConfigReference.Guid)));
            v.Add("фон.спектр             = " + (rd.BackgroundEnergySpectrum == null ? "(нет)" : "есть"));
            v.Add("фон.файл               = " + Nz(rd.BackgroundSpectrumFile));
            v.Add("фон.путь               = " + Nz(rd.BackgroundSpectrumPathname));
            v.Add("кривая эффективности   = " + (rd.Efficiency == null ? "(нет)" : "«" + Nz(rd.Efficiency.Name) + "»"));
            v.Add("родная из файла        = " + (rd.FileEfficiency == null ? "(нет)" : "«" + Nz(rd.FileEfficiency.Name) + "»"));
            v.Add("примета детектора      = " + Nz(rd.DetectorFeature));
            v.Add("найденных пиков        = " + Peaks(rd.DetectedPeaks));
            v.Add("пиков калибровки       = " + Peaks(rd.CalibrationPeaks));
            v.Add("точек калибровки       = " + (rd.CalibrationPoints == null ? "(нет)"
                    : rd.CalibrationPoints.Count.ToString(CultureInfo.InvariantCulture)
                      + (rd.CalibrationPoints.Count == 0 ? "" : ", первая на канале "
                          + rd.CalibrationPoints[0].Channel.ToString(CultureInfo.InvariantCulture))));
            return v;
        }

        /// <summary>Сколько пиков и на каком канале первый: по номеру видно, что он вне новой шкалы.</summary>
        static string Peaks(List<Peak> list)
        {
            if (list == null) return "(нет)";
            if (list.Count == 0) return "0";
            return list.Count.ToString(CultureInfo.InvariantCulture)
                   + ", первый на канале " + list[0].Channel.ToString(CultureInfo.InvariantCulture);
        }

        static string Nz(string s)
        {
            if (s == null) return "(null)";
            if (s.Length == 0) return "(пусто)";
            return s;
        }

        /// <summary>
        /// Одно плечо сброса. <paramref name="emptyConfig"/> — настройка
        /// «ввозить с пустой конфигурацией» на время плеча;
        /// <paramref name="expectReset"/> — обязан ли сброс сработать.
        /// Файл ввоза сочиняется той же длины, что у документа, когда сброса
        /// быть не должно, и вчетверо короче — когда должен.
        /// </summary>
        static int ResetArm(string state, string path, bool emptyConfig, bool expectReset)
        {
            Console.WriteLine("=== " + state + " | " + Path.GetFileName(path) + " ===");
            DocumentManager dm = DocumentManager.GetInstance();
            GlobalConfigInfo gc = GlobalConfigManager.GetInstance().GlobalConfig;
            bool keepEmptyConfig = gc.ImportSpectrumWithEmptyConfig;
            if (File.Exists(path)) File.Delete(path);
            DocEnergySpectrum doc = dm.CreateDocument(path);
            if (doc == null)
            {
                Console.WriteLine("  документ НЕ СОЗДАН — мерить нечего");
                Console.WriteLine();
                armTotals.Add(state + " -> документ не создан — НЕ СОШЛОСЬ");
                return 1;
            }
            ResultData rd = doc.ActiveResultData;

            // Приметы, которых у свежего документа нет: без них опись молчала
            // бы о половине полей, и «пережило сброс» было бы нечем измерить.
            rd.DetectorFeature = "F71-примета детектора";
            rd.Efficiency = new EfficiencyConfigData { Name = "F71-кривая эффективности", Guid = "f71-eff-guid" };
            rd.FileEfficiency = rd.Efficiency;
            rd.BackgroundSpectrumFile = "f71-фон.xml";
            rd.BackgroundSpectrumPathname = Path.Combine(Path.GetDirectoryName(path), "f71-фон.xml");
            // Пики и точки калибровки — на каналах, которых на новой шкале
            //   (1024) нет вовсе. Пустые списки сброс НЕ МЕРЯЛИ: ноль равен
            //   нулю и до, и после, и «ОСТАЛОСЬ» ничего не значило бы.
            rd.DetectedPeaks = new List<Peak> { new Peak { Channel = 5000, Energy = 1460.8, SNR = 42.0 } };
            rd.CalibrationPeaks = new List<Peak> { new Peak { Channel = 6000, Energy = 2614.5, SNR = 17.0 } };
            rd.CalibrationPoints = new List<CalibrationPoint> { new CalibrationPoint(5000, 1460.8m, 123) };

            int chBefore = rd.EnergySpectrum == null ? 0 : rd.EnergySpectrum.NumberOfChannels;
            List<string> before = Inventory(rd);
            int chFile = expectReset && !emptyConfig ? (chBefore == 1024 ? 2048 : 1024) : chBefore;
            string atomPath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_ats.txt");
            WriteAtomSpectra(atomPath, chFile);

            List<string> voices = new List<string>();
            TextWriter realErr = Console.Error;
            StringWriter caught = new StringWriter();
            string trouble = null;
            gc.ImportSpectrumWithEmptyConfig = emptyConfig;
            Console.SetError(caught);
            try
            {
                dm.ImportDocumentAtomSpectra(doc, atomPath);
            }
            catch (Exception ex)
            {
                trouble = "ввоз оборвался: " + ex.Message;
            }
            finally
            {
                Voices(caught, voices);
                Console.SetError(realErr);
                gc.ImportSpectrumWithEmptyConfig = keepEmptyConfig;
            }

            ResultData after = doc.ActiveResultData;
            List<string> now = Inventory(after);

            Console.WriteLine("  настройка «ввозить с пустой конфигурацией»: " + (emptyConfig ? "ВКЛ" : "выкл")
                              + ";  каналов у документа " + chBefore.ToString(CultureInfo.InvariantCulture)
                              + ", в файле " + chFile.ToString(CultureInfo.InvariantCulture)
                              + (chFile == chBefore ? " (то же)" : " (другое)"));
            Console.WriteLine("  ОПИСЬ СПЕКТРА (снято = поле изменилось сбросом, ОСТАЛОСЬ = пережило):");
            int survived = 0, wiped = 0;
            for (int i = 0; i < before.Count && i < now.Count; i++)
            {
                bool same = before[i] == now[i];
                if (same) survived++; else wiped++;
                Console.WriteLine("    " + (same ? "ОСТАЛОСЬ  " : "изменилось") + "  " + before[i]);
                if (!same) Console.WriteLine("                          -> " + now[i].Substring(now[i].IndexOf('=') + 2));
            }
            Console.WriteLine("  полей всего " + before.Count.ToString(CultureInfo.InvariantCulture)
                              + ": изменилось " + wiped.ToString(CultureInfo.InvariantCulture)
                              + ", осталось " + survived.ToString(CultureInfo.InvariantCulture));

            string mark = CurveVoiceMark();
            int curveVoices = 0;
            foreach (string one in voices)
            {
                if (mark.Length > 0 && one.IndexOf(mark, StringComparison.Ordinal) >= 0) curveVoices++;
            }
            Console.WriteLine("  голосов всего " + voices.Count.ToString(CultureInfo.InvariantCulture)
                              + ", из них «кривой нет» " + curveVoices.ToString(CultureInfo.InvariantCulture));
            foreach (string one in voices) Console.WriteLine("    " + one);

            // --- приговор ---------------------------------------------------
            bool devWiped = after.DeviceConfig == null || string.IsNullOrEmpty(after.DeviceConfig.Name);
            bool curveGone = after.FwhmCalibration == null;
            bool chChanged = after.EnergySpectrum != null && after.EnergySpectrum.NumberOfChannels != chBefore;
            List<string> fail = new List<string>();
            if (trouble != null) fail.Add(trouble);
            if (expectReset)
            {
                // Плечо мерит только тогда, когда сброс И ПРАВДА сработал.
                if (!devWiped) fail.Add("прибор НЕ стёрт — сброса не было, плечо не мерит");
                if (!emptyConfig && !chChanged) fail.Add("число каналов не сменилось — сброса не было, плечо не мерит");
                if (!curveGone) fail.Add("кривая разрешения ОСТАЛАСЬ: " + Curve(after.FwhmCalibration));
                if (curveVoices != 1) fail.Add("голосов «кривой нет» " + curveVoices + ", ожидался ровно 1");
            }
            else
            {
                if (devWiped) fail.Add("прибор стёрт, хотя сброса быть не должно");
                if (chChanged) fail.Add("число каналов сменилось, хотя сброса быть не должно");
                if (curveGone) fail.Add("кривая снята, хотя сброса быть не должно");
                if (wiped != 0) fail.Add("опись сдвинулась в " + wiped + " полях, хотя сброса быть не должно");
                if (curveVoices != 0) fail.Add("голосов «кривой нет» " + curveVoices + ", ожидалось 0");
            }
            bool ok = fail.Count == 0;
            string total = state + " -> прибор стёрт " + (devWiped ? "ДА" : "нет")
                           + ", кривая снята " + (curveGone ? "ДА" : "нет")
                           + ", голосов «кривой нет» " + curveVoices.ToString(CultureInfo.InvariantCulture)
                           + ", полей сдвинулось " + wiped.ToString(CultureInfo.InvariantCulture)
                           + (ok ? " — СОШЛОСЬ" : " — НЕ СОШЛОСЬ: " + string.Join("; ", fail.ToArray()));
            Console.WriteLine("  ИТОГ: " + total);
            Console.WriteLine();
            armTotals.Add(total);
            return ok ? 0 : 1;
        }

        /// <summary>
        /// ⛔ ДВЕ ДВЕРИ, КОТОРЫЕ МОЛЧАЛИ (`A260`, полоса F71, 06.09.2026).
        ///
        /// `ImportDocumentGBS` и `ImportCsvToDocument` тоже зовут сброс
        /// настройки спектра, но <c>CheckDocument</c> и голос
        /// <c>ReportMissingFwhmCalibration</c> у них до 06.09.2026 не звучали
        /// вовсе — и были не нужны: сброс кривую ОСТАВЛЯЛ. Теперь он её СНИМАЕТ,
        /// и без читателя обе двери отдавали бы документ без модели разрешения
        /// молча — та самая немота, ради которой заведён ~~`A234`~~. По строке в
        /// каждую дверь; здесь они меряются.
        ///
        /// Сброс здесь вызывается НАСТРОЙКОЙ «ввозить с пустой конфигурацией»,
        /// а не числом каналов: у обеих дверей длина файла берётся из него
        /// самого, и держать её равной документу проще, чем подгонять.
        /// Положительный контроль у каждой двери свой — то же плечо с настройкой
        /// СНЯТОЙ: опись не двигается ни на одно поле, голоса нет.
        /// </summary>
        static int QuietDoors(string dir)
        {
            Console.WriteLine("=== ДВЕ ДВЕРИ БЕЗ ЧИТАТЕЛЯ ПРИЧИНЫ: GBS И CSV СО СЧЁТОМ (`A260`) ===");
            Console.WriteLine();
            int rc = 0;
            rc |= QuietDoorArm("GBS", "СБРОС ПО НАСТРОЙКЕ", Path.Combine(dir, "f71_gbs_on.xml"), true);
            rc |= QuietDoorArm("GBS", "СБРОСА НЕТ — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ", Path.Combine(dir, "f71_gbs_off.xml"), false);
            rc |= QuietDoorArm("CSV", "СБРОС ПО НАСТРОЙКЕ", Path.Combine(dir, "f71_csv_on.xml"), true);
            rc |= QuietDoorArm("CSV", "СБРОСА НЕТ — ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ", Path.Combine(dir, "f71_csv_off.xml"), false);
            return rc;
        }

        static int QuietDoorArm(string door, string state, string path, bool emptyConfig)
        {
            string head = "ДВЕРЬ " + door + " | " + state;
            Console.WriteLine("=== " + head + " | " + Path.GetFileName(path) + " ===");
            DocumentManager dm = DocumentManager.GetInstance();
            GlobalConfigInfo gc = GlobalConfigManager.GetInstance().GlobalConfig;
            bool keepEmptyConfig = gc.ImportSpectrumWithEmptyConfig;
            if (File.Exists(path)) File.Delete(path);
            DocEnergySpectrum doc = dm.CreateDocument(path);
            if (doc == null)
            {
                Console.WriteLine("  документ НЕ СОЗДАН — мерить нечего");
                Console.WriteLine();
                armTotals.Add(head + " -> документ не создан — НЕ СОШЛОСЬ");
                return 1;
            }
            ResultData rd = doc.ActiveResultData;
            rd.DetectorFeature = "F71-примета детектора";
            int chBefore = rd.EnergySpectrum == null ? 0 : rd.EnergySpectrum.NumberOfChannels;
            List<string> before = Inventory(rd);

            string input = Path.Combine(Path.GetDirectoryName(path),
                                        Path.GetFileNameWithoutExtension(path) + (door == "GBS" ? ".spe" : ".csv"));
            if (door == "GBS") WriteGbs(input, chBefore); else WriteCsvCounts(input, chBefore);

            List<string> voices = new List<string>();
            TextWriter realErr = Console.Error;
            StringWriter caught = new StringWriter();
            string trouble = null;
            gc.ImportSpectrumWithEmptyConfig = emptyConfig;
            Console.SetError(caught);
            try
            {
                if (door == "GBS") dm.ImportDocumentGBS(doc, input);
                else dm.ImportCsvToDocument(doc, 600, input);
            }
            catch (Exception ex)
            {
                trouble = "ввоз оборвался: " + ex.Message;
            }
            finally
            {
                Voices(caught, voices);
                Console.SetError(realErr);
                gc.ImportSpectrumWithEmptyConfig = keepEmptyConfig;
            }

            ResultData after = doc.ActiveResultData;
            List<string> now = Inventory(after);
            int wiped = 0;
            for (int i = 0; i < before.Count && i < now.Count; i++)
            {
                if (before[i] != now[i])
                {
                    wiped++;
                    Console.WriteLine("    изменилось  " + before[i]);
                    Console.WriteLine("                          -> " + now[i].Substring(now[i].IndexOf('=') + 2));
                }
            }
            string mark = CurveVoiceMark();
            int curveVoices = 0;
            foreach (string one in voices)
            {
                if (mark.Length > 0 && one.IndexOf(mark, StringComparison.Ordinal) >= 0) curveVoices++;
            }
            Console.WriteLine("  полей сдвинулось " + wiped.ToString(CultureInfo.InvariantCulture)
                              + " из " + before.Count.ToString(CultureInfo.InvariantCulture)
                              + "; голосов всего " + voices.Count.ToString(CultureInfo.InvariantCulture)
                              + ", из них «кривой нет» " + curveVoices.ToString(CultureInfo.InvariantCulture));
            foreach (string one in voices) Console.WriteLine("    " + one);

            bool curveGone = after.FwhmCalibration == null;
            List<string> fail = new List<string>();
            if (trouble != null) fail.Add(trouble);
            if (emptyConfig)
            {
                if (after.DeviceConfig != null && !string.IsNullOrEmpty(after.DeviceConfig.Name))
                    fail.Add("прибор НЕ стёрт — сброса не было, плечо не мерит");
                if (!curveGone) fail.Add("кривая разрешения ОСТАЛАСЬ: " + Curve(after.FwhmCalibration));
                if (curveVoices != 1) fail.Add("голосов «кривой нет» " + curveVoices + ", ожидался ровно 1");
            }
            else
            {
                if (curveGone) fail.Add("кривая снята, хотя сброса быть не должно");
                if (wiped != 0) fail.Add("опись сдвинулась в " + wiped + " полях, хотя сброса быть не должно");
                if (curveVoices != 0) fail.Add("голосов «кривой нет» " + curveVoices + ", ожидалось 0");
            }
            bool ok = fail.Count == 0;
            string total = head + " -> кривая снята " + (curveGone ? "ДА" : "нет")
                           + ", голосов «кривой нет» " + curveVoices.ToString(CultureInfo.InvariantCulture)
                           + ", полей сдвинулось " + wiped.ToString(CultureInfo.InvariantCulture)
                           + (ok ? " — СОШЛОСЬ" : " — НЕ СОШЛОСЬ: " + string.Join("; ", fail.ToArray()));
            Console.WriteLine("  ИТОГ: " + total);
            Console.WriteLine();
            armTotals.Add(total);
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Файл GBS (`Ritecdat`) — вход двери `ImportDocumentGBS`. Разделы
        /// читаются по порядку: `$SPEC_ID:`, `$DATE_MEA:`, `$MEAS_TIM:`,
        /// `$DATA:` (вторым числом строки идёт НОМЕР ПОСЛЕДНЕГО канала, дверь
        /// прибавляет единицу), отсчёты, `$ENER_DATA_X:` (число точек, потом
        /// «канал энергия») и `$COUNTS:`.
        ///
        /// ⚠ ТОЧЕК РОВНО ДВЕ, И ЭТО НЕ ЛЕНЬ: дверь берёт порядок
        /// многочлена `Math.Min(4, numpoints - 1)`. На пяти точках это
        /// ЧЕТВЁРТЫЙ порядок, и подгонка через СТРОГО ПРЯМЫЕ точки
        /// выходит немонотонной от шума в старших коэффициентах — измерено:
        /// ввоз обрывался отказом «функция калибровки должна монотонно
        /// возрастать», к кривой разрешения отношения не имеющим. Две точки
        /// дают первый порядок, а прямая монотонна по построению.
        /// </summary>
        static void WriteGbs(string path, int channels)
        {
            var sb = new StringBuilder();
            sb.AppendLine("$SPEC_ID:");
            sb.AppendLine("проба F71: вход двери GBS");
            sb.AppendLine("$DATE_MEA:");
            sb.AppendLine("09/06/2026 12:00:00");
            sb.AppendLine("$MEAS_TIM:");
            sb.AppendLine("600  600");
            sb.AppendLine("$DATA:");
            sb.AppendLine("0 " + (channels - 1).ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < channels; i++)
            {
                sb.AppendLine(((i % 7) + 1).ToString(CultureInfo.InvariantCulture));
            }
            sb.AppendLine("$ENER_DATA_X:");
            sb.AppendLine("2");
            int[] chs = { 100, channels - 100 };
            for (int i = 0; i < chs.Length; i++)
            {
                sb.AppendLine(chs[i].ToString(CultureInfo.InvariantCulture) + " "
                              + (0.5 * chs[i]).ToString("F4", CultureInfo.InvariantCulture));
            }
            sb.AppendLine("$COUNTS:");
            sb.AppendLine("4096");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// CSV «канал, отсчёты» — вход двери `ImportCsvToDocument`. Шапка ровно
        /// из двух полей, и во втором обязана стоять длительность видом
        /// `(TotalTime=600s)`: без неё дверь бросает «Wrong header format».
        /// </summary>
        static void WriteCsvCounts(string path, int channels)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Channel,Count (TotalTime=600s)");
            for (int i = 0; i < channels; i++)
            {
                sb.AppendLine(i.ToString(CultureInfo.InvariantCulture) + ","
                              + ((i % 5) + 2).ToString(CultureInfo.InvariantCulture));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Сколько строк `BecqMoni:` (голосов `AppUi.Report` без окон) в перехваченном потоке ошибок.</summary>
        static int Voices(StringWriter caught, List<string> into)
        {
            int n = 0;
            foreach (string line in caught.ToString().Split('\n'))
            {
                string one = line.Trim();
                if (one.Length == 0) continue;
                into.Add(one);
                if (one.StartsWith("BecqMoni:")) n++;
            }
            return n;
        }

        static string CurveState(ResultData rd)
        {
            if (rd == null) return "спектра нет";
            return rd.FwhmCalibration == null ? "без кривой" : "кривая есть " + Curve(rd.FwhmCalibration);
        }

        /// <summary>
        /// Одно плечо: создать документ (CreateDocument), записать (SaveDocument),
        /// закрыть, открыть (OpenDocument), подгрузить его же как фон
        /// (LoadBackgroundSpectrum), ввезти в него файл Atom Spectra
        /// (ImportDocumentAtomSpectra) и CSV с энергиями
        /// (ImportCsvEnergyToDocument). Пять чисел голосов — пять событий. Код
        /// возврата 1, если хоть одно число разошлось с ожиданием.
        /// </summary>
        static int OneVoiceArm(string state, string path, int expectCreate, int expectOpen, int expectBg,
                               int expectAtom, int expectCsv)
        {
            string head = state + " | " + Path.GetFileName(path);
            Console.WriteLine("=== " + head + " ===");
            DocumentManager dm = DocumentManager.GetInstance();
            List<string> voices = new List<string>();
            int vCreate = -1, vOpen = -1, vBg = -1, vAtom = -1, vCsv = -1;
            string sCreate = "?", sOpen = "?", sBg = "?", sAtom = "?", sCsv = "?";
            string trouble = null;

            if (File.Exists(path)) File.Delete(path);
            TextWriter realErr = Console.Error;
            StringWriter caught = new StringWriter();
            Console.SetError(caught);
            try
            {
                // 1. Создание — пункт «Новый»: CreateDocument -> CheckDocument.
                DocEnergySpectrum doc = dm.CreateDocument(path);
                vCreate = Voices(caught, voices);
                if (doc == null) throw new InvalidOperationException("документ НЕ СОЗДАН (CreateDocument вернул null)");
                sCreate = CurveState(doc.ActiveResultData);

                // 2. Запись и закрытие — состояние без кривой уходит в файл.
                if (!dm.SaveDocument(doc)) throw new InvalidOperationException("документ НЕ ЗАПИСАН");
                dm.CloseDocument(doc);

                // 3. Открытие сохранённого — OpenDocument -> CheckDocument.
                caught = new StringWriter();
                Console.SetError(caught);
                DocEnergySpectrum doc2 = dm.OpenDocument(path);
                vOpen = Voices(caught, voices);
                if (doc2 == null) throw new InvalidOperationException("документ НЕ ОТКРЫТ (OpenDocument вернул null)");
                sOpen = CurveState(doc2.ActiveResultData);

                // 4. Тот же файл как фон — LoadBackgroundSpectrum -> CheckDocument.
                caught = new StringWriter();
                Console.SetError(caught);
                ResultData rd = doc2.ActiveResultData;
                rd.BackgroundSpectrumPathname = path;
                dm.LoadBackgroundSpectrum(rd);
                vBg = Voices(caught, voices);
                sBg = rd.BackgroundEnergySpectrum == null ? "фон НЕ загружен" : "фон загружен";

                // ⛔ `A240`, остаток (полоса F66): ещё две двери к тому же
                //    `CheckDocument`. Ввоз идёт В ОТКРЫТЫЙ ДОКУМЕНТ — так это и
                //    делает пункт меню, и состояние кривой у него уже своё.
                //
                //    ⚠ Сброс настройки спектра НАРОЧНО не задевается: у обеих
                //    дверей он срабатывает от `ImportSpectrumWithEmptyConfig`
                //    либо от несовпадения числа каналов, и у Atom Spectra тогда
                //    звучит СВОЁ уведомление («каналов в файле не то»), которое
                //    к кривой разрешения отношения не имеет и сбило бы счёт
                //    голосов. Поэтому число каналов у сочинённых файлов — то же,
                //    что у документа, а настройка на время плеча снята.
                bool keepEmptyConfig = GlobalConfigManager.GetInstance().GlobalConfig.ImportSpectrumWithEmptyConfig;
                GlobalConfigManager.GetInstance().GlobalConfig.ImportSpectrumWithEmptyConfig = false;
                try
                {
                    int channels = rd.EnergySpectrum.NumberOfChannels;

                    // 5. Ввоз Atom Spectra — ImportDocumentAtomSpectra -> CheckDocument.
                    string atomPath = Path.Combine(Path.GetDirectoryName(path),
                                                   Path.GetFileNameWithoutExtension(path) + "_ats.txt");
                    WriteAtomSpectra(atomPath, channels);
                    caught = new StringWriter();
                    Console.SetError(caught);
                    dm.ImportDocumentAtomSpectra(doc2, atomPath);
                    vAtom = Voices(caught, voices);
                    sAtom = CurveState(doc2.ActiveResultData) + ", отсчётов "
                            + doc2.ActiveResultData.EnergySpectrum.TotalPulseCount.ToString(CultureInfo.InvariantCulture);

                    // 6. Ввоз CSV с энергиями — ImportCsvEnergyToDocument -> CheckDocument.
                    string csvPath = Path.Combine(Path.GetDirectoryName(path),
                                                  Path.GetFileNameWithoutExtension(path) + "_energy.csv");
                    WriteCsvEnergy(csvPath, channels);
                    caught = new StringWriter();
                    Console.SetError(caught);
                    dm.ImportCsvEnergyToDocument(doc2, 600, csvPath);
                    vCsv = Voices(caught, voices);
                    sCsv = CurveState(doc2.ActiveResultData) + ", отсчётов "
                           + doc2.ActiveResultData.EnergySpectrum.TotalPulseCount.ToString(CultureInfo.InvariantCulture);
                }
                finally
                {
                    GlobalConfigManager.GetInstance().GlobalConfig.ImportSpectrumWithEmptyConfig = keepEmptyConfig;
                }
                dm.CloseDocument(doc2);
            }
            catch (Exception ex)
            {
                trouble = ex.GetType().Name + ": " + Flat(ex.Message);
            }
            finally
            {
                Console.SetError(realErr);
            }

            Console.WriteLine("  создание:     голосов " + vCreate + ", " + sCreate);
            Console.WriteLine("  открытие:     голосов " + vOpen + ", " + sOpen);
            Console.WriteLine("  фон:          голосов " + vBg + ", " + sBg);
            Console.WriteLine("  ввоз ats:     голосов " + vAtom + ", " + sAtom);
            Console.WriteLine("  ввоз csv:     голосов " + vCsv + ", " + sCsv);
            if (trouble != null) Console.WriteLine("  ОТКАЗ: " + trouble);
            if (voices.Count > 0)
            {
                Console.WriteLine("  ГОЛОСА:");
                foreach (string v in voices) Console.WriteLine("    " + v);
            }
            bool ok = trouble == null && vCreate == expectCreate && vOpen == expectOpen && vBg == expectBg
                      && vAtom == expectAtom && vCsv == expectCsv;
            string total = head + " -> создание " + vCreate + " (ожидалось " + expectCreate + "), открытие "
                           + vOpen + " (ожидалось " + expectOpen + "), фон " + vBg + " (ожидалось " + expectBg + ")"
                           + ", ввоз ats " + vAtom + " (ожидалось " + expectAtom + "), ввоз csv "
                           + vCsv + " (ожидалось " + expectCsv + ")"
                           + (ok ? " — СОШЛОСЬ" : " — НЕ СОШЛОСЬ") + (trouble == null ? "" : "; " + trouble);
            Console.WriteLine("  ИТОГ: " + total);
            Console.WriteLine();
            armTotals.Add(total);
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Файл Atom Spectra «FORMAT: 3» — вход двери `ImportDocumentAtomSpectra`
        /// (`A240`, остаток, полоса F66). Порядок строк взят у самого разбора:
        /// первые десять строк дверь читает ДВАЖДЫ (в первом заходе — только
        /// чтобы взять десятую, число каналов), поэтому смещаться им нельзя.
        ///   1 FORMAT: 3      2 примечание   3 время начала, мс   4 время конца
        ///   5 широта         6 долгота      7 имя пробы          8 прибор
        ///   9 длительность, с              10 каналов          11 степень
        ///   12.. коэффициенты (степень + 1), дальше — отсчёты по каналам.
        /// Калибровка нарочно линейная и возрастающая: `CheckCalibration` иначе
        /// отвергает шкалу, и дверь говорит СВОЁ слово про калибровку, которое к
        /// кривой разрешения отношения не имеет.
        /// </summary>
        static void WriteAtomSpectra(string path, int channels)
        {
            var sb = new StringBuilder();
            sb.AppendLine("FORMAT: 3");
            sb.AppendLine("проба F66: вход двери Atom Spectra");
            sb.AppendLine("1757000000000");
            sb.AppendLine("1757000600000");
            sb.AppendLine("55.7500");
            sb.AppendLine("37.6100");
            sb.AppendLine("F66-ATS");
            sb.AppendLine("F66 probe device");
            sb.AppendLine("600");
            sb.AppendLine(channels.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("1");
            sb.AppendLine("0");
            sb.AppendLine("0.5");
            for (int i = 0; i < channels; i++)
            {
                sb.AppendLine(((i % 7) + 1).ToString(CultureInfo.InvariantCulture));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// CSV «энергия, отсчёты» — вход двери `ImportCsvEnergyToDocument`
        /// (`A240`, остаток, полоса F66). Шапка обязана нести длительность в
        /// виде `#0d0h10m0s`, иначе дверь считает время нулевым. Энергия слегка
        /// нелинейна: дверь подгоняет по точкам многочлен 4-й степени, и на
        /// строго прямой линии старший коэффициент вышел бы нулём.
        /// </summary>
        static void WriteCsvEnergy(string path, int channels)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Energy,Count #0d0h10m0s");
            for (int i = 0; i < channels; i++)
            {
                double e = 0.5 * i + 1.0e-6 * i * i;
                sb.AppendLine(e.ToString("F4", CultureInfo.InvariantCulture) + ","
                              + ((i % 5) + 2).ToString(CultureInfo.InvariantCulture));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static string Cfg3(FWHMPeakDetectionMethodConfig cfg)
        {
            if (cfg == null) return "(настроек нет)";
            return "(" + cfg.FWHM_AT_0.ToString("0.###", CultureInfo.InvariantCulture)
                 + "/" + cfg.Ch_Fwhm.ToString("0.###", CultureInfo.InvariantCulture)
                 + "/" + cfg.Width_Fwhm.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        static bool SameCfg3(FWHMPeakDetectionMethodConfig a, FWHMPeakDetectionMethodConfig b)
        {
            return a != null && b != null
                && a.FWHM_AT_0 == b.FWHM_AT_0 && a.Ch_Fwhm == b.Ch_Fwhm && a.Width_Fwhm == b.Width_Fwhm;
        }

        /// <summary>Опорные точки кривой разрешения: «канал:ПШПВ», по ним видно, чьё умолчание её построило.</summary>
        static string Curve(FwhmCalibration c)
        {
            if (c == null) return "null";
            if (c.CalibrationPeaks == null) return c.GetType().Name;
            StringBuilder sb = new StringBuilder();
            sb.Append(c.GetType().Name).Append('[');
            for (int i = 0; i < c.CalibrationPeaks.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(c.CalibrationPeaks[i].Channel).Append(':')
                  .Append(c.CalibrationPeaks[i].FWHM.ToString("0.###", CultureInfo.InvariantCulture));
            }
            return sb.Append(']').ToString();
        }

        /// <summary>
        /// Одно плечо: документ приложения (CreateDocument), названная дверь,
        /// после двери — чьи настройки у КАЖДОГО спектра списка.
        /// Код возврата 1, если хоть у одного ввезённого спектра настройки
        /// поиска пиков — встроенные умолчания, а не документа; это и есть
        /// приговор `A239`, и после правки он обязан стать 0.
        /// </summary>
        static int OriginArm(string configState, string door, string[] files, FWHMPeakDetectionMethodConfig builtin)
        {
            string head = configState + " | документ приложения | дверь " + door;
            Console.WriteLine("=== " + head + " ===");
            int ok = 0, failed = 0, spectra = 0, cfgDoc = 0, cfgBuiltin = 0, cfgOther = 0,
                devSame = 0, devFresh = 0, roiSame = 0, roiNull = 0, roiFresh = 0,
                curveNull = 0, curveDoc = 0, curveBuiltin = 0, curveOther = 0, spoke = 0;
            List<string> voices = new List<string>();

            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                DocEnergySpectrum doc = DocumentManager.GetInstance().CreateDocument(name + ".xml");
                if (doc == null || doc.ActiveResultData == null)
                {
                    Console.WriteLine("  " + name + " | документ НЕ СОЗДАН");
                    failed++;
                    continue;
                }
                ResultData before = doc.ActiveResultData;
                DeviceConfigInfo devBefore = before.DeviceConfig;
                ROIConfigData roiBefore = before.ROIConfig;
                FWHMPeakDetectionMethodConfig cfgBefore = before.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
                string curveBefore = Curve(before.FwhmCalibration);
                // Кривая, какую построило бы умолчание ПРИБОРА ДОКУМЕНТА, и
                // кривая ВСТРОЕННОГО умолчания — по ним опознаётся, чья.
                string curveOfDoc = cfgBefore == null ? "?" : Curve(FwhmCalibration.DefaultCalibration(cfgBefore, before.EnergySpectrum.EnergyCalibration));
                string curveOfBuiltin = Curve(FwhmCalibration.DefaultCalibration(builtin, before.EnergySpectrum.EnergyCalibration));

                string said = null;
                TextWriter realErr = Console.Error;
                StringWriter caught = new StringWriter();
                Console.SetError(caught);
                try
                {
                    if (door == "SpecUtils") DocumentManager.GetInstance().ImportDocumentSpecUtils(doc, f, 3600);
                    else DocumentManager.GetInstance().ImportDocumentN42(doc, f);
                }
                catch (Exception ex)
                {
                    said = ex.GetType().Name + ": " + Flat(ex.Message);
                }
                finally
                {
                    Console.SetError(realErr);
                }
                string voice = caught.ToString().Trim();
                if (voice.Length > 0)
                {
                    spoke++;
                    foreach (string line in voice.Split('\n'))
                    {
                        string one = line.Trim();
                        if (one.Length > 0 && !voices.Contains(one)) voices.Add(one);
                    }
                }
                if (said != null)
                {
                    failed++;
                    Console.WriteLine("  " + name + " | ОТКАЗ | " + said);
                    continue;
                }
                ok++;

                StringBuilder sb = new StringBuilder();
                sb.Append("  ").Append(name).Append(" | ВВЕЗЁН | до: прибор «")
                  .Append(devBefore == null ? "null" : devBefore.Name).Append("», настройки ")
                  .Append(Cfg3(cfgBefore)).Append(", кривая ").Append(curveBefore);
                for (int i = 0; i < doc.ResultDataFile.ResultDataList.Count; i++)
                {
                    ResultData rd = doc.ResultDataFile.ResultDataList[i];
                    if (rd == null || rd.EnergySpectrum == null) continue;
                    spectra++;
                    string dev;
                    if (object.ReferenceEquals(rd.DeviceConfig, devBefore)) { dev = "прибор ДОКУМЕНТА"; devSame++; }
                    else { dev = "прибор СВЕЖИЙ «" + (rd.DeviceConfig == null ? "null" : rd.DeviceConfig.Name) + "»"; devFresh++; }
                    string roi;
                    if (rd.ROIConfig == null) { roi = "ROI null"; roiNull++; }
                    else if (object.ReferenceEquals(rd.ROIConfig, roiBefore)) { roi = "ROI документа"; roiSame++; }
                    else { roi = "ROI СВЕЖИЙ"; roiFresh++; }
                    FWHMPeakDetectionMethodConfig cfg = rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
                    string cfgWho;
                    if (SameCfg3(cfg, cfgBefore)) { cfgWho = "ДОКУМЕНТА"; cfgDoc++; }
                    else if (SameCfg3(cfg, builtin)) { cfgWho = "ВСТРОЕННЫЕ"; cfgBuiltin++; }
                    else { cfgWho = "ЧУЖИЕ"; cfgOther++; }
                    string curve = Curve(rd.FwhmCalibration);
                    string curveWho;
                    if (rd.FwhmCalibration == null) { curveWho = "нет"; curveNull++; }
                    else if (curve == curveBefore || curve == curveOfDoc) { curveWho = "ДОКУМЕНТА"; curveDoc++; }
                    else if (curve == curveOfBuiltin) { curveWho = "ВСТРОЕННАЯ"; curveBuiltin++; }
                    else { curveWho = "ЧУЖАЯ"; curveOther++; }
                    sb.Append(" | [").Append(i).Append("] ").Append(dev)
                      .Append(", ссылка ").Append(rd.DeviceConfigReference == null ? "null" : (rd.DeviceConfigReference.Guid ?? "(пусто)"))
                      .Append(", ").Append(roi)
                      .Append(", настройки ").Append(cfgWho).Append(' ').Append(Cfg3(cfg))
                      .Append(", кривая ").Append(curveWho).Append(' ').Append(curve);
                }
                Console.WriteLine(sb.ToString());
            }

            string total = head + " -> ВВЕЗЕНО " + ok + " / ОТКАЗ " + failed + " (из " + files.Length + ")"
                           + "; спектров " + spectra
                           + ": прибор документа " + devSame + " / свежий " + devFresh
                           + "; ROI документа " + roiSame + " / null " + roiNull + " / свежий " + roiFresh
                           + "; настройки документа " + cfgDoc + " / ВСТРОЕННЫЕ " + cfgBuiltin + " / чужие " + cfgOther
                           + "; кривая документа " + curveDoc + " / ВСТРОЕННАЯ " + curveBuiltin + " / чужая " + curveOther + " / нет " + curveNull
                           + "; дверь сказала слово: " + spoke;
            Console.WriteLine("  ИТОГ: " + total);
            if (voices.Count > 0)
            {
                Console.WriteLine("  ГОЛОСА ДВЕРИ:");
                foreach (string v in voices) Console.WriteLine("    " + v);
            }
            Console.WriteLine();
            armTotals.Add(total);
            return cfgBuiltin + curveBuiltin > 0 ? 1 : 0;
        }

        static readonly List<string> armTotals = new List<string>();

        /// <summary>
        /// Одно плечо: каждый файл каталога ввозится названной дверью в
        /// документ, созданный названным способом. Печатается приговор, а у
        /// отказа — ПЕРВЫЙ кадр следа внутри приложения: строка `A212` называет
        /// место падения по чтению исходника, и подтвердить его обязан след, а
        /// не чтение.
        ///
        /// ⛔ ДВА ЗАМЕРА КРИВОЙ, ДО И ПОСЛЕ ВВОЗА (`A234`, 05.09.2026). Прежде
        ///    мерилось только состояние ДО, и число «без ПШПВ у документа: 12»
        ///    говорило про заготовку, а не про то, что дверь оставила человеку.
        ///    Разница между этими двумя числами и есть предмет `A234`: у двери
        ///    N42 ниже стоит <c>CheckDocument</c>, и он часть состояний
        ///    достраивает умолчанием, а часть — нет.
        ///
        /// ⛔ ГОЛОС ДВЕРИ ПЕЧАТАЕТСЯ, А НЕ ВЫБРАСЫВАЕТСЯ. Поток ошибок и так
        ///    перехватывался (иначе строки <c>AppUi.Report</c> лезли бы в
        ///    середину таблицы), но перехваченное молча терялось — то есть
        ///    проба по устройству не могла отличить дверь, сказавшую слово, от
        ///    двери молчащей. Ровно это и требуется мерить.
        /// </summary>
        static int Arm(string configState, string docWay, string door, string[] files)
        {
            string head = configState + " | " + docWay + " | дверь " + door;
            Console.WriteLine("=== " + head + " ===");

            int ok = 0, failed = 0, nullFwhm = 0, nullAfter = 0, noDoc = 0, spoke = 0;
            Dictionary<string, int> byKind = new Dictionary<string, int>();
            List<string> voices = new List<string>();

            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                DocEnergySpectrum doc = null;
                string said = null;
                string frame = "";
                string fwhm = "?";
                string fwhmAfter = "—";

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

                // Состояние ПОСЛЕ двери — то самое, что достаётся человеку.
                // Считается по ВСЕМУ списку спектров документа, а не по одному
                // активному: ввоз SpecUtils кладёт в документ до шестнадцати.
                if (doc != null && doc.ResultDataFile != null && doc.ResultDataFile.ResultDataList != null)
                {
                    int had = 0, gone = 0;
                    foreach (ResultData rd in doc.ResultDataFile.ResultDataList)
                    {
                        if (rd == null) continue;
                        had++;
                        if (rd.FwhmCalibration == null) gone++;
                    }
                    fwhmAfter = "спектров " + had + ", без ПШПВ " + gone;
                    if (gone > 0) nullAfter++;
                }

                string voice = caught.ToString().Trim();
                if (voice.Length > 0)
                {
                    spoke++;
                    foreach (string line in voice.Split('\n'))
                    {
                        string one = line.Trim();
                        if (one.Length > 0 && !voices.Contains(one)) voices.Add(one);
                    }
                }

                // ⛔ «НЕ УПАЛО» — ЕЩЁ НЕ «ВВЕЗЛО». Сторож null мог бы увести ввоз
                //    мимо спектров и молча отдать пустой документ, и по одному
                //    приговору это неотличимо от удачи. Поэтому в строке стоит
                //    СЛЕПОК: числа плеча со сломанным умолчанием обязаны совпасть
                //    с числами полного плеча той же двери.
                if (said == null)
                {
                    ok++;
                    Console.WriteLine("  " + name + " | ВВЕЗЁН | до: " + fwhm
                                      + " | после: " + fwhmAfter + " | " + Print(doc));
                }
                else
                {
                    failed++;
                    Console.WriteLine("  " + name + " | ОТКАЗ | до: " + fwhm
                                      + " | после: " + fwhmAfter + " | " + said
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
                           + ", без ПШПВ ДО: " + nullFwhm
                           + ", без ПШПВ ПОСЛЕ: " + nullAfter
                           + ", дверь сказала слово: " + spoke
                           + (noDoc > 0 ? ", документ не создан: " + noDoc : "")
                           + (kinds.Length == 0 ? "" : "   |   " + kinds);
            Console.WriteLine("  ИТОГ: " + total);
            if (voices.Count > 0)
            {
                Console.WriteLine("  ГОЛОСА ДВЕРИ:");
                foreach (string v in voices) Console.WriteLine("    " + v);
            }
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
