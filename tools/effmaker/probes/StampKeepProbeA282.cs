using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace StampKeepProbeA282
{
    /// <summary>
    /// (`AMBER282`, полоса `a282b-A`, 03.11.2026) «При круглом колодце и без защиты в сцене всё
    /// побайтно прежнее»: для каждой сцены печатает клеймо матрицы, sha256 текста `.in`
    /// (`GeometryWriter.Render`), sha256 XML модели и sha256 дампа сцены переноса
    /// (`EfficiencySimulator.DumpScene`). Сборки ДО правки и ПОСЛЕ гоняются одной и той же пробой,
    /// выходы сравниваются текстом (`fc`/`diff`): любое различие — дефект правки.
    ///
    ///   StampKeepProbeA282 --out=&lt;файл.tsv&gt; [--in=&lt;каталог .in&gt;]… [--xml=&lt;каталог конфигов прибора&gt;]…
    ///
    /// Конфиги прибора читаются как XML: каждый элемент `&lt;Geometry&gt;` — модель (`EfficiencyConfigData.Geometry`).
    /// Код возврата: 0 — все сцены прочитаны; 1 — хоть одна не прочиталась (печатается).
    /// </summary>
    static class Program
    {
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string outPath = null;
            var inDirs = new List<string>();
            var xmlDirs = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--out=")) outPath = a.Substring(6);
                else if (a.StartsWith("--in=")) inDirs.Add(a.Substring(5));
                else if (a.StartsWith("--xml=")) xmlDirs.Add(a.Substring(6));
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            if (outPath == null)
            {
                Console.Error.WriteLine("нужен --out=");
                return 2;
            }

            var lines = new List<string> { "source\tname\tstamp\tin_sha\txml_sha\tscene_sha" };
            int failed = 0;
            var options = new ResponseMatrixOptions();
            var xs = new XmlSerializer(typeof(GeometryModel));
            var xsGeometry = new XmlSerializer(typeof(GeometryModel), new XmlRootAttribute("Geometry"));
            Action<string, string, GeometryModel> row = (source, name, g) =>
            {
                string stamp, inSha, xmlSha, sceneSha;
                try
                {
                    stamp = ResponseMatrix.ComputeStamp(g, options);
                    inSha = Sha(GeometryWriter.Render(g));
                    using (var sw = new StringWriter(Ci))
                    {
                        xs.Serialize(sw, g);
                        xmlSha = Sha(sw.ToString());
                    }

                    try
                    {
                        sceneSha = Sha(new EfficiencySimulator(g).DumpScene());
                    }
                    catch (Exception e)
                    {
                        sceneSha = "отказ:" + e.GetType().Name;
                    }
                }
                catch (Exception e)
                {
                    failed++;
                    Console.WriteLine("НЕ ПРОЧИТАЛАСЬ {0} {1}: {2}", source, name, e.Message);
                    return;
                }

                lines.Add(string.Join("\t", source, name, stamp, inSha, xmlSha, sceneSha));
            };

            foreach (string dir in inDirs)
            {
                string[] files = Directory.GetFiles(dir, "*.in");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string f in files)
                {
                    GeometryModel g;
                    try
                    {
                        g = GeometryModel.Load(f);
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Console.WriteLine("НЕ ПРОЧИТАЛАСЬ {0}: {1}", f, e.Message);
                        continue;
                    }

                    row("in", Path.GetFileName(f), g);
                }
            }

            foreach (string dir in xmlDirs)
            {
                string[] files = Directory.GetFiles(dir, "*.xml");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string f in files)
                {
                    var doc = new XmlDocument();
                    doc.Load(f);
                    int k = 0;
                    foreach (XmlNode node in doc.GetElementsByTagName("Geometry"))
                    {
                        GeometryModel g;
                        using (var sr = new StringReader(node.OuterXml))
                        {
                            g = (GeometryModel)xsGeometry.Deserialize(sr);
                        }

                        g.ApplyGapDefault();
                        row("xml", Path.GetFileName(f) + "#" + k.ToString(Ci), g);
                        k++;
                    }
                }
            }

            File.WriteAllLines(outPath, lines, new UTF8Encoding(false));
            Console.WriteLine("сцен {0}, не прочиталось {1}: {2}", lines.Count - 1, failed, outPath);
            return failed == 0 ? 0 : 1;
        }

        static string Sha(string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder();
                for (int i = 0; i < 12; i++) sb.Append(h[i].ToString("x2", Ci));
                return sb.ToString();
            }
        }
    }
}
