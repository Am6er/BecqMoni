// Читатель полосы П14, 10.09.2026: ЧТО КРУГ «ОТКРЫЛ — СОХРАНИЛ» ДЕЛАЕТ С
// ЗАЗОРОМ КРИСТАЛЛА (`A303`).
//
// Полоса П10 намерила «текст .in разошёлся у 14 из 14» и назвала подмену:
// `M_DS_Gap.MName` пусто -> «Water, liquid». Её проба
// (`tools/p10_view/EditorDriftP10Probe.cs`) считает ключи ЦЕЛИКОМ и потому
// отвечает на вопрос «сдвинулось ли», но не на вопрос «осталась ли вода там,
// где её поставили нарочно». Второй вопрос — это ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ
// правки: чинить подмену пустого зазора, сломав перенос заданного, значит
// поменять одну беду на другую, и такой обмен ничем бы не поймался.
//
// Поэтому здесь два замера сразу:
//
//   1) ПОДМЕНА. По каждому файлу каталога — три ключа зазора до и после круга
//      (`DS_nCrystalGapElements`, `DS_RoCrystalGap`, `M_DS_Gap.MName`) и
//      сдвинут ли текст целиком.
//   2) ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Тому же файлу зазор задаётся НАРОЧНО — водой и
//      воздухом, с ненулевой толщиной, — текст пишется, читается обратно и
//      прогоняется через тот же круг. Вещество обязано пережить круг дословно.
//
//   gapcarryp14probe --dir=<каталог с .in>
//
// Коды возврата: 0 — подмены нет И контроль прошёл; 1 — есть подмена;
// 3 — провален положительный контроль; 2 — отказ оснастки.

