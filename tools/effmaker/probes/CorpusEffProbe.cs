using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

// B1, шаг 3: привязать посчитанное к спектрам понятной части.
//
// Зачем отдельный шаг. Геометрия и матрица, лежащие файлами рядом с корпусом,
// сами по себе не значат ничего: разбор берёт матрицу у `ResultData.Efficiency`
// по её Guid (`FsaOverlay` -> `ResponseMatrixStore.Load`), а кривую — оттуда же.
// Спектр без этой записи считается СТАРЫМ путём, и «понятная часть» осталась бы
// понятной только на бумаге. Это ровно та ошибка, которой в проекте уже есть
// имя: признак заведён, потребитель не написан.
//
// Что делается на каждую геометрию:
//
//   1. считается КРИВАЯ тем же расчётом, что и в приложении
//      (`EfficiencyCalculation.Run`, умолчания: 40–3000 кэВ, штатная сетка,
//      200 тыс. историй на узел) — она нужна разбору наравне с матрицей;
//   2. собирается `EfficiencyConfigData` с ПОСТОЯННЫМ Guid, выведенным из
//      имени геометрии. Постоянным, а не случайным, нарочно: Guid — это ключ,
//      по которому ищется файл матрицы, и случайный при каждом прогоне
//      оставлял бы сиротами все прежние `.rmx`;
//   3. матрица кладётся под этим Guid в `geometries/response/<guid>.rmx` —
//      каталог готов к тому, чтобы лечь в рабочий каталог прогона целиком;
//   4. запись вставляется в КАЖДЫЙ спектр этой геометрии.
//
// Вставка ХИРУРГИЧЕСКАЯ, а не круговоротом через `XmlSerializer`: файл спектра
// несёт то, чего у нашей модели нет (у `ResultData` пятнадцать свойств помечены
// `[XmlIgnore]`, а в файлах корпуса лежит ещё и то, что писал сборщик), и
// круговорот вычистил бы это молча. Поэтому документ читается `XmlDocument`,
// узел `<Efficiency>` вставляется на своё место по порядку свойств
// (`SampleInfo`, `DeviceConfigReference`, `ROIConfigReference`, **Efficiency**,
// `BackgroundSpectrumFile`, …), остальное не трогается.
//
//   corpuseffprobe [--dir=tools\CORPUS\corpus\geometries]
//                  [--spectra=tools\CORPUS\corpus\spectra] [--n=200000]
//                  [--only=<ключ геометрии>] [--dry]
class CorpusEffProbe
{
    // Порядок свойств ResultData, по которому XmlSerializer читает файл:
    // Efficiency стоит сразу за этими тремя. Список короткий нарочно — он
    // повторяет ровно ту часть объявления, до которой нам есть дело.
    static readonly string[] Before =
        { "ROIConfigReference", "DeviceConfigReference", "SampleInfo" };

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        string dir = Path.Combine("tools", "CORPUS", "corpus", "geometries");
        string spectraDir = Path.Combine("tools", "CORPUS", "corpus", "spectra");
        string only = null;
        bool dry = false;
        bool force = false;
        var options = new EfficiencyCalculationOptions();
        foreach (string a in args)
        {
            if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
            else if (a.StartsWith("--spectra=", StringComparison.Ordinal)) spectraDir = a.Substring(10);
            else if (a.StartsWith("--n=", StringComparison.Ordinal))
                options.Histories = int.Parse(a.Substring(4), CultureInfo.InvariantCulture);
            else if (a.StartsWith("--only=", StringComparison.Ordinal)) only = a.Substring(7);
            else if (a == "--force") force = true;
            else if (a == "--dry") dry = true;
            else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
        }

        string indexPath = Path.Combine(dir, "index.csv");
        if (!File.Exists(indexPath))
        {
            Console.Error.WriteLine("нет описи " + indexPath + " — сначала corpusgeomprobe");
            return 2;
        }

        GlobalConfigManager.GetInstance();

        var byGeometry = new Dictionary<string, List<string>>();
        var order = new List<string>();
        bool head = true;
        foreach (string line in File.ReadAllLines(indexPath))
        {
            if (head) { head = false; continue; }
            if (line.Length == 0) continue;
            string[] parts = line.Split(',');
            if (!byGeometry.ContainsKey(parts[0]))
            {
                byGeometry[parts[0]] = new List<string>();
                order.Add(parts[0]);
            }

            byGeometry[parts[0]].Add(parts[1]);
        }

        string responseDir = Path.Combine(dir, "response");
        bool ok = true;
        int attached = 0;
        int skippedCurves = 0;

