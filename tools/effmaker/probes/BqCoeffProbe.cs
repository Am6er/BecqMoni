using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
using BecquerelMonitor.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace BqCoeffProbe
{
    /// <summary>
    /// Коэффициент перевода в беккерели, посчитанный по кривой эффективности:
    /// Bq = cps · K, K = 100/(ε·I), dK = K·δ/100.
    ///
    /// Проверяется то, чего не видит компилятор:
    ///
    /// 1. САМА ФОРМУЛА — против независимой её записи, которая уже стоит в
    ///    `ROIConfigForm.cs` (кнопка «добавить зону из нуклида» заполняет K
    ///    ровно так же). Два места, где одно и то же считается по-разному, —
    ///    это два разных ответа на вопрос «сколько беккерелей».
    /// 2. ОТКАТ на сохранённое значение и его причина. Кривой нет, энергия за
    ///    краем кривой, у зоны не задан выход — три разные беды, и каждая
    ///    обязана называться своим именем: молча подставленное старое число
    ///    выглядит одинаково во всех трёх случаях.
    /// 3. ХРАНЕНИЕ признака. Галочка «считать по эффективности» живёт у зоны и
    ///    обязана пережить запись в файл и копию: поле, забытое в конструкторе
    ///    копирования, — ошибка, на которой сегодня уже попались кривые в
    ///    конфиге прибора.
    ///
    /// 4. ГДЕ ПРИЧИНА ВИДНА. Отдельной надписи под полем больше нет — текст
    ///    переехал в подсказку самой галочки, — и подсказка проверяется тем же
    ///    впрыском: пустая подсказка от отсутствующей глазами неотличима.
    ///
    ///     bqprobe
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    ///
    /// Измерение «насколько изменится активность, если включить расчёт по
    /// кривой» отсюда убрано: оно читало кривую из ROI-конфига, а кривая из
    /// наборов зон переехала в конфигурацию прибора и в этих файлах её больше
    /// нет.
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            int bad = 0;
            bad += CheckFormula();
            bad += CheckFallbacks();
            bad += CheckPersistence();
            bad += CheckHint();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Кривая из трёх точек с известными числами.</summary>
        static EfficiencyConfigData Curve()
        {
            EfficiencyConfigData config = new EfficiencyConfigData("проба");
            config.Curve = new List<ROIEfficiencyData>
            {
                new ROIEfficiencyData { Energy = 100.0, Efficiency = 1.0e-2, ErrorPercent = 2.0 },
                new ROIEfficiencyData { Energy = 662.0, Efficiency = 1.0e-3, ErrorPercent = 5.0 },
                new ROIEfficiencyData { Energy = 2000.0, Efficiency = 1.0e-4, ErrorPercent = 8.0 },
            };

            return config;
        }

        static ROIDefinitionData Zone(double energy, double intensity, double storedK, double storedError)
        {
            return new ROIDefinitionData
            {
                Name = "зона",
                PeakEnergy = energy,
                Intencity = intensity,
                BecquerelCoefficient = storedK,
                BecquerelCoefficientError = storedError,
                AutoBecquerelCoefficient = true,
            };
        }

        static int CheckFormula()
        {
            Console.WriteLine("=== формула ===");
            int bad = 0;
            EfficiencyConfigData curve = Curve();

            // Точно в узле: 662 кэВ, ε = 1e-3, δ = 5 %, выход 85.1 %
            ROIDefinitionData zone = Zone(662.0, 85.1, 1.0, 0.0);
            BecquerelCoefficient.Result k = BecquerelCoefficient.Resolve(zone, curve);
            double expected = 100.0 / (1.0e-3 * 85.1);
            bad += Near("K в узле кривой", expected, k.Value, 1e-9);
            bad += Near("dK в узле кривой", expected * 5.0 / 100.0, k.Error, 1e-9);
            bad += Same("источник", BecquerelCoefficient.Source.Efficiency, k.From);

            // Та же запись, что в ROIConfigForm: (1/ε)/(I/100)
            double asInForm = (1.0 / 1.0e-3) / (85.1 / 100.0);
            bad += Near("совпадает с записью в форме ROI", asInForm, k.Value, 1e-9);

            // Между узлами: лог-лог по эффективности, линейно по погрешности
            ROIDefinitionData mid = Zone(Math.Sqrt(100.0 * 662.0), 10.0, 1.0, 0.0);
            BecquerelCoefficient.Result km = BecquerelCoefficient.Resolve(mid, curve);
            double epsMid = Math.Sqrt(1.0e-2 * 1.0e-3);
            bad += Near("K между узлами", 100.0 / (epsMid * 10.0), km.Value, 1e-6);

            Console.WriteLine("  K(662, I=85.1) = {0:G8}, dK = {1:G8}", k.Value, k.Error);
            return bad;
        }

        static int CheckFallbacks()
        {
            Console.WriteLine("=== откаты ===");
            int bad = 0;
            EfficiencyConfigData curve = Curve();

            // Кривой нет вовсе
            BecquerelCoefficient.Result none = BecquerelCoefficient.Resolve(Zone(662.0, 85.1, 777.0, 7.0), null);
            bad += Near("нет кривой: K сохранённый", 777.0, none.Value, 1e-9);
            bad += Same("нет кривой: источник", BecquerelCoefficient.Source.Stored, none.From);
            bad += NotEmpty("нет кривой: причина названа", none.Problem);

            // За краем кривой сверху и снизу
            foreach (double energy in new double[] { 40.0, 3000.0 })
            {
                BecquerelCoefficient.Result far =
                    BecquerelCoefficient.Resolve(Zone(energy, 85.1, 777.0, 7.0), curve);
                bad += Near("вне кривой " + energy + ": K сохранённый", 777.0, far.Value, 1e-9);
                bad += Same("вне кривой " + energy + ": источник", BecquerelCoefficient.Source.Stored, far.From);
                bad += NotEmpty("вне кривой " + energy + ": причина названа", far.Problem);
            }

            // Выход не задан
            BecquerelCoefficient.Result noYield =
                BecquerelCoefficient.Resolve(Zone(662.0, 0.0, 777.0, 7.0), curve);
            bad += Near("нет выхода: K сохранённый", 777.0, noYield.Value, 1e-9);
            bad += NotEmpty("нет выхода: причина названа", noYield.Problem);

            // Галочка снята — кривую не спрашиваем вовсе
            ROIDefinitionData manual = Zone(662.0, 85.1, 777.0, 7.0);
            manual.AutoBecquerelCoefficient = false;
            BecquerelCoefficient.Result off = BecquerelCoefficient.Resolve(manual, curve);
            bad += Near("галочка снята: K сохранённый", 777.0, off.Value, 1e-9);
            bad += Same("галочка снята: причины нет", null, off.Problem);

            // Все три причины обязаны различаться: одинаковый текст означал бы,
            // что пользователю не сказали, что именно чинить.
            HashSet<string> reasons = new HashSet<string>(StringComparer.Ordinal);
            reasons.Add(none.Problem);
            reasons.Add(BecquerelCoefficient.Resolve(Zone(3000.0, 85.1, 1.0, 0.0), curve).Problem);
            reasons.Add(noYield.Problem);
            bad += Same("причины различны", 3, reasons.Count);

            // Пустая кривая — это «значения нет», а не единица
            EfficiencyConfigData empty = new EfficiencyConfigData("пустая");
            bad += Same("пустая кривая не строится", null, FsaEfficiency.FromConfig(empty));

            return bad;
        }

        /// <summary>
        /// Где причина отката показывается пользователю. Отдельной надписи под
        /// полем больше нет — текст переехал в подсказку самой галочки, — и
        /// проверяется именно это: подсказка непустая и несёт ту самую строку.
        ///
        /// Проверять глазами дорого: подсказку видно только под мышью, а пустая
        /// подсказка от отсутствующей ничем не отличается.
        /// </summary>
        static int CheckHint()
        {
            Console.WriteLine("=== подсказка на галочке ===");
            int bad = 0;
            // Список примитивов зоны заводит MainForm при запуске, а формы он
            // нужен уже конструктору. Без этого проба падает на первой строке
            // конструктора, не дойдя до проверки.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            using (ROIConfigForm form = new ROIConfigForm())
            {
                CheckBox check = (CheckBox)Field(form, "autoBqCheckBox");
                ToolTip hints = (ToolTip)Field(form, "hints");

                bad += Same("надписи под полем больше нет", null,
                            form.GetType().GetField("bqCoeffStatusLabel",
                                BindingFlags.Instance | BindingFlags.NonPublic));

                // Выход не задан: причина известна и обязана дойти до мыши.
                ROIDefinitionData noYield = Zone(662.0, 0.0, 777.0, 7.0);
                Show(form, noYield);
                bad += Same("нет выхода: подсказка та самая",
                            Resources.BqCoeffNoIntensity, hints.GetToolTip(check));

                // Галочка снята — причины нет, и подсказки быть не должно:
                // висящий текст «K посчитан по кривой» на снятой галочке врал бы.
                ROIDefinitionData manual = Zone(662.0, 85.1, 777.0, 7.0);
                manual.AutoBecquerelCoefficient = false;
                Show(form, manual);
                bad += Same("галочка снята: подсказки нет", "", hints.GetToolTip(check));
            }

            return bad;
        }

        static void Show(ROIConfigForm form, ROIDefinitionData roi)
        {
            MethodInfo method = typeof(ROIConfigForm).GetMethod("ShowBecquerelCoefficient",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InvalidOperationException("нет ROIConfigForm.ShowBecquerelCoefficient");
            }

            method.Invoke(form, new object[] { roi });
        }

        static object Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                throw new InvalidOperationException("нет поля " + name);
            }

            return field.GetValue(target);
        }

        static int CheckPersistence()
        {
            Console.WriteLine("=== хранение признака ===");
            int bad = 0;

            ROIConfigData config = new ROIConfigData();
            config.InitFormatVersion();
            config.Guid = System.Guid.NewGuid().ToString();
            config.Name = "проба";
            ROIDefinitionData on = Zone(662.0, 85.1, 5.0, 0.5);
            ROIDefinitionData off = Zone(1460.8, 10.66, 7.0, 0.7);
            off.AutoBecquerelCoefficient = false;
            config.ROIDefinitions.Add(on);
            config.ROIDefinitions.Add(off);

            XmlSerializer serializer = new XmlSerializer(typeof(ROIConfigData));
            string xml;
            using (StringWriter writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, config);
                xml = writer.ToString();
            }

            ROIConfigData back;
            using (StringReader reader = new StringReader(xml))
            {
                back = (ROIConfigData)serializer.Deserialize(reader);
            }

            bad += Same("файл: включённая осталась включённой", true, back.ROIDefinitions[0].AutoBecquerelCoefficient);
            bad += Same("файл: снятая осталась снятой", false, back.ROIDefinitions[1].AutoBecquerelCoefficient);

            // Старый файл, где признака нет вовсе: зона должна включиться, а
            // сохранённое число — остаться запасным, чтобы числа не поехали.
            string old = xml.Replace("<AutoBecquerelCoefficient>true</AutoBecquerelCoefficient>", "")
                            .Replace("<AutoBecquerelCoefficient>false</AutoBecquerelCoefficient>", "");
            ROIConfigData legacy;
            using (StringReader reader = new StringReader(old))
            {
                legacy = (ROIConfigData)serializer.Deserialize(reader);
            }

            bad += Same("старый файл: признак по умолчанию включён", true,
                        legacy.ROIDefinitions[0].AutoBecquerelCoefficient);
            bad += Near("старый файл: сохранённый K на месте", 5.0,
                        legacy.ROIDefinitions[0].BecquerelCoefficient, 1e-9);

            ROIDefinitionData copy = off.Clone();
            bad += Same("копия зоны несёт признак", false, copy.AutoBecquerelCoefficient);

            return bad;
        }

        static int Near(string what, double expected, double got, double tolerance)
        {
            if (Math.Abs(got - expected) <= tolerance * Math.Max(1.0, Math.Abs(expected)))
            {
                return 0;
            }

            Console.WriteLine("!! {0}: ждали {1:G8}, получили {2:G8}", what, expected, got);
            return 1;
        }

        static int NotEmpty(string what, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return 0;
            }

            Console.WriteLine("!! {0}: причина не названа", what);
            return 1;
        }

        static int Same(string what, object expected, object got)
        {
            if (Equals(expected, got))
            {
                return 0;
            }

            Console.WriteLine("!! {0}: ждали {1}, получили {2}",
                              what, expected ?? "null", got ?? "null");
            return 1;
        }
    }
}
