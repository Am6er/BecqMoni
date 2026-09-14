using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace SpectrumEfficiencyProbe
{
    /// <summary>
    /// Кривая эффективности у СПЕКТРА: ряд «Efficiency» в панели управления
    /// измерением и снимок кривой в файле спектра.
    ///
    /// Проверяются три вещи, и каждая молчит у компилятора:
    ///
    /// 1. РАСКЛАДКА панели. Ряд вставлен между «Device Config» и «ROI Config»,
    ///    а все ряды ниже сдвинуты на 26 точек — и координаты живут не в коде, а
    ///    в `DCControlPanel.resx`. Забытый сдвиг даёт не ошибку сборки, а два
    ///    контрола друг на друге; на глаз это видно, только если открыть панель.
    ///
    /// 2. ЗАПИСЬ В ФАЙЛ СПЕКТРА. `ResultData.Efficiency` — полная копия, а не
    ///    ссылка (`DeviceConfig` и `ROIConfig` рядом помечены `XmlIgnore` и в
    ///    файл не попадают вовсе). Если бы кривая тоже не писалась, файл открылся
    ///    бы у другого человека без единого признака потери — просто «активность
    ///    не считается».
    ///
    /// 3. КОПИЯ `ResultData`. Общий объект означал бы, что два спектра правятся
    ///    за одно.
    ///
    ///     specffprobe &lt;модель.in&gt; &lt;кривая.txt&gt;
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 2)
            {
                Console.WriteLine("specffprobe <модель.in> <кривая.txt>");
                return 2;
            }

            Application.EnableVisualStyles();

            EfficiencyConfigData config = new EfficiencyConfigData("маринелли 0.5")
            {
                Origin = EfficiencyOrigin.Lsrm,
                Geometry = GeometryModel.Load(args[0]),
                Curve = ReadCurve(args[1]),
            };

            int bad = 0;
            bad += CheckLayout();
            bad += CheckFile(config);
            bad += CheckClone(config);

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Ряд стоит там, где заказан, и никого собой не накрыл.
        ///
        /// Панель здесь НЕ создаётся: её конструктор лезет в `MainForm` за
        /// активным документом и без него падает. Раскладка читается из тех же
        /// ресурсов, из которых её берёт `ApplyResources` при запуске, — то
        /// есть проверяется ровно то место, где координаты и живут.
        /// </summary>
        static int CheckLayout()
        {
            ComponentResourceManager resources = new ComponentResourceManager(typeof(DCControlPanel));
            ResourceSet set = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true);

            Dictionary<string, Rectangle> bounds = new Dictionary<string, Rectangle>(StringComparer.Ordinal);
            List<string> own = new List<string>();
            foreach (DictionaryEntry entry in set)
            {
                string key = (string)entry.Key;
                // Метаданные конструктора формы помечены «>>»; родитель «$this»
                // означает контрол самой панели, а не начинки рамки со счётчиками.
                if (!key.StartsWith(">>") || !key.EndsWith(".Parent")
                    || !"$this".Equals(entry.Value as string))
                {
                    continue;
                }

                own.Add(key.Substring(2, key.Length - 2 - ".Parent".Length));
            }

            int bad = 0;
            foreach (string name in own)
            {
                object location = set.GetObject(name + ".Location");
                object size = set.GetObject(name + ".Size");
                if (location is Point && size is Size)
                {
                    bounds[name] = new Rectangle((Point)location, (Size)size);
                }
            }

            foreach (string name in new string[] { "efficiencyLbl", "efficiencyComboBox", "clearEfficiencyBtn" })
            {
                if (!bounds.ContainsKey(name))
                {
                    Console.WriteLine("!! контрола {0} на панели нет", name);
                    bad++;
                }

                if (typeof(DCControlPanel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) == null)
                {
                    Console.WriteLine("!! поля {0} в панели нет: ресурс есть, контрола нет", name);
                    bad++;
                }
            }

            if (bad != 0)
            {
                return bad;
            }

            Console.WriteLine("ряды: прибор y={0}, эффективность y={1}, зоны y={2}, фон y={3}",
                              bounds["deviceConfigLbl"].Top, bounds["efficiencyLbl"].Top,
                              bounds["roiConfigLbl"].Top, bounds["bachgroundLbl"].Top);
            if (!(bounds["deviceConfigLbl"].Top < bounds["efficiencyLbl"].Top
                  && bounds["efficiencyLbl"].Top < bounds["roiConfigLbl"].Top))
            {
                Console.WriteLine("!! ряд «Efficiency» стоит не между прибором и зонами");
                bad++;
            }

            if (Math.Abs(bounds["efficiencyComboBox"].Top - bounds["efficiencyLbl"].Top) > 8
                || Math.Abs(bounds["clearEfficiencyBtn"].Top - bounds["efficiencyLbl"].Top) > 8)
            {
                Console.WriteLine("!! контролы ряда разъехались по высоте");
                bad++;
            }

            // Ни один контрол не накрывает соседа. Сдвиг рядов делался руками по
            // `resx`, и пропущенный контрол проявляется именно так.
            List<string> names = new List<string>(bounds.Keys);
            names.Sort(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                for (int j = i + 1; j < names.Count; j++)
                {
                    if (bounds[names[i]].IntersectsWith(bounds[names[j]]))
                    {
                        Console.WriteLine("!! {0} {1} накрывает {2} {3}",
                                          names[i], bounds[names[i]], names[j], bounds[names[j]]);
                        bad++;
                    }
                }
            }

            Console.WriteLine("контролов панели сверено на перекрытие: {0}", names.Count);
            return bad;
        }

        /// <summary>Файл спектра: пишется и читается ли снимок кривой.</summary>
        static int CheckFile(EfficiencyConfigData config)
        {
            ResultData data = new ResultData();
            data.Efficiency = config.Copy();

            ResultDataFile file = new ResultDataFile();
            file.InitFormatVersion();
            file.ResultDataList.Add(data);

            XmlSerializer serializer = new XmlSerializer(typeof(ResultDataFile));
            string xml;
            using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, file);
                xml = writer.ToString();
            }

            ResultDataFile back;
            using (StringReader reader = new StringReader(xml))
            {
                back = (ResultDataFile)serializer.Deserialize(reader);
            }

            Console.WriteLine("файл спектра: {0} знаков", xml.Length);

            EfficiencyConfigData r = back.ResultDataList[0].Efficiency;
            if (r == null)
            {
                Console.WriteLine("!! файл спектра: кривой после чтения нет вовсе");
                return 1;
            }

            int bad = 0;
            bad += Same("файл: Guid", config.Guid, r.Guid);
            bad += Same("файл: Name", config.Name, r.Name);
            bad += Same("файл: Origin", config.Origin, r.Origin);
            bad += Same("файл: точек кривой", config.Curve.Count, r.Curve.Count);
            bad += Same("файл: геометрия есть", true, r.Geometry != null);
            if (r.Geometry != null)
            {
                bad += Same("файл: тип источника", config.Geometry.SourceType, r.Geometry.SourceType);
                bad += Same("файл: состав кристалла", config.Geometry.Crystal.Fractions.Count,
                            r.Geometry.Crystal.Fractions.Count);
            }

            return bad;
        }

        /// <summary>Копия результата: своя кривая, а не общая.</summary>
        static int CheckClone(EfficiencyConfigData config)
        {
            ResultData data = new ResultData();
            data.Efficiency = config.Copy();
            // Пустой спектр по умолчанию не переживает собственный Clone —
            // заводим настоящий, проба не про него.
            data.EnergySpectrum = new EnergySpectrum(1.0, 1024)
            {
                EnergyCalibration = new PolynomialEnergyCalibration(),
            };
            ResultData copy = data.Clone();

            int bad = 0;
            if (copy.Efficiency == null)
            {
                Console.WriteLine("!! копия результата: кривая потеряна");
                return 1;
            }

            bad += Same("копия: Guid", data.Efficiency.Guid, copy.Efficiency.Guid);
            bad += Same("копия: точек кривой", data.Efficiency.Curve.Count, copy.Efficiency.Curve.Count);
            if (ReferenceEquals(data.Efficiency, copy.Efficiency))
            {
                Console.WriteLine("!! копия: кривая та же самая, два спектра правятся за одно");
                bad++;
            }

            return bad;
        }

        static int Same(string what, object expected, object got)
        {
            if (Equals(expected, got))
            {
                return 0;
            }

            Console.WriteLine("!! {0}: {1} -> {2}", what, expected, got);
            return 1;
        }

        static List<ROIEfficiencyData> ReadCurve(string path)
        {
            List<ROIEfficiencyData> curve = new List<ROIEfficiencyData>();
            bool first = true;
            foreach (string line in File.ReadAllLines(path))
            {
                if (first)
                {
                    first = false;
                    continue;
                }

                List<string> parts = new List<string>();
                foreach (string p in line.Split('\t'))
                {
                    if (p.Trim().Length > 0)
                    {
                        parts.Add(p.Trim());
                    }
                }

                double e, eff, err;
                if (parts.Count >= 3
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out e)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out eff)
                    && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out err))
                {
                    curve.Add(new ROIEfficiencyData { Energy = e, Efficiency = eff, ErrorPercent = err });
                }
            }

            return curve;
        }
    }
}
