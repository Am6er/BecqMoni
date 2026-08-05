using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SetColorProbe
{
    /// <summary>
    /// Присвоение цвета всем нуклидам набора — ряд «Цвет пиков набора» в
    /// редакторе наборов.
    ///
    /// Форма собирается БЕЗ MainForm (такой конструктор есть, его же зовёт
    /// дизайнер), набор выбирается отражением: щёлкать по таблице XPTable
    /// пробе нечем, а проверять надо не щелчок, а что происходит с цветами.
    /// Кнопка нажимается через свой обработчик — тот самый, что подписан в
    /// дизайнере: вызывать напрямую логику, минуя обработчик, значило бы
    /// проверять код, до которого кнопка может и не доходить.
    ///
    /// Заодно снимок формы в PNG: ряд втиснут между таблицей и кнопками, и
    /// налезание на них — единственное, чего здесь можно не заметить числами.
    ///
    ///   setcolorprobe [куда.png]
    ///
    /// Конфиг читается из ТЕКУЩЕГО каталога (`config\NuclideDefinition.xml`),
    /// как и у SetProbe: запускать из копии, чужой конфиг пробой не трогать.
    /// </summary>
    static class Program
    {
        static int failed;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Application.EnableVisualStyles();

            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();
            NuclideSet set = EnsureSet(manager);
            List<NuclideDefinition> members = Members(manager, set);
            if (members.Count < 2)
            {
                Console.WriteLine("РАСХОЖДЕНИЕ: в наборе «{0}» меньше двух нуклидов — красить нечего",
                                  set.Name);
                return 1;
            }

            Console.WriteLine("набор «{0}»: нуклидов {1}", set.Name, members.Count);

            // Разные цвета до присвоения: одинаковые не отличили бы «покрасило»
            // от «и так было».
            members[0].NuclideColor.Color = Color.Green;
            members[1].NuclideColor.Color = Color.Red;

            // Нуклид ВНЕ набора — сторож: красить его не должны.
            NuclideDefinition outsider = Outsider(manager, set);
            Color outsiderBefore = outsider == null ? Color.Empty : outsider.NuclideColor.Color;

            NuclideSetForm form = new NuclideSetForm();
            Select(form, set);

            Field(form, "assignColorComboBox");
            SetColor(form, Color.Magenta);
            Click(form, "buttonAssignColor_Click");

            foreach (NuclideDefinition nuclide in members)
            {
                if (nuclide.NuclideColor.Color.ToArgb() != Color.Magenta.ToArgb())
                {
                    Fail(string.Format("{0} {1} кэВ остался {2}", nuclide.Name, nuclide.Energy,
                                       nuclide.NuclideColor.Color.Name));
                }
            }

            if (failed == 0)
            {
                Console.WriteLine("    ок: все {0} нуклида набора покрашены в Magenta", members.Count);
            }

            if (outsider != null)
            {
                if (outsider.NuclideColor.Color.ToArgb() != outsiderBefore.ToArgb())
                {
                    Fail(string.Format("нуклид вне набора ({0} {1} кэВ) тоже покрашен",
                                       outsider.Name, outsider.Energy));
                }
                else
                {
                    Console.WriteLine("    ок: нуклид вне набора не тронут");
                }
            }

            // Признак правки: без него «Сохранить» останется недоступной, и
            // работа пропадёт по закрытии окна.
            if (!(bool)Field(form, "dirty"))
            {
                Fail("форма не объявила себя изменённой — «Сохранить» останется серой");
            }
            else
            {
                Console.WriteLine("    ок: форма помечена изменённой");
            }

            // Поле цвета показывает цвет набора, когда он один на всех.
            Select(form, set);
            Color shown = ((global::ColorComboBox.ColorComboBox)Field(form, "assignColorComboBox")).SelectedColor;
            if (shown.ToArgb() != Color.Magenta.ToArgb())
            {
                Fail("поле цвета показывает " + shown.Name + ", а у набора один цвет Magenta");
            }
            else
            {
                Console.WriteLine("    ок: поле цвета показывает цвет набора");
            }

            CheckSwatchRepaint(form);

            if (args.Length > 0)
            {
                Shot(form, args[0]);
            }

            // Признак правки снимается ПЕРЕД закрытием: на закрытии форма
            // спрашивает «сохранить?» модальным окном, и проба повисла бы без
            // человека у экрана — а сохранять чужой конфиг она не должна вовсе.
            typeof(NuclideSetForm).GetField("dirty", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(form, false);
            form.Dispose();
            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + failed);
            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// Поле цвета обязано ПОКАЗАТЬ присвоенный цвет, а не только хранить
        /// его. Свойство `SelectedColor` возвращало правильное значение и
        /// тогда, когда квадрат на экране оставался чёрным: цвет рисуется в
        /// обработчике Paint, а перерисовку никто не просил. Чинилось само от
        /// наведения мыши — то есть выглядело как «UI не отрабатывает».
        ///
        /// Проверяется СОБЫТИЕ ОТРИСОВКИ, а не пиксель. Пиксель здесь ничего не
        /// доказывает: `DrawToBitmap` шлёт контролу WM_PRINT и заставляет его
        /// нарисоваться заново независимо от того, просили перерисовку или нет.
        /// Проба, снимавшая цвет так, оставалась зелёной и с УБРАННЫМ
        /// `Invalidate` — то есть не проверяла ничего.
        ///
        /// Поэтому: считаем Paint у самой кнопки. Присвоение цвета обязано его
        /// поднять. Сторож рядом — холостой прогон очереди сообщений без
        /// присвоения: он обязан НЕ поднять ничего, иначе счётчик ловил бы не
        /// перерисовку по запросу, а фоновую (у кнопки крутится свой таймер).
        /// </summary>
        static void CheckSwatchRepaint(NuclideSetForm form)
        {
            global::ColorComboBox.ColorComboBox combo =
                (global::ColorComboBox.ColorComboBox)Field(form, "assignColorComboBox");
            if (combo.Controls.Count == 0)
            {
                Fail("внутри поля цвета нет кнопки — проверять отрисовку не на чем");
                return;
            }

            Control button = combo.Controls[0];
            int paints = 0;
            PaintEventHandler counter = delegate { paints++; };
            button.Paint += counter;

            // Форму надо показать: у скрытого контрола нет дескриптора окна и
            // сообщения отрисовки ему не приходят вовсе.
            form.Show();
            Pump();

            foreach (Color want in new[] { Color.Red, Color.Blue, Color.Yellow })
            {
                paints = 0;
                combo.SelectedColor = want;
                Pump();
                int afterSet = paints;

                paints = 0;
                Pump();
                int idle = paints;

                if (afterSet == 0)
                {
                    Fail(string.Format(
                        "присвоение {0} не вызвало отрисовку — квадрат останется прежним до наведения мыши",
                        want.Name));
                }
                else if (idle != 0)
                {
                    Fail(string.Format(
                        "кнопка перерисовывается и без присвоения ({0} раз на холостом ходу) — счётчик ничего не доказывает",
                        idle));
                }
                else
                {
                    Console.WriteLine("    ок: присвоение {0} перерисовало квадрат ({1}), холостой ход тихий",
                                      want.Name, afterSet);
                }
            }

            button.Paint -= counter;
            form.Hide();
        }

        /// <summary>
        /// Прокрутить очередь сообщений до конца. Один `DoEvents` не
        /// обязательно доносит WM_PAINT: он приходит, когда очередь пуста.
        /// </summary>
        static void Pump()
        {
            for (int i = 0; i < 5; i++)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        /// <summary>Набор с наибольшим числом нуклидов — на нём и видно разницу.</summary>
        static NuclideSet EnsureSet(NuclideDefinitionManager manager)
        {
            NuclideSet best = null;
            int most = -1;
            foreach (NuclideSet set in manager.NuclideSets)
            {
                int count = Members(manager, set).Count;
                if (count > most)
                {
                    most = count;
                    best = set;
                }
            }

            if (best == null)
            {
                best = new NuclideSet { Id = Guid.NewGuid(), Name = "проба" };
                manager.NuclideSets.Add(best);
            }

            return best;
        }

        static List<NuclideDefinition> Members(NuclideDefinitionManager manager, NuclideSet set)
        {
            List<NuclideDefinition> list = new List<NuclideDefinition>();
            foreach (NuclideDefinition nuclide in manager.NuclideDefinitions)
            {
                if (nuclide.Sets.Contains(set.Id))
                {
                    list.Add(nuclide);
                }
            }

            return list;
        }

        static NuclideDefinition Outsider(NuclideDefinitionManager manager, NuclideSet set)
        {
            foreach (NuclideDefinition nuclide in manager.NuclideDefinitions)
            {
                if (!nuclide.Sets.Contains(set.Id))
                {
                    return nuclide;
                }
            }

            return null;
        }

        // --------------------------------------------------------------

        static void Select(NuclideSetForm form, NuclideSet set)
        {
            FieldInfo field = typeof(NuclideSetForm).GetField(
                "selectedSet", BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(form, set);
            typeof(NuclideSetForm).GetMethod("ShowSetColor",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        }

        static object Field(NuclideSetForm form, string name)
        {
            FieldInfo field = typeof(NuclideSetForm).GetField(
                name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException("нет поля " + name);
            }

            return field.GetValue(form);
        }

        static void SetColor(NuclideSetForm form, Color color)
        {
            ((global::ColorComboBox.ColorComboBox)Field(form, "assignColorComboBox")).SelectedColor = color;
        }

        static void Click(NuclideSetForm form, string handler)
        {
            typeof(NuclideSetForm).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { null, EventArgs.Empty });
        }

        static void Shot(NuclideSetForm form, string path)
        {
            form.Show();
            Application.DoEvents();
            using (Bitmap bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(path, ImageFormat.Png);
            }

            Console.WriteLine("снимок: {0}", path);
            form.Hide();
        }

        static void Fail(string message)
        {
            failed++;
            Console.WriteLine("    РАСХОЖДЕНИЕ: " + message);
        }
    }
}
