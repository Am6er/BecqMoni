using BecquerelMonitor;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Serialization;

namespace CalibPeaksProbeP170
{
    /// <summary>
    /// Полоса П170 (28.09.2026) — пять строк ревизии «дубль 4»: `AMBER110`,
    /// `AMBER111`, `AMBER112`, `AMBER113`, `AMBER127`. Одна проба меряет ОБЕ
    /// сборки (до и после правки): всё новое зовётся отражением, и на старой
    /// сборке печатается «метода нет».
    ///
    /// Ключи (каждый — отдельное плечо; можно несколько):
    /// `--stab=&lt;xml&gt;` — `AMBER110`: стабилизатор пиков на спектре,
    ///   сочинённом по калибровке корпусного `xml` (одна опора 661.66, одна
    ///   опора 1460.82, две опоры — контроль «многоточечная ветвь не тронута»),
    ///   при уходе усиления g = 1.000 и 1.020. Печатаются ошибки новой шкалы на
    ///   59.54, 661.66, 1460.82, 2614.51 кэВ и коэффициенты «R».
    /// `--peak=&lt;xml&gt;` — `AMBER111`: `PeakDetector.CreatePeak` отражением
    ///   на дробных центроидах (без уточнения) — |E_пика − E(центроид)|; затем
    ///   настоящий `DetectPeak` на самом спектре — список пиков «канал, энергия».
    /// `--maxch=&lt;каталог&gt;` — `AMBER112`: сочиняет 16384-канальные SPE и CSV
    ///   SpecUtils, импортирует дверью SpecUtils и дверью «CSV с энергиями», печатает
    ///   E(k) у k = 8000, 12000, 16383 против полинома.
    /// `--csv=&lt;xml&gt;,&lt;каталог&gt;` — `AMBER113`: CSV в формате SpecUtils
    ///   `Measurement::write_csv` («Energy, Data», энергия — НИЖНИЙ КРАЙ, float
    ///   6 значащих) по калибровке `xml`, и свой ECSV (центры) — оба дверью
    ///   SpecUtils; печатается E_дверь(k) − P(k) по всем каналам.
    /// `--dt` — `AMBER127`: мёртвое время AtomSpectra: умолчание конфигурации,
    ///   живое время при нём и разбор ответа `-inf` (отражением).
    /// `--corpus-lt=&lt;каталог spectra&gt;` — `AMBER127`: спектры AtomSpectra
    ///   корпуса с LT = T и их скорость счёта.
    ///
    /// Код 1 — не сошлось ожидание, записанное плечом (`--expect-fixed`
    /// включает ожидания ПОСЛЕ правки; без него проба только печатает).
    /// </summary>
    static class Program
    {
        static int bad = 0;
        static bool expectFixed = false;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            List<string[]> jobs = new List<string[]>();
            foreach (string a in args)
            {
                if (a == "--expect-fixed") expectFixed = true;
                else if (a.StartsWith("--stab=")) jobs.Add(new[] { "stab", a.Substring(7) });
                else if (a.StartsWith("--peak=")) jobs.Add(new[] { "peak", a.Substring(7) });
                else if (a.StartsWith("--maxch=")) jobs.Add(new[] { "maxch", a.Substring(8) });
                else if (a.StartsWith("--csv=")) jobs.Add(new[] { "csv", a.Substring(6) });
                else if (a == "--dt") jobs.Add(new[] { "dt", "" });
                else if (a.StartsWith("--corpus-lt=")) jobs.Add(new[] { "clt", a.Substring(12) });
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            string exe = typeof(DocumentManager).Assembly.Location;
            Console.WriteLine("=== СБОРКА === " + exe + "  ("
                              + File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")");
            Console.WriteLine();
            foreach (string[] j in jobs)
            {
                try
                {
                    if (j[0] == "stab") Stab(j[1]);
                    else if (j[0] == "peak") PeakArm(j[1]);
                    else if (j[0] == "maxch") MaxCh(j[1]);
                    else if (j[0] == "csv") Csv(j[1]);
                    else if (j[0] == "dt") DeadTime();
                    else if (j[0] == "clt") CorpusLiveTime(j[1]);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  ⛔ ПЛЕЧО УПАЛО: " + ex.GetType().Name + ": " + One(ex.Message));
                    bad++;
                }
                Console.WriteLine();
            }
            if (bad > 0)
            {
                Console.Error.WriteLine("ОТКАЗ: не сошлось ожиданий — " + bad.ToString(Inv));
                return 1;
            }
            Console.WriteLine("ЗАМЕР СНЯТ.");
            return 0;
        }

