using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace RoiSupplyProbe
{
    /// <summary>
    /// ЧИТАЮТСЯ ЛИ ПОСТАВОЧНЫЕ КОНФИГУРАЦИИ ROI (`A167`) — вердикт по каждому
    /// файлу, кодом самого приложения.
    ///
    ///     roisupplyprobe [--supply=&lt;каталог поставки&gt;] [--noinit] [--control]
    ///
    /// Грузит НАСТОЯЩИЙ <c>ROIConfigManager</c> из каталога сборки
    /// (<c>Package.ROIDir</c>) и печатает по каждому файлу «взят» либо «ОТКАЗ»
    /// с названной причиной. Ожидание — взяты ВСЕ.
    ///
    /// ⛔ «Настоящие поставочные» — это СВЕРЕНО, а не объявлено: с ключом
    /// <c>--supply=</c> каждый файл рядом с пробой сличается по sha256 с
    /// одноимённым файлом поставки. Без сверки проба доказывала бы лишь то, что
    /// читается какая-то дюжина файлов.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ — <c>--control</c>. Рядом с поставочными
    /// кладутся два заведомо негодных файла, и оба ОБЯЗАНЫ отказать с названной
    /// причиной; проба, у которой всё проходит, не мерит ничего. Файлы разные
    /// нарочно: <c>__control_torn.xml</c> рвётся на разборе XML, а
    /// <c>__control_unknown_primitive.xml</c> — на подстановке примитива зоны,
    /// то есть ровно на том шаге, из-за которого и заведена <c>A167</c>.
    /// Портить каталог САМОЙ поставки проба отказывается (см. проверку ниже):
    /// эти файлы — данные Amber.
    ///
    /// ⚠ Плечо <c>--noinit</c> воспроизводит сцену, на которой беда была
    /// найдена: карты примитивов зоны НЕ заполнены (их заполняет
    /// <c>MainForm</c>, а всякий, кто добрался до менеджера раньше, их не
    /// имеет). Это плечо ОБЯЗАНО отказывать до правки и обязано брать все файлы
    /// после неё.
    /// </summary>
    static class Program
    {
        const string TornName = "__control_torn.xml";
        const string UnknownName = "__control_unknown_primitive.xml";

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string supply = null;
            bool noInit = false;
            bool control = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--supply=", StringComparison.Ordinal)) supply = a.Substring(9);
                else if (a == "--noinit") noInit = true;
                else if (a == "--control") control = true;
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 3;
                }
            }

            string roiDir = Package.GetInstance().ROIDir;
            Console.WriteLine("=== поставочные конфигурации ROI ===");
            Console.WriteLine("каталог менеджера: {0}", roiDir);
            Console.WriteLine("карты примитивов зоны: {0}", noInit ? "НЕ заполнены (плечо --noinit)" : "заполнены, как это делает MainForm");

            if (!Directory.Exists(roiDir))
            {
                Console.Error.WriteLine("нет каталога: " + roiDir);
                return 2;
            }

            var expected = new List<string>();
            foreach (string p in Directory.GetFiles(roiDir, "*.xml")) expected.Add(Path.GetFileName(p));
            expected.Sort(StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("файлов рядом с пробой: {0}", expected.Count);

            // ---- сверка с поставкой по sha256 -------------------------------
            int mismatched = 0;
            if (supply != null)
            {
                Console.WriteLine();
                Console.WriteLine("--- сверка с поставкой: {0} ---", supply);
                if (!Directory.Exists(supply))
                {
                    Console.Error.WriteLine("нет каталога поставки: " + supply);
                    return 3;
                }
                var supplyNames = new List<string>();
                foreach (string p in Directory.GetFiles(supply, "*.xml")) supplyNames.Add(Path.GetFileName(p));
                supplyNames.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string name in supplyNames)
                {
                    string here = Path.Combine(roiDir, name);
                    if (!File.Exists(here))
                    {
                        Console.WriteLine("  НЕТ РЯДОМ  {0}", name);
                        mismatched++;
                        continue;
                    }
                    string a = Sha(here), b = Sha(Path.Combine(supply, name));
                    if (a != b)
                    {
                        Console.WriteLine("  РАЗОШЁЛСЯ  {0}  {1} != {2}", name, a, b);
                        mismatched++;
                    }
                }
                Console.WriteLine("сверено {0}, разошлось {1}", supplyNames.Count, mismatched);
                if (supplyNames.Count != expected.Count)
                {
                    Console.WriteLine("⚠ рядом с пробой {0} файлов, в поставке {1}", expected.Count, supplyNames.Count);
                }
            }

            // ---- положительный контроль -------------------------------------
            var controls = new List<string>();
            if (control)
            {
                if (supply != null && Same(roiDir, supply))
                {
                    Console.Error.WriteLine("ОТКАЗ: --control портил бы САМ каталог поставки; гоняйте в копии");
                    return 3;
                }
                File.WriteAllText(Path.Combine(roiDir, TornName),
                    "<?xml version=\"1.0\"?>\r\n<ROIConfigData><Name>оборван", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(roiDir, UnknownName), BrokenPrimitiveXml(), new UTF8Encoding(false));
                controls.Add(TornName);
                controls.Add(UnknownName);
                Console.WriteLine();
                Console.WriteLine("--- положительный контроль: подложены {0} и {1} ---", TornName, UnknownName);
            }

            if (!noInit)
            {
                ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
                ROIPrimitiveOperation.InitializeROIPrimitiveOperations();
            }

            // ---- загрузка кодом приложения ----------------------------------
            TextWriter saved = Console.Error;
            StringWriter captured = new StringWriter();
            List<ROIConfigData> loaded;
            try
            {
                Console.SetError(captured);
                loaded = ROIConfigManager.GetInstance().ROIConfigList;
            }
            finally
            {
                Console.SetError(saved);
            }

            string[] said = captured.ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            string all = string.Join(" ", said);

            var taken = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (ROIConfigData c in loaded)
            {
                if (c != null && c.Filename != null) taken[c.Filename] = true;
            }

            Console.WriteLine();
            Console.WriteLine("=== вердикт по каждому файлу ===");
            int good = 0;
            foreach (string name in expected)
            {
                if (taken.ContainsKey(name)) { Console.WriteLine("  взят   {0}", name); good++; }
                else
                {
                    Console.WriteLine("  ОТКАЗ  {0}   причина: {1}", name, Why(roiDir, name));
                    Console.WriteLine("         дверь назвала файл: {0}", Named(all, name) ? "да" : "МОЛЧОК");
                }
            }

            int controlMute = 0;
            if (controls.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("=== положительный контроль ===");
                foreach (string name in controls)
                {
                    string why = Why(roiDir, name);
                    bool refused = !taken.ContainsKey(name);
                    bool named = Named(all, name);
                    bool reason = why != null && all.IndexOf(why, StringComparison.Ordinal) >= 0;
                    Console.WriteLine("  {0,-34} {1}, имя {2}, причина «{3}» {4}",
                                      name,
                                      refused ? "ОТКАЗАН" : "ПРОШЁЛ (это провал контроля)",
                                      named ? "названо" : "МОЛЧОК",
                                      why ?? "?",
                                      reason ? "названа" : "МОЛЧОК");
                    if (!refused || !named || !reason) controlMute++;
                    try { File.Delete(Path.Combine(roiDir, name)); } catch (Exception) { }
                }
            }

            Console.WriteLine();
            Console.WriteLine("сказано человеку строк: {0}", said.Length);
            foreach (string line in said) Console.WriteLine("  сказано: {0}", line);

            // ⚠ `expected` собран ДО того, как подложены контрольные файлы, —
            // значит он и есть перечень ПОСТАВОЧНЫХ, вычитать из него нечего.
            int supplyTotal = expected.Count;
            int supplyGood = good;

            Console.WriteLine();
            Console.WriteLine("ИТОГ: взято {0} из {1} поставочных", supplyGood, supplyTotal);

            int code = 0;
            if (supplyGood != supplyTotal) { Console.WriteLine("НЕ СОШЛОСЬ: отказали {0} поставочных", supplyTotal - supplyGood); code = 1; }
            if (mismatched != 0) { Console.WriteLine("НЕ СОШЛОСЬ: с поставкой разошлось {0} файлов", mismatched); code = 1; }
            if (controlMute != 0) { Console.WriteLine("НЕ СОШЛОСЬ: положительный контроль не сработал у {0} файлов", controlMute); code = 1; }
            if (code == 0) Console.WriteLine("СОШЛИСЬ: все поставочные конфигурации ROI прочитаны" + (controls.Count > 0 ? ", негодные отказаны с названной причиной" : ""));
            return code;
        }

        /// <summary>
        /// Имя класса исключения, которым отказывает разбор этого файла, —
        /// добыто ТЕМИ ЖЕ шагами, что делает менеджер. Нужно затем, что дверь
        /// печатает причину, и её надо с чем-то сличать: подпись «Причина:»
        /// для сверки не годится — она переведена.
        /// </summary>
        static string Why(string dir, string name)
        {
            string path = Path.Combine(dir, name);
            try
            {
                ROIConfigData config;
                var serializer = new XmlSerializer(typeof(ROIConfigData));
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    config = (ROIConfigData)serializer.Deserialize(stream);
                }
                foreach (ROIDefinitionData roi in config.ROIDefinitions)
                {
                    foreach (ROIPrimitiveData primitive in roi.ROIPrimitives)
                    {
                        GC.KeepAlive(ROIPrimitiveDefinition.DefinitionsMap[primitive.PrimitiveType]);
                        GC.KeepAlive(ROIPrimitiveOperation.OperationsMap[primitive.OperationType]);
                    }
                }
                return null;
            }
            catch (Exception error)
            {
                return error.GetType().Name;
            }
        }

        static bool Named(string said, string name)
        {
            return said.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool Same(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        static string Sha(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Substring(0, 12).ToLowerInvariant();
            }
        }

        static string BrokenPrimitiveXml()
        {
            return "<?xml version=\"1.0\"?>\r\n"
                 + "<ROIConfigData xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n"
                 + "  <FormatVersion>120920</FormatVersion>\r\n"
                 + "  <Guid>00000000-0000-0000-0000-0000000c0de1</Guid>\r\n"
                 + "  <Name>контроль: примитив, которого нет</Name>\r\n"
                 + "  <ROIDefinitions>\r\n"
                 + "    <ROIDefinitionData>\r\n"
                 + "      <Name>зона</Name>\r\n"
                 + "      <Enabled>true</Enabled>\r\n"
                 + "      <PeakEnergy>661.657</PeakEnergy>\r\n"
                 + "      <LowerLimit>600</LowerLimit>\r\n"
                 + "      <UpperLimit>700</UpperLimit>\r\n"
                 + "      <Color>#FF0000</Color>\r\n"
                 // ⚠ Имя элемента — НЕ `ROIPrimitiveData`, а имя ПОДКЛАССА:
                 // список примитивов зоны разбирается по подклассам, и
                 // «ROIPrimitiveData» разбор пропускает молча — контроль тогда
                 // проходит, ничего не проверив. Поймано на себе 05.09.2026.
                 + "      <ROIPrimitives>\r\n"
                 + "        <ROISimpleDifferenceData>\r\n"
                 + "          <PrimitiveType>ТАКОГО ПРИМИТИВА НЕТ</PrimitiveType>\r\n"
                 + "          <OperationType>Addition</OperationType>\r\n"
                 + "          <Coefficient>1</Coefficient>\r\n"
                 + "          <CoefficientError>0</CoefficientError>\r\n"
                 + "          <LowerLimit>600</LowerLimit>\r\n"
                 + "          <UpperLimit>700</UpperLimit>\r\n"
                 + "        </ROISimpleDifferenceData>\r\n"
                 + "      </ROIPrimitives>\r\n"
                 + "    </ROIDefinitionData>\r\n"
                 + "  </ROIDefinitions>\r\n"
                 + "</ROIConfigData>\r\n";
        }
    }
}
