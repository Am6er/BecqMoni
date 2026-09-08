using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor;

/// <summary>
/// Не уполз ли контрол редактора геометрии в невидимую зону.
///
/// ## Откуда взялась
///
/// Разметка `GeometryEditorPanel` собирается кодом, и высоты панелей стояли
/// числами. Строка веществ — 48 точек; строк было три (144 из записанных 150),
/// `AMBER1` 07.09.2026 добавила четвёртую, «наполнитель зазора», — стало 192.
/// Четвёртая строка, «Cladding material», обрезалась краем панели: снимок Amber
/// 08.09.2026. До того тем же порядком уезжали вещества пробы (`E27`).
///
/// ⛔ Это НЕ ЛОВИТСЯ ничем из имеющегося. Панель детей обрезает МОЛЧА: ни
/// исключения, ни следа в журнале, ни пропажи из дерева UI Automation —
/// контрол на месте, просто его не видно. Прокрутка вокруг (`FieldColumn`,
/// `AutoScroll`) прокручивает СЕБЯ, а переполнение вложенной панели ей не
/// видно вовсе. Разбор кода тоже слеп: подписи AutoSize, и в русском
/// (`*.ru.resx`) они длиннее английских и переносятся на вторую строку —
/// высота, сошедшаяся на английском, на русском не сходится.
///
/// ## Что меряется
///
/// Панель строится ВЖИВУЮ и показывается за краем экрана. Дальше обход дерева:
/// у каждого контейнера БЕЗ прокрутки каждый ПОКАЗАННЫЙ ребёнок обязан
/// умещаться в клиентскую область. Вышел за неё — назван поимённо.
///
/// ⚠ Форма ПОКАЗЫВАЕТСЯ нарочно, и окно за краем экрана — тоже нарочно.
/// `Control.Visible` у WinForms — видимость ФАКТИЧЕСКАЯ: у непоказанной формы
/// она false у всех детей разом, и проба сочла бы скрытым весь редактор
/// (та же грабля, что у `RowControls.Shown` в самой панели).
///
/// Перебираются все состояния разметки, в которых панели разной высоты:
/// две формы кристалла (у бруска на строку и подпись больше) × шесть строк
/// списка форм источника (от точки в одно поле до маринелли в десять), и обе
/// культуры — английская и русская.
///
/// Сборка — общая, `build_all.ps1`. Запуск из каталога проб:
///   GeometryLayoutProbe.exe
///
/// Коды возврата:
///   0 — всё уместилось; напечатано, сколько состояний проверено;
///   1 — есть уползшие контролы (перечислены с числами);
///   2 — редактор не построился (отказ назван).
/// </summary>
static class GeometryLayoutProbe
{
    /// <summary>Одна находка: кто, где и насколько вышел за край.</summary>
    sealed class Overflow
    {
        public string Scene;
        public string Container;
        public string Child;
        public int Have;        // клиентская высота (или ширина) контейнера
        public int Need;        // низ (или правый край) ребёнка
        public string Axis;
    }

    [STAThread]
    static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Application.EnableVisualStyles();

        List<Overflow> bad = new List<Overflow>();
        int scenes = 0;

