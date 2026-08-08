using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Serialization;

namespace UnitsProbe
{
    /// <summary>
    /// Геометрия в МИЛЛИМЕТРАХ. Проверяется не «поле стало больше в десять
    /// раз» — это переписывание кода словами, — а три утверждения, от каждого
    /// из которых зависит число на экране:
    ///
    ///  1. Файл `.in` (в нём сантиметры по формату LSRM) читается в миллиметры.
    ///  2. Конфигурация эффективности пишется и читается в миллиметрах —
    ///     размеры круг переживают.
    ///  3. КРИВАЯ НЕ ИЗМЕНИЛАСЬ. Это главное: перевод единиц обязан быть
    ///     переименованием, а не физикой. Считается по геометрии из файла и
    ///     сверяется с кривой, лежащей в том же файле рядом с ней.
    ///
    ///   unitsprobe --in=&lt;модель.in&gt; --config=&lt;спектр или конфиг.xml&gt;
    ///              [--legacy-cm] [--n=200000] [--tol=3]
    ///
    /// `--legacy-cm`: геометрия в `--config` записана в САНТИМЕТРАХ — так
    /// выглядят файлы, сохранённые до перехода. Проба домножит её сама. В
    /// приложении такого пути нет и не будет: совместимость со старыми файлами
    /// не поддерживается (решение Amber 05.08.2026), а здесь старый файл нужен
    /// как единственный источник ЭТАЛОННОЙ кривой — посчитанной до перехода.
    ///
    /// Расхождение кривой считается в процентах и сравнивается с --tol. Число
    /// историй должно совпадать с тем, которым считали сверяемую кривую
    /// (в приложении это умолчание поля «Историй на точку» = 200000,
    /// оно же здесь по умолчанию): зерно постоянное, и при равном числе историй
    /// расхождение выходит ТОЧНЫМ нулём. Иное число историй само по себе даёт
    /// проценты разницы — это шум счёта, а не ошибка в единицах; ошибка в
    /// единицах даёт разы.
    /// </summary>
    static class Program
    {
        static int failed;

        static int Main(string[] args)
        {
            string inPath = null, configPath = null;
            int histories = 200000;
            double tolerance = 3.0;
            bool legacyCm = false;
            foreach (string arg in args)
            {
                int eq = arg.IndexOf('=');
                string key = eq > 0 ? arg.Substring(0, eq) : arg;
                string value = eq > 0 ? arg.Substring(eq + 1) : "";
                if (key == "--in") inPath = value;
                else if (key == "--config") configPath = value;
                else if (key == "--legacy-cm") legacyCm = true;
                else if (key == "--n") histories = int.Parse(value, CultureInfo.InvariantCulture);
                else if (key == "--tol") tolerance = double.Parse(value, CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("unknown: " + arg); return 1; }
            }

            Console.OutputEncoding = Encoding.UTF8;

            if (inPath != null)
            {
                CheckInFile(inPath);
            }

            CheckRoundTrip();

            if (configPath != null)
            {
                CheckCurveUnchanged(configPath, histories, tolerance, legacyCm);
            }

            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + failed);
            return failed == 0 ? 0 : 1;
        }

        // --------------------------------------------------------------
        // 1. Файл .in
        // --------------------------------------------------------------

        static void CheckInFile(string path)
        {
            Console.WriteLine("=== 1. Чтение {0}", Path.GetFileName(path));
            GeometryModel g = GeometryModel.Load(path);
            Console.WriteLine("    {0}", g.Describe());

            // Сравнивается с тем, что написано В САМОМ ФАЙЛЕ: числа не
            // прописываются в пробе, иначе она проверяла бы свою же копию.
            Dictionary<string, double> raw = ReadCm(path);
            Check("DS_CrystalDiameter", g.CrystalDiameter, raw, "DS_CrystalDiameter");
            Check("DS_CrystalHeight", g.CrystalHeight, raw, "DS_CrystalHeight");
            Check("DS_CrystalFrontReflectorThickness", g.FrontReflectorThickness,
                  raw, "DS_CrystalFrontReflectorThickness");
            Check("DS_DetectorMountingThickness", g.MountingThickness,
                  raw, "DS_DetectorMountingThickness");
            Check("pdistance", g.PointDistance, raw, "pdistance");
            Check("SM_BeakerDiameter", g.MarinelliBeakerDiameter, raw, "SM_BeakerDiameter");
            Check("SM_SourceHeight", g.MarinelliSourceHeight, raw, "SM_SourceHeight");

            // Плотность — не длина, и трогать её нельзя.
            double density = Value(raw, "DS_RoCrystal");
            if (density > 0.0)
            {
                Same("плотность кристалла (г/см3)", g.Crystal.Density, density);
            }
        }