        Console.WriteLine("Привязка кривой и матрицы к спектрам понятной части (B1)");
        Console.WriteLine("кривая: {0:F0}-{1:F0} кэВ, {2} историй на узел",
                          options.MinEnergyKev, options.MaxEnergyKev, options.Histories);
        Console.WriteLine();

        foreach (string key in order)
        {
            // --only= держит пересчёт хирургическим: кривая — Монте-Карло, и
            // прогон «на все геометрии» ПЕРЕПИСАЛ бы уже привязанные кривые
            // понятной части свежим шумом — база уехала бы молча.
            if (only != null && key != only)
            {
                continue;
            }

            string geomPath = Path.Combine(dir, key + ".in");
            string matrixPath = Path.Combine(dir, key + ".rmx");
            Console.WriteLine("== {0} ==", key);
            if (!File.Exists(geomPath))
            {
                Console.WriteLine("   НЕТ ГЕОМЕТРИИ {0}", geomPath);
                ok = false;
                continue;
            }

            GeometryModel geometry = GeometryModel.Load(geomPath);
            string guid = StableGuid(key);

            // ГВАРД ГЛОБАЛЬНОГО ПЕРЕСЧЁТА (указание Amber 16.08.2026), пара к
            // такому же в `CorpusMatrixProbe`. Кривая — Монте-Карло, и считать
            // её заново, когда ни геометрия, ни физика не менялись, значит жечь
            // время (16.08 так ушло 35 минут) и рисковать сдвигом базы.
            //
            // Признак «готово» берётся из САМОГО узла в спектре: он хранит и
            // геометрию, которой посчитан, и клеймо (версия физики, число
            // историй, сетка). Сошлись обе — считать нечего. Поднимется версия
            // физики — не сойдётся сразу у всех, и пересчёт станет глобальным
            // сам, без ключа: ровно тот случай, ради которого глобальный и
            // нужен.
            if (!force && CurveIsCurrent(spectraDir, byGeometry[key], geometry, guid, options))
            {
                Console.WriteLine("   пропущена: клеймо и геометрия сошлись у всех её спектров");
                Console.WriteLine();
                skippedCurves++;
                continue;
            }

            EfficiencyFitResult result = EfficiencyCalculation.Run(geometry, options, null, null);
            if (!string.IsNullOrEmpty(result.Error))
            {
                Console.WriteLine("   РАСЧЁТ КРИВОЙ НЕ ПОШЁЛ: {0}", result.Error);
                ok = false;
                continue;
            }

            var config = new EfficiencyConfigData(key)
            {
                Guid = guid,
                Origin = EfficiencyOrigin.Simulation,
                Geometry = geometry,
                Curve = result.Curve,
                ComputeStamp = result.ComputeStamp ?? "",
                UseResponseMatrix = true,
            };

            Console.WriteLine("   guid     : {0}", guid);
            Console.WriteLine("   кривая   : {0} точек, клеймо {1}",
                              config.Curve.Count, config.ComputeStamp);

            if (File.Exists(matrixPath))
            {
                if (!dry)
                {
                    Directory.CreateDirectory(responseDir);
                    File.Copy(matrixPath, Path.Combine(responseDir, guid + ".rmx"), true);
                }

                Console.WriteLine("   матрица  : {0} -> response\\{1}.rmx", key + ".rmx", guid);
            }
            else
            {
                Console.WriteLine("   МАТРИЦЫ НЕТ ({0}) — сначала corpusmatrixprobe", matrixPath);
                ok = false;
            }

            string fragment = Serialize(config);
            foreach (string spectrum in byGeometry[key])
            {
                string path = Path.Combine(spectraDir, spectrum + ".xml");
                if (!File.Exists(path))
                {
                    Console.WriteLine("   НЕТ СПЕКТРА {0}", path);
                    ok = false;
                    continue;
                }

                int touched = dry ? Count(path) : Attach(path, fragment);
                attached += touched;
                Console.WriteLine("   спектр   : {0} ({1} записей ResultData)", spectrum, touched);
            }

            Console.WriteLine();
        }