        // ------------------------------------------------------------ общее

        static ResultData LoadXml(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ResultDataFile rdf = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                return rdf.ResultDataList[0];
            }
        }

        static PolynomialEnergyCalibration RefCal(string xml, out int n)
        {
            ResultData rd = LoadXml(xml);
            n = rd.EnergySpectrum.NumberOfChannels;
            PolynomialEnergyCalibration p = (PolynomialEnergyCalibration)rd.EnergySpectrum.EnergyCalibration;
            p.CheckCalibration(n);
            return p;
        }

        // Полином ПРЯМО по коэффициентам — без зажимов ChannelToEnergy.
        static double Poly(double[] c, double x)
        {
            double v = 0.0;
            for (int i = c.Length - 1; i >= 0; i--) v = v * x + c[i];
            return v;
        }

        // Обратный полином бисекцией по [lo, hi] (монотонная шкала).
        static double Inverse(double[] c, double e, double lo, double hi)
        {
            for (int it = 0; it < 200; it++)
            {
                double mid = 0.5 * (lo + hi);
                if (Poly(c, mid) < e) lo = mid; else hi = mid;
            }
            return 0.5 * (lo + hi);
        }

        static string Coefs(EnergyCalibration c)
        {
            PolynomialEnergyCalibration p = c as PolynomialEnergyCalibration;
            if (p == null) return c == null ? "null" : c.GetType().Name;
            return "[" + string.Join(", ", p.Coefficients.Select(v => v.ToString("R", Inv))) + "]";
        }

        static string F(double v, int d) { return v.ToString("F" + d.ToString(Inv), Inv); }
        static string R(double v) { return v.ToString("R", Inv); }
        static string One(string s) { return s == null ? "" : s.Replace("\r", " ").Replace("\n", " "); }

        static void Expect(bool ok, string what)
        {
            if (!expectFixed) return;
            Console.WriteLine("  " + (ok ? "СОШЛОСЬ" : "⛔ НЕ СОШЛОСЬ") + ": " + what);
            if (!ok) bad++;
        }

        // ------------------------------------------------------------ AMBER110

        static void Stab(string xml)
        {
            int n;
            PolynomialEnergyCalibration refCal = RefCal(xml, out n);
            Console.WriteLine("=== AMBER110: стабилизатор, калибровка " + Path.GetFileName(xml) + " ===");
            Console.WriteLine("  каналов " + n.ToString(Inv) + ", опорная шкала " + Coefs(refCal)
                              + ", E(0) = " + F(refCal.Coefficients[0], 3) + " кэВ");
            double[] lines = { 59.54, 661.66, 1460.82, 2614.51 };
            decimal[][] setups =
            {
                new decimal[] { 661.66m },
                new decimal[] { 1460.82m },
                new decimal[] { 661.66m, 1460.82m },
            };
            double[] gains = { 1.000, 1.020 };
            foreach (decimal[] targets in setups)
            {
                foreach (double g in gains)
                {
                    StabOne(refCal, n, targets, g, lines);
                }
            }
        }

