using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CrystalOwnHashProbeA274a
{
    /// <summary>
    /// (`AMBER274` Э3, полоса `a274a` 14.11.2026) ПОБИТНОСТЬ ПРЕЖНЕГО ПУТИ при выключенных альфах — проба
    /// зовёт ТОЛЬКО то, что было в коде до полосы, поэтому собирается и прежней сборкой, и новой; выводы
    /// двух сборок сравниваются построчно (`fc`):
    ///
    ///   1. строки ветвей беты «событием целиком» (`FsaBetaLibrary.EventSpec`) — у ВСЕХ β⁻-ветвей `nucdb`:
    ///      хеш таблиц (<c>ElectronBranchSpec.Hash</c>) или причина отказа. Полоса вынесла из `EventSpec`
    ///      таблицы каскада в общий кусок с альфой — хеши обязаны совпасть все;
    ///   2. раздел собственной активности (`CrystalOwnActivityBuilder.Build`, рецепт умолчания — без альф) на
    ///      сценах шаблонов «AtomSpectra Pro LaBr3(Ce)» и «LaBr3 38x38»-подобной (LaBr3 Ø38×38 руками):
    ///      клеймо и SHA-256 гистограмм каждой записи.
    ///
    ///     CrystalOwnHashProbeA274a [--out=файл] [--histories=20000]
    /// </summary>
    internal static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            string outPath = null;
            int histories = 20000;
            foreach (string a in args)
            {
                if (a.StartsWith("--out=", StringComparison.Ordinal)) outPath = a.Substring(6);
                else if (a.StartsWith("--histories=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("ключ не понят: " + a); return 2; }
            }

            var sb = new StringBuilder();
            int specs = 0, refused = 0;
            foreach (string nucid in BetaParents())
            {
                foreach (FsaBeta beta in FsaBetaLibrary.Branches(nucid))
                {
                    if (beta == null || beta.IsLine || beta.Positron || !(beta.EndpointKev > 0.0))
                    {
                        continue;
                    }

                    string reason;
                    ElectronBranchSpec spec = FsaBetaLibrary.EventSpec(beta, out reason);
                    if (spec != null)
                    {
                        specs++;
                        sb.Append("spec ").Append(nucid).Append(' ').Append(beta.EndpointKev.ToString("R", CultureInfo.InvariantCulture))
                          .Append(' ').Append(spec.Hash).Append(' ').Append(spec.LevelKev.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                    }
                    else
                    {
                        refused++;
                        sb.Append("refuse ").Append(nucid).Append(' ').Append(beta.EndpointKev.ToString("R", CultureInfo.InvariantCulture))
                          .Append(' ').Append(reason).Append('\n');
                    }
                }
            }

            sb.Append("specs=").Append(specs).Append(" refused=").Append(refused).Append('\n');

            GeometryPresets.Preset preset = GeometryPresets.Items.FirstOrDefault(p => p.Name == "AtomSpectra Pro LaBr3(Ce)");
            if (preset == null)
            {
                Console.Error.WriteLine("нет шаблона AtomSpectra Pro LaBr3(Ce)");
                return 3;
            }

            var g1 = new GeometryModel();
            preset.Apply(g1);
            var g2 = new GeometryModel();
            preset.Apply(g2);
            g2.CrystalDiameter = 38.0;
            g2.CrystalHeight = 38.0;
            foreach (var pair in new[] { new KeyValuePair<string, GeometryModel>("as2pro", g1), new KeyValuePair<string, GeometryModel>("labr38", g2) })
            {
                string note;
                ResponseCrystalSection section = CrystalOwnActivityBuilder.Build(pair.Value, 2.0, histories, 0x274, out note);
                sb.Append("section ").Append(pair.Key).Append(" stamp=").Append(section.Stamp).Append('\n');
                sb.Append("section ").Append(pair.Key).Append(" note=").Append(section.Note).Append('\n');
                foreach (CrystalOwnEntry e in section.Entries)
                {
                    sb.Append("entry ").Append(pair.Key).Append(' ').Append(e.Nuclide).Append(' ').Append(e.Free ? "free" : "fixed")
                      .Append(" A=").Append(e.ActivityBq.ToString("R", CultureInfo.InvariantCulture))
                      .Append(" bins=").Append(e.PerDecay.Length).Append(" sha=").Append(Sha(e.PerDecay)).Append('\n');
                }
            }

            string text = sb.ToString();
            if (outPath != null)
            {
                File.WriteAllText(outPath, text, new UTF8Encoding(false));
            }

            Console.Write(text.Length > 4000 ? text.Substring(text.Length - 4000) : text);
            return 0;
        }

        static List<string> BetaParents()
        {
            var list = new List<string>();
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            using (var connection = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select distinct parent_nucid from decay_radiations where type_a = 'B-' order by parent_nucid";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(reader.GetString(0));
                        }
                    }
                }
            }

            return list;
        }

        static string Sha(double[] values)
        {
            var bytes = new byte[values.Length * 8];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Substring(0, 16).ToLowerInvariant();
            }
        }
    }
}
