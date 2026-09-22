using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace N42SourceProbeP139
{
    /// <summary>
    /// ⛔ СВЕРКА ФАЙЛА `.n42` С ЕГО КОРПУСНЫМ ИСТОЧНИКОМ (`AMBER73`, полоса П139,
    /// 22.09.2026, решение Amber «Да, перевыгрузить сейчас»).
    ///
    /// Зачем отдельная проба, когда есть `N42RoundTripProbe --mode=export`.
    /// Та проба мерит КРУГ: выгружает спектр и тут же ввозит ТОТ ЖЕ файл. Она по
    /// построению не может сказать ничего о файлах, КОТОРЫЕ УЖЕ ЛЕЖАТ В ДЕРЕВЕ и
    /// писаны прежним вывозом: их она перезаписывает первым же движением. А
    /// вопрос 22.09.2026 именно про них — двенадцать `.n42` в `tools\CORPUS\n42`
    /// записаны старым соглашением о номере канала (полином — ЦЕНТРЫ) и читаются
    /// новым (полином — КРАЯ), то есть уезжают на +h/2.
    ///
    /// Что проба делает: для каждого `<имя>.n42` каталога `--dir` берёт корпусный
    /// спектр `<имя>.xml` из `--corpus`, открывает его настоящей дверью
    /// (`DocumentManager.OpenDocument`), ввозит `.n42` настоящей дверью
    /// (`DocumentManager.ImportDocumentN42`) и сверяет:
    ///   * ШКАЛУ — энергию каждого канала, наибольшее |ΔE| по всем каналам;
    ///   * ОТСЧЁТЫ — побитово, канал в канал (главный контроль перевыгрузки:
    ///     она обязана менять калибровку и НЕ ТРОГАТЬ содержимое);
    ///   * ВРЕМЕНА — измерения и живое, у спектра и у фона;
    ///   * ПРИЗНАК ФОНА и НАЧАЛО НАБОРА.
    ///
    /// ⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ВСТРОЕН В САМО НАЗНАЧЕНИЕ: на файлах ДО
    /// перевыгрузки проба обязана краснеть (шкала уехала), на файлах ПОСЛЕ —
    /// проходить. Проба, зелёная на обоих наборах, не мерит ничего, и это видно
    /// из одного прогона по копии старых файлов.
    ///
    /// ⚠ Порог `--tol=` (кэВ) не выдуман: круг «вывоз → ввоз» сходится сегодня с
    /// точностью одного младшего разряда double — наибольший сдвиг энергии
    /// 7.11E-15 кэВ (замер П138, 22.09.2026). Умолчание 1E-06 кэВ на девять
    /// порядков строже наименьшего измеренного дефекта (+0.0828 кэВ у
    /// `HPGE_Uranium`) и на девять порядков мягче шума представления.
    ///
    /// ⚠ У ВРЕМЁН ПОРОГ СВОЙ (`--ttol=`, умолчание 1E-06 с) И ТОЖЕ НЕ ВЫДУМАН.
    /// Время уходит в файл в формате `PT…S`, то есть на сетку 100 нс (`A147`), и
    /// точного равенства double после круга нет у части файлов ПО ПОСТРОЕНИЮ
    /// ФОРМАТА: в объявленном происхождении дерева (`manifest.csv` от 14.09.2026)
    /// `time_ok = 0` у трёх файлов с остатком до 7.276E-12 с и
    /// `bg_live_ok = 0` — тоже у трёх, с остатком до 2.858E-08 с. Требовать
    /// побитового равенства времён значило бы объявить дефектом сам формат;
    /// порог 1E-06 с на порядок мягче сетки 100 нс и на пять порядков строже
    /// наименьшей величины, которую человек различает на экране. Остаток
    /// ПЕЧАТАЕТСЯ всегда — «сошлось» здесь никогда не значит «не мерили».
    /// ⛔ У ОТСЧЁТОВ порога нет вовсе: они сверяются ПОБИТОВО, канал в канал.
    ///
    /// ⛔ ЧЕСТНАЯ ОГОВОРКА. И запись, и чтение здесь наши: проба доказывает
    /// согласие НАШЕГО файла с НАШИМ же источником и по-прежнему ничего не
    /// говорит о файлах чужих приборов.
    ///
    ///   N42SourceProbeP139.exe --dir=&lt;каталог .n42&gt; --corpus=&lt;каталог спектров&gt;
    ///                          [--tol=1e-6] [--ttol=1e-6] [--dump=&lt;file.csv&gt;]
    ///
    /// Коды возврата: 0 — все файлы сошлись; 1 — есть расхождения (перечень
    /// поимённо); 2 — звать нечем (нет каталога, нет источника, отказ двери).
    /// </summary>
    static class Program
    {
        static string dir = null;
        static string corpus = null;
        static string dump = null;
        static double tol = 1e-6;
        static double ttol = 1e-6;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }

            foreach (string a in args)
            {
                if (a.StartsWith("--dir=")) dir = a.Substring(6);
                else if (a.StartsWith("--corpus=")) corpus = a.Substring(9);
                else if (a.StartsWith("--dump=")) dump = a.Substring(7);
                else if (a.StartsWith("--tol=")) tol = double.Parse(a.Substring(6), NumberStyles.Float, CultureInfo.InvariantCulture);
                else if (a.StartsWith("--ttol=")) ttol = double.Parse(a.Substring(7), NumberStyles.Float, CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (dir == null || corpus == null)
            {
                Console.Error.WriteLine("нужны --dir=<каталог .n42> и --corpus=<каталог корпусных спектров>");
                return 2;
            }

            // ⛔ Обе карты примитивов ROI — ДО любого менеджера-одиночки (`T60`).
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            string asm = typeof(DocumentManager).Assembly.Location;
            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + asm);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(asm).ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("  sha256  " + Sha256(asm));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine("  культура прогона: " + Thread.CurrentThread.CurrentCulture.Name);
            Console.WriteLine();

            if (!Directory.Exists(dir)) { Console.Error.WriteLine("каталога нет: " + dir); return 2; }
            if (!Directory.Exists(corpus)) { Console.Error.WriteLine("каталога нет: " + corpus); return 2; }

            string[] files = Directory.GetFiles(dir, "*.n42");
            Array.Sort(files, StringComparer.Ordinal);
            if (files.Length == 0) { Console.Error.WriteLine("в каталоге нет ни одного .n42: " + dir); return 2; }

            Console.WriteLine("=== СВЕРКА .n42 С КОРПУСНЫМ ИСТОЧНИКОМ ===");
            Console.WriteLine("  .n42:   " + Path.GetFullPath(dir) + ", файлов " + files.Length);
            Console.WriteLine("  корпус: " + Path.GetFullPath(corpus));
            Console.WriteLine("  порог шкалы: " + tol.ToString("E3", CultureInfo.InvariantCulture) + " кэВ"
                              + "; порог времён: " + ttol.ToString("E3", CultureInfo.InvariantCulture) + " с"
                              + "; отсчёты — ПОБИТОВО, без порога");
            Console.WriteLine();

            List<string> rows = new List<string>();
            rows.Add("file,channels,h_keV,E0_src,E0_n42,dE0_keV,Emid_src,Emid_n42,Elast_src,Elast_n42,"
                     + "max_dE_keV,max_dE_channel,dE_over_h,counts_diff_channels,counts_max_abs,"
                     + "sum_src,sum_n42,time_max_abs_s,bg_live_diff_s,start_shift_s,"
                     + "counts_ok,scale_ok,time_ok,bg_ok,bg_live_ok,start_ok,verdict");

            int bad = 0, scaleBad = 0, countsBad = 0, timeBad = 0, bgBad = 0, startBad = 0, broke = 0, bgLiveBad = 0;
            foreach (string f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string row = One(name, f, Path.Combine(corpus, name + ".xml"));
                if (row == null) { broke++; bad++; continue; }
                rows.Add(row);
            }

            List<string> head = new List<string>(rows[0].Split(','));
            int iScale = head.IndexOf("scale_ok"), iCounts = head.IndexOf("counts_ok");
            int iTime = head.IndexOf("time_ok"), iBg = head.IndexOf("bg_ok");
            int iBgLive = head.IndexOf("bg_live_ok"), iStart = head.IndexOf("start_ok");
            int iVerdict = head.IndexOf("verdict");
            if (iScale < 0 || iCounts < 0 || iTime < 0 || iBg < 0 || iBgLive < 0 || iStart < 0 || iVerdict < 0)
            {
                Console.Error.WriteLine("⛔ шапка таблицы разошлась с ИТОГОМ");
                return 2;
            }
            for (int i = 1; i < rows.Count; i++)
            {
                string[] c = rows[i].Split(',');
                if (c[iScale] != "1") scaleBad++;
                if (c[iCounts] != "1") countsBad++;
                if (c[iTime] != "1") timeBad++;
                if (c[iBg] != "1") bgBad++;
                // «-» значит «фона нет и мерить нечего» — в счёт не идёт.
                if (c[iBgLive] == "0") bgLiveBad++;
                if (c[iStart] != "1") startBad++;
                if (c[iVerdict] != "OK") bad++;
            }

            if (dump != null)
            {
                using (StreamWriter w = new StreamWriter(dump, false, new UTF8Encoding(false)))
                {
                    w.NewLine = "\r\n";
                    foreach (string r in rows) w.WriteLine(r);
                }
                Console.WriteLine("таблица: " + Path.GetFullPath(dump));
            }

            Console.WriteLine();
            Console.WriteLine("ИТОГ: сверено " + (rows.Count - 1)
                              + ", дверь отказала у " + broke
                              + ", ШКАЛА разошлась у " + scaleBad
                              + ", ОТСЧЁТЫ разошлись у " + countsBad
                              + ", ВРЕМЕНА разошлись у " + timeBad
                              + ", ПРИЗНАК ФОНА потерян у " + bgBad
                              + ", ЖИВОЕ ВРЕМЯ ФОНА разошлось у " + bgLiveBad
                              + ", НАЧАЛО НАБОРА потеряно у " + startBad
                              + ", расхождений всего у " + bad);
            return bad == 0 ? 0 : 1;
        }

        static string One(string name, string n42, string src)
        {
            Console.WriteLine("--- " + name + " ---");
            if (!File.Exists(src))
            {
                Console.WriteLine("  ⛔ НЕТ КОРПУСНОГО ИСТОЧНИКА: " + src);
                Console.WriteLine();
                return null;
            }

            DocEnergySpectrum sdoc;
            try { sdoc = DocumentManager.GetInstance().OpenDocument(src); }
            catch (Exception ex)
            {
                Console.WriteLine("  ⛔ ИСТОЧНИК НЕ ОТКРЫЛСЯ: " + ex.GetType().Name + ": " + Flat(ex.Message));
                Console.WriteLine();
                return null;
            }
            if (sdoc == null)
            {
                Console.WriteLine("  ⛔ ИСТОЧНИК НЕ ОТКРЫЛСЯ: вернулся null");
                Console.WriteLine();
                return null;
            }

            DocEnergySpectrum ndoc = new DocEnergySpectrum();
            try { DocumentManager.GetInstance().ImportDocumentN42(ndoc, n42); }
            catch (Exception ex)
            {
                Console.WriteLine("  ⛔ ВВОЗ .n42 ОТКАЗАЛ: " + ex.GetType().Name + ": " + Flat(ex.Message));
                Console.WriteLine();
                return null;
            }

            ResultData srd = sdoc.ResultDataFile.ResultDataList[0];
            ResultData nrd = ndoc.ResultDataFile.ResultDataList[0];
            EnergySpectrum ses = srd.EnergySpectrum, nes = nrd.EnergySpectrum;
            EnergyCalibration scal = ses.EnergyCalibration, ncal = nes.EnergyCalibration;

            int chS = ses.NumberOfChannels, chN = nes.NumberOfChannels;
            bool chOk = chS == chN;

            // ШКАЛА. Меряется тем, чем шкала пользуется, — энергией канала, а не
            // коэффициентами: один и тот же сдвиг у полиномов разного порядка даёт
            // разные коэффициенты, и «относительная разница коэффициентов» про
            // величину ошибки не говорит ничего.
            double maxdE = 0.0; int maxCh = -1;
            int n = Math.Min(chS, chN);
            if (scal != null && ncal != null)
            {
                for (int i = 0; i < n; i++)
                {
                    double d = Math.Abs(scal.ChannelToEnergy(i) - ncal.ChannelToEnergy(i));
                    if (d > maxdE) { maxdE = d; maxCh = i; }
                }
            }
            else { maxdE = double.NaN; }
            double e0s = scal == null ? double.NaN : scal.ChannelToEnergy(0);
            double e0n = ncal == null ? double.NaN : ncal.ChannelToEnergy(0);
            int mid = n / 2, last = n - 1;
            double ems = scal == null ? double.NaN : scal.ChannelToEnergy(mid);
            double emn = ncal == null ? double.NaN : ncal.ChannelToEnergy(mid);
            double els = scal == null ? double.NaN : scal.ChannelToEnergy(last);
            double eln = ncal == null ? double.NaN : ncal.ChannelToEnergy(last);
            double h = (scal == null || last <= 0) ? double.NaN : (els - e0s) / last;
            bool scaleOk = !double.IsNaN(maxdE) && maxdE <= tol;

            // ОТСЧЁТЫ — побитово. Это главный контроль перевыгрузки.
            int diffCh = 0; long maxAbs = 0;
            int m = Math.Min(ses.Spectrum.Length, nes.Spectrum.Length);
            for (int i = 0; i < m; i++)
            {
                long d = (long)ses.Spectrum[i] - nes.Spectrum[i];
                if (d != 0) { diffCh++; if (Math.Abs(d) > maxAbs) maxAbs = Math.Abs(d); }
            }
            diffCh += Math.Abs(ses.Spectrum.Length - nes.Spectrum.Length);
            long sumS = ses.Spectrum.Sum(x => (long)x), sumN = nes.Spectrum.Sum(x => (long)x);
            bool countsOk = chOk && diffCh == 0 && sumS == sumN;

            double dTime = Math.Max(Math.Abs(ses.MeasurementTime - nes.MeasurementTime),
                                    Math.Abs(ses.LiveTime - nes.LiveTime));
            bool timeOk = dTime <= ttol;

            bool bgS = srd.BackgroundEnergySpectrum != null, bgN = nrd.BackgroundEnergySpectrum != null;
            bool bgOk = bgS == bgN;
            double bgLiveS = bgS ? srd.BackgroundEnergySpectrum.LiveTime : 0.0;
            double bgLiveN = bgN ? nrd.BackgroundEnergySpectrum.LiveTime : 0.0;
            double dBgLive = (bgS && bgN) ? Math.Abs(bgLiveS - bgLiveN) : 0.0;
            string bgLiveOk = !bgS ? "-" : (bgN && dBgLive <= ttol ? "1" : "0");

            // ⚠ Поля времени начала СРАЗУ ДВА, и это несимметричность самого
            //   разбора, а не описка: вывоз берёт `ResultData.StartTime`, ввоз
            //   кладёт прочитанное ещё и в `SampleInfo.Time`. Печатаются оба.
            double startShift = (nrd.StartTime - srd.StartTime).TotalSeconds;
            double sampleShift = (nrd.SampleInfo.Time - srd.StartTime).TotalSeconds;
            bool startOk = Math.Abs(startShift) < 1.0;

            bool ok = chOk && scaleOk && countsOk && timeOk && bgOk && startOk && bgLiveOk != "0";

            Console.WriteLine("  каналов: " + chS + " / " + chN + (chOk ? "  ✓" : "  ⛔ РАЗОШЛОСЬ"));
            Console.WriteLine("  шкала, кэВ:   канал      источник          .n42       разница");
            Console.WriteLine(Line(0, e0s, e0n));
            Console.WriteLine(Line(mid, ems, emn));
            Console.WriteLine(Line(last, els, eln));
            Console.WriteLine("    средний шаг h = " + h.ToString("0.####", CultureInfo.InvariantCulture)
                              + " кэВ; наибольшее |ΔE| по всем каналам "
                              + maxdE.ToString("E3", CultureInfo.InvariantCulture)
                              + " кэВ (канал " + maxCh + "), ΔE/h = "
                              + (h == 0.0 ? "-" : (maxdE / Math.Abs(h)).ToString("0.####", CultureInfo.InvariantCulture))
                              + (scaleOk ? "  ✓" : "  ⛔ ШКАЛА РАЗОШЛАСЬ"));
            Console.WriteLine("  отсчёты: сумма " + sumS + " / " + sumN
                              + ", различающихся каналов " + diffCh
                              + ", наибольшая разница " + maxAbs
                              + (countsOk ? "  ✓ ПОБИТОВО ТЕ ЖЕ" : "  ⛔ СОДЕРЖИМОЕ РАЗОШЛОСЬ"));
            Console.WriteLine("  времена: измерение "
                              + ses.MeasurementTime.ToString("0.######", CultureInfo.InvariantCulture) + " / "
                              + nes.MeasurementTime.ToString("0.######", CultureInfo.InvariantCulture)
                              + ", живое "
                              + ses.LiveTime.ToString("0.######", CultureInfo.InvariantCulture) + " / "
                              + nes.LiveTime.ToString("0.######", CultureInfo.InvariantCulture)
                              + ", остаток " + dTime.ToString("E3", CultureInfo.InvariantCulture) + " с"
                              + (timeOk ? "  ✓" : "  ⚠ РАЗОШЛИСЬ"));
            Console.WriteLine("  фон: " + (bgS ? "есть" : "нет") + " / " + (bgN ? "есть" : "нет")
                              + (bgOk ? "  ✓" : "  ⚠ ПРИЗНАК ФОНА ПОТЕРЯН")
                              + (bgS && bgN ? ("; живое фона "
                                    + bgLiveS.ToString("R", CultureInfo.InvariantCulture) + " / "
                                    + bgLiveN.ToString("R", CultureInfo.InvariantCulture)
                                    + ", остаток " + dBgLive.ToString("E3", CultureInfo.InvariantCulture) + " с"
                                    + (bgLiveOk == "1" ? "  ✓" : "  ⚠ РАЗОШЛОСЬ")) : ""));
            Console.WriteLine("  начало набора: " + srd.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                              + " / " + nrd.StartTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                              + ", сдвиг StartTime " + startShift.ToString("0.###", CultureInfo.InvariantCulture)
                              + " с, SampleInfo.Time " + sampleShift.ToString("0.###", CultureInfo.InvariantCulture)
                              + " с" + (startOk ? "  ✓" : "  ⚠ ВРЕМЯ НАЧАЛА ПОТЕРЯНО"));
            Console.WriteLine("  ПРИГОВОР: " + (ok ? "СОШЛОСЬ" : "РАСХОЖДЕНИЕ"));
            Console.WriteLine();

            return string.Join(",", new string[] {
                name,
                chS.ToString(CultureInfo.InvariantCulture),
                h.ToString("0.######", CultureInfo.InvariantCulture),
                e0s.ToString("0.######", CultureInfo.InvariantCulture),
                e0n.ToString("0.######", CultureInfo.InvariantCulture),
                (e0n - e0s).ToString("0.######", CultureInfo.InvariantCulture),
                ems.ToString("0.######", CultureInfo.InvariantCulture),
                emn.ToString("0.######", CultureInfo.InvariantCulture),
                els.ToString("0.######", CultureInfo.InvariantCulture),
                eln.ToString("0.######", CultureInfo.InvariantCulture),
                maxdE.ToString("E3", CultureInfo.InvariantCulture),
                maxCh.ToString(CultureInfo.InvariantCulture),
                (h == 0.0 ? "-" : (maxdE / Math.Abs(h)).ToString("0.####", CultureInfo.InvariantCulture)),
                diffCh.ToString(CultureInfo.InvariantCulture),
                maxAbs.ToString(CultureInfo.InvariantCulture),
                sumS.ToString(CultureInfo.InvariantCulture),
                sumN.ToString(CultureInfo.InvariantCulture),
                dTime.ToString("E3", CultureInfo.InvariantCulture),
                (bgS && bgN) ? dBgLive.ToString("E3", CultureInfo.InvariantCulture) : "-",
                startShift.ToString("0.###", CultureInfo.InvariantCulture),
                countsOk ? "1" : "0",
                scaleOk ? "1" : "0",
                timeOk ? "1" : "0",
                bgOk ? "1" : "0",
                bgLiveOk,
                startOk ? "1" : "0",
                ok ? "OK" : "DIFF" });
        }

        static string Line(int ch, double a, double b)
        {
            return "         " + ch.ToString(CultureInfo.InvariantCulture).PadLeft(6)
                   + a.ToString("0.0000", CultureInfo.InvariantCulture).PadLeft(14)
                   + b.ToString("0.0000", CultureInfo.InvariantCulture).PadLeft(14)
                   + (b - a).ToString("+0.0000;-0.0000;0.0000", CultureInfo.InvariantCulture).PadLeft(14);
        }

        static string Flat(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", " ").Replace("\n", " ");
        }

        static string Sha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