        static void StabOne(PolynomialEnergyCalibration refCal, int n, decimal[] targets, double g, double[] lines)
        {
            double[] c = refCal.Coefficients;
            // Спектр прибора с усилением g: линия E стоит на канале g·C_ref(E).
            EnergySpectrum es = new EnergySpectrum(1.0, n);
            es.EnergyCalibration = refCal.Clone();
            long total = 0;
            for (int k = 0; k < n; k++)
            {
                double v = 30.0;
                foreach (decimal t in targets)
                {
                    double ch = g * Inverse(c, (double)t, 0, n);
                    double sigma = Math.Max(1.5, 0.07 * ch / 2.3548);
                    v += 20000.0 * Math.Exp(-0.5 * (k - ch) * (k - ch) / (sigma * sigma));
                }
                es.Spectrum[k] = (int)Math.Round(v);
                total += es.Spectrum[k];
            }
            es.TotalPulseCount = total;
            es.ValidPulseCount = total;
            es.MeasurementTime = 1000.0;

            ResultData rd = new ResultData();
            rd.EnergySpectrum = es;
            DeviceConfigInfo dev = new DeviceConfigInfo();
            dev.NumberOfChannels = n;
            dev.EnergyCalibration = refCal.Clone();
            dev.StabilizerConfig.TargetPeaks.Clear();
            foreach (decimal t in targets)
            {
                TargetPeak tp = new TargetPeak();
                tp.Energy = t;
                tp.Error = 5m;
                dev.StabilizerConfig.TargetPeaks.Add(tp);
            }
            rd.DeviceConfig = dev;

            new PeakStabilizer().Stabilize(rd);
            EnergyCalibration nc = rd.EnergySpectrum.EnergyCalibration;
            StringBuilder sb = new StringBuilder();
            sb.Append("  опоры {" + string.Join(", ", targets.Select(t => t.ToString(Inv))) + "}, g = " + F(g, 3)
                      + ": найдено кан. {" + string.Join(", ", rd.CalibrationPeaks.Select(p => p.Channel.ToString(Inv))) + "}");
            Console.WriteLine(sb.ToString());
            Console.WriteLine("    новая шкала " + Coefs(nc) + (ReferenceEquals(nc, es.EnergyCalibration) ? "" : ""));
            StringBuilder e = new StringBuilder("    E_new(g·C_ref(E)) − E:");
            double worst = 0.0;
            foreach (double line in lines)
            {
                double ch = g * Inverse(c, line, 0, n);
                if (ch > n - 1) { e.Append("  " + F(line, 2) + "=вне"); continue; }
                double d = nc.ChannelToEnergy(ch) - line;
                e.Append("  " + F(line, 2) + ": " + (d >= 0 ? "+" : "") + F(d, 2));
                if (Math.Abs(d) > Math.Abs(worst)) worst = d;
            }
            Console.WriteLine(e.ToString());
            // После правки: одна опора — ошибка не больше полуширины канала
            // стабилизатора (найденный канал целый) на всех линиях: ±h у 2614.
            if (targets.Length <= 2)
            {
                double h = Poly(c, n / 2 + 1) - Poly(c, n / 2);
                Expect(Math.Abs(worst) <= 3.0 * h * (2614.51 / (double)targets[0] + 1.0),
                       "одна-две опоры: наибольшая ошибка " + F(worst, 2) + " кэВ в пределах целого канала опоры, растянутого на шкалу");
            }
        }

        // ------------------------------------------------------------ AMBER111

