// Находка полосы П10 по дороге, 10.09.2026: КРУГ ЧЕРЕЗ РЕДАКТОР СНОВА ДВИГАЕТ
// ТЕКСТ `.in`.
//
// `RawCarryProbe --editor=` (чужая проба, не правилась) на нынешнем дереве даёт
// «текст .in разошёлся у 14 из 14» там, где журнал `A139` 04.09.2026 записал
// «0 из 66». Отпечаток матрицы при этом НЕ сдвинут — то есть беда `A139`
// («матрица объявлена устаревшей») не вернулась, а вернулось что-то другое.
//
// ⛔ ЗАЧЕМ ЭТА ПРОБА, а не просто строка реестра. Мерка `RawCarryProbe`
// позиционная: `Drift` сравнивает строки ПО НОМЕРУ, и одна вставленная строка
// объявляет разошедшимися все последующие. Значит «107–114 строк» — верхняя
// оценка, а не размер беды, и заводить по ней строку значило бы отдать
// следующей полосе непроверенное число. Здесь считается ТО ЖЕ самое, но по
// множеству «ключ = значение»: сколько ключей пропало, сколько появилось,
// сколько сменило значение, и сколько строк всего.
//
//   editordriftp10probe --dir=<каталог с .in> [--show=<сколько ключей печатать>]
//
// Коды возврата: 0 — круг ничего не двигает; 1 — двигает; 2 — отказ оснастки.

using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace EditorDriftP10Probe
{
    static class Program
    {
        static int show = 8;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            var dirs = new List<string>();
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) dirs.Add(a.Substring(6));
                else if (a.StartsWith("--show=", StringComparison.Ordinal))
                    show = int.Parse(a.Substring(7), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (dirs.Count == 0)
            {
                Console.Error.WriteLine("нужен --dir=<каталог с .in>");
                return 2;
            }

            GlobalConfigManager.GetInstance();

            int moved = 0, total = 0;
            try
            {
                foreach (string dir in dirs)
                {
                    string[] files = Directory.GetFiles(dir, "*.in");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    Console.WriteLine();
                    Console.WriteLine("=== КРУГ ЧЕРЕЗ РЕДАКТОР: {0}, {1} файлов ===",
                                      Path.GetFileName(dir.TrimEnd('\\')), files.Length);
                    foreach (string file in files)
                    {
                        total++;
                        if (One(file)) moved++;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОТКАЗ ОСНАСТКИ: " + ex);
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("ИТОГ: текст .in сдвинут у {0} из {1}", moved, total);
            return moved == 0 ? 0 : 1;
        }

        static bool One(string file)
        {
            GeometryModel g = GeometryModel.Load(file);
            string before = GeometryWriter.Render(g);
            string after = GeometryWriter.Render(EditorOf(g));

            string[] a = Lines(before), b = Lines(after);
            Dictionary<string, string> ka = Keys(a), kb = Keys(b);

            var gone = new List<string>();
            var born = new List<string>();
            var changed = new List<string>();
            foreach (KeyValuePair<string, string> p in ka)
            {
                string v;
                if (!kb.TryGetValue(p.Key, out v)) gone.Add(p.Key);
                else if (v != p.Value) changed.Add(p.Key + ": «" + p.Value + "» -> «" + v + "»");
            }

            foreach (string key in kb.Keys)
            {
                if (!ka.ContainsKey(key)) born.Add(key);
            }

            bool moved = before != after;
            Console.WriteLine("{0,-40} строк {1} -> {2}; ключей {3} -> {4}; "
                              + "пропало {5}, появилось {6}, сменило значение {7} — {8}",
                              Path.GetFileName(file), a.Length, b.Length, ka.Count, kb.Count,
                              gone.Count, born.Count, changed.Count,
                              moved ? "СДВИНУТ" : "тот же");

            Print("  пропали:", gone);
            Print("  появились:", born);
            Print("  сменили значение:", changed);
            return moved;
        }

        static void Print(string what, List<string> items)
        {
            if (items.Count == 0) return;
            Console.WriteLine(what);
            for (int i = 0; i < items.Count && i < show; i++)
            {
                Console.WriteLine("      " + items[i]);
            }

            if (items.Count > show)
            {
                Console.WriteLine("      … и ещё " + (items.Count - show));
            }
        }

        static string[] Lines(string text)
        {
            return text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        }

        /// <summary>
        /// Пары «ключ = значение» текста. Повторный ключ пишется с номером —
        /// иначе два одинаковых ключа схлопнулись бы в один и пропажа второго
        /// стала бы невидимой.
        /// </summary>
        static Dictionary<string, string> Keys(string[] lines)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
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
