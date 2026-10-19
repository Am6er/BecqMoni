using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using XPTable.Events;
using XPTable.Models;

namespace BecquerelMonitor
{
    // Token: 0x02000017 RID: 23
    public partial class NuclideDefinitionForm : Form
    {
        // Token: 0x060000C2 RID: 194 RVA: 0x00003DA4 File Offset: 0x00001FA4
        public NuclideDefinitionForm()
        {
            this.InitializeComponent();
            this.Icon = Resources.becqmoni;
            this.button6.Enabled = false;
            // ⛔ (П266, 14.10.2026) ПРЕДЕЛЫ ЧИСЛОВЫХ КОЛОНОК ТАБЛИЦЫ. «Энергия» и «Период» —
            // `NumberColumn` XPTable с умолчаниями Minimum 0 / Maximum 100, и редактор ячейки
            // (`NumberCellEditor.SetEditValue`) зажимает значение в них ещё ДО ввода, а
            // `table1_EditingStopped` переносит зажатое в определение: двойной щелчок по
            // ячейке и Enter (или уход фокуса) без единого символа делали линию U-235
            // 109.19 кэВ линией 100 кэВ, а период 7·10⁸ лет — 100 годами (замер
            // `NuclideEditProbeP266`), в памяти сразу и в файле при сохранении. Пределы —
            // во всю ширину `decimal` (7.9·10²⁸: самый длинный период базы ~10²⁴ лет), снизу
            // ноль, как и было.
            this.textColumn2.Maximum = decimal.MaxValue;
            this.textColumn3.Maximum = decimal.MaxValue;
            // (`AMBER242`, 20.10.2026) Период рисуется шестью значащими цифрами. С форматом
            // колонки «G» он печатался полным числом — до 15 знаков («0.0027397260274») — и в
            // колонку не влезал: при выравнивании вправо голова числа уходила за левый край.
            // ⚠ Это формат только ОТРИСОВКИ (`NumberCellRenderer`). Редактор ячейки
            // (`NumberCellEditor`) формат колонки не берёт — у него свой «G», заданный в его
            // конструкторе и больше нигде, и начальное значение он берёт из `Cell.Data`, а не из
            // напечатанного. Поэтому Enter в нетронутой ячейке период не округляет (класс П266):
            // проверено пробой `NuclideEditProbeP266` на этой правке.
            this.textColumn3.Format = "G6";
            this.DisableForm();
        }

        // Token: 0x060000C3 RID: 195 RVA: 0x00003DD0 File Offset: 0x00001FD0
        void NuclideDefinitionForm_Load(object sender, EventArgs e)
        {
            this.ListupNuclideDefinitions();
        }

        // Token: 0x060000C4 RID: 196 RVA: 0x00003DD8 File Offset: 0x00001FD8
        void ListupNuclideDefinitions()
        {
            this.table1.SuspendLayout();
            this.tableModel1.Rows.Clear();
            this.tableModel1.Selections.Clear();
            // ⛔ (П318, 15.10.2026) Файл библиотеки здесь БОЛЬШЕ НЕ ПЕРЕЧИТЫВАЕТСЯ.
            // Перечитыванием (`d20d69fb`) откатывались правки на ответ «Нет», но
            // оно подменяло ВСЕ объекты менеджера — линии и наборы, — а их держат
            // по ссылке: выбор набора у каждого открытого спектра
            // (`DocEnergySpectrum.SelectedNuclideSet`, панель ищет его
            // `NuclideSets.IndexOf`) при ближайшем обновлении панели молча
            // слетал на «все нуклиды», и разметка пиков и FSA шли по всей
            // библиотеке. Занятый файл ронял окно (NullReferenceException) и
            // оставлял менеджер без библиотеки. Откат «Нет» теперь — по снимку
            // линий до правки (`RollBackUnsaved`), объекты те же. Замер —
            // `P318RevertAndCloseProbe`, журнал П318.
            this.manager.NuclideDefinitions.Sort();
            // ⛔ (`AMBER196`) Активная линия ищется ПО ССЫЛКЕ, а если список
            // перечитан с диска (объекты новые) — по имени И энергии. Прежде
            // искали по одному имени: выделялись все линии нуклида, а активной
            // становилась последняя из них — в полях одна линия, следующая
            // правка ложилась в другую.
            NuclideDefinition listedActive = this.FindListed(this.activeNuclide);
            foreach (NuclideDefinition nuclideDefinition in this.manager.NuclideDefinitions)
            {
                Row row = new Row();
                CellStyle visibilitycellStyle= new CellStyle();
                visibilitycellStyle.BackColor = System.Drawing.Color.LightGray;
                if (nuclideDefinition.Visible) {
                    row.Cells.Add(new Cell(nuclideDefinition.Name));
                    row.Cells.Add(new Cell(nuclideDefinition.Energy.ToString(CultureInfo.InvariantCulture), nuclideDefinition.Energy));
                    row.Cells.Add(new Cell(nuclideDefinition.HalfLife.ToString(CultureInfo.InvariantCulture), nuclideDefinition.HalfLife));
                } else
                {
                    row.Cells.Add(new Cell(nuclideDefinition.Name, visibilitycellStyle));
                    row.Cells.Add(new Cell(nuclideDefinition.Energy.ToString(CultureInfo.InvariantCulture), nuclideDefinition.Energy, visibilitycellStyle));
                    row.Cells.Add(new Cell(nuclideDefinition.HalfLife.ToString(CultureInfo.InvariantCulture), nuclideDefinition.HalfLife, visibilitycellStyle));
                }
                row.Tag = nuclideDefinition;
                this.tableModel1.Rows.Add(row);
                if (listedActive != null && ReferenceEquals(nuclideDefinition, listedActive))
                {
                    this.activeNuclide = nuclideDefinition;
                    this.tableModel1.Selections.AddCell(row.Index, 0);
                }
            }
            this.table1.ResumeLayout();
        }