        Console.WriteLine("геометрий: {0} — посчитано {1}, пропущено {2} (клеймо и геометрия"
                          + " сошлись); записей проставлено: {3}{4}",
                          order.Count, order.Count - skippedCurves, skippedCurves, attached,
                          dry ? " (--dry, ничего не записано)" : "");
        if (skippedCurves == order.Count && order.Count > 0)
        {
            Console.WriteLine("ничего не изменилось — пересчитывать было нечего");
        }
        Console.WriteLine(ok ? "ВСЕ СОШЛИСЬ" : "ЕСТЬ НЕСОШЕДШИЕСЯ");
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Guid, выведенный из имени геометрии: одно и то же имя даёт один и тот же
    /// Guid в любом прогоне. MD5 берётся не ради стойкости, а ради ста двадцати
    /// восьми бит — ровно столько в Guid.
    /// </summary>
    static string StableGuid(string key)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("corpus-efficiency:" + key));
            return new Guid(hash).ToString();
        }
    }

    /// <summary>
    /// Кривая геометрии уже посчитана И ровно из этого: у КАЖДОГО её спектра
    /// стоит узел с тем же guid, тем же клеймом расчёта и той же геометрией.
    ///
    /// Сверяется не «есть ли узел», а ЧЕМ он посчитан. Геометрия сравнивается
    /// отрисовкой тем же писателем, что кладёт её на диск, — так же, как это
    /// делает отпечаток матрицы; клеймо даёт версию физики и число историй.
    /// Хватает одного спектра без узла или с чужим, чтобы кривую пересчитать:
    /// «почти у всех» — это молчаливая дыра, а не готовность.
    /// </summary>
    static bool CurveIsCurrent(string spectraDir, List<string> spectra,
                               GeometryModel geometry, string guid,
                               EfficiencyCalculationOptions options)
    {
        string want;
        try
        {
            want = GeometryWriter.Render(geometry);
        }
        catch (Exception)
        {
            return false;
        }

        string physWant = "phys=" + ResponseMatrix.PhysicsVersion.ToString(CultureInfo.InvariantCulture) + ";";
        string histWant = "hist=" + options.Histories.ToString(CultureInfo.InvariantCulture) + ";";

        foreach (string spectrum in spectra)
        {
            string path = Path.Combine(spectraDir, spectrum + ".xml");
            if (!File.Exists(path))
            {
                return false;
            }

            EfficiencyConfigData have = ReadNode(path);
            if (have == null || have.Geometry == null)
            {
                return false;
            }

            if (!string.Equals(have.Guid, guid, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string stamp = have.ComputeStamp ?? "";
            if (stamp.IndexOf(physWant, StringComparison.Ordinal) < 0
                || stamp.IndexOf(histWant, StringComparison.Ordinal) < 0)
            {
                return false;
            }

            try
            {
                if (!string.Equals(GeometryWriter.Render(have.Geometry), want, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        return spectra.Count > 0;
    }

    /// <summary>Узел `&lt;Efficiency&gt;` первой записи спектра; null, если его нет.</summary>
    static EfficiencyConfigData ReadNode(string path)
    {
        try
        {
            var document = new XmlDocument();
            document.Load(path);
            XmlNode node = document.SelectSingleNode("//ResultDataList/ResultData/Efficiency");
            if (node == null || node.SelectSingleNode("Guid") == null)
            {
                return null;
            }

            var serializer = new XmlSerializer(typeof(EfficiencyConfigData),
                                               new XmlRootAttribute("Efficiency"));
            using (var reader = new StringReader(node.OuterXml))
            {
                return (EfficiencyConfigData)serializer.Deserialize(reader);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string Serialize(EfficiencyConfigData config)
    {
        var serializer = new XmlSerializer(typeof(EfficiencyConfigData),
                                           new XmlRootAttribute("Efficiency"));
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("", "");
        var settings = new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true };
        var text = new StringBuilder();
        using (XmlWriter writer = XmlWriter.Create(text, settings))
        {
            serializer.Serialize(writer, config, namespaces);
        }

        return text.ToString();
    }

    static int Count(string path)
    {
        var document = new XmlDocument();
        document.Load(path);
        return document.SelectNodes("//ResultDataList/ResultData").Count;
    }

    static int Attach(string path, string fragment)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.Load(path);
        int touched = 0;
        foreach (XmlNode data in document.SelectNodes("//ResultDataList/ResultData"))
        {
            var holder = new XmlDocument();
            holder.LoadXml(fragment);
            XmlNode node = document.ImportNode(holder.DocumentElement, true);

            XmlNode existing = data["Efficiency"];
            if (existing != null)
            {
                data.ReplaceChild(node, existing);
            }
            else
            {
                XmlNode after = null;
                foreach (string name in Before)
                {
                    after = data[name];
                    if (after != null) break;
                }

                if (after != null) data.InsertAfter(node, after);
                else data.PrependChild(node);
            }

            touched++;
        }

        document.Save(path);
        return touched;
    }
}
