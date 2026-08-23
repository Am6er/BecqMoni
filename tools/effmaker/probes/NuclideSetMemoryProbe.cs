using BecquerelMonitor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NuclideSetMemoryProbe
{
    /// <summary>
    /// Набор нуклидов запоминается ЗА ДОКУМЕНТОМ, а не один на всё приложение
    /// (TODO R9).
    ///
    ///     nuclidesetmemoryprobe
    ///
    /// Прежде выбор жил единственным полем `NuclideDefinitionManager.ActiveSet`.
    /// Спектров при этом открыто несколько: разметив торием один, человек
    /// получал торий на всех соседних, а вернуться к прежнему выбору было
    /// нечем. Теперь выбор хранит сам документ
    /// (`DocEnergySpectrum.SelectedNuclideSet`), а `ActiveSet` — выбор того из
    /// них, который сейчас на экране (его читает график, до панели он не
    /// дотягивается).
    ///
    /// Проверяется:
    ///
    /// 1. ВЫБОР ЛОЖИТСЯ В ДОКУМЕНТ и виден менеджеру;
    /// 2. СОСЕДНИЙ ДОКУМЕНТ не тронут;
    /// 3. ВОЗВРАТ — переключились туда и обратно, выбор каждого на месте (ради
    ///    этого всё и затевалось);
    /// 4. НАСЛЕДОВАНИЕ — документ, заведённый при выбранном наборе, берёт его
    ///    себе: спектры одной пробы открывают пачкой;
    /// 5. ПЕРЕЧИТЫВАНИЕ СПИСКА (заход в редактор наборов) выбор не сбивает —
    ///    очистка списка поднимает событие, и без флага она стирала бы память
    ///    документа;
    /// 6. УДАЛЁННЫЙ НАБОР забывается и документом, а не только списком;
    /// 7. НОВЫЙ НАБОР, о котором список ещё не знает, — панель перечитывает его
    ///    сама, а не промахивается мимо строки.
    ///
    /// Проба ничего не пишет: конфигурация только читается, служебные наборы
    /// живут в памяти и на диск не уезжают — `SaveDefinitionFile` не зовётся.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ».
    /// </summary>
    static class Program
    {
        static int bad;

        static ComboBox combo;

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo culture = (CultureInfo)Thread.CurrentThread.CurrentCulture.Clone();
            culture.NumberFormat.NumberDecimalSeparator = ".";
            Thread.CurrentThread.CurrentCulture = culture;

            // ⛔ ОБЕ карты примитивов ROI — ДО ЛЮБОГО менеджера-одиночки
            // (`T60`). `GlobalConfigManager` тянет за собой `ROIConfigManager`,
            // тот подставляет из обеих карт, и на пустых падает; отказ выходит
            // МОДАЛЬНЫМ окном «Не удалось загрузить конфигурационный файл ROI»,
            // а в безоконном прогоне окно вешает пробу навсегда. В приложении
            // порядок держит `MainForm` (обе строки подряд, до менеджеров), но
            // проба, тронувшая менеджер раньше формы, этот порядок обходит.
            ROIPrimitiveDefinition.InitializeROIPrimitiveDefinitions();
            ROIPrimitiveOperation.InitializeROIPrimitiveOperations();

            NuclideDefinitionManager nuclides = NuclideDefinitionManager.GetInstance();
            List<NuclideSet> sets = nuclides.NuclideSets;
            int originalCount = sets.Count;

            // Наборы заводятся СВОИ, а не берутся из конфига: проба обязана
            // знать ожидаемое наперёд, а чужие наборы меняются без неё.
            NuclideSet alpha = new NuclideSet { Id = Guid.NewGuid(), Name = "ПРОБА-альфа" };
            NuclideSet beta = new NuclideSet { Id = Guid.NewGuid(), Name = "ПРОБА-бета" };
            sets.Add(alpha);
            sets.Add(beta);

            MainForm mainForm = new MainForm();
            DCPeakDetectionView panel = new DCPeakDetectionView(mainForm);
            combo = (ComboBox)typeof(DCPeakDetectionView)
                .GetField("comboBoxNuclSet", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(panel);

            int alphaRow = sets.IndexOf(alpha) + 1;
            int betaRow = sets.IndexOf(beta) + 1;

            DocEnergySpectrum a = new DocEnergySpectrum();
            DocEnergySpectrum b = new DocEnergySpectrum();
            Check("новый документ наследует текущий выбор (его нет)",
                  a.SelectedNuclideSet, null);

            // Человек выбирает набор для документа A.
            mainForm.ActiveDocument = a;
            panel.ShowPeakDetectionResult();
            combo.SelectedIndex = alphaRow;
            Check("A: выбор лёг в документ", a.SelectedNuclideSet, alpha);
            Check("A: выбор виден менеджеру (график)", nuclides.ActiveSet, alpha);
            Check("B: чужой выбор не тронул", b.SelectedNuclideSet, null);

            // Переход на B: своего выбора у него нет — «все нуклиды».
            mainForm.ActiveDocument = b;
            panel.ShowPeakDetectionResult();
            Check("B: в списке «все нуклиды»", combo.SelectedIndex, 0);
            Check("B: менеджер тоже пуст", nuclides.ActiveSet, null);

            combo.SelectedIndex = betaRow;
            Check("B: свой выбор лёг в документ", b.SelectedNuclideSet, beta);

            // Возврат на A — тот самый случай, ради которого всё затевалось.
            mainForm.ActiveDocument = a;
            panel.ShowPeakDetectionResult();
            Check("A: выбор вернулся в список", combo.SelectedIndex, alphaRow);
            Check("A: выбор вернулся менеджеру", nuclides.ActiveSet, alpha);
            Check("A: документ помнит своё", a.SelectedNuclideSet, alpha);

            mainForm.ActiveDocument = b;
            panel.ShowPeakDetectionResult();
            Check("B: и его выбор на месте", combo.SelectedIndex, betaRow);
            Check("B: менеджер согласен", nuclides.ActiveSet, beta);

            // Документ, заведённый при выбранном наборе, наследует его.
            DocEnergySpectrum c = new DocEnergySpectrum();
            Check("C: новый документ унаследовал выбор", c.SelectedNuclideSet, beta);

            // Заход в редактор наборов (перечитывание списка) выбор не сбивает.
            panel.RefreshNuclideSets();
            Check("B: перечитывание списка выбор сохранило", b.SelectedNuclideSet, beta);
            Check("B: и строку в списке тоже", combo.SelectedIndex, betaRow);

            // Набор удалили, пока документ A лежал в фоне.
            sets.Remove(alpha);
            panel.RefreshNuclideSets();
            mainForm.ActiveDocument = a;
            panel.ShowPeakDetectionResult();
            Check("A: удалённый набор забыт документом", a.SelectedNuclideSet, null);
            Check("A: в списке «все нуклиды»", combo.SelectedIndex, 0);
            Check("A: менеджер пуст", nuclides.ActiveSet, null);

            // Набор завели, пока список этого не видел.
            NuclideSet gamma = new NuclideSet { Id = Guid.NewGuid(), Name = "ПРОБА-гамма" };
            sets.Add(gamma);
            a.SelectedNuclideSet = gamma;
            panel.ShowPeakDetectionResult();
            Check("A: список догнал наборы", combo.SelectedIndex, sets.IndexOf(gamma) + 1);
            Check("A: выбор уцелел", nuclides.ActiveSet, gamma);

            sets.Remove(beta);
            sets.Remove(gamma);
            Check("наборы пользователя не тронуты", sets.Count, originalCount);

            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : bad + " ПРОВЕРОК ПРОВАЛЕНО");

            // Окна закрываются РУКАМИ. Документ заводит контроллер измерения, а
            // тот — контроллер прибора со своими неуправляемыми потрохами;
            // брошенные на финализатор, они валят процесс при выходе, и
            // сошедшаяся проба возвращает код падения.
            foreach (Form form in new Form[] { c, b, a, panel, mainForm })
            {
                form.Dispose();
            }

            return bad == 0 ? 0 : 1;
        }

        static void Check(string title, object got, object want)
        {
            bool ok = ReferenceEquals(got, want) || (got != null && got.Equals(want));
            if (!ok)
            {
                bad++;
            }

            Console.WriteLine("{0} {1,-46} {2} (ждали {3})",
                              ok ? "  ok" : "ПЛОХО", title, Name(got), Name(want));
        }

        static string Name(object value)
        {
            NuclideSet set = value as NuclideSet;
            if (set != null)
            {
                return set.Name;
            }

            return value == null ? "«все нуклиды»" : value.ToString();
        }
    }
}