        /// <summary>
        /// (`AMBER196`) Та же линия в текущем списке менеджера: сама (по
        /// ссылке), а после перечитывания файла — первая с тем же именем И той
        /// же энергией. Нет такой — null.
        /// </summary>
        NuclideDefinition FindListed(NuclideDefinition nuclide)
        {
            if (nuclide == null)
            {
                return null;
            }

            foreach (NuclideDefinition listed in this.manager.NuclideDefinitions)
            {
                if (ReferenceEquals(listed, nuclide))
                {
                    return listed;
                }
            }

            foreach (NuclideDefinition listed in this.manager.NuclideDefinitions)
            {
                if (listed.Name == nuclide.Name && listed.Energy == nuclide.Energy)
                {
                    return listed;
                }
            }

            return null;
        }

        // Token: 0x060000C5 RID: 197 RVA: 0x00003F34 File Offset: 0x00002134
        void LoadFormContents(NuclideDefinition nuclide)
        {
            // (П341, `A338`) Что показано в полях справа — по нему
            // `ShowLibraryChange` отличает поле, которое человек правил, от
            // поля, которое осталось таким, каким его поставила загрузка.
            this.shownValues = LineValues.Of(nuclide);
            this.contentsLoading = true;
            this.textBox1.Text = nuclide.Name;
            this.chainTextBox.Text = nuclide.Chain;
            this.doubleTextBox1.Text = nuclide.Energy.ToString(CultureInfo.InvariantCulture);
            this.doubleTextBox2.Text = nuclide.HalfLife.ToString(CultureInfo.InvariantCulture);
            this.intensityTextBox.Text = nuclide.Intencity.ToString(CultureInfo.InvariantCulture);
            this.textBox2.Text = nuclide.Note;
            this.colorComboBox1.SelectedColor = nuclide.NuclideColor.Color;
            this.checkBox1.Checked = nuclide.Visible;
            this.colorComboBox1.Refresh();
            this.contentsLoading = false;
        }

        // Token: 0x060000C6 RID: 198 RVA: 0x00003FAC File Offset: 0x000021AC
        bool SaveFormContents(NuclideDefinition nuclide)
        {
            try
            {
                nuclide.Name = this.textBox1.Text;
                nuclide.Chain = this.chainTextBox.Text.Trim();
                nuclide.Energy = this.doubleTextBox1.GetValue();
                nuclide.HalfLife = this.doubleTextBox2.GetValue();
                nuclide.Intencity = this.intensityTextBox.GetValue();
                nuclide.Note = this.textBox2.Text;
                nuclide.NuclideColor.Color = this.colorComboBox1.SelectedColor;
                nuclide.Visible = this.checkBox1.Checked;
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        // Token: 0x060000C7 RID: 199 RVA: 0x00004024 File Offset: 0x00002224
        void button3_Click(object sender, EventArgs e)
        {
            NuclideDefinition item = new NuclideDefinition();
            if (!CommitDefinitions(this.manager, () => this.manager.NuclideDefinitions.Add(item)))
            {
                // Записи не вышло — значит нуклид не заведён, и в списке его быть
                // не должно: иначе он выглядит заведённым до перезапуска, а после
                // него исчезает. Окно об отказе показал сам менеджер.
                this.ListupNuclideDefinitions();
                return;
            }
            // (П318) Файл записан целиком — с несохранёнными правками линий тоже:
            // откатывать «Нет» больше нечего.
            this.ForgetUnsaved();
            this.activeNuclide = item;
            this.ListupNuclideDefinitions();
            this.UpdatePeakDetectionResult();
        }

        /// <summary>
        /// Изменить библиотеку нуклидов и записать её на диск. Отказ записи
        /// ОТМЕНЯЕТ изменение: список в памяти обязан сходиться с файлом.
        ///
        /// ⛔ `A9`: прежде обе правки списка — заведение (<c>button3_Click</c>) и
        /// удаление (<c>button4_Click</c>) — меняли
        /// <see cref="NuclideDefinitionManager.NuclideDefinitions"/> и выбрасывали
        /// <c>bool</c> от <c>SaveDefinitionFile</c>. При отказе записи человек
        /// видел окно об ошибке И одновременно видел, что нуклид удалён из списка;
        /// после перезапуска нуклид возвращался на место.
        ///
        /// Измерено 31.08.2026 пробой на настоящем менеджере при захваченном
        /// <c>NuclideDefinition.xml</c>: прежним кодом — «SaveDefinitionFile=False,
        /// нуклид в списке НЕТ, нуклид в файле да»; этим — «вернул False, нуклид в
        /// списке да, нуклид в файле да».
        ///
        /// Метод статический нарочно: он не трогает ни одного поля формы, поэтому
        /// проверяется отражением, не поднимая окон.
        /// </summary>
        static bool CommitDefinitions(NuclideDefinitionManager manager, Action change)
        {
            List<NuclideDefinition> before = new List<NuclideDefinition>(manager.NuclideDefinitions);
            change();
            if (manager.SaveDefinitionFile())
            {
                return true;
            }
            manager.NuclideDefinitions.Clear();
            manager.NuclideDefinitions.AddRange(before);
            return false;
        }

        void checkBox1_CheckStateChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading || this.activeNuclide == null)
            {
                return;
            }

            // (П318) Галка пишет в определение сразу — снимок ДО неё, иначе «Нет»
            // её не откатывал: прежний откат перечитыванием файла срабатывал только
            // после правки в таблице.
            this.RememberBeforeEdit(this.activeNuclide);
            this.activeNuclide.Visible = this.checkBox1.Checked;
            this.NoteEdited(this.activeNuclide);
            this.SetActiveNuclideDirty();
        }