        /// <summary>Значение ключа файла как записано, в сантиметрах.</summary>
        static Dictionary<string, double> ReadCm(string path)
        {
            Dictionary<string, double> map =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                string text = comment >= 0 ? line.Substring(0, comment) : line;
                int eq = text.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = text.Substring(0, eq).Trim();
                string value = text.Substring(eq + 1).Trim();
                System.Text.RegularExpressions.Match m =
                    System.Text.RegularExpressions.Regex.Match(
                        value, @"^\s*(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)");
                double number;
                if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                                                 CultureInfo.InvariantCulture, out number))
                {
                    map[key] = number;
                }
            }

            return map;
        }

        static double Value(Dictionary<string, double> map, string key)
        {
            double v;
            return map.TryGetValue(key, out v) ? v : 0.0;
        }

        static void Check(string caption, double mm, Dictionary<string, double> raw, string key)
        {
            Same(caption + " (мм)", mm, Value(raw, key) * 10.0);
        }

        // --------------------------------------------------------------
        // 2. Конфигурация: круг через XML
        // --------------------------------------------------------------

        /// <summary>
        /// Размеры переживают запись и чтение. Проверка дешёвая, но не пустая:
        /// поле геометрии живёт в конфигурации ОДНИМ элементом, и любая правка
        /// его объявления (имя, `XmlIgnore`, подмена свойством) молча уносит
        /// геометрию из всех файлов сразу — и из конфига прибора, и из снимка в
        /// файле спектра.
        /// </summary>
        static void CheckRoundTrip()
        {
            Console.WriteLine();
            Console.WriteLine("=== 2. Запись конфигурации и чтение обратно");
            EfficiencyConfigData config = new EfficiencyConfigData("проба")
            {
                Geometry = new GeometryModel
                {
                    Name = "mm",
                    IsScintillator = true,
                    SourceType = GeometrySourceType.Point,
                    CrystalDiameter = 25.4,
                    CrystalHeight = 25.4,
                    FrontReflectorThickness = 1.3,
                    PointDistance = 100.0,
                },
            };

            config.Geometry.Crystal.Name = "Cesium iodide";
            config.Geometry.Crystal.Density = 4.51;

            EfficiencyConfigData again = Read(Write(config));
            if (again.Geometry == null)
            {
                Fail("геометрия не прочиталась вовсе");
                return;
            }

            Same("диаметр кристалла", again.Geometry.CrystalDiameter, 25.4);
            Same("отражатель с торца", again.Geometry.FrontReflectorThickness, 1.3);
            Same("расстояние до точки", again.Geometry.PointDistance, 100.0);
            // Плотность — не длина, единица у неё своя: г/см3.
            Same("плотность кристалла", again.Geometry.Crystal.Density, 4.51);
        }

        static EfficiencyConfigData Read(string xml)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(EfficiencyConfigData));
            using (StringReader reader = new StringReader(xml))
            {
                return (EfficiencyConfigData)serializer.Deserialize(reader);
            }
        }

        static string Write(EfficiencyConfigData config)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(EfficiencyConfigData));
            using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, config);
                return writer.ToString();
            }
        }

        // --------------------------------------------------------------
        // 3. Кривая не изменилась
        // --------------------------------------------------------------

        static void CheckCurveUnchanged(string path, int histories, double tolerance,
                                        bool legacyCm)
        {
            Console.WriteLine();
            Console.WriteLine("=== 3. Кривая по геометрии против кривой из файла");
            EfficiencyConfigData config = FindConfig(path);
            if (config == null)
            {
                Fail("в " + Path.GetFileName(path) + " нет конфигурации эффективности");
                return;
            }

            if (!config.HasGeometry || !config.HasCurve)
            {
                Fail("в конфигурации «" + config.Name + "» нет "
                     + (config.HasGeometry ? "кривой" : "геометрии") + " — сверять нечего");
                return;
            }

            // Файл, сохранённый до перехода, несёт сантиметры. Домножает ПРОБА,
            // и только по явному ключу: в приложении такого пути нет, старые
            // файлы не поддерживаются. Здесь старый файл нужен ровно за одним —
            // за эталонной кривой, посчитанной до перехода.
            GeometryModel geometry = legacyCm
                ? config.Geometry.Scaled(GeometryModel.MmPerCm)
                : config.Geometry;

            Console.WriteLine("    «{0}»: {1}", config.Name, geometry.Describe());
            EfficiencyFitResult result = EfficiencyCalculation.Run(
                geometry, histories, delegate { }, () => false);
            if (result.Error != null)
            {
                Fail("расчёт не пошёл: " + result.Error);
                return;
            }

            Dictionary<double, double> got = new Dictionary<double, double>();
            foreach (ROIEfficiencyData point in result.Curve)
            {
                got[Math.Round(point.Energy, 3)] = point.Efficiency;
            }

            int compared = 0;
            double worst = 0.0;
            double worstEnergy = 0.0;
            foreach (ROIEfficiencyData point in config.Curve)
            {
                double fresh;
                if (!got.TryGetValue(Math.Round(point.Energy, 3), out fresh)
                    || !(point.Efficiency > 0.0))
                {
                    continue;
                }

                compared++;
                double diff = 100.0 * Math.Abs(fresh - point.Efficiency) / point.Efficiency;
                if (diff > worst)
                {
                    worst = diff;
                    worstEnergy = point.Energy;
                }
            }

            if (compared == 0)
            {
                Fail("ни одна энергия кривой не совпала с сеткой расчёта");
                return;
            }

            Console.WriteLine("    сверено точек: {0}, наибольшее расхождение {1:F2} % на {2:F0} кэВ",
                              compared, worst, worstEnergy);
            if (worst > tolerance)
            {
                Fail(string.Format(CultureInfo.InvariantCulture,
                    "кривая уехала на {0:F2} % — это не шум счёта", worst));
            }
            else
            {
                Console.WriteLine("    ок: кривая та же");
            }
        }

        /// <summary>
        /// Конфигурация эффективности из файла спектра или из конфигурации
        /// прибора — что дали, то и читаем.
        /// </summary>
        static EfficiencyConfigData FindConfig(string path)
        {
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    ResultDataFile file = (ResultDataFile)serializer.Deserialize(stream);
                    foreach (ResultData data in file.ResultDataList)
                    {
                        if (data.Efficiency != null)
                        {
                            return data.Efficiency;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(DeviceConfigInfo));
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    DeviceConfigInfo device = (DeviceConfigInfo)serializer.Deserialize(stream);
                    foreach (EfficiencyConfigData item in device.EfficiencyConfigs)
                    {
                        if (item.HasGeometry && item.HasCurve)
                        {
                            return item;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        // --------------------------------------------------------------

        static void Same(string caption, double got, double want)
        {
            double scale = Math.Max(Math.Abs(want), 1e-12);
            if (Math.Abs(got - want) / scale > 1e-9)
            {
                Fail(string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1:G8}, ожидалось {2:G8}", caption, got, want));
            }
            else
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    ок: {0} = {1:G8}", caption, got));
            }
        }

        static void Fail(string message)
        {
            failed++;
            Console.WriteLine("    РАСХОЖДЕНИЕ: " + message);
        }
    }
}
