using BecquerelMonitor;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// ЕДИНАЯ КАЛИБРОВКА ЭНЕРГИИ И FWHM (`AMBER276`, 29.10.2026). Amber, консоль, дословно:
    /// «Объединить калибровки по энергии и FWHM в один процесс на одной вкладке, сейчас их
    /// две. Выбор пика для линии калибровки делать как сейчас это делается в FWHM. Таблица
    /// калибровки - единая. В файл спектра помимо калибровки fwhm теперь попадает и
    /// калибровка по энергии.» и «текущее местоположение коэффициентов калибровки ни в
    /// коем случае нельзя трогать».
    ///
    /// Проба собирается против НОВОЙ сборки, а режимы save, dump и fit идут и из каталога
    /// ПРЕЖНЕЙ (всё, чего у прежней нет, — отражением).
    ///
    ///   unifiedcalibrationprobea276 --mode=format --corpus=&lt;спектр&gt; --lines=E1,E2,E3 --tmp=&lt;каталог&gt;
    ///       (новая сборка) открыть — сохранить настоящими OpenDocument/SaveDocument: N0 —
    ///       без строк, N1 — с линиями единой калибровки (без расчёта), N1r — N1 открыт и
    ///       сохранён ещё раз. Судья элемента EnergyCalibration (место и байты) — N1
    ///       против N0; ⛔ положительный контроль — подменённая цифра коэффициента и
    ///       перенесённый элемент ОБЯЗАНЫ быть отвергнуты. Строки — в секции FWHM, всё
    ///       прочее в файле то же. Старый файл (строки без признака линии) — строки видны
    ///       в таблице единой вкладки, серые, в подгонку энергии не идут.
    ///   ... --mode=save --in=&lt;файл&gt; --out=&lt;файл&gt;   открыть и сохранить (любая сборка)
    ///   ... --mode=dump --in=&lt;файл&gt;                 что сборка прочла: шкалы энергии, кривая FWHM, строки
    ///   ... --mode=fit --corpus=&lt;спектр&gt; --lines=... --out=&lt;csv&gt; --tmp=&lt;каталог&gt;
    ///       одни и те же пики (выделение ±1.5 FWHM вокруг линии, та же арифметика, что
    ///       выделение мышью на сыром спектре) — в ПРЕЖНИЕ две вкладки (прежняя сборка:
    ///       «Выполнить калибровку» энергии и «Калибровать» FWHM) либо в единую (новая:
    ///       «Рассчитать»), четыре варианта: три вида кривой FWHM без весов и
    ///       квадратный корень полинома с весами. В CSV — коэффициенты шкалы и кривой
    ///       побитово (формат R) и форма пика; две CSV сравниваются строка в строку.
    ///   ... --mode=leave --corpus=&lt;спектр&gt; --tmp=&lt;каталог&gt;
    ///       выход из поля коэффициента, которое не правили: шкала побитово та же,
    ///       документ не изменён (прежняя сборка — положительный контроль: код 1).
    ///   ... --mode=pick --corpus=&lt;спектр&gt; --lines=E1,E2 --far=E3 --tmp=&lt;каталог&gt;
    ///       (остаток `AMBER276`, решение Amber вопросником 29.10.2026, дословно: «Только
    ///       с введённой энергией (Рекомендую)») строки, выделенные щелчком (настоящий
    ///       обработчик выделения), без ввода энергии — серые, в подгонку энергии не
    ///       идут: «Рассчитать» шкалу побитово не трогает. ⛔ Положительный контроль — те
    ///       же строки линиями (прежнее поведение): шкала обязана уйти, сдвиг печатается
    ///       в канале линии E3. Затем энергии вводятся настоящей правкой ячейки — строки
    ///       чёрные, идут в подгонку, шкала проходит через введённые энергии. Подписанный
    ///       и неподписанный пик таблицы найденных пиков — линия и опора.
    ///
    /// Окон проба не поднимает (окно сравнения форм пика у прежней сборки закрывает
    /// таймер). Запускать из каталога сборки проб (там поставочный `config\`).
    /// Коды: 0 — сошлось; 1 — не сошлось; 2 — ключи/файлы; 3 — сбой.
    /// </summary>
    static class UnifiedCalibrationProbeA276
    {
        static int bad;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = Inv;
            var opt = new Dictionary<string, string>();
            foreach (string a in args)
            {
                int eq = a.IndexOf('=');
                if (!a.StartsWith("--", StringComparison.Ordinal) || eq < 0)
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
                opt[a.Substring(2, eq - 2)] = a.Substring(eq + 1);
            }
            string mode;
            if (!opt.TryGetValue("mode", out mode))
            {
                Console.Error.WriteLine("нужен --mode=format|save|dump|fit");
                return 2;
            }
            try
            {
                GlobalConfigManager.GetInstance();
                DeviceConfigManager.GetInstance();
                NuclideDefinitionManager.GetInstance();
                switch (mode)
                {
                    case "format": return Format(Need(opt, "corpus"), Lines(Need(opt, "lines")), Need(opt, "tmp"));
                    case "save": return Save(Need(opt, "in"), Need(opt, "out"));
                    case "dump": return Dump(Need(opt, "in"));
                    case "fit": return Fit(Need(opt, "corpus"), Lines(Need(opt, "lines")), Need(opt, "out"), Need(opt, "tmp"));
                    case "leave": return Leave(Need(opt, "corpus"), Need(opt, "tmp"));
                    case "pick": return PickRule(Need(opt, "corpus"), Lines(Need(opt, "lines")), double.Parse(Need(opt, "far"), Inv), Need(opt, "tmp"));
                    default:
                        Console.Error.WriteLine("неизвестный режим: " + mode);
                        return 2;
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("СБОЙ: " + Inner(ex));
                return 3;
            }
        }

        static string Need(Dictionary<string, string> opt, string key)
        {
            string v;
            if (!opt.TryGetValue(key, out v) || string.IsNullOrEmpty(v))
            {
                throw new ArgumentException("нужен --" + key + "=");
            }
            return v;
        }

        static double[] Lines(string text)
        {
            return text.Split(',').Select(s => double.Parse(s, Inv)).ToArray();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  format
        // ══════════════════════════════════════════════════════════════════════

        static int Format(string corpus, double[] lines, string tmp)
        {
            Directory.CreateDirectory(tmp);
            string c0 = Path.Combine(tmp, "C.xml");
            File.Copy(corpus, c0, true);

            // N0: открыть — сохранить без строк.
            string n0 = Path.Combine(tmp, "N0.xml");
            File.Copy(c0, n0, true);
            DocEnergySpectrum d0 = DocumentManager.GetInstance().OpenDocument(n0);
            Check("N0 открыт", d0 != null);
            DocumentManager.GetInstance().SaveDocument(d0);
            Close(d0);

            // N1: N0 + линии единой калибровки (пики — как у fit, расчёта нет).
            string n1 = Path.Combine(tmp, "N1.xml");
            File.Copy(n0, n1, true);
            DocEnergySpectrum d1 = DocumentManager.GetInstance().OpenDocument(n1);
            ResultData rd1 = d1.ActiveResultData;
            Check("у спектра есть кривая FWHM (строкам есть где жить в файле)", rd1.FwhmCalibration != null);
            List<Pick> picks = Picks(rd1, lines);
            Check("пиков столько, сколько линий: " + picks.Count, picks.Count == lines.Length);
            foreach (Pick p in picks)
            {
                CalibrationPeak row = CalibrationPeak.ClonePeaks(new List<CalibrationPeak> { p.Peak })[0];
                row.Energy = p.LineEnergy;
                row.EnergyLine = true;
                row.Count = p.Count;
                rd1.CalibrationLines.Add(row);
            }
            DocumentManager.GetInstance().SaveDocument(d1);
            Close(d1);

            string x0 = File.ReadAllText(n0);
            string x1 = File.ReadAllText(n1);
            Console.WriteLine();
            Console.WriteLine("=== 1. элемент EnergyCalibration: N1 против N0 ===");
            string why = EnergyElementsDiffer(x0, x1);
            Check("элемент EnergyCalibration на месте и побайтно тот же" + (why != null ? " (" + why + ")" : ""), why == null);
            foreach (Elem e in EnergyElements(x1))
            {
                Console.WriteLine("  {0} после <{1}>: sha256 {2}, {3} байт", e.Path, e.Before, e.Sha, e.Text.Length);
            }

            // ⛔ положительный контроль судьи
            string tampered = TamperCoefficient(x1);
            string why2 = EnergyElementsDiffer(x0, tampered);
            Check("контроль: подменённая цифра коэффициента отвергнута" + (why2 != null ? " (" + why2 + ")" : ""), why2 != null && tampered != x1);
            string moved = MoveEnergyElement(x1);
            string why3 = EnergyElementsDiffer(x0, moved);
            Check("контроль: перенесённый элемент отвергнут" + (why3 != null ? " (" + why3 + ")" : ""), why3 != null && moved != x1);

            Console.WriteLine();
            Console.WriteLine("=== 2. строки — в секции FWHM, всё прочее то же ===");
            Check("в N1 линий <EnergyLine>true</EnergyLine>: " + Count(x1, "<EnergyLine>true</EnergyLine>"),
                  Count(x1, "<EnergyLine>true</EnergyLine>") == lines.Length);
            Check("в N0 элементов EnergyLine нет", Count(x0, "<EnergyLine>") == 0);
            string s0 = StripFwhmPeaks(x0), s1 = StripFwhmPeaks(x1);
            Check("N1 без опор секции FWHM = N0 без опор (" + s1.Length + " / " + s0.Length + " знаков)", s0 == s1);
            Match peaks = Regex.Match(x1, "<CalibrationPeaks>.*?</CalibrationPeaks>", RegexOptions.Singleline);
            Check("опоры лежат внутри секции FWHM", peaks.Success && IsInsideFwhm(x1, peaks.Index));
            Console.WriteLine("  секция FWHM N1 (первая строка):");
            Console.WriteLine("  " + FirstPeakXml(x1));

            Console.WriteLine();
            Console.WriteLine("=== 3. круг: N1 открыт и сохранён ещё раз ===");
            string n1r = Path.Combine(tmp, "N1r.xml");
            File.Copy(n1, n1r, true);
            DocEnergySpectrum d2 = DocumentManager.GetInstance().OpenDocument(n1r);
            List<CalibrationPeak> back = d2.ActiveResultData.CalibrationLines;
            bool same = back.Count == picks.Count;
            for (int i = 0; same && i < back.Count; i++)
            {
                same = back[i].EnergyLine && back[i].Count == picks[i].Count && back[i].Channel == picks[i].Peak.Channel
                       && Bits(back[i].Energy) == Bits(picks[i].LineEnergy) && Bits(back[i].FWHM) == Bits(picks[i].Peak.FWHM);
            }
            Check("строки прочитаны побитово: канал, энергия линии, FWHM, признак, отсчёты", same);
            DocumentManager.GetInstance().SaveDocument(d2);
            Close(d2);
            Check("N1r = N1 побайтно", File.ReadAllBytes(n1r).SequenceEqual(File.ReadAllBytes(n1)));

            Console.WriteLine();
            Console.WriteLine("=== 4. файл прежней версии: строки видны в единой таблице, серые ===");
            string old = Path.Combine(tmp, "OLD.xml");
            File.WriteAllText(old, Regex.Replace(Regex.Replace(x1, @"\s*<EnergyLine>true</EnergyLine>", ""), @"\s*<Count>\d+</Count>", ""));
            DocEnergySpectrum d3 = DocumentManager.GetInstance().OpenDocument(old);
            ResultData rd3 = d3.ActiveResultData;
            Check("опоры старого файла читаются: " + rd3.CalibrationLines.Count, rd3.CalibrationLines.Count == lines.Length);
            Check("и все — не линии энергии", rd3.CalibrationLines.All(l => !l.EnergyLine));
            MainForm form = Shell(d3);
            DCEnergyCalibrationView view = new DCEnergyCalibrationView(form);
            Call(view, "SetEnergyCalibration", rd3.EnergySpectrum.EnergyCalibration, rd3.EnergySpectrum.EnergyCalibration);
            Call(view, "UpdateFwhmCalibration", true);
            var model = (XPTable.Models.TableModel)Field(view, "tableModel1");
            Check("строк в таблице единой вкладки: " + model.Rows.Count, model.Rows.Count == lines.Length);
            bool grey = true;
            foreach (XPTable.Models.Row r in model.Rows)
            {
                grey &= r.Cells[2].ForeColor == System.Drawing.SystemColors.GrayText;
            }
            Check("энергия всех строк — серая", grey);
            List<CalibrationPoint> energyPoints = (List<CalibrationPoint>)typeof(DCFwhmCalibrationView)
                .GetMethod("EnergyPoints", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { rd3.CalibrationLines });
            Check("в подгонку энергии из старого файла не идёт ни одной точки", energyPoints.Count == 0);
            Close(d3);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        sealed class Elem
        {
            public string Path;
            public string Before;
            public string Text;
            public string Sha;
        }

        static List<Elem> EnergyElements(string xml)
        {
            var list = new List<Elem>();
            foreach (Match m in Regex.Matches(xml, "<EnergyCalibration[ >].*?</EnergyCalibration>", RegexOptions.Singleline))
            {
                string head = xml.Substring(0, m.Index);
                list.Add(new Elem { Path = PathAt(head), Before = PreviousSibling(head), Text = m.Value, Sha = Sha(m.Value) });
            }
            return list;
        }

        /// <summary>null — элементы те же по месту и байтам; иначе — чем расходятся.</summary>
        static string EnergyElementsDiffer(string a, string b)
        {
            List<Elem> ea = EnergyElements(a), eb = EnergyElements(b);
            if (ea.Count == 0)
            {
                return "в образце элементов нет";
            }
            if (ea.Count != eb.Count)
            {
                return "элементов " + ea.Count + " против " + eb.Count;
            }
            for (int i = 0; i < ea.Count; i++)
            {
                if (ea[i].Path != eb[i].Path || ea[i].Before != eb[i].Before)
                {
                    return "элемент " + (i + 1) + " сдвинут: " + eb[i].Path + " после <" + eb[i].Before + ">";
                }
                if (ea[i].Sha != eb[i].Sha)
                {
                    return "элемент " + (i + 1) + " другой побайтно";
                }
            }
            return null;
        }

        /// <summary>Цепочка открытых элементов к месту (грубый разбор по тексту, файл пишет XmlWriter).</summary>
        static string PathAt(string head)
        {
            var stack = new List<string>();
            foreach (Match m in Regex.Matches(head, @"<(/?)([A-Za-z_][\w\.\-]*)([^>]*?)(/?)>"))
            {
                if (m.Groups[1].Value == "/")
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                }
                else if (m.Groups[4].Value != "/")
                {
                    stack.Add(m.Groups[2].Value);
                }
            }
            return string.Join("/", stack);
        }

        static string PreviousSibling(string head)
        {
            // Последний закрывшийся на том же уровне элемент.
            int depth = 0;
            MatchCollection all = Regex.Matches(head, @"<(/?)([A-Za-z_][\w\.\-]*)([^>]*?)(/?)>");
            for (int i = all.Count - 1; i >= 0; i--)
            {
                Match m = all[i];
                bool close = m.Groups[1].Value == "/";
                bool empty = m.Groups[4].Value == "/";
                if (close)
                {
                    if (depth == 0) return m.Groups[2].Value;
                    depth++;
                }
                else if (empty)
                {
                    if (depth == 0) return m.Groups[2].Value;
                }
                else
                {
                    if (depth == 0) return "(первый)";
                    depth--;
                }
            }
            return "(первый)";
        }

        static string TamperCoefficient(string xml)
        {
            Match m = Regex.Match(xml, "<EnergyCalibration[ >].*?<Coefficient>(-?\\d)", RegexOptions.Singleline);
            if (!m.Success) return xml;
            Group g = m.Groups[1];
            char last = g.Value[g.Value.Length - 1];
            char swapped = last == '9' ? '8' : (char)(last + 1);
            return xml.Substring(0, g.Index + g.Length - 1) + swapped + xml.Substring(g.Index + g.Length);
        }

        static string MoveEnergyElement(string xml)
        {
            Match m = Regex.Match(xml, "\\s*<EnergyCalibration[ >].*?</EnergyCalibration>", RegexOptions.Singleline);
            if (!m.Success) return xml;
            string rest = xml.Remove(m.Index, m.Length);
            int at = rest.IndexOf("</EnergySpectrum>", m.Index, StringComparison.Ordinal);
            return at < 0 ? xml : rest.Insert(at, m.Value.TrimStart() + Environment.NewLine);
        }

        static string StripFwhmPeaks(string xml)
        {
            return Regex.Replace(xml, "<CalibrationPeaks>.*?</CalibrationPeaks>|<CalibrationPeaks />", "<CalibrationPeaks/>", RegexOptions.Singleline);
        }

        static bool IsInsideFwhm(string xml, int index)
        {
            string head = xml.Substring(0, index);
            string path = PathAt(head);
            return path.EndsWith("SimpleSqrtFwhmCalibration", StringComparison.Ordinal)
                   || path.EndsWith("SqrtFwhmCalibration", StringComparison.Ordinal)
                   || path.EndsWith("PowerFwhmCalibration", StringComparison.Ordinal);
        }

        static string FirstPeakXml(string xml)
        {
            Match m = Regex.Match(xml, "<Peak>.*?</Peak>", RegexOptions.Singleline);
            return m.Success ? Regex.Replace(m.Value, "\\s+", " ") : "(нет)";
        }

        static int Count(string text, string what)
        {
            int n = 0, at = 0;
            while ((at = text.IndexOf(what, at, StringComparison.Ordinal)) >= 0) { n++; at += what.Length; }
            return n;
        }

        static string Sha(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  save, dump
        // ══════════════════════════════════════════════════════════════════════

        static int Save(string input, string output)
        {
            File.Copy(input, output, true);
            DocEnergySpectrum doc = DocumentManager.GetInstance().OpenDocument(output);
            if (doc == null)
            {
                Console.Error.WriteLine("не открылся: " + input);
                return 1;
            }
            bool ok = DocumentManager.GetInstance().SaveDocument(doc);
            Console.WriteLine("сохранён: {0} ({1})", output, ok);
            return ok ? 0 : 1;
        }

        static int Dump(string input)
        {
            string copy = Path.Combine(Path.GetTempPath(), "a276_dump_" + Guid.NewGuid().ToString("N") + ".xml");
            File.Copy(input, copy, true);
            DocEnergySpectrum doc = DocumentManager.GetInstance().OpenDocument(copy);
            if (doc == null)
            {
                Console.Error.WriteLine("не открылся: " + input);
                return 1;
            }
            ResultData rd = doc.ActiveResultData;
            Console.WriteLine("сборка: " + typeof(ResultData).Assembly.GetName().Version);
            Console.WriteLine("шкала спектра: " + Cal(rd.EnergySpectrum.EnergyCalibration));
            Console.WriteLine("шкала фона: " + (rd.BackgroundEnergySpectrum != null ? Cal(rd.BackgroundEnergySpectrum.EnergyCalibration) : "(нет)"));
            FwhmCalibration f = rd.FwhmCalibration;
            Console.WriteLine("кривая FWHM: " + Curve(f));
            if (f != null)
            {
                foreach (CalibrationPeak p in f.CalibrationPeaks)
                {
                    Console.WriteLine("  опора: канал {0}, энергия {1}, FWHM {2}", p.Channel, R(p.Energy), R(p.FWHM));
                }
            }
            Close(doc);
            File.Delete(copy);
            return 0;
        }

        static string Cal(EnergyCalibration c)
        {
            PolynomialEnergyCalibration p = c as PolynomialEnergyCalibration;
            if (p == null) return c == null ? "(нет)" : c.GetType().Name;
            return "полином " + p.PolynomialOrder + ": " + string.Join("; ", p.Coefficients.Select(R));
        }

        static string Curve(FwhmCalibration f)
        {
            if (f == null) return "(нет)";
            return string.Format(Inv, "{0} [{1}] форма {2} ({3}; {4}; {5}; {6}) χ² {7}; {8}; {9}; {10}",
                f.GetType().Name, string.Join("; ", f.Coefficients.Select(R)), f.PeakType,
                R(f.ExpGaussExpLeftTail), R(f.ExpGaussExpRightTail), R(f.VoigtSigma), R(f.VoigtGamma),
                R(f.GaussianChi2Total), R(f.ExpGaussExpChi2Total), R(f.VoigtChi2Total), R(f.Chi2pNdp));
        }

        // ══════════════════════════════════════════════════════════════════════
        //  fit
        // ══════════════════════════════════════════════════════════════════════

        sealed class Pick
        {
            public double LineEnergy;
            public CalibrationPeak Peak;
            public int Start, End, Count;
        }

        /// <summary>
        /// Пики линий: окно ±1.5 FWHM кривой спектра вокруг канала линии по шкале спектра
        /// (не меньше ±3), FWHM и центроид — как выделение мышью на сыром спектре
        /// (<c>EnergySpectrumView.EnsureSelectionAnalytics</c>: <c>CalculateFWHM</c>, центр
        /// масс ядра, округление), форма пика — <c>CalcPeakFitValues</c>. Код у прежней и
        /// новой сборки один.
        /// </summary>
        static List<Pick> Picks(ResultData rd, double[] lines)
        {
            EnergySpectrum es = rd.EnergySpectrum;
            EnergyCalibration cal = es.EnergyCalibration;
            var picks = new List<Pick>();
            foreach (double e in lines)
            {
                int ch0 = 0;
                double best = double.MaxValue;
                for (int ch = 0; ch < es.NumberOfChannels; ch++)
                {
                    double d = Math.Abs(cal.ChannelToEnergy(ch) - e);
                    if (d < best) { best = d; ch0 = ch; }
                }
                double fw = rd.FwhmCalibration != null ? rd.FwhmCalibration.ChannelToFwhm(ch0) : 10.0;
                int half = Math.Max(3, (int)Math.Ceiling(1.5 * fw));
                int start = Math.Max(0, ch0 - half), end = Math.Min(es.NumberOfChannels - 1, ch0 + half);
                EnergyResolutionResult r = EnergyResolutionCalculator.CalculateFWHM(es, start, end);
                if (r == null || !(r.Resolution > 0))
                {
                    Console.WriteLine("  линия {0}: ширина не измерена — пропущена", R(e));
                    continue;
                }
                double centroid = new SpectrumAriphmetics().FindCentroid(es, (int)r.MaxChannel, start, end, true);
                var peak = new CalibrationPeak
                {
                    Channel = (int)Math.Round(centroid),
                    Energy = cal.ChannelToEnergy(centroid),
                    FWHM = r.RightChannel - r.LeftChannel
                };
                SpectrumAriphmetics sa = new SpectrumAriphmetics(es);
                peak = sa.CalcPeakFitValues(peak, start, end);
                sa.Dispose();
                picks.Add(new Pick { LineEnergy = e, Peak = peak, Start = start, End = end, Count = es.Spectrum[peak.Channel] });
            }
            return picks;
        }

        static int Fit(string corpus, double[] lines, string outCsv, string tmp)
        {
            Directory.CreateDirectory(tmp);
            bool unified = typeof(DCFwhmCalibrationView).GetMethod("RunCalibration", F) != null;
            Console.WriteLine("сборка: {0}, вкладка: {1}", typeof(ResultData).Assembly.GetName().Version,
                              unified ? "единая (новая)" : "две прежние");
            string copy = Path.Combine(tmp, "fit_" + Path.GetFileName(corpus));
            File.Copy(corpus, copy, true);
            DocEnergySpectrum doc = DocumentManager.GetInstance().OpenDocument(copy);
            ResultData rd = doc.ActiveResultData;
            EnergyCalibration original = rd.EnergySpectrum.EnergyCalibration.Clone();
            List<Pick> picks = Picks(rd, lines);
            MainForm form = Shell(doc);
            Timer closer = new Timer { Interval = 150 };
            closer.Tick += (s, e) =>
            {
                foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
                {
                    if (f.Modal) f.Close();
                }
            };
            closer.Start();

            var rows = new List<string>();
            rows.Add("spectrum,variant,peaks,energy_order,energy_coefficients,fwhm_curve");
            foreach (string pickLine in picks.Select(p => string.Format(Inv, "  пик: линия {0} кэВ, канал {1}, FWHM {2}, окно {3}…{4}, отсчётов {5}",
                                                                         R(p.LineEnergy), p.Peak.Channel, R(p.Peak.FWHM), p.Start, p.End, p.Count)))
            {
                Console.WriteLine(pickLine);
            }
            string[] variants = { "SimpleSqrt", "Sqrt", "Power", "Sqrt+weights" };
            foreach (string variant in variants)
            {
                rd.EnergySpectrum.EnergyCalibration = original.Clone();
                FwhmCalibration start = variant.StartsWith("SimpleSqrt", StringComparison.Ordinal) ? (FwhmCalibration)new SimpleSqrtFwhmCalibration()
                                      : variant.StartsWith("Power", StringComparison.Ordinal) ? (FwhmCalibration)new PowerFwhmCalibration()
                                      : (FwhmCalibration)new SqrtFwhmCalibration();
                start.CalibrationPeaks = CalibrationPeak.ClonePeaks(picks.Select(p => p.Peak).ToList());
                rd.FwhmCalibration = start;
                rd.CalibrationPoints = new List<CalibrationPoint>();
                bool weights = variant.EndsWith("+weights", StringComparison.Ordinal);
                if (unified)
                {
                    PropertyInfo line = typeof(CalibrationPeak).GetProperty("EnergyLine");
                    PropertyInfo count = typeof(CalibrationPeak).GetProperty("Count");
                    for (int i = 0; i < picks.Count; i++)
                    {
                        CalibrationPeak row = rd.FwhmCalibration.CalibrationPeaks[i];
                        row.Energy = picks[i].LineEnergy;
                        line.SetValue(row, true);
                        count.SetValue(row, picks[i].Count);
                    }
                    DCEnergyCalibrationView view = new DCEnergyCalibrationView(form);
                    Call(view, "SetEnergyCalibration", rd.EnergySpectrum.EnergyCalibration, rd.EnergySpectrum.EnergyCalibration);
                    Call(view, "UpdateFwhmCalibration", true);
                    ((CheckBox)Field(view, "checkBox2")).Checked = weights;
                    bool done = (bool)Call(view, "RunCalibration");
                    Console.WriteLine("  {0}: «Рассчитать» {1}", variant, done ? "принят" : "ОТКАЗ");
                }
                else
                {
                    DCEnergyCalibrationView energy = new DCEnergyCalibrationView(form);
                    Call(energy, "SetEnergyCalibration", rd.EnergySpectrum.EnergyCalibration, rd.EnergySpectrum.EnergyCalibration);
                    foreach (Pick p in picks)
                    {
                        Call(energy, "AddCalibration", p.Peak.Channel, (decimal)p.LineEnergy, p.Count);
                    }
                    ((CheckBox)Field(energy, "checkBox2")).Checked = weights;
                    Call(energy, "button7_Click", null, EventArgs.Empty);
                    DCFwhmCalibrationView fwhm = new DCFwhmCalibrationView(form);
                    Call(fwhm, "UpdateFwhmCalibration", true);
                    Call(fwhm, "ExecuteCalibrationButton_Click", null, EventArgs.Empty);
                }
                PolynomialEnergyCalibration got = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
                rows.Add(string.Format(Inv, "{0},{1},{2},{3},{4},{5}", Path.GetFileNameWithoutExtension(corpus), variant, picks.Count,
                                       got.PolynomialOrder, string.Join(";", got.Coefficients.Select(R)), Curve(rd.FwhmCalibration).Replace(",", " ")));
            }
            closer.Stop();
            File.WriteAllLines(outCsv, rows);
            foreach (string r in rows) Console.WriteLine(r);
            Close(doc);
            return 0;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  leave
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Находка экраном 29.10.2026: выход из поля коэффициента, которое не правили, —
        /// шкала побитово та же, документ не изменён. Коэффициент 0.00091681334111664073
        /// поле печатает пятнадцатью знаками (0.000916813341116641), и разбор этого текста
        /// даёт соседний double. Прежняя сборка — положительный контроль: у неё шкала
        /// меняется, и документ помечается изменённым.
        /// </summary>
        static int Leave(string corpus, string tmp)
        {
            Directory.CreateDirectory(tmp);
            string copy = Path.Combine(tmp, "leave_" + Path.GetFileName(corpus));
            File.Copy(corpus, copy, true);
            DocEnergySpectrum doc = DocumentManager.GetInstance().OpenDocument(copy);
            ResultData rd = doc.ActiveResultData;
            var cal = new PolynomialEnergyCalibration
            {
                PolynomialOrder = 3,
                Coefficients = new[] { -7.5, 2.7594512663313253, 0.00091681334111664073, -1.0121718693992851E-06 }
            };
            rd.EnergySpectrum.EnergyCalibration = cal.Clone();
            MainForm form = Shell(doc);
            DCEnergyCalibrationView view = new DCEnergyCalibrationView(form);
            Call(view, "SetEnergyCalibration", cal, cal);
            doc.Dirty = false;
            string shown = ((TextBox)Field(view, "numericUpDown1")).Text;
            Call(view, "numericUpDown1_Leave", null, EventArgs.Empty);
            PolynomialEnergyCalibration after = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
            bool same = Bits(after.Coefficients[2]) == Bits(cal.Coefficients[2]);
            Console.WriteLine("поле: «{0}»; коэффициент после выхода: {1} ({2}); документ изменён: {3}",
                              shown, R(after.Coefficients[2]), same ? "тот же" : "ДРУГОЙ", doc.Dirty);
            Close(doc);
            return same && !doc.Dirty ? 0 : 1;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  pick — «только с введённой энергией»
        // ══════════════════════════════════════════════════════════════════════

        static int PickRule(string corpus, double[] lines, double far, string tmp)
        {
            Directory.CreateDirectory(tmp);
            string copy = Path.Combine(tmp, "pick_" + Path.GetFileName(corpus));
            File.Copy(corpus, copy, true);
            DocEnergySpectrum doc = DocumentManager.GetInstance().OpenDocument(copy);
            ResultData rd = doc.ActiveResultData;
            PolynomialEnergyCalibration original = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration.Clone();
            int farChannel = NearestChannel(rd.EnergySpectrum, original, far);
            List<Pick> picks = Picks(rd, lines);
            Check("пиков столько, сколько линий: " + picks.Count, picks.Count == lines.Length);
            MainForm form = Shell(doc);
            Timer closer = new Timer { Interval = 150 };
            closer.Tick += (s, e) =>
            {
                foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
                {
                    if (f.Modal) f.Close();
                }
            };
            closer.Start();
            DCEnergyCalibrationView view = new DCEnergyCalibrationView(form);
            IntPtr handle = view.Handle; // BeginInvoke правки ячейки
            rd.CalibrationLines.Clear();
            Call(view, "SetEnergyCalibration", original, original);
            Call(view, "UpdateFwhmCalibration", true);
            var model = (XPTable.Models.TableModel)Field(view, "tableModel1");
            Console.WriteLine("шкала спектра: " + Cal(original) + "; линия {0} кэВ — канал {1}", R(far), farChannel);

            Console.WriteLine();
            Console.WriteLine("=== 1. выделение щелчком без ввода энергии ===");
            foreach (Pick p in picks)
            {
                SetField(view, "peakPickupProcessing", true);
                Call(view, "EnergySpectrumView_PeakPickuped", null,
                     new PeakPickupedEventArgs(p.Peak.Channel, p.Peak.Energy, p.Peak.FWHM, p.Start, p.End));
            }
            List<CalibrationPeak> rows = rd.CalibrationLines;
            Check("строк в таблице: " + rows.Count + ", в модели вида: " + model.Rows.Count, rows.Count == picks.Count && model.Rows.Count == picks.Count);
            foreach (CalibrationPeak r in rows)
            {
                Console.WriteLine("  строка: канал {0}, энергия {1} (по шкале), FWHM {2}, линия {3}", r.Channel, R(r.Energy), R(r.FWHM), r.EnergyLine);
            }
            Check("ни одна выделенная строка — не линия энергии", rows.All(r => !r.EnergyLine));
            Check("энергия всех строк — серая", model.Rows.Cast<XPTable.Models.Row>().All(r => r.Cells[2].ForeColor == System.Drawing.SystemColors.GrayText));
            Check("в подгонку энергии не идёт ни одной точки", EnergyPointsOf(rows).Count == 0);
            bool run1 = (bool)Call(view, "RunCalibration");
            Console.WriteLine("  «Рассчитать»: {0}; строки после — тот же список: {1}", run1 ? "принят" : "ОТКАЗ", ReferenceEquals(rows, rd.CalibrationLines));
            rows = rd.CalibrationLines;
            PolynomialEnergyCalibration after = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
            Check("«Рассчитать»: шкала энергии побитово та же (" + Cal(after) + ")", SameCal(after, original));
            Console.WriteLine("  кривая FWHM после «Рассчитать»: " + Curve(rd.FwhmCalibration));

            Console.WriteLine();
            Console.WriteLine("=== 2. ⛔ положительный контроль: те же строки ЛИНИЯМИ (прежнее поведение) ===");
            foreach (CalibrationPeak r in rows)
            {
                r.EnergyLine = true;
                r.Count = rd.EnergySpectrum.Spectrum[r.Channel];
            }
            Call(view, "UpdateFwhmCalibration", true);
            bool run2 = (bool)Call(view, "RunCalibration");
            Console.WriteLine("  «Рассчитать»: {0}", run2 ? "принят" : "ОТКАЗ");
            rows = rd.CalibrationLines;
            PolynomialEnergyCalibration oldWay = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
            double shift = oldWay.ChannelToEnergy(farChannel) - original.ChannelToEnergy(farChannel);
            Console.WriteLine("  шкала: {0}; сдвиг в канале {1}: {2} кэВ", Cal(oldWay), farChannel, shift.ToString("0.0", Inv));
            Check("контроль: строки-линии без ввода уводят шкалу (сдвиг " + shift.ToString("0.0", Inv) + " кэВ)", !SameCal(oldWay, original) && Math.Abs(shift) > 1.0);
            // назад: шкала спектра и строки — как после выделения
            rd.EnergySpectrum.EnergyCalibration = original.Clone();
            Call(view, "SetEnergyCalibration", original, original);
            foreach (CalibrationPeak r in rows)
            {
                r.EnergyLine = false;
                r.Count = 0;
            }
            Call(view, "UpdateFwhmCalibration", true);

            Console.WriteLine();
            Console.WriteLine("=== 3. энергии введены правкой ячейки «Энергия» ===");
            var table = (XPTable.Models.Table)Field(view, "CollectedPeaksTable");
            for (int i = 0; i < picks.Count; i++)
            {
                var editor = new XPTable.Editors.TextCellEditor();
                editor.TextBox.Text = R(picks[i].LineEnergy);
                XPTable.Models.Cell cell = model.Rows[i].Cells[2];
                var args = new XPTable.Events.CellEditEventArgs(cell, editor, table, i, 2, System.Drawing.Rectangle.Empty);
                Call(view, "CollectedPeaksTable_EditingStopped", table, args);
                Application.DoEvents();
            }
            Check("все строки — линии энергии", rows.All(r => r.EnergyLine));
            Check("энергия всех строк — не серая", model.Rows.Cast<XPTable.Models.Row>().All(r => r.Cells[2].ForeColor != System.Drawing.SystemColors.GrayText));
            Check("отсчёты канала у строк — из спектра", rows.All(r => r.Count == rd.EnergySpectrum.Spectrum[r.Channel]));
            Check("в подгонку энергии идёт " + EnergyPointsOf(rows).Count + " точек", EnergyPointsOf(rows).Count == picks.Count);
            bool run3 = (bool)Call(view, "RunCalibration");
            Console.WriteLine("  «Рассчитать»: {0}", run3 ? "принят" : "ОТКАЗ");
            rows = rd.CalibrationLines;
            PolynomialEnergyCalibration entered = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
            double worst = rows.Max(r => Math.Abs(entered.ChannelToEnergy(r.Channel) - r.Energy));
            Console.WriteLine("  шкала: {0}; наибольший промах в каналах линий {1} кэВ; в канале {2}: {3} кэВ",
                              Cal(entered), worst.ToString("0.000000", Inv), farChannel, entered.ChannelToEnergy(farChannel).ToString("0.0", Inv));
            Check("шкала подогнана по введённым энергиям", !SameCal(entered, original) && worst < 1e-6);

            Console.WriteLine();
            Console.WriteLine("=== 4. «Добавить в калибровку» таблицы найденных пиков ===");
            rd.EnergySpectrum.EnergyCalibration = original.Clone();
            Call(view, "SetEnergyCalibration", original, original);
            rd.CalibrationLines.Clear();
            Call(view, "UpdateFwhmCalibration", true);
            Pick a = picks[0], b = picks[picks.Count - 1];
            var signed = new Peak { Channel = a.Peak.Channel, Energy = original.ChannelToEnergy(a.Peak.Channel), FWHM = a.Peak.FWHM };
            signed.Nuclide = new NuclideDefinition { Energy = a.LineEnergy };
            var unsigned = new Peak { Channel = b.Peak.Channel, Energy = original.ChannelToEnergy(b.Peak.Channel), FWHM = b.Peak.FWHM };
            bool added1 = (bool)Call(view, "AddDetectedPeak", signed, a.LineEnergy);
            CalibrationPeak rs = rd.CalibrationLines.LastOrDefault();
            bool added2 = (bool)Call(view, "AddDetectedPeak", unsigned, unsigned.Energy);
            CalibrationPeak ru = rd.CalibrationLines.FirstOrDefault(r => !ReferenceEquals(r, rs));
            foreach (CalibrationPeak r in rd.CalibrationLines)
            {
                Console.WriteLine("  строка: канал {0}, энергия {1}, FWHM {2}, линия {3}", r.Channel, R(r.Energy), R(r.FWHM), r.EnergyLine);
            }
            Check("обе строки добавлены", added1 && added2 && rs != null && ru != null);
            Check("подписанный пик — линия энергии (энергия линии " + R(a.LineEnergy) + ")", rs != null && rs.EnergyLine && Bits(rs.Energy) == Bits(a.LineEnergy));
            Check("неподписанный пик — опора FWHM, не линия", ru != null && !ru.EnergyLine);
            Check("в подгонку энергии идёт 1 точка", EnergyPointsOf(rd.CalibrationLines).Count == 1);

            closer.Stop();
            Close(doc);
            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad);
            return bad == 0 ? 0 : 1;
        }

        static List<CalibrationPoint> EnergyPointsOf(List<CalibrationPeak> lines)
        {
            return (List<CalibrationPoint>)typeof(DCFwhmCalibrationView)
                .GetMethod("EnergyPoints", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { lines });
        }

        static bool SameCal(PolynomialEnergyCalibration a, PolynomialEnergyCalibration b)
        {
            return a.PolynomialOrder == b.PolynomialOrder && a.Coefficients.Length == b.Coefficients.Length
                   && a.Coefficients.Select(Bits).SequenceEqual(b.Coefficients.Select(Bits));
        }

        static int NearestChannel(EnergySpectrum es, EnergyCalibration cal, double e)
        {
            int ch0 = 0;
            double best = double.MaxValue;
            for (int ch = 0; ch < es.NumberOfChannels; ch++)
            {
                double d = Math.Abs(cal.ChannelToEnergy(ch) - e);
                if (d < best) { best = d; ch0 = ch; }
            }
            return ch0;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  мелочь
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>Оболочка главного окна без окна: только то, что зовут вкладки калибровки.</summary>
        static MainForm Shell(DocEnergySpectrum doc)
        {
            MainForm form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            SetField(form, "activeDocument", doc);
            SetField(form, "documentManager", DocumentManager.GetInstance());
            SetField(form, "dcResultViewList", new List<DCResultView>());
            SetField(form, "toolStripStatusLabel1", new ToolStripStatusLabel());
            try { SetField(form, "dcPeakDetectionView", new DCPeakDetectionView(form)); } catch (Exception) { }
            return form;
        }

        static void Close(DocEnergySpectrum doc)
        {
            if (doc != null)
            {
                DocumentManager.GetInstance().DocumentList.Remove(doc);
            }
        }

        static object Call(object target, string name, params object[] args)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                foreach (MethodInfo m in t.GetMethods(F | BindingFlags.DeclaredOnly))
                {
                    if (m.Name == name && m.GetParameters().Length == args.Length)
                    {
                        try { return m.Invoke(target, args); }
                        catch (TargetInvocationException ex) { throw ex.InnerException; }
                    }
                }
            }
            throw new MissingMethodException(target.GetType().Name, name);
        }

        static object Field(object o, string name)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, F | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
            }
            throw new MissingFieldException(o.GetType().Name, name);
        }

        static void SetField(object o, string name, object value)
        {
            for (Type t = o.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, F | BindingFlags.DeclaredOnly);
                if (f != null) { f.SetValue(o, value); return; }
            }
            throw new MissingFieldException(o.GetType().Name, name);
        }

        static void Check(string what, bool ok)
        {
            Console.WriteLine("  {0} {1}", ok ? "да " : "НЕТ", what);
            if (!ok) bad++;
        }

        static long Bits(double x)
        {
            return BitConverter.DoubleToInt64Bits(x);
        }

        static string R(double x)
        {
            return x.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Inner(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex.GetType().Name + ": " + ex.Message + " @ " + (ex.StackTrace ?? "").Split('\n')[0].Trim();
        }
    }
}