using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace GapCarryP14Probe
{
    static class Program
    {
        // Ключи зазора в тексте `.in`. Имена — те же, что у писателя
        // (`GeometryWriter.DsGap`); здесь они списком нарочно, чтобы читатель
        // спрашивал ровно то, о чём заведена `A303`.
        static readonly string[] GapKeys =
        {
            "DS_nCrystalGapElements", "DS_RoCrystalGap", "M_DS_Gap.MName"
        };

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var dirs = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) dirs.Add(a.Substring(6));
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (dirs.Count == 0)
            {
                Console.Error.WriteLine("нужен --dir=<каталог с .in>");
                return 2;
            }

            GlobalConfigManager.GetInstance();

            int spoiled = 0, moved = 0, total = 0;
            string sample = null;
            try
            {
                foreach (string dir in dirs)
                {
                    string[] files = Directory.GetFiles(dir, "*.in");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    Console.WriteLine();
                    Console.WriteLine("=== ЗАЗОР ПОСЛЕ КРУГА: {0}, {1} файлов ===",
                                      Path.GetFileName(dir.TrimEnd('\\')), files.Length);
                    foreach (string file in files)
                    {
                        total++;
                        if (sample == null) sample = file;
                        bool textMoved;
                        if (One(file, out textMoved)) spoiled++;
                        if (textMoved) moved++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: " + ex);
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("ИТОГ: зазор подменён у {0} из {1}; текст .in сдвинут у {2} из {1}",
                              spoiled, total, moved);

            if (sample == null)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: ни одного .in — контроль не на чем ставить");
                return 2;
            }

            bool control;
            try
            {
                control = Control(sample);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ на контроле: " + ex);
                return 2;
            }

            if (!control) return 3;
            return spoiled == 0 ? 0 : 1;
        }

        /// <summary>Один файл: три ключа зазора до и после круга.</summary>
        static bool One(string file, out bool textMoved)
        {
            GeometryModel g = GeometryModel.Load(file);
            string before = GeometryWriter.Render(g);
            string after = GeometryWriter.Render(EditorOf(g));
            textMoved = before != after;

            Dictionary<string, string> ka = Keys(before), kb = Keys(after);
            var changed = new List<string>();
            foreach (string key in GapKeys)
            {
                string va = Value(ka, key), vb = Value(kb, key);
                if (va != vb) changed.Add(key + ": «" + va + "» -> «" + vb + "»");
            }

            Console.WriteLine("{0,-40} зазор {1}{2}", Path.GetFileName(file),
                              changed.Count == 0 ? "тот же" : "ПОДМЕНЁН",
                              textMoved ? "; текст сдвинут" : "");
            foreach (string line in changed) Console.WriteLine("      " + line);
            return changed.Count > 0;
        }

        /// <summary>
        /// ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ. Зазор, заданный НАРОЧНО, обязан пережить
        /// круг дословно — и вода в том числе: `A303` не про воду как таковую,
        /// а про то, что её ставят там, где ничего не задано.
        /// </summary>
        static bool Control(string file)
        {
            Console.WriteLine();
            Console.WriteLine("=== ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: заданный зазор обязан уцелеть ===");
            bool ok = true;
            foreach (string name in new[] { "Water, liquid", "Air, dry" })
            {
                ok &= ControlOne(file, name);
            }

            return ok;
        }

        static bool ControlOne(string file, string name)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            if (entry == null)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: в библиотеке нет вещества «" + name + "»");
                throw new InvalidOperationException("нет вещества " + name);
            }

            GeometryModel g = GeometryModel.Load(file);
            g.Gap = GeometryMaterialLibrary.Make(entry, entry.Density);
            // Толщина ненулевая нарочно: беда `A303` заявлена вредом именно у
            // того, кто её набрал, и контроль обязан стоять там же.
            g.FrontGapThickness = 1.0;
            g.SideGapThickness = 1.0;

            // Через файл, а не через объект: круг начинается с ЧТЕНИЯ `.in`, и
            // проверять надо именно его.
            string temp = Path.Combine(Path.GetTempPath(),
                                       "p14_gap_" + Guid.NewGuid().ToString("N") + ".in");
            bool ok;
            try
            {
                File.WriteAllText(temp, GeometryWriter.Render(g));
                GeometryModel back = GeometryModel.Load(temp);
                string before = GeometryWriter.Render(back);
                string after = GeometryWriter.Render(EditorOf(back));
                Dictionary<string, string> ka = Keys(before), kb = Keys(after);

                ok = true;
                var lines = new List<string>();
                foreach (string key in GapKeys)
                {
                    string va = Value(ka, key), vb = Value(kb, key);
                    lines.Add("      " + key + ": «" + va + "» -> «" + vb + "»");
                    if (va != vb) ok = false;
                }

                Console.WriteLine("зазор задан «{0}», толщина 1 мм — {1}",
                                  name, ok ? "уцелел" : "ПОТЕРЯН");
                foreach (string line in lines) Console.WriteLine(line);
            }
            finally
            {
                try { File.Delete(temp); } catch (IOException) { }
            }

            return ok;
        }

        static string Value(Dictionary<string, string> map, string key)
        {
            string v;
            return map.TryGetValue(key, out v) ? v : "<нет ключа>";
        }

        /// <summary>Пары «ключ = значение» текста; повторный ключ — с номером.</summary>
        static Dictionary<string, string> Keys(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            foreach (string line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                string unique = key;
                for (int n = 2; map.ContainsKey(unique); n++)
                {
                    unique = key + "#" + n.ToString(CultureInfo.InvariantCulture);
                }

                map[unique] = value;
            }

            return map;
        }

        /// <summary>
        /// Тот же круг, что у `EditorDriftP10Probe`: модель уходит в панель
        /// редактора и собирается из неё обратно закрытым `BuildModel`.
        /// </summary>
        static GeometryModel EditorOf(GeometryModel g)
        {
            using (var panel = new GeometryEditorPanel())
            {
                panel.CreateControl();
                panel.SetModel(g);
                MethodInfo m = typeof(GeometryEditorPanel).GetMethod(
                    "BuildModel", BindingFlags.Instance | BindingFlags.NonPublic);
                if (m == null)
                {
                    throw new MissingMethodException("GeometryEditorPanel.BuildModel");
                }

                return (GeometryModel)m.Invoke(panel, null);
            }
        }
    }
}
