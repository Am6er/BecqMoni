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
    ///
    /// ⛔ ВТОРОЙ ПРОХОД — КОДОМ ПРИЛОЖЕНИЯ, И ОН ЗДЕСЬ ГЛАВНЫЙ (`A22`). Первый
    /// проход разбирает файлы ЗАНОВО, своим кодом, и потому доказывает лишь то,
    /// что причина отказа ВООБЩЕ познаваема, — а вопрос был в другом: доезжает
    /// ли она до человека за экраном. Поэтому те же файлы грузятся ещё раз,
    /// настоящим <c>ROIConfigManager.LoadAllConfigFiles</c>, а поток ошибок на
    /// это время перехватывается: без окон единственная дверь приложения
    /// (<c>AppUi.Report</c>) пишет ровно туда, и в окнах тем же текстом
    /// поднимается модальное окно. Что напечатала эта дверь — то и увидит
    /// человек.
    ///
    /// Сверка машинная, а не на глаз: у каждого файла, отказавшего в ПЕРВОМ
    /// проходе, в сообщении приложения обязаны стоять и его имя, и имя того же
    /// класса исключения. Подпись «Причина:» для сверки не годится — она
    /// переведена, и на разных машинах читается по-разному.
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

            // Список примитивов зоны нужен ОБОИМ проходам, и заводится он до
            // менеджеров-одиночек: разбор без него падает на подстановке
            // `DefinitionsMap` у КАЖДОГО файла.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine("нет каталога: " + dir);
                // ⚠ Второй проход идёт ВСЁ РАВНО: «каталога ROI нет вовсе» —
                // это отдельный отказ менеджера (верхний `catch`), и молчать о
                // нём он тоже не должен. Код возврата остаётся прежним — 2.
                AppDoor(dir, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                return 2;
            }

            // Список примитивов зоны заводит `MainForm` при запуске (заведён
            // выше, до проверки каталога): без него разбор падает на подстановке
            // `DefinitionsMap` — и падал бы у КАЖДОГО файла, то есть проба
            // показала бы не то, что ищем.
            var serializer = new XmlSerializer(typeof(ROIConfigData));
            string[] files = Directory.GetFiles(dir, "*.xml");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            Console.WriteLine("=== разбор конфигураций ROI ===");
            Console.WriteLine("каталог: {0}, файлов: {1}", dir, files.Length);

            var bad = new List<string>();
            // Имя файла -> имя класса исключения: по нему второй проход сверяет,
            // что приложение назвало ТУ ЖЕ причину, а не какую-нибудь.
            var reason = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                    reason[Path.GetFileName(path)] = error.GetType().Name;
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
                Console.WriteLine("разобрались все {0}", files.Length);
            }
            else
            {
                Console.WriteLine("не разобрались: {0} из {1} — {2}",
                                  bad.Count, files.Length, string.Join(", ", bad.ToArray()));
            }

            int mute = AppDoor(dir, reason);
            Console.WriteLine();
            if (mute == 0)
            {
                Console.WriteLine("СОШЛИСЬ: причина отказа доезжает до человека");
                return 0;
            }

            Console.WriteLine("НЕ СОШЛОСЬ: приложение промолчало о причине в {0} случаях", mute);
            return 1;
        }

        /// <summary>
        /// Та же загрузка — НАСТОЯЩИМ менеджером приложения, с перехватом того,
        /// что он сказал человеку (`A22`).
        ///
        /// Возвращает число файлов, о которых приложение промолчало или сказало
        /// не то: у каждого отказавшего в первом проходе в сообщении обязаны
        /// стоять и имя файла, и имя класса исключения.
        ///
        /// ⚠ Каталог у менеджера СВОЙ и ключом пробы не двигается: он считается
        /// от каталога сборки (<c>Package</c>). Когда он не совпал с тем, что
        /// разбирал первый проход, сверять нечего — тогда печатается сказанное
        /// приложением, и только.
        /// </summary>
        static int AppDoor(string dir, Dictionary<string, string> reason)
        {
            Console.WriteLine();
            Console.WriteLine("=== та же загрузка кодом приложения ===");

            string appDir = Package.GetInstance().ROIDir;
            Console.WriteLine("каталог менеджера: {0}", appDir);

            TextWriter saved = Console.Error;
            StringWriter captured = new StringWriter();
            int loaded;
            try
            {
                Console.SetError(captured);
                loaded = ROIConfigManager.GetInstance().ROIConfigList.Count;
            }
            finally
            {
                Console.SetError(saved);
            }

            string[] said = captured.ToString()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Console.WriteLine("взято конфигураций: {0}, сказано человеку строк: {1}", loaded, said.Length);
            foreach (string line in said)
            {
                Console.WriteLine("  сказано: {0}", line);
            }

            bool sameDir;
            try
            {
                sameDir = string.Equals(Path.GetFullPath(appDir).TrimEnd('\\'),
                                        Path.GetFullPath(dir).TrimEnd('\\'),
                                        StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                sameDir = false;
            }

            if (!sameDir)
            {
                Console.WriteLine("  (каталоги разные — сверять с первым проходом нечего)");
                return 0;
            }

            string all = string.Join(" ", said);
            int mute = 0;
            foreach (KeyValuePair<string, string> pair in reason)
            {
                bool named = all.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0;
                bool why = all.IndexOf(pair.Value, StringComparison.Ordinal) >= 0;
                Console.WriteLine("  {0,-42} имя {1}, причина «{2}» {3}",
                                  pair.Key, named ? "названо" : "МОЛЧОК",
                                  pair.Value, why ? "названа" : "МОЛЧОК");
                if (!named || !why)
                {
                    mute++;
                }
            }

            if (reason.Count == 0)
            {
                Console.WriteLine("  (отказавших файлов нет — проверять нечего; испортить конфиг и повторить)");
            }

            return mute;
        }
    }
}
