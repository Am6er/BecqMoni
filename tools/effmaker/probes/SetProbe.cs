using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

/// <summary>
/// Что конструктор кривой видит в наборах нуклидов, и что он из них
/// выбрасывает. Печатает ровно то, что попадает в выпадающий список строки
/// спектра, плюс поимённо отвергнутые наборы с причиной.
///
/// Читает конфиг ТОЛЬКО из текущего каталога (`config\NuclideDefinition.xml`):
/// пробу запускают из копии, потому что при отсутствии файла менеджер заводит
/// свой умолчательный, а затирать чужой конфиг пробой нельзя.
/// </summary>
static class SetProbe
{
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();
        Console.WriteLine("Наборов в конфиге: {0}, нуклидов: {1}",
                          manager.NuclideSets.Count, manager.NuclideDefinitions.Count);
        foreach (NuclideSet set in manager.NuclideSets)
        {
            Console.WriteLine("  {0}  {1}", set.Id, set.Name);
        }

        List<EfficiencyLibrary.SetReject> rejected;
        Dictionary<string, List<EfficiencyLine>> chains = EfficiencyLibrary.BuildChains(out rejected);

        Console.WriteLine();
        Console.WriteLine("В списке конструктора ({0}):", chains.Count);
        foreach (KeyValuePair<string, List<EfficiencyLine>> chain in chains)
        {
            Console.WriteLine("  {0}: линий {1}", chain.Key, chain.Value.Count);
        }

        Console.WriteLine();
        Console.WriteLine("Отвергнуто ({0}):", rejected.Count);
        foreach (EfficiencyLibrary.SetReject reject in rejected)
        {
            Console.WriteLine("  {0}: {1}", reject.Name, reject.Reason);
        }

        return CheckForm(chains.Count);
    }

    /// <summary>
    /// Сама форма, без единого клика: что попало в выпадающий список строки и
    /// на каких полях висят подсказки. Проверять глазами это дорого — список
    /// заполняется в коде, а подсказку видно только под мышью.
    /// </summary>
    static int CheckForm(int expectedChains)
    {
        int bad = 0;
        using (EfficiencyMakerForm form = new EfficiencyMakerForm())
        {
            DataGridViewComboBoxColumn column =
                (DataGridViewComboBoxColumn)Field(form, "nuclideSetColumn");
            Console.WriteLine();
            Console.WriteLine("Строк в выпадающем списке: {0} (наборов {1} + «вся библиотека»)",
                              column.Items.Count, expectedChains);
            foreach (object item in column.Items)
            {
                Console.WriteLine("  {0}", item);
            }

            if (column.Items.Count != expectedChains + 1)
            {
                Console.WriteLine("  !! ожидалось {0}", expectedChains + 1);
                bad++;
            }

            ToolTip hints = (ToolTip)Field(form, "hints");
            string[] fields =
            {
                "orderLabel", "orderNumericUpDown",
                "minIntensityLabel", "minIntensityNumericUpDown",
                "minSignificanceLabel", "minSignificanceNumericUpDown",
                "anchorLabel", "anchorEnergyTextBox", "anchorEfficiencyTextBox"
            };

            Console.WriteLine();
            Console.WriteLine("Подсказки:");
            foreach (string name in fields)
            {
                Control control = (Control)Field(form, name);
                string text = hints.GetToolTip(control);
                Console.WriteLine("  {0,-32} {1}", name,
                                  string.IsNullOrEmpty(text) ? "!! ПУСТО" : Head(text));
                if (string.IsNullOrEmpty(text))
                {
                    bad++;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : string.Format("НЕ СОШЛОСЬ: {0}", bad));
        return bad == 0 ? 0 : 1;
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

    static string Head(string text)
    {
        return text.Length <= 48 ? text : text.Substring(0, 48) + "...";
    }
}
