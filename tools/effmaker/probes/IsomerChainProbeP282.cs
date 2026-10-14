using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.NucBase;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П282, 14.10.2026) Ряд родителя с изомером дочки — как его видит ПРИЛОЖЕНИЕ, на той
    /// `nucdb.sqlite`, что лежит рядом с exe (база копии или поставки — решает каталог запуска).
    ///
    /// Раздел 1 — подробно по меткам ряда (--labels=Mo-99,Sr-91,…): доли `ChainBranches` (FSA),
    /// `GetChainBranches` (NucBase), дочерние карточки NucBase (`getNuclude`), и библиотека
    /// `FsaSampleLibrary.Build` при равновесии ВКЛ и ВЫКЛ — линии ≥ 0.05 % на распад корня
    /// (сумма по энергии с точностью 0.1 кэВ, нуклид линии).
    /// Раздел 2 — отпечаток библиотеки (ВКЛ/ВЫКЛ) по списку меток (--hash=…): sha256 строк
    /// «компонент | вид | нуклид | энергия R | выход R», для побайтной сверки двух баз.
    /// Коды: 0 — отработала; 2 — нет базы.
    ///
    /// (П294, 14.10.2026) Перенесена в дерево после записи рёбер в живую базу
    /// (`tools/nucdb/add_isomer_edges.py`, `database/scheme.md` §2). Ожидание на базе
    /// дерева, равновесие ВКЛ: Mo-99 140.5 кэВ 85.84 %, Sr-91 555.6 — 61.18 %, Fe-52
    /// 1434.1 — 102.56 %, Sn-126 666.1 — 81.1 % и 720.7 — 10.0 %; карточка NucBase
    /// Sn-126 — одна дочь «126SBm1 100» (нулевая строка «126SB 0» снята). Журнал —
    /// `handover/handover-2026-10-14-p294-isomer-edges-write.md`.
    /// </summary>
    static class IsomerChainProbeP282
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            string db = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            if (!File.Exists(db))
            {
                Console.WriteLine("нет базы: " + db);
                return 2;
            }

            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(db))
            {
                Console.WriteLine("nucdb: " + db + " sha256 " + BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").Substring(0, 16));
            }

            string labels = Arg(args, "--labels=", "Mo-99,Sr-91,I-135,Sn-126,Fe-52,Zr-89,I-131,Cs-137");
            string hash = Arg(args, "--hash=", "");
            foreach (string label in labels.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Detail(label.Trim());
            }

            if (hash.Length > 0)
            {
                Console.WriteLine("=== 2. отпечатки библиотеки ===");
                foreach (string label in hash.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,-10} ВКЛ {1}  ВЫКЛ {2}  ряд {3}",
                        label.Trim(), Print(label.Trim(), true), Print(label.Trim(), false), ChainPrint(label.Trim())));
                }
            }

            return 0;
        }

        static string Arg(string[] args, string key, string fallback)
        {
            foreach (string a in args)
            {
                if (a.StartsWith(key, StringComparison.Ordinal))
                {
                    return a.Substring(key.Length);
                }
            }

            return fallback;
        }

        static List<FsaComponent> Library(string label, bool eq)
        {
            var spec = new FsaSampleSpec { Equilibrium = eq };
            FsaSampleChain chain = FsaSampleChain.FromLabel(label);
            if (chain == null)
            {
                return new List<FsaComponent>();
            }

            spec.Chains.Add(chain);
            FsaSampleLibrary.Report report;
            List<FsaComponent> lib = FsaSampleLibrary.Build(spec, out report);
            return lib;
        }

        static void Detail(string label)
        {
            string root = FsaSampleLibrary.NucidOf(label);
            Console.WriteLine("=== " + label + " (" + root + ") ===");
            Dictionary<string, double> fsa = FsaSampleLibrary.ChainBranches(root, new FsaSampleLibrary.Report());
            Console.WriteLine("  ряд FSA (ChainBranches): " + string.Join(", ",
                fsa.OrderByDescending(p => p.Value).Select(p => p.Key + " " + p.Value.ToString("G6", CultureInfo.InvariantCulture))));
            var fw = new NucBaseFramework();
            Dictionary<string, double> nb = fw.GetChainBranches(root);
            Console.WriteLine("  ряд NucBase (GetChainBranches): " + string.Join(", ",
                nb.OrderByDescending(p => p.Value).Select(p => p.Key + " " + p.Value.ToString("G6", CultureInfo.InvariantCulture))));
            Nuclide card = fw.getNuclude(root);
            if (card != null && card.Daughters != null)
            {
                Console.WriteLine("  карточка NucBase, дочерние: " + string.Join(", ",
                    card.Daughters.Select(d => d.NucName + " " + d.DecayPercent + " [" + d.DecayTypeString + "]")));
            }

            foreach (bool eq in new[] { true, false })
            {
                FsaSampleLibrary.Report report;
                var spec = new FsaSampleSpec { Equilibrium = eq };
                FsaSampleChain chain = FsaSampleChain.FromLabel(label);
                if (chain == null)
                {
                    Console.WriteLine("  метка не разобрана");
                    return;
                }

                spec.Chains.Add(chain);
                List<FsaComponent> lib = FsaSampleLibrary.Build(spec, out report);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  библиотека, равновесие {0}: компонентов {1} ({2})",
                    eq ? "ВКЛ" : "ВЫКЛ", lib.Count, string.Join(", ", lib.Select(c => c.Name + ":" + c.Kind))));
                var sum = new SortedDictionary<double, KeyValuePair<double, HashSet<string>>>();
                foreach (FsaComponent comp in lib)
                {
                    foreach (FsaLine line in comp.Lines)
                    {
                        double key = Math.Round(line.Energy, 1);
                        KeyValuePair<double, HashSet<string>> have;
                        if (!sum.TryGetValue(key, out have))
                        {
                            have = new KeyValuePair<double, HashSet<string>>(0.0, new HashSet<string>());
                        }

                        have.Value.Add(line.Nuclide);
                        sum[key] = new KeyValuePair<double, HashSet<string>>(have.Key + line.Intensity, have.Value);
                    }
                }

                foreach (KeyValuePair<double, KeyValuePair<double, HashSet<string>>> e in sum)
                {
                    if (e.Value.Key >= 0.05 && e.Key >= 20.0)
                    {
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "    {0,8:F1} кэВ  {1,9:G5} %  {2}",
                            e.Key, e.Value.Key, string.Join("+", e.Value.Value)));
                    }
                }

                foreach (string note in report.Notes)
                {
                    Console.WriteLine("    заметка: " + note);
                }
            }
        }

        static string Print(string label, bool eq)
        {
            var sb = new StringBuilder();
            foreach (FsaComponent comp in Library(label, eq))
            {
                foreach (FsaLine line in comp.Lines)
                {
                    sb.Append(comp.Name).Append('|').Append(comp.Kind).Append('|').Append(line.Nuclide).Append('|')
                      .Append(line.Energy.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                      .Append(line.Intensity.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                }
            }

            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16);
            }
        }

        static string ChainPrint(string label)
        {
            string root = FsaSampleLibrary.NucidOf(label);
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, double> p in FsaSampleLibrary.ChainBranches(root, new FsaSampleLibrary.Report()).OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                sb.Append(p.Key).Append('=').Append(p.Value.ToString("R", CultureInfo.InvariantCulture)).Append(';');
            }

            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16);
            }
        }
    }
}
