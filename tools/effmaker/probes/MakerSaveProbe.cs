using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace MakerSaveProbe
{
    /// <summary>
    /// Кнопка «Сохранить» конструктора кривой после того, как сохранять стало
    /// некуда, кроме конфигурации прибора.
    ///
    /// Сторожится ровно то, на чём здесь уже попались: обработчик выходил, если
    /// КРИВОЙ ещё нет, — а кнопка включается и на одну правку геометрии
    /// (UpdateSaveState). Нажатие не делало ничего: кнопка доступна, звёздочка
    /// в заголовке не гаснет, сообщения нет. Глазами это неотличимо от
    /// «сохранилось».
    ///
    ///     makersaveprobe
    ///
    /// Проверяется:
    ///
    /// 1. ГЕОМЕТРИЯ БЕЗ КРИВОЙ доезжает до конфигурации, и признак правки
    ///    гаснет;
    /// 2. КРИВАЯ КОНФИГУРАЦИИ становится исходной при привязке — по ней
    ///    подгонка берёт абсолютный уровень. Поле пустовало всё время, пока
    ///    кривую выбирали ROI-файлом, и об этом честно говорилось в коде;
    ///    теперь оно обязано заполняться;
    /// 3. САМА ПРИВЯЗКА не объявляет конфигурацию изменённой: окно, открытое и
    ///    закрытое без единой правки, не должно предлагать запись.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo culture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentCulture = culture;

            int bad = 0;
            DeviceConfigInfo device = new DeviceConfigInfo();
            device.Guid = Guid.NewGuid().ToString();
            device.Name = "проба";

            EfficiencyConfigData config = new EfficiencyConfigData("проба-геометрия")
            {
                Origin = EfficiencyOrigin.Measurement,
                Geometry = Preset("Atom Spectra Nano 16"),
            };
            config.Curve.Add(new ROIEfficiencyData { Energy = 100.0, Efficiency = 1e-2, ErrorPercent = 5.0 });
            config.Curve.Add(new ROIEfficiencyData { Energy = 1000.0, Efficiency = 1e-3, ErrorPercent = 7.0 });
            device.EfficiencyConfigs.Add(config);

            DateTime before = config.LastUpdated;
            using (EfficiencyMakerForm form = new EfficiencyMakerForm())
            {
                form.BindTo(device, config);

                bad += Same("исходная кривая взята из конфигурации", 2, Reference(form));
                bad += Same("привязка не объявляет правку", false, Dirty(form));

                // Правка геометрии руками: в поля редактора кладётся ДРУГОЙ
                // детектор, и форме сообщается, что её правили, — ровно то
                // состояние, в котором пользователь жмёт «Сохранить».
                GeometryModel other = Preset("Obsidian");
                Panel(form).SetModel(other);
                Set(form, "dirty", true);

                Call(form, "saveButton_Click", null, EventArgs.Empty);

                bad += Near("геометрия доехала: X кристалла", other.CrystalBoxX,
                            config.Geometry == null ? -1.0 : config.Geometry.CrystalBoxX);
                bad += Near("геометрия доехала: Z кристалла", other.CrystalBoxZ,
                            config.Geometry == null ? -1.0 : config.Geometry.CrystalBoxZ);
                bad += Same("признак правки погас", false, Dirty(form));
                bad += Same("отметка времени обновилась", true, config.LastUpdated > before);
                // Кривой не было — её и не должно появиться, и старую стирать
                // тоже нечем: сохранялась ГЕОМЕТРИЯ.
                bad += Same("кривая не тронута", 2, config.Curve.Count);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>
        /// Пресет ПО ИМЕНИ, а не по номеру в списке (T24). Номер молча меняет
        /// смысл: когда пресет RadiaCode разрезали на 101 и 103 (E14), индекс 2
        /// перестал быть «Obsidian» и стал «RadiaCode-103» — проба продолжала
        /// проходить только потому, что ей нужен просто ДРУГОЙ детектор.
        /// Отсутствующее имя валит пробу сразу, а не подсовывает соседа.
        /// </summary>
        static GeometryModel Preset(string name)
        {
            GeometryPresets.Preset preset =
                GeometryPresets.Items.FirstOrDefault(p => p.Name == name);
            if (preset == null)
            {
                throw new InvalidOperationException(
                    "нет пресета «" + name + "»; есть: "
                    + string.Join(", ", GeometryPresets.Items.Select(p => p.Name)));
            }

            GeometryModel model = new GeometryModel();
            preset.Apply(model);
            model.Name = preset.Name;
            return model;
        }

        static GeometryEditorPanel Panel(EfficiencyMakerForm form)
        {
            return (GeometryEditorPanel)Field(form, "geometryPanel");
        }

        static bool Dirty(EfficiencyMakerForm form)
        {
            return (bool)Field(form, "dirty");
        }

        static int Reference(EfficiencyMakerForm form)
        {
            System.Collections.Generic.List<ROIEfficiencyData> curve =
                (System.Collections.Generic.List<ROIEfficiencyData>)Field(form, "referenceCurve");
            return curve == null ? 0 : curve.Count;
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

        static void Set(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null)
            {
                throw new InvalidOperationException("нет поля " + name);
            }

            field.SetValue(target, value);
        }

        static void Call(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null)
            {
                throw new InvalidOperationException("нет метода " + name);
            }

            method.Invoke(target, args);
        }

        static int Same(string what, object expected, object got)
        {
            bool ok = Equals(expected, got);
            Console.WriteLine("  {0,-44} {1} {2}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(" вместо {0}", expected));
            return ok ? 0 : 1;
        }

        static int Near(string what, double expected, double got)
        {
            bool ok = Math.Abs(got - expected) <= 1e-9;
            Console.WriteLine("  {0,-44} {1} {2:G6}{3}", what, ok ? "=" : "!!", got,
                              ok ? "" : string.Format(" вместо {0:G6}", expected));
            return ok ? 0 : 1;
        }
    }
}
