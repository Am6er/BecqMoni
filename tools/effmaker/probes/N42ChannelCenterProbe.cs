using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace N42ChannelCenterProbe
{
    /// <summary>
    /// `AMBER73` (полоса П137, 22.09.2026) — СОГЛАШЕНИЕ «ИНДЕКС КАНАЛА» НА ВВОЗЕ N42:
    /// граница канала или его центр.
    ///
    /// ⛔ ЧТО МЕРИТСЯ, ПЯТЬЮ ЗАМЕРАМИ:
    ///
    /// 1. `--vendor=a|b`   СОГЛАШЕНИЕ САМОГО ПРИБОРА, без приложения: два файла ОДНОГО
    ///    измерения, один со списком `EnergyBoundaryValues`, другой с полиномом
    ///    `CoefficientValues`. Печатается наибольшее |edges[i] − poly(i)| и
    ///    |edges[i] − poly(i − 0.5)| по всем границам. Меньшее из двух и называет,
    ///    ЧТО прибор понимает под номером канала в полиноме: край или центр.
    /// 2. `--doors=<файлы>` ЧТО ЧИТАЕТ ПРИЛОЖЕНИЕ: обе двери (`ImportDocumentN42` и
    ///    `ImportDocumentSpecUtils`), их коэффициенты, наибольшее расхождение ДВЕРЕЙ
    ///    по каналам (`A253`: оно обязано остаться ничтожным) и — для файла с
    ///    границами — расхождение прочитанной шкалы с КРАЯМИ и с ЦЕНТРАМИ файла.
    /// 3. `--lines=<файл>@<E1,E2,…>` ЭНЕРГИЯ ПИКА ПРОТИВ ПАСПОРТНОЙ ЛИНИИ. Центроид
    ///    считается ДРОБНЫМ (центр тяжести по полувысоте с линейной подложкой), чтобы
    ///    округление `PeakDetector` до целого канала не съедало саму измеряемую
    ///    величину; печатается невязка в кэВ и в долях ширины канала h.
    /// 4. `--synth=<каталог>` ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ БЕЗ ПРИБОРНОЙ ОШИБКИ: сочинённый
    ///    файл с ТОЧНО известными краями (h = 0.5 кэВ) и линией, положенной по ИСТИННОЙ
    ///    энергии (счёт канала = интеграл гауссианы по [edges[i], edges[i+1]]). Истина
    ///    известна до кэВ, и невязка обязана быть 0, а не h/2. Кладётся дважды — тот же
    ///    спектр границами и полиномом, — чтобы оба положения мерились одной меркой.
    /// 5. `--roundtrip=<файл>` КРУГ ПРИЛОЖЕНИЯ: ввоз → `Util.ExportToN42` → ввоз.
    ///    Шкала обязана вернуться той же (`A148`), иначе правка ввоза, не поддержанная
    ///    вывозом, уводит собственные файлы приложения на полканала.
    ///
    /// Общие ключи: `--dir=<каталог>` (основа для имён), `--culture=<имя>`.
    /// Код возврата 1 — если названы ожидания (`--expect-*`) и они не сошлись.
    /// Проба безоконная.
    /// </summary>
    static class Program
    {
        static string dir = ".";
        static string culture = null;
        static readonly List<string> doors = new List<string>();
        static readonly List<string> vendorPairs = new List<string>();
        static readonly List<string> lineJobs = new List<string>();
        static readonly List<string> roundTrips = new List<string>();
        static string synthDir = null;
        static double expectSynthKev = double.NaN;
        static double expectDoorsKev = double.NaN;
        static int winChannels = 0;      // окно центроида в каналах, 0 — по доле
        static double relWin = 0.08;     // доля номера канала под полуокно
        static int bad = 0;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=")) dir = a.Substring(6);
                else if (a.StartsWith("--culture=")) culture = a.Substring(10);
                else if (a.StartsWith("--doors=")) doors.AddRange(a.Substring(8).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--vendor=")) vendorPairs.Add(a.Substring(9));
                else if (a.StartsWith("--lines=")) lineJobs.Add(a.Substring(8));
                else if (a.StartsWith("--roundtrip=")) roundTrips.Add(a.Substring(12));
                else if (a.StartsWith("--synth=")) synthDir = a.Substring(8);
                else if (a.StartsWith("--expect-synth-kev=")) expectSynthKev = double.Parse(a.Substring(19), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--expect-doors-kev=")) expectDoorsKev = double.Parse(a.Substring(19), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--win=")) winChannels = int.Parse(a.Substring(6), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--relwin=")) relWin = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            if (!string.IsNullOrEmpty(culture))
            {
                CultureInfo ci = new CultureInfo(culture);
                Thread.CurrentThread.CurrentCulture = ci;
                Thread.CurrentThread.CurrentUICulture = ci;
            }
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + typeof(DocumentManager).Assembly.Location);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location)
                                                 .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Console.WriteLine("  окна у приложения: " + AppUi.HasWindows);
            Console.WriteLine();

            foreach (string p in vendorPairs) Vendor(p);
            if (doors.Count > 0) Doors();
            foreach (string j in lineJobs) Lines(j);
            if (synthDir != null) Synth();
            foreach (string f in roundTrips) RoundTrip(f);

            Console.WriteLine();
            if (bad > 0)
            {
                Console.Error.WriteLine("ОТКАЗ: не сошлось ожиданий — " + bad.ToString(CultureInfo.InvariantCulture));
                return 1;
            }
            Console.WriteLine("ЗАМЕР СНЯТ.");
            return 0;
        }

        // ==================================================================
        // 1. СОГЛАШЕНИЕ ПРИБОРА — БЕЗ ПРИЛОЖЕНИЯ ВОВСЕ
        // ==================================================================

        static void Vendor(string spec)
        {
            string[] two = spec.Split('|');
            if (two.Length != 2) { Console.Error.WriteLine("--vendor=<файл границ>|<файл полинома>"); bad++; return; }
            string fb = Path.Combine(dir, two[0]);
            string fc = Path.Combine(dir, two[1]);
            Console.WriteLine("=== 1. СОГЛАШЕНИЕ ПРИБОРА: границы против полинома, одно измерение ===");
            Console.WriteLine("  границы: " + two[0]);
            Console.WriteLine("  полином: " + two[1]);
            double[] edges = ParseNumbers(Extract(File.ReadAllText(fb), "EnergyBoundaryValues"));
            double[] coef = ParseNumbers(Extract(File.ReadAllText(fc), "CoefficientValues"));
            if (edges == null || coef == null) { Console.Error.WriteLine("  ⛔ в файлах нет нужных списков"); bad++; return; }
            Console.WriteLine("  границ " + edges.Length.ToString(CultureInfo.InvariantCulture)
                              + ", коэффициентов " + coef.Length.ToString(CultureInfo.InvariantCulture)
                              + " [" + Join(coef) + "]");
            double maxEdge = 0.0, maxCentre = 0.0;
            int argEdge = 0, argCentre = 0;
            for (int i = 0; i < edges.Length; i++)
            {
                double dEdge = Math.Abs(edges[i] - Poly(coef, i));
                double dCentre = Math.Abs(edges[i] - Poly(coef, i - 0.5));
                if (dEdge > maxEdge) { maxEdge = dEdge; argEdge = i; }
                if (dCentre > maxCentre) { maxCentre = dCentre; argCentre = i; }
            }
            Console.WriteLine("  наибольшее |edges[i] − poly(i)|      = " + F(maxEdge) + " кэВ  (i = " + argEdge + ")"
                              + "   ← полином читает индекс КРАЕМ");
            Console.WriteLine("  наибольшее |edges[i] − poly(i−0.5)|  = " + F(maxCentre) + " кэВ  (i = " + argCentre + ")"
                              + "   ← полином читает индекс ЦЕНТРОМ");
            Console.WriteLine("  ВЫВОД: прибор понимает номер канала в полиноме "
                              + (maxEdge < maxCentre ? "КРАЕМ (нижней границей)" : "ЦЕНТРОМ")
                              + "; отношение невязок "
                              + F(maxCentre / Math.Max(maxEdge, 1e-300)));
            Console.WriteLine();
        }

        // ==================================================================
        // 2. ЧТО ЧИТАЮТ ДВЕ ДВЕРИ
        // ==================================================================

        static void Doors()
        {
            Console.WriteLine("=== 2. ДВЕ ДВЕРИ НА ОДНОМ ФАЙЛЕ ===");
            foreach (string name in doors)
            {
                string f = Path.Combine(dir, name);
                Console.WriteLine("  " + name);
                PolynomialEnergyCalibration a = Import(f, true, out int chA, out string errA);
                PolynomialEnergyCalibration b = Import(f, false, out int chB, out string errB);
                if (a == null || b == null)
                {
                    Console.WriteLine("    ⛔ дверь отказала: N42 «" + (errA ?? "—") + "», SpecUtils «" + (errB ?? "—") + "»");
                    bad++;
                    continue;
                }
                Console.WriteLine("    дверь N42:       порядок " + a.PolynomialOrder + " [" + Join(a.Coefficients) + "]");
                Console.WriteLine("    дверь SpecUtils: порядок " + b.PolynomialOrder + " [" + Join(b.Coefficients) + "]");
                int n = Math.Min(chA, chB);
                double maxDoors = 0.0;
                for (int ch = 0; ch < n; ch++)
                {
                    double d = Math.Abs(a.ChannelToEnergy(ch) - b.ChannelToEnergy(ch));
                    if (d > maxDoors) maxDoors = d;
                }
                Console.WriteLine("    ⛔ `A253` СХОЖДЕНИЕ ДВЕРЕЙ: наибольшее расхождение по каналам 0…"
                                  + (n - 1).ToString(CultureInfo.InvariantCulture) + " = " + F(maxDoors) + " кэВ");
                if (!double.IsNaN(expectDoorsKev) && maxDoors > expectDoorsKev)
                {
                    Console.WriteLine("    ⛔ НЕ СОШЛОСЬ: ждали не больше " + F(expectDoorsKev) + " кэВ");
                    bad++;
                }
                double[] edges = ParseNumbers(Extract(File.ReadAllText(f), "EnergyBoundaryValues"));
                if (edges != null && edges.Length >= chA)
                {
                    double maxE = 0.0, maxC = 0.0;
                    for (int ch = 0; ch < chA; ch++)
                    {
                        double centre = ch + 1 < edges.Length ? 0.5 * (edges[ch] + edges[ch + 1]) : double.NaN;
                        double d1 = Math.Abs(a.ChannelToEnergy(ch) - edges[ch]);
                        if (d1 > maxE) maxE = d1;
                        if (!double.IsNaN(centre))
                        {
                            double d2 = Math.Abs(a.ChannelToEnergy(ch) - centre);
                            if (d2 > maxC) maxC = d2;
                        }
                    }
                    Console.WriteLine("    прочитанная шкала против КРАЁВ файла:   наибольшее " + F(maxE) + " кэВ");
                    Console.WriteLine("    прочитанная шкала против ЦЕНТРОВ файла: наибольшее " + F(maxC) + " кэВ");
                    Console.WriteLine("    ⇒ ChannelToEnergy(ch) стоит на "
                                      + (maxE < maxC ? "ГРАНИЦЕ канала ch" : "ЦЕНТРЕ канала ch"));
                }
            }
            Console.WriteLine();
        }

        // ==================================================================
        // 3. ЭНЕРГИЯ ПИКА ПРОТИВ ПАСПОРТНОЙ ЛИНИИ
        // ==================================================================

        static void Lines(string job)
        {
            int at = job.LastIndexOf('@');
            if (at < 0) { Console.Error.WriteLine("--lines=<файл>@<E1,E2,…>"); bad++; return; }
            string name = job.Substring(0, at);
            string[] es = job.Substring(at + 1).Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string f = Path.Combine(dir, name);
            Console.WriteLine("=== 3. ЭНЕРГИЯ ПИКА ПРОТИВ ПАСПОРТНОЙ ЛИНИИ: " + name + " ===");
            int[] spectrum;
            PolynomialEnergyCalibration cal = ImportFull(f, true, out spectrum, out string err);
            if (cal == null) { Console.WriteLine("  ⛔ ввоз отказал: " + err); bad++; return; }
            Console.WriteLine("  каналов " + spectrum.Length + ", шкала порядок " + cal.PolynomialOrder + " [" + Join(cal.Coefficients) + "]");
            Console.WriteLine("  линия, кэВ | канал(дробн.) | E(центроид), кэВ | невязка, кэВ | h, кэВ | невязка/h");
            double sumRatio = 0.0; int nRatio = 0;
            foreach (string t in es)
            {
                double eRef = double.Parse(t, CultureInfo.InvariantCulture);
                double chGuess = cal.EnergyToChannel(eRef, spectrum.Length);
                double centroid = Centroid(spectrum, chGuess);
                if (double.IsNaN(centroid)) { Console.WriteLine("  " + F(eRef) + " | пик не найден"); continue; }
                double eFound = cal.ChannelToEnergy(centroid);
                int ci = (int)Math.Round(centroid);
                double h = cal.ChannelToEnergy(Math.Min(ci + 1, spectrum.Length - 1)) - cal.ChannelToEnergy(Math.Max(ci - 1, 0));
                h = h / 2.0;
                double resid = eFound - eRef;
                Console.WriteLine("  " + F(eRef) + " | " + F(centroid) + " | " + F(eFound) + " | " + F(resid)
                                  + " | " + F(h) + " | " + F(resid / h));
                sumRatio += resid / h; nRatio++;
            }
            if (nRatio > 0)
            {
                Console.WriteLine("  СРЕДНЯЯ невязка по " + nRatio + " линиям, в долях ширины канала: " + F(sumRatio / nRatio));
                Console.WriteLine("  (−0.5 — шкала стоит на КРАЯХ, а читается центрами; 0 — согласована)");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Дробный центроид пика около <paramref name="chGuess"/>. Целого канала
        /// здесь нет нарочно: мерится как раз полканала, и округление до канала
        /// (как в <c>PeakDetector.cs:886</c>) съело бы саму измеряемую величину.
        ///
        /// Два прохода: (1) вершина в окне ±<c>relWin</c>·канал, подложка по концам
        /// окна, полувысота даёт [l, r]; (2) подложка перекладывается по ТРЁМ
        /// каналам у каждого из найденных концов и центр тяжести берётся по
        /// надподложечной части [l, r]. Окно пропорционально номеру канала, потому
        /// что у сцинтиллятора ширина пика — доля энергии, а не число каналов:
        /// окно в 1 % шкалы на 2614 кэВ уже́ самого пика, и полувысота упиралась
        /// бы в край окна.
        /// </summary>
        static double Centroid(int[] y, double chGuess)
        {
            int n = y.Length;
            int c0 = (int)Math.Round(chGuess);
            if (c0 < 2 || c0 > n - 3) return double.NaN;
            int w = winChannels > 0 ? winChannels : Math.Max(6, (int)Math.Round(relWin * c0));
            int lo = Math.Max(1, c0 - w), hi = Math.Min(n - 2, c0 + w);
            int top = lo;
            for (int i = lo; i <= hi; i++) if (y[i] > y[top]) top = i;
            double bl = y[lo], bh = y[hi];
            Func<int, double> baseline = i => bl + (bh - bl) * (i - lo) / (double)Math.Max(1, hi - lo);
            double peak = y[top] - baseline(top);
            if (peak <= 0.0) return double.NaN;
            int l = top, r = top;
            while (l > lo && y[l] - baseline(l) > 0.5 * peak) l--;
            while (r < hi && y[r] - baseline(r) > 0.5 * peak) r++;
            if (r - l < 2) return double.NaN;
            double bl2 = (y[Math.Max(0, l - 1)] + y[l] + y[Math.Min(n - 1, l + 1)]) / 3.0;
            double bh2 = (y[Math.Max(0, r - 1)] + y[r] + y[Math.Min(n - 1, r + 1)]) / 3.0;
            Func<int, double> base2 = i => bl2 + (bh2 - bl2) * (i - l) / (double)Math.Max(1, r - l);
            double num = 0.0, den = 0.0;
            for (int i = l; i <= r; i++)
            {
                double v = y[i] - base2(i);
                if (v <= 0.0) continue;
                num += i * v; den += v;
            }
            return den > 0.0 ? num / den : double.NaN;
        }

        // ==================================================================
        // 4. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ НА СОЧИНЁННОМ ФАЙЛЕ
        // ==================================================================

        const int SynthChannels = 2048;
        const double SynthE0 = -3.0;   // нижняя граница канала 0
        const double SynthH = 0.5;     // ширина канала, кэВ — ТОЧНО

        static void Synth()
        {
            Directory.CreateDirectory(synthDir);
            double[] lines = new double[] { 500.0, 900.0 };
            double[] edges = new double[SynthChannels + 1];
            for (int i = 0; i <= SynthChannels; i++) edges[i] = SynthE0 + SynthH * i;
            int[] counts = new int[SynthChannels];
            for (int i = 0; i < SynthChannels; i++) counts[i] = 200;                 // ровная подложка
            foreach (double e0 in lines)
            {
                double sigma = 3.0;                                                  // кэВ
                for (int i = 0; i < SynthChannels; i++)
                {
                    // ИНТЕГРАЛ гауссианы по ИСТИННОМУ промежутку канала: центр тяжести
                    // спектра тем самым стоит ровно на e0, и ошибка измерения — только
                    // от соглашения о номере канала, а не от раскладки.
                    double p = 0.5 * (Erf((edges[i + 1] - e0) / (sigma * Math.Sqrt(2.0)))
                                      - Erf((edges[i] - e0) / (sigma * Math.Sqrt(2.0))));
                    counts[i] += (int)Math.Round(2000000.0 * p);
                }
            }
            string edgeText = Join(edges, " ");
            StringBuilder cd = new StringBuilder();
            for (int i = 0; i < SynthChannels; i++) { if (i > 0) cd.Append(' '); cd.Append(counts[i].ToString(CultureInfo.InvariantCulture)); }
            string body = cd.ToString();

            // Полином, ТОЧНО равный краям: E = -3.0 + 0.5·ch.
            string polyText = SynthE0.ToString("R", CultureInfo.InvariantCulture) + " " + SynthH.ToString("R", CultureInfo.InvariantCulture);

            Write("synth_boundary.n42", Head("p137-b")
                + "  <EnergyCalibration id=\"EC1\">\r\n"
                + "    <EnergyBoundaryValues>" + edgeText + "</EnergyBoundaryValues>\r\n"
                + "  </EnergyCalibration>\r\n" + Tail(body));
            Write("synth_poly.n42", Head("p137-p")
                + "  <EnergyCalibration id=\"EC1\">\r\n"
                + "    <CoefficientValues>" + polyText + "</CoefficientValues>\r\n"
                + "  </EnergyCalibration>\r\n" + Tail(body));

            Console.WriteLine("=== 4. ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: сочинённый файл, истина известна до кэВ ===");
            Console.WriteLine("  каналов " + SynthChannels + ", края E = " + F(SynthE0) + " + " + F(SynthH) + "·ch (h = " + F(SynthH) + " кэВ)");
            Console.WriteLine("  линии положены по ИСТИННОЙ энергии: " + Join(lines, ", ") + " кэВ");
            Console.WriteLine("  файл        | дверь     | линия | канал(дробн.) | E(центроид) | невязка, кэВ | невязка/h");
            double worst = 0.0;
            foreach (string name in new string[] { "synth_boundary.n42", "synth_poly.n42" })
            {
                foreach (bool n42Door in new bool[] { true, false })
                {
                    int[] sp;
                    PolynomialEnergyCalibration cal = ImportFull(Path.Combine(synthDir, name), n42Door, out sp, out string err);
                    if (cal == null) { Console.WriteLine("  " + name + " | " + (n42Door ? "N42" : "SpecUtils") + " | ⛔ отказ: " + err); bad++; continue; }
                    foreach (double e0 in lines)
                    {
                        double centroid = Centroid(sp, cal.EnergyToChannel(e0, sp.Length));
                        double eFound = cal.ChannelToEnergy(centroid);
                        double resid = eFound - e0;
                        if (Math.Abs(resid) > worst) worst = Math.Abs(resid);
                        Console.WriteLine("  " + name.PadRight(19) + " | " + (n42Door ? "N42      " : "SpecUtils") + " | "
                                          + F(e0) + " | " + F(centroid) + " | " + F(eFound) + " | " + F(resid)
                                          + " | " + F(resid / SynthH));
                    }
                }
            }
            Console.WriteLine("  НАИБОЛЬШАЯ невязка по всем четырём прогонам: " + F(worst) + " кэВ (h/2 = " + F(SynthH / 2.0) + ")");
            if (!double.IsNaN(expectSynthKev) && worst > expectSynthKev)
            {
                Console.WriteLine("  ⛔ НЕ СОШЛОСЬ: ждали не больше " + F(expectSynthKev) + " кэВ");
                bad++;
            }
            Console.WriteLine();
        }

        static double Erf(double x)
        {
            // Абрамовиц–Стиган 7.1.26, точность 1.5e-7 — довольно: раскладка пика
            // здесь нужна симметричной, а не точной до бита.
            double sign = x < 0 ? -1.0 : 1.0;
            x = Math.Abs(x);
            double t = 1.0 / (1.0 + 0.3275911 * x);
            double yv = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
            return sign * yv;
        }

        // ==================================================================
        // 5. КРУГ ПРИЛОЖЕНИЯ: ВВОЗ → ВЫВОЗ → ВВОЗ
        // ==================================================================

        static void RoundTrip(string name)
        {
            Console.WriteLine("=== 5. КРУГ «ВВОЗ → ВЫВОЗ → ВВОЗ»: " + name + " ===");
            string f = Path.Combine(dir, name);
            DocEnergySpectrum doc = new DocEnergySpectrum();
            TextWriter realErr = Console.Error;
            Console.SetError(new StringWriter());
            try { DocumentManager.GetInstance().ImportDocumentN42(doc, f); }
            catch (Exception ex) { Console.SetError(realErr); Console.WriteLine("  ⛔ первый ввоз отказал: " + ex.Message); bad++; return; }
            finally { Console.SetError(realErr); }
            PolynomialEnergyCalibration first = doc.ActiveResultData.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            string outPath = Path.Combine(Path.GetTempPath(), "p137_roundtrip.n42");
            try
            {
                BecquerelMonitor.N42.Util util = new BecquerelMonitor.N42.Util();
                BecquerelMonitor.N42.RadInstrumentData rad = util.ExportToN42(doc);
                XmlSerializer xs = new XmlSerializer(typeof(BecquerelMonitor.N42.RadInstrumentData));
                XmlWriterSettings st = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
                using (XmlWriter w = XmlWriter.Create(outPath, st)) { xs.Serialize(w, rad); w.Flush(); }
            }
            catch (Exception ex) { Console.WriteLine("  ⛔ вывоз отказал: " + ex.Message); bad++; return; }
            DocEnergySpectrum doc2 = new DocEnergySpectrum();
            Console.SetError(new StringWriter());
            try { DocumentManager.GetInstance().ImportDocumentN42(doc2, outPath); }
            catch (Exception ex) { Console.SetError(realErr); Console.WriteLine("  ⛔ второй ввоз отказал: " + ex.Message); bad++; return; }
            finally { Console.SetError(realErr); }
            PolynomialEnergyCalibration second = doc2.ActiveResultData.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            Console.WriteLine("  до вывоза:  порядок " + first.PolynomialOrder + " [" + Join(first.Coefficients) + "]");
            Console.WriteLine("  после круга: порядок " + second.PolynomialOrder + " [" + Join(second.Coefficients) + "]");
            int n = doc.ActiveResultData.EnergySpectrum.NumberOfChannels;
            double max = 0.0;
            for (int ch = 0; ch < n; ch++)
            {
                double d = Math.Abs(first.ChannelToEnergy(ch) - second.ChannelToEnergy(ch));
                if (d > max) max = d;
            }
            Console.WriteLine("  наибольшее расхождение шкалы по каналам 0…" + (n - 1) + ": " + F(max) + " кэВ");
            Console.WriteLine();
        }

        // ==================================================================
        // ОБЩЕЕ
        // ==================================================================

        static PolynomialEnergyCalibration Import(string path, bool n42Door, out int channels, out string err)
        {
            int[] sp;
            PolynomialEnergyCalibration cal = ImportFull(path, n42Door, out sp, out err);
            channels = sp == null ? 0 : sp.Length;
            return cal;
        }

        static PolynomialEnergyCalibration ImportFull(string path, bool n42Door, out int[] spectrum, out string err)
        {
            spectrum = null; err = null;
            DocEnergySpectrum doc = new DocEnergySpectrum();
            TextWriter realErr = Console.Error;
            Console.SetError(new StringWriter());
            try
            {
                if (n42Door) DocumentManager.GetInstance().ImportDocumentN42(doc, path);
                else DocumentManager.GetInstance().ImportDocumentSpecUtils(doc, path, 3600);
            }
            catch (Exception ex) { err = ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " "); return null; }
            finally { Console.SetError(realErr); }
            ResultData rd = doc.ActiveResultData;
            if (rd == null || rd.EnergySpectrum == null) { err = "документа нет"; return null; }
            spectrum = rd.EnergySpectrum.Spectrum;
            PolynomialEnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration as PolynomialEnergyCalibration;
            if (cal == null) { err = "шкала не полиномиальная"; return null; }
            cal.CheckCalibration(channels: rd.EnergySpectrum.NumberOfChannels);
            return cal;
        }

        static double Poly(double[] c, double x)
        {
            double v = 0.0;
            for (int i = c.Length - 1; i >= 0; i--) v = v * x + c[i];
            return v;
        }

        static string Extract(string xml, string tag)
        {
            Match m = Regex.Match(xml, "<" + tag + "[^>]*>([^<]*)</" + tag + ">");
            return m.Success ? m.Groups[1].Value : null;
        }

        static double[] ParseNumbers(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] t = text.Split(new char[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) return null;
            double[] v = new double[t.Length];
            for (int i = 0; i < t.Length; i++)
                if (!double.TryParse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
            return v;
        }

        static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        static string Join(double[] v) { return Join(v, ", "); }

        static string Join(double[] v, string sep)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(sep); sb.Append(v[i].ToString("R", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        static void Write(string name, string xml)
        {
            File.WriteAllText(Path.Combine(synthDir, name), xml, new UTF8Encoding(false));
            Console.WriteLine("  положен вход " + name);
        }

        // Образец файла — тот же, что у N42BoundaryProbeG6 (и у N42RoundTripProbe).
        static string Head(string tag)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n"
                 + "<RadInstrumentData xmlns=\"http://physics.nist.gov/N42/2011/N42\" n42DocUUID=\"probe-"
                 + tag + "\">\r\n"
                 + "  <RadInstrumentInformation id=\"RadInstrument\">\r\n"
                 + "    <RadInstrumentManufacturerName>PROBE</RadInstrumentManufacturerName>\r\n"
                 + "    <RadInstrumentModelName>P137</RadInstrumentModelName>\r\n"
                 + "    <RadInstrumentClassCode>Radionuclide Identifier</RadInstrumentClassCode>\r\n"
                 + "    <RadInstrumentVersion>\r\n"
                 + "      <RadInstrumentComponentName>Hardware</RadInstrumentComponentName>\r\n"
                 + "      <RadInstrumentComponentVersion>1</RadInstrumentComponentVersion>\r\n"
                 + "    </RadInstrumentVersion>\r\n"
                 + "  </RadInstrumentInformation>\r\n";
        }

        static string Tail(string channelData)
        {
            return "  <RadMeasurement id=\"M1\">\r\n"
                 + "    <MeasurementClassCode>Foreground</MeasurementClassCode>\r\n"
                 + "    <StartDateTime>2026-09-22T12:00:00Z</StartDateTime>\r\n"
                 + "    <RealTimeDuration>PT300S</RealTimeDuration>\r\n"
                 + "    <Spectrum id=\"S1\" radDetectorInformationReference=\"D1\" energyCalibrationReference=\"EC1\">\r\n"
                 + "      <LiveTimeDuration>PT295S</LiveTimeDuration>\r\n"
                 + "      <ChannelData>" + channelData + "</ChannelData>\r\n"
                 + "    </Spectrum>\r\n"
                 + "  </RadMeasurement>\r\n"
                 + "</RadInstrumentData>\r\n";
        }
    }
}
