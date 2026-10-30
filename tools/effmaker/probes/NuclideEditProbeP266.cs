using BecquerelMonitor;
using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using XPTable.Models;

namespace BecquerelMonitor.Probes
{
    /// <summary>
    /// (П266, 14.10.2026) Правка ЭНЕРГИИ и ПЕРИОДА прямо в таблице редактора нуклидов
    /// (`NuclideDefinitionForm`, двойной щелчок по ячейке): что попадает в определение.
    ///
    /// Колонки «Энергия» и «Период» — `NumberColumn` XPTable с умолчаниями Minimum 0 /
    /// Maximum 100, а редактор ячейки (`NumberCellEditor.SetEditValue`) зажимает значение в
    /// них ещё ДО ввода; `table1_EditingStopped` переносит то, что вышло, в определение.
    /// Проба открывает ячейку и закрывает её БЕЗ ввода (как Enter или уход фокуса) — значение
    /// обязано остаться прежним. Положительный контроль — ячейка со значением в 0…100 и
    /// ячейка, куда «вписано» новое число через текст редактора.
    ///
    /// Конфиг — ТОЛЬКО из текущего каталога (копия поставки рядом с пробой); файл не пишется:
    /// проба зовёт обработчик правки, а не «Сохранить».
    /// Коды: 0 — сошлось; 1 — значение изменилось без ввода; 2 — нет строк для проверки.
    /// </summary>
    static class NuclideEditProbeP266
    {
        static int bad;

        [STAThread]
        static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            var form = new NuclideDefinitionForm();
            Type t = typeof(NuclideDefinitionForm);
            BindingFlags any = BindingFlags.NonPublic | BindingFlags.Instance;
            var table = (XPTable.Models.Table)t.GetField("table1", any).GetValue(form);
            var model = (TableModel)t.GetField("tableModel1", any).GetValue(form);
            FieldInfo active = t.GetField("activeNuclide", any);
            t.GetMethod("NuclideDefinitionForm_Load", any).Invoke(form, new object[] { form, EventArgs.Empty });
            IntPtr h = form.Handle;     // ручка формы и таблицы — без показа окна
            IntPtr th = table.Handle;
            Console.WriteLine("строк в таблице редактора: {0}", model.Rows.Count);

            int big = -1, small = -1;
            for (int i = 0; i < model.Rows.Count; i++)
            {
                var def = (NuclideDefinition)model.Rows[i].Tag;
                if (big < 0 && def.Energy > 100.0 && def.HalfLife > 100.0) big = i;
                if (small < 0 && def.Energy > 0.0 && def.Energy < 100.0 && def.HalfLife > 0.0 && def.HalfLife < 100.0) small = i;
            }

            if (big < 0 || small < 0)
            {
                Console.WriteLine("нет строк для проверки (нужны энергия и период > 100 и < 100)");
                return 2;
            }

            foreach (int row in new[] { big, small })
            {
                var def = (NuclideDefinition)model.Rows[row].Tag;
                foreach (int column in new[] { 1, 2 })
                {
                    double before = column == 1 ? def.Energy : def.HalfLife;
                    active.SetValue(form, def);
                    bool editable = table.IsCellEditable(row, column);
                    table.EditCell(row, column);
                    Console.WriteLine("    редактор открыт: {0}", table.IsEditing);
                    table.StopEditing();
                    double after = column == 1 ? def.Energy : def.HalfLife;
                    bool ok = after == before;
                    if (!ok) bad++;
                    Console.WriteLine("  {0,-24} {1} {2}: правится={3}, было {4}, после открытия и закрытия без ввода {5}",
                                      def.Name, column == 1 ? "энергия" : "период ", ok ? "ок" : "!!", editable,
                                      before.ToString("R", CultureInfo.InvariantCulture), after.ToString("R", CultureInfo.InvariantCulture));
                    if (column == 1) def.Energy = before; else def.HalfLife = before;   // вернуть, файл не пишется
                }
            }

            // Положительный контроль: число ВПИСАНО в редактор (как с клавиатуры — текст и
            // признак «правил человек»), оно обязано доехать до определения как есть.
            {
                var def = (NuclideDefinition)model.Rows[big].Tag;
                double before = def.Energy;
                active.SetValue(form, def);
                Typed(table, big, 1, "2614.511");
                bool ok = def.Energy == 2614.511;
                if (!ok) bad++;
                Console.WriteLine("  {0,-24} энергия вписана 2614.511 {1} → в определении {2}", def.Name, ok ? "ок" : "!!",
                                  def.Energy.ToString("R", CultureInfo.InvariantCulture));
                def.Energy = before;
            }

            form.Dispose();

            // Плечо `--device` (таблица многоточечной калибровки прибора `DeviceConfigForm.table2`)
            // СНЯТО (`AMBER280`, 29.10.2026): таблицы больше нет — Amber, дословно: «Во вкладке
            // убрать функциональность калибровки и оставить только данные».
            if (Environment.GetCommandLineArgs().Length > 1 && Environment.GetCommandLineArgs()[1] == "--device")
            {
                Console.WriteLine("  --device: таблицы многоточечной калибровки прибора нет (AMBER280) — плечо снято");
            }

            Console.WriteLine(bad == 0 ? "СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        /// <summary>Открыть ячейку, вписать текст «как с клавиатуры» и закрыть правку.</summary>
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
    }
}
