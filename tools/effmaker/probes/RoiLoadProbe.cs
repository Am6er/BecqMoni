using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace RoiLoadProbe
{
    /// <summary>
    /// ПОЧЕМУ не грузится конфигурация ROI — вслух.
    ///
    /// `ROIConfigManager.LoadConfigs` ловит `catch (Exception)` и показывает
    /// модальное окно «Не удалось загрузить конфигурационный файл ROI» с одним
    /// лишь путём. Причина при этом теряется целиком, а в безоконном прогоне
    /// окно ещё и вешает пробу навсегда — так `BqCoeffProbe` простоял 23.08.2026
    /// больше десяти минут, и «проба молчит» выглядело как «проба считает».
    ///
    ///     roiloadprobe [--dir=config\ROI]
    ///
    /// Здесь тот же разбор делается ЯВНО и без окон: файл за файлом, с печатью
    /// исключения и места, где оно вылезло. Ожидание — «СОШЛИСЬ: разобрались
    /// все»; иначе перечень поимённо с причиной.
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "ROI");
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal))
                {
                    dir = a.Substring(6);
                }
            }

            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine("нет каталога: " + dir);
                return 2;
            }

            // Список примитивов зоны заводит `MainForm` при запуске; без него
            // разбор падает на подстановке `DefinitionsMap` — и падал бы у
            // КАЖДОГО файла, то есть проба показала бы не то, что ищем.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            var serializer = new XmlSerializer(typeof(ROIConfigData));
            string[] files = Directory.GetFiles(dir, "*.xml");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("=== разбор конфигураций ROI ===");
            Console.WriteLine("каталог: {0}, файлов: {1}", dir, files.Length);

            var bad = new List<string>();
            foreach (string path in files)
            {
                string stage = "чтение";
                try
                {
                    ROIConfigData config;
                    using (var reader = new StreamReader(path, Encoding.UTF8))
                    {
                        config = (ROIConfigData)serializer.Deserialize(reader);
                    }

                    stage = "подстановка примитивов";
                    int zones = 0, primitives = 0;
                    foreach (ROIDefinitionData roi in config.ROIDefinitions)
                    {
                        zones++;
                        foreach (ROIPrimitiveData primitive in roi.ROIPrimitives)
                        {
                            primitives++;
                            object unusedPrimitive =
                                ROIPrimitiveDefinition.DefinitionsMap[primitive.PrimitiveType];
                            object unusedOperation =
                                ROIPrimitiveOperation.OperationsMap[primitive.OperationType];
                            GC.KeepAlive(unusedPrimitive);
                            GC.KeepAlive(unusedOperation);
                        }
                    }

                    Console.WriteLine("  ok   {0,-42} зон {1,2}, примитивов {2,2}",
                                      Path.GetFileName(path), zones, primitives);
                }
                catch (Exception error)
                {
                    bad.Add(Path.GetFileName(path));
                    Exception inner = error;
                    while (inner.InnerException != null)
                    {
                        inner = inner.InnerException;
                    }

                    Console.WriteLine("  ОТКАЗ {0,-42} на шаге «{1}»", Path.GetFileName(path), stage);
                    Console.WriteLine("        {0}: {1}", error.GetType().Name, error.Message);
                    if (!ReferenceEquals(inner, error))
                    {
                        Console.WriteLine("        причина: {0}: {1}", inner.GetType().Name, inner.Message);
                    }
                }
            }

            Console.WriteLine();
            if (bad.Count == 0)
            {
                Console.WriteLine("СОШЛИСЬ: разобрались все {0}", files.Length);
                return 0;
            }

            Console.WriteLine("НЕ СОШЛОСЬ: {0} из {1} — {2}",
                              bad.Count, files.Length, string.Join(", ", bad.ToArray()));
            return 1;
        }
    }
}
