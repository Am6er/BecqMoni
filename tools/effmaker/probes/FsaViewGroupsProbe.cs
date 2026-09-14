using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FsaViewGroupsProbe
{
    /// <summary>
    /// (`A265`) КАЖДЫЙ ПЕРЕКЛЮЧАТЕЛЬ ОКНА ОТЧЁТА FSA — В СВОЕЙ ГРУППЕ.
    ///
    ///   fsaviewgroupsprobe [--misplace=&lt;имя поля&gt;]
    ///
    /// До 06.09.2026 отрисовочная галка «Невязка модели» стояла шестой в ряду
    /// пяти РАСЧЁТНЫХ, внутри группы «Дополнительные компоненты модели», и
    /// отличить её от соседок можно было только подсказкой при наведении.
    /// Цена смешения уже уплачена одним заходом: вопрос ~~`A264`~~ («вычитает
    /// ли отключение сумм-пиков их из `counts`») родился именно из него.
    /// Решением Amber отрисовочные переключатели вынесены в отдельную группу
    /// «Отрисовка», и граница между родами обязана быть видна БЕЗ НАВЕДЕНИЯ.
    ///
    /// ⛔ РОД ПЕРЕКЛЮЧАТЕЛЯ СУДИТСЯ ПО ФАКТУ, А НЕ ПО СПИСКУ ИМЁН. Список имён
    /// пришлось бы держать в согласии с окном руками, и он разошёлся бы молча —
    /// ровно та беда, из-за которой заведена эта проба. Здесь род измеряется:
    /// переключатель ПЕРЕКЛЮЧАЕТСЯ, и смотрится, изменился ли ОТПЕЧАТОК
    /// настроек разбора (<see cref="FsaCalculationOptions"/>.<c>Stamp</c>) —
    /// та самая величина, по которой окно решает, нужен ли пересчёт. Изменился
    /// — расчётный, не изменился — отрисовочный. Ни имена, ни подсказки в
    /// приговор не входят.
    ///
    /// ⛔ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ ОБЯЗАТЕЛЕН, иначе «всё на местах» проходит и
    /// на пустом списке. Ключ <c>--misplace=&lt;имя поля&gt;</c> перекладывает
    /// названный переключатель в ЧУЖУЮ группу прямо в построенном окне, и
    /// проверка обязана ОТКАЗАТЬ, назвав его поимённо. Без ключа — код 0.
    ///
    /// Третий раздел — ШИРИНА ПОДПИСЕЙ (`A127`: у панели уже был случай, когда
    /// запас был РОВНО НОЛЬ). Подписи групп и метки берутся из собранных
    /// ресурсов обеих культур и меряются тем же <see cref="TextRenderer"/>,
    /// которым их рисует WinForms; у замера своё плечо контроля — заведомо
    /// длинная подпись обязана НЕ влезть.
    ///
    /// ⚠ Окна проба не показывает: всё меряется на построенном, но не
    /// показанном виде. <c>Control.Visible</c> при этом лжёт, и приговор на
    /// него не опирается — смотрится только дерево <c>Parent</c>.
    ///
    /// Коды возврата: 0 — сошлось; 1 — не сошлось; 2 — мерить нечем.
    /// </summary>
    static class Program
    {
        /// <summary>Имя группы, в которой лежит всё отрисовочное.</summary>
        const string DisplayGroup = "displayGroupBox";

        static int bad;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Application.SetCompatibleTextRenderingDefault(false);

            string misplace = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--misplace=", StringComparison.Ordinal))
                {
                    misplace = a.Substring(11);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            MainForm mainForm = new MainForm();
            FSAReportView panel = new FSAReportView(mainForm);
            FsaAnalysisSession session = new FsaAnalysisSession();
            ResultData rd = Spectrum();
            panel.SetProbeSource(session, rd);
            FWHMPeakDetectionMethodConfig cfg = (FWHMPeakDetectionMethodConfig)rd.PeakDetectionMethodConfig;

            if (misplace != null && !Misplace(panel, misplace))
            {
                panel.Dispose();
                mainForm.Dispose();
                return 2;
            }

            List<Switch> switches = Census(panel, cfg);
            Membership(switches);
            Order(panel, switches);
            Captions(panel);

            Console.WriteLine();
            if (bad == 0)
            {
                Console.WriteLine("ВСЕ СОШЛИСЬ: каждый переключатель в группе своего рода, подписи влезают");
            }
            else
            {
                Console.WriteLine("НЕ СОШЛОСЬ: {0}", bad);
            }

            // ⛔ Формы убираются явно: без этого процесс печатает приговор и
            // падает уже ПОСЛЕ, на разборе окон (грабля `FsaFlagsProbe`).
            panel.Dispose();
            mainForm.Dispose();
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        // 1. Перепись: что за переключатель, в какой он группе, какого рода
        // ------------------------------------------------------------------

        sealed class Switch
        {
            public Control Control;
            public string Name;
            public string Group;
            public bool ChangesCalculation;
            public bool Toggled;
        }

        static List<Switch> Census(FSAReportView panel, FWHMPeakDetectionMethodConfig cfg)
        {
            Console.WriteLine("=== перепись переключателей окна отчёта FSA (A265) ===");
            Console.WriteLine("  {0,-24} {1,-18} {2,-13} {3}", "поле", "группа", "род", "отпечаток");

            List<Switch> found = new List<Switch>();
            foreach (Control control in Walk(panel))
            {
                if (!(control is CheckBox) && !(control is RadioButton))
                {
                    continue;
                }

                Switch item = new Switch
                {
                    Control = control,
                    Name = control.Name,
                    Group = GroupOf(control)
                };
                string pair = Toggle(control, cfg, item);
                Console.WriteLine("  {0,-24} {1,-18} {2,-13} {3}", item.Name, item.Group,
                                  item.ChangesCalculation ? "РАСЧЁТНЫЙ" : "отрисовочный", pair);
                if (!item.Toggled)
                {
                    Console.WriteLine("  ⛔ {0}: переключатель не сработал — род не измерен", item.Name);
                    bad++;
                }

                found.Add(item);
            }

            int calc = 0;
            foreach (Switch item in found)
            {
                if (item.ChangesCalculation)
                {
                    calc++;
                }
            }

            Console.WriteLine("  всего {0}: расчётных {1}, отрисовочных {2}",
                              found.Count, calc, found.Count - calc);
            if (found.Count == 0)
            {
                Console.WriteLine("  ⛔ переключателей не найдено ВОВСЕ — проба смотрит не туда");
                bad++;
            }

            return found;
        }

        /// <summary>
        /// Переключить так, чтобы это ЗНАЧИЛО «человек выбрал», и снять
        /// отпечаток В ОБОИХ положениях. У флажка это смена значения; у
        /// радиокнопки — выбор ЕЁ против выбора соседки: обработчики
        /// радиокнопок молча выходят при <c>!Checked</c>, и снятие выбора не
        /// измерило бы ничего.
        ///
        /// ⚠ Признак «сработало» — НЕ конечное <c>Checked</c>, а то, что
        /// событие <c>CheckedChanged</c> ДЕЙСТВИТЕЛЬНО пришло. Родительская
        /// радиокнопка законно возвращается назад (родители недоступны, пока
        /// в результате нет связанного ряда), и приговор по конечному
        /// значению объявил бы её неизмеримой, хотя обработчик отработал.
        ///
        /// Состояние восстанавливается: перепись не должна оставлять окно
        /// иным, чем застала.
        /// </summary>
        static string Toggle(Control control, FWHMPeakDetectionMethodConfig cfg, Switch item)
        {
            int fired = 0;
            EventHandler counter = delegate { fired++; };
            CheckBox box = control as CheckBox;
            RadioButton radio = control as RadioButton;
            string a, b;

            if (box != null)
            {
                box.CheckedChanged += counter;
                bool was = box.Checked;
                a = Stamp(cfg);
                box.Checked = !was;
                b = Stamp(cfg);
                box.Checked = was;
                box.CheckedChanged -= counter;
            }
            else
            {
                RadioButton other = null;
                foreach (Control sibling in radio.Parent.Controls)
                {
                    RadioButton candidate = sibling as RadioButton;
                    if (candidate != null && !ReferenceEquals(candidate, radio))
                    {
                        other = candidate;
                        break;
                    }
                }

                if (other == null)
                {
                    item.Toggled = false;
                    item.ChangesCalculation = false;
                    return Stamp(cfg) + " (соседней радиокнопки нет)";
                }

                RadioButton wasChecked = null;
                foreach (Control sibling in radio.Parent.Controls)
                {
                    RadioButton candidate = sibling as RadioButton;
                    if (candidate != null && candidate.Checked)
                    {
                        wasChecked = candidate;
                        break;
                    }
                }

                radio.CheckedChanged += counter;
                other.Checked = true;
                a = Stamp(cfg);
                radio.Checked = true;
                b = Stamp(cfg);
                radio.CheckedChanged -= counter;
                if (wasChecked != null)
                {
                    wasChecked.Checked = true;
                }
            }

            item.Toggled = fired > 0;
            item.ChangesCalculation = a != b;
            return a == b ? a : a + " -> " + b;
        }

        static string Stamp(FWHMPeakDetectionMethodConfig cfg)
        {
            return FsaCalculationOptions.FromConfig(cfg).Stamp;
        }

        static IEnumerable<Control> Walk(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control deeper in Walk(child))
                {
                    yield return deeper;
                }
            }
        }

        /// <summary>Ближайшая рамка-группа вверх по дереву; «(вне групп)» — её нет.</summary>
        static string GroupOf(Control control)
        {
            for (Control up = control.Parent; up != null; up = up.Parent)
            {
                if (up is GroupBox)
                {
                    return up.Name;
                }
            }

            return "(вне групп)";
        }

        // ------------------------------------------------------------------
        // 2. Приговор: расчётное — вне группы показа, отрисовочное — в ней
        // ------------------------------------------------------------------

        static void Membership(List<Switch> switches)
        {
            Console.WriteLine();
            Console.WriteLine("=== принадлежность: род переключателя против его группы ===");
            int wrong = 0;
            foreach (Switch item in switches)
            {
                bool inDisplay = item.Group == DisplayGroup;
                bool ok = item.ChangesCalculation ? !inDisplay : inDisplay;
                if (!ok)
                {
                    wrong++;
                    bad++;
                    Console.WriteLine(item.ChangesCalculation
                        ? "  ⛔ {0}: РАСЧЁТНЫЙ, а лежит в группе показа «{1}»"
                        : "  ⛔ {0}: отрисовочный, а лежит в расчётной группе «{1}»",
                        item.Name, item.Group);
                }
            }

            Console.WriteLine(wrong == 0
                ? "  все на своих местах"
                : string.Format(CultureInfo.InvariantCulture, "  не на своём месте: {0}", wrong));
        }

        /// <summary>
        /// ⛔ Положительный контроль: названный переключатель переносится в
        /// ЧУЖУЮ группу того же окна. Ищется он ОТРАЖЕНИЕМ по имени поля, а
        /// целевой поток — по дереву, чтобы контроль не пришлось править
        /// вслед за каждым переименованием.
        /// </summary>
        static bool Misplace(FSAReportView panel, string field)
        {
            FieldInfo info = typeof(FSAReportView).GetField(
                field, BindingFlags.Instance | BindingFlags.NonPublic);
            if (info == null)
            {
                Console.Error.WriteLine("поля «" + field + "» в окне нет");
                return false;
            }

            Control control = info.GetValue(panel) as Control;
            if (control == null || control.Parent == null)
            {
                Console.Error.WriteLine("поле «" + field + "» не контрол либо не размещено");
                return false;
            }

            bool inDisplay = GroupOf(control) == DisplayGroup;
            Control target = null;
            foreach (Control candidate in Walk(panel))
            {
                if (!(candidate is FlowLayoutPanel) || ReferenceEquals(candidate, control.Parent))
                {
                    continue;
                }

                if ((GroupOf(candidate) == DisplayGroup) != inDisplay)
                {
                    target = candidate;
                    break;
                }
            }

            if (target == null)
            {
                Console.Error.WriteLine("чужой группы для «" + field + "» не нашлось");
                return false;
            }

            Console.WriteLine("⚠ ПОЛОЖИТЕЛЬНЫЙ КОНТРОЛЬ: «{0}» переложен из «{1}» в «{2}» — проверка обязана отказать",
                              field, control.Parent.Name, target.Name);
            Console.WriteLine();
            control.Parent.Controls.Remove(control);
            target.Controls.Add(control);
            return true;
        }

        // ------------------------------------------------------------------
        // 3. Порядок групп: отрисовочная — НИЖЕ расчётных (решение Amber)
        // ------------------------------------------------------------------

        /// <summary>
        /// Решение Amber 06.09.2026 названо буквально: «отдельная группа
        /// «Отрисовка» НИЖЕ». Здесь у этого слова есть читатель — иначе
        /// группу однажды поставят первой, и ничто не возразит.
        ///
        /// ⚠ Раскладка спрашивается у САМОГО вида после <c>PerformLayout</c>,
        /// а не у чисел в ресурсах: у групп поднят <c>AutoSize</c> и
        /// <c>Dock = Top</c>, то есть числа конструктора — снимок, а не закон.
        /// </summary>
        static void Order(FSAReportView panel, List<Switch> switches)
        {
            Console.WriteLine();
            Console.WriteLine("=== порядок групп сверху вниз (решение Amber: показ НИЖЕ расчёта) ===");
            panel.PerformLayout();

            Dictionary<string, GroupBox> boxes = new Dictionary<string, GroupBox>();
            foreach (Control control in Walk(panel))
            {
                GroupBox box = control as GroupBox;
                if (box != null)
                {
                    boxes[box.Name] = box;
                }
            }

            if (!boxes.ContainsKey(DisplayGroup))
            {
                Console.WriteLine("  ⛔ группы «{0}» на виде нет", DisplayGroup);
                bad++;
                return;
            }

            List<GroupBox> sorted = new List<GroupBox>(boxes.Values);
            sorted.Sort(delegate(GroupBox x, GroupBox y) { return x.Top.CompareTo(y.Top); });
            foreach (GroupBox box in sorted)
            {
                Console.WriteLine("  {0,-18} верх {1,4}, низ {2,4}, высота {3,3}, ширина {4}",
                                  box.Name, box.Top, box.Bottom, box.Height, box.Width);
            }

            // Расчётной считается группа, в которой лежит хоть один расчётный
            // переключатель, — снова по факту, а не по имени.
            int lowestCalc = int.MinValue;
            string lowestName = null;
            foreach (Switch item in switches)
            {
                GroupBox box;
                if (!item.ChangesCalculation || item.Group == null || !boxes.TryGetValue(item.Group, out box))
                {
                    continue;
                }

                if (box.Bottom > lowestCalc)
                {
                    lowestCalc = box.Bottom;
                    lowestName = box.Name;
                }
            }

            GroupBox display = boxes[DisplayGroup];
            if (lowestName == null)
            {
                Console.WriteLine("  ⛔ расчётных групп не нашлось — сравнивать не с чем");
                bad++;
            }
            else if (display.Top < lowestCalc)
            {
                Console.WriteLine("  ⛔ «{0}» (верх {1}) НЕ ниже расчётной «{2}» (низ {3})",
                                  DisplayGroup, display.Top, lowestName, lowestCalc);
                bad++;
            }
            else
            {
                Console.WriteLine("  «{0}» ниже всех расчётных: верх {1} против низа {2} у «{3}»",
                                  DisplayGroup, display.Top, lowestCalc, lowestName);
            }
        }

        // ------------------------------------------------------------------
        // 4. Подписи: влезают ли в отведённое место (`A127`)
        // ------------------------------------------------------------------

        static void Captions(FSAReportView panel)
        {
            Console.WriteLine();
            Console.WriteLine("=== ширина подписей групп и метки (A127) ===");
            ComponentResourceManager view = new ComponentResourceManager(typeof(FSAReportView));
            Font font = view.GetObject("$this.Font") as Font;
            if (font == null)
            {
                Console.WriteLine("  ⛔ нет $this.Font в ресурсах — мерить нечем");
                bad++;
                return;
            }

            Size client = panel.ClientSize;
            Console.WriteLine("  шрифт {0} {1}pt, ширина окна {2}", font.Name,
                              font.SizeInPoints.ToString("F1", CultureInfo.InvariantCulture), client.Width);

            // Рамка группы съедает у подписи левый отступ и правую кромку.
            // Число взято с запасом в БОЛЬШУЮ сторону: замер обязан ругаться
            // раньше, чем подпись обрежется на самом деле.
            const int Frame = 16;
            string[] names = { "sourceGroupBox", "chainGroupBox", "extrasGroupBox", DisplayGroup, "groupingLabel" };
            foreach (string culture in new[] { "en", "ru" })
            {
                Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                Console.WriteLine("  --- культура {0} ---", culture);
                foreach (string name in names)
                {
                    string text = view.GetString(name + ".Text");
                    if (text == null)
                    {
                        Console.WriteLine("  ⛔ {0}: подписи в ресурсах НЕТ", name);
                        bad++;
                        continue;
                    }

                    int need = TextRenderer.MeasureText(text, font).Width;
                    int have = client.Width - Frame;
                    Console.WriteLine("  {0,-18} «{1}» — нужно {2}, есть {3}, запас {4}",
                                      name, text, need, have, have - need);
                    if (need > have)
                    {
                        Console.WriteLine("  ⛔ {0}: подпись НЕ ВЛЕЗАЕТ и обрежется", name);
                        bad++;
                    }
                }
            }

            // ⛔ Плечо контроля: заведомо длинная подпись обязана не влезть,
            // иначе замер выше проходит всегда и не мерит ничего.
            string longText = new string('W', 200);
            if (TextRenderer.MeasureText(longText, font).Width <= client.Width - Frame)
            {
                Console.WriteLine("  ⛔ КОНТРОЛЬ ПРОВАЛЕН: 200 знаков «влезли» — замер ширины не мерит");
                bad++;
            }
            else
            {
                Console.WriteLine("  контроль: 200 знаков не влезают — замер ширины работает");
            }

            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        }

        /// <summary>
        /// Спектр-заглушка: без активного спектра элементы окна погашены, а
        /// считать по нему ничего не нужно — окно не показано.
        /// </summary>
        static ResultData Spectrum()
        {
            ResultData rd = new ResultData();
            rd.EnergySpectrum = new EnergySpectrum(1.0, 64);
            rd.PeakDetectionMethodConfig = new FWHMPeakDetectionMethodConfig();
            return rd;
        }
    }
}