        static void PeakArm(string xml)
        {
            Console.WriteLine("=== AMBER111: энергия найденного пика, " + Path.GetFileName(xml) + " ===");
            ResultData rd = LoadXml(xml);
            EnergySpectrum es = rd.EnergySpectrum;
            ((PolynomialEnergyCalibration)es.EnergyCalibration).CheckCalibration(es.NumberOfChannels);
            MethodInfo create = typeof(PeakDetector).GetMethod("CreatePeak", BindingFlags.Instance | BindingFlags.NonPublic);
            if (create == null) { Console.WriteLine("  ⛔ нет CreatePeak"); bad++; return; }
            PeakDetector pd = new PeakDetector();
            int n = es.NumberOfChannels;
            double maxErr = 0.0, sum2 = 0.0; int cnt = 0;
            double worstAt = 0;
            for (int k = 20; k < n - 20; k += Math.Max(1, n / 256))
            {
                for (int f = 0; f < 10; f++)
                {
                    double centroid = k + f / 10.0 + 0.05;
                    Peak p = (Peak)create.Invoke(pd, new object[] { es, centroid, 10.0, 5.0, 0.0, 100.0, null, null, false });
                    double d = p.Energy - es.EnergyCalibration.ChannelToEnergy(centroid);
                    sum2 += d * d; cnt++;
                    if (Math.Abs(d) > Math.Abs(maxErr)) { maxErr = d; worstAt = centroid; }
                }
            }
            Console.WriteLine("  CreatePeak без уточнения, " + cnt.ToString(Inv) + " дробных центроидов: наибольшая |E_пика − E(центроид)| = "
                              + F(Math.Abs(maxErr), 3) + " кэВ (у " + F(worstAt, 2) + "), СКО " + F(Math.Sqrt(sum2 / cnt), 3) + " кэВ");
            Expect(Math.Abs(maxErr) < 1e-9, "энергия пика = E(дробный центроид)");

            // Настоящий поиск на спектре — список «канал, энергия» для сверки сборок.
            if (es.TotalPulseCount == 0)
            {
                long t = 0; for (int i = 0; i < n; i++) t += es.Spectrum[i];
                es.TotalPulseCount = t; es.ValidPulseCount = t;
            }
            string note = ProbeDeviceConfig.Attach(rd);
            Console.WriteLine("  прибор: " + note);
            if (rd.FwhmCalibration == null && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
                rd.FwhmCalibration = cfg.FwhmCalibration ?? FwhmCalibration.DefaultCalibration(cfg, es.EnergyCalibration);
            List<Peak> found = new PeakDetector().DetectPeak(rd, BackgroundMode.Invisible, SmoothingMethod.None, null, new List<NuclideDefinition>());
            Console.WriteLine("  DetectPeak: пиков " + found.Count.ToString(Inv) + " (канал: энергия, E(канал), разность)");
            foreach (Peak p in found.OrderBy(x => x.Channel))
            {
                double ec = es.EnergyCalibration.ChannelToEnergy(p.Channel);
                Console.WriteLine("    " + p.Channel.ToString(Inv).PadLeft(6) + ": " + F(p.Energy, 3).PadLeft(10)
                                  + "  " + F(ec, 3).PadLeft(10) + "  " + (p.Energy - ec >= 0 ? "+" : "") + F(p.Energy - ec, 3));
            }
        }

        // ------------------------------------------------------------ AMBER112

        static DocEnergySpectrum Import(string door, string path)
        {
            DocEnergySpectrum doc = new DocEnergySpectrum();
            DocumentManager dm = DocumentManager.GetInstance();
            TextWriter realErr = Console.Error;
            Console.SetError(new StringWriter());
            try
            {
                if (door == "su") dm.ImportDocumentSpecUtils(doc, path, 3600);
                else if (door == "csve") dm.ImportCsvEnergyToDocument(doc, 3600, path);
                else if (door == "n42") dm.ImportDocumentN42(doc, path);
                else throw new ArgumentException("дверь " + door);
            }
            finally { Console.SetError(realErr); }
            return doc;
        }

        static void MaxCh(string dir)
        {
            Directory.CreateDirectory(dir);
            Console.WriteLine("=== AMBER112: спектр 16384 каналов, ChannelToEnergy сразу после импорта ===");
            const int n = 16384;
            double[] c = { -3.0, 0.18, 1.0e-7 };
            Random rnd = new Random(170);
            int[] counts = new int[n];
            for (int k = 0; k < n; k++) counts[k] = 50 + rnd.Next(20);

            string spe = Path.Combine(dir, "p170_16384.spe");
            StringBuilder sb = new StringBuilder();
            sb.Append("$SPEC_ID:\r\np170 synthetic 16384\r\n$MEAS_TIM:\r\n1000 1000\r\n$DATA:\r\n0 " + (n - 1).ToString(Inv) + "\r\n");
            foreach (int v in counts) sb.Append(v.ToString(Inv)).Append("\r\n");
            sb.Append("$MCA_CAL:\r\n3\r\n" + R(c[0]) + " " + R(c[1]) + " " + R(c[2]) + " keV\r\n$ENDRECORD:\r\n");
            File.WriteAllText(spe, sb.ToString(), new UTF8Encoding(false));

            // CSV SpecUtils: «Energy, Data», энергия — нижний край, float, 6 значащих.
            string csv = Path.Combine(dir, "p170_16384_specutils.csv");
            WriteSpecUtilsCsv(csv, c, counts, true);

            // Свой «CSV с энергиями»: «Energy,Count», центры.
            string csve = Path.Combine(dir, "p170_16384_energy.csv");
            StringBuilder sc = new StringBuilder("Energy,Count\r\n");
            for (int k = 0; k < n; k++) sc.Append(R(Poly(c, k))).Append(',').Append(counts[k].ToString(Inv)).Append("\r\n");
            File.WriteAllText(csve, sc.ToString(), new UTF8Encoding(false));

            MaxChOne("su", spe, c, 0.0);
            MaxChOne("su", csv, c, -0.5);
            MaxChOne("csve", csve, c, 0.0);
        }

        static void WriteSpecUtilsCsv(string path, double[] centreCoefs, int[] counts, bool edges)
        {
            StringBuilder sb = new StringBuilder("Energy, Data\r\n");
            for (int k = 0; k < counts.Length; k++)
            {
                float e = (float)Poly(centreCoefs, edges ? k - 0.5 : k);
                sb.Append(e.ToString("G6", Inv)).Append(',').Append(counts[k].ToString(Inv)).Append("\r\n");
            }
            sb.Append("\r\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        static void MaxChOne(string door, string path, double[] c, double fileShift)
        {
            Console.WriteLine("  --- " + Path.GetFileName(path) + " дверью " + door);
            DocEnergySpectrum doc;
            try { doc = Import(door, path); }
            catch (Exception ex) { Console.WriteLine("    ОТКАЗ двери: " + ex.GetType().Name + ": " + One(ex.Message)); return; }
            ResultData rd = doc.ActiveResultData;
            EnergyCalibration cal = rd.EnergySpectrum.EnergyCalibration;
            int n = rd.EnergySpectrum.NumberOfChannels;
            Console.WriteLine("    каналов " + n.ToString(Inv) + ", шкала " + Coefs(cal) + ", MaxChannels() = " + cal.MaxChannels().ToString(Inv));
            double worst = 0.0;
            StringBuilder sb = new StringBuilder("    E(k) − полином документа:");
            PolynomialEnergyCalibration p = cal as PolynomialEnergyCalibration;
            foreach (int k in new[] { 4000, 8000, 8192, 8193, 12000, 16383 })
            {
                double e = cal.ChannelToEnergy(k);
                double direct = p != null ? Poly(p.Coefficients, k) : double.NaN;
                double d = e - direct;
                sb.Append("  " + k.ToString(Inv) + ": " + F(e, 2) + " (" + (d >= 0 ? "+" : "") + F(d, 2) + ")");
                if (k < n && Math.Abs(d) > Math.Abs(worst)) worst = d;
            }
            Console.WriteLine(sb.ToString());
            Expect(Math.Abs(worst) < 1e-6, "E(k) внутри спектра = полином документа (без обрезки 8192)");
        }

        // ------------------------------------------------------------ AMBER113

        static void Csv(string spec)
        {
            string[] p = spec.Split(',');
            string xml = p[0], dir = p[1];
            Directory.CreateDirectory(dir);
            int n;
            PolynomialEnergyCalibration refCal = RefCal(xml, out n);
            double[] c = refCal.Coefficients;
            ResultData rd = LoadXml(xml);
            int[] counts = rd.EnergySpectrum.Spectrum;
            Console.WriteLine("=== AMBER113: CSV SpecUtils (край) и свой ECSV (центр) дверью SpecUtils, шкала "
                              + Path.GetFileName(xml) + " " + Coefs(refCal) + " ===");
            string su = Path.Combine(dir, "p170_specutils_edges.csv");
            WriteSpecUtilsCsv(su, c, counts, true);
            string ecsv = Path.Combine(dir, "p170_own_ecsv.csv");
            StringBuilder sb = new StringBuilder();
            sb.Append(String.Format(CultureInfo.InvariantCulture, "Channel,Energy,Counts (TotalTime={0:0.0}s)", rd.EnergySpectrum.MeasurementTime)).Append("\r\n");
            for (int k = 0; k < n; k++)
                sb.Append(String.Format(Inv, "{0},{1},{2}", k, refCal.ChannelToEnergy(k), (double)counts[k])).Append("\r\n");
            File.WriteAllText(ecsv, sb.ToString(), new UTF8Encoding(false));

            double hHalf = 0.5 * (Poly(c, n / 2 + 1) - Poly(c, n / 2));
            double dSu = CsvOne(su, c, n);
            double dOwn = CsvOne(ecsv, c, n);
            Console.WriteLine("  h/2 в середине шкалы = " + F(hHalf, 3) + " кэВ");
            Expect(Math.Abs(dSu) < 0.05 * hHalf, "CSV SpecUtils: шкала документа = центры (|среднее| < 5 % от h/2)");
            Expect(Math.Abs(dOwn) < 0.05 * hHalf, "свой ECSV: шкала документа = центры (не тронута)");
        }

        static double CsvOne(string path, double[] c, int n)
        {
            DocEnergySpectrum doc = Import("su", path);
            EnergyCalibration cal = doc.ActiveResultData.EnergySpectrum.EnergyCalibration;
            double sum = 0.0, max = 0.0; int cnt = 0;
            int k662 = (int)Math.Round(Inverse(c, 661.66, 0, n));
            for (int k = 1; k < n - 1; k++)
            {
                double d = cal.ChannelToEnergy(k) - Poly(c, k);
                sum += d; cnt++;
                if (Math.Abs(d) > Math.Abs(max)) max = d;
            }
            double d662 = cal.ChannelToEnergy(k662) - Poly(c, k662);
            Console.WriteLine("  " + Path.GetFileName(path) + ": шкала " + Coefs(cal));
            Console.WriteLine("    E_дверь(k) − P(k): среднее " + F(sum / cnt, 4) + ", наибольшее " + F(max, 4)
                              + ", у 661.66 (канал " + k662.ToString(Inv) + ") " + F(d662, 4) + " кэВ");
            return sum / cnt;
        }

        // ------------------------------------------------------------ AMBER127

        static void DeadTime()
        {
            Console.WriteLine("=== AMBER127: мёртвое время AtomSpectra ===");
            AtomSpectraDeviceConfig cfg = new AtomSpectraDeviceConfig();
            double tau = cfg.DeadTime();
            Console.WriteLine("  умолчание конфигурации: DeadTime() = " + R(tau) + " с");
            double T = 3600.0, N = 608.0 * T;
            double lt = LiveTime.Calculate(T, N, tau);
            Console.WriteLine("  608 имп/с, T = 3600 с: LT = " + F(lt, 3) + " с (LT/T = " + F(lt / T, 6) + ")");
            foreach (double t in new[] { 7e-6, 14e-6 })
                foreach (double r in new[] { 608.0, 1948.0, 6824.0, 10000.0 })
                {
                    double l = LiveTime.Calculate(T, r * T, t);
                    Console.WriteLine("    τ = " + F(t * 1e6, 1) + " мкс, " + F(r, 0).PadLeft(6) + " имп/с: LT/T = " + F(l / T, 5)
                                      + " → активности при LT = T занижены на " + F(100.0 * (1.0 - l / T), 2) + " %");
                }

            MethodInfo parse = typeof(AtomSpectraDeviceConfig).GetMethod("DeadTimeFromInfo", BindingFlags.Public | BindingFlags.Static);
            if (parse == null)
            {
                Console.WriteLine("  разбор ответа -inf: метода AtomSpectraDeviceConfig.DeadTimeFromInfo НЕТ (сборка до правки);");
                Console.WriteLine("  кнопка режет ответ по пробелу и берёт [3], [5], [9] — отказ глотает пустой catch");
                Expect(false, "есть разбор ответа -inf по именам");
                return;
            }
            string[] answers =
            {
                // пример ответа из PROTOCOL.md atomspectra-waterfall-esp32 (VibeEngineering-LLC)
                "VERSION 13 RISE 8 FALL 12 NOISE 15 F 3000000.00 MAX 30000 HYST 1 MODE 0 STEP 1 t 9219 POT 102 POT2 26 T1 OFF T2 OFF T3 33.5 OUT 0..0/1 Prise 0 Srise 0 Pfall 0 Sfall 0 TC OFF TCpot OFF Tco [0 0 0] TP 1000 PileUp [] PileUpThr 8192",
                "VERSION 13  RISE 8\r\nFALL 12 NOISE 15 F 3000000.00",
                "",
                "-ok",
                "VERSION 13 RISE 8 FALL x NOISE 15 F 3000000.00",
                "VERSION 13 RISE 8 FALL 12 NOISE 15 F 0",
            };
            double[] expect = { 21.0 / 3.0e6, 21.0 / 3.0e6, double.NaN, double.NaN, double.NaN, double.NaN };
            for (int i = 0; i < answers.Length; i++)
            {
                object[] a = { answers[i], null };
                double v = (double)parse.Invoke(null, a);
                string why = (string)a[1];
                string shown = answers[i].Length > 50 ? answers[i].Substring(0, 50) + "…" : answers[i];
                Console.WriteLine("  «" + One(shown) + "» → " + (double.IsNaN(v) ? "отказ: " + why : F(v * 1e6, 4) + " мкс"));
                bool ok = double.IsNaN(expect[i]) ? (double.IsNaN(v) && !string.IsNullOrEmpty(why))
                                                  : Math.Abs(v - expect[i]) < 1e-15;
                Expect(ok, "ответ " + i.ToString(Inv));
            }
        }

        static void CorpusLiveTime(string dir)
        {
            Console.WriteLine("=== AMBER127: спектры AtomSpectra в корпусе, LT и T ===");
            int eq = 0, all = 0;
            foreach (string f in Directory.GetFiles(dir, "AS*.xml").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                ResultData rd = LoadXml(f);
                EnergySpectrum es = rd.EnergySpectrum;
                long t = es.TotalPulseCount;
                if (t == 0) for (int i = 0; i < es.NumberOfChannels; i++) t += es.Spectrum[i];
                double T = es.MeasurementTime, L = es.LiveTime;
                all++;
                bool same = L == 0.0 || L == T;
                if (same) eq++;
                Console.WriteLine("  " + Path.GetFileNameWithoutExtension(f).PadRight(28) + " T " + F(T, 0).PadLeft(8) + "  LT " + F(L, 1).PadLeft(10)
                                  + "  R " + F(t / T, 1).PadLeft(8) + " имп/с  " + (same ? "LT = T" : "LT/T = " + F(L / T, 5)));
            }
            Console.WriteLine("  итого спектров AtomSpectra " + all.ToString(Inv) + ", из них LT = T (или LT не записано) — " + eq.ToString(Inv));
        }
    }
}
