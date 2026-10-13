using BecquerelMonitor;
using Util = BecquerelMonitor.N42.Util;
using RadInstrumentData = BecquerelMonitor.N42.RadInstrumentData;
using EnergyCalibration = BecquerelMonitor.EnergyCalibration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace AtomSpectraN42ProbeP151
{
    /// <summary>
    /// `AMBER87` (полоса П151, 24.09.2026) — N42, ЗАПИСАННЫЙ AtomSpectra, И
    /// ВСЕ ПРОЧИЕ N42: какой признак программы получает файл, какую шкалу
    /// дают ему две двери импорта и не дрейфует ли круг «импорт → экспорт → импорт».
    ///
    /// Решение Amber 24.09.2026 вопросником, дословно: «N42 AtomSpectra без
    /// сдвига (Рекомендую)». Признак — <c>Util.IsWrittenByAtomSpectra</c>
    /// (зовётся ОТРАЖЕНИЕМ: на сборке до правки его нет, и проба печатает «—»,
    /// чтобы одна и та же проба мерила обе сборки).
    ///
    /// Ключи:
    /// `--root=&lt;каталог&gt;` (можно несколько) — все `*.n42` под ним рекурсивно;
    /// `--pair=&lt;txt&gt;|&lt;n42&gt;|&lt;кэВ&gt;` — `.txt` дверью AtomSpectra и N42 дверью N42
    ///   обязаны дать одну шкалу: наибольшее по каналам |ΔE| ≤ порога;
    /// `--pair-differ=…` — то же, но обязано быть НЕ МЕНЬШЕ (положительный контроль
    ///   сборки ДО);
    /// `--check` — у каждого импортированного файла: двери N42 и SpecUtils — одна шкала
    ///   (|ΔE| ≤ 1E-3 кэВ, одинарная точность SpecUtils), круг экспорт→импорт
    ///   (|ΔE| ≤ 1E-9 кэВ);
    /// `--tmp=&lt;каталог&gt;` — куда класть экспортированные копии (по умолчанию %TEMP%);
    /// `--dump=&lt;tsv&gt;` — «файл, дверь, признак, каналов, коэффициенты R» для
    ///   побитовой сверки двух сборок.
    /// Код 1 — не сошлось ожидание.
    /// </summary>
    static class Program
    {
        static int bad = 0;
        static readonly List<string> dump = new List<string>();
        static string tmpDir = Path.GetTempPath();
        static MethodInfo signMethod;

        [STAThread]
        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            List<string> roots = new List<string>();
            List<string[]> pairs = new List<string[]>();
            bool check = false;
            // `--doors-except=<подстрока пути>`: сверка дверей n42↔su у такого
            // файла печатается, но не судится — ИЗВЕСТНОЕ расхождение, не
            // относящееся к AtomSpectra (16384-канальный германий: дверь SpecUtils
            // даёт вдвое меньшую верхнюю энергию, одинаково до и после П151).
            List<string> doorsExcept = new List<string>();
            string dumpFile = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--root=")) roots.Add(a.Substring(7));
                else if (a.StartsWith("--pair=")) pairs.Add(new[] { "same", a.Substring(7) });
                else if (a.StartsWith("--pair-differ=")) pairs.Add(new[] { "differ", a.Substring(14) });
                else if (a == "--check") check = true;
                else if (a.StartsWith("--doors-except=")) doorsExcept.Add(a.Substring(15));
                else if (a.StartsWith("--tmp=")) tmpDir = a.Substring(6);
                else if (a.StartsWith("--dump=")) dumpFile = a.Substring(7);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }
            signMethod = typeof(Util).GetMethod("IsWrittenByAtomSpectra", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

            Console.WriteLine("=== СБОРКА ===");
            Console.WriteLine("  " + typeof(DocumentManager).Assembly.Location);
            Console.WriteLine("  признак IsWrittenByAtomSpectra: " + (signMethod == null ? "НЕТ в сборке" : "есть"));
            Console.WriteLine();

            int files = 0, atom = 0, n42ok = 0;
            foreach (string root in roots)
            {
                foreach (string f in Directory.GetFiles(root, "*.n42", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                {
                    files++;
                    string sign = Sign(f);
                    if (sign == "да") atom++;
                    string rel = f.Substring(root.Length).TrimStart('\\', '/');
                    Console.WriteLine("--- " + rel + "  [AtomSpectra: " + sign + "; " + Header(f) + "]");
                    EnergyCalibration n42 = Load(f, "n42", out int nch, out string e1);
                    EnergyCalibration su = Load(f, "su", out int nch2, out string e2);
                    dump.Add(rel + "\tn42\t" + sign + "\t" + (e1 ?? nch.ToString(CultureInfo.InvariantCulture) + "\t" + Coefs(n42)));
                    dump.Add(rel + "\tsu\t" + sign + "\t" + (e2 ?? nch2.ToString(CultureInfo.InvariantCulture) + "\t" + Coefs(su)));
                    Console.WriteLine("  n42: " + (e1 ?? nch.ToString(CultureInfo.InvariantCulture) + " кан., E(0) = " + F(n42.ChannelToEnergy(0)) + ", E(N−1) = " + F(n42.ChannelToEnergy(nch - 1))));
                    Console.WriteLine("  su : " + (e2 ?? nch2.ToString(CultureInfo.InvariantCulture) + " кан., E(0) = " + F(su.ChannelToEnergy(0)) + ", E(N−1) = " + F(su.ChannelToEnergy(nch2 - 1))));
                    if (e1 == null) n42ok++;
                    if (check && e1 == null && e2 == null)
                    {
                        double d = MaxDiff(n42, su, Math.Min(nch, nch2), out int arg);
                        bool ok = Math.Abs(d) <= 1e-3;
                        bool excepted = doorsExcept.Any(x => rel.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
                        Console.WriteLine("  двери n42↔su: наибольшее |ΔE| " + F(d) + " кэВ (канал " + arg.ToString(CultureInfo.InvariantCulture) + ") — "
                                          + (ok ? "одна шкала" : excepted ? "[--] РАЗНЫЕ, не судится (--doors-except)" : "⛔ РАЗНЫЕ"));
                        if (!ok && !excepted) bad++;
                    }
                    if (check && e1 == null)
                    {
                        string back = RoundTrip(f, n42, nch);
                        Console.WriteLine("  круг импорт→экспорт→импорт: " + back);
                    }
                }
            }
            Console.WriteLine();
            Console.WriteLine("ИТОГ: файлов " + files.ToString(CultureInfo.InvariantCulture) + ", признак AtomSpectra у "
                              + atom.ToString(CultureInfo.InvariantCulture) + ", импортировано дверью N42 " + n42ok.ToString(CultureInfo.InvariantCulture));

            foreach (string[] p in pairs) Pair(p[1], p[0] == "same");

            if (dumpFile != null) File.WriteAllLines(dumpFile, dump.ToArray(), new UTF8Encoding(false));
            Console.WriteLine();
            if (bad > 0)
            {
                Console.Error.WriteLine("ОТКАЗ: не сошлось ожиданий — " + bad.ToString(CultureInfo.InvariantCulture));
                return 1;
            }
            Console.WriteLine("ЗАМЕР СНЯТ, ожидания сошлись.");
            return 0;
        }

        static string Sign(string f)
        {
            if (signMethod == null) return "—";
            return (bool)signMethod.Invoke(null, new object[] { f }) ? "да" : "нет";
        }

        // Программа и создатель из шапки — только для глаза, решает Sign.
        static string Header(string f)
        {
            try
            {
                string head = File.ReadAllText(f);
                if (head.Length > 6000) head = head.Substring(0, 6000);
                string creator = Between(head, "<RadInstrumentDataCreatorName>", "<");
                int k = head.IndexOf(">SoftwareName<", StringComparison.Ordinal);
                string sw = k < 0 ? "" : Between(head.Substring(k), "<RadInstrumentComponentVersion>", "<");
                string man = Between(head, "<RadInstrumentManufacturerName>", "<");
                return "изготовитель «" + man + "», SoftwareName «" + sw + "»" + (creator.Length > 0 ? ", создатель «" + creator + "»" : "");
            }
            catch (Exception ex) { return ex.GetType().Name; }
        }

        static string Between(string s, string a, string b)
        {
            int i = s.IndexOf(a, StringComparison.Ordinal);
            if (i < 0) return "";
            i += a.Length;
            int j = s.IndexOf(b, i, StringComparison.Ordinal);
            return j < 0 ? "" : s.Substring(i, j - i).Trim();
        }

        static EnergyCalibration Load(string path, string door, out int channels, out string error)
        {
            channels = 0; error = null;
            TextWriter realErr = Console.Error;
            Console.SetError(new StringWriter());
            try
            {
                DocEnergySpectrum doc = new DocEnergySpectrum();
                DocumentManager dm = DocumentManager.GetInstance();
                if (door == "n42") dm.ImportDocumentN42(doc, path);
                else if (door == "su") dm.ImportDocumentSpecUtils(doc, path, 3600);
                else if (door == "atom") dm.ImportDocumentAtomSpectra(doc, path);
                ResultData rd = doc.ActiveResultData;
                if (rd == null || rd.EnergySpectrum == null || rd.EnergySpectrum.EnergyCalibration == null)
                { error = "ОТКАЗ — шкалы нет"; return null; }
                channels = rd.EnergySpectrum.NumberOfChannels;
                return rd.EnergySpectrum.EnergyCalibration;
            }
            catch (Exception ex)
            {
                error = "ОТКАЗ — " + ex.GetType().Name + ": " + One(ex.Message);
                return null;
            }
            finally { Console.SetError(realErr); }
        }

        static string RoundTrip(string path, EnergyCalibration first, int nch)
        {
            string dst = Path.Combine(tmpDir, "rt-" + Guid.NewGuid().ToString("N") + ".n42");
            try
            {
                DocEnergySpectrum doc = new DocEnergySpectrum();
                DocumentManager.GetInstance().ImportDocumentN42(doc, path);
                RadInstrumentData rad = new Util().ExportToN42(doc);
                XmlSerializer ser = new XmlSerializer(typeof(RadInstrumentData));
                XmlWriterSettings settings = new XmlWriterSettings { Encoding = Encoding.UTF8, Indent = true };
                using (FileStream fs = new FileStream(dst, FileMode.Create))
                using (XmlWriter w = XmlWriter.Create(fs, settings))
                {
                    ser.Serialize(w, rad);
                }
                string backSign = Sign(dst);
                EnergyCalibration back = Load(dst, "n42", out int n2, out string err);
                if (err != null) { bad++; return "⛔ " + err; }
                double d = MaxDiff(first, back, Math.Min(nch, n2), out int arg);
                bool ok = Math.Abs(d) <= 1e-9;
                if (!ok) bad++;
                return "наибольшее |ΔE| " + F(d) + " кэВ (канал " + arg.ToString(CultureInfo.InvariantCulture)
                       + "), признак экспортированного: " + backSign + " — " + (ok ? "не дрейфует" : "⛔ ДРЕЙФ");
            }
            catch (Exception ex)
            {
                bad++;
                return "⛔ " + ex.GetType().Name + ": " + One(ex.Message);
            }
            finally
            {
                try { File.Delete(dst); } catch { }
            }
        }

        static void Pair(string spec, bool same)
        {
            string[] p = spec.Split('|');
            double lim = double.Parse(p[2], CultureInfo.InvariantCulture);
            Console.WriteLine("=== " + (same ? "ОДНА ШКАЛА" : "ШКАЛЫ РАЗНЫЕ") + ": " + Path.GetFileName(p[0]) + "@atom  vs  " + Path.GetFileName(p[1]) + "@n42 ===");
            EnergyCalibration a = Load(p[0], "atom", out int na, out string ea);
            EnergyCalibration b = Load(p[1], "n42", out int nb, out string eb);
            if (ea != null || eb != null) { Console.WriteLine("  " + ea + " | " + eb); bad++; return; }
            double d = MaxDiff(a, b, Math.Min(na, nb), out int arg);
            int c662 = 0; double best = double.MaxValue;
            for (int ch = 0; ch < na; ch++) { double e = a.ChannelToEnergy(ch); if (Math.Abs(e - 662) < best) { best = Math.Abs(e - 662); c662 = ch; } }
            double d662 = b.ChannelToEnergy(c662) - a.ChannelToEnergy(c662);
            double h2 = (a.ChannelToEnergy(c662 + 1) - a.ChannelToEnergy(c662 - 1)) / 4.0;
            Console.WriteLine("  E_n42 − E_txt: наибольшее " + F(d) + " кэВ (канал " + arg.ToString(CultureInfo.InvariantCulture)
                              + "), у 662 " + F(d662) + " при h/2 = " + F(h2));
            bool ok = same ? Math.Abs(d) <= lim : Math.Abs(d662) >= lim;
            Console.WriteLine("  " + (ok ? "СОШЛОСЬ" : "⛔ НЕ СОШЛОСЬ"));
            if (!ok) bad++;
        }

        static double MaxDiff(EnergyCalibration a, EnergyCalibration b, int n, out int arg)
        {
            double max = 0.0; arg = 0;
            for (int ch = 0; ch < n; ch++)
            {
                double d = b.ChannelToEnergy(ch) - a.ChannelToEnergy(ch);
                if (Math.Abs(d) > Math.Abs(max)) { max = d; arg = ch; }
            }
            return max;
        }

        static string Coefs(EnergyCalibration c)
        {
            PolynomialEnergyCalibration p = c as PolynomialEnergyCalibration;
            if (p == null) return c == null ? "null" : c.GetType().Name;
            return string.Join(" ", p.Coefficients.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));
        }

        static string F(double v) { return v.ToString("G6", CultureInfo.InvariantCulture); }
        static string One(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " "); }
    }
}
