using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace ImportConventionProbeP146
{
    /// <summary>
    /// `AMBER86` / `AMBER87` (полоса П146, 24.09.2026) — ОДИН ФАЙЛ РАЗНЫМИ
    /// ДВЕРЬМИ ИМПОРТА: какую шкалу энергий получает документ.
    ///
    /// Приложение читает номер канала ЦЕНТРОМ (решение Amber, `AMBER71`/`AMBER73`),
    /// SpecUtils — НИЖНИМ КРАЕМ, и полином файла она отдаёт КАК ЕСТЬ для
    /// любого формата (`set_polynomial(n, coefs)` в CHN/CNF/SPE/радиакодовском
    /// XML; столбец Energy CSV — `set_lower_channel_energy`). Отсюда вопрос
    /// строки `AMBER86`: у каких форматов дверь SpecUtils обязана сдвигать
    /// полином на полканала, а у каких — нет.
    ///
    /// ⛔ ЧТО МЕРИТСЯ:
    ///
    /// `--scale=<файл>@<дверь>` — шкала, которую дверь даёт документу:
    ///   коэффициенты форматом «R» и энергии пяти опорных каналов. Двери:
    ///   `xml` (родное «Открыть»: XmlSerializer поверх ResultDataFile, как
    ///   `DocumentManager.OpenDocument`), `n42` (`ImportDocumentN42`), `su`
    ///   (`ImportDocumentSpecUtils`), `atom` (`ImportDocumentAtomSpectra`),
    ///   `csve` (`ImportCsvEnergyToDocument`).
    /// `--same=<файл>@<дверь>,<файл>@<дверь>,<кэВ>` — наибольшее по каналам
    ///   |E_a(ch) − E_b(ch)| обязано быть не больше порога; печатается и
    ///   расхождение на канале около 662 кэВ вместе с h/2 там же.
    /// `--differ=<файл>@<дверь>,<файл>@<дверь>,<кэВ>` — наоборот, обязано быть
    ///   НЕ МЕНЬШЕ порога (так пишется ожидаемый сдвиг, положительный контроль).
    /// `--ecsv=<xml>,<csv>` — пишет ECSV тем же форматом, что
    ///   `DocumentManager.ExportDocumentToECSV` (там строка «Channel,Energy,
    ///   Counts (TotalTime=…)» и `{0},{1},{2}` с `cal.ChannelToEnergy(i)`;
    ///   сам метод зовёт окно выбора файла, поэтому формат повторён здесь
    ///   дословно).
    /// `--dump=<файл.tsv>` — все снятые шкалы строкой «файл, дверь,
    ///   коэффициенты R» для побитовой сверки двух сборок.
    ///
    /// Код 1 — не сошлось ожидание или дверь отказала там, где ждали шкалу.
    /// Проба безоконная.
    /// </summary>
    static class Program
    {
        static string dir = ".";
        static int bad = 0;
        static readonly List<string> dump = new List<string>();

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string dumpFile = null;
            List<string[]> jobs = new List<string[]>();
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=")) dir = a.Substring(6);
                else if (a.StartsWith("--scale=")) jobs.Add(new[] { "scale", a.Substring(8) });
                else if (a.StartsWith("--same=")) jobs.Add(new[] { "same", a.Substring(7) });
                else if (a.StartsWith("--differ=")) jobs.Add(new[] { "differ", a.Substring(9) });
                else if (a.StartsWith("--ecsv=")) jobs.Add(new[] { "ecsv", a.Substring(7) });
                else if (a.StartsWith("--lock=")) jobs.Add(new[] { "lock", a.Substring(7) });
                else if (a.StartsWith("--dump=")) dumpFile = a.Substring(7);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + typeof(DocumentManager).Assembly.Location);
            Console.WriteLine("  собрана " + File.GetLastWriteTime(typeof(DocumentManager).Assembly.Location)
                                                 .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Console.WriteLine();

            foreach (string[] j in jobs)
            {
                if (j[0] == "scale") Scale(j[1]);
                else if (j[0] == "same") Compare(j[1], true);
                else if (j[0] == "differ") Compare(j[1], false);
                else if (j[0] == "ecsv") Ecsv(j[1]);
                else if (j[0] == "lock") Lock(j[1]);
            }

            if (dumpFile != null) File.WriteAllLines(dumpFile, dump.ToArray(), new UTF8Encoding(false));
            Console.WriteLine();
            if (bad > 0)
            {
                Console.Error.WriteLine("ОТКАЗ: не сошлось ожиданий — " + bad.ToString(CultureInfo.InvariantCulture));
                return 1;
            }
            Console.WriteLine("ЗАМЕР СНЯТ.");
            return 0;
        }

        sealed class Loaded
        {
            public EnergyCalibration Cal;
            public int Channels;
            public string Error;
        }

        static readonly Dictionary<string, Loaded> cache = new Dictionary<string, Loaded>();

        static Loaded Load(string spec)
        {
            Loaded l;
            if (cache.TryGetValue(spec, out l)) return l;
            l = new Loaded();
            int at = spec.LastIndexOf('@');
            if (at < 0) { l.Error = "нет «@дверь»"; cache[spec] = l; return l; }
            string path = Path.Combine(dir, spec.Substring(0, at));
            string door = spec.Substring(at + 1);
            TextWriter realErr = Console.Error;
            Console.SetError(new StringWriter());
            try
            {
                ResultData rd;
                if (door == "xml")
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    {
                        ResultDataFile rdf = (ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs);
                        rd = rdf.ResultDataList[0];
                    }
                }
                else
                {
                    DocEnergySpectrum doc = new DocEnergySpectrum();
                    DocumentManager dm = DocumentManager.GetInstance();
                    if (door == "n42") dm.ImportDocumentN42(doc, path);
                    else if (door == "su") dm.ImportDocumentSpecUtils(doc, path, 3600);
                    else if (door == "atom") dm.ImportDocumentAtomSpectra(doc, path);
                    else if (door == "csve") dm.ImportCsvEnergyToDocument(doc, 3600, path);
                    else throw new ArgumentException("неизвестная дверь " + door);
                    rd = doc.ActiveResultData;
                }
                if (rd == null || rd.EnergySpectrum == null) l.Error = "документа нет";
                else
                {
                    l.Channels = rd.EnergySpectrum.NumberOfChannels;
                    l.Cal = rd.EnergySpectrum.EnergyCalibration;
                    if (l.Cal == null) l.Error = "шкалы нет";
                }
            }
            catch (Exception ex) { l.Error = ex.GetType().Name + ": " + One(ex.Message); }
            finally { Console.SetError(realErr); }
            cache[spec] = l;
            return l;
        }

        static void Scale(string spec)
        {
            Loaded l = Load(spec);
            Console.WriteLine("=== ШКАЛА " + spec + " ===");
            if (l.Error != null)
            {
                Console.WriteLine("  ОТКАЗ — " + l.Error);
                dump.Add(spec + "\tОТКАЗ");
                Console.WriteLine();
                return;
            }
            string coefs = Coefs(l.Cal);
            Console.WriteLine("  каналов " + l.Channels.ToString(CultureInfo.InvariantCulture) + ", " + coefs);
            int n = l.Channels;
            int[] chs = new int[] { 0, n / 4, n / 2, (3 * n) / 4, n - 1 };
            StringBuilder sb = new StringBuilder("  E(ch):");
            foreach (int c in chs)
                sb.Append("  ").Append(c.ToString(CultureInfo.InvariantCulture)).Append('=').Append(F(l.Cal.ChannelToEnergy(c)));
            Console.WriteLine(sb.ToString());
            dump.Add(spec + "\t" + n.ToString(CultureInfo.InvariantCulture) + "\t" + coefs);
            Console.WriteLine();
        }

        static void Compare(string spec, bool same)
        {
            string[] p = spec.Split(',');
            Console.WriteLine("=== " + (same ? "ОДНА ШКАЛА" : "ШКАЛЫ РАЗНЫЕ") + ": " + p[0] + "  vs  " + p[1] + " ===");
            double lim = double.Parse(p[2], CultureInfo.InvariantCulture);
            Loaded a = Load(p[0]), b = Load(p[1]);
            if (a.Error != null || b.Error != null)
            {
                Console.WriteLine("  ОТКАЗ — " + (a.Error ?? "") + " | " + (b.Error ?? ""));
                bad++;
                Console.WriteLine();
                return;
            }
            int n = Math.Min(a.Channels, b.Channels);
            double max = 0.0; int arg = 0; double sum = 0.0;
            int c662 = 0; double best = double.MaxValue;
            for (int ch = 0; ch < n; ch++)
            {
                double ea = a.Cal.ChannelToEnergy(ch);
                double d = b.Cal.ChannelToEnergy(ch) - ea;
                sum += d;
                if (Math.Abs(d) > Math.Abs(max)) { max = d; arg = ch; }
                if (Math.Abs(ea - 662.0) < best) { best = Math.Abs(ea - 662.0); c662 = ch; }
            }
            double d662 = b.Cal.ChannelToEnergy(c662) - a.Cal.ChannelToEnergy(c662);
            double h2 = (a.Cal.ChannelToEnergy(c662 + 1) - a.Cal.ChannelToEnergy(c662 - 1)) / 4.0;
            Console.WriteLine("  каналов " + n.ToString(CultureInfo.InvariantCulture)
                              + "; E_b − E_a: наибольшее " + F(max) + " кэВ (канал " + arg.ToString(CultureInfo.InvariantCulture)
                              + "), среднее " + F(sum / n) + ", у 662 кэВ (канал " + c662.ToString(CultureInfo.InvariantCulture)
                              + ") " + F(d662) + " при h/2 = " + F(h2));
            bool ok = same ? Math.Abs(max) <= lim : Math.Abs(d662) >= lim;
            Console.WriteLine("  " + (ok ? "СОШЛОСЬ" : "⛔ НЕ СОШЛОСЬ") + ": ждали "
                              + (same ? "|наибольшее| ≤ " : "|у 662| ≥ ") + F(lim) + " кэВ");
            if (!ok) bad++;
            Console.WriteLine();
        }

        static void Ecsv(string spec)
        {
            string[] p = spec.Split(',');
            string src = Path.Combine(dir, p[0]);
            string dst = Path.Combine(dir, p[1]);
            Console.WriteLine("=== ECSV " + p[0] + " → " + p[1] + " (формат ExportDocumentToECSV) ===");
            ResultData rd;
            using (FileStream fs = new FileStream(src, FileMode.Open, FileAccess.Read))
                rd = ((ResultDataFile)new XmlSerializer(typeof(ResultDataFile)).Deserialize(fs)).ResultDataList[0];
            EnergySpectrum es = rd.EnergySpectrum;
            PolynomialEnergyCalibration cal = (PolynomialEnergyCalibration)es.EnergyCalibration;
            using (StreamWriter w = new StreamWriter(dst, false, Encoding.GetEncoding(65001)))
            {
                w.WriteLine(String.Format(CultureInfo.InvariantCulture, "Channel,Energy,Counts (TotalTime={0:0.0}s)", es.MeasurementTime));
                for (int i = 0; i < es.NumberOfChannels; i++)
                    w.WriteLine(String.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", i, cal.ChannelToEnergy(i), (double)es.Spectrum[i]));
            }
            Console.WriteLine("  записан, каналов " + es.NumberOfChannels.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine();
        }

        // `--lock=<файл N42>`: копия файла, `GetN42Type` на ней и СРАЗУ
        // `File.Delete`. Держит ли дверь файл открытым после своего вопроса.
        static void Lock(string name)
        {
            string src = Path.Combine(dir, name);
            string tmp = Path.Combine(dir, "lock-" + Guid.NewGuid().ToString("N") + ".n42");
            File.Copy(src, tmp);
            Console.WriteLine("=== ФАЙЛ ПОСЛЕ GetN42Type: " + name + " ===");
            Type t = DocumentManager.GetInstance().GetN42Type(tmp);
            try
            {
                File.Delete(tmp);
                Console.WriteLine("  тип " + t.Name + "; копия удалена сразу — файл НЕ держится");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  тип " + t.Name + "; ⛔ удалить нельзя — " + ex.GetType().Name + ": " + One(ex.Message));
                bad++;
                GC.Collect(); GC.WaitForPendingFinalizers();
                try { File.Delete(tmp); } catch { }
            }
            Console.WriteLine();
        }

        static string Coefs(EnergyCalibration c)
        {
            PolynomialEnergyCalibration p = c as PolynomialEnergyCalibration;
            if (p == null) return c.GetType().Name;
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < p.Coefficients.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(p.Coefficients[i].ToString("R", CultureInfo.InvariantCulture));
            }
            return sb.Append(']').ToString();
        }

        static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }
        static string One(string s) { return s == null ? "" : s.Replace("\r", " ").Replace("\n", " "); }
    }
}
