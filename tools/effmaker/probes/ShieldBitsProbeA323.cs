using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

// (`AMBER323`, полоса `a323` 13.11.2026) ПОБИТНОСТЬ ПРИ НУЛЕВОМ МАГНИТНОМ ЭКРАНЕ.
//
// Проба нарочно пишется ТОЛЬКО прежним API (без полей экрана): её собирают против сборки
// ДО правки и против сборки С правкой, и печать двух прогонов сравнивается побайтно
// (`fc /b` или sha256 файла вывода). По каждой сцене `*.in` каталога печатается:
//   * sha256 текста `.in` (`GeometryWriter.Render`) — это и есть текст клейма матрицы;
//   * клеймо `ResponseMatrix.ComputeStamp` при умолчаниях и, если рядом лежит `<ключ>.rmx`
//     склада, `IsValidFor` по своим параметрам матрицы (годна ли матрица склада);
//   * sha256 XML модели (`XmlSerializer`) — конфигурации и спектры несут её этим путём;
//   * `DescribeScene()` симулятора — области сцены, их радиусы и вещества;
//   * эффективность пика и полная на 30, 122 и 662 кэВ при одном зерне и малом числе
//     историй, числа — `R` (все разряды): перенос ЦП до бита.
// ISO-сцены считаются так же (на ЦП они есть). Код 0 — всегда, если всё прочиталось;
// судит сравнение двух выводов.
//
//   shieldbitsprobea323 --dir=<каталог *.in> [--store=<каталог *.rmx>] [--n=20000] [--only=a,b]
static class ShieldBitsProbeA323
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        string dir = null, store = null, only = null;
        int n = 20000;
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
            else if (a.StartsWith("--store=", StringComparison.Ordinal)) store = a.Substring(8);
            else if (a.StartsWith("--only=", StringComparison.Ordinal)) only = a.Substring(7);
            else if (a.StartsWith("--n=", StringComparison.Ordinal)) n = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (dir == null || !Directory.Exists(dir))
        {
            Console.Error.WriteLine("нужно --dir=<каталог *.in>");
            return 2;
        }

        var files = new List<string>(Directory.GetFiles(dir, "*.in"));
        files.Sort(StringComparer.Ordinal);
        if (only != null)
        {
            var wanted = new List<string>(only.Split(','));
            files.RemoveAll(f => !wanted.Contains(Path.GetFileNameWithoutExtension(f)));
        }

        var xml = new XmlSerializer(typeof(GeometryModel));
        int valid = 0, withMatrix = 0;
        foreach (string path in files)
        {
            string key = Path.GetFileNameWithoutExtension(path);
            GeometryModel g = GeometryModel.Load(path);
            Console.WriteLine("== " + key);
            Console.WriteLine("render " + Sha(Encoding.UTF8.GetBytes(GeometryWriter.Render(g))));
            Console.WriteLine("stamp " + ResponseMatrix.ComputeStamp(g, new ResponseMatrixOptions()));
            using (var sw = new StringWriter(CultureInfo.InvariantCulture))
            {
                xml.Serialize(sw, g);
                Console.WriteLine("xml " + Sha(Encoding.UTF8.GetBytes(sw.ToString())));
            }

            if (store != null)
            {
                string rmx = Path.Combine(store, key + ".rmx");
                if (File.Exists(rmx))
                {
                    withMatrix++;
                    ResponseMatrix m = ResponseMatrix.Load(rmx);
                    bool ok = m != null && m.IsValidFor(g);
                    if (ok) valid++;
                    Console.WriteLine("store " + (ok ? "годна" : "НЕ ГОДНА"));
                }
            }

            try
            {
                var sim = new EfficiencySimulator(g) { Histories = n };
                sim.ResetStream(20261113UL);
                Console.WriteLine(sim.DescribeScene().Replace("\r", ""));
                foreach (double e in new[] { 30.0, 122.06, 661.657 })
                {
                    double err, err2;
                    double p = sim.Efficiency(e, out err);
                    double t = sim.TotalEfficiency(e, out err2);
                    Console.WriteLine("E {0} peak {1} {2} total {3} {4}", e.ToString("R", CultureInfo.InvariantCulture),
                                      p.ToString("R", CultureInfo.InvariantCulture), err.ToString("R", CultureInfo.InvariantCulture),
                                      t.ToString("R", CultureInfo.InvariantCulture), err2.ToString("R", CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("сцена: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine("сцен {0}; матриц склада рядом {1}, годных {2}", files.Count, withMatrix, valid);
        return 0;
    }

    static string Sha(byte[] data)
    {
        using (SHA256 sha = SHA256.Create())
        {
            var sb = new StringBuilder();
            foreach (byte b in sha.ComputeHash(data)) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