        // Token: 0x060000C8 RID: 200 RVA: 0x0000406C File Offset: 0x0000226C
        /// <summary>
        /// Удалить выделенное. Строк может быть несколько (Ctrl/Shift), и тогда
        /// спрашивается один раз на все: подтверждение на каждый нуклид из
        /// тридцати — это не защита, а препятствие, его прощёлкивают не глядя.
        /// </summary>
        void button4_Click(object sender, EventArgs e)
        {
            List<NuclideDefinition> doomed = this.SelectedNuclides();
            if (doomed.Count == 0)
            {
                return;
            }

            string question = doomed.Count == 1
                ? string.Format(Resources.MSGDeleteNuclideDefinition, doomed[0].Name)
                : string.Format(CultureInfo.InvariantCulture, Resources.MSGDeleteNuclideDefinitions, doomed.Count,
                                string.Join(", ", doomed.Take(5).Select(n => n.Name)),
                                doomed.Count > 5 ? "…" : "");
            DialogResult dialogResult = MessageBox.Show(question, Resources.ConfirmationDialogTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
            if (dialogResult == DialogResult.OK)
            {
                if (!CommitDefinitions(this.manager, delegate
                    {
                        foreach (NuclideDefinition nuclide in doomed)
                        {
                            this.manager.NuclideDefinitions.Remove(nuclide);
                        }
                    }))
                {
                    // Файл не записан — удаления НЕ БЫЛО, и строки возвращены на
                    // место (`A9`). Окно об отказе показал менеджер; список надо
                    // перечитать, чтобы человек увидел то же, что лежит на диске.
                    this.ListupNuclideDefinitions();
                    return;
                }

                // (П318) Записано всё, что лежало в памяти, — снимок для «Нет» не нужен.
                this.ForgetUnsaved();
                this.activeNuclide = this.manager.NuclideDefinitions.Count > 0
                    ? this.manager.NuclideDefinitions[0]
                    : null;
                this.ListupNuclideDefinitions();
                this.UpdatePeakDetectionResult();
            }
        }

        /// <summary>
        /// Нуклиды выделенных строк, по одному на строку. SelectedItems при
        /// FullRowSelect отдаёт строку на каждую выделенную ячейку, так что
        /// повторы отсеиваются.
        /// </summary>
        List<NuclideDefinition> SelectedNuclides()
        {
            List<NuclideDefinition> list = new List<NuclideDefinition>();
            foreach (Row row in this.table1.SelectedItems)
            {
                NuclideDefinition nuclide = row.Tag as NuclideDefinition;
                if (nuclide != null && !list.Contains(nuclide))
                {
                    list.Add(nuclide);
                }
            }

            if (list.Count == 0 && this.activeNuclide != null)
            {
                list.Add(this.activeNuclide);
            }

            return list;
        }

        // Token: 0x060000C9 RID: 201 RVA: 0x00004110 File Offset: 0x00002310
        void EnableForm()
        {
            this.tabControl1.Enabled = true;
        }

        // Token: 0x060000CA RID: 202 RVA: 0x00004120 File Offset: 0x00002320
        void DisableForm()
        {
            this.tabControl1.Enabled = false;
        }

        void table1_EditingStopped(object sender, CellEditEventArgs e)
        {
            if (this.activeNuclide == null) return;
            // ⛔ (П275, 14.10.2026) ИЗМЕНЁННЫМ ОПРЕДЕЛЕНИЕ СЧИТАЕТСЯ ТОЛЬКО ПРИ ИЗМЕНЁННОМ
            // ЗНАЧЕНИИ. Прежде любое закрытие правки ячейки — Enter или уход фокуса без
            // единого символа — взводило «есть несохранённое», и при уходе на другую строку
            // или закрытии окна человек получал вопрос «Сохранить?» о правке, которой не
            // делал (П273, снимок `11_…`). Значение при этом не менялось (П266), менялся
            // только признак. Теперь значение ячейки сверяется с определением; совпало —
            // определение не трогается вовсе (и период вида 5.808599695585997E-06 не
            // проходит круг double → decimal → double, где теряет шестнадцатую цифру).
            bool changed;
            double number = 0.0;
            switch (e.CellPos.Column)
            {
                case 0:
                    changed = !string.Equals(this.activeNuclide.Name, e.Cell.Text, StringComparison.Ordinal);
                    break;
                case 1:
                    changed = NumberEdited(e.Cell.Data, this.activeNuclide.Energy, out number);
                    break;
                case 2:
                    changed = NumberEdited(e.Cell.Data, this.activeNuclide.HalfLife, out number);
                    break;
                default:
                    changed = false;
                    break;
            }
            if (!changed)
            {
                return;
            }
            // (П318) Снимок линии ДО правки — им откатывает ответ «Нет»
            // (`RollBackUnsaved`); правка ячейки пишет в определение сразу.
            this.RememberBeforeEdit(this.activeNuclide);
            switch (e.CellPos.Column)
            {
                case 0:
                    this.activeNuclide.Name = e.Cell.Text;
                    break;
                case 1:
                    this.activeNuclide.Energy = number;
                    break;
                case 2:
                    this.activeNuclide.HalfLife = number;
                    break;
            }
            this.NoteEdited(this.activeNuclide);
            this.SetActiveNuclideDirty();
            this.definitionsDirty = true;
            this.LoadFormContents(this.activeNuclide);
        }

        /// <summary>
        /// (П275) Отличается ли число, закрытое в ячейке, от числа определения — в той
        /// мере, в какой его видел редактор ячейки. Редактор (`NumberCellEditor`) берёт
        /// начальное значение как <c>Convert.ToDecimal(double)</c> и отдаёт
        /// <c>decimal</c>; то же преобразование числа определения и сравнивается.
        /// Статический и открытый пробе отражением: окна для проверки не нужно.
        /// </summary>
        static bool NumberEdited(object cellData, double current, out double edited)
        {
            decimal now = Convert.ToDecimal(cellData, CultureInfo.InvariantCulture);
            edited = (double)now;
            decimal before;
            try
            {
                before = Convert.ToDecimal(current, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return true;
            }
            return now != before;
        }

        // Token: 0x060000CB RID: 203 RVA: 0x00004130 File Offset: 0x00002330
        void table1_SelectionChanged(object sender, SelectionEventArgs e)
        {
            if (this.reenter)
            {
                return;
            }
            this.reenter = true;
            NuclideDefinition nuclideDefinition = null;
            Row row = null;
            if (this.table1.SelectedItems.Length > 0)
            {
                nuclideDefinition = (NuclideDefinition)this.table1.SelectedItems[0].Tag;
                row = this.table1.SelectedItems[0];
            }
            // ⛔ (`AMBER196`) «Другая строка» — другой ОБЪЕКТ. Прежде стояло
            // `CompareTo(...) > 0`, а `CompareTo` сравнивает ЭНЕРГИИ (так
            // сортируется список): уход на строку с меньшей энергией правку
            // затирал без вопроса. Замысел — не спрашивать на той же строке
            // (`d20d69fb`), и он сохранён.
            bool rebuilt = false;
            if (this.activeNuclide != null && nuclideDefinition != null
                && !ReferenceEquals(nuclideDefinition, this.activeNuclide))
            {
                bool wasDirty = this.activeNuclide.Dirty;
                if (!this.ConfirmSaveNuclide())
                {
                    this.ListupNuclideDefinitions();
                    this.reenter = false;
                    return;
                }
                if (wasDirty)
                {
                    // Вопрос перестроил список (при отказе от правок — ещё и
                    // перечитал файл, объекты новые): выбранную строку надо
                    // найти в нём заново и выделить её, а не прежнюю.
                    nuclideDefinition = this.FindListed(nuclideDefinition);
                    rebuilt = true;
                }
            }
            if (nuclideDefinition != null)
            {
                if (rebuilt)
                {
                    this.activeNuclide = nuclideDefinition;
                    this.ListupNuclideDefinitions();
                }
                // Выделение НЕ сводится обратно к одной ячейке: раньше здесь
                // стояли Selections.Clear() + AddCell, и Ctrl/Shift не работали
                // — таблица сама умеет несколько строк, а форма их гасила.
                // В полях правки по-прежнему один нуклид, первый выделенный:
                // редактировать два разом нечем.
                this.activeNuclide = nuclideDefinition;
                this.LoadFormContents(this.activeNuclide);
                this.button4.Enabled = true;
                this.EnableForm();
            }
            else
            {
                this.button4.Enabled = false;
                this.DisableForm();
            }
            this.reenter = false;
        }

        // Token: 0x060000CC RID: 204 RVA: 0x00004224 File Offset: 0x00002424
        bool ConfirmSaveNuclide()
        {
            if (this.activeNuclide != null && this.activeNuclide.Dirty)
            {
                DialogResult dialogResult = MessageBox.Show(Resources.MSGSavingNuclideDefinition, Resources.ConfirmationDialogTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
                if (dialogResult == DialogResult.Yes)
                {
                    if (!this.SaveNuclideDefinitions())
                    {
                        // Человек ответил «сохранить», а записи не вышло. Уводить
                        // его дальше нельзя: пометка «есть несохранённое» остаётся,
                        // сохранение можно повторить, а отказаться от правок —
                        // ответив «нет» на тот же вопрос.
                        return false;
                    }
                }
                else
                {
                    // (П318) «Нет» — линии возвращаются к снимку до правки, те же
                    // объекты; файл не читается (прежде — перечитывался, см.
                    // `ListupNuclideDefinitions`).
                    this.RollBackUnsaved();
                }
                this.ResetActiveNuclideDirty();
                this.ListupNuclideDefinitions();
            }
            return true;
        }

        void NuclideDefinitionForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Отказ записи держит форму открытой: закрыться, потеряв правки, о
            // которых человек только что сказал «сохранить», — это и есть тихая
            // потеря (`A9`).
            if (!this.ConfirmSaveNuclide())
            {
                e.Cancel = true;
            }
        }

        // Token: 0x060000CD RID: 205 RVA: 0x00004280 File Offset: 0x00002480
        void button6_Click(object sender, EventArgs e)
        {
            if (this.activeNuclide == null)
            {
                return;
            }
            if (!this.SaveNuclideDefinitions())
            {
                // Пометка «есть несохранённое» остаётся стоять: кнопка сохранения
                // не гаснет, и повторить можно, освободив файл.
                return;
            }
            this.ResetActiveNuclideDirty();
            this.ListupNuclideDefinitions();
        }

        // Token: 0x060000CE RID: 206 RVA: 0x000042A0 File Offset: 0x000024A0
        /// <summary>
        /// ⛔ `A9`: <c>bool</c> от <c>SaveDefinitionFile</c> здесь ВЫБРАСЫВАЛСЯ, и
        /// отказ записи ничем не отличался от успеха — вызывающие снимали пометку
        /// «есть несохранённое» и закрывали форму. Человек видел окно об ошибке, а
        /// программа тут же объявляла правки сохранёнными: кнопка сохранения
        /// гасла, при закрытии больше не спрашивали, и правки уходили молча.
        ///
        /// ⚠ Список НЕ откатывается, и это не забывчивость: правки лежат в полях
        /// формы, откат стёр бы набранное человеком. Расхождение с диском здесь
        /// названо обычным способом — пометкой «есть несохранённое», которая
        /// остаётся стоять, плюс окно менеджера об отказе.
        ///
        /// (П318) Файл больше не перечитывается вовсе (см.
        /// <c>ListupNuclideDefinitions</c>), и снимок до правки
        /// (<see cref="RememberBeforeEdit"/>) снимается только УДАЧНОЙ записью:
        /// после отказа ответ «Нет» на следующий вопрос откатывает и поля формы,
        /// уже перенесённые в линию <c>SaveFormContents</c>. Прежде
        /// <c>definitionsDirty</c> гасился до записи, и после отказа «Нет» не
        /// откатывал ничего — перенесённое оставалось в памяти и уходило в файл
        /// при следующей записи библиотеки.
        /// </summary>
        bool SaveNuclideDefinitions()
        {
            this.RememberBeforeEdit(this.activeNuclide);
            bool moved = this.SaveFormContents(this.activeNuclide);
            // (П326) Перенесённое из полей — правка этого окна, даже частичная
            // (отказ разбора числа на середине): откат и запись «без правок окна»
            // обязаны знать, что именно окно положило в линию.
            this.NoteEdited(this.activeNuclide);
            if (!moved)
            {
                MessageBox.Show(Resources.ERRInvalidInputForm);
                return false;
            }
            if (!this.manager.SaveDefinitionFile())
            {
                return false;
            }
            this.ForgetUnsaved();
            this.UpdatePeakDetectionResult();
            return true;
        }

        /// <summary>
        /// (П318) Запомнить линию такой, какой она была ДО первой несохранённой
        /// правки этого окна. Правки ячеек таблицы и галка «Видимый» пишут в
        /// определение сразу, поля формы — при сохранении; ответ «Нет» на
        /// «Сохранить?» возвращает линии к этим снимкам (<see cref="RollBackUnsaved"/>).
        /// Повторный вызов для той же линии снимок не трогает — он «до правки».
        ///
        /// ⚠ Почему снимок, а не перечитывание файла: объекты менеджера держат
        /// по ссылке документы (выбор набора), панель поиска пиков, подписи
        /// пиков и открытый редактор наборов — перечитывание подменяло их все.
        /// </summary>
        void RememberBeforeEdit(NuclideDefinition nuclide)
        {
            if (nuclide == null)
            {
                return;
            }
            foreach (NuclideState state in this.unsaved)
            {
                if (ReferenceEquals(state.Definition, nuclide))
                {
                    return;
                }
            }
            this.unsaved.Add(NuclideState.Of(nuclide));
            this.definitionsDirty = true;
        }

        /// <summary>
        /// (П326) Запомнить линию такой, какой её оставила последняя правка ЭТОГО
        /// окна. Зовётся после каждой записи окна в линию (ячейка таблицы, галка
        /// «Видимый», перенос полей). Нужна затем, что линию правит не только это
        /// окно: редактор наборов красит линии своего набора («Назначить цвет»), и
        /// откат по одному снимку «до правки» вернул бы и чужую покраску.
        /// </summary>
        void NoteEdited(NuclideDefinition nuclide)
        {
            foreach (NuclideState state in this.unsaved)
            {
                if (ReferenceEquals(state.Definition, nuclide))
                {
                    state.After = LineValues.Of(nuclide);
                    return;
                }
            }
        }

        /// <summary>(П318) Ответ «Нет»: линии — как до правок этого окна; объекты те же.</summary>
        void RollBackUnsaved()
        {
            if (!this.definitionsDirty)
            {
                return;
            }
            foreach (NuclideState state in this.unsaved)
            {
                state.Restore();
            }
            this.ForgetUnsaved();
        }

        /// <summary>(П318) Файл записан — в памяти то же, что на диске; откатывать нечего.</summary>
        void ForgetUnsaved()
        {
            this.unsaved.Clear();
            this.definitionsDirty = false;
        }

        /// <summary>
        /// (П326) Есть ли в окне несохранённое — то, о чём оно само спросит
        /// «Сохранить?» при уходе со строки или закрытии: правки линий в памяти
        /// (ячейки таблицы, галка «Видимый») или правки полей справа, ещё не
        /// перенесённые в линию.
        /// </summary>
        internal bool HasUnsavedEdits
        {
            get
            {
                return this.definitionsDirty || (this.activeNuclide != null && this.activeNuclide.Dirty);
            }
        }

        /// <summary>
        /// (П326) Открытые окна редактора нуклидов с несохранённым
        /// (<see cref="HasUnsavedEdits"/>). Библиотека и наборы лежат в ОДНОМ
        /// файле (<see cref="NuclideDefinitionManager.SaveDefinitionFile"/> пишет
        /// всё, что в памяти), поэтому любой, кто пишет библиотеку, обязан знать
        /// об этих окнах: иначе их правки уходят в файл молча.
        /// </summary>
        internal static List<NuclideDefinitionForm> OpenWithUnsavedEdits()
        {
            List<NuclideDefinitionForm> list = new List<NuclideDefinitionForm>();
            foreach (Form form in Application.OpenForms)
            {
                NuclideDefinitionForm editor = form as NuclideDefinitionForm;
                if (editor != null && !editor.IsDisposed && editor.HasUnsavedEdits)
                {
                    list.Add(editor);
                }
            }
            return list;
        }

        /// <summary>
        /// ⛔ (П341, 19.10.2026, `A338`) Библиотеку изменил ДРУГОЙ — открытые окна
        /// редактора нуклидов показывают её заново (<see cref="ShowLibraryChange"/>).
        /// Зовут импорт из NucBase после удачной записи файла и редактор наборов —
        /// после «Присвоить» цвета и после отката своих правок («Нет» при закрытии).
        /// </summary>
        internal static void ShowLibraryChangeInOpen()
        {
            foreach (Form form in Application.OpenForms)
            {
                NuclideDefinitionForm editor = form as NuclideDefinitionForm;
                if (editor != null && !editor.IsDisposed)
                {
                    editor.ShowLibraryChange();
                }
            }
        }

        /// <summary>
        /// ⛔ (П341, 19.10.2026, `A338`) ПОКАЗАТЬ БИБЛИОТЕКУ, ИЗМЕНЁННУЮ ВНЕ ОКНА.
        /// Таблица окна строится только <see cref="ListupNuclideDefinitions"/>, а
        /// зовёт его лишь само окно. Импорт из NucBase при открытом окне дописывал
        /// линии в библиотеку и в файл, а окно показывало прежний список — замер
        /// П341 на копии конфига Amber: импорт 133BA (пять гамма-линий), «Добавлено
        /// - 5», в файле 47 линий, в окне 42, ни одной Ba133. Человек, ушедший на
        /// строку, мог удалить или переписать линии, которых в окне не видел.
        ///
        /// Два места, оба — без вопроса и без пометки «изменено»:
        ///  * таблица строится заново; обработчик выделения при этом НЕ работает
        ///    (<see cref="reenter"/>): иначе перестроение звало бы
        ///    <see cref="LoadFormContents"/> и молча стирало набранное в полях
        ///    справа — несохранённое, которое импорт по ответу «Нет» оставляет
        ///    окну (П328);
        ///  * поля справа: линия без несохранённого показывается заново целиком
        ///    (импорт с «перезаписать» мог сменить её имя, выход, период, ряд, и
        ///    следующее «Сохранить» вернуло бы в линию прежние значения полей);
        ///    у линии с несохранённым заменяются только поля, которых человек не
        ///    трогал, — набранное остаётся за ним.
        /// </summary>
        internal void ShowLibraryChange()
        {
            bool wasReenter = this.reenter;
            this.reenter = true;
            try
            {
                this.ListupNuclideDefinitions();
            }
            finally
            {
                this.reenter = wasReenter;
            }

            if (this.activeNuclide == null)
            {
                return;
            }

            if (!this.activeNuclide.Dirty)
            {
                this.LoadFormContents(this.activeNuclide);
                return;
            }

            this.ReloadUntouchedFields(this.activeNuclide);
        }

        /// <summary>
        /// (П341, `A338`) Поля справа, которых человек не трогал (текст совпадает с
        /// показанным загрузкой, <see cref="shownValues"/>), получают значения линии;
        /// тронутые остаются как есть. Пометку «изменено» не трогает.
        /// </summary>
        void ReloadUntouchedFields(NuclideDefinition nuclide)
        {
            LineValues shown = this.shownValues;
            if (shown == null)
            {
                return;
            }

            LineValues now = LineValues.Of(nuclide);
            this.contentsLoading = true;
            try
            {
                if (this.textBox1.Text == (shown.Name ?? ""))
                {
                    this.textBox1.Text = now.Name;
                }
                if (this.chainTextBox.Text == (shown.Chain ?? ""))
                {
                    this.chainTextBox.Text = now.Chain;
                }
                if (this.doubleTextBox1.Text == shown.Energy.ToString(CultureInfo.InvariantCulture))
                {
                    this.doubleTextBox1.Text = now.Energy.ToString(CultureInfo.InvariantCulture);
                }
                if (this.doubleTextBox2.Text == shown.HalfLife.ToString(CultureInfo.InvariantCulture))
                {
                    this.doubleTextBox2.Text = now.HalfLife.ToString(CultureInfo.InvariantCulture);
                }
                if (this.intensityTextBox.Text == shown.Intensity.ToString(CultureInfo.InvariantCulture))
                {
                    this.intensityTextBox.Text = now.Intensity.ToString(CultureInfo.InvariantCulture);
                }
                string shownNote = shown.Note == null ? "" : (string)shown.Note;
                if (this.textBox2.Text == shownNote)
                {
                    this.textBox2.Text = now.Note == null ? "" : (string)now.Note;
                }
                if (shown.HasColor && now.HasColor && this.colorComboBox1.SelectedColor == shown.Color)
                {
                    this.colorComboBox1.SelectedColor = now.Color;
                    this.colorComboBox1.Refresh();
                }
                if (this.checkBox1.Checked == shown.Visible)
                {
                    this.checkBox1.Checked = now.Visible;
                }
            }
            finally
            {
                this.contentsLoading = false;
            }

            this.shownValues = now;
        }

        /// <summary>
        /// (П326) Ответ «Да» на вопрос редактора наборов: записать правки окна в
        /// файл вместе с остальным — ровно как кнопка «Сохранить» этого окна
        /// (поля переносятся в линию, файл пишется, окно становится «сохранено»).
        /// Отказ (нечисло в поле, файл не записан) — false, пометка остаётся.
        /// </summary>
        internal bool SaveTogether()
        {
            if (this.activeNuclide != null && this.activeNuclide.Dirty)
            {
                if (!this.SaveNuclideDefinitions())
                {
                    return false;
                }
                this.ResetActiveNuclideDirty();
                this.ListupNuclideDefinitions();
                return true;
            }
            if (!this.manager.SaveDefinitionFile())
            {
                return false;
            }
            this.ForgetUnsaved();
            return true;
        }

        /// <summary>
        /// (П326) Ответ «Нет» на вопрос редактора наборов: записать файл так, будто
        /// несохранённых правок ЭТОГО окна нет. На время записи линии получают
        /// значения до правок окна (<see cref="NuclideState.WithoutEdits"/>), после —
        /// ровно прежние, с правками; пометка окна не трогается. Правки полей, не
        /// перенесённые в линию, в файл не попадают и так.
        ///
        /// Разделить честно можно потому, что заведение и удаление линий окно
        /// пишет в файл СРАЗУ (<see cref="CommitDefinitions"/>): несохранёнными
        /// бывают только поля существующих линий, и у каждой есть снимок.
        /// </summary>
        internal bool WriteWithoutUnsavedEdits(Func<bool> write)
        {
            List<KeyValuePair<NuclideState, LineValues>> current = new List<KeyValuePair<NuclideState, LineValues>>();
            foreach (NuclideState state in this.unsaved)
            {
                current.Add(new KeyValuePair<NuclideState, LineValues>(state, LineValues.Of(state.Definition)));
            }
            try
            {
                foreach (KeyValuePair<NuclideState, LineValues> pair in current)
                {
                    pair.Key.WithoutEdits().Put(pair.Key.Definition);
                }
                return write();
            }
            finally
            {
                foreach (KeyValuePair<NuclideState, LineValues> pair in current)
                {
                    pair.Value.Put(pair.Key.Definition);
                }
            }
        }

        /// <summary>
        /// (П318) Всё, что окно может записать в линию: правки таблицы (имя,
        /// энергия, период), галка «Видимый» и поля формы (<c>SaveFormContents</c>).
        /// Заметка — ссылка: форма её не правит, а заменяет новым объектом.
        /// </summary>
        sealed class LineValues
        {
            public string Name;
            public string Chain;
            public double Energy;
            public double HalfLife;
            public double Intensity;
            public CDATA Note;
            public bool HasColor;
            public System.Drawing.Color Color;
            public bool Visible;

            public static LineValues Of(NuclideDefinition d)
            {
                return new LineValues
                {
                    Name = d.Name,
                    Chain = d.Chain,
                    Energy = d.Energy,
                    HalfLife = d.HalfLife,
                    Intensity = d.Intencity,
                    Note = d.Note,
                    HasColor = d.NuclideColor != null,
                    Color = d.NuclideColor != null ? d.NuclideColor.Color : System.Drawing.Color.Empty,
                    Visible = d.Visible,
                };
            }

            /// <summary>Положить значения в линию. Пометку <c>Dirty</c> не трогает.</summary>
            public void Put(NuclideDefinition d)
            {
                d.Name = this.Name;
                d.Chain = this.Chain;
                d.Energy = this.Energy;
                d.HalfLife = this.HalfLife;
                d.Intencity = this.Intensity;
                d.Note = this.Note;
                if (this.HasColor && d.NuclideColor != null)
                {
                    d.NuclideColor.Color = this.Color;
                }
                d.Visible = this.Visible;
            }
        }

        /// <summary>
        /// (П318) Линия до первой несохранённой правки окна (<see cref="Before"/>)
        /// и (П326) такая, какой окно её оставило (<see cref="After"/>).
        /// </summary>
        sealed class NuclideState
        {
            public NuclideDefinition Definition;
            public LineValues Before;
            public LineValues After;

            public static NuclideState Of(NuclideDefinition d)
            {
                return new NuclideState
                {
                    Definition = d,
                    Before = LineValues.Of(d),
                    After = LineValues.Of(d),
                };
            }

            /// <summary>
            /// (П326) Линия без правок ЭТОГО окна. Поле, которое держит то, что
            /// окно туда положило, получает значение до правки; поле, которое с
            /// тех пор сменил кто-то другой, остаётся как есть. Другой здесь —
            /// редактор наборов: он красит линии («Назначить цвет»), и прежний
            /// откат по одному снимку «до» возвращал линии цвет до покраски —
            /// в памяти одно, в файле, записанном наборами, другое.
            /// </summary>
            public LineValues WithoutEdits()
            {
                LineValues now = LineValues.Of(this.Definition);
                LineValues v = LineValues.Of(this.Definition);
                if (string.Equals(now.Name, this.After.Name, StringComparison.Ordinal)) v.Name = this.Before.Name;
                if (string.Equals(now.Chain, this.After.Chain, StringComparison.Ordinal)) v.Chain = this.Before.Chain;
                if (now.Energy.Equals(this.After.Energy)) v.Energy = this.Before.Energy;
                if (now.HalfLife.Equals(this.After.HalfLife)) v.HalfLife = this.Before.HalfLife;
                if (now.Intensity.Equals(this.After.Intensity)) v.Intensity = this.Before.Intensity;
                if (ReferenceEquals(now.Note, this.After.Note)) v.Note = this.Before.Note;
                if (now.HasColor && this.After.HasColor && this.Before.HasColor && now.Color == this.After.Color)
                {
                    v.Color = this.Before.Color;
                }
                if (now.Visible == this.After.Visible) v.Visible = this.Before.Visible;
                return v;
            }

            public void Restore()
            {
                this.WithoutEdits().Put(this.Definition);
                this.Definition.Dirty = false;
            }
        }

        // Token: 0x060000CF RID: 207 RVA: 0x000042D4 File Offset: 0x000024D4
        void UpdatePeakDetectionResult()
        {
            // (П326) Без хозяина-MainForm (окно, поднятое пробой) пересчитывать
            // нечего; прежде здесь было приведение `(MainForm)base.Owner` и NRE.
            MainForm mainForm = base.Owner as MainForm;
            if (mainForm == null)
            {
                return;
            }
            mainForm.UpdateDetectedPeakView();
            if (mainForm.ActiveDocument != null)
            {
                mainForm.ActiveDocument.UpdateEnergySpectrum();
            }
        }

        // Token: 0x060000D0 RID: 208 RVA: 0x00004310 File Offset: 0x00002510
        void button5_Click(object sender, EventArgs e)
        {
            if (!this.ConfirmSaveNuclide())
            {
                return;
            }
            base.Close();
        }

        // Token: 0x060000D1 RID: 209 RVA: 0x00004324 File Offset: 0x00002524
        void SetActiveNuclideDirty()
        {
            if (this.contentsLoading)
            {
                return;
            }
            if (this.activeNuclide == null)
            {
                return;
            }
            this.activeNuclide.Dirty = true;
            this.button6.Enabled = true;
        }

        // Token: 0x060000D2 RID: 210 RVA: 0x00004358 File Offset: 0x00002558
        void ResetActiveNuclideDirty()
        {
            if (this.activeNuclide == null)
            {
                return;
            }
            this.activeNuclide.Dirty = false;
            this.button6.Enabled = false;
        }

        void colorComboBox1_ColorChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        // Token: 0x060000D3 RID: 211 RVA: 0x00004380 File Offset: 0x00002580
        void textBox1_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        void chainTextBox_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        // Token: 0x060000D4 RID: 212 RVA: 0x00004388 File Offset: 0x00002588
        void doubleTextBox1_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        void intensityTextBox_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        // Token: 0x060000D5 RID: 213 RVA: 0x00004390 File Offset: 0x00002590
        void doubleTextBox2_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        // Token: 0x060000D6 RID: 214 RVA: 0x00004398 File Offset: 0x00002598
        void textBox2_TextChanged(object sender, EventArgs e)
        {
            this.SetActiveNuclideDirty();
        }

        // Token: 0x04000038 RID: 56
        NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();

        // Token: 0x04000039 RID: 57
        NuclideDefinition activeNuclide;

        // Token: 0x0400003A RID: 58
        bool contentsLoading;

        // Token: 0x0400003B RID: 59
        bool reenter;

        /// <summary>
        /// В памяти есть правки линий, не записанные в файл (правка ячейки,
        /// галка «Видимый»). Держится вместе с <see cref="unsaved"/>; до П318
        /// по нему перечитывался файл.
        /// </summary>
        bool definitionsDirty = false;

        /// <summary>(П318) Снимки линий до несохранённых правок — откат «Нет».</summary>
        readonly List<NuclideState> unsaved = new List<NuclideState>();

        /// <summary>
        /// (П341, `A338`) Значения линии, поставленные в поля справа последней
        /// загрузкой (<see cref="LoadFormContents"/>) или
        /// <see cref="ReloadUntouchedFields"/>.
        /// </summary>
        LineValues shownValues;
    }
}