        foreach (string culture in new[] { "en", "ru" })
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(culture);
            try
            {
                scenes += Measure(culture, bad);
            }
            catch (Exception e)
            {
                Console.WriteLine("⛔ редактор не построился ({0}): {1}", culture, e.Message);
                return 2;
            }
        }

        Console.WriteLine();
        if (bad.Count == 0)
        {
            Console.WriteLine("СОШЛОСЬ: состояний разметки проверено {0}, "
                              + "уползших контролов нет", scenes);
            return 0;
        }

        Console.WriteLine("⛔ УПОЛЗЛО В НЕВИДИМУЮ ЗОНУ: находок {0} на {1} состояниях",
                          bad.Count, scenes);
        foreach (Overflow o in bad)
        {
            Console.WriteLine("  [{0}] {1}: «{2}» {3} {4} при {5} у панели — за краем на {6}",
                              o.Scene, o.Container, o.Child, o.Axis, o.Need, o.Have,
                              o.Need - o.Have);
        }

        return 1;
    }

    /// <summary>Перебрать состояния разметки одной культуры; вернуть их число.</summary>
    static int Measure(string culture, List<Overflow> bad)
    {
        int scenes = 0;
        using (Form form = new Form())
        {
            // За краем экрана и мимо панели задач: показать надо (иначе
            // `Visible` врёт), а мозолить глаза нечем.
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-4000, -4000);
            form.ShowInTaskbar = false;
            form.ClientSize = new Size(950, 720);

            GeometryEditorPanel panel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            form.Controls.Add(panel);
            form.Show();
            Application.DoEvents();

            TabControl tabs = FirstTabControl(panel);
            RadioButton box = (RadioButton)Field(panel, "boxRadio");
            RadioButton cyl = (RadioButton)Field(panel, "cylinderRadio");
            ComboBox sourceType = (ComboBox)Field(panel, "sourceTypeCombo");
            Dictionary<Control, string> names = FieldNames(panel);

            for (int shape = 0; shape < 2; shape++)
            {
                if (shape == 0) { cyl.Checked = true; } else { box.Checked = true; }
                for (int src = 0; src < sourceType.Items.Count; src++)
                {
                    sourceType.SelectedIndex = src;
                    Application.DoEvents();

                    // Вкладка выбирается ПЕРЕД замером: у невыбранной страницы
                    // дети не показаны, и мерить там нечего.
                    for (int page = 0; page < tabs.TabPages.Count; page++)
                    {
                        tabs.SelectedIndex = page;
                        Application.DoEvents();
                        string scene = string.Format(CultureInfo.InvariantCulture,
                            "{0}/{1}/источник {2}/{3}", culture,
                            shape == 0 ? "цилиндр" : "брусок", src,
                            tabs.TabPages[page].Text);
                        Walk(tabs.TabPages[page], scene, names, bad);
                        scenes++;
                    }
                }
            }

            form.Close();
        }

        Console.WriteLine("{0}: состояний {1}, находок к этому месту {2}",
                          culture, scenes, bad.Count);
        return scenes;
    }

    /// <summary>
    /// Обойти дерево и записать всех, кто вышел за клиентскую область своего
    /// родителя.
    ///
    /// ⚠ Контейнер С ПРОКРУТКОЙ пропускается по существу дела, а не для
    /// снисхождения: до его содержимого можно доехать полосой, и выход за край
    /// там — не пропажа. Обрезание без прокрутки — пропажа.
    /// </summary>
    static void Walk(Control parent, string scene, Dictionary<Control, string> names,
                     List<Overflow> bad)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible)
            {
                continue;
            }

            if (!(parent is ScrollableControl) || !((ScrollableControl)parent).AutoScroll)
            {
                Size room = parent.ClientSize;
                if (child.Bottom > room.Height)
                {
                    bad.Add(new Overflow
                    {
                        Scene = scene,
                        Container = Name(parent, names),
                        Child = Describe(child),
                        Axis = "низ",
                        Need = child.Bottom,
                        Have = room.Height,
                    });
                }

                if (child.Right > room.Width)
                {
                    bad.Add(new Overflow
                    {
                        Scene = scene,
                        Container = Name(parent, names),
                        Child = Describe(child),
                        Axis = "правый край",
                        Need = child.Right,
                        Have = room.Width,
                    });
                }
            }

            Walk(child, scene, names, bad);
        }
    }

    static string Describe(Control c)
    {
        string text = c.Text;
        if (string.IsNullOrEmpty(text) && c is ComboBox && ((ComboBox)c).SelectedItem != null)
        {
            text = ((ComboBox)c).SelectedItem.ToString();
        }

        return string.IsNullOrEmpty(text)
            ? c.GetType().Name
            : c.GetType().Name + " " + text;
    }

    /// <summary>Имя панели — то, каким она названа полем в самом редакторе.</summary>
    static string Name(Control c, Dictionary<Control, string> names)
    {
        string name;
        if (names.TryGetValue(c, out name))
        {
            return name;
        }

        return string.IsNullOrEmpty(c.Name) ? c.GetType().Name : c.Name;
    }

    /// <summary>
    /// Контролы редактора, разложенные по именам его же полей: панели там
    /// безымянные, а «Panel» в отказе не говорит ничего.
    /// </summary>
    static Dictionary<Control, string> FieldNames(object target)
    {
        Dictionary<Control, string> names = new Dictionary<Control, string>();
        for (Type t = target.GetType(); t != null && t != typeof(UserControl); t = t.BaseType)
        {
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance
                                                | BindingFlags.NonPublic | BindingFlags.Public))
            {
                Control c = f.GetValue(target) as Control;
                if (c != null && !names.ContainsKey(c))
                {
                    names[c] = f.Name;
                }
            }
        }

        return names;
    }

    static TabControl FirstTabControl(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            TabControl tabs = c as TabControl;
            if (tabs != null)
            {
                return tabs;
            }

            tabs = FirstTabControl(c);
            if (tabs != null)
            {
                return tabs;
            }
        }

        throw new InvalidOperationException("в редакторе нет вкладок");
    }

    static object Field(object target, string name)
    {
        for (Type t = target.GetType(); t != null; t = t.BaseType)
        {
            FieldInfo f = t.GetField(name, BindingFlags.Instance
                                     | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null)
            {
                return f.GetValue(target);
            }
        }

        throw new InvalidOperationException("нет поля " + name);
    }
}
