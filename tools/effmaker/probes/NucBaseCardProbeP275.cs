using BecquerelMonitor;
using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.NucBase;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using XPTable.Models;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П275, 14.10.2026) Мелкие находки итоговой проверки экраном П273 — отражением, без
    /// показа окон, в ОБЕИХ культурах интерфейса (`ru-RU`, `en-US`; культура чисел — та же,
    /// что интерфейса, как у Amber):
    ///
    ///   1. «Isotope Database», таблица поиска: энергия и выход печатаются с ТОЧКОЙ
    ///      («727.33», не «727,33»); колонка периода — единицей на языке интерфейса.
    ///      Положительный контроль: та же ячейка в таблице БЕЗ поставщика формата на
    ///      `ru-RU` обязана дать запятую — иначе проба запятой не увидела бы вовсе.
    ///   2. Строка состава FSA: подпись обратного рассеяния переведена, служебное имя —
    ///      прежнее.
    ///   3. Карточка NucBase: подписи и значения не налезают друг на друга и не выходят за
    ///      рамку на худших строках базы; период подписан «T½», единица — по-русски;
    ///      пункты единиц у поля отбора совпадают с кодами `NucBase.FilterUnits`.
    ///   4. Редактор нуклидов: закрытие правки ячейки без ввода НЕ взводит «есть
    ///      несохранённое»; вписанное число взводит (положительный контроль); поле периода
    ///      вмещает «5.808599695585997E-06» целиком.
    ///   5. Общие настройки: поле предела счёта вмещает 2147483647 и не налезает на соседей.
    ///
    /// Конфиг — только из текущего каталога (копия поставки рядом с пробой); ничего не
    /// пишется. Коды: 0 — сошлось; 1 — нет.
    /// </summary>
    static class NucBaseCardProbeP275
    {
        static int bad;
        const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            foreach (string lang in new[] { "ru-RU", "en-US" })
            {
                CultureInfo culture = new CultureInfo(lang);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                Console.WriteLine();
                Console.WriteLine("======== {0} ========", lang);
                SearchGrid(lang);
                Backscatter(lang);
                Card(lang);
                Editor(lang);
                CountLimit(lang);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        static void Check(string what, bool ok, string detail)
        {
            if (!ok) bad++;
            Console.WriteLine("  {0} {1}: {2}", ok ? "ок" : "!!", what, detail);
        }

        // ---------------------------------------------------------------- 1
        static void SearchGrid(string lang)
        {
            Console.WriteLine("--- 1. таблица поиска «Isotope Database»");
            using (var form = new NucBase.NucBase())
            {
                var grid = (DataGridView)typeof(NucBase.NucBase).GetField("ResultDataGridView", Any).GetValue(form);
                MethodInfo addRow = typeof(NucBase.NucBase).GetMethod("AddRow", Any);
                addRow.Invoke(form, new object[] { Line("212BI", "G", 727.33, 6.67, 60.55, "m") });
                addRow.Invoke(form, new object[] { Line("232NP", "X", 111.238, 14, 14.7, "m") });
                addRow.Invoke(form, new object[] { Line("212BI", "X", 12.148, 6.99877963, 60.55, "m") });
                string[] want = { "727.33", "6.67", "111.238", "14", "12.148", "6.99877963" };
                string[] got =
                {
                    Shown(grid, 0, 3), Shown(grid, 0, 4), Shown(grid, 1, 3), Shown(grid, 1, 4),
                    Shown(grid, 2, 3), Shown(grid, 2, 4)
                };
                Check("энергия и выход — с точкой", string.Join("|", want) == string.Join("|", got), string.Join(" | ", got));
                Check("значение ячейки — число double (сортировка и импорт)",
                      grid.Rows[0].Cells[3].Value is double && (double)grid.Rows[0].Cells[3].Value == 727.33,
                      grid.Rows[0].Cells[3].Value.GetType().Name);

                string hlCell = (string)grid.Rows[0].Cells[7].Value;
                string hlShown = Shown(grid, 0, 7);
                string hlWant = lang == "ru-RU" ? "60.55(мин)" : "60.55(m)";
                Check("ячейка периода хранит код базы", hlCell == "60.55(m)", hlCell);
                Check("показ периода — на языке интерфейса", hlShown == hlWant, hlShown);
                Check("разбор ячейки периода прежний (годы)",
                      Math.Abs(NucBase.NucBase.HalfLifeYearsFromCell(hlCell) - 60.55 * 60 / 31536000) < 1e-15,
                      NucBase.NucBase.HalfLifeYearsFromCell(hlCell).ToString("R", CultureInfo.InvariantCulture));

                // Положительный контроль: без поставщика формата — культура потока.
                using (var bare = new DataGridView())
                {
                    bare.Columns.Add("e", "e");
                    bare.Rows.Add(727.33);
                    string raw = (string)bare.Rows[0].Cells[0].FormattedValue;
                    string rawWant = lang == "ru-RU" ? "727,33" : "727.33";
                    Check("контроль: голая таблица печатает культурой потока", raw == rawWant, raw);
                }
            }
        }

        static DecayRad Line(string name, string line, double energy, double intensity, double hl, string unit)
        {
            return new DecayRad
            {
                Name = name, DecayLine = line, Energy = energy, Intensity = intensity,
                HalfLife = hl, HalfLifeUnit = unit, XrayType = ""
            };
        }

        static string Shown(DataGridView grid, int row, int col)
        {
            return Convert.ToString(grid.Rows[row].Cells[col].FormattedValue, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- 2
        static void Backscatter(string lang)
        {
            Console.WriteLine("--- 2. подпись обратного рассеяния в составе FSA");
            string shown = FsaPalette.DisplayName(FsaResult.BackscatterLayerName);
            string want = lang == "ru-RU" ? "Обратное рассеяние" : "Backscatter";
            Check("подпись слоя", shown == want, shown);
            Check("служебное имя слоя прежнее", FsaResult.BackscatterLayerName == "Backscatter", FsaResult.BackscatterLayerName);
            Check("соседи прежние (наложения)", FsaPalette.DisplayName(FsaResult.PileUpLayerName) == BecquerelMonitor.Properties.Resources.FSAPileUpName,
                  FsaPalette.DisplayName(FsaResult.PileUpLayerName));
        }

        // ---------------------------------------------------------------- 3
        static void Card(string lang)
        {
            Console.WriteLine("--- 3. карточка нуклида");
            using (var form = new NucBase.NucBase())
            {
                Type t = typeof(NucBase.NucBase);
                MethodInfo show = t.GetMethod("ShowIsotopeCard", Any);
                Func<string, Label> L = n => (Label)t.GetField(n, Any).GetValue(form);
                var box = (GroupBox)t.GetField("groupBox2", Any).GetValue(form);

                string hlLabel = L("label5").Text;
                string want = lang == "ru-RU" ? "T½:" : "Half-Life:";
                Check("подпись периода", hlLabel == want, hlLabel);
                Check("подпись отбора по периоду", L("label12").Text == "T½ >", L("label12").Text);
                Console.WriteLine("    ширины: label5 «{0}» {1}, label12 «{2}» {3}", L("label5").Text, L("label5").Width,
                                  L("label12").Text, L("label12").Width);

                // Строка отбора: подписи и поля не налезают друг на друга.
                Func<string, Control> C = n => (Control)t.GetField(n, Any).GetValue(form);
                Apart("% выхода → поле", C("label8"), C("IntencityTextBox"));
                Apart("поле выхода → T½ >", C("IntencityTextBox"), C("label12"));
                Apart("T½ > → поле периода", C("label12"), C("HalfLifeTextBox"));
                Apart("поле периода → единица", C("HalfLifeTextBox"), C("HalfLifeUOMComboBox"));

                // Худшие строки базы: самые длинные период, удельная активность и
                // распространённость; плюс предел периода («>») — 81 нуклид.
                var worst = new List<Nuclide>
                {
                    Nuc(83, 129, "60.55", "m", 3633.0, 0.0, false),
                    Nuc(1, 2, "3.705e-13", "ns", 3.705e-22, 99.91119, true),
                    Nuc(52, 78, "0.79E+21", "Y", 2.49e28, 34.08, true),
                    Nuc(26, 34, "STABLE", "", 0.0, 99.9998, false),
                };
                foreach (Nuclide nuc in worst)
                {
                    show.Invoke(form, new object[] { "212BI", nuc });
                    Console.WriteLine("    {0} | {1} | {2}", L("IsotopeHLLabel").Text, L("IsotopeSpecActivity").Text, L("IsotopeAbundance").Text);
                    Apart("имя → Z", L("label2"), L("IsotopeNameLabel"));
                    Apart("Z → N", L("IsotopeZLabel"), L("label4"));
                    Apart("N → распространённость", L("IsotopeNLabel"), L("abundanceLbl"));
                    Apart("подпись распространённости → значение", L("abundanceLbl"), L("IsotopeAbundance"));
                    Apart("подпись периода → значение", L("label5"), L("IsotopeHLLabel"));
                    Apart("период → подпись удельной активности", L("IsotopeHLLabel"), L("label13"));
                    Apart("подпись удельной активности → значение", L("label13"), L("IsotopeSpecActivity"));
                    Inside(box, L("IsotopeAbundance"));
                    Inside(box, L("IsotopeSpecActivity"));
                }

                show.Invoke(form, new object[] { "212BI", worst[0] });
                string hl = L("IsotopeHLLabel").Text;
                Check("единица периода", hl == (lang == "ru-RU" ? "60.55 мин" : "60.55 m"), hl);
                show.Invoke(form, new object[] { "56FE", worst[3] });
                Check("стабильный", L("IsotopeHLLabel").Text == (lang == "ru-RU" ? "стабилен " : "STABLE "), "«" + L("IsotopeHLLabel").Text + "»");

                var combo = (ComboBox)t.GetField("HalfLifeUOMComboBox", Any).GetValue(form);
                var codes = (string[])t.GetField("FilterUnits", Any).GetValue(null);
                bool same = combo.Items.Count == codes.Length;
                var shown = new List<string>();
                for (int i = 0; same && i < codes.Length; i++)
                {
                    string item = (string)combo.Items[i];
                    shown.Add(item);
                    same &= item == NucBaseFramework.HalfLifeUnitCaption(codes[i]);
                }

                Check("пункты единиц отбора = коды FilterUnits по месту", same, string.Join(" ", shown));
            }
        }

        static Nuclide Nuc(int z, int n, string hl, string unit, double hlSec, double abundance, bool limit)
        {
            return new Nuclide { Z = z, N = n, HalfLife = hl, HalfLifeUOM = unit, HalfLife_Sec = hlSec, Abundance = abundance, HalfLifeIsLimit = limit };
        }

        static void Apart(string what, Control left, Control right)
        {
            // AutoSize-метка с пустым текстом шириной 0 — ей не с кем пересекаться.
            int gap = right.Left - left.Right;
            if (gap < 2 && left.Width > 0 && right.Width > 0)
            {
                Check(what, false, string.Format(CultureInfo.InvariantCulture, "«{0}» [{1}..{2}] налезает на «{3}» [{4}..{5}]",
                                                left.Text, left.Left, left.Right, right.Text, right.Left, right.Right));
            }
        }

        static void Inside(Control box, Control c)
        {
            if (c.Right > box.ClientSize.Width - 2)
            {
                Check("в рамке", false, string.Format(CultureInfo.InvariantCulture, "«{0}» кончается на {1}, рамка {2}", c.Text, c.Right, box.ClientSize.Width));
            }
        }

        // ---------------------------------------------------------------- 4
        static void Editor(string lang)
        {
            Console.WriteLine("--- 4. редактор нуклидов");
            var form = new NuclideDefinitionForm();
            Type t = typeof(NuclideDefinitionForm);
            var table = (XPTable.Models.Table)t.GetField("table1", Any).GetValue(form);
            var model = (TableModel)t.GetField("tableModel1", Any).GetValue(form);
            FieldInfo active = t.GetField("activeNuclide", Any);
            FieldInfo listDirty = t.GetField("definitionsDirty", Any);
            var save = (Button)t.GetField("button6", Any).GetValue(form);
            t.GetMethod("NuclideDefinitionForm_Load", Any).Invoke(form, new object[] { form, EventArgs.Empty });
            IntPtr h = form.Handle;
            IntPtr th = table.Handle;
            if (model.Rows.Count == 0)
            {
                Check("строки редактора", false, "нет ни одной строки (конфиг рядом с пробой?)");
                form.Dispose();
                return;
            }

            int row = 0;
            var def = (NuclideDefinition)model.Rows[row].Tag;
            active.SetValue(form, def);
            def.Dirty = false;
            // Период — с СЕМНАДЦАТЬЮ значащими цифрами (Tl-208, 3.053 мин в годах): круг
            // double → decimal → double, который прежде проходило всякое закрытие правки,
            // теряет у него шестнадцатую цифру. Ячейка — тем же числом, как её кладёт список.
            double hlOrig = def.HalfLife;
            def.HalfLife = 3.053 * 60 / 31536000;
            model.Rows[row].Cells[2].Data = def.HalfLife;
            double e0 = def.Energy, h0 = def.HalfLife;
            string n0 = def.Name;
            foreach (int column in new[] { 0, 1, 2 })
            {
                table.EditCell(row, column);
                bool opened = table.IsEditing;
                table.StopEditing();
                if (!opened && column == 0)
                {
                    Console.WriteLine("    ячейка имени не правится в таблице — пропуск");
                    continue;
                }

                Check("ячейка " + column + " открыта и закрыта без ввода — «изменено» не взведено",
                      opened && !def.Dirty && !save.Enabled && !(bool)listDirty.GetValue(form),
                      string.Format(CultureInfo.InvariantCulture, "открыта={0}, Dirty={1}, «Сохранить» {2}, список={3}",
                                    opened, def.Dirty, save.Enabled ? "горит" : "погашена", listDirty.GetValue(form)));
            }

            Check("значения не тронуты (побитово)", def.Energy == e0 && def.HalfLife == h0 && def.Name == n0,
                  def.Energy.ToString("R", CultureInfo.InvariantCulture) + " / " + def.HalfLife.ToString("R", CultureInfo.InvariantCulture));

            // Положительный контроль: вписанное число — изменение.
            Typed(table, row, 1, (e0 + 1.0).ToString("R", CultureInfo.InvariantCulture));
            Check("контроль: вписанная энергия взводит «изменено»", def.Dirty && save.Enabled && def.Energy == e0 + 1.0,
                  string.Format(CultureInfo.InvariantCulture, "Dirty={0}, энергия {1}", def.Dirty, def.Energy.ToString("R", CultureInfo.InvariantCulture)));
            def.Energy = e0;
            def.Dirty = false;

            // Поле периода: число Tl-208 (3.053 мин в годах) — целиком, порядок виден.
            var field = (TextBox)t.GetField("doubleTextBox2", Any).GetValue(form);
            var years = (Label)t.GetField("label6", Any).GetValue(form);
            def.HalfLife = 3.053 * 60 / 31536000;
            t.GetMethod("LoadFormContents", Any).Invoke(form, new object[] { def });
            int need = TextRenderer.MeasureText(field.Text, field.Font).Width + 6;
            Check("поле периода вмещает число целиком", need <= field.ClientSize.Width,
                  string.Format(CultureInfo.InvariantCulture, "«{0}» нужно {1}, поле {2}", field.Text, need, field.ClientSize.Width));
            Check("подпись «{0}» правее поля".Replace("{0}", years.Text), years.Left >= field.Right + 2,
                  string.Format(CultureInfo.InvariantCulture, "поле до {0}, подпись с {1}", field.Right, years.Left));
            def.HalfLife = hlOrig;
            form.Dispose();
        }

        static void Typed(XPTable.Models.Table table, int row, int column, string text)
        {
            table.EditCell(row, column);
            object editor = table.EditingCellEditor;
            var box = (TextBox)editor.GetType().GetProperty("TextBox").GetValue(editor);
            box.Text = text;
            Type t = editor.GetType();
            while (t != null && t.GetProperty("UserEdit", BindingFlags.NonPublic | BindingFlags.Instance) == null) t = t.BaseType;
            t.GetProperty("UserEdit", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(editor, true);
            table.StopEditing();
        }

        // ---------------------------------------------------------------- 5
        static void CountLimit(string lang)
        {
            Console.WriteLine("--- 5. общие настройки: поле предела счёта");
            using (var form = new GlobalConfigForm())
            {
                Type t = typeof(GlobalConfigForm);
                Func<string, Control> C = n => (Control)t.GetField(n, Any).GetValue(form);
                var box = (NumericUpDown)C("numericUpDown13");
                box.Value = box.Maximum;
                string text = box.Maximum.ToString(CultureInfo.InvariantCulture);
                int need = TextRenderer.MeasureText(text, box.Font).Width + SystemInformation.VerticalScrollBarWidth + 6;
                Check("поле вмещает " + text, need <= box.Width,
                      string.Format(CultureInfo.InvariantCulture, "нужно {0}, поле {1} [{2}..{3}]", need, box.Width, box.Left, box.Right));
                // (П275) Поле — строкой правой колонки, под «Привязкой масштаба»: в прежнем
                // месте между колонками (84 точки до «Chart Refresh Cycle») 90 не помещалось.
                Apart("подпись → поле", C("label90"), box);
                foreach (string n in new[] { "numericUpDown1", "numericUpDown2", "numericUpDown3", "label27", "label28",
                                             "label44", "comboBox11", "label85", "label86", "numericUpDown8", "label90" })
                {
                    Control other = C(n);
                    bool overlap = other.Bounds.IntersectsWith(box.Bounds);
                    Check("не налезает на " + n + " «" + other.Text + "»", !overlap,
                          string.Format(CultureInfo.InvariantCulture, "{0} против {1}", other.Bounds, box.Bounds));
                }
            }
        }
    }
}
