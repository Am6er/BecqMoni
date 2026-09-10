using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;

// `E37`: КРИВАЯ ЭФФЕКТИВНОСТИ ОБЯЗАНА ПОКРЫВАТЬ РАБОЧУЮ ПОЛОСУ ПРИБОРА.
//
// Зачем читатель. 25.08.2026 нашлось, что у трёх спектров корпуса кривая
// начинается с 40 кэВ при `Min_Range` прибора 30 кэВ, то есть не покрывает
// даже объявленной полосы. Цена молчаливая: край кривой держится КОНСТАНТОЙ,
// и всякой линии ниже первой точки выдаётся эффективность первой точки —
// у `AS80_lu_front` это было 0.01012 вместо 0.04483 на 60 кэВ, вчетверо.
// Ни отказа, ни предупреждения при этом не печатал НИКТО: признак был, а
// потребителя у него не было — повторяющаяся болезнь этого дерева.
//
// Кривые с тех пор пересчитаны (09.09.2026), и сегодня нарушителей нет. Ровно
// поэтому читатель и заводится: пока его нет, следующая такая кривая приедет
// так же тихо, как приехала прошлая.
//
// Что читается. Каждый спектр корпуса: узел `<Efficiency><Curve>` (кривая,
// привязанная к спектру) против `Min_Range`/`Max_Range` конфигурации прибора,
// на которую спектр ссылается своим `DeviceConfigReference`.
//
// ⚠ Ловушка разбора, на которой уже спотыкались: тег `<Efficiency>`
// встречается ДВАЖДЫ — как узел снимка кривой и как поле точки внутри неё.
// Поэтому берётся не он, а `<Curve>`, и точки читаются узлами XML, а не
// образцом по тексту.
//
//   effcoverprobe [--spectra=tools\CORPUS\corpus\spectra]
//                 [--devices=tools\CORPUS\corpus\devices] [--quiet]
//
// Код возврата: 0 — все кривые покрывают полосу своего прибора; 1 — есть
// непокрытые (названы поимённо); 2 — не найдены каталоги.
class EffCoverProbe
{
    sealed class Device
    {
        public string Name;
        public double Min = double.NaN;
        public double Max = double.NaN;
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string spectra = Path.Combine("tools", "CORPUS", "corpus", "spectra");
        string devices = Path.Combine("tools", "CORPUS", "corpus", "devices");
        bool quiet = false;

        foreach (string a in args)
        {
            if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectra = a.Substring(10);
            else if (a.StartsWith("--devices=", StringComparison.Ordinal)) devices = a.Substring(10);
            else if (a == "--quiet") quiet = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        if (!Directory.Exists(spectra) || !Directory.Exists(devices))
        {
            Console.Error.WriteLine("нет каталога спектров или приборов: "
                                    + spectra + " / " + devices);
            return 2;
        }

        Dictionary<string, Device> byGuid = ReadDevices(devices);
        Console.WriteLine("конфигураций приборов: {0}", byGuid.Count);

        int withCurve = 0, noCurve = 0, noDevice = 0, covered = 0;
        List<string> bad = new List<string>();
        string[] files = Directory.GetFiles(spectra, "*.xml");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            XmlDocument doc = new XmlDocument();
            doc.Load(file);

            XmlNode curve = doc.SelectSingleNode("//Efficiency/Curve");
            if (curve == null)
            {
                noCurve++;
                continue;
            }

            double lo = double.MaxValue, hi = double.MinValue;
            int points = 0;
            foreach (XmlNode e in curve.SelectNodes(".//Energy"))
            {
                double v;
                if (!double.TryParse(e.InnerText, NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out v))
                {
                    continue;
                }

                points++;
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }

            if (points == 0)
            {
                noCurve++;
                continue;
            }

            withCurve++;
            XmlNode guidNode = doc.SelectSingleNode("//DeviceConfigReference/Guid");
            Device dev = null;
            if (guidNode != null)
            {
                byGuid.TryGetValue(guidNode.InnerText.Trim().ToLowerInvariant(), out dev);
            }

            if (dev == null)
            {
                noDevice++;
                bad.Add(string.Format(CultureInfo.InvariantCulture,
                                      "  {0,-24} кривая {1:F1}..{2:F1} кэВ ({3} точек)"
                                      + " — КОНФИГУРАЦИИ ПРИБОРА НЕТ, полосу сверить не с чем",
                                      name, lo, hi, points));
                continue;
            }

            bool lowBad = !double.IsNaN(dev.Min) && lo > dev.Min + 1e-9;
            bool highBad = !double.IsNaN(dev.Max) && hi < dev.Max - 1e-9;
            if (!lowBad && !highBad)
            {
                covered++;
                continue;
            }

            bad.Add(string.Format(CultureInfo.InvariantCulture,
                                  "  {0,-24} {1,-42} кривая {2:F1}..{3:F1}, прибор {4:F1}..{5:F1}"
                                  + " — не покрыт {6}",
                                  name, dev.Name, lo, hi, dev.Min, dev.Max,
                                  lowBad && highBad ? "НИЗ и ВЕРХ" : (lowBad ? "НИЗ" : "ВЕРХ")));
        }

        Console.WriteLine("спектров: {0}; с кривой {1}, без кривой {2}",
                          files.Length, withCurve, noCurve);
        Console.WriteLine("покрывают полосу своего прибора: {0}; НЕ покрывают: {1}"
                          + " (из них без конфигурации прибора {2})",
                          covered, bad.Count, noDevice);

        if (bad.Count == 0)
        {
            if (!quiet)
            {
                Console.WriteLine("СОШЛОСЬ: кривых, обрывающихся внутри рабочей полосы прибора, нет");
            }

            return 0;
        }

        Console.WriteLine("⛔ ОТКАЗ: край кривой держится константой, и линиям за краем"
                          + " выдаётся эффективность края");
        foreach (string s in bad)
        {
            Console.WriteLine(s);
        }

        return 1;
    }

    static Dictionary<string, Device> ReadDevices(string dir)
    {
        Dictionary<string, Device> map = new Dictionary<string, Device>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(dir, "*.xml"))
        {
            XmlDocument doc = new XmlDocument();
            doc.Load(file);
            XmlNode guid = doc.SelectSingleNode("//Guid");
            if (guid == null)
            {
                continue;
            }

            Device d = new Device { Name = Path.GetFileNameWithoutExtension(file) };
            XmlNode nameNode = doc.SelectSingleNode("//Name");
            if (nameNode != null && nameNode.InnerText.Trim().Length > 0)
            {
                d.Name = nameNode.InnerText.Trim();
            }

            double v;
            XmlNode min = doc.SelectSingleNode("//Min_Range");
            if (min != null && double.TryParse(min.InnerText, NumberStyles.Float,
                                               CultureInfo.InvariantCulture, out v))
            {
                d.Min = v;
            }

            XmlNode max = doc.SelectSingleNode("//Max_Range");
            if (max != null && double.TryParse(max.InnerText, NumberStyles.Float,
                                               CultureInfo.InvariantCulture, out v))
            {
                d.Max = v;
            }

            map[guid.InnerText.Trim().ToLowerInvariant()] = d;
        }

        return map;
    }
}
