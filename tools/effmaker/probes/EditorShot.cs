using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace EditorShot
{
    /// <summary>
    /// Снимок ВКЛАДКИ ИСТОЧНИКА редактора геометрии в PNG — с уже наложенной
    /// готовой сценой (E27).
    ///
    /// Зачем. `SketchShot` снимает чертёж, а не разметку, а править пришлось
    /// именно разметку: строка готовой сцены сдвинула поля источника на 64
    /// точки вниз, и вещества пробы у маринелли оказались близко к нижнему
    /// краю. Обрезанный родителем контрол не пропадает и не падает — он просто
    /// не виден, и заметить это можно только глазами. Форма берётся РОВНО в
    /// свой MinimumSize: если помещается здесь, поместится везде.
    ///
    ///   editorshot &lt;куда.png&gt; [--scene=1|2] [--energy=3000]
    ///     --scene=1 — «Детектор на земле», =2 — «Детектор в лунке», 0 — без сцены
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("editorshot <куда.png> [--scene=1|2] [--energy=3000]");
                return 1;
            }

            string outPath = args[0];
            int scene = 2;
            double energy = 3000.0;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a.StartsWith("--scene=", StringComparison.Ordinal))
                    scene = int.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--energy=", StringComparison.Ordinal))
                    energy = double.Parse(a.Substring(9), CultureInfo.InvariantCulture);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            Application.EnableVisualStyles();
            GlobalConfigManager.GetInstance();

            using (Form form = new Form())
            using (GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill })
            {
                // Тот же размер, что MinimumSize конструктора кривой, минус
                // место его собственных панелей — тесный случай, ради которого
                // проба и заведена.
                form.ClientSize = new Size(880, 470);
                form.Controls.Add(panel);
                form.Show();

                GeometryModel g = GeometryEditorPanel.Blank();
                GeometryPresets.Items[0].Apply(g);       // Nano 16 — любой, лишь бы обвязка была
                if (scene == 1)
                {
                    GeometryScenes.Ground(g, energy);
                }
                else if (scene == 2)
                {
                    GeometryScenes.Borehole(g, energy);
                }

                panel.SetSceneEnergy(energy);
                // Сцена въезжает МОДЕЛЬЮ, а не выбором в списке: так проверяется
                // и обратный ход — что панель узнаёт вид съёмки в сохранённой
                // геометрии и встаёт на нужную строку списка сама.
                panel.SetModel(g);

                TabControl tabs = FindTabs(panel);
                if (tabs == null)
                {
                    Console.Error.WriteLine("в панели нет вкладок — разметка изменилась");
                    return 1;
                }

                tabs.SelectedIndex = 1;                  // вкладка источника
                ComboBox types = FindSourceTypes(tabs.TabPages[1]);
                if (types == null)
                {
                    Console.Error.WriteLine("на вкладке источника нет списка типов");
                    return 1;
                }

                int want = scene == 1 ? 4 : scene == 2 ? 5 : 0;
                if (types.SelectedIndex != want)
                {
                    Console.Error.WriteLine("список типов встал на строку {0}, а ожидалась {1}",
                                            types.SelectedIndex, want);
                    return 1;
                }

                Application.DoEvents();
                using (Bitmap bmp = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
                {
                    panel.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.ClientSize));
                    bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                }

                form.Hide();
            }

            Console.WriteLine("записано: {0}", outPath);
            return 0;
        }

        static TabControl FindTabs(Control root)
        {
            foreach (Control c in root.Controls)
            {
                TabControl tabs = c as TabControl;
                if (tabs != null)
                {
                    return tabs;
                }

                tabs = FindTabs(c);
                if (tabs != null)
                {
                    return tabs;
                }
            }

            return null;
        }

        /// <summary>
        /// Список типов источника узнаётся по числу строк: их шесть — четыре
        /// формы и две съёмки в поле (E27). Списки веществ на той же вкладке
        /// длиной в библиотеку, спутать нельзя. Привязка к порядку контролов
        /// сломалась бы от любой правки разметки — ровно того, что проба и
        /// проверяет.
        /// </summary>
        const int SourceKindCount = 6;

        static ComboBox FindSourceTypes(Control root)
        {
            foreach (Control c in root.Controls)
            {
                ComboBox combo = c as ComboBox;
                if (combo != null && combo.Items.Count == SourceKindCount)
                {
                    return combo;
                }

                ComboBox found = FindSourceTypes(c);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
